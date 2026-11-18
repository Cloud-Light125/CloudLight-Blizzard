# CloudLight Blizzard 3.0.0 最终交付记录

本轮发布目标仍为 **3.0.0**、Tag **3.0.0**。完整公开发行当前 **BLOCKED**：直接上游未提供可确认的许可证或覆盖公众冻结二进制的授权。完整 SOOP 功能保留于 LOCAL-ONLY 候选；没有删除组件冒充完整版本，没有创建公开 Tag 或 Release。

## 来源与权限

- 起始 HEAD：`2f6f66839856a8b0d0cd25b009041211eca87520`；工作分支 `ui/fluent-v2`。
- 改动前已保存排除敏感文件的源码快照、工作区／暂存 diff、未跟踪源码及 SHA256 清单，位置为仓库外 `delivery-3.0.0-20261118/snapshot-20261118-122852`；既有候选未覆盖。
- 新旧 origin 地址经 GitHub API 确认为同一仓库，ID `1332990262`；目标仓库 `Cloud-Light125/CloudLight-Blizzard`，默认分支 `main`。
- 2026-11-18 远端核查：最新正式版 `v2.1.10`，没有 `3.0.0` / `v3.0.0` Tag 或 Release；没有仓库发布 Actions 工作流。没有强推或改写历史。
- PowerShell 7.6.9。实际 sandbox=`danger-full-access`、approval=`never`，与可读全局默认相同。全局记录模型 `gpt-6.1-sol`、推理 `high`；任务的实际后端模型／推理强度未独立暴露，不能确认有无模型级覆盖。两个 multi-agent 配置均为 false，未使用子代理，未更改安全配置。

## SOOP 界面与数据

共享 `RewardCard`、`RewardTilePanel` 和 `VisualImage`，保留 SOOP 自有 Game/Activity/Mission/Item 与 UID 投影；未修改已验证的调度或观看协议。固定型任务只使用该 UID 官方分钟，随机型／未知进度隐藏确定百分比和进度条。活动按 `dropsIdx` 区分，长标题两行及 Tooltip，奖励尺寸沿用 Twitch。

游戏分组显示活动、进行中、可参与和完成数量、选择与优先级，允许折叠。活动显示生命周期、任务／奖励数、时间、资格、计划状态和活动 ID。优先级的勾选、排序、五种策略与原保存操作集中在观看配置；账号背包领取入口保留。

显示范围作为独立应用偏好持久化，默认全部活动、已选游戏置顶；不调用 Worker `save_settings`。账号切换使用 UID+活动 ID 缓存，旧 UID 奖励详情动作不作用于新 UID。刷新保留未变化的活动对象和列表滚动位置；关闭控件取消图片请求并解除事件订阅。

## 最终候选验证

| 检查 | 最终结果 |
|---|---|
| SOOP Python | 60/60 PASS，含调度、UID、生命周期、随机、资格、Heartbeat、恢复与领取保护 |
| Twitch Python | 121/121 PASS，含 ClaimCoordinator、单项／批量、多 Benefit 及优先级 |
| Bilibili / YouTube / Shared | 21 / 8 / 5 PASS，包含新增分发授权拒绝回归；Python 合计 215 项 |
| 冻结二进制回放 | Twitch 121 / SOOP 60 PASS，从最终 EXE 提取字节码，禁止远程连接 |
| WPF Release 自包含构建 | PASS，Windows x64，版本 3.0.0 |
| FeatureSelfTest all | PASS |
| account-safety / region-verified | PASS；Battle.net 使用隔离数据 |
| update-safety / exit-safety | PASS |
| twitch-audit / packaged-workers | PASS；四平台真实冻结进程 JSONL、停止、重启、优先级及模拟数据保留 |
| SOOP 超时、崩溃及恢复 | PASS，A/B 独立恢复、generation 更新、orphan=False |
| UI-v2 真实 WPF 组件 | 133 PASS、0 FAIL、零绑定错误；三种窗口尺寸、深浅主题、账号切换、详情、筛选及刷新 |
| 完整 UI/Host review | 528 布局组合，零绑定错误；草稿、保存路由和取消保护通过 |
| 解包后 UI/Host 自检 | 347 PASS、0 FAIL；本次真实 STA / Win32 剪贴板写入、读回及复制按钮 PASS |
| Cloudflare 更新服务 | TypeScript 类型检查 PASS；12/12 用例 PASS；修复写死月份的测试夹具，未部署服务 |
| 安装器载荷 | PASS，564 文件逐一 SHA256 与发布目录相同 |
| 安装器隔离数据保留 | PASS，设置、模拟 Session／claim-journal／账号／区服文件保留；主动在实际安装前退出 |
| 解包后真实程序 | 已用安装器提取的 EXE 打开隔离完整 WPF；概览、奖励卡、显示范围、UID 切换可操作；包内四 Worker JSONL 再验 PASS |

