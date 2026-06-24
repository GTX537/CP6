# 租户合规设计 spec — S 类安全合规 #5（定稿）

> 源：brainstorming 共识（2026-06-23 范围三块全包；2026-06-24 续做完成"角色/权限模型与提权面"第二轮探勘 + 5 项地基决策定稿，见 §11；同日定稿评审完成，R1~R10 修订全部就位嵌入对应段落 + 汇总表见 §12）。底座 = #1 认证加固（已落码：JWT/Cookie/刷新令牌/安全审计/多租户基建）。#2 2FA / #3 SSO / #4 字段审计当前**仅有 spec、尚未落码**——本子项目**自洽，不硬依赖 #2/#3/#4**（见 §0 / §11）。本子项目把 CP6 从"单租户可演示"推进到"多租户 SaaS 可运营"：补齐 ①**平台级租户管理 API**（建/改/停/续期租户 + 建租户同时开通首个 admin）②**跨租户运维 + 完整 impersonation（读写）+ 全程审计** ③**GDPR 双粒度数据导出/被遗忘权擦除（匿名化为主）**。命名空间 **Sys** + 新增 **Platform** 端点族（带外 `[RequirePlatformAdmin]`，不入 RBAC 菜单网格）。

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

### §2.6 EF 迁移（**R1 修订**：补 `Sys_OperLog.ImpersonatorId`）
`dotnet ef migrations add TenantCompliance --project CP6.Core --startup-project CP6.WebApi`，**两处 DDL**：
1. `Sys_User.IsPlatformAdmin`(bit, NOT NULL, 默认 0)。
2. `Sys_OperLog.ImpersonatorId`(Guid, NULL)——块② 写操作可追溯真身的归因列（§5.3）。`Sys_OperLog` 是 int Id 非 `BaseTenantEntity` 但已手加 `TenantId` 列（`CP6.Entity/DomainModels/Sys/Sys_OperLog.cs` L19）的范式，`ImpersonatorId` 同此样式手加属性。

`[PiiField]`/拒名单/枚举值不映射列；`Sys_Tenant` 表已存在（无变更）。

> **R1 来源**：初稿 §2.6 只写"仅 `Sys_User` 一列"与 §5.3 要"OperLog 加 ImpersonatorId 列"自相矛盾——迁移漏列则块② 审计核心兜底失败。

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

**令牌前置不变量（R3 修订）**：所有 impersonation access 签发**恒** `mustChangePassword:false`（`JwtHelper.GenerateToken` 该参默认 false，不传即安全）。原因：`MustChangePasswordMiddleware` 仅读 claim 不查库、AllowPaths 仅 `change-password`/`logout`、`/impersonation/end` **不在**豁免——若误把目标用户的 `MustChange=true` 传入 imp 令牌，则平台超管切入后所有业务端点被 E-SEC-009 拦截、连切出都做不到。**加固（可选）**：`/api/platform/impersonation/end` 纳入 `MustChangePasswordMiddleware` 的 `AllowPaths`，作为兜底网（即便有人误传 true 也能切回）。

- `POST /api/platform/impersonation/start { tenantId, userId?, reason }`：
  1. 校验目标租户存在且 Enable（否则 E-SEC-032）。
  2. 目标用户 = `userId` 或该租户首个 admin（`RoleId==1 && Enable`，`IgnoreQueryFilters`）；不存在/停用 → E-SEC-035。
  3. **拉黑被替换的平台超管 access jti（R2 修订上半）**：读当前 `User.FindFirst("jti")` + `exp` → 复用 `AuthController.Logout` L289-300 推算法计算剩余 TTL → `ITokenBlacklistService.BlacklistAsync(jti, ttl)`。否则手存旧平台令牌可在 imp 窗口内绕过"平台权挂起"约束反查/拉操作。
  4. 签发 impersonation access 令牌：`JwtHelper.GenerateToken(userId=目标用户.Id, userName=目标用户.UserName, tenantId=目标租户, isPlatformAdmin:false, impersonatorId: 平台超管.Id, mustChangePassword:false, expireMinutes: SecurityOptions.ImpersonationMinutes(默认 30))`。
  5. **只覆盖 access Cookie**（复用 `_cookies.WriteAuthCookies` 或新增"仅 access"写法）；refresh Cookie 不动（刷新或过期 → 自然回到平台超管正常会话，即隐式切出）。
  6. 审计 `ImpersonationStarted`（userId=真身 platform admin、requestTenantCode=目标租户码、reason）。审计行落**平台租户**（§5.4）。
  7. 返回 `{ impersonating: true, tenantId, tenantName, userId, userName, menus, expiresInMinutes }`——`menus` 字段为目标用户的菜单并集（前端据此替换 localStorage 的 `menus`，避免还在平台超管菜单上误以为切入了，R8）。
