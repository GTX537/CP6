using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

// The actual native order row at generation time; not a formal fulfilment or commercial reference.
public sealed record OutboundOrderLineSnapshotV1(Guid SnapshotId, Guid OrderId, string OrderNumber,
    Guid OrderLineId, int OrderLineNumber, string ProductCode, string? Quantity, string? QuantityUnit,
    DateTimeOffset CapturedAt);

internal static class OutboundOrderLineageV1
{
    private sealed class Stored
    {
        public Guid LineId { get; init; }
        public string Payload { get; init; } = null!;
    }

    internal static async Task Capture(CP6Context db, Order order, IReadOnlyList<OrderDetail> source,
        IReadOnlyList<OutboundOrderDetail> generated)
    {
        var at = DateTimeOffset.UtcNow;
        // Generation supplies these paired rows inside the existing outbound Owner transaction.
        foreach (var (original, line) in source.Zip(generated))
        {
            var snapshot = new OutboundOrderLineSnapshotV1(Guid.NewGuid(), order.Id, order.WebOrderNo,
                original.Id, original.WebOrderDetailNo, original.ProductCd,
                original.Quantity?.ToString("0.########", CultureInfo.InvariantCulture), original.QtyUnit, at);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_outbound_lineage_v1.order_lines(tenant_id,outbound_line_id,outbound_no,payload)
                VALUES({db.CurrentTenantId},{line.Id},{line.OutboundNo},CAST({JsonSerializer.Serialize(snapshot)} AS jsonb))
                """);
        }
    }

    internal static async Task<Dictionary<Guid, OutboundOrderLineSnapshotV1>> Read(CP6Context db,
        string outboundNo, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<Stored>($"""
            SELECT outbound_line_id AS "LineId", payload::text AS "Payload"
            FROM cp6_outbound_lineage_v1.order_lines
            WHERE tenant_id={db.CurrentTenantId} AND outbound_no={outboundNo}
            """).ToArrayAsync(ct);
        return rows.ToDictionary(x => x.LineId, x => JsonSerializer.Deserialize<OutboundOrderLineSnapshotV1>(x.Payload)!);
    }
}
