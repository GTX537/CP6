# WFS Sub-flow (F-T2) -- QA Runbook

**Branch:** `feat/wfs-subflow`
**Date:** 2026-07-14
**Feature scope:** sub-flow call-activity (`subFlow` node, the 9th node handler;
`SubmitChildAsync` writes the three back-pointer columns in the constructor;
depth guard 8 -> `E-WF-026`) + two-phase park/resume (resume credential
`TokenId` = child-instance Id / `NodeId` = `$subFlowResume`; in-request **fast path**
plus the 20s scan-worker as fallback) + multi-instance (`subCollectionVar` expands
N children, `all` / `any` completion policy, `BuildOutMerge` writes back an array
ordered by `SubIndex`) + cascade (all-policy any-reject / any-policy first-pass both
withdraw the remaining in-flight children) + `onBranchReject=prune` composition +
save-time validation (`E-WF-025` static in `FlowSchemaValidator`, `E-WF-025/026`
reference + DFS cycle in `SubFlowRefValidator`) + designer `subFlow` node/panel +
inbox parent/child interlink + i18n (F-T1, 17 keys).

> **Status: written, not run.** This harness is authored per task F-T2 (write-only).
> Live QA -- spin the backend against the isolated DB, run the ps1 HTTP e2e, drive
> the designer and inbox in a real browser -- is executed later by the main agent with
> a QA user present. **Nothing here has been executed.** Bugs found during live QA are
> fixed TDD (regression test into `CP6.Tests/Wf/**` or `cp6.web` vitest).

---

## 1. What this harness covers

Three deliverables:

| File | Purpose |
| --- | --- |
| `seed.sql` | 4 users + 1 FormDef + 5 FlowDefs (1 child + 4 parents), raw INSERT into `CP6DB_OA`. |
| `qa_subflow.ps1` | HTTP e2e over scenarios 1, 3, 4, 5, 6, 8 against a running backend (ASCII data, real status codes). |
| `README.md` | This runbook: setup, scenario matrix, expected results, DB/worker drills, browser steps, i18n cross-ref, DoD self-check. |

### Scenario matrix (brief 7 + B-T3 watch-item 8 -> harness)

| # | Mechanic | FlowKey | Where | Expected outcome |
| --- | --- | --- | --- | --- |
| 1 | **Single instance full chain** + fast-path/worker resume | `sf-parent-single` | ps1 + **DB/worker drill** (sec 4.1) | submit -> parent `Running`, detail shows 1 sub-flow instance; approve child -> child `Approved`, parent resumes to `pa` **in-request** (fast path, <2s); approve `pa` -> parent `Approved`. Worker-fallback drill: sec 4.1. |
| 2 | **Parent/child interlink** (browser) | `sf-parent-single` | **Browser** (sec 5.1) | child `FormDetail` shows a **Parent Flow** link (`oa.detail.parentFlow`) -> parent detail; parent detail shows the **Sub-flow Instances** list (`oa.detail.subFlows`) -> click a child -> child detail. |
| 3 | **Multi-instance ALL + ordered array write-back** | `sf-multi-all` | ps1 | 3-element collection -> 3 children; finish **out of order** -> parent `results` write-back = `["itemA","itemB","itemC"]` ordered by `SubIndex` (`currentDataJson`). |
| 4 | **ALL any-reject + cascade** | `sf-multi-all` | ps1 | reject one child -> the other 2 in-flight children are cascade-**Withdrawn**, parent `Rejected` (no error edge, no `ForkId`); `currentDataJson` carries `subFlowError`. |
| 5 | **ANY first-pass** | `sf-multi-any` | ps1 | first child approved -> parent resumes (`Running`); the other 2 children cascade-**Withdrawn**; approve `pa` -> `Approved`. |
| 6 | **prune composition** | `sf-combo-prune` | ps1 | `parallelSplit onBranchReject=prune` with a `subFlow` branch; reject that branch's child -> only that branch pruned (instance stays `Running`), sibling `bAppr` survives; approve it -> join dyn-counts (pruned drops) -> `Approved`. |
| 7 | **Designer real browser** | -- (designer) | **Browser** (sec 5.2) | palette drag `subFlow` (double-border node) -> panel picks a published, non-self target -> self-ref / mutual-ref save blocked with `E-WF-026` (five-language); delete the non-error out edge -> `oa.designer.errSubFlowConfig`. |
| 8 | **Withdraw child -> parent fast path** (B-T3 watch item) | `sf-parent-single` | ps1 | starter withdraws the **child** instance while parked at `ca` -> `WithdrawAsync` enqueues the resume credential and runs the DI-injected `FlowEngine` fast path in the same request -> parent `Rejected`, `subFlowError` present. Proves the scoped-`FlowEngine`-into-`TaskCenterService` optional-ctor wiring live. |

