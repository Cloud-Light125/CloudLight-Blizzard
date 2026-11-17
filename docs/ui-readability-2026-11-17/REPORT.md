本轮继续在 `ui/fluent-v2` 上工作，基线为 `6084972626c0f081767da9d7887a51979c168c30`。统一奖励卡片、只读视觉投影及布局；Worker/API/观看协议和真实账号配置没有改动。未提交真实 Claim，未合并 main，未创建正式 Release。

[完整截图索引](screenshots.md) · [机器可读验收记录](validation.json) · [763 条 UI 契约映射](../ui-redesign-v2/feature-mapping.md)

**卡片与布局**

| 项目 | 最终设计 |
|---|---|
| 奖励卡宽度 | 网格最小 220 DIP、上限 260 DIP；本轮实际约 226.9–248.8 DIP |
| 奖励图片 | 图片区高 120 DIP，Uniform 完整显示，解码宽度 384 像素 |
| 奖励名称 | 15 DIP、SemiBold、20 DIP 行高；保留两行，超出省略并提供完整 Tooltip |
| 官方分钟 / 数值 | 15 DIP、SemiBold，与本地估算分别显示 |
| 百分比 | 17 DIP、Bold |
| 状态 / 阶段 / 剩余 | 13 DIP；徽标同时使用文字、符号和边框区分 |
| 活动标题 | 18 DIP、SemiBold |
| 进度条 | 8 DIP |
| 操作 | 详情最小高 34 DIP；原领取入口最小高 36 DIP |
| 卡间距 | 14 DIP |
| 高度 | 按内容测量；各行使用各自行高，不给所有奖励固定高度 |

列数使用奖励面板实际可用宽度计算：`floor((availableWidth + 14) / (220 + 14))`，窄时减少列数。分组内边距和滚动条先占用自身空间，不使用显示器分辨率决定列数。相邻的小活动在半行仍能容纳完整卡片时双列展示，大活动保持完整宽度；保留排序和活动分组，不复制任何奖励。无可配对邻项的小活动仍可能保留横向留白。

详情窗口宽度根据实际奖励数量确定，高度随内容调整。首次活动列表测量即约束视口高度，避免真实账号的全部图片控件在 Loaded 之前同时实例化；后续保留活动行虚拟化、异步图片缓存与已有图片切换/取消保护。

**四尺寸测量**

下表用明确标记的密集模拟样本测量网格容量，避免把真实活动只有两件奖励误写成只能排两列。内容视口还需扣除分组内边距及滚动条后才是奖励面板宽度。

| 窗口像素尺寸（125%） | 内容视口 DIP | 卡宽 DIP | 列数 | 概览首屏完整卡 | 活动首屏完整卡 | 滚动顶部信息后 |
|---|---:|---:|---:|---:|---:|---|
| 1366×768 | 812.4 | 248.8 | 3 | 0 | 0 | 概览 / 活动均可完整阅读 3 张 |
| 1600×900 | 987.6 | 226.9 | 4 | 0 | 4 | 概览可完整阅读 4 张 |
| 1920×1080 | 1243.6 | 229.9 | 5 | 5 | 5 | 可完整阅读首行 |
| 2560×1440 | 1755.6 | 233.4 | 7 | 11 | 11 | 首活动 11 张均完整 |

这些样本首行卡高约 323.2–345.7 DIP，取决于该行资格/前置条件说明。名称允许预期的两行省略；其余卡片文字边界、按钮、进度条和横向滚动检查均通过，无文字越出卡片、无奖励横向滚动。小窗口顶部账号与筛选信息需要滚动后才能完整阅读奖励，已保留初始截图及 reading 截图，没有把滚动后数量当作首屏数量。

当前物理显示环境为 120 DPI（125%）。真实 WPF 窗口按目标像素 / 1.25 设置 DIP 尺寸。自动截图来自 WPF RenderTargetBitmap，96 DPI 输出内容区域；PNG 像素数不含系统边框，不能据此推断改变了显示器 DPI。另有 Windows 原生窗口截图证明实际鼠标打开了活动详情与奖励详情。528 组布局检查还覆盖 100% / 125% / 150% 的等效 DIP 尺寸，并未切换物理显示器 DPI。

**共享组件代码证据**

