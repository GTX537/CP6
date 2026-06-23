# 字段级审计回放设计 spec — S 类安全合规 #4（定稿）

> 源：brainstorming 共识（2026-06-22）；定稿评审 2026-06-23（§9，7 点修订，全部锚点经只读 subagent 核验实代码）。底座 = #1 认证加固（已落码）；#2 2FA / #3 SSO 当前**仅有 spec、尚未落码**——本子项目**自洽，不硬依赖 #2/#3**（见 §0 / §9 R1）。本子项目在 `CP6Context.SaveChanges` 写入管道叠加**字段级 before/after 变更史**（who/when/what 全要素），补足现有"请求级操作日志（`Sys_OperLog`）+ 行级审计元字段（`BaseEntity.Creator/Modifier`）"缺失的**数据变更前后值留痕 + 时间线回放**。命名空间 **Sys**，多租户 `BaseTenantEntity`。

## §0 范围

**做（MVP）**：
- **标记式 opt-in 字段级审计**：实体实现空标记接口 `IAuditable` 才纳入审计；默认不审计。聚焦高价值/敏感实体（用户/角色/权限授权/会计科目/价表/客商主数据等），避开海量事务行（库存流水/受注明细已有不可变日志，不重复）。
- **字段级 before/after 捕获**：在 `CP6Context` 写入管道（与 `StampTenant` 同处）遍历 `ChangeTracker`，对标记实体的 Added/Modified/Deleted 捕获每个变更字段的旧值→新值。
- **密钥绝不入审计**：`[AuditIgnore]` 属性级排除 + 内建密钥拒名单（双保险）。
- **原子落库**：审计行与业务变更在同一逻辑事务持久化（§3.4，事务包裹两阶段，relational 生产原子；InMemory 降级）。
- **当前用户归属**：捕获 userId/userName（注入同步 `ICurrentUserAccessor`，读 JWT claims）；后台/系统写入记 null。
- **更新路径补齐（评审 R2）**：把首批 opt-in 中走 attach-as-Modified 的两处控制器 `RoleController.Update` / `MenuController.Update` 改为"先查后改"（仿 `UserController.Update`），令 `Sys_Role` / `Sys_Menu` 的字段变更能产生准确 diff。`RepositoryBase.UpdateAsync`（断连模式、广泛复用）**不动**——其更新路径下的实体仅得 Added/Deleted 留痕（§8 记录）。
- **查询 + 回放 UI**：按 实体/主键/操作人/时间 分页筛选 + 单记录变更时间线回放（diff old→new）。
- 多租户隔离、权限点、五语 i18n、前端。

**不做（YAGNI）**：
- **全实体审计**（存储爆炸 + 与既有不可变日志重复 + 高频写性能）。
- **字段级 opt-in**（逐属性打标，配置量大、漏标风险；已用"实体 opt-in + 字段 ignore"组合）。
- **防篡改**（哈希链 / WORM 存储 / 数字签名）——留 hardening。
- **高频异步卸载**（写入队列 / 旁路表）——MVP 同步原子即可（opt-in 已限流量）。
- **保留期自动清理**——默认**永不自动清理**（合规留痕）；可配清理留后续。
- **审计值级 PII 加密**——留 hardening。
- **跨字段语义快照 / 关联实体级联审计**（只记被改实体本身的标量列变更）。
- **改造 `RepositoryBase` 断连更新为先查后改**（全模块加一次 DB 往返 + 行为变更，blast radius 大）——留 hardening。

## §1 现状锚点（已核验，2026-06-23；行号可能微移）

