# WFS Approver Resolution (Advanced Strategies) -- T18 QA Runbook

**Branch:** `feat/wfs-approver-resolve`
**Date:** 2026-06-28
**Feature scope:** IApproverResolver advanced strategies: FormField (③) / DataMap (②b) / Group (①) / When gate (②a) / Filter candidate filter (②a) + Wf_ApproverMap maintenance page (E-WF-015) + DynamicForm user picker + Forecast concrete approver names.

---

## 1. Environment setup

### 1.1 Isolated database

Reuse the `CP6DB_OA` database already created for the serial-signing QA session, OR create a fresh one:

```sql
-- Run as sysadmin in SSMS or sqlcmd
CREATE DATABASE CP6DB_OA;
```

Point the backend at this database so QA data never touches the live `CP6DB`.

### 1.2 Connection string override

Create `CP6.WebApi/appsettings.Local.json` (gitignored) before starting the backend:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

Or via PowerShell before `dotnet run`:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
```

### 1.3 Backend port

The Space session occupies `http://localhost:5177`; the serial-signing session occupies `http://localhost:5178`. Start this QA backend on **port 5179**:

```powershell
cd D:\CP6-wfs-approver\CP6.WebApi
$env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet run --urls "http://localhost:5179"
```

On first boot, `db.Database.Migrate()` automatically applies all EF migrations including `WfsApproverMap` which adds:
- `Wf_ApproverMap` table (MapKey / MatchValue / ApproverUserId / ApproverRoleId / OrderNo / Enable)
- Index `IX_Wf_ApproverMap_Lookup` on `(TenantId, MapKey, MatchValue)`

Confirm migrations applied:

```sql
SELECT MigrationId FROM CP6DB_OA.dbo.__EFMigrationsHistory
WHERE MigrationId LIKE '%WfsApproverMap%';
-- Expected: 1 row
```

### 1.4 Frontend port

The Space session occupies `http://localhost:5173`; the serial-signing session occupies `http://localhost:5180`. Start the OA frontend on **port 5181**:

```powershell
cd D:\CP6-wfs-approver\cp6.web
npm run dev -- --port 5181
```

Update `vite.config.ts` proxy target to `http://localhost:5179` if needed.

### 1.5 Apply the QA seed

After the backend has booted and migrations have run, apply the seed from a **native PowerShell or cmd** window (not git-bash):

```powershell
sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i "D:\CP6-wfs-approver\docs\superpowers\qa\wfs-approver-resolution\seed.sql"
```

The seed creates:
- 7 users: `qa_a_admin` / `qa_a_start` / `qa_a_user1` / `qa_a_user2` / `qa_a_same_dept` / `qa_a_other_dept` / `qa_a_mgr`
- 1 dept: shared department GUID for same-dept filter scenario
- 3 FormDefs: `approver-field-form` / `datamap-form` / `group-form`
- 6 FlowDefs: `approver-formfield-flow` / `approver-datamap-flow` / `approver-when-flow` / `approver-filter-flow` / `approver-group-flow` / `approver-forecast-flow`
- 2 Wf_ApproverMap rows: `cc/A100 -> qa_a_user1 (user)` + `cc/A100 -> role 9 (role)`

---

## 2. Scenarios

All scenarios run at `http://localhost:5179` (backend) + `http://localhost:5181` (frontend).
All seeded users have password `123456`.

### Seeded user layout

| UserName | NickName | Role | DeptId | ManagerId | Purpose |
|:---------|:---------|:-----|:-------|:----------|:--------|
| `qa_a_admin` | QA Admin | admin role | - | - | designer/maintenance login |
| `qa_a_start` | QA Starter | - | dept_A | qa_a_mgr | flow submitter |
| `qa_a_user1` | QA User1 | role 9 | dept_A | - | FormField target / DataMap user target |
| `qa_a_user2` | QA User2 | - | dept_B | - | Group specified target |
| `qa_a_same_dept` | QA Same Dept | role 7 | dept_A | - | Filter scenario: passes same-dept filter |
| `qa_a_other_dept` | QA Other Dept | role 7 | dept_B | - | Filter scenario: excluded by same-dept filter |
| `qa_a_mgr` | QA Manager | - | dept_A | - | DirectManager of qa_a_start (Group scenario) |

