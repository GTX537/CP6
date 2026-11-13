using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration.StockOutbox;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PGT011_timeout_retries_use_current_service_and_independent_tenant_scopes()
    {
        await using var a = await Seed();
        await using var b = await Seed();
        await using var admin = await TenantManagementSeed();
        await admin.Db.Sys_RoleActions.ExecuteDeleteAsync();
        var first = await PrepareTimeout(a);
        var second = await PrepareTimeout(b);
        var service = Guid.NewGuid();
        var claimAt = (first.Due > second.Due ? first.Due : second.Due).AddSeconds(1);
        var aOwner = await TimeoutOwner(a, first.Form, claimAt, service);
        var bOwner = await TimeoutOwner(b, second.Form, claimAt, service);
        var aClaim = Assert.Single(await aOwner.ClaimAsync());
        var bClaim = Assert.Single(await bOwner.ClaimAsync());
        var bBefore = await TimeoutState(b, bClaim);
        var active = await new TenantEnumerator(a.Db).ListActiveAsync();
        Assert.Contains(a.Tenant, active);
        Assert.Contains(b.Tenant, active);
        var scanOrder = new[] { a.Tenant, b.Tenant }.Where(active.Contains).ToArray();

        await using var api = await AuthenticationApiProcess.Start(a.Db.Database.GetConnectionString()!);
        var adminCookies = await Login(api, admin);
        var requesterCookies = await Login(api, a);
        var initial = await ReadTenantManagement(api, adminCookies, b.Tenant);
        Assert.True(initial.Enable);
        await AssertPlatformAdminCannotWritePrices(api, adminCookies, a, b);
        await AssertImpersonationCannotSuspend(api, adminCookies, a, b.Tenant, initial);

        adminCookies = await Login(api, admin);
        Assert.Equal(initial, await ReadTenantManagement(api, adminCookies, b.Tenant));
        const string pauseRef = "pgt-011-pause-b";
        using var pauseResponse = await api.Send(OperationsRequest(adminCookies, HttpMethod.Post,
            ManagementPath(b.Tenant, "/commands"), new TenantManagementCommandV1(pauseRef, initial.Revision, "suspend", null)));
        var paused = await NumberHttpData<TenantManagementReceiptV1>(pauseResponse);
        Assert.Equal(b.Tenant, paused.Snapshot.Id);
        Assert.False(paused.Snapshot.Enable);
        await a.Db.Sys_Users.Where(u => u.Id == a.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.Enable, false));
        using var revokedProfile = await api.Profile(requesterCookies["cp6_at"]);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedProfile.StatusCode);

        // The earlier scan list is deliberately retained across the pause. Both leases
        // are expired at the retry so only current execution authority can stop B.
        var retryAt = claimAt.AddSeconds(61);
        aOwner.Clock = _ => Task.FromResult(retryAt);
        var options = new NativeTimeoutOptionsV2 { Enabled = true, ServiceId = service, HostKey = "isolated-postgres-test-host" };
        var observed = new List<(ITenantContext Tenant, Guid DatabaseTenant, Guid DatabaseId, HttpContext? Request)>();
        var logger = new TimeoutScanLogger();
        using var services = new ServiceCollection()
            .AddHttpContextAccessor()
            .AddScoped<ITenantEnumerator>(_ => new TimeoutScanSnapshot(scanOrder))
            .AddScoped<ITenantContext, TenantContext>()
            .AddScoped(sp => server.Context(sp.GetRequiredService<ITenantContext>()))
            .AddScoped(sp =>
            {
                var tenant = sp.GetRequiredService<ITenantContext>();
                var db = sp.GetRequiredService<CP6Context>();
                observed.Add((tenant, db.CurrentTenantId, db.ContextId.InstanceId, sp.GetRequiredService<IHttpContextAccessor>().HttpContext));
                var scope = a with { Db = db, Tenant = tenant.CurrentTenantId, User = Guid.Empty, Http = new DefaultHttpContext() };
                var forms = PersonalForm(scope, _ => throw new InvalidOperationException("Background timeout used interactive identity"));
                return new NativeTimeoutOwnerV2(db, ScopeFence(scope), options, forms, Decisions(scope))
                { Clock = _ => Task.FromResult(retryAt) };
            })
            .BuildServiceProvider();
        using var worker = new NativeTimeoutWorkerV2(services.GetRequiredService<IServiceScopeFactory>(), options, logger);
        await worker.ScanOnceAsync();

        Assert.Equal(new[] { a.Tenant, b.Tenant }, observed.Select(scope => scope.DatabaseTenant));
        Assert.Equal(new[] { a.Tenant, b.Tenant }, observed.Select(scope => scope.Tenant.CurrentTenantId));
        Assert.NotSame(observed[0].Tenant, observed[1].Tenant);
        Assert.NotEqual(observed[0].DatabaseId, observed[1].DatabaseId);
        Assert.All(observed, scope => Assert.Null(scope.Request));
        Assert.Null(services.GetRequiredService<IHttpContextAccessor>().HttpContext);
        var denied = Assert.Single(logger.Errors);
        Assert.Contains(b.Tenant.ToString("D"), denied.Message);
        var denial = Assert.IsType<ScopeAuthorizationExceptionV1>(denied.Error);
        Assert.Equal(403, denial.Status);
        Assert.Equal("TIMEOUT_TENANT_DISABLED", denial.Message);

        var receipt = await aOwner.ExecuteAsync(aClaim);
        Assert.Equal(aClaim.ExecutionId, receipt.ExecutionId);
        Assert.Equal(aClaim.ExecutionId, receipt.CommandId);
        Assert.Equal(aClaim.Epoch + 1, receipt.ClaimEpoch);
        Assert.Equal(service, receipt.ServiceId);
        Assert.Equal("COMPLETED", receipt.State);
        var instance = await a.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(i => i.Id == first.Instance);
        Assert.Equal(FlowInstanceStatus.Approved, instance.Status);
        Assert.Equal(a.User, instance.StarterId);
        Assert.Equal(a.Tenant, instance.TenantId);
        var audit = await a.Db.Database.SqlQuery<TimeoutActorEvidence>($"""
            SELECT c.actor_id AS "ServiceActor", c.audit_json AS "Audit", original.actor_id AS "OriginalRequester"
            FROM cp6_personal_form_v2.commands c
            JOIN cp6_personal_form_v2.commands original ON original.root_id=c.root_id AND original.tenant_id=c.tenant_id
            WHERE c.id={receipt.CommandId} AND c.tenant_id={a.Tenant} AND c.completed
              AND original.operation={PersonalFormOwnerV2.Direct} AND original.completed
            """).SingleAsync();
        Assert.Equal(service, audit.ServiceActor);
        Assert.Equal(a.User, audit.OriginalRequester);
        Assert.NotEqual(audit.OriginalRequester, audit.ServiceActor);
        using var systemAudit = JsonDocument.Parse(audit.Audit);
        Assert.Equal("SYSTEM_EXECUTION", systemAudit.RootElement.GetProperty("evidenceKind").GetString());
        Assert.Equal(receipt.ExecutionId, systemAudit.RootElement.GetProperty("executionId").GetGuid());
        Assert.Equal(service, systemAudit.RootElement.GetProperty("serviceId").GetGuid());
        Assert.Equal(0, await a.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={first.Task}").SingleAsync());
        Assert.Equal(1, await a.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.outbox WHERE command_id={receipt.CommandId} AND kind='TIMEOUT_EXECUTED'").SingleAsync());

        await a.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_native_timeout_v2.registrations SET enabled=false WHERE tenant_id={a.Tenant} AND service_id={service}");
        var serviceDenied = await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => aOwner.ExecuteAsync(aClaim));
        Assert.Equal(403, serviceDenied.Status);
        Assert.Equal("TIMEOUT_SERVICE_NOT_AUTHORIZED", serviceDenied.Message);
        Assert.Equal(bBefore, await TimeoutState(b, bClaim));
        var bInstance = await b.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(i => i.Id == second.Instance);
        Assert.Equal(FlowInstanceStatus.Running, bInstance.Status);
        Assert.Equal(b.Tenant, bInstance.TenantId);
        Assert.Equal(b.User, bInstance.StarterId);
        Assert.Equal(FlowTaskStatus.Pending, await b.Db.Wf_FlowTasks.Where(t => t.Id == second.Task).Select(t => t.Status).SingleAsync());
        using var originalResponse = await api.Send(OperationsRequest(adminCookies, HttpMethod.Post,
            ManagementPath(b.Tenant, "/commands/read"), new { OperationRef = pauseRef }));
        Assert.Equal(paused, await NumberHttpData<TenantManagementReceiptV1>(originalResponse));
        Assert.Equal(admin.User, await b.Db.Database.SqlQuery<Guid>($"SELECT actor_id AS \"Value\" FROM cp6_tenant_management_v1.commands WHERE tenant_id={b.Tenant}").SingleAsync());
        output.WriteLine($"A {a.Tenant:D}: execution={receipt.ExecutionId:D} actor={service:D} originalRequester={a.User:D} epoch={receipt.ClaimEpoch}.");
        output.WriteLine($"B {b.Tenant:D}: execution={bClaim.ExecutionId:D} epoch={bClaim.Epoch} {denial.Message}; management receipt {paused.OperationRef} by {admin.User:D}.");
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT011_stock_technical_delivery_requires_its_current_service_authority()
    {
        await using var a = await Seed();
        await using var b = await Seed();
        var aStock = await GrantStocktakeDashboard(a);
        var bStock = await GrantStocktakeDashboard(b);
        await SeedInventory(a);
        await SeedInventory(b);
        var command = StockApply();
        var aMover = InventoryMover(a, aStock);
        await aMover.ApplyCommandAsync(command);
        var bCommand = StockApply();
        await InventoryMover(b, bStock).ApplyCommandAsync(bCommand);
        await a.Db.Sys_Users.Where(u => u.Id == a.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.Enable, false));
        await b.Db.Sys_Tenants.Where(t => t.Id == b.Tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.Enable, false));

        var publisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
        var options = StockHost();
        var worker = new StockOutboxWorkerV1(a.Db, options, publisher);
        await using var queue = StockQueue(a);
        var aAuthority = queue.Set<StockOutboxAuthorityV1>().Where(row => row.TenantId == a.Tenant);
        await aAuthority.ExecuteUpdateAsync(s => s.SetProperty(row => row.Enabled, false));
        var noAuthority = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => worker.RunAsync(a.Tenant, default));
        Assert.Equal("STOCK_OUTBOX_WRITE_NOT_AUTHORIZED", noAuthority.Message);
        Assert.Empty(publisher.Messages);
        await aAuthority.ExecuteUpdateAsync(s => s.SetProperty(row => row.Enabled, true));
        var delivered = await worker.RunAsync(a.Tenant, default);
        Assert.Equal("RESOLVED", delivered.State);
        Assert.Equal(1, delivered.Completed);
        Assert.Equal("IDLE", (await worker.RunAsync(a.Tenant, default)).State);
        var paused = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => new StockOutboxWorkerV1(b.Db, options, publisher).RunAsync(b.Tenant, default));
        Assert.Equal("STOCK_OUTBOX_CURRENT_AUTHORITY_REQUIRED", paused.Message);
        var sent = Assert.Single(publisher.Messages);
        Assert.Equal(a.Tenant, sent.TenantId);
        Assert.Equal(command.OperationId.ToString("D"), sent.AggregateId);
        Assert.Equal("{}"u8.ToArray(), sent.Payload.ToArray());
        var aRoot = StockOutboxProtocolV1.Root(a.Tenant).Id;
        var bRoot = StockOutboxProtocolV1.Root(b.Tenant).Id;
        Assert.NotNull((await queue.Set<StockOutboxRunV1>().SingleAsync(r => r.RootId == aRoot && r.RequestId == delivered.RequestId)).CompletedAtUtc);
        Assert.Single(await queue.Set<OutboxLeaseMutationReceiptV2>().Where(r => r.RootId == aRoot).ToArrayAsync());
        Assert.False(await queue.Set<StockOutboxRunV1>().AnyAsync(r => r.RootId == bRoot));
        Assert.Equal(Cp6OutboxStatus.Pending, (await queue.Set<Cp6OutboxMessage>().SingleAsync(n => n.TenantId == b.Tenant)).Status);
        Assert.Equal(1, await a.Db.StockTransactions.CountAsync());
        Assert.Equal(1, await b.Db.StockTransactions.CountAsync());
        output.WriteLine($"Technical Stock delivery: A original={command.OperationId:D} request={delivered.RequestId:D} actor={options.HostId}:stock-hint requester={a.User:D}; B original={bCommand.OperationId:D} pending.");
    }

    private async Task<(PersonalFormOwnerV2 Form, Guid Instance, Guid Task, DateTimeOffset Due)> PrepareTimeout(Harness h)
    {
        await TimeoutZone(h);
        var form = await NativeFormSetup(h, configure: graph =>
        {
            var node = graph.Nodes.Single(n => n.Id == "a");
            node.TimeoutHours = 1;
            node.TimeoutAction = "approve";
        });
        var due = await h.Db.Database.SqlQuery<DateTimeOffset>($"SELECT due_at AS \"Value\" FROM cp6_native_timeout_v2.deadlines WHERE task_id={form.Task}").SingleAsync();
        return (form.Owner, form.Instance, form.Task, due);
    }

    private static Task<string> TimeoutState(Harness h, NativeTimeoutClaimV2 claim) => h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('claim',to_jsonb(c),'pending',to_jsonb(p),'result',to_jsonb(r),
            'formCommand',to_jsonb(f),'scan',to_jsonb(o))::text AS "Value"
        FROM cp6_native_timeout_v2.claims c
        JOIN cp6_native_timeout_v2.pending p ON p.task_id=c.task_id
        LEFT JOIN cp6_native_timeout_v2.results r ON r.execution_id=c.execution_id
        LEFT JOIN cp6_personal_form_v2.commands f ON f.id=c.execution_id
        LEFT JOIN cp6_native_timeout_v2.observations o ON o.tenant_id={h.Tenant} AND o.environment_id={ScopeEnvironment} AND o.service_id=c.service_id
        WHERE c.execution_id={claim.ExecutionId}
        """).SingleAsync();

    private static async Task<TenantManagementSnapshotV1> ReadTenantManagement(AuthenticationApiProcess api, Dictionary<string, string> cookies, Guid tenant)
    {
        using var response = await api.Send(OperationsRequest(cookies, HttpMethod.Get, ManagementPath(tenant)));
        return await NumberHttpData<TenantManagementSnapshotV1>(response);
    }

    private static async Task AssertPlatformAdminCannotWritePrices(AuthenticationApiProcess api,
        Dictionary<string, string> cookies, Harness a, Harness b)
    {
        var batch = a.Request();
        batch.Items.AddRange(b.Request().Items);
        var aBefore = await ReadPrices(a);
        var bBefore = await ReadPrices(b);
        batch.Items[0].RowVersion = aBefore[0].RowVersion;
        batch.Items[1].RowVersion = bBefore[0].RowVersion;
        using var response = await api.Send(OperationsRequest(cookies, HttpMethod.Put, "api/orders/price-correction/batch", batch));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("erp-order-price-correction:correct", await response.Content.ReadAsStringAsync());
        Assert.Equal(aBefore, await ReadPrices(a));
        Assert.Equal(bBefore, await ReadPrices(b));
        Assert.Equal(0, await Intents(a));
        Assert.Equal(0, await Intents(b));
    }

    private sealed record PriceState(decimal? Price, decimal? Amount, int? ApprovalStatus, string Version)
    {
        public byte[] RowVersion => Convert.FromBase64String(Version);
    }

    private static async Task<PriceState[]> ReadPrices(Harness h)
    {
        var rows = await h.Db.OrderDetails.AsNoTracking().OrderBy(d => d.WebOrderDetailNo).ToArrayAsync();
        return rows.Select(d => new PriceState(d.IndividualUnitPrice, d.Amount, d.ApprovalStatus, Convert.ToBase64String(d.RowVersion!))).ToArray();
    }

    private static async Task AssertImpersonationCannotSuspend(AuthenticationApiProcess api, Dictionary<string, string> cookies,
        Harness target, Guid tenant, TenantManagementSnapshotV1 initial)
    {
        using var entered = await api.Send(OperationsRequest(cookies, HttpMethod.Post, "api/platform/impersonation/start",
            new { TenantId = target.Tenant, UserId = target.User, Reason = "PGT-011 current platform authority" }));
        Assert.True(entered.StatusCode == HttpStatusCode.OK, await OperationsFailure(entered));
        var result = (await entered.Content.ReadFromJsonAsync<ImpersonationStartResult>())!;
        Assert.Equal(target.Tenant, result.TenantId);
        Assert.Equal(target.User, result.UserId);
        var token = entered.Headers.GetValues("Set-Cookie").Single(header => header.StartsWith("cp6_at=", StringComparison.Ordinal))
            .Split(';', 2)[0]["cp6_at=".Length..];
        // Impersonation replaces the access token and leaves the browser's CSRF cookie in place.
        var simulated = new Dictionary<string, string> { ["cp6_at"] = token, ["cp6_csrf"] = cookies["cp6_csrf"] };
        using var profile = await api.Profile(simulated["cp6_at"]);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var legacy = await api.Send(OperationsRequest(simulated, HttpMethod.Post, $"api/platform/tenant/{tenant:D}/suspend"));
        Assert.Equal(HttpStatusCode.Forbidden, legacy.StatusCode);
        Assert.Contains("E-SEC-034", await legacy.Content.ReadAsStringAsync());
        using var versioned = await api.Send(OperationsRequest(simulated, HttpMethod.Post,
            ManagementPath(tenant, "/commands"), new TenantManagementCommandV1("pgt-011-denied-simulation", initial.Revision, "suspend", null)));
        Assert.Equal(HttpStatusCode.Unauthorized, versioned.StatusCode);
        Assert.Contains("REAUTH_REQUIRED", await versioned.Content.ReadAsStringAsync());
    }

    private sealed record TimeoutActorEvidence(Guid ServiceActor, string Audit, Guid OriginalRequester);

    private sealed class TimeoutScanSnapshot(IReadOnlyList<Guid> tenants) : ITenantEnumerator
    {
        public Task<IReadOnlyList<Guid>> ListActiveAsync(CancellationToken ct = default) => Task.FromResult(tenants);
    }

    private sealed class TimeoutScanLogger : ILogger<NativeTimeoutWorkerV2>
    {
        public List<(Exception? Error, string Message)> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Error;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) Errors.Add((exception, formatter(state, exception)));
        }
    }
}
