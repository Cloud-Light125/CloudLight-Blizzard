using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.OverwatchRegion;
using CloudLightBlizzard.Services.Drops;
using Microsoft.Win32;

namespace CloudLightBlizzard.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        Raise(name);
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AccountRow : ObservableObject
{
    public long AccountId { get; init; }
    public string BattleTag { get; init; } = "";
    public string Environment { get; init; } = "";

    private string _customName = "";
    public string CustomName { get => _customName; set { Set(ref _customName, value); Raise(nameof(DisplayName)); Raise(nameof(CustomNameVisibility)); } }
    private string _remark = "";
    public string Remark { get => _remark; set { Set(ref _remark, value); Raise(nameof(RemarkVisibility)); } }
    private AccountRegionOverride _regionOverride;
    public AccountRegionOverride RegionOverride { get => _regionOverride; set { Set(ref _regionOverride, value); Raise(nameof(IsCnRegion)); Raise(nameof(RegionText)); Raise(nameof(StatsVisibility)); } }

    public string DisplayName => string.IsNullOrWhiteSpace(CustomName) ? BattleTag : CustomName.Trim();
    public Visibility CustomNameVisibility => string.IsNullOrWhiteSpace(CustomName) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RemarkVisibility => string.IsNullOrWhiteSpace(Remark) ? Visibility.Collapsed : Visibility.Visible;
    public bool IsCnRegion => RegionOverride == AccountRegionOverride.China ||
                              (RegionOverride == AccountRegionOverride.Auto && (IsCn(Environment) || string.IsNullOrWhiteSpace(Environment)));
    public static bool IsCn(string? environment)
        => environment?.Contains("battlenet.com.cn", StringComparison.OrdinalIgnoreCase) == true;

    public string RegionText => IsCnRegion ? "国服" : "国际服";
    public Visibility RegionVisibility => RegionText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StatsVisibility => IsCnRegion ? Visibility.Collapsed : Visibility.Visible;

    public static string Region(string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment)) return "";
        if (IsCn(environment)) return "国服";
        return environment.Split('.')[0].ToLowerInvariant() switch
        {
            "kr" => "亚服",
            "us" => "美服",
            "eu" => "欧服",
            "tw" => "台服",
            _ => "国际服",
        };
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { Set(ref _isActive, value); Raise(nameof(CanSwitch)); Raise(nameof(SwitchText)); Raise(nameof(CurrentVisibility)); }
    }

    private bool _hasProfile;
    public bool HasProfile
    {
        get => _hasProfile;
        set { Set(ref _hasProfile, value); Raise(nameof(ProfileText)); Raise(nameof(SavedRelative)); Raise(nameof(CanSwitch)); }
    }

    private DateTime? _savedAtUtc;
    public DateTime? SavedAtUtc
    {
        get => _savedAtUtc;
        set { Set(ref _savedAtUtc, value); Raise(nameof(ProfileText)); Raise(nameof(SavedRelative)); }
    }

    private bool _isExpired;
    public bool IsExpired
    {
        get => _isExpired;
        set { Set(ref _isExpired, value); Raise(nameof(ExpiredVisibility)); Raise(nameof(SwitchText)); }
    }

    public Visibility ExpiredVisibility => _isExpired ? Visibility.Visible : Visibility.Collapsed;
    public string SwitchText => IsActive ? "当前" : (_isExpired ? "重新登录" : "切换");
    public Visibility CurrentVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;

    public string NameOnly { get { var i = BattleTag.IndexOf('#'); return i < 0 ? BattleTag : BattleTag[..i]; } }
    public string HashTag { get { var i = BattleTag.IndexOf('#'); return i < 0 ? "" : BattleTag[i..]; } }
    public string AvatarText => string.IsNullOrEmpty(NameOnly) ? "?" : NameOnly[..1];
    public string AccountIdText => AccountId.ToString();

    private (Brush bg, Brush fg)? _av;
    private (Brush bg, Brush fg) Av => _av ??= Avatar.For(AccountId);
    public Brush AvatarBg => Av.bg;
    public Brush AvatarFg => Av.fg;

    public string ProfileText => HasProfile ? $"已保存 · {SavedAtUtc?.ToLocalTime():MM-dd HH:mm}" : "未保存";

    public string SavedRelative
    {
        get
        {
            if (SavedAtUtc is null) return "未保存";
            var t = SavedAtUtc.Value.ToLocalTime();
            var d = DateTime.Now.Date - t.Date;
            if (d.Days == 0) return $"今天 {t:HH:mm}";
            if (d.Days == 1) return $"昨天 {t:HH:mm}";
            return $"{t:MM-dd HH:mm}";
        }
    }

    public bool CanSwitch => HasProfile && !IsActive;
}

public sealed class MainViewModel : ObservableObject
{
    private readonly BattleNetPaths _paths;
    private readonly AccountReader _reader;
    private readonly AppDataStore _profiles;
    private readonly BattleNetController _controller;
    private readonly AppSettings _settings;
    private OverwatchRegionManager _regionManager;
    private readonly AccountSwitchLog _switchLog;
    private readonly BattleNetAuthLogProbe _authLogProbe;
    private readonly SemaphoreSlim _regionStatusGate = new(1, 1);

    public UpdateCheckCoordinator UpdateChecks { get; }
    public CloudHttpClientFactory CloudHttpClients { get; }
    public UpdateDownloadService UpdateDownloader { get; }
    public FeedbackService FeedbackService { get; }
    public DropsHostService DropsHost { get; } = new();
    public PlatformLogSession DropsLogSession { get; }
    private Func<DropsRuntimeDiagnosticSnapshot>? _dropsDiagnosticSnapshotProvider;
    public event Action<bool, OverwatchRegion, string>? RegionSwitchCompleted;

    public ObservableCollection<AccountRow> Accounts { get; } = new();
    public ObservableCollection<AccountRow> SavedAccounts { get; } = new();
    public ObservableCollection<AccountRow> UnsavedAccounts { get; } = new();

    private AccountRow? _current;
    public AccountRow? CurrentAccount
    {
        get => _current;
        set { Set(ref _current, value); Raise(nameof(HasCurrent)); Raise(nameof(HasCurrentVisibility)); Raise(nameof(NoCurrentVisibility)); }
    }

    public bool HasCurrent => _current != null;
    public Visibility HasCurrentVisibility => _current != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoCurrentVisibility => _current == null ? Visibility.Visible : Visibility.Collapsed;

    private string _readyCountText = "";
    public string ReadyCountText { get => _readyCountText; set => Set(ref _readyCountText, value); }

    private string _unsavedCountText = "";
    public string UnsavedCountText { get => _unsavedCountText; set => Set(ref _unsavedCountText, value); }
    public Visibility UnsavedVisibility => UnsavedAccounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private string _totalCountText = "";
    public string TotalCountText { get => _totalCountText; set => Set(ref _totalCountText, value); }

