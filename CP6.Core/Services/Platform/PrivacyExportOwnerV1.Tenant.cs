using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyExportOwnerV1
{
    public const string TenantOwner="Main.Privacy.TenantExport.v1",TenantPolicy="MAIN.TENANT_EXPORT.ACCESS.v1";
    public static readonly string[] TenantPurposeCodes=["TENANT_ACCESS_REQUEST","TENANT_SECURITY_REVIEW"];
    public Task<TenantPrivacyExportContextV1> TenantContextAsync(TenantManagementSessionV1 session,CancellationToken ct=default)
        =>Transaction(false,session,(a,_)=>{Can(a,"tenant-read");return Task.FromResult(new TenantPrivacyExportContextV1(a.Environment,a.Actor.TenantId,a.Actor.Id,TenantOwner,TenantPolicy,TenantPurposeCodes,SourceCodes,
            a.Permissions.ActionKeys.Where(x=>x.StartsWith(Menu+":tenant-",StringComparison.Ordinal)).Select(x=>x[(Menu.Length+1)..]).Order().ToArray(),1440));},ct);
    private Task<Original?> FindTenant(Authority a,Guid operation,CancellationToken ct)=>db.Database.SqlQuery<Original>($"""
        SELECT state AS "State",input_bytes AS "Input",result_bytes AS "Result" FROM cp6_privacy_tenant_export_v1.commands
        WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND actor_id={a.Actor.Id} AND operation_id={operation}
        """).SingleOrDefaultAsync(ct);
    private static TenantPrivacyExportOperationV1 TenantOriginal(Guid operation,Original? row,byte[]? input=null)
    {
        if(row==null)return new(operation,"NOT_FOUND",null);
        if(row.State=="CLOSED")return new(operation,"CLOSED",null);
        if(input!=null&&!input.AsSpan().SequenceEqual(row.Input))throw new PrivacyExportExceptionV1("PRIVACY_TENANT_OPERATION_CONFLICT");
        return new(operation,"COMMITTED",JsonSerializer.Deserialize<TenantPrivacyExportReceiptV1>(row.Result!)!);
    }
    public Task<TenantPrivacyExportOperationV1> TenantOperationAsync(Guid operation,bool close,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(operation);return Transaction(close,session,async(a,t)=>
        {
            Can(a,"tenant-read");var row=await FindTenant(a,operation,t);if(row!=null||!close)return TenantOriginal(operation,row);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_tenant_export_v1.commands(environment_id,actor_tenant,actor_id,operation_id,state) VALUES({a.Environment},{a.Actor.TenantId},{a.Actor.Id},{operation},'CLOSED')",t);
            await TenantAudit(a,operation,null,"CLOSE_ABSENT",t);return new TenantPrivacyExportOperationV1(operation,"CLOSED",null);
        },ct);
    }
    public Task<TenantPrivacyExportOperationV1> GenerateTenantAsync(TenantPrivacyExportCommandV1 command,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(command.OperationId);Id(command.RequestRef);Id(command.TargetTenantId);Id(command.RecipientId);
        if(command.Sources==null||command.Sources.Length==0||command.Sources.Distinct(StringComparer.Ordinal).Count()!=command.Sources.Length
            ||command.Sources.Any(x=>!SourceCodes.Contains(x,StringComparer.Ordinal))||!TenantPurposeCodes.Contains(command.Purpose,StringComparer.Ordinal)||command.AccessMinutes is <1 or >1440)
            throw new PrivacyExportExceptionV1("PRIVACY_TENANT_COMMAND_INVALID",400);
        var input=Bytes(command);return Transaction(true,session,async(a,t)=>
        {
            Can(a,"tenant-read");var old=await FindTenant(a,command.OperationId,t);if(old!=null)return TenantOriginal(command.OperationId,old,input);
            Can(a,"tenant-export");foreach(var source in command.Sources)Can(a,"tenant-scope-"+source);
            if(command.PolicyVersion!=TenantPolicy)throw new PrivacyExportExceptionV1("PRIVACY_TENANT_POLICY_CHANGED",412);
            if(!await db.Sys_Users.AsNoTracking().AnyAsync(x=>x.Id==command.RecipientId&&x.Enable&&x.IsPlatformAdmin,t))throw new PrivacyExportExceptionV1("PRIVACY_TENANT_RECIPIENT_UNAVAILABLE",403);
            var capture=await TenantPrivacyExportReaderV1.Capture(db,command.TargetTenantId,command.Sources,t);var at=await Clock(t);var artifact=Guid.NewGuid();
            var receipt=new TenantPrivacyExportReceiptV1(command.OperationId,command.RequestRef,artifact,a.Environment,a.Actor.TenantId,a.Actor.Id,a.Actor.Id,command.RecipientId,command.TargetTenantId,command.Purpose,command.Sources,TenantPolicy,at,at.AddMinutes(command.AccessMinutes),
                capture.Bytes.LongLength,Convert.ToHexString(SHA256.HashData(capture.Bytes)).ToLowerInvariant(),capture.Manifest);var result=Bytes(receipt);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_tenant_export_v1.artifacts(id,environment_id,actor_tenant,actor_id,recipient_id,target_tenant,policy,expires_at,receipt_bytes)
                VALUES({artifact},{a.Environment},{a.Actor.TenantId},{a.Actor.Id},{command.RecipientId},{command.TargetTenantId},{TenantPolicy},{receipt.ExpiresAt},{result});
                INSERT INTO cp6_privacy_tenant_export_v1.payloads(artifact_id,bytes) VALUES({artifact},{capture.Bytes});
                INSERT INTO cp6_privacy_tenant_export_v1.commands(environment_id,actor_tenant,actor_id,operation_id,state,input_bytes,result_bytes)
                VALUES({a.Environment},{a.Actor.TenantId},{a.Actor.Id},{command.OperationId},'COMMITTED',{input},{result});
                """,t);
            await TenantAudit(a,command.OperationId,artifact,"GENERATED",t);return new TenantPrivacyExportOperationV1(command.OperationId,"COMMITTED",receipt);
        },ct);
    }
    private sealed record TenantArtifact(Guid Actor,Guid Recipient,Guid Target,string Policy,DateTimeOffset Expires,DateTimeOffset? Revoked,byte[] Receipt,bool Disposed);
    private async Task<TenantArtifact> TenantArtifactRow(Authority a,Guid artifact,CancellationToken ct)
    {
        var row=await db.Database.SqlQuery<TenantArtifact>($"""
            SELECT actor_id AS "Actor",recipient_id AS "Recipient",target_tenant AS "Target",policy AS "Policy",expires_at AS "Expires",revoked_at AS "Revoked",receipt_bytes AS "Receipt",EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.tombstones t WHERE t.artifact_id=a.id) AS "Disposed"
            FROM cp6_privacy_tenant_export_v1.artifacts a WHERE id={artifact} AND environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId}
            """).SingleOrDefaultAsync(ct)??throw new PrivacyExportExceptionV1("PRIVACY_TENANT_ARTIFACT_NOT_FOUND",404);
        if(row.Actor!=a.Actor.Id&&row.Recipient!=a.Actor.Id)throw new PrivacyExportExceptionV1("PRIVACY_TENANT_ARTIFACT_ACTOR_REQUIRED",403);return row;
    }
    private static string TenantAvailability(TenantArtifact row,DateTimeOffset at)=>row.Disposed?"DISPOSED":row.Revoked!=null?"REVOKED":row.Expires<=at?"EXPIRED":row.Policy!=TenantPolicy?"POLICY_CHANGED":"AVAILABLE";
    public Task<TenantPrivacyExportArtifactV1> TenantArtifactAsync(Guid artifact,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(artifact);return Transaction(false,session,async(a,t)=>{Can(a,"tenant-read");var row=await TenantArtifactRow(a,artifact,t);return new TenantPrivacyExportArtifactV1(JsonSerializer.Deserialize<TenantPrivacyExportReceiptV1>(row.Receipt)!,TenantAvailability(row,await Clock(t)));},ct);}
    public Task<TenantPrivacyExportArtifactV1> RevokeTenantAsync(Guid artifact,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(artifact);return Transaction(true,session,async(a,t)=>
        {
            Can(a,"tenant-read");Can(a,"tenant-revoke");var row=await TenantArtifactRow(a,artifact,t);if(row.Actor!=a.Actor.Id)throw new PrivacyExportExceptionV1("PRIVACY_TENANT_REQUESTER_REQUIRED",403);
            if(row.Revoked==null){await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_privacy_tenant_export_v1.artifacts SET revoked_at=clock_timestamp() WHERE id={artifact}",t);await TenantAudit(a,null,artifact,"REVOKED",t);}
            return new TenantPrivacyExportArtifactV1(JsonSerializer.Deserialize<TenantPrivacyExportReceiptV1>(row.Receipt)!,row.Disposed?"DISPOSED":"REVOKED");
        },ct);
    }
    public Task<PrivacyExportDownloadV1> DownloadTenantAsync(Guid artifact,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(artifact);return Transaction(true,session,async(a,t)=>
        {
            Can(a,"tenant-download");var row=await TenantArtifactRow(a,artifact,t);if(row.Recipient!=a.Actor.Id)throw new PrivacyExportExceptionV1("PRIVACY_TENANT_RECIPIENT_REQUIRED",403);
            if(TenantAvailability(row,await Clock(t))!="AVAILABLE")throw new PrivacyExportExceptionV1("PRIVACY_TENANT_ARTIFACT_UNAVAILABLE",410);
            var receipt=JsonSerializer.Deserialize<TenantPrivacyExportReceiptV1>(row.Receipt)!;foreach(var source in receipt.Sources)Can(a,"tenant-scope-"+source);
            if(!await db.Sys_Tenants.AsNoTracking().AnyAsync(x=>x.Id==row.Target,t))throw new PrivacyExportExceptionV1("PRIVACY_TENANT_TARGET_BINDING_CHANGED",410);
            var bytes=await db.Database.SqlQuery<byte[]>($"SELECT bytes AS \"Value\" FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id={artifact}").SingleOrDefaultAsync(t)??throw new PrivacyExportExceptionV1("PRIVACY_TENANT_PAYLOAD_UNAVAILABLE",410);
            // This Owner installed immutable payloads with the original receipt in one transaction; no internal re-hashing.
            await TenantAudit(a,null,artifact,"DOWNLOAD_AUTHORIZED",t);return new PrivacyExportDownloadV1(bytes,artifact,row.Expires);
        },ct);
    }
    private Task TenantAudit(Authority a,Guid? operation,Guid? artifact,string kind,CancellationToken ct)
        =>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_privacy_tenant_export_v1.audit(id,environment_id,actor_tenant,actor_id,operation_id,artifact_id,kind) VALUES({Guid.NewGuid()},{a.Environment},{a.Actor.TenantId},{a.Actor.Id},{operation},{artifact},{kind})",ct);
}
