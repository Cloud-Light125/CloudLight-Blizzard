using System.IO;
using System.Text.Json;

namespace CloudLightBlizzard.Services.OverwatchRegion;

public enum SnapshotDisplayState
{
    Normal,
    Unverified,
    Verifying,
    Corrupt,
    Expired,
    Missing,
}

public sealed class SnapshotDescriptor
{
    public string GenerationId { get; init; } = "";
    public RegionBackupMode Mode { get; init; }
    public OverwatchRegion SourceRegion { get; init; }
    public OverwatchRegion TargetRegion { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? LastVerifiedAtUtc { get; init; }
    public DateTime? LastUsedAtUtc { get; init; }
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public SnapshotDisplayState State { get; init; }
    public string StateReason { get; init; } = "";
    public bool IsActive { get; init; }
    public string RootPath { get; init; } = "";
}

public sealed record SnapshotVerificationResult(
    string GenerationId, SnapshotDisplayState State, int FileCount, int DamagedCount,
    int MissingCount, string Summary, DateTime VerifiedAtUtc);

/// <summary>只负责读取和编排现有 OverwatchRegionManager；不在 UI 重复 hash/copy 算法。</summary>
public sealed class SnapshotManagerService
{
    private static readonly object LogGate = new();
    private readonly OverwatchRegionManager _manager;
    private readonly OverwatchRegionBackupStore _store;
    private readonly string _verificationFile;

    public string BackupRoot => _store.Root;

    public SnapshotManagerService(OverwatchRegionManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        // Reuse the manager's already-open store. Constructing another store here
        // would rerun preparation recovery and create directories during a read-only
        // diagnostics/list operation.
        _store = manager.BackupStore;
        _verificationFile = Path.Combine(_store.Root, "snapshot-verification.json");
    }

    public IReadOnlyList<SnapshotDescriptor> List()
    {
        var pointer = _store.LoadPointer();
        var verification = LoadVerification();
        var result = new List<SnapshotDescriptor>();
        foreach (var id in _store.EnumerateGenerationIds())
        {
            var generation = _store.LoadGeneration(id);
            if (generation is null) continue;
            var root = _store.GenerationRoot(id);
            var backupRoot = Path.Combine(root, "backups");
            var files = Directory.Exists(backupRoot)
                ? OverwatchRegionBackupStore.EnumerateFilesWithoutReparse(backupRoot)
                : Array.Empty<string>();
            var expectedFiles = generation.Differences.Sum(item =>
                (item.China is null ? 0 : 1) + (item.International is null ? 0 : 1));
            var totalBytes = files.Sum(path => new FileInfo(path).Length);
            var active = string.Equals(pointer?.GenerationId, id, StringComparison.OrdinalIgnoreCase);
            var verifiedAt = verification.TryGetValue(id, out var state) ? (DateTime?)state.VerifiedAtUtc : null;
            var displayState = ToDisplayState(generation, root, files, expectedFiles, state?.State);
            result.Add(new SnapshotDescriptor
            {
                GenerationId = id,
                Mode = generation.BackupMode,
                SourceRegion = generation.SourceRegion,
                TargetRegion = generation.TargetRegion,
                CreatedAtUtc = generation.CreatedAtUtc,
                LastVerifiedAtUtc = verifiedAt,
                LastUsedAtUtc = active ? pointer?.ActivatedAtUtc : (DateTime?)null,
                FileCount = expectedFiles > 0 ? expectedFiles : files.Count,
                TotalBytes = totalBytes,
                State = displayState,
                StateReason = state?.Reason ?? (displayState == SnapshotDisplayState.Unverified ? "尚未执行完整验证" : ""),
                IsActive = active,
                RootPath = root,
            });
        }
        return result.OrderByDescending(item => item.CreatedAtUtc).ToList();
    }

