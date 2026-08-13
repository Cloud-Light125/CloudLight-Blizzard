using System.Windows;
using System.Windows.Input;

namespace BnetSwitch;

public partial class LoginNewWindow : Window
{
    public LoginNewWindow() => InitializeComponent();

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
