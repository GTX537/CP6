using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("IdentityDirectoryPostgres")]
public sealed class IdentityDirectoryHttpPostgresTests(IdentityDirectoryPostgresFixture postgres)
{
    private static Guid A => IdentityDirectoryPostgresFixture.A;
    private static Guid B => IdentityDirectoryPostgresFixture.B;
    private const string Versions = "/internal/crm/identity/versions";
    private static string Snapshot(Guid tenant) => "/internal/crm/identity/snapshots/tenant:" + tenant.ToString("D");

    [CurrentPermissionPostgresFact]
    public async Task Actual_HTTP_JWT_current_authorization_and_A_B_A_ordinary_directory_reads_compose()
    {
        await using var host = await IdentityDirectoryHttpHost.Start(postgres);
        var a = await host.Token(A); var b = await host.Token(B);
        foreach (var (tenant, token) in new[] { (A, a.Token), (B, b.Token), (A, a.Token) })
        {
            using var pageResponse = await host.Get(Versions, token); await Status(pageResponse, HttpStatusCode.OK);
            var page = (await pageResponse.Content.ReadFromJsonAsync<IdentityVersionPage>())!;
            Assert.Equal(tenant, page.TenantId); Assert.NotEmpty(page.Items);
            using var snapshotResponse = await host.Get(Snapshot(tenant), token); await Status(snapshotResponse, HttpStatusCode.OK);
            var snapshot = (await snapshotResponse.Content.ReadFromJsonAsync<IdentitySnapshot>())!;
            Assert.Equal(tenant, snapshot.TenantId); Assert.Equal($"tenant:{tenant:D}", snapshot.AggregateId);
            using var other = await host.Get(Snapshot(tenant == A ? B : A), token); await Status(other, HttpStatusCode.NotFound);
        }
        Assert.Equal(0, host.Manager.Snapshot().Borrowers); Assert.Equal(2, host.Manager.Snapshot().Pools);
        await using var owner = await postgres.OpenOwner();
        Assert.Equal("fixture_owner", await IdentityDirectoryPostgresFixture.Scalar<string>(owner, "SELECT session_user::text"));
        Assert.Equal(2L, await IdentityDirectoryPostgresFixture.Scalar<long>(owner,
            "SELECT count(DISTINCT usename) FROM pg_stat_activity WHERE usename IN ('directory_a','directory_b') AND state='idle' AND backend_xid IS NULL"));
        // Authorization uses the separate fixture control context. Only directory reads are ordinary.
    }

    [CurrentPermissionPostgresFact]
    public async Task Actual_bearer_and_controller_reject_invalid_unrecorded_and_unregistered_readers()
    {
        await using var host = await IdentityDirectoryHttpHost.Start(postgres);
        var cases = new List<(string? Token, HttpStatusCode Status)>
        {
            (null, HttpStatusCode.Unauthorized), ("not.a.jwt", HttpStatusCode.Unauthorized),
            ((await host.Token(A, record: false)).Token, HttpStatusCode.Forbidden),
            ((await host.Token(A, audience: "unrelated-audience")).Token, HttpStatusCode.Unauthorized),
            ((await host.Token(A, expiry: DateTimeOffset.UtcNow.AddMinutes(-3))).Token, HttpStatusCode.Unauthorized),
            ((await host.Token(A, client: "not-reader")).Token, HttpStatusCode.Forbidden),
            ((await host.Token(A, tenantClaim: A.ToString("N"))).Token, HttpStatusCode.Forbidden),
            // Platform 0.10.3 rejects duplicate required claims during bearer validation,
            // before the controller's service-authorization layer can return 403.
            ((await host.Token(A, duplicateTenant: true)).Token, HttpStatusCode.Unauthorized),
            ((await host.Token(A, client: "reader-b")).Token, HttpStatusCode.Forbidden)
        };
        foreach (var (item, index) in cases.Select((item, index) => (item, index)))
        {
            using var response = await host.Get(Versions, item.Token);
            Assert.True(response.StatusCode == item.Status, $"Authentication matrix case {index}: expected {item.Status}, observed {response.StatusCode}.");
            await Status(response, item.Status);
        }
        var valid = await host.Token(A);
        using var accepted = await host.Get(Versions, valid.Token); await Status(accepted, HttpStatusCode.OK);
        Assert.Equal(0, host.Manager.Snapshot().Borrowers);
    }

