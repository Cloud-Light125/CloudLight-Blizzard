# Drops 第三轮定向修复与验收（2026-10-12）

起始：干净 `main`，`e1d9b72952dcdd6df0279eaf71c0b4d1aeadf1b7`；版本保持 **2.1.8 本地候选**。两轮修复记录作为不可回退基线；不改原账号、凭据、历史及系统设置。所有真实测试关闭自动领取，没有提交 Claim。

用户在本轮明确暂停 YouTube/Bilibili 后续真实登录与业务验收。已经完成的匿名播放证据、二维码证据和已确认修复保留；不再要求登录或扫码。测试结果在 Git 忽略的 `test-output/drops-round3/`，不提交日志、图片、凭据或二进制。

## 本轮确定问题与修改

| 问题 | 根因与证据 | 修改 | 验证 |
|---|---|---|---|
| SOOP B 单账号停止卡死，Host 总超时回收共享 Worker，A 一同中断 | 起始 Host 真实 JSONL 探针：50.233 秒超时，generation 1→2，新进程 A/B 均停止；原探针没有 VM，不据此声称原 VM 永远不恢复 | Host 在回收前明确失败 UID 的意图；VM 受控恢复其他仍有意图账号；共享崩溃显示中断，保留最近官方进度 | 七分支真实 Host/JSONL/VM + 模拟账号业务通过；不是双真实账号网络验收 |
| SOOP B 只读刷新超时也可能回收整个 Worker | Host 原先没有针对当前请求的远端取消；Future.cancel 不证明 asyncio 清理已结束 | Shared reader 精确匹配 requestId 的 cancel_read；SOOP tracked coroutine 确认清理；清理成功保留进程，否则回收并受控恢复 | refresh_timeout generation 不变，A/B 继续；快照 10 分钟保留，下次刷新成功 |
| SOOP 全局刷新失败把全部账号误报停止 | 页面只读刷新 catch 调用了全局 SetSoopFailure，混淆请求失败和观看失败 | 只调用 FailSoopRefresh 并提醒；共享崩溃由 Host 事件处理 | WPF 实际刷新入口 + 受控错误响应，账号仍 running，官方 12 分钟不清零 |
| YouTube 画面停滞仍增加本地历史 | 旧计时仅依据 paused/readyState，并固定增加 5 秒 | 相同页面两次媒体位置实际前进才计时；首次/暂停/广告/停滞/停止不累计；停止门禁；播放及历史事件投影 | 三项契约及匿名真实 CDP 播放；已增长 40.172649 秒，buffering 不继续虚增 |
| Twitch Spade 请求接受、WS Connected 与官方时长增长不一致 | 旧路径仅 Spade 204；旧一小时观察和新播放列表补验分别记录，不把 HTTP 成功作为进度成功 | 局部同步官方上游每次请求全部直播分片的 HEAD 路径及 20 秒间隔；保留 Spade 方法、Smartbox 页面兼容、NoAutoClaim/GQL/Chrome 153 | 官方同奖励分钟采样见下文；请求状态与官方进度分开 |
| 播放凭证异常结构、请求时频道下线可能造成属性访问异常；重试提示可能携带 URL 签名 | `data: null`、凭证字段非字符串或异步期间 Stream 被清空；旧提示使用完整 URL | 验证 GQL 对象/字段类型，保留当前 Stream 引用；网络重试仅展示主机名，Worker/Host 脱敏 sig/signature | Twitch 现有 23 项契约，缺字段/null/非字符串、下线竞态和签名不泄漏回归 |
| Twitch 观看请求诊断依赖后续进度事件或手动刷新，失败反馈可能延迟 | Core 只更新 watchRequests 字典；HTTP 分片拒绝未直接推送该诊断 | 一小时冻结源码验收结束后，增加去重 watch_request 事件；VM 直接处理失败/恢复，并拒绝停止后的迟到事件 | 现有 Twitch 组验证去重；WPF 组验证失败、恢复与停止门禁；不改观看传输或增加 API 轮询 |
| UI 自检把外部剪贴板占用写成总 FAIL | 聚合逻辑把任何非 PASS 都当 FAIL | 总结果明确区分 FAIL / BLOCKED / PASS，BLOCKED 返回码 2 | 实际占用证据仍保留，不改为假成功 |

## SOOP 单账号故障注入

隔离数据目录；真实子进程、stdin/stdout JSONL、DropsHostService、DropsViewModel；模拟 A/B 观看业务。生产 50/180 秒超时不缩短，测试 hook 使用 1.5 秒；刷新调用真实 `SoopWorker._submit()` 与 asyncio 取消。所有场景结束均检查所拥有进程退出。

