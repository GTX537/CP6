using CP6.Core.Services.Plm;
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
