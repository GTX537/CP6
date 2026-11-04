using CP6.Core.Services.Wms;
using CP6.Core.Services.Oa;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;

[ApiController, Authorize, OrderOwnerResult, ToolApprovalAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ToolDispositionApprovalController(ToolDispositionApprovalOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value,int status=200)=>StatusCode(status,new{code=0,data=value});
    [HttpGet("api/wms/tool-approval/v1/withdrawal-scope")]
    public async Task<IActionResult> WithdrawalScope(CancellationToken ct)=>Result(await owner.WithdrawalScopeAsync(ct));
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/approval-context")]
    public async Task<IActionResult> Context(Guid id,CancellationToken ct)
    {var c=await owner.ContextAsync(id,ct);Response.Headers.ETag=c.Etag;return Result(c);}
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/disposition-policies"),RequestSizeLimit(16384)]
    public async Task<IActionResult> Policy(Guid id,[FromBody]ToolDispositionPolicyCommandV1 input,CancellationToken ct)
        =>Result(await owner.PolicyAsync(id,Request.Headers.IfMatch.ToString(),input,ct),201);
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/approval-submissions"),RequestSizeLimit(16384)]
    public async Task<IActionResult> Submit(Guid id,[FromBody]ToolDispositionSubmitV1 input,CancellationToken ct)
        =>Result(await owner.SubmitAsync(id,Request.Headers.IfMatch.ToString(),input,ct),201);
    [HttpGet("api/wms/tool-approval/v1/operations/{id:guid}")]
    public async Task<IActionResult> Original(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("api/wms/tool-approval/v1/operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/approval-submissions/{submission:guid}/withdraw-context")]
    public async Task<IActionResult> WithdrawContext(Guid id,Guid submission,CancellationToken ct)=>Result(await owner.WithdrawalContextAsync(id,submission,ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/approval-submissions/{submission:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid id,Guid submission,[FromBody]ApprovalUserWithdrawV2 input,CancellationToken ct)
    {
        var value=await owner.WithdrawAsync(id,submission,Request.Headers.IfMatch.ToString(),input,ct,ActingAs());
        return Result(value);
    }
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/approval-submissions/{submission:guid}/withdrawals/{previous:guid}/reauthorize")]
    public async Task<IActionResult> ReauthorizeWithdrawal(Guid id,Guid submission,Guid previous,[FromBody]ApprovalUserWithdrawV2 input,CancellationToken ct)
    {
        var value=await owner.ReauthorizeWithdrawalAsync(id,submission,previous,Request.Headers.IfMatch.ToString(),input,ct,ActingAs());
        return Result(value);
    }
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/approval-submissions/{submission:guid}/business-grants")]
    public async Task<IActionResult> BusinessGrants(Guid id,Guid submission,CancellationToken ct)=>Result(await owner.BusinessGrantsAsync(id,submission,ct));
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/approval-business-delegates")]
    public async Task<IActionResult> BusinessDelegates(Guid id,CancellationToken ct)=>Result(await owner.BusinessDelegatesAsync(id,ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/approval-business-grants")]
    public async Task<IActionResult> BusinessGrant(Guid id,[FromBody]ToolBusinessGrantCommandV1 input,CancellationToken ct)
        =>Result(await owner.GrantBusinessAsync(id,input,ActingAs(),ct));
    [HttpGet("api/wms/tool-approval/v1/business-grant-operations/{id:guid}")]
    public async Task<IActionResult> BusinessGrantOperation(Guid id,CancellationToken ct)=>Result(await owner.BusinessGrantOperationAsync(id,false,ct));
    [HttpPost("api/wms/tool-approval/v1/business-grant-operations/{id:guid}/close")]
    public async Task<IActionResult> CloseBusinessGrant(Guid id,CancellationToken ct)=>Result(await owner.BusinessGrantOperationAsync(id,true,ct));
    private Guid? ActingAs()
    {
        var raw=Request.Headers["X-Acting-As"].ToString();if(raw.Length==0)return null;
        if(Guid.TryParseExact(raw,"D",out var id)&&id!=Guid.Empty)return id;
        throw new CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1("ACT_AS_INVALID",422);
    }
    [HttpGet("api/wms/tool-approval/v1/withdrawal-operations/{id:guid}")]
    public async Task<IActionResult> WithdrawalOperation(Guid id,CancellationToken ct)=>Result(await owner.WithdrawalOperationAsync(id,false,ct));
    [HttpPost("api/wms/tool-approval/v1/withdrawal-operations/{id:guid}/close")]
    public async Task<IActionResult> CloseWithdrawal(Guid id,CancellationToken ct)=>Result(await owner.WithdrawalOperationAsync(id,true,ct));
}

public sealed class ToolApprovalAvailabilityAttribute:Microsoft.AspNetCore.Mvc.Filters.ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context,
        Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)
    {
        var result=await next();
        if(result.Exception is CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1 error)
        {
            result.Result=new ObjectResult(new{code=error.Status,message=error.Message}){StatusCode=error.Status};
            result.ExceptionHandled=true;
        }
    }
}
