using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>
/// 登录认证 API（不需要 Token 即可访问）
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController : LocalizedControllerBase
{
    private readonly CP6Context _context;
    private readonly IConfiguration _config;
    private readonly ICurrentPermissionContext _perm;
    private readonly ITenantContext _tenant;

    public AuthController(CP6Context context, IConfiguration config, ICurrentPermissionContext perm, ITenantContext tenant)
    {
        _context = context;
        _config = config;
        _perm = perm;
        _tenant = tenant;
    }

    /// <summary>
    /// 登录
    /// POST /api/auth/login
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // 1. 查找用户。章10 登录引导：此刻租户未知（无 JWT），用 IgnoreQueryFilters 跨租户按名查找
        //    （多租户唯一性需登录租户选择器，属后续；单租户/默认下行为不变）。
        var user = await _context.Sys_Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.UserName == request.UserName);

        if (user == null)
            return BadRequest(new { message = Localizer["用户名不存在"] });

        // 2. 验证密码（简单对比，生产环境应用哈希）
        if (user.Password != request.Password)
            return BadRequest(new { message = Localizer["密码错误"] });

        if (!user.Enable)
            return BadRequest(new { message = Localizer["账号已被禁用"] });

        // 章10：确定当前请求租户为该用户的租户 → 后续权限聚合/菜单查询按其租户正确作用域
        _tenant.CurrentTenantId = user.TenantId;

        // 3. 生成 JWT Token（带 tenant_id，后续请求由 TenantMiddleware 解析）
        var jwt = _config.GetSection("JWT");
        var token = JwtHelper.GenerateToken(
            userId: user.Id.ToString(),
            userName: user.UserName,
            secret: jwt["Secret"]!,
            issuer: jwt["Issuer"]!,
            audience: jwt["Audience"]!,
            expireMinutes: int.Parse(jwt["ExpireMinutes"]!),
            tenantId: user.TenantId);

        // 4. 登录聚合（PUB 章09）：预热权限上下文（多角色聚合 + 缓存），首请求免重建
        var ctx = await _perm.PrewarmAsync(user.Id);

        // 4.1 菜单按全部角色聚合（多角色 RBAC，取并集）
        var roleIds = ctx.RoleIds;
        var menuIds = await _context.Sys_RoleMenus
            .Where(rm => roleIds.Contains(rm.RoleId))
            .Select(rm => rm.MenuId)
            .Distinct()
            .ToListAsync();

        var menus = await _context.Sys_Menus
            .Where(m => menuIds.Contains(m.MenuId) && m.Enable)
            .OrderBy(m => m.OrderNo)
            .Select(m => new { id = m.MenuId, m.MenuName, m.RoutePath, m.Icon, m.ParentId, m.OrderNo } as object)
            .ToListAsync();

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
