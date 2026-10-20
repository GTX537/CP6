using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static HttpRequestMessage BoundRequest(Dictionary<string, string> cookies, string path,
        object? body = null, bool csrf = true)
    {
        var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, path);
        if (body != null) request.Content = JsonContent.Create(body);
        request.Headers.Add("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value)));
        if (csrf) request.Headers.Add("X-CSRF-Token", cookies["cp6_csrf"]);
        return request;
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_program_binding_requires_csrf_cas_action_and_current_department_scope()
    {
        await using var h = await Seed(); await GrantQuery(h); var admin = await MappingAdmin(h);
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.OrderDepartment, "SALES"));
        await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeptId, admin.Child));
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 2));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        const string path = "api/iam/departments/order-mappings";
        const string listPath = "api/orders/price-correction/list?BaseCd=BASE";
        using (var anonymous = await host.Send(new HttpRequestMessage(HttpMethod.Get, path)))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var cookies = await Login(host, h);
        using var editorResponse = await host.Send(BoundRequest(cookies, path));
        Assert.True(editorResponse.IsSuccessStatusCode, await editorResponse.Content.ReadAsStringAsync());
        Assert.True(editorResponse.Headers.CacheControl?.NoStore);
        var editor = await editorResponse.Content.ReadFromJsonAsync<IamOrderDepartmentEditorV1>();
        Assert.Empty(editor!.Mappings);
        var command = new IamOrderDepartmentCommandV1 { OperationId = Guid.NewGuid(), Code = "SALES", DepartmentId = admin.Child,
            ExpectedVersion = "absent", ExpectedReferencesVersion = editor.ReferencesVersion, Reason = "Owned HTTP fixture" };
        using (var unavailable = await host.Send(BoundRequest(cookies, listPath)))
        {
            Assert.True(unavailable.IsSuccessStatusCode, await unavailable.Content.ReadAsStringAsync());
            var json = JsonDocument.Parse(await unavailable.Content.ReadAsStringAsync());
            Assert.Equal(0, json.RootElement.GetProperty("data").GetProperty("total").GetInt32());
        }
        using (var noCsrf = await host.Send(BoundRequest(cookies, path, command, false)))
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using var savedResponse = await host.Send(BoundRequest(cookies, path, command));
        Assert.True(savedResponse.IsSuccessStatusCode, await savedResponse.Content.ReadAsStringAsync());
        var saved = await savedResponse.Content.ReadFromJsonAsync<IamOperationV1>();
        Assert.Equal("COMMITTED", saved!.State);
        using (var repeat = await host.Send(BoundRequest(cookies, path, command)))
            Assert.Equal(saved, await repeat.Content.ReadFromJsonAsync<IamOperationV1>());
        using (var stale = await host.Send(BoundRequest(cookies, path, command with { OperationId = Guid.NewGuid(), Code = "NEW" })))
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using (var allowed = await host.Send(BoundRequest(cookies, listPath)))
        {
            Assert.True(allowed.IsSuccessStatusCode, await allowed.Content.ReadAsStringAsync());
            Assert.True(allowed.Headers.CacheControl?.NoStore);
            var json = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());
            Assert.Equal(2, json.RootElement.GetProperty("data").GetProperty("total").GetInt32());
        }
        using (var write = await host.Send(Request(h, cookies)))
            Assert.True(write.IsSuccessStatusCode, await write.Content.ReadAsStringAsync());
        var menu = await h.Db.Sys_Menus.SingleAsync(m => m.MenuKey == "pub-dept");
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == menu.MenuId && a.ActionCode == "edit").ExecuteDeleteAsync();
        using (var revoked = await host.Send(BoundRequest(cookies, path, command)))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode); // A receipt is not future authorization.
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_order_owner_v1.department_mappings SET enabled=false WHERE id={saved.EntityId}");
        using (var hidden = await host.Send(BoundRequest(cookies, listPath)))
        {
            Assert.True(hidden.IsSuccessStatusCode, await hidden.Content.ReadAsStringAsync());
            var json = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync());
            Assert.Equal(0, json.RootElement.GetProperty("data").GetProperty("total").GetInt32());
        }
        using var denied = await host.Send(Request(h, cookies));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
        Assert.Contains("OWNER_SCOPE_MAPPING_UNAVAILABLE", await denied.Content.ReadAsStringAsync());
        Assert.Equal(1, await Intents(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_program_masks_price_and_amount_and_rejects_filter_calculation_and_revoked_query()
    {
        await using var h = await Seed(); await GrantQuery(h);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "UnitPrice", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        const string path = "api/orders/price-correction/list?BaseCd=BASE";
        using (var response = await host.Send(BoundRequest(cookies, path)))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var rows = json.RootElement.GetProperty("data").GetProperty("rows").EnumerateArray().ToArray();
            Assert.Equal(2, rows.Length);
            foreach (var row in rows)
            {
                foreach (var field in new[] { "individualUnitPriceBefore", "individualUnitPriceAfter", "setUnitPriceBefore",
                    "setUnitPriceAfter", "amount", "specialPriceFlg", "provisionalPriceFlg", "priceChangeReason" })
                    Assert.True(!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null, field);
                Assert.False(row.GetProperty("priceReadable").GetBoolean());
                Assert.False(row.GetProperty("amountReadable").GetBoolean());
                Assert.False(row.GetProperty("priceWritable").GetBoolean());
            }
        }
        foreach (var target in new[] { path + "&AmountFrom=6", path + "&OnlyProvisional=true",
            $"api/orders/price-correction/{h.Number}/1/amount?indPrice=3" })
        {
            using var denied = await host.Send(BoundRequest(cookies, target));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Contains("FIELD_READ_FORBIDDEN", await denied.Content.ReadAsStringAsync());
        }
        await h.Db.Sys_RoleActions.Where(a => a.ActionCode == "query").ExecuteDeleteAsync();
        using var revoked = await host.Send(BoundRequest(cookies, path));
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }
}
