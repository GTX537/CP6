using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class StockMovementService
{
    private async Task<MoveStockMetadata> ReadMoveMetadataAsync(StockMoveRequest request, CancellationToken ct)
    {
        var stocks = _db.Stocks.AsNoTracking().Where(s => !s.IsDeleted
            && s.WarehouseCd == request.WarehouseCd && s.ProductCd == request.ProductCd && s.LotNo == request.LotNo);
        var source = await stocks.SingleOrDefaultAsync(s => s.LocationCd == request.FromLocationCd, ct);
        // Preserve the existing warehouse AllowNegative behavior for a missing source. ApplyInternalAsync
        // still decides whether its negative leg is permitted; no custody is inferred from the destination.
        var metadata = MoveStockMetadata.From(source ?? new Stock());
        var target = await stocks.SingleOrDefaultAsync(s => s.LocationCd == request.ToLocationCd, ct);
        // The legacy stock key cannot represent different owners/attributes at the same product/lot/location.
        // Even a zero-balance existing row keeps its identity and history. Never overwrite or blend it.
        if (target != null && metadata != MoveStockMetadata.From(target))
            throw new OrderPriceCorrectionException("STOCK_MOVE_METADATA_CONFLICT", 409);
        return metadata;
    }

    private sealed record MoveStockMetadata(string OwnerType, string? OwnerCd, string? UnitCd,
        decimal? UnitPrice, DateTime? ReceiveDate, DateTime? ExpiryDate, string? PaperRollNo,
        string QcStatus, bool RecallFlag)
    {
        internal static MoveStockMetadata From(Stock stock) => new(stock.OwnerType, stock.OwnerCd, stock.UnitCd,
            stock.UnitPrice, stock.ReceiveDate, stock.ExpiryDate, stock.PaperRollNo, stock.QcStatus, stock.RecallFlag);

        internal void CopyTo(StockMovementRequest request)
        {
            request.OwnerType = OwnerType;
            request.OwnerCd = OwnerCd;
            request.UnitCd = UnitCd;
            request.UnitPrice = UnitPrice;
            request.ReceiveDate = ReceiveDate;
            request.ExpiryDate = ExpiryDate;
            request.PaperRollNo = PaperRollNo;
        }
    }
}
