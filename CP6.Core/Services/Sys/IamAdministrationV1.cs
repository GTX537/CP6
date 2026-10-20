using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed class IamAdministrationExceptionV1(int status, string code) : InvalidOperationException(code)
{
    public int Status { get; } = status;
}

public record IamCommandV1
{
    public Guid OperationId { get; init; }
    public string? ExpectedVersion { get; init; }
    public string? ExpectedReferencesVersion { get; init; }
    public string? Reason { get; init; }
}

public sealed record IamOperationV1(Guid OperationId, string Kind, string State, Guid? EntityId = null,
    string? Version = null, string? ReferencesVersion = null, string? TreeVersion = null, int? RoleId = null);

/// <summary>Main's existing authentication root serializes actual reference writers and current authorization.</summary>
public sealed class IamAdministrationV1(CP6Context db, ICurrentPermissionContext permissions, IDataProtectionProvider protection)
{
    private readonly IDataProtector fingerprint = protection.CreateProtector("CP6.Iam.Administration.Request.V1");
    public bool IsPostgreSql => db.Database.IsNpgsql();

    public async Task<T> ExecuteAsync<T>(string permission, Func<CancellationToken,Task<Guid>> currentActor,
        Func<Guid,CancellationToken,Task<T>> body, CancellationToken ct)
    {
        if (!IsPostgreSql || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()) throw Error(503,"IAM_AUTHORITY_UNAVAILABLE");
        await using var tx = await db.Database.BeginTransactionAsync(ct); var attempted = false;
        try
        {
            await AuthenticationCompletionGateV1.AcquireAsync(db,ct);
            var actor = await currentActor(ct);
            var user = await db.Sys_Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor && x.TenantId == db.CurrentTenantId,ct);
            var tenant = await db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == db.CurrentTenantId,ct);
            var current = await permissions.GetAsync();
            if (user is not { Enable:true } || user.LockedUntil > DateTime.Now || tenant is not { Enable:true }
                || tenant.ExpireDate <= DateTime.Now || current.UserId != actor || !current.ActionKeys.Contains(permission)) throw Error(403,"IAM_FORBIDDEN");
            db.ChangeTracker.Clear();
            var result = await body(actor,ct); ct.ThrowIfCancellationRequested(); attempted = true;
            await tx.CommitAsync(ct); return result;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch { throw Error(503,"IAM_OPERATION_UNKNOWN"); }
            finally { db.ChangeTracker.Clear(); }
            if (attempted) throw Error(503,"IAM_OPERATION_UNKNOWN");
            throw;
        }
    }

    public async Task<string> ReferencesVersion(CancellationToken ct)
    {
        // Covers ABA and ordinary SQL writers, including role permissions and user memberships.
        // Conservative tenant-wide CAS: unrelated reference changes require a fresh confirmation too.
        var rows = await db.Database.SqlQuery<RevisionRow>($"SELECT object_key AS \"Key\", revision AS \"Revision\" FROM cp6_authentication_v1.identity_admission_revisions WHERE tenant_id={db.CurrentTenantId} ORDER BY object_key COLLATE \"C\"").ToListAsync(ct);
        // The existing authentication revision also covers SSO default-role and approval ABA.
        var policy=await db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().Where(x=>x.Id==db.CurrentTenantId)
            .Select(x=>x.AuthenticationPolicyRevision).SingleAsync(ct);
        return AuthenticationPolicyServiceV1.Hash(new {policy,rows});
    }

    public async Task<string> Version(string key, CancellationToken ct)
        => (await db.Database.SqlQuery<Guid?>($"SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE tenant_id={db.CurrentTenantId} AND object_key={key}").SingleOrDefaultAsync(ct))?.ToString("D") ?? "absent";

    public static void Validate(IamCommandV1 command)
    {
        if (command.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(command.ExpectedVersion)
            || string.IsNullOrWhiteSpace(command.ExpectedReferencesVersion) || string.IsNullOrWhiteSpace(command.Reason)
            || command.Reason.Length > 256) throw Error(400,"IAM_COMMAND_ENVELOPE_REQUIRED");
    }

    public async Task<IamOperationV1?> Original(Guid actor, string kind, IamCommandV1 command, CancellationToken ct)
    {
        var row = await Operation(command.OperationId,ct); if (row == null) return null;
        RequireOwner(row,actor,kind);
        if (row.State == "CLOSED") throw Error(409,"IAM_OPERATION_CLOSED");
        if (row.RequestProtected == null || fingerprint.Unprotect(row.RequestProtected) != AuthenticationPolicyServiceV1.Hash((object)command))
            throw Error(409,"IAM_OPERATION_INPUT_CONFLICT");
        return JsonSerializer.Deserialize<IamOperationV1>(row.ResultJson)!;
    }

    public Task Store(Guid actor, IamCommandV1? command, IamOperationV1 result, CancellationToken ct)
    {
        var hash = command == null ? null : fingerprint.Protect(AuthenticationPolicyServiceV1.Hash((object)command));
        var json = JsonSerializer.Serialize(result);
        return db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.iam_operations(tenant_id,operation_id,actor_id,kind,state,request_protected,result_json) VALUES({db.CurrentTenantId},{result.OperationId},{actor},{result.Kind},{result.State},{hash},{json})",ct);
    }

    public Task<IamOperationV1> Resolve(Guid id, string kind, bool close, Func<CancellationToken,Task<Guid>> actor, CancellationToken ct)
    {
        if (id == Guid.Empty) throw Error(400,"IAM_OPERATION_REQUIRED");
        return ExecuteAsync(Permission(kind),actor,async (who,token) =>
        {
            var row = await Operation(id,token);
            if (row != null) { RequireOwner(row,who,kind); return JsonSerializer.Deserialize<IamOperationV1>(row.ResultJson)!; }
            var result = new IamOperationV1(id,kind,close ? "CLOSED" : "UNKNOWN");
            if (close)
            {
                Audit(who,id,kind,"Closed original uncertain IAM operation",null);
                await db.SaveChangesAsync(token); await Store(who,null,result,token);
            }
            return result;
        },ct);
    }

    public void Audit(Guid actor, Guid operation, string kind, string reason, Guid? entity, int? roleId = null)
        => db.Sys_SecurityLogs.Add(new Sys_SecurityLog { TenantId=db.CurrentTenantId,UserId=actor,
            EventType=(int)SecurityEventType.IamAdministrationChanged, CreatedAt=DateTime.Now,Reason=reason,
            ContextJson=JsonSerializer.Serialize(new { operationId=operation,kind,entityId=entity,roleId }) });

    public static string Permission(string kind) => kind switch
    {
        "user-create" => "user:add", "user-update" or "user-roles" or "user-org" => "user:edit",
        "role-create" => "role:add", "role-update" => "role:edit", "role-delete" => "role:delete",
        "dept-create" => "pub-dept:add", "dept-update" or "dept-move" or "dept-leader" => "pub-dept:edit",
        "dept-delete" => "pub-dept:delete", _ => throw Error(400,"IAM_OPERATION_KIND_INVALID")
    };
    public static IamAdministrationExceptionV1 Error(int status,string code) => new(status,code);
    private Task<OperationRow?> Operation(Guid id,CancellationToken ct) => db.Database.SqlQuery<OperationRow>($"SELECT actor_id AS \"ActorId\",kind AS \"Kind\",state AS \"State\",request_protected AS \"RequestProtected\",result_json AS \"ResultJson\" FROM cp6_authentication_v1.iam_operations WHERE tenant_id={db.CurrentTenantId} AND operation_id={id}").SingleOrDefaultAsync(ct);
    private static void RequireOwner(OperationRow row,Guid actor,string kind)
    { if (row.ActorId != actor) throw Error(403,"IAM_OPERATION_OWNER_MISMATCH"); if (row.Kind != kind) throw Error(409,"IAM_OPERATION_KIND_CONFLICT"); }
    public sealed class RevisionRow { public string Key { get; set; }=""; public Guid Revision { get; set; } }
    public sealed class OperationRow { public Guid ActorId { get; set; } public string Kind { get; set; }=""; public string State { get; set; }=""; public string? RequestProtected { get; set; } public string ResultJson { get; set; }=""; }
}
