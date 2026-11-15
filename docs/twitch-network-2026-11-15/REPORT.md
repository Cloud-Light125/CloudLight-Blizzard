# Twitch 代理、Session验证与网络状态修复检查点

基线ffc98e42f43766566fdf45db90c8c140cb2df3d3；已fetch/pull确认远端。安装BuildCommit及Worker SHA256与该基线一致，实际目录D:/文档/CloudLight/CloudLight Blizzard/drops/twitch。真实主程序17876，PyInstaller父子15740/2328为一组Worker；没有强停、清Session或改系统代理。当前配置已有效，claim=false，不能再归咎NUL配置。

## 根因与证据

1. 确认的核心缺陷：旧request使用ExponentialBackoff(maximum=3)，maximum表示秒数上限，不是次数。真实退避类运行八次时延迟约1、2、3、3…，steps仍为2；因此旧steps>=3退出条件永不成立。首次代理失败后kwargs.proxy被删除，后续无限留在直连。这使代理恢复后当前请求仍无法回到代理，WPF持续显示checking/connecting。已用真实退避类的定向回归验证新实现严格三次。
2. 配置链：实际EnableProxy/TwitchUseProxy/FallbackDirect均true，127.0.0.1:7897；Main.CreateDropsProxySettings与ForWorker正确考虑独立开关。Host原hello→set_proxy→Ready顺序本身正确，不声称初始必然抢跑。另确认并发ApplyProxyAsync使用各调用捕获的旧settings，缺少更新协调；新实现仅Twitch增加最新策略门禁和合并，业务请求前确认策略、启动/重启/快速变更一致。
3. 设置页仅.NET HttpClient访问www.twitch.tv网页。它不验证id.twitch.tv的Session或gql.twitch.tv业务，且与Worker不同运行时、超时；两者不是同一验收目标。页面和复制诊断现已明确标注此范围。
4. 真实历史日志14:29/14:33/14:35记录代理失败后直连失败；14:37:57记录官方validate200。Saved Session恢复执行路径的首个HTTPS请求为id.twitch.tv/oauth2/validate。旧日志没有首个失败的底层异常，不能捏造其DNS/TCP/CONNECT/TLS细分或代理服务故障原因。当前源/冻结受控复现补齐逐尝试证据；初次底层失败的历史原因仍未证实。
5. 同环境对照：系统curl和Python3.13.9/aiohttp3.14.3/OpenSSL3.5.6通过同一代理访问validate401、GQL GET400、www200，TLS校验成功；三域名直连均ConnectionTimeout（约10秒）。无Token的401/400只证明接口到达，不能认定登录成功。
6. 预提交实际冻结探针也遇到GQL连接失败约5004ms，直连ConnectionTimeout约10506ms，随后重新走Proxy成功；说明新路由能从实际瞬时连接错误恢复。该初版记录ClientConnectorError为TCP过粗，最终实现细化为有可靠errno才称TCP，否则EndpointConnectOrTLS，并记录cause类型/errno；不将初版推断冒充确定TLS或TCP原因。

## 实现

- core/network_policy.py与core/twitch.py：每个独立请求最多3次；安全请求Proxy→DirectFallback→Proxy，关闭回退则始终Proxy；返回HTTP401/403/429不当作代理故障；5xx只读有界同路由重试。TLS错误不降级；407不绕过代理认证。不修改共享kwargs或全局路由。响应预读后，在重试捕获之外yield，消费阶段JSON/OAuth异常不能触发请求重放；响应始终释放。
- 只读GQL使用实际operationName白名单。OAuth POST、Claim及未知/变更操作最多发送一次；其HTTP/业务错误和不确定结果不会被外层OAuth/GQL重试再次提交。现有Device Code流程保留，正常authorization_pending轮询保留。
- core/oauth.py：明确官方validate401/status401/message invalid access token才认定失效；无法确认的401保留Session并分类为响应/网络问题。网络超时、TLS、5xx不清Cookie。
- worker.py/headless_gui.py：显式trust_env=False；安全proxy_policy/network_request诊断与快照；相同代理幂等；代理改变会结束旧连接（包括正在登录的连接），按原观看/登录意图恢复。只读network_probe用DummyCookieJar，不把探针401当成登录，不改变业务连接状态。
- DropsHostService：仅Twitch按Worker协调最新策略；并发更新合并、Ready前握手和每次业务请求前的策略门禁；stop/shutdown不被代理门禁阻塞。其它平台业务不变。
- DropsViewModel/XAML：网络终态退出等待，Session未验证与认证失效分开；checking快照不能盖住刚发生的终态；成功清除旧错误；诊断事件受Generation/Sequence隔离。展示脱敏域名/安全路径/路由/阶段/HTTP/耗时/尝试。NetworkDiagnosticService明确www网页连通性范围。

## 已执行验证

- Twitch78测试通过（原53，新25），涵盖真实退避上限、路由恢复/关闭回退/独立请求、407/TLS/401/403/429/5xx、响应释放、取消、OAuth POST/Claim及外层GQL变更不重放、脱敏及配置保护。旧测试中OAuth POST自动重试与407绕过代理的断言根据本轮明确安全要求改为GET安全读取/407直接失败，未降低原读取重试、响应清理断言。
- 真实Host+JSONL模拟Worker10项通过：AppSettings→Main→Host三种范围组合，快速两次更新、无效地址、立即启动、重复开始同PID、真实崩溃后正确策略、手动停止不复活、退出无孤立Worker。此处账号与网络为模拟，不冒称真实API通过。
- .NET Release编译0警告错误；WPF五项网络状态检查通过（含checking快照/旧Generation），整套因剪贴板外部占用BLOCKED；最终以候选结果为准。
- 预提交冻结真实Session副本：代理公开三接口可达；官方validate200、113活动/409奖励、inventorySource=remote、启动/停止exit0，原文件摘要不变。此样本不替代最终干净构建和最终官方分钟证据。

## 交付边界与后续

保留settings_store、NoAutoClaim、Chrome156/Android31.8.0、Smartbox、hash、HLS/HEAD、官方分钟与估计分离及手动停止。未修改YouTube/SOOP/Bilibili/Shared业务、未改全局代理/系统/证书、无真实Claim、无子代理。实际权限danger-full-access/never未改变；具体任务模型/推理强度无独立查询接口。

仍需反向审查、提交push、干净最终构建（仅新Twitch与app，三个未改Worker明确继承原提交/hash）、8组冻结网络故障验收、最终Host/WPF、真实Session/官方进度与实际窗口、哈希和最终收据。所有本轮真实Session副本在结束后仅清理本轮创建的凭据；原始用户数据和之前任务数据保留。本地原始证据含隐私，不整目录上传。
