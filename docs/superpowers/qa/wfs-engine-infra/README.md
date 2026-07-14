# WFS 波⑤ Engine Infra six-pack (F-T2) -- QA Runbook

**Branch:** `feat/wfs-engine-infra`
**Date:** 2026-07-13
**Feature scope:** the six wave-⑤ engine-infra features --
**(1) work calendar** (`Sys_WorkCalendar` exception table + year page `oa-work-calendar` menu 743 +
`JapaneseHolidaySeed` 35 days + timer `workdays` delay mode landing on `WorkdayFireHour`=09:00),
**(2) approval timeout errorEdge** (`TimeoutAction="errorEdge"` node-level void + route along the
IsError edge, `IFlowEngine.TimeoutAdvanceErrorEdgeAsync`, designer-time `E-WF-027`),
**(3) cleanup worker** (`WfServiceJobCleanupWorker`: 180-day terminal hard-delete / reservation
rows never cleaned / stale-reservation alert),
**(4) per-tenant connector** (`Wf_Connector` DataProtection-encrypted + `DbWfConnector` +
tenant-preferred-over-app resolution + `E-WF-028` `TimeoutSec>=lease` reject + masked read),
**(5) per-node HTTP override** (`ServiceHttpMethod` / `ServiceTimeoutSec` carried through the
`ServiceTaskActionRef` snapshot, read by `DbWfConnector`),
**(6) per-tenant time zone** (`Sys_Tenant.TimeZoneId` + `ITenantClock` three-tier chain + DST
policy + save-side `E-WF-028` on an unparseable id).

> **Status: written, not run.** Authored per task F-T2 (write-only). Live QA -- spin the backend
> against the isolated DB, run the ps1 HTTP e2e, drive the pages in a real browser -- is executed
> later by the main agent with a QA user present. **Nothing here has been executed.** Bugs found
> during live QA are fixed TDD (regression test into `CP6.Tests/Wf/**` or `cp6.web` vitest).

---

## 1. What this harness covers

Three deliverables:

| File | Purpose |
| --- | --- |
| `seed.sql` | 4 users + 1 FormDef + 1 errorEdge FlowDef (static fixtures only -- raw INSERT into `CP6DB_OA`). |
| `qa_infra.ps1` | HTTP e2e over the testable parts of scenarios 2, 3, 6, 7, 8 against a running backend (ASCII data, real status codes). |
| `README.md` | This runbook: setup, scenario matrix, expected results, DB/worker drills, browser steps, DoD self-check. |

### Scenario matrix (brief 8 → harness)

