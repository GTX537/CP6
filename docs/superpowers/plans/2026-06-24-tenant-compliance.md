# 多租户合规（Tenant Compliance）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 CP6 从"单租户可演示"推进到"多租户 SaaS 可运营"。三块全包一份 spec 一份计划：①**平台级租户管理 API**（建/改/停/续期 + 建租户同建首个 admin）②**跨租户运维 + 完整 impersonation（读写）+ 全程审计**（双向 jti 黑名单封安全洞 + Kafka 透传 ImpersonatorId）③**GDPR 双粒度数据导出 / 被遗忘权擦除（匿名化为主，purge 显式 opt-in）**。平台超管模型 = `Sys_User.IsPlatformAdmin` 带外标志位（绕 RBAC，防租户管理员自提权 A/B 向量）。源 spec：`docs/superpowers/specs/2026-06-24-tenant-compliance-design.md`（定稿 `500a8e2`，R1~R10 全嵌入）。

**Architecture:** ①新带外鉴权过滤器 `[RequirePlatformAdmin]`（仿 `RequirePermissionAttribute`，imp 期间挂起 + claim 快判 + DB 回查纵深防御）；②新 `Platform/*Controller` 端点族（带外，不入 `Sys_Menu`/`Sys_RoleMenu`）；③`Sys_User` 加 `IsPlatformAdmin` 列 + `Sys_OperLog` 加 `ImpersonatorId` 列（R1）；④`JwtHelper.GenerateToken` 扩 2 claim（`is_platform_admin`/`impersonator_id`）；⑤`SecurityEventType` 扩 19~30；⑥**完整 impersonation**：以目标用户身份签发 access、refresh Cookie 不动（隐式切出窗口）、start/end 双向拉黑被替换 jti（R2 安全洞修复）、imp 令牌 `mustChangePassword:false` 恒（R3 中间件死锁修复）、`TenantScope` 工具类保证审计落平台租户（R5）；⑦`OperLogFilter` 构造 log 时填 `ImpersonatorId`（R7 Kafka 透传）；⑧GDPR 反射统一判式 `t.FindProperty("TenantId") != null && t != Sys_Tenant`（R6 自动纳入 OperLog）+ Kahn 拓扑反向排序 + `ExecuteDeleteAsync` 单 SQL + relational 事务（InMemory 降级 = 仅验匿名化路径同 #4）；⑨`[PiiField]` 标记 + 拒名单（GDPR 擦除 + 导出剔密钥）；⑩前端平台区带外（登录响应 `isPlatformAdmin` + sessionStorage imp 态机 + start/end 替换 menus + 横幅倒计时，R8）。

**Tech Stack:** .NET 8 + EF Core 8（`ChangeTracker`/`Model.GetEntityTypes`/`ExecuteDeleteAsync`/`BeginTransaction`/`IgnoreQueryFilters`）/ `System.Text.Json`（GDPR 导出包）/ BCrypt.Net-Next（首个 admin 临时密码哈希复用 #1 `IPasswordHasher`）/ xUnit + EF InMemory + `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`（过滤器集成测）/ Vue 3.5 + element-plus + vue-i18n + axios（sessionStorage 状态机）。无新外部包。

---

## 关键既有约定（落码前必读，复核行号可能微移）

- **JWT 签发** `JwtHelper.GenerateToken(userId, userName, secret, issuer, audience, expireMinutes, tenantId?, jti?, mustChangePassword=false)`（`CP6.Core/Utilities/JwtHelper.cs` L26-62，9 参）。**T1 加两可选参** `isPlatformAdmin: bool = false` / `impersonatorId: Guid? = null` → 写 `is_platform_admin`/`impersonator_id` claim（仅在 true / 非 null 时写出，防解析端反向默认）。
- **令牌签发唯一入口** `AuthController.BuildAccessToken(user, jti, mustChange)`（L54-67，登录/刷新复用）。**T1 改签名**加 `bool isPlatformAdmin = false`，正常调用方传 `user.IsPlatformAdmin`；impersonation 流程**不复用此方法**（自带身份切换、单独签发，§T5）。
- **登录响应** `AuthController.Login` L195-202 `{ userName, nickName, roleId, menus, mustChangePassword }`。**T9 追加 `isPlatformAdmin`**（前端导航判定，R8）。
- **登出 jti 黑名单算法** `AuthController.Logout` L289-300：取 `User.FindFirst("jti")?.Value` + `User.FindFirst("exp")?.Value` → `DateTimeOffset.FromUnixTimeSeconds(expUnix) - DateTimeOffset.UtcNow` 算剩余 TTL（兜底 `SecurityOptions.Token.AccessTokenMinutes`）→ `ITokenBlacklistService.BlacklistAsync(jti, ttl)`。**T5 在 impersonation start/end 双向复用此算法**（R2 安全洞修复）。
- **Cookie 写出** `AuthCookieWriter.WriteAuthCookies(Response, token, rawRt, csrf)`（三 Cookie 一并写）。**T5 加 `WriteAccessCookieOnly(Response, token)`** 单 Cookie 写法（impersonation start/end 只换 access，refresh/CSRF 不动 → access 过期/refresh 自然回平台超管=隐式切出）。
- **令牌服务** `IRefreshTokenService.RevokeAllForUserAsync(Guid userId, bool saveChanges=true)`（**已存在**，GDPR 数据主体擦除复用，§T7）；`ITokenBlacklistService.BlacklistAsync(string jti, TimeSpan ttl)`（§T5）。
- **租户上下文** `ITenantContext.CurrentTenantId { get; set; }`（请求级 scoped、**可读可写**；默认租户 `TenantContext.DefaultTenant = 00000000-0000-0000-0000-0000000000A1`）。`TenantMiddleware` 从 `tenant_id` claim 写入。**impersonation 令牌的 `tenant_id`=目标租户 → 全部现有控制器零改即作用域到目标租户**。
- **CP6Context 反射批量**（`OnModelCreating` L1877-1885 全局过滤 + L1894-1931 唯一索引前缀；`StampTenant` L1936-1945 盖章 + `SaveChanges/SaveChangesAsync` 重写 L1947/L1953）。**本子项目无新 `BaseTenantEntity`**（仅 `Sys_User` 加列、`Sys_OperLog` 加列） → 不新增过滤/索引。GDPR 反射判式 `t.FindProperty("TenantId") != null && t != typeof(Sys_Tenant)` 自动覆盖 `BaseTenantEntity` ∪ `Sys_OperLog`（R6 §T7）。
- **跨租户遍历** `ITenantEnumerator.ListActiveAsync()`（仅 `Enable=true`） + `TenantScopeRunner.ForEachTenantAsync`（逐租户开 scope 设 `CurrentTenantId`）。GDPR 整租户路径**不需要遍历**（按 tenantId 直查），故不复用。
- **安全审计** `ISecurityAuditService.LogAsync(SecurityEventType type, Guid? userId, string? userName, string? requestTenantCode, string? ip, string? ua, string? reason = null)`：**失败不阻断**+自动截断。`Sys_SecurityLog : BaseTenantEntity`。**不显式盖 TenantId**——靠 `CP6Context.StampTenant` 盖 `CurrentTenantId`（R5）→ **平台审计前须保证 `CurrentTenantId=平台租户`**，§T5 提供 `using` 工具类 `TenantScope`。
- **OperLog 主通道** `OperLogFilter.OnActionExecutionAsync`（`CP6.WebApi/Filters/OperLogFilter.cs` L75-142）：跳 `/api/auth`/`/api/operlog`（L80-83），构造 `log = new Sys_OperLog { TenantId=_context.CurrentTenantId, … }`（L100-114），Kafka 主投递（L116-129） + DB 降级（L132-142）。**T6 在 L100-114 构造时填 `log.ImpersonatorId = ParseGuid(User.FindFirst("impersonator_id"))`**——Kafka payload 透传到 consumer（R7）。
- **MustChangePasswordMiddleware** 读 `must_change_password` claim 不查库，`AllowPaths = ["/api/auth/change-password", "/api/auth/logout"]`。**T5 imp 令牌恒 `mustChangePassword:false`（R3）+ 可选加固**：把 `/api/platform/impersonation/end` 加入 `AllowPaths` 兜底（防误传 `true` 锁死）。
- **AuthController 跨租户登录消歧**（`Login` L76-203）：按 `UserName` `IgnoreQueryFilters` 查多租户 → `_tenant.CurrentTenantId = user.TenantId` → `BuildAccessToken` → `PrewarmAsync` → 三 Cookie。`/api/auth/login` 既有"租户已停用"拦截（L90/L152）→ T3 `suspend` 端点设 `Enable=false` 后该租户用户自动登录被拒，无需重复实现。
- **`Sys_User` 现状**：`UserName/Password` `[Required]`、`Enable` bool、`MustChangePassword` bool、`NickName/Email/LastLoginIp` 可空（**T7 标 `[PiiField]`**）；`BaseEntity.Creator/Modifier` 可空、`Id` `Guid.NewGuid()` 默认、`CreateDate` 默认。**T3 建首个 admin 字段集**：`UserName + Password(BCrypt) + TenantId(显式新租户) + RoleId=1 + Enable=true + MustChangePassword=true + IsPlatformAdmin=false`（已足，R4 核验）。
- **`Sys_Tenant` 现状**：共享表（继承 `BaseEntity` 非 `BaseTenantEntity`）；字段 `TenantCode/TenantName/Enable/ExpireDate/Remark`；**无 IsDeleted**，停用=`Enable=false`。多租户全局过滤对此表不生效 → T3 直接 CRUD。
- **`Sys_OperLog` 现状**：`int Id` 非 `BaseTenantEntity`，但**手加 `TenantId` 列**（`Sys_OperLog.cs` L19）+ `StampTenant` 显式补盖（`CP6Context` L1942-1944）。**T1 同范式加 `Guid? ImpersonatorId`**（手加列，可空，无 `[Required]`）。
- **种子** `Program.cs`：全局 `RoleId=1 管理员` 持全菜单/操作（L576+）；admin 用户 `UserName="admin" && TenantId 未显式` → `StampTenant` 落默认租户 `…A1`（L610-618）。**T8 加两段幂等 seed**：①`Sys_Tenant` 默认租户行（令 `ITenantEnumerator` 可列出）；②引导首个平台超管（默认租户 admin → `IsPlatformAdmin=true`）。**不 seed 任何平台菜单/权限点**（决策 D1 带外；MenuId 117 仍空闲留后续）。
- **错误码** 现 `E-SEC-001~010`（#1）；#2 规划 011~02x、#3 020~029（spec 未落码） → **#5 取 E-SEC-031~038**（§T1 占位、§T9 五语 seed）。
- **i18n / 分页 / 控制器 / 前端范本**（沿用 #1/#4 已核验）：`I18nSecScreenSeed`（`public static readonly Sys_Lang[] Items`，Program.cs `.Concat` 接入）；`SecurityLogController`（`page=Math.Max(1,page)`、`pageSize=Math.Clamp(pageSize,1,200)`、`OrderByDescending`、返 `{total,rows}`、`SecurityLogControllerTests` 直 `new` 控制器绕过过滤器）；前端 `views/pms/SecurityLogView.vue`、`router/index.ts` `viewModules`、`api/sys/`。
- **`SecurityOptions`** 现含 `Token.AccessTokenMinutes`（#1）。**T1 加 `ImpersonationMinutes`(默认 30)**。
- **测试基线** 当前 **996 测**（首 Task `dotnet test` 核对）；`TestHelper.CreateInMemoryContext()` 单参 `CP6Context(options)`。

