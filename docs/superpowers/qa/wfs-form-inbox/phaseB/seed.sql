-- =====================================================================
-- OA 电子表单信箱 Phase B — real-stack QA seed (SQL Server CP6DB_OA)
-- Branch: feat/oa-inbox-core
--
-- Creates:
--   qa_starter  — can start flows (任意已登录用户可发起)
--   qa_approver — the designated approver on node n1 of the leave flow
--   qa_cc       — receives CC notifications
--   Wf_FormDef  leave   — simple "请假申请" form with two fields
--   Wf_FlowDef  leave   — single-approval flow: start→n1(approval)→end
--                         with CcUsers pointing at qa_cc
--   Wf_FlowDef  leave2  — second (disabled) leave flow for the E-WF-008 test
--
-- Idempotent — safe to re-run (all inserts guarded by IF NOT EXISTS).
--
-- Run from cmd.exe or PowerShell (NOT git-bash — MSYS escapes JSON quotes):
--   sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
--
-- PREREQUISITE: start the OA backend once first so EF Migrate() creates
--   CP6DB_OA and all tables before this seed runs.
-- =====================================================================
SET NOCOUNT ON;

-- ─── Constants ───────────────────────────────────────────────────────────────
DECLARE @tenant    uniqueidentifier = '00000000-0000-0000-0000-0000000000A1'; -- DefaultTenant
DECLARE @starter   uniqueidentifier = 'AA000000-0000-0000-0000-000000000AA1'; -- qa_starter
DECLARE @approver  uniqueidentifier = 'BB000000-0000-0000-0000-000000000BB2'; -- qa_approver
DECLARE @ccUser    uniqueidentifier = 'CC000000-0000-0000-0000-000000000CC3'; -- qa_cc
DECLARE @formDefId uniqueidentifier = 'FD000000-0000-0000-0000-0000000FD001'; -- Wf_FormDef leave
DECLARE @flowDefId uniqueidentifier = 'FW000000-0000-0000-0000-0000000FW001'; -- Wf_FlowDef leave
DECLARE @flowDef2  uniqueidentifier = 'FW000000-0000-0000-0000-0000000FW002'; -- Wf_FlowDef leave2 (conflict test)

-- ─── Step 1: Fetch the BCrypt hash of "123456" from the existing admin ────────
-- The admin user's Password column holds the BCrypt hash for "123456".
-- We copy it so all three QA users can log in with password "123456".
--
-- Sanity-check: run this SELECT first and confirm it returns one row:
--   SELECT UserName, Password FROM Sys_Users WHERE UserName = 'admin';
-- If the admin row is absent (fresh DB, first-boot seed not yet run), start
-- the backend once, let it seed, shut it down, then re-run this script.
--
DECLARE @bcryptHash nvarchar(100);
SELECT TOP 1 @bcryptHash = Password FROM Sys_Users WHERE UserName = 'admin';

IF @bcryptHash IS NULL
BEGIN
    RAISERROR('admin user not found — start the backend once to seed Sys_Users, then re-run seed.sql', 16, 1);
    RETURN;
END

-- Fetch the admin's RoleId (typically 1 = super-admin in DefaultTenant)
DECLARE @roleId int;
SELECT TOP 1 @roleId = RoleId FROM Sys_Users WHERE UserName = 'admin';

