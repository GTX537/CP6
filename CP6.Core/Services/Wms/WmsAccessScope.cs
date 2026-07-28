using System.Linq.Expressions;
using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

public sealed record WmsScopeGrant(string WarehouseCd, string? AreaCd);

public sealed class WmsAccessScope
{
    public static WmsAccessScope All { get; } = new(true, Array.Empty<WmsScopeGrant>());
    public static WmsAccessScope None { get; } = new(false, Array.Empty<WmsScopeGrant>());

    public WmsAccessScope(bool allowsAll, IReadOnlyList<WmsScopeGrant> grants)
    {
        AllowsAll = allowsAll;
        Grants = grants;
    }

    public bool AllowsAll { get; }
    public IReadOnlyList<WmsScopeGrant> Grants { get; }

    public bool Allows(string? warehouseCd, string? areaCd)
    {
        if (AllowsAll) return true;
        if (string.IsNullOrWhiteSpace(warehouseCd)) return false;
        return Grants.Any(grant =>
            (grant.WarehouseCd == "*"
             || string.Equals(grant.WarehouseCd, warehouseCd,
                 StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(grant.AreaCd)
                || grant.AreaCd == "*"
                || string.Equals(grant.AreaCd, areaCd,
                    StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// True when at least one grant reaches any area in the warehouse. This is
    /// suitable for area-aware operations such as replenishment generation.
    /// </summary>
    public bool AllowsAnyArea(string? warehouseCd)
    {
        if (AllowsAll) return true;
        if (string.IsNullOrWhiteSpace(warehouseCd)) return false;
        return Grants.Any(grant =>
            grant.WarehouseCd == "*"
            || string.Equals(grant.WarehouseCd, warehouseCd,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Warehouse-level optimization reads every location and therefore
    /// requires a warehouse-wide grant rather than a single-area grant.
    /// </summary>
    public bool AllowsWarehouse(string? warehouseCd)
    {
        if (AllowsAll) return true;
        if (string.IsNullOrWhiteSpace(warehouseCd)) return false;
        return Grants.Any(grant =>
            (grant.WarehouseCd == "*"
             || string.Equals(grant.WarehouseCd, warehouseCd,
                 StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(grant.AreaCd)
                || grant.AreaCd == "*"));
    }

    public IQueryable<MobileTask> Apply(IQueryable<MobileTask> query)
        => Apply(query, nameof(MobileTask.WarehouseCd),
            nameof(MobileTask.AreaCd), requireWarehouseWide: false);

    public IQueryable<Location> Apply(IQueryable<Location> query)
        => Apply(query, nameof(Location.WarehouseCd),
            nameof(Location.AreaCd), requireWarehouseWide: false);

    public IQueryable<SlottingPlan> Apply(IQueryable<SlottingPlan> query)
        => Apply(query, nameof(SlottingPlan.WarehouseCd),
            areaProperty: null, requireWarehouseWide: true);

    private IQueryable<T> Apply<T>(
        IQueryable<T> query,
        string warehouseProperty,
        string? areaProperty,
        bool requireWarehouseWide)
    {
        if (AllowsAll) return query;
        if (Grants.Count == 0) return query.Where(_ => false);

        var row = Expression.Parameter(typeof(T), "row");
        var warehouse = Expression.Property(row, warehouseProperty);
        var area = areaProperty is null
            ? null
            : Expression.Property(row, areaProperty);
        Expression? allowed = null;

        foreach (var grant in Grants)
        {
            if (requireWarehouseWide
                && !string.IsNullOrWhiteSpace(grant.AreaCd)
                && grant.AreaCd != "*")
                continue;

            Expression current = grant.WarehouseCd == "*"
                ? Expression.Constant(true)
                : Expression.Equal(
                    warehouse,
                    Expression.Constant(grant.WarehouseCd, typeof(string)));
            if (area is not null
                && !string.IsNullOrWhiteSpace(grant.AreaCd)
                && grant.AreaCd != "*")
            {
                current = Expression.AndAlso(
                    current,
                    Expression.Equal(
                        area,
                        Expression.Constant(grant.AreaCd, typeof(string))));
            }
            allowed = allowed is null ? current : Expression.OrElse(allowed, current);
        }

        var predicate = Expression.Lambda<Func<T, bool>>(
            allowed ?? Expression.Constant(false),
            row);
        return query.Where(predicate);
    }
}

public interface IWmsAccessScopeProvider
{
    Task<WmsAccessScope> GetCurrentAsync(CancellationToken ct = default);
}

public sealed class FixedWmsAccessScopeProvider(WmsAccessScope scope)
    : IWmsAccessScopeProvider
{
    public Task<WmsAccessScope> GetCurrentAsync(CancellationToken ct = default)
        => Task.FromResult(scope);
}

public sealed class WmsAccessDeniedException()
    : UnauthorizedAccessException("WM-V2-SCOPE-DENIED");
