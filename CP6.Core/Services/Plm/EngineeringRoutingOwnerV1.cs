using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringRoutingOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Plm.EngineeringRouting.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<EngineeringProductScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.EngineeringRouting, "query", false,
        (a, _) => Task.FromResult(new EngineeringProductScopeV1(db.CurrentTenantId, a.ActorId)), ct, readOnlyAuthorityFence: true);

    public Task<EngineeringRoutingSnapshotV1[]> ListAsync(Guid? revisionId = null, string? search = null, int page = 1, CancellationToken ct = default)
    {
        if (revisionId == Guid.Empty || search?.Length > 256 || page is < 1 or > 100000) throw Error("ROUTING_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRouting, "query", false, async (a, token) =>
        {
            var store = new EngineeringRoutingStoreV1(db); await store.Fence(false, token);
            var rows = a.EngineeringRoutings();
            if (revisionId != null) rows = rows.Where(x => x.RevisionId == revisionId);
            if (!string.IsNullOrEmpty(search)) rows = rows.Where(x => x.Name.Contains(search));
            var ids = await rows.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).Select(x => x.Id).ToArrayAsync(token);
            var result = new List<EngineeringRoutingSnapshotV1>();
            foreach (var id in ids) result.Add(await Observe(a, await store.Read(id, null, token), token));
            return result.ToArray();
        }, ct);
    }
    public Task<EngineeringRoutingSnapshotV1> ReadAsync(Guid id, string? version = null, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ROUTING_IDENTITY_INVALID", 400);
        var selected = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRouting, "query", false, async (a, token) =>
        {
            var store = new EngineeringRoutingStoreV1(db); await store.Fence(false, token); await a.RequireEngineeringRouting(id, token);
            return await Observe(a, await store.Read(id, selected, token), token);
        }, ct);
    }
    public Task<EngineeringRoutingDiffV1> DiffAsync(Guid id, string before, string after, CancellationToken ct = default)
    {
        var first = Revision(before); var last = Revision(after);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRouting, "query", false, async (a, token) =>
        {
            var store = new EngineeringRoutingStoreV1(db); await store.Fence(false, token); await a.RequireEngineeringRouting(id, token);
            var left = await Observe(a, await store.Read(id, first, token), token); var right = await Observe(a, await store.Read(id, last, token), token);
            var changes = new List<EngineeringWorkingChangeV1>();
            void Compare(string path, object? x, object? y)
            {
                JsonElement? p = x == null ? null : JsonSerializer.SerializeToElement(x, EngineeringProductStoreV1.Json);
                JsonElement? q = y == null ? null : JsonSerializer.SerializeToElement(y, EngineeringProductStoreV1.Json);
                if (p?.GetRawText() != q?.GetRawText()) changes.Add(new(path, p, q));
            }
            Compare("name", left.Body.Name, right.Body.Name); Compare("revisionOrigin", left.Body.RevisionOrigin, right.Body.RevisionOrigin);
            Compare("productRef", left.Body.ProductRef, right.Body.ProductRef); Compare("siteRef", left.Body.SiteRef, right.Body.SiteRef);
            Compare("manufacturingViewRequirement", left.Body.ManufacturingViewRequirement, right.Body.ManufacturingViewRequirement);
            foreach (var occurrence in left.Body.Operations.Select(x => x.OccurrenceId).Union(right.Body.Operations.Select(x => x.OccurrenceId)).Order())
                Compare("operations/" + occurrence, left.Body.Operations.SingleOrDefault(x => x.OccurrenceId == occurrence), right.Body.Operations.SingleOrDefault(x => x.OccurrenceId == occurrence));
            Compare("legacy", left.Legacy, right.Legacy);
            return new EngineeringRoutingDiffV1(left.Reference, right.Reference, changes.ToArray());
        }, ct);
    }
    internal async Task<EngineeringRoutingSnapshotV1> Source(OperationsAccessV1 root, EngineeringSourceRefV1 reference,
        Guid parent, EngineeringProductRefV1? product, bool current, CancellationToken ct)
    {
        EngineeringRoutingValuesV1.Ref(reference); var a = await root.SourceAsync(OperationsOwnerV1.EngineeringRouting, ct);
        var store = new EngineeringRoutingStoreV1(db); await store.Fence(false, ct); await a.RequireEngineeringRouting(reference.Id, ct);
        var value = await Observe(a, await store.Read(reference.Id, Revision(reference.Version), ct), ct);
        if (value.Reference != reference) throw Error("ROUTING_REFERENCE_CHANGED", 412);
        if (value.Body.RevisionOrigin.Id != parent) throw Error("ROUTING_REVISION_MISMATCH", 422);
        if (current && (!value.IsCurrent || value.Body.ProductRef != product)) throw Error("ROUTING_SOURCE_CHANGED", 412);
        if (current) await Sources(a, value.Body, true, ct, requireCurrentParent: false);
        return value.Body.ProductRef == product ? value : value with { Missing = [..value.Missing!, "PRODUCT_SOURCE_CHANGED"] };
    }
    public Task<EngineeringRoutingResultV1> CommandAsync(EngineeringRoutingCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.RoutingId == Guid.Empty || input.Action is not ("CREATE" or "REVISE")) throw Error("ROUTING_COMMAND_INVALID", 400);
        var command = OperationsOwnerV1.Freeze(input); EngineeringRoutingValuesV1.Text(command.Reason);
        EngineeringRoutingValuesV1.Validate(command.Body);
        if ((command.LegacyMappingRef == null) != (command.LegacyDigest == null)) throw Error("ROUTING_LEGACY_REFERENCE_REQUIRED", 400);
        if (command.Action == "CREATE")
        { if (command.ExpectedVersion != null || command.ExpectedEtag != null) throw Error("ROUTING_COMMAND_INVALID", 400); }
        else { Revision(command.ExpectedVersion); if (command.ExpectedEtag == null || command.ExpectedEtag == Guid.Empty) throw Error("ROUTING_COMMAND_INVALID", 400); }
        var bytes = MainOwnerOperationJournalV1.Encode(command); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null; var action = command.Action == "CREATE" ? "add" : "edit";
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRouting, action, true, async (a, token) =>
        {
            var store = new EngineeringRoutingStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(a, store, original, command.OperationId, bytes, token);
            EngineeringRoutingSnapshotV1? previous = null;
            if (command.Action == "CREATE") await a.RequireEngineeringRoutingCreation(command.Body.RevisionOrigin.Id, token);
            else
            {
                await a.RequireEngineeringRouting(command.RoutingId, token); previous = await store.Read(command.RoutingId, null, token);
                if (previous.Reference.Version != command.ExpectedVersion || previous.Etag != command.ExpectedEtag) throw Error("ROUTING_VERSION_CHANGED", 412);
            }
            await Sources(a, command.Body, true, token);
            var legacy = previous?.Legacy;
            if (command.LegacyMappingRef != null)
            {
                legacy = await Legacy(a, command.LegacyMappingRef, command.Body.ProductRef, command.Body.SiteRef, token);
                if (legacy.Digest != command.LegacyDigest) throw Error("ROUTING_LEGACY_CHANGED", 412);
            }
            var originalIds = legacy?.Rows.Select(x => x.GetProperty("Id").GetGuid()).ToHashSet() ?? [];
            var used = new HashSet<Guid>();
            foreach (var step in command.Body.Operations)
                if (step.LegacyRowId is Guid id && (!originalIds.Contains(id) || !used.Add(id))) throw Error("ROUTING_LEGACY_ROW_INVALID", 400);
            var saved = await store.Append(command, a.ActorId, legacy, token); await Observe(a, saved, token);
            var result = new EngineeringRoutingResultV1(command.OperationId, "ENGINEERING_ROUTING", command.RoutingId, saved.Reference.Version, saved.Reference.Digest);
            await journal.Store(EngineeringRoutingValuesV1.Owner, Contract, command.OperationId, a.ActorId, bytes, result, [command.RoutingId],
                new { action = command.Action, actor = a.ActorId, command.Reason, result, parent = saved.Body.RevisionOrigin }, token);
            return result;
        }, ct, actionAtFence: async token => { original = await journal.Read(EngineeringRoutingValuesV1.Owner, command.OperationId, token); return original == null ? action : "query"; });
    }
    private async Task<EngineeringRoutingResultV1> Disclose(OperationsAccessV1 a, EngineeringRoutingStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid id, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<EngineeringRoutingResultV1>(original.Result);
        if (result == null || result.OperationId != id || result.Kind != "ENGINEERING_ROUTING" || !original.Objects.SequenceEqual(new[] { result.ObjectId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var saved = await Observe(a, await store.Read(result.ObjectId, Revision(result.Version), ct), ct);
        if (saved.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        return result;
    }
    public Task<EngineeringRoutingOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRouting, "query", close, async (a, token) =>
        {
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(EngineeringRoutingValuesV1.Owner, id, token);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                if (original.State == "CLOSED") return new EngineeringRoutingOperationV1(id, "CLOSED");
                var store = new EngineeringRoutingStoreV1(db); await store.Fence(false, token);
                return new EngineeringRoutingOperationV1(id, "COMMITTED", await Disclose(a, store, original, id, null, token));
            }
            if (!close) return new EngineeringRoutingOperationV1(id, "UNKNOWN");
            var result = new EngineeringRoutingOperationV1(id, "CLOSED");
            await journal.Store(EngineeringRoutingValuesV1.Owner, Contract, id, a.ActorId, null, result, [],
                new { action = "close-uncommitted-engineering-routing", actor = a.ActorId }, token);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
