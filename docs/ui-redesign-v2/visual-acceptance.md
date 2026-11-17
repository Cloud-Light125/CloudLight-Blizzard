# 本轮高密度 Twitch 验收（2026-11-17）

对照用户官方背包参考：将“一个巨大单奖励主卡 + 通栏进度 + 独立频道卡”改为紧凑账号/运行栏、当前观看状态条、按 Campaign 分组的 Benefit 网格。统一 238 DIP 卡高、约 172–200 DIP 卡宽、90 DIP 图片区，名称 13.5、状态 12.5、百分比 15 DIP。长名受两行高度约束，完整名可在 Tooltip 与详情读取。完成观看、可领取、已领取、资格不足、前置未满足分别投影，只有最近实际频道的 accepted 请求才标明正在观看。

## 一屏密度（SIMULATED / PASS；真实 WPF 控件）

当前物理 DPI 为 125%，按目标物理窗口尺寸换算 DIP，在实际 WPF 窗口测量控件边界；截图由实际窗口 RenderTargetBitmap 输出。100/150% 仍为等效 DIP 模拟，没有更改系统 DPI。以下首屏完整张数不含仅露出图片的卡片。

| 请求窗口 @125% | 每行卡片 | 概览完整卡 / 相交卡 | 活动页完整卡 / 相交卡 |
|---|---:|---:|---:|
| 1366×768 | 4 | 4 / 4 | 4 / 8 |
| 1600×900 | 5 | 5 / 10 | 5 / 10 |
| 1920×1080 | 6 | 6 / 11 | 11 / 11 |
| 2560×1440 | 9 | 11 / 18 | 11 / 18 |

数字来自 [density-results.txt](density-results.txt)，按内容宽度自适应，没有硬编码显示器分辨率。真实账号每个活动的物品数量不同：最大化活动页实际首屏为 3 Campaign、8 张完整物品卡；真实 Palia 筛选页为一行 8 Benefit。单活动只有 2 件时保留 2 卡，空余部分不放假奖励。

| 布局截图（显式模拟） | 用途 |
|---|---|
| [概览 1366](screenshots-density-simulated/overview-1366x768-125.png) | 首屏状态、操作、4 张完整卡 |
| [概览 1600](screenshots-density-simulated/overview-1600x900-125.png) | 每行 5 卡、多 Benefit 共享进度 |
| [活动 1920](screenshots-density-simulated/activities-1920x1080-125.png) | 一屏 11 张完整卡、未完成/待领取/已领取/资格与前置 |
| [深色](screenshots-density-simulated/activities-dark.png) | 控件、文字、输入框与卡片对比度 |
| [无频道](screenshots-density-simulated/no-eligible-channel.png) | Worker 运行不等于有效观看 |
| [筛选空态](screenshots-density-simulated/filtered-empty.png) | 当前筛选无结果，不误报账号无活动 |

真实账号概览、活动页、多 Benefit、完成状态见 [real-twitch-e2e.md](real-twitch-e2e.md)。主代理已逐张查看上述关键截图及原 01/09/42/43/44，未以自动化结果代替视觉检查。

性能：外层 Campaign 使用 WPF Recycling 虚拟化，图片按可见滚动区域加载，离屏取消待处理请求；卡片采用 192px 解码、详情 640px，二者共享原图片磁盘缓存。保留 4 并发、7 天缓存、32 MiB/64 张内存上限，快速切图防旧结果覆盖。刷新复用 Campaign/Benefit 卡对象。实际最高观测同屏加载按可见图片数增长，没有一次性下载全部 381 个 Benefit；未声称做过数小时独立内存压力测试。

原设置、仅优先游戏筛选、频道选择、详情中的原领取按钮、账号维护及自动领取安全策略保留。没有新增子页面；详情复用原模板。SOOP 增加每账号阶段/同步说明，Bilibili 活跃 Session 与任务完成分开；两平台无可信奖励图时继续正常占位。

---

以下保留前次 UI V2 历史验收，不能作为本轮 REAL E2E 结果。

