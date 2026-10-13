# 主程序六项安全修复与隔离回归 — 2026-10-13

## 基线、范围与执行边界

- 起始分支 `main`，起始 HEAD `43888fb184faf249974e95e4e452a92665a2fd56`；包含 Drops 基线 `ba151b7b57ac4ebbfb16a0147aeff6db6af9116b`。开始时工作区、暂存区均干净。以最新本机代码修复，未回退基线。
- 版本保持 `2.1.9`。修改仅涉及账号数据、操作协调、CLI、退出生命周期、更新请求和对应自测。
- 所有故障测试使用新建的 `test-output/` 隔离目录、模拟账号/区服/HTTP、独立 Worker。实际 WPF Dispatcher、页面、MainViewModel 和窗口 Closing 被用于离线测试；窗口没有执行正常启动初始化。
- 没有真实账号切换、真实配置恢复、Cookie/Token 清理、真实扫码、更新安装、用户进程关闭、系统配置调整。只终止本轮 Host 创建的模拟 Worker。
- 前序使用两个 Luna Max 实现代理辅助账号存储和退出清理；均因服务容量限制中断。主代理接管并检查实际文件、修复部分实现、完成集成、测试及反向审查。
- 当前权限为 `danger-full-access` / 审批 `never`；未调整权限配置。运行说明在任务中切换至 GPT-6，具体推理档位未暴露。

## 六项问题

| 项目 | 根因与修复前复现 | 核心修复及文件 | 实际验证与回归 | 状态 |
| --- | --- | --- | --- | --- |
| P0 清单损坏 | `ReadManifest()` 把解析/读取错误转成 null；`Restore()` 再以 legacy 扫描剩余文件。隔离编译起始 HEAD 的原始 AppDataStore，删掉备份文件并截断清单，原版仍覆盖目标配置。 | `AppDataStore.cs`：只允许真正缺失清单进入 legacy；严格解析版本/结构/字段，规范化路径，验证大小/哈希/类型/冲突及管理/排除集合。完整源 staging、目标原始字节快照、落盘 journal、失败回滚。其他账号损坏清单和 staging/previous 目录不能扩大删除范围。 | 有效新/旧备份、旧可选字段、历史空目录兼容 PASS；截断/null/无效 Entries/版本/缺失/大小/hash/越界/大小写重复/文件目录冲突/管理集合/清单锁定/源锁定/reparse 拒绝 PASS。预检失败逐字节及文件列表不变；写入失败回滚、回滚失败证据及后续恢复阻止 PASS。现有 A-B-A-B/排除项自测 PASS。 | PASS |
| P1 扫码退出 | UI 同步 `.GetAwaiter().GetResult()` 等 Dispose；Dispose 又等 QR 的 Dispatcher continuation。隔离 STA 上复现原等待顺序，线程被阻塞；这是提取控制流的最小复现。 | `MainWindow.xaml.cs`、`DropsPage.xaml.cs`、`DropsHostService.cs`、`ExitLifecycleCoordinator.cs`：第一次 Closing 取消，立即关闭新操作/恢复意图，异步清理，完成后二次 Close；页面与 Host 一次性释放，QR generate/poll 共用取消生命周期；安装启动前等待账号和 Worker 清理。 | 实际 Dispatcher/Page 的生成、等待扫码、网络等待、排队成功、取消竞争、重复退出/Dispose PASS；真实窗口托盘/取消/二次关闭 PASS；模拟 Worker 不响应 shutdown 后被收割 PASS；假安装器确认先等待账号事务再启动一次 PASS；清理异常仍打开最终关闭门禁 PASS。SOOP 七场景 Host 回归无孤立 Worker。 | PASS |
| P1 切换重入 | Busy 设置在读取账号及区服预检的 await 之后。按原 guard/await 顺序提取的隔离控制流可同时进入两次；生产 VM 的原因在原方法中确认。 | `AccountOperationService.cs`、`MainViewModel.cs`：第一次 await 前取得非排队文件租约；同进程状态和跨进程 FileShare.None 共用门禁。Busy 同步设置；切换/保存/更新备份/重新登录/清指针/启动/删除/区服单独切换统一协调；退出取消后续阶段且等待已开始的事务。 | 同/不同账号快速请求、预检等待、实际 GUI Busy、GUI+CLI、另一个 OS 进程租约竞争 PASS；manifest/退出/区服/恢复故障及重试 PASS；游戏运行保护、部分成功诊断、退出等待恢复且不重新启动 PASS。VerifiedDifference 及既有 pipeline 顺序测试 PASS。 | PASS |
| P1 CLI 旁路 | 原 `RunSwitch`/`RunAddAccount` 在单实例判断前操作文件，未检查游戏/区服。隔离编译原入口，模拟游戏运行仍恢复并启动，复现成功。 | `App.xaml.cs`、`AccountCommandRunner.cs`：CLI 先取得与 GUI 相同的实例 Mutex；共享账号服务与核心 CLI 结果处理。参数及正整数 ID 在数据访问前验证。错误、拒绝、未确认均非零退出，保存 JSON 结果。 | 无主实例核心流程、模拟游戏运行、备份损坏/不存在、区服失败、并发 CLI、GUI+CLI、退出门禁 PASS；独立 OS 进程租约探针 PASS；隔离 named Mutex 验证已有实例时明确拒绝。未启动真实 CLI 业务去操作用户安装。 | PASS |
| P2 代理回退 | 下载请求工厂总返回同一个 HttpRequestMessage。按原调用方式注入离线 Handler，代理调用 1 次、直连 Handler 0 次，重复发送抛 InvalidOperationException。 | `UpdateDownloadService.cs`、`CloudHttpClientFactory.cs`：每次尝试创建新请求；路由层 using 负责请求释放，调用方负责响应释放。测试下载根目录按异步上下文隔离。 | 代理失败直连成功且请求不同；代理成功不直连；禁用 fallback 明确失败；206 Range + ETag/Last-Modified；长度/MZ/SHA256；坏哈希/大小；取消无额外直连 PASS。既有重试、200/206/416、metadata 及安装器顺序组 PASS。 | PASS |
| P2 验证不超时 | 原 Evaluate 在 deadline 前返回 WaitingForBattleNet。直接编译原状态函数，缺席且过期仍等待，复现成功。 | `AccountSwitchVerification.cs`、`MainViewModel.cs`、`AccountCommandRunner.cs`：本轮有效目标变化证据优先，其次明确认证过期，再检查截止时间；仅旧缓存不能确认登录。GUI 消费 Unconfirmed 并清等待；后续客户端启动可开始新的有界确认。 | 缺席未超时/超时、等待登录/超时、已确认成功优先、真实过期优先、旧缓存目标不误判 PASS；实际 VM 清除 pending 及后续启动重新验证 PASS。未知初始账号且没有可信变化证据时保持未确认，不假报成功。 | PASS |

