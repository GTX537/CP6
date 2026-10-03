# DB-COMPAT-01 WP2 Space mapping manifest

Date: 2026-10-03. **WP2 native model/migration/initialization gates LocalVerified; all eight owned databases cleaned with absence verified; remote delivery Pending.** Original SQL oracle remains `d6074aaad3098b61adaeda902b92bf24b3eb0c04`; current task HEAD is main `94c0f8c9cac63a72008c4e6b242c94358f25b305` plus uncommitted WP2 inputs. Historical fresh PG33/33 and later existing PG36/36 keep their actual d507/2d inputs. Final SQL populated136→1402/2, full catalog/write/negative35/35, unknown-history2/2 and new API first/repeat initialization with352-table seed retention are recorded separately; actual PG first/repeat retains334 tables. BUG137/139/141/143 are Closed. This manifest retains every original Space expression and writer proof; full Space business/API/worker compatibility remains in WP3–WP6. Exact scopes, original failures and reuse sources are in [implementation](WP2-IMPLEMENTATION.md).

Prerequisites: [WP1 decisions](WP1-DECISIONS.md) and the [cross-context mapping checklist](WP2-MAPPING-CHECKLIST.md). SQL Server model methods and all historical migrations/snapshots remain unchanged. Only PostgreSQL calls the shared `PostgreSqlModelConfiguration.Apply(modelBuilder, explicitUtcProperties)` after shared model configuration.

Implementation: [Space PostgreSQL classifications](../../../CP6.Space.Infrastructure/SpacePostgreSqlModelConfiguration.cs) expands the inherited audit writer contract into concrete entity/property tuples and lists every other UTC DateTime explicitly. A new unclassified Space DateTime fails model construction until its writer semantics are classified. Static comparison against the unchanged SQL snapshot confirms 156 inherited plus 81 explicit properties, with zero missing or additional DateTime mappings. Independent clean model RED: 46 total, 24 passed, 22 expected behavior failures, zero skips and zero warnings; SQL Space's full existing snapshot entity contracts passed. Evidence retained by the model-test owner at `tmp/db-compat-wp2-tests/model-red-verified/wp2-model-red-verified.trx`.

Historical fresh installation: root's `wp2-migration-pg-fresh-restored-order-full-write.json` ran **33/33** at **17:34:57–17:35:12 UTC** in the owned fresh PG database ending `31d68591`, with actual `search_path=public`. Space contributes **83 model tables and41 token installations** to four-context **328/208** model entries, distinct from the old327-entry oracle or352 SQL actual tables. Its full catalog facets and representative EF/native/ExecuteUpdate token fixture passed, including stale rejection and rollback; transaction-only missing-index/wrong-filter controls were rejected and restored. Catalog agreement does not prove every check expression or every Space writer. Later PG36/36 and final SQL35/35 independently repeat their recorded full catalog/scoped writes. [Core boundary](WP2-CORE-MAPPING.md) records original inputs/raw REDs. [Text evidence](WP2-TEXT-PROBE.md) remains reused PG **46/46** / SQL **22/22** with unchanged four consumer hashes.

## Complete coverage and semantic rules

Baseline SpaceContext file SHA-256: `A61958CA2FF5E3372AABE528280AC76586B62F26E78A2D49B7599886BC37C1DC`. Static inventory: **83 entities, 299 explicit store-type calls, 69 index filters, 120 checks, 237 DateTime properties, 9 DateTimeOffset properties, 1 DateOnly property**. Each original expression appears below for independent semantic comparison; no check or FK is removed.

