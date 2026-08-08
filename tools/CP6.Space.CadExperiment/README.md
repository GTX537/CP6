# CP6 Space CAD Experiment Harness

This experiment-only tool captures reproducible E02-S01 evidence without
coupling a production Worker to a CAD vendor SDK.

It provides a set of bounded development capabilities:

- `audit`: verifies dataset metadata, companion answers, SHA-256 values,
  DXF framing, DWG version headers, formal split/family distribution and
  separate E02 readiness gates.
- `generate-stress`: deterministically creates a DXF of at least 50 MiB or
  exactly 1,000,000 `LINE` entities. Generated assets belong under `tmp`.
- `generate-dev-corpus`: deterministically creates 20 synthetic development
  DXF drawings across L1-L5, with hashes, expected-answer companions, issue
  expectations and an explicit non-release asset statement.
- `convert-dev-ir`: converts a bounded development DXF into the versioned,
  vendor-neutral CAD IR contract and writes a deterministic JSON fixture.
- `prepare-dev-coordinate`: applies an explicit unit, origin, rotation and
  target-floor confirmation to a development CAD IR package.
- `build-dev-inventory`: creates a source/transform-bound layer, block and
  block-reference inventory from a ready coordinate preparation.
- `minimize-dev-ai-cad-features`: writes a provider-safe MetadataOnly or
  StructuredFeatures payload plus a separate local-only SourceRef map, without
  invoking a Provider or writing Draft data.
- `run-dev-ai-provider`: runs the deterministic Mock, local heuristic or a
  simulated retryable-failure-to-local fallback through the same Provider SPI,
  then validates its typed Canonical Envelope before writing it.
- `validate-dev-ai-provider-output`: treats a raw CP6 Canonical Envelope as
  untrusted JSON and validates its schema, limits, references and semantics.
- `synthesize-dev-ai-proposals`: binds a validated Provider envelope back to
  the local-only source map and E02 semantic preview, applies
  `HumanLocked > DeterministicRule > AI > TemplateDefault`, and writes a
  deterministic read-only proposal set. Geometry remains rule-generated;
  rack derivation requires explicit profiles and location codes remain pending
  the existing code-service precheck. It never writes a Design Draft.
- `evaluate-ai-offline`: matches normalized final proposals to versioned
  expected targets by sample and stable source key, calculates coverage,
  semantic accuracy, manual-operation reduction and high-confidence precision,
  calibrates only on the Calibration split, and applies a Wilson lower-bound
  gate before any high-confidence shortcut can be enabled.
- `query-dev-inventory`: runs capped, deterministic layer, block or reference
  queries against an inventory artifact.
- `seal-dev-mapping-profile`: validates and hash-seals an immutable development
  mapping profile version.
- `preview-dev-mapping`: applies a system or same-tenant profile plus optional
  per-layer overrides and writes a deterministic, non-writing preview.
- `run`: invokes a candidate adapter as a child process without a shell and
  records timeout, cancellation, process-tree termination, exit status, peak
  working set, diagnostics and observation hash.
- `probe-adapter`: provides a streaming DXF calibration adapter. It is not a
  product candidate and cannot receive selection points.
- `preflight`: fails closed until the formal dataset, legal deployment rights,
  licensed packages or governed service, secret presence and frozen Worker
  isolation evidence are all present.

The tool implements a development-only `ICadConverter` and small-fixture JSON
sink. It does not write Draft data, join `CP6.slnx`, read native DWG, or qualify
as a licensed production adapter. Formal E02-S02 acceptance remains blocked
until E02-S01 has a licensed, scored selection.

## Build and test

```powershell
dotnet restore tools\CP6.Space.CadExperiment.Tests\CP6.Space.CadExperiment.Tests.csproj
dotnet test tools\CP6.Space.CadExperiment.Tests\CP6.Space.CadExperiment.Tests.csproj `
  -c Release --no-restore
