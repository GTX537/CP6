# WP05-13 工程需求来源、分解与可评价判据交付

执行席位为 Main DEV-1，Owner 为 `CP6-EXEC`。采用 D1 的 Main 基线 `0da1432a5802bfae94ad557d1d00a2bbe6e2dca2`。WP05-03 #1340 已在基线。本卡范围为需求来源、原件与澄清、逐条分解及判据草案，沿原 REQ Owner、工作台和命令原键。 [原卡](../../task-plans/WP05.md#wp05-13) · [设计](../../../design-handbook/modules/PLM-REQ-01.md) · [W310 核销](../W310-01/DELIVERY.md#wp05-13)

## 前置与采用

- 以 D1 指定台账递归核对 9 项直接前置、直接层含余卡 11 项、递归 36 项完整 DONE。各交付提交均在所属已核实主线。Main、CRM、Platform、Handoff 的准确提交及实际 PR 合并包含性见原件。 [前置记录](/D:/CP6-test-artifacts/WP05-13/exec-20261126T220847656Z/prerequisites.json)
- ERP 销售沿 WP04-32 #1325 的已采用 `OrderSourceReader`。在同一 Main PostgreSQL 事务中用原订单 Owner 的租户、订单和全行范围、金额字段及当前授权核对准确版和 SHA-256。Sales 历史正式交期、ReleaseLineage、H32 及生产调用继续以原 Owner 的验收状态为准。 [WP04-32 交付](../WP04-32/DELIVERY.md)
- 普通 DOC／FILE 沿 `Main.Plm.ControlledDocument.v1`、`Main.File.Content.v1`。文档版及内容位置对应准确原文件，当前文档、文件、工程父级、用途和扫描／保留门共同生效。ACT 已登记专用文件关闭普通入口。其独立下载沿 `ACT-FILE-REFERENCE/1`、`LOCAL_AUTHORITATIVE_GUARD` 和 `OWNER_GUARDED_STREAM`，本卡继承 WP02-32 的 Main 提供者边界。 [DOC 合同](../../controlled-document-v1.md) · [FILE 合同](../../file-content-v1.md) · [WP02-32 交付](../WP02-32/DELIVERY.md)
- CRM 固定为 `9c071f20d044f9fb26dbf8f91e777a204326adee`。WP03-17 #247 的 `CRM-ACTIVITY/1`、Contact v1.1.1 与 `IN_PERSON` 提供 CRM 会话下的精确活动版。Main 缺可绑定到自身可信租户、当前使用人及最终门的受权读取凭证。WP03-21 #246 的 `OPP/1` 和 CP22 H02 提供稳定子版和原回执，目前商业历史仍为 `COMMERCIAL_HISTORY_UNPROVEN`。WP03-19 未合并，CRM ACT 附件真实采用仍为 `UNPROVEN_G03`。三条路径可保存登记人可读的待补证坐标，相关原文读取继续关闭。 [ACT 交付](https://github.com/GTX537/CP6.CRM/blob/9c071f20d044f9fb26dbf8f91e777a204326adee/docs/development/evidence/WP03-17/DELIVERY.md) · [OPP 交付](https://github.com/GTX537/CP6.CRM/blob/9c071f20d044f9fb26dbf8f91e777a204326adee/docs/development/evidence/WP03-21/DELIVERY.md)

## 实际变更

- 原来源包、需求稳定身份及历史版本沿 `Main.Plm.EngineeringRequirement.v1`。来源可登记精确 Owner、对象、版本和 SHA-256，DOC 还固定内容位置。UTF-8 条款按 `LINE:n` 与原文逐字核对，Sales 按 `JSON:/字段路径` 复核原标量。其他格式可先登记原件，条款待提取。历史版继续读取原件，后继版通过受权差异入口对照。
- 澄清登记固定问题 ID、原来源版和原条款。后继命令可关联单独的受权答复原件，原问题和既有答复保持可追。DOC／FILE 的版本与完整性可验证，客户渠道或权威仍缺证时，工作台保留待核和草稿。没有收到答复时保留待答复。
- 父需求保存固定父版和准确子版的复核依据。按实际父条款展示相关子条款、来源确认范围、必要性、可放宽性、覆盖理由及缺口。只有来源权威范围满足时才显示覆盖。两条作用域不明或部分覆盖的关系继续保留待核。定量沿原十进制字符串、端点和 UOM 评审，定性沿判定依据；判据可固定方法和来源条款。来源缺条件时保持缺项。
- 列表、详情、差异、下载、原操作回读和导出均经当前 REQ 及准确来源的披露门。导出只取当前授权页，需要独立 `export` 动作及原查询权。前滚迁移 `20261126235900_EngineeringRequirementExportV1` 只登记各租户的动作定义，由现有授权入口分配角色。工程需求原件、原回执和存储合同保持兼容。
- 影响评估、正式集合与冲突裁决归 WP05-14，实际分配及满足性签署归 WP05-15。工程自然槽、受理和实际进展归 WP05-60/61。尚待原渠道证明、真实企业适用域及运行采用的材料继续记未证。

