using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Pur;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Oa;

public sealed class ApprovalPanelServiceTests
{
    private sealed class AllowBusiness : IApprovalBusinessAccessAuthorizer
    {
        public string BizType => "PUR_PR";
        public Task<BusinessApprovalAccess> AuthorizeAsync(
            string bizId, UserPermissionContext permission, CancellationToken ct = default) =>
            Task.FromResult(new BusinessApprovalAccess(PrStatus.Submitted.ToString(), false));
    }

    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task P0_AC_P13_NonParticipantCannotReadPanelEvenWithBusinessAccess()
    {
        using var db = Db();
        var instance = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = "pr", BizType = "PUR_PR", BizId = "PR1",
            StarterId = Guid.NewGuid(), Status = FlowInstanceStatus.Running,
        };
        db.Wf_FlowInstances.Add(instance);
        await db.SaveChangesAsync();
        var service = new ApprovalPanelService(db,
            new OaInstanceAccessService(db, new DelegateService(db)), new[] { new AllowBusiness() });

        var unrelated = Guid.NewGuid();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(
            "PUR_PR", "PR1", unrelated, unrelated,
            new UserPermissionContext(), default));
    }

    [Fact]
    public async Task AuthorizedPanelProjectsStableTaskTimelineAndInternalDetailRoute()
    {
        using var db = Db();
        var starter = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var instance = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = "pr", BizType = "PUR_PR", BizId = "PR/1",
            StarterId = starter, Status = FlowInstanceStatus.Running,
        };
        db.Wf_FlowInstances.Add(instance);
        db.Wf_FlowTasks.Add(new Wf_FlowTask
        {
            InstanceId = instance.Id, NodeId = "approve", AssigneeId = approver,
            Status = FlowTaskStatus.Pending,
        });
        db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
        {
            InstanceId = instance.Id, NodeId = "approve", ExpectedHandlerId = approver,
            Status = FlowFormToStatus.Pending, StepSeq = 1, SentAt = DateTime.UtcNow,
        });
        db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding
        {
            BizType = "PUR_PR", FlowKey = "pr", Enable = false,
            DetailRoute = "/pur/pr?prNo={bizId}",
        });
        await db.SaveChangesAsync();
        var service = new ApprovalPanelService(db,
            new OaInstanceAccessService(db, new DelegateService(db)), new[] { new AllowBusiness() });

        var panel = await service.GetAsync("PUR_PR", "PR/1", approver, approver,
            new UserPermissionContext { UserId = approver });

        Assert.Equal(instance.Id, panel.InstanceId);
        Assert.Equal(new[] { "approve", "reject" }, panel.MyTask!.Actions);
        Assert.Single(panel.Timeline);
        Assert.Equal("/pur/pr?prNo=PR%2F1", panel.DetailRoute);
    }
}
