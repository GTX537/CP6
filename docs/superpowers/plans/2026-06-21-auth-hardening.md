# 认证加固（Auth Hardening）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把现有"明文密码 + 无策略 + 无锁定 + 无会话生命周期"的自研 JWT 认证升级到企业可签约水平：BCrypt 哈希 + 密码策略 + 登录锁定 + 安全事件审计（凭证安全核心 A）+ 刷新令牌轮换 + 登出 jti 黑名单 + httpOnly 三 Cookie + CSRF 双提交（会话生命周期 B）。

**Architecture:** 方案1 扩展自研认证——保留 `AuthController`/`Sys_User`/`JwtHelper`/PUB 权限/多租户，叠加 7 个单一职责服务（`CP6.Core/Services/Sys/`）+ 3 新实体（均 `BaseTenantEntity`）+ `Sys_User` 加 7 字段 + 2 中间件 + JWT 从 Cookie 读 + CORS 收紧。安全逻辑全部下沉到可独立 TDD 的服务，`AuthController` 仅编排。命名空间 **Sys**，错误码 **E-SEC-0xx**。

**Tech Stack:** .NET 8 + EF Core 8 / `BCrypt.Net-Next`（新增包）/ `IDistributedCache`（已配，黑名单）/ xUnit + EF Core InMemory + EF Core Sqlite（已引）/ Vue 3.5 + element-plus + vue-i18n + axios。源 spec：`docs/superpowers/specs/2026-06-21-auth-hardening-design.md`（含 6 点审阅修订，commit 4dbfb0b）。

---

## 关键既有约定（落码前必读）

### 现状锚点（勘探实证，落码前复核行号可能微移）
- **登录入口** `CP6.WebApi/Controllers/Sys/AuthController.cs`：`Login` 现 `if (user.Password != request.Password)` 明文对比（L76），返回 `token` 在 body（L122-129）。继承 `LocalizedControllerBase`，有 `Localizer[...]`。租户消歧用 `IgnoreQueryFilters()` 跨租户查 `Sys_Users`（L53-69）。注入 `(CP6Context, IConfiguration, ICurrentPermissionContext _perm, ITenantContext _tenant)`。
- **JWT 生成** `CP6.Core/Utilities/JwtHelper.cs`：`GenerateToken(userId, userName, secret, issuer, audience, expireMinutes, Guid? tenantId=null)`，HmacSha256，claims = `NameIdentifier/Name/tenant_id`。
- **JWT 中间件** `CP6.WebApi/Program.cs:445-458`：`AddJwtBearer`，`TokenValidationParameters` 校验 issuer/audience/lifetime/key。**无 `Events`**。
- **CORS** `Program.cs:462-469`：策略 `"AllowAll"` = `SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials()`——⚠️ **反射任意源 + 带凭证**，不安全，T6 收紧为显式 origin allowlist。
- **缓存** `Program.cs:50-64`：`IDistributedCache`（Redis 有连接串则 Redis，否则 `AddDistributedMemoryCache`）。黑名单直接注入 `IDistributedCache`。
- **中间件管线** `Program.cs:2121-2154`：`UseCors → UseHttpMetrics → UseAuthentication → TenantMiddleware → UseRequestLocalization → BizExceptionMiddleware(L2151) → UseAuthorization(L2153) → MapControllers`。**新中间件 `CsrfMiddleware` / `MustChangePasswordMiddleware` 插在 L2151 之后、L2153 之前**（这样在 `BizExceptionMiddleware` 下游，抛 `BizException` 被捕获并本地化；且 `UseAuthentication` 已在上游，`User` 已填充）。
- **Sys_User** `CP6.Entity/DomainModels/Sys/Sys_User.cs`：`BaseTenantEntity` + `UserName(100)/Password(200)/NickName/RoleId/Enable/DeptId/ManagerId/Email`。

### EF / 索引 / 迁移
- **多租户基类** `BaseTenantEntity`（=`BaseEntity` 的 `Id/Creator/CreateDate/Modifier/ModifyDate` + `TenantId`，**不含** `RowVersion/IsDeleted`）。3 新实体继承它，自动纳入全局查询过滤 + 写入盖章。
- **唯一索引租户前缀自动重写**：`CP6Context.OnModelCreating` 尾部反射循环（约 `CP6Context.cs:1859-1894`）把所有 `BaseTenantEntity` 子类的**唯一索引**自动前缀 `TenantId`，跳过条件 = 索引已含 `TenantId` 或 = FK 主键依赖索引。**`Sys_RefreshToken.TokenHash` 必须保持单列全局唯一**（refresh 无 tenant context 时按 `TokenHash + IgnoreQueryFilters` 查）→ **在该循环加第三个跳过条件**：实体=`Sys_RefreshToken` 且索引唯一列={`TokenHash`} 时 `continue`（保留单列全局唯一，详见 Task T4 Step）。
- **迁移命令**：`dotnet ef migrations add SecAuthHardening --project CP6.Core --startup-project CP6.WebApi`（会先构建；不要带 `--no-build`）。生成后核对新表唯一索引列：`Sys_PasswordHistory`/`Sys_SecurityLog` 的逻辑唯一索引含 `TenantId` 前缀；**`Sys_RefreshToken.TokenHash` 不带 `TenantId` 前缀**（验证跳过条件生效）。
- **DbSet/索引** 在 `CP6Context.cs`：加 `DbSet<Sys_PasswordHistory>`/`DbSet<Sys_RefreshToken>`/`DbSet<Sys_SecurityLog>` + `OnModelCreating` 索引声明。

### 服务 / DI / 控制器 / 测试
- **错误码经 `BizException`**：`throw new BizException("E-SEC-00x")` → `BizExceptionMiddleware` → 本地化（中文 key = 自然语言，由 i18n seed 提供五语）。沿用 Pur/Fin 既有用法（搜 `throw new BizException("E-PUR` 见范本）。
- **强类型配置** `SecurityOptions`：`builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"))`，服务注入 `IOptions<SecurityOptions>`。
- **DI 插入点** `Program.cs` Sys 服务注册区（搜现有 `AddScoped<ICurrentPermissionContext` 附近）：7 服务 + `Configure<SecurityOptions>`。
- **菜单 MenuKey 自动派生**（`Program.cs` 约 L607-612）：`MenuKey = RoutePath.Trim('/').Replace('/','-')`。`SecurityLogView` 路由 `/sys/security-log` → MenuKey `sys-security-log` 自动对齐 `[RequirePermission("sys-security-log","query")]`。
- **i18n 五语 seed**：仿 `CP6.WebApi/Seed/I18nA3ScreenSeed.cs`（ZhCN/ZhTW/En/Ja/Ko），接 `Program.cs` 的 i18n `.Concat(...)` 合并链。新词须真起后端 + `npm run i18n:pull` 重建快照 + `i18n:gen-types`（QA 阶段，T10）。
- **测试基建**：`TestHelper.CreateInMemoryContext()` = `new CP6Context(UseInMemoryDatabase(Guid))` 默认租户。`IOptions` 用 `Microsoft.Extensions.Options.Options.Create(new SecurityOptions{...})`。结构/跨租户/索引类测试走 SQLite harness：
  ```csharp
  using var conn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
  conn.Open();
  var options = new DbContextOptionsBuilder<CP6Context>().UseSqlite(conn).Options;
  using var db = new CP6Context(options);
  db.Database.EnsureCreated();
  ```
  ⚠️ InMemory 不强制唯一索引 / 不真隔离；**跨租户 `IgnoreQueryFilters` 与全局唯一索引的真实行为测试必须走 SQLite**；InMemory 共享上下文会被 EF 导航修复掩盖，单测须 `db.ChangeTracker.Clear()` 才真红。
- **审计日志**：全局 `OperLogFilter` 自动记录 POST/PUT/DELETE，但**主动跳过 `/api/auth`**（防密码泄露）→ 故认证类审计必须由 `Sys_SecurityLog` 独立承担（本计划新增）。

