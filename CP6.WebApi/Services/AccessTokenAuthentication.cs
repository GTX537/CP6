using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

/// <summary>Consume signed original state against current PostgreSQL authority, before controller permission/caching.</summary>
public static class AccessTokenAuthentication
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        try
        {
            var services = context.HttpContext.RequestServices;
            var principal = context.Principal;
            var jti = principal?.FindFirst("jti")?.Value;
            if (string.IsNullOrEmpty(jti) || await services.GetRequiredService<ITokenBlacklistService>().IsBlacklistedAsync(jti))
            { context.Fail("access token revoked or missing identity"); return; }
            var db = services.GetRequiredService<CP6Context>();
            if (db.Database.IsNpgsql() && !await CurrentStateAsync(db, principal!, context.HttpContext.RequestAborted))
            { context.Fail("authentication state changed or original evidence missing"); return; }
            var imp = principal?.FindFirst("impersonator_id")?.Value;
            var family = principal?.FindFirst(imp == null ? "cp6_session" : "impersonator_session")?.Value;
            if (family != null)
            {
                var subject = imp ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var tenant = principal?.FindFirst(imp == null ? "tenant_id" : "impersonator_tenant_id")?.Value;
                if (!Guid.TryParse(subject, out var userId) || !Guid.TryParse(tenant, out var tenantId)
                    || !await services.GetRequiredService<CrmOidcDirectory>().RefreshFamilyMatchesAsync(new CrmOidcGrant
                        { SourceRefreshHash = family, SubjectId = userId, OrganizationId = tenantId }))
                    context.Fail("browser session revoked");
            }
        }
        catch (Exception error)
        {
            // A failed authority/cache read cannot authenticate from a previously valid signature.
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(nameof(AccessTokenAuthentication)).LogWarning("Access state unavailable: {ErrorType}", error.GetType().Name);
            context.Fail("authentication authority unavailable");
        }
    }

    private static async Task<bool> CurrentStateAsync(CP6Context db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var identities = principal.Identities.Where(x => x.IsAuthenticated).ToArray();
        if (identities.Length != 1) return false;
        var identity = identities[0];
        string[] authority = [ClaimTypes.NameIdentifier, "sub", "tenant_id", "jti", "cp6_auth_version", "token_use",
            "must_change_password", "is_platform_admin", "impersonator_id", "impersonator_tenant_id",
            "impersonator_auth_version", "cp6_session", "impersonator_session"];
        if (principal.Identities.Where(x => !ReferenceEquals(x, identity)).SelectMany(x => x.Claims).Any(x => authority.Contains(x.Type))) return false;
        if (authority.Any(type => identity.FindAll(type).Count() > 1) || identity.HasClaim(x => x.Type == "token_use")) return false;
        string? Value(string name) => identity.FindFirst(name)?.Value;
        var subject = Value(ClaimTypes.NameIdentifier) ?? Value("sub");
        if (Value("sub") is { } sub && sub != subject) return false;
        if (!Guid.TryParse(subject, out var id) || id == Guid.Empty || !Guid.TryParse(Value("tenant_id"), out var tenant) || tenant == Guid.Empty) return false;
        var user = await ActiveUserAsync(db, id, tenant, ct);
        if (user == null || Value("cp6_auth_version") != AuthSessionVersion.For(user)
            || Value("must_change_password") is not ("true" or "false")) return false;
        if (Value("impersonator_id") is { } actorText)
        {
            if (!Guid.TryParse(actorText, out var actorId) || actorId == Guid.Empty
                || !Guid.TryParse(Value("impersonator_tenant_id"), out var actorTenant) || actorTenant == Guid.Empty
                || Value("is_platform_admin") != null || Value("cp6_session") != null) return false;
            var actor = await ActiveUserAsync(db, actorId, actorTenant, ct);
            return actor is { IsPlatformAdmin: true, MustChangePassword: false }
                && Value("impersonator_auth_version") == AuthSessionVersion.For(actor);
        }
        return Value("impersonator_tenant_id") == null && Value("impersonator_auth_version") == null && Value("impersonator_session") == null
            && (!user.MustChangePassword || Value("must_change_password") == "true")
            && (Value("is_platform_admin") == null || Value("is_platform_admin") == "true" && user.IsPlatformAdmin);
    }

    private static async Task<Sys_User?> ActiveUserAsync(CP6Context db, Guid id, Guid tenant, CancellationToken ct)
    {
        var user = await db.Sys_Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenant, ct);
        return user is { Enable: true } && !(user.LockedUntil > DateTime.Now)
            && await db.Sys_Tenants.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.Id == tenant && x.Enable
                && (x.ExpireDate == null || x.ExpireDate > DateTime.Now), ct) ? user : null;
    }
}
