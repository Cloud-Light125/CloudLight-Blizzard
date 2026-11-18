using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.Controls;
using CloudLightBlizzard.ViewModels;
using CloudLightBlizzard.ViewModels.Visuals;
using CloudLightBlizzard.Services;
using CloudLightBlizzard.Services.Drops;
namespace CloudLightBlizzard.Views.Pages;

public partial class DropsPage
{
    private StackPanel SoopPanel => SoopWorkspace.SoopPanel;
    private Border SoopAccountsCard => SoopWorkspace.SoopAccountsCard;
    private Border SoopMultiAccountWarningCard => SoopWorkspace.SoopMultiAccountWarningCard;
    private Expander SoopAddAccountExpander => SoopWorkspace.SoopAddAccountExpander;
    private TextBox SoopUserId => SoopWorkspace.SoopUserId;
    private PasswordBox SoopPassword => SoopWorkspace.SoopPassword;
    private TextBlock SoopPasswordHint => SoopWorkspace.SoopPasswordHint;
    private ListBox SoopAccountsList => SoopWorkspace.SoopAccountsList;
    private ListBox SoopInventoryList => SoopWorkspace.SoopInventoryList;
    private Border SoopSettingsCard => SoopWorkspace.SoopSettingsCard;
    private ComboBox SoopChannelMode => SoopWorkspace.SoopChannelMode;
    private ComboBox SoopPriorityMission => SoopWorkspace.SoopPriorityMission;
    private StackPanel SoopManualChannelPanel => SoopWorkspace.SoopManualChannelPanel;
    private TextBox SoopManualChannel => SoopWorkspace.SoopManualChannel;
    private StackPanel SoopPreferredChannelPanel => SoopWorkspace.SoopPreferredChannelPanel;
    private TextBox SoopPreferredChannel => SoopWorkspace.SoopPreferredChannel;
    private CheckBox SoopLowBandwidth => SoopWorkspace.SoopLowBandwidth;
    private CheckBox SoopAutoClaim => SoopWorkspace.SoopAutoClaim;
    private CheckBox SoopHangWithoutMissions => SoopWorkspace.SoopHangWithoutMissions;
    private CheckBox SoopAutoStart => SoopWorkspace.SoopAutoStart;
    private TextBox SoopMissionInterval => SoopWorkspace.SoopMissionInterval;
    private TextBox SoopInventoryInterval => SoopWorkspace.SoopInventoryInterval;
    private TextBox SoopChannelInterval => SoopWorkspace.SoopChannelInterval;
    private TextBox SoopReauthInterval => SoopWorkspace.SoopReauthInterval;
    private Border SoopHelp => SoopWorkspace.SoopOverview.SoopHelp;
    private StackPanel TwitchPanel => TwitchWorkspace.TwitchPanel;
    private ComboBox TwitchCampaignScopePicker => TwitchWorkspace.TwitchCampaignScopePicker;
    private ListBox TwitchChannelsList => TwitchWorkspace.TwitchChannelsList;
    private Border TwitchSettingsCard => TwitchWorkspace.TwitchSettingsCard;
    private ComboBox TwitchLanguage => TwitchWorkspace.TwitchLanguage;
    private ComboBox TwitchPriorityMode => TwitchWorkspace.TwitchPriorityMode;
    private ComboBox TwitchConnectionQuality => TwitchWorkspace.TwitchConnectionQuality;
    private ComboBox TwitchPriorityPicker => TwitchWorkspace.TwitchPriorityPicker;
    private ListBox TwitchPriorityList => TwitchWorkspace.TwitchPriorityList;
    private ComboBox TwitchExcludePicker => TwitchWorkspace.TwitchExcludePicker;
    private ListBox TwitchExcludeList => TwitchWorkspace.TwitchExcludeList;
    private TextBlock TwitchGamesEmptyHint => TwitchWorkspace.TwitchGamesEmptyHint;
    private CheckBox TwitchAutoStart => TwitchWorkspace.TwitchAutoStart;
    private CheckBox TwitchTrayNotifications => TwitchWorkspace.TwitchTrayNotifications;
    private CheckBox TwitchBadgesEmotes => TwitchWorkspace.TwitchBadgesEmotes;
    private CheckBox TwitchAvailableDrops => TwitchWorkspace.TwitchAvailableDrops;
    private CheckBox TwitchAutoClaim => TwitchWorkspace.TwitchAutoClaim;
    private Border TwitchLoginCard => TwitchWorkspace.TwitchOverview.TwitchLoginCard;
    private Button TwitchRefreshButton => TwitchWorkspace.TwitchOverview.TwitchRefreshButton;
    private TextBlock TwitchIntro => TwitchWorkspace.TwitchOverview.TwitchIntro;
    private Button TwitchLoginAction => TwitchWorkspace.TwitchOverview.TwitchLoginAction;
    private Button TwitchCopyCodeButton => TwitchWorkspace.TwitchOverview.TwitchCopyCodeButton;
    private TextBox TwitchAuthorizationCodeBox => TwitchWorkspace.TwitchOverview.TwitchAuthorizationCodeBox;
    private Border TwitchCurrentProgressCard => TwitchWorkspace.TwitchOverview.TwitchCurrentProgressCard;
    private Border TwitchHelp => TwitchWorkspace.TwitchOverview.TwitchHelp;
    private StackPanel BilibiliPanel => BilibiliWorkspace.BilibiliPanel;
    private PasswordBox BilibiliCookieBox => BilibiliWorkspace.BilibiliCookieBox;
    private ListBox BilibiliSessionList => BilibiliWorkspace.BilibiliSessionList;
    private TextBox BilibiliRoomInput => BilibiliWorkspace.BilibiliRoomInput;
    private TextBox BilibiliRoomNameInput => BilibiliWorkspace.BilibiliRoomNameInput;
    private ListBox BilibiliRoomList => BilibiliWorkspace.BilibiliRoomList;
    private ListBox BilibiliActivityList => BilibiliWorkspace.BilibiliActivityList;
    private ListBox BilibiliTaskList => BilibiliWorkspace.BilibiliTaskList;
    private Border BilibiliSettingsCard => BilibiliWorkspace.BilibiliSettingsCard;
    private ComboBox BilibiliWatchModePicker => BilibiliWorkspace.BilibiliWatchModePicker;
    private TextBox BilibiliSessionsPerRoomText => BilibiliWorkspace.BilibiliSessionsPerRoomText;
    private TextBox BilibiliReconnectDelayText => BilibiliWorkspace.BilibiliReconnectDelayText;
    private TextBox BilibiliTaskIntervalText => BilibiliWorkspace.BilibiliTaskIntervalText;
    private TextBox BilibiliTaskIdsText => BilibiliWorkspace.BilibiliTaskIdsText;
    private Border BilibiliNetworkCard => BilibiliWorkspace.BilibiliNetworkCard;
    private CheckBox BilibiliEnabledCheck => BilibiliWorkspace.BilibiliEnabledCheck;
    private CheckBox BilibiliAutoTaskProgress => BilibiliWorkspace.BilibiliAutoTaskProgress;
    private CheckBox BilibiliAutoDiscoverCheck => BilibiliWorkspace.BilibiliAutoDiscoverCheck;
    private CheckBox BilibiliAutoClaimCheck => BilibiliWorkspace.BilibiliAutoClaimCheck;
    private CheckBox BilibiliTaskNotificationsCheck => BilibiliWorkspace.BilibiliTaskNotificationsCheck;
    private CheckBox BilibiliReconnectCheck => BilibiliWorkspace.BilibiliReconnectCheck;
    private CheckBox BilibiliAutoStart => BilibiliWorkspace.BilibiliAutoStart;
    private CheckBox BilibiliAutoResume => BilibiliWorkspace.BilibiliAutoResume;
    private PasswordBox BilibiliNotifierUrlBox => BilibiliWorkspace.BilibiliNotifierUrlBox;
    private Border BilibiliAccountCard => BilibiliWorkspace.BilibiliOverview.BilibiliAccountCard;
    private Button BilibiliStartButton => BilibiliWorkspace.BilibiliOverview.BilibiliStartButton;
    private Button BilibiliStopButton => BilibiliWorkspace.BilibiliOverview.BilibiliStopButton;
    private Button BilibiliRefreshButton => BilibiliWorkspace.BilibiliOverview.BilibiliRefreshButton;
    private Button BilibiliQrLoginButton => BilibiliWorkspace.BilibiliOverview.BilibiliQrLoginButton;
    private Border BilibiliQrCard => BilibiliWorkspace.BilibiliOverview.BilibiliQrCard;
    private Button BilibiliQrCancelButton => BilibiliWorkspace.BilibiliOverview.BilibiliQrCancelButton;
    private Button BilibiliDiscoverButton => BilibiliWorkspace.BilibiliOverview.BilibiliDiscoverButton;
    private Expander BilibiliQuickStartCard => BilibiliWorkspace.BilibiliOverview.BilibiliQuickStartCard;
    private StackPanel YouTubePanel => YouTubeWorkspace.YouTubePanel;
    private Border YouTubeAccountsCard => YouTubeWorkspace.YouTubeAccountsCard;
    private ListBox YouTubeProfilesList => YouTubeWorkspace.YouTubeProfilesList;
    private TextBox YouTubeProfileName => YouTubeWorkspace.YouTubeProfileName;
    private Border YouTubeChannelsCard => YouTubeWorkspace.YouTubeChannelsCard;
    private ListBox YouTubeChannelsList => YouTubeWorkspace.YouTubeChannelsList;
    private TextBox YouTubeChannelName => YouTubeWorkspace.YouTubeChannelName;
    private TextBox YouTubeChannelId => YouTubeWorkspace.YouTubeChannelId;
    private TextBox YouTubeChannelUrl => YouTubeWorkspace.YouTubeChannelUrl;
    private Border YouTubeBrowserCard => YouTubeWorkspace.YouTubeBrowserCard;
    private ComboBox YouTubeBrowser => YouTubeWorkspace.YouTubeBrowser;
    private TextBox YouTubeBrowserPath => YouTubeWorkspace.YouTubeBrowserPath;
    private ComboBox YouTubeMode => YouTubeWorkspace.YouTubeMode;
    private StackPanel YouTubeManualUrlPanel => YouTubeWorkspace.YouTubeManualUrlPanel;
    private TextBox YouTubeManualUrl => YouTubeWorkspace.YouTubeManualUrl;
    private CheckBox YouTubeHeadless => YouTubeWorkspace.YouTubeHeadless;
    private CheckBox YouTubeMute => YouTubeWorkspace.YouTubeMute;
    private TextBox YouTubeInterval => YouTubeWorkspace.YouTubeInterval;
    private Button YouTubeStartButton => YouTubeWorkspace.YouTubeOverview.YouTubeStartButton;
    private Button YouTubeStopButton => YouTubeWorkspace.YouTubeOverview.YouTubeStopButton;
    private Border YouTubeHelp => YouTubeWorkspace.YouTubeOverview.YouTubeHelp;
    public new object FindName(string name) => base.FindName(name) ?? SoopWorkspace.FindName(name) ?? TwitchWorkspace.FindName(name) ?? BilibiliWorkspace.FindName(name) ?? YouTubeWorkspace.FindName(name) ?? SoopWorkspace.SoopOverview.FindName(name) ?? TwitchWorkspace.TwitchOverview.FindName(name) ?? BilibiliWorkspace.BilibiliOverview.FindName(name) ?? YouTubeWorkspace.YouTubeOverview.FindName(name)!;
    private void ConnectVisualWorkspaces()
    {
        SoopWorkspace.ActionRequested += RunLegacyAction;
        TwitchWorkspace.ActionRequested += RunLegacyAction;
        BilibiliWorkspace.ActionRequested += RunLegacyAction;
        YouTubeWorkspace.ActionRequested += RunLegacyAction;
        AddHandler(WorkspaceAction.RequestEvent, new EventHandler<WorkspaceActionEventArgs>(OnVisualAction));
    }
    private void OnVisualAction(object? sender, WorkspaceActionEventArgs e)
    {
        e.Handled = true;
        if(e.Action=="twitch:priority"){OpenTwitchCampaignPriority();return;}
        if(e.Action=="twitch:claim-all"){ClaimAllTwitchRewards(e.Control);return;}
        if(e.Action=="twitch:cancel-claims"){_=StopPlatformAsync(DropsPlatform.Twitch);return;}
        if(e.Action=="twitch:claim"&&e.Control is FrameworkElement{DataContext:RewardVisual claimReward})
        { _=ClaimTwitchRewardAsync(claimReward);return; }
        if(e.Action=="twitch:overview-all"){if(_vm is not null)_vm.TwitchInventory.OverviewPriorityOnly=false;return;}
        if(e.Action=="twitch:channels")
        {
            ShowWorkspace("activities");TwitchWorkspace.TwitchChannelsExpander.IsExpanded=true;
            TwitchWorkspace.TwitchChannelsExpander.BringIntoView();return;
        }
        if(e.Action=="twitch:campaign-details"&&e.Control is FrameworkElement{DataContext:CampaignRewardGroup group})
        {
            var inventory=new CampaignInventoryView{ItemsSource=new[]{group},Margin=new Thickness(16),Height=double.NaN,CampaignActionVisibility=Visibility.Collapsed};
            var dialog=new Window{Owner=Window.GetWindow(this),Title="Twitch 活动详情 · "+group.Title,Width=Math.Min(Math.Clamp(80+group.Cards.Count*206,400,1180),SystemParameters.WorkArea.Width-40),SizeToContent=SizeToContent.Height,MaxHeight=Math.Min(740,SystemParameters.WorkArea.Height-40),WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=inventory};
            dialog.SetResourceReference(BackgroundProperty,"App.Background");dialog.SetResourceReference(ForegroundProperty,"Text.Primary");ThemeManager.Attach(dialog);
            dialog.AddHandler(WorkspaceAction.RequestEvent,new EventHandler<WorkspaceActionEventArgs>(OnVisualAction));dialog.ShowDialog();return;
        }
        if((e.Action=="twitch:reward-details"||e.Action=="reward:details")&&e.Control is FrameworkElement{DataContext:{} context})
        {
            var drop=context is RewardCardVisual card?card.Drop:context as RewardVisual;
            if(drop is null)return;
            if(drop.Brand!="Twitch"){ShowWorkspace(drop.Brand=="Soop"?"accounts":"activities");return;}
            drop=FindTwitchReward(drop.Id,drop.CampaignId)??_vm?.TwitchInventory.WithClaimAction(drop)??drop;
            var details=new StackPanel();
            var cards=_vm?.TwitchInventory.AllCampaigns.FirstOrDefault(c=>c.Id==drop.CampaignId)?.Cards.Where(c=>c.Drop.Id==drop.Id).ToArray();
            details.Children.Add(new ContentControl{Content=new{Cards=(IReadOnlyList<RewardCardVisual>?)cards??drop.Cards},ContentTemplate=(DataTemplate)FindResource("VisualRewardRow")});
            details.Children.Add(new TextBlock{Text=drop.QualificationText+"\n"+drop.PrerequisiteText+"\n"+drop.SyncText,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
            var content=new ContentControl{Content=details,Margin=new Thickness(16)};
            var dialog=new Window{Owner=Window.GetWindow(this),Title="Twitch 奖励详情 · "+drop.Title,Width=Math.Min(Math.Clamp(80+drop.Cards.Count*240,440,900),SystemParameters.WorkArea.Width-40),SizeToContent=SizeToContent.Height,MaxHeight=Math.Min(740,SystemParameters.WorkArea.Height-40),WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
            dialog.SetResourceReference(BackgroundProperty,"App.Background");dialog.SetResourceReference(ForegroundProperty,"Text.Primary");
            ThemeManager.Attach(dialog);dialog.AddHandler(WorkspaceAction.RequestEvent,new EventHandler<WorkspaceActionEventArgs>(OnVisualAction));
            content.Loaded+=(_,_)=>ExpandRewardDetails(content);
            dialog.ShowDialog();return;
        }
        if (e.Action.StartsWith("legacy:")) { RunLegacyAction(e.Action[7..], e.Control, new RoutedEventArgs()); return; }
        if (e.Action.StartsWith("section:")) { ShowWorkspace(e.Action[8..]); return; }
        if (e.Action.StartsWith("quick:")) { OnQuickStartAction(new Button { Tag = e.Action[6..] }, new RoutedEventArgs()); return; }
        if (e.Action.StartsWith("soop:") && e.Control is FrameworkElement { DataContext: AccountVisual account })
        {
            SoopAccountsList.SelectedItem = _vm?.Accounts.FirstOrDefault(r => r.Id == account.Id);
            if (e.Action == "soop:start") OnSoopStartAccount(e.Control, new RoutedEventArgs());
            else if (e.Action == "soop:stop") OnSoopStopAccount(e.Control, new RoutedEventArgs());
            else ShowWorkspace("accounts");
        }
    }
    private static void ExpandRewardDetails(DependencyObject parent)
    {
        if(parent is Expander expander)expander.IsExpanded=true;
        if(parent is RewardCard card)card.DetailsVisibility=Visibility.Collapsed;
        for(var index=0;index<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);index++)ExpandRewardDetails(System.Windows.Media.VisualTreeHelper.GetChild(parent,index));
    }
    private void RunLegacyAction(string action, object sender, RoutedEventArgs args)
    {
        switch (action)
        {
            case "OnWorkspaceChanged": OnWorkspaceChanged(sender, (RoutedEventArgs)args); break;
            case "OnNotifierDraftChanged": OnNotifierDraftChanged(sender, (RoutedEventArgs)args); break;
            case "OnCancelTwitchAuthorization": OnCancelTwitchAuthorization(sender, (RoutedEventArgs)args); break;
            case "OnRenewTwitchAuthorization": OnRenewTwitchAuthorization(sender, (RoutedEventArgs)args); break;
            case "OnPlatformChanged": OnPlatformChanged(sender, (RoutedEventArgs)args); break;
            case "OnStart": OnStart(sender, (RoutedEventArgs)args); break;
            case "OnActiveStart": OnActiveStart(sender, (RoutedEventArgs)args); break;
            case "OnActiveStop": OnActiveStop(sender, (RoutedEventArgs)args); break;
            case "OnActiveRetry": OnActiveRetry(sender, (RoutedEventArgs)args); break;
            case "OnActiveInventory": OnActiveInventory(sender, (RoutedEventArgs)args); break;
            case "OnShowDropsLog": OnShowDropsLog(sender, (RoutedEventArgs)args); break;
            case "OnOpenCurrentWatch": OnOpenCurrentWatch(sender, (RoutedEventArgs)args); break;
            case "OnNetworkCheck": OnNetworkCheck(sender, (RoutedEventArgs)args); break;
            case "OnStop": OnStop(sender, (RoutedEventArgs)args); break;
            case "OnRefresh": OnRefresh(sender, (RoutedEventArgs)args); break;
            case "OnSoopRefresh": OnSoopRefresh(sender, (RoutedEventArgs)args); break;
            case "OnOpenProxySettings": OnOpenProxySettings(sender, (RoutedEventArgs)args); break;
            case "OnDropsProxyScopeChanged": OnDropsProxyScopeChanged(sender, (RoutedEventArgs)args); break;
            case "OnToggleHelp": OnToggleHelp(sender, (RoutedEventArgs)args); break;
            case "OnQuickStartAction": OnQuickStartAction(sender, (RoutedEventArgs)args); break;
            case "OnSoopChannelModeChanged": OnSoopChannelModeChanged(sender, (SelectionChangedEventArgs)args); break;
            case "OnSoopPasswordChanged": OnSoopPasswordChanged(sender, (RoutedEventArgs)args); break;
            case "OnSaveSoop": OnSaveSoop(sender, (RoutedEventArgs)args); break;
            case "OnSoopAddAccount": OnSoopAddAccount(sender, (RoutedEventArgs)args); break;
            case "OnSoopStartAccount": OnSoopStartAccount(sender, (RoutedEventArgs)args); break;
            case "OnSoopStopAccount": OnSoopStopAccount(sender, (RoutedEventArgs)args); break;
            case "OnSoopSetPrimaryAccount": OnSoopSetPrimaryAccount(sender, (RoutedEventArgs)args); break;
            case "OnSoopDeleteAccount": OnSoopDeleteAccount(sender, (RoutedEventArgs)args); break;
            case "OnClaimSoopReward": OnClaimSoopReward(sender, (RoutedEventArgs)args); break;
            case "OnCopySoopCode": OnCopySoopCode(sender, (RoutedEventArgs)args); break;
            case "OnOpenSoopInventory": OnOpenSoopInventory(sender, (RoutedEventArgs)args); break;
            case "OnYouTubeModeChanged": OnYouTubeModeChanged(sender, (SelectionChangedEventArgs)args); break;
            case "OnYouTubeBrowserChanged": OnYouTubeBrowserChanged(sender, (SelectionChangedEventArgs)args); break;
            case "OnBrowseYouTubeBrowser": OnBrowseYouTubeBrowser(sender, (RoutedEventArgs)args); break;
            case "OnSaveYouTube": OnSaveYouTube(sender, (RoutedEventArgs)args); break;
            case "OnYouTubeAddProfile": OnYouTubeAddProfile(sender, (RoutedEventArgs)args); break;
            case "OnYouTubeLogin": OnYouTubeLogin(sender, (RoutedEventArgs)args); break;
            case "OnYouTubeDeleteProfile": OnYouTubeDeleteProfile(sender, (RoutedEventArgs)args); break;
            case "OnYouTubeAddChannel": OnYouTubeAddChannel(sender, (RoutedEventArgs)args); break;
            case "OnYouTubeDeleteChannel": OnYouTubeDeleteChannel(sender, (RoutedEventArgs)args); break;
            case "OnYouTubeToggleChannel": OnYouTubeToggleChannel(sender, (RoutedEventArgs)args); break;
            case "OnTwitchLogin": OnTwitchLogin(sender, (RoutedEventArgs)args); break;
            case "OnOpenTwitchAuthorization": OnOpenTwitchAuthorization(sender, (RoutedEventArgs)args); break;
            case "OnCopyTwitchAuthorizationCode": OnCopyTwitchAuthorizationCode(sender, (RoutedEventArgs)args); break;
            case "OnAutoStartChanged": OnAutoStartChanged(sender, (RoutedEventArgs)args); break;
            case "OnTwitchLogout": OnTwitchLogout(sender, (RoutedEventArgs)args); break;
            case "OnTwitchRetry": OnTwitchRetry(sender, (RoutedEventArgs)args); break;
            case "OnRepairTwitchSettings": OnRepairTwitchSettings(sender, (RoutedEventArgs)args); break;
            case "OnSoopRetryNow": OnSoopRetryNow(sender, (RoutedEventArgs)args); break;
            case "OnTwitchRetryNow": OnTwitchRetryNow(sender, (RoutedEventArgs)args); break;
            case "OnTwitchReload": OnTwitchReload(sender, (RoutedEventArgs)args); break;
            case "OnTwitchCampaignScopeChanged": OnTwitchCampaignScopeChanged(sender, (SelectionChangedEventArgs)args); break;
            case "OnAddPriorityGame": OnAddPriorityGame(sender, (RoutedEventArgs)args); break;
            case "OnAddExcludedGame": OnAddExcludedGame(sender, (RoutedEventArgs)args); break;
            case "OnPriorityUp": OnPriorityUp(sender, (RoutedEventArgs)args); break;
            case "OnPriorityDown": OnPriorityDown(sender, (RoutedEventArgs)args); break;
            case "OnRemovePriorityGame": OnRemovePriorityGame(sender, (RoutedEventArgs)args); break;
            case "OnRemoveExcludedGame": OnRemoveExcludedGame(sender, (RoutedEventArgs)args); break;
            case "OnSaveTwitch": OnSaveTwitch(sender, (RoutedEventArgs)args); break;
            case "OnTwitchSelectChannel": OnTwitchSelectChannel(sender, (RoutedEventArgs)args); break;
            case "OnOpenTwitchInventory": OnOpenTwitchInventory(sender, (RoutedEventArgs)args); break;
            case "OnClaimTwitchDrop": OnClaimTwitchDrop(sender, (RoutedEventArgs)args); break;
            case "OnCopyAllLogs": OnCopyAllLogs(sender, (RoutedEventArgs)args); break;
            case "OnDismissNotice": OnDismissNotice(sender, (RoutedEventArgs)args); break;
            case "OnClearLogDisplay": OnClearLogDisplay(sender, (RoutedEventArgs)args); break;
            case "OnLogTextChanged": OnLogTextChanged(sender, (TextChangedEventArgs)args); break;
            case "OnImport": OnImport(sender, (RoutedEventArgs)args); break;
        }
    }
}
