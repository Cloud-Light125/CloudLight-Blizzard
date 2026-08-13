using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using BnetSwitch.Services.Overwatch;

namespace BnetSwitch.Stats;

// 国际服/亚服生涯:抓暴雪官方生涯页 → 解析 → 灌进和国服共用的 PlayerStats。
//
// 与国服(网易大神)的口径差异,必须在界面上说清楚,不能混为一谈:
//   · 暴雪给的是【每 10 分钟】均值,网易给的是【场均】—— 数值差一大截,标签跟着数据源走
//   · 暴雪没有「抵挡」这个口径(Barrier Damage Done 是打对面护盾造成的伤害,不是同一回事)→ 留空
//   · 暴雪生涯页没有对局记录、没有本周表现、没有口碑、没有历史赛季 —— 这是数据源限制,不是没做
public sealed class CareerService
{
    private readonly BlizzardCareerClient _client = new();

    /// <summary>原始 HTML 缓存有效期。点「刷新」传 force:true 强制重抓。</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>抓取结果。Profile 是整页解析产物 —— 键鼠/手柄 × 快速/竞技 四份数据都在里面,窗口自己挑。</summary>
    public sealed record Outcome(CareerParser.Profile? Profile, string? Error, bool NotFound, bool FromCache);

    public const string InputPc = "mouseKeyboard";
    public const string InputPad = "controller";
    public const string ModeQuick = "quickPlay";
    public const string ModeComp = "competitive";

    public async Task<Outcome> LoadAsync(string battleTag, bool force = false, CancellationToken ct = default)
    {
        string? html = force ? null : ReadCache(battleTag);
        bool fromCache = html != null;

        if (html == null)
        {
            var r = await _client.LoadAsync(battleTag, ct).ConfigureAwait(false);
            if (r.Status == BlizzardCareerClient.Status.NotFound)
                return new Outcome(null, r.Error ?? "该账号在国际服查不到", true, false);
            if (r.Status != BlizzardCareerClient.Status.Ok || string.IsNullOrEmpty(r.Html))
            {
                // 网络挂了但本地还有过期缓存 → 用旧数据,总比白屏强
                var stale = ReadCache(battleTag, ignoreTtl: true);
                if (stale != null) { html = stale; fromCache = true; }
                else return new Outcome(null, r.Error ?? "拉取失败", false, false);
            }
            else
            {
                html = r.Html;
                WriteCache(battleTag, html);
            }
        }

        var profile = CareerParser.Parse(html!, battleTag);
        if (profile.IsEmpty)
            return new Outcome(null, "该生涯档案未公开(或这个号还没有可展示的数据)", false, fromCache);

        return new Outcome(profile, null, false, fromCache);
    }

    // ── 分段可用性(界面上「手柄 · 无数据」那种禁用态靠这个)────
    public static bool HasData(CareerParser.Profile p, string input)
        => p.Find(input) is { HasData: true };

    public static bool HasData(CareerParser.Profile p, string input, string mode)
        => p.Find(input)?.Modes.TryGetValue(mode, out var m) == true && m.HasData;

    /// <summary>默认输入设备:优先键鼠,键鼠没数据就用手柄。</summary>
    public static string PickInput(CareerParser.Profile p)
        => HasData(p, InputPc) ? InputPc : HasData(p, InputPad) ? InputPad : InputPc;

    /// <summary>默认模式:优先竞技(有段位),否则快速。</summary>
    public static string PickMode(CareerParser.Profile p, string input)
        => HasData(p, input, ModeComp) ? ModeComp : HasData(p, input, ModeQuick) ? ModeQuick : ModeComp;

