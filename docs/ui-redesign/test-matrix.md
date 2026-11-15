# 验收矩阵

最终程序源码提交：`c6ad4cfc5b8dfdf15b9faa2ec179de95178b8460`。全部自动测试使用隔离目录；未操作用户的真实账号、奖励、游戏目录或 Session。

权威结果根目录：`D:\code\CloudLight Blizzard\CloudLight Blizzard\test-output\ui-redesign-release-candidate\validation`。此前其他候选目录不作为最终构建结果。

| 检查 | 结果 | 证据与边界 |
|---|---|---|
| WPF Release publish | PASS，退出码 0 | `publish.log`，无编译错误或警告；net8.0-windows10.0.18362.0 |
| FeatureSelfTest | PASS | `feature/feature-selftest.txt`，`OVERALL: PASS`；含账号/区服/快照、原子持久化、更新退出顺序、诊断脱敏 |
| UI / Drops Host 集成回归 | 345 PASS，0 FAIL，1 BLOCKED | `ui.txt`；整套状态是 BLOCKED，不能称全 PASS |
| 剪贴板真实写入与读回 | BLOCKED | 其他程序占用，HRESULT 0x800401D0；已重试，未强行解除外部占用 |
| Dispatcher 与绑定 | PASS，绑定错误 0 | `ui.txt` 与 `presentation/binding-errors.txt`；覆盖真实 WPF 列表、命令、快照与 Host 回调 |
| 设置持久化 | PASS（隔离数据） | Feature TEST 9：原子保存、备份恢复、不被默认值覆盖；UI 保存命令链与草稿保留 |
| 平台草稿 | PASS | Twitch、SOOP、YouTube 跨平台切换和刷新保留；Bilibili 原 dirty/保存机制继续使用 |
| Twitch 保存 | PASS | 实际 WPF Click 进入原 `save_settings` 命令，传输端使用替身；不等于真实授权后的服务器行为 |
| 授权取消竞争 | PASS | 停止或取消后，在途设备码重新获取不会重新启动登录 |
| 快速入口 | PASS | YouTube 频道入口转到活动；SOOP 添加账号入口展开表单 |
| 40 个隔离状态 | PASS | `presentation/matrix-results.txt`，状态列表见下；显式“模拟数据”标签 |
| 360 组窗口布局 | PASS（等效 DIP） | 40 × 3 分辨率 × 3 缩放；1366×768、1920×1080、2560×1440 与 100/125/150% |
| 深浅色 | 已检查 | 18 张桌面截图中的深色页，以及最终 `presentation/wpf-renders/dark.png`；共享 DynamicResource |
| 实际显示器 DPI | 已检查 125% | WPF 读数 120/120；100% 和 150% 没有物理切换显示器，仅等效 DIP 验证 |
| 实际启动与页面切换 | 已检查 | Computer Use 实际打开隔离窗口并复核 17 类页面，见 visual-review.md |
| 自动启动退出 / 本地候选 smoke | PASS（隔离模式） | 最终候选执行 Feature、UI/Host、40 状态矩阵并退出；未强制终止用户安装实例 |
| Worker 来源 | PASS | 四个打包 Worker 与已验证 813e6de 候选 SHA256 一致；未重建 Core |
| 旧 UI 契约静态迁移 | 763 / 763，缺失 0 | inventory-summary.json 与 migration-map.md；53 个 AppSettings 字段完整 |
| 按钮/命名事件接线 | 静态检查未发现缺口 | `test-output/ui-wiring-review.json`：无缺 Click/Command/内置取消行为的按钮，无未引用命名处理函数 |

## 40 个状态

01 总览；02 Battle.net 账号管理；03 区服切换；04 战绩；05 区服快照；06 Twitch 未登录；07 Twitch 设备授权；08 Twitch 已登录；09 Twitch 正在观看；10 Twitch 完整设置；11 SOOP 多账号；12 SOOP 设置；13 Bilibili 登录；14 Bilibili 设置；15 YouTube；16 诊断中心；17 全局设置；18 Twitch 配置损坏；19 Twitch 网络失败；20 SOOP 无账号；21 SOOP 单账号停止；22 Bilibili 二维码过期；23 Bilibili 已登录；24 YouTube 浏览器不可用；25 Worker 崩溃；26 代理不可用；27 账号切换失败；28 区服切换失败；29 Twitch 活动与奖励；30 Twitch 账号维护；31 Bilibili 活动任务；32 Twitch 网络与维护；33 运行日志；34 Twitch 游戏优先级；35 Twitch 自动化；36 SOOP 刷新与恢复；37 Bilibili 自动化与通知；38 YouTube 浏览器配置；39 全局网络代理；40 账号备份与存储。

这些状态通过原 ViewModel 和 WPF 页面渲染，传输替身只在显式验收模式注入。模拟登录、二维码或奖励进度不构成官方认证或真实奖励增长证据。

## 尚未完成的验收

- 解除剪贴板占用后，重新执行真实复制写入/读回检查。
- 在真实 100% 和 150% 显示器 DPI 下补充手工窗口、字体与弹窗检查。等效 DIP 不能代替物理 DPI 栅格化验证。
- 当前 Twitch Worker inventory DTO 不提供奖励图片 URL，本轮未改协议，因此奖励图片视觉要求尚未满足。
- 未逐项执行全部第三方业务。真实登录、领取、Battle.net 账号恢复、游戏文件覆盖及 YouTube 登录按本轮边界不执行；静态 763/763 不代表逐项业务实测 763/763。

## 复现

使用 `build-support/Build-UiCandidate.ps1`，提供已验证 Worker 候选和一个不存在的输出目录。脚本核验工作树、Core 源差异、来源及打包后四个 Worker 哈希，再执行 Release、Feature、UI/Host 与布局矩阵。仅为该进程设置 `MSBuildEnableWorkloadResolver=false`，绕过本机 SDK workload 初始化问题，不修改全局 SDK 配置。UI 返回码 2 如实记录为 BLOCKED。
