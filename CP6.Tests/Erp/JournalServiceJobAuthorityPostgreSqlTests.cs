using System.Runtime.ExceptionServices;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_journal_service_timer_and_pause_share_native_commit_order(bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var checker = await SeedW618Checker(h);
        await PrepareW618Finance(h);
        await using var host = await StartW618Api(h);
        var maker = await Login(host, h);
        var actor = await Login(host, W618CheckerName, W618CheckerPassword, h.Tenant.ToString("N"));
        var manager = await Login(host, admin);
        var fixture = await SeedW618JournalAndWorkflow(h, host, maker, checker, starter: h.User, deferred: true);
        using (var decision = await DecideW618(host, actor, fixture.Task))
            await CheckW618DecisionReceipt(decision, fixture);

        var authority = await ReadPauseAuthority(h, admin.User);
        var command = new TenantManagementCommandV1("pgt017-service-timer-pause", authority.Revision, "suspend", null);
        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(h.Tenant, "/commands"), command));
        var connection = h.Db.Database.GetConnectionString()!;
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var original = await ReadW618(observer, h.Tenant);
        var job = await h.Db.Wf_ServiceJobs.AsNoTracking().SingleAsync(j => j.InstanceId == fixture.Instance);
        Assert.Equal(ServiceKind.Timer, job.Kind);
        Assert.Equal(ServiceJobStatus.Pending, job.Status);
        Assert.Equal((int)JournalStatus.PendingReview, AssetInt(original.Row("journals", fixture.Journal), "Status"));
        await using var workers = CreateJournalTimerWorkers();
        var due = job.NextAttemptAtUtc.AddSeconds(1);

        await using var blocker = new NpgsqlConnection(connection);
        await blocker.OpenAsync();
        await using var hold = await blocker.BeginTransactionAsync();
        await using (var sql = new NpgsqlCommand(pauseFirst
            ? """LOCK TABLE public."Sys_Tenants" IN SHARE MODE"""
            : """LOCK TABLE public."Wf_Notification" IN SHARE MODE""", blocker, hold))
        {
            await sql.ExecuteNonQueryAsync();
        }

        Task<HttpResponseMessage> pausing;
        Task<int?> running;
        FenceWait firstWait;
        FenceWait rootWait;
        if (pauseFirst)
        {
            pausing = Pause();
            firstWait = await WaitForFence(observer, blocker.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
            running = ScanJournalTimer(workers, h.Tenant, due);
            // The claim UPDATE acquires the Main root through the existing workflow trigger.
            rootWait = await WaitForFence(observer, firstWait.Backend, "%UPDATE%Wf_ServiceJob%", null, running);
        }
        else
        {
            running = ScanJournalTimer(workers, h.Tenant, due);
            firstWait = await WaitForFence(observer, blocker.ProcessID, "%INSERT%Wf_Notification%", null, running);
            pausing = Pause();
            rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire%", null, pausing);
        }

        var inFlight = await ReadW618(observer, h.Tenant);
        AssertJournalTimerBusinessUnchanged(original, inFlight);
        Assert.Equal(pauseFirst ? ServiceJobStatus.Pending : ServiceJobStatus.Running,
            AssetInt(inFlight.Row("jobs", job.Id), "Status"));
        var currentAuthority = await ReadPauseAuthority(h, admin.User);
        Assert.True(currentAuthority.RootRevision >= authority.RootRevision);
        Assert.Equal(authority, currentAuthority with { RootRevision = authority.RootRevision });

        await hold.RollbackAsync();
        using var pausedResponse = await pausing.WaitAsync(TimeSpan.FromSeconds(15));
        var paused = await NumberHttpData<TenantManagementReceiptV1>(pausedResponse);
        var disabled = await ReadPauseAuthority(h, admin.User);
        CheckPause(authority, disabled, paused, command);
        Assert.Equal(pauseFirst ? 0 : 1, await running.WaitAsync(TimeSpan.FromSeconds(15)));
        var pausedRows = await ReadW618(observer, h.Tenant);
        if (pauseFirst)
        {
            AssertJournalTimerBusinessUnchanged(original, pausedRows);
            Assert.Equal(ServiceJobStatus.Running, AssetInt(pausedRows.Row("jobs", job.Id), "Status"));
        }
        else
        {
            AssertJournalTimerSucceeded(pausedRows, fixture, job.Id, checker);
        }
        Assert.Null(await ScanJournalTimer(workers, h.Tenant, due.AddMinutes(10)));

        var resume = new TenantManagementCommandV1("pgt017-service-timer-resume", paused.Snapshot.Revision, "reactivate", null);
        using var resumedResponse = await host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(h.Tenant, "/commands"), resume));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(resumedResponse)).Snapshot.Enable);
        var reaped = due.AddMinutes(10);
        Assert.Equal(pauseFirst ? 1 : 0, await ScanJournalTimer(workers, h.Tenant, reaped));
        var finished = await ReadW618(observer, h.Tenant);
        AssertJournalTimerSucceeded(finished, fixture, job.Id, checker);
        Assert.Equal(0, await ScanJournalTimer(workers, h.Tenant, reaped.AddMinutes(10)));
        Assert.Equal(finished, await ReadW618(observer, h.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new { PauseFirst = pauseFirst, h.Tenant, fixture.Journal, fixture.Instance,
            Job = job.Id, FirstWait = firstWait, RootWait = rootWait, OriginalAuthority = authority, PausedAuthority = disabled,
            Paused = JsonSerializer.Deserialize<JsonElement>(pausedRows.Data),
            Recovered = JsonSerializer.Deserialize<JsonElement>(finished.Data) }));
    }

    private static void AssertJournalTimerBusinessUnchanged(AssetRows before, AssetRows after)
    {
        foreach (var table in new[] { "journals", "lines", "periods", "numbers", "instances", "tasks",
            "history", "tokens", "form-tos", "notifications", "audit" })
        {
            Assert.Equal(before.Rows(table).Select(row => row.GetRawText()), after.Rows(table).Select(row => row.GetRawText()));
        }
    }

    private static void AssertJournalTimerSucceeded(AssetRows rows, W618Fixture fixture, Guid job, Guid checker)
    {
        var journal = Assert.Single(rows.Rows("journals"));
        Assert.Equal(fixture.Journal, AssetId(journal));
        Assert.Equal((int)JournalStatus.Posted, AssetInt(journal, "Status"));
        Assert.Equal(checker.ToString("D"), journal.GetProperty("CheckerId").GetString());
        Assert.Equal(2, rows.Rows("lines").Length);
        Assert.Equal(FlowInstanceStatus.Approved, AssetInt(rows.Row("instances", fixture.Instance), "Status"));
        Assert.Equal(FlowTaskStatus.Approved, AssetInt(rows.Row("tasks", fixture.Task), "Status"));
        var notification = Assert.Single(rows.Rows("notifications"));
        Assert.Equal(WfNotificationType.FlowApproved, AssetInt(notification, "Type"));
        Assert.Equal(fixture.Instance, AssetId(notification, "InstanceId"));
        var work = Assert.Single(rows.Rows("jobs"));
        Assert.Equal(job, AssetId(work));
        Assert.Equal(ServiceJobStatus.Succeeded, AssetInt(work, "Status"));
        Assert.Equal(FlowTokenStatus.Consumed, AssetInt(rows.Row("tokens", AssetId(work, "TokenId")), "Status"));
    }

    private ServiceProvider CreateJournalTimerWorkers()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantEnumerator, TenantEnumerator>();
        services.AddScoped(sp => server.Context(sp.GetRequiredService<ITenantContext>()));
        services.AddScoped<IPrefService, PrefService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IWfNotifier, PersistentWfNotifier>();
        services.AddScoped<IJournalEntryService>(sp =>
        {
            var db = sp.GetRequiredService<CP6Context>();
            return new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        });
        services.AddScoped<IApprovalCallback, JournalApprovalCallback>();
        services.AddScoped<ApprovalDispatcher>();
        services.AddScoped(sp =>
        {
            var db = sp.GetRequiredService<CP6Context>();
            return new FlowEngine(db, new ApproverResolver(db), notifier: sp.GetRequiredService<IWfNotifier>(),
                dispatcher: sp.GetRequiredService<ApprovalDispatcher>());
        });
        services.AddScoped<IWfServiceJobService, WfServiceJobService>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task<int?> ScanJournalTimer(ServiceProvider workers, Guid tenant, DateTime now)
    {
        int? processed = null;
        ExceptionDispatchInfo? failure = null;
        await TenantScopeRunner.ForEachTenantAsync(workers.GetRequiredService<IServiceScopeFactory>(), async (scope, id, ct) =>
        {
            if (id != tenant) return;
            try
            {
                Assert.Equal(tenant, scope.GetRequiredService<CP6Context>().CurrentTenantId);
                processed = await scope.GetRequiredService<IWfServiceJobService>().ScanOnceAsync(now, "journal-timer-test", ct);
            }
            catch (Exception error)
            {
                failure = ExceptionDispatchInfo.Capture(error);
            }
        });
        failure?.Throw();
        return processed;
    }
}