- **写入管道** `CP6.Core/EFDbContext/CP6Context.cs`：私有 `StampTenant()`（L1936–1945）遍历 `ChangeTracker.Entries<BaseTenantEntity>()`，Added 且 `TenantId==Guid.Empty` 时盖 `CurrentTenantId`；**并已显式补盖 `Sys_OperLog`**（int Id 非 BaseTenantEntity）。重写 `SaveChanges(bool acceptAllChangesOnSuccess)`（L1947）与 `SaveChangesAsync(bool, CancellationToken)`（L1953）各先调 `StampTenant()` 再转 `base`；无参版本**未重写**（经 base 路由至 bool 重载，不重复盖章）。**字段级审计捕获挂这两个重写**（§3）。**当前 `CP6Context` 全文无任何 `BeginTransaction`/`Database.CurrentTransaction` 用法、`SaveChanges` 重写内除 `StampTenant` 外无其它副作用**——故本子项目的两阶段事务为新增。
- **租户上下文** `CP6Context` 构造 `(DbContextOptions<CP6Context> options, ITenantContext? tenant = null)`（L29–32，**仅此一个构造**）；`CurrentTenantId => _tenant?.CurrentTenantId ?? TenantContext.DefaultTenant`（L35）。**本子项目加可选第三参** `ICurrentUserAccessor? user = null`——经核验向后兼容全部现有单/双参构造调用。**DI 注入机制**：`Program.cs:41` 用 `AddDbContext<CP6Context>(opt => opt.UseSqlServer(...))`（**非** `AddDbContextPool`），现有 `ITenantContext` 即经 **EF Core 自动构造注入**（从应用 DI 解析已注册服务填充 DbContext 构造可选参）。故新增 `ICurrentUserAccessor` 同机制：只需在 DI 注册该服务 + 加可选第三参，**无需改 `AddDbContext` 注册代码**（pooling 才禁止构造服务注入，此处非 pooled，安全）。
- **实体基类** `CP6.Entity/BaseEntity.cs`：`Id` 为 `Guid` 且标 `[Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)]`（L14–16，**store-generated**，无客户端默认 → Added 实体 `Id` 在 `base.SaveChanges` 前为 `Guid.Empty`/临时值，存后才落定真值 → 审计 EntityKey 须**存后**取，见 §3.4，两阶段不可省）。审计元字段：`string? Creator`（L21）/`DateTime CreateDate`（L27，非空默认 `DateTime.Now`）/`string? Modifier`（L32）/`DateTime? ModifyDate`（L38）。`BaseTenantEntity : BaseEntity` 加 `Guid TenantId`（`BaseTenantEntity.cs:11`）。
- **更新模式（关键，评审 R2）**：
  - `CP6.Core/Services/Pub/BaseCrudService.cs` `UpdateAsync`（L73–90）= `FindAsync(incoming.Id)` 载入原实体 → `Db.Entry(original).CurrentValues.SetValues(incoming)`（**tracked 先查后改**，`OriginalValues` 完整保留 → diff 准确，保 Creator/CreateDate）。**Fin/Pur/Erp 主数据（GlAccount/SupplierPrice/BusinessPartner 等）走此路径 → diff 准确。**
  - `UserController.Update`（L77–105）= `FindAsync` → 逐属性改 → SaveChanges（**先查后改，diff 准确**）。
  - **`RoleController.Update`（L64–71）/ `MenuController.Update`（L44–51）= `_context.Entry(entity).State = EntityState.Modified`（attach-as-Modified 断连）** → `OriginalValues==CurrentValues` → diff 全落空。`Sys_Role`/`Sys_Menu` 在首批 opt-in，故 **§0 将这两处改为先查后改**。
  - `RepositoryBase.UpdateAsync`（`CP6.Core/BaseProvider/RepositoryBase.cs` L62–68）= `Entry(entity).State = Modified`（断连，广泛复用）**不动**（§8 局限）。
