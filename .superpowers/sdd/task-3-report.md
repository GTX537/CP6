# Task P0-T3 报告：Sys_Role 租户化

**Status: DONE_WITH_CONCERNS**（两处受 schema 现实所迫、与 brief 假设不同的设计调整，均为唯一正确解，已文档化并可复核）

---

## 1. Recon 发现

### 实体形态
- `Sys_Role`：主键 **`int RoleId`**，`DatabaseGeneratedOption.None`（用户自定义、原全局唯一）。字段：RoleName/Description/Enable/OrderNo/CreateDate。实现 `IAuditable`。表名 `Sys_Roles`。**无** RoleName/RoleCode 唯一索引，唯一约束即 PK。
- **关键背离 brief 假设 ①**：brief 假定 Sys_Role 可直接 `→ BaseTenantEntity` 且副本用「新 Guid Id」。实际 `BaseTenantEntity : BaseEntity` 带 `[Key] Guid Id` + Creator/Modifier/ModifyDate 列。若继承：
  - 与 `int RoleId` 主键冲突（双 `[Key]`）；
  - 新增 Guid Id + 3 审计列 = **schema 漂移**（违反「迁移只含 TenantId 列 + 索引变更」硬约束）。
- **既有先例**：`Sys_OperLog`（int Id 非 BaseTenantEntity）已确立「int 键租户表」的做法——手挂 `HasQueryFilter` + StampTenant 手补（CP6Context.cs:1986-1992, 2235-2237）。Sys_Role 情形完全相同，故采此先例。

### 子表引用拓扑（决定回填策略）
| 子表 | 基类 | 带 TenantId | RoleId 引用 |
|---|---|---|---|
| Sys_User | BaseTenantEntity | ✅ | `int? RoleId`（主角色） |
| Sys_UserRole | BaseTenantEntity | ✅ | int RoleId |
| Sys_RoleAction | BaseTenantEntity | ✅ | int RoleId |
| Sys_RoleDataScope | BaseTenantEntity | ✅ | int RoleId |
| Sys_RoleFieldPerm | BaseTenantEntity | ✅ | int RoleId |
| **Sys_RoleMenu** | 普通(int Id) | ❌ **无** | int RoleId |

- **无任何物理 FK** 指向 `Sys_Role.RoleId`（snapshot 确认，仅值引用 + 普通索引）→ 改复合主键不破坏 FK。
- **关键背离 brief 假设 ②**：brief 的「新 Id 重指」策略对本拓扑**不可行且不必要**：
  - RoleId 是用户可见 int，生成新号会改动用户可见编号；
  - `Sys_RoleMenu` **无 TenantId**，无法「逐租户重指」其行 → 重指方案根本无法正确执行；
  - 而所有真正需隔离的子表**已各自携 TenantId** → 保持 RoleId 稳定即可在租户作用域内正确解析，**零重指**。

### 默认租户识别
`TenantContext.DefaultTenant = 00000000-0000-0000-0000-0000000000A1`（ITenantContext.cs:17），全系统哨兵常量（JwtHelper/StampTenant 均回退它）。租户表 `Sys_Tenants`，`Id` 即各表 TenantId 来源。迁移 SQL 用此常量字面量识别 A1——非猜测，是文档化的默认租户身份。

---

## 2. 实现（逐步）