# 实际 WPF 截图与视觉验收

日期：2026-11-16（Asia/Shanghai）。下列图片全部来自实际 Windows WPF 程序，没有 HTML 或概念效果图。业务账号、授权码、二维码、分钟数和物品图为显式隔离模拟。测试物品由 ReviewImageAssets 绘制并标注“非官方物品”，只用于布局验收。真实官方当前奖励图片端到端仍为 BLOCKED。

`screenshots/01.png` 至 `44.png` 使用运行窗口的 RenderTargetBitmap 捕获内容区（96 DPI 输出栅格）；`native-*.png` 使用 Computer Use 捕获带系统标题栏的实际窗口。两种证据不混称物理分辨率 / DPI 测试。

## 页面索引

| 场景 | 实际截图 | 主代理视觉复核 |
|---|---|---|
| 01 总览 | [打开](screenshots/01.png) | 已查看；隔离数据 |
| 02 Battle.net 账号管理 | [打开](screenshots/02.png) | 已查看；隔离数据 |
| 03 区服切换 | [打开](screenshots/03.png) | 已查看；隔离数据 |
| 04 战绩搜索与空态 | [打开](screenshots/04.png) | 已查看；隔离数据 |
| 05 区服快照 | [打开](screenshots/05.png) | 已查看；隔离数据 |
| 06 Twitch 未登录 | [打开](screenshots/06.png) | 已查看；隔离数据 |
| 07 Twitch 设备授权 | [打开](screenshots/07.png) | 已查看；隔离数据 |
| 08 Twitch 已登录 | [打开](screenshots/08.png) | 已查看；隔离数据 |
| 09 Twitch 正在观看 | [打开](screenshots/09.png) | 已查看；隔离数据 |
| 10 Twitch 基础偏好 | [打开](screenshots/10.png) | 已查看；隔离数据 |
| 11 SOOP 多账号管理 | [打开](screenshots/11.png) | 已查看；隔离数据 |
| 12 SOOP 观看设置 | [打开](screenshots/12.png) | 已查看；隔离数据 |
| 13 Bilibili 扫码 | [打开](screenshots/13.png) | 已查看；隔离数据 |
| 14 Bilibili 观看设置 | [打开](screenshots/14.png) | 已查看；隔离数据 |
| 15 YouTube | [打开](screenshots/15.png) | 已查看；隔离数据 |
| 16 诊断中心 | [打开](screenshots/16.png) | 已查看；隔离数据 |
| 17 全局设置 | [打开](screenshots/17.png) | 已查看；隔离数据 |
| 18 Twitch 配置损坏 | [打开](screenshots/18.png) | 已查看；隔离数据 |
| 19 Twitch 网络失败 | [打开](screenshots/19.png) | 已查看；隔离数据 |
| 20 SOOP 无账号 | [打开](screenshots/20.png) | 已查看；隔离数据 |
| 21 SOOP 单账号停止 | [打开](screenshots/21.png) | 已查看；隔离数据 |
| 22 Bilibili 二维码过期 | [打开](screenshots/22.png) | 已查看；隔离数据 |
| 23 Bilibili 已登录 | [打开](screenshots/23.png) | 已查看；隔离数据 |
| 24 YouTube 浏览器不可用 | [打开](screenshots/24.png) | 已查看；隔离数据 |
| 25 Worker 崩溃 | [打开](screenshots/25.png) | 已查看；隔离数据 |
| 26 代理不可用 | [打开](screenshots/26.png) | 已查看；隔离数据 |
| 27 账号切换失败 | [打开](screenshots/27.png) | 已查看；隔离数据 |
| 28 区服切换失败 | [打开](screenshots/28.png) | 已查看；隔离数据 |
| 29 Twitch 活动与奖励 | [打开](screenshots/29.png) | 已查看；隔离数据 |
| 30 Twitch 账号维护 | [打开](screenshots/30.png) | 已查看；隔离数据 |
| 31 Bilibili 活动任务 | [打开](screenshots/31.png) | 已查看；隔离数据 |
| 32 Twitch 网络与维护 | [打开](screenshots/32.png) | 已查看；隔离数据 |
| 33 运行日志 | [打开](screenshots/33.png) | 已查看；隔离数据 |
| 34 Twitch 游戏优先级 | [打开](screenshots/34.png) | 已查看；隔离数据 |
| 35 Twitch 自动化 | [打开](screenshots/35.png) | 已查看；隔离数据 |
| 36 SOOP 刷新与恢复 | [打开](screenshots/36.png) | 已查看；隔离数据 |
| 37 Bilibili 自动化与通知 | [打开](screenshots/37.png) | 已查看；隔离数据 |
| 38 YouTube 浏览器配置 | [打开](screenshots/38.png) | 已查看；隔离数据 |
| 39 全局网络代理 | [打开](screenshots/39.png) | 已查看；隔离数据 |
| 40 账号备份与存储 | [打开](screenshots/40.png) | 已查看；隔离数据 |
| 41 关于与来源 | [打开](screenshots/41.png) | 已查看；隔离数据 |
| 42 SOOP 观看仪表盘 | [打开](screenshots/42.png) | 已查看；隔离数据 |
| 43 Bilibili 任务仪表盘 | [打开](screenshots/43.png) | 已查看；隔离数据 |
| 44 Twitch 奖励详情 | [打开](screenshots/44.png) | 已查看；隔离数据 |

