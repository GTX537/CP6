using System.Security.Claims;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Auth;

/// <summary>Current platform actor binding. This does not replace session authentication policy.</summary>
internal static class PlatformAdminAuthorization
{
    internal sealed record Actor(Guid Id, string UserName);

    internal static async Task<Actor> RequireAsync(CP6Context db, ClaimsPrincipal? principal,
        CancellationToken ct, bool lockActor = false)
    {
        // Impersonation denial has precedence even for an otherwise malformed principal.
        if (principal?.HasClaim(c => c.Type == "impersonator_id") == true)
            throw new PlatformAdminAuthorizationException("E-SEC-034");
        var identities = principal?.Identities.Where(x => x.IsAuthenticated).ToArray();
        if (identities is not { Length: 1 } || principal!.HasClaim(c => c.Type == "token_use")) Deny();
        var identity = identities![0];
        if (principal!.Identities.Where(x => !ReferenceEquals(x, identity)).Any(x => x.Claims.Any(IsAuthorityClaim))) Deny();
        var platform = identity.FindAll("is_platform_admin").ToArray();
        if (platform.Length == 0 || platform.Any(x => x.Value != "true")
            || identity.FindAll("must_change_password").Any(x => x.Value != "false")) Deny();
        var subject = ReadGuid(identity.Claims.Where(x => x.Type is ClaimTypes.NameIdentifier or "sub"));
        var tenant = ReadGuid(identity.FindAll("tenant_id"));
        if (tenant != db.CurrentTenantId) Deny();

        var query = db.Sys_Users.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == subject);
        if (lockActor)
        {
            if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is null)
                throw new InvalidOperationException("PLATFORM_ADMIN_ACTOR_REQUIRES_POSTGRESQL_TRANSACTION");
            // Acquire the administrator root first. This row lock also protects the actor
            // binding from a direct TenantId change until the actual grant/revoke commits.
            query = db.Sys_Users.FromSqlInterpolated($"SELECT * FROM public.\"Sys_Users\" WHERE \"Id\"={subject} FOR SHARE")
                .IgnoreQueryFilters().AsNoTracking();
        }
        var actor = await query.SingleOrDefaultAsync(ct);
        if (actor is null || actor.TenantId != tenant || !actor.Enable || !actor.IsPlatformAdmin) Deny();
        return new Actor(actor!.Id, actor.UserName);
    }

    private static bool IsAuthorityClaim(Claim claim) => claim.Type is ClaimTypes.NameIdentifier or "sub"
        or "tenant_id" or "is_platform_admin" or "must_change_password";

    private static Guid ReadGuid(IEnumerable<Claim> claims)
    {
        Guid? result = null;
        foreach (var claim in claims)
        {
            if (!Guid.TryParse(claim.Value, out var value) || value == Guid.Empty
                || (result.HasValue && result.Value != value)) Deny();
            result = value;
        }
        if (!result.HasValue) Deny();
        return result!.Value;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Deny() => throw new PlatformAdminAuthorizationException("E-SEC-031");
}

internal sealed class PlatformAdminAuthorizationException(string code) : InvalidOperationException(code);
