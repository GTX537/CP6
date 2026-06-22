namespace CP6.Core.Services.Sys;

/// <summary>
/// 访问令牌（access JWT）黑名单（S 类认证加固 T5）。
/// 自研 JWT 无状态、签名有效即放行，登出无法"作废"已签发的 access。
/// 故登出时把该令牌的 <c>jti</c> 写入分布式缓存黑名单，TTL = 令牌剩余寿命；
/// JWT 校验管线 <c>OnTokenValidated</c> 命中黑名单即拒绝。TTL 到期自动清除（令牌本就过期）。
/// </summary>
public interface ITokenBlacklistService
{
    /// <summary>把 jti 拉黑指定时长（= 令牌剩余寿命，到期自动失效）。</summary>
    Task BlacklistAsync(string jti, TimeSpan ttl);

    /// <summary>该 jti 是否已被拉黑。</summary>
    Task<bool> IsBlacklistedAsync(string jti);
}
