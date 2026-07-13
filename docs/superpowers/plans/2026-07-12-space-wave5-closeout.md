# Space 3D 波5「收尾」Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 收掉 Space 波1-4 遗留的功能票池:对账漂移 job、发布链 N+1 批量化、主数据锚护栏、锚清理、E-SPACE-601 BizException 化、生命周期页错误呈现统一、编辑器属性面板(Zone/Marker/Aisle)、单格补码 UI、SQL 集成测试真库化。

**Architecture:** 后端沿既有范式(BackgroundService+TenantScopeRunner、BizException+I18nSpaceScreenSeed 五语、Service 层护栏);前端沿波2-3 范式(Element Plus+Cp* 组件、v-permission、http 拦截器统一报错)。零迁移(T_WmsBin/Space 表结构不动)。

**Tech Stack:** ASP.NET Core 8 / EF Core / xUnit;Vue 3.5 + Element Plus + Konva(编辑器)/ Vitest。

## Global Constraints

- **基线:后端 `dotnet test C:\CP6\CP6.Tests\CP6.Tests.csproj` 1808 绿;前端 `cd cp6.web && npm run test` 369 绿、`npm run type-check` 0 错(需 `NODE_OPTIONS=--max-old-space-size=8192`)。每任务收尾两侧不得低于基线。**
- 权限键**连字符**(space-code-rule),前端 v-permission 键格式 `资源:动作`(冒号)。
- 新错误码必须在 `CP6.WebApi\Seed\I18nSpaceScreenSeed.cs` 注册**五语**(ZhCN/ZhTW/En/Ja/Ko)词条;抛出用 `BizException(code[, httpStatus])`(namespace `CP6.WebApi.Localization`,文件在 CP6.Core)。
- **每个 commit 立即 push**(用户硬性纪律)。分支 `feat/space-wave5`。
- 零迁移:本波不得新增/修改任何 EF 迁移。
- 前端目录是 **`cp6.web`**(带点)。
- InMemory 单测新建 context 的既有先例照抄仓内测试(搜 `UseInMemoryDatabase` 现有用法)。

---

### Task 1: 对账漂移扫描 Worker(Space.Status=1 ∧ T_WmsBin.IsActive=false)

**Files:**
- Create: `CP6.Core\Services\Space\SpaceBinDriftScanner.cs`
- Create: `CP6.WebApi\BackgroundServices\SpaceBinReconciliationWorker.cs`
- Modify: `CP6.WebApi\Program.cs`(照 `:503` FinReconciliationWorker 注册处,紧邻加一行)
- Test: `CP6.Tests\Space\SpaceBinDriftScannerTests.cs`

**Interfaces:**
- Produces: `SpaceBinDriftScanner.ScanAsync(CP6Context db, CancellationToken ct)` → `Task<List<SpaceBinDrift>>`;`record SpaceBinDrift(Guid LocationId, string? LocationCode, long BinVersion)`。
- Worker 纯壳:照 `CP6.WebApi\BackgroundServices\FinReconciliationWorker.cs`(启动延迟 1min + 每 24h,`TenantScopeRunner.ForEachTenantAsync`,只读,漂移逐条 `LogError`,`ProcessOnceAsync` 公开可测)。

**要点:** 两表以**主键等值 join**(`WmsBin.Id == Space_Location.Id`,跨系统同一 GUID)。漂移=已发布库位(Status=1, IsDeleted=0)对应 bin 存在且 IsActive=false。**只读不自愈**(对账 job 语义,与 FinReconciliationWorker 一致)。

- [ ] **Step 1: 写失败测试**(InMemory context;三例:①Status=1+bin.IsActive=false→命中 ②Status=1+bin.IsActive=true→不命中 ③Status=2+bin.IsActive=false→不命中):