-- ─── Step 2: Create the three QA users ───────────────────────────────────────
-- All three share the same BCrypt hash → login password is "123456"
-- Columns from Sys_Users entity (BaseEntity + SysUser fields):
--   Id / UserName / Password / NickName / RoleId / Enable / Creator / CreateDate /
--   TenantId / FailedLoginCount / MustChangePassword / AllowPasswordFallback /
--   TwoFactorEnabled / IsPlatformAdmin / PasswordChangedAt

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE UserName = 'qa_starter')
    INSERT INTO Sys_Users
        (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
         TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback,
         TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
    VALUES
        (@starter, 'qa_starter', @bcryptHash, 'QA Starter', @roleId, 1, 'qa-seed', GETDATE(),
         @tenant, 0, 0, 0, 0, 0, GETDATE());

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE UserName = 'qa_approver')
    INSERT INTO Sys_Users
        (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
         TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback,
         TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
    VALUES
        (@approver, 'qa_approver', @bcryptHash, 'QA Approver', @roleId, 1, 'qa-seed', GETDATE(),
         @tenant, 0, 0, 0, 0, 0, GETDATE());

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE UserName = 'qa_cc')
    INSERT INTO Sys_Users
        (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
         TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback,
         TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
    VALUES
        (@ccUser, 'qa_cc', @bcryptHash, 'QA CC User', @roleId, 1, 'qa-seed', GETDATE(),
         @tenant, 0, 0, 0, 0, 0, GETDATE());

-- ─── Step 3: Wf_FormDef — leave form with two simple fields ──────────────────
-- Columns: Id / FormKey / FormName / SchemaJson / Version / Enable /
--          Creator / CreateDate / TenantId  (BaseTenantEntity extends BaseEntity)
IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'leave')
    INSERT INTO Wf_FormDef
        (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
    VALUES
        (@formDefId, 'leave', '请假申请',
         -- Two fields: reason (text, required) + days (number, required)
         '{"fields":[{"key":"reason","label":"请假原因","type":"text","required":true},{"key":"days","label":"天数","type":"number","required":true}]}',
         1, 1, 'qa-seed', GETDATE(), @tenant);

-- ─── Step 4: Wf_FlowDef — leave flow (single approval + CC) ─────────────────
-- Columns: Id / FlowKey / FlowName / FormKey / SchemaJson / Version / Enable /
--          Creator / CreateDate / TenantId
--
-- Flow graph:
--   start(s) → n1(approval, Specified=qa_approver, CcUsers=[qa_cc]) → end
--
-- approverUserId = BB000000-0000-0000-0000-000000000BB2 (qa_approver)
-- CcUsers        = [CC000000-0000-0000-0000-000000000CC3] (qa_cc)
IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'leave')
    INSERT INTO Wf_FlowDef
        (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
    VALUES
        (@flowDefId, 'leave', '请假审批', 'leave',
         '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"n1","type":"approval","name":"审批","approverStrategy":"Specified","approverUserId":"BB000000-0000-0000-0000-000000000BB2","countersign":"all","ccUsers":["CC000000-0000-0000-0000-000000000CC3"]},{"id":"end","type":"end","name":"结束"}],"edges":[{"from":"s","to":"n1"},{"from":"n1","to":"end"}]}',
         1, 1, 'qa-seed', GETDATE(), @tenant);

-- ─── Step 5: Wf_FlowDef — leave2 (disabled, same FormKey) ───────────────────
-- Used to test E-WF-008: enabling leave2 while leave is already enabled on the
-- same FormKey must return HTTP 400 with "E-WF-008" in the response body.
IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'leave2')
    INSERT INTO Wf_FlowDef
        (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
    VALUES
        (@flowDef2, 'leave2', '请假审批v2(冲突测试)', 'leave',
         '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"n1","type":"approval","name":"审批v2","approverStrategy":"Specified","approverUserId":"BB000000-0000-0000-0000-000000000BB2","countersign":"all"}],"edges":[{"from":"s","to":"n1"}]}',
         -- Enable=0 (disabled) — the conflict test tries to enable this
         1, 0, 'qa-seed', GETDATE(), @tenant);

-- ─── Report ──────────────────────────────────────────────────────────────────
SELECT 'qa_starter'  AS seeded, CONVERT(varchar(36), Id) AS id FROM Sys_Users  WHERE UserName = 'qa_starter'
UNION ALL
SELECT 'qa_approver', CONVERT(varchar(36), Id)             FROM Sys_Users  WHERE UserName = 'qa_approver'
UNION ALL
SELECT 'qa_cc',       CONVERT(varchar(36), Id)             FROM Sys_Users  WHERE UserName = 'qa_cc'
UNION ALL
SELECT 'leave_form',  CONVERT(varchar(36), Id)             FROM Wf_FormDef WHERE FormKey  = 'leave'
UNION ALL
SELECT 'leave_flow',  CONVERT(varchar(36), Id)             FROM Wf_FlowDef WHERE FlowKey  = 'leave'
UNION ALL
SELECT 'leave2_flow', CONVERT(varchar(36), Id)             FROM Wf_FlowDef WHERE FlowKey  = 'leave2';

PRINT 'seed.sql complete — all rows idempotent.';
