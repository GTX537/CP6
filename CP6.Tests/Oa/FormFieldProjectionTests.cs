using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Oa;

public sealed class FormFieldProjectionTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Hidden_is_physically_removed_and_readonly_remains_visible()
    {
        await using var db = NewDb();
        var viewer = Guid.NewGuid();
        var flowHead = new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "f", FlowName = "F" };
        var formHead = new Wf_FormDef { Id = Guid.NewGuid(), FormKey = "form", FormName = "Form" };
        var flowVersion = new Wf_FlowDefVersion
        {
            Id = Guid.NewGuid(), FlowDefId = flowHead.Id, Version = 1, Status = 1,
            FlowNameSnapshot = "F",
            SchemaJson = """{"nodes":[{"id":"approve","type":"approval","fieldPerms":{"secret":"hidden","requester":"readonly","amount":"edit"}}],"edges":[]}"""
        };
        var formVersion = new Wf_FormDefVersion
        {
            Id = Guid.NewGuid(), FormDefId = formHead.Id, Version = 1, Status = 1,
            FormNameSnapshot = "Form",
            SchemaJson = """{"fields":[{"name":"secret","type":"input"},{"name":"requester","type":"input"},{"name":"amount","type":"number"}]}"""
        };
        var data = new Wf_FormData
        {
            Id = Guid.NewGuid(), FormDefVersionId = formVersion.Id, FormKey = "form",
            FormVersion = 1, DataJson = """{"secret":"classified","requester":"A","amount":2}"""
        };
        var instance = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = "f", FlowDefVersionId = flowVersion.Id,
            FormDefVersionId = formVersion.Id, FormDataId = data.Id, StarterId = Guid.NewGuid(),
            Status = FlowInstanceStatus.Running, CurrentNode = "approve", VarsJson = data.DataJson
        };
        db.AddRange(flowHead, formHead, flowVersion, formVersion, data, instance);
        db.Wf_FlowTasks.Add(new Wf_FlowTask
        {
            Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "approve",
            AssigneeId = viewer, Status = FlowTaskStatus.Pending
        });
        await db.SaveChangesAsync();

        var result = await new FormFieldProjectionService(db)
            .ProjectAsync(instance.Id, viewer, data.DataJson);

        Assert.DoesNotContain("secret", result.SchemaJson);
        Assert.DoesNotContain("classified", result.DataJson);
        Assert.Equal("readonly", result.FieldMask["requester"]);
        Assert.Equal("edit", result.FieldMask["amount"]);

        var task = await db.Wf_FlowTasks.SingleAsync();
        task.Status = FlowTaskStatus.Approved;
        db.Wf_FlowFormTos.Add(new Wf_FlowFormTo
        {
            Id = Guid.NewGuid(), InstanceId = instance.Id, NodeId = "approve",
            ExpectedHandlerId = viewer, ActualHandlerId = viewer,
            Status = FlowFormToStatus.Approved, SentAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var historical = await new FormFieldProjectionService(db)
            .ProjectAsync(instance.Id, viewer, data.DataJson);
        Assert.Equal("readonly", historical.FieldMask["amount"]);
    }

    [Fact]
    public async Task Legacy_without_pinned_schema_exposes_no_raw_data()
    {
        await using var db = NewDb();
        var starter = Guid.NewGuid();
        var instance = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = "legacy", StarterId = starter,
            VarsJson = """{"secret":"must-not-leak"}"""
        };
        db.Wf_FlowInstances.Add(instance);
        await db.SaveChangesAsync();

        var result = await new FormFieldProjectionService(db)
            .ProjectAsync(instance.Id, starter, instance.VarsJson);

        Assert.True(result.LegacyFallback);
        Assert.Equal("{}", result.DataJson);
        Assert.DoesNotContain("must-not-leak", result.SchemaJson);
    }
}
