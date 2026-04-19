using CP6.Core.EFDbContext;
using CP6.Core.Utilities;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers;

/// <summary>
/// 登录认证 API（不需要 Token 即可访问）
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly CP6Context _context;
    private readonly IConfiguration _config;

    public AuthController(CP6Context context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    /// <summary>
    /// 登录
    /// POST /api/auth/login
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // 1. 查找用户
        var user = await _context.Sys_Users
            .FirstOrDefaultAsync(u => u.UserName == request.UserName);

        if (user == null)
            return BadRequest(new { message = "用户名不存在" });

        // 2. 验证密码（简单对比，生产环境应用哈希）
        if (user.Password != request.Password)
            return BadRequest(new { message = "密码错误" });

        if (!user.Enable)
            return BadRequest(new { message = "账号已被禁用" });

        // 3. 生成 JWT Token
        var jwt = _config.GetSection("JWT");
        var token = JwtHelper.GenerateToken(
            userId: user.Id.ToString(),
            userName: user.UserName,
            secret: jwt["Secret"]!,
            issuer: jwt["Issuer"]!,
            audience: jwt["Audience"]!,
            expireMinutes: int.Parse(jwt["ExpireMinutes"]!));

        // 4. 查询用户的菜单权限
        var menus = new List<object>();
        if (user.RoleId.HasValue)
        {
            var menuIds = await _context.Sys_RoleMenus
                .Where(rm => rm.RoleId == user.RoleId.Value)
                .Select(rm => rm.MenuId)
                .ToListAsync();

            menus = await _context.Sys_Menus
                .Where(m => menuIds.Contains(m.MenuId) && m.Enable)
                .OrderBy(m => m.OrderNo)
                .Select(m => new { id = m.MenuId, m.MenuName, m.RoutePath, m.Icon, m.ParentId, m.OrderNo } as object)
                .ToListAsync();
        }

        // 5. 返回 Token、用户信息和菜单权限
        return Ok(new
        {
            token,
            userName = user.UserName,
            nickName = user.NickName,
            roleId = user.RoleId,
            menus
        });
    }
}