- **请求级日志** `CP6.WebApi/Filters/OperLogFilter.cs`（跳过逻辑 L80–83：含 `/api/operlog`、`/api/auth` 的路径 return）+ `Sys_OperLog`（int Id、TenantId 手动注册租户全局过滤、记控制器/动作/参数/耗时/IP），**非**字段级数据变更——与本子项目互补。
- **安全事件审计** `ISecurityAuditService`/`Sys_SecurityLog`（#1，`BaseTenantEntity`）：记登录成败/锁定/改密等**事件**，"失败不阻断"。与本子项目（数据变更）互补，不复用。
- **当前用户** 业务侧 `(await _perm.GetAsync()).UserId`（`ICurrentPermissionContext`，async）；写入管道需**同步**取用户 → 新建轻量 `ICurrentUserAccessor`（读 `IHttpContextAccessor` 的 JWT claims：NameIdentifier→userId、Name→userName），仿 `_tenant` 注入。
- **多租户** `OnModelCreating`（L1873–1931）反射批量：对所有 `BaseTenantEntity` 注册全局查询过滤 `WHERE TenantId==CurrentTenantId` + 唯一索引自动补 `TenantId` 前缀；`StampTenant` 写入盖章。`Sys_FieldAuditLog : BaseTenantEntity` + 加 `DbSet` 即**自动**纳入过滤/索引/盖章遍历。
- **分页列表控制器范本** `SecurityLogController`（L24–66）：`GetList([FromQuery] … int page=1,int pageSize=20)`，`page=Math.Max(1,page); pageSize=Math.Clamp(pageSize,1,200);`，`.OrderByDescending(x=>x.CreatedAt)`，返 `{ total, rows }`，类级/方法级 `[RequirePermission("sys-security-log","query")]`。测试 `SecurityLogControllerTests`：**直 `new SecurityLogController(db)` 调方法**（绕过 `[RequirePermission]` 过滤器，不测 403），`Unwrap` 反射读匿名 `total`/`rows`（§7.1 据此修订）。
- **i18n seed** `I18nSecScreenSeed`（`CP6.WebApi/Seed/`）：`public static readonly Sys_Lang[] Items`，词条形如 `new Sys_Lang{ LangKey="E-SEC-001", ZhCN=…, ZhTW=…, En=…, Ja=…, Ko=… }`；Program.cs 经 `.Concat(...I18nSecScreenSeed.Items)`（L1559）接入 i18n seed 链。
- **授权 / 权限点 seed 范本**（Program.cs，安全日志块 L1031–1057）：`if(!db.Sys_Menus.Any(m=>m.MenuId==114)){ Sys_Menus.Add(new Sys_Menu{MenuId=114,MenuName="安全日志",RoutePath="/sys/security-log",Icon="Lock",ParentId=100,OrderNo=114,Enable=true}); Sys_RoleMenus.Add(new Sys_RoleMenu{RoleId=1,MenuId=114}); }` → **MenuKey 回填** `secMenu.MenuKey = RoutePath.Trim('/').Replace('/','-')`（"/sys/security-log"→`sys-security-log`）→ `Sys_MenuAction(114,"query","查看")` + `Sys_RoleAction(RoleId=1,MenuId=114,"query")`。`[RequirePermission(menuKey,action)]` 的 `menuKey` = **派生的 MenuKey**（即 `sys-security-log`，非 `security-log`）。父菜单 `MenuId=100`"系统管理"（Icon=Setting）已存在；`Sys_Menu.Icon` 字段名 `Icon`。
- **前端** `cp6.web/src`：`views/pms/OperLogView.vue`/`SecurityLogView.vue`（列表+筛选+分页范本）；`router/index.ts` 的 `viewModules: Record<string,()=>Promise<any>>`（L5–154，形如 `'/sys/security-log': () => import('@/views/pms/SecurityLogView.vue')`，新增一行即注册路由）；`api/sys/`（operlog.ts / securityLog.ts 范本，securityLog.ts 含类型）。

## §2 数据模型

### §2.1 新实体 `Sys_FieldAuditLog`（`CP6.Entity/DomainModels/Sys/Sys_FieldAuditLog.cs`）

```csharp
public class Sys_FieldAuditLog : BaseTenantEntity
{
    /// <summary>被审计实体的 CLR 类型名（如 "Sys_User"）。</summary>
    [MaxLength(100)][Required] public string EntityName { get; set; } = string.Empty;
    /// <summary>被审计实体主键（Guid 字符串）。</summary>
    [MaxLength(64)][Required] public string EntityKey { get; set; } = string.Empty;
    /// <summary>操作：1=Added 2=Modified 3=Deleted。</summary>
    public int Operation { get; set; }
    /// <summary>字段差异 JSON：[{ "field":"Email", "old":"a@x", "new":"b@y" }, ...]。</summary>
    public string Changes { get; set; } = "[]";
    /// <summary>操作人（null=后台/系统）。</summary>
    public Guid? UserId { get; set; }
    [MaxLength(100)] public string? UserName { get; set; }
    /// <summary>变更时刻。</summary>
    public DateTime ChangedAt { get; set; }
}
```
索引（`OnModelCreating`）：`(EntityName, EntityKey, ChangedAt)`（单记录时间线回放）；`(UserId, ChangedAt)`（按人审计）；`(EntityName, ChangedAt)`（按实体类型）。均自动补 `TenantId` 前缀（反射批量；非唯一查询索引，正确性不受影响）。`Changes` 为大文本列（nvarchar(max)）。

### §2.2 标记 `IAuditable` + 排除 `[AuditIgnore]`（`CP6.Entity`）

