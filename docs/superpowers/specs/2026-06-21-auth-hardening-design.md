# 认证加固（Auth Hardening）设计 spec

> SaaS 产品化 S 类首批主题"安全合规"的**第一个子项目**（S4 认证安全的核心层）。把现有"明文密码 + 无策略 + 无锁定 + 无会话生命周期"的 JWT 认证升级到企业客户可签约水平：BCrypt 密码哈希 + 密码策略 + 登录锁定 + 安全事件审计（凭证安全核心 A）+ 刷新令牌轮换 + 登出黑名单 + httpOnly Cookie + CSRF（会话令牌生命周期 B）。
>
> 源对话：brainstorming 定稿。方向链：S 类 SaaS 产品化 → 安全合规（S4+S5+S6 拆 5 子项目）→ **#1 认证加固**（首子项目）。决策：MVP=**A+B 整包**、密码迁移=**一次性原地哈希**、认证底座=**方案1 扩展自研认证**。日期 2026-06-21。命名空间 **Sys**（实体 `CP6.Entity/DomainModels/Sys/`，服务 `CP6.Core/Services/Sys/`，错误码 `E-SEC-0xx`）。
>
> 后续子项目（不在本 spec）：#2 2FA(TOTP) · #3 SSO/OIDC · #4 字段级审计回放(S6) · #5 租户合规(S5)。

---

## 0. 目标与范围

**题眼**：现有认证有一处**安全硬伤**——密码明文存储（`AuthController.cs:75` `user.Password != request.Password`，注释自承"生产环境应用哈希"），叠加无密码策略、无防暴破锁定、无登录审计、无刷新/登出/会话失效机制、token 存 localStorage（XSS 面）。任何企业安全审查的"凭证与会话安全"一节都会直接红牌。本子项目把这一层补到可签约水平。

**核心定位**：只动**认证与会话**这一层，**不动** PUB 四粒度权限引擎、不动多租户行级隔离地基、不动业务模块。复用现有 `AuthController`/`Sys_User`/`JwtHelper`/`ITenantContext`/`IDistributedCache` 等成熟资产，叠加安全能力。

### 0.1 纳入 MVP

| 簇 | 能力 | 说明 |
|---|---|---|
| A 凭证安全 | BCrypt 密码哈希 | `BCrypt.Net-Next`；登录改 `Verify`；建/改密码均哈希 |
| A | 一次性原地哈希迁移 | 启动幂等钩子把现有明文就地哈希，现有账号无感 |
| A | 密码策略 | appsettings 驱动：最小长度 / 大小写 / 数字 / 符号 / 有效期 / 近 N 次不可重用 |
| A | 登录锁定（防暴破）| 连续失败计数达阈值锁定 N 分钟；成功重置 |
| A | 安全事件审计 | `Sys_SecurityLog`：登录成败 / 账户锁定 / 登出 / 改密 / 令牌刷新 / 越权拒绝；前端查询页 |
| A | 登录画像字段 | `LastLoginTime` / `LastLoginIp` / `MustChangePassword` |
| B 会话生命周期 | 刷新令牌轮换 | `Sys_RefreshToken`（存哈希）；access 短时 + refresh 长时；轮换 + 重用检测吊链 |
| B | 登出黑名单 | access `jti` 黑名单（`IDistributedCache`），登出即失效 |
| B | httpOnly Cookie 化 | access/refresh/csrf 三 Cookie；JWT 从 Cookie 读；前端弃 localStorage |
| B | CSRF 防护 | 双提交令牌：非 httpOnly `cp6_csrf` cookie ↔ `X-CSRF-Token` 头 |
| B | 强制改密兜底 | `MustChangePassword` 用户除改密/登出外被中间件拦截 |
| 工程护栏 | 操作级权限 + 五语 i18n + 分层测试(InMemory/SQLite) + gstack 真浏览器 QA |

### 0.2 非范围（推迟到后续子项目或后续批次）

- **2FA / TOTP / 短信验证** —— 子项目 #2，本批仅在登录成功后预留"返回 mustChangePassword/后续可插 OTP 分支"的扩展点，不实装。
- **SSO / OIDC / SAML** —— 子项目 #3。
- **字段级变更审计、跨租户访问审计** —— 子项目 #4/#5（S6/S5）。`Sys_SecurityLog` 仅覆盖认证类安全事件，不做全模块字段级回放。
- **按租户的密码/锁定策略** —— 本批策略走**全局 appsettings**；按租户可配（`Sys_TenantSecurityPolicy`）留后续，数据模型不为此预埋表。
- **密码找回 / 邮箱验证码 / 自助重置** —— 需邮件服务，推迟；本批改密=已登录改密 + 管理员重置。
- **设备指纹 / 异地登录告警 / 风控** —— 推迟；`Sys_SecurityLog` 留 IP/UA 字段为将来风控提供数据。
- **OAuth 客户端 / API Key / 开放 API** —— S10，推迟。

