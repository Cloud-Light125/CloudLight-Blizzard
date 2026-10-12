using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CloudLightBlizzard.Services.Drops;

public sealed class DropsHostService : IAsyncDisposable
{
    private static long _nextGeneration;
    private sealed class WorkerConnection
    {
        public required DropsPlatform Platform { get; init; }
        public required Process Process { get; init; }
        public required StreamWriter Input { get; init; }
        public required CancellationTokenSource Lifetime { get; init; }
        public required Task OutputLoop { get; set; }
        public required Task ErrorLoop { get; set; }
        public SemaphoreSlim WriteGate { get; } = new(1, 1);
        public SemaphoreSlim CommandGate { get; } = new(1, 1);
        public ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> Pending { get; } = new();
        public WorkerLifecycle Lifecycle { get; set; } = WorkerLifecycle.Starting;
        public string Status { get; set; } = "正在启动";
        public string Summary { get; set; } = "";
        public string? LastError { get; set; }
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
        public long Generation { get; } = Interlocked.Increment(ref _nextGeneration);
        public long SnapshotRevision;
        public bool BusinessRunning { get; set; }
        public bool RuntimeErrorReported { get; set; }
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<DropsPlatform, WorkerConnection> _workers = new();
    private readonly List<WorkerConnection> _retiredWorkers = [];
    private readonly object _sync = new();
    private readonly Dictionary<DropsPlatform, SemaphoreSlim> _connectionGates = Enum.GetValues<DropsPlatform>()
        .ToDictionary(platform => platform, _ => new SemaphoreSlim(1, 1));
    private volatile bool _disposing;
    private readonly CancellationTokenSource _connectionLifetime = new();
    private DropsProxySettings _proxySettings = new(false, "", false);
    private Func<DropsPlatform, string, object?, CancellationToken, Task<JsonElement>>? _requestOverrideForSelfTest;
    private Func<DropsPlatform, ProcessStartInfo>? _startInfoOverrideForSelfTest;

    public event EventHandler<WorkerEvent>? EventReceived;
    public event EventHandler<WorkerSnapshot>? SnapshotChanged;
    public bool IsCurrentEvent(DropsPlatform platform, JsonElement payload)
    {
        if (!payload.TryGetProperty("_workerGeneration", out var value) || !value.TryGetInt64(out var generation)) return true;
        lock (_sync)
            return !_disposing && (_workers.TryGetValue(platform, out var worker)
                ? worker.Generation == generation && !worker.Lifetime.IsCancellationRequested
                : !_retiredWorkers.Any(retired => retired.Platform == platform));
    }
    public bool IsCurrentGeneration(DropsPlatform platform, long generation)
    {
        if (generation == 0) return true;
        lock (_sync)
            return _workers.TryGetValue(platform, out var worker) ? worker.Generation == generation :
                !_retiredWorkers.Any(retired => retired.Platform == platform);
    }

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
        if (_disposing) throw new ObjectDisposedException(nameof(DropsHostService));
        var requestOverride = _requestOverrideForSelfTest;
        if (requestOverride is not null)
            return await requestOverride(platform, command, payload, cancellationToken).ConfigureAwait(false);

