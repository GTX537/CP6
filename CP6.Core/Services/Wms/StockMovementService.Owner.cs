using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class StockMovementService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true && Owns(_db)?_owner:
        throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public async Task<string> ApplyAsync(StockMovementRequest req,CancellationToken ct=default)
    {
        if(!_db.Database.IsNpgsql())return await ApplyCoreAsync(req,ct);
        var frozen=OperationsOwnerV1.Freeze(req);var effects=new List<StockTransaction>();
        var number=await Owner.ExecuteAsync(OperationsOwnerV1.Stock,"adjust",true,async(a,t)=>
        {
            // Both switches belong to bounded parent operations, never a user-selected escape hatch.
            if(frozen.AllowNegativeOverride || string.Equals(frozen.RelatedType,"STOCKTAKE",StringComparison.OrdinalIgnoreCase))
                throw new OrderPriceCorrectionException("INTERNAL_STOCK_CAPABILITY_REQUIRED",403);
            a.RequirePriceWrite();frozen.OperatorCd=a.Actor;
            await a.RequireStockKey(frozen.WarehouseCd,frozen.LocationCd,frozen.ProductCd,frozen.LotNo,true,t);
            await GuardIndependentLocationUse(a,frozen.WarehouseCd,frozen.LocationCd,frozen.TxnType,t);
            using var inheritance=InheritTransactions(a);
            var no=await ApplyWithinOwnerAsync(frozen,t);
            effects.AddRange(_db.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity));
            a.AtCommit(c=>a.RequireStockKey(frozen.WarehouseCd,frozen.LocationCd,frozen.ProductCd,frozen.LotNo,false,c));return no;
        },ct);
        foreach(var effect in effects)await PublishCommittedCancellationAsync(effect);return number;
    }
    public async Task<(string OutTxnNo,string InTxnNo)> MoveAsync(StockMoveRequest req,CancellationToken ct=default)
    {
        if(!_db.Database.IsNpgsql())return await MoveCoreAsync(req,ct);
        var frozen=OperationsOwnerV1.Freeze(req);var effects=new List<StockTransaction>();
        var result=await Owner.ExecuteAsync(OperationsOwnerV1.Stock,"move",true,async(a,t)=>
        {
            frozen.OperatorCd=a.Actor;
            await a.RequireStockKey(frozen.WarehouseCd,frozen.FromLocationCd,frozen.ProductCd,frozen.LotNo,true,t);
            await a.RequireStockKey(frozen.WarehouseCd,frozen.ToLocationCd,frozen.ProductCd,frozen.LotNo,true,t);
            await GuardIndependentLocationUse(a,frozen.WarehouseCd,frozen.FromLocationCd,WmsTxnType.MOVE,t);
            await GuardIndependentLocationUse(a,frozen.WarehouseCd,frozen.ToLocationCd,WmsTxnType.MOVE,t);
            if(await _db.Stocks.AnyAsync(s=>s.WarehouseCd==frozen.WarehouseCd && s.ProductCd==frozen.ProductCd && s.LotNo==frozen.LotNo
                && s.LocationCd==frozen.FromLocationCd && s.LocationCd==frozen.ToLocationCd,t))
                throw new ArgumentException("WM-MSG-010: 移動先は移動元と異なるロケーションを指定してください。");
            using var inheritance=InheritTransactions(a);
            var pair=await MoveCoreAsync(frozen,t);
            effects.AddRange(_db.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity));
            a.AtCommit(async c=>
            {
                await a.RequireStockKey(frozen.WarehouseCd,frozen.FromLocationCd,frozen.ProductCd,frozen.LotNo,false,c);
                await a.RequireStockKey(frozen.WarehouseCd,frozen.ToLocationCd,frozen.ProductCd,frozen.LotNo,false,c);
            });return pair;
        },ct);
        foreach(var effect in effects)await PublishCommittedCancellationAsync(effect);return result;
    }
    // Ordinary independent commands have no authenticated old-obligation/physical-fact capability.
    // UNRSV only reduces an existing reservation; parent-owned facts use their own bounded primitives.
    private async Task GuardIndependentLocationUse(OperationsAccessV1 access,string warehouse,string location,string type,CancellationToken ct)
    {
        if(type==WmsTxnType.UNRSV)return;
        async Task Check(CancellationToken token)
        {
            await access.RequireLocation(warehouse,location,token);
            var blocked=await _db.Locations.AsNoTracking().Where(l=>!l.IsDeleted && l.WarehouseCd==warehouse
                && l.LocationCd==location).Select(l=>(bool?)l.IsBlocked).SingleOrDefaultAsync(token);
            if(blocked==null)throw new OrderPriceCorrectionException("LOCATION_CONTROL_UNAVAILABLE",409);
            if(blocked.Value)
                throw new OrderPriceCorrectionException("LOCATION_OPERATION_BLOCKED",409);
        }
        // OperationsOwner holds the native Location table fence until this stock transaction ends.
        // Recheck too: a same-transaction participant can still change the control after admission.
        await Check(ct);access.AtCommit(Check);
    }
    // Only audited Core parent Owners call this assembly-internal primitive, after their own object/field checks.
    // A pre-existing transaction is never accepted by the public independent entry above.
    internal Task<string> ApplyWithinOwnerAsync(StockMovementRequest request,CancellationToken ct=default)
    {
        RequireParentTransaction();
        if(request.RelatedType=="STOCKTAKE")throw new OrderPriceCorrectionException("INTERNAL_STOCK_CAPABILITY_REQUIRED",403);
        return ApplyInternalAsync(request,false,ct);
    }
    private void RequireParentTransaction()
    {
        if(!_db.Database.IsNpgsql() || _db.Database.CurrentTransaction==null || !Owns(_db))
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    }
    internal async Task<string> ApplyStockTakeWithinOwnerAsync(StockMovementRequest request,string number,CancellationToken ct)
    {
        RequireParentTransaction();
        if(request.TxnType!=WmsTxnType.ADJ || request.RelatedType!="STOCKTAKE" || request.RelatedNo!=number
            || !await _db.StockTakes.AnyAsync(h=>h.StockTakeNo==number && !h.IsDeleted
                && (h.Status==StockTakeStatus.DiffReview || h.Status==StockTakeStatus.AwaitingApproval),ct)
            || !await _db.StockTakeDetails.AnyAsync(d=>d.StockTakeNo==number && !d.IsDeleted && d.AdjustTxnNo==null
                && d.WarehouseCd==request.WarehouseCd && d.LocationCd==request.LocationCd && d.ProductCd==request.ProductCd && d.LotNo==request.LotNo
                && d.DiffQty==request.Qty && d.UnitPrice==request.UnitPrice && d.ApprovalStatus!=StockTakeDetailApproval.Rejected,ct))
            throw new OrderPriceCorrectionException("INTERNAL_STOCK_CAPABILITY_REQUIRED",403);
        return await ApplyInternalAsync(request,false,ct,stockTakeApproval:true);
    }
}
