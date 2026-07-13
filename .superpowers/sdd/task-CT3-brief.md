### Task C-T3: SendBackToNodeAsync 三规则接线 + E-WF-019 + 集成矩阵

> 依赖 C-T1/C-T2（若与 H-B 并行，注意 `FlowEngine.Tokens.cs` 的 `CancelPendingTasksOfToken` 合并冲突——签名一致，取一份即可）。

**Files:**
- Modify: `CP6.Core/Services/Wf/AdvancedFlow.cs`（`SendBackToNodeAsync` :124-143 重写；删 `CrossesParallelBlock` :196-202 与 `NodesBetween` :204-210）
- Test: `CP6.Tests/Wf/SendBackThreeRuleTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/SendBackThreeRuleTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>退回三规则 × 网关类型 × 三目标矩阵（hardening spec §5.2/§9）。
/// 线性流零 diff 由既有 AdvancedFlowTests/SerialSendBackTests 锁定（断言零改），本文件只测网关场景。</summary>
public class SendBackThreeRuleTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // s → n0 → split → ( a1 → a2 , b1 ) → join → end
    private static FlowSchema P(Guid u0, Guid ua1, Guid ua2, Guid ub) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "n0", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u0 },
            new FlowNode { Id = "split", Type = "parallelSplit" },
            new FlowNode { Id = "a1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua1 },
            new FlowNode { Id = "a2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua2 },
            new FlowNode { Id = "b1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "n0" }, new FlowEdge { From = "n0", To = "split" },
            new FlowEdge { From = "split", To = "a1" }, new FlowEdge { From = "a1", To = "a2" },
            new FlowEdge { From = "split", To = "b1" },
            new FlowEdge { From = "a2", To = "join" }, new FlowEdge { From = "b1", To = "join" },
            new FlowEdge { From = "join", To = "end" },
        },
    };

    private static async Task<(Guid instId, Guid u0, Guid ua1, Guid ua2, Guid ub)> SetupAtA2(CP6Context db)
    {
        Guid u0 = Guid.NewGuid(), ua1 = Guid.NewGuid(), ua2 = Guid.NewGuid(), ub = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "tr", FlowName = "tr", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(P(u0, ua1, ua2, ub)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("tr", Guid.NewGuid(), "{}");
        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u0 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t0.Id, u0, approve: true);              // 进 split → a1/b1
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t1.Id, ua1, approve: true);             // a 支推进到 a2
        return (instId, u0, ua1, ua2, ub);
    }

    [Fact]
    public async Task SameBranch_StripsOwnBranchOnly_SiblingSurvives_JoinStillRecognizesKin()
    {
        using var db = NewDb();
        var (instId, _, ua1, ua2, ub) = await SetupAtA2(db);
        var branchFork = (await db.Wf_FlowTokens.SingleAsync(t => t.NodeId == "a2" && t.Status == FlowTokenStatus.Active)).ForkId;

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("node", "a1"), "分支内退回");

        // ★ 兄弟支 b1 不倒
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "b1" && t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        // ★ 重生 token 在 a1、携剥离层血缘（同 ForkId，外层 join 认亲不破坏）
        var reborn = await db.Wf_FlowTokens.SingleAsync(t => t.NodeId == "a1" && t.Status == FlowTokenStatus.Active);
        Assert.Equal(branchFork, reborn.ForkId);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "sendback"));

        // 重走 a 支到底 + b 支过 → join 齐批 → Approved（认亲回归）
        var r1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(r1.Id, ua1, approve: true);
        var r2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(r2.Id, ua2, approve: true);
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task BeforeSplit_FullClear_SingleRootRebornAtTarget()
    {
        using var db = NewDb();
        var (instId, u0, _, ua2, ub) = await SetupAtA2(db);

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("node", "n0"), "整块重来");

        // 现行全清场语义：全部旧 token Cancelled、b 支任务 Cancelled、单根重生在 n0（血缘归零）
        var actives = await db.Wf_FlowTokens.Where(t => t.InstanceId == instId && t.Status == FlowTokenStatus.Active).ToListAsync();
        Assert.Single(actives);
        Assert.Equal("n0", actives[0].NodeId);
        Assert.Null(actives[0].ParentTokenId);
        Assert.Null(actives[0].ForkId);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowFormTos.AnyAsync(f => f.NodeId == "b1" && f.Status == FlowFormToStatus.Pending));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == u0 && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task SiblingBranch_Throws_E_WF_019_NothingMutated()
    {
        using var db = NewDb();
        var (_, _, _, ua2, ub) = await SetupAtA2(db);

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("node", "b1")));
        Assert.Contains("E-WF-019", ex.Message);

        // 先校验后写：拒绝发生在任何状态突变之前
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.Id == t2.Id && t.Status == FlowTaskStatus.Pending));
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "a2" && t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "b1" && t.Status == FlowTokenStatus.Active));
        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.Action == "sendback"));
    }

    [Fact]
    public async Task Starter_FromParallelBranch_FullClear_BackToDraft()
    {
        using var db = NewDb();
        var (instId, _, _, ua2, _) = await SetupAtA2(db);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("starter"), "退回重填");

        // starter 天然 BeforeSplit → 现行为不变：全清场 + 回草稿
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Draft, inst.Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.Status == FlowTaskStatus.Pending));
    }

    // inclusive 网关 SameBranch/Sibling（网关类型矩阵另一半）
    private static FlowSchema Inc(Guid ua1, Guid ua2, Guid ub, Guid ud) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
            new FlowNode { Id = "a1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua1 },
            new FlowNode { Id = "a2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua2 },
            new FlowNode { Id = "b1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
            new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "isplit" },
            new FlowEdge { From = "isplit", To = "a1", Condition = "goA > 0" },
            new FlowEdge { From = "a1", To = "a2" },
            new FlowEdge { From = "isplit", To = "b1", Condition = "goB > 0" },
            new FlowEdge { From = "isplit", To = "d" },
            new FlowEdge { From = "a2", To = "ijoin" }, new FlowEdge { From = "b1", To = "ijoin" },
            new FlowEdge { From = "d", To = "ijoin" },
            new FlowEdge { From = "ijoin", To = "end" },
        },
    };

    [Fact]
    public async Task Inclusive_SameBranch_SiblingSurvives_And_SiblingTarget_E_WF_019()
    {
        using var db = NewDb();
        Guid ua1 = Guid.NewGuid(), ua2 = Guid.NewGuid(), ub = Guid.NewGuid(), ud = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "itr", FlowName = "x", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(Inc(ua1, ua2, ub, ud)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("itr", Guid.NewGuid(), "{\"goA\":1,\"goB\":1}");   // a/b 两支激活

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t1.Id, ua1, approve: true);              // a 支到 a2

        // 兄弟支目标 → E-WF-019
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("node", "b1")));
        Assert.Contains("E-WF-019", ex.Message);

        // 同支退回 → 兄弟 b1 不倒、重走后齐批
        await Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("node", "a1"), "同支退");
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));

        var r1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(r1.Id, ua1, approve: true);
        var r2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(r2.Id, ua2, approve: true);
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task Nested_SameBranch_OuterStripLayer_InnerSiblingsKilled_OuterSiblingSurvives()
    {
        // s → outer → ( h1 → inner → (x1,x2) → ij , b ) → oj → end；x1 上退回 h1 → 剥离外层支（含 x2）、b 不倒
        using var db = NewDb();
        Guid uh = Guid.NewGuid(), u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "outer", Type = "parallelSplit" },
                new FlowNode { Id = "h1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = uh },
                new FlowNode { Id = "inner", Type = "parallelSplit" },
                new FlowNode { Id = "x1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
                new FlowNode { Id = "x2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u2 },
                new FlowNode { Id = "ij", Type = "parallelJoin" },
                new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
                new FlowNode { Id = "oj", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "outer" },
                new FlowEdge { From = "outer", To = "h1" }, new FlowEdge { From = "h1", To = "inner" },
                new FlowEdge { From = "inner", To = "x1" }, new FlowEdge { From = "inner", To = "x2" },
                new FlowEdge { From = "x1", To = "ij" }, new FlowEdge { From = "x2", To = "ij" },
                new FlowEdge { From = "ij", To = "oj" },
                new FlowEdge { From = "outer", To = "b" }, new FlowEdge { From = "b", To = "oj" },
                new FlowEdge { From = "oj", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "nsb", FlowName = "x", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("nsb", Guid.NewGuid(), "{}");

        var th = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == uh && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(th.Id, uh, approve: true);               // h1 过 → 进 inner → x1/x2
        var outerFork = (await db.Wf_FlowTokens.SingleAsync(t => t.NodeId == "inner")).ForkId;

        var tx1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).SendBackAsync(tx1.Id, u1, new SendBackTarget("node", "h1"), "退到内层 split 之前");

        // 内层兄弟 x2 连带剥离；外层兄弟 b 不倒；重生在 h1 携外层血缘
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t =>
            (t.NodeId == "x1" || t.NodeId == "x2") && t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        var reborn = await db.Wf_FlowTokens.SingleAsync(t => t.NodeId == "h1" && t.Status == FlowTokenStatus.Active);
        Assert.Equal(outerFork, reborn.ForkId);

        // 重走全程 → Approved（外层 join 认亲齐批）
        var rh = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == uh && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(rh.Id, uh, approve: true);
        var rx1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(rx1.Id, u1, approve: true);
        var rx2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(rx2.Id, u2, approve: true);
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter SendBackThreeRuleTests`：SameBranch/BeforeSplit 场景现状抛 E-WF-012（CrossesParallelBlock 禁令）→ 失败；Sibling 场景现状也抛 E-WF-012 而非 019 → 失败。

