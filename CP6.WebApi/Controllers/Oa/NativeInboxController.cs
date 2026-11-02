using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController,Authorize,Route("api/oa/v2/inbox")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class NativeInboxController(NativeInboxOwnerV2 owner):ControllerBase
{
    [HttpGet("counts")]
    public Task<IActionResult> Counts(CancellationToken ct)=>Run(async()=>await owner.CountsAsync(ActingAs(),ct));
    [HttpGet("{kind}")]
    public Task<IActionResult> List(string kind,[FromQuery]int page=1,[FromQuery]int pageSize=20,[FromQuery]string rowMode="expanded",[FromQuery]string search="",CancellationToken ct=default)
        =>Run(async()=>await owner.ListAsync(kind,page,pageSize,rowMode,search,ActingAs(),ct));
    [HttpGet("instances/{id:guid}")]
    public Task<IActionResult> Detail(Guid id,CancellationToken ct)=>Run(async()=>await owner.DetailAsync(id,ActingAs(),ct));
    [HttpPost("tasks/{id:guid}/read"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> TaskRead(Guid id,CancellationToken ct)=>Run(async()=>await owner.MarkReadAsync("TASK",id,await Raw(ct),ActingAs(),ct));
    [HttpPost("cc/{id:guid}/read"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> CcRead(Guid id,CancellationToken ct)=>Run(async()=>await owner.MarkReadAsync("CC",id,await Raw(ct),ActingAs(),ct));
    [HttpGet("read-commands/{kind}/{target:guid}/{command}")]
    public Task<IActionResult> Original(string kind,Guid target,string command,CancellationToken ct)=>Run(async()=>await owner.ReadOriginalAsync(kind,target,command,ActingAs(),ct));
    [HttpPost("read-commands/{kind}/{target:guid}/{command}/close")]
    public Task<IActionResult> Close(string kind,Guid target,string command,CancellationToken ct)=>Run(async()=>await owner.CloseReadAsync(kind,target,command,ActingAs(),ct));
    private Guid? ActingAs()
    {
        var value=Request.Headers["X-Acting-As"].ToString();if(value.Length==0)return null;
        return Guid.TryParseExact(value,"D",out var id)&&id!=Guid.Empty?id:throw new ScopeAuthorizationExceptionV1("ACT_AS_INVALID",422);
    }
    private async Task<byte[]> Raw(CancellationToken ct)
    {
        using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await Request.Body.ReadAsync(buffer,ct))!=0)
        {if(bytes.Length+count>1024*1024)throw new ScopeAuthorizationExceptionV1("JSON_SIZE_LIMIT",400);await bytes.WriteAsync(buffer.AsMemory(0,count),ct);}
        return bytes.ToArray();
    }
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try{return Ok(new{code=0,data=await action()});}
        catch(ScopeAuthorizationExceptionV1 e){return StatusCode(e.Status,new{code=e.Message,messageKey=e.Message,retryMode=e.Status is 503 or 504?"READ_ORIGINAL":"NONE"});}
        catch(Exception e)when(e is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException or CurrentPermissionUnavailableException)
        {return StatusCode(503,new{code="INBOX_STORAGE_UNAVAILABLE",retryMode="READ_ORIGINAL"});}
    }
}