```csharp
namespace CP6.Entity;
/// <summary>实体实现本空接口即纳入字段级审计（opt-in）。</summary>
public interface IAuditable { }

/// <summary>属性级排除：标此特性的列不进入字段审计（密钥/大对象/噪音列）。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute { }
```
- **首批 opt-in 实体（11 个，均已核验存在；实现 `IAuditable`）**：`Sys_User`、`Sys_Role`、`Sys_UserRole`、`Sys_RoleAction`、`Sys_RoleDataScope`、`Sys_RoleFieldPerm`、`Sys_Menu`、`Sys_Tenant`、`GlAccount`、`SupplierPrice`、`BusinessPartner`。
  - 其中 `Sys_Role`/`Sys_Menu` 的 Modified 留痕依赖 §0 的控制器先查后改改造。
  - `Sys_UserRole`/`Sys_RoleAction`/`Sys_RoleDataScope`/`Sys_RoleFieldPerm` 为权限结点表，写入多为增/删（重新授权）→ Added/Deleted 留痕即足。
  - **延后（待 #2/#3 落码后补标，无需改捕获逻辑）**：`Sys_TenantSsoConfig`（#3，当前不存在）。
- **`[AuditIgnore]` 首批标注（已核验存在）**：`Sys_User.Password`。
  - **延后（字段随 #2/#3 落码后补标）**：`Sys_User.TwoFactorSecret`（#2）、`Sys_TenantSsoConfig.ClientSecretProtected`（#3）。**即使漏标也安全**——§3.1 拒名单按"以 `Secret` 结尾"天然命中 `TwoFactorSecret`，并已显式列举 `ClientSecretProtected`（双保险，故 #4 不依赖 #2/#3 的标注先行）。

### §2.3 EF 迁移
`dotnet ef migrations add FieldAudit --project CP6.Core --startup-project CP6.WebApi`（新表 `Sys_FieldAuditLogs`；无其它表/列变更——`IAuditable`/`[AuditIgnore]` 不映射列；控制器先查后改改造不涉及 schema）。

## §3 捕获机制（核心）

### §3.1 内建密钥拒名单（硬墙，与 `[AuditIgnore]` 双保险）
捕获时对字段名做大小写不敏感匹配，命中即跳过（即使实体漏标 `[AuditIgnore]`）：
`Password`、以 `Secret` 结尾、以 `Hash` 结尾、`TokenHash`、`Salt`、`ClientSecretProtected`、`TwoFactorSecret`。
> 核验：现有库内敏感列 `Sys_User.Password`（在拒名单）、`Sys_RefreshToken.TokenHash`/`ReplacedByTokenHash`、`Sys_PasswordHistory.PasswordHash`、`Pub_Attachment.FileHash` 均被"以 `Hash` 结尾"/`Password` 命中（前三者非首批 opt-in，属纵深防御；`FileHash` 为完整性校验非密钥，被排除无害）。无 `Salt` 字段（BCrypt 自带内嵌 salt）。结论：拒名单**覆盖充分**。

### §3.2 跳过字段集
捕获时统一跳过：主键 `Id`、`TenantId`、行级元字段 `Creator/CreateDate/Modifier/ModifyDate`（已有行级审计，避免噪音）、`[AuditIgnore]` 字段、拒名单字段、导航属性（仅标量列）。

### §3.3 `ICurrentUserAccessor`（`CP6.Core/Services/Sys/`）
```csharp
public interface ICurrentUserAccessor { Guid? UserId { get; } string? UserName { get; } }
```
实现读 `IHttpContextAccessor.HttpContext?.User`（`ClaimTypes.NameIdentifier`→`Guid.TryParse`→`Guid?`、`ClaimTypes.Name`→`string?`）；无 HttpContext（后台 Worker/测试）→ 均 null。DI：`AddHttpContextAccessor()`（幂等）+ `AddScoped<ICurrentUserAccessor, CurrentUserAccessor>()`；`CP6Context` 加可选第三参 `ICurrentUserAccessor? user = null`，由 EF Core 自动构造注入（§1，非 pooled，无需改 `AddDbContext`）。

### §3.4 捕获 + 原子落库（`CP6Context`）

**两阶段（兑现"原子"目标，因 `Id` store-generated，Added 键须存后取）：**

