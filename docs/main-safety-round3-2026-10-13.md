# 主程序第三轮定向安全补修 — 2026-10-13

## 基线和边界

起始干净 `main`，HEAD `def92c0a2bc7ba1e3d97faef077a66f4f407eb07`，包含此前 Drops 基线。按照当前代码补修，没有 reset、clean、强推或覆盖用户未提交改动。版本仍为 `2.1.9`。

仅修改账号验证、账号操作安全结果、主窗口退出/更新门禁、恢复 journal 准备边界和相关自测。未修改 `Integrations/Drops/`、Worker 源码/协议、打包代码、SOOP 授权清单和 Local/Distribution 规则。没有启动真实安装器、切换真实账号/区服、真实扫码/领取/长时间观看，也没有关闭用户原有程序或修改系统配置。

本轮使用主代理完成实现、实际 diff 审查和验收，没有新增子代理任务。实际权限为 `danger-full-access`、审批 `never`，按用户限制只操作仓库及隔离测试目录，未调整权限或安全配置；模型/推理档位不从未公开的运行信息推测。

## 三项修复

| 问题 | 根因与离线复现 | 修改和关键机制 | 状态 |
| --- | --- | --- | --- |
| 登录误报 | 原 `AccountSwitchVerification.Evaluate` 将本地 ID 变化认作认证，且先于认证失效检查。直接编译 `def92c0` 原函数：进程运行、指针=目标、明确过期仍返回 `LoggedIn`。 | `AccountSwitchVerification.cs` 删除指针产生成功的分支；`BattleNetAuthLogProbe.cs` 限定新追加证据，忽略旧/截断/历史复制日志和无效游标；`AccountOperationService.cs` 在恢复完成、新启动前建立游标；GUI/CLI 使用相同判断。 | PASS |
| 更新退出 | 原窗口内部 catch 吞掉关键清理失败，更新仅 await 完成后继续安装。原 coordinator + 原吞错/启动控制流模型模拟关键错误，模拟安装器调用 1 次（此修复前复现是模型，不是原 WPF 窗口实测）。 | `ExitLifecycleCoordinator.cs` 增加阶段结果、Failed/TimedOut 和严格 `InstallerReady`；`MainWindow.xaml.cs` 启动前检查；账号服务保存文件操作失败，VM 等 lease 和 Busy 完全释放并检查旧 pending journal；`DropsHostService.cs` 未确认 owned Worker 已退出时抛关键清理失败。 | PASS |
| 准备 journal 残留 | 原 `CreateRestoreJournal` 只捕获 staging 异常。编译 `def92c0` 原 Store，在原快照/journal 写入位置注入 IO 异常：目标未变，却留下目录、阻止合法重试。 | `AppDataStore.cs` 外层 catch 覆盖全部调用自有准备工作；Preparing 完成后以 durable 文件替换持久化 Applying，再进入目标修改。准备失败只清理本次 GUID 目录；清理失败报告路径和两种错误，保留阻塞。 | PASS |

离线基线复现源码及隔离证据：`test-output/main-safety-round3-baseline/`（ignored）；快照/journal 复现仅增设故障注入，不改变原异常边界。登录复现使用原函数；更新复现明确为原控制流模型。

## A. 登录证据及用户结果

当前项目的 `BattleNetLoginEvidence` 只有 None、LoginPage、RealAuthExpired。未发现已有且经过验证的目标账号/本次会话正向认证源。因此生产 evaluator 不返回 `LoggedIn`；保留状态枚举供未来可靠来源使用，不添加猜测性成功正则、计时成功或读取 Token/UnifiedAuth。

明确的新过期证据返回 `LoginRequired`；无成功证据且到期返回 `Unconfirmed`；未到期按客户端状态等待启动/登录。账号文件恢复成功独立显示“账号配置已恢复，等待登录确认”，到期显示“账号配置已恢复，登录未确认”。缓存刷新不再显示“已重新登录”，也不覆盖待确认/过期结果。不删除保存的账号快照。

