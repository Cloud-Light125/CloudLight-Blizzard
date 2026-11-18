# Twitch 领取与活动优先级最终专项审查

执行日期：2026-11-17 至 2026-11-18，Asia/Shanghai。主代理独立完成，未使用子代理。

## 验收结论与证据边界

已修复反向审查及真实连接发现的缺陷。离线故障注入、实际 Host/JSONL 进程集成、WPF 组件、经用户单独批准的真实单项领取、真实观看、停止及重启恢复均有证据。

**真实 Claim 仅执行一次：River Spray。** 官方独立 Inventory 回查确认已领取，WPF 卡片和队列同步，另一次零 Claim 的真实复验确认重启后仍已领取且按钮禁用。没有领取其他奖励，没有执行真实批量领取。

真实观看选中 Aspen，官方频道资格集合包含目标 Campaign，9 次观看请求被接受。目标 Starry Peace Spray 官方分钟数从 552 增至 555。原有 Twitch Worker 一直保留，故增长不能排他归因于本轮候选；不将此解释为能够强制 Twitch 给指定 Campaign 记账。

**未宣称所有真实场景通过。** OWCS 当时没有被发现的合资格直播频道；真实双 Campaign 反向频道切换、真实批量领取、真实网络故障/中断/过期、实体鼠标拖拽和键盘输入保留为 BLOCKED 或 NOT TESTED。对应离线场景的 PASS 独立列出。

证据根目录（仓库之外）：`D:\code\CloudLight Blizzard\twitch-audit-20261117\`，下文称 `E`。所有真实账号副本均位于此目录，未加入仓库。Release 候选位于 `D:\code\CloudLight Blizzard\CloudLight Blizzard\test-output\twitch-final-audit\candidate\`。

## 工作区保全

| 项目 | 结果 |
|---|---|
| 起始/最终分支 | `ui/fluent-v2` |
| 起始 HEAD | `2f6f66839856a8b0d0cd25b009041211eca87520` |
| 原 origin | `https://github.com/yundan125/CloudLight-Blizzard.git`；与任务给定地址不同，未修改、未联网拉取 |
| 原始变更 | 39 个变更/未跟踪叶文件已逐字节备份；全部备份 SHA-256 与起始清单一致，全部原文件仍存在 |
| 原始证据 | 上轮 Twitch 日志、`test-output/twitch-priority`、优先级 UI 证据一并备份 |
| 本轮对原变更的处理 | 14 个原变更文件继续修复；另外 25 个保留起始字节，包括版本与打包脚本等既有成果 |
| 原始账号与正式配置 | 原 `settings.json`、Twitch `settings.json`、`cookies.jar` 的最终哈希、大小与起始记录相同 |
| 发布/安装/Git | 未安装、未替换正在使用的程序；无 commit、push、tag、Release；无 reset/clean |

起始命令结果在 `E/start-*.txt`、`start-unstaged.patch`、`start-staged.patch`；完整备份在 `E/original-files/`。本轮增量补丁为 `E/this-audit-against-backup.patch`，保全证明为 `E/work-preservation.json`、`original-account-integrity.json`。

## A. 领取正确性

### 实际调用链