### 关键类型签名（跨 Task 一致，勿改名）
```csharp
// SecurityOptions（强类型配置根）
public class SecurityOptions {
    public PasswordPolicyOptions Password { get; set; } = new();
    public LockoutOptions Lockout { get; set; } = new();
    public TokenOptions Token { get; set; } = new();
    public AuthCookieOptions Cookie { get; set; } = new();
}
public class PasswordPolicyOptions { public int MinLength {get;set;}=8; public bool RequireUpper{get;set;}=true; public bool RequireLower{get;set;}=true; public bool RequireDigit{get;set;}=true; public bool RequireSymbol{get;set;} public int ExpiryDays{get;set;} public int HistoryCount{get;set;}=3; }
public class LockoutOptions { public int MaxFailedAttempts{get;set;}=5; public int LockoutMinutes{get;set;}=15; public int ResetCounterMinutes{get;set;}=15; }
public class TokenOptions { public int AccessTokenMinutes{get;set;}=15; public int RefreshTokenDays{get;set;}=7; }
public class AuthCookieOptions { public bool Secure{get;set;}=true; public string SameSite{get;set;}="Strict"; }

public interface IPasswordHasher { string Hash(string plain); bool Verify(string plain, string hash); bool IsHashed(string value); }
public interface IPasswordPolicyService { void Validate(string plain); Task CheckHistoryAsync(Guid userId, string plain); bool IsExpired(Sys_User user); }
public interface ILoginSecurityService { void EnsureNotLocked(Sys_User user); Task RecordFailureAsync(Sys_User user); Task RecordSuccessAsync(Sys_User user, string? ip); }
public interface IRefreshTokenService { Task<string> IssueAsync(Sys_User user, string? ip, string? ua); Task<(string newToken, Sys_User user)> RotateAsync(string rawToken, string? ip, string? ua); Task RevokeAsync(string rawToken); Task RevokeAllForUserAsync(Guid userId); }
public interface ITokenBlacklistService { Task BlacklistAsync(string jti, TimeSpan ttl); Task<bool> IsBlacklistedAsync(string jti); }
public interface ISecurityAuditService { Task LogAsync(SecurityEventType type, Guid? userId, string? userName, string? requestTenantCode, string? ip, string? ua, string? reason = null); }
public interface IAuthCookieWriter { void WriteAuthCookies(HttpResponse resp, string accessJwt, string rawRefresh, string csrf); void ClearAuthCookies(HttpResponse resp); }
```

---

## File Structure

### 新建 — 实体（`CP6.Entity/DomainModels/Sys/`）
- `Sys_PasswordHistory.cs`、`Sys_RefreshToken.cs`、`Sys_SecurityLog.cs`、`SecurityEventType.cs`(enum)

### 修改 — 实体
- `Sys/Sys_User.cs`（加 7 字段：`PasswordChangedAt`/`FailedLoginCount`/`LastFailedLoginAt`/`LockedUntil`/`LastLoginTime`/`LastLoginIp`/`MustChangePassword`）

### 新建 — 配置 / 服务（`CP6.Core/Services/Sys/`）
- `SecurityOptions.cs`（4 子类 + 根）
- `IPasswordHasher.cs` / `BCryptPasswordHasher.cs`
- `IPasswordPolicyService.cs` / `PasswordPolicyService.cs`
- `ILoginSecurityService.cs` / `LoginSecurityService.cs`
- `IRefreshTokenService.cs` / `RefreshTokenService.cs`
- `ITokenBlacklistService.cs` / `CacheTokenBlacklistService.cs`
- `ISecurityAuditService.cs` / `SecurityAuditService.cs`
- `IAuthCookieWriter.cs` / `AuthCookieWriter.cs`

### 修改 — 服务 / 工具
- `CP6.Core/Utilities/JwtHelper.cs`（加 `jti` + `must_change_password` claim 形参）
- `CP6.WebApi/Controllers/Sys/AuthController.cs`（login 改造 + refresh/logout/change-password 端点）
- `CP6.WebApi/Controllers/Sys/UserController.cs`（建/改密哈希 + 管理员重置）

### 新建 — 控制器 / 中间件 / Seed
- `CP6.WebApi/Controllers/Sys/SecurityLogController.cs`
- `CP6.WebApi/Middleware/CsrfMiddleware.cs`
- `CP6.WebApi/Middleware/MustChangePasswordMiddleware.cs`
- `CP6.WebApi/Seed/PasswordHashMigrationSeed.cs`（启动幂等原地哈希）
- `CP6.WebApi/Seed/I18nSecScreenSeed.cs`（五语）

### 修改 — 装配
- `CP6.Core/EFDbContext/CP6Context.cs`（3 DbSet + 索引 + TokenHash 全局唯一跳过条件）
- `CP6.WebApi/Program.cs`（`Configure<SecurityOptions>` + 7 DI + JWT `Events` + CORS 收紧 + 2 中间件注册 + 迁移钩子调用 + 菜单/权限/i18n seed）
- `CP6.WebApi/appsettings.json`（`Security` 段）+ `appsettings.Development.json`（`Cookie:Secure=false`）
- `CP6.Core/CP6.Core.csproj`（`BCrypt.Net-Next` 包）

### 新建 / 修改 — 前端（`cp6.web/src/`）
- `api/http.ts`（withCredentials + X-CSRF-Token + 401-refresh）
- `views/LoginView.vue`（弃 localStorage token）、`views/sys/ChangePasswordView.vue`、`views/sys/SecurityLogView.vue`
- `api/sys/auth.ts`、`api/sys/securityLog.ts`、`types/sys/security.ts`
- `router/index.ts`（路由 + MustChangePassword 守卫）

### 新建 — 测试（`CP6.Tests/Sys/`）
- `PasswordHasherTests.cs`、`PasswordPolicyServiceTests.cs`、`LoginSecurityServiceTests.cs`、`RefreshTokenServiceTests.cs`、`TokenBlacklistServiceTests.cs`、`SecurityAuditServiceTests.cs`、`PasswordHashMigrationSeedTests.cs`、`AuthControllerIntegrationTests.cs`、`SecurityMiddlewareTests.cs`、`RefreshTokenSqliteTests.cs`

---

## Phases / Tasks 总览

| T | 范围 | 依赖 | 提交点 |
|---|---|---|---|
| T1 | `BCrypt.Net-Next` + `SecurityOptions` + `IPasswordHasher` + `Sys_User` 7 字段 + EF 迁移 + 原地哈希钩子；login 改 `Verify`（**拔红牌最小闭环**）| — | 1 |
| T2 | `PasswordPolicyService` + `Sys_PasswordHistory` + change-password 端点 | T1 | 1 |
| T3 | `LoginSecurityService`（锁定 + LastLogin）+ `Sys_SecurityLog` + `SecurityAuditService` + 登录流接审计 | T1 | 1 |
| T4 | `RefreshTokenService` + `Sys_RefreshToken`（TokenHash 全局唯一）+ refresh 端点 + 重用检测 | T1 | 1 |
| T5 | `CacheTokenBlacklistService` + `jti` claim + logout 端点 + JWT `OnTokenValidated` 黑名单校验 | T4 | 1 |
| T6 | Cookie 化（`OnMessageReceived` 读 cookie + `AuthCookieWriter` 三 Cookie）+ `CsrfMiddleware`（refresh 不豁免）+ `MustChangePasswordMiddleware`（claim 优先）+ CORS 收紧 | T4,T5 | 1 |
| T7 | `UserController` 改密/建用户哈希 + 管理员重置（消除明文写入路径）| T1,T2 | 1 |
| T8 | `E-SEC` 错误码五语 seed + `SecurityLogController` + 安全日志菜单 + 权限点 | T3 | 1 |
| T9 | 前端：`http.ts` 改造 + `LoginView` + `ChangePasswordView` + 路由守卫 + `SecurityLogView` + api/types | T6,T8 | 1 |
| T10 | gstack 真浏览器 QA 全流程 | 全部 | — |

> 每 Task 先绿色构建+全量测试再本地 `git commit`（**不 push**），决策写进 commit message。执行序 T1 先（拔红牌）→ A 簇(T2/T3) ‖ B 簇(T4/T5/T6) → 收口(T7/T8/T9) → QA(T10)。
> ⚠️ **T6 与 T9 co-dependent**：T6 启用 CSRF 强制后，所有写请求需 `X-CSRF-Token` 头（T9 前端注入）。CSRF 中间件读配置开关 `Security:Csrf:Enabled`（默认 true），T6 单测用 `TestServer`，**前后端联调与默认开启留到 T9/T10 一起落**，避免中途 403 阻断其它模块。

---

## Task T1：拔红牌最小闭环（BCrypt + 字段 + 迁移 + 原地哈希）

**Files:**
- Modify: `CP6.Core/CP6.Core.csproj`（加包）
- Create: `CP6.Core/Services/Sys/SecurityOptions.cs`、`IPasswordHasher.cs`、`BCryptPasswordHasher.cs`
- Modify: `CP6.Entity/DomainModels/Sys/Sys_User.cs`（7 字段）
- Create: `CP6.WebApi/Seed/PasswordHashMigrationSeed.cs`
- Modify: `CP6.WebApi/Controllers/Sys/AuthController.cs`（login 用 Verify）、`CP6.WebApi/Program.cs`（DI + Configure + 钩子调用）、`appsettings.json`、`appsettings.Development.json`
- Test: `CP6.Tests/Sys/PasswordHasherTests.cs`、`PasswordHashMigrationSeedTests.cs`

- [ ] **Step 1: 加 BCrypt 包**

`CP6.Core/CP6.Core.csproj` 的 `<ItemGroup>` 加：
```xml
<PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
```
Run: `dotnet restore CP6.Core`，Expected: 还原成功。

- [ ] **Step 2: 写 hasher 失败测试**

