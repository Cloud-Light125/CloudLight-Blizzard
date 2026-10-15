using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloudLightBlizzard.Services;

public sealed record AccountProfileMeta(long AccountId, string BattleTag, DateTime SavedAtUtc, bool Expired = false);

public enum AccountProfileState { Missing, Valid, Legacy, Corrupt, Unavailable }
public sealed record AccountProfileStatus(AccountProfileState State, AccountProfileMeta? Meta, string Detail)
{
    public bool CanRestore => State is AccountProfileState.Valid or AccountProfileState.Legacy;
}

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
    private const int SupportedManifestVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly BattleNetPaths _paths;

    // 仅供隔离自测故障注入；生产代码默认 null。
    internal static Func<string, Exception?>? RestoreMutationFailureForSelfTest { get; set; }
    internal static Func<string, Exception?>? RestorePreparationFailureForSelfTest { get; set; }
    internal static Func<string, string, Exception?>? SaveFailureForSelfTest { get; set; }

    public string Root { get; }

    public AppDataStore(BattleNetPaths paths, string? root = null)
    {
        _paths = paths;
        Root = Path.GetFullPath(root ?? AppPaths.Current.AccountsDir);
    }

    private string Dir(long id) => Path.Combine(Root, id.ToString());
    private string DataDir(long id) => Path.Combine(Dir(id), "BattleNet");
    private string MetaFile(long id) => Path.Combine(Dir(id), "meta.json");
    private string ManifestFile(long id) => Path.Combine(Dir(id), "manifest.json");
    // 与 Root 同级，避免恢复 journal 被 ReadAllManagedPaths 当作账号备份扫描。
    private string RecoveryRoot => Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar) + ".recovery";

    public bool HasProfile(long id) => InspectProfile(id).CanRestore;

    public AccountProfileStatus InspectProfile(long id)
    {
        try
        {
            if (!File.GetAttributes(Root).HasFlag(FileAttributes.Directory)) // Exists() hides access failures.
                throw new IOException("账号快照根路径被文件占用。");
            try { _ = File.GetAttributes(Dir(id)); }
            catch (FileNotFoundException) { return new(AccountProfileState.Missing, null, "未保存；请登录该账号后创建快照。"); }
            catch (DirectoryNotFoundException) { return new(AccountProfileState.Missing, null, "未保存；请登录该账号后创建快照。"); }
            var (meta, _, legacy) = ValidateSnapshotDirectory(Dir(id), id);
            return new(legacy ? AccountProfileState.Legacy : AccountProfileState.Valid, meta,
                legacy ? "旧格式快照已通过兼容校验。" : "账号快照完整性校验通过。");
        }
        catch (InvalidDataException ex) { return new(AccountProfileState.Corrupt, ReadMeta(id), ex.Message + "\n位置：" + Dir(id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(AccountProfileState.Unavailable, ReadMeta(id), "备份目录或文件不可用：" + ex.Message + "\n位置：" + Dir(id));
        }
    }

    public IReadOnlyList<long> ReadProfileIds()
    {
        try
        {
            EnsureNoReparsePath(Root, true, "账号快照目录");
            if (!File.GetAttributes(Root).HasFlag(FileAttributes.Directory))
                throw new IOException("账号快照根路径被文件占用：" + Root);
            return Directory.EnumerateDirectories(Root)
                .Select(path => Path.GetFileName(path))
                .Where(name => long.TryParse(name, out var id) && id > 0 && name == id.ToString(CultureInfo.InvariantCulture))
                .Select(name => long.Parse(name, CultureInfo.InvariantCulture)).ToList();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { throw new IOException("账号快照根目录不存在或不可用：" + Root, ex); }
    }

    public AccountProfileMeta? ReadMeta(long id)
    {
        try
        {
            EnsureNoReparsePath(MetaFile(id), true, "账号元数据");
            return JsonSerializer.Deserialize<AccountProfileMeta>(File.ReadAllText(MetaFile(id)));
        }
        catch { return null; }
    }

    public BattleNetAccountSnapshotManifest? ReadManifest(long id)
    {
        var text = ReadOptionalFile(ManifestFile(id));
        return text is null ? null : CanonicalizeManifest(ParseManifestJson(text, ManifestFile(id)));
    }

    public IReadOnlyList<AccountProfileMeta> ReadAllMeta()
    {
        var result = new List<AccountProfileMeta>();
        foreach (var id in ReadProfileIds())
        {
            var status = InspectProfile(id);
            if (status.CanRestore && status.Meta is not null) result.Add(status.Meta);
        }
        return result.GroupBy(m => m.AccountId).Select(g => g.OrderByDescending(m => m.SavedAtUtc).First()).ToList();
    }

    public void Save(long accountId, string battleTag, bool expired = false)
    {
        if (accountId <= 0) throw new ArgumentException("账号 ID 必须是有效的正整数。");
        var profile = Dir(accountId);
        var staging = profile + ".staging-" + Guid.NewGuid().ToString("N");
        var previous = profile + ".previous-" + Guid.NewGuid().ToString("N");
        var stagingData = Path.Combine(staging, "BattleNet");
        var stage = "检查源目录";
        try
        {
            EnsureNoPendingRestoreJournal();
            if (!Directory.Exists(_paths.RoamingDir))
                throw new DirectoryNotFoundException("原始账号配置目录缺失；请在 Battle.net 登录后重试。");
            if (IsWithinRoot(_paths.RoamingDir, Root) || IsWithinRoot(Root, _paths.RoamingDir))
                throw new InvalidDataException("账号快照目录不能与 Battle.net 源目录重叠。");
            EnsureNoReparsePath(Root, true, "账号快照目标目录");
            EnsureNoReparsePath(profile, true, "已有账号快照");
            stage = "创建 staging";
            InjectSaveFailure(stage, staging);
            Directory.CreateDirectory(stagingData);
            stage = "复制账号配置";
            var manifest = BuildManifestAndCopy(stagingData);
            InjectSaveFailure(stage, staging);
            stage = "写入 manifest";
            InjectSaveFailure(stage, staging);
            WriteDurableText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
            stage = "写入 meta";
            InjectSaveFailure(stage, staging);
            WriteDurableText(Path.Combine(staging, "meta.json"), JsonSerializer.Serialize(
                new AccountProfileMeta(accountId, battleTag, DateTime.UtcNow, expired), JsonOptions));
            stage = "验证 staging";
            InjectSaveFailure(stage, staging);
            _ = ValidateSnapshotDirectory(staging, accountId);
            stage = "提升正式快照";
            InjectSaveFailure(stage, staging);
            if (Directory.Exists(profile)) Directory.Move(profile, previous);
            try
            {
                stage = "移动 staging";
                InjectSaveFailure("移动 staging", staging);
                Directory.Move(staging, profile);
            }
            catch
            {
                if (!Directory.Exists(profile) && Directory.Exists(previous)) Directory.Move(previous, profile);
                throw;
            }
            // Promotion is the commit point. Cleanup failure must not report a failed save.
            try
            {
                InjectSaveFailure("清理旧快照", previous);
                if (Directory.Exists(previous)) Directory.Delete(previous, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                new AccountSwitchLog().Write("SnapshotCleanupPending", detail: "旧快照保留于：" + previous);
            }
            new AccountSwitchLog().Write("SnapshotSaved", detail: $"files={manifest.ManagedPaths.Count};destination={profile}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var reason = ex is UnauthorizedAccessException ? "目录不可写或文件无读取权限，请检查权限。" :
                ex is InvalidDataException ? "配置或快照校验失败，请重新登录后保存。" :
                ex is DirectoryNotFoundException ? "路径不存在，请检查磁盘和原始配置。" :
                "请检查文件占用、磁盘空间和目录可写权限。";
            var message = $"账号快照保存失败（{stage}）。{reason}\n源目录：{_paths.RoamingDir}\n目标目录：{profile}\n原因：{ex.Message}";
            new AccountSwitchLog().Write("SnapshotSaveFailed", detail: $"stage={stage};type={ex.GetType().Name};source={_paths.RoamingDir};destination={profile}");
            throw new IOException(message, ex);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            // If promotion AND rollback failed, previous is the last good backup.
            // Never delete it from a finally block.
        }
    }

    private static void InjectSaveFailure(string stage, string path)
    {
        if (SaveFailureForSelfTest?.Invoke(stage, path) is { } error) throw error;
    }

    internal static (AccountProfileMeta Meta, BattleNetAccountSnapshotManifest Manifest, bool Legacy)
        ValidateSnapshotDirectory(string profile, long id)
    {
        EnsureNoReparseTree(profile, "账号快照");
        var metaText = ReadOptionalFile(Path.Combine(profile, "meta.json"));
        AccountProfileMeta? meta;
        try { meta = metaText is null ? null : JsonSerializer.Deserialize<AccountProfileMeta>(metaText); }
        catch (JsonException ex) { throw new InvalidDataException("账号元数据损坏。", ex); }
        if (meta is null || id <= 0 || meta.AccountId != id || meta.SavedAtUtc == default)
            throw new InvalidDataException("账号元数据缺失或与快照目录不匹配。");
        var data = Path.Combine(profile, "BattleNet");
        var text = ReadOptionalFile(Path.Combine(profile, "manifest.json"));
        var manifest = CanonicalizeManifest(text is null ? BuildLegacyManifest(data) : ParseManifestJson(text, profile));
        ValidateManifest(data, manifest);
        return (meta, manifest, text is null);
    }

    /// <summary>
    /// 只执行与 Restore 相同的完整只读预检，不修改 Battle.net 目标目录。
    /// </summary>
    public void ValidateProfile(long accountId) => PrepareRestore(accountId);

    public void Restore(long accountId)
    {
        // 正式执行重新预检，并在任何目标写入前 staging 全部源文件。
        var plan = PrepareRestore(accountId);
        ApplyRestore(plan, accountId);
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
        EnsureNoReparsePath(cfg, true, "当前账号指针");
        var value = File.ReadAllText(cfg);
        value = Regex.Replace(value, "(\"SavedAccountNames\"\\s*:\\s*)\"[^\"]*\"", "$1\"\"", RegexOptions.Singleline);
        var staging = cfg + ".pointer-" + Guid.NewGuid().ToString("N") + ".tmp";
        var attributes = File.GetAttributes(cfg);
        try
        {
            WriteDurableBytes(staging, new UTF8Encoding(false).GetBytes(value));
            EnsureNoReparsePath(cfg, true, "当前账号指针");
            ClearReadOnly(cfg);
            File.Replace(staging, cfg, null);
            File.SetAttributes(cfg, attributes);
        }
        catch
        {
            try { File.SetAttributes(cfg, attributes); } catch { }
            throw;
        }
        finally { try { if (File.Exists(staging)) File.Delete(staging); } catch { } }
    }

    private BattleNetAccountSnapshotManifest BuildManifestAndCopy(string destinationRoot)
    {
        EnsureNoReparseTree(_paths.RoamingDir, "Battle.net 配置目录");
        var entries = new List<BattleNetAccountSnapshotEntry>();
        var managed = new List<string>();
        var excluded = new List<string>();

        foreach (var directory in EnumerateDirectoriesSafely(_paths.RoamingDir))
        {
            var relative = Normalize(Path.GetRelativePath(_paths.RoamingDir, directory));
            if (IsExcluded(relative, true)) { excluded.Add(relative + "/"); continue; }
            Directory.CreateDirectory(SafeCombine(destinationRoot, relative));
            entries.Add(new(relative, 0, Directory.GetLastWriteTimeUtc(directory), true));
        }

        foreach (var file in EnumerateFilesSafely(_paths.RoamingDir))
        {
            var relative = Normalize(Path.GetRelativePath(_paths.RoamingDir, file));
            if (IsExcluded(relative, false)) { excluded.Add(relative); continue; }
            var info = new FileInfo(file);
            var hash = ComputeSha256(file);
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
        EnsureNoReparseTree(data, "账号备份目录");
        var entries = EnumerateFilesSafely(data)
            .Where(file => !IsExcluded(Normalize(Path.GetRelativePath(data, file)), false)).Select(file =>
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

    private RestorePlan PrepareRestore(long accountId)
    {
        EnsureNoPendingRestoreJournal();
        var (_, manifest, _) = ValidateSnapshotDirectory(Dir(accountId), accountId);
        var data = DataDir(accountId);
        if (!Directory.Exists(data))
            throw new DirectoryNotFoundException($"账号 {accountId} 还没有本地备份。");
        EnsureNoReparseTree(data, "账号备份目录");

        var targetRoot = Path.GetFullPath(_paths.RoamingDir);
        if (File.Exists(targetRoot))
            throw new InvalidDataException("Battle.net 目标路径是文件，无法恢复。");
        EnsureNoReparsePath(targetRoot, includeLeaf: true, "Battle.net 目标目录");
        EnsureTargetRootCanBeCreated(targetRoot);

        var targetFiles = manifest.Entries.Where(e => !e.IsDirectory)
            .Select(e => Normalize(e.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 管理范围取所有既有账号清单的并集，这样恢复 B 时才能识别并移除 A 的独占残留。
        var managed = ReadAllManagedPaths();
        var deletes = new List<string>();
        foreach (var current in EnumerateManagedFiles(_paths.RoamingDir))
        {
            var relative = Normalize(Path.GetRelativePath(_paths.RoamingDir, current));
            if (!targetFiles.Contains(relative) && managed.Contains(relative))
                deletes.Add(Path.GetFullPath(current));
        }

        var copies = manifest.Entries.Where(e => !e.IsDirectory).Select(entry =>
            new RestoreCopy(entry, SafeCombine(data, entry.RelativePath), SafeCombine(_paths.RoamingDir, entry.RelativePath)))
            .ToList();

        // 目标端的路径冲突也必须在任何写入之前拒绝。
        foreach (var entry in manifest.Entries)
        {
            var destination = SafeCombine(_paths.RoamingDir, entry.RelativePath);
            if (entry.IsDirectory && File.Exists(destination))
                throw new InvalidDataException($"目标路径同时是文件和目录：{entry.RelativePath}");
            if (!entry.IsDirectory && Directory.Exists(destination))
                throw new InvalidDataException($"目标路径同时是目录和文件：{entry.RelativePath}");

            for (var parent = Path.GetDirectoryName(destination);
                 parent is not null && IsWithinRoot(targetRoot, parent);
                 parent = Path.GetDirectoryName(parent))
            {
                if (File.Exists(parent))
                    throw new InvalidDataException($"目标父路径是文件：{Path.GetRelativePath(targetRoot, parent)}");
            }
        }

        var touched = deletes.Concat(copies.Select(c => c.Destination))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var files = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in touched)
        {
            if (Directory.Exists(path))
                throw new InvalidDataException($"目标路径是目录，无法作为文件写入：{path}");
            files[path] = CaptureFile(path);
        }

        var directories = new Dictionary<string, DirectorySnapshot>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(targetRoot))
        {
            foreach (var directory in EnumerateDirectoriesSafely(targetRoot)
                         .Append(targetRoot).Distinct(StringComparer.OrdinalIgnoreCase))
                directories[Path.GetFullPath(directory)] = CaptureDirectory(directory);
        }

        var initialDirectories = directories.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directoryCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetRoot };
        foreach (var destination in copies.Select(copy => Path.GetDirectoryName(copy.Destination))
                     .Concat(manifest.Entries.Where(entry => entry.IsDirectory)
                         .Select(entry => SafeCombine(targetRoot, entry.RelativePath))))
        {
            var parent = destination;
            while (parent is not null && IsWithinRoot(targetRoot, parent))
            {
                directoryCandidates.Add(Path.GetFullPath(parent));
                if (string.Equals(parent, targetRoot, StringComparison.OrdinalIgnoreCase)) break;
                parent = Path.GetDirectoryName(parent);
            }
        }

        return new RestorePlan(targetRoot, manifest, copies, deletes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            files, directories, initialDirectories, directoryCandidates);
    }

    private void ApplyRestore(RestorePlan plan, long accountId)
    {
        var journal = CreateRestoreJournal(plan, accountId);
        try
        {
            EnsureNoReparsePath(plan.TargetRoot, includeLeaf: true, "Battle.net 目标目录");
            Directory.CreateDirectory(plan.TargetRoot);
            foreach (var directory in plan.DirectoryCandidates.OrderBy(path => path.Length))
            {
                EnsureNoReparsePath(directory, true, "恢复目标目录");
                Directory.CreateDirectory(directory);
            }
            foreach (var file in plan.FilesToDelete)
                DeleteRestoreFile(file);
            foreach (var copy in journal.PreparedCopies)
                CopyRestoreFile(copy);
            // Unknown empty directories are not owned by this transaction.
        }
        catch (Exception original)
        {
            var rollbackErrors = Rollback(plan);
            if (rollbackErrors.Count == 0)
            {
                try { DeleteRestoreJournal(journal); }
                catch (Exception cleanupError)
                {
                    throw new IOException($"Battle.net 配置已回滚，但恢复 journal 无法清理，请检查：{journal.Path}",
                        cleanupError);
                }
                throw;
            }

            var evidence = WriteRecoveryEvidence(journal, original, rollbackErrors);
            throw new IOException(
                $"Battle.net 配置恢复失败，文件级回滚也失败；恢复不是 OS 多文件原子事务。请检查恢复证据：{evidence}",
                new AggregateException(new[] { original }.Concat(rollbackErrors)));
        }

        try { DeleteRestoreJournal(journal); }
        catch (Exception cleanupError)
        {
            throw new IOException($"Battle.net 配置已恢复，但恢复 journal 无法清理，请检查：{journal.Path}",
                cleanupError);
        }
    }

    private static void CopyRestoreFile(RestoreCopy copy)
    {
        InjectRestoreMutationFailure(copy.Destination);
        EnsureNoReparsePath(copy.Source, includeLeaf: true, "账号备份源文件");
        EnsureNoReparsePath(copy.Destination, includeLeaf: true, "Battle.net 目标文件");
        ForceCopy(copy.Source, copy.Destination);
        var info = new FileInfo(copy.Destination);
        if (!info.Exists || info.Length != copy.Entry.Size ||
            (copy.Entry.Sha256 is not null && !string.Equals(ComputeSha256(copy.Destination), copy.Entry.Sha256,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"恢复后的文件校验失败：{copy.Entry.RelativePath}");
        File.SetLastWriteTimeUtc(copy.Destination, copy.Entry.LastWriteTimeUtc);
    }

    private static void DeleteRestoreFile(string path)
    {
        InjectRestoreMutationFailure(path);
        EnsureNoReparsePath(path, includeLeaf: true, "Battle.net 目标文件");
        ClearReadOnly(path);
        File.Delete(path);
        if (File.Exists(path)) throw new IOException($"无法删除受管理文件：{path}");
    }


    private static List<Exception> Rollback(RestorePlan plan)
    {
        var errors = new List<Exception>();

        // 先恢复文件内容，再恢复目录结构；被清空的父目录可安全重建。
        foreach (var snapshot in plan.FileSnapshots.Values)
        {
            try { RestoreFile(snapshot); }
            catch (Exception ex) { errors.Add(new IOException($"回滚文件失败：{snapshot.Path}", ex)); }
        }
        foreach (var snapshot in plan.DirectorySnapshots.Values.OrderBy(s => s.Path.Length))
        {
            try
            {
                EnsureNoReparsePath(snapshot.Path, includeLeaf: true, "回滚目录");
                if (File.Exists(snapshot.Path)) throw new IOException("目录位置被文件占用。");
                if (!Directory.Exists(snapshot.Path)) Directory.CreateDirectory(snapshot.Path);
                File.SetAttributes(snapshot.Path, snapshot.Attributes);
                Directory.SetLastWriteTimeUtc(snapshot.Path, snapshot.LastWriteTimeUtc);
            }
            catch (Exception ex) { errors.Add(new IOException($"回滚目录失败：{snapshot.Path}", ex)); }
        }
        foreach (var path in plan.DirectoryCandidates
                     .Where(path => !plan.InitialDirectories.Contains(path))
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.Exists(path)) continue;
                EnsureNoReparsePath(path, includeLeaf: true, "回滚目录");
                if (EnumerateFileSystemEntriesSafely(path, "回滚目录").Any())
                    throw new IOException("新建目录仍包含文件。");
                Directory.Delete(path);
            }
            catch (Exception ex) { errors.Add(new IOException($"清理回滚目录失败：{path}", ex)); }
        }
        return errors;
    }

    private static void RestoreFile(FileSnapshot snapshot)
    {
        EnsureNoReparsePath(snapshot.Path, includeLeaf: true, "回滚文件");
        if (!snapshot.Existed)
        {
            if (Directory.Exists(snapshot.Path)) throw new IOException("文件位置被目录占用。");
            if (File.Exists(snapshot.Path))
            {
                ClearReadOnly(snapshot.Path);
                File.Delete(snapshot.Path);
            }
            return;
        }

        if (Directory.Exists(snapshot.Path)) throw new IOException("文件位置被目录占用。");
        Directory.CreateDirectory(Path.GetDirectoryName(snapshot.Path)!);
        ClearReadOnly(snapshot.Path);
        File.WriteAllBytes(snapshot.Path, snapshot.Content!);
        File.SetAttributes(snapshot.Path, snapshot.Attributes);
        File.SetLastWriteTimeUtc(snapshot.Path, snapshot.LastWriteTimeUtc);
    }

    private RestoreJournalHandle CreateRestoreJournal(RestorePlan plan, long accountId)
    {
        EnsureNoPendingRestoreJournal();
        EnsureNoReparsePath(RecoveryRoot, includeLeaf: true, "恢复 journal 目录");
        Directory.CreateDirectory(RecoveryRoot);

        var transactionDirectory = Path.Combine(RecoveryRoot, "restore-" + Guid.NewGuid().ToString("N"));
        // Only this fresh invocation-owned directory may be removed on preparation failure.
        // Existing recovery entries were rejected above and must never be auto-cleaned.
        if (Directory.Exists(transactionDirectory) || File.Exists(transactionDirectory))
            throw new IOException("恢复事务目录已存在，已中止准备。");
        try
        {
            InjectRestorePreparationFailure("create-directory");
            var snapshotsDirectory = Path.Combine(transactionDirectory, "snapshots");
            Directory.CreateDirectory(snapshotsDirectory);
            var files = new List<RestoreJournalFile>();
            var index = 0;
            foreach (var snapshot in plan.FileSnapshots.Values.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(plan.TargetRoot, snapshot.Path).Replace('\\', '/');
                if (relative.StartsWith("../", StringComparison.Ordinal) || relative == "..")
                    throw new InvalidDataException($"恢复 journal 路径越界：{snapshot.Path}");
                string? snapshotFile = null;
                if (snapshot.Existed)
                {
                    snapshotFile = $"file-{index++:D8}.bin";
                    InjectRestorePreparationFailure("snapshot-" + index);
                    WriteDurableBytes(Path.Combine(snapshotsDirectory, snapshotFile), snapshot.Content!);
                }
                files.Add(new RestoreJournalFile(relative, snapshot.Existed, snapshotFile,
                    snapshot.LastWriteTimeUtc, snapshot.Attributes));
            }

            var directories = plan.DirectorySnapshots.Values
                .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(snapshot => new RestoreJournalDirectory(
                    Path.GetRelativePath(plan.TargetRoot, snapshot.Path).Replace('\\', '/'),
                    snapshot.LastWriteTimeUtc, snapshot.Attributes))
                .ToList();
            var document = new RestoreJournalDocument(
                1, accountId, DateTime.UtcNow, "Preparing", plan.TargetRoot, files, directories, Array.Empty<string>());
            var journalPath = Path.Combine(transactionDirectory, "journal.json");
            InjectRestorePreparationFailure("journal");
            WriteDurableText(journalPath, JsonSerializer.Serialize(document, JsonOptions));
            // Stage and validate every input before the first target delete/write.
            // This also detects locked/unreadable unhashed legacy files up front.
            var prepared = new List<RestoreCopy>();
            var input = Path.Combine(transactionDirectory, "input");
            Directory.CreateDirectory(input);
            foreach (var copy in plan.Copies)
            {
                EnsureNoReparsePath(copy.Source, true, "账号备份源文件");
                var staged = Path.Combine(input, prepared.Count.ToString("D8"));
                InjectRestorePreparationFailure("staging");
                File.Copy(copy.Source, staged);
                if (new FileInfo(staged).Length != copy.Entry.Size ||
                    (copy.Entry.Sha256 is not null && !string.Equals(ComputeSha256(staged), copy.Entry.Sha256, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"恢复 staging 校验失败：{copy.Entry.RelativePath}");
                prepared.Add(copy with { Source = staged });
            }
            // Persist the boundary before ANY target directory creation/delete/overwrite.
            // If this write fails we are still preparing: target has not been touched.
            InjectRestorePreparationFailure("applying-journal");
            ReplaceDurableText(journalPath, JsonSerializer.Serialize(document with { State = "Applying" }, JsonOptions));
            return new RestoreJournalHandle(transactionDirectory, journalPath, prepared);
        }
        catch (Exception original)
        {
            // Every exception in this boundary precedes the first target mutation.
            // Cleanup failure retains the exact path and blocks future restore, never hides it.
            try
            {
                InjectRestorePreparationFailure("cleanup");
                EnsureNoReparsePath(transactionDirectory, true, "本次准备事务目录");
                // Directory.Exists can hide an access error as false; attempt cleanup
                // and only regard a genuinely missing directory as already removed.
                try { Directory.Delete(transactionDirectory, true); }
                catch (DirectoryNotFoundException) { }
            }
            catch (Exception cleanupError)
            {
                throw new IOException($"恢复准备失败，目标尚未修改；本次事务目录无法清理：{transactionDirectory}",
                    new AggregateException(original, cleanupError));
            }
            throw;
        }
    }

    private static void InjectRestorePreparationFailure(string stage)
    {
        var error = RestorePreparationFailureForSelfTest?.Invoke(stage);
        if (error is not null) throw error;
    }

    private static void ReplaceDurableText(string path, string text)
    {
        var temporary = path + ".next";
        WriteDurableText(temporary, text);
        File.Replace(temporary, path, null);
    }

    private static void DeleteRestoreJournal(RestoreJournalHandle journal)
    {
        if (!Directory.Exists(journal.DirectoryPath)) return;
        Directory.Delete(journal.DirectoryPath, true);
        if (Directory.Exists(journal.DirectoryPath))
            throw new IOException($"恢复 journal 目录仍存在：{journal.DirectoryPath}");
    }

    private static string WriteRecoveryEvidence(
        RestoreJournalHandle journal, Exception original, IReadOnlyList<Exception> rollbackErrors)
    {
        try
        {
            var document = new
            {
                FormatVersion = 1,
                State = "RollbackFailed",
                UpdatedAtUtc = DateTime.UtcNow,
                OriginalError = original.ToString(),
                RollbackErrors = rollbackErrors.Select(error => error.ToString()).ToList(),
                Journal = "原始文件字节保存在同目录 snapshots/ 下。",
                Atomicity = "Best-effort file-level rollback; OS multi-file atomicity is not claimed.",
            };
            WriteDurableText(Path.Combine(journal.DirectoryPath, "failure.json"), JsonSerializer.Serialize(document, JsonOptions));
        }
        catch (Exception evidenceError)
        {
            return journal.Path + $"（journal 更新失败：{evidenceError.Message}）";
        }
        return journal.Path;
    }

    internal void EnsureNoPendingRestoreJournal()
    {
        if (string.Equals(Root, AppPaths.Current.AccountsDir, StringComparison.OrdinalIgnoreCase))
            EnsureRecoveryRootEmpty(Path.Combine(AppPaths.Current.LegacyRoot, "accounts.recovery"));
        EnsureRecoveryRootEmpty(RecoveryRoot);
    }

    internal static void EnsureRecoveryRootEmpty(string recoveryRoot)
    {
        EnsureNoReparsePath(recoveryRoot, includeLeaf: true, "恢复 journal 目录");
        FileAttributes attributes;
        try { attributes = File.GetAttributes(recoveryRoot); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if (!attributes.HasFlag(FileAttributes.Directory))
            throw new IOException($"恢复 journal 路径被文件占用：{recoveryRoot}");
        var entries = Directory.EnumerateFileSystemEntries(recoveryRoot).ToList();
        if (entries.Count > 0)
        {
            var pending = entries.First();
            throw new IOException(
                $"检测到未完成的 AppDataStore 恢复 journal，已阻止新的恢复；请先处理：{pending}");
        }
    }

    private static void WriteDurableBytes(string path, byte[] content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content, 0, content.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void WriteDurableText(string path, string content)
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static BattleNetAccountSnapshotManifest ParseManifestJson(string json, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("manifest 顶层必须是 JSON 对象。");
            EnsureUniqueProperties(root, source);

            var version = 1;
            if (TryGetProperty(root, "Version", out var versionProperty))
            {
                if (versionProperty.ValueKind != JsonValueKind.Number || !versionProperty.TryGetInt32(out version))
                    throw new InvalidDataException("manifest Version 不是整数。");
            }
            if (version != SupportedManifestVersion)
                throw new InvalidDataException($"manifest Version={version} 不受支持。");

            if (!TryGetProperty(root, "Entries", out var entriesProperty) || entriesProperty.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("manifest 缺少有效 Entries 数组。");

            var entries = new List<BattleNetAccountSnapshotEntry>();
            foreach (var item in entriesProperty.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("manifest Entries 包含非法项。");
                EnsureUniqueProperties(item, source);
                var relativePath = ReadRequiredString(item, "RelativePath", source);
                var size = ReadRequiredInt64(item, "Size", source);
                var modified = ReadRequiredDateTime(item, "LastWriteTimeUtc", source);
                var isDirectory = ReadRequiredBoolean(item, "IsDirectory", source);
                string? sha256 = null;
                if (TryGetProperty(item, "Sha256", out var hashProperty))
                {
                    if (hashProperty.ValueKind != JsonValueKind.String && hashProperty.ValueKind != JsonValueKind.Null)
                        throw new InvalidDataException("manifest Sha256 必须是字符串或 null。");
                    sha256 = hashProperty.ValueKind == JsonValueKind.String ? hashProperty.GetString() : null;
                }
                entries.Add(new BattleNetAccountSnapshotEntry(relativePath, size, modified, isDirectory, sha256));
            }

            var managed = TryGetProperty(root, "ManagedPaths", out var managedProperty)
                ? ReadStringArray(managedProperty, "ManagedPaths", source)
                : entries.Where(entry => !entry.IsDirectory).Select(entry => entry.RelativePath).ToList();
            var excluded = TryGetProperty(root, "ExcludedPaths", out var excludedProperty)
                ? ReadStringArray(excludedProperty, "ExcludedPaths", source)
                : new List<string>();
            var createdAt = DateTime.UtcNow;
            if (TryGetProperty(root, "CreatedAtUtc", out var createdProperty))
                createdAt = ReadDateTime(createdProperty, "CreatedAtUtc", source);

            return new BattleNetAccountSnapshotManifest
            {
                Version = version,
                CreatedAtUtc = createdAt,
                Entries = entries,
                ManagedPaths = managed,
                ExcludedPaths = excluded,
            };
        }
        catch (InvalidDataException) { throw; }
        catch (JsonException ex) { throw new InvalidDataException($"无法解析 manifest：{source}", ex); }
        catch (FormatException ex) { throw new InvalidDataException($"manifest 字段格式错误：{source}", ex); }
    }

    private static BattleNetAccountSnapshotManifest CanonicalizeManifest(BattleNetAccountSnapshotManifest manifest)
    {
        return new BattleNetAccountSnapshotManifest
        {
            Version = manifest.Version,
            CreatedAtUtc = manifest.CreatedAtUtc,
            Entries = manifest.Entries.Select(entry => new BattleNetAccountSnapshotEntry(
                CanonicalizeRelativePath(entry.RelativePath, entry.IsDirectory, "Entries.RelativePath"),
                entry.Size, entry.LastWriteTimeUtc, entry.IsDirectory, entry.Sha256)).ToList(),
            ManagedPaths = manifest.ManagedPaths.Select(path =>
                CanonicalizeRelativePath(path, false, "ManagedPaths")).ToList(),
            ExcludedPaths = manifest.ExcludedPaths.Select(path =>
                CanonicalizeRelativePath(path, true, "ExcludedPaths")).ToList(),
        };
    }

    private static void ValidateManifest(string data, BattleNetAccountSnapshotManifest manifest)
    {
        if (manifest.Version != SupportedManifestVersion)
            throw new InvalidDataException($"manifest Version={manifest.Version} 不受支持。");
        if (!Directory.Exists(data)) throw new DirectoryNotFoundException("账号备份数据目录不存在。");
        if (!manifest.Entries.Any(entry => !entry.IsDirectory))
            throw new InvalidDataException("账号快照没有可恢复的配置文件；请登录后重新保存。");

        var entriesByPath = new Dictionary<string, BattleNetAccountSnapshotEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Entries)
        {
            var relative = CanonicalizeRelativePath(entry.RelativePath, entry.IsDirectory, "Entries.RelativePath");
            if (!entriesByPath.TryAdd(relative, entry with { RelativePath = relative }))
                throw new InvalidDataException($"manifest Entries 包含重复或大小写冲突路径：{relative}");
            if (entry.Size < 0 || (entry.IsDirectory && entry.Size != 0))
                throw new InvalidDataException($"manifest 大小无效：{relative}");
            if (entry.IsDirectory && entry.Sha256 is not null)
                throw new InvalidDataException($"目录不应包含 Sha256：{relative}");
            if (entry.Sha256 is not null && !IsSha256(entry.Sha256))
                throw new InvalidDataException($"manifest Sha256 格式无效：{relative}");
            if (IsExcluded(relative, entry.IsDirectory))
                throw new InvalidDataException($"被排除路径不能出现在 Entries：{relative}");

            var file = SafeCombine(data, relative);
            EnsureNoReparsePath(file, includeLeaf: true, "账号备份路径");
            if (entry.IsDirectory)
            {
                // Historical writers recorded empty directories without copying
                // them. Files beneath them are still individually mandatory.
                if (File.Exists(file))
                    throw new InvalidDataException($"账号备份目录缺失或类型不符：{relative}");
            }
            else
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length != entry.Size)
                    throw new InvalidDataException($"账号备份不完整：{relative}");
                if (entry.Sha256 is not null && !string.Equals(ComputeSha256(file), entry.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"账号备份校验失败：{relative}");
            }
        }

        // 同一路径重复/大小写冲突已由字典处理；只有“文件”不能再作为其他条目的父路径。
        foreach (var path in entriesByPath.Keys)
        {
            var parent = Path.GetDirectoryName(path.Replace('/', Path.DirectorySeparatorChar));
            while (!string.IsNullOrEmpty(parent))
            {
                var normalizedParent = Normalize(parent);
                if (entriesByPath.TryGetValue(normalizedParent, out var parentEntry) && !parentEntry.IsDirectory)
                    throw new InvalidDataException($"manifest 存在 file/dir 冲突：{normalizedParent}");
                parent = Path.GetDirectoryName(parent);
            }
        }

        var filePaths = entriesByPath.Values.Where(entry => !entry.IsDirectory)
            .Select(entry => entry.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in manifest.ManagedPaths)
        {
            var canonical = CanonicalizeRelativePath(path, false, "ManagedPaths");
            if (!managed.Add(canonical))
                throw new InvalidDataException($"ManagedPaths 包含重复或大小写冲突路径：{canonical}");
            if (IsExcluded(canonical, false))
                throw new InvalidDataException($"ManagedPaths 不能包含排除路径：{canonical}");
        }
        if (!managed.SetEquals(filePaths))
            throw new InvalidDataException("ManagedPaths 与 Entries 文件集合不一致。");

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in manifest.ExcludedPaths)
        {
            var canonical = CanonicalizeRelativePath(path, true, "ExcludedPaths");
            if (!excluded.Add(canonical))
                throw new InvalidDataException($"ExcludedPaths 包含重复或大小写冲突路径：{canonical}");
            if (!IsExcluded(canonical, false) && !IsExcluded(canonical, true))
                throw new InvalidDataException($"ExcludedPaths 包含不符合排除规则的路径：{canonical}");
            if (entriesByPath.Keys.Any(entry => IsSameOrDescendant(entry, canonical)))
                throw new InvalidDataException($"ExcludedPaths 与 Entries 冲突：{canonical}");
        }
        foreach (var path in managed)
            if (excluded.Any(excludedPath => IsSameOrDescendant(path, excludedPath)))
                throw new InvalidDataException($"ManagedPaths 与 ExcludedPaths 冲突：{path}");
    }

    private HashSet<string> ReadAllManagedPaths()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in Directory.EnumerateDirectories(Root))
        {
            // Only committed account directories own managed paths; staging,
            // previous and nested user files must never expand deletion scope.
            if (!long.TryParse(Path.GetFileName(profile), out var id) || id <= 0 ||
                Path.GetFileName(profile) != id.ToString(CultureInfo.InvariantCulture)) continue;
            try
            {
                var (_, manifest, _) = ValidateSnapshotDirectory(profile, id);
                foreach (var path in manifest.ManagedPaths) result.Add(path);
            }
            catch
            {
                // Other corrupt/unreadable profiles cannot expand deletion scope.
                // The requested profile has already passed strict validation.
            }
        }
        return result;
    }

    private static IEnumerable<string> EnumerateManagedFiles(string root) =>
        Directory.Exists(root)
            ? EnumerateFilesSafely(root)
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

    private static IReadOnlyList<string> EnumerateDirectoriesSafely(string root)
    {
        var result = new List<string>();
        if (!Directory.Exists(root)) return result;
        EnsureNoReparsePath(root, includeLeaf: true, "递归扫描根目录");
        Visit(root);
        return result;

        void Visit(string directory)
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                EnsureNoReparsePath(child, includeLeaf: true, "递归扫描目录");
                result.Add(Path.GetFullPath(child));
                Visit(child);
            }
        }
    }

    private static IReadOnlyList<string> EnumerateFilesSafely(string root)
    {
        var result = new List<string>();
        if (!Directory.Exists(root)) return result;
        EnsureNoReparsePath(root, includeLeaf: true, "递归扫描根目录");
        Visit(root);
        return result;

        void Visit(string directory)
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(directory))
            {
                EnsureNoReparsePath(child, includeLeaf: true, "递归扫描路径");
                if (Directory.Exists(child)) Visit(child);
                else result.Add(Path.GetFullPath(child));
            }
        }
    }

    private static IEnumerable<string> EnumerateFileSystemEntriesSafely(string directory, string purpose)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            EnsureNoReparsePath(entry, includeLeaf: true, purpose);
            yield return entry;
        }
    }

    private static void EnsureNoReparseTree(string root, string purpose)
    {
        EnsureNoReparsePath(root, true, purpose);
        _ = EnumerateFilesSafely(root);
    }

    private static void EnsureNoReparsePath(string path, bool includeLeaf, string purpose)
    {
        var full = Path.GetFullPath(path);
        var current = full;
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                var attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException($"{purpose}包含符号链接或 reparse point，已拒绝：{current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            if (string.Equals(current, Path.GetPathRoot(current), StringComparison.OrdinalIgnoreCase)) break;
            var parent = Path.GetDirectoryName(current);
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
            current = parent;
        }
    }

    private static string SafeCombine(string root, string relative)
    {
        var canonical = CanonicalizeRelativePath(relative, true, "relative path");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                       Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, canonical.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("备份包含不安全的相对路径。");
        return full;
    }

    private static string CanonicalizeRelativePath(string path, bool allowTrailingSeparator, string field)
    {
        if (string.IsNullOrEmpty(path) || path.IndexOf('\0') >= 0)
            throw new InvalidDataException($"{field} 不是安全的相对路径。");
        var value = path.Replace('\\', '/');
        var hadTrailingSeparator = value.EndsWith("/", StringComparison.Ordinal);
        if (value.StartsWith("/", StringComparison.Ordinal) ||
            (hadTrailingSeparator && !allowTrailingSeparator) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            (value.Length >= 2 && value[1] == ':') || Path.IsPathRooted(value))
            throw new InvalidDataException($"{field} 不是安全的相对路径：{path}");

        var segments = value.Split('/');
        if (hadTrailingSeparator) segments = segments[..^1];
        if (segments.Length == 0)
            throw new InvalidDataException($"{field} 不是安全的相对路径。");
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.'))
                throw new InvalidDataException($"{field} 不是安全的相对路径：{path}");
            if (segment.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0 ||
                segment.IndexOf('\0') >= 0 || Path.GetInvalidFileNameChars().Any(segment.Contains))
                throw new InvalidDataException($"{field} 含有非法文件名字符：{path}");
            var device = segment.Split('.')[0];
            if (device.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (device.Length == 4 && (device.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                         device.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 char.IsDigit(device[3])))
                throw new InvalidDataException($"{field} 含有保留设备名：{path}");
        }
        return string.Join('/', segments);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    private static bool IsSameOrDescendant(string path, string ancestor) =>
        string.Equals(path, ancestor, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(ancestor + "/", StringComparison.OrdinalIgnoreCase);

    private static bool IsWithinRoot(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullRoot, fullCandidate, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureTargetRootCanBeCreated(string targetRoot)
    {
        var parent = targetRoot;
        while (!Directory.Exists(parent))
        {
            if (File.Exists(parent))
                throw new InvalidDataException($"Battle.net 目标父路径是文件：{parent}");
            var next = Path.GetDirectoryName(parent);
            if (next is null || string.Equals(next, parent, StringComparison.OrdinalIgnoreCase)) break;
            parent = next;
        }
        if (File.Exists(targetRoot))
            throw new InvalidDataException("Battle.net 目标路径是文件，无法恢复。");
    }

    private static FileSnapshot CaptureFile(string path)
    {
        if (!File.Exists(path)) return new FileSnapshot(path, false, null, default, FileAttributes.Normal);
        try
        {
            return new FileSnapshot(path, true, File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path),
                File.GetAttributes(path));
        }
        catch (Exception ex) { throw new IOException($"无法预检目标文件：{path}", ex); }
    }

    private static DirectorySnapshot CaptureDirectory(string path) =>
        new(path, File.GetAttributes(path), Directory.GetLastWriteTimeUtc(path));

    private static string? ReadOptionalFile(string path)
    {
        EnsureNoReparsePath(path, true, "备份清单");
        try { return File.ReadAllText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) { throw new IOException($"无法读取 manifest：{path}", ex); }
    }

    private static void EnsureUniqueProperties(JsonElement element, string source)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name))
                throw new InvalidDataException($"manifest 包含重复字段：{source}/{property.Name}");
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string ReadRequiredString(JsonElement objectElement, string name, string source)
    {
        if (!TryGetProperty(objectElement, name, out var value) || value.ValueKind != JsonValueKind.String ||
            value.GetString() is null)
            throw new InvalidDataException($"manifest 缺少有效 {name}：{source}");
        return value.GetString()!;
    }

    private static long ReadRequiredInt64(JsonElement objectElement, string name, string source)
    {
        if (!TryGetProperty(objectElement, name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var result))
            throw new InvalidDataException($"manifest 缺少有效 {name}：{source}");
        return result;
    }

    private static bool ReadRequiredBoolean(JsonElement objectElement, string name, string source)
    {
        if (!TryGetProperty(objectElement, name, out var value) ||
            (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
            throw new InvalidDataException($"manifest 缺少有效 {name}：{source}");
        return value.GetBoolean();
    }

    private static DateTime ReadRequiredDateTime(JsonElement objectElement, string name, string source)
    {
        if (!TryGetProperty(objectElement, name, out var value))
            throw new InvalidDataException($"manifest 缺少有效 {name}：{source}");
        return ReadDateTime(value, name, source);
    }

    private static DateTime ReadDateTime(JsonElement value, string name, string source)
    {
        if (value.ValueKind != JsonValueKind.String ||
            !DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var result))
            throw new InvalidDataException($"manifest {name} 不是有效时间：{source}");
        return result;
    }

    private static List<string> ReadStringArray(JsonElement value, string name, string source)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"manifest {name} 不是数组：{source}");
        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is null)
                throw new InvalidDataException($"manifest {name} 含有非法路径：{source}");
            result.Add(item.GetString()!);
        }
        return result;
    }

    private static bool IsSha256(string value)
    {
        if (value.Length != 64) return false;
        foreach (var c in value)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private static void InjectRestoreMutationFailure(string path)
    {
        var failure = RestoreMutationFailureForSelfTest?.Invoke(path);
        if (failure is not null) throw failure;
    }

    private static void ForceCopy(string src, string dst)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        ClearReadOnly(dst);
        File.Copy(src, dst, overwrite: true);
    }

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path)) return;
        var attr = File.GetAttributes(path);
        if (attr.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
    }

    private static string ComputeSha256(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record RestoreCopy(BattleNetAccountSnapshotEntry Entry, string Source, string Destination);

    private sealed record FileSnapshot(string Path, bool Existed, byte[]? Content, DateTime LastWriteTimeUtc,
        FileAttributes Attributes);

    private sealed record DirectorySnapshot(string Path, FileAttributes Attributes, DateTime LastWriteTimeUtc);

    private sealed record RestoreJournalFile(string RelativePath, bool Existed, string? SnapshotFile,
        DateTime LastWriteTimeUtc, FileAttributes Attributes);

    private sealed record RestoreJournalDirectory(string RelativePath, DateTime LastWriteTimeUtc,
        FileAttributes Attributes);

    private sealed record RestoreJournalDocument(int FormatVersion, long AccountId, DateTime CreatedAtUtc,
        string State, string TargetRoot, IReadOnlyList<RestoreJournalFile> Files,
        IReadOnlyList<RestoreJournalDirectory> Directories, IReadOnlyList<string> Errors);

    private sealed record RestoreJournalHandle(string DirectoryPath, string Path, IReadOnlyList<RestoreCopy> PreparedCopies);

    private sealed class RestorePlan
    {
        public string TargetRoot { get; }
        public BattleNetAccountSnapshotManifest Manifest { get; }
        public IReadOnlyList<RestoreCopy> Copies { get; }
        public IReadOnlyList<string> FilesToDelete { get; }
        public IReadOnlyDictionary<string, FileSnapshot> FileSnapshots { get; }
        public IReadOnlyDictionary<string, DirectorySnapshot> DirectorySnapshots { get; }
        public IReadOnlySet<string> InitialDirectories { get; }
        public IReadOnlySet<string> DirectoryCandidates { get; }

        public RestorePlan(string targetRoot, BattleNetAccountSnapshotManifest manifest, IReadOnlyList<RestoreCopy> copies,
            IReadOnlyList<string> filesToDelete, IReadOnlyDictionary<string, FileSnapshot> fileSnapshots,
            IReadOnlyDictionary<string, DirectorySnapshot> directorySnapshots, IReadOnlySet<string> initialDirectories,
            IReadOnlySet<string> directoryCandidates)
        {
            TargetRoot = targetRoot;
            Manifest = manifest;
            Copies = copies;
            FilesToDelete = filesToDelete;
            FileSnapshots = fileSnapshots;
            DirectorySnapshots = directorySnapshots;
            InitialDirectories = initialDirectories;
            DirectoryCandidates = directoryCandidates;
        }
    }
}
