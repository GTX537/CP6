# INT-OPS-01 集成异常与受控重放

## 1. 业务目的、操作者与权威边界

业务 Owner 的恢复操作员查询失败原件、确认原业务结果与当前资格，再申请一次保持原身份的恢复；只读支持人员看安全元数据，审计人员看获准证据，worker 只执行已记录的 Owner 命令。各接收业务 Owner 保留原队列、journal、业务事实和提交权；Platform 提供通用运输原语，不能集中转写业务数据库。 [规范 L124](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:124>)、[规范 L127](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:127>)、[规范 L128](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:128>)、[规范 L129](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:129>)、[规范 L130](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:130>)、[规范 L169](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:169>)

五条原 SPEC 是死信查询、业务拒绝与技术失败、重放资格、审计、原结果对账。技术投递状态与业务 Owner 结果独立：死信不代表业务未生效，UNKNOWN 不等于失败，查不到不是未应用证明，旧 COMPENSATED 仅表示历史台账标记，不能变成反向业务操作成功。 [规范 L141](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:141>)、[规范 L142](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:142>)、[规范 L144](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:144>)、[规范 L146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:146>)、[规范 L424](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:424>)、[规范 L456](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:456>)、[规范 L493](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:493>)、[规范 L527](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:527>)、[规范 L559](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:559>)

## 2. 当前有效组合、接受与历史边界

本册采用 S6 v0.2 的同一份完整正文，两个 Target 各保留原五条 SPEC。正式 CURRENT 是 Stage 100 的 Markdown 静态详设接受；作者包中 STOPPED／NOT_ACCEPTED 和旧 v0.1 RETURN 保持历史原样，不能用它们覆盖后继接受。接受人是用户授权的 root／COORD，记录明确 `UserPersonallySigned=false`，也不授权实现、测试或上线。[CURRENT L4](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/12/123296d148d2561f__CURRENT.json:4>)、[UA L177](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c3b8bae0817a061f__UA-20261008-S6-INT-OPS-MD02.json:177>)

| 材料 | SHA-256 | 作用 | 入口 |
| --- | --- | --- | --- |
| 共同正文 | d9b57a039e18a689615cfb48e181bb248dedecf957d8bdd7dd72d85a53a6dccc | 当前十条 SPEC 和共同接缝 | [原件 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:1>) |
| 定点复审 | a9515be614e35dd514b18c59f8748350a3dc679cacdf55a38c93817f8c41cbbe | B01–B04 静态关闭；真实 Owner 采用未证 | [原件 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/a9/a9515be614e35dd5__S6_身份与集成异常_v0.2_定点复审报告.md:1>) |
| 本目标 CURRENT | 123296d148d2561f939e86f37e193dd1dcb1207c5d2e99617785f453d9d9a267 | 冻结正文、UA、独审、接受范围 | [原件 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/12/123296d148d2561f__CURRENT.json:1>) |
| 本目标 UA | c3b8bae0817a061f29e9956d1166cef605a1738507c60560c8eeb8aacac4b4c9 | 重建既有接受记录；不是遗失临时 UA 的同字节副本 | [原件 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c3b8bae0817a061f__UA-20261008-S6-INT-OPS-MD02.json:1>) |
| 共享来源清单 | 386d6787b61a21b0c1df6be81bb9b089977232aa76687b7b2bce34a82e11c49c | S6／X1 历史及原件字节身份 | [原件 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/38/386d6787b61a21b0__SOURCE-MANIFEST.json:1>) |

原 S6 v0.2 ZIP SHA-256 `88e7050f62a7ca4b87ee140aeba0badf0659cda626790e9cf167de9965b0f80b` 共 125 个成员已只读展开到缓存；其中 99 份固定 SHA 源文本的存在不等于本轮全部语义阅读。作者原统计 38 full／26 excerpts／2 registration excerpts／33 fetched-not-reviewed 必须作为作者历史覆盖保留。[包索引 L18](<D:/CP6-archives/consolidation-20261010/commercial-cache/s6/S6/INDEX.md:18>)

70 项新 AC 全部 NOT_RUN；29 项任务仍 PLANNED_NOT_AUTHORIZED；18 项 Required 中有 16 项真实政策、物理映射、实现或 Owner 采用门，另有 native 格式边界与治理项。后继 UA 已关闭 Required-ACCEPT-01 的接受治理状态，当前 Markdown 接受不要求虚构原生 Excel，也不声称原册完成。[UA L188](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/c3/c3b8bae0817a061f__UA-20261008-S6-INT-OPS-MD02.json:188>)

## 3. 对象、原身份、版本和封闭 DTO

| 对象 | 键／字段轴 | 解释 | 出处 |
| --- | --- | --- | --- |
| OwnerOperationReceipt | tenant+Owner+businessOperationKey+originalRequestVersion；originalInput digest/domain/codec | 业务唯一效果及原结果从真实 Owner journal 读 | [规范 L104](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:104>) [规范 L182](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:182>) |
| 运输记录 | consumer+messageId 或 outboxId、topic/source/target/partition、correlation/causation | attemptId 是新观测，不替代原因果与业务请求键 | [规范 L105](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:105>) [规范 L111](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:111>) |
| ReplayOperation／Audit | tenant+Owner+replayOperationId；固定 admitted actor／action／native tuple | 重排 ID 只标本次运维动作；原业务 ID 不改变 | [规范 L106](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:106>) [规范 L209](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:209>) |
| nativeVersion | 原生并发标记；现 ERP byte[8] base64 | 不当业务版本／时间戳；同义回查比较已保存原 tuple，不要求仍是队列当前版 | [规范 L115](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:115>) [规范 L209](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:209>) |
| origin／evidence | 真实 nativeRef、bytes/hash、来源权限；RECEIVED_EVENT/SOURCE_SNAPSHOT/LEGACY_UNKNOWN | 不将快照包装为已收消息；来源未知保持未知 | [规范 L112](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:112>) [规范 L116](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:116>) |

ReplayRequest.v1 必填八项：replayOperationId、allowedAction、nativeQueueRef、expectedNativeVersion、expectedDigest{domain,codecVersion,value}、reasonCode、eligibilityRef、inputContractVersion。tenant／actor 服务端绑定，原业务键由原 Owner 登记解析。禁止 replacementPayload、newTenant、force、skipValidation；逻辑 DTO 版本不等于运行 schema 已发布。 [规范 L174](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:174>)、[规范 L186](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:186>)

