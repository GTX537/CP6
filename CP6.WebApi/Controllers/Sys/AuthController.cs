using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>
/// 登录认证 API。匿名端点逐个 <c>[AllowAnonymous]</c> 标注；
/// 需登录的端点（如 change-password）用 <c>[Authorize]</c>——故类级不再统一放开匿名，
/// 否则类级 AllowAnonymous 会覆盖方法级 Authorize（ASP.NET Core 语义）。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : LocalizedControllerBase
{
    private readonly CP6Context _context;
    private readonly IConfiguration _config;
    private readonly ICurrentPermissionContext _perm;
    private readonly ITenantContext _tenant;
    private readonly IPasswordHasher _hasher;
    private readonly IPasswordPolicyService _policy;

    public AuthController(CP6Context context, IConfiguration config, ICurrentPermissionContext perm, ITenantContext tenant, IPasswordHasher hasher, IPasswordPolicyService policy)
    {
        _context = context;
        _config = config;
        _perm = perm;
        _tenant = tenant;
        _hasher = hasher;
        _policy = policy;
    }

    /// <summary>
    /// 登录
    /// POST /api/auth/login
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
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

    /// <summary>
    /// 自助改密（需登录）。校验旧密码 → 策略 → 历史不可重用 → 旧哈希入历史 + 写新哈希。
    /// POST /api/auth/change-password
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var uid = (await _perm.GetAsync()).UserId;
        var user = await _context.Sys_Users.FirstAsync(u => u.Id == uid);

        // 旧密码核对（控制器层，直接 BizException 经中间件本地化）
        if (!_hasher.Verify(req.CurrentPassword, user.Password))
            throw new BizException("E-SEC-006");

        // 策略 + 历史校验由 Core 服务以 InvalidOperationException(E-SEC 码) 抛出，边界转 BizException
        try
        {
            _policy.Validate(req.NewPassword);
            await _policy.CheckHistoryAsync(uid, req.NewPassword);
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);
        }

        // 旧哈希入历史并裁剪（不 SaveChanges，与下方写入合并一次保存）（旧 refresh 吊销 T4 叠加、审计 T3 叠加）
        await _policy.RecordHistoryAsync(uid, user.Password);
        user.Password = _hasher.Hash(req.NewPassword);
        user.PasswordChangedAt = DateTime.Now;
        user.MustChangePassword = false;
        await _context.SaveChangesAsync();   // 一次性持久化：裁剪 + 新历史 + 用户更新（原子）

        return Ok(new { code = 0, message = "OK" });
    }
}

/// <summary>改密请求体（当前密码 + 新密码）。</summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
