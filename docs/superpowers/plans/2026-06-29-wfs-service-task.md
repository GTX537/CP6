# WFS 服务任务节点(Service Task)Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **每个 Task 执行前必读对应 spec 章节**(`docs/superpowers/specs/2026-06-29-wfs-service-task-design.md`),本计划的 code step 对体量大的产品代码会引用 spec 的逐字代码块(spec 已含精确契约/伪码/字段表),测试代码在本计划内逐条给全。

**Goal:** 给 WFS 引擎加「服务任务节点」(`Type="serviceTask"`),让流程节点能跑自动化——数据回写 / 调用 WebAPI(注册式连接器)/ JOB 定时(纯等待 + 到点执行动作),失败重试→错误边/挂起,异步靠轮询 worker 停泊-恢复 token。

**Architecture:** 方案 A:单一 `serviceTask` 节点类型 + 单一 `IServiceTaskExecutor` 注册表 + 共享异步底座(新表 `Wf_ServiceJob` + `WfServiceJobScanWorker` 克隆 `WfTimeoutScanWorker`)。sync=内联乐观一击(原子,失败降级异步重试);async/timer=停泊 token + 入队,worker 到点 lease 抢占执行→`ResumeServiceTokenAsync`(幂等)。错误边 `FlowEdge.IsError`(`AdvanceToken` 跳过,耗尽才走)。

**Tech Stack:** .NET 8 / EF Core(SQL Server 生产,SQLite 测试)/ xUnit(`CP6.Tests/Wf`)/ Vue3 + Vue Flow(`cp6.web/src/views/oa/designer`)/ vitest。

---

## 落码纪律(Discipline — 每个 Task 都遵守)

- **隔离 worktree 唯一**:全部改动在 `D:/CP6-wfs-approver` @ `feat/wfs-approver-resolve`(off main `0541bd9`)。**绝不触碰** `D:/CP6` / `D:/CP6-space-backend` / `D:/CP6-oa-core`。
- **零 Space 污染**:不碰 `cp6.web/src/views/space/**`、`Services/*Space*`、Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核 diff,确认无 Space 文件。
- **零改引擎执行态硬闸**:每 Task 跑 `dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf`,既有 Wf 测试**字节等价全绿**(新 `serviceTask` 类型不触碰既有 5 handler/分发;`AdvanceToken` 加 `IsError != true` 过滤对既有边 `null != true` 为真=等价)。
- **subagent-driven TDD**:每 Task 全新 general-purpose[sonnet] 子代理 → 控制器级 diff 复核(主代理 `git show`)→ 本地 commit **不 push**。
- **TDD 节奏**:先写失败测试→跑验证 FAIL→最小实现→跑验证 PASS→commit。
- **测试脚手架**:后端用 SQLite 测试上下文 `SqliteCP6Context`(已有,`HasTrigger` 模拟 rowversion);时间/退避/lease 测试**注入 `nowUtc`**(`ScanOnceAsync(DateTime nowUtc, ...)`);filtered unique index SQLite 不支持 `HasFilter` → 靠 `EnqueueServiceJob` 代码级先查 + 代码级测试。
- **gstack QA**:末波,隔离库 `CP6DB_OA`(真 SQL Server),harness 只写不跑服务器(避并行会话)。
- **不重新设计**:spec 决策 D1~D11 + §0.4 六个 P0 护栏全锁。

---

## File Structure(创建/修改清单,每文件一职责)

**后端 `CP6.Entity`**
- Create `CP6.Entity/DomainModels/Wf/Wf_ServiceJob.cs` — 异步停泊任务台账实体(BaseTenantEntity)。

**后端 `CP6.Core/Services/Wf`**
- Modify `FlowSchema.cs` — `FlowNode` 加服务任务配置字段;`FlowEdge` 加 `IsError`。
- Modify `WfStatus.cs` — 加 `ServiceJobStatus` / `ServiceKind` / `ServiceMode` 常量类。
- Create `IServiceTaskExecutor.cs` — executor 契约 + `ServiceTaskContext` + `ServiceTaskResult`。
- Create `IWfConnector.cs` — 连接器契约。
- Create `ServiceTaskActionRef.cs` — `ActionRefJson` 结构 + `SnapshotActionRef` + `ResolveExecutorKey`(纯逻辑)。
- Create `ServiceVarsHelper.cs` — 参数模板求值(`$.`/`$wf.`)+ `OutputVars` 合并规则(纯逻辑)。
- Create `NodeHandlers/ServiceTaskNodeHandler.cs` — 第 6 个 INodeHandler(sync/async 分支 + 防重入队)。
- Modify `FlowEngine.Tokens.cs` — `AdvanceToken` 跳 `IsError`;新增 `AdvanceAlongErrorEdge`。
- Modify `FlowEngine.cs` — `DefaultHandlers()` 加 serviceTask;新增 `ResumeServiceTokenAsync` / `FailServiceTokenAsync`(幂等 + 重试×3);`CancelAllActiveTokens` 接缝注释。
- Create `WfServiceJobService.cs` — `IWfServiceJobService.ScanOnceAsync`(reaper + lease + 状态闸 + 重试 + 路由)。
- Create `Executors/WebApiExecutor.cs` — `webApi` 通用执行器。
- Create `Executors/EchoConnector.cs` — 样例连接器(QA 用,可 echo)。
- Create `Executors/SampleDataWritebackExecutor.cs` — 1 个样例命名回写动作。
- Modify `FlowSchemaValidator.cs` — serviceTask 配置 + 错误边规则(E-WF-016/017)。
- Modify `DesignerService.cs`(实际路径执行时确认)— save 时校验注册名(E-WF-018)+ 服务目录查询。

**后端 `CP6.WebApi`**
- Create `BackgroundServices/WfServiceJobScanWorker.cs` — 克隆 `WfTimeoutScanWorker`。
- Modify `Program.cs` — DI:ServiceTaskNodeHandler / IWfServiceJobService / worker / WebApiExecutor / 样例 executor / EchoConnector;i18n seed concat。
- Modify `Controllers/Oa/DesignerController.cs`(实际路径执行时确认)— `GET service-catalog` 端点。
- Create `Seeds/I18nOaServiceTaskScreenSeed.cs`(仿 `I18nOaApproverScreenSeed` 实际路径)— 五语键。

**前端 `cp6.web/src/views/oa/designer`**
- Modify `designerModel.ts` — NODE_PALETTE 加 3 入口;`schemaToGraph`/`graphToSchema` round-trip `Service*`/`IsError`;`validateClient` 镜像;TS 类型。
- Create `nodes/ServiceTaskNode.vue` — serviceTask 自定义节点。
- Modify `DesignerCanvas.vue` — 注册 serviceTask 节点类型 + 调色板。
- Modify `NodePropertyPanel.vue` — 服务任务段(按 kind 切换)+ 拉服务目录。
- Modify `EdgePropertyPanel.vue` — 「失败边(IsError)」复选框。
- Modify `cp6.web/src/api/oa/designer.ts`(实际路径)— service-catalog API + 类型。

**测试 / QA**
- Create `CP6.Tests/Wf/ServiceTask*.cs` — 各 Task 的测试(见各任务)。
- Create `docs/superpowers/qa/wfs-service-task/{README.md,seed.sql,qa_service_task.ps1}` — gstack harness。

---

## 共享契约(所有 Task 用这些**精确**名字,见 spec §2/§3/§4)

