using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

/// <summary>Durable retries continue even when the transport exhausts its shorter retry policy.</summary>
public sealed class ErpCommandRetryWorker(IDbContextFactory<ErpIntegrationContext> factory, IServiceScopeFactory scopes,
    ErpIntegrationRuntime runtime, ILogger<ErpCommandRetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RetryDueAsync(stoppingToken);
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("C03 durable command retry is temporarily unavailable.");
                try { await Task.Delay(2000, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    /// <summary>One bounded retry batch per configured tenant; retired histories cannot starve active tenants.</summary>
    public async Task RetryDueAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = runtime.Clock.GetUtcNow();
        foreach (var tenant in runtime.Options.Tenants.Keys)
        {
            var receipts = await db.Inbox.AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.Status == ErpInboxStatus.Processing && x.RetryAtUtc != null && x.RetryAtUtc <= now)
                .OrderBy(x => x.RetryAtUtc).Take(16)
                .Select(x => new { x.Payload, x.TenantId, x.AggregateId }).ToListAsync(ct);
            foreach (var receipt in receipts)
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ErpRequestHandler>().ConsumeAsync(receipt.Payload,
                    ErpEventContracts.RequestTopic, $"{receipt.TenantId:D}:{receipt.AggregateId:D}", ct);
            }
        }
    }
}