| 动作 | 原生对象与 message 条件 | 唯一摘要域／codec | 新调度条件与旧接口映射 | 出处 |
| --- | --- | --- | --- | --- |
| REPLAY_ORIGINAL_REQUEST | ErpCommandInbox；原 messageId／structured CloudEvent 必须存在 | cp6.cloud-event.raw-envelope.sha256 / raw-utf8-v1 | 原 tenant/type/aggregate/version/region/topic、schema、原业务键；Owner 未应用证明与新效果资格；旧 PayloadSha256 为 raw hash | [规范 L190](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:190>) |
| REDELIVER_ORIGINAL_RESULT | ErpResultOutbox；原 outboxId、messageId、结果 envelope | cp6.cloud-event.raw-envelope.sha256 / raw-utf8-v1 | 原结果已提交、resultVersion、唯一未 replayed deadletter；只重投结果不建业务对象；旧 PayloadSha256 为结果 raw hash | [规范 L191](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:191>) |
| RESUME_POSTCOMMIT_BRIDGE | ErpOrderBridgeDispatch；原 orderKey；messageId/envelopeRef 必须 null | cp6.erp.postcommit-bridge.identity.sha256 / c03-bridge-key-v1 | UTF-8 c03:bridge:{tenant:D}:{orderKey} 既有 BridgeHash；原订单／操作绑定、未 Completed／确实耗尽／无活 lease；旧 PayloadSha256 实为桥身份 hash | [规范 L192](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:192>) |

桥身份 hash 不证明原业务内容或结果。域／codec／动作错配返回 DIGEST_DOMAIN_ACTION_MISMATCH，原值不符 ORIGINAL_IDENTITY_CONFLICT，缺原载体或业务映射 ORIGINAL_EVIDENCE_UNAVAILABLE，均零新队列调度／业务效果。不能给没有 envelope 的 bridge 伪填 raw hash。 [规范 L194](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:194>)、[规范 L196](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:196>)

**FailureDetail 精确字段入口及语义：** FailureDetail字段：failureRef、ownerId、routeId/routeEpoch、nativeType/nativeId/nativeStatus/nativeVersion、tenantRef（受控显示）、originalMessageRef、originalRequestRef、replayEvidence[{allowedAction,nativeQueueRef,expectedDigest(domain,codecVersion,value)}]、failureClass/safeCode、firstSeen/lastAttempt/nextAttempt、deliveryState、ownerOutcome、ownerReceiptRef/version、attemptCount、availableActions、ineligibilityReasons、evidenceState。敏感字段默认为不返回，原payload单独受控取证接口不在此稿自动开放。 [规范 L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:178>)

**ReplayReceipt 精确字段入口及语义：** ReplayReceipt字段：replayOperationId、allowedAction、nativeQueueRef、expectedDigest(domain/codec/value)、originalRequestRef、originalMessageRef、acceptedAtUtc、acceptedBy、scheduledAttemptRef、nativeVersionBefore/After、status、ownerOutcome、ownerReceiptRef/version、resultProjectionRef、safeCode、retryAfter、auditRef、contractVersion。重排返回202带Location（由服务端生成，当前同站）及Receipt；同义重复先查原operation，按当前read mask返回200原Receipt/现执行状态，不验新调度条件；原提交进行中返回Processing/Unknown/RetryAfter并保持同ID。schema/输入400，未认证401，无权403或防枚举404，版本/幂等冲突409，资格未就绪409/PendingOwner，后端不可用503。仅当本轮确知所有事务已回滚且没有持久操作记录时才能返回可重试未接纳；否则503附operationId和UNKNOWN，前端转查询。 [规范 L180](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:180>)

**OwnerResultReceipt 精确字段入口及语义：** OwnerResultReceipt字段：ownerId、tenantBinding、businessOperationKey、originalRequestId/version、inputDigest+domain+codecVersion、receiptId、receiptVersion、terminalFlag、outcome、targetRefs（含原对象版本）、decidedAt、committedAtEvidence、originalResultNativeRef、projectionMaskVersion、retentionClass、schemaVersion。APPLIED要有Owner原提交凭据；NOT_APPLIED_PROVEN要有受控不存在/回滚证据及观察边界，不能仅“没有查到”。返回结果含历史事实和当前脱敏视图两个独立字段，不能为迎合当前页面改写原结果hash。 [规范 L182](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:182>)

**ReplayAudit 精确字段入口及语义：** 必填审计字段：auditId、tenantBinding、ownerId、replayOperationId、originalRequestRef/businessOperationKey/requestVersion、originalMessageRef/nativeQueueRef、actorId/actorKind、operatorAuthDecisionId/policyVersion、reasonCode、allowedAction（唯一动作枚举，不另设可偏离的requestedAction）、expectedNativeVersion、expectedDigest(domain/codec/value)+allowedAction/nativeType、eligibilityDecisionRef、ownerReceiptBeforeRef/version、nativeStatusBefore/After、scheduledAt、attemptRefs、ownerReceiptAfterRef/version、outcome、contractVersion、retentionClass、supportRef。必须区分originalActor与replayOperator，不把旧payload.UserName写为当前认证人。秘密值不入日志；需要exact payload证明时保存受控nativeRef+hash，而非将payload拷贝至全员日志。 [规范 L533](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:533>)

## 4. 查询、分类、恢复、审计与对账流程

### 4.1 死信／失败读取

每 Owner adapter 只读原队列，查询不增加 attempt 或引起重放。列表只输出 safeCode／supportRef，LastError 可能含 PII，不能原样暴露；缺原业务键返回 OWNER_LINK_MISSING 而非推算。按可信 UTC 和未知时间独立分区，保持 rawTime／sourceTimeZone／normalizedTimeStatus；一个来源失败时 partial+unavailableOwners，全失败才 503，不能空列表冒正常。 [规范 L428](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:428>)、[规范 L431](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:431>)、[规范 L433](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:433>)、[规范 L435](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:435>)

候选页默认 50、上限 100、时间窗最长 31 日；全序为 timePartition、occurredAtUtc、ownerId、nativeType、nativeId、opaque failureRef。签名 cursor 绑定 tenant／actor／filters／routeVersion、snapshotAt 和 sourceSetVersion／可用 Owner 固定集合。缺源恢复必须新查询，已选源中途丢失返回 SOURCE_SET_UNAVAILABLE 重启，不拼漏项旧页。旧 Bridge health 最近 10 条不是全量计数。 [规范 L171](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:171>)、[规范 L437](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:437>)、[规范 L445](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:445>)

### 4.2 原结果优先分类