`CP6.Tests/Sys/PasswordHasherTests.cs`：
```csharp
using CP6.Core.Services.Sys;
using Xunit;

namespace CP6.Tests.Sys;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _h = new BCryptPasswordHasher();

    [Fact] public void Hash_is_not_plaintext_and_verifies()
    {
        var hash = _h.Hash("S3cret!23");
        Assert.NotEqual("S3cret!23", hash);
        Assert.True(_h.Verify("S3cret!23", hash));
        Assert.False(_h.Verify("wrong", hash));
    }

    [Theory]
    [InlineData("plainText", false)]
    [InlineData("$2a$11$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUV0123456", true)]
    public void IsHashed_detects_bcrypt_format(string value, bool expected)
        => Assert.Equal(expected, _h.IsHashed(value));
}
```
Run: `dotnet test CP6.Tests --filter PasswordHasherTests`，Expected: 编译失败（类型不存在）。

- [ ] **Step 3: 实现 SecurityOptions + hasher**

`CP6.Core/Services/Sys/SecurityOptions.cs`：粘贴"关键类型签名"中的 `SecurityOptions` 及 4 子类（命名空间 `CP6.Core.Services.Sys`）。

`CP6.Core/Services/Sys/IPasswordHasher.cs`：
```csharp
namespace CP6.Core.Services.Sys;
public interface IPasswordHasher { string Hash(string plain); bool Verify(string plain, string hash); bool IsHashed(string value); }
```
`CP6.Core/Services/Sys/BCryptPasswordHasher.cs`：
```csharp
namespace CP6.Core.Services.Sys;

public class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string plain) => BCrypt.Net.BCrypt.HashPassword(plain, workFactor: 11);
    public bool Verify(string plain, string hash) { try { return BCrypt.Net.BCrypt.Verify(plain, hash); } catch { return false; } }
    // BCrypt 哈希固定 60 字符，前缀 $2a$/$2b$/$2y$
    public bool IsHashed(string value)
        => !string.IsNullOrEmpty(value) && value.Length == 60
           && (value.StartsWith("$2a$") || value.StartsWith("$2b$") || value.StartsWith("$2y$"));
}
```
Run: `dotnet test CP6.Tests --filter PasswordHasherTests`，Expected: PASS。

- [ ] **Step 4: Sys_User 加 7 字段**

`Sys/Sys_User.cs` 末尾（`Email` 后）加：
```csharp
    // ───── S 类认证加固：密码安全 + 登录画像 ─────
    /// <summary>最后改密时间（密码有效期判定起点）</summary>
    public DateTime? PasswordChangedAt { get; set; }
    /// <summary>连续登录失败计数</summary>
    public int FailedLoginCount { get; set; }
    /// <summary>最后一次失败时刻（ResetCounterMinutes 滑动重置用）</summary>
    public DateTime? LastFailedLoginAt { get; set; }
    /// <summary>锁定截止（null=未锁）</summary>
    public DateTime? LockedUntil { get; set; }
    /// <summary>最后成功登录时刻</summary>
    public DateTime? LastLoginTime { get; set; }
    /// <summary>最后登录 IP</summary>
    [MaxLength(64)] public string? LastLoginIp { get; set; }
    /// <summary>强制改密标志</summary>
    public bool MustChangePassword { get; set; }
```
> 说明：相对 spec §2.1 的 6 字段，**新增 `LastFailedLoginAt`** 以落地 `ResetCounterMinutes` 滑动重置（spec 列了该配置但未给追踪字段）——这是计划级补全。

- [ ] **Step 5: 生成迁移**

Run: `dotnet ef migrations add SecAuthHardening --project CP6.Core --startup-project CP6.WebApi`
（本 Task 仅含 `Sys_User` 7 列；T2/T3/T4 的新表在各自 Task 追加迁移或本迁移内一并加——**决策：3 新表实体在 T2/T3/T4 创建后，统一在 T4 末尾补一支迁移 `SecAuthTables`**，避免本步反复改迁移。本步迁移只含 7 列。）
Expected: 生成 `*_SecAuthHardening.cs`，含 `Sys_User` 7 个 `AddColumn`。

- [ ] **Step 6: 写迁移钩子失败测试**

`CP6.Tests/Sys/PasswordHashMigrationSeedTests.cs`：
```csharp
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Xunit;

namespace CP6.Tests.Sys;

public class PasswordHashMigrationSeedTests
{
    [Fact] public void Rehashes_plaintext_and_skips_already_hashed()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var hasher = new BCryptPasswordHasher();
        db.Sys_Users.Add(new Sys_User { UserName = "plainuser", Password = "admin123" });
        var already = hasher.Hash("kept");
        db.Sys_Users.Add(new Sys_User { UserName = "hasheduser", Password = already });
        db.SaveChanges();

        var changed = PasswordHashMigrationSeed.EnsureHashed(db, hasher);

        var p = db.Sys_Users.Single(u => u.UserName == "plainuser");
        var h = db.Sys_Users.Single(u => u.UserName == "hasheduser");
        Assert.Equal(1, changed);                       // 只哈希 1 条
        Assert.True(hasher.Verify("admin123", p.Password));
        Assert.Equal(already, h.Password);              // 已哈希原样保留
        Assert.Equal(0, PasswordHashMigrationSeed.EnsureHashed(db, hasher)); // 幂等
    }
}
```
Run: `dotnet test CP6.Tests --filter PasswordHashMigrationSeedTests`，Expected: 编译失败。

- [ ] **Step 7: 实现迁移钩子**

`CP6.WebApi/Seed/PasswordHashMigrationSeed.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>启动幂等：把现有明文密码就地 BCrypt 哈希（一次性原地迁移，不可逆）。返回本次哈希条数。</summary>
public static class PasswordHashMigrationSeed
{
    public static int EnsureHashed(CP6Context db, IPasswordHasher hasher)
    {
        // 跨租户扫描（IgnoreQueryFilters 白名单：一次性运维迁移）
        var users = db.Sys_Users.IgnoreQueryFilters().ToList();
        var n = 0;
        foreach (var u in users)
        {
            if (string.IsNullOrEmpty(u.Password) || hasher.IsHashed(u.Password)) continue;
            u.Password = hasher.Hash(u.Password);
            n++;
        }
        if (n > 0) db.SaveChanges();
        return n;
    }
}
```
Run: `dotnet test CP6.Tests --filter PasswordHashMigrationSeedTests`，Expected: PASS。

- [ ] **Step 8: 接 Program.cs（Configure + DI + 钩子）**

`Program.cs`：缓存注册区附近加配置绑定（在 `var app = builder.Build();` 之前）：
```csharp
builder.Services.Configure<CP6.Core.Services.Sys.SecurityOptions>(builder.Configuration.GetSection("Security"));
builder.Services.AddScoped<CP6.Core.Services.Sys.IPasswordHasher, CP6.Core.Services.Sys.BCryptPasswordHasher>();
```
`Program.cs` 种子区（`db.Database.Migrate()` 之后、其它 seed 同段，搜 `TenantSeed.EnsureSeeded`）加：
```csharp
PasswordHashMigrationSeed.EnsureHashed(db, scope.ServiceProvider.GetRequiredService<CP6.Core.Services.Sys.IPasswordHasher>());
```

- [ ] **Step 9: AuthController login 改 Verify**

`AuthController.cs`：构造注入加 `IPasswordHasher hasher`（字段 `_hasher`）。把 L76：
```csharp
if (user.Password != request.Password)
    return BadRequest(new { message = Localizer["密码错误"] });
```
改为：
```csharp
if (!_hasher.Verify(request.Password, user.Password))
    return BadRequest(new { message = Localizer["密码错误"] });
```
> 本 Task 暂保留 body 返 token（Cookie 化在 T6）。锁定/审计在 T3 叠加。

- [ ] **Step 10: appsettings 加 Security 段**

`appsettings.json` 根加（粘 spec §2.3 的 `Security` JSON，去注释）。`appsettings.Development.json` 加：
```json
"Security": { "Cookie": { "Secure": false } }
```

- [ ] **Step 11: 全量构建 + 测试 + 提交**

Run: `dotnet build CP6.WebApi` → `dotnet test CP6.Tests`，Expected: 全绿（基线 943+ 新增 2 测试）。
```bash
git add -A
git commit -m "feat(sec): T1 BCrypt密码哈希+Sys_User安全字段+原地迁移钩子(拔明文红牌)

login 改 BCrypt.Verify；启动幂等把现有明文就地哈希(不可逆,生产前须备份)。
Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T2：密码策略 + 历史 + 改密端点

**Files:**
- Create: `CP6.Entity/DomainModels/Sys/Sys_PasswordHistory.cs`、`CP6.Core/Services/Sys/IPasswordPolicyService.cs`、`PasswordPolicyService.cs`
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`（DbSet + 索引）、`AuthController.cs`（change-password 端点）
- Test: `CP6.Tests/Sys/PasswordPolicyServiceTests.cs`

- [ ] **Step 1: 实体 + DbSet**

`Sys/Sys_PasswordHistory.cs`：
```csharp
using System.ComponentModel.DataAnnotations;
namespace CP6.Entity.DomainModels.Sys;
public class Sys_PasswordHistory : BaseTenantEntity
{
    public Guid UserId { get; set; }
    [MaxLength(200)] public string PasswordHash { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
}
```
`CP6Context.cs`：加 `public DbSet<Sys_PasswordHistory> Sys_PasswordHistories => Set<Sys_PasswordHistory>();`，`OnModelCreating` 加 `modelBuilder.Entity<Sys_PasswordHistory>().HasIndex(x => new { x.UserId, x.ChangedAt });`（非唯一）。

