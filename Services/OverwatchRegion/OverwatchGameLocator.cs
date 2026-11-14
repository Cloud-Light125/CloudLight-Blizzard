using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloudLightBlizzard.Services.OverwatchRegion;

public static class OverwatchGameLocator
{
    public static string? Detect(BattleNetPaths paths)
    {
        foreach (var file in new[]
        {
            paths.RoamingConfig,
            Path.Combine(paths.LocalRoot, "Battle.net.config"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Battle.net", "Agent", "product.db"),
        })
        {
            foreach (var candidate in ReadCandidates(file))
            {
                string root;
                try { root = NormalizeCandidate(candidate); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                { continue; }
                if (OverwatchRegionManager.IsValidGameRoot(root)) return root;
            }
        }
        return null;
    }

    private static IEnumerable<string> ReadCandidates(string file)
    {
        if (!File.Exists(file)) yield break;
        string text;
        try { text = File.ReadAllText(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(text); } catch { }
        if (doc is not null)
        {
            using (doc)
                foreach (var value in Strings(doc.RootElement))
                    if (value.Contains("Overwatch", StringComparison.OrdinalIgnoreCase))
                        yield return value;
        }
        else
        {
            // Agent product.db is protobuf, not newline-delimited text. Its
            // length/tag bytes can surround a valid installation path. Extract
            // absolute paths without retaining those control bytes, then keep
            // the executable validation in Detect/NormalizeCandidate.
            foreach (Match match in Regex.Matches(text, @"[A-Za-z]:[\\/][^\x00-\x1F""<>|?*]+"))
                if (match.Value.Contains("Overwatch", StringComparison.OrdinalIgnoreCase))
                    yield return match.Value.Trim();
            foreach (var part in text.Split('\0', '\r', '\n', '"'))
                if (part.Contains("Overwatch", StringComparison.OrdinalIgnoreCase))
                    yield return part.Trim();
        }
    }

    private static IEnumerable<string> Strings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
        }
        else if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                foreach (var value in Strings(property.Value)) yield return value;
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var value in Strings(item)) yield return value;
    }

    private static string NormalizeCandidate(string value)
    {
        value = Environment.ExpandEnvironmentVariables(value.Trim());
        if (!Path.IsPathFullyQualified(value)) return value;
        value = Path.GetFullPath(value);
        if (File.Exists(value)) value = Path.GetDirectoryName(value)!;
        var current = value;
        for (var i = 0; i < 4 && !string.IsNullOrWhiteSpace(current); i++)
        {
            if (OverwatchRegionManager.IsValidGameRoot(current)) return current;
            current = Path.GetDirectoryName(current);
        }
        return value;
    }
}
