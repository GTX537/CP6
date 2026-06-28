# OA 电子表单信箱 Phase C — QA Runbook

**Branch:** `feat/oa-inbox-core`  
**Worktree:** `D:\CP6-oa-core`  
**Date authored:** 2026-06-27  
**Backend port:** `http://localhost:5177`  
**Frontend port:** `http://localhost:5173`

---

## Why live QA is run separately

A concurrent "Space 3D" session occupies the same development environment and connects to `CP6DB` on the shared `localhost\KOUSQLSERVER` instance. Running the OA backend against `CP6DB` at the same time risks schema conflicts and test-data pollution.

**Solution:** point the OA backend at an isolated database `CP6DB_OA` via an environment variable override. On first boot EF applies all migrations (including Phase C migrations for `Wf_FlowDelegate` / `Wf_UserPref`), then Program.cs seeds menus and i18n automatically — no manual DDL needed.

---

## Automated gate status (verified at T16 commit)

| Gate | Result |
|------|--------|
| `dotnet build CP6.WebApi` | **green** |
| `npm run type-check` (vue-tsc) | **green (exit 0)** |
| `npx vitest run` | **5 files / 35 tests passed** |
| `npm run build` (Vite/Rolldown) | **green** |

---

## Step 0 — Prerequisites

1. SQL Server `localhost\KOUSQLSERVER` is running and the login has `dbcreator` rights (or DBA creates `CP6DB_OA` manually first).
2. The Space session (if running) uses its own connection string; it will **not** touch `CP6DB_OA`.
3. Node ≥ 18 is on PATH.

---

## Step 1 — Start the OA backend against the isolated DB

Open **a dedicated PowerShell window** in `D:\CP6-oa-core`:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
dotnet run --project D:\CP6-oa-core\CP6.WebApi
```

**What happens on first run:**

1. EF `db.Database.Migrate()` creates `CP6DB_OA` and applies every migration in order (Phase A token kernel → Phase B read-model L2 → Phase C delegate/pref/catalog migrations).
2. Program.cs seed block runs:
   - Sys_Users admin + default tenant
   - All Sys_Menus including **733/734** (Phase B) + **735** (`/oa/form-catalog` 填單), **736** (`/oa/form-search` 表單查詢), **737** (`/oa/settings` 設定), parent group **740**
   - Phase B 94 i18n词条 + Phase C 38 i18n词条 (nav.735/736/737, oa.catalog.*, oa.initiate.*, oa.settings.*, oa.transfer.*) in five languages (ZhCN/ZhTW/En/Ja/Ko)
3. Backend listens on `http://localhost:5177`.

Look for: `Now listening on: http://localhost:5177` and no `DbUpdateException`.

> **Note:** RabbitMQ / Kafka are not required. The backend degrades gracefully (notifications go to `NullWfNotifier`).

---

## Step 2 — Apply the QA seed

With the backend up (so migrations are applied to `CP6DB_OA`), run in a **cmd or PowerShell** window:

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i D:\CP6-oa-core\docs\superpowers\qa\wfs-form-inbox\phaseB\seed.sql
```

This seeds the base users (starter / approver / cc) plus the `leave` flow. For Phase C flows (delegate, transfer) you will seed additional users below.

### Phase C supplemental seed (inline SQL)

Run in `sqlcmd` against `CP6DB_OA`. Substitute `{BCRYPT_HASH}` with the hash for `123456` (same hash as in phaseB/seed.sql).

```sql
-- Phase C extra user: proxy (will act-as approver)
INSERT INTO Sys_Users (UserId, UserName, LoginName, PassWord, TenantId, Enable, RoleId, CreateDate)
SELECT NEWID(), N'qa_proxy', 'qa_proxy', '{BCRYPT_HASH}', TenantId, 1, 1, GETDATE()
FROM Sys_Users WHERE LoginName = 'admin';

