# CloudLight Blizzard 2.1.8

- 修复更新后代理和常规设置可能被默认值覆盖的问题。
- 设置保存改为落盘刷新、原子替换并保留上一份有效备份；主文件异常时自动从备份恢复。
- 如果现有设置无法安全读取，程序不会再用默认设置覆盖原文件。
- 在线更新会先确认当前设置已成功写入磁盘，保存失败时不会启动安装程序。
- 启动流程只加载一次设置，并在应用、主窗口和各服务间共享同一个设置实例。
- SOOP 掉宝按活动要求和当前账号关联状态分别显示“已绑定”“未绑定”“不支持绑定”或“状态待确认”，不再把字段缺失误报为“无需绑定”。
- SOOP 手动与自动领取都会跳过明确要求绑定但账号尚未绑定的奖励；明确不支持绑定的奖励继续按原流程领取。
- 同步 Twitch Drops 最新 Inventory 与 Campaigns persisted query，恢复与当前 Twitch GQL 接口的兼容性。
- 同步 Twitch Web、Mobile Web 与 Smartbox 客户端标识和 User-Agent。
- 将 Twitch GQL 返回的 `request cancelled` 作为临时错误按现有退避策略重试，避免偶发请求取消直接终止 Worker。
- 增强 `PersistedQueryNotFound` 诊断；遇到已失效查询时明确提示更新 Twitch core，且不会无限重试。
- 增加打包后 Twitch GQL/Client 兼容自检，确保安装包内实际使用新的查询哈希和客户端信息。

> 本版本保留 CloudLight 的无自动领取、代理、登录会话、Headless Worker 与 WPF 集成逻辑，不修改 SOOP、YouTube 或哔哩哔哩掉宝逻辑。
