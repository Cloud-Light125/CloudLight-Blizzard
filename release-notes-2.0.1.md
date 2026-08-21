# CloudLight Blizzard 2.0.1

CloudLight Blizzard 2.0.1 精简战绩功能，仅保留国际服官方生涯查询，并改善常用英雄列表的排序与浏览体验。

## 更新内容

- 移除国服战绩查询、网易大神登录、登录状态检查、Cookie / Session、国服战绩请求与相关界面。
- 清理仅服务于国服战绩的窗口、服务、模型、缓存配置和帮助文本；守望先锋国服 / 国际服游戏文件切换功能不受影响。
- 战绩页现在直接使用 BattleTag 查询暴雪官方公开生涯，并继续保留玩家资料、段位、表现概览与国际服错误处理。
- 常用英雄支持按英雄、时长、胜率、场次和使用占比排序，当前列可切换升序 / 降序并显示方向。
- 时长、胜率、场次和使用占比均按原始数值排序，避免格式化文本造成顺序错误。
- 常用英雄区域固定为较大的可视高度，表头保持可见，英雄行在区域内滚动，并在顶部 / 底部与外层页面自然接力。

## 安装

下载 `CloudLight-Blizzard-2.0.1-win-x64-Setup.exe` 后运行即可。

需要 Windows 10/11 x64 和 .NET 8 Windows Desktop Runtime x64。

## 安装包校验

- Windows x64 安装包 SHA-256：`9a1101382bdfae8a50b9fa37e04fb0c7f60a05bfc2f1487500e0f93ebf4933d1`

**Full Changelog**: https://github.com/yundan125/CloudLight-Blizzard/compare/v2.0.0...v2.0.1
