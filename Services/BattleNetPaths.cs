using System.IO;
using System.Text.Json;

namespace BnetSwitch.Services;

/// <summary>
/// 解析战网在本机的各个存储位置,并尽力自动定位 Battle.net.exe。
/// </summary>
public sealed class BattleNetPaths
{
    /// <summary>%LOCALAPPDATA%\Battle.net</summary>
    public string LocalRoot { get; }

    /// <summary>...\Account —— 每个账号一份加密的登录令牌(account.db)。</summary>
    public string AccountDir { get; }

    /// <summary>...\BrowserCaches —— 内嵌 Chromium 的网页会话缓存。</summary>
    public string BrowserCachesDir { get; }

    /// <summary>...\CachedData.db —— SQLite,含 login_cache 表与「当前账号」指针。</summary>
    public string CachedDataDb { get; }

    /// <summary>%APPDATA%\Battle.net —— 决定"当前登录哪个号"的文件都在这里(切换核心)。</summary>
    public string RoamingDir { get; }

    /// <summary>%APPDATA%\Battle.net\Battle.net.config —— SavedAccountNames(第一个=当前号)/ AutoLogin 等。</summary>
    public string RoamingConfig { get; }

    /// <summary>解析出的 Battle.net.exe 完整路径;找不到则为 null。</summary>
    public string? ClientExe { get; set; }

    public BattleNetPaths(string? localRoot = null, string? roamingDir = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        LocalRoot = localRoot ?? Path.Combine(local, "Battle.net");
        AccountDir = Path.Combine(LocalRoot, "Account");
        BrowserCachesDir = Path.Combine(LocalRoot, "BrowserCaches");
        CachedDataDb = Path.Combine(LocalRoot, "CachedData.db");
        RoamingDir = roamingDir ?? Path.Combine(roaming, "Battle.net");
        RoamingConfig = Path.Combine(RoamingDir, "Battle.net.config");

        ClientExe = ResolveClientExe();
    }

    /// <summary>战网数据目录是否存在(且至少登录过一次)。</summary>
    public bool Exists => Directory.Exists(LocalRoot) && File.Exists(CachedDataDb);

    private string? ResolveClientExe()
    {
        // 1) 优先从 Battle.net.config 里出现过的所有 "Path" 推断安装目录
        foreach (var dir in EnumerateConfigPaths())
        {
            var exe = Path.Combine(dir, "Battle.net.exe");
            if (File.Exists(exe)) return exe;
        }

        // 2) 常见默认安装位置兜底
        string[] defaults =
        {
            @"C:\Program Files (x86)\Battle.net\Battle.net.exe",
            @"D:\Battle.net\Battle.net.exe",
            @"C:\Battle.net\Battle.net.exe",
        };
        foreach (var d in defaults)
            if (File.Exists(d)) return d;

        return null;
    }

    private IEnumerable<string> EnumerateConfigPaths()
    {
        if (!File.Exists(RoamingConfig)) yield break;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(RoamingConfig));
        }
        catch
        {
            yield break;
        }

        using (doc)
        {
            foreach (var p in FindPathValues(doc.RootElement))
                yield return p;
        }
    }

    /// <summary>递归收集 JSON 中所有名为 "Path" 的字符串值。</summary>
    private static IEnumerable<string> FindPathValues(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.NameEquals("Path") && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var v = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(v)) yield return v!;
                    }

                    foreach (var sub in FindPathValues(prop.Value))
                        yield return sub;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    foreach (var sub in FindPathValues(item))
                        yield return sub;
                break;
        }
    }
}
