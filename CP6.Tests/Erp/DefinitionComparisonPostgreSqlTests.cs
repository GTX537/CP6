using System.Net;
using System.Text.Json;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_Vue_FLOW_FORM_comparison_reads_selected_publications_without_writes()
    {
        await using var h = await Seed(); var flows = await PreparePublication(h); var forms = FormAuthoring(h);
        await EngineeringAction(h, "oa-designer", "form-save"); await EngineeringAction(h, "oa-designer", "form-publish");
        await forms.ConfigureAsync(new(Guid.NewGuid(), "absent", true, [], "local direct authoring policy"));
        await forms.ConfigurePublicationAsync(new(Guid.NewGuid(), "absent", true, false, [], "explicit direct publication policy"));
        await h.Db.Sys_Menus.Where(x => x.MenuKey == "oa-designer").ExecuteUpdateAsync(x => x.SetProperty(m => m.RoutePath, "/oa/designer"));
        var flow = await flows.SaveAsync("browser-flow", AuthoringCommand(name: "Flow comparison one"));
        var flow1 = await flows.PublishAsync("browser-flow", PublicationCommand(await Preview(flows, "browser-flow", flow)));
        var flowNext = await flows.CreateNextDraftAsync("browser-flow", NextDraftCommand(flow1.ResourceVersion!, flow1.Result!.Publication!.VersionId));
        var flowEdit = await flows.SaveAsync("browser-flow", AuthoringCommand("edit", flowNext.ResourceVersion, "Flow comparison two"));
        var flow2 = await flows.PublishAsync("browser-flow", PublicationCommand(await Preview(flows, "browser-flow", flowEdit), "publish2"));
        const string schema1 = """{"fields":[{"name":"amount","type":"number","label":"Amount","customLimit":9007199254740993.00}]}""";
        var form = await forms.SaveAsync("browser-form", FormAuthoringCommand(name: "Form comparison one", schema: schema1));
        var form1 = await forms.PublishAsync("browser-form", FormPublicationCommand(await FormPreview(forms, "browser-form", form)));
        var formNext = await forms.CreateNextDraftAsync("browser-form", FormNextDraftCommand(form1.ResourceVersion!, form1.Result!.Publication!.VersionId));
        var formEdit = await forms.SaveAsync("browser-form", FormAuthoringCommand("edit", formNext.ResourceVersion, "Form comparison two", schema1.Replace("9007199254740993.00", "9007199254740992.00")));
        var form2 = await forms.PublishAsync("browser-form", FormPublicationCommand(await FormPreview(forms, "browser-form", formEdit), "publish2"));
        using var result = await BrowserScenario(h, h.User, "oa-comparison-browser.mjs", "comparison-browser",
            new() { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") }, new()
            {
                ["CP6_COMPARE_FLOW_LEFT"] = flow1.Result.Publication.VersionId.ToString("D"), ["CP6_COMPARE_FLOW_RIGHT"] = flow2.Result!.Publication!.VersionId.ToString("D"),
                ["CP6_COMPARE_FORM_LEFT"] = form1.Result.Publication.VersionId.ToString("D"), ["CP6_COMPARE_FORM_RIGHT"] = form2.Result!.Publication!.VersionId.ToString("D")
            });
        Assert.Equal(flow2.ResourceVersion, (await flows.ReadHeadAsync("browser-flow")).Version);
        Assert.Equal(form2.ResourceVersion, (await forms.ReadHeadAsync("browser-form")).Version);
        Assert.Equal(5, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_workflow_authoring_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(5, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_authoring_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync()); Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Comparison_FORM_current_authority_cross_head_tenant_pagination_and_same_version()
    {
        await using var h = await Seed(); var owner = await PrepareFormPublication(h);
        var schema = JsonSerializer.Serialize(new { fields = new[] { new { name = "amount", type = "number" } },
            layout = Enumerable.Range(0, 220).ToDictionary(i => "item" + i.ToString("D3"), i => i) });
        var saved = await owner.SaveAsync("compare-form", FormAuthoringCommand(schema: schema));
        var first = await owner.PublishAsync("compare-form", FormPublicationCommand(await FormPreview(owner, "compare-form", saved)));
        var left = first.Result!.Publication!;
        var next = await owner.CreateNextDraftAsync("compare-form", FormNextDraftCommand(first.ResourceVersion!, left.VersionId));
        var nextSchema = JsonSerializer.Serialize(new { fields = new[] { new { name = "amount", type = "number" } },
            layout = Enumerable.Range(0, 220).ToDictionary(i => "item" + i.ToString("D3"), i => i + 1) });
        var edit = await owner.SaveAsync("compare-form", FormAuthoringCommand("edit", next.ResourceVersion, schema: nextSchema));
        var second = await owner.PublishAsync("compare-form", FormPublicationCommand(await FormPreview(owner, "compare-form", edit), "publish2"));
        var right = second.Result!.Publication!;
        var a = await owner.CompareAsync("compare-form", left.VersionId, right.VersionId);
        var b = await owner.CompareAsync("compare-form", left.VersionId, right.VersionId, 2);
        Assert.Equal(220, a.TotalChanges); Assert.Equal(200, a.Changes.Length); Assert.True(a.HasMore);
        Assert.Equal(20, b.Changes.Length); Assert.False(b.HasMore);
        Assert.Equal(220, a.Changes.Concat(b.Changes).Select(x => x.Path).Distinct().Count());
        Assert.Empty((await owner.CompareAsync("compare-form", left.VersionId, left.VersionId)).Changes);
        var other = await owner.SaveAsync("other-form", FormAuthoringCommand());
        var otherPublished = await owner.PublishAsync("other-form", FormPublicationCommand(await FormPreview(owner, "other-form", other)));
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.CompareAsync("compare-form", left.VersionId, otherPublished.Result!.Publication!.VersionId))).Status);
        await using var foreign = await Seed(); var foreignOwner = await PrepareFormPublication(foreign);
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => foreignOwner.CompareAsync("compare-form", left.VersionId, right.VersionId))).Status);
        var stranger = new Sys_User { UserName = "compare-other", Password = "unused", RoleId = 72 };
        h.Db.Sys_Users.Add(stranger); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        h.Http.User = CurrentPermissionContextTests.Principal(stranger.Id, h.Tenant);
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => FormAuthoring(h, _ => Task.FromResult(stranger.Id)).CompareAsync("compare-form", left.VersionId, right.VersionId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Comparison_HTTP_exact_publications_ignores_current_draft_and_preserves_read_scope()
    {
        await using var h = await Seed(); var owner = await PreparePublication(h);
        var saved = await owner.SaveAsync("compare", AuthoringCommand());
        var first = await owner.PublishAsync("compare", PublicationCommand(await Preview(owner, "compare", saved)));
        var left = first.Result!.Publication!;
        var next = await owner.CreateNextDraftAsync("compare", NextDraftCommand(first.ResourceVersion!, left.VersionId));
        var changed = await owner.SaveAsync("compare", AuthoringCommand("edit", next.ResourceVersion, "second"));
        var second = await owner.PublishAsync("compare", PublicationCommand(await Preview(owner, "compare", changed), "publish2"));
        var right = second.Result!.Publication!;
        var draft = await owner.CreateNextDraftAsync("compare", NextDraftCommand(second.ResourceVersion!, left.VersionId, "next3"));
        await owner.SaveAsync("compare", AuthoringCommand("unpublished", draft.ResourceVersion, "private-unpublished"));
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit" || x.ActionCode == "publish").ExecuteDeleteAsync();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h);
        string Path(Guid a, Guid b, int page = 1) => $"/api/oa/v2/definitions/FLOW/compare/compare?leftVersionId={a}&rightVersionId={b}&page={page}";
        async Task<HttpResponseMessage> Read(string path)
        { var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Add("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value))); return await host.Send(request); }
        using (var response = await Read(Path(left.VersionId, right.VersionId)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("private-unpublished", text);
            using var json = JsonDocument.Parse(text); var result = json.RootElement.GetProperty("data");
            Assert.Equal(left.VersionId, result.GetProperty("left").GetProperty("versionId").GetGuid());
            Assert.Equal(right.VersionId, result.GetProperty("right").GetProperty("versionId").GetGuid());
            Assert.Equal("Published", result.GetProperty("left").GetProperty("status").GetString());
            var change = Assert.Single(result.GetProperty("changes").EnumerateArray());
            Assert.Equal("/Name", change.GetProperty("path").GetString());
            Assert.Equal("\"draft\"", change.GetProperty("beforeJson").GetString());
            Assert.Equal("\"second\"", change.GetProperty("afterJson").GetString());
        }
        using (var missing = await Read(Path(left.VersionId, Guid.NewGuid()))) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var invalid = await Read(Path(Guid.Empty, right.VersionId))) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var invalidPage = await Read(Path(left.VersionId, right.VersionId, 0))) Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "workflow-definition", FieldName = "Name", Access = 3 });
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "workflow-definition", FieldName = "SchemaJson", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using (var hidden = await Read(Path(left.VersionId, right.VersionId)))
        {
            Assert.Equal(HttpStatusCode.OK, hidden.StatusCode); var text = await hidden.Content.ReadAsStringAsync();
            Assert.DoesNotContain("second", text); Assert.DoesNotContain(left.SchemaDigest!, text);
            using var json = JsonDocument.Parse(text); var result = json.RootElement.GetProperty("data");
            Assert.Equal("VISIBLE_FIELDS_ONLY", result.GetProperty("scope").GetString());
            Assert.Empty(result.GetProperty("changes").EnumerateArray());
            Assert.Contains(result.GetProperty("omittedFields").EnumerateArray(), x => x.GetString() == "SchemaJson");
        }
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        Assert.Equal(2, (await owner.VersionsAsync("compare")).Length);
    }
}
