using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using CP6.Platform.Messaging;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Configuration;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CP6.ErpLive.Fixture;

internal static partial class ErpLiveFixture
{
    public static async Task DispatchAsync(string configPath, CancellationToken ct)
    {
        var (fixture, owner) = await ReadFixtureAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "live-fixture.json"), ct);
        await using var services = BuildRuntimeServices(configPath, fixture, owner);
        var runtime = services.GetRequiredService<ErpIntegrationRuntime>();
        // C02's established Dapr client also reads this process-scoped SDK setting. Callback authentication stays separate.
        var previousApiToken = Environment.GetEnvironmentVariable("DAPR_API_TOKEN");
        Environment.SetEnvironmentVariable("DAPR_API_TOKEN", runtime.Options.DaprApiToken);
        var configuration = services.GetRequiredService<IConfiguration>();
        var identityOptions = CrmIdentityConfiguration.BindOptions(configuration);
        var identityRuntime = new CrmIdentityRuntime(identityOptions,
            new IdentityEventValidator(Cp6ContractBundle.Load(ContractDirectory("platform")), identityOptions.Issuer));
        var workers = services.GetServices<IHostedService>().OfType<BackgroundService>()
            .Where(w => w is ErpEventDispatchWorker or ErpCommandRetryWorker).ToList();
        using var identity = new IdentityEventDispatchWorker(identityRuntime, configuration,
            services.GetRequiredService<ILogger<IdentityEventDispatchWorker>>());
        workers.Add(identity);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var started = new List<BackgroundService>();
        try
        {
            foreach (var worker in workers)
            {
                await worker.StartAsync(lifetime.Token);
                started.Add(worker);
            }
            Console.WriteLine("C03 real result dispatch, durable retry and C02 identity dispatch started; WMS/MES bridge dispatch is outside this fixture scope.");
            var executions = started.Select(w => w.ExecuteTask ?? throw new FixtureException("C03_WORKER_NOT_STARTED")).ToArray();
            await Task.WhenAny(executions);
            lifetime.Cancel();
            await Task.WhenAll(executions);
        }
        finally
        {
            lifetime.Cancel();
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (var worker in started.AsEnumerable().Reverse())
                try { await worker.StopAsync(shutdown.Token); }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            Environment.SetEnvironmentVariable("DAPR_API_TOKEN", previousApiToken);
        }
    }

    public static async Task ReplayAsync(string fixturePath, string rawTenant, string messageId, string rawOperation, CancellationToken ct)
    {
        var (fixture, owner) = await ReadFixtureAsync(fixturePath, ct);
        var tenant = OwnedTenant(fixture, rawTenant);
        if (!Guid.TryParseExact(rawOperation, "D", out var operation) || operation == Guid.Empty ||
            messageId is not { Length: > 0 and <= 128 } || messageId.Any(char.IsControl))
            throw new FixtureException("C03_FIXTURE_REPLAY_INPUT_INVALID");
        await using var services = BuildRuntimeServices(Path.Combine(fixture.CoreContentRoot, "appsettings.Local.json"), fixture, owner);
        var factory = services.GetRequiredService<IDbContextFactory<ErpIntegrationContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var receipt = await db.Inbox.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.MessageId == messageId, ct)
            ?? throw new FixtureException("C03_FIXTURE_REPLAY_RECEIPT_NOT_FOUND");
        var previous = await db.ReplayAudits.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.OperationId == operation, ct);
        if (previous is not null && previous.MessageId != messageId)
            throw new FixtureException("C03_FIXTURE_REPLAY_OPERATION_CONFLICT");
        // Repeat calls preserve the original input precondition, allowing the production operation-id replay guard to decide.
        var version = previous?.InputRowVersion ?? receipt.RowVersion;
        var digest = previous?.PayloadSha256 ?? receipt.PayloadSha256;
        await using var scope = services.CreateAsyncScope();
        var scheduled = await scope.ServiceProvider.GetRequiredService<ErpInboxReplayService>().ScheduleAsync(tenant.Id, messageId,
            new(operation, version, digest, "dependency-recovered"), tenant.Users.Single().Id.ToString("D"), ct);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schemaId = "cp6.c03.fixture-replay-scheduled.v1", tenantId = tenant.Id,
            scheduled.OperationId, scheduled.MessageId, scheduled.ReplayedAtUtc,
            payloadSha256 = digest, inputRowVersion = Convert.ToBase64String(version),
            driver = "fixture-domain-service; HTTP operator identity is verified separately"
        }, Json));
    }

    private static ServiceProvider BuildRuntimeServices(string configPath, LiveFixtureDocument fixture, DatabaseOwnership owner)
    {
        var path = Path.GetFullPath(configPath);
        if (Path.GetFileName(path) != "appsettings.Local.json" ||
            !string.Equals(Path.GetDirectoryName(path), Path.GetFullPath(fixture.CoreContentRoot), StringComparison.OrdinalIgnoreCase))
            throw new FixtureException("C03_OWNED_RUNTIME_CONFIG_REQUIRED");
        var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
        var sql = new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        ValidateLocalConnection(sql);
        if (sql.MultipleActiveResultSets || sql.InitialCatalog != owner.Database ||
            !string.Equals(sql.DataSource, owner.DataSource, StringComparison.OrdinalIgnoreCase))
            throw new FixtureException("C03_RUNTIME_CONFIG_DATABASE_MISMATCH");
        var oidc = configuration.GetSection("CrmOidc").Get<CrmOidcOptions>()
            ?? throw new FixtureException("C03_RUNTIME_ISSUER_MISSING");
        oidc.Validate(development: true);
        if (oidc.Issuer != fixture.CoreOrigin || !oidc.Organizations.Select(o => o.TenantId).Order().SequenceEqual(owner.TenantIds.Order()))
            throw new FixtureException("C03_RUNTIME_IDENTITY_OWNERSHIP_MISMATCH");
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(TimeProvider.System);
        services.AddErpIntegration(configuration, oidc);
        var boundary = new ExternalHookBoundary();
        services.AddScoped(provider => new ErpRequestHandler(provider.GetRequiredService<IDbContextFactory<ErpIntegrationContext>>(),
            provider.GetRequiredService<ErpIntegrationRuntime>(), db => new OrderService(db, boundary, boundary,
                mesBridge: boundary, fxRate: new FxRateService(db))));
        var provider = services.BuildServiceProvider();
        try
        {
            var runtime = provider.GetRequiredService<ErpIntegrationRuntime>();
            if (!runtime.Options.Tenants.Keys.Order().SequenceEqual(owner.TenantIds.Order()) ||
                !runtime.Options.ReaderClientIds.Order(StringComparer.Ordinal).SequenceEqual(fixture.Tenants.Select(t => t.ReaderClientId).Order(StringComparer.Ordinal)))
                throw new FixtureException("C03_RUNTIME_TENANT_OWNERSHIP_MISMATCH");
            return provider;
        }
        catch { provider.Dispose(); throw; }
    }

    private static LiveTenant OwnedTenant(LiveFixtureDocument fixture, string value)
    {
        if (!Guid.TryParseExact(value, "D", out var id)) throw new FixtureException("C03_FIXTURE_TENANT_REQUIRED");
        return fixture.Tenants.SingleOrDefault(t => t.Id == id) ?? throw new FixtureException("C03_FIXTURE_TENANT_NOT_OWNED");
    }

    public static async Task CleanupAsync(string privateRoot, CancellationToken ct)
    {
        var root = RequirePrivateDirectory(privateRoot);
        string[] directories = File.Exists(Path.Combine(root, "ownership.json")) ? [root]
            : Directory.GetDirectories(root, "CP6C03Live_*", SearchOption.TopDirectoryOnly);
        var owners = new List<DatabaseOwnership>();
        foreach (var directory in directories) owners.Add(await ReadOwnershipAsync(directory, ct));
        foreach (var owner in owners)
        {
            await using var master = new SqlConnection(MasterConnection().ConnectionString);
            var exists = await master.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM sys.databases WHERE name=@name", new { name = owner.Database }, cancellationToken: ct));
            if (exists == 0) continue;
            await VerifyDatabaseMarkerAsync(owner, ct);
            await master.ExecuteAsync(new CommandDefinition(
                $"ALTER DATABASE [{owner.Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{owner.Database}];",
                commandTimeout: 90, cancellationToken: ct));
            if (await master.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM sys.databases WHERE name=@name",
                    new { name = owner.Database }, cancellationToken: ct)) != 0)
                throw new FixtureException("C03_DATABASE_CLEANUP_INCOMPLETE");
        }
        Console.WriteLine($"C03 verified absence of {owners.Count} strictly owned ERP fixture databases. Private artifacts were retained.");
    }
}

// A throw-on-use assertion, not a successful fake adapter. C03's real transactional order method must not call these.
// Post-commit work is left in the real OrderBridgeDispatch table for explicit downstream acceptance outside this fixture.
internal sealed class ExternalHookBoundary : IPowerEggWorkflowService, IWmsBridgeHook, IMesBridgeHook
{
    private static Exception Unexpected() => new FixtureException("C03_EXTERNAL_HOOK_CALLED_INSIDE_COMMAND_TRANSACTION");
    public Task<bool> RequestPriceCorrectionAsync(OrderDetail entity, string? actor, CancellationToken ct = default) => throw Unexpected();
    public Task<WmsBridgeResult> OnOrderCreatedAsync(string order, string? actor) => throw Unexpected();
    Task<MesBridgeResult> IMesBridgeHook.OnOrderCreatedAsync(string order, string? actor) => throw Unexpected();
    public Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string order, string? actor) => throw Unexpected();
    public Task<WmsBridgeResult> OnProductionCompletedAsync(string order, decimal quantity, string? actor) => throw Unexpected();
}
