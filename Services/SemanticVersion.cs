namespace CloudLightBlizzard.Services;

internal readonly record struct SemanticVersion(int Major, int Minor, int Patch, IReadOnlyList<string> PreRelease)
    : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim().TrimStart('v', 'V');
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];
        var dash = text.IndexOf('-');
        var core = dash >= 0 ? text[..dash] : text;
        var pre = dash >= 0 ? text[(dash + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
        var parts = core.Split('.');
        var minor = 0;
        var patch = 0;
        if (parts.Length is < 1 or > 3 || !int.TryParse(parts[0], out var major) ||
            (parts.Length > 1 && !int.TryParse(parts[1], out minor)) ||
            (parts.Length > 2 && !int.TryParse(parts[2], out patch))) return false;
        result = new SemanticVersion(major, minor, patch, pre);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core == 0) core = Minor.CompareTo(other.Minor);
        if (core == 0) core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        if (PreRelease.Count == 0) return other.PreRelease.Count == 0 ? 0 : 1;
        if (other.PreRelease.Count == 0) return -1;
        for (var i = 0; i < Math.Max(PreRelease.Count, other.PreRelease.Count); i++)
        {
            if (i >= PreRelease.Count) return -1;
            if (i >= other.PreRelease.Count) return 1;
            var leftNumeric = int.TryParse(PreRelease[i], out var left);
            var rightNumeric = int.TryParse(other.PreRelease[i], out var right);
            var part = leftNumeric && rightNumeric ? left.CompareTo(right)
                : leftNumeric ? -1 : rightNumeric ? 1
                : string.CompareOrdinal(PreRelease[i], other.PreRelease[i]);
            if (part != 0) return part;
        }
        return 0;
    }
}
