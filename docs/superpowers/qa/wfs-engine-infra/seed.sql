-- =====================================================================
-- WFS 波⑤ Engine Infra six-pack (F-T2) -- QA seed (SQL Server CP6DB_OA)
-- Branch: feat/wfs-engine-infra
-- Date:   2026-07-13
--
-- Covers spec 2026-07-05-wfs-engine-infra-design (the six infra features):
--   (1) work calendar        (Sys_WorkCalendar + year page 743 + JP-holiday seed 35d + timer workdays mode)
--   (2) approval timeout errorEdge  (TimeoutAction="errorEdge" node-level void + route, E-WF-027)
--   (3) cleanup worker       (180d terminal hard-delete / reservation never-clean / stale alert)
--   (4) per-tenant connector (Wf_Connector encrypted + DbWfConnector + tenant-preferred + E-WF-028)
--   (5) per-node HTTP override (ServiceHttpMethod / ServiceTimeoutSec via ActionRef snapshot)
--   (6) per-tenant time zone (Sys_Tenant.TimeZoneId + ITenantClock + save-side E-WF-028)
--
-- *** STATUS: written, not run. *** Authored per task F-T2 (write-only). Live QA -- spin the
-- backend against the isolated CP6DB_OA database, run qa_infra.ps1, and drive the pages in a
-- real browser -- is executed later by the main agent with a QA user present. NOTHING here has
-- been executed. Bugs found during live QA are fixed TDD (regression test into CP6.Tests/**).
--
-- *** WHAT IS SEEDED vs WHAT THE HARNESS CREATES AT RUNTIME (stated honestly) ***
--   * Connectors are NOT seeded here. A Wf_Connector row's AuthJsonEncrypted is a DataProtection
--     ciphertext (purpose "Wfs.Connector.Auth", TenantSsoConfigService pattern) -- it CANNOT be
--     forged by a raw INSERT (a hand-rolled string would fail Unprotect at execute time). So the
--     ps1 CREATEs connectors through the admin API (POST /api/oa/wf-connector), which is also the
--     only path that exercises the encrypt-on-write / mask-on-read contract end-to-end. This is
--     the same principle by which wave-③ seeds message triggers via the API (one-time key) and
--     wave-④ submits runtime flow rows via HTTP rather than raw INSERT.
--   * The work-calendar exception rows for DefaultTenant A1 are already planted on boot by the
--     A-T2 startup block (JapaneseHolidaySeed.For(DefaultTenant), 35 rows, idempotent on
--     (TenantId,Date)). So A1 is NOT empty. The ps1's empty-state scenario CLEARs A1's
--     Sys_WorkCalendar rows first, asserts the import re-creates 35, then leaves them in place.
--   * The timer "workdays=3" node and the errorEdge approval node are BUILT LIVE in the designer
--     during the browser walkthrough (README sec 5) -- exactly as brief scenarios 1 & 3 word it.
--     For the errorEdge RUNTIME check (task void + route along the error edge) the harness needs
--     a persisted, already-valid errorEdge flow, so ONE such FlowDef is raw-seeded below
--     (qa-inf-erroredge). Timeout firing is driven by rolling Wf_FlowTask.DueAt back in SQL and
--     letting the 1-minute WfTimeoutScanWorker pick it up (README sec 4.3) -- same posture as the
--     wave-③ timer-misfire runbook.
--
-- Creates (all in DefaultTenant A1):
--   4 users (password "123456", admin's BCrypt hash cloned):
--     qa_inf_admin   (RoleId=1, IsPlatformAdmin=0) -- drives the OA admin surface: connector CRUD,
--                       calendar import, designer save, flow submit. RoleId=1 is granted
--                       Calendar.View/Edit + Connector.View/Edit by WorkCalendarConnectorPermissionSeed
--                       and oa-designer:edit / oa-form-catalog:submit / oa-inbox:approve by the
--                       existing OA seeds (PermissionService has NO admin bypass -> must be role 1).
--     qa_inf_appr    (RoleId=1)                    -- the errorEdge approval's Specified approver.
--     qa_inf_norole  (RoleId=2)                    -- NOT role 1 -> lacks Connector.Edit -> 403 test.
--     qa_inf_padmin  (RoleId=1, IsPlatformAdmin=1) -- platform admin; drives /api/platform/tenant
--                       (TenantController is [RequirePlatformAdmin]) for the timezone E-WF-028 test.
--   1 FormDef:  qa-inf-form (subject text only).
--   1 FlowDef (raw, bypasses FlowSchemaValidator):
--     qa-inf-erroredge  start -> a1 approval(Specified qa_inf_appr, timeoutHours=1, timeoutAction="errorEdge")
--                        -- normal edge --> end ; -- isError edge --> handler approval(Specified qa_inf_appr) --> end.
--       Valid by construction (a1 is an approval = a legal errorEdge source per
--       FlowSchemaValidator.ErrorEdgeSourceTypes {serviceTask,approval,subFlow}, and it HAS an
--       IsError out edge so E-WF-027 is satisfied). Seeded raw only so the runtime timeout can be
--       driven deterministically; the E-WF-027 NEGATIVE (no IsError edge) is exercised by the ps1
--       POSTing an invalid schema to /api/oa/designer/save.
--
-- Run from a NATIVE shell (cmd / PowerShell), NOT git-bash:
--     sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
--
-- Notes:
--   - SET QUOTED_IDENTIFIER ON (Wf_FlowDef has a filtered unique index on FlowKey WHERE Enable=1).
--   - Table names are SINGULAR: Wf_FlowDef / Wf_FormDef / Sys_Users / Sys_WorkCalendar / Wf_Connector.
--   - Wf_Connector.RowVersion is a rowversion (auto) column -- never inserted (and no connector is seeded anyway).
--   - Idempotent: IF NOT EXISTS guards on every insert.
--   - Schema node/edge fields are camelCase (FlowEngine deserialises PropertyNameCaseInsensitive=true).
-- =====================================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @tenant uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';  -- DefaultTenant A1

-- Fixed GUIDs (referenced from FlowDef SchemaJson + ps1 constants -- must match qa_infra.ps1)
DECLARE @u_admin  uniqueidentifier = 'EEEE0000-0000-0000-0000-0000000000A0';  -- qa_inf_admin
DECLARE @u_appr   uniqueidentifier = 'EEEE0000-0000-0000-0000-0000000000B0';  -- qa_inf_appr
DECLARE @u_norole uniqueidentifier = 'EEEE0000-0000-0000-0000-0000000000C0';  -- qa_inf_norole (RoleId=2)
DECLARE @u_padmin uniqueidentifier = 'EEEE0000-0000-0000-0000-0000000000D0';  -- qa_inf_padmin (IsPlatformAdmin=1)

DECLARE @form  uniqueidentifier = 'EEEE0000-0000-0000-0000-0000000000F1';  -- qa-inf-form
DECLARE @fErr  uniqueidentifier = 'EEEE0000-0000-0000-0000-0000000000F2';  -- qa-inf-erroredge

-- -- 1. Users (Password cloned from admin's BCrypt hash = "123456") -----------------

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_admin)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_admin, 'qa_inf_admin', Password, 'QA Infra Admin', 1, 1, 'qa-inf-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_appr)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_appr, 'qa_inf_appr', Password, 'QA Infra Approver', 1, 1, 'qa-inf-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_inf_norole: RoleId=2 (NOT the admin role) -> no Connector.Edit RoleAction -> 403.
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_norole)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_norole, 'qa_inf_norole', Password, 'QA Infra No-Role (RoleId=2)', 2, 1, 'qa-inf-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_inf_padmin: IsPlatformAdmin=1 -> may call /api/platform/tenant ([RequirePlatformAdmin]) for the timezone test.
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_padmin)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_padmin, 'qa_inf_padmin', Password, 'QA Infra Platform Admin', 1, 1, 'qa-inf-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 1, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- -- 2. FormDef: qa-inf-form ------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'qa-inf-form')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    @form, 'qa-inf-form', 'Engine Infra Demo Form',
    '{"fields":[{"name":"subject","label":"Subject","type":"text","required":false}]}',
    1, 1, 'qa-inf-seed', GETDATE(), @tenant
  );

-- -- 3. FlowDef: qa-inf-erroredge (approval timeout -> error edge, RAW) -----------
--   a1 = approval(Specified qa_inf_appr) with timeoutHours=1 + timeoutAction="errorEdge".
--     normal out edge a1->end (success path); isError out edge a1->handler (timeout path).
--   handler = approval(Specified qa_inf_appr) -> end.
--   Runtime timeout drill (README 4.3): submit -> one Pending task on qa_inf_appr at a1 with
--   DueAt ~ now+1h -> SQL roll DueAt into the past -> WfTimeoutScanWorker (1-min) fires
--   "erroredge": a1's Pending task -> Cancelled, token routes to handler (new Pending task),
--   instance stays Running, VarsJson gains timeoutError {nodeId,dueAt}.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'qa-inf-erroredge')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (@fErr, 'qa-inf-erroredge', 'Engine Infra ErrorEdge Approval', 'qa-inf-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"Approve","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000B0","timeoutHours":1,"timeoutAction":"errorEdge"},{"id":"handler","type":"approval","name":"Timeout Handler","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000B0"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"},{"from":"a1","to":"handler","isError":true},{"from":"handler","to":"end"}]}',
    1, 1, 'qa-inf-seed', GETDATE(), @tenant);

-- -- 4. Sanity report ------------------------------------------------------------

SELECT 'qa_inf_admin'                 AS seeded, CONVERT(varchar(36), Id) AS val FROM Sys_Users WHERE UserName = 'qa_inf_admin'
UNION ALL SELECT 'qa_inf_appr',                 CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_inf_appr'
UNION ALL SELECT 'qa_inf_norole (RoleId=2)',    CONVERT(varchar(36), Id)        FROM Sys_Users WHERE UserName = 'qa_inf_norole'
UNION ALL SELECT 'qa_inf_padmin (PlatformAdmin)', CONVERT(varchar(36), Id)      FROM Sys_Users WHERE UserName = 'qa_inf_padmin'
UNION ALL SELECT 'FormDef:qa-inf-form',          FormKey                        FROM Wf_FormDef WHERE FormKey = 'qa-inf-form'
UNION ALL SELECT 'FlowDef:qa-inf-erroredge',     CONVERT(varchar(36), Id)       FROM Wf_FlowDef WHERE FlowKey = 'qa-inf-erroredge';

-- Expected: 4 users + 1 form + 1 flowdef = 6 rows.

-- Verify Connector.Edit / Calendar.Edit ARE granted to RoleId=1 (WorkCalendarConnectorPermissionSeed)
-- but NOT to RoleId=2 in this tenant (the 403 test rests on this):
SELECT ra.RoleId, m.MenuKey, ra.ActionCode
FROM Sys_RoleActions ra
JOIN Sys_Menus m ON m.Id = ra.MenuId
WHERE ra.TenantId = @tenant
  AND ((m.MenuKey = 'oa-flow-admin'  AND ra.ActionCode = 'Connector.Edit')
    OR (m.MenuKey = 'oa-work-calendar' AND ra.ActionCode = 'Calendar.Edit'))
ORDER BY m.MenuKey, ra.RoleId;
-- Expected: Connector.Edit + Calendar.Edit each present for RoleId=1; RoleId=2 absent -> 403.

-- Show DefaultTenant A1's work-calendar row count (A-T2 boot seed plants 35 JP holidays; the ps1
-- empty-state scenario clears and re-imports them):
SELECT COUNT(*) AS a1_workcalendar_rows FROM Sys_WorkCalendar WHERE TenantId = @tenant;
-- Expected: 35 (2026x18 + 2027x17) on a booted DB, unless a prior ps1 run cleared them.
