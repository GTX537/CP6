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
        await using var h = await Seed(); var form = new FormService(h.Db);
        const string raw = """ {"layout":{"columns":2,"amount":9007199254740993.00},"fields":[{"name":"reason","type":"input","label":"事由","default":"保留","extension":{"x":1.0}}],"rules":[]} """;
        var draft = await form.SaveDraftAsync("w268", "采购单", raw, null);
        var version = await form.PublishAsync("w268", draft.RowVersion, h.User);
        await form.SubmitDataAsync("w268", null, """{"reason":"原数据"}""");
        h.Db.ChangeTracker.Clear();
        Assert.Equal(raw, (await form.GetVersionAsync("w268", 1))!.SchemaJson);
        var before = await h.Db.Wf_FormDefVersions.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => form.SaveDraftAsync("bad", "拒绝", """{"fields":[{"name":"x","type":"script"}]}""", null));
        Assert.False(await h.Db.Wf_FormDefs.AnyAsync(x => x.FormKey == "bad"));
        Assert.Equal(before, await h.Db.Wf_FormDefVersions.CountAsync());
        Assert.False(h.Db.ChangeTracker.HasChanges());
        var next = await form.SaveDraftAsync("w268", "修订", raw.Replace("事由", "原因"), null);
        await form.PublishAsync("w268", next.RowVersion, h.User);
        Assert.Equal(raw, (await form.GetVersionAsync("w268", 1))!.SchemaJson);
        var data = await h.Db.Wf_FormDatas.SingleAsync();
        Assert.Equal(version.VersionId, data.FormDefVersionId);
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