- `POST /api/platform/impersonation/end`：
  1. **拉黑当前 imp access jti（R2 修订下半，安全洞修复）**：读 `User.FindFirst("jti")` + `exp` → 同上算法 → 入黑名单。否则切出后旧 imp 令牌在 TTL（默认 30 min）内可被攻击者重放继续以目标用户身份操作。
  2. 读 `impersonator_id` claim → 取回平台超管用户（`IgnoreQueryFilters`）；若该真身已被 `IsPlatformAdmin=false` 撤销或 `Enable=false` → 拒 E-SEC-031（保证撤销立即生效，§9.1 测试覆盖）。
  3. 重签其正常令牌（`isPlatformAdmin:true`）覆盖 access Cookie；返回 `{ impersonating:false, menus:<平台超管菜单> }`（前端替换 localStorage 的 `menus`，回到原态）。
  4. 审计 `ImpersonationEnded`。
- **此端点不加 `[RequirePlatformAdmin]`**（因当前 token 无 `is_platform_admin`），改为：方法级 `[Authorize]` + 自查 `impersonator_id` claim 存在且真身仍是启用平台超管，否则 401/403。

### §5.3 窗口内每次写操作的可追溯（块②审计核心，**R7 修订**：Kafka 路径透传）
impersonation 期间写操作以目标用户身份落库，但**审计必须能回指真身**。`Sys_OperLog`（每请求一行，`OperLogFilter` 已记控制器/动作/参数/IP）**加一列 `Guid? ImpersonatorId`**（§2.6 已落 DDL）。

**填写位置**（R7 关键）：`OperLogFilter.OnActionExecutionAsync` 构造 `log = new Sys_OperLog { … }`（`CP6.WebApi/Filters/OperLogFilter.cs` L100–114）**当场**从 `context.HttpContext.User.FindFirst("impersonator_id")?.Value` 解析填入 `log.ImpersonatorId`——既覆盖 DB 降级写（L132–142），也随 **Kafka payload 透传到 consumer**（L122 `_transport.PublishAsync(log)`，消费者反序列化落 DB）；若只在 DB 加列不在 log 对象赋值，Kafka 路径会丢字段（消费者侧 scope 与请求上下文断开，无 claim 可读）。

> `OperLogFilter` 跳过 `/api/auth`、`/api/operlog`（既有 L80–83）；impersonation 下的业务写不在跳过名单 → 正常记录 + 带 `ImpersonatorId`。这样 impersonation 窗口内**每个被 OperLog 覆盖的写请求**都可归因真身（start/end 的 `Sys_SecurityLog` 给出窗口边界，OperLog 给出窗口内逐操作）。

### §5.4 平台操作审计行的租户归属（铁律，**R5 修订**：CurrentTenantId 依赖明示；**R10 修订**：拆独立控制器）
所有平台级审计（租户 CRUD、平台超管授撤、impersonation 起止、GDPR）经 `ISecurityAuditService.LogAsync` 落 `Sys_SecurityLog`。**这些行的 `TenantId` 须落平台租户（默认 `…A1`），不落目标租户** → 否则 GDPR 擦除/purge 目标租户会连同删掉"对它做过什么"的平台审计，违反留痕。

