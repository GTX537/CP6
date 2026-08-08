using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

public class SubFlowInboxDetailTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    // 以现签名为准补齐：InboxService(db, IFlowEngine, IForecastService)；ForecastService(db, resolver, planner)。
    private static InboxService Inbox(CP6Context db) => new(db, Engine(db),
        new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db))));

    [Fact]
    public async Task Detail_ParentAndChildren_BothDirectionsLinked()
    {
        using var db = NewDb();
        Guid pa = Guid.NewGuid(), ca = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(ca));
        SeedDef(db, "parent", ParentSchema(pa, "child", collectionVar: "items"));
        await db.SaveChangesAsync();
        var starter = Guid.NewGuid();
        var pid = await Engine(db).SubmitAsync("parent", starter, "{\"items\":[1,2]}");
        var kids = await db.Wf_FlowInstances.Where(i => i.ParentInstanceId == pid).OrderBy(i => i.SubIndex).ToListAsync();

        var svc = Inbox(db);

        var parentDetail = await svc.DetailAsync(starter, starter, pid);
        Assert.NotNull(parentDetail);
        Assert.Null(parentDetail!.SubFlowParent);                      // 顶层实例无父链
        Assert.NotNull(parentDetail.SubFlows);
        Assert.Equal(2, parentDetail.SubFlows!.Count);
        Assert.Equal(new[] { 0, 1 }, parentDetail.SubFlows.Select(s => s.SubIndex).ToArray());
        Assert.All(parentDetail.SubFlows, s => Assert.Equal("sub", s.NodeId));
        Assert.All(parentDetail.SubFlows, s => Assert.Equal("child", s.FlowKey));

        var childDetail = await svc.DetailAsync(starter, starter, kids[0].Id);
        Assert.NotNull(childDetail!.SubFlowParent);
        Assert.Equal(pid, childDetail.SubFlowParent!.InstanceId);
        Assert.Equal("parent", childDetail.SubFlowParent.FlowKey);
    }

    [Fact]
    public async Task Detail_PlainInstance_NullBothWays()
    {
        using var db = NewDb();
        Guid ca = Guid.NewGuid();
        SeedDef(db, "plain", ChildSchema(ca));
        await db.SaveChangesAsync();
        var starter = Guid.NewGuid();
        var id = await Engine(db).SubmitAsync("plain", starter, "{}");
        var svc = Inbox(db);
        var d = await svc.DetailAsync(starter, starter, id);
        Assert.Null(d!.SubFlowParent);
        Assert.True(d.SubFlows is null || d.SubFlows.Count == 0);
    }

    [Fact]
    public async Task Detail_omits_parent_link_when_parent_is_not_independently_authorized()
    {
        using var db = NewDb();
        var parentApprover = Guid.NewGuid();
        var childApprover = Guid.NewGuid();
        SeedDef(db, "child", ChildSchema(childApprover));
        SeedDef(db, "parent", ParentSchema(parentApprover, "child"));
        await db.SaveChangesAsync();
        var parentId = await Engine(db).SubmitAsync("parent", Guid.NewGuid(), "{}");
        var child = await db.Wf_FlowInstances.SingleAsync(x => x.ParentInstanceId == parentId);
        child.StarterId = Guid.NewGuid();
        await db.SaveChangesAsync();

        var detail = await Inbox(db).DetailAsync(childApprover, childApprover, child.Id);

        Assert.Null(detail!.SubFlowParent);
    }
}
