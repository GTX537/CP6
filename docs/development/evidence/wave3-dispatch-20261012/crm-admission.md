# S01-08 CRM INQ/LEAD admission assessment — 2026-10-12

Read-only assessment for coordinator, next worker D (previous WP14-01). No repository files changed, no tests/builds/network/Git mutations performed. Only this report is written. Commands inspected committed CRM `origin/main` blobs, not the root checkout's unrelated branch.

## Decision

The contract implementation task can be dispatched after COORD records the exact bounded write window and source adoption below. S01-02 is available on delivered Main; S01-09's unpublished Platform package is **not** an S01-08 prerequisite. Local contract implementation and local testing need no new database or Platform publish. Delivery through CRM PR/main currently has an Actions blocker and must remain separately gated.

A task-plan wording correction must be carried in dispatch: native INQ has **38 schema definitions**, not 55 definitions. The number **55** is the count of repaired `#/$defs/` reference occurrences. Exact original OpenAPI has 257 `$ref` occurrences (55 `#/$defs/`, 202 `#/components/schemas/`); composed OpenAPI has 257 `#/components/schemas/` references, all resolving to the same 38 definitions. The pinned TS definition map and C# embedded `$defs` also each contain 38 definitions. This follows `docs/design-handbook/remediation/06-wire-types-and-fixtures.md:54` and the source-pinned core-types patch's `expected_count: 55`; do not invent 17 definitions or weaken coverage. Recommended card: “adopt all 38 native definitions, verify all 55 repaired reference occurrences and all 257 resolved references.”

## Confirmed baselines and prerequisites

- Main read baseline: `d7a210256783e45291a5e724ea068ed4fc165ecc`, HEAD/main/origin/main equal at initial read. Main root has unrelated untracked inputs; leave untouched.
- CRM latest fetched main/origin/main, confirmed by COORD: `c778a3052a4416b82facb07b1244fc98fb6ad8a1`.
- CRM root HEAD: `4e62c9e9c04d1ab5c01b45e9b0047dc5e9cc70ad`, clean on `codex/p10-s03-crm-test-consumer-plan`. It is not the implementation baseline. Create an independent CRM worktree/branch from the freshly confirmed main; do not switch this root branch.
- Main S01-02 implementation commit: `2c0ef2129012a8117b0b5b058ac3c470e12d73fd`, source bytes match delivered Main above. Upstream evidence records 77 passed / 0 failed / 0 skipped, including 21 package vectors. This is upstream evidence only; CRM must actually compile and test adoption.
- Use `CP6-S01-CONTRACT-INPUTS/1.0.0`. At Main baseline, git-blob SHA-256 of `eng/contracts/s01/profile-inputs.json` is `2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e`; `vectors.json` is `b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01`.
- CRM has no root AGENTS.md at committed main. `apps/web/AGENTS.md` requires reading relevant bundled Next docs before writing Next-specific code. Pure TS contract modules/tests do not require a new UI design or framework upgrade.
- CRM Directory.Build.props fixes net8.0, C#12, nullable, warnings as errors. Existing test SDK/xunit versions are 17.14.1 / 2.9.3 / runner 3.1.4. Existing Platform packages remain `[0.10.2]`; S01-08 does not adopt unpublished S01-09 behavior or change package pins. Existing historical SQL Server consumers remain historical; no SQL Server acceptance or PG migration belongs to this task.

## Current source and why the window must be explicit

CRM current `contracts/api/crm-v1.openapi.yaml` is “CP6 CRM first business slice” 1.0.0, with 44 schemas and 26 paths. It is not the native INQ R3 V15 OpenAPI. `scripts/generate-crm-api.py` deliberately handles only a small first-slice schema subset and emits `CrmApi.g.cs` and `apps/web/src/generated/crm-api.ts`; neither file may be manually edited while pretending its generator remains authoritative. Replacing that first-slice schema with the native INQ schema would change existing business APIs and exceed S01-08.

`CrmContractInputFilter` validates reflected nullable DTO members in namespace `CP6.CRM.Application.Contracts` after minimal API binding. By then duplicate wire keys cannot be recovered from the DTO. Its existing placement in `CrmEndpoints.cs` cannot establish native raw JSON validation by itself. Native decoder entrypoints must receive original UTF-8/text and run strict checking and schema validation before typed conversion. If current HTTP routes are altered, explicitly identify their original/native version and verify compatibility before editing; broad replacement of endpoint DTOs is not authorized by this contract task.

`apps/web/src/leads/types.ts` re-exports first-slice DTOs and maintains controlled form state. `api.ts` and `PublicInquiry.tsx` use `response.json()` casts to those first-slice DTOs. `CrmBusinessService.Intake.cs` performs actual intake/lead/audit state changes. These are inspected adoption boundaries, not permission to implement S03 or redesign first-slice business flows.

