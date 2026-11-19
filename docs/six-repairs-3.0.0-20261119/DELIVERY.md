# 3.0.0 六项修复最终交付

构建源码提交：`84d2a9740ebfed4c68e20032f6bf3bf245c615ec`。分支：`ui/fluent-v2`。后续交付记录提交仅补充文档和检查结果，安装器身份仍对应此构建提交。

六项根因、修复、专项/完整回归、81 张实际 WPF 截图及未测边界见 [验收报告](README.md)。最终安装候选没有覆盖既有候选。

## 安装候选

- 状态：**LOCAL-ONLY**；版本保持 **3.0.0**。
- 文件：`D:\code\CloudLight Blizzard\CloudLight Blizzard\release\candidates\3.0.0-six-repairs-local-20261119-84d2a97\installer\CloudLight-Blizzard-3.0.0-win-x64-Setup.exe`
- 大小：`152427549` 字节。
- SHA256：`710c79b14dbd9a46b79a5d0abb7227f17d8576223efd2d8b222d05bed0349071`
- 源码清单 SHA256：`c96e81fe3b9989085b74cec14cb8b8b6fbf740b4636fbac758a3480fb032f006`。
- 候选目录的 `candidate.json`、`SHA256SUMS.txt` 及 `manifests/` 记录源码和全部四个冻结 Worker 的身份。

## 最终二进制验证

Freeze / Publish / Validate / Package / Extract 全部通过。四个 Worker 从干净源码提交重建，冻结 SOOP 82、Twitch 121 用例通过，网络隔离未发送真实领取或观看。

发布载荷的 FeatureSelfTest all、Battle.net 账号安全、区服安全、更新安全、退出安全、Twitch audit、真实 Worker JSONL/重启、SOOP 超时和 UID 恢复全部通过。发布载荷 [WPF 组件 203 PASS / 0 FAIL](checks/packaged-ui-v2-selftest.txt)；源码组件套件另有 206 PASS / 0 FAIL，其中三项官方目录夹具刷新身份、回收及滚动位置检查需要源码夹具，未包含于发布载荷。页面矩阵通过，Binding Error 为 0。

安装器提取的 **565 个文件**逐文件哈希与载荷清单相同。隔离设置保留测试通过。两种安装器验证模式在系统安装前主动终止，预期退出码为 1；未执行系统安装。

最终解包程序集经 dotnet 默认启动方式运行的 UI/Host 自检：[346 PASS / 0 FAIL / 1 BLOCKED](checks/extracted-ui-host-selftest.txt)，退出码 2。同一解包 EXE 两次隐藏启动均遇到 Windows 剪贴板占用（HRESULT 0x800401D0），返回 BLOCKED、346 PASS / 0 FAIL；[首次记录](checks/extracted-ui-host-first-run.txt)及[新目录复测](checks/extracted-ui-host-hidden-retry.txt)均保留。未修改剪贴板逻辑、测试断言、系统剪贴板策略或其他程序。包内剪贴板真实读写验收仍为 BLOCKED；源码程序先前的 348 PASS 结果不替代这一限制。

机器可读摘要：[final-candidate-validation.json](checks/final-candidate-validation.json)。仓库中的报告仅规整行尾空白，不改变结果。完整原始构建/安装器日志保留在候选目录 `validation/`，没有作为公开源码上传。

## Core 来源及授权边界

SOOP Core Git 基线为 `6050b8a35690b091996a1efedad4655b1b094cac`，24 个文件聚合 SHA256 为 `65801b6e10f1777cd08af4ae0c6903003b2544c6bfd58b60f7295ba77d879be6`，与此前已验证/交付字节基线一致。该基线本来含兼容性修改及行尾差异；独立构建目录如实记录 **dirty=true**，未将其伪装成干净上游，也未放宽摘要或能力门禁。旁边较新的 Core checkout 未被修改。

**BLOCKED**：SOOP Core 公众再分发授权、SOOP 站外品牌书面授权仍未确认。未创建公开 Tag、GitHub Release，未上传安装器或受限 Core。品牌素材来源、原始哈希和限制见仓库声明。

**NOT TESTED**：真实账号长期观看和官方分钟实际增长、真实领取、真实反馈提交、系统安装、公开自动更新 3.0.0 升级、物理显示器 DPI 切换。状态截图明确标记 SIMULATED；历史官方数据标记 OFFICIAL-REPLAY。

Git 推送目标为 `https://github.com/Cloud-Light125/CloudLight-Blizzard` 的 `ui/fluent-v2`；最终推送结果与远端 HEAD 在交付回复中给出。没有改写远端历史。

权限为 danger-full-access / never，与可读全局权限一致。可读全局模型配置为 gpt-6.2-sol/high，实际后端型号及推理强度未独立暴露，无法确认模型级覆盖。全程主代理完成，未使用子代理，未修改模型、权限、账号认证或系统代理。
