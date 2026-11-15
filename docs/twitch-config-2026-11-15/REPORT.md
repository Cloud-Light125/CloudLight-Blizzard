# Twitch 业务核心初始化故障修复

本记录为提交前证据检查点。最终提交、干净重建的BuildCommit/Worker SHA256、安装候选和实机窗口结果由最终候选的FINAL-ACCEPTANCE.md补充。仅维护Twitch及必要的WPF状态链，没有修改YouTube/SOOP/Bilibili业务。

## 真实根因

起始本地e32ceb03c855bea82f4b945e56d8125d5f18c30a，代理fetch/pull安全快进至faf2671cb9553f129e5d203bb18b35aeedc5c0e5。已安装主程序仍为e32ceb0；Twitch Worker SHA256为7067461bfb1059eeebb6a1ccc4b2680ded040dc1f80dc69d25de6437cc040a96，不含faf2671 Android UA更新。不是仅凭2.1.10判断来源。

实际运行主程序27464，PyInstaller父子Worker17368/45500（一组，不是两个独立业务实例）；--data-dir为D:/文档/CloudLight/CloudLight Blizzard/drops/twitch，与MyDocuments重定向及AppPaths一致。没有强制退出这些进程。

2026-11-15 11:25:22真实日志调用栈：

```text
worker.py:117 _load_core
core/settings.py:78 Settings.__init__
core/utils.py:259 json_load(SETTINGS_PATH, default_settings, merge=False)
json/__init__.py:293 load
json/__init__.py:359 loads
json/decoder.py:345 decode
json/decoder.py:363 raw_decode
json.decoder.JSONDecodeError: Expecting value: line 1 column 1 (char 0)
```

触发文件为上述真实数据目录的settings.json：600字节全部为NUL，SHA256 bd50e12c55dda3ee443c1cb6d71c7bcf6351c4ec96f7bc8d6adec015d1192eea，最后修改2026-10-29 15:08:09；无.new/.bak/.lkg。缓存mapping.json可解析，运行中的Core包21个语言JSON均可解析，Cookies文件存在2630字节。读取成功，无当前权限/占用错误证据。

原文件已只读复制并逐字节摘要核对到本地取证目录；旧版实际冻结Worker使用副本复现完全相同异常。没有将原始设置或Cookies内容输出、提交。

**无法确认最初损坏来源。** 时间与当前单组Worker不足以证明历史并发、断电、旧迁移或磁盘问题。确认的程序缺陷是：正式文件解析异常未处理；旧保存共用.new、无fsync/写后验证/LKG；旧json_load会删除无效.new。新实现消除Twitch设置路径的这些风险，不把可能性写成既定原因。

## 修复行为与文件

- core/settings_store.py：Twitch专用加载与保存；Missing/Valid/LegacyCompatible/InvalidJson/InvalidSchema/Unreadable/RecoveryAvailable/RecoveryFailed明确分类。UTF8/BOM兼容，拒绝非对象、重复键、非法类型及非有限数；旧字段补全，未知字段保留，未知领取偏好false。
- 有效正式文件优先，不用旧.new覆盖。损坏时依序验证.lkg/.bak/.new，校验原始字节备份后恢复。无有效副本保留原文件并安全失败，不能静默默认或清除Session。损坏.new保留；新唯一.tmp属于未提交证据，不自动晋升。
- 保存完整序列化/结构验证后，在同进程RLock及跨进程OS锁中验证加载版本；独立临时文件、flush/fsync、写后读回、原子replace、替换后校验、前一版本.bak及.lkg。陈旧设置对象拒绝覆盖。拒绝符号链接/重解析点/硬链接/非普通文件。
- core/settings.py：仅Settings接入专用Store，不改utils.json_load的缓存/语言等其它调用者。成功保存清除altered标记。
- worker.py：结构化coreErrorKind/configuration；retry_core在当前Worker重新检测；显式repair_settings要求confirmDefaults=true，只允许损坏且停止的配置；原始备份校验通过后恢复默认、自动领取false。新Worker持有目录生命周期锁，正常退出/事件循环结束释放；保留原有幂等shutdown。全局代理只作运行覆盖，不再清空保存的Core代理偏好。保存参数先整体验证，失败回滚内存设置。
- ViewModels/DropsViewModel.cs、Views/Pages/DropsPage.xaml(.cs)：配置错误显示文件与原因，禁用授权/清除登录/开始/刷新/保存，停止自动重试；“重新检测”只检查Core，不启动OAuth。显式备份恢复默认入口说明需要重新确认的选项，Session保留；修复成功解除门禁。无有效默认值时界面自动领取不勾选。
- Services/Drops/DropsReliabilitySelfTest.cs：增加配置分类/四类操作门禁/重复授权不发送请求/恢复清除错误四条WPF检查。
- tests/test_settings_store.py与frozen_config_smoke.py：完整配置、故障、并发、持久化和真实冻结JSONL验证。
- build-support/Build-TwitchConfigCandidate.ps1：要求完全干净检出，只新冻结Twitch；验证其它平台及Shared源码未变后按原SHA256继承既有三个Worker，并保留各自原始commit/manifest。新app与新Twitch使用最终提交；不把旧三个Worker冒称新构建。Local用途，不扩大SOOP再分发授权。

