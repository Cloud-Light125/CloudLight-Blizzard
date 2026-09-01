using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.Services.OverwatchRegion;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Services.Diagnostics;

public sealed class DiagnosticService
{
    private const long MaxLogBytes = 50L * 1024 * 1024;
    private static readonly TimeSpan LogAge = TimeSpan.FromDays(3);
    private static readonly object LogGate = new();
    private readonly MainViewModel _vm;

    public DiagnosticService(MainViewModel vm) => _vm = vm ?? throw new ArgumentNullException(nameof(vm));

    public event Action<DiagnosticProgress>? ProgressChanged;

    public async Task<DiagnosticRunReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.Now;
        var checks = new List<DiagnosticCheck>();
        var definitions = BuildChecks();
        WriteLog($"run-start checks={definitions.Count}");
        try
        {
            for (var i = 0; i < definitions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = definitions[i];
                ProgressChanged?.Invoke(new DiagnosticProgress(i, definitions.Count, new DiagnosticCheck
                {
                    Id = definition.Id, Category = definition.Category, Name = definition.Name,
                    Status = DiagnosticSeverity.Info, Summary = "检查中…", Timestamp = DateTimeOffset.Now,
                }));
                var stopwatch = Stopwatch.StartNew();
                DiagnosticCheck check;
                try
                {
                    check = await definition.Run(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    check = NewCheck(definition.Id, definition.Category, definition.Name, DiagnosticSeverity.Error,
                        "检查失败", ex.Message);
                }
                check.DurationMilliseconds = stopwatch.ElapsedMilliseconds;
                checks.Add(check);
                WriteLog($"check id={check.Id} status={check.Status} durationMs={check.DurationMilliseconds}");
                ProgressChanged?.Invoke(new DiagnosticProgress(i + 1, definitions.Count, check, IsCompleted: true));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WriteLog($"run-cancelled completed={checks.Count}/{definitions.Count}");
            return new DiagnosticRunReport
            {
                AppVersion = _vm.UpdateChecks.CurrentVersion,
                StartedAt = started,
                CompletedAt = DateTimeOffset.Now,
                Cancelled = true,
                Checks = checks,
            };
        }

        WriteLog($"run-complete completed={checks.Count}/{definitions.Count}");
        return new DiagnosticRunReport
        {
            AppVersion = _vm.UpdateChecks.CurrentVersion,
            StartedAt = started,
            CompletedAt = DateTimeOffset.Now,
            Checks = checks,
        };
    }

    public async Task<string> ExportBundleAsync(DiagnosticRunReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var fileName = $"CloudLight-Blizzard-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        var destination = Path.Combine(AppPaths.Current.Root, fileName);
        var suffix = 1;
        while (File.Exists(destination))
            destination = Path.Combine(AppPaths.Current.Root,
                $"CloudLight-Blizzard-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}-{suffix++}.zip");

        try
        {
            Directory.CreateDirectory(AppPaths.Current.Root);
            WriteLog($"export-start checks={report.Checks.Count}");
            await using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite,
                             FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteEntryAsync(archive, "diagnostics.json", report.ToJson(), cancellationToken);
                await WriteEntryAsync(archive, "diagnostics.txt", report.ToDisplayText(), cancellationToken);
                await WriteEntryAsync(archive, "environment.txt", BuildEnvironmentText(), cancellationToken);
                await WriteEntryAsync(archive, "snapshot-summary.json", BuildSnapshotSummary(), cancellationToken);
                await WriteEntryAsync(archive, "update-summary.json", BuildUpdateSummary(), cancellationToken);
                await WriteEntryAsync(archive, "drops-summary.json", BuildDropsSummary(), cancellationToken);
                await AddRecentLogsAsync(archive, cancellationToken);
            }
            WriteLog($"export-complete file={Path.GetFileName(destination)}");
            return destination;
        }
        catch
        {
            // Do not leave a misleading, half-written diagnostic package after cancellation
            // or an I/O error.
            TryDelete(destination);
            WriteLog("export-failed");
            throw;
        }
    }

    public string BuildCopyText(DiagnosticRunReport report) => report.ToDisplayText();

