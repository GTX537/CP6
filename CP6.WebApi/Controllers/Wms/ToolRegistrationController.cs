using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;
[ApiController,Authorize,Route("api/wms/specials/v1/tools"),OrderOwnerResult]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ToolRegistrationController(ToolRegistrationOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value)=>Ok(new{code=0,data=value});
    [HttpGet("creation-context")]
    public async Task<IActionResult> Context(CancellationToken ct)=>Result(await owner.CreationContextAsync(ct));
    [HttpGet("sites")]
    public async Task<IActionResult> Sites([FromQuery]string? search,CancellationToken ct)=>Result(await owner.SitesAsync(search,ct));
    [HttpGet("{id:guid}/registration")]
    public async Task<IActionResult> Read(Guid id,CancellationToken ct)=>Result(await owner.ReadAsync(id,ct));
    [HttpPost,RequestSizeLimit(16384)]
    public async Task<IActionResult> Create([FromBody]ToolRegistrationCommandV1 command,CancellationToken ct)
    {if(command.Kind!="CREATE")return BadRequest();return Result(await owner.CommandAsync(command,Request.Headers.IfMatch.ToString(),ct));}
    [HttpPut("{id:guid}/registration"),RequestSizeLimit(16384)]
    public async Task<IActionResult> Bind(Guid id,[FromBody]ToolRegistrationCommandV1 command,CancellationToken ct)
    {if(command.Kind!="BIND_LEGACY"||id!=command.ObjectId)return BadRequest();return Result(await owner.CommandAsync(command,Request.Headers.IfMatch.ToString(),ct));}
    [HttpGet("registration-operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("registration-operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
}
