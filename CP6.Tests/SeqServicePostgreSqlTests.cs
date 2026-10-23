using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Pub;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests;

[Collection("TenantManagementPostgres")]
public sealed class SeqServicePostgreSqlTests(TenantManagementPostgresFixture server)
{
    private static readonly DateTime Day = new(2026, 10, 23, 12, 0, 0);
    private CP6Context Context(Guid tenant) => server.Context(new TenantContext { CurrentTenantId = tenant });

    private async Task<Guid> Seed(string key = "PO", int value = 0, int reset = 0, string? period = "", string? format = null)
    {
        var tenant = Guid.NewGuid();
        await using var db = Context(tenant);
        db.Pub_DocSequences.Add(new() { BizKey = key, Prefix = key, SeqLength = 4,
            CurrentValue = value, ResetCycle = reset, CurrentPeriod = period, DateFormat = format });
        await db.SaveChangesAsync();
        return tenant;
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_preloaded_contexts_cannot_allocate_the_same_number()
    {
        var tenant = await Seed();
        await using var first = Context(tenant); await using var second = Context(tenant);
        await first.Pub_DocSequences.SingleAsync(); await second.Pub_DocSequences.SingleAsync();
        Assert.Equal("PO0001", await new SeqService(first).NextAsync("PO", Day));
        Assert.Equal("PO0002", await new SeqService(second).NextAsync("PO", Day));
        await using var verify = Context(tenant);
        Assert.Equal(2, (await verify.Pub_DocSequences.SingleAsync()).CurrentValue);
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_parallel_instances_serialize_the_original_collated_rule()
    {
        var tenant = await Seed(value: 9999);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            await using var db = Context(tenant);
            return await new SeqService(db).NextAsync(i % 2 == 0 ? "PO" : "po ", Day);
        }));
        Assert.Equal(Enumerable.Range(10000, 12).Select(x => "PO" + x), results.Order());
        await using var verify = Context(tenant);
        Assert.Equal(10011, (await verify.Pub_DocSequences.SingleAsync()).CurrentValue);
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_tenant_rule_ignores_a_foreign_tracked_rule_and_missing_key_never_allocates()
    {
        var tenant = await Seed(); var other = await Seed();
        await using var db = Context(tenant);
        var foreign = await db.Pub_DocSequences.IgnoreQueryFilters().SingleAsync(s => s.TenantId == other);
        Assert.Equal("PO0001", await new SeqService(db).NextAsync("PO", Day));
        Assert.Equal(0, foreign.CurrentValue);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SeqService(db).NextAsync("MISSING", Day));
        Assert.Equal("E-PUB-051", error.Message);
        await using var theirs = Context(other);
        Assert.Equal(0, (await theirs.Pub_DocSequences.SingleAsync()).CurrentValue);
        Assert.Equal("PO0001", await new SeqService(theirs).NextAsync("PO", Day));
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_periods_and_current_rule_replace_stale_tracked_configuration()
    {
        foreach (var (cycle, previous, expected) in new[] { (0, "", 8), (1, "20261022", 1),
            (2, "202609", 1), (3, "2025", 1), (4, "", 8) })
        {
            var tenant = await Seed(value: 7, reset: cycle, period: previous, format: "yyyyMMdd");
            await using var db = Context(tenant); var stale = await db.Pub_DocSequences.SingleAsync();
            await using (var edit = Context(tenant))
                await edit.Pub_DocSequences.ExecuteUpdateAsync(s => s.SetProperty(x => x.Prefix, "UPDATED"));
            var service = new SeqService(db);
            Assert.Equal("UPDATED20261023" + expected.ToString("D4"), await service.NextAsync("PO", Day));
            Assert.Equal(expected, stale.CurrentValue); Assert.Equal("UPDATED", stale.Prefix);
            stale.Modifier = "caller-after-allocation"; await db.SaveChangesAsync();
            await using var verify = Context(tenant);
            Assert.Equal(expected, (await verify.Pub_DocSequences.SingleAsync()).CurrentValue);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_caller_commit_and_rollback_keep_allocation_in_the_original_transaction()
    {
        var tenant = await Seed(); await using var db = Context(tenant);
        var tracked = await db.Pub_DocSequences.SingleAsync();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal("PO0001", await new SeqService(db).NextAsync("PO", Day));
            await using var observer = Context(tenant);
            Assert.Equal(0, (await observer.Pub_DocSequences.SingleAsync()).CurrentValue);
            await tx.CommitAsync();
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal("PO0002", await new SeqService(db).NextAsync("PO", Day));
            await tx.RollbackAsync();
        }
        // The tracked instance still contains the rolled-back provisional value; allocation rereads storage.
        Assert.Equal(2, tracked.CurrentValue);
        Assert.Equal("PO0002", await new SeqService(db).NextAsync("PO", Day));
        await using var verify = Context(tenant);
        Assert.Equal(2, (await verify.Pub_DocSequences.SingleAsync()).CurrentValue);
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_allocation_does_not_flush_unrelated_pending_business_or_dirty_rule()
    {
        var tenant = await Seed(); await using var db = Context(tenant);
        var row = await db.Pub_DocSequences.SingleAsync();
        db.Pub_DocSequences.Add(new() { BizKey = "UNSAVED", Prefix = "PENDING" });
        Assert.Equal("PO0001", await new SeqService(db).NextAsync("PO", Day));
        await using (var observer = Context(tenant)) Assert.Equal(1, await observer.Pub_DocSequences.CountAsync());
        row.CurrentValue = 500;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SeqService(db).NextAsync("PO", Day));
        Assert.Equal("SEQUENCE_STATE_DIRTY", error.Message); Assert.Equal(500, row.CurrentValue);
        db.Entry(row).State = EntityState.Deleted;
        error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SeqService(db).NextAsync("PO", Day));
        Assert.Equal("SEQUENCE_STATE_DIRTY", error.Message);
        await using var verify = Context(tenant);
        Assert.Equal(1, (await verify.Pub_DocSequences.SingleAsync()).CurrentValue);
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_invalid_format_and_exhaustion_leave_counters_unchanged_and_minimum_width_does_not_cap_numbers()
    {
        foreach (var (value, format, code) in new[] { (0, "%", "RULE_INVALID"),
            (int.MaxValue, "", "SEQUENCE_EXHAUSTED"), (-1, "", "RULE_INVALID") })
        {
            var tenant = await Seed(value: value, format: format); await using var db = Context(tenant);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new SeqService(db).NextAsync("PO", Day));
            Assert.Equal(code, error.Message);
            Assert.Equal(value, (await db.Pub_DocSequences.SingleAsync()).CurrentValue);
        }
        var resetTenant = await Seed(value: int.MaxValue, reset: 1, period: "20261022");
        await using var reset = Context(resetTenant);
        Assert.Equal("PO0001", await new SeqService(reset).NextAsync("PO", Day));
        var maxTenant = await Seed(value: int.MaxValue - 1); await using var maximum = Context(maxTenant);
        Assert.Equal("PO2147483647", await new SeqService(maximum).NextAsync("PO", Day));
    }

