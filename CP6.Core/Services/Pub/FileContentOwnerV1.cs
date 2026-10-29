using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Pub;

public sealed class FileContentOwnerV1(CP6Context db, OperationsOwnerV1 owner, IFileStore files, IFileContentInspectorV1 inspector)
{
    private const string Contract = "Main.File.Content.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<EngineeringProductScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.FileContent, "query", false, (a, _) => { a.RequireFileFields(false); return Task.FromResult(new EngineeringProductScopeV1(db.CurrentTenantId, a.ActorId)); }, ct, readOnlyAuthorityFence: true);
    private async Task Parent(OperationsAccessV1 root, EngineeringRevisionRefV1 reference, bool write, CancellationToken ct)
    {
        var a = await root.ActionAsync(OperationsOwnerV1.EngineeringRevision, write ? "edit" : "query", ct);
        await a.RequireEngineeringRevision(reference.Id, ct); a.AtCommit(t => a.RequireEngineeringRevision(reference.Id, t));
        var store = new EngineeringRevisionStoreV1(db); await store.Fence(false, ct);
        var value = await store.Read(reference.Id, Revision(reference.Version), ct);
        if (value.Reference != reference || write && !value.IsCurrent) throw Error("FILE_PARENT_CHANGED", 412);
        // The parent product is another original Owner, not an authority granted by a guessed business code.
        if (value.Draft.ProductRef is { } product)
        {
            var p = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct);
            await p.RequireEngineeringProduct(product.Id, ct); p.AtCommit(t => p.RequireEngineeringProduct(product.Id, t));
        }
    }
    private async Task<FileContentSnapshotV1> Observe(OperationsAccessV1 a, FileContentSnapshotV1 value, CancellationToken ct)
    {
        await a.RequireFileContent(value.ContentRef.Id, ct); a.AtCommit(t => a.RequireFileContent(value.ContentRef.Id, t));
        await Parent(a, value.Parent, false, ct); return value;
    }
    public Task<FileContentSnapshotV1[]> ListAsync(Guid parent, int page = 1, CancellationToken ct = default)
    {
        if (parent == Guid.Empty || page is < 1 or > 100000) throw Error("FILE_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "query", false, async (a, token) =>
        {
            var store = new FileContentStoreV1(db); await store.Fence(false, token);
            var parentAccess = await a.SourceAsync(OperationsOwnerV1.EngineeringRevision, token);
            await parentAccess.RequireEngineeringRevision(parent, token); parentAccess.AtCommit(t => parentAccess.RequireEngineeringRevision(parent, t));
            var ids = await a.FileContents().Where(x => x.ParentId == parent).OrderBy(x => x.FileName).ThenBy(x => x.Id)
                .Skip((page - 1) * 20).Take(20).Select(x => x.Id).ToArrayAsync(token);
            var result = new List<FileContentSnapshotV1>();
            foreach (var id in ids) result.Add(await Observe(a, await store.Read(id, null, token), token));
            return result.ToArray();
        }, ct);
    }
    public Task<FileContentSnapshotV1> ReadAsync(Guid id, string? stateVersion = null, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("FILE_ID_REQUIRED", 400);
        var version = stateVersion == null ? (long?)null : Revision(stateVersion);
        return Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "query", false, async (a, token) =>
        {
            var store = new FileContentStoreV1(db); await store.Fence(false, token); await a.RequireFileContent(id, token);
            return await Observe(a, await store.Read(id, version, token), token);
        }, ct);
    }
    public async Task<FileContentResultV1> UploadAsync(FileUploadV1 input, Stream stream, CancellationToken ct = default)
    {
        FileContentBytesV1.Validate(input); var command = OperationsOwnerV1.Freeze(input);
        var body = MainOwnerOperationJournalV1.Encode(command); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null;
        // Authorize the exact target before consuming caller bytes. Replay needs current read rights only.
        var previous = await Authority.ExecuteAsync<FileContentResultV1?>(OperationsOwnerV1.FileContent, "add", false, async (a, token) =>
        {
            var store = new FileContentStoreV1(db); await store.Fence(false, token);
            if (original != null) return await Disclose(a, store, original, command.OperationId, body, token);
            await a.RequireFileCreation(command.Parent.Id, token); await Parent(a, command.Parent, true, token); return null;
        }, ct, actionAtFence: async token => { original = await journal.Read(FileContentBytesV1.Owner, command.OperationId, token); return original == null ? "add" : "query"; });
        var bytes = await FileContentBytesV1.Read(stream, command.ByteLength, ct);
        if (FileContentBytesV1.Digest(bytes) != command.Digest) throw Error("FILE_CONTENT_MISMATCH", 400);
        if (previous != null)
        {
            // The input stream is checked even for a duplicate key, then disclosure is reauthorized after the read.
            return (await OperationAsync(command.OperationId, false, ct)).Result ?? throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        }
        // Generated storage key, independent of filenames and MD5. Concurrent attempts never overwrite each other.
        var key = "native-content-v1/" + db.CurrentTenantId.ToString("N") + "/" + Guid.NewGuid().ToString("N");
        var path = await files.SaveAsync(new MemoryStream(bytes, false), key);
        await VerifyStored(path, command.ByteLength, command.Digest, ct);
        var inspection = await inspector.InspectAsync(bytes, ct); ValidateInspection(inspection);
        // Scanner/store side effects happen before the DB transaction. Unreferenced staging is retained, not blindly deleted.
        return await Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "add", true, async (a, token) =>
        {
            var store = new FileContentStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(a, store, original, command.OperationId, body, token);
            await a.RequireFileCreation(command.Parent.Id, token); await Parent(a, command.Parent, true, token);
            await VerifyStored(path, command.ByteLength, command.Digest, token);
            var saved = await store.Create(command, a.ActorId, path, FileContentBytesV1.MediaType(bytes), inspection, token);
            await Observe(a, saved, token); return await SaveResult(journal, command.OperationId, body, saved, a.ActorId, token);
        }, ct, actionAtFence: async token => { original = await journal.Read(FileContentBytesV1.Owner, command.OperationId, token); return original == null ? "add" : "query"; });
    }
    private static void ValidateInspection(FileInspectionV1 result)
    {
        if (result == null || result.Status is not ("CLEAN" or "PENDING" or "NOT_CLEAN") || string.IsNullOrWhiteSpace(result.Provider)
            || result.Status == "CLEAN" && (string.IsNullOrWhiteSpace(result.EngineVersion) || string.IsNullOrWhiteSpace(result.SignatureVersion) || result.EvidenceDigest?.Length != 64))
            throw Error("FILE_INSPECTION_UNAVAILABLE", 503);
    }
    public async Task<FileContentResultV1> CommandAsync(FileContentCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.FileId == Guid.Empty || input.ExpectedEtag == Guid.Empty
            || input.Action is not ("HOLD" or "RELEASE" or "INSPECT")) throw Error("FILE_COMMAND_INVALID", 400);
        Revision(input.ExpectedVersion); EngineeringRoutingValuesV1.Text(input.Reason); var command = OperationsOwnerV1.Freeze(input);
        var body = MainOwnerOperationJournalV1.Encode(command); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null;
        return await Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "edit", true, async (a, token) =>
        {
            var store = new FileContentStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(a, store, original, command.OperationId, body, token);
            a.RequireFileFields(true); await a.RequireFileContent(command.FileId, token);
            var current = await Observe(a, await store.Read(command.FileId, null, token), token);
            if (current.StateVersion != command.ExpectedVersion || current.Etag != command.ExpectedEtag) throw Error("FILE_STATE_CHANGED", 412);
            // Hold/reinspection of historical content is allowed under current parent write authority; it does not mutate that revision.
            var parent = await a.ActionAsync(OperationsOwnerV1.EngineeringRevision, "edit", token);
            await parent.RequireEngineeringRevision(current.Parent.Id, token); parent.AtCommit(t => parent.RequireEngineeringRevision(current.Parent.Id, t));
            var inspection = current.Inspection;
            if (command.Action == "INSPECT")
            {
                var row = await store.Row(command.FileId, token);
                try { inspection = await inspector.InspectAsync(await VerifyStored(row.StorePath, row.ByteLength, row.Digest, token), token); }
                catch (CP6.Core.Services.Erp.OrderPriceCorrectionException e) when (e.Message == "FILE_CONTENT_INTEGRITY_UNAVAILABLE")
                { inspection = new("NOT_CLEAN", "ContentIntegrity", null, null, DateTimeOffset.UtcNow, null, "FILE_CONTENT_INTEGRITY_UNAVAILABLE"); }
                ValidateInspection(inspection);
            }
            var saved = await store.Append(command, a.ActorId, inspection, command.Action == "HOLD" || command.Action == "INSPECT" && current.Held, token);
            return await SaveResult(journal, command.OperationId, body, saved, a.ActorId, token);
        }, ct, actionAtFence: async token => { original = await journal.Read(FileContentBytesV1.Owner, command.OperationId, token); return original == null ? "edit" : "query"; });
    }
    public async Task<FileContentDownloadV1> DownloadAsync(Guid id, string version, string digest, CancellationToken ct = default)
    {
        if (id == Guid.Empty || version != "1" || digest?.Length != 64) throw Error("FILE_REFERENCE_INVALID", 400);
        var outcome = await Authority.ExecuteAsync<FileContentDownloadV1?>(OperationsOwnerV1.FileContent, "query", true, async (a, token) =>
        {
            return await DownloadWithin(a,id,digest,token);
        }, ct);
        return outcome ?? throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE", 503);
    }
    internal async Task<FileContentSnapshotV1> ReadOwnedAsync(OperationsAccessV1 root, Guid id, CancellationToken ct)
    {
        var a=await root.SourceAsync(OperationsOwnerV1.FileContent,ct);var store=new FileContentStoreV1(db);await store.Fence(false,ct);
        return await Observe(a,await store.Read(id,null,ct),ct);
    }
    internal async Task<FileContentSnapshotV1> BoundSource(OperationsAccessV1 root,FileContentRefV1 reference,Guid parent,bool requireAvailable,CancellationToken ct)
    {
        var value=await ReadOwnedAsync(root,reference.Id,ct);
        if(value.ContentRef!=reference || value.Parent.Id!=parent)throw Error("DOCUMENT_FILE_REFERENCE_CHANGED",412);
        if(requireAvailable)
        {
            if(value.Availability!="AVAILABLE")throw Error("FILE_CONTENT_NOT_AVAILABLE",409);
            var row=await new FileContentStoreV1(db).Row(reference.Id,ct);await VerifyStored(row.StorePath,row.ByteLength,row.Digest,ct);
        }
        return value;
    }
    internal async Task<FileContentDownloadV1?> BoundDownload(OperationsAccessV1 root,FileContentRefV1 reference,Guid parent,CancellationToken ct)
    {
        await BoundSource(root,reference,parent,false,ct);
        return await DownloadWithin(await root.SourceAsync(OperationsOwnerV1.FileContent,ct),reference.Id,reference.Digest,ct);
    }
    private async Task<FileContentDownloadV1?> DownloadWithin(OperationsAccessV1 a,Guid id,string digest,CancellationToken token)
    {
            var store = new FileContentStoreV1(db); await store.Fence(true, token); await a.RequireFileContent(id, token);
            var current = await Observe(a, await store.Read(id, null, token), token);
            if (current.ContentRef.Digest != digest) throw Error("FILE_REFERENCE_CHANGED", 412);
            if (current.Availability != "AVAILABLE") throw Error("FILE_CONTENT_NOT_AVAILABLE", 409);
            var row = await store.Row(id, token); byte[] bytes;
            try { bytes = await VerifyStored(row.StorePath, row.ByteLength, row.Digest, token); }
            catch (CP6.Core.Services.Erp.OrderPriceCorrectionException e) when (e.Message == "FILE_CONTENT_INTEGRITY_UNAVAILABLE")
            {
                // A server-observed integrity failure quarantines this exact content. Query authority cannot release it.
                var command = new FileContentCommandV1(Guid.NewGuid(), id, "INTEGRITY_FAILURE", current.StateVersion, current.Etag, "Exact stored content failed verification");
                var saved = await store.Append(command, a.ActorId, new("NOT_CLEAN", "ContentIntegrity", null, null, DateTimeOffset.UtcNow, null, "FILE_CONTENT_INTEGRITY_UNAVAILABLE"), current.Held, token);
                await SaveResult(new MainOwnerOperationJournalV1(db), command.OperationId, MainOwnerOperationJournalV1.Encode(command), saved, a.ActorId, token);
                return null;
            }
            // Bytes are returned only after ExecuteAsync completes its final current authority fence.
            return new FileContentDownloadV1(bytes, current.ContentRef, current.FileName);
    }
    private async Task<byte[]> VerifyStored(string path, long size, string digest, CancellationToken ct)
    {
        try
        {
            await using var stream = await files.OpenReadBoundedAsync(path, size, ct); var bytes = await FileContentBytesV1.Read(stream, size, ct);
            if (FileContentBytesV1.Digest(bytes) != digest) throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE", 503);
            return bytes;
        }
        catch (IOException) { throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE", 503); }
        catch (NotSupportedException) { throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE", 503); }
        catch (CP6.Core.Services.Erp.OrderPriceCorrectionException e) when (e.Message is "FILE_LENGTH_MISMATCH" or "FILE_SIZE_LIMIT")
        { throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE", 503); }
    }
    private static async Task<FileContentResultV1> SaveResult(MainOwnerOperationJournalV1 journal, Guid operation, byte[] body,
        FileContentSnapshotV1 saved, Guid actor, CancellationToken ct)
    {
        var result = new FileContentResultV1(operation, "FILE_CONTENT", saved.ContentRef.Id, saved.StateVersion, saved.OriginalDigest);
        await journal.Store(FileContentBytesV1.Owner, Contract, operation, actor, body, result, [saved.ContentRef.Id],
            new { saved.Action, saved.Reason, actor, result, saved.ContentRef, saved.Parent }, ct); return result;
    }
    private async Task<FileContentResultV1> Disclose(OperationsAccessV1 a, FileContentStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid id, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<FileContentResultV1>(original.Result);
        if (result == null || result.OperationId != id || result.Kind != "FILE_CONTENT" || !original.Objects.SequenceEqual(new[] { result.ObjectId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var saved = await Observe(a, await store.Read(result.ObjectId, Revision(result.Version), ct), ct);
        if (saved.OriginalDigest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503); return result;
    }
    public Task<FileContentOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "query", close, async (a, token) =>
        {
            a.RequireFileFields(false); var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(FileContentBytesV1.Owner, id, token);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                if (original.State == "CLOSED") return new FileContentOperationV1(id, "CLOSED");
                var store = new FileContentStoreV1(db); await store.Fence(false, token);
                return new FileContentOperationV1(id, "COMMITTED", await Disclose(a, store, original, id, null, token));
            }
            if (!close) return new FileContentOperationV1(id, "UNKNOWN");
            var result = new FileContentOperationV1(id, "CLOSED");
            await journal.Store(FileContentBytesV1.Owner, Contract, id, a.ActorId, null, result, [], new { action = "close-uncommitted-file-content", actor = a.ActorId }, token); return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
