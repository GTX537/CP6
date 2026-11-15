using System.Data;
using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wms;
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

[assembly: HostingStartup(typeof(SpacePublishWorkerFixture))]

namespace CP6.Tests.Erp;

public sealed class SpacePublishWorkerFixture : IHostingStartup
{
    internal const string RootVariable = "CP6_SPACE_PUBLISH_WORKER_FIXTURE_ROOT";
    internal const string RelayVariable = "CP6_SPACE_PUBLISH_WORKER_FIXTURE_RELAY";

    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
    {
        if (Environment.GetEnvironmentVariable(RootVariable) is not { } root)
            return;

        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("Space__Compatibility__Sites__0__TenantId")!);
        services.AddSingleton<IStartupFilter>(new WorkerRelay(root, tenant));
        var mode = Environment.GetEnvironmentVariable(RelayVariable);
        if (mode is null)
            return;

        var relay = new NativeRelay(root, mode);
        if (mode == "wms-ack")
        {
            services.AddScoped(provider => new CP6Context(
                new DbContextOptionsBuilder<CP6Context>(provider.GetRequiredService<DbContextOptions<CP6Context>>())
                    .AddInterceptors(relay).Options,
                provider.GetRequiredService<ITenantContext>(),
                provider.GetService<ICurrentUserAccessor>(),
                provider.GetService<CrmIdentityRuntime>()));
        }
        else
        {
            services.AddScoped(provider => new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>(provider.GetRequiredService<DbContextOptions<SpaceContext>>())
                    .AddInterceptors(relay).Options,
                provider.GetRequiredService<ISpaceExecutionContext>(),
                provider.GetRequiredService<ISpaceClock>()));
        }
    });

    private sealed class NativeRelay(string root, string mode) : DbTransactionInterceptor
    {
        private int _claimed;

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData data, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (mode == "runtime-entry" && data.IsolationLevel == IsolationLevel.Serializable && data.Context is SpaceContext space &&
                space.ChangeTracker.Entries<SpacePublishAttempt>().Any(entry =>
                    entry.State == EntityState.Unchanged && entry.Entity.Status == SpacePublishAttemptStatus.ActivatingRuntime))
            {
                await AwaitTestAsync(cancellationToken);
            }
            return result;
        }

        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data,
            CancellationToken cancellationToken = default)
        {
            var ownedCommit = mode switch
            {
                "wms-ack" when data.Context is CP6Context core => core.ChangeTracker.Entries<SpaceWmsOperation>().Any(),
                "runtime-ack" when data.Context is SpaceContext space => space.ChangeTracker.Entries<SpacePublishAttempt>()
                    .Any(entry => entry.Entity.Status == SpacePublishAttemptStatus.ActivatingRuntime)
                    && space.ChangeTracker.Entries<SpaceModelVersion>().Any(entry =>
                        entry.Entity.BasedOnVersionId.HasValue && entry.Entity.Status == SpaceVersionStatus.Published),
                _ => false
            };

            if (ownedCommit && await AwaitTestAsync(cancellationToken))
                throw new IOException("W621 injected loss of a native publish commit acknowledgement.");
        }

        private async Task<bool> AwaitTestAsync(CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _claimed, 1) != 0)
                return false;

            SpaceArtifactWorkerFixture.Write(root, 1, "native-ready", true);
            await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, 1, "native-continue", ct);
            return true;
        }
    }


    private sealed class WorkerRelay(string root, Guid tenant) : IStartupFilter
    {
        Action<IApplicationBuilder> IStartupFilter.Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStarted.Register(() => _ = DriveWorkerAsync(app.ApplicationServices, lifetime.ApplicationStopping));
        };

        private async Task DriveWorkerAsync(IServiceProvider services, CancellationToken stoppingToken)
        {
            using var worker = new SpacePublishJobWorker(new SingleFixtureTenant(services.GetRequiredService<IServiceScopeFactory>(), tenant),
                services.GetRequiredService<ILogger<SpacePublishJobWorker>>());
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

    // Each xUnit case has a tenant in the same owned cluster. Select it only
    // from the real production enumerator's active rows.
    private sealed class SingleFixtureTenant(IServiceScopeFactory inner, Guid tenant) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FixtureScope(inner.CreateScope(), tenant);

        private sealed class FixtureScope(IServiceScope inner, Guid tenant) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type type) => type == typeof(ITenantEnumerator)
                ? new FixtureEnumerator(inner.ServiceProvider.GetRequiredService<ITenantEnumerator>(), tenant)
                : inner.ServiceProvider.GetService(type);

            public void Dispose() => inner.Dispose();
        }

        private sealed class FixtureEnumerator(ITenantEnumerator inner, Guid tenant) : ITenantEnumerator
        {
            public async Task<IReadOnlyList<Guid>> ListActiveAsync(CancellationToken ct = default) =>
                (await inner.ListActiveAsync(ct)).Where(value => value == tenant).ToArray();
        }
    }

    internal sealed record WorkerResult(int Processed, string? Error);
}
