using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringManufacturingOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Plm.EngineeringManufacturing.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<EngineeringProductScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.EngineeringManufacturing, "query", false,
        (a, _) => Task.FromResult(new EngineeringProductScopeV1(db.CurrentTenantId, a.ActorId)), ct, readOnlyAuthorityFence: true);

    public Task<EngineeringManufacturingSnapshotV1[]> ListAsync(Guid? revisionId = null, string? search = null, int page = 1, CancellationToken ct = default)
    {
        if (revisionId == Guid.Empty || search?.Length > 256 || page is < 1 or > 100000) throw Error("MANUFACTURING_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringManufacturing, "query", false, async (a, token) =>
        {
            var store = new EngineeringManufacturingStoreV1(db); await store.Fence(false, token);
            var rows = a.EngineeringManufacturings();
            if (revisionId != null) rows = rows.Where(x => x.RevisionId == revisionId);
            if (!string.IsNullOrEmpty(search)) rows = rows.Where(x => x.Name.Contains(search));
            var ids = await rows.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).Select(x => x.Id).ToArrayAsync(token);
            var result = new List<EngineeringManufacturingSnapshotV1>();
            foreach (var id in ids) result.Add(await Observe(a, await store.Read(id, null, token), token));
            return result.ToArray();
        }, ct);
    }
    public Task<EngineeringManufacturingSnapshotV1> ReadAsync(Guid id, string? version = null, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("MANUFACTURING_IDENTITY_INVALID", 400);
        var selected = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringManufacturing, "query", false, async (a, token) =>
        {
            var store = new EngineeringManufacturingStoreV1(db); await store.Fence(false, token); await a.RequireEngineeringManufacturing(id, token);
            return await Observe(a, await store.Read(id, selected, token), token);
        }, ct);
    }
    public Task<EngineeringManufacturingDiffV1> DiffAsync(Guid id, string before, string after, CancellationToken ct = default)
    {
        var first = Revision(before); var last = Revision(after);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringManufacturing, "query", false, async (a, token) =>
        {
            var store = new EngineeringManufacturingStoreV1(db); await store.Fence(false, token); await a.RequireEngineeringManufacturing(id, token);
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
            Compare("configurationRef", left.Body.ConfigurationRef, right.Body.ConfigurationRef);
            Compare("ebomRef", left.Body.EbomRef, right.Body.EbomRef); Compare("mbomRef", left.Body.MbomRef, right.Body.MbomRef);
            Compare("routingRef", left.Body.RoutingRef, right.Body.RoutingRef);
            foreach (var id in left.Body.Mappings.Select(x => x.MapId).Union(right.Body.Mappings.Select(x => x.MapId)).Order())
                Compare("mappings/" + id, left.Body.Mappings.SingleOrDefault(x => x.MapId == id), right.Body.Mappings.SingleOrDefault(x => x.MapId == id));
            foreach (var id in left.Body.Outputs.Select(x => x.OutputOccurrenceId).Union(right.Body.Outputs.Select(x => x.OutputOccurrenceId)).Order())
                Compare("outputs/" + id, left.Body.Outputs.SingleOrDefault(x => x.OutputOccurrenceId == id), right.Body.Outputs.SingleOrDefault(x => x.OutputOccurrenceId == id));
            return new EngineeringManufacturingDiffV1(left.Reference, right.Reference, changes.ToArray());
        }, ct);
    }
    public Task<EngineeringManufacturingResultV1> CommandAsync(EngineeringManufacturingCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.ManufacturingId == Guid.Empty || input.Action is not ("CREATE" or "REVISE")) throw Error("MANUFACTURING_COMMAND_INVALID", 400);
        var command = OperationsOwnerV1.Freeze(input); EngineeringRoutingValuesV1.Text(command.Reason);
        EngineeringManufacturingValuesV1.Validate(command.Body);
        if (command.Action == "CREATE")
        { if (command.ExpectedVersion != null || command.ExpectedEtag != null) throw Error("MANUFACTURING_COMMAND_INVALID", 400); }
        else { Revision(command.ExpectedVersion); if (command.ExpectedEtag == null || command.ExpectedEtag == Guid.Empty) throw Error("MANUFACTURING_COMMAND_INVALID", 400); }
        var bytes = MainOwnerOperationJournalV1.Encode(command); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null; var action = command.Action == "CREATE" ? "add" : "edit";
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringManufacturing, action, true, async (a, token) =>
        {
            var store = new EngineeringManufacturingStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(a, store, original, command.OperationId, bytes, token);
            EngineeringManufacturingSnapshotV1? previous = null;
            if (command.Action == "CREATE") await a.RequireEngineeringManufacturingCreation(command.Body.RevisionOrigin.Id, token);
            else
            {
                await a.RequireEngineeringManufacturing(command.ManufacturingId, token); previous = await store.Read(command.ManufacturingId, null, token);
                if (previous.Reference.Version != command.ExpectedVersion || previous.Etag != command.ExpectedEtag) throw Error("MANUFACTURING_VERSION_CHANGED", 412);
            }
            await Sources(a, command.Body, true, token);
            var saved = await store.Append(command, a.ActorId, token); await Observe(a, saved, token);
            var result = new EngineeringManufacturingResultV1(command.OperationId, "ENGINEERING_MANUFACTURING", command.ManufacturingId, saved.Reference.Version, saved.Reference.Digest);
            await journal.Store(EngineeringManufacturingValuesV1.Owner, Contract, command.OperationId, a.ActorId, bytes, result, [command.ManufacturingId],
                new { action = command.Action, actor = a.ActorId, command.Reason, result, parent = saved.Body.RevisionOrigin }, token);
            return result;
        }, ct, actionAtFence: async token => { original = await journal.Read(EngineeringManufacturingValuesV1.Owner, command.OperationId, token); return original == null ? action : "query"; });
    }
    private async Task<EngineeringManufacturingResultV1> Disclose(OperationsAccessV1 a, EngineeringManufacturingStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid id, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<EngineeringManufacturingResultV1>(original.Result);
        if (result == null || result.OperationId != id || result.Kind != "ENGINEERING_MANUFACTURING" || !original.Objects.SequenceEqual(new[] { result.ObjectId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var saved = await Observe(a, await store.Read(result.ObjectId, Revision(result.Version), ct), ct);
        if (saved.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        return result;
    }
    public Task<EngineeringManufacturingOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringManufacturing, "query", close, async (a, token) =>
        {
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(EngineeringManufacturingValuesV1.Owner, id, token);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                if (original.State == "CLOSED") return new EngineeringManufacturingOperationV1(id, "CLOSED");
                var store = new EngineeringManufacturingStoreV1(db); await store.Fence(false, token);
                return new EngineeringManufacturingOperationV1(id, "COMMITTED", await Disclose(a, store, original, id, null, token));
            }
            if (!close) return new EngineeringManufacturingOperationV1(id, "UNKNOWN");
            var result = new EngineeringManufacturingOperationV1(id, "CLOSED");
            await journal.Store(EngineeringManufacturingValuesV1.Owner, Contract, id, a.ActorId, null, result, [],
                new { action = "close-uncommitted-engineering-manufacturing", actor = a.ActorId }, token);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