---

## 1. 现状与依赖（落码前必读，均已存在；来自勘探）

| 资产 | 位置 | 本子项目用法 |
|---|---|---|
| `AuthController.Login` | `CP6.WebApi/Controllers/Sys/AuthController.cs:37`（`POST /api/auth/login`，`[AllowAnonymous]`）| 改造：BCrypt 校验 + 锁定 + 安全审计 + 签发三 Cookie；新增 refresh/logout/change-password 端点 |
| `LoginRequest` | `CP6.Entity/DTOs/Sys/LoginRequest.cs`（`UserName/Password/TenantCode?`）| 沿用；租户消歧逻辑保留 |
| `JwtHelper.GenerateToken` | `CP6.Core/Utilities/JwtHelper.cs`（HmacSha256，claims 含 `tenant_id`）| 扩展：加 `jti`、`must_change_password` claim、access TTL 取 `Security:Token:AccessTokenMinutes` |
| `app.UseCors("AllowAll")` | `CP6.WebApi/Program.cs`（勘探：允许所有源）| **收紧**：Cookie 化要求 `AllowCredentials` + 显式 origin（不能用 `*`），见 §5.5 |
| JWT 中间件 | `CP6.WebApi/Program.cs:443-459`（`AddJwtBearer`，校验 issuer/audience/lifetime/key）| 加 `OnMessageReceived` 从 `cp6_at` cookie 取 token；加 `jti` 黑名单校验 |
| `appsettings.json` JWT 段 | `CP6.WebApi/appsettings.json:30-36`（Secret/Issuer/Audience/ExpireMinutes）| 新增 `Security` 段（Password/Lockout/Token/Cookie）|
| `Sys_User` | `CP6.Entity/DomainModels/Sys/Sys_User.cs`（`BaseTenantEntity`；`UserName/Password(MaxLen200)/NickName/RoleId/Enable/Email…`）| `Password` 列**复用存 BCrypt 哈希**（60 字符 ≤ 200）；新增 6 字段（§2.1）|
| `Sys_Tenant` | `CP6.Entity/DomainModels/Sys/Sys_Tenant.cs`（`TenantCode` 全局唯一）| 登录按 `TenantCode` 解析租户（沿用）|
| `ITenantContext` | `CP6.Core/Services/Common/ITenantContext.cs`（`CurrentTenantId`；`DefaultTenant=…A1`）| refresh 时由令牌 `TenantId` 回设上下文 |
| `IUserContext` | （`StampAudit` 已用，取当前用户）| 改密/登出取当前用户 |
| 全局查询过滤 + 盖章 | `CP6.Core/EFDbContext/CP6Context.cs:1848-1920`（反射注册 `HasQueryFilter` + `StampTenant`/`StampAudit`）| 新实体继承 `BaseTenantEntity` 自动隔离；refresh 查 `Sys_RefreshToken` 需 `IgnoreQueryFilters`（§5.2 白名单）|
| `OperationLogFilter` | `CP6.WebApi/Filters/OperLogFilter.cs`（请求级日志；**主动跳过 `/api/auth` 防密码泄露**）| 不改；认证类审计由 `Sys_SecurityLog` 独立承担（填补 `/api/auth` 审计盲点）|
| `IDistributedCache` | 已注册（MemoryCache/Redis）| access `jti` 黑名单存储 |
| `BizExceptionMiddleware` | `CP6.WebApi/Middleware/`（统一异常→错误码）| `E-SEC-0xx` 走此通道 |
| `RequirePermissionAttribute` | `CP6.Core/Auth/RequirePermissionAttribute.cs`（403 但不记日志）| 改：拒绝时调 `ISecurityAuditService` 记越权拒绝 |
| 前端登录 | `cp6.web/src/views/LoginView.vue`（token 存 localStorage）| 改：弃 localStorage、靠 Cookie；菜单/档案存 pinia/内存 |
| 前端拦截器 | `cp6.web/src/api/http.ts`（注入 `Authorization` 头；401 跳登录）| 改：`withCredentials`、注入 `X-CSRF-Token`、401 先 refresh 重试 |
| i18n seed 范式 | 仿 `I18nPurScreenSeed` / `A5BudgetFlowSeed`（接入 `Program.cs` 合并链）| `E-SEC` 错误码 + 安全日志画面词条五语 seed |
| 启动幂等迁移范式 | `TenantSeed.EnsureSeeded` / `A5BudgetI18nFix`（`db.Database.Migrate()` 后跑）| `PasswordHashMigrationSeed.EnsureHashed` 照此 |