官方只读快照来自 2026-11-18 11:50（北京时间），54 游戏、425 目录活动加 1 条账号历史任务，共 426 活动／617 奖励。最终本机实测模型初始化 38.31 ms、初次控件布局 207.47 ms、范围筛选 21.90 ms；只创建 17 奖励控件，展开全部后仍为 17 个已实现控件。12 步滚动约 0.47–171.24 ms，进程专用内存 272957440 → 283897856 bytes。初次布局计时不包含异步图片全部终态；账号切换没有另做独立耗时基准。该结果说明按需实现有效，仍有冷滚动峰值；不将其写成稳定 FPS 或长时间性能 PASS。完整数据见 [性能记录](release-3.0.0-evidence/checks/soop-performance.json)。

曾在重复使用旧缓存测试目录时出现缓存命中计数断言失败；改用全新隔离目录后通过，最终候选使用全新目录验收。没有修改缓存业务来迎合被污染的测试目录。

所有截图标注 OFFICIAL（官方只读快照）、REPLAY（历史隔离回放）或 SIMULATED（合成状态）。最终证据等待图片成功或失败终态后保存；缺图使用占位，不伪造官方素材。未切换物理显示器 DPI；窗口矩阵为实际 WPF DIP 尺寸。

| 界面证据 | 类型 |
|---|---|
| [改造前](release-3.0.0-evidence/screenshots/before-soop-OFFICIAL.png) / [改造后 1366×768](release-3.0.0-evidence/screenshots/soop-OFFICIAL-1366x768-light.png) | OFFICIAL，只读快照 |
| [1920×1080 深色官方卡片](release-3.0.0-evidence/screenshots/soop-OFFICIAL-1920x1080-dark.png) | OFFICIAL，只读快照 |
| [1600×900 多奖励](release-3.0.0-evidence/screenshots/soop-cards-1600x900-light-SIMULATED.png) | SIMULATED |
| [随机活动](release-3.0.0-evidence/screenshots/soop-random-SIMULATED.png) / [未来活动](release-3.0.0-evidence/screenshots/soop-future-SIMULATED.png) / [结束与完成](release-3.0.0-evidence/screenshots/soop-ended-completed-SIMULATED.png) | SIMULATED |
| [奖励详情](release-3.0.0-evidence/screenshots/soop-reward-details-SIMULATED.png) / [观看配置](release-3.0.0-evidence/screenshots/soop-watch-config-SIMULATED.png) | SIMULATED |
| [包内程序概览](release-3.0.0-evidence/screenshots/soop-overview-SIMULATED.jpg) / [活动与奖励](release-3.0.0-evidence/screenshots/soop-activities-SIMULATED.jpg) | SIMULATED，桌面实际窗口截图 |
| [仅显示已选游戏](release-3.0.0-evidence/screenshots/soop-selected-SIMULATED.jpg) / [UID B 的 3/60](release-3.0.0-evidence/screenshots/soop-account-B-SIMULATED.jpg) | SIMULATED，安装器解包 EXE |

## Git 与构建身份

