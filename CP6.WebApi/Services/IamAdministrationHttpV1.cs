using System.Security.Claims;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.WebApi.Services;

public static class IamAdministrationHttpV1
{
    public static async Task<Guid> CurrentActor(HttpContext context,CancellationToken ct)
    {
        bool current;
        try { current=await AccessTokenAuthentication.IsCurrentAsync(context.RequestServices,context.User,ct); }
        catch(InvalidOperationException e) when(e.Message is "REAUTH_REQUIRED" or "AUTHENTICATION_DENIED" or "SSO_REQUIRED")
        { throw new IamAdministrationExceptionV1(401,"REAUTH_REQUIRED"); }
        var user=context.User;
        if(!current || user.HasClaim(x=>x.Type=="impersonator_id") || user.FindFirstValue("must_change_password")!="false"
            || !long.TryParse(user.FindFirstValue("exp"),out var expires) || expires<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier)??user.FindFirstValue("sub"),out var actor))
            throw new IamAdministrationExceptionV1(401,"REAUTH_REQUIRED");
        return actor;
    }
    public static async Task<IActionResult> Execute(ControllerBase controller,Func<Task<object>> action)
    {
        try { return controller.Ok(await action()); }
        catch(IamAdministrationExceptionV1 e){return controller.StatusCode(e.Status,new {code=e.Message});}
        catch(DbUpdateException e) when(PlatformAdminMutationV1.IsSurvivorViolation(e))
        {return controller.Conflict(new {code="E-SEC-037"});}
        catch(DbUpdateException e) when(e.InnerException is PostgresException {SqlState:"23505"})
        {return controller.Conflict(new {code="IAM_REFERENCE_CONFLICT"});}
    }
}
