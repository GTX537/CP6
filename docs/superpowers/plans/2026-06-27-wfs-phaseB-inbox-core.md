# WFS Phase B（信箱核心）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Phase A 落成的 token 内核 + 读模型（`Wf_FlowFormTo`/`Wf_FlowData`/`Wf_FlowCc`）之上，建一套「电子表单信箱」应用层：四文件夹（未處理/在途/已處理/暫存）+ 抄送 + 草稿 + 已读 + 仪表盘 + 批量办理 + 详情左读右签（含预计流程 forecast）+ 轻量流程管理 UI，旧 wf 视图重定向，五语 i18n，gstack QA 跑通。

**Architecture:** 三层中的 L2（应用层），**只读 L1、写动作经 L0 引擎**。新服务全部置 `CP6.Core/Services/Oa/`（消费 `Wf` 引擎，引擎零 churn）；唯一对 `Wf` 的增量是给引擎加 `StartDraftAsync`（草稿就地进流程）+ 给 `Wf_FlowTask` 加 `IsRead/ReadAt` + 给 `FlowInstanceStatus` 加 `Draft`。前端新建 `cp6.web/src/views/oa/`，复用 `DynamicForm.vue`/`fieldMask.ts`；旧 `/wf/todo`、`/wf/my-applications` 302 重定向到 `/oa/inbox`。

**Tech Stack:** .NET 8 / EF Core（SqlServer 运行期 + InMemory 测试）/ xUnit（`CP6.Tests`）；Vue 3 + Element Plus 2.13 + Pinia + vue-i18n（5 语）/ Vite / Vitest（node env，纯逻辑单测）。后端启动项目 `CP6.WebApi`，DbContext + 迁移在 `CP6.Core`。

**配套 spec（落码前必读）：**
- `docs/superpowers/specs/2026-06-26-wfs-form-inbox-unified-design.md`（umbrella；本计划落 §4 信箱应用设计 + §5 阶段 B）
- `docs/superpowers/specs/2026-06-26-wfs-runtime-kernel-design.md`（L0 内核，引用）
- `docs/superpowers/plans/2026-06-26-wfs-phaseA-engine-readmodel.md`（Phase A，已交付，本计划依赖其读模型）

---

## Scope Check（本计划含两 Part，按顺序执行）

- **Part A（T1~T11）= 后端应用服务**：`Services/Oa` 五服务 + 引擎 `StartDraftAsync` + L2 数据模型 + 控制器。独立可测、可交付（xUnit 全绿）。
- **Part B（T12~T19）= 前端信箱 + 流程管理 UI + 重定向 + i18n + gstack QA**：依赖 Part A 的 REST 端点。

两部分顺序执行。Part A 完成即一个可交付里程碑（API 可被 Postman/集成测试驱动）。**Phase C（代理 act-as / 转交 / 填單表单库 / 表單查詢 / 設定）与 C′（完整设计器）不在本计划**——见 umbrella §5。

---

## File Structure（先锁分解）

**后端新建（`CP6.Core/Services/Oa/`，新目录）：**
- `OaUserNames.cs` — 用户 Id→显示名（NickName ?? UserName）批量解析（DRY，多服务共用）
- `InboxModels.cs` — 信箱 DTO（records）：列表项/统计/详情/时间线/快照/CC
- `IInboxService.cs` / `InboxService.cs` — 未處理(待审核+CC)/在途/已處理/仪表盘/已读/批量/详情
- `IDraftService.cs` / `DraftService.cs` — 草稿 增改提删列
- `IForecastService.cs` / `ForecastService.cs` — 预计流程前推（FormDetail 预计段 + 发起预览共用）
- `IFlowAdminService.cs` / `FlowAdminService.cs` — 轻量流程管理（流程列表/启用/绑定 + 1:1 校验）

**后端修改：**
- `CP6.Core/Services/Wf/WfStatus.cs` — `FlowInstanceStatus` 加 `Draft = 5`
- `CP6.Entity/DomainModels/Wf/Wf_FlowTask.cs` — 加 `IsRead` / `ReadAt`
- `CP6.Core/Services/Wf/IFlowEngine.cs` — 加 `StartDraftAsync`
- `CP6.Core/Services/Wf/FlowEngine.cs` — 实现 `StartDraftAsync`（草稿就地进流程）
- `CP6.Core/EFDbContext/CP6Context.cs` — `Wf_FlowTask` 索引补 `(AssigneeId, IsRead)`（可选）；无新 DbSet（草稿复用 FlowInstance）
- `CP6.WebApi/Program.cs` — 注册 4 个 Oa 服务 + i18n 种子合并

**后端控制器新建（`CP6.WebApi/Controllers/Oa/`，新目录）：**
- `InboxController.cs` / `DraftController.cs` / `ForecastController.cs` / `FlowAdminController.cs`

**后端迁移：** `WfsPhaseBInboxL2`（`Wf_FlowTask` 加 IsRead/ReadAt 两列；Draft 是 Status 值不需列）。

**后端测试（`CP6.Tests/Oa/`，新目录）：** `InboxServiceTests.cs`、`DraftServiceTests.cs`、`ForecastServiceTests.cs`、`FlowAdminServiceTests.cs`。

**前端新建：**
- `cp6.web/src/api/oa/inbox.ts` / `draft.ts` / `forecast.ts` / `flowAdmin.ts`
- `cp6.web/src/types/oa/inbox.ts` — TS 接口（对齐后端 DTO）
- `cp6.web/src/views/oa/inbox/inboxModel.ts`（+ `inboxModel.test.ts`）— 纯逻辑（状态文案/时间线合并/分支分组）
- `cp6.web/src/views/oa/inbox/InboxView.vue` — 壳（左文件夹 + 顶栏 + 内容区 + 详情抽屉 + 新建对话框）
- `cp6.web/src/views/oa/inbox/InboxDashboard.vue` / `InboxPending.vue` / `InboxRunning.vue` / `InboxDone.vue` / `InboxDraft.vue`
- `cp6.web/src/views/oa/inbox/FormDetail.vue` — 左读右签详情
- `cp6.web/src/views/oa/inbox/FlowTimeline.vue` — 传签时间线（持久行 + 预计段）
- `cp6.web/src/views/oa/admin/FlowAdmin.vue` — 轻量流程管理

**前端修改：**
- `cp6.web/src/router/index.ts` — `viewModules` 加 `/oa/inbox`、`/oa/flow-admin`；静态 routes 加 `/wf/todo`、`/wf/my-applications` 重定向；删 viewModules 里这两旧条目
- `CP6.WebApi/Seed/I18nOaInboxScreenSeed.cs`（新）— 五语词条 + 菜单 nav

---

## 通用约定

- **测试基线**：`dotnet test CP6.Tests`（Phase A 末 1212 测 / 1 skip）。每 Task 末跑相关测试；Part A 收尾跑全量。
- **兼容硬闸**：本计划**零改 `Wf` 既有服务行为**（仅加 `StartDraftAsync` 新方法 + Task 加两列），`dotnet test CP6.Tests --filter "FullyQualifiedName~Wf"` 任一既有测试转红 = 兼容破坏，回退排查。
- **测试 DB 工厂**（沿用 `ReadModelHookTests`）：
  ```csharp
  private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
  private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
  ```
- **服务装配（测试内手 new，对齐 DI）**：
  ```csharp
  IForecastService Forecast(CP6Context db) => new ForecastService(db, new ApproverResolver(db));
  IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db), Forecast(db));
  IDraftService Draft(CP6Context db) => new DraftService(db, Engine(db));
  IFlowAdminService Admin(CP6Context db) => new FlowAdminService(db);
  ```
- **EF 迁移命令**：`dotnet ef migrations add WfsPhaseBInboxL2 -p CP6.Core -s CP6.WebApi`。InMemory 测试不走迁移。
- **错误码（沿用 §4.6 `E-WF-0xx`，服务抛 `InvalidOperationException("E-WF-0xx")` → 控制器 catch 转 BadRequest）**：
  - `E-WF-003` 草稿越权提交（非本人草稿 / 非草稿态）
  - `E-WF-004` 批量含无效/已办任务（逐条回报，不整体失败）
  - `E-WF-006` 流程定义不存在或已停用
  - `E-WF-008` 该表单已绑定其他启用流程（违反 1 表单 ↔ 1 流程）
- **commit**：每 Task 末本地 commit（不 push；push 由用户自跑）。
- **分支**：本计划在新分支 `feat/wfs-inbox-core`（`git checkout -b feat/wfs-inbox-core`，若隔离 worktree 已建则在其中）。

---

# Part A — 后端应用服务

## Task 1：L2 数据模型 — Draft 状态 + Task 已读列 + 迁移

**Files:**
- Modify: `CP6.Core/Services/Wf/WfStatus.cs`
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FlowTask.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`
- Test: `CP6.Tests/Oa/InboxL2ModelTests.cs`

- [ ] **Step 1: 写失败测试**

`CP6.Tests/Oa/InboxL2ModelTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class InboxL2ModelTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Task_IsRead_DefaultsFalse_AndPersists()
    {
        using var db = NewDb();
        var t = new Wf_FlowTask { Id = Guid.NewGuid(), InstanceId = Guid.NewGuid(), NodeId = "n1",
            AssigneeId = Guid.NewGuid(), Status = FlowTaskStatus.Pending };
        db.Wf_FlowTasks.Add(t);
        await db.SaveChangesAsync();

        var got = await db.Wf_FlowTasks.SingleAsync();
        Assert.False(got.IsRead);
        Assert.Null(got.ReadAt);

        got.IsRead = true; got.ReadAt = new DateTime(2026, 6, 27);
        await db.SaveChangesAsync();
        Assert.True((await db.Wf_FlowTasks.SingleAsync()).IsRead);
    }

    [Fact]
    public void FlowInstanceStatus_HasDraft()
    {
        Assert.Equal(5, FlowInstanceStatus.Draft);
        Assert.NotEqual(FlowInstanceStatus.Draft, FlowInstanceStatus.Running);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~InboxL2ModelTests"`
Expected: 编译失败（`Wf_FlowTask.IsRead` / `FlowInstanceStatus.Draft` 未定义）。

- [ ] **Step 3: `FlowInstanceStatus` 加 `Draft`（`WfStatus.cs`）**

在 `FlowInstanceStatus` 类内（现有 `Running=0…Suspended=4` 之后）追加：
```csharp
/// <summary>草稿（暫存）：有实例、无 token、未进流程（umbrella R2）。提交即 StartDraftAsync 就地起 token。</summary>
public const int Draft = 5;
```

- [ ] **Step 4: `Wf_FlowTask` 加已读列**

在 `Wf_FlowTask.cs` 类内追加（顶部确保 `using System;`）：
```csharp
/// <summary>未處理"未读"标记（信箱 L2）。送签建任务时默认 false；信箱打开详情/标记已读置 true。</summary>
public bool IsRead { get; set; }

/// <summary>标记已读时刻（幂等：已 true 不重置）。</summary>
public DateTime? ReadAt { get; set; }
```

- [ ] **Step 5: `CP6Context.cs` 补索引（`Wf_FlowTask` 索引区）**

在现有 `Wf_FlowTask` 的 `modelBuilder.Entity<Wf_FlowTask>(e => {...})` 块内追加一行：
```csharp
e.HasIndex(x => new { x.AssigneeId, x.IsRead }).HasDatabaseName("IX_Wf_FlowTask_AssigneeRead");
```

- [ ] **Step 6: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~InboxL2ModelTests"`
Expected: PASS。

- [ ] **Step 7: 加迁移**

Run: `dotnet ef migrations add WfsPhaseBInboxL2 -p CP6.Core -s CP6.WebApi`
Expected: 生成迁移仅 `Wf_FlowTask` 加 `IsRead`（bit, default 0）+ `ReadAt`（datetime2 NULL）+ 新索引。打开生成文件核对 `Up()` 无数据迁移、无其他表改动。

- [ ] **Step 8: 兼容回归**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~Wf"`
Expected: 全绿（仅加列/加状态值常量，零行为改动）。

- [ ] **Step 9: Commit**

```bash
git add CP6.Core/Services/Wf/WfStatus.cs CP6.Entity/DomainModels/Wf/Wf_FlowTask.cs CP6.Core/EFDbContext/CP6Context.cs CP6.Core/Migrations/ CP6.Tests/Oa/InboxL2ModelTests.cs
git commit -m "feat(wfs-B): T1 L2 数据模型 FlowInstanceStatus.Draft + Wf_FlowTask.IsRead/ReadAt + 迁移"
```

---

## Task 2：引擎 `StartDraftAsync`（草稿就地进流程）

**Files:**
- Modify: `CP6.Core/Services/Wf/IFlowEngine.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`
- Test: `CP6.Tests/Oa/DraftServiceTests.cs`（先建文件 + 引擎层一例）

> 草稿 = `Wf_FlowInstance.Status=Draft`（有实例、无 token）。提交 = 加载该实例 → 置 Running → spawn 根 token → 进首节点。逻辑等价 `SubmitAsync` 的"建实例后半段"，但作用于已存在实例（不新建）。

- [ ] **Step 1: 写失败测试**

`CP6.Tests/Oa/DraftServiceTests.cs`（建文件，先放引擎层一例）：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class DraftServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task SeedFlowAsync(CP6Context db, Guid approver, string key = "leave")
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = key, FlowName = key, FormKey = key,
            SchemaJson = JsonSerializer.Serialize(new FlowSchema
            {
                Nodes =
                {
                    new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                    new FlowNode { Id = "end", Type = "end" },
                },
                Edges = { new FlowEdge { From = "n1", To = "end" } },
            }),
            Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task StartDraftAsync_DraftInstance_EntersFlow()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid(); var starter = Guid.NewGuid();
        await SeedFlowAsync(db, approver);

        // 手工建一个草稿实例（无 token、无 task）
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "leave", StarterId = starter,
            Status = FlowInstanceStatus.Draft, CurrentNode = "", VarsJson = """{"days":2}""", Creator = starter.ToString() };
        db.Wf_FlowInstances.Add(inst);
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync());

        await Engine(db).StartDraftAsync(inst.Id, starter);

        var got = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Running, got.Status);
        Assert.Equal("n1", got.CurrentNode);
        Assert.Equal(1, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == approver && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(1, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Pending)); // 读模型随推进落库
    }

    [Fact]
    public async Task StartDraftAsync_NotOwner_Throws()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid(); var starter = Guid.NewGuid();
        await SeedFlowAsync(db, approver);
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "leave", StarterId = starter,
            Status = FlowInstanceStatus.Draft, CurrentNode = "", VarsJson = "{}", Creator = starter.ToString() };
        db.Wf_FlowInstances.Add(inst);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).StartDraftAsync(inst.Id, Guid.NewGuid()));
        Assert.Equal("E-WF-003", ex.Message);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~DraftServiceTests.StartDraftAsync"`
