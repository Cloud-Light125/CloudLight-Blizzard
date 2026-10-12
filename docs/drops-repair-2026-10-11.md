# Drops 修复与验收记录（2026-10-11 至 2026-10-12）

基线：本机 `main`，`ff882b2`，版本 2.1.8，开始时工作区干净。没有改变版本号、创建 Tag 或发布 Release。

## 实际 Core 与构建

- Twitch：仓库内 NoAutoClaim 定制 Core；GQL/UA 基线 `22d0c6134f9291d1e904012c465504a22bd3f97c`，保留 Chrome 153、persisted query hash、`request cancelled` 有限重试。
- SOOP：原构建流程从仓库父目录的 `cloudlight soop drops miner` 打包；本轮实际来源提交 `6050b8a35690b091996a1efedad4655b1b094cac`，外部目录没有修改。该 Core 的 `SoopMiner.force_refresh` 与 `MultiMinerManager.force_refresh_account` 确实存在。另一份旧目录不能代表本轮来源。新增打包能力检测，防止使用缺少上述 API 的旧 Core。
- YouTube：仓库内独立 Chromium/yt-dlp Worker；观看历史是本地播放记录，不提供官方奖励进度或领取接口。
- Bilibili：现有内置 MIT Core，基线 `a0d8bd51728aabaef66c651613324adba15d9ce8`。
- 构建：原 `Integrations/Drops/build-workers.ps1`、Python 3.13.2 / OpenSSL 3.0.16、.NET SDK 8.0.424；依赖仍通过 TUNA。第三方来源及许可证声明保留。

## 根因与修复

