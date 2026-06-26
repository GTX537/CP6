# WFS Phase A（引擎 + 读模型）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 CP6 现有"单活动节点（`CurrentNode`）审批引擎"原地泛化为"token 多活动节点 + 并行网关 + `INodeHandler` 插件"的 BPM 运行时，并让引擎在 token 推进时同步写出读模型（传签履历 / 每关卡快照 / 抄送），全程 **631 个 Wf 审批测试不改一行照绿**。

**Architecture:** 三层中的 L0+L1。L0 = `Wf_FlowToken` 独立表 + 三 token 原语（spawn/advance/consume）+ 5 个 `INodeHandler`（start/approval/end/parallelSplit/parallelJoin）+ 乐观并发重试。L1 = 三张读模型表（`Wf_FlowFormTo`/`Wf_FlowData`/`Wf_FlowCc`），由引擎在进节点/办结/流转时 **correct-by-construction** 落库，与 token 变更共享同一 `SaveChanges`（原子）。原地演进 `FlowEngine`，`IFlowEngine` 对外签名不动。

**Tech Stack:** .NET 8 / EF Core（SqlServer + InMemory 测试）/ xUnit（`CP6.Tests`）。启动项目 `CP6.WebApi`，DbContext + 迁移在 `CP6.Core`。

**配套 spec（落码前必读）：**
- `docs/superpowers/specs/2026-06-26-wfs-runtime-kernel-design.md`（L0 内核，§0~§12，含逐行锚点）
- `docs/superpowers/specs/2026-06-26-wfs-form-inbox-unified-design.md`（umbrella；§2.2~§2.4 读模型表、§3 写入钩子）

---

## Scope Check（本计划含两子系统，按 Part 顺序执行）

- **Part 1（T1~T8）= L0 token 内核**：独立可测、可交付（631 绿 + 并行流程跑通）。
- **Part 2（T9~T11）= L1 读模型写入钩子**：依赖 Part 1 的 token 原语/handler。

两部分顺序执行；若需分两次会话，Part 1 完成即是一个可交付里程碑。

---

## File Structure（先锁分解）

**新建实体（`CP6.Entity/DomainModels/Wf/`）：**
- `Wf_FlowToken.cs` — 令牌（活动执行点）
- `Wf_FlowFormTo.cs` — 传签履历台账
- `Wf_FlowData.cs` — 每关卡表单快照
- `Wf_FlowCc.cs` — 抄送

**新建服务（`CP6.Core/Services/Wf/`）：**
- `INodeHandler.cs` — 节点处理器接口 + `NodeContext`（internal）
- `FlowEngine.Tokens.cs` — `FlowEngine` partial，三 token 原语 + 内部访问器（internal）
- `FlowEngine.ReadModel.cs` — `FlowEngine` partial，读模型写入钩子（internal）
- `NodeHandlers/StartNodeHandler.cs` / `ApprovalNodeHandler.cs` / `EndNodeHandler.cs` / `ParallelSplitNodeHandler.cs` / `ParallelJoinNodeHandler.cs`

**新建种子（`CP6.WebApi/Seed/`）：**
- `WfTokenBackfillSeed.cs` — 在途实例 token 幂等回填

**修改：**
- `CP6.Core/Services/Wf/WfStatus.cs` — 加 `FlowTokenStatus` / `FlowFormToStatus`
- `CP6.Core/Services/Wf/FlowSchema.cs` — `FlowNode.Type` 词汇表注释 + `FlowNode.CcUsers/CcRoles` + `FlowEdge.CcUsers/CcRoles`
- `CP6.Core/Services/Wf/FlowEngine.cs` — `EnterNodeAsync` 多态分发、ctor 接 handlers、`SubmitAsync`/`ActAsync` token 化、私有方法转 internal
- `CP6.Core/Services/Wf/AdvancedFlow.cs` — `SendBackAsync`/`AddSignAsync` token 感知
- `CP6.Entity/DomainModels/Wf/Wf_FlowInstance.cs` — 加 `RowVersion`
- `CP6.Entity/DomainModels/Wf/Wf_FlowTask.cs` — 加 `TokenId`
- `CP6.Core/EFDbContext/CP6Context.cs` — 4 个新 DbSet + 索引
- `CP6.WebApi/Program.cs` — 注册 5 个 `INodeHandler` + 调回填种子

**测试（`CP6.Tests/Wf/`）：** `FlowTokenKernelTests.cs`、`ParallelGatewayTests.cs`、`FlowConcurrencyTests.cs`（SQLite）、`ReadModelHookTests.cs`、`WfTokenBackfillTests.cs`。

---

## 通用约定

- **测试基线**：`dotnet test CP6.Tests`（当前 1189 测 / 1 skip，其中 Wf 约 631）。每个 Task 末尾跑相关测试 + 关键节点跑全量。
- **兼容硬闸**：Part 1 任一改动后，`dotnet test CP6.Tests --filter "FullyQualifiedName~Wf"` **不得有任何既有测试转红**（需改既有测试 = 兼容破坏，回退排查）。
- **测试 DB 工厂**（沿用现有 `FlowEngineTests`）：
  ```csharp
  private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
      .Options);
  private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
  ```
- **EF 迁移命令**：`dotnet ef migrations add <Name> -p CP6.Core -s CP6.WebApi`（若无 ef 工具：`dotnet tool install --global dotnet-ef`）。InMemory 测试不走迁移（按实体模型建图），迁移仅供 SqlServer 运行期。
- **commit**：每 Task 末尾本地 commit（不 push）。

---

# Part 1 — L0 token 内核

## Task 1：内核数据模型 + 迁移

