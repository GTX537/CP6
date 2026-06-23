# 单点登录（SSO / OIDC）设计 spec — S 类安全合规 #3

> 源：brainstorming 共识（2026-06-22）；**定稿评审 2026-06-23（§11，8 点修订，全部 §1 锚点经 3 个只读 subagent 核验实代码）**。底座 = **#1 认证加固（已落码**：BCrypt + 策略/锁定/审计 + 刷新令牌轮换 + 登出 jti 黑名单 + httpOnly 三 Cookie + CSRF 双提交 + 强制改密 + Sys 授权加固）。**#2 2FA 当前仅有 spec、尚未落码 → 本子项目自洽，不硬依赖 #2**（§11 R1）。本子项目叠加 **CP6 作 SP（服务提供方）消费外部 OIDC IdP** 的单点登录。命名空间 **Sys**，错误码 **E-SEC-02x**，多租户 `BaseTenantEntity`。

## §0 范围

**做（MVP）**：
- CP6 作 **SP**，**OIDC 单协议**（Authorization Code + PKCE），消费外部 IdP（Azure AD/Entra、Google、Okta、Auth0 等均原生支持 OIDC）。
- **按租户配置 IdP**：每租户一行 `Sys_TenantSsoConfig`（Authority/ClientId/ClientSecret/scopes/策略），登录时按租户路由。
- **JIT 自动供给**：首次 SSO 登录按 email claim 匹配租户内 `Sys_User`；无匹配且 `AutoProvision=true` 时自动建用户（默认最小权限角色，可配），记联邦身份链。
- **并存 + 按租户可强制**：SSO 与密码登录并存；租户可置 `Enforced=true` 关闭本租户密码登录（保留 `AllowPasswordFallback` break-glass 应急账号）。
- **SSO 跳过 CP6 2FA**：走 SSO 即视为 IdP 已做强认证（多数企业 IdP 自带 MFA），CP6 不再叠 TOTP/邮件挑战；租户 2FA 强制策略仅对**密码登录**用户生效。**评审 R1：此为天然属性，不硬依赖 #2**——SSO 回调（§4.1）是独立完成路径，从不经过密码登录里的 2FA 分支，故无论 #2 是否落码，SSO 都不触发 2FA；`HandleCallbackAsync` 不引用 `ITwoFactorService`。#2 后落亦无需改 #3。
- SSO 成功后**复用现有会话签发**（`RefreshTokenService.IssueAsync` + `AuthCookieWriter` 三 Cookie），与密码登录完成态完全一致。
- 安全事件审计、ClientSecret 静态加密、租户 SSO 配置管理 UI、SSO 登录前端流、五语 i18n。

**不做（YAGNI）**：
- **SAML 2.0**（XML 签名/断言验证复杂、攻击面大；OIDC 已覆盖绝大多数现代 IdP——留独立大子项目）。
- **CP6 作 IdP**（给第三方发身份，方向相反、独立大项目）。
- **SCIM 自动用户同步/去激活**（JIT + admin 手动管理已够 MVP；留后续增量）。
- **应用级单一 IdP / 应用级兜底**（已选纯按租户）。
- **email 域名 → 租户自动发现**（MVP 由前端输 TenantCode 选租户；留后续）。
- **多 IdP per 租户、per 角色 SSO 策略**（先单 IdP/单租户标量策略）。
- **后台 SLO/前端会话保活以外的登出联动（Single Logout / back-channel logout）**（CP6 登出清本地会话即可；留后续）。

## §1 现状锚点（落码前复核，行号可能微移）

