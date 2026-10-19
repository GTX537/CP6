using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("IdentityDirectoryConfiguredHttpPostgres")]
public sealed class IdentityDirectoryConfiguredHttpMatrixTests(IdentityDirectoryConfiguredPostgresFixture postgres)
{
    private const string Versions = "/internal/crm/identity/versions";
    private const string Prefix = IdentityDirectoryConfigurationTests.Prefix;
    private static string Snapshot(Guid tenant) => "/internal/crm/identity/snapshots/tenant:" + tenant.ToString("D");

    [CurrentPermissionPostgresFact]
    public async Task Actual_JWT_and_frozen_registration_reject_invalid_unrecorded_and_foreign_readers()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres);
        var valid = await host.Token(A); var parts = valid.Token.Split('.');
        parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        var cases = new List<(string? Token, HttpStatusCode Status)>
        {
            (null, HttpStatusCode.Unauthorized), ("not.a.jwt", HttpStatusCode.Unauthorized),
            (string.Join('.', parts), HttpStatusCode.Unauthorized),
            ((await host.Token(A, record: false)).Token, HttpStatusCode.Forbidden),
            ((await host.Token(A, audience: "unrelated-audience")).Token, HttpStatusCode.Unauthorized),
            ((await host.Token(A, expiry: DateTimeOffset.UtcNow.AddMinutes(-3))).Token, HttpStatusCode.Unauthorized),
            ((await host.Token(A, client: "not-reader")).Token, HttpStatusCode.Forbidden),
            ((await host.Token(A, tenantClaim: A.ToString("N"))).Token, HttpStatusCode.Forbidden),
            ((await host.Token(A, duplicateTenant: true)).Token, HttpStatusCode.Unauthorized),
            ((await host.Token(A, client: "reader-b")).Token, HttpStatusCode.Forbidden)
        };
        foreach (var item in cases)
        { using var response = await host.Get(Versions, item.Token); await Status(response, item.Status); }
        // The accepted legacy JWT clock allowance must not shrink during context binding.
        using var skew = await host.Get(Versions, (await host.Token(A, expiry: DateTimeOffset.UtcNow.AddSeconds(-20))).Token);
        await Status(skew, HttpStatusCode.OK);
        using var accepted = await host.Get(Versions, valid.Token); await Status(accepted, HttpStatusCode.OK);
        Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Current_revocation_and_tenant_eligibility_use_ordinary_identity_on_every_read()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var first = await host.Token(A);
        using (var initial = await host.Get(Versions, first.Token)) await Status(initial, HttpStatusCode.OK);
        await using (var db = postgres.OwnerContext(A))
            await new CrmServiceTokenRecordStore(db, host.Runtime).RevokeAsync(host.Options.Issuer, "reader-a", A, first.Jti, default);
        using (var revoked = await host.Get(Versions, first.Token)) await Status(revoked, HttpStatusCode.Forbidden);
        var fresh = await host.Token(A);
        await using var control = postgres.OwnerContext(A);
        await control.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false WHERE \"Id\"={A}");
        try
        {
            using var disabled = await host.Get(Versions, fresh.Token); await Status(disabled, HttpStatusCode.Forbidden);
            using var other = await host.Get(Versions, (await host.Token(B)).Token); await Status(other, HttpStatusCode.OK);
        }
        finally { await control.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=true WHERE \"Id\"={A}"); }
        using var restored = await host.Get(Versions, fresh.Token); await Status(restored, HttpStatusCode.OK);
        Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Pagination_generation_and_bootstrap_contracts_survive_actual_host_adoption()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var a = await host.Token(A); var b = await host.Token(B);
        using var pageResponse = await host.Get(Versions + "?pageSize=1", a.Token); await Status(pageResponse, HttpStatusCode.OK);
        var page = (await pageResponse.Content.ReadFromJsonAsync<IdentityVersionPage>())!; Assert.NotNull(page.NextCursor);
        var next = Versions + "?pageSize=1&cursor=" + Uri.EscapeDataString(page.NextCursor);
        using (var cross = await host.Get(next, b.Token)) await Status(cross, HttpStatusCode.BadRequest, "C02_INVALID_CURSOR");
        foreach (var query in new[] { "?tenant_id=" + B, "?pageSize=1&pageSize=2", "?pageSize=201", "?cursor=bad" })
        { using var bad = await host.Get(Versions + query, a.Token); await Status(bad, HttpStatusCode.BadRequest); }
        using (var own = await host.Get(Snapshot(A), a.Token))
        { await Status(own, HttpStatusCode.OK); Assert.Equal(A, (await own.Content.ReadFromJsonAsync<IdentitySnapshot>())!.TenantId); }
        await using (var writer = postgres.Server.Context(new TenantContext { CurrentTenantId = A }, host.Runtime))
        {
            var role = await writer.Sys_Roles.SingleAsync(x => x.TenantId == A && x.RoleId == 777);
            role.Enable = !role.Enable; await writer.SaveChangesAsync();
        }
        using (var conflict = await host.Get(next, a.Token)) await Status(conflict, HttpStatusCode.Conflict, "C02_SNAPSHOT_BOUNDARY_CHANGED");
        await using var db = postgres.OwnerContext(A);
        var completed = (await db.CrmIdentityBootstrapStates.AsNoTracking().SingleAsync(x => x.TenantId == A)).CompletedAtUtc;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"=NULL WHERE \"TenantId\"={A}");
        try
        {
            foreach (var path in new[] { Versions, Snapshot(A) })
            { using var unavailable = await host.Get(path, a.Token); await Status(unavailable, HttpStatusCode.ServiceUnavailable, "C02_BOOTSTRAP_NOT_READY"); }
        }
        finally { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"={completed} WHERE \"TenantId\"={A}"); }
        using var recovered = await host.Get(Versions, a.Token); await Status(recovered, HttpStatusCode.OK); Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Installation_identity_credentials_privilege_drift_and_registry_expiry_fail_without_Owner_fallback()
    {
        var overrides = new[]
        {
            new Dictionary<string, string?> { [Prefix + "Fingerprint"] = new string('0', 64) },
            new Dictionary<string, string?> { [Prefix + "DatabaseOid"] = (postgres.Identity.DatabaseOid + 1).ToString() },
            new Dictionary<string, string?> { [Prefix + "Tenants:0:Password"] = "wrong-fixture-password" },
            new Dictionary<string, string?> { [Prefix + "ValidUntilUtc"] = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O") }
        };
        foreach (var settings in overrides)
        {
            await using var rejected = await IdentityDirectoryConfiguredHttpHost.Start(postgres, settings);
            using var response = await rejected.Get(Versions, (await rejected.Token(A)).Token);
            await Status(response, HttpStatusCode.ServiceUnavailable, "C02_IDENTITY_STORE_UNAVAILABLE"); Clean(rejected);
        }
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var a = await host.Token(A);
        await using var owner = await postgres.OpenOwner();
        const string aclSql = "SELECT relacl::text FROM pg_class WHERE oid='crm_identity.\"Snapshot\"'::regclass";
        var originalAcl = await Scalar<string>(owner, aclSql);
        await Sql(owner, "REVOKE SELECT ON crm_identity.\"Snapshot\" FROM directory_a");
        try
        {
            foreach (var path in new[] { Versions, Snapshot(A) })
            { using var denied = await host.Get(path, a.Token); await Status(denied, HttpStatusCode.ServiceUnavailable, "C02_IDENTITY_STORE_UNAVAILABLE"); }
        }
        finally
        {
            // The unchanged versioned catalog seals user ACL array order as well as grants.
            // Restore both fixture readers in installation order; granting A alone appends it
            // after B and correctly remains drift. Do not reseal or change product semantics.
            await Sql(owner, "REVOKE SELECT ON crm_identity.\"Snapshot\" FROM directory_a,directory_b; GRANT SELECT ON crm_identity.\"Snapshot\" TO directory_a,directory_b");
            Assert.Equal(originalAcl, await Scalar<string>(owner, aclSql));
            Assert.Equal(postgres.Identity.Fingerprint, await Scalar<string>(owner, "SELECT cp6_identity_directory_v2.fingerprint()"));
        }
        using var restored = await host.Get(Versions, a.Token); await Status(restored, HttpStatusCode.OK); Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Mutable_original_options_cannot_expand_or_retarget_the_frozen_host()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var accepted = await host.Token(A);
        var original = host.Options.ServiceClients.Single(c => c.ClientId == "reader-a"); original.TenantId = B; original.Enabled = false;
        host.Runtime.Options.Tenants[A] = "changed-region";
        host.Runtime.Options.ProjectionReaderClientIds = ["reader-b", "not-reader"];
        using var same = await host.Get(Versions, accepted.Token); await Status(same, HttpStatusCode.OK);
        Assert.Equal(A, (await same.Content.ReadFromJsonAsync<IdentityVersionPage>())!.TenantId);
        using var expanded = await host.Get(Versions, (await host.Token(A, client: "not-reader")).Token); await Status(expanded, HttpStatusCode.Forbidden);
        using var retargeted = await host.Get(Versions, (await host.Token(B, client: "reader-a")).Token); await Status(retargeted, HttpStatusCode.Forbidden);
        Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Source_expiring_while_directory_query_waits_discards_result_and_releases_lease()
    {
        var clock = new AdjustableClock(DateTimeOffset.UtcNow);
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres,
            new Dictionary<string, string?> { [Prefix + "SourceLifetimeSeconds"] = "1" }, clock);
        var token = await host.Token(A);
        await using var owner = await postgres.OpenOwner(); await using var blocker = await owner.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE crm_identity.\"Snapshot\" IN ACCESS EXCLUSIVE MODE", owner, blocker)) await command.ExecuteNonQueryAsync();
        var pending = host.Get(Versions, token.Token);
        try { await WaitForBlockedDirectory(); clock.Advance(TimeSpan.FromSeconds(2)); }
        finally { await blocker.RollbackAsync(); }
        using var response = await pending; await Status(response, HttpStatusCode.ServiceUnavailable, "C02_IDENTITY_STORE_UNAVAILABLE");
        using var recovered = await host.Get(Versions, token.Token); await Status(recovered, HttpStatusCode.OK); Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Aborted_HTTP_request_cancels_blocked_query_and_returns_pool_capacity()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var token = await host.Token(A);
        await using var owner = await postgres.OpenOwner(); await using var blocker = await owner.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE crm_identity.\"Snapshot\" IN ACCESS EXCLUSIVE MODE", owner, blocker)) await command.ExecuteNonQueryAsync();
        using var cancellation = new CancellationTokenSource(); var pending = host.Get(Versions, token.Token, cancellation.Token);
        try
        {
            await WaitForBlockedDirectory(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using var response = await pending; });
            await WaitUntil(() => Task.FromResult(host.Host.PoolSnapshot.Borrowers == 0));
        }
        finally { cancellation.Cancel(); await blocker.RollbackAsync(); }
        using var recovered = await host.Get(Versions, token.Token); await Status(recovered, HttpStatusCode.OK); Clean(host);
    }

    private async Task WaitForBlockedDirectory()
    {
        await using var observer = await postgres.OpenOwner();
        await WaitUntil(async () => await Scalar<bool>(observer,
            "SELECT EXISTS(SELECT FROM pg_stat_activity WHERE usename='directory_a' AND wait_event_type='Lock' AND query LIKE '%Snapshot%')"));
    }
    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < until) { if (await condition()) return; await Task.Delay(25); }
        Assert.Fail("Expected owned database/lease state was not observed within 8 seconds.");
    }
    private static void Clean(IdentityDirectoryConfiguredHttpHost host)
    { Assert.Equal(0, host.OwnerQueries); Assert.Equal(0, host.Host.PoolSnapshot.Borrowers); }
    private static async Task Status(HttpResponseMessage response, HttpStatusCode expected, string? code = null)
    {
        Assert.Equal(expected, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        if (code is not null)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(code, body.RootElement.GetProperty("code").GetString()); Assert.Single(body.RootElement.EnumerateObject());
        }
    }
    private sealed class AdjustableClock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks = now.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        internal void Advance(TimeSpan interval) => Interlocked.Add(ref _ticks, interval.Ticks);
    }
}
