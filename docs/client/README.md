# CP6 production WMS clients

The existing Vue application remains the complete management UI. The native
clients add two focused experiences:

- `CP6.Desktop`: WPF dispatch console for MOVE task monitoring, creation,
  assignment, exception handling, device activation, live updates, and the
  local ZPL/TSPL/PDF print gateway.
- `CP6.Mobile`: .NET MAUI Android field client for activation, quick operator
  switching, claiming/starting tasks, HID/camera/broadcast scanning,
  weak-network progress recovery, partial completion, and idempotent label
  requests.
- `cp6.web`: the full administration surface for the real-time task board,
  devices, barcode import/maintenance, analytics, serials, LPNs, and labels.

Both native clients depend on `CP6.Client.Core` and the typed surface in
`CP6.Client.Api`. They share transport, in-memory access-token handling,
merged refresh, language packs, version checks, SignalR reconnection, logging
redaction, activation, offline-progress, label-job, and scan contracts. They
do not share UI controls or pages.

The three active clients use `/api/v2/wms/tasks`. The literal v1 route remains
available for one release cycle and accepts only v1 tasks. New production MOVE,
serial/LPN, device, barcode, and label behavior must not be added to v1.

## Production behavior

- MOVE creation reserves source stock and target capacity in one transaction.
- Replenishment execution and slotting approval publish source-linked v2 MOVE
  tasks. They never move physical stock directly. A pending task follows
  source-document edits and cancellation; once work starts, the source
  document is locked. Replenishment completion is written back only after the
  task inventory transaction commits. Replenishment is scoped by the target
  location area; warehouse-wide slotting requires a warehouse-wide role grant.
- Desktop task rows show the source type/number and partial-completion lineage
  so dispatchers can trace replenishment, slotting, parent, and remainder tasks
  without opening the source page first.
- Every write carries an operation ID and row version. Completion, stock
  movement, reservations, audit events, and remainder-task creation commit
  atomically.
- Pause, release, takeover, and exception handling invalidate the active
  execution so stale scan progress cannot be submitted.
- Android stores only an already-started task, scan profile, and uncommitted
  scan progress while offline. It never claims or commits inventory offline.
- Transport loss and client-side timeouts are treated as an unknown outcome,
  including `OperationCanceledException` raised by `HttpClient` timeouts.
  Claim/completion probe current task state without replaying the write.
  Android retains the same client scan number for an explicit scan retry and
  retains the completion operation ID until the result is resolved.
- Serial and LPN commands use the same idempotency and concurrency rules.
  A serial is identified by its product and serial number; LPN move, split,
  merge, and unpack operations resolve that composite identity and never update
  another product that happens to use the same serial number. Moving a parent
  LPN moves the complete container tree in one transaction.
- Controlled conversion of existing stock to serial tracking requires every
  stock-bearing warehouse to have its R2B feature enabled, every physical unit
  to be scanned exactly once in its warehouse/location/lot bucket, and the
  aggregate quantity to reconcile before the product is locked to serial
  tracking.
- Raw scan audit records default to 180-day retention, configurable per
  warehouse from 30 to 3650 days.

## Local configuration

The Web API defaults to `http://localhost:5177`. Set `CP6_API_URL` before
starting a native client to use another environment. A successful device
activation can replace this URL with the server address encoded in the
activation QR.

Native release policy is configured under `Security:NativeClient`:

- `LatestVersion`
- `MinimumVersion`
- `DownloadUrl`
- `Sha256`
- `AllowedRedirectUris`

Use environment variables or the untracked local settings file for real
release URLs and hashes. A client below `MinimumVersion` cannot enter a
business page. Native startup is fail-closed: the bootstrap contract must
match the requesting platform and version before authenticated API calls,
SignalR startup, password/2FA/quick-switch actions, or SSO callback exchange
are allowed. A required upgrade is offered only when its URL is HTTPS, its
file type matches the platform (`.msix`/`.appinstaller` or `.apk`), and its
SHA-256 is valid. If bootstrap is unavailable or invalid, users may retry or
return to device activation, but cannot bypass the gate.

## Security storage

