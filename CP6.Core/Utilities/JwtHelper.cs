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
    public static string GenerateToken(
        string userId,
        string userName,
        string secret,
        string issuer,
        string audience,
        int expireMinutes,
        Guid? tenantId = null)
    {
        // 1. 把用户信息放入 Claims（Token 中携带的数据）。tenant_id 供 TenantMiddleware 解析当前租户（章10）
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userName),
            new Claim("tenant_id", (tenantId ?? TenantContext.DefaultTenant).ToString())
        };

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
}
