using System.Diagnostics;
using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// Claims tenant-scoped Space processing Jobs that are not part of the
/// dedicated publish/reconciliation lane. The shared lease ledger fences
/// every claim, so another host can safely recover work after this host stops.
/// One Job per type is attempted on each tenant pass to prevent a hot queue
/// from starving the remaining processing types.
/// </summary>
public sealed class SpaceProcessingJobWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SpaceProcessingJobWorker> logger) : BackgroundService
{
    internal static readonly Guid WorkerActorId =
        Guid.Parse("7d227ba9-e657-5e55-a0d7-47c76b4b5de9");
    internal const string WorkerActorName = "space-worker:processing";
    internal static IReadOnlyList<SpaceJobType> JobTypes { get; } =
        Array.AsReadOnly(
        [
            SpaceJobType.ExcelPreview,
            SpaceJobType.CadParse,
            SpaceJobType.ExcelCadMatch,
            SpaceJobType.ExcelCadApply,
            SpaceJobType.Import,
            SpaceJobType.BuildScene,
            SpaceJobType.ApplyGeneration,
            SpaceJobType.Validate,
            SpaceJobType.AiRetentionCleanup,
        ]);

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

    public async Task<int> ProcessOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var total = 0;
        await TenantScopeRunner.ForEachTenantAsync(
            scopeFactory,
            async (services, tenantId, tenantToken) =>
            {
                using var activity = new Activity("Space.ProcessingJob")
                    .SetIdFormat(ActivityIdFormat.W3C)
                    .Start();
                var context = new SpaceExecutionContext(
                    Guid.NewGuid(),
                    activity.TraceId.ToHexString(),
                    tenantId,
                    SpaceExecutionContext.SystemActor,
                    WorkerActorId.ToString("D"),
                    WorkerActorName);
                var manager = services.GetRequiredService<
                    ISpaceExecutionContextManager>();
                using var executionScope = manager.Push(context);
                var runner = services.GetRequiredService<
                    ISpaceJobProcessorRunner>();
                var workerId =
                    $"{Environment.MachineName}:{Environment.ProcessId}:processing";
                var processedForTenant = 0;

                foreach (var jobType in JobTypes)
                {
                    if (await runner.RunNextAsync(
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
