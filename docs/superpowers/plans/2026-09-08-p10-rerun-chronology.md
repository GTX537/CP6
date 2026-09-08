# P10 Rerun Chronology Implementation Plan

> **For agentic workers:** Use executing-plans inline, as requested by the owner. Do not delegate.

**Goal:** Accept genuinely bound GitHub reruns whose attempt record is created after execution starts, without accepting substituted runs or weakening execution-time bounds.

**Architecture:** For attempt 1 keep the existing creation/start/update ordering. For later attempts additionally read the same run's exact attempt 1 through the existing allowlisted transport. Bind its identity, completed status, and creation/start/end to the selected attempt. Treat the later attempt's creation timestamp as record metadata bounded by original creation and selected update, not as the execution start. Preserve all selected-attempt/job/source/conclusion/cutoff checks.

**Tech Stack:** .NET 8, xUnit, GitHub Actions REST API, existing P10 verifier.

## Evidence and constraints

- Run `34221083970`, attempt 2 failed with `s06-current-time` before package reads.
- Exact-attempt API: created `2026-09-08T12:06:31Z`, started `2026-09-08T12:06:29Z`, updated `2026-09-08T12:08:30Z`.
- Attempt 1: created/started `2026-09-08T11:30:49Z`, updated `2026-09-08T11:48:29Z`, completed/failure. An earlier failed attempt must not invalidate a later successful attempt.
- The run-level endpoint reports original creation `11:30:49Z`; do not silently replace a selected attempt with the latest run.
- No production deployment, permission changes, endpoint redirects, fabricated success evidence, or arbitrary clock tolerance.
- GitHub reference: https://docs.github.com/en/rest/actions/workflow-runs#get-a-workflow-run-attempt. Observed timestamps above are evidence, not a universal API ordering guarantee.

## Task 1: Isolate and reproduce

- [x] Use existing clean linked worktree and branch `codex/p10-rerun-chronology` from `origin/main@af0154782c8aa73eae47c083a884e2a404ef7b64`; leave root local main and user changes untouched.
- [x] Run baseline `dotnet test tools/p10/ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06CurrentWorkflowTests`.
- [x] Add parser regression tests in `tools/p10/ReleaseVerifier.Tests/GitHubRerunChronologyTests.cs` for live validation/publication and completed main; retain existing fixed PR record regressions. Use original failed attempt plus later selected successful/running attempt, exact reported timestamps, and ordinary created-before-start reruns. Synthetic success/status fields are parser vectors only.
- [x] Compile-compatible signature scaffolding accepted an optional first-attempt JSON argument while retaining the old implementation. New tests recorded assertion failures before implementing behavior.

## Task 2: Enforce separate original/attempt chronology

Files: create `tools/p10/ReleaseVerifier/GitHubRunChronology.cs`; modify `S06CurrentWorkflowChecks.cs`, `GitHubWorkflowChecks.cs`, `S06CurrentWorkflow.cs`, `GitHubEvidenceSource.cs` in the same directory. Inspection confirmed that `CrmPullRequestSelection` pins both approved PRs to attempt 1; preserve that selection and its strict original-attempt path without modifying `CrmForwardBindingSource.cs`.

- [x] Implement one shared first-attempt reader: cancellation first; no extra request for attempt 1; otherwise GET the existing `GitHubReadTarget.Run(repository, runId, 1)`.
- [x] Implement the shared selected-record time check. Parse all three timestamps strictly; require `started <= updated <= cutoff` and `created <= updated`.
- [x] Attempt 1 still requires `created <= started`. A rerun requires a supplied completed attempt 1 with matching run ID, repository/head repository, SHA, branch, workflow path and event, and exact `run_attempt=1`.
- [x] Require `originalCreated <= originalStarted <= originalUpdated <= selectedStarted`, `originalCreated <= selectedCreated`; allow prior failure, never infer prior success. Selected success remains mandatory in completed consumers.
- [x] Thread the real first-attempt observation through the running and completed workflow readers. Keep the fixed CRM PR attempt-1 reader unchanged. Never accept a serialized first-attempt record as a live capability.
- [x] Retain current job start/end bounds and all exact identity/status/conclusion checks.

## Task 3: Regression and delivery

- [x] Add negative vectors for missing/mismatched first attempt, non-completed first attempt, future/backward times, original finishing after rerun start, selected creation outside bounds, missing/offset timestamp, wrong selected attempt and failed selected completed run. Confirm both live and completed paths reject them.
- [x] Run focused rerun tests, then the entire verifier suite with real package/signature/GitHub inputs (no skipped tests), and `dotnet format tools/p10/ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes`.
- [x] Update `docs/devops/HOWTO-P10-PLATFORM-CANDIDATE.md`, `P10-PLATFORM-REFERENCE.md`, and all four project-memory ledgers with actual failure/fix evidence; retain Candidate/No-Go until real validation/publication/audit finish.
- [ ] Review the complete branch diff, preserve no-secrets transport behavior, commit explicitly scoped files, push task branch and open normal PR.
- [ ] Wait for required PR checks; merge normally without admin bypass, verify origin/main contains the commit, run merged-source smoke tests and wait for required exact-main checks.
- [ ] Dispatch validation on the new exact main and request owner approval. Do not rerun old source or approve the protected environment on the owner's behalf.

## Results

- Baseline: 45/45 passed, no skipped tests.
- RED: compile-compatible parser signatures retained old behavior; new suite 8 failed / 51 passed / 0 skipped. Six failures reproduced the invalid creation/start assumption; two exposed missing original-creation lower bounds.
- GREEN: initial related suites 104/104; expanded chronology/GitHub/CRM suite 240/240 with real GitHub reads, all zero skipped.
- Full verifier suite: 1615/1615, zero failed/skipped, 47 seconds. Initial format check found only new test initializer layout; scoped formatting followed by full verify-no-changes exited 0.
- Review: checked all changed production code, transport/cancellation wiring, original attempt identity and time constraints, fixed CRM PR selections, negative vectors and distinction between parser vectors and actual failed-run evidence. No unresolved blocking code findings. PR/main delivery remains unchecked until actual completion.
