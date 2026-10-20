using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CP6.WebApi.Services;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>用户-角色 分配 REST —— PUB 章01。/api/pub/user-role</summary>
[ApiController]
[Route("api/pub/user-role")]
[Authorize]
public class UserRoleController : ControllerBase
{
    private readonly IUserRoleService _svc;
    private readonly IamUserAdministrationV1? _admin;
    public UserRoleController(IUserRoleService svc,IamUserAdministrationV1? admin=null) { _svc=svc;_admin=admin; }

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>取某用户的角色集合 + 主角色</summary>
    [HttpGet("{userId}")]
    [RequirePermission("user", "query")]
    public async Task<IActionResult> Get(Guid userId)
    {
        if(_admin?.IsPostgreSql!=true)return Ok2(await _svc.GetAsync(userId));
        return await IamAdministrationHttpV1.Execute(this,async()=>
        {
            var view=await _admin.ReadAsync(userId,ct=>IamAdministrationHttpV1.CurrentActor(HttpContext,ct),HttpContext.RequestAborted);
            return new {code=0,message="OK",data=new UserRolesDto {RoleIds=view.User!.RoleIds.ToList(),PrimaryRoleId=view.User.RoleId,
                ExpectedVersion=view.User.Version,ExpectedReferencesVersion=view.ReferencesVersion}};
        });
    }

    /// <summary>保存某用户的角色集合 + 主角色</summary>
    [HttpPut("{userId}")]
    [RequirePermission("user", "edit")]
    public async Task<IActionResult> Save(Guid userId, [FromBody] UserRolesDto d)
    {
        if(_admin?.IsPostgreSql==true)return await IamAdministrationHttpV1.Execute(this,async()=>
            (object)await _admin.ChangeAsync("user-roles",new IamUserCommandV1{Id=userId,OperationId=d.OperationId,ExpectedVersion=d.ExpectedVersion,
                ExpectedReferencesVersion=d.ExpectedReferencesVersion,Reason=d.Reason,RoleId=d.PrimaryRoleId,RoleIds=d.RoleIds?.ToArray()},
                ct=>IamAdministrationHttpV1.CurrentActor(HttpContext,ct),HttpContext.RequestAborted));
        try { await _svc.SaveAsync(userId, d.RoleIds, d.PrimaryRoleId, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>历史数据迁移：现有 Sys_User.RoleId → Sys_UserRole（幂等）</summary>
    [HttpPost("migrate")]
    [RequirePermission("user", "edit")]
    public async Task<IActionResult> Migrate()
    {
        if(_admin?.IsPostgreSql==true)return Conflict(new{code="IAM_CONTROLLED_MIGRATION_REQUIRED"});
        return Ok2(new { inserted = await _svc.MigrateAsync() });
    }
}
