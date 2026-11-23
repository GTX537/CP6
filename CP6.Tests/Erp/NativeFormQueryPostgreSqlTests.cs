using System.Net;
using System.Text;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
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
    private const string LegacyQueryFlowKey = "wp1205-historical";

    private static async Task<(Guid Pinned, Guid Missing, Guid Reconstructed, Guid Version)> LegacyQuerySamples(Harness h)
    {
        var head = new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = LegacyQueryFlowKey, FlowName = "只在当前定义的名称", Version = 3 };
        var exact = new Wf_FlowDefVersion { Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 1,
            Status = WfDefinitionVersionStatus.Published, FlowNameSnapshot = "提交时的流程名称" };
        var imported = new Wf_FlowDefVersion { Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 2,
            Status = WfDefinitionVersionStatus.Published, FlowNameSnapshot = "旧系统记录的名称", Creator = "legacy-runtime-backfill" };
        h.Db.Wf_FlowDefs.Add(head);
        h.Db.Wf_FlowDefVersions.AddRange(exact, imported);
        await h.Db.SaveChangesAsync();
        var at = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        Wf_FlowInstance Row(Guid? version, int status) => new()
        {
            Id = Guid.NewGuid(), FlowKey = head.FlowKey, FlowDefVersionId = version, StarterId = h.User, CreateDate = at, Status = status
        };
        var pinned = Row(exact.Id, FlowInstanceStatus.Approved);
        var missing = Row(null, FlowInstanceStatus.Running);
        var reconstructed = Row(imported.Id, FlowInstanceStatus.Approved);
        h.Db.Wf_FlowInstances.AddRange(pinned, missing, reconstructed);
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        return (pinned.Id, missing.Id, reconstructed.Id, exact.Id);
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeQuery_legacy_HTTP_list_and_detail_use_recorded_versions_and_explain_missing_or_reconstructed_pins()
    {
        await using var h = await Seed(); await RegisterScope(h);
        var sample = await LegacyQuerySamples(h);
        var unrelated = await GrantDelegate(h, "wp1205-legacy-nonparticipant");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host, h);

        async Task<FormQueryPage> Page(Dictionary<string, string> session, int number)
        {
            using var response = await host.Send(BoundRequest(session, "/api/oa/query/search",
                new { flowKey = LegacyQueryFlowKey, page = number, pageSize = 2 }));
            return await PersonalHttpResult<FormQueryPage>(response);
        }

        var first = await Page(cookies, 1); var second = await Page(cookies, 2);
        Assert.Equal(3, first.Total); Assert.Equal(3, second.Total);
        var rows = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(new[] { sample.Pinned, sample.Missing, sample.Reconstructed }.OrderByDescending(id => id),
            rows.Select(r => r.InstanceId));
        var pinned = Assert.Single(rows, row => row.InstanceId == sample.Pinned);
        Assert.Equal("提交时的流程名称", pinned.FlowName);
        Assert.Equal(sample.Version, pinned.FlowVersionId); Assert.Equal(1, pinned.FlowVersion);
        Assert.Equal("LEGACY_VERSION_SNAPSHOT", pinned.VersionSource);
        var missing = Assert.Single(rows, row => row.InstanceId == sample.Missing);
        Assert.Null(missing.FlowName); Assert.Null(missing.FlowVersion); Assert.Null(missing.FlowVersionId);
        Assert.Equal("MISSING_VERSION_SNAPSHOT", missing.VersionSource);
        var reconstructed = Assert.Single(rows, row => row.InstanceId == sample.Reconstructed);
        Assert.Equal("LEGACY_HEAD_BACKFILL", reconstructed.VersionSource);

        foreach (var row in rows)
        {
            using var response = await host.Send(BoundRequest(cookies, "/api/oa/inbox/detail/" + row.InstanceId));
            var detail = (await PersonalHttpResult<InboxDetail>(response)).Instance;
            Assert.Equal(row.FlowName, detail.FlowName); Assert.Equal(row.FlowVersion, detail.FlowVersion);
            Assert.Equal(row.FlowVersionId, detail.FlowVersionId); Assert.Equal(row.VersionSource, detail.VersionSource);
        }
        var other = await Page(await DocumentReviewerLogin(host, h, unrelated), 1);
        Assert.Equal(0, other.Total); Assert.Empty(other.Items);
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
    public async Task NativeQuery_HTTP_APPLIED_requires_the_actual_Owner_result_to_reach_the_query_projection()
    {
        await using var h = await Seed(); var fixture = await DocumentApplicationSetup(h); await QueryPermission(h);
        var document = fixture.Document;
        var owner = DocumentReviews(h, document.Files);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, await DocumentHostSettings(document.Files));
        var cookies = await Login(host, h);
        var url = "/api/oa/v2/query/instances/" + fixture.Decision.InstanceId;
        async Task<NativeFormQueryItemV2> Read()
        {
            using var response = await host.Send(BoundRequest(cookies, url));
            return (await PersonalHttpResult<NativeFormQueryDetailV2>(response)).Item;
        }

        var approved = await Read();
        Assert.Equal(FlowInstanceStatus.Approved, approved.InstanceStatus);
        Assert.Equal("UNPROVEN", approved.ApplicationSummary!.ApplicationState);
        Assert.Null(approved.ApplicationSummary.BusinessResultRef);
        var ack = FactAck(await fixture.Receiver.ReceiveAsync(fixture.Payload));
        Assert.Equal("NOT_APPLIED", ack.Application);
        var ready = await owner.SubmissionAsync(document.Document, document.Submission);
        var result = await owner.ApplyAsync(document.Document, ready.Application.Etag,
            new(Guid.NewGuid(), document.Submission, null, "应用本地合成文档的准确审批结果"));
        Assert.Equal("APPLIED", result.Receipt.Application);
        Assert.Equal("UNPROVEN", (await Read()).ApplicationSummary!.ApplicationState);

        var authority = fixture.Authority.Identity;
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_receipt_v2.registrations(environment_id,tenant_id,principal_id,owner,revision,enabled,can_receive,can_query,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{authority.Principal},{result.Receipt.Subject.BusinessOwner},{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_approval_receipt_v2.subject_grants(environment_id,tenant_id,principal_id,submission_id,owner,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{authority.Principal},{document.Submission},{result.Receipt.Subject.BusinessOwner},{Guid.NewGuid()},true,true,true);
            """);
        await ReceiptReceiver(h, fixture.Authority).ReceiveAsync(ReceiptBytes(result.Receipt));
        var applied = await Read();
        Assert.Equal(approved.InstanceStatus, applied.InstanceStatus);
        Assert.Equal("APPLIED", applied.ApplicationSummary!.ApplicationState);
        Assert.Equal(result.Receipt.ReceiptId, applied.ApplicationSummary.ApplicationReceiptId);
        Assert.NotNull(applied.ApplicationSummary.BusinessResultRef);
        Assert.Equal(result.Receipt.BusinessResultRef, applied.ApplicationSummary.BusinessResultRef);
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
        var record = Assert.Single((await inbox.SearchAsync(new())).Items);
        var day = record.SubmittedAtUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.TimeZoneId, "UTC"));
        var page = await inbox.SearchAsync(new(StarterId: h.User, HandlerId: h.User, FlowKey: "native-inbox", Status: 0,
            From: day, To: day, FormKey: "personal", HasInstance: true, TimeZoneId: "UTC"));
        Assert.Equal(1, page.Total);
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
