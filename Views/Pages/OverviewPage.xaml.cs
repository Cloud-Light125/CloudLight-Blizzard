using System.Windows.Controls;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.Models;
namespace CloudLightBlizzard.Views.Pages;
public partial class OverviewPage:UserControl
{
    public OverviewPage()=>InitializeComponent();
    public void Initialize(MainViewModel main,DropsViewModel drops){DataContext=main;Dashboard.Initialize(main,drops);}
    internal void UpdateAnnouncements(IReadOnlyList<Announcement> values)=>Dashboard.UpdateAnnouncements(values);
}