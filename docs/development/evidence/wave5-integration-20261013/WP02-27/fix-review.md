# WP02-27 targeted fix review

**ACCEPT** — final candidate `c105036f166928b7c165f182f99f3147ffbb0d58`. The prior P2 and both mandatory Architecture failures are resolved. No new blocking finding in this correction scope.

Original candidate `0057261a9b3506484afcc981f513381b9268da81`; corrected source freeze `7c59256e13c217fc0e8a66b8f3f46905222fdcbc`. HEAD is exact and clean. This review is limited to the correction and its affected evidence; it does not repeat the original complete review.

## Correctness and guard preservation

Exactly three source/test files changed. Schema line 666 replaces only `.+[^/]$` with `.*[^/]$`. The final required non-slash character still rejects empty/trailing slash, and the absolute-path, backslash, dot/parent-segment and double-slash guards are unchanged. The semantic API legal input is not narrowed.

The 14 new cases include 3 valid and 11 invalid paths. Valid cases create the requested actual fixture ZIP entry and call Evaluate before actual JSON Schema evaluation; invalid cases require semantic and schema rejection. SchemaRegistry is isolated per case. Architecture lines 184 and 423 only add the authorized capabilities/v1 Include/PackagePath beside release/v1. Original dependency independence, Release ownership, other-package constraints and production safety assertions remain.

All other delta changes are new evidence plus two clearly labeled historical-report header pointers in DELIVERY.md/REVIEW.md. Their original bodies and prior raw evidence are preserved. Source after the correction freeze is unchanged; no production C# validator/CLI, trust root, dependency or workflow changed.

## Actual evidence

| Run | Result | Meaning |
|---|---:|---|
| coord-schema-path-red | 0/14; 14 failed | Initial x bug plus 13 fixture SchemaRegistry errors; retained, not used as effective RED |
| coord-schema-path-red-isolated | 13/14; 1 failed; 0 skips | Same final test source; old regex; only x fails schema assertion after archive/API acceptance |
| coord-schema-architecture-green / Release | 127/127; 0 failures/skips | Exact 14 regressions included and pass; original required Release filter retained |
| coord-schema-architecture-green / Architecture | 98/98; 0 failures/skips | Both original failed guard tests pass |

TRX individual outcomes, counters and artifact hashes were independently read and verified. GREEN ran 2026-10-13T13:41:08.342374Z–13:41:59.046579Z with HEAD still on the original candidate and corrected uncommitted bytes. All 262 before/after source hashes match final working source. Pack ran from the correction commit with the same source snapshot. Portable PDB document hashes match both changed test files. No test, build or package operation was rerun by this reviewer.

All **41** retained new evidence files and **7** local-only archives match inventory sizes/hashes. Every new archive matches matrix and consumer-lock identity, actual nuspec ID/version/source/repository; both actual Release Schema entries equal final raw source bytes.

New version: `0.10.3-wp02.27.g7c59256e13c217fc0e8a66b8f3f46905222fdcbc.20261013134250`. Source: `7c59256e13c217fc0e8a66b8f3f46905222fdcbc`.

| Package | Actual SHA-256 |
|---|---|
| CP6.Platform.Abstractions | `e89880bae01bc6e94bad7cdb4b25917fe08ca0f3656793b3457c0036bfe290de` |
| CP6.Platform.AspNetCore | `b4190c5dbcf7e5972d59e70e1ef6ae5d026172f1c331ec4b7f06ae9501ba2227` |
| CP6.Platform.Contracts | `868def2318528f80cf1611242af992c72ffbf1497d0079af102856f5a17e5a12` |
| CP6.Platform.Deployment | `b769ca35159456462fa6c72af68ad17797d7123405a276caaf937cc0202a338e` |
| CP6.Platform.EntityFramework | `12f2f029568b59d364e2e1fef2535f0e3fa313dc784d656c77dcdf5eabdc59bf` |
| CP6.Platform.Messaging | `fd439f96bfbdb4a4ebb922249fafa5d5dae2e26627d179fa4fe2622190551c8d` |
| CP6.Platform.Release | `526eddb7a07e1b44ba3ba65077ed10cac43a905f0565fd5fcd1985d1831a56ea` |

New Schema hashes:
- `contracts/release/capabilities/v1/capability-matrix.v1.schema.json`: `54aeabc581130e2119597a44a9b72953ad5fc0db1b3ef282904e108acd9975a1`
- `contracts/release/capabilities/v1/consumer-lock.v1.schema.json`: `e8868ed248000d2ec253d91819af61a175faa65a172011f45f39fbf0fe3c70fa`

## Formal reuse and integration boundary

The original seven 0.10.2 archive bytes still match their recorded hashes. Their seven retained Current calls show exit0, no timeout and identityCheckedSuccess at **2026-10-13T12:46:51.582Z**. No signature/network execution was repeated. The correction leaves formal validator/CLI source, external dependencies and historical policy/publication/certificate evidence unchanged.

Current rebuilt Release/ReleaseTool DLL hashes differ from the historical run; this is recorded explicitly in JSON. The reused result remains the old archive verification at its original time, and must not be labeled a new run or applied to the new local archives. Other historical-source byte changes are traceable line-ending differences; the three correction files are separately identified.

**New packages remain NOT_PUBLISHED / NOT_FORMALLY_TRUSTED.** Compatibility rows are empty and all six adoption stages remain Unknown. WP02-02 remains BLOCKED. Native profile/full API/runtime and real Main/CRM adoption remain unproven. Coordinator integration, fresh workflow preflight, remote-main containment and Issue60 closure are still required. No source/git/network/database/Actions mutation was performed here.

## Exact source hashes

| File | Git-byte SHA-256 | Working-byte SHA-256 |
|---|---|---|
| `contracts/release/capabilities/v1/capability-matrix.v1.schema.json` | `54aeabc581130e2119597a44a9b72953ad5fc0db1b3ef282904e108acd9975a1` | `54aeabc581130e2119597a44a9b72953ad5fc0db1b3ef282904e108acd9975a1` |
| `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs` | `2d3102c1604e8abb5e02ef20b1dc0a7fae3a93cd107934025329e18a015cddc6` | `732986fe1270e4909a22fb8cab04710f6ff157b847337b983b642e4d3365176f` |
| `tests/CP6.Platform.ReleaseTests/CapabilitiesV1/CapabilityValidationTests.cs` | `3beba66b14b044ee082c364d827931d75b321ff296775715eb932fbdf733bd34` | `1863f669617e75313fb732eec8688eda015663fa03df30ac7b6aa6c3572515cb` |

Full delta inventory, individual test results, actual archive identities and historical reuse differences are in `WP02-27-fix-review.json`.
