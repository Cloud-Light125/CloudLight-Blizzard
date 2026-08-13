using System.IO;
using System.Text.Json;

namespace BnetSwitch.Services.OverwatchRegion;

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
                var root = NormalizeCandidate(candidate);
                if (OverwatchRegionManager.IsValidGameRoot(root)) return root;
            }
        }
        return null;
    }

    private static IEnumerable<string> ReadCandidates(string file)
    {
        if (!File.Exists(file)) yield break;
        var text = File.ReadAllText(file);
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