| 层 | 源码与方法 | 作用 |
|---|---|---|
| 卡片/详情/队列 | `Controls/RewardCard.xaml`；`CampaignInventoryView`；`WorkspaceAction` | 同一 Campaign/Drop 的多 Benefit 和多页面共享动作状态 |
| WPF 动作 | `Views/Pages/DropsPage.Workspaces.cs:OnVisualAction` → `DropsPage.TwitchActions.cs:ClaimTwitchRewardAsync` / `ClaimAllTwitchRewards` | 发送稳定 Drop ID 和 Campaign ID；Begin 防重复点击 |
| ViewModel | `ViewModels/DropsViewModel.cs:RequestAsync`、`ApplyState`、`OnEventReceived` | 路由命令；过滤过期 Generation；同步库存和批次事件 |
| Host | `Services/Drops/DropsHostService.cs:RequestAsync`、`SendAsync`、`HandleMessage`、`StampState` | 真实子进程 JSONL、请求 ID、Generation、超时和生命周期 |
| Worker | `Integrations/Drops/twitch/worker.py:claim_drop` → `_claim_drop` → `_claim_drop_locked`；`_claim_all_drops` → `_claim_all_locked` | 官方预读、资格判断、顺序批次、独立回查与结果统计 |
| 协调器 | `core/claim_guard.py:ClaimCoordinator`、`coordinator`、`claim_once` | 手动/批量/自动领取及库存刷新共用可重入事务门；持久记录不确定请求 |
| Core/官方 | `core/inventory.py:BaseDrop.claim` → `_claim_and_notify` → `_claim` → `GQL_QUERIES["ClaimDrop"]` | `DropsPage_ClaimDropRewards`；仅传官方 dropInstanceID，不以名称或索引定位 |
| 官方回查 | `core/twitch.py:fetch_inventory` → `_fetch_inventory_impl` | 读取 Inventory、Campaigns 和详细 Campaign；原子发布替换库存 |
| 回显 | Worker state → Host Generation 标记 → VM → `TwitchInventoryModel` / `TwitchClaimAction` | 官方确认后才显示已领取；失败和未知独立显示 |

### 安全行为

- PASS（离线）：未连接/未登录、不完整、过期、资格不足、Campaign/Drop 不匹配、重复 ID 不明确时不发送 mutation；稳定实例 ID 本身不能替代官方观看分钟数。
- PASS（离线）：单项重复点击、多页面共享状态、单项与批量交错、两个批量并发；传输层每实例只提交一次。UI Busy 是展示保护，业务权威门位于共用 Core 协调器，未建立互不关联的 UI/Worker mutation 锁。
- PASS（离线）：Claim mutation 不进入 HTTP/GraphQL 自动重放。超时/响应丢失/取消均先保留不确定意图；单项会独立回查。仍未知时显示“领取结果待确认”，不会变成成功或自动重试。
- PASS（离线）：Worker 退出会取消其拥有的领取/刷新任务；发送前已原子写入 `claim-journal.json`，只保存实例哈希与不确定状态。重启后不会自动恢复 Claim；损坏记录安全拒绝领取且保留原文件。
- PASS（离线）：未知项须刷新官方库存；之后的显式重试仍需新鲜可领取状态并经过至少 60 秒观察窗口。批量不会重放未知项。
- PASS（离线）：官方接受 Claim 仅表示请求被接受；Inventory 读取失败或仍未领取时保留未知。成功通知同样延后至库存确认，并按 Campaign/Drop 去重。
- PASS（离线）：多 Benefit 共用一个 Drop、一个计时器和一次 mutation；缺少 `self` 时，不能仅因其中一个 Benefit 已授予就推定整个 Drop 已领取。
- PASS：没有修改既有自动领取默认定义或原配置。原配置及两个真实隔离副本均为 `auto_claim_drops=false`。启动、Session 恢复、刷新、完成、切换、Worker 重启和页面初始化不触发 Claim。显式启用自动领取的既有路径也增加官方确认回归，未删除该功能。

### 批量事务

批次先成功读取官方库存，再按 `(Campaign ID, Drop ID)` 固定本次符合条件集合。只纳入已完成、未领取、官方可领取且符合账号资格的 Drop；不依赖旧 UI 卡片。

依次执行，项间有 0.5 秒间隔，并受既有 GraphQL 限速控制。收到停止意图或达到批次预算后，不再提交后续项。最后统一回查；只按官方结果统计，分别保留 `claimed`、`already_claimed`、失败、`unconfirmed`、`not_submitted`。以前未知而本次未提交的项目后来确认为已领取时，归入无需操作，不冒充本轮提交成功。