| 分类 | 可用处理 | 出处 |
| --- | --- | --- |
| BUSINESS_REJECTED | 原业务终态拒绝，不原地技术重试；新业务版本另发 | [规范 L465](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:465>) |
| TECH_RETRYABLE | 真正未提交或已证明幂等，按边界重试 | [规范 L466](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:466>) |
| TECH_TERMINAL | 合同／完整性不兼容，隔离后处理 | [规范 L467](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:467>) |
| AUTH_DENIED | 当前政策拒绝，不能自动续行 | [规范 L468](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:468>) |
| COMMIT_UNKNOWN | 原键查结果，不能当未应用 | [规范 L469](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:469>) |
| SUPERSEDED | 保留旧版本原结果，不倒写新对象 | [规范 L470](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:470>) |
| DUPLICATE_RESULT | 复用原终态 | [规范 L471](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:471>) |
| EVIDENCE_CONFLICT | 隔离异义证据，禁止最后写覆盖 | [规范 L472](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:472>) |

先查 Owner journal 的相同输入；已有终态直接复用，否则在当前门下写业务事实、回执、Outbox 同事务。失败 DbContext 先销毁，再新事务处理；commit 不确定保持 UNKNOWN。Bridge SUCCESS 仅表 hook 返回，SKIPPED 须有真实 Owner APPLIED_EXISTING／业务拒绝等分类，bool false 为 UNCLASSIFIED/PendingOwner。Space 的加强不能套到其他 route。 [规范 L474](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:474>)、[规范 L476](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:476>)

### 4.3 原 operation 优先，再考虑新调度

先做当前认证、tenant 绑定、历史结果读／字段范围和基础语法，再按 tenant+Owner+replayOperationId 查原根。存在时比较已存 admitted actor、action、nativeType/ref、originalRequest/message/version、expectedNativeVersion（原值）、typed digest、reason、contract 和原 eligibility 身份；同 tuple 按当前 mask 返回原 Receipt／现状态，零新调度、零第二调度审计；异义 409。POST 重复保持原 admitted actor，另有读取权的人可 GET。此路径不要求旧队列仍 DEAD、旧 RV 仍当前或 eligibility 未过期。 [规范 L207](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:207>)、[规范 L208](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:208>)、[规范 L209](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:209>)

原根不存在才先取得动作对应最终授权门，再锁 Operation 根复查竞争提交，然后核新资格、当前队列 RV／lease／route epoch／policy／目标版本并原子调度。禁止先长期持 Operation 锁再等授权 fence。门不足前再查竞争可能已完成的原操作；仍在提交／锁超时保持 Processing／Unknown／RetryAfter，用同 ID 查询，不能换 ID 重排。worker 真正产生新效果时再次取得实际门。 [规范 L210](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:210>)、[规范 L211](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:211>)、[规范 L212](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:212>)

原 Owner APPLIED 时不得重做原请求；结果 outbox 可按原结果动作重投。原订单 APPLIED 不说明 bridge 下游步骤完成，必须查相应步骤。UNKNOWN 不旁路；原 target、purpose、version 与批准窗口必须准确匹配，不用旧审批批准新对象。旧 actor 已撤权不能由操作员借身份续权，除非有独立已授权系统义务。 [规范 L502](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:502>)、[规范 L503](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:503>)、[规范 L505](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:505>)

### 4.4 审计

队列变更和重排 Audit 同事务；现 ERP Audit 字段保留，新增字段版本化扩展或关联记录，历史未记录不能填当前值冒原事实。worker attempt 追加不覆盖，Owner 业务回执与业务事实同事务，不由运维 API 代写 APPLIED。敏感取证的读取审计是读取必要条件；审计写失败则整个新调度不提交，更正使用追加记录。 [规范 L537](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:537>)、[规范 L538](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:538>)、[规范 L539](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:539>)、[规范 L548](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:548>)

技术默认 Published 7 日／Processed 30 日／Dead 90 日不是账务、合同、撤权或 PII 保留政策。UNKNOWN／待重放／争议／被引用证据不能普通成功过期先删，也不能因此无限保留完整敏感 payload；legal hold、最小身份／digest 与脱敏政策交 SYS-AUD／PRIV 和 Owner 签认。 [规范 L73](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:73>)、[规范 L542](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:542>)

### 4.5 原结果与消费者对账

原请求版本、原结果版本、当前目标版本三轴独立。查原 operation 的真实 journal，不从当前对象“看起来像成功”反推。Owner APPLIED 的原结果仅重投；NOT_APPLIED_PROVEN 必须有受控不存在／回滚与观察边界证据，404 只是没查到。consumer 对低版单调忽略、同版异义隔离、SUPERSEDED 不回滚新事实。 [规范 L565](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:565>)、[规范 L568](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:568>)、[规范 L569](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:569>)、[规范 L571](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:571>)、[规范 L572](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:572>)

对账创建时固定 RequiredConsumerSet、registryVersion、routeEpoch、consumerAuthority/targetScope、adoptionVersion、nativeCompletionPredicate 和 resultVersion，不以第一个响应集合当完整集合。CONFIRMED_APPLIED 仍保原 confirm／settle 等独立业务义务；CONFIRMED_REJECTED 是确定拒绝，不是业务成功；NotApplied／Superseded 终态不逆写。NotApplicable 必须有真实 Owner scope 证明，不能从集合删掉离线消费者。 [规范 L576](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:576>)、[规范 L580](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:580>)、[规范 L581](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:581>)、[规范 L582](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:582>)、[规范 L583](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:583>)

Received／Processed／Published／Queued／PendingGap／Processing／Blocked／Unknown 全仍 pending，证据冲突隔离。EvidenceComplete 与 BusinessObligationComplete 分开，Case Resolved 只结束本次调查；consumer 集合变化生成 CaseRevision 并带旧新集合及未完义务回执。probe 只读且有界，不额外产生业务效果。 [规范 L584](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:584>)、[规范 L585](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:585>)、[规范 L587](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:587>)、[规范 L589](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:589>)、[规范 L591](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:591>)

## 5. 现有接口与候选 API／错误

| 固定源码已有入口 | 原输入／效果边界 | 出处 |
| --- | --- | --- |
| GET /api/erp-integration/deadletters | 最多 100，当前不是候选稳定 cursor 合同 | [规范 L158](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:158>) |
| POST /api/erp-integration/deadletters/{messageId}/replay | ErpReplayRequest(OperationId, RowVersion byte[8], PayloadSha256 64, ReasonCode)；202/403/404/409 | [规范 L159](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:159>) |
| GET /delivery-deadletters | kind=result／bridge，分别取原 Owner 队列 | [规范 L160](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:160>) |
| POST .../results/{outboxId}/replay | 已提交结果重投，无新建 order | [规范 L161](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:161>) |
| POST .../bridges/{orderKey}/replay | 原已提交 order；不得在 active lease 下重排 | [规范 L162](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:162>) |
| bridge-health／compensate | 旧 metrics／台账标记；不是实际业务补偿 | [规范 L163](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:163>) |

