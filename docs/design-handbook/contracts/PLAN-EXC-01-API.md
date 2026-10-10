# PLAN-EXC-01 接口和恢复状态明细

来源为CP12组合指定的EXC_API_REGISTRY，SHA `054ff9bd3bc61afe`前缀。29主route+7辅助route全部字段语义已读，关联specimen与类型正文仍按组合成员账继续阅读。这里是原静态合同的转排，全部业务NOT_RUN；不存在统一替换所有错误名称的约定。

[完整API原件](<D:/CP6-archives/consolidation-20261010/root-planning-cache/plan-exc-cp12/EXC_API_REGISTRY.json>)｜[模块说明](../modules/PLAN-EXC-01.md)

## 36个入口

| ID | Method / path | 输入 / query | 输出 | 成功状态 | 精确目的 | UoW |
|---|---|---|---|---|---|---|
| EX01 | GET /api/plan/exc/v1/context | — | ContextOptions | [200] | plan-exc.read | READ |
| EX02 | POST /api/plan/exc/v1/cases/search | CaseFilter | CasePage | [200] | plan-exc.read | READ_CUT |
| EX03 | GET /api/plan/exc/v1/cases/{caseId} | — | CaseView | [200] | plan-exc.read | READ |
| EX04 | GET /api/plan/exc/v1/cases/{caseId}/sources | — | SourcePage | [200] | plan-exc.source.read | SOURCE_GRAPH_READ |
| EX05 | GET /api/plan/exc/v1/cases/{caseId}/impacts/{impactId} | — | ImpactView | [200] | plan-exc.impact.read | IMPACT_READ |
| EX06 | GET /api/plan/exc/v1/cases/{caseId}/history | — | HistoryPage | [200] | plan-exc.history.read | READ |
| EX07 | GET /api/plan/exc/v1/operations/{operationId} | — | OperationResourceView | [200] | plan-exc.recover | READ |
| EX08 | GET /api/plan/exc/v1/cases/{caseId}/recovery/{operationId} | — | RecoveryView | [200] | plan-exc.recover | READ |
| EX09 | POST /api/plan/exc/v1/cases/{caseId}/assessments | AssessCommand | CommandReceipt | [202,200] | plan-exc.assess | TX_REGISTER+TX_ASSESS |
| EX10 | POST /api/plan/exc/v1/cases/{caseId}/impacts | ImpactCommand | CommandReceipt | [201,202,200] | plan-exc.impact.build | TX_IMPACT |
| EX11 | POST /api/plan/exc/v1/cases/{caseId}/owner-dispositions | HandoffCommand | CommandReceipt | [202,200] | plan-exc.handoff | TX_HANDOFF |
| EX12 | POST /api/plan/exc/v1/cases/{caseId}/resolutions | ResolveCommand | CommandReceipt | [201,200] | plan-exc.resolve | TX_RESOLVE |
| EX13 | POST /api/plan/exc/v1/cases/{caseId}/dismissals | DismissCommand | CommandReceipt | [201,200] | plan-exc.alert.dismiss | TX_DISMISS |
| EX14 | POST /api/plan/exc/v1/cases/{caseId}/alert-restorations | RestoreAlertCommand | CommandReceipt | [201,200] | plan-exc.alert.restore | TX_RESTORE_ALERT |
| EX15 | POST /api/plan/exc/v1/cases/{caseId}/recalculations | RecalcCommand | CommandReceipt | [202,200] | plan-exc.recalc | TX_RECALC |
| EX16 | POST /api/plan/exc/v1/operations/{operationId}/resumptions | ResumeCommand | CommandReceipt | [202,200] | plan-exc.recover | TX_RESUME |
| EX17 | POST /api/plan/exc/v1/cases/{caseId}/projection-repairs | RepairCommand | CommandReceipt | [201,202,200] | plan-exc.repair | TX_REPAIR |
| EX18 | POST /api/plan/exc/v1/exports | ExportCommand | ExportRecordedResult | [202,200] | plan-exc.export | TX_EXPORT_BUNDLE_REGISTER |
| EX19 | GET /api/plan/exc/v1/exports/{exportId} | — | ExportView | [200] | plan-exc.export | READ |
| EX20 | GET /api/plan/exc/v1/source-artifacts/{artifactId} | — | SourceArtifactDownloadMetadata | [200] | plan-exc.source.read | READ |
| M01 | POST /api/plan/exc/v1/source-events | SourceEventIntake | IngressReceipt | [202,200] | plan-exc.ingest.source | TX_INGEST |
| M02 | POST /api/plan/exc/v1/disposition-receipts | DispositionIntake | IngressReceipt | [202,200] | plan-exc.ingest.disposition | TX_DISPOSITION_RECEIPT |
| M03 | POST /api/plan/exc/v1/mrp-receipts | MrpReceiptIntake | IngressReceipt | [202,200] | plan-exc.ingest.mrp | TX_MRP_RECEIPT |
| M04 | POST /api/plan/exc/v1/current-observations | CurrentObservationIntake | IngressReceipt | [202,200] | plan-exc.observe.current | TX_CURRENT |
| M05 | GET /api/plan/exc/v1/ingress/{issuerOwner}/{eventId} | — | IngressReceipt | [200] | plan-exc.own-event.read | READ |
| EX21 | GET /api/plan/exc/v1/legacy/{legacyId} | — | LegacyReadView | [200] | plan-exc.read | READ_FRESH_LEGACY |
| EX22 | POST /api/plan/exc/v1/legacy/search | LegacyFilter | LegacyPage | [200] | plan-exc.read | READ_FRESH_LEGACY |
| EX18_PREPARE | POST /api/plan/exc/v1/export-scopes | ExportScopePrepare | ExportScopeView | [200] | plan-exc.export | TX_EXPORT_PREPARE |
| EX03_PREPARE | GET /api/plan/exc/v1/cases/{caseId}/action-preparation | ["action"] | ActionPreparation | [200] | EXPLICIT_QUERY_ACTION_ENUM | READ_FRESH_ACTION_PREPARATION |
| EX07_SLOT | GET /api/plan/exc/v1/operation-slots | ["caseId","episodeId","kind","requestKey"] | OperationView | — | — | — |
| EX19_CONTENT | GET /api/plan/exc/v1/exports/{exportId}/content | — | text/csv; charset=utf-8 | — | plan-exc.export | — |
| EX20_CONTENT | GET /api/plan/exc/v1/source-artifacts/{artifactId}/content | — | original source mediaType | — | source/history purpose and exact object/field mask | — |
| EX05_NODES | GET /api/plan/exc/v1/cases/{caseId}/impacts/{impactId}/nodes | — | ImpactNodePage | — | plan-exc.impact.read | — |
| EX05_EDGES | GET /api/plan/exc/v1/cases/{caseId}/impacts/{impactId}/edges | — | ImpactEdgePage | — | plan-exc.impact.read | — |
| EX07_EXPORT_SLOT | GET /api/plan/exc/v1/export-operation-slots | ["bundleId","requestKey"] | ExportOperationView | — | plan-exc.export | — |
| EX07_EXPORT_SLOT_RESULT | GET /api/plan/exc/v1/export-operation-slots/result | ["bundleId","requestKey"] | ExportRecordedResult | [200] | plan-exc.export + explicit originalSlot/read + all original bundle member/column scopes | PURE_ORIGINAL_RESULT_READ |

