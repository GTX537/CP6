using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;

[ApiController, Authorize, OrderOwnerResult]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ToolDispositionApprovalController(ToolDispositionApprovalOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value,int status=200)=>StatusCode(status,new{code=0,data=value});
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
}