| 场景 / 触发 | 共享进程 | A/B 最终运行 | Generation | 结果 |
|---|---|---|---|---|
| 正常停止 B | 保留 | true / false | 1→1 | PASS；A 不重连 |
| B stop_account 阻塞 60 秒 | 回收，恢复 A 一次 | true / false | 2→3 | PASS；B 意图撤销，不复活 |
| B start_account 阻塞 60 秒 | 回收，恢复 A 一次 | true / false | 4→5 | PASS；没有迟到启动 |
| B refresh asyncio 阻塞 60 秒 | 取消当前只读请求，保留 | true / true | 6→6 | PASS；保留快照，后续刷新成功 |
| B 恢复回调、手动停止、caller cancellation 与总超时交错 | 回收，恢复 A 一次 | true / false | 7→8 | PASS；旧回调不启动 B |
| Worker os._exit(23)，B 意图已撤销 | 重建，恢复 A 一次 | true / false | 9→10 | PASS |
| Worker os._exit(23)，两账号均有运行意图 | 重建，各恢复一次 | true / true | 11→12 | PASS |

全部 orphan=false，VM 运行状态正确；最近任务进度 10 分钟不丢失。共享进程无法安全保留时存在短暂中断，软件不能宣称 A 从未断线。现有真实账号不足两个，真实双账号网络验收为 BLOCKED。上一轮单真实 SOOP 账号一小时 viewTime 增长证据保留，本轮没有重复制造真实账号死锁或宣称新双账号实测。

## Twitch 官方进度与长测

使用第二轮用户已授权的隔离 Smartbox Session 副本，显式 HTTP 代理、fallback 关闭。记录脱敏账号标签、活动/奖励身份、官方分钟、当前频道、资格、WS、generation、刷新来源；不记录 Token/授权码/签名 URL。旧 frozen 基线与新源码补验顺序执行，没有两个测试 watcher 同时运行。

- Campaign：`b4d74b43-1f76-4953-a980-83a68ea6b5ff`。
- Drop：`1faa16f9-c423-11f1-8187-0a58a9feac02`，需求 60 分钟。
- 初始频道 `ow_esports_jp`，在线、Drops enabled、活动 eligible/available。
- 基线从 20:42:23 开始，每 10 分钟官方完整刷新；0/10/20/30/40/50/60 分钟的官方值为 3/3/3/3/4/4/4。最终 3610 秒，stop=true、Session 新进程恢复=true、重启官方值=4、refresh=true、errors=[]、Claim=0。少量只读诊断另探测 Spade 204 和播放凭证；不是完全无诊断干预的对照试验。
- 播放列表预检：Smartbox GQL 凭证→主品质列表成功；没有发送观看分片，Claim=0。
- 局部同步来源：DevilXD/TwitchDropsMiner `f693b0ca41301ac2282f3978713479efebe0490e`。来源/许可证声明保留；既有 GQL/UA 基线仍是 `22d0c6134f9291d1e904012c465504a22bd3f97c`。

新源码从 21:42:49 开始，0/10/20/30/40/50/60 分钟同一奖励官方值 **4/14/23/33/43/52/56 分钟**，最终为 **93.33%**，142/142 次分片请求接受。每个采样点 WS 两条 Connected，generation=1，完整刷新来源 remote。20–30 分钟间自动换到 `ml7support`，其后同一奖励继续增长。官方增长是真实 API 数值，不是本地估算。运行期间冻结 Twitch/Shared 源码，指纹另记录；采样中的 accountTag 包含临时 Session 路径，因此只作为该运行标签，不能作为跨两次运行的稳定账号标识。两次均复制同一已授权隔离 Session。

实际一小时观察完成：最后采样 3601 秒，总流程 3608 秒，官方累计增加 **52 分钟**；不是恰好每秒计入。奖励尚未完成、claimed=false。stop=true，独立进程 Session 恢复=true、重新官方刷新=true、重启官方值=56，errors=[]，Claim=0，exit=0，sourceUnchanged=true。没有观测到 Worker 重建/WS 断线；频道切换已实际观察，不冒称人为断网恢复也已实测。

一小时验证对应冻结的观看传输源码；结束后新增的去重诊断事件不改变传输、分钟来源、间隔或领取策略，其闭环另做针对性验证，不把后加事件冒充已运行一小时。

## YouTube / Bilibili 范围

YouTube：匿名真实 yt-dlp 发现 Lofi Girl 直播 `1-LpQekNa9g`；独立测试 Profile/CDP 确认媒体位置前进，本地历史增加 40.172649 秒；buffering 后不虚增，stop 前后历史相同，exit=0，owned browser remaining=[]。这不是登录后真实账号验收，也不是官方 Drops 进度。用户暂停后不再执行后续流程。

Bilibili：本轮真实官方 QR 生成后过期，未完成扫码；用户暂停。没有登录后 Session/任务/观看或 412 归因证据。此前无账号 HTTP 412 不被当作正常业务成功，也不绕过平台限制。

