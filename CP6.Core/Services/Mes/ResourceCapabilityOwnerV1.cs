using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using static CP6.Core.Services.Mes.ResourceSourceStoreV1;
using static CP6.Core.Services.Mes.ResourceCapabilityValuesV1;

namespace CP6.Core.Services.Mes;

/// <summary>Versioned declarations, not RES change/admission claims or physical production qualification.</summary>
public sealed class ResourceCapabilityOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<ResourceSourceScopeV1> ScopeAsync(string kind, CancellationToken ct = default) => Authority.ExecuteAsync(Resource(kind), "query", false,
        (a, _) => Task.FromResult(new ResourceSourceScopeV1(db.CurrentTenantId, a.ActorId)), ct, readOnlyAuthorityFence: true);

    private async Task<(ResourceSourceVersionV1 Resource, ResourceCapabilityOriginalV1[] Units, string? Time, string[] Missing)> Sources(
        OperationsAccessV1 root, ResourceCapabilityBodyV1 body, bool current, CancellationToken ct)
    {
        var access = await root.SourceAsync(Resource(body.Source.Kind), ct);
        var sources = new ResourceSourceStoreV1(db); await sources.Fence(false, ct);
        var resource = await sources.Read(access, body.Source.Kind, body.Source.Id, Revision(body.Source.Version), ct);
        if (resource.Reference != body.Source) throw Error("RESOURCE_CAPABILITY_SOURCE_CHANGED", 412);
        access.AtCommit(t => access.RequireResourceSource(db, body.Source.Kind, body.Source.Id, t));
        var missing = new List<string>();
        if (!resource.IsCurrent || !resource.SourcePresent || !resource.SourceEnabled) missing.Add("RESOURCE_SOURCE_CHANGED_OR_DISABLED");
        var units = new UomProfileStoreV1(db); var originals = new Dictionary<UomProfileRefV1, UomProfileVersionV1>();
        OperationsAccessV1? uom = null;
        async Task<UomProfileVersionV1> Unit(UomProfileRefV1 reference)
        {
            if (originals.TryGetValue(reference, out var cached)) return cached;
            uom ??= await root.SourceAsync(OperationsOwnerV1.UomProfile, ct);
            await units.Fence(false, ct); await uom.RequireUomProfile(reference.Id, ct);
            var actual = await units.Read(reference.Id, Revision(reference.Version), ct);
            if (actual.Reference != reference) throw Error("RESOURCE_CAPABILITY_UNIT_CHANGED", 412);
            originals.Add(reference, actual); uom.AtCommit(t => uom.RequireUomProfile(reference.Id, t));
            if (!actual.IsCurrent || actual.Profile.Enabled != true) missing.Add("UNIT_SOURCE_CHANGED_OR_DISABLED");
            if (actual.Profile.Mode != "BASE")
            {
                var baseline = await Unit(actual.Profile.BaseRef!);
                if (baseline.Profile.Mode != "BASE" || baseline.Profile.DimensionKey != actual.Profile.DimensionKey) throw Error("RESOURCE_CAPABILITY_UNIT_BASE_INVALID", 422);
            }
            return actual;
        }
        foreach (var reference in ResourceCapabilityValuesV1.Units(body)) await Unit(reference);
        foreach (var item in body.Capabilities.Where(x => x.Unit != null))
        {
            var value = originals[item.Unit!];
            if (value.Profile.DimensionKey != item.Dimension) throw Error("RESOURCE_CAPABILITY_DIMENSION_MISMATCH", 422);
            // Fixed-scale capability values fit decimal(21,8); no rounding or unit conversion is performed.
            var number = decimal.Parse(item.DecimalValue!, CultureInfo.InvariantCulture);
            if (value.Profile.QuantityScale is not { } scale || scale < 8 && decimal.Round(number, scale) != number)
                throw Error("RESOURCE_CAPABILITY_UNIT_PRECISION_MISMATCH", 422);
        }
        string? timeOriginal = null;
        if (body.TimeUnit != null)
        {
            var time = originals[body.TimeUnit]; var baseline = time.Profile.Mode == "BASE" ? time : originals[time.Profile.BaseRef!];
            var store = new UomTimeBasisStoreV1(db); await store.Fence(false, ct);
            var basis = await store.Read(body.TimeBasis!.Id, Revision(body.TimeBasis.Version), ct);
            await uom!.RequireUomProfile(basis.Reference.Id, ct); uom.AtCommit(t => uom.RequireUomProfile(basis.Reference.Id, t));
            if (basis.Reference != body.TimeBasis || basis.Definition.BaseRef != baseline.Reference) throw Error("RESOURCE_CAPABILITY_TIME_BASIS_MISMATCH", 412);
            if (!basis.IsCurrent || !basis.Definition.Enabled) missing.Add("TIME_BASIS_CHANGED_OR_DISABLED");
            timeOriginal = basis.OriginalJson;
        }
        if (current && missing.Count != 0) throw Error("RESOURCE_CAPABILITY_SOURCE_CHANGED", 412);
        if (body.TimeUnit == null) missing.Add("RESOURCE_TIME_UNIT_REQUIRED");
        if (body.Source.Kind == "MACHINE" && body.OutputUnit == null) missing.Add("RESOURCE_OUTPUT_UNIT_REQUIRED");
        if (body.Source.Kind == "MACHINE" && body.Capabilities.Length == 0) missing.Add("RESOURCE_CAPABILITIES_UNKNOWN");
        missing.Add("RESOURCE_QUALIFICATION_REQUIRED");
        return (resource, originals.Select(x => new ResourceCapabilityOriginalV1(x.Key, x.Value.OriginalJson)).ToArray(), timeOriginal, missing.Distinct().ToArray());
    }
    private async Task<ResourceCapabilitySnapshotV1> Observe(OperationsAccessV1 a, ResourceCapabilitySnapshotV1 value, CancellationToken ct)
    {
        var observation = await Sources(a, value.Body, false, ct);
        return value with { Missing = value.IsCurrent ? observation.Missing : ["RESOURCE_CAPABILITY_CHANGED", ..observation.Missing] };
    }
    public Task<ResourceCapabilityEditorV1> EditorAsync(string kind, Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("RESOURCE_ID_INVALID", 400);
        return Authority.ExecuteAsync(Resource(kind), "query", false, async (a, t) =>
        {
            var sources = new ResourceSourceStoreV1(db); await sources.Fence(false, t); var resource = await sources.Read(a, kind, id, null, t);
            a.AtCommit(last => a.RequireResourceSource(db, kind, id, last));
            var store = new ResourceCapabilityStoreV1(db); await store.Fence(false, t); var head = await store.Head(kind, id, t);
            var value = head == null ? null : await Observe(a, await store.Read(kind, id, head, t), t);
            return new ResourceCapabilityEditorV1(resource, head?.ToString(CultureInfo.InvariantCulture) ?? "absent", value);
        }, ct);
    }
    public Task<ResourceCapabilitySnapshotV1> ReadAsync(string kind, Guid id, string? version = null, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("RESOURCE_ID_INVALID", 400); var exact = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(Resource(kind), "query", false, async (a, t) =>
        {
            await a.RequireResourceSource(db, kind, id, t); var store = new ResourceCapabilityStoreV1(db); await store.Fence(false, t);
            return await Observe(a, await store.Read(kind, id, exact, t), t);
        }, ct);
    }
    internal async Task<ResourceCapabilitySnapshotV1> Source(OperationsAccessV1 root, ResourceCapabilityRefV1 reference, bool current, CancellationToken ct)
    {
        Reference(reference); var a = await root.SourceAsync(Resource(reference.Kind), ct);
        await a.RequireResourceSource(db, reference.Kind, reference.Id, ct);
        var store = new ResourceCapabilityStoreV1(db); await store.Fence(false, ct);
        var value = await store.Read(reference.Kind, reference.Id, Revision(reference.Version), ct);
        if (value.Reference != reference || current && !value.IsCurrent) throw Error("RESOURCE_CAPABILITY_CHANGED", 412);
        var sources = await Sources(a, value.Body, current, ct);
        return value with { Missing = value.IsCurrent ? sources.Missing : ["RESOURCE_CAPABILITY_CHANGED", ..sources.Missing] };
    }
    public Task<ResourceSourceResultV1> SaveAsync(string kind, ResourceCapabilityCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty) throw Error("RESOURCE_CAPABILITY_COMMAND_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input); Validate(kind, frozen.Body); Text(frozen.Reason, 2000);
        if (frozen.ExpectedVersion != "absent") Revision(frozen.ExpectedVersion);
        var bytes = MainOwnerOperationJournalV1.Encode(frozen); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(Resource(kind), "capability-edit", true, async (a, t) =>
        {
            var store = new ResourceCapabilityStoreV1(db); await store.Fence(true, t);
            if (original != null) return await Disclose(a, store, kind, frozen.OperationId, original, bytes, t);
            await a.RequireResourceSource(db, kind, frozen.Body.Source.Id, t);
            a.AtCommit(last => a.RequireResourceSource(db, kind, frozen.Body.Source.Id, last));
            var sources = await Sources(a, frozen.Body, true, t);
            var value = await store.Append(kind, frozen, a.ActorId, sources.Resource, sources.Units, sources.Time, t);
            var result = new ResourceSourceResultV1(frozen.OperationId, kind, value.Reference.Id, value.Reference.Version, value.Reference.Digest,
                value.Reference.Version != frozen.ExpectedVersion);
            await journal.Store(Journal(kind), Contract, frozen.OperationId, a.ActorId, bytes, result, [result.ObjectId],
                new { action = "record-resource-capability-declaration", actor = a.ActorId, frozen.Reason, previous = frozen.ExpectedVersion,
                    reference = value.Reference, qualification = "DECLARED_NOT_QUALIFIED", result.Changed }, t);
            return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(Journal(kind), frozen.OperationId, t); return original == null ? "capability-edit" : "query"; });
    }
    private async Task<ResourceSourceResultV1> Disclose(OperationsAccessV1 a, ResourceCapabilityStoreV1 store, string kind, Guid id,
        MainOwnerOperationJournalV1.Original original, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<ResourceSourceResultV1>(original.Result);
        if (result == null || result.OperationId != id || result.Kind != kind || original.Objects.Length != 1 || original.Objects[0] != result.ObjectId)
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var source = await Observe(a, await store.Read(kind, result.ObjectId, Revision(result.Version), ct), ct);
        if (source.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        return result;
    }
    public Task<ResourceSourceOperationV1> OperationAsync(string kind, Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(Resource(kind), "query", close, async (a, t) =>
        {
            var store = new ResourceCapabilityStoreV1(db); await store.Fence(close, t); var journal = new MainOwnerOperationJournalV1(db);
            var original = await journal.Read(Journal(kind), id, t);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new ResourceSourceOperationV1(id, "CLOSED")
                    : new(id, "COMMITTED", await Disclose(a, store, kind, id, original, null, t));
            }
            if (!close) return new ResourceSourceOperationV1(id, "UNKNOWN");
            var result = new ResourceSourceOperationV1(id, "CLOSED");
            await journal.Store(Journal(kind), Contract, id, a.ActorId, null, result, [], new { action = "close-resource-capability-declaration", kind }, t);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
