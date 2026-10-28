using System.Data;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Core.Services.Wms.CapacityOccupancyStoreV1;
using CapacityRange = CP6.Core.Services.Wms.CapacityOccupancyStoreV1.Range;
using CapacitySpace = CP6.Core.Services.Wms.CapacityOccupancyStoreV1.Space;

namespace CP6.Tests.Erp;

[Collection("TenantManagementPostgres")]
public sealed class CapacityOccupancyPostgreSqlTests(TenantManagementPostgresFixture server)
{
    private static readonly CapacitySpace Simulation = new(Guid.NewGuid(), "SIMULATION", Guid.NewGuid());
    private static Evidence E(string owner = "TEST_OWNER") => new(owner, Guid.NewGuid(), "1", new string('a', 64));
    private static Domain D() => new(Guid.NewGuid(), Guid.NewGuid(), new("LOAD", "1"), new("KG", "1"), new("EXACT8", "1"));
    private static Operation Op(string owner = "SOURCE") => new(owner, Guid.NewGuid().ToString("N"));
    private static Share S(string kind, string quantity, string from = "0.00000000", string? to = null) => new(
        new("OWNER", Guid.NewGuid().ToString("N"), "share"), kind, E(), new("EA", "1"), new(from, to ?? quantity), quantity, E("CONVERSION"), E("DELEGATION"));
    private static async Task<Condition> C(CapacityOccupancyStoreV1 store, Domain domain, string? limit = "110.00000000") => new((await store.Read(domain)).Revision, limit, E("CURRENT_RULE"));
    private static async Task F(string code, Func<Task> action) => Assert.Equal(code, (await Assert.ThrowsAsync<InvalidOperationException>(action)).Message);

