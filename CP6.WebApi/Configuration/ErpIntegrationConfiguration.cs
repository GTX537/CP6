using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.AspNetCore;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Configuration;

public static class ErpIntegrationConfiguration
{
    public static IServiceCollection AddErpIntegration(this IServiceCollection services, IConfiguration configuration, CrmOidcOptions oidc)
    {
        services.AddScoped<ErpReadService>();
        var section = configuration.GetSection("ErpIntegration");
        var options = section.Get<ErpIntegrationOptions>() ?? new();
        if (!options.Enabled) return services;
        options.Tenants.Clear();
        foreach (var tenant in section.GetSection("Tenants").GetChildren())
        {
            if (!Guid.TryParseExact(tenant.Key, "D", out var id) || id == Guid.Empty || string.IsNullOrWhiteSpace(tenant.Value) ||
                tenant.GetChildren().Any() || !options.Tenants.TryAdd(id, tenant.Value))
                throw new InvalidOperationException("C03_REQUIRES_VALID_TENANT_MAP");
        }
        var identity = CrmIdentityConfiguration.BindOptions(configuration);
        if (!oidc.Enabled || !identity.Enabled || options.Tenants.Any(t => !identity.Tenants.TryGetValue(t.Key, out var region) || region != t.Value) ||
            options.ReaderClientIds.Any(id => !oidc.ServiceClients.Any(c => c.ClientId == id && c.Enabled &&
                options.Tenants.ContainsKey(c.TenantId) && c.AllowedScopes.Contains("cp6.services", StringComparer.Ordinal))))
            throw new InvalidOperationException("C03_REQUIRES_RECORDED_C01_C02_SERVICE_IDENTITY");
        var connection = configuration.GetConnectionString("DefaultConnection")!;
        if (new SqlConnectionStringBuilder(connection).MultipleActiveResultSets)
            throw new InvalidOperationException("C03_REQUIRES_SQL_SAVEPOINTS_DISABLE_MARS");
        foreach (var endpoint in new[] { options.DaprHttpEndpoint, options.DaprGrpcEndpoint })
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme is not ("http" or "https") ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
                throw new InvalidOperationException("C03_DAPR_ENDPOINT_MUST_BE_LOCAL_SIDECAR");
        var validator = new ErpEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/erp")));
        services.AddSingleton(validator);
        services.AddSingleton(provider => new ErpIntegrationRuntime(options, validator, provider.GetService<TimeProvider>()));
        services.AddSingleton<IDbContextFactory<ErpIntegrationContext>>(new ErpQueueContextFactory(connection));
        services.AddScoped<ErpInboxReplayService>();
        services.AddScoped<ErpDeliveryReplayService>();
        services.AddScoped(provider => new ErpRequestHandler(provider.GetRequiredService<IDbContextFactory<ErpIntegrationContext>>(),
            provider.GetRequiredService<ErpIntegrationRuntime>(),
            db => ActivatorUtilities.CreateInstance<OrderService>(provider, db, new FxRateService(db))));
        services.AddHostedService<ErpEventDispatchWorker>();
        services.AddHostedService<ErpCommandRetryWorker>();
        services.AddHostedService<ErpOrderBridgeWorker>();
        services.AddCp6JwtBearer(new Cp6JwtBearerProfile
        {
            Authority = oidc.Issuer, Issuer = oidc.Issuer, Audiences = ["CP6.Services"], ClockSkew = TimeSpan.FromSeconds(60)
        }, ErpReadController.Scheme);
        services.Configure<AuthenticationOptions>(o => o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme);
        return services;
    }

    public static void MapErpIntegrationEvents(this WebApplication app)
    {
        var runtime = app.Services.GetService<ErpIntegrationRuntime>();
        if (runtime is null) return;
        app.MapGet("/dapr/subscribe", (HttpContext context) => AuthorizedSidecar(context, runtime.Options)
            ? Results.Json(new[] { new { pubsubname = "cp6-kafka-pubsub", topic = ErpEventContracts.RequestTopic,
                route = "/internal/erp/v1/events", deadLetterTopic = "cp6.crm.deadletter.v1", metadata = new { rawPayload = "false" } } })
            : Results.Unauthorized()).AllowAnonymous();
        app.MapPost("/internal/erp/v1/events", async (HttpContext context, ErpRequestHandler handler) =>
        {
            if (!AuthorizedSidecar(context, runtime.Options)) return Results.Unauthorized();
            var topic = Single(context, "__topic"); var partition = Single(context, "__key");
            if (topic != ErpEventContracts.RequestTopic || string.IsNullOrEmpty(partition) || partition.Length > 512 ||
                Single(context, "pubsubname") != "cp6-kafka-pubsub" || context.Request.ContentLength > 1_048_576 ||
                context.Request.ContentType?.Split(';')[0].Trim() != "application/cloudevents+json")
                return Results.Json(new { status = "DROP" });
            using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) != 0)
            {
                if (bytes.Length + count > 1_048_576) return Results.Json(new { status = "DROP" });
                bytes.Write(buffer, 0, count);
            }
            var result = await handler.ConsumeAsync(bytes.ToArray(), topic, partition, context.RequestAborted);
            var status = result.Disposition switch
            {
                Cp6InboxDisposition.Applied or Cp6InboxDisposition.Duplicate or Cp6InboxDisposition.IgnoredOutOfOrder => "SUCCESS",
                Cp6InboxDisposition.RetryScheduled => "RETRY",
                _ => "DROP"
            };
            if (status != "SUCCESS") app.Logger.LogWarning("C03 delivery outcome {Outcome}; code {Code}.", status, result.ErrorCode);
            return Results.Json(new { status });
        }).AllowAnonymous();
    }

    public static bool AuthorizedSidecar(HttpContext context, ErpIntegrationOptions options)
    {
        var token = Single(context, "dapr-api-token");
        return options.DaprAppToken.Length >= 32 && token is { Length: <= 4096 } && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(options.DaprAppToken));
    }
    private static string? Single(HttpContext context, string name)
        => context.Request.Headers.TryGetValue(name, out var values) && values.Count == 1 ? values[0] : null;

    private sealed class ErpQueueContextFactory(string connection) : IDbContextFactory<ErpIntegrationContext>
    {
        public ErpIntegrationContext CreateDbContext() => new(new DbContextOptionsBuilder<ErpIntegrationContext>().UseSqlServer(connection).Options);
    }
}