**落地依赖（R5）**：已核验 `SecurityAuditService.LogAsync` **不显式盖 TenantId**——只 `_db.Sys_SecurityLogs.Add(log)` 后 `SaveChangesAsync`，`TenantId` 靠 `CP6Context.StampTenant` 盖 `CurrentTenantId`（`CP6.Core/EFDbContext/CP6Context.cs` L1936-1945）；`requestTenantCode` 参数是独立的"租户码字符串"字段，非 `TenantId`。因此"落平台租户"=调 `LogAsync` 时**当前请求 scoped `CurrentTenantId` 必须为平台租户**：
- 正常平台端点：当前会话 `tenant_id` claim = 平台超管所在租户 = 默认租户 ✓。
- `impersonation/start`：审计**在签发新 access cookie 之前**记，当前请求仍持平台超管 token、`CurrentTenantId` 仍 = 平台租户 ✓（cookie 覆盖只影响**下一次**请求）。
- `impersonation/end`：审计在切回平台超管 token **之后**记？——否，应**与 start 对称**：先记审计（此刻 `CurrentTenantId` = 目标租户，**会出错**！）→ 因此 end 流程顺序须为：**(1) 先记审计但显式覆盖 `_tenant.CurrentTenantId = 平台租户`**（`ITenantContext.CurrentTenantId` 可写，§1 锚点）→ (2) 拉黑当前 imp jti → (3) 重签 + 覆盖 cookie。或者更稳：所有平台审计写入**包一个 `using var _ = new TenantScope(_tenant, DefaultTenant)`** 即用即还（小工具类，本期内联即可）。
- **GDPR purge 后**写 `GdprTenantErased` 尤须保证 `CurrentTenantId=平台租户`——若 purge 期间临时切到目标租户做反射查询，写审计前必须切回。

`requestTenantCode` 字段填**目标租户码**保留可追溯。

> **跨租户审计查询端点 = 独立 `CrossTenantAuditController`（R10）**：`CP6.WebApi/Controllers/Platform/CrossTenantAuditController.cs`，路由 `api/platform/audit`，类级 `[Authorize]` + `[RequirePlatformAdmin]`。`GET /api/platform/audit?tenantCode=&eventType=&from=&to=&page=&pageSize=` → 查 `Sys_SecurityLog.IgnoreQueryFilters()` 全租户审计（带外，绕行级过滤），按条件筛选 + 分页（仿 `SecurityLogController` 的 clamp/`{total,rows}`）。**与 `ImpersonationController` 拆开**：查审计与切入概念正交，将来扩 OperLog 跨租户查询/导出可挂同一控制器，无需污染 impersonation 路由。

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

### §6.4 整租户擦除（**R6 修订**：反射集合 + 拓扑算法明示）
`DELETE /api/platform/gdpr/erase/tenant/{tenantId}?mode=anonymize|purge&confirm=true`（无 `confirm` → E-SEC-038；默认 `mode=anonymize`）：
- 防护：目标是平台租户 `…A1` → E-SEC-036。

**反射集合（R6 关键）**：全仓共 62 个 `.cs` 文件实现/引用 `BaseTenantEntity`（含 Context 与基类）；但**purge/anonymize 必须遍历的"租户作用域行"集合 = 三集合并**：
1. **所有 `BaseTenantEntity` 直接子类**：`modelBuilder.Model.GetEntityTypes().Where(t => typeof(BaseTenantEntity).IsAssignableFrom(t.ClrType) && t.BaseType is null)`（沿用 `CP6Context.OnModelCreating` L1877-1878 的判式）。
2. **`Sys_OperLog`**：int Id 非 `BaseTenantEntity` 但**手加 `TenantId` 列 + 经 `StampTenant` 盖章**（`Sys_OperLog.cs` L19 注释自述、`CP6Context` L1942-1944 盖章）——若漏 OperLog，目标租户的所有操作日志将残留于 purge 后。
3. **`Sys_Tenant` 行本身**：表共享、键即 `tenantId`，最后删（purge 模式末尾 `_db.Sys_Tenants.Remove(t)`）。
集合 1 ∪ 2 = "TenantId 作用域行表"运行时计算式：`var ownerTypes = _db.Model.GetEntityTypes().Where(t => t.FindProperty("TenantId") != null && t.ClrType != typeof(Sys_Tenant)).Select(t => t.ClrType)`——这个**统一判式**比"是否继承 BaseTenantEntity"更鲁棒，自动把 OperLog 纳入。

- `mode=anonymize`：对集合 1 ∪ 2 中含 `[PiiField]` 的实体逐行按 §6.3 策略匿名化（保行/保 FK）；非 PII 实体不动。`Sys_Tenant.Enable=false`（停用）。
- `mode=purge`：对集合 1 ∪ 2 行**物理删** → 最后删 `Sys_Tenant` 行。**purge 不可逆**。FK 顺序与算法见下。

