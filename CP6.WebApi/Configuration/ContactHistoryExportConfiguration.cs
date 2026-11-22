using CP6.Core.Persistence;
using CP6.Core.Services.Platform;
using CP6.WebApi.Services;

namespace CP6.WebApi.Configuration;

public sealed class ContactHistoryExportOptionsV11
{
    public bool Enabled { get; set; }
    public string Epoch { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
    public int DownloadWindowSeconds { get; set; } = 30;
    public List<ContactHistoryExportClientV11> Clients { get; set; } = [];
}

public sealed class ContactHistoryExportClientV11
{
    public Guid TenantId { get; set; }
    public string ClientId { get; set; } = "";
    public bool CanReconcile { get; set; }
}

public sealed class ContactHistoryExportRegistrationV11
{
    private readonly Dictionary<(Guid, string), bool> clients = [];
    public bool Enabled { get; }

    internal ContactHistoryExportRegistrationV11(ContactHistoryExportOptionsV11 options, CrmOidcOptions oidc, DatabaseProvider provider)
    {
        Enabled = options.Enabled;
        if (!Enabled) return;
        if (provider != DatabaseProvider.PostgreSql || !oidc.Enabled || options.Clients.Count == 0)
            throw new InvalidOperationException("联系人历史导出需要 PostgreSQL 和启用的 CRM 服务注册。");
        foreach (var client in options.Clients)
        {
            if (!oidc.ServiceClients.Any(entry => entry.Enabled && entry.TenantId == client.TenantId && entry.ClientId == client.ClientId) ||
                !oidc.Organizations.Any(organization => organization.CrmEnabled && organization.TenantId == client.TenantId) ||
                !clients.TryAdd((client.TenantId, client.ClientId), client.CanReconcile))
                throw new InvalidOperationException("联系人历史导出调用者须唯一绑定到 CRM 已启用的租户和服务身份。");
        }
    }

    internal bool TryResolve(Guid tenant, string clientId, out bool canReconcile) =>
        clients.TryGetValue((tenant, clientId), out canReconcile);
}

internal static class ContactHistoryExportConfiguration
{
    internal static void AddContactHistoryExport(IServiceCollection services, IConfiguration configuration, CrmOidcOptions oidc, DatabaseProvider provider)
    {
        var options = configuration.GetSection("ContactHistoryExport").Get<ContactHistoryExportOptionsV11>() ?? new();
        services.AddSingleton(new ContactHistoryExportRegistrationV11(options, oidc, provider));
        if (!options.Enabled) return;
        services.AddSingleton(new ContactHistoryExportSignerV11(options.Epoch, options.KeyId, options.PrivateKeyPem, options.DownloadWindowSeconds));
        services.AddScoped<ContactHistoryExportOwnerV11>();
    }
}