- 功能集成提交：`3daa31d`；构建及分发门禁：`c123ed0`；构建前文档提交：`3257e430aac7b42118b652021d934abc3012d1e3`。
- 最终应用源码／App BuildCommit：**`7bd39c83a2e32a504784754afbacda99a6636d9e`**，包含 SOOP 缓存刷新不改变 legacy 观看策略的修复及回归。
- 四 Worker 在 `3257e430aac7b42118b652021d934abc3012d1e3` 全部重新冻结；它与应用 BuildCommit 之间只有两个 WPF 文件变化，`git diff 3257e43 7bd39c8 -- Integrations/Drops` 为空。冻结来源逐组件保留，未将 Worker 来源伪写成后来的应用提交。
- 上述提交均已推送 `ui/fluent-v2`。之后只更新交付文档与脱敏截图，最终分支 HEAD 以本报告所在 Git 提交为准；运行源码不变。
- `main` 仍为 `813e6deda6ded9be8d76392594e38e859875738d`，分支保护为 false，无 ruleset，无现有发布 Actions；本轮没有合并、强推或删除 main 提交。
- Tag `3.0.0`：未创建，BLOCKED；GitHub Release 对应提交／页面／正式安装包下载地址：均不存在，BLOCKED。
- 源码清单 SHA256：`595d1e79acc59ff82345b556882afec422ccc64ae1e273ea3b43302cc3db8577`。完整构建时源码快照位于本地 `release/source-snapshots/source-snapshot-595d1e79acc5/`。
- 独立原始 SOOP Core 不能合法加入公开仓库；完整本机构建仍需要外部的固定 Core，路径、提交与运行源码哈希在 Worker 清单中记录。因此不能宣称完整公开源码已经自足重建受限 Core。

## 本地二进制交付

候选目录：`release/candidates/3.0.0-soop-cards-local-20261118/`，标记 **LOCAL-ONLY**。安装器为 `installer/CloudLight-Blizzard-3.0.0-win-x64-Setup.exe`。它是本轮已提交来源新构建的自包含包，未复用或改名旧安装包。旧 SOOP 多游戏候选 SHA256 仍为 `dec622f295eef9bb45fe2e08e8aa0f8f431ff8bbb5855ec837fb7293ba3317dd`，核对未变。

| 二进制 | SHA256 |
|---|---|
| WPF EXE | `58af0f9e5e8e9b71fa86a01a6caff971f204f0a96b0fd586865aab6da50cd218` |
| WPF DLL | `ee8634d38d6df67886b555f83b014725ce14d9695be5726ffd55a4c3bd654b6b` |
| Twitch Worker | `62311167e869cfe78ef589c7abe1befeaa0ebd83968567851305a6e677c86c9c` |
| SOOP Worker | `2a0f25e1ebaade2f91e6c2f0a98531f91ced01c6bf08c55b1cbdf3619edc2cd9` |
| Bilibili Worker | `9f87b786a6f07ab4b53b4064511d23d2bf4a673d06ac374e91610f9a8533493f` |
| YouTube Worker | `4592388f4cb997823081e017e7b411017235357e07b1b3b286846ae401cba1e3` |
| 安装器 | **`b1592638acf5e0c530f2e12a9188669e7d27ed08390853910d45049d14bd7dde`** |

校验清单：[SHA256SUMS.txt](release-3.0.0-evidence/checks/SHA256SUMS.txt)。本地 `candidate.json`、`manifests/workers.json`、`manifests/source-manifest.json`、`manifests/payload-manifest.json` 分别记录版本、四 Worker 来源、源码和完整载荷。包内保留 .NET、Python、OpenSSL、Twitch、Bilibili 及其它依赖声明；许可证收集来自本轮实际构建环境。原始真实账号审计工具未进入 Release 程序，冻结包中没有真实 Claim 测试脚本或账号配置。

## 文档审查失误与处置