**FK 拓扑与原子性（R6 落地算法）**：
1. **依赖图**：`var fks = _db.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()).Select(fk => (Child: fk.DeclaringEntityType.ClrType, Parent: fk.PrincipalEntityType.ClrType))`；据此拓扑排序（Kahn 算法）得**反向删除顺序**：先无被引用的叶子，再向根删。
2. **批量删**：每类型用 `_db.Set(clrType).IgnoreQueryFilters().Where(e => EF.Property<Guid>(e,"TenantId")==tenantId).ExecuteDeleteAsync()`（EF Core 7+，单 SQL 批量；无需 ChangeTracker 装入）。
3. **原子**：包 `using var tx = await _db.Database.BeginTransactionAsync()`；末尾 `tx.Commit()`。InMemory provider 无事务/无 `ExecuteDelete` 关系语义 → 单测仅验匿名化路径 + 反射集合判式（同 #4 R6）。
4. **降级**：若某类型有循环 FK（自引用如 Wf 流程父子节点）→ 拓扑排序会同层不可解；对这类先一次性 `UPDATE ... SET ParentId = NULL`（按 TenantId 限），再走删除路径。本期不识别非自引用环；预计现仓无此场景，若出现走 hardening。

- 审计 `GdprTenantErased`（**落平台租户**，§5.4 切回工具类；记 mode + 目标租户码）。

> **R6 来源**：初稿对反射集合表述模糊（只说"BaseTenantEntity"漏掉 OperLog）；FK 顺序只说"或暂用 IgnoreQueryFilters 批量按依赖拓扑"但无算法落点；ExecuteDelete + Kahn 拓扑 + InMemory 局限须显式点明，否则实现者会用 `RemoveRange` 装载全表 → 大租户 OOM。

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

### §8.2 前端平台区（`cp6.web/src`，**R8 修订**：登录响应 + impersonation 状态持久化 + 菜单切换）

**8.2.1 登录响应扩字段**：`AuthController.Login` L195-202 当前响应 `{ userName, nickName, roleId, menus, mustChangePassword }`，**新增 `isPlatformAdmin: user.IsPlatformAdmin`** 一字段；登录视图 `LoginView.vue` L234-248 已存 `localStorage.cp6_authed/cp6_mustChangePwd/userName/nickName/menus`——**追加 `localStorage.setItem('cp6_isPlatformAdmin', res.isPlatformAdmin ? '1' : '')`**。Pinia store（新建 `stores/platform.ts` 或并入 `stores/permission.ts`）暴露 `isPlatformAdmin` 计算属性。

**8.2.2 带外导航**：顶栏/侧栏渲染独立"平台管理"入口（仅 `isPlatformAdmin` 为真时显示），路由 `/platform/*`，前端 guard 校验该标志（UX 层；真闸门在后端 `[RequirePlatformAdmin]`）。**不依赖 `Sys_Menu` 下发**（区别于业务菜单）。

**8.2.3 视图**：`views/platform/TenantListView.vue`（租户列表/建租户向导含一次性临时密码展示/停用续期）、`PlatformAdminView.vue`（平台超管授撤）、`ImpersonationView.vue`（选租户/用户切入 + 切出 + 当前 impersonation 横幅）、`CrossTenantAuditView.vue`（跨租户审计查询）、`GdprView.vue`（导出/擦除，二次确认弹窗）。`api/platform/*.ts` + `types/platform/*.ts`；router `viewModules` 加 `/platform/...` 行（仿现 `router/index.ts` L5+ 表）。

**8.2.4 impersonation 状态持久化（R8 关键）**：三 Cookie 全 httpOnly → 前端**读不到** claim，必须靠端点响应 + sessionStorage 自管态：
- `POST /impersonation/start` 响应 `{ impersonating:true, tenantId, tenantName, userId, userName, menus, expiresInMinutes }`（§5.2 已落新字段）。前端处理：
  1. `sessionStorage.setItem('cp6_impersonating', JSON.stringify({tenantName, userName, expiresAt}))`——**用 sessionStorage 不用 localStorage**：避免一次切入污染所有标签页（一个标签切入不影响另一标签的平台超管会话；亦防关闭浏览器后状态残留）。
  2. **替换菜单**：`localStorage.setItem('menus', JSON.stringify(res.menus))` + `addDynamicRoutes(res.menus)`——否则切入后侧栏还在平台超管菜单（"平台管理"还能见），与"平台权挂起"语义矛盾。同时前端 guard 隐藏"平台管理"入口（读 sessionStorage 的 impersonating 标志）。
  3. 顶部渲染醒目横幅"正在以 {tenantName}/{userName} 身份操作 · 还剩 {分钟} · 切出"。
