# WP02-15 隐私导出、擦除与恢复验收

WP02-15 验证了现有 Main 隐私 Owner 的稳定主体、逐来源导出、当前下载许可、独立审批、保留、原结果及墓碑。新增一项 PostgreSQL 和实际 HTTP 联测，验证文件处置在当前隐私 hold、审计证据和旧共享归档之间的实际结果。现行业务路径和合同继续采用直接前置已交付的实现。 [卡定义](../../task-plans/WP02.md#wp02-15) · [COORD 派工](https://github.com/GTX537/CP6.AI-Handoff/blob/afff74eb2e9ef7684237ae835a11b9d03c58a79d/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261118T052117Z-exec-wp02-14b-wp02-15/MESSAGE.md) · [START](https://github.com/GTX537/CP6.AI-Handoff/blob/4a3d18a6eef00567de592295f45915dad521c4a2/handoffs/CP6-DEV-20261012/tasks/WP02-15/messages/20261118T053003Z-exec-wp02-14b/MESSAGE.md)

## 已验证的业务结果

| 出口 | 实际覆盖 |
|---|---|
| 主体和导出 | 现有主体及租户入口使用准确租户与稳定主体，依显式分类产生制品和逐来源时间、采用状态、缺项。用实际 Kestrel 核对制品身份、接收者当前读权、期限、撤权及原键恢复。未登记数据保持未知或未覆盖。 |
| 独立审批及擦除 | 现有 Main 身份、分类安全字段、主体和租户导出载荷分别应用自己的审批、当前权限及保留约束。核对原效果与最小回执，确定先提交 hold 时新效果被阻止。旧直接确认路径返回现有退役结果。 |
| FILE、PRIV 和 AUD | 发布 Main FILE 的物理计划后，在另一租户以实际签名会话设定目标主体的隐私 hold。独立保留准确原件的必要审计引用，再登记一条模拟的历史归档指针。三种情况分别阻止物理删除。归档从真实附件服务下载并核对字节，解绑后仍保留原文件。全部条件解除后确认实际删除。保留可读原版本、摘要、原计划和完成回执。 |
| 恢复 | 使用真实 `pg_dump`、`pg_restore` 及隔离候选库。已登记身份、分类安全字段、主体和租户导出载荷在当前策略与 hold 下重施。验证原件已缺失与实际删除分开、执行权限撤销后回查、回包丢失后的源责任和同一回执、候选开放前的实际源证明。 |

Main 源码基线为 `4ecad67f4a5a092c3e415d2c9516b084adc9e89d`，新增联测的受测提交为 `944d33b900e1657a7f68a44b27241f1501bf2eaa`。本任务仅增加集成回归与交付记录。上述对应 Owner 的历史运行证据已核对实际远端采用，模拟范围和旧验证结果保持原口径。 [本卡 PR](https://github.com/GTX537/CP6/pull/1255)

## 本地验证与历史证据

- Windows、.NET SDK 8.0.424、PostgreSQL 18.6、实际 Kestrel、隔离的文件目录及模拟租户、政策、归档和业务。新增联测中的扫描结果明确使用模拟输入。**27 项不同的后端场景通过，零跳过。** 实际归档包含备份和隔离恢复。原始 TRX、运行命令与测试资源日志位于 `D:/CP6-test-artifacts/WP02-15/`。
- `baseline.trx` 记录首次组合运行中 14 项通过、5 项失败。恢复夹具的库级归档包含同一组合里的其他主体墓碑、策略、hold 与已登记 C02 来源，旧共享菜单夹具也出现固定 ID 冲突。相同源码在独立数据库中分别复验五项并通过。原失败完整保留，最终计数按测试名去重。新联测在 `file-integration.trx`，已接的三条归档路径在 `subject-payload-archive.trx`、`tenant-payload-archive.trx`、`security-field-archive.trx`。
- 逐来源清单、当前制品下载、独立批准、真实身份效果与老库恢复沿用 W311–W315。精确载荷与分类字段边界沿用 W320、W321、W324。既有公开询盘保留的原样本沿用 W491、W496。CRM 远端读基线为 `ac8a2706a018317eafce8247aff1e7fe5e416329`，本卡采用其已存证据和只读源码核对。 [W311](../W311-01/DELIVERY.md) · [W312](../W312-01/DELIVERY.md) · [W313](../W313-01/DELIVERY.md) · [W314](../W314-01/DELIVERY.md) · [W315](../W315-01/DELIVERY.md) · [W320](../W320-01/DELIVERY.md) · [W321](../W321-01/DELIVERY.md) · [W324](../W324-01/DELIVERY.md) · [W491](../W491-01/DELIVERY.md) · [W496](../W496-01/DELIVERY.md)
- 本轮集中审查覆盖受影响权限、归档原件的来源和复用、原回执及完整 diff。测试未修改运行时代码、界面或依赖。既有页面验证复用其历史结果。本轮使用新建隔离测试库，GitHub Actions 运行 0 次，既有环境迁移与生产部署均为 0。

## SYS-PRIV-01 同一 PR 核对

逐项核对原 22 条验收。13 条具有可用于当前目标的完整证明，具体状态见表。当前目标保留跨 Owner、CRM 和真实归档范围。原设计未登记必需运行修订。 [原验收](../../assessments/20261011/catalogs/foundation.json) · [隐私设计](../../../design-handbook/modules/SYS-PRIV-01.md)

| 原验收 | 证据和实际范围 | 状态 |
|---|---|---|
| AC-PV01-01 | 稳定 UserId 与目标租户共同绑定。主体名相同及不同操作者租户的资料隔离。 `PrivacySubject_operator_tenant_and_same_name`、`TenantPrivacyExport_actual_HTTP_two_sessions`。 | PASS |
| AC-PV01-02 | 主体导出使用显式用户、安全事件和稳定角色投影。嵌套自由内容和认证密钥缺省排除，未分类来源保留缺项，原数据仍在。 `PrivacySubject_manifest_uses_actual_stable_sources`、`PrivacyExport_selected_sources`。 | PASS |
| AC-PV01-03 | 已接来源按独立读取时间列出零行、排除和不可用。实际多仓来源的 asOf 与权限组合仍待复核。 `PrivacySubject_known_empty`、`PrivacySubject_manifest_uses_actual_stable_sources`。 | 待验 · WP14-08 |
| AC-PV01-04 | 真实 HTTP 下载复核接收方、主体、当前权限和有效期。撤权、过期或缺少原载荷均不返回旧字节。 `PrivacyExport_actual_HTTP_generates_once`、`PrivacyExport_expired_corrupt_missing_payload`。 | PASS |
| AC-PV01-05 | 导出提交结果未知时按原键恢复原制品。最终会话失败时制品、回执和审计共同回滚。 `PrivacyExport_lost_commit_is_recovered`、`PrivacyExport_actual_HTTP_generates_once`。 | PASS |
| AC-PV02-01 | 历史 PostgreSQL 样本已核询盘 24 日历月及到期前控制。实际 CRM 最新身份采用下，完整变更及软删组合仍待消费卡复核。 `W491`、`W496`。 | 待验 · WP03-04 |
| AC-PV02-02 | 历史运行覆盖询盘原图、原回执和清除。最新 CRM 活动纠正、搜索和全部原命令版本仍需沿实际消费者组合核实。 `W491`、`W496`。 | 待验 · WP03-04 |
| AC-PV02-03 | 既有 CRM 证据显示各询盘处置期限和父子绑定。独立更早期限与非法绑定的实际宿主组合仍属消费卡。 `W491`、`W496`。 | 待验 · WP03-04 |
| AC-PV02-04 | 历史批次按原键恢复与真实保留已有运行证据。本轮已核 Main 恢复责任，CRM 实际子单元未知结果及重复处置沿消费卡核实。 `W491`、`W496`。 | 待验 · WP03-04 |
| AC-PV02-05 | 已读远端 CRM 的原小时调度和 24 小时目标。真实后台运行、延迟诊断和记录按消费卡验证。 `W496`、`CRM-REMOTE`。 | 待验 · WP03-04 |
| AC-PV02-06 | 既有 PostgreSQL 保留测试记录实际回滚与非完整结果。实际 CRM 图、密文和载荷形态异常的隔离与报警仍待核实。 `W491`、`W496`。 | 待验 · WP03-04 |
| AC-PV03-01 | 导出、下载、授权管理、处置与审批各自分权。隐私事务采用同一平台主体门，复用实际模拟会话和独立审批的证据。 `PrivacyDisposition_exact_plan_independent`、`PrivacyExport_selected_sources`、`WP02-13b`。 | PASS |
| AC-PV03-02 | 目标仍具平台权限时阻止处置；旧直删入口拒绝。复用当前平台末名保护的并发、普通原生写入及真实 HTTP 证据。 `PrivacyDisposition_cross_operator_tenant_hold`、`PrivacyDisposition_legacy_confirmation_cannot_bypass`、`WP02-13b`、`W315`。 | PASS |
| AC-PV03-03 | 预览冻结目标、政策、范围、版本及确认依据。变更条件后沿原计划协议重新验证，确认值无法绕过审批。 `PrivacyExecution_scope_confirmation_policy`、`PrivacyDisposition_exact_plan_independent`。 | PASS |
| AC-PV03-04 | 实际原生 hold 与执行竞争有明确胜序。审批人或执行权失效阻止新效果，现有 FILE 计划对后来隐私 hold 和审计保护立即生效。 `PrivacyExecution_native_hold_and_execution`、`PrivacyExecution_hold_and_approver`、`PrivacyFile_current_global_hold`。 | PASS |
| AC-PV03-05 | 执行撤权后当前读者仍可查原回执。候选已提交而独立源未确认时，继续隔离并按原键恢复。 `PrivacyExecution_actual_HTTP`、`PrivacyRestore_candidate_committed_source_unknown`、`ExportPayloadDisposition_restore_actual_candidate_COMMIT_reply_loss`。 | PASS |
| AC-PV04-01 | 已连接的身份、导出载荷、安全字段、技术内容按各自准确政策和当前依据决定，分类保留可追溯。跨 Owner 分类和业务采纳仍待核实。 `W320`、`W321`、`W324`、`PrivacyFile_current_global_hold`。 | 待验 · WP14-08 |
| AC-PV04-02 | 实际跨租户隐私 hold、审计必要证据、旧归档及 DOC/RMA 引用分别阻止处置。源 hold 还会关闭曾获准的恢复候选。 `PrivacyFile_current_global_hold`、`FileDisposition_holds_actual_document`、`PrivacyRestore_admitted_request_holds`、`WP02-14b`。 | PASS |
| AC-PV04-03 | 原身份、摘要和批准依据保留。已分类导出独立于原件；删除载荷留原回执与墓碑，`REAPPLIED` 和实际缺失分开。 `PrivacyFile_current_global_hold`、`PrivacySubject_manifest_uses_actual_stable_sources`、`TenantPayloadDisposition_new_tomb_invalidates`、`W320`、`W321`。 | PASS |
| AC-PV04-04 | 旧归档候选与源库两次提交。候选真实提交、回包丢失时保持隔离及原键责任，明确保留未覆盖的 FILE、备份和外部副本。 `PrivacyRestore_candidate_committed_source_unknown`、`ExportPayloadDisposition_restore_actual_candidate_COMMIT_reply_loss`、`PrivacyRestore_actual_HTTP_independent_approval`、`W324`。 | PASS |
| AC-PV04-05 | 已接身份、安全字段及主体和租户导出载荷在真实旧库重施并保持墓碑，现策略和 hold 变化关闭旧候选。独立 FILE、外部归档及完整跨 Owner 恢复仍待核实。 `PrivacyRestore_native_archive_reapplies`、`SecurityFieldDisposition_restore_source_current_hold`、`TenantPayloadDisposition_new_tomb_invalidates`、`ExportPayloadDisposition_restore_actual_candidate_COMMIT_reply_loss`。 | 待验 · WP14-08 |
| AC-PV04-06 | 旧非 EF 证据限于原有身份适配。已接 Main 处置只确认冻结作用域及个人字段，当前 CRM 注册身份封存记录限于原合同。 `PrivacyExecution_exact_identity_effect`、`PrivacyDisposition_legacy_confirmation_cannot_bypass`、`W670`。 | PASS |

| 归属卡 | 待验范围 |
|---|---|
| WP03-04 | 在最新 CRM 真实 Owner 的询盘、Lead 与相关活动中核对原保留锚点、历史和搜索、父子独立时限、原键恢复与实际调度报警。沿本卡已有证据复用范围逐项组合。 对应 AC-PV02-01、AC-PV02-02、AC-PV02-03、AC-PV02-04、AC-PV02-05、AC-PV02-06。 |
| WP14-08 | 按实际多仓和 Owner 核对逐来源 asOf、保留政策与引用全集，以及独立 FILE、WAL、外部归档和跨 Owner 的恢复墓碑及开放条件。 对应 AC-PV01-03、AC-PV04-01、AC-PV04-05。 |

## SYS-FILE-01 关联核对

| 原验收 | 本卡实证 | 剩余任务 |
|---|---|---|
| AC-FI05-02 | 同一个原存储路径的异租户历史归档能真实读出原字节，其存在使物理处置失败。归档实际解绑后原件仍在，等待原 FILE 操作处置。 | WP14-08 核对各实际外域共享或归档来源与物理引用全集。 |
| AC-FI05-04 | 原文件处置后保留准确身份及摘要，原件下载被关闭。旧候选实际验证已登记 PRIV Owner 的墓碑防回填与恢复重施。 | WP14-08 核对独立工程文件、外部归档及完整跨 Owner 恢复时的原身份和资料状态。 |
| AC-FI07-06 | 原计划遇实际隐私 hold、当前审计必要引用会保持原件及未完成回执。复验 FILE 对未知结果、实际 DOC/RMA 引用的阻断。 | WP14-08 核对跨源未知、争议和必要原证据；技术目标实际采用仍由原消费卡验收。 |

三项增加了已接 Owner 的实证，完整跨域验收仍待运行。CRM ACT 两条必需修订由 WP03-19 核验，原状态保持。 [文件卡报告](../WP02-14b/DELIVERY.md)

## 远端采用与工作区

两个 GitHub 工作流引用的树均为 `fefb806d1cc0c5b8d5c7169bb015fa7c7c41183a`。普通编译和测试入口为手动，R2 候选为受保护标签触发。分支采用、任务引用处置及 DEV-2 的实际释放结果以本 PR 和 `D:/CP6-test-artifacts/WP02-15/finish.json` 为准。回报沿原资料分支发给 COORD。

## 工时与串行接续

WP02-15 卡 DONE。已接 Main 隐私 Owner、原键和归档墓碑复核完成，新增 FILE、PRIV 和 AUD 共门联测。SYS-PRIV-01 原 22 条中 13 条通过，剩余 9 条归 WP03-04 和 WP14-08。SYS-FILE-01 已复核三条关联场景。 本地 27 项通过，零跳过。原号 DONE / 完整 DONE 为 **50 / 48**，登记卡 **53/380**，目标 **4/125**。

本卡自官方开工 `2026-11-18T05:29:05.900365+00:00` 至 `2026-11-18T05:47:08.6912561+00:00` 计 1082.791 秒，低于 M 卡 4 小时上限。结转 WP02-14b 官方收尾 246.740 秒。本轮目标复核 287.897 秒，目标池累计 0.743 小时。计划累计 9.016 小时，未到 24 小时检查点，缓冲耗用为零。正式合并和席位收尾按实际 finish 随下张卡结转。

串行规则计算的下一卡为 **WP02-16b**，全图最长下游链 36 条边。派工由 COORD 确认。
