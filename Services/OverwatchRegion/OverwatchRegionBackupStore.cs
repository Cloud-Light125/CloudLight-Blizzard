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

    public IReadOnlyList<string> EnumerateGenerationIds() =>
        Directory.Exists(GenerationsRoot)
            ? Directory.EnumerateDirectories(GenerationsRoot)
                .Where(path => !IsReparsePoint(path))
                .Select(Path.GetFileName)
                .Where(id => !string.IsNullOrWhiteSpace(id) && IsSafeGenerationId(id!))
                .Cast<string>().ToList()
            : Array.Empty<string>();

    public bool DeleteGeneration(string id)
    {
        if (!IsSafeGenerationId(id)) throw new InvalidDataException("区服快照标识无效。");
        var path = GenerationRoot(id);
        if (!Directory.Exists(path)) return false;
        EnsureNoReparsePointsInTree(path);
        if (LoadGeneration(id) is null)
            throw new InvalidOperationException("指定目录不是 CloudLight Blizzard 管理的区服快照。");
        Directory.Delete(path, true);
        return true;
    }

    public static bool IsSafeGenerationId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 80 &&
        id.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
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
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relative))
            throw new InvalidDataException("区服文件路径不能为空。");

        // Do not repair an absolute or traversing value by trimming separators.
        // A repaired path can turn attacker-controlled input into a different,
        // apparently safe path and is especially dangerous before recursive delete.
        var normalizedRelative = relative.Replace('/', Path.DirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedRelative) || normalizedRelative.Contains(':', StringComparison.Ordinal) ||
            normalizedRelative.StartsWith(Path.DirectorySeparatorChar) ||
            normalizedRelative.Split(Path.DirectorySeparatorChar, StringSplitOptions.None)
                .Any(IsUnsafePathSegment))
            throw new InvalidDataException("区服文件记录包含越界路径。");

        var fullRoot = NormalizeRootPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelative));
        var rootWithSeparator = EnsureTrailingSeparator(fullRoot);
        if (!string.Equals(full, fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("区服文件记录包含越界路径。");
        EnsureNoReparsePointsAlongPath(fullRoot, full);
        return full;
    }

    /// <summary>Enumerates files without following symbolic links, junctions, or other reparse points.</summary>
    public static IReadOnlyList<string> EnumerateFilesWithoutReparse(string root)
    {
        var result = new List<string>();
        if (!Directory.Exists(root)) return result;
        var pending = new Stack<DirectoryInfo>();
        var rootInfo = new DirectoryInfo(Path.GetFullPath(root));
        if (rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) return result;
        pending.Push(rootInfo);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in directory.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (entry is DirectoryInfo child) pending.Push(child);
                else if (entry is FileInfo file) result.Add(file.FullName);
            }
        }
        return result;
    }

    private static bool IsUnsafePathSegment(string segment) =>
        string.IsNullOrEmpty(segment) || segment is "." or ".." ||
        segment.Length >= 2 && segment.All(character => character == '.');

    private static bool IsReparsePoint(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch { return true; }
    }

    private static void EnsureNoReparsePointsAlongPath(string root, string candidate)
    {
        var fullRoot = NormalizeRootPath(root);
        var fullCandidate = Path.GetFullPath(candidate);
        var rootWithSeparator = EnsureTrailingSeparator(fullRoot);
        if (!string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("区服文件记录包含越界路径。");

        var current = fullRoot;
        if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
            throw new InvalidDataException("区服管理根目录不能是符号链接或 Junction。");
        var relative = Path.GetRelativePath(fullRoot, fullCandidate);
        if (relative == ".") return;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current)) break;
            if (IsReparsePoint(current))
                throw new InvalidDataException("区服文件路径经过符号链接或 Junction，已拒绝访问。");
        }
    }

    private static string NormalizeRootPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var fileSystemRoot = Path.GetPathRoot(fullPath);
        return !string.IsNullOrEmpty(fileSystemRoot) &&
               string.Equals(fullPath, fileSystemRoot, StringComparison.OrdinalIgnoreCase)
            ? fileSystemRoot
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path : path + Path.DirectorySeparatorChar;

    private static void EnsureNoReparsePointsInTree(string root)
    {
        if (IsReparsePoint(root))
            throw new InvalidDataException("区服快照目录不能是符号链接或 Junction。");
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in directory.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("区服快照包含符号链接或 Junction，已拒绝删除。");
                if (entry is DirectoryInfo child) pending.Push(child);
            }
        }
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
