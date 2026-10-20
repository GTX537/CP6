using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController,Route("api/iam/roles"),Authorize]
public sealed class IamRoleController(IamRoleAdministrationV1 roles,IamAdministrationV1 authority):ControllerBase
{
    [HttpGet("editor"),RequirePermission("role","query")]
    public Task<IActionResult> Editor()=>Run(async()=>await roles.ReadAsync(Actor,HttpContext.RequestAborted));
    [HttpGet("{id:int}/references"),RequirePermission("role","query")]
    public Task<IActionResult> References(int id)=>Run(async()=>await roles.ReferencesAsync(id,Actor,HttpContext.RequestAborted));
    [HttpPost,RequirePermission("role","add")]
    public Task<IActionResult> Create(IamRoleCommandV1 command)=>Change("role-create",command);
    [HttpPut("{id:int}"),RequirePermission("role","edit")]
    public Task<IActionResult> Update(int id,IamRoleCommandV1 command)=>id!=command.RoleId
        ?Task.FromResult<IActionResult>(BadRequest(new{code="IAM_ROLE_IMMUTABLE_ID"})):Change("role-update",command);
    [HttpDelete("{id:int}"),RequirePermission("role","delete")]
    public Task<IActionResult> Delete(int id,IamRoleCommandV1 command)=>id!=command.RoleId
        ?Task.FromResult<IActionResult>(BadRequest(new{code="IAM_ROLE_IMMUTABLE_ID"})):Change("role-delete",command);
    [HttpGet("operations/{id:guid}")]
    public Task<IActionResult> Operation(Guid id,string kind)=>Resolve(id,kind,false);
    [HttpPost("operations/{id:guid}/close")]
    public Task<IActionResult> Close(Guid id,string kind)=>Resolve(id,kind,true);
    private Task<IActionResult> Resolve(Guid id,string kind,bool close)=>kind is not("role-create" or "role-update" or "role-delete")
        ?Task.FromResult<IActionResult>(BadRequest(new{code="IAM_OPERATION_KIND_INVALID"}))
        :Run(async()=>await authority.Resolve(id,kind,close,Actor,HttpContext.RequestAborted));
    private Task<IActionResult> Change(string kind,IamRoleCommandV1 command)=>Run(async()=>await roles.ChangeAsync(kind,command,Actor,HttpContext.RequestAborted));
    private Task<IActionResult> Run(Func<Task<object>> action)=>IamAdministrationHttpV1.Execute(this,action);
    private Task<Guid> Actor(CancellationToken ct)=>IamAdministrationHttpV1.CurrentActor(HttpContext,ct);
}
