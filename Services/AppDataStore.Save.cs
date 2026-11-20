using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CloudLightBlizzard.Services;

public sealed partial class AppDataStore
{
    private const int AccessDenied = 5;
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    public AccountProfileStatus Save(long accountId, string battleTag, bool expired = false)
    {
        if (accountId <= 0) throw new ArgumentException("账号 ID 必须是有效的正整数。");
        var profile = Dir(accountId);
        var staging = profile + ".staging-" + Guid.NewGuid().ToString("N");
        var previous = profile + ".previous-" + Guid.NewGuid().ToString("N");
        var stage = "检查源目录";
        var operationPath = _paths.RoamingDir;
        var ownsStaging = false;
        SnapshotLock? snapshotLock = null;
        var log = new AccountSwitchLog();
        try
        {
            snapshotLock = AcquireSnapshotLock(Root);
            EnsureNoPendingRestoreJournal();
            if (!Directory.Exists(_paths.RoamingDir))
                throw new DirectoryNotFoundException("原始账号配置目录缺失；请在 Battle.net 登录后重试。");
            if (IsWithinRoot(_paths.RoamingDir, Root) || IsWithinRoot(Root, _paths.RoamingDir))
                throw new InvalidDataException("账号快照目录不能与 Battle.net 源目录重叠。");

            stage = "检查目标目录";
            operationPath = Root;
            EnsureNoReparsePath(Root, true, "账号快照目标目录");
            Directory.CreateDirectory(Root);
            ProbeSnapshotDestination(Root, path => { operationPath = path; stage = "检查临时目录重命名"; });
            stage = "恢复上次保存";
            operationPath = profile;
            RecoverPreviousIfMissing(Root, accountId);
            EnsureNoReparsePath(profile, true, "已有账号快照");
            if (File.Exists(profile))
                throw new IOException("正式快照路径已被同名文件占用，请移走该文件后重试。", unchecked((int)0x800700B7));

            stage = "创建 staging";
            operationPath = staging;
            InjectSaveFailure(stage, staging);
            var stagingData = Path.Combine(staging, "BattleNet");
            Directory.CreateDirectory(staging);
            ownsStaging = true;
            Directory.CreateDirectory(stagingData);
            stage = "复制账号配置";
            var manifest = BuildManifestAndCopy(stagingData);
            InjectSaveFailure(stage, staging);
            stage = "写入 manifest";
            InjectSaveFailure(stage, staging);
            WriteDurableText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
            stage = "写入 meta";
            InjectSaveFailure(stage, staging);
            var meta = new AccountProfileMeta(accountId, battleTag, DateTime.UtcNow, expired);
            WriteDurableText(Path.Combine(staging, "meta.json"), JsonSerializer.Serialize(meta, JsonOptions));
            stage = "验证 staging";
            InjectSaveFailure(stage, staging);
            var verified = ValidateSnapshotDirectory(staging, accountId);
            if (verified.Legacy || verified.Meta != meta ||
                verified.Manifest.Entries.Any(entry => !entry.IsDirectory && entry.Sha256 is null))
                throw new InvalidDataException("待保存快照的元数据或文件哈希不完整。");

            stage = "提升正式快照";
            operationPath = profile;
            InjectSaveFailure(stage, staging);
            var hadPrevious = PathPresent(profile);
            if (hadPrevious) MoveSnapshotDirectory(profile, previous);
            try
            {
                stage = "移动 staging";
                operationPath = staging;
                InjectSaveFailure(stage, staging);
                // Same-parent NTFS rename commits the exact tree we just verified.
                MoveSnapshotDirectory(staging, profile);
                ownsStaging = false;
            }
            catch (Exception promotion) when (IsSnapshotFailure(promotion))
            {
                if (hadPrevious)
                {
                    try
                    {
                        InjectSaveFailure("回滚旧快照", previous);
                        MoveSnapshotDirectory(previous, profile);
                    }
                    catch (Exception rollback) when (IsSnapshotFailure(rollback))
                    {
                        stage = "回滚旧快照";
                        operationPath = previous;
                        throw new SnapshotRollbackException(
                            $"旧快照回滚失败，原备份保留在：{previous}\n提升错误：{promotion.Message}\n回滚错误：{rollback.Message}",
                            new AggregateException(promotion, rollback));
                    }
                }
                throw;
            }

            // Cleanup can be deferred once the verified tree is committed.
            if (hadPrevious)
            {
                try
                {
                    InjectSaveFailure("清理旧快照", previous);
                    DeleteTemporarySnapshot(previous);
                }
                catch (Exception ex) when (IsSnapshotFailure(ex))
                {
                    log.Write("SnapshotCleanupPending", detail: $"path={previous};error={ex.Message}");
                }
            }
            CleanupSupersededStaging(accountId, meta.SavedAtUtc, log);
            log.Write("SnapshotSaved", detail: $"files={manifest.ManagedPaths.Count};destination={profile}");
            return new(AccountProfileState.Valid, meta, "账号快照完整性校验通过。");
        }
        catch (Exception ex) when (IsSnapshotFailure(ex))
        {
            var reason = DescribeSaveFailure(ex, stage, operationPath);
            var message = $"账号快照保存失败（{stage}）。{reason}\n源目录：{_paths.RoamingDir}\n目标目录：{profile}\n操作路径：{operationPath}\n原因：{ex.Message}";
            log.Write("SnapshotSaveFailed", detail: $"stage={stage};hresult=0x{NativeFailure(ex).HResult:X8};path={operationPath};source={_paths.RoamingDir};destination={profile}");
            throw new IOException(message, ex);
        }
        finally
        {
            try
            {
                if (ownsStaging) TryCleanupTemporarySnapshot(staging, log);
            }
            finally { snapshotLock?.Dispose(); }
        }
    }

