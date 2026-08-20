# CloudLight Blizzard 1.1.1

CloudLight Blizzard 1.1.1 修复守望先锋区服文件在版本无法确认时不能恢复，以及首次准备区服备份时受游戏运行时文件干扰的问题。

## 更新内容

- 新增 `RegionSwitchEligibility`，明确区分正常切换、宽容恢复、备份不可用和游戏已更新。
- 当前游戏版本或当前区服无法确认时，只要 Active Generation 和目标备份完整，仍可恢复到国服或国际服。
- BestEffort 模式只处理当前 Generation 已知的 ChinaOnly、InternationalOnly 和 Different；其它新增、缺失或变化的文件保持原样。
- 已知目标文件缺失时会从本地目标区服备份补齐；目标备份大小或 SHA-256 校验失败时会在修改游戏文件前停止。
- 当前区服为 Unknown 或 Mixed 时可直接归一到目标区服，不再要求先识别出当前区服。
- 账号联动支持 BestEffort；游戏已更新或备份不可用时仍会停止账号切换，避免 Battle.net 重新下载。
- 首次建立 Generation 时排除日志、缓存、临时文件、崩溃转储、shader cache、CASC `ecache` 和 `shmem` 等明确运行时文件。
- 第二阶段失败会显示真实异常原因、记录完整异常，并提供重试和打开日志文件夹；普通失败继续复用 Source Staging。

## 安装

下载 `CloudLight-Blizzard-1.1.1-win-x64-Setup.exe` 后运行即可。

需要 Windows 10/11 x64 和 .NET 8 Windows Desktop Runtime x64。

现有账号数据和可用的 Active Generation 会继续保留；游戏版本确认失败不会再强制重新准备区服文件。