WPF 展示符合条件总数、确认成功、已领取、失败、待确认、未执行和当前奖励；提供“刷新官方 Inventory”及“停止批次及观看”。停止按钮同时停止该隔离实例的观看，此行为已在按钮文字说明。真实批量 **NOT TESTED：用户只授权 River Spray 单项**；故障注入与实际 JSONL 批量通过。

### 真实单项领取

| 字段 | 证据 |
|---|---|
| 用户授权 | 当前聊天明确回复：“批准仅领取 River Spray 一次” |
| Campaign | Reign of Talon S5 Mid / `e3a69270-1546-4966-8bcf-2af97c5a0a0b` |
| Drop | River Spray / `8676730b-d7a8-11f1-9ea6-0a58a9feac02` |
| 前置状态 | 官方 30/30、claimed=false、canClaim=true；真实 WPF 已展示同一奖励 |
| 提交 | 2026-11-18 05:18:05；日志仅一个“领取提交”，且仅此 Campaign/Drop |
| 官方确认 | 05:18:07 Core 回查确认；随后再次通过独立 `refresh` 获取 Inventory |
| UI | 已领取、按钮禁用、Busy=false；队列截图同步 |
| 其他奖励 | 独立回查逐 ID 对比：其余 Drop 的 claimed 状态未改变 |
| 重启 | 后续零 Claim 的全新程序/Worker 复验确认 River 仍已领取、按钮仍禁用 |
| 额外领取 | 0；真实批量 0 |

真实领取经已显示的 WPF 页面同一个 `ClaimTwitchRewardAsync` 方法执行，经过真实 VM/Host/JSONL/Core/官方响应；没有将模拟传输结果当作真实成功。桌面工具出现窗口身份映射错误后没有继续坐标点击，使用了候选的显式验收入口。提交前 `CreateNew` 写入一次性尝试标记，入口重启不能重复执行已授权尝试。

证据：`E/claim-authorization-and-submission.json`；`private-ui/approved-river-attempt.json`；`private-ui/live-audit/claim-response-state.json`、`claim-independent-inventory.json`、`real-claim-result.png`、`real-queue-after-claim.png`；`private-observe-ui/live-audit/restart-readonly.json`、`real-restart-readonly.png`。

## B. 活动优先级

### 实际调用链及排序依据

`CampaignPriorityWindow` 游戏选择/排序/保存 → `DropsPage.TwitchActions.OpenTwitchCampaignPriority` → `save_settings` → `SettingsStore` 原子持久化 → `campaign_priority.ordered/campaign_key` → Core `prioritized_campaigns` → `select_watch_channel` 官方 `AvailableDrops` → `choose_watch_channel` → `watch` → 播放列表/媒体观看请求 → 官方 Inventory/PubSub 分钟数 → 目标重评估。

| 策略 | 实际依据 | 验证 |
|---|---|---|
| 最早结束 | Campaign 官方 ends_at | PASS：Core `_run()` 到实际 `watch()` 的受控传输测试 |
| 最短剩余观看 | 官方 required_minutes - real_current_minutes；递归计算前置链，不使用活动结束倒计时或本地估计 | PASS：同上 |
| 完成度最高 | 官方要求与剩余的比例；不符合资格、已完成活动在候选外 | PASS：同上 |
| 完成度最低 | 同一官方比例反向排序 | PASS：同上 |
| 自定义 | 稳定 Game ID 下的 Campaign ID 顺序；新/暂缺 ID 兼容 | PASS：真实弹窗保存、真实 Worker 使用及真实重启恢复；离线验证 A/B 反转 |

游戏优先级先确定游戏顺序；同游戏策略再确定活动顺序；概览“仅显示优先游戏”和显示排序只影响投影。保存按游戏合并，不覆盖其他游戏设置。损坏配置沿用既有有效备份/明确错误机制，不静默破坏文件。

### 频道、共享活动与生命周期

