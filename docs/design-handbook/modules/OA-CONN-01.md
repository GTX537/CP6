# OA-CONN-01 · 触发器、连接器与预演

整理状态：`core_semantics_consolidated`。本册展开已接受静态设计；所有 32 项 AC 仍 `NOT_RUN`，真实 Owner 采用 `UNPROVEN`。设计接受不授权实施或外部业务操作。[原文A:372](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dd/ddb35e86a0458a61__UA-20261008-X5-OA-CONN-01-STATIC-MD02.json:372)

## 1. 业务目的、操作者与归属

为 TIMER/EVENT/MESSAGE 三类工作流触发建立可审查版本、稳定发起身份、外部调用和原结果恢复。管理员配置和独立复核人发布/启停，具备实际发起资格的用户手动发起，注册服务身份接收入站和运行 Job；“名义 Starter”与实际操作者/服务身份分别保存。模块不重建消息平台，不把触发定义塞进 FlowSchema，不代业务 Owner 决定资金、库存或审批应用。[原文C:7](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:7) [原文C:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:9) [原文C:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:29)

Owner 为 Main Workflow。流程定义/依赖 pin 来自 OA-DEF；与 OA-APP 共用同一 WF_AUTH 授权 fence，并使用已接受 APP 回执链。外部供应者负责其幂等、结果查询、无效果证明和调用时效合同；业务 Owner 负责写入资格、字段披露、补偿和业务应用回执。连接器 HTTP ACK、外部 SUCCEEDED、Workflow APPLIED、业务 Owner APPLIED 是不同事实。[原文C:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:11) [原文C:115](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:115) [原文C:131](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:131) [原文C:202](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:202)

## 2. 当前设计组合与接受边界

选定正文是 OA-CONN **R1**，SHA256 `04f7fe716dfe5d69b4ebf7d3b11d4a3d8d0d9184bd931832ccbc2533f77c153c`，249 行、48148 字节；强制合读 COMMON R1。X5 R2 整包的准确组合是 BANK R2 + BUD/ASSET/CONN/COMMON R1；CONN 的字节没有随包名改为 R2。[原文A:179](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dd/ddb35e86a0458a61__UA-20261008-X5-OA-CONN-01-STATIC-MD02.json:179) [原文A:196](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dd/ddb35e86a0458a61__UA-20261008-X5-OA-CONN-01-STATIC-MD02.json:196) [原文A:215](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dd/ddb35e86a0458a61__UA-20261008-X5-OA-CONN-01-STATIC-MD02.json:215) [原文I:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e5/e5934d9a539b1c78__COMPOSITION-IDENTITIES.json:41)

R1 完整独审与 R2 剩余根问题定点复核形成四目标累计结论；CONN 的 R07 控制影响预览、R08 失败结果本地应用已在 R1 闭合。2026-10-08 04:09:11Z 的 USER_AUTHORIZED_DELEGATE 接受是独立治理记录，不是用户亲签；正文末尾 STOPPED/最终审报告待接受保留历史原貌。本次归档不是重新业务审查，不能把历史标签回写为当时通过。[原文R:121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/09/096df8bb50ccf094__CP6_X5_R2_定点独立复核与四专项累计结论_20261008.md:121) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dd/ddb35e86a0458a61__UA-20261008-X5-OA-CONN-01-STATIC-MD02.json:8) [原文A:301](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dd/ddb35e86a0458a61__UA-20261008-X5-OA-CONN-01-STATIC-MD02.json:301) [原文Q:143](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/87/875bbd4b5e0ea711__SOURCE-QUALIFICATIONS.json:143)

范围为 5 个原 SPEC、32 个 NOT_RUN AC。S5/S7/X5 合计 21 目标、101 原 SPEC、613 AC 的静态接受，不是 613 次业务执行。[原文R:147](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/09/096df8bb50ccf094__CP6_X5_R2_定点独立复核与四专项累计结论_20261008.md:147) [原文R:153](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/09/096df8bb50ccf094__CP6_X5_R2_定点独立复核与四专项累计结论_20261008.md:153) [原文A:372](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dd/ddb35e86a0458a61__UA-20261008-X5-OA-CONN-01-STATIC-MD02.json:372)

