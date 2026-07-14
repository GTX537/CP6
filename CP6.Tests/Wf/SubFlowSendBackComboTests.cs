using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>spec §3.3 末两条定点：SameBranch/BeforeSplit 退回重生防双批（旧批取消+新批起+不并跑）；
/// spec §7 组合语义：父 subFlow 在并行支 + onBranchReject=prune + 子驳无错边 → 剪父支不连坐。</summary>
public class SubFlowSendBackComboTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    /// <summary>ps → a1(审批) → split(onBranchReject 可配) → ( sub , b ) → join → pe。</summary>
    private static FlowSchema SendBackParent(Guid ua, Guid ub, string subFlowKey, string? onBranchReject = null) => new()
    {
        Start = "ps",
        Nodes =
        {
            new FlowNode { Id = "ps", Type = "start" },
            new FlowNode { Id = "a1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
            new FlowNode { Id = "split", Type = "parallelSplit", OnBranchReject = onBranchReject },
            new FlowNode { Id = "sub", Type = "subFlow", SubFlowKey = subFlowKey },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "pe", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "ps", To = "a1" }, new FlowEdge { From = "a1", To = "split" },
            new FlowEdge { From = "split", To = "sub" }, new FlowEdge { From = "split", To = "b" },
            new FlowEdge { From = "sub", To = "join" }, new FlowEdge { From = "b", To = "join" },
            new FlowEdge { From = "join", To = "pe" },
        },
    };

    [Fact]
    public async Task BeforeSplitSendBack_OldBatchCancelled_ReapproveStartsNewBatch_NoParallelRun()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "sb", FlowName = "sb", FormKey = "f",
            SchemaJson = System.Text.Json.JsonSerializer.Serialize(SendBackParent(ua, ub, "child")), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("sb", Guid.NewGuid(), "{}");

        var ta1 = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(ta1.Id, ua, approve: true);   // 进并行块,sub 停泊 + 旧批子实例起

        var oldToken = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);
        var oldChild = await db.Wf_FlowInstances.SingleAsync(i => i.ParentTokenId == oldToken.Id);

        // B 支从 b 退回 a1（跨 split 边界=二期 BeforeSplit 整块重来,全清场路径）
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await eng.SendBackAsync(tb.Id, ub, "a1");

        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == oldChild.Id)).Status);   // 旧批死
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == oldChild.Id && t.Status == FlowTaskStatus.Pending));

        // 重批：a1 再过 → 重入 sub 是新 tokenId → (ParentTokenId,SubIndex) 按设计不撞 → 新批照起
        var ta1b = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(ta1b.Id, ua, approve: true);

        var newToken = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);
        Assert.NotEqual(oldToken.Id, newToken.Id);
        var newChild = await db.Wf_FlowInstances.SingleAsync(i => i.ParentTokenId == newToken.Id);
        Assert.Equal(FlowInstanceStatus.Running, newChild.Status);
        // ★ 不并跑：全库在途子实例恰一个（旧批 Withdrawn 不复活）
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync(i => i.ParentInstanceId == pid && i.Status == FlowInstanceStatus.Running));

        // 新批走完 → 父可正常通过（新批凭据链路无残留污染）
        var tNew = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == newChild.Id && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tNew.Id, ca, approve: true);
        var tb2 = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tb2.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }

    [Fact]
    public async Task ComboSemantics_SubFlowInParallelBranch_Prune_ChildReject_PrunesBranchOnly()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "combo", FlowName = "combo", FormKey = "f",
            SchemaJson = System.Text.Json.JsonSerializer.Serialize(SendBackParent(ua, ub, "child", onBranchReject: "prune")),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("combo", Guid.NewGuid(), "{}");
        var ta1 = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(ta1.Id, ua, approve: true);

        var subToken = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentTokenId == subToken.Id);

        // 子驳回 → 复核错误处置 → 无错边 → TryPruneBranch（split 配 prune）→ 只剪 sub 支
        var tc = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == child.Id && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tc.Id, ca, approve: false);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);                                    // ★ 不连坐
        Assert.Equal(FlowTokenStatus.Pruned, (await db.Wf_FlowTokens.SingleAsync(t => t.Id == subToken.Id)).Status);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));

        // B 支办结 → 动态计票放行（Pruned 从等待集消失,二期 D4）→ 实例通过
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }
}
