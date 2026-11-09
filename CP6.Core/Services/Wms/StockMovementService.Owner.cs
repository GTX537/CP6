using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class StockMovementService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true && Owns(_db)?_owner:
        throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<string> ApplyAsync(StockMovementRequest req,CancellationToken ct=default)
        =>ApplyIndependentAsync(req,null,ct);
    internal Task<string> ApplyLegacyHttpAsync(StockMovementRequest req,CancellationToken ct)
        =>ApplyIndependentAsync(req,_tenantStorage is { V1AdjustEnabled: true }?_tenantStorage:null,ct);
    private async Task<string> ApplyIndependentAsync(StockMovementRequest req,TenantStockReadStore? storage,CancellationToken ct)
    {
        if(!_db.Database.IsNpgsql())return await ApplyCoreAsync(req,ct);
        var frozen=OperationsOwnerV1.Freeze(req);var effects=new List<StockTransaction>();
        async Task<string> Stage(StockMovementService mover,OperationsAccessV1 a,CancellationToken t)
        {
            // Both switches belong to bounded parent operations, never a user-selected escape hatch.
            if(frozen.AllowNegativeOverride || string.Equals(frozen.RelatedType,"STOCKTAKE",StringComparison.OrdinalIgnoreCase))
                throw new OrderPriceCorrectionException("INTERNAL_STOCK_CAPABILITY_REQUIRED",403);
            a.RequirePriceWrite();frozen.OperatorCd=a.Actor;
            await a.RequireStockKey(frozen.WarehouseCd,frozen.LocationCd,frozen.ProductCd,frozen.LotNo,true,t);
            await mover.GuardIndependentLocationUse(a,frozen.WarehouseCd,frozen.LocationCd,frozen.TxnType,t);
            using var inheritance=mover.InheritTransactions(a);
            var no=await mover.ApplyWithinOwnerAsync(frozen,t);
            effects.AddRange(mover._db.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity));
            a.AtCommit(c=>a.RequireStockKey(frozen.WarehouseCd,frozen.LocationCd,frozen.ProductCd,frozen.LotNo,false,c));return no;
        }
        var number=storage is null
            ? await Owner.ExecuteAsync(OperationsOwnerV1.Stock,"adjust",true,(access,token)=>Stage(this,access,token),ct)
            : await Owner.ExecuteStockOperationAsync(storage,StockWriteEntry.ApplyLegacy,
                (context,access,token)=>Stage(ForTenantWriter(context),access,token),(_,_)=>Task.FromResult("adjust"),ct);
        foreach(var effect in effects)await PublishCommittedCancellationAsync(effect);return number;
    }
    private StockMovementService ForTenantWriter(StockMovementWriteContextV1 context)
        =>new(context,new WmsSequenceService(context),_notifier,_finBridge);

    public Task<(string OutTxnNo,string InTxnNo)> MoveAsync(StockMoveRequest req,CancellationToken ct=default)
        =>MoveIndependentAsync(req,null,ct);
    internal Task<(string OutTxnNo,string InTxnNo)> MoveLegacyHttpAsync(StockMoveRequest req,CancellationToken ct)
        =>MoveIndependentAsync(req,_tenantStorage is { V1MoveEnabled: true }?_tenantStorage:null,ct);
    private async Task<(string OutTxnNo,string InTxnNo)> MoveIndependentAsync(StockMoveRequest req,TenantStockReadStore? storage,CancellationToken ct)
    {
        if(!_db.Database.IsNpgsql())return await MoveCoreAsync(req,ct);
        var frozen=OperationsOwnerV1.Freeze(req);var effects=new List<StockTransaction>();
        async Task<(string OutTxnNo,string InTxnNo)> Stage(StockMovementService mover,OperationsAccessV1 a,CancellationToken t)
        {
            frozen.OperatorCd=a.Actor;
            await a.RequireStockKey(frozen.WarehouseCd,frozen.FromLocationCd,frozen.ProductCd,frozen.LotNo,true,t);
            await a.RequireStockKey(frozen.WarehouseCd,frozen.ToLocationCd,frozen.ProductCd,frozen.LotNo,true,t);
            await mover.GuardIndependentLocationUse(a,frozen.WarehouseCd,frozen.FromLocationCd,WmsTxnType.MOVE,t);
            await mover.GuardIndependentLocationUse(a,frozen.WarehouseCd,frozen.ToLocationCd,WmsTxnType.MOVE,t);
            if(await mover._db.Stocks.AnyAsync(s=>s.WarehouseCd==frozen.WarehouseCd && s.ProductCd==frozen.ProductCd && s.LotNo==frozen.LotNo
                && s.LocationCd==frozen.FromLocationCd && s.LocationCd==frozen.ToLocationCd,t))
                throw new ArgumentException("WM-MSG-010: 移動先は移動元と異なるロケーションを指定してください。");
            using var inheritance=mover.InheritTransactions(a);
            var pair=await mover.MoveCoreAsync(frozen,t);
            effects.AddRange(mover._db.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity));
            a.AtCommit(async c=>
            {
                await a.RequireStockKey(frozen.WarehouseCd,frozen.FromLocationCd,frozen.ProductCd,frozen.LotNo,false,c);
                await a.RequireStockKey(frozen.WarehouseCd,frozen.ToLocationCd,frozen.ProductCd,frozen.LotNo,false,c);
            });return pair;
        }
        var result=storage is null
            ? await Owner.ExecuteAsync(OperationsOwnerV1.Stock,"move",true,(access,token)=>Stage(this,access,token),ct)
            : await Owner.ExecuteStockOperationAsync(storage,StockWriteEntry.MoveLegacy,
                (context,access,token)=>Stage(ForTenantWriter(context),access,token),(_,_)=>Task.FromResult("move"),ct);
        foreach(var effect in effects)await PublishCommittedCancellationAsync(effect);return result;
    }
    // Ordinary independent commands have no authenticated old-obligation/physical-fact capability.
    // UNRSV only reduces an existing reservation; parent-owned facts use their own bounded primitives.
    private Task GuardIndependentLocationUse(OperationsAccessV1 access,string warehouse,string location,string type,CancellationToken ct)
        =>type==WmsTxnType.UNRSV?Task.CompletedTask:access.GuardNewLocationUse(warehouse,location,ct);
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
