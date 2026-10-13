# WP02-11 concentrated integration review

**Recommendation: REQUEST_CHANGES**; two behavioral blockers, no style-only blocker.

Candidate `0f7c69f13a26b36895cd40aca3fc17299e866b70` against baseline `9259a45d39bae7e98ca804a74778a588c1ad4619`. Worktree `D:/CP6.Platform-worktrees/wp02-11-20261013` was clean at the pinned candidate. All91 changed paths inventoried; production/test C# fully read; large evidence parsed structurally. No tests/builds, repository/Git mutations, network, Actions, DB, package or deployment operations in this review.

## [P2] WP02-11-R1 - Do not retry a received rejection when its body fails

Anchor: `src/CP6.Platform.AspNetCore/HttpV2/Cp6HttpV2Handler.cs:179`; supporting lines [147, 148, 152, 157, 171, 180, 208].

After receiving 401/403/409/422, body IO failure is wrapped as HttpRequestException or body timeout becomes AttemptTimeout. The circuit callback never returns received, so outer response is null. The transient expression forgets the known status and admits another attempt. This violates original HTTP line101 and docs line90. Proven writes can also replay after NotAppliedProven and authorization. Keep observed status independent of completed body and veto generic retries for these rejections, while preserving original-query recovery and Unknown. Existing status tests have complete bodies; body-failure tests use 200. Add status x IO/attempt-timeout read/write regressions asserting one send.

Static control-flow finding; no tests executed in review.

## [P2] WP02-11-R2 - Preserve typed operation outcome on authority faults

Anchor: `src/CP6.Platform.AspNetCore/HttpV2/Cp6HttpV2Handler.cs:210`; supporting lines [97, 138, 188, 204, 245, 247, 252].

A registered authorization adapter may throw synchronously or return a faulted ValueTask after a sent write and NotAppliedProven query. VerifyBindingAsync and AuthorizeReplayAsync sit outside transport catches; outer try only catches OperationCanceledException. Ordinary faults escape raw and leave RequestOption outcome absent, losing typed Unknown/attempt count/original identity. No authority contract forbids ordinary faults. Normalize expected faults into a safe typed outcome without another send, preserve caller/deadline precedence, and retain NotSent on pre-send verification failure. Original HTTP lines73/97 and docs108 require retained operation state. Add synchronous/asynchronous exception regressions at verify/replay, including cancellation and timeout races.

Static control-flow finding; no tests executed in review.

## Independently verified evidence

Manifest SHA256 `5147dd29f33bfbcc8f82e4de9c32b381eb3e700a7066fa4f8d8ef82f64565341`: 236 source +8 dependency +25 compiled artifact hashes match current disk. Pre-GREEN/current source and dependencies match. Current HttpV2 TRX150/150 passed, zero skipped. Original legacy98/98 and architecture98/98 results remain historical and reusable under the stated scope. All16 TRX files parsed with actual outcomes/counts, failed development records retained. Console summaries match reported gates.

Since original gates only new v2 deadline/handler and private v2 tests changed; old implementation/tests, overload signatures, project/dependency inputs and architecture boundaries unchanged. Architecture source-text restrictions inspected. Not claimed to run current binaries. New findings need affected regressions only.

Changed since original gates:
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6HttpDeadline.cs`
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6HttpV2Handler.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/LoopbackTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/ReviewRegressionsTests.cs`

All five source copies match exact original archive bytes or fixed Main Git blobs. Original42 AC objects match pinned foundation exactly; 38 library PARTIAL_PASS and4 NOT_RUN; all42 business NOT_RUN. All267 test references resolve to actual Passed TRX results.

## Boundaries and next action

- Only18 additive lines in old ProblemDetails source; legacy resilience source/defaults/auth/gateway unchanged.
- Explicit opt-in Disabled default, no production binding or real authority supplied; fixture results do not prove remote Owner atomicity.
- Request identity/body/header snapshot, query-before-write-authority, inherited monotonic deadline/cancellation, parser3 date forms/overflow, one jitter/max(S,L)/cap/admission, safe writer limits have substantive coverage.
- No shared contract/dependency/DB/workflow/real Main/CRM adoption changes.
- Original42 business AC remain NOT_RUN; real Owner final authorization/receipt/retention/production exactly-once remain unproven.

Correct both paths and run focused regressions, then recheck changed logic/impact. A mandatory gate failure blocks merge. No need to relabel or repeat unaffected historical results. Root coordinates integration and verifies remote main.

## Complete changed paths

