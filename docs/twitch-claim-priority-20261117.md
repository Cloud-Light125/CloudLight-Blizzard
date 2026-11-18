# Twitch 奖励领取与同游戏活动观看优先级验收

日期：2026-11-17（Asia/Shanghai）。所有实现与检查由主代理独立完成。

## 结果与操作入口

1. 原因：项目已有 `claim_drop` 命令、观众端 GraphQL `ClaimDrop`、库存回查和自动领取开关。旧详情模板使用 `DropsRow` 按钮；当前概览与活动页改用了 `CampaignInventoryView` / `RewardCard`，新卡片未连接领取动作。
2. 已完成且官方返回可领取的奖励，在概览、活动与奖励、奖励详情中显示“领取”。同一 Drop 的多件物品和多个页面共享 `TwitchClaimAction`，领取中禁用重复点击；成功显示“已领取”。错误保留原因和重试入口。全部完成但仍有待领取奖励的活动继续保留在默认活动范围。
3. 已实现真正的“领取全部可领取奖励”。入口在活动与奖励的排序区域旁，顺序提交，逐项记录，最终统计成功、已领取或无需操作、失败；失败原因可查看，对应卡片可单独重试。
4. 复用现有 Twitch 网页观众端 GraphQL 机制，不使用 Helix 开发者 Entitlements 更新接口。原生流程失败时，可点击“前往 Twitch 领取”，走原有浏览器打开 `https://www.twitch.tv/drops/inventory`。浏览器账号需要用户确认与应用当前账号一致。
5. 原“结束时间”是 `TwitchInventoryModel.Filter()` 内的显示排序，未参与 Core 的频道选择。此前 Core 主要按游戏偏好和 ACL 频道选择；推测活动时使用剩余时间，也不读取页面排序。
6. 新入口：“活动与奖励 → 排序区域 → 观看优先级设置”；概览也有“调整观看优先级”。弹窗先选游戏，展示活动时间、完成/剩余奖励、官方剩余时长、可参与状态、当前观看状态和序号。
7. 支持拖拽、上移、下移、置顶、恢复默认、保存；支持 Tab、Alt+↑/↓、Ctrl+Home。拖拽或按钮调整自动进入自定义模式。未保存、保存失败、保存成功均有提示。
8. 提供最早结束、剩余观看时间最短、完成度最高、完成度最低、自定义五种策略。每个游戏默认最早结束；用户既有游戏优先级、仅优先游戏模式及排除列表继续生效。
9. 优先级进入真实 Core 状态机的 `CHANNEL_SWITCH → select_watch_channel() → choose_watch_channel() → watch()`。原有游戏偏好先决定游戏顺序，再按该游戏的活动策略排列；页面显示排序单独持久化，无法改变调度。
10. 频道不仅按分类匹配，还查询 Twitch `AvailableDrops` 的 `viewerDropCampaigns`，结合活动有效期、账号资格、频道 ACL、Drops 状态和前置条件选择。查询并发最多 4，总观察窗口 25 秒，结果缓存 120 秒。共用频道时显示“已优先选择符合条件的频道，但 Twitch 实际进度以官方返回为准”，不承诺强制指定官方累计归属。两个活动的官方进度独立保留。
11. 已完成、已领取、过期和当前不可参与的活动不阻碍下一项；没有合格频道会记录跳过原因。已完成但未领取的奖励不再占据观看候选；自动领取仍遵循原开关。有效频道切换有 120 秒冷却；目标活动 10 分钟没有官方进度增长会重查库存和匹配。离线、完成或失去资格可跳过冷却。原手动频道覆盖规则保留。
12. 在 Twitch 原有 `settings.json` 中新增 `campaign_watch_priority`（Game ID → mode、Campaign ID 数组）和 `campaign_display_sort`，沿用现有原子持久化、备份、校验与旧配置迁移。按游戏合并，保留未知字段及其他游戏配置；新活动加入已有自定义顺序末尾，结束/暂时缺失活动 ID 不会阻碍有效活动。作用范围遵循原有本机 Twitch 数据目录语义，切换账号共享游戏偏好，弹窗明确说明。
13. 实际验证与受控验证见下文。真实账号只读查询成功；没有领取真实奖励，也没有启动真实观看。
14. 未修改 SOOP、Bilibili、YouTube 或共享 Python 协议业务代码。通用奖励卡片对非 Twitch 奖励默认隐藏新增动作，三个平台和共享协议共 75 项回归通过。
15. 修改文件清单见后文。已有用户的版本、打包脚本及其他修改均保留；没有修改版本号、commit、push、tag、GitHub Release 或安装目录。

## 领取确认与不确定结果

