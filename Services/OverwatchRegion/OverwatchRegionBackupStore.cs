using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudLightBlizzard.Services.OverwatchRegion;

public sealed class OverwatchRegionBackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Root { get; }
    public string GenerationsRoot => Path.Combine(Root, "generations");
    public string StagingRoot => Path.Combine(Root, "staging");
    public string ActiveGenerationFile => Path.Combine(Root, "active-generation.json");
    public string LegacyManifestsRoot => Path.Combine(Root, "manifests");

    public OverwatchRegionBackupStore(string? root = null)
    {
        Root = Path.GetFullPath(root ?? AppPaths.Current.DefaultRegionStorageDir);
        Directory.CreateDirectory(GenerationsRoot);
        Directory.CreateDirectory(StagingRoot);
    }

    public bool HasLegacyData => Directory.Exists(LegacyManifestsRoot) ||
                                 Directory.Exists(Path.Combine(Root, "backups"));
    public string GenerationRoot(string id) => SafeCombine(GenerationsRoot, id);
    public string GenerationManifestFile(string id, OverwatchRegion region) =>
        Path.Combine(GenerationRoot(id), region == OverwatchRegion.China ? "china-manifest.json" : "international-manifest.json");
    public string GenerationFile(string id) => Path.Combine(GenerationRoot(id), "pair.json");
    public string BackupRoot(string id, OverwatchRegion region) => Path.Combine(GenerationRoot(id), "backups",
        region == OverwatchRegion.China ? "china" : "international");
    public string BackupFile(string id, OverwatchRegion region, string relative) =>
        SafeCombine(BackupRoot(id, region), relative);
    public string StagingGenerationRoot(string id) => SafeCombine(StagingRoot, id);
    public string StagingRegionRoot(string id, OverwatchRegion region) => Path.Combine(StagingGenerationRoot(id),
        region == OverwatchRegion.China ? "china" : "international");
    public string PendingFile(string id) => Path.Combine(StagingGenerationRoot(id), "pending.json");
    public string StagingManifestFile(string id, OverwatchRegion region) => Path.Combine(StagingGenerationRoot(id),
        region == OverwatchRegion.China ? "china-manifest.json" : "international-manifest.json");

    public void SaveStagingManifest(string id, OverwatchRegionManifest manifest) =>
        WriteJson(StagingManifestFile(id, manifest.Region), manifest);
    public OverwatchRegionManifest? LoadStagingManifest(string id, OverwatchRegion region) =>
        ReadJson<OverwatchRegionManifest>(StagingManifestFile(id, region));
    public void SavePending(PendingRegionPreparation pending) => WriteJson(PendingFile(pending.GenerationId), pending);

    public PendingRegionPreparation? LoadPending()
    {
        if (!Directory.Exists(StagingRoot)) return null;
        return Directory.EnumerateFiles(StagingRoot, "pending.json", SearchOption.AllDirectories)
            .Select(ReadJson<PendingRegionPreparation>)
            .Where(value => value?.SchemaVersion == 2)
            .OrderByDescending(value => value!.CreatedAtUtc)
            .FirstOrDefault();
    }

    public void SaveGeneration(OverwatchRegionGeneration generation) =>
        WriteJson(GenerationFile(generation.GenerationId), generation);
    public void SaveGenerationManifest(string id, OverwatchRegionManifest manifest) =>
        WriteJson(GenerationManifestFile(id, manifest.Region), manifest);
    public OverwatchRegionGeneration? LoadGeneration(string id)
    {
        var value = ReadJson<OverwatchRegionGeneration>(GenerationFile(id));
        return value?.SchemaVersion == 2 ? value : null;
    }
    public OverwatchRegionManifest? LoadGenerationManifest(string id, OverwatchRegion region)
    {
        var value = ReadJson<OverwatchRegionManifest>(GenerationManifestFile(id, region));
        return value?.SchemaVersion == 2 ? value : null;
    }

    public ActiveGenerationPointer? LoadPointer()
    {
        var value = ReadJson<ActiveGenerationPointer>(ActiveGenerationFile);
        return value?.SchemaVersion == 2 ? value : null;
    }

    public (ActiveGenerationPointer Pointer, OverwatchRegionGeneration Generation)? LoadActive()
    {
        var pointer = LoadPointer();
        if (pointer is null) return null;
        var generation = LoadGeneration(pointer.GenerationId);
        return generation is { State: RegionBackupState.Ready or RegionBackupState.Stale }
            ? (pointer, generation) : null;
    }

    public void Activate(string generationId)
    {
        var previous = LoadPointer()?.GenerationId;
        WriteJson(ActiveGenerationFile, new ActiveGenerationPointer
        {
            GenerationId = generationId,
            PreviousGenerationId = previous == generationId ? null : previous,
        });
    }

    public void DeleteStaging(string id)
    {
        var path = StagingGenerationRoot(id);
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    public void Clear()
    {
        foreach (var directory in new[] { GenerationsRoot, StagingRoot, LegacyManifestsRoot, Path.Combine(Root, "backups") })
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        if (File.Exists(ActiveGenerationFile)) File.Delete(ActiveGenerationFile);
        Directory.CreateDirectory(GenerationsRoot);
        Directory.CreateDirectory(StagingRoot);
    }

    public static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("区服文件记录包含越界路径。");
        return full;
    }

    public static T? ReadJson<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default; }
        catch { return default; }
    }

    public static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temp, path, true);
    }
}
