using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>驳回剪枝矩阵（hardening spec §4/§9）：单支剪/兄弟继续/剪后 join 补放行/全剪光→Rejected/
/// cascade 默认零 diff/FormTo 履历状态。通知计数用 CountingPruneNotifier（仿 NotificationEngineHookTests）。</summary>
public class BranchPruneTests
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
    private static FlowEngine Engine(CP6Context db, IWfNotifier? n = null) => new(db, new ApproverResolver(db), n);

    // start → split[onBranchReject 可配] → (a, b) → join → end
    private static FlowSchema ForkSchema(Guid ua, Guid ub, string? onBranchReject) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "split", Type = "parallelSplit", OnBranchReject = onBranchReject },
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

    private static async Task SeedAsync(CP6Context db, Guid ua, Guid ub, string? obr)
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "pr", FlowName = "pr", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(ForkSchema(ua, ub, obr)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Prune_SingleBranch_SiblingContinues_ThenApproves()
    {
        using var db = NewDb();
        var notifier = new CountingPruneNotifier();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub, "prune");
        await Engine(db, notifier).SubmitAsync("pr", Guid.NewGuid(), "{}");

        // a 驳回 → 只剪 a 支
        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(ta.Id, ua, approve: false, "a 部门否");

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);                       // ★ 不连坐
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "a" && t.Status == FlowTokenStatus.Pruned));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending)); // 兄弟不倒
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "branchPruned"));
        Assert.Equal(1, notifier.PrunedCount);
        Assert.Equal(0, notifier.RejectedCount);
        // a 支 Pending 履历 → Voided；b 支不受扰
        Assert.False(await db.Wf_FlowFormTos.AnyAsync(f => f.NodeId == "a" && f.Status == FlowFormToStatus.Pending));
        Assert.True(await db.Wf_FlowFormTos.AnyAsync(f => f.NodeId == "b" && f.Status == FlowFormToStatus.Pending));

        // b 过 → join 动态计票（Pruned 从等待集消失）→ Approved
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task Prune_JoinBackfill_ParkedSiblingReleases_NoFalseCollapse()
    {
        using var db = NewDb();
        var notifier = new CountingPruneNotifier();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub, "prune");
        await Engine(db, notifier).SubmitAsync("pr", Guid.NewGuid(), "{}");

        // 先 b 过（b 到场 join 停泊），再驳 a → 剪枝使 join 凑齐 → 补放行 → Approved（且不得误判全剪光）
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(ta.Id, ua, approve: false, "否");

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);                      // ★ 补放行成功
        Assert.Equal(1, notifier.PrunedCount);
        Assert.Equal(0, notifier.RejectedCount);                                     // ★ 未误判剪光递归驳回
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task Prune_AllBranches_CollapsesToInstanceRejected()
    {
        using var db = NewDb();
        var notifier = new CountingPruneNotifier();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub, "prune");
        await Engine(db, notifier).SubmitAsync("pr", Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(ta.Id, ua, approve: false);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(tb.Id, ub, approve: false);              // 最后一支也剪 → 全剪光

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);                      // ★ 上弹到顶（无外层）→ Rejected
        Assert.Equal(2, notifier.PrunedCount);
        Assert.Equal(1, notifier.RejectedCount);                                     // 终态分发照走
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowFormTos.AnyAsync(f => f.Status == FlowFormToStatus.Pending));
    }

    [Theory]
    [InlineData(null)]          // 未配置（现状）
    [InlineData("cascade")]     // 显式 cascade
    public async Task Cascade_Default_ZeroDiff_RejectTerminatesWholeInstance(string? obr)
    {
        using var db = NewDb();
        var notifier = new CountingPruneNotifier();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub, obr);
        await Engine(db, notifier).SubmitAsync("pr", Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(ta.Id, ua, approve: false, "no");

        // 与 ParallelGatewayTests.Parallel_RejectTerminates 逐字等价的终态
        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Pruned));  // ★ cascade 不产生 Pruned
        Assert.Equal(0, notifier.PrunedCount);
        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.Action == "branchPruned"));
    }

    [Fact]
    public async Task Prune_LinearFlow_NoFork_FallsBackToCascade()
    {
        // 线性流（token ForkId==null）上即便某节点被驳回，也走既有连坐路径（prune 只对分支 token 有意义）
        using var db = NewDb();
        var ua = Guid.NewGuid();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "end" } },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "lin", FlowName = "lin", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("lin", Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: false);
        Assert.Equal(FlowInstanceStatus.Rejected, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }
}
