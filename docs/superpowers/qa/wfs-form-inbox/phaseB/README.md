# OA 电子表单信箱 Phase B — QA Runbook

**Branch:** `feat/oa-inbox-core`  
**Worktree:** `D:\CP6-oa-core`  
**Date authored:** 2026-06-27  
**Backend port:** `http://localhost:5177`  
**Frontend port:** `http://localhost:5173`

---

## Why live QA is run separately

A concurrent "Space 3D" session occupies the same development environment and connects to `CP6DB` on the shared `localhost\KOUSQLSERVER` instance.  Running the OA backend against `CP6DB` at the same time risks schema conflicts (the `WfsPhaseBInboxL2` migration adds `IsRead`/`ReadAt` columns + an index to `Wf_FlowTask`) and test-data pollution.

**Solution:** point the OA backend at an isolated database `CP6DB_OA` via an environment variable override.  On first boot EF applies all migrations (including `WfsPhaseBInboxL2`), then Program.cs seeds menus and i18n automatically — no manual DDL needed.

---

## Automated gate status (already verified — T19 Part 1)

| Gate | Result |
|------|--------|
| `dotnet test` (CP6.Tests) | **1237 passed / 1 skip** |
| `npm run type-check` (vue-tsc) | **green (exit 0, no errors)** |
| `npx vitest run` | **5 files / 33 tests passed** |
| `npm run build` (Vite/Rolldown) | **green — built in ~7.4s** (pre-existing chunk-size warning, unrelated to OA) |

---

## Step 0 — Prerequisites

1. SQL Server `localhost\KOUSQLSERVER` is running and the login has `dbcreator` rights (or DBA creates `CP6DB_OA` manually first).
2. The Space session (if running) uses its own connection string; it will **not** touch `CP6DB_OA`.
3. Node ≥ 18 is on PATH for the i18n scripts.
4. `sqlcmd` is on PATH (usually `C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe`).

---

## Step 1 — Start the OA backend against the isolated DB

Open **a dedicated PowerShell window** in `D:\CP6-oa-core`:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
dotnet run --project D:\CP6-oa-core\CP6.WebApi
```

**What happens on first run:**

1. EF `db.Database.Migrate()` creates `CP6DB_OA` and applies every migration in order, ending with `20260627105302_WfsPhaseBInboxL2` (adds `Wf_FlowTask.IsRead`, `Wf_FlowTask.ReadAt`, index `IX_Wf_FlowTask_AssigneeRead`).
2. Program.cs seed block runs:
   - Sys_Users admin + default tenant
   - All Sys_Menus including **733** (`/oa/inbox` 电子表单信箱) and **734** (`/oa/flow-admin` 流程管理), parent group **740** (OA工作流)
   - 94 OA Phase-B i18n词条 (`nav.733`, `nav.734`, `oa.*`, `E-WF-001`~`E-WF-008`) in five languages
3. Backend listens on `http://localhost:5177`.

Look for: `Now listening on: http://localhost:5177` and no `DbUpdateException`.

> **Note:** RabbitMQ / Kafka are not required. The backend degrades gracefully (notifications go to `NullWfNotifier`). This does not affect the flow engine or read-model.

---

## Step 2 — Apply the QA seed