Expected: 编译失败（`StartDraftAsync` 未定义）。

- [ ] **Step 3: `IFlowEngine` 加方法**

`IFlowEngine.cs` 接口内追加：
```csharp
/// <summary>就地起草稿：把 Draft 实例推进进流程（spawn 根 token + 进首节点 + 读模型随推进落库）。
/// 仅发起人可提交；非草稿态/越权 → E-WF-003。幂等性同 SubmitAsync（一次 SaveChanges）。</summary>
Task StartDraftAsync(Guid instanceId, Guid actorId);
```

- [ ] **Step 4: `FlowEngine.cs` 实现（紧随 `SubmitAsync` 之后）**

```csharp
public async Task StartDraftAsync(Guid instanceId, Guid actorId)
{
    var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId)
               ?? throw new InvalidOperationException("E-WF-003");
    if (inst.StarterId != actorId) throw new InvalidOperationException("E-WF-003");        // 越权提交
    if (inst.Status != FlowInstanceStatus.Draft) throw new InvalidOperationException("E-WF-003"); // 非草稿态

    var schema = await LoadSchemaAsync(inst.FlowKey);
    var first = FirstNode(schema) ?? throw new InvalidOperationException($"流程 {inst.FlowKey} 无节点");

    inst.Status = FlowInstanceStatus.Running;
    inst.CurrentNode = first.Id;
    inst.Modifier = actorId.ToString();
    inst.ModifyDate = DateTime.Now;
    AddHistory(inst.Id, first.Id, actorId, "submit", null);

    var root = SpawnToken(inst, first, parent: null, fork: null);
    await EnterNodeAsync(inst, schema, first, root);
    await DispatchIfFinishedAsync(inst, actorId, null);
    await _db.SaveChangesAsync();
}
```
> `LoadSchemaAsync`/`FirstNode`/`AddHistory`/`SpawnToken`/`EnterNodeAsync`/`DispatchIfFinishedAsync` 均为 `FlowEngine` 既有成员（见 `SubmitAsync`），同类内直接调。

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~DraftServiceTests.StartDraftAsync"`
Expected: PASS。

- [ ] **Step 6: 兼容回归**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~Wf"`
Expected: 全绿（仅加新方法）。

- [ ] **Step 7: Commit**

```bash
git add CP6.Core/Services/Wf/IFlowEngine.cs CP6.Core/Services/Wf/FlowEngine.cs CP6.Tests/Oa/DraftServiceTests.cs
git commit -m "feat(wfs-B): T2 引擎 StartDraftAsync 草稿就地进流程(读模型随推进落库)"
```

---

## Task 3：Oa 共享件 — `OaUserNames` + `InboxModels` DTO

**Files:**
- Create: `CP6.Core/Services/Oa/OaUserNames.cs`
- Create: `CP6.Core/Services/Oa/InboxModels.cs`
- Test: `CP6.Tests/Oa/OaUserNamesTests.cs`

- [ ] **Step 1: 写失败测试**

`CP6.Tests/Oa/OaUserNamesTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class OaUserNamesTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task ResolveAsync_PrefersNickName_FallsBackToUserName()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = a, UserName = "alice", NickName = "Alice 王", Password = "x" },
            new Sys_User { Id = b, UserName = "bob", NickName = null, Password = "x" });
        await db.SaveChangesAsync();

        var names = await OaUserNames.ResolveAsync(db, new[] { a, b, Guid.Empty });
        Assert.Equal("Alice 王", names[a]);
        Assert.Equal("bob", names[b]);
        Assert.False(names.ContainsKey(Guid.Empty));   // 空 Guid 不查
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~OaUserNamesTests"`
Expected: 编译失败（`OaUserNames` 未定义）。

- [ ] **Step 3: 建 `OaUserNames.cs`**

```csharp
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

/// <summary>用户 Id → 显示名（NickName ?? UserName）批量解析。信箱多服务共用（DRY）。租户自动隔离。</summary>
public static class OaUserNames
{
    public static async Task<Dictionary<Guid, string>> ResolveAsync(CP6Context db, IEnumerable<Guid> ids)
    {
        var set = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (set.Count == 0) return new();
        return await db.Sys_Users
            .Where(u => set.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.NickName) ? u.UserName : u.NickName!);
    }
}
```

- [ ] **Step 4: 建 `InboxModels.cs`（全部 DTO records，后续 Task 共用）**

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Oa;

// ── 列表项 ──
public record InboxPendingItem(Guid TaskId, Guid InstanceId, Guid? TokenId, string FlowKey, string? FlowName,
    string NodeId, string? NodeName, Guid StarterId, string StarterName, string? BizType, string? BizId,
    bool IsRead, DateTime SentAt);

public record InboxCcItem(Guid CcId, Guid InstanceId, string FlowKey, string? FlowName, string? AtNodeId,
    Guid StarterId, string StarterName, bool IsRead, DateTime CreateDate);

public record InboxRunningItem(Guid InstanceId, string FlowKey, string? FlowName, string CurrentNode,
    int Status, IReadOnlyList<string> CurrentHandlers, DateTime CreateDate);

public record InboxDoneItem(Guid InstanceId, string FlowKey, string? FlowName, Guid StarterId, string StarterName,
    int FormToStatus, DateTime DoneAt, int InstanceStatus);

// ── 仪表盘 ──
public record TrendPoint(string Date, int Count);
public record InboxStats(int PendingCount, int RunningCount, int DoneThisMonth, int RejectedBackToMe,
    IReadOnlyList<TrendPoint> Trend, IReadOnlyList<InboxPendingItem> RecentPending);

// ── 批量 ──
public record BatchActResultItem(Guid TaskId, bool Ok, string? Error);

// ── 详情（左读右签）──
public record TimelineRow(int StepSeq, Guid? TokenId, string NodeId, string? NodeName,
    Guid ExpectedHandlerId, string ExpectedHandlerName, Guid? ActualHandlerId, string? ActualHandlerName,
    Guid? OnBehalfOfId, string? OnBehalfOfName, int Status, string? Comment, DateTime SentAt, DateTime? HandledAt);

public record SnapshotRow(int StepSeq, string NodeId, string DataJson);
public record CcRow(Guid RecipientId, string RecipientName, string? AtNodeId, bool IsRead);

public record InboxDetail(Wf_FlowInstance Instance, string? FlowName, string? FormKey, string? FormSchemaJson,
    string CurrentDataJson, IReadOnlyList<TimelineRow> Timeline, IReadOnlyList<SnapshotRow> Snapshots,
    IReadOnlyList<ForecastStep> Forecast, IReadOnlyList<CcRow> Cc);

// ── 预计流程（ForecastService 产出，置此便于 InboxDetail 引用）──
public record ForecastStep(string NodeId, string? NodeName, string Type, IReadOnlyList<string> Approvers,
    bool Resolved, string? Note);
public record ForecastResult(IReadOnlyList<ForecastStep> Steps, bool Branched);
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~OaUserNamesTests"`
Expected: PASS。

- [ ] **Step 6: Commit**

```bash
git add CP6.Core/Services/Oa/OaUserNames.cs CP6.Core/Services/Oa/InboxModels.cs CP6.Tests/Oa/OaUserNamesTests.cs
git commit -m "feat(wfs-B): T3 Oa 共享件 OaUserNames + InboxModels DTO"
```

---

## Task 4：`ForecastService` 预计流程前推

**Files:**
- Create: `CP6.Core/Services/Oa/IForecastService.cs`
- Create: `CP6.Core/Services/Oa/ForecastService.cs`
- Test: `CP6.Tests/Oa/ForecastServiceTests.cs`

> 从当前状态（详情：`fromNodeId=inst.CurrentNode`）或起点（发起预览：`fromNodeId=null`）**前推 schema**：逐转移取首个条件为真的边、并行分叉标 `Branched`、调 `IApproverResolver` 预解析后续审批人。能解析显具体人名，不能解析显关卡名占位（`Resolved=false`）。FormDetail 预计段 + FormInitiate 提交前预览共用同一前推算法（umbrella §1.5.2 / §4.3 / W1）。

- [ ] **Step 1: 写失败测试**

`CP6.Tests/Oa/ForecastServiceTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class ForecastServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IForecastService Forecast(CP6Context db) => new ForecastService(db, new ApproverResolver(db));

    private static async Task SeedLinearAsync(CP6Context db, Guid u1)
    {
        db.Sys_Users.Add(new Sys_User { Id = u1, UserName = "mgr", NickName = "经理张", Password = "x" });
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "f", FlowName = "请假", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = {
                    new FlowNode { Id = "n1", Name = "直属主管", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
                    new FlowNode { Id = "n2", Name = "HR 审核", Type = "approval", ApproverStrategy = "Role", ApproverRoleId = 999 },
                    new FlowNode { Id = "end", Name = "结束", Type = "end" },
                },
                Edges = { new FlowEdge { From = "n1", To = "n2" }, new FlowEdge { From = "n2", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Forecast_Linear_ResolvedAndPlaceholder()
    {
        using var db = NewDb();
        var u1 = Guid.NewGuid();
        await SeedLinearAsync(db, u1);

        // 从起点前推（发起预览）
        var res = await Forecast(db).ForecastAsync("f", "{}", Guid.NewGuid(), fromNodeId: null);

        Assert.Equal(3, res.Steps.Count);                 // n1, n2, end
        Assert.False(res.Branched);
        var s1 = res.Steps[0];
        Assert.Equal("n1", s1.NodeId);
        Assert.True(s1.Resolved);
        Assert.Contains("经理张", s1.Approvers);           // Specified 可前解析 → 显人名
        var s2 = res.Steps[1];
        Assert.Equal("n2", s2.NodeId);
        Assert.False(s2.Resolved);                        // Role 999 无人 → 占位
        Assert.Empty(s2.Approvers);
        Assert.Equal("end", res.Steps[2].NodeId);
    }

    [Fact]
    public async Task Forecast_FromCurrentNode_SkipsDone()
    {
        using var db = NewDb();
        var u1 = Guid.NewGuid();
        await SeedLinearAsync(db, u1);

        // 详情视角：当前停在 n1，预计段应从 n1 的下一步（n2）开始
        var res = await Forecast(db).ForecastAsync("f", "{}", Guid.NewGuid(), fromNodeId: "n1");
        Assert.Equal(2, res.Steps.Count);                 // n2, end（不含已到达的 n1）
        Assert.Equal("n2", res.Steps[0].NodeId);
    }

    [Fact]
    public async Task Forecast_ParallelSplit_MarksBranched()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "p", FlowName = "p", FormKey = "p",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = {
                    new FlowNode { Id = "s", Type = "parallelSplit" },
                    new FlowNode { Id = "a", Name = "A 审", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                    new FlowNode { Id = "j", Type = "parallelJoin" },
                    new FlowNode { Id = "end", Type = "end" },
                },
                Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "j" }, new FlowEdge { From = "j", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();

        var res = await Forecast(db).ForecastAsync("p", "{}", Guid.NewGuid(), fromNodeId: null);
        Assert.True(res.Branched);                        // 含 parallelSplit
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~ForecastServiceTests"`
Expected: 编译失败（`IForecastService`/`ForecastService` 未定义）。

