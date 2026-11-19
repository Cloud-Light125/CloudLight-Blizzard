using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using CloudLightBlizzard.Controls;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.ViewModels.Visuals;

namespace CloudLightBlizzard.Views.Drops.Soop;

public sealed class SoopGameView(SoopGame source,Action changed) : ViewModels.ObservableObject
{
    public SoopGame Source => source;
    public string Name => source.Name;
    public string Counts => $"{source.Campaigns.Count} 个活动 · {source.Campaigns.Count(c => c.Lifecycle == "active")} 个进行中";
    public bool Selected {get=>source.Selected;set=>source.Selected=value;}
    private bool _expanded;
    public bool Expanded {get=>_expanded;set {if(_expanded==value)return;_expanded=value;Raise();changed();}}

    internal void Notify()=>Raise("");
}

public partial class SoopCampaignPanel : UserControl
{
    public static readonly DependencyProperty ModelProperty=DependencyProperty.Register(nameof(Model),typeof(SoopCampaignModel),typeof(SoopCampaignPanel),new PropertyMetadata(null,Changed));
    public static readonly DependencyProperty EditableProperty=DependencyProperty.Register(nameof(Editable),typeof(bool),typeof(SoopCampaignPanel),new PropertyMetadata(false,(d,e)=>d.SetValue(EditVisibilityProperty,(bool)e.NewValue?Visibility.Visible:Visibility.Collapsed)));
    public static readonly DependencyProperty EditVisibilityProperty=DependencyProperty.Register(nameof(EditVisibility),typeof(Visibility),typeof(SoopCampaignPanel),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty HeadingProperty=DependencyProperty.Register(nameof(Heading),typeof(string),typeof(SoopCampaignPanel),new PropertyMetadata("活动与奖励"));
    public static readonly DependencyProperty ListHeightProperty=DependencyProperty.Register(nameof(ListHeight),typeof(double),typeof(SoopCampaignPanel),new PropertyMetadata(480d));
    public SoopCampaignModel? Model {get=>(SoopCampaignModel?)GetValue(ModelProperty);set=>SetValue(ModelProperty,value);}
    public bool Editable {get=>(bool)GetValue(EditableProperty);set=>SetValue(EditableProperty,value);}
    public string Heading {get=>(string)GetValue(HeadingProperty);set=>SetValue(HeadingProperty,value);}
    public double ListHeight {get=>(double)GetValue(ListHeightProperty);set=>SetValue(ListHeightProperty,value);}
    public Visibility EditVisibility=>(Visibility)GetValue(EditVisibilityProperty);
    private readonly Dictionary<string,SoopGameView> _games=[];
    private readonly ObservableCollection<object> _visible=[];
    private readonly DispatcherTimer _searchDelay=new(){Interval=TimeSpan.FromMilliseconds(180)};
    private bool _refreshing,_subscribed,_initialized;
    private Point _dragOrigin;
    private Window? _owner;
    private bool _autoHeight;
    public FrameworkElement? PageFooter { get; set; }
    internal ScrollViewer? ActivityScroll { get; private set; }
    internal void ScrollToTop() => ActivityScroll?.ScrollToTop();
    internal IReadOnlyList<SoopGameView> VisibleGames => _visible.OfType<SoopGameView>().ToArray();
    public SoopCampaignPanel()
    {
        InitializeComponent();GameList.ItemsSource=_visible;
        AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnGameWheel), handledEventsToo: true);
        _searchDelay.Tick+=(_,_)=>{_searchDelay.Stop();Refresh();};
        Loaded += (_, _) =>
        {
            Subscribe();
            if (PageFooter is not null)
            {
                if (LayoutRoot.Children.Contains(Filters))
                {
                    LayoutRoot.Children.Remove(Filters);
                    LayoutRoot.Children.Remove(Empty);
                    Grid.SetRow(GameList, 0);
                    Grid.SetRowSpan(GameList, 3);
                    GameList.Height = double.NaN;
                    GameList.ApplyTemplate();
                    ActivityScroll = GameList.Template.FindName("PageScroll", GameList) as ScrollViewer;
                }
            }
            else
            {
                _autoHeight = ReadLocalValue(ListHeightProperty) == DependencyProperty.UnsetValue;
                _owner = Window.GetWindow(this);
                if (_owner is not null) _owner.SizeChanged += OnWindowSize;
                ResizeList();
            }
            Refresh();
        };
        Unloaded+=(_,_)=>{if(_subscribed&&Model is not null)Model.Updated-=Refresh;_subscribed=false;_searchDelay.Stop();if(_owner is not null)_owner.SizeChanged-=OnWindowSize;_owner=null;};
        PreviewMouseLeftButtonDown+=(_,e)=>_dragOrigin=e.GetPosition(this);
        AddHandler(WorkspaceAction.RequestEvent,new EventHandler<WorkspaceActionEventArgs>(OnRewardAction));
    }
    private void OnGameWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
        if (ActivityScroll is { } activities)
        {
            e.Handled = true;
            activities.ScrollToVerticalOffset(activities.VerticalOffset - e.Delta);
            return;
        }
        // The embedded priority list gives the workspace first use of the wheel.
        // At the outer edge its virtualized list can also scroll.
        if (!GameList.IsMouseOver && !ReferenceEquals(e.OriginalSource, GameList)) return;
        DependencyObject? parent = this;
        while ((parent = VisualTreeHelper.GetParent(parent)) is not null)
        {
            if (parent is not ScrollViewer page) continue;
            var canScroll = e.Delta > 0 ? page.VerticalOffset > 0 : page.VerticalOffset < page.ScrollableHeight;
            if (canScroll)
            {
                e.Handled = true;
                page.ScrollToVerticalOffset(page.VerticalOffset - e.Delta);
            }
            return;
        }
    }
    private void OnWindowSize(object sender,SizeChangedEventArgs e)=>ResizeList();
    private void ResizeList(){if(_autoHeight&&_owner is not null)SetCurrentValue(ListHeightProperty,Math.Clamp(_owner.ActualHeight-430,260,660));}
    private void Subscribe(){if(!_subscribed&&Model is not null){Model.Updated+=Refresh;_subscribed=true;}}
    private static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        var panel=(SoopCampaignPanel)d;
        if(e.OldValue is SoopCampaignModel old&&panel._subscribed)old.Updated-=panel.Refresh;
        panel._subscribed=false;panel._games.Clear();panel._initialized=false;
        if(panel.IsLoaded)panel.Subscribe();panel.Refresh();
    }
    private void OnSearch(object sender,RoutedEventArgs e){_searchDelay.Stop();_searchDelay.Start();}
    private void OnFilter(object sender,RoutedEventArgs e)=>Refresh();
    internal void Refresh()
    {
        if(GameList is null||Model is null||_refreshing)return;
        _refreshing=true;
        try
        {
            var search=Search.Text.Trim();var views=new List<object>();
            foreach(var game in Model.Games.OrderBy(g=>g.Selected?0:1))
            {
                if(Scope.SelectedIndex==1&&!game.Selected||Scope.SelectedIndex==2&&!game.Campaigns.Any(c=>c.Available))continue;
                var campaigns=game.Campaigns.Where(c=>(search.Length==0||game.Name.Contains(search,StringComparison.OrdinalIgnoreCase)||c.Title.Contains(search,StringComparison.OrdinalIgnoreCase)||c.Id.Contains(search,StringComparison.OrdinalIgnoreCase))&&
                    (Lifecycle.SelectedIndex<=0||c.Lifecycle==new[]{"","active","notYetOpen","ended"}[Lifecycle.SelectedIndex])&&
                    (Kind.SelectedIndex<=0||(Kind.SelectedIndex==1?c.Random:!c.Random))).ToArray();
                if(campaigns.Length==0)continue;
                if(!_games.TryGetValue(game.Id,out var view))_games[game.Id]=view=new(game,Refresh);
                // Open the first visible game once; the rest remain lazy until explicitly expanded.
                if(!_initialized&&views.Count==0)view.Expanded=true;
                view.Notify();views.Add(view);
                if(view.Expanded)views.AddRange(campaigns);
            }
            if(views.Count>0)_initialized=true;
            Empty.Visibility=views.Count==0?Visibility.Visible:Visibility.Collapsed;
            if (PageFooter is not null && !LayoutRoot.Children.Contains(Filters))
            {
                views.Insert(0, Filters);
                if (Empty.Visibility == Visibility.Visible) views.Add(Empty);
                views.Add(PageFooter);
            }
            SoopCampaignModel.Sync(_visible,views);
        }
        finally{_refreshing=false;}
    }
    private void OnExpandGame(object sender,RoutedEventArgs e){if(sender is FrameworkElement{DataContext:SoopGameView view})view.Expanded=!view.Expanded;}
    private void OnExpandAll(object sender,RoutedEventArgs e){foreach(var view in VisibleGames.ToArray())view.Expanded=true;Refresh();}
    private void OnCollapseAll(object sender,RoutedEventArgs e){foreach(var view in _games.Values)view.Expanded=false;Refresh();}
    internal void ExpandGame(string id){if(_games.TryGetValue(id,out var view))view.Expanded=true;}
    private void OnMove(object sender,RoutedEventArgs e)
    {if(Editable&&sender is Button{DataContext:{} item} button){Model?.Move(item is SoopGameView view?view.Source:item,int.Parse(button.Tag.ToString()!));Refresh();}}
    private void OnDrag(object sender,MouseEventArgs e)
    {
        if(!Editable||e.LeftButton!=MouseButtonState.Pressed||sender is not FrameworkElement element)return;
        var point=e.GetPosition(this);
        if(Math.Abs(point.X-_dragOrigin.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-_dragOrigin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        if(e.OriginalSource is not Border&&e.OriginalSource is not TextBlock)return;
        var item=element.DataContext is SoopGameView view?(object)view.Source:element.DataContext;
        if(item is SoopGame or SoopCampaign){e.Handled=true;DragDrop.DoDragDrop(element,new DataObject("SoopQueue",item),DragDropEffects.Move);}
    }
    private void OnDrop(object sender,DragEventArgs e)
    {
        if(!Editable||Model is null||sender is not FrameworkElement element)return;
        var source=e.Data.GetData("SoopQueue");var target=element.DataContext is SoopGameView view?(object)view.Source:element.DataContext;
        if(source is SoopGame a&&target is SoopGame b)Model.Move(a,Model.Games.IndexOf(b)-Model.Games.IndexOf(a));
        if(source is SoopCampaign x&&target is SoopCampaign y&&x.GameId==y.GameId){var list=Model.Games.First(g=>g.Id==x.GameId).Campaigns;Model.Move(x,list.IndexOf(y)-list.IndexOf(x));}
        e.Handled=true;Refresh();
    }
    private void OnCampaignDetails(object sender,RoutedEventArgs e)
    {if(sender is FrameworkElement{DataContext:SoopCampaign campaign})ShowDetails(campaign,null);}
    private void OnRewardAction(object? sender,WorkspaceActionEventArgs e)
    {
        if(e.Action!="reward:details"||e.Control is not FrameworkElement{DataContext:RewardVisual{Brand:"Soop"} reward})return;
        e.Handled=true;
        // Identity includes the UID; a recycled row can never act on another account's reward.
        if(Model?.Account!=reward.AccountId)return;
        var campaign=Model.Games.SelectMany(g=>g.Campaigns).FirstOrDefault(c=>c.Id==reward.CampaignId&&c.AccountId==reward.AccountId);
        if(campaign is not null)ShowDetails(campaign,reward);
    }
    internal Window CreateDetails(SoopCampaign campaign,RewardVisual? reward)
    {
        var stack = new StackPanel { Margin = new Thickness(20) };
        var heading = new TextBlock { Text = campaign.Title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,8) };
        heading.SetResourceReference(StyleProperty, "SectionTitle");
        stack.Children.Add(heading);
        var metadata = new TextBlock { Text = $"{campaign.DisplayType} · {campaign.Status}\n{campaign.Details}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) };
        metadata.SetResourceReference(StyleProperty, "VisualCaption");
        stack.Children.Add(metadata);
        var rewards = reward is null ? campaign.Rewards.Select(r => r.Visual) : [reward];
        foreach (var item in rewards)
        {
            stack.Children.Add(new RewardCard
            {
                Reward = item, Title = item.Title, ImageUrl = item.ImageUrl, Compact = true,
                DetailsVisibility = Visibility.Collapsed, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0,0,0,8),
            });
            stack.Children.Add(new TextBlock
            {
                Text = item.Claimed ? "已领取" : item.CanClaim ? "官方标记可领取" : "领取状态以账号背包为准",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12),
            });
        }
        var dialog=new Window{Owner=Window.GetWindow(this),FontFamily=new System.Windows.Media.FontFamily("Microsoft YaHei UI, Segoe UI"),Title="SOOP · "+(reward is null?"活动详情":"奖励详情"),Width=640,Height=640,MaxWidth=SystemParameters.WorkArea.Width-40,MaxHeight=SystemParameters.WorkArea.Height-40,Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},WindowStartupLocation=WindowStartupLocation.CenterOwner};
        dialog.SetResourceReference(Window.BackgroundProperty,"App.Background");dialog.SetResourceReference(Window.ForegroundProperty,"Text.Primary");ThemeManager.Attach(dialog);return dialog;
    }
    private void ShowDetails(SoopCampaign campaign,RewardVisual? reward)=>CreateDetails(campaign,reward).ShowDialog();
}
