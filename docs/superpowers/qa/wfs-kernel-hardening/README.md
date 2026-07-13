# WFS Kernel Hardening (E-T2) -- QA Runbook

**Branch:** `feat/wfs-kernel-hardening`
**Date:** 2026-07-13
**Feature scope:** inclusive gateway (`inclusiveSplit` / `inclusiveJoin`, condition-set
activation + default fallback, dynamic-count join) + branch-reject policy
(`onBranchReject` = `prune` | `cascade`) + send-back three-rule matrix
(SameBranch strip / SiblingBranch reject = `E-WF-019` / BeforeSplit full-clear) +
designer palette/property-panel/client-validation mirror (`oa.designer.gw.*`,
`oa.designer.errInclusiveDefault|errInclusivePair|errBranchReject`,
backend `E-WF-020` / `E-WF-021`) + i18n (E-T1).

> **Status: written, not run.** This harness is authored per task E-T2 (write-only).
> Live QA -- spin the backend against the isolated DB, run the ps1 HTTP e2e, drive
> the designer in a real browser -- is executed later by the main agent with a QA
> user present. **Nothing here has been executed.** Bugs found during live QA are
> fixed TDD (regression test into `CP6.Tests/Wf/**`).

---

## 1. What this harness covers

Six runtime scenarios (HTTP e2e, `qa_kernel_hardening.ps1`) plus one real-browser
designer scenario (manual, gstack). Three deliverables:

| File | Purpose |
| --- | --- |
| `seed.sql` | 5 users + 1 FormDef + 4 FlowDefs (raw INSERT into `CP6DB_OA`). |
| `qa_kernel_hardening.ps1` | HTTP e2e over scenarios 1-6 against a running backend (ASCII data). |
| `README.md` | This runbook: setup, scenario matrix, expected results, browser steps. |

### Scenario matrix

| # | FlowKey | Mechanic | Submit vars | Expected terminal outcome |
| --- | --- | --- | --- | --- |
| 1 | `khd-inclusive` | inclusive split, 2 of 3 conditions true | `{"goA":1,"goB":1}` | Exactly **2 todos** (A, B); C & default D get none; both approve -> `inclusiveJoin` dyn-counts 2 -> **Approved**. |
| 2 | `khd-inclusive` | inclusive split, all conditions false | `{}` | Only the **default** branch D gets a todo; approve -> **Approved**. |
| 3 | `khd-prune` | `onBranchReject=prune`, reject one branch | `{}` | A rejects -> A token **Pruned**, instance stays **Running**, B todo alive, starter gets **BranchPruned** notification (`Wf_Notification.Type=5`); B approves -> **Approved**. |
| 4 | `khd-cascade` | default cascade, reject one branch | `{}` | A rejects -> instance **Rejected**, B todo voided (bit-equal to today). |
| 5 | `khd-sameback` | SameBranch send-back | `{}` | a2 sends back to a1 -> only the A branch stripped, **b1 survives**; rerun A + approve B -> **Approved** (reborn keeps ForkId, join recognises kin). |
| 6 | `khd-sameback` | SiblingBranch send-back | `{}` | a2 sends back to b1 -> **HTTP 400 `E-WF-019`**, nothing mutated (verify-before-write). |
| 7 | (designer) | palette drag + client validation | -- | Manual browser: drag `inclusiveSplit`/`inclusiveJoin`, set branch-reject policy, delete default edge -> save blocked by `oa.designer.errInclusiveDefault` (E-WF-020 mirror), i18n text renders in the active language. |

---

## 2. Environment setup

### 2.1 Isolated database

Reuse the `CP6DB_OA` database from prior WFS QA sessions, or create fresh:

```sql
CREATE DATABASE CP6DB_OA;
```

Point the backend at it so QA data never touches live `CP6DB`. On first boot
`db.Database.Migrate()` applies all EF migrations. **This wave adds no migration**
(hardening touches only `FlowNode.OnBranchReject` SchemaJson POCO + the
`FlowTokenStatus.Pruned` constant + the `WfNotificationType.BranchPruned=5` constant --
no new columns), so an existing `CP6DB_OA` needs no schema change.

### 2.2 Apply seed