- [ ] **Step 3: 实现** — `AdvancedFlow.cs` 的 `SendBackToNodeAsync` 整体替换为（并删除 `CrossesParallelBlock`/`NodesBetween` 两个私有方法）：

```csharp
    private async Task SendBackToNodeAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowTask task,
        Guid actorId, string? targetNodeId, string? comment)
    {
        var target = FindNode(schema, targetNodeId ?? "") ?? throw new InvalidOperationException("E-WF-012");
        if (IsType(target, "end") || target.Id == task.NodeId) throw new InvalidOperationException("E-WF-012");
        var tt = (target.Type ?? "approval").Trim().ToLowerInvariant();
        if (tt != "approval" && tt != "start") throw new InvalidOperationException("E-WF-012");
        if (!IsUpstreamReachable(schema, target.Id, task.NodeId)) throw new InvalidOperationException("E-WF-012");

        // hardening C-T3 三规则（spec §5）：作用域判定取代旧 CrossesParallelBlock 一刀切禁令。
        // 先校验后写：SiblingBranch 拒绝发生在任何状态突变之前。旧数据无 token 维度 → 现行为全清场。
        var all = SnapshotTokens(inst.Id);
        var curTok = task.TokenId is Guid tid ? all.FirstOrDefault(t => t.Id == tid) : null;
        var (scope, strip) = curTok is null
            ? (SendBackScope.BeforeSplit, (Wf_FlowToken?)null)
            : SendBackScopeAnalyzer.Analyze(schema, all, curTok, task.NodeId, target.Id);
        if (scope == SendBackScope.SiblingBranch) throw new InvalidOperationException("E-WF-019");

        if (scope == SendBackScope.SameBranch && strip is not null)
        {
            // 分支内剥离：只清剥离层子树（含内层 fork 兄弟），外层兄弟分支零扰动；
            // 重生 token 接管剥离层血缘 → 外层 join 认亲不破坏（spec §5.2 ★）
            CancelTokenSubtree(inst.Id, strip.Id);
            AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? $"退回至 {target.Id}");
            var reborn = SpawnToken(inst, target, parent: strip.ParentTokenId, fork: strip.ForkId);
            await EnterNodeAsync(inst, schema, target, reborn);
            return;
        }

        // BeforeSplit（含线性流 fork 栈为空）：既有全清场路径，逐字保留
        var live = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended))
            .ToListAsync();
        foreach (var t in live) t.Status = FlowTaskStatus.Cancelled;
        AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? $"退回至 {target.Id}");
        CancelAllActiveTokens(inst.Id);
        VoidPendingFormTos(inst.Id);
        var sbToken = SpawnToken(inst, target, parent: null, fork: null);
        await EnterNodeAsync(inst, schema, target, sbToken);
    }
```

