using System.IO;
using System.Security.Cryptography;
using System.Text;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services.OverwatchRegion;

using GameRegion = CloudLightBlizzard.Services.OverwatchRegion.OverwatchRegion;

namespace CloudLightBlizzard.Services;

/// <summary>A nonblocking, crash-released file lease shared by all account mutation entry points.</summary>
internal sealed class AccountOperationGate
{
    private readonly string _lockPath;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _shutdown = new();
    private TaskCompletionSource? _active;
    internal AccountOperationGate(string lockPath) => _lockPath = lockPath;
    internal CancellationToken ShutdownToken => _shutdown.Token;
    internal IDisposable Enter()
    {
        lock (_sync)
        {
            if (_shutdown.IsCancellationRequested) throw new InvalidOperationException("应用正在退出，不能执行账号操作。");
            if (_active is not null) throw new InvalidOperationException("已有账号操作正在执行，请等待完成。");
            Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);
            FileStream stream;
            try { stream = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) { throw new InvalidOperationException("另一实例正在执行账号操作，请等待完成。", ex); }
            _active = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new Lease(this, stream);
        }
    }
    internal Task BeginShutdownAsync()
    {
        // Cancel outside the monitor: callbacks may complete and release the lease.
        _shutdown.Cancel();
        lock (_sync) return _active?.Task ?? Task.CompletedTask;
    }
    private sealed class Lease(AccountOperationGate owner, FileStream stream) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._sync)
            {
                stream.Dispose();
                var completion = owner._active;
                owner._active = null;
                completion?.TrySetResult();
            }
        }
    }
}

internal sealed record AccountSwitchTarget(long Id, string BattleTag, GameRegion Region);
internal sealed record AccountSwitchExecution(AccountSwitchTarget Target, long? PreviousId,
    bool AlreadyCurrent, bool RegionSwitched, string? Warning);

/// <summary>Injected boundaries keep safety/concurrency tests entirely off the real Battle.net installation.</summary>
internal sealed class AccountOperationBackend
{
    internal required Func<long, AccountSwitchTarget> ResolveTarget { get; init; }
    internal required Func<long, Task> ValidateProfile { get; init; }
    internal required Func<Task<long?>> ReadActive { get; init; }
    internal required Func<bool> GameRunning { get; init; }
    internal required Func<Task<RegionSnapshotStatus>> RegionStatus { get; init; }
    internal required Func<GameRegion, IProgress<RegionProgress>?, Task<RegionSwitchResult>> Normalize { get; init; }
    internal required Func<Task<bool>> Quit { get; init; }
    internal required Func<long, Task> Restore { get; init; }
    internal required Func<Task> ClearPointer { get; init; }
    internal required Func<Task> Launch { get; init; }
}

