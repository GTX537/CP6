using System.Globalization;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Pub;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

public sealed partial class NumberAllocationOwnerV1
{
    public const string ApprovedPubCutover = "APPROVED_PUB_OWNER_HISTORY";
    public const string LegacyPubAllocator = "Main.Pub.SeqService";

    public Task<bool> AdoptApprovedAsync(string approvalRef, CancellationToken ct = default)
    {
        Text(approvalRef);
        if (scope.CutoverBasis != ApprovedPubCutover || scope.RuleId == Guid.Empty)
            throw Error("NUMBER_CUTOVER_SCOPE_REQUIRED", 400);
        return Execute(true, async (db, store, token) =>
        {
            var actor = await authority.Require(db, "change-rule", token);
            // 与 SeqService 对同一旧规则行加锁，先行分配完成后才读取原归属方的批准清单。
            var legacy = await db.Pub_DocSequences.FromSqlInterpolated($"""
                SELECT * FROM public."Pub_DocSequence" WHERE "Id"={scope.RuleId} AND "TenantId"={scope.TenantId}
                    AND "BizKey"=CAST({scope.BizKey} AS bpchar) FOR UPDATE
                """).AsNoTracking().SingleOrDefaultAsync(token) ?? throw Error("NUMBER_CUTOVER_SOURCE_NOT_FOUND", 404);
            var registered = await store.RegistrationAsync(token);
            if (registered != null)
            {
                if (registered.Scope != scope || await store.CutoverApproval(token) != approvalRef)
                    throw Error("NUMBER_REGISTRATION_CONFLICT");
                await store.RequireCutoverInventory(token);
                return (true, actor, "change-rule");
            }
            if (predecessor == null) throw Error("NUMBER_CUTOVER_APPROVAL_REQUIRED");
            var snapshot = await predecessor.ReadApprovedAsync(db, scope, token);
            if (snapshot == null || snapshot.Scope != scope || snapshot.ApprovalRef != approvalRef
                || snapshot.OriginalAllocator != LegacyPubAllocator)
                throw Error("NUMBER_CUTOVER_APPROVAL_REQUIRED");
            ValidateCutover(legacy, snapshot);
            await store.SaveCutover(snapshot, token);
            return (true, actor, "change-rule");
        }, ct);
    }

    private void ValidateCutover(Pub_DocSequence legacy, NumberCutoverSnapshotV1 source)
    {
        var rule = source.Rule;
        Text(rule.Version); Text(scope.SourceOwner); Text(scope.BusinessScope);
        if (rule.FirstValue != 1 || rule.ResetCycle != legacy.ResetCycle || rule.Prefix != legacy.Prefix
            || rule.DateFormat != legacy.DateFormat || rule.MinimumWidth != legacy.SeqLength || rule.MinimumWidth < 0)
            throw Error("NUMBER_CUTOVER_RULE_MISMATCH");
        _ = Format(rule, DateTimeOffset.UtcNow, rule.FirstValue);
        var periods = new Dictionary<string, int>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        var slots = new HashSet<(string, int)>();
        var byOperation = new Dictionary<string, NumberPriorSlotV1>(StringComparer.Ordinal);
        foreach (var period in source.Periods)
        {
            var pattern = rule.ResetCycle switch { 1 => "yyyyMMdd", 2 => "yyyyMM", 3 => "yyyy", _ => null };
            bool validKey = pattern == null ? period.PeriodKey == "" : DateTime.TryParseExact(period.PeriodKey,
                pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            if (!validKey || period.LastIssued < 0 || !periods.TryAdd(period.PeriodKey, period.LastIssued))
                throw Error("NUMBER_CUTOVER_INCOMPLETE");
        }
        if (legacy.CurrentValue < 0 || legacy.CurrentValue > periods.GetValueOrDefault(legacy.CurrentPeriod ?? ""))
            throw Error("NUMBER_CUTOVER_INCOMPLETE");
        foreach (var slot in source.Slots)
        {
            if (!periods.TryGetValue(slot.PeriodKey, out var high) || slot.SequenceValue < 1 || slot.SequenceValue > high
                || !slots.Add((slot.PeriodKey, slot.SequenceValue))
                || (slot.ExposedNumber == null ? string.IsNullOrWhiteSpace(slot.GapReason) :
                    slot.ExposedNumber.Length == 0 || slot.GapReason != null || !numbers.Add(slot.ExposedNumber))
                || slot.OperationRef != null && !byOperation.TryAdd(slot.OperationRef, slot))
                throw Error("NUMBER_CUTOVER_INCOMPLETE");
            counts[slot.PeriodKey] = counts.GetValueOrDefault(slot.PeriodKey) + 1;
        }
        // 每个历史序位均有外露原号或跳号原因，计数器或业务表最大号本身无法完成核对。
        if (periods.Any(p => counts.GetValueOrDefault(p.Key) != p.Value)) throw Error("NUMBER_CUTOVER_INCOMPLETE");
        var operations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in source.Operations)
        {
            var command = operation.Command;
            Text(command.OperationRef); Text(command.ExpectedRuleVersion); Text(command.BusinessInputIdentity);
            if (operation.ActorId == Guid.Empty || !operations.Add(command.OperationRef)) throw Error("NUMBER_CUTOVER_INCOMPLETE");
            if (operation.OriginalReceipt != null)
                MatchPrior(operation, byOperation.GetValueOrDefault(command.OperationRef), operation.OriginalReceipt);
        }
        if (byOperation.Keys.Any(key => !operations.Contains(key))) throw Error("NUMBER_CUTOVER_INCOMPLETE");
    }

