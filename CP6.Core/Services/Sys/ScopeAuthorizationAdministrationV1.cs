using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ScopeAuthorizationCommandV1(Guid OperationId, string ExpectedVersion, bool Enabled, string Reason);
public sealed record ScopeAuthorizationContextV1(Guid TenantId, Guid EnvironmentId, string Version,
    ScopeAuthorizationSnapshotV1? Mapping, string Adoption);
public sealed record ScopeAuthorizationRegisteredV1(string Owner, Guid OperationId, ScopeAuthorizationSnapshotV1 Mapping);
public sealed record ScopeAuthorizationOperationV1(Guid OperationId, string State, ScopeAuthorizationRegisteredV1? Result);

public sealed class ScopeAuthorizationAdministrationV1(CP6Context db, IHttpContextAccessor http, ScopeAuthorizationFenceV1 fence,
    Func<CancellationToken, Task<Guid>> currentActor)
{
    private const string Owner = "Main.ScopeAuthorization.v1", Contract = "Main.ScopeAuthorization.register.v1";
    private static ScopeAuthorizationExceptionV1 Error(string code, int status = 503) => new(code, status);
    private void Entry()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw Error("SCOPE_AUTHORITY_UNAVAILABLE");
        _ = fence.EnvironmentId;
    }
    private async Task<Guid> Actor(bool edit, CancellationToken ct)
    {
        // The host supplies current access-session validation, repeated after waiting and at completion.
        // An authenticated claims snapshot alone is not current IAM management authority.
        var actor = await currentActor(ct);
        if (actor != CurrentPermissionContext.RequireCurrentUser(db, http)
            || http.HttpContext!.User.HasClaim(c => c.Type == "impersonator_id")) throw Error("REAUTH_REQUIRED", 401);
        var current = await PostgreSqlPermissionReader.ReadAsync(db, actor, ct);
        if (current.UserId != actor
            || !await db.Sys_Users.AsNoTracking().AnyAsync(x => x.Id == actor && x.Enable && (x.LockedUntil == null || x.LockedUntil <= DateTime.Now), ct)
            || !await db.Sys_Tenants.AsNoTracking().AnyAsync(x => x.Id == db.CurrentTenantId && x.Enable && (x.ExpireDate == null || x.ExpireDate > DateTime.Now), ct))
            throw Error("REAUTH_REQUIRED", 401);
        if (edit && (!current.ActionKeys.Contains("pub-role-perm:edit") || !await
            (from a in db.Sys_MenuActions join m in db.Sys_Menus on a.MenuId equals m.MenuId
             where m.Enable && m.MenuKey == "pub-role-perm" && a.ActionCode == "edit" select a).AnyAsync(ct)))
            throw Error("ACTION_FORBIDDEN", 403);
        return actor;
    }
    public async Task<ScopeAuthorizationContextV1> ContextAsync(CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await fence.Root(ct); await Actor(true, ct);
        var mapping = await fence.ReadLocked(ct);
        return new(db.CurrentTenantId, fence.EnvironmentId, mapping?.RowVersion.ToString("D") ?? "absent", mapping,
            "NATIVE_WRITER_FOUNDATION; FORMAL_WORKFLOW_CONSUMERS_PENDING");
    }
    private ScopeAuthorizationRegisteredV1 Original(MainOwnerOperationJournalV1.Original original, Guid actor, Guid operation, byte[]? input)
    {
        if (original.Actor != actor || original.Contract != Contract) throw Error("OBJECT_FORBIDDEN", 403);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("COMMAND_BODY_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<ScopeAuthorizationRegisteredV1>(original.Result) ?? throw Error("ORIGINAL_RESULT_INVALID");
        if (result.Owner != Owner || result.OperationId != operation || result.Mapping.TenantId != db.CurrentTenantId
            || result.Mapping.EnvironmentId != fence.EnvironmentId) throw Error("ORIGINAL_RESULT_INVALID");
        return result;
    }
    public async Task<ScopeAuthorizationRegisteredV1> RegisterAsync(ScopeAuthorizationCommandV1 command, CancellationToken ct = default)
    {
        if (command == null || command.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 256
            || command.ExpectedVersion != "absent" && !Guid.TryParseExact(command.ExpectedVersion, "D", out _))
            throw Error("SCOPE_COMMAND_INVALID", 400);
        Entry(); var input = MainOwnerOperationJournalV1.Encode(new { EnvironmentId = fence.EnvironmentId, Command = command });
        await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'", ct);
            await fence.Root(ct); var actor = await Actor(false, ct); var journal = new MainOwnerOperationJournalV1(db);
            var original = await journal.Read(Owner, command.OperationId, ct);
            if (original != null) return Original(original, actor, command.OperationId, input);
            await Actor(true, ct);
            var before = await fence.ReadLocked(ct);
            if (command.ExpectedVersion != (before?.RowVersion.ToString("D") ?? "absent")) throw Error("SCOPE_MAPPING_CHANGED", 412);
            if (before == null)
            {
                if (!command.Enabled) throw Error("SCOPE_INITIAL_REGISTRATION_MUST_ENABLE", 422);
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_scope_authorization_v1.environment(singleton,environment_id) VALUES(true,{fence.EnvironmentId}) ON CONFLICT(singleton) DO NOTHING", ct);
                var domain = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_scope_authorization_v1.fences(tenant_id,environment_id,logical_domain,permission_domain,enabled,epoch,row_version,writer_profile)
                    VALUES({db.CurrentTenantId},{fence.EnvironmentId},'WF_AUTH',{domain},true,0,{Guid.NewGuid()},'MAIN_NATIVE_IAM_WF_V1')
                    """, ct);
            }
            else await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_scope_authorization_v1.fences SET enabled={command.Enabled}
                  WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND logical_domain='WF_AUTH'
                """, ct);
            var saved = await fence.ReadLocked(ct) ?? throw Error("SCOPE_MAPPING_REQUIRED");
            var result = new ScopeAuthorizationRegisteredV1(Owner, command.OperationId, saved);
            await journal.Store(Owner, Contract, command.OperationId, actor, input, result, [saved.PermissionDomain],
                new { command.Reason, command.ExpectedVersion, saved.EnvironmentId, saved.Epoch, saved.RowVersion, saved.WriterProfile }, ct);
            await fence.Root(ct);
            if (await Actor(true, ct) != actor) throw Error("REAUTH_REQUIRED", 401);
            // The final persisted proof check follows current-actor resolution as well.
            if (saved != await fence.ReadLocked(ct)) throw Error("SCOPE_AUTHORITY_CHANGED", 409);
            ct.ThrowIfCancellationRequested(); committing = true; await tx.CommitAsync(ct); return result;
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new ScopeAuthorizationExceptionV1("SCOPE_OPERATION_UNKNOWN", 503, new AggregateException(error, rollback)); }
            if (committing) throw new ScopeAuthorizationExceptionV1("SCOPE_OPERATION_UNKNOWN", 503, error);
            if (error is Npgsql.NpgsqlException or CurrentPermissionUnavailableException)
                throw new ScopeAuthorizationExceptionV1("SCOPE_AUTHORITY_UNAVAILABLE", 503, error);
            throw;
        }
    }
    public async Task<ScopeAuthorizationOperationV1> OperationAsync(Guid operation, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var actor = await Actor(false, ct);
        var original = await new MainOwnerOperationJournalV1(db).Read(Owner, operation, ct);
        return new(operation, original == null ? "NOT_OBSERVED" : "SUCCEEDED",
            original == null ? null : Original(original, actor, operation, null));
    }
}
