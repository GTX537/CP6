-- =====================================================================
-- WFS 波④ Inbox UX (E-T2) -- QA seed (SQL Server CP6DB_OA)
-- Branch: feat/wfs-inbox-ux
-- Date:   2026-07-13
--
-- Covers the four wave-④ inbox UX features:
--   (1) notification matrix gating        (NotifyMatrix / PrefController / PersistentWfNotifier)
--   (2) in-flight batch transfer          (POST /api/oa/inbox/batch-transfer[/preview])
--   (3) pending rowMode merged/expanded    (GET /api/oa/inbox/pending?rowMode=)
--   (4) mobile 375px / desktop 1280px walk (browser, README sections 5-6)
--
-- *** DESIGN NOTE -- deliberate anchor-drift correction (state honestly) ***
-- The E-T2 brief's Step-2 asked for a WHILE-loop raw INSERT of 30 Wf_FlowInstance/
-- Wf_FlowToken/Wf_FlowTask/Wf_FlowFormTo rows. We DO NOT raw-seed runtime rows here.
-- Rationale (cross-checked against shipped code):
--   * Batch transfer calls the REAL engine FlowEngine.TransferAsync per task
--     (AdvancedFlow.cs:78-98): it mutates task.AssigneeId, appends a Wf_FlowFormTo
--     pair, writes Wf_FlowHistory(action="transfer"), and notifies. Those runtime
--     rows must be ENGINE-produced to stay coherent (token lineage, FormTo Pending
--     row, countersign snapshot). A hand-rolled raw fixture would drift from what
--     SubmitAsync actually writes and would make TransferAsync behaviour non-
--     representative.
--   * The shipped BatchTransferTests.cs seeds pending work the same way -- via
--     Engine.SubmitAsync in a loop (BatchTransferTests.cs:39-44), not raw INSERT.
--   * The wfs-serial-signing harness set the precedent: seed users+FlowDef only,
--     let the ps1 submit flows over HTTP to create runtime rows.
-- So this seed provisions ONLY the static fixtures (users + 1 FormDef + 2 FlowDefs);
-- qa_inbox_ux.ps1 submits flows over HTTP to create the 30 pending tasks, the dirty
-- (already-handled) row, and the parallel-3 instance. This keeps every runtime row
-- coherent with the engine and the harness self-contained + idempotent.
--
-- Creates (all in DefaultTenant A1):
--   6 users (password "123456", admin's BCrypt hash cloned):
--     qa_bt_admin   (RoleId=1)            -- batch-transfer operator (management action).
--                                            RoleId=1 is granted (oa-inbox, batch-transfer)
--                                            by InboxBatchTransferPermissionSeed per tenant.
--     qa_bt_starter (RoleId=1, Email set) -- submits flows; recipient of FlowRejected (scenario 1);
--                                            sets its OWN notify-matrix pref.
--     qa_bt_from    (RoleId=1)            -- line-flow approver; the batch-transfer FROM user;
--                                            also rejects the notify-scenario flow.
--     qa_bt_to      (RoleId=1, Enable=1)  -- batch-transfer TO (target must be enabled + same tenant).
--     qa_bt_par     (RoleId=1)            -- approver of ALL 3 parallel branches (rowMode scenario);
--                                            the rowMode viewer; sets its OWN rowMode pref.
--     qa_bt_norole  (RoleId=2)            -- NOT role 1 -> lacks (oa-inbox, batch-transfer)
--                                            -> 403 negative test (PermissionService has no admin bypass).
--   1 FormDef:  qa-bt-form (subject text only).
--   2 FlowDefs:
--     qa-bt-line  start -> approval(Specified qa_bt_from) -> end   -- notify + batch-transfer fixture.
--     qa-bt-par3  start -> parallelSplit -> a/b/c approval(Specified qa_bt_par) -> parallelJoin -> end
--                 (all three branches point at the SAME approver, so one submit lands 3 pending
--                  tasks on qa_bt_par in ONE instance -> merged=1 row, expanded=3 rows).
--
-- Run from a NATIVE shell (cmd / PowerShell), NOT git-bash:
--     sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
--
-- Notes:
--   - SET QUOTED_IDENTIFIER ON (Wf_FlowDef has a filtered unique index on FlowKey WHERE Enable=1).
--   - Table names are SINGULAR: Wf_FlowDef / Wf_FormDef / Sys_Users.
--   - Idempotent: IF NOT EXISTS guards on every insert.
--   - Schema node/edge fields are camelCase (FlowEngine deserialises PropertyNameCaseInsensitive=true).
-- =====================================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @tenant uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';  -- DefaultTenant A1

-- Fixed GUIDs (referenced from FlowDef SchemaJson + ps1 constants -- must match qa_inbox_ux.ps1)
DECLARE @u_admin   uniqueidentifier = 'CCCC0000-0000-0000-0000-0000000BB0A0';  -- qa_bt_admin
DECLARE @u_starter uniqueidentifier = 'CCCC0000-0000-0000-0000-0000000BB0B0';  -- qa_bt_starter
DECLARE @u_from    uniqueidentifier = 'CCCC0000-0000-0000-0000-0000000000C0';  -- qa_bt_from
DECLARE @u_to      uniqueidentifier = 'CCCC0000-0000-0000-0000-0000000000D0';  -- qa_bt_to
DECLARE @u_par     uniqueidentifier = 'CCCC0000-0000-0000-0000-0000000000E0';  -- qa_bt_par
DECLARE @u_norole  uniqueidentifier = 'CCCC0000-0000-0000-0000-0000000000F0';  -- qa_bt_norole (RoleId=2)

DECLARE @form  uniqueidentifier = 'CCCC0000-0000-0000-0000-0000000000F1';  -- qa-bt-form
DECLARE @fLine uniqueidentifier = 'CCCC0000-0000-0000-0000-0000000000F2';  -- qa-bt-line
DECLARE @fPar  uniqueidentifier = 'CCCC0000-0000-0000-0000-0000000000F3';  -- qa-bt-par3

-- ── 1. Users (Password cloned from admin's BCrypt hash = "123456") ────────────

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_admin)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_admin, 'qa_bt_admin', Password, 'QA BT Admin', 1, 1, 'qa-bt-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_starter)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_starter, 'qa_bt_starter', Password, 'QA BT Starter', 1, 1, 'qa-bt-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_from)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_from, 'qa_bt_from', Password, 'QA BT From (approver)', 1, 1, 'qa-bt-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_to)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_to, 'qa_bt_to', Password, 'QA BT To (target)', 1, 1, 'qa-bt-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_par)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_par, 'qa_bt_par', Password, 'QA BT Parallel Approver', 1, 1, 'qa-bt-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_bt_norole: RoleId=2 (NOT the admin role) -> no (oa-inbox, batch-transfer) RoleAction -> 403.
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_norole)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_norole, 'qa_bt_norole', Password, 'QA BT No-Role (RoleId=2)', 2, 1, 'qa-bt-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- Give the starter a real email so scenario 1 "no email" is meaningful: with flowRejected x email
-- OFF the LogEmailSender line must be ABSENT even though an address exists (otherwise the absence
-- proves nothing). TrySendEmailAsync no-ops on blank email (PersistentWfNotifier.cs:229-235).
UPDATE Sys_Users SET Email = 'qa_bt_starter@example.com' WHERE Id = @u_starter AND (Email IS NULL OR Email = '');