    private string _statusText = "就绪";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        set
        {
            if (_busy == value) return;
            Set(ref _busy, value);
            Raise(nameof(NotBusy));
            UpdateRegionGuide();
        }
    }
    public bool NotBusy => !_busy;

    private bool _clientRunning;
    public bool ClientRunning
    {
        get => _clientRunning;
        private set { Set(ref _clientRunning, value); Raise(nameof(LaunchText)); }
    }

    public string LaunchText => _clientRunning ? "打开战网窗口" : "启动战网";
    public string AppVersion => "v" + UpdateChecks.CurrentVersion;
    public AppSettings Settings => _settings;
    public bool BattleNetPathValid => !string.IsNullOrWhiteSpace(_paths.ClientExe) && File.Exists(_paths.ClientExe);
    public BattleNetPaths BattleNetDataPaths => _paths;
    public bool OverwatchPathValid => _regionPageStatus.GamePathValid;
    public string RegionBackupRoot => _regionManager.BackupRoot;
    public RegionSnapshotStatus RegionStatusSnapshot => _regionPageStatus;
    public DateTimeOffset? RegionStatusLastCheckedAt => _regionStatusLastCheckedAt;
    internal OverwatchRegionManager RegionManager => _regionManager;
    public bool IsVerifiedDifferenceMode => _settings.RegionBackupMode == RegionBackupMode.VerifiedDifference;
    public bool IsFullSnapshotMode => _settings.RegionBackupMode == RegionBackupMode.FullSnapshot;
    public bool HasPendingRegionPreparation => _regionPageStatus.State == RegionBackupState.Preparing;

    public static string RegionDisplayName(CurrentGameRegion? region) => region switch
    {
        CurrentGameRegion.China => "国服",
        CurrentGameRegion.International => "国际服",
        CurrentGameRegion.Mixed => "混合/未完成",
        _ => "无法确认",
    };

    public static string RegionDisplayName(OverwatchRegion? region) => region switch
    {
        OverwatchRegion.China => "国服",
        OverwatchRegion.International => "国际服",
        _ => "无法确认",
    };

    internal void SetDropsDiagnosticSnapshotProvider(Func<DropsRuntimeDiagnosticSnapshot>? provider) =>
        _dropsDiagnosticSnapshotProvider = provider;

    public DropsRuntimeDiagnosticSnapshot GetDropsDiagnosticSnapshot() =>
        _dropsDiagnosticSnapshotProvider?.Invoke() ?? new DropsRuntimeDiagnosticSnapshot(
            "未初始化", "未初始化", "未初始化", "无", "无", "无", "无");

    internal void RecordSuccessfulUpdate(UpdateCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _settings.LastSuccessfulUpdateFrom = result.CurrentVersion;
        _settings.LastSuccessfulUpdateTo = result.LatestVersion;
        _settings.LastSuccessfulUpdateAt = DateTimeOffset.Now;
        _settings.LastUpdateFailure = null;
        _settings.Save();
        UpdateDownloader.MarkCompleted();
    }

    private string _gameRegionTitle = "当前文件：尚未识别";
    public string GameRegionTitle { get => _gameRegionTitle; set => Set(ref _gameRegionTitle, value); }
    private string _gameRegionFilesText = "国服文件：尚未准备  ·  国际服文件：尚未准备";
    public string GameRegionFilesText { get => _gameRegionFilesText; set => Set(ref _gameRegionFilesText, value); }
    private string _gameRegionSummary = "设置游戏目录后即可准备国服与国际服文件。";
    public string GameRegionSummary { get => _gameRegionSummary; set => Set(ref _gameRegionSummary, value); }
    private string _gameRegionPath = "尚未设置游戏目录";
    public string GameRegionPath { get => _gameRegionPath; set => Set(ref _gameRegionPath, value); }
    private string _regionPrimaryActionText = "开始设置区服文件";
    public string RegionPrimaryActionText { get => _regionPrimaryActionText; set => Set(ref _regionPrimaryActionText, value); }
    private bool _canSwitchChina;
    public bool CanSwitchChina { get => _canSwitchChina; set => Set(ref _canSwitchChina, value); }
    private bool _canSwitchInternational;
    public bool CanSwitchInternational { get => _canSwitchInternational; set => Set(ref _canSwitchInternational, value); }
    private string _switchChinaText = "切换到国服";
    public string SwitchChinaText { get => _switchChinaText; set => Set(ref _switchChinaText, value); }
    private string _switchInternationalText = "切换到国际服";
    public string SwitchInternationalText { get => _switchInternationalText; set => Set(ref _switchInternationalText, value); }
    private Visibility _regionSetupVisibility = Visibility.Visible;
    public Visibility RegionSetupVisibility { get => _regionSetupVisibility; set => Set(ref _regionSetupVisibility, value); }
    private RegionBackupState _homeRegionState;
    private CurrentGameRegion _homeCurrentRegion;
    private RegionSnapshotStatus _regionPageStatus = new();
    private DateTimeOffset? _regionStatusLastCheckedAt;
    private RegionPreparationGuide _regionGuide = new();
    public RegionPreparationGuide RegionGuide { get => _regionGuide; private set => Set(ref _regionGuide, value); }
    private RegionOperationPhase _regionOperationPhase;
    private RegionProgress? _regionOperationProgress;
    private OverwatchRegion? _regionOperationSource;
    private CancellationTokenSource? _regionOperationCancellation;
    private CancellationTokenSource? _switchPlanCancellation;
    private bool _regionRestartRequested;
    private string _regionOperationNotice = "";
    private string _regionOperationError = "";
    private SwitchPlan? _pendingSwitchPlan;
    public SwitchPlan? PendingSwitchPlan
    {
        get => _pendingSwitchPlan;
        private set => Set(ref _pendingSwitchPlan, value);
    }
    public bool IsRegionOperationBusy => _regionOperationPhase != RegionOperationPhase.None;
    private RegionFileCheckResult? _regionFileCheck;
    public RegionFileCheckResult? RegionFileCheck
    {
        get => _regionFileCheck;
        private set
        {
            Set(ref _regionFileCheck, value);
            RaiseRegionFileTools();
        }
    }
    public bool HasRegionFileCheck => RegionFileCheck is not null;
    public bool CanCheckRegionFiles => !Busy && !IsRegionOperationBusy && _regionPageStatus.GamePathValid &&
                                       _regionPageStatus.State is RegionBackupState.Ready or RegionBackupState.Stale;
    public bool CanClearTemporaryFiles => CanCheckRegionFiles && RegionFileCheck?.TemporaryCount > 0;
    public bool CanResetCurrentRegion => CanCheckRegionFiles &&
                                         _regionPageStatus.BackupMode == RegionBackupMode.VerifiedDifference;
    public bool ShowStep4Card => _regionPageStatus.Step4Pending;
    public bool CanRunStep4 => CanCheckRegionFiles && _regionPageStatus.CanRunStep4Now;
    public bool Step4ReminderIgnored => _settings.Step4ReminderIgnored;
    public string Step4RegionText => _regionPageStatus.Step4Region == OverwatchRegion.China ? "国服" : "国际服";
    public string RegionFileCheckSummary => RegionFileCheck is null
        ? "尚未检查。请主动点击检查，结果不会缓存为后续检查。"
        : $"当前检测：{RegionName(RegionFileCheck.Region)}\n" +
          $"永久文件：正常 {RegionFileCheck.NormalCount:N0} · 缺失 {RegionFileCheck.MissingCount:N0}（{FormatBytes(RegionFileCheck.MissingBytes)}） · " +
          $"内容变化 {RegionFileCheck.ChangedCount:N0}（{FormatBytes(RegionFileCheck.ChangedBytes)}）\n" +
          $"应当不存在但仍存在：{RegionFileCheck.ShouldBeAbsentCount:N0}\n" +
          $"临时/额外文件候选：{RegionFileCheck.TemporaryCount:N0}（{FormatBytes(RegionFileCheck.TemporaryBytes)}） · " +
          $"无法读取 {RegionFileCheck.UnreadableCount:N0}" +
          (RegionFileCheck.ReferenceManifestComplete ? "" : "\n当前备份缺少完整文件状态基线；已完整检查现有可用 reference 数据。");
    public string RegionFileCheckDetails => RegionFileCheck is null ? "" : string.Join("\n\n",
        RegionFileCheck.Items.Where(item => item.Kind != RegionFileCheckKind.PermanentNormal).Select(item =>
            $"{CheckKindName(item.Kind)}\n{item.RelativePath}\n" +
            $"原大小：{FormatBytes(item.OriginalSize)} · 当前大小：{FormatBytes(item.CurrentSize)}\n{item.Reason}"));

    public MainViewModel(PlatformLogSession? dropsLogSession = null)
    {
        DropsLogSession = dropsLogSession ?? new PlatformLogSession(AppPaths.Current.LogsDir);
        _settings = AppSettings.Load();
        CloudHttpClients = new CloudHttpClientFactory(_settings);
        UpdateDownloader = new UpdateDownloadService(CloudHttpClients);
        FeedbackService = new FeedbackService(_settings, httpClients: CloudHttpClients);
        DropsHost.ConfigureProxy(new DropsProxySettings(_settings.EnableProxy, _settings.ProxyUrl, _settings.FallbackDirect,
            _settings.BilibiliUseProxy));
        DropsHost.EventReceived += (sender, message) =>
        {
            if (message.Name != "legacy_proxy" || _settings.EnableProxy || !string.IsNullOrWhiteSpace(_settings.ProxyUrl)) return;
            if (!message.Payload.TryGetProperty("url", out var value) ||
                !ProxyValidator.TryNormalize(value.GetString(), out var url, out _)) return;
            _settings.EnableProxy = message.Payload.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
            _settings.ProxyUrl = url;
            _settings.FallbackDirect = message.Payload.TryGetProperty("fallbackDirect", out var fallback) && fallback.GetBoolean();
            _settings.Save();
            DropsHost.ConfigureProxy(new DropsProxySettings(_settings.EnableProxy, _settings.ProxyUrl, _settings.FallbackDirect,
                _settings.BilibiliUseProxy));
        };
        UpdateChecks = new UpdateCheckCoordinator(new UpdateService(_settings, CloudHttpClients), _settings);
        _paths = new BattleNetPaths();
        if (!string.IsNullOrEmpty(_settings.ClientExe) && File.Exists(_settings.ClientExe))
            _paths.ClientExe = _settings.ClientExe;

        _reader = new AccountReader(_paths);
        _profiles = new AppDataStore(_paths);
        _controller = new BattleNetController(_paths);
        _regionManager = new OverwatchRegionManager(_settings.RegionStoragePath);
        _switchLog = new AccountSwitchLog();
        _authLogProbe = new BattleNetAuthLogProbe(_paths);
        _regionPageStatus.GamePath = _settings.OverwatchGamePath ?? "";
        _regionPageStatus.GamePathValid = OverwatchRegionManager.IsValidGameRoot(_settings.OverwatchGamePath);
        UpdateRegionGuide();
    }

    private void RebuildGroups()
    {
        CurrentAccount = Accounts.FirstOrDefault(a => a.IsActive);
        if (CurrentAccount != null && _settings.HiddenAccountIds.Remove(CurrentAccount.AccountId))
            _settings.Save();

        var hidden = new HashSet<long>(_settings.HiddenAccountIds);
        SavedAccounts.Clear();
        foreach (var a in SelectSavedAccounts(Accounts))
            SavedAccounts.Add(a);

        UnsavedAccounts.Clear();
        foreach (var a in Accounts.Where(a => !a.HasProfile && !hidden.Contains(a.AccountId))
                                  .OrderBy(a => a.BattleTag, StringComparer.CurrentCulture))
            UnsavedAccounts.Add(a);

        var total = Accounts.Count(a => a.HasProfile || !hidden.Contains(a.AccountId));
        var saved = Accounts.Count(a => a.HasProfile);
        ReadyCountText = $"{SavedAccounts.Count} 个账号备份";
        UnsavedCountText = $"尚未保存 · {UnsavedAccounts.Count}";
        TotalCountText = $"共 {total} 个 · 已保存 {saved} 个";
        Raise(nameof(UnsavedVisibility));
    }

    public static IReadOnlyList<AccountRow> SelectSavedAccounts(IEnumerable<AccountRow> accounts) =>
        accounts.Where(a => a.HasProfile)
            .OrderByDescending(a => a.SavedAtUtc ?? DateTime.MinValue).ToList();

    public void ApplyAccountLayoutDemo(int count)
    {
        count = Math.Clamp(count, 2, 8);
        SavedAccounts.Clear();
        for (var i = 1; i <= count; i++)
            SavedAccounts.Add(new AccountRow
            {
                AccountId = 900000 + i,
                BattleTag = $"Demo{i}#2200{i}",
                CustomName = i % 2 == 0 ? $"演示账号 {i}" : "",
                Remark = i % 3 == 0 ? "用于检查备注两行以内的卡片布局" : "",
                RegionOverride = i % 2 == 0 ? AccountRegionOverride.International : AccountRegionOverride.China,
                HasProfile = true,
                IsActive = i == 1,
                SavedAtUtc = DateTime.UtcNow.AddMinutes(-i * 17),
            });
        ReadyCountText = $"{count} 个演示账号";
        TotalCountText = $"布局演示 · {count} 个账号";
        StatusText = "账号卡片布局演示，不读取或修改真实账号数据。";
    }

    public void HideAccount(AccountRow row)
    {
        if (row.IsActive) return;
        if (!_settings.HiddenAccountIds.Contains(row.AccountId))
            _settings.HiddenAccountIds.Add(row.AccountId);
        _settings.Save();
        RebuildGroups();
        StatusText = $"已从列表移除「{row.BattleTag}」。它仍在战网里,重新登录该号会再次出现。";
    }

    private string _dbStamp = "";
    private long? _lastActiveId;
    private string _lastIdSet = "";
    private bool _polling;
    private bool _staleNotified;
    private const int SwitchVerifySeconds = 150;
    private long? _pendingSwitchId;
    private DateTime _pendingSwitchUntil;
    private DateTime _pendingSwitchStartedUtc;
    private BattleNetAuthLogCursor _pendingLogCursor = new(new Dictionary<string, long>());
    private bool _pendingClientSeen;
    private long? _lastPendingActiveId;

    private Task<(IReadOnlyList<BattleAccount> list, long? active)> ReadAllAsync() =>
        Task.Run(() =>
        {
            var l = _reader.ReadAccounts(out var act);
            return (l, act);
        });

    private void ApplyAccounts(IReadOnlyList<BattleAccount> accounts, long? activeId)
    {
        Accounts.Clear();
        var seen = new HashSet<long>();
        var envs = accounts.GroupBy(a => a.AccountId).ToDictionary(
            g => g.Key,
            g => g.FirstOrDefault(a => AccountRow.IsCn(a.Environment))?.Environment
                 ?? g.Select(a => a.Environment).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "");

        foreach (var a in accounts)
        {
            if (!seen.Add(a.AccountId)) continue;
            var meta = _profiles.ReadMeta(a.AccountId);
            Accounts.Add(new AccountRow
            {
                AccountId = a.AccountId,
                Environment = envs.TryGetValue(a.AccountId, out var env) ? env : a.Environment,
                BattleTag = string.IsNullOrWhiteSpace(a.BattleTag) ? a.AccountId.ToString() : a.BattleTag,
                IsActive = activeId.HasValue && a.AccountId == activeId.Value,
                HasProfile = meta != null,
                SavedAtUtc = meta?.SavedAtUtc,
                IsExpired = meta?.Expired == true || _settings.ExpiredAccountIds.Contains(a.AccountId),
                CustomName = _settings.PreferenceFor(a.AccountId).CustomName,
                Remark = _settings.PreferenceFor(a.AccountId).Remark,
                RegionOverride = _settings.PreferenceFor(a.AccountId).Region,
            });
        }

        foreach (var meta in _profiles.ReadAllMeta().Where(m => seen.Add(m.AccountId)))
        {
            var pref = _settings.PreferenceFor(meta.AccountId);
            Accounts.Add(new AccountRow
            {
                AccountId = meta.AccountId,
                BattleTag = string.IsNullOrWhiteSpace(meta.BattleTag) ? meta.AccountId.ToString() : meta.BattleTag,
                IsActive = activeId == meta.AccountId,
                HasProfile = true,
                SavedAtUtc = meta.SavedAtUtc,
                IsExpired = meta.Expired || _settings.ExpiredAccountIds.Contains(meta.AccountId),
                CustomName = pref.CustomName,
                Remark = pref.Remark,
                RegionOverride = pref.Region,
            });
        }

        if (activeId is long id && !seen.Contains(id))
        {
            var meta = _profiles.ReadMeta(id);
            Accounts.Add(new AccountRow
            {
                AccountId = id,
                BattleTag = string.IsNullOrWhiteSpace(meta?.BattleTag) ? id.ToString() : meta!.BattleTag,
                IsActive = true,
                HasProfile = meta != null,
                SavedAtUtc = meta?.SavedAtUtc,
                IsExpired = meta?.Expired == true || _settings.ExpiredAccountIds.Contains(id),
                CustomName = _settings.PreferenceFor(id).CustomName,
                Remark = _settings.PreferenceFor(id).Remark,
                RegionOverride = _settings.PreferenceFor(id).Region,
            });
        }

        _lastActiveId = activeId;
        _lastIdSet = string.Join(",", Accounts.Select(r => r.AccountId).OrderBy(x => x));
        RebuildGroups();
    }

    private async Task VerifySwitchAsync(long targetId)
    {
        var active = await Task.Run(() => _reader.ReadActiveAccountId());
        if (active != _lastPendingActiveId)
        {
            _lastPendingActiveId = active;
            _switchLog.Write("ActiveAccountChanged", targetAccountId: targetId,
                detail: active?.ToString() ?? "unknown");
        }
        var evidence = await Task.Run(() => _authLogProbe.ReadAppended(_pendingLogCursor));
        var verification = AccountSwitchVerification.Evaluate(ClientRunning, active, targetId,
            DateTime.UtcNow, _pendingSwitchUntil, evidence);
        if (verification == AccountSwitchVerificationState.WaitingForBattleNet)
        {
            StatusText = "正在等待 Battle.net 启动…";
            return;
        }
        if (!_pendingClientSeen)
        {
            _pendingClientSeen = true;
            _switchLog.Write("WaitingForLogin", targetAccountId: targetId);
        }
        if (verification == AccountSwitchVerificationState.LoggedIn)
        {
            _pendingSwitchId = null;
            var row = Accounts.FirstOrDefault(a => a.AccountId == targetId);
            StatusText = row is { IsExpired: true }
                ? $"「{row.BattleTag}」已经重新登录，建议更新账号备份。"
                : $"已切换到「{row?.BattleTag ?? targetId.ToString()}」。";
            _switchLog.Write("Success", targetAccountId: targetId);
            return;
        }

        if (verification == AccountSwitchVerificationState.LoginRequired)
        {
            _pendingSwitchId = null;
            var expiredTarget = Accounts.FirstOrDefault(a => a.AccountId == targetId);
            if (!_settings.ExpiredAccountIds.Contains(targetId))
            {
                _settings.ExpiredAccountIds.Add(targetId);
                _settings.Save();
            }
            if (expiredTarget is not null) expiredTarget.IsExpired = true;
            RebuildGroups();
            StatusText = $"「{expiredTarget?.BattleTag ?? targetId.ToString()}」需要重新登录 Battle.net。";
            _switchLog.Write("LoginRequired", targetAccountId: targetId,
                detail: "Battle.net log contains explicit session-expired evidence");
            return;
        }

        if (verification == AccountSwitchVerificationState.WaitingForLogin)
        {
            StatusText = evidence == BattleNetLoginEvidence.LoginPage
                ? "Battle.net 已打开登录页面，正在等待明确的登录结果…"
                : "正在等待 Battle.net 完成登录…";
            return;
        }

        _pendingSwitchId = null;
        var target = Accounts.FirstOrDefault(a => a.AccountId == targetId);
        StatusText = $"暂时没有确认「{target?.BattleTag ?? targetId.ToString()}」的登录结果。可以继续等待或打开 Battle.net 查看。";
        _switchLog.Write("Unconfirmed", targetAccountId: targetId,
            detail: "No expiry flag written; active account was not confirmed before timeout");
    }

    private void StampDb()
    {
        try
        {
            var fi = new FileInfo(_paths.CachedDataDb);
            _dbStamp = fi.Exists ? fi.LastWriteTimeUtc.Ticks + ":" + fi.Length : "";
        }
        catch { _dbStamp = ""; }
    }

    public async Task PollAccountsAsync()
    {
        ClientRunning = await Task.Run(() => _controller.IsClientRunning());
        if (_pendingSwitchId is long pending && !Busy)
            await VerifySwitchAsync(pending);
        if (_polling || Busy || !_paths.Exists) return;
        _polling = true;
        try
        {
            string stamp;
            try
            {
                var fi = new FileInfo(_paths.CachedDataDb);
                if (!fi.Exists) return;
                stamp = fi.LastWriteTimeUtc.Ticks + ":" + fi.Length;
            }
            catch { return; }

            if (stamp == _dbStamp) return;
            _dbStamp = stamp;
            var (list, activeId) = await ReadAllAsync();
            if (Busy) { _dbStamp = ""; return; }

            var idSet = string.Join(",", list.Select(a => a.AccountId)
                                             .Concat(activeId.HasValue ? new[] { activeId.Value } : Array.Empty<long>())
                                             .Distinct().OrderBy(x => x));
            if (activeId == _lastActiveId && idSet == _lastIdSet) return;

            var knownBefore = new HashSet<long>(Accounts.Select(r => r.AccountId));
            ApplyAccounts(list, activeId);
            if (CurrentAccount is { IsExpired: true } exp)
                StatusText = $"「{exp.BattleTag}」已经重新登录，建议更新账号备份。";
            else if (CurrentAccount is { } cur && !knownBefore.Contains(cur.AccountId))
                StatusText = $"检测到新登录的账号「{cur.BattleTag}」，可以保存为账号备份。";
            else if (CurrentAccount is { } c2)
                StatusText = $"当前登录账号已变为「{c2.BattleTag}」。";
        }
        catch { }
        finally { _polling = false; }
    }

    public async Task RefreshAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            StatusText = "读取账号列表…";
            if (!_paths.Exists)
            {
                Accounts.Clear();
                RebuildGroups();
                StatusText = "未找到战网数据目录。请确认战网已安装并至少登录过一次。";
                return;
            }

            StampDb();
            var (accounts, activeId) = await ReadAllAsync();
            ApplyAccounts(accounts, activeId);
            var hidden = new HashSet<long>(_settings.HiddenAccountIds);
            var visibleTotal = Accounts.Count(r => r.HasProfile || !hidden.Contains(r.AccountId));
            var saved = Accounts.Count(r => r.HasProfile);
            if (Accounts.Count == 0)
                StatusText = "没读到账号。请先登录一次战网再回来刷新。";
            else if (_paths.ClientExe is null)
                StatusText = "⚠ 未找到 Battle.net.exe,请到设置里指定路径。";
            else if (saved == 0)
                StatusText = "还没有保存账号。请先在 Battle.net 登录，然后保存当前账号。";
            else
                StatusText = $"共 {visibleTotal} 个账号，已保存 {saved} 个账号备份。";

            if (string.IsNullOrWhiteSpace(_settings.OverwatchGamePath))
            {
                _settings.OverwatchGamePath = await Task.Run(() => OverwatchGameLocator.Detect(_paths));
                if (!string.IsNullOrWhiteSpace(_settings.OverwatchGamePath)) _settings.Save();
            }

            // Startup must not let a remembered region mask an updated game generation.
            var regionStatus = await RefreshHomeRegionAsync(verifyFiles: true);
            if (!_staleNotified && regionStatus?.State == RegionBackupState.Stale)
            {
                _staleNotified = true;
                StatusText = "检测到守望先锋文件可能已经更新；现有区服备份仍可尽可能使用，建议在区服文件页面重设当前状态或重新准备。";
            }
        }
        catch (Exception ex)
        {
            StatusText = "读取失败:" + ex.Message;
        }
        finally { Busy = false; }
    }

    public async Task LaunchClientAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            if (await Task.Run(() => _controller.TryFocusClient()))
            {
                StatusText = "战网已在运行,已唤到前台。";
                return;
            }

            StatusText = "正在启动战网…";
            await Task.Run(() => _controller.LaunchClient());
            ClientRunning = true;
            StatusText = CurrentAccount is { } cur
                ? $"战网启动中,稍等几秒会自动登录「{cur.BattleTag}」。"
                : "战网启动中。";
        }
        catch (Exception ex)
        {
            StatusText = "启动失败:" + ex.Message;
            MessageBox.Show(ex.Message, "启动战网失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Busy = false; }
    }

    public async Task SaveCurrentAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            StampDb();
            var (list, activeId) = await ReadAllAsync();
            ApplyAccounts(list, activeId);
            var active = activeId is null ? null : Accounts.FirstOrDefault(a => a.AccountId == activeId.Value);
            if (active is null)
            {
                MessageBox.Show("没有检测到当前登录的账号。\n请先在战网里登录一个账号并确认进入,再回来保存。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            StatusText = "正在关闭战网以保存账号文件…";
            var stopped = await Task.Run(() => _controller.GracefulQuit());
            if (!stopped)
                throw new InvalidOperationException("战网未能完全退出,已中止保存。请从托盘右键『退出』战网后重试。");

            StatusText = $"正在更新「{active.BattleTag}」的账号备份…";
            await Task.Run(() => _profiles.Save(active.AccountId, active.BattleTag));
            if (_settings.ExpiredAccountIds.Remove(active.AccountId)) _settings.Save();
            active.HasProfile = true;
            active.SavedAtUtc = DateTime.UtcNow;
            active.IsExpired = false;
            RebuildGroups();

            StatusText = "正在重新启动战网…";
            await Task.Run(() => _controller.LaunchClient());
            StatusText = $"已更新「{active.BattleTag}」的账号备份，Battle.net 正在重启。";
        }
        catch (Exception ex)
        {
            StatusText = "保存失败:" + ex.Message;
            MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Busy = false; }
    }

    public async Task SwitchToAsync(AccountRow target)
    {
        if (Busy || !target.HasProfile) return;
        var currentId = await Task.Run(() => _reader.ReadActiveAccountId());
        if (currentId == target.AccountId)
        {
            foreach (var a in Accounts) a.IsActive = a.AccountId == target.AccountId;
            RebuildGroups();
            StatusText = $"「{target.BattleTag}」已经是当前登录账号。";
            return;
        }

        if (OverwatchRegionManager.IsGameRunning())
        {
            MessageBox.Show("守望先锋正在运行，请先退出游戏后再切换账号。",
                "无法切换账号", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var targetRegion = target.IsCnRegion ? OverwatchRegion.China : OverwatchRegion.International;
        var shouldSwitchRegion = false;
        var regionSkipReason = "";
        try
        {
            var regionStatus = await _regionManager.GetStatusAsync(_settings.OverwatchGamePath);
            var canNormalize = regionStatus.SwitchEligibility is
                RegionSwitchEligibility.Normal or RegionSwitchEligibility.BestEffort;

            if (regionStatus.GamePathValid && canNormalize)
            {
                shouldSwitchRegion = true;
            }
            else
            {
                regionSkipReason = regionStatus.SwitchEligibility == RegionSwitchEligibility.GameUpdated
                    ? "游戏已更新，现有区服备份基于旧版本。"
                    : !regionStatus.GamePathValid
                        ? "未找到有效的守望先锋游戏目录。"
                        : "区服备份缺失、损坏或当前不可安全使用。";
            }
        }
        catch (Exception ex)
        {
            // 区服文件预检失败发生在任何游戏文件修改之前，因此只降级为账号切换。
            regionSkipReason = "无法检查本地区服文件：" + ex.Message;
        }

        var runStep4AfterSwitch = shouldSwitchRegion && PromptForStep4(targetRegion);
        Busy = true;
        var stage = "关闭 Battle.net";
        string? regionFileWarning = null;
        var regionNormalizeSucceeded = false;
        _switchLog.Write("SwitchStarted", currentId, target.AccountId,
            Accounts.FirstOrDefault(a => a.AccountId == currentId)?.RegionText, target.RegionText);
        _pendingLogCursor = await Task.Run(() => _authLogProbe.CaptureCursor());
        try
        {
            await AccountSwitchPipeline.ExecuteAsync(
                async () =>
                {
                    StatusText = "正在关闭 Battle.net…";
                    _switchLog.Write("BattleNetCloseStarted", currentId, target.AccountId);
                    RegionSwitchLog.Write("Battle.net quit begin", targetRegion);
                    var stopped = await Task.Run(() => _controller.GracefulQuit());
                    if (!stopped)
                        throw new InvalidOperationException("Battle.net 未能完全退出，已中止切换。请从托盘右键“退出”后重试。");
                    _switchLog.Write("BattleNetCloseCompleted", currentId, target.AccountId);
                    RegionSwitchLog.Write("Battle.net quit end", targetRegion);
                },
                async () =>
                {
                    if (!shouldSwitchRegion)
                    {
                        StatusText = string.IsNullOrWhiteSpace(regionSkipReason)
                            ? "区服文件当前不可用，本次仅切换 Battle.net 账号。"
                            : $"区服文件未修改：{regionSkipReason} 本次仅切换 Battle.net 账号。";
                        _switchLog.Write("RegionFilesSkipped", currentId, target.AccountId,
                            targetRegion: target.RegionText, detail: regionSkipReason);
                        return;
                    }

                    stage = "区服文件";
                    var progress = new Progress<RegionProgress>(p => StatusText = p.Message);
                    _switchLog.Write("RegionFilesSwitchStarted", currentId, target.AccountId,
                        sourceRegion: "NormalizeCurrentKnownDifferences", targetRegion: target.RegionText);
                    var result = await _regionManager.NormalizeToRegionAsync(
                        _settings.OverwatchGamePath!, targetRegion, progress);
                    if (result.Outcome == RegionSwitchOutcome.Failed)
                        throw new InvalidDataException($"区服文件切换失败，{result.FailedCount:N0} 个文件无法处理。" +
                                                       FormatRegionFileIssues(result.Issues));
                    if (result.Outcome == RegionSwitchOutcome.PartialSuccess)
                        regionFileWarning = $"{result.FailedCount:N0} 个区服文件存在异常，已跳过；其他文件已继续处理。";
                    regionNormalizeSucceeded = result.Outcome == RegionSwitchOutcome.Success;
                    _switchLog.Write("RegionFilesSwitchCompleted", currentId, target.AccountId,
                        targetRegion: target.RegionText,
                        detail: $"outcome={result.Outcome};restored={result.Restored};deleted={result.Deleted};" +
                                $"failed={result.FailedCount};verified={result.Verified}");
                },
                async () =>
                {
                    stage = "目标账号恢复";
                    StatusText = $"正在准备「{target.BattleTag}」的账号…";
                    _switchLog.Write("TargetRestoreStarted", currentId, target.AccountId);
                    await Task.Run(() => _profiles.Restore(target.AccountId));
                    _switchLog.Write("TargetRestoreCompleted", currentId, target.AccountId);
                },
                async () =>
                {
                    stage = "Battle.net 启动";
                    StatusText = "正在启动 Battle.net…";
                    await Task.Run(() => _controller.LaunchClient());
                    _switchLog.Write("BattleNetStarted", currentId, target.AccountId);
                    RegionSwitchLog.Write("Battle.net restart", targetRegion);
                });

            if (runStep4AfterSwitch && regionNormalizeSucceeded)
            {
                try
                {
                    stage = "可选的第四步验证";
                    var result = await _regionManager.VerifyFourthStepAsync(_settings.OverwatchGamePath!,
                        new Progress<RegionProgress>(p => StatusText = p.Message));
                    regionFileWarning = $"第四步验证完成：确认 {result.DoubleVerified:N0} 个，排除 {result.Rejected:N0} 个，" +
                                        $"{result.Unverified:N0} 个本次无法验证。";
                }
                catch (Exception ex)
                {
                    // 账号、区服 Normalize 和 Battle.net 启动均已完成；第四步绝不能回滚或改判账号切换。
                    regionFileWarning = "账号与区服切换已完成；可选的第四步验证未完成，原备份未受影响：" + ex.Message;
                    _switchLog.Write("OptionalStep4Failed", currentId, target.AccountId,
                        targetRegion: target.RegionText, detail: ex.ToString());
                }
                finally { stage = "Battle.net 启动"; }
            }

            foreach (var a in Accounts) a.IsActive = a.AccountId == target.AccountId;
            _lastActiveId = target.AccountId;
            RebuildGroups();
            _pendingSwitchId = target.AccountId;
            _pendingSwitchUntil = DateTime.UtcNow.AddSeconds(SwitchVerifySeconds);
            _pendingSwitchStartedUtc = DateTime.UtcNow;
            _pendingClientSeen = false;
            _lastPendingActiveId = currentId;
            StatusText = regionFileWarning is not null
                ? $"已切换到「{target.BattleTag}」；{regionFileWarning} 战网正在启动，正在确认登录结果…"
                : shouldSwitchRegion
                ? $"已切换到「{target.BattleTag}」,战网正在启动,正在确认登录结果…"
                : $"已切换到「{target.BattleTag}」；本次未修改守望先锋国服/国际服文件，正在确认登录结果…";
        }
        catch (Exception ex)
        {
            _switchLog.Write(stage.Contains("区服") ? "RegionFileError" :
                stage.Contains("启动") ? "BattleNetStartError" : "SnapshotError",
                currentId, target.AccountId, targetRegion: target.RegionText, detail: ex.Message);
            StatusText = $"{stage}错误：{ex.Message}";
            MessageBox.Show(ex.Message, stage + "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Busy = false; }
    }

    public async Task ReloginAsync(AccountRow row)
    {
        if (Busy) return;
        Busy = true;
        try
        {
            StatusText = "正在关闭战网…";
            var stopped = await Task.Run(() => _controller.GracefulQuit());
            if (!stopped)
                throw new InvalidOperationException("战网未能完全退出,请从托盘右键『退出』战网后重试。");

            StatusText = "正在回到登录页(未登出)…";
            await Task.Run(() => _profiles.ClearCurrentPointer());
            await Task.Run(() => _controller.LaunchClient());
            foreach (var a in Accounts) a.IsActive = false;
            _lastActiveId = null;
            _dbStamp = "";
            _pendingSwitchId = null;
            RebuildGroups();
            StatusText = $"已回到登录页。请在 Battle.net 里登录「{row.BattleTag}」，登录成功后点击『更新账号备份』。";
        }
        catch (Exception ex)
        {
            StatusText = "出错:" + ex.Message;
            MessageBox.Show(ex.Message, "重新登录失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Busy = false; }
    }

    public async Task AddAccountAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            StatusText = "正在关闭战网…";
            var stopped = await Task.Run(() => _controller.GracefulQuit());
            if (!stopped)
                throw new InvalidOperationException("战网未能完全退出,请从托盘右键『退出』战网后重试。");

            StatusText = "正在回到登录页(未登出)…";
            await Task.Run(() => _profiles.ClearCurrentPointer());
            await Task.Run(() => _controller.LaunchClient());
            foreach (var a in Accounts) a.IsActive = false;
            _lastActiveId = null;
            _dbStamp = "";
            RebuildGroups();
            StatusText = "已回到登录页。请在战网里登录新账号(换区也行),登录成功后本工具会自动识别。";
        }
        catch (Exception ex)
        {
            StatusText = "出错:" + ex.Message;
            MessageBox.Show(ex.Message, "登录新号失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Busy = false; }
    }

    public async Task DeleteProfileAsync(AccountRow row)
    {
        if (Busy || !row.HasProfile) return;
        Busy = true;
        try
        {
            await Task.Run(() => _profiles.Delete(row.AccountId));
            if (_settings.ExpiredAccountIds.Remove(row.AccountId)) _settings.Save();
            row.HasProfile = false;
            row.SavedAtUtc = null;
            row.IsExpired = false;
            RebuildGroups();
            StatusText = $"已删除「{row.BattleTag}」的账号备份。";
        }
        catch (Exception ex)
        {
            StatusText = "删除失败:" + ex.Message;
        }
        finally { Busy = false; }
    }

    public void SaveAccountPreference(AccountRow row, string customName, string remark, AccountRegionOverride region)
    {
        var pref = _settings.PreferenceFor(row.AccountId);
        pref.CustomName = customName.Trim();
        pref.Remark = remark.Trim();
        pref.Region = region;
        _settings.Save();
        row.CustomName = pref.CustomName;
        row.Remark = pref.Remark;
        row.RegionOverride = pref.Region;
        RebuildGroups();
        StatusText = $"已保存「{row.DisplayName}」的账号设置。";
    }

    public void SetExePath()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择 Battle.net.exe",
            Filter = "Battle.net.exe|Battle.net.exe|可执行文件 (*.exe)|*.exe",
            FileName = "Battle.net.exe",
        };
        if (!string.IsNullOrEmpty(_paths.ClientExe))
            dlg.InitialDirectory = Path.GetDirectoryName(_paths.ClientExe);

        if (dlg.ShowDialog() == true)
        {
            _paths.ClientExe = dlg.FileName;
            _settings.ClientExe = dlg.FileName;
            _settings.Save();
            StatusText = "已设置 Battle.net.exe 路径:" + dlg.FileName;
        }
    }

    public void SetOverwatchGamePath()
    {
        if (!RegionGuide.CanChangePaths) return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择《守望先锋》安装根目录（包含 Overwatch.exe）",
            UseDescriptionForTitle = true,
            InitialDirectory = _settings.OverwatchGamePath ?? "",
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        if (!OverwatchRegionManager.IsValidGameRoot(dialog.SelectedPath))
        {
            MessageBox.Show("所选目录中未找到 Overwatch.exe 或 _retail_\\Overwatch.exe。",
                "目录无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.OverwatchGamePath = Path.GetFullPath(dialog.SelectedPath);
        _settings.Save();
        _regionOperationError = "";
        _regionOperationNotice = "";
        StatusText = "已设置守望先锋游戏目录：" + _settings.OverwatchGamePath;
        _ = RefreshHomeRegionAsync();
    }

    public bool AutoDetectOverwatchGamePath()
    {
        if (!RegionGuide.CanChangePaths) return false;
        var path = OverwatchGameLocator.Detect(_paths);
        if (string.IsNullOrWhiteSpace(path)) return false;
        _settings.OverwatchGamePath = path;
        _settings.Save();
        _regionOperationError = "";
        _regionOperationNotice = "";
        _ = RefreshHomeRegionAsync();
        return true;
    }

    private void UpdateRegionGuide()
    {
        RegionGuide = RegionPreparationGuide.Create(
            _regionPageStatus,
            _regionOperationPhase,
            _regionRestartRequested,
            Busy,
            _regionOperationProgress,
            _regionManager.BackupRoot,
            _regionOperationNotice,
            _regionOperationError,
            _regionOperationSource,
            _settings.RegionBackupMode);
        Raise(nameof(IsRegionOperationBusy));
        RaiseRegionFileTools();
    }

    private void RaiseRegionFileTools()
    {
        Raise(nameof(HasRegionFileCheck));
        Raise(nameof(CanCheckRegionFiles));
        Raise(nameof(CanClearTemporaryFiles));
        Raise(nameof(CanResetCurrentRegion));
        Raise(nameof(ShowStep4Card));
        Raise(nameof(CanRunStep4));
        Raise(nameof(Step4ReminderIgnored));
        Raise(nameof(Step4RegionText));
        Raise(nameof(RegionFileCheckSummary));
        Raise(nameof(RegionFileCheckDetails));
    }

    private void SetRegionOperation(RegionOperationPhase phase, RegionProgress? progress = null)
    {
        _regionOperationPhase = phase;
        _regionOperationProgress = progress;
        UpdateRegionGuide();
    }

    private void UpdateRegionProgress(RegionProgress progress)
    {
        _regionOperationProgress = progress;
        UpdateRegionGuide();
    }

    public async Task RefreshRegionPageAsync()
    {
        var status = await RefreshHomeRegionAsync(verifyFiles: false);
        if (status?.State is RegionBackupState.Ready or RegionBackupState.Stale)
            await RefreshHomeRegionAsync(verifyFiles: true);
    }

    public async Task<RegionSnapshotStatus> GetRegionStatusAsync(bool verifyFiles = false,
        bool verifyBackupHashes = false, bool persistStateChanges = true)
    {
        await _regionStatusGate.WaitAsync();
        try
        {
            return await Task.Run(() => _regionManager.GetStatusAsync(
                _settings.OverwatchGamePath, verifyFiles: verifyFiles,
                verifyBackupHashes: verifyBackupHashes, persistStateChanges: persistStateChanges));
        }
        finally
        {
            _regionStatusGate.Release();
        }
    }

    public async Task<RegionSnapshotStatus?> RefreshHomeRegionAsync(bool verifyFiles = false)
    {
        try
        {
            var status = await GetRegionStatusAsync(verifyFiles);
            _regionPageStatus = status;
            _regionStatusLastCheckedAt = DateTimeOffset.Now;
            UpdateRegionGuide();
            _homeRegionState = status.State;
            _homeCurrentRegion = status.CurrentRegion;
            GameRegionTitle = status.CurrentRegion switch
            {
                CurrentGameRegion.China => "当前文件：国服",
                CurrentGameRegion.International => "当前文件：国际服",
                CurrentGameRegion.Mixed => "当前文件：正在切换 / 状态不完整",
                _ => "当前文件：无法确认",
            };
            GameRegionFilesText = $"国服文件：{(status.ChinaBackupComplete ? "已准备" : status.ChinaCaptured ? "已保存在本地" : "尚未准备")}  ·  " +
                                  $"国际服文件：{(status.InternationalBackupComplete ? "已准备" : status.InternationalCaptured ? "已保存在本地" : "尚未准备")}";
            GameRegionPath = string.IsNullOrWhiteSpace(status.GamePath) ? "尚未设置游戏目录" : status.GamePath;
            GameRegionSummary = status.State switch
            {
                RegionBackupState.Empty => "首次设置只需要让 Battle.net 完成一次跨区更新。",
                RegionBackupState.Preparing => $"{RegionName(status.PendingSourceRegion)}文件已经保存在本地。请在 Battle.net 中切换到{RegionName(status.PendingTargetRegion)}并等待更新完成，然后回来继续。",
                RegionBackupState.Ready when status.SwitchEligibility == RegionSwitchEligibility.BestEffort =>
                    "当前版本无法确认，但区服备份仍可尽可能使用。建议重设当前区服状态或重新准备。",
                RegionBackupState.Ready when status.SwitchEligibility == RegionSwitchEligibility.BackupUnavailable =>
                    "区服备份文件缺失或损坏，当前不能切换。请检查备份或重新准备。",
                RegionBackupState.Ready when status.CurrentRegion == CurrentGameRegion.Mixed =>
                    $"当前游戏文件处于未完成的区服切换状态，可以直接使用本地备份恢复到国服或国际服。已保存 {status.DifferenceCount} 个差异文件 · {FormatBytes(status.BackupBytes)}",
                RegionBackupState.Ready => $"已保存 {status.DifferenceCount} 个区服差异文件 · {FormatBytes(status.BackupBytes)}",
                RegionBackupState.Stale => "检测到游戏文件可能已经更新；现有备份仍可尽可能切换，建议重设当前区服状态或重新准备。",
                RegionBackupState.Legacy => "区服文件功能已经升级，需要重新准备一次本地文件。",
                _ => "本地文件不完整，请重新准备。",
            };
            RegionPrimaryActionText = status.State switch
            {
                RegionBackupState.Empty => "开始准备区服文件",
                RegionBackupState.Preparing => $"我已经切换到{RegionName(status.PendingTargetRegion)}",
                RegionBackupState.Ready => status.CurrentRegion == CurrentGameRegion.China ? "切换到国际服" : "切换到国服",
                _ => "重新准备区服文件",
            };
            var switchEligible = status.SwitchEligibility is RegionSwitchEligibility.Normal or RegionSwitchEligibility.BestEffort;
            CanSwitchChina = status.GamePathValid && (status.State is RegionBackupState.Ready or RegionBackupState.Stale) && switchEligible &&
                             (status.CurrentRegion != CurrentGameRegion.China || !status.ExactSnapshotMatch);
            CanSwitchInternational = status.GamePathValid && (status.State is RegionBackupState.Ready or RegionBackupState.Stale) && switchEligible &&
                                     (status.CurrentRegion != CurrentGameRegion.International || !status.ExactSnapshotMatch);
            SwitchChinaText = status.CurrentRegion is CurrentGameRegion.Mixed or CurrentGameRegion.Unknown ||
                              status.CurrentRegion == CurrentGameRegion.China && !status.ExactSnapshotMatch ? "恢复到国服" :
                status.CurrentRegion == CurrentGameRegion.China ? "当前为国服" : "切换到国服";
            SwitchInternationalText = status.CurrentRegion is CurrentGameRegion.Mixed or CurrentGameRegion.Unknown ||
                                      status.CurrentRegion == CurrentGameRegion.International && !status.ExactSnapshotMatch ? "恢复到国际服" :
                status.CurrentRegion == CurrentGameRegion.International ? "当前为国际服" : "切换到国际服";
            RegionSetupVisibility = status.State is RegionBackupState.Ready or RegionBackupState.Stale ? Visibility.Collapsed : Visibility.Visible;
            return status;
        }
        catch
        {
            GameRegionSummary = "暂时无法读取区服文件状态。";
            if (_regionOperationPhase == RegionOperationPhase.None)
            {
                _regionOperationError = "暂时无法读取区服文件状态，请检查游戏位置和备份位置后重试。";
                UpdateRegionGuide();
            }
            return null;
        }
    }

    public async Task<SwitchPlan?> CreateRegionSwitchPlanAsync(OverwatchRegion target)
    {
        if (Busy || IsRegionOperationBusy || _switchPlanCancellation is not null || !RegionGuide.CanRestore &&
            (target == OverwatchRegion.China ? !RegionGuide.CanSwitchChina : !RegionGuide.CanSwitchInternational))
            return null;
        var cts = new CancellationTokenSource();
        _switchPlanCancellation = cts;
        try
        {
            StatusText = $"正在生成{RegionName(target)}切换预览…";
            var plan = await Task.Run(() => _regionManager.CreateSwitchPlanAsync(
                _settings.OverwatchGamePath!, target, cts.Token), cts.Token);
            PendingSwitchPlan = plan;
            StatusText = plan.CanExecute ? $"已生成{RegionName(target)}切换预览。" : "切换预览发现安全阻断条件。";
            return plan;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            StatusText = "切换预览已取消。";
            return null;
        }
        catch (Exception ex)
        {
            StatusText = "无法生成切换预览，请检查区服快照和游戏目录。";
            _regionOperationError = ex.Message;
            return null;
        }
        finally
        {
            if (ReferenceEquals(_switchPlanCancellation, cts)) _switchPlanCancellation = null;
            cts.Dispose();
        }
    }

    public void CancelSwitchPlan() => _switchPlanCancellation?.Cancel();

    public async Task SwitchGameRegionOnlyAsync(OverwatchRegion target, SwitchPlan? suppliedPlan = null)
    {
        if (Busy || IsRegionOperationBusy || _switchPlanCancellation is not null ||
            (suppliedPlan is null && !RegionGuide.CanRestore &&
             ((target == OverwatchRegion.China && !RegionGuide.CanSwitchChina) ||
              (target == OverwatchRegion.International && !RegionGuide.CanSwitchInternational)))) return;
        var plan = suppliedPlan ?? (PendingSwitchPlan?.TargetRegion == target ? PendingSwitchPlan : null);
        if (plan is not null && !plan.CanExecute)
        {
            StatusText = "切换已被安全检查阻止：" + string.Join("；", plan.Blockers);
            return;
        }
        var runStep4 = PromptForStep4(target);
        _regionOperationNotice = "";
        _regionOperationError = "";
        SetRegionOperation(RegionOperationPhase.SwitchingRegion,
            new RegionProgress($"正在恢复到{RegionName(target)}…"));
        Busy = true;
        var restartClient = false;
        try
        {
            if (OverwatchRegionManager.IsGameRunning())
                throw new InvalidOperationException("守望先锋正在运行，请先退出游戏后再切换区服文件。");
            restartClient = await Task.Run(() => _controller.IsClientRunning());
            if (plan is not null && restartClient != plan.BattleNetRunning)
                throw new InvalidDataException("预览后 Battle.net 状态已变化，请重新生成切换预览。");
            if (restartClient)
            {
                StatusText = "正在正常关闭 Battle.net…";
                RegionSwitchLog.Write("Battle.net quit begin", target);
                if (!await Task.Run(() => _controller.GracefulQuit()))
                    throw new InvalidOperationException("Battle.net 未能完全退出，请从托盘退出后重试。");
                RegionSwitchLog.Write("Battle.net quit end", target);
            }
            StatusText = "正在切换区服文件…";
            var progress = new Progress<RegionProgress>(UpdateRegionProgress);
            var result = plan is null
                ? await _regionManager.NormalizeToRegionAsync(_settings.OverwatchGamePath!, target, progress)
                : await _regionManager.ExecuteSwitchPlanAsync(_settings.OverwatchGamePath!, plan, progress);
            if (result.Outcome == RegionSwitchOutcome.Failed)
                throw new InvalidDataException($"未能处理任何区服差异文件；{result.FailedCount:N0} 个文件存在异常。" +
                                               FormatRegionFileIssues(result.Issues));
            if (restartClient)
            {
                StatusText = "正在重新启动 Battle.net…";
                await Task.Run(() => _controller.LaunchClient());
                RegionSwitchLog.Write("Battle.net restart", target);
                restartClient = false;
            }
            StatusText = result.Outcome == RegionSwitchOutcome.PartialSuccess
                ? $"区服文件已部分切换到{RegionName(target)}；{result.FailedCount:N0} 个异常文件已跳过。"
                : $"守望先锋区服文件已切换到{RegionName(target)}。";
            RegionSwitchCompleted?.Invoke(true, target, StatusText);
            if (result.Outcome == RegionSwitchOutcome.PartialSuccess)
                MessageBox.Show($"{result.FailedCount:N0} 个文件存在异常，已自动跳过。\n其他区服差异文件已继续处理。" +
                                FormatRegionFileIssues(result.Issues), "区服文件部分完成",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            if (runStep4 && result.Outcome == RegionSwitchOutcome.Success)
            {
                try
                {
                    var step4 = await _regionManager.VerifyFourthStepAsync(
                        _settings.OverwatchGamePath!, progress);
                    MessageBox.Show($"第四步验证完成。\n\n已再次确认稳定区服差异：{step4.DoubleVerified:N0} 个\n" +
                                    $"进一步排除非稳定文件：{step4.Rejected:N0} 个\n本次无法验证：{step4.Unverified:N0} 个\n\n" +
                                    "被确认不稳定的文件将不再参与区服切换，但不会自动删除游戏目录中的文件。",
                        "第四步验证完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("区服切换已经完成，但可选的第四步验证未完成。当前可用备份没有受到影响。\n\n" + ex.Message,
                        "第四步验证未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = "切换区服文件失败：" + ex.Message;
            RegionSwitchCompleted?.Invoke(false, target, ex.Message);
            MessageBox.Show(ex.Message, "无法切换区服文件", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (ReferenceEquals(PendingSwitchPlan, plan)) PendingSwitchPlan = null;
            Busy = false;
            SetRegionOperation(RegionOperationPhase.None);
            await RefreshHomeRegionAsync();
        }
    }

    private bool PromptForStep4(OverwatchRegion target)
    {
        if (_settings.Step4ReminderIgnored || !_regionManager.ShouldOfferStep4(target)) return false;
        var dialog = new CloudLightBlizzard.Views.Step4ReminderWindow(target)
        {
            Owner = Application.Current?.MainWindow,
        };
        dialog.ShowDialog();
        if (dialog.Choice == CloudLightBlizzard.Views.Step4ReminderChoice.Ignore)
        {
            _settings.Step4ReminderIgnored = true;
            _settings.Save();
            RaiseRegionFileTools();
        }
        return dialog.Choice == CloudLightBlizzard.Views.Step4ReminderChoice.Verify;
    }

    public void RestoreStep4Reminder()
    {
        _settings.Step4ReminderIgnored = false;
        _settings.Save();
        RaiseRegionFileTools();
        StatusText = "已恢复第四步验证提醒。";
    }

    public async Task RunStep4ManuallyAsync()
    {
        if (!CanRunStep4 || string.IsNullOrWhiteSpace(_settings.OverwatchGamePath)) return;
        if (!EnsureGameClosedForRegionPreparation()) return;
        Busy = true;
        SetRegionOperation(RegionOperationPhase.ValidatingBackup,
            new RegionProgress("正在进行可选的第四步验证……"));
        try
        {
            var progress = new Progress<RegionProgress>(UpdateRegionProgress);
            var result = await _regionManager.VerifyFourthStepAsync(_settings.OverwatchGamePath, progress);
            _regionOperationNotice = $"第四步验证完成：再次确认 {result.DoubleVerified:N0} 个，" +
                                     $"排除 {result.Rejected:N0} 个，本次无法验证 {result.Unverified:N0} 个。";
            StatusText = _regionOperationNotice;
        }
        catch (Exception ex)
        {
            _regionOperationError = "第四步验证未完成，当前可用备份未受影响：" + ex.Message;
            MessageBox.Show(_regionOperationError, "第四步验证未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Busy = false;
            SetRegionOperation(RegionOperationPhase.None);
            await RefreshHomeRegionAsync(verifyFiles: true);
        }
    }

    public async Task CheckCurrentRegionFilesAsync()
    {
        if (!CanCheckRegionFiles || string.IsNullOrWhiteSpace(_settings.OverwatchGamePath)) return;
        Busy = true;
        SetRegionOperation(RegionOperationPhase.ValidatingBackup,
            new RegionProgress("正在检查当前区服文件状态……"));
        try
        {
            var progress = new Progress<RegionProgress>(UpdateRegionProgress);
            RegionFileCheck = await _regionManager.CheckCurrentRegionFilesAsync(
                _settings.OverwatchGamePath, progress: progress);
            StatusText = "当前区服文件状态检查完成。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "状态检查未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Busy = false;
            SetRegionOperation(RegionOperationPhase.None);
        }
    }

    public async Task ClearTemporaryFilesAsync()
    {
        if (!CanClearTemporaryFiles || RegionFileCheck is null ||
            string.IsNullOrWhiteSpace(_settings.OverwatchGamePath)) return;
        Busy = true;
        try
        {
            var result = await _regionManager.ClearTemporaryFilesAsync(_settings.OverwatchGamePath,
                RegionFileCheck, new Progress<RegionProgress>(UpdateRegionProgress));
            MessageBox.Show($"已清除 {result.Deleted:N0} 个临时/额外文件，共 {FormatBytes(result.DeletedBytes)}。\n" +
                            $"{result.Skipped:N0} 个文件正在使用或不再满足安全条件，已跳过。",
                "清除完成", MessageBoxButton.OK,
                result.Skipped > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            RegionFileCheck = null;
            StatusText = "临时/额外文件清除完成；如需查看最新状态，请重新检查。";
        }
        catch (Exception ex)
        {
            MessageBox.Show("清除过程中遇到问题，已完成的文件不会回滚；其余文件保持不变。\n\n" + ex.Message,
                "清除未完全完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Busy = false;
            RaiseRegionFileTools();
        }
    }

    public async Task ResetCurrentRegionStateAsync()
    {
        if (!CanResetCurrentRegion || string.IsNullOrWhiteSpace(_settings.OverwatchGamePath)) return;
        if (!EnsureGameClosedForRegionPreparation()) return;
        Busy = true;
        SetRegionOperation(RegionOperationPhase.BuildingBackup,
            new RegionProgress("正在重设当前区服状态……"));
        try
        {
            var result = await _regionManager.ResetCurrentRegionStateAsync(_settings.OverwatchGamePath,
                new Progress<RegionProgress>(UpdateRegionProgress));
            _regionOperationNotice = $"已重设当前{RegionName(result.Region)}状态；更新 {result.Updated:N0} 个，" +
                                     $"{result.Degraded:N0} 个异常项已按方向降级。" +
                                     (result.PotentialDifferences > 0
                                         ? $" 检测到 {result.PotentialDifferences:N0} 个无法安全建立备份的新潜在差异。" : "");
            RegionFileCheck = null;
            StatusText = _regionOperationNotice;
        }
        catch (Exception ex)
        {
            _regionOperationError = "重设当前区服状态失败，原备份未受影响：" + ex.Message;
            MessageBox.Show(_regionOperationError, "重设未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Busy = false;
            SetRegionOperation(RegionOperationPhase.None);
            await RefreshHomeRegionAsync(verifyFiles: true);
        }
    }

    private static string CheckKindName(RegionFileCheckKind kind) => kind switch
    {
        RegionFileCheckKind.PermanentMissing => "永久文件缺失",
        RegionFileCheckKind.PermanentChanged => "永久文件内容变化",
        RegionFileCheckKind.ShouldBeAbsent => "应当不存在但当前存在",
        RegionFileCheckKind.TemporaryCandidate => "临时/额外文件候选",
        RegionFileCheckKind.Unreadable => "无法读取",
        _ => "正常永久文件",
    };

    public static string FormatBytes(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024.0:0.0} KB" : $"{bytes / 1024.0 / 1024.0:0.0} MB";

    public async Task BeginRegionPreparationAsync(OverwatchRegion region)
    {
        if (!RegionGuide.CanChooseCurrentRegion) return;
        if (!EnsureGameClosedForRegionPreparation()) return;
        _regionRestartRequested = false;
        _regionOperationNotice = "";
        _regionOperationError = "";
        _regionPageStatus.PendingSourceRegion = region;
        _regionPageStatus.PendingTargetRegion = region == OverwatchRegion.China
            ? OverwatchRegion.International : OverwatchRegion.China;
        _regionOperationCancellation = new CancellationTokenSource();
        _regionOperationSource = region;
        SetRegionOperation(RegionOperationPhase.PreparingCurrentRegion,
            new RegionProgress($"正在保存当前{RegionName(region)}文件…"));
        Busy = true;
        try
        {
            StatusText = "正在准备区服文件…";
            var progress = new Progress<RegionProgress>(UpdateRegionProgress);
            await _regionManager.CaptureAsync(
                _settings.OverwatchGamePath!, region, progress, _regionOperationCancellation.Token,
                _settings.RegionBackupMode);
            StatusText = "区服文件准备已进入下一步。";
        }
        catch (OperationCanceledException)
        {
            _regionOperationNotice = "已取消本次准备，现有可用区服备份没有改变。";
            StatusText = "已取消区服文件准备。";
        }
        catch (IOException ex) when (GameFilesStillChanging(ex))
        {
            _regionOperationNotice = UpdatingFilesNotice;
            StatusText = "游戏文件仍在更新，请稍后重试。";
        }
        catch (Exception ex)
        {
            _regionOperationError = "当前区服文件未能保存完成。\n\n原因：" + ex.Message;
            StatusText = "区服文件准备未完成。";
        }
        finally
        {
            _regionOperationCancellation.Dispose();
            _regionOperationCancellation = null;
            Busy = false;
            SetRegionOperation(RegionOperationPhase.None);
            _regionOperationSource = null;
            await RefreshHomeRegionAsync(verifyFiles: true);
        }
    }

    public async Task CompleteRegionBackupAsync()
    {
        if (!RegionGuide.CanContinueOtherRegion) return;
        if (!EnsureGameClosedForRegionPreparation()) return;
        var hadActiveGeneration = _regionManager.HasActiveGeneration;
        _regionOperationNotice = "";
        _regionOperationError = "";
        _regionOperationCancellation = new CancellationTokenSource();
        SetRegionOperation(RegionOperationPhase.BuildingBackup,
            new RegionProgress("正在确认 Battle.net 已完成游戏文件更新…"));
        Busy = true;
        try
        {
            StatusText = "正在准备区服文件…";
            var progress = new Progress<RegionProgress>(UpdateRegionProgress);
            var state = await _regionManager.CompleteAsync(
                _settings.OverwatchGamePath!, progress, _regionOperationCancellation.Token);
            if (state == RegionBackupState.Ready && _settings.RegionBackupMode == RegionBackupMode.VerifiedDifference)
            {
                var completed = await _regionManager.GetStatusAsync(_settings.OverwatchGamePath,
                    verifyFiles: false);
                _regionOperationNotice = completed.HasWarnings
                    ? $"智能差异备份准备完成，但部分文件存在异常。\n" +
                      $"已确认区服差异：{completed.DifferenceCount:N0} 个\n" +
                      $"自动忽略非稳定变化：{completed.RejectedCount:N0} 个\n" +
                      $"因文件异常跳过：{completed.SkippedFileCount:N0} 个\n\n" +
                      $"{completed.SkippedFileCount:N0} 个文件可能存在异常，已自动跳过。其他区服差异文件仍可正常使用。"
                    : $"已完成区服差异准备。\n已确认 {completed.DifferenceCount:N0} 个区服差异文件。\n" +
                      $"已自动忽略 {completed.RejectedCount:N0} 个非稳定文件变化。";
            }
            StatusText = state == RegionBackupState.Ready
                ? "区服文件已经准备完成。"
                : "区服文件准备已更新。";
        }
        catch (OperationCanceledException)
        {
            _regionOperationNotice = "已取消本次准备，现有可用区服备份没有改变。";
            StatusText = "已取消区服文件准备。";
        }
        catch (IOException ex) when (GameFilesStillChanging(ex))
        {
            RegionSwitchLog.Write("RegionPreparationFailed", detail: ex.ToString());
            _regionOperationError = (_settings.RegionBackupMode == RegionBackupMode.VerifiedDifference
                                        ? "智能差异备份未能完成。\n你可以重新尝试，或改用完整备份模式。"
                                        : "区服文件准备失败") +
                                    "\n\n原因：" + ex.Message +
                                    (hadActiveGeneration
                                        ? "\n\n现有可用备份没有被替换。"
                                        : "");
            StatusText = "区服文件准备失败：" + ex.Message;
        }
        catch (Exception ex)
        {
            RegionSwitchLog.Write("RegionPreparationFailed", detail: ex.ToString());
            _regionOperationError = (_settings.RegionBackupMode == RegionBackupMode.VerifiedDifference
                                        ? "智能差异备份未能完成。\n你可以重新尝试，或改用完整备份模式。"
                                        : "区服文件准备失败") +
                                    "\n\n原因：" + ex.Message +
                                    (hadActiveGeneration
                                        ? "\n\n现有可用备份没有被替换。"
                                        : "");
            StatusText = "区服文件准备失败：" + ex.Message;
        }
        finally
        {
            _regionOperationCancellation.Dispose();
            _regionOperationCancellation = null;
            Busy = false;
            SetRegionOperation(RegionOperationPhase.None);
            await RefreshHomeRegionAsync(verifyFiles: true);
        }
    }

    public async Task ValidateRegionBackupAsync()
    {
        if (!RegionGuide.CanValidate) return;
        _regionOperationNotice = "";
        _regionOperationError = "";
        SetRegionOperation(RegionOperationPhase.ValidatingBackup,
            new RegionProgress("正在检查区服备份…"));
        Busy = true;
        try
        {
            StatusText = "正在检查区服备份…";
            var status = await GetRegionStatusAsync(verifyFiles: true, verifyBackupHashes: true);
            _regionPageStatus = status;
            _regionStatusLastCheckedAt = DateTimeOffset.Now;
            _regionOperationNotice = status.BackupFileIssueCount > 0
                ? $"检测到 {status.BackupFileIssueCount:N0} 个备份文件缺失、损坏或无法读取。" +
                  "切换时将逐文件跳过，其他完整文件仍可继续使用。"
                : status.State == RegionBackupState.Ready &&
                                      status.ChinaBackupComplete && status.InternationalBackupComplete &&
                                      (status.SwitchEligibility is RegionSwitchEligibility.Normal or RegionSwitchEligibility.BestEffort)
                ? "区服备份完整，可以正常使用。"
                : "部分区服备份文件缺失或损坏，需要重新准备。";
            StatusText = "区服备份检查完成。";
        }
        catch
        {
            _regionOperationNotice = "部分区服备份文件缺失或损坏，需要重新准备。";
            StatusText = "区服备份检查未通过。";
        }
        finally
        {
            Busy = false;
            SetRegionOperation(RegionOperationPhase.None);
            UpdateRegionGuide();
        }
    }

    public void RequestRegionReprepare()
    {
        if (!RegionGuide.CanRestart) return;
        _regionManager.CancelPreparation();
        _regionRestartRequested = true;
        _regionOperationNotice = "";
        _regionOperationError = "";
        UpdateRegionGuide();
    }

    public void ResetRegionBackup()
    {
        if (!RegionGuide.CanClear) return;
        _regionManager.Reset();
        _regionRestartRequested = false;
        _regionOperationNotice = "";
        _regionOperationError = "";
        StatusText = "已清除区服备份。";
        _ = RefreshHomeRegionAsync();
    }

    public void ChangeRegionBackupMode(RegionBackupMode mode)
    {
        if (Busy || IsRegionOperationBusy || _settings.RegionBackupMode == mode) return;
        if (_regionPageStatus.State == RegionBackupState.Preparing)
            _regionManager.CancelPreparation();
        _settings.RegionBackupMode = mode;
        _settings.Save();
        _regionRestartRequested = true;
        _regionOperationNotice = "";
        _regionOperationError = "";
        Raise(nameof(IsVerifiedDifferenceMode));
        Raise(nameof(IsFullSnapshotMode));
        Raise(nameof(HasPendingRegionPreparation));
        UpdateRegionGuide();
        _ = RefreshHomeRegionAsync();
    }

    public async Task RedoVerifiedStep1Async()
    {
        if (!RegionGuide.CanRedoStep1) return;
        if (!EnsureGameClosedForRegionPreparation()) return;
        await RunVerifiedRedoAsync("正在重新记录当前区服文件状态……", RegionOperationPhase.PreparingCurrentRegion,
            (progress, token) => _regionManager.RedoVerifiedStep1Async(
                _settings.OverwatchGamePath!, progress, token));
    }

    public async Task RedoVerifiedStep2Async()
    {
        if (!RegionGuide.CanRedoStep2) return;
        if (!EnsureGameClosedForRegionPreparation()) return;
        await RunVerifiedRedoAsync("正在重新分析另一区服文件差异……", RegionOperationPhase.BuildingBackup,
            (progress, token) => _regionManager.RedoVerifiedStep2Async(
                _settings.OverwatchGamePath!, progress, token));
    }

    private async Task RunVerifiedRedoAsync(string status, RegionOperationPhase phase,
        Func<IProgress<RegionProgress>, CancellationToken, Task<RegionBackupState>> operation)
    {
        _regionOperationNotice = "";
        _regionOperationError = "";
        _regionOperationCancellation = new CancellationTokenSource();
        SetRegionOperation(phase, new RegionProgress(status));
        Busy = true;
        try
        {
            var progress = new Progress<RegionProgress>(UpdateRegionProgress);
            await operation(progress, _regionOperationCancellation.Token);
            StatusText = "区服文件准备步骤已重新完成。";
        }
        catch (OperationCanceledException)
        {
            _regionOperationNotice = "已取消本次操作，可以从原有准备步骤继续。";
        }
        catch (Exception ex)
        {
            RegionSwitchLog.Write("RegionPreparationRedoFailed", detail: ex.ToString());
            _regionOperationError = "智能差异备份未能完成。\n\n你可以重新尝试，或改用完整备份模式。\n\n原因：" + ex.Message;
        }
        finally
        {
            _regionOperationCancellation.Dispose();
            _regionOperationCancellation = null;
            Busy = false;
            SetRegionOperation(RegionOperationPhase.None);
            await RefreshHomeRegionAsync(verifyFiles: false);
        }
    }

    public void ReturnRegionPreparationToStep1()
    {
        if (!RegionGuide.CanReturnToStep1) return;
        _regionManager.CancelPreparation();
        _regionRestartRequested = true;
        _regionOperationNotice = "";
        _regionOperationError = "";
        StatusText = "已返回区服文件准备第一步。";
        _ = RefreshHomeRegionAsync();
    }

    public void CancelRegionOperation()
    {
        if (!IsRegionOperationBusy || _regionOperationCancellation is null) return;
        _regionOperationCancellation.Cancel();
    }

    public async Task RetryRegionStatusAsync()
    {
        if (Busy || IsRegionOperationBusy) return;
        _regionOperationError = "";
        _regionOperationNotice = "";
        UpdateRegionGuide();
        if (_regionPageStatus.State == RegionBackupState.Preparing)
        {
            await CompleteRegionBackupAsync();
            return;
        }
        await RefreshRegionPageAsync();
    }

    public void SetRegionStoragePath()
    {
        if (!RegionGuide.CanChangePaths) return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择区服备份位置（建议选择空间充足的磁盘）",
            UseDescriptionForTitle = true,
            InitialDirectory = _settings.RegionStoragePath ?? _regionManager.BackupRoot,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _settings.RegionStoragePath = Path.GetFullPath(dialog.SelectedPath);
        _settings.Save();
        _regionManager = new OverwatchRegionManager(_settings.RegionStoragePath);
        _regionOperationError = "";
        _regionOperationNotice = "";
        StatusText = "已设置区服备份位置：" + _settings.RegionStoragePath;
        _ = RefreshHomeRegionAsync();
    }

    private const string UpdatingFilesNotice =
        "游戏文件似乎还在更新\n\nBattle.net 可能仍在写入守望先锋文件。\n\n请等待 Battle.net 完成更新。当 Battle.net 显示“开始游戏”时，请不要启动游戏，直接返回 CloudLight Blizzard 重试。";

    private bool EnsureGameClosedForRegionPreparation()
    {
        if (!OverwatchRegionManager.IsGameRunning()) return true;
        const string message = "检测到《守望先锋》正在运行，请关闭游戏后继续。备份期间不要启动游戏。";
        _regionOperationNotice = message;
        StatusText = "请关闭《守望先锋》后继续区服文件备份。";
        UpdateRegionGuide();
        MessageBox.Show(message, "备份期间请勿启动游戏", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private static bool GameFilesStillChanging(IOException ex) =>
        ex.Message.Contains("仍在更新", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("仍在变化", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("写入", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("扫描期间", StringComparison.OrdinalIgnoreCase);

    private static string FormatRegionFileIssues(IReadOnlyList<RegionFileIssue>? issues) =>
        issues is { Count: > 0 } ? "\n完整路径和原因已写入区服切换日志。" : "";

    private static string RegionName(OverwatchRegion? region) => region == OverwatchRegion.China ? "国服" : "国际服";
}