    // ── Profile → PlayerStats ───────────────────────────────────
    /// <summary>把指定分段(输入设备 × 模式)映射成界面吃的 PlayerStats。</summary>
    public static async Task<PlayerStats> BuildAsync(CareerParser.Profile p, string input, string modeName, Func<string, Brush?> fb)
    {
        var view = p.Find(input);
        var mode = view != null && view.Modes.TryGetValue(modeName, out var m) ? m : null;

        var tag = p.BattleTag.Contains('#') ? p.BattleTag : $"{p.Name}#";
        var ps = new PlayerStats
        {
            DisplayName = string.IsNullOrEmpty(p.Name) ? tag.Split('#')[0] : p.Name,
            TagSuffix = tag.Contains('#') ? "#" + tag.Split('#')[^1] : "",
            EndorseLevel = p.EndorseLevel,
            UpdatedAt = p.LastUpdate is { } t ? t.ToLocalTime().ToString("MM-dd HH:mm") : "",
            // 段位只属于竞技;快速模式整块藏掉,别留一排空段位让人以为没加载出来
            ShowRanks = modeName == ModeComp && view is { Ranks.Count: > 0 },
            RankSectionTitle = "当前段位",
            AvgSectionTitle = "表现概览",
            AvgDamageLabel = "每10分钟伤害",
            AvgHealLabel = "每10分钟治疗",
            KdaLabel = "KDA",
            WinRateLabel = "胜率",
        };
        ps.Initial = string.IsNullOrEmpty(ps.DisplayName) ? "?" : ps.DisplayName[..1];
        ps.AvatarLocal = await OwImageCache.GetAsync(p.PortraitUrl, 120).ConfigureAwait(false);

        // 段位:tank / offense / support
        if (view != null)
            foreach (var r in view.Ranks)
            {
                var rr = await ToRankAsync(r, fb).ConfigureAwait(false);
                switch (r.Role.ToLowerInvariant())
                {
                    case "tank": ps.Tank = rr; break;
                    case "offense" or "damage" or "dps": ps.Dps = rr; break;
                    case "support" or "healer": ps.Support = rr; break;
                }
            }

        if (mode == null) return ps;

        var all = mode.Heroes.TryGetValue("all-heroes", out var a) ? a : null;
        if (all != null)
        {
            ps.TotalHours = Hours(all.Get("Time Played"));
            ps.AvgDamage = all.Get("Hero Damage Done - Avg per 10 Min") ?? "";
            ps.AvgHeal = all.Get("Healing Done - Avg per 10 Min") ?? "";
            ps.AvgResist = "";                     // 暴雪无「抵挡」口径,HasResist 会自动隐藏这一格

            var elim = Num(all.Get("Eliminations"));
            var asst = Num(all.Get("Assists"));
            var dead = Num(all.Get("Deaths"));
            if (dead > 0) ps.Kda = ((elim + asst) / dead).ToString("0.00");

            var won = Num(all.Get("Games Won"));
            var lost = Num(all.Get("Games Lost"));
            var played = Num(all.Get("Games Played"));
            if (played <= 0) played = won + lost;
            if (played > 0)
            {
                ps.SeasonWinRate = (won / played).ToString("P0");
                ps.WinLossText = $"{won:0}胜 {lost:0}负";
            }

            var solo = all.Get("Solo Kills - Avg per 10 Min");
            var fb10 = all.Get("Final Blows - Avg per 10 Min");
            if (solo != null || fb10 != null)
                ps.AvgExtra = string.Join(" · ", new[]
                {
                    solo == null ? null : $"每10分钟单独消灭 {solo}",
                    fb10 == null ? null : $"每10分钟最后一击 {fb10}",
                }.Where(s => s != null));
        }

        ps.Heroes = await HeroesAsync(mode).ConfigureAwait(false);
        return ps;
    }

    private static async Task<RoleRank> ToRankAsync(CareerParser.Rank r, Func<string, Brush?> fb)
    {
        var (cn, brushKey) = OwMappings.Rank(r.Tier);
        return new RoleRank
        {
            TierText = r.Division > 0 ? $"{cn} {r.Division}" : cn,
            ScoreText = "",                        // 暴雪只给档位,没有分数
            ExtraText = "",                        // 也没有场次/连胜
            TierBrush = brushKey is null ? null : fb(brushKey),
            TierIconLocal = await OwImageCache.GetAsync(r.TierIconUrl, 96).ConfigureAwait(false),
        };
    }

    private static async Task<List<HeroStat>> HeroesAsync(CareerParser.Mode mode)
    {
        var res = new List<HeroStat>();

        // 时长榜就是常用英雄榜(第一名 data-progress 恒为 100),胜率/胜场另按 slug 并进来
        var winRate = mode.Bars(CareerParser.CatWinRate).ToDictionary(b => b.Slug, b => b.Value, StringComparer.OrdinalIgnoreCase);
        var gamesWon = mode.Bars(CareerParser.CatGamesWon).ToDictionary(b => b.Slug, b => b.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var bar in mode.Bars(CareerParser.CatTimePlayed))
        {
            var hs = new HeroStat
            {
                Name = OwEnNames.Hero(bar.Slug),
                IconLocal = await OwImageCache.GetAsync(bar.IconUrl, 128).ConfigureAwait(false),
                PlayPercent = bar.Percent,
                HoursText = Hours(bar.Value),
                WinRateText = winRate.TryGetValue(bar.Slug, out var w) ? Percent(w) : "",
                TotalTabText = "累计",             // 暴雪给的是生涯累计值,不是场均
            };
            hs.Detail = string.Join(" · ", new[] { hs.HoursText, hs.WinRateText }.Where(s => !string.IsNullOrEmpty(s)));

            if (mode.Heroes.TryGetValue(bar.Slug, out var block))
            {
                hs.MatchsText = block.Get("Games Played") is { } g ? $"{g} 场"
                              : gamesWon.TryGetValue(bar.Slug, out var gw) ? $"{gw} 胜" : "";
                Fill(block, hs);
            }
            res.Add(hs);
        }
        return res;
    }