-- ── 2. FormDef: qa-bt-form ────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'qa-bt-form')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    @form, 'qa-bt-form', 'Inbox UX Demo Form',
    '{"fields":[{"name":"subject","label":"Subject","type":"text","required":false}]}',
    1, 1, 'qa-bt-seed', GETDATE(), @tenant
  );

-- ── 3. FlowDef: qa-bt-line (start -> approval(Specified qa_bt_from) -> end) ────
--   Submit -> ONE pending task on qa_bt_from. Approving completes the flow (dirty/done fixture);
--   rejecting fires FlowRejectedAsync to the starter (scenario 1).

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'qa-bt-line')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (@fLine, 'qa-bt-line', 'Inbox UX Line Approval', 'qa-bt-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"Approve","approverStrategy":"Specified","approverUserId":"CCCC0000-0000-0000-0000-0000000000C0"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"}]}',
    1, 1, 'qa-bt-seed', GETDATE(), @tenant);

-- ── 4. FlowDef: qa-bt-par3 (parallel 3-branch, all -> qa_bt_par) ──────────────
--   Submit -> parallelSplit spawns 3 tokens -> 3 approval tasks, ALL assigned to qa_bt_par,
--   ALL in the SAME instance. rowMode merged groups by instance -> 1 row; expanded -> 3 rows.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'qa-bt-par3')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (@fPar, 'qa-bt-par3', 'Inbox UX Parallel 3-Branch', 'qa-bt-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"split","type":"parallelSplit","name":"Split"},{"id":"a","type":"approval","name":"A","approverStrategy":"Specified","approverUserId":"CCCC0000-0000-0000-0000-0000000000E0"},{"id":"b","type":"approval","name":"B","approverStrategy":"Specified","approverUserId":"CCCC0000-0000-0000-0000-0000000000E0"},{"id":"c","type":"approval","name":"C","approverStrategy":"Specified","approverUserId":"CCCC0000-0000-0000-0000-0000000000E0"},{"id":"join","type":"parallelJoin","name":"Join"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"split"},{"from":"split","to":"a"},{"from":"split","to":"b"},{"from":"split","to":"c"},{"from":"a","to":"join"},{"from":"b","to":"join"},{"from":"c","to":"join"},{"from":"join","to":"end"}]}',
    1, 1, 'qa-bt-seed', GETDATE(), @tenant);

-- ── 5. Sanity report ──────────────────────────────────────────────────────────

SELECT 'qa_bt_admin'                AS seeded, CONVERT(varchar(36), Id) AS val FROM Sys_Users WHERE UserName = 'qa_bt_admin'
UNION ALL SELECT 'qa_bt_starter',            CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_bt_starter'
UNION ALL SELECT 'qa_bt_from',               CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_bt_from'
UNION ALL SELECT 'qa_bt_to',                 CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_bt_to'
UNION ALL SELECT 'qa_bt_par',                CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_bt_par'
UNION ALL SELECT 'qa_bt_norole (RoleId=2)',  CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_bt_norole'
UNION ALL SELECT 'FormDef:qa-bt-form',        FormKey                        FROM Wf_FormDef WHERE FormKey = 'qa-bt-form'
UNION ALL SELECT 'FlowDef:' + FlowKey,        CONVERT(varchar(36), Id)       FROM Wf_FlowDef WHERE FlowKey IN ('qa-bt-line', 'qa-bt-par3');

-- Expected: 6 users + 1 form + 2 flowdefs = 9 rows.
-- Verify (oa-inbox, batch-transfer) IS granted to RoleId=1 but NOT RoleId=2 in this tenant:
SELECT ra.RoleId, ra.MenuId, ra.ActionCode
FROM Sys_RoleAction ra
WHERE ra.TenantId = @tenant AND ra.MenuId = 733 AND ra.ActionCode = 'batch-transfer';
-- Expected: exactly one row, RoleId=1 (seeded by InboxBatchTransferPermissionSeed). RoleId=2 absent -> 403.
