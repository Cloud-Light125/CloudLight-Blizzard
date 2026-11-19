# CloudLight Blizzard 3.0.0 六项修复验收

起点：`ecc75f3cea528960e45cd199b8d6287e30d17da2`，分支 `ui/fluent-v2`。执行前远端目标分支与本地 HEAD 相同；没有重置、改写历史或覆盖旧候选。版本保持 3.0.0。

最终安装器、源码身份和逐项二进制验证见 [最终交付记录](DELIVERY.md)。源码 UI/Host 为 348 PASS；最终解包程序三次检查均为 346 PASS / 0 FAIL / 1 BLOCKED（系统剪贴板真实读写被占用），不将其宣称为全通过。发布载荷组件为 203 PASS；源码含三项额外官方目录夹具检查，共 206 PASS。

## 根因及修复

| 问题 | 实际根因 | 修复 |
|---|---|---|
| SOOP 状态混乱 | Worker 从未输出 `loggedIn`；账号数因此为零；Core 的“挂机中”原始文本与共享 Worker 存活被混用；旧进度状态没有完整的频道/活动关联 | 在实际 mission API 成功边界记录认证与官方读取，输出独立 `authenticationState`、`heartbeatEvidence`、`officialEvidence`；WPF 分开展示认证、Worker、频道、心跳、官方同步及增长；进程 generation 更新清除旧证据 |
| 游戏显示 `game:12` | 已保存的 ID 会先建立 WPF 游戏对象；初次名称永久缓存；目录获取失败还会中断活动刷新 | 官方目录统一映射，更新既有对象的名称而保留 ID、选择和排序；目录失败保留已验证缓存；缺失编号明确显示未知；Worker、概览、活动、配置和详情共用名称 |
| 游戏列表拦截滚轮 | 固定高度的虚拟化 ListBox 嵌在页面 ScrollViewer 内，普通滚轮由内部列表消费 | 普通滚轮传给页面主滚动容器；明确说明 Shift+滚轮/独立滚动条用于浏览目录；保留回收、按游戏延迟展开和图片按需加载；顶部四项快速导航、固定保存栏保留 |
| SOOP 标识不正确 | PlatformIcon 用代码生成近似符号 | 改用官方站点原始 favicon，全局 PlatformIcon 共用，选最高分辨率帧，等比显示、不改色；原始资源及 SHA256/条件已登记 |
| 快照按钮与删除反馈 | 三个按钮没有 Fluent 样式；当前使用快照被正常保护，但失败只写入不明显状态文字；UI 未重新核验磁盘 | 四按钮统一 36 DIP；当前快照点击前明确解释并提供安全导航；普通快照确认后调用原管理链路、重新读取磁盘与列表、弹出明确结果；存储边界再次检查活动指针、根目录、reparse point 和删除结果 |
| 关于页面结构 | 更新/反馈实现在 SettingsPage，旧导航仍定位 settings:updates；许可证默认展开 | 唯一 UpdatePanel 从 SettingsPage 实际提取，FeedbackPanel 实际移到 About 顶部；旧更新通知改开 About；保留原服务、摘要校验、退出安装、失败/取消、历史与代理路由；许可证默认折叠、内容与分发文件完整保留 |

### SOOP 证据约束

- 密码、Cookie 文件存在或 Worker 创建均不证明已登录。读取成功属于当前进程和对应 Core UID；认证失效、重登及 generation 变化作独立处理。
- 成功心跳必须匹配 UID、频道 ID、broadcastNo、活动及 target epoch；同频道换活动、换场次、A→B→A、停止或认证失效后的迟到响应均不能验证新目标。最近成功时间、UID、直播间、活动及过期状态独立显示。
- 官方增长仅由同 UID、同活动周期、同阶段、同 giveTerm 的连续可信 API 读取确认；跨任务增长不会认证当前展示目标。心跳成功不证明已计分钟，Worker 回调不累加本地分钟。
- 心跳证据超过 150 秒失效；官方读取超过 300 秒标记过期。失败保留上次官方数值并标明失败/过期；新进程不复用认证或成功心跳。随机型任务继续使用既有非确定进度规则。
- 原领取协议、原登录/DPAPI/自动恢复机制和 SOOP Core 磁盘源码均未修改。

### 官方目录与品牌

2026-11-18 18:50:25（北京时间）仅发出一次官方目录只读请求，取得 **55** 类。此前 54 类全部逐项验证；已从当前目录移除的 `379 / 배구중계` 保留为已验证历史名称。`12 / 오버워치` 使用用户已确认的“守望先锋”本地化，其余保留准确官方韩文/英文，不自动猜测翻译。

