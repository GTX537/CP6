# 租户合规设计 spec — S 类安全合规 #5（初稿）

> 源：brainstorming 共识（2026-06-23 范围三块全包；2026-06-24 续做完成"角色/权限模型与提权面"第二轮探勘 + 5 项地基决策定稿，见 §11）。底座 = #1 认证加固（已落码：JWT/Cookie/刷新令牌/安全审计/多租户基建）。#2 2FA / #3 SSO / #4 字段审计当前**仅有 spec、尚未落码**——本子项目**自洽，不硬依赖 #2/#3/#4**（见 §0 / §11）。本子项目把 CP6 从"单租户可演示"推进到"多租户 SaaS 可运营"：补齐 ①**平台级租户管理 API**（建/改/停/续期租户 + 建租户同时开通首个 admin）②**跨租户运维 + 完整 impersonation（读写）+ 全程审计** ③**GDPR 双粒度数据导出/被遗忘权擦除（匿名化为主）**。命名空间 **Sys** + 新增 **Platform** 端点族（带外 `[RequirePlatformAdmin]`，不入 RBAC 菜单网格）。

## §0 范围

**做（MVP）：**

- **平台超管模型（地基）= 带外标志位**（决策 D1）：`Sys_User.IsPlatformAdmin` 布尔位；平台端点用新过滤器 `[RequirePlatformAdmin]` 校验，**完全不走 RBAC 角色/权限点**。因此现有赋角色/赋权端点（`UserRoleController` / `RolePermController`）够不到它 → 租户管理员自提权向量天然失效（§3）。
- **块① 租户管理 API**：平台超管对 `Sys_Tenant` 建/改/停用(Enable=false)/重启用/续期(ExpireDate)/列表查询；**建租户时同时建该租户首个 admin 用户**（RoleId=1 + `MustChangePassword` + 返回一次性临时密码；该 admin `TenantId`=新租户、`IsPlatformAdmin`=false）（决策 D5）。
- **块② 跨租户运维 + 完整 impersonation（读写）**（决策 D2）：平台超管"切入"任一租户，以该租户某用户身份（默认该租户首个 admin）读写代操作；切入/切出 + 窗口内每次写操作全程审计（impersonator 真身可追溯）。
- **块② 跨租户运维审计**：所有平台级操作（租户 CRUD、平台超管授/撤、impersonation 起止、GDPR 导出/擦除）写 `Sys_SecurityLog`（复用 #1 `ISecurityAuditService`，新增 `SecurityEventType` 19~30）；平台操作审计行**落平台租户**（默认租户 `…A1`），不随目标租户被擦除而丢失（§5.4）。
- **块③ GDPR 导出（双粒度）**（决策 D3）：按**租户**（整租户全表导出，offboarding）+ 按**数据主体**（单个 `Sys_User` 的画像 + 可归因活动/记录）导出为 JSON 包；导出**剔除密钥列**（复用敏感字段拒名单，§6.4）。
- **块③ GDPR 擦除（被遗忘权，匿名化为主）**（决策 D4）：
  - **数据主体擦除** = `[PiiField]` 标记的 PII 列**匿名化**（擦为占位）+ `Enable=false` + 吊销会话（refresh 令牌 + jti 黑名单），**保留行 + Id → FK/审计完整性不破**。
  - **租户擦除** = 默认 `mode=anonymize`（按 `[PiiField]` 批量匿名化该租户全部标记实体）；`mode=purge` 显式开关才物理硬删（offboarding 彻底退租）。
- 多租户隔离、平台端点带外鉴权、五语 i18n、前端平台区（带外导航）。

**不做（YAGNI）：**

- **完整自助租户 Onboarding / 订阅计费**（注册流/套餐/支付）——属 S2/S1，本期只做平台超管手工建租户。
- **物理隔离库 / 每租户独立 DB / schema**——保持共享库 + 行级过滤（已知多租户阶段限制，留后续）。
- **细粒度平台角色分级**（合规专员 vs 平台 Owner，决策 D1 的方案 C）——本期单一 `IsPlatformAdmin` 硬位；分级留 hardening。
- **impersonation 期间做平台操作**——切入后平台权限挂起（`[RequirePlatformAdmin]` 见 `imp` claim 即拒），须先切出。
- **GDPR 跨关系深挖 PII**（如某员工经手的客户主数据联级匿名化）——数据主体擦除聚焦 `Sys_User` 自身 PII + 显式可归因列（Creator/Modifier/UserId），深关系 PII 留 hardening（§10 记录）。
- **导出包加密 / 异步大导出队列 / 分卷**——MVP 同步 JSON（平台操作低频）。
- **防篡改审计**（哈希链 / WORM）——与 #4 同，留 hardening。
- **软删除全仓化**——擦除走匿名化（保行）或显式 purge（物理删），不引入全局软删列。

## §1 现状锚点（本会话 2026-06-24 实读核验；行号可能微移）

