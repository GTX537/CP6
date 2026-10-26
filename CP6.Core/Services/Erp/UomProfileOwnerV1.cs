using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Erp.UomProfileValuesV1;

namespace CP6.Core.Services.Erp;

public sealed class UomProfileOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Erp.UomProfile.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<UomProfileScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.UomProfile, "query", false, (access, _) => Task.FromResult(new UomProfileScopeV1(db.CurrentTenantId, access.ActorId)),
        ct, readOnlyAuthorityFence: true);
    public Task<UomProfileCreationContextV1> CreationContextAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.UomProfile, "add", false, async (access, token) =>
        {
            await new UomProfileStoreV1(db).Fence(false, token);
            return await access.UomCreationContext(token);
        }, ct, readOnlyAuthorityFence: true);

    public Task<UomProfileVersionV1[]> ListAsync(string? search, int page = 1, int size = 20, CancellationToken ct = default)
    {
        if (search?.Length > 128 || page < 1 || size is < 1 or > 100 || page > int.MaxValue / size) throw Error("UOM_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.UomProfile, "query", false, async (access, token) =>
        {
            var store = new UomProfileStoreV1(db); await store.Fence(false, token);
            var rows = access.UomProfiles();
            if (!string.IsNullOrEmpty(search)) rows = rows.Where(row => row.Code.Contains(search));
            var ids = await rows.OrderBy(row => row.Code).ThenBy(row => row.Id).Skip((page - 1) * size).Take(size).Select(row => row.Id).ToArrayAsync(token);
            var result = new List<UomProfileVersionV1>();
            foreach (var id in ids)
            {
                result.Add(await store.Read(id, null, token)); access.AtCommit(last => access.RequireUomProfile(id, last));
            }
            return result.ToArray();
        }, ct);
    }
    public Task<UomProfileVersionV1> ReadAsync(Guid id, string? version, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("UOM_IDENTITY_INVALID", 400);
        var selected = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.UomProfile, "query", false, async (access, token) =>
        {
            var store = new UomProfileStoreV1(db); await store.Fence(false, token);
            await access.RequireUomProfile(id, token); access.AtCommit(last => access.RequireUomProfile(id, last));
            return await store.Read(id, selected, token);
        }, ct);
    }

    // Caller must already hold the actual unit-store fence in its own Main Owner transaction.
    internal static async Task<UomProfileVersionV1> CurrentExact(OperationsAccessV1 access, UomProfileStoreV1 store,
        UomProfileRefV1 reference, CancellationToken ct, bool enabled = true)
    {
        access.RequireUomSourceAccess(store.Context); Reference(reference); await access.RequireUomProfile(reference.Id, ct);
        var current = await store.Read(reference.Id, null, ct);
        if (current.Reference != reference) throw Error("UOM_PROFILE_CHANGED", 412);
        if (enabled && current.Profile.Enabled != true) throw Error("UOM_PROFILE_DISABLED");
        access.AtCommit(token => access.RequireUomProfile(reference.Id, token));
        return current;
    }
    internal static async Task<UomProfileRefV1> CurrentBase(OperationsAccessV1 access, UomProfileStoreV1 store,
        UomProfileVersionV1 profile, CancellationToken ct)
    {
        if (profile.Profile.Mode == "BASE") return profile.Reference;
        var reference = profile.Profile.BaseRef!;
        var baseline = await CurrentExact(access, store, reference, ct);
        if (baseline.Profile.Mode != "BASE" || baseline.Profile.DimensionKey != profile.Profile.DimensionKey)
            throw Error("UOM_BASE_INVALID");
        return baseline.Reference;
    }

    public Task<UomProfileCommandResultV1> CommandAsync(UomProfileCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.ProfileId == Guid.Empty || input.Profile == null
            || input.Action is not ("CREATE" or "REVISE")) throw Error("UOM_COMMAND_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input);
        var bytes = MainOwnerOperationJournalV1.Encode(frozen);
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.UomProfile, frozen.Action == "CREATE" ? "add" : "edit", true, async (access, token) =>
        {
            var store = new UomProfileStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(access, original, frozen.OperationId, bytes, token);
            var profile = Validate(frozen.Profile!);
            OperationsAccessV1.NewUomBinding? creation = null;
            long? expected = null;
            if (frozen.Action == "CREATE")
            {
                if (frozen.ExpectedVersion != null) throw Error("UOM_VERSION_INVALID", 400);
                creation = await access.BeginUomCreation(frozen.ProfileId, frozen.ExpectedAuthorityVersion, frozen.DepartmentId, token);
            }
            else
            {
                if (frozen.ExpectedAuthorityVersion != null || frozen.DepartmentId != null) throw Error("UOM_COMMAND_INVALID", 400);
                expected = Revision(frozen.ExpectedVersion); await access.RequireUomProfile(frozen.ProfileId, token);
            }
            if (profile.Mode == "RATIO")
            {
                if (profile.BaseRef!.Id == frozen.ProfileId) throw Error("UOM_BASE_INVALID");
                var reader = await access.SourceAsync(OperationsOwnerV1.UomProfile, token);
                UomProfileVersionV1 baseline;
                if (profile.Enabled == true) baseline = await CurrentExact(reader, store, profile.BaseRef, token);
                else
                {
                    await reader.RequireUomProfile(profile.BaseRef.Id, token);
                    baseline = await store.Read(profile.BaseRef.Id, Revision(profile.BaseRef.Version), token);
                    if (baseline.Reference != profile.BaseRef) throw Error("UOM_REFERENCE_INVALID", 400);
                    reader.AtCommit(last => reader.RequireUomProfile(profile.BaseRef.Id, last));
                }
                if (baseline.Profile.Mode != "BASE" || baseline.Profile.DimensionKey != profile.DimensionKey) throw Error("UOM_BASE_INVALID");
            }
            var saved = await store.Append(frozen.ProfileId, access.ActorId, profile, expected, token);
            if (creation != null) await access.CompleteUomCreation(creation, token);
            else access.AtCommit(last => access.RequireUomProfile(frozen.ProfileId, last));
            var result = new UomProfileCommandResultV1(frozen.OperationId, "PROFILE", frozen.ProfileId, saved.Reference.Version, saved.Reference.Digest);
            await journal.Store(Owner, Contract, frozen.OperationId, access.ActorId, bytes, result, [frozen.ProfileId],
                new { action = frozen.Action, actor = access.ActorId, profile = frozen.ProfileId, previousVersion = expected, result }, token);
            return result;
        }, ct, actionAtFence: async token =>
        {
            original = await journal.Read(Owner, frozen.OperationId, token);
            return original == null ? frozen.Action == "CREATE" ? "add" : "edit" : "query";
        });
    }

    private static async Task<UomProfileCommandResultV1> Disclose(OperationsAccessV1 access,
        MainOwnerOperationJournalV1.Original original, Guid operation, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<UomProfileCommandResultV1>(original.Result);
        if (result == null || result.Kind != "PROFILE" || result.OperationId != operation
            || !original.Objects.SequenceEqual(new[] { result.ObjectId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        await access.RequireUomProfile(result.ObjectId, ct); access.AtCommit(token => access.RequireUomProfile(result.ObjectId, token));
        return result;
    }
    public Task<UomProfileOperationV1> OperationAsync(Guid operation, bool close, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.UomProfile, "query", close, async (access, token) =>
        {
            await new UomProfileStoreV1(db).Fence(close, token);
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(Owner, operation, token);
            if (original != null)
            {
                if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(operation, "CLOSED") :
                    new UomProfileOperationV1(operation, "COMMITTED", await Disclose(access, original, operation, null, token));
            }
            if (!close) return new UomProfileOperationV1(operation, "UNKNOWN");
            var result = new UomProfileOperationV1(operation, "CLOSED");
            await journal.Store(Owner, Contract, operation, access.ActorId, null, result, [], new { action = "close-uncommitted-uom", actor = access.ActorId }, token);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
    public Task<UomConversionResultV1> ConvertAsync(UomConversionRequestV1 input, CancellationToken ct = default)
    {
        if (input == null) throw Error("UOM_CONVERSION_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input); Reference(frozen.From); Reference(frozen.To);
        return Authority.ExecuteAsync(OperationsOwnerV1.UomProfile, "query", false, async (access, token) =>
        {
            var store = new UomProfileStoreV1(db); await store.Fence(false, token);
            var from = await CurrentExact(access, store, frozen.From, token);
            var to = await CurrentExact(access, store, frozen.To, token);
            if (await CurrentBase(access, store, from, token) != await CurrentBase(access, store, to, token)) throw Error("UOM_BASE_MISMATCH");
            return new UomConversionResultV1(from.Reference, to.Reference, frozen.Quantity, ConvertExact(frozen.Quantity, from.Profile, to.Profile));
        }, ct);
    }
}
