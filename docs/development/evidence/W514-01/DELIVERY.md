# W514-01 · 正式 PGT-004 的可信入口

采用现有 [PGT-004](../../acceptance/postgresql-tenancy.json) 的新业务入口范围。CRM #205 采用测试源 `4ed917fdadf4f5303fda9ce786b9604178b20193`，远端 `3752ec0648f624432428210e5930a1951df4df93` 与验证树一致。本地实际签名 Kestrel、PostgreSQL 18.6 和真实 Node 22 代理函数。使用合成租户、会话与签名权限来源，Node 上游使用测试对照。 [CRM #205](https://github.com/GTX537/CP6.CRM/pull/205)

- 基线用有效 A 请求分别携带 `X-Organization-Id`、`X-Tenant-Id`、`X-Schema`、`X-Region`。四个签名请求均返回 200，各取两条真实业务库连接。四个 Node 请求均返回 200、各调用一次上游。原失败保存在 `pg-red.trx` 与 `node-red.log`。
- 新配置的签名 API、浏览器代理入口拒绝四种外部选择头。实际签名读取、Bearer 读取、保存、编辑上下文、预览和激活共九个头部请求均返回 `400 crm.request_invalid`，零 Owner checkout、无选择器回显，配置与生命周期持久计数一致。无选择器的 A 原草稿读取返回 200，实际打开两条 Owner 连接。Node 八个受测请求均返回同一安全错误、零上游调用。
- 复用 W508 的实际 Main 普通 A/B 连接证据，涵盖缺/空 tenant、未知 issuer、签名租户冒称，以及 `/health/live` 200 和零业务库连接。复用 W513 地区与正文、查询的实际拒绝。W514 同次实测未登记地区仍返回 403、业务库连接为零。 [Main 可信入口](../W508-01/DELIVERY.md) · [CRM 地区与选择器](../W513-01/DELIVERY.md)

最终 `pg-green.trx` 定向 9 通过、0 跳过。TypeScript 测试项目编译和 Node 四个相关套件 44 通过、0 跳过，含公开、审核和旧版代理用例。原始运行、连接和停机证据位于 `D:/CP6-test-artifacts/W514-01`。集中审查覆盖签名入口、浏览器入口、同请求合法对照、现行错误合同及实际零写入。

正式 PostgreSQL 由 19 PASS / 8 NOT_RUN 更新为 20 PASS / 7 NOT_RUN。原询盘业务验收 60/60。CRM 本地及远端任务分支定向清理，DEV-2 席位已释放。Actions 触发和取消为零，部署为零。
