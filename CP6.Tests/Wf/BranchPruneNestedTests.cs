using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>嵌套剪枝递归上弹矩阵（hardening spec §4.2.4/§9）：内层全剪光 × 外层 prune/cascade 两态。</summary>
public class BranchPruneNestedTests
{
    private sealed class CountingPruneNotifier : IWfNotifier
    {
        public int PrunedCount { get; private set; }
        public int RejectedCount { get; private set; }
        public Task TodoCreatedAsync(Guid assigneeId, Guid instanceId, Guid taskId, string flowKey) => Task.CompletedTask;
        public Task FlowApprovedAsync(Guid starterId, Guid instanceId, string flowKey) => Task.CompletedTask;
        public Task FlowRejectedAsync(Guid starterId, Guid instanceId, string flowKey, string? comment)
        { RejectedCount++; return Task.CompletedTask; }
        public Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment)
        { PrunedCount++; return Task.CompletedTask; }
    }

    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db, IWfNotifier n) => new(db, new ApproverResolver(db), n);

    // s → outer[外层策略] → ( inner[prune] → (x1,x2) → ij , b ) → oj → end
    private static FlowSchema Nested(Guid u1, Guid u2, Guid ub, string? outerPolicy) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "outer", Type = "parallelSplit", OnBranchReject = outerPolicy },
            new FlowNode { Id = "inner", Type = "parallelSplit", OnBranchReject = "prune" },
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

    private static async Task SeedAsync(CP6Context db, Guid u1, Guid u2, Guid ub, string? outerPolicy)
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "nstpr", FlowName = "x", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(Nested(u1, u2, ub, outerPolicy)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task InnerAllPruned_OuterPrune_PrunesOuterBranch_SiblingCompletes()
    {
        using var db = NewDb();
        var n = new CountingPruneNotifier();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        await SeedAsync(db, u1, u2, ub, "prune");
        await Engine(db, n).SubmitAsync("nstpr", Guid.NewGuid(), "{}");

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t1.Id, u1, approve: false);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.Equal(1, n.PrunedCount);                                    // 只剪 x1，未上弹

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t2.Id, u2, approve: false);           // 内层全剪光 → 上弹外层（prune）剪外层该支

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);             // ★ 外层 prune：实例不死
        Assert.Equal(3, n.PrunedCount);                                    // x1 + x2 + 外层 inner 支（递归层记痕）
        Assert.Equal(3, await db.Wf_FlowHistories.CountAsync(h => h.Action == "branchPruned"));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending)); // b 支不倒

        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(tb.Id, ub, approve: true);            // b 过 → 外 join 只等活支 → end
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.Equal(0, n.RejectedCount);
    }

    [Fact]
    public async Task InnerAllPruned_OuterCascade_InstanceRejected()
    {
        using var db = NewDb();
        var n = new CountingPruneNotifier();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        await SeedAsync(db, u1, u2, ub, null);                             // 外层未配置 = cascade
        await Engine(db, n).SubmitAsync("nstpr", Guid.NewGuid(), "{}");

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t1.Id, u1, approve: false);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t2.Id, u2, approve: false);           // 内层全剪光 → 上弹外层（cascade）→ 实例 Rejected

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);            // ★ 外层 cascade：整单驳回
        Assert.Equal(1, n.RejectedCount);
        // b 支被连坐清场：任务 Cancelled、token Cancelled、Pending 履历 Voided
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowFormTos.AnyAsync(f => f.Status == FlowFormToStatus.Pending));
    }

    [Fact]
    public async Task InnerOnePruned_OneApproved_InnerJoinBackfills_OuterContinues()
    {
        // 内层剪一支 + 另一支已到场 ij 停泊 → 补放行上弹 → 外层等 b（补放行与递归的边界回归）
        using var db = NewDb();
        var n = new CountingPruneNotifier();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        await SeedAsync(db, u1, u2, ub, "prune");
        await Engine(db, n).SubmitAsync("nstpr", Guid.NewGuid(), "{}");

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t1.Id, u1, approve: true);            // x1 过 → 到场 ij 停泊
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t2.Id, u2, approve: false);           // x2 剪 → ij 补放行 → 上弹外层等 b

        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.Equal(1, n.PrunedCount);                                    // 只剪 x2，无递归记痕

        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }
}
