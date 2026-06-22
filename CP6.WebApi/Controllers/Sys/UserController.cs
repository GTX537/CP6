using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
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
    private readonly IPasswordHasher _hasher;
    private readonly IRefreshTokenService _refresh;

    public UserController(CP6Context context, IPasswordHasher hasher, IRefreshTokenService refresh)
    {
        _context = context;
        _hasher = hasher;
        _refresh = refresh;
    }

    [HttpGet]
    [RequirePermission("user", "query")]
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
    [RequirePermission("user", "add")]
    public async Task<IActionResult> Add([FromBody] Sys_User entity)
    {
        if (await _context.Sys_Users.AnyAsync(u => u.UserName == entity.UserName))
            return BadRequest(new { message = Localizer["用户名已存在"] });

        // S 类认证加固 T7：建用户密码落 BCrypt（消除明文写入），管理员设初始密码 → 首登强制改密
        entity.Password = _hasher.Hash(entity.Password);
        entity.PasswordChangedAt = DateTime.Now;
        entity.MustChangePassword = true;
        entity.CreateDate = DateTime.Now;
        _context.Sys_Users.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(new { entity.Id, entity.UserName, entity.NickName, entity.RoleId, entity.Enable });
    }

    [HttpPut]
    [RequirePermission("user", "edit")]
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

        // S 类认证加固 T7：密码非空才更新，且落 BCrypt（消除明文写入路径，原 I1 遗留已清）。
        // 管理员重置密码 = 强制用户首登改密 + 吊销其全部刷新令牌（强制其它会话下线，防旧凭证沿用）。
        if (!string.IsNullOrEmpty(entity.Password))
        {
            existing.Password = _hasher.Hash(entity.Password);
            existing.PasswordChangedAt = DateTime.Now;
            existing.MustChangePassword = true;
            // saveChanges:false → 与下方用户更新合并一次原子保存（重置成功 ⇔ 旧令牌全失效）
            await _refresh.RevokeAllForUserAsync(existing.Id, saveChanges: false);
        }

        await _context.SaveChangesAsync();
        return Ok(new { existing.Id, existing.UserName, existing.NickName, existing.RoleId, existing.Enable });
    }

    [HttpDelete]
    [RequirePermission("user", "delete")]
    public async Task<IActionResult> Delete([FromBody] Guid[] ids)
    {
        var entities = await _context.Sys_Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        _context.Sys_Users.RemoveRange(entities);
        var count = await _context.SaveChangesAsync();
        return Ok(new { count });
    }
}
