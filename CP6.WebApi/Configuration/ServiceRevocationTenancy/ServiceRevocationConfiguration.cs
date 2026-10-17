using System.Net;
using CP6.Core.Persistence;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Services;
using CP6.WebApi.Services.ServiceRevocationTenancy;
using Npgsql;

namespace CP6.WebApi.Configuration.ServiceRevocationTenancy;

internal sealed class ServiceRevocationOptions
{
    public bool Enabled { get; set; }
    public string HostId { get; set; } = "";
    public string Revision { get; set; } = "";
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset ValidUntilUtc { get; set; }
    public int SourceLifetimeSeconds { get; set; } = 60;
    public string Server { get; set; } = "";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "";
    public uint DatabaseOid { get; set; }
    public string SystemIdentifier { get; set; } = "";
    public Guid InstallationId { get; set; }
    public string SslMode { get; set; } = "VerifyFull";
    public List<ServiceRevocationLogin> Tenants { get; set; } = [];
    public List<ServiceRevocationClient> Clients { get; set; } = [];
}
internal sealed class ServiceRevocationLogin
{
    public Guid TenantId { get; set; }
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string CredentialVersion { get; set; } = "";
}
internal sealed class ServiceRevocationClient
{
    public string ClientId { get; set; } = "";
    public string RegistrationVersion { get; set; } = "";
    public bool AllowSelfRevoke { get; set; }
}

internal static class ServiceRevocationConfiguration
{
    internal static void Register(IServiceCollection services, IConfiguration configuration, CrmOidcOptions oidc,
        CrmIdentityOptions identity, DatabaseOptions database)
    {
        var tokenSection = configuration.GetSection("CrmIdentity:ServiceTokenTenancy");
        var legacySection = configuration.GetSection("CrmIdentity:ServiceRevocationTenancy");
        if (tokenSection.GetValue<bool>("Enabled") && legacySection.GetValue<bool>("Enabled")) throw Rejected();
        var serviceTokens = tokenSection.GetValue<bool>("Enabled") || tokenSection.Exists() && !legacySection.GetValue<bool>("Enabled");
        if (tokenSection.Exists() && !tokenSection.GetValue<bool>("Enabled") && tokenSection.GetChildren().Any(x => x.Key != "Enabled")) throw Rejected();
        if (serviceTokens && legacySection.Exists() && legacySection.GetChildren().Any(x => x.Key != "Enabled")) throw Rejected();
        var section = serviceTokens ? tokenSection : legacySection;
        if (!section.Exists()) return;
        var options = section.Get<ServiceRevocationOptions>(o => o.ErrorOnUnknownConfiguration = true) ?? throw Rejected();
        if (!options.Enabled)
        {
            if (section.GetChildren().Any(x => x.Key != "Enabled")) throw Rejected();
            return;
        }
        if (!identity.Enabled || !oidc.Enabled || database.Provider != DatabaseProvider.PostgreSql) throw Rejected();
        // Numeric endpoints deliberately reject aliases, multi-host failover and implicit default database/user.
        var issuance = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        if (!IPAddress.TryParse(options.Server, out var target) || !IPAddress.TryParse(issuance.Host, out var source)
            || !target.Equals(source) || options.Port != issuance.Port || options.Database != issuance.Database
            || string.IsNullOrEmpty(issuance.Database) || string.IsNullOrEmpty(issuance.Username)
            || options.Tenants.Any(t => t.Login == issuance.Username)
            || options.DatabaseOid == 0 || options.InstallationId == Guid.Empty
            || !ulong.TryParse(options.SystemIdentifier, out var systemId) || systemId == 0
            || options.SourceLifetimeSeconds is < 1 or > 300
            || options.ValidFromUtc.Offset != TimeSpan.Zero || options.ValidUntilUtc.Offset != TimeSpan.Zero
            || options.ValidUntilUtc <= options.ValidFromUtc
            || !Enum.TryParse<SslMode>(options.SslMode, false, out var ssl) || ssl is not (SslMode.VerifyFull or SslMode.Disable)
            || options.Tenants.Count == 0 || options.Clients.Count == 0
            || options.HostId.Length > 128 || options.Revision.Length > 60
            || options.Clients.Select(x => x.ClientId).Distinct(StringComparer.Ordinal).Count() != options.Clients.Count
            || options.Tenants.Any(t => !identity.Tenants.ContainsKey(t.TenantId) || string.IsNullOrEmpty(t.Password))
            || oidc.ServiceClients.Select(x => x.ClientId).Distinct(StringComparer.Ordinal).Count() != oidc.ServiceClients.Count)
            throw Rejected();
        TenantStorageBindingV1.Text(options.HostId); TenantStorageBindingV1.Text(options.Revision);
        foreach (var client in options.Clients)
        {
            TenantStorageBindingV1.Text(client.RegistrationVersion);
            if (client.RegistrationVersion.Length > 60) throw Rejected();
            var registered = oidc.ServiceClients.SingleOrDefault(x => x.ClientId == client.ClientId);
            if (registered is null || !System.Text.RegularExpressions.Regex.IsMatch(registered.SecretSha256, "\\A[A-Fa-f0-9]{64}\\z")
                || !options.Tenants.Any(t => t.TenantId == registered.TenantId)
                || !oidc.Organizations.Any(o => o.TenantId == registered.TenantId && o.Region == identity.Tenants[registered.TenantId]))
                throw Rejected();
        }
        // Construct now so all routing/options validation occurs at registration, never first traffic.
        var frozen = ServiceRevocationTenantHost.Freeze(options, oidc, identity, serviceTokens);
        if (frozen.SigningSnapshot is not null) services.AddSingleton(frozen.SigningSnapshot);
        services.AddSingleton(provider => new ServiceRevocationTenantHost(frozen,
            provider.GetRequiredService<CrmIdentityRuntime>(), provider.GetService<TimeProvider>() ?? TimeProvider.System));
    }
    private static InvalidOperationException Rejected() => new("C02_SERVICE_REVOCATION_CONFIGURATION_REJECTED");
}
