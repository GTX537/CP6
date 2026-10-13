# QA-IPQC-01 过程与最终检验前后端开发 Spec v1.0 — Recovery-R4 Systemic-R4

状态：完整设计静态候选；独立全文复审待执行。业务AC、产品DB、TypeScript、build、CI、runtime均 NOT_RUN / UNPROVEN。

本册唯一范围：Main Quality / QA-IPQC-01 / DG08 / REWIRE。原SC=无，原PLM验收=无；不得借相邻目标替代。

## SPEC-QA-IPQC-01-01 工序检验

页面：/quality/ipqc/workbench, /quality/ipqc/tasks/:taskId
连续夹具：F01, F05, F06A, F06B, F10

### Requirements

- REQ-01-01: 以完整 MES-EXEC ExecQaSource 原件、current SourceHead、QA-POL context 建立 IPQC Task；不得从显示码重造范围。
- REQ-01-02: Population/SampleSelection/MeasurementSlot 必须来自 accepted QAP wire；FULL_COUNT 每成员实际测量。
- REQ-01-03: I09 同事务重读六类 Eligibility current fact/head/authority/coverage 后保存 Observation；value/code XOR。
- REQ-01-04: I11 reviewer 与 inspector 分离，ReviewItem 精确绑定 Task/Observation revision。
- REQ-01-05: I10/I12/I13/I14 形成 Correction→Retest packet→Episode→Ruling 链；旧 FAIL 不可覆盖。
- REQ-01-06: I15 只从已复核 current Observation 冻结 ResultSet；遗漏旧 FAIL 返回 RESULTSET_INCOMPLETE_FAIL_OMITTED。
- REQ-01-07: 专业撤回与 proposed Stock ordinary-use 共用 current family/decision 门；谁先提交谁保历史，之后新用阻断。
- REQ-01-08: REAL Owner/same-UoW adoption 未证时保持 UNPROVEN，不将 DEMO 结果作为企业政策。

### Fields and actions

字段：stage, sourceRef, workOrderId, operationId, actualBaselineRef, purposeRef, policyContextRef, planInstanceRef, population, sampleSelection, eligibilityCut, slotId, value, code, unitRef, observedAt, rawRecordRefs, reviewRef, resultSetRef, currentHeads
动作：I03, I05, I09, I10, I11, I12, I13, I14, I15, I16, I17, I18, I20

### Rules

- E前缀隔离
- Owner原键+version+digest
- Decimal规范字符串
- INITIAL唯一
- Correction不可删历史
- Retest需批准episode
- 独立Review
- Outcome与slot/partition硬等式

### Processing and recovery

所有mutation按 Source/Family/Task/Decision/Delivery 锁序读取current heads，比较expected，读取Owner原件，求值后在同一UoW写domain+Command+Audit。REJECTED/UNKNOWN仅保存terminal ledger；C01用requestKey/commandId返回原terminal。

错误：STALE_HEAD, ELIGIBILITY_EXPIRED, ELIGIBILITY_REVOKED, ELIGIBILITY_AUTHORITY_MISSING, ELIGIBILITY_COVERAGE_GAP, INITIAL_ALREADY_CLAIMED, RESULTSET_INCOMPLETE_FAIL_OMITTED, FAMILY_WITHDRAWN

### Acceptance trace

- AC-01-01: NOT_RUN — REQ-01-01 mechanism is statically specified; product/business execution is not claimed.
- AC-01-02: NOT_RUN — REQ-01-02 mechanism is statically specified; product/business execution is not claimed.
- AC-01-03: NOT_RUN — REQ-01-03 mechanism is statically specified; product/business execution is not claimed.
- AC-01-04: NOT_RUN — REQ-01-04 mechanism is statically specified; product/business execution is not claimed.
- AC-01-05: NOT_RUN — REQ-01-05 mechanism is statically specified; product/business execution is not claimed.
- AC-01-06: NOT_RUN — REQ-01-06 mechanism is statically specified; product/business execution is not claimed.
- AC-01-07: NOT_RUN — REQ-01-07 mechanism is statically specified; product/business execution is not claimed.
- AC-01-08: NOT_RUN — REQ-01-08 mechanism is statically specified; product/business execution is not claimed.

## SPEC-QA-IPQC-01-02 成品检验

页面：/quality/ipqc/workbench, /quality/ipqc/tasks/:taskId, /quality/ipqc/recovery
连续夹具：F02, F04, F07

### Requirements

- REQ-02-01: MES-OUTPUT QualitySource 使用 accepted OUT wire，outputUnitRef 含 owner，receiptMappings 含 receiptSubject。
- REQ-02-02: 同 Canonical source revision1→2(C40)→3(C40+D60)复用一个 Task family；current head 决定可写版本。
- REQ-02-03: 100 Observation + 100 ReviewItem 形成80 ACCEPTED、10 HOLD、10 PENDING core outcome。
- REQ-02-04: OUT projection 唯一映射为 accepted80/rework10/unexamined10；Stock applied quantity独立显示。
- REQ-02-05: source successor保留旧Decision/ResultSet；新scope未完成Task前publicationState=NOT_PUBLISHED。
- REQ-02-06: OutQualityTaskResult 与 OutQualityDisposition 各有唯一 payload type/schema/registration。
- REQ-02-07: Owner result按canonicalKey/sequence持久；高序缺前驱保存GAP，补齐后再归约。
- REQ-02-08: Delivery UNKNOWN 通过C01原terminal查回；lease CAS后旧token零业务写。

### Fields and actions

字段：sourceKey, sourceRevision, previousRef, outputRef, outputRanges, outputUnitRef, receiptMappings.receiptSubject, policyRef, taskRef, resultSetRef, decisionRef, accepted, rework, unexamined, sampleCoverageRef, deliveryId, messageId, canonicalKey, sequence, leaseEpoch
动作：I05, I09, I11, I15, I16, I17, I18, I19, I23, I24, I26, C01

