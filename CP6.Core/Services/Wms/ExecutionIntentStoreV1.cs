using System.Buffers.Binary;
using System.Data;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Services.Wms;

internal sealed partial class ExecutionIntentStoreV1
{
    private readonly CP6Context db;
    private readonly IDbContextTransaction transaction;
    private readonly Guid tenant;
    private readonly Space space;
    private readonly Authority authority;
    private readonly string key;
    private int busy;
    private static readonly ConditionalWeakTable<IDbContextTransaction, object> Sessions = new();
    private const string Where = "environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND authority_key=@k";
    private ExecutionIntentStoreV1(CP6Context db, Space space, Authority authority)
    {
        this.db = db; this.space = Freeze(space); this.authority = Freeze(authority);
        tenant = db.CurrentTenantId; transaction = db.Database.CurrentTransaction!; key = Hash(Canonical(authority));
    }
    private void Require()
    {
        if (!db.Database.IsNpgsql() || tenant == Guid.Empty || db.CurrentTenantId != tenant || transaction == null
            || !ReferenceEquals(transaction, db.Database.CurrentTransaction) || transaction.GetDbTransaction() is not NpgsqlTransaction tx
            || !ReferenceEquals(tx.Connection, db.Database.GetDbConnection()) || tx.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable)
            || System.Transactions.Transaction.Current != null) throw Error("EXECUTION_OWNER_TRANSACTION_REQUIRED");
    }
    private NpgsqlCommand Command(string sql, params (string Name, object Value)[] args)
    {
        Require(); var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());
        command.CommandTimeout = db.Database.GetCommandTimeout() ?? 30;
        command.Parameters.AddWithValue("e", space.EnvironmentId); command.Parameters.AddWithValue("s", space.SandboxScopeId ?? Guid.Empty);
        command.Parameters.AddWithValue("t", tenant); command.Parameters.AddWithValue("k", key);
        foreach (var (name, value) in args)
            if (value is DBNull) command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Bytea) { Value = DBNull.Value });
            else command.Parameters.AddWithValue(name, value);
        return command;
    }
    private async Task Execute(string sql, CancellationToken ct, params (string Name, object Value)[] args)
    { await using var command = Command(sql, args); await command.ExecuteNonQueryAsync(ct); }
    private async Task<T> Atomic<T>(Func<Task<T>> work, CancellationToken ct)
    {
        Require(); if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw Error("EXECUTION_CONCURRENT_SESSION_USE");
        try
        {
            await transaction.CreateSavepointAsync("execution_intent", ct);
            try { var result = await work(); Require(); await transaction.ReleaseSavepointAsync("execution_intent", ct); return result; }
            catch { await transaction.RollbackToSavepointAsync("execution_intent", CancellationToken.None); throw; }
        }
        finally { Volatile.Write(ref busy, 0); }
    }
    internal static async Task<ExecutionIntentStoreV1> Open(CP6Context db, Space space, Authority authority, CancellationToken ct = default)
    {
        if (space == null || space.EnvironmentId == Guid.Empty || space.Mode is not ("PRODUCTION" or "SIMULATION")
            || (space.Mode == "PRODUCTION" ? space.SandboxScopeId != null : space.SandboxScopeId == null || space.SandboxScopeId == Guid.Empty)) throw Error("EXECUTION_ENVIRONMENT_INVALID");
        if (authority == null || authority.Id == Guid.Empty) throw Error("EXECUTION_AUTHORITY_INVALID"); Text(authority.Kind);
        var store = new ExecutionIntentStoreV1(db, space, authority); store.Require();
        lock (Sessions) { if (Sessions.TryGetValue(store.transaction, out _)) throw Error("EXECUTION_SESSION_ALREADY_OPEN"); Sessions.Add(store.transaction, new()); }
        var hash = SHA256.HashData(Canonical(new { space.EnvironmentId, Sandbox = space.SandboxScopeId ?? Guid.Empty, store.tenant, store.key }));
        await store.Execute("SELECT pg_advisory_xact_lock(@lock)", ct, ("lock", BinaryPrimitives.ReadInt64BigEndian(hash)));
        return store;
    }
    private async Task<Intent?> Find(CancellationToken ct)
    {
        await using var command = Command($"SELECT intent_id,state,revision,execution_epoch,intent_digest,intent_bytes,decision_bytes FROM cp6_execution_v1.intents WHERE {Where} FOR UPDATE");
        await using var r = await command.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new(r.GetGuid(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetString(4), Decode<Plan>(r.GetFieldValue<byte[]>(5)), r.IsDBNull(6) ? null : TextBytes(r.GetFieldValue<byte[]>(6)));
    }
    private async Task<Intent> Current(CancellationToken ct) => await Find(ct) ?? throw Error("EXECUTION_NOT_FOUND");
    internal Task<Intent?> Read(CancellationToken ct = default) => Atomic(() => Find(ct), ct);
    internal Task<Intent> Admit(Alias alias, Plan input, CancellationToken ct = default) => Atomic(async () =>
    {
        if (alias == null) throw Error("EXECUTION_ALIAS_INVALID"); Text(alias.Owner); Text(alias.Key);
        var plan = Normalize(input); var bytes = Canonical(plan); var intent = await Find(ct);
        if (intent != null && intent.IntentDigest != Hash(bytes)) throw Error("EXECUTION_AUTHORITY_INPUT_CONFLICT");
        if (intent == null)
        {
            var id = Guid.NewGuid();
            await Execute("""
                INSERT INTO cp6_execution_v1.intents(environment_id,sandbox_id,tenant_id,authority_key,authority_bytes,intent_id,intent_bytes,intent_digest,required_count,state,revision,execution_epoch)
                VALUES(@e,@s,@t,@k,@authority,@id,@body,@digest,@count,'OPEN',1,1)
                """, ct, ("authority", Canonical(authority)), ("id", id), ("body", bytes), ("digest", Hash(bytes)), ("count", plan.RequiredOwners.Length));
            intent = (await Find(ct))!;
            foreach (var target in plan.RequiredOwners)
            {
                var bindingId = Guid.NewGuid(); var request = new Prepare(Guid.NewGuid(), bindingId, id, authority, intent.IntentDigest, plan.CandidateDigest, plan.ScopeDigest, target);
                var binding = new Binding(bindingId, request, Hash(Canonical(request)));
                await Execute("""
                    INSERT INTO cp6_execution_v1.bindings(environment_id,sandbox_id,tenant_id,authority_key,binding_id,owner,scope_key,protection_kind,prepare_key,body_bytes,body_digest)
                    VALUES(@e,@s,@t,@k,@id,@owner,@scope,@kind,@request,@body,@digest)
                    """, ct, ("id", bindingId), ("owner", target.Owner), ("scope", target.ScopeKey), ("kind", target.ProtectionKind), ("request", request.RequestKey), ("body", Bytes(binding)), ("digest", Hash(Bytes(binding))));
                await AddAction(binding, "PREPARE", Canonical(binding), ct);
            }
        }
        var aliasKey = Hash(Canonical(alias));
        await Execute("""
            INSERT INTO cp6_execution_v1.aliases(environment_id,sandbox_id,tenant_id,authority_key,alias_key,body_bytes)
            VALUES(@e,@s,@t,@k,@alias,@body) ON CONFLICT DO NOTHING
            """, ct, ("alias", aliasKey), ("body", Canonical(alias)));
        await using (var command = Command("SELECT authority_key FROM cp6_execution_v1.aliases WHERE environment_id=@e AND sandbox_id=@s AND tenant_id=@t AND alias_key=@alias", ("alias", aliasKey)))
            if ((string?)await command.ExecuteScalarAsync(ct) != key) throw Error("EXECUTION_ALIAS_ALREADY_BOUND");
        return intent;
    }, ct);
    private async Task Move(string state, long epoch, byte[]? decision, CancellationToken ct)
    {
        await Execute($"UPDATE cp6_execution_v1.intents SET state=@state,revision=revision+1,execution_epoch=@epoch,decision_bytes=@decision WHERE {Where}", ct,
            ("state", state), ("epoch", epoch), ("decision", decision == null ? DBNull.Value : decision));
    }
    internal Task<Intent> TakeOver(long revision, long epoch, CancellationToken ct = default) => Atomic(async () =>
    {
        var i = await Current(ct); Expected(i, revision, epoch);
        await Move("PREPARING", i.State == "OPEN" ? epoch : checked(epoch + 1), null, ct); return await Current(ct);
    }, ct);
    internal Task<Intent> Ready(long revision, long epoch, CancellationToken ct = default) => Atomic(async () =>
    {
        var i = await Current(ct); Expected(i, revision, epoch); if (i.State != "PREPARING") throw Error("EXECUTION_NOT_PREPARING");
        await RequireProtected(ct); await Move("READY", epoch, null, ct); return await Current(ct);
    }, ct);
    internal Task<string> Apply(long revision, long epoch, Func<CancellationToken, Task<EffectResult>> write, CancellationToken ct = default) => Atomic(async () =>
    {
        var i = await Current(ct); if (i.State == "APPLIED") return i.Decision!;
        Expected(i, revision, epoch); if (i.State != "READY") throw Error("EXECUTION_NOT_READY"); await RequireProtected(ct);
        // The adopting Owner rechecks current permission, original sources and native controls here,
        // under its real locks, and writes its immutable receipt in this same transaction.
        var effect = Freeze(await write(ct)); Require(); if (effect == null) throw Error("EXECUTION_EFFECT_RESULT_REQUIRED"); Ref(effect.WmsReceipt);
        using var parsed = System.Text.Json.JsonDocument.Parse(effect.CommandResultJson);
        var decision = Canonical(new { executionIntentId = i.Id, authority, intentDigest = i.IntentDigest, scopeDigest = i.Plan.ScopeDigest,
            terminalState = "APPLIED", revision = checked(i.Revision + 1), executionEpoch = i.ExecutionEpoch, effect,
            coverage = "PARTICIPATING_WRITERS_ONLY", externalClosureAuthority = false });
        await Move("APPLIED", epoch, decision, ct);
        foreach (var binding in await BindingsCore(ct)) await AddAction(binding, "CONFIRM_COMMITTED", Canonical(new { binding, wmsDecision = TextBytes(decision) }), ct);
        return TextBytes(decision);
    }, ct);
    internal Task<string> Seal(long revision, string reasonCode, CancellationToken ct = default) => Atomic(async () =>
    {
        Text(reasonCode); var i = await Current(ct); if (Terminal(i)) return i.Decision!;
        if (i.Revision != revision) throw Error("EXECUTION_FENCE_CONFLICT");
        await using var clock = Command("SELECT clock_timestamp()"); var at = (DateTime)(await clock.ExecuteScalarAsync(ct))!;
        var decision = Canonical(new { executionIntentId = i.Id, authority, intentDigest = i.IntentDigest, scopeDigest = i.Plan.ScopeDigest,
            terminalState = "SEALED_NO_EFFECT", sealedRevision = checked(revision + 1), blockedThroughEpoch = i.ExecutionEpoch,
            aliasesCovered = "ALL_CURRENT_AND_FUTURE_FOR_AUTHORITY", closedAt = at, reasonCode,
            coverage = "PARTICIPATING_WRITERS_ONLY", externalClosureAuthority = false });
        await Move("SEALED_NO_EFFECT", checked(i.ExecutionEpoch + 1), decision, ct);
        foreach (var binding in await BindingsCore(ct)) await AddAction(binding, "RELEASE_SEALED", Canonical(new { binding, closureDecision = TextBytes(decision) }), ct);
        return TextBytes(decision);
    }, ct);
}
