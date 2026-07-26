using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CP6.Tests.Wf.SubFlowTestHarness;

namespace CP6.Tests.Wf;

public sealed class SubFlowDefinitionPinTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Published_parent_keeps_child_dependency_version()
    {
        await using var db = NewDb();
        var definitions = new FlowDefService(db);
        var childV1Approver = Guid.NewGuid();
        var childV2Approver = Guid.NewGuid();
        var parentApprover = Guid.NewGuid();

        var childDraft1 = await definitions.SaveDraftAsync(
            "child", "Child", null, JsonSerializer.Serialize(ChildSchema(childV1Approver)), null);
        var childV1 = await definitions.PublishAsync("child", childDraft1.RowVersion, Guid.NewGuid());

        var parentDraft1 = await definitions.SaveDraftAsync(
            "parent", "Parent", null, JsonSerializer.Serialize(ParentSchema(parentApprover, "child")), null);
        var parentV1 = await definitions.PublishAsync("parent", parentDraft1.RowVersion, Guid.NewGuid());

        var childDraft2 = await definitions.SaveDraftAsync(
            "child", "Child v2", null, JsonSerializer.Serialize(ChildSchema(childV2Approver)), null);
        var childV2 = await definitions.PublishAsync("child", childDraft2.RowVersion, Guid.NewGuid());

        var engine = new FlowEngine(db, new ApproverResolver(db));
        var oldParentId = await engine.SubmitAsync("parent", Guid.NewGuid(), "{}");
        var oldChild = await db.Wf_FlowInstances.SingleAsync(x => x.ParentInstanceId == oldParentId);
        Assert.Equal(parentV1.VersionId,
            (await db.Wf_FlowInstances.SingleAsync(x => x.Id == oldParentId)).FlowDefVersionId);
        Assert.Equal(childV1.VersionId, oldChild.FlowDefVersionId);
        Assert.Equal(childV1Approver,
            (await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == oldChild.Id)).AssigneeId);

        var parentDraft2 = await definitions.SaveDraftAsync(
            "parent", "Parent v2", null, JsonSerializer.Serialize(ParentSchema(parentApprover, "child")), null);
        await definitions.PublishAsync("parent", parentDraft2.RowVersion, Guid.NewGuid());
        var newParentId = await engine.SubmitAsync("parent", Guid.NewGuid(), "{}");
        var newChild = await db.Wf_FlowInstances.SingleAsync(x => x.ParentInstanceId == newParentId);
        Assert.Equal(childV2.VersionId, newChild.FlowDefVersionId);
        Assert.Equal(childV2Approver,
            (await db.Wf_FlowTasks.SingleAsync(x => x.InstanceId == newChild.Id)).AssigneeId);
    }
}
