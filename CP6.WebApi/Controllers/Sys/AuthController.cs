using System.IdentityModel.Tokens.Jwt;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>
/// 登录认证 API。匿名端点逐个 <c>[AllowAnonymous]</c> 标注；
/// 需登录的端点（如 change-password）用 <c>[Authorize]</c>——故类级不再统一放开匿名，
/// 否则类级 AllowAnonymous 会覆盖方法级 Authorize（ASP.NET Core 语义）。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : LocalizedControllerBase
{
    private readonly CP6Context _context;
    private readonly IConfiguration _config;
    private readonly ICurrentPermissionContext _perm;
    private readonly ITenantContext _tenant;
    private readonly IPasswordHasher _hasher;
    private readonly IPasswordPolicyService _policy;
    private readonly ILoginSecurityService _login;
    private readonly ISecurityAuditService _audit;
    private readonly IRefreshTokenService _refresh;
    private readonly ITokenBlacklistService _blacklist;
    private readonly IAuthCookieWriter _cookies;
    private readonly SecurityOptions _sec;
    private readonly ITenantSsoConfigService _ssoConfig;
    private readonly ISsoService _sso;
    private readonly ITwoFactorService _2fa;
    private readonly IPendingTokenStore _pending;

    public AuthController(CP6Context context, IConfiguration config, ICurrentPermissionContext perm, ITenantContext tenant, IPasswordHasher hasher, IPasswordPolicyService policy, ILoginSecurityService login, ISecurityAuditService audit, IRefreshTokenService refresh, ITokenBlacklistService blacklist, IAuthCookieWriter cookies, IOptions<SecurityOptions> sec, ITenantSsoConfigService ssoConfig, ISsoService sso, ITwoFactorService twoFa, IPendingTokenStore pending)
    {
        _context = context;
        _config = config;
        _perm = perm;
        _tenant = tenant;
        _hasher = hasher;
        _policy = policy;
        _login = login;
        _audit = audit;
        _refresh = refresh;
        _blacklist = blacklist;
        _cookies = cookies;
        _sec = sec.Value;
        _ssoConfig = ssoConfig;
        _sso = sso;
        _2fa = twoFa;
        _pending = pending;
    }

    /// <summary>签发 access JWT（短寿命，带 jti + must_change_password）。登录/刷新复用。
    /// S 类 #5 T1：正常登录/刷新传 user.IsPlatformAdmin（平台超管令牌带 is_platform_admin claim）；
    /// impersonation 流程不复用此方法（自带身份切换、单独签发，§T5）。</summary>
    private string BuildAccessToken(Sys_User user, string jti, bool mustChange, bool isPlatformAdmin = false)
    {
        var jwt = _config.GetSection("JWT");
        return JwtHelper.GenerateToken(
            userId: user.Id.ToString(),
            userName: user.UserName,
            secret: jwt["Secret"]!,
            issuer: jwt["Issuer"]!,
            audience: jwt["Audience"]!,
            expireMinutes: _sec.Token.AccessTokenMinutes,
            tenantId: user.TenantId,
            jti: jti,
            mustChangePassword: mustChange,
            isPlatformAdmin: isPlatformAdmin);
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? ClientUa => Request.Headers.UserAgent.ToString();

    /// <summary>
    /// 登录
    /// POST /api/auth/login
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // 1. 章10 §7 登录租户选择器：此刻租户未知（无 JWT），跨租户按名查找用户（IgnoreQueryFilters）。
        //    提供 TenantCode → 先解析租户并缩到该租户内；否则按名跨租户，唯一即放行、同名多租户要求指定租户。
        Sys_User? user;
        if (!string.IsNullOrWhiteSpace(request.TenantCode))
        {
            var code = request.TenantCode.Trim();
            var tenant = await _context.Sys_Tenants
                .FirstOrDefaultAsync(t => t.TenantCode == code);
            if (tenant == null)
                return BadRequest(new { message = Localizer["租户不存在"] });
            if (!tenant.Enable)
                return BadRequest(new { message = Localizer["租户已停用"] });

            user = await _context.Sys_Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.UserName == request.UserName && u.TenantId == tenant.Id);
        }
        else
        {
            var matches = await _context.Sys_Users
                .IgnoreQueryFilters()
                .Where(u => u.UserName == request.UserName)
                .Take(2)   // 只需判定 0 / 1 / 多
                .ToListAsync();

            if (matches.Count > 1)
                // 同名用户跨多个租户 → 要求指定租户编码消歧（不泄露具体是哪些租户）
                return BadRequest(new { message = Localizer["该用户名存在于多个租户，请提供租户编码"], needTenant = true });

            user = matches.FirstOrDefault();
        }

        // 用户不存在：审计 + 统一失败码（防用户名枚举——不区分"用户不存在"与"密码错误"）
        if (user == null)
        {
            await _audit.LogAsync(SecurityEventType.LoginFailed, null, request.UserName, request.TenantCode, ClientIp, ClientUa, "user not found");
            throw new BizException("E-SEC-001");
        }

        // 2. 账户锁定优先：锁定期内即使密码正确也拒
        try
        {
            _login.EnsureNotLocked(user);
        }
        catch (InvalidOperationException)
        {
            await _audit.LogAsync(SecurityEventType.AccountLocked, user.Id, user.UserName, request.TenantCode, ClientIp, ClientUa, "account locked");
            throw new BizException("E-SEC-002");
        }

        // 3. 验证密码（BCrypt 哈希对比）
        if (!_hasher.Verify(request.Password, user.Password))
        {
            await _login.RecordFailureAsync(user);
            // 本次失败若触发锁定记 AccountLocked，否则 LoginFailed；对外仍统一 E-SEC-001（防枚举）
            var evt = user.LockedUntil is { } until && until > DateTime.Now
                ? SecurityEventType.AccountLocked : SecurityEventType.LoginFailed;
            await _audit.LogAsync(evt, user.Id, user.UserName, request.TenantCode, ClientIp, ClientUa, "wrong password");
            throw new BizException("E-SEC-001");
        }

        // 账号被禁用：密码虽对也拒（E-SEC-003，五语 seed 归 T8）+ 审计
        if (!user.Enable)
        {
            await _audit.LogAsync(SecurityEventType.LoginFailed, user.Id, user.UserName, request.TenantCode, ClientIp, ClientUa, "account disabled");
            throw new BizException("E-SEC-003");
        }

        // 未指定 TenantCode 走唯一名命中时，仍要校验该用户的租户未停用
        if (string.IsNullOrWhiteSpace(request.TenantCode))
        {
            var ownTenant = await _context.Sys_Tenants.FirstOrDefaultAsync(t => t.Id == user.TenantId);
            if (ownTenant is { Enable: false })
                return BadRequest(new { message = Localizer["租户已停用"] });
        }

        // #3 SSO（T6）：该租户强制 SSO 时拦截密码登录（break-glass 用户 AllowPasswordFallback=true 例外）。
        //   按用户所属租户取配置（覆盖未带 TenantCode 的唯一名命中路径）；仅在密码已校验通过后判，
        //   不泄露"强制 SSO"信号给未持有有效凭证者。
        var ssoCfg = await _ssoConfig.GetByTenantIdAsync(user.TenantId);
        if (ssoCfg is { Enabled: true, Enforced: true } && !user.AllowPasswordFallback)
        {
            await _audit.LogAsync(SecurityEventType.LoginFailed, user.Id, user.UserName, request.TenantCode, ClientIp, ClientUa, "sso enforced");
            throw new BizException("E-SEC-021");
        }

        // 章10：确定当前请求租户为该用户的租户 → 后续权限聚合/菜单查询按其租户正确作用域
        _tenant.CurrentTenantId = user.TenantId;

        // #2 2FA（T6）：用户已启用 2FA 或租户强制 → 写 pending 半登录态，不签发完整 access/refresh。
        //   purpose=2fa_enroll(强制租户且未启用 → 必须先扫码绑定) / 2fa_verify(已启用 → 输码即可)。
        //   pending 失败/超时/重放交由 /auth/2fa/verify(enroll) + IPendingTokenStore.Consume 守卫。
        var twoFaMode = _2fa.ResolveTenantMode(user.TenantId);
        if (_2fa.IsChallengeRequired(user, twoFaMode))
        {
            var purpose = _2fa.MustEnroll(user, twoFaMode) ? "2fa_enroll" : "2fa_verify";
            var pendingJti = _pending.Create(user.Id, user.TenantId, purpose);
            var jwtCfg = _config.GetSection("JWT");
            var pendingToken = JwtHelper.GeneratePendingToken(
                user.Id.ToString(), user.TenantId, purpose, pendingJti,
                jwtCfg["Secret"]!, jwtCfg["Issuer"]!, jwtCfg["Audience"]!,
                _sec.TwoFactor.PendingTokenMinutes);
            _cookies.WritePendingCookies(Response, pendingToken, AuthCookieWriter.NewCsrfToken());
            await _audit.LogAsync(SecurityEventType.TwoFactorChallenged, user.Id, user.UserName, request.TenantCode, ClientIp, ClientUa, purpose == "2fa_enroll" ? "enroll" : null);
            return Ok(new { twoFactorRequired = true, mustEnroll = purpose == "2fa_enroll" });
        }

        // 3. 生成 JWT Token（带 tenant_id，后续请求由 TenantMiddleware 解析）。
        //    T5：带 jti（登出黑名单吊销用）+ must_change_password claim；access TTL 取
        //    SecurityOptions.Token.AccessTokenMinutes（短令牌 + 刷新令牌轮换，替代旧 120min 长令牌）。
        var jti = Guid.NewGuid().ToString();
        var mustChange = user.MustChangePassword || _policy.IsExpired(user);
        var token = BuildAccessToken(user, jti, mustChange, isPlatformAdmin: user.IsPlatformAdmin);

        // 4. 登录聚合（PUB 章09）：预热权限上下文 + 菜单按全部角色聚合（多角色 RBAC 并集）。
        //    放在记成功画像之前，避免聚合抛错时"已记成功但请求失败"的不一致。
        var profile = await BuildProfileAsync(user, mustChange);

        // 5. 登录确实成功（含预热/菜单均就绪）后才记成功画像 + 审计。
        await _login.RecordSuccessAsync(user, ClientIp);
        await _audit.LogAsync(SecurityEventType.LoginSuccess, user.Id, user.UserName, request.TenantCode, ClientIp, ClientUa);

        // 6. T6 Cookie 化：签发 refresh 令牌 + CSRF 令牌，三者写 httpOnly/双提交 Cookie。
        //    access JWT 不再随 body 返回（防 XSS 读取 localStorage token）。
        var rawRt = await _refresh.IssueAsync(user, ClientIp, ClientUa);
        var csrf = AuthCookieWriter.NewCsrfToken();
        _cookies.WriteAuthCookies(Response, token, rawRt, csrf);

        // 7. 返回用户信息和菜单权限（不含 token；mustChangePassword 供前端守卫跳改密页）
        return Ok(profile);
    }

    /// <summary>
    /// 登录态画像：预热权限上下文 + 按全部角色聚合菜单（并集）→ 返 { userName, nickName, roleId, menus, mustChangePassword }。
    /// 密码 Login 与 SSO 落地 profile 端点共用，保证两路返回结构一致。
    /// </summary>
    private async Task<object> BuildProfileAsync(Sys_User user, bool mustChange)
    {
        var ctx = await _perm.PrewarmAsync(user.Id);
        var roleIds = ctx.RoleIds;
        var menuIds = await _context.Sys_RoleMenus
            .Where(rm => roleIds.Contains(rm.RoleId))
            .Select(rm => rm.MenuId)
            .Distinct()
            .ToListAsync();

        var menus = await _context.Sys_Menus
            .Where(m => menuIds.Contains(m.MenuId) && m.Enable)
            .OrderBy(m => m.OrderNo)
            .Select(m => new { id = m.MenuId, m.MenuName, m.RoutePath, m.Icon, m.ParentId, m.OrderNo } as object)
            .ToListAsync();

        return new
        {
            userName = user.UserName,
            nickName = user.NickName,
            roleId = user.RoleId,
            menus,
            mustChangePassword = mustChange,
            isPlatformAdmin = user.IsPlatformAdmin   // T9 #5：带外平台区入口标志（前端存 cp6_isPlatformAdmin）
        };
    }

    /// <summary>SSO 回调 redirect_uri：authorize/callback 同源计算（PublicBaseUrl 优先，否则本请求 scheme+host），防 open-redirect 且保两端 redirect_uri 字节一致。</summary>
    private string SsoRedirectUri()
        => (string.IsNullOrWhiteSpace(_sec.Sso.PublicBaseUrl)
                ? $"{Request.Scheme}://{Request.Host}"
                : _sec.Sso.PublicBaseUrl).TrimEnd('/') + _sec.Sso.CallbackPath;

    /// <summary>SSO 落地/错误前端基址：FrontendBaseUrl 优先，否则回退 CORS 白名单首项（同站约束见 spec R4）。</summary>
    private string FrontendBase()
        => (_sec.Sso.FrontendBaseUrl
                ?? _config.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault()
                ?? "http://localhost:5173").TrimEnd('/');

    /// <summary>
    /// SSO 授权发起：按租户构建 OIDC authorize URL（discovery + PKCE + state），返前端跳转。
    /// GET /api/auth/sso/authorize?tenantCode=&amp;returnUrl=
    /// </summary>
    [HttpGet("sso/authorize")]
    [AllowAnonymous]
    public async Task<IActionResult> SsoAuthorize([FromQuery] string tenantCode, [FromQuery] string? returnUrl = null)
    {
        try
        {
            var url = await _sso.BuildAuthorizeUrlAsync(tenantCode, SsoRedirectUri(), returnUrl);
            return Ok(new { authorizeUrl = url });
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-020（未配/未启用）/ E-SEC-028（discovery 不可达）
        }
    }

    /// <summary>
    /// SSO 回调：换码 + 验 ID Token + 映射/JIT → 签发会话（复用 Login 的 jti/access/refresh/CSRF Cookie 写入）→ 302 到前端落地屏。
    /// 失败亦 302 落地屏并带 ?error=码（前端本地化），不写 Cookie。GET /api/auth/sso/callback?code=&amp;state=
    /// </summary>
    [HttpGet("sso/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> SsoCallback([FromQuery] string code, [FromQuery] string state)
    {
        var landing = $"{FrontendBase()}{_sec.Sso.PostLoginRedirect}";
        try
        {
            var user = await _sso.HandleCallbackAsync(code, state, SsoRedirectUri());
            _tenant.CurrentTenantId = user.TenantId;   // HandleCallback 已设，防御再设

            var jti = Guid.NewGuid().ToString();
            var token = BuildAccessToken(user, jti, mustChange: false, isPlatformAdmin: user.IsPlatformAdmin);   // SSO 用户不走密码过期/强制改密
            await _login.RecordSuccessAsync(user, ClientIp);
            await _audit.LogAsync(SecurityEventType.SsoLoginSuccess, user.Id, user.UserName, null, ClientIp, ClientUa);

            var rawRt = await _refresh.IssueAsync(user, ClientIp, ClientUa);
            _cookies.WriteAuthCookies(Response, token, rawRt, AuthCookieWriter.NewCsrfToken());
            return Redirect(landing);
        }
        catch (InvalidOperationException ex)
        {
            // 失败不写任何认证 Cookie；审计 + 把错误码透到落地屏由前端本地化。
            await _audit.LogAsync(SecurityEventType.SsoLoginFailed, null, null, null, ClientIp, ClientUa, ex.Message);
            return Redirect($"{landing}?error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    /// <summary>
    /// 登录态画像（SSO 落地屏加载后由同站 XHR 调用拿菜单；亦可作通用 whoami）。需登录。
    /// GET /api/auth/profile
    /// </summary>
    [HttpGet("profile")]
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var uid = (await _perm.GetAsync()).UserId;
        var user = await _context.Sys_Users.FirstAsync(u => u.Id == uid);
        var mustChange = user.MustChangePassword || _policy.IsExpired(user);
        return Ok(await BuildProfileAsync(user, mustChange));
    }

    // ───────── #2 2FA（T6）：4 端点 + ReadPending 守卫 ─────────

    /// <summary>读取 cp6_2fa pending JWT → 校验签名/iss/aud/exp → 校验 pending 存仍在(一次性未消费)+(可选)purpose 匹配 → 加载用户(IgnoreQueryFilters) + 回设 _tenant。</summary>
    private async Task<(Sys_User user, string jti, string purpose)> ReadPendingAsync(string? expectedPurpose = null)
    {
        var raw = Request.Cookies[AuthCookieWriter.PendingCookie];
        if (string.IsNullOrEmpty(raw)) throw new BizException("E-SEC-013");

        var jwtCfg = _config.GetSection("JWT");
        var handler = new JwtSecurityTokenHandler();
        try
        {
            handler.ValidateToken(raw, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtCfg["Issuer"],
                ValidAudience = jwtCfg["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtCfg["Secret"]!))
            }, out var validated);

            var jwt = (JwtSecurityToken)validated;
            var jti = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
            var rec = _pending.Get(jti);
            if (rec == null) throw new BizException("E-SEC-013");
            if (expectedPurpose != null && !string.Equals(rec.Value.purpose, expectedPurpose, StringComparison.Ordinal))
                throw new BizException("E-SEC-013");

            var user = await _context.Sys_Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == rec.Value.userId);
            if (user == null) throw new BizException("E-SEC-013");
            _tenant.CurrentTenantId = user.TenantId;
            return (user, jti, rec.Value.purpose);
        }
        catch (BizException) { throw; }
        catch
        {
            // 签名/aud/iss/exp 任一不通过 → 统一 E-SEC-013
            throw new BizException("E-SEC-013");
        }
    }

    /// <summary>完成 2FA：消费 pending + 写三 Cookie + 记成功画像/审计 + 返档案。verify/enroll 共用。</summary>
    private async Task<object> CompleteLoginAfter2faAsync(Sys_User user, string pendingJti)
    {
        _pending.Consume(pendingJti);
        var jti = Guid.NewGuid().ToString();
        var mustChange = user.MustChangePassword || _policy.IsExpired(user);
        var token = BuildAccessToken(user, jti, mustChange, isPlatformAdmin: user.IsPlatformAdmin);
        var profile = await BuildProfileAsync(user, mustChange);
        await _login.RecordSuccessAsync(user, ClientIp);
        await _audit.LogAsync(SecurityEventType.LoginSuccess, user.Id, user.UserName, null, ClientIp, ClientUa, "2fa");
        var rawRt = await _refresh.IssueAsync(user, ClientIp, ClientUa);
        _cookies.WriteAuthCookies(Response, token, rawRt, AuthCookieWriter.NewCsrfToken());
        _cookies.ClearPendingCookies(Response);
        return profile;
    }

    /// <summary>2FA 入会准备：开新密钥 + 返 otpauthUri + secret（前端渲二维码 + 手输备选）。已 Enabled 抛 E-SEC-017。POST /api/auth/2fa/setup</summary>
    [HttpPost("2fa/setup")]
    [AllowAnonymous]
    public async Task<IActionResult> TwoFactorSetup()
    {
        var (user, _, _) = await ReadPendingAsync();
        string uri;
        try { uri = _2fa.BeginEnrollment(user); }
        catch (InvalidOperationException ex) { throw new BizException(ex.Message); }
        await _context.SaveChangesAsync();
        return Ok(new { otpauthUri = uri, secret = user.TwoFactorSecret });
    }

    /// <summary>2FA 入会确认：验码 → 置 Enabled + 完成登录。purpose 必须为 2fa_enroll。POST /api/auth/2fa/enroll</summary>
    [HttpPost("2fa/enroll")]
    [AllowAnonymous]
    public async Task<IActionResult> TwoFactorEnroll([FromBody] TwoFactorCodeRequest req)
    {
        var (user, jti, _) = await ReadPendingAsync("2fa_enroll");
        if (!await _2fa.ConfirmEnrollmentAsync(user, req.Code))
        {
            await _audit.LogAsync(SecurityEventType.TwoFactorFailed, user.Id, user.UserName, null, ClientIp, ClientUa, "enroll");
            throw new BizException("E-SEC-011");
        }
        await _context.SaveChangesAsync();
        return Ok(await CompleteLoginAfter2faAsync(user, jti));
    }

    /// <summary>2FA 挑战验证：method=email→VerifyEmailOtp / 否则 TOTP。purpose 必须为 2fa_verify。POST /api/auth/2fa/verify</summary>
    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> TwoFactorVerify([FromBody] TwoFactorVerifyRequest req)
    {
        var (user, jti, _) = await ReadPendingAsync("2fa_verify");

        try { _login.EnsureNotLocked(user); }
        catch (InvalidOperationException)
        {
            await _audit.LogAsync(SecurityEventType.AccountLocked, user.Id, user.UserName, null, ClientIp, ClientUa, "2fa locked");
            throw new BizException("E-SEC-002");
        }

        var ok = string.Equals(req.Method, "email", StringComparison.OrdinalIgnoreCase)
            ? await _2fa.VerifyEmailOtpAsync(jti, req.Code)
            : _2fa.VerifyTotp(user, req.Code);

        if (!ok)
        {
            await _login.RecordFailureAsync(user);
            await _audit.LogAsync(SecurityEventType.TwoFactorFailed, user.Id, user.UserName, null, ClientIp, ClientUa, req.Method);
            throw new BizException("E-SEC-011");
        }

        await _audit.LogAsync(SecurityEventType.TwoFactorVerified, user.Id, user.UserName, null, ClientIp, ClientUa, req.Method);
        return Ok(await CompleteLoginAfter2faAsync(user, jti));
    }

    /// <summary>2FA 邮件 OTP 发送（仅 verify 态；入会态调用直接拒 E-SEC-014 评审#4 防绕过）。POST /api/auth/2fa/email-otp</summary>
    [HttpPost("2fa/email-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> TwoFactorEmailOtp()
    {
        var (user, jti, purpose) = await ReadPendingAsync();
        if (purpose == "2fa_enroll") throw new BizException("E-SEC-014");
        try { await _2fa.SendEmailOtpAsync(user, jti); }
        catch (InvalidOperationException ex) { throw new BizException(ex.Message); }  // E-SEC-015/016/018
        return Ok(new { code = 0 });
    }

    // ───────── #2 2FA（T7）§4.6：自助启停（均需登录，当前用户）─────────

    /// <summary>2FA 状态：返 { enabled, tenantMode, canDisable }。canDisable = 已启用 && 租户非强制(mode!=2)。GET /api/auth/2fa/status</summary>
    [HttpGet("2fa/status")]
    [Authorize]
    public async Task<IActionResult> TwoFactorStatus()
    {
        var uid = (await _perm.GetAsync()).UserId;
        var user = await _context.Sys_Users.FirstAsync(u => u.Id == uid);
        var mode = _2fa.ResolveTenantMode(user.TenantId);
        return Ok(new { enabled = user.TwoFactorEnabled, tenantMode = mode, canDisable = user.TwoFactorEnabled && mode != 2 });
    }

    /// <summary>自助入会准备：开新密钥 + 返 otpauthUri + secret。已启用拒 E-SEC-017。POST /api/auth/2fa/setup-self</summary>
    [HttpPost("2fa/setup-self")]
    [Authorize]
    public async Task<IActionResult> TwoFactorSetupSelf()
    {
        var uid = (await _perm.GetAsync()).UserId;
        var user = await _context.Sys_Users.FirstAsync(u => u.Id == uid);
        if (user.TwoFactorEnabled) throw new BizException("E-SEC-017");
        string uri;
        try { uri = _2fa.BeginEnrollment(user); }
        catch (InvalidOperationException ex) { throw new BizException(ex.Message); }
        await _context.SaveChangesAsync();
        return Ok(new { otpauthUri = uri, secret = user.TwoFactorSecret });
    }

    /// <summary>自助入会确认：验码通过 → 置 Enabled。失败 E-SEC-011。POST /api/auth/2fa/enroll-self</summary>
    [HttpPost("2fa/enroll-self")]
    [Authorize]
    public async Task<IActionResult> TwoFactorEnrollSelf([FromBody] TwoFactorCodeRequest req)
    {
        var uid = (await _perm.GetAsync()).UserId;
        var user = await _context.Sys_Users.FirstAsync(u => u.Id == uid);
        if (!await _2fa.ConfirmEnrollmentAsync(user, req.Code))
            throw new BizException("E-SEC-011");
        await _context.SaveChangesAsync();
        return Ok(new { code = 0 });
    }

    /// <summary>自助关闭：验密码 E-SEC-006 → 租户强制拒 E-SEC-019 → 验码(email/totp) E-SEC-011 → 重置。POST /api/auth/2fa/disable-self</summary>
    [HttpPost("2fa/disable-self")]
    [Authorize]
    public async Task<IActionResult> TwoFactorDisableSelf([FromBody] DisableTwoFactorRequest req)
    {
        var uid = (await _perm.GetAsync()).UserId;
        var user = await _context.Sys_Users.FirstAsync(u => u.Id == uid);

        if (!_hasher.Verify(req.CurrentPassword, user.Password))
            throw new BizException("E-SEC-006");
        if (_2fa.ResolveTenantMode(user.TenantId) == 2)
            throw new BizException("E-SEC-019");   // 强制租户不可自助关闭

        var ok = string.Equals(req.Method, "email", StringComparison.OrdinalIgnoreCase)
            ? await _2fa.VerifyEmailOtpAsync("self:" + user.Id, req.Code)
            : _2fa.VerifyTotp(user, req.Code);
        if (!ok) throw new BizException("E-SEC-011");

        await _2fa.ResetAsync(user, "self-disable");
        await _context.SaveChangesAsync();
        await _audit.LogAsync(SecurityEventType.TwoFactorReset, user.Id, user.UserName, null, ClientIp, ClientUa, "self-disable");
        return Ok(new { code = 0 });
    }

    /// <summary>自助发送邮件 OTP（otpKey=self:{uid}，用于 disable-self 的 email 分支）。无邮箱/限流/发送失败 → BizException。POST /api/auth/2fa/email-otp-self</summary>
    [HttpPost("2fa/email-otp-self")]
    [Authorize]
    public async Task<IActionResult> TwoFactorEmailOtpSelf()
    {
        var uid = (await _perm.GetAsync()).UserId;
        var user = await _context.Sys_Users.FirstAsync(u => u.Id == uid);
        try { await _2fa.SendEmailOtpAsync(user, "self:" + user.Id); }
        catch (InvalidOperationException ex) { throw new BizException(ex.Message); }  // E-SEC-015/016/018
        return Ok(new { code = 0 });
    }

    /// <summary>
    /// 自助改密（需登录）。校验旧密码 → 策略 → 历史不可重用 → 旧哈希入历史 + 写新哈希。
    /// POST /api/auth/change-password
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var uid = (await _perm.GetAsync()).UserId;
        var user = await _context.Sys_Users.FirstAsync(u => u.Id == uid);

        // 旧密码核对（控制器层，直接 BizException 经中间件本地化）
        if (!_hasher.Verify(req.CurrentPassword, user.Password))
            throw new BizException("E-SEC-006");

        // 策略 + 历史校验由 Core 服务以 InvalidOperationException(E-SEC 码) 抛出，边界转 BizException
        try
        {
            _policy.Validate(req.NewPassword);
            await _policy.CheckHistoryAsync(uid, req.NewPassword);
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);
        }

        // 旧哈希入历史并裁剪（不 SaveChanges，与下方写入合并一次保存）（审计 T3 叠加）
        await _policy.RecordHistoryAsync(uid, user.Password);
        user.Password = _hasher.Hash(req.NewPassword);
        user.PasswordChangedAt = DateTime.Now;
        user.MustChangePassword = false;
        // 改密后吊销该用户全部刷新令牌：saveChanges:false 入轨，与改密合并一次原子保存
        // （改密成功 ⇔ 旧凭证全失效；若分两次保存则第二次失败会留下"改密了但旧 refresh 仍可续命"的窗口）
        await _refresh.RevokeAllForUserAsync(uid, saveChanges: false);
        await _context.SaveChangesAsync();   // 一次性原子持久化：裁剪 + 新历史 + 用户更新 + 令牌吊销

        // 安全审计（改密成功）
        await _audit.LogAsync(SecurityEventType.PasswordChanged, uid, user.UserName, null, ClientIp, ClientUa);

        return Ok(new { code = 0, message = "OK" });
    }

    /// <summary>
    /// 刷新令牌轮换。从 httpOnly cookie <c>cp6_rt</c> 读原始令牌 → 轮换（吊旧发新 + 重用检测）→
    /// 签发新 access JWT（新 jti）+ 新 CSRF，写三 Cookie + 审计 TokenRefreshed。
    /// POST /api/auth/refresh
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        var raw = Request.Cookies[AuthCookieWriter.RefreshCookie];
        if (string.IsNullOrEmpty(raw))
            throw new BizException("E-SEC-007");
        try
        {
            // RotateAsync 内已由令牌回设 _tenant.CurrentTenantId，后续签发/审计按其租户作用域
            var (newRaw, user) = await _refresh.RotateAsync(raw, ClientIp, ClientUa);
            var jti = Guid.NewGuid().ToString();
            var mustChange = user.MustChangePassword || _policy.IsExpired(user);
            var token = BuildAccessToken(user, jti, mustChange, isPlatformAdmin: user.IsPlatformAdmin);
            var csrf = AuthCookieWriter.NewCsrfToken();
            _cookies.WriteAuthCookies(Response, token, newRaw, csrf);
            await _audit.LogAsync(SecurityEventType.TokenRefreshed, user.Id, user.UserName, null, ClientIp, ClientUa);
            return Ok(new { code = 0, userName = user.UserName, mustChangePassword = mustChange });
        }
        catch (InvalidOperationException ex)
        {
            // 重用检测/过期/无效 → 转 BizException 本地化；重用时审计 TokenReuseDetected
            if (ex.Message == "E-SEC-008")
                await _audit.LogAsync(SecurityEventType.TokenReuseDetected, null, null, null, ClientIp, ClientUa, "refresh token reuse");
            throw new BizException(ex.Message);
        }
    }

    /// <summary>
    /// 登出。自研 JWT 无状态，签名有效即放行 → 把当前 access 的 jti 拉黑（TTL = 令牌剩余寿命，
    /// 到期自动清除）；同时吊销 refresh 令牌。清三 Cookie 在 T6（AuthCookieWriter）接入。
    /// POST /api/auth/logout
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        // 1. access jti 入黑名单：TTL 取令牌剩余寿命（exp 推算），否则兜底一个完整 access 寿命
        var jti = User.FindFirst("jti")?.Value;
        if (!string.IsNullOrEmpty(jti))
        {
            var ttl = TimeSpan.FromMinutes(_sec.Token.AccessTokenMinutes);
            if (long.TryParse(User.FindFirst("exp")?.Value, out var expUnix))
            {
                var remaining = DateTimeOffset.FromUnixTimeSeconds(expUnix) - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero) ttl = remaining;
            }
            await _blacklist.BlacklistAsync(jti, ttl);
        }

        // 2. 吊销 refresh 令牌（cp6_rt 不存在则静默）
        var raw = Request.Cookies[AuthCookieWriter.RefreshCookie];
        if (!string.IsNullOrEmpty(raw))
            await _refresh.RevokeAsync(raw);

        // 3. 清三 Cookie
        _cookies.ClearAuthCookies(Response);

        // 4. 安全审计（登出）
        var uid = (await _perm.GetAsync()).UserId;
        await _audit.LogAsync(SecurityEventType.Logout, uid, User.Identity?.Name, null, ClientIp, ClientUa);

        return Ok(new { code = 0, message = "OK" });
    }
}

/// <summary>改密请求体（当前密码 + 新密码）。</summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>2FA 验码请求（enroll/email-otp 用 method=null）。</summary>
public record TwoFactorCodeRequest(string Code);
/// <summary>2FA 挑战验证请求（method=email|totp 默认 totp）。</summary>
public record TwoFactorVerifyRequest(string Code, string? Method);
/// <summary>自助关闭 2FA 请求（当前密码 + 验证码 + method=email|totp 默认 totp）。</summary>
public record DisableTwoFactorRequest(string CurrentPassword, string Code, string? Method);
