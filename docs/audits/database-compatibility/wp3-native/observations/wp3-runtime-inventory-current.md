# WP3 当前运行时盘点（只读）

检查完成 UTC：2026-10-03T08:04:23.8467739Z；工作树：D:\CP6\tmp\worktrees\db-compat-wp2-20261002；检查时 HEAD：45cf2ca58b792d936aab93ee4eaa771c4a77746d。WP2工作树包含未提交改动，精确输入以 JSON 中逐文件 SHA 为准。
本报告只读取源码。没有编译、测试、数据库、凭据或远端操作，也没有重做 WP2 完整审查。所有下列验证均为后续必需场景，尚未在 WP3 执行。
原盘点保留：D:\CP6\tmp\wp3-runtime-inventory.md；SHA256：F7D1BC89AA57A00B33B2ECF4425EED7E62AB09FC1C3F7E10969970B69128E9AB。原 WP1/WP2 成功结果仅可按原来源和实际适用输入复用。

## 已确认的范围与契约
- Preserve existing resource case, GuidN/GuidD, hashes, tenant scope and lock order. Global ERP MessageId/startup/ORD keys do not gain a tenant scope.
- First creation/absence needs logical mutual exclusion; FOR UPDATE alone cannot lock nonexistent record.
- PG advisory lock plus Serializable snapshot timing is a protocol design question to prove with real simultaneous first creation and expected rollback/retry, not a completed implementation.
- Session-owned backfill lock spans committed batches, so transaction-only replacement does not preserve its lifecycle.
- ORD is global never-reset counter; the plan cross-tenant test means same stream plus business isolation, not new tenant counters.
- Two existing nonSQL conditional ExecuteUpdate fences must be preserved and exercised; ordinary read is not equivalent.
- WP2 owns already-installed native SQL rowversion/PG byte8 and durable generation triggers; WP3 adopts actual callers without duplicate model migrations.
- Existing broad capacity DbUpdateException retry is accurately recorded; normalized error contract must keep unrelated integrity failures out of safe retries.
- The six authoritative sites are five named helpers and one inline CAD lease validation, correcting old inventory wording.
- SQL-only business guards remain until complete caller dependencies have real PG implementations; normal runtime PostgreSQL guard remains through pending WP4-WP6.

现有 runtime 仍拒绝普通 PostgreSQL 启动；WP2仅开放初始化。下一工作包应从已完成远端核对的最新 main 创建独立分支，不因本盘点生成而宣称运行时兼容。
JSON 完整保留 source/hash/line/text、所有 Provider 分支及错误/SQL 能力逐行 census；下表按行为/后续工作包分组，匹配行数不当成业务测试数量。

## 16个资源锁与直接调用方

