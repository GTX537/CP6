# WFS 串簽 (Serial Signing) — T13 QA Runbook

**Branch:** `feat/wfs-serial-sign`
**Date:** 2026-06-28
**Feature scope:** Serial multi-stage approval nodes (串簽档位), generalised send-back (退回三目标: prevStage / starter / node), managerChain org-freeze (R13), read-model StageIndex/StageRound, E-WF-011/012/013, five-language i18n.

---

## 1. Environment setup

### 1.1 Isolated database

Create a fresh SQL Server database so this QA session never touches the live `CP6DB`:

```sql
-- Run as sysadmin in SSMS or sqlcmd
CREATE DATABASE CP6DB_OA;
```

Then restore (or copy-restore) the baseline schema into `CP6DB_OA` from the latest `CP6DB` backup — OR just point the backend at the new empty DB and let EF migrations build the schema from scratch on first boot.

### 1.2 Connection string override

Create `CP6.WebApi/appsettings.Local.json` (gitignored) — or set the env var — before starting the backend:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

Or via PowerShell before running `dotnet run`:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
```

### 1.3 Backend port

The Space session occupies `http://localhost:5177`. Start this QA backend on **port 5178**:

```powershell
cd D:\CP6-wfs-serial\CP6.WebApi
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --urls "http://localhost:5178"
```

On first boot, `db.Database.Migrate()` automatically applies all EF migrations including `20260628111925_WfsSerialSign` which adds:
- `Wf_FlowToken.StagePlanJson` (nvarchar(max), nullable)
- `Wf_FlowTask.StageIndex` / `StageRound` (int, default 0)
- `Wf_FlowFormTo.StageIndex` / `StageRound` (int, nullable)
- Index `IX_Wf_FlowTask_Tally` on `(InstanceId, NodeId, TokenId, StageIndex, StageRound, Status)`

Confirm migrations applied:

```sql
SELECT MigrationId FROM CP6DB_OA.dbo.__EFMigrationsHistory
WHERE MigrationId LIKE '%WfsSerial%';
-- Expected: 1 row: 20260628111925_WfsSerialSign
```

### 1.4 Frontend port

The Space session occupies `http://localhost:5173`. Start the OA frontend on **port 5180**:

```powershell
cd D:\CP6-wfs-serial\cp6.web
# Edit vite.config.ts or use env override:
$env:VITE_PORT = "5180"
npm run dev -- --port 5180
```

The Vite dev proxy routes `/api` to `http://localhost:5178` — update `vite.config.ts`'s `server.proxy` target if needed (default is `http://localhost:5177`).

### 1.5 Apply the QA seed

After the backend has booted and migrations have run, apply the seed from a **native PowerShell or cmd** window (not git-bash — see Gotchas):

```powershell
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i "D:\CP6-wfs-serial\docs\superpowers\qa\wfs-serial-signing\seed.sql"
```

The seed creates:
- 6 users: `qa_s_s0` / `qa_s_start` / `qa_s_mgr1` / `qa_s_mgr2` / `qa_s_gm` / `qa_s_newmgr`
- 1 FormDef: `serial-demo-form`
- 1 FlowDef: `serial-demo` (3 design-time stages that expand to 4 runtime stages for `qa_s_start`)

---

## 2. Scenarios

All scenarios run at `http://localhost:5178` (backend) + `http://localhost:5180` (frontend).
All seeded users have password `123456`.

### Runtime stage layout for `serial-demo` when started by `qa_s_start`

| RuntimeStageIndex | Design stage | Kind          | Approver(s)      |
|------------------:|:-------------|:--------------|:-----------------|
| 0                 | Stage 0      | fixed         | `qa_s_s0`        |
| 1                 | Stage 1 L1   | managerChain  | `qa_s_mgr1`      |
| 2                 | Stage 1 L2   | managerChain  | `qa_s_mgr2`      |
| 3                 | Stage 2      | fixed         | `qa_s_gm`        |

---

### Scenario 1 — Designer save/validate (serial stage config)

**Precondition:** logged in as admin.

