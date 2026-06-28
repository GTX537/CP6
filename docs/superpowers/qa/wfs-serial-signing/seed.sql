-- =====================================================================
-- WFS Serial Signing (T13) -- QA seed (SQL Server CP6DB_OA)
-- Branch: feat/wfs-serial-sign
-- Date:   2026-06-28
--
-- Creates:
--   6 users  : qa_s_s0 / qa_s_start / qa_s_mgr1 / qa_s_mgr2 / qa_s_gm / qa_s_newmgr
--   1 FormDef: serial-demo-form
--   1 FlowDef: serial-demo  (3 design-time stages -> 4 runtime stages when qa_s_start submits)
--
-- Design-time stages in serial-demo:
--   Stage 0  : fixed / Specified = qa_s_s0       (档0, countersign=all)
--   Stage 1  : managerChain / MaxLevels=2        (展开 2 个运行档: qa_s_mgr1 L1, qa_s_mgr2 L2)
--   Stage 2  : fixed / Specified = qa_s_gm       (档3, countersign=all)
--
-- Runtime stage layout (when starter = qa_s_start):
--   RuntimeStageIndex 0 : fixed       -> qa_s_s0
--   RuntimeStageIndex 1 : managerChain L1 -> qa_s_mgr1
--   RuntimeStageIndex 2 : managerChain L2 -> qa_s_mgr2
--   RuntimeStageIndex 3 : fixed       -> qa_s_gm
--
-- Manager chain: qa_s_start.ManagerId = qa_s_mgr1; qa_s_mgr1.ManagerId = qa_s_mgr2
-- R13 freeze test: qa_s_newmgr is used to break the chain AFTER submission.
--
-- Run from a NATIVE shell (cmd / PowerShell), NOT git-bash:
--     sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
--
-- Notes:
--   - SET QUOTED_IDENTIFIER ON is required because Wf_FlowDef has filtered unique indexes.
--   - All users share admin's BCrypt password hash (= password "123456").
--   - The seed is idempotent: IF NOT EXISTS guards on all inserts.
-- =====================================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @tenant   uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';  -- DefaultTenant

-- Fixed GUIDs (used in FlowDef SchemaJson below -- must match)
DECLARE @u_s0     uniqueidentifier = 'AAAA0000-0000-0000-0000-0000000000A0';  -- qa_s_s0   (stage0 fixed approver)
DECLARE @u_start  uniqueidentifier = 'AAAA0000-0000-0000-0000-0000000000B0';  -- qa_s_start (flow starter, has manager chain)
DECLARE @u_mgr1   uniqueidentifier = 'AAAA0000-0000-0000-0000-0000000000C0';  -- qa_s_mgr1  (direct manager of qa_s_start)
DECLARE @u_mgr2   uniqueidentifier = 'AAAA0000-0000-0000-0000-0000000000D0';  -- qa_s_mgr2  (2nd level manager)
DECLARE @u_gm     uniqueidentifier = 'AAAA0000-0000-0000-0000-0000000000E0';  -- qa_s_gm    (stage2 fixed approver: GM)
DECLARE @u_newmgr uniqueidentifier = 'AAAA0000-0000-0000-0000-0000000000F0';  -- qa_s_newmgr (R13: alternate mgr, injected post-submit)

-- ── 1. Users (clone admin's BCrypt hash = password "123456") ──────────────

-- qa_s_s0: stage-0 fixed approver
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_s0)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_s0, 'qa_s_s0', Password, 'QA Stage0 Approver', RoleId, 1, 'qa-serial-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_s_start: flow starter (ManagerId set after mgr users exist; see below)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_start)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_start, 'qa_s_start', Password, 'QA Serial Starter', RoleId, 1, 'qa-serial-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_s_mgr1: direct manager (level-1) of qa_s_start
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_mgr1)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_mgr1, 'qa_s_mgr1', Password, 'QA Manager L1', RoleId, 1, 'qa-serial-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_s_mgr2: second-level manager of qa_s_start (manager of qa_s_mgr1)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_mgr2)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_mgr2, 'qa_s_mgr2', Password, 'QA Manager L2', RoleId, 1, 'qa-serial-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_s_gm: final stage fixed approver (General Manager)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_gm)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_gm, 'qa_s_gm', Password, 'QA General Manager', RoleId, 1, 'qa-serial-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_s_newmgr: alternate manager used in R13 freeze test (points qa_s_start at this user post-submit)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_newmgr)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_newmgr, 'qa_s_newmgr', Password, 'QA New Manager (R13)', RoleId, 1, 'qa-serial-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- ── 2. Manager chain wiring ────────────────────────────────────────────────
--   qa_s_start -> qa_s_mgr1 -> qa_s_mgr2 -> (no further manager)
--   ApprovalStagePlanner.BuildAsync walks this chain for the managerChain stage.

UPDATE Sys_Users SET ManagerId = @u_mgr1 WHERE Id = @u_start;
UPDATE Sys_Users SET ManagerId = @u_mgr2 WHERE Id = @u_mgr1;
UPDATE Sys_Users SET ManagerId = NULL     WHERE Id = @u_mgr2;   -- chain top

