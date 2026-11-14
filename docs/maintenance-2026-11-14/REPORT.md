# CloudLight Blizzard 维护与验收记录

执行日期：2026-11-14 至 2026-11-15，Asia/Shanghai。版本保持 2.1.10。

本报告是代码提交时的证据检查点。最终干净提交的构建、安装包摘要及补充实机结果，以本地候选目录的 `candidate.json`、`validation/`、`FINAL-ACCEPTANCE.md` 为准。不能将预提交构建身份或本报告中的隔离通过项当作全部真实业务通过。

## 范围、代码与环境

- 实际仓库：`D:/code/CloudLight Blizzard/CloudLight Blizzard`。用户提供的 C 盘路径不存在。
- 起始 HEAD：`bffdb7d3c33051f74b907e311e0d9887e93804cd`，main；代理 fetch/prune 和 pull/ff-only 后远端仍为同一提交。origin 的旧用户名地址通过 GitHub API 核实归属于 `Cloud-Light125/CloudLight-Blizzard`。
- 没有覆盖已有 tracked 修改；起始 `artifacts/` 未跟踪内容保留。没有 reset --hard、clean、强推、Tag、Release 或公开上传。
- 主代理独立执行；没有子代理、后台代理聊天或自动化。
- Windows 10.0.26300 x64；PowerShell 7.6.9；.NET Desktop Runtime 8.0.33；SDK 10.0.302（另装 9.0.320）；Python 3.13.9；PyInstaller 6.22.3；OpenSSL 3.5.6；aiohttp 3.14.3；httpx 0.28.1；yt-dlp 定向升级至 2026.10.26。
- `dotnet --info` 的工作负载组件抛 TypeInitializationException；使用当前构建进程的 `MSBuildEnableWorkloadResolver=false` 可以正常生成，未修改系统或用户全局 SDK 配置。
- NuGet漏洞审计实际发现 SQLitePCLRaw.lib.e_sqlite3 2.1.6（High，GHSA-2m69-gcr7-jv3q）及System.Drawing.Common 4.7.0（Critical，GHSA-rxg9-xrhp-64gj）。核对GitHub官方公告、NuGet包及许可证后，将Microsoft.Data.Sqlite更新到8.0.33、SQLitePCLRaw bundle显式固定3.0.5（原生SQLite3.53.4）、System.Drawing.Common固定8.0.33。修复后NuGet传递依赖漏洞审计为空，账号原生库安全回归PASS；MIT/Apache-2.0/Public Domain声明随包保留。
- 任务实际权限为 danger-full-access / never，与读取到的用户全局权限一致。全局模型偏好为 gpt-6.1-sol/high，多代理两个开关均 false；任务实际具体模型及推理强度没有独立查询接口，不能把配置文件当作任务级执行证明。未修改这些设置。
- GitHub 下载仅使用当前命令/子进程代理 `127.0.0.1:7897`。搜索工具不可用；未认证 GitHub API 限流后使用本机已有 gh 认证只读查询，未变更认证信息。

## 功能与控件矩阵

- [逐项矩阵 MATRIX.md](MATRIX.md)：209 项，含 PASS / FAIL / BLOCKED / NOT TESTED / NOT APPLICABLE 及每项的验证层级。
- [结构化矩阵 matrix.csv](matrix.csv) 与 [模块汇总 SUMMARY.md](SUMMARY.md)。初始 199 项在修改业务代码前建立，后补充真实业务限制。
- [控件清单 ui-controls.csv](ui-controls.csv)：扫描当前 23 个 XAML 窗口/页面的 243 个 Button / CheckBox / RadioButton / ToggleButton 声明。声明、绑定或页面构造不等于按钮实际业务已通过；没有逐个点击的控件不补写 PASS。

PASS 包含真实业务、真实文件系统隔离、真实 Host/JSONL/进程加模拟账号等不同层级。尤其不能把模拟双 UID 写成真实 SOOP 双账号，把隔离文件恢复写成真实游戏区服往返。

## 已确认缺陷及修复