- **登录** `CP6.WebApi/Controllers/Sys/AuthController.cs`：`Login`（按 `request.TenantCode` 定位 → 密码 `BCrypt.Verify` → `_login.EnsureNotLocked`/`RecordFailureAsync` 锁定 → 禁用 E-SEC-003 → `_tenant.CurrentTenantId=user.TenantId` → `BuildAccessToken(user,jti,mustChange)` → `_perm.PrewarmAsync` + 菜单聚合 → `_login.RecordSuccessAsync` + 审计 LoginSuccess → `_refresh.IssueAsync` + `_cookies.WriteAuthCookies` → 返 `{userName,nickName,roleId,menus,mustChangePassword}`，**不返 token**）。私有 `BuildAccessToken(Sys_User,string jti,bool mustChange)`；`ClientIp`/`ClientUa` 私有属性。注入含 `_context/_config/_perm/_tenant/_hasher/_login/_audit/_refresh/_blacklist/_cookies/_sec`。
- **JWT** `CP6.Core/Utilities/JwtHelper.cs`：`GenerateToken(userId,userName,secret,issuer,audience,expireMinutes,Guid? tenantId=null,string? jti=null,bool mustChangePassword=false)`，HmacSha256，claims=NameIdentifier/Name/tenant_id/jti/must_change_password。
- **Cookie** `CP6.Core/Services/Sys/AuthCookieWriter.cs`：常量 `AccessCookie="cp6_at"`/`RefreshCookie="cp6_rt"`(Path=`/api/auth`)/`CsrfCookie="cp6_csrf"`/`RefreshPath="/api/auth"`；`WriteAuthCookies(resp,accessJwt,rawRefresh,csrf)`/`ClearAuthCookies(resp)`/`static NewCsrfToken()`。（注：SSO 回调走 `/api/auth/sso/callback`，在 `RefreshPath="/api/auth"` 前缀下，cp6_rt 可见。）
- **刷新令牌** `CP6.Core/Services/Sys/RefreshTokenService.cs`：`IssueAsync(user, ...)` 发新 refresh（SHA256 哈希存 `Sys_RefreshToken`，返原始串），SSO 完成态直接复用。
- **锁定** `ILoginSecurityService`：`EnsureNotLocked(user)`→ 锁则 `InvalidOperationException("E-SEC-002")`；`RecordFailureAsync(user)`；`RecordSuccessAsync(user,ip)`。SSO 成功走 `RecordSuccessAsync`。
- **审计** `ISecurityAuditService.LogAsync(SecurityEventType,Guid? userId,string? userName,string? requestTenantCode,string? ip,string? ua,string? reason=null)` 写 `Sys_SecurityLog`（BaseTenantEntity），失败不阻断。`SecurityEventType` 枚举现 1~8（LoginSuccess..PermissionDenied）；2FA（#2）追加 9~14。本子项目追加 15~18（§5）。
- **缓存** `IDistributedCache`（Redis 有连接串则用，否则 `AddDistributedMemoryCache`）——jti 黑名单 `CacheTokenBlacklistService` 与 2FA pending/OTP 均用同款。state/nonce/PKCE 复用此后端。
- **配置** `CP6.Core/Services/Sys/SecurityOptions.cs`：根 `SecurityOptions{Password,Lockout,Token,Cookie,Csrf,TwoFactor}`，`builder.Services.Configure<SecurityOptions>(GetSection("Security"))`，服务注入 `IOptions<SecurityOptions>`。本子项目加根 `Sso`（§6）。
- **错误码经 BizException**：Core 服务 `throw new InvalidOperationException("E-SEC-0xx")`，控制器 catch 转 `new BizException(code[,httpStatus])`（∈ `CP6.WebApi.Localization`），`BizExceptionMiddleware` 本地化。
- **当前用户** `(await _perm.GetAsync()).UserId`。**租户** `ITenantContext.CurrentTenantId`（可写）。
- **实体** `Sys_User : BaseTenantEntity`（`UserName` 登录账号 [Required]、`Password` [Required] [MaxLength(200)]、`Email?` [MaxLength(100)]、`RoleId? int`、`Enable`、`DeptId?`、认证加固 7 字段含 `MustChangePassword`）。`Sys_Tenant : BaseEntity`（共享表，`TenantCode`/`TenantName`/`Enable`/`ExpireDate`；`Id` 即 TenantId）。登录期无租户上下文 → 按 `TenantCode` `IgnoreQueryFilters()` 跨租户读。
- **授权** `[RequirePermission(menuKey,action)]`（∈`CP6.Core.Auth`）→ `IPermissionService.HasActionAsync`，无 admin 旁路；权限点需 seed `Sys_MenuAction`+`Sys_RoleAction` 授 RoleId=1（Program.cs 仿 Sys 授权加固块）。
- **i18n seed** 仿 `I18nSecScreenSeed`（`.Items` 接 `Program.cs` i18n `.Concat` 链），五语 ZhCN/ZhTW/En/Ja/Ko；菜单名直接中文。
- **前端** `cp6.web/src`：`api/http.ts`（withCredentials + 非 GET 注 X-CSRF-Token + 401 自动 refresh）、登录态信号 `localStorage 'cp6_authed'`（非 token）、`views/LoginView.vue`（`authApi.login`，先输 TenantCode）、`router/index.ts`（守卫读 cp6_authed + standalone 静态路由 `meta.standalone`）、`api/sys/auth.ts`。
- **现状**：全仓**无任何 OIDC/SAML/OAuth 外部认证**（`AddAuthentication` 仅 JWT bearer + `OnMessageReceived` 从 cp6_at 读，`OnTokenValidated` 做 jti 黑名单，Program.cs L464–502）——SSO 为全新地基，且**采用手写 OIDC code-flow**（HttpClient 换码 + `JwtSecurityTokenHandler` 验 ID Token），不挂 ASP.NET `AddOpenIdConnect` 处理器（与既有纯 JWT bearer/cookie 认证不冲突）。`System.IdentityModel.Tokens.Jwt` v8.17.0 已在 `CP6.Core.csproj`；**评审 R2：`Microsoft.IdentityModel.Protocols.OpenIdConnect`（`ConfigurationManager<OpenIdConnectConfiguration>` discovery/签名键轮换）未引 → 需新增（取 8.x 对齐 JWT 包版本）**。

