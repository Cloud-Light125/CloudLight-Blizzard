using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BnetSwitch.Services.OverwatchRegion;

public sealed class OverwatchRegionManager
{
    private readonly OverwatchRegionScanner _scanner = new();
    private readonly OverwatchRegionBackupStore _store;
    private readonly Func<bool> _isGameRunning;
    private readonly int _quiescenceMilliseconds;

    public string BackupRoot => _store.Root;

    public OverwatchRegionManager(string? storageRoot = null, Func<bool>? gameRunning = null,
        int quiescenceMilliseconds = 6000)
    {
        _store = new OverwatchRegionBackupStore(storageRoot);
        _isGameRunning = gameRunning ?? IsGameRunning;
        _quiescenceMilliseconds = quiescenceMilliseconds;
    }

    public static bool IsValidGameRoot(string? root) => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root) &&
                                                        OverwatchRegionScanner.FindExecutable(root) is not null;
    public static bool IsGameRunning() => Process.GetProcessesByName("Overwatch").Any(process =>
    {
        process.Dispose();
        return true;
    });

    public async Task<RegionBackupState> CaptureAsync(string gameRoot, OverwatchRegion region,
        IProgress<RegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var pending = _store.LoadPending();
        if (pending is null) return await StartPreparationAsync(gameRoot, region, progress, cancellationToken);
        if (pending.TargetRegion != region)
            throw new InvalidOperationException($"请先在 Battle.net 中切换到{RegionName(pending.TargetRegion)}，等待更新完成后再继续。");
        return await ContinuePreparationAsync(gameRoot, progress, cancellationToken);
    }

    public async Task<RegionBackupState> StartPreparationAsync(string gameRoot, OverwatchRegion sourceRegion,
        IProgress<RegionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
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
            _store.Activate(generation.GenerationId);
            _store.DeleteStaging(generation.GenerationId);
            return RegionBackupState.Ready;
        }
        catch (OperationCanceledException)
        {
            try { if (Directory.Exists(generationRoot)) Directory.Delete(generationRoot, true); } catch { }
            try { _store.DeleteStaging(generation.GenerationId); } catch { }
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
        var current = await DetectCurrentRegionAsync(gameRoot, generation, cancellationToken);
        var compatibility = await EvaluateCompatibilityAsync(gameRoot, generation, cancellationToken);
        RegionSwitchLog.Write("NormalizeBegin", target, current, compatibility.Status, generation.GenerationId,
            compatibility.Reason);
        if (compatibility.Status != GenerationCompatibility.Compatible)
        {
            RegionSwitchLog.Write("NormalizeRejected", target, current, compatibility.Status,
                generation.GenerationId, compatibility.Reason);
            throw new InvalidDataException(compatibility.Status == GenerationCompatibility.Updated
                ? "检测到《守望先锋》已经更新。当前本地区服文件基于旧版本，为了避免覆盖新版游戏，需要重新准备一次区服文件。原因：" + compatibility.Reason
                : "暂时无法确认当前游戏版本是否仍属于已准备的区服文件。原因：" + compatibility.Reason);
        }
        if (current == ToCurrent(target))
        {
            RegionSwitchLog.Write("NormalizeAlreadyTarget", target, current, compatibility.Status,
                generation.GenerationId, "Verification result=already matched");
            return new RegionSwitchResult(0, 0, Verified: true);
        }

        foreach (var difference in generation.Differences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expected = target == OverwatchRegion.China ? difference.China : difference.International;
            if (expected is null) continue;
            var source = _store.BackupFile(generation.GenerationId, target, difference.RelativePath);
            if (!FileMatches(source, expected, hash: true, cancellationToken))
                throw new InvalidDataException("本地保存的区服文件不完整，已停止切换：" + difference.RelativePath);
        }

        var restored = 0;
        var deleted = 0;
        var chinaOnly = 0;
        var internationalOnly = 0;
        var different = 0;
        for (var i = 0; i < generation.Differences.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var difference = generation.Differences[i];
            if (difference.Kind == RegionDifferenceKind.ChinaOnly) chinaOnly++;
            if (difference.Kind == RegionDifferenceKind.InternationalOnly) internationalOnly++;
            progress?.Report(new RegionProgress($"正在切换到{RegionName(target)}… {i + 1:N0} / {generation.Differences.Count:N0}",
                i + 1, generation.Differences.Count));
            var expected = target == OverwatchRegion.China ? difference.China : difference.International;
            var destination = OverwatchRegionBackupStore.SafeCombine(gameRoot, difference.RelativePath);
            if (expected is null)
            {
                if (File.Exists(destination))
                {
                    ClearReadOnly(destination);
                    File.Delete(destination);
                    deleted++;
                }
                continue;
            }
            var source = _store.BackupFile(generation.GenerationId, target, difference.RelativePath);
            if (!FileMatches(source, expected, hash: true, cancellationToken))
                throw new InvalidDataException("本地保存的区服文件不完整，请检查或重新准备：" + difference.RelativePath);
            await RestoreAtomicallyAsync(source, destination, expected, cancellationToken);
            restored++;
            if (difference.Kind == RegionDifferenceKind.Different) different++;
        }

        foreach (var difference in generation.Differences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expected = target == OverwatchRegion.China ? difference.China : difference.International;
            var destination = OverwatchRegionBackupStore.SafeCombine(gameRoot, difference.RelativePath);
            if (!FileMatches(destination, expected, hash: true, cancellationToken))
                throw new InvalidDataException("区服文件恢复后的完整校验失败：" + difference.RelativePath);
        }
        var verifiedRegion = await DetectCurrentRegionAsync(gameRoot, generation, cancellationToken);
        if (verifiedRegion != ToCurrent(target))
            throw new InvalidDataException("区服文件恢复后未能完整匹配目标 Manifest，已停止后续操作。");

        var result = new RegionSwitchResult(restored, deleted, chinaOnly, internationalOnly, different, true);
        RegionSwitchLog.Write("NormalizeCompleted", target, verifiedRegion, compatibility.Status,
            generation.GenerationId,
            $"ChinaOnly processed={chinaOnly}; InternationalOnly processed={internationalOnly}; Different restored={different}; restored={restored}; deleted={deleted}; Verification result=passed");
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
        return active is null ? CurrentGameRegion.Unknown :
            await DetectCurrentRegionAsync(gameRoot, active.Value.Generation, cancellationToken);
    }

    public async Task<RegionSnapshotStatus> GetStatusAsync(string? gameRoot,
        CancellationToken cancellationToken = default, bool verifyFiles = true)
    {
        var valid = IsValidGameRoot(gameRoot);
        var pending = _store.LoadPending();
        var active = _store.LoadActive();
        if (pending is not null)
        {
            return new RegionSnapshotStatus
            {
                GamePath = gameRoot ?? "", GamePathValid = valid, State = RegionBackupState.Preparing,
                PendingSourceRegion = pending.SourceRegion, PendingTargetRegion = pending.TargetRegion,
                ChinaCaptured = pending.SourceRegion == OverwatchRegion.China,
                InternationalCaptured = pending.SourceRegion == OverwatchRegion.International,
                ActiveGenerationId = active?.Pointer.GenerationId,
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
        var current = verifyFiles && valid && generation.State is RegionBackupState.Ready or RegionBackupState.Stale
            ? await DetectCurrentRegionAsync(gameRoot!, generation, cancellationToken) : CurrentGameRegion.Unknown;
        var compatibility = verifyFiles && valid
            ? await EvaluateCompatibilityAsync(gameRoot!, generation, cancellationToken)
            : new CompatibilityResult(GenerationCompatibility.Unknown,
                valid ? "尚未执行完整文件校验" : "游戏目录无效");
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
            GamePath = gameRoot ?? "", GamePathValid = valid, State = generation.State,
            CurrentRegion = current, ChinaCaptured = true, InternationalCaptured = true,
            GenerationCompatibility = compatibility.Status,
            CompatibilityReason = compatibility.Reason,
            ChinaBackupComplete = generation.ChinaBackupComplete,
            InternationalBackupComplete = generation.InternationalBackupComplete,
            DifferenceCount = generation.Differences.Count,
            BackupBytes = Directory.Exists(backups)
                ? Directory.EnumerateFiles(backups, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) : 0,
            ActiveGenerationId = generation.GenerationId,
        };
    }

    public void Reset() => _store.Clear();

    public void CancelPreparation()
    {
        var pending = _store.LoadPending();
        if (pending is not null) _store.DeleteStaging(pending.GenerationId);
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
        var temp = destination + ".bnetswitch-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await CopyFileAsync(source, temp, token);
            if (!FileMatches(temp, expected, true, token))
                throw new InvalidDataException("恢复后的文件校验失败：" + expected.RelativePath);
            File.SetLastWriteTimeUtc(temp, expected.LastWriteTimeUtc);
            ClearReadOnly(destination);
            File.Move(temp, destination, true);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    private static async Task<CurrentGameRegion> DetectCurrentRegionAsync(string root,
        OverwatchRegionGeneration generation, CancellationToken token)
    {
        if (generation.State is not (RegionBackupState.Ready or RegionBackupState.Stale)) return CurrentGameRegion.Unknown;
        var chinaMatches = 0;
        var internationalMatches = 0;
        var unmatched = 0;
        foreach (var difference in generation.Differences)
        {
            token.ThrowIfCancellationRequested();
            var path = OverwatchRegionBackupStore.SafeCombine(root, difference.RelativePath);
            var china = FileMatches(path, difference.China, true, token);
            var international = FileMatches(path, difference.International, true, token);
            if (china && !international) chinaMatches++;
            else if (international && !china) internationalMatches++;
            else unmatched++;
            await Task.Yield();
        }
        if (generation.Differences.Count == 0) return CurrentGameRegion.Unknown;
        if (chinaMatches == generation.Differences.Count) return CurrentGameRegion.China;
        if (internationalMatches == generation.Differences.Count) return CurrentGameRegion.International;
        return chinaMatches > 0 || internationalMatches > 0 || unmatched > 0
            ? CurrentGameRegion.Mixed : CurrentGameRegion.Unknown;
    }

    private static bool FileMatches(string path, RegionFileEntry? expected, bool hash, CancellationToken token)
    {
        if (expected is null) return !File.Exists(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expected.Size) return false;
        return !hash || string.Equals(OverwatchRegionScanner.ComputeHash(path, token), expected.Sha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CompatibilityResult> EvaluateCompatibilityAsync(string root,
        OverwatchRegionGeneration generation, CancellationToken token)
    {
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

    private static bool ExecutableMatches(GameBuildFingerprint actual, GameBuildFingerprint expected) =>
        actual.ExecutableSize == expected.ExecutableSize &&
        (string.IsNullOrEmpty(expected.ExecutableFileVersion) ||
         string.Equals(actual.ExecutableFileVersion, expected.ExecutableFileVersion, StringComparison.Ordinal)) &&
        (string.IsNullOrEmpty(expected.ExecutableProductVersion) ||
         string.Equals(actual.ExecutableProductVersion, expected.ExecutableProductVersion, StringComparison.Ordinal));

    private sealed record CompatibilityResult(GenerationCompatibility Status, string Reason);

    private static bool FilesEqual(RegionFileEntry left, RegionFileEntry right) =>
        left.Size == right.Size && !string.IsNullOrEmpty(left.Sha256) &&
        string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase);

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
        if (_isGameRunning()) throw new InvalidOperationException("守望先锋正在运行，请先退出游戏后再继续。");
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
