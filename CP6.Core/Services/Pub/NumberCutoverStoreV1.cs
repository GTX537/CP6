using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

internal sealed partial class NumberAllocationStoreV1
{
    internal async Task<string?> CutoverApproval(CancellationToken ct)
    {
        var bytes = await db.Database.SqlQuery<byte[]>($"""
            SELECT approval AS "Value" FROM cp6_number_allocation_v1.cutovers WHERE rule_id={scope.RuleId}
            """).SingleOrDefaultAsync(ct);
        return bytes == null ? null : JsonSerializer.Deserialize<string>(bytes);
    }

    internal async Task RequireCutoverInventory(CancellationToken ct)
    {
        var complete = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_number_allocation_v1.cutovers c WHERE c.rule_id={scope.RuleId}
              AND c.period_count=(SELECT count(*) FROM cp6_number_allocation_v1.prior_periods p WHERE p.rule_id=c.rule_id)
              AND c.slot_count=(SELECT count(*) FROM cp6_number_allocation_v1.prior_slots s WHERE s.rule_id=c.rule_id)
              AND c.operation_count=(SELECT count(*) FROM cp6_number_allocation_v1.prior_operations o WHERE o.rule_id=c.rule_id)) AS "Value"
            """).SingleAsync(ct);
        if (!complete) throw new NumberAllocationExceptionV1("NUMBER_CUTOVER_INCOMPLETE", 503);
    }

    internal async Task<NumberPriorOperationV1?> PriorOperation(string key, CancellationToken ct)
    {
        var bytes = await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_number_allocation_v1.prior_operations
             WHERE rule_id={scope.RuleId} AND operation_hash={Key(key)}
            """).SingleOrDefaultAsync(ct);
        return bytes == null ? null : JsonSerializer.Deserialize<NumberPriorOperationV1>(bytes);
    }

    internal async Task<NumberPriorSlotV1?> PriorSlot(string key, CancellationToken ct)
    {
        var bytes = await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_number_allocation_v1.prior_slots
             WHERE rule_id={scope.RuleId} AND operation_hash={Key(key)}
            """).SingleOrDefaultAsync(ct);
        return bytes == null ? null : JsonSerializer.Deserialize<NumberPriorSlotV1>(bytes);
    }

    internal async Task SaveCutover(NumberCutoverSnapshotV1 source, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_number_allocation_v1.scopes(rule_id,tenant_id,body)
            VALUES({scope.RuleId},{scope.TenantId},{Encode(new Registration(scope, source.Rule))})
            """, ct);
        foreach (var period in source.Periods)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_number_allocation_v1.prior_periods(rule_id,period_key,last_issued)
                VALUES({scope.RuleId},{period.PeriodKey},{period.LastIssued})
                """, ct);
        foreach (var operation in source.Operations)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_number_allocation_v1.prior_operations(rule_id,operation_hash,body)
                VALUES({scope.RuleId},{Key(operation.Command.OperationRef)},{Encode(operation)})
                """, ct);
        foreach (var slot in source.Slots)
        {
            var number = slot.ExposedNumber == null ? null : Key(slot.ExposedNumber);
            var op = slot.OperationRef == null ? null : Key(slot.OperationRef);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_number_allocation_v1.prior_slots(rule_id,period_key,sequence_value,number_hash,operation_hash,body)
                VALUES({scope.RuleId},{slot.PeriodKey},{slot.SequenceValue},{number},{op},{Encode(slot)})
                """, ct);
        }
        // 最后封存清单。原水位、外露号和未决原操作与切换记录在同一事务提交。
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_number_allocation_v1.cutovers(rule_id,approval,allocator,period_count,slot_count,operation_count)
            VALUES({scope.RuleId},{Encode(source.ApprovalRef)},{source.OriginalAllocator},
                   {source.Periods.Count},{source.Slots.Count},{source.Operations.Count})
            """, ct);
    }
}