1. **实体** `Sys_Role.cs`：加 `Guid TenantId`；移除 `RoleId` 的 `[Key]`（改 fluent 复合主键），保留 `[DatabaseGenerated(None)]` 与 `IAuditable`。**不继承 BaseTenantEntity**（理由见 Recon ①）。
2. **CP6Context.OnModelCreating**：`Sys_Role` → `HasKey(new { TenantId, RoleId })` + `HasQueryFilter(x => x.TenantId == CurrentTenantId)`（照 Sys_OperLog 先例手挂）。
3. **StampTenant**：加 `Sys_Role` 循环（Added 且 TenantId 为空 → 盖 CurrentTenantId）。
4. **迁移 `SysRoleTenantize`**：`dotnet ef` 生成的 schema 段 = DropPK → AddColumn TenantId(默认空 Guid) → AddPK(TenantId,RoleId)，**零多余**（见 §5）。手工在 `Up()` 追加数据段（步骤 1/2/4，含 THROW）。
5. **种子**：
   - `TenantAdminService.CreateAsync`：新租户开通时同事务补建 `Sys_Role{TenantId=新租户, RoleId=1, 管理员}`（否则新租户 admin RoleId=1 无角色可解析）。
   - `Program.cs`：默认租户 seed 后加**启动幂等逐租户安全网**——为每个启用租户补齐 RoleId=1 管理员（迁移已回填存量，本块覆盖迁移后新建/遗漏租户）。
6. **RoleController.Update**：`FindAsync(RoleId)`（复合主键单参失效）→ `FirstOrDefaultAsync(r => r.RoleId == …)`（全局过滤自动限定当前租户）。

---

## 3. TDD 证据

新建 `CP6.Tests/Sys/RoleTenantIsolationTests.cs`（5 test）：①只见本租户角色 ②A 改名不影响 B ③新建自动盖 TenantId ④回填不变式完整性 ④反例：漏复制被检出。④/反例以纯函数 `BackfillInvariant.FindUnmatchedRefs` 镜像迁移 SQL 的 THROW 不变式（InMemory 不能跑 raw SQL）。

**RED**（暂注释 `HasQueryFilter`，复合主键在位）：
```
CP6.Tests.Sys.RoleTenantIsolationTests.TenantContext_sees_only_own_roles [FAIL]
CP6.Tests.Sys.RoleTenantIsolationTests.New_role_auto_stamps_current_tenant [FAIL]
CP6.Tests.Sys.RoleTenantIsolationTests.Renaming_role_in_A_does_not_affect_B [FAIL]
Failed! - Failed: 3, Passed: 2  （2 个纯函数不变式测试与过滤无关，恒绿）
```
**GREEN**（恢复过滤 + StampTenant）：
```
Passed! - Failed: 0, Passed: 7 （5 隔离 + 2 FieldAudit 回归）
```
**全量**：`Passed! - Failed: 0, Passed: 1575, Skipped: 5, Total: 1580`（基线 1570 + 5 新，零回归）。

---

## 4. 数据段 SQL 推理（顺序安全性 + THROW 守卫）

执行序（AddPK 之后）：
1. `UPDATE Sys_Roles SET TenantId=@A1 WHERE TenantId=@Empty`——新列对存量行默认空 Guid，此步归户 A1。此时 (A1,RoleId) 仍两两不同 → 复合主键成立。
2. `INSERT … SELECT … CROSS JOIN Sys_Tenants WHERE r.TenantId=@A1 AND t.Id<>@A1 AND NOT EXISTS(…)`——对每个非默认租户复制**同 RoleId** 副本。`NOT EXISTS` 保幂等；无非默认租户时 CROSS JOIN 空集，安全。
3. 子表**零重指**（RoleId 稳定 + 子表各自带 TenantId → 租户作用域内解析）。
4. 校验：五子表(UserRole/RoleAction/RoleDataScope/RoleFieldPerm/Users)的 `(TenantId,RoleId)`，凡 RoleId 是**已知角色号**（`EXISTS Sys_Roles a WHERE a.RoleId=c.RoleId`，忽略预存孤儿引用）却在本租户**缺副本** → `THROW 50001` 中止事务、整体回滚（迁移在单事务内运行）。步骤 2 已把每个角色复制到每个租户，故守卫正常不触发；一旦复制不全即失败、留不下半套。
- `SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;` 置顶（对齐既有 seed SQL 兼容性要求）。