- PASS（离线及真实）：不能仅按游戏名判定频道资格。调度组合活动有效期、账号资格、ACL、Drops 状态、前置条件和官方 `viewerDropCampaigns`。资格检查并发 4、缓存 120 秒、总检查窗口 25 秒；检查过期后先清空旧资格，防止超时继续使用过期许可。
- PASS（离线）：同游戏两个 Campaign 的 A/B 自定义反转及四种自动策略改变 Core `_run()` 最终的 `watch()` 目标，非仅 UI 排序；真实 `save_settings` 和重建 Worker 验证持久化。
- PASS（离线）：完成、过期、资格不足、无频道、离线及官方拒绝资格时跳过；共用频道可以原地更新 Campaign/Drop 目标，不需要为了更新标识跳频道。
- 调度证据保留 `campaignId`、`dropId`、`channelId`、`eligibleCampaignIds`。频道共用时分别观察各 Drop 的官方数值，不把本地目标选择当作官方记账承诺。
- PASS（离线）：有效手动频道优先；恢复自动选择及频道失效由现有状态机处理。有效频道自动切换有 120 秒冷却；失效/完成允许重新选择；10 分钟官方无增长触发库存和资格核对，不依靠短时无增长频繁切换。
- PASS（真实）：用户停止后 running=false；延迟再读仍停止；重启仅验证 Session 和库存，没有自动观看或领取。本轮创建的程序和 Worker 最终全部退出，原有程序/Worker 未关闭。

### 真实观看事实

| 项目 | 实际结果 |
|---|---|
| 优先目标 | Reign of Talon S5 Mid，rank=1 |
| 目标 Drop | Starry Peace Spray / `d0d3c9a5-d7a8-11f1-a148-0a58a9feac02` |
| 频道 | Aspen / `147927227` |
| 官方资格集合 | 包含 `e3a69270-1546-4966-8bcf-2af97c5a0a0b` |
| 观看请求 | 9 次 accepted / 9 次 attempts；媒体请求有真实 HTTP 200 |
| 官方分钟数 | 552 → 553 → 553 → 554 → 554 → 555，目标要求 600 |
| WPF | 显示相同目标、频道、官方分钟；真实截图与 JSONL/Inventory 对应 |
| 其他会话 | 发现原应用 PID 31316 及 Twitch Worker PID 50528、53380；保持原状。两个 Worker 也可能属于同一冻结进程树，不能据数量推断独立观看次数 |
| 归因 | 有官方增长，但不具备排他归因条件 |
| OWCS | 官方活动存在、账号有资格；当时无发现的合格频道，实际日志跳过。真实反向切换该活动 BLOCKED，离线双目标测试 PASS |

证据：`E/real-watch-summary.json`，`private-ui/live-audit/watch-start.json`、`watch-sample-*.json`、`watch-final.json`；两个真实实例各自的 `evidence/*/protocol-summary.jsonl`、`wpf-samples.jsonl`、`binding-errors.txt`；`private-observe-ui/live-audit/live-checks.txt` 的 10 项全部 PASS，额外 Claim=0。

## C. UI 与生命周期

| 项目 | 离线/组件结果 | 真实结果 |
|---|---|---|
| 概览、活动与奖励、详情、队列 | PASS，共享 Drop 动作与多 Benefit | PASS，官方库存及 River 状态截图 |
| 单项按钮防重复、Loading 收尾 | PASS | PASS，领取后已领取/禁用/不再 Loading |
| 失败、未知、刷新后显式继续 | PASS，故障注入 | NOT TESTED 真实故障；未制造重复真实 Claim |
| 批次五类结果、当前奖励、刷新入口 | PASS，组件和实际 JSONL | NOT TESTED 真实批量 |
| 优先级弹窗、上移/下移/置顶/恢复 | PASS，实际 WPF 控件事件 | PASS，真实数据置顶并保存 |
| 鼠标拖拽、实体快捷键输入 | 实现及入口属性检查 PASS | NOT TESTED；桌面工具窗口身份错误，未继续盲点 |
| 手动覆盖、失效及冷却 | PASS，Core 状态机 | NOT TESTED 真实手动覆盖及失效诱发 |
| Generation、Worker 异常/窗口退出 | PASS，Host 过滤/故障注入/退出自检 | PASS 正常停止、重启和退出；真实进程硬崩溃 NOT TESTED |
| 主题与窄窗口 | PASS，真实 WPF 组件浅/深色及窄窗口 | PASS，真实深色宽窗口、浅色较窄窗口截图 |
| WPF Binding | PASS，组件错误 0 | PASS，两次真实观察 Binding 错误 0 |