- [ ] **Step 3: 建 `IForecastService.cs`**

```csharp
namespace CP6.Core.Services.Oa;

/// <summary>预计流程前推（umbrella §4.3）。FormDetail 预计段 + FormInitiate 提交前预览共用。</summary>
public interface IForecastService
{
    /// <param name="fromNodeId">null=从起点前推（发起预览）；非 null=从该当前关卡的下一步前推（详情预计段）。</param>
    Task<ForecastResult> ForecastAsync(string flowKey, string varsJson, Guid starterId, string? fromNodeId = null);
}
```

- [ ] **Step 4: 建 `ForecastService.cs`**

```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CP6.Core.Services.Oa;

public class ForecastService : IForecastService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly CP6Context _db;
    private readonly IApproverResolver _approver;
    public ForecastService(CP6Context db, IApproverResolver approver) { _db = db; _approver = approver; }

    public async Task<ForecastResult> ForecastAsync(string flowKey, string varsJson, Guid starterId, string? fromNodeId = null)
    {
        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.FlowKey == flowKey && x.Enable)
                  ?? throw new InvalidOperationException("E-WF-006");
        var schema = JsonSerializer.Deserialize<FlowSchema>(def.SchemaJson, JsonOpts) ?? new FlowSchema();

        var steps = new List<ForecastStep>();
        var visited = new HashSet<string>();
        bool branched = false;

        // 起点：详情 → fromNodeId 的"下一节点"（fromNodeId 已到达，预计=之后）；发起 → 首节点
        var cursor = fromNodeId is null
            ? (!string.IsNullOrEmpty(schema.Start) ? schema.Start : schema.Nodes.FirstOrDefault()?.Id)
            : NextNodeId(schema, fromNodeId, varsJson);

        int guard = 0;
        while (cursor is not null && visited.Add(cursor) && guard++ < 100)
        {
            var node = schema.Nodes.FirstOrDefault(n => n.Id == cursor);
            if (node is null) break;
            var type = (node.Type ?? "approval").Trim().ToLowerInvariant();

            switch (type)
            {
                case "end":
                    steps.Add(new ForecastStep(node.Id, node.Name, "end", Array.Empty<string>(), true, null));
                    cursor = null;
                    break;
                case "start":
                    cursor = NextNodeId(schema, cursor, varsJson);
                    break;
                case "parallelsplit":
                    branched = true;
                    steps.Add(new ForecastStep(node.Id, node.Name, "parallelSplit", Array.Empty<string>(), true, "并行分叉"));
                    cursor = NextNodeId(schema, cursor, varsJson);   // 乐观单链：取首出边（umbrella §4.3）
                    break;
                case "paralleljoin":
                    steps.Add(new ForecastStep(node.Id, node.Name, "parallelJoin", Array.Empty<string>(), true, "汇聚"));
                    cursor = NextNodeId(schema, cursor, varsJson);
                    break;
                default: // approval
                    var (names, resolved) = await ResolveApproverNamesAsync(node, starterId);
                    steps.Add(new ForecastStep(node.Id, node.Name, "approval", names, resolved,
                        resolved ? null : "审批人到达时解析"));
                    cursor = NextNodeId(schema, cursor, varsJson);
                    break;
            }
        }
        return new ForecastResult(steps, branched);
    }

    private static string? NextNodeId(FlowSchema schema, string from, string varsJson)
    {
        foreach (var e in schema.Edges.Where(e => e.From == from))
            if (ExpressionEvaluator.Evaluate(e.Condition, varsJson)) return e.To;
        return null;
    }

    private async Task<(IReadOnlyList<string> Names, bool Resolved)> ResolveApproverNamesAsync(FlowNode node, Guid starterId)
    {
        if (!Enum.TryParse<ApproverStrategy>(node.ApproverStrategy, ignoreCase: true, out var strat))
            return (Array.Empty<string>(), false);
        try
        {
            var rule = new ApproverRule(strat, node.ApproverLevels, node.ApproverRoleId, node.ApproverUserId);
            var res = await _approver.ResolveAsync(rule, new ApproverResolveContext { StarterUserId = starterId });
            if (!res.Resolved) return (Array.Empty<string>(), false);
            var names = await OaUserNames.ResolveAsync(_db, res.ApproverIds);
            return (res.ApproverIds.Select(id => names.GetValueOrDefault(id, id.ToString())).ToList(), true);
        }
        catch { return (Array.Empty<string>(), false); }
    }
}
```
> `ApproverRule`/`ApproverStrategy`/`ApproverResolveContext` 见 `IApproverResolver.cs`；`ExpressionEvaluator.Evaluate(string?, string?)` 见 Phase A 内核（空条件=true）。

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~ForecastServiceTests"`
Expected: PASS（3 例）。

- [ ] **Step 6: Commit**

```bash
git add CP6.Core/Services/Oa/IForecastService.cs CP6.Core/Services/Oa/ForecastService.cs CP6.Tests/Oa/ForecastServiceTests.cs
git commit -m "feat(wfs-B): T4 ForecastService 预计流程前推(线性/占位/并行标注)"
```

---

## Task 5：`InboxService` — 未處理(待审核 + CC) + 已读幂等

**Files:**
- Create: `CP6.Core/Services/Oa/IInboxService.cs`
- Create: `CP6.Core/Services/Oa/InboxService.cs`
- Test: `CP6.Tests/Oa/InboxServiceTests.cs`

> `IInboxService` 在 T5~T8 **增量长出**（每 Task 加自己的方法到接口 + 类）。构造函数一次注入 `(db, engine, forecast)`——`engine` 供 T7 批量、`forecast` 供 T8 详情，T5 仅用 `db`。

- [ ] **Step 1: 写失败测试**

`CP6.Tests/Oa/InboxServiceTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class InboxServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static IForecastService Forecast(CP6Context db) => new ForecastService(db, new ApproverResolver(db));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db), Forecast(db));

    // 流程：n1(approver 审批，CC 给 ccUser) → end。返回 (instanceId, taskId)
    private static async Task SeedAndSubmitAsync(CP6Context db, Guid starter, Guid approver, Guid ccUser, string key = "leave")
    {
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = "starter", NickName = "发起人李", Password = "x" },
            new Sys_User { Id = approver, UserName = "approver", NickName = "审批王", Password = "x" },
            new Sys_User { Id = ccUser, UserName = "cc", NickName = "知会赵", Password = "x" });
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = key, FlowName = "请假单", FormKey = key,
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = {
                    new FlowNode { Id = "n1", Name = "主管审批", Type = "approval", ApproverStrategy = "Specified",
                                   ApproverUserId = approver, CcUsers = new() { ccUser } },
                    new FlowNode { Id = "end", Type = "end" } },
                Edges = { new FlowEdge { From = "n1", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync(key, starter, "{}");
    }

    [Fact]
    public async Task Pending_ReturnsMyTodos_WithStarterName_AndUnread()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);

        var pend = await Inbox(db).PendingAsync(approver);
        var item = Assert.Single(pend);
        Assert.Equal("请假单", item.FlowName);
        Assert.Equal("发起人李", item.StarterName);
        Assert.False(item.IsRead);
        Assert.Equal(approver, ((await db.Wf_FlowTasks.SingleAsync(t => t.Id == item.TaskId)).AssigneeId));
    }

    [Fact]
    public async Task PendingCc_ReturnsCcRecipientItems()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);

        var ccItems = await Inbox(db).PendingCcAsync(cc);
        var item = Assert.Single(ccItems);
        Assert.Equal("请假单", item.FlowName);
        Assert.Equal("n1", item.AtNodeId);
        Assert.False(item.IsRead);
    }

    [Fact]
    public async Task MarkTaskRead_Idempotent_AndOwnerOnly()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);
        var taskId = (await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending)).Id;

        await Inbox(db).MarkTaskReadAsync(approver, taskId);
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.Id == taskId);
        Assert.True(t1.IsRead);
        var firstReadAt = t1.ReadAt;

        await Inbox(db).MarkTaskReadAsync(approver, taskId);            // 幂等：不改 ReadAt
        Assert.Equal(firstReadAt, (await db.Wf_FlowTasks.SingleAsync(t => t.Id == taskId)).ReadAt);

        await Inbox(db).MarkTaskReadAsync(Guid.NewGuid(), taskId);      // 非本人：no-op
        Assert.True((await db.Wf_FlowTasks.SingleAsync(t => t.Id == taskId)).IsRead);
    }

    [Fact]
    public async Task MarkCcRead_SetsIsRead()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);
        var ccId = (await db.Wf_FlowCcs.SingleAsync(c => c.RecipientId == cc)).Id;

        await Inbox(db).MarkCcReadAsync(cc, ccId);
        Assert.True((await db.Wf_FlowCcs.SingleAsync(c => c.Id == ccId)).IsRead);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~InboxServiceTests"`
Expected: 编译失败（`IInboxService`/`InboxService` 未定义）。

- [ ] **Step 3: 建 `IInboxService.cs`（T5 方法）**

```csharp
namespace CP6.Core.Services.Oa;

/// <summary>电子表单信箱读模型服务（umbrella §4.3）。读 L1，写动作经 L0 引擎。T5~T8 增量。</summary>
public interface IInboxService
{
    // ── 未處理（T5）──
    Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId);     // 待審核：我的待办
    Task<IReadOnlyList<InboxCcItem>> PendingCcAsync(Guid userId);        // CC：抄送我
    Task MarkTaskReadAsync(Guid userId, Guid taskId);                    // 幂等、仅本人
    Task MarkCcReadAsync(Guid userId, Guid ccId);                        // 幂等、仅本人
}
```

- [ ] **Step 4: 建 `InboxService.cs`（T5 方法）**

```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class InboxService : IInboxService
{
    private readonly CP6Context _db;
    private readonly IFlowEngine _engine;       // T7 批量办理
    private readonly IForecastService _forecast; // T8 详情预计段
    public InboxService(CP6Context db, IFlowEngine engine, IForecastService forecast)
    {
        _db = db; _engine = engine; _forecast = forecast;
    }

    public async Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId)
    {
        var rows = await (from t in _db.Wf_FlowTasks
                          where t.AssigneeId == userId && t.Status == FlowTaskStatus.Pending
                          join i in _db.Wf_FlowInstances on t.InstanceId equals i.Id
                          where i.Status == FlowInstanceStatus.Running
                          join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                          from d in dd.DefaultIfEmpty()
                          join s in _db.Sys_Users on i.StarterId equals s.Id into ss
                          from s in ss.DefaultIfEmpty()
                          orderby t.CreateDate descending
                          select new { t, i, FlowName = d == null ? null : d.FlowName, Starter = s }).ToListAsync();
        return rows.Select(x => new InboxPendingItem(
            x.t.Id, x.i.Id, x.t.TokenId, x.i.FlowKey, x.FlowName,
            x.t.NodeId, null, x.i.StarterId,
            x.Starter == null ? "" : (string.IsNullOrWhiteSpace(x.Starter.NickName) ? x.Starter.UserName : x.Starter.NickName!),
            x.i.BizType, x.i.BizId, x.t.IsRead, x.t.CreateDate)).ToList();
    }

    public async Task<IReadOnlyList<InboxCcItem>> PendingCcAsync(Guid userId)
    {
        var rows = await (from c in _db.Wf_FlowCcs
                          where c.RecipientId == userId
                          join i in _db.Wf_FlowInstances on c.InstanceId equals i.Id
                          join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                          from d in dd.DefaultIfEmpty()
                          join s in _db.Sys_Users on i.StarterId equals s.Id into ss
                          from s in ss.DefaultIfEmpty()
                          orderby c.CreateDate descending
                          select new { c, i, FlowName = d == null ? null : d.FlowName, Starter = s }).ToListAsync();
        return rows.Select(x => new InboxCcItem(
            x.c.Id, x.i.Id, x.i.FlowKey, x.FlowName, x.c.AtNodeId, x.i.StarterId,
            x.Starter == null ? "" : (string.IsNullOrWhiteSpace(x.Starter.NickName) ? x.Starter.UserName : x.Starter.NickName!),
            x.c.IsRead, x.c.CreateDate)).ToList();
    }

    public async Task MarkTaskReadAsync(Guid userId, Guid taskId)
    {
        var t = await _db.Wf_FlowTasks.FirstOrDefaultAsync(x => x.Id == taskId && x.AssigneeId == userId);
        if (t is null || t.IsRead) return;   // 幂等：不存在/非本人/已读 → no-op
        t.IsRead = true; t.ReadAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task MarkCcReadAsync(Guid userId, Guid ccId)
    {
        var c = await _db.Wf_FlowCcs.FirstOrDefaultAsync(x => x.Id == ccId && x.RecipientId == userId);
        if (c is null || c.IsRead) return;
        c.IsRead = true; c.ReadAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~InboxServiceTests"`
