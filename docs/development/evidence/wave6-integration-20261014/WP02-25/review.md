# WP02-25 concentrated candidate review

**CHANGES_REQUIRED — not ready to merge.** Two P2 findings; no P1 finding. Candidate `095a66bfda8b4df2ee65e920541ce6304e8e66c1`, baseline `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`. Worktree `D:/CP6.Platform-worktrees/wp02-25-20261013` was clean at that exact HEAD before and after review.

## P2 — UTC rollback revives expired runtime identity evidence

Location: `src/CP6.Platform.AspNetCore/ObservabilityV1/OwnedModuleObserverV1.cs:97-99`; downstream consumer `HealthAdapterV1.cs:100-109`.

Freshness uses only `now - observer.ObservedUtc`. A 60-second observation returns `Match` initially, becomes `Unproven` after UTC advances 120 seconds, then becomes `Match` again if UTC rolls back to original time +30 seconds—even while monotonic elapsed time advances to 180 seconds. No new module load or observation occurred. Relevant new-effect route availability consumes this revived Match, so this is a functional freshness failure, not merely inaccurate display time.

**Executed reproduction:** loaded the existing, hash-verified candidate net8.0 Contracts and AspNetCore DLLs into an isolated PowerShell process. An in-memory `TimeProvider` supplied independently controlled UTC and monotonic clocks. Actual sequence: `Match -> Unproven -> Match`. The real system clock was not changed. The loaded Contracts digest was `0d068929cfcc3613c534a7be4ad1fdaac580a6d61eade4c17aa484a4eb5010ef`; AspNetCore digest was `cd2f94dc2108e062b6d00539a56ca29beb76c0686e2829c1d57669e216dc227f`.

Harness runtime was **PowerShell .NET 10.0.11**, not the retained SDK8 gate. The first harness compile failed because `TimeProvider.System` shadowed the `System` namespace; changing the harness references to `global::System` resolved it. That failure involved no product source and is retained here rather than reported as a product failure.

Implementer reproduction using existing test helpers:

```csharp
// SplitClock must override both GetUtcNow and monotonic GetTimestamp.
var t0 = clock.GetUtcNow();
// Construct 60-second binding, observer, separately supplied fixture pin, evaluator.
Assert.Equal(IdentityVerdictV1.Match, evaluator.Observe().Verdict);
clock.UtcNow = t0.AddSeconds(120);
clock.Elapsed = TimeSpan.FromSeconds(120);
Assert.Equal(IdentityVerdictV1.Unproven, evaluator.Observe().Verdict);
clock.UtcNow = t0.AddSeconds(30);
clock.Elapsed = TimeSpan.FromSeconds(180);
Assert.Equal(IdentityVerdictV1.Unproven, evaluator.Observe().Verdict); // candidate: Match
```

Fix freshness using the observer-owned monotonic elapsed clock, retaining UTC validation for metadata. Expired evidence must not revive on rollback. Add a targeted identity and affected-route regression. Existing `FreshnessIsEvaluatedOnEveryObservation` only moves UTC forward; the prior rollback correction protects health dependency caching, not identity evidence.

## P2 — New live and identity responses omit no-store

Location: `src/CP6.Platform.AspNetCore/ObservabilityV1/HealthAdapterV1.cs:32-35`.

Startup/ready call `Write`, which sets `Cache-Control: no-store` at line 49. Live and identity return `Results.Json` directly and never set that header. A stored identity Match response can survive outside the adapter's freshness reevaluation. Fixed source OBS04-A4 requires health output no-store, and legacy operational output already supplies it.

**Source-confirmed; prescribed HTTP reproduction was not newly executed:** use the authorized local fixture, request `/ops/v1/live` and `/ops/v1/identity`, and assert `response.Headers.CacheControl?.NoStore == true`. Candidate supplies no such header; contrast startup/ready. Apply the header consistently to all four V1 routes and add focused HTTP assertions without changing authorization/wire shape. None of the new V1 tests checks this header; the OBS04-A4 mapping uses hidden-aggregate output and an old endpoint test, which does not cover these direct JSON paths.

## Verified evidence and review coverage

- Read Main AGENTS and exact dispatch/admission plus fixed 111-line PLT-OBS source; source SHA-256 matches `20c9a128b3f2bab0621459cc6c6494b16fb21cd8340792738ba89e90efa3bc99`. No Platform worktree AGENTS file exists.
- Reviewed complete candidate change inventory: 142 additions, including all ten production files and eleven task-private test files, supporting scripts/docs and structured evidence. No existing production/test/project/dependency/workflow/schema/state file changed. `git diff --check` passed.
- Independently checked all **261 source/config hashes, 9 resolved dependency snapshots and 33 Release artifact hashes** against current bytes: zero mismatches. Source digest is `a68ad22bcaddba0deec96403fe683de35d73a81bf3e4cb037a258aaa2393284a`.
- Confirmed per-suite before/after/current relevant source equivalence; all three execution exits are 0; actual TRX hashes, names/outcomes/counts agree: **Unit 57/57, host 100/100, Architecture 98/98; total 255/255, no skipped tests**. These are the implementer's 2026-10-14 final-01 runs on SDK 8.0.424/runtime 8.0.30; this reviewer did not rerun or relabel them.
- Checked all 98 acceptance proof rows and their TRX hashes. All 32 original business acceptance cases remain NOT_RUN; 27 have bounded local/regression portions. Prior spec/quality findings and correction reports were read; their retained failures were not removed or treated as successes.
- Retained actual loopback output shows owned-module digest, dependency stop/restart, and SDK final automatic inbound/outbound traces, metrics and logs. The new projection keeps finite keys/values/resources; queue, series, rate, batch, retention and sink concurrency are bounded; no arbitrary formatter enters the private log SDK. Checked blocked/throwing sinks, lifecycle cleanup, exact legacy wire preservation and observer-only definitions. No additional actionable issue was established in those reviewed paths.
- Existing byte-loader Match is expressly limited to owned managed modules, not the business code executing them, whole host/default context, package signature/source trust or deployment attestation. The report does not infer broader proof. WP02-26 SLO/general recorder and WP02-28/29 actual adoption remain separate.

## Integration recommendation

Return R1/R2 for one correction batch and targeted regression/review. Reuse unchanged 255-test evidence at its actual input scope; do not repeat full testing solely because SHA changes. COORD state synchronization and remote-main containment remain future integration steps. This review authorizes neither integration nor production claims.

No repository source edits, Git mutations, remote writes, Actions, database, deployment or full test reruns. Only the assigned `.md` and `.json` review reports were written. No Actions run was started, cancelled or changed.