```
private List<PendingAudit> CaptureFieldAuditBeforeSave()
{
    var list = new List<PendingAudit>();
    foreach (var e in ChangeTracker.Entries<IAuditable>())   // 触发 DetectChanges（AutoDetectChangesEnabled 默认 true）
    {
        if (e.State is not (Added or Modified or Deleted)) continue;
        var changes = BuildChanges(e);          // 跳过集 + 拒名单；Modified 仅 IsModified && !Equals(orig,cur)
        if (e.State == Modified && changes.Count == 0) continue;   // 空改不记
        list.Add(new PendingAudit(e /*EntityEntry 引用*/, MapOp(e.State), changes));
    }
    return list;   // 此刻不读 Added 的 Id（临时值）
}
```
1. `SaveChanges`/`SaveChangesAsync` 重写内：`StampTenant()` → `var pending = CaptureFieldAuditBeforeSave()`。
2. 若 `pending` 空 → 直接 `base.SaveChanges(acceptAllChangesOnSuccess)`（零开销）。
3. 否则：**relational**（`Database.IsRelational()` 为真）时 `if (Database.CurrentTransaction == null) using var tx = Database.BeginTransaction()`（无环境事务才自开；有则参与不另开）；**InMemory** 跳过事务（不支持，且会告警/抛）。
4. `var result = base.SaveChanges(acceptAllChangesOnSuccess)`（业务变更落库，Added 的 `Id` 落定真值）。
5. 对每个 `pending`：读 `entry.Entity.Id`（现为真值）→ 建 `Sys_FieldAuditLog{ EntityName=ClrType.Name, EntityKey=Id.ToString(), Operation, Changes=JsonSerialize(changes), UserId=_user?.UserId, UserName=_user?.UserName, ChangedAt=DateTime.Now, TenantId=ResolveAuditTenant(entry) }` → `Sys_FieldAuditLogs.Add`。
   - **审计行 TenantId 归属（评审 R4）**：`ResolveAuditTenant(entry)` = `entry.Entity is BaseTenantEntity bt ? bt.TenantId : CurrentTenantId`。即审计行**镜像业务实体的 TenantId**（业务实体经 step1 `StampTenant` 已定 TenantId；后台 Worker 按租户循环写他租数据时也正确）；**共享表**（`Sys_Tenant` 等 `BaseEntity` 无 TenantId）回退 `CurrentTenantId`（落操作者当前租户上下文，超管系统上下文审阅；文档化接受）。step6 走 `base.SaveChanges` 不经 `StampTenant`，故此处**显式设** TenantId（避免二次遍历）。
6. `base.SaveChanges(acceptAllChangesOnSuccess: true)`（审计行落库）。relational：`tx.Commit()`。返 step4 的 `result`（业务影响行数；审计行数不计入返回值）。
   - **审计行自身不被审计**（`Sys_FieldAuditLog` 不实现 `IAuditable`）。
   - 异常（任一 `base.SaveChanges` 抛）：relational 事务回滚（业务+审计同生同死=原子）；记 `ILogger`。
   - **递归护栏**：step6 调 `base.SaveChanges`（基类实现，非虚分发）天然不重入本重写；且仅 `IAuditable` 实体被捕获，审计行非 `IAuditable`（双保险，无需额外标志）。
7. 同步重写 `SaveChanges(bool)` 与异步 `SaveChangesAsync(bool)` 各落一份（异步用 `BeginTransactionAsync`/`SaveChangesAsync`/`CommitAsync`）。

> **diff 值化**：旧/新值经 `Convert.ToString(value, CultureInfo.InvariantCulture)`（null→null）；过长截断（1000 字符，防大文本撑爆 JSON）。`Changes` 经 `System.Text.Json` 序列化（自带转义，无注入风险）。
> **DetectChanges 依赖**：捕获前 `ChangeTracker.Entries<IAuditable>()` 在 `AutoDetectChangesEnabled`（默认 true）下触发变更检测，故 `State`/`IsModified`/`OriginalValues` 准确；若某路径显式 `AutoDetectChangesEnabled=false`，须先手工 `ChangeTracker.DetectChanges()`（本库无此用法，记录约束）。

### §3.5 tracked 更新前置（评审 R2）
字段 diff 的正确性以 **tracked 先查后改**（`OriginalValues` 保留真实原值）为前提：
- **改造**：`RoleController.Update` / `MenuController.Update` 由 attach-as-Modified 改为"先查后改"（`FindAsync(entity.Id)` → 逐属性赋值 → `SaveChanges`，仿 `UserController.Update`；保 Creator/CreateDate）。改后 `Sys_Role`/`Sys_Menu` 的 Modified 产生准确 diff。
- **不改 / 局限**：`RepositoryBase.UpdateAsync`（断连，广泛复用，改之全模块加 DB 往返+行为变更）保持；经其更新的实体若 opt-in，则 Modified diff 落空（仅 Added/Deleted 留痕）。当前首批无实体经 `RepositoryBase` 路径做字段修改（Fin/Pur/Erp 主数据走 `BaseCrudService` 先查后改），故无实际缺口；§8 记录该通用局限。