## 3. 数据身份、字段与约束

公共 Scope 仅可信 `environment + tenant`；不能因为载有财务变量而伪造 Ledger Scope。正文原编号、Root、Version、Invocation、Job、Provider operation、Result、Receipt 都保留独立身份。以下为规范概念字段，不是声称数据库表名已实现。[原文C:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:29) [原文X:20](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:20)

|对象|必需身份及关键字段|开发约束|
|---|---|---|
|TriggerRoot / TriggerVersion|Root id、Scope、不可变 kind、flowKey、controlRevision、rowVersion、currentVersion；Version 固定 definition/form/dependency pins、Starter/authority、tagged config、policy、maker/review|Root `DRAFT/ENABLED/DISABLED/RETIRED`；已发布版本不可变，旧发起恢复不 resolve latest。[原文C:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:31)|
|TIMER config|cron 1..200、timezone rules/calendar、`LATEST_ONE_SKIP_MISSED`、准确 vars、scheduleRevision|时区与规则版本参与 pin，不能用浏览器 locale 替代。[原文C:32](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:32)|
|EVENT / MESSAGE config|注册 producer/schema/sourceModule/hookName、typed mappings/filter；message schema/vars/transport/rate policy|hookName 1..100；maps 0..100；payload 不得覆盖 Scope/Starter。[原文C:32](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:32)|
|ConnectorRoot / Version|Scope + sourceKind `TENANT/APP` + id；同 kind 名称唯一；实现 id/version、固定 destination/operation/schema/map、effectClass、duration/lease/retry/idem/query合同、credential locator/generation、disclosure/adoption|解析准确 sourceKind+version；租户版本缺失或停用不能回落同名 APP；秘密不落 DTO。[原文C:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:33) [原文C:34](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:34) [原文C:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:83)|
|TriggerSlot|Root/Version/scheduleRevision/dueUTC 唯一；originalLocal/offset/fold、varsSnapshot、pins、claimEpoch、command|状态 RESERVED/BLOCKED/INSTANCE_CREATED/CANCELLED；fold NORMAL/FIRST/SECOND。[原文C:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:35)|
|TriggerInvocation|输入 identity/digest/snapshot、actual/effective actor、authority、pins、origin、instance/command/attempts|RECEIVED/READY/ADMISSION_BLOCKED/INSTANCE_CREATED/REJECTED/OUTCOME_UNKNOWN；接收不是实例建立。[原文C:36](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:36)|
|ForecastRun|固定 pin、input/Starter、knowledge cut、policy/calendar、timezone、steps/unvisited|coverage COMPLETE_FOR_SUPPORTED_GRAPH/PARTIAL/UNPROVEN；`isExecutionPermit=false`。[原文C:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:37)|
|ConnectorInvocation|instance/token/node/job、connectorVersion/operation/input、effectKey、transportKey、OwnerSubject/admission/epoch|PREPARED/DISPATCH_ADMITTED/SENT/OUTCOME_UNKNOWN/RESULT_RECEIVED/FAILED_NO_EFFECT/QUARANTINED。[原文C:38](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:38)|
|ExternalResultFact|operation/event/status、不可变 snapshot/provenance/revision/previous|ACKNOWLEDGED/RUNNING/SUCCEEDED/FAILED/UNKNOWN；不以 HTTP 200 直接标成功。[原文C:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:39)|
|应用投影|workflowResume 与 businessOwner 两条轴及独立 receipts|前者 NOT_APPLIED/APPLIED/STALE_TOKEN/HELD；后者 NOT_APPLICABLE_PROVEN/PENDING/APPLIED/REJECTED/UNPROVEN。[原文C:40](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:40)|
|ControlImpactPreview|准确 root/version/action/control/activation 与 invocation-slot-job-dispatch frontiers、完整安全 manifest|固定 10 分钟 TTL；COMPLETE/PARTIAL_UNPROVEN；展示脱敏不准漏算内部影响。[原文C:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:41)|
|ConnReviewSubject/Decision|准确 config version、kind、影响/资格证据|kind 为 TRIGGER_PUBLISH/ENABLE/RETIRE 或 CONNECTOR_ACTIVATE/RETIRE，不能混成 APP 业务决定。[原文C:42](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:42)|
|FailureApplicationProjection|terminalFailure、原 token/node、local application/receipt|NOT_REQUIRED/PENDING/OUTCOME_UNKNOWN/APPLIED_ERROR_EDGE/APPLIED_SUSPENDED/STALE_TOKEN/INTEGRITY_HOLD；冲突另留 RESULT_CONFLICT_QUARANTINED。FAILURE_APPLICATION_PENDING 是新投影，不改旧 Job 枚举。[原文C:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:43)|

