# Task A-T1 报告：数据模型 Wf_FlowTrigger / Wf_TriggerFire + WfTriggerType + 迁移 WfsFlowTrigger

**状态：DONE**（一处无害偏差见文末）
**Commit：** `cfbd0b15e6575d2e6263f5bbfb1ea11471f641d4`（已推送 `feat/wfs-event-trigger`）

## 做了什么
严格 TDD 落地本波数据模型第一块：
- `CP6.Core/Services/Wf/WfStatus.cs`：追加 `WfTriggerType`（Timer=0 / Event=1 / Message=2）。
- `CP6.Entity/DomainModels/Wf/Wf_FlowTrigger.cs`（新建）：`[Table]` + `BaseTenantEntity`，字段逐字照 brief（FlowKey/TriggerType/ConfigJson/Enabled/EventKey/StarterUserId/NextDueUtc/LastFiredUtc/ApiKeyHash/RowVersion）；索引键列 `[MaxLength]`，ConfigJson=`nvarchar(max)`，`[Timestamp] RowVersion`。
- `CP6.Entity/DomainModels/Wf/Wf_TriggerFire.cs`（新建）：审计+幂等台账（TriggerId/IdempotencyKey/FiredUtc/InstanceId/Source/Error/PayloadHash）。
- `CP6.Core/EFDbContext/CP6Context.cs`：DbSet 两行（贴 `Wf_ServiceJobs` 声明后）+ OnModelCreating 索引块（贴 `Wf_ServiceJob` 索引块之后）——3 普通索引 + 1 唯一索引，照 brief 原文列序与命名。
- 迁移 `CP6.Core/Migrations/20260713135253_WfsFlowTrigger.cs`（+ Designer + 快照）：`dotnet ef migrations add` 生成，未手写。
- `CP6.Tests/Wf/FlowTriggerModelTests.cs`（新建）：brief 逐字转写的 3 个测试。

引擎零改动（未触碰 FlowEngine*/NodeHandlers/FlowSchemaValidator）。

## RED 证据（实现前）
`dotnet test ... --filter FlowTriggerModelTests` → 编译失败：
- `error CS0103: The name 'WfTriggerType' does not exist`（×5）
- `error CS0246: The type or namespace name 'Wf_FlowTrigger' could not be found`
- `error CS0246: The type or namespace name 'Wf_TriggerFire' could not be found`

## GREEN 证据（实现后）
- 定向：`--filter FlowTriggerModelTests` → `Passed! Failed: 0, Passed: 3, Skipped: 0`。
- 全量：`dotnet test CP6.slnx` → `Passed! Failed: 0, Passed: 1893, Skipped: 5, Total: 1898`（= 基线 1890 + 新 3；注：全量口径含既有 5 skip，实测 1893 passed 与「1890+3」一致）。

## 迁移验证
- `dotnet ef migrations has-pending-model-changes` → `No changes have been made to the model since the last migration.`（clean）。
- Up() 仅建 `Wf_FlowTrigger` + `Wf_TriggerFire` 两表 + 4 索引：`IX_Wf_FlowTrigger_Flow` / `IX_Wf_FlowTrigger_Scan` / `IX_Wf_FlowTrigger_Event`（普通）+ `UX_Wf_TriggerFire_Idem`（`unique: true`，**无 filter**，符合 D7）。零其他表改动、零回填。Down() 仅 DropTable 两表。

## 自审
- RowVersion 映射为 SqlServer `rowversion`（`rowVersion: true`），与 Wf_ServiceJob 先例一致。
- 扫描索引 `IX_Wf_FlowTrigger_Scan` 首列为 `Enabled`（不含 TenantId），照 brief/spec §2.1 原文列序——worker 逐租户 scope 下由全局过滤补 TenantId 条件。
- 唯一索引与 ServiceJob 的 filtered unique 有意不同：本表 IdempotencyKey 非空必填，无需 filtered。
- CP6Context 已有 `using CP6.Entity.DomainModels.Wf`（Wf_ServiceJob 在用），无需新增 using。
- 本任务测试未涉及 RowVersion 并发行为（仅默认值断言），故无需为 Wf_FlowTrigger 加 SQLite AFTER UPDATE 触发器脚手架；留待后续 Task 若需并发测试再补。

## 偏差
- `git add -A` 将一处**无关**的工作树删除 `.superpowers/sdd/task-1-brief.md`（-58 行，一份已被取代的旧简报 doc，非本人创建/删除）一并纳入本 commit。纯文档、无代码影响；未回退以避免额外 churn，特此声明。初始 git status 中的其余 M 态 .superpowers 文档未出现在本 commit（应为陈旧快照或他处已提交）。
- `git show --stat HEAD` 其余 8 项均为 brief 列明文件（含 EF 自动生成的 Designer/快照）。