- 实体 `Wf_ServiceJob` 字段:`Id, TenantId, InstanceId, TokenId, NodeId, Kind, ActionRefJson, DueAtUtc, Status, AttemptCount, MaxAttempts, NextAttemptAtUtc, LockedBy, LockedAtUtc, LockExpiresAtUtc, LastError, CompletedAtUtc, CreateDate, ModifyDate, RowVersion`。
- `ServiceJobStatus`: Pending=0/Running=1/Succeeded=2/Failed=3/Cancelled=4。`ServiceKind`: DataWriteback/WebApi/Timer。`ServiceMode`: Sync/Async。
- `IServiceTaskExecutor { string Key; string Kind; bool VisibleInDesigner; string DisplayName; Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx); }`
- `ServiceTaskContext { Guid InstanceId; Guid TokenId; string NodeId; Guid StarterId; Guid JobId; int AttemptNo; Guid ActorId; DateTime NowUtc; string? VarsJson; string? ActionRefJson; }`
- `ServiceTaskResult { bool Success; string? Error; Dictionary<string,object?>? OutputVars; static Ok(...); static Fail(string); }`
- `IWfConnector { string Name; string DisplayName; Task<ServiceTaskResult> CallAsync(string pathTemplate, string? paramsJson, ServiceTaskContext ctx); }`
- 引擎方法签名:
  - `internal async Task ResumeServiceTokenAsync(Guid instanceId, Guid tokenId, string nodeId, Dictionary<string,object?>? outputVars)`
  - `internal async Task FailServiceTokenAsync(Guid instanceId, Guid tokenId, string nodeId, string? reason)`
  - `internal void AdvanceAlongErrorEdge(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token)`
- `IWfServiceJobService { Task<int> ScanOnceAsync(DateTime nowUtc, string workerId, CancellationToken ct = default); }`
- 错误码:`E-WF-016`(服务任务配置)/ `E-WF-017`(错误边)/ `E-WF-018`(未注册名)。

---

## Wave P-A — 引擎内核

### Task A-T1: 数据模型(POCO 字段 + 枚举 + Wf_ServiceJob 表 + 迁移)

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowSchema.cs`(FlowNode + FlowEdge)
- Modify: `CP6.Core/Services/Wf/WfStatus.cs`
- Create: `CP6.Entity/DomainModels/Wf/Wf_ServiceJob.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`(DbSet + 索引)
- Create: 迁移 `CP6.Core/Migrations/<ts>_WfsServiceTask.cs`(由 `dotnet ef` 生成)
- Test: `CP6.Tests/Wf/ServiceTaskModelTests.cs`

- [ ] **Step 1: 写失败测试** — POCO 字段 + 枚举存在性 + 实体默认值。

```csharp
// CP6.Tests/Wf/ServiceTaskModelTests.cs
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Xunit;

public class ServiceTaskModelTests
{
    [Fact]
    public void FlowNode_HasServiceTaskFields()
    {
        var n = new FlowNode { Id = "n1", Type = "serviceTask",
            ServiceKind = ServiceKind.WebApi, ServiceMode = ServiceMode.Async,
            ServiceConnectorName = "erpEcho", ServicePath = "/x", ServiceParamsJson = "{}",
            ServiceActionName = null, ServiceDelayMode = "duration", ServiceDelayValue = "PT2H",
            ServiceMaxRetries = 3, ServiceRetryBackoffSec = 30 };
        Assert.Equal("webApi", n.ServiceKind);
    }

    [Fact]
    public void FlowEdge_HasIsError()
    {
        var e = new FlowEdge { From = "a", To = "b", IsError = true };
        Assert.True(e.IsError);
    }

    [Fact]
    public void ServiceJobStatus_Constants()
    {
        Assert.Equal(0, ServiceJobStatus.Pending);
        Assert.Equal(1, ServiceJobStatus.Running);
        Assert.Equal(2, ServiceJobStatus.Succeeded);
        Assert.Equal(3, ServiceJobStatus.Failed);
        Assert.Equal(4, ServiceJobStatus.Cancelled);
    }

