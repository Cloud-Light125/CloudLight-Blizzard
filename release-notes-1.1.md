# CloudLight Blizzard 1.1

CloudLight Blizzard 1.1 改进守望先锋国服 / 国际服当前状态识别，并修复托盘启动时主窗口短暂闪现的问题。

## 更新内容

- 当前区服识别不再要求运行中的游戏目录与准备时快照 100% 完全一致。
- 新增 Active Generation 对应的最后成功区服状态，正常 Battle.net 文件漂移后仍保持正确区服显示。
- 使用当前 Generation 的 ChinaOnly / InternationalOnly 动态证据识别区服；少量对侧残留不会误判为混杂。
- 游戏版本更新、明显双侧冲突和大范围文件损坏仍会被正常发现。
- 区服切换成功后立即更新当前区服；失败、取消或异常不会改写最后成功状态。
- `region-switch.log` 新增强证据计数、Difference 漂移和目标快照诊断信息。
- `--tray` 和“启动时最小化”现在直接静默进入托盘，不再先显示主窗口再隐藏。

## 安装

下载 `CloudLight-Blizzard-1.1.0-win-x64-Setup.exe` 后运行即可。

需要 Windows 10/11 x64 和 .NET 8 Windows Desktop Runtime x64。

现有账号数据、Active Generation 和国服 / 国际服本地备份会继续保留，无需重新准备区服文件。
