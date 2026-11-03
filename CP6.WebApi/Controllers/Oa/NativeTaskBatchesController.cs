using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController,Authorize,Route("api/oa/v2/task-batches")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class NativeTaskBatchesController(NativeTaskBatchOwnerV2 owner):ControllerBase
{
    [HttpPost("transfer-preview"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> Preview(CancellationToken ct)=>Run(async()=>await owner.PreviewAsync(await Raw(ct),ct));
    [HttpGet("transfer-previews/commands/{command}")]
    public Task<IActionResult> Original(string command,CancellationToken ct)=>Run(async()=>await owner.OriginalPreviewAsync(command,ct));
    [HttpPost("transfer")]
    public Task<IActionResult> Transfer(NativeTransferBatchConfirmV2 input,CancellationToken ct)=>Run(async()=>await owner.ExecuteAsync(input,ct));
    [HttpGet("{batch:guid}")]
    public Task<IActionResult> Report(Guid batch,CancellationToken ct)=>Run(async()=>await owner.ReportAsync(batch,ct));
    private async Task<byte[]> Raw(CancellationToken ct)
    {
        using var stream=new MemoryStream();var buffer=new byte[8192];int n;
        while((n=await Request.Body.ReadAsync(buffer,ct))!=0)
        {if(stream.Length+n>1024*1024)throw new ScopeAuthorizationExceptionV1("JSON_SIZE_LIMIT",400);await stream.WriteAsync(buffer.AsMemory(0,n),ct);}
        return stream.ToArray();
    }
    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        try
        {
            if(Request.Headers.ContainsKey("X-Acting-As"))throw new ScopeAuthorizationExceptionV1("MANAGEMENT_ACTUAL_ACTOR_REQUIRED",403);
            return Ok(new{code=0,data=await action()});
        }
        catch(ScopeAuthorizationExceptionV1 e){return StatusCode(e.Status,new{code=e.Message,retryMode=e.Status>=500?"READ_ORIGINAL":"NONE"});}
        catch(Exception e)when(e is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException or CurrentPermissionUnavailableException)
        {return StatusCode(503,new{code="BATCH_STORAGE_UNAVAILABLE",retryMode="READ_ORIGINAL"});}
    }
}
