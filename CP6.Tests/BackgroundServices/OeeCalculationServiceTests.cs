using CP6.Core.Services.Common;
using CP6.Core.Services.Mes;
using CP6.Entity.DTOs.Mes;
using CP6.WebApi.BackgroundServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CP6.Tests.BackgroundServices;

public sealed class OeeCalculationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_BodyCancelsAndReturns_StopsWithoutErrorLog()
    {
        using var cancellation = new CancellationTokenSource();
        var state = new OeeState(call =>
        {
            cancellation.Cancel();
            return Task.FromResult(1);
        });
        await using var provider = BuildProvider(state);
        var logger = new CapturingLogger<OeeCalculationService>();
        var service = new TestOeeCalculationService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger);

        await service.RunAsync(cancellation.Token);

        Assert.Equal(1, state.Calls);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Information &&
                     entry.Message.Contains(
                         "OEE Calculation Service 停止",
                         StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_UncancelledOrdinaryFailure_LogsErrorAndContinuesLoop()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new InvalidOperationException(
            "ordinary OEE recalculation failure");
        var state = new OeeState(call =>
        {
            if (call == 1)
                throw expected;

            cancellation.Cancel();
            return Task.FromResult(1);
        });
        await using var provider = BuildProvider(state);
        var logger = new CapturingLogger<OeeCalculationService>();
        var service = new TestOeeCalculationService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger);

        await service.RunAsync(cancellation.Token);

        Assert.Equal(2, state.Calls);
        var error = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Same(expected, error.Exception);
    }

    private static ServiceProvider BuildProvider(OeeState state)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton<ITenantEnumerator>(
            new OneTenantEnumerator(Guid.NewGuid()));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IOeeService, StubOeeService>();
        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
    }

    private sealed class TestOeeCalculationService :
        OeeCalculationService
    {
        public TestOeeCalculationService(
            IServiceScopeFactory scopeFactory,
            ILogger<OeeCalculationService> logger)
            : base(scopeFactory, logger)
        {
        }

        protected override TimeSpan StartupDelay => TimeSpan.Zero;
        protected override TimeSpan Interval => TimeSpan.Zero;

        public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
    }

    private sealed class OneTenantEnumerator : ITenantEnumerator
    {
        private readonly IReadOnlyList<Guid> _tenants;

        public OneTenantEnumerator(Guid tenantId) => _tenants = [tenantId];

        public Task<IReadOnlyList<Guid>> ListActiveAsync(
            CancellationToken ct = default)
            => Task.FromResult(_tenants);
    }

    private sealed class OeeState
    {
        private readonly Func<int, Task<int>> _recalculate;

        public OeeState(Func<int, Task<int>> recalculate)
            => _recalculate = recalculate;

        public int Calls { get; private set; }

        public Task<int> RecalculateAsync()
        {
            Calls++;
            return _recalculate(Calls);
        }
    }

    private sealed class StubOeeService : IOeeService
    {
        private readonly OeeState _state;

        public StubOeeService(OeeState state) => _state = state;

        public Task<int> RecalculateAsync(
            OeeRecalcRequest req,
            string? userName)
            => _state.RecalculateAsync();

        public Task<List<OeeDailyDto>> SearchAsync(OeeSearchQuery query)
            => throw new NotSupportedException();

        public Task<List<OeeDailyDto>> CalculateTodayAsync(
            string? machineCd = null)
            => throw new NotSupportedException();

        public Task<Dictionary<string, List<OeeDailyDto>>> GetTrendAsync(
            int days,
            string? machineCd)
            => throw new NotSupportedException();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception));
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);
}