| 入口 / 语句行 | 生命周期 / 超时ms | 原资源键 | 当前PG行为 / 原错误 | 直接caller行 |
| --- | --- | --- | --- | --- |
| CP6.Core/Services/ErpIntegration/ErpSqlLock.cs:14 (helper:8) | Transaction / 10000 | caller supplied; eight call sites below | Rejects nonSQL or missing CurrentTransaction; C03_SQL_CONTENTION |  |
| CP6.Space.Infrastructure/SpaceEditLeaseService.cs:422 (helper:403) | Transaction / 15000 | cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N} | PG skips at408; EditLeaseHeld/409 | 57,141,197,254 |
| CP6.Space.Infrastructure/SpaceDesignV1Service.cs:6388 (helper:6369) | Transaction / 15000 | cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N} | PG skips at6374; CommandConflict/409 | 692,1223,2031 |
| CP6.Space.Infrastructure/SpaceDesignV1Service.cs:6423 (helper:6405) | Transaction / 15000 | cp6:space:version-floor-init:{tenant:N}:{version:N} | PG skips at6409; ConcurrencyConflict/409 | 261 |
| CP6.Space.Infrastructure/SpaceUnderlayHistory.cs:336 (helper:318) | Transaction / 15000 | cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N} | PG skips at323; EditLeaseHeld/409 | 34 |
| CP6.Space.Infrastructure/SpaceExcelCadApplyService.cs:663 (helper:644) | Transaction / 15000 | cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N} | PG skips at649; EditLeaseHeld/409 | 82 |
| CP6.Space.Infrastructure/SpaceExcelCadApplyJobStepExecutor.cs:1898 (helper:1878) | Transaction / 15000 | cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N}; explicit tenant argument | PG skips at1884; EditLeaseHeld/executor failure | 133 |
| CP6.Space.Infrastructure/SpaceCadParseService.cs:1476 (helper:1457) | Transaction / 15000 | cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N} | PG skips at1462; CommandConflict/409 | 1387 |
| CP6.Space.Infrastructure/SpaceCadParseService.cs:1848 (helper:1825) | Transaction / 15000 | cp6:space:cad-parse:{tenant:N}:{source:N} | PG skips on ProviderName at1830; Conflict | 124,1556 |
| CP6.Space.Infrastructure/SpaceAiAtomicApplyService.cs:400 (helper:377) | Transaction / 15000 | cp6:space:ai-apply:{tenant:N}:{run:N} | PG skips on ProviderName at382; AiReviewConflict/409 | 48 |
| CP6.Space.Infrastructure/SpaceAiRetentionStore.cs:208 (helper:185) | Transaction / 0 | cp6:space:ai-retention:{tenant:N} | PG skips on ProviderName at190; SpaceAiRetentionBusyException | 30 |
| CP6.Space.Infrastructure/SpaceCadProviderCapabilityService.cs:471 (helper:456) | Transaction / 15000 | space:cad-provider:{tenant:N}:{site:N} | IsRelational guard permits PG then executes TSQL; CadProviderRevisionConflict/409 | 58 |
| CP6.Space.Infrastructure/SpaceValidationInfrastructure.cs:103 (helper:83) | Transaction / 15000 | CP6:Space:Validation:{tenant:D}:{version:D} | PG skips SQL block at96; 51000/SPACE_VALIDATION_LOCK_UNAVAILABLE |  entry: CP6.WebApi/Controllers/Space/SpaceValidationController.cs:62 |
| CP6.Space.Infrastructure/SpaceHistoricalRepublishService.cs:77 (helper:36) | Transaction / 15000 | CP6:Space:Republish:{tenant:D}:{Hash(normalizedKey)} | PG skips SQL block at69; 51021/SPACE_REPUBLISH_LOCK_UNAVAILABLE |  entry: CP6.WebApi/Controllers/Space/SpacePublishController.cs:65 |
| CP6.WebApi/Seed/SpaceAuditPermissionSeed.cs:24 (helper:140) | Transaction / 15000 | CP6:Seed:SpaceAuditPermission:v1 | PG process SemaphoreSlim plus Serializable; SQL execution strategy; SPACE_AUDIT_PERMISSION_SEED_LOCK_UNAVAILABLE | 138 entry: CP6.WebApi/Program.cs:1232 |
| CP6.WebApi/BackgroundServices/SpaceIntegrationEventOccurredAtUtcBackfill.cs:17 (helper:39) | Session / 30000 | CP6:SpaceIntegrationEvent:OccurredAtUtc:v1 | PG process SemaphoreSlim only; SPACE_OCCURRED_AT_UTC_BACKFILL_LOCK_UNAVAILABLE |  entry: CP6.WebApi/Program.cs:1047,CP6.WebApi/Program.cs:1048 |