- `docs/WP02-11-HTTP-V2.md`
- `docs/development/evidence/WP02-11/.gitattributes`
- `docs/development/evidence/WP02-11/AC-SCOPE.md`
- `docs/development/evidence/WP02-11/DELIVERY.md`
- `docs/development/evidence/WP02-11/PLAN.md`
- `docs/development/evidence/WP02-11/REVIEW.md`
- `docs/development/evidence/WP02-11/ac-scope.json`
- `docs/development/evidence/WP02-11/audit_workflows.py`
- `docs/development/evidence/WP02-11/current-evidence-check.json`
- `docs/development/evidence/WP02-11/final-scope.json`
- `docs/development/evidence/WP02-11/implementation/RUNS.md`
- `docs/development/evidence/WP02-11/implementation/after-final-fingerprints.json`
- `docs/development/evidence/WP02-11/implementation/after-parent-cancellation-fix-fingerprints.json`
- `docs/development/evidence/WP02-11/implementation/after-review-fixes-fingerprints.json`
- `docs/development/evidence/WP02-11/implementation/before-final-fingerprints.json`
- `docs/development/evidence/WP02-11/implementation/before-parent-cancellation-fix-fingerprints.json`
- `docs/development/evidence/WP02-11/implementation/before-parent-cancellation-green-fingerprints.json`
- `docs/development/evidence/WP02-11/implementation/before-review-fixes-fingerprints.json`
- `docs/development/evidence/WP02-11/implementation/capture-evidence.ps1`
- `docs/development/evidence/WP02-11/implementation/draft-regressions.log`
- `docs/development/evidence/WP02-11/implementation/draft-regressions.trx`
- `docs/development/evidence/WP02-11/implementation/final-architecture.log`
- `docs/development/evidence/WP02-11/implementation/final-architecture.trx`
- `docs/development/evidence/WP02-11/implementation/final-http-v2.log`
- `docs/development/evidence/WP02-11/implementation/final-http-v2.trx`
- `docs/development/evidence/WP02-11/implementation/final-legacy-http-gateway-auth.log`
- `docs/development/evidence/WP02-11/implementation/final-legacy-http-gateway-auth.trx`
- `docs/development/evidence/WP02-11/implementation/https-fixture.log`
- `docs/development/evidence/WP02-11/implementation/https-fixture.trx`
- `docs/development/evidence/WP02-11/implementation/integrated-v2-fixed.log`
- `docs/development/evidence/WP02-11/implementation/integrated-v2-fixed.trx`
- `docs/development/evidence/WP02-11/implementation/integrated-v2.log`
- `docs/development/evidence/WP02-11/implementation/integrated-v2.trx`
- `docs/development/evidence/WP02-11/implementation/parent-cancellation-http-v2.log`
- `docs/development/evidence/WP02-11/implementation/parent-cancellation-http-v2.trx`
- `docs/development/evidence/WP02-11/implementation/parent-cancellation-red.log`
- `docs/development/evidence/WP02-11/implementation/parent-cancellation-red.trx`
- `docs/development/evidence/WP02-11/implementation/parser-green.log`
- `docs/development/evidence/WP02-11/implementation/parser-green.trx`
- `docs/development/evidence/WP02-11/implementation/protocol-parser-red.log`
- `docs/development/evidence/WP02-11/implementation/protocol-parser-red.trx`
- `docs/development/evidence/WP02-11/implementation/protocol-parser-regressions.log`
- `docs/development/evidence/WP02-11/implementation/red-parser.log`
- `docs/development/evidence/WP02-11/implementation/red-parser.trx`
- `docs/development/evidence/WP02-11/implementation/review-fixes-http-v2.log`
- `docs/development/evidence/WP02-11/implementation/review-fixes-http-v2.trx`
- `docs/development/evidence/WP02-11/implementation/review-fixes-red.log`
- `docs/development/evidence/WP02-11/implementation/review-fixes-red.trx`
- `docs/development/evidence/WP02-11/implementation/sdk.txt`
- `docs/development/evidence/WP02-11/implementation/transport-boundary-green.log`
- `docs/development/evidence/WP02-11/implementation/transport-boundary-green.trx`
- `docs/development/evidence/WP02-11/implementation/transport-boundary-red.log`
- `docs/development/evidence/WP02-11/implementation/transport-boundary-red.trx`
- `docs/development/evidence/WP02-11/independent-evidence-check.json`
- `docs/development/evidence/WP02-11/inputs/ADMISSION.md`
- `docs/development/evidence/WP02-11/inputs/PLT-HTTP-01-v0.2.md`
- `docs/development/evidence/WP02-11/inputs/S6-INT-ID-OPS-v0.2.md`
- `docs/development/evidence/WP02-11/inputs/TASK.md`
- `docs/development/evidence/WP02-11/inputs/foundation.json`
- `docs/development/evidence/WP02-11/intake-audit-shape-failure.md`
- `docs/development/evidence/WP02-11/intake-scope.json`
- `docs/development/evidence/WP02-11/intake-workflows.json`
- `docs/development/evidence/WP02-11/legacy-baseline.json`
- `docs/development/evidence/WP02-11/map_acceptance.py`
- `docs/development/evidence/WP02-11/precommit-workflows.json`
- `docs/development/evidence/WP02-11/review-fix-evidence-check.json`
- `docs/development/evidence/WP02-11/sources.json`
- `docs/development/evidence/WP02-11/staged-check.json`
- `docs/development/evidence/WP02-11/verify_execution_evidence.py`
- `docs/development/evidence/WP02-11/verify_scope.py`
- `src/CP6.Platform.AspNetCore/Cp6ProblemDetailsExtensions.cs`
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6HttpDeadline.cs`
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6HttpOperationBinding.cs`
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6HttpV2Handler.cs`
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6HttpV2Outcome.cs`
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6HttpV2Profile.cs`
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6HttpV2Registration.cs`
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6RetryAfter.cs`
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6RetryDecision.cs`
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6SafeProblemPolicy.cs`
- `src/CP6.Platform.AspNetCore/HttpV2/Cp6SafeProblemWriter.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/DeadlineDecisionTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/HandlerTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/LoopbackTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/OwnerProtocolFixtureTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/ProfileRegistrationTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/RecoveryBudgetTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/RetryAfterTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/ReviewRegressionsTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/SafeProblemTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/HttpV2/TestClock.cs`