---

## 2. 数据模型

### 2.1 `Sys_User` 扩展（现有实体加列）

> 决策：**复用 `Password` 列存 BCrypt 哈希**，不新增 `PasswordHash` 列（免列改名 + 数据搬迁；哈希 60 字符 ≤ MaxLen200）。

| 新增字段 | 类型 | 默认 | 用途 |
|---|---|---|---|
| `PasswordChangedAt` | `DateTime?` | null | 密码有效期判定起点 |
| `FailedLoginCount` | `int` | 0 | 连续失败计数 |
| `LockedUntil` | `DateTime?` | null | null=未锁；冻结截止时刻 |
| `LastLoginTime` | `DateTime?` | null | 最后成功登录时刻 |
| `LastLoginIp` | `string?`(64) | null | 最后登录 IP |
| `MustChangePassword` | `bool` | false | 强制改密标志（管理员重置/到期触发）|

### 2.2 新建实体（`CP6.Entity/DomainModels/Sys/`，均 `BaseTenantEntity`）

**`Sys_PasswordHistory`** —— 支撑"近 N 次不可重用"
| 字段 | 类型 | 说明 |
|---|---|---|
| `UserId` | `Guid` | 关联 `Sys_User` |
| `PasswordHash` | `string`(200) | 历史 BCrypt 哈希 |
| `ChangedAt` | `DateTime` | 变更时刻 |

索引：`(UserId, ChangedAt desc)`。写入时裁剪：仅保留每用户最近 `HistoryCount` 条。

**`Sys_RefreshToken`** —— 刷新令牌轮换链
| 字段 | 类型 | 说明 |
|---|---|---|
| `UserId` | `Guid` | 持有者 |
| `TokenHash` | `string`(128) | **存哈希**（SHA-256 of 原值），原值只在 Cookie |
| `ExpiresAt` | `DateTime` | 过期时刻 |
| `RevokedAt` | `DateTime?` | null=有效；非空=已吊销 |
| `ReplacedByTokenHash` | `string?`(128) | 轮换后指向新令牌（重用检测用）|
| `CreatedIp` | `string?`(64) | 签发 IP |
| `UserAgent` | `string?`(256) | 签发 UA |

索引：**`TokenHash` 全局唯一**（SHA-256 天然全局唯一）。⚠️ **必须从"唯一索引自动补 `TenantId` 前缀"机制中显式排除该索引**——否则升级成 `(TenantId, TokenHash)` 复合唯一后，refresh 流程在无 tenant context 时按 `TokenHash + IgnoreQueryFilters` 跨租户查询无法命中单列。落码方式：在 `CP6Context` 唯一索引升级逻辑加例外名单（仿 FK 主键依赖索引的跳过分支），保留 `HasIndex(TokenHash).IsUnique()` 为单列全局唯一；**最低要求**：即便不全局唯一，也必须存在 `TokenHash` 单列（非唯一）索引供跨租户查询，绝不能只依赖 `(TenantId, TokenHash)` 组合索引。

**`Sys_SecurityLog`** —— 认证类安全事件审计
| 字段 | 类型 | 说明 |
|---|---|---|
| `UserId` | `Guid?` | 可空（登录失败时用户名未知/未解析）|
| `UserName` | `string?`(100) | 尝试登录的用户名（原样，便于审计枚举攻击）|
| `RequestTenantCode` | `string?`(64) | **登录请求原始 `TenantCode`**（原样保留，便于审计枚举/租户探测行为）|
| `EventType` | `int`(enum) | 见下 |
| `Reason` | `string?`(256) | 失败原因 / 备注（不含密码）|
| `ClientIp` | `string?`(64) | 客户端 IP |
| `UserAgent` | `string?`(256) | UA |
| `CreatedAt` | `DateTime` | 事件时刻 |

`SecurityEventType` 枚举：`LoginSuccess=1 / LoginFailed=2 / AccountLocked=3 / Logout=4 / PasswordChanged=5 / TokenRefreshed=6 / TokenReuseDetected=7 / PermissionDenied=8`。

> 租户归属：登录失败时由请求 `TenantCode` 解析 `TenantId`；解析不出落 `DefaultTenant`（`…A1`），但**原始 `TenantCode` 始终原样记入 `RequestTenantCode`**——落 DefaultTenant 仅为隔离归属，审计仍能看到攻击者尝试的真实租户码（识别枚举/探测）。其余事件已认证，租户来自上下文。

### 2.3 配置（`appsettings.json` 新增 `Security` 段）

