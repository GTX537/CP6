using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>角色授权配置 REST —— PUB 章02。/api/pub/role-perm</summary>
[ApiController]
[Route("api/pub/role-perm")]
[Authorize]
public class RolePermController : ControllerBase
{
    private readonly IRolePermService _svc;
    public RolePermController(IRolePermService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>取某菜单的操作点定义</summary>
    [HttpGet("menu-action/{menuId:int}")]
    public async Task<IActionResult> GetMenuActions(int menuId) => Ok2(await _svc.GetMenuActionsAsync(menuId));

    /// <summary>取全部菜单的操作点定义（配置页一次性加载）</summary>
    [HttpGet("menu-action/all")]
    public async Task<IActionResult> GetAllMenuActions() => Ok2(await _svc.GetAllMenuActionsAsync());

    /// <summary>维护某菜单的操作点</summary>
    [HttpPut("menu-action/{menuId:int}")]
    public async Task<IActionResult> SaveMenuActions(int menuId, [FromBody] List<MenuActionDto> actions)
    {
        await _svc.SaveMenuActionsAsync(menuId, actions, CurrentUser);
        return Ok2();
    }

    /// <summary>当前用户的全部操作键（前端 v-permission 数据源）</summary>
    [HttpGet("my-actions")]
    public async Task<IActionResult> MyActions() => Ok2(await _svc.MyActionsAsync());

    /// <summary>取某角色的功能权限（菜单 + 操作点）</summary>
    [HttpGet("{roleId:int}")]
    public async Task<IActionResult> GetRolePerm(int roleId) => Ok2(await _svc.GetRolePermAsync(roleId));

    /// <summary>保存某角色的功能权限</summary>
    [HttpPut("{roleId:int}")]
    public async Task<IActionResult> SaveRolePerm(int roleId, [FromBody] RolePermDto dto)
    {
        try { await _svc.SaveRolePermAsync(roleId, dto, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ───── 章03 数据权限配置 ─────

    /// <summary>已注册的数据权限资源</summary>
    [HttpGet("data-scope/resources")]
    public IActionResult DataScopeResources() => Ok2(_svc.GetDataScopeResources());

    /// <summary>取某角色的数据范围配置</summary>
    [HttpGet("data-scope/{roleId:int}")]
    public async Task<IActionResult> GetRoleDataScopes(int roleId) => Ok2(await _svc.GetRoleDataScopesAsync(roleId));

    /// <summary>保存某角色的数据范围配置</summary>
    [HttpPut("data-scope/{roleId:int}")]
    public async Task<IActionResult> SaveRoleDataScopes(int roleId, [FromBody] List<RoleDataScopeDto> items)
    {
        try { await _svc.SaveRoleDataScopesAsync(roleId, items, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ───── 章04 字段权限配置 ─────

    /// <summary>已注册的字段权限资源及字段</summary>
    [HttpGet("field-perm/resources")]
    public IActionResult FieldResources() => Ok2(_svc.GetFieldResources());

    /// <summary>取某角色的字段权限配置</summary>
    [HttpGet("field-perm/{roleId:int}")]
    public async Task<IActionResult> GetRoleFieldPerms(int roleId) => Ok2(await _svc.GetRoleFieldPermsAsync(roleId));

    /// <summary>保存某角色的字段权限配置</summary>
    [HttpPut("field-perm/{roleId:int}")]
    public async Task<IActionResult> SaveRoleFieldPerms(int roleId, [FromBody] List<RoleFieldPermDto> items)
    {
        try { await _svc.SaveRoleFieldPermsAsync(roleId, items, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>当前用户对某资源的只读字段（前端置灰输入）</summary>
    [HttpGet("my-readonly/{resourceKey}")]
    public async Task<IActionResult> MyReadonly(string resourceKey) => Ok2(await _svc.MyReadonlyAsync(resourceKey));
}
