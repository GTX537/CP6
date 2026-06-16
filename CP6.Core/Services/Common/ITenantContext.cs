namespace CP6.Core.Services.Common;

/// <summary>
/// 当前租户上下文（OA 章10 §5，OA4-D4 统一租户身份）。请求级 scoped：TenantMiddleware 从 JWT
/// 的 tenant_id 解析后写入；CP6Context 全局查询过滤 + 写入盖章读取它。后台任务无 HttpContext 时
/// 保持默认租户（或任务内显式设置）。全系统一处租户身份来源。
/// </summary>
public interface ITenantContext
{
    Guid CurrentTenantId { get; set; }
}

/// <summary>默认实现。无中间件覆盖（单租户/后台/单测）时取 <see cref="DefaultTenant"/>。</summary>
public class TenantContext : ITenantContext
{
    /// <summary>默认租户（单租户期 + 存量数据回填目标 + 后台任务兜底）。</summary>
    public static readonly Guid DefaultTenant = Guid.Parse("00000000-0000-0000-0000-0000000000A1");

    public Guid CurrentTenantId { get; set; } = DefaultTenant;
}