    [CurrentPermissionPostgresFact]
    public async Task Capacity_realization_consumes_exact_obligation_once_and_aliases_never_add_quantity()
    {
        var tenant = Guid.NewGuid(); var d = D(); var physical = S("PHYSICAL", "60.00000000");
        var obligation = S("UNRECEIVED", "40.00000000", "1000.00000000", "1040.00000000");
        var resultRoot = S("PHYSICAL", "10.00000000"); var range = new CapacityRange("1000.00000000", "1010.00000000");
        var operation = Op(); var relation = E("SOURCE_ASSIGNMENT"); string original;
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, [d]);
            var initial = await store.Read(d); Assert.Equal("RECORDED_FACTS_ONLY", initial.Coverage); Assert.False(initial.AdmissionGrant);
            await store.Register(Op(), d, physical, await C(store, d));
            await store.Register(Op(), d, obligation, await C(store, d));
            foreach (var a in new[] { new Alias("TASK", "task-a", E()), new Alias("IN", "in-a", E()) })
            { await store.BindAlias(Op(), d, obligation.Key, a, await C(store, d)); Assert.Equal(obligation.Key, await store.ResolveAlias(d, a.Owner, a.Key)); }
            Assert.Equal("100.00000000", (await store.Read(d)).Total);
            original = await store.Realize(operation, d, obligation.Key, range, resultRoot, relation, await C(store, d));
            var after = await store.Read(d); Assert.Equal("70.00000000", after.Physical); Assert.Equal("30.00000000", after.Unreceived); Assert.Equal("100.00000000", after.Total);
            await F("CAPACITY_SHARE_RANGE_ALREADY_USED", async () => await store.Realize(Op("IN_ALIAS"), d, obligation.Key, range, S("PHYSICAL", "10.00000000"), E(), await C(store, d)));
            await F("CAPACITY_SHARE_ALREADY_REGISTERED", async () => await store.Register(Op("LATE_TASK"), d, obligation, await C(store, d)));
            await tx.CommitAsync();
        }
        await using var fresh = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var nextTx = await fresh.Database.BeginTransactionAsync();
        var recovery = await Open(fresh, Simulation, [d]);
        Assert.Equal(original, await recovery.Realize(operation, d, obligation.Key, range, resultRoot, relation, new("0", "0.00000000", E())));
        Assert.Equal("100.00000000", (await recovery.Read(d)).Total); await nextTx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Capacity_independent_receipt_cannot_borrow_existing_obligation_and_failed_write_leaves_no_partial_share()
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = Guid.NewGuid() }); await using var tx = await db.Database.BeginTransactionAsync();
        var d = D(); var store = await Open(db, Simulation, [d]);
        await store.Register(Op(), d, S("PHYSICAL", "60.00000000"), await C(store, d));
        await store.Register(Op(), d, S("UNRECEIVED", "40.00000000"), await C(store, d));
        var independent = S("PHYSICAL", "10.00000000"); var op = Op(); var before = await store.Read(d);
        await F("CAPACITY_LIMIT_EXCEEDED", async () => await store.Register(op, d, independent, await C(store, d, "105.00000000")));
        Assert.Equal(before, await store.Read(d));
        await store.Register(op, d, independent, await C(store, d, "110.00000000"));
        Assert.Equal("110.00000000", (await store.Read(d)).Total); Assert.Equal("40.00000000", (await store.Read(d)).Unreceived);
        await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Capacity_exact_conversion_nonzero_coordinates_and_release_preserve_remaining_ranges()
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = Guid.NewGuid() }); await using var tx = await db.Database.BeginTransactionAsync();
        var d = D(); var store = await Open(db, Simulation, [d]);
        // 10 EA in [100,110) corresponds to 5 KG; no EA/KG numeric substitution.
        var obligation = S("UNRECEIVED", "5.00000000", "100.00000000", "110.00000000");
        await store.Register(Op(), d, obligation, await C(store, d));
        var root = S("PHYSICAL", "2.00000000", "50.00000000", "54.00000000");
        await store.Realize(Op(), d, obligation.Key, new("100.00000000", "104.00000000"), root, E(), await C(store, d));
        Assert.Equal("2.00000000", (await store.Read(d)).Physical); Assert.Equal("3.00000000", (await store.Read(d)).Unreceived);
        await store.Release(Op(), d, obligation.Key, new("104.00000000", "106.00000000"), E(), await C(store, d));
        await store.Release(Op(), d, root.Key, new("50.00000000", "52.00000000"), E(), await C(store, d));
        var last = await store.Read(d); Assert.Equal("1.00000000", last.Physical); Assert.Equal("2.00000000", last.Unreceived);
        await F("CAPACITY_SHARE_RANGE_ALREADY_USED", async () => await store.Release(Op(), d, obligation.Key, new("103.00000000", "105.00000000"), E(), await C(store, d)));
        await F("CAPACITY_RANGE_OUTSIDE_SHARE", async () => await store.Release(Op(), d, obligation.Key, new("99.00000000", "100.00000000"), E(), await C(store, d)));
        var third = S("UNRECEIVED", "1.00000000", "0.00000000", "3.00000000");
        await store.Register(Op(), d, third, await C(store, d));
        await F("CAPACITY_CONVERSION_NOT_EXACT", async () => await store.Release(Op(), d, third.Key, new("0.00000000", "1.00000000"), E(), await C(store, d)));
        await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Capacity_original_input_conflict_alias_retarget_and_stale_revision_fail_without_effect()
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = Guid.NewGuid() }); await using var tx = await db.Database.BeginTransactionAsync();
        var d = D(); var store = await Open(db, Simulation, [d]); var op = Op(); var share = S("UNRECEIVED", "10.00000000"); var stale = await C(store, d);
        var original = await store.Register(op, d, share, stale);
        Assert.Equal(original, await store.Register(op, d, share, stale));
        await F("CAPACITY_OPERATION_INPUT_CONFLICT", () => store.Register(op, d, share with { CapacityQuantity = "11.00000000" }, stale));
        await F("CAPACITY_REVISION_CONFLICT", () => store.Register(Op(), d, S("PHYSICAL", "1.00000000"), stale));
        var a = new Alias("TASK", "stable", E()); var aliasOp = Op(); var condition = await C(store, d);
        var aliasOriginal = await store.BindAlias(aliasOp, d, share.Key, a, condition);
        Assert.Equal(aliasOriginal, await store.BindAlias(aliasOp, d, share.Key, a, condition));
        var other = S("UNRECEIVED", "10.00000000"); await store.Register(Op(), d, other, await C(store, d));
        await F("CAPACITY_ALIAS_ALREADY_BOUND", async () => await store.BindAlias(Op(), d, other.Key, a, await C(store, d)));
        Assert.Equal(share.Key, await store.ResolveAlias(d, a.Owner, a.Key)); Assert.Equal("20.00000000", (await store.Read(d)).Total); await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Capacity_maximum_decimal_and_exact_large_products_are_lossless_and_overflow_rolls_back()
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = Guid.NewGuid() }); await using var tx = await db.Database.BeginTransactionAsync();
        var d = D(); var store = await Open(db, Simulation, [d]); var max = "9999999999999.99999999"; var share = S("UNRECEIVED", max);
        foreach (var invalid in new[] { "1", "01.00000000", "1e0", "-0.00000000", "1.000000001", "10000000000000.00000000" })
            await F("CAPACITY_QUANTITY_INVALID", async () => await store.Register(Op(), d, share with { CapacityQuantity = invalid }, await C(store, d, null)));
        await store.Register(Op(), d, share, await C(store, d, null));
        await F("CAPACITY_QUANTITY_OVERFLOW", async () => await store.Register(Op(), d, S("PHYSICAL", "0.00000001"), await C(store, d, null)));
        var root = S("PHYSICAL", max);
        var receipt = await store.Realize(Op(), d, share.Key, share.Range, root, E(), await C(store, d, null));
        Assert.Contains(max, receipt); Assert.Equal(max, (await store.Read(d)).Physical); Assert.Equal("0.00000000", (await store.Read(d)).Unreceived);
        await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Capacity_requires_current_transaction_complete_lock_plan_and_immutable_domain_identity()
    {
        var tenant = Guid.NewGuid(); var d = D();
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await F("CAPACITY_OWNER_TRANSACTION_REQUIRED", () => Open(db, Simulation, [d]));
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, [d]);
            await F("CAPACITY_LOCK_PLAN_ALREADY_OPEN", () => Open(db, Simulation, [d]));
            await F("CAPACITY_DOMAIN_NOT_DECLARED", () => store.Read(D()));
            await F("CAPACITY_DOMAIN_NOT_DECLARED", () => store.Read(d with { Uom = new("EA", "1") }));
            await tx.CommitAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => store.Read(d));
        }
        foreach (var other in new[] { d with { Uom = new("EA", "1") }, d with { Metric = new(d.Metric.Id, "2") }, d with { Precision = new("ROUNDED", "1") } })
        {
            await using var db = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var tx = await db.Database.BeginTransactionAsync();
            await F("CAPACITY_DOMAIN_IDENTITY_CONFLICT", () => Open(db, Simulation, [other]));
        }
        await using var unsupported = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var repeat = await unsupported.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await F("CAPACITY_OWNER_TRANSACTION_REQUIRED", () => Open(unsupported, Simulation, [d]));
    }

    [CurrentPermissionPostgresFact]
    public async Task Capacity_tenant_environment_sandbox_and_production_namespaces_are_independent()
    {
        var tenant = Guid.NewGuid(); var d = D(); var op = Op(); var share = S("UNRECEIVED", "40.00000000");
        foreach (var (t, space) in new[] { (tenant, Simulation), (Guid.NewGuid(), Simulation), (tenant, Simulation with { EnvironmentId = Guid.NewGuid() }),
            (tenant, Simulation with { SandboxScopeId = Guid.NewGuid() }), (tenant, new CapacitySpace(Simulation.EnvironmentId, "PRODUCTION", null)) })
        {
            await using var db = server.Context(new TenantContext { CurrentTenantId = t }); await using var tx = await db.Database.BeginTransactionAsync();
            var store = await Open(db, space, [d]); Assert.Equal("0.00000000", (await store.Read(d)).Total);
            Assert.Null(await store.ResolveAlias(d, "TASK", "same"));
            await store.Register(op, d, share, await C(store, d)); await store.BindAlias(Op(), d, share.Key, new("TASK", "same", E()), await C(store, d)); await tx.CommitAsync();
        }
        await using var invalid = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var invalidTx = await invalid.Database.BeginTransactionAsync();
        await F("CAPACITY_ENVIRONMENT_INVALID", () => Open(invalid, Simulation with { SandboxScopeId = null }, [d]));
    }

    [CurrentPermissionPostgresFact]
    public async Task Capacity_caller_rollback_includes_realization_original_receipt_and_sibling_main_write()
    {
        var tenant = Guid.NewGuid(); var d = D(); var share = S("UNRECEIVED", "40.00000000"); var root = S("PHYSICAL", "10.00000000"); var op = Op(); var relation = E();
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, [d]);
            await store.Register(Op(), d, share, await C(store, d)); await tx.CommitAsync();
        }
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, [d]);
            await store.Realize(op, d, share.Key, new("0.00000000", "10.00000000"), root, relation, await C(store, d));
            db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "same transaction sibling" }); await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }
        await using var fresh = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var next = await fresh.Database.BeginTransactionAsync();
        var recovery = await Open(fresh, Simulation, [d]); Assert.Equal("0.00000000", (await recovery.Read(d)).Physical); Assert.Equal("40.00000000", (await recovery.Read(d)).Unreceived);
        Assert.False(await fresh.Sys_Tenants.AnyAsync(x => x.Id == tenant));
        await recovery.Realize(op, d, share.Key, new("0.00000000", "10.00000000"), root, relation, await C(recovery, d)); await next.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Capacity_different_products_compete_on_the_same_location_metric_domain()
    {
        var tenant = Guid.NewGuid(); var d = D();
        await using (var seed = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var t = await seed.Database.BeginTransactionAsync(); var store = await Open(seed, Simulation, [d]);
            await store.Register(Op(), d, S("PHYSICAL", "90.00000000"), await C(store, d, "100.00000000")); await t.CommitAsync();
        }
        await using var first = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var tx = await first.Database.BeginTransactionAsync();
        var a = await Open(first, Simulation, [d]); await a.Register(Op("PRODUCT_A"), d, S("PHYSICAL", "10.00000000"), await C(a, d, "100.00000000"));
        var backend = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var contender = Task.Run(async () =>
        {
            await using var db = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var t = await db.Database.BeginTransactionAsync();
            backend.SetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            var b = await Open(db, Simulation, [d]);
            await F("CAPACITY_LIMIT_EXCEEDED", async () => await b.Register(Op("PRODUCT_B"), d, S("PHYSICAL", "10.00000000"), await C(b, d, "100.00000000")));
            var result = await b.Read(d); await t.CommitAsync(); return result;
        });
        try
        {
            var pid = await backend.Task.WaitAsync(TimeSpan.FromSeconds(10)); var blocked = false;
            for (var n = 0; n < 100 && !blocked; n++)
            {
                blocked = await first.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_locks WHERE pid={pid} AND locktype='advisory' AND NOT granted) AS \"Value\"").SingleAsync();
                if (!blocked) await Task.Delay(25);
            }
            Assert.True(blocked); await tx.CommitAsync();
            Assert.Equal("100.00000000", (await contender.WaitAsync(TimeSpan.FromSeconds(15))).Total);
        }
        finally { if (!contender.IsCompleted) { await tx.RollbackAsync(); await contender; } }
    }

    [CurrentPermissionPostgresFact]
    public async Task Capacity_database_rejects_original_mutation_and_unbalanced_head_at_commit()
    {
        var tenant = Guid.NewGuid(); var d = D();
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, [d]);
            await store.Register(Op(), d, S("PHYSICAL", "10.00000000"), await C(store, d)); await tx.CommitAsync();
        }
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_capacity_v1.shares SET capacity_quantity=9 WHERE tenant_id={tenant}"));
        }
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_capacity_v1.domains SET revision=revision+1,physical=11 WHERE tenant_id={tenant}");
            Assert.Contains("CAPACITY_BALANCE_INVALID", (await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync())).Message);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Capacity_zero_metric_does_not_prove_empty_and_consumed_physical_range_stays_consumed()
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = Guid.NewGuid() }); await using var tx = await db.Database.BeginTransactionAsync();
        var d = D(); var store = await Open(db, Simulation, [d]);
        var physical = S("PHYSICAL", "0.00000000", "0.00000000", "1.00000000");
        await store.Register(Op(), d, physical, await C(store, d, "0.00000000"));
        var facts = await store.Read(d); Assert.Equal("0.00000000", facts.Total); Assert.Equal("RECORDED_FACTS_ONLY", facts.Coverage); Assert.False(facts.AdmissionGrant);
        await store.Release(Op(), d, physical.Key, physical.Range, E(), await C(store, d, "0.00000000"));
        await F("CAPACITY_SHARE_RANGE_ALREADY_USED", async () => await store.Release(Op(), d, physical.Key, physical.Range, E(), await C(store, d)));
        await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Capacity_realization_failure_after_consumption_rolls_back_before_retry_and_net_growth_checks_limit()
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = Guid.NewGuid() }); await using var tx = await db.Database.BeginTransactionAsync();
        var d = D(); var store = await Open(db, Simulation, [d]);
        var root = S("PHYSICAL", "10.00000000"); var obligation = S("UNRECEIVED", "40.00000000");
        await store.Register(Op(), d, root, await C(store, d)); await store.Register(Op(), d, obligation, await C(store, d));
        var before = await store.Read(d); var range = new CapacityRange("0.00000000", "10.00000000"); var op = Op(); var relation = E();
        // Use is inserted first. Duplicate destination fails second; the savepoint must undo Use.
        await F("CAPACITY_SHARE_ALREADY_REGISTERED", async () => await store.Realize(op, d, obligation.Key, range, root, relation, await C(store, d)));
        Assert.Equal(before, await store.Read(d));
        var newRoot = S("PHYSICAL", "14.00000000", "0.00000000", "10.00000000");
        // Both the use and the newly inserted physical share precede the final capacity check.
        await F("CAPACITY_LIMIT_EXCEEDED", async () => await store.Realize(op, d, obligation.Key, range, newRoot, relation, await C(store, d, "53.00000000")));
        Assert.Equal(before, await store.Read(d));
        await store.Realize(op, d, obligation.Key, range, newRoot, relation, await C(store, d, "54.00000000"));
        var after = await store.Read(d); Assert.Equal("24.00000000", after.Physical); Assert.Equal("30.00000000", after.Unreceived); Assert.Equal("54.00000000", after.Total);
        await tx.CommitAsync();
    }
}
