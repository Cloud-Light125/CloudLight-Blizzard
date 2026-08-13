using System.Text.RegularExpressions;

namespace CloudLightBlizzard.Services.Overwatch;

// 暴雪生涯页 HTML → 结构化数据。
//
// 页面里【没有】任何内嵌 JSON(__INITIAL_STATE__ / application/json / window.X = {} 出现次数都是 0),
// 只能解析 HTML。这里用正则,不引 HtmlAgilityPack —— 项目现在零 HTML 依赖,为一个窗口加一个包不值。
//
// 铁律:每个字段独立 try,解析不到就留空,【绝不整页失败】。暴雪随时会改 class 名,
// 到时候宁可少显示几项,也不能让整个窗口白屏。
//
// 页面骨架（测试样例约 1.26 MB）：
//   blz-section.Profile-masthead[data-lastUpdate]      头像 / 名字 / 赞赏 / 两套段位(键鼠+手柄)
//   div.mouseKeyboard-view.Profile-view                键鼠大段
//     blz-section.Profile-heroSummary                    常用英雄进度条
//       div.Profile-heroSummary--view.quickPlay-view       ├ 快速
//       div.Profile-heroSummary--view.competitive-view     └ 竞技
//         div.Profile-progressBars[data-category-id]         每个口径一组(15 组)
//     blz-section.stats.quickPlay-view                   生涯统计(快速)
//     blz-section.stats.competitive-view                 生涯统计(竞技)
//       select[data-js=hero-select] > option value=N       N=0 是 ALL HEROES
//       span.stats-container.option-N                      对应英雄的全部统计
//   div.controller-view.Profile-view                   手柄大段(同构;没主机数据时是空壳)
public static class CareerParser
{
    // ── 输出模型 ────────────────────────────────────────────────
    public sealed class Profile
    {
        public string Name = "";
        public string BattleTag = "";
        public string PortraitUrl = "";
        public int EndorseLevel;
        public DateTimeOffset? LastUpdate;
        /// <summary>页面上既没段位也没统计 —— 生涯档案设了私密,或者这号一局没打过。</summary>
        public bool IsEmpty = true;
        public readonly List<View> Views = new();

        public View? Find(string input) => Views.FirstOrDefault(v => v.Input == input);
    }

    /// <summary>输入设备分段:mouseKeyboard(PC)/ controller(主机)。</summary>
    public sealed class View
    {
        public string Input = "";
        public readonly List<Rank> Ranks = new();
        public readonly Dictionary<string, Mode> Modes = new(StringComparer.OrdinalIgnoreCase);
        public bool HasData => Ranks.Count > 0 || Modes.Values.Any(m => m.HasData);
    }

    /// <summary>role: tank / offense / support。Tier 是英文档位(Platinum),Division 1~5(1 最高)。</summary>
    public sealed record Rank(string Role, string Tier, int Division, string TierIconUrl = "", string DivisionIconUrl = "");

    /// <summary>quickPlay / competitive。</summary>
    public sealed class Mode
    {
        public string Name = "";
        /// <summary>常用英雄进度条:categoryId(0x0860…) → 排好序的英雄条目。</summary>
        public readonly Dictionary<string, List<Bar>> Comparisons = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>生涯统计:hero slug(all-heroes / genji / …) → 分类块。</summary>
        public readonly Dictionary<string, HeroBlock> Heroes = new(StringComparer.OrdinalIgnoreCase);
        public bool HasData => Comparisons.Count > 0 || Heroes.Count > 0;

        public List<Bar> Bars(string categoryId) => Comparisons.TryGetValue(categoryId, out var v) ? v : new List<Bar>();
    }

    /// <summary>一条常用英雄进度条。Percent 是相对第一名的百分比(第一名恒为 100)。</summary>
    public sealed record Bar(string Slug, string DisplayName, string IconUrl, double Percent, string Value);

    public sealed class HeroBlock
    {
        public string Slug = "";
        public string DisplayName = "";
        public readonly List<StatGroup> Groups = new();