旧 reasonCode 只允许 dependency-recovered／contract-verified；actor 由服务器确定。保留旧 DTO，不能悄悄添加 force 或改 PayloadSha256 的实际域。 [规范 L165](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:165>)、[规范 L514](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:514>)

| 候选 /api/integration-ops/v1 路由 | 用途及合同 | 出处 |
| --- | --- | --- |
| GET /failures | 固定来源集与稳定 cursor 的 FailurePage | [规范 L171](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:171>) |
| GET /failures/{failureRef} | 再鉴权 opaque locator；不能带任意 URL/SQL/路径 | [规范 L172](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:172>) |
| POST /failures/{failureRef}/eligibility | 只读（可写访问审计）；Eligible/Blocked/PendingOwner/Unknown；reason、receipt、decisionVersion、2 分钟 expiry、诊断 authVersion、exactInputTuple；不是执行许可 | [规范 L173](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:173>) |
| POST /failures/{failureRef}/replays | 封闭三动作及 ReplayRequest.v1；无 replacement／force | [规范 L174](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:174>) |
| GET /replays/{id} | 当前历史读权；Scheduled/Running/Completed/Denied/Unknown 与 Owner outcome 分列 | [规范 L175](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:175>) |
| GET /owner-results/{operationRef} | server-side 授权映射到原 journal；迁移缺证 UNKNOWN | [规范 L176](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:176>) |
| GET /replays/{id}/audit | 候选 AuditPage／seq／时间来源／sourceNativeRef／proofStatus／cursor；无自动授权 export 端点 | [规范 L544](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:544>) |

新接纳 202＋同站服务端 Location／Receipt；同义回查 200 原 Receipt／现状态；schema 400、认证 401、无权 403 或防枚举 404、版本／幂等 409、资格未就绪 409/PendingOwner、依赖 503。只有明确整个事务已回滚且无持久操作记录，才能称“未接纳可重试”；其余 503 带 operationId／UNKNOWN，前端转查询。 [规范 L180](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:180>)

## 6. 共同事务、未知状态与恢复

最终授权逻辑 adapter 为 `OwnerFinalAuthorizationGateV1.EvaluateAndHold`。输入必须包含可信 Owner、tenant、subject／actorKind／requester、action、targetVersion、原 operation、expected epoch、scope／field policy 和实际事务；输出为 `ALLOWED_HELD`（含 fenceRef、epoch、字段证明和绑定事务）、`DENIED` 或 `UNAVAILABLE/AUTHORITY_FENCE_UNAVAILABLE`。布尔校验、TTL、二次 GET、lease 都不能替代持门到提交。 [规范 L245](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:245>)、[规范 L247](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:247>)

同库顺序为 Owner 的 ScopeAuthorizationFence → Operation 根 → 控制／队列 → 业务写集 → Receipt／Outbox → Audit；角色、委派、SoD、政策、租户禁用、撤权、worker 与管理入口所有相关写者必须参与同一门。撤权先完成则旧权限新效果拒绝；业务先持门提交则保留该历史事实。单独新增 adapter 有锁不能证明采用。 [规范 L249](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:249>)

Main 与 CRM 分库时，在线 Main GET、CRM 投影和 CRM 本地事务不是共同安全门。没有已采用等价协议时关闭相关新业务效果及 post-commit bridge 的新下游效果，保留原意图为 `BLOCKED_WAITING_AUTHORITY` 并继续原操作查询；INT-COM 有限业务用途 grant 不能替代安全门。这里消费既有 OA-APP §15.1 与 INT-COM R1 §2.3–2.5，未重新发明 IAM 政策。 [规范 L245](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:245>)、[规范 L251](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:251>)

| 操作类别 | 当前所需门 | 不能误用 |
| --- | --- | --- |
| 历史结果读 | 当前认证、tenant、对象／字段读取范围 | 不要求旧 eligibility、旧 ETag 或新写权；返回字段按当前权限 |
| 可信身份事实接收 | 当前注册／route 与 PreCheckpoint 内容一致性门 | PendingInspection 不形成 allow |
| 原已提交结果的纯技术投递 | 当前机器／操作员 delivery fence | 不重判原人类 create 权；接收者新业务效果另受其门 |
| 新业务 replay／bridge 下游效果 | 实际 Owner 最终安全门＋相应用途／目标版本批准 | 不得把历史 read 或原 grant 当续写权 |

上述四分支保持原 operation／原输入：恢复不换 ID，不因旧 token 无效创造新请求。 [规范 L256](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:256>)、[规范 L257](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:257>)、[规范 L258](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:258>)、[规范 L259](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:259>)、[规范 L261](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:261>)

实际 ERP 队列前置：inbox 为 DeadLettered；result 为 DeadLettered 且准确唯一未重排 deadletter；bridge 未 Completed、达到 attempt 门槛／原因码、无 active lease 且原订单存在。接受时队列状态、版本、调度 audit 同事务，业务真正执行仍由 Owner 原业务事务负责。 [规范 L510](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:510>)、[规范 L537](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:537>)、[规范 L539](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:539>)

原 operation 恢复不依赖旧 eligibility 刷新；锁超时或响应丢不证明未接纳。API／worker／Owner journal／结果 delivery 分开记录状态，查询并不等于授权续写。原 payload 不可修改，旧失败上下文不能继续保存伪结果；正向提交不确定时保持原键和证据，禁止直接“补偿成功”。 [规范 L210](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:210>)、[规范 L211](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:211>)、[规范 L212](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:212>)、[规范 L474](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:474>)

## 7. 权限、租户与敏感证据

integration.query／integration.replay／audit.read 是候选逻辑能力，实际由 IAM 和每个 Owner 映射；固定源的 ERP Roles="1,Admin" 不能扩大为其他 route 通用授权。列表、详情、eligibility、replay、audit 各核 tenant、Owner、对象行范围与当前字段掩码。原 payload 不在通用详情自动开放，原回执事实可保留，但每次返回用当前读权投影。 [规范 L120](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:120>)、[规范 L127](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:127>)、[规范 L128](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:128>)、[规范 L129](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:129>)、[规范 L132](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:132>)、[规范 L178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:178>)