[深色](screenshots/dark.png) · [最小窗口](screenshots/minimum.png) · [最大化](screenshots/maximized.png)

## Computer Use 实际复核

重新查询窗口句柄后，仅操作标题为“CloudLight Blizzard · 隔离界面验收（模拟数据）”的实例。复核当前奖励、多 Benefit、队列滚动、奖励详情、总览、SOOP 多账号、Bilibili 任务、账号卡片、全局设置与深色；所有实例正常关闭。未操作其他用户窗口、真实授权、Claim 或游戏文件。

[观看主卡](screenshots/native-twitch-watch.png) · [奖励队列](screenshots/native-twitch-queue.png) · [总览](screenshots/native-overview.png) · [账号](screenshots/native-accounts.png) · [SOOP](screenshots/native-soop-accounts.png)

## 对照参考图的实际调整

参考 b8b5dec4 / e5a65ccd：保留品牌侧栏、浅灰底、蓝紫主操作、平台图标、双列信息层级和卡片边界。将旧纯文本观看页替换为奖励画廊 + 官方大数字 + 频道/封面 + 队列；总览直接绑定各平台 FeaturedReward。参考 907d156b / 66bb7ae4：登录引导与网络准备分列，设备授权独立呈现；高级 Cookie、代理和危险操作有独立入口。图片中的虚假统计、暂停等未支持功能不照搬。

视觉迭代发现并修复：多 Benefit 被窄列挤成纵向；官方进度低于首屏；总览透明黑底；SOOP 总览没有账号任务；Bilibili 模拟状态不符合 account.loggedIn DTO；设备授权与未登录引导同时出现；小宽度 Switch 被裁切；设置只有少量控件却占多张通栏卡；放大小 ico 导致品牌模糊。最终均重新截图核查。

## 适配范围

44 状态 × 12 个等效 DIP 组合 = 528 布局检查，零横向控件溢出和零 Binding 错误。尺寸涵盖 1600×960、1366×768、1920×1080、2560×1440 的 100/125/150% 等效布局；实际窗口尺寸逐行记录。最小 860×540 DIP；最大化 2062.4×1246.4 DIP。当前显示器实际 120 DPI（125%）。未改动 Windows 缩放；100/150% 物理 DPI 未执行，不能由等效测试代替。1366×768/150% 请求高度低于 MinHeight 时，实际按 540 DIP 测试并如实记录。

本报告为主代理隔离验收记录，最终审美判断由用户审阅；真实官方图片未通过的项目不记为 PASS。
