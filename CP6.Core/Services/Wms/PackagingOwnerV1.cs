using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>Current observations of original legacy packages, not formal OUT manifests or delivery proof.</summary>
public sealed class PackagingOwnerV1(CP6Context db,OperationsOwnerV1 owner)
{
    private OperationsOwnerV1 Owner=>owner.Owns(db)?owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<PackagingPageV1> SearchAsync(PackagingQueryV1 input,CancellationToken ct=default)
    {
        var q=OperationsOwnerV1.Freeze(input);
        if(q.Page<1||q.PageSize is <1 or >200||(long)(q.Page-1)*q.PageSize>int.MaxValue
            ||q.PackageNo?.Length>20||q.OutboundNo?.Length>20||q.CarrierCd?.Length>20||q.TrackingNo?.Length>50
            ||q.From.HasValue&&q.From.Value.Kind!=DateTimeKind.Unspecified||q.To.HasValue&&q.To.Value.Kind!=DateTimeKind.Unspecified
            ||q.From>q.To)throw new OrderPriceCorrectionException("PACKAGING_QUERY_INVALID",400);
        return Owner.ExecuteAsync(OperationsOwnerV1.Packaging,"query",false,async(a,t)=>
        {
            var rows=await a.ShippingPackages(t);a.RequirePackagingFilterFields(q);
            if(q.PackageNo!=null)rows=rows.Where(x=>x.PackageNo.Contains(q.PackageNo));
            if(q.OutboundNo!=null)rows=rows.Where(x=>x.OutboundNo==q.OutboundNo);
            if(q.CarrierCd!=null)rows=rows.Where(x=>x.CarrierCd==q.CarrierCd);
            if(q.TrackingNo!=null)rows=rows.Where(x=>x.TrackingNo==q.TrackingNo);
            if(q.From.HasValue)rows=rows.Where(x=>x.DepartureTime>=q.From);
            if(q.To.HasValue)rows=rows.Where(x=>x.DepartureTime<=q.To);
            var total=await rows.CountAsync(t);
            var page=await rows.OrderByDescending(x=>x.DepartureTime).ThenBy(x=>x.Id).Skip((q.Page-1)*q.PageSize).Take(q.PageSize)
                .Select(x=>new{Package=x,OutboundId=db.OutboundOrders.Where(p=>p.OutboundNo==x.OutboundNo&&!p.IsDeleted).Select(p=>p.Id).Single()}).ToArrayAsync(t);
            return new PackagingPageV1(page.Select(x=>a.ProjectPackage(x.Package,x.OutboundId)).ToArray(),total,q.Page,q.PageSize);
        },ct);
    }
    public Task<PackagingRecordV1> GetAsync(string number,CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Packaging,"query",false,async(a,t)=>
        {
            var row=await (await a.ShippingPackages(t)).SingleOrDefaultAsync(x=>x.PackageNo==number,t)
                ??throw new OrderPriceCorrectionException("PACKAGING_NOT_FOUND",404);
            var id=await db.OutboundOrders.Where(p=>p.OutboundNo==row.OutboundNo&&!p.IsDeleted).Select(p=>p.Id).SingleAsync(t);
            return a.ProjectPackage(row,id);
        },ct);
    public Task RejectLegacyAsync(string? number=null,CancellationToken ct=default)
        =>Owner.ExecuteAsync<object>(OperationsOwnerV1.Packaging,"query",false,async(a,t)=>
        {
            var rows=await a.ShippingPackages(t);
            if(number!=null&&!await rows.AnyAsync(x=>x.PackageNo==number,t))throw new OrderPriceCorrectionException("PACKAGING_NOT_FOUND",404);
            throw new OrderPriceCorrectionException("OWNER_READ_VERSION_REQUIRED",409);
        },ct);
}
