using System.Globalization;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal async Task<IQueryable<ShippingPackage>> ShippingPackages(CancellationToken ct)
    {
        var source=await SourceAsync(OperationsOwnerV1.Outbound,ct);
        // Both independent scopes admit the entire original header, all lines, and its real source.
        // Department scope follows stored order relationships; no guessed package mapping.
        var ownParents=Outbounds();var originalParents=source.Outbounds();
        var rows=db.ShippingPackages.AsNoTracking().Where(x=>!x.IsDeleted
            && ownParents.Any(p=>p.OutboundNo==x.OutboundNo)&&originalParents.Any(p=>p.OutboundNo==x.OutboundNo));
        if(authority.Scope==1)rows=rows.Where(x=>x.Creator==Actor);
        return rows;
    }
    internal void RequirePackagingFilterFields(PackagingQueryV1 q)
    {
        if(q.CarrierCd!=null&&!authority.Fields.Fields["CarrierCd"].CanRead
            ||q.TrackingNo!=null&&!authority.Fields.Fields["TrackingNo"].CanRead)
            throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN",403);
    }
    internal PackagingRecordV1 ProjectPackage(ShippingPackage x,Guid outboundId)
    {
        bool Read(string key)=>authority.Fields.Fields[key].CanRead;
        string? Decimal(decimal? value)=>value?.ToString("0.############################",CultureInfo.InvariantCulture);
        return new(x.Id,x.PackageNo,outboundId,x.OutboundNo,x.CaseQty,Decimal(x.TotalWeightKg),Decimal(x.TotalVolumeM3),
            Read("CarrierCd")?x.CarrierCd:null,Read("TrackingNo")?x.TrackingNo:null,x.DepartureTime,Read("Remarks")?x.Remarks:null,
            Read("CarrierCd"),Read("TrackingNo"),Read("Remarks"));
    }
}