    [Fact]
    public void Wf_ServiceJob_Defaults()
    {
        var j = new Wf_ServiceJob { InstanceId = System.Guid.NewGuid(), TokenId = System.Guid.NewGuid(),
            NodeId = "n1", Kind = ServiceKind.Timer, Status = ServiceJobStatus.Pending,
            AttemptCount = 0, MaxAttempts = 4 };
        Assert.Equal(0, j.AttemptCount);
        Assert.Null(j.LockedBy);
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter ServiceTaskModelTests`。预期编译失败(字段/类型不存在)。

- [ ] **Step 3: 实现** — 按 spec §2.1/§2.2/§2.3/§2.4 逐字加:
  - `FlowSchema.cs` 的 `FlowNode` 加 spec §2.1 全部字段;`FlowEdge` 加 `public bool? IsError { get; set; }`(spec §2.2)。
  - `WfStatus.cs` 加 spec §2.4 三个 `static class`(ServiceJobStatus/ServiceKind/ServiceMode)。
  - `Wf_ServiceJob.cs` 按 spec §2.3 字段表(继承 `BaseTenantEntity`,`[Timestamp] byte[]? RowVersion`)。
  - `CP6Context.cs` 加 `public DbSet<Wf_ServiceJob> Wf_ServiceJobs { get; set; }`;`OnModelCreating` 加 spec §2.3 三索引:
    ```csharp
    b.Entity<Wf_ServiceJob>().HasIndex(x => new { x.TenantId, x.Status, x.NextAttemptAtUtc }).HasDatabaseName("IX_Wf_ServiceJob_Scan");
    b.Entity<Wf_ServiceJob>().HasIndex(x => new { x.TenantId, x.InstanceId }).HasDatabaseName("IX_Wf_ServiceJob_Instance");
    b.Entity<Wf_ServiceJob>().HasIndex(x => new { x.TenantId, x.TokenId, x.NodeId }).IsUnique().HasFilter("[Status] IN (0, 1)").HasDatabaseName("UX_Wf_ServiceJob_LiveToken");
    ```
  - **注意**:`HasFilter` 仅 SQL Server;若 `SqliteCP6Context` 覆写 `OnModelCreating` 须排除该 filter(参既有 Sqlite 子类做法),测试库不建该唯一索引(改靠代码级防重测,见 A-T6)。

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test ... --filter ServiceTaskModelTests`,预期 PASS。

- [ ] **Step 5: 生成迁移** — `dotnet ef migrations add WfsServiceTask --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context`。检查 Up 仅建 `Wf_ServiceJob` 表 + 3 索引,无其他表改动(零回填)。`dotnet ef migrations has-pending-model-changes ...` 应为 clean。

- [ ] **Step 6: Wf 闸 + commit**
```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf   # 既有照绿
git add -A && git commit -m "feat(wfs-service-task): A-T1 数据模型 FlowNode/Edge 字段+Wf_ServiceJob 表+枚举+迁移"
```

---

### Task A-T2: executor / connector 契约(纯接口 + DTO)

**Files:**
- Create: `CP6.Core/Services/Wf/IServiceTaskExecutor.cs`(含 `ServiceTaskContext` + `ServiceTaskResult`)
- Create: `CP6.Core/Services/Wf/IWfConnector.cs`
- Test: `CP6.Tests/Wf/ServiceTaskContractTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/ServiceTaskContractTests.cs
using System; using System.Collections.Generic; using System.Threading.Tasks;
using CP6.Core.Services.Wf; using Xunit;

public class ServiceTaskContractTests
{
    private sealed class FakeExec : IServiceTaskExecutor {
        public string Key => "x"; public string Kind => "dataWriteback";
        public bool VisibleInDesigner => true; public string DisplayName => "X";
        public Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx)
            => Task.FromResult(ServiceTaskResult.Ok(new Dictionary<string,object?>{["k"]=1}));
    }

    [Fact]
    public async Task Result_Ok_CarriesOutputVars()
    {
        var ctx = new ServiceTaskContext { InstanceId = Guid.NewGuid(), TokenId = Guid.NewGuid(),
            NodeId = "n", StarterId = Guid.NewGuid(), JobId = Guid.NewGuid(), AttemptNo = 1,
            ActorId = Guid.Empty, NowUtc = new DateTime(2026,6,29,0,0,0,DateTimeKind.Utc) };
        var r = await new FakeExec().ExecuteAsync(ctx);
        Assert.True(r.Success); Assert.Equal(1, r.OutputVars!["k"]);
    }

    [Fact]
    public void Result_Fail_HasError()
    {
        var r = ServiceTaskResult.Fail("boom");
        Assert.False(r.Success); Assert.Equal("boom", r.Error);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `dotnet test ... --filter ServiceTaskContractTests`(编译失败)。

- [ ] **Step 3: 实现** — 按 spec §3.1 逐字写 `IServiceTaskExecutor` / `ServiceTaskContext`(含 `JobId/AttemptNo/ActorId/NowUtc`)/ `ServiceTaskResult`(`Ok`/`Fail` 工厂);按 spec §3.3 写 `IWfConnector`。纯接口/record,无业务。

- [ ] **Step 4: 跑验证 PASS**。

- [ ] **Step 5: Wf 闸 + commit**
```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-service-task): A-T2 IServiceTaskExecutor/Context/Result + IWfConnector 契约"
```

---

### Task A-T3: ServiceVarsHelper(参数模板求值 + OutputVars 合并,纯逻辑)

**Files:**
- Create: `CP6.Core/Services/Wf/ServiceVarsHelper.cs`
- Test: `CP6.Tests/Wf/ServiceVarsHelperTests.cs`

实现 spec §3.6:模板语法 `$.var`(取 VarsJson 点路径)/ `$wf.ctx`(actorId/jobId/instanceId/nowUtc)/ 字面量;`path` 里 `{x}`≡`$.x`。`MergeOutputVars(varsJson, outputVars)`:仅 top-level object、禁覆盖 `wf.`/`sys.`/`_internal.` 前缀、大小写敏感、仅 JSON 值,返回 (newVarsJson, mergedKeys, skippedKeys)。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/ServiceVarsHelperTests.cs
using System.Collections.Generic; using CP6.Core.Services.Wf; using Xunit;

public class ServiceVarsHelperTests
{
    [Fact]
    public void Resolve_DollarVar_FromVars()
    {
        var vars = "{\"orderId\":\"PO-1\",\"detail\":{\"lineNo\":3}}";
        var ctx = new ServiceTemplateCtx(vars, actorId: "u1", jobId: "j1", instanceId: "i1", nowUtcIso: "2026-06-29T00:00:00Z");
        Assert.Equal("PO-1", ServiceVarsHelper.ResolveValue("$.orderId", ctx));
        Assert.Equal("3", ServiceVarsHelper.ResolveValue("$.detail.lineNo", ctx));
        Assert.Equal("u1", ServiceVarsHelper.ResolveValue("$wf.actorId", ctx));
        Assert.Equal("固定值", ServiceVarsHelper.ResolveValue("固定值", ctx));
    }

    [Fact]
    public void Merge_TopLevelOnly_And_BlocksReserved()
    {
        var vars = "{\"a\":1}";
        var output = new Dictionary<string,object?>{ ["b"]=2, ["wf"]=new{x=1}, ["sys"]="no", ["_internal"]=true };
        var res = ServiceVarsHelper.MergeOutputVars(vars, output);
        Assert.Contains("\"b\":2", res.VarsJson);
        Assert.DoesNotContain("\"sys\"", res.VarsJson);          // reserved blocked
        Assert.Contains("b", res.MergedKeys);
        Assert.Contains("wf", res.SkippedKeys);
        Assert.Contains("sys", res.SkippedKeys);
        Assert.Contains("_internal", res.SkippedKeys);
    }

    [Fact]
    public void Merge_OverwriteExistingNonReserved()
    {
        var res = ServiceVarsHelper.MergeOutputVars("{\"a\":1}", new Dictionary<string,object?>{["a"]=9});
        Assert.Contains("\"a\":9", res.VarsJson);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**(`--filter ServiceVarsHelperTests`)。

- [ ] **Step 3: 实现** — `ServiceVarsHelper`(静态)+ `ServiceTemplateCtx`(record:varsJson/actorId/jobId/instanceId/nowUtcIso)+ `MergeResult`(VarsJson/MergedKeys/SkippedKeys)。用 `System.Text.Json`(`JsonDocument`/`JsonNode`)解析点路径;合并时遍历 top-level,reserved 前缀(`wf`/`sys`/`_internal`,以及任何含 `.` 前缀)跳过并计入 SkippedKeys。缺失路径返回空串/null。

- [ ] **Step 4: 跑验证 PASS**。

- [ ] **Step 5: Wf 闸 + commit**
```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-service-task): A-T3 ServiceVarsHelper 模板求值+OutputVars 合并规则"
```

---

### Task A-T4: ActionRef 结构 + 解析(纯逻辑)

**Files:**
- Create: `CP6.Core/Services/Wf/ServiceTaskActionRef.cs`(record + `SnapshotActionRef(FlowNode)` + `ResolveExecutorKey(actionRef)`)
- Test: `CP6.Tests/Wf/ServiceTaskActionRefTests.cs`

实现 spec §3.5(ActionRefJson 结构)+ §3.2(解析键规则)。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/ServiceTaskActionRefTests.cs
using CP6.Core.Services.Wf; using Xunit;

public class ServiceTaskActionRefTests
{
    [Fact]
    public void Snapshot_WebApi()
    {
        var n = new FlowNode { Id="n", Type="serviceTask", ServiceKind=ServiceKind.WebApi,
            ServiceConnectorName="erpEcho", ServicePath="/o/{orderId}", ServiceParamsJson="{}" };
        var json = ServiceTaskActionRef.Snapshot(n);
        var r = ServiceTaskActionRef.Parse(json);
        Assert.Equal("webApi", r.ServiceKind);
        Assert.Equal("webApi", r.ActionKind);
        Assert.Equal("erpEcho", r.ConnectorName);
        Assert.Equal("webApi", ServiceTaskActionRef.ResolveExecutorKey(r));
    }

    [Fact]
    public void Snapshot_DataWriteback()
    {
        var n = new FlowNode { Id="n", Type="serviceTask", ServiceKind=ServiceKind.DataWriteback,
            ServiceActionName="poConfirm", ServiceParamsJson="{}" };
        var r = ServiceTaskActionRef.Parse(ServiceTaskActionRef.Snapshot(n));
        Assert.Equal("poConfirm", ServiceTaskActionRef.ResolveExecutorKey(r));
    }

    [Fact]
    public void Snapshot_Timer_PureWait_ResolvesNull()
    {
        var n = new FlowNode { Id="n", Type="serviceTask", ServiceKind=ServiceKind.Timer,
            ServiceDelayMode="duration", ServiceDelayValue="PT2H" };
        var r = ServiceTaskActionRef.Parse(ServiceTaskActionRef.Snapshot(n));
        Assert.Equal("none", r.ActionKind);
        Assert.Null(ServiceTaskActionRef.ResolveExecutorKey(r));   // 纯等待无 executor
    }

    [Fact]
    public void Snapshot_Timer_WithWebApiAction()
    {
        var n = new FlowNode { Id="n", Type="serviceTask", ServiceKind=ServiceKind.Timer,
            ServiceDelayMode="untilDate", ServiceDelayValue="2026-07-01",
            ServiceConnectorName="erpEcho", ServicePath="/x" };
        var r = ServiceTaskActionRef.Parse(ServiceTaskActionRef.Snapshot(n));
        Assert.Equal("webApi", r.ActionKind);
        Assert.Equal("webApi", ServiceTaskActionRef.ResolveExecutorKey(r));
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**(`--filter ServiceTaskActionRefTests`)。

- [ ] **Step 3: 实现** — `ServiceTaskActionRef` record(spec §3.5 字段:ServiceKind/ActionKind/Timer{DelayMode,DelayValue}/ActionName/ConnectorName/Path/ParamsJson)。
  - `Snapshot(FlowNode)`:据 `ServiceKind` 推 `ActionKind`(timer 且无 connector/action → "none";timer 有 connector → "webApi";timer 有 actionName → "dataWriteback";否则 actionKind=serviceKind),序列化为 JSON。
  - `Parse(json)`:反序列化。
  - `ResolveExecutorKey(r)`:`r.ActionKind` switch → "webApi"→"webApi" / "dataWriteback"→`r.ActionName` / "none"→null。

- [ ] **Step 4: 跑验证 PASS**。
- [ ] **Step 5: Wf 闸 + commit** — `git commit -m "feat(wfs-service-task): A-T4 ActionRef 结构+SnapshotActionRef+ResolveExecutorKey"`

---

### Task A-T5: 引擎方法(AdvanceToken 跳错误边 + AdvanceAlongErrorEdge + ResumeServiceTokenAsync 幂等 + FailServiceTokenAsync)

> **P0-2(恢复幂等)+ P1-4(错误变量)+ D8(错误边不变量)落点。** 这是最关键的正确性任务。

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`(AdvanceToken 改 1 行过滤 + 新增 AdvanceAlongErrorEdge)
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`(新增 ResumeServiceTokenAsync / FailServiceTokenAsync)
- Test: `CP6.Tests/Wf/ErrorEdgeRoutingTests.cs`、`CP6.Tests/Wf/ResumeServiceTokenTests.cs`

- [ ] **Step 1: 写失败测试(错误边 + 跳过不变量)**

```csharp
// CP6.Tests/Wf/ErrorEdgeRoutingTests.cs
// 用既有 Wf 测试脚手架建 schema:start -> svc(serviceTask) --成功--> end / svc --IsError--> human(approval)
// 断言:① AdvanceToken 从 svc 只走非错误边到 end(成功路径跳过 IsError 边)
//       ② AdvanceAlongErrorEdge 从 svc 走到 human
//       ③ 既有线性流(无 IsError 边)AdvanceToken 行为不变
[Fact] public async Task AdvanceToken_SkipsErrorEdge_TakesSuccessEdge() { /* 见步骤注释,断言 token 落在 end */ }
[Fact] public async Task AdvanceAlongErrorEdge_TakesErrorEdge() { /* 断言 token 落在 human 节点,生成 task */ }
[Fact] public async Task AdvanceToken_NoErrorEdge_Unchanged() { /* 线性 start->approval->end 既有行为 */ }
```

```csharp
// CP6.Tests/Wf/ResumeServiceTokenTests.cs
// ResumeServiceTokenAsync 幂等:
[Fact] public async Task Resume_AdvancesParkedToken_Once() { /* 停泊 token@svc -> Resume -> token 离开 svc 到 end */ }
[Fact] public async Task Resume_NoOp_WhenTokenNotActive() { /* token 已 Consumed -> Resume 不报错不二次推进 */ }
[Fact] public async Task Resume_NoOp_WhenTokenLeftNode() { /* token.NodeId != job.NodeId -> Resume no-op */ }
[Fact] public async Task Resume_MergesOutputVars() { /* outputVars 合并进 inst.VarsJson(经 ServiceVarsHelper) */ }
```

> 测试脚手架照 `CP6.Tests/Wf/ParallelGatewayTests.cs` / `FlowTokenKernelTests.cs`:用 `SqliteCP6Context`(`HasTrigger`)建 db、构造 `FlowEngine`、`SpawnToken`/手动置 token 在 svc 节点、调内部方法(测试项目对 `internal` 有 `InternalsVisibleTo`,执行时确认;若无则经公共入口或加可见性)。

- [ ] **Step 2: 跑验证 FAIL**(`--filter "ErrorEdgeRoutingTests|ResumeServiceTokenTests"`)。

- [ ] **Step 3: 实现**
  - `FlowEngine.Tokens.cs` `AdvanceToken`:出边过滤加 `&& e.IsError != true`(spec §4.4):
    ```csharp
    foreach (var edge in schema.Edges.Where(e => e.From == token.NodeId && e.IsError != true))
    ```
  - 新增 `AdvanceAlongErrorEdge`(spec §4.4):选 `IsError==true` 的出边,有则 `token.NodeId=target.Id; await EnterNodeAsync(...)`;无则 `Suspend(inst, node, "服务任务失败且无错误边")`。
  - `FlowEngine.cs` 新增 `ResumeServiceTokenAsync(instId, tokenId, nodeId, outputVars)`(spec §4.4,**幂等**):重载 inst/schema/token;`if (token == null || token.Status != Active || token.NodeId != nodeId) return;`(P0-2);合并 outputVars(`ServiceVarsHelper.MergeOutputVars`,记 history `serviceVars`);`await AdvanceToken(...)`;`DispatchIfFinishedAsync`;`SaveChangesAsync`;整体包乐观并发重试×3(仿 `ActAsync` 的 `for(attempt) try/catch DbUpdateConcurrencyException reload`)。
  - 新增 `FailServiceTokenAsync(instId, tokenId, nodeId, reason)`(spec §4.3):同样先 reload + 幂等闸(token 已离开则 return);先把 `wf.serviceError{nodeId,jobId?,kind?,message,failedAtUtc}` 合并进 VarsJson(P1-4,用 ServiceVarsHelper 但**允许写 wf.serviceError 这一受控路径**——单独 helper `WriteServiceError` 直接 set,不走 reserved 拦截);若节点有 IsError 边 → `AdvanceAlongErrorEdge`,否则 `Suspend`;重试×3;SaveChanges。

- [ ] **Step 4: 跑验证 PASS**。

- [ ] **Step 5: Wf 闸(关键!)+ commit**
```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf   # 既有全绿:AdvanceToken 改动须字节等价
git add -A && git commit -m "feat(wfs-service-task): A-T5 引擎方法 AdvanceToken跳错误边/AdvanceAlongErrorEdge/ResumeServiceTokenAsync幂等/FailServiceTokenAsync"
```

---

### Task A-T6: ServiceTaskNodeHandler(第 6 handler:sync/async 分支 + 防重入队)+ DI

> **P0-1(AttemptCount)+ P0-3(防重入队)落点。**

**Files:**
- Create: `CP6.Core/Services/Wf/NodeHandlers/ServiceTaskNodeHandler.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`(`DefaultHandlers()` 加 serviceTask)
- Modify: `CP6.WebApi/Program.cs`(DI 追加 `AddScoped<INodeHandler, ServiceTaskNodeHandler>()`,在 §1 锚点 Program.cs:108-112 同块)
- Test: `CP6.Tests/Wf/ServiceTaskHandlerTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/ServiceTaskHandlerTests.cs
// 用 SqliteCP6Context + FakeExec(可控成功/失败)+ schema:start->svc->end
[Fact] public async Task Sync_Success_AdvancesToken_NoJob() { /* dataWriteback sync 成功 -> token 离开 svc,Wf_ServiceJobs 空 */ }
[Fact] public async Task Sync_Fail_EnqueuesJob_AttemptCount1_ParksToken() { /* sync 失败 -> 1 条 Pending job,AttemptCount==1,token 仍 Active@svc */ }
[Fact] public async Task Async_Enqueues_AttemptCount0_ParksToken() { /* webApi async -> 1 条 Pending job,AttemptCount==0,token Active@svc,DueAtUtc≈now */ }
[Fact] public async Task Timer_Enqueues_FutureDueAt() { /* timer duration PT2H -> DueAtUtc≈now+2h */ }
[Fact] public async Task Enqueue_Dedupe_SameTokenNode() { /* 同 token 同 node 二次 OnEnter -> 仍只 1 条活跃 job(P0-3 代码级防重) */ }
```

- [ ] **Step 2: 跑验证 FAIL**(`--filter ServiceTaskHandlerTests`)。

- [ ] **Step 3: 实现** `ServiceTaskNodeHandler : INodeHandler`(`Type => "serviceTask"`),按 spec §3.4 伪码:
  - 持有 `IEnumerable<IServiceTaskExecutor>` → 字典(ctor 注入);timer 到期 `ComputeDueUtc` 见 §4.7(本任务先做 duration;untilDate/untilExpr 由 P-B/B-T1 或此处实现,放此处更内聚——实现 duration+untilDate+untilExpr,untilDate 按 app 默认时区转 UTC,P0-6)。
  - `mode` 解析:`(kind==timer)?async : (ServiceMode ?? (kind==webApi?async:sync))`。
  - `maxAttempts = (node.ServiceMaxRetries ?? 3) + 1`(P0-1)。
  - sync:`Resolve(ResolveExecutorKey).ExecuteAsync(ctx with JobId=Guid.Empty, AttemptNo=1, ActorId=inst.StarterId)`;成功→`MergeOutputVars`+`Engine.AdvanceToken`;失败→`EnqueueServiceJob(AttemptCount=1, NextAttemptAtUtc=now+backoff)`。
  - async/timer:`EnqueueServiceJob(dueAtUtc, AttemptCount=0, Status=Pending)`;不 advance(停泊)。
  - `EnqueueServiceJob`:**先查** `_db.Wf_ServiceJobs.Local + 库` 是否已有同 `(TenantId,TokenId,NodeId)` 且 `Status∈{Pending,Running}`(用 localIds 排除式,仿既有 `HasActiveToken` 写法防漏变更追踪器),有则不重复 Add(P0-3)。`ActionRefJson = ServiceTaskActionRef.Snapshot(node)`。
  - `FlowEngine.DefaultHandlers()` 数组加 `new ServiceTaskNodeHandler(...)`(注意 DefaultHandlers 是无参 new;executor 列表注入——DefaultHandlers 用空 executor 列表,真实运行经 DI 注入带 executor 的实例;测试直接构造带 FakeExec 的 handler 并传给 FlowEngine 的 handlers 参数)。
  - `Program.cs` 加 `AddScoped<INodeHandler, ServiceTaskNodeHandler>()`。

- [ ] **Step 4: 跑验证 PASS**。
- [ ] **Step 5: Wf 闸 + commit** — `git commit -m "feat(wfs-service-task): A-T6 ServiceTaskNodeHandler sync/async+防重入队+DI"`

---

## Wave P-B — 异步底座

### Task B-T1: IWfServiceJobService.ScanOnceAsync(reaper + lease + 状态闸 + 重试 + 路由)

> **P0-4(lease reaper)+ P0-5(撤回后状态闸)+ P0-1(AttemptCount 退避)落点。** 异步引擎核心。

**Files:**
- Create: `CP6.Core/Services/Wf/WfServiceJobService.cs`(`IWfServiceJobService` + 实现)
- Test: `CP6.Tests/Wf/ServiceJobScanTests.cs`

实现 spec §4.1/§4.2/§4.6。签名 `Task<int> ScanOnceAsync(DateTime nowUtc, string workerId, CancellationToken ct = default)`。`LeaseDuration` 默认 5min(常量,可配)。依赖 `CP6Context` + `FlowEngine` + `IEnumerable<IServiceTaskExecutor>` + `IEnumerable<IWfConnector>`(经 WebApiExecutor 间接)。

- [ ] **Step 1: 写失败测试**(注入 `nowUtc` 实现确定性)

```csharp
// CP6.Tests/Wf/ServiceJobScanTests.cs
// 脚手架:SqliteCP6Context + FlowEngine + 可控 FakeExec;schema start->svc->end;停泊 token@svc + 1 Pending job
[Fact] public async Task DueJob_Success_ResumesToken_MarksSucceeded() { /* nowUtc>=DueAt -> 执行成功 -> token 离 svc 到 end,job.Status=Succeeded,CompletedAtUtc 非空 */ }
[Fact] public async Task NotDue_Skipped() { /* NextAttemptAtUtc>now -> 不处理,返回 0 */ }
[Fact] public async Task Fail_Retries_WithBackoff_UntilMaxAttempts() { /* FakeExec 恒失败,MaxAttempts=2 -> 第1次 AttemptCount=1 置 Pending+退避;第2次 AttemptCount=2 -> Failed+路由 */ }
[Fact] public async Task Exhausted_NoErrorEdge_Suspends() { /* 耗尽且无 IsError 边 -> inst.Status=Suspended */ }
[Fact] public async Task Exhausted_WithErrorEdge_RoutesAndWritesServiceError() { /* 耗尽且有 IsError 边 -> token 走错误边,VarsJson 含 wf.serviceError(P1-4) */ }
[Fact] public async Task Reaper_ResetsExpiredLease_Only() { /* Running 且 LockExpiresAtUtc<now -> 重置 Pending+AttemptCount++;Running 且 LockExpiresAtUtc>now -> 不动(P0-4) */ }
[Fact] public async Task StateGate_InstanceWithdrawn_CancelsJob_NoExecute() { /* job Running 前实例已 Withdrawn -> 标 Cancelled,executor 不被调用(P0-5) */ }
[Fact] public async Task StateGate_TokenLeftNode_CancelsJob() { /* token 已不在 svc 节点 -> job Cancelled,不恢复 */ }
[Fact] public async Task Timer_NoAction_JustAdvances() { /* actionKind=none -> 不调 executor,直接恢复 token */ }
```

- [ ] **Step 2: 跑验证 FAIL**(`--filter ServiceJobScanTests`)。

- [ ] **Step 3: 实现** — 按 spec §4.2 主循环:
  1. **reaper**:`Where(Status==Running && LockExpiresAtUtc<nowUtc)` → `Status=Pending; AttemptCount++; Locked*=null`;SaveChanges。
  2. **取到期**:`Where(Status==Pending && NextAttemptAtUtc<=nowUtc).OrderBy(NextAttemptAtUtc).Take(50)`。
  3. **lease 抢占**:`Status=Running; LockedBy=workerId; LockedAtUtc=nowUtc; LockExpiresAtUtc=nowUtc+LeaseDuration`;`try SaveChanges catch DbUpdateConcurrencyException: continue`。
  4. **执行前状态闸**(P0-5):查 inst.Status==Running 且 token 存在/Active/`NodeId==job.NodeId`;否则 `Status=Cancelled; CompletedAtUtc=nowUtc; SaveChanges; continue`。
  5. **执行**:`AttemptCount++`;`actionKind=="none"`→`Ok()`;否则 `Resolve(ResolveExecutorKey(actionRef))`,null→`Fail("E-WF-018 ...")`,否则 `try ExecuteAsync(ctx{JobId,AttemptNo=AttemptCount,ActorId=SystemActor,NowUtc=nowUtc,VarsJson,ActionRefJson}) catch ex→Fail(ex.Message)`。
  6. **成功**:`await engine.ResumeServiceTokenAsync(InstanceId, TokenId, NodeId, r.OutputVars)`;`Status=Succeeded; CompletedAtUtc=nowUtc`;SaveChanges。
  7. **失败**:`if AttemptCount<MaxAttempts: Status=Pending; NextAttemptAtUtc=nowUtc + Backoff*2^(AttemptCount-1); LastError; Locked*=null; else: Status=Failed; CompletedAtUtc; await engine.FailServiceTokenAsync(InstanceId, TokenId, NodeId, r.Error)`;SaveChanges。
  - `Backoff` 基数 = job 关联节点的 `ServiceRetryBackoffSec`(快照进 job?或固定 30s 常量;首切用常量 30s——若要 per-node 退避,Snapshot 时存进 ActionRefJson,这里读取。**决定:固定常量 `BackoffBaseSec=30`,简化**;per-node 退避留后)。
  - `SystemActor = Guid.Empty`(沿用 `WfTimeoutService`)。

- [ ] **Step 4: 跑验证 PASS**。
- [ ] **Step 5: Wf 闸 + commit** — `git commit -m "feat(wfs-service-task): B-T1 ScanOnceAsync reaper+lease+状态闸+退避重试+错误路由"`

---

### Task B-T2: WfServiceJobScanWorker(BackgroundService)+ DI

**Files:**
- Create: `CP6.WebApi/BackgroundServices/WfServiceJobScanWorker.cs`(克隆 `WfTimeoutScanWorker`)
- Modify: `CP6.WebApi/Program.cs`(DI:`AddScoped<IWfServiceJobService, WfServiceJobService>()` + `AddHostedService<WfServiceJobScanWorker>()`,在 §1 锚点 Program.cs:130-131 同块)
- Test:(worker 是薄壳,主要靠 B-T1 覆盖;可加一个 smoke 测确认间隔/workerId 传参,可选)

- [ ] **Step 1: 实现** — 照 `WfTimeoutScanWorker:10-49` 逐字克隆,差异:
  - `Interval = TimeSpan.FromSeconds(20)`(spec §4.1)。
  - `workerId` = `$"{Environment.MachineName}:{Guid.NewGuid():N}"`(进程实例标识,lease 用)。
  - 调 `sp.GetRequiredService<IWfServiceJobService>().ScanOnceAsync(DateTime.UtcNow, workerId, ct)`(**UTC**,P0-6)。
  - 日志文案改「Wf 服务任务扫描」。
- [ ] **Step 2: DI** — `Program.cs` 追加两行(service + hosted)。
- [ ] **Step 3: 编译 + Wf 闸 + commit**
```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-service-task): B-T2 WfServiceJobScanWorker + DI"
```

---

### Task B-T3: 实例终止清理接缝(撤回/驳回 cancel Pending job)

> **P0-5 的入队侧:** Pending job 在实例终止时立即 Cancelled;Running job 由 B-T1 状态闸兜。

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`(`CancelAllActiveTokens` 内或其调用点同步 cancel Pending job)
- Test: `CP6.Tests/Wf/ServiceJobCleanupTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/ServiceJobCleanupTests.cs
[Fact] public async Task Withdraw_CancelsPendingServiceJobs() {
    // 实例有 1 Pending job;撤回(走 CancelAllActiveTokens)-> 该 job.Status=Cancelled
}
[Fact] public async Task Withdraw_DoesNotTouchOtherInstancesJobs() {
    // 另一实例的 Pending job 不受影响
}
```