```csharp
[Fact]
public async Task Scan_PublishedLocationWithInactiveBin_Reported()
{
    using var db = TestDb.Create(); // 照仓内既有 InMemory 先例
    var id = Guid.NewGuid();
    db.Space_Locations.Add(new Space_Location { Id = id, Status = 1, LocationCode = "A-01-01" });
    db.WmsBins.Add(new WmsBin { Id = id, LocationCode = "A-01-01", WarehouseCd = "W1", IsActive = false });
    await db.SaveChangesAsync();

    var drifts = await SpaceBinDriftScanner.ScanAsync(db, default);

    Assert.Single(drifts);
    Assert.Equal(id, drifts[0].LocationId);
}
```

- [ ] **Step 2: 跑测试确认红**(`dotnet test --filter SpaceBinDriftScannerTests`)
- [ ] **Step 3: 最小实现**:

```csharp
public static class SpaceBinDriftScanner
{
    public record SpaceBinDrift(Guid LocationId, string? LocationCode, long BinVersion);

    public static async Task<List<SpaceBinDrift>> ScanAsync(CP6Context db, CancellationToken ct)
        => await db.Space_Locations
            .Where(l => l.Status == 1 && !l.IsDeleted)
            .Join(db.WmsBins.Where(b => !b.IsActive),
                  l => l.Id, b => b.Id,
                  (l, b) => new SpaceBinDrift(l.Id, l.LocationCode, b.Version))
            .ToListAsync(ct);
}
```

Worker(照 FinReconciliationWorker 全文逐字同构,把勾稽逻辑换成调 `ScanAsync` 后 `foreach (var d in drifts) _logger.LogError("[SpaceBinDrift] 已发布库位 {LocationId}({Code}) 对应 WMS bin 处于停用态(version={V})——发布/停用链路漂移,需人工核查", …)`),并在 Program.cs FinReconciliationWorker 注册行旁 `builder.Services.AddHostedService<SpaceBinReconciliationWorker>();`。

- [ ] **Step 4: 跑测试确认绿 + 全量后端绿**
- [ ] **Step 5: Commit + push**(`feat(space): 波5 对账漂移扫描worker(Status=1∧bin停用,只读告警)`)

---

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

### Task 3: WmsBinConsumer 批量化(每 item 3 查→批次 3 查)

**Files:**
- Modify: `CP6.Core\Services\Wms\WmsBinConsumer.cs`(`ConsumeAsync:27-118`)
- Test: 既有 WmsBinConsumer 测试全绿 + 新增混合批(UPSERT×2+DEACTIVATE×1)等价测试

**Interfaces:**
- Consumes: `LocationPublishBatch.Items`(不变)。
- 内部:循环前三次预载——①`var ids = batch.Items.Select(i => i.LocationId)…; var bins = await _db.WmsBins.Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id)`;②DEACTIVATE 项的库存合计一次 `GroupBy` 查询(照 `:69-71` 现有谓词,聚合成 `Dictionary<key, decimal>`);③anchor 查询(`:97-100` 谓词)按批次码集合一次载入。循环体改字典命中。

**要点:** 三分支语义(upsert/墓碑/锚碰撞 REJECTED)与幂等 Version 判据逐行为等价;DeadLetter 路径不动。

- [ ] **Step 1: 写混合批等价测试**(2 upsert+1 deactivate,断言 bin 状态/Version/REJECTED 结果与逐条跑一致)
- [ ] **Step 2: 基线绿确认**
- [ ] **Step 3: 重构为三次预载+内存判定**
- [ ] **Step 4: 全量后端绿**
- [ ] **Step 5: Commit + push**(`perf(space): 波5 WMS消费端批量化——每item 3查收敛为批次3查`)

---

### Task 4: UpdateSite 锚护栏(E-SPACE-406)

