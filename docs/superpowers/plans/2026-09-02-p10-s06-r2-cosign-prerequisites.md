# P10 S06 R2 and cosign Prerequisites Implementation Plan

> **Execution mode:** Run the tasks below sequentially. Stop before any
> candidate, Locator, package publication, image push, or deployment.

**Goal:** Bootstrap the zero-cost, least-privilege external inputs that P10 S04
and S06 require: a protected public-CP6 environment, two distinct cosign
identities, one canonical pinned trust policy for the real Cloudflare R2
authority, and named R2 publisher/consumer credential slots.

**Architecture:** `GTX537/CP6` owns the protected publisher environment and the
reviewed public trust instance. Locator and OCI signing use separate encrypted
cosign private keys. The Cloudflare parent publisher credential is limited to
Object Read & Write on `cp6-release`; a workflow later derives a 15-minute
session credential limited to the approved actions and prefixes. Consumers use
a separate permanent Object Read Only credential. Only public keys and the R2
authority are committed. All private values remain environment secrets.

**Tech stack:** GitHub Environments and Secrets, Cloudflare R2 Standard/S3 API,
cosign v3.1.3, PowerShell 7, .NET 8 `CP6.Platform.ReleaseTool`, canonical
`cp6-deterministic-json-v1`.

## Fixed values and boundaries

| Item | Exact value |
| --- | --- |
| Repository | `GTX537/CP6` (public) |
| Environment | `p10-platform-candidate` |
| Deployment branch policy | custom policy allowing only `main` |
| Required reviewer | the repository owner; `prevent_self_review=false` |
| Storage authority | `cp6-release-r2-v1` |
| Provider/bucket/jurisdiction | `cloudflare-r2` / `cp6-release` / `default` |
| Allowed prefixes | `candidates/platform/`, `objects/sha256/` |
| Public trust instance | `eng/p10/trust/pinned-trust-store.v1.json` |
| cosign binary | official v3.1.3 Windows amd64 asset, SHA-256 pinned before use |
| Key purposes | one `candidate-locator`, one `oci` |
| Key profile | ECDSA P-256 PKIX `PUBLIC KEY` PEM |
| Publisher session | 900 seconds, local JWT signing |
| Session actions | `PutObject`, `HeadObject`, `GetObject`, `GetBucketLocation` |
| Formal publishing/deployment | explicitly excluded |

Environment secret names are fixed as follows:

```text
P10_LOCATOR_COSIGN_PRIVATE_KEY
P10_LOCATOR_COSIGN_PASSWORD
P10_OCI_COSIGN_PRIVATE_KEY
P10_OCI_COSIGN_PASSWORD
P10_R2_PUBLISH_ACCESS_KEY_ID
P10_R2_PUBLISH_SECRET_ACCESS_KEY
P10_R2_CONSUMER_ACCESS_KEY_ID
P10_R2_CONSUMER_SECRET_ACCESS_KEY
```

The first four values are independently generated. The publisher credential is
an R2 Account API token scoped to Object Read & Write on only `cp6-release`.
The consumer credential is the already-created R2 Account API token scoped to
Object Read Only on only `cp6-release`. Neither credential may use the existing
unrelated all-bucket administrative user token.

## Task 1: Create and verify the protected environment

- [x] Reconfirm `GTX537/CP6` is public and the owner identity is unchanged.
- [x] Create `p10-platform-candidate` with one required reviewer,
  `prevent_self_review=false`, and custom deployment-branch policies.
- [x] Add exactly one branch policy for `main`.
- [x] Read the environment back through the GitHub API and record only policy
  metadata; do not create repository-level fallbacks.

The environment may exist before workflows reference it. This task creates no
candidate and grants no deployment authority.

## Task 2: Generate and pin two distinct cosign identities

- [x] Download the official cosign v3.1.3 Windows amd64 asset into a
  task-specific tool directory and verify SHA-256
  `9fe59be0eca1271873ce019061335eb1ac419b7059202e797828467ddabe33be`.
- [x] For each purpose, generate a cryptographically random password and a new
  cosign key pair in a random task-specific staging directory.
- [x] Stream the encrypted private-key bytes and password to the exact
  environment secrets through standard input. Never put a secret in command
  arguments or print it.
- [x] Parse each public key, require ECDSA P-256, canonicalize it to LF-delimited
  PKIX PEM without a trailing newline, and derive
  `keyId=sha256(lowercase SHA-256(DER SPKI))`.
- [x] Prove the two key IDs differ, then remove the staging directories and scan
  the task/repository trees for `.key`, private PEM, or password residue.

