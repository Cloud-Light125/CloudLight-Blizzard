using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BnetSwitch.Stats;

// ===== 数据模型 =====

public sealed class PlayerStats
{
    public string Initial { get; set; } = "?";        // 头像圈里的首字(头像图加载不出时兜底)
    public string? AvatarLocal { get; set; }          // 头像图本地缓存路径(有则盖住首字)
    public string DisplayName { get; set; } = "";     // BattleTag 名字部分
    public string TagSuffix { get; set; } = "";       // #编号
    public bool HasTitle { get; set; }                // 是否显示头衔徽章
    public string TitleText { get; set; } = "";       // 个人称号/头衔,如"高端局"
    public string? TitleIconLocal { get; set; }       // 头衔图标本地路径
    public int EndorseLevel { get; set; }             // 赞赏等级 1-5
    public string? EndorseIconLocal { get; set; }     // 赞赏等级图标本地路径
    public string TotalHours { get; set; } = "";      // 游戏时间
    public string WeekPerf { get; set; } = "";        // 本周表现(按近期胜率派生)
    public string WeekDetail { get; set; } = "";      // 本周近况明细(悬停显示)
    public string ReputeComment { get; set; } = "";   // 口碑评价,例:良好
    public string ReputeText { get; set; } = "";      // 获赞/送赞/被举报
    public bool HasRepute => !string.IsNullOrEmpty(ReputeComment);
    public string UpdatedAt { get; set; } = "";

    public RoleRank Tank { get; set; } = new();
    public RoleRank Dps { get; set; } = new();
    public RoleRank Support { get; set; } = new();
    public RoleRank OpenRank { get; set; } = new();   // 开放职责单段位

    public bool ShowRanks { get; set; } = true;       // 休闲模式隐藏段位区
    public bool IsOpenQueue { get; set; }             // true=开放职责(单段位),false=预设(分定位)
    public string RankSectionTitle { get; set; } = "竞技段位 · 本赛季";
    public string AvgSectionTitle { get; set; } = "场均表现";

    public string AvgDamage { get; set; } = "";
    public string AvgHeal { get; set; } = "";
    public string AvgResist { get; set; } = "";       // 场均抵挡
    public bool HasResist => !string.IsNullOrEmpty(AvgResist);

    // 口径标签:国服(网易)是「场均」,国际服(暴雪生涯页)是「每10分钟」——两边数不是一回事,
    // 标签必须跟着数据源走,不能写死。默认值是国服口径,国服窗不绑这几个属性也不受影响。
    public string AvgDamageLabel { get; set; } = "场均伤害";
    public string AvgHealLabel { get; set; } = "场均治疗";
    public string AvgResistLabel { get; set; } = "场均抵挡";
    public string KdaLabel { get; set; } = "KDA";
    public string WinRateLabel { get; set; } = "本赛季胜率";
    public string Kda { get; set; } = "";
    public string SeasonWinRate { get; set; } = "";
    public string WinLossText { get; set; } = "";     // 例:69胜 60负
    public bool HasWinLoss => !string.IsNullOrEmpty(WinLossText);
    public string AvgExtra { get; set; } = "";        // 场均单独消灭/最后一击
    public bool HasAvgExtra => !string.IsNullOrEmpty(AvgExtra);

    public List<HeroStat> Heroes { get; set; } = new();
    public ObservableCollection<MatchRecord> Matches { get; set; } = new();  // 滚动翻页追加
}

public sealed class RoleRank
{
    public string TierText { get; set; } = "";        // 例:宗师 5
    public string ScoreText { get; set; } = "";       // 例:3890 分 · 最高 3924
    public string ExtraText { get; set; } = "";       // 例:121场 · 胜率52% · KDA 11.4 · 连胜6
    public bool HasExtra => !string.IsNullOrEmpty(ExtraText);
    public Brush? TierBrush { get; set; }             // 段位配色,用 Stats.Tier* 画刷
    public string? TierIconLocal { get; set; }        // 段位图标本地路径
    public bool Highlight { get; set; }
}

