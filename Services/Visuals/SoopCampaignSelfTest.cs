using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudLightBlizzard.Controls;
using CloudLightBlizzard.ViewModels.Visuals;
using CloudLightBlizzard.Views.Drops.Soop;

namespace CloudLightBlizzard.Services.Visuals;

internal static class SoopCampaignSelfTest
{
    internal static JsonElement Fixture()
    {
        object Row(string uid,string id,string game,string name,string title,string type="固定型",string lifecycle="active",int view=15,bool completed=false)=>new
        {
            uid,id,gameId=game,gameName=name,title,type,active=lifecycle=="active",notYetOpen=lifecycle=="notYetOpen",ended=lifecycle=="ended",completed,
            executable=lifecycle=="active"&&!completed,officialProgressAvailable=type=="固定型",gameAccountBindingStatus="无需绑定第三方游戏账号",
            startTime="2026-11-01 09:00 +09:00",endTime="2026-11-30 23:59 +09:00",channelIds=new[]{"official-room"},categoryNames=new[]{name},
            randomNotice=type=="随机型"?"随机掉宝 · 参与不代表必定获奖":"",
            items=Enumerable.Range(1,id=="ow-fixed"?4:1).Select(i=>new {name=$"奖励 {i} · SIMULATED 布局测试",requiredMinutes=60,viewMinutes=view,imageUrl="",completed}).ToArray()
        };
        return JsonSerializer.SerializeToElement(new
        {
            running=false,evidenceKind="SIMULATED",
            settings=new {campaign_selection_mode="selected_only",selected_games=new[]{"game:OW","game:PUBG","game:VAL","game:LOL"},game_priority=new[]{"game:OW","game:PUBG","game:VAL","game:LOL"},
                campaign_priorities=new Dictionary<string,string[]>{{"game:OW",["ow-fixed","ow-random"]},{"game:PUBG",["pubg"]},{"game:VAL",["val"]},{"game:LOL",["lol"]}},campaign_sort_mode="custom",channel_mode="smart",auto_claim_enabled=false},
            accounts=new[]{new{uid="A",loggedIn=true,running=false,primary=true,missionTitle="守望先锋 / Stage 3 季后赛 · SIMULATED"},new{uid="B",loggedIn=true,running=false,primary=false,missionTitle="守望先锋 / Stage 3 季后赛 · SIMULATED"}},
            campaigns=new[]{Row("A","ow-fixed","game:OW","守望先锋","Stage 3 季后赛游戏道具掉宝 · SIMULATED"),Row("A","ow-random","game:OW","守望先锋","OWCS 文化礼品券掉宝 · SIMULATED","随机型"),
                Row("A","future","game:OW","守望先锋","未来活动 · SIMULATED","固定型","notYetOpen"),Row("A","history","game:OW","守望先锋","已结束且完成 · SIMULATED","固定型","ended",60,true),
                Row("A","pubg","game:PUBG","绝地求生","PUBG Drops · SIMULATED"),Row("A","val","game:VAL","无畏契约","VALORANT Drops · SIMULATED"),Row("A","lol","game:LOL","英雄联盟","League Drops · SIMULATED"),
                Row("A","other","game:other","其他游戏","未选择活动 · SIMULATED"),Row("B","ow-fixed","game:OW","守望先锋","Stage 3 季后赛游戏道具掉宝 · SIMULATED",view:3)}
        });
    }
    internal static async Task RunAsync(Action<bool,string> check,string output)
    {
        var bindings=new Errors();PresentationTraceSources.DataBindingSource.Listeners.Add(bindings);
        Window? window=null;
        try
        {
            var state=Fixture();await File.WriteAllTextAsync(Path.Combine(output,"soop-simulated-state.json"),state.GetRawText());
            var model=new SoopCampaignModel();model.Apply(state);
            check(model.Games.Count==5&&model.Games[0].Campaigns.Count==4&&model.Games.Count(g=>g.Selected)==4,"SOOP four selected games and multiple activities use exact IDs");
            check(model.Games.SelectMany(g=>g.Campaigns).All(c=>!c.RewardsMaterialized),"SOOP catalog application creates zero reward projections before realization");
            var legacy=new SoopCampaignModel();legacy.Apply(state);
            var legacyState=JsonSerializer.SerializeToElement(new{campaigns=state.GetProperty("campaigns"),settings=new{campaign_selection_mode="legacy",selected_games=Array.Empty<string>(),campaign_priorities=new Dictionary<string,string[]>(),priority_mission_id="auto"}});
            legacy.Apply(legacyState);
            check(!legacy.ExplicitSelection&&!legacy.Dirty&&legacy.Games.All(g=>!g.Selected),"SOOP cache refresh cannot turn legacy intent into selected-only watch policy");
            model.Move(model.Games[0].Campaigns[1],-99);
            var saved=JsonSerializer.SerializeToElement(model.SettingsPayload());
            var reopened=new SoopCampaignModel();reopened.Apply(JsonSerializer.SerializeToElement(new{settings=saved,campaigns=state.GetProperty("campaigns")}));
            check(reopened.Games[0].Campaigns[0].Id=="ow-random"&&reopened.Games[0].Campaigns.Count(c=>c.Selected)==2,"SOOP custom activity order and selection survive roundtrip");
            reopened.Account="B";var b=reopened.Games[0].Campaigns.Single(c=>c.Id=="ow-fixed").Rewards[0].Visual;
            check(b.Current==3&&b.AccountId=="B"&&!b.Claimed,"SOOP UID B has independent 3/60 and claim state");
            reopened.Account="A";
            var fixedCampaign=reopened.Games[0].Campaigns.Single(c=>c.Id=="ow-fixed");
            check(fixedCampaign.Rewards.Count==4&&fixedCampaign.Rewards[0].Visual.Current==15,"SOOP switching back restores A's four independent rewards at 15/60");
            var random=reopened.Games[0].Campaigns.Single(c=>c.Random).Rewards[0].Visual;
            check(random.IsRandom&&random.Required==0&&random.PercentText=="—"&&random.ProgressVisibility==Visibility.Collapsed&&!random.WatchCompleted,"SOOP random activities suppress deterministic minutes/percentage/completion");
            var displaySettings=new AppSettings();reopened.ConfigureDisplay(displaySettings);
            var before=JsonSerializer.Serialize(reopened.SettingsPayload());reopened.DisplayScope=1;
            check(before==JsonSerializer.Serialize(reopened.SettingsPayload())&&AppSettings.LoadFrom(AppSettings.FilePath).SoopInventoryDisplayScope==1&&!reopened.Dirty,"SOOP persisted presentation scope never changes watch intent");
            reopened.DisplayScope=0;
            reopened.Move(fixedCampaign,-99);
            var panel=new SoopCampaignPanel{Model=reopened,ListHeight=480,Heading="SOOP 活动与奖励 · SIMULATED 合成状态（无真实观看或领取）"};
            window=new Window{Title="SOOP 3.0.0 · SIMULATED",ShowActivated=false,ShowInTaskbar=false,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
            window.SetResourceReference(Window.BackgroundProperty,"App.Background");window.SetResourceReference(Window.ForegroundProperty,"Text.Primary");window.Show();await Idle();
            ((ComboBox)panel.FindName("Scope")).SelectedIndex=1;await Idle();
            check(panel.VisibleGames.Count==4,"SOOP selected-only filter shows exactly four games");
            await Capture(window,panel,Path.Combine(output,"soop-selected-SIMULATED.png"));
            ((ComboBox)panel.FindName("Scope")).SelectedIndex=0;
            foreach(var(width,height)in new[]{(1366,768),(1600,900),(1920,1080)})foreach(var dark in new[]{false,true})
            {
                ThemeManager.Apply(dark);window.Width=width;window.Height=height;panel.ListHeight=height-270;await Idle();
                await Capture(window,panel,Path.Combine(output,$"soop-cards-{width}x{height}-{(dark?"dark":"light")}-SIMULATED.png"));
                check(Descendants<RewardCard>(panel).All(c=>c.Compact&&c.ActualWidth>=270&&c.ActualWidth<=342),$"SOOP shared reward tiles fit {width}x{height} {(dark?"dark":"light")}");
            }
            foreach(var(index,name)in new[]{(1,"active"),(2,"future"),(3,"ended-completed")})
            {((ComboBox)panel.FindName("Lifecycle")).SelectedIndex=index;await Idle();await Capture(window,panel,Path.Combine(output,$"soop-{name}-SIMULATED.png"));}
            ((ComboBox)panel.FindName("Lifecycle")).SelectedIndex=0;((ComboBox)panel.FindName("Kind")).SelectedIndex=1;await Idle();
            await Capture(window,panel,Path.Combine(output,"soop-random-SIMULATED.png"));
            ((ComboBox)panel.FindName("Kind")).SelectedIndex=0;reopened.Account="B";await Idle();
            await Capture(window,panel,Path.Combine(output,"soop-account-B-SIMULATED.png"));reopened.Account="A";
            panel.Editable=true;await Capture(window,panel,Path.Combine(output,"soop-watch-config-SIMULATED.png"));panel.Editable=false;
            var detail=panel.CreateDetails(fixedCampaign,fixedCampaign.Rewards[0].Visual);detail.Show();await Idle();await Capture(detail,detail,Path.Combine(output,"soop-reward-details-SIMULATED.png"));detail.Close();
            var officialInput=Path.Combine(output,"soop-official-input.json");
            if(File.Exists(officialInput))
            {
                var source=JsonDocument.Parse(await File.ReadAllTextAsync(officialInput)).RootElement;
                var officialModel=new SoopCampaignModel();var watch=Stopwatch.StartNew();officialModel.Apply(source);watch.Stop();var init=watch.Elapsed.TotalMilliseconds;
                var officialPanel=new SoopCampaignPanel{Model=officialModel,ListHeight=500,Heading="OFFICIAL · 官方只读快照 · "+V.Text(source,"queriedAt")};
                window.Content=officialPanel;window.Width=1366;window.Height=900;ThemeManager.Apply(false);watch.Restart();await Idle();watch.Stop();var layout=watch.Elapsed.TotalMilliseconds;
                var list=(ListBox)officialPanel.FindName("GameList");
                var coldCards=Descendants<RewardCard>(officialPanel).Count();
                var first=officialModel.Games.SelectMany(g=>g.Campaigns).FirstOrDefault();officialModel.Apply(source);
                check(first is not null&&ReferenceEquals(first,officialModel.Games.SelectMany(g=>g.Campaigns).First()),"SOOP unchanged official refresh retains activity object identity");
                foreach(var view in officialPanel.VisibleGames.ToArray())officialPanel.ExpandGame(view.Source.Id);await Idle();
                var cards=Descendants<RewardCard>(officialPanel).Count();var totalRewards=officialModel.Games.Sum(g=>g.Campaigns.Sum(c=>c.RewardCount));
                check(cards<totalRewards&&list.Items.Count>cards,"SOOP real catalog expands with recycled rows, not all reward controls");
                var scroll=Descendants<ScrollViewer>(list).First();var memoryBefore=Process.GetCurrentProcess().PrivateMemorySize64;var scrollTimes=new List<double>();
                for(int i=0;i<12;i++){watch.Restart();scroll.ScrollToVerticalOffset(i*500);await Idle();watch.Stop();scrollTimes.Add(watch.Elapsed.TotalMilliseconds);}
                var offset=scroll.VerticalOffset;officialModel.Apply(source);await Idle();
                check(Math.Abs(scroll.VerticalOffset-offset)<2,"SOOP unchanged official refresh preserves scroll offset");
                watch.Restart();((ComboBox)officialPanel.FindName("Scope")).SelectedIndex=1;await Idle();watch.Stop();var filter=watch.Elapsed.TotalMilliseconds;
                ((ComboBox)officialPanel.FindName("Scope")).SelectedIndex=0;
                ((TextBox)officialPanel.FindName("Search")).Text="Stage 3";await Task.Delay(220);await Idle();scroll.ScrollToTop();await Idle();
                ((ComboBox)officialPanel.FindName("Lifecycle")).SelectedIndex=1;await Idle();
                foreach(var(width,height)in new[]{(1366,768),(1600,900),(1920,1080)})foreach(var dark in new[]{false,true})
                {ThemeManager.Apply(dark);window.Width=width;window.Height=height;officialPanel.ListHeight=height-270;await Idle();await Capture(window,officialPanel,Path.Combine(output,$"soop-OFFICIAL-{width}x{height}-{(dark?"dark":"light")}.png"));}
                await File.WriteAllTextAsync(Path.Combine(output,"soop-performance.json"),JsonSerializer.Serialize(new{kind="OFFICIAL read-only snapshot replay in real WPF",games=officialModel.Games.Count,activities=officialModel.Games.Sum(g=>g.Campaigns.Count),totalRewards,modelInitMs=init,initialLayoutMs=layout,coldCards,realizedCardsAfterExpandAll=cards,filterMs=filter,scrollStepMs=scrollTimes,privateBytesBeforeScroll=memoryBefore,privateBytesAfterScroll=Process.GetCurrentProcess().PrivateMemorySize64,scope="single local run; no hardware-independent FPS or soak claim"},new JsonSerializerOptions{WriteIndented=true}));
            }
            check(bindings.Messages.Count==0,"SOOP shared controls have zero WPF binding errors");await File.WriteAllLinesAsync(Path.Combine(output,"soop-bindings.txt"),bindings.Messages);
        }
        finally{window?.Close();PresentationTraceSources.DataBindingSource.Listeners.Remove(bindings);ThemeManager.Apply(false);}
    }
    private static async Task Capture(Window window,DependencyObject content,string path)
    {
        // Wait for visible public images to succeed or fail; never snapshot a loading placeholder as completed.
        var until=DateTime.UtcNow.AddSeconds(14);await Task.Delay(150);await Idle();
        while(Descendants<VisualImage>(content).Any(i=>i.IsVisible&&i.LoadState.Contains("正在加载"))&&DateTime.UtcNow<until){await Task.Delay(100);await Idle();}
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
        await File.WriteAllTextAsync(path+".images.json",JsonSerializer.Serialize(Descendants<VisualImage>(content).Where(i=>i.IsVisible).Select(i=>new{i.LoadState,loaded=i.LoadedImage is not null}).ToArray()));
    }
    private static async Task Idle()=>await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var item in Descendants<T>(child))yield return item;}}
    private sealed class Errors:TraceListener
    {internal List<string> Messages{get;}=[];public override void Write(string? value){if(value?.Contains("Error")==true)Messages.Add(value);}public override void WriteLine(string? value)=>Write(value);}
}
