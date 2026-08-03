# CP6 Space CAD Experiment Harness

This experiment-only tool captures reproducible E02-S01 evidence without
coupling a production Worker to a CAD vendor SDK.

It provides seven capabilities:

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