    public async Task<SnapshotVerificationResult> VerifyAsync(string? gameRoot,
        string generationId, IProgress<RegionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = List().FirstOrDefault(item =>
            string.Equals(item.GenerationId, generationId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null) throw new InvalidOperationException("找不到指定的区服快照。");
        WriteLog($"verify-start id={generationId} active={descriptor.IsActive}");
        progress?.Report(new RegionProgress("正在验证快照…", 0, Math.Max(1, descriptor.FileCount)));
        int damaged;
        int missing;
        string summary;
        SnapshotDisplayState display;
        if (descriptor.IsActive)
        {
            var status = await _manager.GetStatusAsync(gameRoot, cancellationToken,
                verifyFiles: true, verifyBackupHashes: true, progress: progress).ConfigureAwait(false);
            damaged = status.BackupFileIssueCount + status.RejectedCount;
            missing = status.SkippedFileCount;
            if (status.SwitchEligibility == RegionSwitchEligibility.BackupUnavailable && damaged == 0 && missing == 0)
            {
                if (status.SwitchEligibilityReason.Contains("缺失", StringComparison.Ordinal) ||
                    status.SwitchEligibilityReason.Contains("不存在", StringComparison.Ordinal))
                    missing = 1;
                else
                    damaged = 1;
            }
            display = status.State == RegionBackupState.Ready && damaged == 0 && missing == 0
                ? SnapshotDisplayState.Normal
                : status.State == RegionBackupState.Stale ? SnapshotDisplayState.Expired
                : SnapshotDisplayState.Corrupt;
            summary = display == SnapshotDisplayState.Normal ? "完整且已验证"
                : display == SnapshotDisplayState.Expired ? "游戏目录或版本已变化，快照可能过期"
                : status.SwitchEligibility == RegionSwitchEligibility.BackupUnavailable
                    ? status.SwitchEligibilityReason
                    : $"发现 {damaged:N0} 个损坏项、{missing:N0} 个缺失项";
        }
        else
        {
            var verification = await _manager.VerifyGenerationAsync(generationId, progress, cancellationToken)
                .ConfigureAwait(false);
            damaged = verification.DamagedCount;
            missing = verification.MissingCount;
            display = verification.Available && damaged == 0 && missing == 0
                ? SnapshotDisplayState.Normal : SnapshotDisplayState.Corrupt;
            summary = verification.Summary;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTime.UtcNow;
        SaveVerification(generationId, new VerificationEntry { VerifiedAtUtc = now, State = display, Reason = summary });
        progress?.Report(new RegionProgress($"已验证 {descriptor.FileCount:N0} / {descriptor.FileCount:N0}", descriptor.FileCount, descriptor.FileCount));
        WriteLog($"verify-complete id={generationId} state={display} damaged={damaged} missing={missing}");
        return new SnapshotVerificationResult(generationId, display, descriptor.FileCount, damaged, missing, summary, now);
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

    private Dictionary<string, VerificationEntry> LoadVerification()
    {
        try
        {
            return File.Exists(_verificationFile)
                ? JsonSerializer.Deserialize<Dictionary<string, VerificationEntry>>(File.ReadAllText(_verificationFile))
                    ?? new(StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private void SaveVerification(string id, VerificationEntry value)
    {
        var all = LoadVerification();
        all[id] = value;
        OverwatchRegionBackupStore.WriteJson(_verificationFile, all);
    }

    private static SnapshotDisplayState ToDisplayState(OverwatchRegionGeneration generation,
        string root, IReadOnlyCollection<string> files, int expectedFiles, SnapshotDisplayState? recorded)
    {
        if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "pair.json"))) return SnapshotDisplayState.Missing;
        if (files.Count < expectedFiles) return SnapshotDisplayState.Missing;
        if (recorded is not null) return recorded.Value;
        if (generation.State == RegionBackupState.Stale) return SnapshotDisplayState.Expired;
        if (generation.State == RegionBackupState.Error) return SnapshotDisplayState.Corrupt;
        return SnapshotDisplayState.Unverified;
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

    private sealed class VerificationEntry
    {
        public DateTime VerifiedAtUtc { get; set; }
        public SnapshotDisplayState State { get; set; }
        public string Reason { get; set; } = "";
    }
}
