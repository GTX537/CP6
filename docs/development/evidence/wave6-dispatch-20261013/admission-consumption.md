# WP02-02 trusted Platform consumption — wave 6 dispatch preparation

Assessment only, 2026-10-13. Main `9a0155dec1c3366df8a6631f3d8fdb086aa32fc8`; Platform `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`. Local main/origin-main refs matched these pins. No new remote readback was performed by this assessment. Companion JSON records exact committed source fingerprints, current dependency closure, and ledger state.

## Decision

**Prefer formal-package preparation, with WP02-02 remaining BLOCKED and its next assignment explicitly PREPARATION.** COORD can dispatch concrete, reversible preparation now without asking for package publication, signing secrets, Actions, database operations, or production deployment. Do not introduce a 376th task or another DONE item; WP02-27's delivered local validator is not a second package-delivery milestone.

The next useful output is a Main consumption admission harness plus a precise producer registration/preparation proposal, not a statement that the package gate is already closed. Only the Main task-private preparation below is recommended for the immediate wave 6 assignment. Any producer implementation follows in a separate, explicitly granted Platform window. The Main production package references, trust configuration and tenant-storage wiring remain unchanged until adoption admission passes.

## Observed facts

- `D:/CP6/docs/development/roadmap.json`, WP02-02: state BLOCKED, entry_gate NOT_CHECKED, assignment null. Its blocker explicitly permits either a trusted immutable formal package or a **separately reviewed and approved reproducible source-adoption plan**. Direct task prerequisites remain S02-00 and WP02-01; do not invent a circular dependency on full WP02-28, which also depends on WP02-25.
- `D:/CP6/docs/design-handbook/modules/PLT-RELEASE-01.md:54` and Required-REL-01..07 at line 73 require actual feed packages and no ProjectReference/source fallback for formal consumer qualification. The source alternative in the blocker is therefore an unapproved alternative, not standing permission to bypass this accepted requirement.
- `D:/CP6/NuGet.config:3` requires signatures. Platform source mapping points to `https://nuget.pkg.github.com/GTX537/index.json`; pinned signer is `1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d`. Do not change it or global NuGet/certificate settings.
- `D:/CP6/CP6.Core/CP6.Core.csproj:14` directly pins Messaging and EntityFramework to `[0.10.2]`; `D:/CP6/CP6.WebApi/CP6.WebApi.csproj:10` directly pins AspNetCore to `[0.10.2]`. All target net8.0. Abstractions and Contracts enter transitively. The actual WebApi closure is five Platform packages, not all seven producer packages.
- `D:/CP6/CP6.Persistence.PostgreSql/CP6.Persistence.PostgreSql.csproj` references Core and Space.Infrastructure; it has no direct Platform reference. `CP6.Tests` references WebApi/Core/Entity and Space projects. Root `Directory.Build.props` enables lock files on Core/WebApi/Tests/Space.Infrastructure/Space.IntegrationTests/Oidc.IntegrationTests; Persistence.PostgreSql enables its own lock file.
- Core uses EF SQL Server 8.0.30, Npgsql EF `[8.0.11]`, Npgsql `[8.0.8]`. PostgreSQL alone is the new task's runtime target; existing SQL Server history is not deleted and no new SQL Server acceptance is added.
- Additional independent direct references matter when drawing the adoption window: `tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj` directly pins EntityFramework `[0.10.2]`; `tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj` directly pins Release `[0.10.1]`. Do not silently upgrade the historical P10 tool as part of Main runtime adoption. Main has more downstream lock files than the four originally named in admission-pg; companion JSON enumerates tracked impacted locks.
- Formal seven-package `0.10.2` producer source is `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`. It lacks ctx-binding-v1 and S01-09. Old-package Current verification at `2026-10-13T12:46:51.582Z` only applies to those old bytes and that evaluation time; it cannot confer new capabilities or perpetual trust.
- WP02-27 final local source is `7c59256e13c217fc0e8a66b8f3f46905222fdcbc`; version is `0.10.3-wp02.27.g7c59256e13c217fc0e8a66b8f3f46905222fdcbc.20261013134250`. Its exact matrix/lock are under Platform `docs/development/evidence/WP02-27/runs/coord-schema-local-pack/inspection/`. Source/shape/archive bytes are Verified; compatibility and all consumer/host/runtime/business layers are Unknown; publication/trust remain NOT_PUBLISHED / NOT_FORMALLY_TRUSTED.
- That local pack does not include later WP02-07/WP02-11 integrated changes. WP02-07 remote-main evidence is `817a2926b0799f70cc87f384967e0883c1943ec5`, source `872acec044eb09bc96660f696844c86127b508bd`; WP02-27 integrated at `51706965ec1e2363a8f4db8c730983b1313fdbfd`, candidate `c105036f166928b7c165f182f99f3147ffbb0d58`; WP02-11 integrated at `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`, candidate `34cf7ab144a02e412de121eee2d9c5083f5a96a8`. Main wave5 integration evidence supplies these identities. Do not relabel the old pack as the latest combined main.

## Comparison of routes