| # | Feature / brief scenario | Where | Expected outcome |
| --- | --- | --- | --- |
| 1 | **Year-page checkbox → timer 3-workday real calc**: year page → mark a day as holiday → designer build a `serviceTask` timer node `serviceDelayMode=workdays`, `serviceDelayValue=3` → test-run → `Wf_ServiceJob.DueAtUtc` lands 3 **workdays** later @ 09:00 (weekends + the JP holiday + 振替 skipped). | **Browser** (sec 5.1) + **DB** (sec 4.1) | `DueAtUtc` = 3rd workday 09:00 tenant-local → UTC (Tokyo 09:00 JST == 00:00Z). |
| 2 | **Empty-state import**: a calendar with no rows shows an empty hint + an "import JP legal holidays" button → click → 35 rows land → the calendar renders holiday cells. | ps1 (import + list) + **Browser** (sec 5.1) | `import-jp` → `inserted=35` when empty (`0` idempotent); `GET ?year=2026` → `isEmpty=false`, items carry the holidays. |
| 3 | **approval timeout errorEdge live**: an approval with `TimeoutAction=errorEdge` + an IsError edge → timeout fires → the original pending task is voided, the token routes to the failure-edge node, `timeoutError` var is injected; **no** IsError edge → designer save blocked `E-WF-027` (2 locales for i18n). | ps1 (E-WF-027 static save neg/pos) + **DB/worker** (sec 4.3) + **Browser** (sec 5.3) | save without failure edge → `400 E-WF-027`; with it → `200`; runtime drill: `a1` task `Cancelled`, new `Pending` at `handler`, instance `Running`, `VarsJson` has `timeoutError`. |
| 4 | **Three existing timeout actions no-regression**: `remind` / `approve` / `reject` / `escalate` each still behave unchanged. | **Unit** (`Timeout_Reject_ByteEquivalent_NoRegression`) + **live** (sec 4.4) | the fourth case (`erroredge`) was inserted between `escalate` and `default`; the four pre-existing branches are byte-equivalent. |
| 5 | **Cleanup worker**: seed an over-age terminal `Wf_ServiceJob` + a reservation `Wf_TriggerFire` (`InstanceId` & `Error` both null) + an aged reservation → trigger cleanup → terminals deleted, running/reservation kept, OperLog records the delete + stale count. | **DB/worker** (sec 4.5) | terminal rows older than `CleanupRetentionDays`(180) hard-deleted in batches of 500; reservation rows never deleted; aged reservations counted as an alert. |
| 6 | **Connector tab full flow**: create a connector (credential input) → list `HasAuth` masked (no plaintext) → refresh still masked → `TimeoutSec<lease` executes/decrypts OK; `TimeoutSec>=lease` save → `E-WF-028`; a tenant connector named the same as the app `EchoConnector` → tenant wins. | ps1 (create/mask/E-WF-028/403) + **DB/exec** (sec 6.2) + **Browser** (sec 5.2) | create → `200 {id}`; `hasAuth=true`, `authJson` always null; `TimeoutSec=300` → `400` `message="E-WF-028"` + `detail`; non-role user → `403 ...Connector.Edit`. |
| 7 | **Per-node HTTP override**: a `serviceTask` webApi node with `serviceHttpMethod=PUT` / `serviceTimeoutSec=5` executes with the node values. | ps1 (save-validation E-WF-028) + **exec** (sec 6.3) + **Browser** (sec 5.3) | `serviceTimeoutSec>=lease` → `400 E-WF-028`; method out of `{GET,POST,PUT,DELETE}` → `400 E-WF-028`; `PUT`+`5` → `200`; on the wire the node PUT/5s wins over the connector default. |
| 8 | **Per-tenant time zone**: set the tenant to `Asia/Tokyo` → timer `untilDate`/`workdays` interpreted in Tokyo local; changing tz does **not** batch-recompute; an unparseable `TimeZoneId` → `E-WF-028`. | ps1 (E-WF-028 + set/restore) + **Browser** (sec 5.4) | `TimeZoneId="Not/AZone"` → `400 E-WF-028`; `"Asia/Tokyo"` → `200`; the "self-heals next fire" note (sec 6.4). |

---

## 2. Environment setup

### 2.1 Isolated database

Reuse the `CP6DB_OA` database from prior WFS QA sessions, or create fresh:

```sql
CREATE DATABASE CP6DB_OA;
```

Point the backend at it so QA data never touches live `CP6DB`. On first boot `db.Database.Migrate()`
applies all EF migrations -- this wave adds **exactly one** migration (`WfsInfra`: tables
`Sys_WorkCalendar` + `Wf_Connector` and the `Sys_Tenant.TimeZoneId` column, plus their indexes), so
an existing `CP6DB_OA` from before this wave needs that one migration applied (automatic on boot).
Startup seeds relevant here:
- `JapaneseHolidaySeed.For(DefaultTenant)` -- A-T2 boot block plants **35** JP holiday rows into
  DefaultTenant **A1**'s `Sys_WorkCalendar` (idempotent on `(TenantId,Date)`). So A1 is **not** empty;
  the empty-state scenario clears them first (sec 3.1 note).
- `WorkCalendarConnectorPermissionSeed` -- grants `(oa-work-calendar, Calendar.View/Edit)` (menu 743)
  and `(oa-flow-admin, Connector.View/Edit)` to **RoleId=1** per tenant (`PermissionService.HasActionAsync`
  has **no admin bypass**, so the operator must be in role 1; `qa_inf_norole` at RoleId=2 gets the 403).
- `I18nOaEngineInfraScreenSeed` (46 keys) + `I18nTenantComplianceSeed` (+3 `platform.tenant.timeZone*`)
  -- five-language, insert-only (see sec 7).

### 2.2 Apply seed