If either secret write is not confirmed, keep the trust policy uncommitted and
rotate both values for that purpose before retrying.

## Task 3: Create the canonical public trust instance

- [x] Add `eng/p10/trust/pinned-trust-store.v1.json` with policy version 1,
  minimum accepted version 1, an empty historical-version set, the fixed real
  R2 authority, and both public keys sorted by key ID.
- [x] Use a two-year metadata validity window beginning at bootstrap time. A
  later rotation increments the policy version; revoked keys remain in policy
  with an explicit UTC time and reason.
- [x] Canonicalize the JSON using the exact current-main
  `CP6.Platform.ReleaseTool`, then validate it with `validate-trust`.
- [x] Confirm the committed file contains no secret, private key, temporary
  path, signed JWT, access key, or presigned URL.

## Task 4: Bind the R2 credentials without broadening authority

- [x] Store the existing bucket-specific read-only access-key pair in the two
  consumer environment secrets. Confirm names only.
- [x] Create one new Account API token named
  `cp6-p10-publisher-parent-v1`, permission Object Read & Write, applied only to
  `cp6-release`, no IP filter, and no broader administrative permission.
- [x] Store its access-key pair in the two publisher environment secrets and
  confirm names only. Do not store or reuse the unrelated all-bucket admin
  token.
- [x] Locally derive a 900-second session credential whose JWT names only the
  fixed bucket, four allowed actions, and two allowed prefixes. Verify the JWT
  claims without printing the credential and perform a non-mutating
  `GetBucketLocation` preflight.
- [x] Do not upload a diagnostic object. Conditional create and post-commit
  behavior are exercised only by the future S06 publisher workflow.

## Task 5: Verify, review, and land the prerequisite commit

- [x] Run repository documentation/contract checks that cover the changed
  files, plus the exact-main Platform trust validator.
- [x] Review the complete diff against `main`, run a secret/private-key scan,
  and stage only this plan and the public trust policy.
- [x] Commit, push, open a PR, wait for all required checks, and merge normally.
- [x] Verify the merge commit on remote `main` and wait for the exact-main
  workflow result before using the trust hashes as an S04 prerequisite.

## Task 6: Hand off the remaining external gate

After all automated steps pass, record:

- environment protection metadata;
- the two public cosign key IDs and trust-policy SHA-256;
- R2 authority ID, bucket, jurisdiction, and approved prefixes;
- the eight confirmed environment secret names; and
- the non-mutating temporary-credential preflight result.

Do not set Platform `S04_EXTERNAL_PREREQUISITES_READY=true` until this public
trust commit is on `main`, the R2 publisher token exists, the permanent
read-only pair is bound, and the separate two-runner RFC3161 gate is complete.
This plan does not authorize formal NuGet publication, S06 candidate/Locator
publication, GHCR changes, production deployment, or deletion/overwrite of any
cloud object.

## Outcome (2026-09-03 UTC)

- PR #80 head `17dc0407f58750d729d7207dfa0f59f79182a4c5` merged normally as
  `main@da54076861b30e710a3eceb9e08023fbc6f9ff87` after all six PR checks
  passed. Exact-main runs 33706881271 and 33706881477 passed the real-SQL,
  Android, Windows, and Web jobs.
- `p10-platform-candidate` has required reviewer `GTX537`,
  `prevent_self_review=false`, custom branch policies, and exactly one `main`
  branch policy. All eight fixed Environment secret names are present; no
  private value is recorded in Git.
- The canonical trust-policy SHA-256 is
  `0a6e72951c196e612a593cc8831e294bb538c9ba8a79eada4538771a3811d8e9`.
  The locator and OCI key IDs are respectively
  `sha256:9c0fd05b3159651cc2e9138555f32387988c6961889ee00211139e710f1febaa`
  and
  `sha256:eb623d784fc55294e942fa49062477769a34943d5997fdbdd483ad0fb0103c21`.
- R2 authority `cp6-release-r2-v1` remains bound to account
  `30c4a8d1697ffd3de6a1e0a88376607c`, bucket `cp6-release`, jurisdiction
  `default`, and the two fixed prefixes. Live temporary-credential validation
  established that explicit `actions` and `scope` must not be sent together;
  the accepted 900-second request used only the four actions plus path
  restrictions. Its non-mutating `GetBucketLocation` preflight returned 200,
  and no object was uploaded.
- The separate two-runner RFC3161 gate remains pending, so
  `S04_EXTERNAL_PREREQUISITES_READY` remains `false`. No publication,
  cloud-object mutation, or deployment was performed.
