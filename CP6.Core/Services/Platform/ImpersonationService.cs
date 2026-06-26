using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Platform;

/// <summary>
/// <see cref="IImpersonationService"/> 实现（多租户合规 #5 块② heart）。
/// <para>令牌签发<b>不</b>复用 <c>AuthController.BuildAccessToken</c>（自带身份切换 + 单独前置不变量），
/// 直接调 <see cref="JwtHelper.GenerateToken"/>。JWT secret/issuer/audience 取 <c>IConfiguration["JWT:*"]</c>
/// （与 <c>AuthController.BuildAccessToken</c> 同源）；imp 寿命取 <see cref="SecurityOptions.ImpersonationMinutes"/>。</para>
/// <para>菜单并集复刻 <c>AuthController.BuildProfileAsync</c>（Sys_UserRole 附加角色 ∪ Sys_User.RoleId 主角色 → Sys_RoleMenu → Sys_Menu），
/// 经 <c>IgnoreQueryFilters</c> 跨租户读目标用户角色（Sys_UserRole 带 TenantId 受全局过滤；Sys_Menu/Sys_RoleMenu 全局表无过滤）。</para>
/// </summary>
public class ImpersonationService : IImpersonationService
{
    private readonly CP6Context _db;
    private readonly ITokenBlacklistService _blacklist;
    private readonly ISecurityAuditService _audit;
    private readonly ITenantContext _tenant;
    private readonly SecurityOptions _sec;
    private readonly IAuthCookieWriter _cookies;
    private readonly IConfiguration _config;

    public ImpersonationService(
        CP6Context db,
        ITokenBlacklistService blacklist,
        ISecurityAuditService audit,
        ITenantContext tenant,
        IOptions<SecurityOptions> sec,
        IAuthCookieWriter cookies,
        IConfiguration config)
    {
        _db = db;
        _blacklist = blacklist;
        _audit = audit;
        _tenant = tenant;
        _sec = sec.Value;
        _cookies = cookies;
        _config = config;
    }

