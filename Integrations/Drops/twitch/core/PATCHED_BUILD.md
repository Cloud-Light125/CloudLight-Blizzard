# TwitchDropsMiner 16.dev Patched Build

本仓库包含基于 [DevilXD/TwitchDropsMiner](https://github.com/DevilXD/TwitchDropsMiner)
`16.dev` 的完整补丁源码，以及对应的 Windows Release。

This repository contains the complete patched source based on
[DevilXD/TwitchDropsMiner](https://github.com/DevilXD/TwitchDropsMiner) `16.dev`,
together with the corresponding Windows release.

## CloudLight vendored baseline / CloudLight 内置基线

CloudLight Blizzard 的 headless Worker 最初集成
`yundan125/TwitchDropsMiner-NoAutoClaim` 提交
`3071a8a43488da9dbf06a6e9ca0e4057a5ad47cf`。该补丁源码以
`DevilXD/TwitchDropsMiner` 提交
`3aec843bbb91a7d00f7bd7784e92bd97e3ee8a5f` 为基础。

Twitch API/GQL 兼容数据已定向同步至 upstream 提交
`22d0c6134f9291d1e904012c465504a22bd3f97c`（2026-09-26）：

- `a3fda85`：Inventory 与 Campaigns persisted-query hash；
- `0e4bc43`：Campaigns persisted-query hash 后续更新；
- `825bde3`：将 `request cancelled` 归类为可重试 GQL 临时错误；
- `d21c0d2`：Web、Mobile Web 与 Smartbox User-Agent 更新至 Chrome 153。

CloudLight 额外限制 GQL 临时错误的最大尝试次数，并为
`PersistedQueryNotFound` 提供明确诊断；不会猜测或自动替换 hash。
upstream 的 Tk GUI、Linux AppIndicator 与启动入口改动不属于 headless Worker，
未被引入。NoAutoClaim、JSONL 协议、代理、Session、日志和退出流程补丁继续保留。

## 补丁行为 / Patch behavior

补丁在设置界面加入“自动领取掉宝”开关，默认开启并持久化到原有设置文件。
关闭后，程序不发送自动领取请求，并尝试跳过已经完成观看要求但尚未领取的掉宝。
重新开启后，程序会在后续库存刷新时恢复原有领取流程。

The patch adds an “Automatically claim Drops” setting. It defaults to enabled
and persists through the existing settings system. When disabled, automatic
claim requests are not sent and locally completed-but-unclaimed Drops are
skipped where possible. Re-enabling restores the normal claim flow on a later
inventory refresh.

Twitch 可能要求先领取前置掉宝，才能为同一活动的后续掉宝记录服务端进度。
补丁不会绕过 Twitch 服务端规则，也不会伪造进度。未领取掉宝可能过期。

Twitch may require prerequisite Drops to be claimed before later Drops in the
same campaign gain server-side progress. This patch does not bypass Twitch
rules or fabricate progress. Unclaimed Drops may expire.

## 下载和校验 / Download and verification

Windows 单文件程序请从 [Releases](../../releases) 下载：

Download the Windows executable from [Releases](../../releases):

```text
TwitchDropsMiner.exe
SHA-256: 58C7B719496F448CA5E6153724E86334EF551ADF3682AF8DDD929A152250592A
```

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath ".\TwitchDropsMiner.exe"
```

## 隐私 / Privacy

仓库和 Release 均不包含 `cookies.jar`、`settings.json`、缓存、日志、登录令牌、
代理认证信息或其他用户凭据。不要将这些运行时文件提交到公开仓库。

Neither the repository nor its releases contain `cookies.jar`, `settings.json`,
cache, logs, login tokens, proxy authentication information, or other user
credentials. Do not commit these runtime files to a public repository.

## 许可和归属 / License and attribution

原项目由 DevilXD 开发并采用 MIT License。本仓库不是 Twitch、Amazon 或原项目
作者的官方发布。详见 [LICENSE](LICENSE)。

The original project is developed by DevilXD and licensed under the MIT License.
This repository is not an official release from Twitch, Amazon, or the upstream
author. See [LICENSE](LICENSE).
