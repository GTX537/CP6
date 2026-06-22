# 字段级审计回放设计 spec — S 类安全合规 #4

> 源：brainstorming 共识（2026-06-22）。底座 = #1 认证加固 + #2 2FA + #3 SSO。本子项目在 `CP6Context.SaveChanges` 写入管道叠加**字段级 before/after 变更史**（who/when/what 全要素），补足现有"请求级操作日志（`Sys_OperLog`）+ 行级审计元字段（`BaseEntity.Creator/Modifier`）"缺失的**数据变更前后值留痕 + 时间线回放**。命名空间 **Sys**，多租户 `BaseTenantEntity`。

## §0 范围

**做（MVP）**：
- **标记式 opt-in 字段级审计**：实体实现空标记接口 `IAuditable` 才纳入审计；默认不审计。聚焦高价值/敏感实体（用户/角色/权限授权/会计科目/价表/客商主数据/SSO·2FA 配置等），避开海量事务行（库存流水/受注明细已有不可变日志，不重复）。
- **字段级 before/after 捕获**：在 `CP6Context` 写入管道（与 `StampTenant` 同处）遍历 `ChangeTracker`，对标记实体的 Added/Modified/Deleted 捕获每个变更字段的旧值→新值。
- **密钥绝不入审计**：`[AuditIgnore]` 属性级排除 + 内建密钥拒名单（双保险）。
- **原子落库**：审计行与业务变更在同一逻辑事务持久化（§3.4，事务包裹两阶段，relational 生产原子；InMemory 降级）。
- **当前用户归属**：捕获 userId/userName（注入同步 `ICurrentUserAccessor`，读 JWT claims）；后台/系统写入记 null。
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

## §1 现状锚点（落码前复核，行号可能微移）

- **写入管道** `CP6.Core/EFDbContext/CP6Context.cs`：私有 `StampTenant()`（遍历 `ChangeTracker.Entries<BaseTenantEntity>()`，Added 且 `TenantId==Guid.Empty` 时盖 `CurrentTenantId`）；`SaveChanges(bool acceptAllChangesOnSuccess)` 与 `SaveChangesAsync(bool, CancellationToken)` 两个重写各调 `StampTenant()` 后转 `base`。无参 `SaveChanges()`/`SaveChangesAsync()` 经 base 路由至 bool 重载（不重复盖章）。**字段级审计捕获挂这两个重写**（§3）。
- **租户上下文** `CP6Context` 构造 `(DbContextOptions<CP6Context> options, ITenantContext? tenant = null)`；`CurrentTenantId => _tenant?.CurrentTenantId ?? TenantContext.DefaultTenant`。**本子项目加可选第三参** `ICurrentUserAccessor? user = null`（向后兼容现有单/双参构造的全部测试）。
- **实体基类** `CP6.Entity/BaseEntity.cs`：`Id` 为 `Guid` 且标 `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]`（**store-generated**：Added 实体的 `Id` 在 `base.SaveChanges` 前为临时值，存后才落定真值 → 审计 EntityKey 须**存后**取，见 §3.4）。审计元字段：`Creator`/`CreateDate`/`Modifier`/`ModifyDate`（行级 who/when 基础，本子项目补字段级前后值）。`BaseTenantEntity : BaseEntity` 加 `Guid TenantId`。
- **更新模式** `CP6.Core/Services/Pub/BaseCrudService.cs`：`UpdateAsync` = `FindAsync(incoming.Id)` 载入原实体 → `Db.Entry(original).CurrentValues.SetValues(incoming)`（**tracked 先查后改**，`OriginalValues` 完整保留 → ChangeTracker diff 准确）。Sys 控制器（UserController 等）同款先查后改。**已知约束**：attach-as-Modified 的断连更新（不先查）会令 `OriginalValues==CurrentValues`、diff 落空——审计仅对 tracked 更新准确（spec §8 记录）。
- **请求级日志** `CP6.WebApi/Filters/OperLogFilter.cs` + `Sys_OperLog`（int Id，手注册租户全局过滤）：记 API 调用（控制器/动作/参数/耗时），**非**字段级数据变更——与本子项目互补。`/api/auth` 被 OperLogFilter 跳过。
- **安全事件审计** `ISecurityAuditService`/`Sys_SecurityLog`（#1）：记登录成败/锁定/改密等**事件**，"失败不阻断"。与本子项目（数据变更）互补，不复用。
- **当前用户** 业务侧 `(await _perm.GetAsync()).UserId`（`ICurrentPermissionContext`，async）；写入管道需**同步**取用户 → 新建轻量 `ICurrentUserAccessor`（读 `IHttpContextAccessor` 的 JWT claims：NameIdentifier→userId、Name→userName），仿 `_tenant` 注入。
- **多租户** `BaseTenantEntity` 经 `OnModelCreating` 反射批量注册全局查询过滤 + 唯一索引自动补 `TenantId` 前缀；`StampTenant` 写入盖章。`Sys_FieldAuditLog` 继承 `BaseTenantEntity` 即自动纳入。
- **授权** `[RequirePermission(menuKey,action)]`（∈`CP6.Core.Auth`）；权限点 seed `Sys_MenuAction`+`Sys_RoleAction` 授 RoleId=1（Program.cs 仿 Sys 授权加固块）。
- **i18n seed** 仿 `I18nSecScreenSeed`（`.Items` 接 Program.cs `.Concat` 链），五语；菜单名直接中文。
- **前端** `cp6.web/src`：`api/http.ts`、`views/OperLogView.vue`（列表+筛选范本）、`router/index.ts`、`api/sys/*`。

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
- **首批 opt-in 实体**（实现 `IAuditable`）：`Sys_User`、`Sys_Role`、`Sys_UserRole`、`Sys_RoleAction`、`Sys_RoleDataScope`、`Sys_RoleFieldPerm`、`Sys_Menu`、`Sys_Tenant`、`GlAccount`、`SupplierPrice`、`BusinessPartner`、`Sys_TenantSsoConfig`（#3）。（后续按需加标，无需改捕获逻辑。）
- **`[AuditIgnore]` 标注**：`Sys_User.Password`、`Sys_User.TwoFactorSecret`（#2）、`Sys_TenantSsoConfig.ClientSecretProtected`（#3）等。

