using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CloudLightBlizzard.Services.Overwatch;

namespace CloudLightBlizzard.Stats;

public partial class StatsWindow : UserControl
{
    private StatsService? _svc;
    private long? _currentRoleId;       // 当前展示的号(刷新用)
    private string _season = "";        // ""=本赛季
    private string _gameMode = "sport"; // sport 竞技 / leisure 休闲 / fight 角斗 / lfight 休闲角斗
    private bool _useOpen;              // 开放职责 / 预设职责
    private int _matchPage = 1;         // 对局已加载页(初始12条=第1页)
    private bool _loadingMore;          // 正在翻页
    private bool _loading;
    private bool _noMoreMatches;        // 已到底
    private int _currentSeasonNum = 23; // 当前赛季号(好友列表用)

    private bool _suppressSeason;   // 程序化填充赛季下拉时,别触发重载
    private bool _seasonMetadataLoaded;

    public StatsWindow()
    {
        InitializeComponent();
        ApplySeasons(new List<int> { 23, 22 });
    }

    // 赛季下拉:从 AIEvaluateConfig 动态生成(本赛季=最大号 + 灰字标号,其余为可查赛季)。纯配置,无需登录。
    private async Task PopulateSeasonsAsync()
    {
        List<int> seasons;
        try { seasons = (await OwMappings.LoadSeasonMetaAsync()).Seasons; }
        catch { seasons = new(); }
        if (seasons.Count == 0) seasons = new List<int> { 23, 22 };   // 配置拉不到时兜底
        ApplySeasons(seasons);
        _seasonMetadataLoaded = true;
    }

    private void ApplySeasons(List<int> seasons)
    {
        int cur = seasons[0];   // 已降序,最大即本赛季
        _currentSeasonNum = cur;
        var opts = new List<SeasonOpt> { new("本赛季", $"第{cur}赛季", "") };
        foreach (var s in seasons)
            if (s != cur) opts.Add(new SeasonOpt($"第{s}赛季", "", s.ToString()));

        _suppressSeason = true;
        SeasonBox.ItemsSource = opts;
        SeasonBox.SelectedIndex = 0;
        _suppressSeason = false;
    }

    /// <summary>演示数据打开(免登录,--statsdemo 调试用),验证界面布局不崩。</summary>
    public static void ShowDemo()
    {
        var view = new StatsWindow();
        view.DataContext = DemoStatsProvider.GetDemo(k => view.TryFindResource(k) as Brush);
        ShowInTestHost(view, "国服战绩演示");
    }

    /// <summary>为某账号打开战绩(roleId = 账号 id = account_id_lo)。从主界面入口调用。</summary>
    public static void ShowFor(Window owner, long roleId)
    {
        var view = new StatsWindow();
        ShowInTestHost(view, "国服战绩", owner);
        view.ShowOverlay("尚未查询战绩", "请从主窗口选择账号并点击“查询国服战绩”", retry: false);
    }

    public async Task LoadAccountAsync(long roleId, DashenClient client)
    {
        if (!_seasonMetadataLoaded) await PopulateSeasonsAsync();
        _svc = new StatsService(client);
        await LoadAsync(roleId);
    }

    private async Task LoadAsync(long roleId)
    {
        if (_svc is null || _loading) return;
        _loading = true;
        RefreshButton.IsEnabled = false;
        SearchBox.IsEnabled = false;
        SeasonBox.IsEnabled = false;
        ModePanel.IsEnabled = false;
        QueuePanel.IsEnabled = false;
        _currentRoleId = roleId;
        ShowOverlay("正在加载战绩…", "拉取账号资料与赛季战绩,约需 2-5 秒", retry: false);
        try
        {
            var ps = await _svc.LoadAsync(roleId, key => TryFindResource(key) as Brush, _season, _gameMode, _useOpen);
            if (ps is null || string.IsNullOrEmpty(ps.DisplayName))
            {
                ShowOverlay("没有查到数据", "该玩家可能已隐藏生涯战绩,或本赛季暂无对局", retry: true);
                return;
            }
            DataContext = ps;
            _loadingMore = false; _noMoreMatches = false;
            // 拉第 1 页对局(统一走 queryMatchList 分页;滚动到底续拉)
            try
            {
                int got = await _svc.LoadMoreMatchesAsync(ps.Matches, roleId, _gameMode, _useOpen, 1);
                if (got < 12) _noMoreMatches = true;
            }
            catch { /* 对局拉取失败不影响其它数据 */ }
            _matchPage = 1;   // 换号/换模式时 DataContext 换新集合,列表自动回到顶部
            HideOverlay();
        }
        catch
        {
            ShowOverlay("网络开小差", "点「重试」再来一次", retry: true);
        }
        finally
        {
            _loading = false;
            RefreshButton.IsEnabled = true;
            SearchBox.IsEnabled = true;
            SeasonBox.IsEnabled = true;
            ModePanel.IsEnabled = true;
            QueuePanel.IsEnabled = true;
        }
    }

