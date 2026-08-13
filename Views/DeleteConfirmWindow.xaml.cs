using System.Windows;
using System.Windows.Input;

namespace BnetSwitch;

public partial class DeleteConfirmWindow : Window
{
    /// <param name="hideMode">true = 从列表移除一个尚未保存的检测号；false = 删除账号备份。</param>
    public DeleteConfirmWindow(string accountName, bool hideMode = false)
    {
        InitializeComponent();
        NameRun.Text = accountName;

        if (hideMode)
        {
            Title = "从列表移除";
            HeadText.Text = "从列表移除";
            BodyPrefix.Text = "确定把 ";
            BodySuffix.Text = " 从列表移除吗?";
            SubText.Text = "只是从本工具列表隐藏,不影响战网、不会登出;以后再次登录该号会自动重新出现。";
            ConfirmText.Text = "移除";
        }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