（`SendBackToPrevStageAsync` / `SendBackToStarterAsync` **零改**：prevStage 天然同支且现行为已是收窄清场；starter 天然 BeforeSplit。）

- [ ] **Step 4: 跑验证 PASS** — `--filter SendBackThreeRuleTests` 全绿。
- [ ] **Step 5: 线性零 diff 铁闸 + commit** — `--filter AdvancedFlowTests` + `--filter SerialSendBackTests` 断言零改全绿（线性流 fork 栈为空 → BeforeSplit → 走原全清场代码块）。

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-hardening): C-T3 退回三规则接线(SameBranch剥离/BeforeSplit全清场/E-WF-019)+网关矩阵测试"
```

---

## 落码纪律 / Global Constraints（每个 Task 都遵守）

- **基线锁定**：后端 `dotnet test CP6.Tests/CP6.Tests.csproj` = **1509 通过（5 skip=SQLite 既知）** → 本波只增不减、全绿；前端 `npm run test`（vitest）= **320 全绿** → +N 全绿；`npm run type-check`（package.json 既有命令，含大堆内存参数）+ `npm run build` 全过。
- **EF clean（本波零迁移）**：只改 SchemaJson POCO（`FlowNode.OnBranchReject`）+ 常量（`FlowTokenStatus.Pruned`），**不加实体列、不生成迁移**。每波末跑 `dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 必须 clean。
- **27 个既有 Wf 不变量测试一个断言不许改**（`CP6.Tests/Wf/**` 既有文件只增不改；`ParallelGatewayTests` 4+1 个并行语义测试是动态计票的等价性铁闸）。唯一例外见 D-T1：前端 `designerModel.test.ts:45` 的 palette 类型清单断言随 palette 扩展同步更新（前端测试不在 27 不变量范围，且该断言就是「palette 全集清单」本身）。
- **默认路径行为与现状全等**：无 inclusive 节点 + 无 onBranchReject 配置的流程，token 状态序列 / 任务 / FormTo / 通知 bit 级等价（cascade 一行不改、动态计票旧场景回归锁定、线性退回走既有全清场分支逐字保留）。
- **引擎内写路径三律（黄金模板铁律）**：① 先校验后写（一切结构化拒绝在任何状态突变之前抛出）；② 幂等（join 计数重入安全、已办任务再办 no-op）；③ handler/引擎内部方法**绝不自行 SaveChanges**（统一由 ActAsync/SendBackAsync 等外壳收口，剪枝/退回全部改动随既有 SaveChanges 落库）。
- **E 波紧跟 D 波不留窗口**：D-T2 合入后立即执行 E-T1/E-T2，不允许「有 UI 无 i18n/无 QA」的中间态过夜。
- **零跨模块污染**：不碰 `cp6.web/src/views/space/**`、`Services/*Space*`、Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核。
- **零硬编码色**：前端新增视觉全部走 Design System token（`var(--cp-warn)` 家族等），沿 `DesignerCanvas.vue` `.dot-*` / `GatewayNode.vue` 既有 token 用法。
- **五语 i18n**：ja / zh-CN / zh-TW / en / ko，新 UI 文案全 `t()` 运行时键，键值入 `I18nOa*ScreenSeed` 家族新 seed。
- **隔离 worktree**：建议 `git worktree add C:/CP6-wfs-hardening -b feat/wfs-kernel-hardening main`（off `fb90d75`），不污染 `C:\CP6` 工作区。
- **subagent-driven TDD**：每 Task 全新编码子代理（模型按 model-policy：Opus 4.8）→ 主代理 `git show` diff 复核 → 本地 commit **不 push**。节奏：先写失败测试 → 跑验证 FAIL → 最小实现 → 跑验证 PASS → commit。提交信息 `feat(wfs-hardening): <Task 号> 中文摘要`。

