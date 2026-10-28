using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Internal;
[ApiController,AllowAnonymous,ApiExplorerSettings(IgnoreApi=true),Route("internal/oa/app/v2")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ApprovalWithdrawalControllerV2:ControllerBase
{
    [HttpGet("submissions/{id:guid}/withdraw-context")]
    public Task<IActionResult> Context(Guid id)=>Run(async(owner,ct)=>Json(await owner.ContextAsync(id,ct),200));
    [HttpGet("withdraw-commands/{id:guid}")]
    public Task<IActionResult> Original(Guid id)=>Run(async(owner,ct)=>{var result=await owner.OriginalAsync(id,ct);return Json(result.Ack,result.Status==409?200:result.Status);});
    [HttpPost("submissions/{id:guid}/withdraw"),RequestSizeLimit(1048576)]
    public Task<IActionResult> Withdraw(Guid id)=>Run(async(owner,ct)=>{
        using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await Request.Body.ReadAsync(buffer,ct))!=0)
        {if(bytes.Length+count>1048576)throw new ScopeAuthorizationExceptionV1("WITHDRAW_SHAPE_INVALID",422);await bytes.WriteAsync(buffer.AsMemory(0,count),ct);}
        var result=await owner.WithdrawAsync(id,Request.Headers.IfMatch.ToString(),bytes.ToArray(),ct);return Json(result.Ack,result.Status);
    });
    private IActionResult Json(object value,int status)
    {Response.StatusCode=status;return File(ApprovalWithdrawalResponseV2.Bytes(value),"application/json");}
    private async Task<IActionResult> Run(Func<ApprovalWithdrawalOwnerV2,CancellationToken,Task<IActionResult>> action)
    {
        Response.Headers.CacheControl="no-store";
        try
        {
            await ApprovalIntakeServiceAuthorityV2.BindTenant(HttpContext,HttpContext.RequestAborted);
            if(Request.Query.Count!=0)throw new ScopeAuthorizationExceptionV1("WITHDRAW_SHAPE_INVALID",422);
            return await action(HttpContext.RequestServices.GetRequiredService<ApprovalWithdrawalOwnerV2>(),HttpContext.RequestAborted);
        }
        catch(ScopeAuthorizationExceptionV1 e){return Failure(e.Status,e.Message);}
        catch(CP6.Core.Services.Erp.OrderPriceCorrectionException e){return Failure(e.Status,e.Message);}
        catch(CurrentPermissionUnavailableException){return Failure(503,"AUTHORITY_UNAVAILABLE");}
        catch(Exception e)when(e is System.Data.Common.DbException or Microsoft.EntityFrameworkCore.DbUpdateException or TimeoutException){return Failure(503,"AUTHORITY_UNAVAILABLE");}
    }
    private ObjectResult Failure(int status,string code)=>StatusCode(status,new{code,messageKey=code,correlationId=Guid.NewGuid(),retryable=status is 429 or 503 or 504,
        fieldErrors=Array.Empty<object>(),currentRowVersion=(string?)null,resultQueryRef=(string?)null});
}