With the backend up (so migrations are applied to `CP6DB_OA`), run in a **cmd or PowerShell** window (not git-bash — MSYS mangles JSON double-quotes in sqlcmd argv):

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i D:\CP6-oa-core\docs\superpowers\qa\wfs-form-inbox\phaseB\seed.sql
```

This seeds three users (starter / approver / cc) plus a `leave` flow and form definition. See `seed.sql` for details and the mandatory password-hash substitution step.

---

## Step 3 — Start the frontend

Open **a second PowerShell window**:

```powershell
cd D:\CP6-oa-core\cp6.web
npm run dev
```

The Vite dev server proxies `/api/*` to `http://localhost:5177` (see `vite.config.ts`). If the Space session is occupying port 5173, pass a different port:

```powershell
npx vite --port 5175
```

and adjust the BASE_URL accordingly in `qa_oa.sh`.

---

## Step 4 — Regenerate i18n types (optional but recommended)

After the backend is up and `CP6DB_OA` is seeded with词条, regenerate the typed i18n keys so the frontend sees the 94 OA entries:

```powershell
cd D:\CP6-oa-core\cp6.web
npm run i18n:pull        # pulls Sys_Lang from backend API into src/i18n/locales/*.json
npm run i18n:gen-types   # re-emits src/i18n/i18n-types.ts
npm run i18n:check       # asserts no raw key leaks (all keys resolve)
```

> If you skip this step the frontend still works — the compiled bundle already has the types baked in from the build-gate run — but the check is a useful regression guard.

---

## Step 5 — Browser QA checklist (gstack headless Chromium)

Run with the `browse` / `gstack` skill in a Claude Code session that has access to the isolated OA environment.  Each flow below maps to a `/skill browse` invocation or a manual browser verification.

### Flow 1 — 仪表盘渲染
- **Action:** Login as `qa_starter` / `123456` (tenantCode `DEFAULT`). Navigate to `/oa/inbox`.
- **Expected:**
  - The inbox shell renders with a left folder panel (五文件夹: 未處理 / 在途 / 已處理 / 暫存 / CC).
  - The dashboard (default view) shows **4 stat cards**: PendingCount / RunningCount / DoneThisMonth / RejectedBackToMe.
  - No raw i18n keys visible (e.g. no `oa.inbox.pending` literal, no `nav.733` literal).
  - Folder badge for 未處理 shows `0` (no tasks for starter initially).

### Flow 2 — 旧路由重定向
- **Action:** Directly visit `/wf/todo`.
- **Expected:** Vue Router redirects automatically to `/oa/inbox` (router entry: `{ path: '/wf/todo', redirect: '/oa/inbox' }`).  Same for `/wf/my-applications`.

### Flow 3 — 草稿保存 → 提交 → 流程启动
- **Action (as `qa_starter`):**
  1. In the 暫存 folder, create a new draft for the `leave` flow (POST `/api/oa/draft/save` with `{ "flowKey": "leave", "varsJson": "{\"reason\":\"annual leave\"}" }`).
  2. Verify it appears in the 暫存 list (GET `/api/oa/draft/list`).
  3. Submit the draft (POST `/api/oa/draft/submit` with the returned draft `id`).
- **Expected DB state (query `CP6DB_OA`):**
  - `Wf_FlowInstance` row: `Status = 0` (Running), `FlowKey = 'leave'`, `StarterId = qa_starter_id`.
  - `Wf_FlowToken` row: `Status = 0` (Active), `NodeId = 'n1'`.
  - `Wf_FlowFormTo` row: `Status = 0` (待签), `NodeId = 'n1'`, `ExpectedHandlerId = qa_approver_id`.
  - `Wf_FlowTask` row for `qa_approver` at node `n1`, `IsRead = 0`.

### Flow 4 — 审批人审批
- **Action:**
  1. Login as `qa_approver` / `123456`.
  2. Navigate to `/oa/inbox` → 未處理 tab.
  3. The item from Flow 3 appears **bold** (unread).
  4. Open the detail: left side shows the read-only leave form (reason field visible); right side shows the flow timeline with current node `n1` highlighted and a greyed-out forecast for `end`.
  5. Approve (comment optional).
- **Expected:**
  - `Wf_FlowInstance.Status = 1` (Approved / 通过).
  - `Wf_FlowFormTo.Status = 1`, `ActualHandlerId = qa_approver_id`, `HandledAt` set.
  - `Wf_FlowToken` at `n1` Consumed; token at `end` Consumed (instance drained).
  - `Wf_FlowTask.IsRead = 1` after opening detail (MarkTaskRead called).

### Flow 5 — CC 用户信箱
- **Action:**
  1. Login as `qa_cc` / `123456`.
  2. Navigate to `/oa/inbox` → 未處理 → CC sub-tab.
  3. Item appears (unread).
  4. Open it → mark read.
  5. Switch to 已處理 → CC tab → item queryable (month filter defaults to current month).
- **Expected:**
  - `Wf_FlowCc` row: `RecipientId = qa_cc_id`, `IsRead = 1` after mark-read.

### Flow 6 — 批量办理
- **Action:**
  1. Seed or start **two** additional leave instances (repeat Flow 3 twice with `qa_starter`).
  2. Login as `qa_approver`. In 未處理, select both items via checkbox.
  3. Click 批量审批 (batch approve).
- **Expected:**
  - API POST `/api/oa/inbox/batch` returns `{ data: [ { taskId, ok: true }, { taskId, ok: true } ] }`.
  - Per-item success toast shown in the UI.
  - Both `Wf_FlowInstance` rows reach `Status = 1`.

### Flow 7 — 流程管理冲突 (E-WF-008)
- **Action:**
  1. Login as any user with role 1.
  2. Navigate to `/oa/flow-admin` — verify the `leave` flow is listed with `Enable = true`.
  3. Seed a **second** flow definition with `FormKey = 'leave'` (see `seed.sql` comment block) but `Enable = false`.
  4. Attempt to enable the second flow: POST `/api/oa/flow-admin/enable` with `{ "flowKey": "leave2", "enabled": true }`.
- **Expected:**
  - Backend returns HTTP 400 with body containing `"E-WF-008"`.
  - Frontend shows the localized error toast (Chinese: e.g. "每张表单只能有一个启用的流程").
  - The enable switch in the UI reverts to off.

---

## Step 6 — HTTP e2e (lower-friction alternative)

See `qa_oa.sh` for a curl-based skeleton covering the same flows without a browser. Run it after seed and backend are up:

```bash
bash D:/CP6-oa-core/docs/superpowers/qa/wfs-form-inbox/phaseB/qa_oa.sh
```

The script logs `PASS=N FAIL=0` when all checks pass.

---

## Appendix — DB verification queries

Run against `CP6DB_OA` after each flow:

```sql
-- Active tokens per instance
SELECT i.Id, i.FlowKey, i.Status AS InstanceStatus,
       COUNT(t.Id) FILTER (WHERE t.Status = 0) AS ActiveTokens,
       COUNT(t.Id) FILTER (WHERE t.Status = 1) AS ConsumedTokens
FROM   Wf_FlowInstance i
LEFT   JOIN Wf_FlowToken t ON t.InstanceId = i.Id
GROUP  BY i.Id, i.FlowKey, i.Status;

-- Pending tasks for approver
SELECT ft.Id, ft.AssigneeId, ft.NodeId, ft.IsRead
FROM   Wf_FlowTask ft
WHERE  ft.Status = 0;   -- 0=Pending

-- FlowFormTo timeline for an instance
SELECT fft.StepSeq, fft.NodeId, fft.Status, fft.ExpectedHandlerId, fft.ActualHandlerId, fft.HandledAt
FROM   Wf_FlowFormTo fft
ORDER  BY fft.InstanceId, fft.StepSeq;
```
