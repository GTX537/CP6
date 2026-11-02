using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

public sealed partial class NumberAllocationOwnerV1
{
    private static NumberRuleChangeReceiptV1 OriginalChange(NumberAllocationStoreV1.Saved original,
        Guid actor, string operation, byte[]? input)
    {
        if (original.Actor != actor) throw Error("NUMBER_ORIGINAL_FORBIDDEN", 403);
        if (!original.Operation.AsSpan().SequenceEqual(NumberAllocationStoreV1.Encode(operation))
            || input != null && !original.Input.AsSpan().SequenceEqual(input)) throw Error("NUMBER_COMMAND_CONFLICT");
        return JsonSerializer.Deserialize<NumberRuleChangeReceiptV1>(original.Result)!;
    }

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
            if (command.Prefix.Length > 20 || command.DateFormat?.Length > 20 || command.MinimumWidth < 0)
                throw Error("RULE_INVALID", 422);
            var next = current with { Version = command.NewVersion, Prefix = command.Prefix,
                DateFormat = command.DateFormat, MinimumWidth = command.MinimumWidth };
            _ = Format(next, DateTimeOffset.UtcNow, next.FirstValue);
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
