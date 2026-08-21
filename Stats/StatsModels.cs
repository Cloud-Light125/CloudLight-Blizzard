using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CloudLightBlizzard.Stats;

public sealed class PlayerStats
{
    public string Initial { get; set; } = "?";
    public string? AvatarLocal { get; set; }
    public string DisplayName { get; set; } = "";
    public string TagSuffix { get; set; } = "";
    public int EndorseLevel { get; set; }
    public string TotalHours { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    public RoleRank Tank { get; set; } = new();
    public RoleRank Dps { get; set; } = new();
    public RoleRank Support { get; set; } = new();

    public bool ShowRanks { get; set; } = true;
    public string RankSectionTitle { get; set; } = "当前段位";
    public string AvgSectionTitle { get; set; } = "表现概览";
    public string AvgDamageLabel { get; set; } = "每10分钟伤害";
    public string AvgHealLabel { get; set; } = "每10分钟治疗";
    public string KdaLabel { get; set; } = "KDA";
    public string WinRateLabel { get; set; } = "胜率";
    public string AvgDamage { get; set; } = "";
    public string AvgHeal { get; set; } = "";
    public string Kda { get; set; } = "";
    public string SeasonWinRate { get; set; } = "";
    public string WinLossText { get; set; } = "";
    public string AvgExtra { get; set; } = "";
    public bool HasAvgExtra => !string.IsNullOrEmpty(AvgExtra);

    public List<HeroStat> Heroes { get; set; } = new();
}

public sealed class RoleRank
{
    public string TierText { get; set; } = "";
    public Brush? TierBrush { get; set; }
    public string? TierIconLocal { get; set; }
}

public sealed class HeroStat
{
    public string Name { get; set; } = "";
    public string? IconLocal { get; set; }
    public string Detail { get; set; } = "";
    public long DurationSeconds { get; set; }
    public double WinRateValue { get; set; }
    public int MatchCount { get; set; }
    public double PlayPercent { get; set; }
    public string PlayPercentText => PlayPercent > 0 ? PlayPercent.ToString("0") + "%" : "";
    public string HoursText { get; set; } = "";
    public string WinRateText { get; set; } = "";
    public string MatchsText { get; set; } = "";
    public string LevelText { get; set; } = "";
    public bool HasLevel => !string.IsNullOrEmpty(LevelText);
    public string RankText { get; set; } = "";
    public bool HasRank => !string.IsNullOrEmpty(RankText);
    public List<StatItem> DetailStats { get; set; } = new();
    public List<StatItem> DetailStatsPerTen { get; set; } = new();
    public string TotalTabText { get; set; } = "累计";
    public bool HasPerTen => DetailStatsPerTen.Count > 0;
}

public enum HeroSortField
{
    Name,
    Duration,
    WinRate,
    Matches,
    Usage,
}

public sealed class HeroStatComparer(HeroSortField field, ListSortDirection direction) : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is not HeroStat left) return -1;
        if (y is not HeroStat right) return 1;

        var result = field switch
        {
            HeroSortField.Name => StringComparer.CurrentCulture.Compare(left.Name, right.Name),
            HeroSortField.Duration => left.DurationSeconds.CompareTo(right.DurationSeconds),
            HeroSortField.WinRate => left.WinRateValue.CompareTo(right.WinRateValue),
            HeroSortField.Matches => left.MatchCount.CompareTo(right.MatchCount),
            HeroSortField.Usage => left.PlayPercent.CompareTo(right.PlayPercent),
            _ => 0,
        };
        if (result == 0 && field != HeroSortField.Name)
            result = StringComparer.CurrentCulture.Compare(left.Name, right.Name);
        return direction == ListSortDirection.Descending ? -result : result;
    }
}

public sealed class StatItem
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

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
