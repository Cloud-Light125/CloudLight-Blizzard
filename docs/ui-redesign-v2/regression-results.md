# 第二代回归结果

源码阶段：Release build 0 警告 / 0 错误。FeatureSelfTest 13 个测试组 OVERALL PASS（账号切换顺序、持久化、恢复、安全退出、快照、诊断等；使用隔离文件）。UI/Host 347 条断言通过、0 FAIL、0 BLOCKED。不是对 763 条基线逐条执行真实账号操作的声明。

图片 / 投影 / 实际 WPF 控件 24 PASS、0 FAIL、1 BLOCKED。包括全部 Benefit、当前奖励 ID 关联、官方与估算分离、UID 隔离、代理范围、7 天缓存、并发合并、失效 / 格式 / 超时 / 取消 / 快速切换、卸载和 48 DIP 开启态 Switch 不裁切。

Twitch Worker 已先完成一次独立冻结：84 个 Python 契约、SSL、打包来源与 GQL/playlist-watch 兼容自检 PASS。未重写 Core；SOOP、Bilibili、YouTube 继续复用已验证二进制。最终候选脚本会在提交后只重新构建 Twitch 并记录每个 Worker 原始来源和 SHA256。

528 布局组合、最小 / 最大化 / 深色、零 Binding 错误、草稿在平台切换与刷新后保留、实际保存 Click 到原 save_settings、取消授权阻止后台续发登录全部通过。真实运行截图见 visual-acceptance.md。

发现的实际功能缺陷已修复：拆分后的 Bilibili Room 模板无法从旧 ElementName=DropsRoot 获得启用 / 删除命令，改为当前组件 DataContext 的 RelativeSource；原 TwoWay 和 CommandParameter 保留。原功能映射审查补回帮助、二维码重取、退出和恢复入口。固定列数的旧布局测试改成验证新导航 / 自适应结构，未删除实际命令与业务保护断言。

证据：test-output/ui-v2-feature-final/feature-selftest.txt；ui-v2-ui-delivery.txt；ui-v2-images-final/ui-v2-selftest.txt；ui-v2-final-checked/matrix-results.txt。早期失败记录保留供追踪，不当作最终结果。

最终候选由 build-support/Build-UiV2Candidate.ps1 生成；该脚本还会对发布目录重新运行 Feature、UI/Host、图片和真实 WPF 布局验收。精确结果、最终提交和二进制散列以候选 candidate.json / validation 为准。

BLOCKED：缺少已经授权 Core 的脱敏 Campaign / Benefit 当前快照，真实官方奖励图片端到端未验收。所有模拟素材均不解除该项。SOOP 和 Bilibili 现有已验证模型没有奖励图片字段，显示明确占位；没有臆造 API。真实登录、领取、账号切换和游戏覆盖不在 UI 故障注入范围内。YouTube 不做新账号登录。