ERP共享helper的8个call sites：
- CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs:270 — c03:message: + SHA256 UTF8 MessageId; global across tenants。WP3 capability and existing key/order; WP4 full message/replay/bridge behavior. Worker external hook effects must not be replayed by a generic DB retry.
- CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs:232 — c03:aggregate:{tenant:D}:{kind:int}:{aggregate:D}; message before aggregate。WP3 capability and existing key/order; WP4 full message/replay/bridge behavior. Worker external hook effects must not be replayed by a generic DB retry.
- CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs:37 — c03:replay:{tenant:D}:{operation:D}; before message lock。WP3 capability and existing key/order; WP4 full message/replay/bridge behavior. Worker external hook effects must not be replayed by a generic DB retry.
- CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs:47 — c03:message: + SHA256 UTF8 MessageId; same processor namespace。WP3 capability and existing key/order; WP4 full message/replay/bridge behavior. Worker external hook effects must not be replayed by a generic DB retry.
- CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs:75 — c03:delivery-replay:{tenant:D}; tenant-wide audit first insertion。WP3 capability and existing key/order; WP4 full message/replay/bridge behavior. Worker external hook effects must not be replayed by a generic DB retry.
- CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs:105 — c03:delivery-result:{tenant:D}:{outbox:D}。WP3 capability and existing key/order; WP4 full message/replay/bridge behavior. Worker external hook effects must not be replayed by a generic DB retry.
- CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs:144 — c03:bridge:{tenant:D}:{orderKey}; delivery-replay precedes bridge。WP3 capability and existing key/order; WP4 full message/replay/bridge behavior. Worker external hook effects must not be replayed by a generic DB retry.
- CP6.WebApi/BackgroundServices/ErpOrderBridgeWorker.cs:45 — c03:bridge:{tenant:D}:{orderKey}; same replay namespace。WP3 capability and existing key/order; WP4 full message/replay/bridge behavior. Worker external hook effects must not be replayed by a generic DB retry.

## 6个同连接Context工厂

| 工厂 / SQL options行 | 事务附着行 | 方向 / 目标profile | 保留事务约定 |
| --- | --- | --- | --- |
| CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs:141 / 142 | CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs:143 | CP6 -> IdentityMessagingContext; IdentityPriority | Priority enqueue and business snapshot joint commit/rollback |
| CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs:265 / 266 | CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs:74 | ErpIntegrationContext -> CP6Context; Core | Inbox/business/aggregate/Outbox same physical connection and transaction |
| CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs:30 / 31 | CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs:32 | ErpIntegrationContext -> CP6Context; Core | Replay/audit and tenant checks in queue transaction |
| CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs:177 / 178 | CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs:71 | ErpIntegrationContext -> CP6Context; Core | Replay result/bridge audit and tenant checks in queue transaction |
| CP6.WebApi/Services/CrmOidcGrantStore.cs:132 / 135 | CP6.WebApi/Services/CrmOidcGrantStore.cs:136 | SqlConnection + DbTransaction -> CP6Context; Core | Grant/family consumption and snapshot revocations atomic; SQL connection signature/store provider flow WP4 |
| CP6.Space.Infrastructure/Cp6SpaceRuntimeMaterializer.cs:129 / 131 | CP6.Space.Infrastructure/Cp6SpaceRuntimeMaterializer.cs:138 | SpaceContext -> CP6Context; Core | Published version/hash and Core projections same transaction/tenant; inner dispose leaves outer connection usable |

复用 DatabaseContextOptions.cs:30–40 的非拥有 DbConnection overload 和 DatabaseMigrationProfile.For；显式选 Provider/profile，不用连接字符串猜测，也不在嵌套工厂另开物理连接。CrmOidcGrantStore 的 SqlConnection 签名及整套 raw store 仍由 WP4处理。

## 编号、时钟与身份cursor

