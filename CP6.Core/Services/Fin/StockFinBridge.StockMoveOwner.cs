using System.Data;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Options;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Fin;

public partial class StockFinBridge
{
    private static readonly JsonSerializerOptions StockMoveJson = new() { PropertyNameCaseInsensitive = true };
    private readonly Dictionary<Guid, Guid> _stagedStockMoves = [];

    internal static bool IsStockMoveEvent(IntegrationEvent evt) => evt.SourceModule == "WMS"
        && evt.TargetModule == "FIN" && evt.HookName == nameof(OnStockMovedAsync);

    internal async Task StageStockMoveAsync(CP6Context owner, StockTransaction txn, CancellationToken ct)
    {
        var relatedType = txn.RelatedType ?? "";
        if (Resolve(txn.TxnType, relatedType, txn.Qty).eventType is null) return;
        if (owner.CurrentTenantId != Db.CurrentTenantId || owner.Database.CurrentTransaction is null)
            throw new InvalidOperationException("WMS_FIN_OWNER_TRANSACTION_REQUIRED");

        if (owner is StockMovementWriteContextV1 writer)
        {
            var eventId = await StockMovementFinanceWriterStorageV1.StageAsync(writer, txn, ct);
            _stagedStockMoves.Add(txn.Id, eventId);
            return;
        }
        if (!ReferenceEquals(Db, owner))
            throw new InvalidOperationException("WMS_FIN_OWNER_TRANSACTION_REQUIRED");

        // Preserve the original inputs before PostgreSQL rounds the stock quantity and unit price to their column scales.
        var evt = new IntegrationEvent
        {
            Id = Guid.NewGuid(), TenantId = Db.CurrentTenantId, CorrelationId = Guid.NewGuid(),
            SourceModule = "WMS", TargetModule = "FIN", HookName = nameof(OnStockMovedAsync), SourceNo = txn.TxnNo,
            Status = IntegrationEventStatus.Pending, NextRetryAt = DateTime.UtcNow,
            PayloadJson = JsonSerializer.Serialize(new { version = 1, stockTransactionId = txn.Id, txnNo = txn.TxnNo,
                txn.TxnType, relatedType, txn.Qty, txn.UnitPrice }),
            Creator = string.IsNullOrWhiteSpace(txn.OperatorCd) ? "system" : txn.OperatorCd, CreateDate = DateTime.Now,
        };
        Db.IntegrationEvents.Add(evt);
        await Db.SaveChangesAsync(ct);
        _stagedStockMoves.Add(txn.Id, evt.Id);
    }

    internal async Task<bool> TryProcessCommittedStockMoveAsync(Guid stockTransactionId)
    {
        if (!_stagedStockMoves.TryGetValue(stockTransactionId, out var eventId)) return false;
        try { await ProcessStockMoveAsync(eventId, DateTime.UtcNow, null, CancellationToken.None); }
        catch (Exception error)
        {
            // The committed event is still the recovery identity for an interrupted or uncertain attempt.
            Logger.LogError(error, "Committed stock financial event {EventId} did not return a result", eventId);
        }
        return true;
    }

