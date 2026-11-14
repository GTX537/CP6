using System.Net;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using CP6.Tests.Wf;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PGT017_legacy_OA_timer_orders_claim_and_recovery_with_pause(bool recovery, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var administrator = await TenantManagementSeed();
        var trigger = await SeedManualTrigger(h, timer: true);
        var now = new DateTime(2026, 11, 12, 17, 0, 0, DateTimeKind.Utc);
        var due = now.AddMinutes(-5);
        var next = recovery ? now.AddHours(1) : due;
        var database = h.Db;
        await database.Wf_FlowTriggers.Where(t => t.Id == trigger).ExecuteUpdateAsync(t => t.SetProperty(t => t.NextDueUtc, next));
        var idempotency = $"{trigger}:{due:O}";
        if (recovery)
        {
            database.Wf_TriggerFires.Add(new()
            {
                TriggerId = trigger, Source = WfTriggerType.Timer, IdempotencyKey = idempotency,
                FiredUtc = now - FlowTriggerService.RecoveryGrace - TimeSpan.FromSeconds(1)
            });
            await database.SaveChangesAsync();
        }

        var tenant = h.Tenant;
        var connection = database.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(tenant));
        var manager = await Login(host, administrator);
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(tenant)));
        var snapshot = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var suspend = new TenantManagementCommandV1("timer-trigger-pause", snapshot.Revision, "suspend", null);
        var before = await ManualEffects(h, trigger);
        var oldAuthority = await ReadPauseAuthority(h, administrator.User);
        Assert.Equal(recovery ? 1 : 0, before.Fires);
        Assert.Equal(0, before.Instances);

        await using var runner = server.Context(new TenantContext { CurrentTenantId = tenant });
        var timer = new FlowTriggerService(runner, SubFlowTestHarness.Engine(runner));
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<int> scan;
        Task<HttpResponseMessage> pause;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var blocker = pauseFirst
                ? """LOCK TABLE public."Sys_Tenants" IN SHARE MODE"""
                : """LOCK TABLE public."Wf_FlowInstance" IN SHARE MODE""";
            await using var block = new NpgsqlCommand(blocker, barrier, hold);
            await block.ExecuteNonQueryAsync();
            if (pauseFirst)
            {
                pause = Pause();
                var authority = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                scan = timer.ScanTimersOnceAsync(now, default);
                var pending = await WaitForFence(observer, authority.Backend, "%cp6_authentication_v1.acquire()%", null, scan);
                output.WriteLine($"Pause backend {authority.Backend}; timer backend {pending.Backend}; {pending.WaitEvent}");
            }
            else
            {
                scan = timer.ScanTimersOnceAsync(now, default);
                var worker = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Wf_FlowInstance%", null, scan);
                pause = Pause();
                var pending = await WaitForFence(observer, worker.Backend, "%cp6_authentication_v1.acquire()%", null, pause);
                output.WriteLine($"Timer backend {worker.Backend}; pause backend {pending.Backend}; {pending.WaitEvent}");
            }
            Assert.Equal(before, await ManualEffects(h, trigger));
            Assert.Equal(next, await NextDue());
            Assert.Equal(oldAuthority, await ReadPauseAuthority(h, administrator.User));
            await hold.RollbackAsync();
        }

        using var paused = await pause;
        Assert.True(paused.StatusCode == HttpStatusCode.OK, await OperationsFailure(paused));
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(oldAuthority, await ReadPauseAuthority(h, administrator.User), receipt, suspend);
        Assert.Equal(pauseFirst ? 0 : 1, await scan);
        var committed = await ManualEffects(h, trigger);
        var nextDue = await NextDue();
        if (pauseFirst)
        {
            Assert.Equal(before, committed);
            Assert.Equal(next, nextDue);
        }
        else
        {
            Assert.Equal((1, 1, 1, 1, 1), (committed.Fires, committed.Instances, committed.Tasks, committed.Submissions, committed.ActiveTokens));
            Assert.NotNull(committed.Watermark);
            Assert.Equal(recovery ? next : WfCronHelper.NextUtc("0 9 * * *", now), nextDue);
            var fire = await database.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == trigger);
            Assert.Equal(idempotency, fire.IdempotencyKey);
            Assert.NotNull(fire.InstanceId);
            Assert.Null(fire.Error);
        }

        Assert.Equal(0, await timer.ScanTimersOnceAsync(now, default));
        Assert.Equal(committed, await ManualEffects(h, trigger));
        Assert.Equal(nextDue, await NextDue());
        if (pauseFirst)
        {
            var resume = new TenantManagementCommandV1("timer-trigger-resume", receipt.Snapshot.Revision, "reactivate", null);
            using var resumed = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"), resume));
            Assert.True(resumed.StatusCode == HttpStatusCode.OK, await OperationsFailure(resumed));
            Assert.Equal(1, await timer.ScanTimersOnceAsync(now, default));
            Assert.Equal(0, await timer.ScanTimersOnceAsync(now, default));
            var resumedFire = await database.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == trigger);
            Assert.Equal(idempotency, resumedFire.IdempotencyKey);
            Assert.NotNull(resumedFire.InstanceId);
            Assert.Equal(1, (await ManualEffects(h, trigger)).Instances);
        }

        Task<HttpResponseMessage> Pause() =>
            host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"), suspend));
        Task<DateTime?> NextDue() =>
            database.Wf_FlowTriggers.Where(t => t.Id == trigger).Select(t => t.NextDueUtc).SingleAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_legacy_OA_timer_workers_complete_one_due_slot()
    {
        await using var h = await Seed();
        var trigger = await SeedManualTrigger(h, timer: true);
        var now = new DateTime(2026, 11, 12, 17, 0, 0, DateTimeKind.Utc);
        var due = now.AddDays(-3);
        await h.Db.Wf_FlowTriggers.Where(t => t.Id == trigger).ExecuteUpdateAsync(t => t.SetProperty(t => t.NextDueUtc, due));
        await using var first = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        await using var second = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var services = new[] { first, second }.Select(db => new FlowTriggerService(db, SubFlowTestHarness.Engine(db)));
        var results = await Task.WhenAll(services.Select(service => service.ScanTimersOnceAsync(now, default)));
        Assert.Equal(1, results.Sum());
        var slot = await h.Db.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == trigger);
        Assert.Equal($"{trigger}:{due:O}", slot.IdempotencyKey);
        Assert.NotNull(slot.InstanceId);
        Assert.Equal(1, (await ManualEffects(h, trigger)).Instances);
        Assert.Equal(WfCronHelper.NextUtc("0 9 * * *", now),
            await h.Db.Wf_FlowTriggers.Where(t => t.Id == trigger).Select(t => t.NextDueUtc).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_legacy_OA_timer_failed_commit_keeps_slot_available()
    {
        await using var h = await Seed();
        var trigger = await SeedManualTrigger(h, timer: true);
        var now = new DateTime(2026, 11, 12, 17, 0, 0, DateTimeKind.Utc);
        var due = now.AddMinutes(-5);
        await h.Db.Wf_FlowTriggers.Where(t => t.Id == trigger).ExecuteUpdateAsync(t => t.SetProperty(t => t.NextDueUtc, due));
        var before = await ManualEffects(h, trigger);

        await using var workerDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var worker = new FlowTriggerService(workerDb, SubFlowTestHarness.Engine(workerDb));
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.cp6_test_timer_commit_reject() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'timer fixture commit rejected' USING ERRCODE='23514'; END $$;
            CREATE CONSTRAINT TRIGGER cp6_test_timer_commit_reject
            AFTER INSERT ON public."Wf_FlowHistory" DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW WHEN (NEW."Action" = 'submit') EXECUTE FUNCTION public.cp6_test_timer_commit_reject();
            """);
        try
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => worker.ScanTimersOnceAsync(now, default));
            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
            Assert.Equal(before, await ManualEffects(h, trigger));
            Assert.Equal(due, await h.Db.Wf_FlowTriggers.Where(t => t.Id == trigger).Select(t => t.NextDueUtc).SingleAsync());
        }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER cp6_test_timer_commit_reject ON public."Wf_FlowHistory";
                DROP FUNCTION public.cp6_test_timer_commit_reject();
                """);
        }

        Assert.Equal(1, await worker.ScanTimersOnceAsync(now, default));
        Assert.Equal(0, await worker.ScanTimersOnceAsync(now, default));
        var slot = await h.Db.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == trigger);
        Assert.Equal($"{trigger}:{due:O}", slot.IdempotencyKey);
        Assert.NotNull(slot.InstanceId);
        Assert.Equal(1, (await ManualEffects(h, trigger)).Instances);
    }
}