---

## 侦察结论（spec §5.3 各核实项，已实读代码定案 —— 执行者照此实现，不再二次侦察）

| 核实项 | 结论 |
|---|---|
| §4.1 **split 节点定位机制** | `Wf_FlowToken`（`CP6.Entity/DomainModels/Wf/Wf_FlowToken.cs`）**没有** split nodeId 列，只有 `ParentTokenId`/`ForkId`/`StagePlanJson`。选定 **ParentTokenId 上溯**：分叉时 `ParallelSplitNodeHandler.cs:21` 以 `parent: ctx.Token.Id` 生子 token，而 ctx.Token 在被消费前 NodeId 已被 `AdvanceToken`（`FlowEngine.Tokens.cs:102`）置为 split 节点 id、`ConsumeToken` 不改 NodeId ⇒ **`ForkParent(t)`（Id==t.ParentTokenId 的 token）.NodeId 恒等于生成 t.ForkId 批次的 split 节点 id**。join 续 token 的「上弹一层」血缘（`ParallelJoinNodeHandler.cs:25-27`）保证该不变量对每层 fork 成立。零迁移。 |
| §5.3 **SendBackToNodeAsync 重生血缘现状** | `AdvancedFlow.cs:141` — `SpawnToken(inst, target, parent: null, fork: null)`，**血缘归零**（根 token）。SameBranch 规则必须改为携带剥离层血缘 `(parent: strip.ParentTokenId, fork: strip.ForkId)`；BeforeSplit/线性流保留归零重生（现状逐字）。 |
| §5.3 **CancelAllActiveTokens 过滤重载** | `FlowEngine.Tokens.cs:40` 现为全实例清场（含 B-T3 的 Pending Wf_ServiceJob 清场）、无过滤参数。不改它；**新增 `CancelTokenSubtree(Guid instanceId, Guid rootTokenId)`**（C-T2）：按 ParentTokenId 闭包算子树，子树内 Active token→Cancelled、Pending/Suspended 任务→Cancelled、Pending FormTo→Voided、Pending ServiceJob→Cancelled（镜像既有 Local ∪ DB localIds-exclusion 惯用法）。**子树闭包正确性论证**：join 续 token 血缘「上弹一层」（parent=祖父），故任何仍在途的分支延续 token 会重新挂在剥离层同级 —— 作用域分析（C-T1）从 current token 血缘出发选剥离层时选到的正是该延续 token 本身，ParentTokenId 后代闭包捕获全部需清场的活 token（C-T3 嵌套测试锁定）。 |
| §5.3 **StageRound 递增与局部退回相容性** | `NextStageRound`（`FlowEngine.ReadModel.cs:53`）按 `(instanceId, nodeId, tokenId, stageIndex)` 键控取 Max+1。SameBranch 重生的是**新 tokenId** ⇒ 新 token 串簽轮次从 0 起，与现状全清场重生同构，**天然相容，零改**。prevStage 退回不换 token（`AdvancedFlow.cs:161-163`），轮次 +1 语义不受本波影响。 |
| §5.3 **剥离层判定与 fork 栈共用血缘辅助** | 新 `TokenLineage` 静态类（A-T1）：`AncestorChain` / `CrossesFork` / `ForkParent` / `ForkStack`。剪枝递归上弹（B-T2/B-T3）与退回剥离层解析（C-T1）共用，单一口径。token 快照沿用 `ParallelJoinNodeHandler.AllTokens` 的 Local ∪ DB 身份映射去重口径，抽为 `FlowEngine.SnapshotTokens`（A-T1）。 |
| **计票退化护栏（计划期新发现）** | 旧静态计票在「join 被 ForkId==null 的线性 token 进入」的怪异 schema 下按入边数计（等不齐永停）；朴素动态判据会立即放行 ⇒ 行为漂移。定案：`GatewayJoinHelper` 对 **ForkId==null 保留旧静态入边计票路径**，bit 级等价（A-T2 专测锁定）。 |
| **剪后补放行 ≠ 全剪光（计划期新发现）** | 剪枝后 join 若齐批放行，同批 token 全部 Consumed、续 token 属上层批次 ⇒ 「无 Active 穿过本批次」也成立，若不加判别会误判全剪光递归驳回。定案：补放行探测中**检测到任一停泊 token 重入后变为 Consumed（即 join 已放行）则立即返回**，不再走全剪光检查（B-T2 `Prune_JoinBackfill_*` 锁定）。 |

