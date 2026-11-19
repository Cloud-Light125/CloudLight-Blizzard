using System.IO;

namespace CloudLightBlizzard.Services.OverwatchRegion;

public enum SnapshotDisplayState
{
    Normal,
    Corrupt,
    Expired,
    Missing,
    Unknown,
}

public sealed class SnapshotDescriptor
{
    public string GenerationId { get; init; } = "";
    public RegionBackupMode Mode { get; init; }
    public OverwatchRegion SourceRegion { get; init; }
    public OverwatchRegion TargetRegion { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? LastUsedAtUtc { get; init; }
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public SnapshotDisplayState State { get; init; }
    public string StateReason { get; init; } = "";
    public bool IsActive { get; init; }
    public string RootPath { get; init; } = "";
}

public sealed record SnapshotCleanupPolicy(bool Enabled, int HistoryLimit)
{
    public static int NormalizeLimit(int value) => value is -1 or 0 or 1 or 2 or 3 or 5 ? value : 2;
}

public enum SnapshotCleanupDisposition { Protected, Retained, Delete }

public sealed record SnapshotCleanupEntry(string GenerationId, long Bytes, SnapshotCleanupDisposition Disposition, string Reason)
{
    public string DisplayText => $"{GenerationId}  ·  {Reason}" +
        (Bytes > 0 ? $"  ·  {UpdateDownloadService.FormatBytes(Bytes)}" : "");
}

public sealed record SnapshotCleanupPlan(string Root, int HistoryLimit, string? BlockedReason,
    IReadOnlyList<SnapshotCleanupEntry> Entries)
{
    public IEnumerable<SnapshotCleanupEntry> ToDelete => Entries.Where(entry => entry.Disposition == SnapshotCleanupDisposition.Delete);
    public int ProtectedCount => Entries.Count(entry => entry.Disposition == SnapshotCleanupDisposition.Protected);
    public long ReclaimableBytes => ToDelete.Sum(entry => entry.Bytes);
}

public sealed record SnapshotCleanupResult(int Removed, long Bytes, IReadOnlyList<string> Failures);

/// <summary>只负责读取和编排现有 OverwatchRegionManager；不在 UI 重复 hash/copy 算法。</summary>
public sealed class SnapshotManagerService
{
    private static readonly object LogGate = new();
    private readonly OverwatchRegionBackupStore _store;

    public string BackupRoot => _store.Root;

    public SnapshotManagerService(OverwatchRegionManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        // Reuse the manager's already-open store. Constructing another store here
        // would rerun preparation recovery and create directories during a read-only
        // diagnostics/list operation.
        _store = manager.BackupStore;
    }

    public IReadOnlyList<SnapshotDescriptor> List()
    {
        using var operation = _store.BeginRegionOperation();
        var pointer = _store.LoadPointer();
        var result = new List<SnapshotDescriptor>();
        foreach (var id in _store.EnumerateGenerationIds())
        {
            var root = _store.GenerationRoot(id);
            var backupPrefix = Path.Combine(root, "backups") + Path.DirectorySeparatorChar;
            var storedFiles = OverwatchRegionBackupStore.EnumerateFilesWithoutReparse(root);
            var files = storedFiles.Where(path => path.StartsWith(backupPrefix, StringComparison.OrdinalIgnoreCase)).ToArray();
            var totalBytes = storedFiles.Sum(path => new FileInfo(path).Length);
            var active = string.Equals(pointer?.GenerationId, id, StringComparison.OrdinalIgnoreCase);
            var generation = _store.LoadGeneration(id);
            if (generation is null)
            {
                var hasPair = File.Exists(Path.Combine(root, "pair.json"));
                result.Add(new SnapshotDescriptor
                {
                    GenerationId = id,
                    Mode = RegionBackupMode.FullSnapshot,
                    CreatedAtUtc = Directory.GetCreationTimeUtc(root),
                    LastUsedAtUtc = active ? pointer?.ActivatedAtUtc : (DateTime?)null,
                    FileCount = files.Length,
                    TotalBytes = totalBytes,
                    State = hasPair ? SnapshotDisplayState.Corrupt : SnapshotDisplayState.Missing,
                    StateReason = hasPair ? "快照记录无法读取" : "快照记录文件缺失",
                    IsActive = active,
                    RootPath = root,
                });
                continue;
            }
            var expectedFiles = generation.Differences.Sum(item =>
                (item.China is null ? 0 : 1) + (item.International is null ? 0 : 1));
            var display = ToDisplayState(generation, root, files, expectedFiles);
            result.Add(new SnapshotDescriptor
            {
                GenerationId = id,
                Mode = generation.BackupMode,
                SourceRegion = generation.SourceRegion,
                TargetRegion = generation.TargetRegion,
                CreatedAtUtc = generation.CreatedAtUtc,
                LastUsedAtUtc = active ? pointer?.ActivatedAtUtc : (DateTime?)null,
                FileCount = expectedFiles > 0 ? expectedFiles : files.Length,
                TotalBytes = totalBytes,
                State = display.State,
                StateReason = display.Reason,
                IsActive = active,
                RootPath = root,
            });
        }
        return result.OrderByDescending(item => item.CreatedAtUtc).ToList();
    }

    public bool Delete(string generationId)
    {
        using var operation = _store.BeginRegionOperation();
        var deleted = _store.DeleteGeneration(generationId);
        WriteLog($"delete id={generationId} result={deleted}");
        return deleted;
    }

    public SnapshotCleanupPlan ScanCleanup(int historyLimit)
    {
        using var operation = _store.BeginRegionOperation();
        return BuildCleanupPlan(historyLimit);
    }

