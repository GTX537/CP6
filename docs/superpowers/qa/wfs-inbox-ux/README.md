# WFS 波④ Inbox UX (E-T2) -- QA Runbook

**Branch:** `feat/wfs-inbox-ux`
**Date:** 2026-07-13
**Feature scope:** the four wave-④ inbox UX enhancements --
**(1) notification matrix** (`NotifyMatrix` type×channel gating in `PersistentWfNotifier`, all
4 notify methods; `GET /api/oa/pref/notify-matrix`; merge-save `POST /api/oa/pref/save`),
**(2) in-flight batch transfer** (`POST /api/oa/inbox/batch-transfer[/preview]`, per-item
independent transactions, MaxBatch=500), **(3) pending rowMode** (merged/expanded,
`GET /api/oa/inbox/pending?rowMode=`, viewer-pref default), **(4) mobile 375px / desktop
1280px** browser walkthroughs.

> **Status: written, not run.** Authored per task E-T2 (write-only). Live QA -- spin the
> backend against the isolated DB, run the ps1 HTTP e2e, drive the pages in a real browser --
> is executed later by the main agent with a QA user present. **Nothing here has been
> executed.** Bugs found during live QA are fixed TDD (regression test into `CP6.Tests/Oa/**`).

---

## 1. What this harness covers

Three deliverables:

| File | Purpose |
| --- | --- |
| `seed.sql` | 6 users + 1 FormDef + 2 FlowDefs (static fixtures only -- raw INSERT into `CP6DB_OA`). |
| `qa_inbox_ux.ps1` | HTTP e2e over the testable parts of scenarios 1, 3, 4 against a running backend (ASCII data, real status codes). |
| `README.md` | This runbook: setup, scenario matrix, expected results, browser steps, DB checks. |

### Scenario matrix (brief 6 -> harness)

| # | Feature / brief scenario | Where | Expected outcome |
| --- | --- | --- | --- |
| 1 | **Notification matrix -> skip verify**: close `flowRejected × email` -> reject a flow -> starter has a `Wf_Notification` Type=3 row + **no email**; then close `flowRejected × inApp` too -> reject again -> **no new** Type=3 row. timeout row = both channels unsupported (disabled cells). Reset restores all channels. | ps1 (matrix + notif rows) + README 4.1 (no-email log check) | matrix 5 rows incl. `branchPruned`; timeout `inAppSupported=false, emailSupported=false`; Type=3 grows once, then holds. |
| 2 | **Legacy flat-pref compatibility**: DB-write an old flat `{"notify":{"todo":false}}` -> trigger a todo -> no Type=1 row + no email; the settings matrix shows `todoCreated` as double-off (fallback parse). | README 4.2 (manual, DB direct-write) | `NotifyMatrix.IsEnabled` legacy fallback treats flat `todo=false` as both channels off. |
| 3 | **Batch transfer full flow incl. fail + retry**: 30 pending on `from` (1 handled = dirty) -> FlowAdmin batch transfer -> preview (29 + sample 10) -> execute (29 ok / 0 fail) -> explicit-taskId retry of the handled one -> fail detail `E-WF-002` (same 口径 as single retry). Non-role user -> 403 `无权限：oa-inbox:batch-transfer`. | ps1 (preview/execute/retry/403) + README 4.3 (engine-audit DB check) | Pending-only candidates = 29; per-task `Wf_FlowHistory action=transfer ActorId=admin` + FormTo Transferred/Pending pairs; preview & execute share the permission point. |
| 4 | **rowMode**: parallel-3-branch instance for one approver -> `merged=1` row / `expanded=3` rows -> omitted `rowMode` follows the saved pref (`PrefsJson.rowMode`); detail-page actions unaffected by display pref. | ps1 (row counts + pref default) | merged groups by instance before paging; pref default = merged; expanded pref returns 3. |
| 5 | **Mobile 375px 三页走查** (gstack browse, viewport 375×812): list card stream + folder chip rail + filter drawer; FormDetail stack + pinned action bar (agree/reject/transfer/sendback tappable); transfer dialog fullscreen; screenshots in this dir. | **Browser** (README 5) | responsive C-T1/C-T2 deliverables render; primary actions reachable. |
| 6 | **Desktop 1280px 像素走查**: same three pages + settings page, zero-regression vs pre-change (table column widths; action-bar non-sticky; drawer ~60%). | **Browser** (README 6) | desktop layout unchanged. |

