using System.Data.Common;
using CP6.Core.Auth;
using CP6.WebApi.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CP6.WebApi.Health;

public static class PlatformHostIdentityEndpoint
{
    public const string Path = "/health/platform-identity";

    public static void MapPlatformHostIdentity(this WebApplication app)
    {
        // Also covers authentication/authorization failures, before the endpoint executes.
        app.Use(async (http, next) =>
        {
            if (string.Equals(http.Request.Path.Value?.TrimEnd('/'), Path, StringComparison.OrdinalIgnoreCase))
                http.Response.Headers.CacheControl = "no-store";
            await next(http);
        });

        app.MapGet(Path, (PlatformHostIdentityObserver observer) => Results.Ok(observer.Capture()))
            .RequireAuthorization()
            .AddEndpointFilter(async (invocation, next) =>
            {
                // Reuse the existing impersonation, claim and live DB revocation gates.
                // A minimal endpoint avoids MVC's global operation-log writer on this read-only probe.
                var action = new ActionContext(invocation.HttpContext, new RouteData(), new ActionDescriptor());
                var authorization = new AuthorizationFilterContext(action, new List<IFilterMetadata>());
                try
                {
                    await new RequirePlatformAdminAttribute().OnAuthorizationAsync(authorization);
                }
                catch (Exception error) when (error is DbException or TimeoutException or InvalidOperationException)
                {
                    return Results.Json(new { code = "IDENTITY_AUTHORITY_UNAVAILABLE" }, statusCode: 503);
                }
                if (authorization.Result is ObjectResult denied)
                    return Results.Json(denied.Value, statusCode: denied.StatusCode ?? 403);
                if (authorization.Result is not null) return Results.StatusCode(403);
                return await next(invocation);
            });
    }
}
