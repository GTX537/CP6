# WF 引擎审批归属校验（P0 越权代批封堵）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在流程引擎四个变更方法（`ActOnceAsync`/`TransferAsync`/`SendBackAsync`/`AddSignAsync`）补归属校验——actor 非任务 AssigneeId（且非有效委派代理、非系统身份）即拒——使「给普通角色放 `oa-inbox:approve` 的那一刻旧栈越权代批复活」的 P0 缺口在引擎层永久闭合。

**Architecture:** 引擎级单一断言助手 `AssertActorMayHandleAsync`（新 partial `FlowEngine.Ownership.cs`），四方法各插一行调用。三条放行路径：本人（actorId==AssigneeId）/ act-as 委派（onBehalfOf==AssigneeId 且引擎侧复验 `Wf_FlowDelegates` 有效授权——防御纵深，不再仅信控制器 `AssertActiveGrant`）/ 系统身份（`Guid.Empty`，超时 worker 硬动作；JWT 登录用户恒非 Empty 不可伪造）。admin 批量转单是唯一可信旁路（`TransferAsync` 新增 `bypassOwnership` 参数，仅 `InboxService.BatchTransferAsync` 传 true——它自带 `AssigneeId==from` 预筛+控制器 `oa-inbox:batch-transfer` admin 闸）。新错误码 **E-WF-029**（028 已占）。

**Tech Stack:** .NET 8 / EF Core（InMemory 单测）/ xUnit。零迁移（不动 schema）；i18n 新键走启动 `SeedLangs` insert-only 自动入库（新键会插，免 SQL 补丁——wfs 波① T10 教训只限「改既有行」）。

## Global Constraints

- 票源：`docs/superpowers/plans/2026-07-07-module-waves-crosscutting.md:121` M-OA/WF 票#1（fable 终审 Important#2）。**此票落地前普通角色授权维持 admin-only**；本票即解锁前置。
- 引擎（CP6.Core）**不做权限概念判断**（不查角色/不豁免 admin）——admin 经旧栈代批他人任务正是本洞，闸后 admin 同样被拒（冒烟以此实证）。
- 委派语义收紧为设计决策：**旧栈直办（onBehalfOf=null 且 actor 系委派代理人）不放行**——代理人必须走新栈 X-Acting-As（act-as）路径，否则履历缺 OnBehalfOfId 审计歧义。E-WF-029 拒之。
- 既有测试若以非 assignee 身份办理而变红：**矫正测试的 actor（让测试模拟真实办理人），严禁弱化闸**。每处矫正在报告中列明归因。
- 错误码词表：`E-WF-029` 入 `CP6.WebApi/Seed/I18nOaInboxScreenSeed.cs`（五语）。复用既有 `E-WF-001`（委派授权无效）不新造。
- 每 commit 立即 push。commit 前缀 `feat(wf):` / `test(wf):`。分支 `feat/wf-actor-ownership`（base=main）。
- 全量绿基线：**2198 绿 / 5 skip**（X-SWEEP 后 main）。

## 漏洞面（开工盘点已实证，实现者免重查）

| 入口 | 路径 | 现状 |
|---|---|---|
| `FlowController.Act` `/api/wf/task/{id}/act` | → `ActAsync`→`ActOnceAsync` | 零归属校验：持 `oa-inbox:approve` 者可批任何人待办 |
| `InboxController.Transfer` `/api/oa/inbox/transfer` | → `TransferAsync` | 只查 toUserId 存在，可抢走任何人任务 |
| `InboxController.SendBack` + `AdvancedFlowController.SendBack` | → `SendBackAsync` | 零归属校验，可退回任何人任务（全清场副作用） |
| `AdvancedFlowController.AddSign` | → `AddSignAsync` | 零归属校验，可在任何人任务上加签（before 加签还会挂起原任务） |

已有闸（勿动）：`InboxService.ActBatch(As)Async`（AssigneeId→E-WF-004）、`BatchTransferAsync`（AssigneeId==from 预筛）、控制器 `AssertActiveGrantAsync`（E-WF-001）、`StartDraftAsync`（StarterId→E-WF-003）。
系统调用方（闸须放行）：`WfTimeoutService.cs:69/74` 以 `SystemActor=Guid.Empty` 调 `ActAsync`（超时自动同意/驳回）。`TimeoutAdvanceErrorEdgeAsync` 不经四方法，不在面内。

