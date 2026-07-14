-- =====================================================================
-- WFS Sub-flow (F-T2) -- QA seed (SQL Server CP6DB_OA)
-- Branch: feat/wfs-subflow
-- Date:   2026-07-14
--
-- Covers spec: sub-flow call-activity (subFlow node, 9th handler) + two-phase
--   park/resume (subFlowResume credential, TokenId=child instance Id,
--   NodeId="$subFlowResume") + multi-instance (subCollectionVar expand, all/any,
--   ordered array write-back by SubIndex) + cascade (all any-reject / any first-pass
--   withdraws the rest) + onBranchReject=prune composition + designer E-WF-025/026.
--
-- Creates:
--   1 starter + 3 approvers (parent / child / combo-B), all cloning admin's BCrypt
--     hash (= password "123456") and admin's RoleId (submit needs oa-form-catalog:submit).
--   1 FormDef: sf-demo-form (subject only; sub-flow reads submit vars, not form fields).
--   5 FlowDefs -- 1 child + 4 parents, one topology per scenario cluster (see table).
--
--   FlowKey            Scenarios   Topology
--   -----------------  ----------  -----------------------------------------------------
--   sf-child-approve   1,3,4,5,6   start -> ca(approval:sf_child) -> ce(end)   [the child]
--   sf-parent-single   1,2,8       start -> sub(subFlow child, single, all)
--                                    -> pa(approval:sf_parent) -> pe(end)
--   sf-multi-all       3,4         start -> sub(subFlow child, multi items, all,
--                                    varsOut results<-$.item) -> pa -> pe
--   sf-multi-any       5           start -> sub(subFlow child, multi items, any) -> pa -> pe
--   sf-combo-prune     6           start -> split(parallelSplit onBranchReject=prune)
--                                    -> [sub(subFlow child) | bAppr(approval:sf_b)]
--                                    -> join(parallelJoin) -> e(end)
--
-- WHY the FlowDefs are INSERTed directly (NOT saved through the designer):
--   Same principle as the wfs-kernel-hardening / wfs-service-task harnesses:
--   FlowSchemaValidator + SubFlowRefValidator run ONLY through DesignerService.SaveAsync.
--   A raw INSERT skips them, so the seeded schemas stand exactly as written. All five
--   flows here are in fact valid (they would also pass E-WF-025/026); they are seeded
--   raw for consistency with the established QA-harness pattern and to keep the seed
--   self-contained (no designer round-trip, no reference-order dependency).
--
-- Run from a NATIVE shell (cmd / PowerShell), NOT git-bash:
--     sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
--
-- Notes:
--   - SET QUOTED_IDENTIFIER ON is required (Wf_FlowDef has filtered unique indexes:
--     (TenantId,FunctionId) WHERE FunctionId IS NOT NULL and (TenantId,FlowCode)
--     WHERE FlowCode IS NOT NULL, CP6Context.cs).
--   - Table names are SINGULAR: Wf_FlowDef / Wf_FormDef.
--   - Idempotent: IF NOT EXISTS guards on every insert.
--   - RowVersion is NOT inserted (SQL Server rowversion is auto-generated;
--     Wf_FlowInstance.RowVersion likewise -- no flow instances are seeded here, they
--     are created at runtime via /api/wf/flow/submit).
--   - Schema fields are camelCase (type/approverStrategy/approverUserId/subFlowKey/
--     subCompletionPolicy/subCollectionVar/subVarsOutJson/onBranchReject); FlowEngine
--     deserialises PropertyNameCaseInsensitive=true. subVarsOutJson is a STRING field
--     holding an escaped JSON map, so its inner quotes are backslash-escaped.
-- =====================================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @tenant uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';  -- DefaultTenant A1

-- Fixed user GUIDs (referenced from FlowDef SchemaJson below -- must match)
DECLARE @u_start  uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000B0';  -- sf_starter  (submits all parents; is the child instances' starter too)
DECLARE @u_parent uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000A1';  -- sf_parent   (approves the parent-side approval node 'pa')
DECLARE @u_child  uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000A2';  -- sf_child    (approves/rejects the child approval node 'ca')
DECLARE @u_b      uniqueidentifier = 'DDDD0000-0000-0000-0000-0000000000A3';  -- sf_b        (approves the combo sibling branch 'bAppr')

