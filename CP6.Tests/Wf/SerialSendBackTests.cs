using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

public class SerialSendBackTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Eng(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task SeedLinear3(CP6Context db, Guid a, Guid b, Guid c)
    {
        var schema = new FlowSchema { Start = "n1", Nodes =
        {
            new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = a },
            new FlowNode { Id = "n2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = b },
            new FlowNode { Id = "n3", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = c },
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new(){From="n1",To="n2"}, new(){From="n2",To="n3"}, new(){From="n3",To="end"} } };
        db.Wf_FlowDefs.Add(new() { Id = Guid.NewGuid(), FlowKey = "lin3", FlowName = "x", FormKey = "t",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SendBackNode_ToUpstream_Works()
    {
        using var db = Db();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        await SeedLinear3(db, a, b, c);
        var instId = await Eng(db).SubmitAsync("lin3", Guid.NewGuid(), "{}");
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Eng(db).ActAsync(t1.Id, a, true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending);

        await Eng(db).SendBackAsync(t2.Id, b, new SendBackTarget("node", "n1"));
        Assert.Equal("n1", (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).CurrentNode);
    }

    [Fact]
    public async Task SendBackNode_ToDownstreamOrSelf_Throws_E_WF_012()
    {
        using var db = Db();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        await SeedLinear3(db, a, b, c);
        await Eng(db).SubmitAsync("lin3", Guid.NewGuid(), "{}");
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Eng(db).ActAsync(t1.Id, a, true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending);

        var e1 = await Assert.ThrowsAsync<InvalidOperationException>(() => Eng(db).SendBackAsync(t2.Id, b, new SendBackTarget("node", "n3")));
        Assert.Contains("E-WF-012", e1.Message);
        var e2 = await Assert.ThrowsAsync<InvalidOperationException>(() => Eng(db).SendBackAsync(t2.Id, b, new SendBackTarget("node", "n2")));
        Assert.Contains("E-WF-012", e2.Message);
    }
}
