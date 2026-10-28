using CP6.Core.Services.Plm;
using CP6.Core.Services.Oa;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Plm;

[ApiController,Authorize,OrderOwnerResult,IterationReviewAvailability,Route("api/engineering-iterations/owner-v1")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class IterationReviewController(IterationReviewOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value)=>Ok(new{code=0,data=value});
    [HttpGet("review-roles")]
    public async Task<IActionResult> Roles(CancellationToken ct)=>Result(await owner.RolesAsync(ct));
    [HttpGet("{id:guid}/reviewers")]
    public async Task<IActionResult> Reviewers(Guid id,CancellationToken ct)=>Result(await owner.ReviewersAsync(id,ct));
    [HttpGet("{id:guid}/review-context")]
    public async Task<IActionResult> Context(Guid id,CancellationToken ct)
    {var value=await owner.ContextAsync(id,ct);Response.Headers.ETag=value.Etag;return Result(value);}
    [HttpPost("{id:guid}/review-policies"),RequestSizeLimit(8388608)]
    public async Task<IActionResult> Policy(Guid id,[FromBody]IterationReviewPolicyCommandV1 input,CancellationToken ct)
        =>Result(await owner.PolicyAsync(id,Request.Headers.IfMatch.ToString(),input,ct));
    [HttpPost("{id:guid}/review-submissions"),RequestSizeLimit(8388608)]
    public async Task<IActionResult> Submit(Guid id,[FromBody]IterationReviewSubmitV1 input,CancellationToken ct)
        =>Result(await owner.SubmitAsync(id,Request.Headers.IfMatch.ToString(),input,ct));
    [HttpGet("review-operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("review-operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
    [HttpGet("{id:guid}/review-submissions/{submission:guid}")]
    public async Task<IActionResult> Submission(Guid id,Guid submission,CancellationToken ct)=>Result(await owner.SubmissionAsync(id,submission,ct));

    [HttpGet("{id:guid}/review-submissions/{submission:guid}/application")]
    public async Task<IActionResult> Application(Guid id,Guid submission,CancellationToken ct)=>Result(await owner.ApplicationAsync(id,submission,ct));
    [HttpPost("{id:guid}/review-application")]
    public async Task<IActionResult> Apply(Guid id,[FromBody]IterationApplicationCommandV1 input,CancellationToken ct)=>Result(await owner.ApplyAsync(id,Request.Headers.IfMatch.ToString(),input,ct));
    [HttpGet("review-application-operations/{id:guid}")]
    public async Task<IActionResult> ApplicationOperation(Guid id,CancellationToken ct)=>Result(await owner.ApplicationOperationAsync(id,false,ct));
    [HttpPost("review-application-operations/{id:guid}/close")]
    public async Task<IActionResult> CloseApplication(Guid id,CancellationToken ct)=>Result(await owner.ApplicationOperationAsync(id,true,ct));
    [HttpGet("{id:guid}/review-issues")]
    public async Task<IActionResult> Issues(Guid id,CancellationToken ct)=>Result(await owner.IssuesAsync(id,ct));
    [HttpPost("{id:guid}/review-issues"),RequestSizeLimit(1048576)]
    public async Task<IActionResult> Issue(Guid id,[FromBody]IterationReviewIssueCommandV1 input,CancellationToken ct)=>Result(await owner.IssueAsync(id,Request.Headers.IfMatch.ToString(),input,ct));
    [HttpGet("review-issue-operations/{id:guid}")]
    public async Task<IActionResult> IssueOperation(Guid id,CancellationToken ct)=>Result(await owner.IssueOperationAsync(id,false,ct));
    [HttpPost("review-issue-operations/{id:guid}/close")]
    public async Task<IActionResult> CloseIssue(Guid id,CancellationToken ct)=>Result(await owner.IssueOperationAsync(id,true,ct));
    [HttpPost("{id:guid}/review-submissions/{submission:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid id,Guid submission,[FromBody]ApprovalUserWithdrawV2 input,CancellationToken ct)
    {
        var value=await owner.WithdrawAsync(id,submission,Request.Headers.IfMatch.ToString(),input,ct);
        return value.State=="CONFLICT"?StatusCode(409,new{code=0,data=value}):Result(value);
    }
    [HttpGet("review-withdrawal-operations/{id:guid}")]
    public async Task<IActionResult> WithdrawalOperation(Guid id,CancellationToken ct)=>Result(await owner.WithdrawalOperationAsync(id,false,ct));
    [HttpPost("review-withdrawal-operations/{id:guid}/close")]
    public async Task<IActionResult> CloseWithdrawal(Guid id,CancellationToken ct)=>Result(await owner.WithdrawalOperationAsync(id,true,ct));

}

/// <summary>Preserve safe native Scope failures at the iteration HTTP boundary.</summary>
public sealed class IterationReviewAvailabilityAttribute:Microsoft.AspNetCore.Mvc.Filters.ActionFilterAttribute
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