### 关键类型签名（跨 Task 一致，勿改名）

```csharp
// CP6.Entity
[AttributeUsage(AttributeTargets.Property)]
public sealed class PiiFieldAttribute : Attribute
{
    public PiiErase Mode { get; init; } = PiiErase.Placeholder;
}
public enum PiiErase { Placeholder, Null }

// CP6.Entity/DomainModels/Sys/Sys_User.cs（追加列）
public bool IsPlatformAdmin { get; set; }

// CP6.Entity/DomainModels/Sys/Sys_OperLog.cs（追加列，手加列同 TenantId 范式）
public Guid? ImpersonatorId { get; set; }

// CP6.Entity/DomainModels/Sys/SecurityEventType.cs（追加）
TenantCreated = 19, TenantUpdated = 20, TenantSuspended = 21, TenantReactivated = 22,
PlatformAdminGranted = 23, PlatformAdminRevoked = 24,
ImpersonationStarted = 25, ImpersonationEnded = 26,
GdprTenantExported = 27, GdprSubjectExported = 28,
GdprTenantErased = 29, GdprSubjectErased = 30,

// CP6.Core/Auth/RequirePlatformAdminAttribute.cs（仿 RequirePermissionAttribute）
public sealed class RequirePlatformAdminAttribute : Attribute, IAsyncAuthorizationFilter { … }

// CP6.Core/Services/Platform/*（块①②③ 服务层，控制器瘦）
public interface ITenantAdminService {
    Task<PagedResult<TenantRow>> ListAsync(string? keyword, bool? enable, int page, int pageSize);
    Task<TenantDetail?> GetAsync(Guid id);
    Task<CreateTenantResult> CreateAsync(string code, string name, DateTime? expire, string? remark, string adminUserName);
    Task UpdateAsync(Guid id, string name, DateTime? expire, string? remark);
    Task SuspendAsync(Guid id); Task ReactivateAsync(Guid id);
}
public record CreateTenantResult(Guid TenantId, string AdminUserName, string TempPassword);

public interface IPlatformAdminService {
    Task<List<PlatformAdminRow>> ListAsync();
    Task GrantAsync(Guid userId);
    Task RevokeAsync(Guid userId);   // 防自锁死 E-SEC-037
}

public interface IImpersonationService {
    Task<ImpersonationStartResult> StartAsync(Guid tenantId, Guid? userId, string? reason, ClaimsPrincipal current, HttpResponse response);
    Task<ImpersonationEndResult> EndAsync(ClaimsPrincipal current, HttpResponse response);
}
public record ImpersonationStartResult(Guid TenantId, string TenantName, Guid UserId, string UserName,
                                       List<MenuRow> Menus, int ExpiresInMinutes);
public record ImpersonationEndResult(List<MenuRow> Menus);

public interface IGdprService {
    Task<Stream> ExportTenantAsync(Guid tenantId);
    Task<Stream> ExportSubjectAsync(Guid userId);
    Task EraseSubjectAsync(Guid userId);
    Task EraseTenantAsync(Guid tenantId, string mode);   // "anonymize"|"purge"
}

// CP6.Core/Services/Common/TenantScope.cs（R5 工具类）
public sealed class TenantScope : IDisposable {
    public TenantScope(ITenantContext ctx, Guid target);   // ctor 替换 + 保旧值；Dispose 还原
    public void Dispose();
}

// CP6.Core/Services/Sys/SecurityOptions.cs（追加）
public int ImpersonationMinutes { get; set; } = 30;
```

### 平台端点矩阵（块①②③ 路由对齐 spec §4/§5/§6）

| Method | Path | Controller | 守卫 | 审计事件 |
|---|---|---|---|---|
| GET | `/api/platform/tenant?…` | TenantController | `[RequirePlatformAdmin]` | — |
| GET | `/api/platform/tenant/{id}` | TenantController | `[RequirePlatformAdmin]` | — |
| POST | `/api/platform/tenant` | TenantController | `[RequirePlatformAdmin]` | TenantCreated(19) |
| PUT | `/api/platform/tenant/{id}` | TenantController | `[RequirePlatformAdmin]` | TenantUpdated(20) |
| POST | `/api/platform/tenant/{id}/suspend` | TenantController | `[RequirePlatformAdmin]` | TenantSuspended(21) |
| POST | `/api/platform/tenant/{id}/reactivate` | TenantController | `[RequirePlatformAdmin]` | TenantReactivated(22) |
| GET/POST | `/api/platform/admin[/{id}/grant\|revoke]` | PlatformAdminController | `[RequirePlatformAdmin]` | 23/24 |
| POST | `/api/platform/impersonation/start` | ImpersonationController | `[RequirePlatformAdmin]` | ImpersonationStarted(25) |
| POST | `/api/platform/impersonation/end` | ImpersonationController | `[Authorize]` 自查 imp claim | ImpersonationEnded(26) |
| GET/DELETE | `/api/platform/gdpr/export\|erase/…` | GdprController | `[RequirePlatformAdmin]` | 27~30 |
| GET | `/api/platform/audit?…` | CrossTenantAuditController | `[RequirePlatformAdmin]` | —（R10 独立） |

