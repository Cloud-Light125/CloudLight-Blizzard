using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CloudLightBlizzard.Services.Overwatch;

namespace CloudLightBlizzard.Stats;

// 国际服生涯数据来自暴雪官方公开生涯页。
// 页面一次包含「键鼠/手柄 × 快速/竞技」四份数据，切换分段时只在本地重建视图。
public partial class CareerWindow : UserControl
{
    private readonly CareerService _svc = new();
    private CareerParser.Profile? _profile;   // 当前账号的整页解析结果(四个分段都在里面)
    private string _battleTag = "";
    private string _input = CareerService.InputPc;
    private string _mode = CareerService.ModeComp;
    private bool _suppressSeg;                // 程序化勾选分段按钮时,别触发重建
    private HeroSortField _heroSortField = HeroSortField.Duration;
    private ListSortDirection _heroSortDirection = ListSortDirection.Descending;
    private ListCollectionView? _heroView;

    public CareerWindow()
    {
        InitializeComponent();
        UpdateHeroSortDisplay();
    }

    /// <summary>预填 BattleTag 并停留在查询前状态，不发起网络请求。</summary>
    public void PrepareAccount(string battleTag)
    {
        _battleTag = battleTag.Trim();
        _profile = null;
        DataContext = null;
        HeroList.ItemsSource = null;
        _heroView = null;
        SearchBox.Text = _battleTag;
        ShowOverlay(OverlayKind.Empty, "查询国际服战绩",
            string.IsNullOrEmpty(_battleTag)
                ? "输入 BattleTag（例如 Player#1234），再点击“查询”。"
                : $"已填入 {_battleTag}，点击“查询”后读取公开生涯数据。");
    }

    /// <summary>为某个国际服账号打开生涯窗。吃的是 BattleTag(如 Player#1234),不是 roleId。</summary>
    public static void ShowFor(Window owner, string battleTag)
    {
        var view = new CareerWindow();
        ShowInTestHost(view, "国际服生涯", owner);
        view.SearchBox.Text = battleTag;
        view.ShowOverlay(OverlayKind.Empty, "尚未查询战绩", "点击“查询”后才会读取国际服公开生涯。");
    }

    /// <summary>无主窗打开(--careerdemo 调试用):空 tag 就停在搜索态,自己敲一个查。</summary>
    public static void ShowStandalone(string battleTag)
    {
        var view = new CareerWindow();
        ShowInTestHost(view, "国际服生涯演示");
        view.SearchBox.Text = battleTag;
        view.ShowOverlay(OverlayKind.Empty, "尚未查询战绩", "在右上角输入战网昵称#编号，回车或点击“查询”。");
    }

    public Task LoadAccountAsync(string battleTag, bool force = false) => LoadAsync(battleTag, force);

    // ── 加载 ────────────────────────────────────────────────────
    private async Task LoadAsync(string battleTag, bool force = false)
    {
        battleTag = battleTag.Trim();
        if (string.IsNullOrEmpty(battleTag)) return;
        _battleTag = battleTag;
        SearchBox.Text = battleTag;

        ShowOverlay(OverlayKind.Loading, "正在读取生涯数据…", "数据来自暴雪公开生涯页,第一次查询可能会慢一点。");
        QueryBtn.IsEnabled = RefreshBtn.IsEnabled = false;
        try
        {
            var outcome = await _svc.LoadAsync(battleTag, force);
            if (outcome.Profile is not { } p)
            {
                if (outcome.NotFound)
                    ShowOverlay(OverlayKind.NotFound, "这个账号在国际服查不到",
                        $"{battleTag}\n请检查昵称与编号，并确认使用的是国际服 BattleTag。");
                else if (outcome.Error?.Contains("未公开") == true)
                    ShowOverlay(OverlayKind.Private, "这个账号的生涯档案没公开",
                        "在游戏里把「生涯档案」设为公开,再回来刷新一次。");
                else
                    ShowOverlay(OverlayKind.Error, "这次没查到", outcome.Error ?? "稍后再试一次。");
                return;
            }

            _profile = p;
            // 默认落在有数据的分段上:优先键鼠 + 竞技,没有就退到实际有数据的那一份
            _input = CareerService.PickInput(p);
            _mode = CareerService.PickMode(p, _input);
            SyncSegmentButtons(p);

            if (!CareerService.HasData(p, CareerService.InputPc) && !CareerService.HasData(p, CareerService.InputPad))
            {
                ShowOverlay(OverlayKind.Empty, "这个账号暂时没有生涯数据",
                    "档案是公开的,但还没有可展示的对局数据。");
                return;
            }

            await RebuildAsync();
        }
        catch (Exception ex)
        {
            ShowOverlay(OverlayKind.Error, "这次没查到", ex.Message);
        }
        finally
        {
            QueryBtn.IsEnabled = RefreshBtn.IsEnabled = true;
        }
    }

