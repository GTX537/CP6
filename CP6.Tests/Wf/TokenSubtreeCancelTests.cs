using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>剥离层子树清场（hardening spec §5.2）：只清子树、兄弟支零扰动。InternalsVisibleTo 直调引擎内部方法。</summary>
public class TokenSubtreeCancelTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // s → outer → ( inner → (x1,x2) → ij , b ) → oj → end（复用 B-T3 拓扑，无剪枝配置）
    private static FlowSchema Nested(Guid u1, Guid u2, Guid ub) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "outer", Type = "parallelSplit" },
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
            new FlowEdge { From = "outer", To = "inner" }, new FlowEdge { From = "outer", To = "b" },
            new FlowEdge { From = "inner", To = "x1" }, new FlowEdge { From = "inner", To = "x2" },
            new FlowEdge { From = "x1", To = "ij" }, new FlowEdge { From = "x2", To = "ij" },
            new FlowEdge { From = "ij", To = "oj" },
            new FlowEdge { From = "b", To = "oj" },
            new FlowEdge { From = "oj", To = "end" },
        },
    };

    [Fact]
    public async Task CancelSubtree_InnerForkKilled_SiblingBranchUntouched()
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "sub", FlowName = "x", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(Nested(u1, u2, ub)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("sub", Guid.NewGuid(), "{}");

        // 剥离层 = 外层「inner 支」代表 token：进了 inner split 的那枚（NodeId=="inner"，Consumed）
        var strip = await db.Wf_FlowTokens.SingleAsync(t => t.NodeId == "inner");
        var eng = Engine(db);
        eng.CancelTokenSubtree(instId, strip.Id);
        await db.SaveChangesAsync();

        // 子树内：x1/x2 token Cancelled、任务 Cancelled、Pending 履历 Voided
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t =>
            (t.NodeId == "x1" || t.NodeId == "x2") && t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t =>
            (t.AssigneeId == u1 || t.AssigneeId == u2) && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowFormTos.AnyAsync(f =>
            (f.NodeId == "x1" || f.NodeId == "x2") && f.Status == FlowFormToStatus.Pending));

        // ★ 兄弟支 b 零扰动
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "b" && t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        Assert.True(await db.Wf_FlowFormTos.AnyAsync(f => f.NodeId == "b" && f.Status == FlowFormToStatus.Pending));
        // 实例不被动状态（清场不改 inst.Status）
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }
}
