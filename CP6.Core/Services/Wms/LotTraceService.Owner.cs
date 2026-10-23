using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class LotTraceService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static OrderPriceCorrectionException Error(string code, int status) => new(code, status);
    public Task<LotTraceResult> TraceForwardAsync(string productCd, string lotNo) => !_db.Database.IsNpgsql() ? TraceForwardCoreAsync(productCd, lotNo) : LegacyRead<LotTraceResult>();
    public Task<LotTraceResult> TraceBackwardAsync(string productCd, string lotNo) => !_db.Database.IsNpgsql() ? TraceBackwardCoreAsync(productCd, lotNo) : LegacyRead<LotTraceResult>();
    public Task<LotStockSummary?> GetStockSummaryAsync(string productCd, string lotNo) => !_db.Database.IsNpgsql() ? GetStockSummaryCoreAsync(productCd, lotNo) : LegacyRead<LotStockSummary?>();
    private Task<T> LegacyRead<T>() => Owner.ExecuteAsync<T>(OperationsOwnerV1.LotTrace, "query", false, (_, _) => throw Error("OWNER_READ_VERSION_REQUIRED", 409));
    public Task<int> SetRecallFlagAsync(string productCd, string lotNo, bool flag, string? userName) => !_db.Database.IsNpgsql() ? SetRecallFlagCoreAsync(productCd, lotNo, flag, userName) :
        Owner.ExecuteAsync<int>(OperationsOwnerV1.LotTrace, "recall", true, (_, _) => throw Error("ROW_VERSION_REQUIRED", 409));

    private static void ValidateLot(string product, string lot)
    {
        // Empty LotNo is the existing no-lot inventory key; retain its exact representation.
        if (string.IsNullOrWhiteSpace(product) || product.Length > 20 || lot == null || lot.Length > 30)
            throw Error("LOT_QUERY_INVALID", 400);
    }
    // An optimistic version of the complete observed set, never a permission or a client-supplied scope.
    private string RecallVersion(string product, string lot, IEnumerable<Stock> stocks)
    {
        var rows = stocks.OrderBy(s => s.Id).Select(s => new { s.Id, Version = s.RowVersion });
        if (stocks.Any(s => s.RowVersion is not { Length: > 0 })) throw Error("ROW_VERSION_UNAVAILABLE", 503);
        var value = JsonSerializer.Serialize(new { Tenant = _db.CurrentTenantId, Product = product, Lot = lot, Rows = rows });
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("CP6/LOT-RECALL/1\n" + value)));
    }
    private LotStockSnapshotV1? StockSnapshot(string product, string lot, Stock[] stocks) => stocks.Length == 0 ? null : new()
    {
        RecallVersion = RecallVersion(product, lot, stocks), StockCount = stocks.Length,
        PhysicalQty = InventoryReportValuesV1.Sum(stocks.Select(s => s.PhysicalQty)).ToString(),
        AvailableQty = InventoryReportValuesV1.Sum(stocks.Select(s => s.AvailableQty)).ToString(),
        RecallState = stocks.All(s => s.RecallFlag) ? "ALL" : stocks.Any(s => s.RecallFlag) ? "MIXED" : "NONE",
        ExpiryDate = stocks.Select(s => s.ExpiryDate).Min()
    };

    public Task<LotTraceSnapshotV1> ReadOwnerAsync(string product, string lot, string direction)
        => Owner.ExecuteAsync(OperationsOwnerV1.LotTrace, "query", false, async (a, ct) =>
        {
            ValidateLot(product, lot); if (direction is not ("FORWARD" or "BACKWARD")) throw Error("LOT_QUERY_INVALID", 400);
            var source = await a.SourceAsync(OperationsOwnerV1.Stock, ct);
            var stocks = await a.LotStocks(source).Where(s => s.ProductCd == product && s.LotNo == lot).ToArrayAsync(ct);
            var transactions = a.LotTransactions(source).Where(t => t.ProductCd == product && t.LotNo == lot);
            var rows = await transactions.OrderBy(t => t.TxnDateTime).ThenBy(t => t.Id).ToArrayAsync(ct);
            var customers = direction == "FORWARD" ? await ReadCustomers(a, transactions, ct) : [];
            var suppliers = direction == "BACKWARD" ? await ReadSuppliers(a, transactions, ct) : [];
            return new LotTraceSnapshotV1
            {
                ProductCd = product, LotNo = lot, Direction = direction, Stock = StockSnapshot(product, lot, stocks),
                OperatorReadable = a.LotFieldReadable("OperatorCd"), RemarkReadable = a.LotFieldReadable("Remark"),
                CustomerNameReadable = a.LotFieldReadable("CustomerName"), SupplierNameReadable = a.LotFieldReadable("SupplierName"),
                Customers = customers, Suppliers = suppliers,
                Nodes = rows.Select(t => new LotTraceNodeV1 { TxnNo = t.TxnNo, TxnType = t.TxnType, TxnAt = t.TxnDateTime,
                    WarehouseCd = t.WarehouseCd, LocationCd = t.LocationCd, Qty = InventoryReportValuesV1.Exact(t.Qty).ToString(),
                    RelatedNo = t.RelatedNo, RelatedType = t.RelatedType, OperatorCd = a.LotFieldReadable("OperatorCd") ? t.OperatorCd : null,
                    Remark = a.LotFieldReadable("Remark") ? t.Remark : null }).ToList()
            };
        });

    private async Task<List<LotCustomerV1>> ReadCustomers(OperationsAccessV1 a, IQueryable<StockTransaction> transactions, CancellationToken ct)
    {
        var source = await a.OptionalLotSource(OperationsOwnerV1.Outbound, ct); if (source == null) return [];
        var allowed = source.Outbounds();
        var rows = await (from t in transactions
            join d in _db.OutboundOrderDetails.Where(d => !d.IsDeleted) on t.TxnNo equals d.ShipTxnNo
            join o in a.Outbounds() on d.OutboundNo equals o.OutboundNo
            where t.RelatedType == "OUTBOUND" && t.RelatedNo == o.OutboundNo
                && (t.TxnType == WmsTxnType.OUT || t.TxnType == WmsTxnType.MOVE && t.Qty < 0)
                && d.ProductCd == t.ProductCd && d.LotNo == t.LotNo && d.LocationCd == t.LocationCd
                && (d.WarehouseCd ?? o.WarehouseCd) == t.WarehouseCd && allowed.Any(x => x.Id == o.Id)
            select new { t.Id, t.Qty, t.TxnDateTime, o.OutboundNo, o.WebOrderNo, o.CustomerCd, o.CustomerName }).ToArrayAsync(ct);
        return rows.GroupBy(x => x.OutboundNo).Select(g =>
        {
            var first = g.First(); var distinct = g.DistinctBy(x => x.Id).ToArray();
            return new LotCustomerV1 { OutboundNo = g.Key, WebOrderNo = first.WebOrderNo, CustomerCd = first.CustomerCd,
                CustomerName = a.LotFieldReadable("CustomerName") ? first.CustomerName : null,
                Qty = InventoryReportValuesV1.Sum(distinct.Select(x => Math.Abs(x.Qty))).ToString(), ShippedAt = distinct.Max(x => x.TxnDateTime) };
        }).OrderByDescending(x => x.ShippedAt).ThenBy(x => x.OutboundNo, StringComparer.Ordinal).ToList();
    }
    private async Task<List<LotSupplierV1>> ReadSuppliers(OperationsAccessV1 a, IQueryable<StockTransaction> transactions, CancellationToken ct)
    {
        var sourceReceipt = await a.OptionalLotSource(OperationsOwnerV1.Receipt, ct); if (sourceReceipt == null) return [];
        var sourceInbound = await a.OptionalLotSource(OperationsOwnerV1.Inbound, ct);
        var receipts = a.Receipts(); var allowedReceipt = sourceReceipt.Receipts();
        var inbounds = a.Inbounds(); var allowedInbound = sourceInbound?.Inbounds() ?? inbounds.Where(_ => false);
        var rows = await (from t in transactions
            join d in _db.InboundReceiptDetails.Where(d => !d.IsDeleted) on t.TxnNo equals d.StockTxnNo
            join r in receipts on d.ReceiptNo equals r.ReceiptNo
            where t.TxnType == WmsTxnType.IN && t.RelatedType == "INBOUND" && t.RelatedNo == r.ReceiptNo
                && d.ProductCd == t.ProductCd && d.LotNo == t.LotNo && d.LocationCd == t.LocationCd && r.WarehouseCd == t.WarehouseCd
                && allowedReceipt.Any(x => x.Id == r.Id)
                && (r.InboundNo == null || r.InboundNo == "" || inbounds.Any(x => x.InboundNo == r.InboundNo && allowedInbound.Any(y => y.Id == x.Id)))
            select new { t.Id, t.Qty, t.TxnDateTime, r.ReceiptNo, r.InboundNo }).ToArrayAsync(ct);
        var numbers = rows.Select(x => x.InboundNo).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
        var parents = await inbounds.Where(x => numbers.Contains(x.InboundNo) && allowedInbound.Any(y => y.Id == x.Id)).ToDictionaryAsync(x => x.InboundNo, ct);
        return rows.GroupBy(x => x.ReceiptNo).Select(g =>
        {
            var first = g.First(); var distinct = g.DistinctBy(x => x.Id).ToArray();
            var inbound = first.InboundNo != null ? parents.GetValueOrDefault(first.InboundNo) : null;
            return new LotSupplierV1 { ReceiptNo = g.Key, InboundNo = first.InboundNo, SupplierCd = inbound?.SupplierCd,
                SupplierName = a.LotFieldReadable("SupplierName") ? inbound?.SupplierName : null,
                Qty = InventoryReportValuesV1.Sum(distinct.Select(x => x.Qty)).ToString(), ReceivedAt = distinct.Max(x => x.TxnDateTime) };
        }).OrderByDescending(x => x.ReceivedAt).ThenBy(x => x.ReceiptNo, StringComparer.Ordinal).ToList();
    }

    public Task<LotRecallResultV1> RecallOwnerAsync(LotRecallCommandV1 input)
    {
        var command = OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.LotTrace, "recall", true, async (a, ct) =>
        {
            ValidateLot(command.ProductCd, command.LotNo);
            if (string.IsNullOrEmpty(command.RecallVersion)) throw Error("ROW_VERSION_REQUIRED", 409);
            var source = await a.SourceAsync(OperationsOwnerV1.Stock, ct);
            var all = _db.Stocks.Where(s => !s.IsDeleted && s.ProductCd == command.ProductCd && s.LotNo == command.LotNo);
            var allowed = a.LotStocks(source);
            async Task Require(CancellationToken t)
            {
                if (!await all.AnyAsync(t) || await all.AnyAsync(s => !allowed.Any(x => x.Id == s.Id), t)) throw Error("OBJECT_FORBIDDEN", 403);
            }
            await Require(ct); var rows = await all.OrderBy(s => s.Id).ToArrayAsync(ct);
            if (command.RecallVersion != RecallVersion(command.ProductCd, command.LotNo, rows)) throw Error("ROW_VERSION_CONFLICT", 409);
            foreach (var row in rows) { row.RecallFlag = command.Flag; row.Modifier = a.Actor; row.ModifyDate = DateTime.Now; }
            await _db.SaveChangesAsync(ct); a.AtCommit(Require);
            return new LotRecallResultV1 { ProductCd = command.ProductCd, LotNo = command.LotNo, Flag = command.Flag, AffectedStocks = rows.Length };
        });
    }
}
