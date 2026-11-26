using System.Data;
using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Erp;

public sealed record LegacyOrderSourceLine(Guid Id, int Number, string? NativeVersion, string ProductCode,
    string? DeliveryCode, DateTime? CustomerDeliveryDate, string? SalesAvailable, decimal? Quantity,
    string? QuantityUnit, decimal? ShippedQuantity, decimal? BackorderQuantity, bool Deleted, SalesReturnTermsV1 Terms);

public sealed record LegacyOrderSourceSnapshot(Guid Id, string Number, string? NativeVersion, string CustomerCode,
    string OrderType, string? Department, DateTime? OrderDate, DateTime? CustomerDeliveryDate, string? ShipDateTime,
    string Lifecycle, DateTime? CancelledAt, bool Deleted, IReadOnlyList<LegacyOrderSourceLine> Lines);

internal static class OrderSourceFacts
{
    internal sealed class Original
    {
        public long Revision { get; init; }
        public string Writer { get; init; } = "";
        public byte[] Origin { get; init; } = [];
        public byte[] Body { get; init; } = [];
        public string Digest { get; init; } = "";

        internal LegacyOrderSourceSnapshot Read() =>
            Digest == MainOwnerOperationJournalV1.Digest(Body)
                ? JsonSerializer.Deserialize<LegacyOrderSourceSnapshot>(Body)!
                : throw Error("SALES_SOURCE_ORIGINAL_UNAVAILABLE", 503);
    }

    internal static OrderPriceCorrectionException Error(string code, int status) => new(code, status);

    internal static void RequireTransaction(CP6Context db, DbTransaction? caller = null)
    {
        if (!db.Database.IsNpgsql() || db.CurrentTenantId == Guid.Empty
            || db.Database.CurrentTransaction?.GetDbTransaction() is not NpgsqlTransaction current
            || !ReferenceEquals(current.Connection, db.Database.GetDbConnection())
            || caller is not null && !ReferenceEquals(current, caller)
            || current.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable))
            throw Error("SALES_SOURCE_TRANSACTION_REQUIRED", 503);
    }

    // 先锁原订单根，再锁明细，与旧订单 Owner 的锁顺序一致。
    internal static async Task LockNative(CP6Context db, Guid orderId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM public."T_Order"
            WHERE "TenantId"={db.CurrentTenantId} AND "Id"={orderId} FOR UPDATE
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT line."Id" FROM public."T_OrderDetail" line JOIN public."T_Order" ord
              ON ord."TenantId"=line."TenantId" AND ord."WebOrderNo"=line."WebOrderNo"
            WHERE ord."TenantId"={db.CurrentTenantId} AND ord."Id"={orderId}
            ORDER BY line."Id" FOR UPDATE OF line
            """, ct);
    }

    internal static async Task<LegacyOrderSourceSnapshot?> ReadNative(CP6Context db, Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return null;
        var lines = await db.OrderDetails.AsNoTracking().Where(d => d.WebOrderNo == order.WebOrderNo)
            .OrderBy(d => d.WebOrderDetailNo).ThenBy(d => d.Id).ToArrayAsync(ct);
        return new(order.Id, order.WebOrderNo, Token(order.RowVersion), order.CustomerCd, order.OrderType,
            order.OrderDepartment, order.OrderDate, order.CustomerDeliveryDate, order.ShipDateTime,
            order.OrderStatus, order.CancelledAt, order.IsDeleted,
            lines.Select(line => new LegacyOrderSourceLine(line.Id, line.WebOrderDetailNo, Token(line.RowVersion),
                line.ProductCd, line.DeliveryCd, line.CustomerDeliveryDate, line.SalesAvailable, line.Quantity,
                line.QtyUnit, line.ShippedQty, line.BackorderQty, line.IsDeleted,
                SalesReturnSourceReaderV1.Terms(order, line))).ToArray());
    }

    private static string? Token(byte[]? value) => value is { Length: > 0 } ? Convert.ToBase64String(value) : null;

    internal static Task<Original?> ReadOriginal(CP6Context db, Guid orderId, long? revision, CancellationToken ct) =>
        db.Database.SqlQuery<Original>($"""
            SELECT revision AS "Revision",writer AS "Writer",origin AS "Origin",body AS "Body",digest AS "Digest"
            FROM cp6_sales_order_source_v1.legacy_originals
            WHERE tenant_id={db.CurrentTenantId} AND order_id={orderId}
              AND (CAST({revision} AS bigint) IS NULL OR revision={revision})
            ORDER BY revision DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);

    internal static async Task<long> Capture(CP6Context db, string number, string writer, CancellationToken ct,
        object? origin = null)
    {
        RequireTransaction(db);
        var id = await db.Orders.AsNoTracking().Where(o => o.WebOrderNo == number).Select(o => o.Id).SingleAsync(ct);
        await LockNative(db, id, ct);
        var snapshot = await ReadNative(db, id, ct) ?? throw Error("SALES_SOURCE_ORIGINAL_UNAVAILABLE", 503);
        var body = MainOwnerOperationJournalV1.Encode(snapshot);
        var digest = MainOwnerOperationJournalV1.Digest(body);
        var prior = await ReadOriginal(db, id, null, ct);
        if (prior?.Digest == digest) return prior.Revision;

        var revision = checked((prior?.Revision ?? 0) + 1);
        var originBytes = MainOwnerOperationJournalV1.Encode(origin);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_sales_order_source_v1.legacy_originals(tenant_id, order_id, revision, writer, origin, body, digest)
            VALUES({db.CurrentTenantId}, {id}, {revision}, {writer}, {originBytes}, {body}, {digest})
            """, ct);
        return revision;
    }

    // 价格更正只变化商业条款。源门比较供给身份和状态，但不把价格写成新的数量或交期。
    internal static string SupplyDigest(LegacyOrderSourceSnapshot order, LegacyOrderSourceLine line) =>
        MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(new
        {
            order.Id, order.CustomerCode, order.OrderType, order.Department, order.OrderDate,
            order.CustomerDeliveryDate, order.ShipDateTime, order.Lifecycle, order.CancelledAt, order.Deleted,
            Line = new { line.Id, line.ProductCode, line.DeliveryCode, line.CustomerDeliveryDate, line.SalesAvailable,
                line.Quantity, line.QuantityUnit, line.ShippedQuantity, line.BackorderQuantity, line.Deleted }
        }));
}
