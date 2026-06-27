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

    [Fact]
    public async Task SendBack_CancelsAllActiveTokens_RebuildsSingleRootAtTarget()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedFlowAsync(db, a, b);

        var instId = await Engine(db).SubmitAsync(FlowKey, Guid.NewGuid(), "{}");
        // A 同意 → 流程停在 n2，此刻应有恰好 1 个 Active token 停在 n2
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Engine(db).ActAsync(t1.Id, a, approve: true);

        var activeBeforeSendback = await db.Wf_FlowTokens
            .Where(t => t.InstanceId == instId && t.Status == FlowTokenStatus.Active)
            .ToListAsync();
        Assert.Single(activeBeforeSendback);
        Assert.Equal("n2", activeBeforeSendback[0].NodeId);   // 停在 n2

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending);

        // B 退回到 n1
        await Engine(db).SendBackAsync(t2.Id, b, "n1", "退回审核");

        // 退回前的所有 Active token 应全部 Cancelled
        var oldTokenAfter = await db.Wf_FlowTokens.SingleAsync(t => t.Id == activeBeforeSendback[0].Id);
        Assert.Equal(FlowTokenStatus.Cancelled, oldTokenAfter.Status);

        // 现在应有且仅有 1 个 Active token，停在 n1
        var activeAfter = await db.Wf_FlowTokens
            .Where(t => t.InstanceId == instId && t.Status == FlowTokenStatus.Active)
            .ToListAsync();
        Assert.Single(activeAfter);
        Assert.Equal("n1", activeAfter[0].NodeId);            // 根 token 重建在目标节点
        Assert.Null(activeAfter[0].ParentTokenId);            // 根 token：无父
        Assert.Null(activeAfter[0].ForkId);                   // 根 token：无分叉

        // 实例仍在运行
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);
    }

    // ───────────────────────── C-3 加签 ─────────────────────────

    private const string SingleFlowKey = "single";

    /// <summary>单审批人节点 n1(A)→end，默认会签 all。</summary>
    private static async Task SeedSingleApproverAsync(CP6Context db, Guid a)
    {
        var schema = new FlowSchema
        {
            Start = "n1",
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = a },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "end" } },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = SingleFlowKey, FlowName = "单审批", FormKey = "test",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AfterAddSign_NodeWaitsForBoth_ThenAdvances()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var c = Guid.NewGuid();
        await SeedSingleApproverAsync(db, a);
        var instId = await Engine(db).SubmitAsync(SingleFlowKey, Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Engine(db).AddSignAsync(ta.Id, a, c, "after");   // 后加签 C

        // A 同意 → 节点未流转（C 还没审，会签 all 需全同意）
        await Engine(db).ActAsync(ta.Id, a, true);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);
        Assert.Equal("n1", inst.CurrentNode);

        // 加签人 C 同意 → 节点决出 → 流转 end → 通过
        var tc = await db.Wf_FlowTasks.SingleAsync(t => t.AddSignSource == "after" && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tc.Id, c, true);
        inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
    }

    [Fact]
    public async Task BeforeAddSign_SuspendsOriginal_ReactivatesAfterAddSigneeApproves()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var c = Guid.NewGuid();
        await SeedSingleApproverAsync(db, a);
        var instId = await Engine(db).SubmitAsync(SingleFlowKey, Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Engine(db).AddSignAsync(ta.Id, a, c, "before");   // 前加签 C

        // 原任务挂起，C 待办
        var taAfter = await db.Wf_FlowTasks.SingleAsync(t => t.Id == ta.Id);
        Assert.Equal(FlowTaskStatus.Suspended, taAfter.Status);

        // C 同意 → 激活 A 的原任务（Suspended→Pending），节点尚未决出
        var tc = await db.Wf_FlowTasks.SingleAsync(t => t.AddSignSource == "before");
        await Engine(db).ActAsync(tc.Id, c, true);
        taAfter = await db.Wf_FlowTasks.SingleAsync(t => t.Id == ta.Id);
        Assert.Equal(FlowTaskStatus.Pending, taAfter.Status);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);

        // A 再同意 → 全同意 → 通过
        await Engine(db).ActAsync(ta.Id, a, true);
        inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
    }

    [Fact]
    public async Task AddSign_InvalidSource_OrNonPending_OrLimit_Throws()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        await SeedSingleApproverAsync(db, a);
        await Engine(db).SubmitAsync(SingleFlowKey, Guid.NewGuid(), "{}");
        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine(db).AddSignAsync(ta.Id, a, Guid.NewGuid(), "sideways"));

        // 加签到上限：连加 10 个 after，第 11 个抛
        for (int i = 0; i < 10; i++)
            await Engine(db).AddSignAsync(ta.Id, a, Guid.NewGuid(), "after");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine(db).AddSignAsync(ta.Id, a, Guid.NewGuid(), "after"));
    }

    [Fact]
    public async Task AddSign_InheritsTokenId_FromOriginalTask()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var c = Guid.NewGuid();
        await SeedSingleApproverAsync(db, a);
        var instId = await Engine(db).SubmitAsync(SingleFlowKey, Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        Assert.NotNull(ta.TokenId);   // Submit 时 token 已绑定到原任务

        await Engine(db).AddSignAsync(ta.Id, a, c, "after");

        // 新建的加签任务继承原任务的 TokenId
        var addSignTask = await db.Wf_FlowTasks.SingleAsync(t => t.AddSignSource == "after");
        Assert.NotNull(addSignTask.TokenId);
        Assert.Equal(ta.TokenId, addSignTask.TokenId);   // ★ 加签继承 TokenId
    }

    // ───────────────────────── C-4 委派 ─────────────────────────

    [Fact]
    public async Task Delegate_InEffect_SwapsAssignee_DualTrace()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedSingleApproverAsync(db, a);

        // A 把审批权委派给 B（有效期覆盖当前）
        await Engine(db).SetDelegateAsync(a, b, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1));

        await Engine(db).SubmitAsync(SingleFlowKey, Guid.NewGuid(), "{}");

        var task = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        Assert.Equal(b, task.AssigneeId);   // 建待办时换成代理人 B
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "delegate"));   // 双痕：代 A 审批
    }

    [Fact]
    public async Task Delegate_Expired_NoSwap()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedSingleApproverAsync(db, a);

        // 已过期的委派 → 不替换
        await Engine(db).SetDelegateAsync(a, b, DateTime.Now.AddDays(-5), DateTime.Now.AddDays(-2));
        await Engine(db).SubmitAsync(SingleFlowKey, Guid.NewGuid(), "{}");

        var task = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        Assert.Equal(a, task.AssigneeId);   // 仍是原审批人 A
        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.Action == "delegate"));
    }

    [Fact]
    public async Task SetDelegate_SelfOrBadRange_Throws()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Engine(db).SetDelegateAsync(a, a, DateTime.Now, DateTime.Now.AddDays(1)));   // 委派给自己
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Engine(db).SetDelegateAsync(a, Guid.NewGuid(), DateTime.Now.AddDays(1), DateTime.Now));   // 失效早于生效
    }
}