-- ---------------------------------------------------------------------------
-- 1. Users (clone admin's BCrypt hash = password "123456" and admin's RoleId)
-- ---------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_start)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_start, 'sf_starter', Password, 'QA SF Starter', RoleId, 1, 'qa-sf-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_parent)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_parent, 'sf_parent', Password, 'QA SF Parent Approver', RoleId, 1, 'qa-sf-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_child)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_child, 'sf_child', Password, 'QA SF Child Approver', RoleId, 1, 'qa-sf-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_b)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_b, 'sf_b', Password, 'QA SF Combo Branch B', RoleId, 1, 'qa-sf-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- ---------------------------------------------------------------------------
-- 2. FormDef: sf-demo-form (subject only; sub-flow reads submit vars items/subject)
-- ---------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'sf-demo-form')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F0',
    'sf-demo-form',
    'Sub-flow Demo Form',
    '{"fields":[{"name":"subject","label":"Subject","type":"text","required":true}]}',
    1, 1, 'qa-sf-seed', GETDATE(), @tenant
  );

-- ---------------------------------------------------------------------------
-- 3. FlowDef: sf-child-approve  (the child; used by every parent below)
--    start -> ca(approval sf_child) -> ce(end)
--    Multi-instance runtime injects item/itemIndex into each child's vars, so
--    the out-map "$.item" resolves per child (scenario 3 array write-back).
-- ---------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'sf-child-approve')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F1',
    'sf-child-approve',
    'SF Child - single approval',
    'sf-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"ca","type":"approval","name":"Child Approval","approverStrategy":"Specified","approverUserId":"DDDD0000-0000-0000-0000-0000000000A2"},{"id":"ce","type":"end","name":"End"}],"edges":[{"from":"s","to":"ca"},{"from":"ca","to":"ce"}]}',
    1, 1, 'qa-sf-seed', GETDATE(), @tenant
  );

-- ---------------------------------------------------------------------------
-- 4. FlowDef: sf-parent-single  (scenarios 1, 2, 8)
--    start -> sub(subFlow child, single instance, policy all) -> pa(approval sf_parent) -> pe(end)
--    S1: submit -> 1 child at 'ca'; approve child -> parent resumes to 'pa' (fast path,
--        <2s); approve pa -> Approved.
--    S2: browser interlink walkthrough reuses an S1 instance.
--    S8: withdraw the CHILD instance (starter) while it is at 'ca' -> parent all-policy
--        sees child Withdrawn (dead) -> subFlowError -> no error edge, no ForkId -> parent
--        Rejected. Proves TaskCenterService.WithdrawAsync fast path (DI scoped FlowEngine).
-- ---------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'sf-parent-single')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F2',
    'sf-parent-single',
    'SF Parent - single sub-flow',
    'sf-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"sub","type":"subFlow","name":"Sub Call","subFlowKey":"sf-child-approve","subCompletionPolicy":"all"},{"id":"pa","type":"approval","name":"Parent Approval","approverStrategy":"Specified","approverUserId":"DDDD0000-0000-0000-0000-0000000000A1"},{"id":"pe","type":"end","name":"End"}],"edges":[{"from":"s","to":"sub"},{"from":"sub","to":"pa"},{"from":"pa","to":"pe"}]}',
    1, 1, 'qa-sf-seed', GETDATE(), @tenant
  );

-- ---------------------------------------------------------------------------
-- 5. FlowDef: sf-multi-all  (scenarios 3, 4)
--    start -> sub(subFlow child, multi over "items", policy all,
--             subVarsOutJson results<-$.item) -> pa(approval sf_parent) -> pe(end)
--    S3: submit {items:[itemA,itemB,itemC]} -> 3 children; approve OUT OF ORDER ->
--        parent resumes, results = ["itemA","itemB","itemC"] ordered by SubIndex.
--    S4: submit 3 children; reject one child -> the other in-flight children are
--        cascade-withdrawn, parent Rejected, currentDataJson carries subFlowError.
-- ---------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'sf-multi-all')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F3',
    'sf-multi-all',
    'SF Parent - multi instance ALL',
    'sf-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"sub","type":"subFlow","name":"Sub Multi All","subFlowKey":"sf-child-approve","subCompletionPolicy":"all","subCollectionVar":"items","subVarsOutJson":"{\"results\":\"$.item\"}"},{"id":"pa","type":"approval","name":"Parent Approval","approverStrategy":"Specified","approverUserId":"DDDD0000-0000-0000-0000-0000000000A1"},{"id":"pe","type":"end","name":"End"}],"edges":[{"from":"s","to":"sub"},{"from":"sub","to":"pa"},{"from":"pa","to":"pe"}]}',
    1, 1, 'qa-sf-seed', GETDATE(), @tenant
  );

