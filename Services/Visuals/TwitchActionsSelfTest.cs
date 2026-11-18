using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudLightBlizzard.Controls;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.ViewModels.Visuals;
using CloudLightBlizzard.Views.Drops.Twitch;

namespace CloudLightBlizzard.Services.Visuals;

internal static class TwitchActionsSelfTest
{
    internal static async Task RunAsync(Action<bool,string> check,string output)
    {
        var listener=new BindingErrors();PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try
        {
            JsonElement State(bool claimed=false)=>JsonSerializer.SerializeToElement(new
            {
                settings=new{campaign_watch_priority=new Dictionary<string,object>{{"1",new{mode="custom",campaignIds=new[]{"b","a","c"}}}},campaign_display_sort="ending"},
                campaigns=new[]{new{id="a",name="Reign of Talon S5 Mid · 模拟",game="Overwatch",gameId="1",active=true,available=false,eligible=true,startsAt=DateTimeOffset.UtcNow.AddDays(-1),endsAt=DateTimeOffset.UtcNow.AddDays(3),remainingMinutes=0,progress=1.0},
                    new{id="b",name="OWCS Stage 3 Playoffs · 模拟",game="Overwatch",gameId="1",active=true,available=true,eligible=true,startsAt=DateTimeOffset.UtcNow.AddDays(-1),endsAt=DateTimeOffset.UtcNow.AddDays(2),remainingMinutes=100,progress=.2},
                    new{id="c",name="很长的第三个活动名称用于检查自动截断和完整提示 · 模拟",game="Overwatch",gameId="1",active=true,available=true,eligible=true,startsAt=DateTimeOffset.UtcNow.AddDays(-1),endsAt=DateTimeOffset.UtcNow.AddDays(4),remainingMinutes=30,progress=.5}},
                inventory=new[]{new{id="river",campaignId="a",name="River Spray · 模拟",game="Overwatch",currentMinutes=30,requiredMinutes=30,completed=true,canClaim=!claimed,claimed},
                    new{id="fika",campaignId="a",name="Lounging Fika Spray · 模拟",game="Overwatch",currentMinutes=60,requiredMinutes=60,completed=true,canClaim=!claimed,claimed},
                    new{id="unfinished",campaignId="b",name="未完成奖励 · 模拟",game="Overwatch",currentMinutes=5,requiredMinutes=100,completed=false,canClaim=false,claimed=false}}
            });
            var model=new TwitchInventoryModel(new(DropsPlatform.Twitch,"Twitch"));model.Apply(State());
            var group=model.AllCampaigns.Single(c=>c.Id=="a");
            var overview=model.OverviewGroups.Single(c=>c.Id=="a");
            var action=group.Drops[0].ClaimAction!;
            check(model.Groups.Any(g=>g.Id=="a"),"fully watched campaigns remain visible for pending claims in the default activity scope");
            check(ReferenceEquals(group,overview)&&ReferenceEquals(action,overview.Cards[0].Drop.ClaimAction),"overview and activities share the same claim action and campaign instance");
            check(action.CanExecute&&action.Visibility==Visibility.Visible&&model.AllCampaigns.Single(c=>c.Id=="b").Drops.Single().ClaimAction?.Visibility==Visibility.Collapsed,"only completed official claimable rewards expose the claim button");
            check(action.Begin()&&!action.Begin()&&!action.CanExecute&&overview.Cards[0].Drop.ClaimAction?.ButtonText=="领取中…","repeat click is rejected and the other page immediately shows claiming");
            action.Finish("模拟断线：结果待确认");
            check(action.CanExecute&&action.MessageVisibility==Visibility.Visible,"a failed or uncertain claim retains its retry action and reason");
            action.Finish("领取结果待确认",true);
            check(!action.CanExecute&&action.ButtonText=="领取结果待确认"&&!action.Busy,"an ambiguous claim disables replay until an official inventory refresh");
            action.Update(group.Drops[0] with{SyncAt=DateTimeOffset.UtcNow.ToString("o")});
            check(action.CanExecute,"a new official inventory sample permits an explicit user retry");
            check(model.BeginBatchClaim()&&!model.BeginBatchClaim()&&!model.CanClaimBatch&&model.CanCancelBatch,"one shared batch state rejects a second page starting another batch");
            model.ApplyBatchClaim(JsonSerializer.SerializeToElement(new{total=6,success=1,skipped=1,failed=1,unknown=1,pending=2,current="模拟奖励"}));
            check(model.BatchClaimSummary.Contains("待确认 1")&&model.BatchClaimSummary.Contains("未执行 2")&&model.BatchClaimSummary.Contains("模拟奖励"),"batch summary distinguishes unknown, unsubmitted and current reward");
            model.EndBatchClaim();
            action.Finish("模拟失败：请核对官方库存");
            var content=new CampaignInventoryView{ItemsSource=new[]{group},Height=520,VerticalAlignment=VerticalAlignment.Top};
            var window=new Window{Title="掉宝领取 · 隔离模拟验收",Width=540,Height=600,ShowInTaskbar=false,ShowActivated=false,Content=content};
            ThemeManager.Apply(false);window.Show();await Idle();
            var buttons=Descendants<Button>(window).Where(b=>WorkspaceAction.GetName(b)=="twitch:claim").ToArray();
            check(buttons.Length==2&&buttons.All(b=>b.IsVisible&&b.IsEnabled&&b.ActualWidth>20),"real WPF reward cards show both claim buttons in a small window");
            string? routed=null;
            window.AddHandler(WorkspaceAction.RequestEvent,new EventHandler<WorkspaceActionEventArgs>((_,e)=>{routed=e.Action;e.Handled=true;}));
            var official=Descendants<Button>(window).First(b=>WorkspaceAction.GetName(b)=="legacy:OnOpenTwitchInventory");
            official.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(routed=="legacy:OnOpenTwitchInventory"&&official.IsVisible,"failed native claims route the official Inventory fallback through the existing browser action");
            Snapshot(window,Path.Combine(output,"claim-light.png"));ThemeManager.Apply(true);await Idle();Snapshot(window,Path.Combine(output,"claim-dark.png"));
            model.Apply(State(true));await Idle();
            check(buttons.All(b=>!b.IsEnabled&&Equals(b.Content,"已领取"))&&action.Message.Length==0,"official claimed inventory refresh updates both cards and shared state");
            model.ApplyEvent("watch_schedule",JsonSerializer.SerializeToElement(new{rank=1,campaignName="模拟优先活动"}));
            check(model.PrioritySummary.Contains("1")&&model.PrioritySummary.Contains("模拟优先活动"),"Worker scheduling events update the displayed current campaign priority");
            window.Close();await Idle();
            check(((System.Collections.IEnumerable)typeof(CampaignInventoryView).GetField("_observed",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(content)!).Cast<object>().Count()==0,"closing an inventory view releases campaign event subscriptions");
            Dictionary<string,CampaignPriorityWindow.PriorityEntry>? saved=null;
            var priority=new CampaignPriorityWindow(model,entries=>{saved=entries;return Task.CompletedTask;}){Width=520,Height=610,ShowActivated=false,ShowInTaskbar=false};
            priority.Show();await Idle();
            var list=(ListBox)priority.FindName("CampaignList");
            check(list.Items.Cast<CampaignPriorityWindow.Row>().Select(r=>r.Campaign.Id).SequenceEqual(new[]{"b","a","c"}),"priority dialog restores three saved campaign IDs independently of display sort");
            list.SelectedIndex=2;
            priority.GetType().GetMethod("MoveTop",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(priority,new object[]{list,new RoutedEventArgs()});
            check(list.Items.Cast<CampaignPriorityWindow.Row>().First().Campaign.Id=="c"&&((TextBlock)priority.FindName("SaveStatus")).Text.Contains("未保存"),"button ordering moves a campaign to the top and marks unsaved changes");
            var mode=(ComboBox)priority.FindName("ModePicker");
            check(mode.Items.Count==5&&list.AllowDrop&&list.Focusable,"priority dialog exposes all strategies, drag/drop and keyboard focus");
            check(((ComboBox)priority.FindName("GamePicker")).SelectedItem?.ToString()=="Overwatch","game picker displays the game name without internal IDs or object text");
            priority.GetType().GetMethod("SaveClicked",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(priority,new object[]{list,new RoutedEventArgs()});await Idle();
            check(saved?["1"].mode=="custom"&&saved["1"].campaignIds.SequenceEqual(new[]{"c","b","a"}),"saving sends stable Game/Campaign IDs in the requested custom order");
            Snapshot(priority,Path.Combine(output,"priority-dark.png"));ThemeManager.Apply(false);await Idle();Snapshot(priority,Path.Combine(output,"priority-light.png"));priority.Close();
            check(listener.Errors.Count==0,"new reward controls and priority dialog produce no WPF binding errors");
            await File.WriteAllLinesAsync(Path.Combine(output,"twitch-action-bindings.txt"),listener.Errors);
        }
        finally{PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);ThemeManager.Apply(false);}
    }
    private static async Task Idle()=>await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)yield return match;
            foreach(var item in Descendants<T>(child))yield return item;
        }
    }
    private static void Snapshot(Window window,string path)
    {
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
    private sealed class BindingErrors:TraceListener
    {
        internal List<string> Errors{get;}=[];
        public override void Write(string? message){if(message?.Contains("Error")==true)Errors.Add(message);}
        public override void WriteLine(string? message)=>Write(message);
    }
}
