using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

internal static class OutboundFulfilmentV1
{
    internal const string ShipmentOwner = "Main.Wms.ShipmentFulfilment.v1", LotOwner = "Main.Wms.ShipmentLot.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal sealed record PreparedLine(Guid Id, Guid OrderLineId, string ProductCode, string UnitCode, string Quantity,
        FulfilmentRefV1 Product, UomProfileRefV1 UnitProfile, UomProfileRefV1 BaseProfile, FulfilmentQtyV1 BaseQty);
    internal sealed record Prepared(DateTimeOffset At, OrderFulfilmentSourceV1 Order, IReadOnlyList<PreparedLine> Lines);
    private static OrderPriceCorrectionException Error(string code, int status = 422) => new(code, status);

    // Only the shipping decision validates mutable source conditions. Readers trust this stored contract.
    internal static async Task<Prepared> Prepare(CP6Context db, OperationsAccessV1 access, OutboundOrder outbound, CancellationToken ct)
    {
        if (outbound.OutboundType != OutboundType.Shipping || string.IsNullOrWhiteSpace(outbound.WebOrderNo))
            throw Error("FULFILMENT_ORDER_SOURCE_REQUIRED");
        var at = DateTimeOffset.UtcNow;
        var order = await OrderFulfilmentSourceReaderV1.Read(db, outbound.WebOrderNo, at, ct);
        if (string.IsNullOrWhiteSpace(outbound.CustomerCd) || order.CustomerCode != outbound.CustomerCd)
            throw Error("FULFILMENT_CUSTOMER_CHANGED", 409);
        var lineage = await OutboundOrderLineageV1.Read(db, outbound.OutboundNo, ct);
        var rows = await db.OutboundOrderDetails.AsNoTracking().Where(x => x.OutboundNo == outbound.OutboundNo
            && !x.IsDeleted && x.ShippedQty < x.AllocatedQty).OrderBy(x => x.LineNo).ToArrayAsync(ct);
        var products = await access.SourceAsync(OperationsOwnerV1.Product, ct);
        var units = await access.SourceAsync(OperationsOwnerV1.UomProfile, ct);
        var store = new UomProfileStoreV1(db); await store.Fence(false, ct);
        var result = new List<PreparedLine>();
        foreach (var line in rows)
        {
            if (!lineage.TryGetValue(line.Id, out var origin)) throw Error("FULFILMENT_ORDER_LINE_REQUIRED");
            var source = order.Lines.SingleOrDefault(x => x.Id == origin.OrderLineId);
            if (origin.OrderId != order.Id || source == null || source.ProductCode != line.ProductCd || source.UnitCode != line.UnitCd)
                throw Error("FULFILMENT_ORDER_LINE_CHANGED", 409);
            var productId = await products.Products().Where(x => x.ProductCd == line.ProductCd).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
                ?? throw Error("FULFILMENT_PRODUCT_UNAVAILABLE", 403);
            var product = await CommercialProductIdentityReader.ReadOwnedAsync(db, products, productId, null, ct);
            var binding = (await store.ReadProduct(productId, null, ct)).Value;
            if (product.Version != binding.ProductVersion || product.Digest != binding.ProductDigest
                || product.Source.GetProperty("isDeleted").GetBoolean() || product.Source.GetProperty("qtyUnitCode").GetString() != binding.UnitCode
                || binding.UnitCode != line.UnitCd)
                throw Error("PRODUCT_UNIT_BINDING_CHANGED", 412);
            var profile = await UomProfileOwnerV1.CurrentExact(units, store, binding.ProfileRef, ct);
            var baseline = profile.Profile.Mode == "BASE" ? profile : await UomProfileOwnerV1.CurrentExact(units, store, profile.Profile.BaseRef!, ct);
            // Unit-store writes already guarantee base mode and dimension for an immutable profile.
            var stock = await db.Stocks.AsNoTracking().SingleAsync(x => !x.IsDeleted && x.WarehouseCd == (line.WarehouseCd ?? outbound.WarehouseCd)
                && x.LocationCd == (line.LocationCd ?? "") && x.ProductCd == line.ProductCd && x.LotNo == (line.LotNo ?? ""), ct);
            if (stock.UnitCd != line.UnitCd) throw Error("FULFILMENT_STOCK_UNIT_MISMATCH", 409);
            var quantity = (line.AllocatedQty - line.ShippedQty).ToString(CultureInfo.InvariantCulture);
            if (quantity.Contains('.')) quantity = quantity.TrimEnd('0').TrimEnd('.');
            var value = UomProfileValuesV1.ConvertExact(quantity, profile.Profile, baseline.Profile);
            if (value.Contains('.')) value = value.TrimEnd('0').TrimEnd('.');
            if (value == "0" || !Regex.IsMatch(value, @"\A(0|[1-9][0-9]{0,12})(\.[0-9]{1,8})?\z", RegexOptions.CultureInvariant))
                throw Error("FULFILMENT_QUANTITY_NOT_REPRESENTABLE");
            result.Add(new(line.Id, source.Id, line.ProductCd, line.UnitCd!, quantity,
                new(EngineeringProductValuesV1.BusinessOwner, productId.ToString(), product.Version, product.Digest),
                profile.Reference, baseline.Reference, new(value, new(baseline.Profile.Code, baseline.Reference.Version))));
        }
        var sourceLines = result.Select(x => x.OrderLineId).ToHashSet();
        return new(at, order with { Lines = order.Lines.Where(x => sourceLines.Contains(x.Id)).ToArray() }, result);
    }

    private static FulfilmentFactV1 Fact<T>(string owner, string id, string version, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        return new(new(owner, id, version, MainOwnerOperationJournalV1.Digest(bytes)), Encoding.UTF8.GetString(bytes));
    }

    // Called after the actual stock/package writes, before the same Owner transaction commits.
    internal static async Task Capture(CP6Context db, OutboundOrder outbound, Prepared prepared, string? package, CancellationToken ct)
    {
        var version = Guid.NewGuid().ToString("N");
        var rows = await db.OutboundOrderDetails.AsNoTracking().Where(x => x.OutboundNo == outbound.OutboundNo && !x.IsDeleted).ToDictionaryAsync(x => x.Id, ct);
        var order = Fact(OrderFulfilmentSourceReaderV1.Owner, prepared.Order.Id.ToString(), version, prepared.Order);
        var shipment = Fact(ShipmentOwner, outbound.Id.ToString(), version, new
        {
            outbound.Id, outbound.OutboundNo, outbound.CustomerCd, PackageNumber = package, CapturedAt = prepared.At,
            Lines = prepared.Lines.Select(x => new { x.Id, x.OrderLineId, x.Product, x.Quantity, x.UnitCode, x.UnitProfile,
                x.BaseProfile, x.BaseQty, rows[x.Id].ShipTxnNo, WarehouseCode = rows[x.Id].WarehouseCd ?? outbound.WarehouseCd,
                rows[x.Id].LocationCd, rows[x.Id].LotNo }).ToArray()
        });
        var facts = new List<FulfilmentFactV1> { order, shipment }; var lines = new List<OriginalFulfilmentV1>();
        foreach (var line in prepared.Lines)
        {
            var row = rows[line.Id]; var lots = new List<FulfilmentRefV1>();
            if (!string.IsNullOrEmpty(row.LotNo))
            {
                // This identifies the actual shipment lot occurrence, not a global lot master or eligibility approval.
                var lot = Fact(LotOwner, row.Id.ToString(), version, new { Shipment = shipment.Reference,
                    ShipmentLineId = row.Id, line.Product, row.LotNo, row.ShipTxnNo });
                facts.Add(lot); lots.Add(lot.Reference);
            }
            lines.Add(new(shipment.Reference, line.Id.ToString(), version, order.Reference, line.OrderLineId.ToString(),
                null, null, null, null, line.Product, [new("0", line.BaseQty.Value)], line.BaseQty, lots, [], []));
        }
        var snapshot = new OutboundFulfilmentSnapshotV1(outbound.Id, outbound.OutboundNo, outbound.CustomerCd!, prepared.At,
            lines, facts, "PARTIAL", ["SCHEDULE", "DELIVERY_PROOF", "INVOICE_LINE", "COMMERCIAL_SNAPSHOT", "SERIAL", "LPN"]);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_outbound_fulfilment_v1.snapshots(tenant_id,outbound_id,payload)
            VALUES({db.CurrentTenantId},{outbound.Id},CAST({JsonSerializer.Serialize(snapshot)} AS jsonb))
            """, ct);
    }

    internal static async Task<OutboundFulfilmentSnapshotV1> Read(CP6Context db, Guid outboundId, CancellationToken ct)
    {
        var payload = await db.Database.SqlQuery<string>($"""
            SELECT payload::text AS "Value" FROM cp6_outbound_fulfilment_v1.snapshots
            WHERE tenant_id={db.CurrentTenantId} AND outbound_id={outboundId}
            """).SingleOrDefaultAsync(ct) ?? throw Error("FULFILMENT_NOT_CAPTURED", 404);
        return JsonSerializer.Deserialize<OutboundFulfilmentSnapshotV1>(payload)!;
    }
}