    // 对局列表滚到底 → 翻页拉下一批,直到拉完全部
    private async void OnMatchScroll(object sender, ScrollChangedEventArgs e)
    {
        if (_svc is null || _currentRoleId is not long rid || _loadingMore || _noMoreMatches) return;
        if (DataContext is not PlayerStats ps) return;
        // ScrollChanged 挂在 ItemsControl 上(附加事件),真正的 ScrollViewer 在 OriginalSource
        if (e.OriginalSource is not ScrollViewer sv || sv.ScrollableHeight <= 0) return;
        if (sv.VerticalOffset < sv.ScrollableHeight - 48) return;   // 离底还远

        _loadingMore = true;
        try
        {
            int got = await _svc.LoadMoreMatchesAsync(ps.Matches, rid, _gameMode, _useOpen, _matchPage + 1);
            _matchPage++;
            if (got < 12) _noMoreMatches = true;   // 不足一页 = 到底
        }
        catch { /* 忽略,下次滚动再试 */ }
        finally { _loadingMore = false; }
    }

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _svc is null) return;
        var tag = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(tag)) return;
        if (!tag.Contains('#'))
        {
            ShowOverlay("格式不对", "请输入完整 BattleTag,如 玩家#5945", retry: false);
            return;
        }
        ShowOverlay("正在搜索玩家…", tag, retry: false);
        try
        {
            var rid = await _svc.ResolveBattleTagAsync(tag);
            if (rid is null) { ShowOverlay("没找到这个玩家", "确认 BattleTag(含 #编号)拼写无误", retry: false); return; }
            await LoadAsync(rid.Value);
        }
        catch { ShowOverlay("搜索失败", "稍后再试", retry: false); }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_currentRoleId is long id) await LoadAsync(id);
    }

    private async void OnSeasonChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSeason) return;
        _season = (SeasonBox.SelectedItem as SeasonOpt)?.Tag ?? "";
        await ReloadIfLoaded();
    }

    private async void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string mode }) return;
        // 角斗领域/休闲角斗无预设/开放之分,隐藏队列切换
        if (QueuePanel != null) QueuePanel.Visibility = mode is "fight" or "lfight" ? Visibility.Collapsed : Visibility.Visible;
        _gameMode = mode;
        await ReloadIfLoaded();
    }

    private async void OnQueueChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string q }) _useOpen = q == "open";
        await ReloadIfLoaded();
    }

    private async Task ReloadIfLoaded()
    {
        if (_currentRoleId is long id && _svc != null) await LoadAsync(id);
    }

    // 点对局行 → 单局记分板
    private void OnMatchClick(object sender, MouseButtonEventArgs e)
    {
        if (_svc is null || _currentRoleId is not long rid) return;
        if (sender is FrameworkElement { DataContext: MatchRecord m } && !string.IsNullOrEmpty(m.MatchId))
            MatchDetailWindow.ShowFor(Window.GetWindow(this)!, _svc, rid, m, _gameMode is "fight" or "lfight");
    }

    // 排行榜:该玩家各英雄省内排名
    private void OnOpenBillboard(object sender, RoutedEventArgs e)
    {
        if (_svc is null || _currentRoleId is not long rid) return;
        BillboardWindow.ShowFor(Window.GetWindow(this)!, _svc, rid, _useOpen);
    }

    // 好友:当前查看玩家的守望先锋好友(roleId 驱动,可查任意玩家)
    private void OnOpenFriends(object sender, RoutedEventArgs e)
    {
        if (_svc is null || _currentRoleId is not long rid) return;
        string name = (DataContext as PlayerStats)?.DisplayName ?? "";
        FriendsWindow.ShowFor(Window.GetWindow(this)!, _svc, rid, _currentSeasonNum, name);
    }

    // 点英雄行 → 场均数据详情
    private void OnHeroClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HeroStat h })
            HeroDetailWindow.ShowFor(Window.GetWindow(this)!, h);
    }

    private void OnBack(object sender, RoutedEventArgs e) { }

    private static void ShowInTestHost(FrameworkElement view, string title, Window? owner = null)
    {
        new Window
        {
            Title = title, Width = 900, Height = 760, MinWidth = 780, MinHeight = 640,
            Owner = owner, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Content = view, Background = Application.Current.TryFindResource("App.Background") as Brush,
        }.Show();
    }

    private void ShowOverlay(string title, string hint, bool retry)
    {
        OverlayTitle.Text = title;
        OverlayHint.Text = hint;
        OverlayRetry.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        OverlayPanel.Visibility = Visibility.Visible;
    }

    private void HideOverlay() => OverlayPanel.Visibility = Visibility.Collapsed;
}

// 赛季下拉项:Main 主文字(本赛季/第N赛季),Sub 灰字副文本(本赛季旁的"第N赛季"),Tag 传给接口的 season("" = 本赛季)
public sealed record SeasonOpt(string Main, string Sub, string Tag);
