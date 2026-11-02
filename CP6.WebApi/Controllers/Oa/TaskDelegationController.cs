using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController,Authorize,Route("api/oa/v2/delegations")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class TaskDelegationController(TaskDelegationOwnerV2 owner):ControllerBase
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct)=>Run(async()=>await owner.ListAsync(ActingAs(),ct));
    [HttpGet("received")]
    public Task<IActionResult> Received(CancellationToken ct)=>Run(async()=>await owner.ReceivedAsync(ct));
    [HttpGet("targets")]
    public Task<IActionResult> Targets([FromQuery]string search,CancellationToken ct)=>Run(async()=>await owner.TargetsAsync(search,ActingAs(),ct));
    [HttpGet("{id:guid}/history")]
    public Task<IActionResult> History(Guid id,CancellationToken ct)=>Run(async()=>await owner.HistoryAsync(id,ActingAs(),ct));
    [HttpPost,RequestSizeLimit(1048576)]
    public Task<IActionResult> Create(CancellationToken ct)=>Run(async()=>await owner.WriteAsync("CREATE",null,await Raw(ct),ActingAs(),ct));
    [HttpPost("{id:guid}/revise"),RequestSizeLimit(1048576)]
    public Task<IActionResult> Revise(Guid id,CancellationToken ct)=>Run(async()=>await owner.WriteAsync("REVISE",id,await Raw(ct),ActingAs(),ct));
    [HttpPost("{id:guid}/revoke"),RequestSizeLimit(1048576)]
    public Task<IActionResult> Revoke(Guid id,CancellationToken ct)=>Run(async()=>await owner.WriteAsync("REVOKE",id,await Raw(ct),ActingAs(),ct));
    [HttpGet("commands/{operation}/{resource}/{key}")]
    public Task<IActionResult> Original(string operation,string resource,string key,CancellationToken ct)
        =>Run(async()=>await owner.OriginalAsync(operation,resource,key,false,ActingAs(),ct));
    [HttpPost("commands/{operation}/{resource}/{key}/close")]
    public Task<IActionResult> Close(string operation,string resource,string key,CancellationToken ct)
        =>Run(async()=>await owner.OriginalAsync(operation,resource,key,true,ActingAs(),ct));
    private Guid? ActingAs()
    {
        var raw=Request.Headers["X-Acting-As"].ToString();if(raw.Length==0)return null;
        return Guid.TryParseExact(raw,"D",out var id)&&id!=Guid.Empty?id:throw new ScopeAuthorizationExceptionV1("ACT_AS_INVALID",422);
    }
    private async Task<byte[]> Raw(CancellationToken ct)
    {
        using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await Request.Body.ReadAsync(buffer,ct))!=0)
        {if(bytes.Length+count>1048576)throw new ScopeAuthorizationExceptionV1("JSON_SIZE_LIMIT",400);await bytes.WriteAsync(buffer.AsMemory(0,count),ct);}
        return bytes.ToArray();
    }
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try{return Ok(new{code=0,data=await action()});}
        catch(ScopeAuthorizationExceptionV1 e){return StatusCode(e.Status,new{code=e.Message,retryMode=e.Status is 503 or 504?"READ_ORIGINAL":"NONE"});}
        catch(Exception e)when(e is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException or CurrentPermissionUnavailableException)
        {return StatusCode(503,new{code="GRANT_STORAGE_UNAVAILABLE",retryMode="READ_ORIGINAL"});}
    }
}
