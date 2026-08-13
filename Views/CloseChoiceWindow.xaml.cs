using System.Windows;
using System.Windows.Input;

namespace CloudLightBlizzard;

public partial class CloseChoiceWindow : Window
{
    public CloseChoiceWindow() => InitializeComponent();

    /// <summary>选了「最小化到托盘」。</summary>
    public bool MinimizeToTray => OptTray.IsChecked == true;

    /// <summary>勾了「记住我的选择」。</summary>
    public bool RememberChoice => Remember.IsChecked == true;

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
