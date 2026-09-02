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
        var pointer = _store.LoadPointer();
        var result = new List<SnapshotDescriptor>();
        foreach (var id in _store.EnumerateGenerationIds())
        {
            var root = _store.GenerationRoot(id);
            var backupRoot = Path.Combine(root, "backups");
            var files = Directory.Exists(backupRoot)
                ? OverwatchRegionBackupStore.EnumerateFilesWithoutReparse(backupRoot)
                : Array.Empty<string>();
            var totalBytes = files.Sum(path => new FileInfo(path).Length);
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
                    FileCount = files.Count,
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
                FileCount = expectedFiles > 0 ? expectedFiles : files.Count,
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
        if (!OverwatchRegionBackupStore.IsSafeGenerationId(generationId))
            throw new InvalidDataException("快照标识无效，已阻止删除。");
        var pointer = _store.LoadPointer();
        if (string.Equals(pointer?.GenerationId, generationId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前正在使用的快照不能删除，请先重新准备区服文件。");
        var deleted = _store.DeleteGeneration(generationId);
        WriteLog($"delete id={generationId} result={deleted}");
        return deleted;
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

    private static void WriteLog(string message)
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
