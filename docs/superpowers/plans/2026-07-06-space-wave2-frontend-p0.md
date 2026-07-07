# Space 波2：前端 P0 可用性（菜单种子 + 落地页 + Site/Floor 管理 + Zone 创建工具）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Space 3D 模块「可进入、可建模」：消灭三个孤儿路由（菜单 + 落地页）、补齐 Site/Floor 管理 UI（主数据不再只能 SQL 种子）、在编辑器内提供 Zone 创建工具（解「放货架必须先有 Zone、但无处建 Zone」的鸡蛋问题）。

**Architecture:** 基线 main=d9ff9d0（波1+1.5 已并入）。三个新管理/落地页走**菜单驱动动态路由**（viewModules 登记 + Sys_Menus 900 段种子），与既有 standalone 三页（editor/viewer/stacked）共存——落地页用 named-route push 带参跳入 standalone 全屏页。Zone 创建走**编辑器差量保存通路**（`scene.zones` push + markDirty，前端保存链路已就绪只缺入口）+ 命令栈（可撤销），不新增独立 API 调用。新页面遵循 Cp* 设计系统（CpPageShell/CpListPage/CpFormDialog/CpStatCard）与 `space.*` 点分 i18n key（词条经 MERGE Sys_Langs 种子落库，落 `_core` 命名空间启动即载）；编辑器内改动跟随 FloorEditor 现有风格（中文字面量 `t('…')`，flatJson 模式下合法）。

**Tech Stack:** Vue 3 `<script setup>` + TypeScript + Element Plus + Cp* 模板组件 + Konva（space-editor 引擎）+ vitest(jsdom)。后端仅 1 处小改（SiteDto 暴露 WarehouseCd）。

## Global Constraints

- **照抄源是硬模板**（探查已核实，实现前先全文读）：
  - CRUD 列表页 → `cp6.web/src/views/wms/WarehouseListView.vue`（CpPageShell + CpListPage + CpFormDialog + `listRef.reload()` 命令式刷新）
  - 落地页 → `cp6.web/src/views/wms/WmsDashboardView.vue`（CpStatCard 网格 + CpSectionHeader）
  - API 封装 → `cp6.web/src/api/wms/warehouse.ts` 风格 + `cp6.web/src/api/space/floor.ts` 的 `Envelope<T>` 包壳（`http.ts` 拦截器已剥 axios 层，业务数据取 `.data`）
  - 菜单种子 → `docs/seeds/wms-menu-seed.sql`（事务骨架/IF NOT EXISTS 幂等/Sys_RoleMenus 区间授权/回滚段）+ `docs/seeds/oa-designer-menu.sql`（**含 MenuKey 的显式列清单变体——本波必须用这个变体**，MenuKey 是横切权限的锚，波4 依赖）
  - i18n 词条种子 → `docs/seeds/wms-realtime-i18n-seed.sql`（临时表 + MERGE Sys_Langs 五语幂等）
  - 组件测试 → `cp6.web/src/components/templates/__tests__/CpListPage.spec.ts`（jsdom pragma / mount+flushPromises / vi.fn fetch）
