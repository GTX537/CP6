using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

public partial class StockMovementService
{
    internal async Task<string[]> ApplyReturnCorrectionWithinOwnerAsync(ReturnCorrectionCandidateV1 c,string actor,FulfilmentRefV1? releaseCorrection,CancellationToken ct)
    {
        RequireParentTransaction(); var target =c.Target; var key =target.SourcePool;
        var original =await _db.Stocks.AsNoTracking().SingleAsync(x => x.Id ==key.PoolId && !x.IsDeleted,ct);
        var metadata =MoveStockMetadata.From(original); var amount =StockRmaPostingV1.Number(c.Quantity.Value);
        StockMovementRequest Request(ReturnDispositionStockKeyV2 k,string type,decimal value,MoveStockMetadata properties)
        {
            var r =new StockMovementRequest { TxnType =type,WarehouseCd =k.Warehouse,LocationCd =k.Location,ProductCd =k.Product,LotNo =k.Lot,
                Qty =value,OperatorCd =actor,RelatedType ="RMA-CORRECTION",RelatedNo =c.Id.ToString(),Remark =c.Reason };
            properties.CopyTo(r); return r;
        }
        if (target.DestinationPool is {} destination)
        {
            var current =await _db.Stocks.AsNoTracking().SingleAsync(x => x.WarehouseCd ==destination.Warehouse && x.LocationCd ==destination.Location && x.ProductCd ==destination.Product && x.LotNo ==destination.Lot && !x.IsDeleted,ct);
            var movedMetadata =MoveStockMetadata.From(current);
            if (target.Kind =="RESALE_STOCK")
            {
                if (releaseCorrection ==null || movedMetadata.QcStatus !=StockQcStatus.Passed || metadata.QcStatus is not (StockQcStatus.Pending or StockQcStatus.Passed)) throw Error("RETURN_QUALITY_RELEASE_CORRECTION_REQUIRED",409);
                if (movedMetadata with { QcStatus =metadata.QcStatus } !=metadata) throw Error("STOCK_MOVE_METADATA_CONFLICT",409);
            }
            else if (movedMetadata !=metadata) throw Error("STOCK_MOVE_METADATA_CONFLICT",409);
            var outgoing =await ApplyInternalAsync(Request(destination,WmsTxnType.MOVE,-amount,movedMetadata),false,ct,moveMetadata:movedMetadata,preserveReservations:true);
            var incoming =await ApplyInternalAsync(Request(key,WmsTxnType.MOVE,amount,metadata),false,ct,moveMetadata:metadata,preserveReservations:true);
            return [outgoing,incoming];
        }
        return [await ApplyInternalAsync(Request(key,WmsTxnType.ADJ,target.Axis =="PHYSICAL_RECEIPT" ? -amount : amount,metadata),false,ct,moveMetadata:metadata,preserveReservations:true)];
    }
}
