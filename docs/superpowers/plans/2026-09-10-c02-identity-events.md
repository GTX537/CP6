# C02 Identity Events Implementation Plan

> **For agentic workers:** Use `executing-plans` in the current task. Steps use checkboxes to record actual implementation and results. The user approved the design on 2026-09-10; no additional execution-choice approval is required.

**Goal:** CP6 identity changes and revocations reach CRM through atomic Outbox/Inbox processing, with local authorization projections and drift reconciliation.

**Architecture:** Core owns versioned identity snapshots and the five event Schemas. The producer reuses published Platform `0.10.2` messaging and transaction components; CRM consumes the same contract bytes, checks local projections before business access, and reconciles against tenant-bound service APIs. Ordinary and priority identity queues share the same SQL transaction and business Topic.

**Tech Stack:** .NET 8, EF Core/SQL Server, Platform `0.10.2`, Draft 2020-12, Dapr/Kafka, xUnit and the existing real SQL fixtures.

Approved scope: [C02 design](../specs/2026-09-10-c02-identity-events-design.md). C01 final acceptance precedes runtime implementation. Deliver Core, CRM and the transport evidence through their normal task branches; never report one completed component as complete C02.

## 1. Core contracts and persistence

Create `contracts/events/platform/contract-bundle.v1.json`, the five `<event-type>/v1/schema.json` files and their required five-example matrices. Add `CP6.Core/Services/CrmIdentity/IdentityEventContracts.cs` for safe typed payloads and `IdentityEventValidator.cs` to adapt the actual Platform validator to Outbox/Inbox validation.

Use the existing SDK APIs directly:

```csharp
var bundle = Cp6ContractBundle.Load(contractDirectory);
var validator = new Cp6CloudEventValidator(bundle);
var result = validator.Validate(structuredCloudEvent);
```

- [x] Add exact `[0.10.2]` Messaging/EntityFramework references to `CP6.Core/CP6.Core.csproj` and AspNetCore to `CP6.WebApi/CP6.WebApi.csproj`. Configure authenticated GitHub Packages restore, source mapping and package locks; align the directly referenced EF/JWT patch versions with the published packages, preserving the other dependencies.
- [x] Add `CP6.Tests/CrmIdentityContractTests.cs`: all five example matrices; false issuer/source, mismatched aggregate/data versions, cross-tenant body, duplicate JSON keys and disallowed PII; valid unknown optional extensions remain accepted. Run only this class while implementing the contract.
- [x] Add `CP6.Entity/DomainModels/Sys/CrmIdentitySnapshot.cs`: TenantId, typed AggregateId, Version, PayloadJson, PayloadSha256, IsDeleted, UpdatedAtUtc, RowVersion; unique `(TenantId, AggregateId)`. A content-identical update does not advance Version.
- [x] Add `CP6.Core/Services/CrmIdentity/IdentityMessagingContext.cs` for the priority queue and configure normal identity messaging in `CP6Context`. Both contexts use Platform table mappings under separate identity schemas. The priority context joins the producer's existing SQL connection/transaction, never commits independently.
- [x] Add `CP6.Core/Migrations/20260910043605_CrmIdentityEvents.cs` and update the model snapshot, including the priority messaging tables. Verify forward migration on the real disposable SQL baseline; do not use EnsureCreated as migration proof.

The Outbox insertion is an enqueue into the caller's transaction:

```csharp
new Cp6OutboxStore<CP6Context>(context, envelopeValidator).Enqueue(envelope);
```

## 2. Core business writes and revocations

Create `IdentityChangeCapture.cs`, `IdentitySnapshotWriter.cs` and `IdentityPermissionSnapshot.cs` under `CP6.Core/Services/CrmIdentity`. Keep `CP6Context` changes limited to invoking capture/write around its existing transaction and field-audit behavior.