## §4 查询 / 回放端点（`CP6.WebApi/Controllers/Sys/FieldAuditController.cs`）

`[Authorize]`（类级）；读端点 `[RequirePermission("sys-field-audit","query")]`（评审 R3：menuKey = RoutePath 派生值 `sys-field-audit`）。多租户全局过滤自动隔离本租户。
- `GET /api/sys/field-audit?entityName=&entityKey=&userId=&from=&to=&page=&pageSize=` → 分页列表（`page=Math.Max(1,page)`、`pageSize=Math.Clamp(pageSize,1,200)`，仿 `SecurityLogController`），按 `ChangedAt` 倒序，返 `{ total, rows }`。**列表投影轻量化（评审 R7）**：`rows` 项返 `{id,entityName,entityKey,operation,changeCount,userId,userName,changedAt}`——`changeCount`=该行字段变更数（由 `Changes` 反序列化计数），**不返完整 `changes` JSON**（防 200 行×大文本负载）。
- `GET /api/sys/field-audit/record?entityName=&entityKey=` → 该记录全部变更**时间线**（`ChangedAt` 正序），**返完整 `changes`**（`[{field,old,new}]`，供回放 diff）。
- （只读，无写端点；无新错误码。`entityName`/`entityKey` 经 EF 参数化查询，无注入风险。）

## §5 授权 / DI / 多租户 / 配置

- **权限点 seed**（Program.cs，仿安全日志块 L1031–1057；评审 R3 补全）：
  - 菜单 `Sys_Menu{ MenuId=115（确认未占用，114 已被安全日志占）, MenuName="字段审计", RoutePath="/sys/field-audit", Icon="Document", ParentId=100, OrderNo=115, Enable=true }`。
  - **授角色** `Sys_RoleMenu{ RoleId=1, MenuId=115 }`（范本含此步，原 spec 漏）。
  - **MenuKey 回填**：`menu.MenuKey = RoutePath.Trim('/').Replace('/','-')` → `sys-field-audit`。
  - **操作点** `Sys_MenuAction{ MenuId=115, ActionCode="query", ActionName="查看" }` + `Sys_RoleAction{ RoleId=1, MenuId=115, ActionCode="query" }`。
- **DI**：`AddHttpContextAccessor()`（幂等）+ `AddScoped<ICurrentUserAccessor,CurrentUserAccessor>()`；`CP6Context` 加可选第三参，EF Core 自动构造注入（§1/§3.3，非 pooled，**不改 `AddDbContext` 注册**）。
- **多租户**：`Sys_FieldAuditLog : BaseTenantEntity` 自动纳入全局过滤；审计行 TenantId 由 §3.4 step5 `ResolveAuditTenant` 显式设（镜像业务实体 / 共享表回退 CurrentTenantId）。
- **配置**：MVP 无需配置项（opt-in 即开关）。保留期清理留后续（不做）。

## §6 i18n（五语）+ 前端

### §6.1 i18n seed `I18nSecAuditScreenSeed`（仿 `I18nSecScreenSeed`，`public static readonly Sys_Lang[] Items`，接 Program.cs `.Concat`）
- 画面词条 `LangKey`：`sec.audit.{title, entityName, entityKey, operation, op.added, op.modified, op.deleted, operator, changedAt, changeCount, field, oldValue, newValue, viewTimeline, timelineTitle, noChanges, filterEntity, filterUser, filterFrom, filterTo}` 等，五语 ZhCN/ZhTW/En/Ja/Ko。
- （无错误码，无新增 `sec.event.*`。）

