using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static WarehouseClockOptionsV1 ClockOptions(Harness h) => new()
    {
        Enabled = true, Tenants = [h.Tenant], Authority = new(ScopeEnvironment, Guid.NewGuid(), "1", "POSTGRESQL_CLOCK_TIMESTAMP_V1",
            "DEMO", "isolated PG18 fixture, explicitly NOT a production UTC attestation", DateTimeOffset.UtcNow.AddDays(-1).ToString("O"),
            DateTimeOffset.UtcNow.AddDays(1).ToString("O"), 25, 10000)
    };
    private static WarehouseClockOwnerV1 Clocks(Harness h, WarehouseClockOptionsV1 c, bool development = true)
        => new(h.Db, RuleReferenceAuthority(h), new(h.Db, ScopeFence(h), Options.Create(c), development));
    private sealed record ClockFixture(Guid Location, Guid Warehouse, LocationRuleResultV1 Candidate, WarehouseClockOptionsV1 Options,
        WarehouseClockCommandV1 Command, WarehouseClockOwnerV1 Owner);
    private static async Task<ClockFixture> ClockSetup(Harness h)
    {
        await RegisterScope(h); var f = await RuleReferencesSetup(h); await EngineeringAction(h, "wms-warehouse", "clock-policy");
        var c = ClockOptions(h); var owner = Clocks(h, c); var source = (await owner.ContextAsync(f.Warehouse)).Source!;
        var draft = ResolvableRule() with { ValidFrom = "2026-10-01T08:00:00.0000001+08:00", ValidTo = "2027-01-01T08:00:00.0000002+08:00" };
        var rule = await ReferenceRules(h).CreateAsync(f.Location, new(Guid.NewGuid(), draft));
        return new(f.Location, f.Warehouse, rule, c, new(Guid.NewGuid(), "absent", new(true, source.Reference, c.Authority!.ValidFrom, c.Authority.ValidTo), "Explicit isolated source policy"), owner);
    }
    private static Task<RuleTimeAssessmentV1> ClockEvaluate(ClockFixture f, string kind = "CURRENT", string? at = null)
        => f.Owner.EvaluateAsync(f.Location, f.Candidate.Reference.Id, f.Candidate.Etag, new(f.Candidate.Reference, kind, at));

    [CurrentPermissionPostgresFact]
    public async Task WarehouseClock_native_policy_observes_real_advancing_PG_and_retains_originals_without_rule_effect()
    {
        await using var h = await Seed(); var f = await ClockSetup(h);
        Assert.Equal("CLOCK_POLICY_REQUIRED", (await ClockEvaluate(f)).Reason);
        var saved = await f.Owner.SaveAsync(f.Warehouse, f.Command); var p = (await f.Owner.ContextAsync(f.Warehouse)).Policy!;
        Assert.Equal(f.Command.Definition, p.Original.Definition); Assert.Equal(saved.Reference, p.Reference);
        var first = await ClockEvaluate(f); Assert.Equal("TIME_SATISFIED", first.Outcome); Assert.False(first.AdmissionGrant); Assert.True(first.RequiresFinalEffectRecheck);
        var clock = first.Clock!; Assert.Equal("DEMO", clock.Original.EvidenceMode); Assert.Equal(saved.Reference, clock.Original.Policy);
        Assert.True(clock.Original.Earliest < clock.Original.ObservedAt && clock.Original.Latest > clock.Original.ObservedAt);
        Assert.Equal(clock, await f.Owner.ObservationAsync(f.Warehouse, clock.Reference.Id));
        var second = await ClockEvaluate(f); Assert.True(second.Clock!.Original.ObservedAt > clock.Original.ObservedAt);
        Assert.Equal(p.OriginalJson, (await f.Owner.HistoryAsync(f.Warehouse, "1")).OriginalJson);
        Assert.Equal("0", Assert.Single((await ReferenceRules(h).ContextAsync(f.Location)).Selectors).Revision);
        var native = await h.Db.Locations.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.TenantId==h.Tenant && x.Id == f.Location);
        Assert.True(native.IsBlocked && native.IsInitializationQuarantined); Assert.Equal(0, native.CapacityQty);
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_warehouse_clock_v1.observations WHERE id={clock.Reference.Id}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseClock_planned_half_open_interval_preserves_offset_and_seven_digits_and_never_grants_admission()
    {
        await using var h = await Seed(); var f = await ClockSetup(h); await f.Owner.SaveAsync(f.Warehouse, f.Command);
        foreach (var (at, expected) in new[] { ("2026-10-01T08:00:00.0000000+08:00", "NOT_YET_VALID"),
            ("2026-10-01T08:00:00.0000001+08:00", "TIME_SATISFIED"), ("2027-01-01T00:00:00.0000001Z", "TIME_SATISFIED"),
            ("2027-01-01T08:00:00.0000002+08:00", "EXPIRED") })
        {
            var result = await ClockEvaluate(f, "PLANNED", at); Assert.Equal(expected, result.Outcome); Assert.Equal(at, result.EvaluatedAt);
            Assert.True(result.IsPrediction); Assert.False(result.AdmissionGrant); Assert.NotNull(result.Clock);
        }
        foreach (var bad in new[] { "2026-10-01T08:00:00", "2026-10-01T08:00:00.12345678Z", "bad" })
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ClockEvaluate(f, "PLANNED", bad));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ClockEvaluate(f, "CURRENT", "2026-10-01T00:00:00Z"));
        Assert.Equal(428, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.EvaluateAsync(f.Location, f.Candidate.Reference.Id, null, new(f.Candidate.Reference, "CURRENT", null)))).Status);
        var changed = await ReferenceRules(h).PatchAsync(f.Location, f.Candidate.Reference.Id, f.Candidate.Etag,
            new(Guid.NewGuid(), RulePatch("""{"validFrom":{"op":"CLEAR"}}""")));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ClockEvaluate(f))).Status);
        Assert.NotEqual(f.Candidate.Reference, changed.Reference);
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseClock_source_missing_changed_expired_wrong_environment_and_disabled_remain_unknown()
    {
        await using var h = await Seed(); var f = await ClockSetup(h); await f.Owner.SaveAsync(f.Warehouse, f.Command);
        var authority = f.Options.Authority!;
        foreach (var change in new Action[] { () => f.Options.Enabled = false, () => f.Options.Tenants = [],
            () => f.Options.Authority = authority with { EnvironmentId = Guid.NewGuid() }, () => f.Options.Authority = authority with { EvidenceMode = "" } })
        {
            change(); Assert.Equal("UNKNOWN", (await ClockEvaluate(f)).Outcome);
            f.Options.Enabled = true; f.Options.Tenants = [h.Tenant]; f.Options.Authority = authority;
        }
        Assert.Null((await Clocks(h, f.Options, false).ContextAsync(f.Warehouse)).Source);
        f.Options.Authority = authority with { AttestationRef = "changed without increasing version" };
        Assert.Equal("CLOCK_SOURCE_CHANGED", (await ClockEvaluate(f)).Reason);
        var expired = authority with { ValidFrom = DateTimeOffset.UtcNow.AddDays(-2).ToString("O"), ValidTo = DateTimeOffset.UtcNow.AddDays(-1).ToString("O") };
        f.Options.Authority = expired; var source = (await f.Owner.ContextAsync(f.Warehouse)).Source!;
        var second = await f.Owner.SaveAsync(f.Warehouse, new(Guid.NewGuid(), "1", new(true, source.Reference, expired.ValidFrom, expired.ValidTo), "Explicit expired source retained as configuration"));
        Assert.Equal("CLOCK_SOURCE_OUTSIDE_VALIDITY", (await ClockEvaluate(f)).Reason);
        f.Options.Enabled = false; var p = (await f.Owner.ContextAsync(f.Warehouse)).Policy!;
        await f.Owner.SaveAsync(f.Warehouse, new(Guid.NewGuid(), second.Reference.Version, p.Original.Definition with { Enabled = false }, "Disable even when source unavailable"));
        Assert.Equal("CLOCK_POLICY_DISABLED", (await ClockEvaluate(f)).Reason);
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseClock_current_uncertainty_overlap_backward_and_slow_reads_fail_closed()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var f = await ClockSetup(h); await f.Owner.SaveAsync(f.Warehouse, f.Command);
        // Explicit fault injection in the actual PG clock query, never a production provider or a successful acceptance fixture.
        hook.Before = cmd => { if (cmd.CommandText.Contains("clock_timestamp()")) cmd.CommandText = cmd.CommandText.Replace("clock_timestamp()", "TIMESTAMPTZ '2026-10-01 00:00:00+00'"); return Task.CompletedTask; };
        Assert.Equal("CLOCK_SOURCE_OUTSIDE_VALIDITY", (await ClockEvaluate(f)).Reason); hook.Before = null;
        var now = DateTimeOffset.UtcNow;
        var changed = await ReferenceRules(h).PatchAsync(f.Location, f.Candidate.Reference.Id, f.Candidate.Etag,
            new(Guid.NewGuid(), RulePatch(JsonSerializer.Serialize(new { validFrom = new { op = "SET", value = now.ToString("O") } }))));
        f = f with { Candidate = changed };
        var stamp = now.ToString("O"); hook.Before = cmd => { if (cmd.CommandText.Contains("clock_timestamp()")) cmd.CommandText = cmd.CommandText.Replace("clock_timestamp()", "TIMESTAMPTZ '" + stamp + "'"); return Task.CompletedTask; };
        Assert.Equal("CLOCK_INTERVAL_CROSSES_RULE_BOUNDARY", (await ClockEvaluate(f)).Reason); hook.Before = null;
        hook.Before = cmd => { if (cmd.CommandText.Contains("clock_timestamp()")) cmd.CommandText = cmd.CommandText.Replace("clock_timestamp()", "(clock_timestamp()+interval '1 hour')"); return Task.CompletedTask; };
        Assert.NotNull((await ClockEvaluate(f)).Clock); hook.Before = null;
        Assert.Equal("CLOCK_MOVED_BACKWARD", (await ClockEvaluate(f)).Reason);
        // A new source version still shares the watermark; a delayed fresh read cannot bypass it either.
        f.Options.Authority = f.Options.Authority! with { Version = "2", MaxReadMilliseconds = 20 };
        var source = (await f.Owner.ContextAsync(f.Warehouse)).Source!;
        await f.Owner.SaveAsync(f.Warehouse, f.Command with { OperationId = Guid.NewGuid(), ExpectedVersion = "1", Definition = f.Command.Definition with { Source = source.Reference } });
        Assert.Equal("CLOCK_MOVED_BACKWARD", (await ClockEvaluate(f)).Reason);
        hook.Before = async cmd => { if (cmd.CommandText.Contains("clock_timestamp()")) await Task.Delay(80); };
        Assert.Equal("CLOCK_READ_TOO_SLOW", (await ClockEvaluate(f)).Reason); hook.Before = null;
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseClock_original_query_close_CAS_competition_and_current_permission_recovery()
    {
        await using var h = await Seed(); var f = await ClockSetup(h); var first = await f.Owner.SaveAsync(f.Warehouse, f.Command);
        Assert.Equal(first, await f.Owner.SaveAsync(f.Warehouse, f.Command));
        Assert.Equal("COMMITTED", (await f.Owner.OperationAsync(f.Warehouse, f.Command.OperationId)).State);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.SaveAsync(f.Warehouse, f.Command with { Reason = "different intent" }))).Status);
        var close = Guid.NewGuid(); Assert.Equal("CLOSED", (await f.Owner.OperationAsync(f.Warehouse, close, true)).State);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.SaveAsync(f.Warehouse, f.Command with { OperationId = close, ExpectedVersion = "1" }))).Status);
        await using var peerDb = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant }); var peer = Clocks(h with { Db = peerDb }, f.Options);
        var c = f.Command with { OperationId = Guid.NewGuid(), ExpectedVersion = "1", Definition = f.Command.Definition with { Enabled = false } };
        var winners = await Task.WhenAll(Record.ExceptionAsync(() => f.Owner.SaveAsync(f.Warehouse, c)), Record.ExceptionAsync(() => peer.SaveAsync(f.Warehouse, c with { OperationId = Guid.NewGuid() })));
        Assert.Single(winners, x => x == null); Assert.Equal("2", (await f.Owner.ContextAsync(f.Warehouse)).Version);
        await RevokeTransferAction(h, "wms-warehouse", "clock-policy"); Assert.Equal(first, await f.Owner.SaveAsync(f.Warehouse, f.Command));
        await RevokeTransferAction(h, "wms-warehouse", "query");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.OperationAsync(f.Warehouse, f.Command.OperationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseClock_policy_and_journal_roll_back_together_and_observation_rechecks_access()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); var f = await ClockSetup(h);
        hook.Before = cmd => { if (cmd.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations")) cmd.CommandText = "SELECT 1"; return Task.CompletedTask; };
        Assert.NotNull(await Record.ExceptionAsync(() => f.Owner.SaveAsync(f.Warehouse, f.Command))); hook.Before = null;
        Assert.Equal("absent", (await f.Owner.ContextAsync(f.Warehouse)).Version); Assert.Equal("UNKNOWN", (await f.Owner.OperationAsync(f.Warehouse, f.Command.OperationId)).State);
        await f.Owner.SaveAsync(f.Warehouse, f.Command);
        var fired = false; hook.Before = async cmd =>
        {
            if (!cmd.CommandText.Contains("INSERT INTO cp6_warehouse_clock_v1.observations")) return; hook.Before = null; fired = true;
            await using var revoke = new NpgsqlCommand("DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"ActionCode\"='query' AND \"MenuId\" IN(SELECT \"MenuId\" FROM public.\"Sys_Menus\" WHERE \"MenuKey\"='wms-location-rule')", (NpgsqlConnection)cmd.Connection!, (NpgsqlTransaction)cmd.Transaction!);
            revoke.Parameters.AddWithValue("tenant", h.Tenant); await revoke.ExecuteNonQueryAsync();
        };
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ClockEvaluate(f))).Status); Assert.True(fired);
        Assert.Equal(0, await h.Db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM cp6_warehouse_clock_v1.observations WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task WarehouseClock_foreign_tenant_hidden_fields_and_missing_database_guard_are_rejected()
    {
        await using var h = await Seed(); var f = await ClockSetup(h); await f.Owner.SaveAsync(f.Warehouse, f.Command);
        await using var other = await Seed(); var g = await ClockSetup(other);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => g.Owner.ContextAsync(f.Warehouse))).Status);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-location-rule", FieldName = "Validity", Access = 3 }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ClockEvaluate(f))).Status);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_warehouse_clock_v1.observations DISABLE TRIGGER warehouse_clock_observation_valid");
        try { Assert.Equal("CLOCK_POLICY_STORE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.ContextAsync(f.Warehouse))).Message); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_warehouse_clock_v1.observations ENABLE TRIGGER warehouse_clock_observation_valid"); }
    }
}