Run from a **native shell** (cmd / PowerShell), not git-bash:

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
```

The seed prints a 6-row sanity report (4 users + 1 form + 1 flowdef) plus a RoleAction probe
confirming `Connector.Edit` / `Calendar.Edit` are granted to RoleId=1 only, and A1's current
work-calendar row count. All users share password `123456` (admin's BCrypt hash cloned).
`qa_inf_admin` is **RoleId=1** (drives the OA admin surface); `qa_inf_padmin` is
**IsPlatformAdmin=1** (drives `/api/platform/tenant`, which is `[RequirePlatformAdmin]`).
`SET QUOTED_IDENTIFIER ON` is set (required because `Wf_FlowDef` carries filtered unique indexes:
`(TenantId,FunctionId) WHERE FunctionId IS NOT NULL` and `(TenantId,FlowCode) WHERE FlowCode IS NOT
NULL`, `CP6Context.cs:712-718`).

### 2.3 Backend

Prior WFS QA sessions used ports 5177-5181; this harness defaults to **5181**:

```powershell
cd <repo>\CP6.WebApi
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --urls "http://localhost:5181"
```

- The `WfTimeoutScanWorker` (1-minute interval) and `WfServiceJobCleanupWorker` (daily 03:00 UTC)
  hosted services start automatically -- scenarios 3-runtime, 4, 5 depend on them.
- **Dev CSRF must be disabled** (`Security:Csrf:Enabled=false`, the dev default): the admin /
  designer / tenant POSTs are cookie-auth'd (the JWT `cp6_at` cookie flows via `-WebSession`) and
  would 403 on the CSRF double-submit otherwise. In **production** posture those POSTs require
  cookie + `X-Csrf-Token`. The scenario-6 403 is a **permission** 403, distinct from any CSRF 403.
- **DataProtection key ring -- already persisted, no production action pending**: connector
  `AuthJsonEncrypted` is a DataProtection ciphertext (`purpose="Wfs.Connector.Auth"`). The key ring
  was persisted to the **database** by P0-T1 (commit `2155fb1`):
  `AddDataProtection().PersistKeysToDbContext<CP6Context>().SetApplicationName("CP6")`
  (`Program.cs:585-587`), verified live to survive restarts; multiple instances share it naturally
  (same DB). So connector credentials survive container rebuilds / second instances out of the box --
  the D-T0 "hard prerequisite" was satisfied before this wave, and there is **no**
  `DataProtection:KeyPath` (or any file-system key path) setting in the codebase. The SSO
  `ClientSecret` risk that `deploy/runbook.md:112` used to flag is resolved by the same provider
  (the runbook line now carries a "resolved by P0-T1" note). The only residual caveat is historical:
  ciphertexts written **before** P0-T1 under the old temporary key (B1/C1 SSO ClientSecret, per the
  P0 ledger) need a one-time re-save -- no connector row can be in that state (the table postdates P0-T1).

---

## 3. Running the HTTP e2e

```powershell
.\qa_infra.ps1
.\qa_infra.ps1 -BaseUrl http://localhost:5181
.\qa_infra.ps1 -TenantId 00000000-0000-0000-0000-0000000000A1   # DefaultTenant A1 (default)
```

The script logs in `qa_inf_admin` + `qa_inf_padmin` (and `qa_inf_norole` for the 403), then walks
scenarios 2, 3, 6, 7, 8, printing `PASS`/`FAIL`/`WARN` and a final tally. It captures **real** HTTP
status codes (via `Invoke-WebRequest -UseBasicParsing`) so it can tell `200` from `400`/`403`/`404`
and read `{code,message,detail}` error bodies. Exit code 1 if any FAIL.

### 3.1 Scenario-2 note -- see the actual empty state

Because A1 is pre-seeded 35 JP holidays on boot, the first `import-jp` returns `inserted=0`
(idempotent) rather than `35`. The ps1 accepts **either** (35 fresh OR 0 idempotent). To exercise
the genuine empty-state → import → 35 path (and the empty-state UI hint), clear the rows first:

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -Q "DELETE FROM Sys_WorkCalendar WHERE TenantId='00000000-0000-0000-0000-0000000000A1'"
```

Then re-run the ps1 (or click the import button in the browser): the first `import-jp` now returns
`inserted=35`, `GET ?year=2026` flips `isEmpty` false, and the year page renders holiday cells.

### Envelope quirks (verified against controllers)

