using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.ViewModels.Visuals;

namespace CloudLightBlizzard.Views.Drops.Twitch;

public partial class CampaignPriorityWindow : Window
{
    private sealed record GameChoice(string Id, string Name)
    {
        public override string ToString()=>Name;
    }
    public sealed record PriorityEntry(string mode, string[] campaignIds);
    public sealed class Row : ObservableObject, IDisposable
    {
        public CampaignRewardGroup Campaign { get; }
        private bool _watching;
        public string State => Campaign.Status + (_watching ? " · 当前观看" : " · 等待调度");
        public Row(CampaignRewardGroup campaign,bool watching)
        {
            Campaign=campaign;_watching=watching;Campaign.PropertyChanged+=CampaignChanged;
        }
        private void CampaignChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(Campaign.Status))Raise(nameof(State));}
        internal void SetWatching(bool value){_watching=value;Raise(nameof(State));}
        public void Dispose()=>Campaign.PropertyChanged-=CampaignChanged;
        private int _rank;
        public int Rank { get => _rank; set => Set(ref _rank, value); }
    }
    private readonly TwitchInventoryModel _model;
    private readonly Func<Dictionary<string,PriorityEntry>,Task> _save;
    private readonly Dictionary<string,PriorityEntry> _draft = new(StringComparer.Ordinal);
    private readonly ObservableCollection<Row> _rows = [];
    private bool _loading, _dirty, _saving;
    private Point _dragStart;
    private Row? _dragRow;
    private string GameId => (GamePicker.SelectedItem as GameChoice)?.Id ?? "";
    public CampaignPriorityWindow(TwitchInventoryModel model, Func<Dictionary<string,PriorityEntry>,Task> save)
    {
        _model=model; _save=save; InitializeComponent(); ThemeManager.Attach(this);
        MaxHeight=SystemParameters.WorkArea.Height-32;MaxWidth=SystemParameters.WorkArea.Width-32;
        CampaignList.ItemsSource=_rows;
        GamePicker.ItemsSource=model.AllCampaigns.Where(c=>!c.Expired&&c.GameId.Length>0)
            .GroupBy(c=>c.GameId).Select(g=>new GameChoice(g.Key,g.First().Game)).OrderBy(g=>g.Name).ToArray();
        GamePicker.SelectedIndex=0;
        _model.PropertyChanged+=RuntimeChanged;
        Closed+=(_,_)=>{_model.PropertyChanged-=RuntimeChanged;foreach(var row in _rows)row.Dispose();};
        if(GamePicker.Items.Count==0)SaveStatus.Text="暂无有效活动，请连接 Twitch 并刷新活动。";
        Closing+=(_,e)=>
        {
            if(_saving){e.Cancel=true;return;}
            if(_dirty&&MessageBox.Show(this,"有未保存的优先级修改，是否放弃？","未保存修改",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)e.Cancel=true;
        };
    }
    private void RuntimeChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(TwitchInventoryModel.CurrentDrop) or nameof(TwitchInventoryModel.HasWatchEvidence))
            foreach(var row in _rows)row.SetWatching(_model.HasWatchEvidence&&_model.CurrentDrop?.CampaignId==row.Campaign.Id);
    }
    private PriorityEntry Entry(string id)
    {
        if(_draft.TryGetValue(id,out var entry))return entry;
        if(_model.WatchPriorities.ValueKind==JsonValueKind.Object&&_model.WatchPriorities.TryGetProperty(id,out var source))
        {
            var mode=source.TryGetProperty("mode",out var m)?m.GetString()??"ending":"ending";
            var ids=source.TryGetProperty("campaignIds",out var a)?a.EnumerateArray().Select(x=>x.GetString()??"").ToArray():[];
            return new(mode,ids);
        }
        return new("ending",[]);
    }
    private void GameChanged(object sender,SelectionChangedEventArgs e)
    {
        if(GameId.Length==0)return;
        _loading=true;
        var entry=Entry(GameId); ModePicker.SelectedValue=entry.mode;
        var campaigns=_model.AllCampaigns.Where(c=>c.GameId==GameId&&!c.Expired).OrderBy(c=>c.SortEnd).ThenBy(c=>c.Id).ToArray();
        if(entry.mode=="custom")campaigns=campaigns.OrderBy(c=>Array.IndexOf(entry.campaignIds,c.Id) is var i&&i>=0?i:int.MaxValue).ToArray();
        foreach(var row in _rows)row.Dispose();
        _rows.Clear();foreach(var campaign in campaigns)_rows.Add(new(campaign,_model.HasWatchEvidence&&_model.CurrentDrop?.CampaignId==campaign.Id));
        AutoOrder(entry.mode);Renumber();_loading=false;
    }
    private void Renumber(){for(var i=0;i<_rows.Count;i++)_rows[i].Rank=i+1;}
    private void AutoOrder(string mode)
    {
        var ordered=(mode switch
        {
            "remaining"=>_rows.OrderBy(r=>Remaining(r.Campaign)),
            "progress_high"=>_rows.OrderByDescending(r=>r.Campaign.Percent),
            "progress_low"=>_rows.OrderBy(r=>r.Campaign.Percent),
            "ending"=>_rows.OrderBy(r=>r.Campaign.SortEnd),
            _=>_rows.OrderBy(r=>r.Rank)
        }).ToArray();
        _rows.Clear();foreach(var row in ordered)_rows.Add(row);Renumber();
    }
    private static double Remaining(CampaignRewardGroup c)=>c.RemainingMinutes;
    private void MarkDirty()
    {
        if(_loading||GameId.Length==0)return;
        var old=Entry(GameId);
        // Keep IDs for temporarily missing campaigns as well as future extension fields in Worker merge.
        _draft[GameId]=new(ModePicker.SelectedValue as string??"ending",_rows.Select(r=>r.Campaign.Id).Concat(old.campaignIds.Where(id=>_rows.All(r=>r.Campaign.Id!=id))).ToArray());
        _dirty=true;SaveStatus.Text="有未保存修改。保存后在安全调度点生效；频道切换有约 2 分钟冷却。";
    }
    private void ModeChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_loading||_saving)return;
        AutoOrder(ModePicker.SelectedValue as string??"ending");MarkDirty();
    }
    private void Move(int destination)
    {
        if(_saving)return;
        var index=CampaignList.SelectedIndex;
        if(index<0||destination<0||destination>=_rows.Count||index==destination)return;
        _loading=true;ModePicker.SelectedValue="custom";_rows.Move(index,destination);Renumber();_loading=false;
        CampaignList.SelectedIndex=destination;CampaignList.ScrollIntoView(_rows[destination]);MarkDirty();
    }
    private void MoveUp(object s,RoutedEventArgs e)=>Move(CampaignList.SelectedIndex-1);
    private void MoveDown(object s,RoutedEventArgs e)=>Move(CampaignList.SelectedIndex+1);
    private void MoveTop(object s,RoutedEventArgs e)=>Move(0);
    private void RestoreDefault(object s,RoutedEventArgs e){if(_saving)return;ModePicker.SelectedValue="ending";AutoOrder("ending");MarkDirty();}
    private void ListKeyDown(object s,KeyEventArgs e)
    {
        if(Keyboard.Modifiers==ModifierKeys.Alt&&e.Key==Key.System)
        {if(e.SystemKey==Key.Up){Move(CampaignList.SelectedIndex-1);e.Handled=true;}else if(e.SystemKey==Key.Down){Move(CampaignList.SelectedIndex+1);e.Handled=true;}}
        if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.Home){Move(0);e.Handled=true;}
    }
    private void DragStarted(object s,MouseButtonEventArgs e)
    {
        _dragStart=e.GetPosition(CampaignList);
        _dragRow=(ItemsControl.ContainerFromElement(CampaignList,e.OriginalSource as DependencyObject) as ListBoxItem)?.DataContext as Row;
    }
    private void DragMoved(object s,MouseEventArgs e)
    {
        var point=e.GetPosition(CampaignList);
        if(_dragRow is not null&&e.LeftButton==MouseButtonState.Pressed&&(Math.Abs(point.Y-_dragStart.Y)>SystemParameters.MinimumVerticalDragDistance||Math.Abs(point.X-_dragStart.X)>SystemParameters.MinimumHorizontalDragDistance))
        {DragDrop.DoDragDrop(CampaignList,_dragRow,DragDropEffects.Move);_dragRow=null;}
    }
    private void Dropped(object s,DragEventArgs e)
    {
        if(e.Data.GetData(typeof(Row)) is not Row row||!_rows.Contains(row))return;
        var target=(ItemsControl.ContainerFromElement(CampaignList,e.OriginalSource as DependencyObject) as ListBoxItem)?.DataContext as Row;
        CampaignList.SelectedItem=row;Move(target is null?_rows.Count-1:_rows.IndexOf(target));e.Handled=true;
    }
    private async void SaveClicked(object s,RoutedEventArgs e)
    {
        if(_saving||GameId.Length==0)return;
        MarkDirty();_saving=true;SaveButton.IsEnabled=false;GamePicker.IsEnabled=false;ModePicker.IsEnabled=false;CampaignList.IsEnabled=false;
        try{await _save(new(_draft));_dirty=false;SaveStatus.Text="优先级已保存，后台将在安全调度点使用新顺序。";}
        catch(Exception){SaveStatus.Text="保存失败，修改仍保留。请检查 Twitch 后台状态后重试。";}
        finally{_saving=false;SaveButton.IsEnabled=true;GamePicker.IsEnabled=true;ModePicker.IsEnabled=true;CampaignList.IsEnabled=true;}
    }
    private void CloseClicked(object s,RoutedEventArgs e)=>Close();
}