**Files:**
- Create: `CP6.Entity/DomainModels/Wf/Wf_FlowToken.cs`
- Modify: `CP6.Core/Services/Wf/WfStatus.cs`
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FlowInstance.cs`
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FlowTask.cs`
- Modify: `CP6.Core/Services/Wf/FlowSchema.cs`（仅 `FlowNode.Type` 注释加并行类型，不加字段）
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`
- Test: `CP6.Tests/Wf/FlowTokenKernelTests.cs`

- [ ] **Step 1: 写失败测试**

`CP6.Tests/Wf/FlowTokenKernelTests.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class FlowTokenKernelTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    [Fact]
    public async Task FlowToken_Persists_WithStatusAndLineage()
    {
        using var db = NewDb();
        var instId = Guid.NewGuid();
        db.Wf_FlowTokens.Add(new Wf_FlowToken
        {
            Id = Guid.NewGuid(), InstanceId = instId, NodeId = "n1",
            Status = FlowTokenStatus.Active, ParentTokenId = null, ForkId = null,
        });
        await db.SaveChangesAsync();

        var tok = await db.Wf_FlowTokens.SingleAsync();
        Assert.Equal("n1", tok.NodeId);
        Assert.Equal(FlowTokenStatus.Active, tok.Status);
        Assert.Equal(0, FlowTokenStatus.Active);
        Assert.Equal(1, FlowTokenStatus.Consumed);
        Assert.Equal(2, FlowTokenStatus.Cancelled);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~FlowTokenKernelTests"`
Expected: 编译失败（`Wf_FlowTokens` / `Wf_FlowToken` / `FlowTokenStatus` 未定义）。

- [ ] **Step 3: 建实体 `Wf_FlowToken.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 流程令牌（WFS P1 运行时内核）。一个 token = 实例内一个活动执行点（停留节点）。
/// 单路径审批：一实例恒一 Active token；并行分叉：一实例多 Active token 并存。
/// 血缘：ParentTokenId 串嵌套层级；ForkId 标同批分叉（parallelJoin 靠"同 ForkId 计数"认亲）。
/// "实例进行中" = 存在 Active token（取代旧"CurrentNode 单值"判定）。
/// </summary>
[Table("Wf_FlowToken")]
public class Wf_FlowToken : BaseTenantEntity
{
    public Guid InstanceId { get; set; }

    [MaxLength(100)]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>0=Active 1=Consumed 2=Cancelled。见 FlowTokenStatus。</summary>
    public int Status { get; set; }

    /// <summary>父令牌 Id（嵌套血缘）。根 token=null。</summary>
    public Guid? ParentTokenId { get; set; }

    /// <summary>分叉批次 Id（同批共享，join 认亲计数）。根/线性 token=null。</summary>
    public Guid? ForkId { get; set; }
}
```

- [ ] **Step 4: 加 `FlowTokenStatus`（`WfStatus.cs` 末尾）**

```csharp
/// <summary>流程令牌状态（Wf_FlowToken.Status）。Active 才参与流转 / join 计数。</summary>
public static class FlowTokenStatus
{
    public const int Active = 0;
    public const int Consumed = 1;
    public const int Cancelled = 2;
}
```

- [ ] **Step 5: `Wf_FlowInstance` 加 `RowVersion`**

在 `Wf_FlowInstance.cs` 类内追加：
```csharp
/// <summary>乐观并发标记（WFS P1）：并行分支近同时办结时序列化（§6 / Task 6）。</summary>
[Timestamp]
public byte[]? RowVersion { get; set; }
```
（顶部确保 `using System.ComponentModel.DataAnnotations;`）

- [ ] **Step 6: `Wf_FlowTask` 加 `TokenId`**

在 `Wf_FlowTask.cs` 类内追加：
```csharp
/// <summary>所属令牌 → Wf_FlowToken.Id（WFS P1）。会签计票按 (InstanceId,NodeId,TokenId) 隔离。
/// 可空：旧数据 / 回填前为 null（Task 8 回填补齐）；新建 task 必带（ApprovalNodeHandler 填）。</summary>
public Guid? TokenId { get; set; }
```

- [ ] **Step 7: `FlowSchema.cs` `FlowNode.Type` 注释加并行类型（不加字段）**

把 `FlowNode.Type` 注释改为：
```csharp
/// <summary>节点类型：start / approval / end / parallelSplit / parallelJoin。
/// parallelSplit=并行分叉(一入 N 出，无条件全激活)；parallelJoin=并行汇聚(N 入一出，等齐放行)。</summary>
public string Type { get; set; } = "approval";
```

- [ ] **Step 8: `CP6Context.cs` 加 DbSet + 索引**

DbSet（挨着现有 Wf DbSet 后）：
```csharp
public DbSet<Wf_FlowToken> Wf_FlowTokens { get; set; }
```
`OnModelCreating` 的 Wf 索引区加：
```csharp
modelBuilder.Entity<Wf_FlowToken>(e =>
{
    e.HasIndex(x => new { x.InstanceId, x.Status }).HasDatabaseName("IX_Wf_FlowToken_InstanceStatus");
    e.HasIndex(x => new { x.InstanceId, x.ForkId, x.NodeId }).HasDatabaseName("IX_Wf_FlowToken_Fork");
});
```

- [ ] **Step 9: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~FlowTokenKernelTests"`
Expected: PASS。

- [ ] **Step 10: 加 EF 迁移**

Run: `dotnet ef migrations add WfsPhaseAKernel -p CP6.Core -s CP6.WebApi`
Expected: 生成迁移：新表 `Wf_FlowToken`（6 业务列 + BaseTenantEntity 公共列 + 2 索引）、`Wf_FlowInstance` 加 `RowVersion`（rowversion）、`Wf_FlowTask` 加 `TokenId`（uniqueidentifier NULL）。打开生成文件肉眼核对 `Up()` 仅这些动作、无数据迁移。

- [ ] **Step 11: 兼容回归 — 631 照绿**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~Wf"`
Expected: 全绿（仅数据模型加列/加表，零行为改动）。

- [ ] **Step 12: Commit**

```bash
git add CP6.Entity/DomainModels/Wf/Wf_FlowToken.cs CP6.Core/Services/Wf/WfStatus.cs CP6.Entity/DomainModels/Wf/Wf_FlowInstance.cs CP6.Entity/DomainModels/Wf/Wf_FlowTask.cs CP6.Core/Services/Wf/FlowSchema.cs CP6.Core/EFDbContext/CP6Context.cs CP6.Core/Migrations/ CP6.Tests/Wf/FlowTokenKernelTests.cs
git commit -m "feat(wfs-A): T1 Wf_FlowToken 表 + FlowTokenStatus + Instance.RowVersion + Task.TokenId + 迁移"
```

---

## Task 2：读模型数据模型 + 迁移

**Files:**
- Create: `CP6.Entity/DomainModels/Wf/Wf_FlowFormTo.cs` / `Wf_FlowData.cs` / `Wf_FlowCc.cs`
- Modify: `CP6.Core/Services/Wf/WfStatus.cs`（加 `FlowFormToStatus`）
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`（3 DbSet + 索引）
- Test: `CP6.Tests/Wf/FlowTokenKernelTests.cs`（追加一例）

- [ ] **Step 1: 写失败测试（追加）**

```csharp
[Fact]
public async Task ReadModelTables_Persist()
{
    using var db = NewDb();
    var inst = Guid.NewGuid();
    db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
    {
        Id = Guid.NewGuid(), InstanceId = inst, NodeId = "n1", StepSeq = 1,
        ExpectedHandlerId = Guid.NewGuid(), Status = FlowFormToStatus.Pending, SentAt = new DateTime(2026, 6, 26),
    });
    db.Wf_FlowDatas.Add(new Wf_FlowData { Id = Guid.NewGuid(), InstanceId = inst, NodeId = "n1", StepSeq = 1, DataJson = "{}" });
    db.Wf_FlowCcs.Add(new Wf_FlowCc { Id = Guid.NewGuid(), InstanceId = inst, RecipientId = Guid.NewGuid() });
    await db.SaveChangesAsync();

    Assert.Equal(1, await db.Wf_FlowFormTos.CountAsync());
    Assert.Equal(FlowFormToStatus.Pending, (await db.Wf_FlowFormTos.SingleAsync()).Status);
    Assert.Equal(1, await db.Wf_FlowDatas.CountAsync());
    Assert.False((await db.Wf_FlowCcs.SingleAsync()).IsRead);
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~ReadModelTables_Persist"`
Expected: 编译失败（实体未定义）。

- [ ] **Step 3: 建 `Wf_FlowFormTo.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 传签履历台账（WFS 读模型）。token 每到一个人工关卡落一行：送签时建、处理时更新。
/// 带 TokenId → 并行多分支履历各成一串。与 Wf_FlowHistory（纯追加事件日志）分工互补。
/// </summary>
[Table("Wf_FlowFormTo")]
public class Wf_FlowFormTo : BaseTenantEntity
{
    public Guid InstanceId { get; set; }
    public Guid? TokenId { get; set; }
    public int StepSeq { get; set; }

    [MaxLength(100)] public string? FromNodeId { get; set; }
    [MaxLength(100)] public string NodeId { get; set; } = string.Empty;
    [MaxLength(100)] public string? NodeCode { get; set; }
    [MaxLength(200)] public string? NodeName { get; set; }

    public Guid ExpectedHandlerId { get; set; }
    public Guid? ActualHandlerId { get; set; }
    public Guid? OnBehalfOfId { get; set; }

    /// <summary>0=待签 1=同意 2=驳回 3=转交 4=加签 5=跳过 6=作废。见 FlowFormToStatus。</summary>
    public int Status { get; set; }

    [MaxLength(1000)] public string? Comment { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime? HandledAt { get; set; }
}
```

- [ ] **Step 4: 建 `Wf_FlowData.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 每关卡表单快照（WFS 读模型）。每到一关 / 每次办结存一份当时表单字段值（不可变留痕）。
/// 区别 Wf_FormData（整单最新）/ VarsJson（流程变量，会被覆盖）：本表按 StepSeq 串"每步变化轨迹"。
/// </summary>
[Table("Wf_FlowData")]
public class Wf_FlowData : BaseTenantEntity
{
    public Guid InstanceId { get; set; }
    public Guid? TokenId { get; set; }
    public int StepSeq { get; set; }
    [MaxLength(100)] public string NodeId { get; set; } = string.Empty;
    [Column(TypeName = "nvarchar(max)")] public string DataJson { get; set; } = "{}";
}
```

- [ ] **Step 5: 建 `Wf_FlowCc.cs`**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>抄送（WFS 读模型）。节点/路徑/提交/结束抄送均落一行；IsRead 给信箱"未读"标记。</summary>
[Table("Wf_FlowCc")]
public class Wf_FlowCc : BaseTenantEntity
{
    public Guid InstanceId { get; set; }
    public Guid RecipientId { get; set; }
    [MaxLength(100)] public string? AtNodeId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
```

- [ ] **Step 6: 加 `FlowFormToStatus`（`WfStatus.cs`）**

```csharp
/// <summary>传签履历关卡状态（Wf_FlowFormTo.Status）。</summary>
public static class FlowFormToStatus
{
    public const int Pending = 0;     // 待签
    public const int Approved = 1;    // 同意
    public const int Rejected = 2;    // 驳回
    public const int Transferred = 3; // 转交
    public const int AddSigned = 4;   // 加签
    public const int Skipped = 5;     // 跳过 / 会签未轮到
    public const int Voided = 6;      // 作废（驳回连坐 / 退回清场）
}
```

- [ ] **Step 7: `CP6Context.cs` 加 3 DbSet + 索引**

```csharp
public DbSet<Wf_FlowFormTo> Wf_FlowFormTos { get; set; }
public DbSet<Wf_FlowData> Wf_FlowDatas { get; set; }
public DbSet<Wf_FlowCc> Wf_FlowCcs { get; set; }
```
索引（Wf 区）：
```csharp
modelBuilder.Entity<Wf_FlowFormTo>(e =>
{
    e.HasIndex(x => new { x.InstanceId, x.StepSeq }).HasDatabaseName("IX_Wf_FlowFormTo_Step");
    e.HasIndex(x => new { x.InstanceId, x.TokenId }).HasDatabaseName("IX_Wf_FlowFormTo_Token");
    e.HasIndex(x => new { x.ExpectedHandlerId, x.Status }).HasDatabaseName("IX_Wf_FlowFormTo_Handler");
});
modelBuilder.Entity<Wf_FlowData>(e =>
    e.HasIndex(x => new { x.InstanceId, x.StepSeq }).HasDatabaseName("IX_Wf_FlowData_Step"));
modelBuilder.Entity<Wf_FlowCc>(e =>
{
    e.HasIndex(x => new { x.RecipientId, x.IsRead }).HasDatabaseName("IX_Wf_FlowCc_Recipient");
    e.HasIndex(x => x.InstanceId).HasDatabaseName("IX_Wf_FlowCc_Instance");
});
```

- [ ] **Step 8: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~ReadModelTables_Persist"`
Expected: PASS。

- [ ] **Step 9: 加迁移**

Run: `dotnet ef migrations add WfsPhaseAReadModel -p CP6.Core -s CP6.WebApi`
Expected: 仅 3 张新表 + 索引，无数据迁移。

- [ ] **Step 10: Commit**

```bash
git add CP6.Entity/DomainModels/Wf/Wf_FlowFormTo.cs CP6.Entity/DomainModels/Wf/Wf_FlowData.cs CP6.Entity/DomainModels/Wf/Wf_FlowCc.cs CP6.Core/Services/Wf/WfStatus.cs CP6.Core/EFDbContext/CP6Context.cs CP6.Core/Migrations/ CP6.Tests/Wf/FlowTokenKernelTests.cs
git commit -m "feat(wfs-A): T2 读模型三表 Wf_FlowFormTo/FlowData/Cc + FlowFormToStatus + 迁移"
```

---

## Task 3：token 三原语 + `INodeHandler` 接口

**Files:**
- Create: `CP6.Core/Services/Wf/INodeHandler.cs`
- Create: `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`
- Test: `CP6.Tests/Wf/FlowTokenKernelTests.cs`（追加）

> `AdvanceToken` 依赖 `EnterNodeAsync(token)`（Task 4 引入），故本 Task 只做 `SpawnToken`/`ConsumeToken`/`CancelAllActiveTokens`/`FinishIfDrained` 四原语 + 接口骨架；`AdvanceToken` 在 Task 4 加。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void TokenPrimitives_SpawnConsumeDrain()
{
    using var db = NewDb();
    var eng = new FlowEngine(db, new ApproverResolver(db));
    var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "f", StarterId = Guid.NewGuid(), Status = FlowInstanceStatus.Running, CurrentNode = "n1" };
    db.Wf_FlowInstances.Add(inst);

    var tok = eng.SpawnToken(inst, new FlowNode { Id = "n1" }, parent: null, fork: null);
    Assert.Equal(FlowTokenStatus.Active, tok.Status);
    Assert.Equal("n1", tok.NodeId);

    eng.FinishIfDrained(inst);
    Assert.Equal(FlowInstanceStatus.Running, inst.Status);   // 还有 Active → 不终态

    eng.ConsumeToken(tok);
    Assert.Equal(FlowTokenStatus.Consumed, tok.Status);
    eng.ConsumeToken(tok);                                   // 重放守卫：no-op
    Assert.Equal(FlowTokenStatus.Consumed, tok.Status);

    eng.FinishIfDrained(inst);
    Assert.Equal(FlowInstanceStatus.Approved, inst.Status);  // 无 Active → 通过
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~TokenPrimitives_SpawnConsumeDrain"`
Expected: 编译失败（`SpawnToken`/`ConsumeToken`/`FinishIfDrained` 未定义）。

- [ ] **Step 3: 建 `INodeHandler.cs`**

```csharp
namespace CP6.Core.Services.Wf;

using CP6.Entity.DomainModels.Wf;

/// <summary>节点处理器（WFS P1 插件架构）。每种 FlowNode.Type 一实现，封装"token 进该节点做什么"。
/// EnterNodeAsync 退化为 _handlers[node.Type].OnEnterAsync(ctx)。改库不落盘（调用方统一 SaveChanges）。</summary>
internal interface INodeHandler
{
    string Type { get; }
    Task OnEnterAsync(NodeContext ctx);
}

/// <summary>节点处理上下文。操作主语 = token（一实例可多 token）。Engine 回指以复用引擎能力。</summary>
internal sealed class NodeContext
{
    public required Wf_FlowInstance Inst { get; init; }
    public required FlowSchema Schema { get; init; }
    public required FlowNode Node { get; init; }
    public required Wf_FlowToken Token { get; init; }
    public required FlowEngine Engine { get; init; }
}
```

- [ ] **Step 4: 建 `FlowEngine.Tokens.cs`（四原语 + 内部访问器）**

```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    // 供 handler 经 ctx.Engine 复用（InternalsVisibleTo CP6.Tests）
    internal CP6Context Db => _db;
    internal IApproverResolver Approver => _approver;
    internal IWfNotifier Notifier => _notifier;

    /// <summary>生一个 Active token 停在 node。parent/fork 串血缘（根皆 null）。不落盘。</summary>
    internal Wf_FlowToken SpawnToken(Wf_FlowInstance inst, FlowNode node, Guid? parent = null, Guid? fork = null)
    {
        var tok = new Wf_FlowToken
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = node.Id,
            Status = FlowTokenStatus.Active, ParentTokenId = parent, ForkId = fork,
            Creator = inst.StarterId.ToString(),
        };
        _db.Wf_FlowTokens.Add(tok);   // TenantId 由 StampTenant 自动盖
        return tok;
    }

    /// <summary>消费 token（正常退场）。带 Active 守卫 → 重放 no-op。</summary>
    internal void ConsumeToken(Wf_FlowToken token)
    {
        if (token.Status != FlowTokenStatus.Active) return;
        token.Status = FlowTokenStatus.Consumed;
    }

    /// <summary>驳回连坐：本实例全 Active token → Cancelled。并查 DB + EF Local。</summary>
    internal void CancelAllActiveTokens(Guid instanceId)
    {
        var actives = _db.Wf_FlowTokens.Local
            .Where(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active).ToList();
        foreach (var t in _db.Wf_FlowTokens
                     .Where(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active).ToList())
            if (!actives.Contains(t)) actives.Add(t);
        foreach (var t in actives) t.Status = FlowTokenStatus.Cancelled;
    }

    /// <summary>无 Active token 残留 ⇒ 实例正常通过（置 Approved；dispatch 由调用方在 SaveChanges 前做）。</summary>
    internal void FinishIfDrained(Wf_FlowInstance inst)
    {
        var anyActive = _db.Wf_FlowTokens.Local.Any(t => t.InstanceId == inst.Id && t.Status == FlowTokenStatus.Active)
            || _db.Wf_FlowTokens.Any(t => t.InstanceId == inst.Id && t.Status == FlowTokenStatus.Active);
        if (anyActive) return;
        if (inst.Status != FlowInstanceStatus.Running) return;   // 已驳回/撤回，不覆盖
        inst.Status = FlowInstanceStatus.Approved;
    }
}
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~TokenPrimitives_SpawnConsumeDrain"`
Expected: PASS。

- [ ] **Step 6: Commit**

```bash
git add CP6.Core/Services/Wf/INodeHandler.cs CP6.Core/Services/Wf/FlowEngine.Tokens.cs CP6.Tests/Wf/FlowTokenKernelTests.cs
git commit -m "feat(wfs-A): T3 token 三原语(spawn/consume/drain)+INodeHandler 接口骨架"
```

---

## Task 4：`INodeHandler` 多态分发 + start/approval/end handler + Submit/Act token 化（★ 兼容硬闸）

**Files:**
- Create: `CP6.Core/Services/Wf/NodeHandlers/StartNodeHandler.cs` / `ApprovalNodeHandler.cs` / `EndNodeHandler.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`（ctor、`EnterNodeAsync`、`SubmitAsync`、`ActAsync`、私有转 internal）
- Modify: `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`（加 `AdvanceToken`）
- Modify: `CP6.WebApi/Program.cs`（注册 3 handler）
- Test: 既有 `CP6.Tests`（631）= 验收闸；不新增功能测试（行为等价旧引擎）。

> **本 Task 是把"单 token 线性"逐字等价迁到 handler + token 上。完成判据 = 631 测试不改一行照绿。**

- [ ] **Step 1: 私有方法转 internal（`FlowEngine.cs`）**

把以下方法签名 `private` → `internal`（供 handler 经 `ctx.Engine` 调用）：`AddHistory`、`Suspend`、`ResolveActualAssigneeAsync`、`NextNodeAsync`（暂留，AdvanceToken 复用其语义）。把 `static`私有 `BuildRule`、`NodeDueAt`、`FindNode`、`IsType` 改 `internal static`。

- [ ] **Step 2: ctor 接 `IEnumerable<INodeHandler>?`（默认回退）**

`FlowEngine.cs` 字段 + ctor：
```csharp
private readonly IReadOnlyDictionary<string, INodeHandler> _handlers;

public FlowEngine(CP6Context db, IApproverResolver approver, IWfNotifier? notifier = null,
                  ApprovalDispatcher? dispatcher = null, IEnumerable<INodeHandler>? handlers = null)
{
    _db = db;
    _approver = approver;
    _notifier = notifier ?? new NullWfNotifier();
    _dispatcher = dispatcher ?? new ApprovalDispatcher(Array.Empty<IApprovalCallback>());
    _handlers = (handlers ?? DefaultHandlers()).ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase);
}

private static IEnumerable<INodeHandler> DefaultHandlers() => new INodeHandler[]
{
    new StartNodeHandler(), new ApprovalNodeHandler(), new EndNodeHandler(),
    new ParallelSplitNodeHandler(), new ParallelJoinNodeHandler(),   // 后两个 Task 5 建；先引用，Task 5 前本步暂只列已建的 3 个
};
```
> 注：本 Task 5 个 handler 中 split/join 在 Task 5 才建。**本 Task 的 `DefaultHandlers()` 暂只 new 已建的 `StartNodeHandler/ApprovalNodeHandler/EndNodeHandler` 三个**；Task 5 建好 split/join 后再补进数组（届时该步在 Task 5 完成）。

- [ ] **Step 3: `EnterNodeAsync` 退化为多态分发（`FlowEngine.cs`）**

把现 `private async Task EnterNodeAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node)`（连同其 if/else 体）整体替换为：
```csharp
internal async Task EnterNodeAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node, Wf_FlowToken token)
{
    inst.CurrentNode = node.Id;   // 兼容：保留代表节点
    var type = (node.Type ?? "approval").Trim().ToLowerInvariant();
    if (!_handlers.TryGetValue(type, out var handler))
        throw new InvalidOperationException($"未知节点类型：{node.Type}（节点 {node.Id}）");
    await handler.OnEnterAsync(new NodeContext { Inst = inst, Schema = schema, Node = node, Token = token, Engine = this });
}
```

- [ ] **Step 4: `FlowEngine.Tokens.cs` 加 `AdvanceToken`**

```csharp
/// <summary>token 排他流转：沿出边取首个条件为真者，改 token.NodeId 后进新节点。不消费。
/// 无后继 → 消费 token + drained 判定（等价旧 NextNodeAsync 兜底结束）。单 token 线性=零差异。</summary>
internal async Task AdvanceToken(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token)
{
    foreach (var edge in schema.Edges.Where(e => e.From == token.NodeId))
    {
        if (!ExpressionEvaluator.Evaluate(edge.Condition, inst.VarsJson)) continue;
        var target = FindNode(schema, edge.To);
        if (target is not null) { token.NodeId = target.Id; await EnterNodeAsync(inst, schema, target, token); return; }
    }
    ConsumeToken(token);
    AddHistory(inst.Id, token.NodeId, inst.StarterId, "end", "无后继节点，自动结束");
    FinishIfDrained(inst);
}
```

- [ ] **Step 5: 建 `StartNodeHandler.cs`**

```csharp
namespace CP6.Core.Services.Wf;

internal sealed class StartNodeHandler : INodeHandler
{
    public string Type => "start";
    public Task OnEnterAsync(NodeContext ctx)
        => ctx.Engine.AdvanceToken(ctx.Inst, ctx.Schema, ctx.Token);   // 沿单边推进，token 不消费
}
```

- [ ] **Step 6: 建 `EndNodeHandler.cs`**

```csharp
namespace CP6.Core.Services.Wf;

internal sealed class EndNodeHandler : INodeHandler
{
    public string Type => "end";
    public Task OnEnterAsync(NodeContext ctx)
    {
        ctx.Engine.ConsumeToken(ctx.Token);                       // 只消费当前 token
        ctx.Engine.AddHistory(ctx.Inst.Id, ctx.Node.Id, ctx.Inst.StarterId, "end", null);
        ctx.Engine.FinishIfDrained(ctx.Inst);                     // 无 Active 残留才整体 Approved
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 7: 建 `ApprovalNodeHandler.cs`（从旧 EnterNodeAsync approval 分支原样搬，唯一增量=task.TokenId）**

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

internal sealed class ApprovalNodeHandler : INodeHandler
{
    public string Type => "approval";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var node = ctx.Node;
        var rule = FlowEngine.BuildRule(node);
        if (rule is null) { eng.Suspend(inst, node, "节点未配置审批人"); return; }

        var res = await eng.Approver.ResolveAsync(rule, new ApproverResolveContext { StarterUserId = inst.StarterId });
        if (!res.Resolved) { eng.Suspend(inst, node, res.UnresolvedReason ?? "审批人无法解析"); return; }

        // 重入节点：作废上一轮遗留任务（避免会签计票串台）
        var stale = eng.Db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id && t.NodeId == node.Id && t.Status != FlowTaskStatus.Cancelled).ToList();
        foreach (var t in stale) t.Status = FlowTaskStatus.Cancelled;

        var dueAt = FlowEngine.NodeDueAt(node);
        foreach (var uid in res.ApproverIds.Distinct())
        {
            var (assignee, delegatedFrom) = await eng.ResolveActualAssigneeAsync(uid);
            var task = new Wf_FlowTask
            {
                Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = node.Id, AssigneeId = assignee,
                Status = FlowTaskStatus.Pending, Countersign = node.Countersign, DueAt = dueAt,
                TokenId = ctx.Token.Id,   // ★ 唯一增量：会签计票按 token 隔离
            };
            eng.Db.Wf_FlowTasks.Add(task);
            if (delegatedFrom is Guid g)
                eng.AddHistory(inst.Id, node.Id, assignee, "delegate", $"代 {g} 审批");
            await eng.Notifier.TodoCreatedAsync(assignee, inst.Id, task.Id, inst.FlowKey);
        }
        // token 停泊（保持 Active，停在本节点等人办理；不流转）
    }
}
```

- [ ] **Step 8: `SubmitAsync` spawn 根 token（`FlowEngine.cs`）**

把 `await EnterNodeAsync(inst, schema, first);` 替换为：
```csharp
var root = SpawnToken(inst, first, parent: null, fork: null);
await EnterNodeAsync(inst, schema, first, root);
```
（`DispatchIfFinishedAsync`/`SaveChanges` 不变。）

- [ ] **Step 9: `ActAsync` token 化（`FlowEngine.cs`）**

三处改动：
1. 会签计票查询加 `TokenId` 维度：
   ```csharp
   var nodeTasks = await _db.Wf_FlowTasks
       .Where(t => t.InstanceId == inst.Id && t.NodeId == task.NodeId
                   && t.TokenId == task.TokenId && t.Status != FlowTaskStatus.Cancelled)
       .ToListAsync();
   ```
2. 通过流转改 token 推进（替换 `NextNodeAsync` 调用块）：
   ```csharp
   if (passed)
   {
       var schema = await LoadSchemaAsync(inst.FlowKey);
       var tok = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == task.TokenId);
       if (tok is not null) await AdvanceToken(inst, schema, tok);
   }
   else
   {
       inst.Status = FlowInstanceStatus.Rejected;
       CancelAllActiveTokens(inst.Id);   // ★ 驳回 = terminate，兄弟分支连坐
   }
   ```

- [ ] **Step 10: DI 注册（`CP6.WebApi/Program.cs`，`AddScoped<IFlowEngine, FlowEngine>` 附近）**

```csharp
builder.Services.AddScoped<INodeHandler, StartNodeHandler>();
builder.Services.AddScoped<INodeHandler, ApprovalNodeHandler>();
builder.Services.AddScoped<INodeHandler, EndNodeHandler>();
// ParallelSplit/Join 在 Task 5 注册
```

- [ ] **Step 11: ★ 兼容硬闸 — 全 Wf 测试照绿**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~Wf"`
Expected: **631 全绿，零既有测试改动**。若任一转红 → token 化引入了行为差异，逐项对照旧 `EnterNodeAsync`/`NextNodeAsync` 排查（常见：approval 建 task 漏 TokenId 导致计票查不到、SubmitAsync 漏 spawn 根 token）。

- [ ] **Step 12: 全量回归**

Run: `dotnet test CP6.Tests`
Expected: 1189 / 1 skip 全绿。

- [ ] **Step 13: Commit**

```bash
git add CP6.Core/Services/Wf/ CP6.WebApi/Program.cs
git commit -m "feat(wfs-A): T4 INodeHandler 多态分发+start/approval/end handler+Submit/Act token 化(631 照绿)"
```

---

## Task 5：并行网关 parallelSplit / parallelJoin

**Files:**
- Create: `CP6.Core/Services/Wf/NodeHandlers/ParallelSplitNodeHandler.cs` / `ParallelJoinNodeHandler.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`（`DefaultHandlers()` 补 split/join）
- Modify: `CP6.WebApi/Program.cs`（注册 split/join）
- Test: `CP6.Tests/Wf/ParallelGatewayTests.cs`

- [ ] **Step 1: 写失败测试（并行 happy path）**

```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class ParallelGatewayTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // start → split → (a:approval, b:approval) → join → end
    private static FlowSchema ForkSchema(Guid ua, Guid ub) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "split", Type = "parallelSplit" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "split" },
            new FlowEdge { From = "split", To = "a" }, new FlowEdge { From = "split", To = "b" },
            new FlowEdge { From = "a", To = "join" }, new FlowEdge { From = "b", To = "join" },
            new FlowEdge { From = "join", To = "end" },
        },
    };

    [Fact]
    public async Task Parallel_BothApprove_Completes()
    {
        using var db = NewDb();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "p", FlowName = "p", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(ForkSchema(ua, ub)), Version = 1, Enable = true });
        await db.SaveChangesAsync();

        await Engine(db).SubmitAsync("p", Guid.NewGuid(), "{}");
        Assert.Equal(2, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));   // 两分支

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);    // join 等待

        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);                                        // 齐了 → 通过
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));        // 无残留
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~Parallel_BothApprove_Completes"`
Expected: FAIL（`parallelSplit` handler 缺失 → `EnterNodeAsync` 抛"未知节点类型"）。

- [ ] **Step 3: 建 `ParallelSplitNodeHandler.cs`**

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

internal sealed class ParallelSplitNodeHandler : INodeHandler
{
    public string Type => "parallelSplit";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var schema = ctx.Schema; var node = ctx.Node;
        eng.ConsumeToken(ctx.Token);                       // 入 token 退场
        var forkId = Guid.NewGuid();
        foreach (var edge in schema.Edges.Where(e => e.From == node.Id))   // 忽略 Condition，全激活
        {
            var target = FlowEngine.FindNode(schema, edge.To);
            if (target is null) continue;
            var child = eng.SpawnToken(inst, target, parent: ctx.Token.Id, fork: forkId);
            await eng.EnterNodeAsync(inst, schema, target, child);
        }
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, "parallelSplit", null);
    }
}
```

- [ ] **Step 4: 建 `ParallelJoinNodeHandler.cs`**

```csharp
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