---

### Scenario 1 -- Designer save: FormField node flow validates

**Precondition:** Logged in as `qa_a_admin`.

**Steps:**

1. Open the flow designer at `/oa/designer` in the frontend.
2. **Happy path -- FormField node:**
   POST `http://localhost:5179/api/oa/designer/save` with:
   ```json
   {
     "flowKey": "qa-ff-test",
     "flowName": "QA FormField Test",
     "formKey": "approver-field-form",
     "functionId": null,
     "flowCode": null,
     "schemaJson": "{\"start\":\"s\",\"nodes\":[{\"id\":\"s\",\"type\":\"start\",\"name\":\"Start\"},{\"id\":\"a1\",\"type\":\"approval\",\"name\":\"FormField Approval\",\"approverStrategy\":\"FormField\",\"approverFieldName\":\"approver\",\"countersign\":\"all\"},{\"id\":\"end\",\"type\":\"end\",\"name\":\"End\"}],\"edges\":[{\"from\":\"s\",\"to\":\"a1\"},{\"from\":\"a1\",\"to\":\"end\"}]}"
   }
   ```
   **Expected:** HTTP 200, `{ code: 0, data: true }`.

3. **Negative -- FormField node missing fieldName (E-WF-014):**
   POST `/api/oa/designer/save` with a `schemaJson` whose approval node has `"approverStrategy":"FormField"` but no `"approverFieldName"` (omitted or empty).
   **Expected:** HTTP 400, `message` contains `"E-WF-014"`.

4. **Negative -- DataMap node missing mapKey (E-WF-014):**
   POST `/api/oa/designer/save` with `"approverStrategy":"DataMap"` but no `"approverMapKey"`.
   **Expected:** HTTP 400, `message` contains `"E-WF-014"`.

5. **Negative -- Group node with empty members (E-WF-014):**
   POST `/api/oa/designer/save` with `"approverStrategy":"Group"` and `"approverMembers":[]`.
   **Expected:** HTTP 400, `message` contains `"E-WF-014"`.

**Verify in designer UI:** The NodePropertyPanel strategy dropdown shows three new options: `oa.designer.strategy.formField` / `oa.designer.strategy.dataMap` / `oa.designer.strategy.group`. Selecting FormField reveals a field-name input. Selecting Group reveals the member list editor. Invalid config shows `oa.designer.errApproverConfig` toast before save is sent.

---

### Scenario 2 -- Maintenance page: ApproverMap CRUD + E-WF-015 duplicate block

**Precondition:** Logged in as `qa_a_admin`.

**Steps:**

1. Open `/oa/approver-map` in the frontend. The maintenance table loads (empty or with seed rows).

2. **Seed row verification via API:**
   GET `http://localhost:5179/api/oa/approver-map?mapKey=cc`
   **Expected:** HTTP 200, `data` array contains 2 rows:
   - Row 1: `mapKey=cc, matchValue=A100, approverUserId=<qa_a_user1 guid>, approverRoleId=null`
   - Row 2: `mapKey=cc, matchValue=A100, approverUserId=null, approverRoleId=9`

3. **Add a new mapping:**
   POST `http://localhost:5179/api/oa/approver-map` with `{ "mapKey":"cc", "matchValue":"B200", "approverUserId":"<qa_a_user2 guid>", "approverRoleId":null }`.
   **Expected:** HTTP 200, new row returned with `id`.

4. **E-WF-015 -- duplicate same target:**
   POST `/api/oa/approver-map` with `{ "mapKey":"cc", "matchValue":"A100", "approverUserId":"<qa_a_user1 guid>", "approverRoleId":null }` (exact duplicate of seed row 1).
   **Expected:** HTTP 400, `message` contains `"E-WF-015"`.

5. **E-WF-015 -- both targets null:**
   POST `/api/oa/approver-map` with `{ "mapKey":"cc", "matchValue":"A100", "approverUserId":null, "approverRoleId":null }`.
   **Expected:** HTTP 400, `message` contains `"E-WF-015"`.