        /// <summary>按英文标签取值(已归一大小写/空格),取不到返回 null。</summary>
        public string? Get(string enLabel)
        {
            foreach (var g in Groups)
                foreach (var (k, v) in g.Items)
                    if (string.Equals(k.Trim(), enLabel, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }
    }

    /// <summary>Combat / Average / Best / Assists / Game / Match Awards / Hero Specific。</summary>
    public sealed class StatGroup
    {
        public string Category = "";
        public readonly List<(string Label, string Value)> Items = new();
    }

    // ── 常用口径的 categoryId(实测 15 个,这几个是我们要用的)────
    public const string CatTimePlayed = "0x0860000000000021";   // Time Played
    public const string CatGamesWon = "0x0860000000000039";     // Games Won
    public const string CatWinRate = "0x08600000000003D1";      // Win Percentage

    // ── 正则 ────────────────────────────────────────────────────
    private const RegexOptions Opt = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex ReLastUpdate = new(@"data-lastUpdate=""(\d+)""", Opt);
    private static readonly Regex RePortrait = new(@"class=""Profile-player--portrait""\s+src=""([^""]+)""", Opt);
    private static readonly Regex ReName = new(@"class=""Profile-player--name""[^>]*>([^<]*)<", Opt);
    private static readonly Regex ReEndorse = new(@"icons/endorsement/(\d)\.", Opt);
    private static readonly Regex ReRankWrap = new(@"(mouseKeyboard|controller)-view Profile-playerSummary--rankWrapper", Opt);
    private static readonly Regex ReRoleWrap = new(@"Profile-playerSummary--roleWrapper", Opt);
    private static readonly Regex ReRoleIcon = new(@"icons/role/(\w+)\.", Opt);
    private static readonly Regex ReTierIcon = new(@"src=""([^""]*Rank_(\w+)Tier\.[^""]*)""", Opt);
    private static readonly Regex ReDivIcon = new(@"src=""([^""]*TierDivision_(\d)[^""]*)""", Opt);
    private static readonly Regex ReViewSplit = new(@"class=""(mouseKeyboard|controller)-view Profile-view", Opt);
    private static readonly Regex ReHeroSummaryView = new(@"class=""Profile-heroSummary--view (\w+)-view", Opt);
    private static readonly Regex ReBarGroup = new(@"class=""Profile-progressBars[^""]*""[^>]*data-category-id=""([^""]+)""", Opt);
    private static readonly Regex ReBar = new(
        @"Profile-progressBar--icon""\s+src=""([^""]+)"".*?data-progress=""([^""]+)""\s+data-hero-id=""([^""]+)"".*?Profile-progressBar-title"">([^<]*)<.*?Profile-progressBar-description"">([^<]*)<", Opt);
    private static readonly Regex ReStatsSection = new(@"<blz-section class=""stats (\w+)-view", Opt);
    private static readonly Regex ReOption = new(@"<option value=""(\d+)"" option-id=""([^""]*)""", Opt);
    private static readonly Regex ReContainer = new(@"<span class=""stats-container option-(\d+)""", Opt);
    private static readonly Regex ReCategoryBlock = new(@"<div class=""header""><p>([^<]*)</p></div>", Opt);
    private static readonly Regex ReStatItem = new(
        @"<div class=""stat-item""><p class=""name"">([^<]*)</p><p class=""value"">([^<]*)</p>", Opt);

    // ── 入口 ────────────────────────────────────────────────────
    public static Profile Parse(string html, string battleTag = "")
    {
        var p = new Profile { BattleTag = battleTag };
        if (string.IsNullOrEmpty(html)) return p;

        Try(() => p.Name = Html(ReName.Match(html).Groups[1].Value));
        if (string.IsNullOrEmpty(p.Name) && battleTag.Contains('#')) p.Name = battleTag.Split('#')[0];
        Try(() => p.PortraitUrl = RePortrait.Match(html).Groups[1].Value);
        Try(() =>
        {
            var m = ReEndorse.Match(html);
            if (m.Success) p.EndorseLevel = int.Parse(m.Groups[1].Value);
        });
        Try(() =>
        {
            var m = ReLastUpdate.Match(html);
            if (m.Success && long.TryParse(m.Groups[1].Value, out var ts))
                p.LastUpdate = DateTimeOffset.FromUnixTimeSeconds(ts);
        });

        var ranks = ParseRanks(html);
        foreach (var (input, slice) in Slices(html, ReViewSplit))
        {
            var v = new View { Input = input };
            if (ranks.TryGetValue(input, out var rl)) v.Ranks.AddRange(rl);
            Try(() => ParseModes(slice, v));
            p.Views.Add(v);
        }

        // 有些号只有段位没打过任何统计(或反过来),两边都空才算"没数据"
        p.IsEmpty = !p.Views.Any(v => v.HasData);
        return p;
    }

    // ── 段位(在 masthead 里,不在两个 Profile-view 里)──────────
    private static Dictionary<string, List<Rank>> ParseRanks(string html)
    {
        var res = new Dictionary<string, List<Rank>>(StringComparer.OrdinalIgnoreCase);
        // 段位只在 masthead 里。先把 masthead 之后的内容切掉,免得手柄那段(通常是空壳)
        // 一路吃到页尾,把生涯统计里的段位图标误当成摘要段位。
        var head = ReViewSplit.Match(html);
        var masthead = head.Success ? html[..head.Index] : html;

        Try(() =>
        {
            foreach (var (input, slice) in Slices(masthead, ReRankWrap))
            {
                var list = new List<Rank>();
                foreach (var w in SliceAt(slice, ReRoleWrap))
                {
                    var role = ReRoleIcon.Match(w);
                    var tier = ReTierIcon.Match(w);
                    if (!role.Success || !tier.Success) continue;   // 空壳 roleWrapper(该定位本赛季未定级)
                    var div = ReDivIcon.Match(w);
                    list.Add(new Rank(
                        role.Groups[1].Value,
                        tier.Groups[2].Value,
                        div.Success ? int.Parse(div.Groups[2].Value) : 0,
                        tier.Groups[1].Value,
                        div.Success ? div.Groups[1].Value : ""));
                }
                if (list.Count > 0) res[input] = list;
            }
        });
        return res;
    }

    // ── 一个输入设备分段内:快速/竞技 两套数据 ──────────────────
    private static void ParseModes(string viewHtml, View v)
    {
        // 常用英雄进度条
        foreach (var (mode, slice) in Slices(viewHtml, ReHeroSummaryView))
        {
            var m = Get(v, mode);
            Try(() =>
            {
                foreach (var (cat, group) in Slices(slice, ReBarGroup))
                {
                    var bars = new List<Bar>();
                    foreach (Match b in ReBar.Matches(group))
                    {
                        double.TryParse(b.Groups[2].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var pct);
                        bars.Add(new Bar(b.Groups[3].Value, Html(b.Groups[4].Value), b.Groups[1].Value, pct, Html(b.Groups[5].Value)));
                    }
                    if (bars.Count > 0) m.Comparisons[cat] = bars;
                }
            });
        }

        // 生涯统计
        foreach (var (mode, slice) in Slices(viewHtml, ReStatsSection))
        {
            var m = Get(v, mode);
            Try(() => ParseStats(slice, m));
        }
    }

    private static void ParseStats(string sectionHtml, Mode m)
    {
        // 下拉:option value=N → 英雄显示名。N=0 固定是 ALL HEROES。
        var names = new Dictionary<string, string>();
        foreach (Match o in ReOption.Matches(sectionHtml))
        {
            // 进度条口径的下拉(option value 是 0x0860… 这种)不会匹配 \d+,天然被排除
            names.TryAdd(o.Groups[1].Value, Html(o.Groups[2].Value));
        }

        foreach (var (idx, block) in Slices(sectionHtml, ReContainer))
        {
            if (!names.TryGetValue(idx, out var display)) continue;
            var slug = OwEnNames.Slug(display);
            var hb = new HeroBlock { Slug = slug, DisplayName = display };

            // 每个 <div class="header"><p>类别</p></div> 起一个新分组,后面的 stat-item 都归它
            var heads = ReCategoryBlock.Matches(block).Cast<Match>().ToList();
            for (int i = 0; i < heads.Count; i++)
            {
                var start = heads[i].Index + heads[i].Length;
                var end = i + 1 < heads.Count ? heads[i + 1].Index : block.Length;
                var g = new StatGroup { Category = Html(heads[i].Groups[1].Value) };
                foreach (Match s in ReStatItem.Matches(block[start..end]))
                {
                    var label = Html(s.Groups[1].Value).Trim();
                    if (OwEnNames.IsPlaceholder(label)) continue;   // 未实装占位(…NYI),整条丢掉
                    g.Items.Add((label, Html(s.Groups[2].Value).Trim()));
                }
                if (g.Items.Count > 0) hb.Groups.Add(g);
            }
            if (hb.Groups.Count > 0) m.Heroes[slug] = hb;
        }
    }

    // ── 小工具 ──────────────────────────────────────────────────
    private static Mode Get(View v, string name)
    {
        if (!v.Modes.TryGetValue(name, out var m)) v.Modes[name] = m = new Mode { Name = name };
        return m;
    }

    /// <summary>
    /// 按同一个标记正则把 HTML 切片:第 i 个匹配到第 i+1 个匹配之间算一段。
    /// 用来代替配对标签解析 —— 页面里嵌套太深,数 &lt;div&gt; 不现实,靠"下一个同级标记"划界够用。
    /// </summary>
    private static List<(string Key, string Slice)> Slices(string html, Regex marker)
    {
        var res = new List<(string, string)>();
        var ms = marker.Matches(html).Cast<Match>().ToList();
        for (int i = 0; i < ms.Count; i++)
        {
            var start = ms[i].Index;
            var end = i + 1 < ms.Count ? ms[i + 1].Index : html.Length;
            res.Add((ms[i].Groups[1].Value, html[start..end]));
        }
        return res;
    }

    /// <summary>同上,但只要切片本身(标记里没有可用作 key 的捕获组时用)。</summary>
    private static List<string> SliceAt(string html, Regex marker)
    {
        var res = new List<string>();
        var ms = marker.Matches(html).Cast<Match>().ToList();
        for (int i = 0; i < ms.Count; i++)
            res.Add(html[ms[i].Index..(i + 1 < ms.Count ? ms[i + 1].Index : html.Length)]);
        return res;
    }

    private static string Html(string? s)
        => string.IsNullOrEmpty(s) ? "" : System.Net.WebUtility.HtmlDecode(s).Trim();

    private static void Try(Action a) { try { a(); } catch { /* fail soft:少一项好过整页崩 */ } }
}
