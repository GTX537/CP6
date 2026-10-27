using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Internal;

[ApiController,AllowAnonymous,ApiExplorerSettings(IgnoreApi=true)]
[Route("/internal/oa/app/v2"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ApprovalIntakeControllerV2 : ControllerBase
{
    [HttpPost("submissions"),RequestSizeLimit(1048576)]
    public Task<IActionResult> Submit()=>Run(async (owner,ct)=>{
        using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=await Request.Body.ReadAsync(buffer,ct))!=0)
        {if(bytes.Length+read>1048576)throw new ScopeAuthorizationExceptionV1("SUBMISSION_SHAPE_INVALID",422);await bytes.WriteAsync(buffer.AsMemory(0,read),ct);}
        var result=await owner.SubmitAsync(bytes.ToArray(),ct);return StatusCode(result.Status,result.Ack);
    });
    [HttpGet("commands/{command:guid}")]
    public Task<IActionResult> Original(Guid command)=>Run(async(owner,ct)=>Ok(await owner.OriginalAsync(command,ct)));
    private async Task<IActionResult> Run(Func<ApprovalIntakeOwnerV2,CancellationToken,Task<IActionResult>> action)
    {
        Response.Headers.CacheControl="no-store";
        try
        {
            await ApprovalIntakeServiceAuthorityV2.BindTenant(HttpContext,HttpContext.RequestAborted);
            if(Request.Query.Count!=0)throw new ScopeAuthorizationExceptionV1("SUBMISSION_SHAPE_INVALID",422);
            return await action(HttpContext.RequestServices.GetRequiredService<ApprovalIntakeOwnerV2>(),HttpContext.RequestAborted);
        }
        catch(ScopeAuthorizationExceptionV1 e){return Failure(e.Status,e.Message);}
        catch(OrderPriceCorrectionException e){return Failure(e.Status,e.Message);}
        catch(CurrentPermissionUnavailableException){return Failure(503,"AUTHORITY_UNAVAILABLE");}
        catch(Exception e)when(e is System.Data.Common.DbException or Microsoft.EntityFrameworkCore.DbUpdateException or TimeoutException)
        {return Failure(503,"AUTHORITY_UNAVAILABLE");}
    }
    private ObjectResult Failure(int status,string code)=>StatusCode(status,new{code,messageKey=code,correlationId=Guid.NewGuid(),retryable=status is 429 or 503 or 504,
        fieldErrors=status==422?new[]{new{field="$",code}}:[],currentRowVersion=(string?)null,resultQueryRef=(string?)null});
}