Expected: PASS（4 例）。

- [ ] **Step 6: Commit**

```bash
git add CP6.Core/Services/Oa/IInboxService.cs CP6.Core/Services/Oa/InboxService.cs CP6.Tests/Oa/InboxServiceTests.cs
git commit -m "feat(wfs-B): T5 InboxService 未處理(待审核+CC)+已读幂等"
```

---

## Task 6：`InboxService` — 在途（RunningAsync）+ 已處理（DoneAsync）

**Files:**
- Modify: `CP6.Core/Services/Oa/IInboxService.cs`
- Modify: `CP6.Core/Services/Oa/InboxService.cs`
- Test: `CP6.Tests/Oa/InboxServiceTests.cs`（追加 3 例 + 复用 T5 的 `SeedAndSubmitAsync`）

> **在途** = 我发起、`Status=Running` 的实例，带「處理人」列（当前关卡待签 `Wf_FlowFormTo` 的应处理人名）。**已處理** = `tab` 三态：`mine`（我办结过的=`Wf_FlowFormTo.ActualHandlerId==我` 的已决行，按实例去重取最近办结）/ `cc`（抄送我）/ `all`（mine ∪ cc 去重）。月份过滤 `year/month` 可空（null=不限月，UI 选月时传具体值）。

- [ ] **Step 1: 写失败测试**（追加到 `InboxServiceTests`）

```csharp
    [Fact]
    public async Task Running_ReturnsMyStarted_WithCurrentHandlers()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);

        var running = await Inbox(db).RunningAsync(starter);
        var item = Assert.Single(running);
        Assert.Equal("请假单", item.FlowName);
        Assert.Equal(FlowInstanceStatus.Running, item.Status);
        Assert.Contains("审批王", item.CurrentHandlers);     // 当前关卡应处理人 = 待签履历 ExpectedHandler
    }

    [Fact]
    public async Task Done_Mine_ReturnsHandledByMe()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);
        var taskId = (await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending)).Id;
        await Engine(db).ActAsync(taskId, approver, approve: true, "OK");   // 办结 → 履历 Approved + 实例 Approved

        var done = await Inbox(db).DoneAsync(approver, null, null, "mine");
        var item = Assert.Single(done);
        Assert.Equal(FlowFormToStatus.Approved, item.FormToStatus);
        Assert.Equal(FlowInstanceStatus.Approved, item.InstanceStatus);
        Assert.Equal("发起人李", item.StarterName);
    }

    [Fact]
    public async Task Done_Cc_ReturnsCcRecipientItems()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);

        var done = await Inbox(db).DoneAsync(cc, null, null, "cc");
        var item = Assert.Single(done);
        Assert.Equal("请假单", item.FlowName);
    }
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~InboxServiceTests"`
Expected: 编译失败（`RunningAsync`/`DoneAsync` 未定义）。

- [ ] **Step 3: `IInboxService` 加方法（在 T5 方法后追加「在途 / 已處理」段）**

```csharp
    // ── 在途（T6）──
    Task<IReadOnlyList<InboxRunningItem>> RunningAsync(Guid userId);
    // ── 已處理（T6）──：tab = mine | cc | all；year/month 可空（null=不限月）
    Task<IReadOnlyList<InboxDoneItem>> DoneAsync(Guid userId, int? year, int? month, string tab = "mine");
```

- [ ] **Step 4: `InboxService` 实现 + 私有姓名助手**

类顶部 `using` 补 `using CP6.Entity.DomainModels.Sys;`。在类内追加：

```csharp
    private static string Name(Sys_User? u) =>
        u == null ? "" : (string.IsNullOrWhiteSpace(u.NickName) ? u.UserName : u.NickName!);

    public async Task<IReadOnlyList<InboxRunningItem>> RunningAsync(Guid userId)
    {
        var rows = await (from i in _db.Wf_FlowInstances
                          where i.StarterId == userId && i.Status == FlowInstanceStatus.Running
                          join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                          from d in dd.DefaultIfEmpty()
                          orderby i.CreateDate descending
                          select new { i, FlowName = d == null ? null : d.FlowName }).ToListAsync();
        var instIds = rows.Select(x => x.i.Id).ToList();
        var pendings = await _db.Wf_FlowFormTos
            .Where(f => instIds.Contains(f.InstanceId) && f.Status == FlowFormToStatus.Pending)
            .Select(f => new { f.InstanceId, f.ExpectedHandlerId }).ToListAsync();
        var names = await OaUserNames.ResolveAsync(_db, pendings.Select(p => p.ExpectedHandlerId));
        return rows.Select(x => new InboxRunningItem(
            x.i.Id, x.i.FlowKey, x.FlowName, x.i.CurrentNode, x.i.Status,
            pendings.Where(p => p.InstanceId == x.i.Id)
                    .Select(p => names.GetValueOrDefault(p.ExpectedHandlerId, p.ExpectedHandlerId.ToString()))
                    .Distinct().ToList(),
            x.i.CreateDate)).ToList();
    }

    public async Task<IReadOnlyList<InboxDoneItem>> DoneAsync(Guid userId, int? year, int? month, string tab = "mine")
    {
        bool InMonth(DateTime dt) => (year is null || dt.Year == year) && (month is null || dt.Month == month);

        var mine = new List<InboxDoneItem>();
        if (tab is "mine" or "all")
        {
            var handled = await (from f in _db.Wf_FlowFormTos
                                 where f.ActualHandlerId == userId && f.HandledAt != null
                                       && (f.Status == FlowFormToStatus.Approved
                                           || f.Status == FlowFormToStatus.Rejected
                                           || f.Status == FlowFormToStatus.Transferred)
                                 join i in _db.Wf_FlowInstances on f.InstanceId equals i.Id
                                 join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                                 from d in dd.DefaultIfEmpty()
                                 join s in _db.Sys_Users on i.StarterId equals s.Id into ss
                                 from s in ss.DefaultIfEmpty()
                                 select new { f, i, FlowName = d == null ? null : d.FlowName, Starter = s }).ToListAsync();
            mine = handled.Where(x => InMonth(x.f.HandledAt!.Value))
                .GroupBy(x => x.i.Id)
                .Select(g => g.OrderByDescending(x => x.f.HandledAt).First())
                .Select(x => new InboxDoneItem(x.i.Id, x.i.FlowKey, x.FlowName, x.i.StarterId, Name(x.Starter),
                    x.f.Status, x.f.HandledAt!.Value, x.i.Status))
                .OrderByDescending(x => x.DoneAt).ToList();
        }

        var cc = new List<InboxDoneItem>();
        if (tab is "cc" or "all")
        {
            var ccRows = await (from c in _db.Wf_FlowCcs
                                where c.RecipientId == userId
                                join i in _db.Wf_FlowInstances on c.InstanceId equals i.Id
                                join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                                from d in dd.DefaultIfEmpty()
                                join s in _db.Sys_Users on i.StarterId equals s.Id into ss
                                from s in ss.DefaultIfEmpty()
                                select new { c, i, FlowName = d == null ? null : d.FlowName, Starter = s }).ToListAsync();
            cc = ccRows.Where(x => InMonth(x.c.CreateDate))
                .Select(x => new InboxDoneItem(x.i.Id, x.i.FlowKey, x.FlowName, x.i.StarterId, Name(x.Starter),
                    x.i.Status, x.c.CreateDate, x.i.Status))
                .OrderByDescending(x => x.DoneAt).ToList();
        }

        if (tab == "mine") return mine;
        if (tab == "cc") return cc;
        return mine.Concat(cc).GroupBy(x => x.InstanceId)
            .Select(g => g.OrderByDescending(x => x.DoneAt).First())
            .OrderByDescending(x => x.DoneAt).ToList();
    }
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~InboxServiceTests"`
Expected: PASS（7 例：T5 四 + T6 三）。

- [ ] **Step 6: Commit**

```bash
git add CP6.Core/Services/Oa/IInboxService.cs CP6.Core/Services/Oa/InboxService.cs CP6.Tests/Oa/InboxServiceTests.cs
git commit -m "feat(wfs-B): T6 InboxService 在途(当前处理人)+已處理(mine/cc/all+月份)"
```

---

## Task 7：`InboxService` — 批量办理（ActBatchAsync，逐条回报）

**Files:**
- Modify: `CP6.Core/Services/Oa/IInboxService.cs`
- Modify: `CP6.Core/Services/Oa/InboxService.cs`
- Test: `CP6.Tests/Oa/InboxServiceTests.cs`（追加 1 例）

> 批量批准/退回：逐条 `engine.ActAsync`，**允许部分失败**——无效/已办/非本人任务回 `E-WF-004`，不整体回滚（每条 `ActAsync` 自带一次 `SaveChanges` 已落库）。Phase B 不含 act-as，批量仅办本人任务。

- [ ] **Step 1: 写失败测试**

```csharp
    [Fact]
    public async Task ActBatch_PartialFailure_ReportsPerItem()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);
        var validTaskId = (await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending)).Id;
        var bogus = Guid.NewGuid();

        var res = await Inbox(db).ActBatchAsync(approver, new[] { validTaskId, bogus }, approve: true, "批量同意");

        Assert.Equal(2, res.Count);
        Assert.True(res.Single(r => r.TaskId == validTaskId).Ok);
        var bad = res.Single(r => r.TaskId == bogus);
        Assert.False(bad.Ok);
        Assert.Equal("E-WF-004", bad.Error);
        Assert.Equal(FlowTaskStatus.Approved, (await db.Wf_FlowTasks.SingleAsync(t => t.Id == validTaskId)).Status);
    }
```

- [ ] **Step 2: 跑测试确认失败** — `ActBatchAsync` 未定义，编译失败。

- [ ] **Step 3: `IInboxService` 加方法**

```csharp
    // ── 批量办理（T7）──
    Task<IReadOnlyList<BatchActResultItem>> ActBatchAsync(Guid userId, IReadOnlyList<Guid> taskIds, bool approve, string? comment = null);
```

- [ ] **Step 4: `InboxService` 实现**

```csharp
    public async Task<IReadOnlyList<BatchActResultItem>> ActBatchAsync(
        Guid userId, IReadOnlyList<Guid> taskIds, bool approve, string? comment = null)
    {
        var results = new List<BatchActResultItem>();
        foreach (var taskId in taskIds.Distinct())
        {
            var t = await _db.Wf_FlowTasks.FirstOrDefaultAsync(x => x.Id == taskId);
            if (t is null || t.AssigneeId != userId || t.Status != FlowTaskStatus.Pending)
            {
                results.Add(new BatchActResultItem(taskId, false, "E-WF-004"));   // 无效/已办/非本人
                continue;
            }
            try
            {
                await _engine.ActAsync(taskId, userId, approve, comment);
                results.Add(new BatchActResultItem(taskId, true, null));
            }
            catch (InvalidOperationException e)
            {
                results.Add(new BatchActResultItem(taskId, false, e.Message));
            }
        }
        return results;
    }
```

- [ ] **Step 5: 跑测试确认通过** — `dotnet test CP6.Tests --filter "FullyQualifiedName~InboxServiceTests"`（8 例）。

- [ ] **Step 6: Commit**

```bash
git add CP6.Core/Services/Oa/IInboxService.cs CP6.Core/Services/Oa/InboxService.cs CP6.Tests/Oa/InboxServiceTests.cs
git commit -m "feat(wfs-B): T7 InboxService 批量办理(逐条回报 E-WF-004 部分失败)"
```

---

## Task 8：`InboxService` — 详情（DetailAsync 左读右签）+ 仪表盘（StatsAsync）

**Files:**
- Modify: `CP6.Core/Services/Oa/IInboxService.cs`
- Modify: `CP6.Core/Services/Oa/InboxService.cs`
- Test: `CP6.Tests/Oa/InboxServiceTests.cs`（追加 2 例）

> **详情** = 表单字段 schema（`Wf_FormDef.SchemaJson` by FormKey）+ 当前数据（`inst.VarsJson`）+ 时间线（`Wf_FlowFormTo` 持久行，解析应/实/代签人名）+ 快照（`Wf_FlowData`）+ **预计段（`_forecast.ForecastAsync(flowKey, varsJson, starterId, fromNodeId: inst.CurrentNode)`，仅 Running 时算）** + 抄送行。**仪表盘** = 待我处理/我发起在途/本月办结/被退回 四计数 + 趋势 + 最近待办。

