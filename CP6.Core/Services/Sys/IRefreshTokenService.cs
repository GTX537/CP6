using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 刷新令牌服务（S 类认证加固 T4）。轮换 + 重用检测。
/// 原始令牌只在签发时返回一次，库内仅存 SHA-256 哈希。
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>签发新刷新令牌，返回原始令牌（仅此一次明文返回，须写入 httpOnly cookie）。</summary>
    Task<string> IssueAsync(Sys_User user, string? ip, string? ua);

    /// <summary>
    /// 轮换：校验原始令牌 → 吊销旧令牌 → 签发新令牌。返回新原始令牌 + 持有用户。
    /// 已吊销令牌被再次提交 = 重用检测（盗用），吊销该用户整条链并拒绝。
    /// 错误经 <see cref="InvalidOperationException"/> 携带 E-SEC 码。
    /// </summary>
    Task<(string newToken, Sys_User user)> RotateAsync(string rawToken, string? ip, string? ua);

    /// <summary>吊销单个令牌（登出用）。令牌不存在或已吊销则静默。</summary>
    Task RevokeAsync(string rawToken);

    /// <summary>吊销某用户全部有效令牌（改密 / 重用检测 / 管理员强制下线用）。</summary>
    Task RevokeAllForUserAsync(Guid userId);
}
