namespace CP6.Core.Services.Platform;

/// <summary>
/// 平台租户管理服务（多租户合规 #5 块①）。平台超管对租户花名册的 CRUD + 停用/重启用。
/// 建租户为原子操作（R9-a）：同一 <c>SaveChanges</c> 落 <c>Sys_Tenant</c> + 首个 admin 用户，
/// EF 单事务包裹，任一失败整体回滚；首个 admin 的临时明文密码仅在 <see cref="CreateTenantResult"/> 返回一次。
/// <para>错误经 <see cref="System.InvalidOperationException"/> 携带 E-SEC 错误码（Core 层惯例）——
/// WebApi 边界（<c>TenantController</c>）转 <c>BizException</c> 本地化：
/// E-SEC-032=租户不存在；E-SEC-033=租户编码冲突。</para>
/// </summary>
public interface ITenantAdminService
{
    Task<PagedResult<TenantRow>> ListAsync(string? keyword, bool? enable, int page, int pageSize);
    Task<TenantDetail?> GetAsync(Guid id);
    Task<CreateTenantResult> CreateAsync(string code, string name, DateTime? expire, string? remark, string adminUserName);
    Task UpdateAsync(Guid id, string name, DateTime? expire, string? remark, string? timeZoneId = null);
    Task SuspendAsync(Guid id);
    Task ReactivateAsync(Guid id);
}

/// <summary>通用分页结果包。</summary>
public record PagedResult<T>(IReadOnlyList<T> Rows, int Total);

/// <summary>租户列表行（含跨租户用户数统计）。</summary>
public record TenantRow(Guid Id, string TenantCode, string TenantName, bool Enable, DateTime? ExpireDate, int UserCount, DateTime CreateDate);

/// <summary>租户详情（含备注 + 用户数 + 时区 id）。</summary>
public record TenantDetail(Guid Id, string TenantCode, string TenantName, bool Enable, DateTime? ExpireDate, string? Remark, int UserCount, string? TimeZoneId = null);

/// <summary>建租户结果：新租户 Id + 首个 admin 账号 + 一次性临时明文密码（仅本次返回）。</summary>
public record CreateTenantResult(Guid TenantId, string AdminUserName, string TempPassword);