- [ ] **Step 1: 写失败测试**

```csharp
    [Fact]
    public async Task Detail_BuildsTimeline_AndForecast()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);
        var instId = (await db.Wf_FlowInstances.SingleAsync()).Id;

        var detail = await Inbox(db).DetailAsync(instId);
        Assert.NotNull(detail);
        Assert.Equal("请假单", detail!.FlowName);
        var cur = Assert.Single(detail.Timeline.Where(r => r.Status == FlowFormToStatus.Pending));
        Assert.Equal("n1", cur.NodeId);
        Assert.Equal("审批王", cur.ExpectedHandlerName);          // 应处理人名解析
        Assert.NotEmpty(detail.Forecast);                         // 预计段含 end（Running 才算）
        Assert.Contains(detail.Cc, c => c.RecipientName == "知会赵");
    }

    [Fact]
    public async Task Stats_CountsPendingAndRunning()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);

        Assert.Equal(1, (await Inbox(db).StatsAsync(approver)).PendingCount);   // 审批人有 1 待办
        Assert.Equal(1, (await Inbox(db).StatsAsync(starter)).RunningCount);    // 发起人有 1 在途
    }
```

- [ ] **Step 2: 跑测试确认失败** — `DetailAsync`/`StatsAsync` 未定义。

- [ ] **Step 3: `IInboxService` 加方法**

```csharp
    // ── 详情 + 仪表盘（T8）──
    Task<InboxDetail?> DetailAsync(Guid instanceId);   // 不存在 → null（控制器转 404）
    Task<InboxStats> StatsAsync(Guid userId);
```

- [ ] **Step 4: `InboxService` 实现**

```csharp
    public async Task<InboxDetail?> DetailAsync(Guid instanceId)
    {
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId);
        if (inst is null) return null;
        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(d => d.FlowKey == inst.FlowKey);
        var formSchema = def == null ? null
            : (await _db.Wf_FormDefs.FirstOrDefaultAsync(fd => fd.FormKey == def.FormKey))?.SchemaJson;

        var formTos = await _db.Wf_FlowFormTos.Where(f => f.InstanceId == instanceId)
            .OrderBy(f => f.StepSeq).ThenBy(f => f.SentAt).ToListAsync();
        var snaps = await _db.Wf_FlowDatas.Where(s => s.InstanceId == instanceId)
            .OrderBy(s => s.StepSeq).ToListAsync();
        var ccs = await _db.Wf_FlowCcs.Where(c => c.InstanceId == instanceId).ToListAsync();

        var ids = formTos.SelectMany(f => new[] { f.ExpectedHandlerId, f.ActualHandlerId ?? Guid.Empty, f.OnBehalfOfId ?? Guid.Empty })
            .Concat(ccs.Select(c => c.RecipientId));
        var names = await OaUserNames.ResolveAsync(_db, ids);
        string? N(Guid? id) => id is null || id == Guid.Empty ? null : names.GetValueOrDefault(id.Value, id.Value.ToString());

        var timeline = formTos.Select(f => new TimelineRow(
            f.StepSeq, f.TokenId, f.NodeId, f.NodeName,
            f.ExpectedHandlerId, names.GetValueOrDefault(f.ExpectedHandlerId, f.ExpectedHandlerId.ToString()),
            f.ActualHandlerId, N(f.ActualHandlerId), f.OnBehalfOfId, N(f.OnBehalfOfId),
            f.Status, f.Comment, f.SentAt, f.HandledAt)).ToList();
        var snapshots = snaps.Select(s => new SnapshotRow(s.StepSeq, s.NodeId, s.DataJson)).ToList();
        var ccRows = ccs.Select(c => new CcRow(c.RecipientId, N(c.RecipientId) ?? "", c.AtNodeId, c.IsRead)).ToList();

        IReadOnlyList<ForecastStep> forecast = inst.Status == FlowInstanceStatus.Running
            ? (await _forecast.ForecastAsync(inst.FlowKey, inst.VarsJson, inst.StarterId, fromNodeId: inst.CurrentNode)).Steps
            : Array.Empty<ForecastStep>();

        return new InboxDetail(inst, def?.FlowName, def?.FormKey, formSchema,
            inst.VarsJson, timeline, snapshots, forecast, ccRows);
    }

    public async Task<InboxStats> StatsAsync(Guid userId)
    {
        var pending = await PendingAsync(userId);
        var running = await RunningAsync(userId);
        var doneMine = await DoneAsync(userId, DateTime.Now.Year, DateTime.Now.Month, "mine");
        var rejectedBack = await _db.Wf_FlowInstances
            .CountAsync(i => i.StarterId == userId && i.Status == FlowInstanceStatus.Rejected);

        // 趋势：近 7 天我办结量（按 HandledAt 天分桶）
        var since = DateTime.Now.Date.AddDays(-6);
        var handledRows = await _db.Wf_FlowFormTos
            .Where(f => f.ActualHandlerId == userId && f.HandledAt != null && f.HandledAt >= since)
            .Select(f => f.HandledAt!.Value).ToListAsync();
        var trend = Enumerable.Range(0, 7).Select(d =>
        {
            var day = since.AddDays(d);
            return new TrendPoint(day.ToString("MM-dd"), handledRows.Count(h => h.Date == day));
        }).ToList();

        return new InboxStats(pending.Count, running.Count, doneMine.Count, rejectedBack,
            trend, pending.Take(5).ToList());
    }
```
> `DetailAsync` 不做"读权限"硬校验（信箱内能看到的实例才会点进来；act-as/越权读留 Phase C）。`StatsAsync` 趋势用 `DateTime.Now` 分桶——单测仅断言计数（`PendingCount`/`RunningCount`），不锁 trend 具体值，避免时钟脆弱。

- [ ] **Step 5: 跑测试确认通过** — `dotnet test CP6.Tests --filter "FullyQualifiedName~InboxServiceTests"`（10 例）。

- [ ] **Step 6: 兼容回归** — `dotnet test CP6.Tests --filter "FullyQualifiedName~Wf"`（Oa 服务零改 Wf 行为，照绿）。

- [ ] **Step 7: Commit**

```bash
git add CP6.Core/Services/Oa/IInboxService.cs CP6.Core/Services/Oa/InboxService.cs CP6.Tests/Oa/InboxServiceTests.cs
git commit -m "feat(wfs-B): T8 InboxService 详情(时间线+快照+预计+CC)+仪表盘(四计数+趋势)"
```

---

## Task 9：`DraftService` — 草稿增改提删列

**Files:**
- Create: `CP6.Core/Services/Oa/IDraftService.cs`
- Create: `CP6.Core/Services/Oa/DraftService.cs`
- Test: `CP6.Tests/Oa/DraftServiceTests.cs`（追加到 T2 已建文件）

> 草稿 = `Wf_FlowInstance.Status=Draft`（有实例无 token，R2）。`SubmitDraftAsync` 委托引擎 `StartDraftAsync`（T2，就地进流程）。越权/非草稿 → `E-WF-003`。

- [ ] **Step 1: 写失败测试**（追加到 `DraftServiceTests`，复用其 `SeedFlowAsync`）

```csharp
    private static IDraftService Draft(CP6Context db) => new DraftService(db, Engine(db));

    [Fact]
    public async Task Save_List_Update_Delete_Roundtrip()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid();
        await SeedFlowAsync(db, Guid.NewGuid());

        var id = await Draft(db).SaveDraftAsync(starter, "leave", """{"days":1}""");
        var list = await Draft(db).ListDraftsAsync(starter);
        Assert.Single(list);
        Assert.Equal(FlowInstanceStatus.Draft, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == id)).Status);

        await Draft(db).UpdateDraftAsync(starter, id, """{"days":3}""");
        Assert.Equal("""{"days":3}""", (await db.Wf_FlowInstances.SingleAsync(i => i.Id == id)).VarsJson);

        await Draft(db).DeleteDraftAsync(starter, id);
        Assert.Empty(await Draft(db).ListDraftsAsync(starter));
    }

    [Fact]
    public async Task SubmitDraft_EntersFlow()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedFlowAsync(db, approver);
        var id = await Draft(db).SaveDraftAsync(starter, "leave", "{}");

        await Draft(db).SubmitDraftAsync(starter, id);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == id);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == approver && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task Update_NotOwner_Throws()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid();
        await SeedFlowAsync(db, Guid.NewGuid());
        var id = await Draft(db).SaveDraftAsync(starter, "leave", "{}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Draft(db).UpdateDraftAsync(Guid.NewGuid(), id, "{}"));
        Assert.Equal("E-WF-003", ex.Message);
    }
```

- [ ] **Step 2: 跑测试确认失败** — `IDraftService`/`DraftService` 未定义。

- [ ] **Step 3: 建 `IDraftService.cs`**

```csharp
namespace CP6.Core.Services.Oa;

/// <summary>草稿（暫存）服务（umbrella §4.3 / R2）。草稿 = Wf_FlowInstance.Status=Draft。</summary>
public interface IDraftService
{
    Task<Guid> SaveDraftAsync(Guid starterId, string flowKey, string varsJson);   // 新建草稿，返回实例 Id
    Task UpdateDraftAsync(Guid starterId, Guid instanceId, string varsJson);       // 改草稿字段
    Task<IReadOnlyList<InboxRunningItem>> ListDraftsAsync(Guid starterId);         // 我的草稿（复用 Running DTO 形状）
    Task DeleteDraftAsync(Guid starterId, Guid instanceId);                        // 删草稿
    Task SubmitDraftAsync(Guid starterId, Guid instanceId);                        // 提交 → 引擎 StartDraftAsync
}
```

- [ ] **Step 4: 建 `DraftService.cs`**

```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class DraftService : IDraftService
{
    private readonly CP6Context _db;
    private readonly IFlowEngine _engine;
    public DraftService(CP6Context db, IFlowEngine engine) { _db = db; _engine = engine; }

    public async Task<Guid> SaveDraftAsync(Guid starterId, string flowKey, string varsJson)
    {
        var inst = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, StarterId = starterId,
            Status = FlowInstanceStatus.Draft, CurrentNode = "", VarsJson = varsJson ?? "{}",
            Creator = starterId.ToString(),
        };
        _db.Wf_FlowInstances.Add(inst);
        await _db.SaveChangesAsync();
        return inst.Id;
    }

    public async Task UpdateDraftAsync(Guid starterId, Guid instanceId, string varsJson)
    {
        var inst = await LoadOwnedDraftAsync(starterId, instanceId);
        inst.VarsJson = varsJson ?? "{}";
        inst.Modifier = starterId.ToString();
        inst.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<InboxRunningItem>> ListDraftsAsync(Guid starterId)
    {
        var rows = await (from i in _db.Wf_FlowInstances
                          where i.StarterId == starterId && i.Status == FlowInstanceStatus.Draft
                          join d in _db.Wf_FlowDefs on i.FlowKey equals d.FlowKey into dd
                          from d in dd.DefaultIfEmpty()
                          orderby i.CreateDate descending
                          select new { i, FlowName = d == null ? null : d.FlowName }).ToListAsync();
        return rows.Select(x => new InboxRunningItem(
            x.i.Id, x.i.FlowKey, x.FlowName, x.i.CurrentNode, x.i.Status,
            Array.Empty<string>(), x.i.CreateDate)).ToList();
    }

    public async Task DeleteDraftAsync(Guid starterId, Guid instanceId)
    {
        var inst = await LoadOwnedDraftAsync(starterId, instanceId);
        _db.Wf_FlowInstances.Remove(inst);
        await _db.SaveChangesAsync();
    }

    public Task SubmitDraftAsync(Guid starterId, Guid instanceId)
        => _engine.StartDraftAsync(instanceId, starterId);   // 引擎内已校验 owner/Draft 态 → E-WF-003

    private async Task<Wf_FlowInstance> LoadOwnedDraftAsync(Guid starterId, Guid instanceId)
    {
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId)
                   ?? throw new InvalidOperationException("E-WF-003");
        if (inst.StarterId != starterId || inst.Status != FlowInstanceStatus.Draft)
            throw new InvalidOperationException("E-WF-003");
        return inst;
    }
}
```

- [ ] **Step 5: 跑测试确认通过** — `dotnet test CP6.Tests --filter "FullyQualifiedName~DraftServiceTests"`（含 T2 两例，共 5 例）。

- [ ] **Step 6: Commit**

```bash
git add CP6.Core/Services/Oa/IDraftService.cs CP6.Core/Services/Oa/DraftService.cs CP6.Tests/Oa/DraftServiceTests.cs
git commit -m "feat(wfs-B): T9 DraftService 草稿增改提删列(提交委托 StartDraftAsync)"
```

