using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 安全事件审计日志（S 类认证加固 T3）。承担 /api/auth 的认证类审计——
/// 全局 OperLogFilter 主动跳过 /api/auth（防密码泄露），故登录成败/锁定/改密等独立记此表。
/// 继承 <see cref="BaseTenantEntity"/>：按租户隔离 + 自动盖章。
/// </summary>
public class Sys_SecurityLog : BaseTenantEntity
{
    /// <summary>关联用户（匿名失败/用户不存在时为 null）</summary>
    public Guid? UserId { get; set; }

    /// <summary>登录名（即使用户不存在也记录尝试名，便于排查）</summary>
    [MaxLength(100)] public string? UserName { get; set; }

    /// <summary>请求携带的租户编码（登录消歧用，原样留痕）</summary>
    [MaxLength(64)] public string? RequestTenantCode { get; set; }

    /// <summary>事件类型（<see cref="SecurityEventType"/> 的 int 值）</summary>
    public int EventType { get; set; }

    /// <summary>原因/备注（如 "user not found" / "wrong password"）</summary>
    [MaxLength(256)] public string? Reason { get; set; }

    /// <summary>客户端 IP</summary>
    [MaxLength(64)] public string? ClientIp { get; set; }

    /// <summary>User-Agent</summary>
    [MaxLength(256)] public string? UserAgent { get; set; }

    /// <summary>事件时刻</summary>
    public DateTime CreatedAt { get; set; }
}