- **角色/权限是"全局共享 + 纯角色制、零租户感知"**（决策 D1 的根因）：
  - `Sys_Role`(int RoleId 自定义键)/`Sys_Menu`(int MenuId)/`Sys_RoleMenu`/`Sys_RoleAction`/`Sys_RoleDataScope`/`Sys_RoleFieldPerm` **全是全局表**（非 `BaseTenantEntity`）。一份角色定义服务所有租户。
  - `Sys_User : BaseTenantEntity`（`CP6.Entity/DomainModels/Sys/Sys_User.cs`）+ `Sys_UserRole : BaseTenantEntity`（`UserId Guid → RoleId int`）= 租户作用域。
  - `PermissionAggregator.BuildAsync(userId)`（`CP6.Core/Services/Sys/PermissionAggregator.cs` L16–62）只按 `roleIds`（主角色 ∪ 附加角色）join 全局表生成 `MenuKeys/ActionKeys`，**判定不含任何租户过滤** → 持有某 RoleId 的用户无论哪租户权限集一致。**故绝不能把平台权限点 seed 到任何角色上**（任一租户拿到该角色即获跨租户权）。
- **两条已确认的自提权向量（无角色范围守卫）**：
  - `UserRoleController.Save`（`CP6.WebApi/Controllers/Sys/UserRoleController.cs` L27–33，`[RequirePermission("user","edit")]`）→ 可给任意用户赋**任意 RoleId**，无"仅本租户/低于自身"限制。
  - `RolePermController.SaveRolePerm` / `SaveMenuActions`（`CP6.WebApi/Controllers/Sys/RolePermController.cs` L33–39/L51–57，`[RequirePermission("pub-role-perm","edit")]`）→ 可给**任意角色**塞**任意菜单/操作点**。
  - ⟹ 平台权限必须是这两端点够不到的**带外闸门**（§3）。
- **鉴权过滤器范本** `RequirePermissionAttribute`（`CP6.Core/Auth/RequirePermissionAttribute.cs`）：`IAsyncAuthorizationFilter`，`OnAuthorizationAsync` 用 `RequestServices.GetService<IPermissionService>()` 服务定位（特性不能构造注入）→ 不命中置 403 `ObjectResult`。**`[RequirePlatformAdmin]` 仿此结构**（§3.2）。
- **JWT 签发** `JwtHelper.GenerateToken(...)`（`CP6.Core/Utilities/JwtHelper.cs`）现有 claims：`NameIdentifier`(userId)、`Name`(userName)、`tenant_id`、`jti`、`must_change_password`。**本子项目扩两个可选 claim**：`is_platform_admin`、`impersonator_id`（impersonation 用，§5.2）。
- **令牌签发唯一入口** `AuthController.BuildAccessToken(user, jti, mustChange)`（`CP6.WebApi/Controllers/Sys/AuthController.cs` L54–67，登录/刷新复用，`tenantId: user.TenantId`）→ **加 `isPlatformAdmin: user.IsPlatformAdmin`**；登录响应（L195–202）**加 `isPlatformAdmin` 字段**（前端据此渲染平台区）。
- **登录流程**（`AuthController.Login` L76–203）：跨租户按名消歧（`IgnoreQueryFilters`）→ `_tenant.CurrentTenantId = user.TenantId`（L157）→ `BuildAccessToken` → `PrewarmAsync` → 菜单按角色并集 → 三 Cookie（access httpOnly + refresh + CSRF 双提交）写出（L188–192）。**impersonation 切入复用三 Cookie 写出机制**（§5.2）。
- **租户上下文** `ITenantContext.CurrentTenantId { get; set; }`（`CP6.Core/Services/Common/ITenantContext.cs`，请求级 scoped、**可读可写**）；`TenantContext.DefaultTenant = 00000000-0000-0000-0000-0000000000A1`。`TenantMiddleware`（`CP6.WebApi/Middleware/TenantMiddleware.cs`）从 `tenant_id` claim 写入。**impersonation 令牌的 `tenant_id`=目标租户 → 全部现有控制器/查询/盖章天然作用域到目标租户**（零散改）。
- **跨租户遍历器**（GDPR 复用）：`ITenantEnumerator.ListActiveAsync()`（`CP6.Core/Services/Common/ITenantEnumerator.cs` L11–34，列 `Sys_Tenant.Enable=true`，空表回退默认租户）；`TenantScopeRunner.ForEachTenantAsync(scopeFactory, body, logger, ct)`（`CP6.WebApi/BackgroundServices/TenantScopeRunner.cs`，逐租户开 scope 设 `CurrentTenantId`）。
- **反射遍历所有 `BaseTenantEntity`**（GDPR 整租户导出/擦除复用）：`CP6Context.OnModelCreating`（`CP6.Core/EFDbContext/CP6Context.cs` L1877–1885 全局过滤 + L1894–1931 唯一索引升级）用 `modelBuilder.Model.GetEntityTypes().Where(t => typeof(BaseTenantEntity).IsAssignableFrom(t.ClrType) && t.BaseType is null)`。**运行时等价**：`_db.Model.GetEntityTypes()` + `_db.Set(clrType)` 动态查每实体；跨租户精确命中用 `.IgnoreQueryFilters().Where(TenantId==targetId)`（已有用法：`RefreshTokenService`/`AuthController` 登录/`PasswordPolicy`）。
- **写入盖章** `StampTenant`（L1936–1945）+ `SaveChanges/SaveChangesAsync` 重写（L1947/L1953）：新 `BaseTenantEntity` 未设租户→盖 `CurrentTenantId`。新实体 `Sys_Tenant`（共享表）/平台审计逻辑不破坏此机制。
- **安全审计**（块②复用）：`ISecurityAuditService.LogAsync(SecurityEventType type, Guid? userId, string? userName, string? requestTenantCode, string? ip, string? ua, string? reason = null)`（`CP6.Core/Services/Sys/ISecurityAuditService.cs`，**失败不阻断**+自动截断）；`Sys_SecurityLog : BaseTenantEntity`。`SecurityEventType`（`CP6.Entity/DomainModels/Sys/SecurityEventType.cs`）现占 **1~8**（9~14 留 #2、15~18 留 #3）→ **#5 取 19~30**（§2.4）。
- **种子** `Program.cs`：全局 `RoleId=1 管理员`「拥有全部权限」（L576 + 全菜单/操作）；admin 用户 RoleId=1（L610–618，**未设 TenantId** → `StampTenant` 落默认租户 `…A1`）。**#5 seed 块**：把默认租户 admin 置 `IsPlatformAdmin=true`（幂等，引导首个平台超管，§7）；并 seed `Sys_Tenant` 默认租户行（若不存在）令 `ITenantEnumerator` 可列出。
- **错误码** 现有 `E-SEC-001~010`（#1 已落，`I18nSecScreenSeed.cs`）；#2/#3 规划 011~02x（spec 未落）→ **#5 取 E-SEC-031+**（§2.5）。
- **i18n / 分页 / 控制器 / 前端范本**（沿用 #4 已核验）：`I18nSecScreenSeed`（`public static readonly Sys_Lang[] Items`，Program.cs `.Concat` 接入）；`SecurityLogController`（`page=Math.Max(1,page)`、`pageSize=Math.Clamp(pageSize,1,200)`、`OrderByDescending`、返 `{total,rows}`、`SecurityLogControllerTests` 直 `new` 控制器绕过过滤器）；前端 `views/pms/SecurityLogView.vue`、`router/index.ts` `viewModules`、`api/sys/`。