| 问题 | 根因和实际修改 | 验证与边界 |
|---|---|---|
| 实际已安装 Overwatch，但“自动查找”失败 | Agent `product.db` 为二进制，路径前后的 protobuf 字段字节被混入路径；提取绝对路径、规范化斜杠，仍验证真实 `_retail_/Overwatch.exe`；不可读/畸形候选继续尝试其它来源 | 真实 WPF 复现失败，修复后显示实际安装目录；隔离中文/空格/双斜杠/JSON兼容测试。只读真实游戏文件 |
| 区服完整回归被用户正在运行的 Battle.net 阻断 | 测试只注入游戏进程探针，没有隔离 Battle.net 进程探针 | 增加可注入进程查询，生产默认仍查真实进程；反测“预览后客户端启动”必须阻止写入。没有关闭用户客户端 |
| Twitch 相同官方分钟样本不重置本地估计和倒计时 | 旧 zero-delta 提前返回 | 定向采纳 upstream 378697a 思路，清除估计并通知，负值作单次钳制；新契约测试确认官方分钟不虚增 |
| Twitch Client UA 落后上游 | 本地 Chrome153，当前上游 Chrome156 | 定向同步 69a4ff9 的 Web/Mobile/Smartbox UA；兼容自检增加明确 client/timer 来源；查询 hash、NoAutoClaim、playlist观看路径保留 |
| Bilibili 新活动页读不到任务 | 旧解析依赖 EvaTabs.Panel，新页面可能只有扁平 EraTasklistPc | 同步上游 2c102efa 的任务组回退；保留嵌套组件遍历、去重、网络与取消。旧页与新页 fixture 均通过；真实登录后的活动任务仍待授权验收 |
| YouTube 公共页可能选中推荐视频 | 全页 regex 取最后一个 videoId，可能与当前直播无关 | 只解析当前 ytInitialPlayerResponse 的 videoDetails 与 liveBroadcastDetails；无有效播放器不判断频道离线；误导推荐和 consent 页回归 |
| YouTube 部分频道 /live 不再指向当前直播 | 多条正在直播时，/live 返回空/旧页面，/streams 仍列出正在直播条目 | yt-dlp 2026.10.26，增加有界 /streams 回退，只选 is_live；真实发现当前直播，fixture 不选第一条旧回放 |
| CDP 消息/标签页及代理干扰播放检测 | 原先读取第一条 WS 帧/第一个含 youtube.com 的标签，可能读到事件或错误视频；WS 缺少明确本地 no_proxy | 按 CDP id 匹配响应、严格官方主机与视频ID、本地直连、释放HTTP Session；事件先于响应、伪域名、不同视频等测试 |
| YouTube 官方要求登录时只显示通用等待 | 实际匿名 Chrome player 返回 LOGIN_REQUIRED，媒体位置始终0 | 增加登录要求/不可播放状态及原因提示；真实受阻保持0时长，新测试确认即使假 playing 字段为true也不累计。没有绕过登录或反机器人限制 |
| SOOP 新领取协议参数与认证分类 | 上游 v1.0.3 要求 use 请求带 itemCodeIdx/itemIdx；业务 result=-1 不证明 Session 过期 | 在本项目 api_compat 适配层覆盖对应方法；保留原 Core 哈希和响应+背包状态变化验证；双ID及HTTP401/业务拒绝测试。不提交真实Claim |
| SOOP 缓存奖励在最新背包消失后仍可进入领取 | 手动领取重新读取背包，但没有在 current_item 缺失时提前终止 | 缺失/绑定未知/未绑定/过期/二次发放等待均停止；所属UID重新读背包；未来有效期不因 expFlag=Y 被错误禁止；模拟验证无 use 请求 |
| 新用户未准备快照被诊断成错误 | Empty/Preparing 落入默认Error；Ready但不可用仍使用“可切换”文案 | Empty/Preparing为信息，Stale/Legacy为警告，损坏/不可用Ready仍为错误；回归保持坏快照不被降级 |
| 旧NuGet传递依赖存在已知漏洞 | 官方审计确认旧SQLite原生包High及Drawing4.7.0 Critical；官方公告指出SQLite旧包全部2.x受影响，未选择仍有漏洞的2.1.11 | 使用兼容当前.NET8的Drawing与Sqlite托管补丁、SQLitePCLRaw3.0.5/SQLite3.53.4；核验许可证、重新restore/build、账号SQLite回归及传递依赖审计；未盲目更新全部依赖 |

完整改动由主代理复核实际 diff、源码及测试。没有修改账号备份 manifest/journal/staging/回滚规则，没有移除游戏运行门禁，没有修改 Host 的 UID 恢复意图和定向取消协议。

## 上游检查与保留范围

