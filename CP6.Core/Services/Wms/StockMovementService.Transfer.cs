using CP6.Core.Services.Wms.Transfer;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.Transfer.TransferEnvironmentStoreV1;

namespace CP6.Core.Services.Wms;

public partial class StockMovementService
{
    internal static string TransferMetadata(Stock source) => TransferRequestStoreV1.Digest(MoveStockMetadata.From(source));
    internal async Task<TransferStageMovementV1> ApplyTransferWithinOwnerAsync(Guid stage, TransferStageLineV1 line, string actor, CancellationToken ct)
    {
        RequireParentTransaction();
        var source = await _db.Stocks.AsNoTracking().SingleAsync(x => x.Id == line.Selection.StockId && !x.IsDeleted, ct);
        var metadata = MoveStockMetadata.From(source);
        var destination = await _db.Stocks.AsNoTracking().SingleOrDefaultAsync(x => x.WarehouseCd == line.DestinationWarehouse
            && x.LocationCd == line.DestinationLocation && x.ProductCd == source.ProductCd && x.LotNo == source.LotNo && !x.IsDeleted, ct);
        if (destination != null && MoveStockMetadata.From(destination) != metadata) throw Error("STOCK_MOVE_METADATA_CONFLICT", 409);
        StockMovementRequest Command(string warehouse, string location, decimal quantity)
        {
            var c = new StockMovementRequest { TxnType = WmsTxnType.MOVE, WarehouseCd = warehouse, LocationCd = location,
                ProductCd = source.ProductCd, LotNo = source.LotNo, Qty = quantity, RelatedType = "TRANSFER-STAGE", RelatedNo = stage.ToString(), OperatorCd = actor };
            metadata.CopyTo(c); return c;
        }
        var amount = StockRmaPostingV1.Number(line.Selection.Quantity);
        var outgoing = await ApplyInternalAsync(Command(line.SourceWarehouse, line.SourceLocation, -amount), false, ct, moveMetadata: metadata, preserveReservations: true);
        var incoming = await ApplyInternalAsync(Command(line.DestinationWarehouse, line.DestinationLocation, amount), false, ct, moveMetadata: metadata, preserveReservations: true);
        var outId = await _db.StockTransactions.Where(x => x.TxnNo == outgoing).Select(x => x.Id).SingleAsync(ct);
        var inId = await _db.StockTransactions.Where(x => x.TxnNo == incoming).Select(x => x.Id).SingleAsync(ct);
        return new(line.Selection.RequestLineId, source.Id, line.Selection.Quantity, outId, inId);
    }
}
