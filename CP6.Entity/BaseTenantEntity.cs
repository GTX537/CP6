namespace CP6.Entity;

/// <summary>
/// 多租户实体基类（OA 章10 §3）。介于 <see cref="BaseEntity"/> 与业务实体之间：需按租户行级隔离的
/// 实体改继承本类即获得 <see cref="TenantId"/>，并自动纳入 CP6Context 的全局查询过滤 + 写入盖章。
/// 纯字典/语言包/菜单结构等系统级共享表保持继承 <see cref="BaseEntity"/>（不带 TenantId）。
/// </summary>
public abstract class BaseTenantEntity : BaseEntity
{
    /// <summary>租户 Id（行级隔离硬墙；写入时由 SaveChanges 自动盖当前租户，查询时全局过滤）。</summary>
    public Guid TenantId { get; set; }
}