```jsonc
"Security": {
  "Password": {
    "MinLength": 8, "RequireUpper": true, "RequireLower": true,
    "RequireDigit": true, "RequireSymbol": false,
    "ExpiryDays": 0,        // 0 = 永不过期
    "HistoryCount": 3       // 近 3 次不可重用；0 = 不查历史
  },
  "Lockout": { "MaxFailedAttempts": 5, "LockoutMinutes": 15, "ResetCounterMinutes": 15 },
  "Token":   { "AccessTokenMinutes": 15, "RefreshTokenDays": 7 },
  "Cookie":  { "Secure": true, "SameSite": "Strict" }   // Development 覆盖 Secure=false（§6）
}
```

> 本批策略**全局生效**；按租户可配留后续。强类型绑定 `SecurityOptions`（`IOptions<SecurityOptions>` 注入）。

---

## 3. 组件与职责（`CP6.Core/Services/Sys/`）

| 组件 | 接口/实现 | 职责 |
|---|---|---|
| 密码哈希 | `IPasswordHasher` → `BCryptPasswordHasher` | `Hash(plain)` / `Verify(plain,hash)` / `IsHashed(value)`（判 `$2a$/$2b$/$2y$` 前缀 + 60 长）|
| 密码策略 | `IPasswordPolicyService` → `PasswordPolicyService` | `Validate(plain)`（长度/复杂度，违反→`E-SEC-004`）；`CheckHistoryAsync(userId, plain)`（近 N 次重用→`E-SEC-005`）；`IsExpired(user)` |
| 登录安全 | `ILoginSecurityService` → `LoginSecurityService` | `EnsureNotLocked(user)`（锁定→`E-SEC-002`）；`RecordFailureAsync(user)`（计数++，达阈值置 `LockedUntil`）；`RecordSuccessAsync(user, ip)`（清零 + 写 LastLogin）|
| 刷新令牌 | `IRefreshTokenService` → `RefreshTokenService` | `IssueAsync(user, ip, ua)→原值`；`RotateAsync(rawToken, ip, ua)→(新原值,user)`（校验/轮换/重用检测）；`RevokeAsync(rawToken)`；`RevokeAllForUserAsync(userId)` |
| 黑名单 | `ITokenBlacklistService` → `CacheTokenBlacklistService` | `BlacklistAsync(jti, ttl)`；`IsBlacklistedAsync(jti)`（`IDistributedCache`）|
| 安全审计 | `ISecurityAuditService` → `SecurityAuditService` | `LogAsync(eventType, userId?, userName?, ip, ua, reason?)`（**独立 DI scope 写库**，仿 OperLog `WriteLogSafely`，不污染主事务）|
| Cookie 装配 | `IAuthCookieWriter` → `AuthCookieWriter` | `WriteAuthCookies(resp, accessJwt, rawRefresh, csrf)` / `ClearAuthCookies(resp)`（集中 Cookie 属性，避免散落）|
| 控制器 | `AuthController`(扩展) | `login` / `refresh` / `logout` / `change-password` |
| 控制器 | `SecurityLogController` | `GET /api/sys/security-log`（分页查询，管理员）|
| 中间件 | `CsrfMiddleware` | 不安全方法校验双提交令牌 |
| 中间件 | `MustChangePasswordMiddleware` | 强制改密兜底 |

> 设计原则：每个服务单一职责、接口隔离、可独立 TDD。`AuthController` 仅编排，不含安全逻辑细节。

---

## 4. 认证流程

### 4.1 登录 `POST /api/auth/login`（匿名）

> 本流程所有 `ISecurityAuditService.Log(...)` 调用均带请求原始 `TenantCode`（落 `Sys_SecurityLog.RequestTenantCode`，修订 6）。

1. 解析租户：按 `TenantCode`（沿用现逻辑）得 `TenantId`，设上下文。
2. 按 `(TenantId, UserName)` 查用户。**不存在** → `ISecurityAuditService.Log(LoginFailed,"user not found")` → 返回 `E-SEC-001`（统一文案防枚举）。
3. `Enable=false` → 返回 `E-SEC-003`。
4. `ILoginSecurityService.EnsureNotLocked`：`LockedUntil>now` → `Log(LoginFailed,"locked")` → `E-SEC-002`。
5. `IPasswordHasher.Verify(password, user.Password)`：
   - **失败** → `RecordFailureAsync`（`FailedLoginCount++`；达 `MaxFailedAttempts` → `LockedUntil=now+LockoutMinutes` + `Log(AccountLocked)`，否则 `Log(LoginFailed)`）→ 返回 `E-SEC-001`。
   - **成功** → `RecordSuccessAsync`（计数清零、`LockedUntil=null`、`LastLoginTime/Ip`）。