> **Why scenario 8 was added.** The B-T3 review flagged that production DI injects the
> scoped `FlowEngine` into `TaskCenterService`'s optional ctor parameter to give
> `WithdrawAsync` its fast path (`Program.cs:142` resolves the `FlowEngine` registered
> at `Program.cs:126`), and that this exact wiring had **no live evidence** (unit tests
> construct `TaskCenterService(db)` with `engine == null`, taking the worker-fallback
> branch). Scenario 8 exercises the non-null branch end to end. It is folded in here per
> the F-T2 brief's watch-item instruction (the brief's 7 scenarios did not cover it).

---

## 2. Environment setup

### 2.1 Isolated database

Reuse the `CP6DB_OA` database from prior WFS QA sessions, or create fresh:

```sql
CREATE DATABASE CP6DB_OA;
```

Point the backend at it so QA data never touches live `CP6DB`. On first boot
`db.Database.Migrate()` applies all EF migrations -- this wave adds **exactly one**
migration (`WfsSubFlow`, `CP6.Core/Migrations/20260714075419_WfsSubFlow.cs`): three
nullable columns on `Wf_FlowInstance` (`ParentInstanceId` / `ParentTokenId` /
`SubIndex`) plus two indexes (`IX_Wf_FlowInstance_Parent` on `(TenantId,
ParentInstanceId)` and the filtered unique `UX_Wf_FlowInstance_SubSlot` on `(TenantId,
ParentTokenId, SubIndex) WHERE ParentTokenId IS NOT NULL`). An existing `CP6DB_OA`
from before this wave needs that one migration applied (automatic on boot).

### 2.2 Apply seed

