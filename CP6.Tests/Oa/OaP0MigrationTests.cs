using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Oa;

public class OaP0MigrationTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Backfill_is_idempotent_and_pins_recoverable_rows()
    {
        await using var db = NewDb();
        var flow = new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "leave-flow", FlowName = "Leave",
            FormKey = "leave", Version = 3, SchemaJson = """{"nodes":[],"edges":[]}"""
        };
        var form = new Wf_FormDef
        {
            Id = Guid.NewGuid(), FormKey = "leave", FormName = "Leave form",
            Version = 2, SchemaJson = """{"fields":[]}"""
        };
        var running = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = flow.FlowKey, StarterId = Guid.NewGuid(),
            Status = FlowInstanceStatus.Running
        };
        var formData = new Wf_FormData
        {
            Id = Guid.NewGuid(), FormKey = form.FormKey, FormVersion = form.Version, DataJson = "{}"
        };
        var legacyDraft = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = flow.FlowKey, StarterId = Guid.NewGuid(),
            Status = FlowInstanceStatus.Draft, VarsJson = """{"reason":"x"}"""
        };
        db.AddRange(flow, form, running, formData, legacyDraft);
        await db.SaveChangesAsync();

        var service = new OaP0MigrationService(db);
        var first = await service.BackfillAsync();
        db.ChangeTracker.Clear();
        var second = await service.BackfillAsync();

        Assert.Equal(1, first.FlowVersions.Inserted);
        Assert.Equal(1, first.FormVersions.Inserted);
        Assert.Equal(1, first.Bindings.Inserted);
        Assert.Equal(1, first.Drafts.Inserted);
        Assert.Equal(0, second.FlowVersions.Inserted);
        Assert.Equal(0, second.FormVersions.Inserted);
        Assert.Equal(0, second.Bindings.Inserted);
        Assert.Equal(0, second.Drafts.Inserted);
        Assert.Equal(1, await db.Wf_FlowDefVersions.CountAsync());
        Assert.Equal(1, await db.Wf_FormDefVersions.CountAsync());
        Assert.Equal(1, await db.Wf_FormFlowBindings.CountAsync());
        Assert.Equal(1, await db.Wf_FormDrafts.CountAsync());
        Assert.NotNull((await db.Wf_FlowInstances.SingleAsync(x => x.Id == running.Id)).FlowDefVersionId);
        Assert.NotNull((await db.Wf_FormDatas.SingleAsync()).FormDefVersionId);
    }

    [Fact]
    public async Task Preflight_fails_closed_for_unpinnable_active_instance()
    {
        await using var db = NewDb();
        db.Wf_FlowInstances.Add(new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = "missing", StarterId = Guid.NewGuid(),
            Status = FlowInstanceStatus.Suspended
        });
        await db.SaveChangesAsync();

        var report = await new OaP0MigrationService(db).PreflightAsync();

        Assert.False(report.SafeToBackfill);
        Assert.Equal(1, report.UnpinnableActiveInstances);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new OaP0MigrationService(db).BackfillAsync());
    }

    [Fact]
    public async Task Preflight_counts_duplicate_active_business_keys_with_running_and_suspended()
    {
        await using var db = NewDb();
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "pur", FlowName = "PUR", SchemaJson = """{"nodes":[],"edges":[]}"""
        });
        db.Wf_FlowInstances.AddRange(
            new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "pur", BizType = "PUR_PR", BizId = "PR1", StarterId = Guid.NewGuid(), Status = FlowInstanceStatus.Running },
            new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "pur", BizType = "PUR_PR", BizId = "PR1", StarterId = Guid.NewGuid(), Status = FlowInstanceStatus.Suspended });
        await db.SaveChangesAsync();

        var report = await new OaP0MigrationService(db).PreflightAsync();

        Assert.Equal(1, report.DuplicateActiveBusinessKeys);
        Assert.False(report.SafeToBackfill);
    }
}