### Rules

- source revision连续
- receiptSubject原坐标
- core/projection分离
- 旧处分不冒新scope
- payload冻结先于发送
- Inbox原件不可变
- GAP前驱门
- lease epoch CAS

### Processing and recovery

所有mutation按 Source/Family/Task/Decision/Delivery 锁序读取current heads，比较expected，读取Owner原件，求值后在同一UoW写domain+Command+Audit。REJECTED/UNKNOWN仅保存terminal ledger；C01用requestKey/commandId返回原terminal。

错误：STALE_SOURCE_REVISION, SUCCESSOR_MEASUREMENT_REQUIRED, GAP_PREDECESSOR_MISSING, OWNER_RESULT_UNKNOWN, STALE_LEASE_EPOCH, PAYLOAD_SCHEMA_MISMATCH

### Acceptance trace

- AC-02-01: NOT_RUN — REQ-02-01 mechanism is statically specified; product/business execution is not claimed.
- AC-02-02: NOT_RUN — REQ-02-02 mechanism is statically specified; product/business execution is not claimed.
- AC-02-03: NOT_RUN — REQ-02-03 mechanism is statically specified; product/business execution is not claimed.
- AC-02-04: NOT_RUN — REQ-02-04 mechanism is statically specified; product/business execution is not claimed.
- AC-02-05: NOT_RUN — REQ-02-05 mechanism is statically specified; product/business execution is not claimed.
- AC-02-06: NOT_RUN — REQ-02-06 mechanism is statically specified; product/business execution is not claimed.
- AC-02-07: NOT_RUN — REQ-02-07 mechanism is statically specified; product/business execution is not claimed.
- AC-02-08: NOT_RUN — REQ-02-08 mechanism is statically specified; product/business execution is not claimed.

## SPEC-QA-IPQC-01-03 结果与处置

页面：/quality/ipqc/dispositions/:taskId, /quality/ipqc/recovery, /quality/ipqc/history
连续夹具：F01, F02, F03, F04, F06A, F06B, F07

### Requirements

- REQ-03-01: Core ResultSet quality仅 ACCEPTED/REJECTED/HOLD/PENDING。
- REQ-03-02: Decision completePartition范围互斥且并集等于current E_scope；每段绑定exact Outcome。
- REQ-03-03: HOLD仅投影HELD或REWORK；PENDING仅投影UNEXAMINED；不得创造第五core状态。
- REQ-03-04: I17冻结ApprovalPacket；I18绑定同Task/ResultSet packet与current source/mapping/family/epoch。
- REQ-03-05: withdraw successor写Decision v2与Family current，不倒改v1或已发事实。
- REQ-03-06: Command保存完整request/terminal bytes；C01逐字段返回原结果。
- REQ-03-07: 五种payload各由唯一schema严格验证，Disposition与TaskResult不可互投。
- REQ-03-08: 历史、导出、legacy隔离不补造Policy/Scope/Baseline/PASS。

### Fields and actions

字段：outcomeId, range, quality, coverageRef, segmentId, outputProjectionKind, streamId, sequence, previousDecisionRef, packetDigest, authorityRef, payloadType, payloadDigest, registrationRef, terminalType, terminalBody, terminalDigest, writerEpoch
动作：I15, I16, I17, I18, I19, I20, I25, I26, I32, I33, I34, I35, C01

### Rules

- ResultSet immutable
- Decision epoch history
- approval separation
- partition/outcome composite FK
- delivery decision all-or-none
- same key same digest replay
- same key different digest conflict

### Processing and recovery

所有mutation按 Source/Family/Task/Decision/Delivery 锁序读取current heads，比较expected，读取Owner原件，求值后在同一UoW写domain+Command+Audit。REJECTED/UNKNOWN仅保存terminal ledger；C01用requestKey/commandId返回原terminal。

错误：PARTITION_NOT_COMPLETE, OUTCOME_DIGEST_MISMATCH, PACKET_NOT_CURRENT, AUTHORITY_NOT_CURRENT, DECISION_CONFLICT, STALE_WRITER_EPOCH, LEGACY_PROOF_MISSING

### Acceptance trace

- AC-03-01: NOT_RUN — REQ-03-01 mechanism is statically specified; product/business execution is not claimed.
- AC-03-02: NOT_RUN — REQ-03-02 mechanism is statically specified; product/business execution is not claimed.
- AC-03-03: NOT_RUN — REQ-03-03 mechanism is statically specified; product/business execution is not claimed.
- AC-03-04: NOT_RUN — REQ-03-04 mechanism is statically specified; product/business execution is not claimed.
- AC-03-05: NOT_RUN — REQ-03-05 mechanism is statically specified; product/business execution is not claimed.
- AC-03-06: NOT_RUN — REQ-03-06 mechanism is statically specified; product/business execution is not claimed.
- AC-03-07: NOT_RUN — REQ-03-07 mechanism is statically specified; product/business execution is not claimed.
- AC-03-08: NOT_RUN — REQ-03-08 mechanism is statically specified; product/business execution is not claimed.

## SPEC-QA-IPQC-01-04 工程和产出关联

页面：/quality/ipqc/workbench, /quality/ipqc/engineering-handoff, /quality/ipqc/recovery
连续夹具：F02, F03, F04, F07, F08

### Requirements

- REQ-04-01: EXEC operation、MES output、receiptSubject、actualBaseline、purpose、site/batch均保存Owner原ref。
- REQ-04-02: FQC与TRIAL SourceContext分别绑定真实outputRef/sourceCoverage；不可用batchRef替代outputRef。
- REQ-04-03: 工程证据handoff只传原始证据与Quality refs，不传command wrapper。
- REQ-04-04: source correction写Impact/Wake/Family reopen；Outbox迟到也先阻旧资格新用。
- REQ-04-05: Required adapter registration、payload、delivery、owner result均保存完整ref/body/digest。
- REQ-04-06: Recovery工作台显示source successor、GAP、UNKNOWN、lease、dependency与current gate。

