# Task C-T2: 样例 dataWriteback executor

（摘自 docs/superpowers/plans/2026-06-29-wfs-service-task.md；执行前必读 spec 对应章节 docs/superpowers/specs/2026-06-29-wfs-service-task-design.md §3.1/§4.5/§5.1）

**Files:**
- Create: `CP6.Core/Services/Wf/Executors/SampleDataWritebackExecutor.cs`(`Key="sampleWriteback"`, `Kind="dataWriteback"`, `VisibleInDesigner=true`)
- Modify: `CP6.WebApi/Program.cs`(DI)
- Test: `CP6.Tests/Wf/SampleDataWritebackExecutorTests.cs`

> 首切给一个可演示的命名回写动作(把表单某字段值写进 inst.VarsJson 作 OutputVars,或调一个既有只读跨模块查询作演示)。**保持幂等**(spec §3.1)。真实业务回写(PO确认/凭证过账等)由后续按需各加一个 executor。

- [ ] **Step 1: 写失败测试** — `ExecuteAsync` 返回 `Ok` 且 `OutputVars` 含约定键;`Kind=="dataWriteback"`、`VisibleInDesigner==true`、`Key=="sampleWriteback"`。
- [ ] **Step 2: FAIL**(`--filter SampleDataWritebackExecutorTests`)。
- [ ] **Step 3: 实现** — 简单幂等回写(如读 `$.amount` × 1 写回 `writebackEcho`),`Ok(OutputVars{...})`。注入 `CP6Context` 如需 DB(同 scoped,sync 路径原子前提,spec §4.5)。
- [ ] **Step 4: PASS + Wf 闸**。
- [ ] **Step 5: commit** — `git commit -m "feat(wfs-service-task): C-T2 样例 dataWriteback executor"`

## 共享契约（精确名字，不得改动）

- `IServiceTaskExecutor { string Key; string Kind; bool VisibleInDesigner; string DisplayName; Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx); }`
- `ServiceTaskContext { Guid InstanceId; Guid TokenId; string NodeId; Guid StarterId; Guid JobId; int AttemptNo; Guid ActorId; DateTime NowUtc; string? VarsJson; string? ActionRefJson; }`
- `ServiceTaskResult { bool Success; string? Error; Dictionary<string,object?>? OutputVars; static Ok(...); static Fail(string); }`
- `ServiceKind`: DataWriteback/WebApi/Timer（常量见 `CP6.Core/Services/Wf/WfStatus.cs`）
- 参数模板求值用既有 `ServiceVarsHelper`（`$.`/`$wf.` 点路径）
- 参考同目录既有实现：`CP6.Core/Services/Wf/Executors/WebApiExecutor.cs`（C-T1 已落地）

## 黄金模板三铁律（本任务额外要求——架构审查 2026-07-05 裁定，写进代码注释供后续 executor 复制）

背景：sync 路径下 executor 与引擎共享同一 DbContext/事务；executor 半途抛异常时，已改的追踪实体会被引擎外层 SaveChanges 一并提交（ServiceTaskNodeHandler.cs:74-95 的入队降级路径不回滚脏改）。因此本样例必须示范：
1. **先校验全部前置条件，再执行任何写操作**——校验失败直接 `ServiceTaskResult.Fail(...)`，不留半截脏改。
2. **幂等**——用 ctx 语义键（如 `jobId`/`instanceId`）判重，重复执行结果一致。
3. **绝不自行 `SaveChanges()`、不开独立事务、不发外部 HTTP**——落库交给引擎的原子接缝；外呼只属于 webApi kind 经 IWfConnector。

把这三条作为代码注释块写在类头部，明确标注「后续 dataWriteback executor 照此模板复制」。

## 落码纪律（每 Task 都遵守）

- 工作目录 `C:\CP6`，分支 `feat/wfs-service-task-finish`（已建，off main 4055dd4）。本地 commit 不 push。
- 零 Space 污染：不碰 `cp6.web/src/views/space/**`、`Services/*Space*`、Space 迁移/DbSet。完成后 `git show --stat` 复核。
- 零改引擎执行态硬闸：跑 `dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf`，既有 Wf 测试字节等价全绿。
- TDD 节奏：先写失败测试→FAIL→最小实现→PASS→commit。
- 测试脚手架：后端用既有 SQLite 测试上下文 `SqliteCP6Context`。
- 不重新设计：spec 决策 D1~D11 + §0.4 六个 P0 护栏全锁。