- [ ] **Step 2: 写策略失败测试**

`CP6.Tests/Sys/PasswordPolicyServiceTests.cs`：
```csharp
using CP6.Core.Exceptions;          // BizException 实际命名空间，落码前 grep 确认
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

public class PasswordPolicyServiceTests
{
    private static PasswordPolicyService Make(CP6.Core.EFDbContext.CP6Context db, PasswordPolicyOptions p)
        => new(db, Options.Create(new SecurityOptions { Password = p }), new BCryptPasswordHasher());

    [Theory]
    [InlineData("Ab1!xyz9", true)]    // 8 位含大小写数字符号
    [InlineData("short1A", false)]    // < 8
    [InlineData("alllower1", false)]  // 无大写
    public void Validate_enforces_rules(string pwd, bool ok)
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = Make(db, new PasswordPolicyOptions { MinLength = 8, RequireUpper = true, RequireLower = true, RequireDigit = true, RequireSymbol = true });
        if (ok) svc.Validate(pwd);
        else Assert.Throws<BizException>(() => svc.Validate(pwd));
    }

    [Fact] public async Task History_rejects_reuse()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var hasher = new BCryptPasswordHasher();
        var uid = Guid.NewGuid();
        db.Sys_PasswordHistories.Add(new Sys_PasswordHistory { UserId = uid, PasswordHash = hasher.Hash("OldPass1!"), ChangedAt = DateTime.Now });
        db.SaveChanges();
        var svc = Make(db, new PasswordPolicyOptions { HistoryCount = 3 });
        await Assert.ThrowsAsync<BizException>(() => svc.CheckHistoryAsync(uid, "OldPass1!"));
        await svc.CheckHistoryAsync(uid, "BrandNew9#");   // 不抛
    }
}
```
Run: `dotnet test CP6.Tests --filter PasswordPolicyServiceTests`，Expected: 编译失败。

- [ ] **Step 3: 实现策略服务**

`IPasswordPolicyService.cs`：粘签名。`PasswordPolicyService.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Exceptions;          // BizException
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

public class PasswordPolicyService : IPasswordPolicyService
{
    private readonly CP6Context _db;
    private readonly PasswordPolicyOptions _p;
    private readonly IPasswordHasher _hasher;
    public PasswordPolicyService(CP6Context db, IOptions<SecurityOptions> opt, IPasswordHasher hasher)
    { _db = db; _p = opt.Value.Password; _hasher = hasher; }

    public void Validate(string plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.Length < _p.MinLength) throw new BizException("E-SEC-004");
        if (_p.RequireUpper && !plain.Any(char.IsUpper)) throw new BizException("E-SEC-004");
        if (_p.RequireLower && !plain.Any(char.IsLower)) throw new BizException("E-SEC-004");
        if (_p.RequireDigit && !plain.Any(char.IsDigit)) throw new BizException("E-SEC-004");
        if (_p.RequireSymbol && plain.All(char.IsLetterOrDigit)) throw new BizException("E-SEC-004");
    }

    public async Task CheckHistoryAsync(Guid userId, string plain)
    {
        if (_p.HistoryCount <= 0) return;
        var recent = await _db.Sys_PasswordHistories.IgnoreQueryFilters()
            .Where(h => h.UserId == userId).OrderByDescending(h => h.ChangedAt)
            .Take(_p.HistoryCount).Select(h => h.PasswordHash).ToListAsync();
        if (recent.Any(h => _hasher.Verify(plain, h))) throw new BizException("E-SEC-005");
    }

    public bool IsExpired(Sys_User user)
        => _p.ExpiryDays > 0 && user.PasswordChangedAt is { } at && (DateTime.Now - at).TotalDays > _p.ExpiryDays;
}
```
Run: `dotnet test CP6.Tests --filter PasswordPolicyServiceTests`，Expected: PASS。

- [ ] **Step 4: DI + change-password 端点**

`Program.cs` 加 `AddScoped<IPasswordPolicyService, PasswordPolicyService>()`。
`AuthController.cs` 加端点（注入 `IPasswordPolicyService _policy`、`IUserContext` 取当前用户，沿用现有 `IUserContext`；当前用户 id 从 `User.FindFirst(ClaimTypes.NameIdentifier)`）：
```csharp
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

[HttpPost("change-password"), Authorize]
public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
{
    var uid = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
    var user = await _context.Sys_Users.FirstAsync(u => u.Id == uid);
    if (!_hasher.Verify(req.CurrentPassword, user.Password)) throw new BizException("E-SEC-006");
    _policy.Validate(req.NewPassword);
    await _policy.CheckHistoryAsync(uid, req.NewPassword);
    // 旧哈希入历史 + 裁剪
    _context.Sys_PasswordHistories.Add(new Sys_PasswordHistory { UserId = uid, PasswordHash = user.Password, ChangedAt = DateTime.Now });
    user.Password = _hasher.Hash(req.NewPassword);
    user.PasswordChangedAt = DateTime.Now;
    user.MustChangePassword = false;
    await _context.SaveChangesAsync();
    // 历史裁剪（保留最近 N+1）+ 吊销其它 refresh（T4 接 _refresh.RevokeAllForUserAsync）+ 审计（T3 接 _audit）留各 Task 叠加
    return Ok(new { code = 0, message = "OK" });
}
```
> 占位注释 `T3/T4 叠加` 指明后续 Task 会在此补 `_audit.LogAsync(PasswordChanged,...)` 与 `_refresh.RevokeAllForUserAsync(uid)`——非 placeholder，是显式跨 Task 接缝。

- [ ] **Step 5: 全量测试 + 提交**

Run: `dotnet test CP6.Tests`，Expected: 全绿。
```bash
git add -A && git commit -m "feat(sec): T2 密码策略(长度/复杂度/历史不可重用)+改密端点

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T3：登录锁定 + 安全事件审计

**Files:**
- Create: `Sys/Sys_SecurityLog.cs`、`Sys/SecurityEventType.cs`、`Services/Sys/ILoginSecurityService.cs`/`LoginSecurityService.cs`、`ISecurityAuditService.cs`/`SecurityAuditService.cs`
- Modify: `CP6Context.cs`（DbSet+索引）、`AuthController.cs`（登录流接锁定+审计）、`Program.cs`（DI）
- Test: `CP6.Tests/Sys/LoginSecurityServiceTests.cs`、`SecurityAuditServiceTests.cs`

- [ ] **Step 1: 枚举 + 实体 + DbSet**

`Sys/SecurityEventType.cs`：
```csharp
namespace CP6.Entity.DomainModels.Sys;
public enum SecurityEventType { LoginSuccess = 1, LoginFailed = 2, AccountLocked = 3, Logout = 4, PasswordChanged = 5, TokenRefreshed = 6, TokenReuseDetected = 7, PermissionDenied = 8 }
```
`Sys/Sys_SecurityLog.cs`：
```csharp
using System.ComponentModel.DataAnnotations;
namespace CP6.Entity.DomainModels.Sys;
public class Sys_SecurityLog : BaseTenantEntity
{
    public Guid? UserId { get; set; }
    [MaxLength(100)] public string? UserName { get; set; }
    [MaxLength(64)]  public string? RequestTenantCode { get; set; }
    public int EventType { get; set; }
    [MaxLength(256)] public string? Reason { get; set; }
    [MaxLength(64)]  public string? ClientIp { get; set; }
    [MaxLength(256)] public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}
```
`CP6Context.cs`：`DbSet<Sys_SecurityLog> Sys_SecurityLogs` + 索引 `HasIndex(x => new { x.EventType, x.CreatedAt })`、`HasIndex(x => x.UserName)`（非唯一）。

- [ ] **Step 2: 写锁定失败测试**

`CP6.Tests/Sys/LoginSecurityServiceTests.cs`：
```csharp
using CP6.Core.Exceptions;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

public class LoginSecurityServiceTests
{
    private static LoginSecurityService Make(CP6.Core.EFDbContext.CP6Context db, LockoutOptions l)
        => new(db, Options.Create(new SecurityOptions { Lockout = l }));

    [Fact] public async Task Locks_after_threshold_and_resets_on_success()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var u = new Sys_User { UserName = "x", Password = "h" }; db.Sys_Users.Add(u); db.SaveChanges();
        var svc = Make(db, new LockoutOptions { MaxFailedAttempts = 3, LockoutMinutes = 15, ResetCounterMinutes = 15 });

        for (int i = 0; i < 3; i++) await svc.RecordFailureAsync(u);
        Assert.NotNull(u.LockedUntil);
        Assert.Throws<BizException>(() => svc.EnsureNotLocked(u));   // 锁定期内即使密码对也拒