---

## Task 10：`FlowAdminService` — 轻量流程管理（列表 / 启用停用 / 1:1 校验）

**Files:**
- Create: `CP6.Core/Services/Oa/IFlowAdminService.cs`
- Create: `CP6.Core/Services/Oa/FlowAdminService.cs`
- Test: `CP6.Tests/Oa/FlowAdminServiceTests.cs`

> 填單能挂流程的前提（umbrella §4.8 / W6/W7）：每表单流程的 列表 / 启停 / **1 表单 ↔ 1 启用流程**守卫。**仅用现有 `Wf_FlowDef` 字段**（FunctionId/FlowCode 等编辑器核心键留 C′ 设计器，不在 Phase B）。启用第二条同 `FormKey` 流程 → `E-WF-008`；流程不存在 → `E-WF-006`。

- [ ] **Step 1: 写失败测试**

`CP6.Tests/Oa/FlowAdminServiceTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class FlowAdminServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IFlowAdminService Admin(CP6Context db) => new FlowAdminService(db);

    private static Wf_FlowDef Def(string flowKey, string formKey, bool enable) => new()
    { Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = formKey, Version = 1, Enable = enable };

    [Fact]
    public async Task List_ReturnsAllDefs()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.AddRange(Def("a", "fa", true), Def("b", "fb", false));
        await db.SaveChangesAsync();
        Assert.Equal(2, (await Admin(db).ListFlowsAsync()).Count);
    }

    [Fact]
    public async Task Enable_SecondFlowSameForm_ThrowsE008()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.AddRange(Def("a", "leave", true), Def("b", "leave", false));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Admin(db).SetEnabledAsync("b", true));
        Assert.Equal("E-WF-008", ex.Message);   // leave 已有启用流程 a
    }

    [Fact]
    public async Task Disable_ThenEnableOther_Ok()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.AddRange(Def("a", "leave", true), Def("b", "leave", false));
        await db.SaveChangesAsync();

        await Admin(db).SetEnabledAsync("a", false);   // 先停 a
        await Admin(db).SetEnabledAsync("b", true);    // 再启 b → 不冲突
        Assert.True((await db.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "b")).Enable);
    }

    [Fact]
    public async Task Enable_NotExist_ThrowsE006()
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Admin(db).SetEnabledAsync("zzz", true));
        Assert.Equal("E-WF-006", ex.Message);
    }
}
```

- [ ] **Step 2: 跑测试确认失败** — `IFlowAdminService`/`FlowAdminService` 未定义。

- [ ] **Step 3: 建 `IFlowAdminService.cs`（含 DTO）**

```csharp
namespace CP6.Core.Services.Oa;

/// <summary>轻量流程管理项（每表单专属流程 1:1，umbrella §4.8）。</summary>
public record FlowAdminItem(string FlowKey, string FlowName, string FormKey, int Version, bool Enable);

/// <summary>轻量流程管理（填單挂流程前提）。流程列表 / 启停 / 1 表单↔1 启用流程守卫。</summary>
public interface IFlowAdminService
{
    Task<IReadOnlyList<FlowAdminItem>> ListFlowsAsync();
    Task<FlowAdminItem?> GetFlowAsync(string flowKey);
    Task SetEnabledAsync(string flowKey, bool enabled);   // 启用时守 E-WF-008；流程不存在 E-WF-006
}
```

- [ ] **Step 4: 建 `FlowAdminService.cs`**

```csharp
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public class FlowAdminService : IFlowAdminService
{
    private readonly CP6Context _db;
    public FlowAdminService(CP6Context db) { _db = db; }

    public async Task<IReadOnlyList<FlowAdminItem>> ListFlowsAsync() =>
        await _db.Wf_FlowDefs.OrderBy(d => d.FormKey).ThenBy(d => d.FlowKey)
            .Select(d => new FlowAdminItem(d.FlowKey, d.FlowName, d.FormKey, d.Version, d.Enable))
            .ToListAsync();

    public async Task<FlowAdminItem?> GetFlowAsync(string flowKey) =>
        await _db.Wf_FlowDefs.Where(d => d.FlowKey == flowKey)
            .Select(d => new FlowAdminItem(d.FlowKey, d.FlowName, d.FormKey, d.Version, d.Enable))
            .FirstOrDefaultAsync();

    public async Task SetEnabledAsync(string flowKey, bool enabled)
    {
        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(d => d.FlowKey == flowKey)
                  ?? throw new InvalidOperationException("E-WF-006");
        if (enabled && !def.Enable)
        {
            var conflict = await _db.Wf_FlowDefs.AnyAsync(d =>
                d.FormKey == def.FormKey && d.FlowKey != def.FlowKey && d.Enable);
            if (conflict) throw new InvalidOperationException("E-WF-008");   // 1 表单 ↔ 1 启用流程
        }
        def.Enable = enabled;
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: 跑测试确认通过** — `dotnet test CP6.Tests --filter "FullyQualifiedName~FlowAdminServiceTests"`（4 例）。

- [ ] **Step 6: Commit**

```bash
git add CP6.Core/Services/Oa/IFlowAdminService.cs CP6.Core/Services/Oa/FlowAdminService.cs CP6.Tests/Oa/FlowAdminServiceTests.cs
git commit -m "feat(wfs-B): T10 FlowAdminService 流程列表/启停+1表单1流程守卫(E-WF-006/008)"
```

---

## Task 11：控制器（Inbox/Draft/Forecast/FlowAdmin）+ DI 注册

**Files:**
- Create: `CP6.WebApi/Controllers/Oa/InboxController.cs`
- Create: `CP6.WebApi/Controllers/Oa/DraftController.cs`
- Create: `CP6.WebApi/Controllers/Oa/ForecastController.cs`
- Create: `CP6.WebApi/Controllers/Oa/FlowAdminController.cs`
- Modify: `CP6.WebApi/Program.cs`（DI）

> 控制器模式照 `Controllers/Wf/FlowController.cs`：基类 `LocalizedControllerBase`、`[ApiController][Route("api/oa/...")][Authorize]`、用户 Id 经 `ICurrentPermissionContext.GetAsync().UserId`、`catch (InvalidOperationException e) => Err(e)`（`BadRequest(new{code=400,message=e.Message})`）、成功 `Ok2(...)`。**控制器无单测**（服务层已全测）；本 Task 验收 = 编译通过 + 全量回归绿 + Program.cs 启动装配（T19 真起验证）。

- [ ] **Step 1: 建 `InboxController.cs`**

```csharp
using CP6.Core.Services.Oa;
using CP6.WebApi.Auth;                 // ICurrentPermissionContext（与 FlowController 同源，落码核对 using）
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController]
[Route("api/oa/inbox")]
[Authorize]
public class InboxController : LocalizedControllerBase
{
    private readonly IInboxService _inbox;
    private readonly ICurrentPermissionContext _ctx;
    public InboxController(IInboxService inbox, ICurrentPermissionContext ctx) { _inbox = inbox; _ctx = ctx; }

