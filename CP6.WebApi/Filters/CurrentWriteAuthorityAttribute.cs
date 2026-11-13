using System.Data.Common;
using System.Runtime.ExceptionServices;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Filters;

/// <summary>For commands that use the request's Core context and save their effects
/// before returning. The Owner and Main IAM share the native transaction root.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CurrentWriteAuthorityAttribute : Attribute, IAsyncActionFilter
{
    public bool ChangesAuthentication { get; set; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        if (!services.GetRequiredService<CP6Context>().Database.IsNpgsql())
        {
            await next();
            return;
        }

        var permission = context.Filters.OfType<RequirePermissionAttribute>().Single().PermissionKey;
        var authority = services.GetRequiredService<IamAdministrationV1>();
        ActionExecutedContext? executed = null;
        try
        {
            await authority.ExecuteAsync(permission, CurrentActor, async (actor, ct) =>
            {
                executed = await next();
                if (executed is { Exception: { } error, ExceptionHandled: false })
                    (executed.ExceptionDispatchInfo ?? ExceptionDispatchInfo.Capture(error)).Throw();
                if (ChangesAuthentication)
                    await TenantAdministrationHttpV1.Complete(http, services, ct);
                else if (await CurrentActor(ct) != actor)
                    throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
                return true;
            }, http.RequestAborted);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            Fail(error.Status, error.Message);
        }
        catch (Exception error) when (error is DbException or DbUpdateException or CurrentPermissionUnavailableException)
        {
            Fail(503, "AUTHORITY_FENCE_UNAVAILABLE");
        }

        // These existing actions accept a validated impersonation session.
        Task<Guid> CurrentActor(CancellationToken ct) =>
            IamAdministrationHttpV1.CurrentActor(http, services, ct, allowImpersonation: true);

        void Fail(int status, string code)
        {
            var result = new ObjectResult(new { code = status, message = code }) { StatusCode = status };
            if (executed == null)
                context.Result = result;
            else
            {
                executed.Result = result;
                executed.ExceptionHandled = true;
            }
        }
    }
}
