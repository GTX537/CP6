using CP6.Core.Services.Erp;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed class EngineeringProductOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Plm.EngineeringProduct.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<EngineeringProductScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.EngineeringProduct, "query", false, (access, _) => Task.FromResult(new EngineeringProductScopeV1(db.CurrentTenantId, access.ActorId)),
        ct, readOnlyAuthorityFence: true);
    public Task<EngineeringProductCreationContextV1> CreationContextAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.EngineeringProduct, "add", false, async (access, token) =>
        {
            await new EngineeringProductStoreV1(db).Fence(false, token);
            return await access.EngineeringCreationContext(token);
        }, ct, readOnlyAuthorityFence: true);

    public Task<EngineeringProductVersionV1[]> ListAsync(string? search, int page = 1, int size = 20, CancellationToken ct = default)
    {
        if (search?.Length > 256 || page < 1 || size is < 1 or > 100 || page > int.MaxValue / size) throw Error("ENGINEERING_PRODUCT_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringProduct, "query", false, async (access, token) =>
        {
            var store = new EngineeringProductStoreV1(db); await store.Fence(false, token);
            var rows = access.EngineeringProducts();
            if (!string.IsNullOrEmpty(search)) rows = rows.Where(row => row.Code.Contains(search));
            var ids = await rows.OrderBy(row => row.Code).ThenBy(row => row.Id).Skip((page - 1) * size).Take(size).Select(row => row.Id).ToArrayAsync(token);
            var result = new List<EngineeringProductVersionV1>();
            foreach (var id in ids)
            {
                result.Add(await store.Read(id, null, token)); access.AtCommit(last => access.RequireEngineeringProduct(id, last));
            }
            return result.ToArray();
        }, ct);
    }
    public Task<EngineeringProductVersionV1> ReadAsync(Guid id, string? version, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ENGINEERING_PRODUCT_IDENTITY_INVALID", 400);
        var selected = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringProduct, "query", false, async (access, token) =>
        {
            var store = new EngineeringProductStoreV1(db); await store.Fence(false, token);
            await access.RequireEngineeringProduct(id, token); access.AtCommit(last => access.RequireEngineeringProduct(id, last));
            return await store.Read(id, selected, token);
        }, ct);
    }

    // Caller must already hold the actual engineering-store fence in its own Main Owner transaction.
    internal static async Task<EngineeringProductVersionV1> CurrentExact(OperationsAccessV1 access, EngineeringProductStoreV1 store,
        EngineeringProductRefV1 reference, CancellationToken ct)
    {
        access.RequireEngineeringSourceAccess(store.Context); Reference(reference); await access.RequireEngineeringProduct(reference.Id, ct);
        var current = await store.Read(reference.Id, null, ct);
        if (current.Reference != reference) throw Error("ENGINEERING_PRODUCT_CHANGED", 412);
        if (current.Product.Enabled != true) throw Error("ENGINEERING_PRODUCT_DISABLED");
        access.AtCommit(token => access.RequireEngineeringProduct(reference.Id, token));
        return current;
    }
    public Task<EngineeringProductCommandResultV1> CommandAsync(EngineeringProductCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.ProductId == Guid.Empty || input.Product == null
            || input.Action is not ("CREATE" or "REVISE")) throw Error("ENGINEERING_PRODUCT_COMMAND_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input);
        var bytes = MainOwnerOperationJournalV1.Encode(frozen);
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringProduct, frozen.Action == "CREATE" ? "add" : "edit", true, async (access, token) =>
        {
            var store = new EngineeringProductStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(access, original, frozen.OperationId, bytes, token);
            var product = Validate(frozen.Product!);
            OperationsAccessV1.NewEngineeringBinding? creation = null;
            long? expected = null;
            if (frozen.Action == "CREATE")
            {
                if (frozen.ExpectedVersion != null) throw Error("ENGINEERING_PRODUCT_VERSION_INVALID", 400);
                creation = await access.BeginEngineeringCreation(frozen.ProductId, frozen.ExpectedAuthorityVersion, frozen.DepartmentId, token);
            }
            else
            {
                if (frozen.ExpectedAuthorityVersion != null || frozen.DepartmentId != null) throw Error("ENGINEERING_PRODUCT_COMMAND_INVALID", 400);
                expected = Revision(frozen.ExpectedVersion); await access.RequireEngineeringProduct(frozen.ProductId, token);
            }
            var saved = await store.Append(frozen.ProductId, access.ActorId, product, expected, token);
            if (creation != null) await access.CompleteEngineeringCreation(creation, token);
            else access.AtCommit(last => access.RequireEngineeringProduct(frozen.ProductId, last));
            var result = new EngineeringProductCommandResultV1(frozen.OperationId, "ENGINEERING_PRODUCT", frozen.ProductId, saved.Reference.Version, saved.Reference.Digest);
            await journal.Store(Owner, Contract, frozen.OperationId, access.ActorId, bytes, result, [frozen.ProductId],
                new { action = frozen.Action, actor = access.ActorId, product = frozen.ProductId, previousVersion = expected, result }, token);
            return result;
        }, ct, actionAtFence: async token =>
        {
            original = await journal.Read(Owner, frozen.OperationId, token);
            return original == null ? frozen.Action == "CREATE" ? "add" : "edit" : "query";
        });
    }

    private static async Task<EngineeringProductCommandResultV1> Disclose(OperationsAccessV1 access,
        MainOwnerOperationJournalV1.Original original, Guid operation, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<EngineeringProductCommandResultV1>(original.Result);
        if (result == null || result.Kind != "ENGINEERING_PRODUCT" || result.OperationId != operation
            || !original.Objects.SequenceEqual(new[] { result.ObjectId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        await access.RequireEngineeringProduct(result.ObjectId, ct); access.AtCommit(token => access.RequireEngineeringProduct(result.ObjectId, token));
        return result;
    }
    public Task<EngineeringProductOperationV1> OperationAsync(Guid operation, bool close, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringProduct, "query", close, async (access, token) =>
        {
            await new EngineeringProductStoreV1(db).Fence(close, token);
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(Owner, operation, token);
            if (original != null)
            {
                if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(operation, "CLOSED") :
                    new EngineeringProductOperationV1(operation, "COMMITTED", await Disclose(access, original, operation, null, token));
            }
            if (!close) return new EngineeringProductOperationV1(operation, "UNKNOWN");
            var result = new EngineeringProductOperationV1(operation, "CLOSED");
            await journal.Store(Owner, Contract, operation, access.ActorId, null, result, [], new { action = "close-uncommitted-engineering", actor = access.ActorId }, token);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
