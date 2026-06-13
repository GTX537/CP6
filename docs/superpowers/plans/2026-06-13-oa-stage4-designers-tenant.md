# OA 阶段4 · 自研设计器 + 系统级多租户（章09 + 10）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**OA 第三份（收官）计划**。设计器部分依赖 OA 阶段1（运行时 schema 结构）；**多租户部分是系统级横切**，依赖 Space/PUB/OA 各模块实体均已落地。

**Goal:** 收口商业化两块——章09 自研设计器（表单三栏拖拽 + 流程有向图，产出与手写**同构**的 schema 喂运行时）；章10 **系统级多租户**（共享表 + 行级隔离 `TenantId`，EF 全局查询过滤 + 租户中间件 + 默认租户迁移）。**⚠️ 章10 是全系统 TenantId 的统一收口**：Space/PUB/OA 各模块计划此前都把 TenantId "延后到此统一处理"，本计划把它一次性做完。

**Architecture:** 设计器**只是 schema 的生产工具**——直接读写运行时 schema（`FormDef.SchemaJson`/`FlowDef.SchemaJson`），画布预览直接复用阶段1 的 `DynamicForm` 渲染器（同构红利，不维护中间格式），保存即 `Version+1` 生效（无生成代码/编译步骤）。多租户走**共享表 + `TenantId` 行级隔离**：抽 `BaseTenantEntity`，EF Core **全局查询过滤器**一处声明全表自动注入 `WHERE TenantId=@current`（防漏命门），`SaveChanges` 拦截自动盖章，`TenantMiddleware` 从 JWT 解析当前租户。与 PUB DataScope（租户内部门隔离）叠加：TenantId 是硬墙（租户间绝对不可见）、DataScope 是软墙（租户内按部门收放）。

**Tech Stack:** .NET 8 + EF Core 8（全局查询过滤 + SaveChanges 拦截）/ xUnit + EF Core InMemory / Vue 3.5 + element-plus + SVG/Canvas（流程图编辑器）。源文档：`docs/approval/09·10`（引用 PUB 数据权限）。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 现状/对账 | **本稿建议值** |
|---|---|---|---|
| **OA4-D1** | **多租户范围 = 系统级** | 章10 给 Wf 实体示范；但 Space/PUB 计划都把 TenantId 延后到"统一处理" | **章10 = 全系统多租户基建**，不止 Wf：抽 `BaseTenantEntity`，**所有需隔离的实体（Space_*、Sys_Dept/UserRole/权限表、Wf_*、业务表）统一接入**。这是 Space/PUB/OA 所有"延后 TenantId"决策的**统一兑现点**。⚠️ 这是本计划最重的一环，请确认范围 |
| **OA4-D2** | **TenantId 迁移路径** | 章10 §8：列可空→默认租户回填→非空+索引→开过滤 | 按章10 §8 五步平滑迁移：①各表加 `TenantId`(可空)②建默认租户 + 回填存量③改非空 + 建索引（含复合唯一索引补 TenantId 前缀，对齐各模块 spec 原始 DDL）④开全局过滤 + 写入盖章。**此步把各模块计划里"唯一索引去 TenantId 前缀"的临时态升级为 spec 原始的 `(TenantId, ...)`** |
| **OA4-D3** | **哪些表隔离/共享** | 章10 §3：业务/配置隔离，纯字典/语言包可共享 | **隔离**：Space_*、Sys_Dept/Sys_UserRole/Sys_MenuAction/RoleAction/RoleDataScope/RoleFieldPerm、Wf_*、Pub_Attachment/DocSequence/GenTable、各业务表。**共享(不带 TenantId)**：Sys_DictType/DictData/Sys_Lang（系统级字典语言，或按需也租户化）、Sys_Menu（菜单结构系统级）。⚠️ 这份盘点请你确认 |
| **OA4-D4** | **ITenantContext 与 PUB/Space 上下文统一** | PUB 有 ICurrentPermissionContext、Space 有 ISpaceTenantContext(桩)、本章 ITenantContext | **统一为一个 `ITenantContext.CurrentTenantId`**（JWT 解析），**替换** Space 的 `ISpaceTenantContext` 桩 + PUB 上下文里的租户来源。一处租户身份，全系统共用 |
| **OA4-D5** | **流程图编辑器实现** | 章09 全自研，轻量有向图 | 自研：SVG 画节点/连线 + 拖拽/缩放（图拓扑与坐标）；节点业务语义（审批人/会签/字段权限/超时）由属性面板写进 schema。**图与语义分离**。撤销/重做用 schema 快照栈 |
| **OA4-D6** | **设计器 vs 运行时同构** | 章09 铁律 | 设计器**直接读写运行时 schema**，无中间格式；画布预览复用 `DynamicForm`；验证标准=导出 JSON 直接喂运行时能跑 |

