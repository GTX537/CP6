using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Core.Services.Pub;

internal sealed partial class NumberAllocationStoreV1(CP6Context db, NumberAllocationScopeV1 scope)
{
    internal sealed record Registration(NumberAllocationScopeV1 Scope, NumberAllocationRuleV1 Rule);
    internal sealed record Saved(Guid Actor, byte[] Operation, byte[] Input, byte[] Result);
    internal static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);
    internal static byte[] Key(string value) => SHA256.HashData(new UTF8Encoding(false, true).GetBytes(value));
    internal async Task<Registration?> RegistrationAsync(CancellationToken ct)
    {
        var bytes = await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_number_allocation_v1.scopes
              WHERE rule_id={scope.RuleId} AND tenant_id={scope.TenantId} FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        return bytes == null ? null : JsonSerializer.Deserialize<Registration>(bytes)!;
    }
    internal Task<Saved?> Original(string operation, CancellationToken ct) => db.Database.SqlQuery<Saved>($"""
        SELECT actor_id AS "Actor",operation_bytes AS "Operation",input_bytes AS "Input",result_bytes AS "Result"
          FROM cp6_number_allocation_v1.receipts WHERE rule_id={scope.RuleId} AND operation_hash={Key(operation)}
        """).SingleOrDefaultAsync(ct);
    internal async Task<int?> Counter(string period, CancellationToken ct)
    {
        try
        {
            var current = await db.Database.SqlQuery<int?>($"""
                SELECT current_value AS "Value" FROM cp6_number_allocation_v1.periods
                  WHERE rule_id={scope.RuleId} AND period_key={period} FOR UPDATE
                """).SingleOrDefaultAsync(ct);
            if (scope.CutoverBasis != NumberAllocationOwnerV1.ApprovedPubCutover) return current;
            await RequireCutoverInventory(ct);
            if (current != null) return current;
            if (await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT FROM cp6_number_allocation_v1.receipts
                 WHERE rule_id={scope.RuleId} AND period_key={period}) AS "Value"
                """).SingleAsync(ct))
                throw new NumberAllocationExceptionV1("NUMBER_COUNTER_UNAVAILABLE", 503);
            return await db.Database.SqlQuery<int?>($"""
                SELECT NULLIF(last_issued,0) AS "Value" FROM cp6_number_allocation_v1.prior_periods
                 WHERE rule_id={scope.RuleId} AND period_key={period}
                """).SingleOrDefaultAsync(ct);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        { throw new NumberAllocationExceptionV1("NUMBER_COUNTER_UNAVAILABLE", 503, e); }
    }
    internal async Task Advance(string period, int? previous, int next, CancellationToken ct)
    {
        if (previous == null)
        {
            if (await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_number_allocation_v1.receipts WHERE rule_id={scope.RuleId} AND period_key={period}) AS \"Value\"").SingleAsync(ct))
                throw new NumberAllocationExceptionV1("NUMBER_COUNTER_UNAVAILABLE", 503);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_number_allocation_v1.periods(rule_id,period_key,current_value) VALUES({scope.RuleId},{period},{next})", ct);
        }
        else
        {
            var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_number_allocation_v1.periods SET current_value={next}
                 WHERE rule_id={scope.RuleId} AND period_key={period} AND current_value={previous.Value}
                """, ct);
            if (changed == 0 && scope.CutoverBasis == NumberAllocationOwnerV1.ApprovedPubCutover)
                changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_number_allocation_v1.periods(rule_id,period_key,current_value)
                    SELECT rule_id,period_key,{next} FROM cp6_number_allocation_v1.prior_periods
                     WHERE rule_id={scope.RuleId} AND period_key={period} AND last_issued={previous.Value}
                    """, ct);
            if (changed != 1) throw new NumberAllocationExceptionV1("NUMBER_COUNTER_UNAVAILABLE", 503);
        }
    }
    internal async Task Save(NumberAllocationCommandV1 command, Guid actor, NumberAllocationReceiptV1 receipt, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_number_allocation_v1.receipts
                (rule_id,operation_hash,operation_bytes,input_bytes,actor_id,result_bytes,period_key,sequence_value,number_hash,number_bytes,allocated_at,rule_version_hash)
                VALUES({scope.RuleId},{Key(command.OperationRef)},{Encode(command.OperationRef)},{Encode(command)},
                  {actor},{Encode(receipt)},{receipt.PeriodKey},{receipt.SequenceValue},{Key(receipt.Number)},{Encode(receipt.Number)},{receipt.AllocatedAtUtc},{Key(receipt.RuleVersion)})
                """, ct);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation && e.ConstraintName == "number_unique")
        { throw new NumberAllocationExceptionV1("NUMBER_HISTORY_COLLISION", 409, e); }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.CheckViolation && e.ConstraintName == "number_predecessor_history")
        { throw new NumberAllocationExceptionV1("NUMBER_PREDECESSOR_HISTORY", 409, e); }
    }
}
