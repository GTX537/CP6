using CP6.Core.Persistence;
using CP6.Core.Services.Pub;
using CP6.WebApi.Services;

namespace CP6.WebApi.Configuration;

public sealed class ActFileReferenceOptionsV1
{
    public bool Enabled { get; set; }
    public string KeyId { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
    public List<ActFileClientOptionsV1> Clients { get; set; } = [];
}

public sealed class ActFileClientOptionsV1
{
    public Guid TenantId { get; set; }
    public string ClientId { get; set; } = "";
    public Guid StoreId { get; set; }
    public string ReadConnectionString { get; set; } = "";
}

public sealed class ActFileReferenceRegistrationV1
{
    private readonly Dictionary<Guid, string> clients = [];
    public bool Enabled { get; }

    internal ActFileReferenceRegistrationV1(ActFileReferenceOptionsV1 options, CrmOidcOptions oidc, DatabaseProvider provider)
    {
        Enabled = options.Enabled;
        if (!Enabled) return;
        if (provider != DatabaseProvider.PostgreSql || !oidc.Enabled || options.Clients.Count == 0)
            throw new InvalidOperationException("ACT 文件提供者需要 PostgreSQL 与已启用的 CRM 服务登记。");
        foreach (var client in options.Clients)
        {
            if (client.TenantId == Guid.Empty || client.StoreId == Guid.Empty ||
                !oidc.ServiceClients.Any(c => c.Enabled && c.TenantId == client.TenantId && c.ClientId == client.ClientId &&
                    c.AllowedScopes.Contains("cp6.services", StringComparer.Ordinal)) ||
                !oidc.Organizations.Any(o => o.CrmEnabled && o.TenantId == client.TenantId) ||
                !clients.TryAdd(client.TenantId, client.ClientId))
                throw new InvalidOperationException("ACT 文件调用者必须唯一绑定到已登记的 CRM 租户、存储和服务身份。");
        }
    }

    internal bool Allows(Guid tenant, string client) => clients.TryGetValue(tenant, out var registered) && registered == client;
}

internal static class ActFileReferenceConfigurationV1
{
    internal static void Register(IServiceCollection services, IConfiguration configuration, CrmOidcOptions oidc, DatabaseProvider provider)
    {
        var options = configuration.GetSection("ActFileReference").Get<ActFileReferenceOptionsV1>() ?? new();
        services.AddSingleton(new ActFileReferenceRegistrationV1(options, oidc, provider));
        if (!options.Enabled) return;
        services.AddSingleton(_ => new ActFileSignerV1(options.KeyId, options.PrivateKeyPem));
        services.AddSingleton(_ => new ActFileGuardReadV1(options.Clients.ToDictionary(x => x.TenantId,
            x => new ActFileGuardBindingV1(x.StoreId, x.ReadConnectionString))));
        services.AddScoped<ActFileReferenceOwnerV1>();
    }
}
