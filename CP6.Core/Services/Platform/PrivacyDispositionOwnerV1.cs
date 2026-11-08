using System.Data;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.CrmIdentity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace CP6.Core.Services.Platform;
public sealed partial class PrivacyDispositionOwnerV1(CP6Context db,IHttpContextAccessor http,
    ICurrentPermissionContext permissions,IOptions<ScopeAuthorizationOptionsV1> environment,IPasswordHasher hasher,
    IOptions<PrivacyRestoreOptionsV1>? restoration=null,CrmIdentityRuntime? identity=null)
{
    public const string Menu="privacy-disposition",Action="ANONYMIZE_IDENTITY_PROFILE";
    internal static readonly string[] Fields=["UserName","NickName","Email","LastLoginIp","BadgeNo","ExternalSubject","ExternalProvider"];
    internal static readonly string[] Effects=["PSEUDONYMIZE_LOGIN_NAME_PRESERVE_STABLE_ID","CLEAR_LISTED_PROFILE_FIELDS","DISABLE_LOGIN_AND_REVOKE_AUTHENTICATION_MATERIAL"];
    internal static readonly string[] Uncovered=["MAIN.SECURITY_LOG_CONTENT","MAIN.AUTHORIZATION_AND_ORGANIZATION_REFERENCES","MAIN.OA_AND_PLM_HISTORY","CRM_BUSINESS_AND_HISTORY","FILE_AND_PRIOR_EXPORT_COPIES","BACKUPS_AND_EXTERNAL_COPIES","UNREGISTERED_OWNERS"];
    private sealed record Authority(PlatformAdminAuthorization.Actor Actor,UserPermissionContext Permissions,Guid Environment);
    private static void Can(Authority a,string action){if(!a.Permissions.ActionKeys.Contains(Menu+":"+action))throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_"+action.ToUpperInvariant()+"_FORBIDDEN",403);}
    private static void Id(Guid id){if(id==Guid.Empty)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_ID_REQUIRED",400);}
    private static void Target(PrivacyDispositionTargetV1 target){Id(target.TargetTenantId);Id(target.SubjectId);Id(target.PolicyId);}
    private string Journal(Authority a)=>"Main.Privacy.Disposition.v1/"+a.Environment.ToString("D")+"/"+a.Actor.Id.ToString("D");
    private Task<DateTimeOffset> Clock(CancellationToken ct)=>db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
    private async Task<T> Transaction<T>(bool write,TenantManagementSessionV1 session,Func<Authority,CancellationToken,Task<T>> body,CancellationToken ct,
        Func<CancellationToken,Task>? beforeCommit=null)
    {
        if(!db.Database.IsNpgsql()||db.Database.CurrentTransaction!=null||db.ChangeTracker.HasChanges()||System.Transactions.Transaction.Current!=null)
            throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_INDEPENDENT_CONTEXT_REQUIRED",503);
        if(environment.Value.EnvironmentId==Guid.Empty)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_ENVIRONMENT_REQUIRED",503);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);var committing=false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'",ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db,ct);
            var current=await session.CurrentActor(ct);
            var actor=await PlatformAdminAuthorization.RequireAsync(db,http.HttpContext?.User,ct,lockActor:true);
            if(current!=actor.Id)throw new PrivacyDispositionExceptionV1("REAUTH_REQUIRED",401);
            var bound=await db.Database.SqlQueryRaw<Guid?>("SELECT environment_id AS \"Value\" FROM cp6_scope_authorization_v1.environment WHERE singleton").SingleOrDefaultAsync(ct);
            if(bound!=environment.Value.EnvironmentId)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_ENVIRONMENT_MISMATCH",503);
            var until=await db.Sys_Tenants.AsNoTracking().Where(x=>x.Id==actor.TenantId && x.Enable).Select(x=>x.ExpireDate).SingleAsync(ct);
            var result=await body(new(actor,await permissions.GetAsync(),bound.Value),ct);
            await session.Complete(ct);
            if(until<=DateTime.Now)throw new PrivacyDispositionExceptionV1("REAUTH_REQUIRED",401);
            ct.ThrowIfCancellationRequested();if(beforeCommit!=null)await beforeCommit(ct);
            ct.ThrowIfCancellationRequested();committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception error)
        {
            if(write&&committing)throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",503,error);
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch(Exception rollback) { throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN",503,new AggregateException(error,rollback)); }
            throw;
        }
    }
}
