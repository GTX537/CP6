using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class DraftServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task SeedFlowAsync(CP6Context db, Guid approver, string key = "leave")
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = key, FlowName = key, FormKey = key,
            SchemaJson = JsonSerializer.Serialize(new FlowSchema
            {
                Nodes =
                {
                    new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                    new FlowNode { Id = "end", Type = "end" },
                },
                Edges = { new FlowEdge { From = "n1", To = "end" } },
            }),
            Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task StartDraftAsync_DraftInstance_EntersFlow()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid(); var starter = Guid.NewGuid();
        await SeedFlowAsync(db, approver);

        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "leave", StarterId = starter,
            Status = FlowInstanceStatus.Draft, CurrentNode = "", VarsJson = """{"days":2}""", Creator = starter.ToString() };
        db.Wf_FlowInstances.Add(inst);
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync());

        await Engine(db).StartDraftAsync(inst.Id, starter);

        var got = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Running, got.Status);
        Assert.Equal("n1", got.CurrentNode);
        Assert.Equal(1, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == approver && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(1, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Pending));
    }

    [Fact]
    public async Task StartDraftAsync_NotOwner_Throws()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid(); var starter = Guid.NewGuid();
        await SeedFlowAsync(db, approver);
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "leave", StarterId = starter,
            Status = FlowInstanceStatus.Draft, CurrentNode = "", VarsJson = "{}", Creator = starter.ToString() };
        db.Wf_FlowInstances.Add(inst);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).StartDraftAsync(inst.Id, Guid.NewGuid()));
        Assert.Equal("E-WF-003", ex.Message);
    }
}