    /// <summary>按当前分段重建界面数据。不发请求 —— 四份数据早就在 _profile 里了。</summary>
    private async Task RebuildAsync()
    {
        if (_profile is not { } p) return;
        var ps = await CareerService.BuildAsync(p, _input, _mode, k => TryFindResource(k) as Brush);
        BindHeroList(ps.Heroes);

        if (!CareerService.HasData(p, _input, _mode))
        {
            // 分段本身没数据(比如这个号只打过快速):卡片留着当前段位/概览会全空,不如直接说清楚
            DataContext = ps;
            ShowOverlay(OverlayKind.Empty, "这个分段没有数据",
                _mode == CareerService.ModeComp ? "换到「快速」看看,或者这个账号本赛季还没打竞技。"
                                                : "这个输入设备下没有快速模式的数据。");
            return;
        }

        DataContext = ps;
        HeroCount.Text = ps.Heroes.Count > 0 ? $"· {ps.Heroes.Count} 个" : "";
        HideOverlay();
    }

    private void BindHeroList(List<HeroStat> heroes)
    {
        _heroView = new ListCollectionView(heroes);
        HeroList.ItemsSource = _heroView;
        ApplyHeroSort();
    }

    private void OnHeroSort(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<HeroSortField>(tag, out var field)) return;
        if (_heroSortField == field)
            _heroSortDirection = _heroSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        else
        {
            _heroSortField = field;
            _heroSortDirection = field == HeroSortField.Name
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;
        }
        ApplyHeroSort();
    }

    private void ApplyHeroSort()
    {
        if (_heroView is not null)
        {
            _heroView.CustomSort = new HeroStatComparer(_heroSortField, _heroSortDirection);
            HeroScrollViewer.ScrollToTop();
        }
        UpdateHeroSortDisplay();
    }

    private void UpdateHeroSortDisplay()
    {
        HeroNameSortIndicator.Text = "";
        HeroDurationSortIndicator.Text = "";
        HeroWinRateSortIndicator.Text = "";
        HeroMatchesSortIndicator.Text = "";
        HeroUsageSortIndicator.Text = "";

        var indicator = _heroSortDirection == ListSortDirection.Ascending ? "↑" : "↓";
        var label = _heroSortField switch
        {
            HeroSortField.Name => "英雄",
            HeroSortField.Duration => "时长",
            HeroSortField.WinRate => "胜率",
            HeroSortField.Matches => "场次",
            HeroSortField.Usage => "使用占比",
            _ => "时长",
        };
        switch (_heroSortField)
        {
            case HeroSortField.Name: HeroNameSortIndicator.Text = indicator; break;
            case HeroSortField.Duration: HeroDurationSortIndicator.Text = indicator; break;
            case HeroSortField.WinRate: HeroWinRateSortIndicator.Text = indicator; break;
            case HeroSortField.Matches: HeroMatchesSortIndicator.Text = indicator; break;
            case HeroSortField.Usage: HeroUsageSortIndicator.Text = indicator; break;
        }
        HeroSortSummary.Text = $"按{label}{(_heroSortDirection == ListSortDirection.Ascending ? "升序" : "降序")}";
    }

    // ── 分段:没数据的一边留着但禁用,并标「· 无数据」──────────
    private void SyncSegmentButtons(CareerParser.Profile p)
    {
        _suppressSeg = true;
        try
        {
            Mark(RbPc, "键鼠", CareerService.HasData(p, CareerService.InputPc), "这个账号没有键鼠数据");
            Mark(RbPad, "手柄", CareerService.HasData(p, CareerService.InputPad), "这个账号没有手柄数据");
            Mark(RbQuick, "快速", CareerService.HasData(p, _input, CareerService.ModeQuick), "当前输入设备下没有快速数据");
            Mark(RbComp, "竞技", CareerService.HasData(p, _input, CareerService.ModeComp), "当前输入设备下没有竞技数据");

            RbPc.IsChecked = _input == CareerService.InputPc;
            RbPad.IsChecked = _input == CareerService.InputPad;
            RbQuick.IsChecked = _mode == CareerService.ModeQuick;
            RbComp.IsChecked = _mode == CareerService.ModeComp;
        }
        finally { _suppressSeg = false; }
    }

    private static void Mark(RadioButton rb, string label, bool has, string tip)
    {
        rb.IsEnabled = has;
        rb.Opacity = has ? 1.0 : 0.45;
        rb.ToolTip = has ? null : tip;
        rb.Content = has ? label : label + " · 无数据";
    }

    private async void OnInputChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSeg || _profile is not { } p) return;
        if (sender is not RadioButton { Tag: string input }) return;
        _input = input;
        // 换了输入设备,原来的模式可能没数据了(手柄常常只有快速)→ 跟着挑一个有数据的
        if (!CareerService.HasData(p, _input, _mode)) _mode = CareerService.PickMode(p, _input);
        SyncSegmentButtons(p);
        await RebuildAsync();
    }

    private async void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSeg || _profile is null) return;
        if (sender is not RadioButton { Tag: string mode }) return;
        _mode = mode;
        await RebuildAsync();
    }

    // ── 顶栏动作 ────────────────────────────────────────────────
    private async void OnQuery(object sender, RoutedEventArgs e) => await QueryFromBoxAsync();

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await QueryFromBoxAsync();
    }

    private async Task QueryFromBoxAsync()
    {
        var tag = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(tag)) return;
        if (!tag.Contains('#'))
        {
            ShowOverlay(OverlayKind.Error, "格式不对", "要带上编号,比如 Player#1234。");
            return;
        }
        await LoadAsync(tag);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_battleTag)) await LoadAsync(_battleTag, force: true);
    }

    private void OnHeroClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HeroStat h })
            HeroDetailWindow.ShowFor(Window.GetWindow(this)!, h);
    }

    // ── 覆盖态 ──────────────────────────────────────────────────
    private enum OverlayKind { Loading, Empty, Error, NotFound, Private }

    private void ShowOverlay(OverlayKind kind, string title, string hint)
    {
        OverlayTitle.Text = title;
        OverlayHint.Text = hint;
        OverlayHint.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
        OverlayBar.Visibility = kind == OverlayKind.Loading ? Visibility.Visible : Visibility.Collapsed;
        OverlayButtons.Visibility = kind == OverlayKind.Loading ? Visibility.Collapsed : Visibility.Visible;

        (OverlayPrimary.Content, OverlaySecondary.Content) = kind switch
        {
            OverlayKind.Private => ("我已公开,刷新", "换个账号"),
            _ => ("重试", "换个账号"),
        };
        // 「没数据」不是错,没什么可重试的,只留「换个账号」
        OverlayPrimary.Visibility = kind == OverlayKind.Empty ? Visibility.Collapsed : Visibility.Visible;

        OverlayPanel.Visibility = Visibility.Visible;
        SegmentBar.IsEnabled = false;
    }

    private void HideOverlay()
    {
        OverlayPanel.Visibility = Visibility.Collapsed;
        SegmentBar.IsEnabled = true;
    }

    private async void OnOverlayPrimary(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_battleTag)) await LoadAsync(_battleTag, force: true);
    }

    private void OnOverlaySecondary(object sender, RoutedEventArgs e)
    {
        SearchBox.SelectAll();
        SearchBox.Focus();
    }

    private static void ShowInTestHost(FrameworkElement view, string title, Window? owner = null)
    {
        new Window
        {
            Title = title, Width = 940, Height = 780, MinWidth = 800, MinHeight = 680,
            Owner = owner, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Content = view, Background = Application.Current.TryFindResource("App.Background") as Brush,
        }.Show();
    }
}
