# Task P0-T1: DataProtection 密钥环持久化到数据库

（提取自 docs/superpowers/plans/2026-07-07-p0-platform-hardening.md）

**Goal（包级）:** 修掉平台级生产隐患：DataProtection 密钥不持久（SSO/2FA/CSRF 重启失效）。用户拍板（2026-07-07）：密钥环存数据库（EF）。

## Global Constraints

- 基线不许跌：后端 `dotnet test` 全绿（当前 1565+）；每 commit 立即 push。
- 迁移命令：`dotnet ef migrations add <Name> --project CP6.Core --startup-project CP6.WebApi`；迁移文件必须只含预期变更，多出=模型漂移停下排查。
- 本包完成后 WFS 深化 engine-infra 的硬前置 D-T0 即满足。

## Files

- Modify: `CP6.WebApi/CP6.WebApi.csproj`（或 CP6.Core.csproj，包加在 DbContext 所在项目）
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`
- Modify: `CP6.WebApi/Program.cs:518` 附近
- Test: `CP6.Tests/Platform/DataProtectionPersistenceTests.cs`（**已存在于工作树，上会话按 TDD 先写好，未提交**）

## Steps

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
