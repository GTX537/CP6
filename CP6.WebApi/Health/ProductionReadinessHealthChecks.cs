using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CP6.WebApi.Health;

internal sealed class DatabaseReadinessHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database connection probe failed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Database connection probe failed.");
        }
    }
}

internal sealed class DistributedCacheReadinessHealthCheck(
    IDistributedCache cache,
    TimeProvider timeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var key = $"health:ready:{Guid.NewGuid():N}";
        var expected = Guid.NewGuid().ToByteArray();

        try
        {
            await cache.SetAsync(
                key,
                expected,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpiration = timeProvider.GetUtcNow().AddSeconds(30)
                },
                cancellationToken);
            var actual = await cache.GetAsync(key, cancellationToken);

            return actual is not null && actual.AsSpan().SequenceEqual(expected)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Distributed cache round-trip probe failed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Distributed cache round-trip probe failed.");
        }
        finally
        {
            try
            {
                await cache.RemoveAsync(key, CancellationToken.None);
            }
            catch
            {
                // The probe result already captures cache failure. Cleanup must not replace it.
            }
        }
    }
}
