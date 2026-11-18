using System.Windows;

namespace CloudLightBlizzard.ViewModels.Visuals;

/// <summary>One mutable action state shared by every view of an official Drop.</summary>
public sealed class TwitchClaimAction : ObservableObject
{
    private bool _busy, _claimable, _claimed, _completed, _unknown;
    private string _sync = "";
    private string _message = "";
    public bool Busy => _busy;
    public string Message => _message;
    public string ButtonText => _busy ? "领取中…" : _claimed ? "已领取" : _unknown ? "领取结果待确认" : "领取";
    public bool CanExecute => !_busy && !_unknown && _claimable && _completed && !_claimed;
    public Visibility Visibility => _claimed || _claimable && _completed || _busy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MessageVisibility => _message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    internal void Update(RewardVisual reward)
    {
        _claimable = reward.CanClaim && reward.Eligible != false && !reward.Upcoming;
        _claimed = reward.Claimed; _completed = reward.WatchCompleted;
        if (_claimed || reward.SyncAt.Length > 0 && reward.SyncAt != _sync)
        { _unknown = false; _message = ""; }
        _sync = reward.SyncAt;
        Notify();
    }
    internal bool Begin()
    {
        if (!CanExecute) return false;
        _busy = true; _message = ""; Notify(); return true;
    }
    internal void Finish(string message, bool unknown = false) { _busy = false; _unknown = unknown && !_claimed; _message = _claimed ? "" : message; Notify(); }
    private void Notify()
    {
        foreach (var name in new[] { nameof(Busy), nameof(Message), nameof(ButtonText), nameof(CanExecute), nameof(Visibility), nameof(MessageVisibility) }) Raise(name);
    }
}