---

## Tasks 总览

| T | 范围 | 依赖 | 提交 |
|---|---|---|---|
| T1 | 数据模型：`Sys_User.IsPlatformAdmin` + `Sys_OperLog.ImpersonatorId` + `[PiiField]` + `SecurityEventType` 19~30 + `JwtHelper` 扩 2 可选参 + `SecurityOptions.ImpersonationMinutes` + EF 迁移 | — | 1 |
| T2 | `[RequirePlatformAdmin]` 过滤器（imp 拒 + claim 快判 + DB 回查纵深）+ `TenantScope` 工具类 + `WebApplicationFactory` 三道闸集成测（R9 闸门测） | T1 | 1 |
| T3 | **块①** `ITenantAdminService` + `TenantController`（5 端点）+ 建租户事务原子（R9-a 失败回滚）+ 测试 | T2 | 1 |
| T4 | `IPlatformAdminService` + `PlatformAdminController`（3 端点，防自锁死 E-SEC-037）+ 测试 | T2 | 1 |
| T5 | **块② heart**：`IImpersonationService` + `ImpersonationController` + `AuthCookieWriter.WriteAccessCookieOnly` + **双向 jti 黑名单（R2）** + **imp 令牌 mustChange:false 恒（R3）** + **TenantScope 落平台租户（R5）** + 返 menus（R8）+ 测试 | T2,T4 | 1 |
| T6 | **R7** `OperLogFilter` 构造 log 时填 `ImpersonatorId`（Kafka 主通道 + DB 降级两路径单测） | T1,T5 | 1 |
| T7 | **块③** `IGdprService` + `GdprController`：导出（双粒度剔密钥）+ 数据主体擦除（PII 匿名化 + `RevokeAllForUserAsync`）+ 整租户擦除（R6 反射统一判式 + Kahn 拓扑 + `ExecuteDeleteAsync` + relational 事务 + InMemory 降级测）+ 防护 E-SEC-036/037/038 | T2,T5 | 1 |
| T8 | **R10 独立** `CrossTenantAuditController`（跨租户审计查询）+ 种子（默认租户 `Sys_Tenant` + 引导首个平台超管）+ DI 注册 4 服务 | T3~T7 | 1 |
| T9 | i18n `I18nTenantComplianceSeed` 五语（E-SEC-031~038 + `sec.event.19~30` + `platform.*` 画面词条）+ `AuthController.Login` 响应加 `isPlatformAdmin` + 前端平台区（5 视图 + `stores/platform.ts` + `api/platform/*` + router）**含 R8 sessionStorage 态机 + start/end 替换 menus + 横幅倒计时 + LoginView localStorage 扩字段** | T3~T8 | 1 |
| T10 | gstack 真浏览器 QA 全流程（含 R9 三条对抗：imp 期间访 `/api/platform/*` 403 / 切出后旧 imp Cookie 重放 401 / 双标签 sessionStorage 隔离） | 全部 | — |

> 每 Task 先绿色构建+全量测试再本地 `git commit`（**不 push**，push 由用户监督）。subagent-driven：每 Task 派 subagent → spec审 + 质量审双过 → 先绿后 commit。基线现 **996 测**（首 Task `dotnet test` 核对）。

---

## Task T1：数据模型 + JwtHelper 扩参 + 配置 + 迁移

**Files:** Modify `CP6.Entity/DomainModels/Sys/Sys_User.cs`、`CP6.Entity/DomainModels/Sys/Sys_OperLog.cs`、`CP6.Entity/DomainModels/Sys/SecurityEventType.cs`、`CP6.Core/Utilities/JwtHelper.cs`、`CP6.Core/Services/Sys/SecurityOptions.cs`、`CP6.WebApi/Controllers/Sys/AuthController.cs`(`BuildAccessToken` 签名)、`appsettings.json`；Create `CP6.Entity/PiiFieldAttribute.cs`、EF 迁移 `TenantCompliance`。**本 Task 仅建模型/扩 claim，无业务行为**（标 `[PiiField]` 此刻惰性）。

- [ ] **Step 1: `[PiiField]`** `CP6.Entity/PiiFieldAttribute.cs`（粘"关键类型签名"，`namespace CP6.Entity;`）。
- [ ] **Step 2: `Sys_User` 加 `IsPlatformAdmin`** + `[PiiField]` 标注 `NickName`(Mode=Null)/`Email`(Mode=Null)/`LastLoginIp`(Mode=Null)（`using CP6.Entity;`）。`UserName/Password` 不标——T7 自管擦除策略（保唯一性 + 哈希）。
- [ ] **Step 3: `Sys_OperLog` 加 `ImpersonatorId`**（`public Guid? ImpersonatorId { get; set; }`，无 `[Required]`；同 L19 `TenantId` 手加列注释范式）。
- [ ] **Step 4: `SecurityEventType` 扩 19~30**（粘签名）。
- [ ] **Step 5: `JwtHelper.GenerateToken` 扩 2 可选参** `bool isPlatformAdmin = false, Guid? impersonatorId = null`：claims 数组改 `var claims = new List<Claim>{ … }`，**条件追加**：`if (isPlatformAdmin) claims.Add(new("is_platform_admin","true"));` / `if (impersonatorId.HasValue) claims.Add(new("impersonator_id", impersonatorId.Value.ToString()));`（**仅在 true / 非 null 时写出**，防解析端默认值反向）。
- [ ] **Step 6: `AuthController.BuildAccessToken` 扩参** `bool isPlatformAdmin = false`，正常 Login/Refresh 调用方传 `user.IsPlatformAdmin`（保持向后兼容默认 false）。
- [ ] **Step 7: `SecurityOptions` 加 `ImpersonationMinutes=30`**；`appsettings.json` `Security` 段加 `"ImpersonationMinutes": 30`。
- [ ] **Step 8: EF 迁移** `dotnet ef migrations add TenantCompliance --project CP6.Core --startup-project CP6.WebApi` → 验生成两处 DDL（`Sys_User.IsPlatformAdmin` bit not null default 0 + `Sys_OperLog.ImpersonatorId` uniqueidentifier null）。
- [ ] **Step 9: 测试** 既有测试不受影响（默认 false / null）；`dotnet test` 应全绿 996 测保持。

**Acceptance:** `dotnet build` 绿；`dotnet test` ≥996 绿；迁移 SQL 含两列 DDL。本地 commit `feat(sec5): T1 数据模型 + JwtHelper 扩 claim + 迁移`。

---

## Task T2：`[RequirePlatformAdmin]` 过滤器 + `TenantScope` 工具类 + 三道闸集成测

**Files:** Create `CP6.Core/Auth/RequirePlatformAdminAttribute.cs`、`CP6.Core/Services/Common/TenantScope.cs`、`CP6.Test/Auth/RequirePlatformAdminFilterTests.cs`。

- [ ] **Step 1: `RequirePlatformAdminAttribute`** 仿 `RequirePermissionAttribute`（`IAsyncAuthorizationFilter`，`OnAuthorizationAsync`）。三道闸顺序（**勿改顺**）：
  1. `if (user.FindFirst("impersonator_id") != null) → Forbid("E-SEC-034");` （imp 期间挂起平台权）。
  2. `if (user.FindFirst("is_platform_admin")?.Value != "true") → Forbid("E-SEC-031");` （claim 快判）。
  3. **DB 回查纵深**：`var db = ctx.HttpContext.RequestServices.GetService<CP6Context>();`、`Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)`、`await db.Sys_Users.IgnoreQueryFilters().AnyAsync(u => u.Id==uid && u.IsPlatformAdmin && u.Enable)` → 否则 `Forbid("E-SEC-031")`。
  - `Forbid(string code)` = `ctx.Result = new ObjectResult(new { code=403, message=code }){ StatusCode=403 };` 内联私有方法。
