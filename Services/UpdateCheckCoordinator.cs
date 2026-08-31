using CloudLightBlizzard.Models;

namespace CloudLightBlizzard.Services;

public enum UpdateCheckMode
{
    Automatic,
    Manual,
}

public enum UpdateCheckOutcomeKind
{
    UpdateAvailable,
    UpToDate,
    NoRelease,
    Failed,
    Suppressed,
    AlreadyChecked,
}

public sealed record UpdateCheckOutcome(UpdateCheckOutcomeKind Kind, UpdateCheckResult? Result = null);

public sealed class UpdateCheckCoordinator
{
    private readonly IUpdateService _service;
    private readonly AppSettings _settings;
    private readonly UpdateLog _log;
    private readonly object _gate = new();
    private readonly HashSet<string> _presentedVersions = new(StringComparer.OrdinalIgnoreCase);
    private Task<UpdateCheckResult>? _activeRequest;
    private bool _automaticCheckStarted;
    private bool _isChecking;
    private UpdaterState _state = UpdaterState.Idle;

    public UpdateCheckCoordinator(IUpdateService service, AppSettings settings, UpdateLog? log = null)
    {
        _service = service;
        _settings = settings;
        _log = log ?? new UpdateLog();
    }

    public event Action? CheckingChanged;
    public event Action? StateChanged;
    public string CurrentVersion => _service.CurrentVersion;
    public UpdateCheckResult? LastResult { get; private set; }
    public DateTimeOffset? LastCheckAt { get; private set; }
    public string? LastFailure { get; private set; }
    public UpdaterState State
    {
        get { lock (_gate) return _state; }
        private set
        {
            lock (_gate) _state = value;
            StateChanged?.Invoke();
        }
    }
    public bool IsChecking
    {
        get { lock (_gate) return _isChecking; }
    }

    public async Task<UpdateCheckOutcome> CheckAfterDelayAsync(
        TimeSpan delay, CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken);
        return await CheckAsync(UpdateCheckMode.Automatic, cancellationToken);
    }

    public async Task<UpdateCheckOutcome> CheckAsync(
        UpdateCheckMode mode, CancellationToken cancellationToken = default)
    {
        Task<UpdateCheckResult> request;
        lock (_gate)
        {
            if (mode == UpdateCheckMode.Automatic && _automaticCheckStarted)
                return new UpdateCheckOutcome(UpdateCheckOutcomeKind.AlreadyChecked);
            if (mode == UpdateCheckMode.Automatic) _automaticCheckStarted = true;

            if (_activeRequest is null || _activeRequest.IsCompleted)
                _activeRequest = RunRequestAsync(mode, cancellationToken);
            request = _activeRequest;
        }

        var result = await request;
        return Evaluate(mode, result);
    }

    public void SkipVersion(string version)
    {
        _settings.SkippedUpdateVersion = UpdateService.NormalizeReleaseVersion(version);
        _settings.Save();
    }

    public void RestoreSkippedVersion()
    {
        _settings.SkippedUpdateVersion = null;
        _settings.Save();
    }

    public void RemindLater(TimeSpan? delay = null)
    {
        _settings.RemindAfter = DateTimeOffset.Now.Add(delay ?? TimeSpan.FromDays(1));
        _settings.Save();
    }

    private async Task<UpdateCheckResult> RunRequestAsync(
        UpdateCheckMode mode, CancellationToken cancellationToken)
    {
        SetChecking(true);
        State = UpdaterState.Checking;
        _log.Write("Update check started", mode, _service.CurrentVersion,
            skippedVersion: _settings.SkippedUpdateVersion);
        try
        {
            var result = await _service.CheckAsync(_settings.UpdateChannel, cancellationToken);
            LastResult = result;
            LastCheckAt = DateTimeOffset.Now;
            LastFailure = result.Status == UpdateCheckResultStatus.Failed ? result.ErrorMessage : null;
            _settings.LastUpdateCheckAt = LastCheckAt;
            _settings.LastUpdateFailure = LastFailure;
            _settings.Save();
            State = result.Status == UpdateCheckResultStatus.Failed
                ? UpdaterState.Failed : UpdaterState.Idle;
            _log.Write(result.Status == UpdateCheckResultStatus.Failed ? "Update check failed" : "Update check completed",
                mode, result.CurrentVersion, result.LatestVersion, _settings.SkippedUpdateVersion,
                result.HasUpdate, result.TechnicalDetail ?? result.ErrorMessage);
            return result;
        }
        finally
        {
            SetChecking(false);
            if (State == UpdaterState.Checking) State = UpdaterState.Idle;
        }
    }

    private UpdateCheckOutcome Evaluate(UpdateCheckMode mode, UpdateCheckResult result)
    {
        if (result.Status == UpdateCheckResultStatus.Failed)
            return new UpdateCheckOutcome(mode == UpdateCheckMode.Automatic
                ? UpdateCheckOutcomeKind.Suppressed : UpdateCheckOutcomeKind.Failed, result);
        if (result.Status == UpdateCheckResultStatus.NoRelease)
            return new UpdateCheckOutcome(mode == UpdateCheckMode.Automatic
                ? UpdateCheckOutcomeKind.Suppressed : UpdateCheckOutcomeKind.NoRelease, result);
        if (!result.HasUpdate)
            return new UpdateCheckOutcome(mode == UpdateCheckMode.Automatic
                ? UpdateCheckOutcomeKind.Suppressed : UpdateCheckOutcomeKind.UpToDate, result);

        if (mode == UpdateCheckMode.Automatic)
        {
            lock (_gate)
            {
                if (string.Equals(UpdateService.NormalizeReleaseVersion(_settings.SkippedUpdateVersion),
                        result.LatestVersion, StringComparison.OrdinalIgnoreCase))
                    return new UpdateCheckOutcome(UpdateCheckOutcomeKind.Suppressed, result);
                if (_settings.RemindAfter is { } remindAfter && remindAfter > DateTimeOffset.Now)
                    return new UpdateCheckOutcome(UpdateCheckOutcomeKind.Suppressed, result);
                if (!_presentedVersions.Add(result.LatestVersion))
                    return new UpdateCheckOutcome(UpdateCheckOutcomeKind.Suppressed, result);
            }
        }
        State = UpdaterState.UpdateAvailable;
        return new UpdateCheckOutcome(UpdateCheckOutcomeKind.UpdateAvailable, result);
    }

    private void SetChecking(bool value)
    {
        lock (_gate) _isChecking = value;
        CheckingChanged?.Invoke();
    }
}
