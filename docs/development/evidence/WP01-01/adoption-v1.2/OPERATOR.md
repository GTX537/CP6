# Operating the formal local acceptance adapter

This package is `ADOPTION_PENDING`, with `actual_adoption=false`. No real policy, signing key, authority registry, authorization, execution attestation or business configuration is installed. The candidate manifest is the object a future authorized decision must bind. A version string without `-proposed` does not activate it. Old `adoption.json` and the 30/48 missing original inventories remain immutable history.

The trusted operator controls the reviewed Python process, environment, Git/gh/OpenSSH binaries and protected external policy/key files. Repository authors control all candidate inputs. The adapter does not defend against an administrator replacing the verifier, its environment or trusted keys. Provisioning policy and exercising business approval are separate from this implementation task. See [PLAN-AND-TRUST.md](PLAN-AND-TRUST.md) for the threat model.

`manage_roadmap.py` reads optional `acceptance_basis` from the ledger. The shape is:

```json
{
  "manifest": {
    "path": "docs/development/evidence/WP01-01/adoption-v1.2/manifest.json",
    "sha256": "<exact raw SHA-256>",
    "bytes": 2570,
    "representation": "RAW_FILE_BYTES"
  },
  "runtime": {
    "PLM-ITER-01": {"path":"<report path>","sha256":"<raw SHA-256>","bytes":123,"representation":"RAW_FILE_BYTES"}
  }
}
```

The lengths above illustrate the protocol; always compute actual bytes. There is no active selection in the real ledger. `--basis-report <path>` writes the predicates used by the CLI without changing task or target states. Selecting a candidate cannot create trust. The only new targets permitted are `PLM-ITER-01` and `PLM-REL-01`; other 123 targets retain their original checks. The original inventory metric counts actual original inventories only.

Set `CP6_ACCEPTANCE_TRUST_POLICY` only in the trusted operator process to an absolute, access-controlled JSON file outside every Git worktree. Policy, signers and environment must not be writable by untrusted package authors. An illustrative schema, deliberately without usable identities or locations, is:

```json
{
  "schema": "CP6-ACCEPTANCE-TRUST/1",
  "authority": {
    "repository": "<independently governed owner/repository>",
    "path": "<current authority registry JSON path>",
    "principal": "<authorized approval attester>",
    "identity_root": "<fixed identity namespace directory>",
    "allowed_signers": "<absolute protected external OpenSSH allowed_signers file>"
  },
  "execution": {
    "repository": "<independently governed owner/repository>",
    "path": "<current execution registry JSON path>",
    "principal": "<independent execution attester>",
    "allowed_signers": "<absolute protected external OpenSSH allowed_signers file>"
  },
  "repositories": {"Main":"<actual owner/repository>","Platform":"<actual owner/repository>","CRM":"<actual owner/repository>"}
}
```

Registry administrators publish raw UTF-8 JSON plus detached OpenSSH signature at the same path with `.sig` appended. Namespaces are `cp6-acceptance-authority-v1` and `cp6-acceptance-execution-v1`. The adapter calls `ssh-keygen -Y verify` with the externally configured principal and allowed-signers file. It never creates production keys or executes downloaded code. The tests create temporary synthetic keys and remove them at test teardown.

Each run resolves registry `main` through `gh api`, reads JSON and signature at that exact full commit, validates their signature, and checks `issued_at <= now < expires_at`. A registry validity window cannot exceed 24 hours. It resolves fresh state on each invocation; a single invocation keeps one immutable registry snapshot per role and rechecks its expiry on each use. Changes after the snapshot are observed on the next invocation. Operators must publish revocations promptly and choose a suitably short window; this checker is not a continuous authorization monitor. Missing credentials, HTTP errors, unknown repositories, timeout, absent signatures and unavailable main are failures, not evidence of absence or permission to continue.

The authority registry schema is `CP6-ACCEPTANCE-AUTHORITY-REGISTRY/1`. It contains `issued_at`, `expires_at`, `identities`, `adoptions` and `scenarios`. Every active record has `effective_at`, `expires_at`, `revoked:false`; times require an explicit timezone. Identity entries bind `identity`, `version`, `manifest_sha256`. Adoption records additionally require:

| Field | Meaning |
|---|---|
| `id`, `identity`, `version`, `manifest_sha256`, `targets` | Unique adoption ID, append-only basis identity, exact version/bytes and the two exact targets |
| `action`, `state`, `actual_adoption` | `ADOPT_PROSPECTIVE_BASIS`, `ADOPTED`, actual boolean `true` |
| `approved_by`, `authorization_source:{uri,sha256}` | Real authorized actor and independently checked original authorization source; technical development permission is insufficient |
| `delivery:{repository,commit,path}` | Main repository, exact delivered commit and manifest path; adapter verifies current remote-main ancestry and downloads exact manifest bytes at that commit |
| `identity_source:{commit,path,sha256}` | Immutable identity JSON in authority repository: path is operator `identity_root` plus `/` plus SHA-256 of the exact UTF-8 basis identity plus `.json`; commit is its sole creation commit |