新锁只协调支持新协议的Worker。旧版本不理解新锁，不能据此保证能阻止旧版本写入；升级使用时应正常退出旧程序。测试未强停真实旧实例。

## 用户数据边界

真实settings.json仍保持原始600字节损坏内容，真实Cookies前后摘要一致；其它平台数据未操作。无可读可信配置副本，**无法从NUL恢复原优先级、排除、语言、观看模式等值**。有效配置/副本中的这些值会完整保留；无副本时只提供明确确认的备份恢复默认操作，不能把恢复默认当作领取授权。全局代理不变，自动领取默认关闭。实际用户尚未执行这个操作，因此真实目录恢复仍需用户明确选择，不能写成已经恢复原设置。

## 测试与真实业务证据

- 最终定向Python 53 PASS，较faf2671原25项新增28项；覆盖用户列出的20类配置场景，另含重复键/BOM/硬链接/实际Windows被占用目标替换、跨进程OS锁、陈旧进程覆盖拒绝、Core错误恢复与重复Worker排他。初次测试发现旧测试直接停事件循环却未释放新锁，已使循环退出释放锁；保留初次失败证据。
- 预提交冻结Worker七组真实JSONL场景PASS：正常、缺失、损坏无备份安全失败、.new恢复、LKG恢复、受控默认恢复、外部修复后重新检测；每组验证Session哨兵字节不变、claim关闭、stop及exit0。SSL/_ssl/OpenSSL3.5.6、Core来源、GQL/Client/playlist自检PASS。
- .NET Release 0警告/错误；WPF四条配置新增检查PASS，无FAIL；系统剪贴板被其它程序占用，整套UI为BLOCKED，不关闭占用程序。
- 预提交冻结真实业务：复制现有Session及坏配置到独立数据目录，在副本执行受控恢复并保持auto_claim_drops=false。Session恢复成功，115 Campaigns/412 Rewards，官方远端Inventory数据可读；自动选中Brawlhalla频道，HLS/分片请求accepted，官方分钟6→9。独立直接官方validate HTTP200、Inventory HTTP200、无GQL错误、21个进行中奖励，相应分钟10。无Claim；stop和exit0，原始Cookies及设置摘要不变。
- 原用户旧Worker始终停留在配置初始化错误，未停止它；本轮运行列表未见另一有效Twitch观看Worker。仍不把短样本解释为排除了所有外部观看来源或完整长期稳定性证明。
- 保留Chrome156、Android App31.8.0、Smartbox Device OAuth、GQL hash、NoAutoClaim、HLS/Segment HEAD、官方零delta估计重置、官方与估计分离、频道资格及Generation/手动停止保护。没有更换OAuth机制。

本地证据在test-output/twitch-config-2026-11-15，含隔离Session副本，禁止整目录上传。源码报告仅提供脱敏结论。最终干净候选需补实机窗口、最终身份与哈希，不用预提交产物替代。

## 尚需最终收据

提交前diff/敏感信息检查与git diff --check；安全push main并核对远端；完全干净检出生成新Twitch/app/安装候选；冻结七组复测与WPF；最终实际窗口状态、停止和退出；记录BuildCommit与SHA256。不创建Tag/Release、不系统安装、不改真实配置。

主代理独立执行，无子代理。实际权限danger-full-access/never未改，与此前读取的全局权限一致；确切任务模型和推理强度无独立查询接口，不能用全局偏好冒充实际任务证明。
