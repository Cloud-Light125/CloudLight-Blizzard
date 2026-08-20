using System.Windows;
using System.Windows.Input;
using CloudLightBlizzard.Services;

namespace CloudLightBlizzard.Views;

public enum ExitChoice
{
    Cancel,
    MinimizeToTray,
    Exit,
}

public partial class ExitConfirmationDialog : Window
{
    public ExitConfirmationDialog()
    {
        InitializeComponent();
        ThemeManager.Attach(this);
    }

    public ExitChoice Choice { get; private set; } = ExitChoice.Cancel;

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnMinimizeToTray(object sender, RoutedEventArgs e)
    {
        Choice = ExitChoice.MinimizeToTray;
        DialogResult = true;
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        Choice = ExitChoice.Exit;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Choice = ExitChoice.Cancel;
        DialogResult = false;
    }
}
