using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Core.Services.Sys;

/// <summary>
/// SSO/OIDC 登录编排（S 类 #3）。CP6 作 SP，走 Authorization Code + PKCE(S256)。
/// T4：<see cref="BuildAuthorizeUrlAsync"/>（discovery + PKCE + state 落库）。
/// T5：<see cref="HandleCallbackAsync"/>（换码 + 验 ID Token 签名/iss/aud/exp/nonce + 映射 6a~c/JIT）。
/// </summary>
public class SsoService : ISsoService
{
    private readonly ITenantSsoConfigService _cfg;
    private readonly ISsoStateStore _state;
    private readonly IOidcDiscovery _disco;
    private readonly ISsoTokenClient _token;
    private readonly CP6Context _db;
    private readonly ISecurityAuditService _audit;
    private readonly ITenantContext _tenant;
    private readonly IPasswordHasher _hasher;

    public SsoService(ITenantSsoConfigService cfg, ISsoStateStore state, IOidcDiscovery disco,
                      ISsoTokenClient token, CP6Context db, ISecurityAuditService audit,
                      ITenantContext tenant, IPasswordHasher hasher)
    {
        _cfg = cfg;
        _state = state;
        _disco = disco;
        _token = token;
        _db = db;
        _audit = audit;
        _tenant = tenant;
        _hasher = hasher;
    }

    public async Task<string> BuildAuthorizeUrlAsync(string tenantCode, string redirectUri, string? returnUrl)
    {
        var cfg = await _cfg.GetByTenantCodeAsync(tenantCode);
        if (cfg == null || !cfg.Enabled)
            throw new InvalidOperationException("E-SEC-020");

        var ep = await _disco.GetAsync(cfg.Authority);   // 不可达 → E-SEC-028

        var nonce = Guid.NewGuid().ToString("N");
        // PKCE code_verifier：32 字节高熵随机 → base64url（43 字符，RFC 7636 区间内）
        var codeVerifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var state = _state.Create(cfg.TenantId, nonce, codeVerifier, returnUrl);

        return $"{ep.AuthorizationEndpoint}?response_type=code"
             + $"&client_id={Uri.EscapeDataString(cfg.ClientId)}"
             + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
             + $"&scope={Uri.EscapeDataString(cfg.Scopes)}"
             + $"&state={state}"
             + $"&nonce={nonce}"
             + $"&code_challenge={challenge}"
             + "&code_challenge_method=S256";
    }