- [ ] **Step 2: 跑验证 FAIL**(`--filter ServiceJobCleanupTests`)。
- [ ] **Step 3: 实现** — 在 `CancelAllActiveTokens(instanceId)`(或撤回/驳回收口处)追加:把该 `instanceId` 下 `Status∈{Pending}` 的 `Wf_ServiceJob` 置 `Cancelled` + `CompletedAtUtc=DateTime.UtcNow`(用 Local+库 localIds 排除式防漏追踪器,仿既有写法)。Running job 不动(B-T1 状态闸处理)。
- [ ] **Step 4: 跑验证 PASS + Wf 闸**(撤回既有测试须仍绿)。
- [ ] **Step 5: commit** — `git commit -m "feat(wfs-service-task): B-T3 撤回/驳回清理 Pending 服务任务 job"`

---

## Wave P-C — 连接器 + 执行器

### Task C-T1: WebApiExecutor + EchoConnector + app 级注册

> **P1-2(Idempotency-Key)+ P1-5(连接器决定 method/response)+ P1-6(Kind/VisibleInDesigner)落点。**

**Files:**
- Create: `CP6.Core/Services/Wf/Executors/WebApiExecutor.cs`(`Key="webApi"`, `Kind="webApi"`, `VisibleInDesigner=false`)
- Create: `CP6.Core/Services/Wf/Executors/EchoConnector.cs`(样例 `IWfConnector`,QA 用:把 path/params echo 回 OutputVars)
- Modify: `CP6.WebApi/Program.cs`(DI:`AddScoped<IServiceTaskExecutor, WebApiExecutor>()` + `AddScoped<IWfConnector, EchoConnector>()`;`AddHttpClient()` 若未注册)
- Test: `CP6.Tests/Wf/WebApiExecutorTests.cs`

