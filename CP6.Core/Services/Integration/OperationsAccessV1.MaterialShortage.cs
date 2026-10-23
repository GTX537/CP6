using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal bool ShortageRemarkReadable => authority.Fields.Fields["Remark"].CanRead;
    internal bool ShortageRemarkWritable => authority.Fields.Fields["Remark"].CanWrite;

    internal async Task<IQueryable<MaterialShortage>> MaterialShortages(CancellationToken ct)
    {
        var work = WorkOrders();
        var sourceWork = (await SourceAsync(OperationsOwnerV1.Mes, ct)).WorkOrders();
        var rows = db.MaterialShortages.AsNoTracking().Where(s => !s.IsDeleted
            && work.Any(w => w.WorkOrderNo == s.WorkOrderNo)
            && sourceWork.Any(w => w.WorkOrderNo == s.WorkOrderNo));
        if (authority.Scope == 1) rows = rows.Where(s => s.Creator == Actor);
        OperationsAccessV1? outboundSource;
        try { outboundSource = await SourceAsync(OperationsOwnerV1.Outbound, ct); }
        catch (OrderPriceCorrectionException e) when (e.Message == "ACTION_FORBIDDEN") { outboundSource = null; }
        if (outboundSource == null) return rows.Where(s => s.RelatedOutboundNo == null || s.RelatedOutboundNo == "");
        var outbounds = Outbounds(); var sourceOutbounds = outboundSource.Outbounds();
        return rows.Where(s => s.RelatedOutboundNo == null || s.RelatedOutboundNo == ""
            || outbounds.Any(o => o.OutboundNo == s.RelatedOutboundNo && o.WorkOrderNo == s.WorkOrderNo && o.OutboundType == OutboundType.Material)
                && sourceOutbounds.Any(o => o.OutboundNo == s.RelatedOutboundNo)
                && db.OutboundOrderDetails.Any(d => !d.IsDeleted && d.OutboundNo == s.RelatedOutboundNo && d.ProductCd == s.ProductCd));
    }

    internal async Task RequireMaterialShortage(Guid id, CancellationToken ct)
    {
        if (!await (await MaterialShortages(ct)).AnyAsync(s => s.Id == id, ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }

    internal async Task RequireShortageCreation(CP6.Core.EFDbContext.CP6Context context, MaterialShortage row, CancellationToken ct)
    {
        RequireQualityParent(context, OperationsOwnerV1.Outbound, "allocate");
        if (string.IsNullOrWhiteSpace(row.WorkOrderNo) || string.IsNullOrWhiteSpace(row.RelatedOutboundNo)
            || !await Outbounds().AnyAsync(o => o.OutboundNo == row.RelatedOutboundNo && o.WorkOrderNo == row.WorkOrderNo
                && o.OutboundType == OutboundType.Material, ct)
            || !await db.OutboundOrderDetails.AnyAsync(d => !d.IsDeleted && d.OutboundNo == row.RelatedOutboundNo && d.ProductCd == row.ProductCd, ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
}
