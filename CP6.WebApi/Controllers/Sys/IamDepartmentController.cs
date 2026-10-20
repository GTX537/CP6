using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController,Route("api/iam/departments"),Authorize]
public sealed class IamDepartmentController(IamDepartmentAdministrationV1 departments,IamAdministrationV1 authority,
    IamOrderDepartmentMappingV1 orderMappings):ControllerBase
{
    [HttpGet("order-mappings"),RequirePermission("pub-dept","query")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> OrderMappings()=>Run(async()=>await orderMappings.ReadAsync(Actor,HttpContext.RequestAborted));
    [HttpPost("order-mappings"),RequirePermission("pub-dept","edit")]
    public Task<IActionResult> ChangeOrderMapping(IamOrderDepartmentCommandV1 command)
        =>Run(async()=>await orderMappings.ChangeAsync(command,Actor,HttpContext.RequestAborted));
    [HttpGet("editor"),RequirePermission("pub-dept","query")]
    public Task<IActionResult> Editor()=>Run(async()=>await departments.ReadAsync(Actor,HttpContext.RequestAborted));
    [HttpGet("{id:guid}/preview"),RequirePermission("pub-dept","edit")]
    public Task<IActionResult> Preview(Guid id,Guid? parentId=null)=>Run(async()=>await departments.PreviewAsync(id,parentId,Actor,HttpContext.RequestAborted));
    [HttpGet("{id:guid}/references"),RequirePermission("pub-dept","query")]
    public Task<IActionResult> References(Guid id)=>Run(async()=>await departments.ReferencesAsync(id,Actor,HttpContext.RequestAborted));
    [HttpPost,RequirePermission("pub-dept","add")]
    public Task<IActionResult> Create(DeptDto dto)=>Change("dept-create",Map(dto,null));
    [HttpPut("{id:guid}"),RequirePermission("pub-dept","edit")]
    public Task<IActionResult> Update(Guid id,DeptDto dto)=>Change("dept-update",Map(dto,id));
    [HttpPost("{id:guid}/move"),RequirePermission("pub-dept","edit")]
    public Task<IActionResult> Move(Guid id,IamDepartmentCommandV1 command)=>Change("dept-move",command with{Id=id});
    [HttpPut("{id:guid}/leader"),RequirePermission("pub-dept","edit")]
    public Task<IActionResult> Leader(Guid id,IamDepartmentCommandV1 command)=>Change("dept-leader",command with{Id=id});
    [HttpDelete("{id:guid}"),RequirePermission("pub-dept","delete")]
    public Task<IActionResult> Delete(Guid id,IamDepartmentCommandV1 command)=>Change("dept-delete",command with{Id=id});
    [HttpGet("operations/{id:guid}")]
    public Task<IActionResult> Operation(Guid id,string kind)=>Run(async()=>await authority.Resolve(id,kind,false,Actor,HttpContext.RequestAborted));
    [HttpPost("operations/{id:guid}/close")]
    public Task<IActionResult> Close(Guid id,string kind)=>Run(async()=>await authority.Resolve(id,kind,true,Actor,HttpContext.RequestAborted));
    internal static IamDepartmentCommandV1 Map(DeptDto d,Guid? id)=>new(){Id=id,OperationId=d.OperationId,ExpectedVersion=d.ExpectedVersion,
        ExpectedReferencesVersion=d.ExpectedReferencesVersion,ExpectedTreeVersion=d.ExpectedTreeVersion,Reason=d.Reason,
        DeptCode=d.DeptCode,DeptName=d.DeptName,ParentId=d.ParentId,LeaderId=d.LeaderId,Sort=d.Sort,Enable=d.Enable};
    private Task<IActionResult> Change(string kind,IamDepartmentCommandV1 command)=>Run(async()=>await departments.ChangeAsync(kind,command,Actor,HttpContext.RequestAborted));
    private Task<IActionResult> Run(Func<Task<object>> action)=>IamAdministrationHttpV1.Execute(this,action);
    private Task<Guid> Actor(CancellationToken ct)=>IamAdministrationHttpV1.CurrentActor(HttpContext,ct);
}