- **OA admin endpoints** (`WorkCalendarController`, `WfConnectorController`, `DesignerController`):
  standard envelope `{ code:0, message:"OK", data }`.
  - `work-calendar` GET → `data.{ year, isEmpty, items:[{date,isWorkday,note}] }` (`WorkCalendarController.cs:30-36`).
  - `work-calendar/import-jp` → `data.{ inserted }` (`WorkCalendarController.cs:57-60`).
  - `wf-connector` GET → `data: WfConnectorView[]` (`name/displayName/baseUrl/timeoutSec/enabled/hasAuth`;
    `authJson` **always null** -- `WfConnectorService.cs:34`).
  - `wf-connector` POST create → `data.{ id }` (`WfConnectorController.cs:60-64`).
  - `designer/save` → `data: true` (`DesignerController.cs:54-63`).
- **Errors**:
  - `DesignerController.Err` / `FlowController.Err` set `{ code:400, message:<e.Message> }` **without**
    splitting -- so `E-WF-027` and the node-side `E-WF-028` surface as a bare code in `message`
    (`DesignerController.cs:31`).
  - `WfConnectorController.Err` **splits on `|`**: `"E-WF-028|timeoutGteLease:300>=300"` →
    `{ code:400, message:"E-WF-028", detail:"timeoutGteLease:300>=300" }` (`WfConnectorController.cs:36-43`;
    the F-T1 presentation-layer fix so the front-end `http.ts` `t(message)` resolves the bare code).
  - permission miss → `{ code:403, message:"无权限：oa-flow-admin:Connector.Edit" }`
    (`RequirePermissionAttribute.cs`).
- **Platform tenant** (`TenantController`, `[RequirePlatformAdmin]`): List/Get return bare objects
  (`{ rows,total }` / `TenantDetail`); Update → `{ code:0 }`; an unparseable `TimeZoneId` throws
  `InvalidOperationException("E-WF-028")` → `BizException` → `400 { code, message:"E-WF-028" }`
  (`TenantController.cs:59-70`).

---

## 4. Manual checks (not automatable in the ps1)

### 4.1 Scenario 1 -- workdays=3 timer DueAt (DB)

After the browser builds and test-runs a `serviceTask` timer node (`serviceDelayMode=workdays`,
`serviceDelayValue=3`) -- or after submitting a flow that contains one -- the timer schedules a
`Wf_ServiceJob` whose `DueAtUtc` is **3 workdays** ahead at `WorkdayFireHour`=09:00 tenant-local,
converted to UTC (`ServiceTaskNodeHandler.ComputeTimerDueUtcAsync` → `WorkdayCalculator.AddWorkdaysAsync`
→ DST-safe convert; A-T3 / E-T2). Weekends **and** any `Sys_WorkCalendar` holiday (`IsWorkday=false`)
and 振替休日 in the window are skipped.

```sql
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C
```
```sql
-- Newest scheduled timer job. With tenant tz = Asia/Tokyo, DueAtUtc = 3rd-workday 09:00 JST == 00:00Z.
SELECT TOP 5 Kind, DueAtUtc, Status, NodeId FROM Wf_ServiceJob ORDER BY CreateDate DESC;
-- Confirm the day you toggled to holiday is present as an exception row (IsWorkday=0):
SELECT Date, IsWorkday, Note FROM Sys_WorkCalendar
WHERE TenantId='00000000-0000-0000-0000-0000000000A1' AND YEAR(Date)=2026 ORDER BY Date;
```

Verify by hand: count 3 Mon-Fri days forward from "today" (tenant-local), skipping any row above with
`IsWorkday=0`; the landing date at 09:00 local must match `DueAtUtc` (minus the tz offset).

### 4.2 Scenario 2 -- empty-state (see sec 3.1)

`DELETE FROM Sys_WorkCalendar WHERE TenantId=A1` → the year page shows the empty hint +
「导入日本法定假日」button → click / `import-jp` → `inserted=35` →
`SELECT COUNT(*) FROM Sys_WorkCalendar WHERE TenantId='...A1'` = **35**.

### 4.3 Scenario 3 -- approval timeout errorEdge runtime (DB + worker)

The ps1 covers the **designer-time** `E-WF-027` (block / allow). The **runtime** void+route needs a
timeout to actually fire; drive it with a SQL `DueAt` roll-back (same posture as the wave-③ timer
misfire runbook), then let the 1-minute `WfTimeoutScanWorker` pick it up:

