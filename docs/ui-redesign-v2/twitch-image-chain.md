# Twitch 图片透传：字段来源与验收边界

## 直接复用的上游能力

没有新增图片 API、GQL 查询、OAuth 权限，也没有修改观看或领取协议。唯一 Python 增量是把 Core 已经拥有的对象投影到原 JSONL 数据结构。

| 上游字段 | 本机来源 | JSONL 新字段 |
|---|---|---|
| Benefit.id / name | core/inventory.py 的 Benefit | drops[].benefits[].id / name |
| Benefit.type.value | BenefitType 枚举 | benefits[].type，完整保留 DIRECT_ENTITLEMENT / BADGE / EMOTE / UNKNOWN |
| Benefit.image_url | benefit_data["imageAssetURL"] | benefits[].imageUrl |
| DropsCampaign.image_url | data["game"]["boxArtURL"]，上游 remove_dimensions | campaigns[].gameImageUrl、drops[].gameImageUrl |
| BaseDrop.id / campaign.id | 已有 Core 对象 | inventory[].id / campaignId、currentDrop.id / campaignId |
| BaseDrop.benefits 全列表 | 上游逐 Benefit 显示，不取第一项 | benefits 数组与 benefitIds，顺序与 Core 相同 |
| precondition_drops | Core 已有前置任务 ID | preconditionDropIds |
| preconditions_met / preconditions_satisfied_for_mining | 原 Core / NoAutoClaim 逻辑的只读结果 | preconditionsMet / preconditionsSatisfiedForMining |
| real_current_minutes / extra_current_minutes | 原官方分钟与本地估算 | currentMinutes / estimatedExtraMinutes，分别保留 |
| watch_requirement_completed / is_claimed / can_claim | 原 Core 状态 | completed / claimed / canClaim，未改变策略 |
| campaign.eligible | 原 Core 资格 | eligible；无数据时为 null，不冒充“不满足” |

get_campaigns 增加封面及各 Drop 的 Benefit 元数据；get_inventory 与 currentDrop 共用 _drop_snapshot。Campaign 嵌套 Drop 只增加图片/关联信息，不额外执行领取或资格计算。JSONL 是原来的可选字段扩展，旧字段及原值保留。

本地 Integrations/Drops/twitch/gui.py 是 headless shim；为核对原桌面行为，读取了仓库已声明的上游提交 [22d0c613 的 gui.py](https://github.com/DevilXD/TwitchDropsMiner/blob/22d0c6134f9291d1e904012c465504a22bd3f97c/gui.py#L1464)：封面按 campaign.image_url 显示，Drop 对每个 benefit.image_url 并发取图，并逐项展示名称。没有引入该文件或 Tkinter 到 WPF。

visual_metadata.py **只有必要的字段映射与 HTTPS URL 校验**，不抓取、不缓存、不查询、不做业务判断。此前“只取第一个 Benefit 的 imageUrl”已删除。

## Host 与 WPF 路径

DropsHostService.StampState 克隆全部 JSON 属性，只添加既有 generation/sequence 元数据，不过滤 benefits。生产 ApplyState 仍先经过原 generation、sequence、停止状态保护，再进行只读投影。

- 当前：TwitchVisual.ActiveReward → Benefits[] → RewardHero → RewardArtwork → BenefitGallery → VisualImage.SourceUrl。
- 概览队列：TwitchVisual.RewardPreview[] → 同一 RewardVisual / RewardArtwork。
- 全部奖励及详情：DropsRow.Payload → DropsRow.Visual（同一 RewardVisual.From）→ RewardArtwork / BenefitGallery；原领取按钮与 IsClaiming 保护保留。
- 活动：DropsRow.Payload.drops[] → CampaignRewards[] → 同一 RewardVisual / RewardArtwork；封面使用 CampaignImageUrl。
- 应用总览：PlatformOverviewModel.FeaturedReward → 同一 RewardArtwork。

当前奖励只按 Core currentDrop.id 关联 inventory.id，Campaign 关联用 campaignId，Benefit 关联用每个 benefit.id。不会用背包第一项或统一平台 Logo 冒充当前奖励。所有 Benefit 均显示，缺图时逐项明确降级。

## WPF 缓存适配

借鉴现有 core/cache.py 的异步锁、7 天生命周期、内存复用、损坏恢复和失败回退；使用 .NET 异步 HTTP 与冻结 BitmapSource，未使用 ImageTk / PhotoImage。

相同 URL 串行合并，其他图片限流并行；7 天滑动过期；32 MiB / 64 项内存上限，128 MiB 磁盘上限；单图片 8 MiB；10 秒超时；切换和 Unloaded 取消；损坏缓存重新获取。失败短暂记忆 30 秒避免重试风暴。文件名是 URL 的 SHA256，不包含 Token 或签名参数，服务不记录完整 URL。统一占位是普通图片轮廓与失败说明，不使用平台 Logo 冒充物品。

## 当前验证

- Python 新增 5 项 DTO 检查通过：多 Benefit ID/类型/顺序、缺图、只读性、currentDrop 全字段与官方 18/60、估算 +2、前置任务及资格。
- 原 Headless GUI 契约 14 项通过。
- .NET / 实际 WPF 组件 24 项通过：关联、全部 Benefit 渲染、官方/估算分离、UID 隔离、缓存复用与并发合并、7 天过期、损坏恢复、超时、取消、格式错误、散列文件名、缓存上限、快速切换与卸载取消。
- **真实当前奖励图片验收 BLOCKED**：隔离环境尚无已授权 Core 的真实 Campaign/Benefit 快照；未读取用户 Session，未发起真实授权或观看。上游 gui.py 自带的调试 CDN 资源、历史公告插图和测试像素图均不算真实当前奖励验收。

可把已脱敏的 Core load_state 结果保存为显式隔离验收目录的 core-visual-state.json 后回放，验收模式不启动 Worker。仅保留展示字段，去除旧 Host generation；生产保护逻辑不受影响。必须另行记录真实字段的来源、图片 HTTP 加载结果和实际 WPF 截图，才可解除图片 BLOCKED。

## 其他平台

SOOP：已检查本机 D:/code/cloudlight soop drops miner/models.py 的 DropItem / Mission / InventoryItem 与当前 worker 适配。现有类型只提供物品名、时长、领取状态等，没有已验证的奖励图片 URL 属性；raw 虽保留响应，但未找到可验证图片键，不能推断接口永远不支持。当前不臆造字段或新接口，沿用统一占位与真实官方进度。

Bilibili：已检查 vendor/client_parts/models.py 与 tasks.py；TaskProgress、TaskCheckpointProgress、MissionRewardInfo 没有奖励图片字段。WBI 的 img_url/sub_url 是签名键来源，**不是奖励图片**，不用于展示。未修改 Bilibili 协议。