桌面工具 `computer-use` 初次显示了真实 WPF，后续把候选窗口归到 Codex，返回错误画面及 `no screenshot targets found for OpenAI.Codex_2p2nqsd0c76g0!App`。未在错误目标继续输入。真实验收随后由程序内显式入口使用相同页面/弹窗方法，截图来自实际运行 WPF 控件树；不把这种方式说成实体鼠标拖拽已通过。

## 缺陷、修复与修复前证据

| 问题/复现条件 | 原因与源码 | 修复前 | 修复后与回归 |
|---|---|---|---|
| 批量开始时 UI 缓存已过时 | Worker `_claim_all_drops` 直接枚举本地库存 | 保全源码复现：先 Claim 后 Inventory，FAIL | `_claim_all_locked` 先官方读取；失败则 0 提交；PASS |
| 官方接受但最终 Inventory 失败 | `BaseDrop._claim_and_notify` 提前置 is_claimed；批次按本地状态计成功 | 保全源码复现：success=1，应为 0，FAIL | 只在独立库存确认后成功；未知单列；PASS |
| 有实例 ID 但官方分钟未满 | `watch_requirement_completed` 接受 can_claim，而 can_claim 只验证实例/期限 | 保全源码复现：1/60 判完成，FAIL | 分钟、资格和期限共同判断；PASS |
| 真实弹窗没有游戏 | Worker `campaign_visual/drop_visual` 没有 gameId；模拟数据手写了此字段 | 首次真实入口在 Claim 前停止，0 提交；真实快照缺字段 | 补齐 Campaign/Drop DTO；Host→UI 字段契约、真实双活动弹窗保存 PASS |
| 两类领取锁与内存不确定记录 | Worker manual 锁与 Core mutation 锁分离，重启丢失结果 | 源码确认，原测试未覆盖重启不确定请求 | 一个可重入协调器涵盖刷新/领取；发送前持久意图；取消/重启/坏记录 PASS |
| 批量未知、失败、未执行混算 | 原摘要只有 success/skipped/failed | 状态不足，不能如实显示 | 五类结果+符合条件总数+当前项；并发/停止/部分失败 PASS |
| 共享频道目标过时、资格缓存过期仍可利用 | 只判断频道是否需要切换；资格检查超时可能留下旧缓存 | 源码及共享频道复现 | 原地更新目标；资格复核前清除旧集合；PASS |
| 一个 Benefit 已授予导致整个多 Benefit Drop 已领取 | `BaseDrop.__init__` 只收集已有 Benefit 后 all()，未要求齐全 | 缺其中一个授予记录也能推断已领取 | 要求全部 Benefit 都有官方时间记录；多 Benefit 与计时/领取去重 PASS |
| 官方确认重构影响既有自动领取/通知 | 成功标记与通知原本绑定 mutation 接受 | 需要保留用户显式启用的旧功能 | 自动领取也独立回查；确认后一次成功通知；false 默认配置与 true 显式模式回归 PASS |

`E/pre-fix-reproduction.log` 是对**保全的修复前副本**运行的 3 个独立需求测试，3 个均 FAIL；不是对当前成果的失败结论。修复后的对应故障测试全部 PASS。

验收程序自身也发现并修正两项检查错误：误读 `watchRequests.successes`（真实字段为 `accepted`），以及 Worker 重启后在旧页面登录标记刷新前调用登录入口。首轮真实领取文件保留这些原始 FAIL/BLOCKED，不删改。之后用**不含领取批准开关的新实例**复验，真实观看、官方资格、停止、配置重启和 River 状态 10 项全部 PASS，Claim=0。