ORD：Global counter per FuncCode, no TenantId, never reset; yyyyMM only formats prefix; int LastSeq and Guid Id. ORD immediately reserves in caller transaction or independently commits. Pending manual ORD changes fail. NonORD tracked/Local allocation remains deferred.
- CP6.Core/Services/Erp/BackorderService.cs:151: var newWebOrderNo = (await DocNumber.NextAsync(_db, "ORD")).No;
- CP6.Core/Services/Erp/EstimateCalcService.cs:97: var (mainNo, nextMain) = await DocNumber.NextAsync(_db, "EMC");
- CP6.Core/Services/Erp/EstimateCalcService.cs:192: var (mainNo, nextMain) = await DocNumber.NextAsync(_db, "EMC");
- CP6.Core/Services/Erp/FscChecklistService.cs:230: var (no, _) = await DocNumber.NextAsync(_db, code);
- CP6.Core/Services/Erp/OrderService.cs:473: var (no, _) = await DocNumber.NextAsync(_db, "ORD");
- CP6.Core/Services/Erp/PlateMoldService.cs:334: var (no, _) = await DocNumber.NextAsync(_db, "MLD");
- CP6.Core/Services/Erp/PlateMoldService.cs:625: var (webOrderNo, _) = await DocNumber.NextAsync(_db, "ORD");
- CP6.Core/Services/Erp/ProductService.cs:337: var (no, _) = await DocNumber.NextAsync(_db, "PRD");
- CP6.Core/Services/Erp/QuotationService.cs:197: var (mainNo, nextMain) = await DocNumber.NextAsync(_db, "QTN");
- CP6.Core/Services/Erp/QuotationService.cs:404: var (mainNo, nextMain) = await DocNumber.NextAsync(_db, "QTN");
- CP6.Core/Services/Plan/MrpEngine.cs:43: var (runNo, _) = await DocNumber.NextAsync(_db, "MRP");
- CP6.Core/Services/Space/LocationPublishService.cs:90: var (_, seq) = await DocNumber.NextAsync(_db, "LPB");
- CP6.Core/Services/Space/LocationPublishService.cs:179: var (_, seq) = await DocNumber.NextAsync(_db, "LPB");
- CP6.Core/Services/Space/LocationPublishService.cs:224: var (_, seq) = await DocNumber.NextAsync(_db, "LPB");
PUB/FIN/MES/WMS另有规则，不能把ORD原子分配器结果外推到这些read/increment服务；各自真实并发验收在WP4登记。精确引用见JSON。

- CP6.Space.Infrastructure/SpaceEditLeaseService.cs:395；protocol:390；callers:40,67,151,207,264；ReadAuthoritativeUtcNowAsync; fallback ISpaceClock.UtcNow
- CP6.Space.Infrastructure/SpaceDesignV1Service.cs:6361；protocol:6356；callers:6275；ReadAuthoritativeUtcNowAsync; fallback RequireUtcNow
- CP6.Space.Infrastructure/SpaceUnderlayHistory.cs:310；protocol:305；callers:124,217,286；ReadAuthoritativeUtcNowAsync; fallback RequireUtcNow
- CP6.Space.Infrastructure/SpaceExcelCadApplyService.cs:636；protocol:631；callers:611；ReadAuthoritativeUtcNowAsync; fallback RequireUtcNow
- CP6.Space.Infrastructure/SpaceExcelCadApplyJobStepExecutor.cs:1870；protocol:1865；callers:1846；ReadAuthoritativeUtcNowAsync; fallback RequireUtcNow
- CP6.Space.Infrastructure/SpaceCadParseService.cs:1432；protocol:1423；callers:1394；Inline EnsureActiveEditLeaseAsync query; fallback RequireUtcNow
这些租约UTC时钟应读取数据库实际前进的时刻，验证长事务/锁等待/application skew；保留UTC和Min/Max例外契约。业务GETDATE/default/date字段不随此切片改成UTC。

IdentitySnapshotReader.cs:19/27仍为v1 purpose/MAX(CONVERT(bigint,RowVersion))；controller调用在CrmIdentityController.cs:29/36。WP2已安装CrmIdentityTenantGeneration及SQL/PG mutation triggers；WP3使用generation和v2，保持Boundary字符串、opaque NextCursor、LastAggregate排序/API外形。需要跨页update/tombstone/physicalDELETE、回滚、empty tenant、跨tenant、旧cursor拒绝、真实进程重启场景。

