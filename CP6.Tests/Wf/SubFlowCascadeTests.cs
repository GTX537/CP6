using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>级联取消三路径（spec §3.3）：父终止递归 / CancelTokenSubtree 第五清 / 撤回路径。
/// 断言口径：子实例 Withdrawn + 在途待办清 + 不回注（父无 subFlowResumed 履历）。</summary>
public class SubFlowCascadeTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    /// <summary>并行父：ps → split(可配 onBranchReject) → ( sub ⊂ A支 , b ⊂ B支 ) → join → pe。</summary>
    private static FlowSchema ParallelParent(Guid ub, string subFlowKey, string? onBranchReject = null) => new()
    {
        Start = "ps",
        Nodes =
        {
            new FlowNode { Id = "ps", Type = "start" },
            new FlowNode { Id = "split", Type = "parallelSplit", OnBranchReject = onBranchReject },
            new FlowNode { Id = "sub", Type = "subFlow", SubFlowKey = subFlowKey },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "pe", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "ps", To = "split" },
            new FlowEdge { From = "split", To = "sub" }, new FlowEdge { From = "split", To = "b" },
            new FlowEdge { From = "sub", To = "join" }, new FlowEdge { From = "b", To = "join" },
            new FlowEdge { From = "join", To = "pe" },
        },
    };

    [Fact]
    public async Task ParentWithdraw_CascadesThreeLevels_NoWriteback()
    {
        using var db = NewDb();
        Guid ca = Guid.NewGuid(), pa = Guid.NewGuid();
        SeedDef(db, "leaf", ChildSchema(ca));
        SeedDef(db, "mid", ParentSchema(pa, "leaf"));
        SeedDef(db, "top", ParentSchema(pa, "mid"));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var topId = await eng.SubmitAsync("top", Guid.NewGuid(), "{}");
        var mid = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == topId);
        var leaf = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == mid.Id);
        var starter = (await db.Wf_FlowInstances.SingleAsync(i => i.Id == topId)).StarterId;

        await new TaskCenterService(db, eng).WithdrawAsync(topId, starter);

        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == mid.Id)).Status);
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == leaf.Id)).Status);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == leaf.Id && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => (t.InstanceId == mid.Id || t.InstanceId == leaf.Id)
            && t.Status == FlowTokenStatus.Active));
        // 级联 Withdrawn 不回注：父/祖父零 subFlowResumed；级联路径不投递唤醒凭据
        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.Action == "subFlowResumed"));
        Assert.False(await db.Wf_ServiceJobs.AnyAsync(j => j.Kind == WfJobKind.SubFlowResume && j.Status == ServiceJobStatus.Pending));
        Assert.True(await db.Wf_FlowHistories.AnyAsync(h => h.InstanceId == leaf.Id && h.Action == "subFlowCascadeCancelled"));
    }

    [Fact]
    public async Task SiblingReject_DefaultCascade_ParentTerminates_ChildrenCancelled()
    {
        using var db = NewDb();
        Guid ub = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "pp", FlowName = "pp", FormKey = "f",
            SchemaJson = System.Text.Json.JsonSerializer.Serialize(ParallelParent(ub, "child")), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("pp", Guid.NewGuid(), "{}");
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);

        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tb.Id, ub, approve: false);   // B 支驳回 → 默认连坐 terminate → CancelAllActiveTokens 钩子级联

        Assert.Equal(FlowInstanceStatus.Rejected, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == child.Id)).Status);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == child.Id && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task CancelTokenSubtree_FifthClean_ParkedSubFlowToken_ChildrenCancelled_SiblingUntouched()
    {
        using var db = NewDb();
        Guid ub = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "pp", FlowName = "pp", FormKey = "f",
            SchemaJson = System.Text.Json.JsonSerializer.Serialize(ParallelParent(ub, "child")), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("pp", Guid.NewGuid(), "{}");
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        var parked = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);

        eng.CancelTokenSubtree(pid, parked.Id);   // 剥离层=停泊 subFlow token（二期 SameBranch 剥离形态,直调 internal）
        await db.SaveChangesAsync();

        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == child.Id)).Status);   // ★ 第五清
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == child.Id && t.Status == FlowTaskStatus.Pending));
        // 兄弟支 b 零扰动（二期 C-T2 不变量在第五清下保持）
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }
}
