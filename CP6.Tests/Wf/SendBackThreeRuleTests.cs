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