---

### Task 1: 引擎归属闸四方法 + E-WF-029（TDD）

**Files:**
- Create: `CP6.Core/Services/Wf/FlowEngine.Ownership.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs:143-151`（ActOnceAsync 插闸）
- Modify: `CP6.Core/Services/Wf/AdvancedFlow.cs`（TransferAsync/AddSignAsync/SendBackAsync 插闸；TransferAsync 加参）
- Modify: `CP6.Core/Services/Wf/IFlowEngine.cs:54`（TransferAsync 签名）
- Modify: `CP6.Core/Services/Wf/IFlowEngine.cs:21`（ActAsAsync 注释「引擎不查委派」失实语同步矫正）
- Modify: `CP6.Core/Services/Oa/InboxService.cs`（BatchTransferAsync 调用点传 `bypassOwnership: true`）
- Modify: `CP6.WebApi/Seed/I18nOaInboxScreenSeed.cs`（E-WF-029 五语行）
- Test: `CP6.Tests/Wf/FlowActorOwnershipTests.cs`（新文件）

**Interfaces:**
- Consumes: `FlowEngine`（partial，`_db` 字段）、`Wf_FlowDelegate`（Enable/GrantorId/DelegateId/ValidFrom/ValidTo）、`WfTimeoutService.SystemActor == Guid.Empty`。
- Produces: `IFlowEngine.TransferAsync(Guid taskId, Guid actorId, Guid toUserId, string? comment = null, bool bypassOwnership = false)`；引擎内部 `Task AssertActorMayHandleAsync(Wf_FlowTask task, Guid actorId, Guid? onBehalfOf = null)`；错误码 `E-WF-029`。

- [ ] **Step 1: 写失败测试（RED）**

新建 `CP6.Tests/Wf/FlowActorOwnershipTests.cs`（沿用 `AdvancedFlowTests` 的 NewDb/Engine/SeedFlow 模式）：

