# WP02-02 consumption preparation checker

This is **PREPARATION_ONLY**, not an admission or trust verifier. Every invocation
reports `admission: BLOCKED`, `entryGate: NOT_CHECKED` and exits **2**. An identity
match cannot qualify Main, authorize source adoption, satisfy the original 17 PGT
cases, or advance business readiness. The task-local DLL/API probe and Git source
inventory are separate evidence; this checker does not turn those into adoption.

```powershell
python eng/wp02-02/consumption/verify.py --input <independently-approved-input.json> --output <new-task-owned-directory>
python -m unittest discover -s eng/wp02-02/consumption/tests -v
```

Use Python 3.10+ with its standard library. The output's parent must already exist;
the output directory must not exist. The checker writes only `report.json` in that
new directory. It never extracts ZIP members, mutates input, runs subprocesses,
contacts feeds, signs, publishes, changes projects, accesses databases or dispatches
Actions. Keep outputs under the task's `artifacts/wp02-02/consumption/` directory.

## Independent inputs

[`input.schema.json`](input.schema.json) documents the closed v1 input shape.
The executable enforces the shape and cross-field rules without third-party
JSON Schema dependencies. JSON duplicate keys, nonfinite numbers, unknown fields,
missing fields, invalid hashes and unsupported compatibility tokens are rejected.

The manifest is a **caller-controlled declaration**, not authenticated approval.
The operator must obtain its expected repository, commit, package, feed and hash
pins from an independently reviewed fixed dispatch/publication/source record and
record that reference in `approval.reference`. Never populate expected pins by
trusting the artifact under inspection. The checker deliberately has no command
that generates approvals from archives. A malicious self-consistent manifest
cannot produce formal success because there is no formal-success path.

| Field | Meaning |
| --- | --- |
| `schemaVersion`, `mode` | Exactly integer `1`, `PREPARATION_ONLY`. |
| `approval.reference` | Fixed external review/dispatch evidence reference, preparation identity scope only. |
| `approval.mainRepository`, `mainSourceSha` | Exact Main repository and full lowercase commit. Declaration recorded; use independent Git inventory for byte/source verification. |
| `approval.platformRepository`, `platformSourceSha` | Exact producer repository and **package-producing** commit, checked against every nuspec. This is not necessarily latest Platform main. |
| `approval.packageVersion` | One exact version for all seven producer packages. |
| `approval.feedUrl` | Independently pinned expected feed/source string; compared exactly to the observation. |
| `approval.compatibility` | `UNKNOWN`, `INCOMPATIBLE`, or `COMPATIBLE`. The first two explicitly block; the third remains an unverified declaration and cannot qualify a consumer. Actual preparation cases use `UNKNOWN`. |
| `approval.requiredCapabilities` | Exactly `["ctx-binding-v1"]`; no speculative future capability. |
| `approval.runtimePackageIds` | Exact five-package Main runtime closure: Abstractions, AspNetCore, Contracts, EntityFramework, Messaging. |
| `observation.feedUrl` | Actual observed source recorded by the operator. String equality is not live feed authentication or immutable readback. |
| `observation.publication` | Only `LOCAL_ONLY` or `UNVERIFIED`; formal trust cannot be asserted. |
| `observation.packages` | Exact seven producer packages: the five above plus Deployment and Release. Each appears once. |
| Package `path` | Absolute or relative to the manifest's directory; read only. |
| Package `sha256` | Independently expected hash of the complete archive. |
| Package `sha512Archive` | Independently expected canonical base64 SHA-512 of the complete archive bytes. **Not** the NuGet lock `contentHash`: signed-package hash semantics are separate. |
| Package `schemas` | Complete set of packaged `*.schema.json`, each exact archive `entry` plus independently expected source-byte `sha256`. Empty only when no schema files are packaged. No newline/BOM normalization. |

The historical formal `0.10.2` producer is
`fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`; the historical wp02.27 local package
producer is `7c59256e13c217fc0e8a66b8f3f46905222fdcbc`. Independently confirm those
pins from the dispatch evidence when authoring real manifests. `0.10.2` cannot
provide the required ctx capability; the local wp02.27 package cannot supply
formal publication/trust even when its source/schema/archive identities match.
The independent P10 ReleaseVerifier `0.10.1` tool is outside this runtime closure.

## What the checks establish

The checker compares SHA-256 and raw archive SHA-512, one root nuspec ID/version/
git repository/commit, same-version Platform dependencies, and the exact schema
entry set/bytes. NuGet producer dependencies permit the same bare version or
exact bracket range; bounded/floating or other-version ranges are rejected.
This records package metadata; it does not claim dependency resolution happened.
The explicit nuspec namespace allowlist covers the actual `2012/06` and `2013/05`
Microsoft packaging schemas. Unknown roots and mixed element namespaces are
rejected; accepting a known namespace does not relax identity/dependency checks.

The Release capability schema is parsed structurally. It must declare the exact
`ctx-binding-v1` capability at version `1.0.0`, profile `ctx-v1`, and scopes
Background/Http/Message. Text in a description cannot satisfy it. Only S01-09 and
ctx-binding-v1 identifiers are recognized. This is a schema declaration check,
not DLL public API inspection or capability/runtime qualification. A valid
schema from an untrusted archive remains untrusted.

ZIP inspection is bounded: 128 MiB archive, 4096 entries, 32 MiB per member,
256 MiB total expanded, compression ratio at most 200 beyond a 1 MiB floor,
and 4 MiB JSON/nuspec. It rejects unsafe names, duplicate/case-colliding entries,
Windows special names, symbolic links, encryption and unsupported compression.
No member is extracted. XML DTD/entities are forbidden. Schema byte comparison
does not substitute for the producer's complete JSON Schema/semantic tests.

Reports separate source metadata, shape, artifact bytes, trust, consumer, host,
runtime and business. Source/shape/artifact matches use narrow names such as
`NUSPEC_METADATA_MATCHED`; they do not mean clean-source build verification.
Trust is always `BLOCKED`; consumer/host/runtime/business always `NOT_RUN`.
The result records the exact input and checker SHA-256 plus Python version.

## Validation evidence

Tests generate synthetic minimal ZIPs in temporary directories. All passing
identity cases are **synthetic plumbing only** and remain formally blocked.
No fixture simulates a successful signed publication. Tests cover source/feed/
hash/ID/version/dependency/schema mismatch, duplicate/malformed input, unknown
compatibility/capabilities, missing ctx, local-candidate rejection and ZIP/XML
boundaries. CLI tests verify unchanged inputs and refusal to reuse outputs.

Local RED/GREEN attempts and source hashes are retained in task-owned artifacts.
The initial RED used a deliberately incomplete report-only skeleton, yielding
assertion failures rather than import failures. The focused follow-up RED caught
duplicate dependencies and unknown capability declarations. Root delivery
evidence records final hashes, real archive rejection runs and the exact test
attempts; those records must not claim formal consumer success.