6. **Delete the B200 row:**
   DELETE `http://localhost:5179/api/oa/approver-map/<id from step 3>`.
   **Expected:** HTTP 200.
   GET `/api/oa/approver-map?mapKey=cc` again -- B200 row gone; still 2 rows.

**Verify in UI:** The maintenance page table shows the two `cc/A100` seed rows. Adding a duplicate in the form shows the E-WF-015 error toast. The distinct-key dropdown (if present) shows `cc` as an option.

---

### Scenario 3 -- FormField: fill form (user picker) → submit → FormField node assigns that user

**Precondition:** `approver-formfield-flow` seeded (FormField node with `fieldName=approver`).
Login as `qa_a_start`.

**Steps:**

1. **Fill and submit the form with approver=qa_a_user1:**
   POST `http://localhost:5179/api/wf/flow/submit` with:
   ```json
   {
     "flowKey": "approver-formfield-flow",
     "varsJson": "{\"subject\":\"FormField Test\",\"approver\":\"<qa_a_user1 guid>\"}",
     "bizType": null,
     "bizId": null
   }
   ```
   **Expected:** HTTP 200, `data.instanceId` = `<inst3>`.

2. **Verify qa_a_user1 has the pending task:**
   Login as `qa_a_user1`.
   GET `http://localhost:5179/api/oa/inbox/pending`
   **Expected:** Array contains an item where `instanceId == <inst3>`. The assignee is `qa_a_user1`.

3. **Verify qa_a_user2 does NOT have a pending task for this instance:**
   Login as `qa_a_user2`.
   GET `/api/oa/inbox/pending` -- no item for `<inst3>`.
   **Expected:** Count of items matching `<inst3>` is 0.

4. **Approve and verify completion:**
   Login as `qa_a_user1`. POST `http://localhost:5179/api/wf/task/<taskId>/act` `{ "approve":true, "comment":"FF approved" }`.
   **Expected:** HTTP 200. Instance reaches `Approved (1)`.

5. **Verify in DynamicForm UI (frontend):**
   Open `/oa/form/approver-formfield-flow` (or the FormInitiate route).
   The form renders a `user` type field for `approver` with a remote-search selector (not a plain text input). Select `qa_a_user1` and submit.
   **Expected:** The form POSTs with `varsJson` containing the selected user GUID.

---

### Scenario 4 -- DataMap: costCenter=A100 assigns user1 + role9 members

**Precondition:** `approver-datamap-flow` seeded (DataMap node with `fieldName=costCenter, mapKey=cc`).
Wf_ApproverMap seed: `cc/A100 -> qa_a_user1 (user)` + `cc/A100 -> role 9 (role, members: qa_a_user1)`.
Login as `qa_a_start`.

**Steps:**

1. **Submit with costCenter=A100:**
   POST `http://localhost:5179/api/wf/flow/submit` with:
   ```json
   {
     "flowKey": "approver-datamap-flow",
     "varsJson": "{\"costCenter\":\"A100\"}",
     "bizType": null,
     "bizId": null
   }
   ```
   **Expected:** HTTP 200, `data.instanceId` = `<inst4>`.

2. **Verify qa_a_user1 has a pending task** (appears both as direct user AND role-9 expansion):
   Login as `qa_a_user1`.
   GET `/api/oa/inbox/pending` -- item for `<inst4>` found.
   **Expected:** At least one pending task for `<inst4>`.

3. **Submit with costCenter=ZZZ (no mapping):**
   POST with `varsJson` containing `costCenter=ZZZ`.
   **Expected:** Either HTTP 400 / E-WF-013 (suspended with missing approver), OR the instance lands in `Suspended (4)` status with no pending tasks.
   Check: GET `/api/oa/inbox/detail/<instZZZ>` -- `instance.status == 4`.

4. **Approve and verify:**
   Login as `qa_a_user1`. Act on `<inst4>` task with `approve=true`.
   **Expected:** HTTP 200. Instance moves to Approved.

---

### Scenario 5 -- When gate: amount=50000 triggers extra approval stage; amount=100 skips it

