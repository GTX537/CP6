# Task 1 报告：Space 波5 对账漂移扫描 Worker

**Status: DONE** — commit `e98021d`（分支 feat/space-wave5，已 push）

## 实现内容

只读对账扫描：Space 侧库位仍 `Status=1`（已发布、`IsDeleted=0`）而其 WMS 消费落点 `T_WmsBin` 已 `IsActive=false` 的漂移，逐条 LogError 告警，不自愈（与 FinReconciliationWorker 语义一致）。

改动/新增文件：
- **新增** `CP6.Core\Services\Space\SpaceBinDriftScanner.cs` — 静态扫描器。`ScanAsync(CP6Context db, CancellationToken ct)` → `Task<List<SpaceBinDrift>>`；嵌套 `record SpaceBinDrift(Guid LocationId, string? LocationCode, long BinVersion)`。两表主键等值 join（`Space_Location.Id == WmsBin.Id`，跨系统同一 GUID），过滤 `Status==1 && !IsDeleted` × `!IsActive`。租户过滤由 CP6Context 全局 query filter 施加。
- **新增** `CP6.WebApi\BackgroundServices\SpaceBinReconciliationWorker.cs` — 照 FinReconciliationWorker 逐字同构：启动延迟 1min + 每 24h，`ProcessOnceAsync` 公开可测，经 `TenantScopeRunner.ForEachTenantAsync` 逐租户从 scope 取 `CP6Context` 调 `ScanAsync`，漂移逐条 `LogError`（含 tenant/LocationId/Code/version），无漂移记 Info。
- **修改** `CP6.WebApi\Program.cs` :503 附近 — 在 FinReconciliationWorker 注册行旁加 `AddHostedService<SpaceBinReconciliationWorker>()`。
- **新增** `CP6.Tests\Space\SpaceBinDriftScannerTests.cs` — 3 例（InMemory）。

## 测试与结果

聚焦：`dotnet test --filter SpaceBinDriftScannerTests` → **Passed 3 / Failed 0**。
全量：`dotnet test CP6.Tests/CP6.Tests.csproj` → **1811 passed / 5 skipped / 0 failed**（基线 1808+3 新增 = 1811，达标）。

三例覆盖 brief 验收：
1. `Scan_PublishedLocationWithInactiveBin_Reported`：Status=1 + bin.IsActive=false → 命中（并断言 LocationId/LocationCode/BinVersion 三字段回传正确）。
2. `Scan_PublishedLocationWithActiveBin_NotReported`：Status=1 + bin.IsActive=true → 不命中。
3. `Scan_UnpublishedLocationWithInactiveBin_NotReported`：Status=2（停用）+ bin.IsActive=false → 不命中。

## TDD 证据

**RED**（scanner 未实现，编译失败）：
```
error CS0103: The name 'SpaceBinDriftScanner' does not exist in the current context (×3)
```

**GREEN**（实现后）：
```
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3 - CP6.Tests.dll
```

## 关键实现决策

- **InMemory context 构造**：照仓内先例 `FinReconciliationServiceTests.NewDb()`——单参 `new CP6Context(options)`。CP6Context 单参时 `CurrentTenantId` 落 `DefaultTenant`，实体 SaveChanges 盖章为同租户，查询 query filter 亦按该租户 → 命中成立，无需显式绕租户。
- **DbSet 名**：`Space_Locations` / `WmsBins`（已核对 CP6Context :427/:440）。
- **Space_Location 必填补齐**：实体 `LocationCode` 可空、`Status` int，BaseBizEntity 提供 Id/TenantId/IsDeleted，InMemory 无需补其他字段即可存。
- **BinVersion 用 `WmsBin.Version`**（已消费的最新发布版本，溯源用），与 brief 示例一致。
- Worker 从 scope 直接 `GetRequiredService<CP6Context>()`（scanner 是静态方法、无独立服务接口），符合 TenantScopeRunner「同 scope 内 ITenantContext 与 CP6Context 同一份」的租户作用域约定。

## 自审发现

- **完整性**：三链路（命中/两不命中）全覆盖，字段级断言到位。
- **YAGNI**：scanner 保持静态无状态，未引入 DI 接口/自愈逻辑（brief 明确只读不自愈）。
- **测试真验证行为**：命中例断言了 LocationId + LocationCode + BinVersion 全部三字段的回传值，而非仅 `Single`，确保 join 投影正确。
- **多租户正确性**：ScanAsync 不写 `.Where(TenantId==)`，依赖 CP6Context 全局过滤 + Worker 逐租户设 CurrentTenantId，与 FinReconciliationWorker 同构，无跨租户泄漏面。

## 疑虑

无阻塞疑虑。一点说明：本次 commit 一并纳入了 `.superpowers\sdd\task-1-brief.md`（此前未跟踪的 sdd 任务简报），符合仓内 sdd 台账入库惯例。Worker 的运行时逐租户告警未做线上实证（本任务范畴为扫描逻辑 + 注册；行为由单测覆盖，Worker 为逐字同构壳）。