internal sealed class ParallelJoinNodeHandler : INodeHandler
{
    public string Type => "parallelJoin";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var schema = ctx.Schema; var node = ctx.Node;
        var inEdges = schema.Edges.Count(e => e.To == node.Id);

        // 同 ForkId 已到 join 的 Active token 数（含自身；并查 Local + DB）
        var arrived = AllTokens(eng).Count(t => t.InstanceId == inst.Id && t.NodeId == node.Id
            && t.ForkId == ctx.Token.ForkId && t.Status == FlowTokenStatus.Active);
        if (arrived < inEdges) return;   // 未齐，停泊等其余分支（幂等闸=计数本身）

        // 齐了：消费这批 fork token
        var batch = AllTokens(eng).Where(t => t.InstanceId == inst.Id && t.NodeId == node.Id
            && t.ForkId == ctx.Token.ForkId && t.Status == FlowTokenStatus.Active).ToList();
        foreach (var t in batch) eng.ConsumeToken(t);

        // 血缘上弹一层：取入 token 之父的 父/fork
        var parentTok = ctx.Token.ParentTokenId is Guid pid
            ? AllTokens(eng).FirstOrDefault(t => t.Id == pid) : null;
        var cont = eng.SpawnToken(inst, node, parent: parentTok?.ParentTokenId, fork: parentTok?.ForkId);
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, "parallelJoin", null);
        await eng.AdvanceToken(inst, schema, cont);   // 续 token 沿 join 单出边继续
    }

    private static IEnumerable<Wf_FlowToken> AllTokens(FlowEngine eng)
        => eng.Db.Wf_FlowTokens.Local.Concat(eng.Db.Wf_FlowTokens.AsEnumerable()).Distinct();
}
```
> `FindNode` 已在 Task 4 转 `internal static`。

- [ ] **Step 5: `DefaultHandlers()` 补 split/join + DI 注册**

`FlowEngine.cs` 的 `DefaultHandlers()` 数组补 `new ParallelSplitNodeHandler(), new ParallelJoinNodeHandler()`（即 Task 4 Step 2 的最终五元素形态）。
`Program.cs` 补：
```csharp
builder.Services.AddScoped<INodeHandler, ParallelSplitNodeHandler>();
builder.Services.AddScoped<INodeHandler, ParallelJoinNodeHandler>();
```

- [ ] **Step 6: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~Parallel_BothApprove_Completes"`
Expected: PASS。