    private List<CheckDefinition> BuildChecks() => new()
    {
        Define("app.version", "应用", "当前版本", _ => Task.FromResult(NewCheck("app.version", "应用", "当前版本", DiagnosticSeverity.Healthy,
            $"CloudLight Blizzard {_vm.UpdateChecks.CurrentVersion}", $"正式版本为 {_vm.UpdateChecks.CurrentVersion}。"))),
        Define("app.runtime", "应用", ".NET / 系统运行环境", _ => Task.FromResult(NewCheck("app.runtime", "应用", ".NET / 系统运行环境", DiagnosticSeverity.Healthy,
            $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}",
            $"Runtime={RuntimeInformation.FrameworkDescription}; Process={Environment.Is64BitProcess switch { true => "x64", false => "x86" }}; 启动时间={Process.GetCurrentProcess().StartTime:yyyy-MM-dd HH:mm:ss}"))),
        Define("app.paths", "应用", "程序、配置与日志路径", _ => Task.FromResult(CheckPaths())),
        Define("app.config", "应用", "配置文件可读取", _ => Task.FromResult(CheckConfig())),
        Define("disk.install", "磁盘", "安装盘剩余空间", _ => Task.FromResult(CheckDrive("disk.install", "安装盘", AppContext.BaseDirectory))),
        Define("disk.game", "磁盘", "游戏盘剩余空间", _ => Task.FromResult(CheckDrive("disk.game", "游戏盘", _vm.Settings.OverwatchGamePath))),
        Define("disk.snapshot", "磁盘", "快照盘剩余空间", _ => Task.FromResult(CheckDrive("disk.snapshot", "快照盘", _vm.RegionBackupRoot))),
        Define("disk.temp", "磁盘", "临时目录可写", _ => Task.FromResult(CheckWritableDirectory("disk.temp", "临时目录", Path.GetTempPath()))),
        Define("battlenet.processes", "Battle.net", "Battle.net / Agent 进程", _ => Task.FromResult(CheckBattleNetProcesses())),
        Define("battlenet.files", "Battle.net", "安装目录与数据源", _ => Task.FromResult(CheckBattleNetFiles())),
        Define("battlenet.region", "Battle.net", "当前可识别区服状态", async token => await CheckBattleNetRegionAsync(token)),
        Define("region.state", "区服", "当前区服与切换状态", _ => Task.FromResult(CheckRegionState())),
        Define("region.pending", "区服", "未完成操作", _ => Task.FromResult(CheckPendingRegionOperation())),
        Define("snapshot.verified", "快照", "VerifiedDifference / FullSnapshot", _ => Task.FromResult(CheckSnapshotState())),
        Define("snapshot.integrity", "快照", "最新快照完整性", async token => await CheckSnapshotIntegrityAsync(token)),
        Define("network.services", "网络", "代理、公告与更新网络", async token => await CheckNetworkAsync(token)),
        Define("update.metadata", "更新", "更新元数据与安装包", async token => await CheckUpdateMetadataAsync(token)),
        Define("drops.worker", "Drops", "Drops Worker 生命周期", _ => Task.FromResult(CheckDropsWorker())),
        Define("drops.platforms", "Drops", "SOOP / Twitch / YouTube / 哔哩哔哩状态", _ => Task.FromResult(CheckDropsPlatforms())),
        Define("drops.bilibili", "Drops", "哔哩哔哩 Worker、直连与凭据", _ => Task.FromResult(CheckBilibiliProvider())),
    };

    private CheckDefinition Define(string id, string category, string name,
        Func<CancellationToken, Task<DiagnosticCheck>> run) => new(id, category, name, run);

    private DiagnosticCheck CheckPaths()
    {
        var details = $"程序={DiagnosticSanitizer.SanitizePath(AppContext.BaseDirectory)}; 配置={DiagnosticSanitizer.SanitizePath(AppPaths.Current.SettingsFile)}; 日志={DiagnosticSanitizer.SanitizePath(AppPaths.Current.LogsDir)}; 快照={DiagnosticSanitizer.SanitizePath(_vm.RegionBackupRoot)}";
        return NewCheck("app.paths", "应用", "程序、配置与日志路径", Directory.Exists(AppPaths.Current.LogsDir)
            ? DiagnosticSeverity.Healthy : DiagnosticSeverity.Warning, "路径已解析", details);
    }

    private DiagnosticCheck CheckConfig()
    {
        var configPath = AppSettings.FilePath;
        var exists = File.Exists(configPath);
        var readable = !exists;
        try
        {
            if (exists)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(configPath));
                readable = document.RootElement.ValueKind == JsonValueKind.Object;
            }
        }
        catch { readable = false; }
        var severity = !exists ? DiagnosticSeverity.Warning
            : readable ? DiagnosticSeverity.Healthy : DiagnosticSeverity.Error;
        return NewCheck("app.config", "应用", "配置文件可读取", severity,
            !exists ? "配置文件尚未生成，将使用默认设置" : readable ? "配置文件可读取" : "配置文件读取失败",
            $"存在={(exists ? "是" : "否")}; 读取={(readable ? "正常" : "失败")}; " +
            "本项仅执行只读检查，不创建、修改或删除探测文件；" +
            $"文件={DiagnosticSanitizer.SanitizePath(configPath)}");
    }

    private static DiagnosticCheck CheckDrive(string id, string name, string? path)
    {
        try
        {
            var root = Path.GetPathRoot(string.IsNullOrWhiteSpace(path) ? AppContext.BaseDirectory : path);
            if (string.IsNullOrWhiteSpace(root)) return NewCheck(id, "磁盘", name, DiagnosticSeverity.Info, "尚未设置", "路径未设置");
            var drive = new DriveInfo(root);
            var free = drive.AvailableFreeSpace;
            var severity = free < 2L * 1024 * 1024 * 1024 ? DiagnosticSeverity.Error
                : free < 10L * 1024 * 1024 * 1024 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy;
            return NewCheck(id, "磁盘", name, severity, $"剩余 {UpdateDownloadService.FormatBytes(free)}",
                $"驱动器={root}; 总容量={UpdateDownloadService.FormatBytes(drive.TotalSize)}");
        }
        catch (Exception ex) { return NewCheck(id, "磁盘", name, DiagnosticSeverity.Warning, "无法读取磁盘空间", ex.Message); }
    }

    private static DiagnosticCheck CheckWritableDirectory(string id, string name, string path)
    {
        try
        {
            var exists = Directory.Exists(path);
            return NewCheck(id, "磁盘", name, exists ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                exists ? "目录存在（未执行写入探测）" : "目录不存在（只读诊断未创建）",
                "诊断保持只读，不创建临时目录或文件；路径=" + DiagnosticSanitizer.SanitizePath(path));
        }
        catch (Exception ex) { return NewCheck(id, "磁盘", name, DiagnosticSeverity.Error, "无法读取目录状态", ex.Message); }
    }

    private DiagnosticCheck CheckBattleNetProcesses()
    {
        var battleNet = Process.GetProcessesByName("Battle.net").Length;
        var agent = Process.GetProcessesByName("Agent").Length;
        var severity = battleNet > 0 || agent > 0 ? DiagnosticSeverity.Healthy : DiagnosticSeverity.Info;
        return NewCheck("battlenet.processes", "Battle.net", "Battle.net / Agent 进程", severity,
            $"Battle.net.exe {(battleNet > 0 ? "运行中" : "未运行")} · Agent.exe {(agent > 0 ? "运行中" : "未运行")}",
            "诊断只读取进程状态，不会终止任何进程。");
    }

    private DiagnosticCheck CheckBattleNetFiles()
    {
        var paths = _vm.BattleNetDataPaths;
        var clientDirectory = string.IsNullOrWhiteSpace(paths.ClientExe) ? null : Path.GetDirectoryName(paths.ClientExe);
        var productDb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Battle.net", "Agent", "product.db");
        var gameRoot = _vm.Settings.OverwatchGamePath;
        string? gameExecutable = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(gameRoot))
                gameExecutable = OverwatchRegionScanner.FindExecutable(gameRoot);
        }
        catch { }

        var locations = new (string Label, string? Location)[]
        {
            ("Battle.net.exe", paths.ClientExe),
            ("Battle.net 安装目录", clientDirectory),
            ("Battle.net 数据目录", paths.LocalRoot),
            ("Account", paths.AccountDir),
            ("BrowserCaches", paths.BrowserCachesDir),
            ("CachedData.db", paths.CachedDataDb),
            ("Roaming 数据目录", paths.RoamingDir),
            ("Battle.net.config", paths.RoamingConfig),
            ("product.db", productDb),
            ("守望先锋目录", gameRoot),
            ("Overwatch.exe", gameExecutable),
        };
        var present = locations.Count(item => !string.IsNullOrWhiteSpace(item.Location) &&
            (File.Exists(item.Location) || Directory.Exists(item.Location)));
        var severity = present == locations.Length ? DiagnosticSeverity.Healthy : present > 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info;
        return NewCheck("battlenet.files", "Battle.net", "安装目录与数据源", severity,
            $"已发现 {present} / {locations.Length} 项路径或数据源",
            string.Join("; ", locations.Select(item =>
            {
                var exists = !string.IsNullOrWhiteSpace(item.Location) &&
                             (File.Exists(item.Location) || Directory.Exists(item.Location));
                return $"{item.Label}={DiagnosticSanitizer.SanitizePath(item.Location)}（{(exists ? "存在" : "缺失")}）";
            })));
    }

    private async Task<DiagnosticCheck> CheckBattleNetRegionAsync(CancellationToken token)
    {
        try
        {
            var region = await _vm.GetRegionStatusAsync(verifyFiles: false,
                persistStateChanges: false).ConfigureAwait(false);
            var severity = region.CurrentRegion == CurrentGameRegion.Unknown ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy;
            return NewCheck("battlenet.region", "Battle.net", "当前可识别区服状态", severity,
                $"{MainViewModel.RegionDisplayName(region.CurrentRegion)} · Battle.net {(_vm.BattleNetPathValid ? "可用" : "路径未识别")}",
                $"检测区服={MainViewModel.RegionDisplayName(region.CurrentRegion)}; 状态={region.State}; 原因={region.CompatibilityReason}");
        }
        catch (Exception ex) { return NewCheck("battlenet.region", "Battle.net", "当前可识别区服状态", DiagnosticSeverity.Warning, "暂时无法识别", ex.Message); }
    }

    private DiagnosticCheck CheckRegionState()
    {
        var status = _vm.RegionStatusSnapshot;
        var recentSwitches = ReadRecentRegionSwitchEvents();
        var severity = status.State == RegionBackupState.Error || status.CurrentRegion == CurrentGameRegion.Mixed
            ? DiagnosticSeverity.Error : status.CurrentRegion == CurrentGameRegion.Unknown ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy;
        if (recentSwitches.FailureIsLatest && severity == DiagnosticSeverity.Healthy)
            severity = DiagnosticSeverity.Warning;
        return NewCheck("region.state", "区服", "当前区服与切换状态", severity,
            $"当前 {MainViewModel.RegionDisplayName(status.CurrentRegion)} · {status.State}",
            $"最近成功区服={MainViewModel.RegionDisplayName(status.LastSuccessfulRegion)}; " +
            $"最近成功切换={recentSwitches.Success ?? "无记录"}; 最近失败切换={recentSwitches.Failure ?? "无记录"}; " +
            $"失败是否晚于成功={recentSwitches.FailureIsLatest}; 未完成操作={_vm.IsRegionOperationBusy}");
    }

    private DiagnosticCheck CheckPendingRegionOperation() => NewCheck("region.pending", "区服", "未完成操作",
        _vm.HasPendingRegionPreparation ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy,
        _vm.HasPendingRegionPreparation ? "存在未完成的区服准备" : "没有未完成切换操作",
        $"Preparation={_vm.HasPendingRegionPreparation}; OperationBusy={_vm.IsRegionOperationBusy}");

    private DiagnosticCheck CheckSnapshotState()
    {
        var status = _vm.RegionStatusSnapshot;
        IReadOnlyList<SnapshotDescriptor> snapshots;
        try { snapshots = new SnapshotManagerService(_vm.RegionManager).List(); }
        catch (Exception ex)
        {
            return NewCheck("snapshot.verified", "快照", "VerifiedDifference / FullSnapshot",
                DiagnosticSeverity.Warning, "无法读取快照目录", ex.Message);
        }
        var hasVerified = snapshots.Any(item => item.Mode == RegionBackupMode.VerifiedDifference);
        var hasFull = snapshots.Any(item => item.Mode == RegionBackupMode.FullSnapshot);
        var damaged = snapshots.Count(item => item.State is SnapshotDisplayState.Corrupt or SnapshotDisplayState.Missing);
        var latestCreated = snapshots.OrderByDescending(item => item.CreatedAtUtc).FirstOrDefault();
        var latestVerified = snapshots.Where(item => item.LastVerifiedAtUtc.HasValue)
            .OrderByDescending(item => item.LastVerifiedAtUtc).FirstOrDefault();
        var severity = status.State == RegionBackupState.Error || damaged > 0 ? DiagnosticSeverity.Error
            : status.State is RegionBackupState.Empty or RegionBackupState.Legacy || snapshots.Count == 0
                ? DiagnosticSeverity.Warning
                : status.State == RegionBackupState.Stale || snapshots.Any(item => item.State is SnapshotDisplayState.Unverified or SnapshotDisplayState.Expired)
                    ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy;
        return NewCheck("snapshot.verified", "快照", "VerifiedDifference / FullSnapshot", severity,
            snapshots.Count == 0 ? "尚未生成快照" : $"已发现 {snapshots.Count:N0} 个快照 · 当前 {status.BackupMode}",
            $"VerifiedDifference={(hasVerified ? "存在" : "未发现")}; FullSnapshot={(hasFull ? "存在" : "未发现")}; " +
            $"文件数={status.DifferenceCount:N0}; 大小={UpdateDownloadService.FormatBytes(status.BackupBytes)}; " +
            $"最新创建={FormatDiagnosticTime(latestCreated?.CreatedAtUtc)}; 最新验证={FormatDiagnosticTime(latestVerified?.LastVerifiedAtUtc)}; " +
            $"损坏/缺失快照={damaged:N0}");
    }

    private async Task<DiagnosticCheck> CheckSnapshotIntegrityAsync(CancellationToken token)
    {
        try
        {
            var status = await _vm.GetRegionStatusAsync(verifyFiles: true,
                persistStateChanges: false).ConfigureAwait(false);
            var severity = status.State == RegionBackupState.Ready ? DiagnosticSeverity.Healthy
                : status.State == RegionBackupState.Stale ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error;
            return NewCheck("snapshot.integrity", "快照", "最新快照完整性", severity,
                status.State == RegionBackupState.Ready ? "完整且已验证" : $"状态：{status.State}",
                $"兼容性={status.GenerationCompatibility}; 文件异常={status.BackupFileIssueCount}; 丢失文件={status.SkippedFileCount}");
        }
        catch (Exception ex) { return NewCheck("snapshot.integrity", "快照", "最新快照完整性", DiagnosticSeverity.Warning, "未能验证", ex.Message); }
    }

    private async Task<DiagnosticCheck> CheckNetworkAsync(CancellationToken token)
    {
        var report = await new NetworkDiagnosticService(_vm.Settings, _vm.CloudHttpClients).RunAsync(token).ConfigureAwait(false);
        var reasons = new List<string>();
        var liveSeverity = CloudServicesSeverity.ClassifyNetwork(_vm.Settings, report, reasons);
        var severity = liveSeverity switch
        {
            LiveSelfTestSeverity.Fail => DiagnosticSeverity.Error,
            LiveSelfTestSeverity.Warning => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Healthy,
        };
        var failures = new[] { report.Proxy, report.Announcement, report.Update, report.Soop, report.Twitch, report.Bilibili }
            .Count(item => item is not null && !item.Success);
        return NewCheck("network.services", "网络", "代理、公告与更新网络", severity,
            failures == 0 ? "代理、公告、更新服务正常" : $"{failures} 项网络检查异常",
            string.Join("; ", new[]
            {
                $"代理={report.Proxy.Message} / HTTP {report.Proxy.StatusCode?.ToString() ?? "n/a"}",
                $"公告={report.Announcement.Message} / {report.Announcement.Route}",
                $"更新={report.Update.Message} / {report.Update.Route}",
                $"SOOP={(report.Soop?.Message ?? "未执行")} / {report.Soop?.Route ?? "n/a"}",
                $"Twitch={(report.Twitch?.Message ?? "未执行")} / {report.Twitch?.Route ?? "n/a"}",
                $"哔哩哔哩={(report.Bilibili?.Message ?? "未执行")} / {report.Bilibili?.Route ?? "n/a"}（固定直连）",
            }.Concat(reasons.Count == 0 ? Array.Empty<string>() : new[] { "建议=" + string.Join("；", reasons) })
             .Select(DiagnosticSanitizer.Sanitize)));
    }

    private async Task<DiagnosticCheck> CheckUpdateMetadataAsync(CancellationToken token)
    {
        var result = await _vm.UpdateChecks.CheckReadOnlyAsync(token).ConfigureAwait(false);
        if (result.Status == UpdateCheckResultStatus.Failed)
            return NewCheck("update.metadata", "更新", "更新元数据与安装包", DiagnosticSeverity.Error, "更新接口失败", result?.ErrorMessage ?? "未知错误");
        if (result.Status == UpdateCheckResultStatus.NoRelease)
            return NewCheck("update.metadata", "更新", "更新元数据与安装包", DiagnosticSeverity.Warning, "没有可用 Release", "更新接口未返回正式版本。");
        var installer = !string.IsNullOrWhiteSpace(result?.InstallerDownloadUrl);
        var digest = UpdateService.IsValidSha256Digest(result?.InstallerDigest);
        var severity = installer && digest ? DiagnosticSeverity.Healthy : DiagnosticSeverity.Warning;
        return NewCheck("update.metadata", "更新", "更新元数据与安装包", severity,
            $"HTTP {result?.HttpStatusCode?.ToString() ?? "2xx"} · latestVersion={result?.LatestVersion} · {(installer ? "installer 可用" : "installer 缺失")}",
            $"tag={result?.Tag}; size={result?.InstallerSize:N0}; digest={(digest ? "sha256 有效" : "缺失/格式无效")}; URL={(installer ? "通过白名单" : "不可用")}");
    }

    private DiagnosticCheck CheckDropsWorker()
    {
        var snapshots = _vm.DropsHost.Snapshots;
        var running = snapshots.Count(item => item.Lifecycle is WorkerLifecycle.Running or WorkerLifecycle.Starting);
        var crashed = snapshots.Count(item => item.Lifecycle == WorkerLifecycle.Crashed);
        var severity = crashed > 0 ? DiagnosticSeverity.Error : running > 0 ? DiagnosticSeverity.Healthy : DiagnosticSeverity.Info;
        return NewCheck("drops.worker", "Drops", "Drops Worker 生命周期", severity,
            crashed > 0 ? $"{crashed} 个 Worker 异常" : running > 0 ? $"{running} 个 Worker 运行中" : "Worker 未启动",
            string.Join("; ", snapshots.Select(item => $"{item.Platform}={item.Lifecycle}/{item.Status}")));
    }

    private DiagnosticCheck CheckDropsPlatforms()
    {
        var state = _vm.GetDropsDiagnosticSnapshot();
        var values = new[] { state.SoopStatus, state.TwitchStatus, state.YouTubeStatus, state.BilibiliStatus };
        var abnormal = values.Count(value => value.Contains("失败", StringComparison.OrdinalIgnoreCase) || value.Contains("异常", StringComparison.OrdinalIgnoreCase));
        return NewCheck("drops.platforms", "Drops", "SOOP / Twitch / YouTube / 哔哩哔哩状态",
            abnormal > 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy,
            abnormal == 0 ? "平台状态可用" : $"{abnormal} 个平台需要注意",
            $"SOOP={state.SoopStatus}; Twitch={state.TwitchStatus}; YouTube={state.YouTubeStatus}; 哔哩哔哩={state.BilibiliStatus}; 最近错误={state.RecentNetworkError}");
    }

    private DiagnosticCheck CheckBilibiliProvider()
    {
        var settings = _vm.Settings;
        var paths = AppPaths.Current;
        var packaged = Path.Combine(AppContext.BaseDirectory, "_internal", "drops", "bilibili", "bilibili-worker.exe");
        var development = FindDevelopmentBilibiliWorker(AppContext.BaseDirectory);
        var workerExists = File.Exists(packaged) || development is not null;
        var integrity = File.Exists(packaged) && IsPyInstallerExecutable(packaged)
            ? "打包 EXE 已通过 MZ/大小完整性检查"
            : development is not null ? "当前为开发脚本，未执行 EXE 完整性检查" : "未找到 Bilibili Worker";
        var snapshot = _vm.DropsHost.Snapshots.FirstOrDefault(item => item.Platform == DropsPlatform.Bilibili);
        var statePath = Path.Combine(paths.BilibiliDropsDir, "state.json");
        var state = ReadSafeBilibiliState(statePath);
        var credentialAvailable = false;
        if (!string.IsNullOrWhiteSpace(settings.BilibiliCredentialBlob))
            credentialAvailable = !string.IsNullOrWhiteSpace(DpapiCredentialStore.Unprotect(settings.BilibiliCredentialBlob));
        var rooms = ReadArrayLength(state, "rooms");
        var tasks = ReadArrayLength(state, "tasks");
        var configuredSessions = ReadInt(state, "sessions", "configuredSessions");
        var activeSessions = ReadInt(state, "sessions", "activeSessions");
        var severity = !settings.BilibiliEnabled && !workerExists ? DiagnosticSeverity.Info
            : !workerExists ? DiagnosticSeverity.Error
            : settings.BilibiliEnabled && !credentialAvailable && !ReadBool(state, "credentialAvailable")
                ? DiagnosticSeverity.Warning
                : snapshot?.Lifecycle == WorkerLifecycle.Crashed ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Healthy;
        var workerLifecycle = snapshot is null ? "未启动" : $"{snapshot.Lifecycle}/{snapshot.Status}";
        var protocol = snapshot?.Lifecycle is WorkerLifecycle.Running or WorkerLifecycle.Starting
            ? "Worker 已启动，hello/JSONL 管道可用" : "尚未执行本次启动握手";
        var lastError = ReadString(state, "lastError");
        var details = string.Join("; ", new[]
        {
            $"启用={settings.BilibiliEnabled}",
            $"Worker存在={(workerExists ? "是" : "否")}",
            $"Worker路径={DiagnosticSanitizer.SanitizePath(File.Exists(packaged) ? packaged : development)}",
            $"完整性={integrity}",
            $"启动/握手={protocol}",
            "网络模式=DIRECT",
            "代理环境隔离=Worker 启动时清理 HTTP_PROXY/HTTPS_PROXY/ALL_PROXY 及小写变量",
            "HTTP 客户端隔离=httpx trust_env=false；未设置显式 proxy",
            $"凭据可用={(credentialAvailable || ReadBool(state, "credentialAvailable") ? "是（DPAPI）" : "否")}",
            $"登录状态={ReadString(state, "account", "loggedIn", fallback: settings.BilibiliUid > 0 ? "已登录" : "未登录")}",
            $"活动/任务={ReadArrayLength(state, "activities")}/{tasks}",
            $"直播间={rooms}",
            $"Worker状态={workerLifecycle}",
            $"ConfiguredSessions={configuredSessions}; ActiveSessions={activeSessions}; ConnectingSessions={ReadInt(state, "sessions", "connectingSessions")}; RetryingSessions={ReadInt(state, "sessions", "retryingSessions")}; FailedSessions={ReadInt(state, "sessions", "failedSessions")}",
            $"最近进度={ReadString(state, "lastProgressAt")}; 最近成功 API={ReadString(state, "lastApiSuccessAt")}; 最近恢复={ReadString(state, "lastRecoveryAt")}; 最近错误={lastError}",
        });
        return NewCheck("drops.bilibili", "Drops", "哔哩哔哩 Worker、直连与凭据", severity,
            !workerExists ? "Bilibili Worker 缺失" : settings.BilibiliEnabled && !credentialAvailable && !ReadBool(state, "credentialAvailable")
                ? "等待扫码登录" : "直连策略与 Worker 状态已检查", details);
    }

    private static string? FindDevelopmentBilibiliWorker(string start)
    {
        var current = new DirectoryInfo(start);
        for (var i = 0; current is not null && i < 8; i++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "Integrations", "Drops", "bilibili", "worker.py");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsPyInstallerExecutable(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[2];
            return stream.Length > 4096 && stream.Read(header, 0, header.Length) == 2 && header[0] == (byte)'M' && header[1] == (byte)'Z';
        }
        catch { return false; }
    }

    private static JsonElement ReadSafeBilibiliState(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }
        catch { return default; }
    }

    private static int ReadArrayLength(JsonElement owner, string property) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength() : 0;

    private static int ReadInt(JsonElement owner, string parent, string property)
    {
        if (owner.ValueKind != JsonValueKind.Object) return 0;
        if (!string.IsNullOrWhiteSpace(parent) && (!owner.TryGetProperty(parent, out owner) || owner.ValueKind != JsonValueKind.Object)) return 0;
        return owner.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;
    }

    private static bool ReadBool(JsonElement owner, string property)
    {
        return owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(property, out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    }

    private static string ReadString(JsonElement owner, string property, string? child = null, string fallback = "无")
    {
        if (owner.ValueKind != JsonValueKind.Object) return fallback;
        if (child is not null)
        {
            if (!owner.TryGetProperty(property, out owner) || owner.ValueKind != JsonValueKind.Object) return fallback;
            property = child;
        }
        return owner.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? DiagnosticSanitizer.Sanitize(value.ToString()) : fallback;
    }

    private string BuildEnvironmentText() => DiagnosticSanitizer.Sanitize(string.Join(Environment.NewLine, new[]
    {
        $"Version: {_vm.UpdateChecks.CurrentVersion}",
        $"OS: {RuntimeInformation.OSDescription}",
        $"OSArchitecture: {RuntimeInformation.OSArchitecture}",
        $"ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}",
        $"Framework: {RuntimeInformation.FrameworkDescription}",
        $"ProcessPath: {AppContext.BaseDirectory}",
        $"StartTime: {Process.GetCurrentProcess().StartTime:O}",
        $"ConfigurationDirectory: {AppPaths.Current.Root}",
        $"LogsDirectory: {AppPaths.Current.LogsDir}",
        $"ProxyEnabled: {_vm.Settings.EnableProxy}",
        $"ProxyUrl: {_vm.Settings.ProxyUrl}",
        $"FallbackDirect: {_vm.Settings.FallbackDirect}",
        "BilibiliNetworkMode: DIRECT",
        "BilibiliProxyEnvironmentIsolation: HTTP_PROXY/HTTPS_PROXY/ALL_PROXY and lowercase variants are removed in the child Worker",
    }));

    private string BuildSnapshotSummary()
    {
        try
        {
            return DiagnosticSanitizer.Sanitize(JsonSerializer.Serialize(new
            {
                current = _vm.RegionStatusSnapshot,
                snapshots = new SnapshotManagerService(_vm.RegionManager).List(),
            }, DiagnosticJson.Options));
        }
        catch (Exception ex)
        {
            return DiagnosticSanitizer.Sanitize(JsonSerializer.Serialize(new
            {
                current = _vm.RegionStatusSnapshot,
                snapshotReadError = ex.Message,
            }, DiagnosticJson.Options));
        }
    }

    private string BuildUpdateSummary()
    {
        var result = _vm.UpdateChecks.LastResult;
        return DiagnosticSanitizer.Sanitize(JsonSerializer.Serialize(new
        {
            channel = _vm.Settings.UpdateChannel.ToString(),
            currentVersion = _vm.UpdateChecks.CurrentVersion,
            lastCheck = _vm.UpdateChecks.LastCheckAt,
            lastFailure = _vm.UpdateChecks.LastFailure,
            result,
        }, DiagnosticJson.Options));
    }

    private string BuildDropsSummary() => DiagnosticSanitizer.Sanitize(JsonSerializer.Serialize(new
    {
        workers = _vm.DropsHost.Snapshots,
        snapshot = _vm.GetDropsDiagnosticSnapshot(),
    }, DiagnosticJson.Options));

    private async Task AddRecentLogsAsync(ZipArchive archive, CancellationToken token)
    {
        var files = OverwatchRegionBackupStore.EnumerateFilesWithoutReparse(AppPaths.Current.LogsDir)
            .Select(path => new FileInfo(path)).ToList();
        var cutoff = DateTime.Now - LogAge;
        long budget = MaxLogBytes;
        foreach (var file in files.Where(item => item.LastWriteTime >= cutoff)
                     .OrderByDescending(item => item.LastWriteTime))
        {
            token.ThrowIfCancellationRequested();
            if (budget <= 0) break;
            if (!TryNormalizeZipEntryName(Path.GetRelativePath(AppPaths.Current.LogsDir, file.FullName), out var relativeName))
            {
                WriteLog($"export-log-skipped-unsafe-path file={file.Name}");
                continue;
            }
            var entryName = "logs/" + relativeName;
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            await using var output = entry.Open();
            await using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            await using var writer = new StreamWriter(output, new UTF8Encoding(false), 16 * 1024, leaveOpen: false);
            string? line;
            long written = 0;
            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) is not null)
            {
                var safe = DiagnosticSanitizer.Sanitize(line) + Environment.NewLine;
                var bytes = Encoding.UTF8.GetByteCount(safe);
                if (written + bytes > budget) break;
                await writer.WriteAsync(safe.AsMemory(), token).ConfigureAwait(false);
                written += bytes;
            }
            await writer.FlushAsync(token).ConfigureAwait(false);
            budget -= written;
        }
    }

    internal static bool TryNormalizeZipEntryName(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var candidate = value.Replace('\\', '/');
        if (candidate.StartsWith('/') || candidate.Contains(':', StringComparison.Ordinal)) return false;
        var segments = candidate.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".." ||
                                    segment.Length >= 2 && segment.All(character => character == '.')))
            return false;
        normalized = string.Join('/', segments);
        return normalized.Length > 0;
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string value, CancellationToken token)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 16 * 1024, leaveOpen: false);
        await writer.WriteAsync(DiagnosticSanitizer.Sanitize(value).AsMemory(), token).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string FormatDiagnosticTime(DateTime? value) =>
        value is { } date ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "无记录";

    private static (string? Success, string? Failure, bool FailureIsLatest) ReadRecentRegionSwitchEvents()
    {
        try
        {
            var path = Path.Combine(AppPaths.Current.LogsDir, "region-switch.log");
            if (!File.Exists(path)) return (null, null, false);
            string? success = null;
            string? failure = null;
            var successOrder = -1;
            var failureOrder = -1;
            var order = 0;
            foreach (var line in File.ReadLines(path).TakeLast(300))
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("NormalizeCompleted", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("NormalizeAlreadyTarget", StringComparison.OrdinalIgnoreCase))
                {
                    success = ShortenDiagnosticLine(trimmed);
                    successOrder = order;
                }
                else if (trimmed.Contains("NormalizeFailed", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.Contains("NormalizeAllFilesFailed", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.Contains("NormalizePartialCompleted", StringComparison.OrdinalIgnoreCase))
                {
                    failure = ShortenDiagnosticLine(trimmed);
                    failureOrder = order;
                }
                order++;
            }
            return (success, failure, failureOrder > successOrder);
        }
        catch { return (null, null, false); }
    }

    private static string ShortenDiagnosticLine(string value) =>
        value.Length <= 400 ? value : value[..400] + "…";

    private static void WriteLog(string message)
    {
        try
        {
            var directory = AppPaths.Current.LogsDir;
            Directory.CreateDirectory(directory);
            lock (LogGate)
                File.AppendAllText(Path.Combine(directory, "diagnostics.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [diagnostics] {DiagnosticSanitizer.Sanitize(message)}{Environment.NewLine}");
        }
        catch { }
    }

    private static DiagnosticCheck NewCheck(string id, string category, string name, DiagnosticSeverity status,
        string summary, string details) => new()
    {
        Id = id, Category = category, Name = name, Status = status,
        Summary = DiagnosticSanitizer.Sanitize(summary), Details = DiagnosticSanitizer.Sanitize(details),
        Timestamp = DateTimeOffset.Now,
    };

    private sealed record CheckDefinition(string Id, string Category, string Name,
        Func<CancellationToken, Task<DiagnosticCheck>> Run);
}
