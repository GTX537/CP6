using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 部门（组织树节点）。PUB 章00 组织模型 —— PUB 数据权限子树过滤 + OA 审批路由"部门长"共用。
/// 继承 BaseEntity（Id/Creator/CreateDate/Modifier/ModifyDate）；用 Enable 软停用，不引入 IsDeleted/RowVersion。
/// 多租户 TenantId 延后到系统级多租户阶段统一加（与 Sys 家族一致）。
/// </summary>
[Table("Sys_Dept")]
public class Sys_Dept : BaseTenantEntity
{
    /// <summary>上级部门 Id；根部门为 null</summary>
    public Guid? ParentId { get; set; }

    /// <summary>部门编码（本阶段全局唯一；多租户后改为租户内唯一）。建后不可改</summary>
    [Required, MaxLength(50)]
    public string DeptCode { get; set; } = string.Empty;

    /// <summary>部门名称</summary>
    [Required, MaxLength(100)]
    public string DeptName { get; set; } = string.Empty;

    /// <summary>物化路径 /{rootId}/.../{selfId}/（首尾带 /，子树前缀匹配；移动时重算子树）</summary>
    [MaxLength(900)]
    public string Path { get; set; } = string.Empty;

    /// <summary>部门负责人 → Sys_User.Id（OA"部门长"审批路由用）</summary>
    public Guid? LeaderId { get; set; }

    /// <summary>同级排序</summary>
    public int Sort { get; set; }

    /// <summary>是否启用（软停用，不物理删除）</summary>
    public bool Enable { get; set; } = true;
}
