using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DTOs;

/// <summary>
/// 登录请求参数
/// </summary>
public class LoginRequest
{
    [Required]
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
