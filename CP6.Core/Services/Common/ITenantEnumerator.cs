using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Common;

/// <summary>
/// 活跃租户枚举（OA 章10 §5）。后台 Worker 无 HttpContext → 无法从 JWT 取租户，须显式按租户循环：
/// 先 <see cref="ListActiveAsync"/> 取全部启用租户，再逐个开 scope 设 <see cref="ITenantContext"/> 跑活。
/// 数据源 <see cref="CP6.Entity.DomainModels.Sys.Sys_Tenant"/>（共享表，不被行级过滤）。
/// </summary>
public interface ITenantEnumerator
{
    /// <summary>列出全部启用租户的 TenantId（= Sys_Tenant.Id）。无租户行时回退默认租户，保证后台至少跑一遍。</summary>
    Task<IReadOnlyList<Guid>> ListActiveAsync(CancellationToken ct = default);
}

/// <summary>默认实现：查 Sys_Tenant.Enable=true 的行。空表（迁移前/单测）回退 <see cref="TenantContext.DefaultTenant"/>。</summary>
public class TenantEnumerator : ITenantEnumerator
{
    private readonly CP6Context _db;

    public TenantEnumerator(CP6Context db) => _db = db;

    public async Task<IReadOnlyList<Guid>> ListActiveAsync(CancellationToken ct = default)
    {
        var ids = await _db.Sys_Tenants
            .Where(t => t.Enable)
            .Select(t => t.Id)
            .ToListAsync(ct);

        // 空表 = 单租户期 / 迁移尚未 seed → 至少跑默认租户，行为与多租户前一致
        return ids.Count > 0 ? ids : new List<Guid> { TenantContext.DefaultTenant };
    }
}