6. 到期/强制改密判定：`MustChangePassword || PasswordPolicyService.IsExpired(user)` → 仍签发会话但响应带 `mustChangePassword=true`（前端引导改密；后端由 `MustChangePasswordMiddleware` 兜底拦截其它端点）。
7. 签发会话：access JWT（含 `jti` + `must_change_password` claim，§5.4）+ `IRefreshTokenService.IssueAsync` + csrf 串 → `IAuthCookieWriter.WriteAuthCookies` 三 Cookie → `Log(LoginSuccess)` → 返回用户档案 + 菜单 + `mustChangePassword` 标志（**token 不进 body**）。

### 4.2 刷新 `POST /api/auth/refresh`（匿名，用 `cp6_rt`）

1. 读 `cp6_rt` cookie；空 → `E-SEC-007`。
2. `IRefreshTokenService.RotateAsync`：按 `SHA256(raw)` **`IgnoreQueryFilters` 跨租户查** `Sys_RefreshToken`（§5.2）。
   - 查无 / 已过期 → `E-SEC-007`。
   - **已吊销**（`RevokedAt!=null`）→ 判盗用：`RevokeAllForUserAsync` 吊销该用户整条链 + `Log(TokenReuseDetected)` → `E-SEC-008`。
   - 有效 → 由令牌 `TenantId` 回设上下文；吊销旧（`RevokedAt=now`、`ReplacedByTokenHash=新哈希`）；签发新 refresh。
3. 重签 access JWT（新 `jti`）+ 新 csrf → 写三 Cookie → `Log(TokenRefreshed)` → 返回精简档案。

### 4.3 登出 `POST /api/auth/logout`（需认证）

吊销当前 refresh（`cp6_rt` → `RevokeAsync`）+ 当前 access `jti` 入黑名单（TTL=access 剩余寿命）+ `ClearAuthCookies` + `Log(Logout)`。

### 4.4 改密 `POST /api/auth/change-password`（需认证）

`{currentPassword, newPassword}`：
1. `Verify(current, user.Password)` 失败 → `E-SEC-006`。
2. `PasswordPolicyService.Validate(new)`（→`E-SEC-004`）+ `CheckHistoryAsync`（→`E-SEC-005`）。
3. `Hash(new)` 入 `user.Password`；`PasswordChangedAt=now`；`MustChangePassword=false`；旧哈希推 `Sys_PasswordHistory`（裁剪到 `HistoryCount`）。
4. `RevokeAllForUserAsync`（强制他端重登）+ `Log(PasswordChanged)`。

### 4.5 管理员重置（扩展 `UserController`）

建用户 / 重置密码：经 `IPasswordHasher.Hash` 存哈希；重置时置 `MustChangePassword=true`、`PasswordChangedAt=now`，并 `RevokeAllForUserAsync` 吊销该用户全部 refresh token（可选把其当前 access `jti` 加黑名单），迫使其带 `must_change_password=true` 的新 token 重登（§5.4）。**消除明文写入路径**（现 `UserController` 直存明文亦修正）。

---

## 5. Cookie / CSRF / JWT 装配

### 5.1 三 Cookie

| Cookie | 内容 | httpOnly | Secure | SameSite | Path |
|---|---|---|---|---|---|
| `cp6_at` | access JWT（短时）| ✅ | 配置 | Strict | `/` |
| `cp6_rt` | refresh 原值（不透明随机 256bit base64url）| ✅ | 配置 | Strict | `/api/auth` |
| `cp6_csrf` | CSRF 双提交令牌（随机）| ❌（前端要读）| 配置 | Strict | `/` |

### 5.2 JWT 从 Cookie 读 + 黑名单 + 跨租户 refresh

- `AddJwtBearer` 的 `Events.OnMessageReceived`：若 `Authorization` 头无 token，则从 `cp6_at` cookie 取（兼容保留头方式便于过渡/测试）。
- `Events.OnTokenValidated`：取 `jti` → `ITokenBlacklistService.IsBlacklistedAsync` 命中 → `context.Fail()`（拒绝）。
- **refresh 跨租户查询白名单**：refresh 发生于无有效 access 时，`ITenantContext` 为默认值，全局查询过滤会漏查他租户令牌 → `RefreshTokenService` 按 `TokenHash` 用 `IgnoreQueryFilters` 查（列入 `IgnoreQueryFilters` 合法用途白名单，与 `OperLogCleanupService` 同级），查到后由令牌 `TenantId` 回设上下文再做后续。

### 5.3 CSRF 中间件

