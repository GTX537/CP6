using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>部门（组织树）REST —— PUB 章00。/api/pub/dept</summary>
[ApiController]
[Route("api/pub/dept")]
[Authorize]
public class DeptController : ControllerBase
{
    private readonly IDeptService _svc;
    private readonly IamDepartmentAdministrationV1? _iam;
    public DeptController(IDeptService svc,IamDepartmentAdministrationV1? iam=null) { _svc=svc;_iam=iam; }

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("tree"),RequirePermission("pub-dept","query")]
    public async Task<IActionResult> Tree() => Ok2(await _svc.TreeAsync());

    [HttpPost]
    [RequirePermission("pub-dept", "add")]
    public async Task<IActionResult> Create([FromBody] DeptDto d)
    {
        if(_iam?.IsPostgreSql==true)return await Change("dept-create",IamDepartmentController.Map(d,null));
        try { return Ok2(new { id = await _svc.CreateAsync(d, d.ParentId, CurrentUser) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPut("{id}")]
    [RequirePermission("pub-dept", "edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] DeptDto d)
    {
        if(_iam?.IsPostgreSql==true)return await Change("dept-update",IamDepartmentController.Map(d,id));
        try { await _svc.UpdateAsync(id, d, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpDelete("{id}")]
    [RequirePermission("pub-dept", "delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if(_iam?.IsPostgreSql==true)return Conflict(new{code="IAM_DEPARTMENT_RETENTION_REVIEW_REQUIRED"});
        try { await _svc.DeleteAsync(id); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("{id}/move")]
    [RequirePermission("pub-dept", "edit")]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveReq r)
    {
        if(_iam?.IsPostgreSql==true)return await Change("dept-move",new(){Id=id,ParentId=r.NewParentId,OperationId=r.OperationId,
            ExpectedVersion=r.ExpectedVersion,ExpectedReferencesVersion=r.ExpectedReferencesVersion,ExpectedTreeVersion=r.ExpectedTreeVersion,PreviewVersion=r.PreviewVersion,Reason=r.Reason});
        try { await _svc.MoveAsync(id, r.NewParentId, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPut("{id}/leader")]
    [RequirePermission("pub-dept", "edit")]
    public async Task<IActionResult> Leader(Guid id, [FromBody] LeaderReq r)
    {
        if(_iam?.IsPostgreSql==true)return await Change("dept-leader",new(){Id=id,LeaderId=r.LeaderId,OperationId=r.OperationId,
            ExpectedVersion=r.ExpectedVersion,ExpectedReferencesVersion=r.ExpectedReferencesVersion,ExpectedTreeVersion=r.ExpectedTreeVersion,Reason=r.Reason});
        try { await _svc.SetLeaderAsync(id, r.LeaderId, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    private Task<IActionResult> Change(string kind,IamDepartmentCommandV1 command)=>IamAdministrationHttpV1.Execute(this,async()=>
        (object)await _iam!.ChangeAsync(kind,command,ct=>IamAdministrationHttpV1.CurrentActor(HttpContext,ct),HttpContext.RequestAborted));
    public record MoveReq(Guid? NewParentId):IamCommandV1 { public string? ExpectedTreeVersion {get;init;} public string? PreviewVersion {get;init;} }
    public record LeaderReq(Guid? LeaderId):IamCommandV1 { public string? ExpectedTreeVersion {get;init;} }
}
