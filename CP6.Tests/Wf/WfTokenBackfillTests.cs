using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class WfTokenBackfillTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Backfill_CreatesRootToken_AndIdempotent()
    {
        using var db = NewDb();
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "f", StarterId = Guid.NewGuid(),
            Status = FlowInstanceStatus.Running, CurrentNode = "n1" };
        db.Wf_FlowInstances.Add(inst);
        db.Wf_FlowTasks.Add(new Wf_FlowTask { Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = "n1",
            AssigneeId = Guid.NewGuid(), Status = FlowTaskStatus.Pending, TokenId = null });
        await db.SaveChangesAsync();

        await WfTokenBackfillSeed.EnsureAsync(db);
        var tok = await db.Wf_FlowTokens.SingleAsync();
        Assert.Equal("n1", tok.NodeId);
        Assert.Equal(FlowTokenStatus.Active, tok.Status);
        Assert.Equal(tok.Id, (await db.Wf_FlowTasks.SingleAsync()).TokenId);

        await WfTokenBackfillSeed.EnsureAsync(db);                  // 重跑幂等
        Assert.Equal(1, await db.Wf_FlowTokens.CountAsync());
    }
}
