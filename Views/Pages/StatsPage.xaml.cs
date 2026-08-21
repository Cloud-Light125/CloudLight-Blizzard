using System.ComponentModel;
using System.Windows.Controls;
using CloudLightBlizzard.Stats;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class StatsPage : UserControl
{
    private readonly CareerWindow _careerView = new();
    private MainViewModel? _vm;
    private AccountRow? _selected;

    public AccountRow? SelectedStatsAccount => _selected;

    public StatsPage()
    {
        InitializeComponent();
        CareerHost.Content = _careerView;
        _careerView.PrepareAccount("");
    }

    public void Initialize(MainViewModel vm)
    {
        _vm = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        SelectAccount(vm.CurrentAccount ?? vm.SavedAccounts.FirstOrDefault(account => !account.IsCnRegion));
    }

    public void OnPageOpened()
    {
        if (_selected is null && _vm is not null)
            SelectAccount(_vm.CurrentAccount ?? _vm.SavedAccounts.FirstOrDefault(account => !account.IsCnRegion));
    }

    public void SelectAccount(AccountRow? row)
    {
        _selected = row;
        _careerView.PrepareAccount(row is { IsCnRegion: false } ? row.BattleTag : "");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentAccount) && _vm?.CurrentAccount is { } current)
            Dispatcher.BeginInvoke(new Action(() => SelectAccount(current)));
    }
}
