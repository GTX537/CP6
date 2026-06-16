using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 角色数据范围 —— PUB 章03。某角色对某资源（如 order）的可见数据范围。
/// ScopeType：1=本人 / 2=本部门 / 3=本部门及下级 / 4=自定义部门 / 5=全部。
/// 多角色聚合取 MAX（最宽）；ScopeType=4 时 CustomDeptIds 取并集。
/// </summary>
[Table("Sys_RoleDataScope")]
public class Sys_RoleDataScope : BaseTenantEntity
{
    /// <summary>角色 → Sys_Role.RoleId（int，B1-D1）</summary>
    public int RoleId { get; set; }

    /// <summary>资源键（= 业务资源标识，如 order）</summary>
    [MaxLength(100)]
    public string ResourceKey { get; set; } = "";

    /// <summary>范围类型 1~5（见类注释）</summary>
    public int ScopeType { get; set; }

    /// <summary>自定义部门 Id 列表（CSV，ScopeType=4 时用）</summary>
    public string? CustomDeptIds { get; set; }
}
