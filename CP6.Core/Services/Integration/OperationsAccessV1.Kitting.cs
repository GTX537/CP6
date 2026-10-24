using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal IQueryable<KitMaster> KitMasters()
    {
        var rows = db.KitMasters.AsNoTracking().Where(x => !x.IsDeleted);
        if (authority.Scope == 1) rows = rows.Where(x => x.Creator == Actor);
        else if (authority.Scope != 5)
        {
            var ids = StandaloneDepartmentScopeV1.Objects(db, authority, "kit-master");
            rows = rows.Where(x => ids.Contains(x.Id));
        }
        rows = rows.Where(x => x.DefaultWarehouseCd == null || x.DefaultWarehouseCd == ""
            || db.Warehouses.Any(w => !w.IsDeleted && w.WarehouseCd == x.DefaultWarehouseCd));
        if (!authority.Actor.RoleIds.Contains(1))
        {
            var grants = db.WmsRoleScopes.Where(g => authority.Actor.RoleIds.Contains(g.RoleId));
            // A recipe has no source location. Partial area grants cannot stand in for the whole
            // default warehouse; a recipe without one requires an explicit all-warehouse grant.
            rows = rows.Where(x => grants.Any(g => (g.WarehouseCd == "*" || x.DefaultWarehouseCd != null && x.DefaultWarehouseCd != "" && g.WarehouseCd == x.DefaultWarehouseCd)
                && (g.AreaCd == null || g.AreaCd == "" || g.AreaCd == "*")));
        }
        return rows;
    }
    internal IQueryable<KitOrder> KitOrders()
    {
        var rows = db.KitOrders.AsNoTracking().Where(x => !x.IsDeleted);
        if (authority.Scope == 1) rows = rows.Where(x => x.Creator == Actor);
        else if (authority.Scope != 5)
        {
            var ids = StandaloneDepartmentScopeV1.Objects(db, authority, "kit-order");
            rows = rows.Where(x => ids.Contains(x.Id));
        }
        var locations = db.Locations.Where(l => !l.IsDeleted
            && db.Warehouses.Any(w => !w.IsDeleted && w.WarehouseCd == l.WarehouseCd));
        if (!authority.Actor.RoleIds.Contains(1))
        {
            var grants = db.WmsRoleScopes.Where(g => authority.Actor.RoleIds.Contains(g.RoleId));
            locations = locations.Where(l => grants.Any(g => (g.WarehouseCd == "*" || g.WarehouseCd == l.WarehouseCd)
                && (g.AreaCd == null || g.AreaCd == "" || g.AreaCd == "*" || g.AreaCd == l.AreaCd)));
        }
        return rows.Where(x => locations.Any(l => l.WarehouseCd == x.WarehouseCd && l.LocationCd == x.KitLocationCd));
    }
    private bool KitRead(string field) => authority.Fields.Fields[field].CanRead;
    internal KitMasterObservationV1 ProjectKitMaster(KitMaster x) => new(x.Id, x.KitSku,
        KitRead("KitName") ? x.KitName : null, x.DefaultWarehouseCd, x.ActiveFlg,
        KitRead("Remarks") ? x.Remarks : null, KitRead("KitName"), KitRead("Remarks"));
    internal KitComponentObservationV1 ProjectKitComponent(KitMasterComponent x) => new(x.Id, x.LineNo, x.ComponentProductCd,
        KitRead("ComponentName") ? x.ComponentName : null, InventoryReportValuesV1.Exact(x.RequiredQty).ToString(),
        x.UnitCd, KitRead("Remarks") ? x.Remarks : null, KitRead("ComponentName"), KitRead("Remarks"));
    internal async Task<KitOrderObservationV1> ProjectKitOrder(KitOrder x, CancellationToken ct)
    {
        var readable = false; var state = "NOT_DISCLOSED";
        if (KitRead("ExecutedTxnNos"))
        {
            if (x.ExecutedTxnNos == null || x.ExecutedTxnNos == "") state = "NOT_RECORDED";
            else
            {
                OperationsAccessV1? source = null;
                try { source = await SourceAsync(OperationsOwnerV1.Stock, ct); }
                catch (OrderPriceCorrectionException e) when (e.Message == "ACTION_FORBIDDEN") { }
                var names = x.ExecutedTxnNos.Split(';');
                if (source != null && names.All(n => n.Length is > 0 and <= 30)
                    && names.Distinct(StringComparer.Ordinal).Count() == names.Length
                    && x.Direction is "ASSEMBLE" or "DISASSEMBLE")
                {
                    var kind = x.Direction == "ASSEMBLE" ? "KIT_ASSEMBLE" : "KIT_DISASSEMBLE";
                    // Authorize every actual referenced transaction, including its original source
                    // location. Neither a current recipe nor a filtered empty set proves history.
                    var count = await source.InventoryTransactions().Where(t => names.Contains(t.TxnNo)
                        && t.RelatedNo == x.KitOrderNo && t.RelatedType == kind
                        && db.Locations.Any(l => !l.IsDeleted && l.WarehouseCd == t.WarehouseCd && l.LocationCd == t.LocationCd)
                        && db.Warehouses.Any(w => !w.IsDeleted && w.WarehouseCd == t.WarehouseCd))
                        .Select(t => t.TxnNo).Distinct().CountAsync(ct);
                    readable = count == names.Length;
                }
                state = readable ? "AUTHORIZED_ORIGINAL_REFERENCES" : "SOURCE_DISCLOSURE_UNAVAILABLE";
            }
        }
        return new(x.Id, x.KitOrderNo, x.KitSku, KitRead("KitName") ? x.KitName : null,
            InventoryReportValuesV1.Exact(x.Qty).ToString(), x.Direction, x.WarehouseCd, x.KitLocationCd, x.KitLotNo,
            x.Status, x.ExecutedAt, x.CreateDate, KitRead("OperatorCd") ? x.OperatorCd : null,
            KitRead("Remarks") ? x.Remarks : null, readable ? x.ExecutedTxnNos : null,
            KitRead("KitName"), KitRead("OperatorCd"), KitRead("Remarks"), readable, state);
    }
}
