using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    private readonly IDbContextTransaction? routingTransaction=db.Database.CurrentTransaction;
    private readonly Guid routingTenant=db.CurrentTenantId;
    internal async Task<IQueryable<OutboundRoutingRule>> RoutingRules(CancellationToken ct)
    {
        var source=await SourceAsync(OperationsOwnerV1.Warehouse,ct);var warehouses=source.MasterWarehouses();
        var rows=db.OutboundRoutingRules.AsNoTracking().Where(x=>!x.IsDeleted&&warehouses.Any(w=>w.WarehouseCd==x.TargetWarehouseCd));
        if(authority.Scope==1)rows=rows.Where(x=>x.Creator==Actor);
        else if(authority.Scope!=5)
        {var ids=StandaloneDepartmentScopeV1.Objects(db,authority,"routing-rule");rows=rows.Where(x=>ids.Contains(x.Id));}
        return rows;
    }
    internal async Task<OutboundRoutingRule> RequireRoutingRule(Guid id,CancellationToken ct)
        =>await (await RoutingRules(ct)).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new OrderPriceCorrectionException("ROUTING_RULE_NOT_FOUND",404);
    internal async Task<RoutingObservationV1> ProjectRoutingRule(OutboundRoutingRule x,CancellationToken ct)
    {
        var source=await SourceAsync(OperationsOwnerV1.Warehouse,ct);
        var warehouses=await source.MasterWarehouses().Where(w=>w.WarehouseCd==x.TargetWarehouseCd).Select(w=>w.Id).Take(2).ToArrayAsync(ct);
        if(warehouses.Length!=1)throw new OrderPriceCorrectionException("ROUTING_SOURCE_UNAVAILABLE",503);
        bool Read(string key)=>authority.Fields.Fields[key].CanRead;
        var customer=Read("CustomerCd");var product=Read("ProductCdPrefix");
        return new(x.Id,Read("RuleName")?x.RuleName:null,x.SortOrder,customer?x.CustomerCd:null,
            !customer?"HIDDEN":x.CustomerCd==null?"LEGACY_ANY":"LEGACY_EXACT",product?x.ProductCdPrefix:null,
            !product?"HIDDEN":string.IsNullOrEmpty(x.ProductCdPrefix)?"LEGACY_ANY":"LEGACY_PREFIX",x.OutboundType,
            warehouses[0],x.TargetWarehouseCd,x.Enabled,Read("Remarks")?x.Remarks:null,MasterEtag(x),
            Read("RuleName"),customer,product,Read("Remarks"));
    }
    internal sealed record RoutingInput(string? CustomerCd,string ProductCd,int OutboundType,string WarehouseCd);
    internal async Task<RoutingInput> RequireRoutingParent(CP6Context context,Guid outboundId,Guid detailId,CancellationToken ct)
    {
        if(!ReferenceEquals(db,context)||!db.Database.IsNpgsql()||routingTransaction==null
            ||!ReferenceEquals(routingTransaction,db.Database.CurrentTransaction)||routingTransaction.GetDbTransaction().Connection==null
            ||routingTenant!=db.CurrentTenantId||authority.Resource!=OperationsOwnerV1.Outbound||authority.Action!="allocate")
            throw new OrderPriceCorrectionException("INTERNAL_ROUTING_CAPABILITY_REQUIRED",403);
        var header=await Outbounds().SingleOrDefaultAsync(x=>x.Id==outboundId&&x.Status==OutboundOrderStatus.Confirmed,ct)
            ??throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        var detail=await db.OutboundOrderDetails.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==detailId&&!x.IsDeleted&&x.OutboundNo==header.OutboundNo,ct)
            ??throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        // Only the stored parent and exact detail define legacy resolver input. No client fallback or customer override.
        return new(header.CustomerCd,detail.ProductCd,header.OutboundType,header.WarehouseCd);
    }
}
