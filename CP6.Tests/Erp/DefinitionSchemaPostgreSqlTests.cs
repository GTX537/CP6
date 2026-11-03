using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Definition_schema_form_rejects_before_writes_and_keeps_complete_published_and_data_versions()
    {
        await using var h = await Seed(); var owner = await PrepareFormPublication(h); var legacy = new FormService(h.Db);
        const string raw = """ {"layout":{"columns":2,"amount":9007199254740993.00},"fields":[{"name":"reason","type":"input","label":"事由","default":"保留","extension":{"x":1.0}}],"rules":[]} """;
        // Preserve an explicitly arranged historical business record while the current Owner publishes separately.
        var historicalDraft = await CP6.Tests.Wf.LegacyDefinitionFixture.FormDraft(h.Db, "historical", "历史采购单", raw);
        var historicalVersion = await CP6.Tests.Wf.LegacyDefinitionFixture.FormPublished(h.Db, "historical", historicalDraft.RowVersion, h.User);
        await legacy.SubmitDataAsync("historical", null, """{"reason":"原数据"}""");
        h.Db.ChangeTracker.Clear();
        var draft = await owner.SaveAsync("w268", FormAuthoringCommand(schema: raw));
        await owner.PublishAsync("w268", FormPublicationCommand(await FormPreview(owner, "w268", draft)));
        Assert.Equal(raw, (await owner.VersionsAsync("w268")).Single().Fields["SchemaJson"]);
        Assert.Equal(422, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("bad", FormAuthoringCommand("invalid", schema: """{"fields":[{"name":"x","type":"script"}]}""")))).Status);
        Assert.False(await h.Db.Wf_FormDefs.AnyAsync(x => x.FormKey == "bad"));
        Assert.Equal("DEFINITION_NOT_FOUND", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.ReadHeadAsync("bad"))).Message);
        Assert.False(h.Db.ChangeTracker.HasChanges());
        var published = await owner.ReadHeadAsync("w268"); var source = (await owner.VersionsAsync("w268")).Single();
        var next = await owner.CreateNextDraftAsync("w268", FormNextDraftCommand(published.Version, source.VersionId));
        var edited = await owner.SaveAsync("w268", FormAuthoringCommand("edit", next.ResourceVersion, "修订", raw.Replace("事由", "原因")));
        await owner.PublishAsync("w268", FormPublicationCommand(await FormPreview(owner, "w268", edited), "publish-two"));
        Assert.Equal(raw, (await owner.VersionsAsync("w268")).Single(x => x.Version == 1).Fields["SchemaJson"]);
        Assert.Equal(raw, (await legacy.GetVersionAsync("historical", 1))!.SchemaJson);
        var data = await h.Db.Wf_FormDatas.SingleAsync();
        Assert.Equal(historicalVersion.VersionId, data.FormDefVersionId);
        Assert.Contains("原数据", data.DataJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task Definition_schema_flow_invalid_identity_and_endpoints_do_not_write_but_unfinished_draft_does()
    {
        await using var h = await Seed(); var owner = await PrepareAuthoring(h);
        var unfinished = """{"nodes":[{"id":"s","type":"start","x":2.0}],"edges":[],"layout":{"value":9007199254740993.00}}""";
        var result = await owner.SaveAsync("w268-flow", AuthoringCommand("unfinished", schema: unfinished));
        Assert.Contains("E-WF-010", result.Result!.Diagnostics);
        Assert.Equal(unfinished, (await owner.ReadAsync("w268-flow")).Fields["SchemaJson"]);
        foreach (var edges in new[] {
            """[{"id":"same","from":"s","to":"s"},{"id":"same","from":"s","to":"s"}]""",
            """[{"id":" ","from":"s","to":"s"}]""",
            """[{"from":"s","to":"missing"}]""" })
        {
            var invalid = unfinished.Replace("\"edges\":[]", "\"edges\":" + edges);
            var error = await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.SaveAsync("w268-flow", AuthoringCommand("invalid", result.ResourceVersion, schema: invalid)));
            Assert.Equal("SCHEMA_SHAPE_INVALID", error.Message);
        }
        Assert.Equal(result.ResourceVersion, (await owner.ReadAsync("w268-flow")).Version);
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}