> **测试基建**：多租户全局过滤/盖章用 InMemory（注意 InMemory 对 HasQueryFilter 支持有限，关键隔离行为补 `[需真库]` 集成测）；设计器交互用 Playwright e2e；schema 快照撤销用 vitest。

---

## File Structure

### 章09 设计器（`cp6.web/src/views/wf/designer/`）
- `FormDesigner.vue`（三栏：控件库 + 画布[复用 DynamicForm 预览] + 属性面板）+ `controlLibrary.ts`（控件默认 schema）
- `FlowDesigner.vue`（SVG 有向图：节点/连线/条件 + 节点属性面板）+ `flowGraph.ts`（图拓扑↔nodes/edges）
- `schemaHistory.ts`（快照栈撤销/重做）+ `designValidate.ts`（设计时校验：断头节点/必填漏配）
- 保存接阶段1 `FormDef`/`FlowDef`（Version+1）

### 章10 系统级多租户（横切）
- `CP6.Entity/BaseTenantEntity.cs`（BaseEntity + TenantId）
- `CP6.Core/Services/Common/ITenantContext.cs`/`TenantContext.cs`（统一租户上下文，替换 ISpaceTenantContext）
- `CP6.WebApi/Middleware/TenantMiddleware.cs`（JWT tenant_id → ITenantContext）
- 修改 `CP6Context.OnModelCreating`（反射对所有 BaseTenantEntity 批量 HasQueryFilter）+ `SaveChanges` 拦截盖章
- 各模块实体改继承 `BaseTenantEntity`（Space_*/Sys_*权限表/Wf_*/Pub_*/业务表）
- 迁移 `*_MultiTenant`（加列 + 回填默认租户 + 改非空 + 复合唯一索引补 TenantId）
- `Sys_Tenant.cs`（租户主数据）+ 租户管理 UI（可选）

### 测试
- 设计器：`FormDesigner.e2e.ts`/`FlowDesigner.e2e.ts`、`schemaHistory.spec.ts`、`designValidate.spec.ts`
- 多租户：`TenantFilterTests`（隔离/盖章）、`TenantMigrationTests`、`[需真库]TenantSqlTests`

---

## 实施分三阶段

- **Phase A**（A-1..A-2）：章09 表单设计器
- **Phase B**（B-1..B-2）：章09 流程设计器
- **Phase C**（C-1..C-5）：章10 系统级多租户（★横切收口）

---

# Phase A — 表单设计器（章09 §2）

## Task A-1: 三栏 FormDesigner + 控件库 + 拖拽产 schema（章09 §2）

**Files:** Create `cp6.web/src/views/wf/designer/FormDesigner.vue`, `controlLibrary.ts`

- [ ] **Step 1: 实现**——三栏：①控件库（input/textarea/number/select/radio/checkbox/date/user/dept/upload）②画布（复用阶段1 `DynamicForm` 渲染当前 schema，所见即所得）③属性面板（选中字段编辑 key/label/type/required/options...）。拖控件→`schema.fields.push(defaultFieldOf(type))`；选中→属性面板加载；改属性→改 `fields[i]`；拖动排序→调整数组顺序。
- [ ] **Step 2: e2e**（拖 3 个控件→画布预览出现→改 label→预览更新→保存）
- [ ] **Step 3: 提交** → `git commit -m "feat(wf): form designer 3-pane drag-to-schema (ch09 §2)"`

## Task A-2: 撤销重做 + 设计时校验 + 保存（章09 §5）

**Files:** Create `schemaHistory.ts`, `designValidate.ts`; Modify `FormDesigner.vue`

