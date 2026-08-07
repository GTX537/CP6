using System.Diagnostics;
using CP6.Core.Services.Space;
using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// Claims tenant-scoped warehouse publish and reconciliation Jobs. Each Job
/// is fenced by the shared Space Job lease ledger; a stopped host leaves an
/// expiring lease that another worker can safely recover.
/// </summary>
public sealed class SpacePublishJobWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SpacePublishJobWorker> logger) : BackgroundService
{
    private const string WorkerActor = "space-worker:publish";
    private const int MaximumJobsPerTenantPass = 8;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var processed = await ProcessOnceAsync(stoppingToken);
                if (processed == 0)
                    await Task.Delay(IdleDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        var total = 0;
        await TenantScopeRunner.ForEachTenantAsync(
            scopeFactory,
            async (services, tenantId, tenantToken) =>
            {
                using var activity = new Activity("Space.PublishJob")
                    .SetIdFormat(ActivityIdFormat.W3C)
                    .Start();
                var context = SpaceExecutionContext.ForSystem(
                    tenantId,
                    WorkerActor,
                    Guid.NewGuid(),
                    activity.TraceId.ToHexString(),
                    jobId: Guid.NewGuid(),
                    runId: Guid.NewGuid());
                var manager = services.GetRequiredService<
                    ISpaceExecutionContextManager>();
                using var executionScope = manager.Push(context);
                var runner = services.GetRequiredService<
                    ISpaceJobProcessorRunner>();
                var workerId = $"{Environment.MachineName}:{Environment.ProcessId}:publish";
                var processedForTenant = 0;
                foreach (var jobType in new[]
                         {
                             SpaceJobType.Reconcile,
                             SpaceJobType.Publish,
                         })
                {
                    while (processedForTenant < MaximumJobsPerTenantPass &&
                           await runner.RunNextAsync(
                               jobType,
                               workerId,
                               tenantToken))
                    {
                        processedForTenant++;
                    }
                }
                Interlocked.Add(ref total, processedForTenant);
            },
            logger,
            cancellationToken);
        return total;
    }
}
