-- =====================================================================
-- WFS Service Task (E-T3) -- QA seed (SQL Server CP6DB_OA)
-- Branch: feat/wfs-service-task-finish
-- Date:   2026-07-05
--
-- Creates:
--   2 users  : qa_svc_starter (submits every scenario)
--              qa_svc_appr    (error-branch human approver, scenario 5)
--   1 FormDef: svc-demo-form  (subject text + amount number; amount fuels dataWriteback)
--   6 FlowDef: one per QA scenario (see table below)
--
--   FlowKey               Scenario                                   serviceKind / mode        action
--   --------------------  -----------------------------------------  ------------------------  ---------------------------
--   svc-sync-writeback    1. sync dataWriteback -> Approved           dataWriteback / sync      sampleWriteback
--   svc-async-webapi      2. async webApi -> job -> worker -> Approved webApi / async           erpEcho  (path /erp/echo/{amount})
--   svc-timer-wait        3. timer pure wait (PT10S) -> advance        timer  / async(forced)   none
--   svc-timer-action      4. timer + webApi action -> advance          timer  / async(forced)   erpEcho  (path /erp/echo/{amount})
--   svc-fail-erroredge    5. fail -> retry exhausted -> IsError edge    webApi / async          GHOST connector (unregistered)
--   svc-fail-suspend      6. fail -> retry exhausted -> Suspend         webApi / async          GHOST connector (unregistered)
--
-- WHY the FlowDefs are INSERTed directly (NOT saved through the designer):
--   Scenarios 5 & 6 reference a connector that is NOT registered
--   ("ghostConnector"). DesignerService.SaveAsync runs a save-time
--   registration check (CP6.Core/Services/Oa/DesignerService.cs) that throws
--   E-WF-018 when a webApi node's ServiceConnectorName is not a registered
--   IWfConnector.Name. That check would REJECT the failure flows at save time,
--   so we bypass the designer entirely and INSERT the FlowDef rows straight
--   into Wf_FlowDef. The runtime E-WF-018 ("connector not registered") is
--   exactly the failure we want the async worker to hit -> retry -> route.
--   For consistency all 6 flows are seeded the same way (raw INSERT).
--   FlowSchemaValidator is NOT invoked on this path (it runs only through
--   DesignerService.SaveAsync), so the seeded schemas stand as written.
--
-- Retry tuning for the failure flows:
--   serviceMaxRetries=0  -> job.MaxAttempts = retries + 1 = 1.
--   An async job enqueues with AttemptCount=0; the first worker scan does
--   AttemptCount++ (=1), fails, and 1 < 1 is false -> retries exhausted on the
--   FIRST scan -> immediate route (no 30s backoff wait). Keeps live QA fast.
--
-- Run from a NATIVE shell (cmd / PowerShell), NOT git-bash:
--     sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
--
-- Notes:
--   - SET QUOTED_IDENTIFIER ON is required: Wf_FlowDef has filtered unique indexes.
--   - Table names are SINGULAR: Wf_FlowDef / Wf_FormDef (plural names翻车 f90a138).
--   - Both users clone admin's BCrypt hash (= password "123456").
--   - Idempotent: IF NOT EXISTS guards on every insert.
--   - Node schema fields are camelCase (serviceKind/serviceMode/serviceConnectorName/
--     serviceActionName/servicePath/serviceParamsJson/serviceDelayMode/
--     serviceDelayValue/serviceMaxRetries); edge failure flag is isError.
--     FlowEngine deserialises with PropertyNameCaseInsensitive=true.
-- =====================================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @tenant uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';  -- DefaultTenant

-- Fixed GUIDs (referenced from FlowDef SchemaJson below -- must match)
DECLARE @u_start uniqueidentifier = 'CCCC0000-0000-0000-0000-0000000000B0';  -- qa_svc_starter (submits all flows)
DECLARE @u_appr  uniqueidentifier = 'CCCC0000-0000-0000-0000-0000000000A0';  -- qa_svc_appr    (error-branch approver)