public sealed class HeroStat
{
    public string Name { get; set; } = "";
    public string? IconLocal { get; set; }            // 英雄小图标本地路径
    public string Detail { get; set; } = "";          // 例:87h · 58%
    public double PlayPercent { get; set; }           // 0~100,条形图长度
    public string PlayPercentText => PlayPercent > 0 ? PlayPercent.ToString("0") + "%" : "";  // 国际服窗单列显示占比
    // 点击详情用:
    public string HoursText { get; set; } = "";       // 87 小时
    public string WinRateText { get; set; } = "";     // 58%
    public string MatchsText { get; set; } = "";      // 132 场
    public string LevelText { get; set; } = "";       // 英雄等级,例:Lv.291
    public bool HasLevel => !string.IsNullOrEmpty(LevelText);
    public string RankText { get; set; } = "";        // 该英雄段位,如 钻石4
    public bool HasRank => !string.IsNullOrEmpty(RankText);
    public List<StatItem> DetailStats { get; set; } = new();  // 场均属性(命中率/伤害/消灭…)
    public List<StatItem> DetailStatsPerTen { get; set; } = new(); // 每10分钟属性
    /// <summary>详情窗左边那个页签的名字:国服是「场均」,国际服(暴雪)给的是生涯累计值。</summary>
    public string TotalTabText { get; set; } = "场均";
    public bool HasPerTen => DetailStatsPerTen.Count > 0;
}

// 通用「名称:值」条目(英雄详情场均属性)
public sealed class StatItem
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class MatchRecord
{
    public string Result { get; set; } = "W";         // W / L / D
    public string ResultText => Result switch { "W" => "胜", "L" => "负", _ => "平" };
    public string ScoreText { get; set; } = "";       // 比分,例:2-0
    public string Mode { get; set; } = "";
    public string Map { get; set; } = "";
    public string? MapIconLocal { get; set; }         // 地图缩略图本地路径
    public string Hero { get; set; } = "";
    public string? HeroIconLocal { get; set; }        // 英雄小图标本地路径
    public string Kda { get; set; } = "";
    public string TimeAgo { get; set; } = "";
    public string MatchId { get; set; } = "";         // 用于拉单局详情
}

// ===== 英雄排行榜(该玩家各英雄的省内排名)=====
public sealed class BillboardRow
{
    public int Order { get; set; }                    // 展示序号 1,2,3…
    public string HeroName { get; set; } = "";
    public string? HeroIconLocal { get; set; }
    public string RankText { get; set; } = "";        // 省内第 91 名
    public string ScoreText { get; set; } = "";       // 3605 分
    public string Extra { get; set; } = "";           // 10 场 · 60%
}

public sealed class BillboardData
{
    public string Title { get; set; } = "英雄排行榜";
    public string SubTitle { get; set; } = "";        // 玩家名 · 省
    public List<BillboardRow> Rows { get; set; } = new();
}

// ===== 我的好友 =====
public sealed class FriendData
{
    public int Total { get; set; }
    public List<FriendRow> Rows { get; set; } = new();
}

public sealed class FriendRow
{
    public long BnetId { get; set; }                  // 点击查该好友
    public string Name { get; set; } = "";            // BattleTag
    public string? AvatarLocal { get; set; }
    public string TierText { get; set; } = "";        // 段位
    public string? TierIconLocal { get; set; }
    public string StatText { get; set; } = "";        // 78场 · 胜率49% · 最高连胜7
}

// ===== 单局详情(记分板)=====
public sealed class MatchPlayer
{
    public long BnetId { get; set; }                  // 点击可查该玩家
    public string Name { get; set; } = "";
    public string? HeroIconLocal { get; set; }
    public string Kad { get; set; } = "";             // K/A/D(击杀/助攻/死亡)
    public string Damage { get; set; } = "";          // 伤害
    public string Cure { get; set; } = "";            // 治疗
    public string Resist { get; set; } = "";          // 抵挡
    public bool DamageMax { get; set; }               // 全场最高伤害
    public bool CureMax { get; set; }                 // 全场最高治疗
    public bool ResistMax { get; set; }               // 全场最高抵挡
    public bool WorthMax { get; set; }                // 全场最高身价(角斗)
    public string TierText { get; set; } = "";        // 段位
    public string? TierIconLocal { get; set; }        // 段位图标
    public string FinalHit { get; set; } = "";        // 最后一击次数
    public string TargetTime { get; set; } = "";      // 占点/目标争夺时间 mm:ss
    public string HealingTaken { get; set; } = "";    // 本局受到治疗
    public string DamageTaken { get; set; } = "";     // 本局受到伤害
    public bool IsMe { get; set; }                    // 被查玩家(高亮)
    public Brush? GroupColor { get; set; }            // 组队颜色(同队开黑的同色圆点),null=单排
    public bool HasGroup => GroupColor != null;
    // 角斗专属:
    public string Worth { get; set; } = "";           // 金钱/身价
    public bool HasWorth => !string.IsNullOrEmpty(Worth);
    public string WorthMaxMark => WorthMax ? " ★最高" : "";  // 全场最高身价标记
    public List<SkillIcon> Skills { get; set; } = new(); // 天赋(异能)+ 道具
    public bool HasSkills => Skills.Count > 0;
}

