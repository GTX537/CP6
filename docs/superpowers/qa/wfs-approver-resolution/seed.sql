-- =====================================================================
-- WFS Approver Resolution (Advanced Strategies) -- T18 QA seed
-- Branch: feat/wfs-approver-resolve
-- Date:   2026-06-28
--
-- Creates:
--   7 users  : qa_a_admin / qa_a_start / qa_a_user1 / qa_a_user2 /
--              qa_a_same_dept / qa_a_other_dept / qa_a_mgr
--   2 depts  : dept_A (shared by qa_a_start/user1/same_dept/mgr)
--              dept_B (qa_a_user2/other_dept)
--   3 FormDefs: approver-field-form / datamap-form / group-form
--   6 FlowDefs: approver-formfield-flow / approver-datamap-flow /
--               approver-when-flow / approver-filter-flow /
--               approver-group-flow / approver-forecast-flow
--   2 Wf_ApproverMap rows: cc/A100->qa_a_user1 (user) + cc/A100->role9 (role)
--
-- User GUIDs (CCCC prefix to avoid collision with serial-signing AAAA prefix):
--   qa_a_admin        CCCC0000-0000-0000-0000-000000000001
--   qa_a_start        CCCC0000-0000-0000-0000-000000000002
--   qa_a_user1        CCCC0000-0000-0000-0000-000000000003
--   qa_a_user2        CCCC0000-0000-0000-0000-000000000004
--   qa_a_same_dept    CCCC0000-0000-0000-0000-000000000005
--   qa_a_other_dept   CCCC0000-0000-0000-0000-000000000006
--   qa_a_mgr          CCCC0000-0000-0000-0000-000000000007
--
-- Dept GUIDs:
--   dept_A            DDDD0000-0000-0000-0000-000000000001
--   dept_B            DDDD0000-0000-0000-0000-000000000002
--
-- Run from a NATIVE shell (cmd / PowerShell), NOT git-bash:
--     sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
--
-- Notes:
--   - SET QUOTED_IDENTIFIER ON required for Wf_FlowDef filtered unique index.
--   - All users share admin's BCrypt password hash (= password "123456").
--   - Seed is idempotent: IF NOT EXISTS guards on all inserts.
--   - RoleId 9 is used for DataMap role-expansion scenario.
--   - RoleId 7 is used for Filter scenario (Role strategy).
-- =====================================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @tenant uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';  -- DefaultTenant

-- Fixed GUIDs
DECLARE @u_admin      uniqueidentifier = 'CCCC0000-0000-0000-0000-000000000001';
DECLARE @u_start      uniqueidentifier = 'CCCC0000-0000-0000-0000-000000000002';
DECLARE @u_user1      uniqueidentifier = 'CCCC0000-0000-0000-0000-000000000003';
DECLARE @u_user2      uniqueidentifier = 'CCCC0000-0000-0000-0000-000000000004';
DECLARE @u_same_dept  uniqueidentifier = 'CCCC0000-0000-0000-0000-000000000005';
DECLARE @u_other_dept uniqueidentifier = 'CCCC0000-0000-0000-0000-000000000006';
DECLARE @u_mgr        uniqueidentifier = 'CCCC0000-0000-0000-0000-000000000007';

DECLARE @dept_A uniqueidentifier = 'DDDD0000-0000-0000-0000-000000000001';
DECLARE @dept_B uniqueidentifier = 'DDDD0000-0000-0000-0000-000000000002';

-- ── 1. Departments (Sys_Dept) ────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM Sys_Dept WHERE Id = @dept_A)
  INSERT INTO Sys_Dept (Id, DeptCode, DeptName, Path, Sort, Enable, Creator, CreateDate, TenantId)
  VALUES (@dept_A, 'QA-DEPT-A', 'QA Dept A', '/qa-dept-a/', 0, 1, 'qa-approver-seed', GETDATE(), @tenant);

IF NOT EXISTS (SELECT 1 FROM Sys_Dept WHERE Id = @dept_B)
  INSERT INTO Sys_Dept (Id, DeptCode, DeptName, Path, Sort, Enable, Creator, CreateDate, TenantId)
  VALUES (@dept_B, 'QA-DEPT-B', 'QA Dept B', '/qa-dept-b/', 0, 1, 'qa-approver-seed', GETDATE(), @tenant);

