using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Pub;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static NumberRuleChangeCommandV1 RuleChange(string operation = "change/一", string expected = "规则/一",
        string version = "规则/二", string prefix = "M") => new(operation, expected, version, prefix, "yyyyMMdd", 4);

    [CurrentPermissionPostgresFact]
    public async Task Number_rule_two_current_version_changes_have_one_winner_and_preserve_original_allocation()
    {
        await using var f = await NumberSeed(); await EngineeringAction(f.H, "pub-seq", "edit");
        var originalCommand = NumberCommand(); var original = await f.Owner.AllocateAsync(originalCommand);
        async Task<object> Change(string key, string version)
        {
            try { return await NumberOwner(f.H, f.Scope).ChangeRuleAsync(RuleChange(key, version: version)); }
            catch (NumberAllocationExceptionV1 e) { return e; }
        }
        var outcomes = await Task.WhenAll(Change("a", "v2a"), Change("b", "v2b"));
        var winner = Assert.Single(outcomes.OfType<NumberRuleChangeReceiptV1>());
        Assert.Equal(412, Assert.Single(outcomes.OfType<NumberAllocationExceptionV1>()).Status);
        Assert.Equal(original, await f.Owner.AllocateAsync(originalCommand));
        Assert.Equal(original, await f.Owner.ReadAsync(originalCommand.OperationRef));
        var next = await f.Owner.AllocateAsync(NumberCommand("next", winner.Rule.Version));
        Assert.Equal(2, next.SequenceValue); Assert.Equal("M202611020002", next.Number);
        Assert.Equal("N202611020001", original.Number);
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_rule_original_change_and_named_versions_survive_later_changes_without_rebinding()
    {
        await using var f = await NumberSeed(); await EngineeringAction(f.H, "pub-seq", "edit");
        var c = RuleChange(new string('界', 3000), version: new string('版', 3000) + "\0/二");
        var competingOriginals = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => NumberOwner(f.H, f.Scope).ChangeRuleAsync(c)));
        var first = Assert.Single(competingOriginals.Distinct());
        var second = await f.Owner.ChangeRuleAsync(RuleChange("next", c.NewVersion, "v3", "Z"));
        Assert.Equal(first, await NumberOwner(f.H, f.Scope).ChangeRuleAsync(c));
        Assert.Equal(first, await f.Owner.ReadRuleChangeAsync(c.OperationRef));
        Assert.Equal(f.Rule, await f.Owner.ReadRuleAsync(f.Rule.Version));
        Assert.Equal(first.Rule, await f.Owner.ReadRuleAsync(c.NewVersion));
        Assert.Equal(second.Rule, await f.Owner.ReadRuleAsync());
        Assert.Null(await f.Owner.ReadRuleAsync("unknown"));
        Assert.Equal("NUMBER_COMMAND_CONFLICT", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.ChangeRuleAsync(c with { Prefix = "changed" }))).Message);
        Assert.Equal("NUMBER_RULE_VERSION_REUSED", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.ChangeRuleAsync(RuleChange("reuse", "v3", f.Rule.Version)))).Message);
        Assert.Equal("NUMBER_RULE_VERSION_REUSED", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.ChangeRuleAsync(RuleChange("reuse2", "v3", c.NewVersion)))).Message);
        Assert.True(await f.Owner.RegisterNewAsync(f.Rule)); // Original registration remains immutable/idempotent.
        Assert.Equal(second.Rule, await f.Owner.ReadRuleAsync());
        Assert.Equal(1, (await f.Owner.AllocateAsync(NumberCommand("first", "v3"))).SequenceValue);
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_rule_current_edit_read_tenant_owner_and_original_actor_are_enforced()
    {
        await using var f = await NumberSeed(); var c = RuleChange();
        Assert.Equal(403, (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.ChangeRuleAsync(c))).Status);
        await EngineeringAction(f.H, "pub-seq", "edit"); var receipt = await f.Owner.ChangeRuleAsync(c);
        await f.H.Db.Sys_RoleActions.Where(x => x.ActionCode == "edit").ExecuteDeleteAsync();
        Assert.Equal(receipt, await f.Owner.ChangeRuleAsync(c)); // Recovery uses current read, not the former edit grant.
        Assert.Equal(403, (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.ChangeRuleAsync(RuleChange("new", "规则/二", "v3")))).Status);
        var other = new CP6.Entity.DomainModels.Sys.Sys_User { UserName = "other-number-reader", RoleId = 72 };
        f.H.Db.Sys_Users.Add(other); await f.H.Db.SaveChangesAsync(); f.H.Db.ChangeTracker.Clear();
        var otherHarness = f.H with { User = other.Id, Http = new() { User = CurrentPermissionContextTests.Principal(other.Id, f.H.Tenant) } };
        Assert.Equal("NUMBER_ORIGINAL_FORBIDDEN", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => NumberOwner(otherHarness, f.Scope).ReadRuleChangeAsync(c.OperationRef))).Message);
        Assert.Equal("NUMBER_OWNER_SCOPE_MISMATCH", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => NumberOwner(f.H, f.Scope with { SourceOwner = "other" }).ReadRuleAsync())).Message);
        await using var b = await NumberSeed();
        Assert.Equal("NUMBER_TENANT_MISMATCH", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => NumberOwner(b.H, f.Scope).ReadRuleChangeAsync(c.OperationRef))).Message);
        await f.H.Db.Sys_RoleActions.Where(x => x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.ReadRuleChangeAsync(c.OperationRef))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.ReadRuleAsync())).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_rule_final_permission_failure_rolls_back_rule_and_original_command()
    {
        await using var f = await NumberSeed(); await EngineeringAction(f.H, "pub-seq", "edit"); var calls = 0;
        var owner = new NumberAllocationOwnerV1(() => server.Context(new TenantContext { CurrentTenantId = f.H.Tenant }), f.Scope,
            new(new NumberHttp(f.H.Http), async (db, ct) =>
            {
                if (++calls == 3) await db.Sys_RoleActions.Where(x => x.ActionCode == "edit").ExecuteDeleteAsync(ct);
                return f.H.User;
            }, "pub-seq", "add", "query"));
        Assert.Equal(403, (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => owner.ChangeRuleAsync(RuleChange()))).Status);
        Assert.Null(await f.Owner.ReadRuleChangeAsync(RuleChange().OperationRef));
        Assert.Equal(f.Rule, await f.Owner.ReadRuleAsync());
        Assert.Equal("规则/二", (await f.Owner.ChangeRuleAsync(RuleChange())).Rule.Version);
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_rule_failed_journal_rolls_back_and_lost_commit_recovers_original_once()
    {
        await using var f = await NumberSeed(); await EngineeringAction(f.H, "pub-seq", "edit"); var c = RuleChange();
        var constraint = "number_rule_failure_" + Guid.NewGuid().ToString("N");
        // Owned GUID-only test DDL; restore the constraint in finally without editing stored history.
        await f.H.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE cp6_number_allocation_v1.rule_changes ADD CONSTRAINT {constraint} CHECK(rule_id<>'{f.Scope.RuleId:D}'::uuid)");
        try { await Assert.ThrowsAsync<PostgresException>(() => f.Owner.ChangeRuleAsync(c)); }
        finally { await f.H.Db.Database.ExecuteSqlRawAsync($"ALTER TABLE cp6_number_allocation_v1.rule_changes DROP CONSTRAINT {constraint}"); }
        Assert.Equal(f.Rule, await f.Owner.ReadRuleAsync()); Assert.Null(await f.Owner.ReadRuleChangeAsync(c.OperationRef));
        var fault = new NumberLostCommit { Armed = true };
        Assert.Equal("NUMBER_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => NumberOwner(f.H, f.Scope, fault).ChangeRuleAsync(c))).Message);
        var original = await f.Owner.ReadRuleChangeAsync(c.OperationRef); Assert.NotNull(original);
        Assert.Equal(original, await f.Owner.ChangeRuleAsync(c));
        Assert.Equal(1, await f.H.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_number_allocation_v1.rule_changes WHERE rule_id={f.Scope.RuleId}").SingleAsync());
        Assert.Equal(1, (await f.Owner.AllocateAsync(NumberCommand("first", c.NewVersion))).SequenceValue);
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_rule_allocation_race_uses_one_locked_version_and_preview_never_reserves()
    {
        await using var f = await NumberSeed(); await EngineeringAction(f.H, "pub-seq", "edit"); var c = NumberCommand();
        async Task<object> Allocate()
        {
            try { return await NumberOwner(f.H, f.Scope).AllocateAsync(c); }
            catch (NumberAllocationExceptionV1 e) { return e; }
        }
        var allocation = Allocate(); var change = f.Owner.ChangeRuleAsync(RuleChange());
        await Task.WhenAll(allocation, change); var first = await allocation;
        if (first is NumberAllocationReceiptV1 a)
        { Assert.Equal("N202611020001", a.Number); Assert.Equal(a, await f.Owner.AllocateAsync(c)); }
        else Assert.Equal(412, Assert.IsType<NumberAllocationExceptionV1>(first).Status);
        var preview = await f.Owner.PreviewAsync(NumberTime);
        Assert.Equal("规则/二", preview.RuleVersion); Assert.False(preview.IsReservation);
        Assert.Equal(preview, await f.Owner.PreviewAsync(NumberTime));
        var next = await f.Owner.AllocateAsync(NumberCommand("next", "规则/二"));
        Assert.Equal(preview.Sample, next.Number); Assert.Equal(first is NumberAllocationReceiptV1 ? 2 : 1, next.SequenceValue);
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_rule_new_format_collision_is_rejected_without_resetting_old_periods()
    {
        await using var f = await NumberSeed(); await EngineeringAction(f.H, "pub-seq", "edit");
        var c = NumberCommand(); var original = await f.Owner.AllocateAsync(c);
        await f.Owner.ChangeRuleAsync(RuleChange() with { Prefix = "N20261102", DateFormat = "" });
        Assert.Equal("NUMBER_HISTORY_COLLISION", (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.AllocateAsync(NumberCommand("tomorrow", "规则/二", NumberTime.AddDays(1))))).Message);
        Assert.Equal(original, await f.Owner.AllocateAsync(c));
        await f.Owner.ChangeRuleAsync(RuleChange("safe-format", "规则/二", "v3", "Z") with { DateFormat = "" });
        Assert.Equal(1, (await f.Owner.AllocateAsync(NumberCommand("tomorrow", "v3", NumberTime.AddDays(1)))).SequenceValue);
        Assert.Equal(2, (await f.Owner.AllocateAsync(NumberCommand("old-period", "v3"))).SequenceValue);
        Assert.Equal(422, (await Assert.ThrowsAsync<NumberAllocationExceptionV1>(() => f.Owner.ChangeRuleAsync(RuleChange("invalid", "v3", "v4") with { DateFormat = "%" }))).Status);
        Assert.Equal("v3", (await f.Owner.ReadRuleAsync())!.Version);
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_rule_history_is_immutable_and_pre_version_writer_cannot_allocate_after_change()
    {
        await using var f = await NumberSeed(); await EngineeringAction(f.H, "pub-seq", "edit");
        var original = await f.Owner.AllocateAsync(NumberCommand()); await f.Owner.ChangeRuleAsync(RuleChange());
        await Assert.ThrowsAsync<PostgresException>(() => f.H.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_number_allocation_v1.rule_versions WHERE rule_id={f.Scope.RuleId}"));
        await Assert.ThrowsAsync<PostgresException>(() => f.H.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_number_allocation_v1.rule_changes SET actor_id={Guid.NewGuid()} WHERE rule_id={f.Scope.RuleId}"));
        await using (var tx = await f.H.Db.Database.BeginTransactionAsync())
        {
            // A new version cannot commit without its original change result in the same unit.
            await f.H.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_number_allocation_v1.rule_versions(rule_id,revision,version_hash,body)
                VALUES({f.Scope.RuleId},2,{new byte[32]},{new byte[] { 42 }})
                """);
            var incomplete = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
            Assert.Equal("NUMBER_RULE_CHANGE_INCOMPLETE", incomplete.MessageText);
        }
        Assert.Equal("规则/二", (await f.Owner.ReadRuleAsync())!.Version);
        await using (var tx = await f.H.Db.Database.BeginTransactionAsync())
        {
            await f.H.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_number_allocation_v1.periods SET current_value=current_value+1 WHERE rule_id={f.Scope.RuleId}");
            // Exact old writer column set: no rule_version_hash. It cannot bypass current-version arbitration.
            var failure = await Assert.ThrowsAsync<PostgresException>(() => f.H.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_number_allocation_v1.receipts
                (rule_id,operation_hash,operation_bytes,input_bytes,actor_id,result_bytes,period_key,sequence_value,number_hash,number_bytes,allocated_at)
                SELECT rule_id,operation_hash,operation_bytes,input_bytes,actor_id,result_bytes,period_key,sequence_value+1,number_hash,number_bytes,allocated_at
                  FROM cp6_number_allocation_v1.receipts WHERE rule_id={f.Scope.RuleId}
                """));
            Assert.Equal("23514", failure.SqlState); Assert.Equal("NUMBER_RULE_VERSION_CHANGED", failure.MessageText);
            await tx.RollbackAsync();
        }
        Assert.Equal(2, (await f.Owner.AllocateAsync(NumberCommand("next", "规则/二"))).SequenceValue);
        Assert.Equal(original, await f.Owner.ReadAsync(original.OperationRef));
    }

    [CurrentPermissionPostgresFact]
    public async Task Number_rule_forward_migration_preserves_nonempty_W254_registration_counter_and_receipt()
    {
        await using var h = await Seed();
        var settings = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { Database = "postgres" };
        var name = "number_rule_forward_" + Guid.NewGuid().ToString("N");
        await using (var root = new NpgsqlConnection(settings.ConnectionString))
        {
            await root.OpenAsync();
            await using var command = new NpgsqlCommand("CREATE DATABASE " + name, root); await command.ExecuteNonQueryAsync();
        }
        settings.Database = name;
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql); var policy = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database, settings.ConnectionString,
            policy.MigrationsAssembly, policy.HistoryTable, policy.HistorySchema);
        await using var db = new CP6Context(options.Options, new TenantContext());
        await db.GetService<IMigrator>().MigrateAsync("20261102021000_NumberAllocationV1");
        var ruleId = Guid.NewGuid(); var original = new byte[] { 0, 255, 128, 42 }; var hash = new byte[32];
        db.Pub_DocSequences.Add(new() { Id = ruleId, BizKey = "FORWARD", Prefix = "F" }); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_number_allocation_v1.scopes(rule_id,tenant_id,body) VALUES({ruleId},{TenantContext.DefaultTenant},{original});
                INSERT INTO cp6_number_allocation_v1.periods VALUES({ruleId},'',7);
                INSERT INTO cp6_number_allocation_v1.receipts VALUES({ruleId},{hash},{original},{original},{Guid.NewGuid()},{original},'',7,{hash},{original},clock_timestamp())
                """);
            await tx.CommitAsync();
        }
        await db.Database.MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(original, await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_number_allocation_v1.scopes WHERE rule_id={ruleId}").SingleAsync());
        Assert.Equal(original, await db.Database.SqlQuery<byte[]>($"SELECT result_bytes AS \"Value\" FROM cp6_number_allocation_v1.receipts WHERE rule_id={ruleId}").SingleAsync());
        Assert.Equal(7, await db.Database.SqlQuery<int>($"SELECT current_value AS \"Value\" FROM cp6_number_allocation_v1.periods WHERE rule_id={ruleId}").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_number_allocation_v1.rule_versions").SingleAsync());
    }
}