**Precondition:** `approver-when-flow` seeded. This flow has two approval nodes:
- Node A1: no When condition -- always active. Specified approver = `qa_a_user1`.
- Node A2: `When = "amount >= 10000"` -- only active when amount threshold met. Specified approver = `qa_a_user2`.

Flow schema: `start -> a1 -> a2 -> end` where A2 has the When gate.

Login as `qa_a_start`.

**Steps:**

**Sub-scenario 5a: amount=50000 (When=true, both nodes active):**

1. Submit:
   ```json
   { "flowKey": "approver-when-flow", "varsJson": "{\"amount\":50000}", "bizType":null, "bizId":null }
   ```
   **Expected:** HTTP 200, `<inst5a>`.

2. `qa_a_user1` approves node A1.
3. `qa_a_user2` receives pending task for `<inst5a>` (A2 When condition satisfied).
   GET `/api/oa/inbox/pending` as `qa_a_user2` -- item found.
4. `qa_a_user2` approves -- instance reaches Approved.

**Sub-scenario 5b: amount=100 (When=false, A2 skipped):**

1. Submit:
   ```json
   { "flowKey": "approver-when-flow", "varsJson": "{\"amount\":100}", "bizType":null, "bizId":null }
   ```
   **Expected:** HTTP 200, `<inst5b>`.

2. `qa_a_user1` approves node A1.
3. Verify `qa_a_user2` has NO pending task for `<inst5b>` (A2 When=false → skipped).
   GET `/api/oa/inbox/pending` as `qa_a_user2` -- count of items for `<inst5b>` is 0.
4. Verify instance is already `Approved (1)` after A1 approval (A2 gate bypassed → flow ends).
   GET `/api/oa/inbox/detail/<inst5b>` -- `instance.status == 1`.

---

### Scenario 6 -- Filter: Role strategy + `user.deptId == starter.deptId` → only same-dept members get tasks

**Precondition:** `approver-filter-flow` seeded. Approval node: `approverStrategy=Role, approverRoleId=7, approverFilter="user.deptId == starter.deptId"`.
Seeded users: `qa_a_same_dept` (dept_A, role 7) + `qa_a_other_dept` (dept_B, role 7).
`qa_a_start` is in dept_A.
Login as `qa_a_start`.

**Steps:**

1. **Submit:**
   POST `http://localhost:5179/api/wf/flow/submit` with:
   ```json
   { "flowKey": "approver-filter-flow", "varsJson": "{}", "bizType":null, "bizId":null }
   ```
   **Expected:** HTTP 200, `<inst6>`.

2. **Verify qa_a_same_dept (dept_A, role 7) has pending task:**
   Login as `qa_a_same_dept`.
   GET `/api/oa/inbox/pending` -- item for `<inst6>` found.
   **Expected:** 1 item.

3. **Verify qa_a_other_dept (dept_B, role 7) does NOT have a pending task:**
   Login as `qa_a_other_dept`.
   GET `/api/oa/inbox/pending` -- no item for `<inst6>`.
   **Expected:** Count = 0.

4. **Approve as qa_a_same_dept:**
   POST `/api/wf/task/<taskId>/act` `{ "approve":true, "comment":"Filter approved" }`.
   **Expected:** HTTP 200. Instance Approved.

---

### Scenario 7 -- Group: mixed DirectManager + Specified → merged dedup countersign

**Precondition:** `approver-group-flow` seeded. Approval node: `approverStrategy=Group, approverMembers=[{Strategy:DirectManager, Levels:1}, {Strategy:Specified, approverUserId:<qa_a_mgr guid>}]`.
`qa_a_start.ManagerId = qa_a_mgr` (both DirectManager L1 and Specified resolve to qa_a_mgr → dedup = single approver).
Login as `qa_a_start`.

**Steps:**

1. **Submit:**
   POST `http://localhost:5179/api/wf/flow/submit` with:
   ```json
   { "flowKey": "approver-group-flow", "varsJson": "{}", "bizType":null, "bizId":null }
   ```
   **Expected:** HTTP 200, `<inst7>`.

