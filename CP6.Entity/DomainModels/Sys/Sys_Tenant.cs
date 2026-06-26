using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 租户主数据 / 注册表（OA 章10 §7）。多租户的"花名册"：每个租户一行，<see cref="BaseEntity.Id"/> 即该租户的
/// TenantId（被各 <see cref="CP6.Entity.BaseTenantEntity"/> 表的 TenantId 引用）。本表自身是**共享表**
/// （继承 <see cref="BaseEntity"/> 而非 BaseTenantEntity，不参与行级过滤）——否则会自我过滤导致谁都查不到租户。
/// 用途：①登录租户消歧（按 <see cref="TenantCode"/> 定位）②后台 Worker 按租户循环（枚举 <see cref="Enable"/> 的行）。
/// </summary>
public class Sys_Tenant : BaseEntity, IAuditable
{
    /// <summary>租户编码（短码，登录时跨同名用户消歧用；全局唯一，大小写不敏感由上层保证）。</summary>
    [MaxLength(50)]
    [Required]
    public string TenantCode { get; set; } = string.Empty;

    /// <summary>租户名称（企业/组织显示名）。</summary>
    [MaxLength(200)]
    [Required]
    public string TenantName { get; set; } = string.Empty;

    /// <summary>是否启用（false=停用/欠费冻结：后台 Worker 跳过、登录拒绝）。</summary>
    public bool Enable { get; set; } = true;

    /// <summary>订阅到期日（null=不限期；过期可由上层逻辑置 <see cref="Enable"/>=false）。</summary>
    public DateTime? ExpireDate { get; set; }

    /// <summary>备注。</summary>
    [MaxLength(500)]
    public string? Remark { get; set; }

    /// <summary>租户 2FA 策略：0=关闭 1=可选 2=强制。默认 0。</summary>
    public int TwoFactorMode { get; set; }
}