- 手动、批量、自动领取共用 Core 领取保护；同一客户端串行执行 mutation，并按 Drop ID 去重。
- 只有 Twitch 接受领取或回查库存已领取才能确认。已领取响应单独统计为无需重复操作。
- 领取 mutation 不走网络层或 GraphQL 层自动重放；超时/取消先标为待确认。明确重试前先查询官方库存，保留至少 60 秒观察窗口。批量操作不会重发待确认项。
- 登录失效、权限不足、限流、未达到领取条件、超过领取期限、网络异常和库存回查延迟均有处理。用户主动停止时，批量操作在当前请求结束后停止提交后续项。
- 库存刷新使用异步锁，避免主循环刷新与领取回查交叉写入；卡片只展示官方分钟数，本地观察估计不计入官方进度。
- 领取 mutation 的日志隐藏请求变量，不输出 drop instance ID 或令牌。

## 真实只读验证

使用当前数据目录中的 Session 隔离副本，保留原 Session 文件；查询结束后删除临时副本。通过只读白名单阻止 GraphQL mutation。脱敏结果位于 `test-output/twitch-priority/official-readonly.json`。

2026-11-17 22:08（北京时间）的官方返回：

| 活动 / 奖励 | 官方状态 |
|---|---|
| Reign of Talon S5 Mid | 有效；当前账号符合资格 |
| River Spray | 30/30；未领取；canClaim=true |
| Lounging Fika Spray | 60/60；未领取；canClaim=true |
| Sakura Love Spray | 125/180；未领取；不可领取 |
| OWCS Stage 3 Playoffs | 有效；当前账号符合资格；全部奖励尚未领取 |

本次真实请求：领取 0 次，观看 0 次。原 Session 文件最后修改时间仍为 2026-11-17 20:02:25，大小 2642 字节。

未进行真实奖励领取、真实批量领取、长时间真实观看或当前安装版 Worker 调度验收。官方频道检查和领取响应通过受控传输测试，不宣称为真实账号 mutation 成功。