originalActor 与 replayOperator 与 worker 分开。审计存受控 nativeRef＋hash，不把秘密／完整 payload 放普通日志；证据过期显示 EVIDENCE_EXPIRED，不用空 message 伪造可重放性。跨租户错误不泄露原对象存在。 [规范 L132](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:132>)、[规范 L445](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:445>)、[规范 L533](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:533>)

## 8. 页面与交互

失败列表至少分 deliveryState／ownerOutcome／evidenceState 三栏，UNKNOWN 只提供原结果调查；旧 COMPENSATED 明示历史标记。详情显示三动作准确摘要域与原对象，eligibility 过期后新调度重查，但已有 operation 查询继续。提交后保留同 ID；关对话框仅取消未发送申请，服务端没有原子 cancel 合同就不能显示已取消持久任务。 [规范 L439](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:439>)、[规范 L512](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:512>)、[规范 L514](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:514>)

审计时间轴分申请、资格评估、调度、worker attempt、Owner 提交、结果投递；技术时间／业务时间分栏。隐藏字段显示“受限”，原审计缺 auth 版本显示“历史未记录”，不显示 0／空冒真实值。对账页分原请求、Owner 结果、delivery、consumer 四层，原对象版本／当前版本并列，不提供 ForceConfirm。 [规范 L546](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:546>)、[规范 L596](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:596>)、[规范 L598](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:598>)、[规范 L600](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:600>)

来源 partial 时显示 unavailableOwners；“重新查询全部来源”清旧页和旧 sourceSet。前端在租户切换、会话失效、迟到响应时清受保护数据与资格，统一 Loading／Empty／Forbidden／NotFound／Conflict／Unavailable／UNKNOWN 和可访问焦点状态。 [规范 L171](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:171>)、[规范 L445](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:445>)、[规范 L709](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:709>)

## 9. 实施顺序与固定源码差异

以下是正文在固定源码版本上记录的实现事实，未在本轮刷新远端 HEAD，也未执行这些源文件：Main `90c871fe571fd6b390f53e8678376d7ce60bcb60`；CRM `c778a3052a4416b82facb07b1244fc98fb6ad8a1`；Platform `30bd23af6808d217a23878bd9437513043c52834`。[规范 L25](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:25>)

| 证据 | 固定源码位置／范围 | 已观察事实／复用 | 仍不能证明 | 原文 |
| --- | --- | --- | --- | --- |
| E16 | Main IntegrationEvent.cs L24–127；BridgeHookBase.cs L32–151 | 原Bridge ledger、PENDING/SUCCESS/SKIPPED/FAILED/DEAD/COMPENSATED；持久化失败best-effort日志 | 所有Bridge事实和ledger原子；无行不证明未执行 | [规范 L65](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:65>) |
| E17 | Main IntegrationEventDispatcher.cs L18–84,121–155；RetryWorker.cs L77–197及Space分支 | 原路由复用；Space persistEvent=false及retry fence，非Space维持旧状态机 | 所有路由统一lease/receipt或bool=false可准确分类 | [规范 L66](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:66>) |
| E18 | Main BridgeHealthService.cs L17–102；Controller.cs L8–44；BridgeHealthView.vue L75–205 | 现有指标/最近10死信；compensate只改状态/操作者/时间，页面显示成功 | 业务补偿已做、原结果一致、具体重放权/当前权限 | [规范 L67](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:67>) |
| E19 | Main ErpReplayController.cs L11–64；ErpInboxReplayService.cs L11–90 | 原ERP deadletters、Admin人类actor、operationId/rowVersion/hash、原消息只调度、审计同事务 | 跨Owner通用重放资格和现权限政策已统一 | [规范 L68](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:68>) |
| E20 | Main ErpDeliveryReplayController.cs L11–69；Service.cs L21–210 | 结果Outbox与post-commit bridge不同重排；原消息/原订单保留；active tenant/lease/precondition | delivery成功即订单新建或接收方已应用 | [规范 L69](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:69>) |
| E21 | Main ErpRequestHandler.cs L48–121,165–228,241–263 | 原request journal按tenant/kind/aggregate/requestVersion，InputSha256冲突，终态原结果复用，Inbox/business/Outbox同事务 | 所有Owner均有通用原回执API；InputSha256等同raw payload hash | [规范 L70](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:70>) |
| E22 | Main ErpReadService.cs L7–55；ERP配置 L23–67 | 业务对象read含原RequestId/Version；ERP replay/worker条件注册 | 仅对象当前值足以证明某原请求未执行 | [规范 L71](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:71>) |
| E23 | Platform Cp6InboxProcessor.cs L31–128,132–224,226–268；OutboxDispatcher.cs L32–80 | 传输去重/版本checkpoint/事务回调/重试；公开requeue原语只处理技术队列 | 业务操作幂等、应用鉴权/审计、可直接给最终用户执行 | [规范 L72](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:72>) |
| E24 | Platform RetentionService.cs L17–47；TransactionalMessagingContractTests.cs L9–20 | 默认published7天、processed30天、deadletter90天；技术清理原语 | 满足凭证/审计法定政策；未决UNKNOWN可安全清理 | [规范 L73](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:73>) |
| E25 | Main和CRM contracts/events/platform/contract-bundle.v1.json | 两边均blob ac1bf0c31b5f7302dfea4994f0d3be57617bdf19 | 仓库相同不代表运行资产一致；未运行schema validator | [规范 L74](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:74>) |
| E26 | Main CrmIdentityChangeCapture/Contract/RevocationTests；CRM IdentityProjection/RevocationOrigin/Authentication/ReconciliationClientTests；ERP ReplaySqlTests/DeliveryReplaySqlTests | 已有断言文本覆盖捕获、版本/tenant、origin、会话、同operation/原payload重放 | 本轮执行均NOT_RUN；不沿用旧报告PASS | [规范 L75](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:75>) |
| E27 | CRM CrmIdentityProjectionExtensions.cs L15–83；Program.cs | 条件consumer/reader/reconcile接线；sidecar来源/region/topic/partition校验 | 已部署组件配置、确切tenant清单及真实收到事件 | [规范 L76](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:76>) |
| E28 | CRM CrmPageState.tsx L22–45,51–115；CrmEndpoints.cs L38–64 | 现成Forbidden/Conflict/Unavailable与workspace动作/业务API | 本稿新增诊断/重放台已经有页面 | [规范 L77](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:77>) |

