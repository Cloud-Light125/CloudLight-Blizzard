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

    public UpdateCheckCoordinator(IUpdateService service, AppSettings settings, UpdateLog? log = null)
    {
        _service = service;
        _settings = settings;
        _log = log ?? new UpdateLog();
    }

    public event Action? CheckingChanged;
    public string CurrentVersion => _service.CurrentVersion;
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
        _settings.SkippedUpdateVersion = UpdateService.NormalizeVersion(version);
        _settings.Save();
    }

    public void RestoreSkippedVersion()
    {
        _settings.SkippedUpdateVersion = null;
        _settings.Save();
    }

    private async Task<UpdateCheckResult> RunRequestAsync(
        UpdateCheckMode mode, CancellationToken cancellationToken)
    {
        SetChecking(true);
        _log.Write("Update check started", mode, _service.CurrentVersion,
            skippedVersion: _settings.SkippedUpdateVersion);
        try
        {
            var result = await _service.CheckAsync(cancellationToken);
            _log.Write(result.Status == UpdateCheckResultStatus.Failed ? "Update check failed" : "Update check completed",
                mode, result.CurrentVersion, result.LatestVersion, _settings.SkippedUpdateVersion,
                result.HasUpdate, result.ErrorMessage);
            return result;
        }
        finally
        {
            SetChecking(false);
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

        lock (_gate)
        {
            if (mode == UpdateCheckMode.Automatic &&
                string.Equals(UpdateService.NormalizeVersion(_settings.SkippedUpdateVersion),
                    result.LatestVersion, StringComparison.OrdinalIgnoreCase))
                return new UpdateCheckOutcome(UpdateCheckOutcomeKind.Suppressed, result);
            if (!_presentedVersions.Add(result.LatestVersion))
                return new UpdateCheckOutcome(UpdateCheckOutcomeKind.Suppressed, result);
        }
        return new UpdateCheckOutcome(UpdateCheckOutcomeKind.UpdateAvailable, result);
    }

    private void SetChecking(bool value)
    {
        lock (_gate) _isChecking = value;
        CheckingChanged?.Invoke();
    }
}