目录来源：`https://drops.sooplive.com/api/get_drops_game_category_list.php`。公开夹具只包含游戏 ID/标题及来源时间，不含 Cookie、真实账号、Session 或密码。活动性能和截图使用 2026-11-18 11:50 的脱敏官方只读快照回放：54 游戏、425 目录活动加 1 条账号历史活动，共 426 活动、617 奖励。

品牌素材来源：<https://res.sooplive.com/favicon.ico>，原始 SHA256 `055a159175fd903499984a84918d416ed49d628776cce228ef1211da7236ccb4`。官方[品牌指南](https://www.sooplive.com/policy/contents/brand_guide.html)要求站外使用取得书面许可，并限制形状、比例及颜色变更。尚未提供书面许可；该问题与 SOOP Core 的公众再分发授权独立记录，均不能由“可下载”推定获得许可。

## 专项与回归

| 检查 | 结果/证据 |
|---|---|
| SOOP Python（认证/官方读取/Heartbeat/UID/频道切换/调度/JSONL 契约） | 82 PASS；旧 currentProgress 契约保持，未修改既有断言迎合新增字段 |
| Twitch ClaimCoordinator、领取保护及活动优先级 | 121 PASS |
| Bilibili / YouTube / Shared | 21 / 8 / 5 PASS；Python 合计 237 |
| WPF 完整组件、SOOP 状态矩阵、图片缓存/取消/延迟加载 | 206 PASS、0 FAIL；[组件报告](checks/ui-v2-selftest.txt) |
| 完整 UI/Host 与真实 JSONL | 348 PASS、0 FAIL；默认折叠与实际展开后许可证内容/入口均检查；[报告](checks/ui-host-selftest.txt) |
| 六项修复真实 WPF 专项矩阵 | 67 PASS、0 FAIL、0 Binding Error；[报告](checks/six-repairs-results.txt) |
| FeatureSelfTest all | PASS；[报告](checks/all.txt) |
| Battle.net 账号安全 | PASS，隔离数据；[报告](checks/account-safety.txt) |
| 区服切换/VerifiedDifference | PASS；[报告](checks/region-verified.txt) |
| 快照删除 | PASS；当前/普通/过期/损坏/不存在/占用/越界/真实 Junction/损坏指针；[报告](checks/snapshot-deletion.txt) |
| 更新服务、摘要校验、恢复与取消 | PASS；[报告](checks/update-safety.txt)；搬迁面板实际执行隔离下载取消，未启动安装器 |
| 反馈 | 唯一 FeedbackPanel 经隔离 HTTP handler 提交一次并显示模拟 report ID；无真实反馈/日志上传 |
| 生命周期退出 | PASS；[报告](checks/exit-safety.txt) |
| Cloudflare 更新/反馈服务 | TypeScript PASS，12/12 用例 PASS；未部署 |
| Release 源码构建 | PASS，0 warning、0 error；版本 3.0.0 |

冻结 Worker、发布载荷、完整 UI/Host、解包和安装器保留测试由候选构建流程再次执行；最终逐项结果、源码 commit 与安装器 SHA256 记录于最终交付记录和 LOCAL-ONLY 候选清单。

性能测量见[组件性能](checks/soop-performance.json)和[整页性能](checks/six-repairs-performance.json)。426 活动/617 奖励展开后的已实现 RewardCard 为 17；折叠目录不实现奖励控件。保留刷新对象身份、滚动位置、搜索/筛选、图片取消及卸载处理。这是单机一次测量，不宣称稳定 FPS 或长时间 soak PASS。

真实鼠标已覆盖游戏标题、空白、展开活动卡、奖励图片、全部折叠、到达直播间策略及返回顶部。正常滚轮推动页面，目录浏览采用明确标示的 Shift+滚轮/滚动条。普通快照点击“否”后文件 SHA256 不变；确认后磁盘目录消失，真实 Win32 提示“快照已删除”，活动快照和指针保持不变。安全导航按钮还执行了真实 WPF 可见性及进入区服流程的检查。

## 真实 WPF 截图

截图矩阵覆盖目标窗口 1366×768、1600×900、1920×1080 及深浅主题。截图为真实 WPF 内容渲染或 Computer Use 的窗口/原生提示截图，非网页仿图。整页 PNG 不含原生标题栏，因此尺寸与外框目标略有差别；未改变物理显示器 DPI。`SIMULATED` 是隔离模拟认证/心跳/分钟，`OFFICIAL-REPLAY` 是历史官方目录/活动快照，不代表实时观看。