## 8个SQL错误classifier与现有重试边界

- CP6.Core/Services/ErpIntegration/ErpCommerceAuthority.cs:34,35,36,39 — Direct inner SqlException; any SQL error2601/2627 plus exact IX_T_WebBusinessPartner_TenantId_CrmAccountId message。Specific binding conflict only; normalized constraint identity needed；业务验收：WP4。
- CP6.Space.Infrastructure/EfSpaceVersionClone.cs:50,106,486,487,489,493 — Walk InnerException chain; SQL1205 bounded retry, SQL2601/2627 duplicate helper。Preserve known uniqueness and whole clone transaction rollback/replay; raw clone SQL WP5；业务验收：WP5。
- CP6.Space.Infrastructure/EfSpaceAiCapacityLedger.cs:11,136,139,141,142,303,306,308,309 — Up to3 attempts; broad DbUpdateException plus direct SqlException1205; explicit rollback between attempts。Inventory correction: broad DbUpdateException is not only EF optimistic conflict. Restrict retry dispositions from actual provider/classification; no statement retry in aborted PG transaction；业务验收：WP5。
- CP6.Space.Infrastructure/SpaceFieldPolicyService.cs:225,226,228 — GetBaseException SQL2601/2627 -> FieldPolicyConflict409。Known business uniqueness only; unrelated integrity error remains failure；业务验收：WP5。
- CP6.Space.Infrastructure/SpaceExternalOrganizationService.cs:415,416,418 — GetBaseException SQL2601/2627 -> supplied conflict409。Maintain caller conflict contract and tenant/business constraint identity；业务验收：WP5。
- CP6.Space.Infrastructure/SpaceExternalGrantService.cs:365,366,368 — GetBaseException SQL2601/2627 -> ExternalGrantConflict409。Maintain grant uniqueness and transaction boundary；业务验收：WP5。
- CP6.Space.Infrastructure/SpaceExcelMappingService.cs:379,380,382,386,396 — SQL2601/2627 -> rollback/clear/reload replay; separate EF concurrency catch。Reload only in usable context after PG rollback; same key plus different input stays conflict；业务验收：WP5。
- CP6.Space.Infrastructure/SpaceCadMappingProfileService.cs:242,243,245,249,259 — SQL2601/2627 -> rollback/clear/reload replay; separate EF concurrency catch。Same replay/rollback distinction as Excel mapping；业务验收：WP5。
CP6Context caller savepoint/SaveChanges(false)状态保留、ERP新失败事务、SpaceRetryFinalizer fresh context和已提交核验必须保留。Current DatabaseContextOptions 没有EnableRetryOnFailure；仅调用CreateExecutionStrategy不能证明PG40001自动重试。PG raw命令错误后整个事务可能中止，需要真实回滚/新context试验。

## 已登记的业务适配与延期范围