    public async Task<ImpersonationStartResult> StartAsync(Guid tenantId, Guid? userId, string? reason, ClaimsPrincipal current, HttpResponse response)
    {
        // 1. 目标租户必须存在且启用。
        var targetTenant = await _db.Sys_Tenants.FirstOrDefaultAsync(t => t.Id == tenantId && t.Enable)
            ?? throw new InvalidOperationException("E-SEC-032");

        // 2. 目标用户：入参 userId（须属该租户且启用）；为 null 则取该租户首个启用管理员（RoleId==1）。
        Sys_User? target;
        if (userId is Guid uid)
            target = await _db.Sys_Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == uid && u.TenantId == tenantId && u.Enable);
        else
            target = await _db.Sys_Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.RoleId == 1 && u.Enable);
        if (target == null)
            throw new InvalidOperationException("E-SEC-035");

        // 3. R2 上半：拉黑被替换的平台 access jti（防踏入后旧平台令牌被复用）。
        var oldJti = current.FindFirst("jti")?.Value;
        if (!string.IsNullOrEmpty(oldJti))
            await _blacklist.BlacklistAsync(oldJti, ComputeRemainingTtl(current, _sec.Token.AccessTokenMinutes));

        // 4. 解析真身（平台超管自身）标识，写入 imp 令牌 impersonator_id claim 供追溯。
        var platformAdminUserId = Guid.Parse(current.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var platformAdminUserName = current.FindFirst(ClaimTypes.Name)?.Value;

        // 5. R3：imp 令牌 mustChangePassword 恒 false（目标即便须改密，imp 会话也不被改密中间件死锁）；
        //    isPlatformAdmin:false（imp 期间挂起平台权）；tenant_id=目标租户 → 现有控制器零改作用域到目标租户。
        var newJti = Guid.NewGuid().ToString();
        var jwt = _config.GetSection("JWT");
        var token = JwtHelper.GenerateToken(
            userId: target.Id.ToString(),
            userName: target.UserName,
            secret: jwt["Secret"]!,
            issuer: jwt["Issuer"]!,
            audience: jwt["Audience"]!,
            expireMinutes: _sec.ImpersonationMinutes,
            tenantId: tenantId,
            jti: newJti,
            mustChangePassword: false,
            isPlatformAdmin: false,
            impersonatorId: platformAdminUserId);

        // 6. 仅换 access Cookie，refresh/CSRF 不动 → 隐式切出窗口。
        _cookies.WriteAccessCookieOnly(response, token);

        // 7. R5：审计强制落平台租户（TenantScope 包裹，Dispose 还原当前上下文）。
        using (new TenantScope(_tenant, TenantContext.DefaultTenant))
        {
            await _audit.LogAsync(SecurityEventType.ImpersonationStarted, platformAdminUserId, platformAdminUserName,
                targetTenant.TenantCode, null, null, reason);
        }

        // 8. R8：返目标用户菜单 + imp 有效分钟数。
        var menus = await BuildMenusAsync(target.Id);
        return new ImpersonationStartResult(tenantId, targetTenant.TenantName, target.Id, target.UserName, menus, _sec.ImpersonationMinutes);
    }

    public async Task<ImpersonationEndResult> EndAsync(ClaimsPrincipal current, HttpResponse response)
    {
        // 1. 必须处于 impersonation 态（带 impersonator_id claim）。
        var impClaim = current.FindFirst("impersonator_id");
        if (impClaim == null)
            throw new InvalidOperationException("E-SEC-031");

        // 2. R2 下半：拉黑当前 imp access jti（防切出后 imp 令牌续命，安全洞修复）。
        var impJti = current.FindFirst("jti")?.Value;
        if (!string.IsNullOrEmpty(impJti))
            await _blacklist.BlacklistAsync(impJti, ComputeRemainingTtl(current, _sec.ImpersonationMinutes));

        // 3. 解析真身 → R9-c：真身须仍是启用中的平台超管（撤销/停用立即生效）。
        if (!Guid.TryParse(impClaim.Value, out var impId))
            throw new InvalidOperationException("E-SEC-031");
        var impAdmin = await _db.Sys_Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == impId && u.IsPlatformAdmin && u.Enable);
        if (impAdmin == null)
            throw new InvalidOperationException("E-SEC-031");

        // 4. R5：显式切回平台租户（其后审计经 StampTenant 落对）。
        _tenant.CurrentTenantId = TenantContext.DefaultTenant;

        // 5. 重签平台超管自身令牌（恢复 is_platform_admin，去掉 impersonator_id）。
        var newJti = Guid.NewGuid().ToString();
        var jwt = _config.GetSection("JWT");
        var token = JwtHelper.GenerateToken(
            userId: impAdmin.Id.ToString(),
            userName: impAdmin.UserName,
            secret: jwt["Secret"]!,
            issuer: jwt["Issuer"]!,
            audience: jwt["Audience"]!,
            expireMinutes: _sec.Token.AccessTokenMinutes,
            tenantId: impAdmin.TenantId,
            jti: newJti,
            mustChangePassword: impAdmin.MustChangePassword,
            isPlatformAdmin: true,
            impersonatorId: null);

        // 6. 仅换 access Cookie。
        _cookies.WriteAccessCookieOnly(response, token);

        // 7. 审计（CurrentTenantId 已是平台租户，StampTenant 落对）。
        await _audit.LogAsync(SecurityEventType.ImpersonationEnded, impAdmin.Id, impAdmin.UserName, null, null, null, null);

        // 8. 返平台超管自身菜单。
        var menus = await BuildMenusAsync(impAdmin.Id);
        return new ImpersonationEndResult(menus);
    }

    /// <summary>菜单并集（复刻 <c>AuthController.BuildProfileAsync</c>）：Sys_UserRole 附加角色 ∪ Sys_User.RoleId 主角色 → Sys_RoleMenu → 启用 Sys_Menu，按 OrderNo 升序。</summary>
    private async Task<List<MenuRow>> BuildMenusAsync(Guid targetUserId)
    {
        // Sys_UserRole 带 TenantId 受全局过滤 → IgnoreQueryFilters 跨租户读目标用户角色。
        var roleIds = await _db.Sys_UserRoles.IgnoreQueryFilters()
            .Where(ur => ur.UserId == targetUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync();
        var primary = (await _db.Sys_Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == targetUserId))?.RoleId;
        if (primary is int p && !roleIds.Contains(p)) roleIds.Add(p);
        roleIds = roleIds.Distinct().ToList();

        var menuIds = await _db.Sys_RoleMenus
            .Where(rm => roleIds.Contains(rm.RoleId))
            .Select(rm => rm.MenuId)
            .Distinct()
            .ToListAsync();

        return await _db.Sys_Menus
            .Where(m => menuIds.Contains(m.MenuId) && m.Enable)
            .OrderBy(m => m.OrderNo)
            .Select(m => new MenuRow(m.MenuId, m.MenuName, m.RoutePath, m.Icon, m.ParentId, m.OrderNo))
            .ToListAsync();
    }

    /// <summary>令牌剩余 TTL（复刻 <c>AuthController.Logout</c> 算法）：exp claim 推算剩余；缺失/已过期则兜底 defaultMinutes。</summary>
    private static TimeSpan ComputeRemainingTtl(ClaimsPrincipal p, int defaultMinutes)
    {
        var ttl = TimeSpan.FromMinutes(defaultMinutes);
        if (long.TryParse(p.FindFirst("exp")?.Value, out var expUnix))
        {
            var remaining = DateTimeOffset.FromUnixTimeSeconds(expUnix) - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero) ttl = remaining;
        }
        return ttl;
    }
}
