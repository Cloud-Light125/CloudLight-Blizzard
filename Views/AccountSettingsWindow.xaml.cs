using System.Windows;
using System.Windows.Controls;
using BnetSwitch.Models;
using BnetSwitch.ViewModels;

namespace BnetSwitch;

public partial class AccountSettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AccountRow _row;

    public AccountSettingsWindow(MainViewModel vm, AccountRow row)
    {
        InitializeComponent();
        _vm = vm;
        _row = row;
        BattleTagText.Text = row.BattleTag;
        CustomNameBox.Text = row.CustomName;
        RemarkBox.Text = row.Remark;
        RegionBox.SelectedIndex = (int)row.RegionOverride;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var region = RegionBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
                     Enum.TryParse<AccountRegionOverride>(tag, out var parsed)
            ? parsed : AccountRegionOverride.Auto;
        _vm.SaveAccountPreference(_row, CustomNameBox.Text, RemarkBox.Text, region);
        DialogResult = true;
    }
}
