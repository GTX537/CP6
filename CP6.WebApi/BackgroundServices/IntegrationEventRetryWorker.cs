using CP6.Core.EFDbContext;
using CP6.Core.Options;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// Background worker that retries failed IntegrationEvent rows and dead-letters exhausted retries.
/// </summary>
public class IntegrationEventRetryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntegrationEventOptions _opts;
    private readonly ILogger<IntegrationEventRetryWorker> _logger;

    public IntegrationEventRetryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<IntegrationEventOptions> options,
        ILogger<IntegrationEventRetryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _opts = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IntegrationEvent retry worker starting");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_opts.Enabled)
                {
                    await ProcessOnceAsync(stoppingToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(_opts.PollIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _logger.LogInformation("IntegrationEvent retry worker stopped");
        }
    }

    /// <summary>
    /// Processes one retry scan.
    /// </summary>
    public async Task ProcessOnceAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
        var notifier = scope.ServiceProvider.GetRequiredService<IDeadLetterNotifier>();

        var now = DateTime.UtcNow;
        var due = await db.IntegrationEvents
            .Where(e => e.Status == IntegrationEventStatus.Failed
                     && e.NextRetryAt != null && e.NextRetryAt <= now
                     && e.Attempts < _opts.MaxAttempts)
            .OrderBy(e => e.NextRetryAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var evt in due)
        {
            evt.Attempts++;
            try
            {
                var ok = await dispatcher.DispatchAsync(evt, ct);
                evt.Status = ok ? IntegrationEventStatus.Success : IntegrationEventStatus.Failed;
                if (!ok)
                {
                    evt.NextRetryAt = now.AddSeconds(_opts.GetBackoffSeconds(evt.Attempts));
                }
                else
                {
                    evt.NextRetryAt = null;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                evt.LastError = ex.ToString();
                evt.NextRetryAt = now.AddSeconds(_opts.GetBackoffSeconds(evt.Attempts));
                evt.Status = IntegrationEventStatus.Failed;
            }

            if (evt.Attempts >= _opts.MaxAttempts && evt.Status == IntegrationEventStatus.Failed)
            {
                evt.Status = IntegrationEventStatus.DeadLetter;
                evt.NextRetryAt = null;
                await notifier.NotifyAsync(evt, ct);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