| 入口 | 当前假设 | 最小实施/验收责任 |
| --- | --- | --- |
| CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs:20,148,150,152,176,177 | Local tracked snapshot short-circuits locked reread; optional first creation uses SQL gap lock; rejects PG | WP3 explicit missing-resource/transaction contract; WP4 full writer/revocation/outbox paths |
| CP6.Core/Services/CrmIdentity/IdentityBootstrapService.cs:15,16,23 | Serializable optional Bootstrap read executes TSQL on all providers | WP3 absent-tenant resource contract; WP4 complete bootstrap atomicity |
| CP6.Core/Services/CrmIdentity/CrmServiceTokenRecordStore.cs:31,32 | Raw SQL locked token then snapshot writer | WP4 provider-specific write/revoke flow uses WP3 lock/error primitives |
| CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs:83,273 | Locked BusinessPartner and SQL-only require guard remain | WP4 actual partner/command flow; do not remove guard until actual capabilities replace all dependencies |
| CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs:17,19,22,24 | SQL caller transaction required; locked BP/quotation/details | WP4 provider row locks and order chain preserve tenant/Fx/token predicates |
| CP6.Core/Services/Erp/OrderService.cs:109,111 | SQL transaction + current tenant + Fx requirement | WP4 retain business checks when replacing provider check |
| CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs:28,29 | Rejects PG before transaction | WP4 real replay acceptance after shared resource/context capabilities |
| CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs:107,146,183 | Rejects PG plus raw Outbox/bridge locked reads | WP4 replay state/audit/input token fence |
| CP6.WebApi/BackgroundServices/ErpOrderBridgeWorker.cs:44,45,46 | ReadCommitted/app lock/raw locked bridge held around downstream hooks | WP4 external side-effect ordering; no generic automatic hook retry |
| CP6.Core/Services/Sys/RefreshTokenService.cs:184,185 | SQL lock family row; PG ordinary EF read | WP4 family lock before refresh/grant/token flow |
| CP6.WebApi/Services/CrmOidcGrantStore.cs:56,68,71,79,87,90,104,115,123,132,135,146,156,158 | Direct SqlConnection, TOP/OUTPUT and family lock; SqlConnection method signature | WP4 atomic update/delete-returned-row store; WP3 shared connection helper only |
| CP6.Core/Services/Pur/PurchaseRequestService.cs:231,238 | SQL row lock; nonSQL reload/read | WP4 callback replay and fence acceptance |
| CP6.Core/Services/Wms/WmsBinConsumer.cs:314,322,348 | SQL lock or existing relational conditional same-value ExecuteUpdate | WP4 retain actual owner/status/expiry/tenant predicates; do not replace fallback with plain read |
| CP6.Core/Services/Integration/DeadLetterNotifier.cs:245,253,282 | SQL lock or existing conditional same-value ExecuteUpdate | WP4 preserve existing fenced notification behavior |
| CP6.Space.Infrastructure/SpaceGenerationApplyStepExecutor.cs:309,317,318,1600,1610,1614,1624,1628,1638 | IsRelational reaches TSQL on PG; lock run then version then model | WP5 row locks preserve tenant/IsDeleted predicates and original ordering |
| CP6.Space.Infrastructure/SpaceAiRunRecoveryService.cs:115,306,533,542 | SQL locked run, PG plain read | WP5 retry/recovery owner fence |
| CP6.Space.Infrastructure/EfSpaceFileSafety.cs:367,471,516,529 | Raw TSQL with no provider guard; includeDeleted controls filter bypass | WP5 file reference/delete consistency; another caller SpaceDesignV1Service3658 |
| CP6.Space.Infrastructure/EfSpaceAiCapacityLedger.cs:24,62,94,109,111,180,234,259,372,428,443,457,470 | SQL row/gap locks, TOP1/READPAST, workslot/budget operations | WP3 cohesive claim/skip-locked and mutation-return primitives; WP5 actual capacity/money/expiry flow |
| CP6.Space.Infrastructure/SpaceDesignV1Service.cs:3391,3441,3449,3455,3498 | Optional lease read/create not inside helper explicit resource-lock/transaction; existed on SQL too | WP5 real concurrency reproducer first; record existing boundary without unrelated WP3 redesign/BUG assertion |
| CP6.Core/Services/Space/Observability/SpaceAuditQueryService.cs:205,226,227,251,252 | SQL DataLength vs other-provider string-length branch | WP5 actual byte-bound audit query; not a row-lock fallback |
| CP6.Core/Services/Wf/WfServiceJobService.cs:55,83,87 | EF rowversion claim with concurrency catch/reload; no raw SQL required | WP4 real two-worker/expired-old-owner workflow acceptance; preserve existing optimistic option |
| CP6.Core/Services/Mes/MesDashboardDapperService.cs:26,35,45 | Dapper stored procedures usp_GetMesDashboardSummary/usp_GetMesDailyTrend/usp_GetMesProcessProgress | WP5 reports result/sort/scope adapters |
| CP6.WebApi/Controllers/Sys/DashboardController.cs:59 | Raw TOP8 Dapper query | WP5 query/sorting and actual tenant/global scope |
| CP6.Core/Services/Platform/GdprService.cs:219,220 | SQL joined DELETE then grant delete | WP5 scope-preserving provider query; no new unrelated scope |

