using System.Reflection;
using System.IO;
using System.Net.Http;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CloudLightBlizzard.Models;

namespace CloudLightBlizzard.Services;

public sealed class AnnouncementService : IDisposable, INotifyPropertyChanged
{
    private readonly AppSettings _settings;
    private readonly string _stateFile;
    private readonly string _appVersion;
    private readonly CloudHttpClientFactory _httpClients;
    private readonly bool _ownsHttpClients;
    private readonly Func<CancellationToken, Task<AnnouncementDocument?>>? _downloadOverride;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateSync = new();
    private readonly SynchronizationContext? _notificationContext;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private AnnouncementLocalState _state;
    private DateTimeOffset? _lastCheckAt;

    public AnnouncementService(AppSettings settings, string? stateFile = null, string? appVersion = null,
        CloudHttpClientFactory? httpClients = null)
        : this(settings, stateFile, appVersion, httpClients, null)
    {
    }

    internal AnnouncementService(AppSettings settings, string? stateFile, string? appVersion,
        CloudHttpClientFactory? httpClients,
        Func<CancellationToken, Task<AnnouncementDocument?>>? downloadOverride)
    {
        _settings = settings;
        _ownsHttpClients = httpClients is null;
        _httpClients = httpClients ?? new CloudHttpClientFactory(settings);
        _downloadOverride = downloadOverride;
        _stateFile = stateFile ?? Path.Combine(AppPaths.Current.AnnouncementsDir, "state.json");
        _appVersion = appVersion ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        _state = LoadState();
        _notificationContext = SynchronizationContext.Current;
    }

    public IReadOnlyList<Announcement> CachedAnnouncements
    {
        get { lock (_stateSync) return Filter(_state.Cache); }
    }
    public DateTimeOffset? LastSuccessfulCheck
    {
        get { lock (_stateSync) return _state.LastSuccessfulCheck; }
    }
    public DateTimeOffset? LastCheckAt
    {
        get { lock (_stateSync) return _lastCheckAt; }
    }
    public string? LastFailureMessage { get; private set; }
    public bool HasUnreadAnnouncements => HasUnread(CachedAnnouncements);
    public bool IsBadgeVisible => _settings.ShowAnnouncementBadge && HasUnreadAnnouncements;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task<IReadOnlyList<Announcement>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return CachedAnnouncements;
        try
        {
            lock (_stateSync) _lastCheckAt = DateTimeOffset.Now;
            var downloaded = await DownloadAsync(cancellationToken).ConfigureAwait(false);
            if (IsValid(downloaded))
            {
                lock (_stateSync)
                {
                    _state.Cache = downloaded;
                    _state.LastSuccessfulCheck = DateTimeOffset.Now;
                    SaveState();
                }
                LastFailureMessage = null;
                NotifyAnnouncementStateChanged();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudNetworkException ex)
        {
            LastFailureMessage = CloudHttpClientFactory.UserMessage(ex.Kind, "announcement");
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            LastFailureMessage = "公告服务暂时不可用。";
        }
        finally
        {
            _refreshGate.Release();
        }
        return CachedAnnouncements;
    }

    public bool IsUnread(Announcement item)
    {
        lock (_stateSync)
            return !_state.ReadRevisions.TryGetValue(item.Id, out var revision) || revision < item.Revision;
    }

    public bool HasUnread(IEnumerable<Announcement> items) => items.Any(IsUnread);

    public void MarkRead(Announcement item)
    {
        var changed = false;
        lock (_stateSync)
        {
            if (_state.ReadRevisions.TryGetValue(item.Id, out var revision) && revision >= item.Revision) return;
            _state.ReadRevisions[item.Id] = item.Revision;
            SaveState();
            changed = true;
        }
        if (changed) NotifyAnnouncementStateChanged();
    }

    public void NotifyBadgeSettingChanged() => NotifyAnnouncementStateChanged();

    private void NotifyAnnouncementStateChanged()
    {
        void Raise()
        {
            OnPropertyChanged(nameof(CachedAnnouncements));
            OnPropertyChanged(nameof(HasUnreadAnnouncements));
            OnPropertyChanged(nameof(IsBadgeVisible));
        }

        if (_notificationContext is not null && SynchronizationContext.Current != _notificationContext)
            _notificationContext.Post(_ => Raise(), null);
        else
            Raise();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private async Task<AnnouncementDocument?> DownloadAsync(CancellationToken cancellationToken)
    {
        if (_downloadOverride is not null)
            return await _downloadOverride(cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var endpoint = EndpointFor(_settings);
        using var response = await _httpClients.SendGetAsync(
            () => new HttpRequestMessage(HttpMethod.Get, endpoint), "announcement", timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<AnnouncementDocument>(stream, _json, timeout.Token).ConfigureAwait(false);
    }

    internal static Uri EndpointFor(AppSettings settings) =>
        new(new Uri(CloudServiceConfiguration.NormalizeBaseUrl(settings.CloudServiceBaseUrl)), "v1/announcements");

    internal static async Task RunPeriodicRefreshAsync(Func<CancellationToken, Task> refresh,
        TimeSpan interval, CancellationToken cancellationToken)
    {
        await refresh(cancellationToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            await refresh(cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<Announcement> Filter(AnnouncementDocument? document)
    {
        if (!SemanticVersion.TryParse(_appVersion, out var current) || !IsValid(document)) return Array.Empty<Announcement>();
        return document!.Announcements.Where(item => item.Enabled && InVersionRange(item, current))
            .OrderByDescending(item => item.PublishedAt).ToArray();
    }

    private static bool InVersionRange(Announcement item, SemanticVersion current)
    {
        if (!string.IsNullOrWhiteSpace(item.MinVersion) &&
            (!SemanticVersion.TryParse(item.MinVersion, out var min) || current.CompareTo(min) < 0)) return false;
        if (!string.IsNullOrWhiteSpace(item.MaxVersion) &&
            (!SemanticVersion.TryParse(item.MaxVersion, out var max) || current.CompareTo(max) > 0)) return false;
        return true;
    }

    private static bool IsValid(AnnouncementDocument? document) => document is { SchemaVersion: 1 } &&
        document.Announcements.Count <= 100 && document.Announcements.All(item =>
            item.Revision > 0 && item.Id.Length is > 0 and <= 100 && item.Title.Length is > 0 and <= 200 &&
            item.Content.Length <= 20_000 && item.PublishedAt != default);

    private AnnouncementLocalState LoadState()
    {
        try
        {
            var state = File.Exists(_stateFile)
                ? JsonSerializer.Deserialize<AnnouncementLocalState>(File.ReadAllText(_stateFile), _json)
                : null;
            if (state is null || (state.Cache is not null && !IsValid(state.Cache))) throw new InvalidDataException();
            state.ReadRevisions ??= new(StringComparer.Ordinal);
            return state;
        }
        catch
        {
            try { if (File.Exists(_stateFile)) File.Delete(_stateFile); } catch { }
            return new AnnouncementLocalState();
        }
    }

    private void SaveState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            var temp = _stateFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_state, _json));
            File.Move(temp, _stateFile, overwrite: true);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_ownsHttpClients) _httpClients.Dispose();
    }
}
