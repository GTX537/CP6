using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 字段级审计日志（#4 字段审计 T1）。每一次对 <see cref="IAuditable"/> 实体的
/// 新增/修改/删除产生一行，记录 who/when/what 全要素 + 标量字段 before/after JSON diff，
/// 支撑单记录时间线回放 + 按人/按实体类型审计查询。继承 <see cref="BaseTenantEntity"/>
/// 即自动纳入多租户全局过滤 + 写入盖章（反射批量覆盖，无需手写过滤）。
/// </summary>
public class Sys_FieldAuditLog : BaseTenantEntity
{
    /// <summary>被审计实体的 CLR 类型名（如 "Sys_User"）。</summary>
    [MaxLength(100)]
    [Required]
    public string EntityName { get; set; } = "";

    /// <summary>被审计实体的主键串（键形无关；复合键以 "|" 连接）。</summary>
    [MaxLength(128)]
    [Required]
    public string EntityKey { get; set; } = "";

    /// <summary>操作类型：1=新增(Added) 2=修改(Modified) 3=删除(Deleted)。</summary>
    public int Operation { get; set; }

    /// <summary>字段变更明细 JSON：[{field,old,new}]（nvarchar max）。无变更时为 "[]"。</summary>
    public string Changes { get; set; } = "[]";

    /// <summary>操作人用户 Id（取自 JWT claims；系统/匿名写入为 null）。</summary>
    public Guid? UserId { get; set; }

    /// <summary>操作人用户名（快照，避免 join；可空）。</summary>
    [MaxLength(100)]
    public string? UserName { get; set; }

    /// <summary>变更发生时刻（审计时间线排序键）。</summary>
    public DateTime ChangedAt { get; set; }
}