-- ── 1. Users (clone admin's BCrypt hash = password "123456") ────────────────

-- qa_svc_starter: submits every scenario instance
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_start)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_start, 'qa_svc_starter', Password, 'QA Service Starter', RoleId, 1, 'qa-svctask-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- qa_svc_appr: fixed approver of the error-branch human node (scenario 5)
IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_appr)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_appr, 'qa_svc_appr', Password, 'QA Error-Branch Approver', RoleId, 1, 'qa-svctask-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- ── 2. FormDef: svc-demo-form (amount feeds sampleWriteback / erpEcho path) ──

IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'svc-demo-form')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F1',
    'svc-demo-form',
    'Service Task Demo Form',
    '{"fields":[{"name":"subject","label":"Subject","type":"text","required":true},{"name":"amount","label":"Amount","type":"number","required":true}]}',
    1, 1, 'qa-svctask-seed', GETDATE(), @tenant
  );

-- ── 3. FlowDef #1: svc-sync-writeback ───────────────────────────────────────
--   start s -> serviceTask w1 (dataWriteback, sync, sampleWriteback) -> end
--   Submit -> executor runs INLINE at submit time, merges writebackEcho into
--   VarsJson, AdvanceToken -> end -> instance Approved (status 1) immediately.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'svc-sync-writeback')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F2',
    'svc-sync-writeback',
    'SVC 1 - Sync DataWriteback',
    'svc-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"w1","type":"serviceTask","name":"Sync Writeback","serviceKind":"dataWriteback","serviceMode":"sync","serviceActionName":"sampleWriteback"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"w1"},{"from":"w1","to":"end"}]}',
    1, 1, 'qa-svctask-seed', GETDATE(), @tenant
  );

-- ── 4. FlowDef #2: svc-async-webapi ─────────────────────────────────────────
--   start s -> serviceTask w1 (webApi, async, erpEcho, path /erp/echo/{amount}) -> end
--   Submit -> token parked + 1 Pending Wf_ServiceJob. Worker (20s scan) leases,
--   runs erpEcho (echoes path), resumes token -> end -> instance Approved.
--   VarsJson gains echoedPath + idempotencyKey.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'svc-async-webapi')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F3',
    'svc-async-webapi',
    'SVC 2 - Async WebApi (erpEcho)',
    'svc-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"w1","type":"serviceTask","name":"Async WebApi","serviceKind":"webApi","serviceMode":"async","serviceConnectorName":"erpEcho","servicePath":"/erp/echo/{amount}"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"w1"},{"from":"w1","to":"end"}]}',
    1, 1, 'qa-svctask-seed', GETDATE(), @tenant
  );

-- ── 5. FlowDef #3: svc-timer-wait ───────────────────────────────────────────
--   start s -> serviceTask t1 (timer, duration PT10S, no action -> actionKind none) -> end
--   Submit -> token parked + Pending job with DueAtUtc = now + 10s. After the due
--   time a worker scan resumes the token (none = pure wait) -> end -> Approved.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'svc-timer-wait')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F4',
    'svc-timer-wait',
    'SVC 3 - Timer Pure Wait (PT10S)',
    'svc-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"t1","type":"serviceTask","name":"Timer Wait","serviceKind":"timer","serviceDelayMode":"duration","serviceDelayValue":"PT10S"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"t1"},{"from":"t1","to":"end"}]}',
    1, 1, 'qa-svctask-seed', GETDATE(), @tenant
  );

-- ── 6. FlowDef #4: svc-timer-action ─────────────────────────────────────────
--   start s -> serviceTask t1 (timer, duration PT10S, erpEcho -> actionKind webApi) -> end
--   Timer + ConnectorName => ActionRef.ActionKind = "webApi" (ServiceTaskActionRef.Snapshot).
--   At due time the worker runs erpEcho then advances -> Approved. VarsJson gains echoedPath.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'svc-timer-action')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F5',
    'svc-timer-action',
    'SVC 4 - Timer + WebApi Action (PT10S)',
    'svc-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"t1","type":"serviceTask","name":"Timer + Echo","serviceKind":"timer","serviceDelayMode":"duration","serviceDelayValue":"PT10S","serviceConnectorName":"erpEcho","servicePath":"/erp/echo/{amount}"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"t1"},{"from":"t1","to":"end"}]}',
    1, 1, 'qa-svctask-seed', GETDATE(), @tenant
  );

