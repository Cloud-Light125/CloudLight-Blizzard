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

## 首轮验证（最终候选结果将在交付完成后补录）

| 检查 | 已观察结果 |
|---|---|
| SOOP Python | 60/60 PASS，含调度、UID、生命周期、随机、资格、Heartbeat、恢复与领取保护 |
| Twitch Python | 121/121 PASS，含 ClaimCoordinator、单项／批量、多 Benefit 及优先级 |
| Bilibili / YouTube / Shared | 21 / 8 / 3 PASS；新增分发授权测试待最终构建执行 |
| WPF Release 编译 | PASS |
| UI-v2 真实 WPF 组件 | PASS，零绑定错误；三种窗口尺寸、深浅主题、账号切换、详情、筛选及刷新 |
| 完整程序交互 | 已实际打开隔离 WPF；卡片、奖励详情、概览及观看配置可操作 |

官方只读快照来自 2026-11-18 11:50（北京时间），54 游戏、425 目录活动加 1 条账号历史任务，共 426 活动／617 奖励。首轮本机实测模型初始化 40.15 ms、初次布局 213.07 ms、范围筛选 26.87 ms；只创建 17 奖励控件。12 步滚动约 2.33–189.88 ms，进程专用内存 284143616 → 306360320 bytes。该结果说明按需实现有效，仍有冷滚动峰值；不将其写成稳定 FPS 或长时间性能 PASS。后续最终数据见补录及 `soop-performance.json`。

所有截图标注 OFFICIAL（官方只读快照）、REPLAY（历史隔离回放）或 SIMULATED（合成状态）。最终证据等待图片成功或失败终态后保存；缺图使用占位，不伪造官方素材。未切换物理显示器 DPI；窗口矩阵为实际 WPF DIP 尺寸。

## 公开分发阻断

直接上游：[tom1230123/soop-drops-miner](https://github.com/tom1230123/soop-drops-miner)，当前提交 `3283f5db1b2ce52d81441731cd41728ed262d5d8`，GitHub API 的 license=null，根目录无许可文件。固定 Core：[6050b8a35690b091996a1efedad4655b1b094cac](https://github.com/Cloud-Light125/cloudlight-soop-drops-miner/tree/6050b8a35690b091996a1efedad4655b1b094cac)，同样无许可证文件；运行源码摘要 `65801b6e10f1777cd08af4ae0c6903003b2544c6bfd58b60f7295ba77d879be6`。

2026-10-13 发布者声明允许本项目打包其所声明拥有的改动，但该声明明确不能独立建立直接上游许可。本轮没有发现覆盖上游贡献、向公众再分发及冻结二进制的权利人授权；因此无法放行。构建来源脚本和 Publish 门禁均加上上游权利核验，不能仅凭旧声明将 Local 构建改为 Distribution。

解除条件：取得相应权利人的明确许可，覆盖实际固定源码、修改和冻结安装包的公众再分发；记录版本、授权人、证据地址、版权与许可保留要求，并完成其它依赖条件。仅有公开仓库、署名、Local 候选或发布者单方声明均不充分。未上传受限源码或二进制；未请求发布删减 SOOP 的版本。

## 更新机制与安装边界

实际更新查询地址为 `https://cloudlight-feedback.2693327171.workers.dev/v1/update/latest`，服务查询 GitHub 正式 latest Release，过滤预发布并输出资产 digest。客户端支持 `3.0.0` / `v3.0.0`，精确匹配安装器文件名，下载后校验大小、MZ 与 SHA256；代理／失败回退／续传及退出后安装逻辑保留。

该客户端更新源码与 `v2.1.10` 的 `UpdateService.cs`、`UpdateDownloadService.cs` 相同。生产端本轮实际查询仍返回 `v2.1.10`。3.0.0 未公开，因此真实旧版发现／下载／安装 3.0.0 与远端文件 SHA256 对比为 BLOCKED，不能用模拟响应代替已发布验证。已经是 3.0.0 的本地候选不会自动更新到相同版本不同构建。

没有可用安全隔离 Windows 系统环境，完整系统新装、旧版升级、重启后的真实数据保留为 NOT TESTED。安装器解包与隔离设置保留测试仅在通过后单独记录，不等于系统升级通过。未操作真实用户配置、Battle.net 文件或稳定安装目录。

## 未完成的真实业务

真实 SOOP 长时间观看、双真实账号持续观看、随机活动获奖、新的真实 Twitch 批量领取均 NOT TESTED；本轮不发起真实 Claim。外部剪贴板占用若再次发生将单列 BLOCKED，不终止无关程序。公开 Tag、Release、正式下载与其他用户自动升级均受许可阻断。
