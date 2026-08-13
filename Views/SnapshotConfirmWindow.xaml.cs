using System.Windows;
using System.Windows.Input;

namespace CloudLightBlizzard;

public partial class SnapshotConfirmWindow : Window
{
    public SnapshotConfirmWindow(string accountName)
    {
        InitializeComponent();
        NameRun.Text = accountName;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
