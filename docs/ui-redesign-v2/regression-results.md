# 最新回归：优先概览、紧凑卡片与观看状态（2026-11-17）

见 [本轮完整验收报告](priority-status-acceptance.md) 与 priority-evidence/：最终 Release 候选全部验证通过，Twitch Python 87、SOOP 43、Bilibili 21；投影/排序/状态/图片组件 95；UI/Host 347；Feature 13 组；WPF 528 布局；独立官方快照回放 145 检查、24 个四尺寸/主题/页面组合，0 Binding 错误。静态迁移映射 763/763。历史回放与模拟请求均明确标识，本轮没有进行真实账号业务操作。

---

# 上一轮回归（2026-11-17）

- Python 按平台隔离进程运行，避免同名 worker 模块互相污染：Twitch+Shared 87 PASS（13 subtests），SOOP 43 PASS（6 subtests），Bilibili 21 PASS（2 subtests），YouTube 8 PASS。最初错误地跨平台同进程 pytest 收集失败，随后按平台正常执行，未修改业务代码绕过失败。
- UI 静态契约继续 763/763，0 待核对。这是契约映射，非 763 项真实账号操作。密集背包替代绑定有明确映射，原 Command、事件、设置和 Bilibili Room 命令作用域保留。
- UI/Host 347 PASS，0 FAIL，最终包含实际剪贴板读写及零 Binding 错误。早期一次剪贴板被其他程序占用为 BLOCKED，最终重跑解除占用后通过，未修改保护。
- 图片/缓存/实际 WPF/状态组件 50 PASS，0 FAIL。新增精确 Campaign+Drop 关联、Benefit 共享进度、已完成不当当前、空筛选、近期频道请求、停止/异常退出、官方值过期保留、Bilibili 任务与 Session 状态、192px 缩略图复用、离屏延迟加载、脱敏记录。真实账号图片已移到独立 REAL E2E 报告，组件套件明确 NOT TESTED real account，未把模拟项冒充真实通过。
- FeatureSelfTest 13 组 OVERALL PASS。
- 全页面 WPF 44 场景 × 12 等效窗口组合 = 528，零横向交互控件溢出、零 Binding 错误；草稿、原保存命令、授权取消、最小/最大化、深色继续通过。
- 聚焦密度 8 个窗口/页面组合通过，1366 首行 4 完整卡、1600 为 5、1920 为 6；未完成、完成待领取、已领取、前置/资格与无频道均有实际 WPF 模拟截图。早期首屏未完整显示的失败已保留在 test-output，最终修复后重新通过。
- Release 编译/发布成功，无编译警告或错误。实际候选启动、正常停止和退出已验；最终发布目录由 Build-UiV2InventoryCandidate.ps1 从提交重建，应用 BuildCommit、散列和各自验证记录见该目录 candidate.json / validation。
- Worker 源码无改动，全部复用已验证二进制；Twitch 来源 25fc6bbb，SHA256 bc4816a3cc7edf4088236f9786af4822eb0dc8fde317e294c145e8c975313ac2。SOOP/Bilibili/YouTube 保留各自来源清单，不把主程序新提交写成旧 Worker 的构建提交。

本地证据：test-output/real-twitch-20261116/checks/；full-layout-delivery/；density-final-source/；private-real-data/evidence/（包含私有 Session 的同级目录，禁止加入 Git）。可提交的精简证据：real-evidence-summary.json、density-results.txt 及隔离界面截图。

REAL E2E：认证、官方活动/背包、官方奖励图/封面、多 Benefit、官方分钟 0→33、重开后35→50、观看请求/频道、停止通过。真实领取、SOOP/Bilibili 真实登录、YouTube 真实播放和真实故障注入 NOT TESTED；100/150% 物理 DPI NOT TESTED。未运行长期内存压力测试。真实证据和边界见 real-twitch-e2e.md。

权限：主代理独立实施；可见用户配置 gpt-6.1-sol/xhigh，任务精确模型与推理档位未单独暴露。实际 danger-full-access/never，与可见全局一致；multi_agent=false。未更改这些设置、系统代理、Git 全局代理或系统 DPI。

---

以下为前次交付记录。

# 第二代回归结果

源码阶段：Release build 0 警告 / 0 错误。FeatureSelfTest 13 个测试组 OVERALL PASS（账号切换顺序、持久化、恢复、安全退出、快照、诊断等；使用隔离文件）。UI/Host 347 条断言通过、0 FAIL、0 BLOCKED。不是对 763 条基线逐条执行真实账号操作的声明。

图片 / 投影 / 实际 WPF 控件 24 PASS、0 FAIL、1 BLOCKED。包括全部 Benefit、当前奖励 ID 关联、官方与估算分离、UID 隔离、代理范围、7 天缓存、并发合并、失效 / 格式 / 超时 / 取消 / 快速切换、卸载和 48 DIP 开启态 Switch 不裁切。

Twitch Worker 已先完成一次独立冻结：84 个 Python 契约、SSL、打包来源与 GQL/playlist-watch 兼容自检 PASS。未重写 Core；SOOP、Bilibili、YouTube 继续复用已验证二进制。最终候选脚本会在提交后只重新构建 Twitch 并记录每个 Worker 原始来源和 SHA256。

528 布局组合、最小 / 最大化 / 深色、零 Binding 错误、草稿在平台切换与刷新后保留、实际保存 Click 到原 save_settings、取消授权阻止后台续发登录全部通过。真实运行截图见 visual-acceptance.md。

发现的实际功能缺陷已修复：拆分后的 Bilibili Room 模板无法从旧 ElementName=DropsRoot 获得启用 / 删除命令，改为当前组件 DataContext 的 RelativeSource；原 TwoWay 和 CommandParameter 保留。原功能映射审查补回帮助、二维码重取、退出和恢复入口。固定列数的旧布局测试改成验证新导航 / 自适应结构，未删除实际命令与业务保护断言。

证据：test-output/ui-v2-feature-final/feature-selftest.txt；ui-v2-ui-delivery.txt；ui-v2-images-final/ui-v2-selftest.txt；ui-v2-final-checked/matrix-results.txt。早期失败记录保留供追踪，不当作最终结果。

最终候选由 build-support/Build-UiV2Candidate.ps1 生成；该脚本还会对发布目录重新运行 Feature、UI/Host、图片和真实 WPF 布局验收。精确结果、最终提交和二进制散列以候选 candidate.json / validation 为准。

BLOCKED：缺少已经授权 Core 的脱敏 Campaign / Benefit 当前快照，真实官方奖励图片端到端未验收。所有模拟素材均不解除该项。SOOP 和 Bilibili 现有已验证模型没有奖励图片字段，显示明确占位；没有臆造 API。真实登录、领取、账号切换和游戏覆盖不在 UI 故障注入范围内。YouTube 不做新账号登录。
