# P10 hosted Linux preflight implementation plan

> Execute inline with `executing-plans`; the owner approved the hosted, manually reviewed, read-only preflight and asked to finish P10. Their existing no-subagent instruction remains in force.

**Goal:** Run the unchanged full P10 suite on independent hosted Linux, so the local WSL UTC discontinuity does not prevent verification of the publication fixes.

**Architecture:** Add a branch-bound, non-deploying preflight workflow and a small standalone test-input collector that calls the existing `FormalPackageSource.DownloadAndVerifyAsync`. Reuse the existing isolated `P10_CRM_ACTIONS_READ_TOKEN` Environment only after adding owner review and an exact task-branch policy. Do not touch the main-only `p10-platform-candidate` Environment, its secrets, or the three authoritative workflow boundaries. Preflight success is a development gate, not candidate acceptance.

**Tech stack:** GitHub-hosted Ubuntu 24.04, pinned .NET 8.0.424, the reviewed cosign build, .NET/xUnit and actionlint.

## Confirmed baseline and alternatives

- Continue the same failure-remediation task in the existing linked worktree/branch `codex/p10-native-scan-preflight`, based on freshly fetched `origin/main@a41711dd55a093ab0ed127d599e0e7bcf11d548d`; source checkpoint is `4e4b0549f000ea205cbe8ca78818256cc342afb0`. Root workspace changes remain untouched.
- Native Windows full suite passes 1644/1644. The same frozen Linux suite fails only a UTC ordering check; independent single-CPU/network-disabled probes reproduce wall-clock reversals. Do not remove or tolerate the assertion, change clocks, or repeat restarts as part of this plan.
- Alternative local-system repair affects shared WSL/business services and has no confirmed fix. Existing formal validation requires main and cannot be used to bypass the failed pre-merge gate. The owner selected independent preflight instead.
- The separate Environment already contains exactly one secret named `P10_CRM_ACTIONS_READ_TOKEN`; no checked-in workflow references that Environment and no run is in progress. Its secret value is not read/exported. Its real read access still needs to pass the job; no broader credential is substituted on failure.

## Task 1: Establish regression and collector-boundary failures

**Create:** `tools/p10/ReleaseVerifier.Tests/PreflightWorkflowTests.cs` and `eng/p10/test-preflight-inputs.ps1`.

- [x] Add source-wiring assertions for the exact branch, read-only job permissions, isolated Environment, pinned checkout/SDK/actions and cosign recipe, an unfiltered full test command, and no candidate image/signing/publication commands or credentials.
- [x] Run `dotnet test tools/p10/ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PreflightWorkflowTests`; initial RED is four expected missing-workflow/collector failures, then GREEN 4/4 after implementation.
- [x] Add executable collector boundary checks for missing arguments, relative/existing output paths, absent credentials and symlink parent/output, preserving a caller marker. Invoke the standalone DLL using the configured dotnet host with an isolated environment. Before implementation, the script fails because the collector DLL does not exist; afterward all seven refusal/preservation checks pass.

## Task 2: Implement minimal inputs and workflow

**Create:** `tools/p10/PreflightInputs/CP6.P10.PreflightInputs.csproj`, `Program.cs`, `packages.lock.json`; `.github/workflows/p10-platform-preflight.yml`.

- [x] Collector accepts one absolute new output directory with an existing, non-reparse parent chain. Reject missing/control-character/overlong feed token before network or output creation. Download exactly the seven fixed 0.10.1 packages through `FormalPackageSource.DownloadAndVerifyAsync`; use `FileMode.CreateNew`, preserve original package bytes, then hand off the completed set through a same-parent non-overwriting directory rename. Emit only a non-acceptance summary. No fake producer/workflow identity or production CLI change.
- [x] Restore the collector through `eng/p10/NuGet.formal.config` to generate/review its lock, then verify locked restore/build. All dependencies match the existing verifier's locked versions/content hashes. The boundary checks and a real authenticated seven-package collection pass; every collected hash matches `S06ReleaseIdentity.PackageHashes`. The existing full suite verifies hash/author/TSA for those exact outputs.
- [x] Workflow triggers only a push of the named task branch (and guarded manual dispatch), checks exact repository/ref/source, uses pinned checkout with `persist-credentials: false`, builds the helper without secrets, restores/builds both .NET projects, collects actual packages, and runs `dotnet test` without filters/skips. Read-token secrets are step-local. Upload only the TRX as diagnostic evidence, never packages/private archives or signing keys. Full job success is required; a failed test cannot produce a success summary.
- [x] New wiring tests, native Windows full suite, format, boundary script and all four workflow actionlint checks pass. Windows full result is **1648 passed / 0 failed / 0 skipped**, 48 seconds, using the newly collected actual packages. TRX `artifacts/p10-cosign-security/test-results/windows-with-hosted-preflight.trx` SHA-256 is `72e1d5427739b6d9bf9a2284ae87aa4dea40e83660a21b5d2e59e6c09143a07e`. The local WSL failure is not reclassified as passing.

## Task 3: Constrain the preflight resource and execute

- [x] Before branch push, recheck the isolated Environment and sole secret name; its deployment history is empty and no checked-in workflow uses it. Add required reviewer `GTX537` (user ID `62733943`) and custom branch policy exactly `codex/p10-native-scan-preflight` (policy ID `59442634`); retain its existing secret without exporting it. Secret metadata remains `2026-09-07T12:50:16Z`. Keep self-review setting compatible with the owner being the dispatcher; the agent never approves an actual job. Read-back verifies this single branch/reviewer and unchanged main-only/owner candidate protection.
- [ ] Commit only reviewed task files, push the task branch normally and open a normal PR. The new push workflow waits for owner approval; show the exact run link. Existing PR checks may run concurrently, but no merge occurs until hosted P10 preflight and all required PR checks are green on the exact head.
- [ ] Inspect complete hosted output, including the reproduced cosign digest, actual package verification, every test count, and TRX identity. Diagnose any new failure instead of changing permissions or skipping gates.

## Task 4: Resume the authoritative P10 completion chain

- [ ] Update all four ledgers with actual evidence, not a predicted pass. Complete normal PR merge only after the exact-head gates; check remote main ancestry, merged smoke and all exact-main checks.
- [ ] Dispatch formal validation at that exact main and request owner approval. It still builds/signs the candidate image once, scans all findings and verifies inside the actual digest.
- [ ] After full validation success and the owner's unused candidate identity choice, continue protected candidate publication and read-only audit. Only complete immutable evidence plus the normal audit/status PR may establish Frozen/Consumable; `deployable=false` and no production deployment remain mandatory.

## Review and acceptance boundary

This is an additive development preflight. It neither broadens the existing release Environment nor consumes its publisher/signing credentials. A workflow YAML `permissions` setting constrains its `GITHUB_TOKEN`, not a PAT's underlying grants; the separate existing credential must remain the owner's intended read identity. Any missing/incorrect read grant is an owner action, not permission for the agent to mint a wider token. The seven package identities are fixed and checked against the verifier profile by regression tests; the tool's output is never a formal validation artifact.
