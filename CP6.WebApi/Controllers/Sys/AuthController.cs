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
    private readonly IPasswordHasher _hasher;

    public AuthController(CP6Context context, IConfiguration config, ICurrentPermissionContext perm, ITenantContext tenant, IPasswordHasher hasher)
    {
        _context = context;
        _config = config;
        _perm = perm;
        _tenant = tenant;
        _hasher = hasher;
    }

    /// <summary>
    /// 登录
    /// POST /api/auth/login
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // 1. 章10 §7 登录租户选择器：此刻租户未知（无 JWT），跨租户按名查找用户（IgnoreQueryFilters）。
        //    提供 TenantCode → 先解析租户并缩到该租户内；否则按名跨租户，唯一即放行、同名多租户要求指定租户。
        Sys_User? user;
        if (!string.IsNullOrWhiteSpace(request.TenantCode))
        {
            var code = request.TenantCode.Trim();
            var tenant = await _context.Sys_Tenants
                .FirstOrDefaultAsync(t => t.TenantCode == code);
            if (tenant == null)
                return BadRequest(new { message = Localizer["租户不存在"] });
            if (!tenant.Enable)
                return BadRequest(new { message = Localizer["租户已停用"] });

            user = await _context.Sys_Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.UserName == request.UserName && u.TenantId == tenant.Id);
        }
        else
        {
            var matches = await _context.Sys_Users
                .IgnoreQueryFilters()
                .Where(u => u.UserName == request.UserName)
                .Take(2)   // 只需判定 0 / 1 / 多
                .ToListAsync();

            if (matches.Count > 1)
                // 同名用户跨多个租户 → 要求指定租户编码消歧（不泄露具体是哪些租户）
                return BadRequest(new { message = Localizer["该用户名存在于多个租户，请提供租户编码"], needTenant = true });

            user = matches.FirstOrDefault();
        }

        if (user == null)
            return BadRequest(new { message = Localizer["用户名不存在"] });

        // 2. 验证密码（BCrypt 哈希对比）
        if (!_hasher.Verify(request.Password, user.Password))
            return BadRequest(new { message = Localizer["密码错误"] });

        if (!user.Enable)
            return BadRequest(new { message = Localizer["账号已被禁用"] });

        // 未指定 TenantCode 走唯一名命中时，仍要校验该用户的租户未停用
        if (string.IsNullOrWhiteSpace(request.TenantCode))
        {
            var ownTenant = await _context.Sys_Tenants.FirstOrDefaultAsync(t => t.Id == user.TenantId);
            if (ownTenant is { Enable: false })
                return BadRequest(new { message = Localizer["租户已停用"] });
        }

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
