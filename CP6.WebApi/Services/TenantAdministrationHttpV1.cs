using System.Globalization;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;

namespace CP6.WebApi.Services;

internal static class TenantAdministrationHttpV1
{
    internal static TenantManagementSessionV1 Session(HttpContext http, IServiceProvider services)
        => new(token => IamAdministrationHttpV1.CurrentActor(http, services, token), async token =>
        {
            // Claims were validated by CurrentActor under the Owner gate. Only logout and
            // clock remain external; an authorized own-tenant change may invalidate its session.
            var expires = long.Parse(http.User.FindFirst("exp")!.Value, CultureInfo.InvariantCulture);
            if (expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                || await services.GetRequiredService<ITokenBlacklistService>().IsBlacklistedAsync(http.User.FindFirst("jti")!.Value))
                throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
            token.ThrowIfCancellationRequested();
        });
}