**发现的 spec 与代码现状出入（不改 spec，按下述口径落码）**：
1. spec §5.2 称 BeforeSplit 为「现行为」——实际现状是 `CrossesParallelBlock`（`AdvancedFlow.cs:196`）对一切跨网关退回直接拒绝 E-WF-012，并非允许后全清场。本波 BeforeSplit = **放开该禁令后套用既有全清场机制**（行为上是新放开的能力，机制逐字复用现状代码块）。既有测试无「跨并行块拒绝」断言（已 grep 核实），无不变量冲突。
2. spec §4.2.2 通知联动信箱 spec 偏好矩阵——`PersistentWfNotifier` 现按 `NotificationPrefs` 强类型字段开关，无 BranchPruned 键。本波 `BranchPrunedAsync` **不查偏好开关**（等价信箱 spec「缺键默认 true」三态坍缩），偏好矩阵接管由信箱 spec 落地时统一改造。
3. spec §1 的 FlowEngine 行号锚点有 ±10 行漂移（办理实际在 `ActOnceAsync :136`、会签计票 `:169-177`、驳回连坐 `:221-226`），语义描述全部核实无误。

---

## File Structure（创建/修改清单，每文件一职责）

**后端 `CP6.Core/Services/Wf`**
- Modify `WfStatus.cs` — `FlowTokenStatus` 加 `Pruned = 3`。
- Modify `FlowSchema.cs` — `FlowNode` 加 `public string? OnBranchReject { get; set; }`。
- Create `TokenLineage.cs` — 血缘辅助纯函数（AncestorChain/CrossesFork/ForkParent/ForkStack）。
- Create `FlowGraph.cs` — schema 图论辅助（ReachableFrom/BfsDepths/NearestCommonJoin/BranchDomain）。
- Modify `FlowEngine.Tokens.cs` — 新增 `SnapshotTokens` / `CancelTokenSubtree` / `CancelPendingTasksOfToken` / `CancelPendingServiceJobsOfToken`。
- Create `NodeHandlers/GatewayJoinHelper.cs` — 双 join 共享动态计票放行（D4）。
- Modify `NodeHandlers/ParallelJoinNodeHandler.cs` — 委托 GatewayJoinHelper（删私有 AllTokens/静态计票）。
- Create `NodeHandlers/InclusiveSplitNodeHandler.cs` — 第 7 个 handler。
- Create `NodeHandlers/InclusiveJoinNodeHandler.cs` — 第 8 个 handler。
- Modify `FlowEngine.cs` — `DefaultHandlers()` 加 2 handler；`ActOnceAsync` 驳回分支加 prune 分流（cascade 一行不改）。
- Create `FlowEngine.Prune.cs` — partial：`TryPruneBranchAsync`/`PruneTokenAsync`/`ReleaseOrCollapseAsync`/`FindSplitNode`/`IsPrune`/`IsJoinType`。
- Create `SendBackScopeAnalyzer.cs` — `SendBackScope` 枚举 + `Analyze` 纯函数。
- Modify `AdvancedFlow.cs` — `SendBackToNodeAsync` 三规则接线（删 `CrossesParallelBlock`/`NodesBetween`）。
- Modify `FlowSchemaValidator.cs` — E-WF-020 / E-WF-021 规则。
- Modify `IWfNotifier.cs` — 加 `BranchPrunedAsync` + `NullWfNotifier` 实现。

