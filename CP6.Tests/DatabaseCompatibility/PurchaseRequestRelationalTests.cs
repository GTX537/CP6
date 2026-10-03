using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur;
using CP6.Core.Services.Pur.Contracts;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Pur;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit.Abstractions;

namespace CP6.Tests.DatabaseCompatibility;

[Collection(CoreBusinessRelationalCollection.Name)]
public sealed class PurchaseRequestRelationalTests(CoreBusinessRelationalFixture fixture, ITestOutputHelper output)
{
    [CoreBusinessFact]
    public async Task Concurrent_double_submit_returns_one_native_active_instance()
    {
        var scope = await SeedAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var leftGate = new SubmitSaveBarrier();
        var rightGate = new SubmitSaveBarrier();
        Task<PurchaseRequest>? left = null, right = null;
        var succeeded = false;
        try
        {
            async Task<PurchaseRequest> Submit(SubmitSaveBarrier barrier)
            {
                await using var db = fixture.CreateContext(scope.Tenant, barrier);
                return await Build(db).Service.SubmitForApprovalAsync(scope.PrNo, scope.Actor, scope.ActorName,
                    Permission(scope), budget.Token);
            }
            left = Submit(leftGate);
            right = Submit(rightGate);
            await Task.WhenAll(leftGate.Arrived.Task, rightGate.Arrived.Task).WaitAsync(budget.Token);
            Assert.Equal(leftGate.OriginalToken, rightGate.OriginalToken);
            Assert.Equal(8, leftGate.OriginalToken.Length);
            leftGate.Release();
            var first = await left;
            rightGate.Release();
            var second = await right;
            await using var verify = fixture.CreateContext(scope.Tenant);
            var instance = Assert.Single(await verify.Wf_FlowInstances.AsNoTracking().Where(row => row.BizType == "PUR_PR"
                && row.BizId == scope.PrNo && (row.Status == FlowInstanceStatus.Running || row.Status == FlowInstanceStatus.Suspended)).ToArrayAsync());
            var pr = await verify.PurchaseRequests.AsNoTracking().SingleAsync(row => row.PrNo == scope.PrNo);
            Assert.Equal(instance.Id.ToString(), first.ApprovalRef);
            Assert.Equal(instance.Id.ToString(), second.ApprovalRef);
            Assert.Equal(instance.Id.ToString(), pr.ApprovalRef);
            Assert.Equal(PrStatus.Submitted, pr.Status);
            Assert.Single(await verify.Wf_FlowTasks.Where(row => row.InstanceId == instance.Id && row.Status == FlowTaskStatus.Pending).ToArrayAsync());
            succeeded = true;
            output.WriteLine("Both actual submit scopes loaded the same 8-byte native token before either persisted; one committed instance won.");
        }
        finally
        {
            leftGate.Release(); rightGate.Release();
            if (!succeeded) await budget.CancelAsync();
            await JoinAsync([left, right], succeeded);
            await RemoveBindingAsync(scope);
        }
    }

