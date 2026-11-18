using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.ViewModels.Visuals;

namespace CloudLightBlizzard.Services.Drops;

/// <summary>Real child process and JSONL with an explicitly offline Core transport.</summary>
internal static class TwitchAuditSelfTest
{
    internal static async Task RunAsync(string output, StringBuilder report)
    {
        var repo=new DirectoryInfo(AppContext.BaseDirectory);
        while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"Integrations","Drops","twitch","worker.py")))repo=repo.Parent;
        if(repo is null)throw new InvalidOperationException("Source checkout is required for this offline integration test.");
        var data=Path.Combine(output,"jsonl-data");Directory.CreateDirectory(data);
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var events=new List<string>();
        await using var host=new DropsHostService();
        host.ConfigureStartInfoForSelfTest(_=>
        {
            var start=new ProcessStartInfo(Path.Combine(repo.FullName,".worker-build-venv","Scripts","python.exe"))
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false)};
            foreach(var arg in new[]{Path.Combine(repo.FullName,"Integrations","Drops","twitch","tests","audit_jsonl_worker.py"),"--data-dir",data,"--log-file",Path.Combine(output,"jsonl-worker.log")})start.ArgumentList.Add(arg);
            return start;
        });
        host.EventReceived+=(_,e)=>{lock(events)events.Add(e.Name);};
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);report.AppendLine("PASS "+name);}
        var state=await host.RequestAsync(DropsPlatform.Twitch,"load_state",cancellationToken:budget.Token);
        var model=new TwitchInventoryModel(new(DropsPlatform.Twitch,"Twitch"));model.Apply(state);
        Check(model.AllCampaigns.All(c=>c.GameId=="1"&&c.Drops.All(d=>d.GameId=="1")),"actual Worker DTO retains stable Game IDs for the real activity priority dialog");
        var action=model.AllCampaigns.Single(c=>c.Id=="a").Drops.Single().ClaimAction!;
        Check(action.Begin()&&!action.Begin(),"JSONL inventory reaches UI and duplicate click is rejected");
        var response=await host.RequestAsync(DropsPlatform.Twitch,"claim_drop",new{id="a-drop",campaignId="a"},budget.Token);
        model.Apply(response.GetProperty("state"));action.Finish("");
        Check(response.GetProperty("status").GetString()=="claimed"&&action.ButtonText=="已领取"&&!action.CanExecute,"Host to Worker to Core to official fixture Inventory to WPF action confirms claim");
        response=await host.RequestAsync(DropsPlatform.Twitch,"claim_drop",new{id="a-drop",campaignId="a"},budget.Token);
        Check(response.GetProperty("status").GetString()=="already_claimed","second JSONL command never replays confirmed claim");
        response=await host.RequestAsync(DropsPlatform.Twitch,"claim_all_drops",cancellationToken:budget.Token);
        model.Apply(response.GetProperty("state"));model.ApplyBatchClaim(response);
        Check(response.GetProperty("total").GetInt32()==1&&response.GetProperty("success").GetInt32()==1&&model.AllCampaigns.SelectMany(c=>c.Drops).All(d=>!d.ClaimAction!.CanExecute),"batch official snapshot excludes claimed reward and synchronizes all UI cards");
        Check(File.ReadAllText(Path.Combine(data,"mutations.json"))=="[\"a\", \"b\"]","exactly one fixture mutation per stable Drop instance");
        var settings=await host.RequestAsync(DropsPlatform.Twitch,"save_settings",new{settings=new{campaign_watch_priority=new Dictionary<string,object>{{"1",new{mode="custom",campaignIds=new[]{"b","a"}}}}}},budget.Token);
        Check(settings.GetProperty("campaign_watch_priority").GetProperty("1").GetProperty("campaignIds")[0].GetString()=="b","activity priority persists through actual Host and JSONL save_settings");
        var generation=state.GetProperty("_workerGeneration").GetInt64();
        Check(!host.IsCurrentGeneration(DropsPlatform.Twitch,generation+1),"Host rejects receipts belonging to another Worker generation");
        var stopped=await host.StopAsync(DropsPlatform.Twitch,budget.Token);
        Check(!stopped.GetProperty("running").GetBoolean(),"explicit stop returns a stopped Worker state");
        lock(events)Check(events.Contains("claim_progress")&&events.Contains("claim_result"),"batch progress and individual results traverse real JSONL events");
        File.WriteAllLines(Path.Combine(output,"jsonl-events.txt"),events);
    }
}