```csharp
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>
/// P0 越权代批封堵（M-OA/WF 票#1）：引擎四变更方法归属闸。
/// 放行三路径=本人 / act-as 有效委派（引擎复验） / SystemActor(Guid.Empty)；
/// 违规=E-WF-029；act-as 无效委派=E-WF-001；批量转单唯一可信旁路 bypassOwnership。
/// </summary>
public class FlowActorOwnershipTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private const string FlowKey = "own-two-step";

    private static async Task SeedFlowAsync(CP6Context db, Guid a, Guid b)
    {
        var schema = new FlowSchema
        {
            Start = "n1",
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = a },
                new FlowNode { Id = "n2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = b },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "n2" }, new FlowEdge { From = "n2", To = "end" } },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = FlowKey, FlowName = "归属闸两段审批", FormKey = "test",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>起流程并取 n1 待办（assignee=a）。</summary>
    private static async Task<Wf_FlowTask> SubmitAndGetTaskAsync(CP6Context db, Guid a, Guid b)
    {
        await SeedFlowAsync(db, a, b);
        await Engine(db).SubmitAsync(FlowKey, Guid.NewGuid(), "{}");
        return await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1" && t.Status == FlowTaskStatus.Pending);
    }

    private static void SeedGrant(CP6Context db, Guid grantor, Guid delegateId, bool enable = true,
        DateTime? from = null, DateTime? to = null)
    {
        db.Wf_FlowDelegates.Add(new Wf_FlowDelegate
        {
            Id = Guid.NewGuid(), GrantorId = grantor, DelegateId = delegateId, Enable = enable,
            ValidFrom = from ?? DateTime.Now.AddDays(-1), ValidTo = to ?? DateTime.Now.AddDays(1),
        });
        db.SaveChanges();
    }

    // ── ActAsync/ActOnceAsync ──

    [Fact]
    public async Task Act_ByNonAssignee_ThrowsE029_AndTaskStaysPending()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var intruder = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).ActAsync(t.Id, intruder, approve: true));
        Assert.Equal("E-WF-029", ex.Message);
        Assert.Equal(FlowTaskStatus.Pending, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).Status);
        Assert.False(await db.Wf_FlowHistories.AnyAsync(h => h.Action == "approve"));   // 零履历污染
    }

    [Fact]
    public async Task Act_ByAssignee_Succeeds()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        await Engine(db).ActAsync(t.Id, a, approve: true);
        Assert.Equal(FlowTaskStatus.Approved, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).Status);
    }

    [Fact]
    public async Task Act_BySystemActor_Bypasses()   // 超时 worker 硬动作路径回归
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        await Engine(db).ActAsync(t.Id, Guid.Empty, approve: true, "超时自动同意");
        Assert.Equal(FlowTaskStatus.Approved, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).Status);
    }

    [Fact]
    public async Task ActAs_DelegateWithActiveGrant_Succeeds()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var me = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        SeedGrant(db, grantor: a, delegateId: me);
        await Engine(db).ActAsAsync(t.Id, me, onBehalfOf: a, approve: true);
        Assert.Equal(FlowTaskStatus.Approved, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).Status);
    }

    [Fact]
    public async Task ActAs_WithoutGrant_ThrowsE001()   // 防御纵深：引擎不再仅信控制器闸
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var me = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).ActAsAsync(t.Id, me, onBehalfOf: a, approve: true));
        Assert.Equal("E-WF-001", ex.Message);
    }

    [Fact]
    public async Task ActAs_ExpiredOrDisabledGrant_ThrowsE001()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var me = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        SeedGrant(db, a, me, to: DateTime.Now.AddDays(-1));            // 已过期
        SeedGrant(db, a, me, enable: false);                            // 已停用
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).ActAsAsync(t.Id, me, onBehalfOf: a, approve: true));
        Assert.Equal("E-WF-001", ex.Message);
    }

    [Fact]
    public async Task ActAs_OnBehalfOfNotAssignee_ThrowsE029()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var me = Guid.NewGuid(); var other = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        SeedGrant(db, other, me);   // me 是 other 的有效代理，但任务属 a
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).ActAsAsync(t.Id, me, onBehalfOf: other, approve: true));
        Assert.Equal("E-WF-029", ex.Message);
    }

    [Fact]
    public async Task Act_DelegateDirect_WithoutActAs_ThrowsE029()   // 设计决策：代理人必须走 act-as，旧栈直办不放行
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var me = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        SeedGrant(db, a, me);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).ActAsync(t.Id, me, approve: true));
        Assert.Equal("E-WF-029", ex.Message);
    }

    // ── TransferAsync ──

    [Fact]
    public async Task Transfer_ByNonAssignee_ThrowsE029()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var intruder = Guid.NewGuid(); var to = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        db.Sys_Users.Add(new Sys_User { Id = to, UserName = "to", UserTrueName = "to", Enable = 1 });
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).TransferAsync(t.Id, intruder, to));
        Assert.Equal("E-WF-029", ex.Message);
        Assert.Equal(a, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).AssigneeId);   // 未被抢走
    }

    [Fact]
    public async Task Transfer_ByAssignee_Succeeds()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var to = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        db.Sys_Users.Add(new Sys_User { Id = to, UserName = "to", UserTrueName = "to", Enable = 1 });
        await db.SaveChangesAsync();
        await Engine(db).TransferAsync(t.Id, a, to);
        Assert.Equal(to, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).AssigneeId);
    }

    [Fact]
    public async Task Transfer_BypassOwnership_AllowsForeignActor()   // admin 批量转单可信旁路回归
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var admin = Guid.NewGuid(); var to = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        db.Sys_Users.Add(new Sys_User { Id = to, UserName = "to", UserTrueName = "to", Enable = 1 });
        await db.SaveChangesAsync();
        await Engine(db).TransferAsync(t.Id, admin, to, comment: null, bypassOwnership: true);
        Assert.Equal(to, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).AssigneeId);
    }

    // ── SendBackAsync / AddSignAsync ──

    [Fact]
    public async Task SendBack_ByNonAssignee_ThrowsE029()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var intruder = Guid.NewGuid();
        await SeedFlowAsync(db, a, b);
        await Engine(db).SubmitAsync(FlowKey, Guid.NewGuid(), "{}");
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Engine(db).ActAsync(t1.Id, a, approve: true);   // 流转到 n2（assignee=b）
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).SendBackAsync(t2.Id, intruder, "n1"));
        Assert.Equal("E-WF-029", ex.Message);
        Assert.Equal(FlowTaskStatus.Pending, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t2.Id)).Status);
    }

    [Fact]
    public async Task AddSign_ByNonAssignee_ThrowsE029()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var intruder = Guid.NewGuid(); var signee = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).AddSignAsync(t.Id, intruder, signee, "after"));
        Assert.Equal("E-WF-029", ex.Message);
        Assert.Equal(FlowTaskStatus.Pending, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == t.Id)).Status);   // before 挂起未发生
    }

    [Fact]
    public async Task AddSign_ByAssignee_Succeeds()
    {
        using var db = NewDb();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var signee = Guid.NewGuid();
        var t = await SubmitAndGetTaskAsync(db, a, b);
        var addId = await Engine(db).AddSignAsync(t.Id, a, signee, "after");
        Assert.Equal(signee, (await db.Wf_FlowTasks.SingleAsync(x => x.Id == addId)).AssigneeId);
    }
}
```

