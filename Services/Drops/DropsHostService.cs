using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

public sealed class DropsHostService : IAsyncDisposable
{
    private sealed class WorkerConnection
    {
        public required DropsPlatform Platform { get; init; }
        public required Process Process { get; init; }
        public required StreamWriter Input { get; init; }
        public required CancellationTokenSource Lifetime { get; init; }
        public required Task OutputLoop { get; set; }
        public required Task ErrorLoop { get; set; }
        public SemaphoreSlim WriteGate { get; } = new(1, 1);
        public ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> Pending { get; } = new();
        public WorkerLifecycle Lifecycle { get; set; } = WorkerLifecycle.Starting;
        public string Status { get; set; } = "正在启动";
        public string Summary { get; set; } = "";
        public string? LastError { get; set; }
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
        public bool BusinessRunning { get; set; }
        public bool RuntimeErrorReported { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<DropsPlatform, WorkerConnection> _workers = new();
    private readonly List<WorkerConnection> _retiredWorkers = [];
    private readonly object _sync = new();
    private bool _disposing;
    private DropsProxySettings _proxySettings = new(false, "", false);

    public event EventHandler<WorkerEvent>? EventReceived;
    public event EventHandler<WorkerSnapshot>? SnapshotChanged;

    public void PublishUserLog(DropsPlatform platform, string level, string message) =>
        EventReceived?.Invoke(this, new WorkerEvent(platform, "log",
            JsonSerializer.SerializeToElement(new
            {
                level,
                message = SensitiveDataRedactor.Redact(message),
                userFacing = true,
            })));

    public bool AnyRunning
    {
        get { lock (_sync) return _workers.Values.Any(worker => worker.BusinessRunning); }
    }

    public IReadOnlyList<WorkerSnapshot> Snapshots
    {
        get
        {
            lock (_sync)
                return Enum.GetValues<DropsPlatform>().Select(GetSnapshotLocked).ToList();
        }
    }

    public async Task<JsonElement> RequestAsync(DropsPlatform platform, string command, object? payload = null,
        CancellationToken cancellationToken = default)
    {
        var worker = await EnsureConnectedAsync(platform, cancellationToken).ConfigureAwait(false);
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!worker.Pending.TryAdd(id, completion)) throw new InvalidOperationException("无法发送后台请求。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(command switch
        {
            "refresh" when platform == DropsPlatform.Twitch => 90,
            "auto_start" when platform == DropsPlatform.Twitch => 270,
            "auto_start" when platform == DropsPlatform.Soop => 120,
            "claim_reward" when platform == DropsPlatform.Soop => 90,
            "stop" or "shutdown" => 40,
            _ => 30,
        }));
        using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));
        try
        {
            var envelope = JsonSerializer.Serialize(new { id, command, payload = payload ?? new { } }, JsonOptions);
            await worker.WriteGate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                await worker.Input.WriteLineAsync(envelope.AsMemory(), timeout.Token).ConfigureAwait(false);
                await worker.Input.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            finally { worker.WriteGate.Release(); }
            var result = await completion.Task.ConfigureAwait(false);
            if (command is "start" or "auto_start")
            {
                worker.BusinessRunning = result.ValueKind == JsonValueKind.Object &&
                                         result.TryGetProperty("running", out var running)
                    ? running.GetBoolean()
                    : true;
            }
            if (command == "stop") worker.BusinessRunning = false;
            PublishSnapshot(worker);
            return result;
        }
        finally { worker.Pending.TryRemove(id, out _); }
    }

    public Task<JsonElement> StartAsync(DropsPlatform platform, CancellationToken cancellationToken = default) =>
        RequestAsync(platform, "start", cancellationToken: cancellationToken);

    public Task<JsonElement> StopAsync(DropsPlatform platform, CancellationToken cancellationToken = default) =>
        RequestAsync(platform, "stop", cancellationToken: cancellationToken);

    public async Task<JsonElement> ClearTwitchAuthenticationAsync(CancellationToken cancellationToken = default)
    {
        WorkerConnection? worker;
        lock (_sync) _workers.TryGetValue(DropsPlatform.Twitch, out worker);
        if (worker is not null && !worker.Process.HasExited)
        {
            using var graceful = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            graceful.CancelAfter(TimeSpan.FromSeconds(8));
            try { await RequestAsync(DropsPlatform.Twitch, "clear_auth", cancellationToken: graceful.Token).ConfigureAwait(false); }
            catch { /* A stuck login/network call is terminated below. */ }

            worker.Lifecycle = WorkerLifecycle.Stopping;
            worker.BusinessRunning = false;
            TryTerminate(worker);
            try { await worker.Process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch { }
            lock (_sync)
            {
                if (_workers.TryGetValue(DropsPlatform.Twitch, out var current) && ReferenceEquals(current, worker))
                {
                    _workers.Remove(DropsPlatform.Twitch);
                    _retiredWorkers.Add(worker);
                }
            }
        }

        var paths = AppPaths.Current;
        paths.EnsureDirectories();
        File.Delete(Path.Combine(paths.TwitchDropsDir, "cookies.jar"));
        File.Delete(Path.Combine(paths.TwitchDropsDir, "cookies.jar.tmp"));
        return await RequestAsync(DropsPlatform.Twitch, "load_state", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        WorkerConnection[] workers;
        lock (_sync) workers = _workers.Values.ToArray();
        foreach (var worker in workers)
        {
            if (worker.Process.HasExited) continue;
            try { await RequestAsync(worker.Platform, "stop", cancellationToken: cancellationToken).ConfigureAwait(false); }
            catch { }
        }
    }

    public async Task ApplyProxyAsync(DropsProxySettings settings, CancellationToken cancellationToken = default)
    {
        _proxySettings = settings;
        WorkerConnection[] workers;
        lock (_sync) workers = _workers.Values.ToArray();
        foreach (var worker in workers)
        {
            if (worker.Process.HasExited) continue;
            try { await RequestAsync(worker.Platform, "set_proxy", settings, cancellationToken).ConfigureAwait(false); }
            catch { }
        }
    }

    public void ConfigureProxy(DropsProxySettings settings) => _proxySettings = settings;

    private async Task<WorkerConnection> EnsureConnectedAsync(DropsPlatform platform, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_workers.TryGetValue(platform, out var connected) && !connected.Process.HasExited)
                return connected;
        }

        var startInfo = BuildStartInfo(platform);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException($"无法启动 {platform} 后台服务。");
        var lifetime = new CancellationTokenSource();
        var worker = new WorkerConnection
        {
            Platform = platform,
            Process = process,
            Input = process.StandardInput,
            Lifetime = lifetime,
            OutputLoop = Task.CompletedTask,
            ErrorLoop = Task.CompletedTask,
        };
        worker.Input.AutoFlush = true;
        worker.OutputLoop = ReadOutputAsync(worker);
        worker.ErrorLoop = ReadErrorsAsync(worker);
        process.Exited += (_, _) => OnExited(worker);
        lock (_sync) _workers[platform] = worker;
        PublishSnapshot(worker);
        if (platform == DropsPlatform.Twitch)
            EventReceived?.Invoke(this, new WorkerEvent(platform, "connection_status",
                JsonSerializer.SerializeToElement(new { phase = "worker_starting" })));

        try
        {
            await RequestAsync(platform, "hello", new { protocol = 1 }, cancellationToken).ConfigureAwait(false);
            await RequestAsync(platform, "set_proxy", _proxySettings, cancellationToken).ConfigureAwait(false);
            worker.Lifecycle = WorkerLifecycle.Running;
            worker.Status = "就绪";
            PublishSnapshot(worker);
            return worker;
        }
        catch
        {
            TryTerminate(worker);
            throw;
        }
    }

    private static ProcessStartInfo BuildStartInfo(DropsPlatform platform)
    {
        var platformName = platform.ToString().ToLowerInvariant();
        var appBase = AppContext.BaseDirectory;
        var packaged = Path.Combine(appBase, "_internal", "drops", platformName, $"{platformName}-worker.exe");
        string executable;
        string scriptArguments = "";
        if (File.Exists(packaged)) executable = packaged;
        else
        {
            var script = FindDevelopmentWorker(appBase, platformName)
                         ?? throw new FileNotFoundException($"未找到 {platformName} 后台组件，请重新安装当前版本。", packaged);
            executable = ResolvePython(script);
            scriptArguments = Quote(script) + " ";
        }

        var paths = AppPaths.Current;
        paths.EnsureDirectories();
        var dataDirectory = platform switch
        {
            DropsPlatform.Soop => paths.SoopDropsDir,
            DropsPlatform.YouTube => paths.YouTubeDropsDir,
            _ => paths.TwitchDropsDir,
        };
        var logFile = Path.Combine(paths.LogsDir, $"drops-{platformName}.log");
        return new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"{scriptArguments}--data-dir {Quote(dataDirectory)} --log-file {Quote(logFile)}",
            WorkingDirectory = Path.GetDirectoryName(File.Exists(packaged) ? packaged : FindDevelopmentWorker(appBase, platformName)!)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
    }

    private static string? FindDevelopmentWorker(string start, string platformName)
    {
        var current = new DirectoryInfo(start);
        for (var i = 0; current is not null && i < 8; i++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "Integrations", "Drops", platformName, "worker.py");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string ResolvePython(string workerScript)
    {
        var configured = Environment.GetEnvironmentVariable("CLOUDLIGHT_DROPS_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var current = new DirectoryInfo(Path.GetDirectoryName(workerScript)!);
        while (current is not null)
        {
            var bundled = Path.Combine(current.FullName, ".worker-build-venv", "Scripts", "python.exe");
            if (File.Exists(bundled)) return bundled;
            current = current.Parent;
        }
        return "python";
    }

    private async Task ReadOutputAsync(WorkerConnection worker)
    {
        try
        {
            while (!worker.Lifetime.IsCancellationRequested &&
                   await worker.Process.StandardOutput.ReadLineAsync(worker.Lifetime.Token).ConfigureAwait(false) is { } line)
                HandleMessage(worker, line);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MarkCrashed(worker, ex.Message); }
    }

    private async Task ReadErrorsAsync(WorkerConnection worker)
    {
        try
        {
            while (!worker.Lifetime.IsCancellationRequested &&
                   await worker.Process.StandardError.ReadLineAsync(worker.Lifetime.Token).ConfigureAwait(false) is { } line)
            {
                var safe = SensitiveDataRedactor.Redact(line);
                if (string.IsNullOrWhiteSpace(safe)) continue;
                if (IsSslRuntimeError(safe))
                {
                    if (!worker.RuntimeErrorReported)
                    {
                        worker.RuntimeErrorReported = true;
                        worker.LastError = "Python SSL 运行库无法加载。";
                        var message = worker.Platform switch
                        {
                            DropsPlatform.Twitch => "Twitch 后台无法启动 HTTPS：Python SSL 运行库无法加载。请重新安装 CloudLight Blizzard。",
                            DropsPlatform.YouTube => "Python SSL 组件无法加载，无法访问 YouTube。请重新安装 CloudLight Blizzard。",
                            _ => "Python SSL 组件无法加载，SOOP 后台无法建立 HTTPS 连接。请重新安装 CloudLight Blizzard。",
                        };
                        EventReceived?.Invoke(this, new WorkerEvent(worker.Platform, "runtime_error",
                            JsonSerializer.SerializeToElement(new
                            {
                                component = "ssl",
                                code = "ssl_runtime_unavailable",
                                message,
                                retryable = false,
                                firstOccurrence = true,
                            })));
                        PublishSnapshot(worker);
                    }
                    continue;
                }
                if (worker.RuntimeErrorReported) continue;
                worker.LastError = safe;
                EventReceived?.Invoke(this, new WorkerEvent(worker.Platform, "log",
                    JsonSerializer.SerializeToElement(new { level = "error", message = safe })));
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void HandleMessage(WorkerConnection worker, string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } id &&
                worker.Pending.TryGetValue(id, out var pending))
            {
                if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                {
                    var result = root.TryGetProperty("result", out var value) ? value.Clone() : JsonSerializer.SerializeToElement(new { });
                    pending.TrySetResult(result);
                }
                else
                {
                    var message = root.TryGetProperty("error", out var error) ? error.GetString() : "后台请求失败。";
                    pending.TrySetException(new InvalidOperationException(SensitiveDataRedactor.Redact(message)));
                }
                return;
            }
            if (!root.TryGetProperty("event", out var eventElement) || eventElement.GetString() is not { } eventName) return;
            var payload = root.TryGetProperty("payload", out var eventPayload)
                ? eventPayload.Clone()
                : JsonSerializer.SerializeToElement(new { });
            if (eventName == "status") UpdateStatus(worker, payload);
            EventReceived?.Invoke(this, new WorkerEvent(worker.Platform, eventName, payload));
        }
        catch (JsonException)
        {
            worker.LastError = "后台服务返回了无效数据。";
            PublishSnapshot(worker);
        }
    }

    private void UpdateStatus(WorkerConnection worker, JsonElement payload)
    {
        if (payload.TryGetProperty("status", out var status)) worker.Status = status.GetString() ?? worker.Status;
        if (payload.TryGetProperty("summary", out var summary)) worker.Summary = summary.GetString() ?? "";
        if (payload.TryGetProperty("running", out var running) && running.ValueKind is JsonValueKind.True or JsonValueKind.False)
            worker.BusinessRunning = running.GetBoolean();
        PublishSnapshot(worker);
    }

    private void OnExited(WorkerConnection worker)
    {
        worker.Lifetime.Cancel();
        foreach (var pending in worker.Pending.Values)
            pending.TrySetException(new IOException($"{worker.Platform} 后台服务已退出。"));
        if (!_disposing && worker.Lifecycle is not WorkerLifecycle.Stopping)
            MarkCrashed(worker, $"后台服务异常退出（代码 {worker.Process.ExitCode}）。");
        else
        {
            worker.Lifecycle = WorkerLifecycle.Stopped;
            worker.BusinessRunning = false;
            worker.Status = "已停止";
            PublishSnapshot(worker);
        }
    }

    private void MarkCrashed(WorkerConnection worker, string error)
    {
        worker.Lifecycle = WorkerLifecycle.Crashed;
        worker.BusinessRunning = false;
        worker.Status = "异常退出";
        worker.LastError = SensitiveDataRedactor.Redact(error);
        PublishSnapshot(worker);
    }

    private void PublishSnapshot(WorkerConnection worker) =>
        SnapshotChanged?.Invoke(this, new WorkerSnapshot(worker.Platform, worker.Lifecycle, worker.Status,
            worker.Summary, worker.StartedAt, worker.Process.HasExited ? null : worker.Process.Id, worker.LastError));

    private WorkerSnapshot GetSnapshotLocked(DropsPlatform platform)
    {
        if (!_workers.TryGetValue(platform, out var worker))
            return new(platform, WorkerLifecycle.Stopped, "未运行", "", null, null, null);
        return new(platform, worker.Lifecycle, worker.Status, worker.Summary, worker.StartedAt,
            worker.Process.HasExited ? null : worker.Process.Id, worker.LastError);
    }

    private static void TryTerminate(WorkerConnection worker)
    {
        try { if (!worker.Process.HasExited) worker.Process.Kill(entireProcessTree: true); } catch { }
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"") + '"';

    private static bool IsSslRuntimeError(string value) =>
        value.Contains("DLL load failed while importing _ssl", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("ModuleNotFoundError: _ssl", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("No module named '_ssl'", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("SSL is not supported", StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        _disposing = true;
        WorkerConnection[] workers;
        lock (_sync) workers = _workers.Values.Concat(_retiredWorkers).ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        foreach (var worker in workers)
        {
            if (worker.Process.HasExited) continue;
            worker.Lifecycle = WorkerLifecycle.Stopping;
            try { await RequestAsync(worker.Platform, "shutdown", cancellationToken: timeout.Token).ConfigureAwait(false); } catch { }
        }
        foreach (var worker in workers)
        {
            try { await worker.Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); } catch { TryTerminate(worker); }
            try
            {
                await Task.WhenAll(worker.OutputLoop, worker.ErrorLoop)
                    .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch { }
            worker.Lifetime.Cancel();
            worker.Lifetime.Dispose();
            worker.WriteGate.Dispose();
            worker.Process.Dispose();
        }
        lock (_sync)
        {
            _workers.Clear();
            _retiredWorkers.Clear();
        }
    }
}
