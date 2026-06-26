namespace CP6.Core.Services.Platform;

/// <summary>
/// 平台超管授撤服务（多租户合规 #5 块②）。平台超管对 <c>Sys_User.IsPlatformAdmin</c> 标志的授予/撤销。
/// 跨租户：目标用户可属任意租户（<c>IgnoreQueryFilters</c> 检索）。
/// <para>防自锁死（E-SEC-037）：当撤销目标本身是<b>最后一个</b>启用中的平台超管时拒绝，
/// 防止平台失去全部超管入口。</para>
/// <para>错误经 <see cref="System.InvalidOperationException"/> 携带 E-SEC 错误码（Core 层惯例）——
/// WebApi 边界（<c>PlatformAdminController</c>）转 <c>BizException</c> 本地化：
/// E-SEC-032=用户不存在；E-SEC-037=不可撤销最后一个平台超管。</para>
/// </summary>
public interface IPlatformAdminService
{
    Task<List<PlatformAdminRow>> ListAsync();
    Task GrantAsync(Guid userId);
    Task RevokeAsync(Guid userId);   // 防自锁死 E-SEC-037
}

/// <summary>平台超管名册行（含所属租户 + 启用态）。</summary>
public record PlatformAdminRow(Guid Id, string UserName, string? NickName, Guid TenantId, bool Enable);