- [ ] **Step 1: 失败测试（vitest）**（schemaHistory：每次操作前快照，undo 回上一快照，redo 前进；designValidate：必填项漏配/重复 key 报错）
- [ ] **Step 2: 跑红 → Step 3: 实现**（快照栈撤销重做[schema 是普通对象，快照最省事]；保存前 designValidate 跑运行时同款校验[漏必填/重复 key]，过了才存 `FormDef.SchemaJson` + Version+1）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(wf): form designer undo/redo + design-time validate + save (ch09 §5)"`

---

# Phase B — 流程设计器（章09 §3）

## Task B-1: 有向图编辑器 FlowDesigner（节点/连线/条件，章09 §3）

**Files:** Create `cp6.web/src/views/wf/designer/FlowDesigner.vue`, `flowGraph.ts`

- [ ] **Step 1: 实现**——SVG 有向图：拖审批节点→`nodes.push({id,type:approval})`；A 拉线到 B→`edges.push({from,to})`；连线配条件→`edge.condition="days<=3"`；图拓扑与坐标由 flowGraph 管，业务语义由属性面板写 schema（图与语义分离，OA4-D5）。撤销重做复用 schemaHistory。
- [ ] **Step 2: e2e**（拖节点+连线→产出 nodes/edges→保存）
- [ ] **Step 3: 提交** → `git commit -m "feat(wf): flow designer directed-graph editor (ch09 §3)"`

## Task B-2: 节点属性面板（配齐审批人/会签/字段权限/超时）+ 设计时校验（章09 §3/§5）

**Files:** Modify `FlowDesigner.vue`, `designValidate.ts`

- [ ] **Step 1: 实现**——节点属性面板配齐前面各章能力：`approver`（01 审批人规则）/`countersign`（03 会签 all/any/veto）/`fieldPerms`（04 字段权限）/`timeout`（07 超时策略）。设计时校验：断头节点（连不到 end）、审批人规则不完整→当场提示，不让坏 schema 进库。保存 `FlowDef.SchemaJson` + Version+1。**验证同构**：导出 JSON 直接喂阶段1 FlowEngine 能跑。
- [ ] **Step 2: e2e + 同构验证（导出 schema 喂 FlowEngine 起流程跑通）+ 提交** → `git commit -m "feat(wf): flow node property panel (approver/countersign/fieldperm/timeout) + validate (ch09 §3/§5)"`

---

# Phase C — 系统级多租户（章10 ★横切收口）

> ⚠️ 本阶段是 **Space/PUB/OA 所有"延后 TenantId"决策的统一兑现**（OA4-D1）。改动面广（多模块实体 + DbContext + 中间件 + 迁移），建议独立分支谨慎推进。

## Task C-1: BaseTenantEntity + ITenantContext + TenantMiddleware（章10 §3/§5）

**Files:** Create `CP6.Entity/BaseTenantEntity.cs`, `CP6.Core/Services/Common/ITenantContext.cs`/`TenantContext.cs`, `CP6.WebApi/Middleware/TenantMiddleware.cs`

- [ ] **Step 1: 实现**

```csharp
// BaseTenantEntity.cs（介于 BaseEntity 与业务实体）
public abstract class BaseTenantEntity : BaseEntity { public Guid TenantId { get; set; } }
```
```csharp
// ITenantContext.cs（统一租户上下文，OA4-D4：替换 ISpaceTenantContext + PUB 租户来源）
public interface ITenantContext { Guid CurrentTenantId { get; set; } }
public class TenantContext : ITenantContext
{
    public static readonly Guid DefaultTenant = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    public Guid CurrentTenantId { get; set; } = DefaultTenant;   // 中间件覆盖
}
```
```csharp
// TenantMiddleware.cs（JWT tenant_id → scoped ITenantContext）
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    public TenantMiddleware(RequestDelegate next) => _next = next;
    public async Task Invoke(HttpContext ctx, ITenantContext tenant)
    {
        var tid = ctx.User.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(tid, out var g)) tenant.CurrentTenantId = g;
        await _next(ctx);
    }
}
```

- [ ] **Step 2: DI（scoped ITenantContext）+ 注册中间件 + JWT 签发带 tenant_id（改 AuthController/JwtHelper）+ 提交** → `git commit -m "feat(tenant): BaseTenantEntity + ITenantContext + TenantMiddleware (ch10 §3/§5)"`

## Task C-2: 实体接入 BaseTenantEntity（多模块，OA4-D3）