| 验收项 | 示例（完整矩阵见 screenshots） |
|---|---|
| 原 HEAD 关于 / 设置 | [关于前态](screenshots/about-before-ecc75f3-SIMULATED.png) / [设置前态](screenshots/settings-before-ecc75f3-SIMULATED.png) |
| 未验证 + Worker 运行 | [状态 1](screenshots/01-unverified-worker-1366x768-light-SIMULATED.png) |
| 登录验证成功 + 等待频道 | [状态 2](screenshots/02-verified-waiting-channel-1366x768-light-SIMULATED.png) |
| 心跳成功 + 官方未增长 | [状态 3](screenshots/03-heartbeat-official-unconfirmed-1366x768-light-SIMULATED.png) |
| 官方增长模拟 | [状态 4](screenshots/04-official-growth-1366x768-light-SIMULATED.png) |
| 官方游戏名称、官方图标 | [活动](screenshots/05-official-game-names-1920x1080-dark-OFFICIAL-REPLAY.png) |
| 多游戏观看配置 | [配置](screenshots/06-watch-configuration-1600x900-light-OFFICIAL-REPLAY.png) |
| 直播间策略 / 鼠标图片滚动 | [策略](screenshots/07-channel-policy-1366x768-light-OFFICIAL-REPLAY.png) / [真实鼠标](screenshots/17-native-wheel-reward-image-OFFICIAL-REPLAY.png) |
| 快照 Fluent 按钮 | [按钮](screenshots/08-snapshot-fluent-1600x900-light-SIMULATED.png) |
| 当前快照保护及安全导航按钮 | [真实对话框](screenshots/14b-active-protected-dialog-SIMULATED.png) |
| 隔离普通快照删除成功 | [原生提示](screenshots/18-native-delete-success-SIMULATED.png) / [列表刷新](screenshots/13-isolated-delete-success-SIMULATED.png) |
| 关于顶部更新 / 反馈 | [更新](screenshots/09-about-updates-top-1366x768-light-SIMULATED.png) / [反馈](screenshots/10-about-feedback-1600x900-light-SIMULATED.png) |
| 许可证折叠 / 展开 | [折叠](screenshots/11-licenses-collapsed-1366x768-light-SIMULATED.png) / [展开](screenshots/12-licenses-expanded-1600x900-dark-SIMULATED.png) |
| 搬迁后取消下载 / 提交反馈 | [取消](screenshots/15-about-download-cancelled-SIMULATED.png) / [模拟反馈成功](screenshots/16-about-feedback-success-SIMULATED.png) |

## 保留的失败记录与边界

早期检查曾因无外层溢出的折叠活动页而无法验证滚动位移，改为展开真实背包区、确认有外层溢出后验证原位移断言。更新面板隔离夹具最初缺少 `sha256:` 前缀，被既有摘要校验正确阻止，修正夹具后通过，未放宽校验。重复使用旧 UI 测试目录导致六项缓存请求计数断言失败；换全新隔离目录后 206/206 通过，未修改缓存业务或测试期望。

额外解包 UI/Host 检查最初有两个许可证视觉查找失败：旧自检未展开默认折叠的 Expander。现增加“默认折叠”断言并实际展开后再执行原有组件文字及链接断言，最后恢复折叠；原完整性检查全部保留。真实窗口还发现保护提示漏把导航按钮加入布局，已修复并验证按钮可见、能安全导航且不删除活动快照。

BLOCKED：公众再分发 SOOP Core 和站外品牌授权未确认。新安装候选仅 LOCAL-ONLY；不创建 3.0.0 Tag、GitHub Release，不上传受限 Core 或安装包。

NOT TESTED：真实账号长期观看、真实官方分钟增长、真实领取、真实反馈提交、实际系统安装、公开自动更新服务 3.0.0 升级、物理显示器 DPI 切换；均未用回放冒充。

实际环境为 Windows 11、PowerShell 7.6.9、sandbox=`danger-full-access`、approval=`never`；与可读全局权限一致。可读全局模型配置为 `gpt-6.2-sol/high`，任务实际后端型号/推理强度未独立暴露，不能确认模型级覆盖。multi_agent=false；全程主代理独立完成，未更改模型、权限、认证、系统代理或多代理设置。Python 使用既有隔离 venv、约束文件和清华 TUNA PyPI 源；GitHub 网络操作仅使用当前命令代理 `127.0.0.1:7897`。
