# Drops 第二轮修复与候选验收记录

起始为干净的 `main`，HEAD `121ce0fa4c426b2289d85eaf708931b4dc06d6cb`。没有回退后续代码、删除原账号凭据或观看历史，也没有修改系统 hosts、系统代理或 TLS 校验。版本保持 **2.1.8 本地候选**；本记录不代表 2.1.9 正式稳定版或正式发布。最终修复提交可通过 `git log -1 -- docs/drops-round2-2026-10-12.md` 查询。

## 不可回退的基线

已完整读取 `docs/drops-repair-2026-10-11.md`。保留 Twitch 上游 GQL hash、Chrome 153、`request cancelled` 有限重试、NoAutoClaim；保留 SOOP `ingameGiveYn` / `itemType == 4` / `acctConn` 的 Required/Unsupported/Unknown 与 Linked/Unlinked/Unknown 领取保护，以及领取前重新读取所属 UID 的 Inventory。保留四平台设置、DPAPI、多账号、代理策略、成功缓存和既有打包入口。真实 smoke 一律关闭自动领取，没有消费任何奖励。

## 本轮实际问题、根因与修复

| 问题与证据 | 根因 | 修改位置 | 修复及验证 | 状态 |
|---|---|---|---|---|
| 新 Token 验证错误被泛化；临时 OAuth 故障提前终止 | validate HTTP 401 没有统一为 invalid_token；临时拒绝缺少有限重试 | `twitch/core/oauth.py`, `twitch/core/twitch.py`, `twitch/tests/test_oauth_recovery.py` | 401 正确分类；临时错误至多 3 次，等待可取消；新 Token 必须匹配发起授权的 Smartbox Client，旧 Web/Android Session 才允许兼容切换 | 模拟状态转换通过；全新人工授权待验收 |
| Twitch ENDING_SOONEST 真实启动报“掉宝刷新超时” | 首次 inventory 已加载后 auto_start 再次 reload；异步频道阶段能覆盖 RESTART；刷新又等待频道稳定 | Twitch Core/Worker、两组现有 Twitch 契约 | 启动直接使用首次成功数据；独立保存刷新意图；按 inventory/games revision 确认官方刷新，频道扫描独立；停止优先 | 最新 frozen 真实观看、刷新、停止通过 |
| Smartbox 新授权后读取活动成功，观看立即退出却没有可见故障 | 真实 traceback：get_spade_url step #1；频道 URL 沿用 OAuth Client 的 android.tv 页面，未提供 streamer Spade 配置；critical task 关闭主循环后错误未向 Worker 传递 | `twitch/core/channel.py`, `twitch/core/twitch.py`, OAuth 回归组 | Smartbox 的公开频道/配置使用 www.twitch.tv，OAuth/GQL Client 身份保留；核心主循环传播后台真实异常 | 修后最新源码新 Session 观看/刷新/停止通过；最终 frozen 另补验 |
| Twitch 本地估算混入官方分钟和活动百分比 | UI 投影使用 current_minutes / total_remaining_minutes，包含 extra_current_minutes | `twitch/worker.py`, `DropsViewModel.cs` | 主进度只用 real_current_minutes；临时估算独立说明；前置奖励剩余时间也使用远端值 | 回归通过；NoAutoClaim 不变 |
| SOOP 重启后无法追踪历史回退；名称/到期日变更拆裂身份 | 历史只有运行内存；展示信息参与进度键 | `soop/worker.py`, `soop/tests/test_progress_sync.py` | 优先 UID + mission + cycle + 稳定 item/stage ID + giveTerm；缺少 ID 时用阶段槽位；有界持久化诊断，重启标 cache，当前值仍用官方 API | 持久化、回退、跨账号/阶段、损坏文件降级回归通过 |
| SOOP 普通状态回调冒充新的成功查询；旧阶段被完整刷新重新确认 | 未区分 unchanged callback 与官方刷新边界 | 同上 | 未变数值不刷新查询时间；只有本次返回的阶段被确认；失败保留快照并标 cache | 现有进度回归通过 |
| SOOP 重登与手动启动/停止并发，可迟到注册 Miner | Core start_account 在 __aenter__ 后注册；Worker 缺少 UID 生命周期锁和取消后复核 | `soop/worker.py`, 现有 SOOP 契约 | 每 UID asyncio 锁，手动启动也走同一门禁；旧重登线程结束前不启动第二线程；迟到注册清理；全局停止等待已有门禁 | 停止/重登竞态回归通过；仅一个真实账号 |
| 停止 B 的恢复请求可能一起停止 A | Host 将已发送请求的 caller cancellation 转为整个平台进程回收 | Host、VM、WPF Drops 自检 | SOOP 带 UID 的 start/stop/refresh：取消尚未发送的工作；已发送由明确 stop_account 完成清理，仍保留请求总超时；恢复每步检查停止意图；取消后的故障不再提示 | 真实 Host/JSONL + 模拟 A/B 验证 A 保持运行、B 停止、generation 不变 |
| 停止按钮排在刷新后，等待网络操作 | JSONL 同步读取并执行；Host 业务 gate 阻挡控制命令 | `Shared/protocol.py`, Host、Shared 契约 | 独立 stdin reader、FIFO 业务处理；只读刷新 future 可被 stop 取消；控制命令绕过业务 gate，写入仍序列化；无效控制不设置取消标记 | 真实子进程传输、WPF Host 停止回归通过 |
| Worker 重启后排队 UI 消息、健康快照覆盖新状态 | Dispatcher 执行时未再次验证 generation；健康快照没有 revision | Host、Models、VM、页面 | 入队和执行时都检查当前 Worker；健康快照增加 generation/revision；退出/握手/Dispose 排空创建与请求门禁 | 乱序、崩溃重建、取消后重建回归通过 |
| 点击复制授权码没有复制 | 旧 native Clipboard 使用无效 owner；本机 GameViewer 也实际占用剪贴板，HRESULT 0x800401D0 | `ClipboardService.cs`, Drops 页面、WPF 自检 | 短 STA 线程由 WPF 写入并同线程读回；有限重试、成功才提示复制；失败给出占用原因并选中可手动输入的码；按钮恢复，失效码不误报成功 | 正常写入及实际按钮路径曾通过；占用间歇复现，受阻结果保留 |
| 未保存设置在切页/后台更新后丢失；成功和故障提示滞留 | 页面重新 PopulateSettings，没有草稿边界；提醒无关闭入口 | 页面/VM/XAML | 分平台编辑草稿；保存失败保留输入，成功后接受服务端值；关闭提醒、同问题去重、成功刷新清理故障 | 实际 Routed Click + 模拟 Worker/VM 反馈通过 |
| YouTube 退出 profile 后扫描重新打开，浏览器子进程可能残留 | 启动注册与关闭并发；只 terminate 父进程 | `youtube/worker.py`, 现有契约 | profile 停止标记和同锁启动注册；CDP Browser.close，失败只清理自己 PID 的树并等待；无运行浏览器不显示“正在观看” | 竞态契约及真实隔离 Chrome 启动/停止通过 |
| 构建可能使用不同 SOOP Core/旧 Worker；许可声明与自动捆绑冲突 | 父目录来源可变，依赖未固定；Skip 可混入旧产物；对外构建未区分授权 | 构建脚本、provenance、constraints、Core baseline、csproj、第三方声明 | Core 提交/dirty/源码摘要/API/工具版本和 Worker SHA256 清单；来源变更拒绝；跳过打包也核对当前源码、提交、用途和 binary hash；Local 与 Distribution 分离 | 完整 Local 和 adapter-only 构建已验证；完整 SOOP 再分发仍阻塞 |