- [ ] **Step 7: 补对抗测试（次序无关 / join 等待 / 驳回 terminate / 嵌套）**

在 `ParallelGatewayTests.cs` 追加（每个独立 `[Fact]`）：
- `Parallel_OrderIndependent`：先 b 后 a 与先 a 后 b 结果一致（均 Approved）。
- `Parallel_RejectTerminates`：a 驳回 → inst Rejected + 全 Active token=0（b 分支 token 连坐 Cancelled）+ b 的待办 task 仍在但实例已终（断言 inst.Status==Rejected、`Wf_FlowTokens.Count(Active)==0`）。
- `Parallel_JoinWaits`：仅 a 批 → inst Running、b token 仍 Active。
- `Nested_Fork`：a 分支内再 split（内 join 续 token ForkId 复原外层），最终单实例 Approved。
  ```csharp
  [Fact]
  public async Task Parallel_RejectTerminates()
  {
      using var db = NewDb();
      var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
      db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "p", FlowName = "p", FormKey = "f",
          SchemaJson = JsonSerializer.Serialize(ForkSchema(ua, ub)), Version = 1, Enable = true });
      await db.SaveChangesAsync();
      await Engine(db).SubmitAsync("p", Guid.NewGuid(), "{}");

      var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
      await Engine(db).ActAsync(ta.Id, ua, approve: false, "no");

      var inst = await db.Wf_FlowInstances.SingleAsync();
      Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
      Assert.Equal(0, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
  }
  ```