**后端其他**
- Modify `CP6.Entity/DomainModels/Wf/WfNotificationType.cs` — 加 `BranchPruned = 5`。
- Modify `CP6.WebApi/Services/PersistentWfNotifier.cs` — `BranchPrunedAsync` 三渠道实现。
- Modify `CP6.WebApi/Services/SignalRWfNotifier.cs` — `BranchPrunedAsync` no-op（接口补全）。
- Modify `CP6.WebApi/Program.cs` — DI 注册 2 个新 INodeHandler（:113 后）；i18n concat（:1819 链尾）。
- Create `CP6.WebApi/Seed/I18nOaKernelHardeningScreenSeed.cs` — 五语 12 键。

**前端 `cp6.web/src`**
- Modify `views/oa/designer/designerModel.ts` — `SchemaNode.onBranchReject` + palette 2 入口 + validateClient E-WF-020/021 镜像。
- Modify `views/oa/designer/designerModel.test.ts` — :45 palette 类型清单断言补 2 类型。
- Create `views/oa/designer/designerModel.hardening.test.ts` — 新 vitest。
- Create `views/oa/designer/nodes/InclusiveGatewayNode.vue` — 空心圆菱形节点（BPMN 惯例）。
- Modify `views/oa/designer/DesignerCanvas.vue` — 注册 2 节点模板 + palette 空心 dot 样式。
- Modify `views/oa/designer/NodePropertyPanel.vue` — 「分支驳回策略」段（parallelSplit/inclusiveSplit）。
- Modify `types/oa/notification.ts` — `NotificationType.BranchPruned = 5` 镜像。