**Steps:**

1. Open the flow designer at `/oa/designer` in the frontend.
2. Load flow `serial-demo` (GET `/api/oa/designer/load/serial-demo`). Confirm response contains `schemaJson` with `"stages"` array and three entries.
3. **Happy path — valid 3-stage config:**
   POST `/api/oa/designer/save` with body:
   ```json
   {
     "flowKey": "serial-demo-v2",
     "flowName": "Serial Demo V2",
     "formKey": "serial-demo-form",
     "functionId": null,
     "flowCode": null,
     "schemaJson": "{\"start\":\"s\",\"nodes\":[{\"id\":\"s\",\"type\":\"start\",\"name\":\"Start\"},{\"id\":\"a1\",\"type\":\"approval\",\"name\":\"Serial Approval\",\"stages\":[{\"name\":\"S0\",\"kind\":\"fixed\",\"approverStrategy\":\"Specified\",\"approverUserId\":\"AAAA0000-0000-0000-0000-0000000000A0\",\"countersign\":\"all\"},{\"name\":\"S1\",\"kind\":\"managerChain\",\"maxLevels\":2,\"countersign\":\"all\"},{\"name\":\"S2\",\"kind\":\"fixed\",\"approverStrategy\":\"Specified\",\"approverUserId\":\"AAAA0000-0000-0000-0000-0000000000E0\",\"countersign\":\"all\"}]},{\"id\":\"end\",\"type\":\"end\",\"name\":\"End\"}],\"edges\":[{\"from\":\"s\",\"to\":\"a1\"},{\"from\":\"a1\",\"to\":\"end\"}]}"
   }
   ```
   **Expected:** HTTP 200, `{ code: 0, data: true }`.

4. **Negative — managerChain without maxLevels (E-WF-011):**
   POST `/api/oa/designer/save` with a `schemaJson` whose stage has `"kind":"managerChain"` and no `maxLevels` (or `maxLevels: 0`).
   **Expected:** HTTP 400, `message` contains `"E-WF-011"`.

5. **Negative — unknown countersign (E-WF-011):**
   POST `/api/oa/designer/save` with a stage having `"countersign":"unknown"`.
   **Expected:** HTTP 400, `message` contains `"E-WF-011"`.

**Verify in designer UI:** The 串簽档位 panel (「Serial Stages」section) shows three rows for `serial-demo`. The 逐级 row displays a 最大级数 field (max levels = 2). Invalid config shows `oa.designer.errStageInvalid` toast before save is sent.

---

### Scenario 2 — Happy-path: Submit → advance all 4 runtime stages → Approved

**Steps:**

1. **Login** as `qa_s_start` (password `123456`).
2. **Submit** `POST /api/wf/flow/submit` `{ "flowKey":"serial-demo", "varsJson":"{}", "bizType":null, "bizId":null }`.
   - **Expected:** HTTP 200, `data.instanceId` = `<inst>`.
3. **Verify stage 0 pending:**
   - Login as `qa_s_s0`.
   - GET `/api/oa/inbox/pending` — expect 1 item for `<inst>`, `stageIndex=0`, `stageRound=0`, `stageName="Stage 0 - Initial"`.
   - DB: `SELECT StageIndex, StageRound, Status FROM Wf_FlowTask WHERE InstanceId='<inst>'` → `0, 0, 0(Pending)`.
   - DB: `SELECT StagePlanJson FROM Wf_FlowToken WHERE InstanceId='<inst>'` → JSON array of 4 RuntimeApprovalStage entries, frozen at submit time.
4. **Approve stage 0:** `POST /api/wf/task/<taskId>/act` `{ "approve":true, "comment":"Approve S0" }`.
   - **Expected:** 200, no error.
5. **Verify stage 1 pending (first managerChain level):**
   - Login as `qa_s_mgr1`.
   - GET `/api/oa/inbox/pending` → `stageIndex=1`, `stageRound=0`, `stageName="Stage 1-2 - Manager Chain"`, `canSendBackPrevStage=true`.
   - DB: `Wf_FlowTask` for this inst — `StageIndex=1, StageRound=0, Status=0(Pending)`.
