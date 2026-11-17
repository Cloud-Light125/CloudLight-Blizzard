using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudLightBlizzard.Controls;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels.Visuals;

namespace CloudLightBlizzard.Services;

internal sealed partial class UiRedesignReview
{
    private async Task RunPriorityStateAsync()
    {
        var shots=Path.Combine(_output,"screenshots");Directory.CreateDirectory(shots);
        try
        {
            _scenarios.SelectedIndex=8;Apply(8);_page.ShowWorkspace("overview");
            var games=_drops.TwitchInventory.OverviewGroups.Select(g=>g.Game).Where(g=>g.Length>0).Distinct().ToArray();
            _drops.TwitchPriorityGames.Clear();
            foreach(var game in games.Take(2).Reverse())_drops.TwitchPriorityGames.Add(game);
            foreach(var (width,height) in new[]{(1366,768),(1600,900),(1920,1080),(2560,1440)})
            foreach(var dark in new[]{false,true})
            foreach(var section in new[]{"overview","activities","queue"})
            {
                ThemeManager.Apply(dark);_window.Width=width/1.25;_window.Height=height/1.25;
                _page.ShowWorkspace(section);_drops.SetTwitchCampaignScope("all");_drops.TwitchInventory.OverviewPriorityOnly=false;
                await Settle();CheckVisibleBounds(8,width,height,1.25);
                AuditRewardCards($"{section} {width} {(dark?"dark":"light")}");
                Capture(Path.Combine(shots,$"{section}-{width}x{height}-{(dark?"dark":"light")}.png"));
            }
            ThemeManager.Apply(false);_window.Width=1280;_window.Height=720;_page.ShowWorkspace("overview");
            _drops.TwitchInventory.OverviewPriorityOnly=false;await Settle();Capture(Path.Combine(shots,"01-priority-all.png"));
            var scope=Descendants<ComboBox>(_page).Single(c=>c.Name=="OverviewScopePicker");
            scope.SelectedValue="priority";await Settle();Capture(Path.Combine(shots,"02-priority-only.png"));
            Check(_drops.TwitchInventory.OverviewPriorityOnly&&AppSettings.LoadFrom(AppSettings.FilePath).TwitchOverviewPriorityOnly,"actual WPF display scope selection saves the independent UI preference");
            _drops.TwitchPriorityGames.Clear();await Settle();Capture(Path.Combine(shots,"03-priority-empty.png"));
            Check(_drops.TwitchInventory.OverviewGroups.Count==0&&_drops.TwitchInventory.OverviewEmptyText=="尚未设置优先游戏","actual WPF empty priorities do not show all");
            _drops.TwitchPriorityGames.Add("__isolated_absent_game__");await Settle();Capture(Path.Combine(shots,"04-priority-no-campaign.png"));
            Check(_drops.TwitchInventory.OverviewEmptyText=="当前优先游戏暂无符合条件的活动","actual WPF missing priority campaign explanation");
            _drops.TwitchInventory.OverviewPriorityOnly=false;
            _drops.TwitchPriorityGames.Clear();
            if(_drops.TwitchInventory.OverviewGroups.FirstOrDefault(g=>g.Drops.Any(d=>d.Current>0&&!d.WatchCompleted)) is {} partialCampaign)_drops.TwitchPriorityGames.Add(partialCampaign.Game);
            _drops.ApplyState(DropsPlatform.Twitch,Json(new{running=false,historicalReplay=false,watchRequests=new{}}));await Settle();
            Capture(Path.Combine(shots,"05-stopped-partial.png"));
            Check(_drops.TwitchInventory.WatchState=="已停止观看"&&!_drops.TwitchInventory.HasWatchEvidence&&_drops.TwitchVisual.RuntimeSummary==_drops.TwitchInventory.WatchState,"Twitch page and dashboard agree on stopped official progress");
            _page.ShowWorkspace("activities");await Settle();Capture(Path.Combine(shots,"06-twitch-activities.png"));

            // These request states are explicit simulations, never replayed as current network evidence.
            if(_evidenceWatermark is not null)_evidenceWatermark.Text="SIMULATED · 隔离状态矩阵，无真实观看请求";
            _drops.ApplyState(DropsPlatform.Twitch,DenseFixture(Twitch(true,false)));
            _drops.ApplyState(DropsPlatform.Twitch,Json(new{historicalReplay=false,running=true,currentChannel=new{id="isolated-channel",name="模拟频道 · 无真实连接"},currentDrop=new{id="reward1",campaignId="review-campaign",name="模拟观看目标",currentMinutes=25,requiredMinutes=120,eligible=true}}));
            _page.ShowWorkspace("overview");await Settle();Capture(Path.Combine(shots,"07-selected-unconfirmed-SIMULATED.png"));
            _drops.ApplyState(DropsPlatform.Twitch,Json(new{watchRequests=new{channelId="isolated-channel",lastResult="accepted",lastAcceptedAt=DateTimeOffset.UtcNow.ToString("o")}}));
            await Settle();Capture(Path.Combine(shots,"08-request-accepted-SIMULATED.png"));
            Check(_drops.TwitchInventory.HasWatchEvidence&&!_drops.TwitchInventory.OfficialGrowthConfirmed,"request acceptance never claims official progress growth");
            await CaptureCardCoverageAsync(shots);
            await CapturePlatformStates(shots);
            _checks.Add("binding-errors="+_bindings.Errors.Count);
            Check(_bindings.Errors.Count==0,"actual WPF binding check");
            _checks.Add("Official snapshot replay verifies layout and saved progress only. Live requests, SOOP and Bilibili are simulated; no authentication/claim/Worker launched.");
            File.WriteAllLines(Path.Combine(_output,"priority-visual-results.txt"),_checks);
            Environment.ExitCode=_checks.Any(c=>c.StartsWith("FAIL"))?1:0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(_output,"priority-visual-failure.txt"),ex.ToString());Environment.ExitCode=1;}
        finally{_window.Close();}
    }
    private void Check(bool condition,string description)=>_checks.Add((condition?"PASS ":"FAIL ")+description);
    private async Task Settle()
    {
        _window.UpdateLayout();await _window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(300);_window.UpdateLayout();
    }
    private void AuditRewardCards(string label)
    {
        var view=Descendants<CampaignInventoryView>(_page).FirstOrDefault(v=>v.IsVisible);
        if(view is null){Check(false,"inventory visible "+label);return;}
        var outer=(ScrollViewer)_page.FindName("DropsScrollViewer");
        var outerBounds=outer.TransformToAncestor(_window).TransformBounds(new Rect(0,0,outer.ActualWidth,outer.ActualHeight));
        var clip=Rect.Intersect(outerBounds,view.TransformToAncestor(_window).TransformBounds(new Rect(0,0,view.ActualWidth,view.ActualHeight)));
        var cards=Descendants<RewardCard>(view).Where(c=>c.IsVisible&&c.ActualWidth>0).ToArray();
        var bounds=cards.Select(c=>c.TransformToAncestor(_window).TransformBounds(new Rect(0,0,c.ActualWidth,c.ActualHeight))).ToArray();
        var full=bounds.Count(b=>clip.Contains(b));var firstRow=bounds.Where(b=>b.IntersectsWith(clip)).GroupBy(b=>Math.Round(b.Top)).OrderBy(g=>g.Key).FirstOrDefault()?.Count()??0;
        Check(cards.Length>0&&cards.All(c=>c.ActualWidth>=189&&c.ActualWidth<=217),"card widths 190–216 DIP "+label);
        Check(cards.All(c=>c.ActualHeight<300),"card heights under 300 DIP "+label);
        Check(!Descendants<ScrollViewer>(view).Any(s=>s.IsVisible&&s.ScrollableWidth>1),"no horizontal card scrolling "+label);
        Check(cards.SelectMany(c=>Descendants<Button>(c)).Where(b=>b.IsVisible).All(b=>b.ActualHeight>=31.9),"details buttons at least 32 DIP "+label);
        Check(cards.SelectMany(c=>Descendants<TextBlock>(c)).All(t=>t.Text!="进行中"),"partial progress never labeled ongoing "+label);
        _checks.Add($"density {label}: content={view.ActualWidth:0.#}; full-cards={full}; first-row={firstRow}; card-width={cards.FirstOrDefault()?.ActualWidth:0.#}; card-height={cards.FirstOrDefault()?.ActualHeight:0.#}; image=92; fonts=15/14/16/13; top={clip.Top:0.#}");
    }
    private async Task CapturePlatformStates(string shots)
    {
        _window.Width=1280;_window.Height=800;
        foreach(var state in new[]{"stopped","running-empty","active","recovering"})
        {
            _page.SelectPlatform(DropsPlatform.Soop);_drops.SelectPlatform(DropsPlatform.Soop);_page.ShowWorkspace("overview");
            _drops.ApplyState(DropsPlatform.Soop,Json(new{running=state!="stopped",accounts=new object[]{
                new{uid="模拟账号 A",loggedIn=true,running=false,currentProgress=new[]{new{account="模拟账号 A",id="a",reward="官方 viewTime 保留（模拟）",currentMinutes=15,requiredMinutes=30}}},
                new{uid="模拟账号 B",loggedIn=true,running=state!="stopped",status=state=="recovering"?"recovering":"",channelId=state=="active"?"room-b":"",channelName="模拟频道 B",connectionHealthy=state=="active",bridgeConnected=state=="active",heartbeatStatus="success",heartbeatLastSuccess=DateTimeOffset.UtcNow.ToString("o"),currentProgress=new[]{new{account="模拟账号 B",id="b",reward="独立任务（模拟）",currentMinutes=5,requiredMinutes=30,watchEligible=true}}}}}));
            await Settle();Capture(Path.Combine(shots,"soop-"+state+"-SIMULATED.png"));
            Check(!_drops.SoopVisual.Accounts[0].HasWatchEvidence,"SOOP A stopped while B "+state);
            _page.SelectPlatform(DropsPlatform.Bilibili);_drops.SelectPlatform(DropsPlatform.Bilibili);_page.ShowWorkspace("activities");
            _drops.ApplyState(DropsPlatform.Bilibili,Json(new{running=state!="stopped",account=new{loggedIn=true,uid=1,userName="模拟 Bilibili 账号"},tasks=new[]{new{id="task",name="官方 30%（模拟数据）",current=18,limit=60,completed=false}},
                sessions=new{configuredSessions=state=="active"?2:1,activeSessions=state=="active"?2:0,retryingSessions=state=="recovering"?1:0,sessions=state=="active"?new[]{new{id="s1",state="active",roomId=1},new{id="s2",state="active",roomId=2}}:Array.Empty<object>()}}));
            await Settle();Capture(Path.Combine(shots,"bilibili-"+state+"-SIMULATED.png"));
            Check(state=="active"?_drops.BilibiliDetails.WatchingSessions==2:_drops.BilibiliDetails.WatchingSessions==0,"Bilibili active sessions "+state);
            Check(_drops.BilibiliVisual.RuntimeSummary==_drops.BilibiliDetails.WatchStateText,"Bilibili dashboard/page agree "+state);
        }
    }
}
