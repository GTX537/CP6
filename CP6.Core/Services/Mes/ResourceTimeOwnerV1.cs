using System.Globalization;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Erp.UomProfileValuesV1;
namespace CP6.Core.Services.Mes;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceTimeRequestV1(ResourceSourceRefV1 SourceRef, string Field, UomProfileRefV1 TargetRef, UomTimeBasisRefV1? ExpectedBasis = null);
public sealed record ResourceTimeResultV1(ResourceSourceRefV1 SourceRef, string Field, string NativeBasis, string? NativeQuantity,
    UomProfileRefV1 TargetRef, UomTimeBasisRefV1? TimeBasisRef, string State, string? ConvertedQuantity, string[] Reasons,
    string Observation = "CURRENT_TRANSACTION_ONLY", string Qualification = "NOT_ATTESTED");

public sealed class ResourceTimeOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    public Task<ResourceTimeResultV1> ReadAsync(string kind, Guid id, ResourceTimeRequestV1 input, CancellationToken ct = default)
    {
        if (!owner.Owns(db)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (input == null) throw Error("RESOURCE_TIME_INPUT_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input); Reference(frozen.TargetRef);
        if (id == Guid.Empty || frozen.SourceRef == null || frozen.SourceRef.Id != id || frozen.SourceRef.Kind != kind)
            throw Error("RESOURCE_TIME_INPUT_INVALID", 400);
        var basis = (kind, frozen.Field) switch
        { ("MACHINE", "standardCycleSeconds") => "SECOND", ("MACHINE", "plannedRunMinutesPerDay") => "MINUTE",
          ("WORK_CENTER", "dailyCapacityHours") => "HOUR", _ => throw Error("RESOURCE_TIME_FIELD_INVALID", 400) };
        return owner.ExecuteAsync(ResourceSourceStoreV1.Resource(kind), "query", false, async (a, token) =>
        {
            var sources = new ResourceSourceStoreV1(db); await sources.Fence(false, token);
            var source = await sources.Read(a, kind, id, null, token);
            if (source.Reference != frozen.SourceRef) throw Error("RESOURCE_SOURCE_CHANGED", 412);
            a.AtCommit(last => a.RequireResourceSource(db, kind, id, last));
            var units = new UomProfileStoreV1(db); await units.Fence(false, token);
            var reader = await a.SourceAsync(OperationsOwnerV1.UomProfile, token);
            var target = await UomProfileOwnerV1.CurrentExact(reader, units, frozen.TargetRef, token);
            var baseRef = await UomProfileOwnerV1.CurrentBase(reader, units, target, token);
            var baseline = await UomProfileOwnerV1.CurrentExact(reader, units, baseRef, token);
            var store = new UomTimeBasisStoreV1(db); await store.Fence(false, token);
            var raw = source.Source.GetProperty("capability").GetProperty(frozen.Field);
            var quantity = raw.ValueKind == System.Text.Json.JsonValueKind.Null ? null : raw.ValueKind == System.Text.Json.JsonValueKind.Number
                ? raw.GetInt32().ToString(CultureInfo.InvariantCulture) : raw.GetString();
            UomTimeBasisVersionV1? meaning = null;
            ResourceTimeResultV1 Unknown(string reason) => new(source.Reference, frozen.Field, basis, quantity, target.Reference, meaning?.Reference, "UNKNOWN", null, [reason]);
            try { meaning = await UomTimeBasisOwnerV1.Current(reader, units, store, baseline, frozen.ExpectedBasis, token); }
            catch (OrderPriceCorrectionException e) when (e.Message is "TIME_BASIS_REQUIRED" or "TIME_BASIS_SOURCE_CHANGED" or "TIME_BASIS_DISABLED")
            { return Unknown(e.Message); }
            if (!source.SourceEnabled) return Unknown("RESOURCE_SOURCE_DISABLED");
            if (quantity == null) return Unknown("RESOURCE_TIME_VALUE_MISSING");
            // Native column meanings are fixed by their original schema; the target's physical meaning is explicitly declared by its own Owner.
            // These are arithmetic operands, never a stored/synthetic authoritative UOM profile.
            var from = target.Profile with { QuantityScale = 8, PackMultiple = null, Numerator = UomTimeBasisOwnerV1.Seconds(basis),
                Denominator = UomTimeBasisOwnerV1.Seconds(meaning.Definition.Basis) };
            var converted = ConvertExact(quantity, from, target.Profile);
            return new ResourceTimeResultV1(source.Reference, frozen.Field, basis, quantity, target.Reference, meaning.Reference, "KNOWN", converted, []);
        }, ct);
    }
}