## Twitch 结果和人工授权边界

上一轮 `KeyError: device_code` 的可复现条件仍是 Android/Web Device API HTTP 400 `invalid client`，错误 JSON 没有成功字段。Smartbox 可获得 HTTP 200、非空官方设备码字段及官方 HTTPS activate 地址。未获取 2026-10-11 19:07:39 原始封包，不将模拟或后来的请求冒充历史封包。

本轮不再索引未经验证的 OAuth 字段。pending/slow_down、取消、过期、invalid_grant/client/token、429、5xx、非 JSON、缺字段及网络失败已有回归。新 Token 不允许静默切换 Client；不支持的旧 Session 不因检测错误删除原文件。

最新一致打包产物真实 smoke（显式 HTTP 代理，fallback 关闭，隔离复制旧 Session，自动领取关闭）：106 活动、372 背包、12 可用活动、14 eligible 活动；频道扫描后 152 频道，当前频道和奖励均非空，8 条 WebSocket Connected；手动 refreshCompleted=true；stop 后 running=false，shutdown 退出码 0。启动完成不等于频道已选定，界面仍按连接阶段显示。

用户已在隔离“Drops 本地验收”真实窗口完成官方网页授权。19:08:14 Device HTTP 200，19:08:51 Token HTTP 200、validate HTTP 200，Session 文件成功保存；随后独立进程恢复新 Session 并读取 2 活动、13 背包。实际验证揭示上述 TV streamer 页面兼容缺陷，已修复并继续真实观看补验。已有 Web/Android Session 成功不替代此新 Token 验证。过期/取消及特定异常 UI 状态仍由模拟响应覆盖。