`3257e43` 中误加入了一份既有真实 Claim 操作叙述，违反本轮不提交真实 Claim 审计数据的要求。没有包含 Session、Token 或 Cookie；原文已保存在仓库外本机快照，并于 `e708de456bb7750af46dea268f3ae7274d23df7e` 替换为隔离技术摘要并推送。**这只能清理当前文件，原文仍存在于中间 Git 提交。**遵照禁止强推／改写历史的指令，没有擅自清除历史；该历史问题不能标为已彻底移除。

## 公开分发阻断

直接上游：[tom1230123/soop-drops-miner](https://github.com/tom1230123/soop-drops-miner)，当前提交 `3283f5db1b2ce52d81441731cd41728ed262d5d8`，GitHub API 的 license=null，根目录无许可文件。固定 Core：[6050b8a35690b091996a1efedad4655b1b094cac](https://github.com/Cloud-Light125/cloudlight-soop-drops-miner/tree/6050b8a35690b091996a1efedad4655b1b094cac)，同样无许可证文件；运行源码摘要 `65801b6e10f1777cd08af4ae0c6903003b2544c6bfd58b60f7295ba77d879be6`。

上游 README 确有“可单独分发 exe 文件”以及衍生版本须保留免责说明的文字，这是分发意向的正面证据，不能忽略。该 README 署名 www5329，完整免责条款指向程序内说明；固定 CloudLight Core 的 README 同时明确记载源码与再分发许可仍不明确。本轮未能确认版权主体及这些提示是否完整覆盖当前固定、修改后 Core 嵌入另一应用的公众冻结二进制分发，也未取得明确补充授权，因此仍保留 BLOCKED。该判断基于授权范围尚未确认，不单以 GitHub 的 license=null 作结论。上游 Release 说明未补充可确认的授权条款。

2026-10-13 发布者声明允许本项目打包其所声明拥有的改动，但该声明明确不能独立建立直接上游许可。本轮没有发现覆盖上游贡献、向公众再分发及冻结二进制的权利人授权；因此无法放行。构建来源脚本和 Publish 门禁均加上上游权利核验，不能仅凭旧声明将 Local 构建改为 Distribution。

解除条件：取得相应权利人的明确许可，覆盖实际固定源码、修改和冻结安装包的公众再分发；记录版本、授权人、证据地址、版权与许可保留要求，并完成其它依赖条件。仅有公开仓库、署名、Local 候选或发布者单方声明均不充分。未上传受限源码或二进制；未请求发布删减 SOOP 的版本。

## 更新机制与安装边界

实际更新查询地址为 `https://cloudlight-feedback.2693327171.workers.dev/v1/update/latest`，服务查询 GitHub 正式 latest Release，过滤预发布并输出资产 digest。客户端支持 `3.0.0` / `v3.0.0`，精确匹配安装器文件名，下载后校验大小、MZ 与 SHA256；代理／失败回退／续传及退出后安装逻辑保留。

该客户端更新源码与 `v2.1.10` 的 `UpdateService.cs`、`UpdateDownloadService.cs` 相同。生产端本轮实际查询仍返回 `v2.1.10`。3.0.0 未公开，因此真实旧版发现／下载／安装 3.0.0 与远端文件 SHA256 对比为 BLOCKED，不能用模拟响应代替已发布验证。已经是 3.0.0 的本地候选不会自动更新到相同版本不同构建。

没有可用安全隔离 Windows 系统环境，完整系统新装、旧版升级、重启后的真实数据保留为 NOT TESTED。安装器解包与隔离设置保留测试仅在通过后单独记录，不等于系统升级通过。未操作真实用户配置、Battle.net 文件或稳定安装目录。

## 未完成的真实业务

真实 SOOP 长时间观看、双真实账号持续观看、随机活动获奖、新的真实 Twitch 批量领取均 NOT TESTED；本轮不发起真实 Claim。外部剪贴板占用若再次发生将单列 BLOCKED，不终止无关程序。公开 Tag、Release、正式下载与其他用户自动升级均受许可阻断。
