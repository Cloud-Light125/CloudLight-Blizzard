using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CloudLightBlizzard.ViewModels.Visuals;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CloudLightBlizzard.Controls;

public partial class CampaignInventoryView : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty=DependencyProperty.Register(nameof(ItemsSource),typeof(IEnumerable),typeof(CampaignInventoryView),new PropertyMetadata(null,SourceChanged));
    public IEnumerable? ItemsSource {get=>(IEnumerable?)GetValue(ItemsSourceProperty);set=>SetValue(ItemsSourceProperty,value);}
    public static readonly DependencyProperty IsOverviewProperty=DependencyProperty.Register(nameof(IsOverview),typeof(bool),typeof(CampaignInventoryView),new PropertyMetadata(false));
    public bool IsOverview{get=>(bool)GetValue(IsOverviewProperty);set=>SetValue(IsOverviewProperty,value);}
    public static readonly DependencyProperty CampaignActionVisibilityProperty=DependencyProperty.Register(nameof(CampaignActionVisibility),typeof(Visibility),typeof(CampaignInventoryView),new PropertyMetadata(Visibility.Visible));
    public Visibility CampaignActionVisibility{get=>(Visibility)GetValue(CampaignActionVisibilityProperty);set=>SetValue(CampaignActionVisibilityProperty,value);}
    private ScrollViewer? _outer;
    public sealed record CampaignRow(IReadOnlyList<CampaignRewardGroup> Groups);
    public ObservableCollection<CampaignRow> Rows {get;}=[];
    private static void SourceChanged(DependencyObject sender,DependencyPropertyChangedEventArgs args)
    {
        var view=(CampaignInventoryView)sender;
        if(args.OldValue is INotifyCollectionChanged old)old.CollectionChanged-=view.GroupsChanged;
        if(view.IsLoaded&&args.NewValue is INotifyCollectionChanged next)next.CollectionChanged+=view.GroupsChanged;
        view.RebuildRows();
    }
    private bool _rowsPending;
    private readonly HashSet<CampaignRewardGroup> _observed=[];
    private void GroupsChanged(object? sender,NotifyCollectionChangedEventArgs args)=>QueueRows();
    private void QueueRows()
    {
        if(_rowsPending)return;
        _rowsPending=true;
        Dispatcher.BeginInvoke(()=>{_rowsPending=false;RebuildRows();},DispatcherPriority.DataBind);
    }
    private void RebuildRows()
    {
        var groups=ItemsSource?.OfType<CampaignRewardGroup>().ToArray()??[];
        foreach(var old in _observed.Except(groups).ToArray()){old.PropertyChanged-=GroupUpdated;_observed.Remove(old);}
        if(IsLoaded)foreach(var group in groups.Where(g=>!_observed.Contains(g))){group.PropertyChanged+=GroupUpdated;_observed.Add(group);}
        var rows=new List<CampaignRow>();
        // Pair small adjacent campaigns only when both keep full-size cards and separate headers.
        var halfContent=(ActualWidth-SystemParameters.VerticalScrollBarWidth)/2-40;
        var capacity=halfContent<190?0:Math.Min(2,RewardTilePanel.ColumnsFor(halfContent));
        for(var index=0;index<groups.Length;index++)
        {
            var group=groups[index];
            if(capacity>0&&group.Cards.Count>0&&group.Cards.Count<=capacity&&index+1<groups.Length&&groups[index+1].Cards.Count>0&&groups[index+1].Cards.Count<=capacity)
                rows.Add(new(new[]{group,groups[++index]}));
            else rows.Add(new(new[]{group}));
        }
        if(Rows.Count==rows.Count&&Rows.Zip(rows).All(pair=>pair.First.Groups.SequenceEqual(pair.Second.Groups)))return;
        Rows.Clear();foreach(var row in rows)Rows.Add(row);
    }
    private void GroupUpdated(object? sender,PropertyChangedEventArgs args){if(args.PropertyName==nameof(CampaignRewardGroup.Summary))QueueRows();}
    public CampaignInventoryView()
    {
        InitializeComponent();
        // Bound the first measure too; otherwise a page StackPanel realizes the entire inventory
        // before Loaded can size the viewport (hundreds of image controls on a real account).
        Height=180;
        SizeChanged+=(_,_)=>QueueRows();
        Loaded+=(_,_)=>{if(ItemsSource is INotifyCollectionChanged source){source.CollectionChanged-=GroupsChanged;source.CollectionChanged+=GroupsChanged;}RebuildRows();for(DependencyObject? p=VisualTreeHelper.GetParent(this);p is not null;p=VisualTreeHelper.GetParent(p))if(p is ScrollViewer scroll){_outer=scroll;break;}if(_outer is not null)_outer.SizeChanged+=ViewportChanged;Dispatcher.BeginInvoke(UpdateHeight,DispatcherPriority.Loaded);};
        Unloaded+=(_,_)=>{if(_outer is not null)_outer.SizeChanged-=ViewportChanged;_outer=null;if(ItemsSource is INotifyCollectionChanged source)source.CollectionChanged-=GroupsChanged;foreach(var group in _observed)group.PropertyChanged-=GroupUpdated;_observed.Clear();};
        IsVisibleChanged+=(_,_)=>{if(IsVisible)Dispatcher.BeginInvoke(UpdateHeight,DispatcherPriority.Loaded);};
    }
    private void ViewportChanged(object sender,SizeChangedEventArgs e)=>UpdateHeight();
    private void UpdateHeight()
    {
        if(_outer is null||!IsVisible||_outer.ActualHeight<=0)return;
        // The outer page can scroll its account/filter headers away. Keep a full reading viewport
        // for the inventory so a large card remains reachable even on a short laptop window.
        Height=Math.Max(180,_outer.ActualHeight-8);
    }
}
