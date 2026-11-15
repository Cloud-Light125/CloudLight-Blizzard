# 前端功能盘点

基线 UI 契约 754 条，包含所有 Views、Controls 与 Stats XAML、绑定、交互控件和确认/命令代码入口。
这是可审计的声明条目数，不是互不重复的业务功能数。每条入口可能包含多项状态或保护。

| 源文件 | 契约数量 |
|---|---|
| Controls/AnnouncementBell.xaml | 2 |
| Controls/PageHeader.xaml | 3 |
| Stats/CareerWindow.xaml | 51 |
| Stats/HeroDetailWindow.xaml | 14 |
| Views/AccountSettingsWindow.xaml | 5 |
| Views/AnnouncementWindow.xaml | 6 |
| Views/CloseChoiceWindow.xaml | 5 |
| Views/DeleteConfirmWindow.xaml | 3 |
| Views/ExitConfirmationDialog.xaml | 4 |
| Views/LoginNewWindow.xaml | 3 |
| Views/MainWindow.xaml | 11 |
| Views/MainWindow.xaml.cs | 9 |
| Views/Pages/AboutPage.xaml | 13 |
| Views/Pages/AboutPage.xaml.cs | 1 |
| Views/Pages/AccountsPage.xaml | 31 |
| Views/Pages/AccountsPage.xaml.cs | 8 |
| Views/Pages/DiagnosticsPage.xaml | 20 |
| Views/Pages/DiagnosticsPage.xaml.cs | 2 |
| Views/Pages/DropsPage.xaml | 334 |
| Views/Pages/DropsPage.xaml.cs | 12 |
| Views/Pages/FeedbackPanel.xaml | 10 |
| Views/Pages/FeedbackPanel.xaml.cs | 3 |
| Views/Pages/RegionFilesPage.xaml | 94 |
| Views/Pages/RegionFilesPage.xaml.cs | 9 |
| Views/Pages/SettingsPage.xaml | 24 |
| Views/Pages/SettingsPage.xaml.cs | 5 |
| Views/Pages/SnapshotsPage.xaml | 43 |
| Views/Pages/SnapshotsPage.xaml.cs | 2 |
| Views/PlatformErrorWindow.xaml | 2 |
| Views/RegionActionConfirmWindow.xaml | 3 |
| Views/RegionSwitchChoiceWindow.xaml | 3 |
| Views/RegionSwitchChoiceWindow.xaml.cs | 1 |
| Views/SnapshotConfirmWindow.xaml | 4 |
| Views/SoopNoticeWindow.xaml | 2 |
| Views/SwitchPreviewWindow.xaml | 3 |
| Views/UpdateDialog.xaml | 6 |
| Views/UpdateDialog.xaml.cs | 1 |
| Views/UploadStalledWindow.xaml | 2 |

## 原有设置字段（53）

缺失字段：无。序列化、默认值与保存服务未改动；仅扩展总览导航取值。

- `ClientExe`
- `CloseToTray`
- `CloseChoiceMade`
- `StartMinimized`
- `DarkMode`
- `EnableProxy`
- `ProxyUrl`
- `FallbackDirect`
- `SoopUseProxy`
- `YouTubeUseProxy`
- `TwitchUseProxy`
- `ShowAnnouncementBadge`
- `CloudServiceBaseUrl`
- `AutoStartSoop`
- `AutoStartTwitch`
- `BilibiliEnabled`
- `BilibiliUseProxy`
- `AutoStartBilibili`
- `AutoResumeBilibiliDrops`
- `BilibiliUserName`
- `BilibiliUid`
- `BilibiliCredentialBlob`
- `BilibiliRoomIds`
- `BilibiliTaskIds`
- `BilibiliWatchMode`
- `BilibiliSessionsPerRoom`
- `BilibiliReconnectDelaySeconds`
- `BilibiliTaskIntervalSeconds`
- `BilibiliAutoClaim`
- `BilibiliTaskNotifications`
- `WindowWidth`
- `WindowHeight`
- `WindowMaximized`
- `HiddenAccountIds`
- `ExpiredAccountIds`
- `OverwatchGamePath`
- `RegionStoragePath`
- `RegionBackupMode`
- `MigrationCompleted`
- `SkippedUpdateVersion`
- `RemindAfter`
- `LastUpdateCheckAt`
- `LastUpdateFailure`
- `LastSuccessfulUpdateFrom`
- `LastSuccessfulUpdateTo`
- `LastSuccessfulUpdateAt`
- `EnableWindowsNotifications`
- `NotifyRegionSwitch`
- `NotifyDrops`
- `NotifyUpdates`
- `NotifyAnnouncements`
- `LastMainSection`
- `AccountPreferences`