`CsrfMiddleware`：对 `POST/PUT/PATCH/DELETE`，校验 `X-CSRF-Token` 头 == `cp6_csrf` cookie 值，不符 → `E-SEC-010`（403）。位置：`UseAuthentication` 之后、`UseAuthorization` 之前。

**豁免口径（决策，修订 3）**：
- **`/api/auth/login` 豁免**——首次登录前端尚无 `cp6_csrf`，无从带头；靠匿名 + SameSite=Strict 保护。
- **`/api/auth/refresh` 不豁免，也校验 `cp6_csrf`**——`cp6_csrf` 是**非 httpOnly** cookie，即使 `cp6_at` 过期，前端仍能读取并带 `X-CSRF-Token` 头，故 refresh 完全可纳入 CSRF 校验，纵深更稳。仅当**确认同站点部署 + SameSite=Strict**时，豁免 refresh 才可接受——但本设计**默认不豁免 refresh**。

### 5.4 强制改密中间件

`MustChangePasswordMiddleware`：已认证请求若 `MustChangePassword=true`，除白名单（`/api/auth/change-password`、`/api/auth/logout`）外一律 `E-SEC-009`（前端据此跳改密页）。

> **性能口径（决策）**：**不无条件每请求查库**。access JWT 签发时写入 `must_change_password` claim → 中间件**优先读 claim**判定；仅在需要强实时性的场景才回查库。**管理员重置密码 / 到期置位时**：置 `MustChangePassword=true` 后**吊销该用户所有 refresh token**，并**可选将其当前 access `jti` 加黑名单**——使旧 access（claim 仍为 false）尽快失效，迫使用户带新 `must_change_password=true` 的 token 重新登录，claim 与库状态自然一致。

### 5.5 CORS / Cookie / 部署假设（决策，修订 2）

Cookie 化认证对跨域与 CORS 有硬约束，必须明确：

- **前端**：axios `withCredentials = true`（`http.ts`），否则浏览器不带 Cookie。
- **后端 CORS**：必须 `AllowCredentials()`，且 **`AllowOrigins` 不能用 `*`**（带凭证时浏览器禁止通配源）。现状 `app.UseCors("AllowAll")`（勘探报告 `Program.cs`，允许所有源）**必须收紧**为显式来源列表（前端站点 origin，配置化）+ `AllowCredentials`。
- **部署拓扑（MVP 默认）**：**前后端同站点部署**（同域，或经同域反向代理统一 origin）→ `SameSite=Strict` 可行，CSRF 风险最低。
- **未来跨站点部署**：若前后端分属不同站点（不同 eTLD+1），需将 Cookie 改为 **`SameSite=None` + `Secure=true`**（且 CORS 精确放行该前端 origin + AllowCredentials），或改走**同域反向代理**把前端与 API 收拢到同一 origin（推荐，免 SameSite=None 的跨站暴露）。
- `SameSite` 取值由 `Security:Cookie:SameSite` 配置驱动，便于按部署拓扑切换。

---

## 6. 一次性密码迁移

- **EF 迁移 `SecAuthHardening`**：`Sys_User` 加 6 列 + 建 3 张新表（`Sys_PasswordHistory/RefreshToken/SecurityLog`，含 `TenantId` + 索引）。`db.Database.Migrate()` 自动对所有环境生效（仿 A5BudgetI18nFix 套路）。
- **原地哈希钩子** `PasswordHashMigrationSeed.EnsureHashed(db, hasher)`：`db.Database.Migrate()` 后跑（`Program.cs`），扫 `Sys_User`（`IgnoreQueryFilters` 跨租户），凡 `!IPasswordHasher.IsHashed(Password)` 即 `Password=Hash(Password)` 就地重哈希，幂等（已哈希跳过）→ 现有明文 seed 用户无感升级，重复启动安全。
- **本地 QA 与 Secure Cookie**：`Secure=true` 在 `http://localhost:5177/5173` 不发送 Cookie → `Cookie:Secure` 在 Development 覆盖为 `false`（`appsettings.Development.json`），保证 gstack 本地 http 全流程跑通；生产 `true`。
- **⚠️ 不可逆迁移 / 生产备份 + 回滚约束（修订 4）**：原地哈希是**单向不可逆**操作——
  - **生产执行前必须完成数据库全量备份**（前置硬要求，写入部署 runbook）；
  - 迁移后**无法还原任何明文密码**（BCrypt 单向），如需回退只能靠备份恢复或全员重置；
  - **EF `Down()` / 回滚代码严禁尝试把 BCrypt 反哈希为明文**——`Down()` 仅负责删列/删表等结构回退，**绝不触碰 `Password` 列的值**；
  - 启动钩子幂等：已哈希跳过，重复部署不二次哈希、不损坏既有哈希。

