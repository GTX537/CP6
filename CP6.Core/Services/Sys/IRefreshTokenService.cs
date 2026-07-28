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
    Task<string> IssueAsync(Sys_User user, string? ip, string? ua, RefreshTokenClientContext client)
        => IssueAsync(user, ip, ua);

    /// <summary>
    /// 轮换：校验原始令牌 → 吊销旧令牌 → 签发新令牌。返回新原始令牌 + 持有用户。
    /// 已吊销令牌被再次提交 = 重用检测（盗用），吊销该用户整条链并拒绝。
    /// 错误经 <see cref="InvalidOperationException"/> 携带 E-SEC 码。
    /// </summary>
    Task<(string newToken, Sys_User user)> RotateAsync(string rawToken, string? ip, string? ua);
    Task<(string newToken, Sys_User user)> RotateAsync(string rawToken, string? ip, string? ua, RefreshTokenClientContext client)
        => RotateAsync(rawToken, ip, ua);

    /// <summary>吊销单个令牌（登出用）。令牌不存在或已吊销则静默。</summary>
    Task RevokeAsync(string rawToken);

    /// <summary>
    /// 吊销某用户全部有效令牌（改密 / 重用检测 / 管理员强制下线用）。
    /// <paramref name="saveChanges"/>=false 时仅把吊销变更入轨不落库，供调用方与其它写入合并一次原子保存
    /// （如改密：改密成功 ⇔ 旧凭证全失效，二者须同生共死）。
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, bool saveChanges = true);

    /// <summary>Revoke every native refresh token issued to one registered device.</summary>
    Task RevokeAllForDeviceAsync(Guid tenantId, string deviceId, bool saveChanges = true)
        => Task.CompletedTask;
}

/// <summary>刷新令牌的非敏感客户端画像，用于设备级审计和会话追踪。</summary>
public record RefreshTokenClientContext(string ClientKind, string? DeviceId, string? AppVersion)
{
    public static readonly RefreshTokenClientContext Web = new("Web", null, null);
}