输入 JSON ≤64KiB；配置 ≤1MiB、深度8、字段500；结果 ≤1MiB，超限隔离而非截断后应用；批处理 ≤100、分页1..100、cron preview最多5时点。`maxCallDuration` 为1..299且严格小于lease，并覆盖内重试和网络总耗时；只是单次 timeout 小不够。[原文C:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:45) [原文C:87](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:87)

## 4. 入口、状态推进及事务写集

**创建和控制。** options → creation-context 固定 Scope、自然键 ABSENT、依赖 heads → Draft → 独立 TRIGGER_PUBLISH review → publish 固定版本 → ENABLE 影响预览 → 独立 TRIGGER_ENABLE review → enable。MESSAGE Draft 保存不生成凭据；未证明外部采用的配置可存草稿但不能启用。首启即使影响为空也要准确零项 manifest。[原文C:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:51) [原文C:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:55)

|动作|当前动作权/决定要求|控制后果|
|---|---|---|
|publish|trigger.publish + TRIGGER_PUBLISH 决定|形成不可变版本/pins。[原文C:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:57)|
|enable|trigger.control + 新 TRIGGER_ENABLE 决定 + 完整影响预览|重核当下全部准入，不挪用发布决定。[原文C:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:57)|
|trigger disable|trigger.control + 完整影响 + reason；decisionRef=null|停止新 Invocation/未完成自动重试，历史查询保留；不撤销已有实例。[原文C:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:53) [原文C:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:57)|
|trigger retire|trigger.control + 新 TRIGGER_RETIRE 决定|永久停止该版本新用，后续须新版本，不翻回旧开关。[原文C:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:57)|
|connector activate|connector.activate + 新 CONNECTOR_ACTIVATE 决定 + 专业采用/影响证据|精确实现、契约、凭据引用和当前业务资格齐备。[原文C:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:57)|
|connector disable / retire|disable 为control+影响且decision=null；retire另需自己的新决定|已 admitted 外部在途不假称撤销成功。[原文C:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:57) [原文C:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:59)|

所有影响 writer 在同一 fence 下推进 frontier。确认时比较完整未过期 preview、WF_AUTH、全部 control/activation/dispatch heads；预览后新增 Job/dispatch 即 412 IMPACT_CHANGED，Root rowVersion 相同也不能放行。ControlReceipt 带 notRevokedInFlightManifest。新意图即使最终 NO_CHANGE 仍需当前 preview；原命令重查则返回原结果。[原文C:41](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:41) [原文C:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:59)

**定时。** 错过时点按 LATEST_ONE_SKIP_MISSED 仅选原策略允许的最近一个，记 selected/skipped range、count 或 EXACT_COUNT_UNAVAILABLE；下一 due 必须晚于 observed。首次 claim 原子冻结 slot、vars、pins 并推进 nextDue；随后 Instance、Invocation/slot、lastFired、command 必须真正参加同一 Main Uow。nextDue 已推进不证明 Instance 已创建；恢复使用旧 slot。[原文C:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:63) [原文C:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:65)

