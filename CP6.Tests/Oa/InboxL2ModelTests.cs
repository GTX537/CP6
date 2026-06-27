using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class InboxL2ModelTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Task_IsRead_DefaultsFalse_AndPersists()
    {
        using var db = NewDb();
        var t = new Wf_FlowTask { Id = Guid.NewGuid(), InstanceId = Guid.NewGuid(), NodeId = "n1",
            AssigneeId = Guid.NewGuid(), Status = FlowTaskStatus.Pending };
        db.Wf_FlowTasks.Add(t);
        await db.SaveChangesAsync();

        var got = await db.Wf_FlowTasks.SingleAsync();
        Assert.False(got.IsRead);
        Assert.Null(got.ReadAt);

        got.IsRead = true; got.ReadAt = new DateTime(2026, 6, 27);
        await db.SaveChangesAsync();
        Assert.True((await db.Wf_FlowTasks.SingleAsync()).IsRead);
    }

    [Fact]
    public void FlowInstanceStatus_HasDraft()
    {
        Assert.Equal(5, FlowInstanceStatus.Draft);
        Assert.NotEqual(FlowInstanceStatus.Draft, FlowInstanceStatus.Running);
    }
}
