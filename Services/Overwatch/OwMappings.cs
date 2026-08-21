namespace CloudLightBlizzard.Services.Overwatch;

/// <summary>暴雪生涯页段位英文名到中文名与主题画刷的映射。</summary>
public static class OwMappings
{
    private static readonly Dictionary<string, (string Cn, string Brush)> RankTable =
        new(StringComparer.OrdinalIgnoreCase)
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
        return RankTable.TryGetValue(rankNameEn, out var value)
            ? (value.Cn, value.Brush)
            : (rankNameEn, "Stats.TierDiamond");
    }
}
