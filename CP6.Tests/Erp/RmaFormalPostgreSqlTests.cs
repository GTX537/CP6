using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly JsonSerializerOptions RmaJson = new(JsonSerializerDefaults.Web);
    private static RmaApplicationOptionsV1 FormalCoordinates(Harness h) => new() { WarehouseSites = h.Db.Warehouses.AsNoTracking()
        .Select(x => new RmaWarehouseSiteV1 { TenantId = h.Tenant, WarehouseId = x.Id, SiteId = h.Db.Space_Sites.Where(s => s.SiteCode == "SITE").Select(s => s.Id).Single() }).ToArray() };
    private static RmaFormalOwnerV1 FormalRma(Harness h, IDataProtectionProvider? keys = null, RmaApplicationOptionsV1? coordinates = null, IRmaReturnSourcesV1? sources = null, IRmaInboundSourcesV1? inboundSources = null)
    {
        var authority = FormalRmaAuthority(h);
        return new(h.Db, authority, ScopeFence(h), new QualitySpecificationRuntimeV1("DEMO"),
            keys ?? new EphemeralDataProtectionProvider(), ContentOwner(h, new ContentStore(), new ContentInspector()), Options.Create(coordinates ?? FormalCoordinates(h)),sources,
            inboundSources == null ? null : new RmaInboundAdapterV1(h.Db,new StockMovementService(h.Db,new WmsSequenceService(h.Db),owner:authority),new WmsSequenceService(h.Db),inboundSources));
    }
    private static OperationsOwnerV1 FormalRmaAuthority(Harness h)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterScope("wms-return-correction","Return correction",[1,2,3,4,5],1); catalog.RegisterFields("wms-return-correction",[]);
        foreach (var definition in ReturnDispositionOwnersV2.All) { catalog.RegisterScope(definition.Resource,definition.Kind,[1,2,3,4,5],1); catalog.RegisterFields(definition.Resource,[]); }
        catalog.RegisterScope("wms-stock-qc","Stock QC",[1,2,3,4,5],1); catalog.RegisterFields("wms-stock-qc",[]);
        catalog.RegisterScope("quality-return-inspection","Return Quality",[1,2,3,4,5],5); catalog.RegisterFields("quality-return-inspection",[]);
        catalog.RegisterScope("wms-rma", "RMA", [1,2,3,4,5], 1);
        catalog.RegisterFields("wms-rma", [new("CustomerName","CustomerName"),new("OperatorCd","OperatorCd"),new("Remarks","Remarks"),new("ReturnReason","ReturnReason")]);
        catalog.RegisterScope("erp-business-partner", "Partner", [1,2,3,4,5], 1);
        catalog.RegisterFields("erp-business-partner", [new("BpName","Name")]);
        catalog.RegisterScope("wms-inbound-receipt","Receipt",[1,2,3,4,5],5);
        catalog.RegisterFields("wms-inbound-receipt",[new("UnitPrice","UnitPrice")]);
        foreach (var key in new[] { "file-content", "plm-engineering-product", "plm-engineering-revision" })
        { catalog.RegisterScope(key, key, [1,2,3,4,5], 5); catalog.RegisterFields(key, key == "file-content" ? [new("Content","Content"),new("FileName","Name")] : []); }
        catalog.Seal();
        return new OperationsOwnerV1(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private async Task<FulfilmentFixture> FormalShipment(Harness h, bool customer = true, bool ship = true)
    {
        // Existing operational warehouse. New draft creation must not bypass its physical-use guard.
        h.Db.Warehouses.Add(new() { WarehouseCd = "W01", WarehouseName = "Existing warehouse", WarehouseType = 1 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); await PhysicalSite(h, "SITE");
        await GrantBusinessPartnerIdentity(h); var partner = await SeedIdentityPartner(h);
        var code = await h.Db.BusinessPartners.Where(x => x.Id == partner).Select(x => x.BpCd).SingleAsync();
        var result = await AllocateFulfilment(h, formalCustomer: code);
        foreach (var action in new[] { "application-write", "application-submit", "application-withdraw" })
            await EngineeringAction(h, "wms-rma", action);
        if (ship) await result.Service.ShipAsync(result.Number, new() { CaptureReturnSource = !customer, CaptureReturnApplicationSource = customer }, null);
        return result;
    }
    private static RmaFulfilmentQueryV1 FormalSearch(RmaFormalContextV1 context, int size = 100) =>
        new(Assert.Single(context.ScopeOptions).OptionRef, null, null, null, false, null, size);
    private static RmaFormalCreateV1 FormalCreate(RmaFormalContextV1 context, RmaFulfilmentOptionV1 source, FulfilmentRefV1 customer) =>
        new(context.ScopeOptions[0].OptionRef, Guid.NewGuid(), customer, "DAMAGED", "RETURN_ONLY",
            [new(Guid.NewGuid(), source.OptionRef, new("1.12345678", source.Original.BaseQty.Uom), null, [])], null);
    private static RmaFormalTransitionV1 FormalTransition(RmaFormalApplicationV1 app) => new(app.ApplicationVersion, app.ApplicationRef, "Customer intent changed");
    private static string FormalEtag(RmaFormalApplicationV1 app) => '"' + app.ApplicationRef.Digest + '"';
    private static string FormalJson(object value) => JsonSerializer.Serialize(value, RmaJson);
    private static Task<int> FormalCount(Harness h) => h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_formal_v1.heads WHERE tenant_id={h.Tenant}").SingleAsync();

    [CurrentPermissionPostgresFact]
    public async Task RmaFormal_actual_sources_exact_ranges_edit_submit_withdraw_preserve_history_without_stock_effects()
    {
        await using var h = await Seed(); var shipment = await FormalShipment(h); var owner = FormalRma(h);
        var customer = (await RmaApplications(h).FulfilmentAsync(shipment.Id)).CustomerRef!;
        var context = await owner.ContextAsync(); Assert.Contains("wms.rma.application.write", context.Capabilities);
        var search = FormalSearch(context, 1); var first = await owner.SourcesAsync(search);
        Assert.Null(first.Items[0].RemainingAuthorizableQuantity); Assert.Equal("PARTIAL", first.Coverage);
        Assert.Contains("ENTITLEMENT_UNKNOWN", first.Items[0].Gates);
        var page = await owner.SourcesAsync(search with { Cursor = first.NextCursor });
        Assert.Equal(first.SnapshotRef, page.SnapshotRef); Assert.Equal(first.CapturedAt, page.CapturedAt); Assert.Null(page.NextCursor);
        Assert.NotEqual(first.Items[0].Original.ShipmentLineId, page.Items[0].Original.ShipmentLineId);
        Assert.Equal("RMA_CURSOR_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SourcesAsync(search with { Cursor = first.NextCursor, Keyword = "changed" }))).Message);
        var command = Guid.NewGuid(); var input = FormalCreate(context, first.Items[0], customer);
        var duplicateSelection = (await owner.SourcesAsync(search)).Items[0];
        input = input with { Lines = [input.Lines[0], input.Lines[0] with { LineId = Guid.NewGuid(), OriginalSelectionRef = duplicateSelection.OptionRef,
            RequestedQuantity = new("1", input.Lines[0].RequestedQuantity.Uom) }] };
        var txnCount = await h.Db.StockTransactions.CountAsync(); var stock = await h.Db.Stocks.Select(x => x.PhysicalQty).SingleAsync();
        var app = await owner.CreateAsync(command, input);
        Assert.Equal("DRAFT", app.State); Assert.Equal(new FulfilmentRangeV1("0", "1.12345678"), Assert.Single(app.RequestedLines[0].RequestedRanges));
        Assert.Equal(new FulfilmentRangeV1("1.12345678", "2.12345678"), Assert.Single(app.RequestedLines[1].RequestedRanges));
        var edit = new RmaFormalEditV1(1, app.ApplicationRef, customer, "CORRECTED", "REFUND", [input.Lines[0]], "Customer note", "Correct request");
        var edited = await owner.EditAsync(app.ApplicationId, Guid.NewGuid(), edit, FormalEtag(app));
        Assert.Equal(2, edited.ApplicationVersion); Assert.Equal(app.ApplicationRef, edited.PreviousVersionRef);
        Assert.Equal(2, Assert.Single(edited.RequestedLines).LineVersion);
        Assert.Equal("CAS_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SubmitAsync(app.ApplicationId, Guid.NewGuid(), FormalTransition(app), FormalEtag(app)))).Message);
        var submitted = await owner.SubmitAsync(app.ApplicationId, Guid.NewGuid(), FormalTransition(edited), FormalEtag(edited));
        Assert.Equal("SUBMITTED", submitted.State);
        Assert.Equal("RMA_APPLICATION_NOT_DRAFT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.EditAsync(app.ApplicationId, Guid.NewGuid(), edit with {
            ExpectedApplicationVersion = submitted.ApplicationVersion, ExpectedApplicationHeadRef = submitted.ApplicationRef }, FormalEtag(submitted)))).Message);
        var withdrawn = await owner.WithdrawAsync(app.ApplicationId, Guid.NewGuid(), FormalTransition(submitted), FormalEtag(submitted));
        Assert.Equal("WITHDRAWN", withdrawn.State); Assert.Equal(4, withdrawn.ApplicationVersion);
        Assert.Equal(FormalJson(app), FormalJson(await owner.ReadAsync(app.ApplicationId, 1)));
        Assert.Equal(FormalJson(app), FormalJson(await owner.CreateAsync(command, input)));
        Assert.Equal(1, await FormalCount(h)); Assert.Empty(await h.Db.RmaHeaders.ToArrayAsync());
        Assert.Equal(txnCount, await h.Db.StockTransactions.CountAsync()); Assert.Equal(stock, await h.Db.Stocks.Select(x => x.PhysicalQty).SingleAsync());
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_rma_formal_v1.versions WHERE tenant_id={h.Tenant}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task RmaFormal_original_command_survives_write_revocation_but_current_read_scope_still_applies()
    {
        await using var h = await Seed(); var f = await FormalShipment(h); var keys = new EphemeralDataProtectionProvider(); var o = FormalRma(h, keys);
        var context = await o.ContextAsync(); var input = FormalCreate(context, (await o.SourcesAsync(FormalSearch(context))).Items[0], (await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef!);
        var command = Guid.NewGuid(); var original = await o.CreateAsync(command, input);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 426 && x.ActionCode == "application-write").ExecuteDeleteAsync();
        Assert.Equal(FormalJson(original), FormalJson(await o.CreateAsync(command, input)));
        Assert.Equal("COMMAND_BINDING_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(command, input with { ReasonCode = "CHANGED" }))).Message);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(Guid.NewGuid(), input with { ClientApplicationId = Guid.NewGuid() }))).Status);
        var query = new RmaCommandQueryV1(input.ScopeOptionRef, command, "POST", RmaFormalOwnerV1.CreateRoute);
        using var found = JsonDocument.Parse(FormalJson(await o.QueryCommandAsync(query)));
        Assert.Equal("FOUND", found.RootElement.GetProperty("status").GetString());
        Assert.Equal(original.ApplicationRef.Digest, found.RootElement.GetProperty("resultRefs")[0].GetProperty("digest").GetString());
        using var absent = JsonDocument.Parse(FormalJson(await o.QueryCommandAsync(query with { CommandId = Guid.NewGuid() })));
        Assert.Equal("NOT_OBSERVED", absent.RootElement.GetProperty("status").GetString()); Assert.False(absent.RootElement.GetProperty("canInferNoEffect").GetBoolean());
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ReadAsync(original.ApplicationId))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(command, input))).Status);
        await using var foreign = await Seed(); await GrantRma(foreign);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => FormalRma(foreign, keys).ReadAsync(original.ApplicationId))).Status);
        Assert.Equal("RMA_SELECTION_NOT_FOUND", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => FormalRma(foreign, keys).SourcesAsync(FormalSearch(context)))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task RmaFormal_boundary_rejects_tamper_overclaim_precision_units_customer_and_missing_CAS()
    {
        await using var h = await Seed(); var f = await FormalShipment(h); var o = FormalRma(h); var context = await o.ContextAsync();
        var source = (await o.SourcesAsync(FormalSearch(context))).Items[0];
        var input = FormalCreate(context, source, (await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef!);
        foreach (var value in new[] { "0", "-1", "1.123456789", "10000000000000", "1e0" })
            Assert.Equal("VALIDATION_FAILED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(Guid.NewGuid(), input with {
                Lines = [input.Lines[0] with { RequestedQuantity = input.Lines[0].RequestedQuantity with { Value = value } }] }))).Message);
        Assert.Equal("RANGE_MEASURE_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(Guid.NewGuid(), input with {
            Lines = [input.Lines[0], input.Lines[0] with { LineId = Guid.NewGuid() }] }))).Message);
        Assert.Equal("RMA_UOM_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(Guid.NewGuid(), input with {
            Lines = [input.Lines[0] with { RequestedQuantity = new("1", new("OTHER", "1")) }] }))).Message);
        Assert.Equal("RMA_SELECTION_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(Guid.NewGuid(), input with {
            Lines = [input.Lines[0] with { OriginalSelectionRef = source.OptionRef with { Id = "forged" } }] }))).Message);
        Assert.Equal("RMA_SELECTION_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(Guid.NewGuid(), input with {
            Lines = [input.Lines[0] with { OriginalSelectionRef = source.OptionRef with { Digest = new string('a', 64) } }] }))).Message);
        var otherPartner = await Partners(h).ReadAsync(await SeedIdentityPartner(h)); var other = otherPartner.Reference;
        Assert.Equal("RMA_CUSTOMER_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(Guid.NewGuid(), input with {
            CustomerRef = new(other.Owner, other.Id.ToString(), other.Version, other.Digest) }))).Message);
        Assert.Equal(0, await FormalCount(h)); var app = await o.CreateAsync(Guid.NewGuid(), input);
        Assert.Equal("IF_MATCH_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.SubmitAsync(app.ApplicationId, Guid.NewGuid(), FormalTransition(app), null))).Message);
    }

    private sealed class FailFormalJournal : DbCommandInterceptor
    {
        public bool Enabled, Reached;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.StartsWith("INSERT INTO cp6_main_owner_v1.operations", StringComparison.Ordinal))
            { Reached = true; throw new InvalidOperationException("INJECTED_FORMAL_JOURNAL_FAILURE"); }
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaFormal_journal_failure_rolls_back_application_binding_and_actor_facts()
    {
        var fault = new FailFormalJournal(); await using var h = await Seed(fault); var f = await FormalShipment(h); var o = FormalRma(h);
        var context = await o.ContextAsync(); var input = FormalCreate(context, (await o.SourcesAsync(FormalSearch(context))).Items[0], (await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef!);
        Task<int> Facts() => h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_formal_v1.facts WHERE tenant_id={h.Tenant}").SingleAsync();
        var before = await Facts(); fault.Enabled = true;
        Assert.Equal("INJECTED_FORMAL_JOURNAL_FAILURE", (await Assert.ThrowsAsync<InvalidOperationException>(() => o.CreateAsync(Guid.NewGuid(), input))).Message);
        Assert.True(fault.Reached); Assert.Equal(0, await FormalCount(h)); Assert.Equal(before, await Facts());
        fault.Enabled = false; Assert.Equal("DRAFT", (await o.CreateAsync(Guid.NewGuid(), input)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaFormal_old_shipments_without_captured_customer_do_not_gain_guessed_identity()
    {
        await using var h = await Seed(); var f = await FormalShipment(h, customer: false); var o = FormalRma(h);
        var old = await RmaApplications(h).FulfilmentAsync(f.Id); Assert.Null(old.CustomerRef);
        Assert.Empty((await o.SourcesAsync(FormalSearch(await o.ContextAsync()))).Items);
        Assert.Null((await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef);
    }

    private static HttpRequestMessage FormalRequest(Dictionary<string, string> cookies, HttpMethod method, string route,
        object? value = null, Guid? command = null, string? etag = null)
    {
        var request = OperationsRequest(cookies, method, route, value);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString()); request.Headers.CacheControl = new() { NoStore = true };
        if (command != null) request.Headers.Add("Idempotency-Key", command.ToString());
        if (etag != null) request.Headers.TryAddWithoutValidation("If-Match", etag); return request;
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaFormal_evidence_keeps_actual_file_parent_authority_and_rechecks_submit()
    {
        await using var h = await Seed(); var f = await FormalShipment(h);
        byte[] bytes = [65,66,67]; var upload = await ContentInput(h, bytes); var file = ContentOwner(h, new ContentStore(), new ContentInspector());
        await file.UploadAsync(upload, new MemoryStream(bytes)); var saved = (await file.ReadAsync(upload.FileId)).ContentRef;
        var evidence = new FulfilmentRefV1(saved.Owner, saved.Id.ToString(), saved.Version, saved.Digest);
        var o = FormalRma(h); var context = await o.ContextAsync();
        var input = FormalCreate(context, (await o.SourcesAsync(FormalSearch(context))).Items[0], (await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef!);
        input = input with { Lines = [input.Lines[0] with { EvidenceRefs = [evidence] }] };
        var app = await o.CreateAsync(Guid.NewGuid(), input); Assert.Equal(evidence, Assert.Single(app.EvidenceRefs));
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "file-content").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.SubmitAsync(app.ApplicationId, Guid.NewGuid(), FormalTransition(app), FormalEtag(app)))).Status);
        Assert.Equal(1, (await o.ReadAsync(app.ApplicationId)).ApplicationVersion);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaFormal_lost_commit_ack_recovers_exact_original_without_duplicate_application()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var f = await FormalShipment(h); var o = FormalRma(h);
        var context = await o.ContextAsync(); var input = FormalCreate(context, (await o.SourcesAsync(FormalSearch(context))).Items[0], (await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef!);
        var command = Guid.NewGuid(); fault.Armed = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(command, input))).Message);
        Assert.Equal(1, await FormalCount(h));
        var recovered = await o.CreateAsync(command, input); Assert.Equal(input.ClientApplicationId, recovered.ApplicationId);
        using var query = JsonDocument.Parse(FormalJson(await o.QueryCommandAsync(new(input.ScopeOptionRef, command, "POST", RmaFormalOwnerV1.CreateRoute))));
        Assert.Equal("FOUND", query.RootElement.GetProperty("status").GetString());
        Assert.Equal(FormalJson(recovered), FormalJson(await o.ReadAsync(input.ClientApplicationId))); Assert.Equal(1, await FormalCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaFormal_current_trusted_coordinates_and_site_enablement_invalidate_old_selection()
    {
        await using var h = await Seed(); var f = await FormalShipment(h); var keys = new EphemeralDataProtectionProvider();
        var coordinates = FormalCoordinates(h); var o = FormalRma(h, keys, coordinates); var context = await o.ContextAsync();
        var input = FormalCreate(context, (await o.SourcesAsync(FormalSearch(context))).Items[0], (await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef!);
        Assert.Empty((await FormalRma(h, keys, new()).ContextAsync()).ScopeOptions);
        var changed = new RmaApplicationOptionsV1 { WarehouseSites = [new() { TenantId = h.Tenant, WarehouseId = coordinates.WarehouseSites[0].WarehouseId, SiteId = await PhysicalSite(h,"OTHER") }] };
        Assert.Equal("SCOPE_NOT_VISIBLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => FormalRma(h, keys, changed).CreateAsync(Guid.NewGuid(), input))).Message);
        await h.Db.Space_Sites.Where(s => s.Id == coordinates.WarehouseSites[0].SiteId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
        Assert.Equal("SCOPE_NOT_VISIBLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(Guid.NewGuid(), input))).Message);
        Assert.Equal(0, await FormalCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaFormal_actual_HTTP_has_closed_contract_headers_CAS_and_original_command_recovery()
    {
        await using var h = await Seed(); var f = await FormalShipment(h, ship: false);
        var coordinates = Assert.Single(FormalCoordinates(h).WarehouseSites);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, new Dictionary<string,string> {
            ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString(), ["QualitySpecification__EvidenceMode"] = "DEMO",
            ["RmaApplication__WarehouseSites__0__TenantId"] = coordinates.TenantId.ToString(),
            ["RmaApplication__WarehouseSites__0__WarehouseId"] = coordinates.WarehouseId.ToString(),
            ["RmaApplication__WarehouseSites__0__SiteId"] = coordinates.SiteId.ToString() });
        var cookies = await Login(host, h); var evidence = new Dictionary<string, JsonElement>();
        async Task<T> Receive<T>(HttpRequestMessage request, string name, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var response = await host.Send(request); var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, response.StatusCode + ": " + text);
            Assert.True(response.Headers.CacheControl?.NoStore); using var json = JsonDocument.Parse(text); evidence[name] = json.RootElement.Clone();
            if (typeof(T) == typeof(RmaFormalApplicationV1)) Assert.Equal('"' + json.RootElement.GetProperty("applicationRef").GetProperty("digest").GetString() + '"', response.Headers.ETag!.Tag);
            return JsonSerializer.Deserialize<T>(text, RmaJson)!;
        }
        using (var ship = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"api/wms/outbound-order/{f.Number}/ship", new { captureReturnApplicationSource = true })))
            Assert.True(ship.IsSuccessStatusCode, await OperationsFailure(ship));
        var context = await Receive<RmaFormalContextV1>(FormalRequest(cookies, HttpMethod.Get, "api/wms/rma/v1/context"), "RmaContext");
        var search = FormalSearch(context); var sources = await Receive<RmaFulfilmentOptionPageV1>(FormalRequest(cookies, HttpMethod.Post,
            "api/wms/rma/v1/original-fulfilment-options/search", search), "OriginalFulfilmentOptionPage");
        var input = FormalCreate(context, sources.Items[0], (await RmaApplications(h).FulfilmentAsync(f.Id)).CustomerRef!);
        var id = Guid.NewGuid(); var path = "api/wms/rma/v1/applications";
        var app = await Receive<RmaFormalApplicationV1>(FormalRequest(cookies, HttpMethod.Post, path, input, id), "RmaApplication", HttpStatusCode.Created);
        var query = new RmaCommandQueryV1(input.ScopeOptionRef, id, "POST", RmaFormalOwnerV1.CreateRoute);
        await Receive<JsonElement>(FormalRequest(cookies, HttpMethod.Post, "api/wms/rma/v1/commands/query", query), "CommandQueryResultFound");
        await Receive<JsonElement>(FormalRequest(cookies, HttpMethod.Post, "api/wms/rma/v1/commands/query", query with { CommandId = Guid.NewGuid() }), "CommandQueryResultNotObserved");
        var missing = await Receive<JsonElement>(FormalRequest(cookies, HttpMethod.Post, path + $"/{app.ApplicationId}/submit", FormalTransition(app), Guid.NewGuid()), "ProblemIfMatch", (HttpStatusCode)428);
        Assert.Equal("IF_MATCH_REQUIRED", missing.GetProperty("code").GetString());
        var submitted = await Receive<RmaFormalApplicationV1>(FormalRequest(cookies, HttpMethod.Post, path + $"/{app.ApplicationId}/submit", FormalTransition(app), Guid.NewGuid(), FormalEtag(app)), "SubmittedApplication");
        Assert.Equal("SUBMITTED", submitted.State);
        var extra = JsonSerializer.SerializeToNode(input, RmaJson)!.AsObject(); extra["authorized"] = true;
        foreach (var invalid in new[] { extra.ToJsonString(), FormalJson(input).Replace("\"lines\":[", "\"lines\":[null,"), "{}" })
        {
            var request = FormalRequest(cookies, HttpMethod.Post, path, command: Guid.NewGuid());
            request.Content = new StringContent(invalid, System.Text.Encoding.UTF8, "application/json");
            await Receive<JsonElement>(request, "ProblemValidation", HttpStatusCode.BadRequest);
        }
        var noHeader = FormalRequest(cookies, HttpMethod.Get, "api/wms/rma/v1/context"); noHeader.Headers.Remove("X-Correlation-Id");
        await Receive<JsonElement>(noHeader, "ProblemHeader", HttpStatusCode.BadRequest);
        var noCsrf = FormalRequest(cookies, HttpMethod.Post, path, input, Guid.NewGuid()); noCsrf.Headers.Remove("X-CSRF-TOKEN");
        using (var denied = await host.Send(noCsrf)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var evidencePath = Environment.GetEnvironmentVariable("CP6_OUTPUT_RELATIONS_WIRE_EVIDENCE");
        if (evidencePath != null) await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(evidence, RmaJson));
    }
}
