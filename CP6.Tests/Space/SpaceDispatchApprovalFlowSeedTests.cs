using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Space;

public sealed class SpaceDispatchApprovalFlowSeedTests
{
    [Fact]
    public void Seed_is_idempotent_published_and_bound_to_a_separate_approver_step()
    {
        using var db = NewDb();
        var approverId = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User
        {
            Id = approverId,
            UserName = "dispatch-approver",
            Password = "x",
            Enable = true,
        });
        db.SaveChanges();

        SpaceDispatchApprovalFlowSeed.Seed(db, approverId);
        SpaceDispatchApprovalFlowSeed.Seed(db, approverId);

        var flow = Assert.Single(db.Wf_FlowDefs);
        Assert.Equal("space-dispatch-assignment", flow.FlowKey);
        Assert.Equal("SpaceDispatchAssignment", flow.FormKey);
        Assert.True(flow.Enable);
        var schema = JsonSerializer.Deserialize<FlowSchema>(flow.SchemaJson);
        Assert.NotNull(schema);
        var approval = Assert.Single(schema!.Nodes, value => value.Type == "approval");
        Assert.Equal("Specified", approval.ApproverStrategy);
        Assert.Equal(approverId, approval.ApproverUserId);

        var version = Assert.Single(db.Wf_FlowDefVersions);
        Assert.Equal(flow.Id, version.FlowDefId);
        Assert.Equal(WfDefinitionVersionStatus.Published, version.Status);

        var binding = Assert.Single(db.Wf_ApprovalBindings);
        Assert.Equal("SPACE_DISPATCH_ASSIGNMENT", binding.BizType);
        Assert.Equal(flow.FlowKey, binding.FlowKey);
        Assert.Equal("/space/viewer", binding.DetailRoute);
        Assert.True(binding.Enable);
    }

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(value =>
                value.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }
}