- [ ] **Step 2: `TenantScope` 工具类**（粘签名）：ctor 缓存 `ctx.CurrentTenantId` 旧值 → 写入 target；`Dispose` 还原。用法 `using (new TenantScope(_tenant, TenantContext.DefaultTenant)) { await _audit.LogAsync(…); }`（R5 保证审计落平台租户）。
- [ ] **Step 3: 单测 `RequirePlatformAdminFilterTests`** （`WebApplicationFactory` 集成测，R9-a/b/c 三道闸）：
  - 起 `WebApplicationFactory<Program>` + InMemory DB → 注入一个测试 `/test-platform` 端点带 `[RequirePlatformAdmin]`。
  - **case (a)** 无 token / 无 `is_platform_admin` claim → POST → 验 403 + body `code=403, message="E-SEC-031"`。
  - **case (b)** claim 在但 DB 中该 user `IsPlatformAdmin=false`（被另一平台超管撤销而 token 未过期）→ 403 E-SEC-031（验纵深防御）。
  - **case (c)** `impersonator_id` claim 在（imp 期间）→ 403 E-SEC-034（验 imp 拒优先于 claim 判定，顺序铁律）。
  - **case (d)** claim 在 + DB `IsPlatformAdmin=true && Enable=true` → 200。

**Acceptance:** 4 个集成测全绿；`dotnet test` ≥1000 绿。本地 commit `feat(sec5): T2 [RequirePlatformAdmin] 三道闸 + TenantScope + R9 集成测`。

---

## Task T3：块① `ITenantAdminService` + `TenantController` + 建租户事务原子（R9-a）

**Files:** Create `CP6.Core/Services/Platform/ITenantAdminService.cs` + `TenantAdminService.cs`、`CP6.WebApi/Controllers/Platform/TenantController.cs`、`CP6.Test/Platform/TenantAdminServiceTests.cs` + `TenantControllerTests.cs`。

- [ ] **Step 1: `ITenantAdminService`** 粘"关键类型签名"。
- [ ] **Step 2: `TenantAdminService` 实现**：构造注入 `CP6Context _db, IPasswordHasher _hasher, ISecurityAuditService _audit, ITenantContext _tenant`。
  - `ListAsync(keyword, enable, page, pageSize)`：`page=Math.Max(1,page); pageSize=Math.Clamp(pageSize,1,200);`；查 `_db.Sys_Tenants`（共享表，无需 `IgnoreQueryFilters`）按 `TenantCode/TenantName Contains keyword`、`Enable` 过滤；投影含 `userCount = _db.Sys_Users.IgnoreQueryFilters().Count(u => u.TenantId==t.Id)`（避免 N+1 用 `Select` + group join）；`OrderByDescending(t => t.CreateDate)`；返 `(rows, total)`。
  - `GetAsync(id)`：`_db.Sys_Tenants.FirstOrDefaultAsync(t => t.Id==id)`。
  - **`CreateAsync(code, name, expire, remark, adminUserName)`（R9-a 事务原子）**：
    1. 校验 `_db.Sys_Tenants.AnyAsync(t => t.TenantCode==code)` → 是则抛 `BizException("E-SEC-033")`（控制器中间件本地化）。
    2. `var tenant = new Sys_Tenant { Id=Guid.NewGuid(), TenantCode=code, TenantName=name, Enable=true, ExpireDate=expire, Remark=remark };`
    3. `var tempPwd = GenerateRandomPassword(16);`（16 字符随机：大小写+数字+符号）；`var admin = new Sys_User { Id=Guid.NewGuid(), UserName=adminUserName, Password=_hasher.Hash(tempPwd), TenantId=tenant.Id /*显式不靠盖章*/, RoleId=1, IsPlatformAdmin=false, Enable=true, MustChangePassword=true };`
    4. `_db.Sys_Tenants.Add(tenant); _db.Sys_Users.Add(admin); await _db.SaveChangesAsync();`（**单 SaveChanges 一并提交，EF 自动事务包裹 → 任一 DbUpdateException 整体回滚**）。
    5. 审计 `using (new TenantScope(_tenant, TenantContext.DefaultTenant)) await _audit.LogAsync(TenantCreated, currentUserId, currentUserName, code, null, null, $"admin={adminUserName}");`（R5 落平台租户）。
    6. 返 `new CreateTenantResult(tenant.Id, adminUserName, tempPwd)`——**临时明文仅本次返回一次**。
  - `UpdateAsync(id, name, expire, remark)`：先查后改（`FirstOrDefaultAsync` → 非空赋值 → SaveChanges）；不存在 → `E-SEC-032`；审计 `TenantUpdated`。
  - `SuspendAsync(id)`：`Enable=false`；审计 `TenantSuspended`。
  - `ReactivateAsync(id)`：`Enable=true`；审计 `TenantReactivated`。
- [ ] **Step 3: `TenantController`** 类级 `[ApiController][Route("api/platform/tenant")][Authorize][RequirePlatformAdmin]`；构造注入 `ITenantAdminService _svc`；5 端点薄包装服务调用 → 返 `Ok(...)`。
- [ ] **Step 4: 单测 `TenantAdminServiceTests`** 直构服务 + InMemory：
  - 建租户成功 → 验 `Sys_Tenant` + `Sys_User` 同时存在、admin `TenantId==新租户` / `IsPlatformAdmin==false` / `MustChangePassword==true` / `Password` 经 `_hasher.Verify(tempPwd, admin.Password)` 验。
  - 编码冲突 → 抛 `BizException("E-SEC-033")`。
  - **R9-a 事务原子失败回滚**：注入假 `IPasswordHasher` 在 `Hash` 时抛 → 验 `Sys_Tenant` 行**也不存在**（验回滚）。
  - 停用/重启用切 `Enable` + 审计行 `TenantId==DefaultTenant`（验 R5 落平台租户）。
  - 列表 keyword/enable 过滤、分页 clamp、`userCount` 正确（建 3 用户后查投影=3）。
- [ ] **Step 5: 控制器测试 `TenantControllerTests`** 直 `new TenantController(svc)` 绕过滤器 → 端到端跑一遍 5 端点。

**Acceptance:** 测试全绿。本地 commit `feat(sec5): T3 块① 租户管理 API + 建租户事务原子 R9-a`。

---

## Task T4：`IPlatformAdminService` + `PlatformAdminController` + 防自锁死

**Files:** Create `CP6.Core/Services/Platform/IPlatformAdminService.cs` + `PlatformAdminService.cs`、`CP6.WebApi/Controllers/Platform/PlatformAdminController.cs`、`CP6.Test/Platform/PlatformAdminServiceTests.cs`。

- [ ] **Step 1: `IPlatformAdminService`** 粘签名。
- [ ] **Step 2: `PlatformAdminService` 实现**：
  - `ListAsync()` → `_db.Sys_Users.IgnoreQueryFilters().Where(u => u.IsPlatformAdmin).Select(…)`。
  - `GrantAsync(userId)`：`IgnoreQueryFilters` 查任意租户用户 → 不存在 `BizException("E-SEC-032")`；置 `IsPlatformAdmin=true`；审计 `PlatformAdminGranted`（`TenantScope` 落平台租户）。
  - **`RevokeAsync(userId)`（防自锁死 E-SEC-037）**：先查目标；若 `target.IsPlatformAdmin && target.Enable`，**计数**：`_db.Sys_Users.IgnoreQueryFilters().Where(u => u.IsPlatformAdmin && u.Enable).CountAsync() == 1` → 抛 `BizException("E-SEC-037")`；置 `IsPlatformAdmin=false`；审计 `PlatformAdminRevoked`。
- [ ] **Step 3: `PlatformAdminController`** 类级 `[Authorize][RequirePlatformAdmin]`；薄包装 + 路由 `api/platform/admin`、`POST /{userId}/grant|revoke`。
- [ ] **Step 4: 测试**：grant → 验列出含该 user；revoke 最后一个 → E-SEC-037；revoke 非最后 → 成功；grant 目标不存在 → E-SEC-032；所有路径审计行落平台租户。

