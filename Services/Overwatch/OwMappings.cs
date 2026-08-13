using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace CloudLightBlizzard.Services.Overwatch;

// GUID→中文/图片 映射:英雄/地图配置从公开 CDN 下载并本地缓存;段位是静态映射(名→中文+配色key)。
// 关键:英雄按 heroGuid(十进制)匹配 matchList;地图按 id(十六进制)匹配 matchList.mapGuid。
public sealed class OwMappings
{
    private const string Cdn = "https://s.166.net/config/ds_ow/";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public sealed record HeroInfo(string Name, string Icon);
    public sealed record MapInfo(string Name, string Icon, string Mode);
    public sealed record TraitModInfo(string Name, string Icon, string Desc, string Category, string Level);

    private Dictionary<string, HeroInfo> _hero = new();   // heroGuid(十进制) → 名+立绘
    private Dictionary<string, MapInfo> _map = new();     // id(十六进制)    → 名+图+模式
    private Dictionary<string, string> _rankIcon = new(StringComparer.OrdinalIgnoreCase); // Bronze→段位图标
    private Dictionary<string, string> _endorseIcon = new();  // 赞赏等级 1-5 → 图标
    private Dictionary<string, string> _attr = new();         // valueGuid → 属性中文名(英雄详情场均字段)
    private Dictionary<string, (string Cn, string Icon)> _fightRank = new(StringComparer.OrdinalIgnoreCase); // 角斗段位 Master→(全明星,icon)
    private string _fightMapUrl = "";                          // 角斗专属地图不在 ow_map_config,用这张通用图兜底
    private Dictionary<string, TraitModInfo> _modTrait = new(); // 角斗天赋/道具:十进制guid → 名/图/描述/类别/品级
    private Dictionary<string, TraitModInfo> _perk = new();      // 竞技对局天赋 perk:十进制guid → 名/图/描述
    public bool Loaded { get; private set; }