GUI 150 秒、CLI 120 秒的原期限保留。GUI 超时清除 pending；之后观测到客户端从未运行到运行，可建立新游标及新有界期限，不继承旧失败证据。游标从旧客户端退出和目标恢复之后开始；无法可靠归属的历史日志宁可保持未知。日志扫描是现有有限启发式负向诊断，不是服务器认证 API，不能承诺识别所有客户端日志格式。

CLI `--switch` 配置恢复且启动调用完成但未确认登录：exit **3** / `unconfirmed`；明确过期：exit **3** / `login_required`；执行错误 exit **1**；已有实例/非法参数 exit **2**。保留 JSON 格式和单实例拒绝政策。`--addaccount` 的 exit 0 / `login_page_requested` 只表示请求登录页，不是登录成功。

“可靠成功证据对应目标”“旧失败之后可靠新成功”的成功分支验收：**NOT TESTED / 无可靠来源**。不能把成功样式日志、模拟本地指针或未发现错误包装成真实成功证据；测试明确确认这些输入仍未确认。

## B. 更新退出结果

必须记录并通过 `AccountOperationDrain`、`DropsPageCleanup`、`DropsHostShutdown`、`SettingsPersistence`，且 coordinator 无关键异常/预算超时，才为 `InstallerReady`。非关键订阅/通知等清理警告独立保留，不冒充账号事务失败，也不阻止其它清理继续执行。

账号 lease 完成不等于账号事务成功：恢复/清指针/区服文件操作失败保留为本进程安全故障；失败被 UI 捕获后，安装门禁仍能看到。旧 `.recovery` 残留也会阻止安装，不自动删除。保守地在曾发生文件操作失败的本进程内拒绝安装；需要重新启动并检查恢复诊断，而不是清掉错误继续安装。

90 秒为合作式退出预算，不会强行抛弃正在写入或回滚的账号事务。超时仍等待事务安全结束，之后记为 TimedOut、拒绝安装、继续受控退出。若事务永远不结束，不允许安装器提前启动；不能以放弃文件事务换取硬超时。Worker 保留已有有界关闭/终止等待，无法确认退出则记录关键失败。

失败后页面/Host 已进入单向退出，不恢复按钮、Worker 或后台服务；提示失败阶段并关闭窗口。普通退出可以记录错误后降级关闭；更新则严格禁止启动安装器。安装器自身启动失败也保持受控退出，不恢复已 Dispose 的服务。没有 UI 同步等待 Dispose。

实际 WPF 窗口模拟安装器计数：关键成功/账号 lease 最终释放 **1**；账号恢复失败、旧 pending journal、页面失败、Host 失败、设置失败、超时各 **0**；非关键警告 **1**；安装器启动故障模拟 Start 调用 **1**、结果失败；重复请求不增加调用。真实安装器 **0**。

## C. Journal 边界

仅当前调用自己生成的 `restore-<GUID>`，且尚未执行目标目录创建/删除/覆盖时，允许清理。覆盖目录创建、第一/中间快照、journal 写入、staging 和 Applying 状态落盘失败。使用精确绝对路径和 reparse 检查，不清空 RecoveryRoot，不删除备份或未知旧事务。不能用 Directory.Exists 的 false 隐藏权限错误，删除仅将 DirectoryNotFound 视作已清理。

清理自身失败：原始异常+清理异常及残留路径同时上报，下一次恢复继续阻塞。Preparing 的旧残留不自动删除，因为新进程没有足够证据证明来源及是否已发生目标修改。

Applying 之后继续原有文件级回滚：成功还原原始字节并清理本次 journal；回滚失败保留原始快照、journal Files 映射、TargetRoot 和独立 failure.json，阻止后续恢复。仍不声称 OS 多文件原子性。断电、进程强杀和介质损坏不保证自动恢复；必须保留证据人工处理。关闭应用会等待同步恢复/回滚完成，不以取消丢弃它。