```

## Audit the dataset

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  audit `
  --manifest <golden-package>\manifest.json `
  --stress-50mb <tmp>\stress-50mb.dxf `
  --stress-million <tmp>\stress-1m-entities.dxf `
  --output <evidence>\dataset-audit.json `
  --require-e02-ready
```

Exit code `0` means integrity and requested readiness gates passed. With
`--require-e02-ready`, exit code `3` means the package is valid but incomplete
for E02. Integrity or hash failures return `1`.

## Generate stress assets

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  generate-stress --kind 50mb --output <tmp>\stress-50mb.dxf

dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  generate-stress --kind million --output <tmp>\stress-1m-entities.dxf
```

Each output receives a `.cad-stress.json` sidecar. Stress assets exercise
capacity only and never replace the 20-file golden set.

## Generate the synthetic development corpus

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  generate-dev-corpus `
  --output docs\space\acceptance\development-v2.0.0
```

The generated package is safe for CP6 parser, mapping, issue, UI, regression
and demo development because all drawings are synthetic. Its manifest always
sets `purpose=DevelopmentSeed` and `countsTowardReleaseGate=false`; it must not
be represented as the licensed native-DWG golden dataset required by E02-S01.

## Convert a development DXF to CAD IR

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  convert-dev-ir `
  --input docs\space\acceptance\development-v2.0.0\seeds\13-automated-warehouse.dxf `
  --output tmp\e02-s02\13-automated-warehouse.cad-ir.json
```

The command verifies the exact source SHA-256, normalizes known units to
millimeters, emits stable source references, preserves unsupported entities as
explicit issues, validates the package contract and prints the source and IR
hashes. It is intentionally limited to UTF-8/ASCII DXF files up to 25 MiB and
uses an in-memory JSON sink for development fixtures only. Production-sized
artifacts require the isolated streaming Worker sink selected after E02-S01.

## Confirm coordinates and assign a target floor

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  prepare-dev-coordinate `
  --input tmp\e02-s02\13-automated-warehouse.cad-ir.json `
  --confirmation docs\space\contracts\cad\v1\examples\development-coordinate-confirmation.json `
  --output tmp\e02-s03\13-automated-warehouse.prepared.json
```

The confirmation is bound to the exact source SHA-256. The command corrects a
detected unit when the confirmed unit differs, applies the source origin,
floor-local origin and counterclockwise Z rotation, rounds output geometry to
integer millimeters and records a deterministic transform hash. It returns exit
code `3` after writing evidence when the extent is implausible or geometry lies
outside the assigned floor boundary. This is E02-S03 development evidence, not
a production parsing authorization.

## Build and query the layer/block inventory

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  build-dev-inventory `
  --input tmp\e02-s03\13-automated-warehouse.prepared.json `
  --output tmp\e02-s04\13-automated-warehouse.inventory.json

dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  query-dev-inventory `
  --input tmp\e02-s04\13-automated-warehouse.inventory.json `
  --kind reference --layer RACK --attribute RACK_ID --value R-01-01 `
  --limit 50 --output tmp\e02-s04\13-rack-reference-query.json
```

The inventory includes declared empty layers, color, line type, visibility,
per-type and supported/unsupported counts, bounds, block definitions,
references and controlled attributes. It is bound to the source hash,
coordinate transform and target floor by a deterministic SHA-256. Query pages
are limited to 200 records. This is E02-S04 development evidence only; it does
not add production persistence, tenant authorization or a licensed DWG adapter.

## Minimize and redact CAD features for AI development

Create a short-lived 32-byte binary HMAC key outside tracked source files:

```powershell
$keyBytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Fill($keyBytes)
[IO.Directory]::CreateDirectory("tmp\e13-s04") | Out-Null
[IO.File]::WriteAllBytes("tmp\e13-s04\dev-hmac.key", $keyBytes)
[Array]::Clear($keyBytes, 0, $keyBytes.Length)
```

Then project a parsing-ready coordinate package:

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  minimize-dev-ai-cad-features `
  --input tmp\e02-s03\13-automated-warehouse.prepared.json `
  --policy StructuredFeatures `
  --hmac-key-file tmp\e13-s04\dev-hmac.key `
  --tenant-id 11111111-1111-1111-1111-111111111111 `
  --site-id 55555555-5555-5555-5555-555555555555 `
  --model-version-id 66666666-6666-6666-6666-666666666666 `
  --run-id 77777777-7777-7777-7777-777777777777 `
  --provider-output tmp\e13-s04\provider-input.json `
  --source-map-output tmp\e13-s04\local-source-map.json
