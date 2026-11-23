using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_favorite_lost_response_recovery_and_bound_unbound_pinned_history()
    {
        await using var h = await Seed(); var (owner, form, flow) = await PreparePersonal(h); await QueryPermission(h);
        await EngineeringAction(h, "oa-form-catalog", "favorite");
        await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.TimeZoneId, LegacyCalendarZone));
        var original = await owner.ContextAsync("personal");
        var unbound = PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal", PersonalCommand(PersonalDirect(original, "{\"a\":\"Original unbound value\"}"), "unbound-history")));
        var next = await form.CreateNextDraftAsync("personal", FormNextDraftCommand((await form.ReadHeadAsync("personal")).Version, original.FormVersionId));
        var edited = await form.SaveAsync("personal", FormAuthoringCommand("renamed", next.ResourceVersion, "Current catalog name"));
        var published = (await form.PublishAsync("personal", FormPublicationCommand(await FormPreview(form, "personal", edited), "second-publication"))).Result!.Publication!;
        await form.SetActivationAsync("personal", FormActivationCommand((await form.ActivationPreviewAsync("personal")).Current, published.VersionId, id: "second-active"));
        await ActiveDependencyFlow(flow, "history-flow", "personal");
        var bound = PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal", PersonalCommand(PersonalDirect(await owner.ContextAsync("personal"), "{\"a\":\"Bound current value\"}"), "bound-history")));
        var legacy = await LegacyQuerySamples(h);
        var ties = Enumerable.Range(1, 21).Select(i => CalendarLegacyRow(h.User, LegacyFallEnd.AddMilliseconds(-1), $"同刻 {i}")).ToArray();
        var firstOfDay = CalendarLegacyRow(h.User, LegacyFallStart, "结束日零点");
        var calendarRows = ties.Concat([
            firstOfDay, CalendarLegacyRow(h.User, LegacyFallStart.AddMilliseconds(-1), "窗口之前"),
            CalendarLegacyRow(h.User, LegacyFallEnd, "下一天零点"),
            CalendarLegacyRow(Guid.NewGuid(), LegacyFallEnd.AddMilliseconds(-1), "其他发起人")
        ]).ToArray();
        h.Db.Wf_FlowInstances.AddRange(calendarRows);
        await h.Db.SaveChangesAsync();
        var added = CalendarLegacyRow(h.User, LegacyFallEnd.AddMilliseconds(-1), "翻页期间新增");
        var ordered = ties.Select(row => row.Id).OrderByDescending(id => id).Append(firstOfDay.Id);
        foreach (var (menu, path) in new[] { ("oa-form-catalog", "/oa/form-catalog"), ("oa-form-search", "/oa/form-search"), ("oa-inbox", "/oa/inbox") })
            await h.Db.Sys_Menus.Where(m => m.MenuKey == menu).ExecuteUpdateAsync(s => s.SetProperty(m => m.RoutePath, path));
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var browserOutput = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "catalog-history-browser");
        async Task ChangeHistoryDuringPaging()
        {
            var ready = Path.Combine(browserOutput, "legacy-page-one.ready");
            while (!File.Exists(ready)) await Task.Delay(50, stop.Token);
            await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            peer.Wf_FlowInstances.Add(added);
            await peer.SaveChangesAsync(stop.Token);
            await File.WriteAllTextAsync(Path.Combine(browserOutput, "legacy-page-one.inserted"), "committed", stop.Token);

            var rewrite = Path.Combine(browserOutput, "legacy-origin-rewrite.ready");
            while (!File.Exists(rewrite)) await Task.Delay(50, stop.Token);
            // 在同一隔离数据库保留数据并更换表存储代际，验证页面对过期截点的处理。
            var before = await ReadLegacyOrigin(peer);
            await peer.Database.ExecuteSqlRawAsync("""VACUUM FULL public."Wf_FlowInstance" """, stop.Token);
            var after = await ReadLegacyOrigin(peer);
            Assert.Equal(before[..before.LastIndexOf('/')], after[..after.LastIndexOf('/')]);
            Assert.NotEqual(before, after);
            await File.WriteAllTextAsync(Path.Combine(browserOutput, "legacy-origin-rewrite.done"), "generation changed", stop.Token);
        }
        var changes = ChangeHistoryDuringPaging();
        try
        {
            using var result = await BrowserScenario(h, h.User, "oa-catalog-history-browser.mjs", "catalog-history-browser",
                new() { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") },
                new()
                {
                    ["CP6_HISTORY_UNBOUND"] = unbound.FormDataId.ToString("D"), ["CP6_HISTORY_BOUND"] = bound.FlowInstanceId!.Value.ToString("D"),
                    ["CP6_HISTORY_FORM_PIN"] = original.FormVersionId.ToString("D"), ["CP6_HISTORY_CURRENT_PIN"] = published.VersionId.ToString("D"),
                    ["CP6_HISTORY_LEGACY_PINNED"] = legacy.Pinned.ToString("D"), ["CP6_HISTORY_LEGACY_MISSING"] = legacy.Missing.ToString("D"),
                    ["CP6_HISTORY_TIMEZONE"] = LegacyCalendarZone, ["CP6_HISTORY_CALENDAR_FLOW"] = LegacyCalendarFlow,
                    ["CP6_HISTORY_ORDERED_IDS"] = string.Join(",", ordered), ["CP6_HISTORY_INSERTED_ID"] = added.Id.ToString("D")
                });
            await changes;
        }
        finally
        {
            await stop.CancelAsync();
            try { await changes; }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        Assert.Equal(2, await h.Db.Wf_FormDatas.CountAsync());
        Assert.Equal(4 + calendarRows.Length + 1, await h.Db.Wf_FlowInstances.CountAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_personal_form_v2.favorites WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_personal_form_v2.commands WHERE tenant_id={h.Tenant} AND operation='FORM_FAVORITE_SET' AND completed").SingleAsync());
        Assert.Equal(original.FormVersionId, (await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync(f => f.Id == unbound.FormDataId)).FormDefVersionId);
    }
}
