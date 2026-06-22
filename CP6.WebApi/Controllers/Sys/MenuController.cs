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
        _context.Entry(entity).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete]
    [RequirePermission("menu", "delete")]
    public async Task<IActionResult> Delete([FromBody] int[] ids)
    {
        var entities = await _context.Sys_Menus.Where(m => ids.Contains(m.MenuId)).ToListAsync();
        _context.Sys_Menus.RemoveRange(entities);
        // 同时删除关联的权限映射
        var mappings = _context.Sys_RoleMenus.Where(rm => ids.Contains(rm.MenuId));
        _context.Sys_RoleMenus.RemoveRange(mappings);
        var count = await _context.SaveChangesAsync();
        return Ok(new { count });
    }
}