**Acceptance:** 测试全绿。本地 commit `feat(sec5): T4 块② 平台超管授撤 + 防自锁死 E-SEC-037`。

---

## Task T5：块② heart — `IImpersonationService` + 双向 jti 黑名单 + 令牌前置不变量 + TenantScope

**Files:** Create `CP6.Core/Services/Platform/IImpersonationService.cs` + `ImpersonationService.cs`、`CP6.WebApi/Controllers/Platform/ImpersonationController.cs`；Modify `CP6.WebApi/Auth/AuthCookieWriter.cs`（加 `WriteAccessCookieOnly`）、`CP6.WebApi/Middleware/MustChangePasswordMiddleware.cs`（`/api/platform/impersonation/end` 进 `AllowPaths`，R3 兜底）；Create tests `ImpersonationServiceTests.cs`。

- [ ] **Step 1: `AuthCookieWriter.WriteAccessCookieOnly(HttpResponse res, string token)`** 仅写 `cp6_at`（access）Cookie，复用既有 `CookieOptions`（httpOnly/SameSite/Secure）；不动 refresh/csrf。
- [ ] **Step 2: `IImpersonationService`** 粘签名。
- [ ] **Step 3: `ImpersonationService.StartAsync(tenantId, userId, reason, current, response)`**：构造注入 `CP6Context _db, ITokenBlacklistService _blacklist, ISecurityAuditService _audit, ITenantContext _tenant, IOptions<SecurityOptions> _sec, AuthCookieWriter _cookies, IPermissionService _perm`。流程：
  1. 校验目标租户 `_db.Sys_Tenants.FirstOrDefaultAsync(t => t.Id==tenantId && t.Enable)` → 否则 `BizException("E-SEC-032")`。
  2. 目标用户 = 入参 `userId` 解析；为 null 则 `_db.Sys_Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.TenantId==tenantId && u.RoleId==1 && u.Enable)` 取首个 admin；不存在/停用 → `BizException("E-SEC-035")`。
  3. **R2 上半 拉黑被替换平台 access jti**：`var oldJti = current.FindFirst("jti")?.Value; if (!string.IsNullOrEmpty(oldJti)) { var ttl = ComputeRemainingTtl(current, _sec.Value.Token.AccessTokenMinutes); await _blacklist.BlacklistAsync(oldJti, ttl); }`。
     - `ComputeRemainingTtl(principal, defaultMinutes)` = 私有 helper（粘 `AuthController.Logout` L289-300 算法）。
  4. **R3 imp 令牌 `mustChangePassword:false` 恒**：`var newJti = Guid.NewGuid().ToString(); var token = JwtHelper.GenerateToken(target.Id.ToString(), target.UserName, _sec.Value.Token.Secret, _sec.Value.Token.Issuer, _sec.Value.Token.Audience, _sec.Value.ImpersonationMinutes, tenantId: tenantId, jti: newJti, mustChangePassword: false, isPlatformAdmin: false, impersonatorId: platformAdminUserId);`。
  5. `_cookies.WriteAccessCookieOnly(response, token);`（refresh Cookie 不动 → 隐式切出窗口）。
  6. **R5 审计落平台租户**：`using (new TenantScope(_tenant, TenantContext.DefaultTenant)) await _audit.LogAsync(SecurityEventType.ImpersonationStarted, platformAdminUserId, platformAdminUserName, targetTenant.TenantCode, …, reason);`。
  7. **R8 返 menus**：调 `_perm.GetAggregatedMenusAsync(target.Id, target.RoleId)`（或复用 `AuthController.Login` L177-181 的菜单并集查询逻辑，抽 helper），返目标用户的菜单列表 → `new ImpersonationStartResult(tenantId, targetTenant.TenantName, target.Id, target.UserName, menus, _sec.Value.ImpersonationMinutes)`。
- [ ] **Step 4: `ImpersonationService.EndAsync(current, response)`**：
  1. 校验 `impersonator_id` claim 存在 → 否则抛 `BizException("E-SEC-031")`。
  2. **R2 下半 拉黑当前 imp access jti（安全洞修复）**：同 ComputeRemainingTtl + `_blacklist.BlacklistAsync(impJti, ttl)`。
  3. 解析 `impersonator_id` → `Guid.Parse` → `_db.Sys_Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id==impId && u.IsPlatformAdmin && u.Enable)`；**R9-c 真身已撤销/停用** → 抛 `BizException("E-SEC-031")`（保证撤销立即生效）。
  4. **R5 切回平台租户**：`_tenant.CurrentTenantId = TenantContext.DefaultTenant;`（直接赋值，进入平台审计上下文）。
  5. 重签：`var token = JwtHelper.GenerateToken(impAdmin.Id.ToString(), impAdmin.UserName, …, tenantId: impAdmin.TenantId, jti: newJti, mustChangePassword: impAdmin.MustChangePassword, isPlatformAdmin: true, impersonatorId: null);`
  6. `_cookies.WriteAccessCookieOnly(response, token);`
  7. 审计 `ImpersonationEnded`（此刻 `CurrentTenantId == 平台租户`，靠 `StampTenant` 落对）。
  8. 返目标=平台超管自身的菜单列表 → `new ImpersonationEndResult(menus)`。
- [ ] **Step 5: `ImpersonationController`** 路由 `api/platform/impersonation`；`POST /start` 类级 `[Authorize][RequirePlatformAdmin]`；`POST /end` 方法级 `[Authorize]`（无 `[RequirePlatformAdmin]`，因当前 token 无 `is_platform_admin`；服务层自查 `impersonator_id` claim）。
- [ ] **Step 6: R3 兜底加固** `MustChangePasswordMiddleware.AllowPaths` 加 `"/api/platform/impersonation/end"`。
- [ ] **Step 7: 测试 `ImpersonationServiceTests`**：
  - `StartAsync` 签发令牌 → 验解析后 `tenant_id`==目标租户 / `NameIdentifier`==目标用户 / `impersonator_id`==真身 / 无 `is_platform_admin` claim / `must_change_password`==false（**R3 验证**）。
  - 目标用户不存在 → `E-SEC-035`；目标租户不存在 → `E-SEC-032`。
  - **R9-b 双向 jti 黑名单**：start 调 `_blacklist.BlacklistAsync(原 jti, ttl)`（spy `ITokenBlacklistService`）；end 调 `_blacklist.BlacklistAsync(imp jti, ttl)`。
  - **R9-c end 时真身已撤销** → 抛 `E-SEC-031`。
  - **R5 LogAsync 调用时 CurrentTenantId==平台租户**：spy `ISecurityAuditService` 在调用瞬间读 `_tenant.CurrentTenantId`，验 = `DefaultTenant`（覆盖 start 路径 = TenantScope 包；end 路径 = 显式赋值）。
  - 返 `menus` 字段非空、`expiresInMinutes==_sec.Value.ImpersonationMinutes`。

**Acceptance:** 测试全绿。本地 commit `feat(sec5): T5 块② impersonation heart + R2 双向jti + R3 mustChange恒false + R5 TenantScope`。

---

## Task T6：R7 `OperLogFilter` 填 `ImpersonatorId`（Kafka 透传）

**Files:** Modify `CP6.WebApi/Filters/OperLogFilter.cs`、`CP6.Test/Filters/OperLogFilterTests.cs`（如无则建）。

- [ ] **Step 1: `OperLogFilter.OnActionExecutionAsync` L100-114 改造**：构造 `log = new Sys_OperLog { … }` 时追加：
  ```csharp
  ImpersonatorId = Guid.TryParse(context.HttpContext.User.FindFirst("impersonator_id")?.Value, out var impId)
                    ? impId
                    : (Guid?)null,
  ```
  - 在 `TenantId` 后插入；**勿改投递路径**——`_transport.PublishAsync(log)` 与 `_context.Sys_OperLogs.Add(log)` 自然透传新列（Kafka payload JSON 序列化含此字段，consumer 反序列化落 DB）。