**Files:** Modify Space_*/Sys_(Dept/UserRole/MenuAction/RoleAction/RoleDataScope/RoleFieldPerm)/Wf_*/Pub_* 实体

- [ ] **Step 1: 改继承**——需隔离实体由 `BaseEntity` 改继承 `BaseTenantEntity`（删各自手写的 `TenantId` 字段——Space 实体之前手写了 TenantId，改为继承得到，去重）。共享表（Sys_DictType/DictData/Sys_Lang/Sys_Menu）保持 BaseEntity（OA4-D3）。
- [ ] **Step 2: 构建确认 + 提交** → `git commit -m "feat(tenant): entities inherit BaseTenantEntity (space/sys-perm/wf/pub) (ch10 §3)"`

## Task C-3: 全局查询过滤 + SaveChanges 盖章（章10 §4，防漏命门）★

**Files:** Modify `CP6.Core/EFDbContext/CP6Context.cs`; Test `TenantFilterTests.cs`

- [ ] **Step 1: 失败测试**（注入 tenant=A 查询只见 A 的行；新增实体自动盖当前 tenant；跨租户查不到）`[InMemory 测盖章逻辑 + 真库测过滤]`

```csharp
[Fact]
public async Task Query_OnlySeesCurrentTenant_AndStampsOnInsert()
{
    var tA = Guid.NewGuid(); var tB = Guid.NewGuid();
    using (var db = DbFor(tA)) { db.Space_Sites.Add(new Space_Site{Id=Guid.NewGuid(),SiteCode="A",SiteName="a"}); await db.SaveChangesAsync(); } // 自动盖 tA
    using (var db = DbFor(tB)) { Assert.Empty(await db.Space_Sites.ToListAsync()); }   // B 看不到 A
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**

```csharp
// OnModelCreating：反射对所有 BaseTenantEntity 批量注册全局过滤
foreach (var et in mb.Model.GetEntityTypes()
    .Where(t => typeof(BaseTenantEntity).IsAssignableFrom(t.ClrType)))
{
    var p = Expression.Parameter(et.ClrType, "e");
    var body = Expression.Equal(
        Expression.Property(p, nameof(BaseTenantEntity.TenantId)),
        Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));  // 闭包到 _tenant
    mb.Entity(et.ClrType).HasQueryFilter(Expression.Lambda(body, p));
}
// SaveChanges 拦截：给新增的 BaseTenantEntity 盖 TenantId
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    foreach (var e in ChangeTracker.Entries<BaseTenantEntity>().Where(x => x.State == EntityState.Added))
        if (e.Entity.TenantId == Guid.Empty) e.Entity.TenantId = _tenant.CurrentTenantId;
    return await base.SaveChangesAsync(ct);
}
```
（CP6Context 构造注入 `ITenantContext _tenant`，暴露 `CurrentTenantId`。）

- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(tenant): global query filter + SaveChanges stamping (ch10 §4)"`

## Task C-4: 迁移 — 加列 + 默认租户回填 + 非空 + 复合唯一索引补 TenantId（章10 §8，OA4-D2）★

**Files:** migration `*_MultiTenant`; Test `TenantMigrationTests.cs`

- [ ] **Step 1: 实现迁移**（按章10 §8 五步：①各隔离表加 `TenantId`(可空)②插入默认租户 + `UPDATE ... SET TenantId=默认` 回填存量③改非空 + 建 `IX(TenantId)`④**把各模块此前"去 TenantId 前缀"的唯一索引升级为 spec 原始 `(TenantId, Code...)`**——如 Space `UX_..._Tenant_Code`、Sys_Dept `(TenantId,DeptCode)`、Sys_UserRole `(TenantId,UserId,RoleId)` 等）
- [ ] **Step 2: 验证（默认租户期 = 单租户平滑，存量数据无损）+ 提交** → `git commit -m "feat(tenant): migration — add TenantId, backfill default, composite unique indexes (ch10 §8)"`

## Task C-5: 替换 Space/PUB 临时租户上下文 + 配置隔离/模板（章10 §6/§7）

**Files:** Modify Space `ISpaceTenantContext` 调用点 → `ITenantContext`; PUB 聚合上下文租户来源; 预置模板复制

