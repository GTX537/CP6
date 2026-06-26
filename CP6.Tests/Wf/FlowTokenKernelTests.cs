using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class FlowTokenKernelTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    [Fact]
    public async Task FlowToken_Persists_WithStatusAndLineage()
    {
        using var db = NewDb();
        var instId = Guid.NewGuid();
        db.Wf_FlowTokens.Add(new Wf_FlowToken
        {
            Id = Guid.NewGuid(), InstanceId = instId, NodeId = "n1",
            Status = FlowTokenStatus.Active, ParentTokenId = null, ForkId = null,
        });
        await db.SaveChangesAsync();

        var tok = await db.Wf_FlowTokens.SingleAsync();
        Assert.Equal("n1", tok.NodeId);
        Assert.Equal(FlowTokenStatus.Active, tok.Status);
        Assert.Equal(0, FlowTokenStatus.Active);
        Assert.Equal(1, FlowTokenStatus.Consumed);
        Assert.Equal(2, FlowTokenStatus.Cancelled);
    }
}
