using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloudLightBlizzard.Services;

public sealed record AccountProfileMeta(long AccountId, string BattleTag, DateTime SavedAtUtc, bool Expired = false);

public sealed record BattleNetAccountSnapshotEntry(
    string RelativePath,
    long Size,
    DateTime LastWriteTimeUtc,
    bool IsDirectory,
    string? Sha256 = null);

public sealed class BattleNetAccountSnapshotManifest
{
    public int Version { get; init; } = 1;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public List<BattleNetAccountSnapshotEntry> Entries { get; init; } = new();
    public List<string> ManagedPaths { get; init; } = new();
    public List<string> ExcludedPaths { get; init; } = new();
}

/// <summary>
/// 保存 Battle.net 自己留在 %APPDATA%\Battle.net 的账号选择配置。
/// 不读取或修改 UnifiedAuth、密码、注册表及 %LOCALAPPDATA%\Battle.net\Account。
/// </summary>
public sealed class AppDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly BattleNetPaths _paths;

    public string Root { get; }

    public AppDataStore(BattleNetPaths paths, string? root = null)
    {
        _paths = paths;
        Root = root ?? AppPaths.Current.AccountsDir;
        Directory.CreateDirectory(Root);
    }

    private string Dir(long id) => Path.Combine(Root, id.ToString());
    private string DataDir(long id) => Path.Combine(Dir(id), "BattleNet");
    private string MetaFile(long id) => Path.Combine(Dir(id), "meta.json");
    private string ManifestFile(long id) => Path.Combine(Dir(id), "manifest.json");

    public bool HasProfile(long id) => File.Exists(MetaFile(id)) && Directory.Exists(DataDir(id));

    public AccountProfileMeta? ReadMeta(long id)
    {
        try { return JsonSerializer.Deserialize<AccountProfileMeta>(File.ReadAllText(MetaFile(id))); }
        catch { return null; }
    }

    public BattleNetAccountSnapshotManifest? ReadManifest(long id)
    {
        try { return JsonSerializer.Deserialize<BattleNetAccountSnapshotManifest>(File.ReadAllText(ManifestFile(id))); }
        catch { return null; }
    }

    public IReadOnlyList<AccountProfileMeta> ReadAllMeta()
    {
        if (!Directory.Exists(Root)) return Array.Empty<AccountProfileMeta>();
        var result = new List<AccountProfileMeta>();
        foreach (var file in Directory.EnumerateFiles(Root, "meta.json", SearchOption.AllDirectories))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<AccountProfileMeta>(File.ReadAllText(file));
                if (meta is not null) result.Add(meta);
            }
            catch { }
        }
        return result.GroupBy(m => m.AccountId).Select(g => g.OrderByDescending(m => m.SavedAtUtc).First()).ToList();
    }

    public void Save(long accountId, string battleTag)
    {
        if (!Directory.Exists(_paths.RoamingDir))
            throw new DirectoryNotFoundException("未找到 Battle.net 的账号配置目录。");

        var profile = Dir(accountId);
        var staging = profile + ".staging-" + Guid.NewGuid().ToString("N");
        var previous = profile + ".previous-" + Guid.NewGuid().ToString("N");
        var stagingData = Path.Combine(staging, "BattleNet");
        Directory.CreateDirectory(stagingData);

        try
        {
            var manifest = BuildManifestAndCopy(stagingData);
            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
            File.WriteAllText(Path.Combine(staging, "meta.json"), JsonSerializer.Serialize(
                new AccountProfileMeta(accountId, battleTag, DateTime.UtcNow), JsonOptions));

            if (Directory.Exists(profile)) Directory.Move(profile, previous);
            try
            {
                Directory.Move(staging, profile);
                if (Directory.Exists(previous)) Directory.Delete(previous, true);
            }
            catch
            {
                if (!Directory.Exists(profile) && Directory.Exists(previous)) Directory.Move(previous, profile);
                throw;
            }
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            try { if (Directory.Exists(previous)) Directory.Delete(previous, true); } catch { }
        }
    }

    public void Restore(long accountId)
    {
        var data = DataDir(accountId);
        if (!Directory.Exists(data))
            throw new DirectoryNotFoundException($"账号 {accountId} 还没有本地备份。");

        var manifest = ReadManifest(accountId) ?? BuildLegacyManifest(data);
        ValidateManifest(data, manifest);
        Directory.CreateDirectory(_paths.RoamingDir);

        var targetFiles = manifest.Entries.Where(e => !e.IsDirectory)
            .Select(e => Normalize(e.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 管理范围取所有既有账号清单的并集，这样恢复 B 时才能识别并移除 A 的独占残留。
        // 不在任何清单里的未知文件仍然保留。
        var managed = ReadAllManagedPaths();

        // 只清理明确属于账号备份管理范围、但目标备份不存在的文件。未知新文件和排除项一律不碰。
        foreach (var current in EnumerateManagedFiles(_paths.RoamingDir))
        {
            var relative = Normalize(Path.GetRelativePath(_paths.RoamingDir, current));
            if (!targetFiles.Contains(relative) && managed.Contains(relative)) TryDelete(current);
        }

        foreach (var entry in manifest.Entries.Where(e => !e.IsDirectory))
        {
            var source = SafeCombine(data, entry.RelativePath);
            var destination = SafeCombine(_paths.RoamingDir, entry.RelativePath);
            ForceCopy(source, destination);
            File.SetLastWriteTimeUtc(destination, entry.LastWriteTimeUtc);
        }

        RemoveEmptyManagedDirectories(_paths.RoamingDir, manifest);
    }

    public void SetExpired(long accountId, bool expired)
    {
        var meta = ReadMeta(accountId);
        if (meta is null || meta.Expired == expired) return;
        File.WriteAllText(MetaFile(accountId), JsonSerializer.Serialize(meta with { Expired = expired }, JsonOptions));
    }

    public void Delete(long accountId)
    {
        var dir = Dir(accountId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    public void ClearCurrentPointer()
    {
        var cfg = _paths.RoamingConfig;
        if (!File.Exists(cfg)) return;
        ClearReadOnly(cfg);
        var value = File.ReadAllText(cfg);
        value = Regex.Replace(value, "(\"SavedAccountNames\"\\s*:\\s*)\"[^\"]*\"", "$1\"\"", RegexOptions.Singleline);
        File.WriteAllText(cfg, value);
    }

    private BattleNetAccountSnapshotManifest BuildManifestAndCopy(string destinationRoot)
    {
        var entries = new List<BattleNetAccountSnapshotEntry>();
        var managed = new List<string>();
        var excluded = new List<string>();

        foreach (var directory in Directory.EnumerateDirectories(_paths.RoamingDir, "*", SearchOption.AllDirectories))
        {
            var relative = Normalize(Path.GetRelativePath(_paths.RoamingDir, directory));
            if (IsExcluded(relative, true)) { excluded.Add(relative + "/"); continue; }
            entries.Add(new(relative, 0, Directory.GetLastWriteTimeUtc(directory), true));
        }

        foreach (var file in Directory.EnumerateFiles(_paths.RoamingDir, "*", SearchOption.AllDirectories))
        {
            var relative = Normalize(Path.GetRelativePath(_paths.RoamingDir, file));
            if (IsExcluded(relative, false)) { excluded.Add(relative); continue; }
            var info = new FileInfo(file);
            var hash = info.Length <= 4 * 1024 * 1024 ? ComputeSha256(file) : null;
            entries.Add(new(relative, info.Length, info.LastWriteTimeUtc, false, hash));
            managed.Add(relative);
            ForceCopy(file, SafeCombine(destinationRoot, relative));
        }

        return new BattleNetAccountSnapshotManifest
        {
            Entries = entries.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
            ManagedPaths = managed.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
            ExcludedPaths = excluded.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList(),
        };
    }

    private static BattleNetAccountSnapshotManifest BuildLegacyManifest(string data)
    {
        var entries = Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(file =>
        {
            var info = new FileInfo(file);
            return new BattleNetAccountSnapshotEntry(Normalize(Path.GetRelativePath(data, file)), info.Length,
                info.LastWriteTimeUtc, false, info.Length <= 4 * 1024 * 1024 ? ComputeSha256(file) : null);
        }).ToList();
        return new BattleNetAccountSnapshotManifest
        {
            Entries = entries,
            ManagedPaths = entries.Select(e => e.RelativePath).ToList(),
        };
    }

    private HashSet<string> ReadAllManagedPaths()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(Root)) return result;
        foreach (var file in Directory.EnumerateFiles(Root, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<BattleNetAccountSnapshotManifest>(File.ReadAllText(file));
                if (manifest is null) continue;
                foreach (var path in manifest.ManagedPaths) result.Add(Normalize(path));
            }
            catch { }
        }
        foreach (var data in Directory.EnumerateDirectories(Root, "BattleNet", SearchOption.AllDirectories))
            foreach (var file in Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories))
            {
                var relative = Normalize(Path.GetRelativePath(data, file));
                if (!IsExcluded(relative, false)) result.Add(relative);
            }
        return result;
    }

    private static void ValidateManifest(string data, BattleNetAccountSnapshotManifest manifest)
    {
        foreach (var entry in manifest.Entries.Where(e => !e.IsDirectory))
        {
            var file = SafeCombine(data, entry.RelativePath);
            var info = new FileInfo(file);
            if (!info.Exists || info.Length != entry.Size)
                throw new InvalidDataException($"账号备份不完整：{entry.RelativePath}");
            if (entry.Sha256 is not null && !string.Equals(ComputeSha256(file), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"账号备份校验失败：{entry.RelativePath}");
        }
    }

    private static IEnumerable<string> EnumerateManagedFiles(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => !IsExcluded(Normalize(Path.GetRelativePath(root, file)), false))
            : Array.Empty<string>();

    private static bool IsExcluded(string relative, bool isDirectory)
    {
        var path = "/" + Normalize(relative).Trim('/') + "/";
        var name = Path.GetFileName(relative);
        if (path.Contains("/unifiedauth/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/logs/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/log/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/crash/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/crashes/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/cache/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/gpucache/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/code cache/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/temp/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/tmp/", StringComparison.OrdinalIgnoreCase)) return true;
        if (isDirectory) return false;
        var extension = Path.GetExtension(name);
        return extension.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".lock", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".pid", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("LOCK", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("-journal", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("-shm", StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveEmptyManagedDirectories(string root, BattleNetAccountSnapshotManifest manifest)
    {
        var allowed = manifest.Entries.Where(e => e.IsDirectory).Select(e => Normalize(e.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(p => p.Length))
        {
            var relative = Normalize(Path.GetRelativePath(root, directory));
            if (allowed.Contains(relative) || IsExcluded(relative, true)) continue;
            try { if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); } catch { }
        }
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("备份包含不安全的相对路径。");
        return full;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
    private static string ComputeSha256(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void ForceCopy(string src, string dst)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        ClearReadOnly(dst);
        File.Copy(src, dst, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try { ClearReadOnly(path); if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path)) return;
        var attr = File.GetAttributes(path);
        if (attr.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
    }
}
