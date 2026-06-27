using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class ActAsServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task<Guid> SeedAsync(CP6Context db, Guid starter, Guid grantor)
    {
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = "s", Password = "x" },
            new Sys_User { Id = grantor, UserName = "g", NickName = "被代理X", Password = "x" });
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "leave", FlowName = "请假", FormKey = "leave",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = { new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = grantor },
                          new FlowNode { Id = "end", Type = "end" } },
                Edges = { new FlowEdge { From = "n1", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("leave", starter, "{}");
        return (await db.Wf_FlowTasks.Where(t => t.Status == FlowTaskStatus.Pending).Select(t => t.Id).SingleAsync());
    }

    [Fact]
    public async Task ActAs_RecordsActualHandler_AndOnBehalfOf()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var grantor = Guid.NewGuid(); var me = Guid.NewGuid();
        var task = await SeedAsync(db, starter, grantor);

        // me 代 grantor 批准（task 归属 grantor）
        await Engine(db).ActAsAsync(task, actorId: me, onBehalfOf: grantor, approve: true, "代签同意");

        var t = await db.Wf_FlowTasks.SingleAsync(x => x.Id == task);
        Assert.Equal(FlowTaskStatus.Approved, t.Status);            // 任务正常办结
        var row = await db.Wf_FlowFormTos.SingleAsync(f => f.ExpectedHandlerId == grantor);
        Assert.Equal(FlowFormToStatus.Approved, row.Status);
        Assert.Equal(me, row.ActualHandlerId);                     // 实办=代理人本人
        Assert.Equal(grantor, row.OnBehalfOfId);                   // 代谁签=被代理人
    }

    [Fact]
    public async Task ActAs_NullOnBehalf_EquivalentToActAsync()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var grantor = Guid.NewGuid();
        var task = await SeedAsync(db, starter, grantor);

        await Engine(db).ActAsAsync(task, actorId: grantor, onBehalfOf: null, approve: true, null);
        var row = await db.Wf_FlowFormTos.SingleAsync(f => f.ExpectedHandlerId == grantor);
        Assert.Equal(grantor, row.ActualHandlerId);
        Assert.Null(row.OnBehalfOfId);
    }
}
