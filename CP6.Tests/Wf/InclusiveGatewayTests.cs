using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>inclusive 网关行为（hardening spec §3.1/§3.2/§9）。构造模式沿 ParallelGatewayTests。</summary>
public class InclusiveGatewayTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // s → isplit → ( a["goA > 0"], b["goB > 0"], c["goC > 0"], d[default 无条件] ) → ijoin → end
    private static FlowSchema IncSchema(Guid ua, Guid ub, Guid uc, Guid ud) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "c", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = uc },
            new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
            new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "isplit" },
            new FlowEdge { From = "isplit", To = "a", Condition = "goA > 0" },
            new FlowEdge { From = "isplit", To = "b", Condition = "goB > 0" },
            new FlowEdge { From = "isplit", To = "c", Condition = "goC > 0" },
            new FlowEdge { From = "isplit", To = "d" },                          // default 兜底边
            new FlowEdge { From = "a", To = "ijoin" }, new FlowEdge { From = "b", To = "ijoin" },
            new FlowEdge { From = "c", To = "ijoin" }, new FlowEdge { From = "d", To = "ijoin" },
            new FlowEdge { From = "ijoin", To = "end" },
        },
    };

    private static async Task<Guid> SeedAndSubmitAsync(CP6Context db, Guid ua, Guid ub, Guid uc, Guid ud, string vars)
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "inc", FlowName = "inc", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(IncSchema(ua, ub, uc, ud)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        return await Engine(db).SubmitAsync("inc", Guid.NewGuid(), vars);
    }

    [Fact]
    public async Task TwoOfThreeTrue_SpawnsOnlyTrueBranches_DefaultNotWalked()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), uc = Guid.NewGuid(), ud = Guid.NewGuid();
        await SeedAndSubmitAsync(db, ua, ub, uc, ud, "{\"goA\":1,\"goB\":1,\"goC\":0}");

        Assert.Equal(2, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == uc));   // 假边不走
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));   // ★ 有真边时 default 不走
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "inclusiveSplit"));

        // 只等实际激活的两支：a、b 都过 → 放行到 end
        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task AllConditionsTrue_AllThreeWalk_DefaultStillNotWalked()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), uc = Guid.NewGuid(), ud = Guid.NewGuid();
        await SeedAndSubmitAsync(db, ua, ub, uc, ud, "{\"goA\":1,\"goB\":1,\"goC\":1}");

        Assert.Equal(3, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));   // ★ 全真条件边时 default 不走
    }

    [Fact]
    public async Task AllFalse_OnlyDefaultWalks_SingleBranchJoinReleases()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), uc = Guid.NewGuid(), ud = Guid.NewGuid();
        await SeedAndSubmitAsync(db, ua, ub, uc, ud, "{\"goA\":0,\"goB\":0,\"goC\":0}");

        Assert.Equal(1, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        var td = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        Assert.Equal(ud, td.AssigneeId);                              // ★ 全假仅 default 兜底

        await Engine(db).ActAsync(td.Id, ud, approve: true);          // 单支到场即齐（活支==1）
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    // 嵌套：s → psplit → ( isplit⊂parallel 支 , p2 支 ) → pjoin → end；inclusive 内嵌 a/b + default d
    [Fact]
    public async Task InclusiveInsideParallel_OuterJoinWaitsInclusiveSubtree()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ud = Guid.NewGuid(), up = Guid.NewGuid();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "psplit", Type = "parallelSplit" },
                new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
                new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
                new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
                new FlowNode { Id = "p2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = up },
                new FlowNode { Id = "pjoin", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "psplit" },
                new FlowEdge { From = "psplit", To = "isplit" }, new FlowEdge { From = "psplit", To = "p2" },
                new FlowEdge { From = "isplit", To = "a", Condition = "goA > 0" },
                new FlowEdge { From = "isplit", To = "d" },
                new FlowEdge { From = "a", To = "ijoin" }, new FlowEdge { From = "d", To = "ijoin" },
                new FlowEdge { From = "ijoin", To = "pjoin" },
                new FlowEdge { From = "p2", To = "pjoin" },
                new FlowEdge { From = "pjoin", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "mix", FlowName = "mix", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("mix", Guid.NewGuid(), "{\"goA\":1}");

        // p2 先过 → 外层 pjoin 必须等 inclusive 子树（血缘感知）
        var tp = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == up && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tp.Id, up, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);   // inclusive 活支只有 a → ijoin 放行 → pjoin 齐
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));   // default 未走
    }

    // 嵌套反向：s → isplit → ( psplit⊂inclusive 支 → (p1,p2) → pj → ijoin , d[default] → ijoin ) → end
    [Fact]
    public async Task ParallelInsideInclusive_InclusiveJoinWaitsParallelSubtree()
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ud = Guid.NewGuid();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
                new FlowNode { Id = "psplit", Type = "parallelSplit" },
                new FlowNode { Id = "p1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
                new FlowNode { Id = "p2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u2 },
                new FlowNode { Id = "pj", Type = "parallelJoin" },
                new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
                new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "isplit" },
                new FlowEdge { From = "isplit", To = "psplit", Condition = "goP > 0" },
                new FlowEdge { From = "isplit", To = "d" },
                new FlowEdge { From = "psplit", To = "p1" }, new FlowEdge { From = "psplit", To = "p2" },
                new FlowEdge { From = "p1", To = "pj" }, new FlowEdge { From = "p2", To = "pj" },
                new FlowEdge { From = "pj", To = "ijoin" },
                new FlowEdge { From = "d", To = "ijoin" },
                new FlowEdge { From = "ijoin", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "pin", FlowName = "pin", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("pin", Guid.NewGuid(), "{\"goP\":1}");   // 真边=psplit 支，default 不走

        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t1.Id, u1, approve: true);   // p1 过 → pj 停泊；ijoin 活支子树在途，必须等
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t2.Id, u2, approve: true);   // pj 齐 → 上弹 → ijoin 齐（活支==1）→ end
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }
}
