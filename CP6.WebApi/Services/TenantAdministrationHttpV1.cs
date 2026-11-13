using System.Globalization;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;

namespace CP6.WebApi.Services;

internal static class TenantAdministrationHttpV1
{
    internal static TenantManagementSessionV1 Session(HttpContext http, IServiceProvider services)
        => new(token => IamAdministrationHttpV1.CurrentActor(http, services, token),
            token => Complete(http, services, token));

    internal static async Task Complete(HttpContext http, IServiceProvider services, CancellationToken ct)
    {
        // Claims were validated under the Owner gate. An authorized change can invalidate
        // its own database session. Clock and logout still apply at completion.
        var expires = long.Parse(http.User.FindFirst("exp")!.Value, CultureInfo.InvariantCulture);
        if (expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            || await services.GetRequiredService<ITokenBlacklistService>().IsBlacklistedAsync(http.User.FindFirst("jti")!.Value))
            throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
        ct.ThrowIfCancellationRequested();
    }
}