表中“—”表示该registry行未独立重复声明，不推导成开放授权/无错误/任意成功。须同时消费主文的公共规则和具体typed根。GET只读；EX02/EX22虽POST不建业务槽。EX18_PREPARE会持久化export preparation，不能与只读列表混类。

EX02的legacyOnly应400 LEGACY_FILTER_REQUIRES_LEGACY_ROUTE，未映射旧行走EX22；EX03_PREPARE只允许精确action枚举，不能回退general-read。EX07_EXPORT_SLOT_RESULT读取原202的完整不可变结果body，HTTP200重放；EX07_EXPORT_SLOT/EX19另看动态状态。原source-artifact权不替export目的。

## 错误与恢复模式

| code | HTTP | retryMode |
|---|---:|---|
| CURSOR_EXPIRED | 410 | READ_NEW_PREDECESSOR |
| EXPORT_LIMIT_EXCEEDED | 422 | NONE |
| UOM_NOT_EXACT | 422 | NONE |
| SHARED_PROTECTION_UNKNOWN | 422 | NONE |
| INDIVISIBLE_GROUP_CONFLICT | 409 | NONE |
| UNRESOLVED_EXECUTION | 422 | NONE |
| GAP_NOT_ELIMINATED | 422 | NONE |
| ALERT_POLICY_REQUIRED_FENCED | 503 | NONE |
| STALE_HEAD | 412 | READ_NEW_PREDECESSOR |
| ORIGINAL_SLOT_BODY_CONFLICT | 409 | NONE |
| RESULTSET_INCOMPLETE | 422 | NONE |
| RECOVERY_INPUT_CHANGED | 422 | NONE |
| CORE_AUTHORIZATION_REQUIRED_FENCED | 503 | NONE |
| CURSOR_PERMISSION_CHANGED | 412 | READ_NEW_PREDECESSOR |
| EXPORT_FIELD_DENIED | 403 | NONE |
| ACTION_CURRENT_MISMATCH | 412 | READ_NEW_PREDECESSOR |
| SOURCE_CURRENT_CHANGED | 412 | READ_NEW_PREDECESSOR |
| BODY_SCHEMA_INVALID | 422 | NONE |
| AUDIENCE_ACTION_DENIED | 422 | NONE |
| COMMIT_OUTCOME_UNKNOWN | 503 | QUERY_ORIGINAL |
| LOCAL_UOW_ROLLED_BACK | 503 | SAME_ORIGINAL_SLOT |
| WRITER_EPOCH_CHANGED | 412 | READ_NEW_PREDECESSOR |
| OWNER_SCOPE_MISMATCH | 422 | NONE |
| EVENT_BODY_CONFLICT | 409 | NONE |
| MEMBERS_NOT_EXACT | 422 | NONE |
| MEMBER_BODY_MISMATCH | 422 | NONE |
| ATTEMPT_NAMESPACE_MISMATCH | 422 | NONE |
| PROVIDER_REQUIRED_FENCED | 503 | NONE |
| LEGACY_FILTER_REQUIRES_LEGACY_ROUTE | 400 | NONE |
| PURPOSE_NOT_AUTHORIZED | 403 | NONE |