### §2.3 EF 迁移
`dotnet ef migrations add FieldAudit --project CP6.Core --startup-project CP6.WebApi`（新表 `Sys_FieldAuditLogs`；无其它表/列变更——`IAuditable`/`[AuditIgnore]` 不映射列）。

## §3 捕获机制（核心）

### §3.1 内建密钥拒名单（硬墙，与 `[AuditIgnore]` 双保险）
捕获时对字段名做大小写不敏感匹配，命中即跳过（即使实体漏标 `[AuditIgnore]`）：
`Password`、以 `Secret` 结尾、以 `Hash` 结尾、`TokenHash`、`Salt`、`ClientSecretProtected`、`TwoFactorSecret`。

### §3.2 跳过字段集
捕获时统一跳过：主键 `Id`、`TenantId`、行级元字段 `Creator/CreateDate/Modifier/ModifyDate`（已有行级审计，避免噪音）、`[AuditIgnore]` 字段、拒名单字段、导航属性（仅标量列）。

### §3.3 `ICurrentUserAccessor`（`CP6.Core/Services/Sys/`）
```csharp
public interface ICurrentUserAccessor { Guid? UserId { get; } string? UserName { get; } }
```
实现读 `IHttpContextAccessor.HttpContext?.User`（ClaimTypes.NameIdentifier→Guid?、ClaimTypes.Name→string?）；无 HttpContext（后台 Worker/测试）→ 均 null。DI `AddHttpContextAccessor()`（若未注册）+ `AddScoped<ICurrentUserAccessor, CurrentUserAccessor>()`。`CP6Context` 构造注入可选 `ICurrentUserAccessor? user = null`。

### §3.4 捕获 + 原子落库（`CP6Context`）

**两阶段（兑现"原子"目标，因 `Id` store-generated，Added 键须存后取）：**

