using System.Text.RegularExpressions;

namespace CloudLightBlizzard.Services.Drops;

public static partial class SensitiveDataRedactor
{
    [GeneratedRegex("(?i)([\\\"']?(?:authorization|authticket|bbsticket|userticket|oauth(?:[_ -]?token)?|access[_ -]?token|refresh[_ -]?token|device[_ -]?code|client[_ -]?secret|proxy[_ -]?password|password|passwd|token|cookie|set-cookie)[\\\"']?\\s*[:=]\\s*[\\\"']?)([^\\\"'\\s,;}\\]]+)")]
    private static partial Regex SecretPattern();

    [GeneratedRegex("(?i)(bearer\\s+)[A-Za-z0-9._~+\\-/=]+")]
    private static partial Regex BearerPattern();

    [GeneratedRegex("(?i)([?&](?:access_token|refresh_token|oauth_token|device_code|code|token|auth|ticket|session)=)[^&#\\s]+")]
    private static partial Regex SecretQueryPattern();

    [GeneratedRegex("(?i)(https?://)[^/\\s:@]+:[^@/\\s]+@")]
    private static partial Regex UrlCredentialsPattern();

    [GeneratedRegex("(?i)(?:[A-Z]:)?\\\\Users\\\\[^\\\\/\\r\\n]+(?=[\\\\/])")]
    private static partial Regex WindowsUserPathPattern();

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var redacted = BearerPattern().Replace(value, "$1<redacted>");
        redacted = SecretPattern().Replace(redacted, "$1<redacted>");
        redacted = SecretQueryPattern().Replace(redacted, "$1<redacted>");
        redacted = UrlCredentialsPattern().Replace(redacted, "$1<redacted>@");
        return WindowsUserPathPattern().Replace(redacted, match =>
        {
            var drive = match.Value.Length >= 2 && match.Value[1] == ':' ? match.Value[..2] : "C:";
            return $@"{drive}\Users\<USER>";
        });
    }
}