**事件/消息。** Scope+注册 producer+eventId 唯一；同键异 payload 隔离。先保存 Inbox 再作当前发起准入，接收 ACK 不等于实例；仅对注册声明 aggregate/sourceSequence 的源执行顺序/gap 查询。Message 使用稳定 requestId 和完整 fingerprint，严格封闭 JSON，拒未知/重复/错误字段，不 silent-filter 后发起。注册/凭据撤销与 Inbox 准入同门，历史消息不携带持续写入权。[原文C:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:69) [原文C:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:71) [原文C:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:75) [原文C:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:77)

**手动发起。** context → preview 固定准确 pin、配置 Starter、实际 actor、vars、side effects → stable commandId 确认。确认 body 只引用原 preview，不另传变更 vars；检查 trigger.fire、Starter 当前资格、WF_AUTH、activation、Owner门。事务依序 fence→command→control/activation→Invocation→engine create key→Instance/Token→receipt/outbox/audit；全部同 Main Uow 后才能宣称实例存在。超时查询原命令，不生成新 GUID。[原文C:93](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:93) [原文C:95](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:95) [原文C:97](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:97)

## 5. API、事件与跨 Owner 合同

OA-CONN 自有新 API 前缀 `/api/oa/conn/v1`；采用唯一 authoritative WFC 命令槽和安全 CommandReceipt 投影。碰到已接受 APP 操作时仅保留原 AppCommand+alias，不创建第二个“成功”槽，不改 APP DTO/canonical/hash。[原文C:151](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:151) [原文C:153](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:153)

|路由族（相对前缀）|请求/结果核心|
|---|---|
|GET /options；POST /creation-contexts|purpose/可信Scope；kind TRIGGER/CONNECTOR + 选定 refs，返回选定依赖 heads 和自然键 ABSENT，不造密钥。[原文C:157](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:157)|
|GET /triggers；GET /triggers/{id}/context；POST /triggers；POST /triggers/{id}/versions|分页 snapshot、准确上下文；tagged TriggerDraftFields；返回 root/version/control/schedule/heads/actions。[原文C:159](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:159)|
|POST /triggers/{id}/submit-review；GET /triggers/{id}/review-context；POST /triggers/{id}/decisions|kind+准确 version+impactRef（PUBLISH null）；独立 subjectRef/decision/qualificationRefs。[原文C:161](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:161)|
|POST /triggers/{id}/publish；.../control-impact-previews；GET /control-impact-previews/{id}；.../enable、disable、retire|原 version/decision/control/impact refs+reason；按动作矩阵执行。[原文C:162](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:162)|
|POST /triggers/{id}/manual-fire-previews；.../manual-fires；GET /triggers/{id}/invocations；GET /invocations/{id}|preview 固定 vars/pins/Starter；确认原 preview+command；查已收/实例/执行/应用多轴。[原文C:165](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:165)|
|POST /cron-previews；POST /forecasts；GET /forecasts/{id}|准确定义/dependencies/input/Starter/fromNode；纯诊断，不调用服务节点。[原文C:168](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:168)|
|GET /connectors；GET /connectors/{id}/context；GET /connectors/{id}/versions/{version}；POST /connectors；.../versions|sourceKind/exact identity；MetadataFields 无 AuthJson；masked view 的 hasAuth 不等于有效。[原文C:171](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:171)|
|POST /connectors/{id}/config-checks；.../submit-review；GET .../review-context；POST .../decisions|check 纯离线；ACTIVATE/RETIRE 各自 subject+adoption+review，不能代密钥授权。[原文C:173](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:173)|
|POST /connectors/{id}/control-impact-previews；.../activate、disable、retire|准确 version/action/decision/impact+reason。[原文C:175](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:175)|
|GET /jobs/{id}/context；GET /connector-invocations/{id}/results；POST /jobs/{id}/recoveries|原 Invocation/Result/Job；QUERY_ORIGINAL、APPLY_KNOWN_RESULT、RETRY_PROVEN_NO_EFFECT 的带标签输入。[原文C:177](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:177)|
|GET /commands/{commandId}；GET /review-subjects；GET /review-subjects/{id}/context|原 operation/resource locator；有权独立复核人从列表可达，不能靠 Maker 私有缓存。[原文C:179](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:179)|

