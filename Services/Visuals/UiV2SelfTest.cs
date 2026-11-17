using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CloudLightBlizzard.Controls;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.ViewModels.Visuals;

namespace CloudLightBlizzard.Services.Visuals;

/// <summary>Isolated projection/cache/actual WPF control tests. Never authenticates or starts workers.</summary>
internal static class UiV2SelfTest
{
    internal static async Task<int> RunAsync(string output)
    {
        output=Path.GetFullPath(output);
        var userRoot=AppPaths.Current.Root.TrimEnd(Path.DirectorySeparatorChar);
        if(output.Equals(userRoot,StringComparison.OrdinalIgnoreCase)||output.StartsWith(userRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||userRoot.StartsWith(output.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Use an isolated output root.");
        Directory.CreateDirectory(output);AppPaths.UseSelfTestRoot(Path.Combine(output,"isolated-data"));
        var checks=new List<string>();void Check(bool ok,string name)=>checks.Add((ok?"PASS ":"FAIL ")+name);
        static JsonElement Json(string value)=>JsonDocument.Parse(value).RootElement.Clone();
        try
        {
            TwitchInventorySelfTest.Run(Check);
            var safeEvidence=RealDropsEvidenceRecorder.SafeSnapshot(Json("""{"accounts":[{"userId":"private-user"}],"token":"private-token","authRequired":{"code":"private-code"},"inventory":[{"id":"drop","benefits":[{"id":"benefit","imageUrl":"https://static-cdn.jtvnw.net/reward.png?signature=private-signature"}]}]}""")).GetRawText();
            Check(!safeEvidence.Contains("private-")&&safeEvidence.Contains("imageUrlSha256")&&safeEvidence.Contains("benefit"),"real evidence excludes account/authentication fields and image signature parameters while retaining source hashes and IDs");
            var health=new DropsPlatformViewModel(DropsPlatform.Twitch,"Twitch"){Running=true};var model=new PlatformOverviewModel(health);
            model.Apply(Json("""{"lastInventorySyncAt":"2026-11-16T00:00:00Z","inventory":[{"id":"other","name":"not current","currentMinutes":1,"requiredMinutes":10},{"id":"current","name":"test drop","game":"Game","campaignId":"campaign","currentMinutes":18,"requiredMinutes":60,"benefits":[{"id":"one","name":"First","type":"DIRECT_ENTITLEMENT","imageUrl":"https://static-cdn.jtvnw.net/one.png"},{"id":"two","name":"Second","type":"BADGE","imageUrl":"https://static-cdn.jtvnw.net/two.png"}]}],"currentDrop":{"id":"current","name":"test drop","campaignId":"campaign","currentMinutes":18,"requiredMinutes":60,"estimatedExtraMinutes":2,"progress":0.99,"preconditionDropIds":["prior"],"preconditionsMet":false,"preconditionsSatisfiedForMining":true,"eligible":true}}"""));
            Check(model.ActiveReward?.Id=="current"&&model.ActiveReward.CampaignId=="campaign","current drop joins by Core drop ID, not inventory position");
            Check(model.ActiveReward?.Benefits.Count==2&&model.ActiveReward.Benefits[1].Id=="two"&&model.ActiveReward.Benefits[1].Type=="BADGE","all Benefit IDs/names/types/images survive the projection");
            Check(model.ActiveReward?.Current==18&&model.ActiveReward.Percent==30&&model.ActiveReward.Estimate==2,"official 18/60 remains 30%; local +2 and supplied percent cannot inflate it");
            Check(model.ActiveReward?.PreconditionDropIds.SequenceEqual(["prior"])==true&&model.ActiveReward.PreconditionsMet==false&&model.ActiveReward.PreconditionsSatisfiedForMining==true,"claim prerequisites and existing NoAutoClaim mining evidence remain separate");
            model.Apply(Json("""{"currentDrop":{"id":"next","name":"next reward","currentMinutes":0,"requiredMinutes":30,"benefits":[{"id":"next-benefit","name":"Next","type":"EMOTE","imageUrl":""}]}}"""));
            Check(model.ActiveReward?.Benefits.Single().Id=="next-benefit","changing current reward cannot retain the previous benefit images");
            var soop=new PlatformOverviewModel(new(DropsPlatform.Soop,"SOOP"));
            soop.UpdateAccount(Json("""{"uid":"A","running":true,"currentProgress":[{"account":"A","reward":"A reward","currentMinutes":15,"requiredMinutes":30}]}"""));
            soop.UpdateAccount(Json("""{"uid":"B","running":true,"currentProgress":[{"account":"B","reward":"B reward","currentMinutes":5,"requiredMinutes":60}]}"""));
            soop.UpdateAccount(Json("""{"uid":"A","running":false}"""));
            Check(soop.Accounts.Single(a=>a.Id=="A").Reward?.Current==15&&soop.Accounts.Single(a=>a.Id=="B").Reward?.Current==5&&!soop.Accounts.Single(a=>a.Id=="A").Running,"SOOP incremental stop keeps UID-specific official progress separate");
            soop.UpdateAccount(Json("""{"uid":"A","currentProgress":[{"account":"B","currentMinutes":999,"requiredMinutes":1000}]}"""));
            Check(soop.Accounts.Single(a=>a.Id=="A").Reward is null,"mismatched SOOP progress UID is never displayed on another account");

            var transport=new ImageTransport();var cache=new VisualImageCache(Path.Combine(output,"cache"),transport);
            var policy=new AppSettings{EnableProxy=true,TwitchUseProxy=false,BilibiliUseProxy=false};
            Check(!VisualImageCache.UsesProxyFor(policy,"Twitch")&&!VisualImageCache.UsesProxyFor(policy,"Bilibili"),"image routing respects disabled per-platform proxy scopes");
            policy.TwitchUseProxy=true;Check(VisualImageCache.UsesProxyFor(policy,"Twitch")&&!VisualImageCache.UsesProxyFor(policy,"Bilibili"),"image routing reuses the existing enabled Twitch scope without enabling Bilibili");
            const string good="https://static-cdn.jtvnw.net/good.png";
            var empty=await cache.LoadAsync("");Check(empty.Image is null&&transport.Requests.Count==0,"empty image URL gives an explicit placeholder without a network request");
            var invalid=await cache.LoadAsync("https://127.0.0.1/private.png");Check(invalid.Image is null&&transport.Requests.Count==0,"private/local image targets are rejected");
            var loaded=await cache.LoadAsync(good);var again=await cache.LoadAsync(good);
            Check(loaded.Image?.IsFrozen==true&&again.Image is not null&&transport.Count("/good.png")==1,"valid async image is frozen and reused from cache");
            var thumbnail=await cache.LoadAsync(good,decodePixelWidth:192);
            Check(thumbnail.Image?.PixelWidth==192&&transport.Count("/good.png")==1&&loaded.Image?.PixelWidth==640,"thumbnail decoding reuses official disk bytes without downloading again or replacing the detail-size bitmap");
            await Task.WhenAll(Enumerable.Range(0,5).Select(_=>cache.LoadAsync("https://static-cdn.jtvnw.net/shared.png")));
            Check(transport.Count("/shared.png")==1,"concurrent controls load the same URL only once");
            File.SetLastWriteTimeUtc(cache.CachePath(good),DateTime.UtcNow.AddDays(-8));await cache.LoadAsync(good);
            Check(transport.Count("/good.png")==2,"upstream seven-day cache expiry fetches the image again");
            await File.WriteAllBytesAsync(cache.CachePath(good),[1,2,3]);cache.Invalidate(good);var recovered=await cache.LoadAsync(good);
            Check(recovered.Image is not null&&transport.Count("/good.png")==3,"corrupt cached bytes are replaced by a valid fetch");
            var malformed=await cache.LoadAsync("https://static-cdn.jtvnw.net/bad.png");await cache.LoadAsync("https://static-cdn.jtvnw.net/bad.png");
            Check(malformed.Image is null&&transport.Count("/bad.png")==1,"invalid image bytes use a bounded failure fallback without retry storms");
            var timeoutCache=new VisualImageCache(Path.Combine(output,"timeout"),new ImageTransport(),TimeSpan.FromMilliseconds(80));
            var timed=await timeoutCache.LoadAsync("https://static-cdn.jtvnw.net/slow.png");Check(timed.Image is null&&timed.State.Contains("超时"),"image timeout produces a fallback");
            using(var cancel=new CancellationTokenSource(40))
            {
                var canceled=false;try{await cache.LoadAsync("https://static-cdn.jtvnw.net/cancel-slow.png",cancel.Token);}catch(OperationCanceledException){canceled=true;}
                Check(canceled,"caller cancellation terminates an in-flight image request");
            }
            var signed=good+"?signature=isolated-example-only";await cache.LoadAsync(signed);
            Check(!Path.GetFileName(cache.CachePath(signed)).Contains("signature")&&!Directory.GetFiles(Path.Combine(output,"cache")).Any(p=>p.Contains("isolated-example")),"cache filenames contain hashes, never signed URL parameters");
            var small=new VisualImageCache(Path.Combine(output,"bounded"),new ImageTransport(),limit:32);await small.LoadAsync(good);
            Check(new DirectoryInfo(Path.Combine(output,"bounded")).EnumerateFiles("*.img").Sum(f=>f.Length)<=32,"disk cache obeys its configured size limit");

            var imageControl=new VisualImage{ImageProvider=cache,SourceUrl="https://static-cdn.jtvnw.net/ui-slow.png",Width=100,Height=100};
            var window=new Window{Title="隔离图片组件回归 · 模拟数据",Width=250,Height=220,ShowActivated=false,ShowInTaskbar=false,Content=imageControl};
            window.Show();await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            imageControl.SourceUrl="https://static-cdn.jtvnw.net/blue.png";
            for(var i=0;i<80&&imageControl.LoadedImage is null;i++)await Task.Delay(25);
            bool blue=false;
            if(imageControl.LoadedImage is {} bitmap){var pixels=new byte[4];new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0).CopyPixels(new Int32Rect(0,0,1,1),pixels,4,0);blue=pixels[0]>200&&pixels[2]<20;}
            Check(blue,"actual WPF fast image switch displays the new reward, never a late prior image");
            imageControl.SourceUrl="https://static-cdn.jtvnw.net/unload-slow.png";await Task.Delay(30);window.Close();await Task.Delay(30);
            Check(transport.Canceled>0,"actual WPF unload cancels pending image loads");
            var deferred=new VisualImage{ImageProvider=cache,SourceUrl="https://static-cdn.jtvnw.net/offscreen.png",Height=100};
            var scrollContent=new System.Windows.Controls.StackPanel();scrollContent.Children.Add(new System.Windows.Controls.Border{Height=600});scrollContent.Children.Add(deferred);
            var viewport=new System.Windows.Controls.ScrollViewer{Content=scrollContent};var viewportWindow=new Window{Width=240,Height=180,ShowActivated=false,ShowInTaskbar=false,Content=viewport};viewportWindow.Show();
            await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(180);
            Check(transport.Count("/offscreen.png")==0,"offscreen reward controls do not download images before entering the viewport");
            viewport.ScrollToBottom();await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(250);
            Check(transport.Count("/offscreen.png")==1&&deferred.LoadedImage is not null,"scrolling into view loads the official thumbnail asynchronously once");viewportWindow.Close();

            var reward=RewardVisual.From(Json("""{"id":"many","name":"isolated test","benefits":[{"id":"a","name":"A","type":"DIRECT_ENTITLEMENT","imageUrl":""},{"id":"b","name":"B","type":"EMOTE","imageUrl":""},{"id":"c","name":"C","type":"BADGE","imageUrl":""}]}"""),"Twitch","");
            var gallery=new BenefitGallery{ItemsSource=reward.Benefits};var galleryWindow=new Window{Title="隔离多 Benefit 组件回归 · 模拟数据",Width=480,Height=280,ShowActivated=false,ShowInTaskbar=false,Content=gallery};galleryWindow.Show();
            await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(gallery.Items.Count==3&&gallery.ItemContainerGenerator.ContainerFromIndex(2) is not null,"actual WPF gallery realizes every Benefit, including the third item");galleryWindow.Close();
            var toggle=new System.Windows.Controls.CheckBox{Content="",IsChecked=true,Width=48,HorizontalAlignment=HorizontalAlignment.Left,Style=(Style)Application.Current.FindResource("SwitchCheckBox")};
            var toggleWindow=new Window{Width=240,Height=180,ShowActivated=false,ShowInTaskbar=false,Content=toggle};toggleWindow.Show();
            await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var track=toggle.Template.FindName("Track",toggle) as FrameworkElement;
            var trackBounds=track?.TransformToAncestor(toggle).TransformBounds(new Rect(0,0,track.ActualWidth,track.ActualHeight));
            Check(trackBounds is {} bounds&&bounds.Left>=0&&bounds.Right<=toggle.ActualWidth,"actual checked WPF switch fits inside its 48 DIP setting editor without clipping");toggleWindow.Close();
            checks.Add("NOT TESTED real account images in this isolated component suite; independently collected official account evidence is recorded in docs/ui-redesign-v2/real-twitch-e2e.md.");
        }
        catch(Exception ex){checks.Add("FAIL selftest exception: "+ex.GetType().Name);}
        await File.WriteAllLinesAsync(Path.Combine(output,"ui-v2-selftest.txt"),checks);
        return checks.Any(c=>c.StartsWith("FAIL "))?1:checks.Any(c=>c.StartsWith("BLOCKED "))?2:0;
    }

    private sealed class ImageTransport:HttpMessageHandler
    {
        internal Dictionary<string,int> Requests{get;}=[];internal int Canceled;
        internal int Count(string path){lock(Requests)return Requests.GetValueOrDefault(path);}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;lock(Requests)Requests[path]=Requests.GetValueOrDefault(path)+1;
            if(path.Contains("slow"))try{await Task.Delay(5000,ct);}catch(OperationCanceledException){Interlocked.Increment(ref Canceled);throw;}
            byte[] bytes;
            if(path.Contains("bad"))bytes=[0,1,2,3];
            else
            {
                var pixels=path.Contains("blue")?new byte[]{255,0,0,255}:new byte[]{0,0,255,255};
                var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,pixels,4);bitmap.Freeze();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();encoder.Save(output);bytes=output.ToArray();
            }
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
        }
    }
}
