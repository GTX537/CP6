# Task E-T2 Report — WFS 波④ Inbox UX QA harness (write-only)

**Branch:** `feat/wfs-inbox-ux`
**Date:** 2026-07-13
**Status:** DONE — harness authored, **written not run** (live QA deferred to a session with a QA user present).

## Deliverables (docs only, zero code changes)

- `docs/superpowers/qa/wfs-inbox-ux/README.md` — runbook: setup, 6-scenario matrix, envelope quirks, manual DB/log checks, mobile/desktop browser walkthroughs, i18n cross-ref.
- `docs/superpowers/qa/wfs-inbox-ux/seed.sql` — 6 users + 1 FormDef + 2 FlowDefs, idempotent (`IF NOT EXISTS`), `SET QUOTED_IDENTIFIER ON`, singular table names, prints a 9-row sanity report + a RoleAction probe.
- `docs/superpowers/qa/wfs-inbox-ux/qa_inbox_ux.ps1` — HTTP e2e for scenarios 1/3/4 (PS5.1, ASCII bodies, real status codes, exit 1 on FAIL).

## Gates

1. **`git show --stat HEAD`** after commit: only the three harness files + this report. Working tree before commit had only the new `docs/superpowers/qa/wfs-inbox-ux/` dir untracked (no tracked-file modifications).
2. **Zero code changes** — docs only. The existing test suite is untouched; no `.cs`/`.vue`/`.ts` edited.
3. **Scenario↔code cross-check** — table below, every anchor cited file:line against shipped code.

## Deliberate anchor-drift correction (stated honestly)

The brief's Step-2 asked for a WHILE-loop raw INSERT of 30 runtime rows (`Wf_FlowInstance`/`Wf_FlowToken`/`Wf_FlowTask`/`Wf_FlowFormTo`). The harness **does not** raw-seed runtime rows. Batch transfer calls the real `FlowEngine.TransferAsync` per task (`AdvancedFlow.cs:78-98`), which mutates `task.AssigneeId`, appends a FormTo pair, writes `Wf_FlowHistory`, and notifies — those rows must be engine-produced to stay coherent. The shipped `BatchTransferTests.cs:39-44` seeds pending work via `Engine.SubmitAsync` in a loop, and `wfs-serial-signing` set the precedent (static fixtures in seed; ps1 submits over HTTP). So `seed.sql` provisions only users + FlowDefs; the ps1 submits `qa-bt-line`×N and one `qa-bt-par3` to create all runtime rows. Documented in seed.sql header + README §7.

## Scenario ↔ code cross-check