        u.LockedUntil = DateTime.Now.AddMinutes(-1);               // 模拟过期
        svc.EnsureNotLocked(u);                                    // 不抛
        await svc.RecordSuccessAsync(u, "1.2.3.4");
        Assert.Equal(0, u.FailedLoginCount);
        Assert.Null(u.LockedUntil);
        Assert.Equal("1.2.3.4", u.LastLoginIp);
        Assert.NotNull(u.LastLoginTime);
    }
}
```
Run: `dotnet test CP6.Tests --filter LoginSecurityServiceTests`，Expected: 编译失败。

- [ ] **Step 3: 实现锁定服务**

`LoginSecurityService.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Exceptions;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

public class LoginSecurityService : ILoginSecurityService
{
    private readonly CP6Context _db; private readonly LockoutOptions _l;
    public LoginSecurityService(CP6Context db, IOptions<SecurityOptions> opt) { _db = db; _l = opt.Value.Lockout; }

    public void EnsureNotLocked(Sys_User user)
    { if (user.LockedUntil is { } until && until > DateTime.Now) throw new BizException("E-SEC-002"); }

    public async Task RecordFailureAsync(Sys_User user)
    {
        // 滑动重置：距上次失败超过 ResetCounterMinutes 则计数清零再累加
        if (user.LastFailedLoginAt is { } last && (DateTime.Now - last).TotalMinutes > _l.ResetCounterMinutes)
            user.FailedLoginCount = 0;
        user.FailedLoginCount++;
        user.LastFailedLoginAt = DateTime.Now;
        if (user.FailedLoginCount >= _l.MaxFailedAttempts)
            user.LockedUntil = DateTime.Now.AddMinutes(_l.LockoutMinutes);
        await _db.SaveChangesAsync();
    }

