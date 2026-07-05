# WFS Service Task (E-T3) -- QA Runbook

**Branch:** `feat/wfs-service-task-finish`
**Date:** 2026-07-05
**Feature scope:** serviceTask node full chain -- engine dispatch (sync / async / timer),
async job queue + lease worker, dataWriteback + webApi executors, EchoConnector,
timer due-time computation, error edges (`isError`), designer palette + property
panel + validation (E-WF-016/017/018), service catalog endpoint, i18n.

> **Status: written, not run.** This harness is authored per task E-T3 (write-only).
> Live QA (spin up the backend against the isolated DB, run the ps1, drive the
> designer in a real browser) is executed later by the main agent with a QA user
> present. Nothing here has been executed.

---

## 1. What this harness covers

Six runtime scenarios (HTTP e2e, `qa_service_task.ps1`) plus real-browser designer
scenarios (manual, gstack). The three deliverables:

| File | Purpose |
| --- | --- |
| `seed.sql` | 2 users + 1 FormDef + 6 FlowDefs (one per scenario), raw INSERT into `CP6DB_OA`. |
| `qa_service_task.ps1` | HTTP e2e over the 6 scenarios against a running backend (ASCII data). |
| `README.md` | This runbook: setup, scenario matrix, expected results, browser steps. |

### Scenario matrix

| # | FlowKey | serviceKind / mode | Action | Expected terminal outcome |
| --- | --- | --- | --- | --- |
| 1 | `svc-sync-writeback` | dataWriteback / sync | `sampleWriteback` | Instance **Approved** at submit; VarsJson has `writebackEcho`. |
| 2 | `svc-async-webapi` | webApi / async | `erpEcho` `/erp/echo/{amount}` | Submit -> Running + 1 Pending job; after worker scan **Approved**; VarsJson has `echoedPath`. |
| 3 | `svc-timer-wait` | timer / async (forced) | none (pure wait) | Submit -> Running; after 10s due + scan **Approved**. |
| 4 | `svc-timer-action` | timer / async (forced) | `erpEcho` at due time | Submit -> Running; after due + scan **Approved**; VarsJson has `echoedPath`. |
| 5 | `svc-fail-erroredge` | webApi / async | GHOST connector (unregistered) | Fail -> retries exhausted -> **IsError edge** -> human node; instance stays **Running**; `wf.serviceError` in VarsJson. |
| 6 | `svc-fail-suspend` | webApi / async | GHOST connector (unregistered) | Fail -> retries exhausted -> no error edge -> **Suspended (4)**; `wf.serviceError` in VarsJson. |

---

## 2. Environment setup

### 2.1 Isolated database

Reuse the `CP6DB_OA` database from prior WFS QA sessions, or create fresh:

```sql
CREATE DATABASE CP6DB_OA;
```

Point the backend at it so QA data never touches live `CP6DB`.

### 2.2 Apply migrations + seed

On first boot `db.Database.Migrate()` applies all EF migrations, including
`20260629142700_WfsServiceTask` which creates the **`Wf_ServiceJob`** table
(columns: `Kind`, `ActionRefJson`, `DueAtUtc`, `Status`, `AttemptCount`,
`MaxAttempts`, `NextAttemptAtUtc`, `LockedBy`/`LockedAtUtc`/`LockExpiresAtUtc`
lease trio, `LastError`, `RowVersion`) plus indexes `IX_Wf_ServiceJob_Instance`,
`IX_Wf_ServiceJob_Scan`, and the filtered unique index
`UX_Wf_ServiceJob_LiveToken` on `(TenantId, TokenId, NodeId) WHERE Status IN (0,1)`.

