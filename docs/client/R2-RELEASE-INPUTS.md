# R2 pilot release inputs

This checklist contains the environment-specific decisions required before
the first signed pilot build. Record only references, owners, and approval
status here. Store passwords, private keys, tokens, and connection strings in
the deployment secret store, never in this document or source control.

## 1. Signing and update channel

| Input | Requirement | Status |
| --- | --- | --- |
| Windows code-signing certificate | CurrentUser/LocalMachine certificate store, accessible private key, Code Signing EKU, trusted by every managed Windows device | Pending |
| Windows certificate subject | Becomes the MSIX `Publisher`; changing it later changes package identity | Pending |
| RFC 3161 timestamp URL | HTTPS and reachable from the release runner | Pending |
| MSIX download URL | Final HTTPS URL ending in `CP6.Desktop.msix` | Pending |
| AppInstaller URL | Final HTTPS URL ending in `CP6.Desktop.appinstaller`; server must support GET/HEAD and return Content-Length | Pending |
| Windows package images | Approved `StoreLogo.png`, `Square150x150Logo.png`, and `Square44x44Logo.png` | Pending |
| Android release keystore | Protected, backed up, and stable for the full application lifetime | Pending |
| Android key alias | Release alias, not a debug key | Pending |
| Android signer fingerprint | Approved SHA-256 certificate digest used by the artifact gate | Pending |
| APK download URL | Final HTTPS URL for the signed APK | Pending |

After signing, record artifact hashes in the deployment change record and set:

- `Security:NativeClient:Windows:LatestVersion`
- `Security:NativeClient:Windows:MinimumVersion`
- `Security:NativeClient:Windows:DownloadUrl`
- `Security:NativeClient:Windows:Sha256`
- `Security:NativeClient:Android:LatestVersion`
- `Security:NativeClient:Android:MinimumVersion`
- `Security:NativeClient:Android:DownloadUrl`
- `Security:NativeClient:Android:Sha256`

## 2. Production service configuration

| Input | Requirement | Status |
| --- | --- | --- |
| Public API base URL | HTTPS URL reachable by Web, Windows, and warehouse Wi-Fi devices | Pending |
| Allowed hosts | Exact API host names under `AllowedHosts`; no global or localhost wildcard | Pending |
| TLS trust model | Trusted CA, or approved self-signed public key imported and pinned on every managed device | Pending |
| SQL Server | Non-local production connection with transport encryption, certificate validation, migration/runtime accounts, encrypted backup, and tested restore | Pending |
| Redis/distributed cache | Non-local TLS connection (`ssl=true`) shared by all API instances for one-time native SSO grants and distributed session behavior | Pending |
| JWT secret | Random deployment secret, minimum 32 characters | Pending |
| Web origin allowlist | Exact production origins under `Cors:AllowedOrigins` | Pending |
| SSO public/frontend URLs | Exact external callback and landing origins when SSO is enabled | Pending |
| OIDC provider | Client ID/secret, issuer, scopes, and registered Web/native callback flow | Pending |
| SMTP | Required if email OTP is enabled; sender, host, credentials, and TLS policy | Pending |
| RabbitMQ/Kafka | Production endpoints and credentials when their workers are enabled | Pending |
| Observability | Central redacted logs, alert route, metrics retention, and on-call owner | Pending |

No production deployment may use `TrustServerCertificate=True`, localhost
service endpoints, the placeholder JWT/RabbitMQ values, or a certificate-error
bypass.

When `ASPNETCORE_ENVIRONMENT=Production`, the API validates these settings
before registering application services or listening on a port. It fails fast
if required infrastructure, HTTPS origins, authentication safeguards, native
client versions/download URLs/hashes, or enabled email OTP delivery are not
production-safe. Validation errors name configuration keys only and never
include secret values.

## 3. Pilot warehouse definition

| Input | Requirement | Status |
| --- | --- | --- |
| Tenant and warehouse | One pilot tenant and one warehouse code | Pending |
| Zones/areas | Exact areas assigned to supervisors, dispatchers, operators, and auditors | Pending |
| Pilot users | Named owners for the four standard roles and emergency takeover | Pending |
| Devices | Ten Android devices plus Web/Windows dispatch consoles; shared/personal mode decided per device | Pending |
| Scanner mix | HID, camera, and/or Android broadcast models to validate | Pending |
| Barcode seed file | Product, lot, location, UOM, and conversion aliases preflighted before import | Pending |
| MOVE scenarios | 1000 representative moves, including partial, pause/takeover, conflict, weak network, and exception cases | Pending |
| Label printers | Printer IP/USB mapping, language (ZPL/TSPL/PDF), media size, and approved templates | Pending |
| Serial/LPN scope | Products and container types approved for controlled conversion after R2A acceptance | Pending |

## 4. Operational approvals

- Database restore rehearsal proves RPO at most five minutes and RTO at most
  one hour.
- Security approves certificate distribution, device activation, session
  revocation, PIN lockout, log redaction, and scan-retention policy.
- Warehouse owner approves reservation, partial completion, exception, and
  takeover procedures.
- Support owner can disable feature flags, stop new v2 task creation, and keep
  compatible clients available for already-started tasks.
- Pilot exit requires two continuous weeks, at least 1000 MOVE tasks, zero
  duplicate stock transactions, zero lost stock, and no unreconciled
  serial/LPN aggregate.

## 5. Candidate-build evidence

Attach these outputs to each release change:

- NuGet and npm vulnerability reports with zero known vulnerabilities.
- Server and client unit/integration test summaries.
- Web type-check, unit test, and production build summaries.
- EF pending-model-change result and the migration script reviewed by the DBA.
- SignTool verification for MSIX and `apksigner verify --print-certs` for APK.
- SHA-256 hashes for MSIX, AppInstaller, and APK.
- Post-deployment smoke evidence for health, bootstrap metadata, and remote
  artifact size/SHA-256 verification.
- Clean-device install, upgrade, minimum-version block, and rollback evidence.
