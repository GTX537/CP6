using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class ControlledDocumentOwnerV1(CP6Context db, OperationsOwnerV1 owner, CP6.Core.Services.Pub.FileContentOwnerV1 files,
    CP6.Core.Services.Sys.ScopeAuthorizationFenceV1 reviewScope)
{
    private const string Contract = "Main.Plm.ControlledDocument.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<EngineeringProductScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.ControlledDocument, "query", false,
        (a, _) => { a.RequireDocumentFields(false); return Task.FromResult(new EngineeringProductScopeV1(db.CurrentTenantId, a.ActorId)); }, ct, readOnlyAuthorityFence: true);

    public Task<ControlledDocumentSnapshotV1[]> ListAsync(Guid? revisionId = null, string? search = null, int page = 1, CancellationToken ct = default)
    {
        if (revisionId == Guid.Empty || search?.Length > 256 || page is < 1 or > 100000) throw Error("DOCUMENT_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument, "query", false, async (a, token) =>
        {
            var store = new ControlledDocumentStoreV1(db); await store.Fence(false, token);
            var rows = a.ControlledDocuments();
            if (revisionId != null) rows = rows.Where(x => x.RevisionId == revisionId);
            if (!string.IsNullOrEmpty(search)) rows = rows.Where(x => x.Name.Contains(search));
            var ids = await rows.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).Select(x => x.Id).ToArrayAsync(token);
            var result = new List<ControlledDocumentSnapshotV1>();
            foreach (var id in ids) result.Add(await Observe(a, await store.Read(id, null, token), token));
            return result.ToArray();
        }, ct);
    }
    public Task<ControlledDocumentSnapshotV1> ReadAsync(Guid id, string? version = null, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("DOCUMENT_IDENTITY_INVALID", 400);
        var selected = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument, "query", false, async (a, token) =>
        {
            var store = new ControlledDocumentStoreV1(db); await store.Fence(false, token); await a.RequireControlledDocument(id, token);
            return await Observe(a, await store.Read(id, selected, token), token);
        }, ct);
    }
    public Task<ControlledDocumentDiffV1> DiffAsync(Guid id, string before, string after, CancellationToken ct = default)
    {
        var first = Revision(before); var last = Revision(after);
        return Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument, "query", false, async (a, token) =>
        {
            var store = new ControlledDocumentStoreV1(db); await store.Fence(false, token); await a.RequireControlledDocument(id, token);
            var left = await Observe(a, await store.Read(id, first, token), token); var right = await Observe(a, await store.Read(id, last, token), token);
            var changes = new List<EngineeringWorkingChangeV1>();
            void Compare(string path, object? x, object? y)
            {
                JsonElement? p = x == null ? null : JsonSerializer.SerializeToElement(x, EngineeringProductStoreV1.Json);
                JsonElement? q = y == null ? null : JsonSerializer.SerializeToElement(y, EngineeringProductStoreV1.Json);
                if (p?.GetRawText() != q?.GetRawText()) changes.Add(new(path, p, q));
            }
            Compare("name", left.Body.Name, right.Body.Name); Compare("revisionLabel", left.Body.RevisionLabel, right.Body.RevisionLabel);
            Compare("revisionOrigin", left.Body.RevisionOrigin, right.Body.RevisionOrigin); Compare("productRef", left.Body.ProductRef, right.Body.ProductRef);
            Compare("source", left.Body.Source, right.Body.Source);
            foreach (var occurrence in left.Body.Contents.Select(x => x.OccurrenceId).Union(right.Body.Contents.Select(x => x.OccurrenceId)).Order())
                Compare("contents/" + occurrence, left.Body.Contents.SingleOrDefault(x => x.OccurrenceId == occurrence), right.Body.Contents.SingleOrDefault(x => x.OccurrenceId == occurrence));
            return new ControlledDocumentDiffV1(left.Reference, right.Reference, changes.ToArray());
        }, ct);
    }
    internal async Task<ControlledDocumentSnapshotV1> Source(OperationsAccessV1 root, EngineeringSourceRefV1 reference,
        Guid parent, EngineeringProductRefV1? product, bool current, CancellationToken ct)
    {
        ControlledDocumentValuesV1.Ref(reference); var a = await root.SourceAsync(OperationsOwnerV1.ControlledDocument, ct);
        var store = new ControlledDocumentStoreV1(db); await store.Fence(false, ct); await a.RequireControlledDocument(reference.Id, ct);
        var value = await Observe(a, await store.Read(reference.Id, Revision(reference.Version), ct), ct);
        if (value.Reference != reference) throw Error("DOCUMENT_REFERENCE_CHANGED", 412);
        if (value.Body.RevisionOrigin.Id != parent) throw Error("DOCUMENT_REVISION_MISMATCH", 422);
        if (current && (!value.IsCurrent || value.Body.ProductRef != product)) throw Error("DOCUMENT_SOURCE_CHANGED", 412);
        if (current) await Sources(a, value.Body, true, ct, requireCurrentParent: false);
        return value.Body.ProductRef == product ? value : value with { Missing = [..value.Missing!, "PRODUCT_SOURCE_CHANGED"] };
    }
    public Task<ControlledDocumentResultV1> CommandAsync(ControlledDocumentCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.DocumentId == Guid.Empty || input.Action is not ("CREATE" or "REVISE")) throw Error("DOCUMENT_COMMAND_INVALID", 400);
        var command = OperationsOwnerV1.Freeze(input); ControlledDocumentValuesV1.Text(command.Reason);
        ControlledDocumentValuesV1.Validate(command.Body);
        if (command.Action == "CREATE")
        { if (command.ExpectedVersion != null || command.ExpectedEtag != null) throw Error("DOCUMENT_COMMAND_INVALID", 400); }
        else { Revision(command.ExpectedVersion); if (command.ExpectedEtag == null || command.ExpectedEtag == Guid.Empty) throw Error("DOCUMENT_COMMAND_INVALID", 400); }
        var bytes = MainOwnerOperationJournalV1.Encode(command); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null; var action = command.Action == "CREATE" ? "add" : "edit";
        return Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument, action, true, async (a, token) =>
        {
            var store = new ControlledDocumentStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(a, store, original, command.OperationId, bytes, token);
            ControlledDocumentSnapshotV1? previous = null;
            if (command.Action == "CREATE") await a.RequireControlledDocumentCreation(command.Body.RevisionOrigin.Id, token);
            else
            {
                await a.RequireControlledDocument(command.DocumentId, token); previous = await store.Read(command.DocumentId, null, token);
                if (previous.Reference.Version != command.ExpectedVersion || previous.Etag != command.ExpectedEtag) throw Error("DOCUMENT_VERSION_CHANGED", 412);
            }
            await Sources(a, command.Body, true, token);
            if (previous != null)
                foreach (var content in command.Body.Contents.Where(x => x.DerivedFromOccurrenceId != null))
                {
                    var old = previous.Body.Contents.SingleOrDefault(x => x.File == content.File);
                    if (old == null) continue;
                    var oldSource = previous.Body.Contents.SingleOrDefault(x => x.OccurrenceId == old.DerivedFromOccurrenceId);
                    var newSource = command.Body.Contents.Single(x => x.OccurrenceId == content.DerivedFromOccurrenceId);
                    if (oldSource?.File != newSource.File) throw Error("DOCUMENT_DERIVATION_REQUIRES_NEW_CONTENT",409);
                }
            a.RequireDocumentFields(true);
            var saved = await store.Append(command, a.ActorId, token); await Observe(a, saved, token);
            var result = new ControlledDocumentResultV1(command.OperationId, "CONTROLLED_DOCUMENT", command.DocumentId, saved.Reference.Version, saved.Reference.Digest);
            await journal.Store(ControlledDocumentValuesV1.Owner, Contract, command.OperationId, a.ActorId, bytes, result, [command.DocumentId],
                new { action = command.Action, actor = a.ActorId, command.Reason, result, parent = saved.Body.RevisionOrigin }, token);
            return result;
        }, ct, actionAtFence: async token => { original = await journal.Read(ControlledDocumentValuesV1.Owner, command.OperationId, token); return original == null ? action : "query"; });
    }
    private async Task<ControlledDocumentResultV1> Disclose(OperationsAccessV1 a, ControlledDocumentStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid id, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<ControlledDocumentResultV1>(original.Result);
        if (result == null || result.OperationId != id || result.Kind != "CONTROLLED_DOCUMENT" || !original.Objects.SequenceEqual(new[] { result.ObjectId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var saved = await Observe(a, await store.Read(result.ObjectId, Revision(result.Version), ct), ct);
        if (saved.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        return result;
    }
    public Task<ControlledDocumentOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument, "query", close, async (a, token) =>
        {
            a.RequireDocumentFields(false); var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(ControlledDocumentValuesV1.Owner, id, token);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                if (original.State == "CLOSED") return new ControlledDocumentOperationV1(id, "CLOSED");
                var store = new ControlledDocumentStoreV1(db); await store.Fence(false, token);
                return new ControlledDocumentOperationV1(id, "COMMITTED", await Disclose(a, store, original, id, null, token));
            }
            if (!close) return new ControlledDocumentOperationV1(id, "UNKNOWN");
            var result = new ControlledDocumentOperationV1(id, "CLOSED");
            await journal.Store(ControlledDocumentValuesV1.Owner, Contract, id, a.ActorId, null, result, [],
                new { action = "close-uncommitted-controlled-document", actor = a.ActorId }, token);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
