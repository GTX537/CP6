# S01-11 implementation handoff

Status: READY_FOR_REVIEW. The implementer has not staged, committed, pushed,
created a PR, merged, changed shared status, invoked GitHub Actions, or deployed.
The parent/coordinator owns those delivery actions and the task's final status.

Implementation baseline: `e20740e37e20bc0210e0b32c5670c2bce962002a` in the assigned
`codex/s01-11-erp-credit-20261012` worktree. Execution identity is the raw source,
input, configuration and lock hashes in each run's metadata, not the baseline SHA.

## Delivered behavior

- Separate exact current-v1/frozen-R8 namespaces, profiles and TypeRegistry pins;
  both require the same exact native WMS and FIN-AR schema bytes.
- Fourteen named full native factories, two named Money/D8 scalar helpers, and
  two separately bounded frozen C01/C02 full-response replay roots. No generic
  TypeRegistry or F09-dependent C06 decoder.
- Strict UTF-8/JSON, decoded-name duplicate rejection, required/null-only/closed
  objects, unions and conditional schemas. C# reuses delivered StrictJson and
  JsonSemanticEquality on actual net8. TS validates exact numeric tokens, uses
  own-property checks and null-prototype objects, and snapshots buffers before
  asynchronous hashing. Both retain complete original bytes and identity.
- Native WMS 30/8 and FIN-AR 20/8 decimal text domains; native `-0` and `1.0`
  preserved. Full money, range, quantity, refund, result projection and member
  semantic checks apply to nested native objects. No FIN-CREDIT canonical rules.
- Current APPLIED/REJECTED/NO_CHANGE distinctions; frozen original semantics.
  Explicit adaptation assessment remains READ_ONLY_UNPROVEN / MANUAL_RECONCILIATION
  with UNKNOWN effects and no FOUND, release or dispatch permission.
- Frozen C01/C02 replay returns HTTP200 and the original complete HTTP201
  COMMITTED response bytes after exact request/key/hash matching. No rename,
  serialization, redaction, Store mutation or writer callback exists.
- Portable exact schemas/registries/current OpenAPI, five exact composed bodies,
  343 provenance-tagged CURRENT projections, and original/composed source range
  hashes plus explicit API and schema-closure mapping. Archived programs were
  read as data, never executed or republished as runnable code.

## Final applicable evidence

| Run directory | Actual result |
| --- | --- |
| `20261013T060354Z-dotnet-final-407086e6` | 417 discovered/executed, 417 passed, zero failed/skipped. TRX records `Environment.Version=8.0.29; .NET 8.0.29`. |
| `20261013T060526Z-ts-final-5254b7ee` | 416 discovered, 416 passed, zero failed/pending. |
| `20261013T060619Z-typecheck-83ec4cb2` | `npm --prefix cp6.web run type-check`, exit 0. |
| `20261013T060706Z-design-f299e68e` | 5/5 portable design-fixture audits; 343 dependency projections, 60 stale identity/proof rejections, 18 Money positions, 29 OpenAPI routes and ten extension blocks. |

Both languages individually execute the 400 private cases and all ten fixed
VERSION-001..008/BYTES-001..002 cases, plus their boundary guards. C# also has an
actual runtime assertion. Commands, timestamps, environment, before/after source
and input hashes, actual logs, TRX/Vitest JSON and compiled artifacts are retained
per run. `fixture-reproduction.json` records a byte-identical reconstruction of
both generated unit fixture files from repository-relative inputs.

RED evidence is retained at `20261013T052402Z-dotnet-red-243e73f1` and
`20261013T052518Z-ts-red-c29b488e`: each executed 208 cases, 145 expected assertion
failures and 63 passes, zero skips. `red-scaffold/` contains those permissive
scaffold source snapshots as noncompiled text. Subsequent green/expanded runs
retain their original, narrower test scope.

The first type-check failed with two task-local TypeScript type errors, preserved
in `20261013T053800Z-typecheck-83b6779d`; the final run verifies their correction.
Locked `npm ci --ignore-scripts --no-audit --no-fund` succeeded in
`20261013T051443Z-restore-ts-92d7c10d`. Its existing glob deprecation warning is
retained. No dependency upgrades or shared project/lock/configuration edits occurred.

## Limits and retained fences

The source-chain audit and selected body projections are design fixture
subassertions. They do not validate whole C06 public bodies or prove real Owner,
issuer, permission, current AR/GL/member/finite or Store adoption. Actual receipt
fields stay absent, and actual new metadata writes stay zero. The shared BYTES
inputs are constructed and intentionally fail native PostInput row-version
validation; their retention type reports NativeSchemaValidated=false.

The 247 runtime business scenarios remain NOT_RUN, 41 reference cases remain
REFERENCE_ONLY, ITER-REL remains missing and WP05 remains PROPOSED. No database,
real HTTP/writer, production release or Owner integration was run. Legacy
CreditNote storage, DTOs, service and controller were inspected read-only. No SQL
Server implementation, PostgreSQL schema migration or database connection was added.

Files are confined to the assigned ErpCredit C#/TS directories, the single task
xUnit test file, `eng/contracts/s01-11`, and this `implementation` evidence directory.
All Git commands used `-c core.longpaths=true`. Existing leaf attributes already
cover the new xUnit file; task-local attributes preserve LF source and exact raw
asset/evidence bytes. Raw logs may need explicit ignored-file staging by the parent.
