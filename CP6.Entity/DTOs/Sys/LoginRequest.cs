using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DTOs.Sys;

/// <summary>
/// 登录请求参数
/// </summary>
public class LoginRequest
{
    [Required]
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 租户编码（OA 章10 §7 登录租户选择器）。多租户下同名用户消歧用：
    /// 单租户/全局唯一用户名时可省略；同名用户跨租户时必填，否则返回"请指定租户"。
    /// </summary>
    public string? TenantCode { get; set; }
}
