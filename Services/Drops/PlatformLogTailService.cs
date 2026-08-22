using System.IO;
using System.Text;

namespace CloudLightBlizzard.Services.Drops;

public sealed record PlatformLogChunk(DropsPlatform Platform, string Text, bool Reset)
{
    public long Revision { get; init; }
}

/// <summary>Captures the per-platform log positions at desktop process startup.</summary>
public sealed class PlatformLogSession
{
    private readonly Dictionary<DropsPlatform, (string FilePath, long StartOffset)> _files;

    public PlatformLogSession(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);
        _files = Enum.GetValues<DropsPlatform>().ToDictionary(
            platform => platform,
            platform =>
            {
                var path = Path.Combine(logsDirectory, $"drops-{platform.ToString().ToLowerInvariant()}.log");
                try
                {
                    var info = new FileInfo(path);
                    return (path, info.Exists ? info.Length : 0L);
                }
                catch (IOException) { return (path, 0L); }
                catch (UnauthorizedAccessException) { return (path, 0L); }
            });
    }

    internal string FilePath(DropsPlatform platform) => _files[platform].FilePath;
    internal long StartOffset(DropsPlatform platform) => _files[platform].StartOffset;
    internal string LogsDirectory => Path.GetDirectoryName(_files[DropsPlatform.Soop].FilePath)!;
}

/// <summary>Incrementally tails the three independent drops logs with a watcher plus timer fallback.</summary>
public sealed class PlatformLogTailService : IAsyncDisposable
{
    private sealed class TailState
    {
        public required DropsPlatform Platform { get; init; }
        public required string FilePath { get; init; }
        public long LastOffset { get; set; }
        public long LastLength { get; set; }
        public DateTime LastWriteTime { get; set; }
        public DateTime CreationTime { get; set; }
        public bool ResetRequested { get; set; }
        public long Revision { get; set; }
        public Decoder Decoder { get; set; } = Encoding.UTF8.GetDecoder();
        public StringBuilder Content { get; } = new();
    }