    [CurrentPermissionPostgresFact]
    public async Task Current_token_revocation_and_tenant_disable_are_checked_on_each_HTTP_read()
    {
        await using var host = await IdentityDirectoryHttpHost.Start(postgres); var first = await host.Token(A);
        using (var initial = await host.Get(Versions, first.Token)) await Status(initial, HttpStatusCode.OK);
        await using (var db = postgres.OwnerContext(A))
            await new CrmServiceTokenRecordStore(db, host.Runtime).RevokeAsync(host.Options.Issuer, "reader-a", A, first.Jti, default);
        using (var revoked = await host.Get(Versions, first.Token)) await Status(revoked, HttpStatusCode.Forbidden);
        var fresh = await host.Token(A);
        await using var control = postgres.OwnerContext(A);
        await control.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false WHERE \"Id\"={A}");
        try { using var disabled = await host.Get(Versions, fresh.Token); await Status(disabled, HttpStatusCode.Forbidden); }
        finally { await control.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=true WHERE \"Id\"={A}"); }
        using var restored = await host.Get(Versions, fresh.Token); await Status(restored, HttpStatusCode.OK);
    }

    [CurrentPermissionPostgresFact]
    public async Task HTTP_preserves_pagination_conflict_input_and_unavailable_contracts_without_control_fallback()
    {
        await using var host = await IdentityDirectoryHttpHost.Start(postgres); var a = await host.Token(A); var b = await host.Token(B);
        using var pageResponse = await host.Get(Versions + "?pageSize=1", a.Token); await Status(pageResponse, HttpStatusCode.OK);
        var page = (await pageResponse.Content.ReadFromJsonAsync<IdentityVersionPage>())!; Assert.NotNull(page.NextCursor);
        var next = Versions + "?pageSize=1&cursor=" + Uri.EscapeDataString(page.NextCursor);
        using (var cross = await host.Get(next, b.Token)) await Status(cross, HttpStatusCode.BadRequest, "C02_INVALID_CURSOR");
        foreach (var query in new[] { "?tenant_id=" + B, "?pageSize=1&pageSize=2", "?pageSize=201", "?cursor=bad" })
        { using var bad = await host.Get(Versions + query, a.Token); await Status(bad, HttpStatusCode.BadRequest); }
        await using (var writer = postgres.Server.Context(new TenantContext { CurrentTenantId = A }, host.Runtime))
        {
            var role = await writer.Sys_Roles.SingleAsync(x => x.TenantId == A && x.RoleId == 777);
            role.Enable = !role.Enable; await writer.SaveChangesAsync();
        }
        using (var conflict = await host.Get(next, a.Token)) await Status(conflict, HttpStatusCode.Conflict, "C02_SNAPSHOT_BOUNDARY_CHANGED");
        await using var owner = await postgres.OpenOwner();
        // Remove only this runtime permission. Auth/current-token SQL still succeeds with control;
        // a successful directory response would expose an impermissible fallback.
        await IdentityDirectoryPostgresFixture.Sql(owner, "REVOKE SELECT ON crm_identity.\"Snapshot\" FROM directory_a");
        try
        {
            foreach (var path in new[] { Versions, Snapshot(A) })
            { using var denied = await host.Get(path, a.Token); await Status(denied, HttpStatusCode.ServiceUnavailable, "C02_IDENTITY_STORE_UNAVAILABLE"); }
        }
        finally { await IdentityDirectoryPostgresFixture.Sql(owner, "GRANT SELECT ON crm_identity.\"Snapshot\" TO directory_a"); }
        await using var db = postgres.OwnerContext(A);
        var completed = (await db.CrmIdentityBootstrapStates.AsNoTracking().SingleAsync(x => x.TenantId == A)).CompletedAtUtc;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"=NULL WHERE \"TenantId\"={A}");
        try
        {
            foreach (var path in new[] { Versions, Snapshot(A) })
            { using var unavailable = await host.Get(path, a.Token); await Status(unavailable, HttpStatusCode.ServiceUnavailable, "C02_BOOTSTRAP_NOT_READY"); }
        }
        finally { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"={completed} WHERE \"TenantId\"={A}"); }
        using var recovered = await host.Get(Versions, a.Token); await Status(recovered, HttpStatusCode.OK);
        Assert.Equal(0, host.Manager.Snapshot().Borrowers);
    }

    private static async Task Status(HttpResponseMessage response, HttpStatusCode expected, string? code = null)
    {
        Assert.Equal(expected, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        if (code is not null)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
            Assert.Single(body.RootElement.EnumerateObject());
        }
    }
}
