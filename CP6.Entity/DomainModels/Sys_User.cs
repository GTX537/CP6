using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels;

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
}
