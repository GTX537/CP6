using System.Collections.Concurrent;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ApplicationExecutionContext =
    CP6.Space.Application.ISpaceExecutionContext;

namespace CP6.Tests.Space;

public sealed class SpaceProcessingJobWorkerTests
{
    [Fact]
    public void Registration_adds_publish_and_processing_workers_once()
    {
        var services = new ServiceCollection();

        services.AddSpaceJobWorkers();
        services.AddSpaceJobWorkers();

        var hostedTypes = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();
        Assert.Equal(2, hostedTypes.Length);
        Assert.Contains(typeof(SpacePublishJobWorker), hostedTypes);
        Assert.Contains(typeof(SpaceProcessingJobWorker), hostedTypes);
    }

    [Fact]
    public void Worker_catalogs_are_disjoint_and_cover_registered_processors()
    {
        var processing = SpaceProcessingJobWorker.JobTypes.ToArray();
        var publishing = SpacePublishJobWorker.JobTypes.ToArray();
        Assert.Empty(processing.Intersect(publishing));

        SpaceJobType[] expected =
        [
            SpaceJobType.Import,
            SpaceJobType.CadParse,
            SpaceJobType.ExcelPreview,
            SpaceJobType.Validate,
            SpaceJobType.BuildScene,
            SpaceJobType.Publish,
            SpaceJobType.Reconcile,
            SpaceJobType.ApplyGeneration,
            SpaceJobType.AiRetentionCleanup,
            SpaceJobType.HistoricalRepublish,
            SpaceJobType.ExcelCadMatch,
            SpaceJobType.ExcelCadApply,
        ];
        Assert.Equal(
            expected.OrderBy(value => value),
            processing.Concat(publishing).OrderBy(value => value));
    }

    [Fact]
    public async Task ProcessOnce_claims_each_processing_type_once_per_tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var provider = BuildProvider(tenantA, tenantB);
        var worker = new SpaceProcessingJobWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SpaceProcessingJobWorker>.Instance);

        var processed = await worker.ProcessOnceAsync();

        var records = provider.GetRequiredService<RecordingState>()
            .Records.ToArray();
        Assert.Equal(
            SpaceProcessingJobWorker.JobTypes.Count * 2,
            processed);
        Assert.Equal(processed, records.Length);
        foreach (var tenantId in new[] { tenantA, tenantB })
        {
            var tenantRecords = records
                .Where(record => record.TenantId == tenantId)
                .ToArray();
            Assert.Equal(
                SpaceProcessingJobWorker.JobTypes,
                tenantRecords.Select(record => record.JobType));
            Assert.All(tenantRecords, record =>
            {
                Assert.Equal(tenantId, record.ScopedTenantId);
                Assert.Equal(tenantId, record.ApplicationTenantId);
                Assert.Equal(
                    SpaceExecutionContext.SystemActor,
                    record.ActorType);
                Assert.Equal(
                    SpaceProcessingJobWorker.WorkerActorName,
                    record.ActorName);
                Assert.Equal(
                    SpaceProcessingJobWorker.WorkerActorId,
                    record.ApplicationActorId);
                Assert.NotEqual(Guid.Empty, record.CorrelationId);
                Assert.EndsWith(
                    ":processing",
                    record.WorkerId,
                    StringComparison.Ordinal);
            });
        }
    }

    private static ServiceProvider BuildProvider(params Guid[] tenants)
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddSingleton<ITenantEnumerator>(
            new FixedTenantEnumerator(tenants));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<SpaceExecutionContextAccessor>();
        services.AddScoped<ISpaceExecutionContextAccessor>(provider =>
            provider.GetRequiredService<SpaceExecutionContextAccessor>());
        services.AddScoped<ISpaceExecutionContextManager>(provider =>
            provider.GetRequiredService<SpaceExecutionContextAccessor>());
        services.AddScoped<ApplicationExecutionContext,
            HttpSpaceApplicationExecutionContext>();
        services.AddSingleton<RecordingState>();
        services.AddScoped<ISpaceJobProcessorRunner, RecordingRunner>();
        return services.BuildServiceProvider();
    }

    private sealed class FixedTenantEnumerator(Guid[] tenants) :
        ITenantEnumerator
    {
        public Task<IReadOnlyList<Guid>> ListActiveAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Guid>>(tenants);
        }
    }

    private sealed class RecordingState
    {
        public ConcurrentQueue<ClaimRecord> Records { get; } = new();
    }

    private sealed class RecordingRunner(
        ITenantContext tenant,
        ISpaceExecutionContextAccessor accessor,
        ApplicationExecutionContext application,
        RecordingState state) : ISpaceJobProcessorRunner
    {
        public Task<bool> RunNextAsync(
            SpaceJobType jobType,
            string workerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = accessor.RequireCurrent();
            state.Records.Enqueue(new ClaimRecord(
                current.TenantId,
                tenant.CurrentTenantId,
                application.TenantId,
                application.ActorId,
                current.CorrelationId,
                current.ActorType,
                current.ActorName,
                jobType,
                workerId));
            return Task.FromResult(true);
        }

        public Task RunClaimedAsync(
            SpaceJobLease lease,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record ClaimRecord(
        Guid TenantId,
        Guid ScopedTenantId,
        Guid ApplicationTenantId,
        Guid ApplicationActorId,
        Guid CorrelationId,
        string ActorType,
        string? ActorName,
        SpaceJobType JobType,
        string WorkerId);
}