先逐 Owner 明确原操作键、journal、typed digest 和失败分类，再确定真实最终门与全部写者；随后封闭旧 ERP 三路径兼容／原 operation 查询顺序和原子 audit；最后接候选 API／UI／consumer 对账与真实 AC。不能先做统一重放按钮后补业务证明。

| 任务 | 代码／责任 | 具体工作 | 依赖／验收 | 出处 |
| --- | --- | --- | --- | --- |
| OP01 | Main/CRM各Owner；原queue read adapters | 统一FailureDetail和§3.3三allowedAction/nativeType/digest域映射，bridge无envelope不伪造；不复制业务主数据 | OPS-01-01；Required-OPS-01；AC-OP01-01/02 | [规范 L680](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:680>) |
| OP02 | Main/CRM API Owner；新只读/failures adapter | 稳定分页/cursor、partial/时间来源、安全错误、current-scope过滤及原详情定位 | OPS-01-01；OP01；AC-OP01-03/04/05/06 | [规范 L681](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:681>) |
| OP03 | Main FE；BridgeHealthView及新详情；CRM FE对应状态组件 | 旧COMPENSATED重标说明，双状态列表，安全复制/分页/租户切换 | OPS-01-01；OP02；AC-OP01-06/07 | [规范 L682](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:682>) |
| OP04 | 各业务Owner；旧Bridge route接口/dispatcher | 逐route将bool/SKIPPED补具体业务结果分类，未证明route保持只读 | OPS-01-02；Required-OPS-04；AC-OP02-01/05/07 | [规范 L683](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:683>) |
| OP05 | ERP/CRM业务Owner；现request journal/handler | 明确business/input/msg/operation键、原结果复用、terminal/unknown判定；不触ERP-CREDIT正文 | OPS-01-02；Required-OPS-01；AC-OP02-02/03/04/06 | [规范 L684](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:684>) |
| OP06 | FE/API Owner | FailureClassification DTO及各状态交互，保原Owner code/version/transport状态 | OPS-01-02；OP04/05；AC-OP02全组 | [规范 L685](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:685>) |
| OP07 | Main IAM+各接收Owner | §3.6按read/fact-receive/delivery/new-effect分类消费既有共同最终门及完整共同写者，逐route缺门关闭；安全门不替代用途/目标版本门 | OPS-01-03；Required-OPS-02/IAM；AC-OP03-03/04/05 | [规范 L686](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:686>) |
| OP08 | ERP Owner；ErpInboxReplayService/DeliveryReplayService及controllers | §3.4先原operation回查，仅新接纳走gate；§3.3三分支摘要映射到旧PayloadSha256，保原hash/rowVersion/reason原子审计；不破旧DTO | OPS-01-03；OP05/07；AC-OP03-01/02/04/06 | [规范 L687](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:687>) |
| OP09 | 各应用API/FE Owner；候选eligibility/replays/status | 按§3.3typed摘要/§3.4先回查后新调度/§3.6最终门编排；提交UNKNOWN保持同ID，过期eligibility不阻历史查询，202/Cancel语义明确 | OPS-01-03；OP07/08、Required-API；AC-OP03-05/06/07 | [规范 L688](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:688>) |
| OP10 | 各Owner审计；ReplayAudit/原journal | 原actor/operator/worker分层，typed action/digest tuple、原operation回查零新审计、最终门fence/epoch证据与append-only关联；历史unknown保真 | OPS-01-04；Required-AUD；AC-OP04-01/02/03/05/07 | [规范 L689](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:689>) |
| OP11 | S5 AUD/PRIV+应用Owner | retention class/legal hold/敏感导出政策与访问审计，区别技术默认与业务保留 | OPS-01-04；Required-AUD；AC-OP04-04/06 | [规范 L690](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:690>) |
| OP12 | 各接收业务Owner；OwnerResult read adapter | 以原操作查询已提交journal，完整Receipt DTO、先原operation读分支；可信NOT_APPLIED证明条件；缺者仅阻新重放而保合法历史读 | OPS-01-05；Required-OPS-01/API；AC-OP05-01/05/06 | [规范 L691](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:691>) |
| OP13 | Producer+consumer应用Owner；原result outbox/inbox | 原结果重投、固定RequiredConsumerSet/原native完成谓词、consumer终态映射及单调处理；分离证据齐/业务完成 | OPS-01-05；OP12；AC-OP05-02/03/04/07 | [规范 L692](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:692>) |
| OP14 | Main/CRM FE Owner | 四层对账页、原对象/现对象版本、UNKNOWN只读路径、同operation重开恢复 | OPS-01-05；OP12/13；AC-OP05全组 | [规范 L693](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:693>) |
| X01 | S6拟稿/S5签认 | 固定authority/tenant/field/版本合同；消费声明精确版本与拒绝/恢复映射 | ID01–05；Required-IAM；不能把讨论当采纳 | [规范 L694](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:694>) |
| X02 | Main/CRM/S7 | 消费OA-APP §15.1及INT-COM R1§2.3–2.5既有门：定义OwnerFinalAuthorizationGateV1 adapter、同库锁序/共同写者、分库缺门关闭和四类权限分支；真实采用取证计划 | ID02/03、OPS03；ID05/06/OP07–09；Required-ID-04/OPS-02；实现未授权/运行UNPROVEN | [规范 L695](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:695>) |
| X03 | Main/CRM/业务Owner | 原结果查询与identity baseline两个不同对账流程/合同窗口 | ID05/OPS05；Required-ID-03/OPS-01 | [规范 L696](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:696>) |
| X04 | 应用发布Owner/S7 | 逐route单consumer/route epoch/旧积压/停旧/回退审阅方案 | OPS03/6.3；Required-PLT-02；不启双写 | [规范 L697](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:697>) |
| X05 | 独立测试/评审Owner | 70AC test design、原测试映射、fixture/生产证据层、缺证与回归范围；独立验收 | 第8节；Required-TEST/ACCEPT；本轮NOT_RUN | [规范 L698](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:698>) |

兼容切换仍保既有 topic／schema／Main Bearer 与 CRM profile，不把全部旧 Bridge 改成消息化。新 adapter 只能在单 writer routeEpoch、旧积压分类和原 OwnerLookup 已明确后激活；旧 in-flight 先终态或明确交接，回退不撤销已完成业务或删除原回执。 [规范 L655](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:655>)、[规范 L657](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:657>)、[规范 L659](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:659>)、[规范 L662](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:662>)

## 10. 验收场景与实际验证状态

五组共 35 项原 AC 如下，全部 **NOT_RUN**；不是恢复成功日志。