- **MenuId 取 900–919 段**（2026-07-06 主控裁决：500 段被 OA 占用[500-504]，600/700 段被 Fin/OA-B 占用，900 段静态检索无占用；种子全部 IF NOT EXISTS 幂等，真库执行前跑 `SELECT MenuId FROM Sys_Menus WHERE MenuId BETWEEN 900 AND 919` 复核）。**每条 Sys_Menus 都填 MenuKey**（space-home / space-site / space-floor）。
- **路由三键必须逐字一致**：Sys_Menus.RoutePath == viewModules key == 页面预期路径（`/space/home`、`/space/site`、`/space/floor`，含前导 `/`）。跳 standalone 页用 named push：`router.push({ name: 'space-editor', params: { floorId } })` / `{ name: 'space-viewer', params: { siteId } }` / `{ name: 'space-stacked', params: { siteId } }`。
- **i18n**：新页面用 `space.*` 点分 key（`space` 不在 LAZY_NAMESPACES → 词条自动进 `_core`，量小无妨，不改 index.ts）；每个用到的 key 都要进 Task 6 的词条种子（五语：ZhCN/ZhTW/En/Ja/Ko）。编辑器内（FloorEditor/ZoneTool 弹窗）跟随该文件现有中文字面量 `t('…')` 风格——不在老文件里混两套。
- **前端命令**（全部在 `cp6.web/` 下）：type-check 必须带堆 `NODE_OPTIONS=--max-old-space-size=8192 npm run type-check`；测试 `npm run test`（vitest 基线以 Task 1 实测为准，此前约 320+）；构建 `npm run build`。后端改动（Task 1 的 SiteDto）用 `dotnet test CP6.Tests/CP6.Tests.csproj`（基线 1556 passed / 5 skipped）+ `dotnet build CP6.slnx`。
- **Zone 差量保存路径无服务端校验**（SceneService 不调 ValidatePolygon/唯一性）→ 前端必须自校验：zoneCode 非空且楼层内唯一（对照 `store.scene.zones`）、拖框矩形短边 ≥ 500mm（防零面积/误触，E-SPACE-006 语义前移）。
- polygon 格式：`[[x0,y0],[x1,y0],[x1,y1],[x0,y1]]` 四点 JSON 字符串，**不重复首点**（渲染层 `closed:true` 闭合），坐标 mm、floor 局部系。
- 提交 `feat(space):` / `docs(space):` + `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`，每 Task 一个 commit。
- 多租户/后端惯例照波1（SiteDto 改动不写 TenantId、映射在 Service 层）。

---

### Task 1: API 层与类型（site.ts 新建 / floor.ts 补全 / SiteDto 暴露 WarehouseCd）

**Files:**
- Modify: `CP6.Entity/DTOs/Space/SpaceMasterDtos.cs`（SiteDto 加 1 字段）
- Modify: `CP6.Core/Services/Space/SpaceMasterService.cs`（Site 的 Create/Update/List 映射补 WarehouseCd——先读现有 Site 段确认三个方法的映射写法后逐处补）
- Create: `cp6.web/src/api/space/site.ts`
- Modify: `cp6.web/src/api/space/floor.ts`（补 create/update/remove）
- Modify: `cp6.web/src/types/space/scene.ts`（加 `SiteVO`；`FloorVO` 若缺 CRUD 字段则补齐——以后端 FloorDto 为准）
- Test: `CP6.Tests/SpaceMasterServiceTests.cs`（1 个后端测试）

**Interfaces:**
- Consumes: 后端既有端点 `GET/POST/PUT/DELETE /api/space/site|floor`（SpaceMasterController:26-74）；`Envelope<T>` 类型（types/space/scene.ts:126）
- Produces:
  ```ts
  // api/space/site.ts
  export interface SiteVO { id?: string; siteCode: string; siteName: string; address?: string | null; lng?: number | null; lat?: number | null; enable: boolean; warehouseCd?: string | null }
  export const siteApi = { list(): Envelope<SiteVO[]>; create(d: SiteVO): Envelope<{ id: string }>; update(id: string, d: SiteVO): Envelope<unknown>; remove(id: string): Envelope<unknown> }
  // api/space/floor.ts 追加
  floorApi.create(d: FloorVO) / update(id, d) / remove(id)
  ```
  （实际返回都是 `Promise<Envelope<…>>`——签名照 floor.ts 现有 `http.get<unknown, Envelope<…>>` 泛型写法。）
  后端 `SiteDto.WarehouseCd`（`string?`，注释注明 ch04 §3.4 映射语义 + ≤10 字符由列约束与发布侧 E-SPACE-405 守卫兜底）。Task 2/3/4 全部依赖本任务的 api/类型。

- [ ] **Step 1: 后端失败测试**

`CP6.Tests/SpaceMasterServiceTests.cs` 按该文件既有 Site 测试风格新增：

```csharp
    [Fact]
    public async Task Site_WarehouseCd_RoundTrips_ThroughDtoAndService()
    {
        // 波1 终审记票兑现：SiteDto 暴露 WarehouseCd（此前是只能 SQL 改的死配置列）
        // Create 带 WarehouseCd → List 回显 → Update 改值 → List 回显新值
        // （种子/调用按本文件既有 CreateSiteAsync/ListSitesAsync/UpdateSiteAsync 测试写法）
    }
```

