using System.Net;
using System.Text;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task QueryPermission(Harness h)
    {
        await EngineeringAction(h, "oa-form-search", "query");
        var menu = await h.Db.Sys_Menus.SingleAsync(m => m.MenuKey == "oa-form-search");
        if (!await h.Db.Sys_RoleMenus.AnyAsync(m => m.RoleId == 72 && m.MenuId == menu.MenuId))
        { h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menu.MenuId }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); }
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeQuery_later_APP_start_does_not_enter_old_snapshot_and_new_names_do_not_rewrite_history()
    {
        await using var h = await Seed(); var (intake, _) = await StartSetup(h); await QueryPermission(h);
        var inbox = NativeInbox(h); var before = await inbox.SearchAsync(new()); Assert.Empty(before.Items);
        await Starter(h).RunAsync(h.Tenant); h.Db.ChangeTracker.Clear();
        Assert.Empty((await inbox.SearchAsync(new(SnapshotAsOf: before.SnapshotAsOf))).Items);
        var original = Assert.Single((await inbox.SearchAsync(new())).Items);
        Assert.Equal(intake.Version, original.FlowVersionId);
        var head = await intake.Flows.ReadHeadAsync("binding-flow");
        var next = await intake.Flows.CreateNextDraftAsync("binding-flow", NextDraftCommand(head.Version, intake.Version, "history-next"));
        var saved = await intake.Flows.SaveAsync("binding-flow", AuthoringCommand("rename-history", next.ResourceVersion, "New flow name", StartSchema));
        await intake.Flows.PublishAsync("binding-flow", PublicationCommand(await Preview(intake.Flows, "binding-flow", saved), "history-new-publication"));
        var detail = await inbox.QueryInstanceAsync(original.InstanceId!.Value);
        Assert.Equal(original.FlowName, detail.Item.FlowName); Assert.NotEqual("New flow name", detail.Item.FlowName);
        Assert.Equal(intake.Version, detail.Item.FlowVersionId);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeQuery_APPROVED_is_UNPROVEN_until_real_continuous_Owner_receipts_are_ingested()
    {
        await using var h = await Seed(); var fixture = await ReceiptSetup(h, 2); await QueryPermission(h);
        var inbox = NativeInbox(h); var before = Assert.Single((await inbox.SearchAsync(new())).Items);
        Assert.Equal(1, before.InstanceStatus); Assert.Equal("UNPROVEN", before.ApplicationSummary!.ApplicationState);
        Assert.Null(before.ApplicationSummary.BusinessResultRef);
        await fixture.Owner.ReceiveAsync(ReceiptBytes(fixture.Receipts[1]));
        var gap = Assert.Single((await inbox.SearchAsync(new())).Items).ApplicationSummary!;
        Assert.True(gap.PendingReceiptGap); Assert.Equal("UNPROVEN", gap.ApplicationState); Assert.Null(gap.ApplicationReceiptId);
        await fixture.Owner.ReceiveAsync(ReceiptBytes(fixture.Receipts[0]));
        var actual = (await inbox.QueryInstanceAsync(before.InstanceId!.Value)).Item.ApplicationSummary!;
        Assert.Equal(fixture.Receipts[1].ReceiptId, actual.ApplicationReceiptId);
        Assert.Equal(fixture.Receipts[1].Application, actual.ApplicationState); Assert.Equal("2", actual.ReceiptRevision);
        Assert.False(actual.PendingReceiptGap); Assert.Equal(fixture.Receipts[1].BusinessResultRef, actual.BusinessResultRef);
        await ApplicationCounts(h, 2); // Reading never executes Owner application again.
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeQuery_unbound_history_uses_exact_publication_current_mask_and_authorized_totals()
    {
        await using var h = await Seed(); var (owner, form, _) = await PreparePersonal(h); await QueryPermission(h);
        var context = await owner.ContextAsync("personal");
        var submitted = PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal", PersonalCommand(PersonalDirect(context), "unbound")));
        var inbox = NativeInbox(h, owner); var page = await inbox.SearchAsync(new()); var item = Assert.Single(page.Items);
        Assert.Equal(1, page.Total); Assert.Equal(submitted.FormDataId, item.FormDataId); Assert.Null(item.InstanceId);
        Assert.Equal("SubmittedUnbound", item.SubmissionState); Assert.Null(item.InstanceStatus); Assert.Equal(context.Name, item.FormName);
        Assert.Empty((await inbox.SearchAsync(new(HasInstance: true))).Items);
        Assert.Equal(0, (await inbox.SearchAsync(new(Keyword: "saved"))).Total); // DataJson is never a full text index.
        var next = await form.CreateNextDraftAsync("personal", FormNextDraftCommand((await form.ReadHeadAsync("personal")).Version, context.FormVersionId));
        var edit = await form.SaveAsync("personal", FormAuthoringCommand("rename", next.ResourceVersion, "Later publication"));
        var newer = (await form.PublishAsync("personal", FormPublicationCommand(await FormPreview(form, "personal", edit), "published"))).Result!.Publication!;
        await form.SetActivationAsync("personal", FormActivationCommand((await form.ActivationPreviewAsync("personal")).Current, newer.VersionId, id: "new-active"));
        var detail = await inbox.QueryFormDataAsync(submitted.FormDataId);
        Assert.Equal(context.Name, detail.Item.FormName); Assert.Equal(context.FormVersionId, detail.Form!.FormVersionId);
        Assert.False(detail.CanEdit); Assert.Null(detail.Instance); Assert.Contains("saved", detail.Form.DataJson);
        var policy = (await owner.PolicyAsync("personal"))!;
        await owner.ConfigureAsync("personal", new(Guid.NewGuid(), policy.Version.ToString("D"), true, true, [new(72, new() { ["a"] = "hidden" })], policy.RetentionPolicyRef, 30, true, "hide current value"));
        Assert.DoesNotContain("saved", (await inbox.QueryFormDataAsync(submitted.FormDataId)).Form!.DataJson);
        Assert.Equal(1, (await inbox.SearchAsync(new(FormKey: "personal", FormVersion: 1))).Total);
        policy = (await owner.PolicyAsync("personal"))!;
        await owner.ConfigureAsync("personal", new(Guid.NewGuid(), policy.Version.ToString("D"), true, true, [], policy.RetentionPolicyRef, 30, true, "revoke use"));
        Assert.Equal(0, (await inbox.SearchAsync(new())).Total);
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => inbox.QueryFormDataAsync(submitted.FormDataId))).Status);
        await using var stranger = await Seed(); await PreparePersonal(stranger); await QueryPermission(stranger);
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => NativeInbox(stranger).QueryFormDataAsync(submitted.FormDataId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeQuery_HTTP_closed_filters_tenant_timezone_half_open_dates_and_stable_snapshot()
    {
        await using var h = await Seed(); var (owner, _, _) = await PreparePersonal(h); await QueryPermission(h);
        await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.TimeZoneId, "America/New_York"));
        var context = await owner.ContextAsync("personal");
        owner.Clock = () => DateTimeOffset.Parse("2026-11-01T03:59:59Z");
        await owner.SubmitDirectAsync("personal", PersonalCommand(PersonalDirect(context), "before"));
        owner.Clock = () => DateTimeOffset.Parse("2026-11-01T04:00:00Z");
        var first = PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal", PersonalCommand(PersonalDirect(context), "start")));
        owner.Clock = () => DateTimeOffset.Parse("2026-11-02T04:59:59Z");
        var last = PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal", PersonalCommand(PersonalDirect(context), "last")));
        owner.Clock = () => DateTimeOffset.Parse("2026-11-02T05:00:00Z");
        await owner.SubmitDirectAsync("personal", PersonalCommand(PersonalDirect(context), "after"));
        var inbox = NativeInbox(h); var query = new NativeFormQueryV2(From: "2026-11-01", To: "2026-11-01", PageSize: 1, TimeZoneId: "America/New_York");
        var page = await inbox.SearchAsync(query); Assert.Equal(2, page.Total); Assert.Equal(last.FormDataId, Assert.Single(page.Items).FormDataId);
        Assert.Equal(TimeSpan.FromHours(25), page.Window.ToExclusiveUtc - page.Window.FromInclusiveUtc);
        Assert.Equal(first.FormDataId, Assert.Single((await inbox.SearchAsync(query with { Page = 2, SnapshotAsOf = page.SnapshotAsOf })).Items).FormDataId);
        var all = await inbox.SearchAsync(new(PageSize: 1));
        owner.Clock = () => DateTimeOffset.Parse("2026-11-01T04:00:00Z"); // A later write with an older business timestamp still cannot enter the previous snapshot.
        var added = PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal", PersonalCommand(PersonalDirect(context), "after-page")));
        Assert.Equal(4, (await inbox.SearchAsync(new(PageSize: 1, Page: 2, SnapshotAsOf: all.SnapshotAsOf))).Total);
        Assert.Equal(5, (await inbox.SearchAsync(new())).Total);
        Assert.Equal("QUERY_TIMEZONE_CHANGED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => inbox.SearchAsync(query with { TimeZoneId = "UTC" }))).Message);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h);
        using var read = await host.Send(PersonalHttp(cookies, "/api/oa/v2/query/search", Encoding.UTF8.GetBytes("{\"hasInstance\":false,\"pageSize\":100}")));
        Assert.Equal(5, (await PersonalHttpResult<NativeFormQueryPageV2>(read)).Total);
        foreach (var raw in new[] { "{\"unknownField\":true}", "{\"status\":99}", "{\"pageSize\":101}", "{\"page\":2}", "{\"page\":1,\"page\":2}" })
        {
            using var invalid = await host.Send(PersonalHttp(cookies, "/api/oa/v2/query/search", Encoding.UTF8.GetBytes(raw)));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        }
        using var actualDetail = await host.Send(BoundRequest(cookies, "/api/oa/v2/form-data/" + added.FormDataId));
        Assert.Equal("SubmittedUnbound", (await PersonalHttpResult<NativeFormQueryDetailV2>(actualDetail)).Item.SubmissionState);
        await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.TimeZoneId, (string?)null));
        Assert.Equal("TENANT_TIMEZONE_REQUIRED", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => inbox.SearchAsync(query))).Message);
        Assert.Equal(5, (await inbox.SearchAsync(new())).Total); // No implicit server timezone is used for an unbounded query.
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeQuery_instance_filters_current_TASK_delegation_and_readonly_detail_use_one_authorized_set()
    {
        await using var h = await Seed(); var runtime = await NativeFormSetup(h); await QueryPermission(h);
        var inbox = NativeInbox(h, runtime.Owner);
        var page = await inbox.SearchAsync(new(StarterId: h.User, HandlerId: h.User, FlowKey: "native-inbox", Status: 0, FormKey: "personal", HasInstance: true));
        Assert.Equal(runtime.Instance, Assert.Single(page.Items).InstanceId);
        Assert.Equal(0, (await inbox.SearchAsync(new(HandlerId: Guid.NewGuid()))).Total);
        var detail = await inbox.QueryInstanceAsync(runtime.Instance); Assert.Single(detail.Instance!.Tasks); Assert.False(detail.CanEdit);
        await GrantManagement(h); var actual = await GrantDelegate(h); await CreateTaskGrant(h, h.User, actual, "native-inbox", new(["TASK_READ"], ["native-inbox"]));
        TaskActor(h, actual); var peer = NativeInbox(h, PersonalForm(h with { User = actual }));
        Assert.Empty((await peer.SearchAsync(new())).Items);
        Assert.Single((await peer.SearchAsync(new(), h.User)).Items);
        Assert.Equal(actual, (await peer.QueryInstanceAsync(runtime.Instance, h.User)).Instance!.ActualUserId);
        var queryMenu = await h.Db.Sys_Menus.SingleAsync(m => m.MenuKey == "oa-form-search");
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == queryMenu.MenuId && a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => peer.SearchAsync(new(), h.User))).Status);
    }
}
