using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class QueryServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static IForecastService Forecast(CP6Context db) => new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db)));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db), Forecast(db));

    [Fact]
    public async Task Query_FiltersByStarterAndFlowKey()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = "s", NickName = "发起李", Password = "x" },
            new Sys_User { Id = approver, UserName = "a", Password = "x" });
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "请假", FormKey = "leave",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = { new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                          new FlowNode { Id = "end", Type = "end" } },
                Edges = { new FlowEdge { From = "n1", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("leave", starter, "{}");

        var hit = await Inbox(db).QueryAsync(new FormQueryFilter(starter, null, "leave", null, null, null, null));
        var item = Assert.Single(hit);
        Assert.Equal("发起李", item.StarterName);
        Assert.Equal("leave", item.FlowKey);

        var miss = await Inbox(db).QueryAsync(new FormQueryFilter(Guid.NewGuid(), null, null, null, null, null, null));
        Assert.Empty(miss);
    }
}
