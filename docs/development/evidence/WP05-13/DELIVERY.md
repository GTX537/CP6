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
| D1 谱系与 FILE 前滚兼容 | `ActFile_forward_migration_preserves_an_existing_file_and_receipt_without_CRM_registration` 核旧库原生 FILE 上传、前滚后的原字节、原回执、保留和未登记 CRM。 `EngineeringLegacy_forward_migration_preserves_existing_revision_original_journal_and_grants` 核前滚前后修订原件、原回执及授权。 |

#1339 的 FILE 前滚用例使用早于 #1340 工程谱系的数据库。当前修订 Owner 在创建时读取 `revision_conflicts`，旧库返回 `42P01`。本卡从 WP05-03 的既有前滚用例提取旧库夹具，按同一事务中的工作版、对象绑定、序号和原操作存储准备父修订。仅在待应用谱系迁移的测试库走该夹具。当前库仍经工程修订 Owner。两个前滚测试的业务断言、产品合同和 FILE 提供者保持原有语义。

ACT 的完整 HTTPS 下载使用逐块守护事务。早期验证两次触及夹具客户端的 15 秒整响应时限。本卡为启用 ACT 的 TLS 夹具配置 30 秒整响应时限，保留完整原字节和摘要核对、撤权断流的单独时限及原有断言。

## R1 复核返工

- 待补证的 ACT 附件来源用 `origin.actFile` 保存 `ACT-FILE-REFERENCE/1` 的 `activityId`、`activityRevision`、`referenceSetVersion`、`referenceId`、`attachmentId`、`version`、`contentSha256`。文件键和文件版按合同保留为原字符串。允许保存已知坐标的草稿，原件下载与尝试确认均返回 `UNPROVEN_G03`。旧通用引用照原版回读和重放，补齐坐标时形成新的草稿版本。新增字段沿现有不可变 JSON 和原操作账本保存，R1 无新增迁移。
- `EngineeringRequirement_wp0513_r1_act_attachment_drafts_preserve_distinct_host_coordinates` 用实际 HTTP 和 PostgreSQL 复核同名、同文件、不同活动及引用成员的两条原始草稿、列表和精确版回读、同键重放及下载封闭。`EngineeringRequirement_wp0513_r1_partial_act_attachment_can_only_be_saved_as_draft` 核缺项时拒绝确认、部分原坐标保存和历史通用格式重放。两项覆盖 AC-REQ-001 和 AC-PLM-PG04-A001/A002 的来源观察，真实 CRM 宿主与文件授权仍按 WP03-19。
- 工作台可编辑和历史回读 ACT 草稿坐标。导出使用最近一次成功显示的目录类别、搜索词和页码，普通查询失败时保留该目录，权限或身份变化时清空。Vitest 覆盖坐标保存与历史回读，以及失败查询后导出原显示页。

## 未运行的 45 项原 AC

以下归属按固定基线台账和原场景逐项核对，均保留 `NOT_RUN`。同一场景中的外部事实由所列提供者负责，其他未证条件保留至指定接续验收。 [原 AC 目录](../../assessments/20261011/catalogs/engineering-oa.json) · [WP05 任务卡](../../task-plans/WP05.md) · [台账](../../roadmap.json)

