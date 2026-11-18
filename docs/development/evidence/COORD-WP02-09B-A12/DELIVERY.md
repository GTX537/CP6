# WP02-09b 与 WP03-06 的 C03 业务伙伴结果分工裁决

## 批准内容

2026-11-18 09:32:17 UTC，项目总负责对 COORD 在 Handoff #307 提出的分工和次序问题答复“同意”。WP02-09b 先承接 A12 中已有原请求、能核验账户归属和当前授权的 C03 业务伙伴结果入史。批准范围限于这个入口。其余 A12、A08 至 A11 继续由 WP03-06 承接，A13 当前映射由 WP03-07 承接，订单结果和订单流程保留原归属。[裁决原问题](https://github.com/GTX537/CP6.AI-Handoff/blob/9ad7ecea570859232db396a988be7ba04bd8e5f9/handoffs/CP6-DEV-20261012/tasks/WP02-16b/replies/20261118T092436Z-coord-ack/MESSAGE.md)

## 生产者、归属与先后

Main ERP 已按 C03 合同在 `cp6.erp.events.v1` 发布结果。CRM 接收 `urn:cp6:erp` 的 `com.gtx537.erp.business-partner-synchronized.v1` 和 `com.gtx537.erp.business-partner-failed.v1`，由 CRM 账户原请求的归属方在原请求上登记历史。入口须从受信接收边界验证生产者和租户，从已持久化的原请求核对账户、请求身份和版本，并在最终决定点复核当前授权。单凭消息声明不能证明这些关系。[C03 合同](../../../crm/C03-ERP-INTEGRATION.md) · [事件合同包](../../../../contracts/events/erp/contract-bundle.v1.json) · [CRM 收件合同](https://github.com/GTX537/CP6.CRM/blob/ac8a2706a018317eafce8247aff1e7fe5e416329/src/CP6.CRM.Infrastructure/Erp/Contracts/ErpEventContracts.cs) · [账户 A12/A13](../../../design-handbook/modules/CRM-ACC-01.md)

已知原请求的身份、账户归属或当前权无法核验时，拒绝业务写入和结果披露。未知请求进入隔离，仅留可审计的隔离证据。不得从来信反建原请求、主动申请 BP、恢复账户、推进当前映射或处理订单。只在同一原请求结果流内比较 `resultVersion`，保留原业务键、`requestVersion`、原摘要和字符串 `BusinessPartnerKey`。同版本同载荷去重，同版本异载荷隔离。

WP02-09 原号已由 #1251 合并。它的直接前置和所属拆分前置已核实。WP02-09b 是原号自己的余卡，可在本裁决生效后先执行。WP03-06 仍须等 WP02-09 连同余卡完整 DONE，并满足 WP02-23、WP03-05、WP04-03。WP03-02、WP03-04 也须等 WP02-09b 交付。分工调整依据为 CP6 `39d741bcd7ab1e6ef7919f00c29c1b6296a24a67` 和 CRM `ac8a2706a018317eafce8247aff1e7fe5e416329`。[原卡拆分报告](../WP02-09/DELIVERY.md) · [原号合并](https://github.com/GTX537/CP6/pull/1251) · [上卡交付](../WP02-16b/DELIVERY.md)

## WP02-09b 的交付出口

- 交付实际 CRM 业务接收入口，并以同一个 PostgreSQL 调用方事务提交 Inbox、A12 原请求历史、原命令回执、审计和 Outbox。验证合法同步成功、失败结果和并发重投，同一原请求只产生一份对应业务效果。
- 验证同键异摘要和同版本异载荷隔离、跨请求版本互不影响、旧范围和错误租户拒绝、未知请求仅隔离。验证撤权先和写入先两种次序、当前披露权拒绝、缺真实最终授权门时零业务效果。
- 注入后段故障确认整组回滚，提交确认丢失时按原键查回同一结果。按原拆分报告逐项对应 DEL01-A1/A3/A4/A7/A8、DEL07-A2/A3/A4/A5/A6/A9 以及 `COM-S6-PRECHECKPOINT-FINALAUTH` 正反例。真实 PostgreSQL 和实际业务入口均需留下可复核证据，模拟数据或服务须标出边界。[原验收与余项](../WP02-09/DELIVERY.md)

## 台账和核验

本次仅修订现有拆分余卡的分工。WP02-09b 由 BLOCKED 转为 PLANNED，执行人领取席位前保留 `NOT_CHECKED` 和空 assignment。阶段 A 唯一可选卡是 WP02-09b，卡预算 12831.351 秒。原号 DONE 50/375，完整 DONE 49/375，登记卡 DONE 54/380，目标 DONE 5/125。此次未核销业务验收、目标或修订。业务开工和交付须另有 START、单卡报告、远端采用与 COORD 验收。[任务定义](../../task-definitions.json) · [唯一状态台账](../../roadmap.json) · [单卡计划](../../planning/20261117-card-execution.md)

本机台账生成及第二次独立检查均为 PASS。逐字段对比确认只调整两张卡的定义和一张余卡的状态、分工及候选选择，既有直接前置、原验收记录和预算均保持一致。所有新增本地引用均可回读。本次为治理记录，业务运行证据由执行卡交付。