- [ ] **Step 1: 写失败测试**(用假 `IWfConnector` 注入)

```csharp
// CP6.Tests/Wf/WebApiExecutorTests.cs
public class WebApiExecutorTests
{
    private sealed class FakeConn : IWfConnector {
        public string Name => "erpEcho"; public string DisplayName => "ERP Echo";
        public Task<ServiceTaskResult> CallAsync(string path, string? prms, ServiceTaskContext ctx)
            => Task.FromResult(ServiceTaskResult.Ok(new System.Collections.Generic.Dictionary<string,object?>{
                ["resolvedPath"]=path, ["jobId"]=ctx.JobId.ToString() }));
    }
    [Fact] public async Task Resolves_Connector_And_Templates_Path() {
        // actionRef: connectorName=erpEcho, path="/o/{orderId}", vars{orderId:PO-1}
        // 断言:CallAsync 收到 path 模板;executor 把 OutputVars 透传;Kind=="webApi";VisibleInDesigner==false
    }
    [Fact] public async Task UnknownConnector_Fails() {
        // connectorName 不在注册表 -> ServiceTaskResult.Fail 含未注册提示
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**(`--filter WebApiExecutorTests`)。
- [ ] **Step 3: 实现**
  - `WebApiExecutor(IEnumerable<IWfConnector> connectors)` → 字典 by Name。`ExecuteAsync`:`Parse(ctx.ActionRefJson)` 取 connectorName/path/paramsJson;字典找连接器,null→`Fail($"E-WF-018 连接器未注册:{name}")`;否则 `await conn.CallAsync(path, paramsJson, ctx)`。`Kind="webApi"`,`VisibleInDesigner=false`,`DisplayName="WebAPI"`,`Key="webApi"`。
  - `EchoConnector`:`CallAsync` 用 `ServiceVarsHelper` 求值 path/params(`$.`/`$wf.`),返回 `Ok(OutputVars{echoedPath, echoedParams, idempotencyKey=$"wf-service-job-{ctx.JobId}"})`。真实连接器会用 `IHttpClientFactory` + 预置 baseURL/认证,并发 `Idempotency-Key: wf-service-job-{JobId}` 头(spec §3.3);EchoConnector 仅 echo 供 QA。
  - `Program.cs` 注册两者(+ `AddHttpClient()` 若缺)。
- [ ] **Step 4: 跑验证 PASS + Wf 闸**。
- [ ] **Step 5: commit** — `git commit -m "feat(wfs-service-task): C-T1 WebApiExecutor+EchoConnector+注册"`

---

### Task C-T2: 样例 dataWriteback executor

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

---

### Task C-T3: 服务目录端点 GET /api/oa/designer/service-catalog

> **P1-6(过滤)落点。**

**Files:**
- Modify: DesignerController(执行时 Glob `**/DesignerController.cs` 确认路径)— 加 action
- Modify: DesignerService(Glob `**/DesignerService*.cs`)— 加 `GetServiceCatalog()` 返回 actions/connectors(注入 `IEnumerable<IServiceTaskExecutor>` + `IEnumerable<IWfConnector>`)
- Test: `CP6.Tests/Wf/ServiceCatalogTests.cs`(服务层)

- [ ] **Step 1: 写失败测试**(服务层,注入 fake executors/connectors)

```csharp
// 断言:actions 只含 Kind=="dataWriteback" && VisibleInDesigner==true 的(WebApiExecutor 不出现);
//       connectors 含全部;每项 {name,label(DisplayName)}
[Fact] public void GetServiceCatalog_FiltersWebApiExecutor_From_Actions() { ... }
```

- [ ] **Step 2: FAIL**(`--filter ServiceCatalogTests`)。
- [ ] **Step 3: 实现**
  - `DesignerService.GetServiceCatalog()` → `{ actions = execs.Where(e => e.Kind=="dataWriteback" && e.VisibleInDesigner).Select(e => new {name=e.Key, label=e.DisplayName}), connectors = conns.Select(c => new {name=c.Name, label=c.DisplayName}) }`。
  - `DesignerController` 加 `[HttpGet("service-catalog")]`,照既有 action 模式(`LocalizedControllerBase` / `Ok2(...)` / `ICurrentPermissionContext`)。
- [ ] **Step 4: PASS + Wf 闸**。
- [ ] **Step 5: commit** — `git commit -m "feat(wfs-service-task): C-T3 service-catalog 端点(按 Kind/VisibleInDesigner 过滤)"`

---

## Wave P-D — 设计器(前端 `cp6.web/src/views/oa/designer`)

> 纪律:视图全用 `t()`(运行时键,免重生 keys.generated);TS 类型加在 designerModel;每 Task 跑 `npm run test`(vitest)+ `npm run type-check`。前端 worktree 首用须 `npm ci`(已装则跳)。

### Task D-T1: designerModel — NODE_PALETTE 3 入口 + Service*/IsError round-trip + validateClient(vitest 可测核心)

**Files:**
- Modify: `cp6.web/src/views/oa/designer/designerModel.ts`
- Test: `cp6.web/src/views/oa/designer/__tests__/designerModel.serviceTask.spec.ts`(或既有 designerModel spec 同目录)

- [ ] **Step 1: 写失败 vitest**

```ts
import { describe, it, expect } from 'vitest'
import { schemaToGraph, graphToSchema, validateClient, NODE_PALETTE } from '../designerModel'

