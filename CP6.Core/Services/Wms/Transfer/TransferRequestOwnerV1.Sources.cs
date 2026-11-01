using System.Globalization;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Space;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.Transfer.TransferEnvironmentStoreV1;
using Store = CP6.Core.Services.Wms.Transfer.TransferRequestStoreV1;

namespace CP6.Core.Services.Wms.Transfer;

public sealed partial class TransferRequestOwnerV1
{
    private sealed record ResolvedLine(Guid Id, TransferRequestLineInputV1 Input, InventoryProductOptionV1 Product,
        EngineeringSourceRefV1 SourceSiteRef, EngineeringSourceRefV1 DestinationSiteRef, string BaseQuantity, string BudgetKey);
    private async Task<Dictionary<Guid, EngineeringSourceRefV1>> Sites(OperationsAccessV1 a, TransferEnvironmentStoreV1 s,
        IEnumerable<Guid> warehouses, CancellationToken ct)
    {
        var configured = options.Value.WarehouseSites.ToArray();
        if (configured.Any(x => x == null || x.WarehouseId == Guid.Empty || x.SiteId == Guid.Empty)
            || configured.Select(x => x.WarehouseId).Distinct().Count() != configured.Length) throw Error("TRANSFER_SITE_CONFIGURATION_INVALID");
        var source = await a.SourceAsync(OperationsOwnerV1.PhysicalSite, ct); var result = new Dictionary<Guid, EngineeringSourceRefV1>();
        foreach (var warehouse in warehouses.Distinct())
        {
            var mapping = configured.SingleOrDefault(x => x.WarehouseId == warehouse) ?? throw Error("TRANSFER_WAREHOUSE_SITE_REQUIRED", 422);
            var site = await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db, source, mapping.SiteId, ct);
            if (!site.Enabled) throw Error("TRANSFER_SITE_DISABLED", 422);
            await s.RequireSource(site.Reference, ct); result.Add(warehouse, site.Reference);
        }
        a.AtCommit(t =>
        {
            if (!configured.SequenceEqual(options.Value.WarehouseSites)) throw Error("TRANSFER_SITE_CONFIGURATION_CHANGED", 412);
            return Task.CompletedTask;
        });
        return result;
    }
    private async Task<ResolvedLine[]> Resolve(OperationsAccessV1 a, TransferEnvironmentRegistrationV1 registration,
        TransferRequestLineInputV1[] lines, Guid id, string version, TransferRequestLineV1[] previous, CancellationToken ct)
    {
        var ids = lines.SelectMany(x => new[] { x.SourceLocationId, x.DestinationLocationId }).Distinct().ToArray();
        var locations = await a.TransferLocations().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        if (locations.Count != ids.Length) throw Error("TRANSFER_LOCATION_FORBIDDEN", 403);
        if (locations.Values.Any(x => x.IsBlocked)) throw Error("TRANSFER_LOCATION_BLOCKED", 422);
        var warehouseIds = lines.SelectMany(x => new[] { x.SourceWarehouseId, x.DestinationWarehouseId }).Distinct().ToArray();
        var warehouses = await db.Warehouses.AsNoTracking().Where(x => !x.IsDeleted && warehouseIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        foreach (var l in lines)
            if (!warehouses.TryGetValue(l.SourceWarehouseId, out var from) || !warehouses.TryGetValue(l.DestinationWarehouseId, out var to)
                || locations[l.SourceLocationId].WarehouseCd != from.WarehouseCd || locations[l.DestinationLocationId].WarehouseCd != to.WarehouseCd)
                throw Error("TRANSFER_LOCATION_WAREHOUSE_MISMATCH", 422);
        var s = EnvironmentStore(); var sites = await Sites(a, s, warehouseIds, ct);
        var unitAccess = await a.SourceAsync(OperationsOwnerV1.UomProfile, ct); var units = new UomProfileStoreV1(db); await units.Fence(false, ct);
        var productCache = new Dictionary<InventoryIdentityRefV1, TransferProductSourceV1>();
        var unitCache = new Dictionary<UomProfileRefV1, (UomProfileVersionV1 Input, UomProfileVersionV1 Base)>(); var result = new List<ResolvedLine>();
        foreach (var l in lines)
        {
            if (!productCache.TryGetValue(l.ProductCatalogueRef, out var product))
                productCache.Add(l.ProductCatalogueRef, product = await products.ReadWithinOwner(a, s, registration, l.ProductCatalogueRef, ct));
            if (!unitCache.TryGetValue(l.InputUomRef, out var unit))
            {
                var inputUnit = await UomProfileOwnerV1.CurrentExact(unitAccess, units, l.InputUomRef, ct);
                var baseRef = await UomProfileOwnerV1.CurrentBase(unitAccess, units, inputUnit, ct);
                unit = (inputUnit, baseRef == inputUnit.Reference ? inputUnit : await units.Read(baseRef.Id, null, ct));
                unitCache.Add(l.InputUomRef, unit);
                await s.RequireSource(new(inputUnit.Reference.Owner, inputUnit.Reference.Id, inputUnit.Reference.Version, inputUnit.Reference.Digest), ct);
            }
            var baseId = Guid.Parse(product.Product.BaseUomRef.Id);
            if (unit.Base.Reference.Id != baseId || unit.Base.Reference.Version != product.Product.BaseUomRef.Version) throw Error("TRANSFER_UOM_PRODUCT_MISMATCH", 422);
            var input = decimal.Parse(l.InputQuantity, CultureInfo.InvariantCulture).ToString("0.########", CultureInfo.InvariantCulture);
            var converted = UomProfileValuesV1.ConvertExact(input, unit.Input.Profile, unit.Base.Profile);
            var split = converted.Split('.'); var fraction = split.Length == 1 ? "" : split[1].TrimEnd('0');
            if (fraction.Length > 8) throw Error("TRANSFER_BASE_PRECISION_EXCEEDED", 422);
            var quantity = Amount(split[0] + "." + fraction.PadRight(8, '0'));
            var lineId = previous.SingleOrDefault(x => x.Input.ClientLineKey == l.ClientLineKey)?.Id ?? Guid.NewGuid();
            result.Add(new(lineId, l, product.Product, sites[l.SourceWarehouseId], sites[l.DestinationWarehouseId], quantity,
                $"MAIN_TRANSFER_REQUEST/{id:D}/{version}/{lineId:D}"));
        }
        return result.ToArray();
    }
    public Task<TransferRequestContextV1> ContextAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.TransferRequest, "query", false, async (a, t) =>
        {
            var deployment = await Fence(a, false, t); var s = EnvironmentStore(); await s.Acquire(a, t);
            var departments = await a.TransferDepartments(t); var mappings = options.Value.WarehouseSites.Select(x => x.WarehouseId).Distinct().ToArray();
            var warehouses = await db.Warehouses.AsNoTracking().Where(x => !x.IsDeleted && mappings.Contains(x.Id)).ToArrayAsync(t);
            var sites = await Sites(a, s, warehouses.Select(x => x.Id), t);
            var rows = await a.TransferLocations().Where(x => !x.IsBlocked).OrderBy(x => x.WarehouseCd).ThenBy(x => x.LocationCd).ToArrayAsync(t);
            var locations = rows.Where(x => warehouses.Any(w => w.WarehouseCd == x.WarehouseCd)).Select(x =>
            {
                var w = warehouses.Single(w => w.WarehouseCd == x.WarehouseCd);
                return new TransferRequestLocationV1(w.Id, w.WarehouseCd, x.Id, x.LocationCd, w.WarehouseCd + " / " + x.LocationCd, sites[w.Id]);
            }).ToArray();
            var identityAccess = await a.SourceAsync(OperationsOwnerV1.InventoryIdentity, t); var identities = new InventoryIdentityStoreV1(db, deployment.EnvironmentId);
            await identities.Fence(false, t); var choices = new List<TransferRequestProductV1>();
            foreach (var head in await identityAccess.InventoryIdentities(deployment.EnvironmentId).Where(x => x.Kind == "PRODUCT").OrderBy(x => x.Code).ToArrayAsync(t))
            {
                var v = await identities.Read(head.Id, null, t);
                if (v.Original.State == "APPROVED") choices.Add(new(v.Reference, v.Original.Definition.Name));
            }
            var unitAccess = await a.SourceAsync(OperationsOwnerV1.UomProfile, t); var unitStore = new UomProfileStoreV1(db); await unitStore.Fence(false, t);
            var unitChoices = new List<UomProfileVersionV1>();
            foreach (var head in await unitAccess.UomProfiles().OrderBy(x => x.Code).ToArrayAsync(t))
            {
                var unit = await unitStore.Read(head.Id, null, t); if (unit.Profile.Enabled == true) unitChoices.Add(unit);
            }
            return new TransferRequestContextV1(db.CurrentTenantId, a.ActorId, deployment, departments.AllowsUnbound, departments.Departments,
                locations, choices.ToArray(), unitChoices.ToArray());
        }, ct);
}
