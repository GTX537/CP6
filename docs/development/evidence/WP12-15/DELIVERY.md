# WP12-15 · 预演读权与 Cron 输入整改

本稿延续 #1319，交总控第 8 回合 REJECT 后复验。Main 基线 a52330b4ef243146e51560ae40d664c9b99d376e，上一候选 7c36ce48d06923eca02e0253b97d4e78bea911cd。 [上一轮证据](https://github.com/GTX537/CP6/blob/7c36ce48d06923eca02e0253b97d4e78bea911cd/docs/development/evidence/WP12-15/DELIVERY.md) · [草稿 #1319](https://github.com/GTX537/CP6/pull/1319)

## 预演授权

当前原生 IAM 读取要求连接器 Owner 菜单 oa-flow-admin 下在当前租户唯一登记的 conn.forecast，以及当前主体的同菜单授权。新流程预演和导出、旧草稿预演、旧发起前预演和两个 Cron 入口共用此检查。旧发起前预演要求准确发布版本，缺 pin 时返回 422 FORECAST_PIN_REQUIRED。流程仍按准确 Scope、定义、发布 pin 或草稿修订核对 SchemaJson 和 FormKey 字段读权。Cron 还要求 Calendar.View 和准确租户日历、修订、时区规则和起算 UTC。旧 Cron 路由还检查 FlowTrigger.View，提供准确输入后返回兼容的 next 与解释。 [动作门](../../../../CP6.Core/Services/Oa/WorkflowForecastAuthorityV1.cs) · [流程预演 Owner](../../../../CP6.Core/Services/Wf/FlowAuthoringOwnerV2.Forecast.cs) · [日期解释 Owner](../../../../CP6.Core/Services/Oa/VersionedWorkCalendarV1.Cron.cs)

初次、最终读取在原生 IAM 同一事务锁内。最新角色撤回、字段屏蔽和另一个租户的请求会被拒绝。公开响应 no-store，拒绝不回显模拟变量、摘要、人员或凭据。页面遇独立预演动作缺失时清除预演结果和输入。工作区已查看到的策略变更会取消在途预演并丢弃回包。现行定义、年历仍按各自资源读权读取。 [实际鉴权用例](../../../../CP6.Tests/Erp/WorkflowForecastAuthorityPostgreSqlTests.cs)

**待 Owner 采用。** Main 的现行权限目录没有 conn.forecast 或已采用的等价 Owner 映射。目录缺失、禁用或歧义时返回 503 FORECAST_AUTHORITY_UNPROVEN，有登记但缺当前动作时返回 403 FORECAST_ACTION_FORBIDDEN。页面分别标示。请由 SYS-IAM 与 OA Connector Owner 确定 conn.forecast 的正式菜单/登记、适用 Scope、资源和字段组合，或提供严格等价的已采用映射及凭证。正向验证来自隔离合成角色。产品词条播种、授权记录和平台安全配置没有写入。 [现行权限词表](../../../../CP6.WebApi/Seed/WorkCalendarConnectorPermissionSeed.cs)

## Cron 输入与无效果验证

配置沿 WfCronHelper 同一五段解析和 200 字符上限。版本化 TIMER、旧 TIMER 配置校验及两个解释 API 复用边界，前端 TIMER 配置和年历解释输入共用 200 字符限制。真实 Main HTTP 与 Chrome 接收语法合法的完整 200 字符。两个 API 拒绝 201 字符、空白和非五段输入。旧路由缺准确时间 pin 时明确拒绝。 [后端共用校验](../../../../CP6.Core/Services/Wf/WfCronHelper.cs) · [前端输入边界](../../../../cp6.web/src/views/oa/admin/flowTriggerModel.ts)

隔离租户 America/New_York 的两个 DST 重复时点为 05:30Z 和 06:30Z。缺失时点按原解释规则跳过。验证了旧/新工作日历修订、未知修订、时区及规则变化、缺时区以及 NextDueUtc、LastFiredUtc 与配置回读。隔离环境的连接器外呼计数为零，Submission、Instance、Token、Task、FormData、通知、Job、连接器准入和所核对的 Outbox 增量均为零。实际 TIMER 开通仍依原运行合同。 [Cron HTTP 证据](../../../../CP6.Tests/Erp/WorkflowCronExplanationPostgreSqlTests.cs) · [实际 OA Chrome](../../../../cp6.web/scripts/oa-simulation-browser.mjs)

