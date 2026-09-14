# CloudLight Blizzard 2.1.4

- 修复 SOOP 已完成掉宝任务仍被自动任务候选和任务选择集合使用的问题。
- 修复 SOOP Worker 动态加载 Core 时遗漏 `zoneinfo`，避免打包安装版启动时报 `No module named 'zoneinfo'`。
- 增加打包后 SOOP Core 导入自检，防止同类问题进入发布包。
