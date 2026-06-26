using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CP6.Core.Services.Common;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Core.Utilities;

/// <summary>
/// JWT Token 生成工具
/// </summary>
public class JwtHelper
{
    /// <summary>
    /// 生成 JWT Token
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="userName">用户名</param>
    /// <param name="secret">密钥（从配置读取）</param>
    /// <param name="issuer">签发者</param>
    /// <param name="audience">接收者</param>
    /// <param name="expireMinutes">过期时间（分钟）</param>
    /// <param name="tenantId">租户 Id（章10）</param>
    /// <param name="jti">JWT 唯一标识（S 类认证加固 T5：登出黑名单按 jti 吊销；null 则自动生成）</param>
    /// <param name="mustChangePassword">强制改密标志（T5：写入 must_change_password claim，T6 中间件据此拦截）</param>
    /// <param name="isPlatformAdmin">平台超管标志（S 类 #5 T1，R1）：仅 true 时写出 is_platform_admin claim（防解析端默认值反向）</param>
    /// <param name="impersonatorId">替身发起者 Id（S 类 #5 impersonation）：仅非 null 时写出 impersonator_id claim</param>
    public static string GenerateToken(
        string userId,
        string userName,
        string secret,
        string issuer,
        string audience,
        int expireMinutes,
        Guid? tenantId = null,
        string? jti = null,
        bool mustChangePassword = false,
        bool isPlatformAdmin = false,
        Guid? impersonatorId = null)
    {
        // 1. 把用户信息放入 Claims（Token 中携带的数据）。tenant_id 供 TenantMiddleware 解析当前租户（章10）
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userName),
            new Claim("tenant_id", (tenantId ?? TenantContext.DefaultTenant).ToString()),
            // S 类认证加固 T5：jti 唯一标识（登出黑名单吊销用）+ 强制改密标志
            new Claim(JwtRegisteredClaimNames.Jti, jti ?? Guid.NewGuid().ToString()),
            new Claim("must_change_password", mustChangePassword ? "true" : "false")
        };

        // S 类 #5 多租户合规（T1）：平台超管 / 替身 claim 仅在 true / 非 null 时写出，
        // 普通用户令牌不变（解析端永不见默认值 claim）。
        if (isPlatformAdmin) claims.Add(new Claim("is_platform_admin", "true"));
        if (impersonatorId.HasValue) claims.Add(new Claim("impersonator_id", impersonatorId.Value.ToString()));

        // 2. 用密钥创建签名凭证
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // 3. 组装 Token
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.Now.AddMinutes(expireMinutes),
            signingCredentials: credentials);

        // 4. 生成 Token 字符串
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// 生成 2FA pending 半登录令牌（S 类 #2 T5）。短命（PendingTokenMinutes，默认 5）+ 自带 jti 用于一次性消费校验。
    /// purpose ∈ {2fa_verify, 2fa_enroll}；存 token_use claim，T6 端点据此守卫（评审#4 邮件不绕过入会）。
    /// </summary>
    public static string GeneratePendingToken(
        string userId,
        Guid tenantId,
        string purpose,
        string pendingJti,
        string secret,
        string issuer,
        string audience,
        int minutes)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("token_use", purpose),
            new Claim(JwtRegisteredClaimNames.Jti, pendingJti)
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.Now.AddMinutes(minutes),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
