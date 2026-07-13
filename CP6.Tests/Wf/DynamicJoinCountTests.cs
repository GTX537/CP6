using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>D4 动态计票定点回归（hardening spec §3.3/§9）。旧场景全等由既有 ParallelGatewayTests 5 测锁定，
/// 本文件补：①嵌套在途防提前放行（spec 评审抓过的洞）②ForkId==null 退化保持旧静态计票。</summary>
public class DynamicJoinCountTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // start → split → ( a → innerSplit → (a1,a2) → innerJoin → join , b → join ) → join → end
    private static FlowSchema NestedSchema(Guid u1, Guid u2, Guid ub) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "split", Type = "parallelSplit" },
            new FlowNode { Id = "a", Type = "parallelSplit" },
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
    public async Task NestedInFlight_OuterJoinWaits_UntilInnerSubtreeDone()
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "nst", FlowName = "nst", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(NestedSchema(u1, u2, ub)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("nst", Guid.NewGuid(), "{}");

        // ★ 先审 b：外层 A 支在内层子 fork 在途（同外层 ForkId 无 Active，只有血缘链）→ 外层 join 必须等
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending));
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "join" && t.Status == FlowTokenStatus.Active));

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t1.Id, u1, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t2.Id, u2, approve: true);   // 内层齐 → 上弹 → 外层齐 → end

        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task NullFork_LinearTokenAtJoin_KeepsLegacyStaticCount_ParksForever()
    {
        using var db = NewDb();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        // 怪异 schema：join 有 2 条入边，但 token 沿线性路径（无 split）到达 join，ForkId==null
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
                new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
                new FlowNode { Id = "join", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "a" },
                new FlowEdge { From = "a", To = "join" }, new FlowEdge { From = "b", To = "join" },
                new FlowEdge { From = "join", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "nullfork", FlowName = "x", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("nullfork", Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);

        // 旧静态计票：到场 1 < 入边 2 → 永停泊。动态判据若不做 null 退化会在此放行 → 行为漂移（本测试拦住）
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "join" && t.Status == FlowTokenStatus.Active));
    }
}
