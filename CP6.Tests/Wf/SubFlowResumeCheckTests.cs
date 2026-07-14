using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>第二段复核 CheckSubFlowGroupAsync 计票语义（spec §3.2 表格逐行）。InternalsVisibleTo 直调；
/// 队列/fast path 接线面在 SubFlowTwoPhaseTests。</summary>
public class SubFlowResumeCheckTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static async Task<(CP6Context db, FlowEngine eng, Guid pid, Guid parkedTokenId, Guid pa, Guid ca)> SetupAsync(
        string? collectionVar = null, string? policy = null, string? varsIn = null, string? varsOut = null,
        bool errorEdge = false, string parentVars = "{}")
    {
        var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar, policy, varsIn, varsOut, errorEdge));
        await db.SaveChangesAsync();
        var eng = Engine(db);
        var pid = await eng.SubmitAsync("parent", Guid.NewGuid(), parentVars);
        var parked = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub" && t.Status == FlowTokenStatus.Active);
        return (db, eng, pid, parked.Id, pa, ca);
    }

    private static async Task ActChildAsync(CP6Context db, FlowEngine eng, Guid childId, Guid approver, bool approve)
    {
        var t = await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == childId && x.Status == FlowTaskStatus.Pending);
        await eng.ActAsync(t.Id, approver, approve);
    }

    [Fact]
    public async Task Single_ChildApproved_ResumesParent_MergesOutVar()
    {
        var (db, eng, pid, tok, pa, ca) = await SetupAsync(
            varsIn: "{\"result\":\"$.seed\"}", varsOut: "{\"subResult\":\"$.result\"}", parentVars: "{\"seed\":\"OK\"}");
        using var _ = db;
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        await ActChildAsync(db, eng, child.Id, ca, approve: true);

        await eng.CheckSubFlowGroupAsync(tok);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal("OK", JsonNode.Parse(inst.VarsJson)!["subResult"]!.GetValue<string>());   // 单实例=标量回注
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowResumed"));
    }

    [Fact]
    public async Task Single_ChildRejected_NoErrorEdge_ParentRejected()
    {
        var (db, eng, pid, tok, _, ca) = await SetupAsync();
        using var _1 = db;
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        await ActChildAsync(db, eng, child.Id, ca, approve: false);

        await eng.CheckSubFlowGroupAsync(tok);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
        var err = JsonNode.Parse(inst.VarsJson)!["subFlowError"]!;
        Assert.Equal(child.Id.ToString(), err["childInstanceId"]!.GetValue<string>());
        Assert.Equal(FlowInstanceStatus.Rejected, err["childStatus"]!.GetValue<int>());
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.InstanceId == pid && t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task Single_ChildRejected_ErrorEdge_RoutesErrBranch_ParentStillRunning()
    {
        var (db, eng, pid, tok, pa, ca) = await SetupAsync(errorEdge: true);
        using var _ = db;
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        await ActChildAsync(db, eng, child.Id, ca, approve: false);

        await eng.CheckSubFlowGroupAsync(tok);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.NodeId == "err" && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task All_N3_AllApproved_ArrayWritebackBySubIndex()
    {
        var (db, eng, pid, tok, pa, ca) = await SetupAsync(collectionVar: "items",
            varsIn: "{\"v\":\"$.item\"}", varsOut: "{\"results\":\"$.v\"}", parentVars: "{\"items\":[10,20,30]}");
        using var _ = db;
        var children = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync();
        // 乱序办结（2→0→1），回注仍按 SubIndex 排
        await ActChildAsync(db, eng, children[2].Id, ca, true);
        await eng.CheckSubFlowGroupAsync(tok);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa));   // 未齐不恢复

        await ActChildAsync(db, eng, children[0].Id, ca, true);
        await ActChildAsync(db, eng, children[1].Id, ca, true);
        await eng.CheckSubFlowGroupAsync(tok);

        var arr = (JsonArray)JsonNode.Parse((await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).VarsJson)!["results"]!;
        Assert.Equal(new[] { 10, 20, 30 }, arr.Select(x => x!.GetValue<int>()).ToArray());
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task All_OneRejected_CascadesSiblings_ErrorPath()
    {
        var (db, eng, pid, tok, _, ca) = await SetupAsync(collectionVar: "items", parentVars: "{\"items\":[1,2,3]}");
        using var _1 = db;
        var children = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync();
        await ActChildAsync(db, eng, children[1].Id, ca, approve: false);

        await eng.CheckSubFlowGroupAsync(tok);

        Assert.Equal(FlowInstanceStatus.Rejected, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
        // 附带动作：其余在途兄弟被级联撤回，其待办作废
        foreach (var sib in new[] { children[0], children[2] })
        {
            Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == sib.Id)).Status);
            Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == sib.Id && t.Status == FlowTaskStatus.Pending));
        }
    }

    [Fact]
    public async Task Any_FirstApproved_Resumes_WithdrawsRest_ScalarWriteback()
    {
        var (db, eng, pid, tok, pa, ca) = await SetupAsync(collectionVar: "items", policy: "any",
            varsIn: "{\"v\":\"$.item\"}", varsOut: "{\"winner\":\"$.v\"}", parentVars: "{\"items\":[\"x\",\"y\"]}");
        using var _ = db;
        var children = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync();
        await ActChildAsync(db, eng, children[1].Id, ca, approve: true);   // SubIndex=1 先过

        await eng.CheckSubFlowGroupAsync(tok);

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal("y", JsonNode.Parse(inst.VarsJson)!["winner"]!.GetValue<string>());   // any=仅首个 Approved 的值(标量)
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == children[0].Id)).Status);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task Any_AllRejected_ErrorPath()
    {
        var (db, eng, pid, tok, _, ca) = await SetupAsync(collectionVar: "items", policy: "any", parentVars: "{\"items\":[1,2]}");
        using var _1 = db;
        var children = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).ToListAsync();
        await ActChildAsync(db, eng, children[0].Id, ca, false);
        await eng.CheckSubFlowGroupAsync(tok);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);   // 任一驳不判死

        await ActChildAsync(db, eng, children[1].Id, ca, false);
        await eng.CheckSubFlowGroupAsync(tok);
        Assert.Equal(FlowInstanceStatus.Rejected, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);   // 全驳才错误处置
    }

    [Fact]
    public async Task ParentAlreadyTerminal_StateGate_ZeroAction()
    {
        var (db, eng, pid, tok, _, ca) = await SetupAsync();
        using var _1 = db;
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        inst.Status = FlowInstanceStatus.Withdrawn;   // 父已终态（模拟撤回竞态窗口）
        await db.SaveChangesAsync();
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        await ActChildAsync(db, eng, child.Id, ca, approve: true);

        await eng.CheckSubFlowGroupAsync(tok);   // 父实例状态闸 → 零动作

        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowResumed"));
        Assert.Equal(FlowInstanceStatus.Withdrawn, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).Status);
    }

    [Fact]
    public async Task TokenAlreadyResumed_LateCheck_ZeroAction()
    {
        var (db, eng, pid, tok, pa, ca) = await SetupAsync();
        using var _ = db;
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        await ActChildAsync(db, eng, child.Id, ca, approve: true);
        await eng.CheckSubFlowGroupAsync(tok);
        await eng.CheckSubFlowGroupAsync(tok);   // 迟到复核（重入）

        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowResumed"));
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.InstanceId == pid && t.NodeId == "pa"));   // 不双推进
    }
}
