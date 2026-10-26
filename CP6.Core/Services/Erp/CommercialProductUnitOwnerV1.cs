using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Erp.UomProfileValuesV1;

namespace CP6.Core.Services.Erp;

/// <summary>Explicit adoption of the original native product head and unit profile, on both current Owner scopes.</summary>
public sealed class CommercialProductUnitOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string BindingOwner = "Main.Erp.ProductUnit.v1", Contract = "Main.Erp.ProductUnit.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<UomProfileScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.Product, "query", false, (access, _) => Task.FromResult(new UomProfileScopeV1(db.CurrentTenantId, access.ActorId)),
        ct, readOnlyAuthorityFence: true);

    public Task<CommercialProductUnitChoiceV1[]> ChoicesAsync(string search, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Length > 100) throw Error("PRODUCT_UNIT_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.Product, "query", false, async (access, token) =>
        {
            var rows = await access.Products().Where(row => row.ProductCd.Contains(search)).OrderBy(row => row.ProductCd)
                .ThenBy(row => row.Id).Take(50).Select(row => new { row.Id, row.ProductCd }).ToArrayAsync(token);
            var result = new List<CommercialProductUnitChoiceV1>();
            foreach (var row in rows)
            {
                var source = await CommercialProductIdentityReader.ReadOwnedAsync(db, access, row.Id, null, token);
                result.Add(new(row.Id, row.ProductCd, source.Version, source.Source.GetProperty("qtyUnitCode").GetString()));
            }
            return result.ToArray();
        }, ct);
    }
    private static async Task RequireProduct(OperationsAccessV1 access, Guid id, CancellationToken ct)
    {
        if (!await access.Products(includeDeleted: true).AnyAsync(row => row.Id == id, ct)) throw Error("OBJECT_FORBIDDEN", 403);
    }
    // An editor can observe a stale binding head for CAS repair. This endpoint never attests adoption.
    public Task<CommercialProductUnitEditorV1> EditorAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("PRODUCT_UNIT_COMMAND_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.Product, "query", false, async (access, token) =>
        {
            var store = new UomProfileStoreV1(db); await store.Fence(false, token);
            var product = await CommercialProductIdentityReader.ReadOwnedAsync(db, access, id, null, token);
            var units = await access.SourceAsync(OperationsOwnerV1.UomProfile, token);
            var head = await store.ProductHead(id, token); UomProfileRefV1? bound = null;
            if (head != null)
            {
                var original = await store.ReadProduct(id, head, token);
                await units.RequireUomProfile(original.Value.ProfileRef.Id, token); bound = original.Value.ProfileRef;
                units.AtCommit(last => units.RequireUomProfile(original.Value.ProfileRef.Id, last));
            }
            access.AtCommit(last => RequireProduct(access, id, last));
            return new CommercialProductUnitEditorV1(id, product.Version, product.Source.GetProperty("qtyUnitCode").GetString(),
                !product.Source.GetProperty("isDeleted").GetBoolean(), head?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "absent", bound);
        }, ct);
    }
    public Task<UomProfileCommandResultV1> BindAsync(CommercialProductUnitCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.ProductId == Guid.Empty) throw Error("PRODUCT_UNIT_COMMAND_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input); Reference(frozen.ProfileRef); Revision(frozen.ProductVersion);
        var expected = frozen.ExpectedBindingVersion == "absent" ? (long?)null : Revision(frozen.ExpectedBindingVersion);
        var bytes = MainOwnerOperationJournalV1.Encode(frozen); var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Product, "edit", true, async (access, token) =>
        {
            var store = new UomProfileStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(access, store, original, frozen.OperationId, bytes, token);
            var product = await CommercialProductIdentityReader.ReadOwnedAsync(db, access, frozen.ProductId, null, token);
            if (product.Version != frozen.ProductVersion) throw Error("PRODUCT_UNIT_SOURCE_CHANGED", 412);
            if (product.Source.GetProperty("isDeleted").GetBoolean()) throw Error("PRODUCT_UNIT_SOURCE_UNAVAILABLE");
            var units = await access.SourceAsync(OperationsOwnerV1.UomProfile, token);
            var profile = await UomProfileOwnerV1.CurrentExact(units, store, frozen.ProfileRef!, token);
            await UomProfileOwnerV1.CurrentBase(units, store, profile, token);
            var code = product.Source.GetProperty("qtyUnitCode").GetString();
            if (string.IsNullOrEmpty(code) || code != profile.Profile.Code) throw Error("PRODUCT_UNIT_CODE_MISMATCH");
            var saved = await store.AppendProduct(product, profile, expected, access.ActorId, token);
            access.AtCommit(last => RequireProduct(access, frozen.ProductId, last));
            var result = new UomProfileCommandResultV1(frozen.OperationId, "PRODUCT_UNIT", frozen.ProductId, saved.Value.Version, saved.Digest);
            await journal.Store(BindingOwner, Contract, frozen.OperationId, access.ActorId, bytes, result, [frozen.ProductId, profile.Reference.Id],
                new { action = "bind-original-product-unit", actor = access.ActorId, product = frozen.ProductId, productVersion = product.Version,
                    profile = profile.Reference, bindingVersion = result.Version }, token);
            return result;
        }, ct, actionAtFence: async token =>
        { original = await journal.Read(BindingOwner, frozen.OperationId, token); return original == null ? "edit" : "query"; });
    }

    private async Task<UomProfileCommandResultV1> Disclose(OperationsAccessV1 access, UomProfileStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid operation, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<UomProfileCommandResultV1>(original.Result);
        if (result == null || result.Kind != "PRODUCT_UNIT" || result.OperationId != operation || original.Objects.Length != 2
            || original.Objects[0] != result.ObjectId) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        await RequireProduct(access, result.ObjectId, ct);
        var saved = await store.ReadProduct(result.ObjectId, Revision(result.Version), ct);
        if (saved.Digest != result.Digest || saved.Value.ProfileRef.Id != original.Objects[1]) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var units = await access.SourceAsync(OperationsOwnerV1.UomProfile, ct);
        await units.RequireUomProfile(saved.Value.ProfileRef.Id, ct);
        access.AtCommit(token => RequireProduct(access, result.ObjectId, token));
        units.AtCommit(token => units.RequireUomProfile(saved.Value.ProfileRef.Id, token));
        return result;
    }

    public Task<CommercialProductUnitVersionV1> ReadAsync(Guid id, string? version, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("PRODUCT_UNIT_COMMAND_INVALID", 400);
        var selected = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.Product, "query", false, async (access, token) =>
        {
            var store = new UomProfileStoreV1(db); await store.Fence(false, token);
            var product = await CommercialProductIdentityReader.ReadOwnedAsync(db, access, id, null, token);
            var saved = await store.ReadProduct(id, selected, token); var original = saved.Value;
            var units = await access.SourceAsync(OperationsOwnerV1.UomProfile, token);
            await units.RequireUomProfile(original.ProfileRef.Id, token);
            var historicalProfile = await store.Read(original.ProfileRef.Id, Revision(original.ProfileRef.Version), token);
            if (historicalProfile.Reference != original.ProfileRef || historicalProfile.Profile.Code != original.UnitCode)
                throw Error("PRODUCT_UNIT_ORIGINAL_UNAVAILABLE", 503);
            var reasons = new List<string>();
            if (product.Version != original.ProductVersion || product.Digest != original.ProductDigest
                || product.Source.GetProperty("isDeleted").GetBoolean() || product.Source.GetProperty("qtyUnitCode").GetString() != original.UnitCode)
                reasons.Add("PRODUCT_UNIT_SOURCE_CHANGED");
            try
            {
                var current = await UomProfileOwnerV1.CurrentExact(units, store, original.ProfileRef, token);
                await UomProfileOwnerV1.CurrentBase(units, store, current, token);
            }
            catch (OrderPriceCorrectionException error) when (error.Status is 412 or 422) { reasons.Add(error.Message); }
            if (!saved.Latest) reasons.Add("PRODUCT_UNIT_BINDING_CHANGED");
            if (selected == null && reasons.Count != 0) throw Error("PRODUCT_UNIT_BINDING_CHANGED", 412);
            access.AtCommit(last => RequireProduct(access, id, last));
            units.AtCommit(last => units.RequireUomProfile(original.ProfileRef.Id, last));
            return new CommercialProductUnitVersionV1(db.CurrentTenantId, id, original.Version, saved.Digest, original.ProductVersion,
                original.ProductDigest, original.UnitCode, original.ProfileRef, saved.Latest, reasons.Count == 0, reasons.ToArray(),
                saved.OriginalJson, Missing: ["CURRENCY_IDENTITY", "DUAL_OWNER_ENGINEERING_MAPPING"]);
        }, ct);
    }

    public Task<UomProfileOperationV1> OperationAsync(Guid operation, bool close, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.Product, "query", close, async (access, token) =>
        {
            var store = new UomProfileStoreV1(db); await store.Fence(close, token);
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(BindingOwner, operation, token);
            if (original != null)
            {
                if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(operation, "CLOSED") :
                    new UomProfileOperationV1(operation, "COMMITTED", await Disclose(access, store, original, operation, null, token));
            }
            if (!close) return new UomProfileOperationV1(operation, "UNKNOWN");
            var result = new UomProfileOperationV1(operation, "CLOSED");
            await journal.Store(BindingOwner, Contract, operation, access.ActorId, null, result, [], new { action = "close-uncommitted-product-unit", actor = access.ActorId }, token);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