    /// <summary>
    /// OIDC 回调：换码 + 验 ID Token（签名/iss/aud/exp/nonce）+ 映射/JIT。返 <see cref="Sys_User"/>，
    /// 不签发会话（控制器统一做）。任何失败抛 InvalidOperationException("E-SEC-0XX")，state 无论成败均消费防重放。
    /// </summary>
    public async Task<Sys_User> HandleCallbackAsync(string code, string state, string redirectUri)
    {
        // 1. state 校验 + 一次性消费（防 CSRF/重放）。无论后续成败均已 Consume。
        var st = _state.Get(state);
        if (st == null)
            throw new InvalidOperationException("E-SEC-022");
        _state.Consume(state);

        // 2. 取租户 SSO 配置 + discovery 端点。
        var cfg = await _cfg.GetByTenantIdAsync(st.TenantId);
        if (cfg == null || !cfg.Enabled)
            throw new InvalidOperationException("E-SEC-020");
        var ep = await _disco.GetAsync(cfg.Authority);   // 失败 → E-SEC-028

        // 3. 换码：Authorization Code + PKCE → ID Token。失败/缺 token → E-SEC-023。
        var idToken = await _token.ExchangeCodeForIdTokenAsync(
            ep, cfg, _cfg.DecryptClientSecret(cfg), code, st.CodeVerifier, redirectUri);
        if (string.IsNullOrEmpty(idToken))
            throw new InvalidOperationException("E-SEC-023");

        // 4. 验 ID Token：签名 + iss + aud + exp（含 5min 容偏）。MapInboundClaims=false →
        //    claim 类型保持原名 "sub"/"email"/"nonce"/"email_verified"，与 cfg.EmailClaim 一致。
        ClaimsPrincipal principal;
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            principal = handler.ValidateToken(idToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = ep.Issuer,
                ValidateAudience = true,
                ValidAudience = cfg.ClientId,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = ep.SigningKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5),
            }, out _);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("E-SEC-024");
        }
        // nonce 绑定回放校验：ID Token 的 nonce 必须等于本次 state 暂存的 nonce。
        if (principal.FindFirstValue("nonce") != st.Nonce)
            throw new InvalidOperationException("E-SEC-024");

        // 5. 提取 sub / email / email_verified。
        var sub = principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(sub))
            throw new InvalidOperationException("E-SEC-024");
        var email = principal.FindFirstValue(cfg.EmailClaim);
        var emailVerified = principal.FindFirstValue("email_verified");
        if (string.IsNullOrEmpty(email) || string.Equals(emailVerified, "false", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("E-SEC-025");

        // 6. 租户上下文落定 → 后续按租户查询/写入正确作用域。
        _tenant.CurrentTenantId = st.TenantId;

        // 映射（R3 防静默改绑）：
        // a. (TenantId, ExternalProvider=iss, ExternalSubject=sub) 命中 → 直接用。
        var user = await _db.Sys_Users
            .FirstOrDefaultAsync(u => u.ExternalProvider == ep.Issuer && u.ExternalSubject == sub);

        if (user == null)
        {
            // b. 邮箱匹配（不区分大小写）。
            var byEmail = await _db.Sys_Users
                .Where(u => u.Email != null && u.Email.ToLower() == email!.ToLower())
                .Take(2)
                .ToListAsync();

            if (byEmail.Count > 1)
                throw new InvalidOperationException("E-SEC-029");   // 多条邮箱匹配 → 歧义

            if (byEmail.Count == 1)
            {
                var u = byEmail[0];
                if (string.IsNullOrEmpty(u.ExternalSubject))
                {
                    // 空联邦身份 → 回填（首次联邦绑定）。
                    u.ExternalSubject = sub;
                    u.ExternalProvider = ep.Issuer;
                    await _db.SaveChangesAsync();
                    user = u;
                }
                else if (u.ExternalProvider != ep.Issuer || u.ExternalSubject != sub)
                {
                    // 已绑别的 (provider,sub) → 拒绝静默改绑。
                    throw new InvalidOperationException("E-SEC-029");
                }
                else
                {
                    user = u;   // 已绑同一 (provider,sub)（理论上 6a 已命中，稳妥兜底）。
                }
            }
            else
            {
                // c. 0 命中 → JIT 供给（必须 AutoProvision && DefaultRoleId）。
                if (!cfg.AutoProvision || cfg.DefaultRoleId == null)
                    throw new InvalidOperationException("E-SEC-026");

                user = new Sys_User
                {
                    TenantId = st.TenantId,
                    UserName = email!,
                    Email = email,
                    ExternalSubject = sub,
                    ExternalProvider = ep.Issuer,
                    RoleId = cfg.DefaultRoleId,
                    // 随机高熵密码哈希：联邦账号无本地密码，但占位防空登录路径；MustChangePassword=false。
                    Password = _hasher.Hash(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
                    Enable = true,
                    MustChangePassword = false,
                };
                _db.Sys_Users.Add(user);
                await _db.SaveChangesAsync();

                // 审计 JIT 供给（best-effort，失败不阻断）。
                await _audit.LogAsync(SecurityEventType.SsoUserProvisioned, user.Id, user.UserName,
                                      null, null, null, "SSO JIT provisioned");
            }
        }

        // 7. 用户禁用 / 租户停用 → 拒绝。
        if (!user.Enable)
            throw new InvalidOperationException("E-SEC-027");
        var tenant = await _db.Sys_Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == st.TenantId);
        if (tenant == null || !tenant.Enable)
            throw new InvalidOperationException("E-SEC-027");

        // 8. 返用户（不签发会话；控制器统一签 JWT/Cookie）。
        return user;
    }
}
