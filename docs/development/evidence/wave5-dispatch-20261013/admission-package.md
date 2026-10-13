# WP02-27 admission — local implementation can be READY

Platform baseline `9259a45d39bae7e98ca804a74778a588c1ad4619`; clean task worktree `D:/CP6.Platform-worktrees/wp02-27-20261013`, branch `codex/wp02-27-package-capabilities-20261013`. Direct prerequisites S01-09 (remote main dc4c0ab2, source 753696c8, PR58) and WP02-01 (remote main 9259a45d, source d5ab56de, PR59) are delivered.

**COORD may confirm READY for the task card's explicit local validation scope.** The card requires versioned capability/compatibility identity, immutable package/manifest validation and current pinned trust/revocation checks; it expressly separates formal publication authorization. These can be implemented and locally validated without publishing new packages, occupying a formal version, executing Actions or changing trust. This does not make WP02-02 ready, publish ctx-binding-v1, or prove Main/CRM adoption.

Worker must first read Main `docs/devops/README.md`, `docs/client/r2/README.md`, the complete WP02-27 task card and `docs/design-handbook/modules/PLT-RELEASE-01.md` plus its exact accepted source; Platform AGENTS and `docs/P10-RELEASE-GOVERNANCE.md`. COORD has read the Main DevOps entries. Preserve Build once/deploy many and the existing formal trust/release authority; no release execution is delegated here.

## Current code and usable local evidence

- `src/CP6.Platform.Release/Cp6FormalPackagePublicationValidator.cs:19-59`: canonical formal record; exact stable 0.10.0/.1/.2 allowlist; source, workflow, toolchain, pinned trust, seven-package and verification identity checks. It validates claims/shape, not actual nupkg signatures or presence on a feed.
- `tools/CP6.Platform.ReleaseTool/FormalPackageVerifier.cs:18-96`: reads local archive, exact identity/source, author primary signature, one RFC3161 timestamp, pinned signer, then returns actual SHA-256. `:99-131` requires online revocation and denies unknown status. Keep these rules; a read-only revocation request is local verification, not permission to use Actions. Unavailable revocation stays a failure/unknown, never an offline success downgrade.
- `Cp6PinnedNuGetTrustPolicy.cs:106-162`: Current/Historical/Revoked decisions are distinct. Historical signing validity never becomes current trust. The real root remains `1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d`, pinned self-signed, internally trusted, not public CA trusted.
- ReleaseTool Program.cs:35-69 already exposes validate-nuget-trust, validate-formal-publication and verify-formal-package over local files. No new feed push path is needed.
- Release csproj includes release/v1 assets; ReleaseSchemaAssetTests.cs and P10PackageTests.cs guard registration, closed schemas, exact formal versions and packed-schema semantics. New capability identity must not widen the formal publication v1 version allowlist.
- `eng/p10/Pack-P10TestPackages.ps1` packages exactly seven packages under artifacts/p10-test and only accepts its historical test-version syntax. All p10 scripts remain read-only; do not repurpose a test package manifest as formal publication.
- `docs/evidence/c01/0.10.2/formal-package-publication.v1.json` binds source `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`. All seven actual 0.10.2 nupkg files are present under the user's existing NuGet cache and their read-only SHA-256 checks match the immutable record (7/7). Their signatures were not reverified during admission. JSON companion retains hashes and paths; paths are machine-local evidence, not committed project references.
- Source 9259a45d has S01-09 exact native profile/raw-byte routing and WP02-01 ctx-binding-v1. Published 0.10.2 predates both; capability rows must distinguish source available, artifact present, currently trusted and host adopted. Do not mark source capabilities as present in 0.10.2 by a version-number assumption.

## Bounded implementation design

Add a separately versioned, closed capability/compatibility manifest and local validator that composes existing immutable package/trust validators. Pin producer repository/full SHA, package ID/exact version/hash and evidence; distinguish formal published identity from local candidate identity. A local candidate may use a unique explicit test prerelease containing the source hash/attempt and be fully hashable, but remains NOT_PUBLISHED/NOT_FORMALLY_TRUSTED and cannot satisfy Main's production package references.