```
private List<PendingAudit> CaptureFieldAuditBeforeSave()
{
    var list = new List<PendingAudit>();
    foreach (var e in ChangeTracker.Entries<IAuditable>())
    {
        if (e.State is not (Added or Modified or Deleted)) continue;
        var changes = BuildChanges(e);          // 跳过集 + 拒名单；Modified 仅 IsModified && !Equals(orig,cur)
        if (e.State == Modified && changes.Count == 0) continue;   // 空改不记
        list.Add(new PendingAudit(e /*引用*/, MapOp(e.State), changes));
    }
    return list;   // 此刻不读 Added 的 Id（临时值）
}
```
1. `SaveChanges`/`SaveChangesAsync` 重写内：`StampTenant()` → `var pending = CaptureFieldAuditBeforeSave()`。
2. 若 `pending` 空 → 直接 `base.SaveChanges`（零开销）。
3. 否则：**relational** 时 `if (Database.CurrentTransaction == null) using var tx = Database.BeginTransaction()`（无环境事务才自开；有则参与）；**InMemory** 跳过事务。
4. `var result = base.SaveChanges(...)`（业务变更落库，Added 的 `Id` 落定真值）。
5. 对每个 `pending`：读 `entry.Entity.Id`（现为真值）→ 建 `Sys_FieldAuditLog{ EntityName=ClrType.Name, EntityKey=Id, Operation, Changes=JsonSerialize, UserId=_user?.UserId, UserName=_user?.UserName, ChangedAt=DateTime.Now }` → `Sys_FieldAuditLogs.Add`。
6. `base.SaveChanges(...)`（审计行落库）。relational：`tx.Commit()`。返 step4 的 `result`。
   - **审计行自身不被审计**（`Sys_FieldAuditLog` 不实现 `IAuditable`）。
   - 异常（任一 base.SaveChanges 抛）：relational 事务回滚（业务+审计同生同死=原子）；记 `ILogger`。
   - **递归护栏**：step6 的 `base.SaveChanges` 入口不再触发新捕获（仅 `IAuditable` 实体捕获，审计行非 `IAuditable`；或加实例 `_inAuditWrite` 标志短路 step1）。
7. 同步重写 `SaveChanges(bool)` 与异步 `SaveChangesAsync(bool)` 各落一份（异步用 `BeginTransactionAsync`/`SaveChangesAsync`/`CommitAsync`）。

> **diff 值化**：旧/新值经 `Convert.ToString(value, InvariantCulture)`（null→null）；过长截断（如 1000 字符，防大文本撑爆 JSON）。`Changes` 经 `System.Text.Json` 序列化。

## §4 查询 / 回放端点（`CP6.WebApi/Controllers/Sys/FieldAuditController.cs`）

`[Authorize]`（类级）；读端点 `[RequirePermission("field-audit","query")]`。多租户全局过滤自动隔离本租户。
- `GET /api/sys/field-audit?entityName=&entityKey=&userId=&from=&to=&page=&pageSize=` → 分页列表（page≥1、pageSize clamp(1,200)，仿 SecurityLogController），按 `ChangedAt` 倒序。投影返 `{id,entityName,entityKey,operation,changes,userId,userName,changedAt}`。
- `GET /api/sys/field-audit/record?entityName=&entityKey=` → 该记录全部变更**时间线**（`ChangedAt` 正序，供回放 diff）。
- （只读，无写端点；无新错误码。）

## §5 授权 / DI / 多租户 / 配置

- **权限点 seed**（Program.cs，仿 Sys 授权加固块）：菜单 `field-audit`（路径 `/sys/field-audit`，ParentId=100，Icon=Document）；`Sys_MenuAction` `query`；`Sys_RoleAction` 授 RoleId=1；`MenuKey` 回填。
- **DI**：`AddHttpContextAccessor()`（幂等）+ `AddScoped<ICurrentUserAccessor,CurrentUserAccessor>()`；`CP6Context` 构造解析可选 `ICurrentUserAccessor`。
- **多租户**：`Sys_FieldAuditLog : BaseTenantEntity` 自动纳入全局过滤 + 写入盖章（捕获在 `StampTenant` 后，审计行 Added 时 `TenantId==Guid.Empty` → step6 前再经 `StampTenant`？注：step6 的 `base.SaveChanges` **不**重走我方 `StampTenant`（已在 step1 调过一次，针对业务实体）——故审计行须在 step5 **显式设 `TenantId=CurrentTenantId`**，或 step6 复用一次 `StampTenant()`。采用 step5 显式设 `TenantId`，避免二次遍历）。
- **配置**：MVP 无需配置项（opt-in 即开关）。保留期清理留后续（不做）。

