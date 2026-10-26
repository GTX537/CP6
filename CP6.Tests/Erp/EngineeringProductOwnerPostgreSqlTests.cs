using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Tests.Platform;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task GrantEngineering(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "plm-engineering-product");
        if (menu == null) { menu = new() { MenuId = 9150, MenuKey = "plm-engineering-product", MenuName = "Engineering products" }; h.Db.Sys_Menus.Add(menu); }
        foreach (var action in new[] { "query", "add", "edit", "confirm" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == menu.MenuId && a.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = action });
        }
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "plm-engineering-product", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringProduct_actual_HTTP_creation_replay_and_current_revocation()
    {
        await using var h = await Seed(); await GrantEngineering(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var context = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/engineering-products/owner-v1/creation-context"));
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        using var json = JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        var id = Guid.NewGuid(); var operation = Guid.NewGuid();
        var command = new { operationId = operation, action = "CREATE", productId = id,
            expectedAuthorityVersion = json.RootElement.GetProperty("data").GetProperty("authorityVersion").GetString(),
            product = new { code = "ENGINEERING-A", name = "Native product", category = "PART", definition = "Exact part specification A", identificationSource = "Controlled source A", enabled = true } };
        async Task<HttpResponseMessage> Save()
        {
            var request = OperationsRequest(cookies, HttpMethod.Post, "api/engineering-products/owner-v1/commands");
            request.Content = new StringContent(JsonSerializer.Serialize(command), Encoding.UTF8, "application/json"); return await host.Send(request);
        }
        using var first = await Save(); Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.True(first.Headers.CacheControl?.NoStore);
        using var replay = await Save(); Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "plm-engineering-product").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == menu && a.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/engineering-products/owner-v1/operations/{operation:D}"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringMapping_actual_HTTP_draft_and_dual_Owner_confirmation_use_original_runtime()
    {
        await using var h = await Seed(); await GrantMappingRead(h); await EngineeringAction(h, "erp-product", "confirm-engineering");
        var candidate = await MappingCandidate(h, await MakeEngineering(h)); var draft = MappingDraft(candidate);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        async Task<HttpResponseMessage> Save(EngineeringMappingCommandV1 command)
        {
            var request = OperationsRequest(cookies, HttpMethod.Post, "api/engineering-mappings/owner-v1/commands");
            request.Content = new StringContent(JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
            return await host.Send(request);
        }
        using var saved = await Save(draft); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var confirm = draft with { OperationId = Guid.NewGuid(), Action = "CONFIRM", ExpectedVersion = "1", Candidate = null };
        using var confirmed = await Save(confirm); Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using var replay = await Save(confirm); Assert.Equal(await confirmed.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/engineering-mappings/owner-v1/{draft.MappingId:D}"));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.True(read.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync()); var data = json.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("canAdoptIdentitySite").GetBoolean()); Assert.Equal("CONFIRMED", data.GetProperty("state").GetString());
        Assert.Equal("confirm-engineering", data.GetProperty("businessConfirmation").GetProperty("action").GetString());
    }
}
