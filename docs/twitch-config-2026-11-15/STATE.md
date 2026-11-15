# Twitch 配置故障执行记录

- 起始本地e32ceb0，安全快进至远端faf2671cb9553f129e5d203bb18b35aeedc5c0e5；保留未跟踪artifacts/。
- 已安装BuildCommit=e32ceb0；Worker SHA256=7067461bfb1059eeebb6a1ccc4b2680ded040dc1f80dc69d25de6437cc040a96，未含faf2671 Android UA。
- 当前唯一主程序27464，一组PyInstaller父子Worker17368/45500；未停止。真实--data-dir为D:/文档/CloudLight/CloudLight Blizzard/drops/twitch。
- 真实traceback：worker.py:117 _load_core → settings.py:78 Settings → utils.py:259 json_load → json.load → JSONDecodeError: Expecting value line1 column1 char0。
- settings.json为600个NUL，mtime 2026-10-29；无.new/.bak/.lkg；cookies2630字节与cache/mapping.json保留，cache可解析。只读诊断在test-output/twitch-config-2026-11-15/file-diagnostics.json（不含配置内容）。无法证实最初损坏来源；当前进程数量不能证明历史并发。
- 本轮只修Twitch配置加载、保存、恢复和必要UI状态，不修改其它平台业务。无真实Claim，无系统修改，无子代理。运行权限danger-full-access/never未变。
- 待执行：隔离复现；专用SettingsStore及故障分类/受控恢复；定向测试；冻结Worker/WPF；真实Session副本只读业务；提交push干净候选。
- 不可自动重置真实设置；无可信副本时保留文件并要求明确恢复默认操作，自动领取关闭。真实Cookies不得修改。
