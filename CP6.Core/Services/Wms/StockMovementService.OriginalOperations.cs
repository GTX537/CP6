using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class StockMovementService : IStockOriginalOperationsV1
{
    private const string OperationOwner="Main.Wms.StockMovement",OperationContract="Main.Wms.StockOperation.v1";
    private sealed record StoredStockResult(StockOperationResultV1 Result,Guid[] StockIds,Guid[] TransactionIds);

    public Task<StockOperationResultV1> ApplyCommandAsync(StockApplyCommandV1 request,CancellationToken ct=default)
    {
        if(request==null || request.OperationId==Guid.Empty)throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        var frozen=OperationsOwnerV1.Freeze(request);
        var bytes=MainOwnerOperationJournalV1.Encode(new {kind="APPLY",request=frozen});
        return ExecuteOriginal(frozen.OperationId,"adjust",bytes,async(a,t)=>
        {
            if(frozen.AllowNegativeOverride || string.Equals(frozen.RelatedType,"STOCKTAKE",StringComparison.OrdinalIgnoreCase))
                throw new OrderPriceCorrectionException("INTERNAL_STOCK_CAPABILITY_REQUIRED",403);
            a.RequirePriceWrite();frozen.OperatorCd=a.Actor;
            await a.RequireStockKey(frozen.WarehouseCd,frozen.LocationCd,frozen.ProductCd,frozen.LotNo,true,t);
            var before=await Snapshot(frozen.WarehouseCd,[frozen.LocationCd],frozen.ProductCd,frozen.LotNo,t);
            using var inheritance=InheritTransactions(a);
            var no=await ApplyWithinOwnerAsync(frozen,t);
            a.AtCommit(c=>a.RequireStockKey(frozen.WarehouseCd,frozen.LocationCd,frozen.ProductCd,frozen.LotNo,false,c));
            return (new StockOperationResultV1(frozen.OperationId,"APPLY",TxnNo:no),before);
        },ct);
    }
    public Task<StockOperationResultV1> MoveCommandAsync(StockMoveCommandV1 request,CancellationToken ct=default)
    {
        if(request==null || request.OperationId==Guid.Empty)throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        var frozen=OperationsOwnerV1.Freeze(request);
        var bytes=MainOwnerOperationJournalV1.Encode(new {kind="MOVE",request=frozen});
        return ExecuteOriginal(frozen.OperationId,"move",bytes,async(a,t)=>
        {
            frozen.OperatorCd=a.Actor;
            await a.RequireStockKey(frozen.WarehouseCd,frozen.FromLocationCd,frozen.ProductCd,frozen.LotNo,true,t);
            await a.RequireStockKey(frozen.WarehouseCd,frozen.ToLocationCd,frozen.ProductCd,frozen.LotNo,true,t);
            if(await _db.Stocks.AnyAsync(s=>s.WarehouseCd==frozen.WarehouseCd && s.ProductCd==frozen.ProductCd && s.LotNo==frozen.LotNo
                && s.LocationCd==frozen.FromLocationCd && s.LocationCd==frozen.ToLocationCd,t))
                throw new ArgumentException("WM-MSG-010: 移動先は移動元と異なるロケーションを指定してください。");
            var before=await Snapshot(frozen.WarehouseCd,[frozen.FromLocationCd,frozen.ToLocationCd],frozen.ProductCd,frozen.LotNo,t);
            using var inheritance=InheritTransactions(a);
            var pair=await MoveCoreAsync(frozen,t);
            a.AtCommit(async c=>
            {
                await a.RequireStockKey(frozen.WarehouseCd,frozen.FromLocationCd,frozen.ProductCd,frozen.LotNo,false,c);
                await a.RequireStockKey(frozen.WarehouseCd,frozen.ToLocationCd,frozen.ProductCd,frozen.LotNo,false,c);
            });
            return (new StockOperationResultV1(frozen.OperationId,"MOVE",OutTxnNo:pair.OutTxnNo,InTxnNo:pair.InTxnNo),before);
        },ct);
    }
    private Task<Stock[]> Snapshot(string warehouse,string[] locations,string product,string lot,CancellationToken ct)
        =>_db.Stocks.AsNoTracking().Where(s=>!s.IsDeleted && s.WarehouseCd==warehouse && locations.Contains(s.LocationCd)
            && s.ProductCd==product && s.LotNo==lot).OrderBy(s=>s.Id).ToArrayAsync(ct);

    private async Task<StockOperationResultV1> ExecuteOriginal(Guid operation,string action,byte[] input,
        Func<OperationsAccessV1,CancellationToken,Task<(StockOperationResultV1 result,Stock[] before)>> apply,CancellationToken ct)
    {
        var journal=new MainOwnerOperationJournalV1(_db);MainOwnerOperationJournalV1.Original? original=null;
        var effects=new List<StockTransaction>();
        var result=await Owner.ExecuteAsync(OperationsOwnerV1.Stock,action,true,async(a,t)=>
        {
            if(original!=null)return await DiscloseStockOriginal(a,original,operation,input,t);
            var changed=await apply(a,t);
            effects.AddRange(_db.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity));
            var stocks=_db.ChangeTracker.Entries<Stock>().Select(e=>e.Entity).OrderBy(s=>s.Id).ToArray();
            var saved=new StoredStockResult(changed.result,stocks.Select(s=>s.Id).ToArray(),effects.Select(s=>s.Id).ToArray());
            await journal.Store(OperationOwner,OperationContract,operation,a.ActorId,input,saved,saved.StockIds,
                new {actor=a.ActorId,action,before=changed.before,after=stocks,transactions=effects},t);
            await Integration.StockOutbox.StockOutboxEnqueueV1.Enqueue(_db,operation,t);
            return changed.result;
        },ct,actionAtFence:async t=>
        {
            original=await journal.Read(OperationOwner,operation,t);return original==null?action:"query";
        });
        // Compatibility bridge/notifications remain best-effort. Replaying the original never invokes them again.
        foreach(var effect in effects)await PublishCommittedCancellationAsync(effect);
        return result;
    }
    private async Task<StockOperationResultV1> DiscloseStockOriginal(OperationsAccessV1 access,
        MainOwnerOperationJournalV1.Original original,Guid operation,byte[]? input,CancellationToken ct)
    {
        RequireOriginalActor(access,original);
        if(original.State=="CLOSED")throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input))
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var saved=JsonSerializer.Deserialize<StoredStockResult>(original.Result);
        if(saved==null || saved.Result.OperationId!=operation || saved.StockIds.Length==0 || saved.TransactionIds.Length==0
            || !saved.StockIds.SequenceEqual(original.Objects))throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_UNAVAILABLE",503);
        // Exact original IDs, never a newly-created row with the same business key. Both MOVE legs must remain readable.
        async Task Check(CancellationToken token)
        {
            if(await access.InventoryStocks().CountAsync(s=>saved.StockIds.Contains(s.Id),token)!=saved.StockIds.Length
                || await access.InventoryTransactions().CountAsync(s=>saved.TransactionIds.Contains(s.Id),token)!=saved.TransactionIds.Length)
                throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        }
        await Check(ct);access.AtCommit(Check);
        // Public original result contains references only, not quantities/prices or the private audit snapshot.
        return saved.Result;
    }
    private static void RequireOriginalActor(OperationsAccessV1 a,MainOwnerOperationJournalV1.Original original)
    {
        if(original.Actor!=a.ActorId || original.Contract!=OperationContract)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_FORBIDDEN",403);
    }
    public Task<StockOperationScopeV1> OperationScopeAsync(CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Stock,"query",false,(a,_)=>Task.FromResult(new StockOperationScopeV1(_db.CurrentTenantId,a.ActorId)),
            ct,readOnlyAuthorityFence:true);
    public Task<StockOperationV1> ReadOperationAsync(Guid operation,CancellationToken ct=default)=>RecoverStockOperation(operation,false,ct);
    public Task<StockOperationV1> CloseOperationAsync(Guid operation,CancellationToken ct=default)=>RecoverStockOperation(operation,true,ct);
    private Task<StockOperationV1> RecoverStockOperation(Guid operation,bool close,CancellationToken ct)
    {
        if(operation==Guid.Empty)throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        return Owner.ExecuteAsync(OperationsOwnerV1.Stock,"query",close,async(a,t)=>
        {
            var journal=new MainOwnerOperationJournalV1(_db);var original=await journal.Read(OperationOwner,operation,t);
            if(original!=null)
            {
                RequireOriginalActor(a,original);
                return original.State=="CLOSED"?new(operation,"CLOSED"):
                    new StockOperationV1(operation,"COMMITTED",await DiscloseStockOriginal(a,original,operation,null,t));
            }
            if(!close)return new StockOperationV1(operation,"UNKNOWN");
            var result=new StockOperationV1(operation,"CLOSED");
            await journal.Store(OperationOwner,OperationContract,operation,a.ActorId,null,result,[],new {action="close-uncommitted-operation",actor=a.ActorId},t);
            return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