Run from a **native shell** (cmd / PowerShell), not git-bash:

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
```

The seed prints a 10-row sanity report (4 users + 1 form + 5 flowdefs). All users
share password `123456` (admin's BCrypt hash cloned) and admin's `RoleId` (submit is
gated by `oa-form-catalog:submit`, `FlowController.cs:54`; `PermissionService` has
**no admin bypass**, so the seeded users inherit admin's role to pass it). Roles:

| User | Role in the flows |
| --- | --- |
| `sf_starter` | submits every parent; is the starter of the child instances too (so it can withdraw them in scenario 8). |
| `sf_parent` | approves the parent-side approval node `pa`. |
| `sf_child` | approves / rejects the child approval node `ca` (all child instances route here). |
| `sf_b` | approves the combo sibling branch `bAppr` (scenario 6). |

`SET QUOTED_IDENTIFIER ON` is set (required by the filtered unique indexes on
`Wf_FlowDef`). Table names are singular (`Wf_FlowDef` / `Wf_FormDef`). `RowVersion` is
never inserted (auto-generated); no flow **instances** are seeded -- they are created
at runtime via `/api/wf/flow/submit`.

**Raw-INSERT rationale:** `FlowSchemaValidator` + `SubFlowRefValidator` run **only**
through `DesignerService.SaveAsync`. A raw INSERT skips them, so the seeded schemas
stand exactly as written and there is no reference-order dependency. All five flows are
in fact valid (they would also pass `E-WF-025/026`).

### 2.3 Backend

Prior WFS QA sessions used ports 5177-5181; this harness defaults to **5181**:

```powershell
cd <repo>\CP6.WebApi
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --urls "http://localhost:5181"
```

- The `WfServiceJobScanWorker` (20s interval, `WfServiceJobScanWorker.cs:12`) starts
  automatically. It is the **fallback** resume path: it handles `subFlowResume` jobs via
  an internal `Kind` short-circuit **before** the executor state gate (`WfServiceJobService.cs:97-115`)
  so the `$subFlowResume` sentinel `TokenId` is never mistaken for a real token and
  Cancelled. In the happy path the **fast path** (`FlowEngine.FastPathSubFlowResumeAsync`,
  invoked at the tail of the write methods and of `TaskCenterService.WithdrawAsync`)
  resumes the parent in the same request, so the worker usually finds the credential
  already `Succeeded` and no-ops (state gate).
- **Dev CSRF must be disabled** (`Security:Csrf:Enabled=false`, the dev default): the
  submit / act / withdraw POSTs are cookie-auth'd (the JWT `cp6_at` cookie flows via
  `-WebSession`) and would 403 on the CSRF double-submit otherwise.

---

## 3. Running the HTTP e2e

```powershell
.\qa_subflow.ps1
.\qa_subflow.ps1 -BaseUrl http://localhost:5181
```

The script logs in the 4 seeded users, then walks scenarios 1, 3, 4, 5, 6, 8, printing
`PASS`/`FAIL`/`WARN` and a final tally. It captures **real** HTTP status codes (via
`Invoke-RestMethod` + `Read400Body`) so it can tell `200` from `400`/`403`. Exit code 1
if any FAIL.

### Endpoints exercised

| Purpose | Call |
| --- | --- |
| Login | `POST /api/auth/login` `{ userName, password }` -> `cp6_at` cookie |
| Submit | `POST /api/wf/flow/submit` `{ flowKey, varsJson }` -> `data.instanceId` (`FlowController.cs:53`) |
| Act | `POST /api/wf/task/{id}/act` `{ approve, comment }` (`FlowController.cs:66`) |
| Withdraw | `POST /api/wf/flow/{id}/withdraw` (route id; `TaskController.cs:34`, `RequirePermission oa-inbox:withdraw`) |
| Pending inbox | `GET /api/oa/inbox/pending` -> `InboxPendingItem[]` (`.taskId .instanceId .nodeId`) |
| Detail | `GET /api/oa/inbox/detail/{id}` -> `InboxDetail` (`InboxController.cs:124`) |

### Detail envelope (verified against `InboxService.DetailAsync` / `InboxModels.cs`)

`GET /api/oa/inbox/detail/{id}` -> `{ code:0, message:"OK", data: InboxDetail }`. The
sub-flow-relevant fields (camelCase JSON of the `InboxDetail` record,
`InboxModels.cs:47-50`):

- `data.instance.status` -- `FlowInstanceStatus`: `0 Running 1 Approved 2 Rejected 3 Withdrawn 4 Suspended 5 Draft` (`WfStatus.cs:6-12`).
- `data.currentDataJson` -- the instance `VarsJson` snapshot (`InboxService.cs:279`); carries the write-back `results` array (scenario 3) and the `subFlowError` object (scenarios 4/8).
- `data.subFlowParent` -- `{ instanceId, flowKey, flowName }` when this instance is a child (`SubFlowParentRow`, `InboxModels.cs:44`); `null` for top-level.
- `data.subFlows[]` -- `{ instanceId, subIndex, flowKey, flowName, status, nodeId }` per child instance under this parent (`SubFlowChildRow`, `InboxModels.cs:45`), grouped/ordered by the parked token's `nodeId` then `subIndex` (`InboxService.cs:268-276`).

The `detail` endpoint applies no owner filter today (any authenticated user reads any
instance's detail, `InboxController.cs`), so the starter session polls child status and
the `subFlows` list directly. If that endpoint ever gains an owner check, the child
polls here need a child-approver session instead -- that is this known assumption
breaking, not a sub-flow bug.

### Task selection

Because all child instances route to the same `sf_child` account, the script selects
each task by **(instanceId, nodeId)** from the acting user's pending inbox -- never
"the single pending task", which is ambiguous across N children and re-runs. Child
instance Ids come from the parent's `data.subFlows` list.

---

## 4. Manual checks (not automatable in the ps1)

### 4.1 Scenario 1 -- fast path vs worker fallback (DB drill)

The ps1 proves the **fast path** implicitly: after `approve child`, the parent
approval task at `pa` is available on the first poll (well under the 20s worker
interval). To make the fast-path-vs-worker distinction explicit, inspect the resume
credential after a scenario-1 run:

```
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C
```
```sql
-- The subFlowResume credential for the just-completed child. Fast path consumed it:
--   Status = Succeeded (2), LockedBy IS NULL (the worker never leased it),
--   CompletedAtUtc within the act request window.
SELECT TokenId, NodeId, Kind, Status, LockedBy, LockExpiresAtUtc, CompletedAtUtc
FROM Wf_ServiceJob WHERE NodeId = '$subFlowResume' ORDER BY CreateDate DESC;
```

`LockedBy IS NULL` on a `Succeeded` credential is the evidence the **fast path** (not
the scan worker) did the resume. If the worker had carried it, `LockedBy` would hold a
`MachineName:guid` lease id.

**Worker-fallback ("stop worker") drill.** The scan worker is an in-process
`IHostedService` co-hosted with the web API (`Program.cs:160`); there is **no runtime
toggle** to stop it independently of `dotnet run`. Two operable ways to exercise the
worker-only path:

1. **Source-disable (true "stop worker"):** comment out
   `builder.Services.AddHostedService<...WfServiceJobScanWorker>();` (`Program.cs:160`),
   rebuild, and run. Now the fast path is the **only** resume path -- run the ps1 and
   confirm scenario 1 still passes (parent resumes in-request). This proves the fast
   path is worker-independent. (Do not commit this edit -- it is a QA-only local change.)
2. **Simulated crash-after-commit (worker carries it):** with the worker **running**,
   submit `sf-parent-single`, note the child + parked parent token, then hand-insert a
   `Pending` `subFlowResume` credential (as if a process crashed after the child's
   terminal commit but before the fast path) and let the 20s worker resume the parent:
   ```sql
   -- Look up the parked parent token (its NodeId is the parent's subFlow node 'sub'):
   SELECT t.Id AS ParentTokenId, t.InstanceId AS ParentInstanceId, c.Id AS ChildInstanceId, c.SubIndex
   FROM Wf_FlowToken t
   JOIN Wf_FlowInstance p ON p.Id = t.InstanceId AND p.FlowKey = 'sf-parent-single'
   JOIN Wf_FlowInstance c ON c.ParentTokenId = t.Id
   WHERE t.NodeId = 'sub' AND t.Status = 0 ORDER BY p.CreateDate DESC;   -- FlowTokenStatus.Active = 0
   -- After approving the child (so it is terminal) insert a fresh Pending credential.
   -- Kind is a STRING column (WfStatus.cs:86 -- WfJobKind.SubFlowResume = "subFlowResume"):
   --   TokenId = child instance Id, NodeId = '$subFlowResume', Kind = 'subFlowResume',
   --   ActionRefJson = {"parentTokenId":"...","parentInstanceId":"...","childInstanceId":"...","subIndex":0}
   --   (camelCase, matches SubFlowResumePayload), Status = 0 (Pending),
   --   DueAtUtc = NextAttemptAtUtc = GETUTCDATE(), MaxAttempts = 4, InstanceId = parent instance Id.
   ```
   Within one 20s scan the worker's `Kind` short-circuit
   (`WfServiceJobService.cs:97`) calls `CheckSubFlowGroupAsync` and resumes the parent
   to `pa`. (Verify the credential ends `Succeeded` with a non-null `LockedBy`.)

### 4.2 Scenario 8 -- withdraw permission prerequisite

`POST /api/wf/flow/{id}/withdraw` is gated by `oa-inbox:withdraw`
(`TaskController.cs:35`). The seeded users inherit admin's `RoleId`; if that role is
**not** granted `oa-inbox:withdraw`, the ps1 emits a WARN and skips scenario 8's
assertions. Grant it (per-tenant `RoleAction`, mirroring how the other WFS harnesses
grant menu actions) and re-run. This is a data prerequisite, not a sub-flow bug.

### 4.3 Manual DB checks (post-run)

```sql
-- Child instances and their three back-pointer columns:
SELECT Id, FlowKey, ParentInstanceId, ParentTokenId, SubIndex, Status
FROM Wf_FlowInstance WHERE ParentInstanceId IS NOT NULL ORDER BY CreateDate DESC;

-- Sub-flow history breadcrumbs (submit/started/resumed/error/cascade):
SELECT InstanceId, NodeId, Action, Comment FROM Wf_FlowHistory
WHERE Action LIKE 'subFlow%' ORDER BY CreateDate DESC;

-- Scenario 4/8: parent VarsJson carries subFlowError {nodeId,code,subIndex,childInstanceId,childStatus,atUtc}:
SELECT Id, Status, VarsJson FROM Wf_FlowInstance WHERE FlowKey IN ('sf-multi-all','sf-parent-single') ORDER BY CreateDate DESC;

-- The filtered unique slot index prevents duplicate (ParentTokenId, SubIndex) children (park re-entry idempotency):
SELECT ParentTokenId, SubIndex, COUNT(*) n FROM Wf_FlowInstance
WHERE ParentTokenId IS NOT NULL GROUP BY ParentTokenId, SubIndex HAVING COUNT(*) > 1;   -- expect 0 rows
```

---

## 5. Browser walkthroughs (manual, gstack browse)

Log in as any seeded user (e.g. `sf_starter`). Save screenshots into this directory.

### 5.1 Scenario 2 -- parent/child interlink (`FormDetail.vue`)

1. Run scenario 1 first (or submit `sf-parent-single` and approve nothing yet) so a
   parked parent + a live child exist.
2. Open the **child** instance's `FormDetail` (from `sf_child`'s inbox, node `ca`).
   Confirm a **Parent Flow / 父流程** link (`oa.detail.parentFlow`) renders; click it ->
   lands on the parent's detail.
3. On the **parent** detail confirm the **Sub-flow Instances / 子流程实例** section
   (`oa.detail.subFlows`) lists the child rows, each showing its `#{subIndex}` (rendered
   directly from the data attribute -- **not** a `t()` key), flow name, and status label
   (reuses the existing `oa.inst.*` status tags). Click a child row -> child detail.
