# Task P0-T3: Sys_Role 租户化（拍板：租户级角色）

（提取自 docs/superpowers/plans/2026-07-07-p0-platform-hardening.md）

**Goal（包级）:** 修掉平台级生产隐患：Sys_Role 是全局实体，跨租户串号（任意租户可见/可改所有租户的角色）。用户拍板（2026-07-07）：Sys_Role 改租户级实体。

## Global Constraints

- 基线不许跌：后端 `dotnet test` 全绿（当前 1570）；每 commit 立即 push。
- 迁移命令：`dotnet ef migrations add SysRoleTenantize --project CP6.Core --startup-project CP6.WebApi`；迁移文件必须只含预期变更，多出=模型漂移停下排查。

## Files

- Modify: `CP6.Entity/DomainModels/Sys/Sys_Role.cs`（基类 → `BaseTenantEntity`，保留 `IAuditable`）
- Create: 迁移 `SysRoleTenantize`（加 TenantId 列 + 存量归户回填 SQL）
- Modify: 角色种子（预置角色改为逐租户播种，照 Space 波4 MenuAction/RoleAction 逐租户先例）
- Test: `CP6.Tests/Sys/RoleTenantIsolationTests.cs`（新建）

## 存量归户策略（迁移内 SQL，必须按此顺序）

- [ ] Step 1: 实体改基类，生成迁移；迁移 `Up()` 手工补数据段：
  1. 现有全部角色行 `TenantId = <DefaultTenant A1 的 Id>`（默认租户继承原行，RoleId 不变——A1 的 Sys_UserRole/Sys_RoleAction 引用零迁移）。
  2. 对 A1 之外每个租户 T：`INSERT` 复制一份角色行（新 Guid Id，TenantId=T），并建临时映射表 `(OldRoleId, TenantId) → NewRoleId`。
  3. `UPDATE` 租户 T 的 `Sys_UserRole`/`Sys_RoleAction` 行：RoleId 按映射表改指到 T 的角色副本。
  4. 校验段：任意租户的 UserRole/RoleAction 所指 RoleId 的 TenantId 必须等于该行 TenantId，不等则 `THROW`（迁移失败可回滚，不留半套）。
- [ ] Step 2: 唯一索引：角色名/编码若有全局唯一索引，确认基建自动升级为 `(TenantId, …)` 复合（`CP6Context.cs:2079` 反射机制，无需手写）；若该索引是手写的，删单列版。
- [ ] Step 3: 种子调整：预置角色（管理员/普通用户等）改为"新租户开通时复制默认集"，照 Space 波4 逐租户种子写法；启动种子对已存在租户幂等补种。
- [ ] Step 4: 测试：①A 租户上下文查角色只见 A 的（全局过滤生效）；②A 改角色名不影响 B（回填后两行独立）；③新建角色自动盖 TenantId（StampTenant）；④回填校验逻辑单测（映射完整性）。
- [ ] Step 5: 前端 PMS Role 页回归（无需改码——全局过滤对它透明），dev 冒烟一遍增删改查。
- [ ] Step 6: 全量测试绿 → commit + push（`fix(sys): Sys_Role 租户化——存量按租户归户复制,UserRole/RoleAction 重指,隔离测试`）。