2. **Verify qa_a_mgr has exactly ONE pending task** (dedup: DirectManager L1 + Specified both resolve to qa_a_mgr):
   Login as `qa_a_mgr`.
   GET `/api/oa/inbox/pending` -- items for `<inst7>`.
   **Expected:** Exactly 1 task (not 2, dedup applied).

3. **Verify qa_a_user2 has NO pending task** (not in the Group members):
   Login as `qa_a_user2`.
   GET `/api/oa/inbox/pending` -- no item for `<inst7>`.

4. **Approve as qa_a_mgr:**
   POST `/api/wf/task/<taskId>/act` `{ "approve":true }`.
   **Expected:** HTTP 200. Instance Approved.

---

### Forecast check (all scenarios): FormInitiate shows concrete approver names

After filling the form (before submit), the frontend calls `POST /api/oa/forecast` with the current `varsJson`.

**Check for FormField flow:** Fill in `approver=<qa_a_user1 guid>` in the form. The forecast panel should show `qa_a_user1` (or NickName "QA User1") as the approver for the FormField node -- not "TBD" or empty.

**Check for DataMap flow:** Fill in `costCenter=A100`. Forecast should show `qa_a_user1` (+ role-9 members) as candidates.

**Check for Group flow:** Without any form data, forecast for `approver-group-flow` should list `qa_a_mgr` (DirectManager L1 of `qa_a_start`).

**Check for Filter flow:** Forecast for `approver-filter-flow` started by `qa_a_start` (dept_A) should list `qa_a_same_dept` only (dept_B member filtered out).

API call shape:
```json
POST http://localhost:5179/api/oa/forecast
{
  "flowKey": "approver-formfield-flow",
  "varsJson": "{\"approver\":\"<qa_a_user1 guid>\"}"
}
```
**Expected:** `data.stages[0].approverNames` contains "QA User1" (or equivalent display name).

---

## 3. Visual checks (frontend)

Open `http://localhost:5181` in the browser after seeding and starting both backend and frontend.

- **NodePropertyPanel:** Strategy dropdown for an approval node shows: Starter / DirectManager / DeptLeader / Role / Specified / FormField / DataMap / Group.
- **FormField strategy selected:** Shows `approverField` input (field name); `approverWhen` (optional condition); `approverFilter` (optional filter).
- **DataMap strategy selected:** Shows `approverField` (value field) + `approverMapKey` (map name); `approverWhen` optional.
- **Group strategy selected:** Shows `addMember` button; each member row has its own strategy selector.
- **DynamicForm user field:** The `approver` field in `approver-field-form` renders as `el-select` with `filterable remote` (not plain text). Typing a username queries `/api/sys/user/list` and populates options.
- **ApproverMap maintenance page:** Accessible at `/oa/approver-map`. Shows table of (MapKey, MatchValue, Approver User, Approver Role, Enable). CRUD controls for add/edit/delete. E-WF-015 error toast on duplicate submit.
- **Forecast panel:** After filling the form in FormInitiate, the right-side forecast panel (`ForecastPanel.vue`) shows named approvers for FormField/DataMap resolved nodes.

---

## 4. i18n check (five languages)

Switch language in the frontend settings and verify:

