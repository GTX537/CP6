using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Wms;

public class StockQcService : IStockQcService
{
    private readonly CP6Context _db;
    private readonly ILogger<StockQcService> _logger;

    public StockQcService(CP6Context db, ILogger<StockQcService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Stock> SetStockQcStatusAsync(Guid stockId, string newStatus, string? reason, string? userName)
    {
        ValidateStatus(newStatus);

        var stock = await _db.Stocks
            .FirstOrDefaultAsync(s => s.Id == stockId && !s.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-QC-404: Stock not found.");

        var oldStatus = stock.QcStatus;
        stock.QcStatus = newStatus;
        stock.Modifier = userName;
        stock.ModifyDate = DateTime.Now;

        await _db.SaveChangesAsync();

        _logger.LogInformation("[StockQc] {StockId} {OldStatus}->{NewStatus} reason={Reason}",
            stockId, oldStatus, newStatus, reason);

        return stock;
    }

    public async Task<int> MarkLinkedStockByWorkOrderAsync(string workOrderNo, string newStatus, string? reason, string? userName)
    {
        ValidateStatus(newStatus);

        var pairs = await (
            from receipt in _db.InboundReceipts
            join detail in _db.InboundReceiptDetails on receipt.ReceiptNo equals detail.ReceiptNo
            where receipt.WorkOrderNo == workOrderNo
                  && !receipt.IsDeleted
                  && !detail.IsDeleted
            select new { detail.ProductCd, detail.LotNo })
            .Distinct()
            .ToListAsync();

        if (pairs.Count == 0)
            return 0;

        var stocks = await _db.Stocks
            .Where(s => !s.IsDeleted)
            .ToListAsync();

        var affected = 0;
        foreach (var stock in stocks)
        {
            if (!pairs.Any(p => p.ProductCd == stock.ProductCd && p.LotNo == stock.LotNo))
                continue;

            var oldStatus = stock.QcStatus;
            stock.QcStatus = newStatus;
            stock.Modifier = userName;
            stock.ModifyDate = DateTime.Now;
            affected++;

            _logger.LogInformation("[StockQc] {StockId} {OldStatus}->{NewStatus} reason={Reason}",
                stock.Id, oldStatus, newStatus, reason);
        }

        await _db.SaveChangesAsync();
        return affected;
    }

    private static void ValidateStatus(string newStatus)
    {
        if (newStatus is StockQcStatus.Pending or StockQcStatus.Passed or StockQcStatus.Failed or StockQcStatus.Hold)
            return;

        throw new ArgumentException("WM-MSG-QC-001: Invalid QC status.", nameof(newStatus));
    }
}