- [ ] **Step 8: 跑全部并行测试 + Wf 回归**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~ParallelGatewayTests"` 然后 `dotnet test CP6.Tests --filter "FullyQualifiedName~Wf"`
Expected: 并行测试全绿 + 631 仍绿。

- [ ] **Step 9: Commit**

```bash
git add CP6.Core/Services/Wf/NodeHandlers/ CP6.Core/Services/Wf/FlowEngine.cs CP6.WebApi/Program.cs CP6.Tests/Wf/ParallelGatewayTests.cs
git commit -m "feat(wfs-A): T5 并行网关 parallelSplit/Join(ForkId 认亲+血缘上弹+驳回 terminate)"
```

---

## Task 6：并发 / 幂等（RowVersion + ActAsync 重试）

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`（`ActAsync` 拆 `ActOnceAsync` + 重试 + inst 写触达）
- Test: `CP6.Tests/Wf/FlowConcurrencyTests.cs`（SQLite provider，InMemory 不触发并发异常）

- [ ] **Step 1: 写失败测试（SQLite 真并发）**

> InMemory 不抛 `DbUpdateConcurrencyException`，故用 SQLite。先确认 `CP6.Tests` 已引 `Microsoft.EntityFrameworkCore.Sqlite`（既有 `RefreshTokenSqliteTests.cs` 已用 → 复用其连接套路）。

```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CP6.Tests;

