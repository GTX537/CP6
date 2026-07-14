using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

/// <summary>SubFlowNodeHandler OnEnter 行为（spec §3.1）：起子/停泊/回指回填/多实例/空集直通/N 上限/深度守卫。
/// InMemory 基座（单线程行为面）；并发面在 SubFlowConcurrencyTests(SQLite)。</summary>
public class SubFlowHandlerTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task SingleInstance_ParksParent_SpawnsChild_BackfillsPointers()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid(), starter = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", varsIn: "{\"result\":\"$.seed\"}"));
        await db.SaveChangesAsync();

        var pid = await Engine(db).SubmitAsync("parent", starter, "{\"seed\":\"OK\"}");

        // 父 token 停泊在 sub（Active 不动）
        var parked = await db.Wf_FlowTokens.SingleAsync(t => t.InstanceId == pid && t.NodeId == "sub");
        Assert.Equal(FlowTokenStatus.Active, parked.Status);
        // 子实例：回指三列 + 变量映射 + 独立收件箱待办
        var child = await db.Wf_FlowInstances.SingleAsync(i => i.ParentInstanceId == pid);
        Assert.Equal(parked.Id, child.ParentTokenId);
        Assert.Equal(0, child.SubIndex);
        Assert.Equal(starter, child.StarterId);
        Assert.Equal("OK", JsonNode.Parse(child.VarsJson)!["result"]!.GetValue<string>());
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == child.Id && t.AssigneeId == ca && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowStarted"));
        // 父审批人此刻不应有待办（父没推进）
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa));
    }

    [Fact]
    public async Task Multi_N3_SpawnsThree_WithItemVars()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items"));
        await db.SaveChangesAsync();

        var pid = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), "{\"items\":[\"a\",\"b\",\"c\"]}");

        var children = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync();
        Assert.Equal(3, children.Count);
        Assert.Equal(new[] { 0, 1, 2 }, children.Select(c => c.SubIndex!.Value).ToArray());
        for (int i = 0; i < 3; i++)
        {
            var o = JsonNode.Parse(children[i].VarsJson)!.AsObject();
            Assert.Equal(new[] { "a", "b", "c" }[i], o["item"]!.GetValue<string>());
            Assert.Equal(i, o["itemIndex"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task EmptyCollection_PassThrough_NoChildren_NoWriteback()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items", varsOut: "{\"r\":\"$.v\"}"));
        await db.SaveChangesAsync();

        var pid = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), "{\"items\":[]}");

        Assert.False(await db.Wf_FlowInstances.AnyAsync(i => i.ParentInstanceId == pid));
        // 直接沿非错误出边前进：父审批人有待办
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == pa && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.InstanceId == pid && h.Action == "subFlowEmptyCollection"));
        Assert.False(JsonNode.Parse((await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid)).VarsJson)!.AsObject().ContainsKey("r"));   // 不回注
    }

    [Fact]
    public async Task OverCap_ErrorEdge_E_WF_025_NoChildren()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid(), errU = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items", errorEdge: true, errApprover: errU));
        await db.SaveChangesAsync();

        // 上限 2 的 handler（DI 读 Wfs:SubFlowMaxInstances 的等价注入面），集合给 3 → E-WF-025 错误处置走错边
        var handlers = new INodeHandler[]
        {
            new StartNodeHandler(), new ApprovalNodeHandler(), new EndNodeHandler(),
            new ParallelSplitNodeHandler(), new ParallelJoinNodeHandler(),
            new ServiceTaskNodeHandler(Array.Empty<IServiceTaskExecutor>()),
            new InclusiveSplitNodeHandler(), new InclusiveJoinNodeHandler(),
            new SubFlowNodeHandler(2),
        };
        var eng = new FlowEngine(db, new ApproverResolver(db), handlers: handlers);
        var pid = await eng.SubmitAsync("parent", Guid.NewGuid(), "{\"items\":[1,2,3]}");

        Assert.False(await db.Wf_FlowInstances.AnyAsync(i => i.ParentInstanceId == pid));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.InstanceId == pid && t.AssigneeId == errU && t.Status == FlowTaskStatus.Pending));
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal("E-WF-025", JsonNode.Parse(inst.VarsJson)!["subFlowError"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task CollectionNotArray_ErrorDisposition_E_WF_025()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items"));   // 无错边 → 传播父驳回
        await db.SaveChangesAsync();

        var pid = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), "{\"items\":\"not-an-array\"}");

        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == pid);
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
        Assert.Equal("E-WF-025", JsonNode.Parse(inst.VarsJson)!["subFlowError"]!["code"]!.GetValue<string>());
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.InstanceId == pid && t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task DepthGuard_ChainOf10_Throws_E_WF_026()
    {
        using var db = NewDb();
        Guid u = Guid.NewGuid();
        // d9 是叶子审批流；d0..d8 逐层引用下一层。提交 d0 递归起子至 d8 实例（祖先数=8）
        // → 其 subFlow handler 深度守卫 ++depth 达 8 → 抛（spec §3.1「≥8 层」；绕过保存时校验直插 def 模拟发布后新环/深链）
        SeedDef(db, "d9", ChildSchema(u));
        for (int i = 8; i >= 0; i--)
            SeedDef(db, $"d{i}", ParentSchema(u, $"d{i + 1}"));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).SubmitAsync("d0", Guid.NewGuid(), "{}"));
        Assert.Contains("E-WF-026", ex.Message);
    }
}