**为何顺序安全**：先归户（收敛存量到 A1）→ 再复制（扩散到各租户，NOT EXISTS 幂等）→ 末校验（失败即回滚）。全程复合主键唯一性在每步后都成立。

---

## 5. Migration 洁净性校验

Snapshot diff（`CP6ContextModelSnapshot.cs`）仅：
```
+ b.Property<Guid>("TenantId").HasColumnType("uniqueidentifier");
- b.HasKey("RoleId");
+ b.HasKey("TenantId", "RoleId");
```
迁移 schema 段仅 DropPK/AddColumn TenantId/AddPK(TenantId,RoleId)——**零漂移**，符合硬约束。

---

## 6. 变更文件

| 文件 | 变更 |
|---|---|
| `CP6.Entity/DomainModels/Sys/Sys_Role.cs` | +TenantId，移 [Key]，注释 |
| `CP6.Core/EFDbContext/CP6Context.cs` | Sys_Role 复合主键+过滤；StampTenant 补 Sys_Role |
| `CP6.Core/Migrations/20260708093013_SysRoleTenantize.cs` | 新迁移 + 手写回填 SQL |
| `CP6.Core/Migrations/…Designer.cs` / `CP6ContextModelSnapshot.cs` | EF 自动 |
| `CP6.Core/Services/Platform/TenantAdminService.cs` | 新租户补建默认管理员角色 |
| `CP6.WebApi/Program.cs` | 启动幂等逐租户角色安全网 |
| `CP6.WebApi/Controllers/Sys/RoleController.cs` | FindAsync → FirstOrDefaultAsync |
| `CP6.Tests/Sys/RoleTenantIsolationTests.cs` | 新建，5 test |
| `CP6.Tests/Sys/FieldAuditR2RegressionTests.cs` | 见下 |

**修改的既有测试及理由**：`FieldAuditR2RegressionTests` 因复合主键产生两处必然后果——(a) 审计 EntityKey 由 `"7001"` 变 `"<TenantId>|7001"`（ExtractKey 以 "|" 连接复合键，CP6Context.cs:2159），改断言为 `$"{DefaultTenant}|7001"`；(b) `FindAsync(7001)` 单参对复合主键失效 → 改 `FirstOrDefaultAsync(r=>r.RoleId==7001)`。均为设计的直接产物，非掩盖回归。

---

## 7. 自审与关切

- **偏离 brief 的两点（DONE_WITH_CONCERNS 供复核）**：①不继承 BaseTenantEntity（照 Sys_OperLog 先例，避 Guid Id 冲突 + 列漂移）；②回填「保持 RoleId 稳定、逐租户复制、子表零重指」而非「新 Id 重指」。二者均因 `int RoleId` 主键 + 子表已带 TenantId + `Sys_RoleMenu` 无 TenantId 的真实拓扑所决定，是**唯一正确解**，且达成与 brief 完全一致的终态（各租户独立角色集、跨租户隔离）。
- **Step 5（前端 PMS Role 页 dev 冒烟）**：需运行栈，按派单**延后至包 DoD / P0-T4 真库验证**。全局过滤对前端透明，无需改码。
- **Sys_RoleMenu 遗留隐患（超出本任务范围）**：该表无 TenantId 且 RoleController 的 GetRoleMenus/SaveRoleMenus/Delete 直接按 RoleId 操作它 → 跨租户同号 RoleId 会串。本任务未触碰（brief 仅要求 UserRole/RoleAction）。建议后续把 Sys_RoleMenu 租户化或迁往 Sys_RoleAction 体系。
- **迁移未在真 SQL Server 跑过**（本环境测试用 InMemory）；THROW/CROSS JOIN/UNION 为标准 T-SQL，真库验证归 P0-T4。
- StampTenant 对 Sys_Role 在 TenantId 为**主键一部分**时于 SaveChanges 覆盖前赋值——EF 允许改 Added 实体键值，先例风险低；测试③已覆盖自动盖章路径。