// 角斗回合(每回合比分)
public sealed class RoundInfo
{
    public string Label { get; set; } = "";           // 第1回合
    public string ScoreText { get; set; } = "";       // 1-0
    public bool IsWin { get; set; }
}

// 角斗天赋/道具图标(点击弹窗看大图+效果)
public sealed class SkillIcon
{
    public string? IconLocal { get; set; }
    public string Name { get; set; } = "";
    public bool IsTrait { get; set; }                 // true=天赋(异能),false=道具
    public string Desc { get; set; } = "";            // 技能效果描述
    public string Category { get; set; } = "";        // 异能/装置/技能
    public string Level { get; set; } = "";           // 品级(稀有…)
    public string TypeLabel => IsTrait ? "天赋" : (string.IsNullOrEmpty(Category) ? "道具" : Category);
}

// 被查玩家本局用的英雄(出战占比 + 独立数据)
public sealed class MatchHeroUse
{
    public string HeroName { get; set; } = "";
    public string? HeroIconLocal { get; set; }
    public string UseRateText { get; set; } = "";     // 出战占比 78%
    public string TimeText { get; set; } = "";        // 游戏时间 07:41
    public List<StatItem> Stats { get; set; } = new();// 英雄独立数据(影重置/反弹伤害…)
}

public sealed class MatchDetail
{
    public string MapName { get; set; } = "";
    public string? MapIconLocal { get; set; }
    public string ModeText { get; set; } = "";        // 运载目标 / 占领要点
    public string StartText { get; set; } = "";       // 开始时间
    public string EndText { get; set; } = "";         // 结束时间
    public string Duration { get; set; } = "";        // 对局时长 mm:ss
    public string ScoreText { get; set; } = "";       // 1 : 0
    public string ResultText { get; set; } = "";      // 胜利/失败
    public bool IsWin { get; set; }
    public List<string> TeamBanIcons { get; set; } = new();   // 我方禁用英雄图标(本地路径)
    public List<string> EnemyBanIcons { get; set; } = new();  // 敌方禁用英雄图标
    public bool HasBans => TeamBanIcons.Count > 0 || EnemyBanIcons.Count > 0;
    public List<MatchHeroUse> MyHeroes { get; set; } = new(); // 被查玩家英雄面板
    public bool HasMyHeroes => MyHeroes.Count > 0;            // 角斗无此面板
    public List<RoundInfo> Rounds { get; set; } = new();      // 角斗多回合
    public bool HasRounds => Rounds.Count > 0;
    public List<MatchPlayer> Teammates { get; set; } = new();
    public List<MatchPlayer> Enemies { get; set; } = new();
}

