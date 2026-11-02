using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

internal sealed partial class NumberAllocationStoreV1
{
    internal async Task<NumberAllocationRuleV1?> CurrentRule(CancellationToken ct)
    {
        var body = await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_number_allocation_v1.rule_versions
              WHERE rule_id={scope.RuleId} ORDER BY revision DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        return body == null ? null : JsonSerializer.Deserialize<NumberAllocationRuleV1>(body)!;
    }
    internal Task<bool> HasVersion(string version, CancellationToken ct) => db.Database.SqlQuery<bool>($"""
        SELECT EXISTS(SELECT FROM cp6_number_allocation_v1.rule_versions
          WHERE rule_id={scope.RuleId} AND version_hash={Key(version)}) AS "Value"
        """).SingleAsync(ct);
    internal async Task<NumberAllocationRuleV1?> HistoricalRule(string version, CancellationToken ct)
    {
        var body = await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_number_allocation_v1.rule_versions
              WHERE rule_id={scope.RuleId} AND version_hash={Key(version)}
            """).SingleOrDefaultAsync(ct);
        if (body == null) return null;
        var rule = JsonSerializer.Deserialize<NumberAllocationRuleV1>(body)!;
        if (rule.Version != version) throw new NumberAllocationExceptionV1("NUMBER_RULE_VERSION_CONFLICT");
        return rule;
    }
    internal Task<Saved?> OriginalChange(string operation, CancellationToken ct) => db.Database.SqlQuery<Saved>($"""
        SELECT actor_id AS "Actor",operation_bytes AS "Operation",input_bytes AS "Input",result_bytes AS "Result"
          FROM cp6_number_allocation_v1.rule_changes WHERE rule_id={scope.RuleId} AND operation_hash={Key(operation)}
        """).SingleOrDefaultAsync(ct);
    internal async Task SaveChange(NumberAllocationRuleV1 current, NumberRuleChangeCommandV1 command,
        Guid actor, NumberRuleChangeReceiptV1 receipt, CancellationToken ct)
    {
        // Existing W254 registration stays byte-for-byte intact. On its first change, retain
        // the original rule as revision zero; the latest append is the sole current pointer.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_number_allocation_v1.rule_versions(rule_id,revision,version_hash,body)
            SELECT {scope.RuleId},0,{Key(current.Version)},{Encode(current)}
              WHERE NOT EXISTS(SELECT FROM cp6_number_allocation_v1.rule_versions WHERE rule_id={scope.RuleId})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_number_allocation_v1.rule_versions(rule_id,revision,version_hash,body)
            SELECT {scope.RuleId},max(revision)+1,{Key(receipt.Rule.Version)},{Encode(receipt.Rule)}
              FROM cp6_number_allocation_v1.rule_versions WHERE rule_id={scope.RuleId};
            INSERT INTO cp6_number_allocation_v1.rule_changes
              (rule_id,operation_hash,operation_bytes,input_bytes,actor_id,result_bytes,version_hash,changed_at)
            VALUES({scope.RuleId},{Key(command.OperationRef)},{Encode(command.OperationRef)},{Encode(command)},
              {actor},{Encode(receipt)},{Key(receipt.Rule.Version)},{receipt.ChangedAtUtc})
            """, ct);
    }
}
