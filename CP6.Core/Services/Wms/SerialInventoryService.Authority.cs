using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed partial class SerialInventoryService
{
    private readonly OperationsOwnerV1? _owner;

    private async Task<SerialOperationResult> ExecuteSerialCommand(
        Func<string?, OperationsAccessV1?, CancellationToken, Task<SerialOperationResult>> command,
        string? userName, bool stockEffect, CancellationToken ct)
    {
        if (!_db.Database.IsNpgsql()) return await command(userName, null, ct);
        var mover = _stock as StockMovementService;
        if (_owner?.Owns(_db) != true || stockEffect && (mover == null || !mover.Owns(_db)))
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        var effects = new List<StockTransaction>();
        var result = await _owner.ExecuteAsync(OperationsOwnerV1.Mobile, "serial-manage", true,
            async (access, token) =>
            {
                // The existing Owner already fences Stock, products, tasks and receipts.
                // Also serialize these identity rows with raw SQL writers in this transaction.
                await _db.Database.ExecuteSqlRawAsync("""
                    LOCK TABLE public."T_StockSerial", public."T_StockSerialTransaction" IN SHARE ROW EXCLUSIVE MODE
                    """, token);
                try
                {
                    var saved = await command(access.Actor, access, token);
                    effects.AddRange(_db.ChangeTracker.Entries<StockTransaction>().Select(e => e.Entity));
                    return saved;
                }
                catch (ArgumentException error) { throw new InvalidOperationException(error.Message, error); }
            }, ct);
        foreach (var effect in effects) await mover!.PublishCommittedCancellationAsync(effect);
        return result;
    }

    private static async Task RequireSerialLocations(OperationsAccessV1 access,
        IEnumerable<(string Warehouse, string Location)> locations, CancellationToken ct)
    {
        foreach (var (warehouse, location) in locations.Distinct())
        {
            await access.RequireLocation(warehouse, location, ct);
            access.AtCommit(token => access.RequireLocation(warehouse, location, token));
        }
    }

    private async Task<string> ApplySerialStock(StockMovementRequest request, OperationsAccessV1? access, CancellationToken ct)
    {
        if (access is null) return await _stock.ApplyAsync(request, ct);
        var stock = await access.ActionAsync(OperationsOwnerV1.Stock, "adjust", ct);
        stock.RequirePriceWrite();
        await stock.RequireStockKey(request.WarehouseCd, request.LocationCd, request.ProductCd, request.LotNo, true, ct);
        await stock.GuardNewLocationUse(request.WarehouseCd, request.LocationCd, ct);
        var mover = (StockMovementService)_stock;
        using var inheritance = mover.InheritTransactions(access);
        var number = await mover.ApplyWithinOwnerAsync(request, ct);
        stock.AtCommit(token => stock.RequireStockKey(request.WarehouseCd, request.LocationCd, request.ProductCd, request.LotNo, false, token));
        return number;
    }

    private async Task<(string OutTxnNo, string InTxnNo)> MoveSerialStock(StockMoveRequest request, OperationsAccessV1? access, CancellationToken ct)
    {
        if (access is null) return await _stock.MoveAsync(request, ct);
        var stock = await access.ActionAsync(OperationsOwnerV1.Stock, "move", ct);
        await stock.RequireStockKey(request.WarehouseCd, request.FromLocationCd, request.ProductCd, request.LotNo, true, ct);
        await stock.RequireStockKey(request.WarehouseCd, request.ToLocationCd, request.ProductCd, request.LotNo, true, ct);
        await stock.GuardNewLocationUse(request.WarehouseCd, request.FromLocationCd, ct);
        await stock.GuardNewLocationUse(request.WarehouseCd, request.ToLocationCd, ct);
        var result = await ((StockMovementService)_stock).MoveWithinOwnerAsync(request, access, ct);
        stock.AtCommit(async token =>
        {
            await stock.RequireStockKey(request.WarehouseCd, request.FromLocationCd, request.ProductCd, request.LotNo, false, token);
            await stock.RequireStockKey(request.WarehouseCd, request.ToLocationCd, request.ProductCd, request.LotNo, false, token);
        });
        return result;
    }
}
