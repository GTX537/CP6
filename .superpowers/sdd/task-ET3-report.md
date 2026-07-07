# Task E-T3 Report — gstack QA harness (wfs-service-task)

**Branch:** `feat/wfs-service-task-finish` (already checked out; no new branch, no push)
**Commit:** `f25c778ecce5f287a4fb867210c637613e879fdc`
**Scope:** write-only. No product code touched. Three files created under
`docs/superpowers/qa/wfs-service-task/`. Nothing executed (live QA runs later).

---

## 1. Deliverables (three files)

| File | Contents |
| --- | --- |
| `docs/superpowers/qa/wfs-service-task/README.md` | Runbook: scenario matrix, env setup, ps1 usage, the raw-INSERT failure-flow principle, real-browser designer steps (5.1–5.4), manual DB checks. |
| `docs/superpowers/qa/wfs-service-task/seed.sql` | 2 users + 1 FormDef + 6 FlowDefs, raw INSERT, `SET QUOTED_IDENTIFIER ON`, singular Wf_ table names, idempotent `IF NOT EXISTS`. |
| `docs/superpowers/qa/wfs-service-task/qa_service_task.ps1` | HTTP e2e over all 6 scenarios, ASCII data, PS5.1-safe (modeled on `wfs-serial-signing/qa_serial.ps1`); polls detail for async settle. |

Modeled on the mature `wfs-serial-signing/` + `wfs-approver-resolution/` three-piece
pattern (README scenarios + seed.sql + parameterized ps1, same helper/assertion style,
same login/cookie/envelope conventions).

---

## 2. Scenario matrix (6 HTTP + 2 browser groups)

| # | FlowKey | kind/mode | Action | ps1 assertion | Verified against |
| --- | --- | --- | --- | --- | --- |
| 1 | svc-sync-writeback | dataWriteback/sync | sampleWriteback | Approved(1) at submit + VarsJson has `writebackEcho` | ServiceTaskNodeHandler sync inline path (AdvanceToken atomic); SampleDataWritebackExecutor outputs `writebackEcho` |
| 2 | svc-async-webapi | webApi/async | erpEcho `/erp/echo/{amount}` | Running(0) then Approved(1) after scan + `echoedPath` | async enqueue + WfServiceJobService ScanOnce + EchoConnector outputs `echoedPath` |
| 3 | svc-timer-wait | timer/async | none | Running(0) then Approved(1) | ComputeDueUtc(duration PT10S); ResolveExecutorKey("none")→null→Ok→advance |
| 4 | svc-timer-action | timer/async | erpEcho at due | Approved(1) + `echoedPath` | Snapshot: timer+ConnectorName→actionKind webApi; worker runs at DueAtUtc |
| 5 | svc-fail-erroredge | webApi/async | ghostConnector | appr pending appears; instance Running(0); `wf.serviceError` | maxAttempts=1 exhaust→FailServiceTokenAsync→AdvanceAlongErrorEdge (isError) |
| 6 | svc-fail-suspend | webApi/async | ghostConnector | Suspended(4); `wf.serviceError` | same fail path, no isError edge→Suspend |
| B1 | (designer) | — | palette drag×3 + save + reload round-trip | serviceKind preserved per node | DesignerCanvas dragKey `serviceTask:<kind>`; NODE_PALETTE 数据回写/接口调用/定时器 |
| B2 | (designer) | — | property panel per kind + error-edge checkbox + chip observation | catalog-sourced pickers; E-WF-017; D-T2 chip verdict | GetServiceCatalog (dataWriteback+VisibleInDesigner only); FlowSchemaValidator E-WF-016/017 |

---

## 3. Seed reconciliation vs migration + entities

**Migration `20260629142700_WfsServiceTask.cs`** — Wf_ServiceJob table confirmed:
columns `Kind`, `ActionRefJson`, `DueAtUtc`, `Status`(int), `AttemptCount`,
`MaxAttempts`, `NextAttemptAtUtc`, lease trio `LockedBy`/`LockedAtUtc`/`LockExpiresAtUtc`,
`LastError`(nvarchar 1000), `RowVersion`; indexes `IX_Wf_ServiceJob_Instance`,
`IX_Wf_ServiceJob_Scan`, filtered unique `UX_Wf_ServiceJob_LiveToken` on
`(TenantId,TokenId,NodeId) WHERE Status IN (0,1)`. → README §2.2 and the ps1
manual-check queries reference only real columns (no seed writes to this table;
jobs are created by the engine at runtime, which is the point).

**Table names:** seed uses `Sys_Users` (plural — matches proven serial seed &
Sys_User entity), `Wf_FormDef` / `Wf_FlowDef` (singular — matches serial seed,
avoids the plural-name翻车 f90a138). ps1/README manual checks use `Wf_FlowInstance`,
`Wf_FlowToken`, `Wf_ServiceJob` (singular — entity class names; Wf_FlowToken
singular already proven in serial harness manual checks).

