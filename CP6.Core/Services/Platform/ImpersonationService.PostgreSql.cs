using System.Security.Claims;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels.Sys;
using CP6.Core.Services.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Platform;

public partial class ImpersonationService
{
    private sealed record PreparedSwitch<T>(T Result, string[] Cookies, Guid ActorTenant, string Jti, int Minutes);

    private Task<ImpersonationStartResult> StartPostgreSqlAsync(Guid tenantId, Guid? userId, string? reason,
        ClaimsPrincipal current, HttpResponse response)
        => SwitchPostgreSqlAsync(current, response, async (identity, ct) =>
        {
            var actor = await PlatformAdminAuthorization.RequireAsync(_db, current, ct, lockActor: true);
            if (actor.MustChangePassword || Single(identity, "cp6_auth_version") != actor.AuthenticationVersion) DenySwitch();
            var originalSession = await RequireOriginalSessionAsync(identity, await LockedUserAsync(actor.Id, ct), ct);
            await LockedTenantAsync(actor.TenantId, ct);
            var targetTenant = await LockedTenantAsync(tenantId, ct);
            var targetId = userId ?? await _db.Sys_Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.RoleId == 1 && u.Enable)
                .OrderBy(u => u.Id).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
            var target = targetId.HasValue ? await LockedUserAsync(targetId.Value, ct) : null;
            if (target is null || target.TenantId != tenantId || !target.Enable || (!userId.HasValue && target.RoleId != 1))
                throw new InvalidOperationException("E-SEC-035");
            var jti = Guid.NewGuid().ToString();
            var jwt = _config.GetSection("JWT");
            var token = JwtHelper.GenerateToken(target.Id.ToString(), target.UserName, jwt["Secret"]!, jwt["Issuer"]!, jwt["Audience"]!,
                _sec.ImpersonationMinutes, tenantId, jti, mustChangePassword: false, isPlatformAdmin: false,
                impersonatorId: actor.Id, authenticationVersion: AuthSessionVersion.For(target), impersonatorTenantId: actor.TenantId,
                impersonatorAuthenticationVersion: actor.AuthenticationVersion, impersonatorSessionReference: SessionReference(identity, "cp6_session"));
            var menus = await CurrentMenusAsync(target, ct);
            var cookies = PrepareCookies(token);
            await new AuthenticationInteractionStoreV1(_db).AliasAccessAsync(jti, originalSession.RefreshHash, ct);
            StageSwitchAudit(SecurityEventType.ImpersonationStarted, actor.Id, actor.UserName, actor.TenantId, target,
                targetTenant.TenantCode, reason, jti, new DateTimeOffset(new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo));
            return new PreparedSwitch<ImpersonationStartResult>(
                new(tenantId, targetTenant.TenantName, target.Id, target.UserName, menus, _sec.ImpersonationMinutes),
                cookies, actor.TenantId, jti, _sec.ImpersonationMinutes);
        });

    private Task<ImpersonationEndResult> EndPostgreSqlAsync(ClaimsPrincipal current, HttpResponse response)
        => SwitchPostgreSqlAsync(current, response, async (identity, ct) =>
        {
            if (identity.FindAll("is_platform_admin").Any(c => c.Value != "false")) DenySwitch();
            var effectiveId = GuidClaim(identity, ClaimTypes.NameIdentifier, "sub");
            var effectiveTenant = GuidClaim(identity, "tenant_id");
            if (effectiveTenant != _db.CurrentTenantId) DenySwitch();
            var actorId = GuidClaim(identity, "impersonator_id");
            var actorTenant = GuidClaim(identity, "impersonator_tenant_id");
            var actor = await LockedUserAsync(actorId, ct);
            if (actor is null || actor.TenantId != actorTenant || !actor.Enable || !actor.IsPlatformAdmin || actor.MustChangePassword
                || Single(identity, "impersonator_auth_version") != AuthSessionVersion.For(actor)) DenySwitch();
            await LockedTenantAsync(actorTenant, ct);
            var originalSession = await RequireOriginalSessionAsync(identity, actor, ct);
            var target = await LockedUserAsync(effectiveId, ct);
            if (target is null || target.TenantId != effectiveTenant || !target.Enable
                || Single(identity, "cp6_auth_version") != AuthSessionVersion.For(target)) DenySwitch();
            var targetTenant = await LockedTenantAsync(effectiveTenant, ct);
            var jti = Guid.NewGuid().ToString(); var jwt = _config.GetSection("JWT");
            var token = JwtHelper.GenerateToken(actor!.Id.ToString(), actor.UserName, jwt["Secret"]!, jwt["Issuer"]!, jwt["Audience"]!,
                _sec.Token.AccessTokenMinutes, actorTenant, jti, mustChangePassword: false, isPlatformAdmin: true,
                authenticationVersion: Single(identity, "impersonator_auth_version"), sessionReference: SessionReference(identity, "impersonator_session"));
            var menus = await CurrentMenusAsync(actor, ct); var cookies = PrepareCookies(token);
            await new AuthenticationInteractionStoreV1(_db).AliasAccessAsync(jti, originalSession.RefreshHash, ct);
            StageSwitchAudit(SecurityEventType.ImpersonationEnded, actor.Id, actor.UserName, actorTenant, target!,
                targetTenant.TenantCode, null, Single(identity, "jti"), DateTimeOffset.FromUnixTimeSeconds(long.Parse(Single(identity, "exp"))));
            return new PreparedSwitch<ImpersonationEndResult>(new(menus), cookies, actorTenant, jti, _sec.Token.AccessTokenMinutes);
        });

    private async Task<T> SwitchPostgreSqlAsync<T>(ClaimsPrincipal current, HttpResponse response,
        Func<ClaimsIdentity, CancellationToken, Task<PreparedSwitch<T>>> prepare)
    {
        // A caller-owned savepoint cannot authorize emitting a cookie before that caller commits.
        if (_db.Database.CurrentTransaction is not null) throw new InvalidOperationException("IMPERSONATION_REQUIRES_OWNED_TRANSACTION");
        if (response.HasStarted) throw new ImpersonationSwitchUnavailableException("IMPERSONATION_RESPONSE_ALREADY_STARTED");
        var identity = SwitchIdentity(current);
        var oldJti = Single(identity, "jti"); var expires = Single(identity, "exp");
        if (!long.TryParse(expires, out var exp) || exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            || exp > DateTimeOffset.MaxValue.ToUnixTimeSeconds()) DenySwitch();
        var ttl = DateTimeOffset.FromUnixTimeSeconds(exp) - DateTimeOffset.UtcNow;
        PreparedSwitch<T> result;
        var retirementAttempted = false;
        try
        {
            result = await PlatformAdminMutationV1.ExecuteAsync(_db, async ct =>
            {
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= exp) DenySwitch();
                try { if (await _blacklist.IsBlacklistedAsync(oldJti)) DenySwitch(); }
                catch (Exception error) when (error is not PlatformAdminAuthorizationException and not OperationCanceledException)
                { throw new ImpersonationSwitchUnavailableException("IMPERSONATION_SWITCH_UNAVAILABLE", error); }
                var prepared = await prepare(identity, ct);
                await _db.SaveChangesAsync(ct);
                // A root/row/audit lock may outlive the source access token.
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= exp) DenySwitch();
                // Cache is external to PostgreSQL. Retire the old access before commit;
                // failure/uncertainty yields no new cookie and never restores/replays it.
                retirementAttempted = true;
                await _blacklist.BlacklistAsync(oldJti, ttl);
                return prepared;
            }, response.HttpContext.RequestAborted);
        }
        catch (Exception error) when (retirementAttempted)
        { throw new ImpersonationSwitchUnavailableException("IMPERSONATION_SWITCH_OUTCOME_UNKNOWN", error); }
        try { response.Headers.Append("Set-Cookie", result.Cookies); }
        catch (Exception error)
        {
            // Exact newly minted access only; never revoke unrelated refresh/session state.
            try { await _blacklist.BlacklistAsync(result.Jti, TimeSpan.FromMinutes(result.Minutes)); }
            catch (Exception quarantine) { throw new ImpersonationSwitchUnavailableException("IMPERSONATION_SWITCH_OUTCOME_UNKNOWN", new AggregateException(error, quarantine)); }
            throw new ImpersonationSwitchUnavailableException("IMPERSONATION_SWITCH_OUTCOME_UNKNOWN", error);
        }
        _tenant.CurrentTenantId = result.ActorTenant;
        return result.Result;
    }

    private string[] PrepareCookies(string token)
    {
        // Evaluate the actual writer before any effect; only copy headers after commit.
        var staging = new DefaultHttpContext(); _cookies.WriteAccessCookieOnly(staging.Response, token);
        var cookies = staging.Response.Headers.SetCookie.Select(x => x!).ToArray();
        if (cookies.Length != 1 || !cookies[0].StartsWith(AuthCookieWriter.AccessCookie + "=", StringComparison.Ordinal))
            throw new ImpersonationSwitchUnavailableException("IMPERSONATION_COOKIE_PREPARATION_FAILED");
        return cookies;
    }

    private async Task<AuthenticationInteractionStoreV1.AuthenticationSessionRow> RequireOriginalSessionAsync(
        ClaimsIdentity identity, Sys_User? actor, CancellationToken ct)
    {
        if (actor == null) DenySwitch();
        var receipt = await new AuthenticationInteractionStoreV1(_db).ReadSessionAsync(Single(identity, "jti"), ct);
        await new AuthenticationSessionAuthorityV1(_db, new AuthenticationPolicyServiceV1(_db,
            Microsoft.Extensions.Options.Options.Create(_sec))).ValidateAsync(receipt, actor!, false, ct);
        if (receipt!.Parse().Restricted) DenySwitch();
        return receipt;
    }

    private async Task<Sys_Tenant> LockedTenantAsync(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Sys_Tenants.FromSqlInterpolated($"SELECT * FROM public.\"Sys_Tenants\" WHERE \"Id\"={id} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (tenant is null || !tenant.Enable || tenant.ExpireDate is { } expiry && expiry <= DateTime.Now)
            throw new InvalidOperationException("E-SEC-032");
        return tenant;
    }

    private Task<Sys_User?> LockedUserAsync(Guid id, CancellationToken ct)
        => _db.Sys_Users.FromSqlInterpolated($"SELECT * FROM public.\"Sys_Users\" WHERE \"Id\"={id} FOR SHARE")
            .IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(ct);

    private async Task<List<MenuRow>> CurrentMenusAsync(Sys_User user, CancellationToken ct)
    {
        var roles = await _db.Sys_UserRoles.IgnoreQueryFilters().Where(x => x.TenantId == user.TenantId && x.UserId == user.Id)
            .Select(x => x.RoleId).ToListAsync(ct);
        if (user.RoleId is { } primary) roles.Add(primary);
        var active = _db.Sys_Roles.IgnoreQueryFilters().Where(x => x.TenantId == user.TenantId && x.Enable && roles.Contains(x.RoleId)).Select(x => x.RoleId);
        var menus = _db.Sys_RoleMenus.IgnoreQueryFilters().Where(x => x.TenantId == user.TenantId && active.Contains(x.RoleId)).Select(x => x.MenuId);
        return await _db.Sys_Menus.Where(x => x.Enable && menus.Contains(x.MenuId)).OrderBy(x => x.OrderNo)
            .Select(x => new MenuRow(x.MenuId, x.MenuName, x.RoutePath, x.Icon, x.ParentId, x.OrderNo)).ToListAsync(ct);
    }

    private void StageSwitchAudit(SecurityEventType type, Guid actorId, string actorName, Guid actorTenant,
        Sys_User target, string targetTenantCode, string? reason, string sessionRef, DateTimeOffset expiresAtUtc)
        => _db.Sys_SecurityLogs.Add(new Sys_SecurityLog
        {
            TenantId = TenantContext.DefaultTenant, UserId = actorId, UserName = Truncate(actorName, 100),
            RequestTenantCode = targetTenantCode, EventType = (int)type, Reason = Truncate(reason, 256), CreatedAt = DateTime.Now,
            ContextJson = JsonSerializer.Serialize(new
            {
                version = 1, actualActorId = actorId, actualTenantId = actorTenant,
                effectiveActorId = target.Id, effectiveTenantId = target.TenantId, sessionRef, reason,
                recordedAtUtc = DateTimeOffset.UtcNow, expiresAtUtc,
                scope = type == SecurityEventType.ImpersonationStarted
                    ? "effective-user-permissions;platform-powers-suspended" : "impersonation-ended;platform-actor-restored"
            })
        });

    private static string? Truncate(string? value, int length) => value?.Length > length ? value[..length] : value;
    private static ClaimsIdentity SwitchIdentity(ClaimsPrincipal principal)
    {
        var identities = principal.Identities.Where(x => x.IsAuthenticated).ToArray();
        if (identities.Length != 1 || principal.HasClaim(c => c.Type == "token_use")) DenySwitch();
        var identity = identities[0];
        if (principal.Identities.Where(x => !ReferenceEquals(x, identity)).Any(x => x.Claims.Any(c => c.Type is
            ClaimTypes.NameIdentifier or "sub" or "tenant_id" or "is_platform_admin" or "must_change_password" or "jti" or "exp"
            or "cp6_auth_version" or "cp6_session" or "impersonator_id" or "impersonator_tenant_id" or "impersonator_auth_version" or "impersonator_session"))) DenySwitch();
        if (identity.FindAll("must_change_password").Any(c => c.Value != "false")) DenySwitch();
        return identity;
    }
    private static string Single(ClaimsIdentity identity, string name)
    {
        var values = identity.FindAll(name).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0].Value)) DenySwitch();
        return values[0].Value;
    }
    private static Guid GuidClaim(ClaimsIdentity identity, params string[] names)
    {
        Guid? value = null;
        foreach (var claim in identity.Claims.Where(c => names.Contains(c.Type)))
        {
            if (!Guid.TryParse(claim.Value, out var parsed) || parsed == Guid.Empty || value.HasValue && value != parsed) DenySwitch();
            value = parsed;
        }
        if (!value.HasValue) DenySwitch(); return value!.Value;
    }
    private static string? SessionReference(ClaimsIdentity identity, string name)
    {
        if (!identity.HasClaim(c => c.Type == name)) return null;
        var value = Single(identity, name);
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c))) DenySwitch();
        return value;
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void DenySwitch() => throw new PlatformAdminAuthorizationException("E-SEC-031");
}

internal sealed class ImpersonationSwitchUnavailableException(string code, Exception? inner = null) : InvalidOperationException(code, inner);
