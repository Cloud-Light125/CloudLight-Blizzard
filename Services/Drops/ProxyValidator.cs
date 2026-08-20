using System.Net;

namespace CloudLightBlizzard.Services.Drops;

public static class ProxyValidator
{
    public static bool TryNormalize(string? value, out string normalized, out string error)
    {
        normalized = (value ?? "").Trim();
        error = "";
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "代理地址必须以 http:// 或 https:// 开头。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath is not ("" or "/"))
        {
            error = "代理地址只能包含主机和端口，不能包含路径、查询或片段。";
            return false;
        }
        normalized = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).TrimEnd('/');
        return true;
    }

    public static IWebProxy? Create(DropsProxySettings settings)
        => settings.EnableProxy && TryNormalize(settings.ProxyUrl, out var url, out _)
            ? new WebProxy(url)
            : null;
}
