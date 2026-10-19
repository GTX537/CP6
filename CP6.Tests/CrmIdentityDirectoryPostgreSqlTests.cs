using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.Messaging;
using CP6.Platform.EntityFramework;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Tests;

[Collection("TenantManagementPostgres")]
public sealed class CrmIdentityDirectoryPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Snapshot_readiness_and_payload_observe_the_same_committed_state()
    {
        var id = Guid.NewGuid(); var runtime = Runtime(id);
        await Seed(id, runtime);
        var changed = new BeforeSnapshot(async () =>
        {
            await using var other = Context(id, runtime);
            await using var tx = await other.Database.BeginTransactionAsync();
            (await other.Sys_Tenants.SingleAsync(x => x.Id == id)).Enable = false;
            await other.SaveChangesAsync();
            await other.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"=NULL WHERE \"TenantId\"={id}");
            await tx.CommitAsync();
        });
        await using var db = server.Context(new TenantContext { CurrentTenantId = id }, null, changed);
        var reader = new IdentitySnapshotReader(db, runtime, new EphemeralDataProtectionProvider());
        IdentitySnapshot? result = null;
        var error = await Record.ExceptionAsync(async () => result = await reader.ReadSnapshotAsync(id, $"tenant:{id:D}"));
        Assert.True(changed.Executed);
        // Depending on when the consistent statement/transaction begins, either old ready v1
        // or the new not-ready result is valid. New v2 paired with old readiness is never valid.
        if (error is not null) Assert.Equal("C02_BOOTSTRAP_NOT_READY", Assert.IsType<IdentityReadException>(error).Message);
        else
        {
            Assert.NotNull(result);
            Assert.Equal(1, result.Version);
            Assert.True(result.Data.GetProperty("enabled").GetBoolean());
        }
        Assert.Null(db.Database.CurrentTransaction);
        var unavailable = await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadSnapshotAsync(id, $"tenant:{id:D}"));
        Assert.Equal("C02_BOOTSTRAP_NOT_READY", unavailable.Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Bootstrap_uses_locked_storage_state_instead_of_a_tracked_completed_marker()
    {
        var id = Guid.NewGuid(); var runtime = Runtime(id);
        await Seed(id, runtime);
        await using var stale = Context(id);
        var tracked = await stale.CrmIdentityBootstrapStates.SingleAsync(x => x.TenantId == id);
        Assert.NotNull(tracked.CompletedAtUtc);
        await using (var other = Context(id))
        {
            await using var tx = await other.Database.BeginTransactionAsync();
            await other.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"=NULL, \"ContractBundleSha256\"={new string('0', 64)} WHERE \"TenantId\"={id}");
            await other.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false WHERE \"Id\"={id}");
            await tx.CommitAsync();
        }
        await new IdentityBootstrapService(stale, runtime).InitializeAsync(id);
        await using var observer = Context(id);
        var stored = await observer.CrmIdentityBootstrapStates.SingleAsync(x => x.TenantId == id);
        Assert.Equal(runtime.ContractBundleSha256, stored.ContractBundleSha256);
        Assert.NotNull(stored.CompletedAtUtc);
        var snapshot = await observer.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == id);
        Assert.Equal(2, snapshot.Version);
        Assert.Contains("\"enabled\":false", snapshot.PayloadJson);
        Assert.Equal(stored.RowVersion, tracked.RowVersion);
    }

    [CurrentPermissionPostgresFact]
    public async Task Pagination_is_complete_and_replayable_until_a_committed_generation_changes()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var runtime = Runtime(a, b);
        await Seed(a, runtime); await Seed(b, runtime);
        await using (var seed = Context(a, runtime))
        {
            seed.Sys_Roles.AddRange(new Sys_Role { TenantId = a, RoleId = 701, RoleName = "first", Enable = true },
                new Sys_Role { TenantId = a, RoleId = 702, RoleName = "second", Enable = true });
            await seed.SaveChangesAsync();
        }
        var protection = new EphemeralDataProtectionProvider();
        await using var db = Context(a);
        var reader = new IdentitySnapshotReader(db, runtime, protection);
        var first = await reader.ReadVersionsAsync(a, null, 1);
        Assert.Single(first.Items); Assert.NotNull(first.NextCursor);
        var second = await reader.ReadVersionsAsync(a, first.NextCursor, 1);
        var replay = await reader.ReadVersionsAsync(a, first.NextCursor, 1);
        Assert.Equal(second.Items, replay.Items); Assert.Equal(second.Boundary, replay.Boundary);
        var third = await reader.ReadVersionsAsync(a, second.NextCursor, 1);
        Assert.Null(third.NextCursor); Assert.Equal(first.Boundary, third.Boundary);
        var all = first.Items.Concat(second.Items).Concat(third.Items).ToArray();
        Assert.Equal(3, all.Select(x => x.AggregateId).Distinct().Count());
        Assert.Contains(all, x => x.AggregateId == $"tenant:{a:D}");
        Assert.DoesNotContain(all, x => x.AggregateId == $"tenant:{b:D}");
        foreach (var item in all)
        {
            var snapshot = await reader.ReadSnapshotAsync(a, item.AggregateId);
            Assert.NotNull(snapshot); Assert.Equal(item.Version, snapshot.Version); Assert.Equal(item.PayloadSha256, snapshot.PayloadSha256);
        }

        var changed = new BeforeSnapshot(async () =>
        {
            await using var other = Context(a, runtime);
            (await other.Sys_Roles.SingleAsync(x => x.TenantId == a && x.RoleId == 701)).Enable = false;
            await other.SaveChangesAsync();
        });
        await using var concurrent = server.Context(new TenantContext { CurrentTenantId = a }, null, changed);
        var concurrentReader = new IdentitySnapshotReader(concurrent, runtime, protection);
        var oldPage = await concurrentReader.ReadVersionsAsync(a, null, 1);
        Assert.True(changed.Executed); Assert.Equal(first.Boundary, oldPage.Boundary); Assert.Equal(first.Items, oldPage.Items);
        var stale = await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(a, oldPage.NextCursor, 1));
        Assert.Equal("C02_SNAPSHOT_BOUNDARY_CHANGED", stale.Message);
        Assert.Null(db.Database.CurrentTransaction);
        var restart = await reader.ReadVersionsAsync(a, null);
        Assert.NotEqual(first.Boundary, restart.Boundary); Assert.Equal(3, restart.Items.Length); Assert.Null(restart.NextCursor);
        Assert.Equal(2, restart.Items.Single(x => x.AggregateId == "permission:role:701").Version);
    }

    [CurrentPermissionPostgresFact]
    public async Task Reader_distinguishes_not_ready_missing_and_empty_without_cross_tenant_fallback()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var unknown = Guid.NewGuid(); var runtime = Runtime(a, b);
        await Seed(a, runtime);
        await using var db = Context(a);
        var reader = new IdentitySnapshotReader(db, runtime, new EphemeralDataProtectionProvider());
        Assert.Null(await reader.ReadSnapshotAsync(a, $"tenant:{b:D}"));
        foreach (var tenant in new[] { b, unknown })
        {
            Assert.Equal("C02_BOOTSTRAP_NOT_READY", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(tenant, null))).Message);
            Assert.Equal("C02_BOOTSTRAP_NOT_READY", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadSnapshotAsync(tenant, $"tenant:{a:D}"))).Message);
        }
        db.CrmIdentityBootstrapStates.Add(new CrmIdentityBootstrapState { TenantId = b, CompletedAtUtc = DateTimeOffset.UtcNow, ContractBundleSha256 = runtime.ContractBundleSha256 });
        await db.SaveChangesAsync();
        var empty = await reader.ReadVersionsAsync(b, null);
        Assert.Empty(empty.Items); Assert.Equal("0", empty.Boundary); Assert.Null(empty.NextCursor);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"ContractBundleSha256\"={new string('0', 64)} WHERE \"TenantId\"={a}");
        Assert.Equal("C02_BOOTSTRAP_NOT_READY", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadSnapshotAsync(a, $"tenant:{a:D}"))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Cursor_is_tenant_bound_expiring_and_rejects_invalid_input()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var clock = new DirectoryClock(); var runtime = RuntimeWithClock(clock, a, b);
        await Seed(a, runtime); await Seed(b, runtime);
        await using var db = Context(a, runtime);
        db.Sys_Roles.Add(new Sys_Role { TenantId = a, RoleId = 711, RoleName = "cursor", Enable = true });
        await db.SaveChangesAsync();
        var reader = new IdentitySnapshotReader(db, runtime, new EphemeralDataProtectionProvider());
        var page = await reader.ReadVersionsAsync(a, null, 1); Assert.NotNull(page.NextCursor);
        Assert.Equal("C02_INVALID_CURSOR", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(b, page.NextCursor))).Message);
        foreach (var cursor in new[] { "not-a-protected-cursor", new string('x', 2049) })
            Assert.Equal("C02_INVALID_CURSOR", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(a, cursor))).Message);
        clock.Now += TimeSpan.FromMinutes(11);
        Assert.Equal("C02_INVALID_CURSOR", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(a, page.NextCursor))).Message);
        foreach (var size in new[] { 0, 201 })
            Assert.Equal("C02_INVALID_PAGE_SIZE", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(a, null, size))).Message);
        foreach (var aggregate in new[] { "", new string('a', 129), "tenant:bad/input" })
            Assert.Equal("C02_INVALID_AGGREGATE", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadSnapshotAsync(a, aggregate))).Message);
        Assert.Null(db.Database.CurrentTransaction);
        Assert.NotEmpty((await reader.ReadVersionsAsync(a, null)).Items);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_wait_cancellation_releases_reader_transaction_and_allows_next_read(bool versions)
    {
        var id = Guid.NewGuid(); var runtime = Runtime(id); await Seed(id, runtime);
        await using var holder = Context(id); await using var held = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync("LOCK TABLE crm_identity.\"Snapshot\" IN ACCESS EXCLUSIVE MODE");
        await using var db = Context(id); var reader = new IdentitySnapshotReader(db, runtime, new EphemeralDataProtectionProvider());
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        if (versions) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadVersionsAsync(id, null, cancellationToken: cancel.Token));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadSnapshotAsync(id, $"tenant:{id:D}", cancel.Token));
        Assert.Null(db.Database.CurrentTransaction);
        await held.RollbackAsync();
        Assert.NotNull(await reader.ReadSnapshotAsync(id, $"tenant:{id:D}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Snapshot_preserves_caller_transaction_and_bootstrap_refuses_to_own_it()
    {
        var id = Guid.NewGuid(); var runtime = Runtime(id); await Seed(id, runtime);
        await using var db = Context(id); var reader = new IdentitySnapshotReader(db, runtime, new EphemeralDataProtectionProvider());
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"=NULL WHERE \"TenantId\"={id}");
            Assert.Equal("C02_BOOTSTRAP_NOT_READY", (await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadSnapshotAsync(id, $"tenant:{id:D}"))).Message);
            Assert.Same(tx, db.Database.CurrentTransaction);
            Assert.Equal("C02_BOOTSTRAP_REQUIRES_OWN_TRANSACTION", (await Assert.ThrowsAsync<InvalidOperationException>(() => new IdentityBootstrapService(db, runtime).InitializeAsync(id))).Message);
            Assert.Same(tx, db.Database.CurrentTransaction);
            await tx.RollbackAsync();
        }
        Assert.NotNull(await reader.ReadSnapshotAsync(id, $"tenant:{id:D}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Bootstrap_preserves_pending_marker_changes_and_recreates_a_deleted_tracked_marker()
    {
        var id = Guid.NewGuid(); var runtime = Runtime(id); await Seed(id, runtime);
        await using var db = Context(id);
        var tracked = await db.CrmIdentityBootstrapStates.SingleAsync(x => x.TenantId == id);
        tracked.CompletedAtUtc = null;
        var conflict = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new IdentityBootstrapService(db, runtime).InitializeAsync(id));
        Assert.Equal("C02_BOOTSTRAP_TRACKING_CONFLICT", conflict.Message); Assert.Null(tracked.CompletedAtUtc);
        Assert.Null(db.Database.CurrentTransaction);
        await db.Entry(tracked).ReloadAsync();
        await using (var other = Context(id))
            await other.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM crm_identity.\"Bootstrap\" WHERE \"TenantId\"={id}");
        await new IdentityBootstrapService(db, runtime).InitializeAsync(id);
        Assert.Equal(EntityState.Detached, db.Entry(tracked).State);
        await using var observer = Context(id);
        Assert.NotNull((await observer.CrmIdentityBootstrapStates.SingleAsync(x => x.TenantId == id)).CompletedAtUtc);
        Assert.Equal(1, (await observer.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == id)).Version);
        Assert.Single(await observer.Set<Cp6OutboxMessage>().Where(x => x.TenantId == id).ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Bootstrap_rolls_back_marker_directory_and_generation_when_outbox_is_rejected()
    {
        var id = Guid.NewGuid(); var runtime = Runtime(id);
        await using var control = Context(id);
        control.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = id.ToString("N"), TenantName = "rollback", Enable = true });
        await control.SaveChangesAsync();
        var constraint = "bootstrap_" + id.ToString("N");
        await control.Database.ExecuteSqlRawAsync($"ALTER TABLE crm_identity.\"Cp6_OutboxMessage\" ADD CONSTRAINT {constraint} CHECK (\"TenantId\" <> '{id:D}'::uuid) NOT VALID");
        try
        {
            await using var failing = Context(id);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => new IdentityBootstrapService(failing, runtime).InitializeAsync(id));
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
            Assert.False(await control.CrmIdentityBootstrapStates.AsNoTracking().AnyAsync(x => x.TenantId == id));
            Assert.False(await control.CrmIdentitySnapshots.AsNoTracking().AnyAsync(x => x.TenantId == id));
            Assert.False(await control.CrmIdentityTenantGenerations.AsNoTracking().AnyAsync(x => x.TenantId == id));
            Assert.False(await control.Set<Cp6OutboxMessage>().AsNoTracking().AnyAsync(x => x.TenantId == id));
        }
        finally { await control.Database.ExecuteSqlRawAsync($"ALTER TABLE crm_identity.\"Cp6_OutboxMessage\" DROP CONSTRAINT {constraint}"); }
        await using var retry = Context(id);
        await new IdentityBootstrapService(retry, runtime).InitializeAsync(id);
        await new IdentityBootstrapService(retry, runtime).InitializeAsync(id);
        Assert.NotNull((await control.CrmIdentityBootstrapStates.AsNoTracking().SingleAsync(x => x.TenantId == id)).CompletedAtUtc);
        Assert.Equal(1, (await control.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == id)).Version);
        Assert.Single(await control.Set<Cp6OutboxMessage>().Where(x => x.TenantId == id).ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Bootstrap_waiters_cancel_or_rebuild_once_from_the_post_wait_state()
    {
        var id = Guid.NewGuid(); var runtime = Runtime(id); await Seed(id, runtime);
        await using var stale = Context(id);
        _ = await stale.CrmIdentityBootstrapStates.SingleAsync(x => x.TenantId == id);
        await using var fresh = Context(id);
        await using var holder = Context(id); await holder.Database.OpenConnectionAsync();
        await using var ownership = await DatabaseResourceLocks.TryAcquireSessionAsync(holder.Database.GetDbConnection(),
            DatabaseProvider.PostgreSql, $"cp6:c02:bootstrap:{id:N}", 0);
        Assert.NotNull(ownership);
        await holder.Database.ExecuteSqlInterpolatedAsync($"UPDATE crm_identity.\"Bootstrap\" SET \"CompletedAtUtc\"=NULL WHERE \"TenantId\"={id}");
        await holder.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Tenants\" SET \"Enable\"=false WHERE \"Id\"={id}");
        await using (var canceled = Context(id))
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new IdentityBootstrapService(canceled, runtime).InitializeAsync(id, cancel.Token));
            Assert.Null(canceled.Database.CurrentTransaction);
            Assert.Equal(System.Data.ConnectionState.Closed, canceled.Database.GetDbConnection().State);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var first = new IdentityBootstrapService(stale, runtime).InitializeAsync(id, timeout.Token);
        var second = new IdentityBootstrapService(fresh, runtime).InitializeAsync(id, timeout.Token);
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => Task.WhenAll(first, second).WaitAsync(TimeSpan.FromMilliseconds(150)));
            Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        }
        finally { await ownership.DisposeAsync(); }
        await Task.WhenAll(first, second);
        Assert.NotNull((await holder.CrmIdentityBootstrapStates.AsNoTracking().SingleAsync(x => x.TenantId == id)).CompletedAtUtc);
        Assert.Equal(2, (await holder.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == id)).Version);
        await using var priority = server.PriorityContext();
        Assert.Single(await priority.Set<Cp6OutboxMessage>().Where(x => x.TenantId == id).ToArrayAsync());
    }

    private CP6Context Context(Guid tenant, CrmIdentityRuntime? runtime = null)
        => server.Context(new TenantContext { CurrentTenantId = tenant }, runtime);

    private async Task Seed(Guid tenant, CrmIdentityRuntime runtime)
    {
        await using var db = Context(tenant);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "directory fixture", Enable = true });
        await db.SaveChangesAsync();
        await new IdentityBootstrapService(db, runtime).InitializeAsync(tenant);
    }

    private static CrmIdentityRuntime Runtime(params Guid[] tenants) => RuntimeWithClock(TimeProvider.System, tenants);

    private static CrmIdentityRuntime RuntimeWithClock(TimeProvider clock, params Guid[] tenants)
    {
        const string issuer = "https://identity-directory.example.test";
        var options = new CrmIdentityOptions { Enabled = true, Issuer = issuer, ProjectionReaderClientIds = ["fixture-reader"] };
        foreach (var id in tenants) options.Tenants.Add(id, "us");
        return new CrmIdentityRuntime(options, new IdentityEventValidator(
            Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform")), issuer), clock);
    }

    private sealed class DirectoryClock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class BeforeSnapshot(Func<Task> change) : DbCommandInterceptor
    {
        internal bool Executed { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Executed && command.CommandText.Contains("crm_identity.\"Snapshot\"", StringComparison.Ordinal))
            {
                Executed = true;
                await change();
            }
            return result;
        }
    }
}
