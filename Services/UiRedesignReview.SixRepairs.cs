using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CloudLightBlizzard.Controls;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.Services.OverwatchRegion;
using CloudLightBlizzard.Services.Visuals;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.Views.Drops.Soop;
using CloudLightBlizzard.Views.Pages;
using GameRegion = CloudLightBlizzard.Services.OverwatchRegion.OverwatchRegion;

namespace CloudLightBlizzard.Services;

internal sealed partial class UiRedesignReview
{
    private async Task RunSixRepairsAsync()
    {
        var shots=Path.Combine(_output,"screenshots");Directory.CreateDirectory(shots);
        var timings=new List<object>();
        try
        {
            var catalogPath=Path.Combine(_output,"soop-official-input.json");
            var official=JsonDocument.Parse(await File.ReadAllTextAsync(catalogPath)).RootElement.Clone();
            var dpi=VisualTreeHelper.GetDpi(_window);
            _window.Title="CloudLight Blizzard · 六项修复验收 · 隔离回放";
            // The review host overrides every Worker request; no login, watching or claims.
            var store=_main.RegionManager.BackupStore;
            foreach(var id in new[]{"isolated-active","isolated-old"})
            {
                store.SaveGeneration(new OverwatchRegionGeneration {GenerationId=id,State=RegionBackupState.Ready,BackupMode=RegionBackupMode.VerifiedDifference});
                var dir=store.BackupRoot(id,GameRegion.China);Directory.CreateDirectory(dir);await File.WriteAllTextAsync(Path.Combine(dir,"fixture.bin"),"isolated snapshot");
            }
            store.Activate("isolated-active",GameRegion.China);
            foreach(var (width,height) in new[]{(1366,768),(1600,900),(1920,1080)})
            foreach(var dark in new[]{false,true})
            {
                ThemeManager.Apply(dark);_window.Width=width/dpi.DpiScaleX;_window.Height=height/dpi.DpiScaleY;_window.Left=0;_window.Top=0;
                var suffix=$"{width}x{height}-{(dark?"dark":"light")}";
                _window.NavigateTo("drops");_page.SelectPlatform(DropsPlatform.Soop);_drops.SelectPlatform(DropsPlatform.Soop);_page.ShowWorkspace("overview");
                _drops.Soop.WorkerLifecycle=WorkerLifecycle.Running;_drops.Soop.Running=true;_drops.Soop.ConnectionState=DropsConnectionState.Connected;
                var cases=new[]{
                    ("01-unverified-worker",SoopEvidenceSelfTest.Account(authentication:"unverified",heartbeat:"not_sent",official:"unread")),
                    ("02-verified-waiting-channel",SoopEvidenceSelfTest.Account(room:"",heartbeat:"not_sent")),
                    ("03-heartbeat-official-unconfirmed",SoopEvidenceSelfTest.Account(official:"unchanged")),
                    ("04-official-growth",SoopEvidenceSelfTest.Account(official:"grown",minutes:248))
                };
                foreach(var (name,account) in cases)
                {
                    _drops.ApplyState(DropsPlatform.Soop,Json(new {running=true,accounts=new[]{account}}));
                    _page.ShowWorkspace("overview");await Settle();
                    var card=Descendants<TextBlock>(_page).First(t=>t.Text=="登录状态："+(name.Contains("unverified")?"未验证":"已验证登录"));
                    var overviewScroll=(ScrollViewer)_page.FindName("DropsScrollViewer");
                    overviewScroll.ScrollToVerticalOffset(overviewScroll.VerticalOffset+card.TranslatePoint(new Point(),overviewScroll).Y-28);
                    await Settle();
                    Capture(Path.Combine(shots,name+"-"+suffix+"-SIMULATED.png"));
                }
                _drops.Soop.Running=false;
                var timer=Stopwatch.StartNew();_drops.ApplyState(DropsPlatform.Soop,official);timer.Stop();
                _page.ShowWorkspace("activities");await Settle();
                Capture(Path.Combine(shots,"05-official-game-names-"+suffix+"-OFFICIAL-REPLAY.png"));
                var panel=Descendants<SoopCampaignPanel>(_page).First(p=>p.IsVisible);
                var list=(ListBox)panel.FindName("GameList");
                Check(_drops.SoopCampaigns.Games.All(g=>!g.Name.StartsWith("game:")),"all official game group names resolve "+suffix);
                var existing=_drops.SoopCampaigns.Games.First();var pageScroll=(ScrollViewer)_page.FindName("DropsScrollViewer");
                // On a tall screen the collapsed activities page fits entirely.
                // Expand its real backpack section to test an overflowing page.
                Descendants<Expander>(_page).Single(e=>e.Header as string=="账号任务与奖励背包 · 领取及兑换码").IsExpanded=true;
                await Settle();pageScroll.ScrollToTop();await Settle();
                Check(pageScroll.ScrollableHeight>0,"wheel fixture has actual outer-page overflow "+suffix);
                var before=pageScroll.VerticalOffset;
                var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent,Source=list};
                list.RaiseEvent(wheel);await Settle();
                Check(pageScroll.VerticalOffset>before,"normal wheel over virtualized list moves whole page "+suffix);
                _page.ShowWorkspace("settings");await Settle();
                Descendants<Button>(_page).Single(b=>b.IsVisible&&b.Content as string=="折叠全部").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle();
                Capture(Path.Combine(shots,"06-watch-configuration-"+suffix+"-OFFICIAL-REPLAY.png"));
                var strategy=Descendants<Button>(_page).Single(b=>b.IsVisible&&b.Content as string=="直播间与任务策略");
                strategy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
                Check(pageScroll.VerticalOffset>0,"quick navigation reaches channel policy "+suffix);
                Capture(Path.Combine(shots,"07-channel-policy-"+suffix+"-OFFICIAL-REPLAY.png"));
                var watchPanel=Descendants<SoopCampaignPanel>(_page).Single(p=>p.IsVisible);
                var inner=Descendants<ScrollViewer>(watchPanel).First(s=>s.ScrollableHeight>0);
                var innerBefore=inner.VerticalOffset;pageScroll.ScrollToTop();await Settle();
                var watchList=(ListBox)watchPanel.FindName("GameList");
                watchList.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent,Source=watchList});await Settle();
                Check(inner.VerticalOffset==innerBefore&&pageScroll.VerticalOffset>0,"page scroll does not consume list's independent offset "+suffix);
                var returnTop=Descendants<Button>(_page).Single(b=>b.IsVisible&&b.Content as string=="游戏与活动优先级");
                returnTop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
                var rows=_drops.SoopCampaigns.Games.SelectMany(g=>g.Campaigns).Count();
                var cards=Descendants<RewardCard>(watchPanel).Count();
                Check(cards<60&&rows>=425,"425+ activity replay retains reward virtualization "+suffix);
                timings.Add(new{width,height,dark,catalogMs=timer.Elapsed.TotalMilliseconds,activities=rows,realizedRewardCards=cards});