| AC | 准确场景与预期 | 状态 | 出处 |
| --- | --- | --- | --- |
| AC-OP01-01 | 三allowedAction/nativeType/digest(domain/codec/value)准确：request/result为真实raw envelope，bridge仅原身份摘要且message/envelope=null；不伪造业务input摘要 | NOT_RUN | [规范 L448](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:448>) |
| AC-OP01-02 | 查询按当前tenant及Owner scope，未授权对象不泄露存在性或payload | NOT_RUN | [规范 L449](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:449>) |
| AC-OP01-03 | 跨Owner分页使用含owner/nativeType/nativeId/failureRef的唯一全序，cursor固定sourceSet；缺源恢复新查询，中途丢源明确未就绪，不拼漏项旧页 | NOT_RUN | [规范 L450](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:450>) |
| AC-OP01-04 | 部分Owner不可用明确partial；全部不可用503；不以空列表冒成功 | NOT_RUN | [规范 L451](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:451>) |
| AC-OP01-05 | 原始异常、PII和secret不进入列表、复制诊断、通用日志或指标标签 | NOT_RUN | [规范 L452](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:452>) |
| AC-OP01-06 | COMPENSATED历史标记不计业务已恢复；旧时间未明不伪造UTC | NOT_RUN | [规范 L453](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:453>) |
| AC-OP01-07 | 页面Back/刷新/切tenant/迟到响应不保留旧组织数据，不因查看自动重排 | NOT_RUN | [规范 L454](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:454>) |
| AC-OP02-01 | 同一可预期业务拒绝持久为terminal，不被worker技术retry重新执行业务 | NOT_RUN | [规范 L485](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:485>) |
| AC-OP02-02 | 技术回滚后用新事务记录retry；失败上下文不二次提交旧跟踪实体 | NOT_RUN | [规范 L486](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:486>) |
| AC-OP02-03 | 提交超时仅UNKNOWN，先查原Owner回执；HTTP503不自动证明未执行 | NOT_RUN | [规范 L487](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:487>) |
| AC-OP02-04 | 重复业务键同输入复用原结果，异输入冲突；messageId变化不创建第二业务效果 | NOT_RUN | [规范 L488](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:488>) |
| AC-OP02-05 | SKIPPED/bool=false逐route经Owner映射；未映射保持未分类且重放关闭 | NOT_RUN | [规范 L489](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:489>) |
| AC-OP02-06 | 已被新版取代旧请求不可恢复旧业务版本，UI同时展示原版本与现版本 | NOT_RUN | [规范 L490](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:490>) |
| AC-OP02-07 | transport success/Inbox processed与Owner applied分列，任一技术状态不冒充业务成功 | NOT_RUN | [规范 L491](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:491>) |
| AC-OP03-01 | 同operation先查原tuple再分支；首次响应丢、queue已改版、eligibility过期或已无新写权但有读权，仍只读回原Receipt，零新调度；异义冲突 | NOT_RUN | [规范 L519](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:519>) |
| AC-OP03-02 | 按三动作分支核原nativeVersion和typed摘要；bridge不要求不存在的payload/schema；域/codec/action错配稳定拒绝且无队列/业务变更 | NOT_RUN | [规范 L520](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:520>) |
| AC-OP03-03 | 新调度/新效果只经§3.6已采用共同门；失权DENIED、分库缺门UNAVAILABLE，历史原结果读/可信事实接收/纯技术重投按各自门，不借原grant续权 | NOT_RUN | [规范 L521](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:521>) |
| AC-OP03-04 | 原已APPLIED业务不重做；原结果delivery或原bridge尚未完成步骤按各自原效果范围/门处理，不能把订单APPLIED当下游步骤成功；对应终态拒绝/取代不改旧结果 | NOT_RUN | [规范 L522](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:522>) |
| AC-OP03-05 | lease/route epoch变化、并发worker或新状态导致资格重判，零双消费者双写 | NOT_RUN | [规范 L523](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:523>) |
| AC-OP03-06 | 新调度audit+queue同事务；响应丢/提交进行中/锁超时返回同operation的Processing/Unknown/RetryAfter，先回查，不以查无/旧版本失配生成新ID | NOT_RUN | [规范 L524](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:524>) |
| AC-OP03-07 | 202只表示已安排；关闭dialog不取消已接纳任务；同ID可重开查询，过期eligibility或原新写权变化不被误作已有操作消失 | NOT_RUN | [规范 L525](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:525>) |
| AC-OP04-01 | 原operation同义回查零新调度/调度audit；新接纳强制audit与调度同commit，异义不重写原audit | NOT_RUN | [规范 L551](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:551>) |
| AC-OP04-02 | 原actor/operator/worker分列，新增效果引用真实Owner最终门fence/epoch/事务证据，缺能力不能补造ALLOWED_HELD | NOT_RUN | [规范 L552](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:552>) |
| AC-OP04-03 | allowedAction/nativeType及typed digest原样入audit；raw/data/canonical/bridge identity四域不互换，不存在载体保持null | NOT_RUN | [规范 L553](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:553>) |
| AC-OP04-04 | 错误/日志/导出不带secret和未授权PII；敏感证据访问受单独scope | NOT_RUN | [规范 L554](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:554>) |
| AC-OP04-05 | 历史缺失数据保持LEGACY_UNKNOWN，不伪造auth版本、真实撤权时间或Owner提交时间 | NOT_RUN | [规范 L555](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:555>) |
| AC-OP04-06 | UNKNOWN/争议/引用证据不被通用技术retention自动清理；Owner政策未证则不启用清理 | NOT_RUN | [规范 L556](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:556>) |
| AC-OP04-07 | 审计只能追加更正，原history保持可追溯；平台trace不冒充Owner业务回执 | NOT_RUN | [规范 L557](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:557>) |
| AC-OP05-01 | 提交UNKNOWN只查原Owner回执；查无/已过期/副本延迟均不等于NOT_APPLIED_PROVEN | NOT_RUN | [规范 L603](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:603>) |
| AC-OP05-02 | Owner已APPLIED且delivery失败仅重发原结果，业务对象数/金额/版本不重复变化 | NOT_RUN | [规范 L604](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:604>) |
| AC-OP05-03 | 业务终态拒绝保持原结果；Inbox processed/Dapr success不显示业务成功 | NOT_RUN | [规范 L605](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:605>) |
| AC-OP05-04 | 消费者同版异内容隔离、低版不回退、新版取代返回明确未采纳原因 | NOT_RUN | [规范 L606](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:606>) |
| AC-OP05-05 | 原请求/结果/当前对象三套版本与raw/canonical域完整保留，对账不重算原输入身份 | NOT_RUN | [规范 L607](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:607>) |
| AC-OP05-06 | 历史结果返回按当前字段/PII权限投影；原不可变结果摘要和nativeRef不改 | NOT_RUN | [规范 L608](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:608>) |
| AC-OP05-07 | 固定consumerSet/registryVersion及每项原完成谓词；证据齐全与业务义务完成分层，Received/Processed/Unknown不当Applied，原WMS/INT-COM独立义务不被Case Resolved覆盖 | NOT_RUN | [规范 L609](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:609>) |