Confirm the migration applied, then seed (native shell, **not** git-bash):

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -Q "SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId LIKE '%WfsServiceTask%'"
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
```

The seed prints a 9-row sanity report (2 users + 1 form + 6 flowdefs).

### 2.3 Backend

Prior sessions use ports 5177 (Space) / 5178 (serial) / 5179 (approver); start
this one on **5180**:

```powershell
cd <repo>\CP6.WebApi
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --urls "http://localhost:5180"
```

The `WfServiceJobScanWorker` background service starts automatically and scans
every **20 seconds** (`WfServiceJobScanWorker.Interval`). There is **no manual
scan endpoint** -- async scenarios wait for the worker.

---

## 3. Running the HTTP e2e

```powershell
.\qa_service_task.ps1                                   # defaults to http://localhost:5180
.\qa_service_task.ps1 -BaseUrl http://localhost:5180 -WaitSeconds 90 -PollSeconds 5
```

Both seeded users share password `123456` (admin's BCrypt hash cloned in seed).
The script logs in `qa_svc_starter` (submits all flows) and `qa_svc_appr` (the
error-branch approver for scenario 5), then walks scenarios 1-6, printing
`PASS`/`FAIL`/`WARN` and a final tally. Async scenarios poll instance detail via
`GET /api/oa/inbox/detail/{id}` until the expected status appears (bounded by
`-WaitSeconds`, default 90; a full run can take ~2-3 minutes because of the 20s
scan cadence + 10s timer durations).

### Timing expectations

- **Scenario 1** resolves *inside* the submit call (sync path runs the executor
  inline and advances the token in the same transaction) -- no wait.
- **Scenarios 2-6** park a token + enqueue a `Wf_ServiceJob`; settling requires
  one worker scan (<=20s) for webApi/fail flows, or 10s due + one scan for timers.

---

## 4. Why the FlowDefs are seeded by raw INSERT (failure-flow principle)

`DesignerService.SaveAsync` runs a **save-time registration check** (E-WF-018):
a webApi node's `serviceConnectorName` must match a registered `IWfConnector.Name`,
and a dataWriteback node's `serviceActionName` must match a registered
dataWriteback executor `Key`. Scenarios 5 & 6 deliberately reference
**`ghostConnector`**, which is *not* registered -- so saving them through the
designer would throw **E-WF-018** and reject the flow.

To stage the failure path we therefore **bypass the designer** and INSERT the
FlowDef rows directly into `Wf_FlowDef` (exactly what `seed.sql` does). Key facts
that make this work and matter for interpreting results:

- `FlowSchemaValidator.Validate` runs **only** through `DesignerService.SaveAsync`.
  A raw INSERT skips it entirely, so the seeded schemas stand as written. (This is
  the same reason the *valid* flows 1-4 are also seeded raw -- consistency.)
- The runtime failure we want is precisely the async worker calling
  `WebApiExecutor` -> connector lookup miss -> `ServiceTaskResult.Fail("E-WF-018 ...")`.
- **Retry tuning:** `serviceMaxRetries: 0` => `job.MaxAttempts = retries + 1 = 1`.
  An async job enqueues with `AttemptCount = 0`; the first worker scan increments
  to 1, fails, and `1 < 1` is false => retries exhausted on the **first scan** =>
  immediate route (no 30s exponential-backoff wait). This keeps live QA fast.
- On exhaustion, `FlowEngine.FailServiceTokenAsync` **direct-writes**
  `wf.serviceError { nodeId, message, failedAtUtc }` into `inst.VarsJson` (the
  reserved `wf.` namespace is written on this controlled path, unlike the
  `MergeOutputVars` path which blocks `wf.*`), then routes along the `isError`
  edge if one exists, else `Suspend`.

> If a future EchoConnector gains a failure-injection switch, scenarios 5/6 could
> instead use a *registered* connector forced to fail (savable through the
> designer). As of this writing `EchoConnector.CallAsync` always returns `Ok`
> (no failure switch), so the unregistered-connector approach is the one used.

---

## 5. Real-browser designer scenarios (manual, gstack)

Component round-trip and drag-drop have **no unit coverage** -- these steps are
the safety net (D-T2 review, 2026-07-05). Log in as `qa_svc_starter`, open the
flow designer (`/api/oa/designer` REST behind the OA designer view).

### 5.1 Palette -> canvas -> save -> reload round-trip (must pass)

The left palette exposes **three** serviceTask entries (same node type, distinct
preset `serviceKind`):

- **数据回写** -> `serviceKind: dataWriteback`
- **接口调用** -> `serviceKind: webApi`
- **定时器** -> `serviceKind: timer`

Steps:

1. Drag all three onto the canvas (each drop preloads its `serviceKind` via the
   palette `dragKey = serviceTask:<kind>`).
2. Wire a minimal valid flow (start -> each serviceTask -> ... -> end) and fill
   the required per-kind fields (see 5.2) so `FlowSchemaValidator` (E-WF-016) passes.
3. **Save** the flow.
4. **Reload** the flow (Load by flowKey).
5. Confirm each of the three nodes retains its correct `serviceKind` after the
   round-trip (schemaToGraph/graphToSchema must preserve the drop-point preset).

### 5.2 Property panel per kind

Select each serviceTask node and confirm the property panel shows kind-specific
fields (camelCase in the schema):

- **数据回写 (dataWriteback):** `serviceActionName` picker sourced from
  `GET /api/oa/designer/service-catalog` -> `actions` (only dataWriteback
  executors with `VisibleInDesigner=true`; `sampleWriteback` should appear,
  `webApi` should **not**). Optional `serviceMode` sync/async.
- **接口调用 (webApi):** `serviceConnectorName` picker from the catalog
  `connectors` list (`erpEcho` / "ERP Echo (demo)" should appear) + `servicePath`
  + optional `serviceParamsJson`. E-WF-016 requires both connector and path.
- **定时器 (timer):** `serviceDelayMode` (`duration` / `untilDate` / `untilExpr`)
  + `serviceDelayValue`. E-WF-016 requires both.

### 5.3 Error-edge checkbox (E-WF-017)

- On an edge leaving a serviceTask node, toggle the **error edge** (`isError`)
  checkbox in the edge property panel.
- Confirm: at most one `isError` edge per node is allowed; an `isError` edge is
  permitted **only** from a serviceTask node (E-WF-017 rejects otherwise).
- Confirm the client-side validator (`validateClient`) mirrors the server error
  codes before save.

### 5.4 Observation check (D-T2)

Look at `ServiceTaskNode.vue` on canvas: the icon **chip** currently sits on the
same brand background as the node body (brand-bg on brand-bg). Confirm whether
this is visually acceptable. **If not acceptable**, change the chip background to
`color-mix(in srgb, var(--cp-brand) 12%, var(--cp-brand-bg))` for separation.
Record the verdict (accept / change) during live QA -- no code change is made in
this write-only task.

---

## 6. Manual DB checks (post-run)

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C
```

```sql
-- Job lifecycle (Status: 0 Pending,1 Running,2 Succeeded,3 Failed,4 Cancelled):
SELECT InstanceId, NodeId, Kind, Status, AttemptCount, MaxAttempts, DueAtUtc, LastError
FROM Wf_ServiceJob ORDER BY CreateDate;

-- Scenario 5/6 error variable direct-written into VarsJson:
SELECT Id, Status, VarsJson FROM Wf_FlowInstance
WHERE FlowKey IN ('svc-fail-erroredge','svc-fail-suspend') ORDER BY CreateDate DESC;

-- Scenario 5 error-branch human token parked at h1:
SELECT t.InstanceId, t.NodeId, t.Status FROM Wf_FlowToken t
JOIN Wf_FlowInstance i ON i.Id = t.InstanceId WHERE i.FlowKey = 'svc-fail-erroredge';
```

Expected: scenarios 1-4 -> one `Succeeded` (2) job each; scenarios 5-6 -> one
`Failed` (3) job each; scenario 5 -> a token parked at node `h1` (Active) and the
instance Running; scenario 6 -> instance Suspended (4).
