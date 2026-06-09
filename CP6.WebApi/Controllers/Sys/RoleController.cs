using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RoleController : ControllerBase
{
    private readonly CP6Context _context;

    public RoleController(CP6Context context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetList(int page = 1, int pageSize = 10, string? keyword = null)
    {
        var query = _context.Sys_Roles.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(r => r.RoleName.Contains(keyword));

        var total = await query.CountAsync();
        var data = await query
            .OrderBy(r => r.OrderNo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { rows = data, total });
    }

    /// <summary>
    /// 获取所有角色（下拉框用）
    /// </summary>
    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _context.Sys_Roles
            .Where(r => r.Enable)
            .OrderBy(r => r.OrderNo)
            .Select(r => new { r.RoleId, r.RoleName })
            .ToListAsync();
        return Ok(roles);
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] Sys_Role entity)
    {
        entity.CreateDate = DateTime.Now;
        _context.Sys_Roles.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] Sys_Role entity)
    {
        _context.Entry(entity).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromBody] int[] ids)
    {
        var entities = await _context.Sys_Roles.Where(r => ids.Contains(r.RoleId)).ToListAsync();
        _context.Sys_Roles.RemoveRange(entities);
        // 同时删除关联的权限映射
        var mappings = _context.Sys_RoleMenus.Where(rm => ids.Contains(rm.RoleId));
        _context.Sys_RoleMenus.RemoveRange(mappings);
        var count = await _context.SaveChangesAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 获取角色已分配的菜单ID列表
    /// </summary>
    [HttpGet("{roleId}/menus")]
    public async Task<IActionResult> GetRoleMenus(int roleId)
    {
        var menuIds = await _context.Sys_RoleMenus
            .Where(rm => rm.RoleId == roleId)
            .Select(rm => rm.MenuId)
            .ToListAsync();
        return Ok(menuIds);
    }

    /// <summary>
    /// 给角色分配菜单权限（整体替换）
    /// </summary>
    [HttpPost("{roleId}/menus")]
    public async Task<IActionResult> SaveRoleMenus(int roleId, [FromBody] int[] menuIds)
    {
        // 1. 删除旧的映射
        var oldMappings = _context.Sys_RoleMenus.Where(rm => rm.RoleId == roleId);
        _context.Sys_RoleMenus.RemoveRange(oldMappings);

        // 2. 添加新的映射
        var newMappings = menuIds.Select(menuId => new Sys_RoleMenu
        {
            RoleId = roleId,
            MenuId = menuId
        });
        _context.Sys_RoleMenus.AddRange(newMappings);

        await _context.SaveChangesAsync();
        return Ok(new { message = "保存成功" });
    }
}
