# E02-S01 licensed vendor trial intake

Status: external-input gate; no production adapter implementation is authorized
until E02-S01 is accepted.

## Why this exists

E02-S02 depends on E02-S01, and the frozen Space specification requires the CAD
selection experiment to complete before implementation. This intake makes the
remaining external prerequisites machine-checkable without accepting vendor
terms, submitting identities, or storing credentials in Git.

## Required inputs

For both candidates:

- the formal, authorized 20-file golden dataset with immutable hashes;
- the separate 50MiB and one-million-entity stress assets;
- an exact candidate/engine version;
- a legal or procurement record confirming multi-tenant SaaS, scaled Workers,
  disaster recovery, non-production, and redistribution or hosted-service use;
- frozen 8 vCPU / 32GiB Worker isolation evidence;
- secrets supplied only through the declared environment variables.

ODA additionally requires:

- exact Windows x64 and Linux x64 SDK packages plus SHA-256 values;
- a license path supplied through `CP6_SPACE_ODA_LICENSE_PATH`;
- a deny-all network policy for the parser Worker.

APS additionally requires:

- an approved data region and approval record;
- DPA and retention/deletion evidence;
- a pinned AutoCAD engine alias/version;
- non-production client credentials supplied through environment variables;
- outbound access restricted to approved service endpoints.

## Run

Copy the relevant example to an evidence directory outside Git, replace every
placeholder, set the named environment variables, and run:

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  preflight `
  --config <evidence>\preflight.json `
  --output <evidence>\preflight-result.json
```

Exit code `4` is a normal fail-closed result while any prerequisite is missing.
The report contains only secret-variable names and configured/not-configured
booleans; it never contains secret values. A passing preflight authorizes the
licensed experiment run, not production deployment or E02-S02 acceptance.