Twitch 官方说明活动可设频道名单，直播主也可关闭特定活动的 Drops，故分类与泛化 Drops 标志不足以确认活动资格：[Twitch Drops Overview](https://dev.twitch.tv/docs/drops/)。

## 构建、测试与证据

- Python Twitch：100 项通过（既有 84 项 + 本轮 16 项）。`test-output/twitch-priority-python.log`。
- 调度测试实际执行 Core `_run()`：库存 → 游戏筛选 → 频道发现 → 官方频道活动资格核对 → `watch()`，不是 ViewModel 排序测试。覆盖两种相反自定义顺序、四种自动策略、完成后切换、无频道、离线、过期、无资格、Twitch 不返回目标活动、手动覆盖、切换冷却、共用频道真实进度不混算。
- 通过真实 `TwitchWorker.save_settings` 命令保存优先级，随后运行 Core 状态机确认目标变化；关闭 Worker、重建 Worker 后确认配置恢复；显示排序和其他游戏配置保持独立。
- 领取测试调用真实 BaseDrop/TimedDrop 领取路径，以受控 GraphQL 响应验证真实 mutation 操作名及参数、去重、超时/取消、登录/权限/限流、失败保留状态、批量独立结果和最终回查。
- WPF 组件验收：111 项通过。测试两个页面共享对象、真实按钮的可见性与禁用状态、官方页面动作路由、库存成功后同步、优先级弹窗恢复与保存、拖放/键盘能力入口、事件订阅释放；新增界面绑定错误 0。`test-output/twitch-priority/ui-verified/ui-v2-selftest.txt`、`twitch-action-bindings.txt`。
- .NET 既有 Twitch 连接状态自检：PASS；`test-output/twitch-priority/feature-verified/feature-selftest.txt`。
- 深浅色、小窗口截图：同目录 `claim-light.png`、`claim-dark.png`、`priority-light.png`、`priority-dark.png`；均使用明确标记的模拟活动。
- 其他平台：SOOP 43、Bilibili 21、YouTube 8、共享协议 3，共 75 项通过。
- WPF Release 构建：0 警告、0 错误。构建目录 `test-output/twitch-priority/release/`；该隔离开发构建从仓库发现开发 Worker，不是覆盖安装版的发布包。
- 未进行真实鼠标拖拽及硬件键盘输入的人工验收；相关处理已实现，WPF 测试验证入口属性和按钮排序事件。

本轮使用 Windows PowerShell 7.6.9（已读取实际可执行文件路径）、.NET SDK 10.0.302（目标框架仍为项目现有 .NET 8 WPF）、现有 Worker Python 3.13.9 环境。没有安装依赖或永久修改代理。

受控调度日志已记录两种相反顺序（完整记录在 `test-output/twitch-priority-python.log`）：

```text
观看优先级 1：Reign of Talon S5 Mid · 合格频道 channel1；实际累计以 Twitch 返回为准
观看优先级 1：OWCS Stage 3 Playoffs · 合格频道 channel2；实际累计以 Twitch 返回为准
```

这是模拟频道、受控传输下的真实 Worker 状态机日志，不能解释为当前账号已按该顺序获得官方进度。

## 修改文件

以下路径相对于 `D:\code\CloudLight Blizzard\CloudLight Blizzard`：

| 文件 | 内容 |
|---|---|
| `Controls/RewardCard.xaml` | 通用奖励卡片领取动作、错误及官方替代入口 |
| `Controls/CampaignInventoryView.xaml.cs` | 控件卸载时释放集合和活动订阅 |
| `ViewModels/Visuals/TwitchClaimAction.cs`（新增） | 跨页面共享领取状态 |
| `ViewModels/Visuals/RewardVisual.cs` | 引用共享领取状态 |
| `ViewModels/Visuals/TwitchInventoryModel.cs` | 卡片状态绑定、优先级元数据及独立显示排序 |
| `ViewModels/DropsViewModel.cs` | 批量命令及调度事件生命周期过滤 |
| `Views/Pages/DropsPage.TwitchActions.cs`（新增） | 单项/批量领取与设置保存入口 |
| `Views/Pages/DropsPage.Workspaces.cs` | 统一动作路由和同步详情卡片 |
| `Views/Pages/DropsPage.xaml.cs` | 旧领取入口复用统一处理，显示排序保存 |
| `Views/Drops/Twitch/TwitchWorkspace.xaml` | 活动页优先级与批量领取按钮 |
| `Views/Drops/Twitch/TwitchOverview.xaml` | 概览优先级入口、状态及官方累计说明 |
| `Views/Drops/Twitch/CampaignPriorityWindow.xaml`（新增） | 优先级弹窗 |
| `Views/Drops/Twitch/CampaignPriorityWindow.xaml.cs`（新增） | 拖放、键盘、按钮排序与保存 |
| `Integrations/Drops/twitch/core/campaign_priority.py`（新增） | 独立活动调度策略 |
| `Integrations/Drops/twitch/core/claim_guard.py`（新增） | 领取去重、冷却及不确定结果保护 |
| `Integrations/Drops/twitch/core/channel.py` | 官方频道活动资格缓存 |
| `Integrations/Drops/twitch/core/inventory.py` | 共用领取保护及完成奖励退出观看候选 |
| `Integrations/Drops/twitch/core/twitch.py` | 真实调度、资格核对、冷却、观察、回查同步及事件 |
| `Integrations/Drops/twitch/core/settings.py` | 兼容新增设置 |
| `Integrations/Drops/twitch/core/settings_store.py` | 设置校验及旧配置兼容 |
| `Integrations/Drops/twitch/worker.py` | 批量领取、配置接口、库存回查及事件循环释放 |
| `Integrations/Drops/twitch/headless_gui.py` | 向 WPF 发送当前活动调度事件 |
| `Services/Drops/DropsHostService.cs` | 批量领取的有界命令超时 |
| `Integrations/Drops/twitch/tests/test_campaign_scheduling.py`（新增） | Core 状态机及 Worker 配置集成测试 |
| `Integrations/Drops/twitch/tests/test_claim_actions.py`（新增） | 受控领取测试 |
| `Integrations/Drops/twitch/tests/test_network_policy.py` | 旧网络测试响应补充真实 HTTP status 属性 |
| `Services/Visuals/TwitchActionsSelfTest.cs`（新增） | WPF 动作、共享状态、弹窗及截图验收 |
| `Services/Visuals/UiV2SelfTest.cs` | 注册新 WPF 组件检查 |
| `build-support/Verify-TwitchReadOnly.py`（新增） | 隔离 Session、禁止 mutation 的真实只读验证 |
| `docs/twitch-claim-priority-20261117.md`（新增） | 本验收报告 |

既有用户修改未触碰：`CloudLight Blizzard.csproj`（当前已为 3.0.0）、`build.ps1`、`installer/app.iss`、`build-support/Build-TwitchConfigCandidate.ps1`，以及已有未跟踪的产物、截图和发布说明。

## 执行配置与交付边界

实际权限是 `danger-full-access`，审批策略 `never`，与读取的全局配置一致；本轮未更改模型、推理或权限设置。全局文件记录 `gpt-6.1-sol` / `xhigh`，运行时未提供可独立核验的精确模型与推理强度，不能将配置值冒充实际遥测。全局多代理开关均为 false，本轮未创建或调用子代理。

未修改系统配置、认证信息或安装目录；未提交、推送、打 tag 或发布。最终执行 `git diff --check` 与 `git status --short`，保留工作区原有修改。