describe('serviceTask round-trip', () => {
  it('palette has 3 serviceTask entries', () => {
    const st = NODE_PALETTE.filter(p => p.type === 'serviceTask')
    expect(st.map(p => (p as any).kind).sort()).toEqual(['dataWriteback','timer','webApi'])
  })
  it('schemaToGraph/graphToSchema preserves Service* fields', () => {
    const schema = { nodes:[{ id:'s', type:'serviceTask', serviceKind:'webApi', serviceMode:'async',
      serviceConnectorName:'erpEcho', servicePath:'/o', serviceParamsJson:'{}', serviceMaxRetries:3 }],
      edges:[{ from:'s', to:'e', isError:true }] }
    const back = graphToSchema(schemaToGraph(schema as any))
    expect(back.nodes[0].serviceKind).toBe('webApi')
    expect(back.nodes[0].serviceConnectorName).toBe('erpEcho')
    expect(back.edges[0].isError).toBe(true)
  })
  it('validateClient flags incomplete serviceTask', () => {
    const schema = { nodes:[{ id:'s', type:'serviceTask', serviceKind:'webApi' /* 缺 connector/path */ }], edges:[] }
    const errs = validateClient(schema as any)
    expect(errs.some(e => e.includes('errServiceConfig') || e.includes('服务'))).toBe(true)
  })
})
```

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npm run test -- designerModel.serviceTask`。
- [ ] **Step 3: 实现** — `designerModel.ts`:
  - NODE_PALETTE 加 spec §5.1 三入口(`{type:'serviceTask', kind:'dataWriteback'|'webApi'|'timer', label, color}`)。
  - TS `SchemaNode` 加可选 `serviceKind?/serviceMode?/serviceActionName?/serviceConnectorName?/servicePath?/serviceParamsJson?/serviceDelayMode?/serviceDelayValue?/serviceMaxRetries?/serviceRetryBackoffSec?`;`SchemaEdge` 加 `isError?`。
  - `schemaToGraph`/`graphToSchema`:把这些字段读/写到节点 data / edge data(round-trip);调色板落点时按 kind 预置 `serviceKind`。
  - `validateClient`:serviceTask 缺必填(webApi 缺 connector/path、dataWriteback 缺 action、timer 缺 delay)→ push `errServiceConfig` 文案(镜像后端 E-WF-016)。