1. Submit the seeded valid errorEdge flow as `qa_inf_admin` (over HTTP or the SPA):
   `POST /api/wf/flow/submit { flowKey:"qa-inf-erroredge", varsJson:"{}" }` → note the `instanceId`.
   One `Pending` task lands on `qa_inf_appr` at node `a1` with `DueAt ~ now+1h`.
2. Roll that task's `DueAt` into the past so the next scan treats it as timed-out:
   ```sql
   UPDATE Wf_FlowTask SET DueAt = DATEADD(HOUR, -2, GETDATE())
   WHERE InstanceId = '<inst>' AND NodeId = 'a1' AND Status = 0;   -- FlowTaskStatus.Pending = 0
   ```
   (`WfTimeoutScanWorker` scans `DueAt <= DateTime.Now && !TimeoutHandled`; `WfTimeoutService.cs:46`.)
3. Wait ≤ 60s for the scan (`Interval = 1 min`, `WfTimeoutScanWorker.cs:12`). Then confirm the
   `erroredge` case (`WfTimeoutService.cs:93`) delegated `IFlowEngine.TimeoutAdvanceErrorEdgeAsync`
   (`FlowEngine.Tokens.cs`), which did **node-level** cleanup + routing:
   ```sql
   -- a1's task Cancelled (=? engine enum), a NEW Pending task at 'handler', instance still Running:
   SELECT NodeId, Status FROM Wf_FlowTask WHERE InstanceId = '<inst>' ORDER BY CreateDate;
   SELECT Status FROM Wf_FlowInstance WHERE Id = '<inst>';   -- Running (NOT Rejected -- errorEdge is a route, not a terminate)
   -- timeoutError variable injected {nodeId,dueAt}:
   SELECT VarsJson FROM Wf_FlowInstance WHERE Id = '<inst>';
   -- history row:
   SELECT Action FROM Wf_FlowHistory WHERE InstanceId = '<inst>' ORDER BY CreateDate;   -- includes 'timeoutErrorEdge'
   ```
   The a1 task must be **Cancelled** (node-level void, not the instance-level reject cascade that
   would clobber sibling branches), and `handler` must have a fresh `Pending` task on `qa_inf_appr`.

### 4.4 Scenario 4 -- three existing timeout actions no-regression

`remind` / `approve` / `reject` / `escalate` are pinned byte-equivalent by
`TimeoutErrorEdgeTests.Timeout_Reject_ByteEquivalent_NoRegression` (B-T1). For a live spot-check,
build (or seed) a flow whose approval node uses `timeoutAction:"reject"` with a short `timeoutHours`,
submit, roll `DueAt` back as in 4.3, and confirm the instance ends `Rejected` (the hard-reject path)
-- unchanged from before this wave. The new `erroredge` case sits **between** `escalate` and
`default` in the switch, touching none of the others.

### 4.5 Scenario 5 -- cleanup worker (DB + worker)

`WfServiceJobCleanupWorker` runs daily at **03:00 UTC**; to exercise it on demand, seed the three row
shapes with an over-age timestamp, then either wait for the window or bounce the process near 03:00
UTC (v1 has no manual-trigger endpoint -- the worker is time-gated, same as the wave-③ timer worker).
`CleanupRetentionDays`=180, `StaleReservationAlertDays`=7 (`WfsInfraOptions`).

```sql
-- (a) an over-age TERMINAL service job (Status Succeeded=2, completed 200 days ago) -> should be DELETED:
INSERT INTO Wf_ServiceJob (Id, InstanceId, TokenId, NodeId, Kind, Status, CompletedAtUtc, Creator, CreateDate, TenantId)
VALUES (NEWID(), NEWID(), NEWID(), 'n1', 'timer', 2, DATEADD(DAY,-200,GETUTCDATE()), 'qa-inf', GETDATE(), '00000000-0000-0000-0000-0000000000A1');
-- (b) a RUNNING job (Status Running=1) however old -> should be KEPT.
-- (c) a reservation Wf_TriggerFire (InstanceId AND Error both null) -> NEVER cleaned; if FiredUtc older
--     than StaleReservationAlertDays it is COUNTED as an aging alert (OperLog IsAlert=true).
```

