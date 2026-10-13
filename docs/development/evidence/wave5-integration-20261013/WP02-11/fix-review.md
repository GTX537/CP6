# WP02-11 targeted independent fix review

**ACCEPT** the two corrections at final candidate **34cf7ab144a02e412de121eee2d9c5083f5a96a8**, relative to the original review of **0f7c69f13a26b36895cd40aca3fc17299e866b70**. No new blocking finding was found. This is a targeted recheck of the reported issues and their impact, not a repeat of the full original task review.

Current integration baseline: **817a2926b0799f70cc87f384967e0883c1943ec5**; original baseline: **9259a45d39bae7e98ca804a74778a588c1ad4619**. The combined tested source is **58e35c36e1987105c144a6dcc1955a524db8ca35**. I verified the final worktree is clean and that only seven WP02-11 evidence/report paths changed after that tested source; no code, tests, contracts, dependency, tool or workflow input changed.

## Issue62 / WP02-11-R1 resolved

`Cp6HttpV2Handler.cs` captures `observedStatus` before body buffering. The retained status now vetoes the complete generic transient expression for non-transient rejection responses. A received 401/403/409/422 therefore cannot be replayed after a body IOException or attempt timeout. A verified uncertain write still queries its original result, but even NotAppliedProven cannot reach replay authorization when the observed rejection makes `transient=false`. An incomplete rejection body does not manufacture Applied/Rejected; terminal failure retains Unknown, original identity, attempts and recorded request outcome. Existing disposal remains intact.

The 24-case regression has 16 target combinations (four statuses x read/write x IO/attempt-timeout) and eight 200/503 controls. The target failures observed three sends instead of one in RED; GREEN verifies one send/no replay authorization for rejection cases while preserving eligible bounded recovery for the controls. Send/query/authorization counts, typed outcome, original identity and disposal are asserted.

## Issue63 / WP02-11-R2 resolved

Both authority calls now enter `CheckAuthorityAsync`; delegate invocation is inside its try, so synchronous throws and faulted ValueTasks are covered. Ordinary faults become the existing safe RecoveryUnavailable outcome; a Polly timeout with no owned timeout provenance remains UnknownTimeoutSource. `EnsureTotalActive` checks actual caller/ancestor cancellation and total deadline before normalizing a callback fault. OperationCanceledException continues through the existing source-aware outer path, preserving cancellation precedence and unrelated-cancellation classification.

The wrapper uses the existing Failure factory, which records exactly the same typed outcome in RequestOptions: verification failure is NotSent/0 attempts, replay-authority failure remains Unknown/1 after querying the same original operation. The captured operation identity is retained, no extra send is admitted, and pending transport responses are disposed by the existing finally block. No public enum/API or permission semantics were added.

The 24-case regression covers 16 verify/replay x synchronous/faulted-task x fault-kind combinations, six caller/parent-caller/total races, and two unrelated-cancellation controls. These match the original finding and test the actual handler with private fixtures.

## Evidence independently checked without execution

| Existing execution | Total | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| issue62 RED | 24 | 8 | 16 | 0 |
| issue62 GREEN | 24 | 24 | 0 | 0 |
| issue63 RED | 24 | 2 | 22 | 0 |
| issue63 GREEN | 24 | 24 | 0 | 0 |
| combined HttpV2 final | 198 | 198 | 0 | 0 |

All five original TRX and log SHA256 values match `coord-final-evidence-check.json`; XML result counts/outcomes match counters. RED messages identify the reported behavior, not a build/discovery failure. For each issue, the sole source change between before-RED and before-GREEN is the handler; the test source matches that issue branch. RED handler bytes match the original candidate and GREEN handler bytes match the issue branch after accounting for declared Git text checkout normalization. Before/after GREEN source/dependency arrays are equal.

Final manifest SHA256 **6f99832ec575b7507ae444bde0c958fbd4155c93ea5aff75ac929c5486861a6b**: I independently rehashed all **249 source/config/lock**, **8 restored dependency**, and **25 compiled artifact** entries against disk, with zero mismatches. Before/after final source and dependencies are identical. Build artifacts legitimately changed from pre-build to post-build; the after values match disk. The listed historical ArchitectureTests assembly is not treated as a new architecture execution. Final TRX SHA256 **379d8e823e746b64466ee9a33e93730c8e907d8b8d83a6ed661c2a2d282551cd**.

The retained bug branches are independent and both contained in final candidate:

- Issue62: `codex/bug-62-http-rejected-body-20261013` at `a52f416b7681a7d67d2852ad19aa689da26daa05`; fix `a09762b60dc974c66bd270504553ab8552658706`.
- Issue63: `codex/bug-63-http-authority-outcome-20261013` at `8395e7506728ff7b2f53e4d5be6da288beeaba75`.

Each starts from baseline817a2926, imports the original candidate and changes only its own HTTP defect/regression. The final corrective HTTP delta is one handler plus two private regression classes. Inherited WP02-07 files are baseline context, not HTTP scope expansion.

## Reuse and integration boundary

The original legacy HTTP/gateway/auth **98/98** and Architecture **98/98** TRX remain genuine historical results, with zero skips. Since these corrections only touch v2 handler internals and private tests, unchanged legacy behavior/signatures/projects/dependencies and existing WP02-11 architecture assertions can reuse those results. They are not executions of final binaries. The newer WP02-07 baseline has its separate coordinator evidence, and root still owns the planned combined-main architecture smoke.

This reviewer ran no tests/builds, network checks, Actions, DB, publication or deployment operations, and made no repository changes. Only these external review files were written. Acceptance closes the two local review findings; it does not claim remote-main inclusion, Issue closure, production Owner/Host adoption or durable receipt/permission/deduplication proof. Original42 business AC remain NOT_RUN.
