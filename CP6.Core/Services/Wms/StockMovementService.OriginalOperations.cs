using System.Runtime.ExceptionServices;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence.Tenancy;
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
        return ExecuteOriginal(frozen.OperationId,"adjust",bytes,(mover,access,token)=>mover.ApplyV1Adjustment(frozen,access,token),ct,
            trustedStorage:_tenantStorage is { V1AdjustEnabled: true } ? _tenantStorage : null);
    }

    private async Task<(StockOperationResultV1 result,Stock[] before)> ApplyV1Adjustment(StockApplyCommandV1 command,
        OperationsAccessV1 access,CancellationToken ct)
    {
        if(command.AllowNegativeOverride || string.Equals(command.RelatedType,"STOCKTAKE",StringComparison.OrdinalIgnoreCase))
            throw new OrderPriceCorrectionException("INTERNAL_STOCK_CAPABILITY_REQUIRED",403);
        access.RequirePriceWrite();command.OperatorCd=access.Actor;
        await access.RequireStockKey(command.WarehouseCd,command.LocationCd,command.ProductCd,command.LotNo,true,ct);
        await GuardIndependentLocationUse(access,command.WarehouseCd,command.LocationCd,command.TxnType,ct);
        var before=await Snapshot(command.WarehouseCd,[command.LocationCd],command.ProductCd,command.LotNo,ct);
        using var inheritance=InheritTransactions(access);
        var no=await ApplyWithinOwnerAsync(command,ct);
        access.AtCommit(c=>access.RequireStockKey(command.WarehouseCd,command.LocationCd,command.ProductCd,command.LotNo,false,c));
        return (new(command.OperationId,"APPLY",TxnNo:no),before);
    }
    public Task<StockOperationResultV1> MoveCommandAsync(StockMoveCommandV1 request,CancellationToken ct=default)
    {
        if(request==null || request.OperationId==Guid.Empty)throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        var frozen=OperationsOwnerV1.Freeze(request);
        var bytes=MainOwnerOperationJournalV1.Encode(new {kind="MOVE",request=frozen});
        return ExecuteOriginal(frozen.OperationId,"move",bytes,async(mover,a,t)=>
        {
            frozen.OperatorCd=a.Actor;
            await a.RequireStockKey(frozen.WarehouseCd,frozen.FromLocationCd,frozen.ProductCd,frozen.LotNo,true,t);
            await a.RequireStockKey(frozen.WarehouseCd,frozen.ToLocationCd,frozen.ProductCd,frozen.LotNo,true,t);
            await mover.GuardIndependentLocationUse(a,frozen.WarehouseCd,frozen.FromLocationCd,WmsTxnType.MOVE,t);
            await mover.GuardIndependentLocationUse(a,frozen.WarehouseCd,frozen.ToLocationCd,WmsTxnType.MOVE,t);
            if(await mover._db.Stocks.AnyAsync(s=>s.WarehouseCd==frozen.WarehouseCd && s.ProductCd==frozen.ProductCd && s.LotNo==frozen.LotNo
                && s.LocationCd==frozen.FromLocationCd && s.LocationCd==frozen.ToLocationCd,t))
                throw new ArgumentException("WM-MSG-010: 移動先は移動元と異なるロケーションを指定してください。");
            var before=await mover.Snapshot(frozen.WarehouseCd,[frozen.FromLocationCd,frozen.ToLocationCd],frozen.ProductCd,frozen.LotNo,t);
            using var inheritance=mover.InheritTransactions(a);
            var pair=await mover.MoveCoreAsync(frozen,t);
            a.AtCommit(async c=>
            {
                await a.RequireStockKey(frozen.WarehouseCd,frozen.FromLocationCd,frozen.ProductCd,frozen.LotNo,false,c);
                await a.RequireStockKey(frozen.WarehouseCd,frozen.ToLocationCd,frozen.ProductCd,frozen.LotNo,false,c);
            });
            return (new StockOperationResultV1(frozen.OperationId,"MOVE",OutTxnNo:pair.OutTxnNo,InTxnNo:pair.InTxnNo),before);
        },ct,trustedStorage:_tenantStorage is { V1MoveEnabled: true } ? _tenantStorage : null,writeEntry:StockV1WriteEntry.Move);
    }
    private Task<Stock[]> Snapshot(string warehouse,string[] locations,string product,string lot,CancellationToken ct)
        =>_db.Stocks.AsNoTracking().Where(s=>!s.IsDeleted && s.WarehouseCd==warehouse && locations.Contains(s.LocationCd)
            && s.ProductCd==product && s.LotNo==lot).OrderBy(s=>s.Id).ToArrayAsync(ct);

    private async Task<StockOperationResultV1> ExecuteOriginal(Guid operation,string action,byte[] input,
        Func<StockMovementService,OperationsAccessV1,CancellationToken,Task<(StockOperationResultV1 result,Stock[] before)>> apply,CancellationToken ct,
        string contract=OperationContract,TenantStockReadStore? trustedStorage=null,StockV1WriteEntry writeEntry=StockV1WriteEntry.Apply)
    {
        MainOwnerOperationJournalV1.Original? original=null;
        var effects=new List<StockTransaction>();
        async Task<string> SelectAction(CP6Context context,CancellationToken token)
        {
            original=await new MainOwnerOperationJournalV1(context).Read(OperationOwner,operation,token);
            return original==null?action:"query";
        }
        async Task<StockOperationResultV1> Stage(StockMovementService mover,OperationsAccessV1 access,CancellationToken token)
        {
            if(original!=null)
            {
                RequireOriginalActor(access,original);
                if(original.State!="CLOSED" && original.Contract!=contract)
                    throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
                return await mover.DiscloseStockOriginal(access,original,operation,input,token);
            }
            var changed=await apply(mover,access,token);
            var context=mover._db;
            effects.AddRange(context.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity));
            var stocks=context.ChangeTracker.Entries<Stock>().Select(e=>e.Entity).OrderBy(s=>s.Id).ToArray();
            var saved=new StoredStockResult(changed.result,stocks.Select(s=>s.Id).ToArray(),effects.Select(s=>s.Id).ToArray());
            var tenantWriter=context is StockMovementWriteContextV1;
            await new MainOwnerOperationJournalV1(context,tenantWriter).Store(OperationOwner,contract,operation,access.ActorId,input,saved,saved.StockIds,
                new {actor=access.ActorId,action,before=changed.before,after=stocks,transactions=effects},token);
            await Integration.StockOutbox.StockOutboxEnqueueV1.Enqueue(context,operation,token,tenantWriter);
            return changed.result;
        }
        var result=trustedStorage is null
            ? await Owner.ExecuteAsync(OperationsOwnerV1.Stock,action,true,(access,token)=>Stage(this,access,token),ct,
                actionAtFence:token=>SelectAction(_db,token))
            : await Owner.ExecuteStockOperationAsync(trustedStorage,writeEntry,(context,access,token)=>
                Stage(new StockMovementService(context,new WmsSequenceService(context),_notifier,_finBridge),access,token),SelectAction,ct);
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
        if(original.Actor!=a.ActorId || original.Contract is not (OperationContract or CreationOperationContract))
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
        if(close && _tenantStorage is { V1AdjustEnabled: true } writer)
            return Owner.ExecuteStockOperationAsync(writer,StockV1WriteEntry.Close,
                (context,access,token)=>Recover(context,access,token),(_,_)=>Task.FromResult("query"),ct);

        return Owner.ExecuteAsync(OperationsOwnerV1.Stock,"query",close,async(access,token)=>
        {
            if(close || _tenantStorage is not { V1AdjustEnabled: true } storage)
                return await Recover(_db,access,token);

            var outcome=await storage.ReadAsync<(StockOperationV1 Result,ExceptionDispatchInfo? Failure)>(_db,access.ActorId,
                async (context,readToken)=>
                {
                    var scoped=access.ForStockRead(context);
                    try
                    {
                        var result=await Recover(context,scoped,readToken);
                        await scoped.ValidateFinalAsync(readToken);
                        return (result,null);
                    }
                    catch(OrderPriceCorrectionException error)
                    {
                        return (default!,ExceptionDispatchInfo.Capture(error));
                    }
                },token,requireJournalRls:true);
            outcome.Failure?.Throw();
            return outcome.Result;
        },ct,readOnlyAuthorityFence:!close);

        async Task<StockOperationV1> Recover(CP6Context context,OperationsAccessV1 access,CancellationToken token)
        {
            var journal=new MainOwnerOperationJournalV1(context,stockTenantWriter:context is StockMovementWriteContextV1);
            var original=await journal.Read(OperationOwner,operation,token);
            if(original!=null)
            {
                RequireOriginalActor(access,original);
                return original.State=="CLOSED"?new(operation,"CLOSED"):
                    new StockOperationV1(operation,"COMMITTED",await DiscloseStockOriginal(access,original,operation,null,token));
            }
            if(!close)return new StockOperationV1(operation,"UNKNOWN");
            var result=new StockOperationV1(operation,"CLOSED");
            await journal.Store(OperationOwner,OperationContract,operation,access.ActorId,null,result,[],
                new {action="close-uncommitted-operation",actor=access.ActorId},token);
            return result;
        }
    }
}
