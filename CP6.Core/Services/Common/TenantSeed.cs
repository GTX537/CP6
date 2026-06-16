using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Common;

/// <summary>
/// 默认租户种子（OA 章10 §7/§8）。多租户迁移五步的第②步"建默认租户 + 回填存量"在 SQL 里已写死默认租户 Id，
/// 本 seed 负责把它登记进 <see cref="Sys_Tenant"/> 注册表（幂等，按 Id 判存），使枚举/登录/管理 UI 能看到它。
/// 默认租户 = <see cref="TenantContext.DefaultTenant"/>，单租户期的唯一租户，也是后台兜底租户。
/// </summary>
public static class TenantSeed
{
    /// <summary>幂等播种默认租户。返回是否新增。</summary>
    public static bool EnsureSeeded(CP6Context db)
    {
        if (db.Sys_Tenants.Any(t => t.Id == TenantContext.DefaultTenant)) return false;

        db.Sys_Tenants.Add(new Sys_Tenant
        {
            Id = TenantContext.DefaultTenant,
            TenantCode = "DEFAULT",
            TenantName = "默认租户",
            Enable = true,
            Remark = "系统初始单租户 / 存量数据回填目标 / 后台任务兜底租户",
        });
        db.SaveChanges();
        return true;
    }
}
