using System.Data.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.WebApi.Filters;
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
    public Task<IActionResult> Add([FromBody] Sys_Menu entity)
        => WriteLegacyAsync("menu:add", "menu-add", [entity.MenuId], entity.MenuKey, EditableInput(entity), async ct =>
        {
            entity.CreateDate = DateTime.Now;
            _context.Sys_Menus.Add(entity);
            await _context.SaveChangesAsync(ct);
            return new(Menu: entity);
        });

    [HttpPut]
    [RequirePermission("menu", "edit")]
    public Task<IActionResult> Update([FromBody] Sys_Menu entity)
        => WriteLegacyAsync("menu:edit", "menu-edit", [entity.MenuId], entity.MenuKey, EditableInput(entity), async ct =>
        {
            // Load the row for accurate field auditing and preserve its creation time.
            var existing = await _context.Sys_Menus.FindAsync([entity.MenuId], ct);
            if (existing == null) return new();

            existing.MenuName = entity.MenuName;
            existing.RoutePath = entity.RoutePath;
            existing.MenuKey = entity.MenuKey;
            existing.Icon = entity.Icon;
            existing.ParentId = entity.ParentId;
            existing.OrderNo = entity.OrderNo;
            existing.Enable = entity.Enable;

            await _context.SaveChangesAsync(ct);
            return new(Menu: existing);
        });

    /// <summary>
    /// 批量保存菜单树的父子关系与同级顺序。
    /// </summary>
    [HttpPut("tree")]
    [RequirePermission("menu", "edit")]
    [CurrentWriteAuthority]
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
    public Task<IActionResult> Delete([FromBody] int[] ids)
        => WriteLegacyAsync("menu:delete", "menu-delete", ids.Distinct().Order().ToArray(), null, new { }, async ct =>
        {
            var entities = await _context.Sys_Menus.Where(m => ids.Contains(m.MenuId)).ToListAsync(ct);
            _context.Sys_Menus.RemoveRange(entities);
            // Menus are global and the original operation removes all tenants' RoleMenu mappings.
            var mappings = _context.Sys_RoleMenus.IgnoreQueryFilters().Where(rm => ids.Contains(rm.MenuId));
            _context.Sys_RoleMenus.RemoveRange(mappings);
            return new(DeletedCount: await _context.SaveChangesAsync(ct));
        });

    private async Task<IActionResult> WriteLegacyAsync(string permission, string kind, int[] ids, string? key,
        object input, Func<CancellationToken, Task<LegacyMenuMutationResultV1>> write)
    {
        if (!_context.Database.IsNpgsql()) return LegacyResult(await write(default));

        var http = HttpContext;
        var services = http.RequestServices;
        try
        {
            // The original authority now owns its individual transactions. The single-transaction
            // CurrentWriteAuthority filter still handles menu tree ordering.
            var authority = services.GetRequiredService<IamAdministrationV1>();
            var result = await authority.WriteLegacyMenuAsync(permission, kind, ids, key, input, http.User.FindFirst("impersonator_id")?.Value,
                ct => IamAdministrationHttpV1.CurrentActor(http, services, ct, allowImpersonation: true),
                write, http.RequestAborted);
            return LegacyResult(result);
        }
        catch (IamAdministrationExceptionV1 error) { return Failure(error.Status, error.Message); }
        catch (TenantManagementExceptionV1 error) { return Failure(error.Status, error.Message); }
        catch (Exception error) when (error is DbException or DbUpdateException or CurrentPermissionUnavailableException)
        {
            return Failure(503, "AUTHORITY_FENCE_UNAVAILABLE");
        }

        IActionResult Failure(int status, string code) => StatusCode(status, new { code = status, message = code });
    }

    private IActionResult LegacyResult(LegacyMenuMutationResultV1 result)
    {
        if (result.Menu is not null) return Ok(result.Menu);
        if (result.DeletedCount is { } count) return Ok(new { count });
        return NotFound();
    }

    private static object EditableInput(Sys_Menu entity)
        => new { entity.MenuName, entity.RoutePath, entity.MenuKey, entity.Icon, entity.ParentId, entity.OrderNo, entity.Enable };
}

public record MenuTreePosition(int MenuId, int? ParentId, int OrderNo);