Run: `dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~Site_WarehouseCd"` → FAIL（编译错：DTO 无该字段）。

- [ ] **Step 2: 后端实现**

① `SpaceMasterDtos.cs` 的 `SiteDto` 末尾加：

```csharp
    /// <summary>WMS 仓库编码映射（ch04 §3.4：空=默认 WarehouseCd=SiteCode；≤10 字符，超长由发布侧 E-SPACE-405 守卫拦截）</summary>
    public string? WarehouseCd { get; set; }
```

② `SpaceMasterService.cs` Site 段三处映射补 `WarehouseCd`（Create 的实体构造、Update 的字段覆盖、List 的 DTO 投影——照该段现有字段的写法逐处加一行）。

Run: 过滤测试 PASS → 全量 `dotnet test CP6.Tests/CP6.Tests.csproj` → 1557 passed / 5 skipped；`dotnet build CP6.slnx` → 0 errors。

- [ ] **Step 3: 前端 API 层**

① 新建 `cp6.web/src/api/space/site.ts`（Envelope 泛型写法照 `floor.ts`，REST 路径照后端端点表；`remove` 用 `http.delete`，路径参数 `encodeURIComponent(id)` 照 warehouse.ts 惯例）。
② `floor.ts` 补 `create/update/remove` 三方法（`POST /space/floor`、`PUT /space/floor/{id}`、`DELETE /space/floor/{id}`）。
③ `types/space/scene.ts`：加 `SiteVO`（上方 Produces 形状）；核对 `FloorVO` 是否含 `siteId/level/floorCode/floorName/height/underlayImage/underlayScale/underlayOffsetX/underlayOffsetY/originX/originY`（后端 FloorDto 全字段），缺则补——管理页表单要用。

- [ ] **Step 4: 前端验证 + Commit**

Run（cp6.web/）: `NODE_OPTIONS=--max-old-space-size=8192 npm run type-check` → 0 errors；`npm run test` → 全绿（记录基线数 N）。

```bash
git add CP6.Entity/DTOs/Space/SpaceMasterDtos.cs CP6.Core/Services/Space/SpaceMasterService.cs CP6.Tests/SpaceMasterServiceTests.cs cp6.web/src/api/space/site.ts cp6.web/src/api/space/floor.ts cp6.web/src/types/space/scene.ts
git commit -m "feat(space): Site/Floor 前端 API 层 + SiteDto 暴露 WarehouseCd 映射列（波1记票兑现）

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: 站点管理页 SpaceSiteView

**Files:**
- Create: `cp6.web/src/views/space/master/SpaceSiteView.vue`
- Test: `cp6.web/src/views/space/master/__tests__/SpaceSiteView.spec.ts`

**Interfaces:**
- Consumes: Task 1 `siteApi`/`SiteVO`；Cp* 组件（全局注册与否照 WarehouseListView 的 import 方式）
- Produces: 路由组件 `/space/site`（Task 5 登记）。页面行为规格（**模板=WarehouseListView.vue，先全文读再 1:1 套用**）：

| 项 | 规格 |
|---|---|
| 外壳 | `CpPageShell :title="t('space.site.title')" :count`，`#actions` 放「新建站点」按钮 |
| 列 | siteCode(mono) / siteName / warehouseCd(mono，空显示 `—`+`title` 提示默认=siteCode) / address(overflowTooltip) / enable(kind:tag map: true→{label:t('space.common.enabled'),tone:'ok'} false→{…muted}) / `_action`(编辑/削除 + 「楼层」跳 `/space/floor?siteId=` ) |
| 筛选 | siteCode(text) / siteName(text) |
| fetch | `siteApi.list()` 前端切片分页（照 WarehouseListView 的切片写法；list 端点无分页） |
| 表单 | CpFormDialog：siteCode(必填,≤50)/siteName(必填,≤100)/warehouseCd(≤10,placeholder=t('space.site.whDefault')「空=同站点编码」)/address/enable(switch)；编辑时 siteCode 只读（发布后 code 是映射锚——保守禁改，波1.5 终审 M 项背书） |
| 删除 | ElMessageBox.confirm → `siteApi.remove` → 失败把后端 message 原样 ElMessage.error（后端有子级护栏） |
| 刷新 | 增删改后 `listRef.value?.reload()` |
| i18n | 全部 `space.site.*`/`space.common.*` 点分 key，用到的每个 key 记入清单交 Task 6 |

