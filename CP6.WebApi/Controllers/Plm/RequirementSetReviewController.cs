using CP6.Core.Services.Plm;
using CP6.Core.Services.Oa;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Plm;

[ApiController,Authorize,OrderOwnerResult,RequirementSetReviewAvailability,Route("api/requirement-set/owner-v1")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class RequirementSetReviewController(RequirementSetReviewOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value)=>Ok(new{code=0,data=value});
    [HttpGet("review-roles")]
    public async Task<IActionResult> Roles(CancellationToken ct)=>Result(await owner.RolesAsync(ct));
    [HttpGet("{id:guid}/review-context")]
    public async Task<IActionResult> Context(Guid id,CancellationToken ct)
    {var value=await owner.ContextAsync(id,ct);Response.Headers.ETag=value.Etag;return Result(value);}
    [HttpPost("{id:guid}/review-policies"),RequestSizeLimit(8388608)]
    public async Task<IActionResult> Policy(Guid id,[FromBody]RequirementSetReviewPolicyCommandV1 input,CancellationToken ct)
        =>Result(await owner.PolicyAsync(id,Request.Headers.IfMatch.ToString(),input,ct));
    [HttpPost("{id:guid}/review-submissions"),RequestSizeLimit(8388608)]
    public async Task<IActionResult> Submit(Guid id,[FromBody]RequirementSetReviewSubmitV1 input,CancellationToken ct)
        =>Result(await owner.SubmitAsync(id,Request.Headers.IfMatch.ToString(),input,ct));
    [HttpGet("review-operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("review-operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
    [HttpGet("{id:guid}/review-submissions/{submission:guid}")]
    public async Task<IActionResult> Submission(Guid id,Guid submission,CancellationToken ct)=>Result(await owner.SubmissionAsync(id,submission,ct));
    [HttpPost("{id:guid}/review-application")]
    public async Task<IActionResult> Apply(Guid id,[FromBody]RequirementSetApplicationCommandV1 input,CancellationToken ct)
        =>Result(await owner.ApplyAsync(id,Request.Headers.IfMatch.ToString(),input,ct));
    [HttpGet("{id:guid}/review-submissions/{submission:guid}/withdraw-context")]
    public async Task<IActionResult> WithdrawContext(Guid id,Guid submission,CancellationToken ct)=>Result(await owner.WithdrawalContextAsync(id,submission,ct));
    [HttpPost("{id:guid}/review-submissions/{submission:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid id,Guid submission,[FromBody]ApprovalUserWithdrawV2 input,CancellationToken ct)
    {
        var value=await owner.WithdrawAsync(id,submission,Request.Headers.IfMatch.ToString(),input,ct,ActingAs());
        return value.State=="CONFLICT"?Conflict(new{code=0,data=value}):Result(value);
    }
    [HttpPost("{id:guid}/review-submissions/{submission:guid}/withdrawals/{previous:guid}/reauthorize")]
    public async Task<IActionResult> ReauthorizeWithdrawal(Guid id,Guid submission,Guid previous,[FromBody]ApprovalUserWithdrawV2 input,CancellationToken ct)
    {
        var value=await owner.ReauthorizeWithdrawalAsync(id,submission,previous,Request.Headers.IfMatch.ToString(),input,ct,ActingAs());
        return value.State=="CONFLICT"?Conflict(new{code=0,data=value}):Result(value);
    }
    [HttpGet("{id:guid}/review-submissions/{submission:guid}/business-grants")]
    public async Task<IActionResult> BusinessGrants(Guid id,Guid submission,CancellationToken ct)=>Result(await owner.BusinessGrantsAsync(id,submission,ct));
    [HttpGet("{id:guid}/review-business-delegates")]
    public async Task<IActionResult> BusinessDelegates(Guid id,CancellationToken ct)=>Result(await owner.BusinessDelegatesAsync(id,ct));
    [HttpPost("{id:guid}/review-business-grants")]
    public async Task<IActionResult> BusinessGrant(Guid id,[FromBody]RequirementSetBusinessGrantCommandV1 input,CancellationToken ct)
        =>Result(await owner.GrantBusinessAsync(id,input,ActingAs(),ct));
    [HttpGet("review-business-grant-operations/{id:guid}")]
    public async Task<IActionResult> BusinessGrantOperation(Guid id,CancellationToken ct)=>Result(await owner.BusinessGrantOperationAsync(id,false,ct));
    [HttpPost("review-business-grant-operations/{id:guid}/close")]
    public async Task<IActionResult> CloseBusinessGrant(Guid id,CancellationToken ct)=>Result(await owner.BusinessGrantOperationAsync(id,true,ct));
    private Guid? ActingAs()
    {
        var raw=Request.Headers["X-Acting-As"].ToString();if(raw.Length==0)return null;
        if(Guid.TryParseExact(raw,"D",out var id)&&id!=Guid.Empty)return id;
        throw new CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1("ACT_AS_INVALID",422);
    }
    [HttpGet("review-withdrawal-operations/{id:guid}")]
    public async Task<IActionResult> WithdrawalOperation(Guid id,CancellationToken ct)=>Result(await owner.WithdrawalOperationAsync(id,false,ct));
    [HttpPost("review-withdrawal-operations/{id:guid}/close")]
    public async Task<IActionResult> CloseWithdrawal(Guid id,CancellationToken ct)=>Result(await owner.WithdrawalOperationAsync(id,true,ct));
    [HttpGet("review-application-operations/{id:guid}")]
    public async Task<IActionResult> ApplicationOperation(Guid id,CancellationToken ct)=>Result(await owner.ApplicationOperationAsync(id,false,ct));
    [HttpPost("review-application-operations/{id:guid}/close")]
    public async Task<IActionResult> CloseApplication(Guid id,CancellationToken ct)=>Result(await owner.ApplicationOperationAsync(id,true,ct));
}

/// <summary>Preserve safe native Scope failures at the requirementSet HTTP boundary.</summary>
public sealed class RequirementSetReviewAvailabilityAttribute:Microsoft.AspNetCore.Mvc.Filters.ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context,
        Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)
    {
        var result=await next();
        if(result.Exception is CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1 error)
        {
            context.HttpContext.Response.Headers.CacheControl="no-store";
            result.Result=new ObjectResult(new{code=error.Status,message=error.Message}){StatusCode=error.Status};
            result.ExceptionHandled=true;
        }
    }
}