| Key | ZH-CN | ZH-TW | EN | JA | KO |
|:----|:------|:------|:---|:---|:---|
| `oa.designer.strategy.formField` | 表单字段指定 | 表單欄位指定 | Form Field | フォーム項目指定 | 양식 필드 지정 |
| `oa.designer.strategy.dataMap` | 数据映射 | 資料映射 | Data Map | データマップ | 데이터 매핑 |
| `oa.designer.strategy.group` | 混合组 | 混合組 | Group | 混合グループ | 혼합 그룹 |
| `oa.designer.approverField` | 审批人字段 | 審批人欄位 | Approver Field | 承認者項目 | 승인자 필드 |
| `oa.designer.approverWhen` | 适用条件 | 適用條件 | When (condition) | 適用条件 | 적용 조건 |
| `oa.designer.approverFilter` | 候选过滤 | 候選過濾 | Candidate Filter | 候補フィルタ | 후보 필터 |
| `oa.designer.member` | 成员 | 成員 | Member | メンバー | 구성원 |
| `oa.designer.addMember` | 加成员 | 加成員 | Add Member | メンバー追加 | 구성원 추가 |
| `oa.approverMap.key` | 映射键 | 映射鍵 | Map Key | マップキー | 매핑 키 |
| `oa.approverMap.matchValue` | 匹配值 | 匹配值 | Match Value | 一致値 | 일치 값 |
| `nav.739` | 审批人映射 | 審批人映射 | Approver Mapping | 承認者マッピング | 승인자 매핑 |
| `E-WF-014` | 审批人高级配置非法 | 審批人進階配置非法 | Invalid advanced approver config | 承認者の詳細設定が不正です | 고급 승인자 구성이 잘못되었습니다 |
| `E-WF-015` | 审批人映射重复或非法 | 審批人映射重複或非法 | Duplicate or invalid approver mapping | 承認者マッピングが重複または不正です | 승인자 매핑이 중복되거나 잘못되었습니다 |

**Trigger E-WF-014 deliberately:** In the designer, save a FormField node without `approverFieldName`. Frontend toast should show `oa.designer.errApproverConfig`; backend returns `E-WF-014`.

**Trigger E-WF-015 deliberately:** In the maintenance page, add a duplicate row for an existing `(mapKey, matchValue, approverUserId)` triple. Backend returns `E-WF-015`.

---

## 5. Gotchas

1. **`SET QUOTED_IDENTIFIER ON` for `Wf_FlowDef` inserts:** `Wf_FlowDef` has filtered unique indexes. SQL Server requires `QUOTED_IDENTIFIER ON` when inserting into tables with such indexes via sqlcmd. `seed.sql` sets this at the top. Always run seed.sql with `sqlcmd -i` from PowerShell/cmd, not from git-bash MSYS.

2. **git-bash (MSYS) mangles JSON in sqlcmd:** Use `sqlcmd -i seed.sql` (file input), never inline `-Q` with embedded quotes.

3. **PS5.1 `Invoke-RestMethod` 400 body:** PS5.1 throws `System.Net.WebException` for non-2xx. Read error body via:
   ```powershell
   $stream = $_.Exception.Response.GetResponseStream()
   $reader = New-Object System.IO.StreamReader($stream)
   $errBody = $reader.ReadToEnd() | ConvertFrom-Json
   ```

4. **Envelope shape:** All responses wrap data as `{ code: 0, message: "OK", data: ... }`. Instance id is at `$r.data.instanceId`. Pending list is at `$r.data` (array).

5. **ASCII-only data in PS5.1 scripts:** Use ASCII-only values in JSON body strings. Comment text like `"FF approved"`, not Chinese characters.

6. **CSRF disabled in dev:** `appsettings.Development.json` has `Security:Csrf:Enabled=false`. No `X-CSRF-Token` header needed.

7. **VarsJson must match FlowNode.ApproverFieldName exactly:** The resolver reads `VarsJson["approver"]` for a `FormField` node configured with `approverFieldName="approver"`. Field names are case-sensitive (JSON property name).

8. **DataMap role expansion:** `Wf_ApproverMap` rows with `ApproverRoleId` expand to all enabled users in that role via `Sys_Users.RoleId`. Ensure the seeded users have the correct `RoleId` column set.

9. **When gate and VarsJson:** The When condition evaluator (`ExpressionEvaluator.Evaluate`) receives `VarsJson` from the instance. `amount=50000` triggers `amount >= 10000`. Numeric comparison requires the field to be stored as a JSON number (not a string) in `varsJson`.

10. **Group dedup:** The Group resolver merges member results with `Distinct()`. If DirectManager L1 and Specified both resolve to the same GUID, exactly one task is created -- not two.

---

## 6. Files in this folder

| File | Purpose |
|:-----|:--------|
| `README.md` | This runbook |
| `seed.sql` | Idempotent T-SQL: users + depts + FormDefs + FlowDefs + Wf_ApproverMap rows |
| `qa_approver.ps1` | PowerShell HTTP e2e skeleton for scenarios 1-7 (run after backend is up) |
