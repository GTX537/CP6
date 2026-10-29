namespace CP6.Core.Services.Wms;

// Trusted deployment coordinates for existing warehouses; never accepted from a browser command.
public sealed class RmaApplicationOptionsV1
{
    public RmaWarehouseSiteV1[] WarehouseSites { get; set; } = [];
    public bool IsValid() => WarehouseSites.All(x => x.TenantId != Guid.Empty && x.WarehouseId != Guid.Empty && x.SiteId != Guid.Empty)
        && WarehouseSites.Select(x => (x.TenantId, x.WarehouseId)).Distinct().Count() == WarehouseSites.Length;
}
public sealed class RmaWarehouseSiteV1
{
    public Guid TenantId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid SiteId { get; set; }
}