4. Switch the UI language (ja / zh-CN / zh-TW / en / ko) and confirm `parentFlow` /
   `subFlows` render localised text in at least two locales.

### 5.2 Scenario 7 -- designer real browser (`SubFlowNode.vue` / `NodePropertyPanel.vue` / `designerModel.ts`)

Component drag/drop and the palette->canvas->save round-trip have no unit coverage --
these steps are the safety net. Open the OA flow **designer**.

1. **Palette -> canvas:** drag **子流程 / Sub-flow** (`subFlow`, `oa.designer.subflow.title`)
   from the palette onto the canvas. Confirm it renders as the **double-border**
   call-activity node (distinct from plain approval / serviceTask nodes) with the i18n
   label for the active language.
2. **Target dropdown:** in the property panel, the **目标流程 / Target Flow**
   (`oa.designer.subflow.target`, hint `oa.designer.subflow.targetHint`) select is fed by
   `GET /api/oa/designer/list` (`DesignerController.cs:33` -> `FlowDefSummary`) and filters
   to **published (`enable`) flows other than the current one** -- so the seeded
   `sf-child-approve` etc. appear, but the flow you are editing does not.
3. **Multi-instance:** toggle **多实例 / Multi-instance** (`oa.designer.subflow.multi`) ->
   the **集合变量 / Collection Variable** (`oa.designer.subflow.collectionVar`) and
   **完成策略 / Completion Policy** (`oa.designer.subflow.policy` with `.policy.all` /
   `.policy.any`, hint `.policyHint`) fields appear; **传入/回注变量映射**
   (`oa.designer.subflow.varsIn` / `.varsOut`, hint `.varsHint`) accept the JSON maps.
