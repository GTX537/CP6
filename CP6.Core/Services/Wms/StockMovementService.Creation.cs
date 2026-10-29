using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

public partial class StockMovementService : IStockCreationOperationsV2
{
    private const string CreationOperationContract="Main.Wms.StockOperation.v2";
    public Task<StockCreationContextV2> CreationContextAsync(string action,StockCreationKeyV2 key,CancellationToken ct=default)
    {
        if(action is not ("adjust" or "move"))throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
        return Owner.ExecuteAsync(OperationsOwnerV1.Stock,action,false,(a,t)=>a.StockCreationContext(key,t),ct,readOnlyAuthorityFence:true);
    }
    public Task<StockOperationResultV1> ApplyCommandAsync(StockApplyCommandV2 request,CancellationToken ct=default)
    {
        if(request==null || request.OperationId==Guid.Empty)throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        var frozen=OperationsOwnerV1.Freeze(request);var bytes=MainOwnerOperationJournalV1.Encode(new{kind="APPLY",request=frozen});
        return ExecuteOriginal(frozen.OperationId,"adjust",bytes,async(a,t)=>
        {
            if(frozen.AllowNegativeOverride || string.Equals(frozen.RelatedType,"STOCKTAKE",StringComparison.OrdinalIgnoreCase))
                throw new OrderPriceCorrectionException("INTERNAL_STOCK_CAPABILITY_REQUIRED",403);
            Validate(frozen);a.RequirePriceWrite();frozen.OperatorCd=a.Actor;
            var before=await Snapshot(frozen.WarehouseCd,[frozen.LocationCd],frozen.ProductCd,frozen.LotNo,t);
            await PrepareStockCreation(a,frozen,frozen.NewStock,null,t);
            await GuardIndependentLocationUse(a,frozen.WarehouseCd,frozen.LocationCd,frozen.TxnType,t);
            using var inheritance=InheritTransactions(a);
            var no=await ApplyWithinOwnerAsync(frozen,t);
            a.AtCommit(c=>a.RequireStockKey(frozen.WarehouseCd,frozen.LocationCd,frozen.ProductCd,frozen.LotNo,false,c));
            return(new StockOperationResultV1(frozen.OperationId,"APPLY",TxnNo:no),before);
        },ct,CreationOperationContract);
    }
    public Task<StockOperationResultV1> MoveCommandAsync(StockMoveCommandV2 request,CancellationToken ct=default)
    {
        if(request==null || request.OperationId==Guid.Empty)throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        var frozen=OperationsOwnerV1.Freeze(request);var bytes=MainOwnerOperationJournalV1.Encode(new{kind="MOVE",request=frozen});
        return ExecuteOriginal(frozen.OperationId,"move",bytes,async(a,t)=>
        {
            frozen.OperatorCd=a.Actor;
            // This creation intent applies only to the destination; source custody must already exist and be authorized.
            await a.RequireStockKey(frozen.WarehouseCd,frozen.FromLocationCd,frozen.ProductCd,frozen.LotNo,false,t);
            var before=await Snapshot(frozen.WarehouseCd,[frozen.FromLocationCd,frozen.ToLocationCd],frozen.ProductCd,frozen.LotNo,t);
            var metadata=await ReadMoveMetadataAsync(frozen,t);
            var target=new StockMovementRequest{TxnType=WmsTxnType.MOVE,WarehouseCd=frozen.WarehouseCd,
                LocationCd=frozen.ToLocationCd,ProductCd=frozen.ProductCd,LotNo=frozen.LotNo,Qty=frozen.Qty,OperatorCd=a.Actor};
            metadata.CopyTo(target);
            await PrepareStockCreation(a,target,frozen.NewStock,metadata,t);
            await GuardIndependentLocationUse(a,frozen.WarehouseCd,frozen.FromLocationCd,WmsTxnType.MOVE,t);
            await GuardIndependentLocationUse(a,frozen.WarehouseCd,frozen.ToLocationCd,WmsTxnType.MOVE,t);
            using var inheritance=InheritTransactions(a);
            var pair=await MoveCoreAsync(frozen,t);
            a.AtCommit(async c=>
            {
                await a.RequireStockKey(frozen.WarehouseCd,frozen.FromLocationCd,frozen.ProductCd,frozen.LotNo,false,c);
                await a.RequireStockKey(frozen.WarehouseCd,frozen.ToLocationCd,frozen.ProductCd,frozen.LotNo,false,c);
            });
            return(new StockOperationResultV1(frozen.OperationId,"MOVE",OutTxnNo:pair.OutTxnNo,InTxnNo:pair.InTxnNo),before);
        },ct,CreationOperationContract);
    }
    private async Task PrepareStockCreation(OperationsAccessV1 access,StockMovementRequest request,StockCreationIntentV2? intent,
        MoveStockMetadata? metadata,CancellationToken ct)
    {
        var key=new StockCreationKeyV2(request.WarehouseCd,request.LocationCd,request.ProductCd,request.LotNo);
        var binding=await access.BeginStockCreation(key,intent,ct);
        if(binding==null)return;
        var stock=NewStock(request,metadata);stock.Id=binding.Id;_db.Stocks.Add(stock);
        // Still the original Owner transaction. Bind the exact new zero-balance row before W137 captures its source.
        await _db.SaveChangesAsync(ct);await access.CompleteStockCreation(binding,ct);
    }
    private static Stock NewStock(StockMovementRequest req,MoveStockMetadata? moveMetadata)=>new()
    {
        WarehouseCd=req.WarehouseCd,LocationCd=req.LocationCd,ProductCd=req.ProductCd,LotNo=req.LotNo,
        PhysicalQty=0m,AllocatedQty=0m,AvailableQty=0m,UnitCd=req.UnitCd,ReceiveDate=req.ReceiveDate,
        ExpiryDate=req.ExpiryDate,UnitPrice=req.UnitPrice,OwnerType=req.OwnerType,OwnerCd=req.OwnerCd,
        PaperRollNo=req.PaperRollNo,QcStatus=moveMetadata?.QcStatus??StockQcStatus.Pending,
        RecallFlag=moveMetadata?.RecallFlag??false,Creator=req.OperatorCd
    };
}
