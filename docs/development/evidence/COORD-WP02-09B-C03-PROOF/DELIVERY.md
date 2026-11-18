# WP02-09b 既有原请求凭据与账户当前授权的同卡续做裁决

## 项目总负责批准

2026-11-18 11:20:20 UTC，项目总负责答复“同意”。原问题是能否在 WP02-09b 内先由 Main.C03 提供已有原请求的只读凭据，再由 CRM.Account 提供本卡的账户归属、写入授权和原回执当前读权，最后沿原 CRM 分支完成 A12 入史。此前的裁决已限定结果种类和归属，本次只批准该范围的提供方与实施顺序。本卡实际完成后结束本轮协调。[原范围裁决](../COORD-WP02-09B-A12/DELIVERY.md) · [阻断回执](https://github.com/GTX537/CP6.AI-Handoff/blob/df60a6df19915e3887c9a31d0a56f9c589d17c0e/handoffs/CP6-DEV-20261012/tasks/WP02-09b/replies/20261118T105755Z-coord-blocker-ack/MESSAGE.md)

| 卡内顺序 | 负责方 | 交付及核验 |
|---|---|---|
| 1. 既有请求的只读凭据 | Main.C03 | 在受信租户和已登记调用方范围内，仅对确有原件的 C03 业务伙伴请求提供凭据。按原合同验证生产者、合同及 epoch、租户、账户、请求 ID 和版本。用 `CommandInbox` 原命令字节及其摘要与 `Request` 的业务键、账户、版本和规范化输入摘要相互核对。保留两种摘要各自的含义。来源缺失、冲突、越租户或不具备访问权时不出具可用凭据。 |
| 2. 账户归属和当前权 | CRM.Account | 结果到来前，从独立、可信渠道核验已存在的原请求及其账户关联。以 CRM 有权读取的账户记录核对实际租户、账户、归属和版本。写入采取 A12 的 `service:account.c03.ingest` 及对象范围。原回执披露独立核对 `account.command.read` 和原结果范围。沿用 PostgreSQL 调用方事务的 C02 当前主体或服务身份检查，在归属方最终决定点重核并与撤权、影响归属的更新互斥。单独的身份检查、账户 ID 或旧允许均不能放行。 |
| 3. 按原卡入史与恢复 | CRM.Account | 只处理合同规定的业务伙伴成功/失败结果。在可信相同请求流内保留原键、原摘要、版本及字符串 `BusinessPartnerKey`。一次提交 Inbox、原请求结果历史、原命令回执、审计和 Outbox。未知请求仅隔离，结果先到不得触发补建或登记原请求。 |

Main 现有伙伴、报价、订单读取接口不提供这项凭据。已有账户模型、历史日志、结果来信和传输探针不能单独证明账户当前权。凭据处理不发起 BP 请求或重放。完整账户维护、完整 A11、其余 A12、A13 当前映射及订单继续归原卡。授权不含生产数据迁移、现网授权或安全设置变更、GitHub Actions 和生产部署。[Main 原请求账](https://github.com/GTX537/CP6/blob/f522beeb6fbe2398f3ded503a20eb1d2f5ad549d/CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs#L28) · [原命令写入](https://github.com/GTX537/CP6/blob/f522beeb6fbe2398f3ded503a20eb1d2f5ad549d/CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs#L257) · [A11/A12 合同](../../../design-handbook/remediation/02-commercial-physical-contracts.md) · [服务身份当前事实](https://github.com/GTX537/CP6.CRM/blob/ac8a2706a018317eafce8247aff1e7fe5e416329/src/CP6.CRM.Infrastructure/Identity/PostgreSql/IdentityServiceInspectionV1.cs#L36) · [用户身份当前事实](https://github.com/GTX537/CP6.CRM/blob/ac8a2706a018317eafce8247aff1e7fe5e416329/src/CP6.CRM.Infrastructure/Identity/PostgreSql/IdentityHeldInspectionV1.cs#L35)

## 原卡接续与验收

直接前置仍为原号 WP02-09。原号及其既有上游已交付。本次登记的是该余卡内的工作顺序。CP6 核对基线为 `f522beeb6fbe2398f3ded503a20eb1d2f5ad549d`，CRM 主线为 `ac8a2706a018317eafce8247aff1e7fe5e416329`，CRM `codex/wp02-09b` 保存于 `07fc456a5ae283d93e82a33fff43acdb5ee944a9`。原执行人 `EXEC-WP02-14B` 沿用原 START、原分支和报告路径。本次不新建业务卡、不追加预算。本卡仍为 PLANNED，上次业务席位记录为 deferred。[原卡分工](../../task-plans/WP02.md#wp02-09b) · [START](https://github.com/GTX537/CP6.AI-Handoff/blob/e86772d4d9f477ae31aa0695a40bf48c01e28b4a/handoffs/CP6-DEV-20261012/tasks/WP02-09b/messages/20261118T103318Z-exec-wp02-14b/MESSAGE.md) · [暂缓核验报告](https://github.com/GTX537/CP6.CRM/blob/07fc456a5ae283d93e82a33fff43acdb5ee944a9/docs/development/evidence/WP02-09b/DELIVERY.md)

- 在 Main 先以真实产品处理、持久化和只读提供路径验证既有请求凭据，固定源提交、合同版本及样本来源。CRM 使用该确切提供版本，完整跑通成功、失败和未知请求隔离。保留两个仓各自受保护 PR 的实际采用证据及同一份业务报告。
- 真实 PostgreSQL 上逐项验证并发重复只生效一次、同键异摘要及同版异载荷隔离、后段故障整组回滚、提交确认丢失按原键恢复、当前原结果读权、撤权两序、旧范围及错误租户拒绝、缺真实最终门时零业务效果。报告逐项对应 DEL01-A1/A3/A4/A7/A8、DEL07-A2/A3/A4/A5/A6/A9 及 `COM-S6-PRECHECKPOINT-FINALAUTH` 正反两例。可以使用明确标记的模拟业务输入，必须经过实际产品入口与归属方授权门。
- 本裁决没有运行上述业务验收。11 项原 AC 和 2 项最终授权场景仍为 NOT_RUN。仅在实际采用和验证达标后按原规则在同卡交付 PR 核销。在此之前保留 PLT-DEL-01、INT-ID-01 的缺口和下游前置。本次不安排其他业务卡。[原验收缺口](../WP02-09/DELIVERY.md) · [唯一台账](../../roadmap.json)
