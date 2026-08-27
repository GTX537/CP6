# AutoCAD Core Console candidate Worker

This is the runnable, CAD-only server half of the CP6 remote Worker protocol.
It accepts native DWG and DXF over mutually authenticated HTTPS, stages and
verifies the complete source hash before conversion, invokes
`SpaceCadConverterContractRunner`, returns CAD IR only, and fails the request
if its per-attempt raw-data directory cannot be deleted.
Protocol schema 2 also requires the full deployment-approved Worker Release
SHA-256 on every request and echoes it in every validated response.

DWG runs through the exact Core Console executable and then the managed DXF
parser. Native DXF runs directly through that same managed parser without
starting AutoCAD. The advertised candidate version binds both the executable
version and `cp6-dxf-1.1.0`, so either side of the chain requires a new Site
qualification.

It is a **candidate**, not an approved production Provider. Do not enable the
Web API registration until the exact AutoCAD version and Worker release have
passed ADR-0001 qualification on the authorized golden dataset and the
licensing, security, data-region, retention/deletion, identity, certificate,
and Site approvals are recorded in the deployment-owned approval Manifest.

The runnable host no longer advertises a `development` Provider identity. Build
an isolated payload and create its immutable release Manifest before startup:

```powershell
dotnet publish .\tools\CP6.Space.CadWorker.AutoCadCandidate\CP6.Space.CadWorker.AutoCadCandidate.csproj -c Release -r win-x64 --self-contained false -o C:\cp6-space-cad-worker\1.0.0
C:\cp6-space-cad-worker\1.0.0\CP6.Space.CadWorker.AutoCadCandidate.exe release-manifest C:\cp6-space-cad-worker\1.0.0 1.0.0 <40-lowercase-source-commit> win-x64 "C:\Program Files\Autodesk\AutoCAD 2025\accoreconsole.exe"
```

The command writes `cp6-space-cad-worker-release.json` with an ordinal inventory
of every payload file plus the exact Core Console hash/version, runtime, source
commit and managed DXF converter version. It prints the full Manifest SHA-256,
release Provider Key and derived Provider Version. Do not add logs, certificates,
configuration or other files to that payload directory after sealing it.

Required deployment settings:

- `CP6_SPACE_CAD_LISTEN_URL` — absolute HTTPS origin without credentials,
  path, query, or fragment.
- `CP6_SPACE_CAD_CLIENT_CERT_SHA256` — pinned client certificate SHA-256.
- `CP6_SPACE_CAD_ACCORECONSOLE_PATH` — approved `accoreconsole.exe` path.
- `CP6_SPACE_CAD_WORK_ROOT` — dedicated encrypted ephemeral volume.
  Its absolute path must be at most 120 characters so nested Core Console script
  paths remain below the vendor path limit.
- `CP6_SPACE_CAD_WORKER_RELEASE_MANIFEST_PATH` — the in-payload immutable
  `cp6-space-cad-worker-release.json` path.
- `CP6_SPACE_CAD_WORKER_RELEASE_SHA256` — exact lowercase SHA-256 printed by
  `release-manifest`; it must also equal the approval Manifest's
  `workerReleaseSha256`.
- Standard Kestrel server-certificate configuration; secrets stay outside the
  repository and command line.

Before Kestrel starts, the Worker re-hashes the release Manifest, every payload
file and the external Core Console executable, checks the exact file version,
runtime and managed DXF version, and derives a non-development Provider Version
containing the release hash prefix. Any added, removed or changed payload file,
Manifest drift, runtime drift, or Core Console drift fails startup closed.

Optional limits are `CP6_SPACE_CAD_CONVERSION_TIMEOUT_SECONDS` (default 300)
and `CP6_SPACE_CAD_MAX_CONCURRENCY` (default 1, maximum 4). Network egress,
service identity, filesystem ACLs, encryption, and volume destruction remain
deployment controls and must be proven by the approval Manifest.
