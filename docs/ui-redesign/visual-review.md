# 真实界面检查

四张附件在设计及编写代码前均已读取，以浅色背景、蓝紫强调、左侧导航、清晰标题、卡片、留白和平台分组作为共同基准。图片中的账号、奖励和授权码不进入生产数据。

## Computer Use 实际窗口截图

目录：`D:\code\CloudLight Blizzard\CloudLight Blizzard\test-output\ui-redesign\actual-screenshots`。

以下为开发过程中真实打开 WPF 窗口后保存的桌面截图，窗口约 1267×814 像素，物理 DPI 125%。所有账号、奖励、授权及二维码均为隔离模拟数据，窗口标题和顶部横幅明确标记。每个页面均做过人工视觉复核；它们不是设计稿，也不是真实登录或领取证据。

| 文件 | 页面/状态 | 复核重点 |
|---|---|---|
| 01-overview.png | 总览 | 账号、游戏路径与平台状态分组，快捷入口 |
| 02-accounts.png | Battle.net 账号管理 | 当前账号、账号操作、备份入口 |
| 03-region.png | 区服切换 | 当前状态、步骤、路径与恢复分组 |
| 04-stats.png | 战绩 | 查询空态、指标区域、响应式列 |
| 05-snapshots.png | 区服快照 | 无快照空态、创建入口、列表区域 |
| 06-twitch-signed-out.png | Twitch 未登录 | 一个登录主操作，帮助与配置入口 |
| 07-twitch-authorization.png | Twitch 设备授权 | 完整模拟码、倒计时、复制与取消，不重复登录按钮 |
| 08-twitch-signed-in.png | Twitch 已登录 | Session 状态与开始观看入口 |
| 09-twitch-watching.png | Twitch 正在观看 | 官方进度、本地估算与观看请求证据分别呈现 |
| 10-twitch-settings.png | Twitch 设置 | 设置分区、固定保存区、游戏与自动化入口 |
| 11-soop-accounts.png | SOOP 多账号 | 主账号、选中账号操作、独立运行状态 |
| 12-soop-settings.png | SOOP 设置 | 任务策略、刷新恢复与保存 |
| 13-bilibili-login.png | Bilibili 登录 | 扫码/过期/取消入口、敏感导入分层 |
| 14-bilibili-settings.png | Bilibili 设置 | 观看模式、并发、重连与任务参数 |
| 15-youtube.png | YouTube | 账号、频道、浏览器配置与历史入口 |
| 16-diagnostics.png | 诊断中心 | 已完成诊断分级、可见网络故障、折叠技术详情 |
| 17-settings.png | 全局设置 | 用途分类、代理与保存机制 |
| 18-dark-theme.png | Bilibili 深色 | 文字、输入框、导航和保存区对比度 |

截图是各轮视觉检查的记录，未全部在最终源码提交后重新拍摄。01 的 Battle.net 进程文案与 18 的验收横幅底色保留了较早版本；对应修正以最终候选的 WPF 渲染为准，不能把历史截图冒充最终逐页重新拍摄。

收尾时 Computer Use 对测试窗口的应用归属识别异常，返回了 Codex 或局部战绩画面。没有将这些错位画面保存为验收截图，也没有依据错误画面继续点击。最终候选的新增桌面补拍因该工具问题未完成。

## 最终候选 WPF 渲染

目录：`D:\code\CloudLight Blizzard\CloudLight Blizzard\test-output\ui-redesign-release-candidate\validation\presentation\wpf-renders`。

`01.png`–`40.png` 与 test-matrix.md 场景顺序一致，另有深色 `dark.png` 及关键页 `*-1366-150.png`。它们由实际创建并显示的 WPF 窗口视觉树渲染，属于运行渲染快照，**不标记为 Computer Use 桌面截图**。

最终重点人工复核：`37.png` 的 Bilibili 自动化 Switch 与通知分组、`06-1366-150.png` 的 Twitch 登录首屏可达性、`18.png` 的配置异常状态、`dark.png` 的观看进度和深色对比度。未见重叠或横向裁切；长内容保留垂直滚动。

## 已修正并复查

- 授权中收起普通登录卡，保留一个完整授权流程。
- 未登录 Twitch/Bilibili 不展示观看主入口；无快照时收起空白详情区域。
- 小高度窗口压缩页头和导航间距，1366×768@150% 等效 DIP 首屏可以看到 Twitch 登录按钮。
- Bilibili 自动化使用统一 Switch，保存区固定在内容区底部。
- 调整响应式重排的尺寸刷新，长平台错误文本换行，侧栏选中项滚动到可见区域。
- 深色验收横幅改用动态资源；保留页面内正常深色控件对比度。

物理 100%/150% DPI、解除占用后的系统剪贴板操作以及 Twitch 奖励图片仍属待补项目，见 REPORT.md。