After the run:
```sql
SELECT Status, COUNT(*) AS n FROM Wf_ServiceJob GROUP BY Status;         -- terminal over-age gone; running kept
SELECT COUNT(*) AS reservations FROM Wf_TriggerFire WHERE InstanceId IS NULL AND Error IS NULL;  -- unchanged
SELECT TOP 5 IsAlert, Content, CreateDate FROM Sys_OperLog WHERE Content LIKE '%cleanup%' ORDER BY CreateDate DESC;
```
Expected: terminal rows older than 180 days hard-deleted (in batches of 500); running + reservation
rows retained; an OperLog line per tenant that had any action, carrying the delete + stale counts.
**Idempotency-window caveat (`WfCleanupService` XML doc):** deleting terminal `Wf_TriggerFire` rows
also expires their idempotency keys, so a message/event replay older than the retention window can
re-start a flow -- shortening `Wfs:CleanupRetentionDays` shortens that window; reservation rows are
never deleted so in-flight placeholders are safe.

---

## 5. Browser walkthroughs (manual, gstack browse)

Log in as `qa_inf_admin` (OA surface) or `qa_inf_padmin` (platform tenant page). Save screenshots
into this directory.

### 5.1 Work-calendar year page (`WorkCalendar.vue`, menu 743, scenarios 1 & 2)
1. Open **工作日历 / Work Calendar** (sidebar label `nav.743`). On a **cleared** tenant it shows the
   empty hint + an 「导入日本法定假日」button (`oa.workcal.*`); click → 35 rows import → the `el-calendar`
   `#date-cell` slot paints holiday vs workday cells.
2. Click a normal workday cell → toggle it to **holiday** (`oa.workcal.kind.closed`); a 补班 toggles a
   weekend to **workday** (`oa.workcal.kind.makeup`). The four `kind.{makeup|closed|weekend|normal}`
   states all render.
3. In the **designer** (5.3) build a timer node `workdays=3` and test-run; confirm the DueAt lands 3
   workdays out, skipping the day you just marked holiday (verify against sec 4.1 DB check).

### 5.2 Connector tab (`WfConnectorPanel.vue` / `WfConnectorDialog.vue` under `FlowAdmin.vue`, scenario 6)
1. Open the OA flow-admin page → the **连接器 / Connectors** tab (`oa.connector.tab`).
2. New connector → fill name / baseUrl / credential JSON → save. The list shows a **hasAuth** badge
   (`oa.connector.authYes`), **never** the plaintext; **refresh** → still masked (the credential input
   shows the `oa.connector.form.authConfigured` "已配置（不回显）" placeholder, never the secret).
3. Set `TimeoutSec` to 300 and save → the toast resolves `E-WF-028` to its localised text (not the
   bare code -- the `Err` split + F-T1 seed). Editing name/URL while leaving the credential blank
   keeps the existing secret (mask-read contract).

### 5.3 Designer panels (`NodePropertyPanel.vue` / `designerModel.ts`, scenarios 3 & 7)
1. **errorEdge**: on an approval node set 超时动作 to **错误边 / errorEdge** (`oa.designer.timeout.errorEdge`).
   Without an IsError out edge, client validation + save both surface `E-WF-027`
   (`oa.designer.errTimeoutErrorEdge`); add the failure edge → save OK. Switch the UI language
   (ja / zh-CN / zh-TW / en / ko) and re-trigger to confirm **≥2 locales** render localised text.
2. **workdays**: on a serviceTask timer node pick delay mode **工作日 / workdays**
   (`oa.designer.svc.delayMode.workdays`) + value 3.
3. **HTTP override**: on a serviceTask webApi node fill **HTTP 方法（覆盖）** = PUT
   (`oa.designer.svc.httpMethod` / `.httpMethodHint` "留空＝用连接器默认") and **超时（秒，覆盖）** = 5
   (`oa.designer.svc.timeoutSec`). An out-of-domain method or a timeout ≥ lease surfaces
   `oa.designer.errHttpOverride` / `E-WF-028`.

### 5.4 Tenant time zone (`views/platform/TenantListView.vue`, scenario 8)
1. As a platform admin, edit DefaultTenant → the **时区 / Time Zone** dropdown
   (`platform.tenant.timeZone`, `filterable clearable`, 21 common ids) → pick **Asia/Tokyo** → save.
2. The `platform.tenant.timeZoneHint` note states the self-heal semantics (changing tz does not
   batch-recompute existing `NextDueUtc`; the next fire self-heals, at most one fire under the old tz).