    private static string CacheDir
    {
        get
        {
            var d = Path.Combine(AppPaths.Current.OverwatchCacheDir, "config");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public async Task InitAsync()
    {
        var heroDoc = await LoadConfigAsync("ow_hero_config.json");
        var mapDoc = await LoadConfigAsync("ow_map_config.json");
        // 同时按十进制 guid 和十六进制 id 索引、大小写不敏感:matchList 用哪种格式都能命中。
        // 英雄用小图标(ddHeroIcon→smallIconUrl→icon),避免缓存整张大立绘。
        _hero = BuildDict(heroDoc, new[] { "heroGuid", "id" },
            el => new HeroInfo(GetStr(el, "name"), FirstNonEmpty(el, "ddHeroIcon", "smallIconUrl", "icon")));
        _map = BuildDict(mapDoc, new[] { "guid", "id" }, el => new MapInfo(GetStr(el, "name"), GetStr(el, "icon"), GetStr(el, "mode")));

        // 段位图标:ow_rank_config.json 的 rankList,key(Bronze…)→ icon
        var rankDoc = await LoadConfigAsync("ow_rank_config.json");
        if (rankDoc != null)
            foreach (var r in EnumArr(rankDoc.RootElement, "rankList"))
            {
                var k = GetStr(r, "key"); var ic = GetStr(r, "icon");
                if (k.Length > 0 && ic.Length > 0) _rankIcon[k] = ic;
            }
        // 赞赏等级图标:工具配置的 appreciationLevelIcon { "1":url, ... }
        var toolDoc = await LoadConfigAsync("ow_record_query_tool.json");
        if (toolDoc != null && toolDoc.RootElement.TryGetProperty("appreciationLevelIcon", out var ap) && ap.ValueKind == JsonValueKind.Object)
            foreach (var pr in ap.EnumerateObject())
                if (pr.Value.ValueKind == JsonValueKind.String) _endorseIcon[pr.Name] = pr.Value.GetString() ?? "";
        // 角斗段位:rankListFight,key(Bronze…Grandmaster,同竞技英文键)→ (中文名 菜鸟…传奇, 图标)
        if (toolDoc != null)
            foreach (var r in EnumArr(toolDoc.RootElement, "rankListFight"))
            {
                var k = GetStr(r, "key"); var nm = GetStr(r, "name"); var ic = GetStr(r, "icon");
                if (k.Length > 0) _fightRank[k] = (nm, ic);
            }
        if (toolDoc != null && toolDoc.RootElement.TryGetProperty("fightMapUrl", out var fmu) && fmu.ValueKind == JsonValueKind.String)
            _fightMapUrl = fmu.GetString() ?? "";

        // 角斗天赋(异能)/道具 + 竞技对局天赋(perk):都按英雄hex+common 分组、条目 id 是十六进制 → 建十进制全局索引
        LoadIdIndexed(await LoadConfigAsync("ow_record_query_tool_mod_trait.json"), _modTrait);
        LoadIdIndexed(await LoadConfigAsync("ow_perk_list.json"), _perk);

        // 英雄详情属性名:ow_hero_attr.json 是 [{valueGuid, valueText}] 扁平数组
        var attrDoc = await LoadConfigAsync("ow_hero_attr.json");
        if (attrDoc != null && attrDoc.RootElement.ValueKind == JsonValueKind.Array)
            foreach (var a in attrDoc.RootElement.EnumerateArray())
            {
                var g = GetStr(a, "valueGuid"); var t = GetStr(a, "valueText");
                if (g.Length > 0 && t.Length > 0) _attr.TryAdd(g, t);
            }
        Loaded = true;
    }

    public sealed record SeasonMeta(List<int> Seasons, HashSet<string> ModesWithData);

    /// <summary>从 ow_record_query_tool.json 的 AIEvaluateConfig 读可查赛季(降序)+ 有数据的模式(SportPreset 等)。纯配置,无需登录。</summary>
    public static async Task<SeasonMeta> LoadSeasonMetaAsync()
    {
        var seasons = new SortedSet<int>();
        var modes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var doc = await LoadConfigAsync("ow_record_query_tool.json");
        if (doc != null && doc.RootElement.TryGetProperty("AIEvaluateConfig", out var ae) && ae.ValueKind == JsonValueKind.Array)
            foreach (var m in ae.EnumerateArray())
            {
                if (!m.TryGetProperty("seasonIdList", out var sl) || sl.ValueKind != JsonValueKind.Array) continue;
                bool any = false;
                foreach (var s in sl.EnumerateArray())
                    if (s.ValueKind == JsonValueKind.String && int.TryParse(s.GetString(), out var n)) { seasons.Add(n); any = true; }
                if (any) modes.Add(GetStr(m, "mode"));
            }
        return new SeasonMeta(seasons.Reverse().ToList(), modes);
    }

    private static IEnumerable<JsonElement> EnumArr(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    // 天赋/道具/perk 类配置:{ heroHex|common: [{id(十六进制), name, icon, desc, category, level}] } → 十进制guid 全局索引
    // 注:perk 无独立 desc,把描述用 <br /> 拼在 name 里,需拆开;并统一去掉 HTML 标签。
    private static void LoadIdIndexed(JsonDocument? doc, Dictionary<string, TraitModInfo> into)
    {
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return;
        foreach (var grp in doc.RootElement.EnumerateObject())
            if (grp.Value.ValueKind == JsonValueKind.Array)
                foreach (var it in grp.Value.EnumerateArray())
                {
                    var hx = GetStr(it, "id");
                    if (!hx.StartsWith("0x") || !long.TryParse(hx.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var dec)) continue;
                    string rawName = GetStr(it, "name"), desc = GetStr(it, "desc");
                    int br = rawName.IndexOf("<br", StringComparison.OrdinalIgnoreCase);
                    if (br >= 0) { if (desc.Length == 0) desc = rawName[br..]; rawName = rawName[..br]; }
                    into[dec.ToString()] = new TraitModInfo(StripHtml(rawName), GetStr(it, "icon"),
                        StripHtml(desc), GetStr(it, "category"), GetStr(it, "level"));
                }
    }

    private static string StripHtml(string s)
        => string.IsNullOrEmpty(s) ? s
           : System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", " ").Replace("&nbsp;", " ").Trim();

    private static async Task<JsonDocument?> LoadConfigAsync(string file)
    {
        string path = Path.Combine(CacheDir, file);
        string? json = null;
        try
        {
            if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < MaxAge)
                json = await File.ReadAllTextAsync(path);
        }
        catch { }

        if (json is null)
        {
            try { json = await Http.GetStringAsync(Cdn + file); await File.WriteAllTextAsync(path, json); }
            catch { if (File.Exists(path)) { try { json = await File.ReadAllTextAsync(path); } catch { } } }
        }
        if (json is null) return null;
        try { return JsonDocument.Parse(json); } catch { return null; }
    }

    // 配置是扁平对象数组;每个条目按其所有 guidKeys(如 guid+id)都建一条索引,大小写不敏感。
    private static Dictionary<string, T> BuildDict<T>(JsonDocument? doc, string[] guidKeys, Func<JsonElement, T> make)
    {
        var dict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        if (doc != null) Walk(doc.RootElement, guidKeys, make, dict);
        return dict;
    }

    private static void Walk<T>(JsonElement el, string[] guidKeys, Func<JsonElement, T> make, Dictionary<string, T> dict)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in el.EnumerateArray()) Walk(it, guidKeys, make, dict);
            return;
        }
        if (el.ValueKind != JsonValueKind.Object) return;