```

The provider file contains only allowlisted enums, counts, buckets, HMAC tokens
and—under `StructuredFeatures`—0–1 relative bounds and bounded relations. It
never contains raw files, absolute coordinates, tenant/site IDs, SourceRef,
attribute values or storage details. The second file is explicitly local-only
because it restores `SourceKey` to raw SourceRef. The command never calls an
external Provider and never writes a model Draft. Use an environment secret
reference rather than this development key-file mechanism in production.

Run the minimized input through a development Provider:

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  run-dev-ai-provider `
  --input tmp\e13-s04\provider-input.json `
  --provider local `
  --output tmp\e13-s05\local-output.json

dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  run-dev-ai-provider `
  --input tmp\e13-s04\provider-input.json `
  --provider fallback-local `
  --failure timeout `
  --output tmp\e13-s05\timeout-fallback-output.json

dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  validate-dev-ai-provider-output `
  --input tmp\e13-s04\provider-input.json `
  --provider-output tmp\e13-s05\local-output.json
```

`mock` and `local` are deterministic, network-free Provider implementations.
`fallback-local` injects only a declared unavailable, timeout or rate-limit
failure and then calls the local implementation through the same SPI. User
cancellation and contract violations never fallback. The raw validation command
rejects invalid JSON, unknown or duplicate properties, non-string enums, unsafe
control characters, excessive arrays, unknown/duplicate SourceKeys, invalid or
self relations, range violations, duplicate evidence, invalid diagnostic
references and incompatible type-specific attributes. Both commands enforce a
64 MiB development Canonical Envelope cap and print only stable evidence.

These commands do not register a production Provider, resolve credentials, call
a network endpoint, map a vendor-native response, persist Run/Usage data or write
Draft. External adapters must still cap the HTTP/SDK response before mapping it
to the CP6 Canonical Envelope and then invoke the same validator.

## Run the offline AI quality gate

Build a normalized `SpaceAiOfflineEvaluationRequestV1` from the immutable
dataset manifest, expected targets, final fused proposal sets and recorded
manual/AI-assisted operation counts. Proposal adapters use the existing
`WarehouseDraftProposalV1.SourceKey`; runtime database GUIDs are never treated
as expected answers.

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  evaluate-ai-offline `
  --input tmp\e13-s14\evaluation-request.json `
  --output tmp\e13-s14\evaluation-report.json `
  --require-release-eligible
```

Without `--require-release-eligible`, exit code `0` means the normalized data
is structurally valid and the report was written; a DevelopmentSeed report is
allowed but always records `releaseEligible=false`. With the flag, exit code
`4` means evidence or quality remains insufficient. Invalid dataset structure
returns `3`.

Threshold selection reads Calibration proposals only. Validation and
ReleaseHoldout never influence the selected threshold and are evaluated as the
out-of-sample group. The high-confidence path requires both at least 95%
precision and a 95% Wilson lower bound of at least 90%; a perfect but too-small
sample therefore stays closed. Formal release additionally requires the exact
10/5/5 split, L1-L5 coverage, unique CAD hashes, per-asset license and
de-identification evidence, version/annotation/acceptance records, an immutable
package and a passed hash-sealed integrity audit. The canonical report carries
its own SHA-256 and is rejected after tampering.

The synthetic `development-v2.0.0` corpus may exercise this command but cannot
be upgraded into release evidence by changing a flag. Geometry matching remains
the deterministic CAD golden-data responsibility; this E13 gate measures the
final semantic type, declared key attributes and exact logical relations.

## Build and query the read-only AI proposal review workspace

After `synthesize-dev-ai-proposals` has produced a validated proposal set, seal a
complete floor projection exported from the current Draft and build the review
workspace:

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  seal-dev-ai-review-baseline `
  --input tmp\e13-s08\baseline-draft.json `
  --output tmp\e13-s08\baseline.json

dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  build-dev-ai-review-workspace `
  --proposals tmp\e13-s08\proposals.json `
  --baseline tmp\e13-s08\baseline.json `
  --output tmp\e13-s08\review-workspace.json

dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  query-dev-ai-review-workspace `
  --input tmp\e13-s08\review-workspace.json `
  --cursor-key-file tmp\e13-s08\dev-hmac.key `
  --band High --winning-source DeterministicRule --locatable `
  --limit 50 --output tmp\e13-s08\high-rule-page.json

dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  preview-dev-ai-review-batch `
  --input tmp\e13-s08\review-workspace.json `
  --action Accept --band High `
  --output tmp\e13-s08\accept-preview.json
```

The baseline must declare `IsCompleteFloorProjection=true` and is bound to the
Tenant, ModelVersion, Floor, ContentRevision and optional ContentHash. The
workspace shows Added/Modified/Unchanged geometry, fields and rack capacity,
preserves source/evidence/issues, orders proposals by confidence band, object
type and stable identity, and uses an HMAC-protected development cursor with a
50/200 page bound. Batch preview accepts either explicit IDs or one filter and
caps the match set at 1,000; it always reports
`requiresServerRevalidation=true`, `decisionWritten=false` and
`draftWritten=false`. The cursor key is short-lived local development material
and must never be committed. Production endpoints use the existing
Data Protection cursor binding to tenant, actor, grant version and expiry.

## Seal and preview a CAD mapping profile

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  seal-dev-mapping-profile `
  --input docs\space\contracts\cad\v1\examples\development-mapping-profile-draft.json `
  --output tmp\e02-s05\development-mapping-profile.json

dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  preview-dev-mapping `
  --inventory tmp\e02-s04\13-automated-warehouse.inventory.json `
  --profile tmp\e02-s05\development-mapping-profile.json `
  --tenant-id 55555555-5555-5555-5555-555555555555 `
  --output tmp\e02-s05\13-mapping-preview.json
```

The profile version is immutable and bound by `definitionSha256`. System profiles
are tenant-neutral; tenant profiles are accepted only for their owning tenant and
must be created as copies/new versions. Exact, glob and safe regex precedence,
block attribute conditions, absent/empty required-source failures and explicit layer overrides
are resolved without creating Draft elements. Exit code `3` means the preview was
written but contains a Blocking conflict or missing required source. This remains
E02-S05 development evidence, not production mapping persistence or semantic parsing.

## Run an adapter

The runner invokes this versioned contract. Relative adapter arguments resolve from
the directory where the runner was started; input and output paths are absolute:

```text
<adapter> <adapter-prefix-arguments> inspect
  --input <absolute-input-path>
  --output <absolute-observation-path>
  --candidate-version <version>
```

Example:

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  run `
  --candidate vendor-id `
  --candidate-version 1.2.3 `
  --adapter dotnet `
  --adapter-arg <adapter.dll> `
  --input <sample.dxf> `
  --output <evidence-directory> `
  --runs 5 `
  --timeout-seconds 300
```

The runner is an evidence collector, not an OS sandbox. Malicious-file and
vendor trials still require the restricted identity, network policy, storage,
CPU, memory and time controls frozen by ADR-0001 and E01-S06.

## Preflight a licensed trial

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  preflight `
  --config <trial>\preflight.json `
  --output <evidence>\preflight-result.json
```

Exit code `0` authorizes only the licensed experiment. Exit code `4` is the
expected fail-closed result while any prerequisite is missing. Secret values
are read only from named environment variables and are never serialized.

The observation schema is documented in
`docs/space/experiments/e02-s01/adapter-contract-v1.md`.
