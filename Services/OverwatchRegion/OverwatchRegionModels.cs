using System.Text.Json.Serialization;

namespace CloudLightBlizzard.Services.OverwatchRegion;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverwatchRegion { China, International }

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

public sealed class RegionFileEntry
{
    public string RelativePath { get; set; } = "";
    public long Size { get; set; }
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
}

public sealed class OverwatchRegionGeneration
{
    public int SchemaVersion { get; set; } = 2;
    public string GenerationId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public RegionBackupState State { get; set; } = RegionBackupState.Preparing;
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
}

public sealed class PendingRegionPreparation
{
    public int SchemaVersion { get; set; } = 2;
    public string GenerationId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public OverwatchRegion SourceRegion { get; set; }
    public OverwatchRegion TargetRegion { get; set; }
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
public sealed record RegionSwitchResult(
    int Restored,
    int Deleted,
    int ChinaOnlyProcessed = 0,
    int InternationalOnlyProcessed = 0,
    int DifferentRestored = 0,
    bool Verified = false,
    RegionSwitchEligibility Eligibility = RegionSwitchEligibility.Normal);

public sealed class RegionSnapshotStatus
{
    public bool GamePathValid { get; set; }
    public string GamePath { get; set; } = "";
    public RegionBackupState State { get; set; }
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
}