Preferred bounded implementation: compile the pinned native INQ and CP21 LEAD contracts/decoders under distinct native namespaces/modules in the actual CRM Application/web projects, publish explicit versioned contract entrypoints, and prove those entrypoints with real CRM tests. Retain first-slice generated behavior and record exact remaining HTTP/business consumers. Tests must invoke production decoder code, not an isolated test-only reimplementation. Contract adoption is allowed; H01, download/ETag/session/expiry and Owner effects remain future actual-interface acceptance.

## Exact source package and portability

All six original/composed pairs below were present locally and SHA-256 matched. The original LEAD query schema was also present and matched. Input resolution is through Main's `docs/development/s01-input-index.json` and the source selectors in the package; machine-specific archive locations must not become CRM build/test/runtime paths.

| Source | Selected variant / SHA-256 | Full patch set |
| --- | --- | --- |
| composed-18 `CrmLeadCp20Contracts.cs` | composed `45ad1fa71237bbadf5e4ced495ea3a35ff625ee385270b6566057f9782e1eab7` | core-types bounded patches, 1 operation |
| composed-20 INQ OpenAPI | composed `740b840c697b60ebf51ceb0bd9a1898238814a49e36c2afe1d819c473b7df349` | core-types bounded patches, 1 operation (55 refs) |
| composed-21 INQ TypeScript | composed `acd6c5a6a2f6660b52ca978077e130e86f2d920f0d6bbf9d7937d081e48b953b` | core-types bounded patches, 3 operations |
| composed-22 INQ C# | composed `59c94a5a99eb2383a5e0e0bfaa957ee6ad9dfaeff5d2ff0bb449e0e07a22d850` | core-types bounded patches, 3 operations |
| composed-23 UI positive cases | composed `b5cd3840d288f191ed213e348ae2fed4d01f26c2c28812ab92d3e4ed323b989b` | core-types bounded patches, 1 operation |
| composed-26 full INQ fixture file | composed `4087f29215dd4de47dedc4b2e7d9e34eb0dcd63a645bae8f032364b2cc8a3ac8` | fixtures bounded patches, 16 operations |
| lead-query-schema CP20-qualification-h02 | original `2428190864cfd501096d4816a4fa6dfdb4473cdea9ebb8dc2e8b27691df291e8` | none |

The original filenames with CP20 have actual C# namespace `CP6.CRM.CP21.Contracts` and schema identity `urn:cp6:crm-lead:cp21:contracts:1`. `LeadQueryFilters.ownerId` is **required** and allows canonical lowercase UUID / `unassigned` / null; absent and arbitrary string reject. `ContactCreateInput.ownerId` is independently optional. Do not impose requiredness from one field on every ownerId field or globally rewrite existing CRM QueueQuery ownership semantics.

Adopt Main Wire by source pin, not an absolute or sibling-worktree ProjectReference. Recommended: check in the three exact upstream files as pinned input resources and reproducibly emit CRM-local copies with at most a recorded namespace substitution; keep output and upstream hashes distinct. Namespaces can be `CP6.CRM.Application.Contracts.Wire` while native INQ stays `CP6.CRM.Contracts.InquiryRecoveryR3V15`. Native C# asset calls `Cp6.DesignRevision.JsonSemanticEquality`; importer must explicitly map that helper to the delivered local implementation, preserving the transform and output hash.

| Main source at implementation commit and delivered Main | git-blob SHA-256 |
| --- | --- |
| `CP6.Entity/Contracts/Wire/StrictJson.cs` | `9a2a22e31c32f24235ad4b2ac8cb93f625ab1c75e9e358251b9dd54e001ab80d` |
| `CP6.Entity/Contracts/Wire/JsonSemanticEquality.cs` | `61afae5efd22fbff880d9a5c17f9c5ec465381533fd880e692f9ba648b469eee` |
| `CP6.Entity/Contracts/Wire/NullOnly.cs` | `3fa80c80928c4ce04b904a98b1bb668445412732c635eb77b0d01e050fcada4b` |

Suggested importer `scripts/import-s01-contracts.py` reads only approved source/schema/type/test data and fails on source hash or transform-count mismatch. It must not execute archived programs. Check in exact package JSON/pins plus narrow portable contract resources under `contracts/s01/`. For the 11 MB full fixture source, either check in its exact selected variant or deterministically extract the contract-relevant entries with original whole-file hash, source JSON pointer/case ID, extracted-byte hash and extraction procedure. Tests consume the checked-in extracted resources, not archive paths. Fixture success means only the asserted contract subcase, never full business fixture execution.