**Files:**
- Modify: `CP6.Core\Services\Space\SpaceMasterService.cs:60-74`(UpdateSiteAsync)
- Modify: `CP6.WebApi\Seed\I18nSpaceScreenSeed.cs`(发布/停用/删除段加 E-SPACE-406)
- Test: `CP6.Tests\Space\`(挨着既有 SpaceMasterService 测试)

**要点:** `SiteCode`/`WarehouseCd` 任一与库中值不同,且站点下(`Floor.SiteId==id` → `Location.FloorId∈floors`)存在 `Status==1 && !IsDeleted` 库位 → `throw new BizException("E-SPACE-406")`。词条五语,语义:「站点下存在已发布库位,不可修改站点编码/仓库码(WMS 锚)」/ ja:「公開済みロケーションが存在するため、サイトコード/倉庫コードは変更できません」(En/ZhTW/Ko 同义自拟)。**其余字段(SiteName/Address/坐标/Enable)不受限**。两锚字段未变时护栏不触发(护栏查询也不要跑,避免每次改名多两查)。

- [ ] **Step 1: 失败测试×3**(①改 SiteCode+有已发布库位→BizException E-SPACE-406 ②改 WarehouseCd 同 ③只改 SiteName+有已发布库位→成功)
- [ ] **Step 2: 红**
- [ ] **Step 3: 实现护栏+词条**
- [ ] **Step 4: 绿+全量绿**
- [ ] **Step 5: Commit + push**(`feat(space): 波5 UpdateSite锚护栏E-SPACE-406——已发布库位在时拒改SiteCode/WarehouseCd`)

---

### Task 5: SpaceLocateController 裸 BadRequest → BizException(E-SPACE-601/004)

**Files:**
- Modify: `CP6.WebApi\Controllers\Space\SpaceLocateController.cs:27,41`
- Test: 既有 SpaceLocate 测试(若断言了裸 400 信封需同步改断言为 BizException 语义)

**要点:** 两处 `return BadRequest(new { code=400, message="E-SPACE-xxx" })` 改 `throw new BizException("E-SPACE-601")` / `("E-SPACE-004")`,走 BizExceptionMiddleware 按 culture 翻译(词条已在 seed,零新增)。

- [ ] **Step 1: 失败/改写测试**(断言 message 不再是裸码——单测层面断言抛 BizException 且 code 正确)
- [ ] **Step 2: 红 → 实现 → 绿 → 全量绿**
- [ ] **Step 3: Commit + push**(`fix(space): 波5 E-SPACE-601/004 BizException化——定位端点统一走中间件翻译`)

---

### Task 6: 库位删除时清理 T_WmsBin 墓碑锚

**Files:**
- Modify: `CP6.Core\Services\Space\SpaceMasterService.cs`(`DeleteRackAsync:415-501`、`DeleteAisleAsync:260-334` 中删 `Space_Locations` 处;另 grep 全仓其余 `Space_Locations.Remove` 删除通道——含 SceneService 保存删除分支,一并接线)
- Test: `CP6.Tests\Space\`

**Interfaces:**
- Produces: `SpaceMasterService` 内 `private async Task RemoveTombstoneBinsAsync(List<Guid> locationIds)`:`_db.WmsBins.Where(b => locationIds.Contains(b.Id) && !b.IsActive)` → `RemoveRange`。**仅删 IsActive=false 的墓碑行**(活跃 bin 理论不可达——Status=1 不可删 E-SPACE-408;护栏兜底:命中活跃 bin 时不删并保留)。SceneService 若在别类,提取为 `internal static` 帮助或复制同构块并注释互指。

**要点:** 与库位删除**同一事务/同一 SaveChanges**;删除后同码再发布不再 REJECTED(锚释放)。顺带更新 `:495-499` 注释(「锚清理记后续票」→「波5 已清理」)。

- [ ] **Step 1: 失败测试×2**(①删 Status=2 库位(带 IsActive=false bin)→bin 行消失 ②删 Status=0 库位(无 bin)→不炸)
- [ ] **Step 2: 红 → 实现(所有删除通道接线)→ 绿 → 全量绿**
- [ ] **Step 3: 追加集成断言**:同码重发布不再碰撞(构造 删→再建同码→publish→bin 重建 IsActive=true)
- [ ] **Step 4: Commit + push**(`feat(space): 波5 库位删除清理T_WmsBin墓碑锚——同码再发布不再REJECTED`)

---

### Task 7: 生命周期页错误呈现统一(前端)

**Files:**
- Create: `cp6.web\src\i18n\tOr.ts`(回退助手)
- Modify: `cp6.web\src\views\space\lifecycle\SpacePublishView.vue`(47-54 统计卡区、181-183 watcher else、198-234 runPrecheck/onGenerate)
- Modify: `cp6.web\src\views\space\lifecycle\SpaceEventsView.vue`(108-110 showError)
- Test: `cp6.web\src\views\space\lifecycle\__tests__\`(挨着既有测试)

**要点(四项):**
1. `tOr(key, fallback?)` 助手:`te(key) ? t(key) : (fallback ?? key)`(vue-i18n `te` 判存;导出纯函数,组件内用 `useI18n` 组合)。
2. **duplicateGroups 明细展开**:统计卡下加 `<el-collapse>`(仅 `pc.duplicateGroups.length>0` 时渲染),逐组列出重复码与所在库位 id 列表(数据已在 precheck 响应,照后端 DTO 字段名)。
3. **precheckErrors 旁「去编码规则页」链接**:`<router-link :to="'/space/code-rule'">`(路由 `:180`),文案走 i18n key `space.publish.goCodeRule`(五语补 `cp6.web\src\i18n\locales\*` 的 space 命名空间,照波3 键组织)。
4. **onGenerate/watcher seq 守卫**:`onGenerate` 响应处理前比对 `pcSeq` 快照(照 `runPrecheck:198-212` 的守卫写法);watcher else 分支(181-183)清空 `precheck` 时同步 `pcSeq++`,防在途 precheck 回填复活。
5. EventsView `showError`:`row.lastError` 经 `tOr` 后展示(E-SPACE 码词条化,非码原样)。

- [ ] **Step 1: 失败测试**(tOr 三例:已注册 key→译文/未注册→fallback/未注册无 fallback→key;PublishView 挂载态:dupGroups>0 渲染 collapse、errors>0 渲染 router-link)
- [ ] **Step 2: 红 → 实现 → `npm run test` 绿 + `npm run type-check` 0 错**
- [ ] **Step 3: Commit + push**(`feat(space-web): 波5 生命周期页错误呈现统一(tOr/重复组展开/去规则页链接/生码seq守卫)`)

---

### Task 8: 编辑器属性面板(Zone 选中编辑 + Marker 编辑 + Aisle 一览)

**Files:**
- Create: `cp6.web\src\views\space\editor\panels\PropertiesPanel.vue`
- Modify: `cp6.web\src\views\space\editor\FloorEditor.vue`(右侧 aside `:639-648` 挂第三面板;选中态解析 `:59-63` 旁扩 selectedZone/selectedMarker)
- Modify(若 Zone 不可选中): `cp6.web\src\space-editor\interact\tools\SelectTool.ts`(允许点选 zone 图形,与 rack 同 selectionIds 语义)
- Test: `cp6.web\src\views\space\editor\__tests__\PropertiesPanel.spec.ts`

**Interfaces:**
- Consumes: 既有命令层——Zone 改名/改码走**新建** `EditZoneCmd`(照 `space-editor\command\commands\EditMarkerCmd.ts` 逐字同构:prev/next 快照 do/undo);Marker 编辑复用 `EditMarkerCmd`;Aisle 只读一览(方向/所属 Zone/命中库位数),**不做 Aisle 手绘**(生成模型是模板阵列副产物,波5 不动)。
- Produces: `PropertiesPanel` props `{ selection: SelectionInfo }`,内部三分支(zone/marker/rack);rack 分支只读展示尺寸+「反向建模」入口保持原工具栏不动。

**要点:** 面板变更全部走命令栈(undo/redo 生效);保存仍走既有场景保存(命令改 store,`spaceEditor.ts` deletes/updates 已有通道)。无选中时显示 Aisle 一览 tab。

- [ ] **Step 1: 失败测试**(EditZoneCmd do/undo 往返;PropertiesPanel 选中 zone 渲染名称输入、blur 后 store 值变)
- [ ] **Step 2: 红 → 实现 → vitest 绿 + type-check 0 错**
- [ ] **Step 3: Commit + push**(`feat(space-web): 波5 编辑器属性面板——Zone选中编辑(EditZoneCmd)/Marker编辑/Aisle一览`)

---

### Task 9: 单格补码 UI

**Files:**
- Modify: `cp6.web\src\views\space\editor\panels\BindCodesDialog.vue`(unplaced 列表行加「补码」按钮)
- Modify: `cp6.web\src\api\space\codeRule.ts`(`genSingle:93-97` 已封装,零改动,仅引用)
- Test: `cp6.web\src\views\space\editor\__tests__\`(挨着既有)

**要点:** 行内按钮 `v-permission="'space-code-rule:generate'"`,点击 `codeRuleApi.genSingle(row.id)` → 成功后行内展示新码+刷新 unplaced 列表;失败靠 http 拦截器(照三生命周期页 catch 静默范式)。加载态防连点。

- [ ] **Step 1: 失败测试**(mock genSingle,点击后调用一次且行更新)
- [ ] **Step 2: 红 → 实现 → vitest 绿 + type-check 0 错**
- [ ] **Step 3: Commit + push**(`feat(space-web): 波5 单格补码UI——BindCodesDialog行内gen-code接线`)

---

### Task 10: SpaceSqlIntegrationTests 真库化(环境变量门控)

**Files:**
- Modify: `CP6.Tests\SpaceSqlIntegrationTests.cs`(全文重写 4 测试)
- Create: `CP6.Tests\Infra\SqlServerFactAttribute.cs`

**Interfaces:**
- Produces: `SqlServerFactAttribute : FactAttribute`——ctor 中 `if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CP6_TEST_SQLSERVER"))) Skip = "设 CP6_TEST_SQLSERVER=<连接串> 以运行真库集成测试";`。

