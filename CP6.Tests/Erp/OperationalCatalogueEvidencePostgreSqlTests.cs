using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string CatalogueConversionPath = "api/operational-catalogue/v1/unit-conversion";
    private const string CatalogueSourceKey = "OperationalCatalogue__Source";

    private static string CatalogueMetricPath(Guid warehouse) =>
        $"api/operational-catalogue/v1/warehouses/{warehouse:D}/capacity-metric";

    private static Task<AuthenticationApiProcess> StartCatalogue(Harness h, string source = "MAIN_NATIVE_V1") =>
        AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string, string> { [CatalogueSourceKey] = source });

    private static async Task<JsonElement> ReadCatalogue(AuthenticationApiProcess host, Dictionary<string, string> cookies,
        string path, object input)
    {
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path, input));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }

    private static JsonElement KnownCatalogueEvidence(JsonElement result)
    {
        Assert.Equal("KNOWN", result.GetProperty("state").GetString());
        return result.GetProperty("evidence");
    }

    private static void AssertUnknownCatalogueEvidence(JsonElement result)
    {
        Assert.Equal("UNKNOWN", result.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("evidence").ValueKind);
    }

    [CurrentPermissionPostgresFact]
    public async Task Operational_catalogue_approved_native_ratio_can_be_pinned_by_draft_bom_before_stock_posting()
    {
        await using var h = await Seed();
        var parent = await BomParent(h);
        await GrantProductUnitEdit(h);
        await GrantUomProfile(h);
        var unitOwner = Units(h);
        var basis = await MakeUnit(unitOwner, BaseUnit("个", "COUNT", 8));
        var unit = await MakeUnit(unitOwner, BaseUnit("包装 箱", "COUNT", 8) with
        { Mode = "RATIO", BaseRef = basis.Reference, Numerator = "10" });
        var productId = await SeedIdentityProduct(h);
        await h.Db.ProductMasters.Where(row => row.Id == productId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.QtyUnit, unit.Profile.Code));
        var product = await ProductIdentity(h).ReadAsync(productId);
        var binding = await ProductUnits(h).BindAsync(new CommercialProductUnitCommandV1
        {
            OperationId = Guid.NewGuid(), ProductId = productId, ExpectedBindingVersion = "absent",
            ProductVersion = product.Version, ProfileRef = unit.Reference
        });
        Assert.Equal(unit.Reference, (await ProductUnits(h).ReadAsync(productId, binding.Version)).ProfileRef);

        await using var host = await StartCatalogue(h);
        var cookies = await Login(host, h);
        const string quantity = "1.00000001";
        var evidence = KnownCatalogueEvidence(await ReadCatalogue(host, cookies, CatalogueConversionPath,
            new { from = unit.Reference, to = basis.Reference, quantity }));
        Assert.True(evidence.GetProperty("isCurrent").GetBoolean());
        Assert.Equal("10.00000010", evidence.GetProperty("convertedQuantity").GetString());
        Assert.Equal(unit.OriginalJson, evidence.GetProperty("from").GetProperty("originalJson").GetString());
        Assert.Equal(basis.OriginalJson, evidence.GetProperty("base").GetProperty("originalJson").GetString());
        var reference = evidence.GetProperty("from").GetProperty("reference")
            .Deserialize<UomProfileRefV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bom = Boms(h);
        var code = await h.Db.ProductMasters.Where(row => row.Id == productId).Select(row => row.ProductCd).SingleAsync();
        var item = Assert.Single(await bom.ItemChoicesAsync("BUSINESS", code));
        Assert.Equal(product.Reference, item.Reference);
        Assert.Equal("Main.Erp.ProductIdentity.v1", item.Reference.Owner);
        var create = BomCreate(parent);
        var occurrence = Assert.Single(create.Body.Occurrences) with
        { ItemRef = item.Reference, UnitRef = reference, Quantity = quantity };
        await bom.CommandAsync(create with { Body = create.Body with { Occurrences = [occurrence] } });
        var original = await bom.ReadAsync(create.BomId, "1");
        Assert.Equal(occurrence, Assert.Single(original.Body.Occurrences));
        Assert.Equal("DRAFT", (await BomRevisions(h).ReadAsync(parent.Reference.Id, null)).State);
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Operational_catalogue_historical_conversion_keeps_ratio_and_high_precision_after_base_revision()
    {
        await using var h = await Seed();
        await GrantUomProfile(h);
        var owner = Units(h);
        var basis = await MakeUnit(owner, BaseUnit("mL", "VOLUME", 40));
        var unit = await MakeUnit(owner, BaseUnit("合成 计量", "VOLUME", 40) with
        { Mode = "RATIO", BaseRef = basis.Reference, Numerator = "3", Denominator = "2" });
        const string quantity = "1234567890123.0000000000000000000000000000000000000002";
        const string converted = "1851851835184.5000000000000000000000000000000000000003";
        await using var host = await StartCatalogue(h);
        var cookies = await Login(host, h);
        var current = new { from = unit.Reference, to = basis.Reference, quantity };
        var first = KnownCatalogueEvidence(await ReadCatalogue(host, cookies, CatalogueConversionPath, current));
        Assert.Equal(converted, first.GetProperty("convertedQuantity").GetString());
        var revised = await owner.CommandAsync(Revise(basis, basis.Profile with { Name = "升版后的原基本单位" }));

        AssertUnknownCatalogueEvidence(await ReadCatalogue(host, cookies, CatalogueConversionPath, current));
        var old = KnownCatalogueEvidence(await ReadCatalogue(host, cookies, CatalogueConversionPath,
            new { from = unit.Reference, to = basis.Reference, quantity, requireCurrent = false }));
        Assert.False(old.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(quantity, old.GetProperty("quantity").GetString());
        Assert.Equal(converted, old.GetProperty("convertedQuantity").GetString());
        Assert.Equal(unit.OriginalJson, old.GetProperty("from").GetProperty("originalJson").GetString());
        Assert.Equal(basis.OriginalJson, old.GetProperty("base").GetProperty("originalJson").GetString());
        var currentBase = basis.Reference with { Version = revised.Version, Digest = revised.Digest };
        AssertUnknownCatalogueEvidence(await ReadCatalogue(host, cookies, CatalogueConversionPath,
            new { from = unit.Reference, to = currentBase, quantity, requireCurrent = false }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Operational_catalogue_capacity_metric_preserves_policy_units_and_exact_history_after_disable()
    {
        await using var h = await Seed();
        var f = await RuleReferencesSetup(h);
        var unit = await MakeUnit(Units(h), BaseUnit("PALLET", "COUNT", 8) with
        { Mode = "RATIO", BaseRef = f.Unit.Reference, Numerator = "12" });
        const string metricId = " 计数/容量 ";
        var definition = f.Command.Definition with
        {
            Metrics = [new(metricId, "COUNT", [f.Unit.Reference, unit.Reference])],
            Domains = [f.Command.Definition.Domains[0] with { MetricIds = [metricId] }]
        };
        var owner = RuleReferences(h);
        var command = f.Command with { Definition = definition };
        var approved = await owner.SaveAsync(f.Warehouse, command);
        var original = await owner.HistoryAsync(f.Warehouse, approved.Reference.Version);
        await using var host = await StartCatalogue(h);
        var cookies = await Login(host, h);
        var path = CatalogueMetricPath(f.Warehouse);
        var current = KnownCatalogueEvidence(await ReadCatalogue(host, cookies, path, new { metricId }));
        Assert.True(current.GetProperty("isCurrent").GetBoolean());
        Assert.False(current.GetProperty("admissionGrant").GetBoolean());
        Assert.Equal(metricId, current.GetProperty("metric").GetProperty("id").GetString());
        Assert.Equal("COUNT", current.GetProperty("metric").GetProperty("dimensionKey").GetString());
        Assert.Equal(approved.Reference.Digest, current.GetProperty("policy").GetProperty("digest").GetString());
        Assert.Equal(f.Unit.OriginalJson, current.GetProperty("base").GetProperty("originalJson").GetString());
        Assert.Contains(current.GetProperty("units").EnumerateArray(),
            row => row.GetProperty("originalJson").GetString() == unit.OriginalJson);
        Assert.Equal(8, current.GetProperty("precisions")[0].GetProperty("scale").GetInt32());

        await owner.SaveAsync(f.Warehouse, new(Guid.NewGuid(), approved.Reference.Version, definition with { Enabled = false }, "停用本定义"));
        AssertUnknownCatalogueEvidence(await ReadCatalogue(host, cookies, path, new { metricId }));
        AssertUnknownCatalogueEvidence(await ReadCatalogue(host, cookies, path, new { metricId, policy = approved.Reference }));
        var history = KnownCatalogueEvidence(await ReadCatalogue(host, cookies, path,
            new { metricId, policy = approved.Reference, requireCurrent = false }));
        Assert.False(history.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(current.GetProperty("metric").GetRawText(), history.GetProperty("metric").GetRawText());
        Assert.Equal(current.GetProperty("units").GetRawText(), history.GetProperty("units").GetRawText());
        Assert.Equal(original.OriginalJson, (await owner.HistoryAsync(f.Warehouse, "1")).OriginalJson);
        Assert.Equal(approved, await owner.SaveAsync(f.Warehouse, command));
        Assert.Equal(approved, (await owner.OperationAsync(f.Warehouse, command.OperationId)).Result);
    }

    [CurrentPermissionPostgresFact]
    public async Task Operational_catalogue_unknown_sources_do_not_fall_back_on_code_dimension_or_caller_owner()
    {
        await using var h = await Seed();
        var f = await RuleReferencesSetup(h);
        var otherBase = await MakeUnit(Units(h), BaseUnit("Other", "COUNT", 8));
        await using var host = await StartCatalogue(h);
        var cookies = await Login(host, h);
        var path = CatalogueMetricPath(f.Warehouse);
        AssertUnknownCatalogueEvidence(await ReadCatalogue(host, cookies, path, new { metricId = "unit-count" }));
        var approved = await RuleReferences(h).SaveAsync(f.Warehouse, f.Command);
        AssertUnknownCatalogueEvidence(await ReadCatalogue(host, cookies, path, new { metricId = "UNIT-COUNT" }));
        AssertUnknownCatalogueEvidence(await ReadCatalogue(host, cookies, path,
            new { metricId = "unit-count", policy = approved.Reference with { Digest = new string('0', 64) } }));

        foreach (var to in new[] { otherBase.Reference, f.Unit.Reference with { Owner = "WMS-MASTER" },
            f.Unit.Reference with { Digest = new string('0', 64) }, f.Unit.Reference with { Version = "999" } })
            AssertUnknownCatalogueEvidence(await ReadCatalogue(host, cookies, CatalogueConversionPath,
                new { from = f.Unit.Reference, to, quantity = "1" }));

        using var injected = await host.Send(OperationsRequest(cookies, HttpMethod.Post, CatalogueConversionPath,
            new { from = f.Unit.Reference, to = f.Unit.Reference, quantity = "1", source = "MAIN_NATIVE_V1" }));
        Assert.Equal(HttpStatusCode.BadRequest, injected.StatusCode);
        await using var unconfigured = await StartCatalogue(h, "UNRECOGNIZED");
        var otherCookies = await Login(unconfigured, h);
        AssertUnknownCatalogueEvidence(await ReadCatalogue(unconfigured, otherCookies, path, new { metricId = "unit-count" }));
        AssertUnknownCatalogueEvidence(await ReadCatalogue(unconfigured, otherCookies, CatalogueConversionPath,
            new { from = f.Unit.Reference, to = f.Unit.Reference, quantity = "1" }));
        using var old = await unconfigured.Send(OperationsRequest(otherCookies, HttpMethod.Get,
            $"api/uom-profiles/owner-v1/{f.Unit.Reference.Id:D}?version={f.Unit.Reference.Version}"));
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Operational_catalogue_current_native_query_permissions_and_tenant_apply_to_history()
    {
        await using var h = await Seed();
        var f = await RuleReferencesSetup(h);
        var approved = await RuleReferences(h).SaveAsync(f.Warehouse, f.Command);
        var metric = new { metricId = "unit-count", policy = approved.Reference, requireCurrent = false };
        var conversion = new { from = f.Unit.Reference, to = f.Unit.Reference, quantity = "1", requireCurrent = false };
        await using var host = await StartCatalogue(h);
        var cookies = await Login(host, h);
        KnownCatalogueEvidence(await ReadCatalogue(host, cookies, CatalogueMetricPath(f.Warehouse), metric));
        KnownCatalogueEvidence(await ReadCatalogue(host, cookies, CatalogueConversionPath, conversion));

        await using var foreign = await Seed();
        await GrantMaster(foreign);
        await GrantUomProfile(foreign);
        var foreignCookies = await Login(host, foreign);
        using var foreignMetric = await host.Send(OperationsRequest(foreignCookies, HttpMethod.Post, CatalogueMetricPath(f.Warehouse), metric));
        Assert.Equal(HttpStatusCode.NotFound, foreignMetric.StatusCode);
        using var foreignConversion = await host.Send(OperationsRequest(foreignCookies, HttpMethod.Post, CatalogueConversionPath, conversion));
        Assert.Equal(HttpStatusCode.Forbidden, foreignConversion.StatusCode);

        await RevokeTransferAction(h, "erp-uom-profile", "query");
        using var revokedMetric = await host.Send(OperationsRequest(cookies, HttpMethod.Post, CatalogueMetricPath(f.Warehouse), metric));
        Assert.Equal(HttpStatusCode.Forbidden, revokedMetric.StatusCode);
        using var revokedConversion = await host.Send(OperationsRequest(cookies, HttpMethod.Post, CatalogueConversionPath, conversion));
        Assert.Equal(HttpStatusCode.Forbidden, revokedConversion.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Operational_catalogue_warehouse_and_derived_unit_scope_do_not_grant_base_unit_read()
    {
        await using var h = await Seed();
        var f = await RuleReferencesSetup(h);
        var derived = await MakeUnit(Units(h), BaseUnit("BOX", "COUNT", 8) with
        { Mode = "RATIO", BaseRef = f.Unit.Reference, Numerator = "10" });
        var command = f.Command with
        {
            Definition = f.Command.Definition with { Metrics = [new("unit-count", "COUNT", [derived.Reference])] }
        };
        var policy = await RuleReferences(h).SaveAsync(f.Warehouse, command);
        var admin = await MappingAdmin(h);
        await BindStandalone(h, admin, "warehouse", f.Warehouse);
        await BindStandalone(h, admin, "uom-profile", derived.Reference.Id);
        await StandaloneScope(h, admin);
        var catalogue = new OperationalCatalogueEvidenceAdapter(h.Db, RuleReferenceAuthority(h), "MAIN_NATIVE_V1");
        var metric = new OperationalCapacityMetricQueryV1("unit-count", policy.Reference, false);
        var conversion = new OperationalUnitConversionQueryV1(derived.Reference, derived.Reference, "1", false);

        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            catalogue.ReadCapacityMetricAsync(f.Warehouse, metric))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => catalogue.ReadConversionAsync(conversion))).Status);
        await BindStandalone(h, admin, "uom-profile", f.Unit.Reference.Id);
        Assert.Equal(f.Unit.Reference, (await catalogue.ReadCapacityMetricAsync(f.Warehouse, metric)).Evidence!.Base.Reference);
        Assert.Equal(f.Unit.Reference, (await catalogue.ReadConversionAsync(conversion)).Evidence!.Base.Reference);
    }

    [CurrentPermissionPostgresFact]
    public async Task Operational_catalogue_concurrent_native_base_writer_waits_and_recovers_one_original_key()
    {
        var hook = new CommandHook();
        await using var h = await Seed(hook);
        var f = await RuleReferencesSetup(h);
        var policy = await RuleReferences(h).SaveAsync(f.Warehouse, f.Command);
        var catalogue = new OperationalCatalogueEvidenceAdapter(h.Db, RuleReferenceAuthority(h), "MAIN_NATIVE_V1");
        var revise = Revise(f.Unit, f.Unit.Profile with { Enabled = false });
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        await peer.Database.OpenConnectionAsync();
        await observer.Database.OpenConnectionAsync();
        var writer = Units(h, peer);
        var writerPid = ((NpgsqlConnection)peer.Database.GetDbConnection()).ProcessID;
        Task<UomProfileCommandResultV1>? pending = null;
        var waitObserved = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("FROM cp6_uom_profile_v1.versions", StringComparison.Ordinal)) return;
            hook.After = null;
            var readerPid = ((NpgsqlConnection)command.Connection!).ProcessID;
            Assert.True(await observer.Database.SqlQuery<bool>($"""
                SELECT (count(*)=2) AS "Value" FROM pg_catalog.pg_locks
                WHERE pid={readerPid} AND granted AND mode='ShareLock' AND relation IN
                    ('cp6_location_rule_refs_v1.heads'::regclass, 'cp6_uom_profile_v1.heads'::regclass)
                """).SingleAsync());
            pending = writer.CommandAsync(revise);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (!await observer.Database.SqlQuery<bool>($"""
                SELECT ({readerPid}=ANY(pg_catalog.pg_blocking_pids({writerPid}))) AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            waitObserved = true;
            Assert.False(pending.IsCompleted);
        };

        OperationalCatalogueResolutionV1<OperationalCapacityMetricEvidenceV1>? first = null;
        try { first = await catalogue.ReadCapacityMetricAsync(f.Warehouse, new("unit-count", policy.Reference)); }
        finally { if (pending is not null) await pending; }
        Assert.True(waitObserved);
        Assert.True(first!.Evidence!.IsCurrent);
        Assert.Equal(f.Unit.Reference, first.Evidence.Base.Reference);
        Assert.Equal("UNKNOWN", (await catalogue.ReadCapacityMetricAsync(f.Warehouse, new("unit-count", policy.Reference))).State);
        var history = await catalogue.ReadCapacityMetricAsync(f.Warehouse, new("unit-count", policy.Reference, false));
        Assert.False(history.Evidence!.IsCurrent);
        Assert.Equal(f.Unit.OriginalJson, history.Evidence.Base.OriginalJson);
        var receipt = await pending!;
        Assert.Equal(receipt, await writer.CommandAsync(revise));
        Assert.Equal(receipt, (await writer.OperationAsync(revise.OperationId, false)).Result);
        Assert.Equal("2", (await writer.ReadAsync(f.Unit.Reference.Id, null)).Reference.Version);
    }

    [CurrentPermissionPostgresFact]
    public async Task Operational_catalogue_final_authority_recheck_denies_result_after_mid_read_revocation()
    {
        var hook = new StartWriteHook();
        await using var h = await Seed(hook);
        var f = await RuleReferencesSetup(h);
        var policy = await RuleReferences(h).SaveAsync(f.Warehouse, f.Command);
        var catalogue = new OperationalCatalogueEvidenceAdapter(h.Db, RuleReferenceAuthority(h), "MAIN_NATIVE_V1");
        var revokedInRead = false;
        hook.Before = async command =>
        {
            if (!command.CommandText.StartsWith("LOCK TABLE cp6_uom_profile_v1.heads", StringComparison.Ordinal)) return;
            hook.Before = null;
            await using var revoke = new NpgsqlCommand("""
                DELETE FROM public."Sys_RoleAction" WHERE "TenantId"=@tenant AND "RoleId"=72 AND "ActionCode"='query'
                    AND "MenuId" IN (SELECT "MenuId" FROM public."Sys_Menus" WHERE "MenuKey"='erp-uom-profile')
                """, (NpgsqlConnection)command.Connection!, (NpgsqlTransaction)command.Transaction!);
            revoke.Parameters.AddWithValue("tenant", h.Tenant);
            revokedInRead = await revoke.ExecuteNonQueryAsync() == 1;
        };
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            catalogue.ReadConversionAsync(new(f.Unit.Reference, f.Unit.Reference, "1", false)));
        Assert.True(revokedInRead);
        Assert.Equal(403, error.Status);
        // 夹具中的撤权与被拒读取同事务，回滚后原取证仍可读。
        Assert.Equal("KNOWN", (await catalogue.ReadCapacityMetricAsync(f.Warehouse, new("unit-count", policy.Reference))).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Operational_catalogue_unready_original_store_and_nonrepresentable_quantity_are_fenced()
    {
        await using var h = await Seed();
        var f = await RuleReferencesSetup(h, BaseUnit("EA", "COUNT", 0));
        var policy = await RuleReferences(h).SaveAsync(f.Warehouse, f.Command);
        var units = Units(h);
        var fraction = await MakeUnit(units, BaseUnit("⅓", "COUNT", 0) with
        { Mode = "RATIO", BaseRef = f.Unit.Reference, Denominator = "3" });
        var catalogue = new OperationalCatalogueEvidenceAdapter(h.Db, RuleReferenceAuthority(h), "MAIN_NATIVE_V1");
        var conversion = new OperationalUnitConversionQueryV1(fraction.Reference, f.Unit.Reference, "1");
        var failure = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => catalogue.ReadConversionAsync(conversion));
        Assert.Equal(422, failure.Status);
        Assert.Equal("UOM_CONVERSION_NOT_EXACT", failure.Message);
        Assert.Equal("1", (await catalogue.ReadConversionAsync(conversion with { Quantity = "3" })).Evidence!.ConvertedQuantity);

        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_location_rule_refs_v1.versions DISABLE TRIGGER rule_refs_versions_immutable");
        try
        {
            var unavailable = await catalogue.ReadCapacityMetricAsync(f.Warehouse, new("unit-count", policy.Reference, false));
            Assert.Equal("UNKNOWN", unavailable.State);
            Assert.Null(unavailable.Evidence);
        }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_location_rule_refs_v1.versions ENABLE TRIGGER rule_refs_versions_immutable");
        }
        Assert.Equal("KNOWN", (await catalogue.ReadCapacityMetricAsync(f.Warehouse, new("unit-count", policy.Reference))).State);
    }
}
