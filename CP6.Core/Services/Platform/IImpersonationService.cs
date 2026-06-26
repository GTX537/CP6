using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace CP6.Core.Services.Platform;

/// <summary>
/// 完整 impersonation（替身登录）服务（多租户合规 #5 块② heart）。
/// <para>平台超管"踏入"目标租户某用户：以<b>目标用户身份</b>签发短寿命 access 令牌
/// （<c>tenant_id</c>=目标租户 → 全部现有控制器零改即作用域到目标租户），并带 <c>impersonator_id</c>=真身（可追溯），
/// <b>不</b>带 <c>is_platform_admin</c>。refresh Cookie 不动（隐式切出窗口：imp access 过期 → refresh 自然回平台超管会话）。</para>
/// <para>安全不变量（spec R2/R3/R5/R9）：</para>
/// <list type="bullet">
///   <item><b>R2 双向 jti 黑名单</b>：start 拉黑被替换的平台 access jti（防踏入后旧令牌复用）；end 拉黑当前 imp access jti（防切出后 imp 令牌续命）。</item>
///   <item><b>R3</b>：imp 令牌 <c>mustChangePassword</c> 恒 false（目标用户即便须改密，imp 会话也不被 MustChangePasswordMiddleware 死锁）。</item>
///   <item><b>R5</b>：审计强制落平台租户（start 经 <see cref="Common.TenantScope"/>；end 显式切回）。</item>
///   <item><b>R9-c</b>：end 时真身已撤销/停用 → 拒绝（撤销立即生效），E-SEC-031。</item>
/// </list>
/// <para>错误经 <see cref="System.InvalidOperationException"/> 携 E-SEC 码（Core 惯例）→ <c>ImpersonationController</c> 边界转 <c>BizException</c> 本地化。</para>
/// </summary>
public interface IImpersonationService
{
    /// <summary>踏入：以目标租户内目标用户（userId 为 null 则取首个启用管理员）身份签发 imp 令牌。
    /// 目标租户不存在/停用 → E-SEC-032；无可用目标用户 → E-SEC-035。</summary>
    Task<ImpersonationStartResult> StartAsync(Guid tenantId, Guid? userId, string? reason, ClaimsPrincipal current, HttpResponse response);

    /// <summary>切出：重签平台超管自身令牌。无 <c>impersonator_id</c> claim / 真身已撤销停用 → E-SEC-031。</summary>
    Task<ImpersonationEndResult> EndAsync(ClaimsPrincipal current, HttpResponse response);
}

/// <summary>踏入结果（含目标租户/用户标识 + 目标用户菜单 + imp 令牌有效分钟数，供前端横幅倒计时 R8）。</summary>
public record ImpersonationStartResult(Guid TenantId, string TenantName, Guid UserId, string UserName,
                                       List<MenuRow> Menus, int ExpiresInMinutes);

/// <summary>切出结果（返平台超管自身的菜单，供前端替换导航 R8）。</summary>
public record ImpersonationEndResult(List<MenuRow> Menus);

/// <summary>登录态菜单行（字段对齐 <c>AuthController.BuildProfileAsync</c> 的菜单并集投影，
/// 保证 impersonation start/end 替换的 menus 与 Login/Profile 响应结构一致，T9 前端复用）。</summary>
public record MenuRow(int Id, string MenuName, string? RoutePath, string? Icon, int? ParentId, int OrderNo);