-- ── 3. FormDef: serial-demo-form ──────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'serial-demo-form')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'BBBB0000-0000-0000-0000-0000000000F1',
    'serial-demo-form',
    'Serial Demo Form',
    '{"fields":[{"name":"subject","label":"Subject","type":"text","required":true},{"name":"amount","label":"Amount","type":"number","required":false}]}',
    1, 1, 'qa-serial-seed', GETDATE(), @tenant
  );

-- ── 4. FlowDef: serial-demo ───────────────────────────────────────────────
--
-- Schema JSON structure (formatted for readability; inserted as a single string):
--
--   start -> approval node "a1" with 3 design-time stages -> end
--
--   Stage 0  (kind=fixed, Specified=qa_s_s0)
--   Stage 1  (kind=managerChain, maxLevels=2)   <- expands to 2 runtime stages
--   Stage 2  (kind=fixed, Specified=qa_s_gm)
--
-- All ApprovalStage field names are camelCase (FlowEngine deserialises
-- with PropertyNameCaseInsensitive=true; validator also uses ToLowerInvariant).
--
-- IMPORTANT: approverUserId values below match @u_s0 and @u_gm declared above.
--   AAAA0000-0000-0000-0000-0000000000A0  = qa_s_s0
--   AAAA0000-0000-0000-0000-0000000000E0  = qa_s_gm
--
-- The FlowSchemaValidator will accept this schema because:
--   - stage 0: kind=fixed, approverStrategy=Specified (known strategy), countersign=all (valid)
--   - stage 1: kind=managerChain, maxLevels=2 (>=1), countersign=all (valid)
--   - stage 2: kind=fixed, approverStrategy=Specified, countersign=all (valid)
--
-- SET QUOTED_IDENTIFIER ON (at top) is required for this INSERT because
-- Wf_FlowDefs has a filtered unique index on (FlowKey) WHERE Enable=1.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'serial-demo')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'BBBB0000-0000-0000-0000-0000000000F2',
    'serial-demo',
    'Serial Demo Approval',
    'serial-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"Serial Approval","stages":[{"name":"Stage 0 - Initial","kind":"fixed","approverStrategy":"Specified","approverUserId":"AAAA0000-0000-0000-0000-0000000000A0","countersign":"all"},{"name":"Stage 1-2 - Manager Chain","kind":"managerChain","maxLevels":2,"countersign":"all"},{"name":"Stage 3 - GM Approval","kind":"fixed","approverStrategy":"Specified","approverUserId":"AAAA0000-0000-0000-0000-0000000000E0","countersign":"all"}]},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"}]}',
    1, 1, 'qa-serial-seed', GETDATE(), @tenant
  );

-- ── 5. Sanity report ──────────────────────────────────────────────────────

SELECT 'qa_s_s0'              AS seeded, CONVERT(varchar(36), Id) AS val FROM Sys_Users  WHERE UserName = 'qa_s_s0'
UNION ALL
SELECT 'qa_s_start',                     CONVERT(varchar(36), Id)        FROM Sys_Users  WHERE UserName = 'qa_s_start'
UNION ALL
SELECT 'qa_s_mgr1',                      CONVERT(varchar(36), Id)        FROM Sys_Users  WHERE UserName = 'qa_s_mgr1'
UNION ALL
SELECT 'qa_s_mgr2',                      CONVERT(varchar(36), Id)        FROM Sys_Users  WHERE UserName = 'qa_s_mgr2'
UNION ALL
SELECT 'qa_s_gm',                        CONVERT(varchar(36), Id)        FROM Sys_Users  WHERE UserName = 'qa_s_gm'
UNION ALL
SELECT 'qa_s_newmgr',                    CONVERT(varchar(36), Id)        FROM Sys_Users  WHERE UserName = 'qa_s_newmgr'
UNION ALL
SELECT 'serial-demo-form (FormDef)',      FormKey                          FROM Wf_FormDef WHERE FormKey = 'serial-demo-form'
UNION ALL
SELECT 'serial-demo (FlowDef)',           FlowKey                          FROM Wf_FlowDef WHERE FlowKey = 'serial-demo';

-- Verify manager chain wiring:
SELECT
    u.UserName,
    mgr.UserName AS ManagerUserName,
    CONVERT(varchar(36), u.ManagerId) AS ManagerId
FROM Sys_Users u
LEFT JOIN Sys_Users mgr ON mgr.Id = u.ManagerId
WHERE u.UserName IN ('qa_s_start','qa_s_mgr1','qa_s_mgr2')
ORDER BY u.UserName;
-- Expected:
--   qa_s_mgr1  | qa_s_mgr2 | AAAA0000-0000-0000-0000-0000000000D0
--   qa_s_mgr2  | NULL      | NULL
--   qa_s_start | qa_s_mgr1 | AAAA0000-0000-0000-0000-0000000000C0
