using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.WebApi.Services;

internal sealed class SpaceArtifactWriteAuthority(IServiceScopeFactory scopeFactory, TimeProvider clock)
    : ISpaceArtifactWriteAuthority
{
    public async Task<DateTime?> AuthorizeTenantAsync(SpaceContext space, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContext>().CurrentTenantId = space.CurrentTenantId;
        var core = services.GetRequiredService<CP6Context>();
        core.Database.SetDbConnection(space.Database.GetDbConnection(), contextOwnsConnection: false);
        await core.Database.UseTransactionAsync(space.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken);
        await AuthenticationCompletionGateV1.AcquireAsync(core, cancellationToken);
        var tenant = await core.Sys_Tenants.AsNoTracking()
            .Where(row => row.Id == space.CurrentTenantId)
            .Select(row => new { row.Enable, row.ExpireDate })
            .SingleOrDefaultAsync(cancellationToken);
        if (tenant is not { Enable: true })
            throw Inactive();
        ConfirmTenantExpiry(tenant.ExpireDate);
        return tenant.ExpireDate;
    }

    public void ConfirmTenantExpiry(DateTime? expiresAt)
    {
        if (expiresAt <= clock.GetLocalNow().DateTime)
            throw Inactive();
    }

    private static SpaceJobProcessingException Inactive() => new(SpaceJobFailureKind.Transient, "SPACE_ARTIFACT_TENANT_INACTIVE",
        "The tenant is paused or expired. The artifact Job can retry when the tenant is active.");
}
