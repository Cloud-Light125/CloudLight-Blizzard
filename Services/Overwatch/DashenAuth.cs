using System.IO;
using System.Text.Json;

namespace BnetSwitch.Services.Overwatch;

// 网易大神登录态缓存:把会话 cookie 存到本机,免每次扫码。仅本机,不上传。
public static class DashenAuth
{
    private sealed record CookieDto(string Name, string Value, string Domain, string Path);

    private static string SessionFile
    {
        get
        {
            var d = AppPaths.Current.OverwatchSessionDir;
            Directory.CreateDirectory(d);
            return Path.Combine(d, "session.json");
        }
    }

    /// <summary>当前进程内已登录的 client(登录成功后由 UI/Probe 设置)。</summary>
    public static DashenClient? Current { get; private set; }

    /// <summary>有内存态 或 磁盘上有缓存会话。</summary>
    public static bool HasSession => Current != null || File.Exists(SessionFile);

    /// <summary>登录成功后调:记为当前 client 并把 cookie 落盘。</summary>
    public static void Save(DashenClient client)
    {
        Current = client;
        try
        {
            var dtos = client.ExportCookies().Select(c => new CookieDto(c.Name, c.Value, c.Domain, c.Path)).ToList();
            File.WriteAllText(SessionFile, JsonSerializer.Serialize(dtos));
        }
        catch { /* 落盘失败不致命,内存态仍可用 */ }
    }

    /// <summary>从磁盘缓存重建一个 client(未校验是否过期,调用方可 IsSessionAliveAsync)。无缓存返回 null。</summary>
    public static DashenClient? TryLoad()
    {
        if (Current != null) return Current;
        try
        {
            if (!File.Exists(SessionFile)) return null;
            var dtos = JsonSerializer.Deserialize<List<CookieDto>>(File.ReadAllText(SessionFile));
            if (dtos is null || dtos.Count == 0) return null;
            var client = new DashenClient();
            client.ImportCookies(dtos.Select(d => (d.Name, d.Value, d.Domain, d.Path)));
            return client;
        }
        catch { return null; }
    }

    /// <summary>拿一个可用的已登录 client:优先复用缓存(校验存活),失效返回 null(需重新扫码)。</summary>
    public static async Task<DashenClient?> GetAliveAsync()
    {
        var c = TryLoad();
        if (c is null) return null;
        if (await c.IsSessionAliveAsync()) { Current = c; return c; }
        return null;
    }

    public static void Logout()
    {
        Current = null;
        try { if (File.Exists(SessionFile)) File.Delete(SessionFile); } catch { }
    }
}