The authority custodian must validate actual authority and delegated scope against the original authorization source before signing; the adapter trusts that independently provisioned attester for this organizational fact. Git authorship or a signature by an unrelated principal is insufficient. Identity records must never be reassigned or overwritten: corrections append a new identity/version. The checker rejects alternate record-chosen paths, duplicate identities, identity-to-version/hash changes, mismatched identity bytes and absent ancestry. At the exact observed authority-main SHA it queries the deterministic path's GitHub history (`per_page=2`) and requires exactly one change, the declared creation commit. That commit must have at most one parent and report this path as `added`, never modified/renamed; unknown, unavailable, truncated or ambiguous creation evidence fails closed. It reads the current identity bytes at that same main SHA and compares the complete identity/version/manifest hash/two-target object. This independently rejects reassigning the same identity even if a new registry row was signed. Operators must keep this namespace and the authoritative repository history stable/protected; changing the external trust root or rewriting authority history remains outside this local checker threat boundary. The signed registry retains identity history and revoked records.

A scenario record is scoped to `id`, `target`, `basis_version`, `manifest_sha256`, `state:APPROVED`, the exact ordered `obligation_ids`, `required_evidence_types`, a hashed `configuration` reference and `execution_scope:{owner,consumer}` containing the exact real authority/consumer identities. The configuration bytes must declare `state:CONFIGURED`; the signing authority must verify the actual values and their applicability, not fill unknown values merely to satisfy this tag. The complete basis and every source assertion remain required independently of scenario partitioning.

Each approved scenario `edges` entry has a declared planning edge `id`, a nonempty applicability `reason`, `provider_scope` and `consumer_scope` (each exact `{owner,consumer}`), and `contract:{profile,version,sha256,artifact}`. `artifact` is an exact byte reference; its hash must equal the selected contract/package hash. All 1256 planning edges remain in the immutable graph. Approval selects the actual runtime edges for a specific scenario; it cannot delete graph edges or remove manager `TASK_DELIVERY` dependencies. The authority must verify that the selected edges exhaust actual applicable dependencies. The checker verifies the selected set exactly and rejects extra/missing/duplicate observed edges.

A runtime report binds `target`, `basis_version`, `manifest_sha256`, the exact full target `obligations` array and unique `scenarios`. Each scenario includes `id`, nonempty `executions` (byte references), and `edges`. An observed edge has `id`, the exact approved `contract`, `provider` and `consumer` execution byte references. Every obligation must occur in at least one approved scenario. This does not require all 1256 planning edges to execute for every scenario.

Each execution JSON requires `kind:BUSINESS_RUNTIME`, `state:PASS`, `synthetic:false`, `run_id`, `command`, `environment`, `observed_at`, `repository`, `source_sha`, `owner`, `consumer` and nonempty exact `logs` byte references. It must also bind `basis_version`, `manifest_sha256`, `target`, `scenario_id`, exact ordered `obligation_ids`, exact approved `contract_bindings` and `configuration`. The real source commit must be an ancestor of the independently queried current product `main`. Provider and consumer reports must belong to their declared repositories and approved identity scopes. Raw log bytes and selected artifact bytes must be available and match hashes; a package reference or JSON PASS alone is insufficient.

The separately signed execution registry uses schema `CP6-ACCEPTANCE-EXECUTION-REGISTRY/1`. Its `executions` records contain unique `id`, `state:VERIFIED_BUSINESS_RUNTIME`, current validity/revocation fields, `report_sha256`, `report_bytes`, `verified_raw_logs` equal to exact report log references, and `origin` equal to the report's `run_id`, `command`, `environment`, `observed_at`, `repository`, `source_sha`, `owner`, `consumer`, `kind`, `state`, `synthetic`. The attester must independently observe the actual execution and raw logs, the real environment/configuration, assertions and actual provider/consumer package use. It must not sign an uploaded self-declared PASS without checking those facts. The report hash binds all additional scenario/obligation/profile/configuration fields. Signing authority cannot turn the synthetic tests here into business evidence.

Historical sources use `RAW_FILE_BYTES` or `GIT_BLOB_BYTES` explicitly. Versioned historical inputs are read with `git show <full commit>:<contained path>`; raw archive sources are read only from the checkout or operator-specified `CP6_ACCEPTANCE_ARCHIVE_ROOTS` (OS path separator, `;` on Windows). Path traversal, absolute paths and symlink escapes are rejected. Missing materialized archives fail coverage closed; they are distinct from permanently missing original 30/48 inventories. This delivery used `D:/CP6` as the explicit read-only archive root and did not copy archive originals into the candidate.

For a read-only candidate inspection before adoption, run `inspect_candidate.py --root <checkout> --archive-root <readonly archive root>` with no trust policy configured. Expected result is `inventory_coverage_valid:true`, `basis_adoption_valid:false`, both `target_runtime_valid:false`, and `INDEPENDENT_VERIFIER_UNCONFIGURED`. Coverage is a structural fact, not business approval or execution. Unavailable source bytes instead produce coverage false with a precise source error.
