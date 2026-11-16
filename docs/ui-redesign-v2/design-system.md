# 视觉系统

Windows 11 Fluent 与游戏工具客户端的组合。背景 #F5F6FC，白色表面，品牌紫 #6857F5，Twitch 紫 / SOOP 蓝 / Bilibili 粉 / YouTube 红。14 DIP 正文，13 DIP 次要文本，26 DIP 页标题，18 DIP 卡片标题；小标签不承担正文。

Shell 侧栏 216 DIP，品牌横排；上下文顶部与平台身份独立。主任务采用奖励大图/官方数字进度/状态侧栏的组合；列表是图像、状态和操作共同组成的行。单项设置不使用通栏空白卡。

共享组件职责：PlatformIcon（图形身份），VisualImage（异步下载/缓存/占位），RewardArtwork / BenefitGallery（全部物品），RewardHero（当前奖励与官方进度），MetricTile（真实指标），SettingRow（说明与编辑），AccountTile（Battle.net 账号），PlatformOverviewModel（平台真实状态投影）。图像不阻塞 Dispatcher；只在显式测试模式使用模拟数据。

阶段验收记录写入 visual-acceptance.md。未实测物理 DPI 的环境明确标为等效 DIP。