- `POST /impersonation/end` 响应 `{ impersonating:false, menus:<平台超管菜单> }`。前端：
  1. `sessionStorage.removeItem('cp6_impersonating')`。
  2. `localStorage.setItem('menus', JSON.stringify(res.menus))` + 路由刷新。
  3. 横幅消失，恢复"平台管理"入口可见。
- **F5 持久化**：sessionStorage 自动跨刷新存活；但若用户关闭并重开标签 → 状态丢失。MVP 接受（与 access TTL 半小时齐数量级，且 `/api/auth/me`-style 端点本仓暂无）。**Hardening（§10 记录）**：新增 `GET /api/platform/impersonation/status` 端点回查 cookie claim → 前端 app 启动时调用一次重建状态。
- **隐式切出（refresh 到期）**：refresh 自动续 → access 回到平台超管态（refresh Cookie 不动，§5.2 第 5 步）。前端无法感知该切换；横幅在 `expiresAt` 到期后**前端自动隐藏 + 提示"已自动切回平台超管会话"**（setTimeout 计时）。

**8.2.5 LoginView 现行为兼容**：`LoginView.vue` L234-248 `localStorage.setItem('cp6_authed'/'cp6_mustChangePwd'/'userName'/'nickName'/'menus')`——impersonation start 不动 `cp6_authed/userName/nickName`（避免 localStorage 全局污染影响其它 SPA 状态），**只换 `menus`**；横幅靠 sessionStorage 的 `cp6_impersonating` 字段渲染（userName/tenantName 从那里读，不与登录态混）。

## §9 测试策略（**R9 修订**：补三条对抗用例）

### §9.1 单测（InMemory + 直构控制器/服务，注入假 claims）
- **平台超管闸门**（§3）：`[RequirePlatformAdmin]` —— 这是过滤器，仿 `SecurityLogControllerTests` 直 `new` 控制器会**绕过**过滤器，故过滤器本身的 403 移交 §9.2 集成（用 `WebApplicationFactory`）打**三道闸**：(a) 无 `is_platform_admin` claim → 403 E-SEC-031；(b) claim 在但 DB `IsPlatformAdmin==false`（被另一平台超管撤销而 token 未过期，模拟 §3.2 第(3)步纵深防御）→ 403 E-SEC-031；(c) `impersonator_id` claim 在 → 403 E-SEC-034。服务层逻辑（防自锁死、租户/平台超管校验等）走直构单测。**防自锁死**（撤销/擦除最后一个平台超管 → 抛 → E-SEC-037）服务层单测。
- **块①**：建租户 → `Sys_Tenant` + 首个 admin 同事务落库、admin `TenantId`==新租户 / `IsPlatformAdmin`==false / `MustChangePassword`==true / 返回 tempPassword 且库内为 BCrypt 哈希（非明文）；编码冲突 → E-SEC-033；停用/重启用切换 `Enable`；审计行落平台租户。**+R9-a**：建租户事务原子失败回滚——服务层注入"步骤 3 BCrypt 哈希抛错"或 "DbUpdateException on Sys_User"→ 验 `Sys_Tenant` 行**也不存在**（单 `SaveChangesAsync` 一并提交，任一失败整体回滚）。
- **块②**：impersonation start 签发令牌 `tenant_id`==目标租户、`NameIdentifier`==目标用户、`impersonator_id`==真身、无 `is_platform_admin`、`must_change_password`==false（R3）；目标用户不存在 → E-SEC-035；`OperLog.ImpersonatorId` 在有 `impersonator_id` claim 时被填（filter 单测，注入带 `impersonator_id` 的 `ClaimsPrincipal` 验 `log.ImpersonatorId == 真身.Id`）；平台操作审计行 `TenantId`==平台租户、`requestTenantCode`==目标租户码（§5.4）。**+R9-b**：start 拉黑被替换平台 access jti（R2 上半）——验调用 `ITokenBlacklistService.BlacklistAsync(原 jti, 剩余 TTL)`；end 拉黑当前 imp access jti（R2 下半）——同验。**+R9-c**：impersonation end 时真身已被 `IsPlatformAdmin=false` 撤销（或 `Enable=false`）→ 拒 E-SEC-031（保证撤销立即生效）；end 流程中 `LogAsync` 前 `_tenant.CurrentTenantId` 被显式置平台租户（R5）——通过假 `ITenantContext` spy 验 `LogAsync` 调用瞬间值。
- **块③**：数据主体擦除 → `[PiiField]` 列被擦、`UserName`==`anon-…`、`Enable`==false、**行仍在**（Id 不变，FK 可解析）、Password 非原值；导出剔密钥（导出 JSON 不含 Password/TokenHash 等）；整租户匿名化 vs purge 两路径（purge 真删/原子仅 relational 可验，InMemory 验匿名化 + 反射集合判式：`OperLog` 是否被纳入待擦集合）；防护 E-SEC-036/037/038。
- **纯函数**：敏感列拒名单匹配、PII 反射擦除器、按 TenantId 反射查询表达式构造、防自锁死计数、Kahn 拓扑排序（构造若干假 FK 图验出栈顺序，R6）。

