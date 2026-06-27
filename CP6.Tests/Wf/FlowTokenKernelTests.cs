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

    [Fact]
    public async Task ReadModelTables_Persist()
    {
        using var db = NewDb();
        var inst = Guid.NewGuid();
        db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
        {
            Id = Guid.NewGuid(), InstanceId = inst, NodeId = "n1", StepSeq = 1,
            ExpectedHandlerId = Guid.NewGuid(), Status = FlowFormToStatus.Pending, SentAt = new DateTime(2026, 6, 26),
        });
        db.Wf_FlowDatas.Add(new Wf_FlowData { Id = Guid.NewGuid(), InstanceId = inst, NodeId = "n1", StepSeq = 1, DataJson = "{}" });
        db.Wf_FlowCcs.Add(new Wf_FlowCc { Id = Guid.NewGuid(), InstanceId = inst, RecipientId = Guid.NewGuid() });
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Wf_FlowFormTos.CountAsync());
        Assert.Equal(FlowFormToStatus.Pending, (await db.Wf_FlowFormTos.SingleAsync()).Status);
        Assert.Equal(1, await db.Wf_FlowDatas.CountAsync());
        Assert.False((await db.Wf_FlowCcs.SingleAsync()).IsRead);
    }

    [Fact]
    public void TokenPrimitives_SpawnConsumeDrain()
    {
        using var db = NewDb();
        var eng = new FlowEngine(db, new ApproverResolver(db));
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "f", StarterId = Guid.NewGuid(), Status = FlowInstanceStatus.Running, CurrentNode = "n1" };
        db.Wf_FlowInstances.Add(inst);

        var tok = eng.SpawnToken(inst, new FlowNode { Id = "n1" }, parent: null, fork: null);
        Assert.Equal(FlowTokenStatus.Active, tok.Status);
        Assert.Equal("n1", tok.NodeId);

        eng.FinishIfDrained(inst);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);   // 还有 Active → 不终态

        eng.ConsumeToken(tok);
        Assert.Equal(FlowTokenStatus.Consumed, tok.Status);
        eng.ConsumeToken(tok);                                   // 重放守卫：no-op
        Assert.Equal(FlowTokenStatus.Consumed, tok.Status);

        eng.FinishIfDrained(inst);
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);  // 无 Active → 通过
    }
}