---

## 7. 错误处理与错误码

统一走 `BizException` → `BizExceptionMiddleware` → 前端 `http.ts` 拦截器本地化展示。`Sys_SecurityLog` 写入用独立 DI scope，**审计失败不阻断主流程**（仿 OperLog）。

| 错误码 | 含义（中文=自然语言 key）|
|---|---|
| E-SEC-001 | 用户名或密码错误（登录失败统一口径，不区分"用户不存在/密码错"防枚举）|
| E-SEC-002 | 账户已锁定，请稍后再试 |
| E-SEC-003 | 账户已禁用 |
| E-SEC-004 | 密码不符合安全策略 |
| E-SEC-005 | 新密码不能与最近若干次重复 |
| E-SEC-006 | 原密码错误 |
| E-SEC-007 | 刷新令牌无效或已过期 |
| E-SEC-008 | 会话已失效，请重新登录 |
| E-SEC-009 | 请先修改密码 |
| E-SEC-010 | 安全校验失败（CSRF）|

全部五语 seed（ZhCN/ZhTW/En/Ja/Ko），接入 `Program.cs` i18n 合并链。

---

## 8. 测试矩阵（TDD，xUnit；InMemory 为主，迁移/触发器类用 SQLite）

| 组件 | 用例 |
|---|---|
| `BCryptPasswordHasher` | hash≠明文；Verify 成/败；`IsHashed` 判明文 vs 哈希 |
| `PasswordPolicyService` | 逐规则（长度/大写/小写/数字/符号）；历史重用拒绝；到期判定；`HistoryCount=0` 不查 |
| `LoginSecurityService` | 计数累加；达阈值置 `LockedUntil`；成功重置；锁定期内即使密码正确也拒 |
| `RefreshTokenService` | 签发/轮换；过期拒；已吊销拒；**重用检测吊整链**；跨租户 `IgnoreQueryFilters` 查得到 |
| `CacheTokenBlacklistService` | 登出后 `jti` 被拒；TTL 到期后放行 |
| `SecurityAuditService` | 各事件类型/租户/IP 正确落库；写入失败不抛断主流程 |
| `PasswordHashMigrationSeed` | 明文重哈希；已哈希幂等跳过；跨租户覆盖 |
| `AuthController` 集成 | 登录成功置三 Cookie 且 body 无 token；失败统一文案；锁定；到期返回 `mustChangePassword`；改密流；刷新流（轮换后旧 rt 失效）；登出（黑名单生效）|
| `CsrfMiddleware` | 头/cookie 不符 403；login/refresh 豁免；安全方法放行 |
| `MustChangePasswordMiddleware` | 拦其它端点；放行 change-password/logout |

绿色门槛：新增单测全绿 + 全量回归绿（当前基线 943 测 +1skip）+ 前端 type-check / vitest / i18n:check / vite build 全绿。

---

## 9. 任务拆分预览（完整清单转 writing-plans，按自治模式关口先交用户审核再逐任务执行）

| T | 范围 | 依赖 | 提交点 |
|---|---|---|---|
| T1 | `BCrypt.Net-Next` + `IPasswordHasher` + `Sys_User` 6 字段 + EF 迁移 + 原地哈希钩子；login 改 `Verify`（**拔红牌最小闭环**）| — | 1 |
| T2 | `PasswordPolicyService` + `Sys_PasswordHistory` + change-password 端点 | T1 | 1 |
| T3 | `LoginSecurityService`（锁定 + LastLogin）+ `Sys_SecurityLog` + `ISecurityAuditService` + 登录流接审计 | T1 | 1 |
| T4 | `RefreshTokenService` + `Sys_RefreshToken` + refresh 端点 + 重用检测 + `IgnoreQueryFilters` 白名单 | T1 | 1 |
| T5 | `ITokenBlacklistService` + `jti` claim + logout 端点 + JWT `OnTokenValidated` 黑名单校验 | T4 | 1 |
| T6 | Cookie 化（`OnMessageReceived` 读 cookie + `IAuthCookieWriter` 三 Cookie）+ `CsrfMiddleware`(refresh 不豁免) + `MustChangePasswordMiddleware`(claim 优先) + **CORS 收紧**(AllowCredentials+显式 origin) | T4,T5 | 1 |
| T7 | `UserController` 改密/建用户哈希 + 管理员重置（消除明文写入路径）| T1,T2 | 1 |
| T8 | `E-SEC` 错误码五语 seed + `SecurityLogController` + 安全日志菜单 + 权限点 seed | T3 | 1 |
| T9 | 前端：`http.ts` 改造（withCredentials/CSRF/401-refresh）+ `LoginView`（弃 localStorage）+ `ChangePasswordView` + 路由守卫 + `SecurityLogView` + api/types | T6,T8 | 1 |
| T10 | gstack 真浏览器 QA 全流程（登录 / 连续失败锁定 / 改密 / 刷新轮换 / 登出黑名单 / 安全日志查询）| 全部 | — |