Run from a **native shell** (cmd / PowerShell), not git-bash:

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
```

The seed prints a 10-row sanity report (5 users + 1 form + 4 flowdefs). All users
share password `123456` (admin's BCrypt hash cloned). `SET QUOTED_IDENTIFIER ON` is
set in the script (required by the filtered unique indexes on `Wf_FlowDef`).

### 2.3 Backend

Prior WFS QA sessions used ports 5177-5180; start this one on **5181**:

```powershell
cd <repo>\CP6.WebApi
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --urls "http://localhost:5181"
```

No background worker is involved in this harness -- every submit/approve/send-back is
synchronous within the request.

---

## 3. Running the HTTP e2e

```powershell
.\qa_kernel_hardening.ps1
.\qa_kernel_hardening.ps1 -BaseUrl http://localhost:5181
```

The script logs in the 5 seeded users, then walks scenarios 1-6, printing
`PASS`/`FAIL`/`WARN` and a final tally.

### Endpoints exercised

| Purpose | Call |
| --- | --- |
| Login | `POST /api/auth/login` `{ userName, password }` |
| Submit | `POST /api/wf/flow/submit` `{ flowKey, varsJson }` -> `data.instanceId` |
| Approve / reject | `POST /api/wf/task/{taskId}/act` `{ approve, comment }` |
| Send-back | `POST /api/wf/advanced/sendback` `{ taskId, targetNodeId, comment }` |
| Pending inbox | `GET /api/oa/inbox/pending` -> `InboxPendingItem[]` (`.taskId .instanceId .nodeId`) |
| Detail (status) | `GET /api/oa/inbox/detail/{id}` -> `.instance.status` |
| Notifications | `GET /api/oa/notification/list` -> `NotificationItem[]` (`.type .instanceId`) |

Envelope: `{ code: 0, message: "OK", data: <payload> }`.
Instance status (`FlowInstanceStatus`): `0 Running 1 Approved 2 Rejected 3 Withdrawn 4 Suspended 5 Draft`.
Notification type (`WfNotificationType`): `5 = BranchPruned`.

### Task selection

Because the four branch approvers reuse the same accounts across flows, the script
selects each task by **(instanceId, nodeId)** from the acting user's pending inbox --
never "the single pending task", which would be ambiguous across a re-run. The
`GET /api/oa/inbox/detail/{id}` endpoint applies no owner filter today (any
authenticated user reads any instance detail), so the starter session can poll every
instance's status directly. If that endpoint ever gains an owner check, the status
polls here need a same-branch session instead -- that is this known assumption
breaking, not a hardening bug.

---

## 4. Why the FlowDefs are seeded by raw INSERT

Same principle as the `wfs-service-task` harness: `FlowSchemaValidator.Validate`
runs **only** through `DesignerService.SaveAsync`. A raw `INSERT` into `Wf_FlowDef`
skips it, so the seeded schemas stand exactly as written. Unlike the service-task
harness (whose failure flows deliberately reference an unregistered connector that
the designer would reject), **all four flows here are valid** and would also pass
`E-WF-020`/`E-WF-021`; they are seeded raw purely for consistency with the
established QA-harness pattern and to keep the seed self-contained.

Key runtime facts that make the scenarios deterministic:

- **Condition evaluation** (`ExpressionEvaluator`): an edge `condition` reads the
  submit `varsJson`. An **unknown field safe-fails to FALSE**; an empty/absent
  condition is the unconditional **default** edge. `inclusiveSplit` activates every
  conditional edge that is TRUE, or -- if none are -- the single default edge
  (`E-WF-020` guarantees exactly one). So scenario 1 `{"goA":1,"goB":1}` lights A+B
  (C false, default not taken); scenario 2 `{}` lights only the default.
- **Dynamic-count join**: `inclusiveJoin` / `parallelJoin` count over the live
  fork batch. Pruned tokens drop out of the wait set, which is why scenario 3's join
  releases on B alone.
- **Prune vs cascade**: `onBranchReject=prune` on the split node makes a branch
  rejection strip only that branch (token -> `Pruned`, its Pending `Wf_FlowFormTo` ->
  `Voided`) and fire `BranchPrunedAsync` (notification Type 5) to the starter; the
  instance stays `Running`. No `onBranchReject` (or `cascade`) keeps today's behaviour:
  one rejection terminates the whole instance (`Rejected`). If **every** branch is
  pruned, the engine bubbles up and the instance collapses to `Rejected`.
- **Send-back three rules**: SameBranch strips the acting branch back to the target
  and reborns the token carrying the stripped layer's lineage (same `ForkId`) so the
  outer join still recognises kin; a SiblingBranch target is rejected `E-WF-019`
  **before any mutation**; BeforeSplit / starter targets fall back to today's
  full-clear (not exercised over HTTP here -- covered by `SendBackThreeRuleTests`).

---

## 5. Scenario 7 -- Real-browser designer (manual, gstack)

Component drag/drop and the palette->canvas->save round-trip have **no unit
coverage** (D-T2 review) -- these steps are the safety net. Log in as any seeded user,
open the OA flow designer.

### 5.1 Palette -> canvas (inclusive gateway rendering)

1. Drag **包容分叉 / Inclusive Split** (`inclusiveSplit`) and **包容汇聚 /
   Inclusive Join** (`inclusiveJoin`) from the left palette onto the canvas.
2. Confirm each renders with the BPMN inclusive glyph -- a **diamond with an inset
   hollow circle** (distinct from the parallel gateway's solid-plus diamond); palette
   dots for the two are **hollow** (`background:transparent; border:2px solid var(--cp-warn)`).
3. Confirm the node label shows the i18n text for the active language
   (`oa.designer.gw.inclusiveSplit` / `oa.designer.gw.inclusiveJoin`).

### 5.2 Property panel -- branch reject policy

1. Select an `inclusiveSplit` (or `parallelSplit`) node.
2. Confirm the property panel shows a **分支驳回策略 / Branch Reject Policy**
   (`oa.designer.gw.branchReject`) select with options **整单驳回（默认） /
   Reject whole instance** (`cascade`) and **仅剪除本分支 / Prune this branch**
   (`prune`), plus the hint line (`oa.designer.gw.branchRejectHint`).
3. Confirm default `cascade` does **not** write `onBranchReject` into the schema
   (zero pollution of legacy flows); choosing `prune` writes the field; switching
   back to `cascade` clears it. The segment appears only on split-type nodes.

### 5.3 Client validation i18n (E-WF-020 mirror)

1. Wire `start -> inclusiveSplit -> (>=2 branches) -> inclusiveJoin -> end`.
2. Give the split **two conditional out-edges and no unconditional default edge**
   (or two defaults). On save, the client validator (`designerModel.ts validateClient`)
   must surface **`oa.designer.errInclusiveDefault`** (mirror of backend `E-WF-020`)
   and block the save.
3. Break the pairing (e.g. point the split at a `parallelJoin`) -> expect
   **`oa.designer.errInclusivePair`** (`E-WF-021` mirror). Set `onBranchReject` to a
   bad value / on a non-split node -> expect **`oa.designer.errBranchReject`**.
4. Switch the UI language (ja / zh-CN / zh-TW / en / ko) and re-trigger a validation
   error to confirm all five locales render (E-T1 seeded 12 keys into
   `I18nOaKernelHardeningScreenSeed`). Confirm the backend rejection also localises:
   `E-WF-019/020/021` resolve to seeded messages, not bare codes.

---

## 6. Manual DB checks (post-run)

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C
```

