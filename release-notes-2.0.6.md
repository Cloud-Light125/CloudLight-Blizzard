# CloudLight Blizzard 2.0.6

## 公告、反馈与网络

- 新增应用内公告入口、未读蓝点、按公告 revision 记录已读状态，以及本地缓存回退。
- 新增用户反馈面板，可选提交经过脱敏和大小限制的诊断日志，并显示真实上传进度与服务端处理阶段。
- 公告与反馈正式接入已部署的 Cloudflare Worker；桌面端不包含 GitHub Token 或 PAT。
- 公告、反馈、软件更新和 Drops 统一复用应用的 `EnableProxy`、`ProxyUrl`、`FallbackDirect` 设置。
- 公告和更新的幂等 GET 请求支持代理失败后直连一次；反馈 POST 不自动重传，避免重复提交。
- 网络日志会记录 Proxy / Direct、代理 host/port、HTTP 状态和异常类型，不记录代理凭据。

## 区服文件

- 增强智能差异备份（VerifiedDifference）与完整快照兼容逻辑。
- 新增进一步验证、区服文件状态检查、临时文件清理和重设当前区服状态流程。
- 改进游戏更新、未知版本、部分备份异常和 BestEffort 恢复时的状态提示与逐文件处理。

## Drops 与运行环境

- 修复掉宝页面右上角“刷新”按钮与公告铃铛重叠的问题。
- 改进 Twitch 登录、清除登录、连接状态、手动领取和刷新流程的稳定性。
- 改进 SOOP、Twitch、YouTube Worker 的 JSONL 协议、错误分类与敏感信息脱敏。
- 三个冻结 Worker 统一打包 `_ssl`、OpenSSL 及实际运行依赖，并在完整构建和最终 publish 布局中执行隔离 SSL 自检。
- 修复 Windows PowerShell 管道 UTF-8 BOM 导致冻结 Worker 无法解析首条 JSONL 命令的问题。

**完整变更**：https://github.com/yundan125/CloudLight-Blizzard/compare/v2.0.5...v2.0.6
