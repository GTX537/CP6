using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<Dictionary<string, string>> Login(AuthenticationApiProcess host, Harness h)
    {
        using var response = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        { Content = JsonContent.Create(new { UserName = "price-owner", Password = "Price1!Password", TenantCode = h.Tenant.ToString("N") }) });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0].Split('=', 2))
            .Where(x => x[1].Length > 0).ToDictionary(x => x[0], x => x[1]);
    }

    private static HttpRequestMessage Request(Harness h, Dictionary<string, string> cookies, bool csrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "api/orders/price-correction/batch")
        { Content = JsonContent.Create(h.Request()) };
        request.Headers.Add("Cookie", string.Join("; ", cookies.Select(x => x.Key + "=" + x.Value)));
        if (csrf) request.Headers.Add("X-CSRF-Token", cookies["cp6_csrf"]);
        return request;
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_program_uses_original_login_csrf_owner_permissions_and_preserves_403_503_success_contracts()
    {
        await using var h = await Seed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using (var noCsrf = await host.Send(Request(h, cookies, false)))
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "UnitPrice", Access = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using (var field = await host.Send(Request(h, cookies)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, field.StatusCode);
            Assert.Contains("FIELD_WRITE_FORBIDDEN", await field.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, await Intents(h));
        await h.Db.Sys_RoleFieldPerms.ExecuteDeleteAsync();
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(d => d.ScopeType, 2));
        using (var scope = await host.Send(Request(h, cookies)))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, scope.StatusCode);
            Assert.Contains("OWNER_SCOPE_MAPPING_UNAVAILABLE", await scope.Content.ReadAsStringAsync());
        }
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(d => d.ScopeType, 5));
        using var allowed = await host.Send(Request(h, cookies));
        Assert.True(allowed.StatusCode == HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        var data = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("updatedCount").GetInt32());
        Assert.Equal("AwaitingAdapter", data.GetProperty("workflowDeliveryState").GetString());
        Assert.Equal(1, await Intents(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_program_rechecks_action_and_original_authentication_after_waiting_for_owner_root(bool credentials)
    {
        await using var h = await Seed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        await AuthenticationCompletionGateV1.AcquireAsync(h.Db);
        var attempt = host.Send(Request(h, cookies));
        try
        {
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                AND wait_event_type='Lock' AND query LIKE '%cp6_authentication_v1.acquire()%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            if (credentials)
                await h.Db.Sys_Users.Where(u => u.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.AuthenticationEpoch, Guid.NewGuid()));
            else
                await h.Db.Sys_RoleActions.Where(a => a.RoleId == 72).ExecuteDeleteAsync();
        }
        finally { await tx.CommitAsync(); }
        using var response = await attempt;
        Assert.True(response.StatusCode == (credentials ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden),
            await response.Content.ReadAsStringAsync());
        Assert.Contains(credentials ? "REAUTH_REQUIRED" : "ACTION_FORBIDDEN", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await Intents(h));
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(), d => Assert.Equal(3m, d.IndividualUnitPrice));
    }
}
