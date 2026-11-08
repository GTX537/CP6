using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyDispositionOwnerV1
{
    private async Task<PrivacyDispositionExecutionV1?> Execution(Authority a,Guid plan,CancellationToken ct)
    {
        var json=await db.Database.SqlQuery<string>($"""
            SELECT receipt_json AS "Value" FROM cp6_privacy_disposition_v1.executions
            WHERE environment_id={a.Environment} AND actor_tenant={a.Actor.TenantId} AND plan_id={plan}
            """).SingleOrDefaultAsync(ct);
        return json==null?null:JsonSerializer.Deserialize<PrivacyDispositionExecutionV1>(json)!;
    }
    public Task<PrivacyDispositionExecutionV1?> ExecutionAsync(Guid plan,TenantManagementSessionV1 session,CancellationToken ct=default)
    {Id(plan);return Transaction(false,session,(a,t)=>{Can(a,"read");return Execution(a,plan,t);},ct);}
    public Task<PrivacyDispositionOperationV1> ExecuteAsync(Guid id,PrivacyDispositionExecuteV1 input,TenantManagementSessionV1 session,CancellationToken ct=default)
    {
        Id(id);Id(input.OperationId);Id(input.ExpectedVersion);Id(input.ConfirmationRef);
        if(string.IsNullOrEmpty(input.ExpectedScopeDigest))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_PREVIEW_REQUIRED",400);
        var bytes=CP6.Core.Services.Integration.MainOwnerOperationJournalV1.Encode(new{kind="EXECUTE",id,input});
        return Transaction(true,session,async(a,t)=>
        {
            Can(a,"read");var old=await Original(a,input.OperationId,bytes,t);if(old!=null)return old;Can(a,"execute");
            var row=await Plan(a,id,t)??throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_PLAN_NOT_FOUND",404);
            if(await Execution(a,id,t)!=null)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_ALREADY_APPLIED");
            var current=await View(a,row,t);
            if(current.Version!=input.ExpectedVersion||current.ScopeDigest!=input.ExpectedScopeDigest)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_APPROVAL_CHANGED",412);
            if(!current.ApprovalCurrent)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_EXECUTION_BLOCKED");
            var scope=current.Scope;
            try{db.RequireIdentityCapture(scope.TargetTenantId);}
            catch(InvalidOperationException e)when(e.Message=="SSO_IDENTITY_WRITER_UNAVAILABLE")
            {throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_IDENTITY_WRITER_UNAVAILABLE",503,e);}
            // Do not materialize old profile values or produce an audit containing before-images.
            var name="anon_"+scope.SubjectId.ToString("N");
            var unusable=hasher.Hash(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));var epoch=Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public."Sys_Users" SET "UserName"={name},"NickName"=NULL,"Email"=NULL,"LastLoginIp"=NULL,
                  "BadgeNo"=NULL,"ExternalSubject"=NULL,"ExternalProvider"=NULL,"Enable"=false,"Password"={unusable},
                  "TwoFactorSecret"=NULL,"TwoFactorEnabled"=false,"TwoFactorEnrolledAt"=NULL,"QuickPinHash"=NULL,
                  "AllowPasswordFallback"=false,"AuthenticationEpoch"={epoch},"MustChangePassword"=true,
                  "PasswordChangedAt"=LOCALTIMESTAMP,"Modifier"={a.Actor.Id.ToString("D")},"ModifyDate"=LOCALTIMESTAMP
                WHERE "TenantId"={scope.TargetTenantId} AND "Id"={scope.SubjectId};
                UPDATE public."Sys_RefreshTokens" SET "RevokedAt"=LOCALTIMESTAMP
                WHERE "TenantId"={scope.TargetTenantId} AND "UserId"={scope.SubjectId} AND "RevokedAt" IS NULL;
                UPDATE public."Sys_BrowserSessions" SET "LoggedOutAtUtc"=timezone('UTC',clock_timestamp())
                WHERE "TenantId"={scope.TargetTenantId} AND "UserId"={scope.SubjectId} AND "LoggedOutAtUtc" IS NULL;
                """,t);
            await db.CapturePrivacyIdentityInvalidationAsync(scope.TargetTenantId,scope.SubjectId,t);
            await db.SaveChangesAsync(t);
            var version=await db.Database.SqlQuery<Guid>($"SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE tenant_id={scope.TargetTenantId} AND object_key={"user:"+scope.SubjectId.ToString("D")}").SingleAsync(t);
            var receipt=new PrivacyDispositionExecutionV1(id,input.OperationId,a.Environment,a.Actor.TenantId,a.Actor.Id,row.Requester,current.ApproverId!.Value,
                input.ConfirmationRef,scope.TargetTenantId,scope.SubjectId,"Main.Identity.Profile.v1",scope.Action,scope.PolicyVersion,scope.TargetVersion,version,
                await Clock(t),"APPLIED","PARTIAL",scope.Fields,scope.UncoveredOwners);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_privacy_disposition_v1.executions(plan_id,environment_id,actor_tenant,target_tenant,subject_id,receipt_json)
                VALUES({id},{a.Environment},{a.Actor.TenantId},{scope.TargetTenantId},{scope.SubjectId},{JsonSerializer.Serialize(receipt)})
                """,t);
            var result=new PrivacyDispositionOperationV1(input.OperationId,"COMMITTED","EXECUTE",id,current.Version,scope.PolicyId,scope.TargetTenantId,scope.SubjectId,receipt);
            await Store(a,input.OperationId,bytes,result,t);return result;
        },ct);
    }
}