## §2 数据模型

### §2.1 新实体 `Sys_TenantSsoConfig`（`CP6.Entity/DomainModels/Sys/Sys_TenantSsoConfig.cs`）

每租户一行（`TenantId` 唯一）。继承 `BaseTenantEntity`（行级过滤）；登录期无上下文时按 `TenantId` `IgnoreQueryFilters()` 读。

```csharp
public class Sys_TenantSsoConfig : BaseTenantEntity
{
    /// <summary>IdP 的 OIDC discovery 根（Authority）。运行时拼 {Authority}/.well-known/openid-configuration。</summary>
    [MaxLength(300)][Required] public string Authority { get; set; } = string.Empty;
    /// <summary>OIDC client_id。</summary>
    [MaxLength(200)][Required] public string ClientId { get; set; } = string.Empty;
    /// <summary>OIDC client_secret —— DataProtection 加密后的密文（明文绝不落库/出接口）。</summary>
    [MaxLength(1000)] public string? ClientSecretProtected { get; set; }
    /// <summary>请求 scopes，空格分隔。默认 "openid email profile"。</summary>
    [MaxLength(300)] public string Scopes { get; set; } = "openid email profile";
    /// <summary>email 所在 claim 名（默认 "email"）。</summary>
    [MaxLength(50)] public string EmailClaim { get; set; } = "email";
    /// <summary>是否启用 SSO（false=该租户不可走 SSO）。</summary>
    public bool Enabled { get; set; }
    /// <summary>是否强制 SSO（true=关闭本租户密码登录，AllowPasswordFallback 用户除外）。</summary>
    public bool Enforced { get; set; }
    /// <summary>是否自动供给（JIT 建用户）。默认 true；false=仅预置用户可登。</summary>
    public bool AutoProvision { get; set; } = true;
    /// <summary>JIT 新建用户的默认角色。AutoProvision=true 时**必填**（评审 R5：系统无"最小权限角色"种子约定，RoleId=1 为 admin；为空则该租户 JIT 失败 E-SEC-026，强制管理员显式指定 SSO 新用户角色，防误授高权）。</summary>
    public int? DefaultRoleId { get; set; }
}
```
> 决策：单 IdP/租户用单表单行；多 IdP/多策略留后续抽表（YAGNI）。`ClientSecretProtected` 命名显式标注"已加密"，杜绝误当明文。

### §2.2 `Sys_User` 加 2 字段（`CP6.Entity/DomainModels/Sys/Sys_User.cs`，`MustChangePassword` 后）

```csharp
// ───── S 类 SSO：联邦身份链 + 强制 SSO 例外 ─────
/// <summary>联邦身份 subject（IdP 的 sub claim）；与 ExternalProvider 共同唯一定位。null=本地账号。</summary>
[MaxLength(200)] public string? ExternalSubject { get; set; }
/// <summary>联邦身份提供方（ID Token 的 iss）；防跨 IdP sub 串号。</summary>
[MaxLength(300)] public string? ExternalProvider { get; set; }
/// <summary>强制 SSO 下的密码登录例外（break-glass 应急账号）。默认 false。</summary>
public bool AllowPasswordFallback { get; set; }
```
> JIT 建的用户：`Password` 落**随机不可用哈希**（`_hasher.Hash(Guid.NewGuid().ToString("N")+Guid.NewGuid())`）——满足 `[Required]` 且密码登录恒不可能；`MustChangePassword=false`（SSO 用户不走改密流）。

### §2.3 SSO 暂态（state/nonce/PKCE）— `IDistributedCache`（不建表）

- key `sec:sso:state:{state}`，value = JSON `{ tenantId, nonce, codeVerifier, returnUrl }`，TTL = `Security:Sso:StateMinutes`（默认 10）。
- `state` = `Guid.NewGuid().ToString("N")`（128bit 防 CSRF）；`nonce` 同款（防 ID Token 重放）；`codeVerifier` = 高熵随机串（PKCE，43~128 字符），`code_challenge = BASE64URL(SHA256(codeVerifier))`，`code_challenge_method=S256`。
- **一次性**：回调 `HandleCallbackAsync` 先 `Get` 校验存在（不存在=已用/过期/伪造 → `E-SEC-022`），成功或失败均 `Remove`（防重放）。
- 决策：短命瞬态用缓存（仿 2FA pending/jti 黑名单），不污染库；按 state 隔离每次授权请求。

### §2.4 EF 迁移