| 页面 / 数据 | 共享入口与证据 |
|---|---|
| Twitch 概览、全部活动、奖励队列、活动详情 | [CampaignInventoryView](../../Controls/CampaignInventoryView.xaml) 的唯一 RewardTileTemplate 使用同一 [RewardCard](../../Controls/RewardCard.xaml)；所有 Campaign 都由 ItemsSource 渲染，没有活动名称或 ID 特例 |
| 奖励详情、多 Benefit | [VisualRewardRow](../../Themes/Visuals.xaml) 与 [原详情模板](../../Views/Drops/Shared/DropsResources.xaml) 复用 RewardCard；保留原 Claim 控件、资格与前置详情 |
| 自适应列数、每行行高 | [RewardTilePanel](../../Controls/RewardTilePanel.cs)；账号网格可按少量账号伸展，Twitch 奖励卡保持合理上限 |
| Campaign / Drop / Benefit | [TwitchInventoryModel](../../ViewModels/Visuals/TwitchInventoryModel.cs)；每个 Benefit 保留自己的 ID/名称/图片，引用所属同一个 Drop；分钟和统计仍以 Drop 为单位 |
| 完成 / 待领取 / 已领取 / 前置 / 资格 / 结束 | [RewardVisual](../../ViewModels/Visuals/RewardVisual.cs) 的 StateKind、StateBadge、StateText；100% 不等于已领取，未知前置条件显示待确认 |
| SOOP | [SoopOverview](../../Views/Drops/Soop/SoopOverview.xaml) 使用共享奖励卡；[PlatformOverviewModel](../../ViewModels/Visuals/PlatformOverviewModel.cs) 逐 UID 过滤其官方阶段，保留账号独立启停、刷新和详情 |
| Bilibili | [BilibiliWorkspace](../../Views/Drops/Bilibili/BilibiliWorkspace.xaml) 与 [任务只读投影](../../ViewModels/BilibiliDropsViewModel.cs)；任务、Worker、观看 Session 和官方任务状态分别显示；直播间管理保持默认展开及原命令 |
| 主程序总览 | [DashboardView](../../Views/Dashboard/DashboardView.xaml) 显示 Logo、账号、Worker、任务、官方进度、缩略图/占位、刷新时间及下一操作；未登录显示连接入口 |

SOOP 显示官方 viewTime，Bilibili 保持自己的任务数值语义。两者未补造奖励图片或猜测图片 API。YouTube 仅保留现有 UI 与视觉体系，本轮未维护其业务。

**数据与实际 WPF 验收范围**

Twitch 回放来自已有只读官方快照，采集时间为 2026-11-16 23:12:09 +08:00，包含 96 Campaign、337 Drop、358 Benefit；复用已有官方图片缓存。回放运行在隔离目录中，Worker 不运行，未访问或修改原始认证文件。界面明确标注历史快照，不能把这些截图当作 11 月 17 日的新官方分钟或实时观看证明。

官方回放覆盖 1 / 2 / 3 / 5 件以上奖励、未完成、已完成、待领取、资格限制、结束活动、以及 Palia 3 Drop / 8 Benefit。快照没有已领取和未开放样本，这两类由明确标记的模拟数据补充；未混入真实活动。SOOP、Bilibili 的账号任务及 Session 状态为隔离模拟，展示的是实际 WPF 页面，未执行真实账号观看或领取。真实账号联网端到端验证不属于本轮已通过证据。

**最终检查**

| 检查 | 结果 |
|---|---|
| Twitch Python 契约 / 图片元数据 | 84 passed，13 subtests passed |
| SOOP Python 协议 / viewTime 回归 | 43 passed，6 subtests passed |
| Bilibili Python 协议 | 21 passed，2 subtests passed |
| WPF 图片加载、缓存、取消、多 Benefit 和映射 | PASS；[日志](checks/images-and-mapping.txt) |
| FeatureSelfTest | OVERALL: PASS；[日志](checks/feature-selftest.txt) |
| 全布局 / 草稿 / 授权取消 | 528 组布局，PASS，Binding errors = 0；[日志](checks/layout-matrix.txt) |
| 密集 / 全状态卡片与深色矩阵 | PASS，Binding errors = 0；[日志](checks/simulated-cards.txt) |
| 官方快照全活动 / 四尺寸 / 跨平台深浅色 | PASS，Binding errors = 0；[日志](checks/official-cards.txt) |
| UI/Host 回归 | 345 PASS，0 FAIL，1 BLOCKED；[日志](checks/ui-host.txt) |
| 原 UI 契约静态迁移映射 | 763 / 763，待核对 0；静态映射不等同真实账号操作 |
| Release publish | Exit 0；[日志](checks/release-publish.txt) |

UI/Host 唯一未通过的必要项是实际系统剪贴板写入/读回：其他程序占用剪贴板，HRESULT=0x800401D0。没有强制解除外部占用，也没有把 BLOCKED 当作通过。真实 JSONL 握手、generation、停止/取消隔离及其余 UI/Host 项目通过。

Python 各平台测试在独立进程运行，防止同名 worker 模块互相污染。Bilibili 测试依赖装入本轮 test-output 隔离目录；清华 TUNA 未返回匹配 httpx 包，之后使用官方 PyPI，未修改系统或用户包源。Release 构建复用既有 Worker 构建产物，Integrations/Drops 源码没有变更。

本任务实际配置为 gpt-6.1-sol / xhigh、danger-full-access、never，multi_agent=false；与读取到的全局默认一致，没有任务级覆盖，没有使用子代理。GitHub 查询和推送使用当前命令的 127.0.0.1:7897 代理，不修改永久网络设置。