## 最小连续实施切片及真实双库门禁

### 1. A-error-and-transaction-contracts

Freeze small provider capabilities and precise error/retry disposition。SQL adapters preserve existing results; PG categories carry SQLSTATE and canonical constraint identity. Keep optimistic conflict, unique constraint, deadlock, serialization and cancellation distinct. Bounded retry belongs to whole safe transaction owner; no blanket provider retry switch.
- 必需：Real SQL2601/2627 vs PG23505; preserve specific constraint identity including PG physical truncated names
- 必需：Real deadlock/serialization events with rollback and a fresh valid transaction/context
- 必需：Unrelated FK/check/unknown unique failures do not become duplicate success
- 必需：EF optimistic token conflict is distinct from infrastructure transient retry
仍未覆盖：Full ERP/Space business replay/external side effects WP4/WP5

### 2. B-resource-lock-and-clock

Implement existing 16 resource-lock lifecycles and 6 authoritative UTC sites。SQL sp_getapplock behavior/key/order/timeout/errors unchanged; deterministic PG advisory resource identity, caller transaction required for transaction locks; lifecycle explicit for Session lock. Clock reads actual advancing DB UTC instant.
- 必需：Two actual connections and a second process contend on same key; distinct tenant keys proceed
- 必需：Cross-entrypoint floor edit mutual exclusion on identical GuidN resource vectors
- 必需：Absent-record first creation under real concurrent Serializable transactions; any expected abort receives full rollback/fresh retry by actual owner
- 必需：Exact0 no-wait, finite timeout, cancellation, commit/rollback/dispose release and no leaked session lock
- 必需：Backfill multi-batch cross-process exclusion and release on fail/cancel; pending-row timezone behavior unchanged
- 必需：DB wall clock advances during long transaction and lock wait despite skewed application clock
仍未覆盖：Full lease/CAD/AI/publish flows and owner-fence business acceptance WP5; no claim that lock primitive alone passes them

### 3. C-ORD-atomic-allocator

Make only the existing immediate ORD allocator equivalent on both providers。Global per FuncCode never-reset counter; exact format/int result; join caller transaction or independent owned short Serializable reservation; pending tracked manual ORD changes rejected; nonORD Local/deferred behavior retained. No standalone PostgreSQL sequence or new TenantId.
- 必需：Concurrent absent ORD creation; concurrent existing increment; unique numbers from mixed ordinary/backorder/CRM writer protocols
- 必需：Two tenants share global stream while business tenant scope remains intact
- 必需：Caller transaction rollback restores counter; caller-free committed reservation can leave gap
- 必需：Exact yyyyMM formatting/date argument/counter continues across month
- 必需：Pending manual insert/update/delete rejected and nonORD same-context Local batching unchanged
仍未覆盖：PUB/FIN/MES/WMS distinct numbering rules and full financial workflows WP4

### 4. D-shared-context-and-generation-cursor

Adopt existing provider/profile/non-owning options at6 factories and frozen generation-v2 reader。Same actual connection/transaction/tenant, correct target migration profile. Reader boundary comes from durable tenant generation in a consistent short-page transaction, string Boundary/opaque NextCursor/LastAggregate API retained; v2 purpose/format rejects old rowversion cursors. Do not add duplicate token/generation migrations.
- 必需：Actual4 context models commit and rollback with appropriate business+priority/outbox+audit tables, explicit Dapper transaction
- 必需：Inner disposal preserves outer open connection and continued commands; mismatched provider/connection rejects before command
- 必需：Empty tenant generation0, updates/tombstones/physicalDELETE, across-page change rejects, rollback keeps committed boundary
- 必需：Tenant mismatch rejects, stable aggregate sort, boundary is not opaque token order, real process restart with same protection keys
- 必需：Oldv1 cursor rejected even when numeric boundary happens to match
仍未覆盖：Full OIDC SQL store/identity revocation/ERP and Space chains still WP4/WP5