`dotnet ef migrations add Sso --project CP6.Core --startup-project CP6.WebApi`（新表 `Sys_TenantSsoConfigs` + `Sys_Users` 加 3 列；无其它新表）。`Sys_TenantSsoConfigs` 带 `TenantId` 行级列 + **`TenantId` 单列唯一索引**（每租户一行）。**评审 R6：无需登记白名单**——`OnModelCreating` 反射批量对"已含 `TenantId` 的索引"本就跳过（`if(idx.Properties.Contains(tenantProp)) continue;`，已核验 L1909），故单列 `(TenantId)` 唯一索引被原样保留，正是所需。

## §3 服务（`CP6.Core/Services/Sys/`）

### §3.1 `ITenantSsoConfigService` — 配置 CRUD + 解析 + 密钥边界

```csharp
public interface ITenantSsoConfigService
{
    Task<Sys_TenantSsoConfig?> GetByTenantIdAsync(Guid tenantId);   // IgnoreQueryFilters（登录期无上下文）
    Task<Sys_TenantSsoConfig?> GetByTenantCodeAsync(string tenantCode);
    Task UpsertAsync(Guid tenantId, SsoConfigInput input);          // 含 ClientSecret 加密（仅当传入新值时覆盖）
    string DecryptClientSecret(Sys_TenantSsoConfig cfg);            // 内部供 SsoService；密钥不出本服务边界
}
```
- ClientSecret 用 `IDataProtectionProvider.CreateProtector("CP6.Sso.ClientSecret")`，`UpsertAsync` 时 `Protect`，`DecryptClientSecret` 时 `Unprotect`。
- **密钥边界（对齐 2FA 评审#7）**：`GetBy*` 返回的实体若要出接口/审计，调用方**必须不外泄** `ClientSecretProtected`（控制器返投影；审计 reason 不带 secret）。
- `UpsertAsync`：ClientSecret 入参为空字符串=不改（保留原密文）；非空=加密覆盖。可选 discovery 探活（拉 `{Authority}/.well-known/openid-configuration` 失败 → `E-SEC-028`）。

### §3.2 `ISsoStateStore` — state/nonce/PKCE 一次性存取（IDistributedCache）

```csharp
public interface ISsoStateStore
{
    string Create(Guid tenantId, string nonce, string codeVerifier, string? returnUrl);  // 返 state
    SsoState? Get(string state);
    void Consume(string state);                                                            // = Remove
}
public record SsoState(Guid TenantId, string Nonce, string CodeVerifier, string? ReturnUrl);
```
仿 2FA `PendingTokenStore`：key `sec:sso:state:{state}`，JSON 序列化，`AbsoluteExpirationRelativeToNow=StateMinutes`。

### §3.3 `ISsoService` — 编排

```csharp
public interface ISsoService
{
    Task<string> BuildAuthorizeUrlAsync(string tenantCode, string redirectUri, string? returnUrl);  // 返完整 authorize URL
    Task<Sys_User> HandleCallbackAsync(string code, string state, string redirectUri);               // 验令牌+映射/JIT，返已登用户
}
```

**`BuildAuthorizeUrlAsync`**：
1. `cfg = GetByTenantCodeAsync(tenantCode)`；null 或 `!Enabled` → `E-SEC-020`。
2. 拉 discovery（`ConfigurationManager<OpenIdConnectConfiguration>` 按 Authority 缓存，自动轮换签名键）取 `authorization_endpoint`/`token_endpoint`/`jwks_uri`/`issuer`。discovery 不可达 → `E-SEC-028`。
3. 生成 `nonce`/`codeVerifier`/`code_challenge`；`state = _state.Create(cfg.TenantId, nonce, codeVerifier, returnUrl)`。
4. 拼 URL：`response_type=code`、`client_id`、`redirect_uri`、`scope=cfg.Scopes`、`state`、`nonce`、`code_challenge`、`code_challenge_method=S256`。

