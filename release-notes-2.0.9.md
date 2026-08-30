# CloudLight Blizzard 2.0.9

本测试版本与 CloudLight Blizzard 2.0.8 功能完全一致，仅将产品版本、程序集版本和安装器版本更新为 2.0.9，用于验证从 2.0.8 到 2.0.9 的在线更新流程。

## 在线更新

- 设置页发现新版本后可直接打开官方 release 页面或在线下载更新。
- 启动更新提醒提供“稍后提醒”“打开更新链接”“在线更新”入口。
- 在线下载显示实时进度、百分比以及已下载/总大小，并沿用 CloudLight Blizzard 当前代理配置。
- 下载完成后自动启动官方 Inno Setup 安装程序。
- 下载器严格校验 Cloud-Light125/CloudLight-Blizzard 的 HTTPS release asset URL、版本文件名、声明大小和 Windows 安装程序格式。

## 更新服务与仓库迁移

- 保持 GitHub 仓库迁移到 `Cloud-Light125` 后的 release 与 installer 链接有效。
- 保持 Worker 的 GitHub Release 元数据请求认证，避免匿名 API 限流导致更新服务不可用或返回 HTTP 502。
- 保持公告服务、更新服务和网络代理诊断正常工作。

## 其他

- 继承当前 `main` 中已经合入的 Drops 恢复、公告刷新和网络诊断改进。

**完整变更**：https://github.com/Cloud-Light125/CloudLight-Blizzard/compare/v2.0.8...v2.0.9