注意：`Sys_User` 的必填字段以实体真实形状为准（编译报缺什么补什么，Enable 可能是 `int` 或 `bool`——照 `TransferAsync` 现有查询 `u.Enable` 用法与既有测试用例改）。`Wf_FlowHistories` DbSet 名同理（照 `AddHistory` 实现）。

- [ ] **Step 2: 跑新测试确认 RED**

```
dotnet build CP6.Tests/CP6.Tests.csproj -m:1 --nologo -v q
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo --filter "FullyQualifiedName~FlowActorOwnershipTests"
```
预期：`Transfer_BypassOwnership_*` 编译失败（无该参数）→ 先注释该测试跑其余：负面用例（ThrowsE029/E001）全 FAIL（现状引擎不抛），正面用例（ByAssignee/SystemActor）PASS。记录 RED 证据。

- [ ] **Step 3: 实现归属闸**

新建 `CP6.Core/Services/Wf/FlowEngine.Ownership.cs`：

```csharp
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 引擎归属闸（P0 越权代批封堵，M-OA/WF 票#1）：四变更方法（ActOnce/Transfer/SendBack/AddSign）
/// 统一断言 actor 有权处置该任务。放行三路径：
///   ① 本人：actorId == task.AssigneeId；
///   ② act-as 委派：onBehalfOf == task.AssigneeId 且 Wf_FlowDelegates 存在有效授权
///      （Enable && ValidFrom<=now<=ValidTo，谓词与 DelegateService.Active() 同款）——引擎侧复验，
///      不再仅信控制器 AssertActiveGrant（防御纵深：未来新调用方绕过控制器闸也拦得住）；
///   ③ 系统身份：actorId == SystemActor(Guid.Empty)——超时 worker 硬动作（WfTimeoutService）。
///      JWT 登录用户 UserId 恒非 Empty，该路径不可从 HTTP 面伪造。
/// 违规抛 E-WF-029（非本人待办）；act-as 无效委派抛 E-WF-001（复用既有码）。
/// 设计决策：委派代理人旧栈直办（onBehalfOf=null）不放行——必须走 act-as，否则履历缺
/// OnBehalfOfId 审计歧义（拒 E-WF-029）。admin 亦不豁免（引擎无权限概念；批量转单走
/// TransferAsync bypassOwnership 显式可信旁路）。
/// </summary>
public partial class FlowEngine
{
    /// <summary>系统身份（超时 worker 等引擎内部硬动作）。与 WfTimeoutService.SystemActor 同值。</summary>
    internal static readonly Guid SystemActor = Guid.Empty;

    private async Task AssertActorMayHandleAsync(Wf_FlowTask task, Guid actorId, Guid? onBehalfOf = null)
    {
        if (actorId == SystemActor) return;                               // ③ 系统硬动作
        var owner = onBehalfOf ?? actorId;
        if (owner != task.AssigneeId) throw new InvalidOperationException("E-WF-029");
        if (owner == actorId) return;                                     // ① 本人
        var granted = await _db.Wf_FlowDelegates.AnyAsync(d => d.Enable   // ② act-as 复验
            && d.GrantorId == owner && d.DelegateId == actorId
            && d.ValidFrom <= DateTime.Now && d.ValidTo >= DateTime.Now);
        if (!granted) throw new InvalidOperationException("E-WF-001");
    }
}
```

`FlowEngine.cs` ActOnceAsync 插闸（幂等静默返回之后、首次状态突变之前）：