### §9.2 gstack QA（T-QA）
起后端+前端：平台超管登录见平台区，普通租户管理员登录**不见**平台区且直接访问 `/api/platform/*` → 403（验带外闸门 + 向量 A/B 失效：用普通管理员尝试经 user-role/role-perm 自赋无法获得平台权）；建租户 → 用返回临时密码登录新租户 admin（被强制改密）；切入某租户读写一条数据 + 横幅可见 + 菜单切换到目标用户菜单（R8）+ 切出后菜单回原态；跨租户审计页见 impersonation 起止 + OperLog 带 impersonator；GDPR 导出下载 JSON（目检无密钥）+ 数据主体匿名化后原账号无法登录但其历史单据仍在；多租户隔离目检。**+R9 加测**：(i) impersonation 期间访问 `/api/platform/tenant` → 403 E-SEC-034；(ii) 切出后用浏览器开发者面板把旧 imp Cookie 重放（模拟攻击者抓包）→ 401（jti 已黑，R2）；(iii) 在另一标签建立独立平台超管会话 → 不受第一标签 impersonation 状态影响（sessionStorage 隔离验证，R8）。

## §10 已知局限 / Hardening（记录，不做）

**本期局限（文档化接受）：**
- **impersonation 刷新即隐式切出**：access 令牌过期 / 前端自动 refresh → 回平台超管会话；窗口 = `ImpersonationMinutes`。MVP 接受（横幅倒计时提示，R8）；持久化 impersonation 会话留后续。
- **impersonation 状态 F5 跨标签重建**：sessionStorage 自管 → 关闭并重开标签丢失横幅显示，但后端 cookie claim 仍可用（业务端点正常以目标身份工作）。Hardening = 新增 `GET /api/platform/impersonation/status` 端点 + 前端启动时调用（R8）。
- **GDPR 数据主体深关系 PII 不自动发现**：仅显式可归因列（UserId/Creator/Modifier）+ `Sys_User` 自身（§6.2）。
- **整租户 purge 的 FK 拓扑/原子**：Kahn 拓扑排序 + `ExecuteDeleteAsync` 反向删除 + relational 事务包裹（R6 §6.4）；InMemory provider 无事务/无 `ExecuteDelete` 关系语义 → 单测仅验匿名化路径 + 反射集合判式；purge 真删走集成/gstack。
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

## §12 定稿评审修订表（2026-06-24，R1~R10；锚点本会话实读核验）

