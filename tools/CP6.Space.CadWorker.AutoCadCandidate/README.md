# AutoCAD Core Console candidate Worker

This is the runnable, CAD-only server half of the CP6 remote Worker protocol.
It accepts native DWG over mutually authenticated HTTPS, stages and verifies
the complete source hash before conversion, invokes
`SpaceCadConverterContractRunner`, returns CAD IR only, and fails the request
if its per-attempt raw-data directory cannot be deleted.

It is a **candidate**, not an approved production Provider. Do not enable the
Web API registration until the exact AutoCAD version and Worker release have
passed ADR-0001 qualification on the authorized golden dataset and the
licensing, security, data-region, retention/deletion, identity, certificate,
and Site approvals are recorded in the deployment-owned approval Manifest.

Required deployment settings:

- `CP6_SPACE_CAD_LISTEN_URL` — absolute HTTPS origin without credentials,
  path, query, or fragment.
- `CP6_SPACE_CAD_CLIENT_CERT_SHA256` — pinned client certificate SHA-256.
- `CP6_SPACE_CAD_ACCORECONSOLE_PATH` — approved `accoreconsole.exe` path.
- `CP6_SPACE_CAD_WORK_ROOT` — dedicated encrypted ephemeral volume.
- Standard Kestrel server-certificate configuration; secrets stay outside the
  repository and command line.

Optional limits are `CP6_SPACE_CAD_CONVERSION_TIMEOUT_SECONDS` (default 300)
and `CP6_SPACE_CAD_MAX_CONCURRENCY` (default 1, maximum 4). Network egress,
service identity, filesystem ACLs, encryption, and volume destruction remain
deployment controls and must be proven by the approval Manifest.
