using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;

namespace CP6.WebApi.Services;

public interface IServiceIdentityPublisher
{
    Task PublishAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed class AccountC03ServiceIdentityPublisher(CP6Context db, ITenantContext tenant,
    AccountC03ServicePrincipalRegistryV1 registry, CrmIdentityRuntime runtime) : IServiceIdentityPublisher
{
    public Task PublishAsync(Guid tenantId, CancellationToken ct = default) =>
        InquiryServiceIdentityPublisherV1.PublishSourceAsync(db, tenant, registry, runtime,
            IdentityEventContracts.AccountServicePrincipalChanged, IdentityEventContracts.AccountServiceProfile, tenantId, ct);
}
