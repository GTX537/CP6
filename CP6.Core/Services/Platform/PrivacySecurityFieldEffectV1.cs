using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
internal sealed record PrivacySecurityFieldTombstoneV1(Guid LogId,Guid SourcePlanId,Guid OperatorTenantId,Guid TargetTenantId,Guid SubjectId,Guid PolicyId,string Scope,string Receipt);
internal static class PrivacySecurityFieldEffectV1
{
    private sealed record ExistingTomb(Guid Plan,string Scope,string Receipt);
    private sealed record CandidateRow(Guid Tenant,Guid? Subject,int EventType,DateTime CreatedAt,Guid Version,int ValueCount,string Digest);
    internal static async Task Ready(CP6Context db,CancellationToken ct)
    {
        try{if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_privacy_security_fields_v1.ready() AS \"Value\"").SingleAsync(ct))throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_BOUNDARY_UNAVAILABLE",503);}
        catch(Npgsql.PostgresException e)when(e.SqlState is "42883" or "3F000" or "42P01")
        {throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_BOUNDARY_UNAVAILABLE",503,e);}
    }
    // This restored database is the external boundary. Read actual field contents only into a digest here.
    internal static async Task<PrivacyRestoreUnitV1> Qualify(CP6Context candidate,PrivacySecurityFieldTombstoneV1 tomb,CancellationToken ct)
    {
        var existing=await candidate.Database.SqlQuery<ExistingTomb>($"""
            SELECT source_plan_id AS "Plan",scope_json AS "Scope",receipt_json AS "Receipt" FROM cp6_privacy_security_fields_v1.tombstones WHERE log_id={tomb.LogId}
            """).SingleOrDefaultAsync(ct);
        if(existing!=null&&(existing.Plan!=tomb.SourcePlanId||existing.Scope!=tomb.Scope||existing.Receipt!=tomb.Receipt))throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_RESTORE_TOMBSTONE_CONFLICT",412);
        var row=await candidate.Database.SqlQuery<CandidateRow>($"""
            SELECT "TenantId" AS "Tenant","UserId" AS "Subject","EventType" AS "EventType","CreatedAt" AS "CreatedAt",privacy_revision AS "Version",
              ("UserName" IS NOT NULL)::integer+("ClientIp" IS NOT NULL)::integer AS "ValueCount",
              cp6_privacy_security_fields_v1.field_digest("UserName"::text,"ClientIp"::text) AS "Digest" FROM public."Sys_SecurityLogs" WHERE "Id"={tomb.LogId}
            """).SingleOrDefaultAsync(ct);
        if(row!=null)
        {
            var scope=JsonSerializer.Deserialize<SecurityFieldScopeV1>(tomb.Scope)!;
            if(row.Tenant!=tomb.TargetTenantId||row.Subject!=tomb.SubjectId||row.EventType!=scope.EventType||row.CreatedAt!=scope.OriginalCreatedAt
                ||row.ValueCount>0&&(row.Version!=scope.TargetVersion||row.Digest!=scope.BeforeDigest))
                throw new PrivacyDispositionExceptionV1("PRIVACY_SECURITY_FIELDS_RESTORE_IDENTITY_CHANGED",412);
        }
        return new(tomb.SourcePlanId,tomb.TargetTenantId,tomb.SubjectId,row==null?"ABSENT_FENCED":row.ValueCount==0?"ALREADY_CLEARED_FENCED":"REAPPLIED",row?.Version,row==null?null:Guid.NewGuid(),
            PrivacyDispositionOwnerV1.SecurityOwner,LogId:tomb.LogId,ClearedValueCount:row?.ValueCount??0);
    }
    internal static async Task Apply(CP6Context db,PrivacySecurityFieldTombstoneV1 tomb,Guid environment,Guid? after,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_privacy_security_fields_v1.tombstones(log_id,source_plan_id,environment_id,operator_tenant,target_tenant,subject_id,policy_id,scope_json,receipt_json)
            SELECT {tomb.LogId},{tomb.SourcePlanId},{environment},{tomb.OperatorTenantId},{tomb.TargetTenantId},{tomb.SubjectId},{tomb.PolicyId},{tomb.Scope},{tomb.Receipt}
            WHERE NOT EXISTS(SELECT FROM cp6_privacy_security_fields_v1.tombstones WHERE log_id={tomb.LogId})
            """,ct);
        if(after!=null)await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_SecurityLogs\" SET \"UserName\"=NULL,\"ClientIp\"=NULL,privacy_revision={after.Value} WHERE \"Id\"={tomb.LogId}",ct);
    }
}