```csharp
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId);
        if (inst is null || inst.Status != FlowInstanceStatus.Running) return;   // 实例已结束/挂起

        await AssertActorMayHandleAsync(task, actorId, onBehalfOf);   // ★ 归属闸（P0 票#1）：非本人/非委派/非系统 → E-WF-029

        task.Status = approve ? FlowTaskStatus.Approved : FlowTaskStatus.Rejected;
```

`AdvancedFlow.cs` 三处（各在 task+inst 校验之后、任何状态突变之前插一行）：

TransferAsync——签名加参并插闸：
```csharp
    public async Task TransferAsync(Guid taskId, Guid actorId, Guid toUserId, string? comment = null,
        bool bypassOwnership = false)
    {
        var task = await _db.Wf_FlowTasks.FirstOrDefaultAsync(t => t.Id == taskId)
                   ?? throw new InvalidOperationException("E-WF-002");
        if (task.Status != FlowTaskStatus.Pending) throw new InvalidOperationException("E-WF-002");

        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId);
        if (inst is null || inst.Status != FlowInstanceStatus.Running) throw new InvalidOperationException("E-WF-002");

        // ★ 归属闸（P0 票#1）。bypassOwnership 唯一可信调用方=InboxService.BatchTransferAsync
        //（admin 批量转单：控制器 oa-inbox:batch-transfer 闸 + AssigneeId==from 预筛已把关）。
        if (!bypassOwnership) await AssertActorMayHandleAsync(task, actorId);
```

AddSignAsync——`if (inst is null || inst.Status != FlowInstanceStatus.Running) throw ...("流程已结束，不能加签");` 之后插：
```csharp
        await AssertActorMayHandleAsync(task, actorId);   // ★ 归属闸（P0 票#1）：只有本任务处理人可发起加签
```

SendBackAsync（SendBackTarget 重载，node/prevstage/starter 三路共同入口）——`if (inst is null || inst.Status != FlowInstanceStatus.Running) return;` 之后插：
```csharp
        await AssertActorMayHandleAsync(task, actorId);   // ★ 归属闸（P0 票#1）：只有本任务处理人可退回
```

`IFlowEngine.cs:54` 同步签名：
```csharp
    Task TransferAsync(Guid taskId, Guid actorId, Guid toUserId, string? comment = null, bool bypassOwnership = false);
```
`IFlowEngine.cs:21` 注释失实语矫正：「授权由控制器 AssertActiveGrant 把关，引擎不查委派」→「控制器 AssertActiveGrant 先闸，引擎 AssertActorMayHandleAsync 复验（防御纵深）」。

`InboxService.BatchTransferAsync` 调用点：
```csharp
                await _engine.TransferAsync(taskId, actorId, toUserId, comment, bypassOwnership: true);
```

`I18nOaInboxScreenSeed.cs` 在 E-WF-004 行后加：
```csharp
        new Sys_Lang { LangKey = "E-WF-029", ZhCN = "非本人待办，无权办理", ZhTW = "非本人待辦，無權辦理", En = "You are not the assignee of this task", Ja = "本人の未処理タスクではないため操作できません", Ko = "본인의 대기 작업이 아니므로 처리할 수 없습니다" },
```
（字段名/形状照该文件既有行原样；若 Seed 类形状不同以文件为准。）

- [ ] **Step 4: 跑新测试确认 GREEN**

```
dotnet build CP6.Tests/CP6.Tests.csproj -m:1 --nologo -v q
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo --filter "FullyQualifiedName~FlowActorOwnershipTests"
```
预期：全部 PASS（解除 Step 2 的注释）。

- [ ] **Step 5: 全量回归 + 既有测试 actor 矫正**

```
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo
```
预期红点全部落在「测试以非 assignee 身份办理」的既有用例（含 `FlowConcurrencyTests`、`AdvancedFlowTests`、子流程/剪枝/超时等波次测试）。**逐个矫正 actor 为该任务 assignee（或 SystemActor 若模拟系统路径），严禁改闸**。每处矫正记录在报告（文件:行 + 原 actor→新 actor + 归因「测试建模瑕疵非产品路径」）。若发现某红点实为**产品内部调用方以非 assignee 过闸**（除已知 WfTimeoutService/BatchTransfer 外），BLOCKED 报回附证据勿自行放水。
终态：全量 **≥2198+15 绿 / 5 skip / 0 fail**。

