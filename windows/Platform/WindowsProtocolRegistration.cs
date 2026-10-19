using System.Runtime.InteropServices;
using System.Text;

namespace Lexi.Social;

/// <summary>
/// Registers this app as the per-user handler for its own URI scheme, so the system browser can
/// hand the GitHub sign-in callback back to a running Lexi. The key lives under HKCU only, which
/// needs no elevation and is removed by the uninstaller. It is used only when the default loopback
/// listener cannot be started; the same Supabase project then has to allow-list the scheme.
/// </summary>
public static class WindowsProtocolRegistration
{
    public const string Scheme = "com.lexi.windows";
    public const string CallbackHost = "auth";
    public const string CallbackPath = "/callback";
    public const string CallbackUri = SocialOAuthFlow.ProtocolCallbackBase;

    private const string KeyPath = @"Software\Classes\" + Scheme;
    private const string Description = "URL:Lexi GitHub sign-in";
    private static readonly IntPtr HKeyCurrentUser = new(unchecked((int)0x80000001));
    private const int KeyWrite = 0x20006;
    private const int RegSz = 1;

    /// <summary>True when the supplied argument is this app's own OAuth callback URI.</summary>
    public static bool IsCallbackUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        // The same fail-closed shape SocialOAuthFlow.TryConsumeCallback enforces: a callback the
        // consumer will refuse (userinfo, fragment, explicit port) must not be treated as ours and
        // forwarded to the single-instance pipe.
        if (uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 || uri.Port != -1) return false;
        return string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.Host, CallbackHost, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalisePath(uri.AbsolutePath), CallbackPath, StringComparison.Ordinal);
    }

    /// <summary>Makes sure the scheme points at this executable. Returns false when the machine
    /// forbids the write, in which case the caller cannot use a protocol callback.</summary>
    public static bool TryEnsureCallback(out Uri callback, out string message)
    {
        callback = new Uri(CallbackUri, UriKind.Absolute);
        message = "";
        if (!OperatingSystem.IsWindows()) { message = "Not Windows."; return false; }
        // Automated runs and restricted machines never touch the registry.
        if (Environment.GetEnvironmentVariable("LEXI_DISABLE_PROTOCOL_REGISTRATION") == "1")
        { message = "Protocol registration is disabled for this run."; return false; }
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            { message = "The running executable path is unavailable."; return false; }
            var error = WriteKey(KeyPath, null, Description, executable);
            error ??= WriteKey(KeyPath, "URL Protocol", "", null);
            error ??= WriteKey(KeyPath + @"\DefaultIcon", null, "\"" + executable + "\",0", null);
            error ??= WriteKey(KeyPath + @"\shell\open\command", null, "\"" + executable + "\" \"%1\"", null);
            if (error is not null) { message = error; return false; }
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            message = error.GetType().Name;
            return false;
        }
    }

    /// <summary>Removes the per-user handler. Used by tests and by the uninstaller.</summary>
    public static void Unregister()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { _ = RegDeleteTreeW(HKeyCurrentUser, KeyPath); }
        catch (DllNotFoundException) { }
    }

    private static string? WriteKey(string path, string? name, string value, string? iconSource)
    {
        if (RegCreateKeyExW(HKeyCurrentUser, path, 0, null, 0, KeyWrite, IntPtr.Zero, out var key, out _) != 0)
            return "The per-user protocol key could not be created.";
        try
        {
            var bytes = Encoding.Unicode.GetBytes(value + "\0");
            return RegSetValueExW(key, name, 0, RegSz, bytes, bytes.Length) == 0
                ? null
                : "The per-user protocol value could not be written.";
        }
        finally { _ = RegCloseKey(key); }
    }

    private static string NormalisePath(string path) =>
        path.Length > 1 && path.EndsWith('/') ? path[..^1] : path;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegCreateKeyExW(IntPtr key, string subKey, int reserved, string? className,
        int options, int desired, IntPtr attributes, out IntPtr result, out int disposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegSetValueExW(IntPtr key, string? name, int reserved, int type, byte[] data, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr key);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegDeleteTreeW(IntPtr key, string subKey);
}
