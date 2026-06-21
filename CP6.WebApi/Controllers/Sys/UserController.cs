using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserController : LocalizedControllerBase
{
    private readonly CP6Context _context;

    public UserController(CP6Context context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetList(int page = 1, int pageSize = 10, string? keyword = null)
    {
        var query = _context.Sys_Users.AsQueryable();
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(u => u.UserName.Contains(keyword) || (u.NickName != null && u.NickName.Contains(keyword)));

        var total = await query.CountAsync();
        var data = await query
            .OrderByDescending(u => u.CreateDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.NickName,
                u.RoleId,
                u.Enable,
                u.DeptId,       // PUB 章00 组织字段
                u.ManagerId,
                u.Email,
                u.Creator,
                u.CreateDate
            })
            .ToListAsync();

        return Ok(new { rows = data, total });
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] Sys_User entity)
    {
        if (await _context.Sys_Users.AnyAsync(u => u.UserName == entity.UserName))
            return BadRequest(new { message = Localizer["用户名已存在"] });

        entity.CreateDate = DateTime.Now;
        _context.Sys_Users.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] Sys_User entity)
    {
        var existing = await _context.Sys_Users.FindAsync(entity.Id);
        if (existing == null)
            return NotFound();

        existing.UserName = entity.UserName;
        existing.NickName = entity.NickName;
        existing.RoleId = entity.RoleId;
        existing.Enable = entity.Enable;
        existing.DeptId = entity.DeptId;       // PUB 章00 组织字段
        existing.ManagerId = entity.ManagerId;
        existing.Email = entity.Email;
        existing.ModifyDate = DateTime.Now;

        // 密码非空才更新
        // TODO(S类 认证加固 T7): 此处直接存明文是已知遗留写入路径；T7「UserController 哈希+管理员重置」将改为
        //   existing.Password = _hasher.Hash(entity.Password) 并彻底消除明文写入。T7 落地前由启动钩子 PasswordHashMigrationSeed 兜底。
        if (!string.IsNullOrEmpty(entity.Password))
            existing.Password = entity.Password;

        await _context.SaveChangesAsync();
        return Ok(new { existing.Id, existing.UserName, existing.NickName, existing.RoleId, existing.Enable });
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromBody] Guid[] ids)
    {
        var entities = await _context.Sys_Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        _context.Sys_Users.RemoveRange(entities);
        var count = await _context.SaveChangesAsync();
        return Ok(new { count });
    }
}
