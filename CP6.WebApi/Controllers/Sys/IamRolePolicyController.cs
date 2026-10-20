using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController,Route("api/iam/role-policies"),Authorize]
public sealed class IamRolePolicyController(IamRolePolicyAdministrationV1 policies,IamAdministrationV1 authority):ControllerBase
{
    // Mode-specific query/edit authorization is re-evaluated inside the common authentication gate.
    [HttpGet("{mode}/editor")]
    public Task<IActionResult> Editor(string mode,int? roleId=null)=>Run(async()=>await policies.ReadAsync(mode,roleId,Actor,HttpContext.RequestAborted));
    [HttpPut("{mode}/{roleId:int}")]
    public Task<IActionResult> Save(string mode,int roleId,IamRolePolicyCommandV1 command)=>Run(async()=>await policies.ChangeAsync(mode,roleId,command,Actor,HttpContext.RequestAborted));
    [HttpGet("menu-actions/{menuId:int}/editor")]
    public Task<IActionResult> MenuActions(int menuId)=>Run(async()=>await policies.ReadMenuActionsAsync(menuId,Actor,HttpContext.RequestAborted));
    [HttpPut("menu-actions/{menuId:int}")]
    public Task<IActionResult> SaveMenuActions(int menuId,IamMenuActionCommandV1 command)=>menuId!=command.MenuId
        ?Task.FromResult<IActionResult>(BadRequest(new{code="IAM_MENU_IMMUTABLE_ID"}))
        :Run(async()=>await policies.ChangeMenuActionsAsync(command,Actor,HttpContext.RequestAborted));
    [HttpGet("operations/{id:guid}")]
    public Task<IActionResult> Operation(Guid id,string kind)=>Resolve(id,kind,false);
    [HttpPost("operations/{id:guid}/close")]
    public Task<IActionResult> Close(Guid id,string kind)=>Resolve(id,kind,true);
    private Task<IActionResult> Resolve(Guid id,string kind,bool close)=>kind is not("role-policy-actions" or "role-policy-scopes" or "role-policy-fields" or "menu-actions")
        ?Task.FromResult<IActionResult>(BadRequest(new{code="IAM_OPERATION_KIND_INVALID"}))
        :Run(async()=>await authority.Resolve(id,kind,close,Actor,HttpContext.RequestAborted));
    private Task<IActionResult> Run(Func<Task<object>> action)=>IamAdministrationHttpV1.Execute(this,action);
    private Task<Guid> Actor(CancellationToken ct)=>IamAdministrationHttpV1.CurrentActor(HttpContext,ct);
}