### 5. E-atomic-claim-return-and-runtime-regression

Deliver independent claim/skip-locked and mutation-return capabilities with real contention evidence。Capability and caller matrix distinguishes PostgreSQL equivalents from future WP4/WP5 adapters. Keep claim ordering, lease owner/token/expiry and affected-row semantics explicit; no general SQL fragment translator. Runtime guard remains until remaining WP4/WP5/WP6 acceptance succeeds.
- 必需：Concurrent dual claim yields at most one owner per eligible row and skiplocked selection makes progress with held competitor row
- 必需：Atomic mutation-return returns exactly modified/deleted rows; no stale select-after-update result
- 必需：Expired lease takeover then old owner cannot renew/release/complete; actual affected-row fence
- 必需：Two simultaneous capacity/budget operations cannot exceed representative bounded limits; full Space budget model remains WP5
- 必需：Byte8 EF/raw/ExecuteUpdate token change, stale-write rejection, caller rollback, Base64/replay exact bytes on bothDBs
仍未覆盖：Full business stock/accounting/messages/Space/report contracts, full migration-backed runner/restore/API readiness WP4-WP6. Unintegrated mandatory plan items must stay pending.

这些是同一个WP3任务内相关切片，连续实现后集中审查；不是每个切片重新完整review/交付。原计划中的真实容量/租约/fence等门禁如果消费端仍延期，应保持对应项Pending，不能以通用primitive通过冒充完整业务通过。

## census与未完成项

- ProviderSelectionAndBranches：84行 / 45文件；regex=\b(IsSqlServer|IsNpgsql|ProviderName|UseSqlServer|UseNpgsql)\b|new SqlConnection\b|DatabaseProvider\.(SqlServer|PostgreSql)
- ResourceLockStatements：16行 / 14文件；regex=sp_getapplock
- SessionLockReleases：1行 / 1文件；regex=sp_releaseapplock
- SharedSqlOnlyContextFactories：6行 / 6文件；regex=UseSqlServer
- AuthoritativeSqlUtcQueries：6行 / 6文件；regex=SELECT SYSUTCDATETIME\(\) AS \[Value\]
- DocNumberCallers：14行 / 9文件；regex=DocNumber.NextAsync
- SqlOnlyErrorClassifier：22行 / 8文件；regex=\b(SqlException|SqlError)\b|\b(2601|2627|1205|1222)\b
- ExceptionAndRetryDisposition：169行 / 80文件；regex=\b(DbUpdateException|DbUpdateConcurrencyException|SqlException|SqlError|PostgresException)\b|CreateExecutionStrategy|\b(2601|2627|1205|1222)\b
- RowRangeClaimReturnSql：38行 / 17文件；regex=UPDLOCK|HOLDLOCK|READPAST|READCOMMITTEDLOCK|SELECT TOP|UPDATE TOP|OUTPUT inserted|OUTPUT deleted|FOR UPDATE|SKIP LOCKED
- WP2 delivery/remote main closure must be verified by root before WP3 branch creation; no main development in this subtask
- WP3 capabilities/caller integrations/tests not implemented or executed by this inventory
- WP4 authentication/identity/messaging/ERP/WMS/purchase/workflow and financial flows remain pending
- WP5 full Space capacity/lease/apply/clone/publish/CAD/reports remain pending
- WP6 full runner, migration-backed zero-skip real business/API tests, native backup/restore, runtime readiness and normal deployment selection remain pending
- Existing SQL data transfer into PostgreSQL, mixed providers per deployment, independent external CRM repository, existing environment switch and production deployment remain outside accepted main task

当前仅证明源码盘点已刷新，没有证明WP3或整体双库兼容已完成。
