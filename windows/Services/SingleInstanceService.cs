using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Lexi.Social;

namespace Lexi;

public sealed class SingleInstanceService : IDisposable
{
    private static readonly string Scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Environment.UserDomainName + "|" + Environment.UserName + "|" +
        System.Diagnostics.Process.GetCurrentProcess().SessionId + "|" +
        Path.GetFullPath(VocabularyService.GetDefaultDatabasePath()).ToUpperInvariant())))[..24];
    private static readonly string MutexName = @"Local\LexiNative_" + Scope;
    private static readonly string PipeName = "LexiNative_" + Scope;
    private Mutex? _mutex;
    private readonly CancellationTokenSource _stop = new();
    private bool _owns;
    private Action? _wakeup;
    private bool _pending;
    private Action<Uri>? _protocolCallback;
    private Uri? _pendingProtocolCallback;
    private readonly object _gate = new();
    public bool IsPrimary => _owns;
    public event Action WakeupRequested
    {
        add { bool pending; lock (_gate) { _wakeup += value; pending = _pending; _pending = false; } if (pending) value(); }
        remove { lock (_gate) _wakeup -= value; }
    }
    public event Action<Uri> ProtocolCallbackRequested
    {
        add
        {
            Uri? pending;
            lock (_gate)
            {
                _protocolCallback += value;
                pending = _pendingProtocolCallback;
                _pendingProtocolCallback = null;
            }
            if (pending != null) value(pending);
        }
        remove { lock (_gate) _protocolCallback -= value; }
    }
    public bool CheckAndAcquire(bool wakeExisting = true, string? activationArgument = null)
    {
        _mutex = new Mutex(false, MutexName);
        try { _owns = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _owns = true; }
        if (!_owns)
        {
            if (!wakeExisting) return false;
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(5000);
            WriteActivation(client, activationArgument);
            client.Flush();
            return false;
        }
        if (WindowsProtocolRegistration.IsCallbackUri(activationArgument))
        {
            var callback = new Uri(activationArgument!, UriKind.Absolute);
            lock (_gate) _pendingProtocolCallback = callback;
        }
        _ = ListenAsync(_stop.Token);
        return true;
    }
    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(2000);
                var buffer = new byte[4];
                await server.ReadExactlyAsync(buffer, timeout.Token);
                if (Encoding.UTF8.GetString(buffer) == "SHOW")
                {
                    RaiseWakeup();
                    continue;
                }
                var length = BinaryPrimitives.ReadInt32LittleEndian(buffer);
                if (length is <= 0 or > 4096) continue;
                var payload = new byte[length];
                await server.ReadExactlyAsync(payload, timeout.Token);
                var candidate = Encoding.UTF8.GetString(payload);
                if (!WindowsProtocolRegistration.IsCallbackUri(candidate)) continue;
                RaiseProtocolCallback(new Uri(candidate, UriKind.Absolute));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception) { try { await Task.Delay(150, token); } catch (OperationCanceledException) { break; } }
        }
    }
    private static void WriteActivation(NamedPipeClientStream client, string? activationArgument)
    {
        if (WindowsProtocolRegistration.IsCallbackUri(activationArgument))
        {
            var payload = Encoding.UTF8.GetBytes(activationArgument!);
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            client.Write(header);
            client.Write(payload);
            return;
        }
        client.Write("SHOW"u8);
    }
    private void RaiseWakeup()
    {
        Action? action;
        lock (_gate) { action = _wakeup; if (action == null) _pending = true; }
        action?.Invoke();
    }
    private void RaiseProtocolCallback(Uri callback)
    {
        Action<Uri>? action;
        lock (_gate) { action = _protocolCallback; if (action == null) _pendingProtocolCallback = callback; }
        action?.Invoke(callback);
        RaiseWakeup();
    }
    public void Dispose()
    {
        _stop.Cancel();
        if (_owns) { _mutex?.ReleaseMutex(); _owns = false; }
        _mutex?.Dispose();
        _mutex = null;
    }
}
