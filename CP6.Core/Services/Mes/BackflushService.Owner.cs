using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public partial class BackflushService
{
    internal bool Owns(CP6Context db)=>ReferenceEquals(_db,db) && _stock is StockMovementService stock && stock.Owns(db);
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true && Owns(_db)?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task BackflushAsync(string no,string? user)=>!_db.Database.IsNpgsql()?BackflushCoreAsync(no,user):BackflushOwned(no,null);
    internal Task BackflushCompletedAsync(OperationsDelegationV1 ticket)=>BackflushOwned(ticket.Number,ticket);
    private async Task BackflushOwned(string no,OperationsDelegationV1? ticket)
    {
        // Each material retains the accepted compensation unit. Its stock and usage now commit together.
        var materials=await Owner.ExecuteAsync(OperationsOwnerV1.Production,"backflush",true,async(a,ct)=>
        {
            await RequireCompleted(a,no,ct);
            return await _db.Set<ProductMaterial>().Where(p=>!p.IsDeleted && _db.WorkOrders.Any(w=>w.WorkOrderNo==no && w.ProductCd==p.ProductCd))
                .Select(p=>p.MaterialCd).Distinct().ToArrayAsync(ct);
        },delegation:ticket);
        var failures=new List<Exception>();
        foreach(var material in materials)
        {
            var transactions=new List<StockTransaction>();var shortages=new List<decimal>();
            try
            {
                await Owner.ExecuteAsync(OperationsOwnerV1.Production,"backflush",true,async(a,ct)=>
                {
                    await RequireCompleted(a,no,ct);
                    var wo=await _db.WorkOrders.AsNoTracking().SingleAsync(w=>w.WorkOrderNo==no,ct);
                    if(await _db.StockTransactions.AnyAsync(t=>t.RelatedType==BackflushRelatedType && t.RelatedNo==no && t.ProductCd==material,ct))return true;
                    var bom=await _db.Set<ProductMaterial>().AsNoTracking().Where(p=>!p.IsDeleted && p.ProductCd==wo.ProductCd && p.MaterialCd==material).OrderBy(p=>p.SortOrder).ToArrayAsync(ct);
                    var spec=await _db.Set<ProductMaster>().AsNoTracking().SingleOrDefaultAsync(p=>p.ProductCd==wo.ProductCd && !p.IsDeleted,ct);
                    var specByCd=new Dictionary<string,ProductMaster>();var yieldByFlute=new Dictionary<string,decimal>();
                    if(spec!=null)
                    {
                        // Resolve equivalence in PostgreSQL, then use the caller's exact key for the pure calculator.
                        specByCd[wo.ProductCd]=spec;
                        if(spec.SheetFlute!=null)
                        {
                            var yield=await _db.Set<CP6.Entity.DomainModels.Common.MasterGenericCode>().AsNoTracking()
                                .Where(c=>c.GroupCode=="M067" && c.Code==spec.SheetFlute).Select(c=>c.Num1).FirstOrDefaultAsync(ct);
                            yieldByFlute[spec.SheetFlute]=yield??1m;
                        }
                    }
                    using var inheritance=((StockMovementService)_stock).InheritTransactions(a);
                    foreach(var row in bom)
                    {
                        var qty=BomUsageResolver.ComputeUsage(_usage,wo.ProductCd,row,wo.CompletedQty,specByCd,yieldByFlute);if(qty<=0)continue;
                        var available=_db.Stocks.Where(s=>!s.IsDeleted && s.ProductCd==row.MaterialCd && s.OwnerType==StockOwnerType.Self);
                        if(ticket==null)available=a.Stocks(available);
                        var src=await available.OrderByDescending(s=>s.PhysicalQty).FirstOrDefaultAsync(ct);
                        var wh=src?.WarehouseCd??DefaultMaterialWarehouse;var loc=src?.LocationCd??DefaultMaterialLocation;var onHand=src?.PhysicalQty??0;
                        if(ticket==null)await a.RequireLocation(wh,loc,ct);
                        await ((StockMovementService)_stock).ApplyWithinOwnerAsync(new(){TxnType=WmsTxnType.OUT,WarehouseCd=wh,LocationCd=loc,ProductCd=row.MaterialCd,LotNo=src?.LotNo??"",
                            Qty=qty,UnitCd=row.UsageUnit,UnitPrice=src?.UnitPrice??row.SupplyPrice,RelatedNo=no,RelatedType=BackflushRelatedType,
                            OperatorCd=a.Actor,Remark=$"完工反冲 {no} 工程 {row.ProcessCd} 材料 {row.MaterialCd}",AllowNegativeOverride=true},ct);
                        var mat=await _db.WorkOrderMaterials.SingleOrDefaultAsync(m=>m.WorkOrderNo==no && m.ProcessCd==row.ProcessCd && m.MaterialCd==row.MaterialCd && !m.IsDeleted,ct);
                        if(mat==null)
                        {
                            mat=new(){WorkOrderNo=wo.WorkOrderNo,ProcessCd=row.ProcessCd,MaterialCd=row.MaterialCd,MaterialTypeDiv=row.MaterialTypeDiv,Unit=row.UsageUnit,Creator=a.Actor,CreateDate=DateTime.Now};
                            _db.WorkOrderMaterials.Add(mat);
                        }
                        mat.ActualQty+=qty;mat.Modifier=a.Actor;mat.ModifyDate=DateTime.Now;
                        await _db.SaveChangesAsync(ct);
                        if(onHand<qty)shortages.Add(qty-onHand);
                    }
                    transactions.AddRange(_db.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity));
                    a.AtCommit(t=>a.RequireProductionWorkOrder(no,t));return true;
                },delegation:ticket);
                foreach(var transaction in transactions)await ((StockMovementService)_stock).PublishCommittedCancellationAsync(transaction);
                foreach(var shortfall in shortages)try{if(_shortageNotifier!=null)await _shortageNotifier.NotifyAsync(no,material,shortfall);}catch{}
            }
            catch(OrderPriceCorrectionException){throw;} // Denied authority or UNKNOWN must never be replayed/continued.
            catch(Exception error){failures.Add(error);}
        }
        if(failures.Count!=0)throw new InvalidOperationException("BACKFLUSH_PARTIAL_FAILURE",new AggregateException(failures));
    }
    private async Task RequireCompleted(OperationsAccessV1 access,string no,CancellationToken ct)
    {
        await access.RequireProductionWorkOrder(no,ct);
        var wo=await _db.WorkOrders.AsNoTracking().SingleAsync(w=>w.WorkOrderNo==no,ct);
        if(wo.Status is not (WorkOrderStatus.Completed or WorkOrderStatus.Inspected) || wo.CompletedQty<=0)throw new OrderPriceCorrectionException("PRODUCTION_STATE_INVALID",409);
        await access.RequireActiveAssociations(wo.WebOrderNo,null,ct);
    }
}
