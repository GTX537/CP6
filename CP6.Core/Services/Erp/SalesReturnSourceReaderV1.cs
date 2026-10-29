using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed record SalesReturnTermsV1(string CurrencyCode, string FxRate, string? HeaderSalesPriceDivision,
    string? Quantity, string? QuantityUnit, string? PriceUnit, string? SetUnitPrice, string? IndividualUnitPrice,
    string? Amount, string? SpecialPriceFlag, bool ProvisionalPrice, string? LineSalesPriceDivision);
public sealed record SalesReturnOriginalV1(FulfilmentRefV1 ShipmentRef, string ShipmentLineId, FulfilmentRefV1 OrderRef,
    string OrderLineId, string OrderNumber, int OrderLineNumber, string ProductCode, FulfilmentRefV1 CustomerRef,
    DateTimeOffset CapturedAt, SalesReturnTermsV1 Terms);
public sealed record SalesReturnCurrentV1(string CustomerCode, string ProductCode, bool OrderDeleted, bool LineDeleted,
    DateTime? CancelledAt, SalesReturnTermsV1 Terms);
public sealed record SalesReturnSourceV1(FulfilmentRefV1 Reference, SalesReturnOriginalV1 Original,
    SalesReturnCurrentV1? Current, string CurrentState);

/// <summary>Exact commercial provenance. This source does not approve return eligibility or credit.</summary>
public sealed class SalesReturnSourceReaderV1(CP6Context db, OperationsOwnerV1 owner)
{
    internal const string Owner = "Main.Erp.SalesReturnSource.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string? Exact(decimal? value) => value?.ToString("0.########", CultureInfo.InvariantCulture);
    internal static SalesReturnTermsV1 Terms(Order header, OrderDetail line) => new(header.CurrencyCd,
        Exact(header.FxRate)!, header.SalesPriceDiv, Exact(line.Quantity), line.QtyUnit, line.UnitPriceUnit,
        Exact(line.SetUnitPrice), Exact(line.IndividualUnitPrice), Exact(line.Amount), line.SpecialPriceFlg,
        line.ProvisionalPriceFlg, line.SalesPriceDiv);

    internal sealed record Prepared(int Number, SalesReturnTermsV1 Terms);
    internal static async Task<Dictionary<Guid, Prepared>> Prepare(CP6Context db, Guid orderId, IEnumerable<Guid> lineIds, CancellationToken ct)
    {
        var header = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId, ct);
        var ids = lineIds.ToArray();
        var lines = await db.OrderDetails.AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync(ct);
        return lines.ToDictionary(x => x.Id, x => new Prepared(x.WebOrderDetailNo, Terms(header, x)));
    }
    // Enlisted by the actual shipping Owner; bytes and reference are written once in that transaction.
    internal static async Task<FulfilmentRefV1> Capture(CP6Context db, Guid outboundId, Guid lineId, string version,
        SalesReturnOriginalV1 original, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(original, Json);
        var reference = new FulfilmentRefV1(Owner, lineId.ToString(), version, MainOwnerOperationJournalV1.Digest(body));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_sales_return_source_v1.snapshots(tenant_id,outbound_id,line_id,version,digest,body)
            VALUES({db.CurrentTenantId},{outboundId},{lineId},{version},{reference.Digest},{body})
            """, ct);
        return reference;
    }
    private sealed class Stored
    {
        public string Version { get; init; } = null!;
        public string Digest { get; init; } = null!;
        public byte[] Body { get; init; } = null!;
    }
    public Task<SalesReturnSourceV1> ReadAsync(Guid outboundId, Guid shipmentLineId, CancellationToken ct = default) =>
        owner.ExecuteAsync(OperationsOwnerV1.Rma, "query", false, async (access, token) =>
        {
            if (!await access.Outbounds().AnyAsync(x => x.Id == outboundId, token))
                throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
            var stored = await db.Database.SqlQuery<Stored>($"""
                SELECT version AS "Version", digest AS "Digest", body AS "Body"
                FROM cp6_sales_return_source_v1.snapshots
                WHERE tenant_id={db.CurrentTenantId} AND outbound_id={outboundId} AND line_id={shipmentLineId}
                """).SingleOrDefaultAsync(token) ?? throw new OrderPriceCorrectionException("SALES_RETURN_SOURCE_NOT_CAPTURED", 404);
            var original = JsonSerializer.Deserialize<SalesReturnOriginalV1>(stored.Body, Json)!;
            var current = await access.ReadHistoricalOrderLine(Guid.Parse(original.OrderRef.Id), Guid.Parse(original.OrderLineId), token);
            return new SalesReturnSourceV1(new(Owner, shipmentLineId.ToString(), stored.Version, stored.Digest), original,
                current, current == null ? "LINE_MISSING" : current.Terms == original.Terms ? "TERMS_UNCHANGED" : "TERMS_CHANGED");
        }, ct);
}