- [ ] **Step 2: 单测 `OperLogFilterTests`**（仿 `SecurityLogControllerTests` 直构 filter）：
  - 注入 `ClaimsPrincipal` 含 `impersonator_id` claim → 调 `OnActionExecutionAsync` → 验 `Sys_OperLog.ImpersonatorId == 真身.Id`。
  - 无 `impersonator_id` claim → `ImpersonatorId == null`。
  - Kafka 路径 vs DB 降级路径**两路径**均验（mock `IOperLogTransport.IsConnected`=true/false）：两条路径 `log` 对象在投递/写入时都已含字段（**关键**：T6 不在两路径之间分别填，而在构造时一次填，保 Kafka payload 透传到 consumer）。

**Acceptance:** 测试全绿。本地 commit `feat(sec5): T6 R7 OperLogFilter 填 ImpersonatorId + Kafka 透传`。

---

## Task T7：块③ `IGdprService` + `GdprController` + R6 反射拓扑 + 防护

**Files:** Create `CP6.Core/Services/Platform/IGdprService.cs` + `GdprService.cs`、`CP6.WebApi/Controllers/Platform/GdprController.cs`、`CP6.Core/Services/Platform/SensitiveFieldPolicy.cs`（拒名单 + PII 反射擦除）、`CP6.Core/Services/Platform/TenantPurgeTopology.cs`（Kahn 拓扑）、`CP6.Test/Platform/GdprServiceTests.cs`。

- [ ] **Step 1: `SensitiveFieldPolicy`** 静态类：
  - `IsSensitive(string columnName)`：大小写不敏感后缀/全名匹配 `["Secret","Hash","Password","TokenHash","Salt","ClientSecretProtected","TwoFactorSecret"]`（spec §2.3，与 #4 同源；#4 若已落则复用其类型，否则本期自建）。
  - `EraseSubjectAsync(CP6Context db, Guid userId)`：取 `Sys_User`（`IgnoreQueryFilters`） → 反射遍历 `typeof(Sys_User).GetProperties()`，对 `[PiiField]` 列按 `Mode` 擦（Placeholder=`$"REDACTED-{Id前8}"`、Null=`null`）；`UserName`→`$"anon-{Id.ToString("N")[..8]}"`、`Password`→`_hasher.Hash(Guid.NewGuid().ToString("N"))`、`Enable=false`；**保留行 + Id**。
- [ ] **Step 2: `TenantPurgeTopology`** 静态类：
  - `GetOwnerEntityTypes(IModel model)` = `model.GetEntityTypes().Where(t => t.FindProperty("TenantId") != null && t.ClrType != typeof(Sys_Tenant)).Select(t => t.ClrType).ToList();`（**R6 统一判式，自动纳入 OperLog**）。
  - `BuildDeleteOrder(IModel model)`：Kahn 拓扑——节点=实体类型，边=FK `(Child → Parent)`；出栈顺序 = 先无入度叶子 → 反向得**删除顺序**（leaf-first），返 `List<Type>`。自引用环（`Wf_*` 父子）检测：若拓扑结束仍有未访问节点（在环中），返这些节点列表供 `EraseTenantAsync` 先一次性 `UPDATE SET ParentId=NULL`。
- [ ] **Step 3: `IGdprService`** 粘签名。
- [ ] **Step 4: `GdprService` 实现**：构造注入 `CP6Context _db, IPasswordHasher _hasher, IRefreshTokenService _refresh, ITokenBlacklistService _blacklist, ISecurityAuditService _audit, ITenantContext _tenant`。
  - **`ExportTenantAsync(tenantId)`**：校验存在 → 取 `Sys_Tenant` 行 + `TenantPurgeTopology.GetOwnerEntityTypes()` 每类 `_db.Set(clrType).IgnoreQueryFilters().Where(EF.Property<Guid>(e,"TenantId")==tenantId).ToListAsync()` → **逐行剔密钥**（反射遍历属性，`SensitiveFieldPolicy.IsSensitive(p.Name)` 则跳过） → 序列化 `{ tenant, data: { "TableName": [...] } }` JSON → 返 `MemoryStream`。审计 `GdprTenantExported`（TenantScope 落平台租户）。
  - **`ExportSubjectAsync(userId)`**：取 `Sys_User`（`IgnoreQueryFilters`） + `Sys_SecurityLog.IgnoreQueryFilters().Where(l => l.UserId==userId)` + `Sys_OperLog.IgnoreQueryFilters().Where(l => l.UserName==user.UserName)` + best-effort 遍历 `Creator/Modifier==user.UserName` 的 `BaseEntity` 子类（反射枚举）→ 剔密钥 → JSON。审计 `GdprSubjectExported`。
  - **`EraseSubjectAsync(userId)`**：
    1. 防护 `_db.Sys_Users.IgnoreQueryFilters().FirstAsync(u=>u.Id==userId)` → 若 `target.IsPlatformAdmin`：(a) 平台租户用户 → `BizException("E-SEC-036")`；(b) 否则若 `Count(IsPlatformAdmin && Enable)==1` → `E-SEC-037`。
    2. `SensitiveFieldPolicy.EraseSubjectAsync(_db, userId)`（匿名化 + 哈希 + Enable=false）。
    3. `await _refresh.RevokeAllForUserAsync(userId);`（吊销 refresh 令牌族）。**jti 黑名单**：当前实现无"按 userId 拉黑全部 jti"，按 spec §6.3 第 3 步**仅吊销 refresh 即可**（access TTL 分钟级，过期后无法续 → 自然失效）；标 §10 局限（即时 access 在 TTL 内仍可用）。
    4. `_db.SaveChangesAsync();` 审计 `GdprSubjectErased`（TenantScope 落平台租户）。
  - **`EraseTenantAsync(tenantId, mode)`** （R6 关键）：
    1. 防护：`tenantId == TenantContext.DefaultTenant` → `E-SEC-036`；mode∉{anonymize,purge} → `BizException("E-SEC-038")`（同 confirm 参数缺失合并到控制器层处理）。
    2. **`mode==anonymize`**：对每个 `ownerType` 中含 `[PiiField]` 属性的实体，`_db.Set(t).IgnoreQueryFilters().Where(EF.Property<Guid>(e,"TenantId")==tenantId).ToListAsync()` → 逐行 `SensitiveFieldPolicy` 擦 PII；Tenant 行 `Enable=false`；`SaveChangesAsync`。
    3. **`mode==purge`**：
       - `if (_db.Database.IsRelational())` → `using var tx = await _db.Database.BeginTransactionAsync();`
       - 处理自引用环：`TenantPurgeTopology.BuildDeleteOrder` 返 `(order, cycleNodes)`；对 `cycleNodes` 用 raw SQL 或 EF `ExecuteUpdateAsync(e => e.SetProperty("ParentId", (Guid?)null))`（按 TenantId 过滤）。
       - 按 `order` 反向（leaf-first）：`_db.Set(t).IgnoreQueryFilters().Where(...).ExecuteDeleteAsync();`（单 SQL 批量，无 ChangeTracker 装入压力）。
       - 最后 `_db.Sys_Tenants.Where(t => t.Id==tenantId).ExecuteDeleteAsync();`
       - `await tx.CommitAsync();`
       - `else /* InMemory */` → 抛 `NotSupportedException("purge requires relational DB; use anonymize for tests")`（InMemory 限制）。
    4. 审计 `GdprTenantErased`（TenantScope 落平台租户，mode + 目标租户码记 reason）。
- [ ] **Step 5: `GdprController`** 类级 `[Authorize][RequirePlatformAdmin]`：
  - `GET /api/platform/gdpr/export/tenant/{tenantId}`：→ `_svc.ExportTenantAsync` → `File(stream, "application/json", $"tenant-{code}-{yyyyMMdd}.json")`。
  - `GET /api/platform/gdpr/export/subject/{userId}` 同。
  - `DELETE /api/platform/gdpr/erase/subject/{userId}?confirm=true`：confirm!=true → `E-SEC-038`。
  - `DELETE /api/platform/gdpr/erase/tenant/{tenantId}?mode=anonymize|purge&confirm=true`：同；mode 默认 `anonymize`。