| 原 AC | 接续卡 | 理由 |
| --- | --- | --- |
| AC-PLM-PG03-A005 | WP05-14 | 批准准确需求集、固定成员和签署范围。冻结与制造授权仍保留各自原结果。 |
| AC-PLM-PG03-A006 | WP05-14 | 批准门复核原集合的必需单位歧义，筛选后仍按原成员阻断并可退回。 |
| AC-PLM-PG03-A007 | WP05-15 | 对指定需求集和目标汇总准确专业证据，固定满足性签署范围与证据版。 |
| AC-PLM-PG03-A008 | WP05-15 | 签署门在报告齐全时仍保留专业 FAIL、未满足及提交任务完成的分立状态。 |
| AC-PLM-FB01-001 | WP05-23，许可事实取 WP05-32 | H42 分列计划、试制申请和 REL 许可。只有计划时，WP05-23 的最终准入不发出 MES 根工单请求。 |
| AC-PLM-FB01-002 | WP05-24 | H53 的逐要求适用性由 VER 给出 REUSE、RETEST 或 N/A，只有基线变化通知时保留待评。 |
| AC-PLM-FB01-003 | WP05-23，联验 WP05-22、WP05-24、WP05-32 | 试制 Owner 贯通 H42/H43/H44/H46 的原申请、许可和根子执行关联。真实投产取执行 Owner，受测对象及专业结果分别取 VER。 |
| AC-PLM-FB01-004 | WP05-12，输入取 WP05-18、WP05-19 | 冻结清单核对方案和双摘要，联查源包、原要求、需求集及 ITER 精确送审输入的独立引用。 |
| AC-PLM-FB01-005 | WP05-19 | 在审包固定原来源和输入版，新的来源变化可查，采用后继走显式修订。 |
| AC-PLM-FB01-006 | WP05-12 | 冻结门核双候选双摘要的声明语义，旧通用摘要保留待核而无冻结资格。 |
| AC-PLM-FB01-007 | WP05-15 | REQ 汇总分开显示任务提交、证据充分、VER 专业 FAIL 与需求未满足。 |
| AC-PLM-FB01-008 | WP05-15，联验 WP05-20 | 整体满足性保留强制失败和复测责任。迭代采用在本 Owner 门复核相同硬约束。 |
| AC-PLM-FB01-009 | WP05-15 | 逐目标区分有依据的 N/A 与报告缺证，汇总保留两类原状态。 |
| AC-PLM-PG04-A013 | WP05-14 | 送审固定准确清单、判据和成员缺项，候选后续编辑另形成版本。 |
| AC-PLM-PG04-A014 | WP05-14 | 送审及批准门复核成员来源，拒绝通过页面标签伪造批准，保留缺项或退回事实。 |
| AC-PLM-PG04-A016 | WP05-14 | 请求撤销时记录影响与处置范围，保留既有工单引用，实际停工取执行 Owner。 |
| AC-PLM-PG04-A017 | WP05-14 | 受权批准后继要求并向 ITER、VER 记录准确影响交接和实际接收，保留原工单、库存和 VER 事实。 |
| AC-PLM-PG04-A018 | WP05-14 | 签署前复核变化后的来源版，旧就绪视图转影响复核并保留送审原件。 |
| AC-PLM-PG04-A019 | WP05-15 | 分配分别保留 ITER、VER 原请求、真实接收及完成，由各 Owner 提供后续事实。 |
| AC-PLM-PG04-A020 | WP05-15 | 传递 ACK 仅证明已发，等待 VER 实际接收或计划，保留另一工程分配的已知结果。 |
| AC-PLM-PG04-A021 | WP05-15 | 逐必需条件核对同一 Baseline 的准确计划和结果，独立保留覆盖与专业结论。 |
| AC-PLM-PG04-A022 | WP05-15 | 显示错目标或未覆盖条件，保留原关联以待适用性复核，任务完成不足以证明覆盖。 |
| AC-PLM-PG04-A023 | WP05-15 | 汇总适用性、证据完整性、专业结果和满足性四维，完整 FAIL 继续显示。 |
| AC-PLM-PG04-A024 | WP05-15，适用性事实取 WP05-24 | 后继 Baseline 缺专业适用证据时保持待评，保留原目标证据及已知失败。 |
| AC-REQ-007 | WP05-18，需求集取 WP05-14 | 从工程候选输入回查所采用的准确 RBS 及每条原成员版本。 |
| AC-REQ-008 | WP05-14 | 新版需求集沿后继影响链评估，已有工程 Baseline 保留原集合版本。 |
| AC-REQ-015 | WP05-24，后继要求取 WP05-14 | 受控放宽后重新评价适用性，现行结论按新依据形成，旧 FAIL 仍可回查。 |
| AC-REQ-016 | WP05-14 | 撤销来源生成处置与真实下游义务，活动工单原引用和执行事实继续可追。 |
| AC-REQ-011 | WP05-15 | 两个必需条件分别连接同目标的两个准确 VER 结果，再按完整覆盖汇总。 |
| AC-REQ-012 | WP05-15 | 单项 PASS 仅覆盖对应条件，另一缺项保留，整体保持待核。 |
| AC-REQ-013 | WP05-15，许可事实取 WP05-32、WP05-42 | 签署准确覆盖范围，生产用途仍由 REL 按其条件独立决定。 |
| AC-REQ-014 | WP05-15 | 强制 FAIL 时拒绝整体满足签署，显式保留失败和缺项。 |
| AC-PLM-FB02-025 | WP05-22，REL 用途取 WP05-32 | VER 计划独立声明主次验证目标和范围，另引用 REL 的 TRIAL 用途身份。 |
| AC-PLM-FB02-026 | WP05-22 | 核计划层级按实际受测对象登记，风险和评审由各自政策裁决。 |
| AC-PLM-FB02-027 | WP05-15，专业结果取 WP05-24 | REQ 分开汇总 PROCESS 证据与产品必需性能缺项，保留各自适用对象。 |
| AC-PLM-FB02-028 | WP05-15，方法判断取 WP05-24 | 复核提交满足性时的原物理试验要求，演示录像保留为原证据，所缺试验仍阻断。 |
| AC-PLM-FB02-029 | WP05-22 | 按总控裁定由验证计划接入准确语义版、显示修订记录和历史回读，语言包可复用 WP02-17。 |
| AC-PLM-FB02-030 | WP05-22 | 按总控裁定由验证计划保留旧词条版本引用，并按当前可用性策略选择新计划词条。 |
| AC-PLM-FB02-049 | WP05-24，试制许可取 WP05-32 | 设计确认前检验必需试验齐备，独立获准试制仅可用于取得所缺证据。 |
| AC-PLM-FB02-050 | WP05-24，正式用途联验 WP05-42 | 固定有证的 E1 确认范围和独立性证据，E2 保持未确认，REL 复核范围上限。 |
| AC-PLM-FB02-051 | WP05-24 | 条件确认时核验同一范围的原必需项和不可豁免 FAIL，合法变更须有准确后继依据。 |
| AC-PLM-FB02-052 | WP05-24，执行联验 WP05-23、WP05-33 | 确认签署可记录有责任人和验收条件的独立工具实例前置，实际准入仍核当前实例。 |
| AC-PLM-FB02-053 | WP05-24，原证据取 WP05-22 | 原试验仪器有效性属于当前证据充分性，未知时阻断设计确认，等待原证据核验。 |
| AC-PLM-FB02-054 | WP05-28，设计确认联验 WP05-24 | 关闭水位核对明确要求的真实暴露与现场观察；在当前确认中要求该结果时由 VER 核实。 |
| AC-PLM-PG05-X009 | WP05-24，正式用途联验 WP05-42 | 签署前重核 Quality/VER 新事实和原送审快照，关键要求转重评，REL 依据当前准确范围授权。 |

