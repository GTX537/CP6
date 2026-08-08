using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

public class DefinitionImmutabilityTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Direct_EF_update_of_published_flow_version_is_rejected()
    {
        await using var db = NewDb();
        var head = new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "f", FlowName = "F" };
        var version = new Wf_FlowDefVersion
        {
            Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 1,
            Status = WfDefinitionVersionStatus.Published, FlowNameSnapshot = "F", SchemaJson = "{}"
        };
        db.AddRange(head, version);
        await db.SaveChangesAsync();

        version.SchemaJson = """{"changed":true}""";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Equal("E-WF-037", ex.Message);
    }

    [Fact]
    public async Task Direct_EF_delete_of_published_form_version_is_rejected()
    {
        await using var db = NewDb();
        var head = new Wf_FormDef { Id = Guid.NewGuid(), FormKey = "f", FormName = "F" };
        var version = new Wf_FormDefVersion
        {
            Id = Guid.NewGuid(), FormDefId = head.Id, Version = 1,
            Status = WfDefinitionVersionStatus.Published, FormNameSnapshot = "F", SchemaJson = "{}"
        };
        db.AddRange(head, version);
        await db.SaveChangesAsync();

        db.Remove(version);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Equal("E-WF-037", ex.Message);
    }

    [Fact]
    public async Task Draft_to_published_transition_is_allowed_once()
    {
        await using var db = NewDb();
        var head = new Wf_FormDef { Id = Guid.NewGuid(), FormKey = "f", FormName = "F" };
        var version = new Wf_FormDefVersion
        {
            Id = Guid.NewGuid(), FormDefId = head.Id, Version = 1,
            Status = WfDefinitionVersionStatus.Draft, FormNameSnapshot = "F", SchemaJson = "{}"
        };
        db.AddRange(head, version);
        await db.SaveChangesAsync();

        version.Status = WfDefinitionVersionStatus.Published;
        await db.SaveChangesAsync();
        Assert.Equal(WfDefinitionVersionStatus.Published, version.Status);
    }
}
