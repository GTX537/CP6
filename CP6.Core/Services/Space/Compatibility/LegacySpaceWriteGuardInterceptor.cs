using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity;
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Core.Services.Space.Compatibility;

/// <summary>
/// Blocks writes through the legacy Space model once a Site enters freeze/cutover.
/// Reads are deliberately untouched so published runtime data remains compatible.
/// </summary>
public sealed class LegacySpaceWriteGuardInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenant;
    private readonly ISpaceCompatibilityGate _gate;

    public LegacySpaceWriteGuardInterceptor(
        ITenantContext tenant,
        ISpaceCompatibilityGate gate)
    {
        _tenant = tenant;
        _gate = gate;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        GuardAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await GuardAsync(eventData.Context, cancellationToken);
        return result;
    }

    private async Task GuardAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is not CP6Context db)
            return;

        var changed = db.ChangeTracker.Entries()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(x => x.Metadata.ClrType.Namespace == typeof(Space_Site).Namespace)
            // E00 Space_AuditEvent is an append-only audit ledger, not a legacy Space
            // model write. CP6Context independently rejects its Modified/Deleted states.
            .Where(x => x.Metadata.ClrType != typeof(Space_AuditEvent))
            .Select(x => x.Entity)
            .ToList();

        if (changed.Count == 0)
            return;

        if (changed
            .OfType<BaseTenantEntity>()
            .Any(x => x.TenantId != _tenant.CurrentTenantId))
            throw new CP6.WebApi.Localization.BizException(
                SpaceCompatibilityErrors.TenantScopeDenied,
                403);

        // Analytics configuration and snapshots are operational control-tower data,
        // not legacy design-model writes. They remain writable after Design V1 cutover.
        changed = changed
            .Where(x => x is not Space_AnalyticsConfig and not Space_AbcSnapshot)
            .ToList();

        if (changed.Count == 0)
            return;

        var siteIds = new HashSet<Guid>();
        var floorIds = new HashSet<Guid>();
        var rackIds = new HashSet<Guid>();
        var zoneIds = new HashSet<Guid>();
        var tenantWide = false;

        foreach (var entity in changed)
        {
            switch (entity)
            {
                case Space_Site site:
                    siteIds.Add(site.Id);
                    break;
                case Space_Floor floor:
                    siteIds.Add(floor.SiteId);
                    break;
                case Space_Zone zone:
                    floorIds.Add(zone.FloorId);
                    break;
                case Space_Aisle aisle:
                    zoneIds.Add(aisle.ZoneId);
                    break;
                case Space_Rack rack:
                    floorIds.Add(rack.FloorId);
                    break;
                case Space_Location location when location.FloorId.HasValue:
                    floorIds.Add(location.FloorId.Value);
                    break;
                case Space_Location location when location.RackId.HasValue:
                    rackIds.Add(location.RackId.Value);
                    break;
                case Space_Location:
                    tenantWide = true;
                    break;
                case Space_Marker marker:
                    floorIds.Add(marker.FloorId);
                    break;
                case Space_Connector connector:
                    siteIds.Add(connector.SiteId);
                    break;
                case Space_ConnectorStop stop:
                    floorIds.Add(stop.FloorId);
                    break;
                case Space_Template:
                case Space_CodeRule:
                    tenantWide = true;
                    break;
                default:
                    // Fail closed for a newly introduced legacy Space entity until
                    // its Site relationship is explicitly mapped here.
                    tenantWide = true;
                    break;
            }
        }

        AddTrackedParents(db, zoneIds, rackIds, floorIds, siteIds);

        if (zoneIds.Count > 0)
        {
            var resolvedZones = await db.Space_Zones
                .AsNoTracking()
                .Where(x => zoneIds.Contains(x.Id))
                .Select(x => new { x.Id, x.FloorId })
                .ToListAsync(cancellationToken);
            foreach (var zone in resolvedZones)
            {
                zoneIds.Remove(zone.Id);
                floorIds.Add(zone.FloorId);
            }
        }

        if (rackIds.Count > 0)
        {
            var resolvedRacks = await db.Space_Racks
                .AsNoTracking()
                .Where(x => rackIds.Contains(x.Id))
                .Select(x => new { x.Id, x.FloorId })
                .ToListAsync(cancellationToken);
            foreach (var rack in resolvedRacks)
            {
                rackIds.Remove(rack.Id);
                floorIds.Add(rack.FloorId);
            }
        }

        AddTrackedFloors(db, floorIds, siteIds);

        if (floorIds.Count > 0)
        {
            var resolvedFloors = await db.Space_Floors
                .AsNoTracking()
                .Where(x => floorIds.Contains(x.Id))
                .Select(x => new { x.Id, x.SiteId })
                .ToListAsync(cancellationToken);
            foreach (var floor in resolvedFloors)
            {
                floorIds.Remove(floor.Id);
                siteIds.Add(floor.SiteId);
            }
        }

        tenantWide |= zoneIds.Count > 0 || rackIds.Count > 0 || floorIds.Count > 0;

        if (tenantWide)
        {
            _gate.EnsureLegacyTenantWideWriteAllowed(_tenant.CurrentTenantId);
            return;
        }

        foreach (var siteId in siteIds)
            _gate.EnsureLegacyWriteAllowed(_tenant.CurrentTenantId, siteId);
    }

    private static void AddTrackedParents(
        CP6Context db,
        HashSet<Guid> zoneIds,
        HashSet<Guid> rackIds,
        HashSet<Guid> floorIds,
        HashSet<Guid> siteIds)
    {
        foreach (var zone in db.ChangeTracker.Entries<Space_Zone>().Select(x => x.Entity))
        {
            if (zoneIds.Remove(zone.Id))
                floorIds.Add(zone.FloorId);
        }

        foreach (var rack in db.ChangeTracker.Entries<Space_Rack>().Select(x => x.Entity))
        {
            if (rackIds.Remove(rack.Id))
                floorIds.Add(rack.FloorId);
        }

        AddTrackedFloors(db, floorIds, siteIds);
    }

    private static void AddTrackedFloors(
        CP6Context db,
        HashSet<Guid> floorIds,
        HashSet<Guid> siteIds)
    {
        foreach (var floor in db.ChangeTracker.Entries<Space_Floor>().Select(x => x.Entity))
        {
            if (floorIds.Remove(floor.Id))
                siteIds.Add(floor.SiteId);
        }
    }
}