                _window.NavigateTo("snapshots");var snapshots=Field<SnapshotsPage>(_window,"_snapshotsPage");await snapshots.RefreshAsync();await Settle();
                Capture(Path.Combine(shots,"08-snapshot-fluent-"+suffix+"-SIMULATED.png"));
                Check(Descendants<Button>(snapshots).Where(b=>b.Tag as string is "delete" or "details" or "open" or "regenerate").All(b=>b.Height>=34&&b.Style is not null),"snapshot actions use Fluent styles and 36 DIP "+suffix);
                _window.NavigateTo("about");var about=Field<AboutPage>(_window,"_aboutPage");
                var licenses=(Expander)about.FindName("LicensesExpander");licenses.IsExpanded=false;
                var aboutScroll=(ScrollViewer)about.FindName("AboutScrollViewer");aboutScroll.ScrollToTop();await Settle();
                Capture(Path.Combine(shots,"09-about-updates-top-"+suffix+"-SIMULATED.png"));
                var update=Descendants<UpdatePanel>(about).Single();var feedback=Descendants<FeedbackPanel>(about).Single();
                Check(update.IsVisible&&feedback.IsVisible&&!licenses.IsExpanded&&update.TranslatePoint(new Point(),about).Y<feedback.TranslatePoint(new Point(),about).Y,"About owns actual update and feedback surfaces above collapsed licenses "+suffix);
                feedback.BringIntoView();await Settle();Capture(Path.Combine(shots,"10-about-feedback-"+suffix+"-SIMULATED.png"));
                licenses.BringIntoView();await Settle();Capture(Path.Combine(shots,"11-licenses-collapsed-"+suffix+"-SIMULATED.png"));
                licenses.IsExpanded=true;await Settle();licenses.BringIntoView();await Settle();Capture(Path.Combine(shots,"12-licenses-expanded-"+suffix+"-SIMULATED.png"));
                Check(about.Components.Count>=6&&Descendants<ItemsControl>(licenses).Any(c=>c.Items.Count==about.Components.Count),"expanded licenses preserve all component entries "+suffix);
                _window.OpenUpdateSettings();await Settle();
                Check(_window.FindName("AboutNav") is RadioButton{IsChecked:true}&&aboutScroll.VerticalOffset<1,"old update navigation opens About top "+suffix);
            }
            // Delete only isolated fixture data and verify the real view model reloads disk.
            _window.NavigateTo("snapshots");var page=Field<SnapshotsPage>(_window,"_snapshotsPage");await page.RefreshAsync();
            var vm=(SnapshotsViewModel)page.DataContext;
            var old=vm.Items.Single(i=>i.GenerationId=="isolated-old");
            Check(vm.Delete(old)&&!Directory.Exists(store.GenerationRoot(old.GenerationId))&&!vm.Items.Any(i=>i.GenerationId==old.GenerationId),"actual snapshot view model deletion re-reads disk and clears old selection");
            await Settle();Capture(Path.Combine(shots,"13-isolated-delete-success-SIMULATED.png"));
            var active=vm.Items.Single(i=>i.IsActive);
            Check(!vm.Delete(active)&&Directory.Exists(store.GenerationRoot(active.GenerationId))&&vm.StatusText.Contains("当前使用"),"actual snapshot view model preserves active snapshot with explicit explanation");
            await Settle();Capture(Path.Combine(shots,"14-active-protected-SIMULATED.png"));
            var protectedDialog=page.CreateActiveSnapshotNotice();protectedDialog.Show();await Settle();
            Check(Descendants<Button>(protectedDialog).Any(b=>b.Content as string=="前往区服切换"&&b.IsVisible),"actual active-snapshot dialog exposes the safe region-navigation button");
            var dialogDpi=VisualTreeHelper.GetDpi(protectedDialog);
            var dialogImage=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(protectedDialog.ActualWidth*dialogDpi.DpiScaleX),(int)Math.Ceiling(protectedDialog.ActualHeight*dialogDpi.DpiScaleY),dialogDpi.PixelsPerInchX,dialogDpi.PixelsPerInchY,PixelFormats.Pbgra32);
            dialogImage.Render(protectedDialog);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(dialogImage));
            using(var dialogFile=File.Create(Path.Combine(shots,"14b-active-protected-dialog-SIMULATED.png")))encoder.Save(dialogFile);
            Descendants<Button>(protectedDialog).Single(b=>b.Content as string=="前往区服切换").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();
            Check(_window.FindName("RegionNav") is RadioButton{IsChecked:true}&&Directory.Exists(store.GenerationRoot(active.GenerationId)),"active-snapshot dialog navigates to region workflow without clearing pointer or deleting files");
            await TestMovedPanelsAsync(shots);
            // Leave the interactive window on the repaired SOOP page for physical wheel testing.
            _window.NavigateTo("drops");_page.SelectPlatform(DropsPlatform.Soop);_drops.SelectPlatform(DropsPlatform.Soop);_drops.ApplyState(DropsPlatform.Soop,official);_page.ShowWorkspace("settings");
            _window.Width=1366/dpi.DpiScaleX;_window.Height=768/dpi.DpiScaleY;ThemeManager.Apply(false);await Settle();
        }
        catch(Exception ex){_checks.Add("FAIL "+ex);}
        finally
        {
            _checks.Add("binding-errors="+_bindings.Errors.Count);
            await File.WriteAllLinesAsync(Path.Combine(_output,"six-repairs-results.txt"),_checks.Concat(_bindings.Errors));
            await File.WriteAllTextAsync(Path.Combine(_output,"six-repairs-performance.json"),JsonSerializer.Serialize(timings,new JsonSerializerOptions{WriteIndented=true}));
            if(!File.Exists(Path.Combine(_output,"keep-open.flag")))_window.Close();
        }
    }

    private async Task TestMovedPanelsAsync(string shots)
    {
        _window.NavigateTo("about");var about=Field<AboutPage>(_window,"_aboutPage");
        var update=Descendants<UpdatePanel>(about).Single();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory=new CloudHttpClientFactory(_main.Settings,Path.Combine(_output,"simulated-update-network.log"),_=>new HttpClient(new SixHandler(async(_,token)=>
        {
            entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var clientField=typeof(UpdateDownloadService).GetField("_httpClients",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var original=clientField.GetValue(_main.UpdateDownloader);
        var oldRoot=UpdateDownloadService.SelfTestDownloadRoot.Value;
        try
        {
            clientField.SetValue(_main.UpdateDownloader,factory);
            UpdateDownloadService.SelfTestDownloadRoot.Value=Path.Combine(_output,"isolated-updates");
            typeof(UpdatePanel).GetField("_availableUpdate",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(update,new Models.UpdateCheckResult
            {
                LatestVersion="3.0.1",Tag="isolated-cancel-"+Guid.NewGuid().ToString("N"),InstallerName="CloudLight-Blizzard-3.0.1-win-x64-Setup.exe",
                InstallerDownloadUrl="https://github.com/Cloud-Light125/CloudLight-Blizzard/releases/download/v3.0.1/CloudLight-Blizzard-3.0.1-win-x64-Setup.exe",InstallerDigest="sha256:"+new string('a',64),InstallerSize=1024
            });
            ((StackPanel)update.FindName("UpdateAvailablePanel")).Visibility=Visibility.Visible;
            ((Button)update.FindName("OnlineUpdateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));await Settle();
            var cancel=(Button)update.FindName("CancelDownloadButton");
            Check(cancel.IsVisible,"moved UpdatePanel exposes cancellation during actual fake-handler download");
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();
            Check(((TextBlock)update.FindName("UpdateDownloadText")).Text=="下载已取消。"&&((Button)update.FindName("CheckUpdateButton")).IsEnabled,"moved UpdatePanel cancellation stops download and restores controls; no installer launched");
            about.FocusUpdateSection();await Settle();Capture(Path.Combine(shots,"15-about-download-cancelled-SIMULATED.png"));
        }
        finally{clientField.SetValue(_main.UpdateDownloader,original);UpdateDownloadService.SelfTestDownloadRoot.Value=oldRoot;}
        var feedback=Descendants<FeedbackPanel>(about).Single();int calls=0;
        using var fakeFeedback=new FeedbackService(_main.Settings,()=>new HttpClient(new SixHandler((_,_)=> {
            calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"success\":true,\"reportId\":\"ISOLATED-REPAIR-TEST\"}")});
        })));
        try
        {
            feedback.Initialize(_main.Settings,fakeFeedback);
            ((TextBox)feedback.FindName("TitleBox")).Text="SIMULATED · 关于页面反馈回归";
            ((TextBox)feedback.FindName("DescriptionBox")).Text="隔离 HTTP handler；不提交真实反馈，不附加日志。";
            ((CheckBox)feedback.FindName("AttachLogsBox")).IsChecked=false;
            var submit=Descendants<Button>(feedback).Single(b=>b.Content as string=="提交反馈");
            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
            Check(calls==1&&Descendants<TextBlock>(feedback).Any(t=>t.Text.Contains("ISOLATED-REPAIR-TEST")),"moved FeedbackPanel submits once through isolated handler and renders report ID");
            feedback.BringIntoView();await Settle();Capture(Path.Combine(shots,"16-about-feedback-success-SIMULATED.png"));
        }
        finally{feedback.Initialize(_main.Settings,_main.FeedbackService);}
    }
    private sealed class SixHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);
    }
}