**Sys_Users insert column list** copied verbatim from the proven serial seed
(Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate, TenantId,
FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled,
IsPlatformAdmin, PasswordChangedAt) — all present on Sys_User entity (grep-confirmed).

**FlowDef/FormDef column lists** identical to serial seed (Id, FlowKey, FlowName,
FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId) — proven.

**Node schema fields** all camelCase per FlowSchema.cs `FlowNode`:
`serviceKind`/`serviceMode`/`serviceConnectorName`/`serviceActionName`/`servicePath`/
`serviceParamsJson`/`serviceDelayMode`/`serviceDelayValue`/`serviceMaxRetries`; edge
`isError`. FlowEngine deserializes with PropertyNameCaseInsensitive — camelCase OK.

**delayMode values** used: `duration` with `PT10S` (ISO-8601, parsed by
`ParseDuration`→XmlConvert.ToTimeSpan). Matches ComputeDueUtc switch.

**Executor/connector names** used exactly as landed: executor Key `sampleWriteback`
(dataWriteback, VisibleInDesigner=true), connector Name `erpEcho` (EchoConnector,
DisplayName "ERP Echo (demo)"), webApi executor Key `webApi` (VisibleInDesigner=false).

---

## 4. API-path reconciliation vs controllers

| ps1 call | Controller | Confirmed |
| --- | --- | --- |
| `POST /api/auth/login` {userName,password} | auth | serial harness proven |
| `POST /api/wf/flow/submit` {flowKey,varsJson,bizType,bizId}→data.instanceId | FlowController.cs:51 `SubmitReq(FlowKey,VarsJson,BizType,BizId)`; `SubmitAsync(...,varsJson??"{}",...)` | ✅ |
| `GET /api/oa/inbox/detail/{id}`→data.instance.status / data.currentDataJson | InboxDetail(Instance, …, CurrentDataJson=inst.VarsJson) InboxModels.cs:37, InboxService.cs:252 | ✅ |
| `GET /api/oa/inbox/pending`→data[].instanceId | serial harness proven | ✅ |
| `GET /api/oa/designer/service-catalog` (README browser step) | DesignerController.cs:36 | ✅ |

---

## 5. Self-check findings

1. **EchoConnector has no failure-injection switch** — verified by reading
   `EchoConnector.cs`: `CallAsync` always returns `ServiceTaskResult.Ok(...)`.
   → Failure scenarios 5/6 therefore use the brief's fallback: reference an
   **unregistered** connector (`ghostConnector`) and **INSERT the FlowDef directly**
   to bypass the E-WF-018 save-time registration check in `DesignerService.SaveAsync`
   (lines 51–65). README §4 documents this principle explicitly.

2. **No manual ScanOnce HTTP endpoint exists** — grep of `CP6.WebApi/Controllers`
   found none. Async scenarios therefore must wait for `WfServiceJobScanWorker`
   (20s interval). ps1 uses a bounded poll loop (`-WaitSeconds`, default 90) and
   README warns a full run takes ~2–3 min. This matches the brief's "无则等 20s".

3. **Fast-fail tuning:** `serviceMaxRetries: 0` → `MaxAttempts = 1`. An async job
   (AttemptCount=0) fails and exhausts on the **first** scan (`1 < 1` false),
   routing immediately with **no 30s backoff**. Verified against
   ServiceTaskNodeHandler (`maxAttempts = retries + 1`) and WfServiceJobService
   (increment-then-compare, backoff only when `AttemptCount < MaxAttempts`).

4. **`wf.serviceError` write path** — confirmed `FailServiceTokenAsync`
   (FlowEngine.cs:350) calls `WriteServiceError` which direct-writes
   `wf.serviceError{nodeId,message,failedAtUtc}` into VarsJson (controlled reserved
   path, not via MergeOutputVars). ps1 asserts substring `serviceError` in the
   detail VarsJson for scenarios 5/6.

5. **Detail VarsJson exposure** — InboxDetail exposes both `Instance` (full entity,
   carries `varsJson`) and `CurrentDataJson` (= inst.VarsJson). ps1 `VarsOf` reads
   `currentDataJson` first, falls back to `instance.varsJson`.

### Open concerns (for live QA, not blockers)

- **Detail read authorization for non-Approved instances:** ps1 relies on the
  starter (`qa_svc_starter`) reading `inbox/detail/{id}` for Running (S2/3/4/5) and
  Suspended (S6) instances. Serial harness proved starter-read for Approved; owner-read
  of Running/Suspended is assumed. If detail is role-gated beyond ownership, S2–S6
  status polling would need adjustment — flag for the live run.
- **Case-sensitivity note (from prior observations):** validator uses Ordinal for
  connector/kind matching while runtime uses OrdinalIgnoreCase. The seed uses exact
  casing (`erpEcho`, `sampleWriteback`, `dataWriteback`, `webApi`, `timer`) so this
  divergence is not exercised; noted only so a future casing-variant scenario is aware.
- **Chip observation (D-T2 §5.4)** is a verdict to record during live QA, not a code
  change in this write-only task.