- [ ] **Step 1: 写失败测试**（`SpaceSiteView.spec.ts`，照 CpListPage.spec 范式：jsdom pragma；`vi.mock('@/api/space/site')` 让 `list` 返回 2 行样例；断言①渲染出两行站点编码文本 ②点「新建」后对话框可见 ③mock `create` 后保存触发 `list` 二次调用）
- [ ] **Step 2: 确认 RED**（组件不存在 → 编译/解析失败）
- [ ] **Step 3: 实现页面**（严格按上表 + 模板；`console` 零告警）
- [ ] **Step 4: GREEN + type-check(8192) 全过**
- [ ] **Step 5: Commit** `feat(space): 站点管理页（CRUD+WarehouseCd 映射编辑，Cp* 模板）`（+尾注）

---

### Task 3: 楼层管理页 SpaceFloorView

**Files:**
- Create: `cp6.web/src/views/space/master/SpaceFloorView.vue`
- Test: `cp6.web/src/views/space/master/__tests__/SpaceFloorView.spec.ts`

**Interfaces:**
- Consumes: Task 1 `floorApi`（含新 CRUD）+ `siteApi.list`（站点下拉）
- Produces: 路由组件 `/space/floor`。规格（模板同 WarehouseListView）：

| 项 | 规格 |
|---|---|
| 站点选择 | 顶部工具栏 `el-select`（siteApi.list 填充；支持 `route.query.siteId` 预选）；未选站点显示 CpEmpty 提示先选站点 |
| 列 | level(num) / floorCode(mono) / floorName / height(num, 单位 mm) / `_action`(编辑/削除/「編集画面」按钮 named-push `{ name:'space-editor', params:{ floorId: row.id } }`) |
| fetch | `floorApi.list(siteId)` 前端切片；siteId 变更时 `listRef.reload()` |
| 表单 | CpFormDialog：level(必填 int)/floorCode(必填)/floorName(必填)/height(int 默认 6000)；siteId 取当前选中站点（新建时隐藏字段带入）；underlay/origin 字段**不进表单**（编辑器域，YAGNI） |
| 删除/刷新/i18n | 同 Task 2 范式，key 用 `space.floor.*` |

- [ ] **Step 1-5**：TDD 流程同 Task 2（spec 断言：mock site+floor list → 选站点后渲染楼层行；「編集画面」按钮触发 `router.push` 且 name/params 正确——用 `vi.mock('vue-router')` 或注入 stub router 照既有页面 spec 先例）。Commit：`feat(space): 楼层管理页（站点下拉+CRUD+跳编辑器）`（+尾注）

---

### Task 4: 落地页 SpaceHomeView（消灭孤儿路由的入口）

**Files:**
- Create: `cp6.web/src/views/space/SpaceHomeView.vue`
- Test: `cp6.web/src/views/space/__tests__/SpaceHomeView.spec.ts`

**Interfaces:**
- Consumes: Task 1 `siteApi.list` + `floorApi.list`
- Produces: 路由组件 `/space/home`。规格（模板=WmsDashboardView 的 CpStatCard 网格 + 卡片区）：

| 项 | 规格 |
|---|---|
| 顶部 | CpSectionHeader `t('space.home.title')`；CpStatCard×2：站点数（tone:brand）/ 楼层数（tone:brand）（数据=两个 list 的长度；不做库位数——无轻量端点，YAGNI） |
| 站点卡片区 | 每站点一张卡（el-card 或照 dashboard 的卡片写法）：标题=siteCode+siteName；卡头按钮「3D」（named-push space-viewer, params:{siteId}）与「全景」（space-stacked）；卡内楼层列表（floorApi.list 按需拉取——卡片展开时 or 全量预拉，取实现简单者），每层一行：`L{level} {floorName}` + 「編集」（space-editor, params:{floorId}）+「3D」（space-viewer + query:{floorId}） |
| 空态 | 无站点 → CpEmpty + 按钮「去创建站点」（push `/space/site`） |
| i18n | `space.home.*` |

