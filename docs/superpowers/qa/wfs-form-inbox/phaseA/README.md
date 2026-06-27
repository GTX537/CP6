# WFS Phase A — real-stack e2e QA (token runtime + read-model)

**Branch:** `feat/wfs-form-inbox`
**Date:** 2026-06-27
**Stack under test:** REAL — SQL Server `localhost\KOUSQLSERVER` / DB `CP6DB`, backend `CP6.WebApi` (`http://localhost:5177`), frontend `cp6.web` Vite (`http://localhost:5173`), driven through a real headless Chromium (gstack `browse`).

Phase A's logic is already exhaustively unit-tested (1212 green on InMemory + SQLite). This run covers the four gaps only a real run can close:

1. The two new EF migrations apply to **real SQL Server**.
2. Engine + read-model writes persist correctly through the real **HTTP → EF → SQL Server** path.
3. A **parallel** approval flow drives end-to-end through the API and the existing 待办中心 UI (`/wf/todo`).
4. The existing **linear** approval still works (compat).

**Result: DONE. All four gaps validated. No bug found. No code changed.**

Phase A adds no frontend — the UI exercised is the existing `cp6.web/src/views/wf/TodoCenter.vue`.

---

## What was verified

### Gap 1 — migrations apply to real SQL Server ✅
Pre-state: `__EFMigrationsHistory` did **not** contain the two WFS migrations and the new tables did not exist. On backend boot (`db.Database.Migrate()`):

- Startup log: `Applying migration '20260626201249_WfsPhaseAKernel'` then `Applying migration '20260626202009_WfsPhaseAReadModel'`, with the `ALTER TABLE [Wf_FlowInstance] ADD [RowVersion] rowversion`, `CREATE TABLE [Wf_FlowToken]`, `Wf_FlowCc`, `Wf_FlowData`, `Wf_FlowFormTo` DDL — **no `DbUpdateException` / no SQL error**.
- Post-state confirmed in DB:
  - `__EFMigrationsHistory` now has `20260626201249_WfsPhaseAKernel` + `20260626202009_WfsPhaseAReadModel`.
  - New tables present: `Wf_FlowToken`, `Wf_FlowData`, `Wf_FlowFormTo`, `Wf_FlowCc`.
  - New columns present: `Wf_FlowInstance.RowVersion` (rowversion/timestamp), `Wf_FlowTask.TokenId` (uniqueidentifier).
- The idempotent `WfTokenBackfillSeed` also ran (log shows `SELECT ... FROM [Wf_FlowToken]` + `INSERT INTO [Wf_FlowToken]`): the pre-existing in-flight `budget-approve` instance got a backfilled root token and remained actionable in 待办中心 (visible in screenshot 01).

### Gap 2/3 — parallel flow end-to-end through API + real 待办中心 UI ✅
Flow `qa_parallel`: `start → parallelSplit → [approval a = admin, approval b = qa_user_b] → parallelJoin → end`.

Drive (REAL BROWSER, gstack `browse` headless Chromium):
1. Logged in at `http://localhost:5173` as **admin / 123456** (cookie `cp6_at` set).
2. Started a `qa_parallel` instance. The 待办中心 has no 发起/填单 page, so the instance was started by the browser's own same-origin `fetch('/api/wf/flow/submit', …)` (cookies auto-sent; dev `Security:Csrf:Enabled=false`), then the UI was refreshed — instance `d4514a8a-ba1b-4832-af8c-7f94b7701282`.
3. admin saw branch-a (node `a`) in `/wf/todo` (screenshot 01) and **approved it via the UI dialog** (screenshot 02). Instance stayed **Running** — branch-a's token advanced to `join` and parked (1/2 arrived); branch-b's token still Active at `b`. admin's todo then showed only the pre-existing `budget-approve` (screenshot 03).
4. Logged out, logged in as **qa_user_b / 123456**; `/wf/todo` showed **only** branch-b (node `b`) — tenant/assignee scoping correct (screenshot 04). Approved it via the UI dialog (screenshot 05).
5. Instance reached **Approved**; qa_user_b's todo emptied (screenshot 06); admin's `/wf/my-applications` shows `qa_parallel` at node `end`, status **承認 (Approved)** (screenshot 07).

Final read-model in CP6DB for the parallel instance:

```
(1) Wf_FlowInstance:  Status=1 (Approved)  CurrentNode=end

(2) Wf_FlowToken (all Consumed, zero Active):
    split  Status=1  fork=(root)            <- root token, consumed at split
    join   Status=1  fork=BFBC0745-...      <- branch-a child, advanced a->join
    join   Status=1  fork=BFBC0745-...      <- branch-b child, advanced b->join
    end    Status=1  fork=(root)            <- join continuation, advanced join->end
    COUNT  Active(0)=0  Consumed(1)=4  Cancelled(2)=0

(3) Wf_FlowFormTo (both approval关卡 Approved w/ handler + timestamp, zero Pending):
    seq1  a  Status=1  expect=E551EED9-...(admin)      actual=E551EED9-...(admin)      handledAt=2026-06-27 04:47:20  comment="QA approve branch-a (admin)"
    seq2  b  Status=1  expect=0B000000-...(qa_user_b)  actual=0B000000-...(qa_user_b)  handledAt=2026-06-27 04:50:04  comment="QA approve branch-b (qa_user_b)"
    COUNT Pending(0)=0  Approved(1)=2

(4) Wf_FlowData (送签 + 办结 snapshot per关卡):
    seq1 node=a {}   seq1 node=a {}     <- branch-a: on-enter + on-handle
    seq2 node=b {}   seq2 node=b {}     <- branch-b: on-enter + on-handle
    total snapshots=4
```

The two parallel children share one `ForkId` (`parallelJoin` 认亲计数), both children + root + the join continuation all end `Consumed`, instance drains to `Approved`. Read-model (`Wf_FlowFormTo` traçabilité + `Wf_FlowData` per-step snapshots) persisted exactly as the unit tests assert — now confirmed through the real HTTP→EF→SQL Server path.

### Gap 4 — linear flow compat ✅
Flow `qa_linear`: `start → approval(admin) → end` (isolated; no business-callback binding, unlike `pr/po/budget-approve`). Submitted via the browser, approved via the UI dialog. Instance `3b91e0b2-5875-441f-b66a-d91744dcd5f1`:

- Post-submit: exactly one Active token at node `a`, one Pending task (classic single-token linear behavior — zero diff vs the pre-Phase-A engine).
- After approve: `Status=1 (Approved)`, `CurrentNode=end`, single token `Consumed`, task `a` Approved, `Wf_FlowFormTo` row Approved (actual=admin), 2 `Wf_FlowData` snapshots at node `a` (送签 + 办结).

---

## Environment notes / gotchas

- Backend run with `ASPNETCORE_ENVIRONMENT=Development` so `appsettings.Development.json` applies (`Security:Csrf:Enabled=false`). Connection string from gitignored `appsettings.Local.json`.
- RabbitMQ (5672) and Kafka (29092) are not running locally; the backend logs a graceful degrade warning (notifications skipped via `NullWfNotifier`). This does not affect the flow engine or read-model.
- admin's BCrypt password hash verifies against `123456` (confirmed by a real login). `qa_user_b` is a clone of admin's auth columns (same hash) in the same tenant, so it logs in with `123456` too.
- **git-bash + native sqlcmd.exe quoting:** MSYS backslash-escapes embedded `"` in argv, which mangles JSON literals. The seeds were therefore applied via `-Q` with JSON written using `~` placeholders converted by `REPLACE(...,'~',CHAR(34))`. `seed.sql` in this folder keeps the readable double-quote form (valid for `sqlcmd -i` from cmd/PowerShell).

## Files
- `seed.sql` — the QA seed (qa_user_b + qa_parallel + qa_linear, idempotent, tenant A1).
- `01-admin-todo-list.png` — admin 待办中心: qa_parallel/`a` + pre-existing budget-approve.
- `02-admin-approve-dialog.png` — admin approving branch-a.
- `03-admin-todo-after-approve.png` — branch-a gone; instance still Running (join waiting).
- `04-userb-todo-list.png` — qa_user_b sees only branch-b (`b`).
- `05-userb-approve-dialog.png` — qa_user_b approving branch-b.
- `06-userb-todo-empty.png` — qa_user_b todo empty after final approval.
- `07-admin-my-applications.png` — qa_parallel at `end`, status 承認 (Approved).