public class FlowConcurrencyTests
{
    private static (CP6Context, SqliteConnection) NewSqlite()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    [Fact]
    public async Task Parallel_NearSimultaneous_NoLostWakeup()
    {
        var (db, conn) = NewSqlite();
        using var _ = conn;
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "p", FlowName = "p", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(ParallelGatewayTests_SchemaProxy(ua, ub)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await new FlowEngine(db, new ApproverResolver(db)).SubmitAsync("p", Guid.NewGuid(), "{}");

        // 两个独立 DbContext 模拟近同时办结（各自 ActAsync 走重试路径）
        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);

        await using var dbA = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlite(conn).Options);
        await using var dbB = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlite(conn).Options);
        var t1 = new FlowEngine(dbA, new ApproverResolver(dbA)).ActAsync(ta.Id, ua, approve: true);
        var t2 = new FlowEngine(dbB, new ApproverResolver(dbB)).ActAsync(tb.Id, ub, approve: true);
        await Task.WhenAll(t1, t2);

        var inst = await db.Wf_FlowInstances.AsNoTracking().SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);   // 无丢失唤醒 → 最终通过
    }

    // 复用 ParallelGatewayTests 的 fork schema（避免重复，这里就地构造同形）
    private static FlowSchema ParallelGatewayTests_SchemaProxy(Guid ua, Guid ub) => new()
    {
        Start = "s",
        Nodes = { new FlowNode { Id = "s", Type = "start" }, new FlowNode { Id = "split", Type = "parallelSplit" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" }, new FlowNode { Id = "end", Type = "end" } },
        Edges = { new FlowEdge { From = "s", To = "split" }, new FlowEdge { From = "split", To = "a" },
            new FlowEdge { From = "split", To = "b" }, new FlowEdge { From = "a", To = "join" },
            new FlowEdge { From = "b", To = "join" }, new FlowEdge { From = "join", To = "end" } },
    };
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~Parallel_NearSimultaneous_NoLostWakeup"`
Expected: FAIL 或不稳定（无重试/无 RowVersion 写触达 → 脏读丢失唤醒 → inst 停在 Running）。

- [ ] **Step 3: `ActAsync` 拆 `ActOnceAsync` + 重试循环 + inst 写触达**

`FlowEngine.cs`：把现 `ActAsync` 主体整体改名为 `private async Task ActOnceAsync(...)`，并新增公开 `ActAsync` 外壳：
```csharp
public async Task ActAsync(Guid taskId, Guid actorId, bool approve, string? comment = null)
{
    for (int attempt = 0; ; attempt++)
    {
        try { await ActOnceAsync(taskId, actorId, approve, comment); return; }
        catch (DbUpdateConcurrencyException) when (attempt < 2)
        {
            foreach (var e in _db.ChangeTracker.Entries().ToList()) await e.ReloadAsync();
        }
    }
}
```
在 `ActOnceAsync` 最终 `SaveChangesAsync()` 前，对 inst 做一次写触达（令 RowVersion 参与并发）：
```csharp
inst.ModifyDate = DateTime.Now;   // 触达 inst 行 → UPDATE 带 WHERE RowVersion=@orig
await DispatchIfFinishedAsync(inst, actorId, comment);
await _db.SaveChangesAsync();
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~FlowConcurrencyTests"`
Expected: PASS（败方 `DbUpdateConcurrencyException` → 重读重算 join 计数 → 正确触发）。

- [ ] **Step 5: Wf 回归（重试外壳不改单线程行为）**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~Wf"`
Expected: 631 绿。

- [ ] **Step 6: Commit**

```bash
git add CP6.Core/Services/Wf/FlowEngine.cs CP6.Tests/Wf/FlowConcurrencyTests.cs
git commit -m "feat(wfs-A): T6 乐观并发 RowVersion 写触达+ActOnceAsync 重试×3(防 join 丢失唤醒)"
```

---

## Task 7：退回 / 加签 token 感知（`AdvancedFlow.cs`）

**Files:**
- Modify: `CP6.Core/Services/Wf/AdvancedFlow.cs`（`SendBackAsync`、`AddSignAsync`）
- Test: `CP6.Tests/Wf/AdvancedFlowTests.cs`（既有为兼容闸；新增 token 断言）

- [ ] **Step 1: 写失败测试（退回后单 token 线性恢复）**

在 `AdvancedFlowTests.cs` 追加：
```csharp
[Fact]
public async Task SendBack_CancelsActiveTokens_RebuildsRoot()
{
    using var db = NewDb();   // 复用该测试类已有 NewDb/Engine/Seed
    // 线性流程 n1(approval)→n2(approval)→end，办到 n2 后从 n2 退回 n1
    // …（按本类既有 schema 构造，提交并办结 n1 进 n2）
    // 退回前断言存在 1 个 Active token 停在 n2；退回后断言：旧 token 全 Cancelled、新建 1 个 Active 停在 n1
    // var actives = await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active);
    // Assert.Equal(1, actives);
}
```
> 实现时按 `AdvancedFlowTests` 既有 schema/工具补全（本类已有退回测试，复用其装配，仅加 token 断言）。

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~SendBack_CancelsActiveTokens"`
Expected: FAIL（退回未动 token → Active token 仍停旧节点）。

- [ ] **Step 3: `SendBackAsync` token 感知**

在 `SendBackAsync` 作废全实例在途待办之后、`EnterNodeAsync(target)` 之前，加：
```csharp
CancelAllActiveTokens(inst.Id);                                  // 退回 = 清并行，回单 token 线性
var token = SpawnToken(inst, target, parent: null, fork: null);
await EnterNodeAsync(inst, schema, target, token);               // 注意：签名已带 token（Task 4）
```
（删除原直接 `await EnterNodeAsync(inst, schema, target);` 调用。`P1 约定：退回只在审批节点、不跨并行块`，文档已述。）

- [ ] **Step 4: `AddSignAsync` 继承 TokenId**

加签建任务处，新任务 `addTask.TokenId = task.TokenId;`（继承原任务 token，计入同 token 会签）。

- [ ] **Step 5: 跑测试确认通过 + Wf 回归**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~AdvancedFlow"` 然后 `dotnet test CP6.Tests --filter "FullyQualifiedName~Wf"`
Expected: 新增绿 + 631 绿。

- [ ] **Step 6: Commit**

```bash
git add CP6.Core/Services/Wf/AdvancedFlow.cs CP6.Tests/Wf/AdvancedFlowTests.cs
git commit -m "feat(wfs-A): T7 退回清 Active token 重建根+加签继承 TokenId"
```

---

## Task 8：在途实例 token 回填种子

**Files:**
- Create: `CP6.WebApi/Seed/WfTokenBackfillSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（种子块内、`Migrate()` 后调用）
- Test: `CP6.Tests/Wf/WfTokenBackfillTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class WfTokenBackfillTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Backfill_CreatesRootToken_AndIdempotent()
    {
        using var db = NewDb();
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "f", StarterId = Guid.NewGuid(),
            Status = FlowInstanceStatus.Running, CurrentNode = "n1" };
        db.Wf_FlowInstances.Add(inst);
        db.Wf_FlowTasks.Add(new Wf_FlowTask { Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = "n1",
            AssigneeId = Guid.NewGuid(), Status = FlowTaskStatus.Pending, TokenId = null });
        await db.SaveChangesAsync();

        await WfTokenBackfillSeed.EnsureAsync(db);
        var tok = await db.Wf_FlowTokens.SingleAsync();
        Assert.Equal("n1", tok.NodeId);
        Assert.Equal(FlowTokenStatus.Active, tok.Status);
        Assert.Equal(tok.Id, (await db.Wf_FlowTasks.SingleAsync()).TokenId);   // task 补 TokenId

        await WfTokenBackfillSeed.EnsureAsync(db);                              // 重跑幂等
        Assert.Equal(1, await db.Wf_FlowTokens.CountAsync());                   // 不重复建
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WfTokenBackfillTests"`
Expected: 编译失败（`WfTokenBackfillSeed` 未定义）。

- [ ] **Step 3: 建 `WfTokenBackfillSeed.cs`**

```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>在途实例 token 回填（幂等，守卫"无 token 才建"）。迁移前建的 Running/Suspended 实例补一个根 token。</summary>
public static class WfTokenBackfillSeed
{
    public static async Task EnsureAsync(CP6Context db)
    {
        var insts = await db.Wf_FlowInstances
            .Where(i => i.Status == FlowInstanceStatus.Running || i.Status == FlowInstanceStatus.Suspended)
            .ToListAsync();

        foreach (var inst in insts)
        {
            if (await db.Wf_FlowTokens.AnyAsync(t => t.InstanceId == inst.Id)) continue;   // 守卫：有 token 跳过

            var tok = new Wf_FlowToken
            {
                Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = inst.CurrentNode,
                Status = FlowTokenStatus.Active, ParentTokenId = null, ForkId = null,
                Creator = inst.StarterId.ToString(),
            };
            db.Wf_FlowTokens.Add(tok);

            var tasks = await db.Wf_FlowTasks
                .Where(t => t.InstanceId == inst.Id && t.TokenId == null
                            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended))
                .ToListAsync();
            foreach (var t in tasks) t.TokenId = tok.Id;
        }
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~WfTokenBackfillTests"`
Expected: PASS。

- [ ] **Step 5: `Program.cs` 种子块接线（`Migrate()` 后、`using scope` 内）**

在现有种子块（`db.Database.Migrate()` 之后）追加：
```csharp
await WfTokenBackfillSeed.EnsureAsync(db);   // WFS P1：在途实例 token 回填（每启动幂等）
```

- [ ] **Step 6: 全量回归**

Run: `dotnet test CP6.Tests`
Expected: 1189+新增 / 1 skip 全绿。

- [ ] **Step 7: Commit**

```bash
git add CP6.WebApi/Seed/WfTokenBackfillSeed.cs CP6.WebApi/Program.cs CP6.Tests/Wf/WfTokenBackfillTests.cs
git commit -m "feat(wfs-A): T8 在途实例 token 幂等回填种子"
```

---

# Part 2 — L1 读模型写入钩子

> 钩子嵌进 token 原语 / handler，随引擎推进落库，与 token 变更共享同一 `SaveChanges`。新增 `FlowEngine.ReadModel.cs`（internal 钩子）；由 `ApprovalNodeHandler`（送签）与 `ActAsync`（办结）调用。

## Task 9：`Wf_FlowFormTo` 传签履历写入钩子

**Files:**
- Create: `CP6.Core/Services/Wf/FlowEngine.ReadModel.cs`
- Modify: `CP6.Core/Services/Wf/NodeHandlers/ApprovalNodeHandler.cs`（送签建履历行）
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`（`ActOnceAsync` 办结更新履历 + 驳回连坐作废）
- Test: `CP6.Tests/Wf/ReadModelHookTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class ReadModelHookTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task SeedAsync(CP6Context db, Guid approver)
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "leave", FormKey = "leave",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = { new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                          new FlowNode { Id = "end", Type = "end" } },
                Edges = { new FlowEdge { From = "n1", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FormTo_SendCreatesPending_HandleUpdatesApproved()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        await SeedAsync(db, approver);
        await Engine(db).SubmitAsync("leave", Guid.NewGuid(), """{"days":2}""");

        var pend = await db.Wf_FlowFormTos.SingleAsync();
        Assert.Equal(FlowFormToStatus.Pending, pend.Status);
        Assert.Equal(approver, pend.ExpectedHandlerId);
        Assert.Equal("n1", pend.NodeId);
        Assert.Equal(1, pend.StepSeq);
        Assert.Null(pend.HandledAt);

        var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(task.Id, approver, approve: true, "ok");

        var done = await db.Wf_FlowFormTos.SingleAsync();
        Assert.Equal(FlowFormToStatus.Approved, done.Status);
        Assert.Equal(approver, done.ActualHandlerId);
        Assert.Equal("ok", done.Comment);
        Assert.NotNull(done.HandledAt);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~FormTo_SendCreatesPending"`
Expected: FAIL（`Wf_FlowFormTos` 空，无写入钩子）。

- [ ] **Step 3: 建 `FlowEngine.ReadModel.cs`（钩子方法）**

```csharp
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    /// <summary>本实例下一关卡序号（含并行分支共享序号空间）。</summary>
    internal int NextStepSeq(Guid instanceId)
    {
        var localMax = _db.Wf_FlowFormTos.Local.Where(f => f.InstanceId == instanceId).Select(f => (int?)f.StepSeq).Max() ?? 0;
        var dbMax = _db.Wf_FlowFormTos.Where(f => f.InstanceId == instanceId).Select(f => (int?)f.StepSeq).Max() ?? 0;
        return Math.Max(localMax, dbMax) + 1;
    }

    /// <summary>送签：approval 建待办时，每个应处理人落一行 Pending 履历。</summary>
    internal void WriteFormToOnSend(Wf_FlowInstance inst, FlowNode node, Wf_FlowToken token,
        Guid expectedHandler, Guid? onBehalfOf, int stepSeq)
    {
        _db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, TokenId = token.Id, StepSeq = stepSeq,
            NodeId = node.Id, NodeCode = node.Id, NodeName = node.Name,
            ExpectedHandlerId = expectedHandler, OnBehalfOfId = onBehalfOf,
            Status = FlowFormToStatus.Pending, SentAt = DateTime.Now,
        });
    }

    /// <summary>办结：更新该 (inst,node,token,expectedHandler) 的待签行。</summary>
    internal async Task UpdateFormToOnHandleAsync(Wf_FlowTask task, Guid actorId, bool approve, string? comment)
    {
        var row = await _db.Wf_FlowFormTos
            .Where(f => f.InstanceId == task.InstanceId && f.NodeId == task.NodeId
                        && f.TokenId == task.TokenId && f.ExpectedHandlerId == task.AssigneeId
                        && f.Status == FlowFormToStatus.Pending)
            .FirstOrDefaultAsync();
        if (row is null) return;
        row.Status = approve ? FlowFormToStatus.Approved : FlowFormToStatus.Rejected;
        row.ActualHandlerId = actorId;
        row.HandledAt = DateTime.Now;
        row.Comment = comment;
    }

    /// <summary>驳回连坐 / 退回清场：本实例全 Pending 履历行 → 作废。</summary>
    internal void VoidPendingFormTos(Guid instanceId)
    {
        foreach (var f in _db.Wf_FlowFormTos.Local.Where(f => f.InstanceId == instanceId && f.Status == FlowFormToStatus.Pending))
            f.Status = FlowFormToStatus.Voided;
        foreach (var f in _db.Wf_FlowFormTos.Where(f => f.InstanceId == instanceId && f.Status == FlowFormToStatus.Pending).ToList())
            f.Status = FlowFormToStatus.Voided;
    }
}
```

- [ ] **Step 4: `ApprovalNodeHandler` 送签写履历**

在建 `task` 并 `Add` 之后、`TodoCreatedAsync` 之前插入（每个应处理人一行，StepSeq 取一次）：
```csharp
var step = eng.NextStepSeq(inst.Id);
// …foreach uid 内，建 task 后：
eng.WriteFormToOnSend(inst, node, ctx.Token, expectedHandler: assignee, onBehalfOf: delegatedFrom, stepSeq: step);
```
> 同一节点多会签人共享同一 `step`（同关卡）；不同人各一行。`step` 在 foreach 外取一次。

- [ ] **Step 5: `ActOnceAsync` 办结更新 + 驳回连坐**

在改 `task.Status` 后、流转前，加：
```csharp
await UpdateFormToOnHandleAsync(task, actorId, approve, comment);
```
驳回分支（`inst.Status = Rejected; CancelAllActiveTokens(...)` 处）加：
```csharp
VoidPendingFormTos(inst.Id);
```

- [ ] **Step 6: 跑测试确认通过 + Wf 回归**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~ReadModelHookTests"` 然后 `--filter "FullyQualifiedName~Wf"`
Expected: 新增绿 + 631 绿（读模型为旁路写入，不改审批行为）。

- [ ] **Step 7: 补会签多行 + 驳回作废测试**

追加 `[Fact]`：① 会签节点（`Countersign="all"`，2 审批人）→ 2 行 FormTo 同 StepSeq；② 驳回 → 该行 Rejected、其余 Pending 行 Voided。

- [ ] **Step 8: Commit**

```bash
git add CP6.Core/Services/Wf/FlowEngine.ReadModel.cs CP6.Core/Services/Wf/NodeHandlers/ApprovalNodeHandler.cs CP6.Core/Services/Wf/FlowEngine.cs CP6.Tests/Wf/ReadModelHookTests.cs
git commit -m "feat(wfs-A): T9 FlowFormTo 传签履历写入钩子(送签建行/办结更新/驳回作废)"
```

---

## Task 10：`Wf_FlowData` 每关卡快照写入钩子

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowEngine.ReadModel.cs`（加快照方法）
- Modify: `CP6.Core/Services/Wf/NodeHandlers/ApprovalNodeHandler.cs`（送签存快照）
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`（办结存快照）
- Test: `CP6.Tests/Wf/ReadModelHookTests.cs`（追加）

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task FlowData_SnapshotPerStep()
{
    using var db = NewDb();
    var approver = Guid.NewGuid();
    await SeedAsync(db, approver);
    await Engine(db).SubmitAsync("leave", Guid.NewGuid(), """{"days":2}""");

    var snap = await db.Wf_FlowDatas.SingleAsync();      // 送签存一份
    Assert.Equal("n1", snap.NodeId);
    Assert.Equal(1, snap.StepSeq);
    Assert.Contains("days", snap.DataJson);

    var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
    await Engine(db).ActAsync(task.Id, approver, approve: true);
    Assert.True(await db.Wf_FlowDatas.CountAsync() >= 1);   // 办结再存一份（同 StepSeq 关卡）
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~FlowData_SnapshotPerStep"`
Expected: FAIL（`Wf_FlowDatas` 空）。

- [ ] **Step 3: `FlowEngine.ReadModel.cs` 加快照方法**

```csharp
/// <summary>存一份该关卡当时表单快照（来源 inst.VarsJson）。</summary>
internal void WriteSnapshot(Wf_FlowInstance inst, FlowNode node, Wf_FlowToken token, int stepSeq)
{
    _db.Wf_FlowDatas.Add(new Wf_FlowData
    {
        Id = Guid.NewGuid(), InstanceId = inst.Id, TokenId = token.Id, StepSeq = stepSeq,
        NodeId = node.Id, DataJson = string.IsNullOrWhiteSpace(inst.VarsJson) ? "{}" : inst.VarsJson,
    });
}
```

- [ ] **Step 4: `ApprovalNodeHandler` 送签存快照**

在 Step 4(Task 9) 取 `step` 后，foreach 外加：
```csharp
eng.WriteSnapshot(inst, node, ctx.Token, step);
```

- [ ] **Step 5: `ActOnceAsync` 办结存快照**

在 `UpdateFormToOnHandleAsync` 之后加（取该 task 的 token + 关卡序号）：
```csharp
var doneTok = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == task.TokenId);
if (doneTok is not null)
{
    var seq = await _db.Wf_FlowFormTos.Where(f => f.InstanceId == inst.Id && f.NodeId == task.NodeId && f.TokenId == task.TokenId)
        .Select(f => (int?)f.StepSeq).MaxAsync() ?? NextStepSeq(inst.Id);
    var snapNode = FindNode(await LoadSchemaAsync(inst.FlowKey), task.NodeId);
    if (snapNode is not null) WriteSnapshot(inst, snapNode, doneTok, seq);
}
```

- [ ] **Step 6: 跑测试确认通过 + Wf 回归**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~ReadModelHookTests"` 然后 `--filter "FullyQualifiedName~Wf"`
Expected: 全绿 + 631 绿。

- [ ] **Step 7: Commit**

```bash
git add CP6.Core/Services/Wf/FlowEngine.ReadModel.cs CP6.Core/Services/Wf/NodeHandlers/ApprovalNodeHandler.cs CP6.Core/Services/Wf/FlowEngine.cs CP6.Tests/Wf/ReadModelHookTests.cs
git commit -m "feat(wfs-A): T10 FlowData 每关卡表单快照写入钩子"
```

---

## Task 11：`Wf_FlowCc` 抄送写入钩子（节点/路徑/结束）

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowSchema.cs`（`FlowNode.CcUsers/CcRoles` + `FlowEdge.CcUsers/CcRoles`）
- Modify: `CP6.Core/Services/Wf/FlowEngine.ReadModel.cs`（`WriteCcAsync` 解析 + 写行）
- Modify: `CP6.Core/Services/Wf/NodeHandlers/ApprovalNodeHandler.cs`（进节点写节点抄送）
- Modify: `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`（`AdvanceToken` 经路徑写抄送）
- Modify: `CP6.Core/Services/Wf/NodeHandlers/EndNodeHandler.cs`（结束抄送）
- Test: `CP6.Tests/Wf/ReadModelHookTests.cs`（追加）

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task Cc_NodeRecipients_Written()
{
    using var db = NewDb();
    var approver = Guid.NewGuid(); var ccUser = Guid.NewGuid();
    db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "leave", FormKey = "leave",
        SchemaJson = JsonSerializer.Serialize(new FlowSchema {
            Nodes = { new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver,
                                     CcUsers = new() { ccUser } },
                      new FlowNode { Id = "end", Type = "end" } },
            Edges = { new FlowEdge { From = "n1", To = "end" } } }),
        Version = 1, Enable = true });
    await db.SaveChangesAsync();

    await Engine(db).SubmitAsync("leave", Guid.NewGuid(), "{}");

    var cc = await db.Wf_FlowCcs.SingleAsync();
    Assert.Equal(ccUser, cc.RecipientId);
    Assert.Equal("n1", cc.AtNodeId);
    Assert.False(cc.IsRead);
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~Cc_NodeRecipients_Written"`
Expected: 编译失败（`FlowNode.CcUsers` 未定义）。

- [ ] **Step 3: `FlowSchema.cs` 加抄送字段**

`FlowNode` 加：
```csharp
/// <summary>节点抄送人（进入本节点时抄送，WFS 读模型）。</summary>
public List<Guid>? CcUsers { get; set; }
public int? CcRoleId { get; set; }
```
`FlowEdge` 加：
```csharp
/// <summary>路徑抄送人（token 经此转移时抄送，对齐 Delta 知會人員）。</summary>
public List<Guid>? CcUsers { get; set; }
```

- [ ] **Step 4: `FlowEngine.ReadModel.cs` 加 `WriteCc`**

```csharp
/// <summary>写抄送行（去重：同实例+同人+同节点不重复）。roleId 经 IApproverResolver Role 策略解析。</summary>
internal async Task WriteCcAsync(Wf_FlowInstance inst, string? atNodeId, IEnumerable<Guid>? users, int? roleId)
{
    var ids = new HashSet<Guid>(users ?? Enumerable.Empty<Guid>());
    if (roleId is int rid)
    {
        var res = await _approver.ResolveAsync(new ApproverRule(ApproverStrategy.Role, null, rid, null),
            new ApproverResolveContext { StarterUserId = inst.StarterId });
        foreach (var id in res.ApproverIds) ids.Add(id);
    }
    foreach (var uid in ids)
    {
        bool exists = _db.Wf_FlowCcs.Local.Any(c => c.InstanceId == inst.Id && c.RecipientId == uid && c.AtNodeId == atNodeId)
            || await _db.Wf_FlowCcs.AnyAsync(c => c.InstanceId == inst.Id && c.RecipientId == uid && c.AtNodeId == atNodeId);
        if (exists) continue;
        _db.Wf_FlowCcs.Add(new Wf_FlowCc { Id = Guid.NewGuid(), InstanceId = inst.Id, RecipientId = uid, AtNodeId = atNodeId });
    }
}
```

- [ ] **Step 5: 三处调用**

- `ApprovalNodeHandler.OnEnterAsync` 开头（解析审批人前后均可，置 token 停泊前）：
  ```csharp
  await eng.WriteCcAsync(inst, node.Id, node.CcUsers, node.CcRoleId);
  ```
- `FlowEngine.Tokens.cs` 的 `AdvanceToken` 命中边后、`EnterNodeAsync` 前：
  ```csharp
  if (edge.CcUsers is { Count: > 0 }) await WriteCcAsync(inst, edge.To, edge.CcUsers, null);
  ```
- `EndNodeHandler.OnEnterAsync`：结束抄送（若 end 节点配 CcUsers）：
  ```csharp
  if (ctx.Node.CcUsers is { Count: > 0 })
      await ctx.Engine.WriteCcAsync(ctx.Inst, ctx.Node.Id, ctx.Node.CcUsers, ctx.Node.CcRoleId);
  ```
  （`EndNodeHandler.OnEnterAsync` 改为 `async Task`，在 consume 前 await 抄送。）

- [ ] **Step 6: 跑测试确认通过 + 全量回归**

Run: `dotnet test CP6.Tests --filter "FullyQualifiedName~Cc_NodeRecipients"` 然后 `dotnet test CP6.Tests`
Expected: 新增绿 + 1189+新增 / 1 skip 全绿。

- [ ] **Step 7: Commit**

```bash
git add CP6.Core/Services/Wf/ CP6.Tests/Wf/ReadModelHookTests.cs
git commit -m "feat(wfs-A): T11 FlowCc 抄送写入钩子(节点/路徑知會人員/结束)"
```

---

## Phase A 收尾验收

- [ ] **全量回归 + gstack 真浏览器 QA**

Run: `dotnet test CP6.Tests`
Expected: 1189 + 新增（Token/Parallel/Concurrency/ReadModel/Backfill）全绿 / 1 skip；**631 Wf 既有测试零改动**。

gstack QA（[[feedback_coding_skills]]）：种一个并行审批流程 `SchemaJson`（start→split→2 审批→join→end）+ 种子，真浏览器走待办中心：发起 → 两审批人各自分支待办 → 各同意 → join 收齐 → 实例通过；并检查 `Wf_FlowFormTo` 时间线行（送签/办结/StepSeq）+ `Wf_FlowData` 快照 + 既有线性审批 UI 照常。固化 `docs/superpowers/qa/wfs-form-inbox/phaseA/`。

- [ ] **Commit QA 固化**

```bash
git add docs/superpowers/qa/wfs-form-inbox/
git commit -m "test(wfs-A): Phase A gstack 并行审批 QA 固化"
```

---

## Self-Review（写完本计划的自检结论）

**1. spec 覆盖**：
- 内核 spec §2 数据模型 → T1/T2；§3 INodeHandler → T3/T4；§4 token 原语 → T3/T4；§5 并行网关 → T5；§6 并发幂等 → T6；§7 Submit/Act/退回加签 token 化 → T4/T7；§8 兼容回填 → T1(列)/T8(种子)；§10 测试 → 各 Task TDD + 收尾。
- umbrella §2.2~§2.4 读模型表 → T2；§3 写入钩子（送签/办结/会签多行/路徑 CC/驳回 Voided/快照）→ T9/T10/T11。
- **未纳入（按 Phase 边界正确延后）**：act-as `OnBehalfOf` 主动代理（Phase C；T9 仅捕获被动委派 delegatedFrom）、转交 TransferAsync（Phase C）、forecast（Phase B/C）、身份编码 FunctionId/FlowCode（Phase B 设计器/流程管理）。

**2. 占位扫描**：无 TBD/TODO。唯一"按既有装配补全"处 = T7 Step 1（退回测试复用 `AdvancedFlowTests` 既有 schema 工具）—— 已注明复用既有装配，非空白占位。

**3. 类型一致**：`SpawnToken(inst,node,parent,fork)` / `AdvanceToken(inst,schema,token)` / `ConsumeToken(token)` / `FinishIfDrained(inst)` / `CancelAllActiveTokens(instanceId)` / `EnterNodeAsync(inst,schema,node,token)` 全计划一致；`FlowFormToStatus`/`FlowTokenStatus` 常量名一致；`NextStepSeq`/`WriteFormToOnSend`/`UpdateFormToOnHandleAsync`/`WriteSnapshot`/`WriteCcAsync`/`VoidPendingFormTos` 命名贯穿一致。

**4. 歧义**：StepSeq 同节点会签多行共享、办结快照复用送签 StepSeq —— 已在 T9/T10 显式化。

---

## Execution Handoff

计划已存 `docs/superpowers/plans/2026-06-26-wfs-phaseA-engine-readmodel.md`。两种执行方式：

1. **Subagent-Driven（推荐）** — 每 Task 派新 subagent，Task 间双阶段评审，快迭代（REQUIRED SUB-SKILL: superpowers:subagent-driven-development）。
2. **Inline Execution** — 本会话内逐 Task 执行 + 检查点（REQUIRED SUB-SKILL: superpowers:executing-plans）。

哪种？
