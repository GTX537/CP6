using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Erp.CurrencyPolicyValuesV1;

namespace CP6.Core.Services.Erp;

public sealed class CurrencyPolicyOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Erp.CurrencyPolicy.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<CurrencyPolicyScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.CurrencyPolicy, "query", false, (access, _) => Task.FromResult(new CurrencyPolicyScopeV1(db.CurrentTenantId, access.ActorId)),
        ct, readOnlyAuthorityFence: true);
    public Task<CurrencyPolicyCreationContextV1> CreationContextAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.CurrencyPolicy, "add", false, async (access, token) =>
        {
            await new CurrencyPolicyStoreV1(db).Fence(false, token);
            return await access.CurrencyCreationContext(token);
        }, ct, readOnlyAuthorityFence: true);

    public Task<CurrencyPolicyVersionV1[]> ListAsync(string? search, int page = 1, int size = 20, CancellationToken ct = default)
    {
        if (search?.Length > 128 || page < 1 || size is < 1 or > 100 || page > int.MaxValue / size) throw Error("CURRENCY_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.CurrencyPolicy, "query", false, async (access, token) =>
        {
            var store = new CurrencyPolicyStoreV1(db); await store.Fence(false, token);
            var rows = access.CurrencyPolicies();
            if (!string.IsNullOrEmpty(search)) rows = rows.Where(row => row.Code.Contains(search));
            var ids = await rows.OrderBy(row => row.Code).ThenBy(row => row.Id).Skip((page - 1) * size).Take(size).Select(row => row.Id).ToArrayAsync(token);
            var result = new List<CurrencyPolicyVersionV1>();
            foreach (var id in ids)
            {
                result.Add(await store.Read(id, null, token)); access.AtCommit(last => access.RequireCurrencyPolicy(id, last));
            }
            return result.ToArray();
        }, ct);
    }
    public Task<CurrencyPolicyVersionV1> ReadAsync(Guid id, string? version, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("CURRENCY_IDENTITY_INVALID", 400);
        var selected = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.CurrencyPolicy, "query", false, async (access, token) =>
        {
            var store = new CurrencyPolicyStoreV1(db); await store.Fence(false, token);
            await access.RequireCurrencyPolicy(id, token); access.AtCommit(last => access.RequireCurrencyPolicy(id, last));
            return await store.Read(id, selected, token);
        }, ct);
    }

    // Caller must already hold the actual currency-store fence in its own Main Owner transaction.
    internal static async Task<CurrencyPolicyVersionV1> CurrentExact(OperationsAccessV1 access, CurrencyPolicyStoreV1 store,
        CurrencyPolicyRefV1 reference, CancellationToken ct)
    {
        access.RequireCurrencySourceAccess(store.Context); Reference(reference); await access.RequireCurrencyPolicy(reference.Id, ct);
        var current = await store.Read(reference.Id, null, ct);
        if (current.Reference != reference) throw Error("CURRENCY_POLICY_CHANGED", 412);
        if (current.Policy.Enabled != true) throw Error("CURRENCY_POLICY_DISABLED");
        access.AtCommit(token => access.RequireCurrencyPolicy(reference.Id, token));
        return current;
    }
    public Task<CurrencyPolicyCommandResultV1> CommandAsync(CurrencyPolicyCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.PolicyId == Guid.Empty || input.Policy == null
            || input.Action is not ("CREATE" or "REVISE")) throw Error("CURRENCY_COMMAND_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input);
        var bytes = MainOwnerOperationJournalV1.Encode(frozen);
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.CurrencyPolicy, frozen.Action == "CREATE" ? "add" : "edit", true, async (access, token) =>
        {
            var store = new CurrencyPolicyStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(access, original, frozen.OperationId, bytes, token);
            var policy = Validate(frozen.Policy!);
            OperationsAccessV1.NewCurrencyBinding? creation = null;
            long? expected = null;
            if (frozen.Action == "CREATE")
            {
                if (frozen.ExpectedVersion != null) throw Error("CURRENCY_VERSION_INVALID", 400);
                creation = await access.BeginCurrencyCreation(frozen.PolicyId, frozen.ExpectedAuthorityVersion, frozen.DepartmentId, token);
            }
            else
            {
                if (frozen.ExpectedAuthorityVersion != null || frozen.DepartmentId != null) throw Error("CURRENCY_COMMAND_INVALID", 400);
                expected = Revision(frozen.ExpectedVersion); await access.RequireCurrencyPolicy(frozen.PolicyId, token);
            }
            var saved = await store.Append(frozen.PolicyId, access.ActorId, policy, expected, token);
            if (creation != null) await access.CompleteCurrencyCreation(creation, token);
            else access.AtCommit(last => access.RequireCurrencyPolicy(frozen.PolicyId, last));
            var result = new CurrencyPolicyCommandResultV1(frozen.OperationId, "POLICY", frozen.PolicyId, saved.Reference.Version, saved.Reference.Digest);
            await journal.Store(Owner, Contract, frozen.OperationId, access.ActorId, bytes, result, [frozen.PolicyId],
                new { action = frozen.Action, actor = access.ActorId, policy = frozen.PolicyId, previousVersion = expected, result }, token);
            return result;
        }, ct, actionAtFence: async token =>
        {
            original = await journal.Read(Owner, frozen.OperationId, token);
            return original == null ? frozen.Action == "CREATE" ? "add" : "edit" : "query";
        });
    }

    private static async Task<CurrencyPolicyCommandResultV1> Disclose(OperationsAccessV1 access,
        MainOwnerOperationJournalV1.Original original, Guid operation, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<CurrencyPolicyCommandResultV1>(original.Result);
        if (result == null || result.Kind != "POLICY" || result.OperationId != operation
            || !original.Objects.SequenceEqual(new[] { result.ObjectId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        await access.RequireCurrencyPolicy(result.ObjectId, ct); access.AtCommit(token => access.RequireCurrencyPolicy(result.ObjectId, token));
        return result;
    }
    public Task<CurrencyPolicyOperationV1> OperationAsync(Guid operation, bool close, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.CurrencyPolicy, "query", close, async (access, token) =>
        {
            await new CurrencyPolicyStoreV1(db).Fence(close, token);
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(Owner, operation, token);
            if (original != null)
            {
                if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(operation, "CLOSED") :
                    new CurrencyPolicyOperationV1(operation, "COMMITTED", await Disclose(access, original, operation, null, token));
            }
            if (!close) return new CurrencyPolicyOperationV1(operation, "UNKNOWN");
            var result = new CurrencyPolicyOperationV1(operation, "CLOSED");
            await journal.Store(Owner, Contract, operation, access.ActorId, null, result, [], new { action = "close-uncommitted-currency", actor = access.ActorId }, token);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
    public Task<CurrencyQuantizationResultV1> QuantizeAsync(CurrencyQuantizationRequestV1 input, CancellationToken ct = default)
    {
        if (input == null) throw Error("CURRENCY_QUANTIZATION_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input); Reference(frozen.PolicyRef);
        return Authority.ExecuteAsync(OperationsOwnerV1.CurrencyPolicy, "query", false, async (access, token) =>
        {
            var store = new CurrencyPolicyStoreV1(db); await store.Fence(false, token);
            var current = await CurrentExact(access, store, frozen.PolicyRef, token);
            return new CurrencyQuantizationResultV1(current.Reference, frozen.Kind, frozen.Value,
                Quantize(frozen.Value, frozen.Kind, current.Policy));
        }, ct);
    }
}