| Route | Can prepare now | What it proves | Gate before real Main adoption |
| --- | --- | --- | --- |
| Reproducible source adoption | A task-local source experiment pinned to full SHA/tree, clean inputs, SDK/dependency locks and output hashes; proposed build/restore policy, path-independent harness and threat/rollback analysis | Local compile/runtime behavior of those source bytes; no formal package trust | Explicit accepted revision/exception reconciled with PLT-RELEASE Required-REL feed/no-source-fallback rule; exact source trust/provenance and complete dependency identity; independent review, tests and COORD adoption window. Merely adding an external ProjectReference is inadequate. |
| Formal-package preparation — recommended | Complete next-version registration patch, offline negative/parity tests, new local pack at frozen source, exact Main dependency and consumer checks, dry-run publication runbook | Readiness for separately authorized formal release and bounded consumer implementation | Actual protected formal publication with new authorization, current trust verification, byte-preserving feed readback, new immutable seven-package identity, and independent Main qualification. |

The source route is technically feasible as a separately controlled development lane, but treating it as formal Main adoption changes an accepted qualification boundary. It also requires a reproducible source acquisition/verification/build chain and eliminating mixed old-package/new-source type identities. This is greater policy and engineering work than preserving the current package contract. It should not be chosen merely to avoid the release gate.

No user decision is mandatory for the recommended **preparation**. Actual Actions execution/formal publication/signing needs new explicit user authorization; old 0.10.2 publication approval is not reusable. If source adoption is requested instead, resolve the concrete accepted design exception before production project changes; the ledger's alternative alone does not grant it. COORD's precise task ownership, shared-file windows and source freeze are ordinary coordination decisions, not reasons to stop and ask the user now.

## Exact bounded next dispatch

### A. Main WP02-02 PREPARATION: consumer admission and dependency closure

Create an independent branch/worktree from the then-confirmed latest Main main. Root Main has unrelated untracked inputs; do not clean or stage them. Authorize new task-owned paths only:

- `eng/wp02-02/consumption/` — source/dependency inventory, input identity schema, fail-closed admission checker, task-local test harness and fixtures; no network writer or invocation of signing/publication helpers.
- `docs/development/evidence/WP02-02/consumption-preparation/` — inputs, results, exact proposed producer version/source/capability requirements, dependency-edit proposal, and delivery report.

Reserve `Main:host-package-adoption` for this scoped preparation namespace and record any overlap with WP02-28. No reservation of Main:ef-model/tenant-storage is needed while they remain read-only. A task-local project file is allowed under the first path; no root solution/props/lock changes. A helper needing the current Release API must run against the fixed Platform source/tool from an isolated temporary build or direct archive inspection; it may not add an untrusted package source to Main's production restore path.

Deliver executable checks that:

1. Inventory exact Main csproj/lock closure and the producer's seven archives/source/SDK/capabilities/packaged schemas. Pin inputs independently; never auto-approve a matrix by copying its input hash.
2. Reject 0.10.2 as missing ctx-binding-v1; reject the existing wp02.27 local candidate for formal admission; reject source/hash/feed/mixed-version/schema mismatches and unknown compatibility. Do not manufacture a successful formal input. Historical valid bytes can exercise identity/trust checks only with their genuine capability absence.
3. Inspect full public signatures needed by Main and compile a task-local contract probe when possible. Exact seam: Contracts `Context/ContextContractsV1.cs`; Abstractions `Context/ContextBoundariesV1.cs`, `ContextBindingValidatorV1.cs`, `TrustedContextExecutorV1.cs`, `ContextScopeAccessorV1.cs`; AspNetCore `Context/TrustedRequestContextExtensionsV1.cs` and `TrustedRequestContextMiddlewareV1.cs`; Messaging `Context/MessageContextExecutorV1.cs`. Host must implement `IContextSourceVerifierV1<HttpContext>` and `IContextBindingAuthorityV1`; only the successful executor creates `TrustedRequestContextV1`. Test fakes must be labeled test fixtures, not actual authority/host adoption.
4. Produce a deterministic preparation manifest with publication/trust/consumer/host/runtime/business states separate. Missing real publication yields BLOCKED/NOT_RUN. Keep actual feature tests and compilation separate from readiness state.
5. Prepare exact dependency edits as a proposal/patch, not applied production changes. Enumerate every impacted lock, including Space/OIDC and database probes; exclude unrelated ReleaseVerifier 0.10.1 unless independently justified and explicitly windowed.

Do not modify Core/WebApi/Space/Persistence source, Program.cs, middleware/DI, root NuGet.config, production csproj/locks, EF models, migrations, DB configuration, shared accepted contracts, roadmap or project-state files. Do not create or connect to a database. The original 17 PGT cases remain future WP02-02 requirements.

### B. Future producer preparation window proposal: outside this Main assignment

This is a separate **Platform repository prepare-only window supporting WP02-02**, not completion of the Main task or a second DONE for WP02-27. COORD must assign one owner and record the cross-repository association. Branch from current verified Platform main (assessment pin `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`), serialize against Platform:package-trust and future Platform public-contract editors. All seven packages come from one final frozen source; do not combine prebuilt 7c59256 packages with newer DLLs.

