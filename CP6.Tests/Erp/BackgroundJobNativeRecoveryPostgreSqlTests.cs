using System.Data.Common;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private NativeTimeoutOwnerV2 RemainderOwner(Harness h, NativeTimeoutOptionsV2 options, Func<DateTimeOffset> clock,
        PersonalFormOwnerV2? forms = null) =>
        new(h.Db, ScopeFence(h), options, forms ?? PersonalForm(h), Decisions(h)) { Clock = _ => Task.FromResult(clock()) };

    private static async Task<NativeTimeoutOptionsV2> RemainderRegistration(Harness h)
    {
        var options = new NativeTimeoutOptionsV2 { Enabled = true, ServiceId = Guid.NewGuid(), HostKey = "wp02-21b-isolated-host" };
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_native_timeout_v2.registrations(tenant_id,environment_id,service_id,host_key,enabled)
            VALUES({h.Tenant},{ScopeEnvironment},{options.ServiceId},{options.HostKey},true)
            """);
        return options;
    }

    private static Task<DateTimeOffset> RemainderDue(Harness h) => h.Db.Database.SqlQuery<DateTimeOffset>($"""
        SELECT max(due_at) AS "Value" FROM cp6_native_timeout_v2.deadlines WHERE tenant_id={h.Tenant}
        """).SingleAsync();

    private static void RemainderReminders(FlowSchema graph)
    {
        foreach (var node in graph.Nodes.Where(n => n.Type == "approval"))
        {
            node.TimeoutHours = 1;
            node.TimeoutAction = "remind";
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_native_unknown_is_partial_and_late_receipt_appends_to_original_attempt()
    {
        var fault = new CommitFault();
        await using var h = await Seed(fault);
        await TimeoutZone(h);
        var form = await NativeFormSetup(h, parallel: true, configure: RemainderReminders);
        var options = await RemainderRegistration(h);
        var now = (await RemainderDue(h)).AddSeconds(1);
        var owner = RemainderOwner(h, options, () => now, form.Owner);
        var inbox = NativeInbox(h, form.Owner);
        var claims = await owner.ClaimAsync();
        Assert.Equal(2, claims.Length);
        var committed = await owner.ExecuteAsync(claims[0]);
        fault.Armed = true;
        Assert.Equal("TIMEOUT_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(
            () => owner.ExecuteAsync(claims[1]))).Message);

        var partial = Assert.Single((await inbox.DetailAsync(form.Instance)).Timeout!.Runs);
        Assert.Equal("PARTIALLY_COMPLETED", partial.State);
        Assert.Equal((2, 1, 1, 0), (partial.CandidateCount, partial.AppliedCount, partial.UnknownCount, partial.InFlightCount));
        var unknown = partial.Units.Single(u => u.OperationId == claims[1].ExecutionId);
        Assert.Null(unknown.CommandId);
        var first = Assert.Single(unknown.Observations);
        Assert.Equal("UNKNOWN", first.State);
        var beforeReads = await RemainderNativeWriteSet(h);
        Assert.Equal((2, 2), beforeReads);
        Assert.Equal(partial.RunId, Assert.Single((await inbox.DetailAsync(form.Instance)).Timeout!.Runs).RunId);
        Assert.Equal(beforeReads, await RemainderNativeWriteSet(h));

        await using var recovery = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var resumedOwner = RemainderOwner(h with { Db = recovery }, options, () => now.AddSeconds(5));
        var decision = await resumedOwner.ResolveOriginalAsync(claims[1]);
        Assert.Equal(NativeTimeoutRecoveryActionV2.ReturnOriginal, decision.AllowedAction);
        var late = await resumedOwner.ResumeOriginalAsync(claims[1], decision.InputDigest);
        Assert.Equal(decision.Result, late);
        Assert.NotEqual(committed.CommandId, late.CommandId);

        var complete = Assert.Single((await inbox.DetailAsync(form.Instance)).Timeout!.Runs);
        Assert.Equal(partial.RunId, complete.RunId);
        Assert.Equal("COMPLETED_WITH_VERIFIED_RESULTS", complete.State);
        Assert.Equal(2, complete.AppliedCount);
        Assert.Equal(0, complete.UnknownCount);
        var restored = complete.Units.Single(u => u.OperationId == late.ExecutionId);
        Assert.Equal(unknown.AttemptId, restored.AttemptId);
        Assert.Equal(first, restored.Observations[0]);
        Assert.Equal("OWNER_VERIFIED", restored.Observations[1].State);
        Assert.True(restored.Observations[1].Sequence > first.Sequence);
        Assert.Equal(beforeReads, await RemainderNativeWriteSet(h));
        Assert.Empty(await owner.ClaimAsync());
        var zero = (await inbox.DetailAsync(form.Instance)).Timeout!.Runs.Single(r => r.CandidateCount == 0);
        Assert.Equal("COMPLETED_NO_CHANGES", zero.State);
        await DeviceEvidence("job-remainder-native-observations.json", new
        {
            Simulation = "模拟 OA 用户、并行任务、到期时间和第二个真实事务的提交确认丢失",
            Partial = partial, Completed = complete, ConfirmedZero = zero, NativeWrites = new { beforeReads.Item1, beforeReads.Item2 }
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_native_schedule_versions_freeze_calendar_timezone_and_bound_misfires()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        var options = await RemainderRegistration(h);
        var start = new DateTimeOffset(2026, 11, 16, 13, 0, 0, TimeSpan.Zero);
        var now = start;
        var localDay = DateTime.SpecifyKind(start.Date, DateTimeKind.Unspecified);
        h.Db.Sys_WorkCalendars.Add(new Sys_WorkCalendar { Date = localDay, IsWorkday = true });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var published = new NativeTimeoutScheduleChangeV2(Guid.NewGuid(), Guid.Empty, "模拟工作日调度发布",
            new(true, 3600, NativeTimeoutMissedRunV2.CatchUpBounded, 3, true, "America/New_York"));
        options.Schedule = published;
        var owner = RemainderOwner(h, options, () => now);
        Assert.Empty(await owner.ClaimAsync());
        await h.Db.Sys_WorkCalendars.Where(d => d.Date == localDay).ExecuteUpdateAsync(p => p.SetProperty(d => d.IsWorkday, false));

        now = start.AddHours(10);
        for (var i = 0; i < 5; i++) Assert.Empty(await owner.ClaimAsync());
        var slots = await h.Db.Database.SqlQuery<DateTimeOffset>($"""
            SELECT slot_at AS "Value" FROM cp6_native_timeout_v2.runs
            WHERE tenant_id={h.Tenant} AND schedule_version={published.OperationId} ORDER BY slot_at
            """).ToArrayAsync();
        Assert.Equal(new[] { start, start.AddHours(8), start.AddHours(9), start.AddHours(10) }, slots);

        options.Schedule = published with { Reason = "模拟修改相同操作的载荷" };
        Assert.Equal("TIMEOUT_SCHEDULE_OPERATION_CONFLICT",
            (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.ClaimAsync())).Message);
        options.Schedule = published with { OperationId = Guid.NewGuid() };
        Assert.Equal("TIMEOUT_SCHEDULE_VERSION_CHANGED",
            (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.ClaimAsync())).Message);

        var next = new NativeTimeoutScheduleChangeV2(Guid.NewGuid(), published.OperationId, "模拟换用新地区及日历",
            new(true, 3600, NativeTimeoutMissedRunV2.Skip, 1, true, "Asia/Tokyo"));
        options.Schedule = next;
        now = start.AddHours(11);
        Assert.Empty(await owner.ClaimAsync());
        now = start.AddDays(2).AddHours(2);
        Assert.Empty(await owner.ClaimAsync());
        var revisions = await h.Db.Database.SqlQuery<string>($"""
            SELECT (calendar_version||':'||zone_id||':'||zone_version) AS "Value" FROM cp6_native_timeout_v2.schedules
            WHERE tenant_id={h.Tenant} AND version IN ({published.OperationId},{next.OperationId}) ORDER BY sequence
            """).ToArrayAsync();
        Assert.Equal(2, revisions.Length);
        Assert.NotEqual(revisions[0].Split(':')[0], revisions[1].Split(':')[0]);
        Assert.Contains("America/New_York", revisions[0]);
        Assert.Contains("Asia/Tokyo", revisions[1]);
        Assert.NotEqual(revisions[0].Split(':')[2], revisions[1].Split(':')[2]);
        var nextSlots = await h.Db.Database.SqlQuery<DateTimeOffset>($"""
            SELECT slot_at AS "Value" FROM cp6_native_timeout_v2.runs
            WHERE tenant_id={h.Tenant} AND schedule_version={next.OperationId} ORDER BY slot_at
            """).ToArrayAsync();
        Assert.Equal(new[] { start.AddHours(11), now }, nextSlots);
        await DeviceEvidence("job-remainder-schedule.json", new { Simulation = "模拟宿主时钟、版本发布、日历修改与错过周期", slots, nextSlots, revisions });
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_native_disable_preserves_applied_and_blocks_inflight_with_no_new_scan()
    {
        var fault = new CommitFault();
        await using var h = await Seed(fault);
        await TimeoutZone(h);
        var form = await NativeFormSetup(h, parallel: true, configure: RemainderReminders);
        var options = await RemainderRegistration(h);
        var now = (await RemainderDue(h)).AddSeconds(1);
        var owner = RemainderOwner(h, options, () => now, form.Owner);
        var inbox = new NativeInboxOwnerV2(h.Db, ScopeFence(h), form.Owner, new(ToolApproval(h)), options, new());
        var claims = await owner.ClaimAsync();
        Assert.Equal(2, claims.Length);
        fault.Armed = true;
        Assert.Equal("TIMEOUT_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(
            () => owner.ExecuteAsync(claims[0]))).Message);
        var before = Assert.Single((await inbox.DetailAsync(form.Instance)).Timeout!.Runs);
        Assert.Equal(1, before.UnknownCount);
        Assert.Equal(1, before.InFlightCount);

        options.Schedule = new(Guid.NewGuid(), Guid.Empty, "模拟停发作业",
            new(false, 20, NativeTimeoutMissedRunV2.ContinueOriginal, 1, false, "UTC"));
        Assert.Empty(await owner.ClaimAsync());
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var worker = new NativeTimeoutWorkerV2(serviceProvider.GetRequiredService<IServiceScopeFactory>(), options,
            NullLogger<NativeTimeoutWorkerV2>.Instance);
        await worker.ProcessAsync(owner, claims[1], default);
        var after = (await inbox.DetailAsync(form.Instance)).Timeout!;
        Assert.False(after.Stages.Single(stage => stage.Layer == "CONFIG_ENABLED").Confirmed);
        var settled = Assert.Single(after.Runs);
        Assert.Equal(before.RunId, settled.RunId);
        Assert.Equal((1, 1, 0), (settled.AppliedCount, settled.BlockedCount, settled.UnknownCount));
        var original = await owner.ResolveOriginalAsync(claims[0]);
        Assert.Equal(NativeTimeoutRecoveryActionV2.ReturnOriginal, original.AllowedAction);
        Assert.Equal(original.Result, await owner.ResumeOriginalAsync(claims[0], original.InputDigest));
        var blocked = await owner.ResolveOriginalAsync(claims[1]);
        Assert.Equal(NativeTimeoutRecoveryActionV2.Blocked, blocked.AllowedAction);
        Assert.Equal("TIMEOUT_SCHEDULE_DISABLED", blocked.Reason);
        Assert.Equal((1, 1), await RemainderNativeWriteSet(h));
        await DeviceEvidence("job-remainder-disable.json", new
        {
            Simulation = "模拟两个并行任务、确认丢失和操作员停发", Before = before, After = settled, Original = original, Blocked = blocked
        });
    }

    [TenantManagementPostgresTheory]
    [InlineData("40001", 3, "RETRY_LIMIT_REACHED")]
    [InlineData("23514", 1, "PERMANENT_OR_CONFLICT")]
    public async Task JobRemainder_native_retries_only_transient_and_isolates_original_attempt(string sqlState, int calls, string reason)
    {
        var fault = new RemainderResultFault(sqlState);
        await using var h = await Seed(fault);
        await TimeoutZone(h);
        var form = await NativeFormSetup(h, configure: RemainderReminders);
        var options = await RemainderRegistration(h);
        var now = (await RemainderDue(h)).AddSeconds(1);
        var owner = RemainderOwner(h, options, () => now, form.Owner);
        var original = Assert.Single(await owner.ClaimAsync());
        fault.Armed = true;
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var worker = new NativeTimeoutWorkerV2(serviceProvider.GetRequiredService<IServiceScopeFactory>(), options,
            NullLogger<NativeTimeoutWorkerV2>.Instance);
        await worker.ProcessAsync(owner, original, default);
        Assert.Equal(calls, fault.Calls);
        Assert.Equal((0, 0), await RemainderNativeWriteSet(h));
        var unit = Assert.Single(Assert.Single((await NativeInbox(h, form.Owner).DetailAsync(form.Instance)).Timeout!.Runs).Units);
        Assert.Equal("FAILED_BEFORE_EFFECT", unit.State);
        Assert.Equal(original.ExecutionId, unit.OperationId);
        Assert.Equal(calls - 1, unit.Observations.Count(o => o.State == "RETRYING"));
        Assert.Equal(reason, unit.Observations.Last().Reason);
        now = now.AddMinutes(5);
        Assert.Empty(await owner.ClaimAsync());
        Assert.Equal(NativeTimeoutRecoveryActionV2.Isolate, (await owner.ResolveOriginalAsync(original)).AllowedAction);
        Assert.DoesNotContain((await NativeInbox(h, form.Owner).DetailAsync(form.Instance)).Timeout!.Runs, r => r.State == "COMPLETED_NO_CHANGES");
        await DeviceEvidence("job-remainder-retry-" + sqlState + ".json", new { Simulation = "模拟结果落库时 SQLSTATE，原 PostgreSQL 事务回滚", sqlState, calls, unit, Writes = new { Results = 0, Messages = 0 } });
    }

    private sealed class RemainderResultFault(string sqlState) : DbCommandInterceptor
    {
        internal bool Armed;
        internal int Calls;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("INSERT INTO cp6_native_timeout_v2.results", StringComparison.Ordinal))
            {
                Calls++;
                throw new PostgresException("模拟原结果存储失败", "ERROR", "ERROR", sqlState);
            }
            return ValueTask.FromResult(result);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_native_recovers_exact_input_and_cross_database_preserves_independent_history()
    {
        await using var h = await Seed();
        await TimeoutZone(h);
        var form = await NativeFormSetup(h, parallel: true, configure: RemainderReminders);
        var options = await RemainderRegistration(h);
        var now = (await RemainderDue(h)).AddSeconds(1);
        var owner = RemainderOwner(h, options, () => now, form.Owner);
        var claims = await owner.ClaimAsync();
        Assert.Equal(2, claims.Length);
        var original = claims[0];
        var before = await owner.ResolveOriginalAsync(original);
        Assert.Equal(NativeTimeoutRecoveryActionV2.ContinueOriginal, before.AllowedAction);
        Assert.Equal("NOT_APPLIED_PROVEN", before.OwnerOutcome);
        Assert.Equal(before.ExpectedRevision, original.Revision);
        Assert.Equal("TIMEOUT_ORIGINAL_INPUT_MISMATCH",
            (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => owner.ResumeOriginalAsync(
                original with { Occurrence = original.Occurrence + 1 }, before.InputDigest))).Message);
        var missing = await owner.ResolveOriginalAsync(original with { ExecutionId = Guid.NewGuid() });
        Assert.Equal("UNKNOWN", missing.OwnerOutcome);
        Assert.Equal(NativeTimeoutRecoveryActionV2.Isolate, missing.AllowedAction);
        now = now.AddSeconds(61);
        var response = await owner.ResumeOriginalAsync(original, before.InputDigest);
        Assert.Equal(original.ExecutionId, response.ExecutionId);
        Assert.Equal(original.Epoch + 1, response.ClaimEpoch);
        var recoveryRun = Assert.Single((await NativeInbox(h, form.Owner).DetailAsync(form.Instance)).Timeout!.Runs, r => r.IsRecovery);
        Assert.Equal(original.ExecutionId, Assert.Single(recoveryRun.Units).OperationId);
        Assert.Equal((1, 1), await RemainderNativeWriteSet(h));

        var dbName = "wp0221b_remote_" + Guid.NewGuid().ToString("N");
        Assert.Matches("^wp0221b_remote_[0-9a-f]{32}$", dbName);
        await h.Db.Database.ExecuteSqlRawAsync("CREATE DATABASE " + dbName);
        var connections = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { Database = dbName };
        await using var remote = PrivacyRestoreNativeV1.Context(connections.ConnectionString, h.Tenant);
        await remote.Database.ExecuteSqlRawAsync("CREATE TABLE local_owner_probe(id integer PRIMARY KEY)");
        var otherOwner = RemainderOwner(h, options, () => now, PersonalForm(h with { Db = remote }));
        var history = await otherOwner.ResolveOriginalAsync(original);
        Assert.Equal(NativeTimeoutRecoveryActionV2.ReturnOriginal, history.AllowedAction);
        Assert.Equal(response, history.Result);
        var dispatch = await otherOwner.ResolveOriginalAsync(claims[1]);
        Assert.Equal(NativeTimeoutRecoveryActionV2.Blocked, dispatch.AllowedAction);
        Assert.Equal("TIMEOUT_COMMON_AUTHORITY_UNAVAILABLE", dispatch.Reason);
        Assert.Equal(dispatch.Reason,
            (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => otherOwner.ResumeOriginalAsync(claims[1], dispatch.InputDigest))).Message);
        Assert.Equal((1, 1), await RemainderNativeWriteSet(h));
        Assert.Equal(0, await remote.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM local_owner_probe").SingleAsync());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_native_timeout_v2.registrations SET history_read=false WHERE tenant_id={h.Tenant} AND service_id={options.ServiceId}
            """);
        Assert.Equal(403, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => otherOwner.ResolveOriginalAsync(original))).Status);
        Assert.Equal((1, 1), await RemainderNativeWriteSet(h));
        await DeviceEvidence("job-remainder-original-recovery.json", new
        {
            Simulation = "模拟原业务身份、原键、到期后续领、两个本地 PostgreSQL 数据库及读取权撤销",
            before, response, missing, history, dispatch, OriginalEffects = 1, CrossDatabaseEffects = 0, RevokedReadStatus = 403
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_native_separates_already_terminal_and_blocked_without_counting_business_effects()
    {
        await using var h = await Seed();
        await TimeoutZone(h);
        var form = await NativeFormSetup(h, parallel: true, configure: RemainderReminders);
        var options = await RemainderRegistration(h);
        var now = (await RemainderDue(h)).AddSeconds(1);
        var owner = RemainderOwner(h, options, () => now, form.Owner);
        var claims = await owner.ClaimAsync();
        Assert.Equal(2, claims.Length);
        var human = await form.Owner.TaskContextAsync(claims[0].TaskId);
        await DecideNative(form.Owner, claims[0].TaskId, NativeVote(human, "模拟人工决定先提交", "approve"));
        var terminal = await owner.ExecuteAsync(claims[0]);
        Assert.Equal("SKIPPED_ALREADY_HANDLED", terminal.State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_native_timeout_v2.registrations SET enabled=false WHERE service_id={options.ServiceId}
            """);
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var worker = new NativeTimeoutWorkerV2(serviceProvider.GetRequiredService<IServiceScopeFactory>(), options,
            NullLogger<NativeTimeoutWorkerV2>.Instance);
        await worker.ProcessAsync(owner, claims[1], default);
        var run = Assert.Single((await NativeInbox(h, form.Owner).DetailAsync(form.Instance)).Timeout!.Runs);
        Assert.Equal("PARTIALLY_COMPLETED", run.State);
        Assert.Equal((2, 0, 1, 1, 0), (run.CandidateCount, run.AppliedCount, run.AlreadyTerminalCount, run.BlockedCount, run.UnknownCount));
        Assert.Equal((1, 0), await RemainderNativeWriteSet(h));
        Assert.Single((await NativeInbox(h, form.Owner).DetailAsync(form.Instance)).Votes);
        await DeviceEvidence("job-remainder-native-blocked.json", new
        {
            Simulation = "模拟人工先完成且另一尝试的 worker 当前授权被撤销", Run = run, NativeReceipts = 1, NativeEffects = 0
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_native_source_error_never_reports_confirmed_zero()
    {
        var fault = new CommandHook();
        await using var h = await Seed(fault);
        await TimeoutZone(h);
        var form = await NativeFormSetup(h, configure: RemainderReminders);
        var options = await RemainderRegistration(h);
        var now = (await RemainderDue(h)).AddSeconds(1);
        var owner = RemainderOwner(h, options, () => now, form.Owner);
        fault.Before = command =>
        {
            if (command.CommandText.Contains("ORDER BY p.next_due,p.task_id LIMIT 100", StringComparison.Ordinal))
                throw new PostgresException("模拟候选查询失败", "ERROR", "ERROR", "42P01");
            return Task.CompletedTask;
        };
        Assert.Equal("42P01", (await Assert.ThrowsAsync<PostgresException>(() => owner.ClaimAsync())).SqlState);
        fault.Before = null;
        var view = (await NativeInbox(h, form.Owner).DetailAsync(form.Instance)).Timeout!;
        Assert.Empty(view.Runs);
        Assert.Null(view.LastScan);
        Assert.Null(view.LastEffect);
        Assert.Equal((0, 0), await RemainderNativeWriteSet(h));
        Assert.Single(await owner.ClaimAsync());
        await DeviceEvidence("job-remainder-source-failure.json", new
        {
            Simulation = "模拟候选查询故障", Fault = "42P01", SuccessfulScansBeforeRecovery = 0,
            BusinessEffectsBeforeRecovery = 0, RealCandidateAfterRecovery = 1
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task JobRemainder_disable_keeps_unconfirmed_attempt_unknown_without_original_receipt()
    {
        var fault = new RemainderCommitUnknown();
        await using var h = await Seed(fault);
        await TimeoutZone(h);
        var form = await NativeFormSetup(h, configure: RemainderReminders);
        var options = await RemainderRegistration(h);
        var now = (await RemainderDue(h)).AddSeconds(1);
        var owner = RemainderOwner(h, options, () => now, form.Owner);
        var claim = Assert.Single(await owner.ClaimAsync());
        fault.Armed = true;
        Assert.Equal("TIMEOUT_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(
            () => owner.ExecuteAsync(claim))).Message);
        Assert.Equal((0, 0), await RemainderNativeWriteSet(h));

        options.Schedule = new(Guid.NewGuid(), Guid.Empty, "模拟提交待核期间停发",
            new(false, 20, NativeTimeoutMissedRunV2.ContinueOriginal, 1, false, "UTC"));
        Assert.Empty(await owner.ClaimAsync());
        var run = Assert.Single((await NativeInbox(h, form.Owner).DetailAsync(form.Instance)).Timeout!.Runs);
        Assert.Equal("UNKNOWN", run.State);
        Assert.Equal(1, run.UnknownCount);
        Assert.Equal(claim.ExecutionId, Assert.Single(run.Units).OperationId);
        Assert.Equal(NativeTimeoutRecoveryActionV2.Blocked, (await owner.ResolveOriginalAsync(claim)).AllowedAction);
        Assert.Equal((0, 0), await RemainderNativeWriteSet(h));
        await DeviceEvidence("job-remainder-disable-unknown.json", new
        {
            Simulation = "模拟原结果未获确认且独立查询无可核结果，随后停止发放",
            Run = run, OriginalReceipts = 0, OwnerEffects = 0
        });
    }

    private sealed class RemainderCommitUnknown : DbTransactionInterceptor
    {
        internal bool Armed;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                throw new IOException("模拟提交调用确认边界不明");
            }
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<(int, int)> RemainderNativeWriteSet(Harness h)
    {
        var count = await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_native_timeout_v2.results r
            JOIN cp6_native_timeout_v2.claims c ON c.execution_id=r.execution_id
            JOIN cp6_native_timeout_v2.deadlines d ON d.task_id=c.task_id AND d.revision=c.revision WHERE d.tenant_id={h.Tenant}
            """).SingleAsync();
        var messages = await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_personal_form_v2.outbox o
            JOIN cp6_personal_form_v2.commands c ON c.id=o.command_id WHERE c.tenant_id={h.Tenant} AND o.kind='TIMEOUT_REMINDER'
            """).SingleAsync();
        return (count, messages);
    }
}