## §2 数据模型

### §2.1 `Sys_User` 加平台超管位（`CP6.Entity/DomainModels/Sys/Sys_User.cs`）

```csharp
// ───── S 类租户合规 #5：平台超管标志（带外鉴权，绝不入 RBAC）─────
/// <summary>平台超管：可调用 Platform 端点族（租户管理/跨租户/GDPR）。带外位，
/// 不经任何角色/权限点授予；仅由另一平台超管经 PlatformAdminController 翻转。</summary>
public bool IsPlatformAdmin { get; set; }
```
（无其它新列；现有 `BaseTenantEntity` 审计字段复用。）

### §2.2 GDPR PII 匿名化标记 `[PiiField]`（`CP6.Entity`，仿 #4 `[AuditIgnore]`）

```csharp
namespace CP6.Entity;
/// <summary>标记该列为个人可识别信息(PII)。GDPR 被遗忘权擦除时，匿名化器把它擦为占位值
/// （string→"REDACTED-{n}"/ null；按 ReplaceWith 策略）。opt-in：未标记的列不被擦除。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PiiFieldAttribute : Attribute
{
    /// <summary>擦除策略：Placeholder=占位串（默认）；Null=置 null（仅可空列）。</summary>
    public PiiErase Mode { get; init; } = PiiErase.Placeholder;
}
public enum PiiErase { Placeholder, Null }
```
- **首批 `[PiiField]` 标注（数据主体 = `Sys_User`）**：`NickName`(Null)、`Email`(Null)、`LastLoginIp`(Null)。`UserName` 特殊处理（匿名化为 `anon-{Id 前 8}`，保唯一 + 可登录约束失效）见 §6.3。`Password` 不标 PII（已是密钥，擦除时一并随机化，§6.3）。
- **延后**：`BusinessPartner` 联系人 PII、其它含个人数据实体——加 `[PiiField]` 即纳入，无需改擦除器（§10 记录 MVP 聚焦 `Sys_User`）。

### §2.3 敏感字段拒名单（GDPR 导出剔除 + 与 #4 同源）

复用"以 `Secret` 结尾 / 以 `Hash` 结尾 / `Password` / `TokenHash` / `Salt` / `ClientSecretProtected` / `TwoFactorSecret`"大小写不敏感拒名单（§6.4 导出时跳过这些列）。若 #4 已落 `SensitiveFieldPolicy`，复用之；否则 #5 内建一份（自洽，不依赖 #4）。

### §2.4 `SecurityEventType` 扩 19~30（`CP6.Entity/DomainModels/Sys/SecurityEventType.cs`）

```csharp
// ───── S 类租户合规 #5（19~30；1~8=#1，9~14 留 #2，15~18 留 #3）─────
TenantCreated = 19,
TenantUpdated = 20,
TenantSuspended = 21,        // Enable=false
TenantReactivated = 22,      // Enable=true
PlatformAdminGranted = 23,
PlatformAdminRevoked = 24,
ImpersonationStarted = 25,
ImpersonationEnded = 26,
GdprTenantExported = 27,
GdprSubjectExported = 28,
GdprTenantErased = 29,
GdprSubjectErased = 30,
```