- [ ] **Step 6: 测试 `GdprServiceTests`**：
  - 数据主体擦除 → 验 `[PiiField]` 列被擦、`UserName==anon-…`、`Enable==false`、`Password` 非原值、**行仍在 Id 不变**（验 FK 不破）；`IRefreshTokenService.RevokeAllForUserAsync(userId)` 被调（spy）。
  - 擦平台超管 → `E-SEC-036`；擦最后一个平台超管 → `E-SEC-037`；无 confirm → `E-SEC-038`。
  - 整租户 `anonymize`：建 3 实体含 `[PiiField]` + 2 不含 → 验前者被擦后者不动、`Sys_Tenant.Enable==false`。
  - 整租户 `purge` InMemory → 抛 `NotSupportedException`（InMemory 局限测）。
  - **R6 反射集合判式**：纯函数测 `TenantPurgeTopology.GetOwnerEntityTypes()` 含 `Sys_OperLog`（验未漏）+ 不含 `Sys_Tenant`；Kahn 拓扑构造若干假 FK 图验出栈顺序（leaf-first）。
  - 导出 JSON 含 `data.Sys_User` 行但**不含 `Password` 字段**（验剔密钥）。

**Acceptance:** 测试全绿。本地 commit `feat(sec5): T7 块③ GDPR 双粒度 + R6 反射统一判式 + Kahn 拓扑`。

---

## Task T8：R10 独立 `CrossTenantAuditController` + 种子 + DI 注册

**Files:** Create `CP6.WebApi/Controllers/Platform/CrossTenantAuditController.cs`、`CP6.Test/Platform/CrossTenantAuditControllerTests.cs`；Modify `CP6.WebApi/Program.cs`（DI + seed）。

- [ ] **Step 1: `CrossTenantAuditController`** 类级 `[ApiController][Route("api/platform/audit")][Authorize][RequirePlatformAdmin]`；构造注入 `CP6Context _db`。
  - `GET /api/platform/audit?tenantCode=&eventType=&from=&to=&page=&pageSize=`：
    - 仿 `SecurityLogController`：`page=Math.Max(1,page); pageSize=Math.Clamp(pageSize,1,200);`
    - 查 `_db.Sys_SecurityLogs.IgnoreQueryFilters()`（带外，绕行级过滤）按 `tenantCode == RequestTenantCode`（注：spec §5.4 `requestTenantCode` 填**目标租户码**）、`type` 等于 `SecurityEventType` 枚举、`CreatedAt` 介于 [from,to]；`OrderByDescending(CreatedAt)`；返 `{ rows, total }`。
- [ ] **Step 2: 种子**（`Program.cs` admin seed 之后追加，幂等）：
  - **段 1: `Sys_Tenant` 默认租户行**：
    ```csharp
    if (!db.Sys_Tenants.Any(t => t.Id == TenantContext.DefaultTenant)) {
        db.Sys_Tenants.Add(new Sys_Tenant {
            Id = TenantContext.DefaultTenant,
            TenantCode = "DEFAULT", TenantName = "默认租户",
            Enable = true });
        db.SaveChanges();
    }
    ```
  - **段 2: 引导首个平台超管**：
    ```csharp
    var seedAdmin = db.Sys_Users.IgnoreQueryFilters()
                       .FirstOrDefault(u => u.UserName=="admin" && u.TenantId==TenantContext.DefaultTenant);
    if (seedAdmin != null && !seedAdmin.IsPlatformAdmin) {
        seedAdmin.IsPlatformAdmin = true;
        db.SaveChanges();
    }
    ```
  - **不 seed 任何 `Sys_Menu`/`Sys_RoleMenu`**（决策 D1 带外；MenuId 117 留后续）。
- [ ] **Step 3: DI 注册** `Program.cs`：`builder.Services.AddScoped<ITenantAdminService, TenantAdminService>(); AddScoped<IPlatformAdminService, PlatformAdminService>(); AddScoped<IImpersonationService, ImpersonationService>(); AddScoped<IGdprService, GdprService>();`
- [ ] **Step 4: 测试**：直 `new CrossTenantAuditController(db)` → 注入跨租户测试数据 → 按 `tenantCode/eventType/range` 过滤验返行；分页 clamp。**整启动测**（如有）：起 Program → 默认租户 + admin `IsPlatformAdmin==true`（验种子幂等：跑两次结果不变）。

**Acceptance:** 测试全绿；启动主机检查 admin user `IsPlatformAdmin==true`。本地 commit `feat(sec5): T8 R10 CrossTenantAuditController + 种子 + DI 注册`。

---

## Task T9：i18n 五语 + 登录响应 + 前端平台区（R8 sessionStorage 完整态机）

**Files:** Create `CP6.WebApi/Seed/I18nTenantComplianceSeed.cs`；Modify `CP6.WebApi/Program.cs`（`.Concat`）、`CP6.WebApi/Controllers/Sys/AuthController.cs`（L195-202 加 `isPlatformAdmin`）、`cp6.web/src/views/LoginView.vue`（L234-248 localStorage 扩字段）、`cp6.web/src/router/index.ts`（5 路由）；Create `cp6.web/src/stores/platform.ts`、`cp6.web/src/views/platform/TenantListView.vue` + `PlatformAdminView.vue` + `ImpersonationView.vue` + `CrossTenantAuditView.vue` + `GdprView.vue` + `cp6.web/src/api/platform/{tenant,admin,impersonation,audit,gdpr}.ts` + `cp6.web/src/types/platform/*.ts` + `cp6.web/src/components/ImpersonationBanner.vue`。

- [ ] **Step 1: i18n seed** `I18nTenantComplianceSeed`：
  - 错误码 `E-SEC-031~038` 五语（仿 `I18nSecScreenSeed`，`new Sys_Lang { LangKey="E-SEC-031", ZhCN="无平台超管权限", ZhTW="無平台超管權限", En="Platform admin required", Ja="プラットフォーム管理者権限が必要", Ko="플랫폼 관리자 권한 필요" }` 等等）。
  - 事件名 `sec.event.19~30` 五语。
  - 画面词条 `platform.tenant.{title,create,suspend,reactivate,tempPassword,…}`、`platform.admin.{grant,revoke,lastOne,…}`、`platform.impersonation.{start,end,bannerActive,countdown,reason,…}`、`platform.gdpr.{exportTenant,exportSubject,eraseSubject,eraseTenant,modeAnonymize,modePurge,confirmDelete,…}`、`platform.audit.{title,tenantCode,eventType,from,to,…}` 五语。
  - `Program.cs` `.Concat(...I18nTenantComplianceSeed.Items)`。
- [ ] **Step 2: `AuthController.Login` L195-202 响应加字段**：
  ```csharp
  return Ok(new {
      userName = user.UserName, nickName = user.NickName, roleId = user.RoleId,
      menus, mustChangePassword = mustChange,
      isPlatformAdmin = user.IsPlatformAdmin   // T9 新增
  });
  ```
- [ ] **Step 3: `LoginView.vue` L234-248 扩字段**：`localStorage.setItem('cp6_isPlatformAdmin', res.isPlatformAdmin ? '1' : '');`
- [ ] **Step 4: Pinia store `stores/platform.ts`**：
  ```ts
  export const usePlatformStore = defineStore('platform', () => {
    const isPlatformAdmin = ref(localStorage.getItem('cp6_isPlatformAdmin') === '1');
    const impersonating = ref<null | { tenantName:string; userName:string; expiresAt:number }>(
      JSON.parse(sessionStorage.getItem('cp6_impersonating') || 'null')
    );
    function setImpersonation(data) { impersonating.value = data; sessionStorage.setItem('cp6_impersonating', JSON.stringify(data)); }
    function clearImpersonation() { impersonating.value = null; sessionStorage.removeItem('cp6_impersonating'); }
    return { isPlatformAdmin, impersonating, setImpersonation, clearImpersonation };
  });
  ```