## CLI 单实例策略

采用明确拒绝策略，不新增 IPC：已有主实例无论空闲、忙碌、托盘或退出中，CLI 都返回 exit 2，并提示在主窗口执行。这个策略兼容可能已经运行的旧版本，避免新 CLI 与未升级 GUI 并发。没有主实例时，CLI 在自己的生命周期内持有实例 Mutex，并使用与 GUI 一致的跨进程账号文件租约、游戏检查、区服判断及恢复校验。

`--switch` 只有观察到本轮有效目标登录结果才返回 0；未确认/需要重新登录返回 3，执行错误返回 1。`--addaccount` 返回 0 仅表示清指针并请求启动登录页，结果明确不声称认证成功。区服不可用时保留原 GUI 的“仅切账号”诊断，区服执行失败则阻止后续恢复/启动。

## 恢复故障边界及 journal

这不是操作系统级多文件原子事务。普通 IO 异常执行文件级回滚；模拟失败已经验证原始字节恢复。内存不足发生于预检时，目标尚未写入。进程被强杀、断电、磁盘损坏，或回滚文件被外部程序锁住时，不能保证自动还原完成。

写入前在账号根目录同级的 `<AccountsRoot>.recovery/restore-<GUID>/` 落盘原始快照及 `journal.json`。journal 含实际 `TargetRoot`、文件是否原本存在、相对路径、快照文件映射、时间及属性。`failure.json` 单独保存错误，不能覆盖映射。存在任何未清理 journal 时，新的恢复在目标写入前被阻止。

遇到残留 journal，先停下账号操作并另存整个恢复目录；确认实际 TargetRoot，再按 Files 映射人工恢复 `Existed=true` 的原始字节、删除该事务原本不存在的文件，按 Directories 检查原结构和属性。保留备份与证据直到确认目标一致，不能直接删除恢复目录来绕过门禁。本轮没有对真实恢复目录执行该步骤。

历史无 manifest 备份继续兼容；历史 v1 没有 ManagedPaths/ExcludedPaths 时使用原条目集合及空排除元数据。历史写入器没有实际复制的空目录元数据继续接受，所有清单文件仍必须存在并校验。未知文件和空目录保留；新快照复制目录。清指针改为同目录 staging + 单文件 Replace，锁定失败时原字节不变。

## 已执行的验证

