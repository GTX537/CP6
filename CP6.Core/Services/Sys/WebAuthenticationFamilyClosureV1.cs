using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Sys;

internal enum WebAuthenticationCloseKindV1 { Logout, Completion, Browser }

internal sealed record WebAuthenticationLogoutAuditV1(Guid TenantId, Guid UserId, string? UserName, string? ClientIp, string? UserAgent);

internal sealed record WebAuthenticationFamilyClosureV1(WebAuthenticationCloseKindV1 Kind, Guid TenantId, Guid UserId,
    Guid FamilyId, string RefreshHash, Guid CompletionId, string? TargetRefreshHash, string? BrowserKeyHash,
    WebAuthenticationLogoutAuditV1? LogoutAudit = null)
{
    internal static async Task<WebAuthenticationFamilyClosureV1?> PrepareAsync(CP6Context db, WebAuthenticationCloseKindV1 kind,
        string? refreshHash, string? targetHash, Guid? completionId, string? browserKeyHash, CancellationToken ct)
    {
        var roots = targetHash is null && completionId is { } completion
            ? await RootHashesAsync(db, completion, ct)
            : [];
        var originalHash = refreshHash ?? roots.FirstOrDefault();
        if (originalHash is null) return null;
        var original = await ReadWebRefreshAsync(db, originalHash, ct);
        if (original?.BrowserSessionId is not { } familyId) return null;

        var receipt = await new AuthenticationInteractionStoreV1(db).ReadSessionByRefreshAsync(originalHash, ct);
        var proof = receipt?.Parse().Primary;
        if (proof is null || proof.TenantId != original.TenantId || proof.UserId != original.UserId || proof.ClientKind != "Web" ||
            completionId.HasValue && proof.CompletionRef != completionId.Value)
            throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");

        var candidate = new WebAuthenticationFamilyClosureV1(kind, proof.TenantId, proof.UserId, familyId, originalHash,
            proof.CompletionRef, targetHash, browserKeyHash);
        await candidate.RequireNativeFamilyAsync(db, ct);
        if (!await candidate.CoversLiveFamilyAsync(db, roots, ct)) return null;
        return candidate;
    }

    internal async Task RequireNativeFamilyAsync(CP6Context db, CancellationToken ct)
    {
        var row = await ReadWebRefreshAsync(db, RefreshHash, ct);
        if (row is null || row.BrowserSessionId != FamilyId || row.TenantId != TenantId || row.UserId != UserId ||
            !await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.session_evidence
                    WHERE refresh_hash={RefreshHash} AND completion_id={CompletionId}) AS "Value"
                """).SingleAsync(ct)) throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT true AS "Value" FROM public."Sys_BrowserSessions"
            WHERE "TenantId"={TenantId} AND "UserId"={UserId} AND "Id"={FamilyId} FOR UPDATE
            """).SingleOrDefaultAsync(ct)) throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
    }

    internal async Task<bool> CoversLiveFamilyAsync(CP6Context db, CancellationToken ct)
    {
        var roots = TargetRefreshHash is null ? await RootHashesAsync(db, CompletionId, ct) : [];
        return await CoversLiveFamilyAsync(db, roots, ct);
    }

    private async Task<bool> CoversLiveFamilyAsync(CP6Context db, string[] roots, CancellationToken ct)
    {
        var targets = TargetRefreshHash is { } target ? new[] { target } : roots;
        var wholeOrigin = Kind == WebAuthenticationCloseKindV1.Logout;
        // Match RejectGenerationAsync's original seeds and descendant walk. A live refresh
        // outside that walk still authorizes the family and keeps this close local.
        var facts = await db.Database.SqlQuery<ClosureEffects>($"""
            WITH RECURSIVE affected AS (
              SELECT refresh_hash FROM cp6_authentication_v1.session_evidence
              WHERE refresh_hash=ANY({targets}) OR ({wholeOrigin} AND completion_id={CompletionId})
              UNION SELECT c.refresh_hash FROM cp6_authentication_v1.session_evidence c JOIN affected p ON c.parent_refresh_hash=p.refresh_hash
            ) SELECT EXISTS(SELECT 1 FROM affected a LEFT JOIN public."Sys_RefreshTokens" r ON a.refresh_hash=r."TokenHash"
                   WHERE r."BrowserSessionId" IS DISTINCT FROM {FamilyId} OR r."TenantId" IS DISTINCT FROM {TenantId}
                      OR r."UserId" IS DISTINCT FROM {UserId} OR r."ClientKind" IS DISTINCT FROM 'Web') AS "ForeignGeneration",
               EXISTS(SELECT 1 FROM public."Sys_RefreshTokens" r WHERE r."BrowserSessionId"={FamilyId}
                   AND r."TenantId"={TenantId} AND r."UserId"={UserId} AND r."ClientKind"='Web'
                   AND r."RevokedAt" IS NULL AND r."ExpiresAt">clock_timestamp()::timestamp
                   AND NOT EXISTS(SELECT 1 FROM affected a WHERE a.refresh_hash=r."TokenHash")) AS "SurvivingRefresh"
            """).SingleAsync(ct);
        if (facts.ForeignGeneration) throw new InvalidOperationException("AUTH_SESSION_AUTHORITY_UNAVAILABLE");
        return !facts.SurvivingRefresh;
    }

    internal async Task ApplyWithinRootAsync(CP6Context db, CancellationToken ct)
    {
        if (Kind == WebAuthenticationCloseKindV1.Browser)
        {
            var browser = new BrowserAuthenticationRecoveryV1(db);
            await new SsoMappingRecoveryV1(db).ResolveBrowserAsync(BrowserKeyHash!, close: true, ct);
            var proof = (await new AuthenticationInteractionStoreV1(db).ReadSessionByRefreshAsync(RefreshHash, ct))!.Parse().Primary;
            await browser.ApplyCloseWithinRootAsync(BrowserKeyHash!, TargetRefreshHash, TargetRefreshHash is null ? proof : null, ct);
            return;
        }

        if (Kind == WebAuthenticationCloseKindV1.Completion)
        {
            await AuthenticationCompletionRecoveryV1.ApplyCloseWithinRootAsync(db, CompletionId, TargetRefreshHash, ct);
            return;
        }

        await new AuthenticationInteractionStoreV1(db).RejectGenerationAsync(RefreshHash, true, ct);
        var originalAudit = LogoutAudit!;
        var audit = SecurityAuditService.CreateRecord(SecurityEventType.Logout, originalAudit.UserId, originalAudit.UserName,
            null, originalAudit.ClientIp, originalAudit.UserAgent, null);
        audit.TenantId = originalAudit.TenantId;
        var transaction = db.Database.CurrentTransaction!;
        const string auditSavepoint = "web_logout_audit_v1";
        await transaction.CreateSavepointAsync(auditSavepoint, ct);
        try
        {
            db.Sys_SecurityLogs.Add(audit);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception error) when (error is DbUpdateException or DbException)
        {
            // Ordinary logout has always kept its best-effort audit. Recover its savepoint
            // before committing the original termination. Browser close's audit remains required.
            await transaction.RollbackToSavepointAsync(auditSavepoint, ct);
            db.Entry(audit).State = EntityState.Detached;
            db.GetService<ILoggerFactory>().CreateLogger<SecurityAuditService>()
                .LogWarning("Web logout audit write failed for tenant {TenantId}, user {UserId}", originalAudit.TenantId, originalAudit.UserId);
        }
        await transaction.ReleaseSavepointAsync(auditSavepoint, ct);
    }

    internal static Task<WebRefreshRow?> ReadWebRefreshAsync(CP6Context db, string hash, CancellationToken ct)
        => db.Sys_RefreshTokens.IgnoreQueryFilters().AsNoTracking().Where(x => x.TokenHash == hash && x.ClientKind == "Web")
            .Select(x => new WebRefreshRow(x.TenantId, x.UserId, x.BrowserSessionId)).SingleOrDefaultAsync(ct);

    internal static async Task<string[]> RootHashesAsync(CP6Context db, Guid completion, CancellationToken ct)
        => (await db.Database.SqlQuery<string>($"""
            SELECT refresh_hash AS "Value" FROM cp6_authentication_v1.session_evidence
            WHERE completion_id={completion} AND parent_refresh_hash IS NULL ORDER BY refresh_hash
            """).ToListAsync(ct)).ToArray();

    internal sealed record WebRefreshRow(Guid TenantId, Guid UserId, Guid? BrowserSessionId);

    private sealed class ClosureEffects
    {
        public bool ForeignGeneration { get; init; }
        public bool SurvivingRefresh { get; init; }
    }
}