---

## 2. Environment setup

### 2.1 Isolated database

Reuse the `CP6DB_OA` database from prior WFS QA sessions, or create fresh:

```sql
CREATE DATABASE CP6DB_OA;
```

Point the backend at it so QA data never touches live `CP6DB`. On first boot
`db.Database.Migrate()` applies all EF migrations. Startup seeds relevant here:
- `InboxBatchTransferPermissionSeed` -- grants `(oa-inbox, batch-transfer)` to **RoleId=1**
  per tenant, menu 733 (`PermissionService.HasActionAsync` has **no admin bypass**, so the
  operator must be in role 1; `qa_bt_norole` at RoleId=2 gets the 403).
- `I18nOaInboxUxScreenSeed` -- **39** five-language keys (matrix / batch-transfer / rowMode /
  mobile / `oa.pref.errBadJson`), insert-only.

### 2.2 Apply seed

Run from a **native shell** (cmd / PowerShell), not git-bash:

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
```

The seed prints a 9-row sanity report (6 users + 1 form + 2 flowdefs) plus a RoleAction probe
confirming `(oa-inbox, batch-transfer)` is granted to RoleId=1 only. All users share password
`123456` (admin's BCrypt hash cloned). `SET QUOTED_IDENTIFIER ON` is set (required by the
filtered unique index on `Wf_FlowDef`).

### 2.3 Backend

Prior WFS QA sessions used ports 5177-5181; this harness defaults to **5181**:

```powershell
cd <repo>\CP6.WebApi
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --urls "http://localhost:5181"
```

- **Dev CSRF must be disabled** (`Security:Csrf:Enabled=false`, the dev default): the
  settings/inbox/admin POSTs are cookie-auth'd (the JWT `cp6_at` cookie flows via
  `-WebSession`) and would 403 on the CSRF double-submit otherwise. In **production** posture
  those POSTs require cookie + `X-Csrf-Token`. The scenario-3 403 is a **permission** 403,
  distinct from any CSRF 403.
- **Email**: with no `Email:Smtp:Host` configured in Dev the DI resolves
  `LogEmailSender` (`Program.cs:596-610`), which logs `[DEV-EMAIL→{to}] {subject}: {body}` at
  Warning level (`LogEmailSender.cs:12`). Scenario 1's "no email" assertion is the **absence**
  of that line for the reject subject -- watch the backend console.

---

## 3. Running the HTTP e2e

```powershell
.\qa_inbox_ux.ps1
.\qa_inbox_ux.ps1 -BaseUrl http://localhost:5181
.\qa_inbox_ux.ps1 -N 30       # count of line flows for the batch-transfer fixture (default 30)
```

The script logs in the four driver users (`qa_bt_admin`/`qa_bt_starter`/`qa_bt_from`/
`qa_bt_par`, plus `qa_bt_norole` for the 403), then walks scenarios 1, 3, 4, printing
`PASS`/`FAIL`/`WARN` and a final tally. It captures **real** HTTP status codes (via
`Invoke-WebRequest -UseBasicParsing`) so it can tell `200` from `403` and read
`{code,message}` error bodies. Exit code 1 if any FAIL.

### Envelope quirks (verified against controllers)

- **OA/inbox/pref/notification** endpoints: standard envelope `{ code:0, message:"OK", data }`.
  - `pending` -> `data: InboxPendingItem[]` (camelCase `taskId`, `instanceId`, ...).
  - `batch-transfer/preview` -> `data.{ total, sample:[...] }` (`InboxService.cs:378-386`).
  - `batch-transfer` -> `data.{ total, succeeded, failed:[{ taskId, flowKey, ok, error }] }`
    (`InboxService.cs:345-376`).
  - `notify-matrix` -> `data: NotifyMatrixRow[]` (`typeKey`, `typeValue`, `inAppSupported`,
    `emailSupported`; `NotifyMatrix.cs:76-87`).
  - `notification/list` -> `data: NotificationItem[]` (bare array; `type` is the int
    `WfNotificationType`; `NotificationService.cs:30-33`).
- **Errors**: `{ code:400, message }` for engine/service `InvalidOperationException`
  (message = i18n key or `E-WF-xxx`); `{ code:403, message:"无权限：oa-inbox:batch-transfer" }`
  for a permission miss (`RequirePermissionAttribute.cs:38`).

---

## 4. Manual checks (not automatable in the ps1)

### 4.1 Scenario 1 -- no email (log)

After the ps1's scenario-1 reject with `flowRejected × email = false`, the backend console must
have **no** `[DEV-EMAIL→qa_bt_starter@example.com]` line for the rejection subject
(`您的申请被驳回`). The starter has a real email set by the seed, so the absence proves the
matrix gated it (not merely a blank-address no-op). DB confirmation of the Type=3 row:

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C
```
```sql
-- Type 3 = WfNotificationType.FlowRejected. Expect exactly the count the ps1 asserted (grows once).
SELECT Type, COUNT(*) AS n
FROM Wf_Notification n JOIN Sys_Users u ON u.Id = n.UserId
WHERE u.UserName = 'qa_bt_starter'
GROUP BY Type;
```

