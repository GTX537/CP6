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

    private static async Task SeedAsync(CP6Context db, Guid ua, Guid ub)
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "p", FlowName = "p", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(ForkSchema(ua, ub)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Parallel_BothApprove_Completes()
    {
        using var db = NewDb();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub);

        await Engine(db).SubmitAsync("p", Guid.NewGuid(), "{}");
        Assert.Equal(2, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task Parallel_OrderIndependent()
    {
        using var db = NewDb();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub);
        await Engine(db).SubmitAsync("p", Guid.NewGuid(), "{}");

        // 顺序颠倒：先 b 后 a，结果应一致
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task Parallel_JoinWaits()
    {
        using var db = NewDb();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub);
        await Engine(db).SubmitAsync("p", Guid.NewGuid(), "{}");

        // 只审 a，join 未齐 → 实例仍 Running，b 分支 token 仍 Active、任务仍 Pending
        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);

        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        // b 分支仍停在 b 节点（Active token + Pending 任务）
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "b" && t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task Parallel_RejectTerminates()
    {
        using var db = NewDb();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub);
        await Engine(db).SubmitAsync("p", Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: false, "no");

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
    }

    // start → split → ( a → innerSplit → (a1,a2) → innerJoin → join , b → join ) → join → end
    private static FlowSchema NestedSchema(Guid u1, Guid u2, Guid ub) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "split", Type = "parallelSplit" },
            new FlowNode { Id = "a", Type = "parallelSplit" },           // a 分支自身再分叉
            new FlowNode { Id = "a1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
            new FlowNode { Id = "a2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u2 },
            new FlowNode { Id = "innerJoin", Type = "parallelJoin" },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "split" },
            new FlowEdge { From = "split", To = "a" }, new FlowEdge { From = "split", To = "b" },
            new FlowEdge { From = "a", To = "a1" }, new FlowEdge { From = "a", To = "a2" },
            new FlowEdge { From = "a1", To = "innerJoin" }, new FlowEdge { From = "a2", To = "innerJoin" },
            new FlowEdge { From = "innerJoin", To = "join" },
            new FlowEdge { From = "b", To = "join" },
            new FlowEdge { From = "join", To = "end" },
        },
    };

    [Fact]
    public async Task Nested_Fork()
    {
        using var db = NewDb();
        var u1 = Guid.NewGuid(); var u2 = Guid.NewGuid(); var ub = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "p", FlowName = "p", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(NestedSchema(u1, u2, ub)), Version = 1, Enable = true });
        await db.SaveChangesAsync();

        await Engine(db).SubmitAsync("p", Guid.NewGuid(), "{}");
        // 三个待办并存：a1/a2(内层分叉) + b(外层分支)
        Assert.Equal(3, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t1.Id, u1, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t2.Id, u2, approve: true);   // innerJoin 齐 → 上弹至外 join 等 b
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);   // 外 join 齐 → end

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }
}