**要点:** 4 测试改 `[SqlServerFact]`,连接串来自环境变量,**每测试类一个唯一名临时库**(`CP6Test_{Guid:N}`),`EnsureCreated` 建 schema,`finally EnsureDeleted`。四测试按类头注释(`:7-17`)语义真实断言:①同非空码二插抛唯一索引冲突 ②双 NULL 码共存 ③两阶段换码(经 NULL 中转)成功 ④RowVersion 并发第二写抛 `DbUpdateConcurrencyException`。无环境变量时 Skip(CI 恒绿);本机验证用 `CP6_TEST_SQLSERVER="Server=localhost,1433;Database=master;User Id=sa;Password=<从 C:\CP6\.env 的 MSSQL_SA_PASSWORD 读>;TrustServerCertificate=True"`(实跑一次证明 4 绿,报告贴输出)。

- [ ] **Step 1: 写 SqlServerFactAttribute + 重写 4 测试**
- [ ] **Step 2: 无环境变量跑→4 Skip;设变量跑→4 绿(两种输出都贴报告)**
- [ ] **Step 3: 全量后端绿(默认路径 Skip,基线不降)**
- [ ] **Step 4: Commit + push**(`test(space): 波5 SQL集成测试真库化——CP6_TEST_SQLSERVER门控,过滤唯一索引/两阶段换码/RowVersion并发首获真覆盖`)

---

## 波终验收(主控执行,非任务)

1. fable 终审(whole-branch review)→ 修复 → Ready。
2. 合并 main(--no-ff)+ push,重建 cp6-api 镜像部署(宿主 `dotnet publish -o publish-docker` → 删 appsettings.Local/Development → `docker build` 薄 Dockerfile → `compose up -d cp6-api`),cp6-web 同步重建(本波有前端)。
3. 线上冒烟:①对账 worker 启动日志 ②UpdateSite 改锚 400/E-SPACE-406 ③locate 裸码端点 ja 译文 ④删停用位→T_WmsBin 墓碑行消失→同码再发布成功 ⑤rehome/H4 republish 真库冒烟(波1.5 遗留) ⑥浏览器视觉走查(900 段菜单/三生命周期页/编辑器新面板/Zone 弹窗 ghost 矩形)。
4. 台账+票据落档(平台票不动:CpFormDialog 二次 toast/403 信封中文/总纲 §16.3 错误码表同步)。
