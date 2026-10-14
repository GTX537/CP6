# WP02-25 — independent review of fixes #68 / #69

**PASS for the two corrections and their affected behavior; no new P1/P2 findings.** The corrected candidate can proceed to integration within the original bounded Platform scope. This is a focused follow-up, not a replacement full review or production acceptance.

| Identity | Full SHA |
| --- | --- |
| Platform base | `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0` |
| Original candidate (still CHANGES_REQUIRED) | `095a66bfda8b4df2ee65e920541ce6304e8e66c1` |
| #68 fix | `db728d17d95147be89ab4afd7bbbab9ebe15cbcb` |
| #69 fix | `621f0abab99db5708ccbe46fc592f33333ca08b1` |
| Aggregate tested source | `ae4b56abf88aa40f1ba3bf82f1ef4c83a878b378` |
| Final reviewed candidate | `80f43346e4e94e6d7b402d7b557c685b475ba743` |

Worktree: `D:\CP6.Platform-worktrees\wp02-25-corrected-20261014`. HEAD was clean before and after review. Final candidate adds eight evidence/capture files to the tested source; no source/configuration delta. Compared with the original: 37 files, exactly two production edits, two new test files, one consumer note and 32 correction evidence/capture files. `git diff --check` passed. No public signatures, DTO/wire, package/project/dependency/version or architecture boundary changed.

## Findings and closure

- **R1 / #68 verified fixed.** `OwnedModuleObserverV1.cs:14–25,49` records the observer clock's timestamp before loading bytes. `:94,102–109,120` uses its elapsed time and latches expiry on the evaluator; a UTC correction cannot revive that evaluator. UTC metadata checks remain, and clock exceptions return Unproven without leaking exception contents. Tests at `IdentityClockRegressionTests.cs:19–69` cover early UTC expiry followed by rollback, ordinary elapsed expiry, frozen/different evaluator clocks, freshness before expiry and clock failure. The actual HTTP regression at `:73–103` proves ready 200 → 503 → 503, independent read availability, closed NewEffect availability, Unproven identity and live 200.
- **R2 / #69 verified fixed.** `HealthAdapterV1.cs:32–43` adds `Cache-Control: no-store` to live and identity. Existing startup/ready writing at `:53–58` retains it for 200/503. All four handlers retain the existing authorization policy. `OperationalNoStoreRegressionTests.cs:17–41` sends real Kestrel HTTP requests to all four routes and both dependency-failure 503 paths, asserting status, no-store and JSON content type.

The original `review-WP02-25.md/.json` are unchanged and remain CHANGES_REQUIRED on the original SHA. This report records fixes on the corrected SHA; it does not close either GitHub Issue. Original report hashes: MD `0a3afc06316ff24d765b1ca183fb5c88c0d285258a9fee3821111ce3e334a8e0`, JSON `cbf45438d2518a7e4e6406db3e83f7e62e18b1412f382f48213faf0c35b80437`.

## Evidence independently checked and reused

All execution below is retained implementer evidence, **not a new reviewer execution**. SDK 8.0.424 / runtime 8.0.30; the original review's .NET 10 in-memory reproduction is not substituted for SDK 8 regression.

| Run | Verified result | Exit |
| --- | --- | --- |
| #68 RED | 7 failed / 7; expected stale Match and rollback HTTP 200 | 1 |
| #68 GREEN | 7 passed / 7 | 0 |
| #69 RED | live/identity missing header: 2 failed / 6; 4 controls passed | 1 |
| #69 GREEN | 6 passed / 6 | 0 |
| Aggregate focused run | **69 passed / 69, zero skipped** | 0 |

Evidence root: `docs/development/evidence/WP02-25/20261014T052841Z`. The correction runs are under `coord-corrections/issue-68`, `issue-69`, and `aggregate/focused-01`. Independently parsed TRX counters, test names, failure messages and Kestrel outputs. Aggregate names are exactly 56 original host cases plus 13 added regressions; old host names outside this run are exactly the recorded 44 retained names. The HTTP results come from actual loopback Kestrel handlers, with controlled clocks and local dependency fixtures. They do not execute actual business read/write operations or change the system clock.

- Aggregate TRX SHA-256: `da7c92ed8a72f87fc5f245da306c0be3398282e1ca22ed89a47ba2507bb3d0d5`.
- Aggregate console SHA-256: `7076ae9cb88172b3bcfc88991677eb96ad81808e47e006c22d93f8d1187e4bae`.
- Aggregate source digest: `7210d14fc5eeefeb5c7a2200db5ee41e74697f112589f93bf7b2e10917fab45a`.
- Verified all **263 source/config, 7 resolved dependency and 25 artifact raw SHA-256 values** against current files, zero mismatches; source lists are identical before/after the run. Source content matches final Git candidate after ordinary checkout line-ending handling.
- Each RED/GREEN source list has 262 entries and unchanged before/after inputs; source content matches original-plus-regression for RED and the corresponding fix commit for GREEN. TRX hashes match execution records. Failures remain retained.

Old final-01 evidence remains **255/255 on the original candidate**: Unit 57, Host 100, Architecture 98. Reuse only Unit 57 + Architecture 98 + untouched Host 44 (199 prior cases); the 56 affected host cases were reexecuted within the 69. Reverified original TRX hashes and original 261 source hashes against its worker. Comparing original/current inputs: 240 raw source hashes are unchanged, 19 differ only by LF/CRLF checkout conversion, two production files changed behavior and two tests were added. Therefore reuse is bounded semantic evidence for unchanged portions, not byte-identical corrected binaries and not a newly executed corrected 255-case gate. No public/dependency/project/schema boundary change warrants repeating those unchanged cases.

## Remaining boundaries

No remaining P1/P2 blocker was found in the two fixes. COORD still owns workflow-safe integration, necessary integration smoke, state synchronization, remote main containment and eventual Issue closure. Actual Main/CRM/Owner adoption, trusted package/publication, production SLO and deployment attestation are unproven; the original 32 business acceptance cases remain NOT_RUN. The handler no-store tests use a fixture allow policy; permission regression remains in the affected suite, while middleware-denial cache behavior is outside this handler correction.

No new build/tests/HTTP execution, repository edit, Git mutation, remote write, DB, Actions, package publication or deployment was performed by this reviewer. Only this report and its JSON companion were written in scratch. Detailed commands, run times, hashes, new/retained case names and reuse limits are in `review-WP02-25-fixes.json`.
