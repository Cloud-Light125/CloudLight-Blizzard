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
            $"CloudLight Blizzard {_vm.UpdateChecks.CurrentVersion}", "正式版本保持为 2.0.10。"))),
        Define("app.runtime", "应用", ".NET / 系统运行环境", _ => Task.FromResult(NewCheck("app.runtime", "应用", ".NET / 系统运行环境", DiagnosticSeverity.Healthy,
            $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}",
            $"Runtime={RuntimeInformation.FrameworkDescription}; Process={Environment.Is64BitProcess switch { true => "x64", false => "x86" }}; 启动时间={Process.GetCurrentProcess().StartTime:yyyy-MM-dd HH:mm:ss}"))),
        Define("app.paths", "应用", "程序、配置与日志路径", _ => Task.FromResult(CheckPaths())),
        Define("app.config", "应用", "配置文件可读写", _ => Task.FromResult(CheckConfig())),
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
        Define("drops.platforms", "Drops", "SOOP / Twitch / YouTube 状态", _ => Task.FromResult(CheckDropsPlatforms())),
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
        var writable = false;
        try
        {
            Directory.CreateDirectory(AppPaths.Current.Root);
            var probe = Path.Combine(AppPaths.Current.Root, ".diagnostic-write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            writable = true;
        }
        catch { }
        var severity = !exists && writable ? DiagnosticSeverity.Warning
            : readable && writable ? DiagnosticSeverity.Healthy : DiagnosticSeverity.Error;
        return NewCheck("app.config", "应用", "配置文件可读写", severity,
            !exists ? "配置文件尚未生成，将使用默认设置" : readable && writable ? "配置可读写" : "配置文件不可正常读写",
            $"存在={(exists ? "是" : "否")}; 读取={(readable ? "正常" : "失败")}; 写入={(writable ? "正常" : "失败")}; 文件={DiagnosticSanitizer.SanitizePath(configPath)}");
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
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $"cloudlight-diagnostic-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return NewCheck(id, "磁盘", name, DiagnosticSeverity.Healthy, "可写", DiagnosticSanitizer.SanitizePath(path));
        }
        catch (Exception ex) { return NewCheck(id, "磁盘", name, DiagnosticSeverity.Error, "不可写", ex.Message); }
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
            var region = await _vm.GetRegionStatusAsync(verifyFiles: false).ConfigureAwait(false);
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
        var severity = status.State == RegionBackupState.Error || status.CurrentRegion == CurrentGameRegion.Mixed
            ? DiagnosticSeverity.Error : status.CurrentRegion == CurrentGameRegion.Unknown ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy;
        return NewCheck("region.state", "区服", "当前区服与切换状态", severity,
            $"当前 {MainViewModel.RegionDisplayName(status.CurrentRegion)} · {status.State}",
            $"最近成功区服={MainViewModel.RegionDisplayName(status.LastSuccessfulRegion)}; 未完成操作={_vm.IsRegionOperationBusy}");
    }

    private DiagnosticCheck CheckPendingRegionOperation() => NewCheck("region.pending", "区服", "未完成操作",
        _vm.HasPendingRegionPreparation ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy,
        _vm.HasPendingRegionPreparation ? "存在未完成的区服准备" : "没有未完成切换操作",
        $"Preparation={_vm.HasPendingRegionPreparation}; OperationBusy={_vm.IsRegionOperationBusy}");

    private DiagnosticCheck CheckSnapshotState()
    {
        var status = _vm.RegionStatusSnapshot;
        var hasVerified = status.BackupMode == RegionBackupMode.VerifiedDifference &&
                          status.State is (RegionBackupState.Ready or RegionBackupState.Stale);
        var hasFull = status.BackupMode == RegionBackupMode.FullSnapshot &&
                      status.State is (RegionBackupState.Ready or RegionBackupState.Stale);
        var severity = status.State == RegionBackupState.Error ? DiagnosticSeverity.Error
            : status.State == RegionBackupState.Empty ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy;
        return NewCheck("snapshot.verified", "快照", "VerifiedDifference / FullSnapshot", severity,
            status.State == RegionBackupState.Empty ? "尚未生成快照" : $"{status.BackupMode} · {status.DifferenceCount:N0} 个差异文件",
            $"VerifiedDifference={(hasVerified ? "存在" : "未启用/不可用")}; FullSnapshot={(hasFull ? "存在" : "未启用/不可用")}; 文件数={status.DifferenceCount:N0}; 大小={UpdateDownloadService.FormatBytes(status.BackupBytes)}");
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
        var probes = new[] { report.Proxy, report.Announcement, report.Update, report.Soop, report.Twitch }
            .Where(item => item is not null).Cast<CloudNetworkProbeResult>().ToArray();
        var failures = probes.Count(item => !item.Success);
        var severity = failures == 0 ? DiagnosticSeverity.Healthy : failures >= 2 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
        return NewCheck("network.services", "网络", "代理、公告与更新网络", severity,
            failures == 0 ? "代理、公告、更新服务正常" : $"{failures} 项网络检查异常",
            string.Join("; ", new[]
            {
                $"代理={report.Proxy.Message} / HTTP {report.Proxy.StatusCode?.ToString() ?? "n/a"}",
                $"公告={report.Announcement.Message} / {report.Announcement.Route}",
                $"更新={report.Update.Message} / {report.Update.Route}",
                $"SOOP={(report.Soop?.Message ?? "未执行")} / {report.Soop?.Route ?? "n/a"}",
                $"Twitch={(report.Twitch?.Message ?? "未执行")} / {report.Twitch?.Route ?? "n/a"}",
            }.Select(DiagnosticSanitizer.Sanitize)));
    }

    private async Task<DiagnosticCheck> CheckUpdateMetadataAsync(CancellationToken token)
    {
        var outcome = await _vm.UpdateChecks.CheckAsync(UpdateCheckMode.Manual, token).ConfigureAwait(false);
        var result = outcome.Result;
        if (outcome.Kind == UpdateCheckOutcomeKind.Failed || result?.Status == UpdateCheckResultStatus.Failed)
            return NewCheck("update.metadata", "更新", "更新元数据与安装包", DiagnosticSeverity.Error, "更新接口失败", result?.ErrorMessage ?? "未知错误");
        if (outcome.Kind == UpdateCheckOutcomeKind.NoRelease)
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
        var values = new[] { state.SoopStatus, state.TwitchStatus, state.YouTubeStatus };
        var abnormal = values.Count(value => value.Contains("失败", StringComparison.OrdinalIgnoreCase) || value.Contains("异常", StringComparison.OrdinalIgnoreCase));
        return NewCheck("drops.platforms", "Drops", "SOOP / Twitch / YouTube 状态",
            abnormal > 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy,
            abnormal == 0 ? "平台状态可用" : $"{abnormal} 个平台需要注意",
            $"SOOP={state.SoopStatus}; Twitch={state.TwitchStatus}; YouTube={state.YouTubeStatus}; 最近错误={state.RecentNetworkError}");
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
    }));

    private string BuildSnapshotSummary() => DiagnosticSanitizer.Sanitize(JsonSerializer.Serialize(_vm.RegionStatusSnapshot, DiagnosticJson.Options));

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
        var files = new List<FileInfo>();
        if (Directory.Exists(AppPaths.Current.LogsDir))
            files.AddRange(new DirectoryInfo(AppPaths.Current.LogsDir).EnumerateFiles("*", SearchOption.AllDirectories));
        var cutoff = DateTime.Now - LogAge;
        long budget = MaxLogBytes;
        foreach (var file in files.Where(item => item.LastWriteTime >= cutoff)
                     .OrderByDescending(item => item.LastWriteTime))
        {
            token.ThrowIfCancellationRequested();
            if (budget <= 0) break;
            var entryName = "logs/" + DiagnosticSanitizer.SanitizePath(Path.GetRelativePath(AppPaths.Current.LogsDir, file.FullName)).Replace('\\', '/');
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