**`HandleCallbackAsync`**：
1. `st = _state.Get(state)`；null → `E-SEC-022`。**先 `_state.Consume(state)`**（无论成败均消费，防重放）。
2. `cfg = GetByTenantIdAsync(st.TenantId)`；null/`!Enabled` → `E-SEC-020`。
3. **换码**：POST `token_endpoint`（HttpClient，超时 `Sso:HttpTimeoutSeconds`）：`grant_type=authorization_code`、`code`、`redirect_uri`、`client_id`、`client_secret=Decrypt(cfg)`、`code_verifier=st.CodeVerifier`。非 2xx/无 `id_token` → `E-SEC-023`。
4. **验 ID Token**：`JwtSecurityTokenHandler.ValidateToken` + `TokenValidationParameters{ ValidIssuer=discovery.Issuer, ValidAudience=cfg.ClientId, IssuerSigningKeys=discovery.SigningKeys, ValidateLifetime=true, ClockSkew=5min }`；校验 `nonce` claim == `st.Nonce`。任一失败 → `E-SEC-024`。
5. **取身份**：`sub`=NameIdentifier/`sub` claim；`email`=cfg.EmailClaim claim；`email_verified`。email 缺失/`email_verified==false` → `E-SEC-025`。
6. **映射（顺序，评审 R3 收紧）**，`_tenant.CurrentTenantId=st.TenantId` 后：
   - a. 按 `(TenantId, ExternalProvider=issuer, ExternalSubject=sub)` 找 → 命中即用（联邦身份已链接）。
   - b. 否则按 `(TenantId, Email)` 找（大小写不敏感）：命中多条 → `E-SEC-029`（邮箱歧义）；命中**唯一**则——该用户 `ExternalSubject` 为空 → 回填 `(ExternalSubject=sub, ExternalProvider=issuer)` 链接后用；**非空且 `(ExternalProvider,ExternalSubject)≠(issuer,sub)`** → `E-SEC-029`（账号已绑别的联邦身份，拒绝静默改绑）。
   - c. 否则（0 命中）：`cfg.AutoProvision==true && cfg.DefaultRoleId!=null` ? JIT 建（`UserName`=email/`preferred_username`、`Email`、`ExternalSubject/Provider`、`RoleId=cfg.DefaultRoleId`、随机密码哈希、`Enable=true`）+ 审计 `SsoUserProvisioned` : `E-SEC-026`（未预置且未开自动供给**或未配 `DefaultRoleId`**——评审 R5：不取隐式"最小权限约定"，必须显式配角色，防误授高权）。
   - （原独立 step d"已绑他 sub 冲突"并入 b，删除。）
7. `!user.Enable` 或租户 `!Enable` → `E-SEC-027`。
8. 返 `user`（**不**在此签发会话；会话签发由控制器统一做，与密码登录完成态对齐）。

> **SSO 不查 2FA**：`HandleCallbackAsync` 不调 `ITwoFactorService`——MFA 委托 IdP。

## §4 流程

### §4.1 SSO 登录（浏览器三段式）
1. 前端（输 TenantCode）→ `GET /api/auth/sso/authorize?tenantCode=&returnUrl=` → 控制器调 `BuildAuthorizeUrlAsync`（`redirectUri = {publicBase}{Sso:CallbackPath}`）→ 返 `{ authorizeUrl }` → 前端 `window.location = authorizeUrl`。
2. IdP 认证（含其自身 MFA）→ 浏览器重定向回 `GET /api/auth/sso/callback?code=&state=`。
3. 控制器：`user = HandleCallbackAsync(code,state,redirectUri)` → **登录完成（同密码登录路径）**：`_tenant.CurrentTenantId=user.TenantId`（HandleCallback 已设）→ `jti=Guid`→`BuildAccessToken(user,jti,mustChange=false)`→ `_perm.PrewarmAsync` → `_login.RecordSuccessAsync(user,ip)` → 审计 `SsoLoginSuccess` → `_refresh.IssueAsync` → `_cookies.WriteAuthCookies` → **302 重定向到** `{frontendBase}{Sso:PostLoginRedirect}`（默认 `/sso/landing`）。失败：catch → 审计 `SsoLoginFailed` → 302 到 `/sso/landing?error={E-SEC-02x}`（前端按码本地化提示）。

### §4.2 前端落地（拿菜单）
SSO 回调是浏览器重定向（非 XHR），无法直接把菜单塞回 JS。新增 `GET /api/auth/profile`（`[Authorize]`，读 cookie 会话，**已核验当前不存在 → 新增；GET 故 CSRF 豁免、`/api/auth` 前缀故 OperLog 跳过**）返 `{userName,nickName,roleId,menus,mustChangePassword}`（与密码 `login` body 同形）。`/sso/landing` 页：有 `error` → 显错回登录页；否则调 `profile` → 置 `cp6_authed` + 路由首页。（`profile` 对密码登录亦无害，可前端刷新自愈用。）
> **评审 R4（SameSite=Strict 健壮性，已核验 cp6_at Path="/"、三 Cookie `SameSite=Same`=默认 Strict）**：此"落地页中转"设计**刻意规避** OIDC 回调下 Strict Cookie 的经典坑——回调以 302 写 Cookie（写 Cookie 与 SameSite 无关，恒生效），但 Cookie 不在那条**跨站**导航链上被消费；待 `/sso/landing` 静态页加载完，由其 JS 发起的 `/api/auth/profile` 是**同站 XHR**（`withCredentials`），Strict 放行 cp6_at。**前提：前端与后端同站**（已是 #1 cookie 认证的既有约束；若未来跨站部署，则全站 cookie 认证均需 `SameSite=None;Secure`，超出本期）。

### §4.3 密码登录的强制 SSO 拦截（AuthController.Login 内）
`request.TenantCode` 定位租户后、返成功前，插：
```
cfg = _ssoConfig.GetByTenantCodeAsync(request.TenantCode)
if (cfg?.Enabled == true && cfg.Enforced && !user.AllowPasswordFallback)
    throw E-SEC-021   // 该租户强制 SSO，请用 SSO 登录
```
位置：在用户解析、`EnsureNotLocked`/密码校验**之后**（确保是合法用户才提示走 SSO；break-glass 用户照常密码登录）。