## D. 回归、来源与交付

### 测试结果

| 检查 | 结果 | 日志（相对 E） |
|---|---|---|
| 起始 Twitch 回归 | PASS，100 项 | `baseline-twitch.log` |
| 修复前故障复现 | FAIL，3/3，预期结果 | `pre-fix-reproduction.log` |
| 最终 Twitch Python | PASS，121 项（原 100 + 新 21） | `twitch-delivery.log` |
| 实际 Host/JSONL/Core/UI 状态连接 | PASS，10 项，独立子进程 | `host-delivery/feature-selftest.txt` |
| WPF 控件/投影/弹窗/绑定 | PASS，115 项 | `ui-delivery/ui-v2-selftest.txt` |
| FeatureSelfTest Twitch 连接状态 | PASS | `final-connection/feature-selftest.txt` |
| FeatureSelfTest 退出与收尾 | PASS，87 条断言 | `exit-lifecycle/feature-selftest.txt` |
| SOOP | PASS，43 项；业务源码未改 | `regression-soop.log` |
| Bilibili | PASS，21 项；业务源码未改 | `regression-bilibili.log` |
| YouTube | PASS，8 项；业务源码未改 | `regression-youtube.log` |
| 共享协议 | PASS，3 项；业务源码未改 | `regression-Shared.log` |
| 真实独立 Inventory 读取 | PASS；原 Session 哈希不变；0 Claim | `official-readonly.json` |
| 经批准真实单项 Claim | PASS；1 次且只领取 River | `claim-authorization-and-submission.json`、`private-ui/live-audit/claim-independent-inventory.json` |
| 最终无 Claim 真实复验 | PASS，10 项；额外 Claim=0 | `private-observe-ui/live-audit/result.json` |
| Release 编译 | PASS，0 错误、0 警告；版本 3.0.0 | `release-final-build.log` |

测试包含真实子进程及 JSONL、Core 状态机实际 `watch()`、配置保存和重建 Worker、真实 WPF 与联网官方回查；不是只有单元测试。没有安装任何依赖、修改软件源或系统代理。

### 本轮修改文件

相对于任务起始备份继续修改的 14 个文件：

```text
build-support/Verify-TwitchReadOnly.py
Integrations/Drops/twitch/core/claim_guard.py
Integrations/Drops/twitch/core/inventory.py
Integrations/Drops/twitch/core/twitch.py
Integrations/Drops/twitch/tests/test_claim_actions.py
Integrations/Drops/twitch/worker.py
Services/Drops/DropsHostService.cs
Services/Visuals/TwitchActionsSelfTest.cs
ViewModels/DropsViewModel.cs
ViewModels/Visuals/TwitchClaimAction.cs
ViewModels/Visuals/TwitchInventoryModel.cs
Views/Drops/Twitch/TwitchWorkspace.xaml
Views/Pages/DropsPage.TwitchActions.cs
Views/Pages/DropsPage.Workspaces.cs
```

原来干净、此次修改的 5 个文件：`App.xaml.cs`、`Integrations/Drops/twitch/tests/test_headless_gui_contract.py`、`Integrations/Drops/twitch/visual_metadata.py`、`Services/Drops/RealDropsEvidenceRecorder.cs`、`Services/FeatureSelfTest.cs`。

本轮新增：`Integrations/Drops/twitch/tests/test_claim_audit.py`、`audit_jsonl_worker.py`；`Services/Drops/TwitchAuditSelfTest.cs`、`TwitchLiveAudit.cs`；本报告。未改版本、安装脚本或 SOOP/Bilibili/YouTube 业务源码。

### 候选及 Worker 来源

这是 **Release 配置的开发验收候选**，从当前仓库发现 Python Worker，未打包或替换安装版冻结 Worker：

