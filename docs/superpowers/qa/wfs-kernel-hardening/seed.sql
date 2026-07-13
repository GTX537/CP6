-- =====================================================================
-- WFS Kernel Hardening (E-T2) -- QA seed (SQL Server CP6DB_OA)
-- Branch: feat/wfs-kernel-hardening
-- Date:   2026-07-13
--
-- Covers spec: inclusive gateway (inclusiveSplit/inclusiveJoin) + branch-reject
--   policy (onBranchReject prune|cascade) + send-back three-rule matrix
--   (SameBranch / SiblingBranch=E-WF-019 / BeforeSplit).
--
-- Creates:
--   1 starter + 4 branch approvers (a/b/c/d), all cloning admin's BCrypt hash
--     (= password "123456").
--   1 FormDef: khd-demo-form (subject text only; inclusive conditions read the
--     submit vars goA/goB/goC, NOT form fields, so no numeric field is required).
--   4 FlowDefs, one topology per scenario cluster (see table).
--
--   FlowKey          Scenarios            Topology
--   ---------------  -------------------  ------------------------------------------
--   khd-inclusive    1 (2/3 real edges),  inclusiveSplit -[goA>0]->a -[goB>0]->b
--                    2 (all-false ->      -[goC>0]->c -[default]->d ; all -> inclusiveJoin
--                       default fallback)
--   khd-prune        3 (prune one branch) parallelSplit(onBranchReject=prune) (a,b) -> parallelJoin
--   khd-cascade      4 (cascade default)  parallelSplit (a,b) -> parallelJoin  (NO onBranchReject)
--   khd-sameback     5 (SameBranch back), parallelSplit (a1->a2 , b1) -> parallelJoin
--                    6 (SiblingBranch=E-WF-019)
--
-- WHY the FlowDefs are INSERTed directly (NOT saved through the designer):
--   Same principle as the wfs-service-task harness: FlowSchemaValidator runs ONLY
--   through DesignerService.SaveAsync. A raw INSERT skips it, so the seeded schemas
--   stand as written. All four flows here are in fact valid (they would also pass
--   E-WF-020/021), but we seed by raw INSERT for consistency with the established
--   QA-harness pattern and to keep the seed self-contained (no designer round-trip).
--
-- Condition semantics (ExpressionEvaluator, CP6.Core/Services/Wf/ExpressionEvaluator.cs):
--   - Edge "condition" reads flow vars (submit varsJson). Unknown field -> safe-fail
--     FALSE. Empty/absent condition = the DEFAULT edge (unconditional).
--   - inclusiveSplit: activates every conditional edge whose condition is TRUE; if
--     NONE are true it activates the single default edge (E-WF-020 guarantees exactly
--     one default). Each activated edge spawns a same-ForkId child token; inclusiveJoin
--     counts dynamically over the activated set.
--
-- Run from a NATIVE shell (cmd / PowerShell), NOT git-bash:
--     sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql
--
-- Notes:
--   - SET QUOTED_IDENTIFIER ON is required (Wf_FlowDef has filtered unique indexes).
--   - Table names are SINGULAR: Wf_FlowDef / Wf_FormDef.
--   - Idempotent: IF NOT EXISTS guards on every insert.
--   - Node/edge schema fields are camelCase (approverStrategy/approverUserId/
--     condition/onBranchReject); FlowEngine deserialises PropertyNameCaseInsensitive=true.
-- =====================================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @tenant uniqueidentifier = '00000000-0000-0000-0000-0000000000A1';  -- DefaultTenant

-- Fixed user GUIDs (referenced from FlowDef SchemaJson below -- must match)
DECLARE @u_start uniqueidentifier = 'EEEE0000-0000-0000-0000-0000000000B0';  -- qa_khd_starter (submits all flows)
DECLARE @u_a     uniqueidentifier = 'EEEE0000-0000-0000-0000-0000000000A1';  -- branch A / a1 / prune-a
DECLARE @u_b     uniqueidentifier = 'EEEE0000-0000-0000-0000-0000000000A2';  -- branch B / b1 / prune-b
DECLARE @u_c     uniqueidentifier = 'EEEE0000-0000-0000-0000-0000000000A3';  -- branch C / a2 (send-back inner)
DECLARE @u_d     uniqueidentifier = 'EEEE0000-0000-0000-0000-0000000000A4';  -- default branch D

