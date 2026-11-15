# CloudLight Blizzard Fluent 前端重构

## 基线与边界

- 起点：813e6deda6ded9be8d76392594e38e859875738d；分支 ui/fluent-redesign。
- 本地 origin 与用户给出的仓库 HEAD 一致。保留原有未跟踪 artifacts/。
- 四张附件均已读取。图 1 用于整体外壳、卡片和留白；图 2 用于授权状态及平台配置；图 3 用于平台内功能分层；图 4 用于账号、进度、设置行的一致性。
- 图片中的认证、统计、奖励、频道、二维码均不作为业务数据。
- 保留全部 Core、协议、领取策略、凭据保存、区服安全检查和设置序列化语义。仅新增展示状态、导航与隔离验收入口。

## 导航与布局

一级：总览、账号管理、区服切换、战绩、掉宝管理、区服快照、诊断中心、设置、关于。

掉宝二级：SOOP、Twitch、Bilibili、YouTube。各平台使用独立的功能标签，以用途组织现有控件。账号/概览、活动与奖励、观看配置、维护与网络、日志；标签按平台适配。

设置：常规与外观、启动与退出、网络代理、通知、数据与存储、更新、反馈。

主窗口保留一个页面宿主与缓存的页面实例；切页不重建配置输入、不丢失草稿。掉宝页固定页头与平台导航，内容区单一垂直滚动；长列表内部滚动沿用现有滚轮转发。

## 设计资源

背景 #F5F7FB、卡片 #FFFFFF、强调 #635BEE、正文 #172033、次级 #657084、边框 #E4E8F1。

侧栏 210 DIP；卡片圆角 14；控件圆角 8；按钮 38 DIP；间距 8/12/16/24；标题 26；正文 13。中文使用 Microsoft YaHei UI，图标使用现有矢量图标与 Segoe Fluent Icons。共享 DynamicResource 支持深色切换。

组件：PageHeader、SectionCard、SettingRow、StatusBadge、EmptyState、ProgressCard、FormField、FluentTabs、Primary/Secondary/Danger/IconButton、ToggleSwitch；已有确认窗口继续保留安全语义。

## 状态真实性

展示沿用 ViewModel/Host 的 Session、授权、重试、配置损坏、进度与 Worker 状态；官方进度和本地观察分开。未登录隐藏空奖励区，仍可访问配置、网络和帮助。授权码与有效期沿用真实事件状态。

## 验收约束

使用隔离 AppPaths 根与请求替身，不读取用户凭据，不真实领取、不切账号、不覆盖游戏文件，不进行 YouTube 登录。可视化模拟必须显式显示“隔离验收 · 模拟数据”。真实窗口截图与 WPF 渲染快照分别记录，不混称。