    [CoreBusinessFact]
    public async Task Actual_flow_engine_dispatcher_callback_commits_purchase_and_workflow_together()
    {
        var scope = await SeedAsync();
        try
        {
            var submitted = await SubmitAsync(scope);
            await using (var db = fixture.CreateContext(scope.Tenant))
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await Build(db).Engine.ActAsync(submitted.Task, scope.Approver, true, "native approval");
                await transaction.CommitAsync();
            }
            await using var verify = fixture.CreateContext(scope.Tenant);
            var pr = await verify.PurchaseRequests.AsNoTracking().SingleAsync(row => row.PrNo == scope.PrNo);
            Assert.Equal(PrStatus.Approved, pr.Status);
            Assert.Equal(submitted.Instance.ToString(), pr.ApprovalRef);
            Assert.Equal(8, pr.RowVersion!.Length);
            Assert.Equal(FlowInstanceStatus.Approved, (await verify.Wf_FlowInstances.SingleAsync(row => row.Id == submitted.Instance)).Status);
            Assert.Equal(FlowTaskStatus.Approved, (await verify.Wf_FlowTasks.SingleAsync(row => row.Id == submitted.Task)).Status);
        }
        finally { await RemoveBindingAsync(scope); }
    }

    [CoreBusinessFact]
    public async Task Native_callback_write_failure_rolls_back_purchase_and_workflow_transaction()
    {
        var scope = await SeedAsync();
        var constraint = "CK_wp4_pr_callback_" + Guid.NewGuid().ToString("N");
        var installed = false;
        try
        {
            var submitted = await SubmitAsync(scope);
            byte[] originalToken;
            int originalHistory;
            await using (var before = fixture.CreateContext(scope.Tenant))
            {
                originalToken = (await before.PurchaseRequests.AsNoTracking().SingleAsync(row => row.PrNo == scope.PrNo)).RowVersion!.ToArray();
                originalHistory = await before.Wf_FlowHistories.CountAsync(row => row.InstanceId == submitted.Instance);
                var ddl = fixture.Database.Provider == DatabaseProvider.PostgreSql
                    ? $"ALTER TABLE \"Pur_PurchaseRequest\" ADD CONSTRAINT \"{constraint}\" CHECK (\"TenantId\" <> '{scope.Tenant:D}'::uuid OR \"PrNo\" <> '{scope.PrNo}' OR \"Status\" <> 2) NOT VALID"
                    : $"ALTER TABLE [dbo].[Pur_PurchaseRequest] WITH NOCHECK ADD CONSTRAINT [{constraint}] CHECK ([TenantId] <> '{scope.Tenant:D}' OR [PrNo] <> '{scope.PrNo}' OR [Status] <> 2)";
                await before.Database.ExecuteSqlRawAsync(ddl);
                installed = true;
            }
            await using (var db = fixture.CreateContext(scope.Tenant))
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => Build(db).Engine.ActAsync(submitted.Task, scope.Approver, true));
                var failure = DatabaseFailureClassifier.Classify(error);
                Assert.Equal(DatabaseFailureKind.CheckConstraint, failure.Kind);
                Assert.Equal(constraint, failure.ConstraintName);
                Assert.False(failure.CanRetryTransaction);
                if (fixture.Database.Provider == DatabaseProvider.PostgreSql) Assert.Equal("23514", failure.SqlState);
                else Assert.Equal(547, failure.DatabaseErrorCode);
                await transaction.RollbackAsync();
                output.WriteLine($"Actual native callback failure: provider={fixture.Database.Provider}; constraint={constraint}; kind={failure.Kind}.");
            }
            await using var verify = fixture.CreateContext(scope.Tenant);
            var pr = await verify.PurchaseRequests.AsNoTracking().SingleAsync(row => row.PrNo == scope.PrNo);
            Assert.Equal(PrStatus.Submitted, pr.Status);
            Assert.Equal(submitted.Instance.ToString(), pr.ApprovalRef);
            Assert.Equal(originalToken, pr.RowVersion);
            Assert.Equal(FlowInstanceStatus.Running, (await verify.Wf_FlowInstances.SingleAsync(row => row.Id == submitted.Instance)).Status);
            Assert.Equal(FlowTaskStatus.Pending, (await verify.Wf_FlowTasks.SingleAsync(row => row.Id == submitted.Task)).Status);
            Assert.Equal(originalHistory, await verify.Wf_FlowHistories.CountAsync(row => row.InstanceId == submitted.Instance));
        }
        finally
        {
            if (installed)
            {
                await using var cleanup = fixture.CreateContext(scope.Tenant);
                await cleanup.Database.ExecuteSqlRawAsync(fixture.Database.Provider == DatabaseProvider.PostgreSql
                    ? $"ALTER TABLE \"Pur_PurchaseRequest\" DROP CONSTRAINT \"{constraint}\""
                    : $"ALTER TABLE [dbo].[Pur_PurchaseRequest] DROP CONSTRAINT [{constraint}]");
                await using var native = new DatabaseConnectionFactory(fixture.Database).Create(fixture.ConnectionString);
                await native.OpenAsync();
                Assert.Equal(0, await native.QuerySingleAsync<int>(fixture.Database.Provider == DatabaseProvider.PostgreSql
                    ? "SELECT COUNT(*)::int FROM pg_constraint WHERE conname=@constraint"
                    : "SELECT COUNT(*) FROM sys.check_constraints WHERE name=@constraint", new { constraint }));
            }
            await RemoveBindingAsync(scope);
        }
    }

    [CoreBusinessFact]
    public async Task Old_callback_cannot_overwrite_actual_rejected_then_resubmitted_correlation()
    {
        var scope = await SeedAsync();
        try
        {
            var first = await SubmitAsync(scope);
            await using (var db = fixture.CreateContext(scope.Tenant))
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await Build(db).Engine.ActAsync(first.Task, scope.Approver, false, "resubmit");
                await transaction.CommitAsync();
            }
            var second = await SubmitAsync(scope);
            Assert.NotEqual(first.Instance, second.Instance);
            await using (var db = fixture.CreateContext(scope.Tenant))
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    Build(db).Service.ApproveFromApprovalAsync(scope.PrNo, first.Instance, scope.Approver.ToString()));
                Assert.Equal("E-PUR-061", error.Message);
                await transaction.RollbackAsync();
            }
            await using var verify = fixture.CreateContext(scope.Tenant);
            var pr = await verify.PurchaseRequests.AsNoTracking().SingleAsync(row => row.PrNo == scope.PrNo);
            Assert.Equal(PrStatus.Submitted, pr.Status);
            Assert.Equal(second.Instance.ToString(), pr.ApprovalRef);
            Assert.Equal(FlowInstanceStatus.Running, (await verify.Wf_FlowInstances.SingleAsync(row => row.Id == second.Instance)).Status);
        }
        finally { await RemoveBindingAsync(scope); }
    }

    [CoreBusinessFact]
    public async Task Callback_target_read_holds_native_lock_until_owning_workflow_transaction_finishes()
    {
        var scope = await SeedAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var barrier = new CallbackBarrier(budget.Token);
        var secondRead = new CallbackReadSignal(scope.PrNo);
        Task? first = null;
        Task<Exception?>? second = null;
        CP6Context? firstDb = null, secondDb = null;
        IDbContextTransaction? firstTransaction = null, secondTransaction = null;
        var succeeded = false;
        try
        {
            var submitted = await SubmitAsync(scope);
            firstDb = fixture.CreateContext(scope.Tenant);
            secondDb = fixture.CreateContext(scope.Tenant, secondRead);
            firstDb.Database.SetCommandTimeout(15); secondDb.Database.SetCommandTimeout(15);
            await firstDb.Database.OpenConnectionAsync(budget.Token);
            await secondDb.Database.OpenConnectionAsync(budget.Token);
            var firstSession = await SessionAsync(firstDb.Database.GetDbConnection(), budget.Token);
            var secondSession = await SessionAsync(secondDb.Database.GetDbConnection(), budget.Token);
            Assert.NotEqual(firstSession, secondSession);
            firstTransaction = await firstDb.Database.BeginTransactionAsync(budget.Token);
            secondTransaction = await secondDb.Database.BeginTransactionAsync(budget.Token);
            async Task Approve()
            {
                await Build(firstDb, callback => new PausedCallback(callback, barrier)).Engine.ActAsync(submitted.Task, scope.Approver, true);
                await firstTransaction.CommitAsync(budget.Token);
            }
            async Task<Exception?> Reject()
            {
                try
                {
                    await Build(secondDb).Engine.ActAsync(submitted.Task, scope.Approver, false, "competing callback");
                    await secondTransaction.CommitAsync(budget.Token);
                    return null;
                }
                catch (Exception error)
                {
                    await secondTransaction.RollbackAsync();
                    return error;
                }
            }
            first = Approve();
            await barrier.Arrived.Task.WaitAsync(budget.Token);
            second = Reject();
            await secondRead.Arrived.Task.WaitAsync(budget.Token);
            var blocked = await ObserveBlockingOrCompletionAsync(firstSession, secondSession, second, budget.Token);
            output.WriteLine($"Actual callback overlap: provider={fixture.Database.Provider}; differentSessions=true; nativeWaiterBlockedByFirst={blocked}; secondCompletedBeforeRelease={second.IsCompleted}.");
            Assert.True(blocked, "The competing callback finished before the owning workflow transaction released its target row lock.");
            Assert.False(second.IsCompleted);
            barrier.Release();
            await first;
            var rejected = await second;
            Assert.Equal("E-PUR-061", Assert.IsType<InvalidOperationException>(rejected).Message);
            await using var verify = fixture.CreateContext(scope.Tenant);
            Assert.Equal(PrStatus.Approved, (await verify.PurchaseRequests.AsNoTracking().SingleAsync(row => row.PrNo == scope.PrNo)).Status);
            Assert.Equal(FlowInstanceStatus.Approved, (await verify.Wf_FlowInstances.SingleAsync(row => row.Id == submitted.Instance)).Status);
            Assert.Equal(FlowTaskStatus.Approved, (await verify.Wf_FlowTasks.SingleAsync(row => row.Id == submitted.Task)).Status);
            succeeded = true;
        }
        finally
        {
            barrier.Release();
            if (!succeeded) await budget.CancelAsync();
            try { await JoinAsync([first, second], succeeded); }
            finally
            {
                if (secondTransaction is not null) await secondTransaction.DisposeAsync();
                if (firstTransaction is not null) await firstTransaction.DisposeAsync();
                if (secondDb is not null) await secondDb.DisposeAsync();
                if (firstDb is not null) await firstDb.DisposeAsync();
                await RemoveBindingAsync(scope);
            }
        }
    }

    private async Task<Scope> SeedAsync()
    {
        var tenant = Guid.NewGuid();
        var scope = new Scope(tenant, Guid.NewGuid(), Guid.NewGuid(), "w4pr" + Guid.NewGuid().ToString("N")[..16],
            "buyer-" + Guid.NewGuid().ToString("N")[..8], "wp4-pr-" + Guid.NewGuid().ToString("N"), Guid.NewGuid());
        await using var db = fixture.CreateContext(tenant);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = "wp4-pr-" + tenant.ToString("N"), TenantName = "WP4 purchase fixture", Enable = true });
        var schema = new FlowSchema
        {
            Nodes = { new FlowNode { Id = "approve", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = scope.Approver }, new FlowNode { Id = "end", Type = "end" } },
            Edges = { new FlowEdge { From = "approve", To = "end" } }
        };
        var head = new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = scope.FlowKey, FlowName = "WP4 purchase", FormKey = "", SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true };
        db.Wf_FlowDefs.Add(head);
        db.Wf_FlowDefVersions.Add(new Wf_FlowDefVersion { Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 1, Status = WfDefinitionVersionStatus.Published, FlowNameSnapshot = head.FlowName, SchemaJson = head.SchemaJson });
        db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding { Id = scope.Binding, BizType = "PUR_PR", FlowKey = head.FlowKey, Enable = true });
        db.PurchaseRequests.Add(new PurchaseRequest { PrNo = scope.PrNo, RequesterId = scope.ActorName, RequestDate = DateTime.UtcNow, Creator = scope.ActorName, Status = PrStatus.Draft, Source = PrSource.Manual });
        db.PurchaseRequestLines.Add(new PurchaseRequestLine { PrNo = scope.PrNo, LineNo = 1, ItemId = "WP4-ITEM", Qty = 2, EstPrice = 10, Status = 0, Creator = scope.ActorName });
        await db.SaveChangesAsync();
        output.WriteLine(fixture.SetupSummary);
        return scope;
    }

    private async Task<(Guid Instance, Guid Task)> SubmitAsync(Scope scope)
    {
        await using var db = fixture.CreateContext(scope.Tenant);
        var submitted = await Build(db).Service.SubmitForApprovalAsync(scope.PrNo, scope.Actor, scope.ActorName, Permission(scope));
        var instance = Guid.Parse(submitted.ApprovalRef!);
        var task = await db.Wf_FlowTasks.SingleAsync(row => row.InstanceId == instance && row.Status == FlowTaskStatus.Pending);
        return (instance, task.Id);
    }

    private static (PurchaseRequestService Service, FlowEngine Engine) Build(CP6Context db, Func<IApprovalCallback, IApprovalCallback>? decorate = null)
    {
        var holder = new ServiceHolder();
        IApprovalCallback callback = new PrApprovalCallback(holder);
        var engine = new FlowEngine(db, new ApproverResolver(db), dispatcher: new ApprovalDispatcher([decorate?.Invoke(callback) ?? callback]));
        var adapter = new ApprovalServiceAdapter(new ApprovalService(db, engine), db);
        var po = new PurchaseOrderService(db, new SupplierPriceService(db), new FxRateService(db), new SeqService(db), adapter);
        var service = new PurchaseRequestService(db, new SeqService(db), adapter, po, new DataScopeFilter(db));
        holder.Service = service;
        return (service, engine);
    }

    private async Task RemoveBindingAsync(Scope scope)
    {
        await using var db = fixture.CreateContext(scope.Tenant);
        var binding = await db.Wf_ApprovalBindings.SingleOrDefaultAsync(row => row.Id == scope.Binding);
        if (binding is not null) { db.Remove(binding); await db.SaveChangesAsync(); }
    }

    private static UserPermissionContext Permission(Scope scope) => new() { UserId = scope.Actor, UserName = scope.ActorName, DataScopes = { ["pur-pr"] = 5 } };
    private async Task<int> SessionAsync(DbConnection connection, CancellationToken token) => await connection.QuerySingleAsync<int>(new CommandDefinition(
        fixture.Database.Provider == DatabaseProvider.PostgreSql ? "SELECT pg_backend_pid()" : "SELECT @@SPID", cancellationToken: token));

    private async Task<bool> ObserveBlockingOrCompletionAsync(int blocker, int waiter, Task competing, CancellationToken token)
    {
        await using var observer = new DatabaseConnectionFactory(fixture.Database).Create(fixture.ConnectionString);
        await observer.OpenAsync(token);
        while (!competing.IsCompleted)
        {
            var blocked = await observer.QuerySingleAsync<bool>(new CommandDefinition(fixture.Database.Provider == DatabaseProvider.PostgreSql
                ? "SELECT @blocker = ANY(pg_blocking_pids(@waiter))"
                : "SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM sys.dm_exec_requests WHERE session_id=@waiter AND blocking_session_id=@blocker) THEN 1 ELSE 0 END AS bit)",
                new { blocker, waiter }, commandTimeout: 5, cancellationToken: token));
            if (blocked) return true;
            await Task.Delay(20, token); // Poll a real session blocking relationship; elapsed time alone never proves the lock.
        }
        return false;
    }

    private static async Task JoinAsync(Task?[] tasks, bool succeeded)
    {
        var started = tasks.Where(task => task is not null).Cast<Task>().ToArray();
        try { await Task.WhenAll(started); }
        catch when (!succeeded) { /* Preserve the main assertion after joining every worker. */ }
        Assert.All(started, task => Assert.True(task.IsCompleted));
    }

    private sealed record Scope(Guid Tenant, Guid Actor, Guid Approver, string PrNo, string ActorName, string FlowKey, Guid Binding);
    private sealed class ServiceHolder : IServiceProvider
    {
        public IPurchaseRequestService? Service { get; set; }
        public object? GetService(Type type) => type == typeof(IPurchaseRequestService) ? Service : null;
    }
    private sealed class SubmitSaveBarrier : SaveChangesInterceptor
    {
        private int entered;
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[] OriginalToken { get; private set; } = [];
        public void Release() => release.TrySetResult();
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is { } db && db.ChangeTracker.Entries<Wf_FlowInstance>().Any(entry => entry.State == EntityState.Added)
                && Interlocked.CompareExchange(ref entered, 1, 0) == 0)
            {
                OriginalToken = db.ChangeTracker.Entries<PurchaseRequest>().Single().Property(row => row.RowVersion).OriginalValue!.ToArray();
                Arrived.TrySetResult();
                await release.Task.WaitAsync(token);
            }
            return result;
        }
    }
    private sealed class CallbackBarrier(CancellationToken token)
    {
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => release.TrySetResult();
        public async Task PauseAsync() { Arrived.TrySetResult(); await release.Task.WaitAsync(token); }
    }
    private sealed class PausedCallback(IApprovalCallback inner, CallbackBarrier barrier) : IApprovalCallback
    {
        public string BizType => inner.BizType;
        public async Task OnApprovedAsync(ApprovalCallbackContext context) { await inner.OnApprovedAsync(context); await barrier.PauseAsync(); }
        public async Task OnRejectedAsync(ApprovalCallbackContext context) { await inner.OnRejectedAsync(context); await barrier.PauseAsync(); }
    }
    private sealed class CallbackReadSignal(string prNo) : DbCommandInterceptor
    {
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (command.CommandText.Contains("Pur_PurchaseRequest", StringComparison.Ordinal)
                && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, prNo))) Arrived.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
