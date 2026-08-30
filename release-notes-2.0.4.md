# CloudLight Blizzard 2.0.4

本版本重点优化 Twitch 掉宝奖励页面布局，并增加应用内手动领取能力。

## Twitch

- “掉宝进度与奖励”和“当前频道与可用频道”改为全宽上下布局，提升奖励信息的可读性。
- 在奖励卡片标题行增加“打开背包”按钮，可直接打开 Twitch Drops Inventory。
- 已完成且可领取的奖励现在会显示“领取”按钮，并提供领取中状态与重复点击保护。
- 手动领取复用 Twitch Core 的真实 ClaimDrop 流程，成功后刷新 Inventory 并确认领取状态。
- 已完成但暂时不可领取的奖励会显示“已完成 · 等待可领取”，不会错误触发领取。
- 已完成奖励不再显示超过需求时长的进度，例如 `300 / 60 分钟`。
- 领取失败提示和日志改为简洁中文，不向界面暴露底层异常信息。
- 保留关闭自动领取后 completed-but-unclaimed 奖励继续推进后续掉宝阶段的行为。

## 安装

- 适用于 Windows x64。
- 需要 .NET 8 Windows Desktop Runtime x64；未安装时安装程序会提供官方下载入口。

**完整变更**：https://github.com/Cloud-Light125/CloudLight-Blizzard/compare/v2.0.3...v2.0.4