最终源代码的定向 Release 构建：PASS，0 warning / 0 error，未携带或发布 Worker 正式包。初始修复候选的 BuildCommit 标记为起始 HEAD，含未提交源码，不能当作发布制品；提交后须以最终 HEAD 重新写入 BuildCommit 构建，最终本机候选的 provenance 单独记录于忽略的测试目录。

| 检查 | 结果 | 本机证据位置（均为 ignored） |
| --- | --- | --- |
| 六项修复前隔离复现 | REPRODUCED；P0/CLI/期限使用原始代码，重入/死锁为原控制流最小 fixture，HTTP 为原请求工厂契约 | `test-output/main-safety-baseline-repro/` |
| account-safety | PASS | `test-output/main-safety-final/account-safety/feature-selftest.txt` |
| update-safety | PASS | `test-output/main-safety-final/update-safety/feature-selftest.txt` |
| exit-safety（实际 STA Dispatcher） | PASS | `test-output/main-safety-final/exit-safety/feature-selftest.txt` |
| SOOP normal/stop/start/refresh/cancel timeout/crash/crash_all_running | 7/7 PASS，orphan=False | `test-output/main-safety-r3/soop/feature-selftest.txt` |
| VerifiedDifference + 状态检查/临时清理/重设 | PASS | `test-output/main-safety-r5/region-verified/feature-selftest.txt` |
| Drops recovery / Twitch connection / SOOP policy | PASS | `test-output/main-safety-r5/<group>/feature-selftest.txt` |

重复执行仅针对补修影响的组。未运行全仓库 lint/全量测试/四平台打包；Worker 协议及打包代码未改。真实扫码、真实切换、真实更新安装、数小时观看为 NOT TESTED（按任务要求未执行），模拟 PASS 不代表这些人工验收通过。

可在新的专用输出目录重新运行三个关键组：

```powershell
$candidateCommit = git rev-parse HEAD
dotnet build 'CloudLight Blizzard.csproj' -c Release --no-restore -v minimal -p:DropsArtifactsRoot=__safety_no_worker_artifacts__ -p:BuildCommit=$candidateCommit
dotnet 'bin/Release/net8.0-windows10.0.18362.0/CloudLight Blizzard.dll' --feature-selftest 'test-output/new-isolated-account-run' account-safety
dotnet 'bin/Release/net8.0-windows10.0.18362.0/CloudLight Blizzard.dll' --feature-selftest 'test-output/new-isolated-update-run' update-safety
dotnet 'bin/Release/net8.0-windows10.0.18362.0/CloudLight Blizzard.dll' --feature-selftest 'test-output/new-isolated-exit-run' exit-safety
```

预期退出码 0 且各报告 `OVERALL: PASS`；非零退出码、FAIL 或缺失报告均判失败。exit-safety 中独立 Worker 需要已有 `.worker-build-venv/Scripts/python.exe`；没有该环境不能声称 Worker 清理测试通过。不要复用含有重要文件的输出目录。

## 反向审查与保留范围

逐项核对：损坏清单无 legacy 旁路；删除/覆盖之前完成验证和 staging；失败可回滚或提供原始快照及阻止后续恢复；WPF 退出无同步异步 Dispose 等待；扫码取消与窗口关闭正常泵送 Dispatcher；托盘/取消不进入退出；Busy/租约在第一次 await 前；GUI/CLI 入口共用保护；直连仍保留续传头；缺席客户端进入截止终态；既有确认成功不被期限覆盖；退出阻止新任务及自动恢复；安装器在安全清理后启动。

另外修复了保存快照失败后 finally 删除最后一份 previous 的风险、源/目标 reparse 绕过、迟到初始化重新启动计时器以及更新安装早于账号事务结束的风险。检查全部 diff、新文件、Git 状态及 `diff --check`；测试日志、临时源码和模拟账号均保持 ignored。

`Integrations/Drops/`、工程版本、installer 和 build-support 均无修改；Twitch 授权/播放链路、SOOP UID 恢复/领取逻辑、YouTube 播放计时及 owned PID 清理沿用基线。SOOP Local/Distribution 判定、授权清单及发布校验没有改动。没有新增 Tag、Release、正式安装包发布或扩大再分发授权。

A：损坏 manifest 不会转成旧格式恢复。B：离线 QR/Dispatcher 退出各场景无死锁。C：同一管理路径的两次切换不能同时进入文件操作。D：两个 CLI 命令与 GUI 共用安全保护，已有实例明确拒绝。E：代理失败后新请求直连续传并完成校验。F：未启动时按期限结束，后续启动可有界再确认。G：Drops 修复代码及 SOOP 授权约束保留，上述定向回归通过。