入站仅注册 transport 调用：`MessageEnvelope{requestId,schemaRef,payload}`；`EventEnvelope{eventId,schemaRef,sourceRef,sequence?,payload}`。Scope/producer 来自登记，不从任意 admin body 信任。`IngressReceipt{inboxRef,ingestion:RECEIVED/DUPLICATE/QUARANTINED,invocationLocator?,queryPath}`；201 不承诺实例或业务应用。真实认证和凭据 endpoint 由安全 Owner 提供。[原文C:182](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:182)

写入 expectedHeads 覆盖：创建的 Scope/注册/Definition/Starter/policy/ABSENT；版本修改的 content/control/dependencies；publish 的 version/review/control/pins；enable 的 activation+全部准入；fire 的 TriggerControl/DefinitionActivation/StarterAuthority/vars-policy/Invocation；recover 的原 Invocation/Result/Job claim/token/connector/current authority。Root rowVersion 无法替代。[原文C:184](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:184)

Required/Provider 最小合同：RP01 准确 pin/input 的真实 Main Uow PinnedWorkflowAdmission；RP02 全 writer 同 WF_AUTH 与 RegistrationInbox/send control；RP03 provider 稳定幂等 lifetime/query/no-effect/disclosure/采用；RP04 timezone/calendar/cron/DST/misfire；RP05 DurableResultApply 将 mapping、唯一 ResumeReceipt、APP Tx D、OwnerReceipt 链原子闭合。缺项应阻断对应动作，不把“配置存在”晋升为运行采用。[原文C:198](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:198) [原文C:200](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:200) [原文C:202](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:202) [原文C:204](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:204) [原文C:206](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:206)

## 6. 并发、外部未知与失败恢复

**锁与幂等。** 复用 accepted APP 的准确 ScopeAuthorizationFence；缺失/多值/不同映射拒绝，不另建 conn auth。顺序：AuthorizationFence→Command阶段→Trigger/Connector/DefinitionActivation/业务 controls（同类稳定排序）→Invocation/Slot→Instance→Task/Token/FormData→Decision/Inbox→Receipt/Outbox→Audit；跨 Owner 用其协议，不能凭字符串 uowId 宣称原子。[原文C:151](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:151)

**派发。** 外呼前在当前 Running instance、原 node/token、WF_AUTH/control/service/disclosure/Owner 资格下提交 DISPATCH_ADMITTED + stable effectKey，提交后释放事务再 HTTP。effectKey/transportKey 不随 attempt/epoch 改变。先撤权则不得准入；已 admitted 在途后撤权只能表示 CANCELLATION_REQUESTED。若业务要求远端即时撤销而 provider 没有等价 fence，则禁自动该业务写。[原文C:113](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:113) [原文C:115](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:115)

**结果 UNKNOWN。** HTTP 200 可能仅 ACK；网络超时、连接断开、外部完成但本地保存失败都为 UNKNOWN，不等于无效果。provider 声明的幂等生命周期、同 payload、原结果查询必须实际采用；只加 header 无效。非幂等 mutation 无查询时，首次风险调用也只按特定 Owner 策略，未知后不可自动重试/换 key。查询 NOT_OBSERVED 不是永久无效果；人工核证保存受控 evidence assessment，不给 force-success。[原文C:115](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:115) [原文C:119](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:119) [原文C:121](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:121)