| 症状 | 根因 / 证据 | 修改位置 | 修复及验证 | 状态 |
|---|---|---|---|---|
| Twitch 登录抛 `KeyError: device_code` | Android/Web Client 实测 HTTP 400，JSON `{status, message: invalid client}`；原代码直接取成功字段 | `twitch/core/twitch.py`, 新增 `oauth.py`, `exceptions.py` | 使用仓库已有且实测 HTTP 200 的 Smartbox Client；校验 HTTP、对象、字段和可信地址；OAuth 回归与实际设备码请求 | 已修；未取得历史 19:07:39 原始封包，当前拒绝条件可复现同一异常 |
| 授权错误后一直等待 | 原 token 轮询将所有非 200 都当作待授权 | 同上 | 区分 pending、slow_down、取消、过期、无效 grant、429、5xx、HTML/空响应；过期/取消显示授权原因；新授权被 validate 拒绝不进入网络重试；自动恢复不复用已退出轮询的设备码 | 模拟回归通过；全新用户人工确认未完成 |
| Session 恢复反复超时 / 连接重叠 | HTTP 原无限重试；future 超时未取消；外层重试与未清理连接重叠 | Twitch Core/Worker、`DropsViewModel.cs`, `DropsHostService.cs` | 单请求有上限；future 取消；清理门禁；自动启动超时先停止；主机长事务超时结束旧 Worker；停止有总预算 | 实际 Session 与观看/停止通过 |
| 网络阶段不清楚 | DNS/直连与 Client ID 拒绝混在同一错误；诊断可能受环境代理干扰 | `Shared/protocol.py`, 四平台 Worker/Host/UI | 明确 route、HTTP、DNS、TLS；使用各平台 HTTP 库且关闭环境继承；保留 fallback 策略和证书验证 | Twitch 代理可用、直连超时；未改 hosts/系统代理 |
| SOOP 增加后刷新回退 | 当前 Core 将 Mission API 的 `itemList.viewTime` 替换到模型；如果远端给低值就显示低值，无法从旧本地值恢复服务端数据库 | `soop/worker.py`, 新增 `test_progress_sync.py`, VM/XAML | 主值始终远端值；独立历史高水位、回退诊断、资格、心跳、最近完整刷新；`10→0` 模拟保持显示真实 0 | 软件数据投影已修；真实观看后 4 个阶段增长、0 个回退；官网历史清零未复现 |
| SOOP 网络失败看似没有活动 | 请求状态与成功快照缺少独立诊断 | SOOP Worker/VM/页面 | 失败保留成功快照，发布每账号失败诊断；不清空成功数据、不将失败当成空活动 | 模拟通过；旧 Session 失败、重新登录后成功，均正确呈现 |
| SOOP 失效 Session 被当成网络问题 | 官方返回 `Drops API 认证失败: 로그인 후 가능 합니다.`，UI 原分类没有该文本 | SOOP Worker/VM/页面 | 中文“登录信息已失效”；按 UID 提醒，不阻断别的账号 | 旧 Session 已确认失效；隔离重新登录后读取官方任务/活动/背包成功 |
| SOOP 停止 B 会取消 A 重试 / 旧恢复回调复活状态 | 单一全局恢复 UID/CTS；回调缺少意图代次检查 | `DropsViewModel.cs`, 页面 code-behind | UID 独立意图/CTS/唤醒；全局与 UID 恢复分开；停止和确认检查 UID/代次；Running 来自账号状态 | WPF 回归通过 |
| SOOP 改代理使停止账号复活或其它账号无法恢复 | 原重启 `stop()+start_all()`；逐个恢复时第一个异常中断循环 | SOOP Worker | 仅恢复之前活跃 UID，逐账号失败继续并提醒；停止超时保留清理门禁 | 针对性回归 / 代码核查通过 |
| SOOP UID 查询串账号 | 查询未知 UID 回退到所有账号；频道只按频道 ID 合并 | SOOP Worker | 未知 UID 返回空；频道键包含 UID；领取仍重新读取所属账号 Inventory | 多账号与绑定保护契约通过 |
| SOOP unknown 心跳隐藏已增长的官方进度 | 实际 API 有 4 个阶段增长，但 Worker 因心跳 unknown 返回空 currentProgress | SOOP Worker、VM、进度回归 | 未知心跳仍显示远端数字并独立警告；换奖励/活动阶段不沿用历史高水位 | 模拟回归通过；不把 unknown 强行当成功 |
| 切平台时账号/背包被其它平台覆盖 | VM 每次 ApplyState 清空共享集合；Twitch 重建任务还清空 SOOP 可选任务 | VM | 独立平台集合；局部字段不清数据；选中平台集合通知 | 实际 WPF 控件验证通过 |
| 迟到事件覆盖新状态 | Worker 重启、异步回调和旧请求无版本区分 | Shared/Host/VM | JSONL sequence + Host generation + 单调采集时间；退役进程输出忽略；较早采集但晚送达的状态拒绝；业务命令串行；Twitch 列表整批发布 | WPF 模拟乱序 / 实际 Host 协议验证 |
| Worker 早退时 UI 等待不结束 | 建连门禁内递归 Request/EnsureConnected 可能等待自身门禁 | Host | 私有 SendAsync 在已创建 Worker 上握手；进程早退立即报错 | Release 构建及真实隔离 Worker 握手 |
| Twitch 新账号显示旧背包 / 重连清空列表 | 成功快照未绑定身份；初始空列表与成功空列表混淆；登录结束清理丢数据 | Twitch Worker/VM | 快照 owner 比较覆盖在线和离线状态；明确 remote/cache/unavailable；成功空可以替换、未完成空保留缓存 | 缓存身份回归 / 实际 Session 背包查询 |
| Twitch 手动停止被报成网络故障 | ExitRequest 落入普通异常分类，迟到授权事件仍可打开页面 | Twitch Worker/VM/页面 | 正常退出单独处理；用户停止/退出后不消费连接授权阶段；页面只打开当前有效授权 | 模拟及实际停止通过 |
| Bilibili 刷新失败看似成功 | discover/progress 异常被捕获但返回普通成功状态 | Bilibili Worker/页面 | refreshCompleted/refreshStatus/refreshError，保留旧任务并提示 | 契约通过；无真实登录账号 |
| Bilibili 停止后仍可能自动领取下一任务 / 清理异常 | 未绑定停止状态；进度 poller 丢线程引用；手动领取与 close 分属不同 asyncio loop | Bilibili Worker/tests | 停止取消 poll task，保留未结束线程；自动领取每任务检查；claim/close 在同一 loop | 20 项契约通过；已到达服务器的请求无法撤销，超时结果须官方刷新确认 |
| YouTube 停止后旧扫描发布观看 / 孤立浏览器 | 超时后清线程引用；Popen 与 stop 竞态仅 terminate 不等待 | YouTube Worker | 保留旧线程；检测后检查停止；浏览器竞态 terminate/wait/kill；最终清理 | 契约与只读协议通过；真实浏览器观看未测 |
| 按钮无反馈、设置被后台重置、切页漏加载、窄屏裁切 | 页面错误隐藏在泛化弹窗；重复操作；后台全量 PopulateSettings；全局 loading 吞掉切页请求；窗口最小尺寸超过高 DPI 工作区 | 页面/VM/MainWindow | 非阻塞分级提醒；主要/保存/账号按钮防重复；后台仅同步数据；补发当前平台加载；布局重排和工作区约束；当前直播间资格/链接、服务端查询时间可见 | 真实 WPF 自检、按钮与布局截图通过 |