修后最新源码真实补验：新 Smartbox Session 在独立进程中成功恢复，2 活动、13 背包、32 频道、当前频道/当前奖励均非空、2 条 WebSocket Connected；45 秒后仍 running=true，没有 error 事件；refreshCompleted=true；stop 后停止，退出码 0。新 Token → validate → Session 保存/新进程恢复 → GQL → 观看/WS → 刷新/停止已真实执行，最终提交后的 frozen 产物仍需再核对。

## SOOP 结果和数据含义

数据链：Core Mission API `itemList.viewTime` → 模型 view_time → Worker 按 UID/活动/阶段投影 → Host JSONL → VM/XAML。软件不把本地观看计时或历史最高值加回官方值。官网独立“刷新归零”不能归因于 CloudLight；本轮未复现同一活动/阶段官方归零。

`progress_diagnostics.json` 位于现有 SOOP 数据目录：最多 512 阶段，保留 30 天，读取上限 512KB，原子写入；包括最新官方数值、高水位、查询/回退时间和前后值。没有 Cookie、Token、密码；错误文本也经过脱敏。删除账号清该 UID 的诊断；损坏或无法写入不阻断观看。重启先显示历史 cache，成功官方刷新后只确认实际返回的阶段。API 缺少稳定阶段 ID 时槽位兼容策略无法完全证明官方列表重排后的身份，这是数据结构边界。

真实观看完成 **3621 秒**：一账号各检查点持续 bridge connected，资格有效，17 任务、11 活动、59 背包、46 历史阶段；每 600 秒完整刷新均保持观看，最终比较 39 阶段，5 阶段官方 viewTime 增长，0 回退。stop 成功、首次退出码 0；新进程恢复 46 条历史且全部 source=cache，完整官方刷新成功、39 阶段身份仍匹配，最终退出码 0。长测加载的是本轮较早代码；后续生命周期门禁和脱敏修正另用最新源码/最终 frozen 短 smoke 补验，不冒称一小时观察覆盖之后全部代码。heartbeat 业务状态一直 unknown，不能宣称成功心跳；显示官方数值并说明“观看连接正常，心跳业务状态待确认；以官方进度为准”。

只有一个可用真实 SOOP 账号。双账号、单账号 Session 失效、停止/恢复串线的验证由契约及真实 Host 传输 + 模拟账号业务覆盖，不称真实双账号验收。官方数据库回退软件无法修复；若发生，当前官方值照常显示，历史高值和回退诊断单独保留，不补造时长。

## 四平台功能验收矩阵

“模拟/契约”不表示真实账号端到端成功；未执行真实领取。

| 功能 | Twitch | SOOP | YouTube | Bilibili |
|---|---|---|---|---|
| 登录/Session/重启 | Device/Token/validate 新授权真实；新旧 Session 均真实恢复 | DPAPI 隔离重新登录真实；历史缓存重启及完整刷新真实成功 | 隔离浏览器 profile 真实；未确认实际登录 | 官方 QR 图片真实生成，180 秒；未扫码完成登录 |
| 自动启动/停止/崩溃 | 真实观看/停止；Host 崩溃 generation 回归 | 真实观看/停止观察；UID 生命周期和恢复契约 | 真实浏览器启动/stop，0 剩余 Session，owned PID 退出 | QR cancel/stop/exit 0；poller/claim/close 同 loop 保留契约 |
| 任务/频道/进度/背包 | 真实活动、背包、频道、当前奖励；远端与估算分离 | 官方任务/活动/背包/viewTime；历史和 unknown 心跳降级 | 直播发现和本地历史语义保留；本次主页 smoke 历史 0，未验证真实直播时长 | 无账号 API 直连/代理均 HTTP 412；不是活动查询成功 |
| 手动/自动领取 | NoAutoClaim 和领取前置条件契约，不消费 | 绑定保护、按 UID 重读 Inventory 契约，不消费 | 不适用：不提供官方 Drops 奖励进度/领取 | 配置及停止后不继续领取契约，不消费 |
| 网络/代理/设置 | HTTP/WS 指定代理真实；有限重试与停止门禁 | 真实指定代理；仅恢复原活跃 UID 契约；草稿保存回归 | 浏览器指定代理；CDP 本机直连；profile/config/history 保留 | 显式直连/代理均有 412 拒绝证据；不绕过限制 |

## WPF、提醒和布局

本轮保留上一轮顶部账号/运行状态及主要操作、中部进度、下部活动/背包、次级设置/诊断的布局。增加 SOOP 可折叠历史诊断、可选中 Twitch 验证码、关闭提醒入口、分阶段登录按钮文字，以及分平台设置草稿和保存成功反馈。复制失败提供真实原因；需要绑定的 SOOP 奖励继续显示打开背包，不能直接领取。

