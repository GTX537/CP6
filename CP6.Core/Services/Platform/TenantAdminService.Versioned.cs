using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Platform;

public partial class TenantAdminService
{
    private static byte[] ManagementBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);
    private static byte[] ManagementKey(string operation) => SHA256.HashData(ManagementBytes(operation));
    private static void ManagementOperation(string operation)
    {
        if (string.IsNullOrEmpty(operation)) throw new TenantManagementExceptionV1("TENANT_OPERATION_REQUIRED", 400);
        try { _ = new UTF8Encoding(false, true).GetByteCount(operation); }
        catch (EncoderFallbackException e) { throw new TenantManagementExceptionV1("TENANT_OPERATION_INVALID", 400, e); }
    }
    // Each public V1 call owns a fresh unit of work. currentSession is supplied by the host,
    // and uses this context to validate the original authenticated session under the IAM gate.
    private async Task<T> ManagementTransaction<T>(Guid id, bool write, TenantManagementSessionV1 currentSession,
        Func<PlatformAdminAuthorization.Actor, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new TenantManagementExceptionV1("TENANT_ID_REQUIRED", 400);
        if (!_db.Database.IsNpgsql() || _db.Database.CurrentTransaction != null || _db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null)
            throw new TenantManagementExceptionV1("TENANT_INDEPENDENT_CONTEXT_REQUIRED", 503);
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var committing = false;
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'", ct);
            await AuthenticationCompletionGateV1.AcquireAsync(_db, ct);
            var session = await currentSession.CurrentActor(ct);
            var actor = await PlatformAdminAuthorization.RequireAsync(_db, _http?.HttpContext?.User, ct, lockActor: true);
            if (session != actor.Id) throw new TenantManagementExceptionV1("REAUTH_REQUIRED", 401);
            var authorizedUntil = await _db.Sys_Tenants.AsNoTracking().Where(x => x.Id == actor.TenantId).Select(x => x.ExpireDate).SingleAsync(ct);
            var result = await body(actor, ct);
            // The shared gate holds DB authority through commit. Check external logout/clock,
            // without rejecting session invalidation deliberately caused by our own effect.
            await currentSession.Complete(ct);
            if (authorizedUntil <= DateTime.Now) throw new TenantManagementExceptionV1("REAUTH_REQUIRED", 401);
            ct.ThrowIfCancellationRequested(); committing = true; await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception error)
        {
            if (write && committing) throw new TenantManagementExceptionV1("TENANT_OUTCOME_UNKNOWN", 503, error);
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new TenantManagementExceptionV1("TENANT_OUTCOME_UNKNOWN", 503, new AggregateException(error, rollback)); }
            throw;
        }
    }

    private Task<TenantManagementSnapshotV1?> ManagementSnapshot(Guid id, CancellationToken ct)
        => _db.Database.SqlQuery<TenantManagementSnapshotV1>($"""
            SELECT t."Id",t."TenantCode",t."TenantName",t."Enable",t."ExpireDate",t."Remark",t."TimeZoneId",v.revision AS "Revision"
            FROM public."Sys_Tenants" t JOIN cp6_tenant_management_v1.versions v ON v.tenant_id=t."Id"
            WHERE t."Id"={id}
            """).SingleOrDefaultAsync(ct);

    private sealed class ManagementSaved
    {
        public byte[] Operation { get; set; } = []; public byte[] Input { get; set; } = [];
        public Guid Actor { get; set; } public Guid ActorTenant { get; set; } public byte[] Result { get; set; } = [];
    }
    private Task<ManagementSaved?> ManagementOriginal(Guid id, string operation, CancellationToken ct)
        => _db.Database.SqlQuery<ManagementSaved>($"""
            SELECT operation_bytes AS "Operation", input_bytes AS "Input", actor_id AS "Actor",actor_tenant AS "ActorTenant",result_bytes AS "Result"
            FROM cp6_tenant_management_v1.commands WHERE tenant_id={id} AND operation_hash={ManagementKey(operation)}
            """).SingleOrDefaultAsync(ct);
    private static TenantManagementReceiptV1 ManagementReceipt(ManagementSaved saved, PlatformAdminAuthorization.Actor actor, string operation, byte[]? input)
    {
        if (saved.Actor != actor.Id || saved.ActorTenant != actor.TenantId) throw new TenantManagementExceptionV1("TENANT_ORIGINAL_FORBIDDEN", 403);
        if (!saved.Operation.AsSpan().SequenceEqual(ManagementBytes(operation)) || input != null && !saved.Input.AsSpan().SequenceEqual(input))
            throw new TenantManagementExceptionV1("TENANT_OPERATION_CONFLICT");
        return JsonSerializer.Deserialize<TenantManagementReceiptV1>(saved.Result)!;
    }
    public Task<TenantManagementSnapshotV1?> ReadManagementAsync(Guid id, TenantManagementSessionV1 currentSession, CancellationToken ct = default)
        => ManagementTransaction(id, false, currentSession, (_, token) => ManagementSnapshot(id, token), ct);
    public Task<TenantManagementReceiptV1?> ReadManagementOperationAsync(Guid id, string operation, TenantManagementSessionV1 currentSession, CancellationToken ct = default)
    {
        ManagementOperation(operation);
        return ManagementTransaction<TenantManagementReceiptV1?>(id, false, currentSession, async (actor, token) =>
        {
            var original = await ManagementOriginal(id, operation, token);
            return original == null ? null : ManagementReceipt(original, actor, operation, null);
        }, ct);
    }
    public Task<TenantManagementReceiptV1> ExecuteManagementAsync(Guid id, TenantManagementCommandV1 command, TenantManagementSessionV1 currentSession, CancellationToken ct = default)
    {
        ManagementOperation(command.OperationRef);
        if (command.ExpectedRevision <= 0 || command.Kind is not ("update" or "suspend" or "reactivate")
            || (command.Kind == "update") != (command.Configuration != null))
            throw new TenantManagementExceptionV1("TENANT_COMMAND_INVALID", 400);
        var input = ManagementBytes(command);
        return ManagementTransaction(id, true, currentSession, async (actor, token) =>
        {
            var original = await ManagementOriginal(id, command.OperationRef, token);
            if (original != null) return ManagementReceipt(original, actor, command.OperationRef, input);
            var current = await ManagementSnapshot(id, token) ?? throw new TenantManagementExceptionV1("TENANT_NOT_FOUND", 404);
            if (current.Revision != command.ExpectedRevision) throw new TenantManagementExceptionV1("TENANT_VERSION_CHANGED", 412);
            if (current.Revision == long.MaxValue) throw new TenantManagementExceptionV1("TENANT_REVISION_EXHAUSTED", 422);
            if (command.Configuration is { } config)
            {
                ValidateManagementConfiguration(config);
                await UpdateCoreAsync(id, config.Name, config.ExpireDate, config.Remark, config.TimeZoneId, actor, token);
            }
            else await SetEnableAsync(id, command.Kind == "reactivate",
                command.Kind == "reactivate" ? SecurityEventType.TenantReactivated : SecurityEventType.TenantSuspended, actor, token);
            var snapshot = (await ManagementSnapshot(id, token))!;
            var at = await _db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(token);
            var result = new TenantManagementReceiptV1(command.OperationRef, command.Kind, snapshot, at);
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_tenant_management_v1.commands(tenant_id,operation_hash,operation_bytes,input_bytes,actor_id,actor_tenant,result_bytes)
                VALUES({id},{ManagementKey(command.OperationRef)},{ManagementBytes(command.OperationRef)},{input},{actor.Id},{actor.TenantId},{ManagementBytes(result)})
                """, token);
            return result;
        }, ct);
    }
    private static void ValidateManagementConfiguration(TenantConfigurationV1 config)
    {
        if (string.IsNullOrWhiteSpace(config.Name) || config.Name.Length > 200 || config.Remark?.Length > 500 || config.TimeZoneId?.Length > 64)
            throw new TenantManagementExceptionV1("TENANT_CONFIGURATION_INVALID", 400);
        foreach (var text in new[] { config.Name, config.Remark, config.TimeZoneId })
        {
            if (text is null) continue;
            if (text.Contains('\0')) throw new TenantManagementExceptionV1("TENANT_CONFIGURATION_INVALID", 400);
            try { _ = new UTF8Encoding(false, true).GetByteCount(text); }
            catch (EncoderFallbackException e) { throw new TenantManagementExceptionV1("TENANT_CONFIGURATION_INVALID", 400, e); }
        }
        // The existing store is timestamp without time zone, with PostgreSQL microsecond precision.
        if (config.ExpireDate is { } date && (date.Kind != DateTimeKind.Unspecified || date.Ticks % 10 != 0))
            throw new TenantManagementExceptionV1("TENANT_EXPIRY_REQUIRES_LOCAL_MICROSECONDS", 400);
    }
}