```sql
-- Scenario 3: pruned token + BranchPruned notification (Type 5) + history:
SELECT t.NodeId, t.Status FROM Wf_FlowToken t
JOIN Wf_FlowInstance i ON i.Id = t.InstanceId WHERE i.FlowKey = 'khd-prune';
SELECT Type, Title, InstanceId FROM Wf_Notification WHERE Type = 5 ORDER BY CreateDate DESC;
SELECT InstanceId, NodeId, Action FROM Wf_FlowHistory
WHERE Action IN ('branchPruned','sendback','inclusiveSplit') ORDER BY CreateDate DESC;

-- Scenario 4: cascade produced NO Pruned token, instance Rejected:
SELECT i.Status, COUNT(CASE WHEN t.Status = 5 THEN 1 END) AS pruned_tokens
FROM Wf_FlowInstance i LEFT JOIN Wf_FlowToken t ON t.InstanceId = i.Id
WHERE i.FlowKey = 'khd-cascade' GROUP BY i.Status;   -- FlowTokenStatus.Pruned = 5
```

Expected: scenario 3 -> one `Pruned` token at `a` + one Type-5 notification;
scenario 4 -> instance `Rejected` with zero `Pruned` tokens.

---

## 7. i18n keys (E-T1, cross-referenced)

The 12 keys below are seeded five-language by `I18nOaKernelHardeningScreenSeed`
(Program.cs concat, insert-only). All are referenced by the front-end designer or
thrown by the backend engine/validator:

| Key | Referenced by |
| --- | --- |
| `oa.designer.gw.inclusiveSplit` / `.inclusiveJoin` | `InclusiveGatewayNode.vue` |
| `oa.designer.gw.branchReject` / `.cascade` / `.prune` / `oa.designer.gw.branchRejectHint` | `NodePropertyPanel.vue` |
| `oa.designer.errInclusiveDefault` / `errInclusivePair` / `errBranchReject` | `designerModel.ts validateClient` |
| `E-WF-019` / `E-WF-020` / `E-WF-021` | `AdvancedFlow.cs` / `InclusiveSplitNodeHandler.cs` / `FlowSchemaValidator.cs` |
