# CloudLight Blizzard 掉宝整合审计

审计日期：2026-08-20（Asia/Shanghai）

审计基线：

- `Cloud-Light125/CloudLight-Blizzard` `main`：`a3617f6efe200d7472e54a36016bb56bf4e3c0cb`
- `yundan125/cloudlight-soop-drops-miner` `main`：`2959a35c6ad8e1d807dad465c1cd8896719e5086`（本地业务代码与远端一致，仅 README 落后一行）
- `yundan125/CloudLight-Overwatch-YouTube-Watcher` `main`：`8768e8e407a02aa0ee8333e1beb82f87c1960bb2`
- `yundan125/TwitchDropsMiner-NoAutoClaim` `main`：`3071a8a43488da9dbf06a6e9ca0e4057a5ad47cf`

## 1. 持久化文件、设置和登录数据

### SOOP

持久化文件：`settings.json`、旧 `config.json`、`accounts/<userid>/cookies.json`、旧单账号 `cookies.json`、`.disclaimer_accepted`，以及运行日志。账号 Session 按账号目录隔离。

`AppConfig` 的全部字段：

- `settings_version`
- `auto_claim_enabled`
- `low_bandwidth_mode`
- `proxy_enabled`（仅迁移读取，整合后由全局代理覆盖）
- `proxy_url`（仅迁移读取）
- `proxy_fallback_direct`（仅迁移读取）
- `auto_start_enabled`
- `start_minimized_to_tray`
- `close_to_tray`
- `appearance_mode`
- `mission_poll_interval`
- `inventory_poll_interval`
- `channel_refresh_interval`

原 GUI 运行态另有 `ChannelConfig`：`mode`（`smart` / `manual` / `owesports`）、`manual_input`、`preferred_bjid`、`priority_mission_id`、`hang_without_missions`。整合 Worker 将这些字段持久化为 `channel_mode`、`manual_input`、`preferred_bjid`、`priority_mission_id`、`hang_without_missions`，避免 UI 替换后丢失。

登录由 `login.sooplive.co.kr` 返回 Cookie；敏感项包含 `AuthTicket`、`BbsTicket`、`UserTicket` 等。Worker/UI/日志不得返回 Cookie 内容，只返回账号 ID 和 Session 是否存在。

### YouTube

持久化文件：`config.json`、`profiles/<ProfileName>/`、`logs/`、`watch_history.json`。浏览器原生 Cookie、Local Storage 和 Google/YouTube 登录状态均在独立 Profile 目录；不得使用用户日常 Chrome Profile。

`config.json` 全部字段：

- `browser`
- `browser_path`
- `headless`
- `mute`
- `mode`
- `manual_url`
- `check_interval`
- `channels[]`：`name`、`id`、`url`、`enabled`
- `profiles[]`

### Twitch

持久化文件：`settings.json`、`cookies.jar`、`cache/mapping.json` 与图片缓存、`log.txt`、`lock.file`、可选 `dump.dat`。`cookies.jar` 是敏感登录 Session，禁止显示、记录、上传或提交。

`SettingsFile` 除旧代理字段外的全部字段：

- `language`
- `dark_mode`
- `exclude`
- `priority`
- `autostart_tray`
- `connection_quality`
- `tray_notifications`
- `enable_badges_emotes`
- `available_drops_check`
- `auto_claim_drops`
- `priority_mode`（`PRIORITY_ONLY` / `ENDING_SOONEST` / `LOW_AVBL_FIRST`）

旧 `proxy` 只用于迁移到 CloudLight Blizzard 全局代理。登录使用 Twitch Device OAuth/现有 CookieJar；Session 持久化到 `cookies.jar`。

## 2. 网络客户端与 WebSocket

### SOOP

- `aiohttp.ClientSession` 由每账号 `AccountNetworkContext` 创建，CookieJar、连接池和流量统计互相隔离。
- HTTP/HTTPS：登录、Drops API、直播搜索/直播状态、播放器心跳、Bridge 初始化。
- WebSocket：`BridgeSession` 和 `AccountWebSocket`；代理由统一 `AppConfig` 注入 HTTP 与 `ws_connect`。
- `proxy_fallback_direct` 在账号请求和 WebSocket 建连路径均有回退语义。

### YouTube

- `yt-dlp`：直播元数据和直播状态。
- `requests`：公开频道页回退、Chrome DevTools HTTP。
- `websocket-client`：Chrome DevTools Runtime evaluate、播放状态采样和恢复。
- Chrome/Brave 子进程：独立 `--user-data-dir`、本机随机调试端口。
- 全局代理应用到 yt-dlp、公开 HTTP 和 Chrome `--proxy-server`；`127.0.0.1` / `localhost` / `::1` 明确 bypass，DevTools 客户端禁用环境代理。

### Twitch

