using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RoleController : LocalizedControllerBase
{
    private readonly CP6Context _context;

    public RoleController(CP6Context context)
    {
        _context = context;
    }

    [HttpGet]
    [RequirePermission("role", "query")]
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
    [RequirePermission("role", "add")]
    public async Task<IActionResult> Add([FromBody] Sys_Role entity)
    {
        entity.CreateDate = DateTime.Now;
        _context.Sys_Roles.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut]
    [RequirePermission("role", "edit")]
    public async Task<IActionResult> Update([FromBody] Sys_Role entity)
    {
        // #4 字段审计 T4：先查后改（替 attach-as-Modified），令 Modified diff 准确。
        // 仅拷可编辑列；RoleId(PK) 与 CreateDate 刻意不动（无 Creator/Modifier）。
        // P0-T3：复合主键 (TenantId,RoleId) 后不能用单参 FindAsync；按 RoleId 查（全局过滤自动限定当前租户）。
        var existing = await _context.Sys_Roles.FirstOrDefaultAsync(r => r.RoleId == entity.RoleId);
        if (existing == null)
            return NotFound();

        existing.RoleName = entity.RoleName;
        existing.Description = entity.Description;
        existing.Enable = entity.Enable;
        existing.OrderNo = entity.OrderNo;

        await _context.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete]
    [RequirePermission("role", "delete")]
    public async Task<IActionResult> Delete([FromBody] int[] ids)
    {
        var entities = await _context.Sys_Roles.Where(r => ids.Contains(r.RoleId)).ToListAsync();
        _context.Sys_Roles.RemoveRange(entities);
        // 同时删除关联的权限映射（P0-T3 补口：Sys_RoleMenu 已租户化，全局过滤限定当前租户——
        // 删本租户角色只清本租户映射，他租同号 RoleId 不受影响）
        var mappings = _context.Sys_RoleMenus.Where(rm => ids.Contains(rm.RoleId));
        _context.Sys_RoleMenus.RemoveRange(mappings);
        var count = await _context.SaveChangesAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 获取角色已分配的菜单ID列表
    /// </summary>
    [HttpGet("{roleId}/menus")]
    [RequirePermission("role", "query")]
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
    [RequirePermission("role", "edit")]
    public async Task<IActionResult> SaveRoleMenus(int roleId, [FromBody] int[] menuIds)
    {
        // 1. 删除旧的映射（P0-T3 补口：全局过滤限定当前租户——A 改角色菜单不再串改 B 的同号角色）
        var oldMappings = _context.Sys_RoleMenus.Where(rm => rm.RoleId == roleId);
        _context.Sys_RoleMenus.RemoveRange(oldMappings);

        // 2. 添加新的映射（TenantId 由 StampTenant 自动盖当前租户）
        var newMappings = menuIds.Select(menuId => new Sys_RoleMenu
        {
            RoleId = roleId,
            MenuId = menuId
        });
        _context.Sys_RoleMenus.AddRange(newMappings);

        await _context.SaveChangesAsync();
        return Ok(new { message = Localizer["保存成功"] });
    }
}