4. **Cycle / self-ref save block:** point the target at the current flow itself (self-ref),
   or wire two flows that reference each other (mutual-ref), and save. The backend
   `SubFlowRefValidator` (`DesignerService.SaveAsync` -> DI layer) throws
   **`E-WF-026`**, surfaced through `DesignerController.Err` as a bare code in `message`;
   the UI resolves it to the seeded five-language text (`E-WF-026` key). Confirm at least
   two locales render.
5. **Client validation:** delete the node's non-error out edge (or clear `SubFlowKey`) ->
   the client validator (`designerModel.ts validateClient`, mirror of the `E-WF-025`
   static rules) surfaces **`oa.designer.errSubFlowConfig`** and blocks the save.

---

## 6. i18n keys (F-T1, cross-referenced)

`I18nOaSubFlowScreenSeed` seeds **17** keys five-language (ZhCN/ZhTW/En/Ja/Ko),
insert-only, appended to the `Program.cs` i18n concat chain after
`I18nOaEngineInfraScreenSeed` (wave ⑤). All are referenced by the front-end designer /
inbox or thrown by the backend engine/validator:

| Key(s) | Referenced by |
| --- | --- |
| `oa.designer.subflow.title` | `SubFlowNode.vue` (node label) |
| `oa.designer.subflow.target` / `.targetHint` / `.varsIn` / `.varsOut` / `.varsHint` / `.multi` / `.collectionVar` / `.policy` / `.policy.all` / `.policy.any` / `.policyHint` (11) | `NodePropertyPanel.vue` sub-flow segment |
| `oa.designer.errSubFlowConfig` | `designerModel.ts validateClient` (mirror of static `E-WF-025`) |
| `oa.detail.parentFlow` / `oa.detail.subFlows` | `FormDetail.vue` interlink |
| `E-WF-025` / `E-WF-026` | `FlowSchemaValidator.cs` / `SubFlowRefValidator.cs` / `SubFlowNodeHandler.cs` / `WfServiceJobService.cs` / `FlowEngine.SubFlow.cs` |

