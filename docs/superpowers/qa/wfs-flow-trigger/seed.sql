-- =====================================================================
-- WFS Event-Trigger Start (F-T3) -- QA seed (SQL Server CP6DB_OA)
-- Branch: feat/wfs-event-trigger
-- Date:   2026-07-13
--
-- Covers spec 2026-07-05-wfs-event-trigger-start-design: timer / event /
--   message trigger start paths + admin backend + validation (E-WF-022/023/024).
--
-- Creates (all in DefaultTenant A1):
--   3 users:
--     qa_wtr_starter   (RoleId=1, Enable=1) -- nominal starter + admin-API caller
--                       (RoleId=1 is granted FlowTrigger.View/Edit by
--                        FlowTriggerPermissionSeed, so this user may create/fire/preview).
--     qa_wtr_approver  (Enable=1)           -- Specified approver of wtr-simple's lone node.
--     qa_wtr_disabled  (Enable=0)           -- disabled; used as the bad StarterUserId of
--                                              wtr-badstarter to force runtime E-WF-022.
--   1 FormDef:  wtr-demo-form (subject text only).
--   2 FlowDefs:
--     wtr-simple        (Enable=1) start -> approval(qa_wtr_approver) -> end  -- fire target.
--     wtr-disabled-flow (Enable=0) same shape                                 -- E-WF-023 fixture.
--   3 raw-INSERTed triggers (raw = bypass FlowTriggerValidator, exactly as the
--     wfs-service-task / wfs-kernel-hardening harnesses seed FlowDefs raw):
--     wtr-event       event  , EventKey='QA|OnEchoAsync',
--                     ConfigJson varsMap {outboundNo:$.OutboundNo}   -- echo-driven scenario 4.
--     wtr-shortcycle  timer  , cron '*/1 * * * *', NextDueUtc in the PAST
--                     (immediately due -> WfTriggerWorker fires within one 30s scan) -- scenario 3.
--     wtr-badstarter  timer  , StarterUserId=qa_wtr_disabled, NextDueUtc=NULL
--                     (NULL => the worker's "NextDueUtc != null && <= now" scan skips it;
--                      only manual-fire touches it) -- scenario 8 human failure row.
--
--   NOTE: message triggers are NOT seeded here. The ps1 CREATEs them through the admin
--   API (POST /api/oa/flow-triggers) so it can capture the one-time plaintext apiKeyPlain
--   (WfApiKeyHelper.NewRawKey; library stores SHA-256 only -- unrecoverable), which is the
--   only way to obtain a usable key. Seeding a message trigger raw would leave no plaintext.
--
-- WHY the timer/event triggers are INSERTed directly (NOT saved through the admin API):
--   FlowTriggerValidator.ValidateAsync runs only through FlowTriggerAdminService.Create/Update.
--   A raw INSERT skips it, which is REQUIRED for wtr-badstarter (a disabled StarterUserId
--   would be rejected E-WF-022 at save) and keeps the seed self-contained / deterministic.
--
-- Condition/mapping semantics (verified against shipped code):
--   - event varsMap value '$.OutboundNo' is a JSONPath-lite dot path resolved by
--     ServiceVarsHelper.ResolveValue over the echo payloadJson; '{"OutboundNo":"OB-1"}'
--     -> VarsJson '{"outboundNo":"OB-1"}' (WfTriggerVarsMapper.MapVars).
--   - timer NextUtc is computed by WfCronHelper (NCrontab 5-field). '*/1 * * * *' = every minute.
--
-- Run from a NATIVE shell (cmd / PowerShell), NOT git-bash:
--     sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
--
-- Notes:
--   - SET QUOTED_IDENTIFIER ON (Wf_FlowDef has filtered unique indexes).
--   - Table names are SINGULAR: Wf_FlowTrigger / Wf_FlowDef / Wf_FormDef.
--   - Wf_FlowTrigger.RowVersion is a rowversion (auto) column -- never inserted.
--   - Idempotent: IF NOT EXISTS guards on every insert.
--   - ConfigJson / node fields are camelCase; parsers use PropertyNameCaseInsensitive=true.
-- =====================================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @tenant uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';  -- DefaultTenant

-- Fixed GUIDs (referenced from FlowDef SchemaJson / trigger rows below -- must match)
DECLARE @u_start    uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000B0';  -- qa_wtr_starter
DECLARE @u_appr     uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000A1';  -- qa_wtr_approver
DECLARE @u_disabled uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000A2';  -- qa_wtr_disabled (Enable=0)

-- Fixed FlowDef / FormDef / trigger GUIDs
DECLARE @form   uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000F1';  -- wtr-demo-form
DECLARE @fSimple  uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000F2';  -- wtr-simple (enabled)
DECLARE @fDisabled uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000F3';  -- wtr-disabled-flow (Enable=0)
DECLARE @tEvent uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000E1';  -- wtr-event
DECLARE @tShort uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000E2';  -- wtr-shortcycle
DECLARE @tBad   uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000E3';  -- wtr-badstarter

-- Reusable single-approval schema (start -> approval(Specified qa_wtr_approver) -> end)
DECLARE @schema nvarchar(max) =
  '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"h1","type":"approval","name":"Approve","approverStrategy":"Specified","approverUserId":"DDDD0000-0000-0000-0000-0000000000A1"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"h1"},{"from":"h1","to":"end"}]}';