- [ ] **Step 1-5**：TDD 同前（spec：mock 两 API → 断言 StatCard 数值与站点卡片渲染、按钮 push 参数正确、空态分支）。Commit：`feat(space): Space 落地页（站点/楼层导航，消灭孤儿路由）`（+尾注）

---

### Task 5: 路由登记 + 菜单种子 + i18n 词条种子

**Files:**
- Modify: `cp6.web/src/router/index.ts`（viewModules 加 3 条）
- Create: `docs/seeds/space-menu-seed.sql`
- Create: `docs/seeds/space-i18n-seed.sql`

**Interfaces:**
- Consumes: Task 2/3/4 的三个页面组件路径；三页各自上报的 `space.*` key 清单（从三个 Task 的代码 grep `t('space.` 汇总，一个不漏）
- Produces: 菜单可见的 Space 入口（重登后生效）。

- [ ] **Step 1: viewModules 登记**

`router/index.ts` 的 viewModules 表内（按既有分组注释风格加一组）：

```ts
  // ── Space 空間管理（波2 P0）─────────────────────────────
  '/space/home': () => import('@/views/space/SpaceHomeView.vue'),
  '/space/site': () => import('@/views/space/master/SpaceSiteView.vue'),
  '/space/floor': () => import('@/views/space/master/SpaceFloorView.vue'),
```

- [ ] **Step 2: 菜单种子**

`docs/seeds/space-menu-seed.sql`——完整照 wms-menu-seed.sql 骨架（头注释/事务/TRY-CATCH/验证查询/回滚段），INSERT 用 oa-designer-menu.sql 的**显式列清单含 MenuKey 变体**：

```sql
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 900)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (900, N'空間管理(Space)', NULL, N'space', N'Grid', NULL, 900, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 901)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (901, N'スペースホーム', N'/space/home', N'space-home', N'HomeFilled', 900, 901, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 902)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (902, N'サイト管理', N'/space/site', N'space-site', N'OfficeBuilding', 900, 902, 1, SYSDATETIME());
IF NOT EXISTS (SELECT 1 FROM Sys_Menus WHERE MenuId = 903)
    INSERT INTO Sys_Menus (MenuId, MenuName, RoutePath, MenuKey, Icon, ParentId, OrderNo, Enable, CreateDate)
    VALUES (903, N'フロア管理', N'/space/floor', N'space-floor', N'Files', 900, 903, 1, SYSDATETIME());
-- RoleId=1 授权（900-919 区间幂等）照 wms-menu-seed.sql:173-180 写法
```

（头注释注明：**执行前先 `SELECT MenuId FROM Sys_Menus WHERE MenuId BETWEEN 900 AND 919` 复核空段**——静态检索 900 段无占用，2026-07-06 裁决记录；Icon 名以 Element Plus 图标集实际存在为准，参考 wms 种子用过的图标名。）

- [ ] **Step 3: i18n 词条种子**

`docs/seeds/space-i18n-seed.sql` 照 wms-realtime-i18n-seed.sql 的 MERGE 范式：汇总 Task 2/3/4 全部 `space.*` key（逐文件 grep 核对零遗漏），五语（ZhCN/ZhTW/En/Ja/Ko——Ja 为主要用户语言，用词参考既有 wms 种子的术语系）。

- [ ] **Step 4: 验证 + Commit**

Run: type-check(8192) + `npm run test` + `npm run build` 全过（种子 SQL 不参与构建，人工检查两遍 SQL 语法与 key 对账清单）。