## 验收映射

下列为本卡和受影响专项的观察点。正式 PLM-REQ-01 目标仍依台账完整原 AC、其余卡及远端采用。逐项范围、用例名和同树结果见外部原件。

| 原卡与原 AC | 验证入口及观察 |
| --- | --- |
| 来源可追、同名异源、历史差异及不可访问不披露。AC-REQ-001，PG04 A001/A002 | `EngineeringRequirement_wp0513_exact_doc_original_http_and_current_disclosure` 核精确版、错摘要／版本、伪造条款、原键重放、后继及撤权后的列表、详情、导出、下载与原回执。 `EngineeringRequirement_wp0513_sales_exact_original_and_crm_sources_remain_fenced` 核销售原件、跨租户拒绝及 CRM 缺证路径。 |
| 澄清草稿、原版和答复分立。AC-REQ-002，PG03 A001/A002 | `EngineeringRequirement_wp0513_reply_is_fixed_to_an_earlier_question_and_old_source` 核来源后继、原版问题、答复证据、历史回查及扣留后拒绝。真实客户权威确认未证。 |
| 逐条覆盖及防环。AC-REQ-003/004，PG03 A003/A004，PG04 A003/A004 | `EngineeringRequirement_wp0513_per_clause_coverage_keeps_disjoint_children_and_holes` 核同名异源、异范围、真实父子版、重复子项和逐条缺口。`EngineeringRequirement_relations_reject_historical_self_proof_and_retain_uncovered_clauses` 核自证环与保持原条款。 |
| 定量／定性、方法、精度、未知条件、必要性与适用范围。AC-REQ-005/006/009/010，PG04 A005–A012 | `EngineeringRequirement_exact_criteria_keep_unknown_units_and_endpoints_without_guessing` 和 `RequirementEvaluation_unknown_boundary_native_unit_precision_and_disabled_base_fail_closed` 核端点、单位、精度和定性依据；销售来源用例核方法对应条款。逐条覆盖和原冲突回归按来源权威检查范围及必要性。正式裁决、真实分配和执行由后继卡办理。 |
| 当前授权、共同最终门、同键恢复及前滚 | `EngineeringRequirement_competing_revisions_have_one_CAS_winner_and_close_prevents_late_commit`、`EngineeringRequirement_write_fault_and_missing_journal_rollback_then_same_key_recovers`、`EngineeringRequirement_unknown_commit_rolls_back_binding_and_admin_uses_identity_only` 及 `EngineeringRequirement_wp0513_export_migration_keeps_original_receipt_and_requires_new_permission`。 |
| ACT 文件守护边界及持久上游回归 | `EngineeringRequirement_wp0513_act_registered_attachment_cannot_use_ordinary_file_source` 核普通 FILE 缺资格。WP02-32 的 `ActFile_stream_returns_exact_hash_and_stops_after_first_slice_when_DL_is_revoked` 运行守护流撤权和截断。CRM 生产消费按 WP03-19。 |

## 同树证据与工作区

- 最终运行从本报告所在提交独立还原并构建 .NET SDK 10.0.404 Release。隔离 PostgreSQL 18 只监听回环，用 D1 范围内的三个端口和独立 `permission-consumption-` 根。实际 Main API、浏览器组件及 PostgreSQL 使用明确合成的租户、用户、权限、商业记录、澄清、文件和守护证明。源与生产部署状态单独保留。
- [验证脚本](/D:/CP6-test-artifacts/WP05-13/exec-20261126T235242212Z/run-final.ps1) 固定同一源码提交及树、各组完整命令、脱敏环境变量、子进程设置、测试清单、TRX 与摘要。 [最终验证汇总](/D:/CP6-test-artifacts/WP05-13/exec-20261126T235242212Z/final-verification.json) · [逐项 AC 原件](/D:/CP6-test-artifacts/WP05-13/exec-20261126T235242212Z/original-acceptance-final.json)
- 本地分支 `codex/wp05-13` 交总控复核。实际提交、分支处置、秒数和停机读回见外部签收记录。 [工作区读回](/D:/CP6-test-artifacts/WP05-13/exec-20261126T235242212Z/workspace-readback.json)