The composed C# decoder still uses local recursive duplicate scanning, MaxDepth=64 and decimal/Int64 conversions for numeric validation. The composed TS decoder checks duplicate keys then uses JSON.parse/Number and canonical JSON string comparison. These are source assets to adapt deliberately, not pre-certified runtime implementations. Dispatch must require all assigned equality cases to exercise the actual adopted boundary, including huge exponent and 38-digit numbers without floating-point corruption or arbitrary narrowing. Preserve original bytes/text separately from schema equality keys. No digest, signing or replay behavior may use structural normalization.

## Assigned profiles, every vector and added tests

Exactly **5 profiles / 30 vectors** target CRM:S01-08. Preserve original IDs/expectations and fail the test if an assigned operation/profile is unsupported; do not silently skip. Keep Main package NOT_RUN untouched and write separate CRM results.

| Profile | IDs | Required outcomes |
| --- | --- | --- |
| strict-json | JSON-001..JSON-007 | 001/007 accept; 002..006 reject duplicate properties at root/nested/array/escaped equivalent names; case-sensitive distinct names remain legal |
| null-only | NULL-001..NULL-007 | only 002 (explicit required null) accepts; absent, number, boolean, string, object reject |
| json-semantic-equality | EQUAL-001..EQUAL-007 | 001 true (key order, 1 vs 1.0); 002 false (array order); 003 false (string vs number); 004 true (-0.00 vs 0e1000); 005 true (38-digit scientific equivalent); 006 true (1e1000 vs 10e999); 007 false (delimiter-shaped object names) |
| crm-lead-cp20 | LEAD-OWNER-001..LEAD-OWNER-005 | null/unassigned/canonical UUID accept; arbitrary owner/absent reject; retain FIELD_SUBASSERTION_ONLY |
| crm-inq-r3-v15 | INQ-VERSION-001..INQ-VERSION-004 | exact OpenAPI/title/version/root/hash accepts; wrong version/root/hash reject; routing metadata stays outside closed native payloads |

Additional explicit contract coverage is mandatory because four INQ package vectors cover identity routing, not fields: all 38 definition inventory parity, all OpenAPI refs resolve; legal boolean extension properties on the three source-exact event envelopes and their data objects, illegal nonboolean extensions reject; TS unknown index shape remains compilable; closed objects reject unknown properties; required/missing/null, enums, codepoint-length boundaries (including supplementary Unicode); structural uniqueItems rejecting reordered equal objects and accepting unequal arrays; duplicate/escaped names before deserialization; native LEAD required/optional owner projections separately; original byte preservation. Add numeric inequality cases near JS safe integer boundaries so floating-point collapse cannot falsely satisfy equality. Native definition-specific tests must preserve schema range/type constraints and avoid generic any-string owner types.

## Proposed exact CRM write window

COORD should reserve this entire window for D before START; no other worker edits the same generator or generated artifacts.

