using System.Windows.Controls;
using CloudLightBlizzard.ViewModels;

namespace CloudLightBlizzard.Views.Pages;

public partial class AboutPage : UserControl
{
    public AboutPage() => InitializeComponent();
    public void Initialize(MainViewModel main) => DataContext = main;
}