6. **Approve stage 1:** POST act `{ "approve":true }` as `qa_s_mgr1`.
7. **Verify stage 2 pending (second managerChain level):**
   - Login as `qa_s_mgr2`.
   - GET `/api/oa/inbox/pending` → `stageIndex=2`, `stageRound=0`.
8. **Approve stage 2** as `qa_s_mgr2`.
9. **Verify stage 3 pending:**
   - Login as `qa_s_gm`.
   - GET `/api/oa/inbox/pending` → `stageIndex=3`, `stageRound=0`, `stageName="Stage 3 - GM Approval"`, `canSendBackPrevStage=true`.
10. **Approve stage 3** as `qa_s_gm`.
    - **Expected:** instance status → `Approved (1)`.
11. **DB assertions:**
    ```sql
    -- Instance Approved
    SELECT Status FROM Wf_FlowInstance WHERE Id = '<inst>';
    -- Expected: 1

    -- All tokens Consumed
    SELECT Status, COUNT(*) FROM Wf_FlowToken WHERE InstanceId = '<inst>' GROUP BY Status;
    -- Expected: only Status=1 (Consumed)

    -- FormTo timeline: 4 Approved rows, one per stage, StageIndex 0..3, StageRound 0
    SELECT StageIndex, StageRound, Status FROM Wf_FlowFormTo
    WHERE InstanceId = '<inst>' ORDER BY StageIndex;
    -- Expected: 4 rows, Status=1 (Approved), StageRound=0 for each

    -- StagePlanJson frozen (unchanged after submission)
    SELECT LEN(StagePlanJson), StagePlanJson FROM Wf_FlowToken WHERE InstanceId = '<inst>';
    -- Expected: non-null, contains 4 entries
    ```

---

### Scenario 3 — 串中带并: countersign `any` within a stage

**Setup:** Create a `serial-demo-cs` flow (via designer save) with stage 0 using `countersign:"any"` and `approverRoleId` pointing to a role that has both `qa_s_s0` and `qa_s_mgr1` as members. (Alternatively use two fixed Specified tasks by testing the multi-person same-role path.)

**Steps:**

1. Submit `serial-demo-cs` as `qa_s_start`.
2. Stage 0 generates tasks for both `qa_s_s0` AND the second approver in the role.
3. `qa_s_s0` approves → with `countersign="any"`, stage 0 immediately advances (the other task gets `Cancelled`).
4. **Expected:** only 1 of the 2 stage-0 tasks reaches `Status=1 (Approved)`; the other is `Status=3 (Cancelled)`; instance advances to stage 1.
5. Complete remaining stages and confirm instance reaches Approved.

**DB check:**
```sql
SELECT Status, COUNT(*) FROM Wf_FlowTask
WHERE InstanceId = '<inst>' AND StageIndex = 0
GROUP BY Status;
-- Expected: one Approved(1), one Cancelled(3)
```

**Note:** If the test tenant has no suitable role configured, substitute a two-person Specified countersign by temporarily pointing stage 0 to a parallel approver pair; this scenario is primarily about verifying countersign="any" short-circuits within a serial stage.

---

### Scenario 4 — 退回上一档 (prevStage send-back): repeat twice, tally isolation

**This is the most important serial-signing regression scenario (R12).**

**Steps:**

1. Login as `qa_s_start`, submit `serial-demo` → `<inst4>`.
2. As `qa_s_s0`: approve stage 0 → advances to stage 1.
3. As `qa_s_mgr1`: approve stage 1 → advances to stage 2.
4. As `qa_s_mgr2` (now at stage 2, round 0): **send back to stage 1 (prevStage)**:
   ```json
   POST /api/oa/inbox/sendback
   { "taskId": "<task_at_s2>", "kind": "prevStage", "nodeId": null, "comment": "SB round 1" }
   ```
   **Expected:** HTTP 200.
