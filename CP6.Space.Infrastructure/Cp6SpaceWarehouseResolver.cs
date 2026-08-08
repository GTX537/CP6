using CP6.Core.EFDbContext;
using CP6.Space.Application;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class Cp6SpaceWarehouseResolver : ISpaceWarehouseResolver
{
    private readonly CP6Context _context;

    public Cp6SpaceWarehouseResolver(CP6Context context)
    {
        _context = context;
    }

    public async Task<SpaceWarehouseIdentity?> ResolveAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        if (siteId == Guid.Empty)
            throw new ArgumentException("Site is required.", nameof(siteId));

        var site = await _context.Space_Sites
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == siteId,
                cancellationToken);
        return site is null
            ? null
            : new SpaceWarehouseIdentity(
                site.Id,
                site.SiteCode,
                string.IsNullOrWhiteSpace(site.WarehouseCd)
                    ? site.SiteCode
                    : site.WarehouseCd);
    }
}

public sealed class SpaceExcelBindingAuthorityResolver(
    ISpaceWarehouseResolver warehouses,
    ISpaceWmsRuntimeSource runtimeSource) :
    ISpaceExcelBindingAuthorityResolver
{
    public async Task<SpaceExcelBindingAuthority?> ResolveAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        var warehouse = await warehouses.ResolveAsync(siteId, cancellationToken);
        if (warehouse is null)
            return null;
        if (string.IsNullOrWhiteSpace(runtimeSource.RuntimeAdapterId) ||
            runtimeSource.RuntimeAdapterId.Length > 100 ||
            string.IsNullOrWhiteSpace(warehouse.WarehouseCode) ||
            warehouse.WarehouseCode.Length > 100)
        {
            throw new InvalidOperationException(
                "The active WMS adapter or warehouse identity is invalid.");
        }
        return new SpaceExcelBindingAuthority(
            siteId,
            runtimeSource.RuntimeAdapterId.Trim(),
            warehouse.WarehouseCode.Trim());
    }
}
