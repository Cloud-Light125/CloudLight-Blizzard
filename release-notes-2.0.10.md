# CloudLight Blizzard 2.0.10

## 在线更新修复

- 修复在线更新下载达到 100% 后界面停留在“正在下载”的问题。
- 下载完成后先关闭文件流，再校验安装包并进入启动流程。
- 增加“正在校验安装包…”和“正在启动安装程序…”状态提示。
- 修复必须手动关闭 CloudLight Blizzard 后安装程序才会启动的问题。
- 修正 CancellationTokenSource 的 Cancel / Dispose 生命周期，避免关闭窗口与异步 continuation 竞态。
- 修复在线更新完成后可能出现的 `The CancellationTokenSource has been disposed.` 错误。
- 安装程序成功启动后才退出 CloudLight Blizzard；如果启动失败，程序继续运行并允许用户重试。
- 安装程序启动成功后显示“安装程序已启动，正在退出 CloudLight Blizzard…”。
- 保持安装包声明大小与 Windows `MZ` 文件头校验，并在发布验收中记录唯一安装包的 SHA-256。
- 在线更新继续使用 CloudLight Blizzard 当前代理配置。

**完整变更**：https://github.com/Cloud-Light125/CloudLight-Blizzard/compare/v2.0.9...v2.0.10
