using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CloudLightBlizzard.Services.OverwatchRegion;

public sealed class OverwatchRegionManager
{
    private readonly OverwatchRegionScanner _scanner = new();
    private readonly OverwatchRegionBackupStore _store;
    private readonly Func<bool> _isGameRunning;
    private readonly int _quiescenceMilliseconds;

    public string BackupRoot => _store.Root;
    public bool HasActiveGeneration => _store.LoadActive() is not null;

    public OverwatchRegionManager(string? storageRoot = null, Func<bool>? gameRunning = null,
        int quiescenceMilliseconds = 6000)
    {
        _store = new OverwatchRegionBackupStore(storageRoot);
        _isGameRunning = gameRunning ?? IsGameRunning;
        _quiescenceMilliseconds = quiescenceMilliseconds;
    }

    public static bool IsValidGameRoot(string? root) => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root) &&
                                                        OverwatchRegionScanner.FindExecutable(root) is not null;
    public static bool IsGameRunning()
    {
        // Battle.net 和 Agent 负责跨区更新，准备流程中必须允许它们运行。
        // Blizzard Launcher 是游戏随附的启动进程，需与 Overwatch.exe 一并视为游戏正在启动/运行。
        foreach (var processName in new[] { "Overwatch", "Blizzard Launcher" })
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Length > 0) return true;
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        return false;
    }

    public async Task<RegionBackupState> CaptureAsync(string gameRoot, OverwatchRegion region,
        IProgress<RegionProgress>? progress = null, CancellationToken cancellationToken = default,
        RegionBackupMode backupMode = RegionBackupMode.FullSnapshot)
    {
        var pending = _store.LoadPending();
        if (pending is null) return await StartPreparationAsync(gameRoot, region, backupMode, progress, cancellationToken);
        if (pending.BackupMode == RegionBackupMode.FullSnapshot && pending.TargetRegion != region)
            throw new InvalidOperationException($"请先在 Battle.net 中切换到{RegionName(pending.TargetRegion)}，等待更新完成后再继续。");
        return await ContinuePreparationAsync(gameRoot, progress, cancellationToken);
    }

    public async Task<RegionBackupState> StartPreparationAsync(string gameRoot, OverwatchRegion sourceRegion,
        RegionBackupMode backupMode = RegionBackupMode.FullSnapshot,
        IProgress<RegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (backupMode == RegionBackupMode.VerifiedDifference)
            return await StartVerifiedDifferenceAsync(gameRoot, sourceRegion, progress, cancellationToken);
        EnsureGameReady(gameRoot);
        if (_store.LoadPending() is not null)
            throw new InvalidOperationException("已有一次区服文件准备正在进行，请继续完成或先取消。");

        await _scanner.WaitForQuiescenceAsync(gameRoot, progress, cancellationToken, _quiescenceMilliseconds);
        var manifest = await _scanner.ScanAsync(gameRoot, sourceRegion, progress, cancellationToken);
        var generationId = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var stagingRoot = _store.StagingRegionRoot(generationId, sourceRegion);
        var bytes = manifest.Files.Values.Sum(entry => entry.Size);
        EnsureDiskSpace(_store.StagingRoot, bytes);

        try
        {
            await CopyManifestFilesAsync(gameRoot, stagingRoot, manifest, "正在保存当前" + RegionName(sourceRegion) + "文件…",
                progress, cancellationToken);
            _store.SaveStagingManifest(generationId, manifest);
            _store.SavePending(new PendingRegionPreparation
            {
                GenerationId = generationId,
                BackupMode = RegionBackupMode.FullSnapshot,
                SourceRegion = sourceRegion,
                TargetRegion = Other(sourceRegion),
            });
            return RegionBackupState.Preparing;
        }
        catch (OperationCanceledException)
        {
            try { _store.DeleteStaging(generationId); } catch { }
            throw;
        }
        catch
        {
            try { _store.DeleteStaging(generationId); } catch { }
            throw;
        }
    }

    public async Task<RegionBackupState> CompleteAsync(string gameRoot, IProgress<RegionProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        await ContinuePreparationAsync(gameRoot, progress, cancellationToken);

    public async Task<RegionBackupState> ContinuePreparationAsync(string gameRoot,
        IProgress<RegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureGameReady(gameRoot);
        var pending = _store.LoadPending() ?? throw new InvalidOperationException("当前没有等待继续的区服文件准备任务。");
        if (pending.BackupMode == RegionBackupMode.VerifiedDifference)
            return pending.Checkpoint == RegionPreparationCheckpoint.Step1Ready
                ? await CaptureVerifiedOtherRegionAsync(gameRoot, pending, progress, cancellationToken)
                : await VerifyAndCommitDifferenceAsync(gameRoot, pending, progress, cancellationToken);
        var sourceManifest = _store.LoadStagingManifest(pending.GenerationId, pending.SourceRegion) ??
                             throw new InvalidDataException("本地保存的源区服文件记录不完整，请重新准备。");
        var stagingSource = _store.StagingRegionRoot(pending.GenerationId, pending.SourceRegion);

        await _scanner.WaitForQuiescenceAsync(gameRoot, progress, cancellationToken, _quiescenceMilliseconds);
        var targetManifest = await _scanner.ScanAsync(gameRoot, pending.TargetRegion, progress, cancellationToken);
        var china = pending.SourceRegion == OverwatchRegion.China ? sourceManifest : targetManifest;
        var international = pending.SourceRegion == OverwatchRegion.International ? sourceManifest : targetManifest;
        progress?.Report(new RegionProgress("正在比较国服和国际服文件…"));
        var differences = Compare(china, international);
        var changed = differences.Where(item => item.Kind != RegionDifferenceKind.Same).ToList();
        var generation = new OverwatchRegionGeneration
        {
            GenerationId = pending.GenerationId,
            BackupMode = RegionBackupMode.FullSnapshot,
            SourceRegion = pending.SourceRegion,
            TargetRegion = pending.TargetRegion,
            ChinaManifestId = china.ManifestId,
            InternationalManifestId = international.ManifestId,
            ChinaBuildFingerprint = china.BuildFingerprint,
            InternationalBuildFingerprint = international.BuildFingerprint,
            CommonBaselineFingerprint = ComputeCommonBaseline(differences),
            Differences = changed,
        };

        var required = changed.Sum(item => (item.China?.Size ?? 0) + (item.International?.Size ?? 0));
        EnsureDiskSpace(_store.GenerationsRoot, required);
        var generationRoot = _store.GenerationRoot(pending.GenerationId);
        try
        {
            Directory.CreateDirectory(generationRoot);
            await BackupDifferencesAsync(gameRoot, stagingSource, pending.SourceRegion, generation, progress,
                cancellationToken);
            generation.ChinaBackupComplete = true;
            generation.InternationalBackupComplete = true;
            generation.State = RegionBackupState.Ready;
            _store.SaveGenerationManifest(generation.GenerationId, china);
            _store.SaveGenerationManifest(generation.GenerationId, international);
            _store.SaveGeneration(generation);
            WriteDiagnostic(generation, china, international);
            // The second captured region is still the live on-disk region when this generation becomes active.
            _store.Activate(generation.GenerationId, pending.TargetRegion);
            _store.DeleteStaging(generation.GenerationId);
            return RegionBackupState.Ready;
        }
        catch (OperationCanceledException)
        {
            try { if (Directory.Exists(generationRoot)) Directory.Delete(generationRoot, true); } catch { }
            throw;
        }
        catch
        {
            if (_store.LoadPointer()?.GenerationId != pending.GenerationId)
            {
                try { if (Directory.Exists(generationRoot)) Directory.Delete(generationRoot, true); } catch { }
            }
            throw;
        }
    }

    public async Task<RegionSwitchResult> SwitchGameRegionAsync(string gameRoot, OverwatchRegion target,
        IProgress<RegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureGameReady(gameRoot);
        var active = _store.LoadActive() ?? throw new InvalidOperationException(
            _store.HasLegacyData ? "区服文件功能已经升级，需要重新准备一次本地文件。" : "尚未准备国服和国际服文件。");
        var generation = active.Generation;
        var compatibility = await EvaluateCompatibilityAsync(gameRoot, generation, cancellationToken);
        var eligibility = await EvaluateSwitchEligibilityAsync(generation, compatibility, target,
            hashBackups: true, cancellationToken);
        DetectionResult detection;
        try
        {
            detection = eligibility.Status == RegionSwitchEligibility.Normal
                ? await DetectCurrentRegionAsync(gameRoot, active.Pointer, generation, compatibility,
                    persistStrongCorrection: false, cancellationToken)
                : DetectionResult.Unknown;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (generation.BackupMode == RegionBackupMode.VerifiedDifference &&
                                   IsPerFileException(ex))
        {
            detection = DetectionResult.Unknown;
            RegionSwitchLog.Write("NormalizeDetectionDegraded", target,
                compatibility: compatibility.Status, generationId: generation.GenerationId,
                detail: "单个已知文件无法读取，改为逐文件恢复：" + ex);
        }
        var current = detection.DetectedRegion;
        RegionSwitchLog.Write("NormalizeBegin", target, current, compatibility.Status, generation.GenerationId,
            $"Reason={compatibility.Reason}; SwitchMode={eligibility.Status}; " +
            $"EligibilityReason={eligibility.Reason}; KnownDifferences={generation.Differences.Count:N0}; " +
            "IgnoredUnknownFiles=未枚举，未参与处理");
        if (eligibility.Status is RegionSwitchEligibility.GameUpdated or RegionSwitchEligibility.BackupUnavailable)
        {
            RegionSwitchLog.Write("NormalizeRejected", target, current, compatibility.Status,
                generation.GenerationId, $"SwitchMode={eligibility.Status}; Reason={eligibility.Reason}");
            throw new InvalidDataException(eligibility.Status == RegionSwitchEligibility.GameUpdated
                ? "检测到《守望先锋》已经更新。当前本地区服文件基于旧版本，为了避免覆盖新版游戏，需要重新准备一次区服文件。原因：" + compatibility.Reason
                : "本地区服备份不完整，已停止切换。原因：" + eligibility.Reason);
        }
        if (current == ToCurrent(target) && detection.ExactSnapshotMatch)
        {
            var targetEvidence = target == OverwatchRegion.China
                ? RegionEvidenceResult.StrongChina : RegionEvidenceResult.StrongInternational;
            if (detection.Evidence == targetEvidence &&
                (active.Pointer.LastSuccessfulRegion != target ||
                 !string.Equals(active.Pointer.LastSuccessfulGenerationId, generation.GenerationId,
                     StringComparison.OrdinalIgnoreCase)))
                _store.SaveLastSuccessfulRegion(generation.GenerationId, target);
            RegionSwitchLog.Write("NormalizeAlreadyTarget", target, current, compatibility.Status,
                generation.GenerationId,
                $"SwitchMode={eligibility.Status}; KnownDifferences={generation.Differences.Count:N0}; Verification=passed");
            return new RegionSwitchResult(0, 0, Verified: true, Eligibility: eligibility.Status);
        }

        var restored = 0;
        var deleted = 0;
        var chinaOnly = 0;
        var internationalOnly = 0;
        var different = 0;
        var issues = new List<RegionFileIssue>();
        var completedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tolerateFileIssues = generation.BackupMode == RegionBackupMode.VerifiedDifference;
        for (var i = 0; i < generation.Differences.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var difference = generation.Differences[i];
            if (difference.Kind == RegionDifferenceKind.Same) continue;
            progress?.Report(new RegionProgress($"正在切换到{RegionName(target)}… {i + 1:N0} / {generation.Differences.Count:N0}",
                i + 1, generation.Differences.Count));
            try
            {
                var expected = target == OverwatchRegion.China ? difference.China : difference.International;
                var destination = OverwatchRegionBackupStore.SafeCombine(gameRoot, difference.RelativePath);
                if (expected is null)
                {
                    if (File.Exists(destination))
                    {
                        DeleteFilePreservingOnFailure(destination);
                        deleted++;
                    }
                }
                else
                {
                    var source = _store.BackupFile(generation.GenerationId, target, difference.RelativePath);
                    var sourceInspection = InspectFile(source, expected, cancellationToken);
                    if (sourceInspection.Status != FileInspectionStatus.Match)
                        throw new InvalidDataException("本地目标备份缺失或校验失败：" + sourceInspection.Reason);
                    await RestoreAtomicallyAsync(source, destination, expected, cancellationToken);
                    var restoredInspection = InspectFile(destination, expected, cancellationToken);
                    if (restoredInspection.Status != FileInspectionStatus.Match)
                        throw new InvalidDataException("原子恢复后的校验失败：" + restoredInspection.Reason);
                    restored++;
                    if (difference.Kind == RegionDifferenceKind.Different) different++;
                }
                if (difference.Kind == RegionDifferenceKind.ChinaOnly) chinaOnly++;
                if (difference.Kind == RegionDifferenceKind.InternationalOnly) internationalOnly++;
                completedPaths.Add(difference.RelativePath);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (tolerateFileIssues && IsPerFileException(ex))
            {
                issues.Add(new RegionFileIssue { RelativePath = difference.RelativePath, Reason = ex.Message });
                RegionSwitchLog.Write("NormalizeFileSkipped", target, current, compatibility.Status,
                    generation.GenerationId, $"Path={difference.RelativePath}; Reason={ex}");
            }
        }

        foreach (var difference in generation.Differences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (difference.Kind == RegionDifferenceKind.Same || !completedPaths.Contains(difference.RelativePath)) continue;
            var expected = target == OverwatchRegion.China ? difference.China : difference.International;
            var destination = OverwatchRegionBackupStore.SafeCombine(gameRoot, difference.RelativePath);
            try
            {
                if (!FileMatches(destination, expected, hash: true, cancellationToken))
                    throw new InvalidDataException("区服文件恢复后的完整校验失败");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (tolerateFileIssues && IsPerFileException(ex))
            {
                completedPaths.Remove(difference.RelativePath);
                issues.Add(new RegionFileIssue { RelativePath = difference.RelativePath, Reason = ex.Message });
                RegionSwitchLog.Write("NormalizeFilePostValidationFailed", target, current, compatibility.Status,
                    generation.GenerationId, $"Path={difference.RelativePath}; Reason={ex}");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (issues.Count > 0)
        {
            var outcome = completedPaths.Count > 0
                ? RegionSwitchOutcome.PartialSuccess : RegionSwitchOutcome.Failed;
            var partial = new RegionSwitchResult(restored, deleted, chinaOnly, internationalOnly, different,
                Verified: false, Eligibility: eligibility.Status, Outcome: outcome, FailedCount: issues.Count,
                Issues: issues);
            RegionSwitchLog.Write(outcome == RegionSwitchOutcome.PartialSuccess
                    ? "NormalizePartialCompleted" : "NormalizeAllFilesFailed",
                target, current, compatibility.Status, generation.GenerationId,
                $"SwitchMode={eligibility.Status}; SuccessfulEntries={completedPaths.Count}; " +
                $"FailedEntries={issues.Count}; ActiveRegionState=unchanged");
            return partial;
        }
        if (!_store.SaveLastSuccessfulRegion(generation.GenerationId, target))
            throw new InvalidDataException("目标文件已经恢复，但 Active Generation 已变化，未更新当前区服状态。");
        var verifiedRegion = ToCurrent(target);

        var result = new RegionSwitchResult(restored, deleted, chinaOnly, internationalOnly, different, true,
            eligibility.Status, RegionSwitchOutcome.Success);
        RegionSwitchLog.Write("NormalizeCompleted", target, verifiedRegion, compatibility.Status,
            generation.GenerationId,
            $"SwitchMode={eligibility.Status}; KnownDifferences={generation.Differences.Count:N0}; " +
            $"ChinaOnlyProcessed={chinaOnly}; InternationalOnlyProcessed={internationalOnly}; " +
            $"DifferentRestored={different}; Restored={restored}; Deleted={deleted}; " +
            "IgnoredUnknownFiles=未枚举，未参与处理; Verification=passed");
        return result;
    }

    public async Task<RegionSwitchResult> NormalizeToRegionAsync(string gameRoot, OverwatchRegion target,
        IProgress<RegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await SwitchGameRegionAsync(gameRoot, target, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            RegionSwitchLog.Write("NormalizeFailed", target, detail:
                "Verification result=failed; reason=" + ex.Message);
            throw;
        }
    }

    public async Task<CurrentGameRegion> DetectCurrentRegionAsync(string gameRoot,
        CancellationToken cancellationToken = default)
    {
        var active = _store.LoadActive();
        if (active is null) return CurrentGameRegion.Unknown;
        var compatibility = await EvaluateCompatibilityAsync(gameRoot, active.Value.Generation, cancellationToken);
        if (compatibility.Status != GenerationCompatibility.Compatible)
        {
            RegionSwitchLog.Write("RegionDetected", current: CurrentGameRegion.Unknown,
                compatibility: compatibility.Status, generationId: active.Value.Generation.GenerationId,
                detail: $"LastSuccessfulRegion={active.Value.Pointer.LastSuccessfulRegion?.ToString() ?? "Unknown"}; " +
                        $"LastSuccessfulGenerationId={active.Value.Pointer.LastSuccessfulGenerationId ?? "-"}; {compatibility.Reason}");
            return CurrentGameRegion.Unknown;
        }
        return (await DetectCurrentRegionAsync(gameRoot, active.Value.Pointer, active.Value.Generation,
            compatibility, persistStrongCorrection: true, cancellationToken)).DetectedRegion;
    }

    public async Task<RegionSnapshotStatus> GetStatusAsync(string? gameRoot,
        CancellationToken cancellationToken = default, bool verifyFiles = true, bool verifyBackupHashes = false)
    {
        var valid = IsValidGameRoot(gameRoot);
        var pending = _store.LoadPending();
        var active = _store.LoadActive();
        if (pending is not null && active is null)
        {
            return new RegionSnapshotStatus
            {
                GamePath = gameRoot ?? "", GamePathValid = valid, State = RegionBackupState.Preparing,
                BackupMode = pending.BackupMode, PreparationCheckpoint = pending.Checkpoint,
                PendingSourceRegion = pending.SourceRegion, PendingTargetRegion = pending.TargetRegion,
                ChinaCaptured = pending.SourceRegion == OverwatchRegion.China,
                InternationalCaptured = pending.SourceRegion == OverwatchRegion.International,
                ActiveGenerationId = active?.Pointer.GenerationId,
                CandidateCount = pending.CandidateCount,
                CandidateBackupSavedCount = pending.CandidateBackupSavedCount,
                SkippedFileCount = pending.CandidateBackups.Count(item => item.Status == CandidateBackupStatus.Unavailable),
                HasWarnings = pending.Step1Warnings.Count > 0 ||
                              pending.CandidateBackups.Any(item => item.Status == CandidateBackupStatus.Unavailable),
                FileIssues = pending.Step1Warnings.Concat(pending.CandidateBackups
                    .Where(item => item.Status == CandidateBackupStatus.Unavailable)
                    .Select(item => new RegionFileIssue
                    {
                        RelativePath = item.RelativePath,
                        Reason = item.Reason,
                    })).ToList(),
            };
        }
        if (active is null)
        {
            return new RegionSnapshotStatus
            {
                GamePath = gameRoot ?? "", GamePathValid = valid,
                State = _store.HasLegacyData ? RegionBackupState.Legacy : RegionBackupState.Empty,
            };
        }

        var generation = active.Value.Generation;
        var pointer = active.Value.Pointer;
        var compatibility = verifyFiles && valid
            ? await EvaluateCompatibilityAsync(gameRoot!, generation, cancellationToken)
            : new CompatibilityResult(GenerationCompatibility.Unknown,
                valid ? "尚未执行完整文件校验" : "游戏目录无效");
        var eligibility = await EvaluateSwitchEligibilityAsync(generation, compatibility,
            target: null, hashBackups: verifyBackupHashes, cancellationToken);
        var detection = verifyFiles && valid && eligibility.Status == RegionSwitchEligibility.Normal &&
                        generation.State is RegionBackupState.Ready or RegionBackupState.Stale
            ? await DetectCurrentRegionAsync(gameRoot!, pointer, generation, compatibility,
                persistStrongCorrection: true, cancellationToken)
            : DetectionResult.FromLastSuccessful(pointer, generation.GenerationId);
        if (verifyFiles && eligibility.Status != RegionSwitchEligibility.Normal)
            detection = DetectionResult.Unknown;
        if (verifyFiles && compatibility.Status == GenerationCompatibility.Updated && generation.State != RegionBackupState.Stale)
        {
            generation.State = RegionBackupState.Stale;
            _store.SaveGeneration(generation);
        }
        else if (verifyFiles && compatibility.Status == GenerationCompatibility.Compatible && generation.State == RegionBackupState.Stale)
        {
            // Older builds marked every Mixed/Unknown directory stale. A positive common-baseline
            // verification safely re-enables that existing generation without rebuilding it.
            generation.State = RegionBackupState.Ready;
            _store.SaveGeneration(generation);
        }
        var backups = Path.Combine(_store.GenerationRoot(generation.GenerationId), "backups");
        return new RegionSnapshotStatus
        {
            GamePath = gameRoot ?? "", GamePathValid = valid,
            State = pending is null ? generation.State : RegionBackupState.Preparing,
            BackupMode = pending?.BackupMode ?? generation.BackupMode,
            PreparationCheckpoint = pending?.Checkpoint,
            PendingSourceRegion = pending?.SourceRegion,
            PendingTargetRegion = pending?.TargetRegion,
            CurrentRegion = detection.DetectedRegion, ChinaCaptured = true, InternationalCaptured = true,
            GenerationCompatibility = compatibility.Status,
            CompatibilityReason = compatibility.Reason,
            SwitchEligibility = eligibility.Status,
            SwitchEligibilityReason = eligibility.Reason,
            BackupFileIssueCount = eligibility.FileIssueCount,
            ChinaBackupComplete = generation.ChinaBackupComplete,
            InternationalBackupComplete = generation.InternationalBackupComplete,
            DifferenceCount = generation.Differences.Count,
            BackupBytes = Directory.Exists(backups)
                ? Directory.EnumerateFiles(backups, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) : 0,
            ActiveGenerationId = generation.GenerationId,
            LastSuccessfulRegion = pointer.LastSuccessfulRegion,
            LastSuccessfulGenerationId = pointer.LastSuccessfulGenerationId,
            RegionEvidence = detection.Evidence,
            ExactSnapshotMatch = detection.ExactSnapshotMatch,
            CandidateCount = pending?.CandidateCount ?? generation.VerificationSummary?.CandidateCount ?? 0,
            CandidateBackupSavedCount = pending?.CandidateBackupSavedCount ?? 0,
            RejectedCount = pending is null ? generation.VerificationSummary?.RejectedCount ?? 0 : 0,
            SkippedFileCount = pending is null
                ? generation.VerificationSummary?.SkippedFileCount ?? 0
                : pending.CandidateBackups.Count(item => item.Status == CandidateBackupStatus.Unavailable),
            HasWarnings = pending is null
                ? generation.VerificationSummary?.HasWarnings ?? false
                : pending.Step1Warnings.Count > 0 ||
                  pending.CandidateBackups.Any(item => item.Status == CandidateBackupStatus.Unavailable),
            FileIssues = pending is null
                ? generation.VerificationSummary?.Results
                    .Where(item => item.Outcome == CandidateVerificationOutcome.FileIssueSkipped)
                    .Select(item => new RegionFileIssue { RelativePath = item.RelativePath, Reason = item.Reason })
                    .ToList() ?? new List<RegionFileIssue>()
                : pending.Step1Warnings.Concat(pending.CandidateBackups
                    .Where(item => item.Status == CandidateBackupStatus.Unavailable)
                    .Select(item => new RegionFileIssue
                    {
                        RelativePath = item.RelativePath,
                        Reason = item.Reason,
                    })).ToList(),
        };
    }

    public void Reset() => _store.Clear();

    public void CancelPreparation()
    {
        var pending = _store.LoadPending();
        if (pending is null) return;
        if (pending.BackupMode == RegionBackupMode.VerifiedDifference) _store.DeletePreparation();
        else _store.DeleteStaging(pending.GenerationId);
    }

    public static List<RegionDifference> Compare(OverwatchRegionManifest china,
        OverwatchRegionManifest international)
    {
        var paths = china.Files.Keys.Union(international.Files.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        var result = new List<RegionDifference>();
        foreach (var path in paths)
        {
            china.Files.TryGetValue(path, out var chinaEntry);
            international.Files.TryGetValue(path, out var internationalEntry);
            var kind = chinaEntry is null ? RegionDifferenceKind.InternationalOnly :
                internationalEntry is null ? RegionDifferenceKind.ChinaOnly :
                FilesEqual(chinaEntry, internationalEntry) ? RegionDifferenceKind.Same : RegionDifferenceKind.Different;
            result.Add(new RegionDifference
            {
                RelativePath = path, Kind = kind, China = chinaEntry, International = internationalEntry,
            });
        }
        return result;
    }

    private async Task BackupDifferencesAsync(string currentRoot, string stagedSourceRoot,
        OverwatchRegion stagedRegion, OverwatchRegionGeneration generation, IProgress<RegionProgress>? progress,
        CancellationToken token)
    {
        var operations = new List<(OverwatchRegion Region, RegionFileEntry Entry, string Source)>();
        // 日常识别优先看独占、大小不同和较小文件，通常只需检查极少数文件，
        // 不会像建立 Generation 时那样重新 Hash 整个游戏目录。
        foreach (var difference in generation.Differences
                     .OrderBy(item => item.China is null || item.International is null ? 0 :
                         item.China.Size != item.International.Size ? 1 : 2)
                     .ThenBy(item => Math.Max(item.China?.Size ?? 0, item.International?.Size ?? 0)))
        {
            if (difference.China is { } china)
                operations.Add((OverwatchRegion.China, china, OverwatchRegionBackupStore.SafeCombine(
                    stagedRegion == OverwatchRegion.China ? stagedSourceRoot : currentRoot, difference.RelativePath)));
            if (difference.International is { } international)
                operations.Add((OverwatchRegion.International, international, OverwatchRegionBackupStore.SafeCombine(
                    stagedRegion == OverwatchRegion.International ? stagedSourceRoot : currentRoot, difference.RelativePath)));
        }
        var totalBytes = operations.Sum(item => item.Entry.Size);
        long completedBytes = 0;
        for (var i = 0; i < operations.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var operation = operations[i];
            progress?.Report(new RegionProgress($"正在保存区服差异… {i + 1:N0} / {operations.Count:N0}",
                i + 1, operations.Count, completedBytes, totalBytes));
            if (!FileMatches(operation.Source, operation.Entry, true, token))
                throw new IOException("准备期间游戏文件发生变化，请等待 Battle.net 完成更新后重试：" + operation.Entry.RelativePath);
            var destination = _store.BackupFile(generation.GenerationId, operation.Region, operation.Entry.RelativePath);
            await CopyFileAsync(operation.Source, destination, token);
            File.SetLastWriteTimeUtc(destination, operation.Entry.LastWriteTimeUtc);
            if (!FileMatches(destination, operation.Entry, true, token))
                throw new InvalidDataException("本地文件校验失败：" + operation.Entry.RelativePath);
            completedBytes += operation.Entry.Size;
        }
    }

    private static async Task CopyManifestFilesAsync(string sourceRoot, string destinationRoot,
        OverwatchRegionManifest manifest, string message, IProgress<RegionProgress>? progress, CancellationToken token)
    {
        var entries = manifest.Files.Values.OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        var totalBytes = entries.Sum(entry => entry.Size);
        long completedBytes = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = entries[i];
            progress?.Report(new RegionProgress(message + $" {FormatBytes(completedBytes)} / {FormatBytes(totalBytes)}",
                i, entries.Count, completedBytes, totalBytes));
            var source = OverwatchRegionBackupStore.SafeCombine(sourceRoot, entry.RelativePath);
            if (!FileMatches(source, entry, true, token))
                throw new IOException("准备期间游戏文件发生变化，请等待 Battle.net 完成更新后重试：" + entry.RelativePath);
            var destination = OverwatchRegionBackupStore.SafeCombine(destinationRoot, entry.RelativePath);
            await CopyFileAsync(source, destination, token);
            File.SetLastWriteTimeUtc(destination, entry.LastWriteTimeUtc);
            if (!FileMatches(destination, entry, true, token))
                throw new InvalidDataException("临时保存的文件校验失败：" + entry.RelativePath);
            completedBytes += entry.Size;
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, 1024 * 1024, token);
        await output.FlushAsync(token);
    }

    private static async Task RestoreAtomicallyAsync(string source, string destination, RegionFileEntry expected,
        CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".cloudlightblizzard-" + Guid.NewGuid().ToString("N") + ".tmp";
        FileAttributes? originalAttributes = null;
        try
        {
            await CopyFileAsync(source, temp, token);
            if (!FileMatches(temp, expected, true, token))
                throw new InvalidDataException("恢复后的文件校验失败：" + expected.RelativePath);
            if (expected.LastWriteTimeUtc > DateTime.UnixEpoch)
                File.SetLastWriteTimeUtc(temp, expected.LastWriteTimeUtc);
            if (File.Exists(destination)) originalAttributes = File.GetAttributes(destination);
            ClearReadOnly(destination);
            File.Move(temp, destination, true);
        }
        catch
        {
            if (originalAttributes is not null && File.Exists(destination))
                try { File.SetAttributes(destination, originalAttributes.Value); } catch { }
            throw;
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    private async Task<DetectionResult> DetectCurrentRegionAsync(string root, ActiveGenerationPointer pointer,
        OverwatchRegionGeneration generation, CompatibilityResult compatibility, bool persistStrongCorrection,
        CancellationToken token)
    {
        if (generation.State is not (RegionBackupState.Ready or RegionBackupState.Stale))
            return DetectionResult.Unknown;
        var chinaOnlyPresent = 0;
        var internationalOnlyPresent = 0;
        var differentChinaMatches = 0;
        var differentInternationalMatches = 0;
        var chinaSnapshotMatches = 0;
        var internationalSnapshotMatches = 0;
        var differentMismatches = new List<string>();
        var chinaSnapshotMismatches = new List<string>();
        var internationalSnapshotMismatches = new List<string>();
        foreach (var difference in generation.Differences)
        {
            token.ThrowIfCancellationRequested();
            var path = OverwatchRegionBackupStore.SafeCombine(root, difference.RelativePath);
            if (difference.Kind == RegionDifferenceKind.ChinaOnly && File.Exists(path)) chinaOnlyPresent++;
            if (difference.Kind == RegionDifferenceKind.InternationalOnly && File.Exists(path)) internationalOnlyPresent++;

            bool china;
            bool international;
            try
            {
                // Snapshot diagnostics remain byte-exact even though region classification no longer requires
                // every file to match one side.
                china = FileMatches(path, difference.China, hash: true, token);
                international = FileMatches(path, difference.International, hash: true, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (generation.BackupMode == RegionBackupMode.VerifiedDifference &&
                                       IsPerFileException(ex))
            {
                chinaSnapshotMismatches.Add(difference.RelativePath);
                internationalSnapshotMismatches.Add(difference.RelativePath);
                if (difference.Kind == RegionDifferenceKind.Different)
                    differentMismatches.Add(difference.RelativePath);
                RegionSwitchLog.Write("RegionDetectionFileSkipped",
                    generationId: generation.GenerationId,
                    detail: $"Path={difference.RelativePath}; Reason={ex}");
                await Task.Yield();
                continue;
            }
            if (china) chinaSnapshotMatches++;
            else chinaSnapshotMismatches.Add(difference.RelativePath);
            if (international) internationalSnapshotMatches++;
            else internationalSnapshotMismatches.Add(difference.RelativePath);
            if (difference.Kind == RegionDifferenceKind.Different)
            {
                if (china && !international) differentChinaMatches++;
                else if (international && !china) differentInternationalMatches++;
                else if (!china && !international) differentMismatches.Add(difference.RelativePath);
            }
            await Task.Yield();
        }

        var evidence = ClassifyEvidence(chinaOnlyPresent, internationalOnlyPresent,
            differentChinaMatches, differentInternationalMatches);
        var lastMatchesGeneration = pointer.LastSuccessfulRegion is not null &&
                                    string.Equals(pointer.LastSuccessfulGenerationId, generation.GenerationId,
                                        StringComparison.OrdinalIgnoreCase);
        var rememberedSnapshotMatches = pointer.LastSuccessfulRegion switch
        {
            OverwatchRegion.China => chinaSnapshotMatches,
            OverwatchRegion.International => internationalSnapshotMatches,
            _ => 0,
        };
        var severeSnapshotDamage = lastMatchesGeneration && generation.Differences.Count >= 4 &&
                                   rememberedSnapshotMatches * 2 < generation.Differences.Count;
        var detected = evidence switch
        {
            RegionEvidenceResult.StrongChina => CurrentGameRegion.China,
            RegionEvidenceResult.StrongInternational => CurrentGameRegion.International,
            RegionEvidenceResult.StrongConflict => CurrentGameRegion.Mixed,
            _ when lastMatchesGeneration && !severeSnapshotDamage => ToCurrent(pointer.LastSuccessfulRegion!.Value),
            _ => CurrentGameRegion.Unknown,
        };
        var exact = detected switch
        {
            CurrentGameRegion.China => chinaSnapshotMatches == generation.Differences.Count,
            CurrentGameRegion.International => internationalSnapshotMatches == generation.Differences.Count,
            _ => false,
        };

        var stronglyDetectedRegion = evidence switch
        {
            RegionEvidenceResult.StrongChina => OverwatchRegion.China,
            RegionEvidenceResult.StrongInternational => OverwatchRegion.International,
            _ => (OverwatchRegion?)null,
        };
        if (persistStrongCorrection && stronglyDetectedRegion is not null &&
            (!lastMatchesGeneration || pointer.LastSuccessfulRegion != stronglyDetectedRegion))
        {
            if (_store.SaveLastSuccessfulRegion(generation.GenerationId, stronglyDetectedRegion.Value))
            {
                pointer.LastSuccessfulRegion = stronglyDetectedRegion;
                pointer.LastSuccessfulGenerationId = generation.GenerationId;
            }
        }

        var snapshotMismatches = detected switch
        {
            CurrentGameRegion.China => chinaSnapshotMismatches,
            CurrentGameRegion.International => internationalSnapshotMismatches,
            _ when lastMatchesGeneration && pointer.LastSuccessfulRegion == OverwatchRegion.China =>
                chinaSnapshotMismatches,
            _ when lastMatchesGeneration && pointer.LastSuccessfulRegion == OverwatchRegion.International =>
                internationalSnapshotMismatches,
            _ => new List<string>(),
        };

        RegionSwitchLog.Write("RegionDetected", current: detected, compatibility: compatibility.Status,
            generationId: generation.GenerationId,
            detail: $"LastSuccessfulRegion={pointer.LastSuccessfulRegion?.ToString() ?? "Unknown"}; " +
                    $"LastSuccessfulGenerationId={pointer.LastSuccessfulGenerationId ?? "-"}; " +
                    $"StrongEvidence={evidence}; StrongChinaEvidenceCount={chinaOnlyPresent + differentChinaMatches}; " +
                    $"StrongInternationalEvidenceCount={internationalOnlyPresent + differentInternationalMatches}; " +
                    $"ChinaOnlyPresentCount={chinaOnlyPresent}; InternationalOnlyPresentCount={internationalOnlyPresent}; " +
                    $"DifferentMismatchCount={differentMismatches.Count}; " +
                    $"DifferentMismatchFiles={(differentMismatches.Count == 0 ? "-" : string.Join(", ", differentMismatches))}; " +
                    $"DetectedSnapshotMismatchCount={snapshotMismatches.Count}; " +
                    $"DetectedSnapshotMismatchFiles={(snapshotMismatches.Count == 0 ? "-" : string.Join(", ", snapshotMismatches))}; " +
                    $"SevereSnapshotDamage={severeSnapshotDamage}; " +
                    $"ExactSnapshotMatch={exact}");
        return new DetectionResult(detected, evidence, exact);
    }

    internal static RegionEvidenceResult ClassifyEvidence(int chinaOnlyPresent, int internationalOnlyPresent,
        int differentChinaMatches, int differentInternationalMatches)
    {
        if (chinaOnlyPresent > 0 && internationalOnlyPresent > 0)
        {
            // A few stale files from the opposite side are common after Battle.net maintenance. Only a
            // clear numerical advantage may override that residue; close counts remain a real conflict.
            const int minimumExclusiveLead = 2;
            if (chinaOnlyPresent >= internationalOnlyPresent * 2 &&
                chinaOnlyPresent - internationalOnlyPresent >= minimumExclusiveLead)
                return RegionEvidenceResult.StrongChina;
            if (internationalOnlyPresent >= chinaOnlyPresent * 2 &&
                internationalOnlyPresent - chinaOnlyPresent >= minimumExclusiveLead)
                return RegionEvidenceResult.StrongInternational;
            return RegionEvidenceResult.StrongConflict;
        }
        if (chinaOnlyPresent > 0) return RegionEvidenceResult.StrongChina;
        if (internationalOnlyPresent > 0) return RegionEvidenceResult.StrongInternational;

        // Difference files are supporting evidence only. One ordinary match/mismatch is never enough to
        // establish or correct the region; multiple unopposed exact matches are required.
        const int minimumSupportingMatches = 2;
        if (differentChinaMatches >= minimumSupportingMatches && differentInternationalMatches >= minimumSupportingMatches)
            return RegionEvidenceResult.StrongConflict;
        if (differentChinaMatches >= minimumSupportingMatches && differentInternationalMatches == 0)
            return RegionEvidenceResult.StrongChina;
        if (differentInternationalMatches >= minimumSupportingMatches && differentChinaMatches == 0)
            return RegionEvidenceResult.StrongInternational;
        return RegionEvidenceResult.NoStrongConflict;
    }

    private static bool FileMatches(string path, RegionFileEntry? expected, bool hash, CancellationToken token)
    {
        if (expected is null) return !File.Exists(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expected.Size) return false;
        return !hash || string.Equals(OverwatchRegionScanner.ComputeHash(path, token), expected.Sha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFileMatches(string path, RegionFileEntry? expected, CancellationToken token)
    {
        try { return FileMatches(path, expected, hash: true, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static FileInspectionResult InspectFile(string path, RegionFileEntry? expected, CancellationToken token)
    {
        try
        {
            if (expected is null)
                return File.Exists(path)
                    ? new FileInspectionResult(FileInspectionStatus.Mismatch, "文件应不存在但当前仍存在")
                    : new FileInspectionResult(FileInspectionStatus.Match, "");
            var info = new FileInfo(path);
            if (!info.Exists)
                return new FileInspectionResult(FileInspectionStatus.Mismatch, "文件缺失");
            if (info.Length != expected.Size)
                return new FileInspectionResult(FileInspectionStatus.Mismatch,
                    $"size 不匹配（expected={expected.Size}, actual={info.Length}）");
            var actualHash = OverwatchRegionScanner.ComputeHash(path, token);
            return string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase)
                ? new FileInspectionResult(FileInspectionStatus.Match, "")
                : new FileInspectionResult(FileInspectionStatus.Mismatch, "Hash 不匹配");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (IsPerFileException(ex))
        {
            return new FileInspectionResult(FileInspectionStatus.Issue, ex.Message);
        }
    }

    private static bool IsPerFileException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException;

    private static void AddCandidateResult(List<RegionCandidateResult> results, string relativePath,
        CandidateVerificationOutcome outcome, string reason)
    {
        results.RemoveAll(item => string.Equals(item.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        results.Add(new RegionCandidateResult { RelativePath = relativePath, Outcome = outcome, Reason = reason });
    }

    private static void LogCandidateIssue(string operation, OverwatchRegion current, string relativePath,
        string reason) => RegionSwitchLog.Write(operation, current: ToCurrent(current),
        detail: $"Path={relativePath}; Reason={reason}");

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            ClearReadOnly(path);
            File.Delete(path);
        }
        catch { }
    }

    private static void DeleteFilePreservingOnFailure(string path)
    {
        var attributes = File.GetAttributes(path);
        try
        {
            if (attributes.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
        catch
        {
            if (File.Exists(path))
                try { File.SetAttributes(path, attributes); } catch { }
            throw;
        }
    }

    private void TryDeleteGenerationEntryBackups(string generationId, string relativePath)
    {
        TryDeleteFile(_store.BackupFile(generationId, OverwatchRegion.China, relativePath));
        TryDeleteFile(_store.BackupFile(generationId, OverwatchRegion.International, relativePath));
    }

    private static string SafeHash(string path, CancellationToken token)
    {
        try { return OverwatchRegionScanner.ComputeHash(path, token); }
        catch (OperationCanceledException) { throw; }
        catch { return "unreadable"; }
    }

    private static OverwatchRegionManifest CreateVerifiedManifest(OverwatchRegion region,
        OverwatchRegionManifest source, IEnumerable<RegionDifference> verified)
    {
        var manifest = new OverwatchRegionManifest
        {
            Region = region,
            BuildFingerprint = source.BuildFingerprint,
        };
        foreach (var difference in verified)
        {
            var entry = region == OverwatchRegion.China ? difference.China : difference.International;
            if (entry is not null) manifest.Files[difference.RelativePath] = entry;
        }
        return manifest;
    }

    private async Task<CompatibilityResult> EvaluateCompatibilityAsync(string root,
        OverwatchRegionGeneration generation, CancellationToken token)
    {
        try
        {
            if (generation.BackupMode == RegionBackupMode.VerifiedDifference)
            {
                var chinaVerified = _store.LoadGenerationManifest(generation.GenerationId, OverwatchRegion.China);
                var internationalVerified = _store.LoadGenerationManifest(generation.GenerationId,
                    OverwatchRegion.International);
                if (chinaVerified is null || internationalVerified is null)
                    return new CompatibilityResult(GenerationCompatibility.Unknown,
                        "智能差异 Generation Manifest 缺失");
                return new CompatibilityResult(GenerationCompatibility.Compatible,
                    "智能差异备份只校验已确认的区服差异文件，不要求完整游戏目录匹配");
            }
            var actual = OverwatchRegionScanner.ReadBuildFingerprint(root);
            if (!ExecutableMatches(actual, generation.ChinaBuildFingerprint) &&
                !ExecutableMatches(actual, generation.InternationalBuildFingerprint))
                return new CompatibilityResult(GenerationCompatibility.Updated,
                    $"Overwatch.exe 版本或大小变化（当前 ProductVersion={actual.ExecutableProductVersion}, Size={actual.ExecutableSize}）");

            var china = _store.LoadGenerationManifest(generation.GenerationId, OverwatchRegion.China);
            var international = _store.LoadGenerationManifest(generation.GenerationId, OverwatchRegion.International);
            if (china is null || international is null)
                return new CompatibilityResult(GenerationCompatibility.Unknown, "Generation Manifest 缺失");

            var manifestComparison = Compare(china, international);
            var expectedBaseline = ComputeCommonBaseline(manifestComparison);
            if (!string.IsNullOrWhiteSpace(generation.CommonBaselineFingerprint) &&
                !string.Equals(expectedBaseline, generation.CommonBaselineFingerprint, StringComparison.OrdinalIgnoreCase))
                return new CompatibilityResult(GenerationCompatibility.Unknown,
                    "Generation 公共基线 Fingerprint 与 Manifest 不一致");

            var commonCore = manifestComparison
                .Where(item => item.Kind == RegionDifferenceKind.Same && item.China is not null &&
                               OverwatchRegionScanner.IsCommonBaselineFile(item.RelativePath))
                .ToList();
            foreach (var item in commonCore)
            {
                token.ThrowIfCancellationRequested();
                var path = OverwatchRegionBackupStore.SafeCombine(root, item.RelativePath);
                if (!FileMatches(path, item.China, hash: true, token))
                    return new CompatibilityResult(GenerationCompatibility.Updated,
                        "公共 Same 核心文件变化：" + item.RelativePath);
                await Task.Yield();
            }
            return new CompatibilityResult(GenerationCompatibility.Compatible,
                commonCore.Count == 0
                    ? "Overwatch.exe 版本与 Active Generation 一致；无额外公共核心文件"
                    : $"Overwatch.exe 与 {commonCore.Count} 个公共 Same 核心文件一致");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CompatibilityResult(GenerationCompatibility.Unknown,
                $"读取当前游戏版本文件失败：{ex.Message}");
        }
    }

    private async Task<RegionBackupState> StartVerifiedDifferenceAsync(string gameRoot,
        OverwatchRegion sourceRegion, IProgress<RegionProgress>? progress, CancellationToken cancellationToken)
    {
        EnsureGameReady(gameRoot);
        if (_store.LoadPending() is not null)
            throw new InvalidOperationException("已有一次区服文件准备正在进行，请继续完成或先重新开始。");

        progress?.Report(new RegionProgress("正在记录当前区服文件状态……"));
        await _scanner.WaitForQuiescenceAsync(gameRoot, progress, cancellationToken, _quiescenceMilliseconds);
        var scan = await _scanner.ScanBestEffortAsync(gameRoot, sourceRegion, progress, cancellationToken);
        var manifest = scan.Manifest;
        KeepContentIdentityOnly(manifest);
        var generationId = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var pending = new PendingRegionPreparation
        {
            GenerationId = generationId,
            BackupMode = RegionBackupMode.VerifiedDifference,
            Checkpoint = RegionPreparationCheckpoint.Step1Ready,
            SourceRegion = sourceRegion,
            TargetRegion = Other(sourceRegion),
            Step1Warnings = scan.Issues.ToList(),
        };

        _store.DeletePreparation();
        var working = Path.Combine(_store.PreparationRoot, "step1-working-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(working);
            OverwatchRegionBackupStore.WriteJson(Path.Combine(working, "step1-manifest.json"), manifest);
            OverwatchRegionBackupStore.WriteJson(Path.Combine(working, "state.json"), pending);
            _store.CommitPreparationDirectory(working);
            progress?.Report(new RegionProgress($"当前区服文件状态记录完成，共扫描 {manifest.Files.Count:N0} 个文件" +
                                                (scan.Issues.Count > 0 ? $"，跳过 {scan.Issues.Count:N0} 个异常文件。" : "。"),
                manifest.Files.Count, manifest.Files.Count));
            RegionSwitchLog.Write("VerifiedDifferenceStep1Ready", current: ToCurrent(sourceRegion),
                detail: $"正在记录当前区服文件状态；Scanned={manifest.Files.Count:N0}; FileIssues={scan.Issues.Count:N0}");
            return RegionBackupState.Preparing;
        }
        catch
        {
            try { if (Directory.Exists(working)) Directory.Delete(working, true); } catch { }
            throw;
        }
    }

    private async Task<RegionBackupState> CaptureVerifiedOtherRegionAsync(string gameRoot,
        PendingRegionPreparation pending, IProgress<RegionProgress>? progress, CancellationToken cancellationToken)
    {
        var sourceManifest = _store.LoadPreparationManifest(1) ??
                             throw new InvalidDataException("第一步文件状态记录不完整，请重新执行第一步。");
        _store.ResetVerifiedToStep1(pending);
        var working = Path.Combine(_store.PreparationCurrentRoot,
            "step2-working-" + Guid.NewGuid().ToString("N"));
        try
        {
            progress?.Report(new RegionProgress("正在分析另一区服文件差异……"));
            await _scanner.WaitForQuiescenceAsync(gameRoot, progress, cancellationToken, _quiescenceMilliseconds);
            var targetScan = await _scanner.ScanBestEffortAsync(gameRoot, pending.TargetRegion, progress,
                cancellationToken);
            var targetManifest = targetScan.Manifest;
            KeepContentIdentityOnly(targetManifest);
            var china = pending.SourceRegion == OverwatchRegion.China ? sourceManifest : targetManifest;
            var international = pending.SourceRegion == OverwatchRegion.International ? sourceManifest : targetManifest;
            var candidates = Compare(china, international)
                .Where(item => item.Kind != RegionDifferenceKind.Same).ToList();
            var scanIssues = pending.Step1Warnings.Concat(targetScan.Issues)
                .GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => string.Join("；", group.Select(item => item.Reason).Distinct()),
                    StringComparer.OrdinalIgnoreCase);
            var candidatePaths = candidates.Select(item => item.RelativePath)
                .Union(scanIssues.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            var needed = candidates.Sum(item => (pending.TargetRegion == OverwatchRegion.China
                ? item.China?.Size : item.International?.Size) ?? 0);
            var workingCandidateRoot = Path.Combine(working, "candidate",
                pending.TargetRegion == OverwatchRegion.China ? "china" : "international");
            Directory.CreateDirectory(workingCandidateRoot);
            long completedBytes = 0;
            var savedCount = 0;
            var candidateBackups = candidatePaths.ToDictionary(path => path, path => new CandidateBackupRecord
            {
                RelativePath = path,
                Status = scanIssues.ContainsKey(path) ? CandidateBackupStatus.Unavailable : CandidateBackupStatus.Available,
                Reason = scanIssues.GetValueOrDefault(path, ""),
            }, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < candidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = candidates[i];
                var backup = candidateBackups[candidate.RelativePath];
                if (backup.Status == CandidateBackupStatus.Unavailable) continue;
                var entry = pending.TargetRegion == OverwatchRegion.China
                    ? candidate.China : candidate.International;
                if (entry is null) continue;
                progress?.Report(new RegionProgress(
                    $"正在保存候选区服差异… {i + 1:N0} / {candidates.Count:N0}", i + 1,
                    candidates.Count, completedBytes, needed));
                var source = OverwatchRegionBackupStore.SafeCombine(gameRoot, entry.RelativePath);
                var destination = OverwatchRegionBackupStore.SafeCombine(workingCandidateRoot, entry.RelativePath);
                try
                {
                    if (!FileMatches(source, entry, hash: true, cancellationToken))
                        throw new IOException("当前文件与扫描记录的大小或 Hash 不匹配");
                    await RestoreAtomicallyAsync(source, destination, entry, cancellationToken);
                    completedBytes += entry.Size;
                    savedCount++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (IsPerFileException(ex))
                {
                    TryDeleteFile(destination);
                    backup.Status = CandidateBackupStatus.Unavailable;
                    backup.Reason = "候选备份不可用：" + ex.Message;
                    RegionSwitchLog.Write("VerifiedDifferenceCandidateBackupSkipped",
                        current: ToCurrent(pending.TargetRegion),
                        detail: $"Path={candidate.RelativePath}; Reason={ex}");
                }
            }

            OverwatchRegionBackupStore.WriteJson(Path.Combine(working, "step2-manifest.json"), targetManifest);
            if (Directory.Exists(_store.CandidateRoot)) Directory.Delete(_store.CandidateRoot, true);
            Directory.Move(Path.Combine(working, "candidate"), _store.CandidateRoot);
            File.Move(Path.Combine(working, "step2-manifest.json"), _store.Step2ManifestFile, true);
            pending.Checkpoint = RegionPreparationCheckpoint.Step2Ready;
            pending.CandidateCount = candidatePaths.Count;
            pending.CandidateBackupSavedCount = savedCount;
            pending.CandidateBackups = candidateBackups.Values
                .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var warning in pending.CandidateBackups.Where(item =>
                         item.Status == CandidateBackupStatus.Unavailable))
                RegionSwitchLog.Write("VerifiedDifferenceStep2FileIssue",
                    current: ToCurrent(pending.TargetRegion),
                    detail: $"Path={warning.RelativePath}; FullPath=" +
                            $"{OverwatchRegionBackupStore.SafeCombine(gameRoot, warning.RelativePath)}; " +
                            $"Reason={warning.Reason}");
            _store.SaveVerifiedPending(pending); // 最后提交状态；此前异常仍按 Step1Ready 恢复。
            try { Directory.Delete(working, true); } catch { }
            var warningCount = candidateBackups.Values.Count(item => item.Status == CandidateBackupStatus.Unavailable);
            progress?.Report(new RegionProgress(
                $"发现候选差异 {candidatePaths.Count:N0} 个，成功保存 {savedCount:N0} 个，因文件异常跳过 {warningCount:N0} 个。",
                candidatePaths.Count, candidatePaths.Count));
            RegionSwitchLog.Write("VerifiedDifferenceStep2Ready", current: ToCurrent(pending.TargetRegion),
                detail: $"Candidates={candidatePaths.Count:N0}; Saved={savedCount:N0}; " +
                        $"FileIssues={warningCount:N0}; Target={pending.TargetRegion}");
            return RegionBackupState.Preparing;
        }
        catch
        {
            try { if (Directory.Exists(working)) Directory.Delete(working, true); } catch { }
            // state.json 仍为 Step1Ready；残留候选数据会在重做步骤 2 时清理。
            throw;
        }
    }

    private async Task<RegionBackupState> VerifyAndCommitDifferenceAsync(string gameRoot,
        PendingRegionPreparation pending, IProgress<RegionProgress>? progress, CancellationToken cancellationToken)
    {
        var sourceManifest = _store.LoadPreparationManifest(1) ??
                             throw new InvalidDataException("第一步文件状态记录不完整，请返回第一步重新准备。");
        var targetManifest = _store.LoadPreparationManifest(2) ??
                             throw new InvalidDataException("第二步文件状态记录不完整，请重新执行第二步。");
        var chinaFull = pending.SourceRegion == OverwatchRegion.China ? sourceManifest : targetManifest;
        var internationalFull = pending.SourceRegion == OverwatchRegion.International ? sourceManifest : targetManifest;
        var candidates = Compare(chinaFull, internationalFull)
            .Where(item => item.Kind != RegionDifferenceKind.Same).ToList();
        var logicalVerified = new List<RegionDifference>();
        var results = new List<RegionCandidateResult>();
        var unavailable = pending.CandidateBackups
            .Where(item => item.Status == CandidateBackupStatus.Unavailable)
            .ToDictionary(item => item.RelativePath, item => item.Reason, StringComparer.OrdinalIgnoreCase);

        progress?.Report(new RegionProgress("正在验证区服差异文件……"));
        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[i];
            progress?.Report(new RegionProgress(
                $"正在验证区服差异文件… {i + 1:N0} / {candidates.Count:N0}", i + 1, candidates.Count));
            var sourceExpected = pending.SourceRegion == OverwatchRegion.China
                ? candidate.China : candidate.International;
            var targetExpected = pending.TargetRegion == OverwatchRegion.China
                ? candidate.China : candidate.International;
            var livePath = OverwatchRegionBackupStore.SafeCombine(gameRoot, candidate.RelativePath);
            var targetCandidatePath = _store.CandidateFile(pending.TargetRegion, candidate.RelativePath);
            var sourceReturned = InspectFile(livePath, sourceExpected, cancellationToken);
            if (sourceReturned.Status == FileInspectionStatus.Issue)
            {
                AddCandidateResult(results, candidate.RelativePath, CandidateVerificationOutcome.FileIssueSkipped,
                    "A2 当前文件无法读取：" + sourceReturned.Reason);
                LogCandidateIssue("VerifiedDifferenceCandidateSkipped", pending.SourceRegion,
                    candidate.RelativePath, sourceReturned.Reason);
            }
            else if (sourceReturned.Status == FileInspectionStatus.Mismatch)
            {
                AddCandidateResult(results, candidate.RelativePath, CandidateVerificationOutcome.VerificationRejected,
                    "A2 与 A1 不一致");
                RegionSwitchLog.Write("VerifiedDifferenceCandidateRejected", current: ToCurrent(pending.SourceRegion),
                    detail: $"Path={candidate.RelativePath}; " +
                            $"A1={sourceExpected?.Sha256 ?? "missing"}; " +
                            $"B1={targetExpected?.Sha256 ?? "missing"}; A2=" +
                            (File.Exists(livePath) ? SafeHash(livePath, cancellationToken) : "missing"));
            }
            else if (unavailable.TryGetValue(candidate.RelativePath, out var unavailableReason))
            {
                AddCandidateResult(results, candidate.RelativePath, CandidateVerificationOutcome.FileIssueSkipped,
                    string.IsNullOrWhiteSpace(unavailableReason) ? "另一端区服文件备份不可用" : unavailableReason);
            }
            else
            {
                var targetSaved = targetExpected is null
                    ? new FileInspectionResult(FileInspectionStatus.Match, "")
                    : InspectFile(targetCandidatePath, targetExpected, cancellationToken);
                if (targetSaved.Status != FileInspectionStatus.Match)
                {
                    AddCandidateResult(results, candidate.RelativePath, CandidateVerificationOutcome.FileIssueSkipped,
                        "另一端区服文件备份不可用：" + targetSaved.Reason);
                    LogCandidateIssue("VerifiedDifferenceCandidateSkipped", pending.SourceRegion,
                        candidate.RelativePath, targetSaved.Reason);
                }
                else
                {
                    logicalVerified.Add(candidate);
                }
            }
            await Task.Yield();
        }

        foreach (var backup in pending.CandidateBackups.Where(item =>
                     item.Status == CandidateBackupStatus.Unavailable &&
                     candidates.All(candidate => !string.Equals(candidate.RelativePath, item.RelativePath,
                         StringComparison.OrdinalIgnoreCase))))
            AddCandidateResult(results, backup.RelativePath, CandidateVerificationOutcome.FileIssueSkipped,
                string.IsNullOrWhiteSpace(backup.Reason) ? "候选文件元数据不可用" : backup.Reason);

        var generationRoot = _store.GenerationRoot(pending.GenerationId);
        try
        {
            if (Directory.Exists(generationRoot)) Directory.Delete(generationRoot, true);
            Directory.CreateDirectory(generationRoot);
            var usable = new List<RegionDifference>();
            for (var i = 0; i < logicalVerified.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var difference = logicalVerified[i];
                progress?.Report(new RegionProgress(
                    $"正在保存已验证区服差异… {i + 1:N0} / {logicalVerified.Count:N0}",
                    i + 1, logicalVerified.Count));
                try
                {
                    if (difference.China is { } chinaEntry)
                    {
                        var source = pending.SourceRegion == OverwatchRegion.China
                            ? OverwatchRegionBackupStore.SafeCombine(gameRoot, difference.RelativePath)
                            : _store.CandidateFile(OverwatchRegion.China, difference.RelativePath);
                        await RestoreAtomicallyAsync(source,
                            _store.BackupFile(pending.GenerationId, OverwatchRegion.China, difference.RelativePath),
                            chinaEntry, cancellationToken);
                    }
                    if (difference.International is { } internationalEntry)
                    {
                        var source = pending.SourceRegion == OverwatchRegion.International
                            ? OverwatchRegionBackupStore.SafeCombine(gameRoot, difference.RelativePath)
                            : _store.CandidateFile(OverwatchRegion.International, difference.RelativePath);
                        await RestoreAtomicallyAsync(source,
                            _store.BackupFile(pending.GenerationId, OverwatchRegion.International,
                                difference.RelativePath), internationalEntry, cancellationToken);
                    }
                    usable.Add(difference);
                    AddCandidateResult(results, difference.RelativePath,
                        CandidateVerificationOutcome.VerifiedUsable, "往返验证与两侧备份校验通过");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (IsPerFileException(ex))
                {
                    TryDeleteGenerationEntryBackups(pending.GenerationId, difference.RelativePath);
                    AddCandidateResult(results, difference.RelativePath,
                        CandidateVerificationOutcome.FileIssueSkipped, "最终 Generation 保存失败：" + ex.Message);
                    LogCandidateIssue("VerifiedDifferenceGenerationEntrySkipped", pending.SourceRegion,
                        difference.RelativePath, ex.ToString());
                }
            }

            var candidateCount = Math.Max(pending.CandidateCount,
                candidates.Select(item => item.RelativePath)
                    .Union(pending.CandidateBackups.Select(item => item.RelativePath),
                        StringComparer.OrdinalIgnoreCase).Count());
            if (candidateCount > 0 && usable.Count == 0)
            {
                try { Directory.Delete(generationRoot, true); } catch { }
                RegionSwitchLog.Write("VerifiedDifferenceNoUsableEntries",
                    current: ToCurrent(pending.SourceRegion), generationId: pending.GenerationId,
                    detail: $"Candidates={candidateCount}; Rejected=" +
                            $"{results.Count(item => item.Outcome == CandidateVerificationOutcome.VerificationRejected)}; " +
                            $"FileIssues={results.Count(item => item.Outcome == CandidateVerificationOutcome.FileIssueSkipped)}; " +
                            "ActiveGeneration=unchanged");
                throw new InvalidOperationException("未生成可用的区服差异文件。现有 Active Generation 未被替换；可以重新执行步骤 3、步骤 2，返回步骤 1，或改用完整备份。");
            }

            var generation = new OverwatchRegionGeneration
            {
                GenerationId = pending.GenerationId,
                BackupMode = RegionBackupMode.VerifiedDifference,
                SourceRegion = pending.SourceRegion,
                TargetRegion = pending.TargetRegion,
                ChinaBuildFingerprint = chinaFull.BuildFingerprint,
                InternationalBuildFingerprint = internationalFull.BuildFingerprint,
                CommonBaselineFingerprint = "",
                Differences = usable,
                VerificationSummary = new RegionVerificationSummary
                {
                    CandidateCount = candidateCount,
                    VerifiedCount = usable.Count,
                    RejectedCount = results.Count(item =>
                        item.Outcome == CandidateVerificationOutcome.VerificationRejected),
                    SkippedFileCount = results.Count(item =>
                        item.Outcome == CandidateVerificationOutcome.FileIssueSkipped),
                    HasWarnings = results.Any(item =>
                        item.Outcome == CandidateVerificationOutcome.FileIssueSkipped),
                    Results = results.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
                },
            };
            var china = CreateVerifiedManifest(OverwatchRegion.China, chinaFull, usable);
            var international = CreateVerifiedManifest(OverwatchRegion.International, internationalFull, usable);
            generation.ChinaManifestId = china.ManifestId;
            generation.InternationalManifestId = international.ManifestId;

            generation.ChinaBackupComplete = true;
            generation.InternationalBackupComplete = true;
            generation.State = RegionBackupState.Ready;
            _store.SaveGenerationManifest(generation.GenerationId, china);
            _store.SaveGenerationManifest(generation.GenerationId, international);
            _store.SaveGeneration(generation);
            var validation = await ValidateGenerationBackupsAsync(generation, null, true, cancellationToken);
            if (!validation.Available && validation.Issues is { Count: > 0 })
            {
                foreach (var issue in validation.Issues
                             .GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                             .Select(group => group.First()))
                {
                    usable.RemoveAll(item => string.Equals(item.RelativePath, issue.RelativePath,
                        StringComparison.OrdinalIgnoreCase));
                    TryDeleteGenerationEntryBackups(generation.GenerationId, issue.RelativePath);
                    AddCandidateResult(results, issue.RelativePath,
                        CandidateVerificationOutcome.FileIssueSkipped,
                        "最终 Generation 提交前复核失败：" + issue.Reason);
                }
                if (candidateCount > 0 && usable.Count == 0)
                    throw new InvalidOperationException("未生成可用的区服差异文件。现有 Active Generation 未被替换；可以重新执行步骤 3、步骤 2，返回步骤 1，或改用完整备份。");

                generation.Differences = usable;
                generation.VerificationSummary.VerifiedCount = usable.Count;
                generation.VerificationSummary.SkippedFileCount = results.Count(item =>
                    item.Outcome == CandidateVerificationOutcome.FileIssueSkipped);
                generation.VerificationSummary.HasWarnings = generation.VerificationSummary.SkippedFileCount > 0;
                generation.VerificationSummary.Results = results
                    .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
                china = CreateVerifiedManifest(OverwatchRegion.China, chinaFull, usable);
                international = CreateVerifiedManifest(OverwatchRegion.International, internationalFull, usable);
                generation.ChinaManifestId = china.ManifestId;
                generation.InternationalManifestId = international.ManifestId;
                _store.SaveGenerationManifest(generation.GenerationId, china);
                _store.SaveGenerationManifest(generation.GenerationId, international);
                _store.SaveGeneration(generation);
                validation = await ValidateGenerationBackupsAsync(generation, null, true, cancellationToken);
            }
            if (!validation.Available) throw new InvalidDataException(validation.Reason);
            WriteDiagnostic(generation, china, international);
            _store.Activate(generation.GenerationId, pending.SourceRegion);
            _store.DeletePreparation();
            progress?.Report(new RegionProgress(
                $"已确认 {generation.VerificationSummary.VerifiedCount:N0} 个区服差异文件，" +
                $"自动忽略 {generation.VerificationSummary.RejectedCount:N0} 个非稳定变化，" +
                $"因文件异常跳过 {generation.VerificationSummary.SkippedFileCount:N0} 个。",
                generation.VerificationSummary.VerifiedCount, candidateCount));
            RegionSwitchLog.Write("VerifiedDifferenceReady", current: ToCurrent(pending.SourceRegion),
                generationId: generation.GenerationId,
                detail: $"智能差异备份准备完成；Verified={generation.VerificationSummary.VerifiedCount:N0}; " +
                        $"Rejected={generation.VerificationSummary.RejectedCount:N0}; " +
                        $"FileIssues={generation.VerificationSummary.SkippedFileCount:N0}");
            return RegionBackupState.Ready;
        }
        catch
        {
            if (_store.LoadPointer()?.GenerationId != pending.GenerationId)
            {
                try { if (Directory.Exists(generationRoot)) Directory.Delete(generationRoot, true); } catch { }
            }
            // Preparation 仍是 Step2Ready，下一次可直接重新执行步骤 3。
            throw;
        }
    }

    public async Task<RegionBackupState> RedoVerifiedStep2Async(string gameRoot,
        IProgress<RegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureGameReady(gameRoot);
        var pending = _store.LoadPending() ?? throw new InvalidOperationException("当前没有智能差异备份准备任务。");
        if (pending.BackupMode != RegionBackupMode.VerifiedDifference)
            throw new InvalidOperationException("当前准备任务不是智能差异备份模式。");
        _store.ResetVerifiedToStep1(pending);
        return await CaptureVerifiedOtherRegionAsync(gameRoot, pending, progress, cancellationToken);
    }

    public async Task<RegionBackupState> RedoVerifiedStep1Async(string gameRoot,
        IProgress<RegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureGameReady(gameRoot);
        var pending = _store.LoadPending() ?? throw new InvalidOperationException("当前没有智能差异备份准备任务。");
        if (pending.BackupMode != RegionBackupMode.VerifiedDifference)
            throw new InvalidOperationException("当前准备任务不是智能差异备份模式。");
        var source = pending.SourceRegion;
        progress?.Report(new RegionProgress("正在重新记录当前区服文件状态……"));
        await _scanner.WaitForQuiescenceAsync(gameRoot, progress, cancellationToken, _quiescenceMilliseconds);
        var scan = await _scanner.ScanBestEffortAsync(gameRoot, source, progress, cancellationToken);
        var manifest = scan.Manifest;
        KeepContentIdentityOnly(manifest);
        var replacement = new PendingRegionPreparation
        {
            GenerationId = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8],
            BackupMode = RegionBackupMode.VerifiedDifference,
            Checkpoint = RegionPreparationCheckpoint.Step1Ready,
            SourceRegion = source,
            TargetRegion = Other(source),
            Step1Warnings = scan.Issues.ToList(),
        };
        var working = Path.Combine(_store.PreparationRoot,
            "step1-working-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(working);
            OverwatchRegionBackupStore.WriteJson(Path.Combine(working, "step1-manifest.json"), manifest);
            OverwatchRegionBackupStore.WriteJson(Path.Combine(working, "state.json"), replacement);
            _store.CommitPreparationDirectory(working);
            return RegionBackupState.Preparing;
        }
        catch
        {
            try { if (Directory.Exists(working)) Directory.Delete(working, true); } catch { }
            throw;
        }
    }

    private async Task<SwitchEligibilityResult> EvaluateSwitchEligibilityAsync(
        OverwatchRegionGeneration generation, CompatibilityResult compatibility, OverwatchRegion? target,
        bool hashBackups, CancellationToken token)
    {
        if (compatibility.Status == GenerationCompatibility.Updated ||
            generation.State == RegionBackupState.Stale && compatibility.Status != GenerationCompatibility.Compatible)
            return new SwitchEligibilityResult(RegionSwitchEligibility.GameUpdated,
                compatibility.Status == GenerationCompatibility.Updated
                    ? compatibility.Reason
                    : "Active Generation 已被标记为旧游戏版本，当前无法重新确认兼容性");
        var backup = await ValidateGenerationBackupsAsync(generation, target, hashBackups, token,
            allowPerFileIssues: generation.BackupMode == RegionBackupMode.VerifiedDifference);
        if (!backup.Available)
            return new SwitchEligibilityResult(RegionSwitchEligibility.BackupUnavailable, backup.Reason);
        return compatibility.Status == GenerationCompatibility.Compatible
            ? new SwitchEligibilityResult(RegionSwitchEligibility.Normal,
                backup.FileIssueCount > 0 ? backup.Reason : "游戏版本与区服备份均已确认",
                backup.FileIssueCount)
            : new SwitchEligibilityResult(RegionSwitchEligibility.BestEffort,
                "当前游戏版本无法确认；将只处理 Active Generation 中可用的已知 Difference" +
                (backup.FileIssueCount > 0 ? $"；{backup.FileIssueCount:N0} 个目标备份文件将跳过" : ""),
                backup.FileIssueCount);
    }

    private async Task<BackupValidationResult> ValidateGenerationBackupsAsync(
        OverwatchRegionGeneration generation, OverwatchRegion? target, bool hashBackups, CancellationToken token,
        bool allowPerFileIssues = false)
    {
        try
        {
            if (generation.State is not (RegionBackupState.Ready or RegionBackupState.Stale))
                return new BackupValidationResult(false, "Active Generation 尚未准备完成");
            if (!generation.ChinaBackupComplete || !generation.InternationalBackupComplete)
                return new BackupValidationResult(false, "pair.json 未标记国服和国际服备份完整");

            var china = _store.LoadGenerationManifest(generation.GenerationId, OverwatchRegion.China);
            var international = _store.LoadGenerationManifest(generation.GenerationId, OverwatchRegion.International);
            if (china is null || international is null)
                return new BackupValidationResult(false, "Generation Manifest 缺失或无法读取");
            if (!string.Equals(generation.ChinaManifestId, china.ManifestId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(generation.InternationalManifestId, international.ManifestId,
                    StringComparison.OrdinalIgnoreCase))
                return new BackupValidationResult(false, "pair.json 与 Generation Manifest 不匹配");

            var manifestDifferences = Compare(china, international)
                .Where(item => item.Kind != RegionDifferenceKind.Same)
                .ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
            if (manifestDifferences.Count != generation.Differences.Count)
                return new BackupValidationResult(false, "pair.json 的已知 Difference 数量与 Manifest 不一致");

            foreach (var difference in generation.Differences)
            {
                token.ThrowIfCancellationRequested();
                if (difference.Kind == RegionDifferenceKind.Same ||
                    !manifestDifferences.TryGetValue(difference.RelativePath, out var manifestDifference) ||
                    manifestDifference.Kind != difference.Kind ||
                    !EntriesEqual(manifestDifference.China, difference.China) ||
                    !EntriesEqual(manifestDifference.International, difference.International))
                    return new BackupValidationResult(false,
                        "pair.json 包含无效或与 Manifest 不一致的 Difference：" + difference.RelativePath);
            }

            var targets = target is null
                ? new[] { OverwatchRegion.China, OverwatchRegion.International }
                : new[] { target.Value };
            var fileIssues = new List<RegionFileIssue>();
            foreach (var backupRegion in targets)
            {
                foreach (var difference in generation.Differences)
                {
                    token.ThrowIfCancellationRequested();
                    var expected = backupRegion == OverwatchRegion.China
                        ? difference.China : difference.International;
                    if (expected is null) continue;
                    var source = _store.BackupFile(generation.GenerationId, backupRegion,
                        difference.RelativePath);
                    try
                    {
                        if (!FileMatches(source, expected, hashBackups, token))
                            throw new InvalidDataException(
                                $"{RegionName(backupRegion)}目标备份缺失或校验失败");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) when (IsPerFileException(ex))
                    {
                        var issue = new RegionFileIssue
                        {
                            RelativePath = difference.RelativePath,
                            Reason = ex.Message,
                        };
                        fileIssues.Add(issue);
                        RegionSwitchLog.Write("GenerationBackupEntryUnavailable",
                            generationId: generation.GenerationId,
                            detail: $"Region={backupRegion}; Path={difference.RelativePath}; Reason={ex}");
                        if (!allowPerFileIssues)
                            return new BackupValidationResult(false,
                                $"{RegionName(backupRegion)}目标备份缺失或校验失败：{difference.RelativePath}",
                                fileIssues.Count, fileIssues);
                    }
                    await Task.Yield();
                }
            }
            return new BackupValidationResult(true,
                fileIssues.Count > 0
                    ? $"Generation 结构有效；{fileIssues.Count:N0} 个目标备份文件异常，将在切换时逐文件跳过"
                    : hashBackups
                        ? "目标区服备份已通过大小与 Hash 校验"
                        : "区服备份结构、文件存在性与大小正常",
                fileIssues.Count, fileIssues);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new BackupValidationResult(false, "读取 Generation 或目标备份失败：" + ex.Message);
        }
    }

    private static bool ExecutableMatches(GameBuildFingerprint actual, GameBuildFingerprint expected) =>
        actual.ExecutableSize == expected.ExecutableSize &&
        (string.IsNullOrEmpty(expected.ExecutableFileVersion) ||
         string.Equals(actual.ExecutableFileVersion, expected.ExecutableFileVersion, StringComparison.Ordinal)) &&
        (string.IsNullOrEmpty(expected.ExecutableProductVersion) ||
         string.Equals(actual.ExecutableProductVersion, expected.ExecutableProductVersion, StringComparison.Ordinal));

    private sealed record CompatibilityResult(GenerationCompatibility Status, string Reason);
    private sealed record SwitchEligibilityResult(RegionSwitchEligibility Status, string Reason,
        int FileIssueCount = 0);
    private sealed record BackupValidationResult(bool Available, string Reason, int FileIssueCount = 0,
        IReadOnlyList<RegionFileIssue>? Issues = null);
    private enum FileInspectionStatus { Match, Mismatch, Issue }
    private sealed record FileInspectionResult(FileInspectionStatus Status, string Reason);

    private sealed record DetectionResult(CurrentGameRegion DetectedRegion, RegionEvidenceResult Evidence,
        bool ExactSnapshotMatch)
    {
        public static DetectionResult Unknown { get; } = new(CurrentGameRegion.Unknown,
            RegionEvidenceResult.NoStrongConflict, false);

        public static DetectionResult FromLastSuccessful(ActiveGenerationPointer pointer, string generationId) =>
            pointer.LastSuccessfulRegion is not null &&
            string.Equals(pointer.LastSuccessfulGenerationId, generationId, StringComparison.OrdinalIgnoreCase)
                ? new DetectionResult(ToCurrent(pointer.LastSuccessfulRegion.Value),
                    RegionEvidenceResult.NoStrongConflict, false)
                : Unknown;
    }

    private static bool FilesEqual(RegionFileEntry left, RegionFileEntry right) =>
        left.Size == right.Size && !string.IsNullOrEmpty(left.Sha256) &&
        string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase);

    private static bool EntriesEqual(RegionFileEntry? left, RegionFileEntry? right) =>
        left is null && right is null || left is not null && right is not null &&
        string.Equals(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase) &&
        FilesEqual(left, right);

    private static void KeepContentIdentityOnly(OverwatchRegionManifest manifest)
    {
        foreach (var entry in manifest.Files.Values) entry.LastWriteTimeUtc = default;
        manifest.BuildFingerprint = new GameBuildFingerprint();
    }

    private static string ComputeCommonBaseline(IEnumerable<RegionDifference> differences)
    {
        var text = string.Join("\n", differences.Where(item => item.Kind == RegionDifferenceKind.Same)
            .Select(item => $"{item.RelativePath}|{item.China?.Size}|{item.China?.Sha256}")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static void WriteDiagnostic(OverwatchRegionGeneration generation, OverwatchRegionManifest china,
        OverwatchRegionManifest international)
    {
        try
        {
            var file = Path.Combine(AppPaths.Current.LogsDir, "region-diff-diagnostic.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var all = Compare(china, international);
            var builder = new StringBuilder();
            builder.AppendLine($"Generation ID: {generation.GenerationId}");
            builder.AppendLine($"China files: {china.Files.Count:N0}");
            builder.AppendLine($"International files: {international.Files.Count:N0}");
            foreach (var kind in Enum.GetValues<RegionDifferenceKind>())
            {
                var group = all.Where(item => item.Kind == kind).ToList();
                builder.AppendLine();
                builder.AppendLine($"{kind}: count={group.Count:N0}; china={FormatBytes(group.Sum(item => item.China?.Size ?? 0))}; international={FormatBytes(group.Sum(item => item.International?.Size ?? 0))}");
            }
            builder.AppendLine();
            builder.AppendLine("Top 50 differences:");
            foreach (var item in all.Where(value => value.Kind != RegionDifferenceKind.Same)
                         .OrderByDescending(value => Math.Max(value.China?.Size ?? 0, value.International?.Size ?? 0)).Take(50))
                builder.AppendLine($"{item.Kind}\t{item.RelativePath}\tChina={item.China?.Size ?? 0}\tInternational={item.International?.Size ?? 0}\tChinaHash={item.China?.Sha256 ?? "-"}\tInternationalHash={item.International?.Sha256 ?? "-"}");
            File.WriteAllText(file, builder.ToString());
        }
        catch { }
    }

    private static void EnsureDiskSpace(string root, long needed)
    {
        Directory.CreateDirectory(root);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
        if (drive.AvailableFreeSpace < needed + 256L * 1024 * 1024)
            throw new IOException($"准备区服文件需要约 {FormatBytes(needed)} 临时空间，当前磁盘空间不足。");
    }

    private void EnsureGameReady(string root)
    {
        if (!IsValidGameRoot(root)) throw new DirectoryNotFoundException("守望先锋游戏目录无效，请选择包含 Overwatch.exe 的安装根目录。");
        if (_isGameRunning())
            throw new InvalidOperationException("检测到《守望先锋》正在运行，请关闭游戏后继续。备份期间不要启动游戏。");
    }

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path)) return;
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static string FormatBytes(long bytes) => bytes < 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024d:0.0} MB" : $"{bytes / 1024d / 1024d / 1024d:0.0} GB";
    private static OverwatchRegion Other(OverwatchRegion region) => region == OverwatchRegion.China
        ? OverwatchRegion.International : OverwatchRegion.China;
    private static string RegionName(OverwatchRegion region) => region == OverwatchRegion.China ? "国服" : "国际服";
    private static CurrentGameRegion ToCurrent(OverwatchRegion region) => region == OverwatchRegion.China
        ? CurrentGameRegion.China : CurrentGameRegion.International;
}