    public async Task RecordSuccessAsync(Sys_User user, string? ip)
    {
        user.FailedLoginCount = 0; user.LockedUntil = null; user.LastFailedLoginAt = null;
        user.LastLoginTime = DateTime.Now; user.LastLoginIp = ip;
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: 写审计失败测试 + 实现审计服务**

`CP6.Tests/Sys/SecurityAuditServiceTests.cs`：
```csharp
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Xunit;

namespace CP6.Tests.Sys;

public class SecurityAuditServiceTests
{
    [Fact] public async Task Logs_event_with_fields()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = new SecurityAuditService(db);
        await svc.LogAsync(SecurityEventType.LoginFailed, null, "ghost", "ACME", "9.9.9.9", "UA/1", "user not found");
        var log = db.Sys_SecurityLogs.Single();
        Assert.Equal((int)SecurityEventType.LoginFailed, log.EventType);
        Assert.Equal("ghost", log.UserName);
        Assert.Equal("ACME", log.RequestTenantCode);
    }
}
```
`SecurityAuditService.cs`：
```csharp
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

public class SecurityAuditService : ISecurityAuditService
{
    private readonly CP6Context _db;
    public SecurityAuditService(CP6Context db) => _db = db;

    public async Task LogAsync(SecurityEventType type, Guid? userId, string? userName, string? requestTenantCode, string? ip, string? ua, string? reason = null)
    {
        try
        {
            _db.Sys_SecurityLogs.Add(new Sys_SecurityLog {
                UserId = userId, UserName = userName, RequestTenantCode = requestTenantCode,
                EventType = (int)type, ClientIp = ip, UserAgent = ua, Reason = reason, CreatedAt = DateTime.Now });
            await _db.SaveChangesAsync();
        }
        catch { /* 审计失败不阻断主流程（仿 OperLog WriteLogSafely） */ }
    }
}
```
Run: `dotnet test CP6.Tests --filter "LoginSecurityServiceTests|SecurityAuditServiceTests"`，Expected: PASS。

- [ ] **Step 5: 登录流接锁定 + 审计**

`Program.cs` DI：`AddScoped<ILoginSecurityService,LoginSecurityService>()` + `AddScoped<ISecurityAuditService,SecurityAuditService>()`。
`AuthController.cs` 注入 `_login`/`_audit`，改造 `Login`：用户找不到 → `await _audit.LogAsync(LoginFailed, null, request.UserName, request.TenantCode, ip, ua, "user not found")` 后返回统一 `E-SEC-001` 文案；锁定 → `_login.EnsureNotLocked(user)`（捕获/前置判定）→ 审计后 `E-SEC-002`；`Verify` 失败 → `await _login.RecordFailureAsync(user)` + 审计 `LoginFailed`/达阈值 `AccountLocked`，返回 `E-SEC-001`；成功 → `await _login.RecordSuccessAsync(user, ip)` + 审计 `LoginSuccess`。
> `ip = HttpContext.Connection.RemoteIpAddress?.ToString()`；`ua = Request.Headers.UserAgent.ToString()`。
> T2 的 change-password 端点此时补 `await _audit.LogAsync(PasswordChanged, uid, user.UserName, null, ip, ua)`。

- [ ] **Step 6: 全量测试 + 提交**

Run: `dotnet test CP6.Tests`，Expected: 全绿。
```bash
git add -A && git commit -m "feat(sec): T3 登录锁定(防暴破)+Sys_SecurityLog安全事件审计

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T4：刷新令牌轮换 + 重用检测

**Files:**
- Create: `Sys/Sys_RefreshToken.cs`、`Services/Sys/IRefreshTokenService.cs`/`RefreshTokenService.cs`
- Modify: `CP6Context.cs`（DbSet+索引+TokenHash 全局唯一跳过条件）、`AuthController.cs`（refresh 端点）、`Program.cs`（DI）
- Test: `CP6.Tests/Sys/RefreshTokenServiceTests.cs`、`RefreshTokenSqliteTests.cs`

- [ ] **Step 1: 实体 + DbSet + 全局唯一索引跳过**

`Sys/Sys_RefreshToken.cs`：
```csharp
using System.ComponentModel.DataAnnotations;
namespace CP6.Entity.DomainModels.Sys;
public class Sys_RefreshToken : BaseTenantEntity
{
    public Guid UserId { get; set; }
    [MaxLength(128)] public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    [MaxLength(128)] public string? ReplacedByTokenHash { get; set; }
    [MaxLength(64)]  public string? CreatedIp { get; set; }
    [MaxLength(256)] public string? UserAgent { get; set; }
}
```
`CP6Context.cs`：`DbSet<Sys_RefreshToken> Sys_RefreshTokens` + `HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UX_Sys_RefreshToken_TokenHash")` + `HasIndex(x => x.UserId)`。
**唯一索引前缀循环加跳过条件**（约 `CP6Context.cs:1859-1894`，在跳过 `idx.Properties.Contains(tenantProp)` / FK 主键依赖之后加）：
```csharp
// S 类认证加固：RefreshToken.TokenHash 必须保持单列全局唯一（refresh 无 tenant context 跨租户查）
if (et.ClrType == typeof(CP6.Entity.DomainModels.Sys.Sys_RefreshToken)
    && idx.Properties.Count == 1
    && idx.Properties[0].Name == nameof(CP6.Entity.DomainModels.Sys.Sys_RefreshToken.TokenHash))
    continue;
```

- [ ] **Step 2: 写轮换/重用失败测试**

`CP6.Tests/Sys/RefreshTokenServiceTests.cs`：
```csharp
using CP6.Core.Exceptions;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

public class RefreshTokenServiceTests
{
    private static RefreshTokenService Make(CP6.Core.EFDbContext.CP6Context db)
        => new(db, Options.Create(new SecurityOptions { Token = new TokenOptions { RefreshTokenDays = 7 } }));

    [Fact] public async Task Issue_then_rotate_invalidates_old_and_detects_reuse()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var user = new Sys_User { UserName = "u", Password = "h" }; db.Sys_Users.Add(user); db.SaveChanges();
        var svc = Make(db);

        var raw1 = await svc.IssueAsync(user, "1.1.1.1", "UA");
        var (raw2, who) = await svc.RotateAsync(raw1, "1.1.1.1", "UA");
        Assert.Equal(user.Id, who.Id);
        Assert.NotEqual(raw1, raw2);

        // 旧令牌已吊销 → 再用触发重用检测，整链吊销
        await Assert.ThrowsAsync<BizException>(() => svc.RotateAsync(raw1, "1.1.1.1", "UA"));
        // 重用检测后新令牌也被吊销
        await Assert.ThrowsAsync<BizException>(() => svc.RotateAsync(raw2, "1.1.1.1", "UA"));
    }

    [Fact] public async Task Expired_is_rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var user = new Sys_User { UserName = "u", Password = "h" }; db.Sys_Users.Add(user); db.SaveChanges();
        var svc = Make(db);
        var raw = await svc.IssueAsync(user, null, null);
        var row = db.Sys_RefreshTokens.IgnoreQueryFilters().Single();
        row.ExpiresAt = DateTime.Now.AddMinutes(-1); db.SaveChanges();
        await Assert.ThrowsAsync<BizException>(() => svc.RotateAsync(raw, null, null));
    }
}
```
Run: `dotnet test CP6.Tests --filter RefreshTokenServiceTests`，Expected: 编译失败。

- [ ] **Step 3: 实现 RefreshTokenService**

`RefreshTokenService.cs`：
```csharp
using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Core.Exceptions;
using CP6.Core.Services.Common;     // ITenantContext
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

public class RefreshTokenService : IRefreshTokenService
{
    private readonly CP6Context _db; private readonly TokenOptions _t; private readonly ITenantContext _tenant;
    public RefreshTokenService(CP6Context db, IOptions<SecurityOptions> opt, ITenantContext tenant)
    { _db = db; _t = opt.Value.Token; _tenant = tenant; }

    private static string NewRaw() { var b = RandomNumberGenerator.GetBytes(32); return Base64Url(b); }
    private static string HashOf(string raw) { using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw))); }
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_');

    public async Task<string> IssueAsync(Sys_User user, string? ip, string? ua)
    {
        var raw = NewRaw();
        _db.Sys_RefreshTokens.Add(new Sys_RefreshToken {
            UserId = user.Id, TokenHash = HashOf(raw),
            ExpiresAt = DateTime.Now.AddDays(_t.RefreshTokenDays), CreatedIp = ip, UserAgent = ua });
        await _db.SaveChangesAsync();
        return raw;
    }

    public async Task<(string newToken, Sys_User user)> RotateAsync(string rawToken, string? ip, string? ua)
    {
        var hash = HashOf(rawToken);
        // 无 tenant context：按 TokenHash 跨租户查（全局唯一索引；IgnoreQueryFilters 白名单）
        var row = await _db.Sys_RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TokenHash == hash);
        if (row == null || row.ExpiresAt <= DateTime.Now) throw new BizException("E-SEC-007");
        if (row.RevokedAt != null)
        {
            // 重用检测：已吊销令牌被再次提交 → 盗用 → 吊销该用户整条链
            await RevokeAllForUserAsync(row.UserId);
            throw new BizException("E-SEC-008");
        }
        // 由令牌 TenantId 回设上下文，后续查询/盖章正确作用域
        _tenant.CurrentTenantId = row.TenantId;
        var user = await _db.Sys_Users.IgnoreQueryFilters().FirstAsync(u => u.Id == row.UserId);
        var raw2 = NewRaw();
        row.RevokedAt = DateTime.Now; row.ReplacedByTokenHash = HashOf(raw2);
        _db.Sys_RefreshTokens.Add(new Sys_RefreshToken {
            UserId = user.Id, TenantId = row.TenantId, TokenHash = HashOf(raw2),
            ExpiresAt = DateTime.Now.AddDays(_t.RefreshTokenDays), CreatedIp = ip, UserAgent = ua });
        await _db.SaveChangesAsync();
        return (raw2, user);
    }

    public async Task RevokeAsync(string rawToken)
    {
        var hash = HashOf(rawToken);
        var row = await _db.Sys_RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TokenHash == hash);
        if (row is { RevokedAt: null }) { row.RevokedAt = DateTime.Now; await _db.SaveChangesAsync(); }
    }

    public async Task RevokeAllForUserAsync(Guid userId)
    {
        var rows = await _db.Sys_RefreshTokens.IgnoreQueryFilters()
            .Where(r => r.UserId == userId && r.RevokedAt == null).ToListAsync();
        foreach (var r in rows) r.RevokedAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
```
Run: `dotnet test CP6.Tests --filter RefreshTokenServiceTests`，Expected: PASS。

- [ ] **Step 4: SQLite 跨租户/全局唯一测试**

`CP6.Tests/Sys/RefreshTokenSqliteTests.cs`：用 SQLite harness 建两个租户的 `Sys_RefreshToken`，验证 `RotateAsync` 在默认 tenant context 下仍能跨租户按 `TokenHash` 命中（证明全局唯一索引 + IgnoreQueryFilters 生效）。
```csharp
[Fact] public async Task Rotate_finds_token_across_tenants_under_default_context() { /* SQLite harness：建 TenantB 的令牌，默认上下文 RotateAsync 成功并回设 TenantId */ }
```
Run: `dotnet test CP6.Tests --filter RefreshTokenSqliteTests`，Expected: PASS。

- [ ] **Step 5: 补建表迁移 + refresh 端点 + DI**

补一支迁移把 T2/T3/T4 的 3 新表落库：
Run: `dotnet ef migrations add SecAuthTables --project CP6.Core --startup-project CP6.WebApi`
核对：`Sys_PasswordHistory`/`Sys_SecurityLog` 唯一索引（若有）含 `TenantId`；**`UX_Sys_RefreshToken_TokenHash` 单列、无 `TenantId` 前缀**。
`Program.cs` DI：`AddScoped<IRefreshTokenService,RefreshTokenService>()`。
`AuthController.cs` 加 refresh 端点（Cookie 读写在 T6，本步先用 body 透传便于单测；T6 改为 Cookie）：
```csharp
[HttpPost("refresh"), AllowAnonymous]
public async Task<IActionResult> Refresh()
{
    var raw = Request.Cookies["cp6_rt"];            // T6 起从 cookie；过渡期可同时支持 body
    if (string.IsNullOrEmpty(raw)) throw new BizException("E-SEC-007");
    var (newRaw, user) = await _refresh.RotateAsync(raw, ip, ua);
    // T6：签发新 access + 写三 Cookie + 审计 TokenRefreshed；本步先占位
    return Ok(new { code = 0 });
}
```
T2 的 change-password 端点此时补 `await _refresh.RevokeAllForUserAsync(uid);`。

- [ ] **Step 6: 全量测试 + 提交**

Run: `dotnet test CP6.Tests`，Expected: 全绿。
```bash
git add -A && git commit -m "feat(sec): T4 刷新令牌轮换+重用检测吊链(TokenHash全局唯一,跨租户查)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T5：登出黑名单 + jti

**Files:**
- Create: `Services/Sys/ITokenBlacklistService.cs`/`CacheTokenBlacklistService.cs`
- Modify: `JwtHelper.cs`（jti + must_change_password claim）、`AuthController.cs`（logout 端点 + login 带 jti）、`Program.cs`（DI + JWT `OnTokenValidated` 黑名单校验）
- Test: `CP6.Tests/Sys/TokenBlacklistServiceTests.cs`

- [ ] **Step 1: JwtHelper 加 jti + must_change_password**

`JwtHelper.GenerateToken` 形参尾加 `string? jti = null, bool mustChangePassword = false`，claims 数组加：
```csharp
new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, jti ?? Guid.NewGuid().ToString()),
new Claim("must_change_password", mustChangePassword ? "true" : "false"),
```
> 注意：`JwtRegisteredClaimNames.Jti` → claim 类型 `"jti"`，读取用 `User.FindFirst("jti")`。

- [ ] **Step 2: 写黑名单测试 + 实现**

`CP6.Tests/Sys/TokenBlacklistServiceTests.cs`：
```csharp
using CP6.Core.Services.Sys;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

public class TokenBlacklistServiceTests
{
    [Fact] public async Task Blacklisted_jti_is_detected()
    {
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var svc = new CacheTokenBlacklistService(cache);
        Assert.False(await svc.IsBlacklistedAsync("jti-1"));
        await svc.BlacklistAsync("jti-1", TimeSpan.FromMinutes(5));
        Assert.True(await svc.IsBlacklistedAsync("jti-1"));
    }
}
```
`CacheTokenBlacklistService.cs`：
```csharp
using Microsoft.Extensions.Caching.Distributed;
namespace CP6.Core.Services.Sys;
public class CacheTokenBlacklistService : ITokenBlacklistService
{
    private readonly IDistributedCache _cache;
    public CacheTokenBlacklistService(IDistributedCache cache) => _cache = cache;
    private static string Key(string jti) => $"sec:bl:{jti}";
    public Task BlacklistAsync(string jti, TimeSpan ttl)
        => _cache.SetStringAsync(Key(jti), "1", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
    public async Task<bool> IsBlacklistedAsync(string jti) => await _cache.GetStringAsync(Key(jti)) != null;
}
```
Run: `dotnet test CP6.Tests --filter TokenBlacklistServiceTests`，Expected: PASS。

- [ ] **Step 3: logout 端点 + login 带 jti + DI + JWT 黑名单校验**

`Program.cs` DI：`AddScoped<ITokenBlacklistService,CacheTokenBlacklistService>()`。
`AddJwtBearer` 加 `Events`（与 T6 的 `OnMessageReceived` 合并到同一个 `JwtBearerEvents`）：
```csharp
options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
{
    OnTokenValidated = async ctx =>
    {
        var jti = ctx.Principal?.FindFirst("jti")?.Value;
        if (!string.IsNullOrEmpty(jti))
        {
            var bl = ctx.HttpContext.RequestServices.GetRequiredService<CP6.Core.Services.Sys.ITokenBlacklistService>();
            if (await bl.IsBlacklistedAsync(jti)) ctx.Fail("blacklisted");
        }
    }
};
```
`AuthController.cs`：login 签发时生成 `var jti = Guid.NewGuid().ToString();` 传入 `JwtHelper.GenerateToken(..., jti: jti, mustChangePassword: needChange)`，access TTL 取 `IOptions<SecurityOptions>.Value.Token.AccessTokenMinutes`。logout 端点：
```csharp
[HttpPost("logout"), Authorize]
public async Task<IActionResult> Logout()
{
    var jti = User.FindFirst("jti")?.Value;
    var exp = User.FindFirst("exp")?.Value;     // 计算剩余寿命
    if (!string.IsNullOrEmpty(jti))
    {
        var ttl = TimeSpan.FromMinutes(15);     // 兜底；精确值由 exp 推算
        await _blacklist.BlacklistAsync(jti, ttl);
    }
    var raw = Request.Cookies["cp6_rt"]; if (!string.IsNullOrEmpty(raw)) await _refresh.RevokeAsync(raw);
    _cookies.ClearAuthCookies(Response);        // T6 接入 IAuthCookieWriter
    await _audit.LogAsync(SecurityEventType.Logout, /*uid*/..., User.Identity?.Name, null, ip, ua);
    return Ok(new { code = 0 });
}
```

- [ ] **Step 4: 全量测试 + 提交**

Run: `dotnet test CP6.Tests`，Expected: 全绿。
```bash
git add -A && git commit -m "feat(sec): T5 登出jti黑名单(IDistributedCache)+JWT OnTokenValidated校验

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T6：Cookie 化 + CSRF + 强制改密中间件 + CORS 收紧

**Files:**
- Create: `Services/Sys/IAuthCookieWriter.cs`/`AuthCookieWriter.cs`、`CP6.WebApi/Middleware/CsrfMiddleware.cs`、`MustChangePasswordMiddleware.cs`
- Modify: `Program.cs`（JWT `OnMessageReceived` + CORS 收紧 + 2 中间件注册 + DI）、`AuthController.cs`（login/refresh 写三 Cookie、不再 body 返 token）
- Test: `CP6.Tests/Sys/SecurityMiddlewareTests.cs`（`TestServer`）

- [ ] **Step 1: AuthCookieWriter**

`AuthCookieWriter.cs`（读 `IOptions<SecurityOptions>.Cookie`）：
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
namespace CP6.Core.Services.Sys;   // 若需 HttpResponse，放 WebApi 层亦可；保持与接口同程序集

public class AuthCookieWriter : IAuthCookieWriter
{
    private readonly AuthCookieOptions _c;
    public AuthCookieWriter(IOptions<SecurityOptions> opt) => _c = opt.Value.Cookie;
    private SameSiteMode Same => Enum.TryParse<SameSiteMode>(_c.SameSite, true, out var m) ? m : SameSiteMode.Strict;