### §4.4 租户 SSO 配置管理
- `GET /api/sys/sso-config`（`[Authorize]`+`[RequirePermission("sso-config","query")]`）→ 返当前租户配置投影（**不含** `ClientSecretProtected`，仅返 `hasClientSecret:bool`）。
- `PUT /api/sys/sso-config`（`[Authorize]`+`[RequirePermission("sso-config","edit")]`）→ `UpsertAsync`（ClientSecret 空=不改）。作用当前租户 `_tenant.CurrentTenantId`。

## §5 错误码（E-SEC-020~029）+ 审计事件（15~18）

| 码 | 语义 | 抛出点 |
|---|---|---|
| E-SEC-020 | SSO 未配置/未启用 | authorize/callback：cfg null 或 !Enabled |
| E-SEC-021 | 该租户强制 SSO，禁止密码登录 | 密码 Login |
| E-SEC-022 | SSO state 无效/过期/已用（CSRF/重放） | callback |
| E-SEC-023 | 授权码换取令牌失败 | callback token endpoint |
| E-SEC-024 | ID Token 验证失败（签名/iss/aud/exp/nonce） | callback |
| E-SEC-025 | 邮箱缺失或未验证 | callback |
| E-SEC-026 | 用户未预置，且未开自动供给或未配 DefaultRoleId（R5） | callback 映射 c |
| E-SEC-027 | 账号或租户已禁用 | callback |
| E-SEC-028 | SSO 配置无效 / discovery 不可达 | authorize/Upsert |
| E-SEC-029 | 联邦身份冲突（sub 已绑他号 / email 命中多账号） | callback 映射 b/d |

`SecurityEventType` 追加 `SsoLoginSuccess=15`、`SsoLoginFailed=16`、`SsoUserProvisioned=17`、`SsoConfigChanged=18`。**评审 R1：现状枚举仅 1~8（已核验，#2 2FA 未落码 → 9~14 不存在）。SSO 仍占 15~18，刻意保留 9~14 给 #2**——#3 自洽：若 #3 先于 #2 落码，9~14 为无害空缺；#3 实现时只追加 15~18（与 2FA spec 已分配的 9~14 不冲突）。

## §6 配置（`SecurityOptions` 加 `Sso`）

```csharp
public class SsoOptions {
    public int StateMinutes { get; set; } = 10;
    public string CallbackPath { get; set; } = "/api/auth/sso/callback";
    public string PostLoginRedirect { get; set; } = "/sso/landing";    // 前端相对路径
    public int HttpTimeoutSeconds { get; set; } = 15;
    public string? PublicBaseUrl { get; set; }   // 对外回调根；null=从请求 scheme+host 推断
    public string? FrontendBaseUrl { get; set; } // 302 落地根；null=从 Cors:AllowedOrigins[0] 取
}
```
`SecurityOptions` 加根 `public SsoOptions Sso { get; set; } = new();`（已核验现有 Password/Lockout/Token/Cookie/Csrf/TwoFactor 六段，`TwoFactor` 由 2FA T1 已落、本子项目加 `Sso` 第七段）。`appsettings.json` 的 `Security` 段加 `"Sso": { ... }`。**DataProtection（评审 R7）：显式 `builder.Services.AddDataProtection()`**——纯 `AddControllers` 的 Web API 不保证自动注册 `IDataProtectionProvider`（已核验全仓当前无 `AddDataProtection`、无 `IDataProtectionProvider` 用法），ClientSecret 加解密依赖它，故不依赖"默认已注册"（生产持久化密钥环见 §10）。`AddHttpClient` 注册命名 client `"sso"`（超时）。

## §7 授权 / DI / 多租户

- **权限点 seed**（Program.cs，仿 Sys 授权加固块；菜单 114 安全日志/115 字段审计已占 → SSO 用**下一个未占 MenuId**，落码前核对）：菜单 `sso-config`（路径 `/sys/sso-config`，ParentId=100，Icon=Key）；MenuKey 回填 = `RoutePath.Trim('/').Replace('/','-')` → `sys-sso-config`，故 `[RequirePermission("sys-sso-config", …)]`；`Sys_MenuAction` query/edit；`Sys_RoleAction` 授 RoleId=1；补 `Sys_RoleMenu(RoleId=1, MenuId)`。
- **DI**（Program.cs）：`AddScoped<ITenantSsoConfigService>` / `AddScoped<ISsoStateStore>` / `AddScoped<ISsoService>` / `AddHttpClient("sso")` / **`AddDataProtection()`（R7：显式注册，不假设默认）**。SSO 端点入 `AuthController`（复用 `BuildAccessToken`/登录完成助手），故其构造再注入 `ITenantSsoConfigService`/`ISsoService` 两服务。
- **多租户**：`Sys_TenantSsoConfig : BaseTenantEntity` 走全局过滤；登录期（authorize/callback、密码 Login 拦截）无上下文 → `GetByTenantCode/Id` 内 `IgnoreQueryFilters()` 读。**评审 R6：`(TenantId)` 单列唯一索引无需白名单**（反射批量对已含 TenantId 的索引原样跳过，见 §2.4）。

