using System.Data.Common;
using CP6.Core.Services.Common;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Erp;
using CP6.WebApi.BackgroundServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[assembly: HostingStartup(typeof(SpaceHistoricalRepublishWorkerFixture))]

namespace CP6.Tests.Erp;

public sealed class SpaceHistoricalRepublishWorkerFixture : IHostingStartup
{
    internal const string RootVariable = "CP6_HISTORICAL_REPUBLISH_FIXTURE_ROOT";
    internal const string PhaseVariable = "CP6_HISTORICAL_REPUBLISH_FIXTURE_PHASE";
    internal const string LostAcknowledgementVariable = "CP6_HISTORICAL_REPUBLISH_FIXTURE_COMMIT";

    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
    {
        if (Environment.GetEnvironmentVariable(RootVariable) is not { } root)
            return;

        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("Space__Compatibility__Sites__0__TenantId")!);
        var phase = Environment.GetEnvironmentVariable(PhaseVariable);
        services.AddSingleton<IStartupFilter>(new FixtureStartup(root, tenant, phase));
        if (Environment.GetEnvironmentVariable(LostAcknowledgementVariable) == "1")
        {
            var relay = new QueueCommitRelay(root);
            services.AddScoped(provider => new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>(provider.GetRequiredService<DbContextOptions<SpaceContext>>())
                    .AddInterceptors(relay).Options,
                provider.GetRequiredService<ISpaceExecutionContext>(),
                provider.GetRequiredService<ISpaceClock>()));
        }
    });

    private sealed class QueueCommitRelay(string root) : DbTransactionInterceptor
    {
        private int _observed;

        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data,
            CancellationToken cancellationToken = default)
        {
            if (data.Context is not SpaceContext space ||
                !space.ChangeTracker.Entries<SpaceHistoricalRepublish>().Any(row => row.Entity.PublishAttemptId.HasValue) ||
                !space.ChangeTracker.Entries<SpacePublishAttempt>().Any() ||
                Interlocked.Exchange(ref _observed, 1) != 0)
                return;

            SpaceArtifactWorkerFixture.Write(root, 1, "child-committed", true);
            await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, 1, "release-commit", cancellationToken);
            throw new IOException("The fixture lost the historical child COMMIT acknowledgement.");
        }
    }

    private sealed class FixtureStartup(string root, Guid tenant, string? phase) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStarted.Register(() => _ = RunAsync(app.ApplicationServices, lifetime.ApplicationStopping));
        };

        private async Task RunAsync(IServiceProvider services, CancellationToken ct)
        {
            var scopes = new FixtureScopes(services.GetRequiredService<IServiceScopeFactory>(), tenant, root, phase);
            using var worker = new SpacePublishJobWorker(scopes, services.GetRequiredService<ILogger<SpacePublishJobWorker>>());
            for (var sequence = 1; !ct.IsCancellationRequested; sequence++)
            {
                try
                {
                    await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, sequence, "request", ct);
                    var processed = await worker.ProcessOnceAsync(ct);
                    SpaceArtifactWorkerFixture.Write(root, sequence, "result", new SpacePublishWorkerFixture.WorkerResult(processed, null));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    SpaceArtifactWorkerFixture.Write(root, sequence, "result", new SpacePublishWorkerFixture.WorkerResult(-1, error.ToString()));
                }
            }
        }
    }

    private sealed class FixtureScopes(IServiceScopeFactory inner, Guid tenant, string root, string? phase) : IServiceScopeFactory
    {
        private int _observed;

        public IServiceScope CreateScope() => new FixtureScope(inner.CreateScope(), tenant, this);

        private async Task ObserveAsync(string point, CancellationToken ct)
        {
            if (phase != point || Interlocked.Exchange(ref _observed, 1) != 0)
                return;
            SpaceArtifactWorkerFixture.Write(root, 1, "before-effect", point);
            await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, 1, "release-effect", ct);
        }

        private sealed class FixtureScope(IServiceScope inner, Guid tenant, FixtureScopes fixture) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type type)
            {
                var services = inner.ServiceProvider;
                if (type == typeof(ITenantEnumerator))
                    return new FixtureTenant(services.GetRequiredService<ITenantEnumerator>(), tenant);
                if (type != typeof(ISpaceJobProcessorRunner))
                    return services.GetService(type);

                var context = services.GetRequiredService<SpaceContext>();
                var executor = new SpaceHistoricalRepublishJobExecutor(
                    context, services.GetRequiredService<ISpaceClock>(),
                    services.GetRequiredService<ISpaceVersionSnapshotCloner>(),
                    new ProfileRelay(services.GetRequiredService<ISpaceValidationProfileProvider>(), context, fixture),
                    services.GetRequiredService<SpaceValidationEngine>(),
                    services.GetRequiredService<ISpacePublishPreviewService>(),
                    services.GetRequiredService<ISpaceHistoricalRepublishPublishStarter>(),
                    services.GetRequiredService<ISpaceArtifactWriteAuthority>());
                var processor = new SpaceHistoricalRepublishJobProcessor(new StepRelay(executor, fixture));
                return new ParentRunner(new SpaceJobProcessorRunner(
                    services.GetRequiredService<ISpaceJobLeaseStore>(), [processor], services.GetService<SpaceJobProcessorOptions>()));
            }

            public void Dispose() => inner.Dispose();
        }

        private sealed class FixtureTenant(ITenantEnumerator inner, Guid tenant) : ITenantEnumerator
        {
            public async Task<IReadOnlyList<Guid>> ListActiveAsync(CancellationToken ct = default) =>
                (await inner.ListActiveAsync(ct)).Where(row => row == tenant).ToArray();
        }

        private sealed class ProfileRelay(ISpaceValidationProfileProvider inner, SpaceContext context, FixtureScopes fixture)
            : ISpaceValidationProfileProvider
        {
            public async Task<SpaceValidationProfile> GetProfileAsync(Guid tenantId, Guid siteId, Guid correlationId,
                CancellationToken ct = default)
            {
                if (context.Database.CurrentTransaction is not null)
                    throw new InvalidOperationException("Historical validation must prepare outside the write transaction.");
                await fixture.ObserveAsync("validation-finish", ct);
                return await inner.GetProfileAsync(tenantId, siteId, correlationId, ct);
            }
        }

        private sealed class StepRelay(ISpaceHistoricalRepublishJobExecutor inner, FixtureScopes fixture) : ISpaceHistoricalRepublishJobExecutor
        {
            public async Task<SpaceJobStepOutput> ExecuteAsync(SpaceJobStepExecution execution, CancellationToken ct = default)
            {
                await fixture.ObserveAsync(execution.StepCode, ct);
                return await inner.ExecuteAsync(execution, ct);
            }
        }

        private sealed class ParentRunner(SpaceJobProcessorRunner inner) : ISpaceJobProcessorRunner
        {
            public Task<bool> RunNextAsync(SpaceJobType jobType, string workerId, CancellationToken ct = default) =>
                jobType == SpaceJobType.HistoricalRepublish ? inner.RunNextAsync(jobType, workerId, ct) : Task.FromResult(false);

            public Task RunClaimedAsync(SpaceJobLease lease, CancellationToken ct = default) => inner.RunClaimedAsync(lease, ct);
        }
    }
}
