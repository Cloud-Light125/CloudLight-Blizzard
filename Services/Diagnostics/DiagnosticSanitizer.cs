using System.Text.RegularExpressions;

namespace CloudLightBlizzard.Services.Diagnostics;

/// <summary>
/// The diagnostic bundle is user-shareable by design. Keep all redaction in one place
/// so future checks cannot accidentally add a credential-bearing string to the export.
/// </summary>
public static partial class DiagnosticSanitizer
{
    [GeneratedRegex("(?i)(https?://)[^/\\s:@]+:[^@/\\s]+@")]
    private static partial Regex UrlCredentials();

    [GeneratedRegex("(?i)(https?://)<redacted>@")]
    private static partial Regex RedactedUrlCredentials();

    [GeneratedRegex("(?i)(authorization|proxy[-_ ]?password|github[-_ ]?token|cloudflare[-_ ]?token|bearer|cookie|secret|oauth[-_ ]?token|access[-_ ]?token|refresh[-_ ]?token|device[-_ ]?code|password|passwd|token|SESSDATA|bili_jct|DedeUserID(?:__ckMd5)?|buvid3|buvid4|b_nut|sid|LIVE_BUVID|csrf(?:_token)?)(\\s*[:=]\\s*)[^\\r\\n,;}]{1,}")]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex("(?i)\\bBearer\\s+[A-Za-z0-9._~+\\-/=]+")]
    private static partial Regex BearerToken();

    [GeneratedRegex("(?i)([?&](?:access_token|refresh_token|oauth_token|device_code|code|token|auth|ticket|session|csrf|csrf_token|SESSDATA|bili_jct|DedeUserID|DedeUserID__ckMd5)=)[^&#\\s]+")]
    private static partial Regex SecretQuery();

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var safe = CloudLightBlizzard.Services.Drops.SensitiveDataRedactor.Redact(value);
        safe = UrlCredentials().Replace(safe, "$1***:***@");
        safe = RedactedUrlCredentials().Replace(safe, "$1***:***@");
        safe = BearerToken().Replace(safe, "Bearer [REDACTED]");
        safe = SecretQuery().Replace(safe, "$1[REDACTED]");
        safe = KeyValueSecret().Replace(safe, "$1$2[REDACTED]");
        return safe;
    }

    public static string SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        return Sanitize(path);
    }
}