-- ── 1. Users (clone admin's BCrypt hash = password "123456") ────────────────

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_start)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_start, 'qa_khd_starter', Password, 'QA KHD Starter', RoleId, 1, 'qa-khd-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_a)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_a, 'qa_khd_a', Password, 'QA KHD Branch A', RoleId, 1, 'qa-khd-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_b)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_b, 'qa_khd_b', Password, 'QA KHD Branch B', RoleId, 1, 'qa-khd-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_c)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_c, 'qa_khd_c', Password, 'QA KHD Branch C / a2', RoleId, 1, 'qa-khd-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

IF NOT EXISTS (SELECT 1 FROM Sys_Users WHERE Id = @u_d)
  INSERT INTO Sys_Users (Id, UserName, Password, NickName, RoleId, Enable, Creator, CreateDate,
      TenantId, FailedLoginCount, MustChangePassword, AllowPasswordFallback, TwoFactorEnabled, IsPlatformAdmin, PasswordChangedAt)
  SELECT @u_d, 'qa_khd_d', Password, 'QA KHD Default Branch', RoleId, 1, 'qa-khd-seed', GETDATE(),
      @tenant, 0, 0, 0, 0, 0, GETDATE()
  FROM Sys_Users WHERE UserName = 'admin';

-- ── 2. FormDef: khd-demo-form (subject only; conditions read submit vars) ────

IF NOT EXISTS (SELECT 1 FROM Wf_FormDef WHERE FormKey = 'khd-demo-form')
  INSERT INTO Wf_FormDef (Id, FormKey, FormName, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'EEEE0000-0000-0000-0000-0000000000F1',
    'khd-demo-form',
    'Kernel Hardening Demo Form',
    '{"fields":[{"name":"subject","label":"Subject","type":"text","required":true}]}',
    1, 1, 'qa-khd-seed', GETDATE(), @tenant
  );

-- ── 3. FlowDef #1: khd-inclusive (scenarios 1 & 2) ──────────────────────────
--   inclusiveSplit -> a[goA>0] / b[goB>0] / c[goC>0] / d[default] -> inclusiveJoin -> end
--   Scenario 1 vars {"goA":1,"goB":1}: edges a,b true (c false: goC unknown -> safe-fail
--     FALSE; default d NOT taken because >=1 conditional edge is true). Exactly 2 todos
--     (qa_khd_a, qa_khd_b); qa_khd_c & qa_khd_d get NONE. Both approve -> inclusiveJoin
--     dynamic-counts 2 -> instance Approved.
--   Scenario 2 vars {} (or all-zero): every conditional edge false -> DEFAULT d activated
--     only; sole todo qa_khd_d; approve -> Approved.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'khd-inclusive')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'EEEE0000-0000-0000-0000-0000000000F2',
    'khd-inclusive',
    'KHD 1/2 - Inclusive Split/Join',
    'khd-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"isplit","type":"inclusiveSplit","name":"Inclusive Split"},{"id":"a","type":"approval","name":"Branch A","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000A1"},{"id":"b","type":"approval","name":"Branch B","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000A2"},{"id":"c","type":"approval","name":"Branch C","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000A3"},{"id":"d","type":"approval","name":"Default Branch","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000A4"},{"id":"ijoin","type":"inclusiveJoin","name":"Inclusive Join"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"isplit"},{"from":"isplit","to":"a","condition":"goA > 0"},{"from":"isplit","to":"b","condition":"goB > 0"},{"from":"isplit","to":"c","condition":"goC > 0"},{"from":"isplit","to":"d"},{"from":"a","to":"ijoin"},{"from":"b","to":"ijoin"},{"from":"c","to":"ijoin"},{"from":"d","to":"ijoin"},{"from":"ijoin","to":"end"}]}',
    1, 1, 'qa-khd-seed', GETDATE(), @tenant
  );

-- ── 4. FlowDef #2: khd-prune (scenario 3) ───────────────────────────────────
--   parallelSplit(onBranchReject=prune) -> a / b -> parallelJoin -> end
--   qa_khd_a rejects -> a token Pruned, instance STAYS Running, qa_khd_b todo alive,
--   starter (qa_khd_starter) gets a BranchPruned notification (Wf_Notification.Type=5).
--   qa_khd_b approves -> parallelJoin dynamic-counts (Pruned drops out of wait set) -> Approved.

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'khd-prune')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'EEEE0000-0000-0000-0000-0000000000F3',
    'khd-prune',
    'KHD 3 - Parallel + onBranchReject=prune',
    'khd-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"split","type":"parallelSplit","name":"Split (prune)","onBranchReject":"prune"},{"id":"a","type":"approval","name":"Branch A","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000A1"},{"id":"b","type":"approval","name":"Branch B","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000A2"},{"id":"join","type":"parallelJoin","name":"Join"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"split"},{"from":"split","to":"a"},{"from":"split","to":"b"},{"from":"a","to":"join"},{"from":"b","to":"join"},{"from":"join","to":"end"}]}',
    1, 1, 'qa-khd-seed', GETDATE(), @tenant
  );

