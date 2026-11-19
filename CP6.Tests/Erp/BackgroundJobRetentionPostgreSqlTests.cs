using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static JobRetentionSourceV1 RemainderSource() => new() { EnvironmentId = ScopeEnvironment, SourceId = Guid.NewGuid() };

    private static async Task<Guid> RemainderPolicy(Harness h, JobRetentionSourceV1 source, string dataClass, int days,
        bool enabled = true, long? approvedDatabaseOid = null)
    {
        var actual = await PrivacyRestoreNativeV1.Database(h.Db, default);
        var version = Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_job_retention_v1.policies(tenant_id,environment_id,data_class,version,source_id,basis_ref,enabled,
                database_name,database_oid,database_address,database_port,retention_days)
            VALUES({h.Tenant},{source.EnvironmentId},{dataClass},{version},{source.SourceId},{Guid.NewGuid()},{enabled},
                {actual.Name},{approvedDatabaseOid ?? actual.Oid},{actual.Address},{actual.Port},{days})
            """);
        return version;
    }

    private static Task<int> RemainderGuard(CP6Context db, Guid tenant, string dataClass, Guid item, string kind) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_job_retention_v1.safeguards(tenant_id,environment_id,data_class,entity_id,reference_id,kind,active)
            VALUES({tenant},{ScopeEnvironment},{dataClass},{item},{Guid.NewGuid()},{kind},true)
            """);

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_wfs_preserves_reservations_inflight_unknown_and_referenced_native_rows()
    {
        await using var h = await Seed();
        await using var peer = await Seed();
        await RegisterScope(h);
        var now = DateTimeOffset.UtcNow;
        var old = now.UtcDateTime.AddDays(-200);
        var cleanup = RemainderSource();
        await RemainderPolicy(h, cleanup, "WFS_SERVICE_JOB", 180);
        await RemainderPolicy(h, cleanup, "WFS_TRIGGER_FIRE", 180);
        var disposed = RemainderJob(old, ServiceJobStatus.Succeeded);
        var failed = RemainderJob(old, ServiceJobStatus.Failed);
        var cancelled = RemainderJob(old, ServiceJobStatus.Cancelled);
        var inFlight = RemainderJob(old, ServiceJobStatus.Running);
        var waiting = RemainderJob(old, ServiceJobStatus.Pending);
        var unknown = RemainderJob(old, ServiceJobStatus.Failed);
        var referenced = RemainderJob(old, ServiceJobStatus.Succeeded);
        var held = RemainderJob(old, ServiceJobStatus.Succeeded);
        var edge = RemainderJob(now.UtcDateTime.AddDays(-180), ServiceJobStatus.Succeeded);
        var reservation = RemainderFire(old, null, null);
        var newReservation = RemainderFire(now.UtcDateTime.AddDays(-1), null, null);
        var target = RemainderFire(old, Guid.NewGuid(), null);
        var error = RemainderFire(old, null, "SIMULATED-WF-ERROR");
        var processing = RemainderFire(old, Guid.NewGuid(), null);
        h.Db.Wf_ServiceJobs.AddRange(disposed, failed, cancelled, inFlight, waiting, unknown, referenced, held, edge);
        h.Db.Wf_TriggerFires.AddRange(reservation, newReservation, target, error, processing);
        var otherJob = RemainderJob(old, ServiceJobStatus.Succeeded);
        peer.Db.Wf_ServiceJobs.Add(otherJob);
        await peer.Db.SaveChangesAsync();
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await RemainderGuard(h.Db, h.Tenant, "WFS_SERVICE_JOB", unknown.Id, "UNKNOWN");
        await RemainderGuard(h.Db, h.Tenant, "WFS_SERVICE_JOB", referenced.Id, "REQUIRED_REFERENCE");
        await RemainderGuard(h.Db, h.Tenant, "WFS_SERVICE_JOB", held.Id, "HOLD");
        await RemainderGuard(h.Db, h.Tenant, "WFS_TRIGGER_FIRE", processing.Id, "PROCESSING");

        var paused = await new WfCleanupService(h.Db, new WfsInfraOptions { CleanupRetentionDays = 0 }, cleanup)
            .CleanupOnceAsync(now.UtcDateTime, default);
        Assert.Null(paused.ServiceJobsPlan);
        Assert.Equal(0, paused.ServiceJobsDeleted);
        var result = await new WfCleanupService(h.Db, new WfsInfraOptions(), cleanup).CleanupOnceAsync(now.UtcDateTime, default);
        Assert.Equal((3, 2, 1), (result.ServiceJobsDeleted, result.TriggerFiresDeleted, result.StaleReservationCount));
        Assert.Equal(new[] { inFlight.Id, waiting.Id, unknown.Id, referenced.Id, held.Id, edge.Id }.Order(),
            (await h.Db.Wf_ServiceJobs.Select(j => j.Id).ToArrayAsync()).Order());
        Assert.Equal(new[] { reservation.Id, newReservation.Id, processing.Id }.Order(),
            (await h.Db.Wf_TriggerFires.Select(j => j.Id).ToArrayAsync()).Order());
        Assert.True(await peer.Db.Wf_ServiceJobs.AnyAsync(j => j.Id == otherJob.Id));
        Assert.Equal("PARTIALLY_COMPLETED", result.ServiceJobsPlan!.State);
        Assert.Equal(3, Assert.Single(result.ServiceJobsPlan.Shards).Held);
        Assert.Equal(1, Assert.Single(result.TriggerFiresPlan!.Shards).Held);
        await DeviceEvidence("job-remainder-wfs-eligibility.json", new
        {
            Simulation = "模拟两租户、服务作业和触发占坑、保留依据和 Owner 保护引用",
            Actual = result, JobsKept = 6, FiresKept = 3, ForeignPreserved = true, DisabledDisposals = 0
        });
    }

    [TenantManagementPostgresTheory]
    [InlineData("hold")]
    [InlineData("expiry")]
    [InlineData("policy")]
    public async Task JobRemainder_retention_rechecks_current_hold_expiry_and_policy_after_preview(string change)
    {
        var hook = new RemainderAfterCommit();
        await using var h = await Seed(hook);
        await RegisterScope(h);
        var source = RemainderSource();
        var version = await RemainderPolicy(h, source, "WFS_SERVICE_JOB", 180);
        var now = DateTimeOffset.UtcNow;
        var clock = new RemainderClock(now);
        var row = RemainderJob(now.UtcDateTime.AddDays(-200), ServiceJobStatus.Succeeded);
        h.Db.Wf_ServiceJobs.Add(row);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        hook.Next = async () =>
        {
            await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            switch (change)
            {
                case "hold":
                    await other.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO cp6_privacy_disposition_v1.holds(environment_id,actor_tenant,target_tenant,subject_id,
                          version,active,basis_ref,review_at,owner_id)
                        VALUES({ScopeEnvironment},{h.Tenant},{h.Tenant},{Guid.NewGuid()},{Guid.NewGuid()},true,{Guid.NewGuid()},
                          {now.AddHours(4)},{h.User})
                        """);
                    break;
                case "expiry":
                    clock.Now = now.AddHours(2);
                    break;
                case "policy":
                    await RemainderPolicy(h with { Db = other }, source, "WFS_SERVICE_JOB", 180, false);
                    break;
            }
        };
        var result = await new JobRetentionV1(h.Db, source, clock).RunAsync(JobRetentionClassV1.WorkflowJob, now, 180, false, default);
        Assert.NotNull(result.PlanId);
        Assert.Equal(version, result.PolicyVersion);
        Assert.Equal("BLOCKED", result.State);
        Assert.Equal(0, result.Disposed);
        Assert.True(await h.Db.Wf_ServiceJobs.AnyAsync(j => j.Id == row.Id));
        if (change == "hold")
        {
            Assert.Equal(1, Assert.Single(result.Shards).Held);
            Assert.Equal(1, result.NextCursor);
        }
        else
        {
            Assert.Empty(result.Shards);
            Assert.Equal(change == "expiry" ? "RETENTION_PREVIEW_EXPIRED" : "RETENTION_POLICY_CHANGED", result.Reason);
            Assert.Equal(0, result.NextCursor);
        }
        await DeviceEvidence("job-remainder-retention-" + change + ".json", new
        {
            Simulation = "在真实计划事务提交后、原 Owner 分片开始前改变模拟条件",
            Change = change, Result = result, OriginalRows = 1, Disposed = 0
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_retention_first_shard_commits_then_original_cursor_recovers_second_and_lost_ack()
    {
        var hook = new RemainderAfterCommit();
        await using var h = await Seed(hook);
        await RegisterScope(h);
        var source = RemainderSource();
        await RemainderPolicy(h, source, "WFS_SERVICE_JOB", 180);
        var now = DateTimeOffset.UtcNow;
        var old = now.UtcDateTime.AddDays(-200);
        var first = Enumerable.Range(0, 500).Select(_ => RemainderJob(old, ServiceJobStatus.Succeeded)).ToArray();
        var last = RemainderJob(old, ServiceJobStatus.Succeeded);
        last.Id = Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffffe");
        h.Db.Wf_ServiceJobs.AddRange(first.Append(last));
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.wp0221b_fixture_reject_later_shard() RETURNS trigger LANGUAGE plpgsql AS $test$
            BEGIN RAISE EXCEPTION '模拟第二片失败' USING ERRCODE='23514'; END; $test$;
            CREATE TRIGGER wp0221b_fixture_reject_later_shard BEFORE DELETE ON public."Wf_ServiceJob"
                FOR EACH ROW WHEN (OLD."Id"='ffffffff-ffff-ffff-ffff-fffffffffffe'::uuid)
                EXECUTE FUNCTION public.wp0221b_fixture_reject_later_shard();
            """);
        var journal = new JobRetentionV1(h.Db, source);
        try
        {
            var error = await Assert.ThrowsAsync<PostgresException>(
                () => journal.RunAsync(JobRetentionClassV1.WorkflowJob, now, 180, false, default));
            Assert.Equal("23514", error.SqlState);
        }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER wp0221b_fixture_reject_later_shard ON public."Wf_ServiceJob";
                DROP FUNCTION public.wp0221b_fixture_reject_later_shard();
                """);
        }

        var plan = await h.Db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_job_retention_v1.plans WHERE tenant_id={h.Tenant} AND data_class='WFS_SERVICE_JOB'
            """).SingleAsync();
        await using var reader = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var independent = new JobRetentionV1(reader, source);
        var afterFailure = (await independent.ReadAsync(JobRetentionClassV1.WorkflowJob, plan, default))!;
        Assert.Equal("PARTIALLY_COMPLETED", afterFailure.State);
        Assert.Equal(500, afterFailure.NextCursor);
        var originalShard = Assert.Single(afterFailure.Shards);
        Assert.Equal(500, originalShard.Disposed);
        Assert.Equal(last.Id, await reader.Wf_ServiceJobs.Select(j => j.Id).SingleAsync());

        hook.Next = () => throw new IOException("模拟第二片提交确认丢失");
        var unknown = await Assert.ThrowsAsync<JobRetentionOutcomeUnknownV1>(
            () => journal.RunAsync(JobRetentionClassV1.WorkflowJob, now.AddMinutes(2), 180, true, default, plan));
        Assert.Equal(plan, unknown.PlanId);
        Assert.Equal(500, unknown.Cursor);
        var confirmed = (await independent.ReadAsync(JobRetentionClassV1.WorkflowJob, unknown.PlanId, default))!;
        Assert.Equal((501, 501), (confirmed.Disposed, confirmed.NextCursor));
        Assert.Equal(originalShard, confirmed.Shards[0]);
        var resumed = await independent.RunAsync(JobRetentionClassV1.WorkflowJob, now.AddMinutes(5), 180, true, default, plan);
        Assert.Equal("COMPLETED_WITH_VERIFIED_RESULTS", resumed.State);
        Assert.Equal(confirmed.Shards, resumed.Shards);
        Assert.False(await reader.Wf_ServiceJobs.AnyAsync());
        await DeviceEvidence("job-remainder-retention-shards.json", new
        {
            Simulation = "501 条模拟 WFS 终态，真数据库触发器拒绝第二片和真实提交后的模拟确认丢失",
            FirstShard = originalShard, Failure = afterFailure, UnknownPlan = unknown.PlanId, UnknownCursor = unknown.Cursor,
            Verified = confirmed, OriginalPlanResumed = true, FinalOwnerRows = 0
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_wms_host_keeps_business_events_and_refuses_unapproved_database_identity()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        var now = DateTimeOffset.UtcNow;
        var source = RemainderSource();
        var terminal = new MobileTask { MobileTaskNo = "SIM-TERM", TaskType = MobileTaskType.Move, Status = MobileTaskStatus.Completed, ExecutionVersion = 1 };
        var active = new MobileTask { MobileTaskNo = "SIM-ACTIVE", TaskType = MobileTaskType.Move, Status = MobileTaskStatus.InProgress, ExecutionVersion = 1 };
        h.Db.MobileTasks.AddRange(terminal, active);
        var expired = RemainderScan(terminal.MobileTaskNo, now.AddDays(-1));
        var stillValid = RemainderScan(terminal.MobileTaskNo, now.AddDays(1));
        var inFlight = RemainderScan(active.MobileTaskNo, now.AddDays(-1));
        var orphan = RemainderScan("SIM-UNKNOWN", now.AddDays(-1));
        var held = RemainderScan(terminal.MobileTaskNo, now.AddDays(-1));
        var business = new MobileTaskEvent
        {
            TaskNo = terminal.MobileTaskNo, EventType = "MobileTaskCompleted", OperationId = Guid.NewGuid(),
            ExecutionVersion = 1, OccurredAt = now.UtcDateTime, DataJson = "{\"scope\":\"SIMULATED\"}"
        };
        h.Db.MobileTaskScanLogs.AddRange(expired, stillValid, inFlight, orphan, held);
        h.Db.MobileTaskEvents.Add(business);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await RemainderGuard(h.Db, h.Tenant, "WMS_RAW_SCAN", held.Id, "REQUIRED_REFERENCE");

        var actual = await PrivacyRestoreNativeV1.Database(h.Db, default);
        await RemainderPolicy(h, source, "WMS_RAW_SCAN", 0, approvedDatabaseOid: actual.Oid + 1);
        var refused = await new JobRetentionV1(h.Db, source).RunAsync(JobRetentionClassV1.WmsRawScan, now, 0, false, default);
        Assert.Equal("RETENTION_SOURCE_OR_POLICY_UNAVAILABLE", refused.Reason);
        Assert.Null(refused.PlanId);
        Assert.Equal(5, await h.Db.MobileTaskScanLogs.CountAsync());

        var registered = await RemainderPolicy(h, source, "WMS_RAW_SCAN", 0);
        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(source);
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton<TimeProvider>(new RemainderClock(now));
            services.AddScoped<ITenantContext, TenantContext>();
            services.AddScoped<ITenantEnumerator, TenantEnumerator>();
            services.AddScoped(sp => server.Context(sp.GetRequiredService<ITenantContext>()));
            services.AddHostedService<WmsScanRetentionCleanupService>();
        }).Build();
        await host.StartAsync();
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (await h.Db.MobileTaskScanLogs.AnyAsync(s => s.Id == expired.Id))
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "实际 WMS 宿主未产生预期清理回执。");
                await Task.Delay(100);
            }
        }
        finally { await host.StopAsync(); }

        var planId = await h.Db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_job_retention_v1.plans WHERE tenant_id={h.Tenant} AND data_class='WMS_RAW_SCAN'
            """).SingleAsync();
        var actualResult = await new JobRetentionV1(h.Db, source).ReadAsync(JobRetentionClassV1.WmsRawScan, planId, default);
        Assert.NotNull(actualResult);
        Assert.Equal(registered, actualResult.PolicyVersion);
        Assert.Equal(1, actualResult.Disposed);
        Assert.Equal(1, Assert.Single(actualResult.Shards).Held);
        Assert.Equal(new[] { stillValid.Id, inFlight.Id, orphan.Id, held.Id }.Order(),
            (await h.Db.MobileTaskScanLogs.Select(s => s.Id).ToArrayAsync()).Order());
        var preserved = await h.Db.MobileTaskEvents.AsNoTracking().SingleAsync();
        Assert.Equal(business.Id, preserved.Id);
        Assert.Equal(business.OperationId, preserved.OperationId);
        Assert.Equal(business.DataJson, preserved.DataJson);
        await DeviceEvidence("job-remainder-wms-retention.json", new
        {
            Simulation = "模拟仓库任务、扫码、实际宿主时钟及未批准的数据库标识",
            Unapproved = refused, Approved = actualResult, RawPreserved = 4, RealHostedService = true, BusinessEventsPreserved = 1
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_pending_purpose_survives_ttl_and_current_read_revoke_until_exact_owner_terminal()
    {
        await using var h = await Seed();
        await TimeoutZone(h);
        var form = await NativeFormSetup(h, configure: RemainderReminders);
        var opts = await RemainderRegistration(h);
        var now = (await RemainderDue(h)).AddSeconds(1);
        var owner = RemainderOwner(h, opts, () => now, form.Owner);
        var original = Assert.Single(await owner.ClaimAsync());
        var purposeSource = RemainderSource();
        await RemainderPolicy(h, purposeSource, "WFS_SERVICE_JOB", 180);
        var anchor = DateTimeOffset.UtcNow;
        var underUse = RemainderJob(anchor.UtcDateTime.AddDays(-200), ServiceJobStatus.Succeeded);
        h.Db.Wf_ServiceJobs.Add(underUse);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var review = anchor.AddDays(-2);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_job_retention_v1.purpose_uses(tenant_id,environment_id,data_class,entity_id,operation_id,
              owner_key,input_digest,review_at)
            VALUES({h.Tenant},{ScopeEnvironment},'WFS_SERVICE_JOB',{underUse.Id},{original.ExecutionId},'OA_NATIVE_TIMEOUT',
              {NativeTimeoutOwnerV2.InputDigest(original)},{review})
            """);
        var inbox = NativeInbox(h, form.Owner);
        Assert.Equal(form.Instance, (await inbox.DetailAsync(form.Instance)).InstanceId);
        var journal = new JobRetentionV1(h.Db, purposeSource);
        var first = await journal.RunAsync(JobRetentionClassV1.WorkflowJob, anchor, 180, false, default);
        Assert.Equal("BLOCKED", first.State);
        Assert.Equal(1, first.PendingTerminal);
        Assert.Equal(0, first.Disposed);
        var originalPolicy = (await form.Owner.PolicyAsync("personal"))!;
        await form.Owner.ConfigureAsync("personal", new(Guid.NewGuid(), originalPolicy.Version.ToString("D"), true, true,
            [], originalPolicy.RetentionPolicyRef, 30, true, "模拟撤销读取资格"));
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => inbox.DetailAsync(form.Instance))).Status);
        var retained = await journal.RunAsync(JobRetentionClassV1.WorkflowJob, anchor.AddMinutes(1), 180, false, default);
        Assert.Equal(1, retained.PendingTerminal);
        Assert.Equal(0, retained.Disposed);
        Assert.True(await h.Db.Wf_ServiceJobs.AnyAsync(j => j.Id == underUse.Id));

        var result = await owner.ExecuteAsync(original);
        Assert.Equal("COMPLETED", result.State);
        var released = await journal.RunAsync(JobRetentionClassV1.WorkflowJob, anchor.AddMinutes(2), 180, false, default);
        Assert.Equal(0, released.PendingTerminal);
        Assert.Equal(1, released.Disposed);
        Assert.Equal(1, (await journal.ReadAsync(JobRetentionClassV1.WorkflowJob, first.PlanId!.Value, default))!.PendingTerminal);
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => inbox.DetailAsync(form.Instance))).Status);
        await DeviceEvidence("job-remainder-purpose.json", new
        {
            Simulation = "模拟用途占用、过期检查时间和撤销用户读取权，真实 OA Owner 提交原回执",
            First = first, AfterRevoke = retained, AfterTrustedTerminal = released, HistoricalReadRevoked = 404,
            OriginalOperation = result.ExecutionId, ActualOwnerReceipt = result.CommandId
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_retention_missing_hold_writer_blocks_only_new_effects()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        var source = RemainderSource();
        await RemainderPolicy(h, source, "WFS_SERVICE_JOB", 180);
        var now = DateTimeOffset.UtcNow;
        h.Db.Wf_ServiceJobs.Add(RemainderJob(now.UtcDateTime.AddDays(-200), ServiceJobStatus.Succeeded));
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var journal = new JobRetentionV1(h.Db, source);
        var done = await journal.RunAsync(JobRetentionClassV1.WorkflowJob, now, 180, false, default);
        Assert.Equal(1, done.Disposed);
        var original = done.PlanId!.Value;
        var candidate = RemainderJob(now.UtcDateTime.AddDays(-201), ServiceJobStatus.Cancelled);
        h.Db.Wf_ServiceJobs.Add(candidate);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_disposition_v1.holds DISABLE TRIGGER authority_gate");
        try
        {
            var blocked = await journal.RunAsync(JobRetentionClassV1.WorkflowJob, now.AddSeconds(1), 180, false, default);
            Assert.Equal("RETENTION_SOURCE_OR_POLICY_UNAVAILABLE", blocked.Reason);
            Assert.Null(blocked.PlanId);
            Assert.Equal(candidate.Id, await h.Db.Wf_ServiceJobs.Select(j => j.Id).SingleAsync());
            var history = await journal.ReadAsync(JobRetentionClassV1.WorkflowJob, original, default);
            Assert.NotNull(history);
            Assert.Equal(done.Shards, history.Shards);
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_privacy_disposition_v1.holds ENABLE TRIGGER authority_gate"); }
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_wfs_host_reopens_only_the_original_persisted_plan()
    {
        var hook = new RemainderAfterCommit();
        await using var h = await Seed(hook);
        await using var peer = await Seed();
        await RegisterScope(h);
        var source = RemainderSource();
        await RemainderPolicy(h, source, "WFS_SERVICE_JOB", 180);
        await RemainderPolicy(peer, source, "WFS_SERVICE_JOB", 180);
        var now = DateTimeOffset.UtcNow;
        var row = RemainderJob(now.UtcDateTime.AddDays(-200), ServiceJobStatus.Succeeded);
        var other = RemainderJob(now.UtcDateTime.AddDays(-200), ServiceJobStatus.Succeeded);
        h.Db.Wf_ServiceJobs.Add(row);
        peer.Db.Wf_ServiceJobs.Add(other);
        await h.Db.SaveChangesAsync();
        await peer.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        peer.Db.ChangeTracker.Clear();

        using var halted = new CancellationTokenSource();
        hook.Next = () => { halted.Cancel(); return Task.CompletedTask; };
        var journal = new JobRetentionV1(h.Db, source);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            journal.RunAsync(JobRetentionClassV1.WorkflowJob, now, 180, false, halted.Token));
        var original = await h.Db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_job_retention_v1.plans WHERE tenant_id={h.Tenant}
            """).SingleAsync();
        Assert.Equal("READY", (await journal.ReadAsync(JobRetentionClassV1.WorkflowJob, original, default))!.State);

        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(source);
            services.AddSingleton(new WfsInfraOptions());
            services.AddScoped<ITenantContext, TenantContext>();
            services.AddScoped<ITenantEnumerator, TenantEnumerator>();
            services.AddScoped(sp => server.Context(sp.GetRequiredService<ITenantContext>()));
            services.AddScoped<IWfCleanupService, WfCleanupService>();
            services.AddHostedService<WfServiceJobCleanupWorker>();
        }).Build();
        await host.StartAsync();
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (await h.Db.Wf_ServiceJobs.AnyAsync(j => j.Id == row.Id))
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "WFS 宿主未续跑原计划。");
                await Task.Delay(100);
            }
        }
        finally { await host.StopAsync(); }

        var completed = await journal.ReadAsync(JobRetentionClassV1.WorkflowJob, original, default);
        Assert.NotNull(completed);
        Assert.Equal(1, completed.Disposed);
        Assert.Equal(1, completed.NextCursor);
        Assert.Equal(other.Id, await peer.Db.Wf_ServiceJobs.Select(j => j.Id).SingleAsync());
        Assert.False(await peer.Db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_job_retention_v1.plans WHERE tenant_id={peer.Tenant}) AS "Value"
            """).SingleAsync());
        await DeviceEvidence("job-remainder-retention-host-resume.json", new
        {
            Simulation = "模拟在原计划提交后进程停止，随后启动实际 WFS 清理宿主",
            Original = original, Completed = completed, NewTenantPlans = 0
        });
    }

    private static Wf_TriggerFire RemainderFire(DateTime fired, Guid? instance, string? error) => new()
    {
        Id = Guid.NewGuid(), TriggerId = Guid.NewGuid(), IdempotencyKey = Guid.NewGuid().ToString("N"),
        FiredUtc = fired, InstanceId = instance, Error = error, Source = 2
    };

    private static MobileTaskScanLog RemainderScan(string task, DateTimeOffset expires) => new()
    {
        Id = Guid.NewGuid(), TaskNo = task, ClientScanNo = Guid.NewGuid().ToString("N"), ExecutionVersion = 1, Step = "Quantity",
        RawBarcode = "SIMULATED", DeviceId = "SIM-DEVICE", ScannedAt = expires.UtcDateTime.AddDays(-30), RetainUntil = expires.UtcDateTime
    };

    private sealed class RemainderAfterCommit : DbTransactionInterceptor
    {
        internal Func<Task>? Next;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            var action = Next;
            Next = null;
            return action?.Invoke() ?? Task.CompletedTask;
        }
    }

    private sealed class RemainderClock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            TimeProvider.System.CreateTimer(callback, state, dueTime == TimeSpan.FromMinutes(1) ? TimeSpan.FromMilliseconds(100) : dueTime, period);
    }
}