- [x] Capture affected tenant/user/department/role IDs before saves/deletes. Read the final state inside the same transaction; write the snapshot, monotonic version and validated event before committing. Preserve synchronous and asynchronous save entry points and no-event saves.
- [x] Cover `UserController`, `RoleController`, `DeptService`, `UserRoleService`, `RolePermService`, `TenantAdminService`, SSO creation and identity-invalidating password/2FA/lock changes. Use current main/extra role union and enabled-role semantics; moved department descendants receive updated paths.
- [x] Adapt the direct SQL GDPR paths in `GdprService` to write durable tombstones and revocations within their transactions. Do not lose the identity event when the underlying entity is removed.
- [x] Update `SqlCrmOidcGrantStore` in `CP6.WebApi/Services/CrmOidcGrantStore.cs`: collect the affected grant IDs under the existing session-family lock and append token-revoked events in the same transaction as refresh/session/grant revocation.
- [x] Add `CrmServiceTokenRecord` persistence, record actual issuer/client/tenant/jti/expiry before returning a service token, and implement `POST /connect/service-revocations` in `CrmOidcController`. Reuse strict Basic client authentication; scope the lookup to that client and bound tenant, with idempotent non-enumerating responses.
- [x] Add `CP6.Tests/CrmIdentityChangeCaptureTests.cs` and `CrmIdentityRevocationTests.cs`. Prove no event for unchanged state, role/user ID separation, empty-grant removal, own-client-only service revocation and original CRM grant jti selection.
- [x] Add real SQL scenarios to `eng/crm/identity-events-fixture`: inject snapshot/envelope failures and assert business/version/Outbox zero partial writes; concurrently update an aggregate and assert a single ordered committed version sequence; cover native and direct SQL revocation paths.

A successful transaction must satisfy this observable relationship:

```text
committed business state
  = committed identity snapshot/version
  = committed validated Outbox facts
```

Do not publish to Kafka inside the business transaction. Preserve the current 300-second service token lifetime and actual consumer clock skew.

## 3. Core bootstrap, read APIs and dispatch

Create `IdentitySnapshotReader.cs`, `IdentityBootstrapService.cs`, `CP6.WebApi/Controllers/Internal/CrmIdentityController.cs` and `CP6.WebApi/BackgroundServices/IdentityEventDispatchWorker.cs`.

- [x] Add `GET /internal/crm/identity/versions` with at most 200 rows/page and a tenant/boundary-bound cursor; add `GET /internal/crm/identity/snapshots/{aggregateId}`. Validate real CP6.Services tokens and an explicit projection-reader client allowlist. Request parameters cannot change the authenticated tenant.
- [x] Build initial snapshots for explicitly configured CRM tenants. Assign versions atomically and preserve tombstones; expose bootstrap readiness separately from process readiness.
- [x] Run the actual Platform Outbox dispatcher for the independent priority and ordinary queue budgets. Use `ICp6OutboxPublisher` with the actual Dapr transport; retain the fixed Topic and `{tenantid}:{aggregateid}` partition key. Implement bounded retry/dead-letter handling and ownership-safe shutdown.
- [x] Test unsigned/wrong-audience/wrong-client/cross-tenant read requests, pagination boundaries, concurrent bootstrap/event writes and priority progress during an ordinary backlog. Use the actual HTTP middleware in the SQL fixture.
- [x] Review the complete Core diff once, use the owner-authorized local verification and publication evidence, normally merge and confirm remote main. Record precise source/package/schema identities and component-only completion in the four project records.

## 4. CRM Inbox and local authorization

Create an isolated CRM branch from its then-current verified main. Add `src/CP6.CRM.Infrastructure/Identity` for projection entities, `IdentityProjectionStore`, `IdentityEventConsumer`, and `IdentityProjectionMigration`; add `src/CP6.CRM.Application/Identity` for the authorization reader. Copy the exact reviewed Core contract bundle and preserve its SHA in the consumer locator.

- [x] Register Platform Inbox processing and the versioned business projection tables. Unique keys include TenantId; revoke identity is `(Issuer, Jti)` and expires only after token expiry plus the configured maximum validation skew.
- [x] Validate exact contract/type/source/tenant/partition identities before Inbox effects. Process full snapshots transactionally. Identical redelivery is a duplicate, different payload under the same ID is a conflict, old versions do not mutate state, and version gaps mark dependent users unavailable pending reconciliation.
- [x] Add non-secret tenant/subject/issuer/jti metadata to durable auth-store session rows. Stop accepting the old unindexed session format when the projection feature is enabled; existing protected payloads remain protected.
- [x] Integrate the projection reader into `src/CP6.CRM.Api/Authentication/CrmAuthenticationService.cs` after actual identity/session validation and before returning an actor. Check current tenant/user/role/department/revocation/health on every management request; do not cache cross-request allow decisions. Consume disable/revoke events to remove affected durable sessions.
- [x] Preserve public-route behavior and the existing UI revalidation that removes PII/drafts after authorization loss. Test the actual management request path, not only isolated projection classes.
- [x] Add `tests/CP6.CRM.UnitTests/IdentityProjectionTests.cs` and real SQL fixture scenarios for duplicate/out-of-order/conflict, cross-tenant keys, missing dependencies, permission removal, session invalidation and projection unavailability.

## 5. CRM initialization and reconciliation

Add `IdentityReconciliationClient`, `IdentityReconciliationWorker` and persistent reconciliation state under `src/CP6.CRM.Infrastructure/Identity`.

