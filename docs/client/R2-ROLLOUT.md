# R2 rollout and operations checklist

Complete the environment and warehouse decisions in
[`R2-RELEASE-INPUTS.md`](./R2-RELEASE-INPUTS.md) before producing the first
pilot candidate.

## Before enabling a warehouse

1. Apply all EF Core migrations and confirm the latest migration is
   `ProductionDataIntegrityAndScanRetention`.
2. Configure a trusted CA certificate, or import and pin the approved
   self-signed public key on every managed device. Never bypass certificate
   validation.
3. Publish signed MSIX/AppInstaller and APK artifacts, record their SHA-256
   values, and update `Security:NativeClient`.
   - The MSIX manifest `Publisher` must exactly match the code-signing
     certificate subject. Use an RFC 3161 timestamp service when available.
   - The Android APK signer must be the protected organization release key,
     never `CN=Android Debug`.
   - Use approved non-placeholder Windows package images.
4. Start the API with `ASPNETCORE_ENVIRONMENT=Production` and confirm the
   startup configuration gate passes before routing warehouse traffic.
   Configure the orchestrator/load balancer to use:
   - `GET /health/live` for process liveness.
   - `GET /health/ready` for SQL Server and distributed-cache readiness.
   Both endpoints are anonymous, return `no-store` JSON, and omit dependency
   exceptions and connection details.
5. Create the four standard warehouse roles and assign warehouse/area scopes.
   Use the Web production console or
   `PUT /api/v2/admin/wms-role-scopes/{roleId}`. Scope replacement requires
   `pub-data-scope:edit`; device management permission alone cannot widen a
   role's task access. A blank area grants the whole named warehouse. Roles
   other than administrator role `1` fail closed when they have no scope rows.
6. Create activation tickets for the intended device group. For Android,
   encode the approved HID prefix/suffix, Enter/Tab/manual termination mode,
   and duplicate window in the activation QR. Then verify signed heartbeat,
   remote disable, session revocation, and the 12-hour shared-device full-auth
   boundary.
7. Import barcode aliases through preflight first. Enable serial/LPN only after
   controlled conversion counts reconcile exactly with aggregate stock.
8. Enable `ProductionMoveEnabled`, then `SerialLpnEnabled`, per warehouse.

## Release quality gate

Run these checks from the repository root immediately before producing signed
artifacts:

```powershell
dotnet list .\CP6.slnx package --vulnerable --include-transitive
npm --prefix .\cp6.web audit --registry=https://registry.npmjs.org
dotnet test .\CP6.Tests\CP6.Tests.csproj -c Release
$env:CP6_TEST_SQLSERVER = "<ephemeral SQL Server test connection>"
dotnet test .\CP6.Tests\CP6.Tests.csproj -c Release `
  --filter FullyQualifiedName~WmsProductionSqlServerTests
Remove-Item Env:CP6_TEST_SQLSERVER
dotnet test .\CP6.Client.Tests\CP6.Client.Tests.csproj -c Release
npm --prefix .\cp6.web run type-check
npm --prefix .\cp6.web test -- --maxWorkers=4
npm --prefix .\cp6.web run e2e -- `
  --project=wms-production-mocked e2e/wms-production-console.spec.ts
npm --prefix .\cp6.web run build-only
dotnet tool restore
.\scripts\test-r2-source-gate.ps1 -Configuration Release
```

The vulnerability commands must report zero known vulnerabilities. The npm
registry override is intentional because the configured mirror may not
implement the audit endpoint.

The SQL Server gate creates and removes an isolated test database when the
connection targets `master`. It verifies MOVE concurrency and replay,
replenishment transaction write-back, source-document warehouse/area
fail-closed behavior, serial reconciliation, and LPN tree atomicity. The
`wms-production-sql.yml` workflow runs the same class against SQL Server 2022
for every pull request and push to `main`.

The mocked Web acceptance project owns its authentication state and API
fixtures. It verifies that production rollout/device data render, Android HID
settings stay out of the activation-ticket request, the settings are encoded
in the one-time QR, Windows hides scanner provisioning, and the browser reports
no uncaught or console errors. It does not replace a deployed-environment smoke
test.

When signed artifacts are ready, run `scripts/test-r2-artifacts.ps1` with the
approved Windows publisher, Android signing-certificate fingerprint, and a
secret-store-rendered production settings file. Archive the generated
`release-manifest.json` with the change record.

After deployment and artifact publication, run:

```powershell
.\scripts\test-r2-deployment.ps1 `
  -BaseUrl "https://cp6.example" `
  -ReleaseManifestPath "D:\release\release-manifest.json" `
  -OutputEvidencePath "D:\release\deployment-evidence.json"
