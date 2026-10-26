using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task GrantCurrencyPolicy(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "erp-currency-policy");
        if (menu == null)
        {
            menu = new() { MenuId = 9149, MenuKey = "erp-currency-policy", MenuName = "Currency policies" };
            h.Db.Sys_Menus.Add(menu);
        }
        foreach (var action in new[] { "query", "add", "edit" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu.MenuId && a.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
        }
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "currency-policy", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task CurrencyPolicy_actual_HTTP_create_replay_read_and_revoke()
    {
        await using var h = await Seed(); await GrantCurrencyPolicy(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var context = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/currency-policies/owner-v1/creation-context"));
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        using var json = JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        var id = Guid.NewGuid(); var operation = Guid.NewGuid();
        var command = new { operationId = operation, action = "CREATE", policyId = id,
            expectedAuthorityVersion = json.RootElement.GetProperty("data").GetProperty("authorityVersion").GetString(),
            policy = new { code = "JPY", name = "円 policy", minorDigits = 0, priceDigits = 3, rounding = "HALF_EVEN", enabled = true } };
        async Task<HttpResponseMessage> Save()
        {
            var request = OperationsRequest(cookies, HttpMethod.Post, "api/currency-policies/owner-v1/commands");
            request.Content = new StringContent(JsonSerializer.Serialize(command), Encoding.UTF8, "application/json");
            return await host.Send(request);
        }
        using var first = await Save(); Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.True(first.Headers.CacheControl?.NoStore);
        using var replay = await Save(); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var current = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/currency-policies/owner-v1/{id:D}"));
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "erp-currency-policy").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == menu && a.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/currency-policies/owner-v1/operations/{operation:D}"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
