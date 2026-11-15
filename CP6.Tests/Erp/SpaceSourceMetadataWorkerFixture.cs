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

[assembly: HostingStartup(typeof(SpaceSourceMetadataWorkerFixture))]

namespace CP6.Tests.Erp;

public sealed class SpaceSourceMetadataWorkerFixture : IHostingStartup
{
    internal const string RootVariable = "CP6_SPACE_SOURCE_METADATA_FIXTURE_ROOT";
    internal const string PhaseVariable = "CP6_SPACE_SOURCE_METADATA_FIXTURE_PHASE";
    internal const string FaultVariable = "CP6_SPACE_SOURCE_METADATA_FIXTURE_ACK";

    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
    {
        if (Environment.GetEnvironmentVariable(RootVariable) is not { } root)
            return;

        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("Space__Compatibility__Sites__0__TenantId")!);
        var phase = Environment.GetEnvironmentVariable(PhaseVariable);
        services.AddScoped<ISpaceCadParseProvider, SpaceArtifactWorkerFixture.ArtifactFixtureCadProvider>();
        services.AddSingleton<IStartupFilter>(new WorkerRelay(root, tenant, phase));
        if (Environment.GetEnvironmentVariable(FaultVariable) == "1")
        {
            var fault = new IssueCommitRelay(root);
            services.AddScoped(provider => new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>(provider.GetRequiredService<DbContextOptions<SpaceContext>>())
                    .AddInterceptors(fault).Options,
                provider.GetRequiredService<ISpaceExecutionContext>(),
                provider.GetRequiredService<ISpaceClock>()));
        }
    });

    private sealed class IssueCommitRelay(string root) : DbTransactionInterceptor
    {
        private int _injected;

        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data,
            CancellationToken cancellationToken = default)
        {
            if (data.Context is not SpaceContext context || !context.ChangeTracker.Entries<SpaceModelIssue>().Any() ||
                Interlocked.Exchange(ref _injected, 1) != 0)
                return;

            SpaceArtifactWorkerFixture.Write(root, 1, "committed", true);
            await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, 1, "release", cancellationToken);
            throw new IOException("W626 injected loss of the Excel Issues commit acknowledgement.");
        }
    }

    private sealed class WorkerRelay(string root, Guid tenant, string? phase) : IStartupFilter
    {
        Action<IApplicationBuilder> IStartupFilter.Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStarted.Register(() => _ = DriveAsync(app.ApplicationServices, lifetime.ApplicationStopping));
        };

        private async Task DriveAsync(IServiceProvider services, CancellationToken stoppingToken)
        {
            var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
            using var worker = new SpaceProcessingJobWorker(new ScopedWorkerFixture(scopeFactory, tenant, root, phase),
                services.GetRequiredService<ILogger<SpaceProcessingJobWorker>>());
            for (var sequence = 1; !stoppingToken.IsCancellationRequested; sequence++)
            {
                try
                {
                    await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, sequence, "request", stoppingToken);
                    var processed = await worker.ProcessOnceAsync(stoppingToken);
                    SpaceArtifactWorkerFixture.Write(root, sequence, "result", new WorkerResult(processed, null));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    SpaceArtifactWorkerFixture.Write(root, sequence, "result", new WorkerResult(-1, error.ToString()));
                }
            }
        }
    }

    private sealed class ScopedWorkerFixture(IServiceScopeFactory inner, Guid tenant, string root, string? phase) : IServiceScopeFactory
    {
        private readonly Guid _tenant = tenant;
        private readonly string? _phase = phase;
        private int _relayed;

        public IServiceScope CreateScope() => new FixtureScope(inner.CreateScope(), this);

        private async Task AtStepAsync(SpaceJobStepExecution execution, CancellationToken ct)
        {
            if (execution.StepCode != _phase || Interlocked.Exchange(ref _relayed, 1) != 0)
                return;

            SpaceArtifactWorkerFixture.Write(root, 1, "at-step", execution.Lease.AttemptId);
            await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, 1, "release", ct);
        }

        private sealed class FixtureScope(IServiceScope inner, ScopedWorkerFixture fixture) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type type)
            {
                var services = inner.ServiceProvider;
                if (type == typeof(ITenantEnumerator))
                    return new FixtureEnumerator(services.GetRequiredService<ITenantEnumerator>(), fixture._tenant);
                if (type == typeof(ISpaceJobProcessorRunner) && fixture._phase is not null)
                {
                    return new SpaceJobProcessorRunner(services.GetRequiredService<ISpaceJobLeaseStore>(),
                        services.GetServices<ISpaceJobProcessor>().Select(processor => new StepRelay(processor, fixture)),
                        services.GetService<SpaceJobProcessorOptions>());
                }
                return services.GetService(type);
            }

            public void Dispose() => inner.Dispose();
        }

        private sealed class FixtureEnumerator(ITenantEnumerator inner, Guid tenant) : ITenantEnumerator
        {
            public async Task<IReadOnlyList<Guid>> ListActiveAsync(CancellationToken ct = default) =>
                (await inner.ListActiveAsync(ct)).Where(value => value == tenant).ToArray();
        }

        private sealed class StepRelay(ISpaceJobProcessor inner, ScopedWorkerFixture fixture) : ISpaceJobProcessor
        {
            public SpaceJobType JobType => inner.JobType;
            public SpaceJobSubjectType SubjectType => inner.SubjectType;
            public string ProcessorVersion => inner.ProcessorVersion;
            public IReadOnlyList<string> StepCodes => inner.StepCodes;

            public async Task<SpaceJobStepOutput> ExecuteStepAsync(SpaceJobStepExecution execution, CancellationToken ct = default)
            {
                await fixture.AtStepAsync(execution, ct);
                return await inner.ExecuteStepAsync(execution, ct);
            }
        }
    }

    internal sealed record WorkerResult(int Processed, string? Error);
}
