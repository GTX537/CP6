using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed class SampleOwnerV1(CP6Context db,OperationsOwnerV1 owner)
{
    internal bool Owns(CP6Context context)=>ReferenceEquals(db,context)&&owner.Owns(db);
    private OperationsOwnerV1 Owner=>Owns(db)?owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<SamplePageV1> SearchAsync(SampleQueryV1 input,CancellationToken ct=default)
    {
        var q=OperationsOwnerV1.Freeze(input);
        if(q.Page<1||q.PageSize is <1 or >100||(long)(q.Page-1)*q.PageSize>int.MaxValue
            ||q.SampleNo?.Length>100||q.SampleType?.Length>100||q.CustomerCd?.Length>100||q.ProductCd?.Length>100)
            throw new OrderPriceCorrectionException("SAMPLE_QUERY_INVALID",400);
        return Owner.ExecuteAsync(OperationsOwnerV1.Sample,"query",false,async(a,t)=>
        {
            var rows=a.SampleRecords();
            if(q.SampleNo!=null)rows=rows.Where(x=>x.SampleNo.Contains(q.SampleNo));
            if(q.SampleType!=null)rows=rows.Where(x=>x.SampleType==q.SampleType);
            if(q.CustomerCd!=null){a.RequireSampleFieldRead("CustomerCd");rows=rows.Where(x=>x.CustomerCd==q.CustomerCd);}
            if(q.ProductCd!=null){a.RequireSampleFieldRead("ProductCd");rows=rows.Where(x=>x.ProductCd==q.ProductCd);}
            if(q.LegacyStatus.HasValue)rows=rows.Where(x=>x.Status==q.LegacyStatus.Value);
            if(q.AwaitingReturnEvidence==true)rows=rows.Where(x=>x.ReturnedAt==null&&(x.LentAt!=null||x.Status==1));
            var total=await rows.CountAsync(t);
            var page=await rows.OrderByDescending(x=>x.RegisteredAt).ThenBy(x=>x.Id).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToArrayAsync(t);
            return new SamplePageV1(page.Select(a.ProjectSample).ToArray(),total,q.Page,q.PageSize);
        },ct);
    }
    public Task<SampleObservationV1> GetAsync(Guid id,CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Sample,"query",false,async(a,t)=>a.ProjectSample(await a.RequireSample(id,t)),ct);
    internal Task RejectLegacyAsync(string action,string? number=null,CancellationToken ct=default)
        =>Owner.ExecuteAsync<object>(OperationsOwnerV1.Sample,action,false,async(a,t)=>
        {
            if(number!=null&&!await a.SampleRecords().AnyAsync(x=>x.SampleNo==number,t))throw new OrderPriceCorrectionException("SAMPLE_NOT_FOUND",404);
            throw new OrderPriceCorrectionException(action=="query"?"OWNER_READ_VERSION_REQUIRED":"SAMPLE_WRITE_ADOPTION_UNPROVEN",action=="query"?409:503);
        },ct);
}
