# UI 功能迁移映射

基线：`813e6deda6ded9be8d76392594e38e859875738d`。每行对应实际控件、绑定、事件或确认入口；静态保留不等于运行验证。

| ID | 原有页面/功能 | 原控件与事件/绑定 | 重构后页面与入口 | 保留 | 验证 |
|---|---|---|---|---|---|
| UI-0001 | AnnouncementBell /  | Button  {"Click": "OnOpenAnnouncements"} | AnnouncementBell | 是 | 静态契约匹配 |
| UI-0002 | AnnouncementBell /  | DataTrigger  {"Binding": "{Binding AnnouncementState.IsBadgeVisible,                                 RelativeSource={RelativeSource AncestorType={x:Type app:MainWindow}}}"} | AnnouncementBell | 是 | 静态契约匹配 |
| UI-0003 | PageHeader / {Binding Title, RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}} | TextBlock  {"Text": "{Binding Title, RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}}"} | PageHeader | 是 | 静态契约匹配 |
| UI-0004 | PageHeader / {Binding Description, RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}} | TextBlock  {"Text": "{Binding Description, RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}}"} | PageHeader | 是 | 静态契约匹配 |
| UI-0005 | PageHeader / {Binding HeaderActions,                               RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}} | ContentPresenter  {"Content": "{Binding HeaderActions,                               RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}}"} | PageHeader | 是 | 静态契约匹配 |
| UI-0006 | CareerWindow /  | ImageBrush  {"ImageSource": "{Binding IconLocal, Converter={StaticResource PathToImg}}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0007 | CareerWindow / {Binding Name} | TextBlock HeroName {"Text": "{Binding Name}", "ToolTip": "{Binding Name}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0008 | CareerWindow / {Binding HoursText} | TextBlock  {"Text": "{Binding HoursText}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0009 | CareerWindow / {Binding WinRateText} | TextBlock  {"Text": "{Binding WinRateText}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0010 | CareerWindow / {Binding MatchsText} | TextBlock  {"Text": "{Binding MatchsText}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0011 | CareerWindow / {Binding PlayPercentText} | TextBlock  {"Text": "{Binding PlayPercentText}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0012 | CareerWindow /  | TextBox SearchBox {"KeyDown": "OnSearchKeyDown"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0013 | CareerWindow /  | Condition  {"Binding": "{Binding Text, ElementName=SearchBox}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0014 | CareerWindow /  | Condition  {"Binding": "{Binding IsKeyboardFocused, ElementName=SearchBox}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0015 | CareerWindow / 查询 | Button QueryBtn {"Click": "OnQuery"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0016 | CareerWindow /  | Button RefreshBtn {"Click": "OnRefresh"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0017 | CareerWindow / 键鼠 | RadioButton RbPc {"Checked": "OnInputChanged"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0018 | CareerWindow / 手柄 | RadioButton RbPad {"Checked": "OnInputChanged"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0019 | CareerWindow / 快速 | RadioButton RbQuick {"Checked": "OnModeChanged"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0020 | CareerWindow / 竞技 | RadioButton RbComp {"Checked": "OnModeChanged"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0021 | CareerWindow / {Binding Initial} | TextBlock  {"Text": "{Binding Initial}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0022 | CareerWindow /  | ImageBrush  {"ImageSource": "{Binding AvatarLocal, Converter={StaticResource PathToImg}}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0023 | CareerWindow / {Binding DisplayName, Mode=OneWay} | Run  {"Text": "{Binding DisplayName, Mode=OneWay}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0024 | CareerWindow / {Binding TagSuffix, Mode=OneWay} | Run  {"Text": "{Binding TagSuffix, Mode=OneWay}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0025 | CareerWindow / {Binding EndorseLevel, Mode=OneWay} | Run  {"Text": "{Binding EndorseLevel, Mode=OneWay}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0026 | CareerWindow / {Binding TotalHours} | TextBlock  {"Text": "{Binding TotalHours}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0027 | CareerWindow / {Binding UpdatedAt, Mode=OneWay} | Run  {"Text": "{Binding UpdatedAt, Mode=OneWay}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0028 | CareerWindow /  | StackPanel  {"Visibility": "{Binding ShowRanks, Converter={StaticResource BoolToVis}}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0029 | CareerWindow / {Binding RankSectionTitle} | TextBlock  {"Text": "{Binding RankSectionTitle}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0030 | CareerWindow /  | Image  {"Source": "{Binding Tank.TierIconLocal, Converter={StaticResource PathToImg}}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0031 | CareerWindow / {Binding Tank.TierText} | TextBlock  {"Text": "{Binding Tank.TierText}", "Foreground": "{Binding Tank.TierBrush}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0032 | CareerWindow /  | Image  {"Source": "{Binding Dps.TierIconLocal, Converter={StaticResource PathToImg}}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0033 | CareerWindow / {Binding Dps.TierText} | TextBlock  {"Text": "{Binding Dps.TierText}", "Foreground": "{Binding Dps.TierBrush}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0034 | CareerWindow /  | Image  {"Source": "{Binding Support.TierIconLocal, Converter={StaticResource PathToImg}}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0035 | CareerWindow / {Binding Support.TierText} | TextBlock  {"Text": "{Binding Support.TierText}", "Foreground": "{Binding Support.TierBrush}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0036 | CareerWindow / {Binding AvgSectionTitle} | TextBlock  {"Text": "{Binding AvgSectionTitle}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0037 | CareerWindow / {Binding AvgDamageLabel} | TextBlock  {"Text": "{Binding AvgDamageLabel}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0038 | CareerWindow / {Binding AvgDamage} | TextBlock  {"Text": "{Binding AvgDamage}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0039 | CareerWindow / {Binding AvgHealLabel} | TextBlock  {"Text": "{Binding AvgHealLabel}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0040 | CareerWindow / {Binding AvgHeal} | TextBlock  {"Text": "{Binding AvgHeal}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0041 | CareerWindow / {Binding KdaLabel} | TextBlock  {"Text": "{Binding KdaLabel}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0042 | CareerWindow / {Binding Kda} | TextBlock  {"Text": "{Binding Kda}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0043 | CareerWindow / {Binding WinRateLabel} | TextBlock  {"Text": "{Binding WinRateLabel}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0044 | CareerWindow / {Binding SeasonWinRate} | TextBlock  {"Text": "{Binding SeasonWinRate}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0045 | CareerWindow / {Binding WinLossText} | TextBlock  {"Text": "{Binding WinLossText}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0046 | CareerWindow /  | Border  {"Visibility": "{Binding HasAvgExtra, Converter={StaticResource BoolToVis}}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0047 | CareerWindow / {Binding AvgExtra} | TextBlock  {"Text": "{Binding AvgExtra}", "ToolTip": "{Binding AvgExtra}"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0048 | CareerWindow /  | Expander  {} | CareerWindow | 是 | 静态契约匹配 |
| UI-0049 | CareerWindow /  | Button  {"Click": "OnHeroSort"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0050 | CareerWindow /  | Button  {"Click": "OnHeroSort"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0051 | CareerWindow /  | Button  {"Click": "OnHeroSort"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0052 | CareerWindow /  | Button  {"Click": "OnHeroSort"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0053 | CareerWindow /  | Button  {"Click": "OnHeroSort"} | CareerWindow | 是 | 静态契约匹配 |
| UI-0054 | CareerWindow /  | ItemsControl HeroList {} | CareerWindow | 是 | 静态契约匹配 |
| UI-0055 | CareerWindow / Overlay / 重试 | Button OverlayPrimary {"Click": "OnOverlayPrimary"} | CareerWindow / Overlay | 是 | 静态契约匹配 |
| UI-0056 | CareerWindow / Overlay / 换个账号 | Button OverlaySecondary {"Click": "OnOverlaySecondary"} | CareerWindow / Overlay | 是 | 静态契约匹配 |
| UI-0057 | HeroDetailWindow / 英雄详情 / {Binding Name} | TextBlock  {"Text": "{Binding Name}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0058 | HeroDetailWindow / 英雄详情 / {Binding Value} | TextBlock  {"Text": "{Binding Value}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0059 | HeroDetailWindow / 英雄详情 /  | ImageBrush  {"ImageSource": "{Binding IconLocal, Converter={StaticResource PathToImg}}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0060 | HeroDetailWindow / 英雄详情 / {Binding Name} | TextBlock  {"Text": "{Binding Name}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0061 | HeroDetailWindow / 英雄详情 / {Binding LevelText} | TextBlock  {"Text": "{Binding LevelText}", "Visibility": "{Binding HasLevel, Converter={StaticResource BoolToVis}}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0062 | HeroDetailWindow / 英雄详情 /  | Border  {"Visibility": "{Binding HasRank, Converter={StaticResource BoolToVis}}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0063 | HeroDetailWindow / 英雄详情 / {Binding RankText, Mode=OneWay} | Run  {"Text": "{Binding RankText, Mode=OneWay}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0064 | HeroDetailWindow / 英雄详情 / {Binding WinRateText, Mode=OneWay} | Run  {"Text": "{Binding WinRateText, Mode=OneWay}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0065 | HeroDetailWindow / 英雄详情 / {Binding MatchsText, Mode=OneWay} | Run  {"Text": "{Binding MatchsText, Mode=OneWay}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0066 | HeroDetailWindow / 英雄详情 / {Binding HoursText, Mode=OneWay} | Run  {"Text": "{Binding HoursText, Mode=OneWay}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0067 | HeroDetailWindow / 英雄详情 /  | StackPanel  {"Visibility": "{Binding HasPerTen, Converter={StaticResource BoolToVis}}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0068 | HeroDetailWindow / 英雄详情 / {Binding TotalTabText, Mode=OneWay} | RadioButton RbAve {"Content": "{Binding TotalTabText, Mode=OneWay}", "Checked": "OnStatModeChanged"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0069 | HeroDetailWindow / 英雄详情 / 每10分钟 | RadioButton RbTen {"Checked": "OnStatModeChanged"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0070 | HeroDetailWindow / 英雄详情 /  | ItemsControl StatList {"ItemsSource": "{Binding DetailStats}"} | HeroDetailWindow / 英雄详情 | 是 | 静态契约匹配 |
| UI-0071 | AccountSettingsWindow / 账号设置 /  | TextBox CustomNameBox {} | AccountSettingsWindow / 账号设置 | 是 | 静态契约匹配 |
| UI-0072 | AccountSettingsWindow / 账号设置 /  | TextBox RemarkBox {} | AccountSettingsWindow / 账号设置 | 是 | 静态契约匹配 |
| UI-0073 | AccountSettingsWindow / 账号设置 /  | ComboBox RegionBox {} | AccountSettingsWindow / 账号设置 | 是 | 静态契约匹配 |
| UI-0074 | AccountSettingsWindow / 账号设置 / 取消 | Button  {} | AccountSettingsWindow / 账号设置 | 是 | 静态契约匹配 |
| UI-0075 | AccountSettingsWindow / 账号设置 / 保存 | Button  {"Click": "OnSave"} | AccountSettingsWindow / 账号设置 | 是 | 静态契约匹配 |
| UI-0076 | AnnouncementWindow / 公告 /  | ListBox AnnouncementList {"SelectionChanged": "OnAnnouncementSelected"} | AnnouncementWindow / 公告 | 是 | 静态契约匹配 |
| UI-0077 | AnnouncementWindow / 公告 /  | Ellipse  {"Visibility": "{Binding UnreadVisibility}"} | AnnouncementWindow / 公告 | 是 | 静态契约匹配 |
| UI-0078 | AnnouncementWindow / 公告 / {Binding Announcement.Title} | TextBlock  {"Text": "{Binding Announcement.Title}"} | AnnouncementWindow / 公告 | 是 | 静态契约匹配 |
| UI-0079 | AnnouncementWindow / 公告 / {Binding PublishedText} | TextBlock  {"Text": "{Binding PublishedText}"} | AnnouncementWindow / 公告 | 是 | 静态契约匹配 |
| UI-0080 | AnnouncementWindow / 公告 /  | TextBox DetailContent {} | AnnouncementWindow / 公告 | 是 | 静态契约匹配 |
| UI-0081 | AnnouncementWindow / 公告 / 关闭 | Button  {"Click": "OnClose"} | AnnouncementWindow / 公告 | 是 | 静态契约匹配 |
| UI-0082 | CloseChoiceWindow / 关闭窗口时 /  | Button  {"Click": "OnCancel"} | CloseChoiceWindow / 关闭窗口时 | 是 | 静态契约匹配 |
| UI-0083 | CloseChoiceWindow / 关闭窗口时 /  | RadioButton OptTray {} | CloseChoiceWindow / 关闭窗口时 | 是 | 静态契约匹配 |
| UI-0084 | CloseChoiceWindow / 关闭窗口时 /  | RadioButton OptExit {} | CloseChoiceWindow / 关闭窗口时 | 是 | 静态契约匹配 |
| UI-0085 | CloseChoiceWindow / 关闭窗口时 /  | CheckBox Remember {} | CloseChoiceWindow / 关闭窗口时 | 是 | 静态契约匹配 |
| UI-0086 | CloseChoiceWindow / 关闭窗口时 / 确定 | Button  {"Click": "OnConfirm"} | CloseChoiceWindow / 关闭窗口时 | 是 | 静态契约匹配 |
| UI-0087 | DeleteConfirmWindow / 删除账号备份 /  | Button  {"Click": "OnCancel"} | DeleteConfirmWindow / 删除账号备份 | 是 | 静态契约匹配 |
| UI-0088 | DeleteConfirmWindow / 删除账号备份 / 取消 | Button  {"Click": "OnCancel"} | DeleteConfirmWindow / 删除账号备份 | 是 | 静态契约匹配 |
| UI-0089 | DeleteConfirmWindow / 删除账号备份 /  | Button  {"Click": "OnConfirm"} | DeleteConfirmWindow / 删除账号备份 | 是 | 静态契约匹配 |
| UI-0090 | ExitConfirmationDialog / 退出 CloudLight Blizzard / × | Button  {"Click": "OnCancel"} | ExitConfirmationDialog / 退出 CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0091 | ExitConfirmationDialog / 退出 CloudLight Blizzard / 取消 | Button  {} | ExitConfirmationDialog / 退出 CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0092 | ExitConfirmationDialog / 退出 CloudLight Blizzard / 最小化到托盘 | Button  {"Click": "OnMinimizeToTray"} | ExitConfirmationDialog / 退出 CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0093 | ExitConfirmationDialog / 退出 CloudLight Blizzard / 退出 | Button  {"Click": "OnExit"} | ExitConfirmationDialog / 退出 CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0094 | LoginNewWindow / 登录新号 /  | Button  {"Click": "OnCancel"} | LoginNewWindow / 登录新号 | 是 | 静态契约匹配 |
| UI-0095 | LoginNewWindow / 登录新号 / 取消 | Button  {"Click": "OnCancel"} | LoginNewWindow / 登录新号 | 是 | 静态契约匹配 |
| UI-0096 | LoginNewWindow / 登录新号 / 继续 | Button  {"Click": "OnConfirm"} | LoginNewWindow / 登录新号 | 是 | 静态契约匹配 |
| UI-0097 | MainWindow / CloudLight Blizzard / 账号 | RadioButton AccountsNav {"Checked": "OnNavigate"} | MainWindow / CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0098 | MainWindow / CloudLight Blizzard / 区服切换 | RadioButton RegionNav {"Checked": "OnNavigate"} | MainWindow / CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0099 | MainWindow / CloudLight Blizzard / 战绩 | RadioButton StatsNav {"Checked": "OnNavigate"} | MainWindow / CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0100 | MainWindow / CloudLight Blizzard / 掉宝 | RadioButton DropsNav {"Checked": "OnNavigate"} | MainWindow / CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0101 | MainWindow / CloudLight Blizzard / 区服快照 | RadioButton SnapshotsNav {"Checked": "OnNavigate"} | MainWindow / CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0102 | MainWindow / CloudLight Blizzard / 诊断中心 | RadioButton DiagnosticsNav {"Checked": "OnNavigate"} | MainWindow / CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0103 | MainWindow / CloudLight Blizzard / 设置 | RadioButton SettingsNav {"Checked": "OnNavigate"} | MainWindow / CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0104 | MainWindow / CloudLight Blizzard / 关于 | RadioButton AboutNav {"Checked": "OnNavigate"} | MainWindow / CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0105 | MainWindow / CloudLight Blizzard / {Binding AppVersion} | TextBlock  {"Text": "{Binding AppVersion}"} | MainWindow / CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0106 | MainWindow / CloudLight Blizzard / 刷新账号 | Button  {"Click": "OnRefresh"} | MainWindow / CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0107 | MainWindow / CloudLight Blizzard / {Binding StatusText} | TextBlock  {"Text": "{Binding StatusText}"} | MainWindow / CloudLight Blizzard | 是 | 静态契约匹配 |
| UI-0108 | Views/MainWindow.xaml.cs:191 / dialog.ShowDialog(); | confirmation/command  {} | Views/MainWindow.xaml.cs:203 | 是 | 静态契约匹配 |
| UI-0109 | Views/MainWindow.xaml.cs:224 / dialog.ShowDialog(); | confirmation/command  {} | Views/MainWindow.xaml.cs:236 | 是 | 静态契约匹配 |
| UI-0110 | Views/MainWindow.xaml.cs:242 / MessageBox.Show($"当前版本：{_vm.UpdateChecks.CurrentVersion}", "已是最新版本", | confirmation/command  {} | Views/MainWindow.xaml.cs:254 | 是 | 静态契约匹配 |
| UI-0111 | Views/MainWindow.xaml.cs:245 / MessageBox.Show("当前没有可用的正式更新。", "软件更新", | confirmation/command  {} | Views/MainWindow.xaml.cs:257 | 是 | 静态契约匹配 |
| UI-0112 | Views/MainWindow.xaml.cs:248 / MessageBox.Show(outcome.Result?.ErrorMessage ?? "暂时无法连接更新服务器。", "暂时无法检查更新", | confirmation/command  {} | Views/MainWindow.xaml.cs:260 | 是 | 静态契约匹配 |
| UI-0113 | Views/MainWindow.xaml.cs:261 / MessageBox.Show("无法打开系统浏览器，请稍后重试。", "打开更新链接", | confirmation/command  {} | Views/MainWindow.xaml.cs:273 | 是 | 静态契约匹配 |
| UI-0114 | Views/MainWindow.xaml.cs:434 / MessageBox.Show("安装包不存在或设置无法保存，已取消安装。", "在线更新"); | confirmation/command  {} | Views/MainWindow.xaml.cs:446 | 是 | 静态契约匹配 |
| UI-0115 | Views/MainWindow.xaml.cs:482 / else MessageBox.Show(message, "在线更新", MessageBoxButton.OK, MessageBoxImage.Warning); | confirmation/command  {} | Views/MainWindow.xaml.cs:494 | 是 | 静态契约匹配 |
| UI-0116 | Views/MainWindow.xaml.cs:681 / dialog.ShowDialog(); | confirmation/command  {} | Views/MainWindow.xaml.cs:714 | 是 | 静态契约匹配 |
| UI-0117 | AboutPage / {Binding AppVersion} | TextBlock  {"Text": "{Binding AppVersion}"} | AboutPage | 是 | 静态契约匹配 |
| UI-0118 | AboutPage /  | ItemsControl ThirdPartyComponentsList {"ItemsSource": "{Binding Components, ElementName=AboutRoot}"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0119 | AboutPage / {Binding License} | TextBlock  {"Text": "{Binding License}"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0120 | AboutPage / {Binding Name} | TextBlock  {"Text": "{Binding Name}"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0121 | AboutPage / {Binding Purpose} | TextBlock  {"Text": "{Binding Purpose}"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0122 | AboutPage / {Binding SourceUrl} | TextBlock  {"Text": "{Binding SourceUrl}", "ToolTip": "{Binding SourceUrl}"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0123 | AboutPage / {Binding VersionInfo} | TextBlock  {"Text": "{Binding VersionInfo}"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0124 | AboutPage / {Binding DistributionInfo} | TextBlock  {"Text": "{Binding DistributionInfo}"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0125 | AboutPage / {Binding LicenseLocation} | TextBlock  {"Text": "{Binding LicenseLocation}"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0126 | AboutPage / {Binding NoticeLocation} | TextBlock  {"Text": "{Binding NoticeLocation}"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0127 | AboutPage / 打开上游项目 | Button  {"Tag": "{Binding SourceOpenUrl}", "Visibility": "{Binding HasSourceOpenUrl, Converter={StaticResource BoolVisibility}}", "Click": "OnOpenSource"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0128 | AboutPage / 打开许可证 | Button  {"Tag": "{Binding LicenseOpenPath}", "Visibility": "{Binding HasLicenseOpenPath, Converter={StaticResource BoolVisibility}}", "Click": "OnOpenLocalPath"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0129 | AboutPage / 打开第三方说明 | Button  {"Tag": "{Binding NoticeOpenPath}", "Visibility": "{Binding HasNoticeOpenPath, Converter={StaticResource BoolVisibility}}", "Click": "OnOpenLocalPath"} | AboutPage / 开源组件与许可证 | 是 | 静态契约匹配 |
| UI-0130 | Views/Pages/AboutPage.xaml.cs:138 / MessageBox.Show($"当前发布目录中未找到：{relativePath}", "打开第三方文件", | confirmation/command  {} | Views/Pages/AboutPage.xaml.cs:138 | 是 | 静态契约匹配 |
| UI-0131 | AccountsPage /  | StackPanel  {"Visibility": "{Binding HasCurrentVisibility}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0132 | AccountsPage / {Binding CurrentAccount.DisplayName} | TextBlock  {"Text": "{Binding CurrentAccount.DisplayName}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0133 | AccountsPage / {Binding CurrentAccount.BattleTag} | TextBlock  {"Text": "{Binding CurrentAccount.BattleTag}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0134 | AccountsPage / {Binding CurrentAccount.RegionText} | TextBlock  {"Text": "{Binding CurrentAccount.RegionText}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0135 | AccountsPage / 当前未检测到登录账号。 | TextBlock  {"Visibility": "{Binding NoCurrentVisibility}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0136 | AccountsPage / 打开 Battle.net | Button  {"Click": "OnLaunchClient"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0137 | AccountsPage / 更新账号备份 | Button  {"Click": "OnSaveCurrent"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0138 | AccountsPage / 登录新账号 | Button  {"Click": "OnAddAccount"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0139 | AccountsPage / {Binding AccountSnapshotPath} | TextBlock AccountSnapshotPathText {"Text": "{Binding AccountSnapshotPath}"} | AccountsPage / storage | 是 | 静态契约匹配 |
| UI-0140 | AccountsPage / {Binding AccountMigrationWarning} | TextBlock  {"Text": "{Binding AccountMigrationWarning}"} | AccountsPage / storage | 是 | 静态契约匹配 |
| UI-0141 | AccountsPage / 打开账号快照目录 | Button  {"Click": "OnOpenSnapshots"} | AccountsPage / storage | 是 | 静态契约匹配 |
| UI-0142 | AccountsPage / {Binding ReadyCountText} | TextBlock  {"Text": "{Binding ReadyCountText}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0143 | AccountsPage /  | ItemsControl  {"ItemsSource": "{Binding SavedAccounts}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0144 | AccountsPage / {Binding RegionText} | TextBlock  {"Text": "{Binding RegionText}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0145 | AccountsPage / {Binding DisplayName} | TextBlock  {"Text": "{Binding DisplayName}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0146 | AccountsPage / {Binding BattleTag} | TextBlock  {"Text": "{Binding BattleTag}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0147 | AccountsPage /  | Border  {"Visibility": "{Binding CurrentVisibility}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0148 | AccountsPage / {Binding Remark} | TextBlock  {"Text": "{Binding Remark}", "Visibility": "{Binding RemarkVisibility}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0149 | AccountsPage / {Binding ProfileText} | TextBlock  {"Text": "{Binding ProfileText}", "ToolTip": "{Binding ProfileDetail}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0150 | AccountsPage / 需要重新登录 Battle.net | TextBlock  {"Visibility": "{Binding ExpiredVisibility}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0151 | AccountsPage / {Binding SwitchText} | Button  {"Content": "{Binding SwitchText}", "IsEnabled": "{Binding CanSwitch}", "Click": "OnSwitchCard"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0152 | AccountsPage / 战绩 | Button  {"Visibility": "{Binding StatsVisibility}", "Click": "OnStats"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0153 | AccountsPage / 编辑 | Button  {"Click": "OnEdit"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0154 | AccountsPage / 删除 | Button  {"Click": "OnDelete"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0155 | AccountsPage /  | StackPanel  {"Visibility": "{Binding UnsavedVisibility}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0156 | AccountsPage / {Binding UnsavedCountText} | TextBlock  {"Text": "{Binding UnsavedCountText}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0157 | AccountsPage /  | ItemsControl  {"ItemsSource": "{Binding UnsavedAccounts}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0158 | AccountsPage / 隐藏 | Button  {"Click": "OnHide"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0159 | AccountsPage / 重新登录并保存 | Button  {"Click": "OnSwitchCard"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0160 | AccountsPage / {Binding BattleTag} | TextBlock  {"Text": "{Binding BattleTag}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0161 | AccountsPage / {Binding ProfileText} | TextBlock  {"Text": "{Binding ProfileText}", "ToolTip": "{Binding ProfileDetail}"} | AccountsPage / library | 是 | 静态契约匹配 |
| UI-0162 | Views/Pages/AccountsPage.xaml.cs:25 / catch (Exception ex) { MessageBox.Show("无法打开账号快照目录：" + ex.Message + "\n" + _vm.AccountSnapshotPath, "账号快照"); } | confirmation/command  {} | Views/Pages/AccountsPage.xaml.cs:34 | 是 | 静态契约匹配 |
| UI-0163 | Views/Pages/AccountsPage.xaml.cs:33 / MessageBox.Show("请先在 Battle.net 登录该账号，刷新列表后保存账号快照。", "尚未识别当前账号"); | confirmation/command  {} | Views/Pages/AccountsPage.xaml.cs:42 | 是 | 静态契约匹配 |
| UI-0164 | Views/Pages/AccountsPage.xaml.cs:37 / if (confirmation.ShowDialog() == true) | confirmation/command  {} | Views/Pages/AccountsPage.xaml.cs:46 | 是 | 静态契约匹配 |
| UI-0165 | Views/Pages/AccountsPage.xaml.cs:42 / if (_vm != null && new LoginNewWindow { Owner = Window.GetWindow(this) }.ShowDialog() == true) | confirmation/command  {} | Views/Pages/AccountsPage.xaml.cs:51 | 是 | 静态契约匹配 |
| UI-0166 | Views/Pages/AccountsPage.xaml.cs:50 / var ok = MessageBox.Show($"「{row.BattleTag}」需要重新登录 Battle.net。\n{row.ProfileDetail}\n\n现在关闭 Battle.net 并请求登录页，请手动选择该账号。登录完成后点击“更新账号备份”保存；此操作不会自动保存。", | confirmation/command  {} | Views/Pages/AccountsPage.xaml.cs:59 | 是 | 静态契约匹配 |
| UI-0167 | Views/Pages/AccountsPage.xaml.cs:60 / new AccountSettingsWindow(_vm, row) { Owner = Window.GetWindow(this) }.ShowDialog(); | confirmation/command  {} | Views/Pages/AccountsPage.xaml.cs:69 | 是 | 静态契约匹配 |
| UI-0168 | Views/Pages/AccountsPage.xaml.cs:65 / new DeleteConfirmWindow(row.BattleTag) { Owner = Window.GetWindow(this) }.ShowDialog() == true) | confirmation/command  {} | Views/Pages/AccountsPage.xaml.cs:74 | 是 | 静态契约匹配 |
| UI-0169 | Views/Pages/AccountsPage.xaml.cs:71 / new DeleteConfirmWindow(row.BattleTag, true) { Owner = Window.GetWindow(this) }.ShowDialog() == true) _vm.HideAccount(row); | confirmation/command  {} | Views/Pages/AccountsPage.xaml.cs:80 | 是 | 静态契约匹配 |
| UI-0170 | DiagnosticsPage / {Binding StatusIcon} | TextBlock  {"Text": "{Binding StatusIcon}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0171 | DiagnosticsPage /  | DataTrigger  {"Binding": "{Binding Status}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0172 | DiagnosticsPage /  | DataTrigger  {"Binding": "{Binding Status}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0173 | DiagnosticsPage /  | DataTrigger  {"Binding": "{Binding Status}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0174 | DiagnosticsPage / {Binding Category} | TextBlock  {"Text": "{Binding Category}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0175 | DiagnosticsPage / {Binding Name} | TextBlock  {"Text": "{Binding Name}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0176 | DiagnosticsPage / {Binding Summary} | TextBlock  {"Text": "{Binding Summary}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0177 | DiagnosticsPage / {Binding Details} | TextBlock  {"Text": "{Binding Details}"} | DiagnosticsPage / 技术详情 | 是 | 静态契约匹配 |
| UI-0178 | DiagnosticsPage / {Binding StatusText} | TextBlock  {"Text": "{Binding StatusText}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0179 | DiagnosticsPage / 开始诊断 | Button  {"Click": "OnStart"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0180 | DiagnosticsPage / 复制诊断结果 | Button  {"IsEnabled": "{Binding CanExport}", "Click": "OnCopy"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0181 | DiagnosticsPage / 导出诊断包 | Button  {"IsEnabled": "{Binding CanExport}", "Click": "OnExport"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0182 | DiagnosticsPage / {Binding StatusText} | TextBlock  {"Text": "{Binding StatusText}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0183 | DiagnosticsPage / {Binding ProgressText} | TextBlock  {"Text": "{Binding ProgressText}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0184 | DiagnosticsPage /  | ProgressBar  {"Value": "{Binding ProgressPercent, Mode=OneWay}", "IsIndeterminate": "{Binding IsRunning, Mode=OneWay}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0185 | DiagnosticsPage / 取消诊断 | Button  {"Visibility": "{Binding IsRunning, Converter={StaticResource BoolVisibility}}", "Click": "OnCancel"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0186 | DiagnosticsPage / {Binding SummaryText} | TextBlock  {"Text": "{Binding SummaryText}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0187 | DiagnosticsPage /  | ItemsControl  {"ItemsSource": "{Binding Checks}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0188 | DiagnosticsPage /  | DataTrigger  {"Binding": "{Binding Checks.Count}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0189 | DiagnosticsPage /  | DataTrigger  {"Binding": "{Binding Checks.Count}"} | DiagnosticsPage | 是 | 静态契约匹配 |
| UI-0190 | Views/Pages/DiagnosticsPage.xaml.cs:34 / catch { MessageBox.Show("无法写入剪贴板，请稍后重试。", "诊断中心", MessageBoxButton.OK, MessageBoxImage.Information); } | confirmation/command  {} | Views/Pages/DiagnosticsPage.xaml.cs:53 | 是 | 静态契约匹配 |
| UI-0191 | Views/Pages/DiagnosticsPage.xaml.cs:42 / var result = MessageBox.Show($"诊断包已生成：\n{path}\n\n是否打开所在目录？", "诊断中心", | confirmation/command  {} | Views/Pages/DiagnosticsPage.xaml.cs:61 | 是 | 静态契约匹配 |
| UI-0192 | DropsPage /  | ToggleButton  {"IsChecked": "{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0193 | DropsPage / {Binding SelectionBoxItem, RelativeSource={RelativeSource AncestorType=ComboBox}} | ContentPresenter  {"Content": "{Binding SelectionBoxItem, RelativeSource={RelativeSource AncestorType=ComboBox}}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0194 | DropsPage / {Binding Text, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged} | TextBox PART_EditableTextBox {"Text": "{Binding Text, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0195 | DropsPage /  | Border  {"MinWidth": "{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"} | StatsStyles | 是 | 静态契约匹配 |
| UI-0196 | DropsPage / {Binding Primary} | TextBlock  {"Text": "{Binding Primary}", "ToolTip": "{Binding Primary}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0197 | DropsPage / {Binding Secondary} | TextBlock  {"Text": "{Binding Secondary}", "ToolTip": "{Binding Secondary}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0198 | DropsPage / {Binding Status} | TextBlock  {"Text": "{Binding Status}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0199 | DropsPage / {Binding Primary} | TextBlock  {"Text": "{Binding Primary}", "ToolTip": "{Binding Primary}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0200 | DropsPage / {Binding Secondary} | TextBlock  {"Text": "{Binding Secondary}", "ToolTip": "{Binding Secondary}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0201 | DropsPage / {Binding Status} | TextBlock  {"Text": "{Binding Status}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0202 | DropsPage / {Binding TwitchClaimButtonText} | Button  {"Content": "{Binding TwitchClaimButtonText}", "Visibility": "{Binding TwitchClaimVisibility}", "IsEnabled": "{Binding CanStartTwitchClaim}", "Click": "OnClaimTwitchDrop"} | DropsPage | 是 | 静态契约匹配 |
| UI-0203 | DropsPage / {Binding Secondary} | TextBlock  {"Text": "{Binding Secondary}", "ToolTip": "{Binding Secondary}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0204 | DropsPage / {Binding Primary} | TextBlock  {"Text": "{Binding Primary}", "ToolTip": "{Binding Primary}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0205 | DropsPage / {Binding Status} | TextBlock  {"Text": "{Binding Status}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0206 | DropsPage /  | ProgressBar  {"Value": "{Binding Progress, Mode=OneWay}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0207 | DropsPage / {Binding Detail} | TextBlock  {"Text": "{Binding Detail}", "ToolTip": "{Binding Detail}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0208 | DropsPage /  | DataTrigger  {"Binding": "{Binding Enabled}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0209 | DropsPage / {Binding Primary} | TextBlock  {"Text": "{Binding Primary}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0210 | DropsPage / {Binding Secondary} | TextBlock  {"Text": "{Binding Secondary}", "ToolTip": "{Binding Secondary}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0211 | DropsPage / {Binding Number} | TextBlock  {"Text": "{Binding Number}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0212 | DropsPage / {Binding Title} | TextBlock  {"Text": "{Binding Title}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0213 | DropsPage / {Binding StateText} | TextBlock  {"Text": "{Binding StateText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0214 | DropsPage /  | DataTrigger  {"Binding": "{Binding StateKind}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0215 | DropsPage /  | DataTrigger  {"Binding": "{Binding StateKind}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0216 | DropsPage / {Binding Description} | TextBlock  {"Text": "{Binding Description}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0217 | DropsPage / {Binding ActionText} | Button  {"Content": "{Binding ActionText}", "Tag": "{Binding ActionKey}", "Visibility": "{Binding ActionVisibility}", "Click": "OnQuickStartAction"} | DropsPage | 是 | 静态契约匹配 |
| UI-0218 | DropsPage / {Binding Campaign} | TextBlock  {"Text": "{Binding Campaign}", "ToolTip": "{Binding Campaign}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0219 | DropsPage / {Binding ProgressText} | TextBlock  {"Text": "{Binding ProgressText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0220 | DropsPage / {Binding Reward, Mode=OneWay} | Run  {"Text": "{Binding Reward, Mode=OneWay}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0221 | DropsPage / {Binding Account, Mode=OneWay} | Run  {"Text": "{Binding Account, Mode=OneWay}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0222 | DropsPage / {Binding Channel, Mode=OneWay} | Run  {"Text": "{Binding Channel, Mode=OneWay}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0223 | DropsPage / {Binding GameAccountBindingText} | TextBlock  {"Text": "{Binding GameAccountBindingText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0224 | DropsPage /  | ProgressBar  {"Value": "{Binding Percent, Mode=OneWay}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0225 | DropsPage / {Binding WatchPathText} | TextBlock  {"Text": "{Binding WatchPathText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0226 | DropsPage / {Binding SyncState} | TextBlock  {"Text": "{Binding SyncState}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0227 | DropsPage / {Binding RemainingText} | TextBlock  {"Text": "{Binding RemainingText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0228 | DropsPage / {Binding ConfirmationText} | TextBlock  {"Text": "{Binding ConfirmationText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0229 | DropsPage / {Binding Diagnostic} | TextBlock  {"Text": "{Binding Diagnostic}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0230 | DropsPage / {Binding Name} | TextBlock  {"Text": "{Binding Name}", "ToolTip": "{Binding Name}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0231 | DropsPage / {Binding OfficialText} | TextBlock  {"Text": "{Binding OfficialText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0232 | DropsPage / {Binding Status} | TextBlock  {"Text": "{Binding Status}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0233 | DropsPage /  | ProgressBar  {"Value": "{Binding Percent}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0234 | DropsPage / {Binding ProgressText} | TextBlock  {"Text": "{Binding ProgressText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0235 | DropsPage / {Binding ClaimButtonText} | Button  {"Content": "{Binding ClaimButtonText}", "Visibility": "{Binding ClaimVisibility}", "IsEnabled": "{Binding CanClaim}", "Command": "{Binding DataContext.BilibiliDetails.ClaimRewardCommand, ElementName=DropsRoot}", "CommandParameter": "{Binding}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0236 | DropsPage /  | CheckBox  {"IsChecked": "{Binding Enabled, Mode=TwoWay}", "Command": "{Binding DataContext.BilibiliDetails.SetRoomEnabledCommand, ElementName=DropsRoot}", "CommandParameter": "{Binding}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0237 | DropsPage / {Binding Name} | TextBlock  {"Text": "{Binding Name}", "ToolTip": "{Binding Name}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0238 | DropsPage / {Binding RoomIdText, Mode=OneWay} | Run  {"Text": "{Binding RoomIdText, Mode=OneWay}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0239 | DropsPage / {Binding Status, Mode=OneWay} | Run  {"Text": "{Binding Status, Mode=OneWay}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0240 | DropsPage / {Binding SessionText, Mode=OneWay} | Run  {"Text": "{Binding SessionText, Mode=OneWay}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0241 | DropsPage / 删除 | Button  {"Command": "{Binding DataContext.BilibiliDetails.RemoveRoomCommand, ElementName=DropsRoot}", "CommandParameter": "{Binding}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0242 | DropsPage / {Binding Id} | TextBlock  {"Text": "{Binding Id}", "ToolTip": "{Binding Id}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0243 | DropsPage / {Binding StateText} | TextBlock  {"Text": "{Binding StateText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0244 | DropsPage / {Binding Detail} | TextBlock  {"Text": "{Binding Detail}", "ToolTip": "{Binding Detail}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0245 | DropsPage / {Binding Status} | TextBlock  {"Text": "{Binding Status}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0246 | DropsPage / {Binding Name} | TextBlock  {"Text": "{Binding Name}", "ToolTip": "{Binding Name}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0247 | DropsPage / {Binding RoomText, Mode=OneWay} | Run  {"Text": "{Binding RoomText, Mode=OneWay}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0248 | DropsPage / {Binding TaskText, Mode=OneWay} | Run  {"Text": "{Binding TaskText, Mode=OneWay}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0249 | DropsPage /  | DataTrigger  {"Binding": "{Binding ConnectionState}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0250 | DropsPage /  | DataTrigger  {"Binding": "{Binding ConnectionState}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0251 | DropsPage /  | DataTrigger  {"Binding": "{Binding ConnectionState}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0252 | DropsPage /  | DataTrigger  {"Binding": "{Binding ConnectionState}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0253 | DropsPage /  | DataTrigger  {"Binding": "{Binding ConnectionState}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0254 | DropsPage /  | DataTrigger  {"Binding": "{Binding ConnectionState}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0255 | DropsPage /  | DataTrigger  {"Binding": "{Binding ConnectionState}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0256 | DropsPage / 掉宝 / 刷新 | Button  {"Click": "OnRefresh"} | DropsPage / 掉宝管理 | 是 | 静态契约匹配 |
| UI-0257 | DropsPage /  | RadioButton SoopTab {"Checked": "OnPlatformChanged"} | DropsPage | 是 | 静态契约匹配 |
| UI-0258 | DropsPage /  | StackPanel  {"DataContext": "{Binding Soop}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0259 | DropsPage /  | Ellipse SoopStatusDot {"ToolTip": "{Binding ConnectionStateText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0260 | DropsPage /  | RadioButton YouTubeTab {"Checked": "OnPlatformChanged"} | DropsPage | 是 | 静态契约匹配 |
| UI-0261 | DropsPage /  | StackPanel  {"DataContext": "{Binding YouTube}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0262 | DropsPage /  | Ellipse YouTubeStatusDot {"ToolTip": "{Binding ConnectionStateText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0263 | DropsPage /  | RadioButton TwitchTab {"Checked": "OnPlatformChanged"} | DropsPage | 是 | 静态契约匹配 |
| UI-0264 | DropsPage /  | StackPanel  {"DataContext": "{Binding Twitch}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0265 | DropsPage /  | Ellipse TwitchStatusDot {"ToolTip": "{Binding ConnectionStateText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0266 | DropsPage /  | RadioButton BilibiliTab {"Checked": "OnPlatformChanged"} | DropsPage | 是 | 静态契约匹配 |
| UI-0267 | DropsPage /  | StackPanel  {"DataContext": "{Binding Bilibili}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0268 | DropsPage /  | Ellipse BilibiliStatusDot {"ToolTip": "{Binding ConnectionStateText}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0269 | DropsPage / {Binding ActivePlatform.Name} | TextBlock  {"Text": "{Binding ActivePlatform.Name}"} | DropsPage / network | 是 | 静态契约匹配 |
| UI-0270 | DropsPage / {Binding ActivePlatform.Status} | TextBlock  {"Text": "{Binding ActivePlatform.Status}"} | DropsPage / network | 是 | 静态契约匹配 |
| UI-0271 | DropsPage / {Binding ActiveAccountText, StringFormat=账号：{0}} | TextBlock  {"Text": "{Binding ActiveAccountText, StringFormat=账号：{0}}"} | DropsPage / network | 是 | 静态契约匹配 |
| UI-0272 | DropsPage / {Binding ActivePlatform.Summary} | TextBlock  {"Text": "{Binding ActivePlatform.Summary}"} | DropsPage / network | 是 | 静态契约匹配 |
| UI-0273 | DropsPage / {Binding ActiveStartText} | Button DropsActiveStartButton {"Content": "{Binding ActiveStartText}", "IsEnabled": "{Binding CanActiveStart}", "Click": "OnActiveStart"} | DropsPage / network | 是 | 静态契约匹配 |
| UI-0274 | DropsPage / 停止 | Button DropsActiveStopButton {"IsEnabled": "{Binding CanActiveStop}", "Click": "OnActiveStop"} | DropsPage / network | 是 | 静态契约匹配 |
| UI-0275 | DropsPage / 刷新当前平台 | Button  {"Click": "OnRefresh"} | DropsPage / network | 是 | 静态契约匹配 |
| UI-0276 | DropsPage / 官方背包 / 平台 | Button  {"Click": "OnActiveInventory"} | DropsPage / network | 是 | 静态契约匹配 |
| UI-0277 | DropsPage / 打开直播间 | Button  {"Click": "OnOpenCurrentWatch"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0278 | DropsPage /  | Border DropsNoticeBanner {"Visibility": "{Binding ActivePlatform.NoticeVisibility}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0279 | DropsPage / {Binding ActivePlatform.NoticeLevel} | TextBlock  {"Text": "{Binding ActivePlatform.NoticeLevel}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0280 | DropsPage /  | DataTrigger  {"Binding": "{Binding ActivePlatform.NoticeLevel}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0281 | DropsPage /  | DataTrigger  {"Binding": "{Binding ActivePlatform.NoticeLevel}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0282 | DropsPage /  | DataTrigger  {"Binding": "{Binding ActivePlatform.NoticeLevel}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0283 | DropsPage /  | DataTrigger  {"Binding": "{Binding ActivePlatform.NoticeLevel}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0284 | DropsPage / {Binding ActivePlatform.Notice} | TextBlock  {"Text": "{Binding ActivePlatform.Notice}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0285 | DropsPage / 重试连接 / 刷新 | Button  {"Click": "OnActiveRetry"} | DropsPage | 是 | 静态契约匹配 |
| UI-0286 | DropsPage / 查看日志 | Button  {"Click": "OnShowDropsLog"} | DropsPage | 是 | 静态契约匹配 |
| UI-0287 | DropsPage / 关闭提醒 | Button DropsDismissNoticeButton {"Click": "OnDismissNotice"} | DropsPage | 是 | 静态契约匹配 |
| UI-0288 | DropsPage / Soop /  | StackPanel SoopPanel {"Visibility": "{Binding SoopPanelVisibility}"} | DropsPage / Soop | 是 | 静态契约匹配 |
| UI-0289 | DropsPage / Soop / 立即重试 | Button  {"Visibility": "{Binding SoopRetryNowVisibility}", "Click": "OnSoopRetryNow"} | DropsPage / Soop / overview | 是 | 静态契约匹配 |
| UI-0290 | DropsPage / Soop / {Binding SoopRetryStatusText} | TextBlock  {"Text": "{Binding SoopRetryStatusText}", "Visibility": "{Binding SoopRetryStatusVisibility}"} | DropsPage / Soop / overview | 是 | 静态契约匹配 |
| UI-0291 | DropsPage / Soop / {Binding SoopLastSuccessText} | TextBlock  {"Text": "{Binding SoopLastSuccessText}", "Visibility": "{Binding SoopLastSuccessVisibility}"} | DropsPage / Soop / overview | 是 | 静态契约匹配 |
| UI-0292 | DropsPage / Soop / {Binding SoopWatchStatusText} | TextBlock  {"Text": "{Binding SoopWatchStatusText}"} | DropsPage / Soop / overview | 是 | 静态契约匹配 |
| UI-0293 | DropsPage / Soop /  | ListBox  {"ItemsSource": "{Binding SoopCurrentProgress}"} | DropsPage / Soop / overview | 是 | 静态契约匹配 |
| UI-0294 | DropsPage / Soop /  | DataTrigger  {"Binding": "{Binding SoopCurrentProgress.Count}"} | DropsPage / Soop / overview | 是 | 静态契约匹配 |
| UI-0295 | DropsPage / Soop /  | ListBox  {"ItemsSource": "{Binding Tasks}"} | DropsPage / Soop / activities | 是 | 静态契约匹配 |
| UI-0296 | DropsPage / Soop /  | DataTrigger  {"Binding": "{Binding Tasks.Count}"} | DropsPage / Soop / activities | 是 | 静态契约匹配 |
| UI-0297 | DropsPage / Soop / 打开背包 | Button  {"Click": "OnOpenSoopInventory"} | DropsPage / Soop / activities | 是 | 静态契约匹配 |
| UI-0298 | DropsPage / Soop /  | ListBox SoopInventoryList {"ItemsSource": "{Binding Inventory}"} | DropsPage / Soop / activities | 是 | 静态契约匹配 |
| UI-0299 | DropsPage / Soop /  | DataTrigger  {"Binding": "{Binding Inventory.Count}"} | DropsPage / Soop / activities | 是 | 静态契约匹配 |
| UI-0300 | DropsPage / Soop / {Binding SelectedItem.SoopClaimButtonText, ElementName=SoopInventoryList, TargetNullValue=领取选中奖励, FallbackValue=领取选中奖励} | Button  {"Content": "{Binding SelectedItem.SoopClaimButtonText, ElementName=SoopInventoryList, TargetNullValue=领取选中奖励, FallbackValue=领取选中奖励}", "Click": "OnClaimSoopReward"} | DropsPage / Soop / activities | 是 | 静态契约匹配 |
| UI-0301 | DropsPage / Soop / 复制选中兑换码 | Button  {"Click": "OnCopySoopCode"} | DropsPage / Soop / activities | 是 | 静态契约匹配 |
| UI-0302 | DropsPage / Soop / 进度历史与回退诊断 / 进度历史与回退诊断 | Expander  {} | DropsPage / Soop / activities / 进度历史与回退诊断 | 是 | 静态契约匹配 |
| UI-0303 | DropsPage / Soop / 进度历史与回退诊断 /  | ListBox  {"ItemsSource": "{Binding SoopProgressHistory}"} | DropsPage / Soop / activities / 进度历史与回退诊断 | 是 | 静态契约匹配 |
| UI-0304 | DropsPage / Soop /  | TextBox SoopUserId {} | DropsPage / Soop / accounts / 添加 SOOP 账号 | 是 | 静态契约匹配 |
| UI-0305 | DropsPage / Soop /  | PasswordBox SoopPassword {"PasswordChanged": "OnSoopPasswordChanged"} | DropsPage / Soop / accounts / 添加 SOOP 账号 | 是 | 静态契约匹配 |
| UI-0306 | DropsPage / Soop / 添加账号 | Button  {"Click": "OnSoopAddAccount"} | DropsPage / Soop / accounts / 添加 SOOP 账号 | 是 | 静态契约匹配 |
| UI-0307 | DropsPage / Soop /  | ListBox SoopAccountsList {"ItemsSource": "{Binding Accounts}"} | DropsPage / Soop / accounts | 是 | 静态契约匹配 |
| UI-0308 | DropsPage / Soop /  | DataTrigger  {"Binding": "{Binding Accounts.Count}"} | DropsPage / Soop / accounts | 是 | 静态契约匹配 |
| UI-0309 | DropsPage / Soop / 启动选中账号 | Button  {"Click": "OnSoopStartAccount"} | DropsPage / Soop / accounts / 选中账号操作 | 是 | 静态契约匹配 |
| UI-0310 | DropsPage / Soop / 停止选中账号 | Button  {"Click": "OnSoopStopAccount"} | DropsPage / Soop / accounts / 选中账号操作 | 是 | 静态契约匹配 |
| UI-0311 | DropsPage / Soop / 设为主账号 | Button  {"Click": "OnSoopSetPrimaryAccount"} | DropsPage / Soop / accounts / 选中账号操作 | 是 | 静态契约匹配 |
| UI-0312 | DropsPage / Soop / 删除选中账号 | Button  {"Click": "OnSoopDeleteAccount"} | DropsPage / Soop / accounts / 选中账号操作 | 是 | 静态契约匹配 |
| UI-0313 | DropsPage / Soop / 全部开始 | Button  {"Click": "OnStart"} | DropsPage / Soop / accounts | 是 | 静态契约匹配 |
| UI-0314 | DropsPage / Soop / 全部停止 | Button  {"Click": "OnStop"} | DropsPage / Soop / accounts | 是 | 静态契约匹配 |
| UI-0315 | DropsPage / Soop /  | ComboBox SoopChannelMode {"SelectionChanged": "OnSoopChannelModeChanged"} | DropsPage / Soop / settings | 是 | 静态契约匹配 |
| UI-0316 | DropsPage / Soop /  | ComboBox SoopPriorityMission {} | DropsPage / Soop / settings | 是 | 静态契约匹配 |
| UI-0317 | DropsPage / Soop / SoopManualChannel /  | TextBox SoopManualChannel {} | DropsPage / Soop / settings / SoopManualChannel | 是 | 静态契约匹配 |
| UI-0318 | DropsPage / Soop / SoopPreferredChannel /  | TextBox SoopPreferredChannel {} | DropsPage / Soop / settings / SoopPreferredChannel | 是 | 静态契约匹配 |
| UI-0319 | DropsPage / Soop / 低流量模式 | CheckBox SoopLowBandwidth {} | DropsPage / Soop / settings | 是 | 静态契约匹配 |
| UI-0320 | DropsPage / Soop / 自动领取奖励 | CheckBox SoopAutoClaim {} | DropsPage / Soop / settings | 是 | 静态契约匹配 |
| UI-0321 | DropsPage / Soop / 没有任务时继续等待 | CheckBox SoopHangWithoutMissions {} | DropsPage / Soop / settings | 是 | 静态契约匹配 |
| UI-0322 | DropsPage / Soop / 启动 CloudLight Blizzard 时自动运行 SOOP | CheckBox SoopAutoStart {"Click": "OnAutoStartChanged"} | DropsPage / Soop / settings | 是 | 静态契约匹配 |
| UI-0323 | DropsPage / Soop /  | TextBox SoopMissionInterval {} | DropsPage / Soop / advanced | 是 | 静态契约匹配 |
| UI-0324 | DropsPage / Soop /  | TextBox SoopInventoryInterval {} | DropsPage / Soop / advanced | 是 | 静态契约匹配 |
| UI-0325 | DropsPage / Soop /  | TextBox SoopChannelInterval {} | DropsPage / Soop / advanced | 是 | 静态契约匹配 |
| UI-0326 | DropsPage / Soop /  | TextBox SoopReauthInterval {} | DropsPage / Soop / advanced | 是 | 静态契约匹配 |
| UI-0327 | DropsPage / Soop / 保存设置 | Button SoopSaveSettingsButton {"Click": "OnSaveSoop"} | DropsPage | 是 | 静态契约匹配 |
| UI-0328 | DropsPage / Soop / {Binding SoopRefreshButtonText} | Button  {"Content": "{Binding SoopRefreshButtonText}", "IsEnabled": "{Binding CanRefreshSoop}", "Click": "OnSoopRefresh"} | DropsPage / Soop / settings | 是 | 静态契约匹配 |
| UI-0329 | DropsPage / Soop /  | ProgressBar  {"Visibility": "{Binding SoopRefreshProgressVisibility}"} | DropsPage / Soop / settings | 是 | 静态契约匹配 |
| UI-0330 | DropsPage / Soop / {Binding SoopRefreshStatus} | TextBlock  {"Text": "{Binding SoopRefreshStatus}"} | DropsPage / Soop / settings | 是 | 静态契约匹配 |
| UI-0331 | DropsPage / Soop / 使用帮助 | Button  {"Click": "OnToggleHelp"} | DropsPage / Soop / overview | 是 | 静态契约匹配 |
| UI-0332 | DropsPage / Soop / {Binding SoopQuickStart.Summary} | Expander  {"Header": "{Binding SoopQuickStart.Summary}", "IsExpanded": "{Binding SoopQuickStart.IsExpanded, Mode=TwoWay}"} | DropsPage / Soop / overview | 是 | 静态契约匹配 |
| UI-0333 | DropsPage / Soop /  | ItemsControl  {"ItemsSource": "{Binding SoopQuickStart.Steps}"} | DropsPage / Soop / overview | 是 | 静态契约匹配 |
| UI-0334 | DropsPage / YouTube /  | StackPanel YouTubePanel {"Visibility": "{Binding YouTubePanelVisibility}"} | DropsPage / YouTube | 是 | 静态契约匹配 |
| UI-0335 | DropsPage / YouTube / {Binding YouTubeLastSuccessText} | TextBlock  {"Text": "{Binding YouTubeLastSuccessText}", "Visibility": "{Binding YouTubeLastSuccessVisibility}"} | DropsPage / YouTube / overview | 是 | 静态契约匹配 |
| UI-0336 | DropsPage / YouTube /  | ListBox  {"ItemsSource": "{Binding Tasks}"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0337 | DropsPage / YouTube /  | DataTrigger  {"Binding": "{Binding Tasks.Count}"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0338 | DropsPage / YouTube /  | ListBox YouTubeProfilesList {"ItemsSource": "{Binding Accounts}"} | DropsPage / YouTube / accounts | 是 | 静态契约匹配 |
| UI-0339 | DropsPage / YouTube /  | DataTrigger  {"Binding": "{Binding Accounts.Count}"} | DropsPage / YouTube / accounts | 是 | 静态契约匹配 |
| UI-0340 | DropsPage / YouTube /  | TextBox YouTubeProfileName {} | DropsPage / YouTube / accounts | 是 | 静态契约匹配 |
| UI-0341 | DropsPage / YouTube / 添加账号 | Button  {"Click": "OnYouTubeAddProfile"} | DropsPage / YouTube / accounts | 是 | 静态契约匹配 |
| UI-0342 | DropsPage / YouTube / 打开登录窗口 | Button  {"Click": "OnYouTubeLogin"} | DropsPage / YouTube / accounts | 是 | 静态契约匹配 |
| UI-0343 | DropsPage / YouTube / 删除账号 | Button  {"Click": "OnYouTubeDeleteProfile"} | DropsPage / YouTube / accounts | 是 | 静态契约匹配 |
| UI-0344 | DropsPage / YouTube /  | ListBox YouTubeChannelsList {"ItemsSource": "{Binding Channels}"} | DropsPage / YouTube / activities | 是 | 静态契约匹配 |
| UI-0345 | DropsPage / YouTube /  | DataTrigger  {"Binding": "{Binding Channels.Count}"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0346 | DropsPage / YouTube /  | TextBox YouTubeChannelName {} | DropsPage / YouTube / activities | 是 | 静态契约匹配 |
| UI-0347 | DropsPage / YouTube /  | TextBox YouTubeChannelId {} | DropsPage / YouTube / activities | 是 | 静态契约匹配 |
| UI-0348 | DropsPage / YouTube /  | TextBox YouTubeChannelUrl {} | DropsPage / YouTube / activities | 是 | 静态契约匹配 |
| UI-0349 | DropsPage / YouTube / 添加频道 | Button  {"Click": "OnYouTubeAddChannel"} | DropsPage / YouTube / activities | 是 | 静态契约匹配 |
| UI-0350 | DropsPage / YouTube / 启用 / 停用 | Button  {"Click": "OnYouTubeToggleChannel"} | DropsPage / YouTube / activities | 是 | 静态契约匹配 |
| UI-0351 | DropsPage / YouTube / 删除 | Button  {"Click": "OnYouTubeDeleteChannel"} | DropsPage / YouTube / activities | 是 | 静态契约匹配 |
| UI-0352 | DropsPage / YouTube /  | ComboBox YouTubeBrowser {"SelectionChanged": "OnYouTubeBrowserChanged"} | DropsPage / YouTube / settings | 是 | 静态契约匹配 |
| UI-0353 | DropsPage / YouTube / 自定义浏览器路径（可选） / 自定义浏览器路径（可选） | Expander  {} | DropsPage / YouTube / settings / 自定义浏览器路径（可选） | 是 | 静态契约匹配 |
| UI-0354 | DropsPage / YouTube / 自定义浏览器路径（可选） / 浏览... | Button  {"Click": "OnBrowseYouTubeBrowser"} | DropsPage / YouTube / settings / 自定义浏览器路径（可选） | 是 | 静态契约匹配 |
| UI-0355 | DropsPage / YouTube / 自定义浏览器路径（可选） /  | TextBox YouTubeBrowserPath {"ToolTip": "{Binding Text, RelativeSource={RelativeSource Self}}"} | DropsPage / YouTube / settings / 自定义浏览器路径（可选） | 是 | 静态契约匹配 |
| UI-0356 | DropsPage / YouTube /  | ComboBox YouTubeMode {"SelectionChanged": "OnYouTubeModeChanged"} | DropsPage / YouTube / settings | 是 | 静态契约匹配 |
| UI-0357 | DropsPage / YouTube / YouTubeManualUrl /  | TextBox YouTubeManualUrl {} | DropsPage / YouTube / settings / YouTubeManualUrl | 是 | 静态契约匹配 |
| UI-0358 | DropsPage / YouTube / 无头模式 | CheckBox YouTubeHeadless {} | DropsPage / YouTube / settings | 是 | 静态契约匹配 |
| UI-0359 | DropsPage / YouTube / 静音播放 | CheckBox YouTubeMute {} | DropsPage / YouTube / settings | 是 | 静态契约匹配 |
| UI-0360 | DropsPage / YouTube /  | TextBox YouTubeInterval {} | DropsPage / YouTube / settings | 是 | 静态契约匹配 |
| UI-0361 | DropsPage / YouTube / 保存设置 | Button YouTubeSaveSettingsButton {"Click": "OnSaveYouTube"} | DropsPage | 是 | 静态契约匹配 |
| UI-0362 | DropsPage / YouTube /  | ListBox  {"ItemsSource": "{Binding History}"} | DropsPage / YouTube / activities | 是 | 静态契约匹配 |
| UI-0363 | DropsPage / YouTube /  | DataTrigger  {"Binding": "{Binding History.Count}"} | DropsPage / YouTube / activities | 是 | 静态契约匹配 |
| UI-0364 | DropsPage / YouTube / 使用帮助 | Button  {"Click": "OnToggleHelp"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0365 | DropsPage / YouTube / 开始观看 | Button YouTubeStartButton {"Visibility": "{Binding YouTube.StartVisibility}", "Click": "OnStart"} | DropsPage / YouTube / overview | 是 | 静态契约匹配 |
| UI-0366 | DropsPage / YouTube / 停止观看 | Button YouTubeStopButton {"Visibility": "{Binding YouTube.StopVisibility}", "Click": "OnStop"} | DropsPage / YouTube / overview | 是 | 静态契约匹配 |
| UI-0367 | DropsPage / YouTube / {Binding YouTubeQuickStart.Summary} | Expander  {"Header": "{Binding YouTubeQuickStart.Summary}", "IsExpanded": "{Binding YouTubeQuickStart.IsExpanded, Mode=TwoWay}"} | DropsPage / YouTube / overview | 是 | 静态契约匹配 |
| UI-0368 | DropsPage / YouTube /  | ItemsControl  {"ItemsSource": "{Binding YouTubeQuickStart.Steps}"} | DropsPage / YouTube / overview | 是 | 静态契约匹配 |
| UI-0369 | DropsPage / Twitch /  | StackPanel TwitchPanel {"Visibility": "{Binding TwitchPanelVisibility}"} | DropsPage / Twitch | 是 | 静态契约匹配 |
| UI-0370 | DropsPage / Twitch /  | Border  {"Visibility": "{Binding TwitchAuthorizationVisibility}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0371 | DropsPage / Twitch / {Binding TwitchAuthorizationCode, Mode=OneWay} | TextBox TwitchAuthorizationCodeBox {"Text": "{Binding TwitchAuthorizationCode, Mode=OneWay}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0372 | DropsPage / Twitch / {Binding TwitchAuthorizationExpiryText} | TextBlock  {"Text": "{Binding TwitchAuthorizationExpiryText}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0373 | DropsPage / Twitch / 打开 Twitch 授权页面 | Button  {"Click": "OnOpenTwitchAuthorization"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0374 | DropsPage / Twitch / 复制授权码 | Button TwitchCopyCodeButton {"Click": "OnCopyTwitchAuthorizationCode"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0375 | DropsPage / Twitch / {Binding TwitchChannelText} | TextBlock  {"Text": "{Binding TwitchChannelText}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0376 | DropsPage / Twitch / {Binding TwitchDataSourceText} | TextBlock  {"Text": "{Binding TwitchDataSourceText}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0377 | DropsPage / Twitch / {Binding TwitchCurrentProgressText} | TextBlock  {"Text": "{Binding TwitchCurrentProgressText}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0378 | DropsPage / Twitch / {Binding TwitchWatchRequestText} | TextBlock  {"Text": "{Binding TwitchWatchRequestText}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0379 | DropsPage / Twitch / {Binding TwitchNetworkRouteText} | TextBlock  {"Text": "{Binding TwitchNetworkRouteText}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0380 | DropsPage / Twitch /  | ProgressBar  {"Value": "{Binding TwitchCurrentProgress, Mode=OneWay}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0381 | DropsPage / Twitch / 使用帮助 | Button  {"Click": "OnToggleHelp"} | DropsPage / YouTube / overview | 是 | 静态契约匹配 |
| UI-0382 | DropsPage / Twitch / {Binding TwitchQuickStart.Summary} | Expander  {"Header": "{Binding TwitchQuickStart.Summary}", "IsExpanded": "{Binding TwitchQuickStart.IsExpanded, Mode=TwoWay}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0383 | DropsPage / Twitch /  | ItemsControl  {"ItemsSource": "{Binding TwitchQuickStart.Steps}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0384 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding TwitchConnectionStage}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0385 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding TwitchConnectionStage}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0386 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding TwitchConnectionStage}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0387 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding TwitchConnectionStage}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0388 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding TwitchConnectionStage}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0389 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding TwitchConnectionStage}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0390 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding TwitchConnectionStage}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0391 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding TwitchConnectionStage}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0392 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding TwitchConnectionStage}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0393 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding TwitchConnectionStage}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0394 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding TwitchConnectionStage}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0395 | DropsPage / Twitch / {Binding Twitch.Status} | TextBlock  {"Text": "{Binding Twitch.Status}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0396 | DropsPage / Twitch / {Binding Twitch.Summary} | TextBlock  {"Text": "{Binding Twitch.Summary}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0397 | DropsPage / Twitch / {Binding TwitchLoginButtonText} | Button  {"Content": "{Binding TwitchLoginButtonText}", "IsEnabled": "{Binding CanTwitchLogin}", "Visibility": "{Binding TwitchLoginVisibility}", "Click": "OnTwitchLogin"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0398 | DropsPage / Twitch / 清除登录信息 | Button  {"IsEnabled": "{Binding CanClearTwitchLogin}", "Click": "OnTwitchLogout"} | DropsPage / Twitch / accounts | 是 | 静态契约匹配 |
| UI-0399 | DropsPage / Twitch / 重新检测 | Button  {"Visibility": "{Binding TwitchRetryVisibility}", "Click": "OnTwitchRetry"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0400 | DropsPage / Twitch / 备份并恢复默认配置 | Button  {"Visibility": "{Binding TwitchConfigRepairVisibility}", "Click": "OnRepairTwitchSettings"} | DropsPage / Twitch / network | 是 | 静态契约匹配 |
| UI-0401 | DropsPage / Twitch / 开始 Twitch 掉宝 | Button  {"IsEnabled": "{Binding IsTwitchCoreAvailable}", "Visibility": "{Binding Twitch.StartVisibility}", "Click": "OnStart"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0402 | DropsPage / Twitch / 停止 Twitch 掉宝 | Button  {"Visibility": "{Binding Twitch.StopVisibility}", "Click": "OnStop"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0403 | DropsPage / Twitch / {Binding TwitchRefreshButtonText} | Button TwitchRefreshButton {"Content": "{Binding TwitchRefreshButtonText}", "IsEnabled": "{Binding CanRefreshTwitch}", "Click": "OnTwitchReload"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0404 | DropsPage / Twitch /  | ProgressBar  {"Visibility": "{Binding TwitchRefreshProgressVisibility}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0405 | DropsPage / Twitch / {Binding TwitchRefreshStatus} | TextBlock  {"Text": "{Binding TwitchRefreshStatus}", "Visibility": "{Binding TwitchRefreshStatusVisibility}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0406 | DropsPage / Twitch /  | Border  {"Visibility": "{Binding TwitchRetryStatusVisibility}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0407 | DropsPage / Twitch / 立即重试 | Button  {"Visibility": "{Binding TwitchRetryNowVisibility}", "Click": "OnTwitchRetryNow"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0408 | DropsPage / Twitch / {Binding TwitchRetryStatusText} | TextBlock  {"Text": "{Binding TwitchRetryStatusText}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0409 | DropsPage / Twitch / {Binding TwitchLastSuccessText} | TextBlock  {"Text": "{Binding TwitchLastSuccessText}", "Visibility": "{Binding TwitchLastSuccessVisibility}"} | DropsPage / Twitch / overview | 是 | 静态契约匹配 |
| UI-0410 | DropsPage / Twitch / 启动 CloudLight Blizzard 时自动运行 Twitch | CheckBox TwitchAutoStart {"Click": "OnAutoStartChanged"} | DropsPage / Twitch / automation | 是 | 静态契约匹配 |
| UI-0411 | DropsPage / Twitch / 打开背包 | Button  {"Click": "OnOpenTwitchInventory"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0412 | DropsPage / Twitch /  | ListBox  {"ItemsSource": "{Binding Inventory}"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0413 | DropsPage / Twitch / {Binding TwitchInventoryEmptyText} | TextBlock  {"Text": "{Binding TwitchInventoryEmptyText}"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0414 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding Inventory.Count}"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0415 | DropsPage / Twitch /  | ListBox TwitchChannelsList {"ItemsSource": "{Binding Channels}"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0416 | DropsPage / Twitch / {Binding TwitchChannelsEmptyText} | TextBlock  {"Text": "{Binding TwitchChannelsEmptyText}"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0417 | DropsPage / Twitch /  | DataTrigger  {"Binding": "{Binding Channels.Count}"} | DropsPage / YouTube / activities | 是 | 静态契约匹配 |
| UI-0418 | DropsPage / Twitch / 选择并切换频道 | Button  {"Click": "OnTwitchSelectChannel"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0419 | DropsPage / Bilibili /  | StackPanel BilibiliPanel {"Visibility": "{Binding BilibiliPanelVisibility}"} | DropsPage / Bilibili | 是 | 静态契约匹配 |
| UI-0420 | DropsPage / Bilibili / {Binding BilibiliDetails.LoginStateText} | TextBlock  {"Text": "{Binding BilibiliDetails.LoginStateText}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0421 | DropsPage / Bilibili / {Binding BilibiliDetails.AccountStatus} | TextBlock  {"Text": "{Binding BilibiliDetails.AccountStatus}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0422 | DropsPage / Bilibili / {Binding BilibiliDetails.NetworkModeText} | TextBlock  {"Text": "{Binding BilibiliDetails.NetworkModeText}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0423 | DropsPage / Bilibili / {Binding BilibiliDetails.NetworkPolicyText} | TextBlock  {"Text": "{Binding BilibiliDetails.NetworkPolicyText}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0424 | DropsPage / Bilibili / {Binding Bilibili.Status} | TextBlock  {"Text": "{Binding Bilibili.Status}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0425 | DropsPage / Bilibili / {Binding Bilibili.ConnectionStateText} | TextBlock  {"Text": "{Binding Bilibili.ConnectionStateText}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0426 | DropsPage / Bilibili / 扫码登录 | Button BilibiliQrLoginButton {"Visibility": "{Binding BilibiliDetails.LoginVisibility}", "Command": "{Binding BilibiliDetails.ScanQrLoginCommand}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0427 | DropsPage / Bilibili / 退出登录 | Button  {"Visibility": "{Binding BilibiliDetails.LogoutVisibility}", "Command": "{Binding BilibiliDetails.LogoutCommand}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0428 | DropsPage / Bilibili / 开始掉宝 | Button BilibiliStartButton {"Visibility": "{Binding Bilibili.StartVisibility}", "Command": "{Binding BilibiliDetails.StartCommand}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0429 | DropsPage / Bilibili / 停止掉宝 | Button BilibiliStopButton {"Visibility": "{Binding Bilibili.StopVisibility}", "Command": "{Binding BilibiliDetails.StopCommand}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0430 | DropsPage / Bilibili / 刷新进度 | Button BilibiliRefreshButton {"Command": "{Binding BilibiliDetails.RefreshCommand}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0431 | DropsPage / Bilibili / 发现活动 | Button BilibiliDiscoverButton {"Command": "{Binding BilibiliDetails.DiscoverCommand}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0432 | DropsPage / Bilibili / {Binding BilibiliDetails.QrStateText} | TextBlock  {"Text": "{Binding BilibiliDetails.QrStateText}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0433 | DropsPage / Bilibili / 取消扫码 | Button BilibiliQrCancelButton {"Visibility": "{Binding BilibiliDetails.QrCancelVisibility}", "Command": "{Binding BilibiliDetails.CancelQrCommand}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0434 | DropsPage / Bilibili / 重新获取二维码 | Button  {"Visibility": "{Binding BilibiliDetails.QrRetryVisibility}", "Command": "{Binding BilibiliDetails.ReacquireQrCommand}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0435 | DropsPage / Bilibili /  | Border BilibiliQrCard {"Visibility": "{Binding BilibiliDetails.QrAreaVisibility}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0436 | DropsPage / Bilibili /  | Image  {"Source": "{Binding BilibiliDetails.QrImagePath}", "Visibility": "{Binding BilibiliDetails.QrImageVisibility}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0437 | DropsPage / Bilibili / {Binding BilibiliQuickStart.Summary} | Expander BilibiliQuickStartCard {"Header": "{Binding BilibiliQuickStart.Summary}", "IsExpanded": "{Binding BilibiliQuickStart.IsExpanded, Mode=TwoWay}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0438 | DropsPage / Bilibili /  | ItemsControl  {"ItemsSource": "{Binding BilibiliQuickStart.Steps}"} | DropsPage / Bilibili / overview | 是 | 静态契约匹配 |
| UI-0439 | DropsPage / Bilibili /  | PasswordBox BilibiliCookieBox {} | DropsPage / Bilibili / accounts | 是 | 静态契约匹配 |
| UI-0440 | DropsPage / Bilibili / 导入并验证 Cookie | Button  {"Command": "{Binding BilibiliDetails.ManualCookieCommand}"} | DropsPage / Bilibili / accounts | 是 | 静态契约匹配 |
| UI-0441 | DropsPage / Bilibili / {Binding BilibiliDetails.RoomReference, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged} | TextBox BilibiliRoomInput {"Text": "{Binding BilibiliDetails.RoomReference, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0442 | DropsPage / Bilibili / 添加 | Button  {"Command": "{Binding BilibiliDetails.AddRoomCommand}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0443 | DropsPage / Bilibili / {Binding BilibiliDetails.RoomName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged} | TextBox BilibiliRoomNameInput {"Text": "{Binding BilibiliDetails.RoomName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0444 | DropsPage / Bilibili /  | ListBox BilibiliRoomList {"ItemsSource": "{Binding BilibiliDetails.Rooms}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0445 | DropsPage / Bilibili / {Binding BilibiliDetails.Activities.Count, StringFormat=已发现活动：{0} 个} | TextBlock  {"Text": "{Binding BilibiliDetails.Activities.Count, StringFormat=已发现活动：{0} 个}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0446 | DropsPage / Bilibili /  | ListBox BilibiliActivityList {"ItemsSource": "{Binding BilibiliDetails.Activities}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0447 | DropsPage / Bilibili /  | ComboBox BilibiliWatchModePicker {"SelectedValue": "{Binding BilibiliDetails.WatchMode, Mode=TwoWay}"} | DropsPage / Bilibili / settings | 是 | 静态契约匹配 |
| UI-0448 | DropsPage / Bilibili / {Binding BilibiliDetails.SessionsPerRoomText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged} | TextBox BilibiliSessionsPerRoomText {"Text": "{Binding BilibiliDetails.SessionsPerRoomText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | DropsPage / Bilibili / settings | 是 | 静态契约匹配 |
| UI-0449 | DropsPage / Bilibili / {Binding BilibiliDetails.ReconnectDelayText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged} | TextBox BilibiliReconnectDelayText {"Text": "{Binding BilibiliDetails.ReconnectDelayText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | DropsPage / Bilibili / settings | 是 | 静态契约匹配 |
| UI-0450 | DropsPage / Bilibili / {Binding BilibiliDetails.SessionEstimateText} | TextBlock  {"Text": "{Binding BilibiliDetails.SessionEstimateText}"} | DropsPage / Bilibili / settings | 是 | 静态契约匹配 |
| UI-0451 | DropsPage / Bilibili / 前往网络设置 | Button  {"Click": "OnOpenProxySettings"} | DropsPage / Bilibili / network | 是 | 静态契约匹配 |
| UI-0452 | DropsPage / Bilibili / {Binding BilibiliDetails.NetworkModeText} | TextBlock  {"Text": "{Binding BilibiliDetails.NetworkModeText}"} | DropsPage / Bilibili / network | 是 | 静态契约匹配 |
| UI-0453 | DropsPage / Bilibili / {Binding BilibiliDetails.NetworkPolicyText} | TextBlock  {"Text": "{Binding BilibiliDetails.NetworkPolicyText}"} | DropsPage / Bilibili / network | 是 | 静态契约匹配 |
| UI-0454 | DropsPage / Bilibili / 全局代理尚未启用/配置，哔哩哔哩当前使用直连。 | TextBlock  {"Visibility": "{Binding BilibiliDetails.ProxyConfigurationWarningVisibility}"} | DropsPage / Bilibili / network | 是 | 静态契约匹配 |
| UI-0455 | DropsPage / Bilibili / {Binding BilibiliDetails.TaskIntervalText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged} | TextBox BilibiliTaskIntervalText {"Text": "{Binding BilibiliDetails.TaskIntervalText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | DropsPage / Bilibili / settings | 是 | 静态契约匹配 |
| UI-0456 | DropsPage / Bilibili / {Binding BilibiliDetails.TaskIdsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged} | TextBox BilibiliTaskIdsText {"Text": "{Binding BilibiliDetails.TaskIdsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | DropsPage / Bilibili / settings | 是 | 静态契约匹配 |
| UI-0457 | DropsPage / Bilibili / 启用哔哩哔哩 | CheckBox BilibiliEnabledCheck {"IsChecked": "{Binding BilibiliDetails.Enabled, Mode=TwoWay}"} | DropsPage / Bilibili / automation | 是 | 静态契约匹配 |
| UI-0458 | DropsPage / Bilibili / 自动查询任务进度 | CheckBox BilibiliAutoTaskProgress {"IsChecked": "{Binding BilibiliDetails.AutoTaskProgress, Mode=TwoWay}"} | DropsPage / Bilibili / automation | 是 | 静态契约匹配 |
| UI-0459 | DropsPage / Bilibili / 自动发现当前活动 | CheckBox BilibiliAutoDiscoverCheck {"IsChecked": "{Binding BilibiliDetails.AutoDiscover, Mode=TwoWay}"} | DropsPage / Bilibili / automation | 是 | 静态契约匹配 |
| UI-0460 | DropsPage / Bilibili / 自动领取奖励 | CheckBox BilibiliAutoClaimCheck {"IsChecked": "{Binding BilibiliDetails.AutoClaim, Mode=TwoWay}"} | DropsPage / Bilibili / automation | 是 | 静态契约匹配 |
| UI-0461 | DropsPage / Bilibili / 任务完成通知 | CheckBox BilibiliTaskNotificationsCheck {"IsChecked": "{Binding BilibiliDetails.TaskNotifications, Mode=TwoWay}"} | DropsPage / Bilibili / automation | 是 | 静态契约匹配 |
| UI-0462 | DropsPage / Bilibili / 断线自动重连 | CheckBox BilibiliReconnectCheck {"IsChecked": "{Binding BilibiliDetails.ReconnectEnabled, Mode=TwoWay}"} | DropsPage / Bilibili / automation | 是 | 静态契约匹配 |
| UI-0463 | DropsPage / Bilibili / 启动 CloudLight Blizzard 时自动恢复 Worker | CheckBox BilibiliAutoStart {"IsChecked": "{Binding BilibiliDetails.AutoRestore, Mode=TwoWay}", "Click": "OnAutoStartChanged"} | DropsPage / Bilibili / automation | 是 | 静态契约匹配 |
| UI-0464 | DropsPage / Bilibili / 上次正在掉宝时自动恢复掉宝 | CheckBox BilibiliAutoResume {"IsChecked": "{Binding BilibiliDetails.AutoResume, Mode=TwoWay}", "Click": "OnAutoStartChanged"} | DropsPage / Bilibili / automation | 是 | 静态契约匹配 |
| UI-0465 | DropsPage / Bilibili / {Binding BilibiliDetails.ProxyConfigurationText} | TextBlock  {"Text": "{Binding BilibiliDetails.ProxyConfigurationText}"} | DropsPage / Bilibili / settings | 是 | 静态契约匹配 |
| UI-0466 | DropsPage / Bilibili /  | PasswordBox BilibiliNotifierUrlBox {} | DropsPage / Bilibili / automation | 是 | 静态契约匹配 |
| UI-0467 | DropsPage / Bilibili / {Binding BilibiliDetails.NotifierText} | TextBlock  {"Text": "{Binding BilibiliDetails.NotifierText}"} | DropsPage / Bilibili / automation | 是 | 静态契约匹配 |
| UI-0468 | DropsPage / Bilibili / 保存设置 | Button  {"Command": "{Binding BilibiliDetails.SaveSettingsCommand}"} | DropsPage | 是 | 静态契约匹配 |
| UI-0469 | DropsPage / Bilibili / 清除第三方通知 | Button  {"Command": "{Binding BilibiliDetails.ClearNotifierCommand}"} | DropsPage / Bilibili / automation | 是 | 静态契约匹配 |
| UI-0470 | DropsPage / Bilibili / 刷新 Session 详情 | Button  {"Command": "{Binding BilibiliDetails.RefreshSessionsCommand}"} | DropsPage / Bilibili / settings | 是 | 静态契约匹配 |
| UI-0471 | DropsPage / Bilibili / {Binding BilibiliDetails.SessionSummary} | TextBlock  {"Text": "{Binding BilibiliDetails.SessionSummary}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0472 | DropsPage / Bilibili / {Binding BilibiliDetails.LastApiSuccessAt, StringFormat=最近成功 API：{0}} | TextBlock  {"Text": "{Binding BilibiliDetails.LastApiSuccessAt, StringFormat=最近成功 API：{0}}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0473 | DropsPage / Bilibili /  | ListBox BilibiliTaskList {"ItemsSource": "{Binding BilibiliDetails.Tasks}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0474 | DropsPage / Bilibili /  | DataTrigger  {"Binding": "{Binding BilibiliDetails.Tasks.Count}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0475 | DropsPage / Bilibili /  | ListBox  {"ItemsSource": "{Binding BilibiliDetails.Rewards}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0476 | DropsPage / Bilibili / {Binding TaskName} | TextBlock  {"Text": "{Binding TaskName}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0477 | DropsPage / Bilibili / {Binding State} | TextBlock  {"Text": "{Binding State}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0478 | DropsPage / Bilibili / {Binding Reward} | TextBlock  {"Text": "{Binding Reward}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0479 | DropsPage / Bilibili / {Binding Message} | TextBlock  {"Text": "{Binding Message}"} | DropsPage / Bilibili / activities | 是 | 静态契约匹配 |
| UI-0480 | DropsPage / Bilibili / {Binding BilibiliDetails.ConfiguredSessions} | TextBlock  {"Text": "{Binding BilibiliDetails.ConfiguredSessions}"} | DropsPage / Bilibili / accounts | 是 | 静态契约匹配 |
| UI-0481 | DropsPage / Bilibili / {Binding BilibiliDetails.ActiveSessions} | TextBlock  {"Text": "{Binding BilibiliDetails.ActiveSessions}"} | DropsPage / Bilibili / accounts | 是 | 静态契约匹配 |
| UI-0482 | DropsPage / Bilibili / {Binding BilibiliDetails.ConnectingSessions} | TextBlock  {"Text": "{Binding BilibiliDetails.ConnectingSessions}"} | DropsPage / Bilibili / accounts | 是 | 静态契约匹配 |
| UI-0483 | DropsPage / Bilibili / {Binding BilibiliDetails.RetryingSessions} | TextBlock  {"Text": "{Binding BilibiliDetails.RetryingSessions}"} | DropsPage / Bilibili / accounts | 是 | 静态契约匹配 |
| UI-0484 | DropsPage / Bilibili / {Binding BilibiliDetails.FailedSessions} | TextBlock  {"Text": "{Binding BilibiliDetails.FailedSessions}"} | DropsPage / Bilibili / accounts | 是 | 静态契约匹配 |
| UI-0485 | DropsPage / Bilibili /  | ListBox BilibiliSessionList {"ItemsSource": "{Binding BilibiliDetails.Sessions}"} | DropsPage / Bilibili / accounts | 是 | 静态契约匹配 |
| UI-0486 | DropsPage /  | Border  {"Visibility": "{Binding TwitchPanelVisibility}"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0487 | DropsPage /  | ComboBox TwitchCampaignScopePicker {"SelectionChanged": "OnTwitchCampaignScopeChanged"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0488 | DropsPage /  | ListBox  {"ItemsSource": "{Binding Tasks}"} | DropsPage / YouTube / overview | 是 | 静态契约匹配 |
| UI-0489 | DropsPage / {Binding TwitchCampaignsEmptyText} | TextBlock  {"Text": "{Binding TwitchCampaignsEmptyText}"} | DropsPage / Twitch / activities | 是 | 静态契约匹配 |
| UI-0490 | DropsPage /  | DataTrigger  {"Binding": "{Binding Tasks.Count}"} | DropsPage / YouTube / overview | 是 | 静态契约匹配 |
| UI-0491 | DropsPage /  | Border TwitchSettingsCard {"Visibility": "{Binding TwitchPanelVisibility}"} | DropsPage / Twitch / settings | 是 | 静态契约匹配 |
| UI-0492 | DropsPage /  | ComboBox TwitchLanguage {} | DropsPage / Twitch / settings | 是 | 静态契约匹配 |
| UI-0493 | DropsPage /  | ComboBox TwitchPriorityMode {} | DropsPage / Twitch / settings | 是 | 静态契约匹配 |
| UI-0494 | DropsPage /  | ComboBox TwitchConnectionQuality {} | DropsPage / Twitch / settings | 是 | 静态契约匹配 |
| UI-0495 | DropsPage / 添加 | Button  {"Click": "OnAddPriorityGame"} | DropsPage / Twitch / games | 是 | 静态契约匹配 |
| UI-0496 | DropsPage /  | ComboBox TwitchPriorityPicker {"ItemsSource": "{Binding TwitchPriorityChoices}"} | DropsPage / Twitch / games | 是 | 静态契约匹配 |
| UI-0497 | DropsPage /  | ListBox TwitchPriorityList {"ItemsSource": "{Binding TwitchPriorityGames}"} | DropsPage / Twitch / games | 是 | 静态契约匹配 |
| UI-0498 | DropsPage / ↑ | Button  {"Click": "OnPriorityUp"} | DropsPage / Twitch / games | 是 | 静态契约匹配 |
| UI-0499 | DropsPage / ↓ | Button  {"Click": "OnPriorityDown"} | DropsPage / Twitch / games | 是 | 静态契约匹配 |
| UI-0500 | DropsPage / 移除 | Button  {"Click": "OnRemovePriorityGame"} | DropsPage / Twitch / games | 是 | 静态契约匹配 |
| UI-0501 | DropsPage / 添加 | Button  {"Click": "OnAddExcludedGame"} | DropsPage / Twitch / games | 是 | 静态契约匹配 |
| UI-0502 | DropsPage /  | ComboBox TwitchExcludePicker {"ItemsSource": "{Binding TwitchExcludeChoices}"} | DropsPage / Twitch / games | 是 | 静态契约匹配 |
| UI-0503 | DropsPage /  | ListBox TwitchExcludeList {"ItemsSource": "{Binding TwitchExcludedGames}"} | DropsPage / Twitch / games | 是 | 静态契约匹配 |
| UI-0504 | DropsPage / 移除 | Button  {"Click": "OnRemoveExcludedGame"} | DropsPage / Twitch / games | 是 | 静态契约匹配 |
| UI-0505 | DropsPage / 托盘通知 | CheckBox TwitchTrayNotifications {} | DropsPage / Twitch / automation | 是 | 静态契约匹配 |
| UI-0506 | DropsPage / 显示徽章和表情 | CheckBox TwitchBadgesEmotes {} | DropsPage / Twitch / automation | 是 | 静态契约匹配 |
| UI-0507 | DropsPage / 检查可用掉宝 | CheckBox TwitchAvailableDrops {} | DropsPage / Twitch / automation | 是 | 静态契约匹配 |
| UI-0508 | DropsPage / 自动领取掉宝 | CheckBox TwitchAutoClaim {} | DropsPage / Twitch / automation | 是 | 静态契约匹配 |
| UI-0509 | DropsPage / 保存设置 | Button TwitchSaveSettingsButton {"IsEnabled": "{Binding IsTwitchCoreAvailable}", "Click": "OnSaveTwitch"} | DropsPage | 是 | 静态契约匹配 |
| UI-0510 | DropsPage / 网络、代理与诊断 / 网络、代理与诊断 | Expander DropsDiagnosticsExpander {} | DropsPage / network / 网络、代理与诊断 | 是 | 静态契约匹配 |
| UI-0511 | DropsPage / 网络、代理与诊断 / 网络测试 | Button  {"Click": "OnNetworkCheck"} | DropsPage / network / 网络、代理与诊断 | 是 | 静态契约匹配 |
| UI-0512 | DropsPage / 网络、代理与诊断 / {Binding NetworkProxyModeText} | TextBlock  {"Text": "{Binding NetworkProxyModeText}"} | DropsPage / network / 网络、代理与诊断 | 是 | 静态契约匹配 |
| UI-0513 | DropsPage / 网络、代理与诊断 / {Binding NetworkProxyUrlText} | TextBlock  {"Text": "{Binding NetworkProxyUrlText}", "ToolTip": "{Binding NetworkProxyUrlText}"} | DropsPage / network / 网络、代理与诊断 | 是 | 静态契约匹配 |
| UI-0514 | DropsPage / 网络、代理与诊断 / {Binding NetworkProxyFallbackText} | TextBlock  {"Text": "{Binding NetworkProxyFallbackText}"} | DropsPage / network / 网络、代理与诊断 | 是 | 静态契约匹配 |
| UI-0515 | DropsPage / 网络、代理与诊断 / {Binding NetworkProxyScopeText} | TextBlock  {"Text": "{Binding NetworkProxyScopeText}"} | DropsPage / network / 网络、代理与诊断 | 是 | 静态契约匹配 |
| UI-0516 | DropsPage / 网络、代理与诊断 / SOOP | CheckBox SoopUseProxyCheck {"Checked": "OnDropsProxyScopeChanged", "Unchecked": "OnDropsProxyScopeChanged"} | DropsPage / network / 网络、代理与诊断 | 是 | 静态契约匹配 |
| UI-0517 | DropsPage / 网络、代理与诊断 / YouTube | CheckBox YouTubeUseProxyCheck {"Checked": "OnDropsProxyScopeChanged", "Unchecked": "OnDropsProxyScopeChanged"} | DropsPage / network / 网络、代理与诊断 | 是 | 静态契约匹配 |
| UI-0518 | DropsPage / 网络、代理与诊断 / Twitch | CheckBox TwitchUseProxyCheck {"Checked": "OnDropsProxyScopeChanged", "Unchecked": "OnDropsProxyScopeChanged"} | DropsPage / network / 网络、代理与诊断 | 是 | 静态契约匹配 |
| UI-0519 | DropsPage / 网络、代理与诊断 / 哔哩哔哩 | CheckBox BilibiliUseProxyCheck {"Checked": "OnDropsProxyScopeChanged", "Unchecked": "OnDropsProxyScopeChanged"} | DropsPage / network / 网络、代理与诊断 | 是 | 静态契约匹配 |
| UI-0520 | DropsPage / 网络、代理与诊断 / 前往设置 | Button  {"Click": "OnOpenProxySettings"} | DropsPage / network / 网络、代理与诊断 | 是 | 静态契约匹配 |
| UI-0521 | DropsPage / 复制全部 | Button  {"Click": "OnCopyAllLogs"} | DropsPage / logs | 是 | 静态契约匹配 |
| UI-0522 | DropsPage / 清空显示 | Button  {"Click": "OnClearLogDisplay"} | DropsPage / logs | 是 | 静态契约匹配 |
| UI-0523 | DropsPage /  | TextBox LogTextBox {"TextChanged": "OnLogTextChanged"} | DropsPage / logs | 是 | 静态契约匹配 |
| UI-0524 | DropsPage / 复制 / 复制 | MenuItem  {"Command": "Copy", "CommandTarget": "{Binding PlacementTarget, RelativeSource={RelativeSource AncestorType={x:Type ContextMenu}}}"} | DropsPage / logs / 复制 | 是 | 静态契约匹配 |
| UI-0525 | DropsPage / 全选 / 全选 | MenuItem  {"Command": "SelectAll", "CommandTarget": "{Binding PlacementTarget, RelativeSource={RelativeSource AncestorType={x:Type ContextMenu}}}"} | DropsPage / logs / 全选 | 是 | 静态契约匹配 |
| UI-0526 | Views/Pages/DropsPage.xaml.cs:884 / }.ShowDialog() == true; | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:979 | 是 | 静态契约匹配 |
| UI-0527 | Views/Pages/DropsPage.xaml.cs:943 / if (MessageBox.Show($"删除 SOOP 账号「{row.Primary}」的本地登录信息？", "删除账号", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:1038 | 是 | 静态契约匹配 |
| UI-0528 | Views/Pages/DropsPage.xaml.cs:985 / if (MessageBox.Show($"确认领取奖励「{row.Primary}」？", "领取奖励", | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:1080 | 是 | 静态契约匹配 |
| UI-0529 | Views/Pages/DropsPage.xaml.cs:1104 / if (dialog.ShowDialog() == true) YouTubeBrowserPath.Text = dialog.FileName; | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:1199 | 是 | 静态契约匹配 |
| UI-0530 | Views/Pages/DropsPage.xaml.cs:1157 / var result = MessageBox.Show("是否同时删除该观看账号的浏览器登录数据？\n\n选择“否”只从列表移除，浏览器资料目录仍保留。", "删除观看账号", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning); | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:1253 | 是 | 静态契约匹配 |
| UI-0531 | Views/Pages/DropsPage.xaml.cs:1307 / MessageBox.Show("清除 Twitch 本地登录信息并停止当前登录/连接尝试？\n\n优先游戏、排除游戏、自动启动、代理和其它界面设置都会保留。", | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:1404 | 是 | 静态契约匹配 |
| UI-0532 | Views/Pages/DropsPage.xaml.cs:1356 / if (_vm == null \|\| MessageBox.Show( | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:1453 | 是 | 静态契约匹配 |
| UI-0533 | Views/Pages/DropsPage.xaml.cs:1539 / if (_vm == null \|\| MessageBox.Show("退出哔哩哔哩登录并删除本机加密凭据？\n\n直播间和其它设置会保留。", "退出哔哩哔哩登录", | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:1636 | 是 | 静态契约匹配 |
| UI-0534 | Views/Pages/DropsPage.xaml.cs:1604 / if (_vm == null \|\| MessageBox.Show("删除已保存的 Gotify / Server 酱通知配置？", "清除第三方通知", | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:1701 | 是 | 静态契约匹配 |
| UI-0535 | Views/Pages/DropsPage.xaml.cs:2046 / if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return; | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:2144 | 是 | 静态契约匹配 |
| UI-0536 | Views/Pages/DropsPage.xaml.cs:2053 / var choice = MessageBox.Show($"目标已有「{relative}」。\n\n是：覆盖\n否：跳过\n取消：终止导入", "同名数据", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning); | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:2151 | 是 | 静态契约匹配 |
| UI-0537 | Views/Pages/DropsPage.xaml.cs:2058 / MessageBox.Show(message, "导入旧版数据", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning); | confirmation/command  {} | Views/Pages/DropsPage.xaml.cs:2156 | 是 | 静态契约匹配 |
| UI-0538 | FeedbackPanel /  | TextBox TitleBox {} | FeedbackPanel | 是 | 静态契约匹配 |
| UI-0539 | FeedbackPanel /  | TextBox DescriptionBox {} | FeedbackPanel | 是 | 静态契约匹配 |
| UI-0540 | FeedbackPanel /  | TextBox ContactBox {} | FeedbackPanel | 是 | 静态契约匹配 |
| UI-0541 | FeedbackPanel / 附加运行日志 | CheckBox AttachLogsBox {} | FeedbackPanel | 是 | 静态契约匹配 |
| UI-0542 | FeedbackPanel / 查看将提交的日志 | Button PreviewButton {"Click": "OnPreviewLogs"} | FeedbackPanel | 是 | 静态契约匹配 |
| UI-0543 | FeedbackPanel / 提交反馈 | Button SubmitButton {"Click": "OnSubmit"} | FeedbackPanel | 是 | 静态契约匹配 |
| UI-0544 | FeedbackPanel / Progress / 取消上传 | Button CancelUploadButton {"Click": "OnCancelUpload"} | FeedbackPanel / Progress | 是 | 静态契约匹配 |
| UI-0545 | FeedbackPanel / Result / 复制反馈编号 | Button CopyReportButton {"Click": "OnCopyReportId"} | FeedbackPanel / Result | 是 | 静态契约匹配 |
| UI-0546 | FeedbackPanel / Result / 重试 | Button RetryButton {"Click": "OnSubmit"} | FeedbackPanel / Result | 是 | 静态契约匹配 |
| UI-0547 | FeedbackPanel / Result / 复制QQ群号 | Button CopyQqButton {"Click": "OnCopyQqGroup"} | FeedbackPanel / Result | 是 | 静态契约匹配 |
| UI-0548 | Views/Pages/FeedbackPanel.xaml.cs:43 / MessageBox.Show(detail, "将提交的日志", MessageBoxButton.OK, MessageBoxImage.Information); | confirmation/command  {} | Views/Pages/FeedbackPanel.xaml.cs:43 | 是 | 静态契约匹配 |
| UI-0549 | Views/Pages/FeedbackPanel.xaml.cs:53 / MessageBox.Show("请填写错误主题和错误描述。", "提交反馈", MessageBoxButton.OK, MessageBoxImage.Information); | confirmation/command  {} | Views/Pages/FeedbackPanel.xaml.cs:53 | 是 | 静态契约匹配 |
| UI-0550 | Views/Pages/FeedbackPanel.xaml.cs:150 / dialog.ShowDialog(); | confirmation/command  {} | Views/Pages/FeedbackPanel.xaml.cs:150 | 是 | 静态契约匹配 |
| UI-0551 | RegionFilesPage / {Binding PreparationWarningTitle} | TextBlock  {"Text": "{Binding PreparationWarningTitle}"} | RegionFilesPage | 是 | 静态契约匹配 |
| UI-0552 | RegionFilesPage / {Binding PreparationWarningText} | TextBlock  {"Text": "{Binding PreparationWarningText}"} | RegionFilesPage | 是 | 静态契约匹配 |
| UI-0553 | RegionFilesPage / {Binding RegionGuide.CurrentFileText} | TextBlock  {"Text": "{Binding RegionGuide.CurrentFileText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0554 | RegionFilesPage / {Binding RegionGuide.ChinaBackupText} | TextBlock  {"Text": "{Binding RegionGuide.ChinaBackupText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0555 | RegionFilesPage / {Binding RegionGuide.InternationalBackupText} | TextBlock  {"Text": "{Binding RegionGuide.InternationalBackupText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0556 | RegionFilesPage / {Binding RegionGuide.BackupStateText} | TextBlock  {"Text": "{Binding RegionGuide.BackupStateText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0557 | RegionFilesPage / {Binding RegionGuide.BackupModeText} | TextBlock  {"Text": "{Binding RegionGuide.BackupModeText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0558 | RegionFilesPage / {Binding RegionGuide.BackupSizeText} | TextBlock  {"Text": "{Binding RegionGuide.BackupSizeText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0559 | RegionFilesPage /  | StackPanel  {"Visibility": "{Binding RegionGuide.ShowTopRegionActions, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0560 | RegionFilesPage / {Binding RegionGuide.SwitchChinaText} | Button  {"Content": "{Binding RegionGuide.SwitchChinaText}", "IsEnabled": "{Binding RegionGuide.CanSwitchChina}", "Click": "OnSwitchChina"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0561 | RegionFilesPage / {Binding RegionGuide.SwitchInternationalText} | Button  {"Content": "{Binding RegionGuide.SwitchInternationalText}", "IsEnabled": "{Binding RegionGuide.CanSwitchInternational}", "Click": "OnSwitchInternational"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0562 | RegionFilesPage /  | Border  {"Visibility": "{Binding RegionGuide.ShowTopRegionActions, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / files | 是 | 静态契约匹配 |
| UI-0563 | RegionFilesPage / 检查当前区服文件状态 | Button  {"IsEnabled": "{Binding CanCheckRegionFiles}", "Click": "OnCheckCurrentFiles"} | RegionFilesPage / files | 是 | 静态契约匹配 |
| UI-0564 | RegionFilesPage / {Binding RegionFileCheckSummary} | TextBlock  {"Text": "{Binding RegionFileCheckSummary}"} | RegionFilesPage / files | 是 | 静态契约匹配 |
| UI-0565 | RegionFilesPage / 查看详情 / 查看详情 | Expander  {"Visibility": "{Binding HasRegionFileCheck, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / files / 查看详情 | 是 | 静态契约匹配 |
| UI-0566 | RegionFilesPage / 查看详情 / {Binding RegionFileCheckDetails} | TextBlock  {"Text": "{Binding RegionFileCheckDetails}"} | RegionFilesPage / files / 查看详情 | 是 | 静态契约匹配 |
| UI-0567 | RegionFilesPage / 清除临时文件 | Button  {"IsEnabled": "{Binding CanClearTemporaryFiles}", "Click": "OnClearTemporary"} | RegionFilesPage / files | 是 | 静态契约匹配 |
| UI-0568 | RegionFilesPage / 重设当前区服状态 | Button  {"IsEnabled": "{Binding CanResetCurrentRegion}", "Click": "OnResetCurrentRegion"} | RegionFilesPage / files | 是 | 静态契约匹配 |
| UI-0569 | RegionFilesPage / {Binding RegionGuide.GamePathText} | TextBlock  {"Text": "{Binding RegionGuide.GamePathText}"} | RegionFilesPage / paths | 是 | 静态契约匹配 |
| UI-0570 | RegionFilesPage / 自动查找 | Button  {"IsEnabled": "{Binding RegionGuide.CanChangePaths}", "Click": "OnDetectGame"} | RegionFilesPage / paths | 是 | 静态契约匹配 |
| UI-0571 | RegionFilesPage / 更改位置 | Button  {"IsEnabled": "{Binding RegionGuide.CanChangePaths}", "Click": "OnChooseGame"} | RegionFilesPage / paths | 是 | 静态契约匹配 |
| UI-0572 | RegionFilesPage / {Binding RegionGuide.BackupPathText} | TextBlock  {"Text": "{Binding RegionGuide.BackupPathText}"} | RegionFilesPage / paths | 是 | 静态契约匹配 |
| UI-0573 | RegionFilesPage / 更改位置 | Button  {"IsEnabled": "{Binding RegionGuide.CanChangePaths}", "Click": "OnChooseStorage"} | RegionFilesPage / paths | 是 | 静态契约匹配 |
| UI-0574 | RegionFilesPage / 打开备份文件夹 | Button  {"Click": "OnOpenStorage"} | RegionFilesPage / switch / 高级操作 | 是 | 静态契约匹配 |
| UI-0575 | RegionFilesPage / 智能差异备份（推荐） | RadioButton VerifiedModeRadio {"IsEnabled": "{Binding RegionGuide.CanChangePaths}", "IsChecked": "{Binding IsVerifiedDifferenceMode, Mode=OneWay}", "Click": "OnVerifiedMode"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0576 | RegionFilesPage / 完整备份 | RadioButton FullModeRadio {"IsEnabled": "{Binding RegionGuide.CanChangePaths}", "IsChecked": "{Binding IsFullSnapshotMode, Mode=OneWay}", "Click": "OnFullMode"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0577 | RegionFilesPage /  | StackPanel  {"Visibility": "{Binding RegionGuide.ShowNotPrepared, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0578 | RegionFilesPage / {Binding RegionGuide.StepText} | TextBlock  {"Text": "{Binding RegionGuide.StepText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0579 | RegionFilesPage / {Binding RegionGuide.Title} | TextBlock  {"Text": "{Binding RegionGuide.Title}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0580 | RegionFilesPage / {Binding RegionGuide.Description} | TextBlock  {"Text": "{Binding RegionGuide.Description}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0581 | RegionFilesPage / {Binding RegionGuide} | ContentControl  {"Content": "{Binding RegionGuide}", "Visibility": "{Binding RegionGuide.ShowPreparationWarning, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0582 | RegionFilesPage /  | Border  {"Visibility": "{Binding RegionGuide.ShowGamePathRequired, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0583 | RegionFilesPage / 当前游戏是国服 | Button  {"IsEnabled": "{Binding RegionGuide.CanChooseCurrentRegion}", "Click": "OnCaptureChina"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0584 | RegionFilesPage / 当前游戏是国际服 | Button  {"IsEnabled": "{Binding RegionGuide.CanChooseCurrentRegion}", "Click": "OnCaptureInternational"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0585 | RegionFilesPage /  | StackPanel  {"Visibility": "{Binding RegionGuide.ShowWaiting, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0586 | RegionFilesPage / {Binding RegionGuide.FirstStepCompleteText} | TextBlock  {"Text": "{Binding RegionGuide.FirstStepCompleteText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0587 | RegionFilesPage / {Binding RegionGuide.StepText} | TextBlock  {"Text": "{Binding RegionGuide.StepText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0588 | RegionFilesPage / {Binding RegionGuide.Title} | TextBlock  {"Text": "{Binding RegionGuide.Title}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0589 | RegionFilesPage / {Binding RegionGuide.PreparationRouteText} | TextBlock  {"Text": "{Binding RegionGuide.PreparationRouteText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0590 | RegionFilesPage / {Binding RegionGuide.Description} | TextBlock  {"Text": "{Binding RegionGuide.Description}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0591 | RegionFilesPage / {Binding RegionGuide} | ContentControl  {"Content": "{Binding RegionGuide}", "Visibility": "{Binding RegionGuide.ShowPreparationWarning, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0592 | RegionFilesPage / {Binding RegionGuide.ContinueButtonText} | Button  {"Content": "{Binding RegionGuide.ContinueButtonText}", "IsEnabled": "{Binding RegionGuide.CanContinueOtherRegion}", "Click": "OnComplete"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0593 | RegionFilesPage / 重新执行步骤 1 | Button  {"IsEnabled": "{Binding RegionGuide.CanRedoStep1}", "Visibility": "{Binding RegionGuide.CanRedoStep1, Converter={StaticResource BoolVisibility}}", "Click": "OnRedoStep1"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0594 | RegionFilesPage / 重新执行步骤 2 | Button  {"IsEnabled": "{Binding RegionGuide.CanRedoStep2}", "Visibility": "{Binding RegionGuide.CanRedoStep2, Converter={StaticResource BoolVisibility}}", "Click": "OnRedoStep2"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0595 | RegionFilesPage / 返回步骤 1 | Button  {"IsEnabled": "{Binding RegionGuide.CanReturnToStep1}", "Visibility": "{Binding RegionGuide.CanReturnToStep1, Converter={StaticResource BoolVisibility}}", "Click": "OnReturnStep1"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0596 | RegionFilesPage / 重新开始准备 | Button  {"IsEnabled": "{Binding RegionGuide.CanRestart}", "Click": "OnReprepare"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0597 | RegionFilesPage /  | StackPanel  {"Visibility": "{Binding RegionGuide.ShowProgress, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0598 | RegionFilesPage /  | StackPanel  {"Visibility": "{Binding RegionGuide.ShowCompletedPreparationSteps, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0599 | RegionFilesPage / {Binding RegionGuide.FirstStepCompleteText} | TextBlock  {"Text": "{Binding RegionGuide.FirstStepCompleteText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0600 | RegionFilesPage / {Binding RegionGuide.SecondStepCompleteText} | TextBlock  {"Text": "{Binding RegionGuide.SecondStepCompleteText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0601 | RegionFilesPage / {Binding RegionGuide.StepText} | TextBlock  {"Text": "{Binding RegionGuide.StepText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0602 | RegionFilesPage / {Binding RegionGuide.Title} | TextBlock  {"Text": "{Binding RegionGuide.Title}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0603 | RegionFilesPage / {Binding RegionGuide.Description} | TextBlock  {"Text": "{Binding RegionGuide.Description}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0604 | RegionFilesPage / {Binding RegionGuide} | ContentControl  {"Content": "{Binding RegionGuide}", "Visibility": "{Binding RegionGuide.ShowPreparationWarning, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0605 | RegionFilesPage / {Binding RegionGuide.ProgressText} | TextBlock  {"Text": "{Binding RegionGuide.ProgressText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0606 | RegionFilesPage /  | ProgressBar  {"Maximum": "{Binding RegionGuide.ProgressTotal, Mode=OneWay}", "Value": "{Binding RegionGuide.ProgressCurrent, Mode=OneWay}", "IsIndeterminate": "{Binding RegionGuide.ProgressIndeterminate, Mode=OneWay}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0607 | RegionFilesPage / 取消 | Button  {"IsEnabled": "{Binding RegionGuide.CanCancel}", "Visibility": "{Binding RegionGuide.CanCancel, Converter={StaticResource BoolVisibility}}", "Click": "OnCancel"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0608 | RegionFilesPage /  | StackPanel  {"Visibility": "{Binding RegionGuide.ShowReady, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0609 | RegionFilesPage / {Binding RegionGuide.Title} | TextBlock  {"Text": "{Binding RegionGuide.Title}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0610 | RegionFilesPage / {Binding RegionGuide.Description} | TextBlock  {"Text": "{Binding RegionGuide.Description}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0611 | RegionFilesPage / {Binding RegionGuide.ChinaBackupText} | TextBlock  {"Text": "{Binding RegionGuide.ChinaBackupText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0612 | RegionFilesPage / {Binding RegionGuide.InternationalBackupText} | TextBlock  {"Text": "{Binding RegionGuide.InternationalBackupText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0613 | RegionFilesPage / {Binding RegionGuide.DifferenceText} | TextBlock  {"Text": "{Binding RegionGuide.DifferenceText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0614 | RegionFilesPage / {Binding RegionGuide.BackupBytesText} | TextBlock  {"Text": "{Binding RegionGuide.BackupBytesText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0615 | RegionFilesPage / {Binding RegionGuide.WarningSummaryText} | Expander  {"Header": "{Binding RegionGuide.WarningSummaryText}", "Visibility": "{Binding RegionGuide.ShowWarningDetails, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0616 | RegionFilesPage / {Binding RegionGuide.WarningDetailsText} | TextBlock  {"Text": "{Binding RegionGuide.WarningDetailsText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0617 | RegionFilesPage / 重新准备区服文件 | Button  {"IsEnabled": "{Binding RegionGuide.CanRestart}", "Click": "OnReprepare"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0618 | RegionFilesPage / 高级操作 / 高级操作 | Expander  {} | RegionFilesPage / switch / 高级操作 | 是 | 静态契约匹配 |
| UI-0619 | RegionFilesPage / 高级操作 / 打开备份文件夹 | Button  {"Click": "OnOpenStorage"} | RegionFilesPage / paths | 是 | 静态契约匹配 |
| UI-0620 | RegionFilesPage /  | StackPanel  {"Visibility": "{Binding RegionGuide.ShowOutdated, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0621 | RegionFilesPage / {Binding RegionGuide.StepText} | TextBlock  {"Text": "{Binding RegionGuide.StepText}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0622 | RegionFilesPage / {Binding RegionGuide.Title} | TextBlock  {"Text": "{Binding RegionGuide.Title}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0623 | RegionFilesPage / {Binding RegionGuide.Description} | TextBlock  {"Text": "{Binding RegionGuide.Description}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0624 | RegionFilesPage / 重新准备区服文件 | Button  {"IsEnabled": "{Binding RegionGuide.CanRestart}", "Click": "OnReprepare"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0625 | RegionFilesPage /  | StackPanel  {"Visibility": "{Binding RegionGuide.ShowMixed, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0626 | RegionFilesPage / {Binding RegionGuide.Title} | TextBlock  {"Text": "{Binding RegionGuide.Title}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0627 | RegionFilesPage / {Binding RegionGuide.Description} | TextBlock  {"Text": "{Binding RegionGuide.Description}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0628 | RegionFilesPage / 恢复到国服 | Button  {"IsEnabled": "{Binding RegionGuide.CanRestore}", "Click": "OnSwitchChina"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0629 | RegionFilesPage / 恢复到国际服 | Button  {"IsEnabled": "{Binding RegionGuide.CanRestore}", "Click": "OnSwitchInternational"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0630 | RegionFilesPage / 重新准备 | Button  {"IsEnabled": "{Binding RegionGuide.CanRestart}", "Click": "OnReprepare"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0631 | RegionFilesPage /  | StackPanel  {"Visibility": "{Binding RegionGuide.ShowError, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0632 | RegionFilesPage / {Binding RegionGuide.Title} | TextBlock  {"Text": "{Binding RegionGuide.Title}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0633 | RegionFilesPage / {Binding RegionGuide.Description} | TextBlock  {"Text": "{Binding RegionGuide.Description}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0634 | RegionFilesPage / 重试 | Button  {"IsEnabled": "{Binding RegionGuide.CanRetry}", "Click": "OnRetry"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0635 | RegionFilesPage / 重新执行步骤 2 | Button  {"IsEnabled": "{Binding RegionGuide.CanRedoStep2}", "Visibility": "{Binding RegionGuide.CanRedoStep2, Converter={StaticResource BoolVisibility}}", "Click": "OnRedoStep2"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0636 | RegionFilesPage / 返回步骤 1 | Button  {"IsEnabled": "{Binding RegionGuide.CanReturnToStep1}", "Visibility": "{Binding RegionGuide.CanReturnToStep1, Converter={StaticResource BoolVisibility}}", "Click": "OnReturnStep1"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0637 | RegionFilesPage / 改用完整备份 | Button  {"IsEnabled": "{Binding RegionGuide.CanUseFullSnapshot}", "Visibility": "{Binding RegionGuide.CanUseFullSnapshot, Converter={StaticResource BoolVisibility}}", "Click": "OnUseFullSnapshot"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0638 | RegionFilesPage / 打开日志文件夹 | Button  {"Click": "OnOpenLogs"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0639 | RegionFilesPage / 重新准备区服文件 | Button  {"IsEnabled": "{Binding RegionGuide.CanRestart}", "Click": "OnReprepare"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0640 | RegionFilesPage /  | Border  {"Visibility": "{Binding RegionGuide.ShowNotice, Converter={StaticResource BoolVisibility}}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0641 | RegionFilesPage /  | DataTrigger  {"Binding": "{Binding RegionGuide.ShowSuccessNotice}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0642 | RegionFilesPage / {Binding RegionGuide.Notice} | TextBlock  {"Text": "{Binding RegionGuide.Notice}"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0643 | RegionFilesPage / 删除所有区服备份 | Button  {"IsEnabled": "{Binding RegionGuide.CanClear}", "Click": "OnClear"} | RegionFilesPage / switch | 是 | 静态契约匹配 |
| UI-0644 | RegionFilesPage / 为什么需要准备？ / 为什么需要准备？ | Expander  {} | RegionFilesPage / files / 为什么需要准备？ | 是 | 静态契约匹配 |
| UI-0645 | Views/Pages/RegionFilesPage.xaml.cs:43 / if (preview.ShowDialog() == true) | confirmation/command  {} | Views/Pages/RegionFilesPage.xaml.cs:52 | 是 | 静态契约匹配 |
| UI-0646 | Views/Pages/RegionFilesPage.xaml.cs:54 / MessageBox.Show("未能可靠找到守望先锋安装目录，请手动选择。", "自动查找", | confirmation/command  {} | Views/Pages/RegionFilesPage.xaml.cs:63 | 是 | 静态契约匹配 |
| UI-0647 | Views/Pages/RegionFilesPage.xaml.cs:84 / }.ShowDialog() == true; | confirmation/command  {} | Views/Pages/RegionFilesPage.xaml.cs:93 | 是 | 静态契约匹配 |
| UI-0648 | Views/Pages/RegionFilesPage.xaml.cs:101 / { Owner = Window.GetWindow(this) }.ShowDialog() == true) | confirmation/command  {} | Views/Pages/RegionFilesPage.xaml.cs:110 | 是 | 静态契约匹配 |
| UI-0649 | Views/Pages/RegionFilesPage.xaml.cs:109 / { Owner = Window.GetWindow(this) }.ShowDialog() == true) | confirmation/command  {} | Views/Pages/RegionFilesPage.xaml.cs:118 | 是 | 静态契约匹配 |
| UI-0650 | Views/Pages/RegionFilesPage.xaml.cs:141 / { Owner = Window.GetWindow(this) }.ShowDialog() != true) | confirmation/command  {} | Views/Pages/RegionFilesPage.xaml.cs:150 | 是 | 静态契约匹配 |
| UI-0651 | Views/Pages/RegionFilesPage.xaml.cs:171 / { Owner = Window.GetWindow(this) }.ShowDialog() == true) | confirmation/command  {} | Views/Pages/RegionFilesPage.xaml.cs:180 | 是 | 静态契约匹配 |
| UI-0652 | Views/Pages/RegionFilesPage.xaml.cs:183 / if (MessageBox.Show( | confirmation/command  {} | Views/Pages/RegionFilesPage.xaml.cs:192 | 是 | 静态契约匹配 |
| UI-0653 | Views/Pages/RegionFilesPage.xaml.cs:193 / if (MessageBox.Show( | confirmation/command  {} | Views/Pages/RegionFilesPage.xaml.cs:202 | 是 | 静态契约匹配 |
| UI-0654 | SettingsPage / 关闭主窗口时最小化到托盘 | CheckBox CloseToTrayBox {"Checked": "OnSettingChanged", "Unchecked": "OnSettingChanged"} | SettingsPage / startup | 是 | 静态契约匹配 |
| UI-0655 | SettingsPage / 启动时进入托盘 | CheckBox StartMinimizedBox {"Checked": "OnSettingChanged", "Unchecked": "OnSettingChanged"} | SettingsPage / startup | 是 | 静态契约匹配 |
| UI-0656 | SettingsPage / 开机自动启动 | CheckBox StartupBox {"Checked": "OnSettingChanged", "Unchecked": "OnSettingChanged"} | SettingsPage / startup | 是 | 静态契约匹配 |
| UI-0657 | SettingsPage / 深色模式 | CheckBox DarkModeBox {"Checked": "OnThemeChanged", "Unchecked": "OnThemeChanged"} | SettingsPage / general | 是 | 静态契约匹配 |
| UI-0658 | SettingsPage / 自动检测 | Button  {"Click": "OnAutoDetect"} | SettingsPage / general | 是 | 静态契约匹配 |
| UI-0659 | SettingsPage / 更改 | Button  {"Click": "OnChooseExe"} | SettingsPage / general | 是 | 静态契约匹配 |
| UI-0660 | SettingsPage / 启用网络代理 | CheckBox EnableProxyBox {"Checked": "OnProxyInputChanged", "Unchecked": "OnProxyInputChanged"} | SettingsPage / network | 是 | 静态契约匹配 |
| UI-0661 | SettingsPage /  | TextBox ProxyUrlBox {"TextChanged": "OnProxyInputChanged"} | SettingsPage / network | 是 | 静态契约匹配 |
| UI-0662 | SettingsPage / 代理连接失败时允许直连 | CheckBox FallbackDirectBox {"Checked": "OnProxyInputChanged", "Unchecked": "OnProxyInputChanged"} | SettingsPage / network | 是 | 静态契约匹配 |
| UI-0663 | SettingsPage / 保存代理设置 | Button  {"Click": "OnSaveProxy"} | SettingsPage / network | 是 | 静态契约匹配 |
| UI-0664 | SettingsPage / 测试连接 | Button NetworkTestButton {"Click": "OnTestNetwork"} | SettingsPage / network | 是 | 静态契约匹配 |
| UI-0665 | SettingsPage / 复制诊断结果 | Button CopyDiagnosticButton {"Click": "OnCopyDiagnostic"} | SettingsPage / network | 是 | 静态契约匹配 |
| UI-0666 | SettingsPage / 有新公告时显示小蓝点 | CheckBox AnnouncementBadgeBox {"Checked": "OnAnnouncementBadgeChanged", "Unchecked": "OnAnnouncementBadgeChanged"} | SettingsPage / notifications | 是 | 静态契约匹配 |
| UI-0667 | SettingsPage / 启用 Windows 通知 | CheckBox EnableWindowsNotificationsBox {"Checked": "OnNotificationSettingChanged", "Unchecked": "OnNotificationSettingChanged"} | SettingsPage / notifications | 是 | 静态契约匹配 |
| UI-0668 | SettingsPage / 区服切换 | CheckBox NotifyRegionSwitchBox {"Checked": "OnNotificationSettingChanged", "Unchecked": "OnNotificationSettingChanged"} | SettingsPage / notifications | 是 | 静态契约匹配 |
| UI-0669 | SettingsPage / Drops | CheckBox NotifyDropsBox {"Checked": "OnNotificationSettingChanged", "Unchecked": "OnNotificationSettingChanged"} | SettingsPage / notifications | 是 | 静态契约匹配 |
| UI-0670 | SettingsPage / 软件更新 | CheckBox NotifyUpdatesBox {"Checked": "OnNotificationSettingChanged", "Unchecked": "OnNotificationSettingChanged"} | SettingsPage / notifications | 是 | 静态契约匹配 |
| UI-0671 | SettingsPage / 公告 | CheckBox NotifyAnnouncementsBox {"Checked": "OnNotificationSettingChanged", "Unchecked": "OnNotificationSettingChanged"} | SettingsPage / notifications | 是 | 静态契约匹配 |
| UI-0672 | SettingsPage / 检查更新 | Button CheckUpdateButton {"Click": "OnCheckUpdate"} | SettingsPage / updates | 是 | 静态契约匹配 |
| UI-0673 | SettingsPage / UpdateAvailable / 打开更新链接 | Button OpenUpdateLinkButton {"Click": "OnOpenUpdateLink"} | SettingsPage / updates / UpdateAvailable | 是 | 静态契约匹配 |
| UI-0674 | SettingsPage / UpdateAvailable / 在线更新 | Button OnlineUpdateButton {"Click": "OnOnlineUpdate"} | SettingsPage / updates / UpdateAvailable | 是 | 静态契约匹配 |
| UI-0675 | SettingsPage / SkippedUpdate / 恢复此版本提示 | Button  {"Click": "OnRestoreSkippedUpdate"} | SettingsPage / updates / SkippedUpdate | 是 | 静态契约匹配 |
| UI-0676 | SettingsPage / 打开数据文件夹 | Button  {"Click": "OnOpenData"} | SettingsPage / data | 是 | 静态契约匹配 |
| UI-0677 | SettingsPage / 清理战绩缓存 | Button  {"Click": "OnClearCache"} | SettingsPage / data | 是 | 静态契约匹配 |
| UI-0678 | Views/Pages/SettingsPage.xaml.cs:168 / MessageBox.Show(error, "网络代理", MessageBoxButton.OK, MessageBoxImage.Warning); | confirmation/command  {} | Views/Pages/SettingsPage.xaml.cs:184 | 是 | 静态契约匹配 |
| UI-0679 | Views/Pages/SettingsPage.xaml.cs:264 / MessageBox.Show($"当前版本：{_vm.UpdateChecks.CurrentVersion}", "已是最新版本", | confirmation/command  {} | Views/Pages/SettingsPage.xaml.cs:280 | 是 | 静态契约匹配 |
| UI-0680 | Views/Pages/SettingsPage.xaml.cs:267 / MessageBox.Show("当前没有可用的正式版本更新。", "软件更新", | confirmation/command  {} | Views/Pages/SettingsPage.xaml.cs:283 | 是 | 静态契约匹配 |
| UI-0681 | Views/Pages/SettingsPage.xaml.cs:270 / MessageBox.Show(outcome.Result?.ErrorMessage ?? "暂时无法连接更新服务器。", "暂时无法检查更新", | confirmation/command  {} | Views/Pages/SettingsPage.xaml.cs:286 | 是 | 静态契约匹配 |
| UI-0682 | Views/Pages/SettingsPage.xaml.cs:335 / MessageBox.Show(ex.Message, "在线更新失败", MessageBoxButton.OK, MessageBoxImage.Warning); | confirmation/command  {} | Views/Pages/SettingsPage.xaml.cs:351 | 是 | 静态契约匹配 |
| UI-0683 | SnapshotsPage /  | DataTrigger  {"Binding": "{Binding State}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0684 | SnapshotsPage /  | DataTrigger  {"Binding": "{Binding State}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0685 | SnapshotsPage /  | DataTrigger  {"Binding": "{Binding State}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0686 | SnapshotsPage /  | DataTrigger  {"Binding": "{Binding State}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0687 | SnapshotsPage /  | DataTrigger  {"Binding": "{Binding State}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0688 | SnapshotsPage / {Binding ModeText} | TextBlock  {"Text": "{Binding ModeText}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0689 | SnapshotsPage /  | Border  {"Visibility": "{Binding ActiveVisibility}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0690 | SnapshotsPage / {Binding SourceText, Mode=OneWay} | Run  {"Text": "{Binding SourceText, Mode=OneWay}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0691 | SnapshotsPage / {Binding TargetText, Mode=OneWay} | Run  {"Text": "{Binding TargetText, Mode=OneWay}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0692 | SnapshotsPage / {Binding StateText} | TextBlock  {"Text": "{Binding StateText}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0693 | SnapshotsPage / {Binding StateReason} | TextBlock  {"Text": "{Binding StateReason}", "Visibility": "{Binding StateReasonVisibility}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0694 | SnapshotsPage / {Binding FileCountText} | TextBlock  {"Text": "{Binding FileCountText}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0695 | SnapshotsPage / {Binding SizeText} | TextBlock  {"Text": "{Binding SizeText}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0696 | SnapshotsPage / {Binding CreatedText} | TextBlock  {"Text": "{Binding CreatedText}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0697 | SnapshotsPage / {Binding UsedText} | TextBlock  {"Text": "{Binding UsedText}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0698 | SnapshotsPage / 查看详情 | Button  {"Click": "OnSnapshotAction"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0699 | SnapshotsPage / 重新生成 | Button  {"Click": "OnSnapshotAction"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0700 | SnapshotsPage / 打开目录 | Button  {"Click": "OnSnapshotAction"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0701 | SnapshotsPage / 删除 | Button  {"Click": "OnSnapshotAction"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0702 | SnapshotsPage / 区服快照 / 刷新 | Button  {"Click": "OnRefresh"} | SnapshotsPage / 区服快照 | 是 | 静态契约匹配 |
| UI-0703 | SnapshotsPage / {Binding SnapshotCount, StringFormat=共 {0} 个快照} | TextBlock  {"Text": "{Binding SnapshotCount, StringFormat=共 {0} 个快照}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0704 | SnapshotsPage / {Binding StatusText} | TextBlock  {"Text": "{Binding StatusText}"} | OverviewPage | 是 | 静态契约匹配 |
| UI-0705 | SnapshotsPage / SnapshotList / {Binding SnapshotCount, StringFormat=共 {0} 个} | TextBlock  {"Text": "{Binding SnapshotCount, StringFormat=共 {0} 个}"} | SnapshotsPage / SnapshotList | 是 | 静态契约匹配 |
| UI-0706 | SnapshotsPage / SnapshotList /  | ListBox SnapshotsList {"ItemsSource": "{Binding Items}", "SelectedItem": "{Binding SelectedSnapshot, Mode=TwoWay}"} | SnapshotsPage / SnapshotList | 是 | 静态契约匹配 |
| UI-0707 | SnapshotsPage / SnapshotList /  | DataTrigger  {"Binding": "{Binding Items.Count}"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0708 | SnapshotsPage / SnapshotDetails /  | Border SnapshotDetailsPanel {"Visibility": "{Binding HasSelectedSnapshot, Converter={StaticResource BoolVisibility}}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0709 | SnapshotsPage / SnapshotDetails /  | StackPanel  {"DataContext": "{Binding SelectedSnapshot}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0710 | SnapshotsPage / SnapshotDetails / 关闭 | Button  {"Click": "OnCloseDetails"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0711 | SnapshotsPage / SnapshotDetails / {Binding ModeText} | TextBlock  {"Text": "{Binding ModeText}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0712 | SnapshotsPage / SnapshotDetails / {Binding StateText} | TextBlock  {"Text": "{Binding StateText}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0713 | SnapshotsPage / SnapshotDetails / {Binding SourceText, Mode=OneWay} | Run  {"Text": "{Binding SourceText, Mode=OneWay}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0714 | SnapshotsPage / SnapshotDetails / {Binding TargetText, Mode=OneWay} | Run  {"Text": "{Binding TargetText, Mode=OneWay}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0715 | SnapshotsPage / SnapshotDetails / {Binding GenerationId} | TextBlock  {"Text": "{Binding GenerationId}", "ToolTip": "{Binding GenerationId}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0716 | SnapshotsPage / SnapshotDetails / {Binding StateText} | TextBlock  {"Text": "{Binding StateText}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0717 | SnapshotsPage / SnapshotDetails / {Binding StateReason} | TextBlock  {"Text": "{Binding StateReason}", "Visibility": "{Binding StateReasonVisibility}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0718 | SnapshotsPage / SnapshotDetails / {Binding FileCountText} | TextBlock  {"Text": "{Binding FileCountText}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0719 | SnapshotsPage / SnapshotDetails / {Binding SizeText} | TextBlock  {"Text": "{Binding SizeText}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0720 | SnapshotsPage / SnapshotDetails / {Binding RootPath} | TextBlock  {"Text": "{Binding RootPath}", "ToolTip": "{Binding RootPath}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0721 | SnapshotsPage / SnapshotDetails / {Binding CreatedText} | TextBlock  {"Text": "{Binding CreatedText}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0722 | SnapshotsPage / SnapshotDetails / {Binding UsedText} | TextBlock  {"Text": "{Binding UsedText}"} | SnapshotsPage / SnapshotDetails | 是 | 静态契约匹配 |
| UI-0723 | SnapshotsPage / SnapshotDetailsEmpty /  | DataTrigger  {"Binding": "{Binding HasSelectedSnapshot}"} | SnapshotsPage / SnapshotDetailsEmpty | 是 | 静态契约匹配 |
| UI-0724 | SnapshotsPage /  | DataTrigger  {"Binding": "{Binding Items.Count}"} | SnapshotsPage / SnapshotList | 是 | 静态契约匹配 |
| UI-0725 | SnapshotsPage / 前往区服切换 | Button  {"Click": "OnOpenRegion"} | SnapshotsPage | 是 | 静态契约匹配 |
| UI-0726 | Views/Pages/SnapshotsPage.xaml.cs:30 / if (MessageBox.Show("将返回区服准备流程，重新生成快照不会直接删除游戏文件。是否继续？", "重新生成快照", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) | confirmation/command  {} | Views/Pages/SnapshotsPage.xaml.cs:30 | 是 | 静态契约匹配 |
| UI-0727 | Views/Pages/SnapshotsPage.xaml.cs:34 / if (MessageBox.Show($"确定删除此 CloudLight Blizzard 快照？\n\n模式：{item.ModeText}\n文件：{item.FileCountText}\n大小：{item.SizeText}\n\n只会删除受管理的快照路径，不会删除游戏文件。", "删除快照", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) | confirmation/command  {} | Views/Pages/SnapshotsPage.xaml.cs:34 | 是 | 静态契约匹配 |
| UI-0728 | PlatformErrorWindow / 服务运行失败 / 查看日志 | Button  {"Click": "OnViewLogs"} | PlatformErrorWindow / 服务运行失败 | 是 | 静态契约匹配 |
| UI-0729 | PlatformErrorWindow / 服务运行失败 / 关闭 | Button  {} | PlatformErrorWindow / 服务运行失败 | 是 | 静态契约匹配 |
| UI-0730 | RegionActionConfirmWindow / 区服文件准备 / × | Button  {"Click": "OnCancel"} | RegionActionConfirmWindow / 区服文件准备 | 是 | 静态契约匹配 |
| UI-0731 | RegionActionConfirmWindow / 区服文件准备 / 取消 | Button  {"Click": "OnCancel"} | RegionActionConfirmWindow / 区服文件准备 | 是 | 静态契约匹配 |
| UI-0732 | RegionActionConfirmWindow / 区服文件准备 /  | Button ConfirmButton {"Click": "OnConfirm"} | RegionActionConfirmWindow / 区服文件准备 | 是 | 静态契约匹配 |
| UI-0733 | RegionSwitchChoiceWindow / 区服文件尚未准备好 / 取消 | Button  {"Click": "OnCancel"} | RegionSwitchChoiceWindow / 区服文件尚未准备好 | 是 | 静态契约匹配 |
| UI-0734 | RegionSwitchChoiceWindow / 区服文件尚未准备好 / 仅切换账号 | Button  {"Click": "OnAccountOnly"} | RegionSwitchChoiceWindow / 区服文件尚未准备好 | 是 | 静态契约匹配 |
| UI-0735 | RegionSwitchChoiceWindow / 区服文件尚未准备好 / 前往区服文件 | Button  {"Click": "OnSettings"} | RegionSwitchChoiceWindow / 区服文件尚未准备好 | 是 | 静态契约匹配 |
| UI-0736 | Views/RegionSwitchChoiceWindow.xaml.cs:24 / public RegionSwitchChoice ShowDialogChoice() { ShowDialog(); return _choice; } | confirmation/command  {} | Views/RegionSwitchChoiceWindow.xaml.cs:24 | 是 | 静态契约匹配 |
| UI-0737 | SnapshotConfirmWindow / 更新账号备份 /  | Button  {"Click": "OnCancel"} | SnapshotConfirmWindow / 更新账号备份 | 是 | 静态契约匹配 |
| UI-0738 | SnapshotConfirmWindow / 更新账号备份 /  | CheckBox ConfirmedLoginBox {} | SnapshotConfirmWindow / 更新账号备份 | 是 | 静态契约匹配 |
| UI-0739 | SnapshotConfirmWindow / 更新账号备份 / 取消 | Button  {"Click": "OnCancel"} | SnapshotConfirmWindow / 更新账号备份 | 是 | 静态契约匹配 |
| UI-0740 | SnapshotConfirmWindow / 更新账号备份 /  | Button  {"Click": "OnConfirm"} | SnapshotConfirmWindow / 更新账号备份 | 是 | 静态契约匹配 |
| UI-0741 | SoopNoticeWindow / SOOP 提示 / 取消 | Button  {"Click": "OnCancel"} | SoopNoticeWindow / SOOP 提示 | 是 | 静态契约匹配 |
| UI-0742 | SoopNoticeWindow / SOOP 提示 /  | Button ConfirmButton {"Click": "OnConfirm"} | SoopNoticeWindow / SOOP 提示 | 是 | 静态契约匹配 |
| UI-0743 | SwitchPreviewWindow / 切换前预览 / × | Button  {"Click": "OnCancel"} | SwitchPreviewWindow / 切换前预览 | 是 | 静态契约匹配 |
| UI-0744 | SwitchPreviewWindow / 切换前预览 / 取消 | Button  {"Click": "OnCancel"} | SwitchPreviewWindow / 切换前预览 | 是 | 静态契约匹配 |
| UI-0745 | SwitchPreviewWindow / 切换前预览 / 开始切换 | Button StartButton {"Click": "OnStart"} | SwitchPreviewWindow / 切换前预览 | 是 | 静态契约匹配 |
| UI-0746 | UpdateDialog / 发现新版本 / × | Button CloseButton {"Click": "OnLater"} | UpdateDialog / 发现新版本 | 是 | 静态契约匹配 |
| UI-0747 | UpdateDialog / 发现新版本 / 不再提示此版本 | CheckBox SkipVersionBox {} | UpdateDialog / 发现新版本 | 是 | 静态契约匹配 |
| UI-0748 | UpdateDialog / 发现新版本 / 取消 | Button CancelDownloadButton {"Click": "OnCancelDownload"} | UpdateDialog / 发现新版本 | 是 | 静态契约匹配 |
| UI-0749 | UpdateDialog / 发现新版本 / 稍后提醒 | Button LaterButton {"Click": "OnLater"} | UpdateDialog / 发现新版本 | 是 | 静态契约匹配 |
| UI-0750 | UpdateDialog / 发现新版本 / 打开更新链接 | Button OpenReleaseButton {"Click": "OnOpenRelease"} | UpdateDialog / 发现新版本 | 是 | 静态契约匹配 |
| UI-0751 | UpdateDialog / 发现新版本 / 在线更新 | Button OnlineUpdateButton {"Click": "OnOnlineUpdate"} | UpdateDialog / 发现新版本 | 是 | 静态契约匹配 |
| UI-0752 | Views/UpdateDialog.xaml.cs:103 / MessageBox.Show(ex.Message, "在线更新失败", MessageBoxButton.OK, MessageBoxImage.Warning); | confirmation/command  {} | Views/UpdateDialog.xaml.cs:103 | 是 | 静态契约匹配 |
| UI-0753 | UploadStalledWindow / 上传无响应 / 继续等待 | Button  {"Click": "OnWait"} | UploadStalledWindow / 上传无响应 | 是 | 静态契约匹配 |
| UI-0754 | UploadStalledWindow / 上传无响应 / 取消上传 | Button  {"Click": "OnCancel"} | UploadStalledWindow / 上传无响应 | 是 | 静态契约匹配 |