    /// <summary>把一个英雄的全部统计翻成简体,按「每10分钟」拆成两组(英雄详情窗分栏显示)。</summary>
    private static void Fill(CareerParser.HeroBlock block, HeroStat hs)
    {
        // 暴雪页面本身就有重复条目(同一个 Games Won 在 Game 分类里出现三次),按「译名+值」去重。
        // 只去完全相同的那种,不同值的同名项照留,免得把真数据丢了。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in block.Groups)
            foreach (var (label, value) in g.Items)
            {
                var item = new StatItem { Name = OwEnNames.Stat(label), Value = value };
                if (!seen.Add(item.Name + " " + item.Value)) continue;
                if (label.Contains("Avg per 10", StringComparison.OrdinalIgnoreCase)) hs.DetailStatsPerTen.Add(item);
                else hs.DetailStats.Add(item);
            }
    }

    // ── 小工具 ──────────────────────────────────────────────────
    /// <summary>"245:10:48" → "245 小时 10 分";"07:41" → "7 分钟";不足一分钟不显示 "0 分钟"。</summary>
    private static string Hours(string? hms)
    {
        if (string.IsNullOrWhiteSpace(hms)) return "";
        var parts = hms.Split(':');
        int h = 0, m = 0;
        if (parts.Length == 3) { int.TryParse(parts[0], out h); int.TryParse(parts[1], out m); }
        else if (parts.Length == 2) int.TryParse(parts[0], out m);
        else return hms;

        if (h > 0) return m > 0 ? $"{h} 小时 {m} 分" : $"{h} 小时";
        return m > 0 ? $"{m} 分钟" : "不足 1 分钟";
    }

    /// <summary>胜率进度条在 0 的时候只给 "0"(没有百分号),补上,免得界面上一列 % 里混一个光杆 0。</summary>
    private static string Percent(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.Trim();
        return t.EndsWith('%') ? t : (double.TryParse(t, out _) ? t + "%" : t);
    }

    /// <summary>"31,088" / "8.12" / "49%" → double。解析不了返回 0。</summary>
    private static double Num(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var t = s.Replace(",", "").Replace("%", "").Trim();
        return double.TryParse(t, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    // ── 原始 HTML 缓存(gzip)───────────────────────────────────
    // 存原始 HTML 而不是解析结果:解析模型里全是只读集合和 Brush,序列化要额外一套 DTO,
    // 不划算;整页 1.2MB gzip 后 100KB 出头,存盘完全可以接受,而且改了解析器还能拿旧页重放。
    private static string CacheDir
    {
        get { var d = Path.Combine(OwImageCache.CacheRoot, "career"); Directory.CreateDirectory(d); return d; }
    }

    private static string CacheFile(string battleTag)
    {
        // 统一小写:暴雪的 URL 区分大小写,但同一个号大小写写错也该命中同一份缓存
        var safe = new string(battleTag.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        return Path.Combine(CacheDir, safe + ".html.gz");
    }

    private static string? ReadCache(string battleTag, bool ignoreTtl = false)
    {
        try
        {
            var f = CacheFile(battleTag);
            if (!File.Exists(f)) return null;
            if (!ignoreTtl && DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > CacheTtl) return null;
            using var fs = File.OpenRead(f);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var sr = new StreamReader(gz, Encoding.UTF8);
            return sr.ReadToEnd();
        }
        catch { return null; }
    }

    private static void WriteCache(string battleTag, string html)
    {
        try
        {
            using var fs = File.Create(CacheFile(battleTag));
            using var gz = new GZipStream(fs, CompressionLevel.Optimal);
            using var sw = new StreamWriter(gz, Encoding.UTF8);
            sw.Write(html);
        }
        catch { /* 缓存写不了不影响本次展示 */ }
    }
}
