using System.Net;
using System.Text;
using CP6.Core.Persistence;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Services;
using CP6.WebApi.Services.IdentityDirectoryTenancy;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace CP6.WebApi.Configuration.IdentityDirectoryTenancy;

internal sealed class IdentityDirectoryOptions
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
    public string Fingerprint { get; set; } = "";
    public string SslMode { get; set; } = "VerifyFull";
    public List<IdentityDirectoryLogin> Tenants { get; set; } = [];
    public List<IdentityDirectoryClient> Clients { get; set; } = [];
}
internal sealed class IdentityDirectoryLogin
{
    public Guid TenantId { get; set; }
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string CredentialVersion { get; set; } = "";
}
internal sealed class IdentityDirectoryClient
{
    public string ClientId { get; set; } = "";
    public string RegistrationVersion { get; set; } = "";
}

internal static class IdentityDirectoryConfiguration
{
    internal static void Register(IServiceCollection services, IConfiguration configuration, CrmOidcOptions oidc,
        CrmIdentityOptions identity, DatabaseOptions database)
    {
        var section = configuration.GetSection("CrmIdentity:IdentityDirectoryTenancy");
        if (!section.Exists()) return;
        try
        {
            var options = section.Get<IdentityDirectoryOptions>(o => o.ErrorOnUnknownConfiguration = true) ?? throw Rejected();
            if (!options.Enabled)
            {
                if (section.GetChildren().Any(x => x.Key != "Enabled")) throw Rejected();
                return;
            }
            // These fresh-only profiles have incompatible tables/permissions on the same default target.
            if (!identity.Enabled || !oidc.Enabled || database.Provider != DatabaseProvider.PostgreSql ||
                configuration.GetValue<bool>("CrmIdentity:ServiceTokenTenancy:Enabled") ||
                configuration.GetValue<bool>("CrmIdentity:ServiceRevocationTenancy:Enabled")) throw Rejected();
            var control = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
            if (!IPAddress.TryParse(options.Server, out var target) || target.ToString() != options.Server ||
                !IPAddress.TryParse(control.Host, out var source) || !target.Equals(source) || options.Port != control.Port ||
                options.Port is < 1 or > 65535 || options.Database != control.Database || string.IsNullOrEmpty(control.Database) ||
                string.IsNullOrWhiteSpace(control.Username) || options.DatabaseOid == 0 || options.InstallationId == Guid.Empty ||
                !ulong.TryParse(options.SystemIdentifier, out var system) || system == 0 || system.ToString(System.Globalization.CultureInfo.InvariantCulture) != options.SystemIdentifier ||
                options.Fingerprint.Length != 64 || options.Fingerprint.Any(c => !char.IsAsciiHexDigitUpper(c)) ||
                options.SourceLifetimeSeconds is < 1 or > 300 || options.ValidFromUtc.Offset != TimeSpan.Zero ||
                options.ValidUntilUtc.Offset != TimeSpan.Zero || options.ValidUntilUtc <= options.ValidFromUtc ||
                !Enum.TryParse<SslMode>(options.SslMode, false, out var ssl) || ssl is not (SslMode.VerifyFull or SslMode.Disable) ||
                options.HostId.Length > 128 || options.Revision.Length > 60 || options.Tenants.Count is < 1 or > 128 || options.Clients.Count is < 1 or > 128 ||
                options.Clients.Select(x => x.ClientId).Distinct(StringComparer.Ordinal).Count() != options.Clients.Count ||
                oidc.ServiceClients.Select(x => x.ClientId).Distinct(StringComparer.Ordinal).Count() != oidc.ServiceClients.Count ||
                options.Tenants.Any(t => !identity.Tenants.ContainsKey(t.TenantId) || string.IsNullOrEmpty(t.Password) ||
                    t.Login == control.Username || Encoding.UTF8.GetByteCount(t.Login) > 63)) throw Rejected();
            TenantStorageBindingV1.Text(options.HostId); TenantStorageBindingV1.Text(options.Revision);
            foreach (var client in options.Clients)
            {
                TenantStorageBindingV1.Text(client.RegistrationVersion);
                var registered = oidc.ServiceClients.SingleOrDefault(c => c.ClientId == client.ClientId);
                if (client.RegistrationVersion.Length > 60 || registered is null || !registered.Enabled ||
                    !registered.AllowedScopes.Contains("cp6.services", StringComparer.Ordinal) ||
                    !identity.ProjectionReaderClientIds.Contains(client.ClientId, StringComparer.Ordinal) ||
                    !options.Tenants.Any(t => t.TenantId == registered.TenantId) ||
                    !oidc.Organizations.Any(o => o.TenantId == registered.TenantId && o.CrmEnabled && o.Region == identity.Tenants[registered.TenantId]))
                    throw Rejected();
            }
            // Validate and copy every routing/credential/registration input now, before first traffic.
            var frozen = IdentityDirectoryTenantHost.Freeze(options, oidc, identity);
            services.AddSingleton(provider => new IdentityDirectoryTenantHost(frozen,
                provider.GetRequiredService<CrmIdentityRuntime>(), provider.GetRequiredService<IDataProtectionProvider>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System));
        }
        catch { throw Rejected(); } // Never echo a secret connection string, binder value or credential.
    }
    private static InvalidOperationException Rejected() => new("C02_DIRECTORY_CONFIGURATION_REJECTED");
}
