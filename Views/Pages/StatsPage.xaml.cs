using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.Stats;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class StatsPage : UserControl
{
    private MainViewModel? _vm;
    private AccountRow? _selected;
    private bool _suppressSelection;
    public StatsPage() => InitializeComponent();
    public void Initialize(MainViewModel vm)
    {
        _vm = vm; AccountBox.ItemsSource = vm.SavedAccounts;
        vm.SavedAccounts.CollectionChanged += OnAccountsChanged;
        SelectAccount(vm.CurrentAccount ?? vm.SavedAccounts.FirstOrDefault(), load: false);
    }
    private void OnAccountsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm != null && (_selected == null || !_vm.SavedAccounts.Contains(_selected)))
            SelectAccount(_vm.CurrentAccount ?? _vm.SavedAccounts.FirstOrDefault(), false);
    }
    public void SelectAccount(AccountRow? row, bool load = true)
    {
        if (row == null) { _selected = null; AccountBox.SelectedItem = null; ShowEmpty(); return; }
        if (!ReferenceEquals(AccountBox.SelectedItem, row))
        {
            _suppressSelection = !load;
            AccountBox.SelectedItem = row;
            _suppressSelection = false;
            _selected = row;
            RegionText.Text = row.RegionText;
            BattleTagText.Text = row.BattleTag;
            if (!load) ShowEmpty(keepSelectionText: true);
        }
        else if (load) _ = LoadAsync(row, false);
    }
    private async void OnAccountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (AccountBox.SelectedItem is AccountRow row) await LoadAsync(row, false); else ShowEmpty();
    }
    public Task LoadSelectedAsync() => _selected == null ? Task.CompletedTask : LoadAsync(_selected, false);
    public async void LoadChinaRoleId(long roleId)
    {
        EmptyPanel.Visibility = Visibility.Collapsed;
        RegionText.Text = "国服";
        var view = StatsHost.Content as StatsWindow ?? new StatsWindow();
        StatsHost.Content = view;
        await view.LoadAccountAsync(roleId);
    }
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (_selected != null) await LoadAsync(_selected, true); }
    private async Task LoadAsync(AccountRow row, bool force)
    {
        _selected = row; RegionText.Text = row.RegionText; BattleTagText.Text = row.BattleTag; EmptyPanel.Visibility = Visibility.Collapsed;
        if (row.IsCnRegion)
        {
            var view = StatsHost.Content as StatsWindow ?? new StatsWindow(); StatsHost.Content = view;
            await view.LoadAccountAsync(row.AccountId);
        }
        else
        {
            var view = StatsHost.Content as CareerWindow ?? new CareerWindow(); StatsHost.Content = view;
            await view.LoadAccountAsync(row.BattleTag, force);
        }
    }
    public static string DataSourceFor(AccountRow row) => row.IsCnRegion ? "ChinaStats" : "BlizzardCareer";
    private void ShowEmpty(bool keepSelectionText = false)
    {
        StatsHost.Content = null; EmptyPanel.Visibility = Visibility.Visible;
        if (!keepSelectionText) { RegionText.Text = "—"; BattleTagText.Text = ""; }
    }
}