**成功应用。** ResultInbox 验 auth/Scope/invocation/operation/schema/fingerprint；同结果重放读原 receipt，异内容隔离。结果事实可由迟到 worker 安全保存，但过期 epoch 不能推进 Job/Token。token 已离开/实例已撤回只存 STALE_TOKEN，不复活流程、不直接补偿。合法结果在同事务内做白名单 OutputVars mapping、ResumeReceipt、token/job/outbox，进入 APP 终态必须同 Tx D Recorder；本地失败只重做 apply，零次新增 CallAsync。业务 Owner APPLIED 必须另有其 receipt；缺前驱沿固定链补齐，不再次外呼。[原文C:119](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:119) [原文C:123](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:123) [原文C:127](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:127) [原文C:129](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:129) [原文C:131](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:131)

**失败应用。** ResultAssessmentFact 分开 effectKnowledge NO_EFFECT_PROVEN/EFFECT_OCCURRED/UNKNOWN 与 retryability RETRY_ALLOWED/DO_NOT_RETRY/HOLD。provider FAILED 不证明无效果；部分效果/未知必须 HOLD。只有可靠无效果、允许重试、attempt未达max且窗口未过，才用原 effect/pin/body 在当前门内创建新 Attempt；否则可靠拒绝、重试耗尽或窗口到期冻结唯一 TerminalFailureFact。同一 Job/Invocation/原 token-node 已 terminal 即永久阻 CallAsync，后续恢复仅本地应用。[原文C:137](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:137) [原文C:139](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:139)

失败 apply 的幂等身份固定 Scope+job+invocation+token/node+failureId+FAILURE_ROUTE。当前合格 worker（或 recovery AND failure.apply）按 fence→command→Instance→Token→FailureFact/receipt/outbox/audit，核验当前 epoch、Running、Active原node/pin。有唯一合法 IsError 边，仅一次走该边并 Job Failed；多条歧义边 INTEGRITY_HOLD，不走成功边。无 error 边则 Instance Suspended、Token Active 留原node+FailureHold、Job Failed；不能通过把 Job 改 Pending 自动外呼。FailureReceipt、Job失败、history/outbox 同事务；本地崩溃重查/重做 apply。[原文C:141](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:141) [原文C:143](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:143) [原文C:145](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:145)

终态失败后迟到矛盾成功仍保存 ResultFact+RESULT_CONFLICT_QUARANTINED，不覆盖失败终态/复活 token。`APPLY_KNOWN_RESULT` 必须 SUCCESS+exactResultRef **或** TERMINAL_FAILURE+terminalFailureRef；`RETRY_PROVEN_NO_EFFECT` 只接受未终结 assessment。恢复权不代最终动作权，失败另需 conn.failure.apply。[原文C:145](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:145) [原文C:147](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:147)

## 7. 权限、租户、秘密与审计

trigger.write/review/publish/control/fire、forecast、connector.write/review/activate/control、recovery、history/export 分别授权；仅 Edit 不允许 Fire，不允许独立 review，也不允许凭据或财务操作。复核检查 Maker/代理 SOD；独立复核列表有当前原文权才可见，不能借 Maker 缓存。回看已完成命令检查当前 read，不要求操作者仍持已失去的旧 write 权。[原文C:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:9) [原文C:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:57) [原文C:179](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:179)

ConnectorMetadataFields 无 AuthJson。编辑时 credential 空值保留既有 binding；删除/生成/轮换是安全 Owner 的独立显式动作。只用 opaque locator/generation；hasAuth 不证明凭据可用。目的地址/operation 必须登记 HTTPS 白名单，不能任意 URL、header、脚本；redirect 重新检查策略，披露按字段 Owner。输出映射不得覆盖 Scope、Starter、pins、权限或业务状态。[原文C:34](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:34) [原文C:81](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:81) [原文C:85](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:85) [原文C:127](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:127)

跨 Scope、撤权、结果导出途中失权都需服务器拒绝并清客户端缓存；错误不得泄 payload/hash/credentials。fence 阻断范围覆盖旧入口、导入、worker、repair，不能只给新页面加检查。[原文C:190](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:190) [原文C:192](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:192) [原文C:200](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:200) [原文X:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:31)