        bool isEntry = false;
        T? val = default;
        foreach (var gk in guidKeys)
            if (el.TryGetProperty(gk, out var g))
            {
                string key = g.ValueKind == JsonValueKind.String ? g.GetString() ?? "" : g.ToString();
                if (key.Length == 0) continue;
                if (!isEntry) { val = make(el); isEntry = true; }
                dict.TryAdd(key, val!);
            }
        if (!isEntry)
            foreach (var p in el.EnumerateObject()) Walk(p.Value, guidKeys, make, dict);
    }

    private static string GetStr(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string FirstNonEmpty(JsonElement el, params string[] props)
    {
        foreach (var p in props) { var s = GetStr(el, p); if (!string.IsNullOrEmpty(s)) return s; }
        return "";
    }

    // ===== 查询 =====
    public string HeroName(object? guid) => _hero.TryGetValue(guid?.ToString() ?? "", out var h) ? h.Name : "英雄";
    public string HeroIcon(object? guid) => _hero.TryGetValue(guid?.ToString() ?? "", out var h) ? h.Icon : "";
    public string MapName(object? guid) => _map.TryGetValue(guid?.ToString() ?? "", out var m) ? m.Name : "";
    public string MapIcon(object? guid) => _map.TryGetValue(guid?.ToString() ?? "", out var m) ? m.Icon : "";
    public string MapMode(object? guid) => _map.TryGetValue(guid?.ToString() ?? "", out var m) ? m.Mode : "";
    public string RankIconUrl(string? rankKey) => rankKey != null && _rankIcon.TryGetValue(rankKey, out var u) ? u : "";
    public string EndorseIconUrl(int level) => _endorseIcon.TryGetValue(level.ToString(), out var u) ? u : "";
    public string? AttrName(string valueGuid) => _attr.TryGetValue(valueGuid, out var t) ? t : null;
    public string FightRankIcon(string rankNameKey) => _fightRank.TryGetValue(rankNameKey, out var v) ? v.Icon : "";
    public string FightMapFallbackIcon => _fightMapUrl;
    public TraitModInfo? TraitMod(string guid) => _modTrait.TryGetValue(guid, out var v) ? v : null;
    public TraitModInfo? Perk(string guid) => _perk.TryGetValue(guid, out var v) ? v : null;

    /// <summary>角斗段位:用 rankName 字段(Master…)查 → (中文 全明星…, 配色key)。配色复用竞技表(英文键相同)。</summary>
    public (string Cn, string? BrushKey) FightRank(string? rankNameKey)
    {
        if (string.IsNullOrEmpty(rankNameKey) || rankNameKey == "None") return ("未定级", null);
        string cn = _fightRank.TryGetValue(rankNameKey, out var v) ? v.Cn : rankNameKey;
        string? brush = RankTable.TryGetValue(rankNameKey, out var rt) ? rt.Brush : "Stats.TierDiamond";
        return (cn, brush);
    }
    public int HeroCount => _hero.Values.Select(h => h.Name).Distinct().Count();
    public int MapCount => _map.Values.Select(m => m.Name).Distinct().Count();

    /// <summary>全部英雄图标 URL(供批量预取缩略图)。</summary>
    public IEnumerable<string> AllHeroIconUrls()
        => _hero.Values.Select(h => h.Icon).Where(s => !string.IsNullOrEmpty(s)).Distinct();

    /// <summary>全部地图图 URL(供批量预取缩略图)。</summary>
    public IEnumerable<string> AllMapIconUrls()
        => _map.Values.Select(m => m.Icon).Where(s => !string.IsNullOrEmpty(s)).Distinct();

    // ===== 段位:英文名 → (中文, 配色画刷 key)。画刷 key 需在 StatsColors 里存在(集成时补齐8档)=====
    private static readonly Dictionary<string, (string Cn, string Brush)> RankTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bronze"] = ("青铜", "Stats.TierBronze"),
        ["Silver"] = ("白银", "Stats.TierSilver"),
        ["Gold"] = ("黄金", "Stats.TierGold"),
        ["Platinum"] = ("白金", "Stats.TierPlatinum"),
        ["Diamond"] = ("钻石", "Stats.TierDiamond"),
        ["Master"] = ("大师", "Stats.TierMaster"),
        ["Grandmaster"] = ("宗师", "Stats.TierGm"),
        ["Champion"] = ("英杰", "Stats.TierChampion"),
    };

    public static (string Cn, string? BrushKey) Rank(string? rankNameEn)
    {
        if (string.IsNullOrEmpty(rankNameEn) || rankNameEn == "None") return ("未定级", null);
        return RankTable.TryGetValue(rankNameEn, out var v) ? (v.Cn, v.Brush) : (rankNameEn, "Stats.TierDiamond");
    }
}
