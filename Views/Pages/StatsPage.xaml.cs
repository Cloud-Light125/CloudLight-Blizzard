using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CloudLightBlizzard.Services.Overwatch;
using CloudLightBlizzard.Stats;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class StatsPage : UserControl
{
    private readonly StatsQueryWorkflow _workflow = new();
    private MainViewModel? _vm;
    private AccountRow? _selected;
    private bool _suppressSelection;

    public AccountRow? SelectedStatsAccount => _selected;

    public StatsPage()
    {
        InitializeComponent();
        _workflow.Changed += Render;
    }

    public void Initialize(MainViewModel vm)
    {
        _vm = vm;
        AccountBox.ItemsSource = vm.SavedAccounts;
        vm.SavedAccounts.CollectionChanged += OnAccountsChanged;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        SelectAccount(vm.CurrentAccount ?? vm.SavedAccounts.FirstOrDefault());
    }

    public void OnPageOpened()
    {
        if (_selected is null && _vm is not null)
            SelectAccount(_vm.CurrentAccount ?? _vm.SavedAccounts.FirstOrDefault());
        _workflow.PageOpened();
    }

    private void OnAccountsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(SyncToCurrentAccount, DispatcherPriority.DataBind);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentAccount))
            Dispatcher.BeginInvoke(SyncToCurrentAccount, DispatcherPriority.DataBind);
    }

    private void SyncToCurrentAccount()
    {
        if (_vm is null) return;
        var desired = _vm.CurrentAccount is { HasProfile: true } current && _vm.SavedAccounts.Contains(current)
            ? current
            : _selected is not null && _vm.SavedAccounts.Contains(_selected) ? _selected : _vm.SavedAccounts.FirstOrDefault();
        if (!ReferenceEquals(desired, _selected)) SelectAccount(desired);
    }

    public void SelectAccount(AccountRow? row)
    {
        _selected = row;
        if (!ReferenceEquals(AccountBox.SelectedItem, row))
        {
            _suppressSelection = true;
            AccountBox.SelectedItem = row;
            _suppressSelection = false;
        }

        if (row is null)
        {
            RegionText.Text = "—";
            BattleTagText.Text = "";
            _workflow.SelectAccount(null);
            return;
        }

        RegionText.Text = row.RegionText;
        BattleTagText.Text = row.BattleTag;
        _workflow.SelectAccount(new StatsAccountSelection(
            $"{row.AccountId}:{(row.IsCnRegion ? "cn" : "global")}", row.IsCnRegion));
    }

    private void OnAccountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        SelectAccount(AccountBox.SelectedItem as AccountRow);
    }

    private async void OnQuery(object sender, RoutedEventArgs e) => await QuerySelectedAsync(force: false);

    private async void OnRefresh(object sender, RoutedEventArgs e) => await QuerySelectedAsync(force: true);

    private async Task QuerySelectedAsync(bool force)
    {
        if (_selected is not { } selected || _workflow.IsBusy) return;
        DashenClient? dashenClient = null;
        await _workflow.QueryAsync(
            async () => (dashenClient = await DashenAuth.GetAliveAsync()) is not null,
            async () =>
            {
                var view = new StatsWindow();
                await view.LoadAccountAsync(selected.AccountId, dashenClient!);
                return view;
            },
            async () =>
            {
                var view = new CareerWindow();
                await view.LoadAccountAsync(selected.BattleTag, force);
                return view;
            });
    }

    private async void OnLoginDashen(object sender, RoutedEventArgs e)
    {
        await _workflow.LoginAsync(() =>
        {
            var dialog = new QrLoginDialog { Owner = Window.GetWindow(this) };
            var success = dialog.ShowDialog() == true && DashenAuth.Current is not null;
            return Task.FromResult(success);
        });
    }

    private void Render()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Render);
            return;
        }

        AccountBox.IsEnabled = !_workflow.IsBusy;
        QueryButton.IsEnabled = !_workflow.IsBusy && _selected is not null;
        LoginButton.IsEnabled = !_workflow.IsBusy;
        HeaderRefreshButton.IsEnabled = !_workflow.IsBusy;
        LoadingBar.Visibility = _workflow.State == StatsQueryState.Loading ? Visibility.Visible : Visibility.Collapsed;
        LoginButton.Visibility = _workflow.State == StatsQueryState.LoginRequired ? Visibility.Visible : Visibility.Collapsed;
        QueryButton.Visibility = _workflow.State is StatsQueryState.Idle or StatsQueryState.ReadyToQuery or StatsQueryState.Error
            ? Visibility.Visible : Visibility.Collapsed;
        HeaderRefreshButton.Visibility = _workflow.State == StatsQueryState.Loaded ? Visibility.Visible : Visibility.Collapsed;
        StatsHost.Visibility = _workflow.State == StatsQueryState.Loaded ? Visibility.Visible : Visibility.Collapsed;
        StatePanel.Visibility = _workflow.State == StatsQueryState.Loaded ? Visibility.Collapsed : Visibility.Visible;
        StatsHost.Content = _workflow.State == StatsQueryState.Loaded ? _workflow.CurrentResult : null;

        if (_selected is null)
        {
            StateTitle.Text = "选择一个已保存账号";
            StateDescription.Text = "选择账号后，页面会等待你主动查询，不会自动请求战绩。";
            QueryButton.Visibility = Visibility.Collapsed;
            return;
        }

        switch (_workflow.State)
        {
            case StatsQueryState.LoginRequired:
                StateTitle.Text = "查询国服战绩需要登录网易大神。";
                StateDescription.Text = "点击下方按钮打开网易大神登录窗口。登录成功后仍由你决定何时查询战绩。";
                break;
            case StatsQueryState.ReadyToQuery:
                StateTitle.Text = "网易大神已登录";
                StateDescription.Text = "登录成功。点击查询后才会请求国服战绩。";
                QueryButton.Content = "查询国服战绩";
                break;
            case StatsQueryState.Loading:
                StateTitle.Text = selectedRegionLoadingText();
                StateDescription.Text = "正在获取最新数据，请稍候。";
                break;
            case StatsQueryState.Error:
                StateTitle.Text = "这次没有查询成功";
                StateDescription.Text = string.IsNullOrWhiteSpace(_workflow.ErrorMessage)
                    ? "请稍后重试。" : _workflow.ErrorMessage;
                QueryButton.Content = _selected.IsCnRegion ? "重新查询国服战绩" : "重新查询国际服生涯";
                break;
            default:
                StateTitle.Text = "尚未查询战绩";
                StateDescription.Text = "选择账号后，点击查询即可获取最新战绩。";
                QueryButton.Content = _selected.IsCnRegion ? "查询国服战绩" : "查询国际服生涯";
                break;
        }

        string selectedRegionLoadingText() => _selected.IsCnRegion ? "正在查询国服战绩…" : "正在查询国际服生涯…";
    }

    public static string DataSourceFor(AccountRow row) => row.IsCnRegion ? "ChinaStats" : "BlizzardCareer";
}