    public void WriteAuthCookies(HttpResponse resp, string accessJwt, string rawRefresh, string csrf)
    {
        var baseOpt = new CookieOptions { HttpOnly = true, Secure = _c.Secure, SameSite = Same };
        resp.Cookies.Append("cp6_at", accessJwt, new CookieOptions { HttpOnly = true, Secure = _c.Secure, SameSite = Same, Path = "/" });
        resp.Cookies.Append("cp6_rt", rawRefresh, new CookieOptions { HttpOnly = true, Secure = _c.Secure, SameSite = Same, Path = "/api/auth" });
        resp.Cookies.Append("cp6_csrf", csrf, new CookieOptions { HttpOnly = false, Secure = _c.Secure, SameSite = Same, Path = "/" });
    }
    public void ClearAuthCookies(HttpResponse resp)
    {
        foreach (var (name, path) in new[] { ("cp6_at","/"), ("cp6_rt","/api/auth"), ("cp6_csrf","/") })
            resp.Cookies.Append(name, "", new CookieOptions { Expires = DateTimeOffset.UnixEpoch, Path = path, Secure = _c.Secure, SameSite = Same });
    }
}
```
> 若 `CP6.Core` 不引用 `Microsoft.AspNetCore.Http`，把 `IAuthCookieWriter`/`AuthCookieWriter` 放 `CP6.WebApi/Services/Sys/`（落码前确认 Core 是否已引 Http.Abstractions；多数 ASP.NET Core 项目 Core 层可引 `Microsoft.AspNetCore.Http.Abstractions`）。

- [ ] **Step 2: CSRF + 强制改密中间件**

`CsrfMiddleware.cs`：
```csharp
using CP6.Core.Exceptions;
namespace CP6.WebApi.Middleware;
public class CsrfMiddleware
{
    private readonly RequestDelegate _next;
    public CsrfMiddleware(RequestDelegate next) => _next = next;
    private static readonly string[] Unsafe = { "POST", "PUT", "PATCH", "DELETE" };
    public async Task Invoke(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        var exempt = path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase); // refresh 不豁免
        if (!exempt && Unsafe.Contains(ctx.Request.Method.ToUpperInvariant()))
        {
            var cookie = ctx.Request.Cookies["cp6_csrf"];
            var header = ctx.Request.Headers["X-CSRF-Token"].ToString();
            if (string.IsNullOrEmpty(cookie) || cookie != header) throw new BizException("E-SEC-010");
        }
        await _next(ctx);
    }
}
```
`MustChangePasswordMiddleware.cs`：
```csharp
using CP6.Core.Exceptions;
namespace CP6.WebApi.Middleware;
public class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;
    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;
    private static readonly string[] Allow = { "/api/auth/change-password", "/api/auth/logout" };
    public async Task Invoke(HttpContext ctx)
    {
        if (ctx.User?.Identity?.IsAuthenticated == true
            && ctx.User.FindFirst("must_change_password")?.Value == "true"
            && !Allow.Any(a => (ctx.Request.Path.Value ?? "").StartsWith(a, StringComparison.OrdinalIgnoreCase)))
            throw new BizException("E-SEC-009");      // claim 优先，不查库
        await _next(ctx);
    }
}
```

- [ ] **Step 3: JWT OnMessageReceived + CORS 收紧 + 中间件注册 + DI**

`Program.cs` `AddJwtBearer` 的 `Events` 补 `OnMessageReceived`（与 T5 的 `OnTokenValidated` 同对象）：
```csharp
OnMessageReceived = ctx =>
{
    if (string.IsNullOrEmpty(ctx.Token))
    {
        var c = ctx.Request.Cookies["cp6_at"];
        if (!string.IsNullOrEmpty(c)) ctx.Token = c;
    }
    return Task.CompletedTask;
},
```
CORS 收紧（L462-469）：策略改为显式 origin（配置 `Cors:AllowedOrigins`，默认 `http://localhost:5173`）：
```csharp
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:5173" };
options.AddPolicy("AllowAll", policy =>
    policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials());
```
中间件注册（`Program.cs:2151` `BizExceptionMiddleware` 之后、`2153` `UseAuthorization` 之前）：
```csharp
app.UseMiddleware<CP6.WebApi.Middleware.CsrfMiddleware>();
app.UseMiddleware<CP6.WebApi.Middleware.MustChangePasswordMiddleware>();
```
DI：`AddScoped<IAuthCookieWriter,AuthCookieWriter>()`。

- [ ] **Step 4: AuthController 写三 Cookie**

login 成功：生成 `csrf = AuthCookieWriter`-同款随机串（或 `RefreshTokenService` 暴露静态 `NewCsrf()`）→ `var rawRt = await _refresh.IssueAsync(user, ip, ua);` → `_cookies.WriteAuthCookies(Response, token, rawRt, csrf)` → 返回 `{ userName, nickName, roleId, menus, mustChangePassword }`（**不含 token**）。
refresh 端点：`RotateAsync` 后用新 user + 新 jti 签 access + 新 csrf，`WriteAuthCookies`，审计 `TokenRefreshed`。
logout：`ClearAuthCookies`（T5 已占位接入）。

- [ ] **Step 5: 中间件测试（TestServer）**

`CP6.Tests/Sys/SecurityMiddlewareTests.cs`：用 `WebApplicationFactory`/`TestServer` 验证：①POST 无 `X-CSRF-Token` → 403 `E-SEC-010`；②header==cookie → 放行；③`/api/auth/login` 豁免；④带 `must_change_password=true` token 访问非白名单 → `E-SEC-009`，访问 `/api/auth/change-password` 放行。
Run: `dotnet test CP6.Tests --filter SecurityMiddlewareTests`，Expected: PASS。

- [ ] **Step 6: 全量测试 + 提交**

Run: `dotnet test CP6.Tests`，Expected: 全绿。
```bash
git add -A && git commit -m "feat(sec): T6 httpOnly三Cookie+JWT从cookie读+CSRF双提交(refresh不豁免)+强制改密中间件+CORS收紧

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T7：UserController 哈希 + 管理员重置

**Files:**
- Modify: `CP6.WebApi/Controllers/Sys/UserController.cs`
- Test: `CP6.Tests/Sys/UserControllerPasswordTests.cs`（若现有 UserController 有可测服务层则测之；否则集成测试）

- [ ] **Step 1: 写"建用户/改密落哈希"测试**

针对 `UserController` 建用户、改密路径断言 `Sys_User.Password` 是 BCrypt 格式（`IsHashed`）、管理员重置置 `MustChangePassword=true` 且吊销该用户 refresh。
Run: Expected 编译/断言失败。

- [ ] **Step 2: 改 UserController**

注入 `IPasswordHasher`/`IRefreshTokenService`。建用户/设密码：`user.Password = _hasher.Hash(plain); user.PasswordChangedAt = DateTime.Now;`。管理员重置：另 `user.MustChangePassword = true; await _refresh.RevokeAllForUserAsync(user.Id);`（可选 `_blacklist.BlacklistAsync(currentJti, ttl)`）。**删除所有明文写 `Password` 的路径**。
Run: `dotnet test CP6.Tests`，Expected: 全绿。

- [ ] **Step 3: 提交**
```bash
git add -A && git commit -m "feat(sec): T7 UserController建用户/改密落BCrypt+管理员重置(置MustChange+吊销refresh)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T8：E-SEC 五语 i18n + SecurityLogController + 菜单/权限