    internal async Task<IntegrationEvent?> ProcessStockMoveAsync(Guid eventId, DateTime dueBefore,
        IntegrationEventOptions? retry, CancellationToken ct)
    {
        if (!Db.Database.IsNpgsql() || Db.Database.CurrentTransaction is not null ||
            System.Transactions.Transaction.Current is not null || Db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("WMS_FIN_OWNER_TRANSACTION_REQUIRED");

        var tenant = Db.CurrentTenantId;
        await using var transaction = await Db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await AuthenticationCompletionGateV1.AcquireAsync(Db, ct);
        if (!await StockTenantIsActiveAsync(tenant, ct)) return null;
        var evt = await Db.IntegrationEvents.FromSqlInterpolated($"""
            SELECT * FROM public."T_IntegrationEvent" WHERE "TenantId"={tenant} AND "Id"={eventId} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        if (evt is null || evt.IsDeleted || !IsStockMoveEvent(evt) || evt.NextRetryAt is not { } due || due > dueBefore
            || (evt.Status != IntegrationEventStatus.Pending || evt.Attempts != 0)
                && (retry is null || evt.Status != IntegrationEventStatus.Failed || evt.Attempts >= retry.MaxAttempts))
            return null;

        const string effects = "wms_fin_stock_move";
        try
        {
            await transaction.CreateSavepointAsync(effects, ct);
            StockMoveAttempt result;
            try { result = await ApplyStockMoveAsync(evt, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Logger.LogError(error, "Stock financial effects failed for {EventId}", eventId);
                result = StockMoveAttempt.Failed(error is JsonException ? "DISPATCH-400" : error.ToString());
            }
            if (result.Status == IntegrationEventStatus.Failed)
            {
                await transaction.RollbackToSavepointAsync(effects, ct);
                Db.ChangeTracker.Clear();
            }
            await transaction.ReleaseSavepointAsync(effects, ct);

            Db.IntegrationEvents.Attach(evt);
            evt.Attempts++;
            var dead = result.Status == IntegrationEventStatus.Failed && retry is not null && evt.Attempts >= retry.MaxAttempts;
            evt.Status = dead ? IntegrationEventStatus.DeadLetter : result.Status;
            evt.TargetNo = result.EventType;
            evt.LastError = result.Error;
            evt.NextRetryAt = evt.Status == IntegrationEventStatus.Failed
                ? DateTime.UtcNow.AddSeconds(retry?.GetBackoffSeconds(evt.Attempts) ?? 60) : null;
            await Db.SaveChangesAsync(ct);
            if (!await StockTenantIsActiveAsync(tenant, ct)) return null;
            await transaction.CommitAsync(ct);
            return dead ? evt : null;
        }
        finally
        {
            Db.ChangeTracker.Clear();
        }
    }

    private async Task<StockMoveAttempt> ApplyStockMoveAsync(IntegrationEvent evt, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<StockMovePayload>(evt.PayloadJson, StockMoveJson);
        if (payload?.Qty is not decimal qty || string.IsNullOrWhiteSpace(evt.SourceNo) || payload.TxnNo != evt.SourceNo
            || (payload.Version, payload.StockTransactionId) is not ((null, null) or (1, not null))
            || payload.StockTransactionId == Guid.Empty)
            return StockMoveAttempt.Failed("DISPATCH-400");

        var movements = Db.StockTransactions.AsNoTracking().Where(row => row.TenantId == evt.TenantId && !row.IsDeleted);
        // Historical events have no UUID. The tenant and original transaction number are a native unique key.
        var stock = payload.StockTransactionId is Guid id
            ? await movements.SingleOrDefaultAsync(row => row.Id == id, ct)
            : await movements.SingleOrDefaultAsync(row => row.TxnNo == evt.SourceNo, ct);
        var relatedType = payload.RelatedType ?? "";
        var storedPrice = payload.UnitPrice is decimal unit ? RoundStockValue(unit, 4) : (decimal?)null;
        if (stock is null || stock.TxnNo != evt.SourceNo || stock.TxnType != payload.TxnType
            || (stock.RelatedType ?? "").TrimEnd(' ') != relatedType.TrimEnd(' ')
            || stock.Qty != RoundStockValue(qty, 8) || stock.UnitPrice != storedPrice)
            return StockMoveAttempt.Failed("DISPATCH-400");

        var (posting, skipReason) = StockPosting(stock, relatedType, qty, payload.UnitPrice);
        if (posting is null) return new(IntegrationEventStatus.Skipped, null, skipReason);

        var journals = Db.JournalEntries.AsNoTracking().Where(row => row.TenantId == evt.TenantId
            && row.Source == VoucherSource.Inventory && row.SourceDocNo == evt.SourceNo);
        var posted = journals.Include(row => row.Lines).Where(row => row.Status == JournalStatus.Posted);
        var voucher = await posted.SingleOrDefaultAsync(ct);
        if (voucher is null)
        {
            // A reversal is a separate accepted accounting decision. A retry must not recreate that original.
            if (await journals.AnyAsync(row => row.Status == JournalStatus.Reversed, ct))
                return StockMoveAttempt.Failed("DISPATCH-400");
            var result = await _engine.GenerateAsync(posting);
            if (!result.Ok) return StockMoveAttempt.Failed(result.Code);
            voucher = await posted.SingleOrDefaultAsync(ct);
        }

        var amount = posting.HeaderAmounts["Amount"];
        if (voucher is not { AutoPosted: true } || voucher.VoucherDate != stock.TxnDateTime
            || voucher.Lines.Sum(line => line.Debit) != amount || voucher.Lines.Sum(line => line.Credit) != amount)
            return StockMoveAttempt.Failed("DISPATCH-400");

        return new(IntegrationEventStatus.Success, posting.EventType, null);
    }

    private async Task<bool> StockTenantIsActiveAsync(Guid tenant, CancellationToken ct)
    {
        var state = await Db.Sys_Tenants.AsNoTracking().Where(row => row.Id == tenant)
            .Select(row => new { row.Enable, row.ExpireDate }).SingleOrDefaultAsync(ct);
        return state is { Enable: true } && (state.ExpireDate is null || state.ExpireDate > DateTime.Now);
    }

    private static decimal RoundStockValue(decimal value, int scale) => Math.Round(value, scale, MidpointRounding.AwayFromZero);

    private sealed record StockMovePayload(int? Version, Guid? StockTransactionId, string? TxnNo, string? TxnType,
        string? RelatedType, decimal? Qty, decimal? UnitPrice);

    private sealed record StockMoveAttempt(string Status, string? EventType, string? Error)
    {
        internal static StockMoveAttempt Failed(string? error) => new(IntegrationEventStatus.Failed, null, error);
    }
}
