# 只读可视化数据契约

Twitch：官方 GQL Campaign/Inventory → Core Benefit.image_url（imageAssetURL）及 DropsCampaign.image_url（game.boxArtURL）→ Worker 增量可选 imageUrl/gameImageUrl/benefits/campaignId → 原 JSONL/Host → 只读 RewardVisual / CampaignVisual / PlatformOverview → WPF。旧字段与含义保持不变，旧 Worker 缺字段时明确占位。

当前奖励只按 Core gui.progress.drop 的 ID 确定；背包第一项不能冒充当前观看奖励。奖励关联使用 campaignId 与 drop id。官方 currentMinutes 与本地 estimatedExtraMinutes 分别显示，官方百分比仅由服务端分钟计算。lastInventorySyncAt 是同步依据，不使用本地计时制造官方增长。

SOOP：每个 UID 的 currentProgress 独立投影；不拿历史最高值替代当前官方进度。Bilibili：原官方 tasks/rooms/sessions 与 QR 状态投影。YouTube：仅消费原账号/频道/会话信息，不改观看核心。

图片缓存只存散列文件名；不记录 URL、认证信息或签名参数。独立超时、取消、限流、TTL、大小限制、损坏恢复与空/失败占位。官方图片来源验证另记，不以测试图替代。