// ===== 演示数据源(F5 直接看界面效果;真实数据走 StatsMapper) =====
public static class DemoStatsProvider
{
    public static PlayerStats GetDemo(Func<string, Brush?> findBrush)
    {
        return new PlayerStats
        {
            Initial = "初",
            DisplayName = "初次见面我叫源氏",
            TagSuffix = "#1234",
            HasTitle = true,
            TitleText = "高端局",
            EndorseLevel = 4,
            TotalHours = "812 小时",
            WeekPerf = "优秀",
            WeekDetail = "近期 · 胜率60% · 场均伤害9,001 · 场均击杀20 · 场均治疗1,200",
            ReputeComment = "良好",
            ReputeText = "获赞 21 · 送赞 62 · 被举报 2",
            UpdatedAt = "今天 14:02",
            Tank = new RoleRank { TierText = "大师 4", ScoreText = "3512 分 · 最高 3560", ExtraText = "121场 · 胜率52%\nKDA 3.4 · 连胜6", TierBrush = findBrush("Stats.TierMaster") },
            Dps = new RoleRank { TierText = "宗师 5", ScoreText = "3890 分 · 最高 3924", ExtraText = "203场 · 胜率57%\nKDA 4.1 · 连胜8", TierBrush = findBrush("Stats.TierGm"), Highlight = true },
            Support = new RoleRank { TierText = "钻石 2", ScoreText = "3120 分 · 最高 3255", ExtraText = "88场 · 胜率49%\nKDA 5.2 · 连胜4", TierBrush = findBrush("Stats.TierDiamond") },
            AvgDamage = "8,432",
            AvgHeal = "2,105",
            AvgResist = "1,204",
            Kda = "3.42",
            SeasonWinRate = "57%",
            WinLossText = "69胜 60负",
            AvgExtra = "场均单独消灭 3 · 场均最后一击 12",
            Heroes = new()
            {
                new HeroStat { Name = "源氏", Detail = "87h · 58%", PlayPercent = 78 },
                new HeroStat { Name = "温斯顿", Detail = "54h · 55%", PlayPercent = 52 },
                new HeroStat { Name = "安娜", Detail = "41h · 53%", PlayPercent = 40 },
            },
            Matches = new()
            {
                new MatchRecord { Result = "W", ScoreText = "2-0", Mode = "竞技", Map = "漓江塔", Hero = "源氏", Kda = "24/8/6", TimeAgo = "1h前" },
                new MatchRecord { Result = "L", ScoreText = "1-2", Mode = "竞技", Map = "阿努比斯", Hero = "温斯顿", Kda = "11/9/14", TimeAgo = "昨天" },
            }
        };
    }
}

// 对局详情演示数据(--matchdemo 验证记分板布局不崩)
public static class DemoMatchProvider
{
    public static MatchDetail Get()
    {
        Brush g1 = new SolidColorBrush(Color.FromRgb(0xE8, 0x93, 0x3D)); g1.Freeze();
        var md = new MatchDetail
        {
            MapName = "漓江塔", ModeText = "占领要点", ScoreText = "2 : 1", ResultText = "胜利", IsWin = true,
            StartText = "07-05 21:51", EndText = "22:02", Duration = "11:16",
            Rounds = { new RoundInfo { Label = "第1回合", ScoreText = "1-0", IsWin = true },
                       new RoundInfo { Label = "第2回合", ScoreText = "0-1", IsWin = false } },
        };
        MatchPlayer P(string n, bool me = false, Brush? grp = null, bool worthMax = false) => new()
        {
            Name = n, IsMe = me, GroupColor = grp, Kad = "14/8/6", Damage = "6,464", Cure = "184", Resist = "652",
            TierText = "钻石3", FinalHit = "9", TargetTime = "00:16", HealingTaken = "2,114", DamageTaken = "4,940",
            Worth = worthMax ? "56,474" : "", WorthMax = worthMax,
        };
        md.Teammates.Add(P("我有一剑可定千军", me: true));
        md.Teammates.Add(P("阿蒋", grp: g1));
        md.Teammates.Add(P("鱼没有脚", grp: g1));
        md.Teammates.Add(P("Mirage"));
        md.Teammates.Add(P("tree"));
        for (int i = 0; i < 5; i++) md.Enemies.Add(P("敌人" + (i + 1)));
        return md;
    }
}

// ===== 英雄时长条形图:百分比 → 宽度 =====
public sealed class PercentToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double percent = value is double d ? d : 0;
        double max = parameter is string s && double.TryParse(s, out var m) ? m : 120;
        return Math.Max(0, Math.Min(100, percent)) / 100.0 * max;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// ===== 英雄时长条形图(轨道宽度随窗口变):(百分比, 轨道实际宽度) → 填充宽度 =====
// PercentToWidthConverter 那套要预先知道轨道有多宽,轨道比参数窄的时候会被 MaxWidth 一律夹成满格。
// 国际服窗的占比列只有 100 多像素,必须按轨道实际宽度算。
public sealed class PercentBarWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double percent = values.Length > 0 && values[0] is double d ? d : 0;
        double track = values.Length > 1 && values[1] is double w && !double.IsNaN(w) ? w : 0;
        return Math.Max(0, Math.Min(100, percent)) / 100.0 * track;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// ===== 反向 bool → Visibility(true→Collapsed)=====
public sealed class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// ===== 本地图片路径 → ImageSource(空/失败返回 null,UI 兜底)=====
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
