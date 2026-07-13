# WFS Event-Trigger Start (F-T3) -- QA Runbook

**Branch:** `feat/wfs-event-trigger`
**Date:** 2026-07-13
**Feature scope:** flow trigger start paths -- **timer** (`WfTriggerWorker` 30s scan +
two-phase slot claim + misfire-catches-only-latest), **event** (`IWfTriggerBridgeHook` +
`IntegrationEventDispatcher` fallback to `ReplayEventAsync` + Echo sample source), **message**
(`[AllowAnonymous]` `/fire` endpoint, `X-Api-Key` + `Idempotency-Key`, 64KB cap, varsSchema
whitelist) -- plus the admin backend (`api/oa/flow-triggers`), save+runtime validation
(`E-WF-022/023/024`), the one-time API-key modal, cron preview, and the fire-log drawer.

> **Status: written, not run.** Authored per task F-T3 (write-only). Live QA -- spin the
> backend against the isolated DB, run the ps1 HTTP e2e, drive the admin page in a real
> browser -- is executed later by the main agent with a QA user present. **Nothing here has
> been executed.** Bugs found during live QA are fixed TDD (regression test into
> `CP6.Tests/Wf/**`).

---

## 1. What this harness covers

The eight brief scenarios, split into HTTP-e2e (`qa_flow_trigger.ps1`) and one real-browser
scenario (manual, gstack). Three deliverables:

| File | Purpose |
| --- | --- |
| `seed.sql` | 3 users + 1 FormDef + 2 FlowDefs + 3 raw triggers (raw INSERT into `CP6DB_OA`). |
| `qa_flow_trigger.ps1` | HTTP e2e over the testable parts of scenarios 2-8 against a running backend (ASCII data, real status codes). |
| `README.md` | This runbook: setup, scenario matrix, expected results, browser steps, DB checks. |

### Scenario matrix (brief 8 -> harness)

| # | Brief scenario | Where | Expected outcome |
| --- | --- | --- | --- |
| 1 | Admin page builds all three trigger types (timer preset "Daily 09:00" + 5-row cron preview / event `QA|OnEchoAsync` + varsMap / message + one-time plaintext key modal) | **Browser** (sec 5) + ps1 covers the cron-preview API (5 future rows) | Three triggers created; message create shows plaintext key **once** (refresh -> gone). |
| 2 | Manual test-fire a timer trigger | ps1 | `manual-fire` -> `200 { instanceId }`; fire-log gains 1 success row (instanceId set, error null). |
| 3 | Timer short-cycle (`*/1 * * * *`) auto-fires | ps1 (needs `WfTriggerWorker`) | Within <=90s a fire row appears with instanceId set; `NextDueUtc` advanced. |
| 4 | Event echo linkage + idempotent replay | ps1 | Echo `POST /api/oa/wf-trigger-echo/fire` -> `matchedCount>=1, firedCount>=1`, one new fire row; **resend same `eventId`** -> `firedCount` still counts it but **no new fire row / instance**. |
| 5 | Message e2e | ps1 | 3 headers -> `201 {instanceId}`; same `Idempotency-Key` -> `200` same instanceId; wrong key -> `401`; missing `Idempotency-Key` -> `400`; body 65KB -> `400`; disabled -> `404` **byte-identical to unknown-GUID 404**; whitelist drops non-schema fields. |
| 6 | Key reset | ps1 | Reset -> new one-time key; old key -> `401`, new key -> `201`. |
| 7 | Save validation | ps1 (+ browser i18n) | cron `not a cron` -> `400 E-WF-022`; disabled target flow -> `400 E-WF-023`. |
| 8 | Fire-log drawer with a human failure | ps1 (+ browser drawer) | Disabled-starter trigger manual-fire -> `400`, fire-log top row has no instanceId + `Error` carries `E-WF-022`. |

---

## 2. Environment setup

### 2.1 Isolated database

Reuse the `CP6DB_OA` database from prior WFS QA sessions, or create fresh:

```sql
CREATE DATABASE CP6DB_OA;
```

Point the backend at it so QA data never touches live `CP6DB`. On first boot
`db.Database.Migrate()` applies all EF migrations -- this wave adds **exactly one**
migration (`WfsFlowTrigger`: tables `Wf_FlowTrigger` + `Wf_TriggerFire`, four indexes),
so an existing `CP6DB_OA` from before this wave needs that one migration applied (automatic
on boot). Startup seeds also run: `FlowTriggerPermissionSeed` (grants `FlowTrigger.View/Edit`
to **RoleId=1** per tenant, menu `734 oa-flow-admin`) and `I18nOaFlowTriggerScreenSeed`
(five-language keys + `E-WF-022/023/024`).

### 2.2 Apply seed

Run from a **native shell** (cmd / PowerShell), not git-bash:

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
```

The seed prints a 9-row sanity report (3 users + 1 form + 2 flowdefs + 3 triggers). All
users share password `123456` (admin's BCrypt hash cloned). `qa_wtr_starter` is **RoleId=1**
so it may call the permission-gated admin endpoints (`FlowTrigger.Edit` for create / fire /
enable / reset-key; `FlowTrigger.View` for cron-preview). `SET QUOTED_IDENTIFIER ON` is set
(required by the filtered unique indexes on `Wf_FlowDef`).

### 2.3 Backend

Prior WFS QA sessions used ports 5177-5181; this harness defaults to **5181**:

```powershell
cd <repo>\CP6.WebApi
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --urls "http://localhost:5181"
```

- The `WfTriggerWorker` hosted service starts automatically (scenario 3 depends on it).
- Dev CSRF must be **disabled** (`Security:Csrf:Enabled=false`, the dev default): the admin
  POSTs are cookie-auth'd and would 403 under CSRF. The message `/fire` endpoint is
  `[AllowAnonymous]` + `X-Api-Key` header (no cookie, no CSRF).

---

## 3. Running the HTTP e2e

```powershell
.\qa_flow_trigger.ps1
.\qa_flow_trigger.ps1 -BaseUrl http://localhost:5181
```

The script logs in `qa_wtr_starter`, then walks the testable parts of scenarios 2-8,
printing `PASS`/`FAIL`/`WARN` and a final tally. It captures **real** HTTP status codes
(via `Invoke-WebRequest -UseBasicParsing`) so it can distinguish `201` (new) from `200`
(idempotent replay) and `400`/`401`/`404` apart.

### Envelope quirks (verified against controllers)

- **Admin endpoints** (`FlowTriggerAdminController`, `Controllers/Oa`): standard envelope
  `{ code:0, message:"OK", data:<payload> }`. Create -> `data.{ id, apiKeyPlain }`
  (`apiKeyPlain` non-null only for message create); manual-fire success -> `data.instanceId`,
  failure -> `400 { code:400, message:<Error> }`; fires -> `data: TriggerFireListItem[]`.
- **Message `/fire`** (`FlowTriggerFireController`, `Controllers/Integration`): **no
  envelope**. `201 { instanceId }` new / `200 { instanceId }` replay / `400`/`401`/`404`
  `{ code, message }`. The auth filter checks order is: parse id -> 404 (missing/disabled)
  -> 401 (bad key) -> 400 (missing/too-long `Idempotency-Key`); the 64KB cap and JSON-object
  check run **inside** the controller after auth passes.
- **404 shape**: both an unknown GUID and a disabled trigger return the identical body
  `{ code:404, message:"trigger not found" }` (no existence leak, spec 3.4).
- **Echo** (`WfTriggerEchoController`): `{ code:0, message:"OK", data:{ success,
  matchedCount, firedCount, message } }` (camelCase).

---

## 4. Why the timer/event triggers are seeded by raw INSERT

Same principle as the `wfs-service-task` / `wfs-kernel-hardening` harnesses seed FlowDefs
raw: `FlowTriggerValidator.ValidateAsync` runs **only** through
`FlowTriggerAdminService.Create/Update`. A raw `INSERT` into `Wf_FlowTrigger` skips it.

This is **required** for `wtr-badstarter` (scenario 8): its `StarterUserId` points at a
disabled user, which the save-side validator would reject with `E-WF-022` -- so it can only
exist via raw INSERT, and its failure surfaces at **runtime** in `FireAsync` (the double-check
the spec mandates). `wtr-event` and `wtr-shortcycle` are seeded raw too for a deterministic,
self-contained fixture. Key runtime facts that make the scenarios deterministic:

- **message triggers are NOT seeded** -- the ps1 creates them through the admin API so it can
  capture the one-time `apiKeyPlain` (`WfApiKeyHelper.NewRawKey`; only the SHA-256 hash is
  stored, so a raw-seeded key would have no recoverable plaintext to fire with).
- **timer misfire**: `WfCronHelper.NextUtc` computes strictly-future next from *now*, so a
  short-cycle trigger seeded with a **past** `NextDueUtc` fires once on the next 30s scan and
  advances; a downed window only ever catches up the most recent due (spec 3.2).
- **event idempotency key** = `{eventId}:{triggerId}`. First echo starts an instance + writes
  a fire row; a resend with the same `eventId` finds the existing fire row (with `InstanceId`
  set), returns "replayed" and writes **no** new row/instance -- yet `firedCount` still counts
  it a success (the bridge increments on any `r.Success`, replay included).
- **varsMap** value `$.OutboundNo` is a JSONPath-lite dot path (`ServiceVarsHelper`); payload
  `{"OutboundNo":"OB-1"}` -> `VarsJson {"outboundNo":"OB-1"}` (`WfTriggerVarsMapper.MapVars`).
- **worker scan filter** is `Enabled && TriggerType==Timer && NextDueUtc != null && <= now`,
  so `wtr-badstarter` (`NextDueUtc=NULL`) is never auto-scanned -- only manual-fire touches it,
  keeping the fire log clean for scenario 8.

### Known concurrency watch item (honest expectation)

There is no exactly-once concurrency script here, and deliberately so: the A-T2 ledger records
a **double-submit window** -- two *truly concurrent* `FireAsync` calls on the same idempotency
key can both pass the pre-check and double-submit before the unique index bites. The timer path
is protected by the two-phase slot claim (`NextDueUtc` advance + unique fire row in one
transaction), but the message/event paths carry that window. Any concurrency test added later
must assert "no duplicate **after** the race resolves", not exactly-once at the instant of the
race.

---

## 5. Scenario 1 -- Real-browser admin page (manual, gstack)

The admin page (`FlowAdmin.vue` new "Triggers" tab -> `FlowTriggerPanel.vue` +
`FlowTriggerDialog.vue`) and its one-time-key modal have **no unit coverage** of the
drag/dialog/round-trip -- these steps are the safety net. Log in as `qa_wtr_starter`, open
the OA flow admin page, switch to the **触发器 / Triggers** tab.

### 5.1 Build a **timer** trigger

1. New trigger -> type **Timer**; pick preset **每日 9 点 / Daily 09:00**.
2. Confirm the cron field fills and the **next-occurrence preview** shows **5** future
   timestamps (server default timezone note visible).
3. Save -> row appears; "Next Due" populated.

### 5.2 Build an **event** trigger

1. New trigger -> type **Event**; `eventKey` = `QA|OnEchoAsync`; add a varsMap row
   (e.g. `outboundNo` = `$.OutboundNo`).
2. Save -> row appears with the event key shown.

### 5.3 Build a **message** trigger -- one-time key

1. New trigger -> type **Message**; varsSchema = `orderNo,amount`.
2. On save, a **key modal** pops the plaintext API key with the "shown only once, store it
   now" warning (`oa.flowtrigger.keyOnce`). Copy it.
3. Close and **refresh** -> the plaintext is **never shown again** (list shows only a
   "has key" state; `GetAsync` returns `hasApiKey=true`, never `apiKeyPlain`).

### 5.4 Test-fire + fire-log drawer (scenarios 2 & 8 visual)

1. Test-fire the timer trigger -> a toast carries the `instanceId` (instance link clickable).
2. Open the **流水 / Fire Log** drawer -> columns **Time / Result / Instance link / Error**
   all render; the success row links to the started instance.
3. (Failure row) Test-fire the seeded `wtr-badstarter` -> the drawer shows a row whose
   **Error** column renders `E-WF-022` (localised message), no instance link.

### 5.5 Save-validation i18n (scenario 7)

1. New timer trigger, cron = `not a cron`, save -> blocked; the surfaced message resolves the
   backend `E-WF-022` to its **localised** text (not a bare code). Switch the UI language
   (ja / zh-CN / zh-TW / en / ko) and re-trigger to confirm at least **2 locales** render.
2. New trigger targeting a **disabled** flow -> `E-WF-023` localised message.

---

## 6. Manual DB checks (post-run)

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C
```

