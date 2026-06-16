using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>OA 章07 高级流程（C-2 退回）。退回作废在途待办 + CurrentNode 回退 + 重建目标待办 + FlowHistory 只追加。</summary>
public class AdvancedFlowTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private const string FlowKey = "two-step";

    /// <summary>两段审批：n1(审批人A)→n2(审批人B)→end。</summary>
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
            Edges =
            {
                new FlowEdge { From = "n1", To = "n2" },
                new FlowEdge { From = "n2", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = FlowKey, FlowName = "两段审批", FormKey = "test",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SendBack_VoidsLiveTask_RebuildsTarget_AppendsHistory()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedFlowAsync(db, a, b);

        var instId = await Engine(db).SubmitAsync(FlowKey, Guid.NewGuid(), "{}");
        // A 同意 → 流转到 n2，B 有待办
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Engine(db).ActAsync(t1.Id, a, approve: true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending);

        // B 退回到 n1
        await Engine(db).SendBackAsync(t2.Id, b, "n1", "资料不全，退回");

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);
        Assert.Equal("n1", inst.CurrentNode);                                   // 回退

        var t2After = await db.Wf_FlowTasks.SingleAsync(t => t.Id == t2.Id);
        Assert.Equal(FlowTaskStatus.Cancelled, t2After.Status);                 // n2 在途待办作废

        // n1 重建出新的待办给 A（原 n1 任务已 Approved，新任务 Pending）
        var n1Pending = await db.Wf_FlowTasks.Where(t => t.NodeId == "n1" && t.Status == FlowTaskStatus.Pending).ToListAsync();
        Assert.Single(n1Pending);
        Assert.Equal(a, n1Pending[0].AssigneeId);

        // 痕迹追加 sendback，不删旧
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "sendback"));
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "submit"));   // 旧痕迹保留
        Assert.True(await db.Wf_FlowHistories.CountAsync() >= 3);                            // submit+approve+sendback
    }

    [Fact]
    public async Task SendBack_ThenContinue_FlowsForwardAgain()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedFlowAsync(db, a, b);

        var instId = await Engine(db).SubmitAsync(FlowKey, Guid.NewGuid(), "{}");
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Engine(db).ActAsync(t1.Id, a, true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending);
        await Engine(db).SendBackAsync(t2.Id, b, "n1");

        // A 重新同意 → 再次到 n2，B 再次有待办
        var t1New = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1" && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t1New.Id, a, true);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal("n2", inst.CurrentNode);
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task SendBack_AlreadyHandledTask_NoOp()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedFlowAsync(db, a, b);
        await Engine(db).SubmitAsync(FlowKey, Guid.NewGuid(), "{}");
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Engine(db).ActAsync(t1.Id, a, true);   // t1 变 Approved

        await Engine(db).SendBackAsync(t1.Id, a, "n1");   // 对已办任务退回 → 幂等无效

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal("n2", inst.CurrentNode);   // 未受影响
        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.Action == "sendback"));
    }

    [Fact]
    public async Task SendBack_InvalidTarget_Throws()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedFlowAsync(db, a, b);
        await Engine(db).SubmitAsync(FlowKey, Guid.NewGuid(), "{}");
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine(db).SendBackAsync(t1.Id, a, "nope"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine(db).SendBackAsync(t1.Id, a, "end"));
    }
}
