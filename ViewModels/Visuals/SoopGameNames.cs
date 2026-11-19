using System.Text.Json;

namespace CloudLightBlizzard.ViewModels.Visuals;

/// <summary>One directory for all SOOP projections. IDs never depend on a localized title.</summary>
internal static class SoopGameNames
{
    private static readonly Dictionary<string,string> Names = Load();
    private static Dictionary<string,string> Load()
    {
        using var stream = typeof(SoopGameNames).Assembly.GetManifestResourceStream("SoopGameCatalog")!;
        using var document = JsonDocument.Parse(stream);
        return V.Rows(document.RootElement,"retainedVerifiedNames").Concat(document.RootElement.GetProperty("data").EnumerateArray())
            .GroupBy(r=>V.Text(r,"gameIdx")).ToDictionary(g=>g.Key,g=>V.Text(g.Last(),"gameTitle"));
    }
    internal static void Apply(JsonElement state)
    {
        if (V.Get(state,"gameCatalog") is not { ValueKind: JsonValueKind.Object } directory) return;
        foreach (var entry in directory.EnumerateObject())
            if (entry.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.Value.GetString())) Names[entry.Name] = entry.Value.GetString()!;
    }
    internal static string Resolve(string id,string supplied="")
    {
        var key = id.StartsWith("game:",StringComparison.Ordinal) ? id[5..] : id;
        if (Names.TryGetValue(key,out var name)) return key == "12" && name == "오버워치" ? "守望先锋（오버워치）" : name;
        return !string.IsNullOrWhiteSpace(supplied) && supplied != id && !supplied.StartsWith("game:",StringComparison.Ordinal) && supplied != "游戏 " + key ? supplied : $"未知游戏（ID: {key}）";
    }
}