该errors表与cp03ExactAdditionalProblemCodes及具体用例共同约束。原件同时出现ORIGINAL_SLOT_BODY_CONFLICT与IDEMPOTENCY_BODY_CONFLICT等具名分支，调用方不得随意映射为同一外部code；AC053/089的完整Problem明确IDEMPOTENCY_BODY_CONFLICT。登记前缺资格operationId=null/NONE。当前cursor签名/codec/filter错误是422 BODY_SCHEMA_INVALID，服务资格/retention未知503 PROVIDER_REQUIRED_FENCED，合格到期410 CURSOR_EXPIRED。

## Inbox允许后继

| 当前 | 允许后继 |
|---|---|
| RECEIVED | WAITING_PREDECESSOR, WAITING_SOURCE, WAITING_LOCAL_APPLY, READY, CONFLICT, BLOCKED_AUTH, REQUIRED_FENCED |
| WAITING_PREDECESSOR | READY, CONFLICT, BLOCKED_AUTH |
| WAITING_SOURCE | READY, WAITING_LOCAL_APPLY, RETRYABLE_ERROR, BLOCKED_AUTH |
| WAITING_LOCAL_APPLY | READY, RETRYABLE_ERROR, BLOCKED_AUTH |
| RETRYABLE_ERROR | READY, BLOCKED_AUTH, REQUIRED_FENCED |
| READY | APPLIED, HISTORY_ONLY, WAITING_SOURCE, WAITING_LOCAL_APPLY, RETRYABLE_ERROR, CONFLICT |
| BLOCKED_AUTH | READY, REQUIRED_FENCED |
| REQUIRED_FENCED | READY, BLOCKED_AUTH |
| APPLIED | 终态，无后继 |
| HISTORY_ONLY | 终态，无后继 |
| CONFLICT | 终态，无后继 |

## EXC本地重算进度与MRP事实

| 本地stage | 必须对应的事实或状态 |
|---|---|
| REGISTERED | local originalslot commit |
| WAITING_OWNER | durable original work |
| OWNER_UNKNOWN | maybeSent/NOT_OBSERVED/UNKNOWN original lookup |
| PREPARING | actual PREPARING receipt |
| INPUT_SEALED | actual Input sealed receipt |
| RUN_REQUESTED | actual Run produced receipt |
| CALCULATING | actual Attempt started |
| OUTPUT_SEALED | actual OutputSeal |
| VALIDATING | Result sealed no selection |
| SELECTED | true Selection no Publication |
| PUBLISHING | original publishslot known outcome unobserved |
| PUBLISHED | true immutable Publication captured |
| WAITING_LOCAL_APPLY | Owner publication true/local apply pending |
| LOCAL_APPLIED | exact once local ResultConsumption |
| RETRYABLE_ERROR | recoverable local error originalbody fixed |
| FAILED | actual failed Run/Attempt oldES retained |
| CANCELLED | true CANCEL winner |
| BLOCKED_AUTH | fresh purpose denied |
| REQUIRED_FENCED | actual qualifier missing |

原native receipt.RESULT_SEALED映射本地OUTPUT_SEALED；原PUBLISHED回执与本地LOCAL_APPLIED分开。没有把Operation.State改为未声明SUCCEEDED；实际本地应用后为APPLIED。所有native未来引用必须null，原PUBLISHED_RECALC26不因今天resolve/recover权限变化被重写。
