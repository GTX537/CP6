using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using RangeSet = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

/// <summary>Source facts only; implementations never approve a return or create an RMA claim.</summary>
public interface IRmaReturnSourcesV1
{
    Task<RmaReturnSourceCutV1> ReadAsync(Guid tenant, OriginalFulfilmentV1 original, CancellationToken ct);
}
public sealed record RmaReturnSourceCutV1(RmaSourceCoordinateV1 Coordinate, OriginalFulfilmentV1 Original,
    DateTimeOffset DeliveredAt, FulfilmentRangeV1[] HistoricalOccupiedRanges, FulfilmentRangeV1[] HistoricalReceivedRanges,
    FulfilmentRangeV1[] CreditedRanges, string EvidenceMode);
public sealed class RmaReturnSimulationOptionsV1
{
    public bool Enabled { get; set; }
    public string CasesJson { get; set; } = "[]";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RmaReturnSimulationCaseV1([property: JsonRequired] Guid TenantId,
    [property: JsonRequired] FulfilmentRefV1 ShipmentRef, [property: JsonRequired] string ShipmentLineId,
    [property: JsonRequired] string CutVersion, [property: JsonRequired] DateTimeOffset DeliveredAt,
    [property: JsonRequired] FulfilmentRefV1 DeliveryProofRef, [property: JsonRequired] FulfilmentRefV1? InvoiceLineRef,
    [property: JsonRequired] FulfilmentRefV1? ScheduleRef, [property: JsonRequired] RmaDeliveryRangeV1[] EligibleRanges,
    [property: JsonRequired] RmaDeliveryRangeV1[] HistoricalOccupiedRanges,
    [property: JsonRequired] RmaDeliveryRangeV1[] HistoricalReceivedRanges, [property: JsonRequired] RmaDeliveryRangeV1[] CreditedRanges);

/// <summary>Explicit, complete acceptance scenarios. No automatic case, implicit zero history or real-data claim.</summary>
public sealed class RmaSimulatedReturnSourcesV1 : IRmaReturnSourcesV1
{
    private sealed record Prepared(RmaReturnSimulationCaseV1 Case, RangeSet.Segment[] Eligible, RangeSet.Segment[] Occupied,
        RangeSet.Segment[] Received, RangeSet.Segment[] Credited, FulfilmentRefV1 CutRef);
    private readonly Dictionary<(Guid Tenant, string Owner, string Shipment, string Line), Prepared> cases = new();
    private readonly bool enabled;
    public RmaSimulatedReturnSourcesV1(IOptions<RmaReturnSimulationOptionsV1> options)
    {
        enabled = options.Value.Enabled; if (!enabled) return;
        var values = JsonSerializer.Deserialize<RmaReturnSimulationCaseV1[]>(options.Value.CasesJson, RmaFormalStoreV1.Json)
            ?? throw new InvalidOperationException("RMA_SIMULATION_CASES_REQUIRED");
        foreach (var c in values)
        {
            Reference(c.ShipmentRef); Reference(c.DeliveryProofRef);
            if (c.InvoiceLineRef != null) Reference(c.InvoiceLineRef); if (c.ScheduleRef != null) Reference(c.ScheduleRef);
            if (c.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(c.ShipmentLineId) || string.IsNullOrWhiteSpace(c.CutVersion)
                || c.CutVersion.Length > 200 || c.DeliveredAt == default) throw new InvalidOperationException("RMA_SIMULATION_CASE_INVALID");
            var eligible = RangeSet.Input(c.EligibleRanges, limit: 200); var occupied = RangeSet.Input(c.HistoricalOccupiedRanges, true, 200);
            var received = RangeSet.Input(c.HistoricalReceivedRanges, true, 200); var credited = RangeSet.Input(c.CreditedRanges, true, 200);
            if (!RangeSet.Subset(occupied, eligible) || !RangeSet.Subset(received, occupied) || !RangeSet.Subset(credited, occupied))
                throw new InvalidOperationException("RMA_SIMULATION_HISTORY_INCONSISTENT");
            var identity = Digest(new { c.TenantId, c.ShipmentRef.Owner, c.ShipmentRef.Id, c.ShipmentLineId });
            var reference = new FulfilmentRefV1("Simulation.Wms.ReturnSourceCut.v1", identity, c.CutVersion, Digest(c));
            if (!cases.TryAdd((c.TenantId,c.ShipmentRef.Owner,c.ShipmentRef.Id,c.ShipmentLineId),new(c,eligible,occupied,received,credited,reference)))
                throw new InvalidOperationException("RMA_SIMULATION_CASE_DUPLICATE");
        }
    }
    private static void Reference(FulfilmentRefV1 reference)
    {
        if (reference == null || string.IsNullOrWhiteSpace(reference.Owner) || string.IsNullOrWhiteSpace(reference.Id)
            || string.IsNullOrWhiteSpace(reference.Version) || reference.Digest == null || reference.Digest.Length != 64
            || reference.Digest.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidOperationException("RMA_SIMULATION_REFERENCE_INVALID");
    }
    public Task<RmaReturnSourceCutV1> ReadAsync(Guid tenant, OriginalFulfilmentV1 original, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!enabled || !cases.TryGetValue((tenant,original.ShipmentRef.Owner,original.ShipmentRef.Id,original.ShipmentLineId),out var p))
            throw Error("RMA_SIMULATION_SOURCE_NOT_CONFIGURED", 503);
        if (p.Case.ShipmentRef != original.ShipmentRef) throw Error("RETURN_SOURCE_VERSION_STALE", 412);
        if (!RangeSet.Subset(p.Eligible, RangeSet.Read(original.SourceShareRanges))) throw Error("RETURN_SOURCE_OUTSIDE_SHIPMENT", 422);
        var line = new FulfilmentRefV1(original.ShipmentRef.Owner + ".Line", original.ShipmentLineId, original.ShipmentLineVersion, Digest(original));
        var enriched = original with { DeliveryProofRef = p.Case.DeliveryProofRef, InvoiceLineRef = p.Case.InvoiceLineRef, ScheduleRef = p.Case.ScheduleRef };
        var coordinate = new RmaSourceCoordinateV1(original.ShipmentRef.Owner, line, p.Case.InvoiceLineRef, p.Case.ScheduleRef,
            original.BaseQty.Uom, p.CutRef, original.ShipmentRef, RangeSet.Wire(p.Eligible), new(RangeSet.Text(RangeSet.Measure(p.Eligible)), original.BaseQty.Uom));
        return Task.FromResult(new RmaReturnSourceCutV1(coordinate,enriched,p.Case.DeliveredAt,RangeSet.Wire(p.Occupied),RangeSet.Wire(p.Received),RangeSet.Wire(p.Credited),"SIMULATED"));
    }
}
