# Twitch 真实账号验收

验收日期：2026-11-16 至 2026-11-17，Asia/Shanghai。账号 UID 末四位 3382；原用户数据目录未覆盖。认证、真实图片、官方进度及观看/停止分项验收通过；没有执行真实 Claim。

## 真实链路与证据

使用正常 WPF 应用及原打包 Twitch Worker，独立数据目录 `test-output/real-twitch-20261116/private-real-data` 复用既有 Session。没有 `--ui-review` 或假 Core；记录器 `--drops-evidence` 仅在独立验收入口显式开启，提交零查询、零领取。普通运行不启用记录器。

- 官方 `id.twitch.tv /oauth2/validate` 返回 HTTP 200；已保存 Session 有效，无需本人重复授权，没有创建新设备码。
- 初次快照：96 Campaign / 337 Drop / 358 Benefit；后续官方刷新：98 Campaign / 360 Drop / 381 Benefit。数量随服务返回变化，没有写死活动。
- 图片来源沿用 `DropsCampaign.image_url` 与 `Benefit.image_url`，经现有 JSONL → Host → ViewModel → VisualImage。没有新增图片 API 或额外图片 GQL。
- [脱敏证据摘要](real-evidence-summary.json) 记录实际 Campaign / Drop / Benefit ID、无签名图片路径、原 URL SHA256、WPF bitmap 加载尺寸、缓存存在及命中状态。至少 19 个 Benefit 图片具备官方字段到可见 WPF bitmap 的匹配证据。原始 Cookie、Token、授权头和签名查询参数没有加入 Git。
- 真实观看主活动：Anno 117 - DLC3 Drops，Campaign `96bdfce5-e774-457f-aa8f-f693144ee50f`；Drop `3e116e8c-cbbe-11f1-9a15-0a58a9feac02`，Sacred Scarab&Eye of Ra。官方确认从 `0/120` 增长到 `33/120`；正常停止/重开后保留 `35/120`，后续到 `50/120`，界面显示 `41.7%`、剩余 `70` 分钟。没有用本地估算补入官方值。
- Core 自动发现实际频道 Pirolino1966，重开后自动选择 Zoeiittv。HLS playlist / media segment HEAD HTTP 200，watchRequests.lastResult=accepted 与频道 ID、最近接受时间对应；它单独证明观看连接，官方分钟另以 Core 的 real_current_minutes 验证。
- 开始前无频道时显示“等待合资格频道”；发现“仅优先游戏且优先列表为空”的真实配置限制，仅在独立验收目录改用原有 ENDING_SOONEST 策略，正式配置未写入。
- 实际多 Benefit：Hearty Homecoming / Palia，Campaign `d6a79a3e-fd63-43ed-906d-3a8443cd8184`。Tier One 的 4 件、Tier Two 的 3 件全部显示；分别共享所属 Drop 的 0/60、0/180 分钟，资格不足单独标明。没有按 Benefit 发明独立进度。
- 实际完成/可领取：Butterfly Wardrobe 的 Profile Icon / Portrait Frame / Belonging 分别 30/30、90/90、180/180，均显示“已完成 · 待领取”。只检查展示，未点击领取。
- 正常停止后当前 Drop 清除，已确认的 50/120 留在活动卡，观看状态改为已停止；正常退出释放本次 Worker。手动停止与再次开始均使用原保护逻辑。

## 实际 WPF 截图

| 截图 | 内容 |
|---|---|
| [01](screenshots-real/01-session-verified.png) | 真实 Session 验证成功 |
| [02](screenshots-real/02-initial-official-images-no-channel.png) | 修复前真实图片已显示，但无频道、历史完成奖励占主卡 |
| [04](screenshots-real/04-density-overview-watching.png) | 高密度概览、实际当前奖励、频道与官方分钟 |
| [05](screenshots-real/05-density-activities-watching.png) | 多 Campaign 的真实物品网格 |
| [06](screenshots-real/06-density-activities-maximized.png) | 最大化活动页，3 Campaign / 8 张完整真实奖励卡 |
| [07](screenshots-real/07-real-multiple-benefits.png) | 真实 Palia 3 Drop / 8 Benefit，全部图片与共享进度 |
| [08](screenshots-real/08-real-benefit-details.png) | 真实多 Benefit 原详情入口 |
| [09](screenshots-real/09-real-completed-claimable.png) | 真实已完成、可领取的 3 奖励，未提交 Claim |
| [10](screenshots-real/10-density-overview-maximized.png) | 真实当前观看概览，50/120、41.7%、剩余 70 |
| [11](screenshots-real/11-real-stopped-progress-retained.png) | 手动停止后未自动复活，官方 50/120 保留 |

真实截图与 `screenshots-density-simulated/` 严格分开。物理显示器 125%；WPF RenderTargetBitmap 输出为 96 DPI 栅格，不能由 PNG 像素大小推断物理 DPI。

## 分项结果与边界

| 项目 | 证据等级 / 结论 |
|---|---|
| 真实 Session / Campaigns / Inventory | REAL E2E / PASS |
| 官方奖励图片、游戏封面、真实多 Benefit | REAL E2E / PASS |
| 官方分钟更新、当前频道、观看请求 | REAL E2E / PASS |
| 正常停止 / 再开 / Session 恢复 | REAL E2E / PASS |
| NoAutoClaim、原领取按钮和前置条件 | COMPONENT / PASS；真实 Claim NOT TESTED（用户禁止） |
| 已领取显示、错误回退、网络恢复/Worker 故障注入 | SIMULATED / PASS 或 COMPONENT / PASS，未冒充真实故障操作 |
| 当前返回没有已领取条目 | 已领取 REAL E2E / NOT TESTED；单独模拟显示通过 |
| SOOP/Bilibili 真实登录 | NOT TESTED；未获得额外本人授权，采用隔离 UI/Host 证据 |
| YouTube | 仅已有 UI 映射与组件，不维护真实登录/播放 |

最初检测到旧安装版和打包父/子进程，确认其无有效观看任务，主代理没有强制关闭；独立目录避免共写 Session。前半段官方进度存在其他程序是否在外部观看的归因限制，不能声称绝对独占账号。后续枚举仅剩本次验收 Worker，原代理地址一直为当前应用范围的 `127.0.0.1:7897`。

Worker 源码本轮未改。Twitch SHA256：`bc4816a3cc7edf4088236f9786af4822eb0dc8fde317e294c145e8c975313ac2`，来源 `25fc6bbb84765ac1ad7807d78ce9e7bd1c8f061c`。54 个适配源文件逐个校验通过。原清单 dirty=true 保留，未伪改来源；本轮复用已验证二进制，最终包再逐个校验。