### Fields and actions

字段：canonicalSourceRef, sourceHeadRef, outputRef, receiptSubject, actualBaselineRef, purposeRef, siteRef, batchRef, coverageRanges, impactRef, dependencyWakeRef, deliveryRef, ownerResultRef, currentness
动作：I02, I03, I19, I23, I24, I25, I26, I29

### Rules

- 原坐标不替代
- 源/输出/样本/coverage不可互换
- 影响先阻门
- 外部结果先Inbox
- UNKNOWN不推无效果

### Processing and recovery

所有mutation按 Source/Family/Task/Decision/Delivery 锁序读取current heads，比较expected，读取Owner原件，求值后在同一UoW写domain+Command+Audit。REJECTED/UNKNOWN仅保存terminal ledger；C01用requestKey/commandId返回原terminal。

错误：SOURCE_REF_UNRESOLVED, OUTPUT_REF_MISMATCH, RECEIPT_SUBJECT_MISMATCH, BASELINE_MISMATCH, OWNER_RESULT_GAP, DEPENDENCY_PENDING

### Acceptance trace

- AC-04-01: NOT_RUN — REQ-04-01 mechanism is statically specified; product/business execution is not claimed.
- AC-04-02: NOT_RUN — REQ-04-02 mechanism is statically specified; product/business execution is not claimed.
- AC-04-03: NOT_RUN — REQ-04-03 mechanism is statically specified; product/business execution is not claimed.
- AC-04-04: NOT_RUN — REQ-04-04 mechanism is statically specified; product/business execution is not claimed.
- AC-04-05: NOT_RUN — REQ-04-05 mechanism is statically specified; product/business execution is not claimed.
- AC-04-06: NOT_RUN — REQ-04-06 mechanism is statically specified; product/business execution is not claimed.

## SPEC-QA-IPQC-01-05 原始试验结果与PLM设计评价分权

页面：/quality/ipqc/engineering-handoff, /quality/ipqc/tasks/:taskId
连续夹具：F03, F08

### Requirements

- REQ-05-01: Quality拥有原测量、Review、ResultSet、Decision；PLM-VER拥有TrialAuthorization/工程评价。
- REQ-05-02: PLM-DOC只提供受权不可变文档ref/version/hash；NOT_AUTHORIZED不复制正文。
- REQ-05-03: RawEvidenceHandoff分别绑定source/testExecution/sample/baseline/method/environment/raw/task/result/decision/coverage/document。
- REQ-05-04: TrialAuthorization按site/batch/purpose/quantity/validity/current head求值；到期、换baseline、量产用途拒绝。
- REQ-05-05: FOUND/UNKNOWN/UNAVAILABLE/NOT_AUTHORIZED四态保真；UNKNOWN不能推断不存在。
- REQ-05-06: REAL与DEMO物理隔离；真实Owner adoption缺失时返回OWNER_PROTOCOL_NOT_READY。

### Fields and actions

字段：trialAuthorizationRef, verificationPlanRef, evaluationRef, documentRef, testExecutionRef, sampleRefs, actualBaselineRef, methodVersionRefs, environmentRefs, rawRecordRefs, contentHashes, qualityTaskRef, resultSetRef, decisionRef, coverageRanges
动作：I27, I28, I29, I30, I31

### Rules

- Owner固定PLM-VER/PLM-DOC
- Quality PASS不签PLM PASS
- PLM评价不改Quality
- 无权限不复制
- REAL adoption门

### Processing and recovery

所有mutation按 Source/Family/Task/Decision/Delivery 锁序读取current heads，比较expected，读取Owner原件，求值后在同一UoW写domain+Command+Audit。REJECTED/UNKNOWN仅保存terminal ledger；C01用requestKey/commandId返回原terminal。

错误：TRIAL_AUTH_EXPIRED, TRIAL_BASELINE_MISMATCH, TRIAL_PURPOSE_NOT_AUTHORIZED, OWNER_PROTOCOL_NOT_READY, NOT_AUTHORIZED, UNAVAILABLE

### Acceptance trace

- AC-05-01: NOT_RUN — REQ-05-01 mechanism is statically specified; product/business execution is not claimed.
- AC-05-02: NOT_RUN — REQ-05-02 mechanism is statically specified; product/business execution is not claimed.
- AC-05-03: NOT_RUN — REQ-05-03 mechanism is statically specified; product/business execution is not claimed.
- AC-05-04: NOT_RUN — REQ-05-04 mechanism is statically specified; product/business execution is not claimed.
- AC-05-05: NOT_RUN — REQ-05-05 mechanism is statically specified; product/business execution is not claimed.
- AC-05-06: NOT_RUN — REQ-05-06 mechanism is statically specified; product/business execution is not claimed.

## Original process / handoff / parameter expansion

### P07 计划至制造执行

原状态：APPROVED_SCOPE。IPQC边界：MES-EXEC operation source enters IPQC; Quality never rewrites WO/report/material facts.
字段：ExecQaSource.sourceId/revision, workOrderId, operationId, scope, actualManifestRef, actualBaselineRef, inspectionPolicyRef
过程：I03 resolve → I05 bind task → I09/I11 measure/review → I15/I18 freeze/approve → MES-EXEC result delivery
错误：SOURCE_REF_UNRESOLVED, STALE_HEAD, OWNER_PROTOCOL_NOT_READY；AC-P07 NOT_RUN

### P09 制造至合格入库与成本