```

This must pass without `-SkipArtifactDownload`. It verifies liveness,
SQL Server/Redis readiness, server time, both bootstrap responses, and the
remote AppInstaller/MSIX/APK bytes against the approved manifest. Archive
`deployment-evidence.json` with the release change.

## Performance gate

Install the pinned workspace-local k6 binary once. The installer downloads the
official Windows amd64 archive and rejects it unless its SHA-256 matches the
reviewed release asset:

```powershell
.\scripts\install-k6-portable.ps1
```

Run preparation only against an isolated non-production warehouse with sufficient
source stock and target capacity. Use a short-lived account that has
`device-manage`, `view`, `add`, `claim`, `scan`, `complete`, and `cancel`.
The token is accepted only through the process environment and is not written to
the pilot manifest:

```powershell
$env:CP6_PILOT_ACCESS_TOKEN = "<short-lived-test-token>"
.\scripts\prepare-r2-pilot.ps1 `
  -BaseUrl "https://cp6.example" `
  -WarehouseCd "PILOT-WH" `
  -AreaCd "PILOT-A" `
  -FromLocationCd "PILOT-SOURCE" `
  -ToLocationCd "PILOT-TARGET" `
  -ProductCd "PILOT-SKU" `
  -Quantity 1 `
  -TaskCount 10 `
  -DeviceIds @("pilot-rf-01", "pilot-rf-02") `
  -ConfirmIsolatedWarehouse
```

Preparation verifies liveness, readiness, bootstrap, the warehouse feature flag,
and active device scope before creating any tasks. If creation fails part-way,
the script makes a best-effort cancellation of tasks created by that run. Its
final line prints the generated `pilot-input.json` path.

Use that manifest to run the 500-device SignalR hold, 100-request-per-second task
read, and MOVE write workflow together:

```powershell
.\scripts\invoke-r2-pilot.ps1 `
  -PilotManifestPath "D:\CP6\artifacts\pilot\<run-id>\pilot-input.json" `
  -Duration "10m"
Remove-Item Env:CP6_PILOT_ACCESS_TOKEN
```

The gate fails on dropped read iterations, task-list P95 above 300 ms, a
SignalR connection/error breach, a connection ending before 90% of the requested
hold time, no task event, real-time delivery P95 above 2 seconds, scan P95 above
300 ms, or completion P95 above 2 seconds. For a WAN run, pass
`-MaxScanP95Ms 1000`.

The runner archives both k6 summaries, stdout/stderr, exit codes, thresholds,
k6 version, and SHA-256 file hashes under `artifacts/pilot`. The evidence and
manifest contain no access token. Preserve the evidence for both LAN and WAN
runs. Never execute the write profile against a production warehouse.

## Pilot acceptance

- One tenant, one warehouse, one device group.
- Ten physical Android devices and the dispatch Web/Windows consoles.
- At least 1000 MOVE tasks over two continuous weeks.
- Zero duplicate inventory transactions, zero lost inventory, and no
  unreconciled serial/LPN aggregate.
- Verify pause/takeover invalidation, request-result-unknown recovery, partial
  completion/remainder generation, device disable, PIN lockout, and print-job
  idempotency.
- Verify Windows and Android heartbeats continue without opening or refreshing
  the task page, pause while Android is stopped, resume immediately in the
  foreground, report the active task, and force a return to activation/login
  after remote device disable.
- Validate every pilot scanner path: HID Enter, HID Tab when used, camera, and
  each vendor broadcast profile. Confirm configured HID framing is stripped,
  missing framing is rejected, CR/LF/TAB never reaches barcode matching, and
  the same normalized value delivered twice inside the configured window
  produces one scan request and advances only one workflow step.
- During the unknown-result drill, cut the response path after claim or
  completion reaches the server. The client must query task state once and
  must not replay the write automatically. A scan retry must retain the same
  `ClientScanNo`; completion retry must retain the same operation ID.
- Before enabling R2B, include two products that deliberately share one serial
  number in the validation set. Moving, splitting, merging, or unpacking an LPN
  for one product must not change the other product's serial location or LPN.
  Controlled conversion must fail while any stock-bearing warehouse has R2B
  disabled or while scanned unit counts do not exactly match every stock
  warehouse/location/lot bucket.

## Recovery and rollback

- Back up SQL Server frequently enough to keep RPO at or below five minutes;
  rehearse restore and service recovery within one hour.
- Disable feature flags to stop creation of new v2 work. Do not downgrade the
  database.
- Keep compatible clients available so already-started v2 tasks can finish.
- Preserve task events indefinitely. Raw scan rows are removed only after each
  row's configured `RetainUntil` timestamp by the hosted cleanup worker.
- Monitor failed label jobs, exception backlog, SignalR disconnects, disabled
  device attempts, refresh-token replay, and stock/serial reconciliation.