## §8 i18n（五语）+ 前端

### §8.1 i18n seed `I18nSecSsoScreenSeed`（仿 `I18nSecScreenSeed`，接 Program.cs `.Concat`）
- E-SEC-020~029 十码五语（ZhCN/ZhTW/En/Ja/Ko）。
- `sec.event.15~18` 四枚举标签（SSO 登录成功/失败/用户已供给/配置变更）。
- 画面词条：`sec.sso.{loginButton, tenantCodePrompt, redirecting, landingError, settingsTitle, authority, clientId, clientSecret, secretSetHint, scopes, enabled, enforced, autoProvision, defaultRole, save, testConnection}` 等，五语。

### §8.2 前端
- `LoginView.vue`：输 TenantCode 后出"用 SSO 登录"按钮 → 调 `ssoApi.authorize(tenantCode)` → `window.location`。（可选：blur TenantCode 时探测该租户 `Enforced` → 隐藏密码框，仅留 SSO。）
- `views/pms/SsoLandingView.vue`（standalone 静态路由 `/sso/landing`，`meta.standalone`）：解析 `?error` → 显错回登录；否则 `authApi.profile()` → 置 `cp6_authed` + 跳首页。
- `views/pms/SsoConfigView.vue`（系统设置内，`[RequirePermission]` 后端兜底）：表单管理本租户 OIDC 配置；ClientSecret 输入框 placeholder 显"已设置（留空不改）"当 `hasClientSecret`。
- `api/sys/sso.ts`（`authorize/getConfig/setConfig`）+ `api/sys/auth.ts` 加 `profile`；`types/sys/sso.ts`。
- router：`staticRoutes` 加 `/sso/landing`；`viewModules` 加 `/sys/sso-config`。

## §9 测试

### §9.1 单测（各 Task）+ T-QA gstack
- **令牌验证 seam**：`ISsoService` 的 discovery/签名键经一个可注入的 `IOidcDiscovery`（或 `ConfigurationManager` 工厂）抽象，测试注入**本地签名的测试 ID Token + 假 JWKS/issuer**，免真连 IdP。覆盖：签名错/iss 错/aud 错/过期/nonce 不符 → E-SEC-024；email 缺失/未验证 → E-SEC-025。
- `ISsoStateStore`：create→get→consume 一次性（消费后 get 为 null = E-SEC-022 前置）。
- `ITenantSsoConfigService`：ClientSecret 加解密往返；GetBy* 用 IgnoreQueryFilters 跨租户读（双租户 InMemory）。
- 映射矩阵：sub 命中 / email 唯一命中回填链 / email 多命中 E-SEC-029 / AutoProvision 建用户(默认角色+随机哈希+ExternalSubject) / AutoProvision=false 无匹配 E-SEC-026 / 已绑他 sub E-SEC-029 / 禁用 E-SEC-027。
- 密码登录强制拦截：Enforced 普通用户 E-SEC-021；`AllowPasswordFallback` 用户照常登。
- 控制器：callback 成功写三 Cookie + 审计 SsoLoginSuccess + 302；失败 302 带 error 码 + 审计 SsoLoginFailed。
- gstack QA（T-QA）：起后端 + 配一个真实/模拟 OIDC（如本地 mock-oidc 或一次性公共 IdP test app）跑全流程；强制租户密码登录被拒；break-glass 放行；ClientSecret 不出接口（profile/config 响应 + SecurityLog 不含密文）。

## §10 Hardening（本 spec 记录，**不做**；后续增量）
- ClientSecret/TwoFactorSecret 列加密统一到 KMS/托管密钥（现 DataProtection 本地密钥环；生产需持久化密钥环到 Redis/DB/Azure KeyVault 并保护）。
- Single Logout（back-channel / front-channel）联动 IdP 注销。
- SCIM 用户去激活同步（IdP 删人 → CP6 自动停用）。
- email 域名 → 租户自动发现（免输 TenantCode）。
- SAML 2.0 协议支持（独立子项目）。

## §11 定稿评审修订（2026-06-23，8 点；锚点经 3 个只读 subagent 核验实代码）

