using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Oa;

public sealed class DraftServiceTests
{
    private const string V1 =
        """{"fields":[{"name":"name","type":"input","required":true},{"name":"legacy","type":"input"}]}""";
    private const string V2 =
        """{"fields":[{"name":"name","type":"input","required":true},{"name":"count","type":"number","default":1}]}""";
    private const string TableSchema =
        """
        {"fields":[{"name":"items","label":"采购明细","type":"table","required":true,"minRows":1,"maxRows":2,
        "columns":[{"name":"material","label":"物料","type":"input","required":true},
        {"name":"qty","label":"数量","type":"number","required":true}]}]}
        """;

    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static DraftService Service(CP6Context db)
    {
        var forms = new FormService(db);
        var submissions = new FormSubmissionService(
            db, new DefinitionVersionResolver(db), forms,
            new FlowEngine(db, new ApproverResolver(db)));
        return new(db, new DefinitionVersionResolver(db), submissions);
    }

    private static async Task PublishAsync(CP6Context db, string schema, string name = "Form")
    {
        var forms = new FormService(db);
        var draft = await forms.SaveDraftAsync("form", name, schema, null);
        await forms.PublishAsync("form", draft.RowVersion, Guid.NewGuid());
    }

    private static JsonElement Json(string value)
    {
        using var doc = JsonDocument.Parse(value);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Create_only_creates_pinned_form_draft_and_list_detail_are_explicit()
    {
        await using var db = NewDb();
        await PublishAsync(db, V1);
        var owner = Guid.NewGuid();

        var created = await Service(db).CreateAsync(owner, "form", Json("""{"name":"A"}"""), "July");
        var page = await Service(db).ListAsync(owner, 1, 20);
        var detail = await Service(db).GetAsync(owner, created.Id);

        Assert.Single(page.Items);
        Assert.Equal(created.FormDefVersionId, detail.FormDefVersionId);
        Assert.Equal("""{"name":"A"}""", detail.DataJson);
        Assert.Contains(@"""legacy""", detail.SchemaJson);
        Assert.Empty(db.Wf_FlowInstances);
        Assert.Empty(db.Wf_FlowTokens);
        Assert.Empty(db.Wf_FlowTasks);
        Assert.Empty(db.Wf_FlowHistories);
    }

    [Fact]
    public async Task Owner_and_row_version_checks_fail_closed()
    {
        await using var db = NewDb();
        await PublishAsync(db, V1);
        var owner = Guid.NewGuid();
        var created = await Service(db).CreateAsync(owner, "form", Json("{}"), null);
        var row = await db.Wf_FormDrafts.SingleAsync();
        row.RowVersion = new byte[] { 1 };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Service(db).GetAsync(Guid.NewGuid(), created.Id));
        var conflict = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).UpdateAsync(owner, created.Id, Json("{}"), null, new byte[] { 2 }));
        Assert.Equal("E-WF-041", conflict.Message);
    }

    [Fact]
    public async Task Stale_draft_requires_rebase_and_removed_values_require_confirmation()
    {
        await using var db = NewDb();
        await PublishAsync(db, V1, "v1");
        var owner = Guid.NewGuid();
        var created = await Service(db).CreateAsync(
            owner, "form", Json("""{"name":"A","legacy":"keep"}"""), null);
        await PublishAsync(db, V2, "v2");

        Assert.True((await Service(db).GetAsync(owner, created.Id)).Stale);
        var stale = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).SubmitAsync(owner, created.Id, "stale-key", null));
        Assert.Equal("E-WF-040", stale.Message);

        var confirm = await Assert.ThrowsAsync<DraftRebaseConfirmationException>(
            () => Service(db).RebaseAsync(owner, created.Id, 2, false, null));
        Assert.Equal(new[] { "legacy" }, confirm.RemovedFields);
        var rebased = await Service(db).RebaseAsync(owner, created.Id, 2, true, null);
        Assert.Contains(@"""name"":""A""", rebased.DataJson);
        Assert.Contains(@"""count"":1", rebased.DataJson);
        Assert.Equal(new[] { "legacy" }, rebased.RemovedFields);
    }

    [Fact]
    public async Task Failed_submit_stays_active_success_is_idempotent_and_hides_from_list()
    {
        await using var db = NewDb();
        await PublishAsync(db, V1);
        var owner = Guid.NewGuid();
        var invalid = await Service(db).CreateAsync(owner, "form", Json("{}"), null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).SubmitAsync(owner, invalid.Id, "invalid-key", null));
        Assert.Equal(WfFormDraftStatus.Active,
            (await db.Wf_FormDrafts.SingleAsync(x => x.Id == invalid.Id)).Status);

        var valid = await Service(db).CreateAsync(owner, "form", Json("""{"name":"A"}"""), null);
        var first = await Service(db).SubmitAsync(owner, valid.Id, "valid-key", null);
        var retry = await Service(db).SubmitAsync(owner, valid.Id, "valid-key", null);
        Assert.Equal(first, retry);
        Assert.Equal(WfFormDraftStatus.Submitted,
            (await db.Wf_FormDrafts.SingleAsync(x => x.Id == valid.Id)).Status);
        Assert.DoesNotContain((await Service(db).ListAsync(owner, 1, 20)).Items, x => x.Id == valid.Id);
        Assert.Single(db.Wf_FormDatas);
    }

    [Fact]
    public async Task TableDraft_AllowsIncompleteRowsButRejectsInvalidShape()
    {
        await using var db = NewDb();
        await PublishAsync(db, TableSchema);
        var owner = Guid.NewGuid();

        var draft = await Service(db).CreateAsync(
            owner, "form", Json("""{"items":[{"material":"A-01"}]}"""), null);
        Assert.Contains(@"""material"":""A-01""", draft.DataJson);

        var wrongType = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(db).UpdateAsync(owner, draft.Id,
                Json("""{"items":[{"material":"A-01","qty":"two"}]}"""), null, null));
        Assert.Equal("E-WF-047", wrongType.Message);

        var unknownColumn = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(db).UpdateAsync(owner, draft.Id,
                Json("""{"items":[{"material":"A-01","unexpected":1}]}"""), null, null));
        Assert.Equal("E-WF-047", unknownColumn.Message);

        var persisted = await db.Wf_FormDrafts.SingleAsync(x => x.Id == draft.Id);
        Assert.DoesNotContain("unexpected", persisted.DataJson);
    }
}