70 项合并 AC 的测试文本映射和已有源码测试只能作为后续实现验证入口。当前没有真实 queue／lease 竞争、未知提交、字段掩码或下游 obligation 完成的运行证据；旧 PASS 不改变此状态。 [规范 L713](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:713>)、[规范 L717](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:717>)、[规范 L742](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:742>)

## 11. 外部依赖与精确退出条件

| Required | 责任 Owner | 须取得的准确能力／证据 | 缺失边界 | 出处 |
| --- | --- | --- | --- | --- |
| Required-OPS-01 | 每一接收业务Owner | 原operation键/输入digest域、原结果journal、APPLIED/NOT_APPLIED/UNKNOWN及业务拒绝分类adapter | 该route只读，重放关闭 | [规范 L626](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:626>) |
| Required-OPS-02 | 每一接收业务Owner | §3.6最终安全门/原Owner adopted fence、完整共同写者、目标状态及技术内容/用途/版本/baseline批准绑定；安全门与INT-COM用途grant分离 | 缺共同安全门AUTHORITY_FENCE_UNAVAILABLE阻新效果；原operation只读可查，旧权限/旧批准不套新对象 | [规范 L627](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:627>) |
| Required-OPS-03 | ERP Owner | 现inbox/result/bridge三路重排原件兼容、事务审计/lease/unknown与原回执读API证据 | 保原入口语义，不称全应用恢复完成 | [规范 L628](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:628>) |
| Required-OPS-04 | Main各Bridge Owner/S1–S4 | 每route业务键、原结果查询、bool/SKIPPED分类、原子性/lease/重复保障；Space采纳原回执 | 不把Space加强覆盖到其他route | [规范 L629](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:629>) |
| Required-PLT-01 | Platform/S7 | ICp6InboxPreCheckpointV1/ProcessWithPreCheckpointV1准确版本、应用持事务及§3.5共同锁序/封闭decision/Dapr映射/全早退覆盖；另核RequeueDeadLetteredAsync应用权限与原子审计 | 缺前置内容门关闭相关新授权/依赖新写，可可信PendingInspection；不直暴露Requeue原语 | [规范 L630](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:630>) |
| Required-PLT-02 | 应用部署Owner/S7 | producer/consumer/schema/topic/contract-bundle矩阵、单写route epoch、旧消费者关停/回退证明 | 不开双consumer、不能直接停旧消费 | [规范 L631](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:631>) |
| Required-AUD-01 | SYS-AUD/SYS-PRIV/S5及业务Owner | 原结果/撤权/重放/技术文档/PII保留冲突策略、访问/导出权限及legal hold依据 | 通用retention不自动启用 | [规范 L632](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:632>) |
| Required-API-01 | Main/CRM/API Owner | 3.2候选路由、DTO兼容、错误码、分页、权限码及客户端版本签认 | 新API仅设计；不冒已有 | [规范 L633](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:633>) |
| Required-UI-01 | Main/CRM FE Owner | 旧COMPENSATED文案/历史保留策略、身份失效缓存清理、UNKNOWN重试交互 | 不能声称页面已修改 | [规范 L634](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:634>) |
| Required-TEST-01 | 各Owner+独立评审 | 被授权真实测试环境、AC与原测试identity、原始run/日志、验收人结论 | 70项新AC全NOT_RUN | [规范 L635](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:635>) |
| Required-ACCEPT-01 | 主对话总负责人 | 作者STOPPED→独立review→修订→正式接受/Registry记录 | 作者不能自判PASS或更新分数 | [规范 L636](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/d9/d9b57a039e18a689__S6_INT-ID_INT-OPS_完整设计候选_v0.2.md:636>) |

Required-ACCEPT-01 后继已关闭，其余不可由文档整理替业务 Owner 决定。尤其每 route 必须具备原业务键、原 input codec、真实 APPLIED／NOT_APPLIED／UNKNOWN adapter、当前最终门及固定消费者完成谓词；缺者该 route 保只读／调查，不能泛化 Space 或 ERP 的已读局部能力。

## 12. 实际阅读覆盖与附件继续队列

共同正文 750 行及原定点复审 167 行全文语义阅读；两份 CURRENT（ID 225 行、OPS 221 行）全文；两份 UA（ID 286 行、OPS 282 行）全部对象结构阅读，OPS 同时逐行全文；共享 SOURCE-MANIFEST 全部顶层对象结构阅读。原 ZIP 的 SOURCE-READ-LIMITS 16 行、作者回应 49 行、INDEX 18 行和 ACCEPTED-DEPENDENCY-READS 43 行全文阅读。精确 SHA、模式和行段记录在[商业阅读台账](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。

必要辅助已补核：18 Required、70 AC、29 Task每条ID均精确落在已全文读正文原行，0定位差异；10 SPEC与原Scope完整结构合读。v0.1首轮独审250行现已全文补读，四项B与三项N按v0.2后继关闭解释，原RETURN不改PASS。BASELINES／候选CURRENT／PRIOR／REVIEW-HISTORY／STOPPED保原历史状态，不覆盖根准确UA。INT-COM 31行、OA-APP 21行、WMS-MASTER 9行精确依赖摘录已全文合读。

99份固定源码的完整bytes/SHA及124成员清单逐项只读核对0差异；Main55、CRM35、Platform9。作者记录38全文、26窗口、2注册窗口、33仅取得未审是旧作者覆盖，本轮没有冒全99源码新审计。大metadata目录／计划／Registry是历史定位资料，本Target完整范围由ORIGINAL-SCOPE保存；其他Target未因索引而宣称已全文读。原v0.1主文仅身份保全，不替代v0.2。

相关：[另一 S6 模块](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/INT-ID-01.md>)、[INT-COM](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/INT-COM-01.md>)、[商业合同](<D:/CP6/docs/CP6_开发设计文档_20261010/contracts/commercial-contracts.json>)。本册状态为 `required_materials_consolidated`；当前必要规范和辅助已归并、历史载体已按实际角色分类，真实实现／业务测试／Owner采用仍未验证。
