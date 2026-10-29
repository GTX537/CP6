using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;

namespace CP6.Core.Services.Wms;

public partial class StockMovementService
{
    // Only the typed independent disposition Owners can use this free-quantity participant.
    // It preserves reservations; neither MOVE nor SHIP implicitly releases another obligation.
    internal async Task<string[]> ApplyReturnDispositionWithinOwnerAsync(ReturnDispositionPreparationV2 p,string actor,FulfilmentRefV1? qualityRelease,CancellationToken ct)
    {
        RequireParentTransaction(); var key =p.Stock.Source;
        var source =await _db.Stocks.AsNoTracking().SingleAsync(x => x.Id ==key.PoolId && !x.IsDeleted,ct);
        var amount =StockRmaPostingV1.Number(p.Quantity.Value); var metadata =MoveStockMetadata.From(source);
        var releaseForResale =p.Definition.Kind =="RESALE_STOCK";
        if (releaseForResale !=(qualityRelease !=null)) throw Error("RETURN_QUALITY_RELEASE_REQUIRED",409);
        var destinationMetadata =releaseForResale ? metadata with { QcStatus =StockQcStatus.Passed } : metadata;
        if (p.Stock.Destination is {} target)
        {
            var existing =await _db.Stocks.AsNoTracking().SingleOrDefaultAsync(x => x.WarehouseCd ==target.Warehouse && x.LocationCd ==target.Location && x.ProductCd ==target.Product && x.LotNo ==target.Lot && !x.IsDeleted,ct);
            if (existing !=null && MoveStockMetadata.From(existing) !=destinationMetadata) throw Error("STOCK_MOVE_METADATA_CONFLICT",409);
        }
        StockMovementRequest Request(ReturnDispositionStockKeyV2 k,string type,decimal value,MoveStockMetadata properties)
        {
            var r =new StockMovementRequest { TxnType =type,WarehouseCd =k.Warehouse,LocationCd =k.Location,ProductCd =k.Product,LotNo =k.Lot,
                Qty =value,OperatorCd =actor,RelatedType ="RMA-DISPOSITION",RelatedNo =p.WorkId.ToString(),Remark =p.Definition.Kind };
            properties.CopyTo(r); return r;
        }
        if (p.Definition.PostingKind =="MOVE")
        {
            var moved =await ApplyInternalAsync(Request(key,WmsTxnType.MOVE,-amount,metadata),false,ct,moveMetadata:metadata,preserveReservations:true);
            var received =await ApplyInternalAsync(Request(p.Stock.Destination!,WmsTxnType.MOVE,amount,destinationMetadata),false,ct,moveMetadata:destinationMetadata,preserveReservations:true);
            return [moved,received];
        }
        var type =p.Definition.PostingKind =="SHIP" ? WmsTxnType.OUT : WmsTxnType.ADJ;
        return [await ApplyInternalAsync(Request(key,type,type ==WmsTxnType.OUT ? amount : -amount,metadata),false,ct,moveMetadata:metadata,preserveReservations:true)];
    }
}