```sql
-- Scenario 5 whitelist: VarsJson keeps only orderNo/amount, drops 'secret':
SELECT i.VarsJson FROM Wf_FlowInstance i
JOIN Wf_TriggerFire f ON f.InstanceId = i.Id
JOIN Wf_FlowTrigger t ON t.Id = f.TriggerId
WHERE t.TriggerType = 2 ORDER BY f.FiredUtc DESC;   -- WfTriggerType.Message = 2

-- Scenario 4 varsMap: VarsJson = {"outboundNo":"OB-1"}:
SELECT i.VarsJson FROM Wf_FlowInstance i
JOIN Wf_TriggerFire f ON f.InstanceId = i.Id
WHERE f.TriggerId = 'DDDD0000-0000-0000-0000-0000000000E1' ORDER BY f.FiredUtc DESC;

-- Scenario 3 timer: NextDueUtc advanced + repeated fires, all with instanceId:
SELECT NextDueUtc, LastFiredUtc FROM Wf_FlowTrigger WHERE Id = 'DDDD0000-0000-0000-0000-0000000000E2';
SELECT IdempotencyKey, InstanceId, Error FROM Wf_TriggerFire
WHERE TriggerId = 'DDDD0000-0000-0000-0000-0000000000E2' ORDER BY FiredUtc DESC;

-- Scenario 8 failure: bad-starter fire row has Error E-WF-022, InstanceId NULL:
SELECT IdempotencyKey, InstanceId, Error FROM Wf_TriggerFire
WHERE TriggerId = 'DDDD0000-0000-0000-0000-0000000000E3' ORDER BY FiredUtc DESC;
```

Expected: message/event instances carry the filtered/mapped `VarsJson`; the short-cycle
trigger's `NextDueUtc` sits in the future with multiple success fire rows; the bad-starter
trigger's only row is a failure carrying `E-WF-022`.

---

## 7. i18n keys (E-T1, cross-referenced)

`I18nOaFlowTriggerScreenSeed` seeds the trigger screen five-language (ZhCN/ZhTW/En/Ja/Ko),
insert-only on startup: `oa.flowadmin.tab.flows` + the `oa.flowtrigger.*` family (tabs,
columns, type labels, form fields, cron presets, the one-time-key strings
`oa.flowtrigger.keyOnce` / `.keyCreateHint` / `.resetKeyConfirm`, and the fire-log column
labels), plus the engine/validator error codes **`E-WF-022` / `E-WF-023` / `E-WF-024`**.
The front end (`FlowAdmin.vue` / `FlowTriggerPanel.vue` / `FlowTriggerDialog.vue` /
`flowTriggerModel.ts`) references these; the backend throws the `E-WF-0xx` codes which the
UI resolves to the localised strings above (scenarios 7 & 8 confirm the resolution).
