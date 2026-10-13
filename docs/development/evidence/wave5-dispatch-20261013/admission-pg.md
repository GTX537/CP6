# WP02-02 admission — HOLD

Read-only assessment at Main `a1824fec3d4824d45e27693f2e715b311ee5f665` and Platform `9259a45d39bae7e98ca804a74778a588c1ad4619`. Main HEAD/main/origin/main match; unrelated untracked files remain, requiring an independent worktree for any implementation. Platform worker HEAD is delivered candidate d5ab56de; its main/origin/main match 9259a45d. The JSON companion records 29 source blob IDs and SHA-256 values over exact committed bytes.

**Keep WP02-02 PLANNED and entry not confirmed.** S02-00 and WP02-01 are delivered, but Main still consumes signed immutable Platform 0.10.2, which lacks ctx-binding-v1. WP02-01 explicitly delivered source only. Existing published API cannot provide the new trusted binding. Storage helpers on legacy ctx-v1 could be preparation but cannot honestly complete WP02-02.

Missing artifact: a new immutable, compatible Platform package set containing ctx-binding-v1, pinned to exact producer source and dependency set, with signed/published package hashes, current accepted signature/timestamp/revocation verification and actual feed readback. Main exact references/locks require a reserved adoption window. Do not overwrite 0.10.2, weaken NuGet.config, use machine-specific ProjectReference, copy Platform APIs into Main, or label local unsigned bytes adopted. WP02-27 may prepare local capability and identity validation; it does not authorize publication or remove this gate.

## Confirmed dependency deliveries

- S02-00: source `9e2f1d4f1ed7048c125d350e31a4067745ae3233`, Main delivery `b584563e5ebf323c840866e9c242a710f8c8d6ec`, PR178. Design cp6-pg-tenancy-v1 only; runtime unproven.
- WP02-01: source `d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6`, Platform delivery `9259a45d39bae7e98ca804a74778a588c1ad4619`, PR59. Library and fixture tests; actual host verifier/authority/routing not adopted.
- S01-10: source `a304f9e728775109cfada83312dbeb23b2da4a98`, Main delivery `8e48ee78fd70a2b53ebb39efe7ba7deb351b6840`, PR198. Formal mapping v1.0.0 and twenty scale18 capacities accepted; real Owner adoption/data migration remain separate.

## Current source anchors and implications

- Core csproj:14-15 and WebApi csproj:10 pin 0.10.2; NuGet.config:3 requires signatures and maps Platform packages to GitHub. The package gate is concrete.
- Program.cs:32,165,210,444,3047: one DefaultConnection feeds Core/Dapper; mutable ITenantContext and legacy TenantMiddleware are registered.
- ITenantContext.cs:15-24, TenantScope.cs:15, CP6Context.cs:43,61: default A1 and mutable tenant. New layout must bind immutable verified tenant before connection acquisition; no tracked-context tenant switching.
- TenantMiddleware.cs:16-20 accepts parseable tenant_id and retains default on missing claim. New routes reject missing/invalid scope, untrusted issuer, unknown binding/version and non-Ready storage; health/auth control paths require explicit independent handling.
- DatabaseConnectionFactory.cs:54-60 hard-pins public. New layout requires pg_catalog default, qualified objects and dedicated role pools; do not globally weaken legacy layout checks.
- DatabaseMigrationProfile.cs:45 puts four PG histories in public. New layout must separate shared/tenant versions while retaining Core/Space/ERP/identity-priority lineages.
- CP6Context.cs:622,2571,2585,2637: large shared model, filters and composite unique-index rewrite; no tenant model key. Object inventory must cover every PK/unique/FK and writer, not only messages. BaseEntity.Id remains table-global PK; duplicating all BaseTenantEntity tables across schemas can weaken uniqueness.
- IdentityMessagingContext.cs:10 and ErpIntegrationContext.cs:11-32 retain shared crm_identity_priority/erp_integration and original global MessageId identity.
- DatabaseSharedContext.cs:36-56 and IdentitySnapshotWriter.cs:148 enforce caller connection/transaction. Binding-aware child contexts must retain that physical identity.
- TenantAdminService.cs:123-140,169 and AuthController.cs:79-99,289-317 use cross-tenant directory access and mutable audit/auth scope. Dedicated controlled identities are needed; ordinary runtime roles must not inherit those privileges.
- Program.cs:1002-1032 performs startup Migrate via global connection. New layout must not auto-migrate existing databases.

## Future bounded write window (proposal only)

Reserve Main:tenant-storage, Main:ef-model, three task-card file locks, host/DI wiring, project/dependency files and PG migration lineage. No parallel worker may edit these shared seams.

Existing file allowlist once package gate closes:

