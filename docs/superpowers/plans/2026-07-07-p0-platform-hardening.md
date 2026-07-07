# P0 平台硬化执行计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax. 编码代理=Opus 4.8（[[model-policy-coding]]）。

**Goal:** 修掉三个平台级生产隐患：DataProtection 密钥不持久（SSO/2FA/CSRF 重启失效）、JWT 过期配置双写、Sys_Role 全局实体跨租户串号。

**依据：** `cp6-global-audit-2026-07-07` 缺陷 T3/#3、T3/#7、T2/#5。**用户拍板（2026-07-07）：密钥环存数据库（EF）；Sys_Role 改租户级实体。**

## Global Constraints

- 基线不许跌：后端 `dotnet test` 全绿（当前 1565+）；每 commit 立即 push。
- 迁移命令：`dotnet ef migrations add <Name> --project CP6.Core --startup-project CP6.WebApi`；迁移文件必须只含预期变更，多出=模型漂移停下排查。
- 本包完成后 **WFS 深化 engine-infra 的硬前置 D-T0 即满足**——完成时在 `wfs-phase2-plans-2026-07` 记忆与 `2026-07-05-wfs-engine-infra.md` 计划头部标注"D-T0 已由 P0 完成"。

---

### Task P0-T1: DataProtection 密钥环持久化到数据库

**Files:**
- Modify: `CP6.WebApi/CP6.WebApi.csproj`（或 CP6.Core.csproj，包加在 DbContext 所在项目）
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`
- Modify: `CP6.WebApi/Program.cs:518` 附近
- Test: `CP6.Tests/Platform/DataProtectionPersistenceTests.cs`（新建）

- [ ] Step 1: 引包 `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`（版本对齐项目 .NET 8 系列）。
- [ ] Step 2: `CP6Context` 实现 `IDataProtectionKeyContext`：加 `public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }`（`using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;`）。**注意**：`DataProtectionKey` 非 BaseTenantEntity，确认 CP6Context 的反射租户过滤只扫 `BaseTenantEntity` 子类（`CP6Context.cs:2062` 一带）不会误伤它——若按基类过滤则天然安全，写一个断言测试。
- [ ] Step 3: 迁移 `DataProtectionKeys`（一张表三列：Id/FriendlyName/Xml）。
- [ ] Step 4: `Program.cs:518` 改为：

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<CP6Context>()
    .SetApplicationName("CP6");   // 多副本/重装后应用名一致才能解密旧密文
```

- [ ] Step 5: 测试：①启动后 `DataProtectionKeys` 表出现至少一行；②用 `IDataProtectionProvider.CreateProtector("test").Protect/Unprotect` 往返；③新建第二个 ServiceProvider（模拟重启，同一 DB）能解密第一个加密的密文。
- [ ] Step 6: **回归关键点**：SSO ClientSecret 既有密文是旧临时密钥加密的，切换后**解不开**——写一步运维说明进 commit message：部署后需在 SSO 配置页重存一次 ClientSecret（PMS SsoConfig 页）。
- [ ] Step 7: 全量测试绿 → commit + push（`fix(platform): DataProtection 密钥环持久化到 DB——SSO/2FA/CSRF 重启存活(兼 WFS D-T0)`）。

### Task P0-T2: JWT 过期配置双写清理

**Files:**
- Modify: `CP6.WebApi/appsettings.json:35`、`appsettings.Development.json`/`appsettings.Local.json` 同键
- Test: 无新增（配置删除）

- [ ] Step 1: `grep -rn "ExpireMinutes" CP6.WebApi CP6.Core CP6.Tests` 确认除 appsettings 外零代码引用（审计结论：AuthController.cs:76 用 Security.Token.AccessTokenMinutes）。若有引用，改为读 Security.Token 后再删。
- [ ] Step 2: 删除 `JWT.ExpireMinutes` 配置项（保留 JWT 节其余签名相关键）；在 `Security.Token` 节旁加一行注释指明"令牌时长唯一配置源"。
- [ ] Step 3: 全量测试绿 → commit + push。

### Task P0-T3: Sys_Role 租户化（拍板：租户级角色）

**Files:**
- Modify: `CP6.Entity/DomainModels/Sys/Sys_Role.cs`（基类 → `BaseTenantEntity`，保留 `IAuditable`）
- Create: 迁移 `SysRoleTenantize`（加 TenantId 列 + 存量归户回填 SQL）
- Modify: 角色种子（预置角色改为逐租户播种，照 Space 波4 MenuAction/RoleAction 逐租户先例）
- Test: `CP6.Tests/Sys/RoleTenantIsolationTests.cs`（新建）

**存量归户策略（迁移内 SQL，必须按此顺序）：**

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

### Task P0-T4: DoD

- [ ] `dotnet test` 全绿；前端 type-check/vitest 不涉及（纯后端包）。
- [ ] 真库验证：重启 cp6-api 容器两次，SSO 配置页密文可解、2FA 挑战可过（第一次重启后按 T1 Step 6 重存 ClientSecret）。
- [ ] 记忆回写：`wfs-phase2-plans-2026-07`（D-T0 已满足）+ `cp6-global-audit-2026-07-07`（T2/#5、T3/#3、T3/#7 关闭）。