    private readonly Dictionary<DropsPlatform, TailState> _states;
    private readonly FileSystemWatcher _watcher;
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(750));
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private Task? _pollLoop;

    public event EventHandler<PlatformLogChunk>? Changed;

    public PlatformLogTailService(string logsDirectory)
        : this(new PlatformLogSession(logsDirectory))
    {
    }

    public PlatformLogTailService(PlatformLogSession session)
    {
        _states = Enum.GetValues<DropsPlatform>().ToDictionary(
            platform => platform,
            platform => new TailState
            {
                Platform = platform,
                FilePath = session.FilePath(platform),
                LastOffset = session.StartOffset(platform),
                LastLength = session.StartOffset(platform),
            });
        _watcher = new FileSystemWatcher(session.LogsDirectory, "drops-*.log")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            EnableRaisingEvents = false,
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_pollLoop is not null) return;
        _watcher.EnableRaisingEvents = true;
        await PollAsync(cancellationToken).ConfigureAwait(false);
        _pollLoop = RunPollLoopAsync();
    }

    public async Task RefreshAsync(DropsPlatform platform, CancellationToken cancellationToken = default)
    {
        await PollAsync(cancellationToken, platform).ConfigureAwait(false);
    }

    public string GetCurrentText(DropsPlatform platform)
    {
        lock (_states)
            return _states[platform].Content.ToString();
    }

    /// <summary>Clears only this process' visible buffer and skips bytes already on disk.</summary>
    public async Task<long> ClearDisplayAsync(
        DropsPlatform platform, CancellationToken cancellationToken = default)
    {
        try
        {
            await _pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return -1; }

        try
        {
            var state = _states[platform];
            FileInfo? info = null;
            try
            {
                info = new FileInfo(state.FilePath);
                if (!info.Exists) info = null;
                else info.Refresh();
            }
            catch (IOException) { info = null; }
            catch (UnauthorizedAccessException) { info = null; }

            lock (_states)
            {
                state.LastOffset = info?.Length ?? 0;
                state.LastLength = info?.Length ?? 0;
                state.LastWriteTime = info?.LastWriteTimeUtc ?? default;
                state.CreationTime = info?.CreationTimeUtc ?? default;
                state.ResetRequested = false;
                state.Decoder = Encoding.UTF8.GetDecoder();
                state.Content.Clear();
                return ++state.Revision;
            }
        }
        finally { _pollGate.Release(); }
    }

    private async Task RunPollLoopAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
                await PollAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var state = FindState(e.FullPath);
        if (state is null) return;
        if (e.ChangeType is WatcherChangeTypes.Created or WatcherChangeTypes.Deleted)
        {
            lock (_states) state.ResetRequested = true;
        }
        _ = PollAsync(_lifetime.Token, state.Platform);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        var oldState = FindState(e.OldFullPath);
        var newState = FindState(e.FullPath);
        lock (_states)
        {
            if (oldState is not null) oldState.ResetRequested = true;
            if (newState is not null) newState.ResetRequested = true;
        }
        if (oldState is not null) _ = PollAsync(_lifetime.Token, oldState.Platform);
        if (newState is not null && newState != oldState) _ = PollAsync(_lifetime.Token, newState.Platform);
    }

    private TailState? FindState(string path)
    {
        lock (_states)
            return _states.Values.FirstOrDefault(state =>
                string.Equals(state.FilePath, path, StringComparison.OrdinalIgnoreCase));
    }

    private async Task PollAsync(CancellationToken cancellationToken, DropsPlatform? only = null)
    {
        try
        {
            await _pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }

        try
        {
            var targets = only.HasValue ? [_states[only.Value]] : _states.Values.ToArray();
            foreach (var state in targets)
                await ReadAppendAsync(state, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally { _pollGate.Release(); }
    }

    private async Task ReadAppendAsync(TailState state, CancellationToken cancellationToken)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(state.FilePath);
            if (!info.Exists)
            {
                lock (_states)
                {
                    if (state.ResetRequested)
                        ResetCursor(state);
                }
                return;
            }
            info.Refresh();
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        bool reset;
        lock (_states)
        {
            reset = state.ResetRequested || info.Length < state.LastOffset ||
                    (state.CreationTime != default && info.CreationTimeUtc != state.CreationTime) ||
                    (state.LastWriteTime != default && info.Length == state.LastLength && state.LastOffset > 0 &&
                     info.LastWriteTimeUtc != state.LastWriteTime);
            if (reset) ResetCursor(state);
        }

        if (info.Length <= state.LastOffset)
        {
            state.LastLength = info.Length;
            state.LastWriteTime = info.LastWriteTimeUtc;
            state.CreationTime = info.CreationTimeUtc;
            state.ResetRequested = false;
            return;
        }

        byte[] bytes;
        try
        {
            await using var stream = new FileStream(state.FilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            stream.Seek(state.LastOffset, SeekOrigin.Begin);
            using var memory = new MemoryStream((int)Math.Min(info.Length - state.LastOffset, 2_000_000));
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            bytes = memory.ToArray();
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        if (bytes.Length == 0) return;
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        state.Decoder.Convert(bytes, 0, bytes.Length, chars, 0, chars.Length, false,
            out var bytesUsed, out var charsUsed, out _);
        var text = new string(chars, 0, charsUsed);
        lock (_states)
        {
            state.LastOffset += bytesUsed;
            state.LastLength = info.Length;
            state.LastWriteTime = info.LastWriteTimeUtc;
            state.CreationTime = info.CreationTimeUtc;
            state.ResetRequested = false;
            state.Content.Append(text);
        }
        Changed?.Invoke(this, new PlatformLogChunk(state.Platform, text, false)
        {
            Revision = state.Revision,
        });
    }

    private static void ResetCursor(TailState state)
    {
        state.LastOffset = 0;
        state.LastLength = 0;
        state.LastWriteTime = default;
        state.CreationTime = default;
        state.ResetRequested = false;
        state.Decoder = Encoding.UTF8.GetDecoder();
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.EnableRaisingEvents = false;
        _lifetime.Cancel();
        if (_pollLoop is not null)
        {
            try { await _pollLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        _watcher.Dispose();
        _timer.Dispose();
        _lifetime.Dispose();
        _pollGate.Dispose();
    }
}
