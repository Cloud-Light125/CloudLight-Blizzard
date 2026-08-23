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
    public string PreparationRoot => Path.Combine(Root, "preparation");
    public string PreparationCurrentRoot => Path.Combine(PreparationRoot, "current");
    private string PreparationPreviousRoot => Path.Combine(PreparationRoot, "previous");
    public string PreparationStateFile => Path.Combine(PreparationCurrentRoot, "state.json");
    public string Step1ManifestFile => Path.Combine(PreparationCurrentRoot, "step1-manifest.json");
    public string Step2ManifestFile => Path.Combine(PreparationCurrentRoot, "step2-manifest.json");
    public string CandidateRoot => Path.Combine(PreparationCurrentRoot, "candidate");
    public string ActiveGenerationFile => Path.Combine(Root, "active-generation.json");
    public string LegacyManifestsRoot => Path.Combine(Root, "manifests");

    public OverwatchRegionBackupStore(string? root = null)
    {
        Root = Path.GetFullPath(root ?? AppPaths.Current.DefaultRegionStorageDir);
        Directory.CreateDirectory(GenerationsRoot);
        Directory.CreateDirectory(StagingRoot);
        Directory.CreateDirectory(PreparationRoot);
        RecoverPreparationSwap();
    }

    public bool HasLegacyData => Directory.Exists(LegacyManifestsRoot) ||
                                 Directory.Exists(Path.Combine(Root, "backups"));
    public string GenerationRoot(string id) => SafeCombine(GenerationsRoot, id);
    public string GenerationManifestFile(string id, OverwatchRegion region) =>
        Path.Combine(GenerationRoot(id), region == OverwatchRegion.China ? "china-manifest.json" : "international-manifest.json");
    public string GenerationReferenceManifestFile(string id, OverwatchRegion region) =>
        Path.Combine(GenerationRoot(id), region == OverwatchRegion.China
            ? "china-reference-manifest.json" : "international-reference-manifest.json");
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

    public void SaveVerifiedPending(PendingRegionPreparation pending) => WriteJson(PreparationStateFile, pending);
    public void SavePreparationManifest(int step, OverwatchRegionManifest manifest) =>
        WriteJson(step == 1 ? Step1ManifestFile : Step2ManifestFile, manifest);
    public OverwatchRegionManifest? LoadPreparationManifest(int step) =>
        ReadJson<OverwatchRegionManifest>(step == 1 ? Step1ManifestFile : Step2ManifestFile);
    public string CandidateRegionRoot(OverwatchRegion region) => Path.Combine(CandidateRoot,
        region == OverwatchRegion.China ? "china" : "international");
    public string CandidateFile(OverwatchRegion region, string relative) =>
        SafeCombine(CandidateRegionRoot(region), relative);

    public PendingRegionPreparation? LoadPending()
    {
        var verified = ReadJson<PendingRegionPreparation>(PreparationStateFile);
        var legacy = !Directory.Exists(StagingRoot) ? Enumerable.Empty<PendingRegionPreparation?>() :
            Directory.EnumerateFiles(StagingRoot, "pending.json", SearchOption.AllDirectories)
            .Select(ReadJson<PendingRegionPreparation>)
            .Where(value => value?.SchemaVersion == 2);
        return legacy.Append(verified)
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

    public void Activate(string generationId, OverwatchRegion currentRegion)
    {
        var previous = LoadPointer()?.GenerationId;
        WriteJson(ActiveGenerationFile, new ActiveGenerationPointer
        {
            GenerationId = generationId,
            PreviousGenerationId = previous == generationId ? null : previous,
            LastSuccessfulRegion = currentRegion,
            LastSuccessfulGenerationId = generationId,
        });
    }
    public void SaveGenerationReferenceManifest(string id, OverwatchRegionManifest manifest) =>
        WriteJson(GenerationReferenceManifestFile(id, manifest.Region), manifest);
    public OverwatchRegionManifest? LoadGenerationReferenceManifest(string id, OverwatchRegion region)
    {
        var value = ReadJson<OverwatchRegionManifest>(GenerationReferenceManifestFile(id, region));
        return value?.SchemaVersion == 2 ? value : null;
    }

    public bool SaveLastSuccessfulRegion(string generationId, OverwatchRegion region)
    {
        var pointer = LoadPointer();
        if (pointer is null || !string.Equals(pointer.GenerationId, generationId, StringComparison.OrdinalIgnoreCase))
            return false;
        pointer.LastSuccessfulRegion = region;
        pointer.LastSuccessfulGenerationId = generationId;
        WriteJson(ActiveGenerationFile, pointer);
        return true;
    }

    public void DeleteStaging(string id)
    {
        var path = StagingGenerationRoot(id);
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    public void DeletePreparation()
    {
        if (Directory.Exists(PreparationRoot)) Directory.Delete(PreparationRoot, true);
        Directory.CreateDirectory(PreparationRoot);
    }

    public void CommitPreparationDirectory(string working)
    {
        if (Directory.Exists(PreparationPreviousRoot)) Directory.Delete(PreparationPreviousRoot, true);
        if (Directory.Exists(PreparationCurrentRoot))
            Directory.Move(PreparationCurrentRoot, PreparationPreviousRoot);
        try
        {
            Directory.Move(working, PreparationCurrentRoot);
            try { if (Directory.Exists(PreparationPreviousRoot)) Directory.Delete(PreparationPreviousRoot, true); }
            catch { /* 新 current 已经提交；previous 仅是下次可清理的回滚副本。 */ }
        }
        catch
        {
            if (!Directory.Exists(PreparationCurrentRoot) && Directory.Exists(PreparationPreviousRoot))
                Directory.Move(PreparationPreviousRoot, PreparationCurrentRoot);
            throw;
        }
    }

    private void RecoverPreparationSwap()
    {
        if (!Directory.Exists(PreparationCurrentRoot) && Directory.Exists(PreparationPreviousRoot))
            Directory.Move(PreparationPreviousRoot, PreparationCurrentRoot);
        else if (Directory.Exists(PreparationCurrentRoot) && Directory.Exists(PreparationPreviousRoot))
            try { Directory.Delete(PreparationPreviousRoot, true); } catch { }
    }

    public void ResetVerifiedToStep1(PendingRegionPreparation pending)
    {
        foreach (var path in new[] { Step2ManifestFile, CandidateRoot })
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        foreach (var path in Directory.Exists(PreparationCurrentRoot)
                     ? Directory.EnumerateDirectories(PreparationCurrentRoot, "step2-working-*")
                     : Array.Empty<string>())
            Directory.Delete(path, true);
        pending.Checkpoint = RegionPreparationCheckpoint.Step1Ready;
        pending.CandidateCount = 0;
        pending.CandidateBackupSavedCount = 0;
        pending.CandidateBackups.Clear();
        SaveVerifiedPending(pending);
    }

    public void Clear()
    {
        foreach (var directory in new[] { GenerationsRoot, StagingRoot, PreparationRoot, LegacyManifestsRoot, Path.Combine(Root, "backups") })
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        if (File.Exists(ActiveGenerationFile)) File.Delete(ActiveGenerationFile);
        Directory.CreateDirectory(GenerationsRoot);
        Directory.CreateDirectory(StagingRoot);
        Directory.CreateDirectory(PreparationRoot);
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
