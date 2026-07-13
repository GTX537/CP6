### Task 2: LocationPublishService.BuildItemAsync 批量化(事务内 7×N→常数次)

**Files:**
- Modify: `CP6.Core\Services\Space\LocationPublishService.cs`(`BuildItemAsync:245-297`、`ResolveWarehouseCdAsync:311-322`、三个调用方 `PublishFloorAsync:87-94` / `DeactivateAsync:154` / `RepublishAsync:193`)
- Test: 既有 LocationPublish 相关测试全绿 + `CP6.Tests` 内新增行为等价测试

**Interfaces:**
- Produces: `private sealed class PublishLookup { Dictionary<Guid,Space_Rack> Racks; Dictionary<Guid,Space_Aisle> Aisles; Dictionary<Guid,Space_Zone> Zones; Dictionary<Guid,Space_Floor> Floors; Dictionary<Guid,Space_Site> Sites; }` + `private async Task<PublishLookup> LoadLookupAsync(IReadOnlyCollection<Space_Location> locs, CancellationToken ct)`(按 locs 的 RackId/FloorId 集合五张表各**一次** `Where(x => ids.Contains(x.Id))` 载入)+ `BuildItemAsync(l, op)` 改签名为 `BuildItem(Space_Location l, string op, PublishLookup lk)`(同步,纯查字典)。

**要点:** 行为**逐字段等价**——`BuildItem` 产出的 `LocationPublishItem`(含 PathJson 五级路径、WarehouseCd 回退 `Site.WarehouseCd ?? SiteCode`)与旧实现一致;缺挂(rack/floor 为 null)分支语义保持。三个调用方先收集 locs → `LoadLookupAsync` 一次 → 循环内纯内存构建。

- [ ] **Step 1: 加行为等价测试**:同一楼层 2 库位(1 挂货架满五级、1 只挂楼层),断言 PathJson/WarehouseCd 与既有测试期望一致(若既有测试已覆盖此形态则引用其数据构造,不重复造)
- [ ] **Step 2: 跑既有 LocationPublish 全部测试确认基线绿**
- [ ] **Step 3: 重构**(LoadLookupAsync + BuildItem 纯函数化;删 ResolveWarehouseCdAsync 的逐库位查询,并入 lookup;`FirstOrDefaultAsync` 逐条查询全部消灭)
- [ ] **Step 4: 全量后端测试绿(≥基线)**
- [ ] **Step 5: Commit + push**(`perf(space): 波5 发布链BuildItem批量化——事务内7×N连查收敛为5表各一次预载`)

---

