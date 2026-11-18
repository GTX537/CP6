using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

public sealed partial class NumberAllocationOwnerV1
{
    /// <summary>Host binding from the current trusted tenant and immutable registration.
    /// Does not authorize disclosure; the resulting Owner enforces current authority.</summary>
    public static async Task<NumberAllocationScopeV1?> FindRegisteredScopeAsync(CP6Context db, Guid ruleId, CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql()) throw Error("NUMBER_POSTGRESQL_REQUIRED", 503);
        var bytes = await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_number_allocation_v1.scopes
             WHERE tenant_id={db.CurrentTenantId} AND rule_id={ruleId}
            """).SingleOrDefaultAsync(ct);
        return bytes == null ? null : JsonSerializer.Deserialize<NumberAllocationStoreV1.Registration>(bytes)!.Scope;
    }

    private static NumberRuleChangeReceiptV1 OriginalChange(NumberAllocationStoreV1.Saved original,
        Guid actor, string operation, byte[]? input)
    {
        if (original.Actor != actor) throw Error("NUMBER_ORIGINAL_FORBIDDEN", 403);
        if (!original.Operation.AsSpan().SequenceEqual(NumberAllocationStoreV1.Encode(operation))
            || input != null && !original.Input.AsSpan().SequenceEqual(input)) throw Error("NUMBER_COMMAND_CONFLICT");
        return JsonSerializer.Deserialize<NumberRuleChangeReceiptV1>(original.Result)!;
    }

    private static NumberAllocationRuleV1 FormatDraft(NumberAllocationRuleV1 current, string prefix,
        string? dateFormat, int width)
    {
        if (prefix.Length > 20 || dateFormat?.Length > 20 || width < 0)
            throw Error("RULE_INVALID", 422);
        var draft = current with { Prefix = prefix, DateFormat = dateFormat, MinimumWidth = width };
        _ = Format(draft, DateTimeOffset.UtcNow, draft.FirstValue);
        return draft;
    }

    public Task<NumberRuleManagementV1> ReadManagementAsync(CancellationToken ct = default)
        => Execute(false, async (db, store, token) =>
        {
            var actor = await authority.Require(db, "read", token);
            var rule = await Rule(store, token);
            var count = await db.Database.SqlQuery<long>($"""
                SELECT count(*) AS "Value" FROM cp6_number_allocation_v1.receipts WHERE rule_id={scope.RuleId}
                """).SingleAsync(token);
            if (scope.CutoverBasis == ApprovedPubCutover)
            {
                await store.RequireCutoverInventory(token);
                count += await db.Database.SqlQuery<long>($"""
                    SELECT count(*) AS "Value" FROM cp6_number_allocation_v1.prior_slots
                     WHERE rule_id={scope.RuleId} AND number_hash IS NOT NULL
                    """).SingleAsync(token);
            }
            var at = await db.Database.SqlQuery<DateTimeOffset?>($"""
                SELECT COALESCE(
                    (SELECT changed_at FROM cp6_number_allocation_v1.rule_changes
                      WHERE rule_id={scope.RuleId} AND version_hash={NumberAllocationStoreV1.Key(rule.Version)}),
                    (SELECT registered_at FROM cp6_number_allocation_v1.scopes WHERE rule_id={scope.RuleId})
                ) AS "Value"
                """).SingleOrDefaultAsync(token);
            var currentPeriod = Format(rule, DateTimeOffset.UtcNow, rule.FirstValue).Period;
            var allocator = scope.CutoverBasis == ApprovedPubCutover ? LegacyPubAllocator : "Main.Pub.NumberAllocationOwnerV1";
            return (new NumberRuleManagementV1(scope, rule, count, at, currentPeriod, allocator), actor, "read");
        }, ct);

    public Task<NumberRuleChangeReceiptV1> ChangeRuleAsync(NumberRuleChangeCommandV1 command, CancellationToken ct = default)
    {
        Text(command.OperationRef); Text(command.ExpectedRuleVersion); Text(command.NewVersion);
        return Execute(true, async (db, store, token) =>
        {
            var actor = await authority.Require(db, "read", token); var current = await Rule(store, token);
            var original = await store.OriginalChange(command.OperationRef, token);
            if (original != null) return (OriginalChange(original, actor, command.OperationRef,
                NumberAllocationStoreV1.Encode(command)), actor, "read");
            await authority.Require(db, "change-rule", token);
            if (current.Version != command.ExpectedRuleVersion) throw Error("NUMBER_RULE_VERSION_CHANGED", 412);
            if (command.NewVersion == current.Version || await store.HasVersion(command.NewVersion, token))
                throw Error("NUMBER_RULE_VERSION_REUSED");
            var next = FormatDraft(current, command.Prefix, command.DateFormat, command.MinimumWidth)
                with { Version = command.NewVersion };
            var at = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(token);
            var receipt = new NumberRuleChangeReceiptV1(Guid.NewGuid(), scope.TenantId, scope.RuleId,
                scope.SourceOwner, command.OperationRef, current.Version, next, at);
            await store.SaveChange(current, command, actor, receipt, token);
            return (receipt, actor, "change-rule");
        }, ct);
    }

    public Task<NumberRuleChangeReceiptV1?> ReadRuleChangeAsync(string operation, CancellationToken ct = default)
    {
        Text(operation);
        return Execute<NumberRuleChangeReceiptV1?>(false, async (db, store, token) =>
        {
            var actor = await authority.Require(db, "read", token); _ = await Rule(store, token);
            var original = await store.OriginalChange(operation, token);
            return (original == null ? null : OriginalChange(original, actor, operation, null), actor, "read");
        }, ct);
    }

    /// <summary>Null version selects current; a named version returns its immutable rule or null.</summary>
    public Task<NumberAllocationRuleV1?> ReadRuleAsync(string? version = null, CancellationToken ct = default)
    {
        if (version != null) Text(version);
        return Execute<NumberAllocationRuleV1?>(false, async (db, store, token) =>
        {
            var actor = await authority.Require(db, "read", token); var current = await Rule(store, token);
            return (version == null || version == current.Version ? current : await store.HistoricalRule(version, token), actor, "read");
        }, ct);
    }
}