- `contracts/s01/` (new pinned package, input resources, adopted schema/fixture subsets, provenance manifest; no absolute runtime paths).
- `scripts/import-s01-contracts.py` (new narrow deterministic importer/checker).
- `src/CP6.CRM.Application/Contracts/Wire/` (new local source-pinned helper copies).
- `src/CP6.CRM.Application/Contracts/Inquiry/` (new native C#/decoder/schema integration).
- `src/CP6.CRM.Application/Contracts/Lead/` (new native CP21 contract/owner boundary integration).
- `apps/web/src/contracts/inquiry/`, `apps/web/src/contracts/lead/`, `apps/web/src/contracts/wire/` (new native TS/helper modules).
- `tests/CP6.CRM.UnitTests/Contracts/InquiryContractAdoptionTests.cs` plus dedicated helper/test resource files under `tests/CP6.CRM.UnitTests/Contracts/S01/`.
- `apps/web/tests/contracts-exact.test.ts` and dedicated resource/helper files under `apps/web/tests/s01/`.
- `docs/evidence/s01-08/` and `docs/delivery/S01-08.md` (worker's private evidence/delivery only).
- Narrow configuration exceptions, if necessary and pre-recorded: `tests/CP6.CRM.UnitTests/CP6.CRM.UnitTests.csproj` only for portable resource copying; `apps/web/package.json` only appending the new test to explicit test-file list; `apps/web/tsconfig.test.json` only if imports do not already cause new sources to compile. Do not change dependencies/lockfiles or root solution.

Original task-card inspection paths `CrmApi.g.cs`, `CrmContractInputFilter.cs`, `leads/types.ts`, `api.ts`, `PublicInquiry.tsx`, and `CrmBusinessService.Intake.cs` remain inspection points. If an actual change to first-slice outputs/filter/endpoint wiring is required, D sends a concrete QUESTION identifying schema version, original accepted behavior, generator source, affected users/tests and precise paths; COORD extends the serial window. Native library adoption must not be represented as current HTTP/business use. Shared Main roadmap/generated progress/four state files are COORD-only. CRM's four state files also require a coordinator-owned final synchronization window.

## Local verification to dispatch (not executed in this assessment)

Run once after related implementation is ready; preserve effective source hashes, environments, test counts, case IDs and failure history. Existing successful unrelated evidence is reused with its true scope.

```powershell
python scripts/import-s01-contracts.py --check
python scripts/generate-crm-api.py --check
python tests/ci/first-slice-implementation.Tests.py

dotnet test tests/CP6.CRM.UnitTests/CP6.CRM.UnitTests.csproj --filter FullyQualifiedName~InquiryContractAdoptionTests --logger 'trx;LogFileName=s01-08.trx'
# If production first-slice filter/client changed, add its relevant existing API tests to the filter.

# CRM/apps/web, use restored repository-local tsc (do not let npx fetch a new compiler):
npx --no-install tsc -p tsconfig.test.json
node --test .test-dist/tests/contracts-exact.test.js
npm run typecheck
```

The unit-test project references Application/API/Infrastructure/Worker, so dotnet test compiles the actual net8 application dependency graph. No SQL suite, owned DB, Docker image rebuild, full browser run or full solution test is required merely for contract modules. If UI/API behavior is broadened, reassess targeted checks before proceeding. If modifying package test list, confirm the new file actually executes; existing npm test enumerates files explicitly. Use own worktree bin/obj/node_modules, restore existing locks/versions only. Package availability must be checked locally; lack of an existing dependency becomes a recorded restore gap, not permission for remote Actions or package upgrades.

## Actions blocker and delivery limits

Committed CRM `.github/workflows/crm-validation.yml` currently includes unfiltered `pull_request` and `push: branches: [main]`, plus workflow_dispatch. No pull_request_target was found. COORD reported current queues empty. Task-branch push presently does not match push-main, but any PR and eventual main push can start paid tests. Changing source/web paths selects .NET, SQL and web work; unknown paths can select full current scope. Do not create/update a PR or push main while these triggers remain effective. Recheck both candidate and protected-branch workflows before every such action.

A YAML-only “manual mode” edit is insufficient: `scripts/ci/workflow-contract.ps1` explicitly requires pull_request/push/main entries and forbids path filters; `tests/ci/workflow.Tests.ps1` depends on those assumptions. Workflow includes formal consumer download/identity/artifact validation and SQL test jobs, so retain manual capability and analyze artifact consumers rather than indiscriminately deleting jobs. Safe remedy is a separate coordinator-owned CRM CI-policy window with YAML, workflow guard and its event assertions updated together, local policy regression checks and an actual remote workflow-state audit before PR. Do not silently expand D's business-contract branch into CI policy work or alter branch protection. If unresolved, D can complete local implementation/evidence and safe task-branch push but must report PR/main delivery blocked.

S01-08 completion is native generated/decoder contract adoption only. Original AC-INQ-009/048/053 are referenced for field identity, schema-before-decode and codepoint length subassertions. Full generated-adoption revision POS/NEG scenarios and full LEAD nullable scenarios retain NOT_RUN until real consumer/permission/persistence sequences run. Download proof/current session/server ETag, 90/99 expiry terminal pages, full negative-length HTTP responses, H01 atomic effects, current authorization, tenant transactions, PostgreSQL storage and production acceptance are excluded. No deployment, package publication, migration, replacement database, Actions run or BUG closure is authorized by this assessment.
## Coordinator decision received after assessment

COORD confirmed a separate CRM CI-policy branch is being handled by helper `crm_manual_validation`, preserving all jobs/artifact identity guards/permissions while converting triggers and corresponding contract tests to manual-only. This resolves the ownership/window question, not yet the remote delivery prerequisite: D must wait for COORD's verified CI-policy delivery before PR/main operations. COORD's protection API returned 403; no protection bypass is authorized or inferred. No migration or Actions execution is part of that prerequisite.

COORD also confirmed the bounded implementation interpretation: adopt native 38 definitions / 55 repaired references in dedicated native generator/validator directories, preserve the first-slice 44-schema API, and leave actual HTTP wire hookup to S03. Record this scope explicitly in the new dispatch packet; do not mutate the original frozen source plan or package merely to correct the “55 definitions” phrase. Therefore first-slice filter/endpoint/source files need no default write grant for S01-08.