using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Authorize, Route("api/oa/v2")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class PersonalFormController(PersonalFormOwnerV2 owner):ControllerBase
{
    [HttpGet("forms/{key}/initiation-context")]
    public Task<IActionResult> Context(string key,CancellationToken ct)=>Run(async()=>await owner.ContextAsync(key,ct));
    [HttpPost("forms/{key}/preview"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> FormPreview(string key,PersonalFormPreviewInputV2 input,CancellationToken ct)=>Run(async()=>await owner.PreviewAsync(key,input,ct));
    [HttpGet("forms/{key}/references/{field}")]
    public Task<IActionResult> References(string key,string field,[FromQuery]string? search=null,[FromQuery]Guid? draft=null,CancellationToken ct=default)=>Run(async()=>await owner.ReferencesAsync(key,field,search??"",draft,ct));
    [HttpPost("forms/{key}/drafts"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> Create(string key,CancellationToken ct)=>Run(async()=>await owner.CreateDraftAsync(key,await Raw(ct),ct));
    [HttpPost("forms/{key}/submissions"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> Submit(string key,CancellationToken ct)=>Run(async()=>await owner.SubmitDirectAsync(key,await Raw(ct),ct));
    [HttpGet("drafts")]
    public Task<IActionResult> List([FromQuery]int page=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>Run(async()=>await owner.DraftsAsync(page,pageSize,ct));
    [HttpGet("drafts/{id:guid}")]
    public Task<IActionResult> Draft(Guid id,CancellationToken ct)=>Run(async()=>await owner.DraftAsync(id,ct));
    [HttpPost("drafts/{id:guid}/preview"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> DraftPreview(Guid id,PersonalFormPreviewInputV2 input,CancellationToken ct)=>Run(async()=>await owner.DraftPreviewAsync(id,input,ct));
    [HttpPut("drafts/{id:guid}"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> Save(Guid id,CancellationToken ct)=>Run(async()=>await owner.SaveDraftAsync(id,await Raw(ct),ct));
    [HttpPost("drafts/{id:guid}/delete"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> Delete(Guid id,CancellationToken ct)=>Run(async()=>await owner.DeleteDraftAsync(id,await Raw(ct),ct));
    [HttpPost("drafts/{id:guid}/restore"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> Restore(Guid id,CancellationToken ct)=>Run(async()=>await owner.RestoreDraftAsync(id,await Raw(ct),ct));
    [HttpPost("drafts/{id:guid}/submit"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> SubmitDraft(Guid id,CancellationToken ct)=>Run(async()=>await owner.SubmitDraftAsync(id,await Raw(ct),ct));
    [HttpPost("drafts/{id:guid}/rebase-preview")]
    public Task<IActionResult> Preview(Guid id,PersonalFormRebasePreviewInputV2 input,CancellationToken ct)=>Run(async()=>await owner.RebasePreviewAsync(id,input,ct));
    [HttpPost("drafts/{id:guid}/rebase"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> Rebase(Guid id,CancellationToken ct)=>Run(async()=>await owner.RebaseDraftAsync(id,await Raw(ct),ct));
    [HttpGet("personal-forms/commands/{operation}/{resource}/{command}")]
    public Task<IActionResult> Original(string operation,string resource,string command,CancellationToken ct)=>Run(async()=>await owner.OriginalAsync(operation,resource,command,ct));
    [HttpPost("personal-forms/commands/{operation}/{resource}/{command}/close")]
    public Task<IActionResult> Close(string operation,string resource,string command,CancellationToken ct)=>Run(async()=>await owner.CloseAsync(operation,resource,command,ct));
    [HttpGet("personal-forms/tasks/{task:guid}/context")]
    public Task<IActionResult> TaskContext(Guid task,CancellationToken ct)=>Run(async()=>
    {
        var value=await owner.TaskContextAsync(task,ActingAs(),ct);Response.Headers.ETag="\""+value.Task.TaskRowVersion+"\"";return value;
    });
    [HttpPost("personal-forms/tasks/{task:guid}/decision")]
    public Task<IActionResult> Decide(Guid task,ApprovalTaskDecisionInputV2 input,CancellationToken ct)=>Run(async()=>await owner.DecideTaskAsync(task,input,ActingAs(),Request.Headers.IfMatch.FirstOrDefault(),ct));
    [HttpGet("personal-forms/tasks/{task:guid}/commands/{command:guid}")]
    public Task<IActionResult> TaskOriginal(Guid task,Guid command,CancellationToken ct)=>Run(async()=>await owner.TaskOriginalAsync(task,command,ct));
    [HttpGet("/api/iam/personal-form-policies/{key}")]
    public Task<IActionResult> Policy(string key,CancellationToken ct)=>Run(async()=>await owner.PolicyAsync(key,ct));
    [HttpPost("/api/iam/personal-form-policies/{key}")]
    public Task<IActionResult> Configure(string key,PersonalFormPolicyInputV2 input,CancellationToken ct)=>Run(async()=>await owner.ConfigureAsync(key,input,ct));
    private Guid? ActingAs()
    {
        var text=Request.Headers["X-Acting-As"].ToString();if(text.Length==0)return null;
        return Guid.TryParseExact(text,"D",out var id)&&id!=Guid.Empty?id:throw new ScopeAuthorizationExceptionV1("ACT_AS_INVALID",422);
    }
    private async Task<byte[]> Raw(CancellationToken ct)
    {
        using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await Request.Body.ReadAsync(buffer,ct))!=0)
        {
            if(bytes.Length+count>1024*1024)throw new ScopeAuthorizationExceptionV1("JSON_SIZE_LIMIT",400);
            await bytes.WriteAsync(buffer.AsMemory(0,count),ct);
        }
        return bytes.ToArray();
    }
    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        try{return Ok(new{code=0,data=await action()});}
        catch(ScopeAuthorizationExceptionV1 e)
        {return StatusCode(e.Status,new{code=e.Message,messageKey=e.Message,retryMode=e.Status is 503 or 504?"READ_ORIGINAL":e.Status==409?"REFRESH_AND_NEW_INTENT":"NONE"});}
        catch(Exception e)when(e is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException or CurrentPermissionUnavailableException)
        {return StatusCode(503,new{code="FORM_STORAGE_UNAVAILABLE",retryMode="READ_ORIGINAL"});}
    }
}