## 平台功能检查矩阵

“契约”指模拟协议/状态或定向代码检查，不代表真实账号端到端完成。

| 功能 | Twitch | SOOP | YouTube | Bilibili |
|---|---|---|---|---|
| 添加/登录 | Device API 真实成功；旧 Session 真实成功；人工确认未测 | DPAPI/添加账号契约；实际重新登录成功 | 独立浏览器 profile，契约检查 | QR/DPAPI 契约；无真实账号 |
| Session 恢复 / 自动启动 | 实际 Session；自动授权退出契约 | 旧 Session 拒绝；保存凭据重新登录实际成功，Core 按账号恢复契约 | profile 保留；不伪称浏览器登录已确认 | 自动恢复/自动继续开关保留，契约 |
| 开始 / 停止 | 实际观看并停止，退出码 0 | UID / 全部操作契约、WPF UID 取消回归；实际观看、刷新、停止 | 线程/浏览器生命周期修复，契约 | session / poller / stop 契约 |
| 发现活动 / 任务 | 真实 120 活动，14 可参与 | 实际读取 16 个任务、10 个活动；阶段/合并契约 | yt-dlp 直播发现；不是官方 Drops 活动 API | 房间/官方任务发现契约 |
| 直播间选择 / 观看 | 实际选中频道和当前奖励，8 条 WS Connected | 实际 Bridge 连接；心跳业务状态 unknown，严格降级并保留官方数值 | Chrome/Brave 播放状态/本地历史；真实观看未测 | x25Kn 多 Session / room schema 契约；真实观看未测 |
| 进度 | Core 同步与本地 fallback 明确标注；未长期测官方增加 | 远端 viewTime 主值；实际比较 38 个阶段，4 个增长、0 个回退；回退/网络失败/阶段隔离回归 | 本地播放时长，不能称官方确认 | 官方 Task API 契约；无真实账号 |
| 背包 / 手动领取 | 实际读取 430 项；领取仅契约，未消费 | 实际读取 59 项；所属 UID 重读、绑定保护契约；未消费 | 官方背包/领取不适用 | 官方任务与奖励契约；未消费 |
| 自动领取 | NoAutoClaim 开关保留；实际 smoke 强制关闭 | 默认关闭；Required + Linked 才允许；Unknown/Unlinked 跳过 | 不适用 | 设置保留；停止后的请求取消/去重契约 |
| 网络 / 代理切换 | HTTP 与 WS 显式代理；fallback 按设置 | 原活跃 UID 恢复；不复活停止账号 | 发现/浏览器使用显式代理；调试连接直连 | 默认直连；全局代理仅 opt-in；不继承环境代理 |
| 设置 / 软件重启 | 原 settings/cookies 保留；身份缓存隔离 | 原账号/DPAPI/settings 保留，既有优先任务保护 | profile/config/history 保留 | DPAPI/settings/last running 恢复策略保留 |
| Worker 崩溃恢复 | 进程门禁、超时释放、迟到事件拒绝 | 所需 UID 独立恢复，停止 UID 不再启动 | 失败可见、手动重启；没有添加未经授权的自动观看 | 既有自愈 + poller 生命周期契约 |

## 网络与真实程序证据