> **Deliberate non-keys** (per F-T1 seed doc, to keep "zero missing / zero orphan"):
> the child-row `#{subIndex}` is rendered directly from the data attribute in
> `FormDetail.vue` (not a `t()` key); child status labels reuse the existing `oa.inst.*`
> tags. `varsHint` describes the JSON map format in prose (no literal `{ }` in the value)
> because vue-i18n forbids bare `{ } @ |` in message strings.

---

## 7. DoD self-check (plan Global Constraints)

F-T2 is docs-only (no code); the code-level gates below are the wave's, verified by the
prior task reports and re-run by the main agent in the full-suite pass. This harness's
own contribution is the last row.

| DoD item (w6-globals) | Status at F-T2 |
| --- | --- |
| Backend `dotnet test` all green (baseline 2181 green / 5 skip) | 2181 green / 5 skip at wave baseline (B-T3 report cited 2148 mid-wave); main agent re-runs the full suite. |
| Existing Wf tests byte-equivalent; `TaskCenterService(db)` 9-call sites unchanged (ctor added an **optional** param) | `TaskCenterService.cs:12` -- `TaskCenterService(CP6Context db, FlowEngine? engine = null)`; `engine == null` (existing tests) takes the worker-fallback branch. |
| Frontend `npm run test` (baseline 481) + type-check + build green | 481 baseline; the palette clist assertion in `designerModel.test.ts` is the one existing test allowed to change (+1 for `subFlow`); main agent re-runs. |
| EF `has-pending-model-changes` clean; **exactly one migration `WfsSubFlow`** (3 columns + 2 indexes) | `20260714075419_WfsSubFlow.cs` only; three nullable `Wf_FlowInstance` columns + `IX_Wf_FlowInstance_Parent` + filtered-unique `UX_Wf_FlowInstance_SubSlot`. |
| Zero cross-module pollution (Wf/Oa services + WebApi Oa/Wf controllers + `cp6.web/src/{api,views}/oa`) | Held per task report; no `space/*` touch. |
| spec §8 matrix covered; `E-WF-025`/`026` each have static + service-layer tests | `SubFlowValidatorTests` (static + `SubFlowRefValidator`), `SubFlowHandlerTests` / `SubFlowResumeCheckTests` / `SubFlowTwoPhaseTests` / `SubFlowConcurrencyTests` / `SubFlowCascadeTests` / `SubFlowSendBackComboTests` / `SubFlowVarsMapperTests` / `SubFlowModelTests`. |
| Five-language seed complete (ZhCN/ZhTW/En/Ja/Ko), no duplicate LangKey | F-T1: `I18nOaSubFlowScreenSeed` (17-key oracle), insert-only, whole-DB dedup verified. |
| Zero hardcoded colors (Design-System token) | Held (F-T1 note; `SubFlowNode.vue` uses `.dot-*` / `var(--cp-*)` tokens). |
| gstack QA harness present (7 scenarios + B-T3 watch-item 8) + live QA all-pass (QA user, isolated CP6DB_OA) | **This harness** (`seed.sql` + `qa_subflow.ps1` + this README): ps1 covers 1/3/4/5/6/8, DB/worker drills cover 1-fallback, browser covers 2/7. **Live pass pending** (write-only). |

**F-T2 self-check conclusion:** the harness is complete and cross-checked against the
shipped code (sec 3 envelope references + `w6-FT2-report.md` file:line table). Every DoD
item is either green at the prior task boundaries or attested here; the main agent runs
the full-suite gate and the live pass (ps1 scenarios 1/3/4/5/6/8 + browser 2/7).
