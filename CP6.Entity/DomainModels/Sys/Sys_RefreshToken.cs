using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 刷新令牌（S 类认证加固 T4）。仅存原始令牌的 SHA-256 哈希（不可逆，泄库不可还原）。
/// 轮换：每次 refresh 吊销旧令牌并签发新令牌，<see cref="ReplacedByTokenHash"/> 串成链；
/// 已吊销令牌再次提交 = 重用检测（盗用），吊销该用户整条链。
/// 继承 <see cref="BaseTenantEntity"/> 自动盖章 + 租户隔离；但 <see cref="TokenHash"/> 索引
/// 保持单列全局唯一（refresh 时无租户上下文，按 TokenHash 跨租户查 + IgnoreQueryFilters）。
/// </summary>
public class Sys_RefreshToken : BaseTenantEntity
{
    /// <summary>所属用户 → Sys_User.Id</summary>
    public Guid UserId { get; set; }

    /// <summary>原始令牌的 SHA-256 十六进制哈希（全局唯一）</summary>
    [MaxLength(128)] public string TokenHash { get; set; } = string.Empty;

    /// <summary>过期时刻</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>吊销时刻（null=有效）</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>轮换后替换本令牌的新令牌哈希（链式追踪）</summary>
    [MaxLength(128)] public string? ReplacedByTokenHash { get; set; }

    /// <summary>签发时客户端 IP</summary>
    [MaxLength(64)] public string? CreatedIp { get; set; }

    /// <summary>签发时 User-Agent</summary>
    [MaxLength(256)] public string? UserAgent { get; set; }
}
