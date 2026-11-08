using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Platform;
// Caller owns the final scope decision and the common IAM gate on this exact transaction.
internal static class PrivacyIdentityEffectV1
{
    internal static async Task<Guid> ApplyAsync(CP6Context db,IPasswordHasher hasher,Guid tenant,Guid subject,Guid actor,CancellationToken t)
    {
        try{db.RequireIdentityCapture(tenant);}
        catch(InvalidOperationException e)when(e.Message=="SSO_IDENTITY_WRITER_UNAVAILABLE")
        {throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_IDENTITY_WRITER_UNAVAILABLE",503,e);}
        // Do not materialize old profile values or produce an audit containing before-images.
        var name="anon_"+subject.ToString("N");
        var unusable=hasher.Hash(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));var epoch=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."Sys_Users" SET "UserName"={name},"NickName"=NULL,"Email"=NULL,"LastLoginIp"=NULL,
              "BadgeNo"=NULL,"ExternalSubject"=NULL,"ExternalProvider"=NULL,"Enable"=false,"Password"={unusable},
              "TwoFactorSecret"=NULL,"TwoFactorEnabled"=false,"TwoFactorEnrolledAt"=NULL,"QuickPinHash"=NULL,
              "AllowPasswordFallback"=false,"AuthenticationEpoch"={epoch},"MustChangePassword"=true,
              "PasswordChangedAt"=LOCALTIMESTAMP,"Modifier"={actor.ToString("D")},"ModifyDate"=LOCALTIMESTAMP
            WHERE "TenantId"={tenant} AND "Id"={subject};
            UPDATE public."Sys_RefreshTokens" SET "RevokedAt"=LOCALTIMESTAMP
            WHERE "TenantId"={tenant} AND "UserId"={subject} AND "RevokedAt" IS NULL;
            UPDATE public."Sys_BrowserSessions" SET "LoggedOutAtUtc"=timezone('UTC',clock_timestamp())
            WHERE "TenantId"={tenant} AND "UserId"={subject} AND "LoggedOutAtUtc" IS NULL;
            """,t);
        await db.CapturePrivacyIdentityInvalidationAsync(tenant,subject,t);
        await db.SaveChangesAsync(t);
        var version=await db.Database.SqlQuery<Guid>($"SELECT revision AS \"Value\" FROM cp6_authentication_v1.identity_admission_revisions WHERE tenant_id={tenant} AND object_key={"user:"+subject.ToString("D")}").SingleAsync(t);
        return version;
    }
}