    private void MatchPrior(NumberPriorOperationV1 operation, NumberPriorSlotV1? slot, NumberAllocationReceiptV1 receipt)
    {
        if (slot?.ExposedNumber == null || receipt.AllocationId == Guid.Empty || receipt.AllocatedAtUtc == default
            || receipt.AllocationState != "ALLOCATED" || receipt.BusinessState != "UNKNOWN" || receipt.RuleId != scope.RuleId
            || receipt.TenantId != scope.TenantId || receipt.SourceOwner != scope.SourceOwner
            || receipt.OperationRef != operation.Command.OperationRef || receipt.RuleVersion != operation.Command.ExpectedRuleVersion
            || receipt.PeriodKey != slot.PeriodKey || receipt.SequenceValue != slot.SequenceValue || receipt.Number != slot.ExposedNumber)
            throw Error("NUMBER_PREDECESSOR_ORIGINAL_MISMATCH");
    }

    private async Task RequireCutoverWriter(CP6Context db, NumberAllocationStoreV1 store, CancellationToken ct)
    {
        if (scope.CutoverBasis != ApprovedPubCutover) return;
        var cutover = await store.CutoverApproval(ct);
        var grant = predecessor == null || cutover == null ? null
            : await predecessor.AcquireWriterAsync(db, scope, cutover, ct);
        if (grant == null || grant.Scope != scope || grant.CutoverApprovalRef != cutover
            || string.IsNullOrWhiteSpace(grant.WriterApprovalRef))
            throw Error("NUMBER_RECOVERY_APPROVAL_REQUIRED", 503);
        // 水位和批准来自不同依据。即使旧备份内部计数完全一致，也须查到已对外给出的每份原回执。
        foreach (var expected in grant.RequiredAllocations)
        {
            var original = await store.Original(expected.OperationRef, ct);
            if (expected.RuleId != scope.RuleId || expected.TenantId != scope.TenantId || expected.SourceOwner != scope.SourceOwner
                || original == null || !original.Result.AsSpan().SequenceEqual(NumberAllocationStoreV1.Encode(expected)))
                throw Error("NUMBER_RECOVERY_INCOMPLETE", 503);
        }
    }

    private async Task<NumberAllocationReceiptV1?> PriorResult(CP6Context db, NumberAllocationStoreV1 store,
        Guid actor, string key, NumberAllocationCommandV1? input, CancellationToken ct)
    {
        if (scope.CutoverBasis != ApprovedPubCutover) return null;
        var operation = await store.PriorOperation(key, ct);
        if (operation == null)
        {
            // 查询缺少原键只有在档案完整时才能返回未找到。新分配在推进计数前核对同一清单。
            if (input == null) await store.RequireCutoverInventory(ct);
            return null;
        }
        if (operation.ActorId != actor) throw Error("NUMBER_ORIGINAL_FORBIDDEN", 403);
        if (operation.Command.OperationRef != key || input != null &&
            !NumberAllocationStoreV1.Encode(input).AsSpan().SequenceEqual(NumberAllocationStoreV1.Encode(operation.Command)))
            throw Error("NUMBER_COMMAND_CONFLICT");
        if (operation.OriginalReceipt != null) return operation.OriginalReceipt;
        var approval = await store.CutoverApproval(ct);
        if (predecessor == null || approval == null) throw Error("NUMBER_PREDECESSOR_LOOKUP_REQUIRED", 503);
        var receipt = await predecessor.ReadOriginalAsync(db, scope, approval, key, ct);
        if (receipt == null) throw Error("NUMBER_PREDECESSOR_OUTCOME_UNKNOWN", 503);
        MatchPrior(operation, await store.PriorSlot(key, ct), receipt);
        return receipt;
    }
}
