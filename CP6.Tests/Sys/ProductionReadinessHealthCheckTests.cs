using CP6.Core.EFDbContext;
using CP6.WebApi.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text;

namespace CP6.Tests.Sys;

public class ProductionReadinessHealthCheckTests
{
    [Fact]
    public async Task DatabaseProbe_ReturnsHealthy_WhenConnectionCanOpen()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<CP6Context>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        var check = new DatabaseReadinessHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CacheProbe_ReturnsHealthy_AfterSuccessfulRoundTrip()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();
        var check = new DistributedCacheReadinessHealthCheck(
            provider.GetRequiredService<IDistributedCache>(),
            TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CacheProbe_ReturnsUnhealthy_WithoutLeakingExceptionDetails()
    {
        var check = new DistributedCacheReadinessHealthCheck(
            new ThrowingDistributedCache(),
            TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.DoesNotContain("sensitive-cache-detail", result.Description, StringComparison.Ordinal);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task HealthResponse_ExposesStatusesButNotDependencyDetails()
    {
        const string sensitiveDetail = "sensitive-connection-detail";
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["sqlserver"] = new(
                    HealthStatus.Unhealthy,
                    sensitiveDetail,
                    TimeSpan.FromMilliseconds(25),
                    new InvalidOperationException(sensitiveDetail),
                    new Dictionary<string, object>
                    {
                        ["connection"] = sensitiveDetail
                    })
            },
            TimeSpan.FromMilliseconds(25));
        var httpContext = new DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await HealthResponseWriter.WriteAsync(httpContext, report);
        body.Position = 0;
        using var reader = new StreamReader(body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();

        Assert.Contains("\"status\":\"Unhealthy\"", json, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"sqlserver\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveDetail, json, StringComparison.Ordinal);
        Assert.Equal("no-store", httpContext.Response.Headers.CacheControl);
    }

    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw CreateException();

        public Task<byte[]?> GetAsync(
            string key,
            CancellationToken token = default) =>
            Task.FromException<byte[]?>(CreateException());

        public void Refresh(string key) => throw CreateException();

        public Task RefreshAsync(
            string key,
            CancellationToken token = default) =>
            Task.FromException(CreateException());

        public void Remove(string key) => throw CreateException();

        public Task RemoveAsync(
            string key,
            CancellationToken token = default) =>
            Task.FromException(CreateException());

        public void Set(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options) =>
            throw CreateException();

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default) =>
            Task.FromException(CreateException());

        private static InvalidOperationException CreateException() =>
            new("sensitive-cache-detail");
    }
}