## 8. 页面、预演和服务端校验

界面至少五个区：触发器配置/复核控制、连接器版本/采用、手动发起、cron/forecast、Invocation/Job/结果与恢复。保存、发布、启停、真实发起、预演分别操作；显露 kind/version/adoption、masked credential 和六个资格轴 CONFIGURED/AUTH_PRESENT/HTTP_ACK/EXTERNAL_SUCCEEDED/WORKFLOW_APPLIED/BUSINESS_OWNER_APPLIED，UNKNOWN 不刷成绿色成功。[原文C:188](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:188) [原文C:202](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:202)

cron preview 不写 NextDue，固定返回最多5个 UTC/local/offset/fold。Forecast 使用准确发布 pin 或标 DRAFT_SIMULATION 的 draft revision，不 lazy-pin、不新建 draft；条件 TRUE/FALSE/UNKNOWN；人员 RESOLVED_AT_CUT、ARRIVAL_TIME、REDACTED 分别呈现。服务节点只诊断不外呼，子流程深度8、图步骤100；未知分支、循环、超限和未支持节点返回 PARTIAL+原因+unvisited。COMPLETE_FOR_SUPPORTED_GRAPH 也只是预测。可保存 ForecastRun 诊断，不得创建 Task/Submission/通知/业务写。[原文C:101](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:101) [原文C:103](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:103) [原文C:105](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:105) [原文C:107](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:107)

服务端错误：400 JSON、401 transport、403字段/动作/Starter、404、安全409 body/pin/disabled/stale/conflict、412 config/control/preview变化、422 schema/timeforecast、428缺heads、503 fence/engine participant/external contract不可用；保留 legacy EWF 兼容安全错误。页面请求带 generation 丢弃过时响应；403清缓存；412显示安全差异，不覆盖旧输入后悄悄重提。[原文C:190](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:190) [原文C:192](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:192)

## 9. 开发顺序、兼容与固定代码证据

代码对照固定 Main `90c871...`。旧实现已有三类触发、占位符、水位和64KiB检查，但原文明确仍有 payload冲突、取latest vars、旧结果/启停、按name回落实现、手动每次新GUID、异常直接Fail重试等缺口。原文没有全文审 DbWfConnector HTTP、API key attributes、cron/mapper/validator、定时/事件 worker注册、forecast controller、相关 FE、真实adapter配置；这部分不能由静态设计推断已实现。[原文C:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:15) [原文C:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:19) [原文C:20](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:20) [原文C:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:21) [原文C:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:23) [原文C:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:25)

按依赖实施：①补具体 writer/adapter/FE证据与切换清单；②不可变配置/公开上下文/精确复核和WF_AUTH控制；③slot/Inbox稳定输入与真正引擎同Uow；④纯forecast及页面恢复；⑤派发准入、provider契约、UNKNOWN、claim fencing、结果和终态失败本地apply；⑥APP TxD+OwnerReceipt六轴；⑦旧入口/在途Job单writer切换；⑧获实施授权后执行32AC。真实IAM权限注册、provider和sameTx接口均须逐项采用核验。[原文C:247](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:247)

切换按 Scope+Trigger/Connector+既存Instance/Job 保留旧id并确保单writer；接管后旧路由必须适配稳定key/pin/fence，否则410 TRUSTED_CONN_V1_REQUIRED。旧Job缺pin不能从latest推回历史；旧 SENT/UNKNOWN 只能查询原effect，不换connector/key重发。首次新效果之后只前向处理，不双跑回滚旧worker。[原文C:194](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:194)

## 10. 验收场景与证据状态

所有 CONN-01..32 仍 NOT_RUN。下面是实施时必须保留的 Given/When/Then，不是本次测试结果。[原文A:372](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dd/ddb35e86a0458a61__UA-20261008-X5-OA-CONN-01-STATIC-MD02.json:372)

