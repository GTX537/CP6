using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.ResourceSourceStoreV1;

namespace CP6.Core.Services.Mes;

/// <summary>Ordinary edits of the single original master, not formal capability/assignment/production admission.</summary>
public sealed class ResourceSourceOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Mes.ResourceSource.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static string JournalOwner(string kind) => Owner + "." + kind;
    public Task<ResourceSourceScopeV1> ScopeAsync(string kind, CancellationToken ct = default) => Authority.ExecuteAsync(
        Resource(kind), "query", false, (a, _) => Task.FromResult(new ResourceSourceScopeV1(db.CurrentTenantId, a.ActorId)), ct, readOnlyAuthorityFence: true);

    public Task<ResourceSourceVersionV1[]> ListAsync(string kind, string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (search?.Length > 100 || page is < 1 or > 100000 || pageSize is not (20 or 50 or 100)) throw Error("RESOURCE_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(Resource(kind), "query", false, async (a, token) =>
        {
            var store = new ResourceSourceStoreV1(db); await store.Fence(false, token);
            var ids = kind == "MACHINE"
                ? await a.Machines().Where(r => search == null || r.MachineCd.Contains(search) || r.MachineName.Contains(search))
                    .OrderBy(r => r.MachineCd).ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(r => r.Id).ToArrayAsync(token)
                : await a.WorkCenters().Where(r => search == null || r.WgCd.Contains(search) || r.WgName != null && r.WgName.Contains(search))
                    .OrderBy(r => r.WgCd).ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(r => r.Id).ToArrayAsync(token);
            var rows = new List<ResourceSourceVersionV1>();
            foreach (var id in ids)
            {
                rows.Add(await store.Read(a, kind, id, null, token)); a.AtCommit(last => a.RequireResourceSource(db, kind, id, last));
            }
            return rows.ToArray();
        }, ct);
    }
    public Task<ResourceSourceVersionV1> ReadAsync(string kind, Guid id, string? version = null, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("RESOURCE_ID_INVALID", 400);
        var exact = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(Resource(kind), "query", false, async (a, token) =>
        {
            var store = new ResourceSourceStoreV1(db); await store.Fence(false, token);
            var result = await store.Read(a, kind, id, exact, token);
            a.AtCommit(last => a.RequireResourceSource(db, kind, id, last)); return result;
        }, ct);
    }
    private static void Validate(string kind, ResourceSourceCommandV1 input)
    {
        if (input == null || input.OperationId == Guid.Empty || input.Expected == null || input.Expected.Owner != Owner
            || input.Expected.Kind != kind || input.Expected.Id == Guid.Empty || input.Expected.Digest == null
            || input.Expected.Digest.Length != 64 || input.Expected.Digest.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            || input.Patches == null || input.Patches.Length is < 1 or > 7 || input.Patches.Any(p => p == null)
            || input.Patches.Select(p => p.Field).Distinct(StringComparer.Ordinal).Count() != input.Patches.Length)
            throw Error("RESOURCE_COMMAND_INVALID", 400);
        Revision(input.Expected.Version);
        foreach (var patch in input.Patches)
        {
            var limit = patch.Field switch { "name" => 100, "manufacturer" or "model" when kind == "MACHINE" => 50,
                "remarks" when kind == "MACHINE" => 500,
                "installDate" or "lastMaintenanceDate" or "nextMaintenanceDate" when kind == "MACHINE" => 10,
                _ => throw Error("RESOURCE_PATCH_FORBIDDEN", 400) };
            if (patch.Value?.Length > limit || patch.Field == "name" && kind == "MACHINE" && string.IsNullOrWhiteSpace(patch.Value))
                throw Error("RESOURCE_PATCH_INVALID", 400);
            if (patch.Field.EndsWith("Date", StringComparison.Ordinal)) Date(patch.Value);
        }
    }
    private static DateTime? Date(string? text) => text == null ? null : DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var parsed) && parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == text
        ? parsed : throw Error("RESOURCE_DATE_INVALID", 400);

    public Task<ResourceSourceResultV1> SaveAsync(string kind, ResourceSourceCommandV1 input, CancellationToken ct = default)
    {
        Resource(kind);
        if (input == null) throw Error("RESOURCE_COMMAND_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input); Validate(kind, frozen);
        var bytes = MainOwnerOperationJournalV1.Encode(frozen); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(Resource(kind), "edit", true, async (a, token) =>
        {
            var store = new ResourceSourceStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(a, store, kind, frozen.OperationId, original, bytes, token);
            var before = await store.Read(a, kind, frozen.Expected.Id, null, token);
            if (before.Reference != frozen.Expected) throw Error("RESOURCE_SOURCE_CHANGED", 412);
            if (before.Source.GetProperty("control").GetProperty("isDeleted").GetBoolean()) throw Error("RESOURCE_SOURCE_DELETED");
            if (kind == "MACHINE")
            {
                var row = await db.Machines.SingleAsync(r => r.Id == frozen.Expected.Id, token);
                foreach (var patch in frozen.Patches) switch (patch.Field)
                {
                    case "name": row.MachineName = patch.Value!; break;
                    case "manufacturer": row.Manufacturer = patch.Value; break;
                    case "model": row.Model = patch.Value; break;
                    case "remarks": row.Remarks = patch.Value; break;
                    case "installDate": row.InstallDate = Date(patch.Value); break;
                    case "lastMaintenanceDate": row.LastMaintenanceDate = Date(patch.Value); break;
                    case "nextMaintenanceDate": row.NextMaintenanceDate = Date(patch.Value); break;
                }
                if (db.ChangeTracker.HasChanges()) { row.Modifier = a.Actor; row.ModifyDate = DateTime.Now; }
            }
            else
            {
                var row = await db.WorkCenters.SingleAsync(r => r.Id == frozen.Expected.Id, token);
                row.WgName = frozen.Patches.Single().Value;
                if (db.ChangeTracker.HasChanges()) { row.Modifier = a.Actor; row.ModifyDate = DateTime.Now; }
            }
            await db.SaveChangesAsync(token);
            var saved = await store.Read(a, kind, frozen.Expected.Id, null, token);
            var result = new ResourceSourceResultV1(frozen.OperationId, kind, saved.Reference.Id, saved.Reference.Version,
                saved.Reference.Digest, saved.Reference != before.Reference);
            a.AtCommit(last => a.RequireResourceSource(db, kind, saved.Reference.Id, last));
            await journal.Store(JournalOwner(kind), Contract, frozen.OperationId, a.ActorId, bytes, result, [result.ObjectId],
                new { action = "ordinary-resource-master-edit", actor = a.ActorId, before = before.Reference, after = saved.Reference,
                    fields = frozen.Patches.Select(p => p.Field).ToArray(), result.Changed }, token);
            return result;
        }, ct, actionAtFence: async token =>
        { original = await journal.Read(JournalOwner(kind), frozen.OperationId, token); return original == null ? "edit" : "query"; });
    }
    private async Task<ResourceSourceResultV1> Disclose(OperationsAccessV1 a, ResourceSourceStoreV1 store, string kind, Guid id,
        MainOwnerOperationJournalV1.Original original, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<ResourceSourceResultV1>(original.Result);
        if (result == null || result.OperationId != id || result.Kind != kind || original.Objects.Length != 1 || original.Objects[0] != result.ObjectId)
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var source = await store.Read(a, kind, result.ObjectId, Revision(result.Version), ct);
        if (source.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        a.AtCommit(last => a.RequireResourceSource(db, kind, result.ObjectId, last)); return result;
    }
    public Task<ResourceSourceOperationV1> OperationAsync(string kind, Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(Resource(kind), "query", close, async (a, token) =>
        {
            var store = new ResourceSourceStoreV1(db); await store.Fence(close, token);
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(JournalOwner(kind), id, token);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(id, "CLOSED") :
                    new ResourceSourceOperationV1(id, "COMMITTED", await Disclose(a, store, kind, id, original, null, token));
            }
            if (!close) return new ResourceSourceOperationV1(id, "UNKNOWN");
            var result = new ResourceSourceOperationV1(id, "CLOSED");
            await journal.Store(JournalOwner(kind), Contract, id, a.ActorId, null, result, [],
                new { action = "close-uncommitted-resource-edit", actor = a.ActorId, kind }, token); return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