    [CurrentPermissionPostgresFact]
    public async Task Native_database_failure_restores_savepoint_and_preserves_prior_caller_work()
    {
        var tenant = await Seed(); await using var db = Context(tenant);
        var id = (await db.Pub_DocSequences.SingleAsync()).Id;
        var constraint = "w65_failure_" + Guid.NewGuid().ToString("N");
        // Fixed test-only identifier and server-generated UUID; removed in finally. No existing environment is touched.
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Pub_DocSequence\" ADD CONSTRAINT {constraint} CHECK (\"Id\" <> '{id:D}'::uuid OR \"CurrentValue\" = 0)");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            db.Pub_DocSequences.Add(new() { BizKey = "PRIOR", Prefix = "P" }); await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => new SeqService(db).NextAsync("PO", Day));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal(2, await db.Pub_DocSequences.CountAsync());
            Assert.Equal(0, (await db.Pub_DocSequences.AsNoTracking().SingleAsync(s => s.BizKey == "PO")).CurrentValue);
            await tx.CommitAsync();
        }
        finally { await db.Database.ExecuteSqlRawAsync($"ALTER TABLE public.\"Pub_DocSequence\" DROP CONSTRAINT {constraint}"); }
        Assert.Equal("PO0001", await new SeqService(db).NextAsync("PO", Day));
        await using var verify = Context(tenant);
        Assert.Equal(2, await verify.Pub_DocSequences.CountAsync());
    }
}