- [ ] **Step 6: Commit + push**

```
git add -A
git commit -m "feat(wf): 引擎归属闸四方法——非本人/非委派/非系统即 E-WF-029, 越权代批引擎层永久闭合(M-OA/WF票#1)"
git push
```

---

### Task 2: 部署上线 + 冒烟实证

**Files:** 无代码改动（部署+验证任务）。产物=冒烟记录入台账。

**Interfaces:**
- Consumes: Task 1 合并后的 main；既有部署降级路线（[new-env-setup-2026-07] 记忆）。
- Produces: 线上 E-WF-029 实证 + 超时 worker 健康证据。

- [ ] **Step 1: 重建 cp6-api 镜像并部署**

```
dotnet publish CP6.WebApi/CP6.WebApi.csproj -c Release -o publish-docker
# 删 publish-docker 里 appsettings.Local.json / appsettings.Development.json（否则遮蔽 docker env）
docker build -t cp6-cp6-api:latest ./publish-docker
docker compose up -d cp6-api
```

- [ ] **Step 2: 词表就位验证**

启动日志无 seed 报错后，SQL 实证：`SELECT LangKey FROM Sys_Langs WHERE LangKey='E-WF-029'`（四租户库/单库按现网拓扑）→ 行存在。

- [ ] **Step 3: 冒烟——闸生效实证（admin 也被拒）**

1. admin 登录 A1，起一条测试流程，审批人指定**非 admin 用户**（A1 租户若无第二用户，用 approver-map/Specified 指向任意真实非 admin UserId；无则起两条流程，一条审批人=admin）。
2. admin 对「审批人≠admin」的待办调 `POST /api/wf/task/{id}/act` → 预期 **400 E-WF-029**（闸生效铁证——部署前同请求会 200）。
3. admin 对「审批人=admin」的待办同请求 → 预期 200（本人路径无回归）。
4. `docker logs cp6-api` 查超时 worker 无新异常（SystemActor 路径健康）。
5. 测试流程数据清理（驳回/作废测试实例）。

- [ ] **Step 4: 台账收口**

progress.md 记 T2 冒烟证据；MEMORY.md 交接点更新：「OA/WF 引擎审批归属校验票✅落地——普通角色授权解锁」。commit+push。

---

## 完成后跟踪票（plan 文末记录，不在本波做）

1. 普通角色授权放开波（本票解锁的后续）：给非 admin 角色配 `oa-inbox:*` 键的种子/页面策略——独立立项。
2. QA harness 若有 admin 代批他人任务的剧本，随下次 live QA 矫正为 assignee 本人/act-as。
3. 【终审 Minor#2】`FlowEngine.SystemActor` 与 `WfTimeoutService.SystemActor` 双 `Guid.Empty` 常量——下次触碰任一文件时合并为单一引用（漂移风险纯理论，注释已互指）。
4. 【任务审 Minor#2】`Wf_FlowDelegate.Scope` 字段全平台不参与判定（控制器 `Active()`/引擎复验同样忽略）——scope 语义启用时须两处同步落地。
5. 【终审观察记档】ActOnce/SendBack 幂等静默返回先于闸：非本人探测已办结任务得静默 200 而非 E-WF-029（零突变、不泄露归属，Transfer 同探测则 E-WF-002——已知不对称形态，非缺陷）。

## Self-Review 记录

- 票面四方法全覆盖（ActOnce/Transfer/SendBack/AddSign）；SendBack 三路（node/prevstage/starter）共走 SendBackTarget 入口单点插闸；SendBackAsync(string) 旧重载转发新重载，天然同闸。
- 系统调用方两处已核（WfTimeoutService.ActAsync=SystemActor 放行；TimeoutAdvanceErrorEdgeAsync 不经四方法）。BatchTransferAsync 旁路显式化。ActBatch(As)Async 走 ActAsync/ActAsAsync——其服务层已有 AssigneeId 预筛，引擎闸系冗余防御，语义一致不红。
- 类型一致性：AssertActorMayHandleAsync 签名在 Task 1 内定义与四处调用一致；TransferAsync 新签名接口/实现/InboxService/测试四处同步。
- 无 placeholder；测试代码完整可编译（Sys_User/DbSet 形状按实体微调已注记）。