## §6 i18n（五语）+ 前端

### §6.1 i18n seed `I18nSecAuditScreenSeed`（仿 `I18nSecScreenSeed`，接 Program.cs `.Concat`）
- 画面词条：`sec.audit.{title, entityName, entityKey, operation, op.added, op.modified, op.deleted, operator, changedAt, field, oldValue, newValue, viewTimeline, timelineTitle, noChanges, filterEntity, filterUser, filterFrom, filterTo}` 等，五语 ZhCN/ZhTW/En/Ja/Ko。
- （无错误码，无新增 `sec.event.*`。）

### §6.2 前端
- `views/pms/FieldAuditView.vue`（系统设置内）：列表（实体/主键/操作/操作人/时间）+ 筛选（实体名/操作人/日期）；`changes` 列渲染字段数摘要；行点开"时间线"抽屉 → 调 record 端点，按时间正序展示每次变更的 `[{field, old→new}]`（回放）。
- `api/sys/fieldAudit.ts`（`getList`/`getRecordTimeline`）+ `types/sys/fieldAudit.ts`（Operation 枚举、Change 项）。
- router `viewModules` 加 `/sys/field-audit`。

## §7 测试策略

### §7.1 单测（各 Task，InMemory + 直构 CP6Context 注入假 `ICurrentUserAccessor`）
- **捕获正确性**：标记实体 Modified（先查后改）→ 一行 `Sys_FieldAuditLog`，`Operation=2`，`Changes` 仅含真实变更字段且 old/new 正确；Added → `Operation=1` + 初值；Deleted → `Operation=3` + 旧值（EntityKey=真键）。
- **opt-in 边界**：未实现 `IAuditable` 的实体变更 → 无审计行。
- **密钥护栏**：改 `Sys_User.Password`/`TwoFactorSecret` → `Changes` **不含**该字段（`[AuditIgnore]` 与拒名单各一测）。
- **跳过集**：仅改 `Modifier/ModifyDate` → 无审计行（空改）；`Id/TenantId` 不入 diff。
- **用户归属**：注入有用户 → `UserId/UserName` 落；注入 null（后台）→ null。
- **原子/键落定**：Added 实体审计行 `EntityKey` == 落定后的真 `Id`（InMemory 验两阶段；审计行与业务行同 `SaveChanges` 调用周期内）。
- **多租户隔离**：双租户 InMemory，查询端点只见本租户审计行。
- `BuildChanges`/拒名单纯函数单测（值截断、null 处理、Invariant 格式）。
- 控制器：筛选 + 分页 clamp + record 时间线正序 + `[RequirePermission]` 强校验（admin 200 / 无权 403，反射读匿名返回，仿 SecurityLogControllerTests）。

### §7.2 gstack QA（T-QA）
起后端 + 前端：在用户管理改某用户邮箱/角色 → 字段审计页见该变更（旧→新）；点时间线回放多次变更；改密码 → 审计行不含密码值；无权用户访问 → 403；多租户隔离目检。

## §8 Hardening（本 spec 记录，**不做**；后续增量）
- 防篡改：审计行哈希链（前行 hash 链入）/ WORM 存储 / 只追加权限（DB 级 deny update/delete）。
- 高频异步卸载（写入队列/旁路），应对未来扩大 opt-in 范围后的写放大。
- 保留期策略自动清理（合规留存窗 + 归档）。
- 审计值级 PII 加密 / 脱敏策略（按字段分级）。
- 断连（attach-as-Modified）更新路径的原值补偿（如审计前强制 reload）。

---

*生成于 2026-06-22。源 brainstorming 2 决策（标记式 opt-in 高价值实体 + 字段 ignore · 每次变更一行+JSON 字段差异）+ 原子落库（用户确认）。落码按真实代码锚点（§1：StampTenant 挂点 / Id store-generated / BaseCrudService 先查后改）。底座 #1~#3。实施 = subagent-driven，每 Task spec 审+质量审双过 + 先绿后本地 commit（不 push）+ gstack QA。*
