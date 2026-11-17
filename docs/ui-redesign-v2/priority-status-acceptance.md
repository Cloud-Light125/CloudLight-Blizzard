# Twitch 优先概览、紧凑奖励卡片与观看状态验收

日期：2026-11-17（Asia/Shanghai）。分支 `ui/fluent-v2`，基线 `e77f7db9ca683023ca06216bd2776c23ad3df3a2`。

实现提交 `1a1f78b`；候选构建提交 `0124a20c306aab3bfa8cfe877aa8b2c40e588e9d`。后续交付提交仅保存本文、截图和验证记录，不改变候选的应用源码。没有合并 main、创建 Release 或 Tag。

## 实现

- 概览使用现有 `TwitchPriorityGames`，按用户列表顺序置顶；同游戏内依次参考有近期请求证据的当前目标、可参与未完成任务、待领取、结束时间与 Campaign ID。去掉前四项截断；相同输入与用户顺序保持确定性排序。
- 有官方 `gameId` 时通过唯一官方名称→ID 对应关系匹配；目前 Core 未提供 ID 的记录按完整官方名称匹配，忽略大小写与首尾空格。拒绝子串猜测和存在多个 ID 的歧义名称。
- `AppSettings.TwitchOverviewPriorityOnly` 独立保存，默认 false。顶部下拉框不会发送 `save_settings`、写入 `priority_mode`、更改优先游戏顺序或启动 Worker。完整背包和队列不受此显示范围影响。
- 优先列表为空时提供“尚未设置优先游戏”和设置入口；已设置但没有活动时提供“当前优先游戏暂无符合条件的活动”和查看全部入口。全范围、无优先列表时仍展示全部活动并提示设置。
- 公共 `RewardCard`、`RewardTilePanel`、`CampaignInventoryView` 和详情使用的 `VisualRewardRow` 同步调整。图片 92 DIP；卡宽 190–216 DIP；间距 12 DIP；名称 15、分钟 14、百分比 16、状态 13 DIP；进度条 7 DIP；详情按钮至少 32 DIP。名称支持两行、省略和完整 Tooltip，重复 Drop 标题与前置说明移至 Tooltip/详情。单奖励和双奖励容器限制宽度，相邻小活动在空间足够时双列排列。
- 奖励状态仅由官方进度、完成、待领取、已领取决定。资格/前置条件另行显示。“部分完成”不表示观看，卡片不附加推断出的观看徽标。
- Twitch 观看证据要求运行、匹配频道、近期且不在未来的接受时间，停止/失败/恢复会失效；精确关联 Campaign+Drop，其他 Drop 不继承。HLS 接受只显示等待官方进度，后续观察到对应 Drop 的官方分钟增长才确认增长。停止保留官方分钟、同步时间和上次频道。
- SOOP 按 UID 保留官方任务，登录、运行、频道资格、连接、桥接、成功心跳和时间窗口分别判断。官方 viewTime 增长在同 UID/任务间比较，停止或连接失败撤销观看证据；共享 Worker 不把一个账号的状态传给另一个账号。
- Bilibili 的有效活跃数受运行、登录、实际 active 记录、近期观测与连接状态约束。Worker Running + 0 Session 显示尚未建立有效观看；有 Session 时仍标记官方进度待确认。任务原“进行中”默认值、停止后的活跃数和重启残留同时修正。
- 总览与平台页面复用相同只读投影。历史回放强制标明非实时；模拟请求与模拟账号截图另有水印。

## 最终候选回归

| 检查 | 结果 |
|---|---|
| Twitch + Shared Python 契约 | 87 PASS，13 subtests PASS |
| SOOP Python 状态/契约 | 43 PASS，6 subtests PASS |
| Bilibili Python 状态/契约 | 21 PASS，2 subtests PASS |
| Inventory 投影、优先排序/独立显示、状态矩阵、图片缓存与实际 WPF 组件 | 95 PASS，0 FAIL |
| UI/Host | 347 PASS，0 FAIL，0 BLOCKED |
| FeatureSelfTest | 13 组 PASS，OVERALL PASS |
| UI 功能迁移静态映射 | 763/763，0 待核对；不等于 763 次真实账号操作 |
| 全页面实际 WPF 布局 | 44 场景 × 12 等效 DIP 组合 = 528，0 Binding 错误 |
| 紧凑密度及跨平台卡片覆盖 | PASS，0 Binding 错误 |
| 官方历史快照及优先范围实际 WPF 验收 | 24 个尺寸/主题/页面组合，见附带完整记录 |
| Release publish | PASS，0 编译警告/错误 |
| Worker 来源/哈希 | 四平台均校验通过，未修改/重建 Worker |

Python 进程按平台隔离运行。Bilibili 首次收集因系统 Python 缺少 httpx 失败，随后使用项目已有 `.worker-build-venv` 通过，没有安装依赖。候选脚本原来读取旧格式 `workers`/`commit` 字段，已兼容原候选实际使用的 `workerBuilds.sha256`/`sourceCommit`，并保留哈希校验。基线最初放在项目内导致 WPF 自动扫描到重复 XAML，已将其移到项目外的工作区隔离目录后重新构建通过。