| # | 等级 | 受影响段 | 修订要点 | 根因 |
|---|------|----------|----------|------|
| **R1** | 🔴 | §2.6 | 迁移补 `Sys_OperLog.ImpersonatorId`(Guid? null) 列（非 BaseTenantEntity 手加属性，同 TenantId 范式） | 初稿只写"仅 Sys_User 一列"与 §5.3 要求自相矛盾；漏列则块② 审计核心兜底失败 |
| **R2** | 🔴 | §5.2 | start 拉黑被替换平台 access jti；end 拉黑当前 imp access jti（剩余 TTL 取 exp 推算，复用 `AuthController.Logout` L289-300 算法） | 切出后旧 imp 令牌在 30 min TTL 内可重放继续以目标用户身份操作；切入也需对称拉黑旧平台令牌（防手存旧 token 绕过权挂起） |
| **R3** | 🟡 | §5.2 / §10 | impersonation 令牌恒 `mustChangePassword:false`；可选加固 `/impersonation/end` 进 `MustChangePasswordMiddleware.AllowPaths` | `MustChangePasswordMiddleware` 读 claim 不查库、AllowPaths 仅 `change-password`/`logout`、end 端点不在豁免；若误传 true 切入后业务端点全被 E-SEC-009 拦且切不出 |
| **R4** | 🟢 | §4（无需改） | 建首个 admin 必填字段已覆盖 | 已核验 `Sys_User` 仅 `UserName/Password` `[Required]`；`BaseEntity.Creator/Modifier` 可空、`Id` 自动生成、`CreateDate` 有默认；§4 step3 字段集（UserName+Password+TenantId+RoleId=1+Enable+MustChange）足够 |
| **R5** | 🟡 | §5.4 | 明示"落平台租户"=调 `LogAsync` 时 `CurrentTenantId` 必须为平台租户；end 流程需先用 `TenantScope`/显式赋值切回；GDPR purge 后写 `GdprTenantErased` 尤须保证 | `SecurityAuditService.LogAsync` 不显式盖 TenantId、靠 `CP6Context.StampTenant` 盖 `CurrentTenantId`；end/purge 中途切到目标租户时写审计会落错 |
| **R6** | 🟡 | §6.4 | 反射集合统一判式 `t.FindProperty("TenantId") != null && t != Sys_Tenant`（自动纳入 OperLog）；purge 用 Kahn 拓扑反向排序 + `ExecuteDeleteAsync` 单 SQL 批量 + relational 事务；自引用环先 SET PARENT=NULL 再删 | 初稿"BaseTenantEntity 反射"漏 OperLog；purge 算法只说"按 EF 关系图拓扑"未点 ExecuteDelete + Kahn；用 RemoveRange 装载会 OOM |
| **R7** | 🟡 | §5.3 | `OperLogFilter.OnActionExecutionAsync` L100-114 构造 log 时**当场**从 `impersonator_id` claim 解析填入 `log.ImpersonatorId`——Kafka payload 透传到 consumer | OperLog 走 Kafka 主通道（L116-129）+ DB 降级（L132-142）；只在 DB 加列不在 log 对象赋值，Kafka 路径 consumer 反序列化无 claim 上下文，归因丢失 |
| **R8** | 🟡 | §8.2 | 登录响应加 `isPlatformAdmin` + LoginView 存 localStorage；impersonation start/end 响应返 `menus` 前端替换；状态用 sessionStorage 不用 localStorage（标签隔离 + 不残留）；横幅倒计时；F5 跨标签局限文档化 | 三 Cookie 全 httpOnly 前端读不到 claim；不替换菜单则侧栏还在平台超管菜单与"权挂起"语义矛盾；localStorage 全局污染会让所有标签同时进入 impersonation 错觉 |
| **R9** | 🟡 | §9 | 补三条对抗测试：(a) `[RequirePlatformAdmin]` 三道闸集成测（无 claim / DB 已撤销 / imp 期间）；(b) 建租户事务原子失败回滚验证；(c) end 时真身已撤销 → 拒 + `LogAsync` 前 `CurrentTenantId` 已切平台租户 | 初稿仅说"过滤器 403 移交集成"未列三道闸断言；事务原子和撤销立即生效是块①/② 关键不变量 |
| **R10** | 🟢 | §5.4 | 跨租户审计查询拆独立 `CrossTenantAuditController`（路由 `api/platform/audit`），不并入 `ImpersonationController` | 查审计与切入概念正交；将来扩 OperLog 跨租户查询/导出可挂同一控制器，无需污染 impersonation 路由 |

**评审覆盖确认**：本轮评审顺 §1→§10 逐段精读，重点针对 §2.6 迁移完备性 / §3 闸门纵深防御 / §5 imp 全生命周期（签发、jti 失效、菜单切换、审计落点、Kafka 透传）/ §6 反射集合与拓扑算法 / §8 前端态机/§9 测试断言覆盖。无遗留疑点。

---

*生成于 2026-06-24（**定稿**）。全部 §1 锚点本会话由主代理实读核验（角色/权限/提权面/JWT/租户基建/审计/反射遍历/种子/错误码）；R1~R10 修订对应 §2.6/§5.2/§5.3/§5.4/§6.4/§8.2/§9/§10 八处段落，再由主代理实读核验（`Sys_OperLog`/`OperLogFilter`/`AuthController.Logout`/`SecurityAuditService`/`CP6Context`/`LoginView.vue` 等 8 处真代码锚点）。底座 #1（已落码）；#5 自洽，不硬依赖 #2/#3/#4。下一步 = `writing-plans`（T1~Tn，先排任务清单过用户关）→ subagent-driven（TDD + 先绿后本地 commit 不 push + gstack QA）。*