    private async Task<Guid> MeAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("pending")]   public async Task<IActionResult> Pending()   => Ok2(await _inbox.PendingAsync(await MeAsync()));
    [HttpGet("pending-cc")]public async Task<IActionResult> PendingCc() => Ok2(await _inbox.PendingCcAsync(await MeAsync()));
    [HttpGet("running")]   public async Task<IActionResult> Running()   => Ok2(await _inbox.RunningAsync(await MeAsync()));

    [HttpGet("done")]
    public async Task<IActionResult> Done([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string tab = "mine")
        => Ok2(await _inbox.DoneAsync(await MeAsync(), year, month, tab));

    [HttpGet("stats")]     public async Task<IActionResult> Stats()     => Ok2(await _inbox.StatsAsync(await MeAsync()));

    [HttpGet("detail/{instanceId:guid}")]
    public async Task<IActionResult> Detail(Guid instanceId)
    {
        var d = await _inbox.DetailAsync(instanceId);
        return d is null ? NotFound(new { code = 404, message = "E-WF-007" }) : Ok2(d);
    }

    public record MarkReadReq(Guid Id);
    [HttpPost("task/read")] public async Task<IActionResult> MarkTaskRead([FromBody] MarkReadReq r)
    { await _inbox.MarkTaskReadAsync(await MeAsync(), r.Id); return Ok2(true); }
    [HttpPost("cc/read")]   public async Task<IActionResult> MarkCcRead([FromBody] MarkReadReq r)
    { await _inbox.MarkCcReadAsync(await MeAsync(), r.Id); return Ok2(true); }

    public record BatchReq(List<Guid> TaskIds, bool Approve, string? Comment);
    [HttpPost("batch")]
    public async Task<IActionResult> Batch([FromBody] BatchReq r)
    {
        try { return Ok2(await _inbox.ActBatchAsync(await MeAsync(), r.TaskIds, r.Approve, r.Comment)); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
```
> `E-WF-007` = 实例不存在（404）——在 §4.6 错误码表补一行（落码时同步 i18n T18）。

- [ ] **Step 2: 建 `DraftController.cs`**

```csharp
[ApiController]
[Route("api/oa/draft")]
[Authorize]
public class DraftController : LocalizedControllerBase
{
    private readonly IDraftService _draft;
    private readonly ICurrentPermissionContext _ctx;
    public DraftController(IDraftService draft, ICurrentPermissionContext ctx) { _draft = draft; _ctx = ctx; }
    private async Task<Guid> MeAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("list")] public async Task<IActionResult> List() => Ok2(await _draft.ListDraftsAsync(await MeAsync()));

    public record SaveReq(string FlowKey, string VarsJson);
    [HttpPost("save")] public async Task<IActionResult> Save([FromBody] SaveReq r)
    { try { return Ok2(new { id = await _draft.SaveDraftAsync(await MeAsync(), r.FlowKey, r.VarsJson) }); }
      catch (InvalidOperationException e) { return Err(e); } }

    public record UpdateReq(Guid Id, string VarsJson);
    [HttpPost("update")] public async Task<IActionResult> Update([FromBody] UpdateReq r)
    { try { await _draft.UpdateDraftAsync(await MeAsync(), r.Id, r.VarsJson); return Ok2(true); }
      catch (InvalidOperationException e) { return Err(e); } }

    public record IdReq(Guid Id);
    [HttpPost("submit")] public async Task<IActionResult> Submit([FromBody] IdReq r)
    { try { await _draft.SubmitDraftAsync(await MeAsync(), r.Id); return Ok2(true); }
      catch (InvalidOperationException e) { return Err(e); } }
    [HttpPost("delete")] public async Task<IActionResult> Delete([FromBody] IdReq r)
    { try { await _draft.DeleteDraftAsync(await MeAsync(), r.Id); return Ok2(true); }
      catch (InvalidOperationException e) { return Err(e); } }
}
```

- [ ] **Step 3: 建 `ForecastController.cs`**（发起预览 = 起点前推）

```csharp
[ApiController]
[Route("api/oa/forecast")]
[Authorize]
public class ForecastController : LocalizedControllerBase
{
    private readonly IForecastService _forecast;
    private readonly ICurrentPermissionContext _ctx;
    public ForecastController(IForecastService forecast, ICurrentPermissionContext ctx) { _forecast = forecast; _ctx = ctx; }
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    public record PreviewReq(string FlowKey, string? VarsJson);
    [HttpPost("preview")]   // 发起预览（fromNodeId=null）
    public async Task<IActionResult> Preview([FromBody] PreviewReq r)
    {
        try
        {
            var me = (await _ctx.GetAsync()).UserId;
            return Ok2(await _forecast.ForecastAsync(r.FlowKey, r.VarsJson ?? "{}", me, fromNodeId: null));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
```

- [ ] **Step 4: 建 `FlowAdminController.cs`**

```csharp
[ApiController]
[Route("api/oa/flow-admin")]
[Authorize]
public class FlowAdminController : LocalizedControllerBase
{
    private readonly IFlowAdminService _admin;
    public FlowAdminController(IFlowAdminService admin) { _admin = admin; }
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("list")] public async Task<IActionResult> List() => Ok2(await _admin.ListFlowsAsync());
    [HttpGet("{flowKey}")] public async Task<IActionResult> Get(string flowKey)
    { var f = await _admin.GetFlowAsync(flowKey); return f is null ? NotFound(new { code = 404, message = "E-WF-006" }) : Ok2(f); }

    public record EnableReq(string FlowKey, bool Enabled);
    [HttpPost("enable")] public async Task<IActionResult> Enable([FromBody] EnableReq r)
    { try { await _admin.SetEnabledAsync(r.FlowKey, r.Enabled); return Ok2(true); }
      catch (InvalidOperationException e) { return Err(e); } }
}
```
> **落码核对**：①`LocalizedControllerBase`/`Ok2`/`ICurrentPermissionContext` 的实际 `using` 命名空间照 `FlowController.cs` 抄（Explore 已确认基类与 ctx 用法）；②若 `Ok2` 签名要求特定包装，对齐 FlowController 现有调用。**这 4 个控制器若访问需 OA 菜单权限，T18 seed 菜单 733-737 + RoleMenu 授 admin**；P1 暂 `[Authorize]` 仅登录态即可（与现有 wf 控制器同档）。

- [ ] **Step 5: Program.cs 注册 4 个 Oa 服务**

在 `// 4.0b OA(Wf) 阶段2 集成` 段之后追加：
```csharp
// 4.0d OA 电子表单信箱（Phase B，消费 Wf 引擎）
builder.Services.AddScoped<CP6.Core.Services.Oa.IForecastService, CP6.Core.Services.Oa.ForecastService>();
builder.Services.AddScoped<CP6.Core.Services.Oa.IInboxService, CP6.Core.Services.Oa.InboxService>();
builder.Services.AddScoped<CP6.Core.Services.Oa.IDraftService, CP6.Core.Services.Oa.DraftService>();
builder.Services.AddScoped<CP6.Core.Services.Oa.IFlowAdminService, CP6.Core.Services.Oa.FlowAdminService>();
```

- [ ] **Step 6: 编译 + 全量回归**

Run: `dotnet build CP6.WebApi` 然后 `dotnet test CP6.Tests`
Expected: 编译通过；后端全绿（Phase A 末 1212 + 本计划新增 T1~T10 测试，无回归）。

- [ ] **Step 7: Commit（Part A 收尾里程碑）**

```bash
git add CP6.WebApi/Controllers/Oa/ CP6.WebApi/Program.cs
git commit -m "feat(wfs-B): T11 Oa 控制器(inbox/draft/forecast/flow-admin)+DI注册(Part A 收尾)"
```

> **Part A 完成**：信箱后端 REST 全可用，可被 Postman/集成测试驱动。Part B 起接前端。

---

# Part B — 前端信箱 + 流程管理 + 重定向 + i18n + QA

> **通用前端约定**：axios 实例 `import http from '@/api/http'`（baseURL `/api`，cookie 自带）；列表用 Element Plus `el-table`；视图用 `t()`（运行时键，免起后端即过 type-check，同 SSO/2FA T9 经验）；类型置 `src/types/oa/`。纯逻辑（状态文案/时间线合并/分支分组）抽到 `inboxModel.ts` 走 **vitest**（node env），视图只做装配。

## Task 12：前端 API 层 + TS 类型

**Files:**
- Create: `cp6.web/src/types/oa/inbox.ts`
- Create: `cp6.web/src/api/oa/inbox.ts` / `draft.ts` / `forecast.ts` / `flowAdmin.ts`

- [ ] **Step 1: TS 类型（对齐后端 DTO，`types/oa/inbox.ts`）**

```typescript
export interface PendingItem { taskId: string; instanceId: string; tokenId?: string; flowKey: string; flowName?: string;
  nodeId: string; nodeName?: string; starterId: string; starterName: string; bizType?: string; bizId?: string; isRead: boolean; sentAt: string }
export interface CcItem { ccId: string; instanceId: string; flowKey: string; flowName?: string; atNodeId?: string;
  starterId: string; starterName: string; isRead: boolean; createDate: string }
export interface RunningItem { instanceId: string; flowKey: string; flowName?: string; currentNode: string; status: number;
  currentHandlers: string[]; createDate: string }
export interface DoneItem { instanceId: string; flowKey: string; flowName?: string; starterId: string; starterName: string;
  formToStatus: number; doneAt: string; instanceStatus: number }
export interface TrendPoint { date: string; count: number }
export interface InboxStats { pendingCount: number; runningCount: number; doneThisMonth: number; rejectedBackToMe: number;
  trend: TrendPoint[]; recentPending: PendingItem[] }
export interface TimelineRow { stepSeq: number; tokenId?: string; nodeId: string; nodeName?: string;
  expectedHandlerId: string; expectedHandlerName: string; actualHandlerId?: string; actualHandlerName?: string;
  onBehalfOfId?: string; onBehalfOfName?: string; status: number; comment?: string; sentAt: string; handledAt?: string }
export interface SnapshotRow { stepSeq: number; nodeId: string; dataJson: string }
export interface CcRow { recipientId: string; recipientName: string; atNodeId?: string; isRead: boolean }
export interface ForecastStep { nodeId: string; nodeName?: string; type: string; approvers: string[]; resolved: boolean; note?: string }
export interface InboxDetail { instance: any; flowName?: string; formKey?: string; formSchemaJson?: string;
  currentDataJson: string; timeline: TimelineRow[]; snapshots: SnapshotRow[]; forecast: ForecastStep[]; cc: CcRow[] }
export interface BatchResultItem { taskId: string; ok: boolean; error?: string }
export interface FlowAdminItem { flowKey: string; flowName: string; formKey: string; version: number; enable: boolean }
```

- [ ] **Step 2: API 模块（4 文件，照 `api/wf/form.ts` 模式 `import http from '../http'`）**

`api/oa/inbox.ts`：
```typescript
import http from '../http'
export const inboxApi = {
  pending:   () => http.get('/oa/inbox/pending'),
  pendingCc: () => http.get('/oa/inbox/pending-cc'),
  running:   () => http.get('/oa/inbox/running'),
  done:      (p: { year?: number; month?: number; tab?: string }) => http.get('/oa/inbox/done', { params: p }),
  stats:     () => http.get('/oa/inbox/stats'),
  detail:    (instanceId: string) => http.get(`/oa/inbox/detail/${instanceId}`),
  markTaskRead: (id: string) => http.post('/oa/inbox/task/read', { id }),
  markCcRead:   (id: string) => http.post('/oa/inbox/cc/read', { id }),
  batch: (taskIds: string[], approve: boolean, comment?: string) => http.post('/oa/inbox/batch', { taskIds, approve, comment }),
}
```
`api/oa/draft.ts`：`list/save/update/submit/delete`（POST body 对齐 DraftController record）。
`api/oa/forecast.ts`：`preview: (flowKey, varsJson) => http.post('/oa/forecast/preview', { flowKey, varsJson })`。
`api/oa/flowAdmin.ts`：`list/get/enable`。

- [ ] **Step 3: type-check** — `cd cp6.web && npm run type-check`（仅类型/接口，无视图引用，应绿）。

- [ ] **Step 4: Commit**

```bash
git add cp6.web/src/types/oa/ cp6.web/src/api/oa/
git commit -m "feat(wfs-B): T12 前端 OA 信箱 API 层 + TS 类型(对齐后端 DTO)"
```

---

## Task 13：`inboxModel.ts` 纯逻辑 + vitest

**Files:**
- Create: `cp6.web/src/views/oa/inbox/inboxModel.ts`
- Test: `cp6.web/src/views/oa/inbox/inboxModel.test.ts`

> 把"状态→文案/颜色"、"时间线 = 持久行 + 预计段合并"、"按 TokenId 分支分组"抽成纯函数，单测覆盖。视图只调它。

- [ ] **Step 1: 写失败测试 `inboxModel.test.ts`**

```typescript
import { describe, it, expect } from 'vitest'
import { formToStatusText, instanceStatusType, mergeTimeline, groupByBranch } from './inboxModel'
import type { TimelineRow, ForecastStep } from '@/types/oa/inbox'

describe('inboxModel', () => {
  it('formToStatusText maps known codes', () => {
    expect(formToStatusText(0)).toBe('oa.formto.pending')
    expect(formToStatusText(1)).toBe('oa.formto.approved')
    expect(formToStatusText(6)).toBe('oa.formto.voided')
  })

  it('instanceStatusType maps to el-tag types', () => {
    expect(instanceStatusType(0)).toBe('warning')   // Running
    expect(instanceStatusType(1)).toBe('success')   // Approved
    expect(instanceStatusType(2)).toBe('danger')    // Rejected
  })

  it('mergeTimeline appends forecast steps after persisted rows, flagged', () => {
    const rows: TimelineRow[] = [{ stepSeq: 1, nodeId: 'n1', expectedHandlerId: 'u1', expectedHandlerName: 'A',
      status: 1, sentAt: '2026-06-27T10:00:00', actualHandlerName: 'A' } as any]
    const forecast: ForecastStep[] = [{ nodeId: 'n2', type: 'approval', approvers: ['B'], resolved: true }]
    const merged = mergeTimeline(rows, forecast)
    expect(merged).toHaveLength(2)
    expect(merged[0].forecast).toBe(false)
    expect(merged[1].forecast).toBe(true)
    expect(merged[1].nodeId).toBe('n2')
  })

  it('groupByBranch groups rows by tokenId', () => {
    const rows: any[] = [{ tokenId: 't1', nodeId: 'a' }, { tokenId: 't1', nodeId: 'b' }, { tokenId: 't2', nodeId: 'c' }]
    const groups = groupByBranch(rows)
    expect(groups.size).toBe(2)
    expect(groups.get('t1')).toHaveLength(2)
  })
})
```

- [ ] **Step 2: 跑测试确认失败** — `cd cp6.web && npx vitest run src/views/oa/inbox/inboxModel.test.ts`（模块未建）。

- [ ] **Step 3: 建 `inboxModel.ts`**

```typescript
import type { TimelineRow, ForecastStep } from '@/types/oa/inbox'

/** 关卡状态码 → i18n 键（FlowFormToStatus）。 */
export function formToStatusText(s: number): string {
  return ['oa.formto.pending', 'oa.formto.approved', 'oa.formto.rejected', 'oa.formto.transferred',
    'oa.formto.addsigned', 'oa.formto.skipped', 'oa.formto.voided'][s] ?? 'oa.formto.pending'
}

/** 实例状态码 → el-tag type（FlowInstanceStatus 0..4）。 */
export function instanceStatusType(s: number): string {
  return (['warning', 'success', 'danger', 'info', 'info'][s]) ?? 'info'
}
export function instanceStatusText(s: number): string {
  return ['oa.inst.running', 'oa.inst.approved', 'oa.inst.rejected', 'oa.inst.withdrawn', 'oa.inst.suspended', 'oa.inst.draft'][s] ?? 'oa.inst.running'
}

export interface MergedRow {
  forecast: boolean; nodeId: string; nodeName?: string; status?: number;
  expectedHandlerName?: string; actualHandlerName?: string; onBehalfOfName?: string;
  approvers?: string[]; resolved?: boolean; comment?: string; sentAt?: string; handledAt?: string; tokenId?: string
}

/** 时间线 = 持久行（完成/当前）+ 预计段（灰）。预计行 forecast=true。 */
export function mergeTimeline(rows: TimelineRow[], forecast: ForecastStep[]): MergedRow[] {
  const persisted: MergedRow[] = rows.map(r => ({
    forecast: false, nodeId: r.nodeId, nodeName: r.nodeName, status: r.status,
    expectedHandlerName: r.expectedHandlerName, actualHandlerName: r.actualHandlerName ?? undefined,
    onBehalfOfName: r.onBehalfOfName ?? undefined, comment: r.comment ?? undefined,
    sentAt: r.sentAt, handledAt: r.handledAt ?? undefined, tokenId: r.tokenId,
  }))
  const future: MergedRow[] = forecast.map(f => ({
    forecast: true, nodeId: f.nodeId, nodeName: f.nodeName, approvers: f.approvers, resolved: f.resolved,
  }))
  return [...persisted, ...future]
}

/** 按 TokenId 分支分组（并行履历各成一串；无 token 归 '_'）。 */
export function groupByBranch<T extends { tokenId?: string }>(rows: T[]): Map<string, T[]> {
  const m = new Map<string, T[]>()
  for (const r of rows) {
    const k = r.tokenId ?? '_'
    ;(m.get(k) ?? m.set(k, []).get(k)!).push(r)
  }
  return m
}
```

- [ ] **Step 4: 跑测试确认通过** — `npx vitest run src/views/oa/inbox/inboxModel.test.ts`（4 例 PASS）。

- [ ] **Step 5: Commit**

```bash
git add cp6.web/src/views/oa/inbox/inboxModel.ts cp6.web/src/views/oa/inbox/inboxModel.test.ts
git commit -m "feat(wfs-B): T13 inboxModel 纯逻辑(状态文案/时间线合并/分支分组)+vitest"
```

---

## Task 14：`InboxView` 信箱外壳（左文件夹 + 顶栏 + 内容区路由）

**Files:**
- Create: `cp6.web/src/views/oa/inbox/InboxView.vue`

> 外壳 = 左侧文件夹树（未處理/在途/已處理/暫存 + 填單入口占位 + 流程管理入口）+ 顶栏（搜索/语言已有全局）+ 中间内容区（按当前文件夹切子组件）+ 右侧详情抽屉 `el-drawer`（点列表行打开 `FormDetail`）+ 新建草稿对话框（选流程→`FormInitiate` 占位，P1 用 DraftService.save）。**单文件壳，子文件夹组件 T15 建**。

- [ ] **Step 1: 建 `InboxView.vue`**（要点，非整文件）
  - `el-container` 左 `el-aside` 文件夹菜单（`el-menu` + 未读 badge：`未處理` 显 `stats.pendingCount`）；右 `el-main` 动态 `<component :is="...">`（dashboard/pending/running/done/draft，T15）。
  - `onMounted` 调 `inboxApi.stats()` 填徽标；切文件夹切子组件。
  - 详情抽屉：`<el-drawer v-model="detail.open" size="60%"><FormDetail :instance-id="detail.id" /></el-drawer>`，子组件 `@open-detail="(id)=>{detail.id=id; detail.open=true}"`。
  - 顶栏「新建/填單」按钮 → 占位对话框（选 `flowAdminApi.list()` 启用流程 → 起草稿）。
- [ ] **Step 2: 占位子组件**：先建 5 个空壳 `.vue`（`<template><div/></template>`）使 `InboxView` 编译过；T15 填实。
- [ ] **Step 3: type-check** — `npm run type-check` 绿。
- [ ] **Step 4: Commit** `git commit -m "feat(wfs-B): T14 InboxView 信箱外壳(左文件夹+徽标+内容区+详情抽屉)"`

---

## Task 15：五文件夹视图（Dashboard / Pending / Running / Done / Draft）

**Files:**
- Create: `InboxDashboard.vue` / `InboxPending.vue` / `InboxRunning.vue` / `InboxDone.vue` / `InboxDraft.vue`（`views/oa/inbox/`）

- [ ] **Step 1: `InboxDashboard.vue`** — `inboxApi.stats()`：4 数字卡片（`el-card`×4：待我处理/我发起/本月完成/被退回）+ 近 7 天趋势（简单 `el-progress` 条或轻量柱状，禁引重图表库）+ 最近待办 `el-table`（recentPending，点击 `@open-detail`）。
- [ ] **Step 2: `InboxPending.vue`** — `待審核|CC` 用 `el-tabs`：待审核 = `inboxApi.pending()`（未读行 `font-weight:bold`，`row-class`）；CC = `inboxApi.pendingCc()`。复选框列 → 选中浮出批量条（同意/退回 + 意见输入 → `inboxApi.batch()`，结果 `BatchResultItem[]` 逐条 `ElMessage`）。行点击 `markTaskRead` + `@open-detail`。
- [ ] **Step 3: `InboxRunning.vue`** — `inboxApi.running()`：列「流程/单号/處理人(currentHandlers join '、')/状态(`instanceStatusType`)/提交时间」，点击 `@open-detail`。
- [ ] **Step 4: `InboxDone.vue`** — 月份选择器（`el-date-picker type=month` → year/month）+ `el-tabs` 全部|我的|CC（tab=all/mine/cc）→ `inboxApi.done()`。状态列用 `formToStatusText`/`instanceStatusType`。
- [ ] **Step 5: `InboxDraft.vue`** — `draftApi.list()`：列表 + 「编辑(打开草稿表单)/提交(`draftApi.submit`)/删除(`draftApi.delete` 二次确认)」。提交成功 `ElMessage` + 刷新。
- [ ] **Step 6: type-check + vitest 回归** — `npm run type-check && npx vitest run`（既有 29 + T13 绿）。
- [ ] **Step 7: Commit** `git commit -m "feat(wfs-B): T15 五文件夹视图(仪表盘/未處理批量/在途/已處理月份/暫存)"`

---

## Task 16：`FormDetail`（左读右签）+ `FlowTimeline`

**Files:**
- Create: `cp6.web/src/views/oa/inbox/FormDetail.vue`
- Create: `cp6.web/src/views/oa/inbox/FlowTimeline.vue`

> 详情 = `inboxApi.detail(instanceId)`。左只读表单（复用 `DynamicForm`，`:schema=parse(formSchemaJson)`、`v-model=parse(currentDataJson)`、`:mask=` 全 readonly via `buildFieldMask`）；右传签时间线（`FlowTimeline`）。底部操作条（按当前是否有我的待办显「同意/退回/加签」——P1 用单任务 `inboxApi.batch([myTaskId])` 或既有 wf act 接口）。

- [ ] **Step 1: `FlowTimeline.vue`** — props `{ timeline, forecast }` → `mergeTimeline` → `el-timeline`：持久行实色（`formToStatusText` + 代签显「actualHandlerName（代 onBehalfOfName 签）」+ 意见 + 时刻），预计行（`forecast=true`）灰色虚线 + `approvers.join('、')` 或 `resolved=false` 显关卡名占位 + note。多 token 时按 `groupByBranch` 分支标题分组。
- [ ] **Step 2: `FormDetail.vue`** — 调 detail；左 `DynamicForm`（readonly mask via `buildFieldMask(fieldNames, allReadonly)`）；右 `<FlowTimeline :timeline="d.timeline" :forecast="d.forecast" />`；CC 行小标签区；底部操作条（有我待办→同意/退回，调批量单条；无→隐藏）。`safeParseObject` 解析 JSON。
- [ ] **Step 3: type-check** 绿。
- [ ] **Step 4: Commit** `git commit -m "feat(wfs-B): T16 FormDetail 左读右签 + FlowTimeline(持久+预计+代签+分支)"`

---

## Task 17：`FlowAdmin` 轻量流程管理视图

**Files:**
- Create: `cp6.web/src/views/oa/admin/FlowAdmin.vue`

- [ ] **Step 1: `FlowAdmin.vue`** — `flowAdminApi.list()`：`el-table` 列「FlowKey/流程名/FormKey/版本/启用」；启用列 `el-switch` → `flowAdminApi.enable(flowKey, val)`，`E-WF-008` 冲突 `ElMessage.error`（i18n 译文）后回滚开关。顶部说明「每表单仅一条启用流程」。
- [ ] **Step 2: type-check** 绿。
- [ ] **Step 3: Commit** `git commit -m "feat(wfs-B): T17 FlowAdmin 轻量流程管理(列表+启停+1:1冲突提示)"`

---

## Task 18：路由重定向 + OA 菜单 + i18n 五语 seed

**Files:**
- Modify: `cp6.web/src/router/index.ts`
- Create: `CP6.WebApi/Seed/I18nOaInboxScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（seed 合并 + OA 菜单 733-737）

- [ ] **Step 1: 路由**（`router/index.ts`）
  - `viewModules` 加：`'/oa/inbox': () => import('@/views/oa/inbox/InboxView.vue')`、`'/oa/flow-admin': () => import('@/views/oa/admin/FlowAdmin.vue')`。
  - 旧路由重定向（静态 routes，`standalone` 或 children 重定向均可，照现有重定向写法）：`/wf/todo` → `/oa/inbox`、`/wf/my-applications` → `/oa/inbox`。**保留 viewModules 里 `/wf/todo`/`/wf/my-applications` 旧条目则会与重定向冲突——删 viewModules 这两条，仅留重定向**（R3：旧视图文件不删，仅路由重定向）。
- [ ] **Step 2: i18n seed `I18nOaInboxScreenSeed.cs`**（照 `I18nSec2faScreenSeed` 静态 `Sys_Lang[] Items`，五语 `LangKey/ZhCN/ZhTW/En/Ja/Ko`）
  - 错误码：`E-WF-001`~`E-WF-008`（act-as 无授权/转交非法/草稿越权/批量部分失败/抄送空/流程停用/实例不存在/表单已绑启用流程）——**Phase B 实际用到 003/004/006/007/008**，其余先占位。
  - 关卡状态：`oa.formto.pending/approved/rejected/transferred/addsigned/skipped/voided`（7）。
  - 实例状态：`oa.inst.running/approved/rejected/withdrawn/suspended/draft`（6）。
  - 信箱画面词：`oa.inbox.pending/cc/running/done/draft/dashboard/catalog/query/settings`、`oa.inbox.batch.approve/reject/comment`、`oa.inbox.col.*`、`oa.forecast.title/placeholder`、`oa.flowadmin.title/uniqueHint` 等（约 30~40 词）。
  - 菜单 nav：`nav.733`(OA 工作台/电子表单信箱)、`nav.734`(流程管理) 等。
- [ ] **Step 3: Program.cs** — `.Concat(CP6.WebApi.Seed.I18nOaInboxScreenSeed.Items)` 接入 seed 合并链（line ~1696）；OA 菜单 seed（MenuId 733=信箱、734=流程管理，对齐现有 `Sys_Menu` 字段 + RoleMenu 授 admin，照菜单 seed 样式；菜单分组归「OA」大组，[[project_module_taxonomy]]）。
- [ ] **Step 4: 后端起一次 + i18n 快照**
  - `dotnet run --project CP6.WebApi`（迁移自动建 IsRead/ReadAt 列 + seed 落词条）→ 停。
  - `cd cp6.web && npm run i18n:pull && npm run gen-types && npm run i18n:check`（拉新词条进 keys.generated，类型重生，校验无缺）。
- [ ] **Step 5: 全前端回归** — `npm run type-check && npx vitest run && npm run build` 全绿。
- [ ] **Step 6: Commit** `git commit -m "feat(wfs-B): T18 旧路由重定向+OA菜单733/734+i18n五语seed+快照重建"`

---

## Task 19：gstack 真浏览器 QA + 全回归固化

**Files:**
- Create: `docs/superpowers/qa/wfs-form-inbox/phaseB/`（README + seed.sql + 截图）

> 必跑 skill：gstack（[[feedback_coding_skills]]）。真 headless Chromium 跑收件箱全流程，落库核验。

- [ ] **Step 1: 种 QA 数据**（seed.sql，复用 Phase A 夹具 + 新流程/表单）：一条启用 `Wf_FlowDef`（leave，含 CC 节点）+ `Wf_FormDef`（leave 字段 schema）+ 三用户（发起/审批/抄送，复用 admin BCrypt=123456）。
- [ ] **Step 2: 后端 + 前端起真服务**（`dotnet run` + `npm run dev`），gstack 信任 dev cert。
- [ ] **Step 3: 浏览器全流程**（gstack browse headless）：
  1. 登录 → 进 `/oa/inbox` 仪表盘（4 卡片渲染 + i18n 解析无裸键）。
  2. 旧 `/wf/todo` → 自动重定向到 `/oa/inbox`（R3 验证）。
  3. 发起人起草草稿（暫存）→ 列表可见 → 提交 → 进流程（DB 确 `Status=Running` + token Active + FlowFormTo Pending 行）。
  4. 审批人登录 → 未處理 待審核 显该单（未读加粗）→ 点开详情：左只读表单渲染 + 右时间线（当前关卡 + **预计段灰显 end**）→ 同意。
  5. 抄送人登录 → 未處理 CC 显该单；已處理 CC 标签可查。
  6. 批量：造两条待办 → 勾选 → 批量同意 → 逐条结果 toast。
  7. 流程管理 `/oa/flow-admin`：列表 + 启用冲突（同 FormKey 启第二条）→ `E-WF-008` 本地化报错。
- [ ] **Step 4: 回归全绿** — 后端 `dotnet test CP6.Tests`、前端 `type-check/vitest/build` 全绿；记录基线（1212 → 1212+N）。
- [ ] **Step 5: 固化 QA** — README（可复现步骤）+ seed.sql + 截图存 `docs/superpowers/qa/wfs-form-inbox/phaseB/`。
- [ ] **Step 6: Commit** `git commit -m "test(wfs-B): T19 Phase B gstack 真浏览器 QA 固化(收件箱四文件夹+左读右签+重定向+流程管理)"`

---

## Phase B 完成定义（DoD）

- [ ] 后端 Oa 五服务（Inbox/Draft/Forecast/FlowAdmin + OaUserNames）+ 4 控制器 + DI 全装配，`dotnet test` 全绿（≥ 1212 + 新增）。
- [ ] 前端信箱壳 + 五文件夹 + 详情左读右签 + 流程管理 UI；旧 `/wf/todo`、`/wf/my-applications` 重定向。
- [ ] i18n 五语 seed 落库 + 快照重建无缺键；`type-check/vitest/build` 全绿。
- [ ] gstack 真浏览器 QA 全流程通过并固化。
- [ ] 兼容硬闸：`Wf` 既有测试零回归（仅加 `StartDraftAsync` + Task 两列 + Draft 状态值）。
- [ ] 每 Task 本地 commit（不 push；push 由用户监督自跑）。

**▶️ Phase B 之后**：`{C 信箱进阶（act-as/转交/填單表单库/表單查詢/設定）‖ C′ 基础版流程设计器}`（umbrella §5），各自 writing-plans → subagent TDD。

---