> 执行序：T1 先落（拔红牌）→ A 簇（T2/T3）与 B 簇（T4/T5/T6）→ 收口（T7/T8/T9）→ QA（T10）。每 Task 先绿色构建+测试再本地提交（不 push），决策写进 commit message。

---

## 10. 决策定稿（brainstorming 采纳值，落码不再问）

| 决策 | 定稿 |
|---|---|
| 方向链 | S 类 SaaS 产品化 → 安全合规拆 5 子项目 → **#1 认证加固**首做 |
| MVP 边界 | **A+B 整包**（凭证安全核心 + 会话令牌生命周期）|
| 认证底座 | **方案1 扩展自研认证**（不引 ASP.NET Identity，避与 PUB 权限/多租户硬碰）|
| 哈希算法 | **BCrypt**（`BCrypt.Net-Next`）|
| 密码列 | **复用 `Sys_User.Password` 列存哈希**，不新增列 |
| 明文迁移 | **一次性原地哈希启动钩子**（幂等、现有账号无感）|
| 密码策略作用域 | **全局 appsettings**；按租户可配留后续 |
| 刷新令牌存储 | **存哈希**（SHA-256）非原值；轮换 + 重用检测吊链 |
| `Sys_RefreshToken.TokenHash` 索引 | **全局唯一**（显式排除 TenantId 前缀升级）；最低也须单列索引，**不得只靠 `(TenantId,TokenHash)` 组合**（修订 1）|
| refresh 跨租户查询 | **`IgnoreQueryFilters` 白名单**，按 `TokenHash` 单列查，查后由令牌 `TenantId` 回设上下文 |
| token 载体 | **httpOnly Cookie**（access/refresh/csrf 三 Cookie）；前端弃 localStorage；前端 `withCredentials=true` |
| CORS / 部署 | CORS `AllowCredentials` + 显式 origin（**禁 `*`**）；MVP **同站点**部署 → SameSite=Strict；跨站点未来→SameSite=None+Secure 或同域反代（修订 2）|
| CSRF | **双提交令牌**（`cp6_csrf` cookie ↔ `X-CSRF-Token` 头）+ SameSite=Strict；**login 豁免、refresh 不豁免**（cp6_csrf 非 httpOnly，access 过期仍可读，修订 3）|
| 强制改密 | **`must_change_password` claim 优先**（不每请求查库）+ 中间件兜底 + 登录响应 `mustChangePassword` 标志；重置时吊销 refresh + 可选 jti 黑名单（修订 5）|
| 密码迁移可逆性 | **不可逆**：生产前必备份；`Down()` 仅删结构、严禁反哈希明文（修订 4）|
| 安全日志 | `Sys_SecurityLog` 含 **`RequestTenantCode`** 原样记录（审计枚举/租户探测，修订 6）|
| 登录失败口径 | **统一"用户名或密码错误"防枚举** |
| 本地 QA Cookie | **Development 覆盖 `Cookie:Secure=false`**，生产 true |
| 命名空间 | **Sys**（实体/服务/错误码 `E-SEC`）|

---

## 11. i18n / 菜单 / 权限 / 前端清单

- **i18n**：`E-SEC-001~010` + 安全日志画面词条（事件类型枚举标签：登录成功/登录失败/账户锁定/登出/改密/令牌刷新/重用检测/越权拒绝）+ 改密页词条，五语全。
- **菜单**：`SecurityLogView`（安全事件）挂"系统管理"组，与 `OperLogView` 同组（具体菜单号落码核定空位）；权限点 MenuKey 回填对齐 `SecurityLogController` 的 `[RequirePermission]`。
- **前端视图**：`LoginView`（改）、`ChangePasswordView`（新）、`SecurityLogView`（新）；路由 + `MustChangePassword` 守卫；`api/sys/{auth,securityLog}.ts` + `types/sys`。
- **前端会话**：菜单/档案存 pinia（非敏感）；Cookie 由浏览器自动携带；登出调 `/api/auth/logout` 后清本地状态。

---

*生成于 2026-06-21。现状据三份只读勘探（认证/多租户/审计）真实代码盘点。本 spec 为认证加固子项目（S4 核心层）的实现基线；转 writing-plans 出带依赖/提交点的完整任务清单后，先交用户审核再逐任务自治执行。*
