using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wf.Executors;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using CP6.Tests.Wf;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("escalate", true)]
    [InlineData("escalate", false)]
    [InlineData("reject", true)]
    [InlineData("reject", false)]
    public async Task PGT017_legacy_timeout_and_signed_pause_share_commit_order(string action, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var fixture = await PrepareLegacyTimeout(h, action);
        await using var host = await StartLegacyTimeoutApi(h);
        var manager = await Login(host, admin);
        var original = await SubmitLegacyTimeout(h, host, fixture);

        if (action == "escalate" && !pauseFirst)
        {
            // A later publication changes the policy for new starts. The accepted task retains version 1.
            var next = await LegacyDefinitionFixture.FlowDraft(h.Db, "budget-approve", "Later budget approval", "BudgetApproval",
                LegacyTimeoutSchema(h.User, "reject", null));
            var publication = await LegacyDefinitionFixture.FlowPublished(h.Db, "budget-approve", next.RowVersion, h.User);
            Assert.Equal(2, next.Version);
            Assert.NotEqual(fixture.Budget.Definition, publication.VersionId);
            h.Db.ChangeTracker.Clear();
        }

        var authority = await ReadPauseAuthority(h, admin.User);
        var command = new TenantManagementCommandV1($"timeout-{action}-{pauseFirst}", authority.Revision, "suspend", null);
        Task<HttpResponseMessage> Suspend() => ManageLegacyTimeout(host, manager, h.Tenant, command);

        await using var workers = CreateLegacyTimeoutWorkers();
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await using var blocker = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        await blocker.OpenAsync();

        Task<HttpResponseMessage> pausing;
        Task<int?> processing;
        FenceWait first, second;
        await using (var hold = await HoldUnderlayTable(blocker, pauseFirst ? "Sys_Tenants" : "Wf_Notification"))
        {
            if (pauseFirst)
            {
                pausing = Suspend();
                first = await WaitForFence(observer, blocker.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
                processing = ScanLegacyTimeout(workers, h.Tenant, original.Due.AddSeconds(1));
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, processing);
            }
            else
            {
                processing = ScanLegacyTimeout(workers, h.Tenant, original.Due.AddSeconds(1));
                first = await WaitForFence(observer, blocker.ProcessID, "%INSERT%Wf_Notification%", null, processing);
                pausing = Suspend();
                second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, pausing);
            }
            Assert.Equal(original.Writes, await ReadBudgetWrites(observer, h.Tenant));
            Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            await hold.RollbackAsync();
        }
        using var pausedResponse = await pausing;
        var paused = await NumberHttpData<TenantManagementReceiptV1>(pausedResponse);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), paused, command);
        Assert.Equal(pauseFirst ? 0 : 1, await processing);
        var afterPause = await ReadBudgetWrites(observer, h.Tenant);
        if (pauseFirst)
            Assert.Equal(original.Writes, afterPause);
        else
            AssertLegacyTimeoutOutcome(fixture, original, afterPause);

        var restart = new TenantManagementCommandV1($"timeout-resume-{action}-{pauseFirst}", paused.Snapshot.Revision, "reactivate", null);
        using var resumed = await ManageLegacyTimeout(host, manager, h.Tenant, restart);
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(resumed)).Snapshot.Enable);
        Assert.Equal(pauseFirst ? 1 : 0, await ScanLegacyTimeout(workers, h.Tenant, original.Due.AddSeconds(2)));
        var completed = await ReadBudgetWrites(observer, h.Tenant);
        AssertLegacyTimeoutOutcome(fixture, original, completed);
        Assert.Equal(0, await ScanLegacyTimeout(workers, h.Tenant, original.Due.AddMinutes(1)));
        Assert.Equal(completed, await ReadBudgetWrites(observer, h.Tenant));
        if (!pauseFirst) Assert.Equal(afterPause, completed);
        await DeviceEvidence($"legacy-timeout-{action}-{pauseFirst}.json", new
        {
            h.Tenant, action, pauseFirst, original.Instance, original.Task, original.Due, Published = fixture.Budget.Definition,
            First = first, Second = second, Pause = paused, Before = TimeoutWriteSet(original.Writes),
            Paused = TimeoutWriteSet(afterPause), Recovered = TimeoutWriteSet(completed)
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_legacy_timeout_final_expiry_rolls_back_the_original_budget_and_notification()
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var fixture = await PrepareLegacyTimeout(h, "reject");
        await using var host = await StartLegacyTimeoutApi(h);
        var original = await SubmitLegacyTimeout(h, host, fixture);
        var manager = await Login(host, admin);
        var current = await ReadTenantManagement(host, manager, h.Tenant);
        var future = DateTime.Now.AddSeconds(8);
        var deadline = new DateTime(future.Ticks - future.Ticks % 10);
        using var set = await ManageLegacyTimeout(host, manager, h.Tenant,
            new("timeout-expiry", current.Revision, "update", new(current.TenantName, deadline, current.Remark, current.TimeZoneId)));
        var configured = await NumberHttpData<TenantManagementReceiptV1>(set);
        Assert.Equal(deadline, configured.Snapshot.ExpireDate);

        await using var workers = CreateLegacyTimeoutWorkers();
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await using var blocker = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        await blocker.OpenAsync();
        Task<int?> processing;
        FenceWait wait;
        DateTime observed;
        await using (var hold = await HoldUnderlayTable(blocker, "Wf_Notification"))
        {
            processing = ScanLegacyTimeout(workers, h.Tenant, original.Due.AddSeconds(1));
            wait = await WaitForFence(observer, blocker.ProcessID, "%INSERT%Wf_Notification%", null, processing);
            Assert.True(wait.ObservedAt.LocalDateTime < deadline);
            Assert.Equal(original.Writes, await ReadBudgetWrites(observer, h.Tenant));
            await using var clock = new NpgsqlCommand("SELECT clock_timestamp()", observer);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            do
            {
                observed = ((DateTime)(await clock.ExecuteScalarAsync(timeout.Token))!).ToLocalTime();
                if (observed <= deadline) await Task.Delay(50, timeout.Token);
            } while (observed <= deadline);
            await hold.RollbackAsync();
        }
        var count = await processing;
        var after = await ReadBudgetWrites(observer, h.Tenant);
        await DeviceEvidence("legacy-timeout-expiry.json", new
        {
            h.Tenant, original.Instance, original.Task, Published = fixture.Budget.Definition,
            Deadline = deadline, Blocked = wait, ReleasedAfter = observed, Count = count,
            Before = TimeoutWriteSet(original.Writes), After = TimeoutWriteSet(after)
        });
        Assert.Equal(0, count);
        Assert.Equal(original.Writes, after);
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_legacy_timeout_error_edge_defers_webapi_under_the_original_job_identity()
    {
        await using var h = await Seed();
        var calls = new ConcurrentQueue<string>();
        var execution = new ConcurrentQueue<ServiceTaskContext>();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var connectorPort = Environment.GetEnvironmentVariable("CP6_LEGACY_TIMEOUT_CONNECTOR_PORT") ?? "0";
        builder.WebHost.UseUrls($"http://127.0.0.1:{connectorPort}");
        await using var connector = builder.Build();
        connector.MapPost("/observe", (HttpRequest request) =>
        {
            calls.Enqueue(request.Headers["Idempotency-Key"].ToString());
            return Results.Json(new { received = true });
        });
        await connector.StartAsync();
        var address = connector.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        h.Db.Wf_Connectors.Add(new() { Name = "legacy-timeout-test", BaseUrl = address, TimeoutSec = 5, Enabled = true });
        await h.Db.SaveChangesAsync();

        var fixture = await PrepareLegacyTimeout(h, "errorEdge");
        await using var host = await StartLegacyTimeoutApi(h);
        var original = await SubmitLegacyTimeout(h, host, fixture);
        await using var workers = CreateLegacyTimeoutWorkers(execution.Enqueue);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        Assert.Equal(1, await ScanLegacyTimeout(workers, h.Tenant, original.Due.AddSeconds(1)));
        var parked = await ReadBudgetWrites(observer, h.Tenant);
        await DeviceEvidence("legacy-timeout-connector-intent.json", new
        {
            h.Tenant, original.Instance, original.Task, Published = fixture.Budget.Definition,
            Calls = calls.ToArray(), Before = TimeoutWriteSet(original.Writes), Parked = TimeoutWriteSet(parked)
        });
        Assert.Empty(calls);
        Assert.Empty(execution);
        var job = Assert.Single(parked.Rows("jobs"));
        var jobId = AssetId(job);
        Assert.Equal(WfJobKind.DeferredWebApiStart, job.GetProperty("Kind").GetString());
        Assert.Equal(ServiceJobStatus.Pending, AssetInt(job, "Status"));
        Assert.Equal(0, AssetInt(job, "AttemptCount"));
        Assert.Equal(h.Tenant, AssetId(job, "TenantId"));
        Assert.Equal(original.Instance, AssetId(job, "InstanceId"));
        Assert.Equal(original.Token, AssetId(job, "TokenId"));
        Assert.Equal("connector", job.GetProperty("NodeId").GetString());
        Assert.Equal(FlowTaskStatus.Cancelled, AssetInt(parked.Row("tasks", original.Task), "Status"));
        Assert.True(parked.Row("tasks", original.Task).GetProperty("TimeoutHandled").GetBoolean());
        Assert.Equal("connector", parked.Row("tokens", original.Token).GetProperty("NodeId").GetString());
        Assert.Equal(FlowInstanceStatus.Running, AssetInt(parked.Row("workflows", original.Instance), "Status"));
        Assert.Single(parked.Rows("history"), row => row.GetProperty("Action").GetString() == "timeoutErrorEdge");

        var due = await h.Db.Wf_ServiceJobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.NextAttemptAtUtc).SingleAsync();
        Assert.Equal(1, await InLegacyTimeoutScope(workers, h.Tenant, (scope, ct) =>
            scope.GetRequiredService<IWfServiceJobService>().ScanOnceAsync(due.AddSeconds(1), "timeout-connector-fixture", ct)));
        Assert.Equal($"wf-service-job-{jobId}", Assert.Single(calls));
        var delivered = Assert.Single(execution);
        Assert.Equal(jobId, delivered.JobId);
        Assert.Equal(original.Instance, delivered.InstanceId);
        Assert.Equal(original.Token, delivered.TokenId);
        Assert.Equal(h.User, delivered.StarterId);
        Assert.Equal(h.User, delivered.ActorId);
        var complete = await ReadBudgetWrites(observer, h.Tenant);
        Assert.Equal(ServiceJobStatus.Succeeded, AssetInt(complete.Row("jobs", jobId), "Status"));
        Assert.Equal(1, AssetInt(complete.Row("jobs", jobId), "AttemptCount"));
        Assert.Equal(FlowTokenStatus.Consumed, AssetInt(complete.Row("tokens", original.Token), "Status"));
        Assert.Equal((int)BudgetVersionStatus.Approved, AssetInt(complete.Row("versions", fixture.Budget.Version), "Status"));
        Assert.Equal(0, await ScanLegacyTimeout(workers, h.Tenant, original.Due.AddMinutes(1)));
        Assert.Equal(complete, await ReadBudgetWrites(observer, h.Tenant));
        await DeviceEvidence("legacy-timeout-connector-completed.json", new
        {
            Job = jobId, Calls = calls.ToArray(), delivered.StarterId, delivered.ActorId, Complete = TimeoutWriteSet(complete)
        });
    }

    private sealed record LegacyTimeoutFixture(BudgetFixture Budget, string Action, Guid EscalatedTo);
    private sealed record LegacyTimeoutOriginal(Guid Instance, Guid Task, Guid Token, DateTime Due, BudgetWrites Writes);
    private static async Task<LegacyTimeoutFixture> PrepareLegacyTimeout(Harness h, string action)
    {
        var target = action == "escalate" ? await SeedW618Checker(h) : h.User;
        var budget = await SeedBudget(h, "version-submit", LegacyTimeoutSchema(h.User, action, action == "escalate" ? target : null));
        await GrantLegacyAuthorityAction(h, "fin-budget", "submit");
        await GrantLegacyAuthorityAction(h, "fin-budget", "view");
        return new(budget, action, target);
    }

    private static string LegacyTimeoutSchema(Guid assignee, string action, Guid? escalation)
    {
        var flow = new FlowSchema
        {
            Start = "start",
            Nodes =
            [
                new() { Id = "start", Type = "start" },
                new() { Id = "approval", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = assignee,
                    TimeoutHours = 1, TimeoutAction = action, EscalateTo = escalation },
                new() { Id = "end", Type = "end" },
            ],
            Edges = [new() { From = "start", To = "approval" }, new() { From = "approval", To = "end" }],
        };
        if (action == "errorEdge")
        {
            flow.Nodes.Add(new() { Id = "connector", Type = "serviceTask", ServiceKind = ServiceKind.WebApi,
                ServiceMode = ServiceMode.Sync, ServiceConnectorName = "legacy-timeout-test", ServicePath = "observe", ServiceHttpMethod = "POST" });
            flow.Edges.Add(new() { From = "approval", To = "connector", IsError = true });
            flow.Edges.Add(new() { From = "connector", To = "end" });
        }
        return JsonSerializer.Serialize(flow, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static Task<AuthenticationApiProcess> StartLegacyTimeoutApi(Harness h)
    {
        var settings = IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant);
        if (Environment.GetEnvironmentVariable("CP6_LEGACY_TIMEOUT_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        return AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, settings);
    }

    private static async Task<LegacyTimeoutOriginal> SubmitLegacyTimeout(Harness h, AuthenticationApiProcess host, LegacyTimeoutFixture fixture)
    {
        var cookies = await Login(host, h);
        using var response = await host.Send(BudgetRequest(cookies, BudgetWriteFor("version-submit", fixture.Budget)));
        await BudgetPayload(response);
        var version = await h.Db.Wf_FlowDefVersions.AsNoTracking().SingleAsync(v => v.Id == fixture.Budget.Definition);
        Assert.Equal(WfDefinitionVersionStatus.Published, version.Status);
        Assert.Equal(1, version.Version);
        Assert.Equal(h.Tenant, version.TenantId);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var original = await ReadBudgetWrites(observer, h.Tenant);
        var instance = Assert.Single(original.Rows("workflows"));
        Assert.Equal(fixture.Budget.Definition, AssetId(instance, "FlowDefVersionId"));
        var instanceId = AssetId(instance);
        var task = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t => t.InstanceId == instanceId);
        Assert.Equal(h.User, task.AssigneeId);
        Assert.Equal(FlowTaskStatus.Pending, task.Status);
        Assert.False(task.TimeoutHandled);
        Assert.Equal(task.Id, AssetId(Assert.Single(original.Rows("notifications")), "TaskId"));
        return new(task.InstanceId, task.Id, Assert.IsType<Guid>(task.TokenId), Assert.IsType<DateTime>(task.DueAt), original);
    }

    private static void AssertLegacyTimeoutOutcome(LegacyTimeoutFixture fixture, LegacyTimeoutOriginal original, BudgetWrites result)
    {
        var escalation = fixture.Action == "escalate";
        var task = Assert.Single(result.Rows("tasks"));
        Assert.Equal(original.Task, AssetId(task));
        Assert.Equal(original.Token, AssetId(task, "TokenId"));
        Assert.Equal(fixture.Budget.Tenant, AssetId(task, "TenantId"));
        Assert.Equal(fixture.EscalatedTo, AssetId(task, "AssigneeId"));
        Assert.True(task.GetProperty("TimeoutHandled").GetBoolean());
        var history = Assert.Single(result.Rows("history"), row => row.GetProperty("Action").GetString() == fixture.Action);
        Assert.Equal(escalation ? fixture.EscalatedTo : WfTimeoutService.SystemActor, AssetId(history, "ActorId"));
        var instance = result.Row("workflows", original.Instance);
        Assert.Equal(fixture.Budget.Definition, AssetId(instance, "FlowDefVersionId"));
        var sourceNotice = Assert.Single(original.Writes.Rows("notifications"));
        Assert.Equal(sourceNotice.GetRawText(), result.Row("notifications", AssetId(sourceNotice)).GetRawText());
        var notice = Assert.Single(result.Rows("notifications"), row => AssetId(row) != AssetId(sourceNotice));
        Assert.Equal(original.Instance, AssetId(notice, "InstanceId"));
        Assert.Equal(fixture.EscalatedTo, AssetId(notice, "UserId"));
        Assert.Equal(fixture.Budget.Tenant, AssetId(notice, "TenantId"));
        Assert.Equal(0, AssetInt(notice, "DispatchStatus"));
        Assert.Equal(original.Writes.Row("versions", fixture.Budget.ActiveVersion).GetRawText(),
            result.Row("versions", fixture.Budget.ActiveVersion).GetRawText());
        Assert.Empty(result.Rows("jobs"));
        var budget = result.Row("versions", fixture.Budget.Version);
        if (escalation)
        {
            Assert.Equal(FlowTaskStatus.Pending, AssetInt(task, "Status"));
            Assert.Equal(JsonValueKind.Null, task.GetProperty("DueAt").ValueKind);
            Assert.Equal(FlowTokenStatus.Active, AssetInt(result.Row("tokens", original.Token), "Status"));
            Assert.Equal(original.Writes.Row("workflows", original.Instance).GetRawText(), instance.GetRawText());
            Assert.Equal(original.Writes.Row("versions", fixture.Budget.Version).GetRawText(), budget.GetRawText());
            Assert.Equal(WfNotificationType.TodoCreated, AssetInt(notice, "Type"));
            Assert.Equal(original.Task, AssetId(notice, "TaskId"));
        }
        else
        {
            Assert.Equal(FlowTaskStatus.Rejected, AssetInt(task, "Status"));
            Assert.Equal(FlowTokenStatus.Cancelled, AssetInt(result.Row("tokens", original.Token), "Status"));
            Assert.Equal(FlowInstanceStatus.Rejected, AssetInt(instance, "Status"));
            Assert.Equal((int)BudgetVersionStatus.Rejected, AssetInt(budget, "Status"));
            Assert.Equal("超时自动驳回", budget.GetProperty("RejectReason").GetString());
            Assert.Equal(WfNotificationType.FlowRejected, AssetInt(notice, "Type"));
            Assert.Equal(JsonValueKind.Null, notice.GetProperty("TaskId").ValueKind);
        }
    }

    private ServiceProvider CreateLegacyTimeoutWorkers(Action<ServiceTaskContext>? observeExecution = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("wf-connector");
        return services.AddHttpContextAccessor()
            .AddScoped<ITenantContext, TenantContext>()
            .AddScoped<ITenantEnumerator, TenantEnumerator>()
            .AddScoped(sp => server.Context(sp.GetRequiredService<ITenantContext>()))
            .AddScoped<IPrefService, PrefService>()
            .AddScoped<INotificationService, NotificationService>()
            .AddScoped<IWfNotifier, PersistentWfNotifier>()
            .AddScoped<FinSequenceService>()
            .AddScoped<IBudgetService, BudgetService>()
            .AddScoped<IApprovalService, ApprovalService>()
            .AddScoped<IApprovalCallback, BudgetApprovalCallback>()
            .AddScoped<ApprovalDispatcher>()
            .AddScoped<TenantConnectorResolver>()
            .AddScoped<IServiceTaskExecutor>(sp =>
            {
                var executor = new WebApiExecutor(sp.GetRequiredService<TenantConnectorResolver>());
                return observeExecution is null ? executor : new ObservedTimeoutExecutor(executor, observeExecution);
            })
            .AddScoped<FlowEngine>(sp =>
            {
                var db = sp.GetRequiredService<CP6Context>();
                return new FlowEngine(db, new ApproverResolver(db), notifier: sp.GetRequiredService<IWfNotifier>(),
                    dispatcher: sp.GetRequiredService<ApprovalDispatcher>(), handlers:
                    [new StartNodeHandler(), new ApprovalNodeHandler(), new EndNodeHandler(), new ServiceTaskNodeHandler(sp.GetServices<IServiceTaskExecutor>())]);
            })
            .AddScoped<IFlowEngine>(sp => sp.GetRequiredService<FlowEngine>())
            .AddScoped<IWfTimeoutService, WfTimeoutService>()
            .AddScoped<IWfServiceJobService, WfServiceJobService>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private sealed class ObservedTimeoutExecutor(WebApiExecutor inner, Action<ServiceTaskContext> observe) : IServiceTaskExecutor
    {
        public string Key => inner.Key;
        public string Kind => inner.Kind;
        public bool VisibleInDesigner => inner.VisibleInDesigner;
        public string DisplayName => inner.DisplayName;
        public Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext context)
        {
            observe(context);
            return inner.ExecuteAsync(context);
        }
    }

    private static Task<int?> ScanLegacyTimeout(ServiceProvider workers, Guid tenant, DateTime now) =>
        InLegacyTimeoutScope(workers, tenant, (scope, ct) => scope.GetRequiredService<IWfTimeoutService>().ScanOnceAsync(now, ct));

    private static async Task<int?> InLegacyTimeoutScope(ServiceProvider workers, Guid tenant, Func<IServiceProvider, CancellationToken, Task<int>> action)
    {
        int? count = null;
        ExceptionDispatchInfo? failure = null;
        await TenantScopeRunner.ForEachTenantAsync(workers.GetRequiredService<IServiceScopeFactory>(), async (scope, id, ct) =>
        {
            if (id != tenant) return;
            try
            {
                Assert.Null(scope.GetRequiredService<IHttpContextAccessor>().HttpContext);
                Assert.Equal(tenant, scope.GetRequiredService<CP6Context>().CurrentTenantId);
                count = await action(scope, ct);
            }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        });
        failure?.Throw();
        return count;
    }

    private static Task<HttpResponseMessage> ManageLegacyTimeout(AuthenticationApiProcess host, Dictionary<string, string> manager,
        Guid tenant, TenantManagementCommandV1 command) => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"), command));

    private static JsonElement TimeoutWriteSet(BudgetWrites rows) => JsonSerializer.Deserialize<JsonElement>(rows.Data);
}