5. **Verify stage 1 reappears:**
   - Login as `qa_s_mgr1`.
   - GET `/api/oa/inbox/pending` → `stageIndex=1`, **`stageRound=1`** (incremented), `canSendBackPrevStage=true`.
   - DB: Old round-0 task for `qa_s_mgr1` has `Status=3 (Cancelled)`; old FormTo row has `Status=7 (SentBack)`. New round-1 task has `Status=0 (Pending)`.
6. **Approve stage 1 round 1** as `qa_s_mgr1` → advances to stage 2.
7. As `qa_s_mgr2` (stage 2, round 1): **send back again**:
   ```json
   POST /api/oa/inbox/sendback
   { "taskId": "<task_at_s2_r1>", "kind": "prevStage", "nodeId": null, "comment": "SB round 2" }
   ```
8. **Verify stage 1 reappears at round 2:**
   - Login as `qa_s_mgr1`.
   - Pending: `stageIndex=1`, **`stageRound=2`**.
9. **Approve stage 1 round 2**, then approve stage 2, then approve stage 3 (as `qa_s_gm`) → instance Approved.
10. **DB tally assertion (R12 — three independent tallies):**
    ```sql
    SELECT StageIndex, StageRound, Status, COUNT(*) AS n
    FROM Wf_FlowTask
    WHERE InstanceId = '<inst4>'
    GROUP BY StageIndex, StageRound, Status
    ORDER BY StageIndex, StageRound;
    ```
    Expected rows:
    - `(1, 0, Approved=1)` → 1  (mgr1 round-0 approved)
    - `(1, 1, Approved=1)` → 1  (mgr1 round-1 approved)
    - `(1, 2, Approved=1)` → 1  (mgr1 round-2 approved)
    - `(2, 0, SentBack/Cancelled)` + `(2, 1, SentBack/Cancelled)` for the two send-backs
    - `(2, 2, Approved=1)` → 1  (final stage-2 approval)

    ```sql
    SELECT StageIndex, StageRound, Status
    FROM Wf_FlowFormTo
    WHERE InstanceId = '<inst4>'
    ORDER BY StageIndex, StageRound, StepSeq;
    -- Stage 1 should have 3 FormTo rows (one per round), each with its own StageRound
    -- Stage-2 send-back rows should have Status=7 (SentBack)
    ```

    **Key invariant (R12):** Each (StageIndex, StageRound) pair is a completely independent tally scope. The index `IX_Wf_FlowTask_Tally` on `(InstanceId, NodeId, TokenId, StageIndex, StageRound, Status)` ensures this.

---

### Scenario 5 — 退回发起人 (starter): instance goes to Draft; resubmit re-runs from stage 0

**Steps:**

1. Submit `serial-demo` as `qa_s_start` → `<inst5>`.
2. As `qa_s_s0`: approve stage 0 → stage 1 active.
3. As `qa_s_mgr1` (stage 1, pending): **send back to starter**:
   ```json
   POST /api/oa/inbox/sendback
   { "taskId": "<task_at_s1>", "kind": "starter", "nodeId": null, "comment": "Back to initiator" }
   ```
   **Expected:** HTTP 200.
4. **Verify instance is Draft:**
   ```sql
   SELECT Status FROM Wf_FlowInstance WHERE Id = '<inst5>';
   -- Expected: 5 (Draft)

   -- All tokens Cancelled
   SELECT Status, COUNT(*) FROM Wf_FlowToken WHERE InstanceId = '<inst5>' GROUP BY Status;
   -- Expected: Status=2 (Cancelled) only (or all Consumed/Cancelled, none Active)

   -- All pending FormTo rows Voided
   SELECT Status, COUNT(*) FROM Wf_FlowFormTo WHERE InstanceId = '<inst5>' GROUP BY Status;
   -- Expected: no Pending(0) rows; prior Approved(1) rows for stage 0 preserved; Pending rows now Voided(6)
   ```
5. **`qa_s_mgr1` inbox:** GET `/api/oa/inbox/pending` → empty (task cancelled).
6. **`qa_s_start` draft list:** GET `/api/oa/draft/list` → `<inst5>` appears with `Status=5 (Draft)`.
7. **Resubmit** as `qa_s_start`:
   ```json
   POST /api/oa/draft/submit
   { "id": "<inst5>" }
   ```
   **Expected:** HTTP 200, no error.
