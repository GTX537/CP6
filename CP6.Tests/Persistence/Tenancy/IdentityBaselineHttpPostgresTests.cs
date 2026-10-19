using System.Text;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.CrmIdentity.Baselines;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("IdentityDirectoryConfiguredHttpPostgres")]
public sealed class IdentityBaselineHttpPostgresTests(IdentityDirectoryConfiguredPostgresFixture postgres)
{
    [CurrentPermissionPostgresFact]
    public async Task Product_collector_completes_real_A_B_A_directories_using_only_ordinary_identities()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres);
        foreach (var tenant in new[] { A, B, A })
        {
            var token = await host.Token(tenant); using var collector = Collector(host, tenant, _ => ValueTask.FromResult(token.Token), 1);
            var baseline = await collector.CollectAsync();
            await using var control = postgres.OwnerContext(tenant);
            var expected = await control.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync();
            Assert.Equal(expected.Length, baseline.TotalCount); Assert.Equal(Source(host, tenant), baseline.Source);
            foreach (var row in expected)
            {
                var actual = Assert.Single(baseline.Items, x => x.Item.AggregateId == row.AggregateId);
                Assert.Equal(Encoding.UTF8.GetBytes(row.PayloadJson), Convert.FromBase64String(actual.PayloadBase64));
                Assert.Equal(row.PayloadSha256, actual.Item.PayloadSha256); Assert.Equal(row.Version, actual.Item.Version);
                using var json = JsonDocument.Parse(Convert.FromBase64String(actual.PayloadBase64));
                Assert.Equal(tenant, json.RootElement.GetProperty("tenantId").GetGuid());
            }
        }
        Clean(host);
        await using var owner = await postgres.OpenOwner();
        Assert.Equal(new[] { "directory_a", "directory_b" }, await Scalar<string[]>(owner,
            "SELECT array_agg(DISTINCT usename::text ORDER BY usename::text) FROM pg_stat_activity WHERE datname=current_database() AND usename IN ('directory_a','directory_b')"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Writes_between_pages_snapshots_and_final_directory_reject_the_attempt_and_fresh_attempt_recovers()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var token = await host.Token(A);
        foreach (var phase in new[] { "page", "snapshot", "final" })
        {
            await using var control = postgres.OwnerContext(A);
            var count = await control.CrmIdentitySnapshots.CountAsync(x => x.TenantId == A);
            var original = await control.Sys_Roles.Where(x => x.TenantId == A && x.RoleId == 777).Select(x => x.Enable).SingleAsync();
            var trigger = phase == "final" ? count + 2 : 2; var calls = 0; var changed = false;
            using var collector = Collector(host, A, async ct =>
            {
                if (++calls == trigger) { await Role(host, !original, ct); changed = true; }
                return token.Token;
            }, phase == "page" ? 1 : 200);
            try
            {
                var error = await Assert.ThrowsAsync<IdentityBaselineException>(() => collector.CollectAsync()); Assert.True(changed);
                if (phase == "page") Assert.Equal("C02_BASELINE_HTTP_409", error.Message);
                if (phase == "final") Assert.Equal("C02_BASELINE_BOUNDARY_CHANGED", error.Message);
                var recovered = await collector.CollectAsync(); Assert.NotEmpty(recovered.Items); Assert.Equal(Source(host, A), recovered.Source);
            }
            finally { if (changed) await Role(host, original, default); }
        }
        Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Corrupt_hash_and_unready_source_fail_and_recover_without_partial_publication()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var token = await host.Token(A);
        using var collector = Collector(host, A, _ => ValueTask.FromResult(token.Token));
        await using var db = postgres.OwnerContext(A); var aggregate = "tenant:" + A.ToString("D");
        var row = await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == A && x.AggregateId == aggregate);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Snapshot\" SET \"PayloadSha256\"={new string('0', 64)} WHERE \"TenantId\"={A} AND \"AggregateId\"={aggregate}");
        try { Assert.Equal("C02_BASELINE_HTTP_503", (await Assert.ThrowsAsync<IdentityBaselineException>(() => collector.CollectAsync())).Message); }
        finally { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Snapshot\" SET \"PayloadSha256\"={row.PayloadSha256} WHERE \"TenantId\"={A} AND \"AggregateId\"={aggregate}"); }
        Assert.NotEmpty((await collector.CollectAsync()).Items);
        var state = await db.CrmIdentityBootstrapStates.AsNoTracking().SingleAsync(x => x.TenantId == A);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"=NULL WHERE \"TenantId\"={A}");
        try { Assert.Equal("C02_BASELINE_HTTP_503", (await Assert.ThrowsAsync<IdentityBaselineException>(() => collector.CollectAsync())).Message); }
        finally { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"={state.CompletedAtUtc} WHERE \"TenantId\"={A}"); }
        Assert.NotEmpty((await collector.CollectAsync()).Items); Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task A_snapshot_removed_after_initial_directory_cannot_be_skipped()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var token = await host.Token(A);
        await using var db = postgres.OwnerContext(A);
        var row = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == A).OrderBy(x => x.AggregateId).FirstAsync();
        var calls = 0; var removed = false;
        using var collector = Collector(host, A, async ct =>
        {
            if (++calls == 2)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM crm_identity.\"Snapshot\" WHERE \"TenantId\"={A} AND \"AggregateId\"={row.AggregateId}", ct);
                removed = true;
            }
            return token.Token;
        });
        try { Assert.Equal("C02_BASELINE_HTTP_404", (await Assert.ThrowsAsync<IdentityBaselineException>(() => collector.CollectAsync())).Message); Assert.Equal(2, calls); }
        finally { if (removed) { db.CrmIdentitySnapshots.Add(row); await db.SaveChangesAsync(); } }
        Assert.NotEmpty((await collector.CollectAsync()).Items); Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Current_revocation_or_tenant_disable_interrupts_collection_and_a_valid_fresh_attempt_recovers()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres);
        foreach (var action in new[] { "revoke", "disable" })
        {
            var token = await host.Token(A); var calls = 0;
            await using var db = postgres.OwnerContext(A);
            using var collector = Collector(host, A, async ct =>
            {
                if (++calls == 2)
                {
                    if (action == "revoke") await new CrmServiceTokenRecordStore(db, host.Runtime).RevokeAsync(host.Options.Issuer, "reader-a", A, token.Jti, ct);
                    else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false WHERE \"Id\"={A}", ct);
                }
                return token.Token;
            });
            try { Assert.Equal("C02_BASELINE_HTTP_403", (await Assert.ThrowsAsync<IdentityBaselineException>(() => collector.CollectAsync())).Message); Assert.Equal(2, calls); }
            finally { if (action == "disable") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=true WHERE \"Id\"={A}"); }
            var fresh = await host.Token(A); using var recovered = Collector(host, A, _ => ValueTask.FromResult(fresh.Token));
            Assert.NotEmpty((await recovered.CollectAsync()).Items);
        }
        Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Configured_expected_source_cannot_silently_switch_tenant_or_installed_identity()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var a = await host.Token(A); var b = await host.Token(B);
        using var wrongTenant = Collector(host, A, _ => ValueTask.FromResult(b.Token));
        Assert.Equal("C02_BASELINE_SOURCE_CHANGED", (await Assert.ThrowsAsync<IdentityBaselineException>(() => wrongTenant.CollectAsync())).Message);
        using var wrongIdentity = new IdentityBaselineCollector(host.Runtime, Source(host, A) with { StorageIdentitySha256 = new string('0', 64) },
            host.Client.BaseAddress!, _ => ValueTask.FromResult(a.Token), new(AllowLoopbackHttp: true));
        Assert.Equal("C02_BASELINE_SOURCE_CHANGED", (await Assert.ThrowsAsync<IdentityBaselineException>(() => wrongIdentity.CollectAsync())).Message);
        Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Complete_empty_native_directory_is_represented_without_claiming_authorization()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var token = await host.Token(A);
        await using var db = postgres.OwnerContext(A); var rows = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == A).ToArrayAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM crm_identity.\"Snapshot\" WHERE \"TenantId\"={A}");
        try
        {
            using var collector = Collector(host, A, _ => ValueTask.FromResult(token.Token)); var empty = await collector.CollectAsync();
            Assert.Empty(empty.Items); Assert.Equal(0, empty.TotalCount); Assert.Equal("SOURCE_SNAPSHOT", empty.Provenance);
        }
        finally { db.CrmIdentitySnapshots.AddRange(rows); await db.SaveChangesAsync(); }
        Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Maximum_original_payload_survives_actual_PG_HTTP_and_complete_collection()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var token = await host.Token(A);
        await using var db = postgres.OwnerContext(A); var aggregate = "tenant:" + A.ToString("D");
        var row = await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == A && x.AggregateId == aggregate);
        var original = Encoding.UTF8.GetBytes(row.PayloadJson);
        var bytes = original.Concat(Enumerable.Repeat((byte)' ', IdentityBaselineVerifier.MaximumItemPayloadBytes - original.Length)).ToArray();
        var text = Encoding.UTF8.GetString(bytes); var hash = IdentityEventContracts.Hash(bytes);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Snapshot\" SET \"PayloadJson\"={text},\"PayloadSha256\"={hash} WHERE \"TenantId\"={A} AND \"AggregateId\"={aggregate}");
        try
        {
            using var collector = Collector(host, A, _ => ValueTask.FromResult(token.Token));
            var baseline = await collector.CollectAsync(); var snapshot = Assert.Single(baseline.Items, x => x.Item.AggregateId == aggregate);
            Assert.Equal(bytes, Convert.FromBase64String(snapshot.PayloadBase64)); Assert.Equal(hash, snapshot.Item.PayloadSha256);
        }
        finally { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Snapshot\" SET \"PayloadJson\"={row.PayloadJson},\"PayloadSha256\"={row.PayloadSha256} WHERE \"TenantId\"={A} AND \"AggregateId\"={aggregate}"); }
        Clean(host);
    }

    [CurrentPermissionPostgresFact]
    public async Task Cancelled_product_collection_releases_blocked_PG_query_and_recovers_pool_capacity()
    {
        await using var host = await IdentityDirectoryConfiguredHttpHost.Start(postgres); var token = await host.Token(A);
        using var collector = Collector(host, A, _ => ValueTask.FromResult(token.Token));
        await using var owner = await postgres.OpenOwner(); await using var blocker = await owner.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE crm_identity.\"Snapshot\" IN ACCESS EXCLUSIVE MODE", owner, blocker)) await command.ExecuteNonQueryAsync();
        using var cancellation = new CancellationTokenSource(); var pending = collector.CollectAsync(cancellation.Token);
        try
        {
            await using var observer = await postgres.OpenOwner();
            await WaitUntil(async () => await Scalar<bool>(observer, "SELECT EXISTS(SELECT FROM pg_stat_activity WHERE usename='directory_a' AND wait_event_type='Lock' AND query LIKE '%Snapshot%')"));
            cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            try { await WaitUntil(() => Task.FromResult(host.Host.PoolSnapshot.Borrowers == 0)); }
            catch (Xunit.Sdk.XunitException)
            {
                var states = await Scalar<string>(observer, "SELECT coalesce(string_agg(state||'/'||coalesce(wait_event_type,'-')||'/'||coalesce(wait_event,'-')||'/standalone_count='||(query LIKE 'SELECT count(*)%' AND query NOT LIKE '%Bootstrap%')::text,','),'none') FROM pg_stat_activity WHERE usename='directory_a'");
                Assert.Fail($"Cancellation trace: {host.RequestDiagnostics}; borrowers={host.Host.PoolSnapshot.Borrowers}; PG={states}");
            }
        }
        finally { cancellation.Cancel(); await blocker.RollbackAsync(); }
        Assert.NotEmpty((await collector.CollectAsync()).Items); Clean(host);
    }

    private IdentityBaselineSourceV1 Source(IdentityDirectoryConfiguredHttpHost host, Guid tenant)
        => new(tenant, host.Runtime.Options.Issuer, host.Runtime.Options.Tenants[tenant], host.Runtime.ContractBundleSha256, postgres.Identity.BaselineSha256);
    private IdentityBaselineCollector Collector(IdentityDirectoryConfiguredHttpHost host, Guid tenant,
        Func<CancellationToken, ValueTask<string>> token, int pageSize = 200)
        => new(host.Runtime, Source(host, tenant), host.Client.BaseAddress!, token, new(PageSize: pageSize, AllowLoopbackHttp: true));
    private async Task Role(IdentityDirectoryConfiguredHttpHost host, bool enabled, CancellationToken ct)
    {
        await using var writer = postgres.Server.Context(new TenantContext { CurrentTenantId = A }, host.Runtime);
        var role = await writer.Sys_Roles.SingleAsync(x => x.TenantId == A && x.RoleId == 777, ct); role.Enable = enabled; await writer.SaveChangesAsync(ct);
    }
    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < until) { if (await condition()) return; await Task.Delay(25); }
        Assert.Fail("Expected owned database/lease state was not observed within 8 seconds.");
    }
    private static void Clean(IdentityDirectoryConfiguredHttpHost host)
    { Assert.Equal(0, host.OwnerQueries); Assert.Equal(0, host.Host.PoolSnapshot.Borrowers); }
}
