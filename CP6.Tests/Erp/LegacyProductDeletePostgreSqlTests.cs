using System.Net;
using System.Text.Json;
using CP6.Entity.DTOs.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly JsonSerializerOptions LegacyDeleteJson = new(JsonSerializerDefaults.Web);
    private static readonly string[] LegacyDeleteChildren = ["processes", "materials", "lotPrices", "coProducts"];

    [CurrentPermissionPostgresFact]
    public async Task Legacy_product_delete_commits_related_rows_and_preserves_original_http_reads()
    {
        await using var h = await Seed();
        await GrantLegacyProductDeleteAsync(h);
        await GrantOrderReads(h);
        await using var api = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(api, h);
        var (id, product) = await CreateLegacyProductForDeleteAsync(h, api, cookies);
        var code = product.ProductCd!;
        var linked = await h.Db.OrderDetails.Where(row => row.WebOrderNo == h.Number && row.WebOrderDetailNo == 1)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProductCd, code));
        Assert.Equal(1, linked);

        var orderPath = $"api/orders/{h.Number}";
        var identityPath = $"api/products/identity-owner-v1/{id:D}";
        var beforeOrder = await ReadLegacyProductDeleteHttpAsync(api, cookies, "order-before", HttpMethod.Get, orderPath);
        var beforeHistory = await ReadLegacyProductDeleteHttpAsync(api, cookies, "history-before", HttpMethod.Get, identityPath + "?version=1");
        var before = await ReadLegacyProductDeleteSnapshotAsync(h, id, code, "before-delete");
        AssertLegacyDeleteRows(before, false);
        Assert.Equal(1, LegacyDeleteRevision(before));
        Assert.NotNull(product.RowVersion);

        var deleted = await ReadLegacyProductDeleteHttpAsync(api, cookies, "delete", HttpMethod.Delete, $"api/products/{code}",
            new { rowVersion = product.RowVersion });
        Assert.Equal(HttpStatusCode.OK, deleted.Status);
        Assert.Equal(0, deleted.Body.GetProperty("code").GetInt32());

        var after = await ReadLegacyProductDeleteSnapshotAsync(h, id, code, "after-delete");
        AssertLegacyDeleteRows(after, true);
        Assert.Equal(id, LegacyDeleteRowId(after.GetProperty("master")));
        foreach (var child in LegacyDeleteChildren)
            Assert.Equal(LegacyDeleteRowId(before.GetProperty(child)[0]), LegacyDeleteRowId(after.GetProperty(child)[0]));

        Assert.Equal(2, LegacyDeleteRevision(after));
        Assert.Equal(2, after.GetProperty("versions").GetArrayLength());
        Assert.Equal(before.GetProperty("versions")[0].GetRawText(), after.GetProperty("versions")[0].GetRawText());

        var current = (await ReadLegacyProductDeleteHttpAsync(api, cookies, "identity-after", HttpMethod.Get, identityPath)).Data();
        Assert.Equal(id, current.GetProperty("productId").GetGuid());
        Assert.Equal("2", current.GetProperty("version").GetString());
        Assert.True(current.GetProperty("isCurrent").GetBoolean());
        Assert.True(current.GetProperty("source").GetProperty("isDeleted").GetBoolean());

        var afterOrder = await ReadLegacyProductDeleteHttpAsync(api, cookies, "order-after", HttpMethod.Get, orderPath);
        foreach (var response in new[] { beforeOrder, afterOrder })
        {
            var order = response.Data().Deserialize<OrderDto>(LegacyDeleteJson)!;
            Assert.Equal(h.Number, order.WebOrderNo);
            Assert.Equal(code, Assert.Single(order.Details, row => row.WebOrderDetailNo == 1).ProductCd);
        }
        Assert.Equal(before.GetProperty("order").GetRawText(), after.GetProperty("order").GetRawText());
        Assert.Equal(before.GetProperty("orderLine").GetRawText(), after.GetProperty("orderLine").GetRawText());

        var original = beforeHistory.Data();
        var history = (await ReadLegacyProductDeleteHttpAsync(api, cookies, "history-after", HttpMethod.Get, identityPath + "?version=1")).Data();
        foreach (var field in new[] { "tenantId", "productId", "version", "digest", "originalJson" })
            Assert.Equal(original.GetProperty(field).GetRawText(), history.GetProperty(field).GetRawText());
        Assert.Equal(id, history.GetProperty("productId").GetGuid());
        Assert.Equal("1", history.GetProperty("version").GetString());
        Assert.False(history.GetProperty("isCurrent").GetBoolean());
        Assert.False(history.GetProperty("source").GetProperty("isDeleted").GetBoolean());
        var stored = before.GetProperty("versions")[0].GetProperty("row");
        Assert.Equal(stored.GetProperty("source_digest").GetString(), history.GetProperty("digest").GetString());
        Assert.Equal(stored.GetProperty("source_json").GetString(), history.GetProperty("originalJson").GetString());

        var stamp = after.GetProperty("master").GetProperty("xmin").GetString();
        Assert.NotEqual(before.GetProperty("master").GetProperty("xmin").GetString(), stamp);
        var saved = LegacyDeleteChildren.Select(child => after.GetProperty(child)[0])
            .Concat([after.GetProperty("head"), after.GetProperty("versions")[1]]);
        Assert.True(saved.All(row => row.GetProperty("xmin").GetString() == stamp),
            "产品主记录、关联记录和新版本应在同一事务提交。");
    }

    [CurrentPermissionPostgresFact]
    public async Task Legacy_product_delete_conflict_preserves_the_current_graph()
    {
        await using var h = await Seed();
        await GrantLegacyProductDeleteAsync(h);
        await using var api = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(api, h);
        var (id, product) = await CreateLegacyProductForDeleteAsync(h, api, cookies);
        var code = product.ProductCd!;
        var originalVersion = product.RowVersion;
        Assert.NotNull(originalVersion);

        product.BasicInfo.QtyUnit = "KG";
        var updated = (await ReadLegacyProductDeleteHttpAsync(api, cookies, "update", HttpMethod.Put, $"api/products/{code}", product))
            .Data().Deserialize<ProductDto>(LegacyDeleteJson)!;
        Assert.NotNull(updated.RowVersion);
        Assert.False(originalVersion!.SequenceEqual(updated.RowVersion!));
        var before = await ReadLegacyProductDeleteSnapshotAsync(h, id, code, "before-conflict");
        AssertLegacyDeleteRows(before, false);
        Assert.Equal(2, LegacyDeleteRevision(before));
        Assert.Equal(2, before.GetProperty("versions").GetArrayLength());

        var conflict = await ReadLegacyProductDeleteHttpAsync(api, cookies, "conflict", HttpMethod.Delete, $"api/products/{code}",
            new { rowVersion = originalVersion });
        var after = await ReadLegacyProductDeleteSnapshotAsync(h, id, code, "after-conflict");
        Assert.Equal(HttpStatusCode.Conflict, conflict.Status);
        Assert.Equal(409, conflict.Body.GetProperty("code").GetInt32());
        Assert.Equal("MSG-W10002", conflict.Body.GetProperty("msgId").GetString());
        Assert.Equal(before.GetRawText(), after.GetRawText());
    }

    private static async Task GrantLegacyProductDeleteAsync(Harness h)
    {
        await GrantProductIdentity(h);
        var menu = await h.Db.Sys_Menus.SingleAsync(row => row.MenuKey == "erp-product");
        foreach (var action in new[] { "add", "edit", "del" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(row => row.MenuId == menu.MenuId && row.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
        }
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private async Task<(Guid Id, ProductDto Product)> CreateLegacyProductForDeleteAsync(
        Harness h, AuthenticationApiProcess api, Dictionary<string, string> cookies)
    {
        var input = new ProductDto
        {
            BasicInfo = new() { CustomerCd = "C", SetProductName = "合成验收产品", QtyUnit = "BOX" },
            Processes = [new() { TaskCd = "CUT", ProcessCd = "CUT" }],
            Materials = [new() { ProcessCd = "CUT", MaterialCd = "SYN-MATERIAL" }],
            LotPrices = [new() { DetailNo = 1, LotQty = 10, CurrentUnitPrice = 2.125m }],
            CoProducts = [new() { ProcessCd = "CUT", RowNo = 1, CoProductName = "合成副件", QtyRatio = 0.5m }]
        };
        output.WriteLine("legacy-delete-evidence " + JsonSerializer.Serialize(new { label = "synthetic-product", input }));
        var created = await ReadLegacyProductDeleteHttpAsync(api, cookies, "create", HttpMethod.Post, "api/products", input);
        var product = created.Data().Deserialize<ProductDto>(LegacyDeleteJson)!;
        var id = await h.Db.ProductMasters.Where(row => row.ProductCd == product.ProductCd).Select(row => row.Id).SingleAsync();
        return (id, product);
    }

    private async Task<LegacyDeleteHttp> ReadLegacyProductDeleteHttpAsync(AuthenticationApiProcess api,
        Dictionary<string, string> cookies, string label, HttpMethod method, string path, object? input = null)
    {
        using var response = await api.Send(OperationsRequest(cookies, method, path, input));
        var url = response.RequestMessage?.RequestUri;
        Assert.True(url is { IsLoopback: true, Scheme: "http" });
        var body = (int)response.StatusCode >= 500 ? await OperationsFailure(response) : await response.Content.ReadAsStringAsync();
        output.WriteLine("legacy-delete-evidence " + JsonSerializer.Serialize(new
        {
            label, method = method.Method, path, server = url!.GetLeftPart(UriPartial.Authority), status = (int)response.StatusCode, body
        }));
        return new(response.StatusCode, body);
    }

    private sealed record LegacyDeleteHttp(HttpStatusCode Status, string Raw)
    {
        public JsonElement Body
        {
            get
            {
                using var json = JsonDocument.Parse(Raw);
                return json.RootElement.Clone();
            }
        }

        public JsonElement Data()
        {
            Assert.Equal(HttpStatusCode.OK, Status);
            var body = Body;
            Assert.Equal(0, body.GetProperty("code").GetInt32());
            return body.GetProperty("data");
        }
    }

    private async Task<JsonElement> ReadLegacyProductDeleteSnapshotAsync(Harness h, Guid id, string cd, string label)
    {
        var locator = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString());
        Assert.Equal("127.0.0.1", locator.Host);
        Assert.Equal(CurrentPermissionPostgresFixture.Port, locator.Port);
        Assert.Equal(TenantManagementPostgresFixture.Database, locator.Database);
        await using var observer = new NpgsqlConnection(locator.ConnectionString);
        await observer.OpenAsync();
        await using var read = new NpgsqlCommand(LegacyProductDeleteSnapshotSql, observer);
        read.Parameters.AddWithValue("tenant", h.Tenant);
        read.Parameters.AddWithValue("id", id);
        read.Parameters.AddWithValue("code", cd);
        read.Parameters.AddWithValue("order", h.Number);
        var body = (string)(await read.ExecuteScalarAsync())!;
        output.WriteLine("legacy-delete-evidence " + JsonSerializer.Serialize(new { label, body }));
        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }

    private static int LegacyDeleteRevision(JsonElement data) => data.GetProperty("head").GetProperty("row").GetProperty("revision").GetInt32();
    private static Guid LegacyDeleteRowId(JsonElement data) => data.GetProperty("row").GetProperty("Id").GetGuid();

    private static void AssertLegacyDeleteRows(JsonElement data, bool deleted)
    {
        Assert.Equal(deleted, data.GetProperty("master").GetProperty("row").GetProperty("IsDeleted").GetBoolean());
        foreach (var child in LegacyDeleteChildren)
        {
            var row = Assert.Single(data.GetProperty(child).EnumerateArray());
            Assert.Equal(deleted, row.GetProperty("row").GetProperty("IsDeleted").GetBoolean());
        }
    }

    private const string LegacyProductDeleteSnapshotSql = """
        SELECT jsonb_build_object(
          'master', (SELECT jsonb_build_object('xmin', r.xmin::text, 'row', to_jsonb(r))
            FROM public."T_ProductMaster" r WHERE r."TenantId"=@tenant AND r."Id"=@id),
          'processes', (SELECT jsonb_agg(jsonb_build_object('xmin', r.xmin::text, 'row', to_jsonb(r)) ORDER BY r."Id")
            FROM public."T_ProductProcess" r WHERE r."TenantId"=@tenant AND r."ProductCd"=@code),
          'materials', (SELECT jsonb_agg(jsonb_build_object('xmin', r.xmin::text, 'row', to_jsonb(r)) ORDER BY r."Id")
            FROM public."T_ProductMaterial" r WHERE r."TenantId"=@tenant AND r."ProductCd"=@code),
          'lotPrices', (SELECT jsonb_agg(jsonb_build_object('xmin', r.xmin::text, 'row', to_jsonb(r)) ORDER BY r."Id")
            FROM public."T_ProductLotPrice" r WHERE r."TenantId"=@tenant AND r."ProductCd"=@code),
          'coProducts', (SELECT jsonb_agg(jsonb_build_object('xmin', r.xmin::text, 'row', to_jsonb(r)) ORDER BY r."Id")
            FROM public."T_ProductCoProduct" r WHERE r."TenantId"=@tenant AND r."ProductCd"=@code),
          'head', (SELECT jsonb_build_object('xmin', r.xmin::text, 'row', to_jsonb(r))
            FROM cp6_product_identity_v1.heads r WHERE r.tenant_id=@tenant AND r.product_id=@id),
          'versions', (SELECT jsonb_agg(jsonb_build_object('xmin', r.xmin::text, 'row', to_jsonb(r)) ORDER BY revision)
            FROM cp6_product_identity_v1.versions r WHERE r.tenant_id=@tenant AND r.product_id=@id),
          'order', (SELECT to_jsonb(r) FROM public."T_Order" r WHERE r."TenantId"=@tenant AND r."WebOrderNo"=@order),
          'orderLine', (SELECT to_jsonb(r) FROM public."T_OrderDetail" r
            WHERE r."TenantId"=@tenant AND r."WebOrderNo"=@order AND r."WebOrderDetailNo"=1)
        )::text
        """;
}
