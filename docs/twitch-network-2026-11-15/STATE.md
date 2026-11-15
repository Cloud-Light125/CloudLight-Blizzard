# Twitch 网络故障记录

- 基线/远端ffc98e42f43766566fdf45db90c8c140cb2df3d3，已fetch/pull无更新；原artifacts/保留。
- 安装BuildCommit ffc98e4，Worker4b97e90c…052ec0；实际主17876，Worker15740/2328，真实目录D:/文档/CloudLight/CloudLight Blizzard/drops/twitch。配置已有效，claim=false，quality1、优先列表空，不再归咎配置损坏。
- 实际全局EnableProxy/TwitchUseProxy/FallbackDirect=true，127.0.0.1:7897；14:29/14:33/14:35日志代理→直连失败，14:37:57官方validate200恢复，历史日志没有具体异常/域名阶段，不能倒推确定原始TCP/TLS根因。
- 设置诊断测试www.twitch.tv，不等于id validate与GQL。当前同环境curl/aiohttp代理id401/gql400/www200、TLS验证通过；直连三域名都连接超时，证据test-output/twitch-network-2026-11-15/network-baseline.json。
- 确认代码缺陷待验证：单次fallback后剩余尝试一直直连；任意POST可重放；HTTP407也回退；consumer异常可能被request捕捉而重发；Host并发旧proxy更新无版本协调；WPF失败事件仍显示Connecting并可能被checking状态覆盖。
- 本轮只Twitch网络/必要Host/WPF；原Session/配置只读，不修改系统代理、不重启代理、不强停用户程序、不Claim、无子代理。实际danger-full-access/never未改变。
- 正在实现受控路由诊断/安全重放与Host时序门禁；后续定向测试、冻结真实Session、最终构建/提交push/窗口验收。原始临时Session需在结束后仅清理本轮创建副本，不能删除用户数据或以前任务数据。
