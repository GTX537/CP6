using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed class OutboundRoutingOwnerV1(CP6Context db,OperationsOwnerV1 owner,bool legacyResolverEnabled=false)
{
    internal bool Owns(CP6Context context)=>ReferenceEquals(context,db)&&owner.Owns(db);
    private OperationsOwnerV1 Owner=>Owns(db)?owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<RoutingPageV1> SearchAsync(RoutingQueryV1 input,CancellationToken ct=default)
    {
        var q=OperationsOwnerV1.Freeze(input);
        if(q.Page<1||q.PageSize is <1 or >200||(long)(q.Page-1)*q.PageSize>int.MaxValue||q.TargetWarehouseId==Guid.Empty)
            throw new OrderPriceCorrectionException("ROUTING_QUERY_INVALID",400);
        return Owner.ExecuteAsync(OperationsOwnerV1.Routing,"query",false,async(a,t)=>
        {
            var rows=await a.RoutingRules(t);
            if(q.TargetWarehouseId.HasValue)
            {
                var source=await a.SourceAsync(OperationsOwnerV1.Warehouse,t);
                var codes=source.MasterWarehouses().Where(w=>w.Id==q.TargetWarehouseId).Select(w=>w.WarehouseCd);
                rows=rows.Where(x=>codes.Contains(x.TargetWarehouseCd));
            }
            if(q.Enabled.HasValue)rows=rows.Where(x=>x.Enabled==q.Enabled.Value);
            var count=await rows.CountAsync(t);var page=await rows.OrderBy(x=>x.SortOrder).ThenBy(x=>x.Id).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToArrayAsync(t);
            var result=new List<RoutingObservationV1>();foreach(var row in page)result.Add(await a.ProjectRoutingRule(row,t));
            return new RoutingPageV1(result,count,q.Page,q.PageSize,legacyResolverEnabled);
        },ct);
    }
    public Task<RoutingObservationV1> GetAsync(Guid id,CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Routing,"query",false,async(a,t)=>await a.ProjectRoutingRule(await a.RequireRoutingRule(id,t),t),ct);
    internal Task RejectLegacyAsync(string action,Guid? id=null,CancellationToken ct=default)
        =>Owner.ExecuteAsync<object>(OperationsOwnerV1.Routing,action,false,async(a,t)=>
        {
            if(id.HasValue)await a.RequireRoutingRule(id.Value,t);
            throw new OrderPriceCorrectionException(action=="query"?"OWNER_READ_VERSION_REQUIRED":action=="preview"?"ROUTING_SOURCE_POLICY_ADOPTION_UNPROVEN":"ROUTING_POLICY_WRITE_ADOPTION_UNPROVEN",action=="query"?409:503);
        },ct);
}
