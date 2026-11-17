# 第二代 UI 功能迁移矩阵

基线为上一轮最终 763 条 UI 契约（早期 754）；不是 763 个独立业务功能。每行保留原事件、命令、输入或状态绑定来源。共享视觉投影和组件作用域变化单独注明。静态映射不等于真实账号操作已执行。

静态映射 763 / 763；待核对 0。运行证据见 regression-results.md；真实登录、Claim、游戏覆盖按用户要求不执行。

| ID | 原页面 / 控件 | 原事件或绑定 | 新入口 / 证据 | 验证层级 |
|---|---|---|---|---|
| UI-0001 | Controls/AnnouncementBell.xaml / Button | {"Click": "OnOpenAnnouncements"} | 原契约保留 · Controls/AnnouncementBell.xaml | 静态；运行验证按测试记录 |
| UI-0002 | Controls/AnnouncementBell.xaml / DataTrigger | {"Binding": "{Binding AnnouncementState.IsBadgeVisible,                                 RelativeSource={RelativeSource AncestorType={x:Type app:MainWindow}}}"} | 原契约保留 · Controls/AnnouncementBell.xaml | 静态；运行验证按测试记录 |
| UI-0003 | Controls/PageHeader.xaml / {Binding Title, RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}} | {"Text": "{Binding Title, RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}}"} | 同一绑定 / Command（共享模板或控件更名） · Controls/PageHeader.xaml | 静态；运行验证按测试记录 |
| UI-0004 | Controls/PageHeader.xaml / {Binding Description, RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}} | {"Text": "{Binding Description, RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}}"} | 同一绑定 / Command（共享模板或控件更名） · Controls/PageHeader.xaml | 静态；运行验证按测试记录 |
| UI-0005 | Controls/PageHeader.xaml / {Binding HeaderActions,                               RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}} | {"Content": "{Binding HeaderActions,                               RelativeSource={RelativeSource AncestorType={x:Type ctrl:PageHeader}}}"} | 原契约保留 · Controls/PageHeader.xaml | 静态；运行验证按测试记录 |
| UI-0006 | Stats/CareerWindow.xaml / HeroRow | {"MouseLeftButtonUp": "OnHeroClick"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0007 | Stats/CareerWindow.xaml / ImageBrush | {"ImageSource": "{Binding IconLocal, Converter={StaticResource PathToImg}}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0008 | Stats/CareerWindow.xaml / HeroName | {"Text": "{Binding Name}", "ToolTip": "{Binding Name}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0009 | Stats/CareerWindow.xaml / {Binding HoursText} | {"Text": "{Binding HoursText}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0010 | Stats/CareerWindow.xaml / {Binding WinRateText} | {"Text": "{Binding WinRateText}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0011 | Stats/CareerWindow.xaml / {Binding MatchsText} | {"Text": "{Binding MatchsText}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0012 | Stats/CareerWindow.xaml / {Binding PlayPercentText} | {"Text": "{Binding PlayPercentText}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0013 | Stats/CareerWindow.xaml / SearchBox | {"KeyDown": "OnSearchKeyDown"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0014 | Stats/CareerWindow.xaml / Condition | {"Binding": "{Binding Text, ElementName=SearchBox}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0015 | Stats/CareerWindow.xaml / Condition | {"Binding": "{Binding IsKeyboardFocused, ElementName=SearchBox}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0016 | Stats/CareerWindow.xaml / QueryBtn | {"Click": "OnQuery"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0017 | Stats/CareerWindow.xaml / RefreshBtn | {"Click": "OnRefresh"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0018 | Stats/CareerWindow.xaml / RbPc | {"Checked": "OnInputChanged"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0019 | Stats/CareerWindow.xaml / RbPad | {"Checked": "OnInputChanged"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0020 | Stats/CareerWindow.xaml / RbQuick | {"Checked": "OnModeChanged"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0021 | Stats/CareerWindow.xaml / RbComp | {"Checked": "OnModeChanged"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0022 | Stats/CareerWindow.xaml / {Binding Initial} | {"Text": "{Binding Initial}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0023 | Stats/CareerWindow.xaml / ImageBrush | {"ImageSource": "{Binding AvatarLocal, Converter={StaticResource PathToImg}}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0024 | Stats/CareerWindow.xaml / {Binding DisplayName, Mode=OneWay} | {"Text": "{Binding DisplayName, Mode=OneWay}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0025 | Stats/CareerWindow.xaml / {Binding TagSuffix, Mode=OneWay} | {"Text": "{Binding TagSuffix, Mode=OneWay}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0026 | Stats/CareerWindow.xaml / {Binding EndorseLevel, Mode=OneWay} | {"Text": "{Binding EndorseLevel, Mode=OneWay}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0027 | Stats/CareerWindow.xaml / {Binding TotalHours} | {"Text": "{Binding TotalHours}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0028 | Stats/CareerWindow.xaml / {Binding UpdatedAt, Mode=OneWay} | {"Text": "{Binding UpdatedAt, Mode=OneWay}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0029 | Stats/CareerWindow.xaml / StackPanel | {"Visibility": "{Binding ShowRanks, Converter={StaticResource BoolToVis}}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0030 | Stats/CareerWindow.xaml / {Binding RankSectionTitle} | {"Text": "{Binding RankSectionTitle}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0031 | Stats/CareerWindow.xaml / Image | {"Source": "{Binding Tank.TierIconLocal, Converter={StaticResource PathToImg}}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0032 | Stats/CareerWindow.xaml / {Binding Tank.TierText} | {"Text": "{Binding Tank.TierText}", "Foreground": "{Binding Tank.TierBrush}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0033 | Stats/CareerWindow.xaml / Image | {"Source": "{Binding Dps.TierIconLocal, Converter={StaticResource PathToImg}}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0034 | Stats/CareerWindow.xaml / {Binding Dps.TierText} | {"Text": "{Binding Dps.TierText}", "Foreground": "{Binding Dps.TierBrush}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0035 | Stats/CareerWindow.xaml / Image | {"Source": "{Binding Support.TierIconLocal, Converter={StaticResource PathToImg}}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0036 | Stats/CareerWindow.xaml / {Binding Support.TierText} | {"Text": "{Binding Support.TierText}", "Foreground": "{Binding Support.TierBrush}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0037 | Stats/CareerWindow.xaml / {Binding AvgSectionTitle} | {"Text": "{Binding AvgSectionTitle}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0038 | Stats/CareerWindow.xaml / {Binding AvgDamageLabel} | {"Text": "{Binding AvgDamageLabel}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0039 | Stats/CareerWindow.xaml / {Binding AvgDamage} | {"Text": "{Binding AvgDamage}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0040 | Stats/CareerWindow.xaml / {Binding AvgHealLabel} | {"Text": "{Binding AvgHealLabel}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0041 | Stats/CareerWindow.xaml / {Binding AvgHeal} | {"Text": "{Binding AvgHeal}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0042 | Stats/CareerWindow.xaml / {Binding KdaLabel} | {"Text": "{Binding KdaLabel}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0043 | Stats/CareerWindow.xaml / {Binding Kda} | {"Text": "{Binding Kda}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0044 | Stats/CareerWindow.xaml / {Binding WinRateLabel} | {"Text": "{Binding WinRateLabel}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0045 | Stats/CareerWindow.xaml / {Binding SeasonWinRate} | {"Text": "{Binding SeasonWinRate}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0046 | Stats/CareerWindow.xaml / {Binding WinLossText} | {"Text": "{Binding WinLossText}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0047 | Stats/CareerWindow.xaml / Border | {"Visibility": "{Binding HasAvgExtra, Converter={StaticResource BoolToVis}}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0048 | Stats/CareerWindow.xaml / {Binding AvgExtra} | {"Text": "{Binding AvgExtra}", "ToolTip": "{Binding AvgExtra}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0049 | Stats/CareerWindow.xaml / Expander | {} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0050 | Stats/CareerWindow.xaml / Button | {"Click": "OnHeroSort"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0051 | Stats/CareerWindow.xaml / Button | {"Click": "OnHeroSort"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0052 | Stats/CareerWindow.xaml / Button | {"Click": "OnHeroSort"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0053 | Stats/CareerWindow.xaml / Button | {"Click": "OnHeroSort"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0054 | Stats/CareerWindow.xaml / Button | {"Click": "OnHeroSort"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0055 | Stats/CareerWindow.xaml / HeroList | {} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0056 | Stats/CareerWindow.xaml / OverlayPrimary | {"Click": "OnOverlayPrimary"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0057 | Stats/CareerWindow.xaml / OverlaySecondary | {"Click": "OnOverlaySecondary"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0058 | Stats/HeroDetailWindow.xaml / {Binding Name} | {"Text": "{Binding Name}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0059 | Stats/HeroDetailWindow.xaml / {Binding Value} | {"Text": "{Binding Value}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0060 | Stats/HeroDetailWindow.xaml / ImageBrush | {"ImageSource": "{Binding IconLocal, Converter={StaticResource PathToImg}}"} | 原契约保留 · Stats/CareerWindow.xaml | 静态；运行验证按测试记录 |
| UI-0061 | Stats/HeroDetailWindow.xaml / {Binding Name} | {"Text": "{Binding Name}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0062 | Stats/HeroDetailWindow.xaml / {Binding LevelText} | {"Text": "{Binding LevelText}", "Visibility": "{Binding HasLevel, Converter={StaticResource BoolToVis}}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0063 | Stats/HeroDetailWindow.xaml / Border | {"Visibility": "{Binding HasRank, Converter={StaticResource BoolToVis}}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0064 | Stats/HeroDetailWindow.xaml / {Binding RankText, Mode=OneWay} | {"Text": "{Binding RankText, Mode=OneWay}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0065 | Stats/HeroDetailWindow.xaml / {Binding WinRateText, Mode=OneWay} | {"Text": "{Binding WinRateText, Mode=OneWay}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0066 | Stats/HeroDetailWindow.xaml / {Binding MatchsText, Mode=OneWay} | {"Text": "{Binding MatchsText, Mode=OneWay}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0067 | Stats/HeroDetailWindow.xaml / {Binding HoursText, Mode=OneWay} | {"Text": "{Binding HoursText, Mode=OneWay}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0068 | Stats/HeroDetailWindow.xaml / StackPanel | {"Visibility": "{Binding HasPerTen, Converter={StaticResource BoolToVis}}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0069 | Stats/HeroDetailWindow.xaml / RbAve | {"Content": "{Binding TotalTabText, Mode=OneWay}", "Checked": "OnStatModeChanged"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0070 | Stats/HeroDetailWindow.xaml / RbTen | {"Checked": "OnStatModeChanged"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0071 | Stats/HeroDetailWindow.xaml / StatList | {"ItemsSource": "{Binding DetailStats}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0072 | Views/AccountSettingsWindow.xaml / CustomNameBox | {} | 原契约保留 · Views/AccountSettingsWindow.xaml | 静态；运行验证按测试记录 |
| UI-0073 | Views/AccountSettingsWindow.xaml / RemarkBox | {} | 原契约保留 · Views/AccountSettingsWindow.xaml | 静态；运行验证按测试记录 |
| UI-0074 | Views/AccountSettingsWindow.xaml / RegionBox | {} | 原契约保留 · Views/AccountSettingsWindow.xaml | 静态；运行验证按测试记录 |
| UI-0075 | Views/AccountSettingsWindow.xaml / 取消 | {} | 原契约保留 · Views/AccountSettingsWindow.xaml | 静态；运行验证按测试记录 |
| UI-0076 | Views/AccountSettingsWindow.xaml / 保存 | {"Click": "OnSave"} | 原契约保留 · Views/AccountSettingsWindow.xaml | 静态；运行验证按测试记录 |
| UI-0077 | Views/AnnouncementWindow.xaml / AnnouncementList | {"SelectionChanged": "OnAnnouncementSelected"} | 原契约保留 · Views/AnnouncementWindow.xaml | 静态；运行验证按测试记录 |
| UI-0078 | Views/AnnouncementWindow.xaml / Ellipse | {"Visibility": "{Binding UnreadVisibility}"} | 原契约保留 · Views/AnnouncementWindow.xaml | 静态；运行验证按测试记录 |
| UI-0079 | Views/AnnouncementWindow.xaml / {Binding Announcement.Title} | {"Text": "{Binding Announcement.Title}"} | 原契约保留 · Views/AnnouncementWindow.xaml | 静态；运行验证按测试记录 |
| UI-0080 | Views/AnnouncementWindow.xaml / {Binding PublishedText} | {"Text": "{Binding PublishedText}"} | 原契约保留 · Views/AnnouncementWindow.xaml | 静态；运行验证按测试记录 |
| UI-0081 | Views/AnnouncementWindow.xaml / DetailContent | {} | 原契约保留 · Views/AnnouncementWindow.xaml | 静态；运行验证按测试记录 |
| UI-0082 | Views/AnnouncementWindow.xaml / 关闭 | {"Click": "OnClose"} | 原契约保留 · Views/AnnouncementWindow.xaml | 静态；运行验证按测试记录 |
| UI-0083 | Views/CloseChoiceWindow.xaml / Grid | {"MouseLeftButtonDown": "OnDrag"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0084 | Views/CloseChoiceWindow.xaml / Button | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0085 | Views/CloseChoiceWindow.xaml / OptTray | {} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0086 | Views/CloseChoiceWindow.xaml / OptExit | {} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0087 | Views/CloseChoiceWindow.xaml / Remember | {} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0088 | Views/CloseChoiceWindow.xaml / 确定 | {"Click": "OnConfirm"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0089 | Views/DeleteConfirmWindow.xaml / Grid | {"MouseLeftButtonDown": "OnDrag"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0090 | Views/DeleteConfirmWindow.xaml / Button | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0091 | Views/DeleteConfirmWindow.xaml / 取消 | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0092 | Views/DeleteConfirmWindow.xaml / Button | {"Click": "OnConfirm"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0093 | Views/ExitConfirmationDialog.xaml / Grid | {"MouseLeftButtonDown": "OnDrag"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0094 | Views/ExitConfirmationDialog.xaml / × | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0095 | Views/ExitConfirmationDialog.xaml / 取消 | {} | 原契约保留 · Views/AccountSettingsWindow.xaml | 静态；运行验证按测试记录 |
| UI-0096 | Views/ExitConfirmationDialog.xaml / 最小化到托盘 | {"Click": "OnMinimizeToTray"} | 原契约保留 · Views/ExitConfirmationDialog.xaml | 静态；运行验证按测试记录 |
| UI-0097 | Views/ExitConfirmationDialog.xaml / 退出 | {"Click": "OnExit"} | 原契约保留 · Views/ExitConfirmationDialog.xaml | 静态；运行验证按测试记录 |
| UI-0098 | Views/LoginNewWindow.xaml / Grid | {"MouseLeftButtonDown": "OnDrag"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0099 | Views/LoginNewWindow.xaml / Button | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0100 | Views/LoginNewWindow.xaml / 取消 | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0101 | Views/LoginNewWindow.xaml / 继续 | {"Click": "OnConfirm"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0102 | Views/MainWindow.xaml / AccountsNav | {"Checked": "OnNavigate"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0103 | Views/MainWindow.xaml / RegionNav | {"Checked": "OnNavigate"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0104 | Views/MainWindow.xaml / StatsNav | {"Checked": "OnNavigate"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0105 | Views/MainWindow.xaml / DropsNav | {"Checked": "OnNavigate"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0106 | Views/MainWindow.xaml / SnapshotsNav | {"Checked": "OnNavigate"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0107 | Views/MainWindow.xaml / DiagnosticsNav | {"Checked": "OnNavigate"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0108 | Views/MainWindow.xaml / SettingsNav | {"Checked": "OnNavigate"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0109 | Views/MainWindow.xaml / AboutNav | {"Checked": "OnNavigate"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0110 | Views/MainWindow.xaml / {Binding AppVersion} | {"Text": "{Binding AppVersion}"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0111 | Views/MainWindow.xaml / 刷新账号 | {"Click": "OnRefresh"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0112 | Views/MainWindow.xaml / {Binding StatusText} | {"Text": "{Binding StatusText}"} | 原契约保留 · Views/Dashboard/DashboardView.xaml | 静态；运行验证按测试记录 |
| UI-0113 | Views/MainWindow.xaml.cs / dialog.ShowDialog(); | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0114 | Views/MainWindow.xaml.cs / dialog.ShowDialog(); | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0115 | Views/MainWindow.xaml.cs / MessageBox.Show($"当前版本：{_vm.UpdateChecks.CurrentVersion}", "已是最新版本", | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0116 | Views/MainWindow.xaml.cs / MessageBox.Show("当前没有可用的正式更新。", "软件更新", | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0117 | Views/MainWindow.xaml.cs / MessageBox.Show(outcome.Result?.ErrorMessage ?? "暂时无法连接更新服务器。", "暂时无法检查更新", | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0118 | Views/MainWindow.xaml.cs / MessageBox.Show("无法打开系统浏览器，请稍后重试。", "打开更新链接", | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0119 | Views/MainWindow.xaml.cs / MessageBox.Show("安装包不存在或设置无法保存，已取消安装。", "在线更新"); | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0120 | Views/MainWindow.xaml.cs / else MessageBox.Show(message, "在线更新", MessageBoxButton.OK, MessageBoxImage.Warning); | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0121 | Views/MainWindow.xaml.cs / dialog.ShowDialog(); | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0122 | Views/Pages/AboutPage.xaml / {Binding AppVersion} | {"Text": "{Binding AppVersion}"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0123 | Views/Pages/AboutPage.xaml / ThirdPartyComponentsList | {"ItemsSource": "{Binding Components, ElementName=AboutRoot}"} | 原契约保留 · Views/Pages/AboutPage.xaml | 静态；运行验证按测试记录 |
| UI-0124 | Views/Pages/AboutPage.xaml / {Binding License} | {"Text": "{Binding License}"} | 原契约保留 · Views/Pages/AboutPage.xaml | 静态；运行验证按测试记录 |
| UI-0125 | Views/Pages/AboutPage.xaml / {Binding Name} | {"Text": "{Binding Name}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0126 | Views/Pages/AboutPage.xaml / {Binding Purpose} | {"Text": "{Binding Purpose}"} | 原契约保留 · Views/Pages/AboutPage.xaml | 静态；运行验证按测试记录 |
| UI-0127 | Views/Pages/AboutPage.xaml / {Binding SourceUrl} | {"Text": "{Binding SourceUrl}", "ToolTip": "{Binding SourceUrl}"} | 原契约保留 · Views/Pages/AboutPage.xaml | 静态；运行验证按测试记录 |
| UI-0128 | Views/Pages/AboutPage.xaml / {Binding VersionInfo} | {"Text": "{Binding VersionInfo}"} | 原契约保留 · Views/Pages/AboutPage.xaml | 静态；运行验证按测试记录 |
| UI-0129 | Views/Pages/AboutPage.xaml / {Binding DistributionInfo} | {"Text": "{Binding DistributionInfo}"} | 原契约保留 · Views/Pages/AboutPage.xaml | 静态；运行验证按测试记录 |
| UI-0130 | Views/Pages/AboutPage.xaml / {Binding LicenseLocation} | {"Text": "{Binding LicenseLocation}"} | 原契约保留 · Views/Pages/AboutPage.xaml | 静态；运行验证按测试记录 |
| UI-0131 | Views/Pages/AboutPage.xaml / {Binding NoticeLocation} | {"Text": "{Binding NoticeLocation}"} | 原契约保留 · Views/Pages/AboutPage.xaml | 静态；运行验证按测试记录 |
| UI-0132 | Views/Pages/AboutPage.xaml / 打开上游项目 | {"Tag": "{Binding SourceOpenUrl}", "Visibility": "{Binding HasSourceOpenUrl, Converter={StaticResource BoolVisibility}}", "Click": "OnOpenSource"} | 原契约保留 · Views/Pages/AboutPage.xaml | 静态；运行验证按测试记录 |
| UI-0133 | Views/Pages/AboutPage.xaml / 打开许可证 | {"Tag": "{Binding LicenseOpenPath}", "Visibility": "{Binding HasLicenseOpenPath, Converter={StaticResource BoolVisibility}}", "Click": "OnOpenLocalPath"} | 原契约保留 · Views/Pages/AboutPage.xaml | 静态；运行验证按测试记录 |
| UI-0134 | Views/Pages/AboutPage.xaml / 打开第三方说明 | {"Tag": "{Binding NoticeOpenPath}", "Visibility": "{Binding HasNoticeOpenPath, Converter={StaticResource BoolVisibility}}", "Click": "OnOpenLocalPath"} | 原契约保留 · Views/Pages/AboutPage.xaml | 静态；运行验证按测试记录 |
| UI-0135 | Views/Pages/AboutPage.xaml.cs / MessageBox.Show($"当前发布目录中未找到：{relativePath}", "打开第三方文件", | {} | 原契约保留 · Views/Pages/AboutPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0136 | Views/Pages/AccountsPage.xaml / StackPanel | {"Visibility": "{Binding HasCurrentVisibility}"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0137 | Views/Pages/AccountsPage.xaml / {Binding CurrentAccount.DisplayName} | {"Text": "{Binding CurrentAccount.DisplayName}"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0138 | Views/Pages/AccountsPage.xaml / {Binding CurrentAccount.BattleTag} | {"Text": "{Binding CurrentAccount.BattleTag}"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0139 | Views/Pages/AccountsPage.xaml / {Binding CurrentAccount.RegionText} | {"Text": "{Binding CurrentAccount.RegionText}"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0140 | Views/Pages/AccountsPage.xaml / 当前未检测到登录账号。 | {"Visibility": "{Binding NoCurrentVisibility}"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0141 | Views/Pages/AccountsPage.xaml / 打开 Battle.net | {"Click": "OnLaunchClient"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0142 | Views/Pages/AccountsPage.xaml / 更新账号备份 | {"Click": "OnSaveCurrent"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0143 | Views/Pages/AccountsPage.xaml / 登录新账号 | {"Click": "OnAddAccount"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0144 | Views/Pages/AccountsPage.xaml / AccountSnapshotPathText | {"Text": "{Binding AccountSnapshotPath}"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0145 | Views/Pages/AccountsPage.xaml / {Binding AccountMigrationWarning} | {"Text": "{Binding AccountMigrationWarning}"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0146 | Views/Pages/AccountsPage.xaml / 打开账号快照目录 | {"Click": "OnOpenSnapshots"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0147 | Views/Pages/AccountsPage.xaml / {Binding ReadyCountText} | {"Text": "{Binding ReadyCountText}"} | 原契约保留 · Views/Dashboard/DashboardView.xaml | 静态；运行验证按测试记录 |
| UI-0148 | Views/Pages/AccountsPage.xaml / ItemsControl | {"ItemsSource": "{Binding SavedAccounts}"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0149 | Views/Pages/AccountsPage.xaml / {Binding RegionText} | {"Text": "{Binding RegionText}"} | 原契约保留 · Views/Accounts/AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0150 | Views/Pages/AccountsPage.xaml / {Binding DisplayName} | {"Text": "{Binding DisplayName}"} | 原契约保留 · Views/Accounts/AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0151 | Views/Pages/AccountsPage.xaml / {Binding BattleTag} | {"Text": "{Binding BattleTag}"} | 原契约保留 · Views/Accounts/AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0152 | Views/Pages/AccountsPage.xaml / Border | {"Visibility": "{Binding CurrentVisibility}"} | 原契约保留 · Views/Accounts/AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0153 | Views/Pages/AccountsPage.xaml / {Binding Remark} | {"Text": "{Binding Remark}", "Visibility": "{Binding RemarkVisibility}"} | 原契约保留 · Views/Accounts/AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0154 | Views/Pages/AccountsPage.xaml / {Binding ProfileText} | {"Text": "{Binding ProfileText}", "ToolTip": "{Binding ProfileDetail}"} | 原契约保留 · Views/Accounts/AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0155 | Views/Pages/AccountsPage.xaml / 需要重新登录 Battle.net | {"Visibility": "{Binding ExpiredVisibility}"} | 原契约保留 · Views/Accounts/AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0156 | Views/Pages/AccountsPage.xaml / {Binding SwitchText} | {"Content": "{Binding SwitchText}", "IsEnabled": "{Binding CanSwitch}", "Click": "OnSwitchCard"} | 原事件或 owner 路由 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0157 | Views/Pages/AccountsPage.xaml / 战绩 | {"Visibility": "{Binding StatsVisibility}", "Click": "OnStats"} | 原事件或 owner 路由 · Views\Accounts\AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0158 | Views/Pages/AccountsPage.xaml / 编辑 | {"Click": "OnEdit"} | 同一绑定 / Command（共享模板或控件更名） · Views\Accounts\AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0159 | Views/Pages/AccountsPage.xaml / 删除 | {"Click": "OnDelete"} | 同一绑定 / Command（共享模板或控件更名） · Views\Accounts\AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0160 | Views/Pages/AccountsPage.xaml / StackPanel | {"Visibility": "{Binding UnsavedVisibility}"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0161 | Views/Pages/AccountsPage.xaml / {Binding UnsavedCountText} | {"Text": "{Binding UnsavedCountText}"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0162 | Views/Pages/AccountsPage.xaml / ItemsControl | {"ItemsSource": "{Binding UnsavedAccounts}"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0163 | Views/Pages/AccountsPage.xaml / 隐藏 | {"Click": "OnHide"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0164 | Views/Pages/AccountsPage.xaml / 重新登录并保存 | {"Click": "OnSwitchCard"} | 原契约保留 · Views/Pages/AccountsPage.xaml | 静态；运行验证按测试记录 |
| UI-0165 | Views/Pages/AccountsPage.xaml / {Binding BattleTag} | {"Text": "{Binding BattleTag}"} | 原契约保留 · Views/Accounts/AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0166 | Views/Pages/AccountsPage.xaml / {Binding ProfileText} | {"Text": "{Binding ProfileText}", "ToolTip": "{Binding ProfileDetail}"} | 原契约保留 · Views/Accounts/AccountTile.xaml | 静态；运行验证按测试记录 |
| UI-0167 | Views/Pages/AccountsPage.xaml.cs / catch (Exception ex) { MessageBox.Show("无法打开账号快照目录：" + ex.Message + "\n" + _vm.AccountSnapshotPath, "账号快照"); } | {} | 原契约保留 · Views/Pages/AccountsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0168 | Views/Pages/AccountsPage.xaml.cs / MessageBox.Show("请先在 Battle.net 登录该账号，刷新列表后保存账号快照。", "尚未识别当前账号"); | {} | 原契约保留 · Views/Pages/AccountsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0169 | Views/Pages/AccountsPage.xaml.cs / if (confirmation.ShowDialog() == true) | {} | 原契约保留 · Views/Pages/AccountsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0170 | Views/Pages/AccountsPage.xaml.cs / if (_vm != null && new LoginNewWindow { Owner = Window.GetWindow(this) }.ShowDialog() == true) | {} | 原契约保留 · Views/Pages/AccountsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0171 | Views/Pages/AccountsPage.xaml.cs / var ok = MessageBox.Show($"「{row.BattleTag}」需要重新登录 Battle.net。\n{row.ProfileDetail}\n\n现在关闭 Battle.net 并请求登录页，请手动选择该账号。登录完成后点击“更新账号备份”保存；此操作不会自动保存。", | {} | 原契约保留 · Views/Pages/AccountsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0172 | Views/Pages/AccountsPage.xaml.cs / new AccountSettingsWindow(_vm, row) { Owner = Window.GetWindow(this) }.ShowDialog(); | {} | 原契约保留 · Views/Pages/AccountsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0173 | Views/Pages/AccountsPage.xaml.cs / new DeleteConfirmWindow(row.BattleTag) { Owner = Window.GetWindow(this) }.ShowDialog() == true) | {} | 原契约保留 · Views/Pages/AccountsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0174 | Views/Pages/AccountsPage.xaml.cs / new DeleteConfirmWindow(row.BattleTag, true) { Owner = Window.GetWindow(this) }.ShowDialog() == true) _vm.HideAccount(row); | {} | 原契约保留 · Views/Pages/AccountsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0175 | Views/Pages/DiagnosticsPage.xaml / {Binding StatusIcon} | {"Text": "{Binding StatusIcon}"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0176 | Views/Pages/DiagnosticsPage.xaml / DataTrigger | {"Binding": "{Binding Status}"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0177 | Views/Pages/DiagnosticsPage.xaml / DataTrigger | {"Binding": "{Binding Status}"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0178 | Views/Pages/DiagnosticsPage.xaml / DataTrigger | {"Binding": "{Binding Status}"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0179 | Views/Pages/DiagnosticsPage.xaml / {Binding Category} | {"Text": "{Binding Category}"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0180 | Views/Pages/DiagnosticsPage.xaml / {Binding Name} | {"Text": "{Binding Name}"} | 原契约保留 · Stats/HeroDetailWindow.xaml | 静态；运行验证按测试记录 |
| UI-0181 | Views/Pages/DiagnosticsPage.xaml / {Binding Summary} | {"Text": "{Binding Summary}"} | 原契约保留 · Controls/CampaignInventoryView.xaml | 静态；运行验证按测试记录 |
| UI-0182 | Views/Pages/DiagnosticsPage.xaml / {Binding Details} | {"Text": "{Binding Details}"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0183 | Views/Pages/DiagnosticsPage.xaml / {Binding StatusText} | {"Text": "{Binding StatusText}"} | 原契约保留 · Views/Dashboard/DashboardView.xaml | 静态；运行验证按测试记录 |
| UI-0184 | Views/Pages/DiagnosticsPage.xaml / 开始诊断 | {"Click": "OnStart"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0185 | Views/Pages/DiagnosticsPage.xaml / 复制诊断结果 | {"IsEnabled": "{Binding CanExport}", "Click": "OnCopy"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0186 | Views/Pages/DiagnosticsPage.xaml / 导出诊断包 | {"IsEnabled": "{Binding CanExport}", "Click": "OnExport"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0187 | Views/Pages/DiagnosticsPage.xaml / {Binding StatusText} | {"Text": "{Binding StatusText}"} | 原契约保留 · Views/Dashboard/DashboardView.xaml | 静态；运行验证按测试记录 |
| UI-0188 | Views/Pages/DiagnosticsPage.xaml / {Binding ProgressText} | {"Text": "{Binding ProgressText}"} | 原契约保留 · Controls/RewardHero.xaml | 静态；运行验证按测试记录 |
| UI-0189 | Views/Pages/DiagnosticsPage.xaml / ProgressBar | {"Value": "{Binding ProgressPercent, Mode=OneWay}", "IsIndeterminate": "{Binding IsRunning, Mode=OneWay}"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0190 | Views/Pages/DiagnosticsPage.xaml / 取消诊断 | {"Visibility": "{Binding IsRunning, Converter={StaticResource BoolVisibility}}", "Click": "OnCancel"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0191 | Views/Pages/DiagnosticsPage.xaml / {Binding SummaryText} | {"Text": "{Binding SummaryText}"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0192 | Views/Pages/DiagnosticsPage.xaml / ItemsControl | {"ItemsSource": "{Binding Checks}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0193 | Views/Pages/DiagnosticsPage.xaml / DataTrigger | {"Binding": "{Binding Checks.Count}"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0194 | Views/Pages/DiagnosticsPage.xaml / DataTrigger | {"Binding": "{Binding Checks.Count}"} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml | 静态；运行验证按测试记录 |
| UI-0195 | Views/Pages/DiagnosticsPage.xaml.cs / catch { MessageBox.Show("无法写入剪贴板，请稍后重试。", "诊断中心", MessageBoxButton.OK, MessageBoxImage.Information); } | {} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0196 | Views/Pages/DiagnosticsPage.xaml.cs / var result = MessageBox.Show($"诊断包已生成：\n{path}\n\n是否打开所在目录？", "诊断中心", | {} | 原契约保留 · Views/Pages/DiagnosticsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0197 | Views/Pages/DropsPage.xaml / ToggleButton | {"IsChecked": "{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0198 | Views/Pages/DropsPage.xaml / {Binding SelectionBoxItem, RelativeSource={RelativeSource AncestorType=ComboBox}} | {"Content": "{Binding SelectionBoxItem, RelativeSource={RelativeSource AncestorType=ComboBox}}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0199 | Views/Pages/DropsPage.xaml / PART_EditableTextBox | {"Text": "{Binding Text, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0200 | Views/Pages/DropsPage.xaml / Border | {"MinWidth": "{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"} | 原契约保留 · Stats/Theme/StatsStyles.xaml | 静态；运行验证按测试记录 |
| UI-0201 | Views/Pages/DropsPage.xaml / {Binding Primary} | {"Text": "{Binding Primary}", "ToolTip": "{Binding Primary}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0202 | Views/Pages/DropsPage.xaml / {Binding Secondary} | {"Text": "{Binding Secondary}", "ToolTip": "{Binding Secondary}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0203 | Views/Pages/DropsPage.xaml / {Binding Status} | {"Text": "{Binding Status}"} | 原契约保留 · Controls/CampaignInventoryView.xaml | 静态；运行验证按测试记录 |
| UI-0204 | Views/Pages/DropsPage.xaml / {Binding Primary} | {"Text": "{Binding Primary}", "ToolTip": "{Binding Primary}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0205 | Views/Pages/DropsPage.xaml / {Binding Secondary} | {"Text": "{Binding Secondary}", "ToolTip": "{Binding Secondary}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0206 | Views/Pages/DropsPage.xaml / {Binding Status} | {"Text": "{Binding Status}"} | 原契约保留 · Controls/CampaignInventoryView.xaml | 静态；运行验证按测试记录 |
| UI-0207 | Views/Pages/DropsPage.xaml / {Binding TwitchClaimButtonText} | {"Content": "{Binding TwitchClaimButtonText}", "Visibility": "{Binding TwitchClaimVisibility}", "IsEnabled": "{Binding CanStartTwitchClaim}", "Click": "OnClaimTwitchDrop"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0208 | Views/Pages/DropsPage.xaml / {Binding Secondary} | {"Text": "{Binding Secondary}", "ToolTip": "{Binding Secondary}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0209 | Views/Pages/DropsPage.xaml / {Binding Primary} | {"Text": "{Binding Primary}", "ToolTip": "{Binding Primary}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0210 | Views/Pages/DropsPage.xaml / {Binding Status} | {"Text": "{Binding Status}"} | 原契约保留 · Controls/CampaignInventoryView.xaml | 静态；运行验证按测试记录 |
| UI-0211 | Views/Pages/DropsPage.xaml / ProgressBar | {"Value": "{Binding Progress, Mode=OneWay}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0212 | Views/Pages/DropsPage.xaml / {Binding Detail} | {"Text": "{Binding Detail}", "ToolTip": "{Binding Detail}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0213 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding Enabled}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0214 | Views/Pages/DropsPage.xaml / {Binding Primary} | {"Text": "{Binding Primary}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0215 | Views/Pages/DropsPage.xaml / {Binding Secondary} | {"Text": "{Binding Secondary}", "ToolTip": "{Binding Secondary}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0216 | Views/Pages/DropsPage.xaml / {Binding Number} | {"Text": "{Binding Number}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0217 | Views/Pages/DropsPage.xaml / {Binding Title} | {"Text": "{Binding Title}"} | 原契约保留 · Controls/RewardHero.xaml | 静态；运行验证按测试记录 |
| UI-0218 | Views/Pages/DropsPage.xaml / {Binding StateText} | {"Text": "{Binding StateText}"} | 原契约保留 · Controls/RewardHero.xaml | 静态；运行验证按测试记录 |
| UI-0219 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding StateKind}"} | 原契约保留 · Controls/RewardCard.xaml | 静态；运行验证按测试记录 |
| UI-0220 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding StateKind}"} | 原契约保留 · Controls/RewardCard.xaml | 静态；运行验证按测试记录 |
| UI-0221 | Views/Pages/DropsPage.xaml / {Binding Description} | {"Text": "{Binding Description}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0222 | Views/Pages/DropsPage.xaml / {Binding ActionText} | {"Content": "{Binding ActionText}", "Tag": "{Binding ActionKey}", "Visibility": "{Binding ActionVisibility}", "Click": "OnQuickStartAction"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0223 | Views/Pages/DropsPage.xaml / {Binding Campaign} | {"Text": "{Binding Campaign}", "ToolTip": "{Binding Campaign}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0224 | Views/Pages/DropsPage.xaml / {Binding ProgressText} | {"Text": "{Binding ProgressText}"} | 原契约保留 · Controls/RewardHero.xaml | 静态；运行验证按测试记录 |
| UI-0225 | Views/Pages/DropsPage.xaml / {Binding Reward, Mode=OneWay} | {"Text": "{Binding Reward, Mode=OneWay}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0226 | Views/Pages/DropsPage.xaml / {Binding Account, Mode=OneWay} | {"Text": "{Binding Account, Mode=OneWay}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0227 | Views/Pages/DropsPage.xaml / {Binding Channel, Mode=OneWay} | {"Text": "{Binding Channel, Mode=OneWay}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0228 | Views/Pages/DropsPage.xaml / {Binding GameAccountBindingText} | {"Text": "{Binding GameAccountBindingText}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0229 | Views/Pages/DropsPage.xaml / ProgressBar | {"Value": "{Binding Percent, Mode=OneWay}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0230 | Views/Pages/DropsPage.xaml / {Binding WatchPathText} | {"Text": "{Binding WatchPathText}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0231 | Views/Pages/DropsPage.xaml / {Binding SyncState} | {"Text": "{Binding SyncState}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0232 | Views/Pages/DropsPage.xaml / {Binding RemainingText} | {"Text": "{Binding RemainingText}"} | 原契约保留 · Controls/RewardCard.xaml | 静态；运行验证按测试记录 |
| UI-0233 | Views/Pages/DropsPage.xaml / {Binding ConfirmationText} | {"Text": "{Binding ConfirmationText}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0234 | Views/Pages/DropsPage.xaml / {Binding Diagnostic} | {"Text": "{Binding Diagnostic}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0235 | Views/Pages/DropsPage.xaml / {Binding Name} | {"Text": "{Binding Name}", "ToolTip": "{Binding Name}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0236 | Views/Pages/DropsPage.xaml / {Binding OfficialText} | {"Text": "{Binding OfficialText}"} | 结构化可视化替代：OfficialText → Subtitle="官方任务接口" · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0237 | Views/Pages/DropsPage.xaml / {Binding Status} | {"Text": "{Binding Status}"} | 原契约保留 · Controls/CampaignInventoryView.xaml | 静态；运行验证按测试记录 |
| UI-0238 | Views/Pages/DropsPage.xaml / ProgressBar | {"Value": "{Binding Percent}"} | 结构化可视化替代：Percent → Binding Percent,Mode=OneWay · Controls/RewardCard.xaml | 静态；运行验证按测试记录 |
| UI-0239 | Views/Pages/DropsPage.xaml / {Binding ProgressText} | {"Text": "{Binding ProgressText}"} | 原契约保留 · Controls/RewardHero.xaml | 静态；运行验证按测试记录 |
| UI-0240 | Views/Pages/DropsPage.xaml / {Binding ClaimButtonText} | {"Content": "{Binding ClaimButtonText}", "Visibility": "{Binding ClaimVisibility}", "IsEnabled": "{Binding CanClaim}", "Command": "{Binding DataContext.BilibiliDetails.ClaimRewardCommand, ElementName=DropsRoot}", "CommandParameter": "{Binding}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0241 | Views/Pages/DropsPage.xaml / CheckBox | {"IsChecked": "{Binding Enabled, Mode=TwoWay}", "Command": "{Binding DataContext.BilibiliDetails.SetRoomEnabledCommand, ElementName=DropsRoot}", "CommandParameter": "{Binding}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0242 | Views/Pages/DropsPage.xaml / {Binding Name} | {"Text": "{Binding Name}", "ToolTip": "{Binding Name}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0243 | Views/Pages/DropsPage.xaml / {Binding RoomIdText, Mode=OneWay} | {"Text": "{Binding RoomIdText, Mode=OneWay}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0244 | Views/Pages/DropsPage.xaml / {Binding Status, Mode=OneWay} | {"Text": "{Binding Status, Mode=OneWay}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0245 | Views/Pages/DropsPage.xaml / {Binding SessionText, Mode=OneWay} | {"Text": "{Binding SessionText, Mode=OneWay}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0246 | Views/Pages/DropsPage.xaml / 删除 | {"Command": "{Binding DataContext.BilibiliDetails.RemoveRoomCommand, ElementName=DropsRoot}", "CommandParameter": "{Binding}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0247 | Views/Pages/DropsPage.xaml / {Binding Id} | {"Text": "{Binding Id}", "ToolTip": "{Binding Id}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0248 | Views/Pages/DropsPage.xaml / {Binding StateText} | {"Text": "{Binding StateText}"} | 原契约保留 · Controls/RewardHero.xaml | 静态；运行验证按测试记录 |
| UI-0249 | Views/Pages/DropsPage.xaml / {Binding Detail} | {"Text": "{Binding Detail}", "ToolTip": "{Binding Detail}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0250 | Views/Pages/DropsPage.xaml / {Binding Status} | {"Text": "{Binding Status}"} | 原契约保留 · Controls/CampaignInventoryView.xaml | 静态；运行验证按测试记录 |
| UI-0251 | Views/Pages/DropsPage.xaml / {Binding Name} | {"Text": "{Binding Name}", "ToolTip": "{Binding Name}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0252 | Views/Pages/DropsPage.xaml / {Binding RoomText, Mode=OneWay} | {"Text": "{Binding RoomText, Mode=OneWay}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0253 | Views/Pages/DropsPage.xaml / {Binding TaskText, Mode=OneWay} | {"Text": "{Binding TaskText, Mode=OneWay}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0254 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding ConnectionState}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0255 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding ConnectionState}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0256 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding ConnectionState}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0257 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding ConnectionState}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0258 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding ConnectionState}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0259 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding ConnectionState}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0260 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding ConnectionState}"} | 原契约保留 · Views/Drops/Shared/DropsResources.xaml | 静态；运行验证按测试记录 |
| UI-0261 | Views/Pages/DropsPage.xaml / 刷新 | {"Click": "OnRefresh"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0262 | Views/Pages/DropsPage.xaml / SoopTab | {"Checked": "OnPlatformChanged"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0263 | Views/Pages/DropsPage.xaml / StackPanel | {"DataContext": "{Binding Soop}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0264 | Views/Pages/DropsPage.xaml / SoopStatusDot | {"ToolTip": "{Binding ConnectionStateText}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0265 | Views/Pages/DropsPage.xaml / YouTubeTab | {"Checked": "OnPlatformChanged"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0266 | Views/Pages/DropsPage.xaml / StackPanel | {"DataContext": "{Binding YouTube}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0267 | Views/Pages/DropsPage.xaml / YouTubeStatusDot | {"ToolTip": "{Binding ConnectionStateText}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0268 | Views/Pages/DropsPage.xaml / TwitchTab | {"Checked": "OnPlatformChanged"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0269 | Views/Pages/DropsPage.xaml / StackPanel | {"DataContext": "{Binding Twitch}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0270 | Views/Pages/DropsPage.xaml / TwitchStatusDot | {"ToolTip": "{Binding ConnectionStateText}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0271 | Views/Pages/DropsPage.xaml / BilibiliTab | {"Checked": "OnPlatformChanged"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0272 | Views/Pages/DropsPage.xaml / StackPanel | {"DataContext": "{Binding Bilibili}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0273 | Views/Pages/DropsPage.xaml / BilibiliStatusDot | {"ToolTip": "{Binding ConnectionStateText}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0274 | Views/Pages/DropsPage.xaml / {Binding ActivePlatform.Name} | {"Text": "{Binding ActivePlatform.Name}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0275 | Views/Pages/DropsPage.xaml / {Binding ActivePlatform.Status} | {"Text": "{Binding ActivePlatform.Status}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0276 | Views/Pages/DropsPage.xaml / {Binding ActiveAccountText, StringFormat=账号：{0}} | {"Text": "{Binding ActiveAccountText, StringFormat=账号：{0}}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0277 | Views/Pages/DropsPage.xaml / {Binding ActivePlatform.Summary} | {"Text": "{Binding ActivePlatform.Summary}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0278 | Views/Pages/DropsPage.xaml / DropsActiveStartButton | {"Content": "{Binding ActiveStartText}", "IsEnabled": "{Binding CanActiveStart}", "Click": "OnActiveStart"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0279 | Views/Pages/DropsPage.xaml / DropsActiveStopButton | {"IsEnabled": "{Binding CanActiveStop}", "Click": "OnActiveStop"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0280 | Views/Pages/DropsPage.xaml / 刷新当前平台 | {"Click": "OnRefresh"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0281 | Views/Pages/DropsPage.xaml / 官方背包 / 平台 | {"Click": "OnActiveInventory"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0282 | Views/Pages/DropsPage.xaml / 打开直播间 | {"Click": "OnOpenCurrentWatch"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0283 | Views/Pages/DropsPage.xaml / DropsNoticeBanner | {"Visibility": "{Binding ActivePlatform.NoticeVisibility}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0284 | Views/Pages/DropsPage.xaml / {Binding ActivePlatform.NoticeLevel} | {"Text": "{Binding ActivePlatform.NoticeLevel}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0285 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding ActivePlatform.NoticeLevel}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0286 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding ActivePlatform.NoticeLevel}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0287 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding ActivePlatform.NoticeLevel}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0288 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding ActivePlatform.NoticeLevel}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0289 | Views/Pages/DropsPage.xaml / {Binding ActivePlatform.Notice} | {"Text": "{Binding ActivePlatform.Notice}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0290 | Views/Pages/DropsPage.xaml / 重试连接 / 刷新 | {"Click": "OnActiveRetry"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0291 | Views/Pages/DropsPage.xaml / 查看日志 | {"Click": "OnShowDropsLog"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0292 | Views/Pages/DropsPage.xaml / DropsDismissNoticeButton | {"Click": "OnDismissNotice"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0293 | Views/Pages/DropsPage.xaml / SoopPanel | {"Visibility": "{Binding SoopPanelVisibility}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0294 | Views/Pages/DropsPage.xaml / 立即重试 | {"Visibility": "{Binding SoopRetryNowVisibility}", "Click": "OnSoopRetryNow"} | 同一绑定 / Command（共享模板或控件更名） · Views\Drops\Soop\SoopOverview.xaml | 静态；运行验证按测试记录 |
| UI-0295 | Views/Pages/DropsPage.xaml / {Binding SoopRetryStatusText} | {"Text": "{Binding SoopRetryStatusText}", "Visibility": "{Binding SoopRetryStatusVisibility}"} | 原契约保留 · Views/Drops/Soop/SoopOverview.xaml | 静态；运行验证按测试记录 |
| UI-0296 | Views/Pages/DropsPage.xaml / {Binding SoopLastSuccessText} | {"Text": "{Binding SoopLastSuccessText}", "Visibility": "{Binding SoopLastSuccessVisibility}"} | 原契约保留 · Views/Drops/Soop/SoopOverview.xaml | 静态；运行验证按测试记录 |
| UI-0297 | Views/Pages/DropsPage.xaml / {Binding SoopWatchStatusText} | {"Text": "{Binding SoopWatchStatusText}"} | 原契约保留 · Views/Drops/Soop/SoopOverview.xaml | 静态；运行验证按测试记录 |
| UI-0298 | Views/Pages/DropsPage.xaml / ListBox | {"ItemsSource": "{Binding SoopCurrentProgress}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Soop/SoopOverview.xaml | 静态；运行验证按测试记录 |
| UI-0299 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding SoopCurrentProgress.Count}"} | 原契约保留 · Views/Drops/Soop/SoopOverview.xaml | 静态；运行验证按测试记录 |
| UI-0300 | Views/Pages/DropsPage.xaml / ListBox | {"ItemsSource": "{Binding Tasks}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0301 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding Tasks.Count}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0302 | Views/Pages/DropsPage.xaml / 打开背包 | {"Click": "OnOpenSoopInventory"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0303 | Views/Pages/DropsPage.xaml / SoopInventoryList | {"ItemsSource": "{Binding Inventory}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0304 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding Inventory.Count}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0305 | Views/Pages/DropsPage.xaml / {Binding SelectedItem.SoopClaimButtonText, ElementName=SoopInventoryList, TargetNullValue=领取选中奖励, FallbackValue=领取选中奖励} | {"Content": "{Binding SelectedItem.SoopClaimButtonText, ElementName=SoopInventoryList, TargetNullValue=领取选中奖励, FallbackValue=领取选中奖励}", "Click": "OnClaimSoopReward"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0306 | Views/Pages/DropsPage.xaml / 复制选中兑换码 | {"Click": "OnCopySoopCode"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0307 | Views/Pages/DropsPage.xaml / 进度历史与回退诊断 | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0308 | Views/Pages/DropsPage.xaml / ListBox | {"ItemsSource": "{Binding SoopProgressHistory}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0309 | Views/Pages/DropsPage.xaml / SoopUserId | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0310 | Views/Pages/DropsPage.xaml / SoopPassword | {"PasswordChanged": "OnSoopPasswordChanged"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0311 | Views/Pages/DropsPage.xaml / 添加账号 | {"Click": "OnSoopAddAccount"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0312 | Views/Pages/DropsPage.xaml / SoopAccountsList | {"ItemsSource": "{Binding Accounts}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0313 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding Accounts.Count}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0314 | Views/Pages/DropsPage.xaml / 启动选中账号 | {"Click": "OnSoopStartAccount"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0315 | Views/Pages/DropsPage.xaml / 停止选中账号 | {"Click": "OnSoopStopAccount"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0316 | Views/Pages/DropsPage.xaml / 设为主账号 | {"Click": "OnSoopSetPrimaryAccount"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0317 | Views/Pages/DropsPage.xaml / 删除选中账号 | {"Click": "OnSoopDeleteAccount"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0318 | Views/Pages/DropsPage.xaml / 全部开始 | {"Click": "OnStart"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0319 | Views/Pages/DropsPage.xaml / 全部停止 | {"Click": "OnStop"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0320 | Views/Pages/DropsPage.xaml / SoopChannelMode | {"SelectionChanged": "OnSoopChannelModeChanged"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0321 | Views/Pages/DropsPage.xaml / SoopPriorityMission | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0322 | Views/Pages/DropsPage.xaml / SoopManualChannel | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0323 | Views/Pages/DropsPage.xaml / SoopPreferredChannel | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0324 | Views/Pages/DropsPage.xaml / SoopLowBandwidth | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0325 | Views/Pages/DropsPage.xaml / SoopAutoClaim | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0326 | Views/Pages/DropsPage.xaml / SoopHangWithoutMissions | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0327 | Views/Pages/DropsPage.xaml / SoopAutoStart | {"Click": "OnAutoStartChanged"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0328 | Views/Pages/DropsPage.xaml / SoopMissionInterval | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0329 | Views/Pages/DropsPage.xaml / SoopInventoryInterval | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0330 | Views/Pages/DropsPage.xaml / SoopChannelInterval | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0331 | Views/Pages/DropsPage.xaml / SoopReauthInterval | {} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0332 | Views/Pages/DropsPage.xaml / SoopSaveSettingsButton | {"Click": "OnSaveSoop"} | 同一绑定 / Command（共享模板或控件更名） · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0333 | Views/Pages/DropsPage.xaml / {Binding SoopRefreshButtonText} | {"Content": "{Binding SoopRefreshButtonText}", "IsEnabled": "{Binding CanRefreshSoop}", "Click": "OnSoopRefresh"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0334 | Views/Pages/DropsPage.xaml / ProgressBar | {"Visibility": "{Binding SoopRefreshProgressVisibility}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0335 | Views/Pages/DropsPage.xaml / {Binding SoopRefreshStatus} | {"Text": "{Binding SoopRefreshStatus}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0336 | Views/Pages/DropsPage.xaml / 使用帮助 | {"Click": "OnToggleHelp"} | 同一绑定 / Command（共享模板或控件更名） · Views\Drops\Soop\SoopOverview.xaml | 静态；运行验证按测试记录 |
| UI-0337 | Views/Pages/DropsPage.xaml / {Binding SoopQuickStart.Summary} | {"Header": "{Binding SoopQuickStart.Summary}", "IsExpanded": "{Binding SoopQuickStart.IsExpanded, Mode=TwoWay}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Soop/SoopOverview.xaml | 静态；运行验证按测试记录 |
| UI-0338 | Views/Pages/DropsPage.xaml / ItemsControl | {"ItemsSource": "{Binding SoopQuickStart.Steps}"} | 原契约保留 · Views/Drops/Soop/SoopOverview.xaml | 静态；运行验证按测试记录 |
| UI-0339 | Views/Pages/DropsPage.xaml / YouTubePanel | {"Visibility": "{Binding YouTubePanelVisibility}"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0340 | Views/Pages/DropsPage.xaml / {Binding YouTubeLastSuccessText} | {"Text": "{Binding YouTubeLastSuccessText}", "Visibility": "{Binding YouTubeLastSuccessVisibility}"} | 原契约保留 · Views/Drops/YouTube/YouTubeOverview.xaml | 静态；运行验证按测试记录 |
| UI-0341 | Views/Pages/DropsPage.xaml / ListBox | {"ItemsSource": "{Binding Tasks}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0342 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding Tasks.Count}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0343 | Views/Pages/DropsPage.xaml / YouTubeProfilesList | {"ItemsSource": "{Binding Accounts}"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0344 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding Accounts.Count}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0345 | Views/Pages/DropsPage.xaml / YouTubeProfileName | {} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0346 | Views/Pages/DropsPage.xaml / 添加账号 | {"Click": "OnYouTubeAddProfile"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0347 | Views/Pages/DropsPage.xaml / 打开登录窗口 | {"Click": "OnYouTubeLogin"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0348 | Views/Pages/DropsPage.xaml / 删除账号 | {"Click": "OnYouTubeDeleteProfile"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0349 | Views/Pages/DropsPage.xaml / YouTubeChannelsList | {"ItemsSource": "{Binding Channels}"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0350 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding Channels.Count}"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0351 | Views/Pages/DropsPage.xaml / YouTubeChannelName | {} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0352 | Views/Pages/DropsPage.xaml / YouTubeChannelId | {} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0353 | Views/Pages/DropsPage.xaml / YouTubeChannelUrl | {} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0354 | Views/Pages/DropsPage.xaml / 添加频道 | {"Click": "OnYouTubeAddChannel"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0355 | Views/Pages/DropsPage.xaml / 启用 / 停用 | {"Click": "OnYouTubeToggleChannel"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0356 | Views/Pages/DropsPage.xaml / 删除 | {"Click": "OnYouTubeDeleteChannel"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0357 | Views/Pages/DropsPage.xaml / YouTubeBrowser | {"SelectionChanged": "OnYouTubeBrowserChanged"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0358 | Views/Pages/DropsPage.xaml / 自定义浏览器路径（可选） | {} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0359 | Views/Pages/DropsPage.xaml / 浏览... | {"Click": "OnBrowseYouTubeBrowser"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0360 | Views/Pages/DropsPage.xaml / YouTubeBrowserPath | {"ToolTip": "{Binding Text, RelativeSource={RelativeSource Self}}"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0361 | Views/Pages/DropsPage.xaml / YouTubeMode | {"SelectionChanged": "OnYouTubeModeChanged"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0362 | Views/Pages/DropsPage.xaml / YouTubeManualUrl | {} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0363 | Views/Pages/DropsPage.xaml / YouTubeHeadless | {} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0364 | Views/Pages/DropsPage.xaml / YouTubeMute | {} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0365 | Views/Pages/DropsPage.xaml / YouTubeInterval | {} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0366 | Views/Pages/DropsPage.xaml / YouTubeSaveSettingsButton | {"Click": "OnSaveYouTube"} | 同一绑定 / Command（共享模板或控件更名） · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0367 | Views/Pages/DropsPage.xaml / ListBox | {"ItemsSource": "{Binding History}"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0368 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding History.Count}"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0369 | Views/Pages/DropsPage.xaml / 使用帮助 | {"Click": "OnToggleHelp"} | 同一绑定 / Command（共享模板或控件更名） · Views\Drops\Soop\SoopOverview.xaml | 静态；运行验证按测试记录 |
| UI-0370 | Views/Pages/DropsPage.xaml / YouTubeStartButton | {"Visibility": "{Binding YouTube.StartVisibility}", "Click": "OnStart"} | 同一绑定 / Command（共享模板或控件更名） · Views\Drops\YouTube\YouTubeOverview.xaml | 静态；运行验证按测试记录 |
| UI-0371 | Views/Pages/DropsPage.xaml / YouTubeStopButton | {"Visibility": "{Binding YouTube.StopVisibility}", "Click": "OnStop"} | 同一绑定 / Command（共享模板或控件更名） · Views\Drops\YouTube\YouTubeOverview.xaml | 静态；运行验证按测试记录 |
| UI-0372 | Views/Pages/DropsPage.xaml / {Binding YouTubeQuickStart.Summary} | {"Header": "{Binding YouTubeQuickStart.Summary}", "IsExpanded": "{Binding YouTubeQuickStart.IsExpanded, Mode=TwoWay}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/YouTube/YouTubeOverview.xaml | 静态；运行验证按测试记录 |
| UI-0373 | Views/Pages/DropsPage.xaml / ItemsControl | {"ItemsSource": "{Binding YouTubeQuickStart.Steps}"} | 原契约保留 · Views/Drops/YouTube/YouTubeOverview.xaml | 静态；运行验证按测试记录 |
| UI-0374 | Views/Pages/DropsPage.xaml / TwitchPanel | {"Visibility": "{Binding TwitchPanelVisibility}"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0375 | Views/Pages/DropsPage.xaml / Border | {"Visibility": "{Binding TwitchAuthorizationVisibility}"} | 原契约保留 · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0376 | Views/Pages/DropsPage.xaml / TwitchAuthorizationCodeBox | {"Text": "{Binding TwitchAuthorizationCode, Mode=OneWay}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0377 | Views/Pages/DropsPage.xaml / {Binding TwitchAuthorizationExpiryText} | {"Text": "{Binding TwitchAuthorizationExpiryText}"} | 原契约保留 · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0378 | Views/Pages/DropsPage.xaml / 打开 Twitch 授权页面 | {"Click": "OnOpenTwitchAuthorization"} | 同一绑定 / Command（共享模板或控件更名） · Views\Drops\Twitch\TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0379 | Views/Pages/DropsPage.xaml / TwitchCopyCodeButton | {"Click": "OnCopyTwitchAuthorizationCode"} | 同一绑定 / Command（共享模板或控件更名） · Views\Drops\Twitch\TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0380 | Views/Pages/DropsPage.xaml / {Binding TwitchChannelText} | {"Text": "{Binding TwitchChannelText}"} | 结构化可视化替代：TwitchChannelText → TwitchInventory.Channel · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0381 | Views/Pages/DropsPage.xaml / {Binding TwitchDataSourceText} | {"Text": "{Binding TwitchDataSourceText}"} | 原契约保留 · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0382 | Views/Pages/DropsPage.xaml / {Binding TwitchCurrentProgressText} | {"Text": "{Binding TwitchCurrentProgressText}"} | 结构化可视化替代：TwitchCurrentProgressText → Binding ProgressText · Controls/RewardHero.xaml | 静态；运行验证按测试记录 |
| UI-0383 | Views/Pages/DropsPage.xaml / {Binding TwitchWatchRequestText} | {"Text": "{Binding TwitchWatchRequestText}"} | 结构化可视化替代：TwitchWatchRequestText → TwitchInventory.WatchState · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0384 | Views/Pages/DropsPage.xaml / {Binding TwitchNetworkRouteText} | {"Text": "{Binding TwitchNetworkRouteText}"} | 原契约保留 · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0385 | Views/Pages/DropsPage.xaml / ProgressBar | {"Value": "{Binding TwitchCurrentProgress, Mode=OneWay}"} | 结构化可视化替代：TwitchCurrentProgress → Binding Percent · Controls/RewardHero.xaml | 静态；运行验证按测试记录 |
| UI-0386 | Views/Pages/DropsPage.xaml / 使用帮助 | {"Click": "OnToggleHelp"} | 同一绑定 / Command（共享模板或控件更名） · Views\Drops\Soop\SoopOverview.xaml | 静态；运行验证按测试记录 |
| UI-0387 | Views/Pages/DropsPage.xaml / {Binding TwitchQuickStart.Summary} | {"Header": "{Binding TwitchQuickStart.Summary}", "IsExpanded": "{Binding TwitchQuickStart.IsExpanded, Mode=TwoWay}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0388 | Views/Pages/DropsPage.xaml / ItemsControl | {"ItemsSource": "{Binding TwitchQuickStart.Steps}"} | 原契约保留 · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0389 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding TwitchConnectionStage}"} | 结构化可视化替代：TwitchConnectionStage → TwitchLoginVisibility · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0390 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding TwitchConnectionStage}"} | 结构化可视化替代：TwitchConnectionStage → TwitchLoginVisibility · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0391 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding TwitchConnectionStage}"} | 结构化可视化替代：TwitchConnectionStage → TwitchLoginVisibility · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0392 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding TwitchConnectionStage}"} | 结构化可视化替代：TwitchConnectionStage → TwitchLoginVisibility · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0393 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding TwitchConnectionStage}"} | 结构化可视化替代：TwitchConnectionStage → TwitchLoginVisibility · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0394 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding TwitchConnectionStage}"} | 结构化可视化替代：TwitchConnectionStage → TwitchLoginVisibility · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0395 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding TwitchConnectionStage}"} | 结构化可视化替代：TwitchConnectionStage → TwitchLoginVisibility · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0396 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding TwitchConnectionStage}"} | 结构化可视化替代：TwitchConnectionStage → TwitchLoginVisibility · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0397 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding TwitchConnectionStage}"} | 结构化可视化替代：TwitchConnectionStage → TwitchLoginVisibility · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0398 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding TwitchConnectionStage}"} | 结构化可视化替代：TwitchConnectionStage → TwitchLoginVisibility · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0399 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding TwitchConnectionStage}"} | 结构化可视化替代：TwitchConnectionStage → TwitchLoginVisibility · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0400 | Views/Pages/DropsPage.xaml / {Binding Twitch.Status} | {"Text": "{Binding Twitch.Status}"} | 原契约保留 · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0401 | Views/Pages/DropsPage.xaml / {Binding Twitch.Summary} | {"Text": "{Binding Twitch.Summary}"} | 原契约保留 · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0402 | Views/Pages/DropsPage.xaml / {Binding TwitchLoginButtonText} | {"Content": "{Binding TwitchLoginButtonText}", "IsEnabled": "{Binding CanTwitchLogin}", "Visibility": "{Binding TwitchLoginVisibility}", "Click": "OnTwitchLogin"} | 原事件或 owner 路由 · Views\Drops\Twitch\TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0403 | Views/Pages/DropsPage.xaml / 清除登录信息 | {"IsEnabled": "{Binding CanClearTwitchLogin}", "Click": "OnTwitchLogout"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0404 | Views/Pages/DropsPage.xaml / 重新检测 | {"Visibility": "{Binding TwitchRetryVisibility}", "Click": "OnTwitchRetry"} | 同一绑定 / Command（共享模板或控件更名） · Views\Drops\Twitch\TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0405 | Views/Pages/DropsPage.xaml / 备份并恢复默认配置 | {"Visibility": "{Binding TwitchConfigRepairVisibility}", "Click": "OnRepairTwitchSettings"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0406 | Views/Pages/DropsPage.xaml / 开始 Twitch 掉宝 | {"IsEnabled": "{Binding IsTwitchCoreAvailable}", "Visibility": "{Binding Twitch.StartVisibility}", "Click": "OnStart"} | 原事件或 owner 路由 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0407 | Views/Pages/DropsPage.xaml / 停止 Twitch 掉宝 | {"Visibility": "{Binding Twitch.StopVisibility}", "Click": "OnStop"} | 同一绑定 / Command（共享模板或控件更名） · Views\Drops\Twitch\TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0408 | Views/Pages/DropsPage.xaml / TwitchRefreshButton | {"Content": "{Binding TwitchRefreshButtonText}", "IsEnabled": "{Binding CanRefreshTwitch}", "Click": "OnTwitchReload"} | 同名输入 / 控件保留 · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0409 | Views/Pages/DropsPage.xaml / ProgressBar | {"Visibility": "{Binding TwitchRefreshProgressVisibility}"} | 原契约保留 · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0410 | Views/Pages/DropsPage.xaml / {Binding TwitchRefreshStatus} | {"Text": "{Binding TwitchRefreshStatus}", "Visibility": "{Binding TwitchRefreshStatusVisibility}"} | 原契约保留 · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0411 | Views/Pages/DropsPage.xaml / Border | {"Visibility": "{Binding TwitchRetryStatusVisibility}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0412 | Views/Pages/DropsPage.xaml / 立即重试 | {"Visibility": "{Binding TwitchRetryNowVisibility}", "Click": "OnTwitchRetryNow"} | 同一绑定 / Command（共享模板或控件更名） · Views\Drops\Twitch\TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0413 | Views/Pages/DropsPage.xaml / {Binding TwitchRetryStatusText} | {"Text": "{Binding TwitchRetryStatusText}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0414 | Views/Pages/DropsPage.xaml / {Binding TwitchLastSuccessText} | {"Text": "{Binding TwitchLastSuccessText}", "Visibility": "{Binding TwitchLastSuccessVisibility}"} | 原契约保留 · Views/Drops/Twitch/TwitchOverview.xaml | 静态；运行验证按测试记录 |
| UI-0415 | Views/Pages/DropsPage.xaml / TwitchAutoStart | {"Click": "OnAutoStartChanged"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0416 | Views/Pages/DropsPage.xaml / 打开背包 | {"Click": "OnOpenTwitchInventory"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0417 | Views/Pages/DropsPage.xaml / ListBox | {"ItemsSource": "{Binding Inventory}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0418 | Views/Pages/DropsPage.xaml / {Binding TwitchInventoryEmptyText} | {"Text": "{Binding TwitchInventoryEmptyText}"} | 结构化可视化替代：TwitchInventoryEmptyText → TwitchInventory.EmptyText · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0419 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding Inventory.Count}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0420 | Views/Pages/DropsPage.xaml / TwitchChannelsList | {"ItemsSource": "{Binding Channels}"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0421 | Views/Pages/DropsPage.xaml / {Binding TwitchChannelsEmptyText} | {"Text": "{Binding TwitchChannelsEmptyText}"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0422 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding Channels.Count}"} | 原契约保留 · Views/Drops/YouTube/YouTubeWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0423 | Views/Pages/DropsPage.xaml / 选择并切换频道 | {"Click": "OnTwitchSelectChannel"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0424 | Views/Pages/DropsPage.xaml / BilibiliPanel | {"Visibility": "{Binding BilibiliPanelVisibility}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0425 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.LoginStateText} | {"Text": "{Binding BilibiliDetails.LoginStateText}"} | 结构化可视化替代：BilibiliDetails.LoginStateText → BilibiliDetails.AccountStatus · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0426 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.AccountStatus} | {"Text": "{Binding BilibiliDetails.AccountStatus}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0427 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.NetworkModeText} | {"Text": "{Binding BilibiliDetails.NetworkModeText}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0428 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.NetworkPolicyText} | {"Text": "{Binding BilibiliDetails.NetworkPolicyText}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0429 | Views/Pages/DropsPage.xaml / {Binding Bilibili.Status} | {"Text": "{Binding Bilibili.Status}"} | 结构化可视化替代：Bilibili.Status → BilibiliDetails.WatchStateText · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0430 | Views/Pages/DropsPage.xaml / {Binding Bilibili.ConnectionStateText} | {"Text": "{Binding Bilibili.ConnectionStateText}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0431 | Views/Pages/DropsPage.xaml / BilibiliQrLoginButton | {"Visibility": "{Binding BilibiliDetails.LoginVisibility}", "Command": "{Binding BilibiliDetails.ScanQrLoginCommand}"} | 同名输入 / 控件保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0432 | Views/Pages/DropsPage.xaml / 退出登录 | {"Visibility": "{Binding BilibiliDetails.LogoutVisibility}", "Command": "{Binding BilibiliDetails.LogoutCommand}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0433 | Views/Pages/DropsPage.xaml / BilibiliStartButton | {"Visibility": "{Binding Bilibili.StartVisibility}", "Command": "{Binding BilibiliDetails.StartCommand}"} | 同名输入 / 控件保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0434 | Views/Pages/DropsPage.xaml / BilibiliStopButton | {"Visibility": "{Binding Bilibili.StopVisibility}", "Command": "{Binding BilibiliDetails.StopCommand}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0435 | Views/Pages/DropsPage.xaml / BilibiliRefreshButton | {"Command": "{Binding BilibiliDetails.RefreshCommand}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0436 | Views/Pages/DropsPage.xaml / BilibiliDiscoverButton | {"Command": "{Binding BilibiliDetails.DiscoverCommand}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0437 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.QrStateText} | {"Text": "{Binding BilibiliDetails.QrStateText}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0438 | Views/Pages/DropsPage.xaml / BilibiliQrCancelButton | {"Visibility": "{Binding BilibiliDetails.QrCancelVisibility}", "Command": "{Binding BilibiliDetails.CancelQrCommand}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0439 | Views/Pages/DropsPage.xaml / 重新获取二维码 | {"Visibility": "{Binding BilibiliDetails.QrRetryVisibility}", "Command": "{Binding BilibiliDetails.ReacquireQrCommand}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0440 | Views/Pages/DropsPage.xaml / BilibiliQrCard | {"Visibility": "{Binding BilibiliDetails.QrAreaVisibility}"} | 结构化可视化替代：BilibiliDetails.QrAreaVisibility → BilibiliDetails.QrImageVisibility · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0441 | Views/Pages/DropsPage.xaml / Image | {"Source": "{Binding BilibiliDetails.QrImagePath}", "Visibility": "{Binding BilibiliDetails.QrImageVisibility}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0442 | Views/Pages/DropsPage.xaml / BilibiliQuickStartCard | {"Header": "{Binding BilibiliQuickStart.Summary}", "IsExpanded": "{Binding BilibiliQuickStart.IsExpanded, Mode=TwoWay}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0443 | Views/Pages/DropsPage.xaml / ItemsControl | {"ItemsSource": "{Binding BilibiliQuickStart.Steps}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0444 | Views/Pages/DropsPage.xaml / BilibiliCookieBox | {} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0445 | Views/Pages/DropsPage.xaml / 导入并验证 Cookie | {"Command": "{Binding BilibiliDetails.ManualCookieCommand}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0446 | Views/Pages/DropsPage.xaml / BilibiliRoomInput | {"Text": "{Binding BilibiliDetails.RoomReference, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0447 | Views/Pages/DropsPage.xaml / 添加 | {"Command": "{Binding BilibiliDetails.AddRoomCommand}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0448 | Views/Pages/DropsPage.xaml / BilibiliRoomNameInput | {"Text": "{Binding BilibiliDetails.RoomName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0449 | Views/Pages/DropsPage.xaml / BilibiliRoomList | {"ItemsSource": "{Binding BilibiliDetails.Rooms}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0450 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.Activities.Count, StringFormat=已发现活动：{0} 个} | {"Text": "{Binding BilibiliDetails.Activities.Count, StringFormat=已发现活动：{0} 个}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0451 | Views/Pages/DropsPage.xaml / BilibiliActivityList | {"ItemsSource": "{Binding BilibiliDetails.Activities}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0452 | Views/Pages/DropsPage.xaml / BilibiliWatchModePicker | {"SelectedValue": "{Binding BilibiliDetails.WatchMode, Mode=TwoWay}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0453 | Views/Pages/DropsPage.xaml / BilibiliSessionsPerRoomText | {"Text": "{Binding BilibiliDetails.SessionsPerRoomText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0454 | Views/Pages/DropsPage.xaml / BilibiliReconnectDelayText | {"Text": "{Binding BilibiliDetails.ReconnectDelayText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0455 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.SessionEstimateText} | {"Text": "{Binding BilibiliDetails.SessionEstimateText}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0456 | Views/Pages/DropsPage.xaml / 前往网络设置 | {"Click": "OnOpenProxySettings"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0457 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.NetworkModeText} | {"Text": "{Binding BilibiliDetails.NetworkModeText}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0458 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.NetworkPolicyText} | {"Text": "{Binding BilibiliDetails.NetworkPolicyText}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0459 | Views/Pages/DropsPage.xaml / 全局代理尚未启用/配置，哔哩哔哩当前使用直连。 | {"Visibility": "{Binding BilibiliDetails.ProxyConfigurationWarningVisibility}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0460 | Views/Pages/DropsPage.xaml / BilibiliTaskIntervalText | {"Text": "{Binding BilibiliDetails.TaskIntervalText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0461 | Views/Pages/DropsPage.xaml / BilibiliTaskIdsText | {"Text": "{Binding BilibiliDetails.TaskIdsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0462 | Views/Pages/DropsPage.xaml / BilibiliEnabledCheck | {"IsChecked": "{Binding BilibiliDetails.Enabled, Mode=TwoWay}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0463 | Views/Pages/DropsPage.xaml / BilibiliAutoTaskProgress | {"IsChecked": "{Binding BilibiliDetails.AutoTaskProgress, Mode=TwoWay}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0464 | Views/Pages/DropsPage.xaml / BilibiliAutoDiscoverCheck | {"IsChecked": "{Binding BilibiliDetails.AutoDiscover, Mode=TwoWay}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0465 | Views/Pages/DropsPage.xaml / BilibiliAutoClaimCheck | {"IsChecked": "{Binding BilibiliDetails.AutoClaim, Mode=TwoWay}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0466 | Views/Pages/DropsPage.xaml / BilibiliTaskNotificationsCheck | {"IsChecked": "{Binding BilibiliDetails.TaskNotifications, Mode=TwoWay}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0467 | Views/Pages/DropsPage.xaml / BilibiliReconnectCheck | {"IsChecked": "{Binding BilibiliDetails.ReconnectEnabled, Mode=TwoWay}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0468 | Views/Pages/DropsPage.xaml / BilibiliAutoStart | {"IsChecked": "{Binding BilibiliDetails.AutoRestore, Mode=TwoWay}", "Click": "OnAutoStartChanged"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0469 | Views/Pages/DropsPage.xaml / BilibiliAutoResume | {"IsChecked": "{Binding BilibiliDetails.AutoResume, Mode=TwoWay}", "Click": "OnAutoStartChanged"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0470 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.ProxyConfigurationText} | {"Text": "{Binding BilibiliDetails.ProxyConfigurationText}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0471 | Views/Pages/DropsPage.xaml / BilibiliNotifierUrlBox | {} | 同名输入 / 控件保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0472 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.NotifierText} | {"Text": "{Binding BilibiliDetails.NotifierText}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0473 | Views/Pages/DropsPage.xaml / 保存设置 | {"Command": "{Binding BilibiliDetails.SaveSettingsCommand}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0474 | Views/Pages/DropsPage.xaml / 清除第三方通知 | {"Command": "{Binding BilibiliDetails.ClearNotifierCommand}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0475 | Views/Pages/DropsPage.xaml / 刷新 Session 详情 | {"Command": "{Binding BilibiliDetails.RefreshSessionsCommand}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0476 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.SessionSummary} | {"Text": "{Binding BilibiliDetails.SessionSummary}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliOverview.xaml | 静态；运行验证按测试记录 |
| UI-0477 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.LastApiSuccessAt, StringFormat=最近成功 API：{0}} | {"Text": "{Binding BilibiliDetails.LastApiSuccessAt, StringFormat=最近成功 API：{0}}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0478 | Views/Pages/DropsPage.xaml / BilibiliTaskList | {"ItemsSource": "{Binding BilibiliDetails.Tasks}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0479 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding BilibiliDetails.Tasks.Count}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0480 | Views/Pages/DropsPage.xaml / ListBox | {"ItemsSource": "{Binding BilibiliDetails.Rewards}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0481 | Views/Pages/DropsPage.xaml / {Binding TaskName} | {"Text": "{Binding TaskName}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0482 | Views/Pages/DropsPage.xaml / {Binding State} | {"Text": "{Binding State}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0483 | Views/Pages/DropsPage.xaml / {Binding Reward} | {"Text": "{Binding Reward}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0484 | Views/Pages/DropsPage.xaml / {Binding Message} | {"Text": "{Binding Message}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0485 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.ConfiguredSessions} | {"Text": "{Binding BilibiliDetails.ConfiguredSessions}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0486 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.ActiveSessions} | {"Text": "{Binding BilibiliDetails.ActiveSessions}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0487 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.ConnectingSessions} | {"Text": "{Binding BilibiliDetails.ConnectingSessions}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0488 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.RetryingSessions} | {"Text": "{Binding BilibiliDetails.RetryingSessions}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0489 | Views/Pages/DropsPage.xaml / {Binding BilibiliDetails.FailedSessions} | {"Text": "{Binding BilibiliDetails.FailedSessions}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0490 | Views/Pages/DropsPage.xaml / BilibiliSessionList | {"ItemsSource": "{Binding BilibiliDetails.Sessions}"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0491 | Views/Pages/DropsPage.xaml / Border | {"Visibility": "{Binding TwitchPanelVisibility}"} | 同一绑定 / Command（共享模板或控件更名） · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0492 | Views/Pages/DropsPage.xaml / TwitchCampaignScopePicker | {"SelectionChanged": "OnTwitchCampaignScopeChanged"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0493 | Views/Pages/DropsPage.xaml / ListBox | {"ItemsSource": "{Binding Tasks}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0494 | Views/Pages/DropsPage.xaml / {Binding TwitchCampaignsEmptyText} | {"Text": "{Binding TwitchCampaignsEmptyText}"} | 结构化可视化替代：TwitchCampaignsEmptyText → TwitchInventory.EmptyText · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0495 | Views/Pages/DropsPage.xaml / DataTrigger | {"Binding": "{Binding Tasks.Count}"} | 原契约保留 · Views/Drops/Soop/SoopWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0496 | Views/Pages/DropsPage.xaml / TwitchSettingsCard | {"Visibility": "{Binding TwitchPanelVisibility}"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0497 | Views/Pages/DropsPage.xaml / TwitchLanguage | {} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0498 | Views/Pages/DropsPage.xaml / TwitchPriorityMode | {} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0499 | Views/Pages/DropsPage.xaml / TwitchConnectionQuality | {} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0500 | Views/Pages/DropsPage.xaml / 添加 | {"Click": "OnAddPriorityGame"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0501 | Views/Pages/DropsPage.xaml / TwitchPriorityPicker | {"ItemsSource": "{Binding TwitchPriorityChoices}"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0502 | Views/Pages/DropsPage.xaml / TwitchPriorityList | {"ItemsSource": "{Binding TwitchPriorityGames}"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0503 | Views/Pages/DropsPage.xaml / ↑ | {"Click": "OnPriorityUp"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0504 | Views/Pages/DropsPage.xaml / ↓ | {"Click": "OnPriorityDown"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0505 | Views/Pages/DropsPage.xaml / 移除 | {"Click": "OnRemovePriorityGame"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0506 | Views/Pages/DropsPage.xaml / 添加 | {"Click": "OnAddExcludedGame"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0507 | Views/Pages/DropsPage.xaml / TwitchExcludePicker | {"ItemsSource": "{Binding TwitchExcludeChoices}"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0508 | Views/Pages/DropsPage.xaml / TwitchExcludeList | {"ItemsSource": "{Binding TwitchExcludedGames}"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0509 | Views/Pages/DropsPage.xaml / 移除 | {"Click": "OnRemoveExcludedGame"} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0510 | Views/Pages/DropsPage.xaml / TwitchTrayNotifications | {} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0511 | Views/Pages/DropsPage.xaml / TwitchBadgesEmotes | {} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0512 | Views/Pages/DropsPage.xaml / TwitchAvailableDrops | {} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0513 | Views/Pages/DropsPage.xaml / TwitchAutoClaim | {} | 原契约保留 · Views/Drops/Twitch/TwitchWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0514 | Views/Pages/DropsPage.xaml / TwitchSaveSettingsButton | {"IsEnabled": "{Binding IsTwitchCoreAvailable}", "Click": "OnSaveTwitch"} | 同一绑定 / Command（共享模板或控件更名） · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0515 | Views/Pages/DropsPage.xaml / DropsDiagnosticsExpander | {} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0516 | Views/Pages/DropsPage.xaml / 网络测试 | {"Click": "OnNetworkCheck"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0517 | Views/Pages/DropsPage.xaml / {Binding NetworkProxyModeText} | {"Text": "{Binding NetworkProxyModeText}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0518 | Views/Pages/DropsPage.xaml / {Binding NetworkProxyUrlText} | {"Text": "{Binding NetworkProxyUrlText}", "ToolTip": "{Binding NetworkProxyUrlText}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0519 | Views/Pages/DropsPage.xaml / {Binding NetworkProxyFallbackText} | {"Text": "{Binding NetworkProxyFallbackText}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0520 | Views/Pages/DropsPage.xaml / {Binding NetworkProxyScopeText} | {"Text": "{Binding NetworkProxyScopeText}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0521 | Views/Pages/DropsPage.xaml / SoopUseProxyCheck | {"Checked": "OnDropsProxyScopeChanged", "Unchecked": "OnDropsProxyScopeChanged"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0522 | Views/Pages/DropsPage.xaml / YouTubeUseProxyCheck | {"Checked": "OnDropsProxyScopeChanged", "Unchecked": "OnDropsProxyScopeChanged"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0523 | Views/Pages/DropsPage.xaml / TwitchUseProxyCheck | {"Checked": "OnDropsProxyScopeChanged", "Unchecked": "OnDropsProxyScopeChanged"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0524 | Views/Pages/DropsPage.xaml / BilibiliUseProxyCheck | {"Checked": "OnDropsProxyScopeChanged", "Unchecked": "OnDropsProxyScopeChanged"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0525 | Views/Pages/DropsPage.xaml / 前往设置 | {"Click": "OnOpenProxySettings"} | 原契约保留 · Views/Drops/Bilibili/BilibiliWorkspace.xaml | 静态；运行验证按测试记录 |
| UI-0526 | Views/Pages/DropsPage.xaml / 复制全部 | {"Click": "OnCopyAllLogs"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0527 | Views/Pages/DropsPage.xaml / 清空显示 | {"Click": "OnClearLogDisplay"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0528 | Views/Pages/DropsPage.xaml / LogTextBox | {"TextChanged": "OnLogTextChanged"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0529 | Views/Pages/DropsPage.xaml / 复制 | {"Command": "Copy", "CommandTarget": "{Binding PlacementTarget, RelativeSource={RelativeSource AncestorType={x:Type ContextMenu}}}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0530 | Views/Pages/DropsPage.xaml / 全选 | {"Command": "SelectAll", "CommandTarget": "{Binding PlacementTarget, RelativeSource={RelativeSource AncestorType={x:Type ContextMenu}}}"} | 原契约保留 · Views/Pages/DropsPage.xaml | 静态；运行验证按测试记录 |
| UI-0531 | Views/Pages/DropsPage.xaml.cs / }.ShowDialog() == true; | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0532 | Views/Pages/DropsPage.xaml.cs / if (MessageBox.Show($"删除 SOOP 账号「{row.Primary}」的本地登录信息？", "删除账号", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0533 | Views/Pages/DropsPage.xaml.cs / if (MessageBox.Show($"确认领取奖励「{row.Primary}」？", "领取奖励", | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0534 | Views/Pages/DropsPage.xaml.cs / if (dialog.ShowDialog() == true) YouTubeBrowserPath.Text = dialog.FileName; | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0535 | Views/Pages/DropsPage.xaml.cs / var result = MessageBox.Show("是否同时删除该观看账号的浏览器登录数据？\n\n选择“否”只从列表移除，浏览器资料目录仍保留。", "删除观看账号", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning); | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0536 | Views/Pages/DropsPage.xaml.cs / MessageBox.Show("清除 Twitch 本地登录信息并停止当前登录/连接尝试？\n\n优先游戏、排除游戏、自动启动、代理和其它界面设置都会保留。", | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0537 | Views/Pages/DropsPage.xaml.cs / if (_vm == null \|\| MessageBox.Show( | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0538 | Views/Pages/DropsPage.xaml.cs / if (_vm == null \|\| MessageBox.Show("退出哔哩哔哩登录并删除本机加密凭据？\n\n直播间和其它设置会保留。", "退出哔哩哔哩登录", | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0539 | Views/Pages/DropsPage.xaml.cs / if (_vm == null \|\| MessageBox.Show("删除已保存的 Gotify / Server 酱通知配置？", "清除第三方通知", | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0540 | Views/Pages/DropsPage.xaml.cs / if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return; | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0541 | Views/Pages/DropsPage.xaml.cs / var choice = MessageBox.Show($"目标已有「{relative}」。\n\n是：覆盖\n否：跳过\n取消：终止导入", "同名数据", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning); | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0542 | Views/Pages/DropsPage.xaml.cs / MessageBox.Show(message, "导入旧版数据", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning); | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0543 | Views/Pages/FeedbackPanel.xaml / TitleBox | {} | 原契约保留 · Views/Pages/FeedbackPanel.xaml | 静态；运行验证按测试记录 |
| UI-0544 | Views/Pages/FeedbackPanel.xaml / DescriptionBox | {} | 原契约保留 · Views/Pages/FeedbackPanel.xaml | 静态；运行验证按测试记录 |
| UI-0545 | Views/Pages/FeedbackPanel.xaml / ContactBox | {} | 原契约保留 · Views/Pages/FeedbackPanel.xaml | 静态；运行验证按测试记录 |
| UI-0546 | Views/Pages/FeedbackPanel.xaml / AttachLogsBox | {} | 原契约保留 · Views/Pages/FeedbackPanel.xaml | 静态；运行验证按测试记录 |
| UI-0547 | Views/Pages/FeedbackPanel.xaml / PreviewButton | {"Click": "OnPreviewLogs"} | 原契约保留 · Views/Pages/FeedbackPanel.xaml | 静态；运行验证按测试记录 |
| UI-0548 | Views/Pages/FeedbackPanel.xaml / SubmitButton | {"Click": "OnSubmit"} | 原契约保留 · Views/Pages/FeedbackPanel.xaml | 静态；运行验证按测试记录 |
| UI-0549 | Views/Pages/FeedbackPanel.xaml / CancelUploadButton | {"Click": "OnCancelUpload"} | 原契约保留 · Views/Pages/FeedbackPanel.xaml | 静态；运行验证按测试记录 |
| UI-0550 | Views/Pages/FeedbackPanel.xaml / CopyReportButton | {"Click": "OnCopyReportId"} | 原契约保留 · Views/Pages/FeedbackPanel.xaml | 静态；运行验证按测试记录 |
| UI-0551 | Views/Pages/FeedbackPanel.xaml / RetryButton | {"Click": "OnSubmit"} | 原契约保留 · Views/Pages/FeedbackPanel.xaml | 静态；运行验证按测试记录 |
| UI-0552 | Views/Pages/FeedbackPanel.xaml / CopyQqButton | {"Click": "OnCopyQqGroup"} | 原契约保留 · Views/Pages/FeedbackPanel.xaml | 静态；运行验证按测试记录 |
| UI-0553 | Views/Pages/FeedbackPanel.xaml.cs / MessageBox.Show(detail, "将提交的日志", MessageBoxButton.OK, MessageBoxImage.Information); | {} | 原契约保留 · Views/Pages/FeedbackPanel.xaml.cs | 静态；运行验证按测试记录 |
| UI-0554 | Views/Pages/FeedbackPanel.xaml.cs / MessageBox.Show("请填写错误主题和错误描述。", "提交反馈", MessageBoxButton.OK, MessageBoxImage.Information); | {} | 原契约保留 · Views/Pages/FeedbackPanel.xaml.cs | 静态；运行验证按测试记录 |
| UI-0555 | Views/Pages/FeedbackPanel.xaml.cs / dialog.ShowDialog(); | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0556 | Views/Pages/RegionFilesPage.xaml / {Binding PreparationWarningTitle} | {"Text": "{Binding PreparationWarningTitle}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0557 | Views/Pages/RegionFilesPage.xaml / {Binding PreparationWarningText} | {"Text": "{Binding PreparationWarningText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0558 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.CurrentFileText} | {"Text": "{Binding RegionGuide.CurrentFileText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0559 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.ChinaBackupText} | {"Text": "{Binding RegionGuide.ChinaBackupText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0560 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.InternationalBackupText} | {"Text": "{Binding RegionGuide.InternationalBackupText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0561 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.BackupStateText} | {"Text": "{Binding RegionGuide.BackupStateText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0562 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.BackupModeText} | {"Text": "{Binding RegionGuide.BackupModeText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0563 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.BackupSizeText} | {"Text": "{Binding RegionGuide.BackupSizeText}"} | 结构化可视化替代：RegionGuide.BackupSizeText → RegionGuide.BackupSizeText · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0564 | Views/Pages/RegionFilesPage.xaml / StackPanel | {"Visibility": "{Binding RegionGuide.ShowTopRegionActions, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0565 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.SwitchChinaText} | {"Content": "{Binding RegionGuide.SwitchChinaText}", "IsEnabled": "{Binding RegionGuide.CanSwitchChina}", "Click": "OnSwitchChina"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0566 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.SwitchInternationalText} | {"Content": "{Binding RegionGuide.SwitchInternationalText}", "IsEnabled": "{Binding RegionGuide.CanSwitchInternational}", "Click": "OnSwitchInternational"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0567 | Views/Pages/RegionFilesPage.xaml / Border | {"Visibility": "{Binding RegionGuide.ShowTopRegionActions, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0568 | Views/Pages/RegionFilesPage.xaml / 检查当前区服文件状态 | {"IsEnabled": "{Binding CanCheckRegionFiles}", "Click": "OnCheckCurrentFiles"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0569 | Views/Pages/RegionFilesPage.xaml / {Binding RegionFileCheckSummary} | {"Text": "{Binding RegionFileCheckSummary}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0570 | Views/Pages/RegionFilesPage.xaml / 查看详情 | {"Visibility": "{Binding HasRegionFileCheck, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0571 | Views/Pages/RegionFilesPage.xaml / {Binding RegionFileCheckDetails} | {"Text": "{Binding RegionFileCheckDetails}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0572 | Views/Pages/RegionFilesPage.xaml / 清除临时文件 | {"IsEnabled": "{Binding CanClearTemporaryFiles}", "Click": "OnClearTemporary"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0573 | Views/Pages/RegionFilesPage.xaml / 重设当前区服状态 | {"IsEnabled": "{Binding CanResetCurrentRegion}", "Click": "OnResetCurrentRegion"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0574 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.GamePathText} | {"Text": "{Binding RegionGuide.GamePathText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0575 | Views/Pages/RegionFilesPage.xaml / 自动查找 | {"IsEnabled": "{Binding RegionGuide.CanChangePaths}", "Click": "OnDetectGame"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0576 | Views/Pages/RegionFilesPage.xaml / 更改位置 | {"IsEnabled": "{Binding RegionGuide.CanChangePaths}", "Click": "OnChooseGame"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0577 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.BackupPathText} | {"Text": "{Binding RegionGuide.BackupPathText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0578 | Views/Pages/RegionFilesPage.xaml / 更改位置 | {"IsEnabled": "{Binding RegionGuide.CanChangePaths}", "Click": "OnChooseStorage"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0579 | Views/Pages/RegionFilesPage.xaml / 打开备份文件夹 | {"Click": "OnOpenStorage"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0580 | Views/Pages/RegionFilesPage.xaml / VerifiedModeRadio | {"IsEnabled": "{Binding RegionGuide.CanChangePaths}", "IsChecked": "{Binding IsVerifiedDifferenceMode, Mode=OneWay}", "Click": "OnVerifiedMode"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0581 | Views/Pages/RegionFilesPage.xaml / FullModeRadio | {"IsEnabled": "{Binding RegionGuide.CanChangePaths}", "IsChecked": "{Binding IsFullSnapshotMode, Mode=OneWay}", "Click": "OnFullMode"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0582 | Views/Pages/RegionFilesPage.xaml / StackPanel | {"Visibility": "{Binding RegionGuide.ShowNotPrepared, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0583 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.StepText} | {"Text": "{Binding RegionGuide.StepText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0584 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Title} | {"Text": "{Binding RegionGuide.Title}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0585 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Description} | {"Text": "{Binding RegionGuide.Description}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0586 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide} | {"Content": "{Binding RegionGuide}", "Visibility": "{Binding RegionGuide.ShowPreparationWarning, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0587 | Views/Pages/RegionFilesPage.xaml / Border | {"Visibility": "{Binding RegionGuide.ShowGamePathRequired, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0588 | Views/Pages/RegionFilesPage.xaml / 当前游戏是国服 | {"IsEnabled": "{Binding RegionGuide.CanChooseCurrentRegion}", "Click": "OnCaptureChina"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0589 | Views/Pages/RegionFilesPage.xaml / 当前游戏是国际服 | {"IsEnabled": "{Binding RegionGuide.CanChooseCurrentRegion}", "Click": "OnCaptureInternational"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0590 | Views/Pages/RegionFilesPage.xaml / StackPanel | {"Visibility": "{Binding RegionGuide.ShowWaiting, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0591 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.FirstStepCompleteText} | {"Text": "{Binding RegionGuide.FirstStepCompleteText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0592 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.StepText} | {"Text": "{Binding RegionGuide.StepText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0593 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Title} | {"Text": "{Binding RegionGuide.Title}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0594 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.PreparationRouteText} | {"Text": "{Binding RegionGuide.PreparationRouteText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0595 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Description} | {"Text": "{Binding RegionGuide.Description}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0596 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide} | {"Content": "{Binding RegionGuide}", "Visibility": "{Binding RegionGuide.ShowPreparationWarning, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0597 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.ContinueButtonText} | {"Content": "{Binding RegionGuide.ContinueButtonText}", "IsEnabled": "{Binding RegionGuide.CanContinueOtherRegion}", "Click": "OnComplete"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0598 | Views/Pages/RegionFilesPage.xaml / 重新执行步骤 1 | {"IsEnabled": "{Binding RegionGuide.CanRedoStep1}", "Visibility": "{Binding RegionGuide.CanRedoStep1, Converter={StaticResource BoolVisibility}}", "Click": "OnRedoStep1"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0599 | Views/Pages/RegionFilesPage.xaml / 重新执行步骤 2 | {"IsEnabled": "{Binding RegionGuide.CanRedoStep2}", "Visibility": "{Binding RegionGuide.CanRedoStep2, Converter={StaticResource BoolVisibility}}", "Click": "OnRedoStep2"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0600 | Views/Pages/RegionFilesPage.xaml / 返回步骤 1 | {"IsEnabled": "{Binding RegionGuide.CanReturnToStep1}", "Visibility": "{Binding RegionGuide.CanReturnToStep1, Converter={StaticResource BoolVisibility}}", "Click": "OnReturnStep1"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0601 | Views/Pages/RegionFilesPage.xaml / 重新开始准备 | {"IsEnabled": "{Binding RegionGuide.CanRestart}", "Click": "OnReprepare"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0602 | Views/Pages/RegionFilesPage.xaml / StackPanel | {"Visibility": "{Binding RegionGuide.ShowProgress, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0603 | Views/Pages/RegionFilesPage.xaml / StackPanel | {"Visibility": "{Binding RegionGuide.ShowCompletedPreparationSteps, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0604 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.FirstStepCompleteText} | {"Text": "{Binding RegionGuide.FirstStepCompleteText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0605 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.SecondStepCompleteText} | {"Text": "{Binding RegionGuide.SecondStepCompleteText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0606 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.StepText} | {"Text": "{Binding RegionGuide.StepText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0607 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Title} | {"Text": "{Binding RegionGuide.Title}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0608 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Description} | {"Text": "{Binding RegionGuide.Description}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0609 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide} | {"Content": "{Binding RegionGuide}", "Visibility": "{Binding RegionGuide.ShowPreparationWarning, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0610 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.ProgressText} | {"Text": "{Binding RegionGuide.ProgressText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0611 | Views/Pages/RegionFilesPage.xaml / ProgressBar | {"Maximum": "{Binding RegionGuide.ProgressTotal, Mode=OneWay}", "Value": "{Binding RegionGuide.ProgressCurrent, Mode=OneWay}", "IsIndeterminate": "{Binding RegionGuide.ProgressIndeterminate, Mode=OneWay}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0612 | Views/Pages/RegionFilesPage.xaml / 取消 | {"IsEnabled": "{Binding RegionGuide.CanCancel}", "Visibility": "{Binding RegionGuide.CanCancel, Converter={StaticResource BoolVisibility}}", "Click": "OnCancel"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0613 | Views/Pages/RegionFilesPage.xaml / StackPanel | {"Visibility": "{Binding RegionGuide.ShowReady, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0614 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Title} | {"Text": "{Binding RegionGuide.Title}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0615 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Description} | {"Text": "{Binding RegionGuide.Description}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0616 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.ChinaBackupText} | {"Text": "{Binding RegionGuide.ChinaBackupText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0617 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.InternationalBackupText} | {"Text": "{Binding RegionGuide.InternationalBackupText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0618 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.DifferenceText} | {"Text": "{Binding RegionGuide.DifferenceText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0619 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.BackupBytesText} | {"Text": "{Binding RegionGuide.BackupBytesText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0620 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.WarningSummaryText} | {"Header": "{Binding RegionGuide.WarningSummaryText}", "Visibility": "{Binding RegionGuide.ShowWarningDetails, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0621 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.WarningDetailsText} | {"Text": "{Binding RegionGuide.WarningDetailsText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0622 | Views/Pages/RegionFilesPage.xaml / 重新准备区服文件 | {"IsEnabled": "{Binding RegionGuide.CanRestart}", "Click": "OnReprepare"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0623 | Views/Pages/RegionFilesPage.xaml / 高级操作 | {} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0624 | Views/Pages/RegionFilesPage.xaml / 打开备份文件夹 | {"Click": "OnOpenStorage"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0625 | Views/Pages/RegionFilesPage.xaml / StackPanel | {"Visibility": "{Binding RegionGuide.ShowOutdated, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0626 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.StepText} | {"Text": "{Binding RegionGuide.StepText}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0627 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Title} | {"Text": "{Binding RegionGuide.Title}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0628 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Description} | {"Text": "{Binding RegionGuide.Description}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0629 | Views/Pages/RegionFilesPage.xaml / 重新准备区服文件 | {"IsEnabled": "{Binding RegionGuide.CanRestart}", "Click": "OnReprepare"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0630 | Views/Pages/RegionFilesPage.xaml / StackPanel | {"Visibility": "{Binding RegionGuide.ShowMixed, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0631 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Title} | {"Text": "{Binding RegionGuide.Title}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0632 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Description} | {"Text": "{Binding RegionGuide.Description}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0633 | Views/Pages/RegionFilesPage.xaml / 恢复到国服 | {"IsEnabled": "{Binding RegionGuide.CanRestore}", "Click": "OnSwitchChina"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0634 | Views/Pages/RegionFilesPage.xaml / 恢复到国际服 | {"IsEnabled": "{Binding RegionGuide.CanRestore}", "Click": "OnSwitchInternational"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0635 | Views/Pages/RegionFilesPage.xaml / 重新准备 | {"IsEnabled": "{Binding RegionGuide.CanRestart}", "Click": "OnReprepare"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0636 | Views/Pages/RegionFilesPage.xaml / StackPanel | {"Visibility": "{Binding RegionGuide.ShowError, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0637 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Title} | {"Text": "{Binding RegionGuide.Title}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0638 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Description} | {"Text": "{Binding RegionGuide.Description}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0639 | Views/Pages/RegionFilesPage.xaml / 重试 | {"IsEnabled": "{Binding RegionGuide.CanRetry}", "Click": "OnRetry"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0640 | Views/Pages/RegionFilesPage.xaml / 重新执行步骤 2 | {"IsEnabled": "{Binding RegionGuide.CanRedoStep2}", "Visibility": "{Binding RegionGuide.CanRedoStep2, Converter={StaticResource BoolVisibility}}", "Click": "OnRedoStep2"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0641 | Views/Pages/RegionFilesPage.xaml / 返回步骤 1 | {"IsEnabled": "{Binding RegionGuide.CanReturnToStep1}", "Visibility": "{Binding RegionGuide.CanReturnToStep1, Converter={StaticResource BoolVisibility}}", "Click": "OnReturnStep1"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0642 | Views/Pages/RegionFilesPage.xaml / 改用完整备份 | {"IsEnabled": "{Binding RegionGuide.CanUseFullSnapshot}", "Visibility": "{Binding RegionGuide.CanUseFullSnapshot, Converter={StaticResource BoolVisibility}}", "Click": "OnUseFullSnapshot"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0643 | Views/Pages/RegionFilesPage.xaml / 打开日志文件夹 | {"Click": "OnOpenLogs"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0644 | Views/Pages/RegionFilesPage.xaml / 重新准备区服文件 | {"IsEnabled": "{Binding RegionGuide.CanRestart}", "Click": "OnReprepare"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0645 | Views/Pages/RegionFilesPage.xaml / Border | {"Visibility": "{Binding RegionGuide.ShowNotice, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0646 | Views/Pages/RegionFilesPage.xaml / DataTrigger | {"Binding": "{Binding RegionGuide.ShowSuccessNotice}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0647 | Views/Pages/RegionFilesPage.xaml / {Binding RegionGuide.Notice} | {"Text": "{Binding RegionGuide.Notice}"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0648 | Views/Pages/RegionFilesPage.xaml / 删除所有区服备份 | {"IsEnabled": "{Binding RegionGuide.CanClear}", "Click": "OnClear"} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0649 | Views/Pages/RegionFilesPage.xaml / 为什么需要准备？ | {} | 原契约保留 · Views/Pages/RegionFilesPage.xaml | 静态；运行验证按测试记录 |
| UI-0650 | Views/Pages/RegionFilesPage.xaml.cs / if (preview.ShowDialog() == true) | {} | 原契约保留 · Views/Pages/RegionFilesPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0651 | Views/Pages/RegionFilesPage.xaml.cs / MessageBox.Show("未能可靠找到守望先锋安装目录，请手动选择。", "自动查找", | {} | 原契约保留 · Views/Pages/RegionFilesPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0652 | Views/Pages/RegionFilesPage.xaml.cs / }.ShowDialog() == true; | {} | 原契约保留 · Views/Pages/DropsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0653 | Views/Pages/RegionFilesPage.xaml.cs / { Owner = Window.GetWindow(this) }.ShowDialog() == true) | {} | 原契约保留 · Views/Pages/RegionFilesPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0654 | Views/Pages/RegionFilesPage.xaml.cs / { Owner = Window.GetWindow(this) }.ShowDialog() == true) | {} | 原契约保留 · Views/Pages/RegionFilesPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0655 | Views/Pages/RegionFilesPage.xaml.cs / { Owner = Window.GetWindow(this) }.ShowDialog() != true) | {} | 原契约保留 · Views/Pages/RegionFilesPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0656 | Views/Pages/RegionFilesPage.xaml.cs / { Owner = Window.GetWindow(this) }.ShowDialog() == true) | {} | 原契约保留 · Views/Pages/RegionFilesPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0657 | Views/Pages/RegionFilesPage.xaml.cs / if (MessageBox.Show( | {} | 原契约保留 · Views/Pages/RegionFilesPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0658 | Views/Pages/RegionFilesPage.xaml.cs / if (MessageBox.Show( | {} | 原契约保留 · Views/Pages/RegionFilesPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0659 | Views/Pages/SettingsPage.xaml / CloseToTrayBox | {"Checked": "OnSettingChanged", "Unchecked": "OnSettingChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0660 | Views/Pages/SettingsPage.xaml / StartMinimizedBox | {"Checked": "OnSettingChanged", "Unchecked": "OnSettingChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0661 | Views/Pages/SettingsPage.xaml / StartupBox | {"Checked": "OnSettingChanged", "Unchecked": "OnSettingChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0662 | Views/Pages/SettingsPage.xaml / DarkModeBox | {"Checked": "OnThemeChanged", "Unchecked": "OnThemeChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0663 | Views/Pages/SettingsPage.xaml / 自动检测 | {"Click": "OnAutoDetect"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0664 | Views/Pages/SettingsPage.xaml / 更改 | {"Click": "OnChooseExe"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0665 | Views/Pages/SettingsPage.xaml / EnableProxyBox | {"Checked": "OnProxyInputChanged", "Unchecked": "OnProxyInputChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0666 | Views/Pages/SettingsPage.xaml / ProxyUrlBox | {"TextChanged": "OnProxyInputChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0667 | Views/Pages/SettingsPage.xaml / FallbackDirectBox | {"Checked": "OnProxyInputChanged", "Unchecked": "OnProxyInputChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0668 | Views/Pages/SettingsPage.xaml / 保存代理设置 | {"Click": "OnSaveProxy"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0669 | Views/Pages/SettingsPage.xaml / NetworkTestButton | {"Click": "OnTestNetwork"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0670 | Views/Pages/SettingsPage.xaml / CopyDiagnosticButton | {"Click": "OnCopyDiagnostic"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0671 | Views/Pages/SettingsPage.xaml / AnnouncementBadgeBox | {"Checked": "OnAnnouncementBadgeChanged", "Unchecked": "OnAnnouncementBadgeChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0672 | Views/Pages/SettingsPage.xaml / EnableWindowsNotificationsBox | {"Checked": "OnNotificationSettingChanged", "Unchecked": "OnNotificationSettingChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0673 | Views/Pages/SettingsPage.xaml / NotifyRegionSwitchBox | {"Checked": "OnNotificationSettingChanged", "Unchecked": "OnNotificationSettingChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0674 | Views/Pages/SettingsPage.xaml / NotifyDropsBox | {"Checked": "OnNotificationSettingChanged", "Unchecked": "OnNotificationSettingChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0675 | Views/Pages/SettingsPage.xaml / NotifyUpdatesBox | {"Checked": "OnNotificationSettingChanged", "Unchecked": "OnNotificationSettingChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0676 | Views/Pages/SettingsPage.xaml / NotifyAnnouncementsBox | {"Checked": "OnNotificationSettingChanged", "Unchecked": "OnNotificationSettingChanged"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0677 | Views/Pages/SettingsPage.xaml / CheckUpdateButton | {"Click": "OnCheckUpdate"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0678 | Views/Pages/SettingsPage.xaml / OpenUpdateLinkButton | {"Click": "OnOpenUpdateLink"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0679 | Views/Pages/SettingsPage.xaml / OnlineUpdateButton | {"Click": "OnOnlineUpdate"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0680 | Views/Pages/SettingsPage.xaml / 恢复此版本提示 | {"Click": "OnRestoreSkippedUpdate"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0681 | Views/Pages/SettingsPage.xaml / 打开数据文件夹 | {"Click": "OnOpenData"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0682 | Views/Pages/SettingsPage.xaml / 清理战绩缓存 | {"Click": "OnClearCache"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0683 | Views/Pages/SettingsPage.xaml.cs / MessageBox.Show(error, "网络代理", MessageBoxButton.OK, MessageBoxImage.Warning); | {} | 原契约保留 · Views/Pages/SettingsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0684 | Views/Pages/SettingsPage.xaml.cs / MessageBox.Show($"当前版本：{_vm.UpdateChecks.CurrentVersion}", "已是最新版本", | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0685 | Views/Pages/SettingsPage.xaml.cs / MessageBox.Show("当前没有可用的正式版本更新。", "软件更新", | {} | 原契约保留 · Views/Pages/SettingsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0686 | Views/Pages/SettingsPage.xaml.cs / MessageBox.Show(outcome.Result?.ErrorMessage ?? "暂时无法连接更新服务器。", "暂时无法检查更新", | {} | 原契约保留 · Views/MainWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0687 | Views/Pages/SettingsPage.xaml.cs / MessageBox.Show(ex.Message, "在线更新失败", MessageBoxButton.OK, MessageBoxImage.Warning); | {} | 原契约保留 · Views/Pages/SettingsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0688 | Views/Pages/SnapshotsPage.xaml / DataTrigger | {"Binding": "{Binding State}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0689 | Views/Pages/SnapshotsPage.xaml / DataTrigger | {"Binding": "{Binding State}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0690 | Views/Pages/SnapshotsPage.xaml / DataTrigger | {"Binding": "{Binding State}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0691 | Views/Pages/SnapshotsPage.xaml / DataTrigger | {"Binding": "{Binding State}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0692 | Views/Pages/SnapshotsPage.xaml / DataTrigger | {"Binding": "{Binding State}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0693 | Views/Pages/SnapshotsPage.xaml / {Binding ModeText} | {"Text": "{Binding ModeText}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0694 | Views/Pages/SnapshotsPage.xaml / Border | {"Visibility": "{Binding ActiveVisibility}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0695 | Views/Pages/SnapshotsPage.xaml / {Binding SourceText, Mode=OneWay} | {"Text": "{Binding SourceText, Mode=OneWay}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0696 | Views/Pages/SnapshotsPage.xaml / {Binding TargetText, Mode=OneWay} | {"Text": "{Binding TargetText, Mode=OneWay}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0697 | Views/Pages/SnapshotsPage.xaml / {Binding StateText} | {"Text": "{Binding StateText}"} | 原契约保留 · Controls/RewardHero.xaml | 静态；运行验证按测试记录 |
| UI-0698 | Views/Pages/SnapshotsPage.xaml / {Binding StateReason} | {"Text": "{Binding StateReason}", "Visibility": "{Binding StateReasonVisibility}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0699 | Views/Pages/SnapshotsPage.xaml / {Binding FileCountText} | {"Text": "{Binding FileCountText}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0700 | Views/Pages/SnapshotsPage.xaml / {Binding SizeText} | {"Text": "{Binding SizeText}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0701 | Views/Pages/SnapshotsPage.xaml / {Binding CreatedText} | {"Text": "{Binding CreatedText}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0702 | Views/Pages/SnapshotsPage.xaml / {Binding UsedText} | {"Text": "{Binding UsedText}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0703 | Views/Pages/SnapshotsPage.xaml / 查看详情 | {"Click": "OnSnapshotAction"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0704 | Views/Pages/SnapshotsPage.xaml / 重新生成 | {"Click": "OnSnapshotAction"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0705 | Views/Pages/SnapshotsPage.xaml / 打开目录 | {"Click": "OnSnapshotAction"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0706 | Views/Pages/SnapshotsPage.xaml / 删除 | {"Click": "OnSnapshotAction"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0707 | Views/Pages/SnapshotsPage.xaml / 刷新 | {"Click": "OnRefresh"} | 原契约保留 · Views/MainWindow.xaml | 静态；运行验证按测试记录 |
| UI-0708 | Views/Pages/SnapshotsPage.xaml / {Binding SnapshotCount, StringFormat=共 {0} 个快照} | {"Text": "{Binding SnapshotCount, StringFormat=共 {0} 个快照}"} | 结构化可视化替代：SnapshotCount → SnapshotCount · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0709 | Views/Pages/SnapshotsPage.xaml / {Binding StatusText} | {"Text": "{Binding StatusText}"} | 原契约保留 · Views/Dashboard/DashboardView.xaml | 静态；运行验证按测试记录 |
| UI-0710 | Views/Pages/SnapshotsPage.xaml / {Binding SnapshotCount, StringFormat=共 {0} 个} | {"Text": "{Binding SnapshotCount, StringFormat=共 {0} 个}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0711 | Views/Pages/SnapshotsPage.xaml / SnapshotsList | {"ItemsSource": "{Binding Items}", "SelectedItem": "{Binding SelectedSnapshot, Mode=TwoWay}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0712 | Views/Pages/SnapshotsPage.xaml / DataTrigger | {"Binding": "{Binding Items.Count}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0713 | Views/Pages/SnapshotsPage.xaml / SnapshotDetailsPanel | {"Visibility": "{Binding HasSelectedSnapshot, Converter={StaticResource BoolVisibility}}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0714 | Views/Pages/SnapshotsPage.xaml / StackPanel | {"DataContext": "{Binding SelectedSnapshot}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0715 | Views/Pages/SnapshotsPage.xaml / 关闭 | {"Click": "OnCloseDetails"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0716 | Views/Pages/SnapshotsPage.xaml / {Binding ModeText} | {"Text": "{Binding ModeText}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0717 | Views/Pages/SnapshotsPage.xaml / {Binding StateText} | {"Text": "{Binding StateText}"} | 原契约保留 · Controls/RewardHero.xaml | 静态；运行验证按测试记录 |
| UI-0718 | Views/Pages/SnapshotsPage.xaml / {Binding SourceText, Mode=OneWay} | {"Text": "{Binding SourceText, Mode=OneWay}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0719 | Views/Pages/SnapshotsPage.xaml / {Binding TargetText, Mode=OneWay} | {"Text": "{Binding TargetText, Mode=OneWay}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0720 | Views/Pages/SnapshotsPage.xaml / {Binding GenerationId} | {"Text": "{Binding GenerationId}", "ToolTip": "{Binding GenerationId}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0721 | Views/Pages/SnapshotsPage.xaml / {Binding StateText} | {"Text": "{Binding StateText}"} | 原契约保留 · Controls/RewardHero.xaml | 静态；运行验证按测试记录 |
| UI-0722 | Views/Pages/SnapshotsPage.xaml / {Binding StateReason} | {"Text": "{Binding StateReason}", "Visibility": "{Binding StateReasonVisibility}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0723 | Views/Pages/SnapshotsPage.xaml / {Binding FileCountText} | {"Text": "{Binding FileCountText}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0724 | Views/Pages/SnapshotsPage.xaml / {Binding SizeText} | {"Text": "{Binding SizeText}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0725 | Views/Pages/SnapshotsPage.xaml / {Binding RootPath} | {"Text": "{Binding RootPath}", "ToolTip": "{Binding RootPath}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0726 | Views/Pages/SnapshotsPage.xaml / {Binding CreatedText} | {"Text": "{Binding CreatedText}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0727 | Views/Pages/SnapshotsPage.xaml / {Binding UsedText} | {"Text": "{Binding UsedText}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0728 | Views/Pages/SnapshotsPage.xaml / DataTrigger | {"Binding": "{Binding HasSelectedSnapshot}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0729 | Views/Pages/SnapshotsPage.xaml / DataTrigger | {"Binding": "{Binding Items.Count}"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0730 | Views/Pages/SnapshotsPage.xaml / 前往区服切换 | {"Click": "OnOpenRegion"} | 原契约保留 · Views/Pages/SnapshotsPage.xaml | 静态；运行验证按测试记录 |
| UI-0731 | Views/Pages/SnapshotsPage.xaml.cs / if (MessageBox.Show("将返回区服准备流程，重新生成快照不会直接删除游戏文件。是否继续？", "重新生成快照", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) | {} | 原契约保留 · Views/Pages/SnapshotsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0732 | Views/Pages/SnapshotsPage.xaml.cs / if (MessageBox.Show($"确定删除此 CloudLight Blizzard 快照？\n\n模式：{item.ModeText}\n文件：{item.FileCountText}\n大小：{item.SizeText}\n\n只会删除受管理的快照路径，不会删除游戏文件。", "删除快照", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) | {} | 原契约保留 · Views/Pages/SnapshotsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0733 | Views/PlatformErrorWindow.xaml / 查看日志 | {"Click": "OnViewLogs"} | 原契约保留 · Views/PlatformErrorWindow.xaml | 静态；运行验证按测试记录 |
| UI-0734 | Views/PlatformErrorWindow.xaml / 关闭 | {} | 原契约保留 · Views/PlatformErrorWindow.xaml | 静态；运行验证按测试记录 |
| UI-0735 | Views/RegionActionConfirmWindow.xaml / Grid | {"MouseLeftButtonDown": "OnDrag"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0736 | Views/RegionActionConfirmWindow.xaml / × | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0737 | Views/RegionActionConfirmWindow.xaml / 取消 | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0738 | Views/RegionActionConfirmWindow.xaml / ConfirmButton | {"Click": "OnConfirm"} | 原契约保留 · Views/RegionActionConfirmWindow.xaml | 静态；运行验证按测试记录 |
| UI-0739 | Views/RegionSwitchChoiceWindow.xaml / 取消 | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0740 | Views/RegionSwitchChoiceWindow.xaml / 仅切换账号 | {"Click": "OnAccountOnly"} | 原契约保留 · Views/RegionSwitchChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0741 | Views/RegionSwitchChoiceWindow.xaml / 前往区服文件 | {"Click": "OnSettings"} | 原契约保留 · Views/RegionSwitchChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0742 | Views/RegionSwitchChoiceWindow.xaml.cs / public RegionSwitchChoice ShowDialogChoice() { ShowDialog(); return _choice; } | {} | 原契约保留 · Views/RegionSwitchChoiceWindow.xaml.cs | 静态；运行验证按测试记录 |
| UI-0743 | Views/SnapshotConfirmWindow.xaml / Grid | {"MouseLeftButtonDown": "OnDrag"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0744 | Views/SnapshotConfirmWindow.xaml / Button | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0745 | Views/SnapshotConfirmWindow.xaml / ConfirmedLoginBox | {} | 原契约保留 · Views/SnapshotConfirmWindow.xaml | 静态；运行验证按测试记录 |
| UI-0746 | Views/SnapshotConfirmWindow.xaml / 取消 | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0747 | Views/SnapshotConfirmWindow.xaml / Button | {"Click": "OnConfirm"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0748 | Views/SoopNoticeWindow.xaml / 取消 | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0749 | Views/SoopNoticeWindow.xaml / ConfirmButton | {"Click": "OnConfirm"} | 原契约保留 · Views/RegionActionConfirmWindow.xaml | 静态；运行验证按测试记录 |
| UI-0750 | Views/SwitchPreviewWindow.xaml / Grid | {"MouseLeftButtonDown": "OnDrag"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0751 | Views/SwitchPreviewWindow.xaml / × | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0752 | Views/SwitchPreviewWindow.xaml / 取消 | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0753 | Views/SwitchPreviewWindow.xaml / StartButton | {"Click": "OnStart"} | 原契约保留 · Views/SwitchPreviewWindow.xaml | 静态；运行验证按测试记录 |
| UI-0754 | Views/UpdateDialog.xaml / Grid | {"MouseLeftButtonDown": "OnDrag"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
| UI-0755 | Views/UpdateDialog.xaml / CloseButton | {"Click": "OnLater"} | 原契约保留 · Views/UpdateDialog.xaml | 静态；运行验证按测试记录 |
| UI-0756 | Views/UpdateDialog.xaml / SkipVersionBox | {} | 原契约保留 · Views/UpdateDialog.xaml | 静态；运行验证按测试记录 |
| UI-0757 | Views/UpdateDialog.xaml / CancelDownloadButton | {"Click": "OnCancelDownload"} | 原契约保留 · Views/UpdateDialog.xaml | 静态；运行验证按测试记录 |
| UI-0758 | Views/UpdateDialog.xaml / LaterButton | {"Click": "OnLater"} | 原契约保留 · Views/UpdateDialog.xaml | 静态；运行验证按测试记录 |
| UI-0759 | Views/UpdateDialog.xaml / OpenReleaseButton | {"Click": "OnOpenRelease"} | 原契约保留 · Views/UpdateDialog.xaml | 静态；运行验证按测试记录 |
| UI-0760 | Views/UpdateDialog.xaml / OnlineUpdateButton | {"Click": "OnOnlineUpdate"} | 原契约保留 · Views/Pages/SettingsPage.xaml | 静态；运行验证按测试记录 |
| UI-0761 | Views/UpdateDialog.xaml.cs / MessageBox.Show(ex.Message, "在线更新失败", MessageBoxButton.OK, MessageBoxImage.Warning); | {} | 原契约保留 · Views/Pages/SettingsPage.xaml.cs | 静态；运行验证按测试记录 |
| UI-0762 | Views/UploadStalledWindow.xaml / 继续等待 | {"Click": "OnWait"} | 原契约保留 · Views/UploadStalledWindow.xaml | 静态；运行验证按测试记录 |
| UI-0763 | Views/UploadStalledWindow.xaml / 取消上传 | {"Click": "OnCancel"} | 原契约保留 · Views/CloseChoiceWindow.xaml | 静态；运行验证按测试记录 |