## 本机工件

受验源码 `122b22593c9896f04b114e3f8e1130f52f3d8e23`，源码树 `2454b503d677b18702a19c110de6ec162563fdf1`。LAPTOP-3QQ44FJS 的隔离 PostgreSQL 18.6、实际 Main HTTP、实际 OA Chrome 与定向回归合计 **42/42**。Vue 类型检查通过，Vitest **26/26**。SQL Server 记为 NOT_RUN。原始文件位于 C:/Users/tt/CP6-test-artifacts/WP12-15/20261123-wp1215-exec3-r2。

上一轮 Chrome 样本输入与页面首次权限刷新并发，改为等待首次刷新完成后再填表。复测继承 15d250ea4e4a993143d3ebbc5ae8baaff598635c 构建的 .NET 程序集，后续源码提交仅改 Chrome 脚本，逐文件同源核对。

扩展回归另列 9 项既有失败。8 项旧 SQLite 触发器验证夹具在建表时缺 Latin1_General_100_BIN2，1 项全局 POST 权限扫描仍报原 QueryController.HistorySearch 登记。本卡预演三个入口和导出、Cron 的明确 Owner 路径已接受行为测试，相关新增 POST 已登记到边界扫描。本轮及上一候选的原始对照 TRX 单独保留。

| 工件 | SHA-256 |
| --- | --- |
| `wp1215-review-recheck.trx` | `63afb3bc9437e670a50bdbd87ac5678f28939ed9d767640bf8961933091d117d` |
| `wp1215-review-recheck.log` | `7a4f2bdfdae253cb05aa4d0d9a13cb52028a74745d33bf9d3182eb9d0ec59408` |
| `permission-consumption-wp1215-review-recheck/simulation-browser/result.json` | `f1c37370342fe46ddc9f21f864d00b856eda5234b2469cca530cfba56716629a` |
| `type-review-final.exit.txt` | `5feceb66ffc86f38d952786c6d696c79c2dbc239dd4e91b46729d73a27fb57e9` |
| `vitest-review-final.log` | `c40a279abb1deb2d93bc498ae1cb1f0752f067e65a1d7f6c199b292e7b1b3906` |
| `prior-compat-reproduction.trx` | `55004bbe5e1bb14cd3815972f11ca42404736b4c7cb9f847755ed1979d9d8243` |
| `final-compat-separate.trx` | `f36b6f685e1f29aa91befe6bfcf7ba75093c8070f48a840046a90e261cf14bf6` |
| `source-bind.json` | `8a18b881809688052c0765f67931b985ec9ecfec7c19a3c28b119617193b042d` |
| `ledger-check.log` | `0f159c53e2f6586a09c81d072380061067efa7153ea389500022111c7997d86b` |
| `forecast-authority-adoption-check.json` | `b6a2166db1dcf1ee91ba5811a9c7cf5bef978fbeb79fb87c2e0db0e06fc3e86f` |
| `dotnet/bin/CP6.Tests/debug/CP6.Tests.dll` | `503a669379bee8e323a03c53c98d557f2051145ad340550795e018cf8684b717` |
| `dotnet/bin/CP6.WebApi/debug/CP6.WebApi.dll` | `730eaf2858dc1c5b6e149822c565d3ef5b86aa6e50d83e781eaa922a1b61158d` |

## 状态、工时和席位

四项原 AC 仍待总控复验，WP12-15 现行台账为 PLANNED，OA-CONN-01 维持 16/32。WP12-14b 及 RP-CONN-01/02 沿原阻断。WP12-14 的 754.861 秒按原 DELIVERY 提交 951c930a7e05ecd544a3e5ab03770798b7a52793 及路径查重，待合资格 Main 台账 PR 单次结转。 [现行台账](../../roadmap.json) · [原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/951c930a7e05ecd544a3e5ab03770798b7a52793/handoffs/CP6-DEV-20261012/tasks/WP12-14/messages/20261123T163520Z-cp6-exec-3-delivery-closeout/MESSAGE.md)

上一轮有效 2420.388 秒。本轮同卡恢复于 2026-11-23T18:06:29.946213Z，截至证据快照 2026-11-23T18:36:22.6685580+00:00 增加 1792.722 有效秒，本卡累计 4213.110 秒。接续前核对 171.438 秒另列，独立验收等待扣除，快照后的草稿交接按实际回执记录。DEV-1 保留同一分支和 #1319，等待复验。