```bash
git add cp6.web/src/router/index.ts docs/seeds/space-menu-seed.sql docs/seeds/space-i18n-seed.sql
git commit -m "feat(space): 路由登记 + 菜单/i18n 种子（MenuId 900段，MenuKey 齐备）

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Zone 创建工具（鸡蛋问题闭合）

**Files:**
- Create: `cp6.web/src/space-editor/interact/tools/ZoneTool.ts`
- Create: `cp6.web/src/space-editor/command/commands/AddZoneCmd.ts`
- Modify: `cp6.web/src/space-editor/interact/InteractionManager.ts`（ToolType 加 `'zone'` + tools 注册）
- Modify: `cp6.web/src/views/space/editor/FloorEditor.vue`（工具栏按钮 + 拖框完成回调 + 命名弹窗）
- Test: `cp6.web/src/space-editor/command/commands.spec.ts`（或就近既有 spec 文件）加 AddZoneCmd 测试；ZoneTool 几何纯函数测试

**Interfaces:**
- Consumes: 引擎既有件——`SelectTool.ts:37-114` 拖框几何（Konva.Rect 橡皮筋 + `screenToWorld` 两角→WorldRect）、`MarkerTool.ts` 工具→命令栈骨架、`AddMarkerCmd.ts` 命令模板（`do: scene.markers.push + ctx.markDirty` / `undo: splice + markDirtyDelete`）、`EditorContext = { scene, markDirty, markDirtyDelete }`
- Produces:
  ```ts
  // AddZoneCmd.ts —— 照 AddMarkerCmd 逐字改造为 zones
  export class AddZoneCmd implements Command {
    constructor(private zone: ZoneVO) {}
    do(ctx: EditorContext) { ctx.scene.zones.push(this.zone); ctx.markDirty(this.zone.id) }
    undo(ctx: EditorContext) {
      const i = ctx.scene.zones.findIndex(z => z.id === this.zone.id)
      if (i >= 0) { ctx.scene.zones.splice(i, 1); ctx.markDirtyDelete(this.zone.id) }
    }
  }
  ```
  （实际以 Command 接口/AddMarkerCmd 的真实签名为准逐字对齐——markDirty 系方法名与参数以 `command/Command.ts` 为准。）
  ZoneTool：`onMouseDown/Move/Up` 橡皮筋（复制 SelectTool 几何），`onMouseUp` 得世界系矩形后 emit 回调 `onZoneRectDrawn(rect: WorldRect)`（回调由 FloorEditor 注入——照 TemplatePanel 放置流的通信方向：引擎事件 → 页面处理业务）。

**行为规格：**
1. 工具栏（`FloorEditor.vue` 的 `el-button-group`）加「新建库区」按钮 → `switchTool('zone')`（按钮高亮逻辑同现有工具）。
2. 画布拖矩形 → 松开 → FloorEditor 弹 dialog（`el-dialog` 或 `ElMessageBox` 风格照该文件现有弹窗）收 `zoneCode`（必填）/`zoneName`（必填）/`zoneType`（下拉，默认 1）/`color`（可选，el-color-picker 或预设色下拉——取简）。
3. 校验（Global Constraints 的三条）：矩形短边 < 500mm → `ElMessage.warning(t('库区太小，请拖大一点'))` 丢弃；zoneCode 与 `store.scene.zones` 重复 → warning 不落。
4. 通过 → 组 `ZoneVO { id: crypto.randomUUID(), floorId, zoneCode, zoneName, zoneType, polygon: JSON.stringify(4点), color, enable: true }` → `stack.exec(new AddZoneCmd(vo))` → `afterCommand()` 重渲染 → 工具切回 select。新库区立即出现在库区下拉（`zones` computed 自动反应），可直接选中放货架；保存走既有保存按钮（zones 差量通路已就绪，探查实证 `spaceEditor.ts:94`）。
5. 取消 dialog → 丢弃矩形，不产生命令。

- [ ] **Step 1: 失败测试**（AddZoneCmd do/undo 的 push/splice+dirty 断言，照该 spec 文件既有 AddMarkerCmd 测试写法；ZoneTool 的矩形→polygon 纯函数若抽出则单测 4 点顺序与短边校验）
- [ ] **Step 2: RED 确认**
- [ ] **Step 3: 实现**（严格按行为规格；ZoneTool 内部不做业务校验——校验在 FloorEditor 回调里，工具只管几何，与 SelectTool 职责一致）
- [ ] **Step 4: GREEN + type-check(8192) + `npm run test` 全绿**
- [ ] **Step 5: Commit** `feat(space): 编辑器 Zone 创建工具（拖框+命令栈+差量保存，解建模鸡蛋问题）`（+尾注）

---

### Task 7: 前端回归 + 真库 QA 走查（波2 DoD）

**Files:** 无代码变更预期；发现缺陷则修复+测试单独 commit

**Interfaces:**
- Consumes: Task 1-6 全部产物
- Produces: 波2 DoD 证据

- [ ] **Step 1: 回归门**

Run（cp6.web/）: `NODE_OPTIONS=--max-old-space-size=8192 npm run type-check` → 0；`npm run test` → 全绿（≥ Task 1 基线 + 新增 spec）；`npm run build` → 过。后端 `dotnet test CP6.Tests/CP6.Tests.csproj` → 1557/5。

- [ ] **Step 2: 真库种子执行**

1. `SELECT MenuId FROM Sys_Menus WHERE MenuId BETWEEN 900 AND 919` 确认空段（非空→停，回报）。
2. 依序执行 `docs/seeds/space-menu-seed.sql`、`docs/seeds/space-i18n-seed.sql`（sqlcmd，连接方式照 Task 9/波1 冒烟先例——WSL docker 注意端口转发经验）。
3. 验证查询：菜单 4 行、RoleMenus 4 行、Sys_Langs 的 space.* key 数与种子一致。

- [ ] **Step 3: 走查（后端 + vite dev server，不开 headless 浏览器则用 browse 工具按内存情况取舍——先只跑必需进程）**

1. 重新登录（admin/123456）→ 侧边栏出现「空間管理(Space)」组与三个子菜单。
2. `/space/home` 渲染（站点卡片/空态其一）；`/space/site` 新建一个站点（含 WarehouseCd）→ 列表回显；`/space/floor` 选该站点建一层 → 「編集画面」跳入编辑器（standalone 全屏）。
3. 编辑器内：新建库区（拖框+命名）→ 库区下拉出现 → 选模板放一排货架 → 保存 → 刷新重进确认 zone/racks/locations 落库（后端查 Space_Zones/Space_Racks 行数）。
4. i18n 抽查：ja 环境下三页标题/按钮非裸 key。
5. console 零新错（SignalR CSRF 等既有环境噪音除外，对照 OA 回归先例）。

- [ ] **Step 4: 记录 DoD 证据到报告，遗留问题记台账**

若有缺陷：修复+测试，commit `fix(space): 波2走查修复——<问题>`（+尾注）。

---

## 自检记录（写计划时已核）

- **P0 覆盖**：孤儿路由（Task 4+5：落地页+菜单+viewModules）/ Site/Floor 管理（Task 2+3，含 WarehouseCd 死配置列兑现=波1 记票③的 UI 半边）/ Zone 鸡蛋问题（Task 6，探查实证保存通路就绪只缺入口）。明确不做（波3+）：编码规则/生码/预检/发布/停用/采纳/事件查看 UI；Aisle/Marker 面板；Zone 顶点编辑与选中（本波只做新建，探查报告建议一致）；免重登菜单热刷新（新功能，范式外）。
- **决策留档（2026-07-06 主控自洽，用户离场授权）**：MenuId=900 段（500 被 OA 占）；SiteDto 顺带暴露 WarehouseCd（记票兑现，改动面 DTO1 字段+映射 3 处+表单 1 项）；新页面点分 key+五语种子 vs 编辑器内跟随中文字面量（不在老文件混两套）；Zone 走差量保存+命令栈而非独立端点（复用撤销与保存按钮，前端自校验补服务端缺位）。
- **无占位符核查**：页面/工具类任务给行为规格表+硬模板文件（照抄源全文读）而非全码——与波1.5 Task 5/6 测试处理同一先例（模板文件是真实存在的 176 行页面，写死反而与其演进冲突）；小而关键件（AddZoneCmd/种子 SQL/viewModules/DTO）给全码。
- **类型一致性**：`SiteVO.warehouseCd` ↔ 后端 `SiteDto.WarehouseCd`（camelCase 序列化全局约定）；三页组件路径 ↔ viewModules ↔ 菜单 RoutePath 三处逐字核对表在 Task 5；`space.*` key 的唯一权威=三页代码 grep 汇总（Task 5 Step 3 对账）。
- **执行顺序**：1→7 串行（2/3/4 依赖 1 的 api；5 依赖 2/3/4 的组件路径与 key 清单；6 独立于 2-5 但排后避免并行冲突；7 收尾）。