-- Phase C extra user: transfer_to (will receive transferred task)
INSERT INTO Sys_Users (UserId, UserName, LoginName, PassWord, TenantId, Enable, RoleId, CreateDate)
SELECT NEWID(), N'qa_transfer_to', 'qa_transfer_to', '{BCRYPT_HASH}', TenantId, 1, 1, GETDATE()
FROM Sys_Users WHERE LoginName = 'admin';
```

---

## Step 3 — Start the frontend

Open **a second PowerShell window**:

```powershell
cd D:\CP6-oa-core\cp6.web
npm run dev
```

The Vite dev server proxies `/api/*` to `http://localhost:5177`. If port 5173 is occupied:

```powershell
npx vite --port 5175
```

---

## Step 4 — Regenerate i18n types (optional but recommended)

```powershell
cd D:\CP6-oa-core\cp6.web
npm run i18n:pull        # pulls Sys_Lang from backend API into src/i18n/locales/*.json
npm run i18n:gen-types   # re-emits src/i18n/i18n-types.ts
npm run i18n:check       # asserts no raw key leaks
```

> If skipped the frontend still works — the compiled bundle uses `t()` (runtime keys) for the OA views.

---

## Step 5 — Browser QA checklist (gstack headless Chromium)

Run with the `browse` / `gstack` skill in a Claude Code session that has access to the isolated OA environment.

### Flow 1 — act-as 代理审批全流程

**Setup:** Login as admin; go to `/oa/settings` → 委派管理 tab. Add a delegate entry: delegated-by = `qa_approver`, delegated-to = `qa_proxy`, valid period covering today.

**Action sequence:**
1. Login as `qa_proxy` / `123456`. Navigate to `/oa/inbox`.
2. In the top-right, switch **代理身份** selector to `qa_approver`.
3. **Expected:** An act-as banner appears (e.g. "您正在代 qa_approver 处理"). The 未處理 folder loads `qa_approver`'s pending todos.
4. Open one pending item and approve it (comment optional).
5. **Expected DB state (query `CP6DB_OA`):**
   ```sql
   SELECT ActualHandlerId, OnBehalfOfId, HandledAt
   FROM   Wf_FlowFormTo
   WHERE  Status = 1   -- Approved
   ORDER  BY HandledAt DESC;
   ```
   - `ActualHandlerId` = `qa_proxy`'s UserId
   - `OnBehalfOfId` = `qa_approver`'s UserId
6. Open detail → timeline → verify the timeline entry reads **"qa_proxy（代 qa_approver 签）"** (or equivalent proxy label).

### Flow 2 — 转交（Transfer）

**Setup:** Ensure at least one pending task exists for `qa_approver` (start a new leave flow as `qa_starter` if needed).

**Action sequence:**
1. Login as `qa_approver`. Navigate to `/oa/inbox` → 未處理.
2. Open a pending item → click **转交** button → TransferDialog opens.
3. Select recipient = `qa_transfer_to`. Optionally enter a transfer note. Click 確認轉交.
4. **Expected:**
   - Toast shows `oa.transfer.ok` text (e.g. "转交成功").
   - The item **disappears** from `qa_approver`'s 未處理.
5. Login as `qa_transfer_to`. Navigate to 未處理 — the transferred item **appears** there.
6. **Expected DB state:**
   ```sql
   -- Source row should be Transferred (status = 3)
   SELECT Status, ActualHandlerId FROM Wf_FlowFormTo
   WHERE ExpectedHandlerId = (SELECT UserId FROM Sys_Users WHERE LoginName='qa_approver')
     AND Status = 3;   -- 3 = Transferred

   -- New pending row for transfer_to
   SELECT Status, ExpectedHandlerId FROM Wf_FlowFormTo
   WHERE ExpectedHandlerId = (SELECT UserId FROM Sys_Users WHERE LoginName='qa_transfer_to')
     AND Status = 0;   -- 0 = Pending
   ```

### Flow 3 — 填單目录 + 收藏 + 发起

**Action sequence:**
1. Login as `qa_starter`. Navigate to `/oa/form-catalog` (菜单735).
2. **Expected:** FormCatalog renders with a list/grid of available form templates. The `leave` form is listed.
3. Click the ☆ (star/favorite) icon on the `leave` form. **Expected:** Icon fills; the form appears under **我的收藏** tab.
4. Click **填寫** on the `leave` form. **Expected:** Navigates to `/oa/form-initiate?flowKey=leave` (FormInitiate view).
5. In FormInitiate:
   - The flow preview panel shows a forecast (序列图) of the approval path.
   - Fill in the form variables (e.g. reason = "annual leave").
   - Click **提交**.
6. **Expected:**
   - Toast shows `oa.initiate.submitOk` text.
   - Navigate to `/oa/inbox` → 在途 folder — the new instance appears.
   - `qa_approver`'s 未處理 gains a new item.
7. **Expected DB state:**
   ```sql
   SELECT i.Id, i.Status AS InstanceStatus, t.Status AS TokenStatus, t.NodeId
   FROM   Wf_FlowInstance i
   JOIN   Wf_FlowToken t ON t.InstanceId = i.Id
   WHERE  i.Status = 0   -- Running
   ORDER  BY i.CreateDate DESC;
   ```

### Flow 4 — 表單查詢（FormQuery）

**Action sequence:**
1. Login as any user. Navigate to `/oa/form-search` (菜单736).
2. **Expected:** FormQuery view renders with multi-condition search form (flow name, date range, status filter, etc.).
3. Enter a condition (e.g. Status = "Running") and click search.
4. **Expected:** Results grid populates with matching instances. Column headers match `oa.col.*` keys (Flow / Starter / Status / Created At).
5. Click a result row to open detail drawer.
6. **Expected:** Detail drawer shows form content (left) + timeline (right), same as inbox detail.

---

## Step 6 — DB verification queries

Run against `CP6DB_OA` after each flow:

```sql
-- Act-as proxy sign
SELECT fft.StepSeq, fft.NodeId, fft.Status, fft.ExpectedHandlerId, fft.ActualHandlerId,
       fft.OnBehalfOfId, fft.HandledAt
FROM   Wf_FlowFormTo fft
ORDER  BY fft.InstanceId, fft.StepSeq;

-- Transfer history
SELECT fft.StepSeq, fft.Status, fft.ExpectedHandlerId, fft.ActualHandlerId
FROM   Wf_FlowFormTo fft
WHERE  fft.Status IN (0, 3)   -- Pending or Transferred
ORDER  BY fft.InstanceId, fft.StepSeq;

-- Delegate settings
SELECT d.DelegatorId, d.DelegateeId, d.ValidFrom, d.ValidTo, d.IsActive
FROM   Wf_FlowDelegate d
WHERE  d.IsActive = 1;

-- User preferences
SELECT up.UserId, up.PageSize, up.ShowSummary, up.HideCancelled
FROM   Wf_UserPref up;
```

---

## Appendix — i18n verification

After `i18n:pull`, confirm the Phase C keys landed:

```powershell
cd D:\CP6-oa-core\cp6.web
node -e "const k=require('./src/i18n/locales/zh-CN.json'); ['nav.735','nav.736','nav.737','oa.catalog.empty','oa.initiate.submitOk','oa.settings.addDelegate','oa.transfer.title'].forEach(k2 => console.log(k2, k[k2] ? 'OK' : 'MISSING'))"
```

All 38 Phase C keys should print `OK`.
