using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal bool RmaApplicationSelfOnly => authority.Scope == 1;
    internal async Task RequireRmaApplicationSource(Guid outboundId, Guid applicant, CancellationToken ct)
    {
        if (RmaApplicationSelfOnly && applicant != ActorId || !await Outbounds().AnyAsync(x => x.Id == outboundId, ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
    internal void RequireRmaApplicationFields(bool remarks)
    {
        if (!authority.Fields.Fields["ReturnReason"].CanWrite || remarks && !authority.Fields.Fields["Remarks"].CanWrite)
            throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN", 403);
    }
    internal RmaApplicationViewV1 ProjectRmaApplication(RmaApplicationViewV1 value) => value with
    {
        RequestedBy = authority.Fields.Fields["OperatorCd"].CanRead ? value.RequestedBy : null,
        Reason = authority.Fields.Fields["ReturnReason"].CanRead ? value.Reason : null,
        Remarks = authority.Fields.Fields["Remarks"].CanRead ? value.Remarks : null
    };

    internal IQueryable<RmaHeader> Rmas()
    {
        var rows = db.RmaHeaders.AsNoTracking().Where(r => !r.IsDeleted);
        var source = db.OutboundOrders.AsNoTracking().Where(o => !o.IsDeleted && o.OutboundType == OutboundType.Shipping);
        // A present legacy source must remain a real, matching customer shipment in this tenant.
        rows = rows.Where(r => r.OriginalShippingNo == null || r.OriginalShippingNo == "" ||
            source.Any(o => o.OutboundNo == r.OriginalShippingNo && o.CustomerCd == r.CustomerCd));
        if (authority.Scope == 1)
            rows = rows.Where(r => r.Creator == Actor && !db.RmaDetails.Any(d => d.RmaNo == r.RmaNo &&
                (d.Creator == null || d.Creator != Actor)));
        else if (authority.Scope != 5)
        {
            var orders = DepartmentOrders(authority).Select(o => o.WebOrderNo);
            rows = rows.Where(r => r.OriginalShippingNo != null && r.OriginalShippingNo != "" &&
                source.Any(o => o.OutboundNo == r.OriginalShippingNo && o.CustomerCd == r.CustomerCd &&
                    o.WebOrderNo != null && o.WebOrderNo != "" && orders.Contains(o.WebOrderNo)));
        }
        if (authority.Actor.RoleIds.Contains(1)) return rows;
        var grants = db.WmsRoleScopes.Where(g => authority.Actor.RoleIds.Contains(g.RoleId));
        // The header has no qualified location. Area-only grants cannot manufacture one from its warehouse code.
        return rows.Where(r => grants.Any(g => (g.WarehouseCd == "*" || g.WarehouseCd == r.WarehouseCd) &&
            (g.AreaCd == null || g.AreaCd == "" || g.AreaCd == "*")) ||
            db.RmaDetails.Any(d => d.RmaNo == r.RmaNo && !d.IsDeleted) &&
            !db.RmaDetails.Any(d => d.RmaNo == r.RmaNo && !d.IsDeleted &&
                !grants.Any(g => (g.WarehouseCd == "*" || g.WarehouseCd == r.WarehouseCd) &&
                    db.Locations.Any(l => !l.IsDeleted && l.WarehouseCd == r.WarehouseCd &&
                        l.LocationCd == d.DestLocationCd && l.AreaCd == g.AreaCd))));
    }

    internal async Task RequireRma(string number, CancellationToken ct)
    {
        if (!await Rmas().AnyAsync(r => r.RmaNo == number, ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }

    internal void ProjectRma(RmaDto value)
    {
        bool Read(string field) => authority.Fields.Fields.TryGetValue(field, out var grant) && grant.CanRead;
        if (!Read("CustomerName")) value.CustomerName = null;
        if (!Read("OperatorCd")) value.OperatorCd = null;
        if (!Read("ReturnReason")) value.ReturnReason = null;
        if (!Read("Remarks"))
        {
            value.Remarks = null;
            foreach (var detail in value.Details) detail.Remarks = null;
        }
    }
}