-- ---------------------------------------------------------------------------
-- 6. FlowDef: sf-multi-any  (scenario 5)
--    start -> sub(subFlow child, multi over "items", policy any) -> pa -> pe
--    S5: submit 3 children; approve the FIRST one -> parent resumes, the other two
--        in-flight children are cascade-withdrawn; approve pa -> Approved.
-- ---------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'sf-multi-any')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F4',
    'sf-multi-any',
    'SF Parent - multi instance ANY',
    'sf-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"sub","type":"subFlow","name":"Sub Multi Any","subFlowKey":"sf-child-approve","subCompletionPolicy":"any","subCollectionVar":"items"},{"id":"pa","type":"approval","name":"Parent Approval","approverStrategy":"Specified","approverUserId":"DDDD0000-0000-0000-0000-0000000000A1"},{"id":"pe","type":"end","name":"End"}],"edges":[{"from":"s","to":"sub"},{"from":"sub","to":"pa"},{"from":"pa","to":"pe"}]}',
    1, 1, 'qa-sf-seed', GETDATE(), @tenant
  );

-- ---------------------------------------------------------------------------
-- 7. FlowDef: sf-combo-prune  (scenario 6)
--    start -> split(parallelSplit onBranchReject=prune)
--          -> [ sub(subFlow child, single, all) -> join ]
--          -> [ bAppr(approval sf_b)            -> join ]
--          -> join(parallelJoin) -> e(end)
--    S6: reject the sub-flow's child -> subFlowError with the parked token carrying a
--        ForkId -> TryPruneBranchAsync prunes ONLY that branch; sibling bAppr continues;
--        approve bAppr -> join dyn-counts (pruned drops) -> Approved.
-- ---------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'sf-combo-prune')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'DDDD0000-0000-0000-0000-0000000000F5',
    'sf-combo-prune',
    'SF Parent - parallel + prune + sub-flow',
    'sf-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"split","type":"parallelSplit","name":"Split (prune)","onBranchReject":"prune"},{"id":"sub","type":"subFlow","name":"Sub Branch","subFlowKey":"sf-child-approve","subCompletionPolicy":"all"},{"id":"bAppr","type":"approval","name":"Branch B","approverStrategy":"Specified","approverUserId":"DDDD0000-0000-0000-0000-0000000000A3"},{"id":"join","type":"parallelJoin","name":"Join"},{"id":"e","type":"end","name":"End"}],"edges":[{"from":"s","to":"split"},{"from":"split","to":"sub"},{"from":"split","to":"bAppr"},{"from":"sub","to":"join"},{"from":"bAppr","to":"join"},{"from":"join","to":"e"}]}',
    1, 1, 'qa-sf-seed', GETDATE(), @tenant
  );

-- ---------------------------------------------------------------------------
-- 8. Sanity report
-- ---------------------------------------------------------------------------

SELECT 'sf_starter' AS seeded, CONVERT(varchar(36), Id) AS val FROM Sys_Users WHERE UserName = 'sf_starter'
UNION ALL SELECT 'sf_parent', CONVERT(varchar(36), Id) FROM Sys_Users WHERE UserName = 'sf_parent'
UNION ALL SELECT 'sf_child',  CONVERT(varchar(36), Id) FROM Sys_Users WHERE UserName = 'sf_child'
UNION ALL SELECT 'sf_b',      CONVERT(varchar(36), Id) FROM Sys_Users WHERE UserName = 'sf_b'
UNION ALL SELECT 'sf-demo-form (FormDef)', FormKey FROM Wf_FormDef WHERE FormKey = 'sf-demo-form'
UNION ALL SELECT 'FlowDef:' + FlowKey, CONVERT(varchar(36), Id) FROM Wf_FlowDef
  WHERE FlowKey IN ('sf-child-approve','sf-parent-single','sf-multi-all','sf-multi-any','sf-combo-prune');

-- Expected: 4 users + 1 form + 5 flowdefs = 10 rows.