8. **Verify stage 0 active again:**
   - Login as `qa_s_s0`.
   - GET `/api/oa/inbox/pending` → `<inst5>` at `stageIndex=0`, `stageRound=0` (fresh round, clean tally).
   - DB: `Wf_FlowInstance.Status = 0 (Running)`.
9. Complete full approval chain → `<inst5>` reaches Approved.

---

### Scenario 6 — 退回到指定节点 (node send-back): valid + boundary errors

**Setup:** Create a 2-node serial flow `serial-two` in the designer:
`start → approval_A (1 stage, Specified=qa_s_s0) → approval_B (3 stages serial) → end`.

**Steps:**

1. Submit `serial-two` as `qa_s_start` → advances to `approval_A`.
2. As `qa_s_s0`: approve `approval_A` → advances to `approval_B` stage 0.
3. As `qa_s_mgr1` (stage 0 of B): **valid node send-back to `approval_A`**:
   ```json
   POST /api/oa/inbox/sendback
   { "taskId": "<task>", "kind": "node", "nodeId": "approval_A", "comment": "back to A" }
   ```
   **Expected:** HTTP 200; `approval_A` reactivated.
4. **Invalid: send back to self (nodeId = current node)**:
   ```json
   { "taskId": "<task>", "kind": "node", "nodeId": "approval_B" }
   ```
   **Expected:** HTTP 400, `message` = `"E-WF-012"`.
5. **Invalid: send back to `end`**:
   ```json
   { "taskId": "<task>", "kind": "node", "nodeId": "end" }
   ```
   **Expected:** HTTP 400, `message` = `"E-WF-012"`.
6. **Invalid: send back downstream (non-upstream node)**:
   Use a 3-node flow where C is downstream of B. From B, send-back to C.
   **Expected:** HTTP 400, `message` = `"E-WF-012"`.
7. **Invalid: cross-parallel block:**
   Use `start → split → [B1, B2] → join → C`. From C, send-back to B1.
   **Expected:** HTTP 400, `message` = `"E-WF-012"` (CrossesParallelBlock guard).
8. **Invalid kind string:**
   ```json
   { "taskId": "<task>", "kind": "badkind" }
   ```
   **Expected:** HTTP 400, `message` = `"E-WF-012"`.

---

### Scenario 7 — 主管链冻结 (R13): managerChain org-chart change after submission

**Precondition:** `qa_s_start.ManagerId = qa_s_mgr1`, `qa_s_mgr1.ManagerId = qa_s_mgr2` (set by seed.sql).

**Steps:**

1. Submit `serial-demo` as `qa_s_start` → `<inst7>`.
2. **DB: record the frozen plan immediately after submit:**
   ```sql
   SELECT StagePlanJson FROM Wf_FlowToken WHERE InstanceId = '<inst7>' AND Status = 0;
   -- Expected: JSON with 4 RuntimeApprovalStage entries; chain entries point to mgr1 (level 1) and mgr2 (level 2)
   ```
3. **Approve stage 0** as `qa_s_s0`.
4. **BEFORE stage 1 acts: change the org chart.** Point `qa_s_start.ManagerId` to `qa_s_newmgr` (breaking the old chain):
   ```sql
   UPDATE Sys_Users SET ManagerId = 'AAAA0000-0000-0000-0000-0000000000F0'  -- qa_s_newmgr
   WHERE Id = 'AAAA0000-0000-0000-0000-0000000000B0';  -- qa_s_start
   ```
5. **Verify stage 1 still assigned to `qa_s_mgr1`** (frozen plan, not re-resolved):
   - Login as `qa_s_mgr1`.
   - GET `/api/oa/inbox/pending` → `<inst7>` at `stageIndex=1`. `qa_s_mgr1` sees the task. `qa_s_newmgr` does NOT see a task for `<inst7>`.
   - DB: `SELECT AssigneeId FROM Wf_FlowTask WHERE InstanceId='<inst7>' AND StageIndex=1` → `qa_s_mgr1`'s GUID.
