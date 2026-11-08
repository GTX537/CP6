using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Platform;

/// <summary>Main privacy artifact Owner. Immutable command metadata is separate from deletable PII bytes.
/// No engineering FILE parent or cross-application coverage is inferred.</summary>
public sealed partial class PrivacyExportOwnerV1(CP6Context db, IHttpContextAccessor http,
    ICurrentPermissionContext permissions, IOptions<ScopeAuthorizationOptionsV1> environment)
{
    public const string Policy="MAIN.SUBJECT_EXPORT.ACCESS.v1", Menu="privacy-export";
    public static readonly string[] SourceCodes=["identity","security","roles"];
    public static readonly string[] PurposeCodes=["SUBJECT_ACCESS_REQUEST","ACCOUNT_SECURITY_REVIEW"];
    private static byte[] Bytes<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value);
    private static void Id(Guid id) { if(id==Guid.Empty)throw new PrivacyExportExceptionV1("PRIVACY_ID_REQUIRED",400); }
    private sealed record Authority(PlatformAdminAuthorization.Actor Actor, UserPermissionContext Permissions, Guid Environment);
    private static void Can(Authority authority,string action)
    { if(!authority.Permissions.ActionKeys.Contains(Menu+":"+action))throw new PrivacyExportExceptionV1("PRIVACY_"+action.ToUpperInvariant()+"_FORBIDDEN",403); }
    private async Task<T> Transaction<T>(bool write,TenantManagementSessionV1 session,Func<Authority,CancellationToken,Task<T>> body,CancellationToken ct)
    {
        if(!db.Database.IsNpgsql()||db.Database.CurrentTransaction!=null||db.ChangeTracker.HasChanges()||System.Transactions.Transaction.Current!=null)
            throw new PrivacyExportExceptionV1("PRIVACY_INDEPENDENT_CONTEXT_REQUIRED",503);
        if(environment.Value.EnvironmentId==Guid.Empty)throw new PrivacyExportExceptionV1("PRIVACY_ENVIRONMENT_REQUIRED",503);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);var committing=false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'",ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db,ct);
            var current=await session.CurrentActor(ct);
            var actor=await PlatformAdminAuthorization.RequireAsync(db,http.HttpContext?.User,ct,lockActor:true);
            if(current!=actor.Id)throw new PrivacyExportExceptionV1("REAUTH_REQUIRED",401);
            var bound=await db.Database.SqlQueryRaw<Guid?>("SELECT environment_id AS \"Value\" FROM cp6_scope_authorization_v1.environment WHERE singleton").SingleOrDefaultAsync(ct);
            if(bound!=environment.Value.EnvironmentId)throw new PrivacyExportExceptionV1("PRIVACY_ENVIRONMENT_MISMATCH",503);
            var until=await db.Sys_Tenants.AsNoTracking().Where(x=>x.Id==actor.TenantId && x.Enable).Select(x=>x.ExpireDate).SingleAsync(ct);
            var result=await body(new(actor,await permissions.GetAsync(),bound.Value),ct);
            await session.Complete(ct);
            if(until<=DateTime.Now)throw new PrivacyExportExceptionV1("REAUTH_REQUIRED",401);
            if(result is PrivacyExportDownloadV1 download && download.ExpiresAt<=await Clock(ct))
                throw new PrivacyExportExceptionV1("PRIVACY_ARTIFACT_EXPIRED",410);
            ct.ThrowIfCancellationRequested();committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception error)
        {
            if(write&&committing)throw new PrivacyExportExceptionV1("PRIVACY_OUTCOME_UNKNOWN",503,error);
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch(Exception rollback) { throw new PrivacyExportExceptionV1("PRIVACY_OUTCOME_UNKNOWN",503,new AggregateException(error,rollback)); }
            throw;
        }
    }
    public Task<PrivacyExportContextV1> ContextAsync(TenantManagementSessionV1 session,CancellationToken ct=default)
        =>Transaction(false,session,(a,_)=>{Can(a,"read");return Task.FromResult(new PrivacyExportContextV1(a.Environment,a.Actor.TenantId,a.Actor.Id,Policy,PurposeCodes,SourceCodes,
            a.Permissions.ActionKeys.Where(x=>x.StartsWith(Menu+":",StringComparison.Ordinal)).Select(x=>x[(Menu.Length+1)..]).Order().ToArray(),1440));},ct);
    private sealed class Original
    { public string State {get;set;}=""; public byte[]? Input {get;set;} public byte[]? Result {get;set;} }
    private Task<Original?> Find(Authority a,Guid operation,CancellationToken ct)=>db.Database.SqlQuery<Original>($"""
        SELECT state AS "State",input_bytes AS "Input",result_bytes AS "Result" FROM cp6_privacy_export_v1.commands
        WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND actor_id={a.Actor.Id} AND operation_id={operation}
        """).SingleOrDefaultAsync(ct);
    private static PrivacyExportOperationV1 OriginalResult(Guid operation,Original? saved,byte[]? input=null)
    {
        if(saved==null)return new(operation,"NOT_FOUND",null);
        if(saved.State=="CLOSED")return new(operation,"CLOSED",null);
        if(input!=null&&!input.AsSpan().SequenceEqual(saved.Input))throw new PrivacyExportExceptionV1("PRIVACY_OPERATION_CONFLICT");
        return new(operation,"COMMITTED",JsonSerializer.Deserialize<PrivacyExportReceiptV1>(saved.Result!)!);
    }
    public Task<PrivacyExportOperationV1> OperationAsync(Guid operation,TenantManagementSessionV1 session,CancellationToken ct=default)
    { Id(operation);return Transaction(false,session,async(a,t)=>{Can(a,"read");return OriginalResult(operation,await Find(a,operation,t));},ct); }
    public Task<PrivacyExportOperationV1> CloseAsync(Guid operation,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(operation);return Transaction(true,session,async(a,t)=>
        {
            Can(a,"read");var saved=await Find(a,operation,t);if(saved!=null)return OriginalResult(operation,saved);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_export_v1.commands(environment_id,actor_tenant,actor_id,operation_id,state)
                VALUES({a.Environment},{a.Actor.TenantId},{a.Actor.Id},{operation},'CLOSED')
                """,t);
            await Audit(a,operation,null,"CLOSE_ABSENT",t);return new PrivacyExportOperationV1(operation,"CLOSED",null);
        },ct);
    }
    public Task<PrivacyExportOperationV1> GenerateAsync(PrivacyExportCommandV1 command,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(command.OperationId);Id(command.RequestRef);Id(command.TargetTenantId);Id(command.SubjectId);Id(command.RecipientId);
        if(command.Sources==null||command.Sources.Length==0||command.Sources.Distinct(StringComparer.Ordinal).Count()!=command.Sources.Length
            ||command.Sources.Any(x=>!SourceCodes.Contains(x,StringComparer.Ordinal))||!PurposeCodes.Contains(command.Purpose,StringComparer.Ordinal)
            ||command.AccessMinutes is <1 or >1440)throw new PrivacyExportExceptionV1("PRIVACY_COMMAND_INVALID",400);
        var input=Bytes(command);
        return Transaction(true,session,async(a,t)=>
        {
            Can(a,"read");var saved=await Find(a,command.OperationId,t);if(saved!=null)return OriginalResult(command.OperationId,saved,input);
            Can(a,"export");foreach(var source in command.Sources)Can(a,"scope-"+source);
            if(command.PolicyVersion!=Policy)throw new PrivacyExportExceptionV1("PRIVACY_POLICY_CHANGED",412);
            // V1 recipient is an explicitly named, enabled platform actor in the requester's tenant.
            // Its future download is independently authorized. No bearer link or external delivery is created.
            if(!await db.Sys_Users.AsNoTracking().AnyAsync(x=>x.Id==command.RecipientId&&x.Enable&&x.IsPlatformAdmin,t))
                throw new PrivacyExportExceptionV1("PRIVACY_RECIPIENT_UNAVAILABLE",422);
            var capture=await SubjectExportReaderV1.CaptureAsync(db,command.SubjectId,command.TargetTenantId,command.Sources,t);
            var at=await Clock(t);var artifact=Guid.NewGuid();var hash=SHA256.HashData(capture.Bytes);
            var receipt=new PrivacyExportReceiptV1(command.OperationId,command.RequestRef,artifact,a.Environment,a.Actor.TenantId,a.Actor.Id,a.Actor.Id,
                command.RecipientId,command.TargetTenantId,command.SubjectId,command.Purpose,command.Sources,Policy,at,at.AddMinutes(command.AccessMinutes),
                capture.Bytes.LongLength,Convert.ToHexString(hash).ToLowerInvariant(),capture.Manifest);
            var result=Bytes(receipt);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_export_v1.artifacts(id,environment_id,actor_tenant,actor_id,recipient_id,target_tenant,subject_id,policy,expires_at,receipt_bytes)
                VALUES({artifact},{a.Environment},{a.Actor.TenantId},{a.Actor.Id},{command.RecipientId},{command.TargetTenantId},{command.SubjectId},{Policy},{receipt.ExpiresAt},{result});
                INSERT INTO cp6_privacy_export_v1.payloads(artifact_id,bytes) VALUES({artifact},{capture.Bytes});
                INSERT INTO cp6_privacy_export_v1.commands(environment_id,actor_tenant,actor_id,operation_id,state,input_bytes,result_bytes)
                VALUES({a.Environment},{a.Actor.TenantId},{a.Actor.Id},{command.OperationId},'COMMITTED',{input},{result});
                """,t);
            await Audit(a,command.OperationId,artifact,"GENERATED_PARTIAL",t);return new PrivacyExportOperationV1(command.OperationId,"COMMITTED",receipt);
        },ct);
    }
    private sealed class Artifact
    {
        public Guid Actor {get;set;} public Guid Recipient {get;set;} public Guid Target {get;set;} public Guid Subject {get;set;}
        public string Policy {get;set;}="";public DateTimeOffset Expires {get;set;}public DateTimeOffset? Revoked {get;set;}public byte[] Receipt {get;set;}=[];public bool Disposed {get;set;}
    }
    private async Task<Artifact> ArtifactRow(Authority a,Guid artifact,CancellationToken ct)
        =>await db.Database.SqlQuery<Artifact>($"""
            SELECT actor_id AS "Actor",recipient_id AS "Recipient",target_tenant AS "Target",subject_id AS "Subject",policy AS "Policy",
              expires_at AS "Expires",revoked_at AS "Revoked",receipt_bytes AS "Receipt",
              EXISTS(SELECT FROM cp6_privacy_export_disposition_v1.tombstones t WHERE t.artifact_id=id) AS "Disposed" FROM cp6_privacy_export_v1.artifacts
            WHERE id={artifact} AND environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND (actor_id={a.Actor.Id} OR recipient_id={a.Actor.Id})
            """).SingleOrDefaultAsync(ct)??throw new PrivacyExportExceptionV1("PRIVACY_ARTIFACT_NOT_FOUND",404);
    private Task<DateTimeOffset> Clock(CancellationToken ct)=>db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    private static string Availability(Artifact row,DateTimeOffset at)=>row.Disposed?"DISPOSED":row.Revoked!=null?"REVOKED":row.Expires<=at?"EXPIRED":row.Policy!=Policy?"POLICY_CHANGED":"AVAILABLE";
    public Task<PrivacyExportArtifactV1> ReadArtifactAsync(Guid artifact,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(artifact);return Transaction(false,session,async(a,t)=>{Can(a,"read");var row=await ArtifactRow(a,artifact,t);return new PrivacyExportArtifactV1(JsonSerializer.Deserialize<PrivacyExportReceiptV1>(row.Receipt)!,Availability(row,await Clock(t)));},ct);}
    public Task<PrivacyExportArtifactV1> RevokeAsync(Guid artifact,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(artifact);return Transaction(true,session,async(a,t)=>
        {
            Can(a,"read");Can(a,"revoke");var row=await ArtifactRow(a,artifact,t);
            if(row.Actor!=a.Actor.Id)throw new PrivacyExportExceptionV1("PRIVACY_REQUESTER_REQUIRED",403);
            if(row.Revoked==null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_privacy_export_v1.artifacts SET revoked_at=clock_timestamp() WHERE id={artifact}",t);
                await Audit(a,null,artifact,"REVOKED",t);
            }
            return new PrivacyExportArtifactV1(JsonSerializer.Deserialize<PrivacyExportReceiptV1>(row.Receipt)!,row.Disposed?"DISPOSED":"REVOKED");
        },ct);
    }
    public Task<PrivacyExportDownloadV1> DownloadAsync(Guid artifact,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(artifact);return Transaction(true,session,async(a,t)=>
        {
            Can(a,"download");var row=await ArtifactRow(a,artifact,t);
            if(row.Recipient!=a.Actor.Id)throw new PrivacyExportExceptionV1("PRIVACY_RECIPIENT_REQUIRED",403);
            if(Availability(row,await Clock(t))!="AVAILABLE")throw new PrivacyExportExceptionV1("PRIVACY_ARTIFACT_UNAVAILABLE",410);
            var receipt=JsonSerializer.Deserialize<PrivacyExportReceiptV1>(row.Receipt)!;
            foreach(var source in receipt.Sources)Can(a,"scope-"+source);
            if(!await db.Sys_Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(x=>x.Id==row.Subject&&x.TenantId==row.Target,t))
                throw new PrivacyExportExceptionV1("PRIVACY_TARGET_BINDING_CHANGED",410);
            var payload=await db.Database.SqlQuery<byte[]>($"SELECT bytes AS \"Value\" FROM cp6_privacy_export_v1.payloads WHERE artifact_id={artifact}").SingleOrDefaultAsync(t)
                ??throw new PrivacyExportExceptionV1("PRIVACY_PAYLOAD_UNAVAILABLE",410);
            if(payload.LongLength!=receipt.Length||Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()!=receipt.Sha256)
                throw new PrivacyExportExceptionV1("PRIVACY_ARTIFACT_INTEGRITY_FAILED",503);
            await Audit(a,null,artifact,"DOWNLOAD_AUTHORIZED",t);
            // Native authority and artifact writers share the IAM gate. Transaction checks clock after session completion.
            return new PrivacyExportDownloadV1(payload,artifact,row.Expires);
        },ct);
    }
    private Task Audit(Authority a,Guid? operation,Guid? artifact,string kind,CancellationToken ct)
        =>db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_export_v1.audit(id,environment_id,actor_tenant,actor_id,operation_id,artifact_id,kind)
            VALUES({Guid.NewGuid()},{a.Environment},{a.Actor.TenantId},{a.Actor.Id},{operation},{artifact},{kind})
            """,ct);
}
