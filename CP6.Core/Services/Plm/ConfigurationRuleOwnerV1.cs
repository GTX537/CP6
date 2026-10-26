using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringRevisionValuesV1;
namespace CP6.Core.Services.Plm;
public sealed class ConfigurationRuleOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Plm.ConfigurationRule.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<ConfigurationRuleScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.ConfigurationRule, "query", false, (access, _) => Task.FromResult(new ConfigurationRuleScopeV1(db.CurrentTenantId, access.ActorId)), ct, readOnlyAuthorityFence: true);
    public Task<ConfigurationRuleCreationContextV1> CreationContextAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.ConfigurationRule, "add", false, async (access, token) =>
        { await new ConfigurationRuleStoreV1(db).Fence(false, token); return await access.ConfigurationRuleCreationContext(token); }, ct, readOnlyAuthorityFence: true);
    private async Task<bool> Product(OperationsAccessV1 root, EngineeringProductRefV1 reference, bool requireCurrent, CancellationToken ct)
    {
        Reference(reference); var access = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct);
        await access.RequireEngineeringProduct(reference.Id, ct); root.AtCommit(token => access.RequireEngineeringProduct(reference.Id, token));
        var store = new EngineeringProductStoreV1(db); await store.Fence(false, ct); var value = await store.Read(reference.Id, Revision(reference.Version), ct);
        if (value.Reference != reference) throw Error("ENGINEERING_REFERENCE_CHANGED", 412);
        var current = value.IsCurrent && value.Product.Enabled == true;
        if (requireCurrent && !current) throw Error("ENGINEERING_PRODUCT_CHANGED", 412);
        return current;
    }
    private async Task<ConfigurationRuleSnapshotV1> Observe(OperationsAccessV1 access, ConfigurationRuleSnapshotV1 value, CancellationToken ct)
    {
        await access.RequireConfigurationRule(value.Reference.Id, ct); access.AtCommit(token => access.RequireConfigurationRule(value.Reference.Id, token));
        var current = await Product(access, value.Body.ProductRef, false, ct);
        return value with { Missing = current ? [] : ["PRODUCT_SOURCE_CHANGED"] };
    }
    public Task<ConfigurationRuleSnapshotV1[]> ListAsync(string? search, int page = 1, CancellationToken ct = default)
    {
        if (search?.Length > 256 || page < 1 || page > int.MaxValue / 20) throw Error("CONFIGURATION_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ConfigurationRule, "query", false, async (access, token) =>
        {
            var store = new ConfigurationRuleStoreV1(db); await store.Fence(false, token);
            var rows = access.ConfigurationRules();
            if (!string.IsNullOrEmpty(search)) rows = rows.Where(row => row.Id.ToString().Contains(search) || row.ProductId.ToString().Contains(search));
            var ids = await rows.OrderBy(row => row.Id).Skip((page - 1) * 20).Take(20).Select(row => row.Id).ToArrayAsync(token);
            var result = new List<ConfigurationRuleSnapshotV1>(); foreach (var id in ids) result.Add(await Observe(access, await store.Read(id, null, token), token));
            return result.ToArray();
        }, ct);
    }
    public Task<ConfigurationRuleSnapshotV1> ReadAsync(Guid id, string? version, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("CONFIGURATION_RULE_INVALID", 400); var selected = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.ConfigurationRule, "query", false, async (access, token) =>
        {
            var store = new ConfigurationRuleStoreV1(db); await store.Fence(false, token); await access.RequireConfigurationRule(id, token);
            return await Observe(access, await store.Read(id, selected, token), token);
        }, ct);
    }
    // The consumer supplies its current authority and the identical transaction; there is no detached resolver or copied rule.
    internal async Task<ConfigurationRuleSnapshotV1> Source(OperationsAccessV1 root, EngineeringSourceRefV1 reference,
        EngineeringProductRefV1? product, bool requireActive, CancellationToken ct)
    {
        ConfigurationRuleValuesV1.Ref(reference); var access = await root.SourceAsync(OperationsOwnerV1.ConfigurationRule, ct);
        access.RequireConfigurationRuleSourceAccess(db); await access.RequireConfigurationRule(reference.Id, ct);
        root.AtCommit(token => access.RequireConfigurationRule(reference.Id, token));
        root.AtCommit(access.ValidateFinalAsync);
        var store = new ConfigurationRuleStoreV1(db); await store.Fence(false, ct);
        var value = await Observe(access, await store.Read(reference.Id, Revision(reference.Version), ct), ct);
        if (value.Reference != reference) throw Error("CONFIGURATION_RULE_REFERENCE_CHANGED", 412);
        if (requireActive && (!value.IsActive || value.State != "APPROVED" || value.Missing!.Length != 0)) throw Error("CONFIGURATION_RULE_NOT_ACTIVE", 412);
        if (requireActive && value.Body.ProductRef != product) throw Error("CONFIGURATION_RULE_PRODUCT_MISMATCH", 409);
        return value;
    }
    public Task<ConfigurationRuleResultV1> CommandAsync(ConfigurationRuleCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.RuleId == Guid.Empty) throw Error("CONFIGURATION_COMMAND_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input); Text(frozen.Reason);
        var action = frozen.Action switch { "CREATE" => "add", "REVISE" => "edit", "SUBMIT" => "submit", "APPROVE" or "RETURN" => "approve", "WITHDRAW" => "withdraw", _ => throw Error("CONFIGURATION_COMMAND_INVALID", 400) };
        var bytes = MainOwnerOperationJournalV1.Encode(frozen); var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.ConfigurationRule, action, true, async (access, token) =>
        {
            var store = new ConfigurationRuleStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(access, store, original, frozen.OperationId, bytes, token);
            ConfigurationRuleBodyV1 body; string state;
            OperationsAccessV1.NewConfigurationRuleBinding? creation = null;
            if (frozen.Action == "CREATE")
            {
                if (frozen.ExpectedVersion != null || frozen.ExpectedEtag != null || frozen.ExpectedActiveRef != null) throw Error("CONFIGURATION_COMMAND_INVALID", 400);
                ConfigurationRuleValuesV1.Validate(frozen.Body); body = frozen.Body!; state = "DRAFT";
                creation = await access.BeginConfigurationRuleCreation(frozen.RuleId, frozen.ExpectedAuthorityVersion, frozen.DepartmentId, token);
                await Product(access, body.ProductRef, true, token);
            }
            else
            {
                if (frozen.ExpectedAuthorityVersion != null || frozen.DepartmentId != null || frozen.ExpectedEtag == null) throw Error("CONFIGURATION_COMMAND_INVALID", 400);
                Revision(frozen.ExpectedVersion); await access.RequireConfigurationRule(frozen.RuleId, token);
                var current = await Observe(access, await store.Read(frozen.RuleId, null, token), token);
                if (current.Reference.Version != frozen.ExpectedVersion || current.Etag != frozen.ExpectedEtag || current.ActiveRef != frozen.ExpectedActiveRef) throw Error("CONFIGURATION_RULE_CHANGED", 412);
                body = current.Body;
                if (frozen.Action != "REVISE" && frozen.Body != null) throw Error("CONFIGURATION_COMMAND_INVALID", 400);
                state = frozen.Action switch
                {
                    "REVISE" when current.State != "UNDER_REVIEW" => "DRAFT",
                    "SUBMIT" when current.State == "DRAFT" => "UNDER_REVIEW",
                    "APPROVE" when current.State == "UNDER_REVIEW" => "APPROVED",
                    "RETURN" when current.State == "UNDER_REVIEW" => "DRAFT",
                    "WITHDRAW" when current.ActiveRef != null => current.State == "APPROVED" ? "WITHDRAWN" : current.State,
                    _ => throw Error("CONFIGURATION_RULE_TRANSITION_INVALID", 409)
                };
                if (frozen.Action == "REVISE") { ConfigurationRuleValuesV1.Validate(frozen.Body); body = frozen.Body!; }
                if (frozen.Action is "REVISE" or "SUBMIT" or "APPROVE") await Product(access, body.ProductRef, true, token);
                if (frozen.Action == "WITHDRAW") await Observe(access, await store.Read(frozen.RuleId, Revision(current.ActiveRef!.Version), token), token);
            }
            var saved = await store.Append(frozen, access.ActorId, body, state, token);
            if (creation != null) await access.CompleteConfigurationRuleCreation(creation, token);
            await Observe(access, saved, token);
            var result = new ConfigurationRuleResultV1(frozen.OperationId, "CONFIGURATION_RULE", frozen.RuleId, saved.Reference.Version, saved.Reference.Digest);
            await journal.Store(ConfigurationRuleValuesV1.Owner, Contract, frozen.OperationId, access.ActorId, bytes, result, [frozen.RuleId],
                new { action = frozen.Action, actor = access.ActorId, reason = frozen.Reason, result, saved.ActiveRef }, token);
            return result;
        }, ct, actionAtFence: async token => { original = await journal.Read(ConfigurationRuleValuesV1.Owner, frozen.OperationId, token); return original == null ? action : "query"; });
    }
    private async Task<ConfigurationRuleResultV1> Disclose(OperationsAccessV1 access, ConfigurationRuleStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid id, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<ConfigurationRuleResultV1>(original.Result);
        if (result == null || result.OperationId != id || result.Kind != "CONFIGURATION_RULE" || !original.Objects.SequenceEqual(new[] { result.ObjectId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var saved = await Observe(access, await store.Read(result.ObjectId, Revision(result.Version), ct), ct);
        if (saved.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        return result;
    }
    public Task<ConfigurationRuleOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ConfigurationRule, "query", close, async (access, token) =>
        {
            var store = new ConfigurationRuleStoreV1(db); await store.Fence(close, token); var journal = new MainOwnerOperationJournalV1(db);
            var original = await journal.Read(ConfigurationRuleValuesV1.Owner, id, token);
            if (original != null)
            {
                if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(id, "CLOSED") : new ConfigurationRuleOperationV1(id, "COMMITTED", await Disclose(access, store, original, id, null, token));
            }
            if (!close) return new ConfigurationRuleOperationV1(id, "UNKNOWN");
            var result = new ConfigurationRuleOperationV1(id, "CLOSED");
            await journal.Store(ConfigurationRuleValuesV1.Owner, Contract, id, access.ActorId, null, result, [], new { action = "close-uncommitted-configuration-rule", actor = access.ActorId }, token);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
