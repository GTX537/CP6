using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Services;

// The completion writer has already validated the original signed principal inside the authentication fence.
internal sealed class MesCompletionSessionAuthorityV1(IServiceProvider services) : IMesCompletionSessionsV1
{
    public MesCompletionSessionV1 Capture(Guid subject, Guid tenant)
    {
        var identity = services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User.Identities
            .Single(item => item.IsAuthenticated);
        string? Value(string key) => identity.FindFirst(key)?.Value;
        if (Guid.Parse(Value("tenant_id")!) != tenant)
            throw ReauthenticationRequired();

        var actor = Value("impersonator_id");
        return new(subject, Value("jti")!, long.Parse(Value("exp")!), Value("cp6_auth_version")!,
            Value("is_platform_admin") == "true", actor != null ? Guid.Parse(actor) : null,
            actor != null ? Guid.Parse(Value("impersonator_tenant_id")!) : null,
            Value("impersonator_auth_version"), Value(actor == null ? "cp6_session" : "impersonator_session") != null);
    }

    public async Task RequireCurrentAsync(MesCompletionSessionV1 session, Guid tenant, CancellationToken ct)
    {
        if (session.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            || await services.GetRequiredService<ITokenBlacklistService>().IsBlacklistedAsync(session.AccessId))
            throw ReauthenticationRequired();

        var db = services.GetRequiredService<CP6Context>();
        var effective = await AccessTokenAuthentication.ActiveUserAsync(db, session.Subject, tenant, ct);
        if (effective == null || session.AuthenticationVersion != AuthSessionVersion.For(effective))
            throw ReauthenticationRequired();

        Sys_User loginUser = effective;
        if (session.Impersonator is Guid impersonator)
        {
            var actor = await AccessTokenAuthentication.ActiveUserAsync(db, impersonator, session.ImpersonatorTenant!.Value, ct);
            if (actor is not { IsPlatformAdmin: true, MustChangePassword: false }
                || session.ImpersonatorVersion != AuthSessionVersion.For(actor))
                throw ReauthenticationRequired();
            loginUser = actor;
        }
        else if (effective.MustChangePassword || session.PlatformAdmin && !effective.IsPlatformAdmin)
            throw ReauthenticationRequired();

        var receipt = await new AuthenticationInteractionStoreV1(db).ReadSessionAsync(session.AccessId, ct);
        try
        {
            await new AuthenticationSessionAuthorityV1(db, services.GetRequiredService<AuthenticationPolicyServiceV1>())
                .ValidateAsync(receipt, loginUser, refresh: false, ct);
        }
        catch (InvalidOperationException error) when (error.Message == "REAUTH_REQUIRED")
        {
            throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401, error);
        }

        if (receipt!.Parse().Restricted || session.HasBrowserFamily
            && !await services.GetRequiredService<CrmOidcDirectory>().RefreshFamilyMatchesAsync(new CrmOidcGrant
            {
                SubjectId = loginUser.Id, OrganizationId = loginUser.TenantId, SourceRefreshHash = receipt.RefreshHash,
            }))
            throw ReauthenticationRequired();
    }

    private static OrderPriceCorrectionException ReauthenticationRequired() => new("REAUTH_REQUIRED", 401);
}
