using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Oa;

public class SerialInboxDtoTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static IForecastService Forecast(CP6Context db) =>
        new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db)));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db), Forecast(db));

    [Fact]
    public async Task PendingItem_CarriesStageFields_AndCanSendBackPrevStage()
    {
        using var db = Db();
        Guid s1 = Guid.NewGuid(), s2 = Guid.NewGuid();
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s1, Countersign="all", Name="档1" },
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s2, Countersign="all", Name="档2" },
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new(){From="ap",To="end"} } };
        db.Wf_FlowDefs.Add(new() { Id=Guid.NewGuid(), FlowKey="ser2", FlowName="x", FormKey="t",
            SchemaJson=JsonSerializer.Serialize(schema), Version=1, Enable=true });
        await db.SaveChangesAsync();

        var instId = await Engine(db).SubmitAsync("ser2", Guid.NewGuid(), "{}");
        var p0 = (await Inbox(db).PendingAsync(s1)).Single(i => i.InstanceId == instId);
        Assert.Equal(0, p0.StageIndex);
        Assert.False(p0.CanSendBackPrevStage);

        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex == 0 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t0.Id, s1, true);
        var p1 = (await Inbox(db).PendingAsync(s2)).Single(i => i.InstanceId == instId);
        Assert.Equal(1, p1.StageIndex);
        Assert.True(p1.CanSendBackPrevStage);
        Assert.Equal("档2", p1.StageName);
    }
}
