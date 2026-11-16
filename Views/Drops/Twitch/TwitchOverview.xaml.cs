using System.Windows.Controls;
using System.Windows;
namespace CloudLightBlizzard.Views.Drops.Twitch;
public partial class TwitchOverview:UserControl
{
    public TwitchOverview()=>InitializeComponent();
    internal void ApplyCompact(bool compact)
    {
        LoginIllustration.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        LoginHeadline.Margin=compact?new Thickness(0,8,0,8):new Thickness(0,24,0,10);
        TwitchLoginAction.Margin=compact?new Thickness(0,12,0,12):new Thickness(0,24,0,16);
    }
}