### §2.5 错误码 E-SEC-031+（五语 seed，§8）

| 码 | 语义 |
|---|---|
| E-SEC-031 | 无平台超管权限（`[RequirePlatformAdmin]` 拒） |
| E-SEC-032 | 目标租户不存在 |
| E-SEC-033 | 租户编码已存在（建租户唯一冲突） |
| E-SEC-034 | impersonation 期间禁止平台操作（先切出） |
| E-SEC-035 | impersonation 目标用户不存在/已停用 |
| E-SEC-036 | 不能对平台租户/平台超管自身执行 GDPR 擦除 |
| E-SEC-037 | 不能撤销/擦除最后一个平台超管（防自锁死，§3.3） |
| E-SEC-038 | 擦除/purge 需显式确认参数 |

### §2.6 EF 迁移
`dotnet ef migrations add TenantCompliance --project CP6.Core --startup-project CP6.WebApi`：仅 `Sys_User` 加 `IsPlatformAdmin`(bit, 默认 0) 一列。`[PiiField]`/拒名单/枚举值不映射列；`Sys_Tenant` 表已存在（无变更）。

## §3 平台超管模型（地基，决策 D1）

### §3.1 带外标志位为什么是唯一安全解
RBAC 是"全局角色 + 纯角色判定 + 赋权端点无范围守卫"（§1）。任何"平台权限点 seed 到角色"的做法都会被向量 A/B 攻破（租户管理员自赋角色/自塞权限点）。`IsPlatformAdmin` 是 `Sys_User` 上的列，**不是角色、不是菜单、不是操作点** → `UserRoleController`（改 RoleId）/`RolePermController`（改菜单/操作）**结构上够不到** → 向量 A/B 对平台权天然失效。翻转该位的唯一入口是平台专用端点（§4.4），其自身又被 `[RequirePlatformAdmin]` 守 → 只有平台超管能造平台超管。