- CP6.Core/EFDbContext/CP6Context.cs
- CP6.Core/Persistence/DatabaseConnectionFactory.cs, DatabaseContextOptions.cs, DatabaseSharedContext.cs, DatabaseMigrationProfile.cs, DatabaseDesignTimeConfiguration.cs
- CP6.Core/Services/Common/ITenantContext.cs, TenantScope.cs
- CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs, IdentityMessagingContext.cs
- CP6.Core/Services/ErpIntegration/ErpIntegrationContext.cs
- CP6.Core/Services/Platform/TenantAdminService.cs
- CP6.WebApi/Program.cs, Middleware/TenantMiddleware.cs, Controllers/Sys/AuthController.cs
- CP6.Core/CP6.Core.csproj, CP6.WebApi/CP6.WebApi.csproj, CP6.Persistence.PostgreSql/CP6.Persistence.PostgreSql.csproj and their existing dependency locks: exact approved dependency adoption only.
- CP6.Tests/Persistence/DatabaseSearchPathTests.cs, DatabaseSharedContextTests.cs, DatabaseWiringTests.cs, DatabaseModelCompatibilityTests.cs, DatabaseMigrationOwnershipTests.cs, DatabaseInitializationModeTests.cs; specifically affected existing tenant tests.

Task-owned new paths: CP6.Core/Persistence/Tenancy/, CP6.WebApi/Tenancy/, CP6.Persistence.PostgreSql/Tenancy/, CP6.Tests/Persistence/Tenancy/, eng/postgresql-tenancy/, docs/development/evidence/WP02-02/.

Conditional extension requires worker inventory and COORD's exact additional window before edits: SpaceContext/registration/design factories, specific IAM entities, PG snapshots/history and raw SQL/worker adapters. Unclassified/unadapted routes stay closed. This is not permission to rewrite all domains or migrations. Objects with original global PK/unique domains remain shared unless a separately accepted design preserves them.

Excluded: roadmap/generated progress/four state files (COORD only); accepted S02-00 design/matrix and S01 contracts/formal mappings; historical SQL Server deletion; successor final authorization/lifecycle/CRM/message/Owner delivery; frontend; existing DB migration, environment switch, formal release, signer trust changes, Actions and deployment. Necessary hooks do not prove successor tasks complete.

## Required future validation

All 17 assigned cases remain required: PGT-001,002,003,005,006,007,008,009,010,012,018,019,021,022,023,024,027. Task size M and two short acceptance bullets do not erase restore/capacity/version requirements. Unable-to-execute cases remain NOT_RUN with named blockers.

- Real host verifier/current authority + trusted binding; missing/forged/expired context, unavailable authority, collision, non-Ready binding and unknown/stale version deny before business connection/SQL. Preserve health/auth paths explicitly.
- Dedicated runtime A/B LOGIN and pools: positive A→A reuse, A→B→A routing, forced A-connection-for-B rejection before SQL; identity assertion, rollback/cancellation/constraint/timeout/savepoint cleanup; reject NoResetOnClose/multiplexing.
- Direct ordinary-role SQL: explicit B schema read/write, SET ROLE/SESSION AUTHORIZATION, fake GUC, temp/public shadows, directory/keys, TRUNCATE all denied. Record actual memberships, ownership, ACL/default ACL and secure helper definition.
- Shared FORCE RLS and effective per-action USING/WITH CHECK: legitimate nonzero constant UPDATE with no WHERE/RETURNING or old-column read updates exactly A; independent observer proves B unchanged. TenantId=A relabel attempt cannot absorb B. All-zero/all-denied updates are not a pass.
- Raw/bulk TenantId CHECK/NOT NULL and cross-tenant composite FK tests, original global keys and S01 exact values preserved.
- Actual models A/B/concurrent/design-time: key context/provider/layout/model/schema/designTime, explicit schema SQL, fixed shared domains, no unsafe compiled query reuse. Complete object/writer inventory; tiny synthetic model does not prove host adoption.
- Two independent migrators: database lock, script hash, DDL/history atomicity, crash/recovery, B protection; invalid concurrent-index recovery and shared migration failure gate incompatible routes.
- Consistent full tenant export and isolated staging restore include shared IAM/messages/receipts/audit/assets/version dependencies. Protect B and refuse schema-only/incomplete packages, stale revoke/credentials/epoch/lease, global key collisions and UNKNOWN automatic replay. Never switch existing DBs.
- Real-model bounded caches/pools and candidate N=10/100/1000 measurements, explicit unmeasured range, credential drain; mixed-version compatibility allowlist, incompatible writer rejection and forward-only schema.
- Local checks only. Reuse only exact relevant prior successes, retain real failures/source/package/environment identities. No new SQL Server acceptance obligation.

## Local resource proposal

Reserve 127.0.0.1:55422 and D:/CP6-local-test/WP02-02/. Proposal only: not reserved/provisioned, occupancy not checked. Exclude existing 5432, S01-10/55410 and all business DBs. Fresh task cluster/database needs OID/system_identifier/marker ownership evidence; separate runtime A/B, NOLOGIN owner, migrator and observer/backup identities. Credentials remain private/untracked. Validate absolute containment/reparse points/ownership/port before creation or cleanup; only clean verified owned resources and retain failure evidence.

No tests, builds, DB operations, package generation, network writes or repository edits occurred during admission. All runtime cases remain NOT_RUN.
