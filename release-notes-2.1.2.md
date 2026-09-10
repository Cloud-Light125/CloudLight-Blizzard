# CloudLight Blizzard 2.1.2

- 修复“区服快照”列表渲染时 `SourceText` / `TargetText` 只读属性被建立错误可回写 Binding，导致 `XamlParseException` 的问题。
- 快照列表及详情中的只读展示字段显式使用 OneWay Binding。
- 增加真实 `DataTemplate` 实例化回归测试，防止同类 WPF Binding 问题再次进入正式版本。
- 保留精简后的区服快照管理页面。
- 保留区服切换所需的文件、Hash、备份可用性和切换资格安全检查。
- 包含当前主线其它已确认修复。
