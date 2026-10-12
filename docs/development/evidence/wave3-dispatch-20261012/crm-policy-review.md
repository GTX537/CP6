# CRM manual-validation independent review

Result: **NO_BLOCKERS**

Reviewed final candidate: ed295e59e5a8b0498cb5d6964d6f81c85f320daf
Baseline: c778a3052a4416b82facb07b1244fc98fb6ad8a1
Branch: codex/coord-manual-validation-20261012
Worktree: D:/CP6.CRM-worktrees/coord-manual-validation-20261012
Review date: 2026-10-12

One complete task review, followed only by the relevant fix review and final source binding. Final worktree is clean. Both normal commits (3bdb03b6a2d9ee467bb5396fd9f4c6499dee6530 and ed295e59e5a8b0498cb5d6964d6f81c85f320daf) are in scope; no history rewrite.

## Findings

No unresolved P0/P1/P2 blocker.

Resolved P2 at scripts/ci/workflow-contract.ps1:48: the earlier event-key regex missed a YAML escaped equivalent key. Appending the literal line "\u006fn": [push] to the in-memory workflow was accepted by both Assert-CrmCiWorkflowTriggers and Assert-CrmCiWorkflow. Local YAML decoding identifies that key as on; no claim was made about live GitHub duplicate-key behavior or actual Actions execution. Worker added a fixed canonical top-level key/line check (lines 48-52), and four focused mutations covering Unicode escape, hex escape, explicit key and tagged key. Local RED preserved the accepted mutation failure; GREEN now records 71 cases. Reviewed the fix and evidence; issue closed.

## Confirmed boundaries

- Byte comparison proves final crm-validation.yml differs from baseline only by deletion of the four original pull_request/push/main lines. Seven jobs, permissions, optional boolean include_legacy_s03 default false, artifact identity/hash consumers, SQL helpers, gates and summary wiring are unchanged.
- Manual routing still selects Full checks and enables legacy only when explicitly requested. Candidate has no automatic event. This review does not certify remote workflow state or grant Actions execution permission.
- New exact trigger contract is called both by workflow contract checks and verify-product-freeze.ps1. It rejects unrecognized top-level representations and any noncanonical event/input block.
- Existing solo-m0-v1.json, delivery plan and DEC-CRM-C04B-SIMULATED-ACCEPTANCE.md are byte-for-byte identical to baseline. Original approved hash remains e41b36607c18b9c8a748a0584d9cef198798f33ebd3d8e4cd6c7b39640391361.
- Native plan and decision bytes independently match accepted commit 0005c2062240e864cdc91dff70441de4da6f7d32. Removing exactly the recorded two-line addendum reconstructs the original hash and exact parent-commit plan bytes.
- New compatibility helper accepts the original approved hash, or only exact successor c1c602843a4c0018a47f642e9c37367e26c2b6bb9b1cd6fea45ea7be79038e1c with pinned record fc827208887cfe570cd633761946c82dd91b0929b94b79ddbaba38bc8a8f1304 and decision aaab15af9af64cd64719077ce2f4acfc3664b6fe4f81771b62b4d55f93420881, followed by exact body reconstruction. Editing the compatibility record cannot self-authorize a successor. Other source-contract comparisons/approval/Git evidence and production permissions remain intact.
- No new production approval, deployment, product-scope relaxation or false business adoption claim. Current docs retain remote integration and S01-08 entry as separate pending coordinator work.
- No machine-specific runtime paths/configuration were added. Absolute paths occur only in preserved local execution error evidence. Repo LF attributes support raw-byte hash stability.

## Evidence inspected; no full-suite rerun

Existing workflow67 evidence is retained as history. After the fix: workflow71 GREEN and final governance-review-final.txt exit0. Approved-addenda suite records 22 executed cases (2 positives, 20 rejection cases). Existing unchanged-input scope115, shared TRX32, P10 negative33/evidence43 and C01 negative44 evidence is explicitly reused, not presented as a new run. Original failures, invocation failure, and governance baseline failure are preserved.

README/status documents explain the actual execution and reused ranges. Full M0 fixture suite, .NET/Node builds, NuGet restore, business/DB tests, real identity/artifact adoption and production release are explicitly unverified here. C01 fake-dotnet failure-path fixture is described as such, not as a real .NET run.

Independent checks were limited to read-only full diff/source/evidence review, in-memory reproduction of the resolved guard issue, native/hash/source-byte comparison, final clean status and diff whitespace checks. No network calls, repository changes, Git mutations, dotnet/npm/DB/Actions execution, or repeat of complete successful suites by reviewer.

Final-source-inputs.json's seven raw SHA256 bindings match both final working files and final commit blobs. Current workflow remains exactly the four-line deletion after the guard fix. Scope of NO_BLOCKERS is this candidate relative to the stated baseline; coordinator still owns pre-push workflow/remote protection checks, normal merge and remote-main containment.