### 4.2 Scenario 2 -- legacy flat-pref compatibility (DB direct-write)

The wave keeps honoring the legacy flat notify shape via `NotifyMatrix.IsEnabled`'s fallback
(`NotifyMatrix.cs:62-67`): a legacy `{"notify":{"todo":false}}` closes **both** channels for
`todoCreated`; a legacy global `{"notify":{"email":false}}` closes only email. To exercise it
without going through the new matrix UI, write the legacy shape directly:

```sql
-- Force qa_bt_from's pref to the LEGACY flat form with todo=false:
UPDATE Wf_InboxPref
SET PrefsJson = N'{"notify":{"todo":false}}'
WHERE UserId = (SELECT Id FROM Sys_Users WHERE UserName = 'qa_bt_from');
-- (insert a row first if none exists)
```

Then submit a `qa-bt-line` flow (any starter): a new todo lands on `qa_bt_from`, but because
the legacy `todo=false` fallback closes both channels, **no** `Wf_Notification` Type=1
(`TodoCreated`) row is written for `qa_bt_from` and no `[DEV-EMAIL→...]` line appears. Open the
settings page as `qa_bt_from` and confirm the matrix shows `todoCreated` with both cells off
(the fallback parse reflected in the UI). Restore afterward:
`UPDATE Wf_InboxPref SET PrefsJson = N'{}' WHERE UserId = ...`.

### 4.3 Scenario 3 -- per-task engine audit

Batch transfer calls the real `FlowEngine.TransferAsync` per task (`AdvancedFlow.cs:78-98`), so
each transferred task leaves a `Wf_FlowHistory action=transfer` row (ActorId = the operator,
`qa_bt_admin`) plus a `Wf_FlowFormTo` pair (original row -> `Transferred` with
`ActualHandlerId=from`; new `Pending` row with `ExpectedHandlerId=to`) -- exactly as
`BatchTransferTests.Batch_WritesEngineAudit_HistoryAndFormToPair_PerTask` asserts.

```sql
-- Expect N-1 (=29 with default -N 30) transfer history rows, all ActorId = qa_bt_admin:
SELECT Action, ActorId, COUNT(*) AS n FROM Wf_FlowHistory WHERE Action = 'transfer' GROUP BY Action, ActorId;
-- Expect N-1 FormTo Transferred rows (ActualHandlerId = qa_bt_from) and N-1 new Pending rows (ExpectedHandlerId = qa_bt_to):
SELECT Status, COUNT(*) AS n FROM Wf_FlowFormTo
WHERE ExpectedHandlerId = 'CCCC0000-0000-0000-0000-0000000000D0' OR ActualHandlerId = 'CCCC0000-0000-0000-0000-0000000000C0'
GROUP BY Status;
-- OperLog: the two batch-transfer POSTs are recorded by the global OperLogFilter (operator/from/to in the body).
-- Columns are RequestUrl / HttpMethod (Sys_OperLog.cs:38,44):
SELECT TOP 5 HttpMethod, RequestUrl, CreateDate FROM Sys_OperLog WHERE RequestUrl LIKE '%batch-transfer%' ORDER BY CreateDate DESC;
```

The brief's "second session races to handle the task first" variant is folded into the ps1 as a
simpler deterministic retry: the ps1 explicitly names the **already-handled** task, and the
engine returns `E-WF-002` as a failure-detail row (same 口径 as a single retry). To reproduce the
literal two-session race live: keep the preview open, have `qa_bt_from` handle one previewed task
from a second browser, then confirm the batch execute reports that one as failed while the rest
succeed.

---

## 5. Scenario 5 -- Mobile 375px walkthrough (manual, gstack browse)