|AC范围|重点失败路径及必须结果|
|---|---|
|01..03|完整首次启用公开链可达；空影响准确；发布/enable/retire决定不混；预览后新dispatch返回412；停用仍可读原成功命令。[原文C:212](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:212)|
|04..09|双timer争同slot只一Instance；misfire/DST固定；事件同键异payload隔离；超限/未知字段整件拒；ACK后Starter撤权阻发起。[原文C:215](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:215)|
|10..14|TENANT pin停用不APPfallback；凭据空值保留；总call duration≥lease拒；无fire权403；手动超时重提仍原command仅一实例。[原文C:221](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:221)|
|15..17|预演零外呼/Task；多分支UNKNOWN、深8/100步/循环如实PARTIAL；ARRIVAL_TIME与REDACTED不当空名自动通过。[原文C:226](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:226)|
|18..23|先撤权阻send；已admitted不假撤销；无效果终态按IsError/Suspended；非幂等timeout不重发；成功/失败本地apply崩溃后零外呼；旧epoch和staletoken不推进。[原文C:229](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:229)|
|24..28|输出身份字段隔离；矛盾迟到成功不覆盖terminal；APP Recorder不同事务则回滚；Owner receipt缺失不业务成功，gap补链不重调用。[原文C:235](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:235)|
|29..32|旧未知Job切换无双writer；撤权清缓存；模拟/配置始终不晋升UNPROVEN；远端无即时fence且业务要求即时撤销则禁自动写。[原文C:240](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:240)|

## 11. 未完成项与不能从设计推断的内容

材料已经闭合连接器静态局部设计，但实际 provider 幂等/query/noeffect/时效采用、WF_AUTH同一行映射、所有 writer门、Main Uow/APP Recorder真正同事务、权限注册与前端入口尚需编码前逐项对接。正文明确 RP04 未采用时仅阻新timer启用/时间预演，不应错误阻全部事件和消息；RP03等其他门独立判断。[原文C:198](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:198) [原文C:204](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:204) [原文C:206](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:206)

当前未扩读固定代码索引引用的每个实现原件；本册固定代码差距引用原正文的明确审计结论，不能当本次代码全文审计。接受JSON原Scope和治理字段已结构化核对，未声称重新业务审全部X5财政模块。财政 Money/GL合同留各自Owner，本册只使用COMMON真正适用的Scope/fence/Receipt资格，不把财政Command覆盖WFC/APP。[原文C:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:25) [原文X:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:25) [原文X:55](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:55)

## 12. 证据与真实阅读覆盖

本次正文 OA-CONN R1 全文1–249、COMMON R1全文1–94、X5最终复核全文1–166（其中133–153补读）、COMPOSITION-IDENTITIES全文1–51、SOURCE-QUALIFICATIONS全文1–155；接受件的决定、AcceptedBody、SupportingCurrentBodies、组合、ReviewQualifications、OwnerAndExecutionBoundary已按字段结构读，关键段1–17/179–258/301–318/372–392重新定位。此前的原Scope结构读取与本次选段均在 reading JSON 区分记录。[原文C:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/04/04f7fe716dfe5d69__OA-CONN-01.md:1) [原文X:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/62/6271f981bb9187c1__COMMON.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/09/096df8bb50ccf094__CP6_X5_R2_定点独立复核与四专项累计结论_20261008.md:1) [原文I:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e5/e5934d9a539b1c78__COMPOSITION-IDENTITIES.json:1) [原文Q:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/87/875bbd4b5e0ea711__SOURCE-QUALIFICATIONS.json:1) [原文A:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/dd/ddb35e86a0458a61__UA-20261008-X5-OA-CONN-01-STATIC-MD02.json:1)

机器合同见 [platform-engineering-contracts.json](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，逐文件范围/未读段见 [platform-engineering-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。这里只执行文本、身份和链接检查；未运行项目代码、SQL、checker、validator、测试、CI或外部连接器。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
