using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController,Authorize,Route("api/oa/v2/tasks")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class NativeTaskOperationsController(ApprovalTaskDecisionOwnerV2 app,PersonalFormOwnerV2 forms):ControllerBase
{
    [HttpGet("{task:guid}/action-context")]
    public Task<IActionResult> ActionContext(Guid task,CancellationToken ct)=>Run(async()=>
        await app.IsNativeAsync(task,ct)?await app.ActionContextAsync(task,ActingAs(),ct):await forms.ActionContextAsync(task,ActingAs(),ct));
    [HttpPost("{task:guid}/decisions"),RequestSizeLimit(1024*1024)]
    public async Task<IActionResult> Decide(Guid task,CancellationToken ct)
    {
        byte[]? raw=null;bool isApp=false;
        try
        {
            isApp=await app.IsNativeAsync(task,ct);raw=await Raw(ct);
            object result=isApp?await app.DecideWfcAsync(task,raw,ActingAs(),ct):await forms.DecideSfsTaskAsync(task,raw,ActingAs(),ct);
            return Ok(new{code=0,data=result});
        }
        catch(ScopeAuthorizationExceptionV1 e){return DecisionError(e,raw,isApp);}
        catch(CP6.Core.Services.Erp.OrderPriceCorrectionException e){return DecisionError(new(e.Message,e.Status),raw,isApp);}
        catch(Exception e)when(e is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException or CurrentPermissionUnavailableException)
        {return DecisionError(new("TASK_STORAGE_UNAVAILABLE",503),raw,isApp);}
    }
    private IActionResult DecisionError(ScopeAuthorizationExceptionV1 e,byte[]? raw,bool app)
    {
        string? query=null;
        if(app&&raw!=null)
            try{using var doc=System.Text.Json.JsonDocument.Parse(raw);if(doc.RootElement.ValueKind==System.Text.Json.JsonValueKind.Object&&doc.RootElement.TryGetProperty("commandId",out var id)&&id.ValueKind==System.Text.Json.JsonValueKind.String&&id.TryGetGuid(out var command))query="/api/oa/tasks/decision-commands/"+command;}
            catch(System.Text.Json.JsonException){/* Shape errors have no trusted original identifier. */}
        return StatusCode(e.Status,new{code=e.Message,messageKey=e.Message,resultQueryRef=query,retryMode=e.Status is 503 or 504?"READ_ORIGINAL":e.Status is 409 or 412 or 428?"REFRESH_AND_NEW_INTENT":"NONE"});
    }
    [HttpGet("{task:guid}/decision-commands/{command:guid}")]
    public Task<IActionResult> DecisionOriginal(Guid task,Guid command,CancellationToken ct)=>Run(async()=>await app.DecisionOperationAsync(task,command,ct));
    [HttpPost("{task:guid}/decision-commands/{command:guid}/close")]
    public Task<IActionResult> DecisionClose(Guid task,Guid command,CancellationToken ct)=>Run(async()=>await app.CloseAsync(task,command,ct));
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
        catch(CP6.Core.Services.Erp.OrderPriceCorrectionException e){return StatusCode(e.Status,new{code=e.Message,messageKey=e.Message,retryMode="NONE"});}
        catch(Exception e)when(e is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException or CurrentPermissionUnavailableException)
        {return StatusCode(503,new{code="TASK_STORAGE_UNAVAILABLE",retryMode="READ_ORIGINAL"});}
    }
}
