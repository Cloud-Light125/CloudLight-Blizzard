# Issue #3：账号快照保存、发现与迁移

日期：2026-10-15（Asia/Shanghai）。版本保持 **2.1.10**。

## 调查基线与证据

- [Issue #3](https://github.com/Cloud-Light125/CloudLight-Blizzard/issues/3) 创建于 2026-08-21 09:38:41 +08:00；通过 GitHub API 读取完整正文并分页读取全部评论，调查时评论为 0。
- 先 fetch、检查本地独有提交，再将 main 从 `cb26c89` 快进到远端 `f5a381939926960fcf930fb6b9a71d2c67a29e54`，确认 HEAD 等于 origin/main 后开始修改。已有未跟踪 artifacts 保留。
- [日志附件](https://github.com/user-attachments/files/31286561/logs.zip) 已实际下载至 Git 忽略的 `test-output/issue-3/`。SHA256：`8857C1514A362A56749DE530B85E8DE3BD3674414B103AEC3424DBE4843680F6`。
- ZIP CRC 通过；解压前限制总大小、条目数并拒绝绝对路径、路径穿越、链接和可执行内容。6 个文件共 14,063 字节，仅以文本分析。原始日志及提取内容不入库，不公开账号标识或认证值。

### 附件实际能证明什么

| 证据 | 结论 |
| --- | --- |
| update.log：8 月 19–20 日为 1.1.0；8 月 21 日 09:22 发现 2.0.0；09:24:40 起运行 2.0.0 | 报告发生于 1.1.0 → 2.0.0 更新时期，不能直接套用到 2.1.10 |
| account-switch.log：8 月 19 日 02:03:38、02:05:33、03:01:27、23:29:37，以及 8 月 20 日 02:29:50 开始切换 | 5 次都经过关闭、配置恢复和启动；4 次仅在本地指针变化后写出 Success，1 次停在 WaitingForLogin |
| region-switch.log：对应的客户端关闭与重启，目标 China | 无区服文件备份路径或写入异常证据 |
| update.log 曾有 api.github.com DNS 解析失败 | 更新请求的网络错误，不能当作账号快照 IO 错误 |
| 没有 SnapshotSaveFailed、IOException、UnauthorizedAccessException、manifest/meta/staging 写入记录 | **无法确定原用户的保存失败时间、源/目标路径、异常类型、权限/占用状况或迁移失败点** |
| 没有真实认证过期日志、绑定状态、账号配置文件或目录清单 | 不能证明掉登录的具体原因，也不能证明国服登录地址或网易绑定相关 |

## 根因分类

### 当前基线仍可复现，已在本次修复

使用 `git show f5a3819` 提取原始 AppDataStore / AppPaths / BattleNetPaths 到忽略的临时控制台工程，完全使用模拟目录，实际复现：

1. **空源也能保存成功**：空 RoamingDir 执行 Save 后，HasProfile=True，ValidateProfile 也通过。旧代码允许空 Entries，把无可恢复文件的快照标记为成功。
2. **staging 元数据被当作账号备份**：只有 `42.staging-interrupted/meta.json`、没有正式快照，ReadAllMeta 仍返回该账号。ViewModel 又只根据 meta 非空设置“已保存”。

代码审查另外确认：保存没有验证落盘后的 staging；旧目录清理失败会在正式提升成功后抛错；旧迁移逐文件补齐会混合已有账号目录；MigrationCompleted=true 会永久跳过遗留账号发现；CachedData.db 不存在时刷新会直接清空列表，即使账号快照存在。

以上是可确认的软件缺陷，**不能证明哪一项导致原用户当时的空文件夹**。原用户可能查看了不同类型的目录，但附件没有路径证据，不能据此下结论。

### 已由后续提交修复，完整保留

- `def92c0`：严格 manifest 预检、路径与内容校验、文件级回滚、GUI/CLI 共享门禁；不再将损坏 manifest 降级成旧格式。
- `cab3f0f`：本地指针和进程存在不证明登录；超时为未确认，只有明确失效证据才提示重新登录；恢复 journal 准备失败清理和退出安装门禁。
- 本次没有修改 Drops Worker、区服 VerifiedDifference 实现或版本号。

### 日志证据不足与服务端行为边界

快照保存的是 `%APPDATA%\Battle.net` 中受管理的账号选择配置，不是完整认证 Session。本工具不读取/修改 UnifiedAuth、密码或 `%LOCALAPPDATA%\Battle.net\Account` 中的受保护认证数据。

服务器若撤销或拒绝会话，本地恢复配置无法延长其有效期。附件没有这种事件，不能断言本 Issue 的掉登录就是服务端过期，也没有依据承诺固定有效时长。[用户提供的国服登录页面](https://account.battlenet.com.cn/login/zh/?app=apph&showCredentials=true) 只呈现登录入口，不提供“该入口/未绑定网易导致过期”的证据；官方支持资料检索也未得到可用于证明该关联或固定过期周期的文档。

## 两种目录

| 数据 | 默认位置 | 自定义设置 |
| --- | --- | --- |
| Battle.net 账号快照 | Windows 文档目录下 `CloudLight/CloudLight Blizzard/accounts` | 不受 RegionStoragePath 影响；以账号页/设置页显示的实际路径为准，支持 Windows 文档重定向 |
| 守望先锋区服文件 | 应用数据根目录的 `region-switch` | RegionStoragePath，仅影响区服文件 |
| 历史账号快照 | `%LOCALAPPDATA%/BnetSwitch/accounts` | 启动时验证后复制迁移，保留原目录 |

不存在“自定义账号目录失效后静默回退默认目录”的设置逻辑。区服位置设置没有被改造成账号路径设置。

## 修复流程与交互

1. 账号操作仍先获得共享门禁，识别当前本地账号并关闭客户端，再保存配置；无账号时提示先登录。
2. 创建独立 staging，复制受管理文件，新增文件全部记录 SHA256，持久写入 manifest 和 meta。
3. 复用严格校验器验证非空文件集合、meta 的账号匹配、manifest 结构/路径/大小/摘要，再提升为正式快照。失败不能提前修改“已保存”。
4. 提升失败回滚旧目录；即使提升和回滚都失败，也保留 `.previous-*`。提升已成功而旧目录清理失败时记录 SnapshotCleanupPending，不能将成功保存误报失败。
5. 保存失败提供阶段、异常原因、实际源/目标目录和处理建议；日志记录阶段、异常类型和路径，成功记录文件数和位置。
6. 正式账号枚举仅看当前根目录的规范正整数子目录；忽略 staging/previous，损坏或不可读的正式快照仍展示，不能冒充“未保存”。元数据和 manifest 都通过校验才可恢复。
7. 缺少 Battle.net 缓存库时仍发现已有快照；根目录枚举失败先报错并保留当前列表。
8. 卡片区分未保存、正在保存、有效已保存、需重新登录后更新、损坏、目录不可用、保存失败。失败后若旧快照仍有效，可以继续安全恢复旧快照。
9. 无快照/损坏/失效账号提供重新登录入口，包括本地指针仍指向该账号的情况。沿用共享门禁与清本地指针流程，请用户在客户端手动选择该账号，再手动保存；不伪造切换成功。
10. 保存不再自动清除认证失效提醒。保存确认窗口提供默认关闭的“我已在客户端完成重新登录”人工确认，只有明确确认并保存后清除提醒；这是用户声明，不输出服务器登录成功。
11. 账号页和设置页均显示同一实际快照目录；账号页可打开目录、查看错误详情，保存确认说明认证边界。

### 安全迁移

启动时即使旧 MigrationCompleted 已完成，仍检查已知旧账号根目录。旧快照先校验，复制至新目录 staging，再校验并对比源/副本/复制后的源的文件数量及聚合 SHA256，最终整目录提升。记录摘要、文件数和最终位置。

- 不合并账号目录；已有新目录一律不覆盖。新目录损坏或冲突时保留两份并提示人工核对。
- 摘要和复制范围仅包含已校验 manifest 管理的配置及 meta/manifest，不读取或迁移未声明的敏感认证残留；使用锁住的模拟 UnifiedAuth 文件验证这一边界。
- 仅兼容 manifest 真正缺失的合法旧格式；损坏/不可读 manifest 不降级。
- 不将 staging 或 previous 当作正式快照；历史残留和未知路径不自动导入、不自动删除。
- 旧、新 accounts.recovery 存在待处理 journal 时阻止迁移；默认账号存储恢复及保存还检查旧 journal。不复制或清空旧 journal。
- 此迁移仅发现已知历史默认目录；用户手动搬到任意未知目录、冲突目录或中断遗留的 previous 需要保留原文件并人工检查，不能宣称已自动恢复。

## 验证

全部故障注入、模拟 SQLite、备份与区服文件测试均使用 `test-output/issue-3/` 的隔离目录。没有真实账号切换、游戏区服覆盖、更新安装或 Drops 长时间观看。

| 检查 | 结果 |
| --- | --- |
| 原始 f5a3819 隔离复现 | 确认空源假成功、staging 元数据误发现 |
| WPF Release 构建 | PASS，0 warnings / 0 errors；使用已有依赖，不下载依赖或打包 Worker |
| FeatureSelfTest account-safety | PASS；包含保存故障、损坏/合法旧格式、meta 匹配、staging 排除、旧目录迁移、失败保留旧数据、journal、GUI/CLI 跨进程门禁、配置恢复和认证状态机 |
| FeatureSelfTest region-verified | PASS；VerifiedDifference、旧 FullSnapshot、版本与维护流程 |
| 定向 WPF UI `--ui-selftest <report> accounts` | PASS；真实卡片模板渲染、路径一致、无快照/损坏账号入口、人工确认默认关闭、无绑定错误；已检查生成的 accounts-ui.png |
| git diff --check | PASS；提交前检查实际 diff 与暂存范围 |

首次全 UI 检查暴露新增断言先于绑定求值的问题；改为等待 Dispatcher 处理绑定，并新增 accounts 定向入口，最终定向 UI 全部通过，不必重复启动四个平台。故障注入还促使修复了“根路径被文件占用误判未保存”和保存提升阶段错误定位。

## 原用户人工复测

1. 保留所有原目录；打开账号页和设置页，确认看到相同的 `accounts` 实际位置，再与修改过的区服目录对照。
2. 如有迁移提示或 journal，先保留证据并处理冲突；不要删除或把损坏 manifest 改名成旧格式。
3. 在 Battle.net 手动登录目标账号，刷新账号列表，选择更新备份；只有确认已在客户端完成登录时才勾选人工确认。检查有效状态与真实目录内的 BattleNet/manifest.json/meta.json。
4. 保存失败时记录新的 SnapshotSaveFailed 阶段/异常类型和已脱敏路径；确认原有效快照仍在。不要在真实账号目录注入故障。
5. 用户自行执行账号切换，区分“配置已恢复”和客户端真实认证结果；如再次掉登录，提供该时段的脱敏失效事件，才能继续定位。

Issue 保持 **OPEN，等待原用户环境复测**。本次没有证明原环境保存失败的唯一根因，也没有证明国服网页、网易绑定或服务端失效与该报告的因果关系。

## 最终保护检查与运行配置

主代理独立检查文件、diff、定向测试和集成：strict manifest、合法 legacy、恢复 journal、GUI/CLI 互斥、退出等待、VerifiedDifference 均保留；Drops Worker 与已完成 Drops 修复没有修改。无原始附件、认证值、安装包、Tag、Release 或版本变更入库。

用户级配置显示 GPT-6.1 Sol / high，multi_agent 与 multi_agent_v2 均为 false；运行工具没有独立的实际模型/推理强度查询接口，不能将配置值当作独立实测。会话实际权限为 danger-full-access、审批策略 never，覆盖全局 workspace-write；本次只执行用户授权的仓库修改、隔离验证、提交和推送，未更改权限、安全、多代理或系统代理设置。GitHub 访问仅使用命令级 127.0.0.1:7897 代理。