- 程序：`D:\code\CloudLight Blizzard\CloudLight Blizzard\test-output\twitch-final-audit\candidate\CloudLight Blizzard.exe`。
- Worker：同仓库 `Integrations\Drops\twitch\worker.py` 和 `core\`；解释器 `.worker-build-venv\Scripts\python.exe`，既有 Python 3.13.9。
- WPF：既有 .NET 8 目标，使用已安装 .NET SDK 10.0.302；没有调整目标框架或版本号。
- 程序/解释器 SHA-256：`E/candidate-hashes.json`；完整 Python 来源清单及各文件 SHA-256：`E/worker-source-hashes.json`。
- `worker.py` SHA-256：`443c81b8fc8991c285227844221999c22bdf5453064c2241a96cb53569e9525d`。
- `core/twitch.py` SHA-256：`84f7354a618aa0574ee4fd5e45ba6a512d920cb7fbcac0f05608f89a66af36e7`。
- 最终 WPF DLL SHA-256：`f92da9147a28853ec1329ca965faae9c31f1b300756fbd11fbd5be60bfd08028`。
- 真实领取及后续零 Claim 联网复验使用的 WPF DLL SHA-256：`3c2eb9cedfa24580baa243f0c0e6e02755aa9aad79206d37dd5078501b4f7717`；最终编译另外包含批次明确未提交提示的局部收尾。

真实领取后对官方确认通知、多 Benefit 缺项判断、损坏领取记录的重复校验和批次明确未提交提示做了局部收尾，随后最终 Python 契约、实际 Host 子进程及 WPF 回归通过；未再次发送真实 Claim。真实操作的原始运行记录和最终源码哈希分别保留，不声称重放过不可逆操作。

真实 WPF 截图：`E/private-ui/live-audit/real-claim-result.png`、`real-queue-after-claim.png`、`real-priority-saved.png`、`real-watch-final.png`、`real-stopped.png`；浅色及重启截图位于 `E/private-observe-ui/live-audit/`。模拟组件截图独立位于 `E/ui-delivery/`，不混用为真实证据。

### 尚未测试及剩余限制

| 项目 | 状态与原因 |
|---|---|
| 真实批量 Claim | NOT TESTED；未授权，不扩大 River 的单项授权 |
| 真实两个 Campaign 反向观看、共用频道记账归属 | BLOCKED；OWCS 当时没有发现合格频道；共用情况只有受控传输覆盖 |
| 官方进度增长排他归因 | BLOCKED；其他已有会话未关闭；不能消除其影响 |
| 真实手动覆盖、冷却到期切换、完成/过期/离线诱发 | NOT TESTED 真实诱发；对应 Core 集成 PASS |
| 真实会话失效、Claim 限流/响应丢失/窗口关闭中领取 | NOT TESTED 真实破坏性诱发；离线故障注入与退出生命周期 PASS |
| 10 分钟真实无增长观察 | NOT TESTED；未通过短时间无增长伪造此验收 |
| 实体拖拽及快捷键 | NOT TESTED；桌面工具身份映射异常；WPF 事件/属性及保存链路通过 |
| 冻结 Worker 分发包/安装版 | NOT TESTED；本任务不替换安装版；候选明确为源代码 Worker 模式 |
| 官方说明网页独立核验 | BLOCKED；网页工具返回配额 HTTP 429；不据此宣称已重新核验领取宽限期等外部政策 |

## 最终工作区与执行设置

HEAD/分支保持不变；全部既有修改及新增修复留在工作区。最终状态、空暂存区及差异检查见 `E/final-git-status.txt`、`final-diff-check.txt`、`final-staged.patch`。没有提交或推送。

实际 Shell 为捆绑 PowerShell 7.6.9，已核验可执行路径。实际权限 `danger-full-access`、审批策略 `never`，与用户全局值一致；未更改系统、认证或代理配置。全局配置记录 `gpt-6.1-sol` / `xhigh`，运行时未提供可独立核验的精确模型/推理遥测，不能冒充已核验实际值；是否有界面模型覆盖无法独立确认。全局多代理开关 false，本轮无子代理、无新聊天委派。本轮未主动更改任何任务级模型、权限或安全设置。