-- ── 2. Users (clone admin's BCrypt hash = password "123456") ─────────────────

-- qa_a_admin: designer / maintenance login (admin role)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_admin)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, DeptId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_admin, 'qa_a_admin', Password, 'QA Admin', RoleId, NULL, 1, 'qa-approver-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_a_start: flow submitter (dept_A, manager=qa_a_mgr set below)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_start)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, DeptId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_start, 'qa_a_start', Password, 'QA Starter', RoleId, @dept_A, 1, 'qa-approver-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_a_user1: FormField target + DataMap user target (dept_A, role 9)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_user1)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, DeptId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_user1, 'qa_a_user1', Password, 'QA User1', 9, @dept_A, 1, 'qa-approver-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_a_user2: Group specified target (dept_B, no special role)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_user2)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, DeptId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_user2, 'qa_a_user2', Password, 'QA User2', RoleId, @dept_B, 1, 'qa-approver-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_a_same_dept: Filter scenario, passes same-dept filter (dept_A, role 7)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_same_dept)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, DeptId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_same_dept, 'qa_a_same_dept', Password, 'QA Same Dept', 7, @dept_A, 1, 'qa-approver-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_a_other_dept: Filter scenario, excluded by same-dept filter (dept_B, role 7)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_other_dept)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, DeptId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_other_dept, 'qa_a_other_dept', Password, 'QA Other Dept', 7, @dept_B, 1, 'qa-approver-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_a_mgr: Group DirectManager target (dept_A, manager of qa_a_start)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_mgr)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, DeptId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_mgr, 'qa_a_mgr', Password, 'QA Manager', RoleId, @dept_A, 1, 'qa-approver-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- ── 3. Manager chain wiring ───────────────────────────────────────────────────

-- qa_a_start -> qa_a_mgr (DirectManager L1 for Group scenario)
UPDATE Sys_Users SET DeptId = @dept_A, ManagerId = @u_mgr WHERE Id = @u_start;
UPDATE Sys_Users SET DeptId = @dept_A, ManagerId = NULL   WHERE Id = @u_mgr;   -- chain top

-- ── 4. FormDefs ───────────────────────────────────────────────────────────────

-- approver-field-form: has a "user" type field named "approver" (FormField scenario)
IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'approver-field-form')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'EEEE0000-0000-0000-0000-000000000001',
    'approver-field-form',
    'Approver Field Form',
    '{"fields":[{"name":"subject","label":"Subject","type":"text","required":true},{"name":"approver","label":"Approver","type":"user","required":true,"multiple":false}]}',
    1, 1, 'qa-approver-seed', GETDATE(), @tenant
  );

-- datamap-form: has a "text" field named "costCenter" (DataMap scenario)
IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'datamap-form')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'EEEE0000-0000-0000-0000-000000000002',
    'datamap-form',
    'DataMap Form',
    '{"fields":[{"name":"costCenter","label":"Cost Center","type":"text","required":true},{"name":"amount","label":"Amount","type":"number","required":false}]}',
    1, 1, 'qa-approver-seed', GETDATE(), @tenant
  );

-- group-form: simple text form for Group / When / Filter scenarios
IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'group-form')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'EEEE0000-0000-0000-0000-000000000003',
    'group-form',
    'Group Form',
    '{"fields":[{"name":"subject","label":"Subject","type":"text","required":true},{"name":"amount","label":"Amount","type":"number","required":false}]}',
    1, 1, 'qa-approver-seed', GETDATE(), @tenant
  );

-- ── 5. FlowDefs ──────────────────────────────────────────────────────────────
--
-- All schemas: start -> a1 (approval) -> end
-- ApproverStrategy field names are camelCase (FlowEngine uses PropertyNameCaseInsensitive=true).
-- GUIDs in schemaJson must match the declared user GUIDs above.
--   CCCC0000-0000-0000-0000-000000000003 = qa_a_user1
--   CCCC0000-0000-0000-0000-000000000004 = qa_a_user2
--   CCCC0000-0000-0000-0000-000000000007 = qa_a_mgr

