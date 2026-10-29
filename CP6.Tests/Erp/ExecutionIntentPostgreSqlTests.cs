using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Core.Services.Wms.ExecutionIntentStoreV1;
using IntentSpace = CP6.Core.Services.Wms.ExecutionIntentStoreV1.Space;

namespace CP6.Tests.Erp;

[Collection("TenantManagementPostgres")]
public sealed class ExecutionIntentPostgreSqlTests(TenantManagementPostgresFixture server)
{
    private static readonly IntentSpace Simulation = new(Guid.NewGuid(), "SIMULATION", Guid.NewGuid());
    private static readonly string Digest = new('a', 64);
    private static Evidence E(string owner = "TEST_OWNER") => new(owner, Guid.NewGuid(), "1", Digest);
    private static Authority A() => new("RULE_APPLY_INTENT", Guid.NewGuid());
    private static Alias Alias() => new("COMMAND", Guid.NewGuid().ToString("N"));
    private static Plan P(string kind = "RULE_CHANGE", int owners = 1) => new(kind, Digest, Digest,
        Enumerable.Range(0, owners).Select(n => new Target("OWNER_" + n, "LOCATION", kind, Digest, [E()])).ToArray());
    private static OwnerOutcome Outcome(Binding b, ActionView action, string state = "PROTECTED", int? pending = 0) => new(
        action.Id, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(action.Payload))).ToLowerInvariant(), b.Prepare.Target.Owner, b.Prepare.RequestKey, b.PrepareDigest, b.Prepare.ExecutionIntentId, b.Prepare.Target.ScopeDigest,
        b.Prepare.CandidateDigest, state, "COMPLETE", "prepare-" + b.Id, "guard-1", "control-1", E(b.Prepare.Target.Owner), pending);
    private static EffectResult Effect() => new(E("WMS"), "{\"applied\":true}");
    private static async Task F(string code, Func<Task> work) => Assert.Equal(code, (await Assert.ThrowsAsync<InvalidOperationException>(work)).Message);
    private async Task<Intent> Seed(Guid tenant, Authority authority, Plan plan, bool ready = false)
    {
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var tx = await db.Database.BeginTransactionAsync();
        var store = await Open(db, Simulation, authority); var i = await store.Admit(Alias(), plan);
        i = await store.TakeOver(i.Revision, i.ExecutionEpoch);
        if (ready)
        {
            foreach (var b in await store.Bindings())
            { var action = (await store.Actions()).Single(x => x.BindingId == b.Id); var attempt = await store.ClaimAction(action.Id); await store.Observe(action.Id, attempt.AttemptEpoch, Outcome(b, action)); }
            i = await store.Ready(i.Revision, i.ExecutionEpoch);
        }
        await tx.CommitAsync(); return i;
    }

    [CurrentPermissionPostgresFact]
    public async Task Intent_original_prepare_keys_and_required_set_survive_restart_and_alias_conflicts()
    {
        var tenant = Guid.NewGuid(); var authority = A(); var plan = P(owners: 2); var alias = Alias(); Guid id; List<Binding> original;
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, authority);
            id = (await store.Admit(alias, plan)).Id; original = await store.Bindings(); Assert.Equal(2, original.Count);
            Assert.Equal(2, (await store.Actions()).Count); await tx.CommitAsync();
        }
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, authority);
            Assert.Equal(id, (await store.Admit(Alias(), plan with { RequiredOwners = plan.RequiredOwners.Reverse().ToArray() })).Id);
            Assert.Equal(original.Select(x => x.Prepare.RequestKey), (await store.Bindings()).Select(x => x.Prepare.RequestKey));
            await F("EXECUTION_AUTHORITY_INPUT_CONFLICT", () => store.Admit(alias, plan with { CandidateDigest = new string('b', 64) }));
            await F("EXECUTION_AUTHORITY_INPUT_CONFLICT", () => store.Admit(alias, plan with { RequiredOwners = [plan.RequiredOwners[0]] }));
            await tx.CommitAsync();
        }
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var other = await Open(db, Simulation, A());
            await F("EXECUTION_ALIAS_ALREADY_BOUND", () => other.Admit(alias, plan)); Assert.Null(await other.Read()); await tx.CommitAsync();
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Intent_unknown_observation_preserves_confirmed_guard_and_blocks_ready_until_recovery()
    {
        var tenant = Guid.NewGuid(); var authority = A(); var ready = await Seed(tenant, authority, P(), true);
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var tx = await db.Database.BeginTransactionAsync();
        var store = await Open(db, Simulation, authority); var b = (await store.Bindings()).Single(); var action = (await store.Actions()).Single();
        var known = action.LastConfirmed!; var attempt = await store.ClaimAction(action.Id); var preparing = (await store.Read())!;
        Assert.Equal("PREPARING", preparing.State); Assert.Equal(ready.ExecutionEpoch, preparing.ExecutionEpoch);
        var unknown = Outcome(b, action, "NOT_OBSERVED", null) with { Coverage = "UNKNOWN", PrepareId = null, GuardEpoch = null, ControlRevision = null, Receipt = null };
        var observed = await store.Observe(action.Id, attempt.AttemptEpoch, unknown);
        Assert.Equal(known, observed.LastConfirmed); Assert.Equal("UNKNOWN", observed.ObservationState);
        await F("EXECUTION_PROTECTION_INCOMPLETE", () => store.Ready(preparing.Revision, preparing.ExecutionEpoch));
        var retry = await store.ClaimAction(action.Id); Assert.Equal(attempt.AttemptEpoch + 1, retry.AttemptEpoch);
        Assert.Equal(action.Payload, retry.Payload); await store.Observe(action.Id, retry.AttemptEpoch, known);
        Assert.Equal("READY", (await store.Ready(preparing.Revision, preparing.ExecutionEpoch)).State); await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Intent_takeover_fences_old_business_worker_and_all_required_owners_must_be_current()
    {
        var tenant = Guid.NewGuid(); var authority = A(); var seed = await Seed(tenant, authority, P(owners: 2));
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var tx = await db.Database.BeginTransactionAsync();
        var store = await Open(db, Simulation, authority); var taken = await store.TakeOver(seed.Revision, seed.ExecutionEpoch);
        Assert.Equal(seed.ExecutionEpoch + 1, taken.ExecutionEpoch);
        await F("EXECUTION_FENCE_CONFLICT", () => store.Ready(seed.Revision, seed.ExecutionEpoch));
        var bindings = await store.Bindings(); var b = bindings[0]; var action = (await store.Actions()).Single(x => x.BindingId == b.Id);
        var attempt = await store.ClaimAction(action.Id);
        await F("EXECUTION_OWNER_BINDING_CONFLICT", () => store.Observe(action.Id, attempt.AttemptEpoch, Outcome(b, action) with { ActionKey = Guid.NewGuid() }));
        await F("EXECUTION_OWNER_BINDING_CONFLICT", () => store.Observe(action.Id, attempt.AttemptEpoch, Outcome(b, action) with { ActionDigest = new string('b', 64) }));
        await F("EXECUTION_OWNER_BINDING_CONFLICT", () => store.Observe(action.Id, attempt.AttemptEpoch, Outcome(b, action) with { PrepareRequestKey = Guid.NewGuid() }));
        await store.Observe(action.Id, attempt.AttemptEpoch, Outcome(b, action));
        await F("EXECUTION_PROTECTION_INCOMPLETE", () => store.Ready(taken.Revision, taken.ExecutionEpoch));
        b = bindings[1]; action = (await store.Actions()).Single(x => x.BindingId == b.Id); attempt = await store.ClaimAction(action.Id);
        await store.Observe(action.Id, attempt.AttemptEpoch, Outcome(b, action, pending: 1));
        await F("EXECUTION_PROTECTION_INCOMPLETE", () => store.Ready(taken.Revision, taken.ExecutionEpoch));
        attempt = await store.ClaimAction(action.Id); await store.Observe(action.Id, attempt.AttemptEpoch, Outcome(b, action));
        var ready = await store.Ready(taken.Revision, taken.ExecutionEpoch);
        await F("EXECUTION_FENCE_CONFLICT", () => store.Apply(ready.Revision, seed.ExecutionEpoch, _ => throw new Exception("must not execute")));
        await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Intent_applied_original_recovery_only_retries_confirmation_without_reexecuting_effect()
    {
        var tenant = Guid.NewGuid(); var authority = A(); var plan = P("STOP_NEW_USE"); var ready = await Seed(tenant, authority, plan, true); string receipt;
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, authority);
            receipt = await store.Apply(ready.Revision, ready.ExecutionEpoch, async _ =>
            { db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "atomic execution sibling" }); await db.SaveChangesAsync(); return Effect(); });
            await tx.CommitAsync();
        }
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, authority);
            Assert.True(await db.Sys_Tenants.AnyAsync(x => x.Id == tenant));
            Assert.Equal(receipt, await store.Apply(ready.Revision, ready.ExecutionEpoch, _ => throw new Exception("duplicate effect")));
            Assert.Equal(receipt, await store.Seal(1, "LATE_CANCEL"));
            Assert.Equal("APPLIED", (await store.Admit(Alias(), plan)).State);
            var terminal = (await store.Read())!; var b = (await store.Bindings()).Single(); var confirm = (await store.Actions()).Single(x => x.Kind == "CONFIRM_COMMITTED");
            Assert.DoesNotContain(await store.Actions(), x => x.Kind == "RELEASE_SEALED");
            var attempt = await store.ClaimAction(confirm.Id); await store.Observe(confirm.Id, attempt.AttemptEpoch, Outcome(b, confirm, "UNKNOWN", null) with { Coverage = "UNKNOWN" });
            attempt = await store.ClaimAction(confirm.Id);
            await F("EXECUTION_OWNER_OUTCOME_INVALID", () => store.Observe(confirm.Id, attempt.AttemptEpoch, Outcome(b, confirm, "CONFIRMED_TRANSITIONED")));
            await store.Observe(confirm.Id, attempt.AttemptEpoch, Outcome(b, confirm, "CONFIRMED_STOP_CONTROL"));
            var after = (await store.Read())!; Assert.Equal(terminal.Revision, after.Revision); Assert.Equal(terminal.ExecutionEpoch, after.ExecutionEpoch); Assert.Equal(receipt, after.Decision);
            Assert.Equal("SUCCEEDED", (await store.ClaimAction(confirm.Id)).State); await tx.CommitAsync();
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Intent_seal_before_prepare_reply_covers_future_aliases_and_preserves_release_request_key()
    {
        var tenant = Guid.NewGuid(); var authority = A(); var plan = P(); var seed = await Seed(tenant, authority, plan);
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var tx = await db.Database.BeginTransactionAsync();
        var store = await Open(db, Simulation, authority); var b = (await store.Bindings()).Single(); var prepare = (await store.Actions()).Single();
        var attempt = await store.ClaimAction(prepare.Id); var closure = await store.Seal(seed.Revision, "OPERATOR_ABANDON");
        Assert.Contains("ALL_CURRENT_AND_FUTURE_FOR_AUTHORITY", closure); Assert.Contains("\"externalClosureAuthority\":false", closure);
        Assert.Equal("SEALED_NO_EFFECT", (await store.Admit(Alias(), plan)).State);
        await F("EXECUTION_TERMINAL", () => store.Apply(seed.Revision, seed.ExecutionEpoch, _ => throw new Exception("late effect")));
        await F("EXECUTION_PREPARE_CLOSED", () => store.ClaimAction(prepare.Id));
        // Already-sent prepare can report late. It cannot reopen the intent or replace the release action.
        await store.Observe(prepare.Id, attempt.AttemptEpoch, Outcome(b, prepare));
        var release = (await store.Actions()).Single(x => x.Kind == "RELEASE_SEALED"); Assert.Contains(b.Prepare.RequestKey.ToString(), release.Payload);
        var releaseAttempt = await store.ClaimAction(release.Id);
        await F("EXECUTION_OWNER_GUARD_CONFLICT", () => store.Observe(release.Id, releaseAttempt.AttemptEpoch, Outcome(b, release, "RELEASED") with { GuardEpoch = "different" }));
        await store.Observe(release.Id, releaseAttempt.AttemptEpoch, Outcome(b, release, "RELEASED"));
        Assert.Equal(closure, (await store.Read())!.Decision); Assert.Equal(seed.ExecutionEpoch + 1, (await store.Read())!.ExecutionEpoch); await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Intent_sealed_unobserved_prepare_keeps_original_action_and_accepts_authoritative_tombstone_observation()
    {
        var tenant = Guid.NewGuid(); var authority = A(); var seed = await Seed(tenant, authority, P());
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var tx = await db.Database.BeginTransactionAsync();
        var store = await Open(db, Simulation, authority); var b = (await store.Bindings()).Single();
        var prepare = (await store.Actions()).Single(); var sent = await store.ClaimAction(prepare.Id); await store.Seal(seed.Revision, "ABANDON");
        var release = (await store.Actions()).Single(x => x.Kind == "RELEASE_SEALED"); var attempt = await store.ClaimAction(release.Id);
        var tombstone = Outcome(b, release, "CLOSED_NO_EFFECT") with { PrepareId = null, GuardEpoch = null, ControlRevision = null };
        await store.Observe(release.Id, attempt.AttemptEpoch, tombstone);
        await F("EXECUTION_OWNER_CLOSURE_CONFLICT", () => store.Observe(prepare.Id, sent.AttemptEpoch, Outcome(b, prepare)));
        Assert.Null((await store.Actions()).Single(x => x.Kind == "PREPARE").LastConfirmed); await tx.CommitAsync();
        // This records a test Owner response; it does not implement a real Owner's prepare tombstone.
    }

    [CurrentPermissionPostgresFact]
    public async Task Intent_action_takeover_rejects_old_attempt_and_conflicting_observation_replay()
    {
        var tenant = Guid.NewGuid(); var authority = A(); var seed = await Seed(tenant, authority, P());
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var tx = await db.Database.BeginTransactionAsync();
        var store = await Open(db, Simulation, authority); var b = (await store.Bindings()).Single(); var action = (await store.Actions()).Single();
        var old = await store.ClaimAction(action.Id); var current = await store.ClaimAction(action.Id); var outcome = Outcome(b, action);
        await F("EXECUTION_ACTION_FENCE_CONFLICT", () => store.Observe(action.Id, old.AttemptEpoch, outcome));
        await store.Observe(action.Id, current.AttemptEpoch, outcome); await store.Observe(action.Id, current.AttemptEpoch, outcome);
        await F("EXECUTION_OBSERVATION_INPUT_CONFLICT", () => store.Observe(action.Id, current.AttemptEpoch, outcome with { State = "UNKNOWN" }));
        Assert.Equal(seed.ExecutionEpoch, (await store.Read())!.ExecutionEpoch); Assert.Equal(seed.Revision, (await store.Read())!.Revision); await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Intent_mid_effect_failure_and_caller_rollback_leave_no_decision_or_confirmation_actions()
    {
        var tenant = Guid.NewGuid(); var authority = A(); var ready = await Seed(tenant, authority, P(), true);
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, authority);
            await F("FAULT_AFTER_SIBLING_WRITE", () => store.Apply(ready.Revision, ready.ExecutionEpoch, async _ =>
            { db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "rolled back sibling" }); await db.SaveChangesAsync(); throw new InvalidOperationException("FAULT_AFTER_SIBLING_WRITE"); }));
            db.ChangeTracker.Clear(); Assert.False(await db.Sys_Tenants.AnyAsync(x => x.Id == tenant));
            Assert.Equal("READY", (await store.Read())!.State); Assert.Single(await store.Actions());
            await store.Apply(ready.Revision, ready.ExecutionEpoch, async _ =>
            { db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "caller rollback" }); await db.SaveChangesAsync(); return Effect(); });
            await tx.RollbackAsync();
        }
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, authority);
            Assert.False(await db.Sys_Tenants.AnyAsync(x => x.Id == tenant)); Assert.Single(await store.Actions()); Assert.Equal("READY", (await store.Read())!.State);
            await store.Apply(ready.Revision, ready.ExecutionEpoch, _ => Task.FromResult(Effect())); await tx.CommitAsync();
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Intent_failure_after_decision_and_first_confirmation_rolls_back_entire_effect()
    {
        var tenant = Guid.NewGuid(); var authority = A(); var ready = await Seed(tenant, authority, P(owners: 2), true);
        await using var db = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var tx = await db.Database.BeginTransactionAsync();
        var store = await Open(db, Simulation, authority); var bindings = await store.Bindings();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('cp6.test.fail_binding',{bindings[1].Id.ToString()},true)");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION pg_temp.execution_action_fault() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.kind='CONFIRM_COMMITTED' AND NEW.binding_id::text=current_setting('cp6.test.fail_binding') THEN
                RAISE EXCEPTION 'FAULT_AFTER_FIRST_CONFIRMATION';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER execution_test_fault BEFORE INSERT ON cp6_execution_v1.actions FOR EACH ROW EXECUTE FUNCTION pg_temp.execution_action_fault();
            """);
        Assert.Contains("FAULT_AFTER_FIRST_CONFIRMATION", (await Assert.ThrowsAsync<PostgresException>(() => store.Apply(ready.Revision, ready.ExecutionEpoch, async _ =>
        { db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "atomic terminal action failure" }); await db.SaveChangesAsync(); return Effect(); }))).Message);
        db.ChangeTracker.Clear(); Assert.False(await db.Sys_Tenants.AnyAsync(x => x.Id == tenant));
        Assert.Equal("READY", (await store.Read())!.State); Assert.All(await store.Actions(), x => Assert.Equal("PREPARE", x.Kind));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER execution_test_fault ON cp6_execution_v1.actions");
        await store.Apply(ready.Revision, ready.ExecutionEpoch, _ => Task.FromResult(Effect()));
        Assert.Equal(2, (await store.Actions()).Count(x => x.Kind == "CONFIRM_COMMITTED")); await tx.CommitAsync();
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Intent_apply_and_seal_race_share_one_actual_postgresql_decision_lock(bool applyFirst)
    {
        var tenant = Guid.NewGuid(); var authority = A(); var ready = await Seed(tenant, authority, P(), true);
        await using var first = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var tx = await first.Database.BeginTransactionAsync();
        var store = await Open(first, Simulation, authority); var effectCalls = 0;
        var winner = applyFirst ? await store.Apply(ready.Revision, ready.ExecutionEpoch, _ => { Interlocked.Increment(ref effectCalls); return Task.FromResult(Effect()); }) : await store.Seal(ready.Revision, "SEAL_FIRST");
        var backend = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var contender = Task.Run(async () =>
        {
            await using var db = server.Context(new TenantContext { CurrentTenantId = tenant }); await using var t = await db.Database.BeginTransactionAsync();
            backend.SetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID); var second = await Open(db, Simulation, authority);
            if (applyFirst) Assert.Equal(winner, await second.Seal(ready.Revision, "LATE_SEAL"));
            else await F("EXECUTION_TERMINAL", () => second.Apply(ready.Revision, ready.ExecutionEpoch, _ => { Interlocked.Increment(ref effectCalls); return Task.FromResult(Effect()); }));
            var result = (await second.Read())!; await t.CommitAsync(); return result;
        });
        try
        {
            var pid = await backend.Task.WaitAsync(TimeSpan.FromSeconds(10)); var blocked = false;
            for (var n = 0; n < 100 && !blocked; n++)
            { blocked = await first.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_locks WHERE pid={pid} AND locktype='advisory' AND NOT granted) AS \"Value\"").SingleAsync(); if (!blocked) await Task.Delay(25); }
            Assert.True(blocked); await tx.CommitAsync(); var result = await contender.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(applyFirst ? "APPLIED" : "SEALED_NO_EFFECT", result.State); Assert.Equal(applyFirst ? 1 : 0, effectCalls);
        }
        finally { if (!contender.IsCompleted) { await tx.RollbackAsync(); await contender; } }
    }

    [CurrentPermissionPostgresFact]
    public async Task Intent_database_rejects_original_rewrite_terminal_reopen_and_incomplete_terminal_action_set()
    {
        var tenant = Guid.NewGuid(); var authority = A(); var ready = await Seed(tenant, authority, P(), true);
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_execution_v1.bindings SET prepare_key={Guid.NewGuid()} WHERE tenant_id={tenant}"));
        }
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_execution_v1.intents SET state='APPLIED',revision=revision+1,decision_bytes={new byte[] { 1 }} WHERE tenant_id={tenant}");
            Assert.Contains("EXECUTION_ACTION_SET_INCOMPLETE", (await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync())).Message);
        }
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync(); var store = await Open(db, Simulation, authority); await store.Seal(ready.Revision, "ABANDON"); await tx.CommitAsync();
        }
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            Assert.Contains("EXECUTION_INTENT_UPDATE_INVALID", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_execution_v1.intents SET state='PREPARING',revision=revision+1,decision_bytes=NULL WHERE tenant_id={tenant}"))).Message);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Intent_requires_actual_transaction_and_isolates_tenant_environment_and_sandbox()
    {
        var tenant = Guid.NewGuid(); var authority = A(); var plan = P(); var original = await Seed(tenant, authority, plan);
        await using (var db = server.Context(new TenantContext { CurrentTenantId = tenant }))
            await F("EXECUTION_OWNER_TRANSACTION_REQUIRED", () => Open(db, Simulation, authority));
        foreach (var (t, space) in new[] { (Guid.NewGuid(), Simulation), (tenant, Simulation with { EnvironmentId = Guid.NewGuid() }), (tenant, Simulation with { SandboxScopeId = Guid.NewGuid() }), (tenant, Simulation with { Mode = "PRODUCTION", SandboxScopeId = null }) })
        {
            await using var db = server.Context(new TenantContext { CurrentTenantId = t }); await using var tx = await db.Database.BeginTransactionAsync();
            var store = await Open(db, space, authority); Assert.Null(await store.Read()); Assert.NotEqual(original.Id, (await store.Admit(Alias(), plan)).Id); await tx.CommitAsync();
        }
        var context = new TenantContext { CurrentTenantId = tenant };
        await using var mutable = server.Context(context); await using var current = await mutable.Database.BeginTransactionAsync();
        await F("EXECUTION_ENVIRONMENT_INVALID", () => Open(mutable, Simulation with { SandboxScopeId = null }, authority));
        var session = await Open(mutable, Simulation, authority); context.CurrentTenantId = Guid.NewGuid();
        await F("EXECUTION_OWNER_TRANSACTION_REQUIRED", () => session.Read()); context.CurrentTenantId = tenant;
        await F("EXECUTION_SESSION_ALREADY_OPEN", () => Open(mutable, Simulation, authority)); await current.CommitAsync();
        await F("EXECUTION_OWNER_TRANSACTION_REQUIRED", () => session.Read());
    }
}
