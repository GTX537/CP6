# Task 1 Report — 引擎归属闸四方法 + E-WF-029 (TDD)

**Branch:** feat/wf-actor-ownership
**Commit:** fd85127e (pushed to origin)
**Status:** DONE

## Summary

Closed the P0 越权代批 hole (M-OA/WF #1) at the engine layer. Added a single ownership
assertion helper `AssertActorMayHandleAsync(Wf_FlowTask, Guid actorId, Guid? onBehalfOf)`
in a new partial `FlowEngine.Ownership.cs`, and wired it before every state mutation in the
four mutating engine methods: `ActOnceAsync` / `TransferAsync` / `AddSignAsync` /
`SendBackAsync` (SendBackTarget overload = single entry covering node/prevstage/starter).

Three allow paths: assignee self / act-as with engine-side re-verified active delegate grant /
SystemActor (Guid.Empty). Violation → `E-WF-029`; act-as without a valid grant → `E-WF-001`.
`TransferAsync` gained `bool bypassOwnership = false`; only `InboxService.BatchTransferAsync`
passes `true`.

## Files changed

- **New** `CP6.Core/Services/Wf/FlowEngine.Ownership.cs` — gate helper + `SystemActor` const.
- `CP6.Core/Services/Wf/FlowEngine.cs` — gate inserted in `ActOnceAsync` (after inst-Running
  check, before `task.Status = ...`); corrected now-false "引擎不查委派" doc-comment.
- `CP6.Core/Services/Wf/AdvancedFlow.cs` — `TransferAsync` signature + gate (bypass-guarded);
  gate in `AddSignAsync` and `SendBackAsync(SendBackTarget)`.
- `CP6.Core/Services/Wf/IFlowEngine.cs` — `TransferAsync` signature updated; line ~21
  `ActAsAsync` doc-comment "引擎不查委派" corrected to "控制器先闸, 引擎复验(防御纵深)".
- `CP6.Core/Services/Oa/InboxService.cs` — `BatchTransferAsync` call passes `bypassOwnership: true`.
- `CP6.WebApi/Seed/I18nOaInboxScreenSeed.cs` — E-WF-029 five-language row after E-WF-008.
- **New** `CP6.Tests/Wf/FlowActorOwnershipTests.cs` — 14 tests (brief code, adapted).
- `CP6.Tests/Oa/ActAsServiceTests.cs` — existing-test correction (see below).

Test-code adaptations vs brief: `Sys_User.Enable` is `bool` (brief used `1`) and there is no
`UserTrueName` field — used `new Sys_User { Id, UserName="to", NickName="to", Enable=true }`
via a `SeedUser` helper. `Password` has a default (`""`) so it was not required. DbSet names
(`Wf_FlowDelegates`, `Wf_FlowHistories`, `Sys_Users`) matched the brief verbatim.

## RED evidence (Step 2)

New class run with `Transfer_BypassOwnership_*` temporarily commented (param did not yet exist):

```
Failed!  - Failed:     8, Passed:     5, Skipped:     0, Total:    13
```

The 8 failures = all violation/negative cases ("No exception was thrown"):
Act_ByNonAssignee, ActAs_OnBehalfOfNotAssignee, ActAs_WithoutGrant,
ActAs_ExpiredOrDisabledGrant, Act_DelegateDirect_WithoutActAs,
Transfer_ByNonAssignee, SendBack_ByNonAssignee, AddSign_ByNonAssignee.
The 5 passes = positive/system paths (ByAssignee ×3, SystemActor, ActAs_DelegateWithActiveGrant).

## GREEN evidence (Step 4)

After implementing the gate and restoring the bypass test:

```
Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14 - CP6.Tests.dll
```

## Full suite (Step 5)

First full run surfaced exactly one red:

```
Failed!  - Failed:     1, Passed:  2211, Skipped:     5, Total:  2217
  Failed CP6.Tests.ActAsServiceTests.ActAs_RecordsActualHandler_AndOnBehalfOf
```

After the actor correction below, final:

```
Passed!  - Failed:     0, Passed:  2212, Skipped:     5, Total:  2217 - CP6.Tests.dll (net8.0)
```

2198 baseline passed + 14 new = 2212. (The brief's "+15 / ≥2213" over-counted the provided
test file by one — the code in the brief defines 14 `[Fact]` methods, not 15. No test was
dropped; the count is fully accounted for.)

## Existing-test actor corrections (1)

**`CP6.Tests/Oa/ActAsServiceTests.cs:38-42` — `ActAs_RecordsActualHandler_AndOnBehalfOf`**
- Old: `me` calls `ActAsAsync(task, actorId: me, onBehalfOf: grantor, ...)` with **no
  `Wf_FlowDelegate` grant seeded**. Under the new engine-side re-verification this correctly
  throws `E-WF-001`.
- Correction: seeded an active `Wf_FlowDelegate { GrantorId = grantor, DelegateId = me,
  Enable = true, ValidFrom = now-1d, ValidTo = now+1d }` before the act-as call.
- Attribution: **test modeling gap, not a product path.** In production the controller's
  `AssertActiveGrant` guarantees an active grant exists on any real act-as; the test omitted
  seeding it. No gate was weakened — the fix models the real handler's precondition. The
  sibling test `ActAs_NullOnBehalf_EquivalentToActAsync` (actor == assignee `grantor`,
  onBehalfOf null → self path) needed no change and stayed green.

No red revealed a product internal caller passing a non-assignee outside the two known
exceptions (WfTimeoutService SystemActor, BatchTransferAsync bypass). No BLOCKED condition.

## Self-review

- Gate precedes every state mutation in all four methods:
  - `ActOnceAsync` — after `inst.Status != Running` early-return, before `task.Status = ...`.
  - `TransferAsync` — after inst check, before target-user lookup / `task.AssigneeId = ...`;
    wrapped in `if (!bypassOwnership)`.
  - `AddSignAsync` — after inst check, before add-sign count and the `before`-suspend mutation.
  - `SendBackAsync(SendBackTarget)` — after inst check, before `LoadSchemaAsync` and the
    node/prevstage/starter switch, so all three send-back kinds are covered from one entry.
    The legacy 3-arg overload forwards to this overload, so it is covered too.
- IFlowEngine doc-comment (line ~21) and the mirror comment in FlowEngine.cs both corrected.
- Diff limited to the eight in-scope files; staged explicitly (not `git add -A`) to avoid the
  pre-existing untracked `PermissionSeedInterlockTests.cs` / any stray file.
- `SystemActor` newly defined on the FlowEngine partial (Guid.Empty), matching
  `WfTimeoutService.SystemActor`; no prior definition on FlowEngine, no collision (build clean).

## Concerns

- **Count expectation:** final passed = 2212, not the brief's ≥2213. Root cause is the brief's
  own test file defining 14 `[Fact]`s while the prose said 15; 2198 + 14 = 2212 is internally
  consistent and 0-fail. Flagging only so Task 2 does not treat 2212 as a shortfall.
- **Out-of-scope-by-design:** `TimeoutAdvanceErrorEdgeAsync` is a fifth mutating engine method
  but is invoked only by `WfTimeoutService` with `SystemActor`, so it is not an越权 surface and
  was intentionally left ungated per the brief's four-method scope.
- LF→CRLF warnings on the two new files (cosmetic, Windows checkout normalization).