The bounded output can be implemented before publication permission: prepare the next exact formal version registration, proposed `0.10.3` only subject to read-only feed absence verification and final review, plus a complete no-execution runbook. Do not reserve/occupy a version by publishing. Current writer pipeline accepts exactly 0.10.2 and read-only validators exactly 0.10.0/.1/.2; therefore a version flag alone cannot release new capabilities.

Requested write window must explicitly cover the following coordinated surfaces if implementation proceeds:

- `src/CP6.Platform.Release/Cp6FormalPackagePublicationValidator.cs` and `contracts/release/v1/formal-package-publication.v1.schema.json`: preserve historical valid identities and bind exact new source/version/seven-package/feed semantics.
- `src/CP6.Platform.Release/CapabilitiesV1/Cp6CapabilityValidator.cs` and `contracts/release/capabilities/v1/capability-matrix.v1.schema.json`: register the new formal capability identity; current code rejects all formal capabilities and all formal versions after .2. Keep task-local identities permanently untrusted and old formal versions capability-empty.
- `.github/workflows/p10-formal-packages.yml` and `eng/p10/{Test-P10FormalPrerequisites,Pack-P10FormalPackages,New-P10FormalPackageSet,Publish-P10FormalPackageSet,New-P10FormalPublicationRecord,Test-P10FormalPackageSet}.ps1` as required by exact inspection. Preserve workflow_dispatch, protected approval, one build, signing/timestamp, immutable version-absence marker, byte-preserving readback and independent verification. Never execute writer/signing entry points in this preparation.
- Corresponding precise Release/schema/package script tests and producer version identity docs; any required root version/project changes need exact additional file enumeration before edits. No blanket eng/p10 write grant. Trust policy/certificate/history remain byte-preserved.

Use additive next-version semantics and fully reviewed parity; do not merely widen an allowlist. A later candidate's full source SHA must be the final integrated registration source, not the starting e5c63 pin. Local rehearsal remains a distinct unique prerelease/run namespace, not an actual formal package or feed identity. Reuse the WP02-27 inspector within its truthful catalog scope; it only knows S01-09 and ctx-binding-v1 and checks type/method names. Inclusion of InboxV1/HttpV2 code in latest source does not authorize invented matrix capabilities or host acceptance. Native profile schemas missing from the package are a real gap if those profiles are requested; WP02-02 can scope its request to ctx-binding and leave unrelated native qualification blocked.

## Validation and adoption boundary

Preparation: run targeted admission/parser/closure tests, positive identity fixtures and all real failure paths; verify no production project/config/state changes; inspect packed schema bytes and exact API signatures/compile surface. For producer registration, run affected Release semantic/schema/packed-schema tests, formal script version/one-build/immutability checks, current trust negative coverage, and Architecture once for the complete task. Review the full task diff once; repeat only affected checks after fixes. Reuse wave5 combined Architecture 98/98 only while relevant source/inputs are unchanged, never relabel it a new run.

Do not run every original runtime gate merely for documentation or inventory. Conversely do not claim the producer's library fixtures qualify actual Main authority, driver or storage. Retain all failures and NOT_RUN cases. The assessment itself ran no tests/builds.

Before Main production dependency edits or WP02-02 READY:

1. Final registered producer source and immutable package identity have independent review; formal run/publication are explicitly authorized and executed through retained protected authority. Ordinary Actions need explicit scope/minute authorization. No local PFX access or synthetic workflow/run identity.
2. Actual seven signed package hashes, publication/provenance, feed readback and current signer/RFC3161/revocation acceptance match; unavailable status stays Unknown. Exact capability and measured compatibility satisfy the Main request. Windows/Linux requirements and protected source requirements remain as P10 specifies; local-only evidence does not silently waive formal release prerequisites.
3. COORD grants exact Main csproj/lock/host/tenant-storage/EF/migration windows based on the final dependency closure. Main restore uses exact signed feed packages with existing trusted signer/source mapping; no source fallback. Build actual affected Core/WebApi/Persistence/tests and verify loaded assembly/deps/lock identities. Package signing is not host authority or DB isolation proof.
4. Revalidate Main wave5 admission-pg and all assigned PGT-001/002/003/005/006/007/008/009/010/012/018/019/021/022/023/024/027. Retain real host verifier/current authority, dedicated A/B roles/pools, shared RLS, global keys, EF models, concurrent migrations/recovery/restore/capacity tests. New isolated PG resource window must be separately reserved and ownership checked. Existing business databases remain excluded.

COORD alone updates roadmap/generated progress/four project state files during preparation/delivery windows. A successful preparation may be merged and recorded as preparation while WP02-02 remains BLOCKED; it cannot become DONE or advance runtime acceptance totals.

## Assessment limits

Read-only repository inspection plus these two scratch report files only. No repository/Git mutation, network write, package generation, tests/builds, DB access, Actions, signing, publication or deployment. No active run was created by this assessment; no cancellation or trigger changes were performed. Local refs and committed wave5 evidence were checked; fresh remote feed availability, final release prerequisites and runtime behavior remain unverified.
