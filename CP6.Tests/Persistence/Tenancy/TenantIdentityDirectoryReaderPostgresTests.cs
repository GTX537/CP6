using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.CrmIdentity;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("IdentityDirectoryPostgres")]
public sealed class TenantIdentityDirectoryReaderPostgresTests(IdentityDirectoryPostgresFixture postgres)
{
    private static Guid A => IdentityDirectoryPostgresFixture.A;
    private static Guid B => IdentityDirectoryPostgresFixture.B;
    internal static TenantStorageCatalog Catalog(string model = IdentityDirectoryRowSecurityV1.ModelVersion,
        string migration = IdentityDirectoryRowSecurityV1.MigrationVersion, string revision = "r1", string[]? domains = null) =>
        new("main", [model], new[] { A, B }.Select(tenant =>
            new TenantStorageBindingV1(tenant, "main", "db", TenantStorageBindingV1.SupportedLayout, model, revision, "v1",
                tenant == A ? "principal-a" : "principal-b", domains ?? [], TenantStorageState.Ready, migration)),
            [new("db", "127.0.0.1", 55435, TenantManagementPostgresFixture.Database, SslMode.Disable)],
            [new("principal-a", "directory_a"), new("principal-b", "directory_b")]);
    private TenantConnectionManager Manager(TenantStorageContext context, TenantStorageCatalog? catalog = null) => new(
        "main", context.Accessor, catalog ?? Catalog(), new TenantStorageCredentials
        { Resolve = (binding, _) => ValueTask.FromResult(binding.TenantId == A ? postgres.PasswordA : postgres.PasswordB) }, new(2, 2, 1));
    private static TenantIdentityDirectoryReader Reader(TenantConnectionManager manager) =>
        new(manager, IdentityDirectoryPostgresFixture.Runtime(), new EphemeralDataProtectionProvider());

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_reader_reuses_A_B_A_connections_and_returns_only_current_tenant_DTOs()
    {
        var context = new TenantStorageContext(); await using var manager = Manager(context); var reader = Reader(manager);
        var identities = new List<(int Pid, string Login)>();
        foreach (var tenant in new[] { A, B, A })
        {
            await context.Run(tenant, async ct =>
            {
                await using (var lease = await manager.OpenCurrentAsync(ct))
                    identities.Add(await lease.InTransactionAsync(async (tx, token) => await tx.ExecuteAsync(async (connection, transaction, cancellation) =>
                    {
                        await using var command = new NpgsqlCommand("SELECT pg_backend_pid(), session_user::text", connection, transaction);
                        await using var result = await command.ExecuteReaderAsync(cancellation); Assert.True(await result.ReadAsync(cancellation));
                        return (result.GetInt32(0), result.GetString(1));
                    }, token), cancellationToken: ct));
                var page = await reader.ReadVersionsAsync(tenant, null, 1, ct);
                Assert.Equal(tenant, page.TenantId); Assert.Single(page.Items); Assert.NotNull(page.NextCursor);
                var last = await reader.ReadVersionsAsync(tenant, page.NextCursor, 1, ct);
                Assert.Equal(page.Boundary, last.Boundary); Assert.Null(last.NextCursor);
                var own = await reader.ReadSnapshotAsync(tenant, $"tenant:{tenant:D}", ct);
                Assert.NotNull(own); Assert.Equal(tenant, own.TenantId);
                Assert.Null(await reader.ReadSnapshotAsync(tenant, $"tenant:{(tenant == A ? B : A):D}", ct));
            });
            Assert.Equal(0, manager.Snapshot().Borrowers);
        }
        Assert.Equal(new[] { "directory_a", "directory_b", "directory_a" }, identities.Select(x => x.Login));
        Assert.Equal(identities[0].Pid, identities[2].Pid); Assert.NotEqual(identities[0].Pid, identities[1].Pid);
    }