- [ ] **Step 1: 实现**——删 Space `ISpaceTenantContext` 桩，调用点改 `ITenantContext`（OA4-D4 统一）；PUB `UserPermissionContext`/`DataScopeFilter` 的租户来源统一走 `ITenantContext`；与 PUB DataScope 叠加验证（TenantId 硬墙 + DataScope 软墙，章10 §6）；预置表单/流程模板按租户复制（新租户开通克隆，章10 §7 商业化）。
- [ ] **Step 2: 全量构建 + 全测 + `[需真库]` 隔离集成测 + 提交** → `git commit -m "feat(tenant): unify tenant context across modules + template copy (ch10 §6/§7)"`

---

## Self-Review（对照章09/10 覆盖）

- **章09**：表单设计器三栏(A-1) ✅ / 拖拽产 schema(A-1) ✅ / 画布复用运行时渲染器(A-1) ✅ / 撤销重做快照(A-2) ✅ / 流程设计器有向图(B-1) ✅ / 节点属性配齐审批人/会签/字段权限/超时(B-2) ✅ / 设计时校验断头节点(A-2/B-2) ✅ / 同构验证(B-2) ✅ / 保存 Version+1(A-2/B-2) ✅
- **章10**：共享表+行级隔离(C-1~C-4) ✅ / BaseTenantEntity(C-1/C-2) ✅ / 全局查询过滤防漏(C-3) ✅ / SaveChanges 盖章(C-3) ✅ / TenantMiddleware JWT(C-1) ✅ / TenantId×DataScope 两层叠加(C-5) ✅ / 配置隔离+模板市场(C-5) ✅ / 单租户迁移五步(C-4) ✅ / TenantId 用 Guid(C-1) ✅

**已知缺口/推迟（已标注）：**
1. **多租户范围 = 系统级**（OA4-D1/D3）—— 本计划统一兑现 Space/PUB/OA 所有延后的 TenantId；隔离/共享表盘点待你确认。
2. **独立库/独立 schema 混合模式**（章10 §2 强合规大客户）—— v1 共享表行级，物理隔离后续按大客户上。
3. **超时扫描/后台任务的租户上下文**—— HostedService 无 HttpContext，需显式按租户循环或在任务内设 tenant（C-3 注：后台写入盖章需特殊处理，留注）。
4. **模板市场/按租户计费**（章10 §7）—— v1 做模板复制，市场/计费是商业化后续。
5. **设计器复杂画布交互**（对齐/吸附）—— v1 朴素画布 + 完整属性面板（章09 §6：属性面板比画布重要），交互打磨后续。

**Type 一致性：** `ITenantContext.CurrentTenantId`(C-1) 被 CP6Context 全局过滤/盖章(C-3) + TenantMiddleware(C-1) + 替换 Space/PUB 上下文(C-5) 一致用；`BaseTenantEntity`(C-1) 被各模块实体继承(C-2)；设计器产出 schema(A/B) 喂阶段1 `FormDef`/`FlowDef` 运行时（同构 OA4-D6）。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-oa-stage4-designers-tenant.md`。**OA 第三份（收官）**。至此 **OA 三份计划全齐**：
1. `2026-06-13-oa-stage1-runtime.md`（阶段1 可用 OA）
2. `2026-06-13-oa-stage2-3-integration-advanced.md`（阶段2 接业务 + 阶段3 复杂审批）
3. `2026-06-13-oa-stage4-designers-tenant.md`（阶段4 设计器 + 系统级多租户）← 本文

**下一步按工作流是你修订**（拍板 OA4-D1~D6，尤其 D1/D3 多租户范围与隔离表盘点——这是全系统决策）。

> **⚠️ 重要排期建议**：章10 多租户是 Space/PUB/OA 所有计划"延后 TenantId"的统一收口。建议**各模块单租户功能先落地、多租户(本计划 Phase C)作为全系统一次集中改造**（章10 §9 原则：该延后的延后、到点集中做）——不要每个模块各自上 TenantId。Phase C 可独立于设计器(Phase A/B)排期。

---

*初稿生成于 2026-06-13。源：docs/approval/09·10（引用 PUB 数据权限）。已勘察：零多租户现状、各模块计划均延后 TenantId 到此、阶段1 DynamicForm/FormDef/FlowDef 运行时为设计器前置、JWT/AuthController 现成、EF Core 全局查询过滤可反射批量注册。*
