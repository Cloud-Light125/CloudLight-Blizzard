# CloudLight Blizzard 1.0

CloudLight Blizzard 的首个正式版本。

本次 1.0 正式包已更新应用图标，修正已安装 .NET 8 Windows Desktop Runtime 时安装器仍可能误报的问题，让窗口标题栏正确跟随深色主题，兼容少数启动器遗漏 `WINDIR` 环境变量的情况，并将启动及区服页面的磁盘状态读取移到首帧渲染后的后台任务，避免界面卡顿。

## 主要功能

- Battle.net 多账号本地管理与快速切换
- 账号自定义名称、备注和国服 / 国际服绑定
- 守望先锋国服 / 国际服本地文件切换
- 使用本地文件减少跨区时 Battle.net 的重复下载
- 账号切换自动联动对应的守望先锋区服
- 国服战绩与国际服生涯数据查询
- 明暗主题
- 托盘与开机启动

## 安装

下载 `CloudLight-Blizzard-1.0.0-win-x64-Setup.exe`，运行安装程序即可。

需要 Windows 10/11 x64 和 .NET 8 Windows Desktop Runtime。

## 数据

账号备份、设置和日志默认保存在：

`文档\CloudLight\CloudLight Blizzard`

守望先锋区服文件可以单独选择其它大容量磁盘。

升级或卸载程序不会主动删除这些用户数据。