3. Leaving it blank clears it (app default). An id outside the whitelist that the backend can't parse
   → `E-WF-028`.

---

## 6. Why some runtime facts are DB/execution checks, not ps1 assertions

- **6.1 no raw connector seed**: `Wf_Connector.AuthJsonEncrypted` is a DataProtection ciphertext; a
  raw-INSERTed string would fail `Unprotect` at execute time. So the ps1 **creates** connectors via
  the admin API (the only path that encrypts on write + masks on read), mirroring how wave-③ seeds
  message triggers via the API for the one-time key.
- **6.2 tenant-preferred-over-app**: the app registers a sample `EchoConnector` (name `erpEcho`,
  `Program.cs:136-138`). Creating a tenant `Wf_Connector` named `erpEcho` and resolving it prefers the
  tenant row (`TenantConnectorResolver.ResolveAsync`, unit-pinned by
  `Catalog_MergesBothSources_TenantRowDedups` / `Resolve_TenantRowPreferred`). There is no HTTP endpoint
  that exposes the merged catalog, so precedence is confirmed by those unit tests + a real service-task
  execution (a webApi node pointing at `erpEcho` hits the tenant row's baseUrl), not a ps1 assertion.
- **6.3 node override on the wire**: the ps1 proves the **save-validation** of `serviceHttpMethod` /
  `serviceTimeoutSec` (E-WF-028 domains). That the node PUT/5s actually reaches the downstream is a
  real-outbound check (`DbWfConnector.CallAsync` reads `ctx.ActionRefJson`; unit-pinned by
  `NodeHttpOverrideTests` with a capturing `HttpMessageHandler`). Drive a real service task against a
  reachable echo endpoint and inspect the outbound method/timeout.
  **Save-side connector-name caveat:** `DesignerService.SaveAsync`'s E-WF-018 registered-name check
  (`DesignerService.cs:51-64`, step ①b, which runs **before** the ①c lease check) only sees
  **DI-registered app-level** `IWfConnector` names (`erpEcho`); tenant `Wf_Connector` rows are
  resolved at **runtime** by `TenantConnectorResolver` and are invisible to the save-side check.
  Hence the ps1's scenario-7 schemas reference `erpEcho` -- a tenant-connector name there would be
  rejected `E-WF-018` before the E-WF-028 checks are reached. (A designer flow that targets a
  tenant-only connector must therefore reuse an app-registered name -- exactly the
  tenant-preferred-shadowing shape of 6.2.)
- **6.4 tz self-heal**: changing the tenant tz does not rewrite existing `Wf_FlowTrigger.NextDueUtc`;
  the next fire recomputes under the new tz (at most one fire under the old tz). This is a runtime
  timer observation, not a save assertion.

---

## 7. i18n keys (F-T1, cross-referenced)

`I18nOaEngineInfraScreenSeed` seeds **46** keys five-language (ZhCN/ZhTW/En/Ja/Ko), insert-only, plus
`I18nTenantComplianceSeed` adds **3** `platform.tenant.timeZone*` keys (domain-local to the platform
family). Families: `oa.workcal.*` ×13 (year page, incl. dynamic `kind.{makeup|closed|weekend|normal}`
and the `{n}` interpolated `imported`) + `nav.743` (sidebar) + `oa.connector.*` ×22 (tab / new / empty
/ authYes / authNo / `col.*` ×7 / `form.*` ×10) + `oa.designer.svc.httpMethod` / `.httpMethodHint` /
`.timeoutSec` + `oa.designer.svc.delayMode.workdays` + `oa.designer.timeout.errorEdge` +
`oa.designer.errHttpOverride` + `oa.designer.errErrorEdgeSource` / `.errTimeoutErrorEdge` + the error
codes **`E-WF-027`** / **`E-WF-028`** + `platform.tenant.timeZone` / `.timeZonePlaceholder` /
`.timeZoneHint`. The views (`WorkCalendar.vue`, `WfConnectorPanel/Dialog.vue`, `NodePropertyPanel.vue`,
`designerModel.ts`, `TenantListView.vue`) reference these; the backend throws the `E-WF-0xx` codes
which the UI resolves (scenarios 3, 6, 7, 8 confirm the resolution).

> **Known cross-cutting gap (F-T1 concern, not this wave's debt):** `common.edit/cancel/save` are
> consumed by 10+ pages across space/wms/oa but never seeded to `Sys_Lang`; since i18n is pure
> DB-driven (`/lang/{lang}`, no static front-end dictionary) they fall back to the bare key. The
> connector dialog reuses this existing pattern. Tracked as a global `common.*` seed follow-up.

---

## 8. DoD self-check (plan Global Constraints)

F-T2 is docs-only (no code); the code-level gates below are the wave's, verified by the prior task
reports and re-run by the main agent in the full-suite pass. This harness's own contribution is the
last row (gstack QA harness present). Status column: what F-T2 can attest vs what the live pass runs.

| DoD item (plan / w5-globals) | Status at F-T2 |
| --- | --- |
| Backend `dotnet test` all green (1509[5 skip] baseline → +N) | **2110 green / 5 skip** at F-T1 (baseline 2098 +12); main agent re-runs full suite. |
| Existing Wf tests byte-equivalent (3 timeout actions / ComputeDueUtc 3 modes / connector app fallback) | Pinned by `Timeout_Reject_ByteEquivalent_NoRegression`, `ComputeDueUtc_ExistingThreeModes_ByteEquivalent`, `Resolve_FallsBackToApp_*`. |
| Frontend `npm run test` (320 → +N) / `type-check` / `build` all green | **463 green** at E-T2; type-check + build clean. |
| EF `has-pending-model-changes` clean; **exactly one migration `WfsInfra`** (2 tables + 1 column) | Clean at every task; only A-T1 migrated (two entities + `Sys_Tenant.TimeZoneId`); D/E POCO + column consumed zero-migration. |
| Zero cross-module pollution (`{Sys,Wf}` entities / `Services/{Wf,Sys}` / WebApi Program+BG+Controllers Oa/Sys+Seed / `cp6.web/src/{api,views}/oa`) | Held per task; E-T2 platform-tenant page is the plan-declared exception (Platform domain, not the Oawf guard face). |
| spec §8 test matrix fully covered; `E-WF-027`/`028` each have static + service-layer tests | `ErrorEdgeSourceValidatorTests` (E-WF-027 static + validator), `WfConnectorServiceTests` / `NodeHttpOverrideTests` (E-WF-028 both faces), `WorkdaysTokyoTimeZoneTests`, `WfCleanupServiceTests`. |
| Five-language seed complete (ZhCN/ZhTW/En/Ja/Ko), no duplicate LangKey; `Calendar.View/Edit` + `Connector.View/Edit` seed + RoleId=1 grant | F-T1: `I18nOaEngineInfraScreenSeedTests` (46-key oracle, missing/orphan + real Ja/Ko), `WorkCalendarConnectorPermissionSeedTests` (per-tenant 4-tuple, idempotent). |
| Zero hardcoded colors (CpTag tone / Design-System token) | Held (F-T1 note; views use `t()` + tokens). |
| JP holiday seed 35 dates (2026×18 + 2027×17, incl. 振替 + 2026-09-22 国民の休日), idempotent on `(TenantId,Date)` | `JapaneseHolidaySeedTests` (`Items_Cover2026And2027_35Dates_AllDistinct`, `ImportJapaneseHolidays_Idempotent_35Rows`). |
| DataProtection key-ring persistence landed (D-T0); runbook:112 note updated | **Done before this wave** by P0-T1 (`PersistKeysToDbContext<CP6Context>()` + `SetApplicationName("CP6")`, `Program.cs:585-587`, verified live across restarts; DB-shared for multi-instance). `deploy/runbook.md:112` now carries the "resolved by P0-T1" note (this task). No production action pending. |
| gstack QA harness present (8 scenarios) + live QA all-pass (QA user present, isolated CP6DB_OA) | **This harness** (`seed.sql` + `qa_infra.ps1` + this README): ps1 covers 2/3/6/7/8, DB/worker drills cover 1/3-runtime/4/5, browser covers 1/2/3/6/7/8. **Live pass pending** (write-only). |

**F-T2 self-check conclusion:** the harness is complete and cross-checked against shipped code
(sec 3 envelope references + `w5-FT2-report.md` file:line table). Every DoD item is either green at
the prior task boundaries (backend/frontend/EF/i18n/seed gates) or already satisfied pre-wave
(DataProtection key ring, P0-T1). Nothing carries a pending production action; the main agent runs
the full-suite gate and the live 8-scenario pass.
