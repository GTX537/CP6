using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;
[ApiController,Authorize,Route("api/oa/form-field-dispositions/v1")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class FormFieldDispositionController(FormFieldDispositionOwnerV1 owner):ControllerBase
{
    [HttpPost("requests"),RequestSizeLimit(8192)]
    public Task<IActionResult> CreateRequest(FormDispositionRequestV1 input,CancellationToken ct)=>Run(async()=>await owner.RequestAsync(input,ct));
    [HttpGet("requests/{id:guid}")]
    public Task<IActionResult> Read(Guid id,CancellationToken ct)=>Run(async()=>await owner.ReadAsync(id,ct));
    [HttpPost("requests/{id:guid}/{decision:regex(^(approve|reject|retain|revoke)$)}"),RequestSizeLimit(8192)]
    public Task<IActionResult> Decide(Guid id,string decision,FormDispositionCommandV1 input,CancellationToken ct)=>Run(async()=>await owner.DecideAsync(id,decision,input,ct));
    [HttpPost("requests/{id:guid}/hold"),RequestSizeLimit(8192)]
    public Task<IActionResult> Hold(Guid id,FormDispositionHoldCommandV1 input,CancellationToken ct)=>Run(async()=>await owner.HoldAsync(id,input,ct));
    [HttpGet("operations/{id:guid}")]
    public Task<IActionResult> Original(Guid id,CancellationToken ct)=>Run(async()=>await owner.OperationAsync(id,false,ct));
    [HttpPost("operations/{id:guid}/close")]
    public Task<IActionResult> Close(Guid id,CancellationToken ct)=>Run(async()=>await owner.OperationAsync(id,true,ct));
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try{return Ok(new{code=0,data=await action()});}
        catch(ScopeAuthorizationExceptionV1 e){return StatusCode(e.Status,new{code=e.Message,retryMode=e.Status is 503 or 504?"READ_ORIGINAL":"NONE"});}
        catch(Exception e)when(e is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException or CurrentPermissionUnavailableException)
        {return StatusCode(503,new{code="FORM_DISPOSITION_STORAGE_UNAVAILABLE",retryMode="READ_ORIGINAL"});}
    }
}
