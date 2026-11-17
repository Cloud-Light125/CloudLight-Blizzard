using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CloudLightBlizzard.Controls;

public partial class CampaignInventoryView : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty=DependencyProperty.Register(nameof(ItemsSource),typeof(IEnumerable),typeof(CampaignInventoryView));
    public IEnumerable? ItemsSource {get=>(IEnumerable?)GetValue(ItemsSourceProperty);set=>SetValue(ItemsSourceProperty,value);}
    public static readonly DependencyProperty IsOverviewProperty=DependencyProperty.Register(nameof(IsOverview),typeof(bool),typeof(CampaignInventoryView),new PropertyMetadata(false));
    public bool IsOverview{get=>(bool)GetValue(IsOverviewProperty);set=>SetValue(IsOverviewProperty,value);}
    private ScrollViewer? _outer;
    public CampaignInventoryView()
    {
        InitializeComponent();
        Loaded+=(_,_)=>{for(DependencyObject? p=VisualTreeHelper.GetParent(this);p is not null;p=VisualTreeHelper.GetParent(p))if(p is ScrollViewer scroll){_outer=scroll;break;}if(_outer is not null)_outer.SizeChanged+=ViewportChanged;Dispatcher.BeginInvoke(UpdateHeight,DispatcherPriority.Loaded);};
        Unloaded+=(_,_)=>{if(_outer is not null)_outer.SizeChanged-=ViewportChanged;_outer=null;};
        IsVisibleChanged+=(_,_)=>{if(IsVisible)Dispatcher.BeginInvoke(UpdateHeight,DispatcherPriority.Loaded);};
    }
    private void ViewportChanged(object sender,SizeChangedEventArgs e)=>UpdateHeight();
    private void UpdateHeight()
    {
        if(_outer is null||!IsVisible||_outer.ActualHeight<=0)return;
        var top=TranslatePoint(new Point(0,0),_outer).Y+_outer.VerticalOffset;
        Height=Math.Max(180,_outer.ActualHeight-Math.Max(0,top)-8);
    }
}
