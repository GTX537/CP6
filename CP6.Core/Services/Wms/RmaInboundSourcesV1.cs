using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public sealed record RmaRootRangeMapV1(string RmaFrom, string RmaTo, Guid RootId, string RootFrom, string RootTo);
public sealed record RmaInboundSourceFactsV1(FulfilmentRefV1 Reference, ReturnStockDimensionsV1 Dimensions,
    string ProductCode, string LotCode, string OwnerType, string? OwnerCode, ReturnStockReceiptRefV1? OriginalMovementRef,
    RmaRootRangeMapV1[] RootMap, ReturnStockSpanV1[] PriorReturnedSpans, JsonElement? SourcePlace,
    FulfilmentRefV1? NewSourceReasonRef, FulfilmentRefV1 QualityBasisRef, bool Simulation);
public interface IRmaInboundSourcesV1
{
    Task<RmaInboundSourceFactsV1> ReadAsync(Guid tenant, Guid site, RmaPhysicalLineV1 line, CancellationToken ct);
}
public sealed class RmaInboundSimulationOptionsV1
{
    public bool Enabled { get; set; }
    public string CasesJson { get; set; } = "[]";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaInboundSimulationCaseV1([property: JsonRequired] Guid TenantId,
    [property: JsonRequired] FulfilmentRefV1 ShipmentRef, [property: JsonRequired] string ShipmentLineId,
    [property: JsonRequired] string Version, [property: JsonRequired] ReturnStockDimensionsV1 Dimensions,
    [property: JsonRequired] RmaRootRangeMapV1[] RootMap, [property: JsonRequired] ReturnStockSpanV1[] PriorReturnedSpans,
    [property: JsonRequired] JsonElement? SourcePlace, [property: JsonRequired] FulfilmentRefV1? NewSourceReasonRef,
    [property: JsonRequired] FulfilmentRefV1 QualityBasisRef);

// Only absent external root/identity/quality source facts are simulated. Native shipment/movement facts are read from their actual Owners.
public sealed class RmaSimulatedInboundSourcesV1 : IRmaInboundSourcesV1
{
    private readonly CP6Context db;
    private readonly Dictionary<(Guid Tenant,string Shipment,string Line),RmaInboundSimulationCaseV1> cases = [];
    public RmaSimulatedInboundSourcesV1(CP6Context context, IOptions<RmaInboundSimulationOptionsV1> options)
    {
        db = context;
        if (!options.Value.Enabled) return;
        var values = JsonSerializer.Deserialize<RmaInboundSimulationCaseV1[]>(options.Value.CasesJson,RmaFormalStoreV1.Json)
            ?? throw new InvalidOperationException("RMA_INBOUND_SCENARIOS_REQUIRED");
        foreach (var c in values)
        {
            if (c.TenantId == Guid.Empty || c.ShipmentRef == null || string.IsNullOrWhiteSpace(c.ShipmentLineId)
                || string.IsNullOrWhiteSpace(c.Version) || c.Dimensions == null || c.RootMap == null || c.PriorReturnedSpans == null || c.QualityBasisRef == null)
                throw new InvalidOperationException("RMA_INBOUND_SCENARIO_INVALID");
            var dims = c.Dimensions;
            if (dims.SiteId == Guid.Empty || dims.ProductRef == null || dims.BaseUomRef == null || dims.PrecisionPolicyRef == null
                || dims.OriginPurposeRef == null || dims.TechnicalIdentity == null || dims.Owner?.Kind is not ("SELF" or "CUSTOMER")
                || dims.Owner.Reference == null || dims.Lot?.Kind is not ("TRACKED" or "NOT_TRACKED" or "UNKNOWN")
                || dims.TechnicalIdentity.Kind is not ("KNOWN" or "NOT_APPLICABLE" or "UNKNOWN")
                || dims.TechnicalIdentity.Kind == "KNOWN" && dims.TechnicalIdentity.RevisionRef == null)
                throw new InvalidOperationException("RMA_INBOUND_DIMENSIONS_INCOMPLETE");
            if (c.RootMap.Length == 0 ? c.SourcePlace != null || c.NewSourceReasonRef == null || c.PriorReturnedSpans.Length != 0
                : c.SourcePlace == null || c.NewSourceReasonRef != null) throw new InvalidOperationException("RMA_INBOUND_ROUTE_AMBIGUOUS");
            if (c.SourcePlace is { } place && (place.GetProperty("kind").GetString() != "EXTERNAL" || !place.TryGetProperty("custodyReceiptRef",out _)))
                throw new InvalidOperationException("RMA_INBOUND_EXTERNAL_CUSTODY_REQUIRED");
            if (c.RootMap.Length > 0)
            {
                _ = Ranges.Input(c.RootMap.Select(x => new RmaDeliveryRangeV1(x.RmaFrom,x.RmaTo)).ToArray(),limit:2000);
                foreach (var m in c.RootMap)
                    if (m.RootId == Guid.Empty || Ranges.Input(m.RmaTo)-Ranges.Input(m.RmaFrom) != Ranges.Input(m.RootTo)-Ranges.Input(m.RootFrom))
                        throw new InvalidOperationException("RMA_INBOUND_RANGE_MAP_INVALID");
                foreach (var group in c.RootMap.GroupBy(x => x.RootId)) _ = Ranges.Input(group.Select(x => new RmaDeliveryRangeV1(x.RootFrom,x.RootTo)).ToArray(),limit:2000);
                foreach (var group in c.PriorReturnedSpans.GroupBy(x => x.RootId))
                {
                    var previous = Ranges.Input(group.Select(x => new RmaDeliveryRangeV1(x.From,x.To)).ToArray(),limit:2000);
                    if (!Ranges.Subset(previous,c.RootMap.Where(x => x.RootId == group.Key).Select(x => new Ranges.Segment(Ranges.Input(x.RootFrom),Ranges.Input(x.RootTo)))))
                        throw new InvalidOperationException("RMA_INBOUND_HISTORY_INCONSISTENT");
                }
            }
            if (!cases.TryAdd((c.TenantId,c.ShipmentRef.Id,c.ShipmentLineId),c)) throw new InvalidOperationException("RMA_INBOUND_SCENARIO_DUPLICATE");
        }
    }
    public async Task<RmaInboundSourceFactsV1> ReadAsync(Guid tenant, Guid site, RmaPhysicalLineV1 line, CancellationToken ct)
    {
        var original = line.Share.Original;
        if (tenant != db.CurrentTenantId || !cases.TryGetValue((tenant,original.ShipmentRef.Id,original.ShipmentLineId),out var c))
            throw Error("RMA_INBOUND_SIMULATION_NOT_CONFIGURED",503);
        if (c.ShipmentRef != original.ShipmentRef || c.Dimensions.ProductRef != original.ProductRef || c.Dimensions.SiteId != site
            || c.Dimensions.BaseUomRef != new ReturnStockCodeV1(original.BaseQty.Uom.Code,original.BaseQty.Uom.Version)) throw Error("RMA_INBOUND_SOURCE_MISMATCH",409);
        var snapshot = await OutboundFulfilmentV1.Read(db,Guid.Parse(original.ShipmentRef.Id),ct);
        var fact = snapshot.Facts.Single(x => x.Reference == original.ShipmentRef);
        using var body = JsonDocument.Parse(fact.OriginalJson);
        var source = body.RootElement.GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == Guid.Parse(original.ShipmentLineId));
        var number = source.GetProperty("shipTxnNo").GetString();
        var movement = await db.StockTransactions.AsNoTracking().SingleAsync(x => x.TxnNo == number && !x.IsDeleted,ct);
        if (c.Dimensions.Owner.Kind != movement.OwnerType || c.Dimensions.Lot.Kind == "TRACKED" && c.Dimensions.Lot.LotCode != movement.LotNo
            || c.Dimensions.Lot.Kind == "NOT_TRACKED" && movement.LotNo != "")
            throw Error("RMA_INBOUND_DIMENSIONS_MISMATCH",409);
        if (line.Input.SerialRefs.Length != (c.Dimensions.SerialRef == null ? 0 : 1)
            || c.Dimensions.SerialRef != null && line.Input.SerialRefs[0] != c.Dimensions.SerialRef
            || line.Input.LpnRefs.Length != (c.Dimensions.HandlingUnitRef == null ? 0 : 1)
            || c.Dimensions.HandlingUnitRef != null && line.Input.LpnRefs[0] != c.Dimensions.HandlingUnitRef) throw Error("RMA_INBOUND_TRACKING_MISMATCH",409);
        var reference = new FulfilmentRefV1("Simulation.Wms.RmaInboundSources.v1",Digest(new { tenant,c.ShipmentRef.Id,c.ShipmentLineId }),c.Version,Digest(c));
        return new(reference,c.Dimensions,movement.ProductCd,movement.LotNo,movement.OwnerType,movement.OwnerCd,
            c.RootMap.Length == 0 ? null : new("Main.Wms.StockMovement.v1",movement.Id.ToString(),Convert.ToBase64String(movement.RowVersion)),
            c.RootMap,c.PriorReturnedSpans,c.SourcePlace,c.NewSourceReasonRef,c.QualityBasisRef,true);
    }
}