    private static void ProbeSnapshotDestination(string root, Action<string> beginRename)
    {
        var probe = Path.Combine(root, ".snapshot-probe-" + Guid.NewGuid().ToString("N"));
        var renamed = probe + "-renamed";
        var current = probe;
        try
        {
            Directory.CreateDirectory(probe);
            WriteDurableBytes(Path.Combine(probe, "probe"), [0]);
            beginRename(probe);
            MoveSnapshotDirectory(probe, renamed);
            current = renamed;
        }
        finally { TryCleanupTemporarySnapshot(current, new AccountSwitchLog()); }
    }

    internal static void MoveSnapshotDirectory(string source, string destination)
    {
        EnsureNoReparsePath(source, true, "待移动的账号快照");
        EnsureNoReparsePath(destination, true, "账号快照目标");
        for (var attempt = 0; ; attempt++)
        {
            if (PathPresent(destination))
                throw new IOException($"目标路径已存在，无法替换：{destination}", unchecked((int)0x800700B7));
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception ex) when (IsSnapshotFailure(ex))
            {
                var code = Win32Error(ex);
                // NTFS reports ACCESS_DENIED for a directory rename with an open descendant,
                // even when the descendant's handle allowed delete sharing.
                var locked = code is SharingViolation or LockViolation ||
                             (code == AccessDenied && FindOpenSnapshotFile(source) is not null);
                if (!locked) throw;
                if (attempt == 2)
                    throw new SnapshotDirectoryBusyException($"快照目录中的文件或目录仍被占用：{source}", ex);
                Thread.Sleep(80 * (attempt + 1));
            }
        }
    }

    private void RecoverInterruptedSaves()
    {
        foreach (var id in Directory.GetDirectories(Root, "*.previous-*")
                     .Select(path => TransactionAccountId(path, ".previous-")).OfType<long>().Distinct())
        {
            try { RecoverPreviousIfMissing(Root, id); }
            catch (Exception ex) when (IsSnapshotFailure(ex))
            {
                new AccountSwitchLog().Write("SnapshotRecoveryPending", targetAccountId: id, detail: ex.Message);
            }
        }
    }

    internal static void RecoverPreviousIfMissing(string root, long id)
    {
        var profile = Path.Combine(root, id.ToString(CultureInfo.InvariantCulture));
        if (PathPresent(profile) || !Directory.Exists(root)) return;

        var previous = FindPrevious(root, id)
            .Select(path => (Path: path, Meta: ReadTransactionMeta(path, id, allowLegacy: true)))
            .Where(candidate => candidate.Meta is not null)
            .OrderByDescending(candidate => candidate.Meta!.SavedAtUtc)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (previous.Meta is null) return;

        MoveSnapshotDirectory(previous.Path, profile);
        new AccountSwitchLog().Write("SnapshotSaveRecovered", targetAccountId: id, detail: previous.Path);
    }

    private static IEnumerable<string> FindPrevious(string root, long id) =>
        Directory.GetDirectories(root, $"{id.ToString(CultureInfo.InvariantCulture)}.previous-*")
            .Where(path => TransactionAccountId(path, ".previous-") == id);

    private static long? TransactionAccountId(string path, string separator)
    {
        var name = Path.GetFileName(path);
        var split = name.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
        if (split <= 0 || !long.TryParse(name.AsSpan(0, split), out var id) || id <= 0 ||
            name[..split] != id.ToString(CultureInfo.InvariantCulture) ||
            !Guid.TryParseExact(name[(split + separator.Length)..], "N", out _))
            return null;
        return id;
    }

    private static AccountProfileMeta? ReadTransactionMeta(string path, long id, bool allowLegacy)
    {
        try
        {
            var verified = ValidateSnapshotDirectory(path, id);
            return allowLegacy || !verified.Legacy ? verified.Meta : null;
        }
        catch (Exception ex) when (IsSnapshotFailure(ex)) { return null; }
    }

    private void CleanupSupersededStaging(long id, DateTime savedAt, AccountSwitchLog log)
    {
        try
        {
            foreach (var path in Directory.GetDirectories(Root, $"{id.ToString(CultureInfo.InvariantCulture)}.staging-*"))
                if (TransactionAccountId(path, ".staging-") == id &&
                    ReadTransactionMeta(path, id, allowLegacy: false) is { } meta && meta.SavedAtUtc <= savedAt)
                    TryCleanupTemporarySnapshot(path, log);
        }
        catch (Exception ex) when (IsSnapshotFailure(ex))
        {
            log.Write("SnapshotCleanupPending", detail: ex.Message);
        }
    }

    private static void TryCleanupTemporarySnapshot(string path, AccountSwitchLog log)
    {
        try { DeleteTemporarySnapshot(path); }
        catch (Exception ex) when (IsSnapshotFailure(ex))
        {
            log.Write("SnapshotCleanupPending", detail: $"path={path};error={ex.Message}");
        }
    }

    internal static void DeleteTemporarySnapshot(string path)
    {
        if (!PathPresent(path)) return;
        EnsureNoReparseTree(path, "临时快照目录");
        if (FindOpenSnapshotFile(path) is { } locked)
            throw new IOException($"临时快照文件仍被占用：{locked}");
        Directory.Delete(path, recursive: true);
    }

    private static string? FindOpenSnapshotFile(string directory)
    {
        try
        {
            foreach (var path in EnumerateFilesSafely(directory))
            {
                try { using var probe = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None); }
                catch (IOException ex) when (Win32Error(ex) is SharingViolation or LockViolation) { return path; }
                catch (Exception ex) when (IsSnapshotFailure(ex)) { }
            }
        }
        catch (Exception ex) when (IsSnapshotFailure(ex)) { }
        return null;
    }

    private static bool PathPresent(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static bool IsSnapshotFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException;

    private static Exception NativeFailure(Exception ex) => ex.GetBaseException();

    private static int Win32Error(Exception ex) => NativeFailure(ex).HResult & 0xffff;

    private static string DescribeSaveFailure(Exception ex, string stage, string path)
    {
        if (ex is SnapshotRollbackException) return "请保留旧快照目录，解除占用或修复权限后重试。";
        if (ex is SnapshotDirectoryBusyException || Win32Error(ex) is SharingViolation or LockViolation)
            return "文件或目录被占用，请关闭正在访问该路径的程序，稍后重试。";
        return Win32Error(ex) switch
        {
            39 or 112 => "目标磁盘空间不足，请释放空间后重试。",
            80 or 145 or 183 => "目标路径已存在，无法替换。请核对同名文件或目录。",
            AccessDenied when stage is "移动 staging" or "检查临时目录重命名" =>
                $"临时目录无法重命名。请检查「{path}」与其父目录的修改权限及安全软件拦截记录。",
            AccessDenied when stage == "提升正式快照" =>
                $"已有快照无法移动。请检查「{path}」的修改权限及安全软件拦截记录。",
            AccessDenied when stage is "检查目标目录" or "创建 staging" or "写入 manifest" or "写入 meta" =>
                $"目标目录无写入权限。请检查「{path}」及其父目录权限，或使用当前用户可修改的备份目录。",
            AccessDenied => "配置读取或备份写入被拒绝，请检查下方路径的权限及安全软件拦截记录。",
            _ when ex is InvalidDataException => "配置或快照校验失败，请重新登录后保存。",
            _ when ex is DirectoryNotFoundException => "路径不存在，请检查磁盘和原始配置。",
            _ => "请按下方操作路径和错误详情检查后重试。",
        };
    }

    private sealed class SnapshotRollbackException(string message, Exception inner) : IOException(message, inner);
    private sealed class SnapshotDirectoryBusyException(string message, Exception inner) : IOException(message, inner);

    internal static SnapshotLock AcquireSnapshotLock(string root) => new(root);

    internal sealed class SnapshotLock : IDisposable
    {
        private readonly Mutex _mutex;

        internal SnapshotLock(string root)
        {
            var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            _mutex = new Mutex(false, @"Local\CloudLight Blizzard.AccountSnapshots." + hash);
            try { _mutex.WaitOne(); }
            catch (AbandonedMutexException) { /* The caller owns the mutex and checks for interrupted saves. */ }
            catch { _mutex.Dispose(); throw; }
        }

        public void Dispose()
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    private static void InjectSaveFailure(string stage, string path)
    {
        if (SaveFailureForSelfTest?.Invoke(stage, path) is { } error) throw error;
    }
}