Drive a real browser at **viewport 375×812**. Log in through the SPA (dev server or the built
`cp6.web`) and QA the three inbox pages plus the transfer dialog. Save screenshots into this
directory (`mobile-*.png`).

### 5.1 Pending list (`InboxPending.vue`)
1. The list renders as a **card stream** (not a wide table); each card shows flow name / starter
   / sent-at / unread dot.
2. The folder / tab bar is a **horizontally scrollable chip rail**.
3. The **filter drawer** opens from a filter affordance (`oa.inbox.mobileFilter` = "筛选"),
   overlays, and applies.
4. The **rowMode** toggle (`oa.inbox.rowMode.merged` / `.expanded`) is reachable and flips the
   list between merged (one row per instance) and per-task.

### 5.2 FormDetail (`FormDetail.vue`)
1. The left-read / right-sign two-pane collapses into a **vertical stack**.
2. A **pinned bottom action bar** keeps 同意 / 驳回 / 转交 / 退回 tappable without scrolling.

### 5.3 Transfer dialog
1. Opening 转交 (or the batch-transfer dialog `BatchTransferDialog.vue` from FlowAdmin) presents
   a **fullscreen** dialog on mobile; the from/to pickers, preview list, and confirm button all
   fit and are operable.

## 6. Scenario 6 -- Desktop 1280px regression (manual)

At **viewport 1280px**, re-walk the same three pages **plus the settings page** (`InboxSettings.vue`
notify-matrix tab) and confirm zero regression vs the pre-change layout:
- table column widths unchanged; the pending table is a real table (not cards);
- the FormDetail action bar is **non-sticky** on desktop;
- drawers open at roughly **60%** width, not fullscreen;
- the notify-matrix renders as a type×channel grid with the `timeout` row's two cells disabled
  (tooltip `oa.notify.matrix.unsupported`), and a `恢复默认` (`oa.notify.matrix.reset`) action.

---

## 7. Why runtime rows are engine-produced, not raw-seeded

The E-T2 brief's Step-2 asked to WHILE-loop raw-INSERT 30 `Wf_FlowInstance`/`Wf_FlowToken`/
`Wf_FlowTask`/`Wf_FlowFormTo` rows. This harness **deliberately does not** (an anchor-drift
correction, stated honestly):

- Batch transfer invokes the **real engine** `TransferAsync` per task, which mutates
  `task.AssigneeId`, appends a FormTo pair, writes history, and notifies. Those rows must be
  **engine-produced** to stay coherent (token lineage, countersign snapshot, FormTo Pending row).
  A hand-rolled raw fixture would drift from what `SubmitAsync` actually writes and make the
  transfer path non-representative.
- The shipped `BatchTransferTests.cs` seeds pending work the same way -- via `Engine.SubmitAsync`
  in a loop (`BatchTransferTests.cs:39-44`) -- and the `wfs-serial-signing` harness set the
  precedent (seed users + FlowDef only; ps1 submits over HTTP).

So `seed.sql` provisions only the static fixtures; `qa_inbox_ux.ps1` submits `qa-bt-line`
(single-approval, approver=`qa_bt_from`) N times for the batch-transfer fixture, handles one to
create the dirty/done row, and submits one `qa-bt-par3` (parallel-3, all branches ->
`qa_bt_par`) so one instance yields three same-assignee pending tasks for the rowMode scenario.

---

## 8. i18n keys (E-T1, cross-referenced)

`I18nOaInboxUxScreenSeed` seeds **39** keys five-language (ZhCN/ZhTW/En/Ja/Ko), insert-only:
`oa.notify.matrix.*` ×7 + `oa.notify.type.*` ×5 (data-driven `t('oa.notify.type.'+typeKey)`;
`typeKey` from backend `NotifyMatrix.Rows()`: todoCreated/flowApproved/flowRejected/timeout/
branchPruned) + `oa.pref.errBadJson` ×1 + `oa.bt.*` ×23 + `oa.inbox.rowMode.*` ×2 +
`oa.inbox.mobileFilter` ×1. The settings page (`InboxSettings.vue`), batch-transfer dialog
(`BatchTransferDialog.vue` + `FlowAdmin.vue` entry), and pending toggle (`InboxPending.vue`)
reference these; the backend throws the `oa.bt.*` / `oa.pref.*` keys and `E-WF-002` which the UI
resolves.
