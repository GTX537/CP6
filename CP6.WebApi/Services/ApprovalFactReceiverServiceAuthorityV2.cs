using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;

namespace CP6.WebApi.Services;

public sealed class ApprovalFactReceiverOptionsV2
{
    public bool Enabled { get; set; }
    public string Version { get; set; } = "";
    public Guid EnvironmentId { get; set; }
    public string EvidenceMode { get; set; } = "REAL";
    public List<ApprovalFactReceiverClientV2> Clients { get; set; } = [];
}
public sealed class ApprovalFactReceiverClientV2
{
    public string ClientId { get; set; } = "";
    public Guid TenantId { get; set; }
    public Guid PrincipalId { get; set; }
    public bool CanReceive { get; set; }
}
// Separate feature registration; intake permission never implicitly grants fact reception.
public sealed class ApprovalFactReceiverRegistryV2
{
    internal ApprovalIntakeRegistryV2 Transport { get; }
    public ApprovalFactReceiverRegistryV2(ApprovalFactReceiverOptionsV2 o, CrmOidcOptions oidc, bool development)
    {
        Transport = new(new ApprovalIntakeOptionsV2 { Enabled = o.Enabled, Version = o.Version, EnvironmentId = o.EnvironmentId,
            EvidenceMode = o.EvidenceMode, Clients = o.Clients.Select(c => new ApprovalIntakeClientV2 { ClientId = c.ClientId,
                TenantId = c.TenantId, PrincipalId = c.PrincipalId, CanSubmit = c.CanReceive }).ToList() }, oidc, development);
    }
}
public sealed class ApprovalFactReceiverServiceAuthorityV2(CP6Context db, IHttpContextAccessor http,
    ApprovalFactReceiverRegistryV2 registry, CrmOidcOptions oidc) : IApprovalServiceAuthorityV2
{
    internal static async Task BindTenant(HttpContext http, CancellationToken ct)
    {
        var registry = http.RequestServices.GetRequiredService<ApprovalFactReceiverRegistryV2>();
        var oidc = http.RequestServices.GetRequiredService<CrmOidcOptions>();
        var facts = await ApprovalIntakeServiceAuthorityV2.Authenticate(http, registry.Transport, oidc, ct);
        http.RequestServices.GetRequiredService<ITenantContext>().CurrentTenantId = facts.Client.Tenant;
    }
    public Task<ApprovalServiceIdentityV2> CurrentAsync(CancellationToken ct)
        => new ApprovalIntakeServiceAuthorityV2(db, http, registry.Transport, oidc).CurrentAsync(ct);
}