- [x] Authenticate with the configured tenant-bound service client, import the consistent snapshot baseline and consume subsequent events. Keep management authorization closed until required projections are complete.
- [x] Compare Core versions/hashes every 15 minutes; mark dependent users unavailable before repair. Fetch only affected full snapshots. Apply repair and clear drift within a transaction that cannot overwrite a newer event.
- [x] Retain failure/retry state and emit safe alert/latency metrics. A stale or unavailable reconciliation baseline cannot grant access indefinitely.
- [x] Test the deterministic 15-minute schedule, omitted/tampered snapshot detection, failed repair, concurrent newer events and successful recovery. Reuse existing SQL results where source and inputs have not changed.
- [x] Review the whole CRM diff once, use the owner-authorized local verification and publication evidence and normally deliver to remote main. Synchronize its four project records without calling component delivery complete C02.

## 6. Actual transport acceptance and records

Use the CRM entry `scripts/test-c02-real-transport.ps1` and `tests/CP6.CRM.C02Acceptance` with the Core identity fixture. The Core component entry remains `scripts/test-c02-identity-events.ps1`. Use private external connection/client inputs and uniquely owned SQL/transport resources. Reuse current Docker availability without altering the running development stack.

- [x] Start local committed Core/CRM code, real SQL and Dapr/Kafka; execute business entry points. The user's later budget policy permits local verification before integration. Core PR #101 and CRM PR #60 now normally deliver the verified runtime inputs; original execution identities remain unchanged.
- [x] Prove duplicate/out-of-order/failure replay, sender crash after SQL commit, receiver crash before acknowledgement, two-tenant isolation, projection outage and reconciliation recovery.
- [x] Observe actual management HTTP denial after user disable, tenant disable and token revocation. Record every trigger/denial timestamp, sample count, failure count and p99; require at least 99% within 30 real seconds for each category.
- [x] Write a public zero-skip summary/JUnit bound to source SHAs, fixed package and contract hashes. Missing real transport inputs or a failed requirement returns nonzero and leaves failure evidence; raw secrets and tokens remain private.
- [x] Normally deliver the public results and accurate Core/CRM/Platform state records. Close C02 only after all component and transport requirements pass; then proceed to the separate C03 ERP design and implementation.

## Verification budget

While implementing, run only the named new test classes and directly affected existing tests. Run one meaningful SQL transaction/migration batch when the corresponding source is ready. The final cross-repository transport run is separate from unit tests. Reuse unchanged signed-package and historical results; do not add default double-agent reviews, repeat full local suites after commits, or run business tests for documentation-only changes.

## Current local evidence (2026-09-10)

Core implementation and its 123 focused unit / 23 real SQL-HTTPS cases passed. Raw attempts remain in the fixture evidence directory. Subsequent actual startup found and fixed GUID map binding, optional update passwords and the exact service-revocation CSRF route, with focused 7/5/15-case regressions. Actual priority backlog progress is now covered by the separate Dapr/Kafka acceptance. The producer is disabled by default and requires MARS disabled when activated. Docker build was completed before the new Actions budget instruction; unchanged results are reused.

The user's later verification policy supersedes all automatic CI steps above: no push/PR may indirectly trigger ordinary Actions; no rerun or paid fallback is authorized. Seven ordinary Core workflows are manual-only on the separate policy branch, whose protected-main integration is blocked by required checks. C02 Core and CRM work remains local until normal integration is possible. No required test, release gate or branch protection is waived. Remote delivery checkboxes therefore remain open. CRM has 43 original focused unit cases, four additional reader-cache cases, passing coverage of 32 distinct SQL/HTTP cases including four new reactive-repair cases, affected auth compatibility, nine browser cases and 25 authorization checks. The local Dapr/Kafka byte probe exposed and verified the producer raw-publish fix; its three final cases passed. Actual SQL-backed Core/CRM acceptance and latency sampling have now passed as described below.

## Local real-transport result (2026-09-10)

Thirteen distinct cases have passed evidence, using scoped reuse across attempts 2/3/4/6; attempt 6 passed its six requested cases. Each denial category has 100/100 real samples within 30 seconds. The fixture evidence locator binds the consumer coverage index and actual source identities. The owner later authorized local publication in place of ordinary required Actions checks. Three Release HTTP/message cases and actual production browser checks passed. Normal source/evidence delivery and remote-main verification are complete through Core PR #101 and CRM PR #60; Platform C01 records were completed by PR #57, without starting Actions. C02 development delivery is closed; C03 remains a separate next task. Production deployment and production performance are untested. No ordinary Actions or new image build was used.
