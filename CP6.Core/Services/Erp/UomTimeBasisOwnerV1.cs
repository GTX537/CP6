using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Erp.UomProfileValuesV1;
using static CP6.Core.Services.Erp.UomTimeBasisStoreV1;
namespace CP6.Core.Services.Erp;

/// <summary>Explicit original UOM Owner declaration. Neither unit code nor a dimension label defines physical time.</summary>
public sealed class UomTimeBasisOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Erp.UomTimeBasis.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    internal static string Seconds(string basis) => basis switch { "SECOND" => "1", "MINUTE" => "60", "HOUR" => "3600", _ => throw Error("TIME_BASIS_INVALID", 400) };
    internal static UomTimeBasisDefinitionV1 Validate(UomTimeBasisDefinitionV1 definition)
    {
        if (definition == null) throw Error("TIME_BASIS_INVALID", 400);
        Reference(definition.BaseRef); Seconds(definition.Basis);
        if (string.IsNullOrWhiteSpace(definition.Reason) || definition.Reason.Length > 500 || definition.Reason.Any(char.IsControl))
            throw Error("TIME_BASIS_REASON_REQUIRED", 400);
        return definition with { Reason = definition.Reason.Trim().Normalize(NormalizationForm.FormC) };
    }
    public Task<UomProfileScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.UomProfile, "query", false, (a, _) => Task.FromResult(new UomProfileScopeV1(db.CurrentTenantId, a.ActorId)), ct, readOnlyAuthorityFence: true);
    private static async Task VerifyOriginal(OperationsAccessV1 access, UomProfileStoreV1 units, UomTimeBasisVersionV1 value, CancellationToken ct)
    {
        access.RequireUomSourceAccess(units.Context); await access.RequireUomProfile(value.Reference.Id, ct);
        var original = await units.Read(value.Definition.BaseRef.Id, Revision(value.Definition.BaseRef.Version), ct);
        if (original.Reference != value.Definition.BaseRef || original.Profile.Mode != "BASE") throw Error("TIME_BASIS_ORIGINAL_UNAVAILABLE", 503);
        access.AtCommit(last => access.RequireUomProfile(value.Reference.Id, last));
    }
    internal static async Task<UomTimeBasisVersionV1> Current(OperationsAccessV1 access, UomProfileStoreV1 units,
        UomTimeBasisStoreV1 store, UomProfileVersionV1 baseline, UomTimeBasisRefV1? expected, CancellationToken ct)
    {
        if (!ReferenceEquals(units.Context, store.Context)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        access.RequireUomSourceAccess(store.Context);
        var value = await store.Read(baseline.Reference.Id, null, ct); await VerifyOriginal(access, units, value, ct);
        if (expected != null && value.Reference != expected) throw Error("TIME_BASIS_CHANGED", 412);
        if (baseline.Profile.Mode != "BASE" || baseline.Reference != value.Definition.BaseRef) throw Error("TIME_BASIS_SOURCE_CHANGED", 412);
        if (!value.Definition.Enabled || baseline.Profile.Enabled != true) throw Error("TIME_BASIS_DISABLED");
        return value with { CurrentApplicable = true, Reasons = [] };
    }
    private async Task<UomTimeBasisVersionV1> ReadOwned(OperationsAccessV1 a, UomProfileStoreV1 units, UomTimeBasisStoreV1 store,
        Guid id, long? version, CancellationToken ct)
    {
        await a.RequireUomProfile(id, ct); var value = await store.Read(id, version, ct); await VerifyOriginal(a, units, value, ct);
        var current = await units.Read(id, null, ct); var reasons = new List<string>();
        if (!value.IsCurrent) reasons.Add("TIME_BASIS_CHANGED");
        if (current.Reference != value.Definition.BaseRef || current.Profile.Mode != "BASE") reasons.Add("TIME_BASIS_SOURCE_CHANGED");
        if (current.Profile.Enabled != true || !value.Definition.Enabled) reasons.Add("TIME_BASIS_DISABLED");
        return value with { CurrentApplicable = reasons.Count == 0, Reasons = reasons.ToArray() };
    }
    public Task<UomTimeBasisEditorV1> EditorAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("UOM_IDENTITY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.UomProfile, "query", false, async (a, token) =>
        {
            var units = new UomProfileStoreV1(db); await units.Fence(false, token); var store = new UomTimeBasisStoreV1(db); await store.Fence(false, token);
            await a.RequireUomProfile(id, token); var current = await units.Read(id, null, token);
            if (current.Profile.Mode != "BASE") throw Error("TIME_BASIS_BASE_REQUIRED");
            var head = await store.Head(id, token); var value = head == null ? null : await ReadOwned(a, units, store, id, head, token);
            a.AtCommit(last => a.RequireUomProfile(id, last)); return new UomTimeBasisEditorV1(current, head?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "absent", value);
        }, ct);
    }
    public Task<UomTimeBasisVersionV1> ReadAsync(Guid id, string? version, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("UOM_IDENTITY_INVALID", 400); var exact = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.UomProfile, "query", false, async (a, token) =>
        {
            var units = new UomProfileStoreV1(db); await units.Fence(false, token); var store = new UomTimeBasisStoreV1(db); await store.Fence(false, token);
            return await ReadOwned(a, units, store, id, exact, token);
        }, ct);
    }
    public Task<UomProfileCommandResultV1> SaveAsync(UomTimeBasisCommandV1 input, CancellationToken ct = default)
    {
        if (input == null) throw Error("TIME_BASIS_COMMAND_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input); var definition = Validate(frozen.Definition);
        if (frozen.OperationId == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        var expected = frozen.ExpectedVersion == "absent" ? (long?)null : Revision(frozen.ExpectedVersion);
        var bytes = MainOwnerOperationJournalV1.Encode(frozen); var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.UomProfile, "edit", true, async (a, token) =>
        {
            var units = new UomProfileStoreV1(db); await units.Fence(false, token); var store = new UomTimeBasisStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(a, units, store, frozen.OperationId, original, bytes, token);
            await a.RequireUomProfile(definition.BaseRef.Id, token);
            a.AtCommit(last => a.RequireUomProfile(definition.BaseRef.Id, last));
            var reader = await a.SourceAsync(OperationsOwnerV1.UomProfile, token);
            var baseline = await UomProfileOwnerV1.CurrentExact(reader, units, definition.BaseRef, token, enabled: definition.Enabled);
            if (baseline.Profile.Mode != "BASE") throw Error("TIME_BASIS_BASE_REQUIRED");
            var saved = await store.Append(definition, expected, a.ActorId, token);
            var result = new UomProfileCommandResultV1(frozen.OperationId, "TIME_BASIS", saved.Reference.Id, saved.Reference.Version, saved.Reference.Digest);
            await journal.Store(BasisOwner, Contract, frozen.OperationId, a.ActorId, bytes, result, [result.ObjectId],
                new { action = "declare-unit-time-basis", actor = a.ActorId, previousVersion = expected, definition, result }, token);
            return result;
        }, ct, actionAtFence: async token => { original = await journal.Read(BasisOwner, frozen.OperationId, token); return original == null ? "edit" : "query"; });
    }
    private async Task<UomProfileCommandResultV1> Disclose(OperationsAccessV1 a, UomProfileStoreV1 units, UomTimeBasisStoreV1 store,
        Guid id, MainOwnerOperationJournalV1.Original original, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<UomProfileCommandResultV1>(original.Result);
        if (result == null || result.OperationId != id || result.Kind != "TIME_BASIS" || original.Objects.Length != 1 || original.Objects[0] != result.ObjectId)
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var value = await store.Read(result.ObjectId, Revision(result.Version), ct); await VerifyOriginal(a, units, value, ct);
        if (value.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503); return result;
    }
    public Task<UomProfileOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.UomProfile, "query", close, async (a, token) =>
        {
            var units = new UomProfileStoreV1(db); await units.Fence(false, token); var store = new UomTimeBasisStoreV1(db); await store.Fence(close, token);
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(BasisOwner, id, token);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(id, "CLOSED") : new UomProfileOperationV1(id, "COMMITTED", await Disclose(a, units, store, id, original, null, token));
            }
            if (!close) return new UomProfileOperationV1(id, "UNKNOWN");
            var result = new UomProfileOperationV1(id, "CLOSED");
            await journal.Store(BasisOwner, Contract, id, a.ActorId, null, result, [], new { action = "close-uncommitted-time-basis", actor = a.ActorId }, token); return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
