using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Wms;

public sealed class SerialInventoryService : ISerialInventoryService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly CP6Context _db;
    private readonly IStockMovementService _stock;

    public SerialInventoryService(CP6Context db, IStockMovementService stock)
    {
        _db = db;
        _stock = stock;
    }

    public async Task<PagedResult<StockSerialDto>> GetAsync(
        string? productCd,
        string? serialNo,
        string? warehouseCd,
        string? locationCd,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.StockSerials.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(productCd))
            query = query.Where(x => x.ProductCd == productCd);
        if (!string.IsNullOrWhiteSpace(serialNo))
            query = query.Where(x => x.SerialNo.Contains(serialNo));
        if (!string.IsNullOrWhiteSpace(warehouseCd))
            query = query.Where(x => x.WarehouseCd == warehouseCd);
        if (!string.IsNullOrWhiteSpace(locationCd))
            query = query.Where(x => x.LocationCd == locationCd);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.ProductCd).ThenBy(x => x.SerialNo)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<StockSerialDto>
        {
            Items = rows.Select(Map).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<SerialOperationResult> EnableTrackingAsync(
        EnableSerialTrackingRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        EnsureOperation(request.OperationId);
        if (string.IsNullOrWhiteSpace(request.ProductCd)
            || !ProductTrackingMode.UsesSerial(request.TrackingMode))
            throw new ArgumentException("WM-SERIAL-TRACKING-DATA");
        var receiptKey = $"SERIAL:{request.ProductCd.Trim()}";
        var replay = await ReplayAsync(receiptKey, request.OperationId, "enable", ct);
        if (replay is not null) return replay;

        IDbContextTransaction? tx = await BeginTransactionAsync(ct);
        try
        {
            var productCd = request.ProductCd.Trim();
            var product = await _db.ProductMasters.FirstOrDefaultAsync(
                x => !x.IsDeleted && x.ProductCd == productCd, ct)
                ?? throw new ArgumentException("WM-SERIAL-PRODUCT-NOT-FOUND");
            if (product.SerialTrackingLockedAt.HasValue
                || await _db.StockSerialTransactions.AnyAsync(
                    x => x.ProductCd == productCd, ct))
                throw new MobileTaskConflictException("WM-SERIAL-TRACKING-LOCKED");

            var stocks = await _db.Stocks.AsNoTracking()
                .Where(x => !x.IsDeleted
                            && x.ProductCd == productCd
                            && x.PhysicalQty != 0m)
                .Select(x => new
                {
                    x.WarehouseCd, x.LocationCd, x.LotNo, x.PhysicalQty
                })
                .ToListAsync(ct);
            var stockWarehouses = stocks
                .Select(x => x.WarehouseCd)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (stockWarehouses.Count > 0)
            {
                var enabledWarehouses = await _db.WmsFeatureFlags.AsNoTracking()
                    .Where(x => !x.IsDeleted
                                && x.SerialLpnEnabled
                                && stockWarehouses.Contains(x.WarehouseCd))
                    .Select(x => x.WarehouseCd)
                    .ToListAsync(ct);
                if (stockWarehouses.Except(
                        enabledWarehouses,
                        StringComparer.OrdinalIgnoreCase).Any())
                    throw new MobileTaskConflictException(
                        "WM-R2B-FEATURE-DISABLED");
            }
            if (stocks.Any(x => x.PhysicalQty < 0m
                                || decimal.Truncate(x.PhysicalQty) != x.PhysicalQty))
                throw new MobileTaskConflictException("WM-SERIAL-STOCK-NOT-WHOLE");
            var expected = stocks.Sum(x => (int)x.PhysicalQty);
            if (expected != request.ExistingSerials.Count)
                throw new MobileTaskConflictException("WM-SERIAL-CONVERSION-QTY-MISMATCH");
            var duplicate = request.ExistingSerials
                .GroupBy(x => x.SerialNo.Trim(), StringComparer.OrdinalIgnoreCase)
                .Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1);
            if (duplicate)
                throw new ArgumentException("WM-SERIAL-DUPLICATE");

            foreach (var stock in stocks)
            {
                var count = request.ExistingSerials.Count(x =>
                    x.WarehouseCd == stock.WarehouseCd
                    && x.LocationCd == stock.LocationCd
                    && (x.LotNo ?? string.Empty) == stock.LotNo);
                if (count != (int)stock.PhysicalQty)
                    throw new MobileTaskConflictException("WM-SERIAL-CONVERSION-BUCKET-MISMATCH");
            }

            var rows = new List<StockSerial>();
            foreach (var input in request.ExistingSerials)
            {
                var serial = new StockSerial
                {
                    ProductCd = productCd,
                    SerialNo = input.SerialNo.Trim(),
                    WarehouseCd = input.WarehouseCd.Trim(),
                    LocationCd = input.LocationCd.Trim(),
                    LotNo = input.LotNo?.Trim() ?? string.Empty,
                    Status = StockSerialStatus.InStock,
                    Creator = userName
                };
                var txnNo = AddLedger(
                    request.OperationId, "SERIALIZE", serial, null,
                    serial.LocationCd, null, userName, null);
                serial.LastTxnNo = txnNo;
                rows.Add(serial);
                _db.StockSerials.Add(serial);
            }
            product.TrackingMode = request.TrackingMode;
            product.SerialTrackingLockedAt = rows.Count > 0
                ? DateTime.UtcNow
                : null;
            product.Modifier = userName;
            product.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync(ct);

            var result = new SerialOperationResult
            {
                OperationId = request.OperationId,
                TxnType = "SERIALIZE",
                ProductCd = productCd,
                SerialCount = rows.Count,
                Serials = rows.Select(Map).ToList()
            };
            AddReceipt(receiptKey, request.OperationId, "enable", result);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    public async Task<SerialOperationResult> PostAsync(
        SerialLifecycleRequest request,
        string? userName,
        CancellationToken ct = default)
    {
        ValidateLifecycle(request);
        var productCd = request.ProductCd.Trim();
        var receiptKey = $"SERIAL:{productCd}";
        var replay = await ReplayAsync(receiptKey, request.OperationId, "lifecycle", ct);
        if (replay is not null) return replay;
        await EnsureFeatureAsync(request.WarehouseCd, ct);

        IDbContextTransaction? tx = await BeginTransactionAsync(ct);
        try
        {
            var product = await _db.ProductMasters.FirstOrDefaultAsync(
                x => !x.IsDeleted && x.ProductCd == productCd, ct)
                ?? throw new ArgumentException("WM-SERIAL-PRODUCT-NOT-FOUND");
            if (!ProductTrackingMode.UsesSerial(product.TrackingMode))
                throw new MobileTaskConflictException("WM-SERIAL-TRACKING-NOT-ENABLED");
            if (ProductTrackingMode.UsesLot(product.TrackingMode)
                && string.IsNullOrWhiteSpace(request.LotNo))
                throw new ArgumentException("WM-SERIAL-LOT-REQUIRED");

            var serialNos = request.SerialNos.Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (serialNos.Count != request.SerialNos.Count)
                throw new ArgumentException("WM-SERIAL-DUPLICATE");
            var existing = await _db.StockSerials
                .Where(x => !x.IsDeleted
                            && x.ProductCd == productCd
                            && serialNos.Contains(x.SerialNo))
                .ToListAsync(ct);
            var type = request.TxnType.Trim().ToUpperInvariant();
            var stockTxnNos = new List<string>();

            switch (type)
            {
                case "RECEIVE":
                    if (existing.Count != 0)
                        throw new MobileTaskConflictException("WM-SERIAL-ALREADY-EXISTS");
                    RequireTarget(request);
                    foreach (var serialNo in serialNos)
                    {
                        var row = new StockSerial
                        {
                            ProductCd = productCd,
                            SerialNo = serialNo,
                            WarehouseCd = request.WarehouseCd.Trim(),
                            LocationCd = request.ToLocationCd!.Trim(),
                            LotNo = request.LotNo?.Trim() ?? string.Empty,
                            LpnNo = NullIfWhiteSpace(request.LpnNo),
                            Status = StockSerialStatus.InStock,
                            Creator = userName
                        };
                        row.LastTxnNo = AddLedger(request.OperationId, type, row,
                            null, row.LocationCd, row.LpnNo, userName, request.DeviceId);
                        _db.StockSerials.Add(row);
                        existing.Add(row);
                    }
                    stockTxnNos.Add(await _stock.ApplyAsync(StockRequest(
                        request, WmsTxnType.IN, serialNos.Count, userName,
                        request.ToLocationCd!), ct));
                    break;

                case "PUTAWAY":
                case "MOVE":
                    EnsureExistingAtSource(existing, serialNos, request);
                    RequireTarget(request);
                    var moved = await _stock.MoveAsync(new StockMoveRequest
                    {
                        WarehouseCd = request.WarehouseCd.Trim(),
                        FromLocationCd = request.FromLocationCd!.Trim(),
                        ToLocationCd = request.ToLocationCd!.Trim(),
                        ProductCd = productCd,
                        LotNo = request.LotNo?.Trim() ?? string.Empty,
                        Qty = serialNos.Count,
                        OperatorCd = userName,
                        Remark = $"{type} serialized stock"
                    }, ct);
                    stockTxnNos.Add(moved.OutTxnNo);
                    stockTxnNos.Add(moved.InTxnNo);
                    foreach (var row in existing)
                    {
                        var from = row.LocationCd;
                        row.LocationCd = request.ToLocationCd!.Trim();
                        row.Status = StockSerialStatus.InStock;
                        row.LastTxnNo = AddLedger(request.OperationId, type, row,
                            from, row.LocationCd, row.LpnNo, userName, request.DeviceId);
                    }
                    break;

                case "PICK":
                    EnsureExistingAtSource(existing, serialNos, request);
                    foreach (var row in existing)
                    {
                        row.Status = StockSerialStatus.Picked;
                        row.LastTxnNo = AddLedger(request.OperationId, type, row,
                            row.LocationCd, row.LocationCd, row.LpnNo,
                            userName, request.DeviceId);
                    }
                    break;

                case "SHIP":
                    EnsureExistingAtSource(existing, serialNos, request);
                    stockTxnNos.Add(await _stock.ApplyAsync(StockRequest(
                        request, WmsTxnType.OUT, serialNos.Count, userName,
                        request.FromLocationCd!), ct));
                    foreach (var row in existing)
                    {
                        row.Status = StockSerialStatus.Shipped;
                        row.LastTxnNo = AddLedger(request.OperationId, type, row,
                            row.LocationCd, null, row.LpnNo, userName, request.DeviceId);
                        row.LpnNo = null;
                    }
                    break;

                case "COUNT":
                    EnsureExistingAtSource(existing, serialNos, request);
                    foreach (var row in existing)
                        row.LastTxnNo = AddLedger(request.OperationId, type, row,
                            row.LocationCd, row.LocationCd, row.LpnNo,
                            userName, request.DeviceId);
                    break;

                case "RETURN":
                    RequireTarget(request);
                    if (existing.Count != serialNos.Count
                        || existing.Any(x => x.Status != StockSerialStatus.Shipped))
                        throw new MobileTaskConflictException("WM-SERIAL-NOT-SHIPPED");
                    stockTxnNos.Add(await _stock.ApplyAsync(StockRequest(
                        request, WmsTxnType.IN, serialNos.Count, userName,
                        request.ToLocationCd!), ct));
                    foreach (var row in existing)
                    {
                        row.WarehouseCd = request.WarehouseCd.Trim();
                        row.LocationCd = request.ToLocationCd!.Trim();
                        row.LotNo = request.LotNo?.Trim() ?? row.LotNo;
                        row.Status = StockSerialStatus.Returned;
                        row.LastTxnNo = AddLedger(request.OperationId, type, row,
                            null, row.LocationCd, null, userName, request.DeviceId);
                    }
                    break;

                default:
                    throw new ArgumentException("WM-SERIAL-TXN-TYPE");
            }

            product.SerialTrackingLockedAt ??= DateTime.UtcNow;
            foreach (var row in existing)
            {
                row.Modifier = userName;
                row.ModifyDate = DateTime.Now;
            }
            await _db.SaveChangesAsync(ct);
            await ReconcileAsync(productCd, request.WarehouseCd,
                request.LotNo ?? string.Empty,
                new[] { request.FromLocationCd, request.ToLocationCd }, ct);

            var result = new SerialOperationResult
            {
                OperationId = request.OperationId,
                TxnType = type,
                ProductCd = productCd,
                SerialCount = serialNos.Count,
                StockTxnNos = stockTxnNos,
                Serials = existing.Select(Map).ToList()
            };
            AddReceipt(receiptKey, request.OperationId, "lifecycle", result);
            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    private async Task ReconcileAsync(
        string productCd,
        string warehouseCd,
        string lotNo,
        IEnumerable<string?> locations,
        CancellationToken ct)
    {
        foreach (var location in locations.Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var physical = await _db.Stocks.AsNoTracking()
                .Where(x => !x.IsDeleted
                            && x.ProductCd == productCd
                            && x.WarehouseCd == warehouseCd
                            && x.LocationCd == location
                            && x.LotNo == lotNo)
                .SumAsync(x => (decimal?)x.PhysicalQty, ct) ?? 0m;
            var serialCount = await _db.StockSerials.AsNoTracking()
                .CountAsync(x => !x.IsDeleted
                                 && x.ProductCd == productCd
                                 && x.WarehouseCd == warehouseCd
                                 && x.LocationCd == location
                                 && x.LotNo == lotNo
                                 && x.Status != StockSerialStatus.Shipped, ct);
            if (physical != serialCount)
                throw new MobileTaskConflictException("WM-SERIAL-AGGREGATE-MISMATCH");
        }
    }

    private static StockMovementRequest StockRequest(
        SerialLifecycleRequest request,
        string txnType,
        decimal qty,
        string? userName,
        string locationCd) => new()
        {
            TxnType = txnType,
            WarehouseCd = request.WarehouseCd.Trim(),
            LocationCd = locationCd.Trim(),
            ProductCd = request.ProductCd.Trim(),
            LotNo = request.LotNo?.Trim() ?? string.Empty,
            Qty = qty,
            RelatedNo = request.OperationId.ToString(),
            RelatedType = $"SERIAL_{request.TxnType.Trim().ToUpperInvariant()}",
            OperatorCd = userName
        };

    private string AddLedger(
        Guid operationId,
        string type,
        StockSerial serial,
        string? from,
        string? to,
        string? lpnNo,
        string? userName,
        string? deviceId)
    {
        var txnNo = $"S{DateTime.UtcNow:yyMMdd}{Guid.NewGuid():N}"[..25];
        _db.StockSerialTransactions.Add(new StockSerialTransaction
        {
            TenantId = serial.TenantId == Guid.Empty
                ? _db.CurrentTenantId
                : serial.TenantId,
            TxnNo = txnNo,
            OperationId = operationId,
            TxnType = type,
            ProductCd = serial.ProductCd,
            SerialNo = serial.SerialNo,
            WarehouseCd = serial.WarehouseCd,
            LotNo = serial.LotNo,
            FromLocationCd = from,
            ToLocationCd = to,
            LpnNo = lpnNo,
            OperatorCd = userName,
            DeviceId = NullIfWhiteSpace(deviceId)
        });
        return txnNo;
    }

    private static void EnsureExistingAtSource(
        List<StockSerial> existing,
        IReadOnlyCollection<string> requested,
        SerialLifecycleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FromLocationCd)
            || existing.Count != requested.Count
            || existing.Any(x => x.Status == StockSerialStatus.Shipped
                                 || x.WarehouseCd != request.WarehouseCd
                                 || x.LocationCd != request.FromLocationCd
                                 || x.LotNo != (request.LotNo ?? string.Empty)))
            throw new MobileTaskConflictException("WM-SERIAL-SOURCE-MISMATCH");
    }

    private static void RequireTarget(SerialLifecycleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ToLocationCd))
            throw new ArgumentException("WM-SERIAL-TARGET-REQUIRED");
    }

    private static void ValidateLifecycle(SerialLifecycleRequest request)
    {
        EnsureOperation(request.OperationId);
        if (string.IsNullOrWhiteSpace(request.TxnType)
            || string.IsNullOrWhiteSpace(request.ProductCd)
            || string.IsNullOrWhiteSpace(request.WarehouseCd)
            || request.SerialNos.Count == 0
            || request.SerialNos.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("WM-SERIAL-LIFECYCLE-DATA");
    }

    private async Task EnsureFeatureAsync(string warehouseCd, CancellationToken ct)
    {
        if (!await _db.WmsFeatureFlags.AsNoTracking()
            .AnyAsync(x => !x.IsDeleted
                           && x.WarehouseCd == warehouseCd
                           && x.SerialLpnEnabled, ct))
            throw new MobileTaskConflictException("WM-R2B-FEATURE-DISABLED");
    }

    private async Task<SerialOperationResult?> ReplayAsync(
        string key,
        Guid operationId,
        string command,
        CancellationToken ct)
    {
        var receipt = await _db.TaskCommandReceipts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == operationId, ct);
        if (receipt is null) return null;
        if (receipt.TaskNo != key || receipt.CommandName != command)
            throw new MobileTaskConflictException("WM-V2-OPERATION-ID-USED");
        return JsonSerializer.Deserialize<SerialOperationResult>(
            receipt.ResultJson, JsonOptions);
    }

    private void AddReceipt(
        string key,
        Guid operationId,
        string command,
        SerialOperationResult result)
        => _db.TaskCommandReceipts.Add(new TaskCommandReceipt
        {
            OperationId = operationId,
            TaskNo = key,
            CommandName = command,
            ResultJson = JsonSerializer.Serialize(result, JsonOptions)
        });

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken ct)
        => _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

    private static void EnsureOperation(Guid operationId)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("WM-V2-OPERATION-ID-REQUIRED");
    }

    private static StockSerialDto Map(StockSerial x) => new()
    {
        ProductCd = x.ProductCd,
        SerialNo = x.SerialNo,
        WarehouseCd = x.WarehouseCd,
        LocationCd = x.LocationCd,
        LotNo = x.LotNo,
        LpnNo = x.LpnNo,
        Status = x.Status,
        LastTxnNo = x.LastTxnNo,
        RowVersion = x.RowVersion is { Length: > 0 }
            ? Convert.ToBase64String(x.RowVersion)
            : string.Empty
    };

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
