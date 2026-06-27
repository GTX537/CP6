-- =====================================================================
-- WFS Phase A — real-stack QA seed (SQL Server CP6DB)
-- Branch: feat/wfs-form-inbox
-- Creates: a 2nd approver user (qa_user_b), a PARALLEL approval flow
-- (qa_parallel) and a LINEAR approval flow (qa_linear), all scoped to the
-- DefaultTenant (A1 = admin's tenant). Idempotent (IF NOT EXISTS guards).
--
-- Run from a NATIVE shell (cmd / PowerShell), NOT git-bash:
--     sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB -E -C -i seed.sql
--
-- NOTE on the QA run itself: git-bash (MSYS) backslash-escapes embedded
-- double quotes when passing argv to the native sqlcmd.exe, which mangles
-- the JSON literals below. During the QA the inserts were therefore issued
-- via `-Q` with the JSON written using tilde (~) placeholders converted to
-- double quotes by REPLACE(...,'~',CHAR(34)). The two forms are equivalent;
-- this file keeps the human-readable double-quote form for the record.
-- =====================================================================
SET NOCOUNT ON;

DECLARE @tenant uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';   -- DefaultTenant (admin's tenant)
DECLARE @userA  uniqueidentifier = 'E551EED9-8664-4DB3-E604-08DE956B883B';   -- admin     -> approver of node a
DECLARE @userB  uniqueidentifier = '0B000000-0000-0000-0000-0000000000B2';   -- qa_user_b -> approver of node b

-- 1) qa_user_b: clone admin's BCrypt password hash (= 123456) + role + tenant so it can LOG IN
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE UserName = 'qa_user_b')
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @userB, 'qa_user_b', Password, 'QA User B', RoleId, 1, 'qa-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- 2) qa_parallel form (minimal; satisfies FlowDef.FormKey binding for the TodoCenter dialog)
IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'qa_parallel')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES ('F0000000-0000-0000-0000-00000000F0F0', 'qa_parallel', 'QA Parallel Form', '{"fields":[]}', 1, 1, 'qa-seed', GETDATE(), @tenant);

-- 3) qa_parallel flow: start(s) -> parallelSplit(split) -> [approval(a)=userA, approval(b)=userB] -> parallelJoin(join) -> end
IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'qa_parallel')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES ('F1000000-0000-0000-0000-00000000F1F1', 'qa_parallel', 'QA Parallel Approval', 'qa_parallel',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"split","type":"parallelSplit","name":"Split"},{"id":"a","type":"approval","name":"Approval A","approverStrategy":"Specified","approverUserId":"E551EED9-8664-4DB3-E604-08DE956B883B","countersign":"all"},{"id":"b","type":"approval","name":"Approval B","approverStrategy":"Specified","approverUserId":"0B000000-0000-0000-0000-0000000000B2","countersign":"all"},{"id":"join","type":"parallelJoin","name":"Join"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"split"},{"from":"split","to":"a"},{"from":"split","to":"b"},{"from":"a","to":"join"},{"from":"b","to":"join"},{"from":"join","to":"end"}]}',
    1, 1, 'qa-seed', GETDATE(), @tenant);

-- 4) qa_linear form
IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'qa_linear')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES ('F2000000-0000-0000-0000-00000000F2F2', 'qa_linear', 'QA Linear Form', '{"fields":[]}', 1, 1, 'qa-seed', GETDATE(), @tenant);

-- 5) qa_linear flow: start(s) -> approval(a)=admin -> end  (linear compat, no business callback binding)
IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'qa_linear')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES ('F3000000-0000-0000-0000-00000000F3F3', 'qa_linear', 'QA Linear Approval', 'qa_linear',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a","type":"approval","name":"Approval","approverStrategy":"Specified","approverUserId":"E551EED9-8664-4DB3-E604-08DE956B883B","countersign":"all"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a"},{"from":"a","to":"end"}]}',
    1, 1, 'qa-seed', GETDATE(), @tenant);

-- report
SELECT 'qa_user_b'        AS seeded, CONVERT(varchar(36), Id) AS val FROM Sys_Users  WHERE UserName = 'qa_user_b'
UNION ALL SELECT 'qa_parallel_flow', FlowKey FROM Wf_FlowDef WHERE FlowKey = 'qa_parallel'
UNION ALL SELECT 'qa_parallel_form', FormKey FROM Wf_FormDef WHERE FormKey = 'qa_parallel'
UNION ALL SELECT 'qa_linear_flow',   FlowKey FROM Wf_FlowDef WHERE FlowKey = 'qa_linear'
UNION ALL SELECT 'qa_linear_form',   FormKey FROM Wf_FormDef WHERE FormKey = 'qa_linear';