原状态：APPROVED_SCOPE。IPQC边界：MES-OUTPUT FQC source and Quality disposition remain distinct from WMS application and Finance value.
字段：OutQualitySource.outputRef/ranges/unit, receiptMappings.receiptSubject, OutQualityTaskResult, OutQualityDisposition, StockConsumerResult
过程：source revision 1→2→3 → FQC task/result/decision → five-array projection → same-UoW Stock current guard
错误：GAP_PREDECESSOR_MISSING, FAMILY_WITHDRAWN, STALE_LEASE_EPOCH；AC-P09 NOT_RUN

### P18 工程验证、受控试制与设计确认

原状态：NEW_INTEGRATION_ID_REVIEW。IPQC边界：TRIAL Quality evidence is handed to PLM evaluation; Quality PASS and engineering evaluation remain separate.
字段：TrialAuthorization, verificationPlanRef, testExecutionRef, sampleRefs, actualBaselineRef, rawRecordRefs, qualityDecisionRef
过程：RequiredFenced reads → TRIAL eligibility → Quality decision → RawEvidenceHandoff → PLM result observation
错误：TRIAL_AUTH_EXPIRED, TRIAL_BASELINE_MISMATCH, OWNER_PROTOCOL_NOT_READY；AC-P18 NOT_RUN

### H20 生产产出形成成品收货依据

原状态：APPROVED_DESIGN。Owner边界：MES owns output; WMS owns receipt; Quality observes exact output/receipt mappings
最小输入：OutputId, WO/final operation/batch, Revision, incremental Qty, acceptance policy ref
成功凭证：WMS receipt references exact OutputId; pending receipt remains explicit
实现：OutQualitySource.outputRef, outputRanges, receiptMappings.receiptSubject, actualBaselineRef, purposeRef
错误：OUTPUT_REF_MISMATCH；AC-H20 NOT_RUN

### H22 收货/生产触发检验任务

原状态：APPROVED_DESIGN。Owner边界：Quality owns inspection task
最小输入：InspectionSourceId/Line, Lot or OutputId, Qty, InspectionPolicyVersion, ranges
成功凭证：Quality task or authorized exemption; unknown policy never means skip
实现：I03, I05, PolicyTaskContext, PlanInstance, TaskKey/UQ
错误：OWNER_REF_UNRESOLVED；AC-H22 NOT_RUN

### H23 质量判定驱动可用性与商业验收投影

原状态：APPROVED_DESIGN。Owner边界：Quality owns decision; WMS owns availability; PUR/MES own projections
最小输入：DispositionId/Version, inspection source, accepted/rejected/held/rework/unexamined ranges, approval
成功凭证：each owner returns its own application result; pending is visible
实现：I18/I20, completePartition, five payload schemas, F15 same-UoW guard
错误：FAMILY_WITHDRAWN；AC-H23 NOT_RUN

### H42 验证计划与试制用途授权

原状态：DETAIL_FROM_APPROVED_PLM_REVIEW。Owner边界：PLM-VER owns verification/trial authorization
最小输入：VerificationPlanId/Version, BaselineId, requirements/method/criteria, Site, quantity/batch, purpose, validity, approval
成功凭证：versioned TrialAuthorization; production purpose remains separate
实现：I27/I28/I30/I31 read-only, TrialAuthorization current gate, REAL OWNER_PROTOCOL_NOT_READY
错误：TRIAL_AUTH_EXPIRED；AC-H42 NOT_RUN

### H44 试制与试验原始证据进入方案评价

原状态：DETAIL_FROM_APPROVED_PLM_REVIEW。Owner边界：source Owner owns raw data; PLM owns engineering evaluation; QA-IPQC owns Quality facts
最小输入：WO/Output/sample, actual baseline, TestExecution, method/environment, raw refs/hashes
成功凭证：coverage/evaluation/problem refs without copying a second Quality ledger
实现：I29 RawEvidenceHandoff, PLM RequiredFenced four states, source/sample/output/coverage distinction
错误：NOT_AUTHORIZED；AC-H44 NOT_RUN

### DP02 数量、UOM、预留与线边模型

批准方向：受控线边为标准制造方向；Reservation占用一次，Allocation仅分解该占用；不使用二次扣Available公式。
待细化：状态桶互斥、单位换算版本、增量/累计报工、允许精度和舍入、材料消耗模式矩阵。
PLM overlay：无
落实：canonical decimal strings, versioned UOM refs, half-open exact ranges, Quality does not compute Stock available
AC-DP02 NOT_RUN

### DP07 检验、免检与部分判定

批准方向：缺结果不PASS；明确策略豁免可接受；判定与WMS应用分开留证。
待细化：策略版本、生效范围、抽样/全检配置、部分判定份额、更正和让步审批、既有待检库存准入。
PLM overlay：无
落实：QAP REQUIRED/NONE/EXEMPT/UNKNOWN, CoreQuality four states, OUT five-array projection, correction/retest/withdraw
AC-DP07 NOT_RUN

### DP11 存量单据延续与切换批次

批准方向：存量不自动重置状态、补造工单/QC/价格/回执；新旧执行不能双写同一事实。
待细化：存量类别、允许继续旧链/转换/人工处置的规则、切换单位、读兼容、前滚/回退条件与授权清单。
PLM overlay：同时核验完整PLM用途、技术基线、试制、变更、文档和下游回执。
落实：WriterEpoch prepare/activate/retire, legacy read-only routes, no synthesized old PASS, old writer rejected
AC-DP11 NOT_RUN

### DP14 试制授权、验证与样件追踪

批准方向：试制与量产用途分开；真实受测基线和原始试验结果保留；旧PASS不自动复用。
待细化：授权额度占用/回收、期限时间源、Site/批次范围、样件UOM、证据方法、失效与复测/复用规则。
PLM overlay：APR-CP6-PLM-001
落实：TrialAuthorization scope/time/baseline/purpose, sample/output/source/coverage separate refs, RawEvidenceHandoff
AC-DP14 NOT_RUN

