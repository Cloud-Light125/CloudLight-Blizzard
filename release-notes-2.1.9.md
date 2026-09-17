# CloudLight Blizzard 2.1.9

- 修复手动运行安装包更新时，旧进程退出阶段可能导致主设置恢复为默认值的问题。
- 安装器现在会在关闭旧程序之前备份主设置以及 SOOP、Twitch、YouTube、Bilibili 设置，并在安装完成后原样恢复。
- 安装前快照保留在用户数据目录的 `update-backups` 文件夹中；备份失败时安装器会中止安装，避免继续更新。