Compatibility rows identify consumer/producer, package/feed, runtime/SDK/OS/architecture, schema/validator, exact message profiles, DB provider/driver/migration, host/config, decision/evidence/blocked reason. Unknown and unmeasured combinations fail closed rather than inheriting major-version compatibility. Capabilities must list the exact S01-09 profile set and ctx-binding-v1 scope without claiming actual host verifier, DB routing, Owner final gate, Inbox v2 or other undelivered capability.

Output a deterministic consumer identity/lock document suitable for later exact pinning, plus explicit state per step: SourceFrozen, ContractShapeValid, ArtifactBytesVerified, TrustCurrentlyAccepted and later consumer/host/runtime/business states. New-source candidate rows must accurately retain missing formal package evidence. This meets the card's identity/validation work; publication and consumer package adoption remain subsequent authorized work.

Do not modify old formal manifest fields or silently convert source/test results into formal workflow identities. Existing seven-package stable records, source hashes, trust root, certificate and writer version gates remain byte-preserved.

## Exact write allowlist and locks

Reserve Platform:package-trust, Release library, task capability contract namespace, Release test/fixture namespace and the listed Release project file exclusively. The same task owns registration and packaging of its new contract so parallel workers cannot create rival definitions.

New task-owned paths:

- `src/CP6.Platform.Release/CapabilitiesV1/`
- `contracts/release/capabilities/v1/` (schema, registry/manifest examples and fixtures; distinct from immutable formal publication v1)
- `tests/CP6.Platform.ReleaseTests/CapabilitiesV1/`
- `eng/wp02-27/` (local inspect/build/pack/verify runner and evidence helpers only; no publish or signing-root change)
- `docs/WP02-27-PACKAGE-CAPABILITIES.md`
- `docs/development/evidence/WP02-27/`

Existing files allowed for narrow integration:

- `src/CP6.Platform.Release/CP6.Platform.Release.csproj`: include new capability assets; no formal stable version occupation or dependency change.
- `src/CP6.Platform.Release/Cp6FormalPackagePublicationValidator.cs`: additive compatible capability-validation entry only if necessary; preserve all current formal publication/version/trust semantics and existing entry behavior. Prefer composition in CapabilitiesV1.

Existing CLI Program.cs, shared ReleaseContractIds/MediaTypes, tools/test project files and P10 test harness/collection are **not** included merely for convenience. Invoke current CLI from the task-local runner and test new API under the task-owned namespace. Additional shared file edits require a precise extra COORD window before modification.

New capability contract does not need to be inserted into old primary candidate/media-type grammar; future promotion is separately scoped. The Release project already compiles new .cs files by convention; no global project or package dependency update is needed to add the new namespace.

Read-only exclusions: `eng/p10/**` including trust/certificates and all pack/sign/publish scripts; `.github/workflows/**`; `NuGet.config`; `tools/CP6.Platform.ReleaseTool/**`; `src/CP6.Platform.Release/Cp6ReleaseContractIds.cs`, `Cp6ReleaseMediaTypes.cs`; existing Release test harness/collection/project files; `Directory.Build.props`, `Directory.Packages.props`, solution-wide package/version configuration; original `contracts/release/v1/**` and historical fixtures/evidence; Platform ctx/native contract implementations; Main/CRM projects/configuration; signing secrets/certificate stores/global NuGet config; Main roadmap/generated progress/project-state files. No database, environment, Docker, deployment or repository-wide version change.

If the implementation needs another shared file or contract amendment, send QUESTION with actual source/diff reasoning before editing. Do not silently extend the write list to the whole Release package or p10 pipeline.

## Local acceptance plan