## 页面事件与 Command 对照

### Controls/AnnouncementBell.xaml.cs

`OnOpenAnnouncements`

### Stats/CareerWindow.xaml.cs

`OnHeroSort`, `OnInputChanged`, `OnModeChanged`, `OnQuery`, `OnSearchKeyDown`, `OnRefresh`, `OnHeroClick`, `OnOverlayPrimary`, `OnOverlaySecondary`

### Stats/HeroDetailWindow.xaml.cs

`OnStatModeChanged`

### Views/AccountSettingsWindow.xaml.cs

`OnSave`

### Views/AnnouncementWindow.xaml.cs

`OnAnnouncementSelected`, `OnClose`, `OnPropertyChanged`

### Views/CloseChoiceWindow.xaml.cs

`OnDrag`, `OnConfirm`, `OnCancel`

### Views/DeleteConfirmWindow.xaml.cs

`OnDrag`, `OnConfirm`, `OnCancel`

### Views/ExitConfirmationDialog.xaml.cs

`OnDrag`, `OnMinimizeToTray`, `OnExit`, `OnCancel`

### Views/LoginNewWindow.xaml.cs

`OnDrag`, `OnConfirm`, `OnCancel`

### Views/MainWindow.xaml.cs

`OnRegionSwitchCompleted`, `OnDropsEventForNotification`, `OnNavigate`, `OnRefresh`, `OnShowSignal`, `OnTrayDoubleClick`, `OnTrayOpen`, `OnTrayExit`

### Views/Pages/AboutPage.xaml.cs

`OnOpenSource`, `OnOpenLocalPath`

### Views/Pages/AccountsPage.xaml.cs

`OnOpenSnapshots`, `OnLaunchClient`, `OnSaveCurrent`, `OnAddAccount`, `OnSwitchCard`, `OnStats`, `OnEdit`, `OnDelete`, `OnHide`

### Views/Pages/DiagnosticsPage.xaml.cs

`OnStart`, `OnCancel`, `OnCopy`, `OnExport`

### Views/Pages/DropsPage.xaml.cs

