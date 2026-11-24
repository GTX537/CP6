using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly JsonSerializerOptions ProductWire = new(JsonSerializerDefaults.Web);

    [CurrentPermissionPostgresFact]
    public async Task Wp0433_legacy_product_http_keeps_order_identity_and_rolls_back_stale_delete_with_its_children()
    {
        await using var h = await Seed();
        await GrantMappingRead(h);
        foreach (var action in new[] { "add", "edit", "del" })
            await EngineeringAction(h, "erp-product", action);

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);

        Task<HttpResponseMessage> Send(HttpMethod method, string path, object? input = null) =>
            host.Send(OperationsRequest(cookies, method, path, input));

        async Task<ProductDto> ReadProduct(HttpResponseMessage response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("data").Deserialize<ProductDto>(ProductWire)!;
        }

        var input = new ProductDto
        {
            BasicInfo = new() { CustomerCd = "C", SetProductName = "同名经营料", QtyUnit = "BOX" },
            Processes = [new() { TaskCd = "CUT", ProcessCd = "CUT" }],
            Materials = [new() { ProcessCd = "CUT", MaterialCd = "RAW-1" }],
            LotPrices = [new() { DetailNo = 1, LotQty = 1, CurrentUnitPrice = 2.125m }],
            CoProducts = [new() { ProcessCd = "CUT", RowNo = 1, CoProductName = "副件", QtyRatio = 0.5m }]
        };
        using var created = await Send(HttpMethod.Post, "api/products", input);
        var product = await ReadProduct(created);
        var code = product.ProductCd!;
        var id = await h.Db.ProductMasters.Where(row => row.ProductCd == code).Select(row => row.Id).SingleAsync();
        var original = await ProductIdentity(h).ReadAsync(id);

        using var copied = await Send(HttpMethod.Post, $"api/products/{code}/copy");
        var copy = await ReadProduct(copied);
        var search = await EngineeringMappings(h).BusinessChoicesAsync("同名经营料");
        Assert.Equal(2, search.Length);
        var originalChoice = Assert.Single(search, row => row.Reference.Id == id);
        var copyChoice = Assert.Single(search, row => row.Code == copy.ProductCd);
        Assert.Equal(original.Reference, originalChoice.Reference);
        Assert.Equal(originalChoice.Name, copyChoice.Name);
        Assert.NotEqual(originalChoice.MaterialKey, copyChoice.MaterialKey);
        Assert.NotEqual(id, copyChoice.Reference.Id);

        var orderLine = h.Db.OrderDetails.Where(d => d.WebOrderNo == h.Number && d.WebOrderDetailNo == 1);
        await orderLine.ExecuteUpdateAsync(set => set.SetProperty(d => d.ProductCd, code));

        var stale = product.RowVersion;
        Assert.NotNull(stale);
        product.BasicInfo.QtyUnit = "KG";
        using var edited = await Send(HttpMethod.Put, $"api/products/{code}", product);
        var next = await ReadProduct(edited);
        var version2 = await ProductIdentity(h).ReadAsync(id);
        Assert.Equal("2", version2.Version);
        Assert.Equal("KG", version2.DeclaredUnitCode);

        using var rejected = await Send(HttpMethod.Delete, $"api/products/{code}", new { rowVersion = stale });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(version2.Reference, (await ProductIdentity(h).ReadAsync(id)).Reference);
        Assert.False(await h.Db.ProductMaterials.Where(row => row.ProductCd == code).Select(row => row.IsDeleted).SingleAsync());
        Assert.False(await h.Db.ProductProcesses.Where(row => row.ProductCd == code).Select(row => row.IsDeleted).SingleAsync());
        Assert.False(await h.Db.ProductLotPrices.Where(row => row.ProductCd == code).Select(row => row.IsDeleted).SingleAsync());
        Assert.False(await h.Db.ProductCoProducts.Where(row => row.ProductCd == code).Select(row => row.IsDeleted).SingleAsync());

        using var deleted = await Send(HttpMethod.Delete, $"api/products/{code}", new { rowVersion = next.RowVersion });
        Assert.True(deleted.StatusCode == HttpStatusCode.OK, await OperationsFailure(deleted));
        var version3 = await ProductIdentity(h).ReadAsync(id);
        Assert.Equal("3", version3.Version);
        Assert.Equal("INACTIVE", version3.EffectiveState);
        Assert.True(await h.Db.ProductMaterials.Where(row => row.ProductCd == code).Select(row => row.IsDeleted).SingleAsync());
        Assert.True(await h.Db.ProductProcesses.Where(row => row.ProductCd == code).Select(row => row.IsDeleted).SingleAsync());
        Assert.True(await h.Db.ProductLotPrices.Where(row => row.ProductCd == code).Select(row => row.IsDeleted).SingleAsync());
        Assert.True(await h.Db.ProductCoProducts.Where(row => row.ProductCd == code).Select(row => row.IsDeleted).SingleAsync());
        Assert.Equal(code, await orderLine.Select(row => row.ProductCd).SingleAsync());
        Assert.Equal(original.OriginalJson, (await ProductIdentity(h).ReadAsync(id, original.Version)).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0433_exact_http_scope_reads_original_owners_and_keeps_unproven_product_scope_visible()
    {
        await using var h = await Seed();
        await GrantProductUnitEdit(h);
        await GrantUomProfile(h);
        await GrantCurrencyPolicy(h);
        await GrantEngineering(h);
        await EngineeringAction(h, "space-site", "query");
        await EngineeringAction(h, "erp-product", "confirm-engineering");

        var product = await SeedIdentityProduct(h, "SCOPE");
        await h.Db.ProductMasters.Where(row => row.Id == product).ExecuteUpdateAsync(set => set.SetProperty(row => row.QtyUnit, "BOX"));
        var native = await ProductIdentity(h).ReadAsync(product);
        var unit = await MakeUnit(Units(h), BaseUnit("BOX", "COUNT"));
        var bound = await ProductUnits(h).BindAsync(new CommercialProductUnitCommandV1
        {
            OperationId = Guid.NewGuid(), ProductId = product, ExpectedBindingVersion = "absent",
            ProductVersion = native.Version, ProfileRef = unit.Reference
        });
        var currencies = Currencies(h);
        var currency = await MakeCurrency(currencies, CurrencyInput("USD"));
        var engineering = await MakeEngineering(h);
        var site = await PhysicalSite(h, "SCOPE-SITE");
        var mappings = EngineeringMappings(h);
        var siteRef = Assert.Single(await mappings.SiteChoicesAsync("SCOPE-SITE"), row => row.Reference.Id == site).Reference;
        var draft = await MakeMapping(h, new EngineeringMappingCandidateV1
        {
            EngineeringRef = engineering.Reference, BusinessRef = native.Reference, SiteRef = siteRef,
            Purpose = "部材身份在物理站点的配对", Evidence = "工程 Owner 与经营 Owner 核对原始身份", ConflictsResolved = true
        });
        Assert.Null(draft.ConfirmedIdentitySite);
        Assert.False(draft.CanAdoptIdentitySite);
        var confirmed = await mappings.CommandAsync(MappingAction(draft, "CONFIRM"));
        var mapping = (await mappings.ReadAsync(confirmed.ObjectId, confirmed.Version)).Reference;
        var input = new CommercialProductQualificationQueryV1(native.Reference, bound.Version, currency.Reference, mapping);

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        Task<HttpResponseMessage> Read(CommercialProductQualificationQueryV1 select) =>
            host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/products/identity-owner-v1/qualification", select));
        async Task<JsonElement> Result(HttpResponseMessage response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("data").Clone();
        }

        using var current = await Read(input);
        var observed = await Result(current);
        Assert.True(observed.GetProperty("canAdoptIdentitySite").GetBoolean());
        Assert.Equal("APPROVED", observed.GetProperty("product").GetProperty("effectiveState").GetString());
        Assert.Equal(native.Version, observed.GetProperty("product").GetProperty("version").GetString());
        Assert.Equal(unit.Reference, observed.GetProperty("unit").GetProperty("profileRef").Deserialize<UomProfileRefV1>(ProductWire));
        Assert.True(observed.GetProperty("unit").GetProperty("currentApplicable").GetBoolean());
        Assert.Equal(currency.Reference, observed.GetProperty("selectedCurrencyPolicy").GetProperty("reference").Deserialize<CurrencyPolicyRefV1>(ProductWire));
        Assert.Equal("USD", observed.GetProperty("selectedCurrencyPolicy").GetProperty("policy").GetProperty("code").GetString());
        var map = observed.GetProperty("mapping");
        Assert.Equal("REQUIRED_FENCED", map.GetProperty("legalEntityApplicability").GetString());
        Assert.Equal("REQUIRED_FENCED", map.GetProperty("manufacturingApplicability").GetString());
        Assert.Equal(mapping, map.GetProperty("confirmedIdentitySite").GetProperty("mappingRef").Deserialize<EngineeringMappingRefV1>(ProductWire));
        Assert.Equal(siteRef, map.GetProperty("confirmedIdentitySite").GetProperty("siteRef").Deserialize<EngineeringSourceRefV1>(ProductWire));
        Assert.Equal(native.Reference, map.GetProperty("confirmedIdentitySite").GetProperty("productRef").Deserialize<EngineeringSourceRefV1>(ProductWire));
        Assert.Equal("REQUIRED_FENCED", observed.GetProperty("formalAdoption").GetString());
        Assert.Equal(new[] { "PRODUCT_CURRENCY_BINDING", "LEGAL_ENTITY_APPLICABILITY", "MANUFACTURING_APPLICABILITY" },
            observed.GetProperty("missing").Deserialize<string[]>());

        var noUnit = await SeedIdentityProduct(h, "NO-UOM");
        var other = await ProductIdentity(h).ReadAsync(noUnit);
        using var missingResponse = await Read(new(other.Reference));
        var missing = await Result(missingResponse);
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("unit").ValueKind);
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("product").GetProperty("declaredUnitCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("selectedCurrencyPolicy").ValueKind);
        Assert.Contains("VERSIONED_UOM_PROFILE", missing.GetProperty("missing").Deserialize<string[]>()!);
        using var mismatched = await Read(new(other.Reference, MappingRef: mapping));
        Assert.Equal(HttpStatusCode.PreconditionFailed, mismatched.StatusCode);

        await currencies.CommandAsync(ReviseCurrency(currency, currency.Policy with { Enabled = false }));
        await mappings.CommandAsync(MappingAction(await mappings.ReadAsync(mapping.Id, null), "DISABLE"));
        await h.Db.ProductMasters.Where(row => row.Id == product).ExecuteUpdateAsync(set =>
            set.SetProperty(row => row.Status, 9).SetProperty(row => row.McTransferFlg, true));

        using var historyResponse = await Read(input);
        var history = await Result(historyResponse);
        Assert.False(history.GetProperty("canAdoptIdentitySite").GetBoolean());
        Assert.False(history.GetProperty("product").GetProperty("isCurrent").GetBoolean());
        Assert.Equal(native.OriginalJson, history.GetProperty("product").GetProperty("originalJson").GetString());
        Assert.False(history.GetProperty("unit").GetProperty("currentApplicable").GetBoolean());
        Assert.False(history.GetProperty("selectedCurrencyPolicy").GetProperty("isCurrent").GetBoolean());
        Assert.Equal(currency.OriginalJson, history.GetProperty("selectedCurrencyPolicy").GetProperty("originalJson").GetString());
        Assert.Equal(map.GetProperty("originalJson").GetString(), history.GetProperty("mapping").GetProperty("originalJson").GetString());
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ConsumeMapping(h, mapping))).Status);
        var latest = await ProductIdentity(h).ReadAsync(product);
        Assert.Equal("TRANSFERRED", latest.EffectiveState);
        Assert.Equal(native.ProductId, latest.ProductId);
        Assert.NotEqual(native.Version, latest.Version);
        using var disabledResponse = await Read(new(latest.Reference, CurrencyPolicyRef: (await currencies.ReadAsync(currency.Reference.Id, null)).Reference));
        var disabled = await Result(disabledResponse);
        Assert.False(disabled.GetProperty("selectedCurrencyPolicy").GetProperty("policy").GetProperty("enabled").GetBoolean());
    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0433_serial_tracking_writer_advances_the_product_frontier_once_and_recovers_original_result()
    {
        await using var h = await Seed();
        await GrantProductIdentity(h);
        await GrantMobile(h);
        await EngineeringAction(h, "wms-mobile", "serial-manage");
        var id = await SeedIdentityProduct(h, "SERIAL");
        var product = await ProductIdentity(h).ReadAsync(id);

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var input = new EnableSerialTrackingRequest
        {
            OperationId = Guid.NewGuid(), ProductCd = product.ProductCode, TrackingMode = ProductTrackingMode.Serial
        };
        Task<HttpResponseMessage> Enable() =>
            host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/v2/wms/serials/enable-tracking", input));

        using var first = await Enable();
        Assert.True(first.StatusCode == HttpStatusCode.OK, await OperationsFailure(first));
        var version2 = await ProductIdentity(h).ReadAsync(id);
        Assert.Equal("2", version2.Version);
        Assert.Equal(ProductTrackingMode.Serial, version2.Source.GetProperty("trackingMode").GetInt32());
        using var replay = await Enable();
        Assert.True(replay.StatusCode == HttpStatusCode.OK, await OperationsFailure(replay));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(version2.Reference, (await ProductIdentity(h).ReadAsync(id)).Reference);
        Assert.Equal(product.OriginalJson, (await ProductIdentity(h).ReadAsync(id, product.Version)).OriginalJson);

        // 旧登记的序列与一个原库位数量一致，第一次盘点把追踪设置锁定。
        h.Db.Warehouses.Add(new() { WarehouseCd = "W1", WarehouseName = "合成库位" });
        h.Db.Locations.Add(new() { WarehouseCd = "W1", LocationCd = "P33-SRC", AreaCd = "SYN" });
        h.Db.WmsFeatureFlags.Add(new() { WarehouseCd = "W1", SerialLpnEnabled = true });
        var stock = CustodyStock("P33-SRC");
        stock.ProductCd = product.ProductCode;
        stock.WarehouseCd = "W1";
        stock.PhysicalQty = stock.AvailableQty = 2m;
        h.Db.Stocks.Add(stock);
        h.Db.StockSerials.Add(new()
        {
            ProductCd = product.ProductCode, SerialNo = "P33-0001", WarehouseCd = "W1",
            LocationCd = "P33-SRC", LotNo = stock.LotNo
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var count = new SerialLifecycleRequest
        {
            OperationId = Guid.NewGuid(), TxnType = "COUNT", ProductCd = product.ProductCode,
            WarehouseCd = "W1", FromLocationCd = "P33-SRC", LotNo = stock.LotNo, SerialNos = ["P33-0001"]
        };
        Task<HttpResponseMessage> Count() =>
            host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/v2/wms/serials", count));
        using var mismatch = await Count();
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Equal(version2.Reference, (await ProductIdentity(h).ReadAsync(id)).Reference);
        Assert.Null(await h.Db.ProductMasters.Where(row => row.Id == id).Select(row => row.SerialTrackingLockedAt).SingleAsync());
        Assert.False(await h.Db.TaskCommandReceipts.AnyAsync(row => row.OperationId == count.OperationId));
        await h.Db.Stocks.Where(row => row.Id == stock.Id).ExecuteUpdateAsync(set =>
            set.SetProperty(row => row.PhysicalQty, 1m).SetProperty(row => row.AvailableQty, 1m));

        using var posted = await Count();
        Assert.True(posted.StatusCode == HttpStatusCode.OK, await OperationsFailure(posted));
        var version3 = await ProductIdentity(h).ReadAsync(id);
        Assert.Equal("3", version3.Version);
        Assert.Equal(version2.Digest, version3.Digest);
        Assert.NotNull(await h.Db.ProductMasters.Where(row => row.Id == id).Select(row => row.SerialTrackingLockedAt).SingleAsync());
        using var postedAgain = await Count();
        Assert.True(postedAgain.StatusCode == HttpStatusCode.OK, await OperationsFailure(postedAgain));
        Assert.Equal(await posted.Content.ReadAsStringAsync(), await postedAgain.Content.ReadAsStringAsync());
        Assert.Equal(version3.Reference, (await ProductIdentity(h).ReadAsync(id)).Reference);
    }
}
