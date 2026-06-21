using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 历史密码哈希（S 类认证加固 T2）。改密时旧哈希入表，校验新密码不得与最近 N 条重用。
/// 继承 <see cref="BaseTenantEntity"/>：按租户隔离 + 自动盖章。
/// </summary>
public class Sys_PasswordHistory : BaseTenantEntity
{
    /// <summary>所属用户 → Sys_User.Id</summary>
    public Guid UserId { get; set; }

    /// <summary>历史密码的 BCrypt 哈希（非明文）</summary>
    [MaxLength(200)] public string PasswordHash { get; set; } = string.Empty;

    /// <summary>该密码被替换（写入历史）的时刻</summary>
    public DateTime ChangedAt { get; set; }
}
