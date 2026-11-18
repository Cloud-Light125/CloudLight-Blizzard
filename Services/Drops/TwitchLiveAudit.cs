using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.ViewModels.Visuals;
using CloudLightBlizzard.Views.Pages;
using CloudLightBlizzard.Views.Drops.Twitch;

namespace CloudLightBlizzard.Services.Drops;

/// <summary>Explicitly opted-in real acceptance on the already isolated app.
/// Claim requires its own command-line switch and a durable one-attempt marker.
/// This runner uses the same page methods as the WPF actions; never retries Claim.
/// </summary>
internal static class TwitchLiveAudit
{
    private const string CampaignId="e3a69270-1546-4966-8bcf-2af97c5a0a0b";
    private const string DropId="8676730b-d7a8-11f1-9ea6-0a58a9feac02";
    internal static async Task RunAsync(MainWindow window,string root,bool approvedRiver)
    {
        var output=Path.Combine(root,"live-audit");Directory.CreateDirectory(output);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var main=(MainViewModel)typeof(MainWindow).GetField("_vm",flags)!.GetValue(window)!;
        var page=(DropsPage)typeof(MainWindow).GetField("_dropsPage",flags)!.GetValue(window)!;
        var vm=(DropsViewModel)page.DataContext;
        var checks=new List<string>();
        var samples=new List<JsonElement>();
        var host=main.DropsHost;
        var claimsCalled=0;
        using var budget=new CancellationTokenSource(TimeSpan.FromMinutes(14));
        void Progress(string phase)=>File.WriteAllText(Path.Combine(output,"progress.json"),JsonSerializer.Serialize(new{phase,at=DateTimeOffset.Now,claimsCalled,checks}));
        void Check(bool ok,string name){checks.Add((ok?"PASS ":"FAIL ")+name);if(!ok)throw new InvalidOperationException(name);}
        async Task Call(string name,params object[] args)=>await (Task)typeof(DropsPage).GetMethod(name,flags)!.Invoke(page,args)!;
        async Task<JsonElement> State()
        {
            var state=await vm.RequestAsync(DropsPlatform.Twitch,"load_state",token:budget.Token);vm.ApplyState(DropsPlatform.Twitch,state);return state;
        }
        void Save(string name,JsonElement state)=>File.WriteAllText(Path.Combine(output,name+".json"),RealDropsEvidenceRecorder.SafeSnapshot(state).GetRawText());
        async Task Picture(string name,Window? target=null)
        {
            target??=window;
            await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            target.UpdateLayout();var bitmap=new RenderTargetBitmap((int)target.ActualWidth,(int)target.ActualHeight,96,96,PixelFormats.Pbgra32);
            bitmap.Render(target);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));png.Save(stream);
        }
        static JsonElement Find(JsonElement state,string id)=>state.GetProperty("inventory").EnumerateArray().Single(d=>d.GetProperty("id").GetString()==id);
        static bool Claimed(JsonElement drop)=>drop.GetProperty("claimed").GetBoolean();
        try
        {
            Progress("connect_existing_session");window.NavigateTo("drops");page.SelectPlatform(DropsPlatform.Twitch);page.ShowWorkspace("overview");
            await Call("LoginTwitchAsync");
            JsonElement state=default;
            for(var i=0;i<60;i++)
            {
                state=await State();if(state.GetProperty("inventory").GetArrayLength()>0)break;
                await Task.Delay(2000,budget.Token);
            }
            Check(state.GetProperty("inventory").GetArrayLength()>0,"real existing Session restores official Inventory without watching or claiming");
            Check(!state.GetProperty("settings").GetProperty("auto_claim_drops").GetBoolean(),"NoAutoClaim stays disabled in isolated real configuration");
            Save("before",state);await Picture("real-before");
            var originalClaims=state.GetProperty("inventory").EnumerateArray().ToDictionary(d=>d.GetProperty("id").GetString()!,Claimed);
            var saved=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var priority=new CampaignPriorityWindow(vm.TwitchInventory,async entries=>
            {
                var settings=await vm.RequestAsync(DropsPlatform.Twitch,"save_settings",new{settings=new{campaign_watch_priority=entries}},budget.Token);
                vm.TwitchInventory.Apply(JsonSerializer.SerializeToElement(new{settings}));saved.TrySetResult();
            }){Owner=window};
            priority.Show();
            var games=(ComboBox)priority.FindName("GamePicker");
            games.SelectedItem=games.Items.Cast<object>().Single(g=>g.GetType().GetProperty("Id")!.GetValue(g)?.ToString()=="515025");
            var list=(ListBox)priority.FindName("CampaignList");
            Check(list.Items.Count>=2,"real priority dialog receives multiple Campaigns through the Worker Game ID DTO");
            list.SelectedItem=list.Items.Cast<CampaignPriorityWindow.Row>().Single(r=>r.Campaign.Id==CampaignId);
            typeof(CampaignPriorityWindow).GetMethod("MoveTop",flags)!.Invoke(priority,new object[]{list,new RoutedEventArgs()});
            ((Button)priority.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await saved.Task.WaitAsync(TimeSpan.FromSeconds(30),budget.Token);await Picture("real-priority-saved",priority);priority.Close();
            Progress("start_actual_watch");await Call("StartPlatformAsync",DropsPlatform.Twitch,false);
            for(var i=0;i<75;i++)
            {
                state=await State();
                if(state.GetProperty("currentChannel").ValueKind==JsonValueKind.Object&&V.Number(state.GetProperty("watchRequests"),"attempts")>0)break;
                await Task.Delay(2000,budget.Token);
            }
            Save("watch-start",state);await Picture("real-watching");
            if(approvedRiver)
            {
                Progress("verify_approved_river");
                var drop=Find(state,DropId);
                Check(drop.GetProperty("campaignId").GetString()==CampaignId&&drop.GetProperty("name").GetString()=="River Spray"&&!Claimed(drop)&&drop.GetProperty("canClaim").GetBoolean(),"approved River Spray is still the same official unclaimed claimable Drop");
                var reward=vm.TwitchInventory.AllCampaigns.Single(c=>c.Id==CampaignId).Drops.Single(d=>d.Id==DropId);
                Check(reward.ClaimAction?.CanExecute==true,"real WPF action is enabled for the specifically approved River Spray");
                // CreateNew is a hard one-attempt boundary across app/Worker restarts.
                using(var marker=new FileStream(Path.Combine(root,"approved-river-attempt.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {JsonSerializer.Serialize(marker,new{CampaignId,DropId,approved=true,at=DateTimeOffset.Now});marker.Flush(true);}
                claimsCalled=1;Progress("submit_approved_river_once");
                await Call("ClaimTwitchRewardAsync",reward);
                state=await State();Save("claim-response-state",state);await Picture("real-claim-result");
                Check(V.Number(state.GetProperty("claimOperations"),"submitted")==1,"one real mutation was submitted and no other reward was claimed by this Worker");
                Progress("independent_official_verification");
                state=await vm.RequestAsync(DropsPlatform.Twitch,"refresh",token:budget.Token);vm.ApplyState(DropsPlatform.Twitch,state);Save("claim-independent-inventory",state);
                Check(Claimed(Find(state,DropId)),"independent official Inventory confirms River Spray claimed");
                Check(state.GetProperty("inventory").EnumerateArray().Where(d=>d.GetProperty("id").GetString()!=DropId).All(d=>!originalClaims.TryGetValue(d.GetProperty("id").GetString()!,out var was)||was==Claimed(d)),"every other official reward retains its prior claimed status");
                reward=vm.TwitchInventory.AllCampaigns.Single(c=>c.Id==CampaignId).Drops.Single(d=>d.Id==DropId);
                Check(reward.Claimed&&reward.ClaimAction?.CanExecute==false&&reward.ClaimAction.ButtonText=="已领取"&&!reward.ClaimAction.Busy,"real WPF card is claimed, disabled and no longer loading");
                page.ShowWorkspace("queue");await Picture("real-queue-after-claim");page.ShowWorkspace("overview");
            }
            Progress("observe_official_watch_progress");
            for(var i=0;i<(approvedRiver?5:2);i++)
            {
                if(i>0)await Task.Delay(TimeSpan.FromSeconds(45),budget.Token);
                state=await vm.RequestAsync(DropsPlatform.Twitch,"refresh",token:budget.Token);vm.ApplyState(DropsPlatform.Twitch,state);
                samples.Add(RealDropsEvidenceRecorder.SafeSnapshot(state));Save("watch-sample-"+i,state);
                File.WriteAllText(Path.Combine(output,"watch-samples.json"),JsonSerializer.Serialize(samples));Progress("watch_sample_"+i);
            }
            Save("watch-final",state);await Picture("real-watch-final");
            checks.Add(V.Number(state.GetProperty("watchRequests"),"accepted",V.Number(state.GetProperty("watchRequests"),"successes"))>0?"PASS real selected channel accepted watch requests":"BLOCKED no accepted real watch request; inspect qualification and network evidence");
            checks.Add(state.GetProperty("watchSchedule").TryGetProperty("eligibleCampaignIds",out var eligible)&&eligible.EnumerateArray().Any(c=>c.GetString()==V.Text(state.GetProperty("watchSchedule"),"campaignId"))?"PASS selected target Campaign appears in this channel's official eligibility list":"BLOCKED no officially qualified watch target observed");
            Progress("explicit_stop");await Call("StopPlatformAsync",DropsPlatform.Twitch,false);
            state=await State();Check(!state.GetProperty("running").GetBoolean(),"explicit user stop ends real watching");Save("stopped",state);await Picture("real-stopped");
            await Task.Delay(TimeSpan.FromSeconds(8),budget.Token);state=await State();Check(!state.GetProperty("running").GetBoolean(),"refresh and late callbacks do not restart stopped watching");
            Progress("restart_worker_readonly");await host.RestartAsync(DropsPlatform.Twitch,budget.Token);
            // Refresh the new generation before beginning a read-only Session
            // validation; the old page can still display its logged-in flag.
            state=await State();vm.BeginTwitchLogin();
            await vm.RequestAsync(DropsPlatform.Twitch,"login",new{continueWatching=false},budget.Token);
            for(var i=0;i<60;i++){state=await State();if(state.GetProperty("inventory").GetArrayLength()>0)break;await Task.Delay(2000,budget.Token);}
            Save("restart-readonly",state);await Picture("real-restart-readonly");
            Check(state.GetProperty("inventory").GetArrayLength()>0&&!state.GetProperty("running").GetBoolean()&&!state.GetProperty("settings").GetProperty("auto_claim_drops").GetBoolean(),"Worker restart restores Session without watching or automatically claiming");
            Check(state.GetProperty("settings").GetProperty("campaign_watch_priority").GetProperty("515025").GetProperty("campaignIds")[0].GetString()==CampaignId,"real custom Campaign order survives Worker restart");
            if(approvedRiver||originalClaims.GetValueOrDefault(DropId))Check(Claimed(Find(state,DropId))&&!vm.TwitchInventory.AllCampaigns.Single(c=>c.Id==CampaignId).Drops.Single(d=>d.Id==DropId).ClaimAction!.CanExecute,"official River claim and disabled WPF action survive Worker restart");
            Progress("completed");
        }
        catch(Exception error)
        {
            checks.Add("FAIL "+error.GetType().Name+": "+error.Message);File.WriteAllText(Path.Combine(output,"runner-error.txt"),error.ToString());Progress("failed");
        }
        finally
        {
            try{await Call("StopPlatformAsync",DropsPlatform.Twitch,false);}catch{checks.Add("FAIL final Worker stop");}
            File.WriteAllLines(Path.Combine(output,"live-checks.txt"),checks);
            File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{claimsCalled,approvedRiver,checks,completedAt=DateTimeOffset.Now}));
            window.Close();
        }
    }
}
