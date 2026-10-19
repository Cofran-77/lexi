using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Lexi.Social;

/// <summary>
/// Windows loopback OAuth callback adapter. The listener binds only to 127.0.0.1 and never
/// returns authorization codes, state values, or other callback parameters in its response.
/// </summary>
public sealed class WindowsOAuthBrowser : IDisposable
{
    public const string CallbackPath = "/auth/callback/";
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public Uri CallbackBase { get; }

    public WindowsOAuthBrowser()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows OAuth browser is required.");
        _listener = StartLoopbackListener(out var callbackBase);
        CallbackBase = callbackBase;
    }

    /// <summary>Opens the already validated Supabase authorization URL in the user's default browser.</summary>
    public static void Open(SocialOAuthStart start, SocialCloudOptions options)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(options);
        var uri = start.AuthorizationUri;
        if (uri.Scheme != Uri.UriSchemeHttps ||
            uri.GetLeftPart(UriPartial.Authority) != options.ProjectUrl.GetLeftPart(UriPartial.Authority) ||
            uri.AbsolutePath != "/auth/v1/authorize" || uri.Fragment.Length > 0)
            throw new ArgumentException("Unexpected OAuth destination.", nameof(start));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows OAuth browser is required.");

        var process = new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        };
        using var opened = Process.Start(process)
            ?? throw new InvalidOperationException("The system browser could not be opened.");
    }

    /// <summary>Waits for a callback, accepting only the active flow's state and callback origin.</summary>
    public async Task<SocialOAuthExchange> WaitForCallbackAsync(SocialOAuthFlow flow,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(15));

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_disposed)
            {
                throw new OperationCanceledException("OAuth callback listener was closed.", cancellation);
            }
            catch (HttpListenerException) when (_disposed)
            {
                throw new OperationCanceledException("OAuth callback listener was closed.", cancellation);
            }

            try
            {
                if (flow.TryConsumeCallback(context.Request.Url!, DateTimeOffset.UtcNow,
                    out var exchange, out var publicError) && exchange is not null)
                {
                    await WriteResponseAsync(context.Response, HttpStatusCode.OK,
                        "Login complete. You can close this window.", linked.Token).ConfigureAwait(false);
                    return exchange;
                }
                if (!string.IsNullOrEmpty(publicError))
                    throw new InvalidOperationException(publicError);
                await WriteResponseAsync(context.Response, HttpStatusCode.BadRequest,
                    "This login callback is invalid. Return to Lexi and try again.", linked.Token).ConfigureAwait(false);
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    private static HttpListener StartLoopbackListener(out Uri callbackBase)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var port = ReserveLoopbackPort();
            var prefix = $"http://127.0.0.1:{port}{CallbackPath}";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
                callbackBase = new Uri(prefix, UriKind.Absolute);
                return listener;
            }
            catch (Exception error) when (error is HttpListenerException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                listener.Close();
                lastError = error;
            }
        }
        throw new InvalidOperationException("A local OAuth callback listener could not be started.", lastError);
    }

    private static int ReserveLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, HttpStatusCode status,
        string message, CancellationToken cancellation)
    {
        response.StatusCode = (int)status;
        response.ContentType = "text/plain; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Pragma"] = "no-cache";
        var body = Encoding.UTF8.GetBytes(message);
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body, cancellation).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _listener.Close();
        _lifetime.Dispose();
    }
}
