namespace CP6.Core.Services.Sys;

/// <summary>
/// 2FA pending 暂态一次性存储（S 类 #2 T4，仿 SSO state）。
/// IDistributedCache 后端，key=sec:2fa:pending:{jti}；TTL=TwoFactorOptions.PendingTokenMinutes。
/// </summary>
public interface IPendingTokenStore
{
    /// <summary>创建 pending 记录，返回 jti。purpose=2fa_verify | 2fa_enroll。</summary>
    string Create(Guid userId, Guid tenantId, string purpose);

    /// <summary>读取 pending 记录；null=不存在/已消费/已过期。</summary>
    (Guid userId, Guid tenantId, string purpose)? Get(string pendingJti);

    /// <summary>消费 pending 记录（一次性=Remove）。</summary>
    void Consume(string pendingJti);
}