- [ ] **Step 4: PASS + `npm run type-check`**。
- [ ] **Step 5: commit** — `git commit -m "feat(wfs-service-task): D-T1 designerModel 调色板+Service*/IsError round-trip+validateClient"`

---

### Task D-T2: serviceTask 自定义节点组件 + 画布接线

**Files:**
- Create: `cp6.web/src/views/oa/designer/nodes/ServiceTaskNode.vue`
- Modify: `cp6.web/src/views/oa/designer/DesignerCanvas.vue`(注册节点类型 + 调色板渲染 serviceTask)

- [ ] **Step 1: 实现** — 仿既有 Start/Approval/Gateway/End 自定义节点(带 `<Handle>` 入/出),按 `data.serviceKind` 显示标签/图标/颜色(spec §5.2)。`DesignerCanvas.vue` 的 `:node-types` 注册 `serviceTask: ServiceTaskNode`;调色板拖拽 project() 落点生成 serviceTask 节点带预置 kind。
- [ ] **Step 2: 验证** — `npm run type-check` + `npm run build`(确认无 TS/编译错;Vue Flow 节点渲染 smoke 留 QA)。
- [ ] **Step 3: commit** — `git commit -m "feat(wfs-service-task): D-T2 ServiceTaskNode 自定义节点+画布接线"`

---

### Task D-T3: NodePropertyPanel 服务任务段 + EdgePropertyPanel 错误边 + 拉服务目录

**Files:**
- Modify: `cp6.web/src/views/oa/designer/NodePropertyPanel.vue`
- Modify: `cp6.web/src/views/oa/designer/EdgePropertyPanel.vue`
- Modify: `cp6.web/src/api/oa/designer.ts`(Glob 确认)— 加 `getServiceCatalog()` API + 类型

- [ ] **Step 1: 实现**(spec §5.3/§5.4/§5.5)
  - `designer.ts`:`getServiceCatalog(): Promise<{actions:{name,label}[], connectors:{name,label}[]}>`(`http.get('/oa/designer/service-catalog')`,沿用既有 API 模式)。
  - `NodePropertyPanel.vue`:节点 `type==='serviceTask'` 时显示服务任务段(el-collapse),按 `serviceKind` 切换:dataWriteback(动作下拉=catalog.actions / mode / 参数模板 textarea / 重试)、webApi(连接器下拉=catalog.connectors / 路径 / 参数 / mode / 重试)、timer(延时模式 radio / 延时值 / 可选动作 / 重试)。onMounted 拉 catalog。
  - `EdgePropertyPanel.vue`:加「失败边(IsError)」`el-checkbox` 绑 `edge.isError`(patch 回 designerModel)。
  - 文案全 `t('oa.designer.svc.*')`(键在 P-E/E-T2 seed)。
- [ ] **Step 2: 验证** — `npm run type-check` + `npm run build`。
- [ ] **Step 3: commit** — `git commit -m "feat(wfs-service-task): D-T3 属性面板服务任务段+错误边复选+服务目录拉取"`

---

## Wave P-E — 校验 + 错误码 + i18n + QA

### Task E-T1: FlowSchemaValidator serviceTask 规则 + DesignerService.save 注册校验

