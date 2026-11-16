# JSONL 结构样例

下例是隔离 DTO 的协议示意，ID 采用脱敏占位，URL 表示字段来源；**不是从真实账号抓取的认证或奖励记录**，也不能直接用作真实图片验收。正式图片验证需要 Core 实际输出的 URL。

```json
{"id":"request-redacted","ok":true,"result":{"currentDrop":{"id":"drop-redacted","campaignId":"campaign-redacted","name":"Core Drop 名称","game":"Core Game 名称","gameImageUrl":"<DropsCampaign.image_url 的真实 HTTPS URL>","benefitIds":["benefit-redacted-a","benefit-redacted-b"],"benefits":[{"id":"benefit-redacted-a","name":"Core Benefit A 名称","type":"DIRECT_ENTITLEMENT","imageUrl":"<Benefit A.image_url 的真实 HTTPS URL>"},{"id":"benefit-redacted-b","name":"Core Benefit B 名称","type":"BADGE","imageUrl":"<Benefit B.image_url 的真实 HTTPS URL>"}],"currentMinutes":18,"requiredMinutes":60,"estimatedExtraMinutes":2,"progressSource":"remote_with_estimate","progress":0.3,"completed":false,"claimed":false,"canClaim":false,"eligible":true,"preconditionDropIds":["prior-drop-redacted"],"preconditionsMet":false,"preconditionsSatisfiedForMining":true,"lastInventorySyncAt":"2026-11-16T00:00:00Z"}}}
```

Campaign 的 gameImageUrl 与嵌套 drops[].benefits 直接来自同一 Core 对象；inventory[] 与 currentDrop 使用相同 _drop_snapshot 序列化函数。原 JSONL id / ok / result 封装不变，不新增认证命令或 OAuth scope。
