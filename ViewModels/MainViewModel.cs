using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using BnetSwitch.Models;
using BnetSwitch.Services;
using BnetSwitch.Services.OverwatchRegion;
using Microsoft.Win32;

namespace BnetSwitch.ViewModels;

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
    public AccountRegionOverride RegionOverride { get => _regionOverride; set { Set(ref _regionOverride, value); Raise(nameof(IsCnRegion)); Raise(nameof(RegionText)); } }

    public string DisplayName => string.IsNullOrWhiteSpace(CustomName) ? BattleTag : CustomName.Trim();
    public Visibility CustomNameVisibility => string.IsNullOrWhiteSpace(CustomName) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RemarkVisibility => string.IsNullOrWhiteSpace(Remark) ? Visibility.Collapsed : Visibility.Visible;
    public bool IsCnRegion => RegionOverride == AccountRegionOverride.China ||
                              (RegionOverride == AccountRegionOverride.Auto && (IsCn(Environment) || string.IsNullOrWhiteSpace(Environment)));
    public static bool IsCn(string? environment)
        => environment?.Contains("battlenet.com.cn", StringComparison.OrdinalIgnoreCase) == true;

    public string RegionText => IsCnRegion ? "国服" : "国际服";
    public Visibility RegionVisibility => RegionText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

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
    public event Action<string>? MainSectionRequested;
    private readonly BattleNetPaths _paths;
    private readonly AccountReader _reader;
    private readonly AppDataStore _profiles;
    private readonly BattleNetController _controller;
    private readonly AppSettings _settings;
    private OverwatchRegionManager _regionManager;
    private readonly AccountSwitchLog _switchLog;
    private readonly BattleNetAuthLogProbe _authLogProbe;
    private readonly SemaphoreSlim _regionStatusGate = new(1, 1);

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
    public bool Busy { get => _busy; set { Set(ref _busy, value); Raise(nameof(NotBusy)); } }
    public bool NotBusy => !_busy;

    private bool _clientRunning;
    public bool ClientRunning
    {
        get => _clientRunning;
        private set { Set(ref _clientRunning, value); Raise(nameof(LaunchText)); }
    }

    public string LaunchText => _clientRunning ? "打开战网窗口" : "启动战网";
    public string AppVersion => "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    public AppSettings Settings => _settings;
    public string RegionBackupRoot => _regionManager.BackupRoot;

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

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _paths = new BattleNetPaths();
        if (!string.IsNullOrEmpty(_settings.ClientExe) && File.Exists(_settings.ClientExe))
            _paths.ClientExe = _settings.ClientExe;

        _reader = new AccountReader(_paths);
        _profiles = new AppDataStore(_paths);
        _controller = new BattleNetController(_paths);
        _regionManager = new OverwatchRegionManager(_settings.RegionStoragePath);
        _switchLog = new AccountSwitchLog();
        _authLogProbe = new BattleNetAuthLogProbe(_paths);
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
                IsExpired = meta?.Expired == true,
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
                IsExpired = meta.Expired,
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
                IsExpired = meta?.Expired == true,
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
            if (row is { IsExpired: true })
            {
                await Task.Run(() => _profiles.SetExpired(targetId, false));
                row.IsExpired = false;
                RebuildGroups();
            }
            StatusText = $"已切换到「{row?.BattleTag ?? targetId.ToString()}」。";
            _switchLog.Write("Success", targetAccountId: targetId);
            return;
        }

        if (verification == AccountSwitchVerificationState.LoginRequired)
        {
            _pendingSwitchId = null;
            var expiredTarget = Accounts.FirstOrDefault(a => a.AccountId == targetId);
            await Task.Run(() => _profiles.SetExpired(targetId, true));
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
                StatusText = $"「{exp.BattleTag}」已经重新登录，建议更新本地账号备份。";
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

            var regionStatus = await RefreshHomeRegionAsync(verifyFiles: false);
            if (!_staleNotified && regionStatus?.State == RegionBackupState.Stale)
            {
                _staleNotified = true;
                StatusText = "检测到守望先锋已经更新，需要重新准备区服文件。";
                if (MessageBox.Show(
                    "当前游戏文件与之前记录的版本不一致。\n\n可能是 Battle.net 仍在更新，或者《守望先锋》已经发布了新版本。\n\n请等待 Battle.net 完成更新后重新准备区服文件。现在打开区服文件页面吗？",
                    "需要重新准备区服文件", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    MainSectionRequested?.Invoke("region");
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
        var skipGameFiles = false;
        try
        {
            var regionStatus = await _regionManager.GetStatusAsync(_settings.OverwatchGamePath);
            if (regionStatus.CurrentRegion != (targetRegion == OverwatchRegion.China
                    ? CurrentGameRegion.China : CurrentGameRegion.International))
            {
                if (!regionStatus.GamePathValid || regionStatus.State != RegionBackupState.Ready)
                {
                    var choice = new RegionSwitchChoiceWindow(target.RegionText, regionStatus.State)
                    {
                        Owner = Application.Current.MainWindow
                    }.ShowDialogChoice();
                    if (choice == RegionSwitchChoice.Settings)
                    {
                        MainSectionRequested?.Invoke("region");
                        return;
                    }
                    if (choice != RegionSwitchChoice.AccountOnly) return;
                    skipGameFiles = true;
                }
                else if (OverwatchRegionManager.IsGameRunning())
                {
                    MessageBox.Show("守望先锋正在运行，请先退出游戏后再切换区服。",
                        "无法切换游戏文件", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法检查本地区服文件：" + ex.Message, "无法检查区服文件",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Busy = true;
        var stage = "关闭 Battle.net";
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
                    if (currentId is not long cur || cur == target.AccountId) return;
                    stage = "账号备份";
                    var curRow = Accounts.FirstOrDefault(a => a.AccountId == cur);
                    if (curRow is not { HasProfile: true }) return;
                    StatusText = $"正在更新当前账号「{curRow.BattleTag}」的本地备份…";
                    _switchLog.Write("SourceBackupStarted", currentId, target.AccountId);
                    await Task.Run(() => _profiles.Save(cur, curRow.BattleTag));
                    _switchLog.Write("SourceBackupCompleted", currentId, target.AccountId);
                    curRow.SavedAtUtc = DateTime.UtcNow;
                    curRow.IsExpired = false;
                },
                async () =>
                {
                    if (skipGameFiles) return;
                    stage = "区服文件";
                    var currentRegion = await _regionManager.DetectCurrentRegionAsync(_settings.OverwatchGamePath!);
                    var expectedRegion = targetRegion == OverwatchRegion.China
                        ? CurrentGameRegion.China : CurrentGameRegion.International;
                    if (currentRegion == expectedRegion) return;
                    var progress = new Progress<RegionProgress>(p => StatusText = p.Message);
                    _switchLog.Write("RegionFilesSwitchStarted", currentId, target.AccountId,
                        sourceRegion: currentRegion.ToString(), targetRegion: target.RegionText);
                    var result = await _regionManager.NormalizeToRegionAsync(
                        _settings.OverwatchGamePath!, targetRegion, progress);
                    _switchLog.Write("RegionFilesSwitchCompleted", currentId, target.AccountId,
                        targetRegion: target.RegionText, detail: $"restored={result.Restored};deleted={result.Deleted};verified={result.Verified}");
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

            foreach (var a in Accounts) a.IsActive = a.AccountId == target.AccountId;
            _lastActiveId = target.AccountId;
            RebuildGroups();
            _pendingSwitchId = target.AccountId;
            _pendingSwitchUntil = DateTime.UtcNow.AddSeconds(SwitchVerifySeconds);
            _pendingSwitchStartedUtc = DateTime.UtcNow;
            _pendingClientSeen = false;
            _lastPendingActiveId = currentId;
            StatusText = $"已切换到「{target.BattleTag}」,战网正在启动,正在确认登录结果…";
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
            var currentId = await Task.Run(() => _reader.ReadActiveAccountId());
            var stopped = await Task.Run(() => _controller.GracefulQuit());
            if (!stopped)
                throw new InvalidOperationException("战网未能完全退出,请从托盘右键『退出』战网后重试。");

            if (currentId is long cur && cur != row.AccountId)
            {
                var curRow = Accounts.FirstOrDefault(a => a.AccountId == cur);
                if (curRow is { HasProfile: true })
                {
                    StatusText = $"正在保存当前号「{curRow.BattleTag}」…";
                    await Task.Run(() => _profiles.Save(cur, curRow.BattleTag));
                    curRow.SavedAtUtc = DateTime.UtcNow;
                    curRow.IsExpired = false;
                }
            }

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
            row.HasProfile = false;
            row.SavedAtUtc = null;
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
        StatusText = "已设置守望先锋游戏目录：" + _settings.OverwatchGamePath;
    }

    public bool AutoDetectOverwatchGamePath()
    {
        var path = OverwatchGameLocator.Detect(_paths);
        if (string.IsNullOrWhiteSpace(path)) return false;
        _settings.OverwatchGamePath = path;
        _settings.Save();
        return true;
    }

    public async Task<RegionSnapshotStatus> GetRegionStatusAsync(bool verifyFiles = false)
    {
        await _regionStatusGate.WaitAsync();
        try
        {
            return await Task.Run(() => _regionManager.GetStatusAsync(
                _settings.OverwatchGamePath, verifyFiles: verifyFiles));
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
            _homeRegionState = status.State;
            _homeCurrentRegion = status.CurrentRegion;
            GameRegionTitle = status.CurrentRegion switch
            {
                CurrentGameRegion.China => "当前文件：国服",
                CurrentGameRegion.International => "当前文件：国际服",
                CurrentGameRegion.Mixed => "当前文件：正在切换 / 状态不完整",
                _ => "当前文件：尚未识别",
            };
            GameRegionFilesText = $"国服文件：{(status.ChinaBackupComplete ? "已准备" : status.ChinaCaptured ? "已保存在本地" : "尚未准备")}  ·  " +
                                  $"国际服文件：{(status.InternationalBackupComplete ? "已准备" : status.InternationalCaptured ? "已保存在本地" : "尚未准备")}";
            GameRegionPath = string.IsNullOrWhiteSpace(status.GamePath) ? "尚未设置游戏目录" : status.GamePath;
            GameRegionSummary = status.State switch
            {
                RegionBackupState.Empty => "首次设置只需要让 Battle.net 完成一次跨区更新。",
                RegionBackupState.Preparing => $"{RegionName(status.PendingSourceRegion)}文件已经保存在本地。请在 Battle.net 中切换到{RegionName(status.PendingTargetRegion)}并等待更新完成，然后回来继续。",
                RegionBackupState.Ready when status.CurrentRegion == CurrentGameRegion.Mixed =>
                    $"当前游戏文件处于未完成的区服切换状态，可以直接使用本地备份恢复到国服或国际服。已保存 {status.DifferenceCount} 个差异文件 · {FormatBytes(status.BackupBytes)}",
                RegionBackupState.Ready => $"已保存 {status.DifferenceCount} 个区服差异文件 · {FormatBytes(status.BackupBytes)}",
                RegionBackupState.Stale => "游戏已经更新，需要重新准备区服文件。",
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
            CanSwitchChina = status.GamePathValid && status.State == RegionBackupState.Ready && status.CurrentRegion != CurrentGameRegion.China;
            CanSwitchInternational = status.GamePathValid && status.State == RegionBackupState.Ready && status.CurrentRegion != CurrentGameRegion.International;
            SwitchChinaText = status.CurrentRegion == CurrentGameRegion.Mixed ? "恢复到国服" :
                status.CurrentRegion == CurrentGameRegion.China ? "当前为国服" : "切换到国服";
            SwitchInternationalText = status.CurrentRegion == CurrentGameRegion.Mixed ? "恢复到国际服" :
                status.CurrentRegion == CurrentGameRegion.International ? "当前为国际服" : "切换到国际服";
            RegionSetupVisibility = status.State == RegionBackupState.Ready ? Visibility.Collapsed : Visibility.Visible;
            return status;
        }
        catch
        {
            GameRegionSummary = "暂时无法读取区服文件状态。";
            return null;
        }
    }

    public async Task SwitchGameRegionOnlyAsync(OverwatchRegion target)
    {
        if (Busy) return;
        Busy = true;
        var restartClient = false;
        try
        {
            if (OverwatchRegionManager.IsGameRunning())
                throw new InvalidOperationException("守望先锋正在运行，请先退出游戏后再切换区服文件。");
            restartClient = await Task.Run(() => _controller.IsClientRunning());
            if (restartClient)
            {
                StatusText = "正在正常关闭 Battle.net…";
                RegionSwitchLog.Write("Battle.net quit begin", target);
                if (!await Task.Run(() => _controller.GracefulQuit()))
                    throw new InvalidOperationException("Battle.net 未能完全退出，请从托盘退出后重试。");
                RegionSwitchLog.Write("Battle.net quit end", target);
            }
            StatusText = $"正在切换守望先锋到{(target == OverwatchRegion.China ? "国服" : "国际服")}…";
            var progress = new Progress<RegionProgress>(p => StatusText = p.Message);
            await _regionManager.NormalizeToRegionAsync(_settings.OverwatchGamePath!, target, progress);
            if (restartClient)
            {
                StatusText = "正在重新启动 Battle.net…";
                await Task.Run(() => _controller.LaunchClient());
                RegionSwitchLog.Write("Battle.net restart", target);
                restartClient = false;
            }
            StatusText = $"守望先锋区服文件已切换到{(target == OverwatchRegion.China ? "国服" : "国际服")}。";
        }
        catch (Exception ex)
        {
            StatusText = "切换区服文件失败：" + ex.Message;
            MessageBox.Show(ex.Message, "无法切换区服文件", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Busy = false;
            await RefreshHomeRegionAsync();
        }
    }

    public static string FormatBytes(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024.0:0.0} KB" : $"{bytes / 1024.0 / 1024.0:0.0} MB";

    public async Task CaptureRegionAsync(OverwatchRegion region, IProgress<RegionProgress> progress,
        CancellationToken cancellationToken = default)
    {
        Busy = true;
        try
        {
            var state = await _regionManager.CaptureAsync(_settings.OverwatchGamePath!, region, progress, cancellationToken);
            StatusText = state switch
            {
                RegionBackupState.Preparing => $"{RegionName(region)}文件已经保存在本地。请在 Battle.net 中切换到{RegionName(region == OverwatchRegion.China ? OverwatchRegion.International : OverwatchRegion.China)}并等待更新完成。",
                RegionBackupState.Ready => "国服和国际服文件都已准备好，可以直接切换。",
                _ => "区服文件状态已更新。",
            };
        }
        finally { Busy = false; await RefreshHomeRegionAsync(); }
    }

    public async Task CompleteRegionBackupAsync(IProgress<RegionProgress> progress,
        CancellationToken cancellationToken = default)
    {
        Busy = true;
        try
        {
            var state = await _regionManager.CompleteAsync(_settings.OverwatchGamePath!, progress, cancellationToken);
            StatusText = state == RegionBackupState.Ready
                ? "国服和国际服文件都已准备好，可以随账号自动切换。"
                : "区服文件还需要完成最后一步。";
        }
        finally { Busy = false; await RefreshHomeRegionAsync(); }
    }

    public void ResetRegionBackup()
    {
        _regionManager.Reset();
        StatusText = "已清除国服和国际服文件备份。";
        _ = RefreshHomeRegionAsync();
    }

    public void CancelRegionPreparation()
    {
        _regionManager.CancelPreparation();
        StatusText = "已取消本次准备，现有可用区服文件没有改变。";
        _ = RefreshHomeRegionAsync();
    }

    public void SetRegionStoragePath()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择区服文件存储位置（建议选择空间充足的磁盘）",
            UseDescriptionForTitle = true,
            InitialDirectory = _settings.RegionStoragePath ?? _regionManager.BackupRoot,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _settings.RegionStoragePath = Path.GetFullPath(dialog.SelectedPath);
        _settings.Save();
        _regionManager = new OverwatchRegionManager(_settings.RegionStoragePath);
        StatusText = "已设置区服文件存储位置：" + _settings.RegionStoragePath;
    }

    private static string RegionName(OverwatchRegion? region) => region == OverwatchRegion.China ? "国服" : "国际服";
}
