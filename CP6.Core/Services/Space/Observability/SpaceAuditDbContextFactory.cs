using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space.Observability;

public sealed class SpaceAuditDbContextFactory : ISpaceAuditDbContextFactory
{
    private readonly DbContextOptions<CP6Context> _options;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserAccessor _user;

    public SpaceAuditDbContextFactory(
        DbContextOptions<CP6Context> options,
        ITenantContext tenant,
        ICurrentUserAccessor user)
    {
        _options = options;
        _tenant = tenant;
        _user = user;
    }

    public CP6Context CreateDbContext() => new(_options, _tenant, _user);
}
