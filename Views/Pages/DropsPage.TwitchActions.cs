using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.Services.Drops;
using CloudLightBlizzard.ViewModels.Visuals;
using CloudLightBlizzard.Views.Drops.Twitch;

namespace CloudLightBlizzard.Views.Pages;

public partial class DropsPage
{
    private bool _twitchBatchClaiming;
    private RewardVisual? FindTwitchReward(string id,string campaignId="") => _vm?.TwitchInventory.AllCampaigns
        .SelectMany(c=>c.Drops).FirstOrDefault(d=>d.Id==id&&(campaignId.Length==0||d.CampaignId==campaignId));

    private async Task ClaimTwitchRewardAsync(RewardVisual reward)
    {
        if(_vm is null)return;
        reward=FindTwitchReward(reward.Id,reward.CampaignId)??_vm.TwitchInventory.WithClaimAction(reward);
        var action=reward.ClaimAction;
        if(action is null||!action.Begin())return;
        var message="";
        var unknown=false;
        try
        {
            var result=await _vm.RequestAsync(DropsPlatform.Twitch,"claim_drop",new{id=reward.Id,campaignId=reward.CampaignId});
            if(result.TryGetProperty("state",out var state)&&state.ValueKind==JsonValueKind.Object)_vm.ApplyState(DropsPlatform.Twitch,state);
            if(Text(result,"status") is not ("claimed" or "already_claimed"))message=Text(result,"message","领取结果待确认，请刷新库存后重试。");
            unknown=Text(result,"status")=="unconfirmed";
        }
        catch{unknown=true;message="领取结果待确认，请先刷新官方库存；不会自动重复提交。";}
        finally{action.Finish(message,unknown);}
    }

    private async void ClaimAllTwitchRewards(object sender)
    {
        if(_vm is null||_twitchBatchClaiming||!_vm.TwitchInventory.BeginBatchClaim())return;
        _twitchBatchClaiming=true;
        var button=sender as Button;if(button is not null)button.IsEnabled=false;
        var actions=_vm.TwitchInventory.AllCampaigns.SelectMany(c=>c.Drops).Select(d=>d.ClaimAction).OfType<TwitchClaimAction>().Distinct().Where(a=>a.Begin()).ToArray();
        var knownResult=false;
        try
        {
            var result=await _vm.RequestAsync(DropsPlatform.Twitch,"claim_all_drops");
            knownResult=Text(result,"status") is "completed" or "inventory_error" or "not_running" or "busy";
            if(result.TryGetProperty("state",out var state)&&state.ValueKind==JsonValueKind.Object)_vm.ApplyState(DropsPlatform.Twitch,state);
            if(Text(result,"status")!="completed"){ShowInfo(Text(result,"message","请先连接 Twitch 并刷新奖励。"),"批量领取");return;}
            _vm.TwitchInventory.ApplyBatchClaim(result);
            var summary=_vm.TwitchInventory.BatchClaimSummary;
            if(result.TryGetProperty("results",out var rows))foreach(var row in rows.EnumerateArray())
            {
                var reward=FindTwitchReward(Text(row,"id"),Text(row,"campaignId"));
                if(Text(row,"status") is not ("claimed" or "already_claimed"))
                {
                    var reason=Text(row,"message","状态待确认");
                    summary+=$"\n\n{reward?.Title??"奖励"}：{reason}";
                    reward?.ClaimAction?.Finish(reason,Text(row,"status")=="unconfirmed");
                }
            }
            if(Text(result,"message").Length>0)summary+="\n\n"+Text(result,"message");
            _vm.Twitch.Notice=summary;
            ShowInfo(summary+"\n\n请刷新官方 Inventory 核对；待确认项不会自动重发。","批量领取结果");
        }
        catch{ShowInfo("批量请求结果待确认。请先刷新库存；已提交的请求不会自动重发。","批量领取");}
        finally
        {
            foreach(var action in actions.Where(a=>a.Busy))action.Finish(knownResult?"本批次未提交此奖励，请以最新官方库存为准。":"领取结果待确认，请刷新官方库存。",!knownResult);
            _vm.TwitchInventory.EndBatchClaim();
            _twitchBatchClaiming=false;if(button is not null)button.IsEnabled=true;
        }
    }

    private void OpenTwitchCampaignPriority()
    {
        if(_vm is null)return;
        var dialog=new CampaignPriorityWindow(_vm.TwitchInventory,async entries=>
        {
            var settings=await _vm.RequestAsync(DropsPlatform.Twitch,"save_settings",new{settings=new{campaign_watch_priority=entries}});
            _vm.TwitchInventory.Apply(JsonSerializer.SerializeToElement(new{settings}));
        }){Owner=Window.GetWindow(this)};
        dialog.ShowDialog();
    }

    private async void SaveTwitchDisplaySort(string sort)
    {
        if(_vm is null||_shutdownStarted)return;
        try{await _vm.RequestAsync(DropsPlatform.Twitch,"save_settings",new{settings=new{campaign_display_sort=sort}});}
        catch{_vm.Twitch.Notice="显示排序暂未保存，请检查后台状态后重新选择。";}
    }
}