### §3.2 `[RequirePlatformAdmin]`（`CP6.Core/Auth/RequirePlatformAdminAttribute.cs`，仿 `RequirePermissionAttribute`）

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequirePlatformAdminAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        var user = ctx.HttpContext.User;
        // (1) impersonation 期间平台权挂起：imp claim 在 → 拒（E-SEC-034）
        if (user.FindFirst("impersonator_id") != null) { ctx.Result = Forbid(34); return; }
        // (2) claim 快速判定
        if (user.FindFirst("is_platform_admin")?.Value != "true") { ctx.Result = Forbid(31); return; }
        // (3) 纵深防御：按 NameIdentifier 回查 DB，防令牌签发后被撤销仍可用（平台端点低频，DB 读可接受）
        var db = ctx.HttpContext.RequestServices.GetService<CP6Context>();
        var idStr = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (db == null || !Guid.TryParse(idStr, out var uid)
            || !(await db.Sys_Users.IgnoreQueryFilters().AnyAsync(u => u.Id == uid && u.IsPlatformAdmin && u.Enable)))
        { ctx.Result = Forbid(31); return; }
    }
}
```
- `Forbid(code)` = `new ObjectResult(new { code=403, message=$"E-SEC-0{code}" }){ StatusCode=403 }`（前端按码本地化）。
- **回查用 `IgnoreQueryFilters`**：平台超管在默认租户，但当前 `tenant_id` claim 也是默认租户（正常会话），其实无需忽略；但用之更稳健（防边界）。token TTL 短（access 分钟级）→ 撤销最迟下次刷新失效，回查再补硬。

### §3.3 防自锁死铁律
- **撤销平台超管** / **擦除某用户** 前，若该用户是平台超管且为**最后一个启用的平台超管**（`COUNT(IsPlatformAdmin && Enable)==1`）→ 拒（E-SEC-037）。保证系统永远至少有一个可用平台超管（同 #1/② 授权加固的"不锁死"铁律）。
- GDPR 擦除目标若是平台租户（`…A1`）自身或任一平台超管 → 拒（E-SEC-036）。

## §4 块① 租户管理 API（`CP6.WebApi/Controllers/Platform/TenantController.cs`，路由 `api/platform/tenant`）

类级 `[Authorize]` + `[RequirePlatformAdmin]`。多租户全局过滤对 `Sys_Tenant`（共享表）不生效 → 直接 CRUD。

- `GET /api/platform/tenant?keyword=&enable=&page=&pageSize=` → 分页列表（仿 `SecurityLogController` 的 clamp/`{total,rows}`），返 `{id,tenantCode,tenantName,enable,expireDate,remark,userCount}`（`userCount` 由 `Sys_User.IgnoreQueryFilters().Count(TenantId==id)` 投影）。
- `GET /api/platform/tenant/{id}` → 单租户详情。
- `POST /api/platform/tenant` → **建租户 + 首个 admin**（决策 D5）：
  1. 校验 `TenantCode` 全局唯一（不存在则 E-SEC-033）。
  2. 建 `Sys_Tenant{ Id=Guid.NewGuid(), TenantCode, TenantName, Enable=true, ExpireDate, Remark }`。
  3. 建首个 admin：`Sys_User{ UserName, TenantId=新租户.Id, RoleId=1, IsPlatformAdmin=false, Enable=true, MustChangePassword=true, Password=BCrypt(临时随机密码) }`（密码经 `IPasswordHasher` 哈希；**临时明文仅本次响应返回一次**，不落库明文）。
  4. 审计 `TenantCreated`（reason=租户编码 + admin 用户名）。
  5. 返回 `{ tenantId, adminUserName, tempPassword }`（tempPassword 一次性）。
  - **原子**：步骤 2~3 同一 `SaveChanges`（`Sys_Tenant` 与 `Sys_User` 一并提交）；`Sys_User.TenantId` **显式设为新租户**（不靠盖章，因当前 `CurrentTenantId` 是平台租户）。
- `PUT /api/platform/tenant/{id}` → 改 `TenantName/ExpireDate/Remark`（先查后改）；审计 `TenantUpdated`。
- `POST /api/platform/tenant/{id}/suspend` → `Enable=false`；审计 `TenantSuspended`。（停用后该租户用户登录被 `AuthController` 既有"租户已停用"拦截，L90/L152。）
- `POST /api/platform/tenant/{id}/reactivate` → `Enable=true`；审计 `TenantReactivated`。
- 错误码 E-SEC-032（不存在）/033（编码冲突）。

### §4.4 平台超管授/撤（`CP6.WebApi/Controllers/Platform/PlatformAdminController.cs`，路由 `api/platform/admin`）
类级 `[Authorize]` + `[RequirePlatformAdmin]`。
- `GET /api/platform/admin` → 列当前全部平台超管（`Sys_User.IgnoreQueryFilters().Where(IsPlatformAdmin)` 投影 `{id,userName,tenantId,enable}`）。
- `POST /api/platform/admin/{userId}/grant` → 置 `IsPlatformAdmin=true`（按 `IgnoreQueryFilters` 取任意租户用户）；审计 `PlatformAdminGranted`；失效该用户权限缓存 + 令其重登（其 token 下次刷新带上新 claim）。
- `POST /api/platform/admin/{userId}/revoke` → 置 false；**防自锁死**（§3.3，最后一个 → E-SEC-037）；审计 `PlatformAdminRevoked`。

## §5 块② 跨租户运维 + 完整 impersonation（决策 D2）

### §5.1 身份模型（落地决策，按推荐自决，§11 可推翻）
完整 impersonation（读写）= 平台超管**以目标租户某用户身份**进入。令牌身份（`NameIdentifier`/`Name`）= **目标用户**（默认该租户首个 admin，可指定 userId）→ 现有 `ICurrentPermissionContext`/`PermissionAggregator` 按目标用户正确解析权限、写入按目标用户盖 `Creator/Modifier`、`tenant_id`=目标租户 → **全部现有控制器零改即作用于目标租户**。真身（platform admin userId）写入 `impersonator_id` claim → 可追溯（§5.3）。

### §5.2 切入 / 切出（`CP6.WebApi/Controllers/Platform/ImpersonationController.cs`，`api/platform/impersonation`）
类级 `[Authorize]` + `[RequirePlatformAdmin]`（注意：切入端点本身要平台权；切入**后**令牌无 `is_platform_admin`、有 `impersonator_id` → 平台端点全被 §3.2 第(1)条拒）。
- `POST /api/platform/impersonation/start { tenantId, userId?, reason }`：
  1. 校验目标租户存在且 Enable（否则 E-SEC-032）。
  2. 目标用户 = `userId` 或该租户首个 admin（`RoleId==1 && Enable`，`IgnoreQueryFilters`）；不存在/停用 → E-SEC-035。
  3. 签发 impersonation access 令牌：`JwtHelper.GenerateToken(userId=目标用户.Id, userName=目标用户.UserName, tenantId=目标租户, isPlatformAdmin:false, impersonatorId: 平台超管.Id, expireMinutes: SecurityOptions.ImpersonationMinutes(默认 30))`。
  4. **只覆盖 access Cookie**（复用 `_cookies.WriteAuthCookies` 或新增"仅 access"写法）；refresh Cookie 不动（刷新或过期 → 自然回到平台超管正常会话，即隐式切出）。
  5. 审计 `ImpersonationStarted`（userId=真身 platform admin、requestTenantCode=目标租户码、reason）。审计行落**平台租户**（§5.4）。
  6. 返回 `{ impersonating: true, tenantName, userName, expiresInMinutes }`。
- `POST /api/platform/impersonation/end`：读 `impersonator_id` claim → 取回平台超管用户 → 重签其正常令牌（`isPlatformAdmin:true`）覆盖 access Cookie；审计 `ImpersonationEnded`。**此端点不加 `[RequirePlatformAdmin]`**（因当前 token 无 is_platform_admin），改为：仅校验 `impersonator_id` claim 存在且该真身仍是启用平台超管，否则 401/403。

### §5.3 窗口内每次写操作的可追溯（块②审计核心）
impersonation 期间写操作以目标用户身份落库，但**审计必须能回指真身**。`Sys_OperLog`（每请求一行，`OperLogFilter` 已记控制器/动作/参数/IP）**加一列 `Guid? ImpersonatorId`**：`OperLogFilter` 写日志时若当前 `User` 有 `impersonator_id` claim → 填入。这样 impersonation 窗口内**每个被 OperLog 覆盖的写请求**都可归因真身（start/end 的 `Sys_SecurityLog` 给出窗口边界，OperLog 给出窗口内逐操作）。
> `OperLogFilter` 跳过 `/api/auth`、`/api/operlog`（既有 L80–83）；impersonation 下的业务写不在跳过名单 → 正常记录 + 带 `ImpersonatorId`。

### §5.4 平台操作审计行的租户归属（铁律）
所有平台级审计（租户 CRUD、平台超管授撤、impersonation 起止、GDPR）经 `ISecurityAuditService.LogAsync` 落 `Sys_SecurityLog`。**这些行的 `TenantId` 须落平台租户（默认 `…A1`），不落目标租户** → 否则 GDPR 擦除/purge 目标租户会连同删掉"对它做过什么"的平台审计，违反留痕。实现：调 `LogAsync` 前后保证 `CurrentTenantId` 为平台租户上下文（平台端点正常会话即平台超管自身租户=默认租户；impersonation start 在切入**前**记，仍是平台上下文）。`requestTenantCode` 字段填**目标租户码**保留可追溯。
> 跨租户运维查询端点 `GET /api/platform/audit?tenantCode=&eventType=&from=&to=&page=&pageSize=`（`ImpersonationController` 或独立 `CrossTenantAuditController`）：平台超管查 `Sys_SecurityLog.IgnoreQueryFilters()` 全租户审计（带外，绕行级过滤），按条件筛选 + 分页。

## §6 块③ GDPR 导出 / 擦除（决策 D3/D4）

控制器 `CP6.WebApi/Controllers/Platform/GdprController.cs`（`api/platform/gdpr`），类级 `[Authorize]` + `[RequirePlatformAdmin]`。

### §6.1 整租户导出
`GET /api/platform/gdpr/export/tenant/{tenantId}` → 校验租户存在（E-SEC-032）→ 反射枚举 `_db.Model.GetEntityTypes()` 中所有 `BaseTenantEntity` 子类 → 逐类 `_db.Set(clrType).IgnoreQueryFilters().Where(e => EF.Property<Guid>(e,"TenantId")==tenantId)`（表达式树构造）→ 行经**敏感列剔除**（§6.4）后序列化 → 输出 `{ tenant:{…Sys_Tenant 行}, data:{ "Sys_User":[…], "Wf_…":[…], … } }` JSON（`Content-Disposition: attachment; filename=tenant-{code}-{yyyyMMdd}.json`）。审计 `GdprTenantExported`。

### §6.2 数据主体导出
`GET /api/platform/gdpr/export/subject/{userId}` → 取 `Sys_User`（`IgnoreQueryFilters`）→ 汇总该主体可归因数据：(a) 用户画像（剔密钥）；(b) `Sys_SecurityLog` / `Sys_OperLog` 中 `UserId==userId`（或 OperLog 的创建者）；(c) 其 `Creator`/`Modifier` == 该用户 `UserName` 的记录（best-effort，跨 `BaseEntity` 子类反射，剔密钥）。输出单 JSON。审计 `GdprSubjectExported`。
> **局限（§10）**：深关系 PII（如该员工经手的客户主数据本身的个人字段）不自动发现；MVP 聚焦显式可归因列。

### §6.3 数据主体擦除（被遗忘权，匿名化为主）
`DELETE /api/platform/gdpr/erase/subject/{userId}?confirm=true`（无 `confirm` → E-SEC-038）：
1. 防护：目标是平台超管/最后一个平台超管 → E-SEC-036/037（§3.3）。
2. 匿名化 `Sys_User`：遍历 `[PiiField]` 列按 `Mode` 擦（`NickName/Email/LastLoginIp`→null）；`UserName`→`anon-{Id.ToString("N")[..8]}`（保唯一、令原账号不可再登录）；`Password`→`BCrypt(随机)`；`Enable=false`。**保留行 + Id → 所有 FK/审计引用完整**。
3. 吊销会话：撤销该用户全部 refresh 令牌 + jti 入黑名单（复用 #1 `IRefreshTokenService`/黑名单；若仅有按 token 撤销，则标记其令牌族失效）。
4. （可选 best-effort）把 `Creator/Modifier == 原 UserName` 的审计串改为匿名串。
5. 审计 `GdprSubjectErased`（落平台租户，记原 userId）。

### §6.4 整租户擦除
`DELETE /api/platform/gdpr/erase/tenant/{tenantId}?mode=anonymize|purge&confirm=true`（无 `confirm` → E-SEC-038；默认 `mode=anonymize`）：
- 防护：目标是平台租户 `…A1` → E-SEC-036。
- `mode=anonymize`：反射枚举该租户全部 `BaseTenantEntity` 行，对含 `[PiiField]` 的实体逐行按 §6.3 策略匿名化（保行/保 FK）；非 PII 实体不动。`Sys_Tenant.Enable=false`（停用）。
- `mode=purge`：反射枚举该租户全部 `BaseTenantEntity` 行**物理删**（注意 FK 顺序：先子后父，或暂用 `IgnoreQueryFilters` 批量按依赖拓扑；复杂 FK 链分批；relational 事务包裹原子）；最后删 `Sys_Tenant` 行。**purge 不可逆**。
- 审计 `GdprTenantErased`（落平台租户，记 mode + 目标租户码）。
> **purge 的 FK 顺序与原子性是落地难点**（§10）：MVP 用 relational 事务 + 按 EF 关系图拓扑排序删除；InMemory 测仅验匿名化路径（事务/级联不可验，同 #4）。

### §6.5 敏感列剔除（导出共用）
导出序列化前对每实体列名做拒名单匹配（§2.3）跳过 → 导出包不含密码哈希/令牌哈希/密钥。`Sys_User.Password` 等天然被剔。

## §7 授权 / DI / 多租户 / 配置 / 种子

- **DI**：`AddScoped` 新服务 `ITenantAdminService`/`IImpersonationService`/`IGdprService`/`IPlatformAdminService`（控制器瘦、逻辑入服务，便于单测）；`[RequirePlatformAdmin]` 经 `RequestServices` 取 `CP6Context`（已注册）。`SecurityOptions` 加 `ImpersonationMinutes`(默认 30)。
- **多租户**：无新 `BaseTenantEntity`（仅 `Sys_User` 加列）→ 不新增过滤/索引。平台端点一律带外 + 跨租户用 `IgnoreQueryFilters`。
- **种子**（`Program.cs`，幂等，admin seed 之后）：
  1. `Sys_Tenant` 默认租户行：若 `!Any(t=>t.Id==DefaultTenant)` → 加 `{ Id=DefaultTenant, TenantCode="DEFAULT", TenantName="默认租户", Enable=true }`（令 `ITenantEnumerator` 可列出、租户列表非空）。
  2. 引导首个平台超管：默认租户 admin（`UserName=="admin" && TenantId==DefaultTenant`）→ `IsPlatformAdmin=true`（幂等）。
  - **不 seed 任何平台菜单/权限点**（决策 D1 带外 → 平台区不入 `Sys_Menu`/`Sys_RoleMenu`；MenuId 117 仍空闲留后续 RBAC 功能）。
- **配置**：`SecurityOptions.ImpersonationMinutes`；GDPR 无配置项。

## §8 i18n（五语）+ 前端（平台区带外）

### §8.1 i18n seed `I18nTenantComplianceSeed`（仿 `I18nSecScreenSeed`，`.Concat` 接 Program.cs）
- 错误码 `E-SEC-031~038` 五语（ZhCN/ZhTW/En/Ja/Ko）。
- 画面词条 `platform.{tenant.*, admin.*, impersonation.*, gdpr.*}`（标题/列名/按钮/确认提示/字段）五语。

### §8.2 前端平台区（`cp6.web/src`）
- **带外导航**：登录响应返 `isPlatformAdmin` → Pinia 存；顶栏/侧栏渲染独立"平台管理"入口（仅 `isPlatformAdmin` 时显示），路由 `/platform/*`，前端 guard 校验该标志（UX 层；真闸门在后端 `[RequirePlatformAdmin]`）。**不依赖 `Sys_Menu` 下发**（区别于业务菜单）。
- 视图：`views/platform/TenantListView.vue`（租户列表/建租户向导含一次性临时密码展示/停用续期）、`PlatformAdminView.vue`（平台超管授撤）、`ImpersonationView.vue`（选租户/用户切入 + 切出 + 当前 impersonation 横幅）、`CrossTenantAuditView.vue`（跨租户审计查询）、`GdprView.vue`（导出/擦除，二次确认弹窗）。
- **impersonation 横幅**：切入后全局顶部醒目条"正在以 {租户}/{用户} 身份操作 · 切出"（防误操作 + 合规可见）。
- `api/platform/*.ts` + `types/platform/*.ts`；router `viewModules` 加 `/platform/...` 行。

## §9 测试策略

### §9.1 单测（InMemory + 直构控制器/服务，注入假 claims）
- **平台超管闸门**（§3）：`[RequirePlatformAdmin]` —— 这是过滤器，仿 `SecurityLogControllerTests` 直 `new` 控制器会**绕过**过滤器，故过滤器本身的 403 移交 §9.2 集成/gstack；服务层逻辑单测覆盖。**防自锁死**（撤销/擦除最后一个平台超管 → 抛 → E-SEC-037）服务层单测。
- **块①**：建租户 → `Sys_Tenant` + 首个 admin 同事务落库、admin `TenantId`==新租户 / `IsPlatformAdmin`==false / `MustChangePassword`==true / 返回 tempPassword 且库内为 BCrypt 哈希（非明文）；编码冲突 → E-SEC-033；停用/重启用切换 `Enable`；审计行落平台租户。
- **块②**：impersonation start 签发令牌 `tenant_id`==目标租户、`NameIdentifier`==目标用户、`impersonator_id`==真身、无 `is_platform_admin`；目标用户不存在 → E-SEC-035；`OperLog.ImpersonatorId` 在有 `impersonator_id` claim 时被填（filter 单测）；平台操作审计行 `TenantId`==平台租户、`requestTenantCode`==目标租户码（§5.4）。
- **块③**：数据主体擦除 → `[PiiField]` 列被擦、`UserName`==`anon-…`、`Enable`==false、**行仍在**（Id 不变，FK 可解析）、Password 非原值；导出剔密钥（导出 JSON 不含 Password/TokenHash 等）；整租户匿名化 vs purge 两路径（purge 真删/原子仅 relational 可验，InMemory 验匿名化）；防护 E-SEC-036/037/038。
- **纯函数**：敏感列拒名单匹配、PII 反射擦除器、按 TenantId 反射查询表达式构造、防自锁死计数。

### §9.2 gstack QA（T-QA）
起后端+前端：平台超管登录见平台区，普通租户管理员登录**不见**平台区且直接访问 `/api/platform/*` → 403（验带外闸门 + 向量 A/B 失效：用普通管理员尝试经 user-role/role-perm 自赋无法获得平台权）；建租户 → 用返回临时密码登录新租户 admin（被强制改密）；切入某租户读写一条数据 + 横幅可见 + 切出；跨租户审计页见 impersonation 起止 + OperLog 带 impersonator；GDPR 导出下载 JSON（目检无密钥）+ 数据主体匿名化后原账号无法登录但其历史单据仍在；多租户隔离目检。

## §10 已知局限 / Hardening（记录，不做）

**本期局限（文档化接受）：**
- **impersonation 刷新即隐式切出**：access 令牌过期 / 前端自动 refresh → 回平台超管会话；窗口 = `ImpersonationMinutes`。MVP 接受（横幅提示）；持久化 impersonation 会话留后续。
- **GDPR 数据主体深关系 PII 不自动发现**：仅显式可归因列（UserId/Creator/Modifier）+ `Sys_User` 自身（§6.2）。
- **整租户 purge 的 FK 拓扑/原子**：复杂关系链分批删，原子仅 relational 可验（InMemory 限制，同 #4）。
- **平台超管单一硬位**：无平台内分级（合规专员 vs Owner）。
- **`OperLog.ImpersonatorId` 仅覆盖被 OperLog 记录的请求**：跳过名单内的路径（/api/auth 等）在 impersonation 下不带归因（边界由 start/end SecurityLog 兜底）。

**Hardening（后续）：**
- 平台角色分级（决策 D1 方案 C：平台权限点 on top of 硬位）。
- impersonation 持久会话 + 范围限定（只读模式开关 / 限定模块）。
- GDPR：导出包加密/签名、异步大导出、深关系 PII 图遍历、擦除可验证报告（擦了哪些表/行）。
- 防篡改审计（哈希链/WORM）、平台审计独立存储。
- 物理隔离库 / 每租户独立 DB（强隔离）。

## §11 brainstorming 决策定稿（2026-06-23~24；锚点本会话实读核验）

| # | 决策 | 选定 |
|---|------|------|
| **范围** | #5 做哪几块 | **三块全包一份 spec**：租户管理 API + 跨租户运维审计 + GDPR 导出/删除 |
| **D1** | 平台超管模型 | **A. `Sys_User.IsPlatformAdmin` 带外标志位**（RBAC 赋权端点够不到，向量 A/B 天然失效；§3） |
| **D2** | 跨租户运维做到哪层 | **C. 完整 impersonation（读写）**——以目标用户身份切入、全程审计（§5） |
| **D3** | GDPR 粒度 | **A. 双粒度**：按租户 + 按数据主体（§6.1/6.2） |
| **D4** | 被遗忘权删除方式 | **A. 匿名化为主**（`[PiiField]` 擦 PII 保 FK/审计）；物理 purge 为显式 opt-in（§6.3/6.4） |
| **D5** | 建租户引导 | **A. 建租户同时建首个 admin**（RoleId=1 + 强制改密 + 一次性临时密码；§4） |
| **落地决策（按推荐自决，可推翻）** | impersonation 身份模型 | 以**目标租户某用户身份**（默认该租户首个 admin）切入，真身记 `impersonator_id` claim → 复用全部现有权限/盖章/作用域机制（§5.1） |
| **落地决策** | 平台区是否入 RBAC 菜单 | **否**（带外）：平台区前端独立导航 + 后端 `[RequirePlatformAdmin]`，不 seed `Sys_Menu`/`Sys_RoleMenu`（§7/§8.2） |
| **落地决策** | 平台审计租户归属 | 落**平台租户**（默认 `…A1`），`requestTenantCode` 记目标租户 → 不随目标租户被擦而丢（§5.4） |

---

*生成于 2026-06-24（初稿）。全部 §1 锚点本会话由主代理实读核验（角色/权限/提权面/JWT/租户基建/审计/反射遍历/种子/错误码）。底座 #1（已落码）；#5 自洽，不硬依赖 #2/#3/#4。下一步 = 定稿评审（可选，仿 #3/#4 的 R 系列）→ writing-plans（T1~Tn，先排任务清单过用户关）→ subagent-driven（TDD + 先绿后本地 commit 不 push + gstack QA）。*