-- == 1. Users (Password cloned from admin's BCrypt hash = "123456") ============
-- qa_wtr_starter: RoleId=1 EXPLICIT (FlowTrigger.View/Edit is seeded to RoleId=1 per tenant;
--   PermissionService.HasActionAsync has no admin bypass, so the caller MUST be in role 1).

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_start)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_start, 'qa_wtr_starter', Password, 'QA WTR Starter', 1, 1, 'qa-wtr-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_appr)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_appr, 'qa_wtr_approver', Password, 'QA WTR Approver', 1, 1, 'qa-wtr-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_disabled)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_disabled, 'qa_wtr_disabled', Password, 'QA WTR Disabled Starter', 1, 0, 'qa-wtr-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- == 2. FormDef: wtr-demo-form ================================================

IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'wtr-demo-form')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    @form, 'wtr-demo-form', 'Flow Trigger Demo Form',
    '{"fields":[{"name":"subject","label":"Subject","type":"text","required":false}]}',
    1, 1, 'qa-wtr-seed', GETDATE(), @tenant
  );

-- == 3. FlowDef: wtr-simple (ENABLED -- the fire target) ======================

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'wtr-simple')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (@fSimple, 'wtr-simple', 'WTR Simple Approval', 'wtr-demo-form', @schema, 1, 1, 'qa-wtr-seed', GETDATE(), @tenant);

-- == 4. FlowDef: wtr-disabled-flow (Enable=0 -- E-WF-023 save-validation) =====

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'wtr-disabled-flow')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (@fDisabled, 'wtr-disabled-flow', 'WTR Disabled Flow', 'wtr-demo-form', @schema, 1, 0, 'qa-wtr-seed', GETDATE(), @tenant);

-- == 5. Trigger: wtr-event (event, echo-driven) ===============================
--   EventKey MUST equal the echo body's eventKey exactly (WfTriggerBridgeHook matches on ==).
--   ConfigJson varsMap {outboundNo:$.OutboundNo}: payload {"OutboundNo":"OB-1"} -> VarsJson {"outboundNo":"OB-1"}.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowTrigger WHERE Id = @tEvent)
  INSERT INTO Wf_FlowTrigger (Id, FlowKey, TriggerType, ConfigJson, Enabled, EventKey, StarterUserId,
      NextDueUtc, LastFiredUtc, ApiKeyHash, Creator, CreateDate, TenantId)
  VALUES (@tEvent, 'wtr-simple', 1, '{"varsMap":{"outboundNo":"$.OutboundNo"}}', 1, 'QA|OnEchoAsync', @u_start,
      NULL, NULL, NULL, 'qa-wtr-seed', GETDATE(), @tenant);

-- == 6. Trigger: wtr-shortcycle (timer every minute, immediately due) =========
--   NextDueUtc in the PAST => WfTriggerWorker (30s interval) fires it on the next scan.
--   Requires the backend's WfTriggerWorker hosted service to be running (normal boot).

IF NOT EXISTS (SELECT 1 FROM Wf_FlowTrigger WHERE Id = @tShort)
  INSERT INTO Wf_FlowTrigger (Id, FlowKey, TriggerType, ConfigJson, Enabled, EventKey, StarterUserId,
      NextDueUtc, LastFiredUtc, ApiKeyHash, Creator, CreateDate, TenantId)
  VALUES (@tShort, 'wtr-simple', 0, '{"cron":"*/1 * * * *"}', 1, NULL, @u_start,
      DATEADD(MINUTE, -2, GETUTCDATE()), NULL, NULL, 'qa-wtr-seed', GETDATE(), @tenant);

-- == 7. Trigger: wtr-badstarter (timer, disabled starter -> runtime E-WF-022) ==
--   NextDueUtc=NULL so the worker never auto-scans it; only manual-fire exercises it.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowTrigger WHERE Id = @tBad)
  INSERT INTO Wf_FlowTrigger (Id, FlowKey, TriggerType, ConfigJson, Enabled, EventKey, StarterUserId,
      NextDueUtc, LastFiredUtc, ApiKeyHash, Creator, CreateDate, TenantId)
  VALUES (@tBad, 'wtr-simple', 0, '{"cron":"0 9 * * *"}', 1, NULL, @u_disabled,
      NULL, NULL, NULL, 'qa-wtr-seed', GETDATE(), @tenant);

-- == 8. Sanity report =========================================================

SELECT 'qa_wtr_starter'  AS seeded, CONVERT(varchar(36), Id) AS val FROM Sys_Users WHERE UserName = 'qa_wtr_starter'
UNION ALL SELECT 'qa_wtr_approver', CONVERT(varchar(36), Id) FROM Sys_Users WHERE UserName = 'qa_wtr_approver'
UNION ALL SELECT 'qa_wtr_disabled (Enable=0)', CONVERT(varchar(36), Id) FROM Sys_Users WHERE UserName = 'qa_wtr_disabled'
UNION ALL SELECT 'FormDef:wtr-demo-form', FormKey FROM Wf_FormDef WHERE FormKey = 'wtr-demo-form'
UNION ALL SELECT 'FlowDef:' + FlowKey + ' (Enable=' + CONVERT(varchar(1), Enable) + ')', CONVERT(varchar(36), Id)
  FROM Wf_FlowDef WHERE FlowKey IN ('wtr-simple', 'wtr-disabled-flow')
UNION ALL SELECT 'Trigger:' + FlowKey + ' type=' + CONVERT(varchar(1), TriggerType), CONVERT(varchar(36), Id)
  FROM Wf_FlowTrigger WHERE Id IN (@tEvent, @tShort, @tBad);

-- Expected: 3 users + 1 form + 2 flowdefs + 3 triggers = 9 rows.