- Access tokens exist in process memory only.
- WPF protects the refresh token with current-user DPAPI.
- Android uses MAUI `SecureStorage`.
- The device private key and one-time SSO PKCE verifier use the platform
  protected store.
- Native SSO redirect URIs are derived from the client platform and cannot be
  supplied by UI code. Callbacks with a different scheme, host, path, duplicate
  grant parameter, fragment, or error parameter are rejected before the PKCE
  verifier is consumed.
- SSO request and grant records use atomic compare-and-delete semantics.
  Invalid PKCE/device attempts do not burn a valid grant, while concurrent
  valid exchanges allow exactly one consumer across Redis-backed API replicas.
- Language packs, device ID, user settings, release metadata, active offline
  scan progress, and redacted logs are the only other allowed persisted data.
- Passwords, PINs, OTPs, authorization codes, access tokens, refresh tokens,
  and raw private keys must never be logged.

## Build

```powershell
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet build CP6.Desktop/CP6.Desktop.csproj
dotnet test CP6.Tests/CP6.Tests.csproj
dotnet test CP6.Client.Tests/CP6.Client.Tests.csproj
npm --prefix cp6.web run type-check
npm --prefix cp6.web test
npm --prefix cp6.web run build-only
```

Android targets .NET 10 and requires the MAUI Android workload. The shared
client libraries and server remain on .NET 8 until their separately tested
upgrade:

```powershell
dotnet workload install maui-android
dotnet build CP6.Mobile/CP6.Mobile.csproj -f net10.0-android
```

On a memory-constrained Windows development machine, shut down reusable build
workers before retrying a failed Android Release AOT build:

```powershell
dotnet build-server shutdown
dotnet clean CP6.Mobile/CP6.Mobile.csproj -f net10.0-android -c Release
dotnet build CP6.Mobile/CP6.Mobile.csproj -f net10.0-android -c Release -m:1
```

The clean step is needed only after an interrupted native build leaves
inconsistent generated state. CI should start from a clean workspace.

Release scripts require organization signing material supplied at invocation:

- `CP6.Desktop/scripts/publish-msix.ps1`
- `CP6.Mobile/scripts/publish-apk.ps1`

The Windows script discovers `MakeAppx.exe` and `SignTool.exe` from the latest
installed Windows SDK even when they are not on `PATH`. It validates the
certificate, makes the manifest publisher match the certificate subject,
packages and verifies the signed MSIX, and generates the matching
`.appinstaller` from the release URLs:

```powershell
.\CP6.Desktop\scripts\publish-msix.ps1 `
  -CertificateThumbprint "<organization-code-signing-thumbprint>" `
  -OutputDirectory "D:\CP6\artifacts\desktop" `
  -PackageUri "https://updates.cp6.example/desktop/CP6.Desktop.msix" `
  -AppInstallerUri "https://updates.cp6.example/desktop/CP6.Desktop.appinstaller" `
  -PackageVersion "1.0.0.0" `
  -TimestampServerUrl "https://timestamp.example"
```

Supply `-AssetsDirectory` with `StoreLogo.png`, `Square150x150Logo.png`, and
`Square44x44Logo.png` for production branding. Without it, the script emits a
warning and uses packaging-only placeholder images.

Android signing passwords are read by the .NET Android signing tasks through
environment variable references, so the values are not placed in command-line
arguments or build logs:

```powershell
$env:CP6_ANDROID_STORE_PASSWORD = "<secret>"
$env:CP6_ANDROID_KEY_PASSWORD = "<secret>"
.\CP6.Mobile\scripts\publish-apk.ps1 `
  -KeyStore "D:\secure\cp6-release.keystore" `
  -KeyAlias "cp6-release"
Remove-Item Env:CP6_ANDROID_STORE_PASSWORD, Env:CP6_ANDROID_KEY_PASSWORD
```

Both scripts print the resulting SHA-256 for the bootstrap release
configuration. Never distribute an APK signed by the Android debug
certificate.

## Release gates

The source gate checks client version alignment, MSIX/Android security
declarations, signing-script safety, NuGet/npm advisories, PowerShell syntax,
pilot performance/recovery contracts, and pending EF model changes:

```powershell
dotnet tool restore
.\scripts\test-r2-source-gate.ps1 `
  -ExpectedVersion "1.0.0" `
  -Configuration Release
```

Production WMS persistence behavior is additionally gated by
`.github/workflows/wms-production-sql.yml`. It runs
`WmsProductionSqlServerTests` on SQL Server 2022, including source-document
area filtering and the warehouse-wide slotting authorization boundary.

Run the native startup contract directly while developing authentication,
bootstrap, packaging protocols, or client navigation:

```powershell
.\scripts\test-native-client-contract.ps1 -Configuration Release
```

It verifies the server redirect allowlist, Windows protocol ownership, Android
intent filter, fixed client callback routing, atomic SSO grant consumption,
SemVer minimum-version behavior, client tests, server security tests, and the
Desktop build. Add `-IncludeAndroidBuild` when the .NET 10 MAUI Android workload
is available; CI always performs the Android Release build separately.

After placing the signed MSIX, generated AppInstaller, and signed APK in one
artifact directory, run the strict artifact gate. The approved Android
fingerprint is the SHA-256 certificate digest printed by `apksigner`; it is
not a private value.

```powershell
.\scripts\test-r2-artifacts.ps1 `
  -ArtifactDirectory "D:\CP6\artifacts\r2-1.0.0" `
  -ExpectedVersion "1.0.0" `
  -ExpectedWindowsPublisher "CN=CP6 Production Signing" `
  -ExpectedAndroidSignerSha256 "<64-hex-certificate-fingerprint>" `
  -ResolvedSettingsPath "D:\secure\cp6.production.resolved.json"
```

The artifact gate rejects invalid or mismatched MSIX signatures, placeholder
package images, debug-signed or unapproved APKs, cleartext-enabled Android
manifests, non-HTTPS update URLs, mismatched bootstrap hashes, local SQL
Server configuration, unvalidated SQL TLS, missing Redis, and placeholder
secrets. It writes `release-manifest.json` with artifact sizes, SHA-256 hashes,
signer identities, release version, and Git commit.

After deploying the API and publishing the exact artifacts approved by that
manifest, run the remote smoke gate:

```powershell
.\scripts\test-r2-deployment.ps1 `
  -BaseUrl "https://cp6.example" `
  -ReleaseManifestPath "D:\release\release-manifest.json" `
  -OutputEvidencePath "D:\release\deployment-evidence.json"
```

The gate requires healthy liveness, SQL Server, and Redis checks; validates
server clock and Windows/Android bootstrap metadata; downloads APK/MSIX update
content; and verifies size and SHA-256 against the release manifest. When
Windows bootstrap points to AppInstaller, it verifies both the AppInstaller
and its referenced MSIX. `-AllowLoopbackHttp` is restricted to loopback test
servers. `-SkipArtifactDownload` is diagnostic only and is not release
acceptance evidence.

## SQL Server integration tests

The production transaction tests require SQL Server and intentionally do not
fall back to EF InMemory:

```powershell
$env:CP6_TEST_SQLSERVER = "Server=localhost\KOUSQLSERVER;Database=master;Trusted_Connection=True;TrustServerCertificate=True"
dotnet test CP6.Tests/CP6.Tests.csproj --filter FullyQualifiedName~WmsProductionSqlServerTests
```

The fixture creates a uniquely named temporary database, migrates it from
empty to the latest schema, executes the tests, and drops it afterwards. The
suite covers the seven serial lifecycle operations, controlled conversion and
downgrade prevention, same-number serials on different products, default LPN
mixing denial, LPN cycle prevention, idempotent whole-tree movement, and
pack/split/merge/unpack integrity.

## OpenAPI contract check

For contract-only startup, set:

```powershell
$env:Startup__SkipDatabaseInitialization = "true"
$env:Startup__SkipHostedServices = "true"
```

Start the Web API in Development and run
`CP6.Client.Api/scripts/check-openapi-client.ps1`. CI performs this check and
fails if the client-facing Swagger surface drifts.
