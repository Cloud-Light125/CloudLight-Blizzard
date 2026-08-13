using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class AccountsPage : UserControl
{
    private MainViewModel? _vm;
    public event Action<AccountRow>? OpenStatsRequested;

    public AccountsPage() => InitializeComponent();
    public void Initialize(MainViewModel vm) { _vm = vm; DataContext = vm; }

    private async void OnLaunchClient(object sender, RoutedEventArgs e) { if (_vm != null) await _vm.LaunchClientAsync(); }
    private async void OnSaveCurrent(object sender, RoutedEventArgs e)
    {
        if (_vm?.CurrentAccount is not { } account) return;
        if (new SnapshotConfirmWindow(account.BattleTag) { Owner = Window.GetWindow(this) }.ShowDialog() == true)
            await _vm.SaveCurrentAsync();
    }
    private async void OnAddAccount(object sender, RoutedEventArgs e)
    {
        if (_vm != null && new LoginNewWindow { Owner = Window.GetWindow(this) }.ShowDialog() == true)
            await _vm.AddAccountAsync();
    }
    private async void OnSwitchCard(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not FrameworkElement { DataContext: AccountRow row }) return;
        if (row.IsExpired)
        {
            var ok = MessageBox.Show($"「{row.BattleTag}」需要重新登录 Battle.net。\n\n现在关闭 Battle.net 并回到登录页，登录成功后请更新账号备份。",
                "重新登录", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok == MessageBoxResult.OK) await _vm.ReloginAsync(row);
        }
        else await _vm.SwitchToAsync(row);
    }
    private void OnStats(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: AccountRow row }) OpenStatsRequested?.Invoke(row); }
    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (_vm != null && sender is FrameworkElement { DataContext: AccountRow row })
            new AccountSettingsWindow(_vm, row) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_vm != null && sender is FrameworkElement { DataContext: AccountRow row } &&
            new DeleteConfirmWindow(row.BattleTag) { Owner = Window.GetWindow(this) }.ShowDialog() == true)
            await _vm.DeleteProfileAsync(row);
    }
    private void OnHide(object sender, RoutedEventArgs e)
    {
        if (_vm != null && sender is FrameworkElement { DataContext: AccountRow row } &&
            new DeleteConfirmWindow(row.BattleTag, true) { Owner = Window.GetWindow(this) }.ShowDialog() == true) _vm.HideAccount(row);
    }
}
