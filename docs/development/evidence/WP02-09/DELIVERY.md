# WP02-09 CRM 首个 Owner 共同事务拆卡

**拆卡。** 原号只核销已验证的配置、公开询盘和人工受理 Owner。真实业务消息入站的 Inbox、业务全图、原回执、审计、Outbox 与最终授权由 **WP02-09b** 完成。原号和余卡均须 DONE 才满足原下游前置。 [原卡](../../task-plans/WP02.md#wp02-09) · [余卡](../../task-plans/WP02.md#wp02-09b)

## 原验收与 PostgreSQL 实测

原卡要求业务图、回执、审计、Inbox 和 Outbox 由真实 CRM Owner 在本仓 PostgreSQL 同一事务提交，未知结果按原键查回，已提交结果按当前权披露；收件拒绝须先于业务效果。原正验收覆盖并发同命令唯一效果、完整回滚和提交回包丢失；原反验收覆盖撤权与新写互斥、旧范围、错误租户及原键异内容零新业务效果。 [原定义](../../task-definitions.json) · [PLT-DEL-01 原合同与验收入口](../../../design-handbook/modules/PLT-DEL-01.md)

| 已核销范围 | 证据 |
|---|---|
| 公开自动 H01 与人工放行、拒绝 | 原生入口提交唯一 Submission、Lead、历史、同意、原回执、审计和 Outbox；并发同或不同动作只有一个赢家，拒绝无 Lead。后段故障整组回滚，实际 PostgreSQL 提交确认丢失后按原键恢复。当前来源、范围、租户、会话、ETag 及同意进入真实决定门。对应 DEL01-A1/A3/A8、DEL07-A2/A3/A6 的该业务范围。 [W117](../W117-01/DELIVERY.md) · [W118](../W118-01/DELIVERY.md) |
| 配置写入及共同撤权门 | 配置原图、原回执、审计和 Outbox 一次提交。真实 COMMIT 回包截断后同键读取原状态、正文和 ETag。既有同租户撤权先、业务先两序与合法原回执当前读权沿用 W246 和 W530。对应 DEL01-A1/A3、DEL07-A3/A4/A6 的配置范围。 [W246](../W246-01/DELIVERY.md) · [W530](../W530-01/DELIVERY.md) |
| FX24 传输边界 | 原生 `/internal/crm-inq/outbox/dispatch` 在独立接收事务读提交后的原事件，只写 `Dispatch` 和 `ConsumerInbox`。重复、后段失败及隔离可证明投递状态，发送端 H01 已在先前事务完成。对应 DEL01-A4/A7 的此通知路径及 AC-INQ-023。 [W494](../W494-01/DELIVERY.md) · [接收实现](https://github.com/GTX537/CP6.CRM/blob/ac8a2706a018317eafce8247aff1e7fe5e416329/src/CP6.CRM.Infrastructure/Inquiry/InquiryProjectionReceiverV1.cs) |

在 CRM 远端主线 `ac8a2706a018317eafce8247aff1e7fe5e416329` 的准确源码构建，使用 .NET 8.0.424、实际 Kestrel、两个独立的 PostgreSQL 18.6 测试实例和模拟业务、身份、运输输入。H01 定向回归 21 项，FX24 三场景及配置 COMMIT 回包丢失四项，合计 **25 项通过、零跳过**。首轮按文件名筛选只命中 21 项，第二轮按方法名执行缺少的四项，复用同一已构建程序集。实际命令和日志保存在 `D:/CP6-test-artifacts/WP02-09`，两个 TRX 为 `crm-focused.trx` 与 `crm-fx24.trx`。两套仅本任务占用的 PostgreSQL 均核对停止。

## WP02-09b 的前置冲突与待验出口

1. **实际业务入站入口受阻。** 公开自动受理和人工审核都是冻结的同步入口。FX24 是已经提交的通知。C02 的 Kafka 入站只维护身份投影、授权和会话。C03 的 `cp6.erp.events.v1` 有已接受的 BP/Order 结果身份，CRM 当前产品只有 ERP 查询及合同，CRM 结果接收属于 WP03-06。WP03-06 直接依赖 WP02-09，另经 WP04-03、WP03-05 回到 WP02-09。将该后继卡作为 WP02-09b 的前置会成环。恢复前由总负责确认已接受的生产者、真实业务 Owner、入口及采纳顺序。 [冻结询盘合同](../../../design-handbook/modules/CRM-INQ-01.md) · [C02 采用](../W627-01/DELIVERY.md) · [C03 合同及现行入口](../../../crm/C03-ERP-INTEGRATION.md) · [WP03-06](../../task-plans/WP03.md#wp03-06) · [WP04-03](../../task-plans/WP04.md#wp04-03)
2. **同一事务及原结果仍待实际验收。** 入口确认后，使用实际消息身份与原业务键，同一 CRM PostgreSQL 事务拒绝不合格收件并提交合法 Inbox、业务图、原回执、审计和 Outbox。验证并发原键同内容唯一、同键不同摘要隔离、后段故障整组回滚、提交确认丢失按原键查回、原结果按当前披露权、撤权两序、旧范围和错误租户零业务效果。缺真实最终授权门时阻断新效果。对应 DEL01-A1/A3/A4/A7/A8、DEL07-A2/A3/A4/A5/A6/A9 的业务入站范围，以及 INT-ID-01、PLT-DEL-01 共用的 `COM-S6-PRECHECKPOINT-FINALAUTH` 正反修订。身份入口已验证的内容门按其实际范围保留。 [原修订正反例](../../../design-handbook/remediation/08-acceptance-catalog.json) · [共同授权约定](../../../design-handbook/remediation/03-transactions-and-recovery.md)

PLT-DEL-01 仍等待其完整覆盖卡 WP02-09b，保留 60 条原验收和两条必需修订状态。INT-ID-01 的既有 CRM 业务入站缺口随原范围移至 WP02-09b；两项目标都等待实际入口证据。 [目标覆盖](../../10-work-package-tasks.md) · [INT-ID-01 既有核对](../WP02-06/DELIVERY.md)

## 远端采用与工作区

S01-08 的准确采用来自 CRM #90、WP02-06 来自 CRM #239 和 CP6 #1249、WP02-07 来自 Platform #61。CRM 的 W117、W246 和 W494 源提交均为本轮 CRM 受测远端主线祖先。CP6 从 `9878a9d11621e8e4f1f8b986b60566ed01eb292e` 领取 `codex/wp02-09`，由 DEV-2 提交拆卡。CRM 验证使用换绑前已核实的固定路径及外部制品，CRM 源码未提交修改。正常合并、远端采用、CP6 分支处置和席位释放见本卡 PR 与 `D:/CP6-test-artifacts/WP02-09/main-finish.json`。 [CRM #90](https://github.com/GTX537/CP6.CRM/pull/90) · [CRM #239](https://github.com/GTX537/CP6.CRM/pull/239) · [CP6 #1249](https://github.com/GTX537/CP6/pull/1249) · [Platform #61](https://github.com/GTX537/CP6.Platform/pull/61)

本次交付只调整任务定义、台账、派生页和项目状态。保留原业务合同、原依赖和原验收。常规 Actions 未运行、重跑或取消，生产部署和既有数据库迁移均为零。

## 工时与接续

本轮从约 `2026-11-18T02:40:00+00:00` 的只读准备至候选验证记录 `2026-11-18T02:57:05.1227401+00:00`，计 1025.123 秒。原号 M 卡上限 14400 秒，余卡上限为 13374.877 秒。结转 WP02-06 从候选至官方 finish 的 146.466 秒，单卡计划累计 7.363 小时。本次按实际依赖冲突主动拆卡，未达到 150% 强制拆卡点。卡池仍为 2,052 小时，缓冲耗用为零。本候选之后的正式收尾差额按实际 finish 结转并扣减 WP02-09b 余量。

下一张可开工卡为 WP02-14b，全图最长下游链 41 条边。