-- approver-formfield-flow: FormField node reads "approver" field from varsJson
IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'approver-formfield-flow')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'FFFF0000-0000-0000-0000-000000000001',
    'approver-formfield-flow',
    'FormField Approver Flow',
    'approver-field-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"FormField Node","approverStrategy":"FormField","approverFieldName":"approver","countersign":"all"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"}]}',
    1, 1, 'qa-approver-seed', GETDATE(), @tenant
  );

-- approver-datamap-flow: DataMap node reads "costCenter" field -> looks up Wf_ApproverMap key "cc"
IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'approver-datamap-flow')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'FFFF0000-0000-0000-0000-000000000002',
    'approver-datamap-flow',
    'DataMap Approver Flow',
    'datamap-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"DataMap Node","approverStrategy":"DataMap","approverFieldName":"costCenter","approverMapKey":"cc","countersign":"all"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"}]}',
    1, 1, 'qa-approver-seed', GETDATE(), @tenant
  );

-- approver-when-flow: two nodes; a2 has When gate "amount >= 10000"
-- Node a1: Specified=qa_a_user1 (always active)
-- Node a2: Specified=qa_a_user2 (When: amount >= 10000)
IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'approver-when-flow')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'FFFF0000-0000-0000-0000-000000000003',
    'approver-when-flow',
    'When Gate Flow',
    'datamap-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"First Approval","approverStrategy":"Specified","approverUserId":"CCCC0000-0000-0000-0000-000000000003","countersign":"all"},{"id":"a2","type":"approval","name":"High Amount Approval","approverStrategy":"Specified","approverUserId":"CCCC0000-0000-0000-0000-000000000004","approverWhen":"amount >= 10000","countersign":"all"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"a2"},{"from":"a2","to":"end"}]}',
    1, 1, 'qa-approver-seed', GETDATE(), @tenant
  );

-- approver-filter-flow: Role 7 node with Filter "user.deptId == starter.deptId"
IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'approver-filter-flow')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'FFFF0000-0000-0000-0000-000000000004',
    'approver-filter-flow',
    'Filter Role Flow',
    'group-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"Same Dept Approval","approverStrategy":"Role","approverRoleId":7,"approverFilter":"user.deptId == starter.deptId","countersign":"all"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"}]}',
    1, 1, 'qa-approver-seed', GETDATE(), @tenant
  );

-- approver-group-flow: Group node with DirectManager L1 + Specified=qa_a_mgr (dedup -> single task)
IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'approver-group-flow')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'FFFF0000-0000-0000-0000-000000000005',
    'approver-group-flow',
    'Group Mixed Flow',
    'group-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"Group Node","approverStrategy":"Group","approverMembers":[{"strategy":"DirectManager","approverLevels":1},{"strategy":"Specified","approverUserId":"CCCC0000-0000-0000-0000-000000000007"}],"countersign":"all"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"}]}',
    1, 1, 'qa-approver-seed', GETDATE(), @tenant
  );

-- approver-forecast-flow: FormField node (same as formfield flow) used for forecast preview
IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'approver-forecast-flow')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'FFFF0000-0000-0000-0000-000000000006',
    'approver-forecast-flow',
    'Forecast Preview Flow',
    'approver-field-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"FormField Preview","approverStrategy":"FormField","approverFieldName":"approver","countersign":"all"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"}]}',
    1, 1, 'qa-approver-seed', GETDATE(), @tenant
  );

-- ── 6. Wf_ApproverMap rows (DataMap scenario) ────────────────────────────────
--
-- MapKey "cc", MatchValue "A100":
--   Row 1: ApproverUserId = qa_a_user1 (direct user assignment)
--   Row 2: ApproverRoleId = 9          (role-expansion: all role-9 members -> qa_a_user1)

IF NOT EXISTS (SELECT 1 FROM Wf_ApproverMap WHERE MapKey = 'cc' AND MatchValue = 'A100' AND ApproverUserId = @u_user1)
  INSERT INTO Wf_ApproverMap (Id, MapKey, MatchValue, ApproverUserId, ApproverRoleId, OrderNo, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'ABCD0000-0000-0000-0000-000000000001',
    'cc', 'A100',
    @u_user1, NULL,
    0, 1, 'qa-approver-seed', GETDATE(), @tenant
  );