6. **Continue to completion:** approve stages 1, 2, 3 via their frozen assignees.
   - Stage 2 still assigned to `qa_s_mgr2` (frozen at submit, org change doesn't drift it to `qa_s_newmgr`'s chain).
7. **Also verify subsequent fixed stages (stage 3 = `qa_s_gm`) don't drift:**
   - Approve stage 3 as `qa_s_gm` → instance Approved.
8. **Restore org chart** (cleanup):
   ```sql
   UPDATE Sys_Users SET ManagerId = 'AAAA0000-0000-0000-0000-0000000000C0'  -- restore to qa_s_mgr1
   WHERE Id = 'AAAA0000-0000-0000-0000-0000000000B0';
   ```

**Key assertion:** The same `StagePlanJson` from step 2 should be identical at the end of the flow (no mutation). The plan is written once when the token is spawned into the approval node and never updated.

---

## 3. Inbox / timeline visual checks

Open `http://localhost:5180/oa/detail/<instanceId>` after a multi-stage flow completes.

- **Timeline** (FlowTimeline.vue) groups rows by `(stageIndex, stageRound)` with header `第 K+1 档 · 第 R+1 轮` (1-indexed display).
- **SentBack rows** (Status=7) render the `oa.timeline.sentBack` tag (`已退回`) in the timeline grouping for that round.
- **FormDetail send-back selector** (SendBackDialog.vue): when `canSendBackPrevStage=true` in the pending item, the selector includes option `oa.detail.sendback.prevStage` (`退回上一档`). For stage 0 tasks (`stageIndex=0`), `canSendBackPrevStage=false` — the prevStage option must NOT appear.
- **Forecast panel**: shows future stages with `stageIndex` and `stageName` from ForecastService.
- **Draft state** (after starter send-back): the instance disappears from all other users' pending/running views; appears in `qa_s_start`'s draft list.

---

## 4. i18n check (five languages)

Switch language in the frontend settings and verify:

| Key | ZH-CN | ZH-TW | EN | JA | KO |
|:----|:------|:------|:---|:---|:---|
| `oa.designer.stagesSection` | 串簽档位 | 串簽檔位 | Serial Stages | 直列承認段階 | 직렬 결재 단계 |
| `oa.designer.stage.kind.managerChain` | 逐级 | 逐級 | Manager Chain | 上長連鎖 | 단계별 상사 |
| `oa.designer.stage.maxLevels` | 最大级数 | 最大級數 | Max Levels | 最大階層 | 최대 레벨 |
| `oa.detail.sendback.prevStage` | 退回上一档 | 退回上一檔 | To Previous Stage | 前の段階へ差し戻し | 이전 단계로 반려 |
| `oa.detail.sendback.starter` | 退回发起人 | 退回發起人 | To Initiator | 申請者へ差し戻し | 신청자에게 반려 |
| `oa.detail.sendback.node` | 退回到指定节点 | 退回到指定節點 | To Specific Node | 指定ノードへ差し戻し | 지정 노드로 반려 |
| `oa.timeline.sentBack` | 已退回 | 已退回 | Sent Back | 差し戻し | 반려됨 |
| `oa.designer.errStageInvalid` | 串簽档配置不完整 | 串簽檔配置不完整 | Serial stage config incomplete | 直列段階の設定が不完全です | 직렬 단계 설정이 불완전합니다 |
| `E-WF-011` | 串簽档配置非法 | 串簽檔配置非法 | Invalid serial-stage configuration | 直列段階の設定が不正です | 직렬 단계 설정이 잘못되었습니다 |
| `E-WF-012` | 退回目标非法 | 退回目標非法 | Invalid send-back target | 差し戻し先が不正です | 반려 대상이 잘못되었습니다 |
| `E-WF-013` | 该关卡审批人缺失，流程已挂起待指派 | 該關卡審批人缺失，流程已掛起待指派 | Stage approver missing; instance suspended for assignment | 段階の承認者が見つからず、案件は割当待ちで保留されました | 단계 승인자가 없어 결재가 보류되었습니다 |

