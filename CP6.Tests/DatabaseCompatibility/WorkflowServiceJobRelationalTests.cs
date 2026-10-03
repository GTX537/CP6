using System.Collections.Concurrent;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace CP6.Tests.DatabaseCompatibility;

/// <summary>
/// Actual Core migrations and production job/flow services; the counting executor verifies
/// the database coordination boundary, without claiming an external connector acceptance.
/// </summary>
[Collection(CoreBusinessRelationalCollection.Name)]
public sealed class WorkflowServiceJobRelationalTests
{
    private static readonly DateTime T0 = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private readonly CoreBusinessRelationalFixture fixture;
    private readonly ITestOutputHelper output;

    public WorkflowServiceJobRelationalTests(CoreBusinessRelationalFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output = output;
    }

    [CoreBusinessFact]
    public async Task Due_job_success_persists_token_progress_and_succeeded_state()
    {
        var tenant = Guid.NewGuid();
        ParkedJob parked;
        await using (var seed = Context(tenant)) parked = await ParkAsync(seed, T0);
        var executor = new CountingExecutor(succeed: true);

        await using (var worker = Context(tenant))
            Assert.Equal(1, await Service(worker, executor).ScanOnceAsync(T0, "success-worker"));

        await using var verify = Context(tenant);
        var job = await verify.Wf_ServiceJobs.AsNoTracking().SingleAsync(x => x.Id == parked.JobId);
        Assert.Equal(1, executor.CallCount);
        Assert.Equal(parked.JobId, Assert.Single(executor.Executions).JobId);
        Assert.Equal(ServiceJobStatus.Succeeded, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(T0, job.CompletedAtUtc);
        AssertNativeVersionChanged(parked.RowVersion, job.RowVersion);
        Assert.False(await verify.Wf_FlowTokens.AnyAsync(x => x.InstanceId == parked.InstanceId
            && x.NodeId == "svc" && x.Status == FlowTokenStatus.Active));
        Assert.Equal(FlowInstanceStatus.Approved,
            (await verify.Wf_FlowInstances.SingleAsync(x => x.Id == parked.InstanceId)).Status);
        output.WriteLine(fixture.SetupSummary + "; production job success and token advancement persisted.");
    }

    [CoreBusinessFact]
    public async Task Failed_job_preserves_backoff_then_exhaustion_and_suspension()
    {
        var tenant = Guid.NewGuid();
        ParkedJob parked;
        await using (var seed = Context(tenant)) parked = await ParkAsync(seed, T0, maxAttempts: 2);
        var executor = new CountingExecutor(succeed: false);

        await using (var first = Context(tenant))
            Assert.Equal(1, await Service(first, executor).ScanOnceAsync(T0, "backoff-first"));
        byte[] firstVersion;
        await using (var verify = Context(tenant))
        {
            var job = await verify.Wf_ServiceJobs.AsNoTracking().SingleAsync(x => x.Id == parked.JobId);
            Assert.Equal(ServiceJobStatus.Pending, job.Status);
            Assert.Equal(1, job.AttemptCount);
            Assert.Equal(T0.AddSeconds(WfServiceJobService.BackoffBaseSec), job.NextAttemptAtUtc);
            Assert.Equal("boom", job.LastError);
            Assert.Null(job.CompletedAtUtc);
            AssertLeaseCleared(job);
            AssertNativeVersionChanged(parked.RowVersion, job.RowVersion);
            firstVersion = job.RowVersion!.ToArray();
        }

        // A fresh worker before the persisted due time must not repeat the side effect.
        await using (var early = Context(tenant))
            Assert.Equal(0, await Service(early, executor).ScanOnceAsync(T0.AddSeconds(29), "backoff-early"));
        Assert.Equal(1, executor.CallCount);

        var secondAttempt = T0.AddSeconds(31);
        await using (var second = Context(tenant))
            Assert.Equal(1, await Service(second, executor).ScanOnceAsync(secondAttempt, "backoff-second"));
        await using var final = Context(tenant);
        var exhausted = await final.Wf_ServiceJobs.AsNoTracking().SingleAsync(x => x.Id == parked.JobId);
        Assert.Equal(2, executor.CallCount);
        Assert.Equal(new[] { 1, 2 }, executor.Executions.Select(x => x.AttemptNo).ToArray());
        Assert.Equal(ServiceJobStatus.Failed, exhausted.Status);
        Assert.Equal(2, exhausted.AttemptCount);
        Assert.Equal(secondAttempt, exhausted.CompletedAtUtc);
        Assert.Equal("boom", exhausted.LastError);
        AssertNativeVersionChanged(firstVersion, exhausted.RowVersion);
        var instance = await final.Wf_FlowInstances.SingleAsync(x => x.Id == parked.InstanceId);
        Assert.Equal(FlowInstanceStatus.Suspended, instance.Status);
        using var vars = JsonDocument.Parse(instance.VarsJson);
        Assert.Equal("svc", vars.RootElement.GetProperty("wf").GetProperty("serviceError").GetProperty("nodeId").GetString());
        Assert.Equal("boom", vars.RootElement.GetProperty("wf").GetProperty("serviceError").GetProperty("message").GetString());
    }

    [CoreBusinessFact]
    public async Task Reaper_only_reclaims_expired_leases_without_burning_attempts()
    {
        var tenant = Guid.NewGuid();
        ParkedJob neverExecuted;
        ParkedJob previouslyExecuted;
        ParkedJob live;
        await using (var seed = Context(tenant))
        {
            neverExecuted = await ParkAsync(seed, T0.AddHours(1));
            previouslyExecuted = await ParkAsync(seed, T0.AddHours(1));
            live = await ParkAsync(seed, T0);
            foreach (var parked in new[] { neverExecuted, previouslyExecuted, live })
            {
                var job = await seed.Wf_ServiceJobs.SingleAsync(x => x.Id == parked.JobId);
                job.Status = ServiceJobStatus.Running;
                job.AttemptCount = parked.JobId == previouslyExecuted.JobId ? 1 : 0;
                job.LockedBy = parked.JobId == live.JobId ? "live-worker" : "dead-worker";
                job.LockedAtUtc = T0.AddMinutes(-10);
                job.LockExpiresAtUtc = parked.JobId == live.JobId ? T0.AddMinutes(5) : T0.AddMinutes(-1);
            }
            await seed.SaveChangesAsync();
        }
        var executor = new CountingExecutor(succeed: true);
        await using (var worker = Context(tenant))
            Assert.Equal(0, await Service(worker, executor).ScanOnceAsync(T0, "reaper"));

        await using var verify = Context(tenant);
        var reclaimedZero = await verify.Wf_ServiceJobs.AsNoTracking().SingleAsync(x => x.Id == neverExecuted.JobId);
        Assert.Equal(ServiceJobStatus.Pending, reclaimedZero.Status);
        Assert.Equal(0, reclaimedZero.AttemptCount);
        Assert.Equal(T0.AddHours(1), reclaimedZero.NextAttemptAtUtc);
        AssertLeaseCleared(reclaimedZero);
        var reclaimedOne = await verify.Wf_ServiceJobs.AsNoTracking().SingleAsync(x => x.Id == previouslyExecuted.JobId);
        Assert.Equal(ServiceJobStatus.Pending, reclaimedOne.Status);
        Assert.Equal(1, reclaimedOne.AttemptCount);
        AssertLeaseCleared(reclaimedOne);
        var untouched = await verify.Wf_ServiceJobs.AsNoTracking().SingleAsync(x => x.Id == live.JobId);
        Assert.Equal(ServiceJobStatus.Running, untouched.Status);
        Assert.Equal(0, untouched.AttemptCount);
        Assert.Equal("live-worker", untouched.LockedBy);
        Assert.Equal(T0.AddMinutes(5), untouched.LockExpiresAtUtc);
        Assert.Equal(0, executor.CallCount);
    }

    [CoreBusinessFact]
    public async Task Withdrawn_instance_cancels_due_job_before_executor_call()
    {
        var tenant = Guid.NewGuid();
        ParkedJob parked;
        await using (var seed = Context(tenant))
            parked = await ParkAsync(seed, T0, instanceStatus: FlowInstanceStatus.Withdrawn);
        var executor = new CountingExecutor(succeed: true);
        await using (var worker = Context(tenant))
            Assert.Equal(1, await Service(worker, executor).ScanOnceAsync(T0, "withdrawn-worker"));

        await using var verify = Context(tenant);
        var job = await verify.Wf_ServiceJobs.AsNoTracking().SingleAsync(x => x.Id == parked.JobId);
        Assert.Equal(0, executor.CallCount);
        Assert.Equal(ServiceJobStatus.Cancelled, job.Status);
        Assert.Equal(T0, job.CompletedAtUtc);
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(FlowInstanceStatus.Withdrawn,
            (await verify.Wf_FlowInstances.SingleAsync(x => x.Id == parked.InstanceId)).Status);
    }

    [CoreBusinessFact]
    public async Task Two_contexts_claiming_same_pending_version_call_executor_once()
    {
        var tenant = Guid.NewGuid();
        ParkedJob parked;
        await using (var seed = Context(tenant)) parked = await ParkAsync(seed, T0);
        var barrier = new ClaimBarrier(parked.JobId);
        await using var first = Context(tenant, new ClaimInterceptor(barrier));
        await using var second = Context(tenant, new ClaimInterceptor(barrier));
        Assert.NotEqual(first.ContextId, second.ContextId);
        var executor = new CountingExecutor(succeed: true);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var tasks = new List<Task<int>>();
        int[] processed;
        try
        {
            tasks.Add(Service(first, executor).ScanOnceAsync(T0, "claim-first", budget.Token));
            tasks.Add(Service(second, executor).ScanOnceAsync(T0, "claim-second", budget.Token));
            await barrier.BothArrived.WaitAsync(TimeSpan.FromSeconds(15), budget.Token);
            var versions = barrier.OriginalVersions.ToArray();
            Assert.Equal(2, versions.Length);
            Assert.Equal(parked.RowVersion, versions[0]);
            Assert.Equal(parked.RowVersion, versions[1]);
            Assert.Equal(0, executor.CallCount);
            Assert.All(tasks, task => Assert.False(task.IsCompleted));
            barrier.Release();
            processed = await Task.WhenAll(tasks).WaitAsync(budget.Token);
        }
        finally
        {
            barrier.Release();
            await budget.CancelAsync();
            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException) when (budget.IsCancellationRequested) { }
            Assert.All(tasks, task => Assert.True(task.IsCompleted));
        }

        Assert.Equal(new[] { 0, 1 }, processed.OrderBy(x => x).ToArray());
        Assert.Equal(1, barrier.NativeConcurrencyFailures);
        Assert.Equal(1, executor.CallCount);
        var execution = Assert.Single(executor.Executions);
        Assert.Equal(parked.JobId, execution.JobId);
        Assert.Equal(1, execution.AttemptNo);
        await using var verify = Context(tenant);
        var job = await verify.Wf_ServiceJobs.AsNoTracking().SingleAsync(x => x.Id == parked.JobId);
        Assert.Equal(ServiceJobStatus.Succeeded, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(T0, job.CompletedAtUtc);
        AssertNativeVersionChanged(parked.RowVersion, job.RowVersion);
        Assert.Equal(FlowInstanceStatus.Approved,
            (await verify.Wf_FlowInstances.SingleAsync(x => x.Id == parked.InstanceId)).Status);
        Assert.False(await verify.Wf_FlowTokens.AnyAsync(x => x.InstanceId == parked.InstanceId
            && x.Status == FlowTokenStatus.Active));
        output.WriteLine(fixture.SetupSummary + "; two native claims, one real optimistic conflict, one executor call; all workers joined.");
    }

    private CP6Context Context(Guid tenant, params IInterceptor[] interceptors)
    {
        var context = fixture.CreateContext(tenant, interceptors);
        context.Database.SetCommandTimeout(30);
        return context;
    }

    private static WfServiceJobService Service(CP6Context db, params IServiceTaskExecutor[] executors)
        => new(db, new FlowEngine(db, new ApproverResolver(db)), executors);

    private static async Task<ParkedJob> ParkAsync(CP6Context db, DateTime due, int maxAttempts = 4,
        int instanceStatus = FlowInstanceStatus.Running)
    {
        if (!await db.Sys_Tenants.AnyAsync(x => x.Id == db.CurrentTenantId))
            db.Sys_Tenants.Add(new Sys_Tenant
            {
                Id = db.CurrentTenantId, TenantCode = "wp4-job-" + db.CurrentTenantId.ToString("N"),
                TenantName = "WP4 workflow job fixture", Enable = true
            });
        var serviceNode = new FlowNode
        {
            Id = "svc", Type = "serviceTask", ServiceKind = ServiceKind.DataWriteback, ServiceActionName = "act"
        };
        var schema = new FlowSchema
        {
            Start = "start",
            Nodes = { new FlowNode { Id = "start", Type = "start" }, serviceNode, new FlowNode { Id = "end", Type = "end" } },
            Edges = { new FlowEdge { From = "start", To = "svc" }, new FlowEdge { From = "svc", To = "end" } }
        };
        var json = JsonSerializer.Serialize(schema);
        var head = new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "wp4-job-" + Guid.NewGuid().ToString("N"),
            FlowName = "WP4 relational service job", FormKey = "wp4-job-form", SchemaJson = json,
            Version = 1, Enable = true
        };
        var version = new Wf_FlowDefVersion
        {
            Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 1,
            Status = WfDefinitionVersionStatus.Published, FlowNameSnapshot = head.FlowName,
            SchemaJson = json, PublishedAtUtc = T0
        };
        var instance = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowKey = head.FlowKey, FlowDefVersionId = version.Id,
            StarterId = Guid.NewGuid(), Status = instanceStatus, CurrentNode = "svc", VarsJson = "{}"
        };
        db.Wf_FlowDefs.Add(head);
        db.Wf_FlowDefVersions.Add(version);
        db.Wf_FlowInstances.Add(instance);
        var token = new FlowEngine(db, new ApproverResolver(db)).SpawnToken(instance, serviceNode);
        var job = new Wf_ServiceJob
        {
            Id = Guid.NewGuid(), InstanceId = instance.Id, TokenId = token.Id, NodeId = serviceNode.Id,
            Kind = serviceNode.ServiceKind!, ActionRefJson = ServiceTaskActionRef.Snapshot(serviceNode),
            DueAtUtc = due, Status = ServiceJobStatus.Pending, AttemptCount = 0,
            MaxAttempts = maxAttempts, NextAttemptAtUtc = due, CreateDate = T0
        };
        db.Wf_ServiceJobs.Add(job);
        await db.SaveChangesAsync();
        Assert.NotNull(job.RowVersion);
        Assert.Equal(8, job.RowVersion.Length);
        return new(instance.Id, token.Id, job.Id, job.RowVersion.ToArray());
    }

    private static void AssertLeaseCleared(Wf_ServiceJob job)
    {
        Assert.Null(job.LockedBy);
        Assert.Null(job.LockedAtUtc);
        Assert.Null(job.LockExpiresAtUtc);
    }

    private static void AssertNativeVersionChanged(byte[] original, byte[]? current)
    {
        Assert.NotNull(current);
        Assert.Equal(8, current.Length);
        Assert.False(original.AsSpan().SequenceEqual(current));
    }

    private sealed record ParkedJob(Guid InstanceId, Guid TokenId, Guid JobId, byte[] RowVersion);
    private sealed record Execution(Guid JobId, int AttemptNo);

    private sealed class CountingExecutor(bool succeed) : IServiceTaskExecutor
    {
        private int calls;
        public string Key => "act";
        public string Kind => ServiceKind.DataWriteback;
        public bool VisibleInDesigner => true;
        public string DisplayName => "WP4 counting executor boundary";
        public int CallCount => Volatile.Read(ref calls);
        public ConcurrentQueue<Execution> Executions { get; } = new();

        public Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext context)
        {
            Interlocked.Increment(ref calls);
            Executions.Enqueue(new(context.JobId, context.AttemptNo));
            return Task.FromResult(succeed ? ServiceTaskResult.Ok() : ServiceTaskResult.Fail("boom"));
        }
    }

    private sealed class ClaimBarrier(Guid jobId)
    {
        private readonly TaskCompletionSource arrivals = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrived;
        private int conflicts;
        public Guid JobId => jobId;
        public Task BothArrived => arrivals.Task;
        public ConcurrentQueue<byte[]> OriginalVersions { get; } = new();
        public int NativeConcurrencyFailures => Volatile.Read(ref conflicts);
        public void Release() => release.TrySetResult();
        public void ObserveNativeConflict() => Interlocked.Increment(ref conflicts);

        public async Task ArriveAsync(byte[] originalVersion, CancellationToken cancellationToken)
        {
            OriginalVersions.Enqueue(originalVersion.ToArray());
            var count = Interlocked.Increment(ref arrived);
            Assert.InRange(count, 1, 2);
            if (count == 2) arrivals.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ClaimInterceptor(ClaimBarrier barrier) : SaveChangesInterceptor
    {
        private int intercepted;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var claim = eventData.Context?.ChangeTracker.Entries<Wf_ServiceJob>().SingleOrDefault(entry =>
                entry.Entity.Id == barrier.JobId && entry.State == EntityState.Modified
                && entry.Entity.Status == ServiceJobStatus.Running && entry.Entity.AttemptCount == 0
                && entry.Property(x => x.Status).OriginalValue == ServiceJobStatus.Pending);
            if (claim is not null && Interlocked.CompareExchange(ref intercepted, 1, 0) == 0)
            {
                var original = claim.Property(x => x.RowVersion).OriginalValue;
                Assert.NotNull(original);
                Assert.Equal(8, original.Length);
                await barrier.ArriveAsync(original, cancellationToken);
            }
            return result;
        }

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            barrier.ObserveNativeConflict();
            return ValueTask.FromResult(result);
        }
    }
}
