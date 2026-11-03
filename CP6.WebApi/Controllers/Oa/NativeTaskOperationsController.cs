using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController,Authorize,Route("api/oa/v2/tasks")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class NativeTaskOperationsController(ApprovalTaskDecisionOwnerV2 app,PersonalFormOwnerV2 forms):ControllerBase
{
    [HttpGet("{task:guid}/operations-context")]
    public Task<IActionResult> Context(Guid task,CancellationToken ct)=>Run(async()=>await app.IsNativeAsync(task,ct)?await app.OperationContextAsync(task,ActingAs(),ct):await forms.OperationContextAsync(task,ActingAs(),ct));
    [HttpPost("{task:guid}/sendback-preview")]
    public Task<IActionResult> Preview(Guid task,NativeSendBackTargetV2 input,CancellationToken ct)=>Run(async()=>
        await app.IsNativeAsync(task,ct)?await app.SendBackPreviewAsync(task,input,ActingAs(),ct):await forms.SendBackPreviewAsync(task,input,ActingAs(),ct));
    [HttpPost("{task:guid}/sendback"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> SendBack(Guid task,CancellationToken ct)=>Run(async()=>
        await app.IsNativeAsync(task,ct)?await app.SendBackTaskAsync(task,await Raw(ct),ActingAs(),ct):await forms.SendBackAsync(task,await Raw(ct),ActingAs(),ct));
    [HttpPost("{task:guid}/transfer"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> Transfer(Guid task,CancellationToken ct)=>Run(async()=>
        await app.IsNativeAsync(task,ct)?await app.TransferTaskAsync(task,await Raw(ct),ActingAs(),ct):await forms.TransferTaskAsync(task,await Raw(ct),ActingAs(),ct));
    [HttpPost("{task:guid}/addsign"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> AddSign(Guid task,CancellationToken ct)=>Run(async()=>
        await app.IsNativeAsync(task,ct)?await app.AddSignTaskAsync(task,await Raw(ct),ActingAs(),ct):await forms.AddSignTaskAsync(task,await Raw(ct),ActingAs(),ct));
    [HttpGet("operations/{operation}/{resource:guid}/{command}")]
    public Task<IActionResult> Original(string operation,Guid resource,string command,CancellationToken ct)=>Run(async()=>
        await AppResource(operation,resource,ct)?await app.OriginalOperationAsync(operation,resource,command,ct):await forms.OriginalAsync(operation,resource.ToString("D"),command,ct));
    [HttpPost("operations/{operation}/{resource:guid}/{command}/close")]
    public Task<IActionResult> Close(string operation,Guid resource,string command,CancellationToken ct)=>Run(async()=>
        await AppResource(operation,resource,ct)?await app.CloseOperationAsync(operation,resource,command,ct):await forms.CloseAsync(operation,resource.ToString("D"),command,ct));
    private Task<bool> AppResource(string operation,Guid resource,CancellationToken ct)=>operation=="OA_APPROVER_RESOLVE"?app.IsAppResolutionAsync(resource,ct):app.IsNativeAsync(resource,ct);
    private Guid? ActingAs()
    {
        var text=Request.Headers["X-Acting-As"].ToString();if(text.Length==0)return null;
        return Guid.TryParseExact(text,"D",out var id)&&id!=Guid.Empty?id:throw new ScopeAuthorizationExceptionV1("ACT_AS_INVALID",422);
    }
    private async Task<byte[]> Raw(CancellationToken ct)
    {
        using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await Request.Body.ReadAsync(buffer,ct))!=0)
        {if(bytes.Length+count>1024*1024)throw new ScopeAuthorizationExceptionV1("JSON_SIZE_LIMIT",400);await bytes.WriteAsync(buffer.AsMemory(0,count),ct);}
        return bytes.ToArray();
    }
    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        try{return Ok(new{code=0,data=await action()});}
        catch(ScopeAuthorizationExceptionV1 e){return StatusCode(e.Status,new{code=e.Message,messageKey=e.Message,retryMode=e.Status is 503 or 504?"READ_ORIGINAL":e.Status==409?"REFRESH_AND_NEW_INTENT":"NONE"});}
        catch(Exception e)when(e is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException or CurrentPermissionUnavailableException)
        {return StatusCode(503,new{code="TASK_STORAGE_UNAVAILABLE",retryMode="READ_ORIGINAL"});}
    }
}