| # | Scenario assumption | Shipped-code anchor (file:line) | Honored / drift note |
| --- | --- | --- | --- |
| 1 | `GET /api/oa/pref/notify-matrix` → 5 rows incl. `branchPruned`; `timeout` both channels unsupported | `PrefController.cs:62-63`; `NotifyMatrix.cs:38-45` (Support) + `:76-87` (Rows reflection); `WfNotificationType.cs:10-22` (5 consts) | Honored. matrix asserts 5 rows, timeout `inApp/emailSupported=false`, branchPruned present. |
| 1 | close `flowRejected×email` via merge-save, `inApp` still fires Type=3, no email | `PrefController.cs:44-58` (SavePrefReq{PrefsJson,Merge}); `PrefService.SaveMergeAsync:50-83`; `PersistentWfNotifier.FlowRejectedAsync:138-175`; `NotifyMatrix.IsEnabled:47-73` | Honored. Type=3 = `WfNotificationType.FlowRejected` (`:16`). No-email is a LogEmailSender log check (`LogEmailSender.cs:12`), README §4.1. |
| 1 | close `inApp` too → no new Type=3 row | `PersistentWfNotifier.cs:140-142` (`if(!inApp&&!email) return`) | Honored — count held equal across a 2nd reject. |
| 2 | legacy flat `{"notify":{"todo":false}}` → both channels off; matrix shows fallback | `NotifyMatrix.cs:62-67` (LegacyKeyMap fallback) | Honored — manual DB-write check (README §4.2); flat form can't be produced by the new matrix UI. |
| 3 | `POST /api/oa/inbox/batch-transfer[/preview]`, both `[RequirePermission("oa-inbox","batch-transfer")]` | `InboxController.cs:217-238` | Honored. |
| 3 | 30 pending, 1 handled = dirty → candidates 29 (Pending-only); preview total+sample(10) | `InboxService.QueryTransferCandidatesAsync:334-343` (Pending+Running filter); `BatchTransferPreviewAsync:378-386` (sample `.Take(10)`) | Honored. Preview `total=N-1`, `sample=10`. |
| 3 | execute → 29 ok / 0 fail; per-item independent txn; MaxBatch=500 | `InboxService.BatchTransferAsync:345-376`; `MaxBatchTransfer=500 :313` | Honored. |
| 3 | explicit-taskId retry of handled task → fail `E-WF-002` (engine verdict, not pre-filtered) | `QueryTransferCandidatesAsync:324-332` (TaskIds path, no status pre-filter); `AdvancedFlow.TransferAsync:82` (`Status!=Pending → E-WF-002`) | Honored. `total=1, succeeded=0, failed[0].error=E-WF-002`. |
| 3 | audit: `Wf_FlowHistory action=transfer ActorId=admin` + FormTo double row | `AdvancedFlow.cs:92-95` (`TransferFormToAsync`+`AddHistory(...,"transfer",...)`); confirmed by `BatchTransferTests.cs:151-168` | Honored — DB check README §4.3. |
| 3 | non-role user → 403 `无权限：oa-inbox:batch-transfer`; preview same point | `RequirePermissionAttribute.cs:36-40`; `InboxController.cs:218,230`; `InboxBatchTransferPermissionSeed.cs:31-33` (RoleId=1 only) | Honored. `qa_bt_norole` RoleId=2 → 403 on both. |
| 4 | `GET /api/oa/inbox/pending?rowMode=` merged=1 / expanded=3; grouping before paging | `InboxController.cs:57-70`; `InboxService.PendingAsync:33-39` (group-by-instance when `!=expanded`, then page) | Honored. Parallel-3 (`ParallelSplitNodeHandler.cs:22-29`, schema per `ParallelGatewayTests.cs:18-37`) → 3 same-assignee tasks in 1 instance. |
| 4 | omitted `rowMode` → viewer's pref (default merged); pref via merge-save `{rowMode}` | `InboxController.cs:66` (`?? _pref.GetRowModeAsync`); `PrefService.GetRowModeAsync:86-97` (default merged) | Honored. Set `{"rowMode":"expanded"}`→3; remove key→merged=1. |
| 5-6 | mobile 375 / desktop 1280 browser walkthroughs (C-T1/C-T2 deliverables) | `InboxPending.vue`, `FormDetail.vue`, `BatchTransferDialog.vue`, `InboxSettings.vue` | Manual gstack browse — README §5-6. |
| i18n | 39 keys `I18nOaInboxUxScreenSeed` insert-only | `I18nOaInboxUxScreenSeed.cs:19-69` (7+5+1+23+2+1=39) | Honored — README §8. |

## Watch items stated honestly

- **No exactly-once claim.** The wave watch note says the A-T2-family double-submit window does not apply here — batch transfer is per-task engine `TransferAsync` calls, not `TriggerFire`. The ps1 makes no concurrency/exactly-once assertion; the "second session races" variant is documented as a manual live variant (README §4.3), with the deterministic retry (explicit already-handled task → `E-WF-002`) standing in for the automatable part.
- **No-email is a log/DB check**, not an HTTP assertion — the ps1 can't read the backend console; README §4.1 spells out the `[DEV-EMAIL→…]`-absent check plus the Type=3 count SQL.
- **Legacy flat-pref (scenario 2)** is DB-direct-write only — the new matrix UI cannot emit the flat shape, so it's a manual fixture (README §4.2).

## Concerns

- `NotificationService.ListAsync` returns a bare `NotificationItem[]` under `data` (not paged envelope); the ps1's `NotifyCountOfType` assumes that shape (`NotificationService.cs:30-33`). If a future change wraps it, the helper needs a tweak.
- The ps1's Type-3 assertions use `-gt`/equality on counts rather than absolute values, to tolerate residual notifications from repeated runs (seed + submit are idempotent but notifications accumulate). Live QA against a fresh `CP6DB_OA` gives the cleanest read.