- 当前应用设置：全局 HTTP 代理 `127.0.0.1:7897`；Twitch/SOOP/YouTube 选择使用，Bilibili 不选择；fallback direct 开启。真实 Twitch/SOOP smoke 的隔离配置关闭 fallback，以证明请求经过指定代理。
- Windows 系统代理只读检查：已启用并使用同一 `127.0.0.1:7897`；应用请求仍显式按各平台选择传递。没有修改系统代理。
- 当前 DNS 查询出现变化，未复现稳定 `127.0.0.1`；hosts 没有检索到 Twitch/SOOP 条目。不能把先前异常解析归因于 GQL。直连 OAuth 实测超时；代理 Android/Web HTTP 400 invalid client；代理 Smartbox HTTP 200。
- Twitch Device API 返回官方 `https://www.twitch.tv/activate`、1800 秒有效期和非空授权字段；不记录 device_code、Token 或 Cookie。
- 隔离复制真实 Twitch cookies、关闭自动领取：确认 1 个账号，120 个活动、430 项背包，14 个可参与活动，选中观看频道和当前奖励，8 条 WebSocket Connected，运行后 stop/shutdown，退出码 0。
- SOOP：旧 cookies 返回官方认证拒绝 `로그인 후 가능 합니다.`，正确失败。随后通过 DPAPI 在内存读取保存凭据，在临时数据目录重新登录，关闭自动领取：读取 16 个任务、10 个活动、59 项背包；Bridge 连接后观看约 80 秒，再次完整刷新成功且会话继续。38 个奖励阶段中 4 个官方分钟数增加、0 个回退，停止成功、退出码 0。心跳业务响应为 unknown，不能称为已确认成功心跳；新修复保证这种情况仍显示官方确认进度。没有领取奖励，原凭据/账号文件未写入。
- YouTube：真实无账号 Worker hello/set_proxy/network_check/load_state/stop/shutdown；代理 HTTPS 200。
- Bilibili：真实无账号 Worker 同组命令；按当前策略直连，HTTPS 得到 HTTP 412，说明请求被拒绝/受限，不能当作活动查询或登录成功。
- UI：运行真实 WPF 程序、Measure/Arrange/Render、Routed Click、视觉树与 binding 检查。Worker 回复部分为模拟；额外使用真实打包 Worker 检查 JSONL → Host 握手，并实际终止一个无账号测试 Worker，验证重新创建进程使用更大的 generation。桌面浏览器自动化连续两次超时，未目视验证默认浏览器中的授权完成过程。
- 布局：1366×768、1920×1080、2560×1440、1200×760 × 100%/125%/150% DIP 模拟；四平台操作控件横向边界、滚动到底和截图检查。没有实际切换 Windows 系统 DPI，也没有把模拟当作真实 DPI 切换。

## 手工补充验收

1. 新 Twitch 授权：无 Session 时点击登录，确认验证码/有效期立即出现、官方页自动打开；复制码、手动打开、取消、过期重试；人工确认后显示账号，再读取背包。失败标准：空码、非官方链接、授权前报登录成功、一直登录中。
2. SOOP 使用重新登录后的有效账号：选一个合资格直播间，观察成功心跳和官方 viewTime；刷新/断网/恢复/停止；对照官网同一账号、同一 Mission 和奖励阶段。失败标准：网络失败显示 0、不同账号/阶段合并、历史高值伪称最新官方确认。官网独立归零需向 SOOP 提供脱敏 Mission/时间/返回字段证据。
3. YouTube/Bilibili 使用实际账号：验证登录、直播发现、播放/Session、官方任务（Bilibili）、停止/重启/代理切换；首次检查先关闭自动领取。没有必要为了验收消耗奖励。
4. 多账号 SOOP：A/B 同时运行；只停 B，A 继续；A 断网恢复时不启动 B；代理变更只恢复原活跃账号；一个 Session 失效不停止其它账号。
5. Windows 实际 DPI 100/125/150% 和目标显示器：检查验证码、操作按钮、下部背包/设置/日志的滚动。DIP 模拟不覆盖字体渲染和多显示器 DPI 迁移的全部差异。

## 执行范围

主代理完成整合、实际 diff 检查、构建和最终验收。使用一个 SOOP Implementer 和一个 Reviewer（均 Luna Max）；Reviewer 两次只读核查，主代理检查实际源并修复确认的问题。没有执行全仓库 lint、全量集成/E2E、完整平台构建矩阵、压力或性能测试。

实际权限为 `danger-full-access` / `never`；未主动覆盖任务模型或推理设置。未修改系统代理、hosts、TLS 校验或原账号配置；实际 smoke 的凭据复制仅在临时隔离目录使用并清理。临时日志、截图和二进制保持 Git 忽略；本文件仅包含脱敏汇总。

## 最终检查结果

- Worker 契约：Shared 2、YouTube 1、Twitch 22、SOOP 34、Bilibili 20，共 79 项通过。
- 四个平台 PyInstaller 打包通过；SSL DLL 归档检查、实际冻结 Worker SSL 导入、Twitch GQL/Chrome 153 基线检查、SOOP 即时刷新 API 能力检查全部通过。
- WPF Release 构建：0 警告、0 错误。实际 WPF 自检：305 PASS、0 FAIL；含采集时间防乱序、自动恢复过期设备码、授权错误分类、按 UID 取消、unknown 心跳进度显示、真实进程崩溃重建。
- 已检查实际 diff、无无关文件、无真实凭据或测试日志入库；`git diff --check` 通过。
- 构建产物仅本地交付；版本号维持 2.1.8，未创建 Tag 或正式 Release。
