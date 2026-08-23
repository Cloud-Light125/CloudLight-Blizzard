namespace CloudLightBlizzard.Services;

public static class CloudServiceConfiguration
{
    // Public endpoint, not a credential.
    public const string DefaultBaseUrl = "https://cloudlight-feedback.2693327171.workers.dev/";
    public const string QqGroup = "1108021175";
    public const long MaximumZipBytes = 25L * 1024 * 1024;

    public static string NormalizeBaseUrl(string? value)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? DefaultBaseUrl : value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            uri.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
            return DefaultBaseUrl;
        return uri.AbsoluteUri.TrimEnd('/') + "/";
    }
}