- Numeric integer/enum predicates remain numeric. Only actual boolean store columns translate 0/1 to false/true. BindingMode, State, Status, Purpose and similar enums must not become booleans.
- JSON/evidence `nvarchar(max)` uses unbounded `bpchar` through the consumer mapping, preserving raw content/tails and hashes; no jsonb normalization. Managed checks read full raw text. ISJSON without a type constraint accepts object/array, not JSON scalars; preserve NULL/UNKNOWN behavior.
- LEN removes only trailing U+0020 spaces before measuring. The actual source default is Chinese_PRC_CI_AS (non-SC); these range columns have no explicit binary collation. Selected native comparisons now establish uppercase/lowercase and fullwidth/accented bracket-range membership, NULL and empty behavior through the versioned range function. Space's exact **77 fixed-text tuples** (72 char(64), five char(3)) have a separate supported ASCII-domain check, within the shared **83-tuple** manifest; variable text is not classified by suffix. Thus the function's linguistic samples do not promise non-ASCII fixed-field input compatibility. Provenance, legitimate empty hashes and the policy currency public-writer contract are retained in [fixed-domain evidence](WP2-CORE-MAPPING.md#fixed-text-domain-provenance). YEAR/MONTH extract DateOnly calendar values without timezone conversion.
- Unicode length is a separate required boundary: SQL Server LEN counts supplementary-character pairs differently under SC and non-SC collations, while nvarchar(n) capacity is always n UTF-16 byte pairs. PostgreSQL varchar(n) capacity is n characters. The general-Unicode Code and Rationale checks and bounded text therefore need the actual SQL collation and supplementary-character boundary evidence, rather than assuming that length(rtrim(...)) and equal numeric limits prove equivalence. Sources: [SQL LEN](https://learn.microsoft.com/en-us/sql/t-sql/functions/len-transact-sql?view=sql-server-ver17), [SQL nvarchar](https://learn.microsoft.com/en-us/sql/t-sql/data-types/nchar-and-nvarchar-transact-sql?view=sql-server-ver17), [PostgreSQL character types](https://www.postgresql.org/docs/18/datatype-character.html).
- Non-Unicode bounded columns have a distinct capacity boundary. SQL char/varchar(n) limits bytes under the source collation's code page; PostgreSQL character/varchar(n) limits characters. Space has varchar(30/32/50/64/100/200/256/500), including external identifiers and codes without a general ASCII-only domain guard. An nvarchar UTF-16 capacity check does not prove these varchar mappings equivalent. Source code-page, non-ASCII and trailing-space write/read boundaries remain explicit inputs to real acceptance. Sources: [SQL char/varchar](https://learn.microsoft.com/en-us/sql/t-sql/data-types/char-and-varchar-transact-sql?view=sql-server-ver17), [PostgreSQL character types](https://www.postgresql.org/docs/18/datatype-character.html).
- Ordinary string UNIQUE and lookup behavior also needs a frozen contract. Space's FloorCode, ZoneCode, LocationCode, external event/device identifiers, Namespace/Key and BusinessKey mappings inherit the source default collation. A PostgreSQL C collation alone does not preserve case-insensitive or trailing-space equality. The source expression inventory below remains valid, while true database comparisons must resolve these boundaries before semantic equivalence is claimed.
- SQL native rowversion remains unchanged. PostgreSQL uses database BEFORE INSERT OR UPDATE generated 8-byte bytea, concurrency=true, ValueGenerated.OnAddOrUpdate and before/after save=Ignore, never xmin. The separate PG migration owns sequence/function/trigger and byte length checks.
- Preserve PK/FK/AK/index/check meaning, key order, tenant keys and DeleteBehavior. PG names must be at most 63 UTF-8 bytes with deterministic shortening and original-name traceability. Do not rely on server truncation. The sole current Space name consumer is SpaceWmsAdoptionService matching the UX_Space_WmsAdoption prefix; the relevant original names already fit. Provider error classification and business SQL remain later work packages.

## Explicit property-to-writer time classification

Classifications follow actual domain guards and writer paths, not suffixes or SQL datetime2. All current **237 Space DateTime** mappings use the UTC contract: timestamptz with ordinary Local/Unspecified rejection; nullable values may be NULL. The shared converter explicitly recognizes **MinValue/MaxValue ticks as sentinel exceptions** and sets their Kind to Utc; this does not admit general non-UTC instants. Nine DateTimeOffset properties require Offset=0 and the single PeriodDay DateOnly remains date. Space has no business-local DateTime classification; Core/legacy columns differ. Installed native time gates passed selected UTC/wall-clock/offset/date round trips, Min/Max sentinels and invalid-kind/offset rejection. PG preserves microsecond precision here and does not promise SQL's extra 100ns precision. This is a property mapping contract plus selected boundary evidence, not writes to all 247 temporal properties.

**156 inherited audit fields on 78 mapped SpaceTenantEntity types:** CreatedAtUtc and ModifiedAtUtc are stamped by [StampAndValidateTenant](../../../CP6.Space.Infrastructure/SpaceContext.cs#L6809) before both SaveChanges methods, using ISpaceClock.UtcNow after Kind==Utc validation. Expand this inherited contract into concrete entity/property tuples, rather than matching a Utc suffix.

| Concrete mapped entity | Audited UTC properties |
| --- | --- |
| `SpaceAiBudgetReservation` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceAiTenantPolicyConfiguration` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceAiUsageRecord` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceAisleRevision` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceArtifact` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceCadMappingProfile` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceCadMappingProfileVersion` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceCadParsePreparation` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceCadSiteProviderCertification` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceCadSiteProviderConfiguration` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceDesignAttribute` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceDeviceAlarmState` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceDeviceCurrentState` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceDeviceEvent` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceDeviceMapping` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceDispatchRecommendation` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceEditLease` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceEditLeaseTakeoverAudit` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceElementAttribute` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceElementCommandBatch` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceElementCommandRecord` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceElementRevision` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceExcelMappingProfile` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceExcelMappingProfileVersion` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceExternalGrant` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceExternalGrantFloor` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceExternalGrantObject` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceExternalGrantOwner` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceExternalGrantZone` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceExternalMembership` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceExternalOrganization` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceFieldPolicy` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceFieldPolicyField` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceFile` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceFloorRevision` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceGenerationLockedFact` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceGenerationProposal` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceGenerationRun` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceGenerationStagingElement` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceHistoricalRepublish` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceIdempotencyRecord` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceJob` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceJobAttempt` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceJobStep` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceLocationExternalBinding` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceLocationRevision` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceModel` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceModelIssue` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceModelSource` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceModelVersion` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePersonnelCurrentState` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePersonnelEvent` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePlanningComparison` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePlanningComparisonEntry` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePlanningComparisonRisk` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePlanningDecisionRecord` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePlanningHistoricalDataset` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePlanningHistoricalTask` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePlanningScenarioBranch` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePlanningSimulationLocationResult` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePlanningSimulationRun` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceProposalDecision` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePublishAttempt` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePublishAuditEvent` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePublishBatch` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePublishPlan` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpacePutawayRecommendation` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceRackLevelRevision` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceRackRevision` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceReconciliationIssue` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceRuntimeElement` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceUnderlayCalibration` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceValidationRun` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceWarehouseTemplate` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceWarehouseTemplateVersion` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceWmsAdoption` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceWmsReceipt` | `CreatedAtUtc`, `ModifiedAtUtc` |
| `SpaceZoneRevision` | `CreatedAtUtc`, `ModifiedAtUtc` |

**81 explicitly classified DateTime fields, 9 DateTimeOffset fields and 1 DateOnly field:** the following links identify each assignment. The domain source uses RequireUtc/Kind validation, or copies an event whose constructor already validated UTC. Planning DateTimeOffset writers require Offset==0. PeriodDay is a business calendar date and remains date.

| Entity | Explicit properties and classification | Assignment/domain writer evidence |
| --- | --- | --- |
| `SpaceAiBudgetReservation` | `PeriodDay` (date), `ExpiresAtUtc` (UTC Kind) | [PeriodDay writer](../../../CP6.Space.Domain/SpaceAiCapacity.cs#L198); [ExpiresAtUtc writer](../../../CP6.Space.Domain/SpaceAiCapacity.cs#L203) |
| `SpaceAiTenantPolicyConfiguration` | `UpdatedAtUtc` (UTC Kind) | [UpdatedAtUtc writer](../../../CP6.Space.Domain/SpaceAiTenantPolicyConfiguration.cs#L63) |
| `SpaceAiUsageRecord` | `RecordedAtUtc` (UTC Kind), `ArchivedAtUtc` (UTC Kind) | [RecordedAtUtc writer](../../../CP6.Space.Domain/SpaceAiUsageRecord.cs#L92); [ArchivedAtUtc writer](../../../CP6.Space.Domain/SpaceAiUsageRecord.cs#L110) |
| `SpaceAsset` | `CreatedAtUtc` (UTC Kind) | [CreatedAtUtc writer](../../../CP6.Space.Domain/SpaceAssets.cs#L117) |
| `SpaceAssetVersion` | `CreatedAtUtc` (UTC Kind) | [CreatedAtUtc writer](../../../CP6.Space.Domain/SpaceAssets.cs#L191) |
| `SpaceCadParsePreparation` | `ExpiresAtUtc` (UTC Kind) | [ExpiresAtUtc writer](../../../CP6.Space.Domain/SpaceCadParsePreparation.cs#L105) |
| `SpaceCadSiteProviderCertification` | `ExpiresAtUtc` (UTC Kind), `ValidFromUtc` (UTC Kind) | [ExpiresAtUtc writer](../../../CP6.Space.Domain/SpaceCadSiteProviderConfiguration.cs#L233); [ValidFromUtc writer](../../../CP6.Space.Domain/SpaceCadSiteProviderConfiguration.cs#L232) |
| `SpaceCadSiteProviderConfiguration` | `ApprovedAtUtc` (UTC Kind) | [ApprovedAtUtc writer](../../../CP6.Space.Domain/SpaceCadSiteProviderConfiguration.cs#L108) |
| `SpaceDeviceAlarmState` | `OccurredAtUtc` (UTC Kind), `ReceivedAtUtc` (UTC Kind) | [OccurredAtUtc writer](../../../CP6.Space.Domain/SpaceDeviceEvents.cs#L605); [ReceivedAtUtc writer](../../../CP6.Space.Domain/SpaceDeviceEvents.cs#L606) |
| `SpaceDeviceCurrentState` | `OperatingStateOccurredAtUtc` (UTC Kind), `OperatingStateReceivedAtUtc` (UTC Kind), `PositionOccurredAtUtc` (UTC Kind), `PositionReceivedAtUtc` (UTC Kind) | [OperatingStateOccurredAtUtc writer](../../../CP6.Space.Domain/SpaceDeviceEvents.cs#L498); [OperatingStateReceivedAtUtc writer](../../../CP6.Space.Domain/SpaceDeviceEvents.cs#L499); [PositionOccurredAtUtc writer](../../../CP6.Space.Domain/SpaceDeviceEvents.cs#L478); [PositionReceivedAtUtc writer](../../../CP6.Space.Domain/SpaceDeviceEvents.cs#L479) |
| `SpaceDeviceEvent` | `OccurredAtUtc` (UTC Kind), `ReceivedAtUtc` (UTC Kind) | [OccurredAtUtc writer](../../../CP6.Space.Domain/SpaceDeviceEvents.cs#L276); [ReceivedAtUtc writer](../../../CP6.Space.Domain/SpaceDeviceEvents.cs#L277) |
| `SpaceDispatchRecommendation` | `GeneratedAtUtc` (UTC Kind) | [GeneratedAtUtc writer](../../../CP6.Space.Domain/SpaceDispatchRecommendation.cs#L126) |
| `SpaceEditLease` | `AcquiredAtUtc` (UTC Kind), `ExpiresAtUtc` (UTC Kind), `LastRenewedAtUtc` (UTC Kind) | [AcquiredAtUtc writer](../../../CP6.Space.Domain/SpaceEditLease.cs#L120); [ExpiresAtUtc writer](../../../CP6.Space.Domain/SpaceEditLease.cs#L89); [LastRenewedAtUtc writer](../../../CP6.Space.Domain/SpaceEditLease.cs#L88) |
| `SpaceEditLeaseTakeoverAudit` | `TakenOverAtUtc` (UTC Kind) | [TakenOverAtUtc writer](../../../CP6.Space.Domain/SpaceEditLease.cs#L215) |
| `SpaceElementCommandBatch` | `AppliedAtUtc` (UTC Kind) | [AppliedAtUtc writer](../../../CP6.Space.Domain/SpaceElementCommands.cs#L127) |
| `SpaceElementRevision` | `ManualCorrectionUpdatedAtUtc` (UTC Kind) | [ManualCorrectionUpdatedAtUtc writer](../../../CP6.Space.Domain/SpaceRevisions.cs#L1220) |
| `SpaceExternalGrant` | `ValidFromUtc` (UTC Kind), `ValidToUtc` (UTC Kind) | [ValidFromUtc writer](../../../CP6.Space.Domain/SpaceExternalGrant.cs#L55); [ValidToUtc writer](../../../CP6.Space.Domain/SpaceExternalGrant.cs#L56) |
| `SpaceExternalMembership` | `ValidFromUtc` (UTC Kind), `AcceptedAtUtc` (UTC Kind), `ValidToUtc` (UTC Kind) | [ValidFromUtc writer](../../../CP6.Space.Domain/SpaceExternalAccess.cs#L215); [AcceptedAtUtc writer](../../../CP6.Space.Domain/SpaceExternalAccess.cs#L219); [ValidToUtc writer](../../../CP6.Space.Domain/SpaceExternalAccess.cs#L216) |
| `SpaceFile` | `ContentDeletedAtUtc` (UTC Kind), `DeletionRequestedAtUtc` (UTC Kind), `RetainUntilUtc` (UTC Kind) | [ContentDeletedAtUtc writer](../../../CP6.Space.Domain/SpaceFile.cs#L155); [DeletionRequestedAtUtc writer](../../../CP6.Space.Domain/SpaceFile.cs#L134); [RetainUntilUtc writer](../../../CP6.Space.Domain/SpaceFile.cs#L42) |
| `SpaceGenerationProposal` | `PayloadPurgedAtUtc` (UTC Kind) | [PayloadPurgedAtUtc writer](../../../CP6.Space.Domain/SpaceGenerationProposal.cs#L192) |
| `SpaceGenerationRun` | `ApplyPreparedAtUtc` (UTC Kind), `CancelRequestedAtUtc` (UTC Kind), `CancelledAtUtc` (UTC Kind), `PayloadPurgedAtUtc` (UTC Kind), `RetentionHoldUntilUtc` (UTC Kind), `ReviewCompletedAtUtc` (UTC Kind) | [ApplyPreparedAtUtc writer](../../../CP6.Space.Domain/SpaceGenerationRun.cs#L259); [CancelRequestedAtUtc writer](../../../CP6.Space.Domain/SpaceGenerationRun.cs#L300); [CancelledAtUtc writer](../../../CP6.Space.Domain/SpaceGenerationRun.cs#L302); [PayloadPurgedAtUtc writer](../../../CP6.Space.Domain/SpaceGenerationRun.cs#L552); [RetentionHoldUntilUtc writer](../../../CP6.Space.Domain/SpaceGenerationRun.cs#L525); [ReviewCompletedAtUtc writer](../../../CP6.Space.Domain/SpaceGenerationRun.cs#L217) |
| `SpaceHistoricalRepublish` | `RequestedAtUtc` (UTC Kind) | [RequestedAtUtc writer](../../../CP6.Space.Domain/SpaceHistoricalRepublish.cs#L68) |
| `SpaceIdempotencyRecord` | `ReplayUntilUtc` (UTC Kind), `RetainUntilUtc` (UTC Kind) | [ReplayUntilUtc writer](../../../CP6.Space.Domain/SpaceIdempotencyRecord.cs#L48); [RetainUntilUtc writer](../../../CP6.Space.Domain/SpaceIdempotencyRecord.cs#L49) |
| `SpaceJob` | `NextAttemptAtUtc` (UTC Kind), `RequestedAtUtc` (UTC Kind), `CancellationRequestedAtUtc` (UTC Kind), `FinishedAtUtc` (UTC Kind), `LockExpiresAtUtc` (UTC Kind), `LockedAtUtc` (UTC Kind), `StartedAtUtc` (UTC Kind) | [NextAttemptAtUtc writer](../../../CP6.Space.Domain/SpaceJob.cs#L87); [RequestedAtUtc writer](../../../CP6.Space.Domain/SpaceJob.cs#L89); [CancellationRequestedAtUtc writer](../../../CP6.Space.Domain/SpaceJob.cs#L247); [FinishedAtUtc writer](../../../CP6.Space.Domain/SpaceJob.cs#L201); [LockExpiresAtUtc writer](../../../CP6.Space.Domain/SpaceJob.cs#L132); [LockedAtUtc writer](../../../CP6.Space.Domain/SpaceJob.cs#L131); [StartedAtUtc writer](../../../CP6.Space.Domain/SpaceJob.cs#L134) |
| `SpaceJobAttempt` | `StartedAtUtc` (UTC Kind), `FinishedAtUtc` (UTC Kind) | [StartedAtUtc writer](../../../CP6.Space.Domain/SpaceJobAttempt.cs#L45); [FinishedAtUtc writer](../../../CP6.Space.Domain/SpaceJobAttempt.cs#L64) |
| `SpaceJobStep` | `StartedAtUtc` (UTC Kind), `FinishedAtUtc` (UTC Kind) | [StartedAtUtc writer](../../../CP6.Space.Domain/SpaceJobStep.cs#L39); [FinishedAtUtc writer](../../../CP6.Space.Domain/SpaceJobStep.cs#L58) |
| `SpaceModelIssue` | `AcknowledgedAtUtc` (UTC Kind), `PayloadPurgedAtUtc` (UTC Kind) | [AcknowledgedAtUtc writer](../../../CP6.Space.Domain/SpaceModelIssue.cs#L113); [PayloadPurgedAtUtc writer](../../../CP6.Space.Domain/SpaceModelIssue.cs#L169) |
| `SpaceModelVersion` | `PublishedAtUtc` (UTC Kind) | [PublishedAtUtc writer](../../../CP6.Space.Domain/SpaceModelVersion.cs#L247) |
| `SpacePersonnelCurrentState` | `PositionOccurredAtUtc` (UTC Kind), `PositionReceivedAtUtc` (UTC Kind), `WorkStateOccurredAtUtc` (UTC Kind), `WorkStateReceivedAtUtc` (UTC Kind) | [PositionOccurredAtUtc writer](../../../CP6.Space.Domain/SpacePersonnelEvents.cs#L271); [PositionReceivedAtUtc writer](../../../CP6.Space.Domain/SpacePersonnelEvents.cs#L272); [WorkStateOccurredAtUtc writer](../../../CP6.Space.Domain/SpacePersonnelEvents.cs#L291); [WorkStateReceivedAtUtc writer](../../../CP6.Space.Domain/SpacePersonnelEvents.cs#L292) |
| `SpacePersonnelEvent` | `OccurredAtUtc` (UTC Kind), `ReceivedAtUtc` (UTC Kind) | [OccurredAtUtc writer](../../../CP6.Space.Domain/SpacePersonnelEvents.cs#L139); [ReceivedAtUtc writer](../../../CP6.Space.Domain/SpacePersonnelEvents.cs#L140) |
| `SpacePlanningComparison` | `HistoricalFromUtc` (UTC Offset=0), `HistoricalToUtc` (UTC Offset=0) | [HistoricalFromUtc writer](../../../CP6.Space.Domain/SpacePlanningComparison.cs#L108); [HistoricalToUtc writer](../../../CP6.Space.Domain/SpacePlanningComparison.cs#L109) |
| `SpacePlanningHistoricalDataset` | `HistoricalFromUtc` (UTC Offset=0), `HistoricalToUtc` (UTC Offset=0), `ReplayStartUtc` (UTC Offset=0) | [HistoricalFromUtc writer](../../../CP6.Space.Domain/SpacePlanningHistoricalDataset.cs#L82); [HistoricalToUtc writer](../../../CP6.Space.Domain/SpacePlanningHistoricalDataset.cs#L83); [ReplayStartUtc writer](../../../CP6.Space.Domain/SpacePlanningHistoricalDataset.cs#L84) |
| `SpacePlanningHistoricalTask` | `OriginalCompletedAtUtc` (UTC Offset=0), `OriginalCreatedAtUtc` (UTC Offset=0), `ReplayCompletedAtUtc` (UTC Offset=0), `ReplayCreatedAtUtc` (UTC Offset=0) | [OriginalCompletedAtUtc writer](../../../CP6.Space.Domain/SpacePlanningHistoricalDataset.cs#L246); [OriginalCreatedAtUtc writer](../../../CP6.Space.Domain/SpacePlanningHistoricalDataset.cs#L245); [ReplayCompletedAtUtc writer](../../../CP6.Space.Domain/SpacePlanningHistoricalDataset.cs#L248); [ReplayCreatedAtUtc writer](../../../CP6.Space.Domain/SpacePlanningHistoricalDataset.cs#L247) |
| `SpacePublishAttempt` | `QueuedAtUtc` (UTC Kind), `StartedAtUtc` (UTC Kind), `FinishedAtUtc` (UTC Kind), `LastRetriedAtUtc` (UTC Kind), `RuntimeActivatedAtUtc` (UTC Kind), `WmsCommittedAtUtc` (UTC Kind) | [QueuedAtUtc writer](../../../CP6.Space.Domain/SpacePublishing.cs#L170); [StartedAtUtc writer](../../../CP6.Space.Domain/SpacePublishing.cs#L164); [FinishedAtUtc writer](../../../CP6.Space.Domain/SpacePublishing.cs#L253); [LastRetriedAtUtc writer](../../../CP6.Space.Domain/SpacePublishing.cs#L345); [RuntimeActivatedAtUtc writer](../../../CP6.Space.Domain/SpacePublishing.cs#L252); [WmsCommittedAtUtc writer](../../../CP6.Space.Domain/SpacePublishing.cs#L230) |
| `SpacePublishAuditEvent` | `OccurredAtUtc` (UTC Kind) | [OccurredAtUtc writer](../../../CP6.Space.Domain/SpacePublishing.cs#L725) |
| `SpacePublishBatch` | `ObservedAtUtc` (UTC Kind) | [ObservedAtUtc writer](../../../CP6.Space.Domain/SpacePublishing.cs#L471) |
| `SpacePutawayRecommendation` | `GeneratedAtUtc` (UTC Kind) | [GeneratedAtUtc writer](../../../CP6.Space.Domain/SpacePutawayRecommendation.cs#L136) |
| `SpaceRackGenerationProfile` | `CreatedAtUtc` (UTC Kind) | [CreatedAtUtc writer](../../../CP6.Space.Domain/SpaceRackGenerationProfiles.cs#L111) |
| `SpaceRackGenerationProfileVersion` | `CreatedAtUtc` (UTC Kind) | [CreatedAtUtc writer](../../../CP6.Space.Domain/SpaceRackGenerationProfiles.cs#L201) |
| `SpaceTenantAiWorkSlot` | `LeaseExpiresAtUtc` (UTC Kind) | [LeaseExpiresAtUtc writer](../../../CP6.Space.Domain/SpaceAiCapacity.cs#L56) |
| `SpaceValidationRun` | `RequestedAtUtc` (UTC Kind), `FinishedAtUtc` (UTC Kind), `StartedAtUtc` (UTC Kind) | [RequestedAtUtc writer](../../../CP6.Space.Domain/SpaceValidationRun.cs#L82); [FinishedAtUtc writer](../../../CP6.Space.Domain/SpaceValidationRun.cs#L152); [StartedAtUtc writer](../../../CP6.Space.Domain/SpaceValidationRun.cs#L99) |
| `SpaceWmsAdoption` | `LastObservedAtUtc` (UTC Kind), `BoundAtUtc` (UTC Kind) | [LastObservedAtUtc writer](../../../CP6.Space.Domain/SpaceWmsAdoption.cs#L68); [BoundAtUtc writer](../../../CP6.Space.Domain/SpaceWmsAdoption.cs#L146) |
| `SpaceWmsReceipt` | `ReceivedAtUtc` (UTC Kind) | [ReceivedAtUtc writer](../../../CP6.Space.Domain/SpacePublishing.cs#L549) |

## All 299 explicit store-type source calls

These are source call counts, not expanded runtime property counts. The inherited audit helper expands its two calls across multiple entities. Preserve precision/scale, Unicode/max length, fixed length, nullable state and enum conversions alongside type names.

| Original store type | Calls | PG target/contract | Every baseline source line |
| --- | ---: | --- | --- |
| `bigint` | 4 | bigint, same fixed-length/enum/date contract | 4517, 4586, 4650, 4854 |
| `char(3)` | 5 | char(3), same fixed-length/enum/date contract | 4198, 4337, 4427, 5597, 5832 |
| `char(64)` | 42 | char(64), same fixed-length/enum/date contract | 451, 534, 539, 544, 549, 1518, 1650, 1921, 2217, 2318, 2424, 2624, 2630, 2635, 2997, 3056, 3188, 3194, 3262, 3315, 3362, 3370, 3610, 3616, 3689, 3696, 4105, 4450, 4462, 4968, 5042, 5136, 5239, 5286, 5383, 5389, 5470, 5476, 5610, 5825, 5936, 6098 |
| `date` | 1 | date, same fixed-length/enum/date contract | 4335 |
| `datetime2` | 70 | timestamptz, only explicit confirmed UTC properties | 1204, 1205, 1206, 1207, 1212, 1213, 1258, 1288, 1344, 1397, 1522, 1527, 1647, 1648, 1710, 1711, 1716, 1717, 1918, 1919, 2008, 2009, 2015, 2017, 2091, 2092, 2155, 2225, 2368, 2432, 2502, 2640, 2716, 2718, 2719, 2763, 2811, 2852, 2899, 2900, 3010, 3011, 3012, 3378, 3379, 3380, 3487, 3624, 3625, 3683, 3685, 3687, 3701, 3705, 3707, 3867, 4206, 4208, 4278, 4345, 4431, 4577, 4579, 4584, 4648, 4649, 5108, 5211, 6177, 6178 |
| `datetimeoffset(7)` | 9 | timestamptz, Offset=0 | 5376, 5378, 5380, 5487, 5489, 5491, 5493, 5838, 5840 |
| `decimal(18,3)` | 16 | numeric(18,3), same precision/scale | 1642, 1643, 1644, 1646, 1705, 1706, 1707, 1709, 1907, 1908, 1909, 1911, 2003, 2004, 2005, 2007 |
| `decimal(18,4)` | 4 | numeric(18,4), same precision/scale | 670, 672, 880, 923 |
| `decimal(18,6)` | 6 | numeric(18,6), same precision/scale | 660, 661, 662, 663, 664, 665 |
| `decimal(18,8)` | 3 | numeric(18,8), same precision/scale | 599, 667, 3063 |
| `decimal(6,5)` | 2 | numeric(6,5), same precision/scale | 3859, 4020 |
| `decimal(9,4)` | 4 | numeric(9,4), same precision/scale | 600, 668, 807, 2498 |
| `nvarchar(max)` | 48 | text, original JSON/evidence bytes retained | 597, 721, 761, 762, 1151, 1211, 1256, 1257, 1348, 1491, 2212, 2315, 2421, 2494, 2639, 2686, 2687, 2688, 2794, 2802, 3064, 3204, 3205, 3268, 3313, 3471, 3476, 3622, 3703, 3855, 3857, 3940, 3942, 4473, 4965, 5039, 5118, 5121, 5124, 5127, 5130, 5133, 5221, 5224, 5227, 5230, 5233, 5236 |
| `smallint` | 85 | smallint, same fixed-length/enum/date contract | 446, 449, 524, 527, 531, 720, 763, 926, 929, 1005, 1200, 1201, 1254, 1282, 1283, 1311, 1312, 1341, 1342, 1343, 1390, 1525, 1633, 1638, 1641, 1703, 1715, 1767, 1773, 1893, 1900, 1903, 1906, 1916, 1999, 2013, 2079, 2089, 2147, 2154, 2207, 2210, 2224, 2361, 2367, 2419, 2431, 2497, 2955, 3003, 3009, 3053, 3067, 3115, 3183, 3186, 3201, 3208, 3260, 3271, 3312, 3377, 3468, 3481, 3484, 3666, 3669, 3862, 3865, 3937, 4019, 4103, 4204, 4343, 4503, 4515, 4575, 4582, 4647, 4850, 4853, 4890, 4897, 5482, 5485 |

## Every original index filter (69)

| Ordinal/baseline source | Entity | Original SQL Server predicate |
| --- | --- | --- |
| [1 / L459](../../../CP6.Space.Infrastructure/SpaceContext.cs#L464) | `SpaceModel` | `[IsDeleted] = 0` |
| [2 / L463](../../../CP6.Space.Infrastructure/SpaceContext.cs#L468) | `SpaceModel` | `[ActiveDraftVersionId] IS NOT NULL AND [IsDeleted] = 0` |
| [3 / L467](../../../CP6.Space.Infrastructure/SpaceContext.cs#L472) | `SpaceModel` | `[CurrentPublishedVersionId] IS NOT NULL AND [IsDeleted] = 0` |
| [4 / L562](../../../CP6.Space.Infrastructure/SpaceContext.cs#L567) | `SpaceModelVersion` | `[BasedOnVersionId] IS NOT NULL` |
| [5 / L571](../../../CP6.Space.Infrastructure/SpaceContext.cs#L576) | `SpaceModelVersion` | `[CloneOperationId] IS NOT NULL` |
| [6 / L604](../../../CP6.Space.Infrastructure/SpaceContext.cs#L609) | `SpaceTenantEntity<T>` | `[IsDeleted] = 0` |
| [7 / L734](../../../CP6.Space.Infrastructure/SpaceContext.cs#L739) | `SpaceTenantEntity<T>` | `[IsDeleted] = 0` |
| [8 / L774](../../../CP6.Space.Infrastructure/SpaceContext.cs#L779) | `SpaceTenantEntity<T>` | `[IsDeleted] = 0` |
| [9 / L818](../../../CP6.Space.Infrastructure/SpaceContext.cs#L823) | `SpaceTenantEntity<T>` | `[IsDeleted] = 0` |
| [10 / L891](../../../CP6.Space.Infrastructure/SpaceContext.cs#L896) | `SpaceTenantEntity<T>` | `[IsDeleted] = 0` |
| [11 / L933](../../../CP6.Space.Infrastructure/SpaceContext.cs#L938) | `SpaceTenantEntity<T>` | `[LocationCode] IS NOT NULL AND [IsDeleted] = 0` |
| [12 / L945](../../../CP6.Space.Infrastructure/SpaceContext.cs#L950) | `SpaceTenantEntity<T>` | `[RackLogicalId] IS NOT NULL AND [IsDeleted] = 0` |
| [13 / L1017](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1022) | `SpaceLocationExternalBinding` | `[IsDeleted] = 0` |
| [14 / L1027](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1032) | `SpaceLocationExternalBinding` | `[BindingMode] = 0 AND [IsDeleted] = 0` |
| [15 / L1109](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1114) | `SpaceDesignAttribute` | `[IsDeleted] = 0` |
| [16 / L1220](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1225) | `SpacePublishAttempt` | `[OwnsPublishSlot] = 1 AND [IsDeleted] = 0` |
| [17 / L1413](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1418) | `SpaceHistoricalRepublish` | `[PublishAttemptId] IS NOT NULL` |
| [18 / L1538](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1543) | `SpaceWmsAdoption` | `[IsDeleted] = 0` |
| [19 / L1549](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1554) | `SpaceWmsAdoption` | `[ExternalLocationId] IS NOT NULL AND [IsDeleted] = 0` |
| [20 / L1561](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1566) | `SpaceWmsAdoption` | `[LocationLogicalId] IS NOT NULL AND [IsDeleted] = 0` |
| [21 / L1954](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1959) | `SpaceDeviceEvent` | `[AlarmExternalId] IS NOT NULL` |
| [22 / L2165](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2170) | `SpaceAsset` | `[IsDeleted] = 0` |
| [23 / L2283](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2288) | `SpaceWarehouseTemplate` | `[IsDeleted] = 0` |
| [24 / L2325](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2330) | `SpaceWarehouseTemplateVersion` | `[IsDeleted] = 0` |
| [25 / L2377](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2382) | `SpaceRackGenerationProfile` | `[IsDeleted] = 0` |
| [26 / L2587](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2592) | `SpaceElementAttribute` | `[IsDeleted] = 0` |
| [27 / L2867](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2872) | `SpaceCadSiteProviderConfiguration` | `[IsCurrent] = 1 AND [IsDeleted] = 0` |
| [28 / L3020](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3025) | `SpaceFile` | `[Sha256] IS NOT NULL AND [State] IN (1, 2, 3) AND [IsDeleted] = 0` |
| [29 / L3025](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3030) | `SpaceFile` | `[RetainUntilUtc] IS NOT NULL AND [IsDeleted] = 0` |
| [30 / L3034](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3039) | `SpaceFile` | `[State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [ContentDeletedAtUtc] IS NULL` |
| [31 / L3081](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3086) | `SpaceModelSource` | `[IsDeleted] = 0` |
| [32 / L3084](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3089) | `SpaceModelSource` | `[FileId] IS NOT NULL AND [IsDeleted] = 0` |
| [33 / L3121](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3126) | `SpaceArtifact` | `[JobId] IS NOT NULL AND [IsDeleted] = 0` |
| [34 / L3124](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3129) | `SpaceArtifact` | `[IsDeleted] = 0` |
| [35 / L3127](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3132) | `SpaceArtifact` | `[SourceId] IS NOT NULL AND [IsDeleted] = 0` |
| [36 / L3215](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3220) | `SpaceJob` | `[Status] IN (0, 1) AND [IsDeleted] = 0` |
| [37 / L3396](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3401) | `SpaceValidationRun` | `[Status] <> 4 AND [IsDeleted] = 0` |
| [38 / L3500](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3505) | `SpaceModelIssue` | `[JobId] IS NOT NULL AND [IsDeleted] = 0` |
| [39 / L3511](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3516) | `SpaceModelIssue` | `[ValidationRunId] IS NOT NULL AND [IsDeleted] = 0` |
| [40 / L3524](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3529) | `SpaceModelIssue` | `[GenerationRunId] IS NOT NULL AND [IsDeleted] = 0` |
| [41 / L3535](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3540) | `SpaceModelIssue` | `[GenerationRunId] IS NOT NULL AND [IsDeleted] = 0` |
| [42 / L3636](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3641) | `SpaceIdempotencyRecord` | `[IsDeleted] = 0` |
| [43 / L3712](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3717) | `SpaceGenerationRun` | `[IsCurrent] = 1 AND [IsDeleted] = 0` |
| [44 / L3747](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3752) | `SpaceGenerationRun` | `[IsDeleted] = 0` |
| [45 / L3879](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3884) | `SpaceGenerationProposal` | `[IsDeleted] = 0` |
| [46 / L3902](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3907) | `SpaceGenerationProposal` | `[IsDeleted] = 0` |
| [47 / L4033](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4038) | `SpaceGenerationLockedFact` | `[IsDeleted] = 0` |
| [48 / L4113](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4118) | `SpaceGenerationStagingElement` | `[IsDeleted] = 0` |
| [49 / L4118](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4123) | `SpaceGenerationStagingElement` | `[IsDeleted] = 0` |
| [50 / L4123](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4128) | `SpaceGenerationStagingElement` | `[IsDeleted] = 0` |
| [51 / L4218](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4223) | `SpaceAiUsageRecord` | `[IsDeleted] = 0` |
| [52 / L4238](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4243) | `SpaceAiUsageRecord` | `[IsDeleted] = 0` |
| [53 / L4283](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4288) | `SpaceTenantAiWorkSlot` | `[RunId] IS NOT NULL` |
| [54 / L4439](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4444) | `SpaceAiTenantPolicyConfiguration` | `[IsActive] = 1 AND [IsDeleted] = 0` |
| [55 / L4527](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4532) | `SpaceExternalOrganization` | `[IsDeleted] = 0` |
| [56 / L4538](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4543) | `SpaceExternalOrganization` | `[BusinessPartnerId] IS NOT NULL AND [IsDeleted] = 0` |
| [57 / L4596](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4601) | `SpaceExternalMembership` | `[Status] <> 3 AND [IsDeleted] = 0` |
| [58 / L4712](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4717) | `SpaceExternalGrantFloor` | `[IsDeleted] = 0` |
| [59 / L4740](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4745) | `SpaceExternalGrantZone` | `[IsDeleted] = 0` |
| [60 / L4773](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4778) | `SpaceExternalGrantOwner` | `[IsDeleted] = 0` |
| [61 / L4816](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4821) | `SpaceExternalGrantObject` | `[IsDeleted] = 0` |
| [62 / L4863](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4868) | `SpaceFieldPolicy` | `[IsDeleted] = 0` |
| [63 / L4912](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4917) | `SpaceFieldPolicyField` | `[IsDeleted] = 0` |
| [64 / L4938](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4943) | `SpaceCadMappingProfile` | `[IsDeleted] = 0` |
| [65 / L4975](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4980) | `SpaceCadMappingProfileVersion` | `[IsDeleted] = 0` |
| [66 / L5012](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5017) | `SpaceExcelMappingProfile` | `[IsDeleted] = 0` |
| [67 / L5049](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5054) | `SpaceExcelMappingProfileVersion` | `[IsDeleted] = 0` |
| [68 / L5971](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5976) | `SpacePlanningComparisonEntry` | `[IsBaseline] = 1` |
| [69 / L6118](../../../CP6.Space.Infrastructure/SpaceContext.cs#L6123) | `SpacePlanningDecisionRecord` | `[SupersedesDecisionId] IS NOT NULL` |

## Every original check (120)

Keep original constraint names and full SQL below. The PG translator must support the whole expression and fail model construction on unknown functions/patterns. Independent model comparisons must verify checks, filters, keys and relations per entity; real installation checks must exercise permitted and rejected boundaries.

| Ordinal/baseline source | Entity / original name | Original SQL Server check |
| --- | --- | --- |
| [1 / L493](../../../CP6.Space.Infrastructure/SpaceContext.cs#L498) | `SpaceModelVersion` / `CK_Space_ModelVersion_Purpose` | `[Purpose] IN (0, 1) AND ([Purpose] = 0 OR ([Status] NOT IN (3, 4, 5, 6) AND [PublishedAtUtc] IS NULL AND [PublishedBy] IS NULL))` |
| [2 / L499](../../../CP6.Space.Infrastructure/SpaceContext.cs#L504) | `SpaceModelVersion` / `CK_Space_ModelVersion_CreationSource` | `[CreationSource] IN (0, 1, 2, 3) AND (((([CreationSource] = 0 AND [BasedOnVersionId] IS NULL) OR ([CreationSource] = 1 AND [BasedOnVersionId] IS NOT NULL)) AND [SourceTemplateId] IS NULL AND [SourceTemplateVersionId] IS NULL AND [SourceTemplateContentHash] IS NULL) OR ([CreationSource] IN (2, 3) AND [BasedOnVersionId] IS NULL AND [SourceTemplateId] IS NOT NULL AND [SourceTemplateVersionId] IS NOT NULL AND [SourceTemplateContentHash] IS NOT NULL))` |
| [3 / L801](../../../CP6.Space.Infrastructure/SpaceContext.cs#L806) | `SpaceTenantEntity<T>` / `CK_Space_RackRevision_Geometry` | `[RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Depth] >= 0 AND [Height] >= 0` |
| [4 / L877](../../../CP6.Space.Infrastructure/SpaceContext.cs#L882) | `SpaceTenantEntity<T>` / `CK_Space_RackLevelRevision_Dimensions` | `[LevelNo] > 0 AND [BottomZ] >= 0 AND [ClearHeight] > 0 AND [BinCount] > 0 AND [DepthCount] > 0 AND [CellWidth] > 0 AND [CellDepth] > 0 AND [BeamHeight] >= 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)` |
| [5 / L918](../../../CP6.Space.Infrastructure/SpaceContext.cs#L923) | `SpaceTenantEntity<T>` / `CK_Space_LocationRevision_Dimensions` | `[ColumnNo] > 0 AND [LevelNo] > 0 AND [DepthNo] > 0 AND [Width] > 0 AND [Height] > 0 AND [Depth] > 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)` |
| [6 / L986](../../../CP6.Space.Infrastructure/SpaceContext.cs#L991) | `SpaceLocationExternalBinding` / `CK_Space_LocationExternalBinding_Mode` | `[BindingMode] IN (0, 1)` |
| [7 / L1080](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1085) | `SpaceDesignAttribute` / `CK_Space_DesignAttribute_ObjectType` | `[ObjectType] IN ('Rack', 'RackLevel', 'Location')` |
| [8 / L1185](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1190) | `SpacePublishAttempt` / `CK_Space_PublishAttempt_Slot` | `([OwnsPublishSlot] = 1 AND [FinishedAtUtc] IS NULL) OR ([OwnsPublishSlot] = 0)` |
| [9 / L1188](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1193) | `SpacePublishAttempt` / `CK_Space_PublishAttempt_Recovery` | `[ManualRetryCount] >= 0 AND ISJSON([RequestJson]) = 1 AND (([ManualRetryCount] = 0 AND [LastRetriedAtUtc] IS NULL AND [LastRetriedBy] IS NULL) OR ([ManualRetryCount] > 0 AND [LastRetriedAtUtc] IS NOT NULL AND [LastRetriedBy] IS NOT NULL))` |
| [10 / L1244](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1249) | `SpacePublishBatch` / `CK_Space_PublishBatch_Recovery` | `[AttemptCount] >= 0 AND [BatchAttemptNo] >= 0 AND ISJSON([RequestJson]) = 1` |
| [11 / L1331](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1336) | `SpacePublishAuditEvent` / `CK_Space_PublishAuditEvent_Invariants` | `[EventNo] > 0 AND ISJSON([EvidenceJson]) = 1 AND LEN([EvidenceHash]) = 64 AND [EvidenceHash] NOT LIKE '%[^0-9a-f]%' AND LEN([EventHash]) = 64 AND [EventHash] NOT LIKE '%[^0-9a-f]%' AND ([PreviousEventHash] IS NULL OR (LEN([PreviousEventHash]) = 64 AND [PreviousEventHash] NOT LIKE '%[^0-9a-f]%'))` |
| [12 / L1380](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1385) | `SpaceHistoricalRepublish` / `CK_Space_HistoricalRepublish_Status` | `[Status] IN (0, 1, 2, 3, 4)` |
| [13 / L1594](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1599) | `SpacePersonnelEvent` / `CK_Space_PersonnelEvent_SourceSequence` | `[SourceSequence] IS NULL OR [SourceSequence] >= 0` |
| [14 / L1597](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1602) | `SpacePersonnelEvent` / `CK_Space_PersonnelEvent_Accuracy` | `[AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)` |
| [15 / L1603](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1608) | `SpacePersonnelEvent` / `CK_Space_PersonnelEvent_SourceKind` | `[SourceKind] IN (0, 1)` |
| [16 / L1606](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1611) | `SpacePersonnelEvent` / `CK_Space_PersonnelEvent_Kind` | `[EventKind] IN (0, 1)` |
| [17 / L1609](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1614) | `SpacePersonnelEvent` / `CK_Space_PersonnelEvent_WorkState` | `[WorkState] IS NULL OR [WorkState] BETWEEN 0 AND 4` |
| [18 / L1612](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1617) | `SpacePersonnelEvent` / `CK_Space_PersonnelEvent_Shape` | `([EventKind] = 0 AND [WorkState] IS NULL AND ([LocationLogicalId] IS NOT NULL OR ([FloorLogicalId] IS NOT NULL AND [XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL))) OR ([EventKind] = 1 AND [WorkState] IS NOT NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL)` |
| [19 / L1687](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1692) | `SpacePersonnelCurrentState` / `CK_Space_PersonnelState_SourceKind` | `[SourceKind] IN (0, 1)` |
| [20 / L1690](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1695) | `SpacePersonnelCurrentState` / `CK_Space_PersonnelState_WorkState` | `[WorkState] BETWEEN 0 AND 4` |
| [21 / L1751](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1756) | `SpaceDeviceMapping` / `CK_Space_DeviceMapping_SourceKind` | `[SourceKind] IN (0, 1)` |
| [22 / L1754](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1759) | `SpaceDeviceMapping` / `CK_Space_DeviceMapping_DeviceKind` | `[DeviceKind] BETWEEN 0 AND 7` |
| [23 / L1836](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1841) | `SpaceDeviceEvent` / `CK_Space_DeviceEvent_SourceKind` | `[SourceKind] IN (0, 1)` |
| [24 / L1839](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1844) | `SpaceDeviceEvent` / `CK_Space_DeviceEvent_DeviceKind` | `[DeviceKind] BETWEEN 0 AND 7` |
| [25 / L1842](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1847) | `SpaceDeviceEvent` / `CK_Space_DeviceEvent_Kind` | `[EventKind] BETWEEN 0 AND 3` |
| [26 / L1845](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1850) | `SpaceDeviceEvent` / `CK_Space_DeviceEvent_OperatingState` | `[OperatingState] IS NULL OR [OperatingState] BETWEEN 0 AND 6` |
| [27 / L1848](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1853) | `SpaceDeviceEvent` / `CK_Space_DeviceEvent_AlarmSeverity` | `[AlarmSeverity] IS NULL OR [AlarmSeverity] BETWEEN 0 AND 2` |
| [28 / L1851](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1856) | `SpaceDeviceEvent` / `CK_Space_DeviceEvent_SourceSequence` | `[SourceSequence] IS NULL OR [SourceSequence] >= 0` |
| [29 / L1854](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1859) | `SpaceDeviceEvent` / `CK_Space_DeviceEvent_CoordinateTriple` | `([XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL) OR ([XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)` |
| [30 / L1858](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1863) | `SpaceDeviceEvent` / `CK_Space_DeviceEvent_Accuracy` | `[AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL)` |
| [31 / L1862](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1867) | `SpaceDeviceEvent` / `CK_Space_DeviceEvent_Shape` | `([EventKind] = 0 AND [OperatingState] IS NULL AND [AlarmExternalId] IS NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL AND ([LocationLogicalId] IS NOT NULL OR ([FloorLogicalId] IS NOT NULL AND [XMillimeters] IS NOT NULL))) OR ([EventKind] = 1 AND [OperatingState] IS NOT NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL) OR ([EventKind] = 2 AND [OperatingState] IS NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NOT NULL AND [AlarmCode] IS NOT NULL AND [AlarmSeverity] IS NOT NULL) OR ([EventKind] = 3 AND [OperatingState] IS NULL AND [FloorLogicalId] IS NULL AND [LocationLogicalId] IS NULL AND [XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL AND [AccuracyMillimeters] IS NULL AND [AlarmExternalId] IS NOT NULL AND [AlarmCode] IS NULL AND [AlarmSeverity] IS NULL AND [AlarmMessage] IS NULL)` |
| [32 / L1975](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1980) | `SpaceDeviceCurrentState` / `CK_Space_DeviceState_SourceKind` | `[SourceKind] IN (0, 1)` |
| [33 / L1978](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1983) | `SpaceDeviceCurrentState` / `CK_Space_DeviceState_OperatingState` | `[OperatingState] BETWEEN 0 AND 6` |
| [34 / L1981](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1986) | `SpaceDeviceCurrentState` / `CK_Space_DeviceState_CoordinateTriple` | `([XMillimeters] IS NULL AND [YMillimeters] IS NULL AND [ZMillimeters] IS NULL) OR ([XMillimeters] IS NOT NULL AND [YMillimeters] IS NOT NULL AND [ZMillimeters] IS NOT NULL)` |
| [35 / L1985](../../../CP6.Space.Infrastructure/SpaceContext.cs#L1990) | `SpaceDeviceCurrentState` / `CK_Space_DeviceState_Accuracy` | `[AccuracyMillimeters] IS NULL OR ([AccuracyMillimeters] >= 0 AND [XMillimeters] IS NOT NULL)` |
| [36 / L2057](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2062) | `SpaceDeviceAlarmState` / `CK_Space_DeviceAlarmState_SourceKind` | `[SourceKind] IN (0, 1)` |
| [37 / L2060](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2065) | `SpaceDeviceAlarmState` / `CK_Space_DeviceAlarmState_Severity` | `[AlarmSeverity] IS NULL OR [AlarmSeverity] BETWEEN 0 AND 2` |
| [38 / L2063](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2068) | `SpaceDeviceAlarmState` / `CK_Space_DeviceAlarmState_SourceSequence` | `[SourceSequence] IS NULL OR [SourceSequence] >= 0` |
| [39 / L2066](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2071) | `SpaceDeviceAlarmState` / `CK_Space_DeviceAlarmState_ActiveShape` | `[IsActive] = 0 OR ([AlarmCode] IS NOT NULL AND [AlarmSeverity] IS NOT NULL)` |
| [40 / L2132](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2137) | `SpaceAsset` / `CK_Space_Asset_ScopeOwner` | `([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')` |
| [41 / L2188](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2193) | `SpaceAssetVersion` / `CK_Space_AssetVersion_ScopeOwner` | `([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')` |
| [42 / L2192](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2197) | `SpaceAssetVersion` / `CK_Space_AssetVersion_VersionNo` | `[VersionNo] > 0` |
| [43 / L2266](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2271) | `SpaceWarehouseTemplate` / `CK_Space_WarehouseTemplate_CurrentVersion` | `[CurrentVersion] > 0` |
| [44 / L2297](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2302) | `SpaceWarehouseTemplateVersion` / `CK_Space_WarehouseTemplateVersion_VersionNo` | `[VersionNo] > 0` |
| [45 / L2300](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2305) | `SpaceWarehouseTemplateVersion` / `CK_Space_WarehouseTemplateVersion_SchemaVersion` | `[SchemaVersion] > 0` |
| [46 / L2303](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2308) | `SpaceWarehouseTemplateVersion` / `CK_Space_WarehouseTemplateVersion_Counts` | `[FloorCount] > 0 AND [ZoneCount] >= 0 AND [AisleCount] >= 0 AND [RackCount] >= 0 AND [LocationCount] >= 0` |
| [47 / L2347](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2352) | `SpaceRackGenerationProfile` / `CK_Space_RackGenerationProfile_ScopeOwner` | `([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')` |
| [48 / L2394](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2399) | `SpaceRackGenerationProfileVersion` / `CK_Space_RackGenerationProfileVersion_ScopeOwner` | `([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')` |
| [49 / L2398](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2403) | `SpaceRackGenerationProfileVersion` / `CK_Space_RackGenerationProfileVersion_VersionNo` | `[VersionNo] > 0` |
| [50 / L2401](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2406) | `SpaceRackGenerationProfileVersion` / `CK_Space_RackGenerationProfileVersion_Dimensions` | `[RackWidthMillimeters] > 0 AND [RackDepthMillimeters] > 0 AND [RackHeightMillimeters] > 0` |
| [51 / L2404](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2409) | `SpaceRackGenerationProfileVersion` / `CK_Space_RackGenerationProfileVersion_LocationCount` | `[LocationCount] > 0 AND [LocationCount] <= 10000000` |
| [52 / L2475](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2480) | `SpaceTenantEntity<T>` / `CK_Space_ElementRevision_Geometry` | `[RotationZ] >= 0 AND [RotationZ] < 360 AND [Width] >= 0 AND [Height] >= 0 AND [Depth] >= 0` |
| [53 / L2478](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2483) | `SpaceTenantEntity<T>` / `CK_Space_ElementRevision_ModelAssetScope` | `([ModelAssetId] IS NULL AND [ModelAssetScope] IS NULL AND [ModelAssetOwnerTenantId] IS NULL) OR ([ModelAssetId] IS NOT NULL AND [ModelAssetScope] IS NOT NULL AND [ModelAssetOwnerTenantId] IS NOT NULL AND (([ModelAssetScope] = 0 AND [ModelAssetOwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([ModelAssetScope] = 1 AND [ModelAssetOwnerTenantId] = [TenantId])))` |
| [54 / L2484](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2489) | `SpaceTenantEntity<T>` / `CK_Space_ElementRevision_ManualCorrection` | `[UserCorrectionVersion] >= 0 AND ([IsManualCorrectionLocked] = 0 OR ([SourceId] IS NOT NULL AND [SourceRef] IS NOT NULL AND [UserCorrectionVersion] > 0 AND [ManualCorrectionUpdatedBy] IS NOT NULL AND [ManualCorrectionUpdatedAtUtc] IS NOT NULL))` |
| [55 / L2614](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2619) | `SpaceElementCommandBatch` / `CK_Space_ElementCommandBatch_Result` | `([ResultFloorRevision] IS NULL AND [ResultVersionContentRevision] IS NULL AND [ResponseJson] IS NULL) OR ([ResultFloorRevision] IS NOT NULL AND [ResultVersionContentRevision] IS NOT NULL AND [ResponseJson] IS NOT NULL)` |
| [56 / L2901](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2906) | `SpaceCadSiteProviderCertification` / `CK_Space_CadProviderCertification_QualificationScore` | `[QualificationScore] IS NULL OR ([QualificationScore] >= 0 AND [QualificationScore] <= 100)` |
| [57 / L2982](../../../CP6.Space.Infrastructure/SpaceContext.cs#L2987) | `SpaceFile` / `CK_Space_File_ContentDeletion` | `[ContentDeletedAtUtc] IS NULL OR ([State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [IsDeleted] = 1)` |
| [58 / L3165](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3170) | `SpaceJob` / `CK_Space_Job_Attempts` | `[AttemptCount] >= 0 AND [MaxAttempts] BETWEEN 1 AND 20 AND [AttemptCount] <= [MaxAttempts]` |
| [59 / L3168](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3173) | `SpaceJob` / `CK_Space_Job_Progress` | `[ProgressDone] >= 0 AND [ProgressTotal] >= 0 AND ([ProgressTotal] = 0 OR [ProgressDone] <= [ProgressTotal])` |
| [60 / L3171](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3176) | `SpaceJob` / `CK_Space_Job_Lease` | `([Status] = 1 AND [LockedBy] IS NOT NULL AND [LockedAtUtc] IS NOT NULL AND [LockExpiresAtUtc] IS NOT NULL AND [ActiveAttemptId] IS NOT NULL) OR ([Status] <> 1 AND [LockedBy] IS NULL AND [LockedAtUtc] IS NULL AND [LockExpiresAtUtc] IS NULL AND [ActiveAttemptId] IS NULL)` |
| [61 / L3248](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3253) | `SpaceJobAttempt` / `CK_Space_JobAttempt_OutcomeTime` | `([Outcome] = 0 AND [FinishedAtUtc] IS NULL) OR ([Outcome] <> 0 AND [FinishedAtUtc] IS NOT NULL)` |
| [62 / L3302](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3307) | `SpaceJobStep` / `CK_Space_JobStep_StatusTime` | `([Status] = 0 AND [FinishedAtUtc] IS NULL) OR ([Status] <> 0 AND [FinishedAtUtc] IS NOT NULL)` |
| [63 / L3344](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3349) | `SpaceValidationRun` / `CK_Space_ValidationRun_StatusTime` | `([Status] = 0 AND [StartedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR ([Status] = 1 AND [StartedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([Status] IN (2, 3, 4) AND [FinishedAtUtc] IS NOT NULL)` |
| [64 / L3349](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3354) | `SpaceValidationRun` / `CK_Space_ValidationRun_Counts` | `[BlockingCount] >= 0 AND [WarningCount] >= 0 AND [InfoCount] >= 0 AND ([Status] <> 2 OR [BlockingCount] = 0) AND ([Status] <> 3 OR [BlockingCount] > 0)` |
| [65 / L3439](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3444) | `SpaceModelIssue` / `CK_Space_ModelIssue_Context` | `[ModelVersionId] IS NOT NULL OR [SourceId] IS NOT NULL OR [JobId] IS NOT NULL` |
| [66 / L3442](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3447) | `SpaceModelIssue` / `CK_Space_ModelIssue_SourceVersion` | `[SourceId] IS NULL OR [ModelVersionId] IS NOT NULL` |
| [67 / L3445](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3450) | `SpaceModelIssue` / `CK_Space_ModelIssue_GenerationScope` | `([GenerationProposalId] IS NULL OR [GenerationRunId] IS NOT NULL) AND ([ResolutionDecisionId] IS NULL OR [GenerationProposalId] IS NOT NULL)` |
| [68 / L3449](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3454) | `SpaceModelIssue` / `CK_Space_ModelIssue_Resolution` | `([Status] <> 1 AND [ResolutionKind] = 0 AND [ResolutionCommandBatchId] IS NULL AND [ResolutionDecisionId] IS NULL) OR ([Status] = 1 AND (([ResolutionKind] = 1 AND [ResolutionCommandBatchId] IS NOT NULL AND [ResolutionDecisionId] IS NULL) OR ([ResolutionKind] IN (2, 3) AND [ResolutionCommandBatchId] IS NULL AND [ResolutionDecisionId] IS NOT NULL)))` |
| [69 / L3457](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3462) | `SpaceModelIssue` / `CK_Space_ModelIssue_ValidationScope` | `[ValidationRunId] IS NULL OR ([ModelVersionId] IS NOT NULL AND [JobId] IS NOT NULL)` |
| [70 / L3652](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3657) | `SpaceGenerationRun` / `CK_Space_GenerationRun_Progress` | `[Progress] >= 0 AND [Progress] <= 100` |
| [71 / L3827](../../../CP6.Space.Infrastructure/SpaceContext.cs#L3832) | `SpaceGenerationProposal` / `CK_Space_GenerationProposal_Confidence` | `[ConfidenceScore] >= 0 AND [ConfidenceScore] <= 1` |
| [72 / L4003](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4008) | `SpaceGenerationLockedFact` / `CK_Space_GenerationLockedFact_Match` | `[MatchScore] >= 0 AND [MatchScore] <= 1 AND [RunId] <> [BasedOnRunId]` |
| [73 / L4086](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4091) | `SpaceGenerationStagingElement` / `CK_Space_GenerationStagingElement_Validation` | `([ValidationStatus] = 0 AND [ValidationHash] IS NULL) OR ([ValidationStatus] = 1 AND [ValidationHash] IS NOT NULL)` |
| [74 / L4173](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4178) | `SpaceAiUsageRecord` / `CK_Space_AiUsageRecord_Units` | `[InputUnits] >= 0 AND [OutputUnits] >= 0` |
| [75 / L4176](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4181) | `SpaceAiUsageRecord` / `CK_Space_AiUsageRecord_Cost` | `[EstimatedCostMinor] >= 0 AND ([ActualCostMinor] IS NULL OR [ActualCostMinor] >= 0)` |
| [76 / L4180](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4185) | `SpaceAiUsageRecord` / `CK_Space_AiUsageRecord_Latency` | `[LatencyMs] >= 0` |
| [77 / L4263](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4268) | `SpaceTenantAiWorkSlot` / `CK_Space_TenantAiWorkSlot_SlotNo` | `[SlotNo] >= 1 AND [SlotNo] <= 3` |
| [78 / L4266](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4271) | `SpaceTenantAiWorkSlot` / `CK_Space_TenantAiWorkSlot_Lease` | `([RunId] IS NULL AND [LeaseOwner] IS NULL AND [LeaseExpiresAtUtc] IS NULL) OR ([RunId] IS NOT NULL AND [LeaseOwner] IS NOT NULL AND [LeaseExpiresAtUtc] IS NOT NULL)` |
| [79 / L4314](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4319) | `SpaceAiBudgetReservation` / `CK_Space_AiBudgetReservation_Cost` | `[ReservedCostMinor] >= 0 AND ([ActualCostMinor] IS NULL OR [ActualCostMinor] >= 0)` |
| [80 / L4318](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4323) | `SpaceAiBudgetReservation` / `CK_Space_AiBudgetReservation_Period` | `[PeriodMonth] = YEAR([PeriodDay]) * 100 + MONTH([PeriodDay])` |
| [81 / L4322](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4327) | `SpaceAiBudgetReservation` / `CK_Space_AiBudgetReservation_Currency` | `[ReservedCostMinor] = 0 OR [Currency] IS NOT NULL` |
| [82 / L4397](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4402) | `SpaceAiTenantPolicyConfiguration` / `CK_Space_AiTenantPolicy_Version` | `[Version] >= 1` |
| [83 / L4400](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4405) | `SpaceAiTenantPolicyConfiguration` / `CK_Space_AiTenantPolicy_Concurrency` | `[MaxConcurrentRuns] >= 1 AND [MaxConcurrentRuns] <= 3` |
| [84 / L4403](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4408) | `SpaceAiTenantPolicyConfiguration` / `CK_Space_AiTenantPolicy_Budget` | `([DailyBudgetMinor] IS NULL OR [DailyBudgetMinor] >= 0) AND ([MonthlyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] >= 0) AND ([DailyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] >= [DailyBudgetMinor])` |
| [85 / L4409](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4414) | `SpaceAiTenantPolicyConfiguration` / `CK_Space_AiTenantPolicy_Currency` | `([DailyBudgetMinor] IS NULL AND [MonthlyBudgetMinor] IS NULL) OR [Currency] IS NOT NULL` |
| [86 / L4484](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4489) | `SpaceExternalOrganization` / `CK_Space_ExternalOrganization_BusinessPartner` | `([BusinessPartnerType] IS NULL AND [BusinessPartnerId] IS NULL) OR ([BusinessPartnerType] IS NOT NULL AND [BusinessPartnerId] IS NOT NULL)` |
| [87 / L4488](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4493) | `SpaceExternalOrganization` / `CK_Space_ExternalOrganization_Type` | `[Type] >= 0 AND [Type] <= 2` |
| [88 / L4491](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4496) | `SpaceExternalOrganization` / `CK_Space_ExternalOrganization_Status` | `[Status] >= 0 AND [Status] <= 2` |
| [89 / L4557](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4562) | `SpaceExternalMembership` / `CK_Space_ExternalMembership_Validity` | `[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]` |
| [90 / L4560](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4565) | `SpaceExternalMembership` / `CK_Space_ExternalMembership_Role` | `[Role] >= 0 AND [Role] <= 2` |
| [91 / L4563](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4568) | `SpaceExternalMembership` / `CK_Space_ExternalMembership_Status` | `[Status] >= 0 AND [Status] <= 3` |
| [92 / L4629](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4634) | `SpaceExternalGrant` / `CK_Space_ExternalGrant_Status` | `[Status] >= 0 AND [Status] <= 2` |
| [93 / L4632](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4637) | `SpaceExternalGrant` / `CK_Space_ExternalGrant_Validity` | `[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]` |
| [94 / L4635](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4640) | `SpaceExternalGrant` / `CK_Space_ExternalGrant_Version` | `[GrantVersion] > 0` |
| [95 / L4829](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4834) | `SpaceFieldPolicy` / `CK_Space_FieldPolicy_AudienceType` | `[AudienceType] >= 0 AND [AudienceType] <= 2` |
| [96 / L4832](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4837) | `SpaceFieldPolicy` / `CK_Space_FieldPolicy_Status` | `[Status] >= 0 AND [Status] <= 1` |
| [97 / L4835](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4840) | `SpaceFieldPolicy` / `CK_Space_FieldPolicy_Version` | `[PolicyVersion] > 0` |
| [98 / L4876](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4881) | `SpaceFieldPolicyField` / `CK_Space_FieldPolicyField_ResourceType` | `[ResourceType] >= 0 AND [ResourceType] <= 2` |
| [99 / L4879](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4884) | `SpaceFieldPolicyField` / `CK_Space_FieldPolicyField_MaskingRule` | `[MaskingRule] >= 0 AND [MaskingRule] <= 3` |
| [100 / L4923](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4928) | `SpaceCadMappingProfile` / `CK_Space_LayerMappingProfile_CurrentVersion` | `[CurrentVersion] > 0` |
| [101 / L4951](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4956) | `SpaceCadMappingProfileVersion` / `CK_Space_LayerMappingProfileVersion_Version` | `[Version] > 0` |
| [102 / L4954](../../../CP6.Space.Infrastructure/SpaceContext.cs#L4959) | `SpaceCadMappingProfileVersion` / `CK_Space_LayerMappingProfileVersion_Base` | `([BasedOnProfileId] IS NULL AND [BasedOnVersion] IS NULL) OR ([BasedOnProfileId] IS NOT NULL AND [BasedOnVersion] > 0)` |
| [103 / L4997](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5002) | `SpaceExcelMappingProfile` / `CK_Space_ExcelMappingProfile_CurrentVersion` | `[CurrentVersion] > 0` |
| [104 / L5025](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5030) | `SpaceExcelMappingProfileVersion` / `CK_Space_ExcelMappingProfileVersion_Version` | `[Version] > 0` |
| [105 / L5028](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5033) | `SpaceExcelMappingProfileVersion` / `CK_Space_ExcelMappingProfileVersion_Base` | `([BasedOnProfileId] IS NULL AND [BasedOnVersion] IS NULL) OR ([BasedOnProfileId] IS NOT NULL AND [BasedOnVersion] > 0)` |
| [106 / L5073](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5078) | `SpacePutawayRecommendation` / `CK_Space_PutawayRecommendation_Counts` | `[ExaminedLocationCount] >= 0 AND [EligibleCandidateCount] >= 0 AND [ReturnedCandidateCount] >= 0 AND [EligibleCandidateCount] <= [ExaminedLocationCount] AND [ReturnedCandidateCount] <= [EligibleCandidateCount] AND (([IsTruncated] = 1 AND [ReturnedCandidateCount] < [EligibleCandidateCount]) OR ([IsTruncated] = 0 AND [ReturnedCandidateCount] = [EligibleCandidateCount]))` |
| [107 / L5084](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5089) | `SpacePutawayRecommendation` / `CK_Space_PutawayRecommendation_Evidence` | `[Outcome] IN ('NoCandidate', 'CandidatesGenerated') AND ISJSON([RequestJson]) = 1 AND ISJSON([SourcesJson]) = 1 AND ISJSON([ExclusionsJson]) = 1 AND ISJSON([ExclusionSamplesJson]) = 1 AND ISJSON([CandidatesJson]) = 1 AND ISJSON([LimitationsJson]) = 1` |
| [108 / L5093](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5098) | `SpacePutawayRecommendation` / `CK_Space_PutawayRecommendation_Immutable` | `LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0` |
| [109 / L5168](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5173) | `SpaceDispatchRecommendation` / `CK_Space_DispatchRecommendation_Counts` | `[ExaminedTaskCount] >= 0 AND [EligibleTaskCount] >= 0 AND [ExaminedPersonCount] >= 0 AND [EligiblePersonCount] >= 0 AND [EligiblePairCount] >= 0 AND [MatchableAssignmentCount] >= 0 AND [ReturnedAssignmentCount] >= 0 AND [EligibleTaskCount] <= [ExaminedTaskCount] AND [EligiblePersonCount] <= [ExaminedPersonCount] AND [MatchableAssignmentCount] <= [EligibleTaskCount] AND [MatchableAssignmentCount] <= [EligiblePersonCount] AND [MatchableAssignmentCount] <= [EligiblePairCount] AND [ReturnedAssignmentCount] <= [MatchableAssignmentCount] AND (([IsTruncated] = 1 AND [ReturnedAssignmentCount] < [MatchableAssignmentCount]) OR ([IsTruncated] = 0 AND [ReturnedAssignmentCount] = [MatchableAssignmentCount]))` |
| [110 / L5187](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5192) | `SpaceDispatchRecommendation` / `CK_Space_DispatchRecommendation_Evidence` | `[Outcome] IN ('NoAssignment', 'AssignmentsGenerated') AND ISJSON([RequestJson]) = 1 AND ISJSON([SourcesJson]) = 1 AND ISJSON([ExclusionsJson]) = 1 AND ISJSON([ExclusionSamplesJson]) = 1 AND ISJSON([AssignmentsJson]) = 1 AND ISJSON([LimitationsJson]) = 1` |
| [111 / L5196](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5201) | `SpaceDispatchRecommendation` / `CK_Space_DispatchRecommendation_Immutable` | `LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0` |
| [112 / L5269](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5274) | `SpacePlanningScenarioBranch` / `CK_Space_PlanningScenarioBranch_Immutable` | `[BasePublishedVersionId] <> [ScenarioVersionId] AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0` |
| [113 / L5348](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5353) | `SpacePlanningHistoricalDataset` / `CK_Space_PlanningHistoricalDataset_Invariants` | `[HistoricalFromUtc] < [HistoricalToUtc] AND [ReplaySpeedFactor] > 0 AND [ReplaySpeedFactor] <= 1000 AND [TaskCount] BETWEEN 1 AND 10000 AND LEN([SourceDatasetHash]) = 64 AND [SourceDatasetHash] NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0` |
| [114 / L5450](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5455) | `SpacePlanningHistoricalTask` / `CK_Space_PlanningHistoricalTask_Invariants` | `[SequenceNo] > 0 AND [Quantity] > 0 AND [OriginalCreatedAtUtc] <= [OriginalCompletedAtUtc] AND [ReplayCreatedAtUtc] <= [ReplayCompletedAtUtc] AND [ToLocationLogicalId] <> '00000000-0000-0000-0000-000000000000' AND ([FromLocationLogicalId] IS NULL OR [FromLocationLogicalId] <> '00000000-0000-0000-0000-000000000000') AND LEN([TaskToken]) = 64 AND [TaskToken] NOT LIKE '%[^0-9a-f]%' AND ([WorkerToken] IS NULL OR (LEN([WorkerToken]) = 64 AND [WorkerToken] NOT LIKE '%[^0-9a-f]%')) AND [TaskType] BETWEEN 0 AND 4 AND [Outcome] BETWEEN 0 AND 2 AND [IsDeleted] = 0` |
| [115 / L5529](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5534) | `SpacePlanningSimulationRun` / `CK_Space_PlanningSimulationRun_Invariants` | `[ScenarioContentRevision] >= 0 AND [DefaultQuantityCapacity] > 0 AND [DefaultConcurrentTaskCapacity] BETWEEN 1 AND 10000 AND [LocationCapacityOverrideCount] BETWEEN 0 AND 10000 AND [ThroughputWindowMinutes] BETWEEN 1 AND 1440 AND [DistanceCostPerMeter] >= 0 AND [LaborCostPerHour] >= 0 AND [CongestionCostPerTaskHour] >= 0 AND [TaskCount] BETWEEN 1 AND 10000 AND [CompletedTaskCount] BETWEEN 0 AND [TaskCount] AND [CompletedQuantity] >= 0 AND [DistanceEligibleTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalDistanceMeters] >= 0 AND [DistanceCoveragePercent] BETWEEN 0 AND 100 AND [PeakConcurrentTasks] >= 0 AND [CongestionSeconds] >= 0 AND [CongestionTaskSeconds] >= 0 AND [OverloadedLocationCount] >= 0 AND [PeakCapacityUtilizationPercent] >= 0 AND [AverageCompletedTasksPerHour] >= 0 AND [PeakCompletedTasksPerHour] >= 0 AND [AverageCompletedQuantityPerHour] >= 0 AND [PeakCompletedQuantityPerHour] >= 0 AND [LaborHours] >= 0 AND [DistanceCost] >= 0 AND [LaborCost] >= 0 AND [CongestionCost] >= 0 AND [TotalCost] >= 0 AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([DatasetRequestHash]) = 64 AND [DatasetRequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([ResultHash]) = 64 AND [ResultHash] NOT LIKE '%[^0-9a-f]%' AND LEN([CurrencyCode]) = 3 AND [CurrencyCode] NOT LIKE '%[^A-Z]%' AND [IsDeleted] = 0` |
| [116 / L5717](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5722) | `SpacePlanningSimulationLocationResult` / `CK_Space_PlanningSimulationLocationResult_Invariants` | `[TaskCount] > 0 AND [CompletedTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalQuantity] > 0 AND [DistanceEligibleTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalDistanceMeters] >= 0 AND [QuantityCapacity] > 0 AND [ConcurrentTaskCapacity] BETWEEN 1 AND 10000 AND [PeakConcurrentTasks] >= 0 AND [PeakConcurrentQuantity] >= 0 AND [CapacityUtilizationPercent] >= 0 AND [CongestionSeconds] >= 0 AND [CongestionTaskSeconds] >= 0 AND [IsDeleted] = 0` |
| [117 / L5791](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5796) | `SpacePlanningComparison` / `CK_Space_PlanningComparison_Invariants` | `[RunCount] BETWEEN 2 AND 10 AND [HistoricalFromUtc] < [HistoricalToUtc] AND [MinimumDistanceCoveragePercent] BETWEEN 0 AND 100 AND [MaximumPeakCapacityUtilizationPercent] >= 0 AND [MaximumCongestionTaskHours] >= 0 AND ([MaximumTotalCost] IS NULL OR [MaximumTotalCost] >= 0) AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([ComparisonHash]) = 64 AND [ComparisonHash] NOT LIKE '%[^0-9a-f]%' AND LEN([SourceDatasetHash]) = 64 AND [SourceDatasetHash] NOT LIKE '%[^0-9a-f]%' AND LEN([CurrencyCode]) = 3 AND [CurrencyCode] NOT LIKE '%[^A-Z]%' AND [IsDeleted] = 0` |
| [118 / L5899](../../../CP6.Space.Infrastructure/SpaceContext.cs#L5904) | `SpacePlanningComparisonEntry` / `CK_Space_PlanningComparisonEntry_Invariants` | `[SequenceNo] BETWEEN 1 AND 10 AND [ScenarioContentRevision] >= 0 AND [DistanceCoveragePercent] BETWEEN 0 AND 100 AND [TotalDistanceMeters] >= 0 AND [CongestionTaskSeconds] >= 0 AND [OverloadedLocationCount] >= 0 AND [PeakCapacityUtilizationPercent] >= 0 AND [AverageCompletedTasksPerHour] >= 0 AND [PeakCompletedTasksPerHour] >= 0 AND [TotalCost] >= 0 AND [RiskCount] BETWEEN 0 AND 10 AND LEN([RunResultHash]) = 64 AND [RunResultHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0` |
| [119 / L6015](../../../CP6.Space.Infrastructure/SpaceContext.cs#L6020) | `SpacePlanningComparisonRisk` / `CK_Space_PlanningComparisonRisk_Invariants` | `[Severity] BETWEEN 1 AND 3 AND LEN([Code]) BETWEEN 1 AND 100 AND [IsDeleted] = 0` |
| [120 / L6063](../../../CP6.Space.Infrastructure/SpaceContext.cs#L6068) | `SpacePlanningDecisionRecord` / `CK_Space_PlanningDecisionRecord_Invariants` | `[Outcome] BETWEEN 1 AND 3 AND (([Outcome] = 1 AND [SelectedRunId] IS NOT NULL) OR ([Outcome] IN (2, 3) AND [SelectedRunId] IS NULL)) AND ([SupersedesDecisionId] IS NULL OR [SupersedesDecisionId] <> [Id]) AND LEN([Rationale]) BETWEEN 1 AND 2000 AND LEN([ComparisonHash]) = 64 AND [ComparisonHash] NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0` |

## Remaining verification and delivery

The PG branch and exact UTC tuples are implemented; original expressions and real REDs remain unchanged. BUG137/139/141/143 have independently merged and Closed, with historical aliases checked by actual ordered keys/filter semantics. Actual compiled PG and final SQL first/repeat initialization and seed retention passed. Relevant151/151 plus separate25/25,30/45 task review and later delta review retain their recorded inputs. All eight owned databases were cleaned and absence verified ([actual proof](wp2-native/wp2-cleanup-final-owned-eight-verified-env.json)); WP2 remote delivery remains Pending. Full Space writers, worker SQL, recovery, API and WP3–WP6 gates are not claimed by these model/catalog fixtures.

The 142 original evidence files (prepared 140 plus final byte proof/script 2) are archived and all source/copy hashes and byte sizes match: [manifest](wp2-native/manifest.json). The [final recorded-input proof](wp2-native/wp2-final-review-source-applicability.json) reports 75 unique source inputs unchanged; this is a byte-applicability check, not a new 75-file AI review, test or execution. WP2 remote delivery remains Pending.
