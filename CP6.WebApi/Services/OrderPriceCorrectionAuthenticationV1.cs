using System.Security.Claims;
using CP6.Core.Services.Erp;

namespace CP6.WebApi.Services;

/// <summary>Reconsume the actual original access/session evidence under the Owner transaction root.</summary>
public static class OrderPriceCorrectionAuthenticationV1
{
    public static async Task<Guid> CurrentActor(HttpContext? context, CancellationToken ct)
    {
        if (context is null) throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
        try
        {
            var current = await AccessTokenAuthentication.IsCurrentAsync(context.RequestServices, context.User, ct);
            var identities = context.User.Identities.Where(x => x.IsAuthenticated).ToArray();
            if (!current || identities.Length != 1) throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
            var identity = identities[0];
            var expirations = identity.FindAll("exp").ToArray();
            if (context.User.Identities.Where(x => !ReferenceEquals(x, identity)).Any(x => x.HasClaim(c => c.Type == "exp"))
                || expirations.Length != 1 || !long.TryParse(expirations[0].Value, out var expires)
                || expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                || identity.FindFirst("must_change_password")?.Value != "false"
                || !Guid.TryParse(identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? identity.FindFirst("sub")?.Value, out var actor)
                || actor == Guid.Empty)
                throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
            // Impersonation keeps its existing original actor/session validation and effective
            // user's business permissions. No platform-admin shortcut is introduced.
            return actor;
        }
        catch (OrderPriceCorrectionException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (InvalidOperationException error) when (error.Message is "REAUTH_REQUIRED" or "AUTHENTICATION_DENIED" or "SSO_REQUIRED")
        { throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401, error); }
        catch (Exception error)
        { throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503, error); }
    }
}
