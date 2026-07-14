using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>spec §3.3/§7 退回重生防双批端到端定点（旧批取消+新批起+不并跑）。三测同覆：
/// ①BeforeSplit——跨 split 边界整块重来（全清场路径 CancelAllActiveTokens）；
/// ②SameBranch——嵌套并行内层子流程支被剥离层级联取消、外层兄弟支零扰动（CancelTokenSubtree 第五清路径）；
/// ③spec §7 组合语义：父 subFlow 在并行支 + onBranchReject=prune + 子驳无错边 → 剪父支不连坐。
/// C-T2 审查 Important 补缺：原 schema（sub 直挂 split）对 b→a1 恒判 BeforeSplit，构造不出 SameBranch；
/// 本嵌套 schema（sub 处内层分支域、退回目标 m 处外层同分支域）经 SendBackScopeAnalyzer 真判 SameBranch。</summary>
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

    /// <summary>嵌套并行父流：ps → split → ( m → split2 → (x审批, sub子流程) → join2 , b审批 ) → join → pe。
    /// sub 处内层分支域，退回目标 m 处外层同分支域（含 split2/x/sub/join2）。
    /// SendBackScopeAnalyzer 语义推演（x 所在 token 的 ForkStack=[内层split2, 外层split]）：
    /// ·内层 split2：域=[{x},{sub}]，mine={x}，目标 m 不在任何内层域 → 上探外层；
    /// ·外层 split：域=[{m,split2,x,sub,join2},{b}]，mine=含 x 的外层支域，其含 m → 判 SameBranch，
    ///   剥离层=外层 branch1 token。CancelTokenSubtree(branch1) 闭包含内层 x/sub 两 token，
    ///   sub 停泊 token 经第五清级联取消子实例；外层兄弟支 b 的 token 不在闭包 → 零扰动。</summary>
    private static FlowSchema NestedSameBranchParent(Guid um, Guid ux, Guid ub, string subFlowKey) => new()
    {
        Start = "ps",
        Nodes =
        {
            new FlowNode { Id = "ps", Type = "start" },
            new FlowNode { Id = "split", Type = "parallelSplit" },
            new FlowNode { Id = "m", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = um },
            new FlowNode { Id = "split2", Type = "parallelSplit" },
            new FlowNode { Id = "x", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ux },
            new FlowNode { Id = "sub", Type = "subFlow", SubFlowKey = subFlowKey },
            new FlowNode { Id = "join2", Type = "parallelJoin" },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "pe", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "ps", To = "split" },
            new FlowEdge { From = "split", To = "m" }, new FlowEdge { From = "split", To = "b" },
            new FlowEdge { From = "m", To = "split2" },
            new FlowEdge { From = "split2", To = "x" }, new FlowEdge { From = "split2", To = "sub" },
            new FlowEdge { From = "x", To = "join2" }, new FlowEdge { From = "sub", To = "join2" },
            new FlowEdge { From = "join2", To = "join" }, new FlowEdge { From = "b", To = "join" },
            new FlowEdge { From = "join", To = "pe" },
        },
    };

    /// <summary>spec §3.3/§5.2 SameBranch 端到端：嵌套并行内层子流程支退回 → 剥离层级联取消旧子实例、
    /// 外层兄弟支零扰动、重生恰一组新子实例（无并跑双批）、父实例仍 Running。
    /// 这条正是 C-T2 审查 Important 证明既有 BeforeSplitSendBack/直调 C-T1 单测都测不到的组合缺口。</summary>
    [Fact]
    public async Task SameBranchSendBack_StripLayerCascadesOldChild_SiblingUntouched_RebornSingleBatch_NoParallelRun()
    {
        using var db = NewDb();
        Guid um = Guid.NewGuid(), ux = Guid.NewGuid(), ub = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "sbnest", FlowName = "sbnest", FormKey = "f",
            SchemaJson = System.Text.Json.JsonSerializer.Serialize(NestedSameBranchParent(um, ux, ub, "child")), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("sbnest", Guid.NewGuid(), "{}");

        // 提交即外层 split 分叉：m 支待办 + b 支待办。批准 m → 进内层 split2 → x 待办 + sub 停泊子实例起。
        var tm = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == um && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tm.Id, um, approve: true);

        var oldSubToken = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);
        var oldChild = await db.Wf_FlowInstances.SingleAsync(i => i.ParentTokenId == oldSubToken.Id);
        // ① 退回前旧子实例 Running
        Assert.Equal(FlowInstanceStatus.Running, oldChild.Status);

        // 外层兄弟支 b 的凭据（退回后须证明零扰动）
        var bTokenBefore = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "b" && t.Status == FlowTokenStatus.Active);
        var bTaskBefore = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);

        // 从内层 x 支退回外层同分支域目标 m → SendBackScopeAnalyzer 判 SameBranch → 剥离外层 branch1 层。
        var tx = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ux && t.Status == FlowTaskStatus.Pending);
        await eng.SendBackAsync(tx.Id, ux, "m");

        // ② 旧子实例被剥离层第五清级联取消（非 BeforeSplit 全清场）；旧 sub token 亦 Cancelled
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == oldChild.Id)).Status);
        Assert.Equal(FlowTokenStatus.Cancelled, (await db.Wf_FlowTokens.SingleAsync(t => t.Id == oldSubToken.Id)).Status);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == oldChild.Id && t.Status == FlowTaskStatus.Pending));
        // ★ 外层兄弟支 b 零扰动（BeforeSplit 全清场会连坐取消它 → 此断言即 SameBranch 剥离路径的判别证据）
        Assert.Equal(FlowTokenStatus.Active, (await db.Wf_FlowTokens.SingleAsync(t => t.Id == bTokenBefore.Id)).Status);
        Assert.Equal(FlowTaskStatus.Pending, (await db.Wf_FlowTasks.SingleAsync(t => t.Id == bTaskBefore.Id)).Status);
        // ④ 父实例仍 Running
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);

        // 重批：reborn token 停泊 m → 再批准 → 重走 split2 → 恰一组新子实例（新槽不撞旧 SubIndex）。
        var tmb = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == um && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tmb.Id, um, approve: true);

        var newSubToken = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);
        Assert.NotEqual(oldSubToken.Id, newSubToken.Id);
        var newChild = await db.Wf_FlowInstances.SingleAsync(i => i.ParentTokenId == newSubToken.Id);
        Assert.Equal(FlowInstanceStatus.Running, newChild.Status);
        // ③ 不并跑：全库该父下在途子实例恰一个（旧批 Withdrawn 不复活），总数=2（1 Withdrawn + 1 Running）
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync(i => i.ParentInstanceId == pid && i.Status == FlowInstanceStatus.Running));
        Assert.Equal(2, await db.Wf_FlowInstances.CountAsync(i => i.ParentInstanceId == pid));

        // 新批走完全程 → 父正常通过（重生血缘无残留污染，内外层 join 认亲不破）
        var tNewChild = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == newChild.Id && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tNewChild.Id, ca, approve: true);
        var txb = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ux && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(txb.Id, ux, approve: true);
        var tbb = await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == pid && t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(tbb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }
}
