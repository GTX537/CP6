using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
public sealed class ToolSpecificationOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Plm.ToolSpecification.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<EngineeringProductScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.ToolSpecification, "query", false, (a, _) => Task.FromResult(new EngineeringProductScopeV1(db.CurrentTenantId, a.ActorId)), ct, readOnlyAuthorityFence: true);
    private async Task<(OperationsAccessV1 Native, PlateMoldSourceStoreV1 Source, ToolSpecificationStoreV1 Store)> Stores(OperationsAccessV1 access, bool write, CancellationToken ct)
    {
        var query = await access.SourceAsync(OperationsOwnerV1.PlateMold, ct);
        var source = new PlateMoldSourceStoreV1(db); await source.Fence(ct);
        var store = new ToolSpecificationStoreV1(db); await store.Fence(write, ct);
        return (query, source, store);
    }
    private async Task<bool> Product(OperationsAccessV1 a, EngineeringProductRefV1 reference, bool current, CancellationToken ct)
    {
        var query = await a.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct);
        var store = new EngineeringProductStoreV1(db); await store.Fence(false, ct);
        if (current) { await EngineeringProductOwnerV1.CurrentExact(query, store, reference, ct); return true; }
        Reference(reference); await query.RequireEngineeringProduct(reference.Id, ct); query.AtCommit(t => query.RequireEngineeringProduct(reference.Id, t));
        var original = await store.Read(reference.Id, Revision(reference.Version), ct);
        if (original.Reference != reference) throw Error("TOOL_PRODUCT_ORIGINAL_UNAVAILABLE", 503);
        return original.IsCurrent && original.Product.Enabled == true;
    }
    private async Task<ToolSpecificationViewV1> Observe(OperationsAccessV1 a, OperationsAccessV1 native, PlateMoldSourceStoreV1 source,
        ToolSpecificationStoreV1 store, Guid id, long? version, CancellationToken ct)
    {
        await a.RequireSpecificationTool(id, ct);
        var observed = await source.Read(native, id, null, ct); var control = await store.Control(observed.Reference, ct);
        var draft = await store.Read(id, version, ct); var productMatches = false;
        if (draft != null)
        {
            var original = await source.Read(native, id, Revision(draft.Source.Version), ct);
            if (original.Reference != draft.Source) throw Error("TOOL_SPECIFICATION_ORIGINAL_UNAVAILABLE", 503);
            productMatches = await Product(a, draft.Requirements.ProductRef, false, ct);
        }
        return new(observed, control, draft, draft == null || control.Version == draft.Reference.Version,
            draft != null && draft.Source == observed.Reference && !observed.Source.GetProperty("control").GetProperty("isDeleted").GetBoolean(), productMatches);
    }
    public Task<ToolSpecificationViewV1[]> ListAsync(string? search = null, int page = 1, CancellationToken ct = default)
    {
        if (search?.Length > 100 || page is < 1 or > 100000) throw Error("TOOL_SPECIFICATION_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolSpecification, "query", false, async (a, token) =>
        {
            var (native, source, store) = await Stores(a, false, token);
            var nativeIds = native.PlateMolds().Select(p => p.Id);
            var ids = await a.SpecificationTools().Where(p => nativeIds.Contains(p.Id) && (search == null || p.BaseCd.Contains(search) || p.WdPtnNo.Contains(search) || p.VrsnName.Contains(search)))
                .OrderBy(p => p.BaseCd).ThenBy(p => p.WdPtnNo).ThenBy(p => p.WdRev).ThenBy(p => p.Id).Skip((page - 1) * 20).Take(20).Select(p => p.Id).ToArrayAsync(token);
            var result = new List<ToolSpecificationViewV1>();
            foreach (var id in ids) result.Add(await Observe(a, native, source, store, id, null, token));
            return result.ToArray();
        }, ct);
    }
    public Task<ToolSpecificationViewV1> ReadAsync(Guid id, string? version = null, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("TOOL_IDENTITY_REQUIRED", 400);
        var exact = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolSpecification, "query", false, async (a, token) =>
        { var (native, source, store) = await Stores(a, false, token); return await Observe(a, native, source, store, id, exact, token); }, ct);
    }
    public Task<ToolSpecificationResultV1> CommandAsync(ToolSpecificationCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.Source == null || input.Source.Id == Guid.Empty || input.ExpectedTechnicalControl == null
            || string.IsNullOrWhiteSpace(input.ExpectedNativeRowVersion) || input.Action is not ("CREATE" or "REVISE")) throw Error("TOOL_SPECIFICATION_COMMAND_INVALID", 400);
        var command = OperationsOwnerV1.Freeze(input); ToolSpecificationStoreV1.Validate(command.Requirements, command.Reason);
        var bytes = MainOwnerOperationJournalV1.Encode(command); var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        var action = command.Action == "CREATE" ? "add" : "edit";
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolSpecification, action, true, async (a, token) =>
        {
            var (native, source, store) = await Stores(a, true, token);
            if (original != null) return await Disclose(a, native, source, store, original, command.OperationId, bytes, token);
            await a.RequireSpecificationTool(command.Source.Id, token);
            var query = await a.SourceAsync(OperationsOwnerV1.ToolSpecification, token); await query.RequireSpecificationTool(command.Source.Id, token);
            var current = await source.CurrentExact(native, command.Source, token);
            if (current.CurrentNativeRowVersion != command.ExpectedNativeRowVersion) throw Error("TOOL_NATIVE_VERSION_CHANGED", 412);
            var control = await store.Control(current.Reference, token);
            if (control != command.ExpectedTechnicalControl) throw Error("TECHNICAL_CONTROL_CHANGED", 412);
            var exists = control.Knowledge == "TECHNICAL_SPECIFICATION_DRAFT";
            if ((command.Action == "CREATE") == exists) throw Error("TOOL_SPECIFICATION_STATE_CHANGED", 409);
            await Product(a, command.Requirements.ProductRef, true, token);
            var saved = await store.Append(command, a.ActorId, exists ? Revision(control.Version) : null, token);
            var result = new ToolSpecificationResultV1(command.OperationId, "TOOL_SPECIFICATION_DRAFT", command.Source.Id, saved.Reference.Version, saved.Reference.Digest);
            await journal.Store(ToolSpecificationStoreV1.Owner, Contract, command.OperationId, a.ActorId, bytes, result, [command.Source.Id],
                new { action = command.Action, actor = a.ActorId, command.Reason, source = command.Source, result }, token);
            return result;
        }, ct, actionAtFence: async token => { original = await journal.Read(ToolSpecificationStoreV1.Owner, command.OperationId, token); return original == null ? action : "query"; });
    }
    private async Task<ToolSpecificationResultV1> Disclose(OperationsAccessV1 a, OperationsAccessV1 native, PlateMoldSourceStoreV1 source, ToolSpecificationStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid id, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<ToolSpecificationResultV1>(original.Result);
        if (result == null || result.OperationId != id || result.Kind != "TOOL_SPECIFICATION_DRAFT" || !original.Objects.SequenceEqual(new[] { result.ObjectId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var saved = (await Observe(a, native, source, store, result.ObjectId, Revision(result.Version), ct)).Specification;
        if (saved?.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        return result;
    }
    public Task<ToolSpecificationOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolSpecification, "query", close, async (a, token) =>
        {
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(ToolSpecificationStoreV1.Owner, id, token);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                if (original.State == "CLOSED") return new ToolSpecificationOperationV1(id, "CLOSED");
                var (native, source, store) = await Stores(a, false, token);
                return new ToolSpecificationOperationV1(id, "COMMITTED", await Disclose(a, native, source, store, original, id, null, token));
            }
            if (!close) return new ToolSpecificationOperationV1(id, "UNKNOWN");
            var result = new ToolSpecificationOperationV1(id, "CLOSED");
            await journal.Store(ToolSpecificationStoreV1.Owner, Contract, id, a.ActorId, null, result, [], new { action = "close-uncommitted-tool-specification", actor = a.ActorId }, token);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
