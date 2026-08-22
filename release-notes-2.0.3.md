# CloudLight Blizzard 2.0.3

本版本重点改善 Drops 平台的连接反馈、自动恢复和日志体验。

## Twitch

- 登录按钮现在会立即显示连接进度，并按登录检查、Session 恢复、授权、活动加载和实时连接展示明确状态。
- 网络或代理异常时会提示连接较慢，并在服务不可用时每 60 秒自动重试。
- 等待 Device Code 授权、主动停止、退出登录或 Session 明确失效时不会自动刷新验证码。
- 区分登录状态和实时连接状态，WebSocket 临时断线不会误报为未登录。
- 修复已完成但未领取奖励时继续错误观看的问题。
- 掉宝活动统计改为观看完成进度，不再受自动领取设置影响。
- Twitch 日志改为简洁中文提示，并降低重复网络错误噪声。

## Drops 平台

- 修复 Twitch 与 SOOP 自动启动流程，YouTube 保持手动启动。
- 全局代理设置统一应用于 Drops Worker，并保留代理失败后的直连回退。
- SOOP 奖励领取及兑换码交互更加明确。
- “运行日志”只显示本次启动 CloudLight Blizzard 后产生的内容；完整历史日志仍保留在磁盘。
- UI 日志支持文件截断/重建恢复，“清空显示”不会删除磁盘日志或在刷新后重新出现。
- 修复 SSL Worker 打包和剪贴板调用稳定性问题。

## 安装

- 适用于 Windows x64。
- 需要 .NET 8 Windows Desktop Runtime x64；未安装时安装程序会提供官方下载入口。

**完整变更**：https://github.com/yundan125/CloudLight-Blizzard/compare/v2.0.2...v2.0.3