IF NOT EXISTS (SELECT 1 FROM Wf_ApproverMap WHERE MapKey = 'cc' AND MatchValue = 'A100' AND ApproverRoleId = 9)
  INSERT INTO Wf_ApproverMap (Id, MapKey, MatchValue, ApproverUserId, ApproverRoleId, OrderNo, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'ABCD0000-0000-0000-0000-000000000002',
    'cc', 'A100',
    NULL, 9,
    1, 1, 'qa-approver-seed', GETDATE(), @tenant
  );

-- ── 7. Sanity report ──────────────────────────────────────────────────────────

SELECT 'qa_a_admin'       AS seeded, CONVERT(varchar(36), Id) AS val FROM Sys_Users WHERE UserName = 'qa_a_admin'
UNION ALL
SELECT 'qa_a_start',                 CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_a_start'
UNION ALL
SELECT 'qa_a_user1',                 CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_a_user1'
UNION ALL
SELECT 'qa_a_user2',                 CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_a_user2'
UNION ALL
SELECT 'qa_a_same_dept',             CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_a_same_dept'
UNION ALL
SELECT 'qa_a_other_dept',            CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_a_other_dept'
UNION ALL
SELECT 'qa_a_mgr',                   CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_a_mgr'
UNION ALL
SELECT 'approver-field-form',        FormKey                          FROM Wf_FormDef WHERE FormKey = 'approver-field-form'
UNION ALL
SELECT 'datamap-form',               FormKey                          FROM Wf_FormDef WHERE FormKey = 'datamap-form'
UNION ALL
SELECT 'group-form',                 FormKey                          FROM Wf_FormDef WHERE FormKey = 'group-form'
UNION ALL
SELECT 'approver-formfield-flow',    FlowKey                          FROM Wf_FlowDef WHERE FlowKey = 'approver-formfield-flow'
UNION ALL
SELECT 'approver-datamap-flow',      FlowKey                          FROM Wf_FlowDef WHERE FlowKey = 'approver-datamap-flow'
UNION ALL
SELECT 'approver-when-flow',         FlowKey                          FROM Wf_FlowDef WHERE FlowKey = 'approver-when-flow'
UNION ALL
SELECT 'approver-filter-flow',       FlowKey                          FROM Wf_FlowDef WHERE FlowKey = 'approver-filter-flow'
UNION ALL
SELECT 'approver-group-flow',        FlowKey                          FROM Wf_FlowDef WHERE FlowKey = 'approver-group-flow'
UNION ALL
SELECT 'approver-forecast-flow',     FlowKey                          FROM Wf_FlowDef WHERE FlowKey = 'approver-forecast-flow'
UNION ALL
SELECT 'Wf_ApproverMap cc/A100 user',CONVERT(varchar(36), Id)        FROM Wf_ApproverMap WHERE MapKey='cc' AND MatchValue='A100' AND ApproverUserId = 'CCCC0000-0000-0000-0000-000000000003'
UNION ALL
SELECT 'Wf_ApproverMap cc/A100 role9',CONVERT(varchar(36), Id)       FROM Wf_ApproverMap WHERE MapKey='cc' AND MatchValue='A100' AND ApproverRoleId = 9;

-- Verify dept + manager wiring:
SELECT u.UserName, CONVERT(varchar(36), u.DeptId) AS DeptId, mgr.UserName AS ManagerName
FROM Sys_Users u
LEFT JOIN Sys_Users mgr ON mgr.Id = u.ManagerId
WHERE u.UserName IN ('qa_a_start','qa_a_mgr','qa_a_user1','qa_a_same_dept','qa_a_other_dept')
ORDER BY u.UserName;
-- Expected:
--   qa_a_mgr       | DDDD...0001 (dept_A) | NULL
--   qa_a_other_dept| DDDD...0002 (dept_B) | NULL
--   qa_a_same_dept | DDDD...0001 (dept_A) | NULL
--   qa_a_start     | DDDD...0001 (dept_A) | qa_a_mgr
--   qa_a_user1     | DDDD...0001 (dept_A) | NULL