    public SnapshotCleanupResult Clean(SnapshotCleanupPlan preview, int currentLimit)
    {
        using var operation = _store.BeginRegionOperation();
        if (!string.Equals(preview.Root, _store.Root, StringComparison.OrdinalIgnoreCase) || preview.HistoryLimit != currentLimit)
            throw new InvalidOperationException("快照目录或保留设置已变化，请重新扫描。");
        var current = BuildCleanupPlan(currentLimit);
        if (current.BlockedReason is { } blocked)
            throw new InvalidOperationException(blocked);
        if (!preview.ToDelete.SequenceEqual(current.ToDelete))
            throw new InvalidOperationException("快照或保护状态已变化，请重新扫描并确认。");
        return DeletePlanned(current, "手动清理");
    }

    // Called by the region manager while its preparation/switch guard is still held.
    internal void PruneAfterActivation(int historyLimit)
    {
        var plan = BuildCleanupPlan(historyLimit);
        if (plan.BlockedReason is { } blocked)
            WriteLog($"自动清理已跳过 · {blocked}");
        else
            DeletePlanned(plan, "自动清理");
    }

    private SnapshotCleanupPlan BuildCleanupPlan(int historyLimit)
    {
        historyLimit = SnapshotCleanupPolicy.NormalizeLimit(historyLimit);
        string? blocked = null;
        IReadOnlyDictionary<string, string> protectedIds = new Dictionary<string, string>();
        try
        {
            protectedIds = _store.GetSnapshotProtection();
            if (_store.HasPendingSnapshotRecovery())
                blocked = "检测到未完成的区服准备或恢复，暂不可清理。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            blocked = ex.Message;
        }

        var entries = new List<SnapshotCleanupEntry>();
        IReadOnlyList<string> directories;
        try { directories = _store.ListCleanupDirectories(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new SnapshotCleanupPlan(_store.Root, historyLimit, ex.Message, entries);
        }

        var eligible = new List<(string Id, long Bytes, DateTime Created)>();
        foreach (var id in directories)
        {
            if (blocked is not null)
            {
                entries.Add(new(id, 0, SnapshotCleanupDisposition.Protected, blocked));
                continue;
            }
            if (protectedIds.TryGetValue(id, out var reason))
            {
                entries.Add(new(id, 0, SnapshotCleanupDisposition.Protected, reason));
                continue;
            }
            try
            {
                if (_store.LoadGeneration(id) is not { State: RegionBackupState.Ready or RegionBackupState.Stale } item)
                {
                    entries.Add(new(id, 0, SnapshotCleanupDisposition.Protected, "缺少可验证的已完成快照记录"));
                    continue;
                }
                eligible.Add((id, _store.MeasureGeneration(id), item.CreatedAtUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                entries.Add(new(id, 0, SnapshotCleanupDisposition.Protected, ex.Message));
            }
        }
        var position = 0;
        foreach (var item in eligible.OrderByDescending(item => item.Created).ThenByDescending(item => item.Id, StringComparer.Ordinal))
        {
            var retain = historyLimit < 0 || position++ < historyLimit;
            entries.Add(new(item.Id, item.Bytes, retain ? SnapshotCleanupDisposition.Retained : SnapshotCleanupDisposition.Delete,
                retain ? "按历史保留数量保留" : "将删除"));
        }
        return new(_store.Root, historyLimit, blocked, entries);
    }

    private SnapshotCleanupResult DeletePlanned(SnapshotCleanupPlan plan, string operation)
    {
        var failures = new List<string>();
        var removed = 0;
        long bytes = 0;
        foreach (var item in plan.ToDelete)
        {
            try
            {
                if (!_store.DeleteGeneration(item.GenerationId))
                    throw new IOException("快照目录已不存在");
                removed++;
                bytes += item.Bytes;
            }
            catch (Exception ex)
            {
                var failure = $"{item.GenerationId} · {ex.Message}";
                failures.Add(failure);
                WriteLog($"{operation}失败 · {failure}");
            }
        }
        WriteLog($"{operation}完成 · 已删除 {removed} 份 · {bytes} 字节 · 失败 {failures.Count} 份");
        return new(removed, bytes, failures);
    }

    private static SnapshotDisplayStatus ToDisplayState(OverwatchRegionGeneration generation,
        string root, IReadOnlyCollection<string> files, int expectedFiles)
    {
        if (!Directory.Exists(root))
            return new(SnapshotDisplayState.Missing, "快照目录不存在");
        if (!File.Exists(Path.Combine(root, "pair.json")))
            return new(SnapshotDisplayState.Missing, "快照记录文件缺失");
        if (files.Count < expectedFiles)
            return new(SnapshotDisplayState.Missing, $"快照文件不完整，缺少 {expectedFiles - files.Count:N0} 个文件");
        if (generation.State == RegionBackupState.Stale)
            return new(SnapshotDisplayState.Expired, "游戏文件或版本发生变化，快照可能已过期");
        if (generation.State == RegionBackupState.Error)
            return new(SnapshotDisplayState.Corrupt, "快照生成记录标记为损坏");
        if (generation.State != RegionBackupState.Ready)
            return new(SnapshotDisplayState.Unknown, "快照尚未准备完成");
        return new(SnapshotDisplayState.Normal, "");
    }

    internal static void WriteLog(string message)
    {
        try
        {
            var directory = AppPaths.Current.LogsDir;
            Directory.CreateDirectory(directory);
            lock (LogGate)
                File.AppendAllText(Path.Combine(directory, "snapshot.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [snapshot] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private sealed record SnapshotDisplayStatus(SnapshotDisplayState State, string Reason);
}
