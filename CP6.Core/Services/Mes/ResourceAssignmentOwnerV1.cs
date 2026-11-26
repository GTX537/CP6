using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Mes.ResourceAssignmentStoreV1;
using static CP6.Core.Services.Mes.ResourceSourceStoreV1;

namespace CP6.Core.Services.Mes;

public sealed class ResourceAssignmentOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);

    private static DateTimeOffset Instant(string value) => ResourceCapabilityValuesV1.Time(value, "RESOURCE_ASSIGNMENT_TIME_INVALID");

    private static void Reference(ResourceSourceRefV1 value, string kind)
    {
        if (value == null || value.Owner != ResourceSourceStoreV1.Owner || value.Kind != kind || value.Id == Guid.Empty
            || value.Digest?.Length != 64 || value.Digest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw Error("RESOURCE_ASSIGNMENT_REFERENCE_INVALID", 400);
        Revision(value.Version);
    }

    private static void Validate(ResourceAssignmentCommandV1 input)
    {
        if (input == null || input.OperationId == Guid.Empty || input.Plan?.Intervals == null || input.Plan.Intervals.Length > 100)
            throw Error("RESOURCE_ASSIGNMENT_INPUT_INVALID", 400);
        Reference(input.Plan.Machine, "MACHINE");
        ResourceCapabilityValuesV1.Text(input.Reason, 2000, "RESOURCE_ASSIGNMENT_REASON_INVALID");
        if (input.ExpectedVersion != "absent") Revision(input.ExpectedVersion);
        DateTimeOffset? previousEnd = null;
        for (var i = 0; i < input.Plan.Intervals.Length; i++)
        {
            var interval = input.Plan.Intervals[i] ?? throw Error("RESOURCE_ASSIGNMENT_INPUT_INVALID", 400);
            Reference(interval.WorkCenter, "WORK_CENTER");
            var start = Instant(interval.ValidFrom);
            DateTimeOffset? end = interval.ValidTo == null ? null : Instant(interval.ValidTo);
            if (end <= start || i > 0 && (previousEnd == null || start < previousEnd))
                throw Error("RESOURCE_ASSIGNMENT_PERIOD_INVALID", 400);
            previousEnd = end;
        }
    }

    private async Task<ResourceSourceVersionV1> ReadSource(OperationsAccessV1 root, ResourceAssignmentStoreV1 store,
        ResourceSourceRefV1 reference, CancellationToken ct)
    {
        var access = await root.SourceAsync(Resource(reference.Kind), ct);
        var read = await store.Sources.Read(access, reference.Kind, reference.Id, Revision(reference.Version), ct);
        if (read.Reference != reference) throw Error("RESOURCE_ASSIGNMENT_SOURCE_CHANGED", 412);
        access.AtCommit(last => access.RequireResourceSource(db, reference.Kind, reference.Id, last));
        return read;
    }

    private static bool CurrentSource(ResourceSourceVersionV1 source) => source.IsCurrent && source.SourcePresent && source.SourceEnabled;

    private async Task<ResourceSourceRefV1?> Observe(OperationsAccessV1 root, ResourceAssignmentStoreV1 store,
        ResourceAssignmentPlanV1 plan, bool requireCurrent, CancellationToken ct)
    {
        var machine = await ReadSource(root, store, plan.Machine, ct);
        if (requireCurrent && !CurrentSource(machine)) throw Error("RESOURCE_ASSIGNMENT_SOURCE_CHANGED", 412);
        var centers = new Dictionary<ResourceSourceRefV1, ResourceSourceVersionV1>();
        ResourceSourceRefV1? current = null;
        var now = DateTimeOffset.UtcNow;
        foreach (var interval in plan.Intervals)
        {
            if (!centers.TryGetValue(interval.WorkCenter, out var center))
            {
                center = await ReadSource(root, store, interval.WorkCenter, ct);
                centers.Add(interval.WorkCenter, center);
            }
            if (interval.ValidTo != null && Instant(interval.ValidTo) <= now) continue;
            if (requireCurrent && !CurrentSource(center)) throw Error("RESOURCE_ASSIGNMENT_SOURCE_CHANGED", 412);
            if (CurrentSource(machine) && CurrentSource(center) && Instant(interval.ValidFrom) <= now) current = center.Reference;
        }
        return current;
    }

    public Task<ResourceAssignmentVersionV1?> ReadAsync(Guid machineId, string? version, CancellationToken ct = default)
    {
        if (machineId == Guid.Empty) throw Error("RESOURCE_ID_INVALID", 400);
        var exact = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.Machine, "query", false, async (access, token) =>
        {
            var store = new ResourceAssignmentStoreV1(db);
            await store.Fence(false, token);
            await access.RequireResourceSource(db, "MACHINE", machineId, token);
            access.AtCommit(last => access.RequireResourceSource(db, "MACHINE", machineId, last));
            var versioned = await store.Read(machineId, exact, token);
            if (versioned == null)
            {
                if (exact.HasValue) throw Error("RESOURCE_ASSIGNMENT_VERSION_NOT_FOUND", 404);
                return null;
            }
            var current = await Observe(access, store, versioned.Plan, false, token);
            return versioned with { CurrentWorkCenter = versioned.IsCurrent ? current : null };
        }, ct, readOnlyAuthorityFence: true);
    }

    public Task<ResourceSourceResultV1> SaveAsync(ResourceAssignmentCommandV1 input, CancellationToken ct = default)
    {
        if (input == null) throw Error("RESOURCE_ASSIGNMENT_INPUT_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input);
        Validate(frozen);
        var machineId = frozen.Plan.Machine.Id;
        var bytes = MainOwnerOperationJournalV1.Encode(frozen);
        var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Machine, "assignment-edit", true, async (access, token) =>
        {
            var store = new ResourceAssignmentStoreV1(db);
            await store.Fence(true, token);
            if (original != null) return await Disclose(access, store, original, frozen.OperationId, bytes, token);
            await access.RequireResourceSource(db, "MACHINE", machineId, token);
            access.AtCommit(last => access.RequireResourceSource(db, "MACHINE", machineId, last));
            var previous = await store.Read(machineId, null, token);
            if ((previous?.Reference.Version ?? "absent") != frozen.ExpectedVersion) throw Error("RESOURCE_ASSIGNMENT_CHANGED", 412);
            if (previous != null) await Observe(access, store, previous.Plan, false, token);
            await Observe(access, store, frozen.Plan, true, token);
            var saved = await store.Append(frozen, previous, access.ActorId, token);
            var result = new ResourceSourceResultV1(frozen.OperationId, "MACHINE", machineId, saved.Reference.Version,
                saved.Reference.Digest, saved.Reference.Version != frozen.ExpectedVersion);
            await journal.Store(AssignmentOwner, Contract, frozen.OperationId, access.ActorId, bytes, result, [machineId],
                new { action = "record-resource-assignment", frozen.Reason, previous = frozen.ExpectedVersion, saved.Reference, result.Changed }, token);
            return result;
        }, ct, actionAtFence: async token =>
        {
            original = await journal.Read(AssignmentOwner, frozen.OperationId, token);
            return original == null ? "assignment-edit" : "query";
        });
    }

    private async Task<ResourceSourceResultV1> Disclose(OperationsAccessV1 access, ResourceAssignmentStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid operationId, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<ResourceSourceResultV1>(original.Result);
        if (result == null || result.OperationId != operationId || result.Kind != "MACHINE"
            || original.Objects.Length != 1 || original.Objects[0] != result.ObjectId)
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var saved = await store.Read(result.ObjectId, Revision(result.Version), ct);
        if (saved == null || saved.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        await Observe(access, store, saved.Plan, false, ct);
        return result;
    }

    public Task<ResourceSourceOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.Machine, "query", close, async (access, token) =>
        {
            var store = new ResourceAssignmentStoreV1(db);
            await store.Fence(close, token);
            var journal = new MainOwnerOperationJournalV1(db);
            var original = await journal.Read(AssignmentOwner, id, token);
            if (original != null)
            {
                if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new ResourceSourceOperationV1(id, "CLOSED")
                    : new(id, "COMMITTED", await Disclose(access, store, original, id, null, token));
            }
            if (!close) return new ResourceSourceOperationV1(id, "UNKNOWN");
            var closed = new ResourceSourceOperationV1(id, "CLOSED");
            await journal.Store(AssignmentOwner, Contract, id, access.ActorId, null, closed, [],
                new { action = "close-resource-assignment" }, token);
            return closed;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