### DP15 工程变更、偏离与执行回执

批准方向：各Owner决定自己的处置；发布/通知不等于变更完成；偏离有限且不代替质量授权。
待细化：影响清单水位、实施项状态、同基线并发合并、冻结/竞争、关闭例外、偏离到期与已发生事实处理。
PLM overlay：APR-CP6-PLM-001
落实：source correction Impact/Wake/REOPEN, owner result Inbox/GAP, Quality decision immutable history
AC-DP15 NOT_RUN

### DP16 发布、生效、采用和技术退役

批准方向：技术内容、用途授权、目标接收、就绪与实际采用分层；反馈闭环，停用不删历史。
待细化：目标清单、用途/日期/序列生效、规则版本、回执单调性、效果指标、替代兼容和旧版支持矩阵。
PLM overlay：APR-CP6-PLM-001
落实：registration/payload/delivery/owner result separated, WriterEpoch history, RequiredFenced adoption UNPROVEN
AC-DP16 NOT_RUN

## Mandatory interface field mapping annex

`QA-IPQC-01_INTERFACE-FIELD-MAPPING.md`（SHA256 `0c158b7e7765ccb71565bb0bcbeac8a62c638a808e693343f900cab5f288d585`）为本册规范成员；EXEC/OUT/QAP/PLM字段、四态→五数组映射、revision域和错误处理逐行适用。任何冲突以accepted Owner wire与该逐字段闭包为静态设计门。

## Mutation API contract matrix

|Code|Method|Path|Actor|Request|Response|Result|Actual branch evidence|
|---|---|---|---|---|---|---|---|
|I03|POST|/api/quality/ipqc/v1/policy-contexts/resolve|QA_IPQC_PLANNER|PolicyResolveCommand|PolicyResolveOutcome|QapPolicyTaskContext|F02-01, F03-01|
|I05|POST|/api/quality/ipqc/v1/tasks|QA_IPQC_PLANNER|CreateTaskCommand|CreateTaskOutcome|TaskDetail|F02-02, F03-02|
|I09|POST|/api/quality/ipqc/v1/tasks/{taskId}/observations|QA_IPQC_INSPECTOR|ObservationBatchCommand|ObservationOutcome|ObservationCommitResult|F02-03, F03-03, F05-01, F05-02, F05-09, F08-01, F10-01, F10-02, F10-03, F10-04, F10-05|
|I10|POST|/api/quality/ipqc/v1/tasks/{taskId}/observations/{observationId}/corrections|QA_IPQC_INSPECTOR|CorrectionCommand|CorrectionOutcome|CorrectionCommitResult|F05-05|
|I11|POST|/api/quality/ipqc/v1/tasks/{taskId}/reviews|QA_IPQC_REVIEWER|ReviewCommand|ReviewOutcome|ReviewCommitResult|F02-04, F03-04, F05-03|
|I12|POST|/api/quality/ipqc/v1/tasks/{taskId}/retest-proposals|QA_IPQC_PLANNER|RetestProposalCommand|RetestProposalOutcome|RetestProposalResult|F05-06|
|I13|POST|/api/quality/ipqc/v1/tasks/{taskId}/retest-episodes|QA_IPQC_APPROVER|ActivateRetestCommand|RetestActivationOutcome|RetestActivationResult|F05-07|
|I14|POST|/api/quality/ipqc/v1/tasks/{taskId}/retest-episodes/{episodeId}/rulings|QA_IPQC_APPROVER|RulingCommand|RulingOutcome|RulingCommitResult|F05-08|
|I15|POST|/api/quality/ipqc/v1/tasks/{taskId}/result-sets|QA_IPQC_REVIEWER|FreezeResultSetCommand|FreezeResultSetOutcome|ResultSet|F02-05, F03-05, F05-04, F05-10|
|I16|POST|/api/quality/ipqc/v1/tasks/{taskId}/disposition-drafts|QA_IPQC_PLANNER|DraftCommand|DraftOutcome|DispositionDraftResult|F02-06, F03-06|
|I17|POST|/api/quality/ipqc/v1/tasks/{taskId}/approval-packets|QA_IPQC_PLANNER|FreezePacketCommand|PacketOutcome|ApprovalPacketResult|F02-07, F03-07|
|I18|POST|/api/quality/ipqc/v1/tasks/{taskId}/approvals|QA_IPQC_APPROVER|ApprovalCommand|ApprovalOutcome|Decision|F02-08, F02-09, F03-08|
|I19|POST|/api/quality/ipqc/v1/decisions/{decisionId}/successors|QA_IPQC_APPROVER|SuccessorCommand|SuccessorOutcome|Decision|F04-02|
|I20|POST|/api/quality/ipqc/v1/decisions/{decisionId}/withdraw|QA_IPQC_APPROVER|WithdrawCommand|WithdrawOutcome|Decision|F06A-01, F06B-02|
|I23|POST|/api/quality/ipqc/v1/sources/{canonicalId}/reassess|QA_IPQC_REVIEWER|SourceReassessmentCommand|ImpactOutcome|SourceReassessmentResult|F04-01|
|I24|POST|/api/quality/ipqc/v1/owner-results|QA_IPQC_ADAPTER|OwnerResultCommand|IngressOutcome|OwnerIngressResult|F07-03|
|I26|POST|/api/quality/ipqc/v1/deliveries/{deliveryId}/resume|QA_IPQC_RECOVERY|DeliveryResumeCommand|DeliveryOutcome|FrozenDelivery|F07-01, F07-04, F07-05|
|I29|POST|/api/quality/ipqc/v1/plm/evidence-handoffs|QA_IPQC_REVIEWER|HandoffCommand|HandoffOutcome|FrozenDelivery|F03-09, F03-10, F03-11, F03-12, F08-06|
|I33|POST|/api/quality/ipqc/v1/exports|QA_IPQC_AUDITOR|ExportCommand|ExportOutcome|ExportReceipt|F09-07|
|I35|POST|/api/quality/ipqc/v1/legacy/{legacyId}/map|QA_IPQC_MIGRATION|LegacyMapCommand|LegacyMapOutcome|LegacyMapResult|F09-05|

