using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Wms;

/// <summary>Recorded capacity facts within one actual Main transaction; never an authorization or coverage assessment.</summary>
internal sealed partial class CapacityOccupancyStoreV1
{
    private readonly CP6Context db;
    private readonly IDbContextTransaction transaction;
    private readonly Guid tenant;
    private readonly Space space;
    private readonly Dictionary<string, Domain> domains;
    private int busy;
    private Guid Sandbox => space.SandboxScopeId ?? Guid.Empty;
    private static readonly ConditionalWeakTable<IDbContextTransaction, object> Sessions = new();
    private CapacityOccupancyStoreV1(CP6Context db, Space space, Dictionary<string, Domain> domains)
    { this.db = db; this.space = space; this.domains = domains; tenant = db.CurrentTenantId; transaction = db.Database.CurrentTransaction!; }

    private void Require(Domain? domain = null)
    {
        if (!db.Database.IsNpgsql() || tenant == Guid.Empty || db.CurrentTenantId != tenant
            || transaction == null || !ReferenceEquals(transaction, db.Database.CurrentTransaction)
            || transaction.GetDbTransaction() is not NpgsqlTransaction t || !ReferenceEquals(t.Connection, db.Database.GetDbConnection())
            || t.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable)
            || System.Transactions.Transaction.Current != null) throw Error("CAPACITY_OWNER_TRANSACTION_REQUIRED");
        if (domain != null && (!domains.TryGetValue(DomainKey(domain), out var declared) || declared != domain))
            throw Error("CAPACITY_DOMAIN_NOT_DECLARED");
    }
    private NpgsqlCommand Command(string sql, params (string Name, object Value)[] parameters)
    {
        Require(); var cmd = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());
        cmd.CommandTimeout = db.Database.GetCommandTimeout() ?? 30;
        cmd.Parameters.AddWithValue("t", tenant); cmd.Parameters.AddWithValue("e", space.EnvironmentId); cmd.Parameters.AddWithValue("s", Sandbox);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
    private async Task Execute(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    { await using var cmd = Command(sql, parameters); await cmd.ExecuteNonQueryAsync(ct); }

    internal static async Task<CapacityOccupancyStoreV1> Open(CP6Context db, Space space, IEnumerable<Domain> declared, CancellationToken ct = default)
    {
        if (space == null || space.EnvironmentId == Guid.Empty || space.Mode is not ("PRODUCTION" or "SIMULATION")
            || (space.Mode == "PRODUCTION" ? space.SandboxScopeId != null : space.SandboxScopeId is null || space.SandboxScopeId == Guid.Empty))
            throw Error("CAPACITY_ENVIRONMENT_INVALID");
        var map = new Dictionary<string, Domain>(StringComparer.Ordinal);
        foreach (var d in declared.Select(Freeze))
        {
            var key = DomainKey(d);
            if (map.TryGetValue(key, out var old) && old != d) throw Error("CAPACITY_DOMAIN_IDENTITY_CONFLICT");
            map[key] = d;
        }
        if (map.Count == 0) throw Error("CAPACITY_DOMAIN_REQUIRED");
        var store = new CapacityOccupancyStoreV1(db, Freeze(space), map); store.Require();
        lock (Sessions)
        { if (Sessions.TryGetValue(store.transaction, out _)) throw Error("CAPACITY_LOCK_PLAN_ALREADY_OPEN"); Sessions.Add(store.transaction, new()); }
        await store.transaction.CreateSavepointAsync("capacity_open", ct);
        try
        {
            foreach (var (key, d) in map.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var hash = SHA256.HashData(Canonical(new { space.EnvironmentId, store.Sandbox, store.tenant, key }));
                await store.Execute("SELECT pg_advisory_xact_lock(@lock)", ct, ("lock", BinaryPrimitives.ReadInt64BigEndian(hash)));
                var body = Canonical(d);
                await store.Execute("""
                    INSERT INTO cp6_capacity_v1.domains(environment_id,sandbox_id,tenant_id,domain_key,identity_bytes,identity_digest,revision,physical,unreceived)
                    VALUES(@e,@s,@t,@d,@body,@digest,0,0,0) ON CONFLICT DO NOTHING
                    """, ct, ("d", key), ("body", body), ("digest", Hash(body)));
                await using var cmd = store.Command("SELECT identity_bytes FROM cp6_capacity_v1.domains WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND domain_key=@d", ("d", key));
                var saved = (byte[])(await cmd.ExecuteScalarAsync(ct))!;
                if (!saved.SequenceEqual(body)) throw Error("CAPACITY_DOMAIN_IDENTITY_CONFLICT");
            }
            await store.transaction.ReleaseSavepointAsync("capacity_open", ct); return store;
        }
        catch { await store.transaction.RollbackToSavepointAsync("capacity_open", CancellationToken.None); throw; }
    }

    private async Task<Snapshot> SnapshotCore(Domain d, CancellationToken ct)
    {
        Require(d);
        await using var cmd = Command("SELECT revision,physical,unreceived FROM cp6_capacity_v1.domains WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND domain_key=@d FOR UPDATE", ("d", DomainKey(d)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw Error("CAPACITY_DOMAIN_MISSING");
        return new(r.GetInt64(0).ToString(CultureInfo.InvariantCulture), r.GetDecimal(1).ToString("F8", CultureInfo.InvariantCulture),
            r.GetDecimal(2).ToString("F8", CultureInfo.InvariantCulture), (r.GetDecimal(1) + r.GetDecimal(2)).ToString("F8", CultureInfo.InvariantCulture), d);
    }
    private void Enter() { if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw Error("CAPACITY_CONCURRENT_SESSION_USE"); }
    internal async Task<Snapshot> Read(Domain domain, CancellationToken ct = default)
    { Enter(); try { return await SnapshotCore(Freeze(domain), ct); } finally { Volatile.Write(ref busy, 0); } }

    private async Task<string> Mutate(Operation op, Domain d, string action, object input, Condition condition,
        Func<string, CancellationToken, Task<(BigInteger Physical, BigInteger Unreceived)>> write, CancellationToken ct)
    {
        Require(d); Text(op.Owner); Text(op.Key); Enter();
        var operation = Hash(Canonical(op)); var domain = DomainKey(d); var body = Canonical(new { action, input });
        try
        {
            await transaction.CreateSavepointAsync("capacity_mutation", ct);
            try
            {
                await using (var cmd = Command("SELECT input_bytes,result_bytes FROM cp6_capacity_v1.operations WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND domain_key=@d AND operation_key=@op", ("d", domain), ("op", operation)))
                await using (var r = await cmd.ExecuteReaderAsync(ct))
                    if (await r.ReadAsync(ct))
                    {
                        if (!r.GetFieldValue<byte[]>(0).SequenceEqual(body)) throw Error("CAPACITY_OPERATION_INPUT_CONFLICT");
                        var result = System.Text.Encoding.UTF8.GetString(r.GetFieldValue<byte[]>(1));
                        await r.DisposeAsync(); await transaction.ReleaseSavepointAsync("capacity_mutation", ct); return result;
                    }
                if (condition == null) throw Error("CAPACITY_CURRENT_CONDITION_REQUIRED");
                Ref(condition.Authority); var expected = Revision(condition.ExpectedRevision);
                var limit = condition.Limit == null ? (BigInteger?)null : Quantity(condition.Limit);
                var before = await SnapshotCore(d, ct);
                if (Revision(before.Revision) != expected) throw Error("CAPACITY_REVISION_CONFLICT");
                if (expected == long.MaxValue) throw Error("CAPACITY_REVISION_EXHAUSTED");
                var delta = await write(operation, ct);
                var physical = Quantity(before.Physical) + delta.Physical; var unreceived = Quantity(before.Unreceived) + delta.Unreceived;
                var total = physical + unreceived; QuantityText(physical); QuantityText(unreceived); QuantityText(total);
                if (limit != null && total > limit.Value) throw Error("CAPACITY_LIMIT_EXCEEDED");
                var after = new Snapshot((expected + 1).ToString(CultureInfo.InvariantCulture), QuantityText(physical), QuantityText(unreceived), QuantityText(total), d);
                var resultBytes = Bytes(new Receipt(space, tenant, op, action, Hash(body), after));
                await Execute("""
                    INSERT INTO cp6_capacity_v1.operations(environment_id,sandbox_id,tenant_id,domain_key,operation_key,input_bytes,input_digest,condition_bytes,result_bytes,result_digest)
                    VALUES(@e,@s,@t,@d,@op,@input,@inputDigest,@condition,@result,@resultDigest)
                    """, ct, ("d", domain), ("op", operation), ("input", body), ("inputDigest", Hash(body)), ("condition", Bytes(condition)), ("result", resultBytes), ("resultDigest", Hash(resultBytes)));
                await Execute("""
                    UPDATE cp6_capacity_v1.domains SET revision=@revision,physical=@physical,unreceived=@unreceived
                    WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND domain_key=@d
                    """, ct, ("d", domain), ("revision", expected + 1), ("physical", Number(physical)), ("unreceived", Number(unreceived)));
                await transaction.ReleaseSavepointAsync("capacity_mutation", ct);
                return System.Text.Encoding.UTF8.GetString(resultBytes);
            }
            catch { await transaction.RollbackToSavepointAsync("capacity_mutation", CancellationToken.None); throw; }
        }
        finally { Volatile.Write(ref busy, 0); }
    }
}
