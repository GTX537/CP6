using System.Net;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using Npgsql;

namespace CP6.WebApi.Configuration;

internal static class RmaReadTenancyConfiguration
{
    internal static void Add(IServiceCollection services, IConfiguration configuration, DatabaseOptions database, string connectionString)
    {
        var section = configuration.GetSection("RmaReadTenancy");
        if (section.Exists())
        {
            Settings settings;
            try { settings = section.Get<Settings>(options => options.ErrorOnUnknownConfiguration = true) ?? throw Rejected(); }
            catch (InvalidOperationException) { throw Rejected(); }
            if (settings.Enabled)
                RegisterTenantRead(services, settings, database, connectionString);
            else if (section.GetChildren().Any(child => child.Key != nameof(Settings.Enabled)))
                throw Rejected();
        }

        services.AddScoped(sp => new RmaApplicationOwnerV1(sp.GetRequiredService<CP6Context>(),
            sp.GetRequiredService<OperationsOwnerV1>(), sp.GetService<TenantRmaReadStore>()));
    }

    private static void RegisterTenantRead(IServiceCollection services, Settings settings, DatabaseOptions database, string connectionString)
    {
        if (database.Provider != DatabaseProvider.PostgreSql || settings.InstallationId == Guid.Empty
            || settings.Tenants.Length is < 1 or > 128)
            throw Rejected();
        var endpoint = new NpgsqlConnectionStringBuilder(connectionString);
        if (!IPAddress.TryParse(endpoint.Host, out var ip) || string.IsNullOrWhiteSpace(endpoint.Database)
            || string.IsNullOrWhiteSpace(endpoint.Username)
            || !Enum.TryParse<SslMode>(settings.SslMode, out var ssl) || settings.SslMode != ssl.ToString()
            || !(ssl == SslMode.VerifyFull || ssl == SslMode.Disable && IPAddress.IsLoopback(ip))
            || settings.Tenants.Any(t => t is null || string.IsNullOrEmpty(t.Password) || t.Login == endpoint.Username))
            throw Rejected();

        const string databaseRef = "main-db";
        var tenants = settings.Tenants;
        TenantStorageCatalog catalog;
        try
        {
            catalog = new(TenantRmaReadStore.Host, [RmaTenantStorageV1.CoreReadProfile],
                tenants.Select(t => new TenantStorageBindingV1(t.TenantId, TenantRmaReadStore.Host, databaseRef,
                    TenantStorageBindingV1.SupportedLayout, RmaTenantStorageV1.CoreReadProfile, settings.Revision,
                    t.CredentialVersion, t.Login, [], TenantStorageState.Ready, RmaTenantStorageV1.CoreReadMigration)),
                [new(databaseRef, ip.ToString(), endpoint.Port, endpoint.Database, ssl)],
                tenants.Select(t => new TenantPrincipal(t.Login, t.Login)));
        }
        catch (TenantStorageException) { throw Rejected(); }

        var credentials = new Credentials(tenants.ToDictionary(t => t.TenantId, t => t.Password));
        services.AddSingleton(sp => new TenantRmaReadStore(catalog, credentials, sp.GetRequiredService<IHttpContextAccessor>(),
            settings.InstallationId, settings.DraftEnabled, settings.SubmitEnabled));
    }

    private static InvalidOperationException Rejected() => new("RMA_READ_TENANCY_CONFIGURATION_REJECTED");

    private sealed class Settings
    {
        public bool Enabled { get; set; }
        public bool DraftEnabled { get; set; }
        public bool SubmitEnabled { get; set; }
        public Guid InstallationId { get; set; }
        public string Revision { get; set; } = "";
        public string SslMode { get; set; } = nameof(Npgsql.SslMode.VerifyFull);
        public Tenant[] Tenants { get; set; } = [];
    }

    private sealed class Tenant
    {
        public Guid TenantId { get; set; }
        public string Login { get; set; } = "";
        public string Password { get; set; } = "";
        public string CredentialVersion { get; set; } = "";
    }

    private sealed class Credentials(IReadOnlyDictionary<Guid, string> passwords) : ITenantCredentialResolver
    {
        public ValueTask<string> ResolvePasswordAsync(TenantStorageBindingV1 binding, CancellationToken ct) =>
            ValueTask.FromResult(passwords[binding.TenantId]);
    }
}