## Facade contract matrix

|Code|Name|Kind|Input|Result|Runtime adoption|
|---|---|---|---|---|---|
|F01-QAP-GUARD|PolicyFinalGuard|ACCEPTED_QAP_SAME_UOW|QapFinalGuardInput|QapFinalGuardResult|DESIGN_ONLY|
|F02-QAP-USE|RecordUse|ACCEPTED_QAP_SAME_UOW|QapUseFact|QapUseResult|DESIGN_ONLY|
|F03-FAMILY-READ|ReadCurrentFamilyCut|ACCEPTED_QAP_CURRENT_READ|QapFamilyKey|QapFamilyReadResult|DESIGN_ONLY|
|F04-FAMILY-COMMIT|CommitFamilyControl|ACCEPTED_QAP_SAME_UOW|QapFamilyControlChange|QapFamilyChangeResult|DESIGN_ONLY|
|F05-IPQC-GUARD|IPQCFinalCurrentGuard|IN_PROCESS_CURRENT_GUARD|Decision|IpqcFinalGuardResult|DESIGN_ONLY|
|F06-EFFECT-USE|RecordEffectUse|ACCEPTED_QAP_SAME_UOW|QapUseFact|QapUseResult|DESIGN_ONLY|
|F07-OUT-TASK-PUBLISH|PublishOutputQualityTaskResult|OUTBOUND_STRICT_PAYLOAD|OutQualityTaskResult|FrozenDelivery|DESIGN_ONLY|
|F08-OUT-DISPOSITION-PUBLISH|PublishOutputQualityDisposition|OUTBOUND_STRICT_PAYLOAD|OutQualityDisposition|FrozenDelivery|DESIGN_ONLY|
|F09-EXEC-TASK-PUBLISH|PublishExecQualityTaskResult|PROPOSED_REQUIRED_ADAPTER|ExecQualityTaskResult|FrozenDelivery|UNPROVEN|
|F10-EXEC-DISPOSITION-PUBLISH|PublishExecQualityDisposition|PROPOSED_REQUIRED_ADAPTER|ExecQualityDisposition|FrozenDelivery|UNPROVEN|
|F11-DELIVERY-LOOKUP|LookupOriginalDeliveryResult|OUTBOUND_LOOKUP|QapEvidence|FrozenDelivery|DESIGN_ONLY|
|F12-DELIVERY-LEASE|AcquireDeliveryLease|IN_PROCESS_LEASE_CAS|DeliveryResumeCommand|DeliveryLeaseResult|DESIGN_ONLY|
|F13-INBOX-REDUCE|ReduceInboxPredecessorChain|IN_PROCESS_OWNER_REDUCTION|OwnerResultCommand|OwnerObservationResult|DESIGN_ONLY|
|F14-WRITER-EPOCH|CheckWriterEpoch|IN_PROCESS_WRITER_EPOCH|WriterEpochTransitionCommand|WriterEpochResult|DESIGN_ONLY|
|F15-PROPOSED-STOCK-QAP-ADAPTER|GuardProposedOrdinaryStockUse|PROPOSED_REQUIRED_ADAPTER|StockOrdinaryUseCommand|StockConsumerResult|UNPROVEN|
|F16-ELIGIBILITY-CURRENT-READ|ReadEligibilityOwnerCurrent|OWNER_CURRENT_READ_SAME_UOW|EligibilityCut|Eligibility|DESIGN_ONLY|
|F17-FIXTURE-PREREQUISITE-READ|ReadFixedFixturePrerequisites|FIXTURE_INPUT_READ|QapEvidence|FixturePrerequisiteBundle|DESIGN_ONLY|
|F18-ADAPTER-REGISTRATION-READ|ReadRequiredAdapterRegistration|REQUIRED_ADAPTER_READ|QapEvidence|QapAdoptionRegistration|UNPROVEN|
|F19-TYPED-DISPOSITION-PROJECT|ProjectTypedDisposition|TYPED_WIRE_PROJECTION|Decision|OutQualityDisposition|DESIGN_ONLY|
|F20-RESULTSET-COVERAGE-READ|ReadResultSetCoverageProof|TYPED_CURRENT_PROOF_READ|QapEvidence|ResultSetCoverageProof|DESIGN_ONLY|
|F21-TRIAL-OUT-SOURCE-READ|ReadTrialOutSource|TYPED_FIXED_OWNER_CURRENT_READ|OutEvidence|TrialSourceReadResult|DESIGN_ONLY|

## Relational table matrix