**Trigger E-WF-013 deliberately:** Submit `serial-demo` after setting `qa_s_mgr1.ManagerId = NULL` (so managerChain resolves to nobody). The engine should set `Wf_FlowInstance.Status = 4 (Suspended)` and log `"E-WF-013"` in `Wf_FlowHistory`. The frontend should show the suspended state in the running/detail view.

---

## 5. Gotchas (carried forward + new)

1. **`SET QUOTED_IDENTIFIER ON` for `Wf_FlowDef` inserts:** `Wf_FlowDef` has filtered unique indexes; SQL Server requires `QUOTED_IDENTIFIER ON` when creating/modifying filtered indexes and also when inserting into tables with such indexes via sqlcmd. `seed.sql` sets this at the top. Always run seed.sql with `sqlcmd -i` from PowerShell/cmd, not from git-bash MSYS.

2. **git-bash (MSYS) mangles JSON argv for sqlcmd:** MSYS backslash-escapes embedded `"` in command-line arguments, corrupting JSON literals passed via `-Q`. Always use `sqlcmd -i seed.sql` (file input) or use `REPLACE(..., '~', CHAR(34))` tilde trick for inline `-Q` invocations.

3. **PS5.1 `Invoke-RestMethod` 400 body:** PS5.1 throws `System.Net.WebException` for non-2xx. To read the error body:
   ```powershell
   try {
       $r = Invoke-RestMethod ...
   } catch {
       $stream = $_.Exception.Response.GetResponseStream()
       $reader = New-Object System.IO.StreamReader($stream)
       $errBody = $reader.ReadToEnd() | ConvertFrom-Json
       Write-Host "Error: $($errBody.message)"
   }
   ```

4. **Envelope shape:** all responses wrap data as `{ code: 0, message: "OK", data: ... }`. The instance id is at `$r.data.instanceId` (not `$r.data.data.id`). The pending list is at `$r.data` (array). Adjust per actual response shape if controllers deviate.

5. **ASCII-only data in PS5.1 scripts:** PS5.1 default encoding is UTF-16 LE. JSON body strings with Chinese characters may fail. Use ASCII comment text and ASCII-only field values in body JSON (e.g., comment `"Approve S0"` not `"同意 S0"`). The `qa_serial.ps1` script uses ASCII-only values throughout.

6. **CSRF disabled in dev:** `appsettings.Development.json` sets `Security:Csrf:Enabled=false`. No `X-CSRF-Token` header needed in the HTTP e2e script. Do not set it.

7. **RabbitMQ / Kafka not required:** If message brokers are not running locally, the backend logs a graceful-degrade warning. Flow notifications use `NullWfNotifier` silently. No impact on engine/read-model.

8. **EF localId exclusion for `.Any()` with change tracker:** When checking `Wf_FlowTask` active counts inside a transaction, the engine uses `localIds` exclusion to prevent counting newly-added (uncommitted) tasks. This is an existing engine-level fix; no QA action needed.

9. **prevStage from stage 0 → E-WF-012:** `SendBackToPrevStageAsync` checks `task.StageIndex <= 0` and throws `E-WF-012`. Verify the frontend hides the prevStage option (via `canSendBackPrevStage=false` in `InboxPendingItem`) before reaching the backend.

10. **StagePlanJson isolation across instances:** Two concurrent `serial-demo` instances share the same `FlowDef` SchemaJson but each token carries its own `StagePlanJson` snapshot. An org-chart change between submissions of instance A and B only affects the freshly-computed plan for B, not A's frozen plan.

---

## 6. Files in this folder

| File | Purpose |
|:-----|:--------|
| `README.md` | This runbook |
| `seed.sql` | Idempotent T-SQL: users + FormDef + FlowDef for serial-demo |
| `qa_serial.ps1` | PowerShell HTTP e2e for scenarios 2, 4, 5 (run after backend is up) |
