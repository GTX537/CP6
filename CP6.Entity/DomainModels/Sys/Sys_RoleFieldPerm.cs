using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 角色字段权限 —— PUB 章04。某角色对某资源某字段的访问级。
/// Access：1=可读写 / 2=只读 / 3=隐藏。多角色聚合取 MIN（最可见）。
/// 仅记录非默认（受限）字段；未记录字段默认 1=可读写。
/// </summary>
[Table("Sys_RoleFieldPerm")]
public class Sys_RoleFieldPerm : BaseTenantEntity
{
    /// <summary>角色 → Sys_Role.RoleId（int，B1-D1）</summary>
    public int RoleId { get; set; }

    /// <summary>资源键（如 order）</summary>
    [MaxLength(100)]
    public string ResourceKey { get; set; } = "";

    /// <summary>字段名（实体属性名，如 Cost）</summary>
    [MaxLength(100)]
    public string FieldName { get; set; } = "";

    /// <summary>访问级 1可读写/2只读/3隐藏</summary>
    public int Access { get; set; }
}