|Table|Primary key|FK count|Append-only|Purpose|
|---|---|---:|---|---|
|AdapterRegistration|TenantId, EnvironmentId, Mode, RegistrationId, Revision|0|true|immutable revision/evidence|
|Approval|TenantId, EnvironmentId, Mode, StreamId, DecisionId, DecisionVersion|7|true|immutable revision/evidence|
|ApprovalPacket|TenantId, EnvironmentId, Mode, PacketId, Version|2|true|immutable revision/evidence|
|AuditEvent|TenantId, EnvironmentId, Mode, AuditId|1|true|immutable revision/evidence|
|Command|TenantId, EnvironmentId, Mode, CommandId|0|true|immutable revision/evidence|
|Decision|TenantId, EnvironmentId, Mode, StreamId, DecisionId, Version|6|true|immutable revision/evidence|
|DecisionHead|TenantId, EnvironmentId, Mode, StreamId|1|false|current head/CAS or operational state|
|DecisionPartitionRange|TenantId, EnvironmentId, Mode, StreamId, DecisionId, DecisionVersion, SegmentId|5|true|immutable revision/evidence|
|DecisionStream|TenantId, EnvironmentId, Mode, StreamId|1|false|current head/CAS or operational state|
|Delivery|TenantId, EnvironmentId, Mode, DeliveryId|9|true|immutable revision/evidence|
|DeliveryAttempt|TenantId, EnvironmentId, Mode, DeliveryId, AttemptId|1|true|immutable revision/evidence|
|DeliveryLease|TenantId, EnvironmentId, Mode, DeliveryId|1|false|current head/CAS or operational state|
|DeliveryObservationHead|TenantId, EnvironmentId, Mode, DeliveryId|1|false|current head/CAS or operational state|
|DeliveryObservationRevision|TenantId, EnvironmentId, Mode, DeliveryId, ObservationRevision|1|true|immutable revision/evidence|
|DeliveryPayload|TenantId, EnvironmentId, Mode, PayloadId|1|true|immutable revision/evidence|
|DependencyWake|TenantId, EnvironmentId, Mode, WakeId|0|true|immutable revision/evidence|
|DispositionDraft|TenantId, EnvironmentId, Mode, DraftId, Revision|2|true|immutable revision/evidence|
|EligibilityCut|TenantId, EnvironmentId, Mode, CutId, CutVersion|1|true|immutable revision/evidence|
|EligibilityCutFact|TenantId, EnvironmentId, Mode, CutId, CutVersion, FactOrdinal|1|true|immutable revision/evidence|
|EvaluatedRangeOutcome|TenantId, EnvironmentId, Mode, OutcomeId|1|true|immutable revision/evidence|
|ExportReceipt|TenantId, EnvironmentId, Mode, ExportId|0|true|immutable revision/evidence|
|FamilyControlHead|TenantId, EnvironmentId, Mode, FamilyId|1|false|current head/CAS or operational state|
|FamilyControlVersion|TenantId, EnvironmentId, Mode, FamilyId, ControlRevision|1|true|immutable revision/evidence|
|FamilyCutEvidence|TenantId, EnvironmentId, Mode, CutId, CutVersion|1|true|immutable revision/evidence|
|FamilyDirectoryHead|TenantId, EnvironmentId, Mode, DomainKeyDigest, Stage|1|false|current head/CAS or operational state|
|FamilyDirectoryMember|TenantId, EnvironmentId, Mode, DomainKeyDigest, Stage, DirectoryRevision, FamilyId|1|true|immutable revision/evidence|
|FamilyDirectoryVersion|TenantId, EnvironmentId, Mode, DomainKeyDigest, Stage, DirectoryRevision|0|true|immutable revision/evidence|
|FamilyObligationSegment|TenantId, EnvironmentId, Mode, FamilyId, ControlRevision, ObligationId, SegmentId|1|true|immutable revision/evidence|
|ImpactCase|TenantId, EnvironmentId, Mode, ImpactId, Revision|0|true|immutable revision/evidence|
|Inbox|TenantId, EnvironmentId, Mode, Owner, CanonicalKey, Sequence|0|true|immutable revision/evidence|
|InitialClaim|TenantId, EnvironmentId, Mode, TaskId, SlotId|2|true|immutable revision/evidence|
|InspectionTask|TenantId, EnvironmentId, Mode, TaskId|1|false|current head/CAS or operational state|
|LegacyMapping|TenantId, EnvironmentId, Mode, LegacyOwner, LegacyId|1|true|immutable revision/evidence|
|MeasurementSlot|TenantId, EnvironmentId, Mode, TaskId, SlotId|2|true|immutable revision/evidence|
|Observation|TenantId, EnvironmentId, Mode, ObservationId, Revision|3|true|immutable revision/evidence|
|ObservationHead|TenantId, EnvironmentId, Mode, ObservationId|2|false|current head/CAS or operational state|
|ObservationReview|TenantId, EnvironmentId, Mode, ReviewId, Revision|2|true|immutable revision/evidence|
|ObservationReviewItem|TenantId, EnvironmentId, Mode, ReviewId, Revision, ObservationId|5|true|immutable revision/evidence|
|ObservationRuling|TenantId, EnvironmentId, Mode, TaskId, RulingId|2|true|immutable revision/evidence|
|Outbox|TenantId, EnvironmentId, Mode, MessageId|3|true|immutable revision/evidence|
|OwnerObservationHead|TenantId, EnvironmentId, Mode, Owner, CanonicalKey, Sequence|1|false|current head/CAS or operational state|
|OwnerObservationRevision|TenantId, EnvironmentId, Mode, Owner, CanonicalKey, Sequence, ObservationRevision|1|true|immutable revision/evidence|
|OwnerResultOriginal|TenantId, EnvironmentId, Mode, Owner, CanonicalKey, Sequence|1|true|immutable revision/evidence|
|PlmEvidenceHandoff|TenantId, EnvironmentId, Mode, HandoffId, Revision|0|true|immutable revision/evidence|
|PopulationMember|TenantId, EnvironmentId, Mode, TaskId, MemberId|1|true|immutable revision/evidence|
|QapConsumerResult|TenantId, EnvironmentId, Mode, ConsumerOwner, ConsumerResultId, ConsumerResultVersion|0|true|immutable revision/evidence|
|QapGuardEvidence|TenantId, EnvironmentId, Mode, GuardId, Revision|2|true|immutable revision/evidence|
|QapUseFact|TenantId, EnvironmentId, Mode, UseKey|3|true|immutable revision/evidence|
|RequirementOutcome|TenantId, EnvironmentId, Mode, ResultSetId, Revision, RequirementId|1|true|immutable revision/evidence|
|ResultSet|TenantId, EnvironmentId, Mode, ResultSetId, Revision|1|true|immutable revision/evidence|
|ResultSetHead|TenantId, EnvironmentId, Mode, TaskId|2|false|current head/CAS or operational state|
|ResultSetSlot|TenantId, EnvironmentId, Mode, ResultSetId, Revision, SlotId|4|true|immutable revision/evidence|
|RetestEpisode|TenantId, EnvironmentId, Mode, TaskId, EpisodeId|1|true|immutable revision/evidence|
|ReviewHead|TenantId, EnvironmentId, Mode, TaskId|2|false|current head/CAS or operational state|
|SampleSelectionRoster|TenantId, EnvironmentId, Mode, TaskId, RequirementId, MemberId|1|true|immutable revision/evidence|
|SourceAlias|TenantId, EnvironmentId, Mode, SourceInternalId, AliasOwner, AliasId, AliasVersion|1|true|immutable revision/evidence|
|SourceHead|TenantId, EnvironmentId, Mode, SourceInternalId|3|false|current head/CAS or operational state|
|SourceMappingVersion|TenantId, EnvironmentId, Mode, SourceInternalId, MappingRevision|1|true|immutable revision/evidence|
|SourceOriginalVersion|TenantId, EnvironmentId, Mode, SourceInternalId, OwnerRevision|0|true|immutable revision/evidence|
|StockConsumerResult|TenantId, EnvironmentId, Mode, ConsumerResultId, ConsumerResultVersion|2|true|immutable revision/evidence|
|StockEffect|TenantId, EnvironmentId, Mode, EffectId|1|true|immutable revision/evidence|
|TaskHead|TenantId, EnvironmentId, Mode, TaskId|1|false|current head/CAS or operational state|
|TaskRevision|TenantId, EnvironmentId, Mode, TaskId, TaskRevision|1|true|immutable revision/evidence|
|WriterEpochHead|TenantId, EnvironmentId, Mode, WriterName|1|false|current head/CAS or operational state|
|WriterEpochHistory|TenantId, EnvironmentId, Mode, WriterName, Epoch|0|false|current head/CAS or operational state|
|ResultInvalidation|TenantId, EnvironmentId, Mode, InvalidationId|1|true|immutable revision/evidence|
|AuthorityEvidence|TenantId, EnvironmentId, Mode, AuthorityOwner, AuthorityId, AuthorityVersion|0|true|immutable revision/evidence|
|ApprovalEvidence|TenantId, EnvironmentId, Mode, ApprovalOwner, ApprovalId, ApprovalVersion|3|true|immutable revision/evidence|

