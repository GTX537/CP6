using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static RmaApplicationQueryV1 FormalList(RmaFormalContextV1 context, int size = 25) =>
        new(context.ScopeOptions[0].OptionRef, null, null, null, [], null, null, null, size);

    [CurrentPermissionPostgresFact]
    public async Task RmaFormalEntry_listing_freezes_members_heads_and_filter_but_rechecks_current_access()
    {
        await using var h = await Seed(); var f = await FormalShipment(h); var o = FormalRma(h); var context = await o.ContextAsync();
        var input = FormalCreate(context, (await o.SourcesAsync(FormalSearch(context))).Items[0], (await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef!);
        var first = await o.CreateAsync(Guid.NewGuid(), input);
        var second = await o.CreateAsync(Guid.NewGuid(), input with { ClientApplicationId = Guid.NewGuid() });
        var query = FormalList(context, 1); var page = await o.ListAsync(query);
        Assert.Equal(second.ApplicationId, Assert.Single(page.Items).RmaId); Assert.NotNull(page.NextCursor); Assert.Equal("PARTIAL", page.Coverage);
        var submitted = await o.SubmitAsync(first.ApplicationId, Guid.NewGuid(), FormalTransition(first), FormalEtag(first));
        await o.CreateAsync(Guid.NewGuid(), input with { ClientApplicationId = Guid.NewGuid() });
        var frozen = await o.ListAsync(query with { Cursor = page.NextCursor });
        Assert.Equal(first.ApplicationId, Assert.Single(frozen.Items).RmaId); Assert.Equal(first.ApplicationRef, frozen.Items[0].HeadRef);
        Assert.Equal("DRAFT", frozen.Items[0].ApplicationState); Assert.Equal("UNKNOWN", frozen.Items[0].PhysicalState);
        Assert.Equal(page.SnapshotRef, frozen.SnapshotRef); Assert.Equal(page.CapturedAt, frozen.CapturedAt); Assert.Null(frozen.NextCursor);
        var filtered = await o.ListAsync(FormalList(context) with { States = ["SUBMITTED"], CustomerRef = input.CustomerRef, RecordedFrom = first.RequestedAt, RecordedTo = first.RequestedAt });
        Assert.Equal(submitted.ApplicationRef, Assert.Single(filtered.Items).HeadRef);
        Assert.Equal("RMA_CURSOR_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ListAsync(query with { Cursor = page.NextCursor, States = ["SUBMITTED"] }))).Message);
        Assert.Equal("APPLICATION_FILTER_SOURCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ListAsync(query with { InvoiceLineRef = input.CustomerRef }))).Message);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ListAsync(query with { Cursor = page.NextCursor }))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task RmaFormalEntry_edit_context_keeps_original_remarks_and_fails_if_the_field_is_hidden()
    {
        await using var h = await Seed(); var f = await FormalShipment(h); var o = FormalRma(h); var context = await o.ContextAsync();
        var input = FormalCreate(context, (await o.SourcesAsync(FormalSearch(context))).Items[0], (await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef!) with { Remarks = "Original private note" };
        var app = await o.CreateAsync(Guid.NewGuid(), input); var edit = await o.EditContextAsync(app.ApplicationId);
        Assert.Equal(input.Remarks, edit.Remarks); Assert.Equal(app.ApplicationRef, edit.Application.ApplicationRef);
        var next = await o.EditAsync(app.ApplicationId, Guid.NewGuid(), new(1, app.ApplicationRef, app.CustomerRef, "CORRECTED", app.RequestedOutcome, input.Lines, edit.Remarks, "Correct reason"), FormalEtag(app));
        Assert.Equal(input.Remarks, (await o.EditContextAsync(app.ApplicationId)).Remarks); Assert.Equal(2, next.ApplicationVersion);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-rma", FieldName = "Remarks", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.EditContextAsync(app.ApplicationId))).Message);
        Assert.Equal(next.ApplicationRef, (await o.ReadAsync(app.ApplicationId)).ApplicationRef);
    }

    [CurrentPermissionPostgresFact]
    public async Task RmaFormalEntry_read_only_remarks_can_be_preserved_but_cannot_be_changed_or_cleared()
    {
        await using var h = await Seed(); var f = await FormalShipment(h); var o = FormalRma(h); var context = await o.ContextAsync();
        var input = FormalCreate(context, (await o.SourcesAsync(FormalSearch(context))).Items[0], (await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef!) with { Remarks = "Keep this note" };
        var app = await o.CreateAsync(Guid.NewGuid(), input);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-rma", FieldName = "Remarks", Access = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var edit = new RmaFormalEditV1(1, app.ApplicationRef, app.CustomerRef, "CORRECTED", app.RequestedOutcome, input.Lines, input.Remarks, "Correct reason");
        foreach (var remarks in new[] { null, "Changed" })
            Assert.Equal("FIELD_WRITE_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.EditAsync(app.ApplicationId, Guid.NewGuid(), edit with { Remarks = remarks }, FormalEtag(app)))).Message);
        Assert.Equal(1, (await o.ReadAsync(app.ApplicationId)).ApplicationVersion);
        var updated = await o.EditAsync(app.ApplicationId, Guid.NewGuid(), edit, FormalEtag(app));
        Assert.Equal(2, updated.ApplicationVersion); Assert.Equal(input.Remarks, (await o.EditContextAsync(app.ApplicationId)).Remarks);
    }

    [CurrentPermissionPostgresFact]
    public async Task RmaFormalEntry_actual_HTTP_binds_nested_query_and_returns_canonical_summary_with_lossless_edit_context()
    {
        await using var h = await Seed(); var f = await FormalShipment(h); var coordinates = Assert.Single(FormalCoordinates(h).WarehouseSites);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, new Dictionary<string,string> {
            ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString(), ["QualitySpecification__EvidenceMode"] = "DEMO",
            ["RmaApplication__WarehouseSites__0__TenantId"] = coordinates.TenantId.ToString(),
            ["RmaApplication__WarehouseSites__0__WarehouseId"] = coordinates.WarehouseId.ToString(),
            ["RmaApplication__WarehouseSites__0__SiteId"] = coordinates.SiteId.ToString() });
        var cookies = await Login(host, h); const string root = "api/wms/rma/v1/";
        async Task<T> Receive<T>(HttpMethod method, string route, object? body = null, Guid? command = null)
        {
            using var response = await host.Send(FormalRequest(cookies, method, root + route, body, command));
            var text = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, response.StatusCode + ": " + text);
            Assert.True(response.Headers.CacheControl?.NoStore); return JsonSerializer.Deserialize<T>(text, RmaJson)!;
        }
        var context = await Receive<RmaFormalContextV1>(HttpMethod.Get, "context");
        var options = await Receive<RmaFulfilmentOptionPageV1>(HttpMethod.Post, "original-fulfilment-options/search", FormalSearch(context));
        var input = FormalCreate(context, options.Items[0], (await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef!) with { Remarks = "Original note" };
        var app = await Receive<RmaFormalApplicationV1>(HttpMethod.Post, "applications", input, Guid.NewGuid());
        var option = context.ScopeOptions[0].OptionRef;
        var q = "scopeOptionRef.owner=" + Uri.EscapeDataString(option.Owner) + "&scopeOptionRef.id=" + Uri.EscapeDataString(option.Id)
            + "&scopeOptionRef.version=" + option.Version + "&scopeOptionRef.digest=" + option.Digest + "&states=DRAFT&pageSize=1";
        var page = await Receive<RmaApplicationPageV1>(HttpMethod.Get, "applications?" + q);
        Assert.Equal(app.ApplicationRef, Assert.Single(page.Items).HeadRef);
        var edit = await Receive<RmaApplicationEditContextV1>(HttpMethod.Get, $"applications/{app.ApplicationId}/edit-context");
        Assert.Equal(input.Remarks, edit.Remarks); Assert.Equal(app.ApplicationRef, edit.Application.ApplicationRef);
        using var invalid = await host.Send(FormalRequest(cookies, HttpMethod.Get, root + "applications?" + q + "&states=APPROVED"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var evidence = Environment.GetEnvironmentVariable("CP6_OUTPUT_RELATIONS_WIRE_EVIDENCE");
        if (evidence != null) await File.WriteAllTextAsync(evidence, FormalJson(new { ApplicationPage = page, RmaApplication = edit.Application }));
    }
}