**Files:**
- Create: `CP6.WebApi/Seed/I18nSecScreenSeed.cs`、`Controllers/Sys/SecurityLogController.cs`
- Modify: `Program.cs`（i18n `.Concat` + 菜单 seed + RoleMenu + RoleAction 权限点）

- [ ] **Step 1: E-SEC + 画面词条五语 seed**

`I18nSecScreenSeed.cs`（仿 `I18nA3ScreenSeed`）：`E-SEC-001~010`（中文 key=自然语言，spec §7 表）+ `SecurityEventType` 八枚举标签 + 改密页/安全日志页词条，五语 ZhCN/ZhTW/En/Ja/Ko。接 `Program.cs` i18n 合并链 `.Concat(I18nSecScreenSeed.Rows)`。

- [ ] **Step 2: SecurityLogController（分页查询）**

`Controllers/Sys/SecurityLogController.cs`：`[Authorize]` + `GET /api/sys/security-log?eventType=&userName=&from=&to=&page=&size=`，`[RequirePermission("sys-security-log","query")]`，按当前租户（全局过滤自动）分页返回 `Sys_SecurityLog`，`EventType` 映射枚举标签由前端做。

- [ ] **Step 3: 菜单 + 权限点 seed**

`Program.cs`：菜单"系统管理"组下加 `SecurityLogView`（RoutePath `/sys/security-log`，MenuKey 自动 `sys-security-log`，菜单号取 Sys 组空位），RoleMenu 授 admin，`Sys_MenuAction`/`RoleAction` 加 `query` 操作点授 admin。

- [ ] **Step 4: 构建 + 提交**

Run: `dotnet build CP6.WebApi` + `dotnet test CP6.Tests`，Expected: 全绿。
```bash
git add -A && git commit -m "feat(sec): T8 E-SEC错误码五语+SecurityLogController安全日志查询+菜单权限

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T9：前端 Cookie 化 + 改密页 + 安全日志页

**Files:**
- Modify: `cp6.web/src/api/http.ts`、`views/LoginView.vue`、`router/index.ts`
- Create: `views/sys/ChangePasswordView.vue`、`views/sys/SecurityLogView.vue`、`api/sys/auth.ts`、`api/sys/securityLog.ts`、`types/sys/security.ts`

- [ ] **Step 1: http.ts 改造**

去掉注入 `Authorization` 头；axios 实例加 `withCredentials: true`；请求拦截器对非 GET 注入 `X-CSRF-Token`（读 `document.cookie` 的 `cp6_csrf`）；响应 401 → 调一次 `POST /api/auth/refresh`，成功则重放原请求，失败 → 清本地状态跳 `/login`。

- [ ] **Step 2: LoginView 弃 localStorage token**

`handleLogin`：成功后**不再** `localStorage.setItem('token', ...)`（token 在 httpOnly cookie）；菜单/档案存 pinia（或 localStorage 仅存非敏感 menus/userName）；若 `res.mustChangePassword` → `router.push('/sys/change-password')`。

- [ ] **Step 3: ChangePasswordView + SecurityLogView + 路由 + 守卫**

`ChangePasswordView.vue`：当前/新/确认密码表单 → `POST /api/auth/change-password`，成功提示并跳首页。
`SecurityLogView.vue`：多条件筛选（事件类型枚举下拉/用户名/时间范围）+ 分页表格（仿 `OperLogView`），事件类型用 i18n 标签映射。
`router/index.ts`：加 `/sys/change-password`、`/sys/security-log` 路由；全局 `beforeEach` 守卫：若标志 `mustChangePassword` 为真且目标非改密页 → 强制跳改密页。
`api/sys/auth.ts`（login/logout/refresh/changePassword）、`api/sys/securityLog.ts`、`types/sys/security.ts`（`SecurityEventType` 标签映射）。

- [ ] **Step 4: 前端校验 + 提交**

Run: `cd cp6.web && npm run type-check && npm run i18n:check && npx vitest run && npm run build`，Expected: 全绿。
```bash
git add -A && git commit -m "feat(sec): T9 前端Cookie化(去localStorage token)+CSRF头+401自动refresh+改密页+安全日志页

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task T10：gstack 真浏览器 QA 全流程

> 本地 `5177` 后端 / `5173` 前端，`admin`。Development 下 `Cookie:Secure=false` 保 http 可跑。新词须真起后端 + `npm run i18n:pull` 重建快照 + `i18n:gen-types`，再 `i18n:check` 绿。

- [ ] **Step 1: 起 dev（后端 + 前端），admin 登录**——验证：成功后浏览器有 `cp6_at/cp6_rt/cp6_csrf` 三 Cookie，`Application > Local Storage` 无 token；DB `Sys_User.Password` 为 BCrypt 哈希（迁移钩子生效）。
- [ ] **Step 2: 连续 5 次错密**——第 5 次后 `Sys_User.LockedUntil` 被置，再登录即使正确密码也提示"账户已锁定"（E-SEC-002 本地化）；`Sys_SecurityLog` 有 LoginFailed×N + AccountLocked。
- [ ] **Step 3: 改密流**——改密页改密；旧密码登录失败、新密码成功；`Sys_PasswordHistory` 增一行；用旧密码做新密码触发 E-SEC-005。
- [ ] **Step 4: 刷新轮换**——等 access 过期（或调短 `AccessTokenMinutes`）触发 401 → 自动 refresh，`cp6_rt` 轮换（DB 旧行 RevokedAt 非空、新行出现）；手工重放旧 rt → E-SEC-008 重用检测、整链吊销。
- [ ] **Step 5: 登出黑名单**——登出后三 Cookie 清；用登出前抓取的 access 直接打 API → 401（jti 黑名单命中）。
- [ ] **Step 6: CSRF**——构造无 `X-CSRF-Token` 的 POST → 403 E-SEC-010；正常前端写操作（带头）通过。
- [ ] **Step 7: 安全日志页**——`/sys/security-log` 按事件类型/时间筛选，看到上述各事件，`RequestTenantCode` 记录正确。
- [ ] **Step 8: 全量回归**——`dotnet test CP6.Tests` + 前端四校验全绿；提交 QA 修复（如有）。
```bash
git add -A && git commit -m "test(sec): T10 gstack真浏览器QA全流程(登录/锁定/改密/刷新轮换/登出黑名单/CSRF/安全日志)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review（计划自审）

**1. Spec 覆盖**：spec §0 范围→T1-T9；§2 数据模型→T1(字段)/T2/T3/T4(实体)；§3 组件→7 服务分落 T1-T6；§4 五流程→login(T1/T3/T6)/refresh(T4/T6)/logout(T5)/change-password(T2)/admin-reset(T7)；§5 Cookie/CSRF/JWT→T5/T6；§5.5 CORS→T6；§6 迁移→T1；§7 错误码→T8；§8 测试矩阵→各 Task TDD+T10；§11 i18n/菜单/前端→T8/T9。**无遗漏**。
**2. Placeholder 扫描**：服务体/中间件/迁移钩子/关键测试均有完整代码；T4/T5/T6 的"占位"注释是显式跨 Task 接缝（指明哪个后续 Task 在该处补哪行），非含糊 TODO；控制器/前端 view 脚手架按房屋风格"仿 X"引用真实范本（`OperLogView`/`I18nA3ScreenSeed`/`JournalEntryController`）。
**3. 类型一致**：`IPasswordHasher.Hash/Verify/IsHashed`、`RotateAsync→(string,Sys_User)`、`LogAsync(type,userId,userName,requestTenantCode,ip,ua,reason)`、`WriteAuthCookies(resp,accessJwt,rawRefresh,csrf)` 跨 Task 一致。
**待落码前确认项**（非阻塞，首个 Task 落码时 grep 核定）：①`BizException` 真实命名空间（计划写 `CP6.Core.Exceptions`，以仓库为准）②`IUserContext` 取当前用户的确切 API ③`CP6.Core` 是否可引 `Microsoft.AspNetCore.Http.Abstractions`（决定 `AuthCookieWriter` 落 Core 还是 WebApi）④唯一索引前缀循环确切行号 ⑤i18n 合并链与菜单 seed 的确切位置。

---

*生成于 2026-06-21。源 spec：`docs/superpowers/specs/2026-06-21-auth-hardening-design.md`（4dbfb0b）。执行：subagent-driven，每 Task 先绿后本地 commit（不 push），完成后按自治模式由用户读提交监督。*