## WPF、按钮与显示环境

实测一个主显示器，1920×1080，工作区 1920×1008，DPI=144（Windows **150%**）；manifest=PerMonitorV2。没有修改显示设置。100%/125% 仍仅 DIP 模拟；无第二显示器，不能验证跨显示器 DPI 迁移。

保留既有布局，只在 Twitch 进度区增加观看分片请求诊断：未确认、接受、具体阶段 HTTP 失败、停止；始终说明以官方奖励进度为准。失败/恢复投影不清空官方值，恢复清掉旧请求错误。真实 WPF 控件、自检、Routed Click、Host 传输和模拟业务反馈有证据，但不是全部真实账号按钮闭环。

剪贴板曾被 SogouCloud 间歇占用，HRESULT=0x800401D0；失败不提示假成功，验证码仍能手动选择。未强行关闭占用程序。之后同环境 `ui-final.txt`、`ui-final-integration.txt` 实际 STA 写入/读回及复制按钮路径通过（模拟授权码，真实系统剪贴板）。最新窗口使用独立 `manual-ui-final` 数据目录及已有隔离 Session 副本；自动启动/自动领取关闭。已集中请求用户检查 SOOP↔Twitch、尺寸/滚动、设置草稿和 Twitch 登录/开始/刷新/停止；人工结果尚未回复时明确 NOT TESTED，不冒充目视通过。

## 测试、构建与交付

- Worker 契约：Shared 3 / Twitch 23 / SOOP 39 / YouTube 3 / Bilibili 20，共 88 项通过；Twitch 测试存在既有 unclosed event loop ResourceWarning，不据此宣称产品无任何资源警告。
- SOOP 七分支注入 PASS，`p0-review` 收紧 generation/恢复次数断言后再次 PASS；Drops recovery FeatureSelfTest PASS；最新 Release 构建 0 警告/错误。
- UI 早期外部剪贴板占用结果为 BLOCKED；之后 `ui-final.txt` 总体 PASS，真实剪贴板写入/读回和按钮路径有成功证据；加入签名脱敏检查的 `ui-review.txt` 无 FAIL，但 SogouCloud 再次占用，明确 BLOCKED / exit=2。全部结果保留，不删除受阻证据。
- 长测结束后的最新 `ui-final-integration.txt` 总体 PASS；分片失败/恢复事件直接投影、停止门禁和系统剪贴板检查通过。Twitch 最新 23 项通过，包括诊断事件去重。
- 本轮未运行无关全仓库 lint、性能、压力、依赖审计；没有子代理。任务权限实际为 danger-full-access / never，未利用权限改变系统或执行未经要求的发布。
- SOOP 外部 Core 不修改；来源固定 `6050b8a35690b091996a1efedad4655b1b094cac`，摘要 `65801b6e10f1777cd08af4ae0c6903003b2544c6bfd58b60f7295ba77d879be6`。完整 Core 仍仅 Local 本机自用，对外再分发授权仍 BLOCKED。
- 最终候选使用本轮提交命名：`artifacts/candidates/drops-round3-<BuildCommit>`。提交后从清洁源码构建四 Worker 和主程序，核对清单/内嵌 provenance/SHA256 与主程序 BuildCommit，不创建 Tag、Release 或正式安装包。提交后的最终构建与冻结产物验收记录保存在 Git 忽略目录：`worker-build-final.txt`、`publish-final.txt`、`candidate-consistency.json`、`twitch-frozen-final/result.json`、`candidate-feature/feature-selftest.txt`、`candidate-ui.txt`；最终交付报告依据这些实际结果，不能把构建命令当成已通过。这样不因追加验收日志提交而改变已构建的代码提交身份。

本地复现命令（完整 SOOP 仅 Local）：

```powershell
./Integrations/Drops/build-workers.ps1 -BuildPurpose Local
$dropsRound3Commit = git rev-parse HEAD
dotnet publish './CloudLight Blizzard.csproj' -c Release -p:DropsBuildPurpose=Local -p:BuildCommit=$dropsRound3Commit -o "artifacts/candidates/drops-round3-$dropsRound3Commit"
```

修改范围：Shared 协议及现有契约；SOOP Worker/现有进度契约；Twitch Channel/constants/Core/headless GUI/Worker/现有 OAuth 契约；YouTube Worker/现有契约；Worker SSL 检查；DropsHostService/敏感信息脱敏；DropsViewModel/DropsPage；现有 Feature/UI/Drops 自检、新增 opt-in SOOP 超时及 Twitch 长测工具；第三方声明、本记录。没有修改其他业务模块、版本号或外部 SOOP Core。

最终停止仅本轮启动的 Worker、浏览器和窗口；保留用户原安装程序及其任务、账号与 Profile 数据。