真实 WPF 程序已启动；自检使用实际控件、Routed Click、Measure/Arrange/Render、视觉树、Binding 和真实 Worker Host 传输。账号业务或特定状态部分使用模拟响应。四平台 1366×768、1920×1080、2560×1440、1200×760 × 100/125/150% **DIP 模拟**检查无按钮横向裁切、底部可滚动，已查看四平台渲染截图。未切换真实 Windows 系统 DPI，未验证多显示器 DPI 迁移；不能据此称已通过系统 DPI 验收。桌面自动化没有提供 native app 控制，人工真实窗口验收仍需用户。

剪贴板实际出现 GameViewer 占用，0x800401D0；解除占用后正常系统写入/读回和按钮曾通过，后续又间歇受阻。BLOCKED 保留，整份自检有受阻项时不宣称全部 PASS。验证码本身仍来自可信 OAuth 响应，未把真实码写入本记录。

## 来源、许可和构建

SOOP 外部 Core 未修改：父目录 `cloudlight soop drops miner`，提交 `6050b8a35690b091996a1efedad4655b1b094cac`，运行源码 SHA256 `65801b6e10f1777cd08af4ae0c6903003b2544c6bfd58b60f7295ba77d879be6`。能力版本 1 检查 force_refresh、force_refresh_account、绑定保护和远端 viewTime。构建期间源码发生变化会明确拒绝混合产物，本轮实际触发并重新一致构建过。

Python 3.13.2 / OpenSSL 3.0.16；PyInstaller 与运行依赖固定在 `build-constraints.txt`；.NET SDK 8.0.424。清单记录 Core 实际路径、Git/dirty、源码及适配器摘要、工具版本、四 Worker SHA256。保证来源可重复检查，不宣称 PyInstaller 产物已证明逐字节重现。

完整捆绑 SOOP 只允许明确 Local 本机自用。默认 Distribution 阻止未确认授权的完整捆绑；adapter-only 在独立目录构建，frozen 分发不加载外部 Core，显示明确不可用。实际 adapter-only 的 SSL/provenance/import/unavailable 自检通过，即使设置外部 Core 环境变量仍拒绝加载。完整 SOOP 对外再分发仍需权利人许可；未获得或伪造授权，未删除来源声明。

本地重建命令：

```powershell
.\Integrations\Drops\build-workers.ps1 -BuildPurpose Local
$dropsCandidateCommit = git rev-parse HEAD
dotnet publish '.\CloudLight Blizzard.csproj' -c Release -p:DropsBuildPurpose=Local -p:BuildCommit=$dropsCandidateCommit -o "artifacts/candidates/drops-round2-$dropsCandidateCommit"
```

最终候选目录使用修复提交命名；主程序 metadata、四 Worker 内嵌清单、外部清单和 SHA256 另做一致性核对。禁止拿 Local 候选向外发布。`build.ps1 -OmitUnlicensedSoopCore` 为明确缺少 SOOP Core 的分发方案，不代表完整 SOOP 功能已获发布许可。本轮不创建 Tag、Release 或正式安装包发布。

## 关键检查和剩余最小人工操作

- Shared 3、Twitch 23、SOOP 39、YouTube 2、Bilibili 20，共 87 Worker 契约通过。新增回归集中在已有 Twitch OAuth、SOOP 进度、Host/WPF 三组；没有运行全仓库 lint、安全审计、性能/压力测试。
- 四 Worker PyInstaller 一致打包、归档 SSL DLL、冻结 SSL 导入、provenance、Twitch GQL/Chrome 153、SOOP Core import 已通过；最终提交后的清洁产物另核对。
- WPF Release 0 警告/错误；FeatureSelfTest 13 组通过；UI 控件/Binding/按钮/乱序/停止/崩溃回归通过，真实剪贴板占用按证据保留受阻状态，不掩盖为 PASS。
- 真实网络证据存放在 Git 忽略的 `test-output/drops-round2`：Twitch 最终 `twitch-stable-summary.json`，SOOP `soop-long-summary.json`，Bilibili `targeted-network-summary.json`，YouTube `network-smoke-final-summary.json`。不提交凭据、原始隐私日志、截图或二进制。
- Twitch 官方网页人工授权已完成；程序侧继续验证观看与新 Session 的恢复。若复制被其它程序占用，使用可选中码或解除占用，不使用过期码。
- 人工：第二真实 SOOP 账号 A/B 停止和恢复；YouTube 实际登录直播播放；Bilibili 扫码登录后的任务/观看；真实 Windows DPI 和多显示器移动。自动领取均保持关闭。

最终自查由主代理检查实际文件、完整 diff 与必要验证。前期 SOOP 诊断有子代理辅助；后续独立代理审查请求因 401 Unauthorized 无法执行，没有宣称该审查通过。任务权限是 danger-full-access / never，未主动调整模型或推理设置；没有利用权限执行系统配置更改或未授权发布。测试 Worker、临时浏览器和观看任务必须退出；用户原安装程序保持运行。
