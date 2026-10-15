using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class AccountsPage : UserControl
{
    private MainViewModel? _vm;
    public event Action<AccountRow>? OpenStatsRequested;

    public AccountsPage() => InitializeComponent();
    public void Initialize(MainViewModel vm) { _vm = vm; DataContext = vm; }

    private void OnOpenSnapshots(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        try
        {
            Directory.CreateDirectory(_vm.AccountSnapshotPath);
            Process.Start(new ProcessStartInfo { FileName = _vm.AccountSnapshotPath, UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show("无法打开账号快照目录：" + ex.Message + "\n" + _vm.AccountSnapshotPath, "账号快照"); }
    }

    private async void OnLaunchClient(object sender, RoutedEventArgs e) { if (_vm != null) await _vm.LaunchClientAsync(); }
    private async void OnSaveCurrent(object sender, RoutedEventArgs e)
    {
        if (_vm?.CurrentAccount is not { } account)
        {
            MessageBox.Show("请先在 Battle.net 登录该账号，刷新列表后保存账号快照。", "尚未识别当前账号");
            return;
        }
        var confirmation = new SnapshotConfirmWindow(account.BattleTag) { Owner = Window.GetWindow(this) };
        if (confirmation.ShowDialog() == true)
            await _vm.SaveCurrentAsync(confirmation.ClientLoginConfirmed);
    }
    private async void OnAddAccount(object sender, RoutedEventArgs e)
    {
        if (_vm != null && new LoginNewWindow { Owner = Window.GetWindow(this) }.ShowDialog() == true)
            await _vm.AddAccountAsync();
    }
    private async void OnSwitchCard(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not FrameworkElement { DataContext: AccountRow row }) return;
        if (row.NeedsRelogin)
        {
            var ok = MessageBox.Show($"「{row.BattleTag}」需要重新登录 Battle.net。\n{row.ProfileDetail}\n\n现在关闭 Battle.net 并请求登录页，请手动选择该账号。登录完成后点击“更新账号备份”保存；此操作不会自动保存。",
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
