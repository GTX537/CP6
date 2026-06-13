using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>用户-角色 分配 REST —— PUB 章01。/api/pub/user-role</summary>
[ApiController]
[Route("api/pub/user-role")]
[Authorize]
public class UserRoleController : ControllerBase
{
    private readonly IUserRoleService _svc;
    public UserRoleController(IUserRoleService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>取某用户的角色集合 + 主角色</summary>
    [HttpGet("{userId}")]
    public async Task<IActionResult> Get(Guid userId) => Ok2(await _svc.GetAsync(userId));

    /// <summary>保存某用户的角色集合 + 主角色</summary>
    [HttpPut("{userId}")]
    public async Task<IActionResult> Save(Guid userId, [FromBody] UserRolesDto d)
    {
        try { await _svc.SaveAsync(userId, d.RoleIds, d.PrimaryRoleId, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>历史数据迁移：现有 Sys_User.RoleId → Sys_UserRole（幂等）</summary>
    [HttpPost("migrate")]
    public async Task<IActionResult> Migrate() => Ok2(new { inserted = await _svc.MigrateAsync() });
}
