using System.Net;
using CP6.Core.Services.Common;
using CP6.Tests.Platform;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Price_recovery_catalog_registration_requires_explicit_current_grant_before_HTTP_read_and_close()
    {
        // Install the real anchored menu before the existing business fixture selects it.
        await using (var setup = server.Context(new TenantContext())) ErpMenuSeed.EnsureSeeded(setup);
        await using var h = await Seed();
        ErpPermissionSeed.EnsureSeeded(h.Db); h.Db.ChangeTracker.Clear();
        Assert.Equal(1, await h.Db.Sys_MenuActions.CountAsync(a => a.MenuId == 209 && a.ActionCode == "query"));
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(a => a.MenuId == 209 && a.ActionCode == "query"));
        ErpPermissionSeed.EnsureSeeded(h.Db); h.Db.ChangeTracker.Clear();
        Assert.Equal(1, await h.Db.Sys_MenuActions.CountAsync(a => a.MenuId == 209 && a.ActionCode == "query"));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h); var operation = Guid.NewGuid();
        HttpRequestMessage Message(HttpMethod method, string path, bool csrf = true)
        {
            var request = new HttpRequestMessage(method, "api/orders/price-correction/operations/v1/" + path);
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value)));
            if (csrf) request.Headers.Add("X-CSRF-Token", cookies["cp6_csrf"]);
            return request;
        }
        using (var denied = await host.Send(Message(HttpMethod.Get, "scope"))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await host.Send(Message(HttpMethod.Post, operation + "/close"))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, await Operations(h));
        // Simulated IAM input: explicitly grant the registered catalog capability, never a seed-side grant.
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 209, ActionCode = "query" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using (var scope = await host.Send(Message(HttpMethod.Get, "scope"))) Assert.True(scope.IsSuccessStatusCode, await scope.Content.ReadAsStringAsync());
        using (var read = await host.Send(Message(HttpMethod.Get, operation.ToString("D"))))
        { Assert.True(read.IsSuccessStatusCode, await read.Content.ReadAsStringAsync()); Assert.Contains("UNKNOWN", await read.Content.ReadAsStringAsync()); }
        using (var denied = await host.Send(Message(HttpMethod.Post, operation + "/close", false))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var closed = await host.Send(Message(HttpMethod.Post, operation + "/close")))
        { Assert.True(closed.IsSuccessStatusCode, await closed.Content.ReadAsStringAsync()); Assert.Contains("CLOSED", await closed.Content.ReadAsStringAsync()); }
        using (var original = await host.Send(Message(HttpMethod.Get, operation.ToString("D"))))
        { Assert.True(original.IsSuccessStatusCode, await original.Content.ReadAsStringAsync()); Assert.Contains("CLOSED", await original.Content.ReadAsStringAsync()); }
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 209 && a.ActionCode == "query").ExecuteDeleteAsync();
        using (var denied = await host.Send(Message(HttpMethod.Get, operation.ToString("D")))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(1, await Operations(h)); Assert.Equal(0, await Intents(h));
        Assert.All(await h.Db.OrderDetails.ToArrayAsync(), row => Assert.Equal(3m, row.IndividualUnitPrice));
    }
}