    [CurrentPermissionPostgresFact]
    public async Task Pg_page_preserves_the_callers_read_committed_transaction()
    {
        await using var db = postgres.OwnerContext(A);
        var reader = new IdentitySnapshotReader(db, IdentityDirectoryPostgresFixture.Runtime(), new EphemeralDataProtectionProvider());
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.NotEmpty((await reader.ReadVersionsAsync(A, null)).Items); Assert.Same(tx, db.Database.CurrentTransaction);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"=NULL WHERE \"TenantId\"={A}");
            Assert.Equal("C02_BOOTSTRAP_NOT_READY", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(A, null))).Message);
            Assert.Same(tx, db.Database.CurrentTransaction); await tx.RollbackAsync();
        }
        Assert.NotEmpty((await reader.ReadVersionsAsync(A, null)).Items);
    }

    [CurrentPermissionPostgresFact]
    public async Task Missing_disposed_scope_wrong_tenant_and_unrelated_profiles_fail_closed()
    {
        var context = new TenantStorageContext(); await using var manager = Manager(context); var reader = Reader(manager);
        await Unavailable(() => reader.ReadVersionsAsync(A, null));
        await context.Run(A, async ct =>
        {
            await Unavailable(() => reader.ReadSnapshotAsync(B, $"tenant:{B:D}", ct));
            foreach (var catalog in new[] { Catalog(model: "other"), Catalog(migration: "other"), Catalog(domains: ["crm_identity_priority"]) })
            {
                manager.ReplaceCatalog(catalog);
                await Unavailable(() => reader.ReadVersionsAsync(A, null, cancellationToken: ct));
            }
            manager.ReplaceCatalog(Catalog());
            Assert.NotEmpty((await reader.ReadVersionsAsync(A, null, cancellationToken: ct)).Items);
        });
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<IdentityVersionPage>? escaped = null;
        await context.Run(A, _ =>
        {
            // Deliberately retain an async flow to verify that disposing the trusted scope
            // invalidates its captured capability; production code must not detach work.
            escaped = Task.Run(async () => { await released.Task; return await reader.ReadVersionsAsync(A, null); });
            return Task.CompletedTask;
        });
        released.SetResult(); await Unavailable(() => escaped!);
        Assert.Equal(0, manager.Snapshot().Borrowers);
    }

    [CurrentPermissionPostgresFact]
    public async Task Reader_preserves_cursor_errors_and_generation_conflict_without_leaking_storage_errors()
    {
        var context = new TenantStorageContext(); await using var manager = Manager(context); var reader = Reader(manager);
        string? cursor = null;
        await context.Run(A, async ct =>
        {
            cursor = (await reader.ReadVersionsAsync(A, null, 1, ct)).NextCursor; Assert.NotNull(cursor);
            Assert.Equal("C02_INVALID_PAGE_SIZE", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(A, null, 201, ct))).Message);
            Assert.Equal("C02_INVALID_CURSOR", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(A, "bad-cursor", 1, ct))).Message);
            Assert.Equal("C02_INVALID_AGGREGATE", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadSnapshotAsync(A, "bad/id", ct))).Message);
        });
        await context.Run(B, async ct => Assert.Equal("C02_INVALID_CURSOR",
            (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(B, cursor, 1, ct))).Message));
        var runtime = IdentityDirectoryPostgresFixture.Runtime();
        await using (var writer = postgres.Server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = A }, runtime))
        {
            var role = await writer.Sys_Roles.SingleAsync(x => x.TenantId == A && x.RoleId == 777);
            role.Enable = !role.Enable; await writer.SaveChangesAsync();
        }
        await context.Run(A, async ct =>
        {
            Assert.Equal("C02_SNAPSHOT_BOUNDARY_CHANGED", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(A, cursor, 1, ct))).Message);
            Assert.NotEmpty((await reader.ReadVersionsAsync(A, null, cancellationToken: ct)).Items);
        });
        Assert.Equal(0, manager.Snapshot().Borrowers);
    }

    [CurrentPermissionPostgresFact]
    public async Task Revoked_read_permission_has_no_Owner_fallback_and_can_recover_after_restore()
    {
        var context = new TenantStorageContext(); await using var manager = Manager(context); var reader = Reader(manager);
        await using var owner = await postgres.OpenOwner();
        await IdentityDirectoryPostgresFixture.Sql(owner, "REVOKE SELECT ON crm_identity.\"Snapshot\" FROM directory_a");
        try
        {
            await context.Run(A, async ct =>
            {
                await Unavailable(() => reader.ReadVersionsAsync(A, null, cancellationToken: ct));
                await Unavailable(() => reader.ReadSnapshotAsync(A, $"tenant:{A:D}", ct));
            });
        }
        finally { await IdentityDirectoryPostgresFixture.Sql(owner, "GRANT SELECT ON crm_identity.\"Snapshot\" TO directory_a"); }
        await context.Run(A, async ct => Assert.NotEmpty((await reader.ReadVersionsAsync(A, null, cancellationToken: ct)).Items));
        Assert.Equal(0, manager.Snapshot().Borrowers);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceling_a_blocked_read_releases_the_pool_and_transaction(bool versions)
    {
        var context = new TenantStorageContext(); await using var manager = Manager(context); var reader = Reader(manager);
        await using var owner = await postgres.OpenOwner(); await using var held = await owner.BeginTransactionAsync();
        await IdentityDirectoryPostgresFixture.Sql(owner, "LOCK TABLE crm_identity.\"Snapshot\" IN ACCESS EXCLUSIVE MODE");
        using var cancel = new CancellationTokenSource();
        var pending = context.Run(A, async _ =>
        {
            if (versions) await reader.ReadVersionsAsync(A, null, cancellationToken: cancel.Token);
            else await reader.ReadSnapshotAsync(A, $"tenant:{A:D}", cancel.Token);
        });
        try
        {
            await WaitForLock(owner, "relation"); cancel.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(cancel.Token, error.CancellationToken);
        }
        finally { cancel.Cancel(); await held.RollbackAsync(); }
        Assert.Equal(0, manager.Snapshot().Borrowers);
        await context.Run(A, async ct => Assert.NotNull(await reader.ReadSnapshotAsync(A, $"tenant:{A:D}", ct)));
    }

    [CurrentPermissionPostgresFact]
    public async Task Catalog_rotation_during_a_read_discards_the_result_and_drains_the_old_pool()
    {
        var context = new TenantStorageContext(); await using var manager = Manager(context); var reader = Reader(manager);
        await using var owner = await postgres.OpenOwner(); await using var held = await owner.BeginTransactionAsync();
        await IdentityDirectoryPostgresFixture.Sql(owner, "LOCK TABLE crm_identity.\"Snapshot\" IN ACCESS EXCLUSIVE MODE");
        var pending = context.Run(A, async ct => await reader.ReadVersionsAsync(A, null, cancellationToken: ct));
        try { await WaitForLock(owner, "relation"); manager.ReplaceCatalog(Catalog(revision: "r2")); }
        finally { await held.RollbackAsync(); }
        await Unavailable(() => pending); Assert.Equal(0, manager.Snapshot().Borrowers); Assert.Equal(0, manager.Snapshot().DrainingPools);
        await context.Run(A, async ct => Assert.NotEmpty((await reader.ReadVersionsAsync(A, null, cancellationToken: ct)).Items));
    }

    [CurrentPermissionPostgresFact]
    public async Task A_reader_waiting_for_mapping_revocation_observes_the_committed_removal()
    {
        var context = new TenantStorageContext(); await using var manager = Manager(context); var reader = Reader(manager);
        await using var owner = await postgres.OpenOwner(); await using var held = await owner.BeginTransactionAsync();
        await IdentityDirectoryPostgresFixture.Sql(owner, "DELETE FROM cp6_tenancy_v1.login_tenant WHERE role_name='directory_a'");
        var pending = context.Run(A, async ct => await reader.ReadVersionsAsync(A, null, cancellationToken: ct));
        var committed = false;
        try
        {
            await WaitForLock(owner, "advisory"); await held.CommitAsync(); committed = true;
            await Unavailable(() => pending);
        }
        finally
        {
            if (committed) await IdentityDirectoryPostgresFixture.Sql(owner,
                $"INSERT INTO cp6_tenancy_v1.login_tenant SELECT oid,rolname,'{A:D}'::uuid FROM pg_roles WHERE rolname='directory_a'");
            else await held.RollbackAsync();
        }
        Assert.Equal(0, manager.Snapshot().Borrowers);
        await context.Run(A, async ct => Assert.NotEmpty((await reader.ReadVersionsAsync(A, null, cancellationToken: ct)).Items));
    }

    private static async Task WaitForLock(NpgsqlConnection observer, string waitEvent)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            // The observer may own the blocking transaction; refresh its cached statistics.
            await IdentityDirectoryPostgresFixture.Sql(observer, "SELECT pg_stat_clear_snapshot()");
            await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE usename='directory_a' AND wait_event_type='Lock' AND wait_event=@event)", observer);
            command.Parameters.AddWithValue("event", waitEvent);
            if (await command.ExecuteScalarAsync(deadline.Token) is true) return;
            await Task.Delay(25, deadline.Token);
        }
    }

    private static async Task Unavailable(Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<IdentityReadException>(action);
        Assert.Equal("C02_IDENTITY_STORE_UNAVAILABLE", error.Message); Assert.Null(error.InnerException);
    }
}
