# 验收矩阵

静态契约：由 build-support/UiMigrationAudit.py 从基线生成逐行映射。运行验证单独报告，不能以静态匹配冒充点击成功。

待执行：Release 构建；FeatureSelfTest（含 Host、Dispatcher、持久化）；UI visual tree；隔离前端状态与真实窗口；100/125/150% 等效布局与 1366×768、1920×1080、2560×1440；深浅色；启动退出；本地候选 smoke。

禁止真实账号切换、奖励领取、游戏文件覆盖、YouTube 登录。需要这些外部状态的行为仅核对入口、参数和既有保护，报告中明确限制。
