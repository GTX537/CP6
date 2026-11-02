using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static string NumberRulePath(Guid id, string suffix = "") => $"api/pub/seq/owner-v1/{id}" + suffix;
    private static async Task<T> NumberHttpData<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == HttpStatusCode.OK, response.StatusCode + ": " + await OperationsFailure(response));
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(text); var root = json.RootElement;
        return (root.TryGetProperty("data", out var data) ? data : root).Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_http_current_history_CAS_original_recovery_and_preview_preserve_allocations()
    {
        await using var f = await NumberSeed(); var h = f.H; await EngineeringAction(h, "pub-seq", "edit");
        var allocation = await f.Owner.AllocateAsync(NumberCommand());
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        Task<HttpResponseMessage> Send(HttpMethod method, string suffix = "", object? body = null, bool csrf = true)
            => host.Send(OperationsRequest(cookies, method, NumberRulePath(f.Scope.RuleId, suffix), body, csrf));
        using (var current = await Send(HttpMethod.Get)) Assert.Equal(f.Rule, await NumberHttpData<NumberAllocationRuleV1>(current));
        var wide = new string('界', 3000) + "\0/原键";
        var command = new NumberRuleChangeCommandV1(wide, f.Rule.Version, wide, "NEW", "yyyyMM", 5);
        using (var denied = await Send(HttpMethod.Post, "/changes", command, false)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        NumberRuleChangeReceiptV1 changed;
        using (var response = await Send(HttpMethod.Post, "/changes", command)) changed = await NumberHttpData<NumberRuleChangeReceiptV1>(response);
        Assert.Equal(h.Tenant, changed.TenantId); Assert.Equal(f.Scope.SourceOwner, changed.SourceOwner); Assert.Equal(wide, changed.OperationRef);
        using (var repeated = await Send(HttpMethod.Post, "/changes", command)) Assert.Equal(changed, await NumberHttpData<NumberRuleChangeReceiptV1>(repeated));
        using (var historical = await Send(HttpMethod.Post, "/versions/read", new { Version = f.Rule.Version })) Assert.Equal(f.Rule, await NumberHttpData<NumberAllocationRuleV1>(historical));
        using (var current = await Send(HttpMethod.Get)) Assert.Equal(changed.Rule, await NumberHttpData<NumberAllocationRuleV1>(current));
        var a = command with { OperationRef = "race-a", ExpectedRuleVersion = wide, NewVersion = "v3-a", Prefix = "A" };
        var b = a with { OperationRef = "race-b", NewVersion = "v3-b", Prefix = "B" };
        var results = await Task.WhenAll(Send(HttpMethod.Post, "/changes", a), Send(HttpMethod.Post, "/changes", b));
        try { Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.PreconditionFailed); }
        finally { foreach (var result in results) result.Dispose(); }
        using (var original = await Send(HttpMethod.Post, "/changes/read", new { OperationRef = wide })) Assert.Equal(changed, await NumberHttpData<NumberRuleChangeReceiptV1>(original));
        using (var oldVersion = await Send(HttpMethod.Post, "/versions/read", new { Version = wide })) Assert.Equal(changed.Rule, await NumberHttpData<NumberAllocationRuleV1>(oldVersion));
        using (var missing = await Send(HttpMethod.Post, "/versions/read", new { Version = "unknown" })) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var missing = await Send(HttpMethod.Post, "/changes/read", new { OperationRef = "unknown" })) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var invalid = await Send(HttpMethod.Post, "/changes", new { command.OperationRef, command.ExpectedRuleVersion, command.NewVersion, command.Prefix, command.DateFormat, command.MinimumWidth, TenantId = Guid.NewGuid() })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var invalid = await Send(HttpMethod.Post, "/changes", command with { Prefix = null! })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var preview = await Send(HttpMethod.Get, "/preview")) Assert.False((await NumberHttpData<NumberAllocationPreviewV1>(preview)).IsReservation);
        Assert.Equal(allocation, await f.Owner.ReadAsync(NumberCommand().OperationRef));
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_number_allocation_v1.receipts WHERE rule_id={f.Scope.RuleId}").SingleAsync());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT sum(current_value)::int AS \"Value\" FROM cp6_number_allocation_v1.periods WHERE rule_id={f.Scope.RuleId}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_http_current_permissions_cross_tenant_and_revoked_session_control_reads_and_changes()
    {
        await using var f = await NumberSeed(); await using var other = await NumberSeed(); var h = f.H;
        await EngineeringAction(h, "pub-seq", "edit");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        Task<HttpResponseMessage> Send(HttpMethod method, string suffix = "", object? body = null)
            => host.Send(OperationsRequest(cookies, method, NumberRulePath(f.Scope.RuleId, suffix), body));
        using (var anonymous = await host.Send(new(HttpMethod.Get, NumberRulePath(f.Scope.RuleId)))) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var cross = await host.Send(OperationsRequest(cookies, HttpMethod.Get, NumberRulePath(other.Scope.RuleId)))) Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
        var change = new NumberRuleChangeCommandV1("one", f.Rule.Version, "v2", "N2", null, 4);
        using (var saved = await Send(HttpMethod.Post, "/changes", change)) _ = await NumberHttpData<NumberRuleChangeReceiptV1>(saved);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "pub-seq").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "edit").ExecuteDeleteAsync();
        using (var original = await Send(HttpMethod.Post, "/changes", change)) _ = await NumberHttpData<NumberRuleChangeReceiptV1>(original);
        using (var forbidden = await Send(HttpMethod.Post, "/changes", change with { OperationRef = "two", ExpectedRuleVersion = "v2", NewVersion = "v3" })) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        using (var forbidden = await Send(HttpMethod.Post, "/changes/read", new { OperationRef = "one" })) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await EngineeringAction(h, "pub-seq", "query");
        using (var logout = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/auth/logout", new { }))) Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using (var revoked = await Send(HttpMethod.Get)) Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal("v2", (await f.Owner.ReadRuleAsync())!.Version);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Number_http_rechecks_current_authority_after_waiting_for_owner_gate(bool credentials)
    {
        await using var f = await NumberSeed(); var h = f.H; await EngineeringAction(h, "pub-seq", "edit");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        await using var tx = await h.Db.Database.BeginTransactionAsync(); await AuthenticationCompletionGateV1.AcquireAsync(h.Db);
        var attempt = host.Send(OperationsRequest(cookies, HttpMethod.Post, NumberRulePath(f.Scope.RuleId, "/changes"),
            new NumberRuleChangeCommandV1("blocked", f.Rule.Version, "v2", "N2", null, 4)));
        try
        {
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database()
                AND wait_event_type='Lock' AND query LIKE '%cp6_authentication_v1.acquire()%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            if (credentials) await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.AuthenticationEpoch, Guid.NewGuid()));
            else await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit").ExecuteDeleteAsync();
        }
        finally { await tx.CommitAsync(); }
        using var response = await attempt;
        Assert.True(response.StatusCode == (credentials ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden), await OperationsFailure(response));
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_number_allocation_v1.rule_changes WHERE rule_id={f.Scope.RuleId}").SingleAsync());
    }
}
