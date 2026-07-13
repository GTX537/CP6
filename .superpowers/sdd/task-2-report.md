# Task 2 报告：LocationPublishService.BuildItemAsync 批量化

**分支** feat/space-wave5 · **提交** 52486c7（已 push，与 origin 同步）

（注：本文件此前残留一份无关任务报告，已整体覆盖为本次 Task 2 报告。）

## 实现

将发布链路径解析从「事务内逐库位连查」重构为「一次预载 + 纯内存构建」。

### 新增结构
- `private sealed class PublishLookup`：Racks/Aisles/Zones/Floors/Sites 五个 `Dictionary<Guid, T>`（init-only）。
- `private async Task<PublishLookup> LoadLookupAsync(IReadOnlyCollection<Space_Location> locs, CancellationToken ct)`
  依赖链顺序加载，五张表各**一次** `Where(ids.Contains)`：
  1. Rack ← locs 的 RackId 集合
  2. Aisle ← racks 的 AisleId 集合
  3. Zone ← racks 的 ZoneId 集合
  4. Floor ← **zones 的 FloorId ∪ locs 的 FloorId**（并集：Path 链走 zone.FloorId，WarehouseCd 回退链走 l.FloorId 冗余列，两链共用同一 Floors/Sites 字典）
  5. Site ← floors 的 SiteId 集合
  每级 ids 空集直接跳过该查询。查询次数恒为常数 5，与库位数 N 无关。

### 改签名
- `BuildItemAsync(l, op)` → `static LocationPublishItem BuildItem(Space_Location l, string op, PublishLookup lk)`（同步，纯查字典）。
- `ResolveWarehouseCdAsync(l)` → `static string? ResolveWarehouseCd(Space_Location l, PublishLookup lk)`；E-SPACE-405 长度守卫、`Site.WarehouseCd ?? SiteCode` 回退、无楼层归属返回 null 全部原样保留。

### 三调用方接线
- `PublishFloorAsync`：批构造后、foreach 前 `var lk = await LoadLookupAsync(locs, default)`；循环内 `BuildItem(l, "UPSERT", lk)`。
- `DeactivateAsync`：单库位也走统一预载——lookup 在①前置校验前加载一次，供 `ResolveWarehouseCd`（stock 预检）与后续 `BuildItem(l, "DEACTIVATE", lk)` 共用（期间 FloorId/RackId 不变）。
- `RepublishAsync`：foreach 前加载 `lk`，循环内 `BuildItem(l, "UPSERT", lk)`。

## 重构前后查询次数

| 路径 | 旧（事务内 / N 库位） | 新 |
|---|---|---|
| BuildItem 路径链 | 每库位最多 5 查（Rack/Aisle/Zone/Floor/Site）= 5×N | 预载 5 表各 1 = 5（含 WarehouseCd 链） |
| ResolveWarehouseCd | 每库位 2 查（Floor/Site）= 2×N | 并入上面的 5，0 额外 |
| **合计** | **约 7×N** | **常数 5** |

Deactivate（N=1）：旧 2（预检）+ 最多 5（BuildItem）= 最多 7 → 新 5。

## 行为等价性

`BuildItem` 与旧 `BuildItemAsync` 逐字段等价，缺挂分支语义对齐：
- aisle 缺失：旧 `aisle?.AisleCode`（null）↔ 新 TryGetValue 失败跳过（保持 null）。
- site 缺失：旧 `site?.SiteCode`（null）↔ 新 TryGetValue 失败跳过（保持 null）。
- zone/floor/rack 为 null 各级短路一致。
- E-SPACE-405 抛出时机仍在循环内、`SaveChangesAsync` 之前——fail-fast 无孤儿性质不变。

## 测试结果

- 基线（重构前）：LocationPublishServiceTests 19 passed；全量 1811 passed / 5 skipped。
- 新增 1 用例 `Publish_MixedMounting_FullFiveLevelAndFloorOnly_PathAndWarehouseCd_Equivalent`：
  同层一次发布 2 库位——①满五级挂载（Site→Floor→Zone→**Aisle**→Rack，覆盖既有测试从未验的巷道支路 + 坐标 + WarehouseCd 回退）②只挂楼层（RackId=null，五级路径全 null/FloorLevel=0，WarehouseCd 仍走 l.FloorId→Site 回退到 "WH1"）。
- 重构后：LocationPublishServiceTests 20 passed；**全量 1812 passed / 5 skipped**（≥ 基线 1811/5）。
- 既有 20 断言零改动（等价性护栏）。5 skipped 为 SpaceSqlIntegrationTests（需 SQL Server，本环境一贯跳过，与本任务无关）。

## 改动文件
- `CP6.Core/Services/Space/LocationPublishService.cs`（PublishLookup + LoadLookupAsync + BuildItem/ResolveWarehouseCd 纯函数化 + 三调用方接线）
- `CP6.Tests/LocationPublishServiceTests.cs`（新增 1 等价用例）

## 自审
- 五表恰好各一次查询：已核，每级 ids 空集短路，无 N 相关往返残留。
- Floors/Sites 并集覆盖 WarehouseCd 链（l.FloorId）：已核，避免只挂楼层库位丢 WarehouseCd。
- ToDictionary 无重复键风险：ids 均 Distinct，返回行按 PK 唯一。
- InMemory 事务守卫、事件落库、SignalR 通知时序均未触碰。

## 疑虑
- 无阻断性疑虑。轻微：新查询在真库为 5 次串行往返（依赖链使然，无法并行），但相较旧 7×N 已是数量级改善，且发布批通常单事务内容量有限。
