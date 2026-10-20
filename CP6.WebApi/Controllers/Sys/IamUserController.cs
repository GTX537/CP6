using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController,Route("api/iam/users"),Authorize]
public sealed class IamUserController(IamUserAdministrationV1 users,IamAdministrationV1 authority):ControllerBase
{
    [HttpGet("editor"),RequirePermission("user","query")]
    public Task<IActionResult> Editor(Guid? id=null)=>IamAdministrationHttpV1.Execute(this,async ()=>(object)await users.ReadAsync(id,Actor,HttpContext.RequestAborted));
    [HttpPost,RequirePermission("user","add")]
    public Task<IActionResult> Create(UserCreateRequest command)=>Change("user-create",new IamUserCommandV1
    {OperationId=command.OperationId,ExpectedVersion=command.ExpectedVersion,ExpectedReferencesVersion=command.ExpectedReferencesVersion,
        Reason=command.Reason,UserName=command.UserName,NickName=command.NickName,Password=command.Password,RoleId=command.RoleId,
        RoleIds=command.RoleIds,Enable=command.Enable,DeptId=command.DeptId,ManagerId=command.ManagerId,Email=command.Email});
    [HttpPut("{id:guid}"),RequirePermission("user","edit")]
    public Task<IActionResult> Update(Guid id,IamUserCommandV1 command)=>Change("user-update",command with {Id=id});
    [HttpPut("{id:guid}/roles"),RequirePermission("user","edit")]
    public Task<IActionResult> Roles(Guid id,IamUserCommandV1 command)=>Change("user-roles",command with {Id=id});
    [HttpPut("{id:guid}/organization"),RequirePermission("user","edit")]
    public Task<IActionResult> Organization(Guid id,IamUserCommandV1 command)=>Change("user-org",command with {Id=id});
    [HttpGet("operations/{id:guid}")]
    public Task<IActionResult> Operation(Guid id,string kind)=>Resolve(id,kind,false);
    [HttpPost("operations/{id:guid}/close")]
    public Task<IActionResult> Close(Guid id,string kind)=>Resolve(id,kind,true);
    private Task<IActionResult> Change(string kind,IamUserCommandV1 command)=>IamAdministrationHttpV1.Execute(this,async ()=>(object)await users.ChangeAsync(kind,command,Actor,HttpContext.RequestAborted));
    private Task<IActionResult> Resolve(Guid id,string kind,bool close)=>IamAdministrationHttpV1.Execute(this,async ()=>(object)await authority.Resolve(id,kind,close,Actor,HttpContext.RequestAborted));
    private Task<Guid> Actor(CancellationToken ct)=>IamAdministrationHttpV1.CurrentActor(HttpContext,ct);
}