> **E-WF-016/017/018 + P2-3(非 end 须成功出边)落点。**

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowSchemaValidator.cs`
- Modify: DesignerService(Glob 确认)— save 时注册名校验
- Test: `CP6.Tests/Wf/ServiceTaskValidatorTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/ServiceTaskValidatorTests.cs
[Fact] public void WebApi_MissingConnector_E_WF_016() { /* serviceTask webApi 无 connector -> 抛/返回含 E-WF-016 */ }
[Fact] public void DataWriteback_MissingAction_E_WF_016() { }
[Fact] public void Timer_MissingDelay_E_WF_016() { }
[Fact] public void ServiceTask_NonEnd_NoSuccessEdge_E_WF_016() { /* 仅 IsError 出边或无出边 -> E-WF-016(P2-3) */ }
[Fact] public void MoreThanOneErrorEdge_E_WF_017() { /* 一节点 2 条 IsError 出边 -> E-WF-017 */ }
[Fact] public void ErrorEdge_FromNonServiceTask_E_WF_017() { /* IsError 边出自 approval 节点 -> E-WF-017 */ }
[Fact] public void ValidServiceTask_Passes() { /* webApi 全配齐 + 1 成功边 + ≤1 错误边 -> 通过 */ }
```

- [ ] **Step 2: 跑验证 FAIL**(`--filter ServiceTaskValidatorTests`)。
- [ ] **Step 3: 实现**(spec §6.1)— `FlowSchemaValidator` 加 serviceTask 分支:
  - `ServiceKind` 非法 / dataWriteback 缺 ActionName / webApi 缺 Connector|Path / timer 缺 DelayMode|DelayValue → E-WF-016。
  - 非 end 的 serviceTask 无非错误出边 → E-WF-016(P2-3)。
  - `IsError` 出边 >1 / `IsError` 边来源节点非 serviceTask → E-WF-017。
  - 沿用既有抛错/收集风格(参既有 E-WF-010/011 写法)。
- [ ] **Step 4: 写 DesignerService.save 注册校验测试 + 实现**(spec §6.2)— save 时引用的 `ServiceActionName`(在 dataWriteback executor 目录)/`ServiceConnectorName`(在连接器目录)未注册 → E-WF-018。测试注入 fake 目录,引用不存在名 → 抛 E-WF-018。
- [ ] **Step 5: PASS + Wf 闸 + commit** — `git commit -m "feat(wfs-service-task): E-T1 FlowSchemaValidator+save 校验 E-WF-016/017/018"`

---

### Task E-T2: i18n 五语 seed

**Files:**
- Create: `I18nOaServiceTaskScreenSeed.cs`(Glob `**/I18nOaApproverScreenSeed.cs` 同目录/同模式)
- Modify: `CP6.WebApi/Program.cs`(concat 进 i18n seed 链,带去重)
- Test:(seed 是静态数据,靠 build + 启动 + QA 验;可加一个 key 唯一性单测可选)

- [ ] **Step 1: 实现** — 仿 `I18nOaApproverScreenSeed` 静态 `Sys_Lang[] Items`,五语(zh-CN/zh-TW/en/ja/…按既有语种)键:
  - 面板:`oa.designer.svc.kind/.mode/.action/.connector/.path/.params/.delayMode/.delayValue/.maxRetries/.backoff/.errorEdge` + 三 kind 标签 + delayMode 三选项。
  - 错误:`E-WF-016/E-WF-017/E-WF-018` 文案 + 前端 `errServiceConfig`。
  - **去重**:grep 已有 seed(I18nOaApprover/Inbox/Advanced/Designer)避免 LangKey 重复(参 approver seed 9 键 dedup 做法)。
  - `Program.cs` concat 链加 `.Concat(I18nOaServiceTaskScreenSeed.Items)`(带去重逻辑,同既有)。
- [ ] **Step 2: build 验证** — `dotnet build CP6.WebApi/CP6.WebApi.csproj`(无重复键编译期不报,运行期 SeedLangs 幂等去重)。
- [ ] **Step 3: commit** — `git commit -m "feat(wfs-service-task): E-T2 I18nOaServiceTaskScreenSeed 五语+concat"`

---

### Task E-T3: gstack QA harness(只写不跑)

**Files:**
- Create: `docs/superpowers/qa/wfs-service-task/README.md`(剧本)
- Create: `docs/superpowers/qa/wfs-service-task/seed.sql`(含 serviceTask 节点的 FlowDef + 表单)
- Create: `docs/superpowers/qa/wfs-service-task/qa_service_task.ps1`(HTTP e2e 脚本,ASCII 数据)

- [ ] **Step 1: 写 harness**(参既有 `docs/superpowers/qa/wfs-serial-signing/` / `wfs-approver-resolve` README+seed+ps1 模式)。剧本覆盖:
  1. **sync 数据回写**:设计 start→svc(dataWriteback sync, sampleWriteback)→end;发起→实例直接 Approved,VarsJson 含 writebackEcho。
  2. **async webApi**:start→svc(webApi async, erpEcho)→end;发起→实例 Running + 1 Pending job;跑/等 worker(或手调 ScanOnce 端点?无则等 20s)→实例 Approved,VarsJson 含 echo。
  3. **timer 纯等待**:short duration(如 PT10S)→等到点→advance。
  4. **timer 到点动作**:duration + erpEcho → 到点执行 echo → advance。
  5. **失败→重试→错误边**:配 EchoConnector 失败模式(或不存在 connector)+ IsError 边→耗尽走错误边,下游 human 节点出现 + VarsJson 含 `wf.serviceError`。
  6. **失败→挂起**:同上但无 IsError 边→实例 Suspended。
  - 真浏览器:设计器 3 调色板入口、按 kind 属性面板、错误边复选。
  - seed.sql 对 OA 表用单数表名(`Wf_FlowDef`/`Wf_FormDef`),`SET QUOTED_IDENTIFIER ON`。
- [ ] **Step 2: commit** — `git commit -m "test(wfs-service-task): E-T3 gstack QA harness(6 剧本+seed+e2e 脚本)"`
- [ ] **Step 3: 末期 live QA(用户在场)** — 隔离库 `CP6DB_OA` 起后端(避 Space 端口)+ 前端 → 跑 ps1 HTTP e2e + gstack 真浏览器。**抓 bug 当场 TDD 修**。

---

## DoD / 验收

- [ ] 后端 `dotnet test CP6.Tests/CP6.Tests.csproj` 全绿;**`--filter Wf` 既有测试字节等价**(零回归)。
- [ ] 6 个 P0 护栏各有专测且绿(见下「覆盖核对」)。
- [ ] §11 六条高价值测试齐全且绿。
- [ ] EF `dotnet ef migrations has-pending-model-changes ...` clean。
- [ ] 前端 `npm run type-check` / `npm run test`(vitest)/ `npm run build` 全绿。
- [ ] **整支零 Space 污染**(`git diff --stat <base>..HEAD` 无 `views/space`/`*Space*`/Space 迁移)。
- [ ] gstack QA harness 齐(6 剧本)+ live QA 全过(用户在场,隔离库 CP6DB_OA)。
- [ ] 错误码 E-WF-016/017/018 各有校验测试。

### 覆盖核对(6 P0 + 6 高价值测试 → 任务)

| 护栏/测试 | 任务 |
|---|---|
| P0-1 AttemptCount/MaxAttempts | A-T1(字段)/ A-T6(入队)/ B-T1(退避计数) |
| P0-2 ResumeServiceTokenAsync 幂等 | A-T5 + 测 `ResumeServiceTokenTests.Resume_NoOp_When*` |
| P0-3 live-token 防重入队 | A-T1(filtered unique)/ A-T6(`Enqueue_Dedupe_SameTokenNode`) |
| P0-4 lease reaper | B-T1(`Reaper_ResetsExpiredLease_Only`) |
| P0-5 撤回后 Running job 状态闸 | B-T1(`StateGate_*`)/ B-T3(Pending cancel) |
| P0-6 UTC + 租户时区 | A-T1(*Utc 字段)/ A-T6(ComputeDueUtc untilDate)/ B-T2(UtcNow) |
| 高价值①重复入队保护 | A-T6 `Enqueue_Dedupe_SameTokenNode` |
| 高价值②恢复幂等 | A-T5 `Resume_NoOp_WhenTokenLeftNode` |
| 高价值③撤回后 worker 返回不恢复 | B-T1 `StateGate_InstanceWithdrawn_CancelsJob_NoExecute` |
| 高价值④reaper 不误杀未过期 lease | B-T1 `Reaper_ResetsExpiredLease_Only` |
| 高价值⑤timer untilDate 租户时区 | A-T6(ComputeDueUtc 测 untilDate→UTC) |
| 高价值⑥error edge 写错误变量 | B-T1 `Exhausted_WithErrorEdge_RoutesAndWritesServiceError` |

### 执行顺序与依赖

A-T1 → A-T2 → A-T3 → A-T4 → A-T5 → A-T6(P-A 顺序,A-T5/A-T6 依赖前面契约)→ B-T1(依赖 A-T5/A-T6)→ B-T2 → B-T3 → C-T1 → C-T2 → C-T3 → D-T1 → D-T2 → D-T3 → E-T1 → E-T2 → E-T3。P-C 可与 P-B 部分并行(C 依赖 A 的契约,不依赖 B);P-D 依赖 C-T3(服务目录)+ A-T1(字段);E-T1 依赖 A-T1/A-T4。
