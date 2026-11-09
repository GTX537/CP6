using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration.StockOutbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Integration;

/// <summary>Internal Main journal on the Owner's actual PostgreSQL transaction. Never commits or calls transport.</summary>
internal sealed class MainOwnerOperationJournalV1(CP6Context db, bool stockTenantWriter = false)
{
    internal sealed record Original
    {
        public Guid Actor { get; init; }
        public string Contract { get; init; } = "";
        public string State { get; init; } = "";
        public byte[]? Input { get; init; }
        public string? Digest { get; init; }
        public byte[] Result { get; init; } = [];
        public Guid[] Objects { get; init; } = [];
    }
    private static void RequireTransaction(DbContext db, Guid tenant)
    {
        if (!db.Database.IsNpgsql() || tenant == Guid.Empty
            || db.Database.CurrentTransaction?.GetDbTransaction() is not NpgsqlTransaction tx
            || !ReferenceEquals(tx.Connection, db.Database.GetDbConnection())
            || tx.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable))
            throw new InvalidOperationException("MAIN_OWNER_TRANSACTION_REQUIRED");
    }
    internal Task<Original?> Read(string owner, Guid operation, CancellationToken ct) =>
        Read(db, db.CurrentTenantId, owner, operation, ct);

    internal static async Task<Original?> Read(DbContext db, Guid tenant, string owner, Guid operation, CancellationToken ct)
    {
        RequireTransaction(db, tenant);
        return await db.Database.SqlQuery<Original>($"""
            SELECT actor_id AS "Actor",contract AS "Contract",state AS "State",input_bytes AS "Input",
              input_sha256 AS "Digest",result_bytes AS "Result",object_ids AS "Objects"
            FROM cp6_main_owner_v1.operations WHERE tenant_id={tenant} AND owner={owner} AND operation_id={operation}
            """).SingleOrDefaultAsync(ct);
    }
    internal static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);
    internal static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static bool Matches(Original original, byte[] input)
        => original.Digest == Digest(input) && original.Input != null && original.Input.AsSpan().SequenceEqual(input);
    internal async Task Store(string owner, string contract, Guid operation, Guid actor, byte[]? input,
        object result, Guid[] objects, object audit, CancellationToken ct)
    {
        RequireTransaction(db, db.CurrentTenantId);
        var state=input == null ? "CLOSED" : "COMMITTED";
        var digest=input == null ? null : Digest(input);
        var resultBytes=Encode(result);
        var auditJson=JsonSerializer.Serialize(audit);
        if (stockTenantWriter)
        {
            if (owner != StockOutboxProtocolV1.SourceOwner)
                throw new InvalidOperationException("STOCK_ORIGINAL_OWNER_REQUIRED");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT cp6_stock_original_journal_v1.store({db.CurrentTenantId},{operation},{actor},{contract},{state},
                    {input},{digest},{resultBytes},{objects},CAST({auditJson} AS jsonb))
                """, ct);
            return;
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_main_owner_v1.operations
              (tenant_id,owner,operation_id,actor_id,contract,state,input_bytes,input_sha256,result_bytes,object_ids,audit_json)
            VALUES ({db.CurrentTenantId},{owner},{operation},{actor},{contract},{state},{input},{digest},
              {resultBytes},{objects},CAST({auditJson} AS jsonb))
            """,ct);
    }
}
