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
public class MenuController : ControllerBase
{
    private readonly CP6Context _context;

    public MenuController(CP6Context context)
    {
        _context = context;
    }

    /// <summary>
    /// 获取所有菜单
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var menus = await _context.Sys_Menus
            .OrderBy(m => m.OrderNo)
            .ToListAsync();
        return Ok(menus);
    }

    [HttpPost]
    [RequirePermission("menu", "add")]
    public async Task<IActionResult> Add([FromBody] Sys_Menu entity)
    {
        entity.CreateDate = DateTime.Now;
        _context.Sys_Menus.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut]
    [RequirePermission("menu", "edit")]
    public async Task<IActionResult> Update([FromBody] Sys_Menu entity)
    {
        // #4 字段审计 T4：先查后改（替 attach-as-Modified），令 Modified diff 准确。
        // 仅拷可编辑列；MenuId(PK) 与 CreateDate 刻意不动（无 Creator/Modifier）。
        var existing = await _context.Sys_Menus.FindAsync(entity.MenuId);
        if (existing == null)
            return NotFound();

        existing.MenuName = entity.MenuName;
        existing.RoutePath = entity.RoutePath;
        existing.MenuKey = entity.MenuKey;
        existing.Icon = entity.Icon;
        existing.ParentId = entity.ParentId;
        existing.OrderNo = entity.OrderNo;
        existing.Enable = entity.Enable;

        await _context.SaveChangesAsync();
        return Ok(existing);
    }

    /// <summary>
    /// 批量保存菜单树的父子关系与同级顺序。
    /// </summary>
    [HttpPut("tree")]
    [RequirePermission("menu", "edit")]
    public async Task<IActionResult> UpdateTree([FromBody] MenuTreePosition[] positions)
    {
        if (positions.Length == 0)
            return BadRequest(new { message = "菜单树不能为空" });

        if (positions.Any(x => x.MenuId <= 0))
            return BadRequest(new { message = "菜单 ID 必须大于 0" });

        if (positions.Any(x => x.OrderNo < 0))
            return BadRequest(new { message = "菜单排序不能小于 0" });

        if (positions.GroupBy(x => x.MenuId).Any(group => group.Count() > 1))
            return BadRequest(new { message = "菜单树包含重复 ID" });

        var menus = await _context.Sys_Menus.ToListAsync();
        var menuById = menus.ToDictionary(x => x.MenuId);
        var missingId = positions
            .Select(x => (int?)x.MenuId)
            .FirstOrDefault(id => id.HasValue && !menuById.ContainsKey(id.Value));
        if (missingId.HasValue)
            return NotFound(new { message = $"菜单 {missingId.Value} 不存在" });

        var parentById = menus.ToDictionary(x => x.MenuId, x => x.ParentId);
        foreach (var position in positions)
        {
            if (position.ParentId == position.MenuId)
                return BadRequest(new { message = $"菜单 {position.MenuId} 不能作为自己的父节点" });
            if (position.ParentId.HasValue && !menuById.ContainsKey(position.ParentId.Value))
                return BadRequest(new { message = $"父菜单 {position.ParentId.Value} 不存在" });

            parentById[position.MenuId] = position.ParentId;
        }

        foreach (var position in positions)
        {
            var visited = new HashSet<int>();
            int? currentId = position.MenuId;
            while (currentId.HasValue)
            {
                if (!visited.Add(currentId.Value))
                    return BadRequest(new { message = $"菜单 {position.MenuId} 的层级形成循环" });
                currentId = parentById.GetValueOrDefault(currentId.Value);
            }
        }

        foreach (var position in positions)
        {
            var menu = menuById[position.MenuId];
            menu.ParentId = position.ParentId;
            menu.OrderNo = position.OrderNo;
        }

        await _context.SaveChangesAsync();
        return Ok(new { count = positions.Length });
    }

    [HttpDelete]
    [RequirePermission("menu", "delete")]
    public async Task<IActionResult> Delete([FromBody] int[] ids)
    {
        var entities = await _context.Sys_Menus.Where(m => ids.Contains(m.MenuId)).ToListAsync();
        _context.Sys_Menus.RemoveRange(entities);
        // 同时删除关联的权限映射。P0-T3 补口：Sys_Menu 是全局表，删菜单须清**所有租户**的映射
        // （Sys_RoleMenu 已租户化，不带 IgnoreQueryFilters 只会清当前租户、他租留孤儿行）。
        var mappings = _context.Sys_RoleMenus.IgnoreQueryFilters().Where(rm => ids.Contains(rm.MenuId));
        _context.Sys_RoleMenus.RemoveRange(mappings);
        var count = await _context.SaveChangesAsync();
        return Ok(new { count });
    }
}

public record MenuTreePosition(int MenuId, int? ParentId, int OrderNo);
