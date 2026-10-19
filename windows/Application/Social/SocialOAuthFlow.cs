using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Lexi.Social;

public sealed record SocialOAuthStart(Uri AuthorizationUri);

// Deliberately not a positional record: auto-generated ToString would expose credentials.
public sealed class SocialOAuthExchange
{
    public string Code { get; }
    public string Verifier { get; }
    internal SocialOAuthExchange(string code, string verifier) { Code = code; Verifier = verifier; }
    public override string ToString() => "SocialOAuthExchange([redacted])";
}

/// <summary>Single in-memory PKCE attempt. Process restart/cancel invalidates the attempt.</summary>
public sealed class SocialOAuthFlow
{
    public const string CallbackBase = "http://127.0.0.1/auth/callback/";
    public const string ProtocolCallbackBase = "com.lexi.windows://auth/callback";
    private readonly object _gate = new();
    private string? _verifier;
    private string? _state;
    private Uri? _callbackBase;
    private DateTimeOffset _expiresAt;

    public SocialOAuthStart Begin(Uri projectUrl, DateTimeOffset now) =>
        Begin(projectUrl, now, new Uri(CallbackBase, UriKind.Absolute));

    public SocialOAuthStart Begin(Uri projectUrl, DateTimeOffset now, Uri callbackBase)
    {
        ValidateProjectUrl(projectUrl);
        ValidateCallbackBase(callbackBase);
        lock (_gate)
        {
            if (_verifier is not null && now < _expiresAt)
                throw new InvalidOperationException("A GitHub login is already pending.");
            _verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            _state = Base64Url(RandomNumberGenerator.GetBytes(32));
            _callbackBase = callbackBase;
            _expiresAt = now.AddMinutes(10);
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(_verifier)));
            var redirect = BuildCallback(callbackBase, _state);
            var uri = new Uri(projectUrl, "/auth/v1/authorize?provider=github&scopes=user%3Aemail&redirect_to=" +
                Uri.EscapeDataString(redirect) + "&code_challenge=" + challenge + "&code_challenge_method=S256");
            return new(uri);
        }
    }

    public static void ValidateProjectUrl(Uri projectUrl)
    {
        ArgumentNullException.ThrowIfNull(projectUrl);
        if (!projectUrl.IsAbsoluteUri || projectUrl.Scheme != Uri.UriSchemeHttps || projectUrl.UserInfo.Length > 0 ||
            !projectUrl.IsDefaultPort || projectUrl.AbsolutePath != "/" || projectUrl.Query.Length > 0 ||
            projectUrl.Fragment.Length > 0 || !projectUrl.Host.EndsWith(".supabase.co", StringComparison.Ordinal) ||
            projectUrl.Host[..^".supabase.co".Length].Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("Expected an HTTPS Supabase project origin.", nameof(projectUrl));
    }

    private static void ValidateCallbackBase(Uri callbackBase)
    {
        ArgumentNullException.ThrowIfNull(callbackBase);
        if (!callbackBase.IsAbsoluteUri || callbackBase.UserInfo.Length > 0 ||
            callbackBase.Query.Length > 0 || callbackBase.Fragment.Length > 0)
            throw new ArgumentException("Expected a loopback OAuth callback origin.", nameof(callbackBase));
        if (callbackBase.Scheme == Uri.UriSchemeHttp)
        {
            if (callbackBase.Host != IPAddress.Loopback.ToString() ||
                callbackBase.AbsolutePath != "/auth/callback/" || callbackBase.Port is < 1 or > 65535)
                throw new ArgumentException("Expected a loopback OAuth callback origin.", nameof(callbackBase));
            return;
        }
        if (callbackBase.Scheme != "com.lexi.windows" ||
            !string.Equals(callbackBase.Host, "auth", StringComparison.OrdinalIgnoreCase) ||
            NormalisePath(callbackBase.AbsolutePath) != "/callback" || !callbackBase.IsDefaultPort)
            throw new ArgumentException("Expected the Lexi Windows protocol callback.", nameof(callbackBase));
    }

    public bool TryConsumeCallback(Uri callback, DateTimeOffset now,
        out SocialOAuthExchange? exchange, out string? publicError)
    {
        exchange = null; publicError = null;
        if (callback is null || !callback.IsAbsoluteUri || callback.UserInfo.Length > 0 ||
            callback.Fragment.Length > 0 || callback.Query.Length > 4096) return false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in callback.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            if (pair.Length != 2) return false;
            try
            {
                if (!values.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1]))) return false;
            }
            catch (UriFormatException) { return false; }
        }
        lock (_gate)
        {
            var verifier = _verifier;
            var state = _state;
            var callbackBase = _callbackBase;
            if (verifier is null || state is null || callbackBase is null) return false;
            if (!SameCallbackOriginAndPath(callback, callbackBase)) return false;
            if (now >= _expiresAt) { Clear(); publicError = "登录等待已过期，请重新登录。"; return false; }
            // Supabase reserves OAuth's state parameter for its own server-side flow state.
            // Keep the one-time desktop nonce in flow_id, as the macOS client does.
            if (values.ContainsKey("state") || !values.TryGetValue("flow_id", out var candidate) ||
                string.IsNullOrWhiteSpace(candidate) || !FixedTimeEquals(candidate, state))
                return false;
            if (values.ContainsKey("error"))
            {
                Clear(); publicError = "GitHub 登录未完成，可以再试一次。"; return false;
            }
            if (!values.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code) || code.Length > 1024 ||
                code.Any(char.IsControl)) return false;
            exchange = new(code, verifier);
            Clear();
            return true;
        }
    }

    public void Cancel() { lock (_gate) Clear(); }

    private void Clear() { _verifier = null; _state = null; _callbackBase = null; _expiresAt = default; }
    private static bool SameCallbackOriginAndPath(Uri callback, Uri callbackBase) =>
        string.Equals(callback.Scheme, callbackBase.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(callback.Host, callbackBase.Host, StringComparison.OrdinalIgnoreCase) &&
        callback.Port == callbackBase.Port &&
        string.Equals(callback.AbsolutePath, callbackBase.AbsolutePath, StringComparison.Ordinal);
    private static bool FixedTimeEquals(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    private static string BuildCallback(Uri callbackBase, string state)
    {
        var encoded = Uri.EscapeDataString(state);
        var builder = new UriBuilder(callbackBase) { Query = "flow_id=" + encoded };
        return builder.Uri.AbsoluteUri;
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string NormalisePath(string path) =>
        path.Length > 1 && path.EndsWith('/') ? path[..^1] : path;
}
