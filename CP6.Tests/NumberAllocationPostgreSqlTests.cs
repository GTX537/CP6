using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class NumberHttp(Microsoft.AspNetCore.Http.HttpContext context) : Microsoft.AspNetCore.Http.IHttpContextAccessor
    { public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; } = context; }
    private static readonly DateTimeOffset NumberTime = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private sealed record NumberFixture(Harness H, NumberAllocationScopeV1 Scope, NumberAllocationRuleV1 Rule,
        NumberAllocationOwnerV1 Owner) : IAsyncDisposable
    { public ValueTask DisposeAsync() => H.DisposeAsync(); }
    private NumberAllocationOwnerV1 NumberOwner(Harness h, NumberAllocationScopeV1 scope, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        => new(() => server.Context(new TenantContext { CurrentTenantId = h.Tenant }, null, interceptors), scope,
            new(new NumberHttp(h.Http), (_, _) => Task.FromResult(h.User), "pub-seq", "add", "query"));
    private async Task<NumberFixture> NumberSeed(NumberAllocationRuleV1? rule = null, string key = "ALLOC")
    {
        var h = await Seed(); await EngineeringAction(h, "pub-seq", "add"); await EngineeringAction(h, "pub-seq", "query");
        var scope = new NumberAllocationScopeV1(Guid.NewGuid(), h.Tenant, "Main.Component.NumberOwner", key,
            "tenant business scope", "法人/1", "warehouse:A", "NEW_DOMAIN_NO_LEGACY_INTENTS");
        rule ??= new("规则/一", "N", "yyyyMMdd", 4, 1, "UTC", "", 1);
        var owner = NumberOwner(h, scope); await owner.RegisterNewAsync(rule);
        return new(h, scope, rule, owner);
    }
    private static NumberAllocationCommandV1 NumberCommand(string key = "original/业务命令", string version = "规则/一", DateTimeOffset? at = null)
        => new(key, version, at ?? NumberTime, "original business input identity/一");
    [CurrentPermissionPostgresFact]
    public async Task Number_allocation_original_operation_recovery_does_not_consume_a_second_number()
    {
        await using var f = await NumberSeed(); var c = NumberCommand(new string('界', 3000));
        var a = await f.Owner.AllocateAsync(c);
        Assert.Equal(a, await NumberOwner(f.H, f.Scope).AllocateAsync(c));
        Assert.Equal(a, await f.Owner.ReadAsync(c.OperationRef));
        Assert.Equal("N202611020001", a.Number); Assert.Equal("ALLOCATED", a.AllocationState); Assert.Equal("UNKNOWN", a.BusinessState);
        Assert.Equal("NUMBER_COMMAND_CONFLICT", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.AllocateAsync(c with { BusinessInputIdentity = "changed" }))).Message);
        Assert.Equal(1, await f.H.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_number_allocation_v1.receipts WHERE rule_id={f.Scope.RuleId}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Number_allocation_independent_connections_are_unique_and_business_rollback_cannot_recycle()
    {
        await using var f = await NumberSeed();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => NumberOwner(f.H, f.Scope).AllocateAsync(NumberCommand("op/" + i))));
        Assert.Equal(8, results.Select(x => x.Number).Distinct().Count()); Assert.Equal(Enumerable.Range(1, 8), results.Select(x => x.SequenceValue).Order());
        await using (var business = await f.H.Db.Database.BeginTransactionAsync())
        {
            Assert.Equal(9, (await f.Owner.AllocateAsync(NumberCommand("business-fails"))).SequenceValue);
            await business.RollbackAsync();
        }
        Assert.Equal(9, (await f.Owner.AllocateAsync(NumberCommand("business-fails"))).SequenceValue);
        Assert.Equal(10, (await f.Owner.AllocateAsync(NumberCommand("next"))).SequenceValue);
        var same = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => NumberOwner(f.H, f.Scope).AllocateAsync(NumberCommand("same-key"))));
        Assert.Single(same.Distinct()); Assert.Equal(11, same[0].SequenceValue);
    }
    private sealed class NumberLostCommit : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public bool Armed;
        public override Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction, Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData eventData, CancellationToken ct = default)
        { if (Armed) { Armed = false; throw new IOException("owned lost commit response"); } return Task.CompletedTask; }
    }
    [CurrentPermissionPostgresFact]
    public async Task Number_allocation_lost_commit_response_recovers_original_without_second_effect()
    {
        await using var f = await NumberSeed(); var fault = new NumberLostCommit { Armed = true }; var c = NumberCommand();
        Assert.Equal("NUMBER_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => NumberOwner(f.H, f.Scope, fault).AllocateAsync(c))).Message);
        var saved = await f.Owner.ReadAsync(c.OperationRef); Assert.NotNull(saved);
        Assert.Equal(saved, await f.Owner.AllocateAsync(c)); Assert.Equal(2, (await f.Owner.AllocateAsync(NumberCommand("next"))).SequenceValue);
    }
    [CurrentPermissionPostgresFact]
    public async Task Number_allocation_current_permission_and_tenant_owner_control_original_disclosure()
    {
        await using var a = await NumberSeed(); await using var b = await NumberSeed(); var c = NumberCommand();
        var first = await a.Owner.AllocateAsync(c); var second = await b.Owner.AllocateAsync(c);
        Assert.Equal(first.Number, second.Number); Assert.NotEqual(first.TenantId, second.TenantId);
        await a.H.Db.Sys_Roles.ExecuteUpdateAsync(r => r.SetProperty(x => x.Enable, false));
        Assert.Equal(403, (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => a.Owner.ReadAsync(c.OperationRef))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => a.Owner.AllocateAsync(NumberCommand("new")))).Status);
        Assert.Equal(second, await b.Owner.ReadAsync(c.OperationRef));
        Assert.Equal("NUMBER_TENANT_MISMATCH", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => NumberOwner(b.H, a.Scope).ReadAsync(c.OperationRef))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Number_allocation_governed_domain_rejects_old_allocator_rule_edits_and_deletion()
    {
        await using var f = await NumberSeed(key: "编号A"); await f.Owner.AllocateAsync(NumberCommand());
        await using var db = server.Context(new TenantContext { CurrentTenantId = f.H.Tenant });
        Assert.Equal("NUMBER_ALLOCATION_OWNER_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => new SeqService(db).NextAsync("编号a ", NumberTime.DateTime))).Message);
        Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Pub_DocSequences.ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentValue, 0)))).SqlState);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Pub_DocSequences.ExecuteDeleteAsync());
        var preview = await f.Owner.PreviewAsync(NumberTime); Assert.False(preview.IsReservation); Assert.Equal("N202611020002", preview.Sample);
        Assert.Equal(preview, await f.Owner.PreviewAsync(NumberTime)); Assert.Equal(2, (await f.Owner.AllocateAsync(NumberCommand("next"))).SequenceValue);
        db.Pub_DocSequences.Add(new() { BizKey = "LEGACY", Prefix = "L" }); await db.SaveChangesAsync();
        Assert.Equal("L0001", await new SeqService(db).NextAsync("LEGACY", NumberTime.DateTime));
        var old = f.Scope with { RuleId = Guid.NewGuid(), BizKey = "legacy " };
        Assert.Equal("NUMBER_EXISTING_DOMAIN_KEEP_OWNER", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => NumberOwner(f.H, old).RegisterNewAsync(f.Rule))).Message);
        var controller = new CP6.WebApi.Controllers.Pub.SeqController(db);
        Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(await controller.Update(new() { Id = f.Scope.RuleId, Prefix = "changed" }));
        Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(await controller.Delete([f.Scope.RuleId]));
        Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(await controller.Preview("编号a "));
    }
    [CurrentPermissionPostgresFact]
    public async Task Number_allocation_rule_version_period_timezone_and_capacity_are_explicit()
    {
        await using var f = await NumberSeed(new("规则/一", "N", "yyyyMMdd", 4, 1, "America/New_York", "", 1));
        var before = new DateTimeOffset(2026, 11, 2, 4, 59, 59, TimeSpan.Zero);
        Assert.Equal("20261101", (await f.Owner.AllocateAsync(NumberCommand("before", at: before))).PeriodKey);
        var next = await f.Owner.AllocateAsync(NumberCommand("after", at: before.AddSeconds(1))); Assert.Equal("20261102", next.PeriodKey); Assert.Equal(1, next.SequenceValue);
        Assert.Equal(2, (await f.Owner.AllocateAsync(NumberCommand("older-period", at: before))).SequenceValue);
        Assert.Equal("NUMBER_RULE_VERSION_CHANGED", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.AllocateAsync(NumberCommand("bad-version", "旧版")))).Message);
        await using var max = await NumberSeed(new("规则/一", "MAX", "", 4, 0, "UTC", "", int.MaxValue));
        Assert.Equal("MAX2147483647", (await max.Owner.AllocateAsync(NumberCommand())).Number);
        Assert.Equal("SEQUENCE_EXHAUSTED", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => max.Owner.AllocateAsync(NumberCommand("overflow")))).Message);
        var bad = max.Scope with { RuleId = Guid.NewGuid(), BizKey = "BAD" };
        Assert.Equal("RULE_INVALID", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => NumberOwner(max.H, bad).RegisterNewAsync(max.Rule with { DateFormat = "%" }))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Number_allocation_receipt_failure_rolls_back_counter_and_history_collision_is_rejected()
    {
        await using var f = await NumberSeed(new("规则/一", "N", "", 4, 1, "UTC", "", 1));
        var constraint = "number_failure_" + Guid.NewGuid().ToString("N");
        await f.H.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE cp6_number_allocation_v1.receipts ADD CONSTRAINT {constraint} CHECK(rule_id<>'{f.Scope.RuleId:D}'::uuid)");
        try { await Assert.ThrowsAsync<Npgsql.PostgresException>(() => f.Owner.AllocateAsync(NumberCommand())); }
        finally { await f.H.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE cp6_number_allocation_v1.receipts DROP CONSTRAINT {constraint}"); }
        var first = await f.Owner.AllocateAsync(NumberCommand()); Assert.Equal(1, first.SequenceValue);
        Assert.Equal("NUMBER_HISTORY_COLLISION", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.AllocateAsync(NumberCommand("tomorrow", at: NumberTime.AddDays(1))))).Message);
        Assert.Equal(first, await f.Owner.ReadAsync(first.OperationRef));
    }
    [CurrentPermissionPostgresFact]
    public async Task Number_allocation_missing_counter_storage_fails_closed_but_original_remains_readable()
    {
        await using var f = await NumberSeed(); var first = await f.Owner.AllocateAsync(NumberCommand());
        // Owned isolated test schema only. Rename preserves all rows/constraints; restore in finally.
        await f.H.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_number_allocation_v1.periods RENAME TO periods_unavailable");
        try
        {
            Assert.Equal("NUMBER_COUNTER_UNAVAILABLE", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.AllocateAsync(NumberCommand("new")))).Message);
            Assert.Equal(first, await f.Owner.ReadAsync(first.OperationRef));
        }
        finally { await f.H.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_number_allocation_v1.periods_unavailable RENAME TO periods"); }
        Assert.Equal(2, (await f.Owner.AllocateAsync(NumberCommand("new"))).SequenceValue);
    }
    [CurrentPermissionPostgresFact]
    public async Task Number_allocation_final_current_authority_failure_rolls_back_staged_allocation()
    {
        await using var f = await NumberSeed(); var calls = 0;
        var owner = new NumberAllocationOwnerV1(() => server.Context(new TenantContext { CurrentTenantId = f.H.Tenant }), f.Scope,
            new(new NumberHttp(f.H.Http), async (db, ct) =>
            {
                // Change real current IAM facts in the owned transaction immediately before
                // the final permission read, after the allocation graph has been staged.
                if (++calls == 3) await db.Sys_Roles.ExecuteUpdateAsync(r => r.SetProperty(x => x.Enable, false), ct);
                return f.H.User;
            }, "pub-seq", "add", "query"));
        Assert.Equal(403, (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => owner.AllocateAsync(NumberCommand()))).Status);
        Assert.Null(await f.Owner.ReadAsync(NumberCommand().OperationRef));
        Assert.Equal(1, (await f.Owner.AllocateAsync(NumberCommand())).SequenceValue);
        var wrong = f.Scope with { SourceOwner = "another-owner" };
        Assert.Equal("NUMBER_OWNER_SCOPE_MISMATCH", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => NumberOwner(f.H, wrong).ReadAsync(NumberCommand().OperationRef))).Message);
    }
}
