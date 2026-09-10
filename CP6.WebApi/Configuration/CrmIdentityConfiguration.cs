using CP6.Core.Services.CrmIdentity;
using CP6.Platform.AspNetCore;
using CP6.Platform.Messaging;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;

namespace CP6.WebApi.Configuration;

public static class CrmIdentityConfiguration
{
    public static CrmIdentityOptions BindOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("CrmIdentity");
        var options = section.Get<CrmIdentityOptions>() ?? new();
        if (!options.Enabled) return options;
        // The .NET configuration binder does not populate dictionaries with Guid keys.
        // Parse the operator's map explicitly so JSON and environment overrides share
        // the same validation instead of silently dropping configured tenants.
        options.Tenants.Clear();
        foreach (var tenant in section.GetSection("Tenants").GetChildren())
        {
            if (!Guid.TryParseExact(tenant.Key, "D", out var id) || id == Guid.Empty ||
                string.IsNullOrWhiteSpace(tenant.Value) || tenant.GetChildren().Any() ||
                !options.Tenants.TryAdd(id, tenant.Value))
                throw new InvalidOperationException("C02_REQUIRES_VALID_TENANT_MAP");
        }
        if (options.Tenants.Count == 0) throw new InvalidOperationException("C02_REQUIRES_VALID_TENANT_MAP");
        return options;
    }

    public static IServiceCollection AddCrmIdentityEvents(this IServiceCollection services, IConfiguration configuration, CrmOidcOptions oidc)
    {
        var options = BindOptions(configuration);
        if (!options.Enabled) return services;
        if (!oidc.Enabled) throw new InvalidOperationException("C02_REQUIRES_ENABLED_CRM_ISSUER");
        if (new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection")).MultipleActiveResultSets)
            throw new InvalidOperationException("C02_REQUIRES_SQL_SAVEPOINTS_DISABLE_MARS");
        options.Issuer = oidc.Issuer;
        options.PasswordMaxAgeDays = configuration.GetValue<int>("Security:Password:ExpiryDays");
        if (options.Tenants.Any(t => !oidc.Organizations.Any(o => o.TenantId == t.Key && o.Region == t.Value && o.CrmEnabled)) ||
            options.ProjectionReaderClientIds.Distinct(StringComparer.Ordinal).Count() != options.ProjectionReaderClientIds.Length ||
            options.ProjectionReaderClientIds.Any(id => !oidc.ServiceClients.Any(c => c.ClientId == id && c.Enabled &&
                options.Tenants.ContainsKey(c.TenantId) && c.AllowedScopes.Contains("cp6.services", StringComparer.Ordinal))))
            throw new InvalidOperationException("C02_REQUIRES_EXPLICIT_MAPPED_READERS");
        foreach (var endpoint in new[] { options.DaprHttpEndpoint, options.DaprGrpcEndpoint })
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme is not ("http" or "https") ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
                throw new InvalidOperationException("C02_DAPR_ENDPOINT_MUST_BE_LOCAL_SIDECAR");
        var directory = Path.Combine(AppContext.BaseDirectory, "contracts/events/platform");
        var bundle = Cp6ContractBundle.Load(directory);
        var validator = new IdentityEventValidator(bundle, oidc.Issuer);
        services.AddSingleton(validator);
        services.AddSingleton(provider => new CrmIdentityRuntime(options, validator, provider.GetService<TimeProvider>(), directory));
        services.AddScoped<IdentitySnapshotReader>();
        services.AddScoped<IdentityBootstrapService>();
        services.AddScoped<ICrmServiceTokenRecordStore, CrmServiceTokenRecordStore>();
        services.AddHostedService<IdentityBootstrapWorker>();
        services.AddHostedService<IdentityEventDispatchWorker>();
        services.AddCp6JwtBearer(new Cp6JwtBearerProfile
        {
            Authority = oidc.Issuer, Issuer = oidc.Issuer, Audiences = ["CP6.Services"], ClockSkew = TimeSpan.FromSeconds(60)
        }, CrmIdentityController.Scheme);
        // The SDK registration selects its scheme by default. Existing application/native routes
        // keep their established bearer scheme; only the internal controller requests C02.Services.
        services.Configure<AuthenticationOptions>(o => o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme);
        return services;
    }
}