| # | 发现（证据） | 修订 |
|---|------|------|
| **R1** | `SecurityEventType` 现仅 1~8（已核验 `SecurityEventType.cs`，#2 2FA 未落码 → 9~14 不存在）；spec 头称"底座=#1+#2"、SSO 事件占 15~18 | 头部改"底座=#1(已落码)；#2 仅 spec → #3 自洽不硬依赖 #2"；§0/§5 注明 SSO 仍占 15~18（保留 9~14 给 #2，未落则无害空缺）；"SSO 跳过 2FA"为天然属性（回调独立路径，不引用 `ITwoFactorService`） |
| **R2** | `Microsoft.IdentityModel.Protocols.OpenIdConnect`（`ConfigurationManager<OpenIdConnectConfiguration>` discovery 依赖）未引（已核验 `CP6.Core.csproj` 仅 `System.IdentityModel.Tokens.Jwt` v8.17.0） | §1/§7 标为需新增包（取 8.x 对齐）；§1 注明 SSO 用手写 code-flow，不挂 `AddOpenIdConnect` 处理器 |
| **R3** | §3.3 `HandleCallbackAsync` 映射 6a~6d：step d"已绑他 sub 冲突"不可达（email 命中已有用户但其 `ExternalSubject`≠本次 sub 的冲突未在 b 校验，会静默改绑） | §3.3 step 6 重排：冲突并入 b（唯一 email 命中且 `ExternalSubject` 非空且 `(Provider,Subject)≠(issuer,sub)` → E-SEC-029；为空才回填链接），删独立 step d |
| **R4** | cp6_at `SameSite=Strict`（默认，已核验 `AuthCookieWriter`）与跨站 IdP 302 回调写 Cookie 的交互未论证 | §4.2 论证"落地页中转"规避 Strict 经典坑（写 Cookie 与 SameSite 无关；消费走同站 XHR `/profile`）+ 显式记"前后端同站"约束（#1 既有） |
| **R5** | `DefaultRoleId ?? 系统最小权限角色约定`——"约定"未落码（已核验无最小权限角色种子/常量，RoleId=1=admin），易误授高权 | §2.1/§3.3/§5：去掉隐式约定，`AutoProvision=true` 必配 `DefaultRoleId`，为空则 JIT 失败 E-SEC-026（管理员显式指定 SSO 新用户角色） |
| **R6** | §2.4/§7 称 `Sys_TenantSsoConfigs` 的 TenantId 唯一索引"需登记白名单" | 已核验 `OnModelCreating` L1909 对"已含 TenantId 的索引"原样跳过 → `(TenantId)` 单列唯一索引天然保留，**白名单不需要**；§2.4/§7 删除该指示 |
| **R7** | §6/§7 称 DataProtection"ASP.NET 默认已注册"——已核验全仓无 `AddDataProtection`、无 `IDataProtectionProvider` 用法，纯 `AddControllers` 不保证自动注册 | §6/§7：显式 `AddDataProtection()`（ClientSecret 加解密依赖），不靠默认 |
| **R8** | `/api/auth/profile` 端点确认不存在（新增项，非错误）；菜单 114/115 已被安全日志/字段审计占 → SSO 菜单须用下一未占 MenuId；SSO 端点入 `AuthController` 需补注入 2 服务 | §4.2 标 profile 为新增（GET 故 CSRF 豁免/OperLog 跳过）；§7 SSO 用未占 MenuId + MenuKey 派生 `sys-sso-config` + 补 `Sys_RoleMenu` + AuthController 补注入 `ITenantSsoConfigService`/`ISsoService` |

**核验补强（非问题）**：`AuthController.Login` 全流程 / `BuildAccessToken(Sys_User,string,bool)` / 12 注入服务（含 spec 未列的 `_policy`）/ `JwtHelper.GenerateToken` 九参 + 五 claim / `LogAsync` 七参 / `RefreshTokenService.IssueAsync(user,ip,ua)` / `ILoginSecurityService` 三法（`EnsureNotLocked` 抛 E-SEC-002）/ `AuthCookieWriter` 三 Cookie 常量与 Path / `SecurityOptions` 六段（`TwoFactor` 已落）/ `CsrfMiddleware`（GET 豁免、仅 `/api/auth/login` 豁免）/ `OperLogFilter` 跳过 `/api/auth` / `Sys_User`·`Sys_Tenant` 字段 / `Sys_TenantSsoConfig` 不存在 / 外部认证仅 JWT bearer ——**全部经 subagent 核验属实**。

---

*生成于 2026-06-22；定稿 2026-06-23（§11 八点评审修订，全部 §1 锚点经 3 个只读 subagent 核验实代码）。源 brainstorming 5 决策（SP/OIDC 单协议 · 按租户 IdP · JIT 邮箱匹配最小权限 · 并存+按租户强制+break-glass · SSO 跳过 CP6 2FA）。落码按真实代码锚点（§1）。底座 #1 认证加固（已落码）；#3 自洽不硬依赖 #2 2FA。实施 = subagent-driven，每 Task spec 审+质量审双过 + 先绿后本地 commit（不 push）+ gstack QA。下一步 = 据本定稿撰写实现计划（T1~Tn）。*