最低十项要求均包含在回归内：A/B/C 顺序、只筛概览、显示与挂机策略独立、停止+25/120、Running 无请求、精确当前 Drop、SOOP UID、Bilibili 零 Session、官方值不叠加估算、历史快照不能变成实时观看。还覆盖排序调整、同游戏多活动、资格不足、已结束活动、后台刷新、磁盘重开与旧设置默认值、失败/恢复、已完成/待领取/已领取、多 Benefit。

## 四尺寸与新旧对比

使用同一份官方历史数据：97 个 Campaign、359 个 Drop、380 件 Benefit。快照文件 SHA256 为 `4a8bc8ab3fb37d39a1c08b321d194fe314cd8838df1b8a9704d87af6cf33a685`。仅复制展示字段与 40 张哈希命名的图片缓存，不复制认证或 Session。优先列表只在隔离 UI 中配置。

| 请求窗口，125% DPI | 基线概览首屏完整卡片 | 新概览首屏完整卡片 | 新首行列数 |
|---|---:|---:|---:|
| 1366×768 | 0 | 3 | 3 |
| 1600×900 | 0 | 4 | 4 |
| 1920×1080 | 2 | 5 | 5 |
| 2560×1440 | 2 | 8 | 5（首活动共 5 件，其余活动继续显示） |

基线为精确 e77f7db 源码独立构建。新概览的首活动因优先排序而改变，因此首屏卡片数反映整体效果；不能把不同活动的卡片数当作单纯尺寸变化。独立尺寸变化：图片 120→92 DIP；基线代表卡高 323.2 DIP，新常见卡高 221.3–241.3 DIP；资格提示/长标题可能增加一行。实际内容宽度决定列数，少量奖励不拉伸填满整行。

当前物理显示 DPI 为 120（125%），四个请求尺寸均使用实际 WPF 窗口与控件布局，PNG 捕获客户区。528 组合中 100%/150% 是等效 DIP 布局，不声称切换了系统物理 DPI。新 PNG 按当前 DPI 渲染，旧基线 PNG 使用基线自带的 96 DPI 捕获方法；尺寸比较以上表 DIP 指标为准。

## 截图

以下官方图片及分钟来自历史快照；观看请求、SOOP/Bilibili 运行场景为明确标记的模拟数据。

- [全部活动、优先游戏置顶](screenshots-priority/01-priority-all.png)
- [仅显示优先游戏](screenshots-priority/02-priority-only.png)
- [优先列表为空](screenshots-priority/03-priority-empty.png) / [优先游戏暂无活动](screenshots-priority/04-priority-no-campaign.png)
- [停止观看、部分完成](screenshots-priority/05-stopped-partial.png)
- [Twitch 活动页](screenshots-priority/06-twitch-activities.png)
- [选中频道但没有请求证据](screenshots-priority/07-selected-unconfirmed-SIMULATED.png) / [请求接受、官方待确认](screenshots-priority/08-request-accepted-SIMULATED.png)
- [基线旧卡片](screenshots-priority/before-e77-overview-1600.png) / [新紧凑卡片](screenshots-priority/05-stopped-partial.png)
- 四尺寸：[1366](screenshots-priority/overview-1366x768-light.png)、[1600](screenshots-priority/overview-1600x900-light.png)、[1920](screenshots-priority/overview-1920x1080-light.png)、[2560](screenshots-priority/overview-2560x1440-light.png)、[深色](screenshots-priority/overview-1600x900-dark.png)
- [SOOP A 停止、B 活跃](screenshots-priority/soop-active-SIMULATED.png) / [SOOP 全部停止](screenshots-priority/soop-stopped-SIMULATED.png)
- [Bilibili 零 Session](screenshots-priority/bilibili-running-empty-SIMULATED.png) / [多 Session、官方待确认](screenshots-priority/bilibili-active-SIMULATED.png) / [恢复中](screenshots-priority/bilibili-recovering-SIMULATED.png)
- [单奖励](screenshots-priority/campaign-one.png)、[双奖励](screenshots-priority/campaign-two.png)、[多奖励](screenshots-priority/campaign-many.png)、[多 Benefit](screenshots-priority/campaign-multi-benefit.png)、[已领取模拟覆盖](screenshots-priority/state-claimed-SIMULATED.png)

## 来源与边界

候选目录：`release/candidates/2.1.10-priority-status-UI-V2-Checked/`，精确来源、各 Worker 原始提交及校验值见本地 `candidate.json`。应用 DLL SHA256：`359f3f15492f3340b7c03000c139855f39c2c85c48c95a85afa9b06f0987089f`。

验证记录见 `priority-evidence/`。本轮未执行真实账号登录、真实 HLS 观看、真实奖励领取、SOOP/Bilibili 线上故障注入；历史数据与模拟状态不能替代这些外部业务测试。真实 Session、账号配置、游戏文件、OAuth、观看协议、NoAutoClaim、UID 保护与 Worker Generation 保持不变，YouTube 未扩展业务维护。

主代理独立实施，无子代理。可见用户配置 `gpt-6.1-sol / xhigh`、`multi_agent=false`；实际任务精确模型/推理档位未单独暴露。实际 `danger-full-access / never` 与可见全局一致，未修改模型、权限、安全设置、系统代理或 Git 全局代理。GitHub 仅对当前命令使用 `http://127.0.0.1:7897`；旧 origin 已验证重定向到 `Cloud-Light125/CloudLight-Blizzard`。