`OnPlatformChanged`, `OnOpenPlatform`, `OnStart`, `OnActiveStart`, `OnActiveStop`, `OnActiveRetry`, `OnActiveInventory`, `OnShowDropsLog`, `OnOpenCurrentWatch`, `OnNetworkCheck`, `OnStop`, `OnRefresh`, `OnSoopRefresh`, `OnOpenProxySettings`, `OnDropsProxyScopeChanged`, `OnToggleHelp`, `OnQuickStartAction`, `OnSoopChannelModeChanged`, `OnSoopPasswordChanged`, `OnSaveSoop`, `OnSoopAddAccount`, `OnSoopStartAccount`, `OnSoopStopAccount`, `OnSoopSetPrimaryAccount`, `OnSoopDeleteAccount`, `OnClaimSoopReward`, `OnCopySoopCode`, `OnOpenSoopInventory`, `OnYouTubeModeChanged`, `OnYouTubeBrowserChanged`, `OnBrowseYouTubeBrowser`, `OnSaveYouTube`, `OnYouTubeAddProfile`, `OnYouTubeLogin`, `OnYouTubeDeleteProfile`, `OnYouTubeAddChannel`, `OnYouTubeDeleteChannel`, `OnYouTubeToggleChannel`, `OnTwitchLogin`, `OnOpenTwitchAuthorization`, `OnCopyTwitchAuthorizationCode`, `OnAutoStartChanged`, `OnTwitchLogout`, `OnTwitchRetry`, `OnRepairTwitchSettings`, `OnSoopRetryNow`, `OnTwitchRetryNow`, `OnTwitchReload`, `OnTwitchCampaignScopeChanged`, `OnAddPriorityGame`, `OnAddExcludedGame`, `OnPriorityUp`, `OnPriorityDown`, `OnRemovePriorityGame`, `OnRemoveExcludedGame`, `OnSaveTwitch`, `OnTwitchSelectChannel`, `OnOpenTwitchInventory`, `OnClaimTwitchDrop`, `OnLogTailChanged`, `OnCopyAllLogs`, `OnDismissNotice`, `OnClearLogDisplay`, `OnLogTextChanged`, `OnImport`, `OnWorkerEvent`

### Views/Pages/FeedbackPanel.xaml.cs

`OnPreviewLogs`, `OnSubmit`, `OnStallTimerTick`, `OnCancelUpload`, `OnCopyReportId`, `OnCopyQqGroup`

### Views/Pages/RegionFilesPage.xaml.cs

`OnSwitchChina`, `OnSwitchInternational`, `OnChooseGame`, `OnChooseStorage`, `OnDetectGame`, `OnOpenStorage`, `OnOpenLogs`, `OnCaptureChina`, `OnCaptureInternational`, `OnComplete`, `OnReprepare`, `OnClear`, `OnCancel`, `OnRetry`, `OnVerifiedMode`, `OnFullMode`, `OnUseFullSnapshot`, `OnRedoStep1`, `OnRedoStep2`, `OnReturnStep1`, `OnCheckCurrentFiles`, `OnClearTemporary`, `OnResetCurrentRegion`

### Views/Pages/SettingsPage.xaml.cs

`OnUnloaded`, `OnSettingChanged`, `OnAnnouncementBadgeChanged`, `OnNotificationSettingChanged`, `OnThemeChanged`, `OnChooseExe`, `OnAutoDetect`, `OnOpenData`, `OnClearCache`, `OnProxyInputChanged`, `OnSaveProxy`, `OnTestNetwork`, `OnCopyDiagnostic`, `OnCheckUpdate`, `OnOpenUpdateLink`, `OnOnlineUpdate`, `OnRestoreSkippedUpdate`, `OnUpdateCheckingChanged`

### Views/Pages/SnapshotsPage.xaml.cs

`OnRefresh`, `OnCloseDetails`, `OnOpenRegion`, `OnSnapshotAction`

### Views/Pages/StatsPage.xaml.cs

`OnPageOpened`, `OnViewModelPropertyChanged`

### Views/PlatformErrorWindow.xaml.cs

`OnViewLogs`

### Views/RegionActionConfirmWindow.xaml.cs

`OnDrag`, `OnConfirm`, `OnCancel`

### Views/RegionSwitchChoiceWindow.xaml.cs

`OnCancel`, `OnAccountOnly`, `OnSettings`

### Views/SnapshotConfirmWindow.xaml.cs

`OnDrag`, `OnConfirm`, `OnCancel`

### Views/SoopNoticeWindow.xaml.cs

`OnConfirm`, `OnCancel`

### Views/SwitchPreviewWindow.xaml.cs

`OnDrag`, `OnStart`, `OnCancel`

### Views/UpdateDialog.xaml.cs

`OnDrag`, `OnOpenRelease`, `OnOnlineUpdate`, `OnCancelDownload`, `OnLater`

### Views/UploadStalledWindow.xaml.cs

`OnWait`, `OnCancel`