## Frontend routes and action implementation

|Route|Component|Menu|IAM|
|---|---|---|---|
|/quality/ipqc/workbench|IpqcWorkbenchPage|quality.ipqc.workbench|QA_IPQC_VIEWER|
|/quality/ipqc/tasks/:taskId|IpqcTaskDetailPage|quality.ipqc.tasks|QA_IPQC_INSPECTOR|
|/quality/ipqc/dispositions/:taskId|IpqcDispositionPage|quality.ipqc.dispositions|QA_IPQC_APPROVER|
|/quality/ipqc/engineering-handoff|IpqcEngineeringHandoffPage|quality.ipqc.engineering|QA_IPQC_ENGINEERING_REVIEWER|
|/quality/ipqc/recovery|IpqcRecoveryPage|quality.ipqc.recovery|QA_IPQC_OPERATOR|
|/quality/ipqc/history|IpqcHistoryPage|quality.ipqc.history|QA_IPQC_AUDITOR|

每个mutation action在 `FRONTEND_CONTRACT_RECOVERY_R4_SYSTEMIC.json` 中给出真实request/response、leaf字段 required/nullable/enum/format/pattern/Owner/currentness、client method、router/menu/IAM、page states、terminal receipt和C01恢复入口。隐藏按钮不替代服务端授权。

这些路径是 DESIGN_CODEPOINT_CONTRACT_ONLY；本设计包未交付或声称已存在对应 TS/Vue 源文件，frontend build=NOT_RUN。旧 `/mes/quality-inspection` 与 `/mes/quality-inspection-list` 在 WriterEpoch 启用后固定为 LEGACY_READ_ONLY，分别导航到新任务详情与历史搜索；旧 mutation 禁用，必须有 LegacyMapping 才展示，未知字段不补造。

## Cross-SPEC canonical contracts

五SPEC共享同一 Schema registry、API/facade registry、68-table relational model、continuous fixture ledger与20-action前端合同。CoreQuality严格四态；OUT projection五数组只由PartitionSegment判别映射。Owner ref保存owner/id/version/digest并解析固定body。

I18批准提交在同一UoW写入ApprovalEvidence与Approval；ApprovalEvidence对完整approval identity/digest、approver、Decision/Packet、Authority identity/digest建立复合唯一键，Approval用同列复合外键引用该唯一事实，并保留CommandActor、DecisionPacket、AuthorityEvidence三条收敛约束。前端只能提交批准意图并读取服务端receipt，不能拆分或拼装这些物理事实。

F01使用独立PHYSICALIDENTITY检查点；F02–F10使用CONTINUOUS_FIXTURES_RECOVERY_R4.json。所有source/body、API request/response、ResultSet/Decision、payload与关系行由同一systemic生成器和validator核对。

## Boundary

REAL专业政策、IAM采用、PLM-VER/PLM-DOC运行协议、same-UoW Owner能力、产品迁移、数据库编译、前端编译、业务AC、部署均未运行或未证明。DEMO输入不得外推为生产采用。