WP05-22 当前承接以上两项词典场景的接入验收。建议另立 VER 语义词典补充卡，待总控按补充卡流程登记。 [WP05-22](../../task-plans/WP05.md#wp05-22)

## 总控独立复核与同 PR 生效

- R1 `FAIL`。独立复跑与首轮执行侧的 .NET 275 项和 Vitest 145 项一致，迁移审阅通过。阻塞是活动附件待补证坐标不完整、45 项未运行原 AC 缺逐项归属。 [首轮独立复核](</D:/CP6-test-artifacts/WP05-13/coord-verify-20261127T014153130Z/COORD-VERIFY.md>)
- R2 `PASS_WITH_NITS`。在产品提交 `191a6b9e717c7529bd0bc56486ba30671a9a0f1f`、树 `2812cf73d60882d7f18ebd392ad2f3e179e47c52` 上复验阻塞项，执行侧与独立复跑的用例名和结果一致。两侧各通过 .NET 277 项和 Vitest 147 项，零失败、零跳过，Release 锁定还原与构建、`vue-tsc --build` 和 Vite 均通过。导出动作迁移 `20261126235900_EngineeringRequirementExportV1` 已在隔离 PostgreSQL 18.6 的模拟数据上验证，真实环境迁移和生产采用仍待验证。 [R2 独立复核](</D:/CP6-test-artifacts/WP05-13/coord-verify-r2-20261127T034946532Z/COORD-VERIFY-R2.md>) · [逐组证据对照](</D:/CP6-test-artifacts/WP05-13/coord-verify-r2-20261127T034946532Z/verification-comparison.json>)

| 独立复核 R2 分组 | 通过 | TRX SHA-256 |
| --- | ---: | --- |
| WP05-02 | 95/95 | `eb413ee5ccb8f84668076b36a31debfb9dd393df9a0c3a536fb4f8d505b67b88` [TRX](</D:/CP6-test-artifacts/WP05-13/coord-verify-r2-20261127T034946532Z/validation-final-r2/results-wp0502/wp05-13-wp0502.trx>) |
| WP02-32、FILE、G05 | 35/35 | `a97d1773504e71230c12be6a570627a26cc51bd83f7eecadfb3f857ea2be5996` [TRX](</D:/CP6-test-artifacts/WP05-13/coord-verify-r2-20261127T034946532Z/validation-final-r2/results-file/wp05-13-file.trx>) |
| REQ、Sales、DOC、QUALITY | 103/103 | `42f3ffabb395a07d1c7c9b91501bf6a6328e1311ad91ee40725ed62d948e7c58` [TRX](</D:/CP6-test-artifacts/WP05-13/coord-verify-r2-20261127T034946532Z/validation-final-r2/results-req/wp05-13-req.trx>) |
| WP05-03 工程，含 TC-018 | 44/44 | `5b1438501457302c2d846e6be9de72bb5b23184aa4817aef6894cc78f8481736` [TRX](</D:/CP6-test-artifacts/WP05-13/coord-verify-r2-20261127T034946532Z/validation-final-r2/results-engineering/wp05-13-engineering.trx>) |

启用 `actFile` 后，应用回退版本须具备读取该格式的兼容证据。首轮 `89b62833` 程序集在严格读取时拒绝该字段。 [DTO 字节与兼容性核对](</D:/CP6-test-artifacts/WP05-13/coord-verify-r2-20261127T034946532Z/act-wire-compatibility.json>)

总控已推送产品候选并登记 Draft PR #1341。卡级 `DONE` 在该 PR 正常合入受保护的 `main`，并核实远端包含已验收产品提交和本次台账后生效。`delivery_evidence.remote_main_sha` 固定待核的产品祖先，实际合并 SHA 以 PR 回执为准。PLM-REQ-01 与相关目标保持开放。 [PR #1341](https://github.com/GTX537/CP6/pull/1341) · [台账](../../roadmap.json)

## 同树证据与工作区

- 执行侧最终运行从产品提交 `191a6b9e717c7529bd0bc56486ba30671a9a0f1f` 独立还原并构建 .NET SDK 10.0.404 Release。隔离 PostgreSQL 18 只监听回环，用 D1 范围内的四个端口和独立 `permission-consumption-` 根。按原报告的用例名核验 WP05-02 全部 95 项及 WP05-03 工程 44 项，并执行 FILE、REQ、Sales、DOC、QUALITY 与前端专项。实际 Main API、工作台组件及 PostgreSQL 使用明确合成的租户、用户、权限、商业记录、澄清、文件和守护证明。源与生产部署状态单独保留。
- R1 统一运行记录固定同一源码提交及树、各组完整命令、脱敏环境变量、子进程设置、测试清单、TRX 与摘要。首轮证据保留供比较。 [R1 验证脚本](/D:/CP6-test-artifacts/WP05-13/exec-20261127T023915701Z-r1/run-final.ps1) · [R1 最终验证](/D:/CP6-test-artifacts/WP05-13/exec-20261127T023915701Z-r1/final-verification.json) · [R1 逐项 AC 原件](/D:/CP6-test-artifacts/WP05-13/exec-20261127T023915701Z-r1/original-acceptance-final.json) · [首轮验证](/D:/CP6-test-artifacts/WP05-13/exec-20261126T235242212Z/final-verification.json)
- 执行侧累计有效时间 **10929 秒**，首轮 6886 秒含准入 407 秒，返工 4043 秒。M 卡预算 14400 秒，强制拆分线 21600 秒。DEV-1 以 `CP6:WP05-13` 保留本地分支 `codex/wp05-13`，关联 Draft PR #1341。 [执行侧时间与停机读回](/D:/CP6-test-artifacts/WP05-13/exec-20261127T023915701Z-r1/workspace-readback.json)