- `aiohttp.ClientSession`：Twitch 页面、OAuth、GQL、图片和观看元数据。
- `WebsocketPool`：最多 8 个分片，订阅用户 Drops/通知以及频道 StreamState/StreamUpdate。
- 原实现的 `Twitch.request` 与 `Websocket._backoff_connect` 均读取 `Settings.proxy`；整合 Worker 在启动前用全局代理覆盖旧值。

## 3. 后台结构

- SOOP：单一 asyncio loop；`MultiMinerManager` 为每账号创建 `SoopMiner` Task；每个 Miner 并行维护心跳、任务/背包轮询、Bridge/WebSocket 和网络恢复。
- YouTube：原实现以 `WatcherService` 的主监控线程 + 5 秒播放采样线程运行；每个 Profile 对应一个 Chrome/Brave 子进程和 DevTools 会话。
- Twitch：单一 asyncio loop；状态机驱动 Campaign/Inventory/Channel 刷新；独立 watching Task、maintenance Task 和 WebSocket 分片 Task。

整合后每个平台是一个独立进程。WPF 退出会发送 `stop`、`shutdown`，超时才终止进程树；Worker 崩溃只改变平台状态，不退出 WPF。

## 4. GUI 耦合与可抽取边界

### 可直接抽取

- SOOP：`auth.py`、`network.py`、`drops.py`、`channel.py`、`center.py`、`watch.py`、`models.py`、`miner.py`、`multi_miner.py`。
- YouTube：配置验证、直播检测、浏览器/DevTools 控制、观看历史和日志轮转的业务概念可独立实现。
- Twitch：HTTP/GQL、Campaign/Inventory/Drop 模型、Channel、WebSocketPool、Settings 序列化和状态机。

### 必须重构/适配

- SOOP：原 `constants.DATA_DIR` 绑定源码或 exe 目录；原 GUI 同时承担 ChannelConfig 构造、账号 CRUD、状态增量刷新和设置保存。Worker 必须注入数据根、全局代理及结构化事件 sink。
- YouTube：原 `WatcherService` 继承 Qt `QObject` 并用 `Signal` 输出；`paths.py` 绑定应用目录；需用与 Qt 无关的事件和停止令牌重做服务壳。
- Twitch：核心大量调用 `GUIManager` 的 status/channels/inventory/login/progress/tray 接口。整合采用无窗口 headless facade，保持状态机和网络核心不变，同时将这些调用转换为协议事件。图片 `ImageTk` 缓存不属于 Worker 必需运行时。

## 5. 统一协议

stdin/stdout 使用 UTF-8 JSON Lines：

- Request：`{"id":"...","command":"start","payload":{}}`
- Response：`{"id":"...","ok":true,"result":{}}`
- Event：`{"event":"status","payload":{}}`

公共命令：`hello`、`load_state`、`start`、`stop`、`refresh`、`save_settings`、`login`、`logout`、`get_accounts`、`get_tasks`、`get_inventory`、`get_logs`、`set_proxy`、`shutdown`。平台命令只扩展，不替代公共命令。

## 6. 许可证与 NOTICE

- TwitchDropsMiner-NoAutoClaim：DevilXD MIT，必须保留 `Copyright (c) 2024 DevilXD`、完整 MIT permission notice、补丁来源说明。MIT 核心作为独立 Worker 组件保留原许可证。
- SOOP：CloudLight 仓库及其直接源码上游 `tom1230123/soop-drops-miner` 当前均未声明许可证。NOTICE 只记录来源，不把上游代码声明为 CloudLight Blizzard GPLv3 原创。发布构建默认不自动复制该核心；需先获得明确再分发授权，或由未接触上游实现的团队进行 clean-room 重写。
- YouTube：CloudLight 项目直接上游 `ucarno/ow-league-tokens` 当前未声明许可证。整合 Worker 依据功能需求和公开接口独立重写无 GUI 服务壳，不复制或再分发上游源码/PySide6 GUI；由于实现者已执行兼容性审计，不能把它表述为有人员隔离的正式 clean-room 流程。

## 7. 最终目录

源码：

```text
Integrations/Drops/
├─ Shared/                 # 协议、原子写入、日志脱敏
├─ soop/                   # Worker 适配层；未授权核心不默认复制
├─ youtube/                # 无 PySide6 的 Worker
├─ twitch/
│  ├─ worker.py
│  ├─ headless_gui.py
│  └─ core/                # DevilXD MIT 核心与原许可证
└─ build-workers.ps1
```

安装目录：

```text
_internal/drops/
├─ soop/soop-worker.exe
├─ youtube/youtube-worker.exe
└─ twitch/twitch-worker.exe
```

用户数据：

```text
<Documents>/CloudLight/CloudLight Blizzard/
├─ drops/
│  ├─ soop/
│  ├─ youtube/
│  └─ twitch/
└─ logs/
   ├─ drops-soop.log
   ├─ drops-youtube.log
   └─ drops-twitch.log
```