- [ ] **Step 5: 路由注册** `router/index.ts` `viewModules` 加 5 行：`'/platform/tenant': … TenantListView` 等；router guard 在 `beforeEach` 中：访问 `/platform/*` 且 `isPlatformAdmin==false` → `next('/')`；imp 期间隐藏"平台管理"入口（读 `impersonating != null`）。**guard 是 UX 层，真闸门在后端 `[RequirePlatformAdmin]`**。
- [ ] **Step 6: `ImpersonationBanner.vue`** 全局组件 mount 到 App.vue 顶部：
  - 当 `usePlatformStore().impersonating != null` 时渲染醒目条："正在以 {tenantName}/{userName} 身份操作 · 还剩 {min} 分钟 · [切出]"。
  - **倒计时**：`setInterval` 每秒计算 `(expiresAt - Date.now())/60000`；归零时**自动隐藏 + el-message 提示"已自动切回平台超管会话"**（refresh 会自然续到平台 access，前端无需特殊处理，§10 隐式切出局限）。
- [ ] **Step 7: 5 个 platform 视图**（仿 `SecurityLogView.vue` 列表范本 + el-form 表单）：
  - `TenantListView.vue`：表格 + 建租户向导对话框（建后弹"临时密码 = {tempPwd} 仅显示一次"复制按钮）+ 停用/重启用按钮 + 续期 input。
  - `PlatformAdminView.vue`：表格 + 撤销按钮（点击前 confirm，撤最后一个时后端拒 E-SEC-037 → 前端 el-message error）。
  - `ImpersonationView.vue`：租户下拉 + 用户下拉（可空=该租户首 admin）+ reason input + [切入]；切入响应后**调 store.setImpersonation + 替换 localStorage `menus` + `router.push('/dashboard')` 刷新路由表 + emit 全局事件触发横幅渲染**。[切出] 调 end → store.clearImpersonation + 替换 menus 回原。
  - `CrossTenantAuditView.vue`：tenantCode/eventType/from/to 筛选 + 表格分页（仿 `OperLogView.vue`）。
  - `GdprView.vue`：四 tab（按租户导出/按主体导出/按主体擦除/按租户擦除）；每个操作有**二次确认弹窗**（el-message-box prompt 要求输入"CONFIRM"才可继续 → 前端禁用→拦截误操作）。
- [ ] **Step 8: `api/platform/*.ts`** 5 个文件，axios 调用 + TS 类型；imp start/end 的响应类型含 `menus` 字段。
- [ ] **Step 9: 前端 vitest + i18n:check + type-check** 全绿。

**Acceptance:** 前端 `npm run build && npm run type-check && npm run test:unit && npm run i18n:check` 全绿；后端 `dotnet test` 全绿。本地 commit `feat(sec5): T9 i18n 五语 + 登录响应 isPlatformAdmin + 前端平台区 R8 完整态机`。

---

## Task T10：gstack 真浏览器 QA 全流程（含 R9 三条对抗）

**Files:** Run `gstack`。不写代码。

- [ ] **Step 1: 起后端 + 前端**（dotnet run + vite dev）。
- [ ] **Step 2: 黄金路径**：
  1. admin 登录（默认租户，已是平台超管） → 见侧栏"平台管理"入口。
  2. 建租户 "tenant-qa" + adminUser "qa-admin" → 弹临时密码（复制）→ 退出 → 用 qa-admin + 临时密码登录 → 被强制改密页拦。
  3. 改密成功 → qa-admin 登录正常 → **不见**"平台管理"入口。
  4. 退出 → 回 admin 登录 → 切入 "tenant-qa" + "qa-admin" → **横幅可见** + **侧栏菜单切到 qa-admin 菜单**（"平台管理"消失） + 任改一条 qa-admin 可见的业务数据。
  5. 切出 → 横幅消失 + 菜单回平台超管态。
  6. 查 `/platform/audit` → 见 `ImpersonationStarted`/`ImpersonationEnded` 两行 + 中间业务写操作的 OperLog 带 `ImpersonatorId`。
  7. GDPR 导出 `tenant-qa` → 下载 JSON，目检不含 `Password`/`TokenHash`。
  8. GDPR 数据主体匿名化 qa-admin → 验原账号无法登录（"用户已停用"）+ 其历史单据仍在（Creator 字段被 best-effort 改 anon-…）。
- [ ] **Step 3: R9 三条对抗**：
  - **(i) imp 期间访 `/api/platform/*`**：切入后浏览器开发者面板 `fetch('/api/platform/tenant')` → 验 403 + `E-SEC-034`。
  - **(ii) 切出后旧 imp Cookie 重放**：切入 → 开发者面板复制 `cp6_at` cookie 值 → 切出 → 手动用旧值 set-cookie → 任意业务 GET → 验 401（jti 已黑名单）。
  - **(iii) 双标签 sessionStorage 隔离**：标签 A 切入 → 标签 B 新开 → 标签 B 见 admin 正常会话**不受影响**（无 imp 横幅、平台管理入口可见）。
- [ ] **Step 4: 多租户隔离目检**：qa-admin 登录后无法看到 admin 的默认租户数据；交叉走访所有 ERP 主菜单确认无跨租户泄漏。
- [ ] **Step 5: 防自锁死**：再建另一平台超管 X → 撤销 X 成功 → 尝试撤销 admin（最后一个）→ 验弹 E-SEC-037 拒绝。

**Acceptance:** 全部步骤通过；截图/日志记入会话；测试基线维持。本地不 commit（QA 不产代码）。

---

## 跨 Task 验收单

- [ ] **构建/测试** `dotnet build` + `dotnet test` 全绿；测试增量 ≥ 50（T1 + T2 集成 4 + T3 服务 6 + T4 服务 4 + T5 服务 8 + T6 filter 2 + T7 服务 8 + T8 controller 2 + T9 前端 vitest 增量）。
- [ ] **迁移** `TenantCompliance` 含 `Sys_User.IsPlatformAdmin` + `Sys_OperLog.ImpersonatorId` 两 DDL；下游 EF 迁移链未断。
- [ ] **R1~R10 验收**（逐条对位）：
  - R1: 迁移 DDL 含 `Sys_OperLog.ImpersonatorId` ✓
  - R2: T5 服务测验 `BlacklistAsync` 在 start 与 end 各调用一次 ✓
  - R3: T5 服务测验 imp 令牌 `must_change_password==false` claim ✓；中间件 AllowPaths 含 `/impersonation/end` ✓
  - R4: T3 建租户字段集已覆盖（已核验，无需测） ✓
  - R5: T5 服务测验 `LogAsync` 调用瞬间 `_tenant.CurrentTenantId == DefaultTenant`（start/end 两路径） ✓
  - R6: T7 纯函数测 `GetOwnerEntityTypes` 含 `Sys_OperLog`、`BuildDeleteOrder` Kahn 顺序 ✓；purge InMemory 抛 NotSupported ✓
  - R7: T6 filter 测验 Kafka payload + DB 降级两路径含 `ImpersonatorId` ✓
  - R8: T9 前端 vitest 验 store + sessionStorage 持久化 + start/end menus 替换；gstack 验双标签隔离 ✓
  - R9: T2 集成测三道闸 ✓；T3 服务测原子回滚 ✓；T5 服务测 end 真身已撤销拒 ✓；T10 gstack 三对抗 ✓
  - R10: T8 独立 `CrossTenantAuditController` 存在 + 测试 ✓
- [ ] **本地 commit 历史**（T1~T9，9 提交，T10 不提交）+ **未 push**（由用户监督）。
- [ ] **memory** `project_current_focus.md` 续做"S 类 #5 全栈完成"段，迁移 origin push 待用户。

---

*生成于 2026-06-24（实现计划，据定稿 spec `500a8e2` + R1~R10 全嵌入）。锚点本会话主代理实读核验真代码（JwtHelper.GenerateToken 9 参签名 / IRefreshTokenService.RevokeAllForUserAsync 已有 / ITokenBlacklistService.BlacklistAsync / AuthCookieWriter.WriteAuthCookies 三 cookie / OperLogFilter L100-114 构造 / AuthController.Login L195-202 响应 + Logout L280-315 jti 算法 / CP6Context L1870-1957 反射 + StampTenant / LoginView.vue L234-248 localStorage / Sys_OperLog int Id 手加 TenantId 范式 / Sys_User 字段集）。下一步 = subagent-driven 执行 T1 起（TDD + 先绿后本地 commit 不 push + gstack QA）。*