## 验证记录

| 检查 | 结果 | 范围/证据（本机 ignored） |
| --- | --- | --- |
| Release WPF 构建 | PASS，0 warning / 0 error | 主程序，不打包 Worker；提交后以最终 HEAD 作为 BuildCommit 再构建本地候选 |
| Account Safety | PASS | `test-output/main-safety-round3-r2/account-safety/feature-selftest.txt`；全部准备故障目标/备份逐文件字节一致、清理/合法重试、清理失败保留阻塞；Applying 回滚成功/失败映射；严格 manifest/legacy；真实隔离跨进程 lease/GUI+CLI 并发；证据游标顺序 |
| Update Safety | PASS | `test-output/main-safety-round3-r2/update-safety/feature-selftest.txt`；期限/指针/过期/历史日志；离线 Handler 代理回退、Range/ETag/日期、长度/hash、取消、原更新组 |
| Exit Safety / 实际 STA Dispatcher | PASS | 最终候选见 `test-output/main-safety-round3-final-exit/feature-selftest.txt`；二维码生成/等待/网络/排队成功/取消竞态/更新；托盘/取消/二次退出；真实隔离不响应 Python Worker 回收、模拟无法确认退出；GUI/CLI 同输入状态；真实隔离 SQLite 指针和保存快照字节 |
| Drops recovery 短回归 | PASS | `test-output/main-safety-round3-r1/drops-recovery/feature-selftest.txt`；未改 Core，不重复长时间网络验收 |
| 真实账号、区服、安装、扫码、奖励、长观看 | NOT TESTED | 按任务边界禁止执行；模拟 PASS 不表示真实账号验收通过 |

反向审查检查了全部实际 diff：无指针到 LoggedIn 路径；无关键吞错到安装路径；账号事务/旧恢复证据均有门禁；无同步 Dispatcher Dispose；二次退出不会二次释放；准备失败自有清理与 Applying 回滚边界分开；旧 journal 原始映射/字节保留；strict manifest、legacy、互斥、VerifiedDifference、代理请求所有权和续传未回退。补修了游标采集过早、旧 journal 更新旁路及 Directory.Exists 掩盖清理权限异常的问题，再运行受影响组。

完整 diff / `git diff --check` / 精确暂存清单经过核对。未纳入凭据、日志、测试目录、截图、候选二进制。最终提交为包含本记录的 main 提交，最终 HEAD、BuildCommit、UTC 构建时间及 SHA256 在本机 `test-output/main-safety-round3-provenance.md` 记录，最终交付报告同时给出 commit/push 和干净状态；不在文档中制造提交自身哈希的循环依赖。

复跑仅限新建的专用隔离输出目录（不得复用有重要文件的目录）：

```powershell
$candidateCommit = git rev-parse HEAD
dotnet build 'CloudLight Blizzard.csproj' -c Release --no-restore -v minimal -p:DropsArtifactsRoot=__safety_no_worker_artifacts__ -p:BuildCommit=$candidateCommit
dotnet 'bin/Release/net8.0-windows10.0.18362.0/CloudLight Blizzard.dll' --feature-selftest 'test-output/new-round3-account' account-safety
dotnet 'bin/Release/net8.0-windows10.0.18362.0/CloudLight Blizzard.dll' --feature-selftest 'test-output/new-round3-update' update-safety
dotnet 'bin/Release/net8.0-windows10.0.18362.0/CloudLight Blizzard.dll' --feature-selftest 'test-output/new-round3-exit' exit-safety
```

预期各命令 exit 0 且报告 OVERALL: PASS；非零、FAIL 或无报告均为失败。Exit 的真实隔离子进程自检需要已有 `.worker-build-venv/Scripts/python.exe`，未具备不能宣称通过。未进行全仓库静态扫描、完整测试矩阵或四平台打包。
