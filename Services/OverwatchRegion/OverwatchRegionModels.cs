using System.Text.Json.Serialization;

namespace CloudLightBlizzard.Services.OverwatchRegion;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverwatchRegion { China, International }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegionBackupMode
{
    // 必须保持为 0：旧 Generation / pending.json 没有 BackupMode 字段时按完整备份兼容读取。
    FullSnapshot = 0,
    VerifiedDifference = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegionPreparationCheckpoint { Step1Ready, Step2Ready }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CurrentGameRegion { Unknown, China, International, Mixed }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GenerationCompatibility { Compatible, Updated, Unknown }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegionSwitchEligibility { Normal, BestEffort, BackupUnavailable, GameUpdated }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegionEvidenceResult { NoStrongConflict, StrongChina, StrongInternational, StrongConflict }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegionBackupState
{
    Empty,
    Preparing,
    Ready,
    Stale,
    Legacy,
    Error,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegionDifferenceKind { Same, ChinaOnly, InternationalOnly, Different }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CandidateBackupStatus { Available, Unavailable }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CandidateVerificationOutcome { VerifiedUsable, VerificationRejected, FileIssueSkipped }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegionVerificationLevel { RoundTrip, DoubleRoundTrip }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Step4VerificationOutcome { DoubleVerified, Step4Rejected, Step4Unverified }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegionFileCheckKind { PermanentNormal, PermanentMissing, PermanentChanged, ShouldBeAbsent, TemporaryCandidate, Unreadable }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegionSwitchOutcome { Success, PartialSuccess, Failed }

public sealed class RegionFileEntry
{
    public string RelativePath { get; set; } = "";
    public long Size { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime LastWriteTimeUtc { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class GameBuildFingerprint
{
    public string BuildInfoSha256 { get; set; } = "";
    public string ExecutableFileVersion { get; set; } = "";
    public string ExecutableProductVersion { get; set; } = "";
    public long ExecutableSize { get; set; }
    public string CoreFingerprint { get; set; } = "";
}

public sealed class OverwatchRegionManifest
{
    public int SchemaVersion { get; set; } = 2;
    public string ManifestId { get; set; } = Guid.NewGuid().ToString("N");
    public OverwatchRegion Region { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public GameBuildFingerprint BuildFingerprint { get; set; } = new();
    public Dictionary<string, RegionFileEntry> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RegionDifference
{
    public string RelativePath { get; set; } = "";
    public RegionDifferenceKind Kind { get; set; }
    public RegionFileEntry? China { get; set; }
    public RegionFileEntry? International { get; set; }
    // null 表示旧数据，按原有语义视为可用；false 只由逐侧重设显式写入。
    public bool? ChinaAvailable { get; set; }
    public bool? InternationalAvailable { get; set; }
}

public sealed class OverwatchRegionGeneration
{
    public int SchemaVersion { get; set; } = 2;
    public string GenerationId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public RegionBackupState State { get; set; } = RegionBackupState.Preparing;
    public RegionBackupMode BackupMode { get; set; } = RegionBackupMode.FullSnapshot;
    public OverwatchRegion SourceRegion { get; set; }
    public OverwatchRegion TargetRegion { get; set; }
    public string ChinaManifestId { get; set; } = "";
    public string InternationalManifestId { get; set; } = "";
    public GameBuildFingerprint ChinaBuildFingerprint { get; set; } = new();
    public GameBuildFingerprint InternationalBuildFingerprint { get; set; } = new();
    public string CommonBaselineFingerprint { get; set; } = "";
    public bool ChinaBackupComplete { get; set; }
    public bool InternationalBackupComplete { get; set; }
    public List<RegionDifference> Differences { get; set; } = new();
    public RegionVerificationSummary? VerificationSummary { get; set; }
    public RegionVerificationLevel VerificationLevel { get; set; } = RegionVerificationLevel.RoundTrip;
    public Step4VerificationSummary? Step4Summary { get; set; }
    public bool ChinaReferenceComplete { get; set; }
    public bool InternationalReferenceComplete { get; set; }
    public List<RegionFileIssue> ResetWarnings { get; set; } = new();
}

public sealed class Step4VerificationSummary
{
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
    public int DoubleVerifiedCount { get; set; }
    public int RejectedCount { get; set; }
    public int UnverifiedCount { get; set; }
    public List<Step4EntryResult> Results { get; set; } = new();
}

public sealed class Step4EntryResult
{
    public string RelativePath { get; set; } = "";
    public Step4VerificationOutcome Outcome { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class RegionVerificationSummary
{
    public int CandidateCount { get; set; }
    public int VerifiedCount { get; set; }
    public int RejectedCount { get; set; }
    public int SkippedFileCount { get; set; }
    public bool HasWarnings { get; set; }
    public List<RegionCandidateResult> Results { get; set; } = new();
}

public sealed class RegionFileIssue
{
    public string RelativePath { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class CandidateBackupRecord
{
    public string RelativePath { get; set; } = "";
    public CandidateBackupStatus Status { get; set; } = CandidateBackupStatus.Available;
    public string Reason { get; set; } = "";
}

public sealed class RegionCandidateResult
{
    public string RelativePath { get; set; } = "";
    public CandidateVerificationOutcome Outcome { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class PendingRegionPreparation
{
    public int SchemaVersion { get; set; } = 2;
    public string GenerationId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public RegionBackupMode BackupMode { get; set; } = RegionBackupMode.FullSnapshot;
    public RegionPreparationCheckpoint Checkpoint { get; set; } = RegionPreparationCheckpoint.Step1Ready;
    public OverwatchRegion SourceRegion { get; set; }
    public OverwatchRegion TargetRegion { get; set; }
    public List<RegionFileIssue> Step1Warnings { get; set; } = new();
    public int CandidateCount { get; set; }
    public int CandidateBackupSavedCount { get; set; }
    public List<CandidateBackupRecord> CandidateBackups { get; set; } = new();
}

public sealed class ActiveGenerationPointer
{
    public int SchemaVersion { get; set; } = 2;
    public string GenerationId { get; set; } = "";
    public string? PreviousGenerationId { get; set; }
    public DateTime ActivatedAtUtc { get; set; } = DateTime.UtcNow;
    public OverwatchRegion? LastSuccessfulRegion { get; set; }
    public string? LastSuccessfulGenerationId { get; set; }
}

public sealed record RegionProgress(string Message, int Current = 0, int Total = 0, long BytesCurrent = 0, long BytesTotal = 0);
public sealed record RegionGenerationVerificationResult(
    bool Available,
    int CheckedFileCount,
    int TotalFileCount,
    int DamagedCount,
    int MissingCount,
    string Summary,
    IReadOnlyList<RegionFileIssue> Issues);
public sealed record RegionSwitchResult(
    int Restored,
    int Deleted,
    int ChinaOnlyProcessed = 0,
    int InternationalOnlyProcessed = 0,
    int DifferentRestored = 0,
    bool Verified = false,
    RegionSwitchEligibility Eligibility = RegionSwitchEligibility.Normal,
    RegionSwitchOutcome Outcome = RegionSwitchOutcome.Success,
    int FailedCount = 0,
    IReadOnlyList<RegionFileIssue>? Issues = null);

public enum SwitchPlanOperation { Restore, Delete, Keep }

public sealed class SwitchPlanFile
{
    public string RelativePath { get; init; } = "";
    public SwitchPlanOperation Operation { get; init; }
    public RegionDifferenceKind DifferenceKind { get; init; }
    public RegionFileEntry? Expected { get; init; }
    public bool DestinationExists { get; init; }
    public long EstimatedBytes { get; init; }
}

public sealed class SwitchPlan
{
    public string GenerationId { get; init; } = "";
    public OverwatchRegion? SourceRegion { get; init; }
    public OverwatchRegion TargetRegion { get; init; }
    public CurrentGameRegion CurrentRegion { get; init; }
    public RegionBackupMode BackupMode { get; init; }
    public GenerationCompatibility Compatibility { get; init; }
    public string CompatibilityReason { get; init; } = "";
    public RegionSwitchEligibility Eligibility { get; init; }
    public string EligibilityReason { get; init; } = "";
    public int EligibilityFileIssueCount { get; init; }
    public RegionEvidenceResult RegionEvidence { get; init; }
    public bool ExactSnapshotMatch { get; init; }
    public bool BattleNetRunning { get; init; }
    public string CurrentBattleNetState { get; init; } = "";
    public string SnapshotState { get; init; } = "";
    public long EstimatedBytes { get; set; }
    public long RequiredDiskSpace { get; set; }
    public List<SwitchPlanFile> Operations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Blockers { get; set; } = new();

    [JsonIgnore]
    public IReadOnlyList<SwitchPlanFile> FilesToRestore => Operations.Where(item => item.Operation == SwitchPlanOperation.Restore).ToList();
    [JsonIgnore]
    public IReadOnlyList<SwitchPlanFile> FilesToCopy => FilesToRestore.Where(item => !item.DestinationExists).ToList();
    [JsonIgnore]
    public IReadOnlyList<SwitchPlanFile> FilesToOverwrite => FilesToRestore.Where(item => item.DestinationExists).ToList();
    [JsonIgnore]
    public IReadOnlyList<SwitchPlanFile> FilesToDelete => Operations.Where(item => item.Operation == SwitchPlanOperation.Delete).ToList();
    [JsonIgnore]
    public IReadOnlyList<SwitchPlanFile> FilesToKeep => Operations.Where(item => item.Operation == SwitchPlanOperation.Keep).ToList();
    [JsonIgnore]
    public int RestoreCount => FilesToRestore.Count;
    [JsonIgnore]
    public int CopyCount => FilesToCopy.Count;
    [JsonIgnore]
    public int OverwriteCount => FilesToOverwrite.Count;
    [JsonIgnore]
    public int DeleteCount => FilesToDelete.Count;
    [JsonIgnore]
    public int KeepCount => FilesToKeep.Count;
    [JsonIgnore]
    public string EstimatedBytesText => FormatBytes(EstimatedBytes);
    [JsonIgnore]
    public string RequiredDiskSpaceText => FormatBytes(RequiredDiskSpace);
    [JsonIgnore]
    public bool CanExecute => Blockers.Count == 0;

    private static string FormatBytes(long bytes) => bytes < 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024d:0.0} MB" : $"{bytes / 1024d / 1024d / 1024d:0.0} GB";
}

public sealed class RegionSnapshotStatus
{
    public bool GamePathValid { get; set; }
    public string GamePath { get; set; } = "";
    public RegionBackupState State { get; set; }
    public RegionBackupMode BackupMode { get; set; } = RegionBackupMode.FullSnapshot;
    public RegionPreparationCheckpoint? PreparationCheckpoint { get; set; }
    public CurrentGameRegion CurrentRegion { get; set; }
    public GenerationCompatibility GenerationCompatibility { get; set; } = GenerationCompatibility.Unknown;
    public string CompatibilityReason { get; set; } = "";
    public RegionSwitchEligibility SwitchEligibility { get; set; } = RegionSwitchEligibility.BackupUnavailable;
    public string SwitchEligibilityReason { get; set; } = "";
    public OverwatchRegion? PendingSourceRegion { get; set; }
    public OverwatchRegion? PendingTargetRegion { get; set; }
    public bool ChinaCaptured { get; set; }
    public bool InternationalCaptured { get; set; }
    public bool ChinaBackupComplete { get; set; }
    public bool InternationalBackupComplete { get; set; }
    public int DifferenceCount { get; set; }
    public long BackupBytes { get; set; }
    public string? ActiveGenerationId { get; set; }
    public OverwatchRegion? LastSuccessfulRegion { get; set; }
    public string? LastSuccessfulGenerationId { get; set; }
    public RegionEvidenceResult RegionEvidence { get; set; } = RegionEvidenceResult.NoStrongConflict;
    public bool ExactSnapshotMatch { get; set; }
    public int CandidateCount { get; set; }
    public int CandidateBackupSavedCount { get; set; }
    public int RejectedCount { get; set; }
    public int SkippedFileCount { get; set; }
    public int BackupFileIssueCount { get; set; }
    public bool HasWarnings { get; set; }
    public IReadOnlyList<RegionFileIssue> FileIssues { get; set; } = Array.Empty<RegionFileIssue>();
    public RegionVerificationLevel VerificationLevel { get; set; } = RegionVerificationLevel.RoundTrip;
    public bool ChinaReferenceAvailable { get; set; }
    public bool InternationalReferenceAvailable { get; set; }
    public bool PossibleGameUpdate { get; set; }
}

public sealed record RegionScanResult(OverwatchRegionManifest Manifest, IReadOnlyList<RegionFileIssue> Issues);

public sealed class RegionFileCheckItem
{
    public string RelativePath { get; set; } = "";
    public RegionFileCheckKind Kind { get; set; }
    public long OriginalSize { get; set; }
    public long CurrentSize { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class RegionFileCheckResult
{
    public OverwatchRegion Region { get; set; }
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
    public bool HasReferenceManifest { get; set; }
    public bool ReferenceManifestComplete { get; set; }
    public IReadOnlyList<RegionFileCheckItem> Items { get; set; } = Array.Empty<RegionFileCheckItem>();
    public int NormalCount => Items.Count(item => item.Kind == RegionFileCheckKind.PermanentNormal);
    public int MissingCount => Items.Count(item => item.Kind == RegionFileCheckKind.PermanentMissing);
    public long MissingBytes => Items.Where(item => item.Kind == RegionFileCheckKind.PermanentMissing).Sum(item => item.OriginalSize);
    public int ChangedCount => Items.Count(item => item.Kind == RegionFileCheckKind.PermanentChanged);
    public long ChangedBytes => Items.Where(item => item.Kind == RegionFileCheckKind.PermanentChanged).Sum(item => Math.Max(item.OriginalSize, item.CurrentSize));
    public int ShouldBeAbsentCount => Items.Count(item => item.Kind == RegionFileCheckKind.ShouldBeAbsent);
    public int TemporaryCount => Items.Count(item => item.Kind == RegionFileCheckKind.TemporaryCandidate);
    public long TemporaryBytes => Items.Where(item => item.Kind == RegionFileCheckKind.TemporaryCandidate).Sum(item => item.CurrentSize);
    public int UnreadableCount => Items.Count(item => item.Kind == RegionFileCheckKind.Unreadable);
}

public sealed record TemporaryCleanupResult(int Deleted, long DeletedBytes, int Skipped,
    IReadOnlyList<RegionFileIssue> Issues);

public sealed record Step4VerificationResult(string GenerationId, int DoubleVerified, int Rejected,
    int Unverified);

public sealed record RegionResetResult(string GenerationId, OverwatchRegion Region, int Updated,
    int Degraded, int PotentialDifferences, IReadOnlyList<RegionFileIssue> Warnings);