-- ── 5. FlowDef #3: khd-cascade (scenario 4) ─────────────────────────────────
--   parallelSplit (NO onBranchReject) -> a / b -> parallelJoin -> end
--   qa_khd_a rejects -> whole instance Rejected, qa_khd_b todo Voided/Cancelled.
--   Bit-for-bit equal to today's behaviour (ParallelGatewayTests.Parallel_RejectTerminates).

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'khd-cascade')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'EEEE0000-0000-0000-0000-0000000000F4',
    'khd-cascade',
    'KHD 4 - Parallel cascade (default reject)',
    'khd-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"split","type":"parallelSplit","name":"Split (cascade)"},{"id":"a","type":"approval","name":"Branch A","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000A1"},{"id":"b","type":"approval","name":"Branch B","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000A2"},{"id":"join","type":"parallelJoin","name":"Join"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"split"},{"from":"split","to":"a"},{"from":"split","to":"b"},{"from":"a","to":"join"},{"from":"b","to":"join"},{"from":"join","to":"end"}]}',
    1, 1, 'qa-khd-seed', GETDATE(), @tenant
  );

-- ── 6. FlowDef #4: khd-sameback (scenarios 5 & 6) ───────────────────────────
--   parallelSplit -> (a1 -> a2 , b1) -> parallelJoin -> end
--     a1=qa_khd_a, a2=qa_khd_c, b1=qa_khd_b.
--   Scenario 5 (SameBranch): approve a1 -> a2 todo; a2 send-back to a1 -> only the A
--     branch is stripped (b1 survives, reborn token keeps ForkId so join still recognises
--     kin); rerun a1,a2 + approve b1 -> Approved.
--   Scenario 6 (SiblingBranch): at a2, send-back TargetNodeId=b1 -> HTTP 400 "E-WF-019"
--     and nothing mutates (verify-before-write).

IF NOT EXISTS (SELECT 1 FROM Wf_FlowDef WHERE FlowKey = 'khd-sameback')
  INSERT INTO Wf_FlowDef (Id, FlowKey, FlowName, FormKey, SchemaJson, Version, Enable, Creator, CreateDate, TenantId)
  VALUES (
    'EEEE0000-0000-0000-0000-0000000000F5',
    'khd-sameback',
    'KHD 5/6 - Parallel send-back (SameBranch / Sibling E-WF-019)',
    'khd-demo-form',
    '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"split","type":"parallelSplit","name":"Split"},{"id":"a1","type":"approval","name":"A1","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000A1"},{"id":"a2","type":"approval","name":"A2","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000A3"},{"id":"b1","type":"approval","name":"B1","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000A2"},{"id":"join","type":"parallelJoin","name":"Join"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"split"},{"from":"split","to":"a1"},{"from":"a1","to":"a2"},{"from":"split","to":"b1"},{"from":"a2","to":"join"},{"from":"b1","to":"join"},{"from":"join","to":"end"}]}',
    1, 1, 'qa-khd-seed', GETDATE(), @tenant
  );

-- ── 7. Sanity report ────────────────────────────────────────────────────────

SELECT 'qa_khd_starter' AS seeded, CONVERT(varchar(36), Id) AS val FROM Sys_Users  WHERE UserName = 'qa_khd_starter'
UNION ALL SELECT 'qa_khd_a', CONVERT(varchar(36), Id) FROM Sys_Users WHERE UserName = 'qa_khd_a'
UNION ALL SELECT 'qa_khd_b', CONVERT(varchar(36), Id) FROM Sys_Users WHERE UserName = 'qa_khd_b'
UNION ALL SELECT 'qa_khd_c', CONVERT(varchar(36), Id) FROM Sys_Users WHERE UserName = 'qa_khd_c'
UNION ALL SELECT 'qa_khd_d', CONVERT(varchar(36), Id) FROM Sys_Users WHERE UserName = 'qa_khd_d'
UNION ALL SELECT 'khd-demo-form (FormDef)', FormKey FROM Wf_FormDef WHERE FormKey = 'khd-demo-form'
UNION ALL SELECT 'FlowDef:' + FlowKey, CONVERT(varchar(36), Id) FROM Wf_FlowDef
  WHERE FlowKey IN ('khd-inclusive','khd-prune','khd-cascade','khd-sameback');

-- Expected: 5 users + 1 form + 4 flowdefs = 10 rows.