| 平台 | 本地来源与执行时上游 | 决策 |
|---|---|---|
| Twitch | NoAutoClaim 基线 3071a8a / DevilXD 3aec843；GQL 定向基线22d0c61；[最新master 99d12fd](https://github.com/DevilXD/TwitchDropsMiner/commit/99d12fd2d815d7737258ed57405c63c0686bd3e1)，dev-build 2026-10-28 | 同步UA与计时修复；f693b0c playlist已由本项目验证。3f6f0ef的浏览器隐式授权没有整包覆盖：本轮Smartbox device字段、Session、官方活动和分钟增长均有效。新授权人工确认仍BLOCKED |
| SOOP | 定制仓库最新6050b8a；[原上游v1.0.3 / 3283f5d](https://github.com/tom1230123/soop-drops-miner/releases/tag/v1.0.3)，2026-11-08 | 仅在适配层同步领取协议，不替换定制生命周期、绑定模型或JSONL；没有扩大既有再分发授权 |
| YouTube | 本项目CDP观看实现，原来源ucarno/ow-league-tokens；[yt-dlp stable 2026.10.26](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.10.26) | 只更新有相关YouTube提取修复的yt-dlp，保留requests/websocket等锁定版本；TUNA403后使用官方PyPI，只改项目venv |
| Bilibili | vendored a0d8bd5；[上游main 0200d60 / v1.7.1](https://github.com/mi0e/BiliBiliDropsMiner/releases/tag/v1.7.1) | 仅选择扁平任务解析，不移植Web GUI/Docker/重复Miner循环；DPAPI、路由、Poller取消、领取开关保留 |

上游近期 Issues 包含 Twitch Inventory读取、浏览器发现、频道资格及奖励历史，Bilibili开始/停止与多时段任务映射；没有把Issue标题当可复现根因。项目 [Issue #3](https://github.com/Cloud-Light125/CloudLight-Blizzard/issues/3) 正文及已有修复评论已重读，账号安全测试覆盖空目录、旧迁移、manifest损坏、互斥和不伪造登录。Issue #4 的旧备份问题：本轮真实文件系统回归保留当前代保护与历史快照删除，未自动清理用户快照。

官方公开资料也已访问 [Twitch OAuth](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/) 和 [Chrome CDP](https://developer.chrome.com/docs/devtools/remote-debugging/local-server)。Blizzard实际客户端格式以本机只读文件为证据，没有凭旧文档宣称当前双区文件完整兼容。

### SOOP 授权来源恢复

旁边的现有 SOOP 仓库实际为旧提交3bc08ad，缺少绑定能力，构建门禁正确拒绝；原目录未改。

独立检出6050b8a后仍不匹配授权摘要，原因是授权记录对应当时带未提交修改的完整运行时源码，并非纯 Git checkout。已从既有PyInstaller数据区恢复24个 `.py` 源文件到独立目录，先逐文件核对原 manifest，再核对聚合摘要 `65801b6e10f1777cd08af4ae0c6903003b2544c6bfd58b60f7295ba77d879be6`，与授权记录完全一致。恢复的是源码字节；本轮四个Worker全部重新冻结，没有复制旧exe作为新产物。

恢复证据：本地 `test-output/maintenance-2026-11-14/core-source-recovery.json`。独立来源目录：`D:/code/CloudLight Blizzard/.audit-upstreams/soop-cloudlight-20261114`。`coreDirty=true` 如实保留。Local候选不构成新的公开再分发授权。

## 本轮真实业务结果

- Twitch：已有Session副本恢复，114活动/403奖励；冻结Worker playlist请求有效，刷新分钟值3→5。随后独立官方Inventory返回相应6分钟，15个奖励条目的增长得到独立复核；不是以HTTP204或WS Connected认定观看。停止与退出成功，未Claim。
- SOOP：1个真实账号恢复，任务/活动/90条背包可读；冻结Worker Bridge及连接健康，官方进度记录mission14907为3→4→6，独立任务API也读到6。官方进度与历史高水位分离。真实Host+模拟双UID七分支故障恢复通过，未宣称真实双账号通过。后续单次官方请求曾TLS EOF，保留为网络限制，不删失败证据。
- 并发归因限制：任务开始前20:48已有用户主程序及四组Worker运行，未强制关闭。Twitch/SOOP官方增长、冻结Worker请求和连接各有证据，但无法排除旧实例对同账号官方进度的贡献；不能把这些样本当成新Worker独占产生增长的证明。独占业务验收另标BLOCKED。
- YouTube：匿名Chrome及独立Profile启动、真实直播发现、CDP读取、停止和清理成功；媒体currentTime=0/readyState=0，官方LOGIN_REQUIRED，因此真实媒体播放BLOCKED、本地时长0。非零历史恢复和推进判定由隔离测试验证，未用真实账号登录。
- Bilibili：真实QR生成code0，公开直播间code0，未登录nav为-101；二维码/Session/任务/Poller/停止/412/代理由隔离契约验证。真人扫码及有账号观看/官方进度BLOCKED；没有扫码就宣布登录成功。
- Battle.net/游戏：实际Battle.net 2.53.8.17986，游戏 `.build.info` 2.25.1.0.155284，`_retail_/Overwatch.exe` 存在；只读检测与WPF目录显示通过。备份/恢复/双区切换及失败回滚均用隔离真实文件系统；没有关闭用户进程或改真实配置/游戏文件。
- 更新/网络：实际WPF自动检查最新2.1.10成功；代理下公告、更新、Twitch与SOOP诊断HTTP200；GitHub正式安装包Range请求HTTP206，读取65536字节、MZ/Content-Range/ETag符合预期。这不是远端整包SHA验证。完整下载重试、续传、digest与安装清理门禁有本地HTTP/文件系统/模拟launcher证据。
- WPF：实际打开账号、掉宝、设置、深色主题、区服、诊断，真实查找安装目录；诊断完成20/20。布局回归覆盖1366×768、1920×1080、2560×1440以及100/125/150%的DIP模拟，没有改变系统显示设置。早期Windows剪贴板占用明确BLOCKED；后续ui-security-final整体PASS，真实剪贴板及复制按钮通过，没有关闭占用程序。

## 自动化、构建和可恢复记录

本地原始证据位于 Git 忽略的 `test-output/maintenance-2026-11-14/`，其中包含隔离Session副本，禁止整目录提交或上传。报告只引用不含凭据的摘要；原始日志/会话不会随源码推送。

本轮预提交Worker测试：Shared3、Twitch24、SOOP43、Bilibili21；YouTube增加登录限制测试后8项通过，最终全量应为99项。早期Twitch测试有既存unclosed event loop ResourceWarning，未以它证明生产没有资源问题。完整FeatureSelfTest、账号安全、区服VerifiedDifference、更新安全、退出安全及SOOP七分支超时/崩溃测试均有本轮通过记录。早期UA固定断言及隔离探针失败记录保留，没有删除失败日志。

最终候选构建入口：

```powershell
./build-support/Build-MaintenanceCandidate.ps1 `
  -SoopCorePath 'D:/code/CloudLight Blizzard/.audit-upstreams/soop-cloudlight-20261114' `
  -BuildPurpose Local
```

必须先提交并保持tracked工作区干净。脚本创建带版本/commit/用途的新目录，重新构建四Worker、WPF Release、Inno安装包，校验SSL、来源、SHA256、JSONL、组件/UI及安装包payload/settings测试；安装器测试在隔离数据目录执行专用入口并在安装前Abort，不安装到系统。既有候选非空则拒绝覆盖。最终结果生成 `candidate.json`，UI若BLOCKED会保留BLOCKED，不把打包成功当完整验收通过。

## 仍未完成的验收

- 新Twitch人工Device授权交换；SOOP第二个真实账号；YouTube/Bilibili真实登录；所有真实奖励领取。
- Battle.net真实账号A-B-A认证、真实守望先锋双区文件往返及更新后的双区全量兼容。
- YouTube当前登录限制后的真实媒体前进。公开战绩查询请求遇到Blizzard TLS EOF，页面存在不等于当前真实战绩业务通过。
- 实际系统安装/自动启动写入/已安装身份Windows Toast/多显示器；物理托盘退出按钮没有本轮直接点击证据。
- Twitch运行中的自动换频道与单独WS完整验收、SOOP强制换台与心跳字段的独立验收。短时官方进度增长不能替代全部长期网络恢复场景；没有重复数小时观看实验。
- 精确逐控件业务点击仍以ui-controls.csv的未执行项为准。整个系统没有“全部功能彻底正常”的结论。

以上限制不得从最终交付摘要或矩阵中省略。
