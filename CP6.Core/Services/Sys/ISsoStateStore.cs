namespace CP6.Core.Services.Sys;

/// <summary>
/// SSO state/nonce/PKCE 暂态存储（S 类 #3 T3）。
/// 一次性：Create 写 state→SsoState，Consume(state) 取出后立即移除，防重放 + 防 CSRF。
/// 基于 <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>，
/// TTL=<see cref="SsoOptions.StateMinutes"/>（默认 10 分钟）。
/// </summary>
public interface ISsoStateStore
{
    /// <summary>生成 state 并写入暂态；返 state 串供前端跳转 IdP 携带。</summary>
    string Create(Guid tenantId, string nonce, string codeVerifier, string? returnUrl);

    /// <summary>按 state 取回（不消费，回调时先 Get 校验 + 再 Consume）；找不到/已过期返 null。</summary>
    SsoState? Get(string state);

    /// <summary>消费（=移除），保证 state 单次有效。</summary>
    void Consume(string state);
}

/// <summary>SSO 暂态对象（state 命中后的回调所需上下文）。</summary>
public record SsoState(Guid TenantId, string Nonce, string CodeVerifier, string? ReturnUrl);
