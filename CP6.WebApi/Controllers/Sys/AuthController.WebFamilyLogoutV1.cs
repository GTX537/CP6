using System.Data.Common;
using System.Security.Claims;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

public partial class AuthController
{
    private SqlCrmOidcGrantStore? NativeCrmGrants
        => HttpContext.RequestServices.GetService<ICrmOidcGrantStore>() as SqlCrmOidcGrantStore;

    private async Task<bool> CloseRegisteredWebFamilyAsync(string refresh)
    {
        var store = NativeCrmGrants;
        if (store is null) return false;
        var ct = HttpContext.RequestAborted;
        CrmOidcFamilyLogoutV1? accepted = null;
        try
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            await AuthenticationCompletionGateV1.AcquireAsync(_context, ct);
            var source = await PrepareWebFamilyLogoutAsync(refresh, ct);
            if (source is not null) accepted = await store.TryStageWebFamilyCloseAsync(_context, source, ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception error) when (error is CrmOidcFamilyLogoutUnavailableException or DbException or DbUpdateException)
        {
            throw new RefreshCompletionUnknownException(error);
        }
        if (accepted is null) return false;
        await CompleteWebFamilyAsync(store, accepted, ct);
        return SqlCrmOidcGrantStore.ReadWebClose(accepted)?.Kind == WebAuthenticationCloseKindV1.Logout;
    }

    private async Task<TResult> ResolveWebFamilyAsync<TResult>(bool close,
        Func<Func<WebAuthenticationFamilyClosureV1, CancellationToken, Task<bool>>?, Task<TResult>> resolve)
    {
        var store = close ? NativeCrmGrants : null;
        if (store is null) return await resolve(null);
        CrmOidcFamilyLogoutV1? accepted = null;
        TResult result;
        try
        {
            result = await resolve(async (source, ct) =>
                (accepted = await store.TryStageWebFamilyCloseAsync(_context, source, ct)) is not null);
        }
        catch (Exception error) when (error is CrmOidcFamilyLogoutUnavailableException or DbException or DbUpdateException)
        {
            throw new RefreshCompletionUnknownException(error);
        }
        if (accepted is null) return result;
        await CompleteWebFamilyAsync(store, accepted, HttpContext.RequestAborted);
        // The original cookie or capability still owns its existing response, including SSO mapping reads.
        return await resolve(null);
    }

    private static async Task CompleteWebFamilyAsync(SqlCrmOidcGrantStore store, CrmOidcFamilyLogoutV1 accepted, CancellationToken ct)
    {
        try { await store.CompleteAcceptedFamilyAsync(accepted, ct); }
        catch (Exception error) when (error is CrmOidcFamilyLogoutUnavailableException or DbException or DbUpdateException)
        {
            throw new RefreshCompletionUnknownException(error);
        }
    }

    // The action's signed identity and CSRF have already been checked. Pick the exact
    // persisted Web source under the same authentication root as refresh and completion.
    private async Task<WebAuthenticationFamilyClosureV1?> PrepareWebFamilyLogoutAsync(string raw, CancellationToken ct)
    {
        var identity = User.Identities.Single(i => i.IsAuthenticated);
        var acting = identity.FindFirst("impersonator_id");
        var alias = identity.FindFirst(acting is null ? "cp6_session" : "impersonator_session")?.Value;
        var signedJti = identity.FindFirst("jti")!.Value;
        var refreshHash = AuthenticationInteractionStoreV1.Hash(raw);
        var presented = await WebAuthenticationFamilyClosureV1.ReadWebRefreshAsync(_context, refreshHash, ct);
        // With no recognized Web receipt for this cookie the raw action keeps its legacy local behavior.
        if (presented is null) return null;
        var nativeReceipt = await new AuthenticationInteractionStoreV1(_context).ReadSessionAsync(signedJti, ct);
        var origin = nativeReceipt is null ? null : await WebAuthenticationFamilyClosureV1.ReadWebRefreshAsync(_context, nativeReceipt.RefreshHash, ct);
        if (origin?.BrowserSessionId is null && presented.BrowserSessionId is null && alias is null) return null;

        if (nativeReceipt is null || origin?.BrowserSessionId is null || presented != origin ||
            alias is not null && alias != nativeReceipt.RefreshHash ||
            !await AccessTokenAuthentication.CurrentStateAsync(_context, Policies, User, ct))
            throw new InvalidOperationException("REAUTH_REQUIRED");

        var signedActor = acting?.Value ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? identity.FindFirst("sub")?.Value;
        var signedTenant = identity.FindFirst(acting is null ? "tenant_id" : "impersonator_tenant_id")?.Value;
        if (!Guid.TryParse(signedActor, out var actor) || actor != origin.UserId ||
            !Guid.TryParse(signedTenant, out var tenant) || tenant != origin.TenantId)
            throw new InvalidOperationException("REAUTH_REQUIRED");

        var close = await WebAuthenticationFamilyClosureV1.PrepareAsync(_context, WebAuthenticationCloseKindV1.Logout,
            refreshHash, refreshHash, nativeReceipt.Parse().Primary.CompletionRef, null, ct);
        if (close is null) return null;
        // Raw logout has always audited the authenticated effective account.
        var effectiveUser = Guid.Parse((identity.FindFirst(ClaimTypes.NameIdentifier) ?? identity.FindFirst("sub"))!.Value);
        var effectiveTenant = Guid.Parse(identity.FindFirst("tenant_id")!.Value);
        var audit = SecurityAuditService.CreateRecord(SecurityEventType.Logout, effectiveUser, identity.Name, null, ClientIp, ClientUa, null);
        return close with { LogoutAudit = new(effectiveTenant, effectiveUser, audit.UserName, audit.ClientIp, audit.UserAgent) };
    }
}
