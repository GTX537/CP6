using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    private async Task<OperationsAccessV1?> CrossDockSource(string resource, CancellationToken ct)
    {
        try { return await SourceAsync(resource, ct); }
        catch (OrderPriceCorrectionException error) when (error.Message == "ACTION_FORBIDDEN") { return null; }
    }
    internal async Task<IQueryable<CrossDockOrder>> CrossDocks(CancellationToken ct)
    {
        var rows = db.CrossDockOrders.AsNoTracking().Where(x => !x.IsDeleted);
        if (authority.Scope == 1) rows = rows.Where(x => x.Creator == Actor);
        else if (authority.Scope != 5)
        {
            var ids = StandaloneDepartmentScopeV1.Objects(db, authority, "cross-dock");
            rows = rows.Where(x => ids.Contains(x.Id));
        }
        var locations = db.Locations.AsNoTracking().Where(l => !l.IsDeleted && db.Warehouses.Any(w => !w.IsDeleted && w.WarehouseCd == l.WarehouseCd));
        if (!authority.Actor.RoleIds.Contains(1))
        {
            var grants = db.WmsRoleScopes.Where(g => authority.Actor.RoleIds.Contains(g.RoleId));
            locations = locations.Where(l => grants.Any(g => (g.WarehouseCd == "*" || g.WarehouseCd == l.WarehouseCd)
                && (g.AreaCd == null || g.AreaCd == "" || g.AreaCd == "*" || g.AreaCd == l.AreaCd)));
        }
        rows = rows.Where(x => locations.Any(l => l.WarehouseCd == x.WarehouseCd && l.LocationCd == x.TempLocationCd));
        // Old free-text dock labels do not identify additional physical locations or confer authority.
        // Existing commercial references restrict visibility; matching a header is not source adoption.
        var input = await CrossDockSource(OperationsOwnerV1.Inbound, ct);
        var output = await CrossDockSource(OperationsOwnerV1.Outbound, ct);
        var ownInputs = Inbounds(); var allowedInputs = input?.Inbounds() ?? ownInputs.Where(_ => false);
        var ownOutputs = Outbounds(); var allowedOutputs = output?.Outbounds() ?? ownOutputs.Where(_ => false);
        return rows.Where(x => (x.InboundNo == null || x.InboundNo == "" || ownInputs.Any(i => i.InboundNo == x.InboundNo
                && i.WarehouseCd == x.WarehouseCd && allowedInputs.Any(y => y.Id == i.Id)
                && db.InboundOrderDetails.Any(d => !d.IsDeleted && d.InboundNo == i.InboundNo && d.ProductCd == x.ProductCd)))
            && (x.OutboundNo == null || x.OutboundNo == "" || ownOutputs.Any(o => o.OutboundNo == x.OutboundNo
                && o.WarehouseCd == x.WarehouseCd && allowedOutputs.Any(y => y.Id == o.Id)
                && db.OutboundOrderDetails.Any(d => !d.IsDeleted && d.OutboundNo == o.OutboundNo && d.ProductCd == x.ProductCd
                    && (d.WarehouseCd ?? o.WarehouseCd) == x.WarehouseCd))));
    }
    internal CrossDockObservationV1 ProjectCrossDock(CrossDockOrder x)
    {
        bool Read(string field) => authority.Fields.Fields[field].CanRead;
        return new() { Id = x.Id, XDockNo = x.XDockNo, ProductCd = x.ProductCd, ProductName = x.ProductName,
            Qty = InventoryReportValuesV1.Exact(x.Qty).ToString(), WarehouseCd = x.WarehouseCd, TempLocationCd = x.TempLocationCd,
            LotNo = x.LotNo, LegacyStatus = x.Status, InboundNo = x.InboundNo, OutboundNo = x.OutboundNo,
            InTxnNo = x.InTxnNo, OutTxnNo = x.OutTxnNo, FromDock = x.FromDock, ToDock = x.ToDock, ExecutedAt = x.ExecutedAt, CreateDate = x.CreateDate,
            SupplierCdReadable = Read("SupplierCd"), CustomerCdReadable = Read("CustomerCd"), OperatorCdReadable = Read("OperatorCd"), RemarksReadable = Read("Remarks"),
            SupplierCd = Read("SupplierCd") ? x.SupplierCd : null, CustomerCd = Read("CustomerCd") ? x.CustomerCd : null,
            OperatorCd = Read("OperatorCd") ? x.OperatorCd : null, Remarks = Read("Remarks") ? x.Remarks : null };
    }
}
