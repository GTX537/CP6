using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 系统用户实体
/// </summary>
public class Sys_User : BaseEntity
{
    /// <summary>
    /// 用户名（登录账号）
    /// </summary>
    [MaxLength(100)]
    [Required]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 密码（存储加密后的值）
    /// </summary>
    [MaxLength(200)]
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 昵称
    /// </summary>
    [MaxLength(100)]
    public string? NickName { get; set; }

    /// <summary>
    /// 所属角色ID
    /// </summary>
    public int? RoleId { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enable { get; set; } = true;

    // ───── PUB 章00 组织模型：补三字段（数据权限 + 审批路由用）─────

    /// <summary>所属部门 → Sys_Dept.Id（PUB 数据权限"本部门"用）</summary>
    public Guid? DeptId { get; set; }

    /// <summary>直属上级 → Sys_User.Id（OA"直属上级"审批路由用）</summary>
    public Guid? ManagerId { get; set; }

    /// <summary>邮箱（通知用）</summary>
    [MaxLength(100)]
    public string? Email { get; set; }
}
