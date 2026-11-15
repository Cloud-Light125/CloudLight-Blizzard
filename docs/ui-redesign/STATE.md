# 持续任务记录

- [x] 读取全部四张参考图片。
- [x] 检查 Shell：PowerShell 7.6.9，bundled pwsh.exe。
- [x] Git fetch（命令级代理 127.0.0.1:7897），本地与远端 HEAD 一致。
- [x] 建立独立 ui/fluent-redesign 分支；未跟踪 artifacts/ 保留。
- [x] 读取 computer-use 技能、guidance、api、confirmations。
- [x] 旧 UI 盘点：763 条 UI 契约（含拖动、鼠标事件），53 个 AppSettings 字段；静态迁移 763/763。
- [x] 共享设计系统、九项一级导航、真实服务数据总览。
- [x] SOOP / Twitch / Bilibili / YouTube 分组；Twitch 授权流程、固定保存区与草稿保留。
- [x] 账号、区服、战绩、快照、诊断、设置、关于及共享窗口样式。
- [x] computer-use 实际打开并人工复核 17 类页面，修复授权重复主按钮、未登录观看入口、空快照布局等。
- [x] 首轮 35 状态、315 等效 DIP 布局，0 binding error；真实 DPI 为 120（125%）。
- [x] 首轮 FeatureSelfTest PASS；UI/Host 345 PASS、0 FAIL、1 BLOCKED（外部剪贴板占用）。
- [x] 最终来源一致候选：c6ad4cf，40 状态、360 等效 DIP 布局、Feature / Host / Dispatcher / 持久化 / 启动退出复验；Host 仍有剪贴板 BLOCKED。
- [x] 最终报告、测试矩阵、视觉记录与运行计数口径写入 docs/ui-redesign/。
- [x] 分支交付资料已归档；最终文档提交 SHA、安全推送及远端核对结果以本地候选 delivery-receipt.json 和最终交付回复为准。

最终候选：test-output/ui-redesign-release-candidate；不要使用先前候选替代。程序源码 c6ad4cfc5b8dfdf15b9faa2ec179de95178b8460，Feature PASS，UI/Host 345 PASS / 0 FAIL / 1 BLOCKED，Presentation PASS，binding errors 0。四个 Worker 哈希与已验证来源完全相同。

Computer Use 17 类页面与深色共 18 张历史实际截图保留；收尾工具误识别测试窗口，最终整套桌面重拍未完成。最终 40 状态 WPF 渲染单独保存，已复核关键改动；不把两类证据混称。

打包核查发现默认 artifacts/drops 是旧 bffdb7d 清单，不能用作本次最终来源。已定位经过验证的 release/candidates/2.1.10-813e6deda6de-Twitch-Network-Local；Twitch 为 813e6de，另三个 Worker 明确继承 e32ceb0。Build-UiCandidate.ps1 将验证源差异与四个 SHA256，不重新构建 Core，也不复制任何真实 Session 验收目录。

已知验收边界：100%/150% 未物理切换显示器 DPI，仅做等效 DIP；系统剪贴板占用持续存在；当前 Twitch Worker inventory DTO 不提供奖励图片 URL，本轮未修改 Worker 协议，不伪造奖励图片。不能将这些边界称为全项无阻通过。

全局配置可见值：gpt-6.1-sol / xhigh；运行时精确模型/推理档位未由环境单独暴露。当前权限 danger-full-access，approval=never，与全局配置一致。multi_agent=false，multi_agent_v2=false；遵从用户禁止子代理规则。

环境发现：dotnet --info 在已安装 SDK 10.0.302 的 workload MSI 初始化出错；9.0.320 同时已安装。构建前使用进程级 MSBuildEnableWorkloadResolver=false 验证，不修改全局 SDK 或系统配置。