internal sealed class AccountOperationService(AccountOperationGate gate, AccountOperationBackend backend,
    AccountSwitchLog log)
{
    private Exception? _mutationFailure;
    internal async Task BeginShutdownAsync()
    {
        await gate.BeginShutdownAsync();
        if (_mutationFailure is not null)
            throw new InvalidOperationException("账号文件操作曾失败，无法确认更新安装安全；请重新启动软件并检查恢复诊断。", _mutationFailure);
    }
    private async Task TrackMutationAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _mutationFailure ??= ex; throw; }
    }
    internal IDisposable EnterOperation() => gate.Enter();

    internal async Task RunExclusiveAsync(Func<CancellationToken, Task> action)
    {
        using var lease = gate.Enter(); // Before the first await; reject, never queue stale user intent.
        await action(gate.ShutdownToken);
    }

    internal async Task<AccountSwitchExecution> SwitchAsync(long id, Action<string>? status = null,
        Action? beginVerification = null)
    {
        using var lease = gate.Enter();
        var token = gate.ShutdownToken;
        if (id <= 0) throw new ArgumentException("账号 ID 必须是有效的正整数。");
        var target = await Task.Run(() => backend.ResolveTarget(id));
        await backend.ValidateProfile(id); // Must fail before quitting or touching region/account files.
        token.ThrowIfCancellationRequested();
        var current = await backend.ReadActive();
        token.ThrowIfCancellationRequested();
        if (current == id) return new(target, current, true, false, null);
        EnsureGameClosed();
        var switchRegion = false;
        string? warning = null;
        try
        {
            var region = await backend.RegionStatus();
            switchRegion = region.GamePathValid && region.SwitchEligibility is
                RegionSwitchEligibility.Normal or RegionSwitchEligibility.BestEffort;
            if (!switchRegion) warning = region.SwitchEligibility == RegionSwitchEligibility.GameUpdated
                ? "游戏已更新，现有区服备份基于旧版本；本次仅切换账号。"
                : "游戏目录或区服备份当前不可安全使用；本次仅切换账号。";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { warning = "无法检查本地区服文件：" + ex.Message + "；本次仅切换账号。"; }
        token.ThrowIfCancellationRequested();
        var stage = "关闭 Battle.net";
        log.Write("SwitchStarted", current, id, targetRegion: target.Region.ToString());
        try
        {
            await AccountSwitchPipeline.ExecuteAsync(
                async () =>
                {
                    token.ThrowIfCancellationRequested();
                    EnsureGameClosed();
                    status?.Invoke("正在关闭 Battle.net…");
                    log.Write("BattleNetCloseStarted", current, id);
                    RegionSwitchLog.Write("Battle.net quit begin", target.Region);
                    if (!await backend.Quit()) throw new InvalidOperationException("Battle.net 未能完全退出，已中止操作。");
                    log.Write("BattleNetCloseCompleted", current, id);
                    RegionSwitchLog.Write("Battle.net quit end", target.Region);
                },
                async () =>
                {
                    token.ThrowIfCancellationRequested();
                    EnsureGameClosed();
                    if (!switchRegion)
                    {
                        log.Write("RegionFilesSkipped", current, id, detail: warning);
                        RegionSwitchLog.Write("Region files skipped", target.Region, detail: warning);
                        return;
                    }
                    stage = "区服文件";
                    log.Write("RegionFilesSwitchStarted", current, id, targetRegion: target.Region.ToString());
                    RegionSwitchLog.Write("Region files begin", target.Region);
                    RegionSwitchResult result = null!;
                    await TrackMutationAsync(async () => result = await backend.Normalize(target.Region,
                        new Progress<RegionProgress>(p => status?.Invoke(p.Message))));
                    if (result.Outcome == RegionSwitchOutcome.Failed)
                    {
                        var error = new InvalidDataException($"区服文件切换失败，{result.FailedCount} 个文件无法处理。" + FormatIssues(result));
                        _mutationFailure ??= error;
                        throw error;
                    }
                    if (result.Outcome == RegionSwitchOutcome.PartialSuccess)
                        warning = $"{result.FailedCount} 个区服文件异常，已跳过；其他文件已处理。" + FormatIssues(result);
                    log.Write("RegionFilesSwitchCompleted", current, id, detail:
                        $"outcome={result.Outcome};restored={result.Restored};deleted={result.Deleted};failed={result.FailedCount};verified={result.Verified}");
                    RegionSwitchLog.Write("Region files end", target.Region, detail: result.Outcome.ToString());
                },
                async () =>
                {
                    token.ThrowIfCancellationRequested();
                    EnsureGameClosed();
                    stage = "目标账号恢复";
                    status?.Invoke($"正在准备「{target.BattleTag}」的账号…");
                    log.Write("TargetRestoreStarted", current, id);
                    await TrackMutationAsync(() => backend.Restore(id)); // Never abandon restore/rollback at shutdown.
                    log.Write("TargetRestoreCompleted", current, id);
                },
                async () =>
                {
                    token.ThrowIfCancellationRequested();
                    stage = "Battle.net 启动";
                    // Scope log evidence after old-client shutdown and target restore, not before the switch.
                    beginVerification?.Invoke();
                    await backend.Launch();
                    log.Write("BattleNetStarted", current, id);
                });
            return new(target, current, false, switchRegion, warning);
        }
        catch (Exception ex)
        {
            log.Write(stage == "区服文件" ? "RegionFileError" : stage == "Battle.net 启动" ? "BattleNetStartError" : "SnapshotError",
                current, id, detail: stage + ": " + ex.Message);
            throw;
        }
    }

    internal Task AddAccountAsync() => RunExclusiveAsync(async token =>
    {
        EnsureGameClosed();
        token.ThrowIfCancellationRequested();
        if (!await backend.Quit()) throw new InvalidOperationException("Battle.net 未能完全退出，已中止操作。");
        token.ThrowIfCancellationRequested();
        EnsureGameClosed();
        await TrackMutationAsync(backend.ClearPointer);
        token.ThrowIfCancellationRequested();
        await backend.Launch();
    });

    private void EnsureGameClosed()
    {
        if (backend.GameRunning()) throw new InvalidOperationException("守望先锋正在运行，请先退出游戏后再执行账号操作。");
    }
    private static string FormatIssues(RegionSwitchResult result) => result.Issues is null ? "" :
        string.Join("；", result.Issues.Take(8).Select(i => i.RelativePath + ": " + i.Reason));

    internal static AccountOperationService Create(BattleNetPaths paths, AppSettings settings,
        AppDataStore profiles, BattleNetController controller, AccountReader reader,
        Func<OverwatchRegionManager> regionManager, AccountSwitchLog log)
    {
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(paths.RoamingDir).TrimEnd('\\', '/').ToUpperInvariant())));
        var lockPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CloudLight", "account-operation-locks", identity + ".lock");
        return new(new AccountOperationGate(lockPath), new AccountOperationBackend
        {
            ResolveTarget = id =>
            {
                var meta = profiles.ReadMeta(id);
                if (!profiles.HasProfile(id) || meta is null || meta.AccountId != id)
                    throw new InvalidDataException("目标账号没有有效的本地备份或账号元数据不匹配。");
                var rows = reader.ReadAccounts(out _).Where(a => a.AccountId == id).ToList();
                var environment = rows.FirstOrDefault(a => a.Environment.Contains("battlenet.com.cn", StringComparison.OrdinalIgnoreCase))?.Environment
                    ?? rows.Select(a => a.Environment).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e)) ?? "";
                var preference = settings.AccountPreferences.TryGetValue(id.ToString(), out var savedPreference)
                    ? savedPreference.Region : AccountRegionOverride.Auto;
                var china = preference == AccountRegionOverride.China || preference == AccountRegionOverride.Auto &&
                    (string.IsNullOrWhiteSpace(environment) || environment.Contains("battlenet.com.cn", StringComparison.OrdinalIgnoreCase));
                return new(id, meta.BattleTag, china ? GameRegion.China : GameRegion.International);
            },
            ValidateProfile = id => Task.Run(() => profiles.ValidateProfile(id)),
            ReadActive = () => Task.Run(reader.ReadActiveAccountId),
            GameRunning = OverwatchRegionManager.IsGameRunning,
            RegionStatus = () => regionManager().GetStatusAsync(settings.OverwatchGamePath),
            Normalize = (target, progress) => regionManager().NormalizeToRegionAsync(settings.OverwatchGamePath!, target, progress),
            Quit = () => Task.Run(() => controller.GracefulQuit()),
            Restore = id => Task.Run(() => profiles.Restore(id)),
            ClearPointer = () => Task.Run(profiles.ClearCurrentPointer),
            Launch = () => Task.Run(controller.LaunchClient),
        }, log);
    }
}