1. Targeted capability validator/local-runner/schema parity tests with deterministic raw-byte/hash output. Exercise exact known capability rows and preserve unmeasured states; reject unknown capability/profile/version/compatibility, malformed/duplicate JSON and input-controlled identity shortcuts.
2. Immutable negatives: mutable tag/latest as identity, same ID+version with different hash, changed source, mixed package versions/set, changed manifest bytes and wrong feed identity. Keep original artifacts and report each failure; no overwrites.
3. Trust negatives: revoked/noncurrent/unknown root, stale policy, wrong signer, missing/bad timestamp, altered package/archive. Use task-owned fixture copies for revoked-policy tests; never edit production trust assets. Historical evidence remains historical and does not satisfy Current.
4. Existing signed 0.10.2 archive path: rehash and locally verify all seven matching bytes with the unchanged current trust/publication validator and actual package verifier. Record evaluation time, policy/cert hashes and actual results. If online revocation cannot be checked, report the real unresolved result and do not claim current trust success.
5. New source candidate path: isolated local build/pack of actual affected artifacts and inspect packaged schema/API/capability contents. Exact prerelease/source/assembly/package hashes, no formal source/run/feed claim. A local candidate proving ctx-binding-v1 presence must still report formal trust/publication/adoption absent. A test signing identity is never the formal trust root.
6. Run affected Release tests and necessary architecture/package boundary checks once for the task; reuse exact unchanged S01-09/WP02-01 library evidence. New capability asset must appear in packed Release output; source-schema-only tests are insufficient. Do not invoke full recursive P10 package set build merely because its helper exists.
7. Task-level complete diff review, local evidence and remote source merge verification per repository rules. Before every push/PR, check both candidate and protected-branch workflow triggers; no Actions dispatch or automatic CI reenable. State actual tests/cancelled runs/trigger changes, with unrelated runtime cases NOT_RUN.

If formal signature verification is unavailable, implementation can progress but the relevant local task gate cannot be marked passed. This is an execution failure to resolve or report, not an admission reason to substitute a fake signer. All seven existing immutable archives are already available; no publication is needed for the positive test route.


## Concrete local command plan

Commands execute only in the assigned worktree after implementation. No commands were executed during admission. Preserve actual selected source SHA and all output hashes; these are local results, not formal run identities.

- `dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj -c Release --filter "FullyQualifiedName~CapabilitiesV1|FullyQualifiedName~FormalPackagePublicationTests|FullyQualifiedName~PinnedNuGetTrustPolicyTests|FullyQualifiedName~FormalPackageVerifierTests" --logger "trx;LogFileName=wp02-27-release.trx" --results-directory artifacts/wp02-27/test-results`
- `dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj -c Release --logger "trx;LogFileName=wp02-27-architecture.trx" --results-directory artifacts/wp02-27/test-results` (the task crosses new contract/package boundaries; reuse if exact affected inputs already checked).
- Task runner under `eng/wp02-27/` records current immutable 0.10.2 input identities then invokes existing `dotnet run --project tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj -c Release --no-build -- verify-formal-package <archive> <policy> <certificate-directory> <exact-id> 0.10.2 fbcd21528078a04e5b53c42c5fdfebe6ffa9655f <actual-canonical-UTC> Current` for each of seven local archives. Ensure the existing tool is built once by the affected project build; never relax revocation/timestamp rules to make it pass.
- Pack the changed Release project locally using `dotnet pack src/CP6.Platform.Release/CP6.Platform.Release.csproj -c Release --no-restore -p:PackageVersion=<unique-task-test-prerelease> -p:RepositoryCommit=<actual-full-SHA> -p:ContinuousIntegrationBuild=true --output artifacts/wp02-27/packages`; inspect exact packed capability schema/manifest and record byte hash. Other actual candidate artifacts only when necessary to prove asserted source capabilities, all task-local prereleases, no feed push/signing-root change. A version string alone proves nothing about included bytes.
- `git diff --check` and a single task-level full diff against the latest confirmed main; preserve targeted regression after substantive fixes.

Use unique artifact subdirectories per run, preserve original failure logs, and avoid P10 helper auto-generation of fake workflow run IDs. Actual new test counts and current-trust results are unknown until execution.

## Explicit outcome boundary

WP02-27 DONE would mean a delivered local validation/identity capability and accurate compatibility matrix. It would not mean a new formal package was published, signing trust changed, Main/CRM consumed it, tenant routes enabled, production candidate accepted or deployment authorized. WP02-02 package blocker remains until its exact immutable trusted consumer artifact is independently available and adopted.

Admission performed source reads and artifact hash comparison only. No build/test/signature verification, network writes, package creation, Actions, database or repository mutations occurred. The JSON companion holds 15 source anchors and seven cache/archive identity checks.
