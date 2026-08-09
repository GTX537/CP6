using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Oa;

public sealed class OaInstanceAccessServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Participant_union_is_fail_closed_and_acting_as_uses_effective_identity()
    {
        await using var db = NewDb();
        var starter = Guid.NewGuid();
        var handler = Guid.NewGuid();
        var cc = Guid.NewGuid();
        var unrelated = Guid.NewGuid();
        var agent = Guid.NewGuid();
        var instance = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = "leave", StarterId = starter,
            Status = FlowInstanceStatus.Running
        };
        db.Wf_FlowInstances.Add(instance);
        db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
        {
            Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "approve",
            ExpectedHandlerId = handler, SentAt = DateTime.UtcNow
        });
        db.Wf_FlowCcs.Add(new Wf_FlowCc
        {
            Id = Guid.NewGuid(), InstanceId = instance.Id, RecipientId = cc
        });
        db.Wf_FlowDelegates.Add(new Wf_FlowDelegate
        {
            Id = Guid.NewGuid(), GrantorId = handler, DelegateId = agent, Enable = true,
            ValidFrom = DateTime.Now.AddMinutes(-1), ValidTo = DateTime.Now.AddMinutes(10)
        });
        await db.SaveChangesAsync();

        var service = new OaInstanceAccessService(db, new DelegateService(db));
        Assert.Contains(instance.Id, await service.VisibleInstanceIds(starter).ToListAsync());
        Assert.Contains(instance.Id, await service.VisibleInstanceIds(handler).ToListAsync());
        Assert.Contains(instance.Id, await service.VisibleInstanceIds(cc).ToListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetAsync(unrelated, unrelated, instance.Id));
        Assert.True((await service.GetAsync(agent, handler, instance.Id)).CanRead);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(unrelated, handler, instance.Id));
    }
}