### §6.2 前端
- `views/pms/FieldAuditView.vue`（系统设置内）：列表（实体/主键/操作/操作人/时间/**变更字段数 `changeCount`**）+ 筛选（实体名/操作人/日期）；行点开"时间线"抽屉 → 调 `record` 端点（返完整 `changes`），按时间正序展示每次变更的 `[{field, old→new}]`（回放）。
- `api/sys/fieldAudit.ts`（`getList`/`getRecordTimeline`，仿 securityLog.ts）+ `types/sys/fieldAudit.ts`（`Operation` 枚举、列表项、`Change` 项）。
- router `viewModules` 加 `'/sys/field-audit': () => import('@/views/pms/FieldAuditView.vue')`。

## §7 测试策略

### §7.1 单测（各 Task，InMemory + 直构 `CP6Context` 注入假 `ICurrentUserAccessor`）
- **捕获正确性**：标记实体 Modified（先查后改）→ 一行 `Sys_FieldAuditLog`，`Operation=2`，`Changes` 仅含真实变更字段且 old/new 正确；Added → `Operation=1` + 初值（EntityKey=落定真键）；Deleted → `Operation=3` + 旧值。
- **opt-in 边界**：未实现 `IAuditable` 的实体变更 → 无审计行。
- **R2 回归**：`RoleController.Update`/`MenuController.Update` 改造后，改 `Sys_Role.RoleName`/`Sys_Menu.MenuName` → 产生 `Operation=2` 审计行且 diff 含该字段（防回退到 attach-as-Modified）。
- **密钥护栏**：改 `Sys_User.Password` → `Changes` **不含**该字段（`[AuditIgnore]` 与拒名单各一测，含一例"故意不标 `[AuditIgnore]`、靠拒名单兜底"的字段）。
- **跳过集**：仅改 `Modifier/ModifyDate` → 无审计行（空改）；`Id/TenantId` 不入 diff。
- **用户归属**：注入有用户 → `UserId/UserName` 落；注入 null（后台）→ null。
- **键落定**：Added 实体审计行 `EntityKey` == 落定后的真 `Id`（InMemory 验两阶段在同一 `SaveChanges` 周期内完成）。
- **审计行 TenantId（R4）**：双租户 InMemory，`BaseTenantEntity` 业务实体的审计行 TenantId==该实体 TenantId；查询端点只见本租户审计行。
- `BuildChanges`/拒名单/`ResolveAuditTenant` 纯函数单测（值截断、null 处理、Invariant 格式、共享表回退）。
- 控制器（仿 `SecurityLogControllerTests`，**直 `new FieldAuditController(db, fakeUser?)` 调方法**）：筛选 + 分页 clamp + 列表返 `{total,rows}` 且 `rows` 含 `changeCount` 不含完整 changes + `record` 时间线正序返完整 changes（反射读匿名）。**`[RequirePermission]` 不在单测覆盖**（直调绕过过滤器）——移交 §7.2 gstack/集成（评审 R5）。
- **覆盖局限（评审 R6）**：InMemory 无事务，仅能验"键落定 + 审计行随业务行同周期写入"，**真正的业务+审计同生同死回滚仅 relational 可验**（不在单测；列 §8 可选集成）。

### §7.2 gstack QA（T-QA）
起后端 + 前端：在用户管理改某用户邮箱/角色 → 字段审计页见该变更（旧→新）；角色/菜单管理改名 → 见审计行（验 R2 改造）；点时间线回放多次变更；改密码 → 审计行不含密码值；**无权用户访问 → 403（验 RequirePermission）**；多租户隔离目检。

## §8 已知局限 / Hardening（本 spec 记录，**不做**；后续增量）
**本期已知局限（文档化接受）：**
- **attach-as-Modified 通用局限**：经 `RepositoryBase.UpdateAsync`（断连）更新的实体若纳入 opt-in，Modified diff 落空（仅 Added/Deleted 留痕）。当前首批无此缺口（首批 Modified 路径均先查后改）。
- **DB 级级联删除**：`ON DELETE CASCADE` 删除的子行不入 `ChangeTracker` → 不被审计（仅 EF 级级联/显式删除可审计）。
- **原子回滚不可单测**：见 §7.1（InMemory 限制）。
- **共享表审计租户归属**：`Sys_Tenant` 等共享表审计行落操作者当前租户上下文（§3.4 R4）。

**Hardening（后续）：**
- 防篡改：审计行哈希链（前行 hash 链入）/ WORM 存储 / 只追加权限（DB 级 deny update/delete）。
- 高频异步卸载（写入队列/旁路），应对未来扩大 opt-in 范围后的写放大。
- 保留期策略自动清理（合规留存窗 + 归档）。
- 审计值级 PII 加密 / 脱敏策略（按字段分级）。
- 断连（attach-as-Modified）更新路径的原值补偿（如审计前强制 reload）/ `RepositoryBase` 先查后改化。
- relational 原子回滚集成冒烟（Sqlite/真库）。

## §9 定稿评审修订（2026-06-23，7 点；锚点经只读 subagent 核验实代码）

| # | 发现（证据） | 修订 |
|---|------|------|
| **R1** | 首批 opt-in 含 `Sys_TenantSsoConfig`(#3) — **NOT FOUND**；`[AuditIgnore]` 目标 `Sys_User.TwoFactorSecret`(#2)、`Sys_TenantSsoConfig.ClientSecretProtected`(#3) — **均不存在**（#2/#3 仅 spec 未落码） | 首批收敛为现存 11 实体（§2.2）；`[AuditIgnore]` 首批仅 `Sys_User.Password`，#2/#3 字段列入"落地后补标"；显式声明 **#4 不硬依赖 #2/#3**——拒名单按"以 `Secret` 结尾"/显式列举已天然覆盖未来字段（§3.1 双保险） |
| **R2** | `Sys_Role`/`Sys_Menu` 在首批，但 `RoleController.Update`(L64–71)/`MenuController.Update`(L44–51) 用 attach-as-Modified → diff 全落空（这两实体 Modified 不留痕）。`RepositoryBase.UpdateAsync`(L62–68) 同款且广泛复用 | **（用户定夺：选 A）** §0/§3.5 将这两控制器改"先查后改"（仿 UserController，2 文件、Sys 表低风险）纳入 #4；`RepositoryBase` 不动（blast radius 大）→ §8 记录通用局限；§7.1 加 R2 回归测试 |
| **R3** | `[RequirePermission("field-audit","query")]` 与 MenuKey 派生不符——范本 RoutePath `/sys/security-log`→MenuKey `sys-security-log`→`RequirePermission("sys-security-log",…)`；且 §5 漏 `Sys_RoleMenu` 授权 | §4 改 `[RequirePermission("sys-field-audit","query")]`；§5 补全 MenuId=115、`Sys_RoleMenu(RoleId=1,MenuId=115)`、MenuKey 回填、`Sys_MenuAction`/`Sys_RoleAction` |
| **R4** | 审计行 TenantId 归属不严谨（step6 不走 StampTenant；后台按租户循环时 CurrentTenantId≠业务 TenantId；`Sys_Tenant` 共享表无 TenantId） | §3.4 step5 定 `ResolveAuditTenant`：业务实体是 `BaseTenantEntity`→镜像其 TenantId；共享表回退 `CurrentTenantId`（文档化）。§7.1 加 TenantId 测试 |
| **R5** | §7.1 原称单测验 `[RequirePermission]` "无权 403"——但范本 `SecurityLogControllerTests` 直 `new` 控制器**绕过**过滤器，不测 403 | §7.1 删 403 单测主张，改为 `{total,rows}` 反射 + 分页 clamp + 时间线正序；403 移交 §7.2 gstack/集成 |
| **R6** | 原子回滚在 InMemory 不可验（无事务） | §7.1/§8 写明覆盖局限：键落定+同周期写入可验、真回滚仅 relational；列可选集成冒烟 |
| **R7** | 落码歧义点 | §3.3/§3.4 点名 `Database.IsRelational()` 守事务、DI 自动构造注入（非 pooled，不改 AddDbContext）、`DetectChanges` 依赖、`acceptAllChangesOnSuccess` 透传；§4/§6 列表端点投影 `changeCount` 摘要（详情才返完整 changes，防大文本负载）；§8 收 DB 级级联删除不入审计 |

**核验补强（非问题）**：`Id` 经核实为 `[DatabaseGenerated(Identity)]` 无客户端默认 → store-generated 属实，两阶段对 Added 不可省；`CP6Context` 当前无事务/无其它 SaveChanges 副作用 → 两阶段事务为安全新增；`OnModelCreating` 反射批量确认 → 新表自动纳入过滤/索引/盖章；密钥拒名单经全库扫描确认覆盖充分。

---

*生成于 2026-06-22；定稿 2026-06-23（§9 七点评审修订，全部 §1 锚点经只读 subagent 核验实代码）。源 brainstorming 2 决策（标记式 opt-in 高价值实体 + 字段 ignore · 每次变更一行+JSON 字段差异）+ 原子落库（用户确认）。底座 #1（已落码）；#4 自洽不硬依赖 #2/#3。实施 = subagent-driven，每 Task spec 审+质量审双过 + 先绿后本地 commit（不 push）+ gstack QA。下一步 = 据本定稿撰写实现计划（T1~Tn）。*