        var worker = await EnsureConnectedAsync(platform, cancellationToken).ConfigureAwait(false);
        return await SendAsync(worker, command, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement> SendAsync(WorkerConnection worker, string command, object? payload,
        CancellationToken cancellationToken)
    {
        if (_disposing && command != "shutdown") throw new ObjectDisposedException(nameof(DropsHostService));
        var platform = worker.Platform;
        if (worker.Process.HasExited) throw new IOException($"{platform} 后台在握手前已退出。");
        if (command is not ("hello" or "set_proxy" or "shutdown"))
            await worker.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        using var commandLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, worker.Lifetime.Token);
        var control = command is "stop" or "stop_account" or "shutdown";
        if (!control) await worker.CommandGate.WaitAsync(commandLifetime.Token).ConfigureAwait(false);
        try
        {
        cancellationToken.ThrowIfCancellationRequested();
        var requestPayload = JsonSerializer.SerializeToElement(payload ?? new { }, JsonOptions);
        var accountScoped = platform == DropsPlatform.Soop && (command is "refresh" or "start_account" or "stop_account") &&
            requestPayload.ValueKind == JsonValueKind.Object &&
            (requestPayload.TryGetProperty("uid", out var accountUid) || requestPayload.TryGetProperty("userid", out accountUid)) &&
            accountUid.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(accountUid.GetString());
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!worker.Pending.TryAdd(id, completion)) throw new InvalidOperationException("无法发送后台请求。");
        // A UID retry token cancels queued work. Once sent, that account's
        // explicit stop command is responsible for cancellation; retiring the
        // shared SOOP process would also stop every other account. The request
        // deadline and Worker lifetime still bound in-flight operations.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            accountScoped ? CancellationToken.None : cancellationToken, worker.Lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(command switch
        {
            "refresh" when platform == DropsPlatform.Twitch => 90,
            "refresh" when platform == DropsPlatform.Soop => 180,
            "auto_start" when platform == DropsPlatform.Twitch => 270,
            "auto_start" when platform == DropsPlatform.Soop => 120,
            "start_account" or "stop_account" when platform == DropsPlatform.Soop => 50,
            "claim_reward" when platform == DropsPlatform.Soop => 90,
            "claim_drop" when platform == DropsPlatform.Twitch => 70,
            "network_check" => 65,
            "refresh" when platform == DropsPlatform.Bilibili => 120,
            "discover" or "claim_reward" when platform == DropsPlatform.Bilibili => 120,
            "start" when platform == DropsPlatform.Bilibili => 90,
            "stop" or "shutdown" => 40,
            _ => 30,
        }));
        using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));
        var sent = false;
        try
        {
            var envelope = JsonSerializer.Serialize(new { id, command, payload = requestPayload }, JsonOptions);
            using var writeLifetime = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
            await worker.WriteGate.WaitAsync(writeLifetime.Token).ConfigureAwait(false);
            try
            {
                timeout.Token.ThrowIfCancellationRequested();
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposing && command != "shutdown") throw new ObjectDisposedException(nameof(DropsHostService));
                sent = true; // a cancelled write may still have delivered bytes
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
        catch (OperationCanceledException) when (!worker.Lifetime.IsCancellationRequested)
        {
            // JSONL has no remote cancellation command. Once written, cancelling
            // only the wait would leave that business operation alive.
            if (sent && command is not ("hello" or "load_state" or "get_logs"))
            {
                MarkCrashed(worker, $"{command} 操作超时，已终止旧后台以避免迟到状态覆盖。");
                worker.Lifecycle = WorkerLifecycle.Stopping;
                worker.Lifetime.Cancel();
                TryTerminate(worker);
                await worker.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{platform} {command} 操作超时，请重试连接；领取结果请重新刷新确认。");
        }
        finally { worker.Pending.TryRemove(id, out _); }
        }
        finally { if (!control) worker.CommandGate.Release(); }
    }

    public Task<JsonElement> StartAsync(DropsPlatform platform, CancellationToken cancellationToken = default) =>
        RequestAsync(platform, "start", cancellationToken: cancellationToken);

    public async Task<JsonElement> StopAsync(DropsPlatform platform, CancellationToken cancellationToken = default)
    {
        if (_disposing) return JsonSerializer.SerializeToElement(new { running = false, status = "正在退出" });
        if (_requestOverrideForSelfTest is null)
        {
            lock (_sync)
                if (!_workers.TryGetValue(platform, out var current) || current.Process.HasExited)
                    return JsonSerializer.SerializeToElement(new { running = false, status = "已停止" });
        }
        try
        {
            using var stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stopTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            return await RequestAsync(platform, "stop", cancellationToken: stopTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            WorkerConnection? worker;
            lock (_sync) _workers.TryGetValue(platform, out worker);
            if (worker is null) throw;
            worker.Lifecycle = WorkerLifecycle.Stopping;
            worker.BusinessRunning = false;
            TryTerminate(worker);
            await worker.Process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            PublishUserLog(platform, "warning", "后台停止请求未完成，已关闭本应用持有的后台进程。可手动重新启动。");
            return JsonSerializer.SerializeToElement(new { running = false, status = "已停止", forcedStop = true });
        }
    }

    /// <summary>
    /// 只重启本服务实际创建并持有的 worker。不会按进程名查杀用户启动的 Python 进程。
    /// </summary>
    public async Task RestartAsync(DropsPlatform platform, CancellationToken cancellationToken = default)
    {
        WorkerConnection? worker;
        lock (_sync) _workers.TryGetValue(platform, out worker);
        if (worker is not null && !worker.Process.HasExited)
        {
            worker.Lifecycle = WorkerLifecycle.Stopping;
            try
            {
                using var graceful = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                graceful.CancelAfter(TimeSpan.FromSeconds(8));
                await RequestAsync(platform, "shutdown", cancellationToken: graceful.Token).ConfigureAwait(false);
            }
            catch { }
            worker.BusinessRunning = false;
            TryTerminate(worker);
            try { await worker.Process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch { }
        }
        if (worker is not null)
        {
            lock (_sync)
            {
                if (_workers.TryGetValue(platform, out var current) && ReferenceEquals(current, worker))
                {
                    _workers.Remove(platform);
                    _retiredWorkers.Add(worker);
                }
            }
        }

        await EnsureConnectedAsync(platform, cancellationToken).ConfigureAwait(false);
    }

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
            try
            {
                await RequestAsync(worker.Platform, "set_proxy", settings.ForWorker(worker.Platform), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                PublishUserLog(worker.Platform, "warning", $"代理策略尚未应用：{SensitiveDataRedactor.Redact(ex.Message)}，请停止并重新启动该平台。");
                EventReceived?.Invoke(this, new WorkerEvent(worker.Platform, "notice",
                    JsonSerializer.SerializeToElement(new { message = "代理策略更新失败，请停止并重新启动该平台。" })));
            }
        }
    }

    public void ConfigureProxy(DropsProxySettings settings) => _proxySettings = settings;

    internal void ConfigureRequestOverrideForSelfTest(
        Func<DropsPlatform, string, object?, CancellationToken, Task<JsonElement>>? handler) =>
        _requestOverrideForSelfTest = handler;

    internal void ConfigureStartInfoForSelfTest(Func<DropsPlatform, ProcessStartInfo> handler) =>
        _startInfoOverrideForSelfTest = handler;

    private async Task<WorkerConnection> EnsureConnectedAsync(DropsPlatform platform, CancellationToken cancellationToken)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectionLifetime.Token);
        cancellationToken = connection.Token;
        lock (_sync)
        {
            if (_disposing) throw new ObjectDisposedException(nameof(DropsHostService));
            if (_workers.TryGetValue(platform, out var ready) && !ready.Process.HasExited) return ready;
        }
        var gate = _connectionGates[platform];
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        lock (_sync)
        {
            if (_disposing) throw new ObjectDisposedException(nameof(DropsHostService));
            if (_workers.TryGetValue(platform, out var connected) && !connected.Process.HasExited)
                return connected;
            if (_workers.TryGetValue(platform, out var exited))
            {
                _workers.Remove(platform);
                _retiredWorkers.Add(exited);
            }
        }

        var startInfo = _startInfoOverrideForSelfTest?.Invoke(platform) ?? BuildStartInfo(platform);
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
        process.Exited += (_, _) => OnExited(worker);
        lock (_sync)
        {
            if (_disposing)
            {
                lifetime.Cancel();
                TryTerminate(worker);
                _retiredWorkers.Add(worker);
                throw new ObjectDisposedException(nameof(DropsHostService));
            }
            _workers[platform] = worker;
        }
        worker.OutputLoop = ReadOutputAsync(worker);
        worker.ErrorLoop = ReadErrorsAsync(worker);
        PublishSnapshot(worker);
        if (platform == DropsPlatform.Twitch)
            EventReceived?.Invoke(this, new WorkerEvent(platform, "connection_status",
                JsonSerializer.SerializeToElement(new { phase = "worker_starting", _workerGeneration = worker.Generation })));

        try
        {
            await SendAsync(worker, "hello", new { protocol = 1 }, cancellationToken).ConfigureAwait(false);
            await SendAsync(worker, "set_proxy", _proxySettings.ForWorker(platform), cancellationToken)
                .ConfigureAwait(false);
            worker.Lifecycle = WorkerLifecycle.Running;
            worker.Status = "就绪";
            worker.Ready.TrySetResult();
            PublishSnapshot(worker);
            return worker;
        }
        catch (Exception ex)
        {
            worker.Ready.TrySetException(ex);
            TryTerminate(worker);
            throw;
        }
        }
        finally { gate.Release(); }
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
            DropsPlatform.Bilibili => paths.BilibiliDropsDir,
            _ => paths.TwitchDropsDir,
        };
        var logFile = Path.Combine(paths.LogsDir, $"drops-{platformName}.log");
        var startInfo = new ProcessStartInfo
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
        if (platform == DropsPlatform.Bilibili)
        {
            // Bilibili always receives its route explicitly through set_proxy;
            // never inherit ambient proxy variables from the WPF process.
            foreach (var variable in new[]
                     { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" })
                startInfo.Environment.Remove(variable);
            startInfo.Environment["NO_PROXY"] = "*";
            startInfo.Environment["no_proxy"] = "*";
        }
        return startInfo;
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
                lock (_sync)
                    if (!_workers.TryGetValue(worker.Platform, out var current) || !ReferenceEquals(current, worker)) continue;
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
                            DropsPlatform.Bilibili => "Python SSL 组件无法加载，无法访问哔哩哔哩。请重新安装 CloudLight Blizzard。",
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
                                _workerGeneration = worker.Generation,
                            })));
                        PublishSnapshot(worker);
                    }
                    continue;
                }
                if (worker.RuntimeErrorReported) continue;
                worker.LastError = safe;
                EventReceived?.Invoke(this, new WorkerEvent(worker.Platform, "log",
                    JsonSerializer.SerializeToElement(new { level = "error", message = safe, _workerGeneration = worker.Generation })));
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void HandleMessage(WorkerConnection worker, string line)
    {
        if (worker.Lifetime.IsCancellationRequested) return;
        lock (_sync)
            if (!_workers.TryGetValue(worker.Platform, out var current) || !ReferenceEquals(current, worker)) return;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } id &&
                worker.Pending.TryGetValue(id, out var pending))
            {
                if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                {
                    var result = root.TryGetProperty("result", out var value) ? StampState(value, root, worker) : JsonSerializer.SerializeToElement(new { });
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
                ? StampState(eventPayload, root, worker)
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

    private static JsonElement StampState(JsonElement value, JsonElement envelope, WorkerConnection worker)
    {
        if (value.ValueKind != JsonValueKind.Object) return value.Clone();
        var fields = value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        fields["_workerGeneration"] = JsonSerializer.SerializeToElement(worker.Generation);
        fields["_workerSequence"] = envelope.TryGetProperty("sequence", out var sequence)
            ? sequence.Clone() : JsonSerializer.SerializeToElement(0);
        fields["_stateTime"] = value.TryGetProperty("_snapshotStarted", out var captured)
            ? captured.Clone() : envelope.TryGetProperty("_emittedAt", out var emitted)
                ? emitted.Clone() : JsonSerializer.SerializeToElement(0);
        if (fields.TryGetValue("state", out var nested) && nested.ValueKind == JsonValueKind.Object)
            fields["state"] = StampState(nested, envelope, worker);
        return JsonSerializer.SerializeToElement(fields);
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

    private void PublishSnapshot(WorkerConnection worker)
    {
        lock (_sync)
            if (!_workers.TryGetValue(worker.Platform, out var current) || !ReferenceEquals(current, worker)) return;
        SnapshotChanged?.Invoke(this, new WorkerSnapshot(worker.Platform, worker.Lifecycle, worker.Status,
            worker.Summary, worker.StartedAt, worker.Process.HasExited ? null : worker.Process.Id, worker.LastError,
            worker.Generation, Interlocked.Increment(ref worker.SnapshotRevision)));
    }

    private WorkerSnapshot GetSnapshotLocked(DropsPlatform platform)
    {
        if (!_workers.TryGetValue(platform, out var worker))
            return new(platform, WorkerLifecycle.Stopped, "未运行", "", null, null, null);
        return new(platform, worker.Lifecycle, worker.Status, worker.Summary, worker.StartedAt,
            worker.Process.HasExited ? null : worker.Process.Id, worker.LastError, worker.Generation, worker.SnapshotRevision);
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
        lock (_sync) _disposing = true;
        _connectionLifetime.Cancel();
        // A process can be between Start() and registration. Drain creation
        // gates before capturing workers so that process is also retired.
        foreach (var gate in _connectionGates.Values)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            gate.Release();
        }
        WorkerConnection[] workers;
        lock (_sync) workers = _workers.Values.Concat(_retiredWorkers).ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        foreach (var worker in workers)
        {
            if (worker.Process.HasExited) continue;
            worker.Lifecycle = WorkerLifecycle.Stopping;
            try { await SendAsync(worker, "shutdown", null, timeout.Token).ConfigureAwait(false); } catch { }
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
            var drainDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (!worker.Pending.IsEmpty && DateTimeOffset.UtcNow < drainDeadline)
                await Task.Delay(10).ConfigureAwait(false);
            // Active requests release these gates in finally. Wait for that
            // unwind before disposal rather than racing Release() with Dispose().
            if (await worker.CommandGate.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                worker.WriteGate.Dispose();
                worker.CommandGate.Dispose();
                worker.Lifetime.Dispose();
            }
            worker.Process.Dispose();
        }
        lock (_sync)
        {
            _workers.Clear();
            _retiredWorkers.Clear();
        }
        _connectionLifetime.Dispose();
    }
}