-- ── 7. FlowDef #5: svc-fail-erroredge ───────────────────────────────────────
--   start s -> serviceTask f1 (webApi, async, GHOST connector, serviceMaxRetries 0)
--     success edge  f1 -> end
--     error   edge  f1 -> h1  (isError:true)
--   h1 (approval, Specified = qa_svc_appr) -> end
--   Worker: connector unregistered -> Fail("E-WF-018 ...") -> maxAttempts=1 exhausted
--     on first scan -> FailServiceTokenAsync writes wf.serviceError into VarsJson and
--     routes along the IsError edge to h1. Instance stays Running; qa_svc_appr gets a
--     Pending task. GHOST connector is only reachable because this flow was INSERTed
--     directly (designer save would have thrown E-WF-018 -- see header).

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'svc-fail-erroredge')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F6',
    'svc-fail-erroredge',
    'SVC 5 - Fail -> Error Edge -> Human',
    'svc-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"f1","type":"serviceTask","name":"Failing WebApi","serviceKind":"webApi","serviceMode":"async","serviceConnectorName":"ghostConnector","servicePath":"/nope","serviceMaxRetries":0},{"id":"h1","type":"approval","name":"Handle Failure","approverStrategy":"Specified","approverUserId":"CCCC0000-0000-0000-0000-0000000000A0","countersign":"all"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"f1"},{"from":"f1","to":"end"},{"from":"f1","to":"h1","isError":true},{"from":"h1","to":"end"}]}',
    1, 1, 'qa-svctask-seed', GETDATE(), @tenant
  );

-- ── 8. FlowDef #6: svc-fail-suspend ─────────────────────────────────────────
--   start s -> serviceTask f1 (webApi, async, GHOST connector, serviceMaxRetries 0) -> end
--   NO IsError edge. Worker: connector unregistered -> Fail -> exhausted on first scan
--   -> FailServiceTokenAsync writes wf.serviceError, finds no error edge -> Suspend.
--   Instance status becomes Suspended (4). VarsJson gains wf.serviceError.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'svc-fail-suspend')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F7',
    'svc-fail-suspend',
    'SVC 6 - Fail -> Suspend (no error edge)',
    'svc-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"f1","type":"serviceTask","name":"Failing WebApi","serviceKind":"webApi","serviceMode":"async","serviceConnectorName":"ghostConnector","servicePath":"/nope","serviceMaxRetries":0},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"f1"},{"from":"f1","to":"end"}]}',
    1, 1, 'qa-svctask-seed', GETDATE(), @tenant
  );

-- ── 9. Sanity report ────────────────────────────────────────────────────────

SELECT 'qa_svc_starter' AS seeded, CONVERT(varchar(36), Id) AS val FROM Sys_Users  WHERE UserName = 'qa_svc_starter'
UNION ALL
SELECT 'qa_svc_appr',              CONVERT(varchar(36), Id)        FROM Sys_Users  WHERE UserName = 'qa_svc_appr'
UNION ALL
SELECT 'svc-demo-form (FormDef)',  FormKey                         FROM Wf_FormDef WHERE FormKey = 'svc-demo-form'
UNION ALL
SELECT 'FlowDef:' + FlowKey,       CONVERT(varchar(36), Id)        FROM Wf_FlowDef
  WHERE FlowKey IN ('svc-sync-writeback','svc-async-webapi','svc-timer-wait','svc-timer-action','svc-fail-erroredge','svc-fail-suspend');

-- Expected: 2 users + 1 form + 6 flowdefs = 9 rows.
