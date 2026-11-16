using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Services.Platform;

// Caller owns the final scope decision and the common IAM gate on this exact transaction.
internal static class PrivacyIdentityEffectV1
{
    internal static Task<Guid> ApplyAsync(CP6Context db, IPasswordHasher hasher, Guid tenant, Guid subject, Guid actor, CancellationToken ct)
        => ApplyAsync(db, hasher, tenant, subject, actor, Guid.NewGuid(), null, ct);

    internal static async Task<Guid> ApplyAsync(CP6Context db, IPasswordHasher hasher, Guid tenant, Guid subject, Guid actor,
        Guid epoch, DateTime? passwordChangedAt, CancellationToken ct)
    {
        try { db.RequireIdentityCapture(tenant); }
        catch (InvalidOperationException error) when (error.Message == "SSO_IDENTITY_WRITER_UNAVAILABLE")
        {
            throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_IDENTITY_WRITER_UNAVAILABLE", 503, error);
        }

        // Do not materialize old profile values or produce an audit containing before-images.
        var name = $"anon_{subject:N}";
        var unusablePassword = hasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var changedAt = new NpgsqlParameter("privacy_password_changed_at", NpgsqlDbType.Timestamp)
        {
            Value = passwordChangedAt is { } saved ? DateTime.SpecifyKind(saved, DateTimeKind.Unspecified) : DBNull.Value
        };
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."Sys_Users" SET "UserName"={name},"NickName"=NULL,"Email"=NULL,"LastLoginIp"=NULL,
              "BadgeNo"=NULL,"ExternalSubject"=NULL,"ExternalProvider"=NULL,"Enable"=false,"Password"={unusablePassword},
              "TwoFactorSecret"=NULL,"TwoFactorEnabled"=false,"TwoFactorEnrolledAt"=NULL,"QuickPinHash"=NULL,
              "AllowPasswordFallback"=false,"AuthenticationEpoch"={epoch},"MustChangePassword"=true,
              "PasswordChangedAt"=COALESCE({changedAt},LOCALTIMESTAMP),
              "Modifier"={actor.ToString("D")},"ModifyDate"=LOCALTIMESTAMP
            WHERE "TenantId"={tenant} AND "Id"={subject};
            UPDATE public."Sys_RefreshTokens" SET "RevokedAt"=LOCALTIMESTAMP
            WHERE "TenantId"={tenant} AND "UserId"={subject} AND "RevokedAt" IS NULL;
            UPDATE public."Sys_BrowserSessions" SET "LoggedOutAtUtc"=timezone('UTC',clock_timestamp())
            WHERE "TenantId"={tenant} AND "UserId"={subject} AND "LoggedOutAtUtc" IS NULL;
            """, ct);
        await db.CapturePrivacyIdentityInvalidationAsync(tenant, subject, ct);
        await db.SaveChangesAsync(ct);
        return await db.Database.SqlQuery<Guid>($"""
            SELECT revision AS "Value" FROM cp6_authentication_v1.identity_admission_revisions
            WHERE tenant_id={tenant} AND object_key={$"user:{subject:D}"}
            """).SingleAsync(ct);
    }
}