**测试 / QA**
- Create `CP6.Tests/Wf/TokenLineageTests.cs` / `DynamicJoinCountTests.cs` / `InclusiveGatewayTests.cs` / `InclusiveValidatorTests.cs` / `BranchPruneTests.cs` / `BranchPruneNestedTests.cs` / `SendBackScopeTests.cs` / `TokenSubtreeCancelTests.cs` / `SendBackThreeRuleTests.cs`。
- Modify `CP6.Tests/Oa/NotificationEngineHookTests.cs` — `CountingNotifier` 补 `BranchPrunedAsync` 计数。
- Create `docs/superpowers/qa/wfs-kernel-hardening/{README.md,seed.sql,qa_kernel_hardening.ps1}` — gstack harness。

---

## 共享契约（所有 Task 用这些**精确**名字与签名，前后一致，不许漂移）

```csharp
// WfStatus.cs
public const int Pruned = 3;                                  // FlowTokenStatus 新常量

// FlowSchema.cs / FlowNode
public string? OnBranchReject { get; set; }                   // null/"cascade"=连坐(现状)；"prune"=剪枝

// TokenLineage.cs（internal static class TokenLineage）
public static IEnumerable<Wf_FlowToken> AncestorChain(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);
public static bool CrossesFork(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t, Guid forkId);
public static Wf_FlowToken? ForkParent(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);
public static List<(Wf_FlowToken BranchToken, Guid ForkId, string SplitNodeId)> ForkStack(
    IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);          // 内→外

// FlowGraph.cs（internal static class FlowGraph）
public static FlowNode? NearestCommonJoin(FlowSchema schema, FlowNode split);
public static HashSet<string> ReachableFrom(FlowSchema schema, string startId);
public static HashSet<string> BranchDomain(FlowSchema schema, string edgeTargetId, string pairedJoinId);

// FlowEngine.Tokens.cs（partial FlowEngine）
internal IReadOnlyList<Wf_FlowToken> SnapshotTokens(Guid instanceId);          // Local ∪ DB 去重快照
internal void CancelTokenSubtree(Guid instanceId, Guid rootTokenId);           // 剥离层子树清场

// NodeHandlers/GatewayJoinHelper.cs
internal static class GatewayJoinHelper { public static Task TryReleaseAsync(NodeContext ctx, string historyAction); }

// FlowEngine.Prune.cs（partial FlowEngine）
internal Task<bool> TryPruneBranchAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token, Guid actorId, string? comment);
internal static FlowNode? FindSplitNode(FlowSchema schema, IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken token);
internal static bool IsPrune(FlowNode split);

// SendBackScopeAnalyzer.cs
public enum SendBackScope { SameBranch, BeforeSplit, SiblingBranch }
public static (SendBackScope Scope, Wf_FlowToken? StripToken) Analyze(
    FlowSchema schema, IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken current,
    string currentNodeId, string targetNodeId);                // 结构不可判定 → throw E-WF-012

// IWfNotifier.cs / WfNotificationType.cs
Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment);
public const int BranchPruned = 5;
```

- 节点类型串：`"inclusiveSplit"` / `"inclusiveJoin"`（比较一律 OrdinalIgnoreCase，对齐 `EnterNodeAsync` 的 ToLowerInvariant 分发）。
- 履历 action：`"inclusiveSplit"` / `"inclusiveJoin"` / `"branchPruned"`；退回沿用 `"sendback"`。
- 错误码：**E-WF-019**（运行时，退回兄弟支拒绝）/ **E-WF-020**（静态，inclusive default 边）/ **E-WF-021**（静态，配对 + onBranchReject 值域/落点）。
- 前端 i18n 键：`oa.designer.gw.inclusiveSplit|inclusiveJoin|branchReject|branchReject.cascade|branchReject.prune|branchRejectHint`、`oa.designer.errInclusiveDefault|errInclusivePair|errBranchReject`、`E-WF-019|E-WF-020|E-WF-021`（12 键五语）。

---

