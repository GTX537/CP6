using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

public class DefinitionVersionServiceTests
{
    private const string FlowV1 =
        """{"start":"start","nodes":[{"id":"start","type":"start"},{"id":"end","type":"end"}],"edges":[{"from":"start","to":"end"}]}""";
    private const string FlowV2 =
        """{"start":"start","nodes":[{"id":"start","type":"start"},{"id":"review","type":"approval","approverStrategy":"Starter"},{"id":"end","type":"end"}],"edges":[{"from":"start","to":"review"},{"from":"review","to":"end"}]}""";

    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Saving_draft_does_not_replace_latest_published_until_publish()
    {
        await using var db = NewDb();
        var service = new FlowDefService(db);
        var firstDraft = await service.SaveDraftAsync("leave", "Leave v1", null, FlowV1, null);
        var first = await service.PublishAsync("leave", firstDraft.RowVersion, Guid.NewGuid());

        var secondDraft = await service.SaveDraftAsync("leave", "Leave v2", null, FlowV2, null);
        var resolver = new DefinitionVersionResolver(db);
        var beforePublish = await resolver.ResolveLatestFlowAsync("leave");
        Assert.Equal(first.VersionId, beforePublish.Version.Id);
        Assert.Equal(1, beforePublish.Version.Version);

        var second = await service.PublishAsync("leave", secondDraft.RowVersion, Guid.NewGuid());
        var afterPublish = await resolver.ResolveLatestFlowAsync("leave");
        Assert.Equal(second.VersionId, afterPublish.Version.Id);
        Assert.Equal(2, afterPublish.Version.Version);
        Assert.Equal(2, await db.Wf_FlowDefVersions.CountAsync(x => x.Status == WfDefinitionVersionStatus.Published));
    }

    [Fact]
    public async Task Definition_draft_rejects_stale_rowversion()
    {
        await using var db = NewDb();
        var service = new FlowDefService(db);
        var created = await service.SaveDraftAsync("leave", "Leave", null, FlowV1, null);
        var row = await db.Wf_FlowDefVersions.SingleAsync(x => x.Id == created.VersionId);
        row.RowVersion = new byte[] { 1 };
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveDraftAsync("leave", "Other", null, FlowV2, new byte[] { 2 }));
        Assert.Equal("E-WF-045", ex.Message);
        Assert.Equal("Leave", (await db.Wf_FlowDefVersions.SingleAsync()).FlowNameSnapshot);
    }

    [Fact]
    public async Task Form_versions_are_copy_on_write_and_resolvable()
    {
        await using var db = NewDb();
        var service = new FormService(db);
        var draft = await service.SaveDraftAsync("leave", "Leave",
            """{"fields":[{"name":"reason","type":"input"}]}""", null);
        await service.PublishAsync("leave", draft.RowVersion, Guid.NewGuid());

        var copy = await service.GetDraftAsync("leave");
        Assert.NotNull(copy);
        Assert.Equal(2, copy!.Version);

        var resolved = await new DefinitionVersionResolver(db).ResolveLatestFormAsync("leave");
        Assert.Equal(1, resolved.Version!.Version);
    }
}
