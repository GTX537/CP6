# Space P1 后端地基 Implementation Plan（初稿）

> **v1.1 评审返改（2026-06-27）**：多租户从「自建桩方案」升级为**复用 CP6 真·多租户基建**——Space 实体继承 `BaseBizEntity`（已含 `TenantId`/`IsDeleted`/`RowVersion`），直接吃 `ITenantContext`（`CP6.Core.Services.Common`）+ `CP6Context.OnModelCreating` 对所有 `BaseTenantEntity` 子类反射注册的全局查询过滤 + `TenantMiddleware`（JWT `tenant_id` 解析）+ `SaveChanges` 自动盖章。**删自建 `ISpaceTenantContext`/`DefaultSpaceTenantContext` 桩、删默认 GUID 常量（改用 `TenantContext.DefaultTenant`，恰好同值）、删所有显式 `.Where(x => x.TenantId == ...)`（全局过滤自动加）与手工 `entity.TenantId = ...`（盖章自动）**。下文凡 v1.1 触及处均标 `(v1.1: …)`；并联动 00/03/04 v1.1 补丁（见各 Phase 注记）。**全局名称映射（下文代码样例一律按此读）**：`ISpaceTenantContext`→`ITenantContext`；`DefaultSpaceTenantContext`→`TenantContext`；属性 `.TenantId`→`.CurrentTenantId`；`DefaultSpaceTenantContext.DefaultTenant`→`TenantContext.DefaultTenant`；`new DefaultSpaceTenantContext()`→`new TenantContext()`（或测试走 `TestHelper.CreateInMemoryContext(user, tenant)`）。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流说明（丛书模式）**：本文是**我出的初稿**。按既定工作流，下一步是**你做修订版**（重点看下面「关键前置决策」5 条），我再评审合并为唯一定稿后才进编码。**先别按本稿动代码**——决策未拍板前，schema/签名可能整体改。

**Goal:** 落地 Space 空间数字底座 P1 的**后端地基**——9 张几何主数据表 + 主数据 CRUD/场景聚合 API（00 章）+ 可配置库位编码引擎（03 章）+ 库位发布与 WMS 集成契约（04 章），产出可独立编译、可单元/集成测试、可发布事件给 WMS 桩的后端闭环。

**Architecture:** 沿用 CP6 既有 `folder=namespace` 分层（`CP6.Entity/DomainModels/Space`、`CP6.Core/Services/Space`、`CP6.WebApi/Controllers/Space`）。几何真相落 9 表；编码引擎按层级链遍历生成租户全局唯一库位编码；发布**复用** CP6 既有集成基建（`T_IntegrationEvent` + `BridgeHookBase` + `IntegrationEventDispatcher` + 重试 Worker/死信），新增 `SPACE|WMS|OnLocationPublishedAsync` 路由。WMS 消费端与库存查询 P1 用**桩实现**（真实实现属 WMS 模块工作），后端因此不被 WMS 绑死、可独立交付。前端（01/02 Konva 编辑器、05/06 Three.js viewer）是**紧随其后的第二份计划**，不在本稿范围。

**Tech Stack:** .NET 8 Web API + EF Core 8 + SQL Server / xUnit + EF Core InMemory（单测）。源文档：`docs/space/00-data-model.md`、`docs/space/03-code-engine.md`、`docs/space/04-publish-contract.md`。

---

## 关键前置决策（待你修订时确认 —— 本稿已按"建议值"展开，改了就要回改对应任务）

丛书 DDL 是按"理想多租户 schema"写的，与 CP6 现状有 5 处缺口。我已勘察代码库，逐条给出**建议值 + 理由**，请在修订版里确认或推翻：

> **(v1.1: D-A/D-B/D-C 已于评审定案)**——本计划初稿写于「CP6 零多租户」假设，故 D-A 用桩。**S 类安全合规整包落地后 CP6 已有真·多租户基建**（`BaseTenantEntity.TenantId` + `BaseBizEntity` + `ITenantContext` + `CP6Context` 反射全局过滤/盖章 + `TenantMiddleware`）。下表 D-A/D-B/D-C 三行按 v1.1 重写为「复用真基建」；D-D/D-E 不变。

| # | 议题 | 文档原意 | CP6 现状（已勘察） | **本稿建议值** | 影响面 |
|---|---|---|---|---|---|
| **D-A** | **TenantId / 多租户** | 全表带 `TenantId`，唯一索引均 `(TenantId, Code...)`，EF 全局查询过滤器隔离 | **(v1.1 修正现状)** S 类合规后 CP6 已有真·多租户：`BaseTenantEntity.TenantId` + `CP6Context.OnModelCreating` 对所有 `BaseTenantEntity` 子类反射注册 `HasQueryFilter(x => x.TenantId == CurrentTenantId)` + 反射把单列唯一索引升级为 `(TenantId, …)` 复合唯一 + `SaveChanges` 自动盖章 + `ITenantContext`/`TenantMiddleware`（JWT `tenant_id`） | **(v1.1 定案) 复用真基建**：9 表实体**继承 `BaseBizEntity`**（→ 自带 `TenantId`），即获全局查询过滤 + 复合唯一升级 + 写入盖章，**P1 即真多租户**。**无需** `ISpaceTenantContext` 桩、**无需**默认 GUID 常量（复用 `TenantContext.DefaultTenant`，恰好同值 `…A1`）、**无需**每查询显式 `.Where(TenantId)`（全局过滤自动加）。服务/控制器构造注入 `ITenantContext`（仅在确需跨租户/绕过时用）。**09 章从「设计租户」降级为「接线收口 + 冒烟」**（中间件已全局生效，只验证 Space 端点随当前租户隔离）。 | 实体改基类即可；查询无需改 |
| **D-B** | **审计字段名** | DDL 写 `CreateTime/Creator/UpdateTime/Updater` | 真实 `BaseEntity` = `Creator/CreateDate/Modifier/ModifyDate`（见 `CP6.Entity/BaseEntity.cs`） | **以代码为准（确认）**：用 `Creator/CreateDate/Modifier/ModifyDate`；文档 DDL 的 `CreateTime/UpdateTime/Updater` 视为笔误。**(v1.1)** 这些字段经由 `BaseBizEntity → BaseTenantEntity → BaseEntity` 继承链获得，无需在 Space 实体内声明 | 全部 DDL/迁移 |
| **D-C** | **基类与软删** | "继承 BaseEntity（含 Id/TenantId）"；"全表不做软删列、用 Enable" | `BaseEntity`(Id/审计字段)；`BaseTenantEntity : BaseEntity`(+`TenantId`)；`BaseBizEntity : BaseTenantEntity`(+`IsDeleted`+`[Timestamp]RowVersion`) | **(v1.1 改：继承 `BaseBizEntity`)**——一次性获得 `TenantId`（纳入全局过滤/盖章）+ `IsDeleted` + `RowVersion`。**各实体不再自行声明 `TenantId`、不再手工加 `[Timestamp] RowVersion`**（全 9 表都有 RowVersion，乐观锁全表可用，超出原"仅 Rack/Location"）。`IsDeleted` 列随基类带入但**P1 不启用软删**（无对应全局过滤，列休眠；删除护栏仍按 B-5 应用层物理校验，与"用 Enable"语义不冲突——`Enable`=业务停用，`IsDeleted`=技术软删，两者正交）。 | 每个实体改基类一行 |
| **D-D** | **WMS 消费 / 库存查询** | 04 发布给 WMS 消费；D6 停用前 `IWmsStockQuery` 查库存 | WMS 模块未实现这两个契约 | P1 **只立契约 + 注册路由 + 桩实现**：`IWmsLocationConsumer`（NoOp，返回成功）、`IWmsStockQuery`（桩返回 0 库存）。真实 WMS 消费/库存查询是 **WMS 模块的后续工作**，配置开关切换（仿既有 `WmsBridge:Enabled` NoOp 范式）。**(v1.1: 联动 04 v1.1)** 真实消费侧落 `T_WmsBin` 消费表、停用改「同步 RPC」、`SiteCode ↔ WarehouseCd` 映射——均属 WMS 模块后续；P1 仍只保 `NoOpWmsLocationConsumer`/`StubWmsStockQuery` 桩，但桩签名按 04 v1.1 对齐 | 04 全部 + DI |
| **D-E** | **发布批号格式** | `LPUB-20260613-0001`（带日 + 横杠，18 字符） | 既有 `DocNumber.NextAsync` 产 13 字符 `CODE+yyyyMM+seq4`（无日无杠） | 用 `DocNumber.NextAsync(db,"LPB")` 取自增 `seq`，**自行格式化**为 `LPUB-{yyyyMMdd}-{seq:D4}`（≤30 字符，满足 `IntegrationEvent.SourceNo` 约束） | 04 发布服务 |

> **测试基建限制（重要）**：CP6.Tests 用 **EF Core InMemory**（`UseInMemoryDatabase`）。InMemory **测不了**：①过滤唯一索引 `WHERE LocationCode IS NOT NULL` 的真实约束；②`ROWVERSION` 乐观并发冲突；③两阶段重排"先置 NULL 避中途违约"的 SQL Server 无延迟校验行为。这些**逻辑层**（候选码去重、版本单调、状态翻转）能用 InMemory 测；但**索引级/并发级**正确性需补一组**真 SQL Server（或 SQLite）集成测**作兜底（见 Task D-9）。本稿对受影响的测试都标注了 `[InMemory 仅测逻辑]` / `[需真库]`。

---

## File Structure（先锁分层，再拆任务）

### 实体（`CP6.Entity/DomainModels/Space/`，每文件一实体）
- `Space_Site.cs` `Space_Floor.cs` `Space_Zone.cs` `Space_Aisle.cs` `Space_Rack.cs` `Space_Location.cs` `Space_Template.cs` `Space_CodeRule.cs` `Space_Marker.cs` — 9 张表（00 §4）

### DTO / 契约（`CP6.Entity/DTOs/Space/`）
- `SpaceMasterDtos.cs` — Site/Floor/Zone/Aisle/Rack CRUD 的请求/响应 DTO
- `SceneDto.cs` — `/floor/{id}/scene` 聚合响应（Zone/Aisle/Rack/Location/Marker 几何）
- `CodeRuleDtos.cs` — `Segments` 模型、规则 CRUD、preview 请求/响应、precheck 响应
- `LocationPublishBatch.cs` — 04 事件载荷（batch + items + 变长 path + attrs）

### 租户上下文 ~~（自建桩）~~ **(v1.1: 删除——复用既有 `ITenantContext`)**
- ~~`ISpaceTenantContext.cs` + `DefaultSpaceTenantContext.cs`~~ **删除**。直接注入既有 `CP6.Core.Services.Common.ITenantContext`（`TenantMiddleware` 已全局接 JWT；测试用 `new TenantContext { CurrentTenantId = … }` 或 `TestHelper.CreateInMemoryContext(user, tenant)`）。不新增任何租户上下文文件。

### 服务（`CP6.Core/Services/Space/`）
- `ISpaceMasterService.cs` / `SpaceMasterService.cs` — Site/Floor/Zone/Aisle/Rack/Location CRUD + 删除护栏 + scene + unplaced（00 §9）
- `LocationGeometryService.cs` — 绝对坐标缓存重算 `RecalcRackLocationsAsync`（00 §6.2）
- `CodeEngineService.cs` — 规则解析 + 层级遍历生成 + 两阶段重排 + 静态/值级唯一校验 + preview + code-precheck（03）
- `LocationPublishService.cs` — 发布/停用/采纳/对账/删除护栏（04）

### 集成（`CP6.Core/Services/Integration/`，复用既有基建）
- `SpaceBridgeHook.cs` — 继承 `BridgeHookBase`，`OnLocationPublishedAsync`（04 §2.1）
- `IWmsLocationConsumer.cs` + `NoOpWmsLocationConsumer.cs` — WMS 消费端契约 + P1 桩（D-D）
- `IWmsStockQuery.cs` + `StubWmsStockQuery.cs` — 停用库存校验契约 + P1 桩返回 0（D-D；07 章给完整契约，本稿只用"查某码库存量"）
- 修改 `IntegrationEventDispatcher.cs` — 新增 `SPACE|WMS|OnLocationPublishedAsync` 路由 + `LocationPublishBatch` 反序列化

### 控制器（`CP6.WebApi/Controllers/Space/`）
- `SpaceMasterController.cs`（`/api/space/site|floor|zone|aisle|rack|location|floor/{id}/scene|location/unplaced`）
- `CodeRuleController.cs`（`/api/space/code-rule*`、`/floor/{id}/generate-codes`、`/floor/{id}/code-precheck`、`/location/{id}/gen-code`）
- `LocationPublishController.cs`（`/floor/{id}/publish`、`/location/{id}/deactivate`、`/location/adopt`、`/reconcile`、`/aisle|rack/{id}` DELETE、`/publish/events`）

### 注册与迁移
- 修改 `CP6.Core/EFDbContext/CP6Context.cs` — 9 个 `DbSet` + `OnModelCreating` 业务索引配置。**(v1.1: 全局查询过滤、`(TenantId, …)` 复合唯一升级、RowVersion 均由既有反射块对 `BaseTenantEntity` 子类自动覆盖，Space 无需手写；只写过滤唯一索引 `WHERE LocationCode IS NOT NULL` 等业务专属索引)**
- 修改 `CP6.WebApi/Program.cs` — DI 注册（Space 服务 + WMS 桩）。**(v1.1: 租户上下文 `ITenantContext`/`TenantMiddleware` 已在主程序注册，Space 不再注册任何租户上下文)**
- 新增迁移 `CP6.Core/Migrations/*_SpaceP1Init.cs`

### 测试（`CP6.Tests/`）
- `SpacePersistenceTests.cs`（实体落库往返）、`SpaceMasterServiceTests.cs`（CRUD + 删除护栏 + scene）、`LocationGeometryServiceTests.cs`（坐标公式）、`CodeEngineServiceTests.cs`（生成/重排/唯一校验/变长）、`LocationPublishServiceTests.cs`（发布/幂等/D6 停用/删除护栏）、`SpaceBridgeHookTests.cs`（事件持久化 + 路由）、`SpaceSqlIntegrationTests.cs`（`[需真库]` 索引/并发兜底）

---

## 实施分四阶段

- **Phase A**（~~Task A-1..A-3~~ **v1.1: A-1 删除，A-2/A-3**）：9 实体（继承 `BaseBizEntity`）+ DbContext + 迁移 — 地基 schema。**(v1.1: 原 A-1「租户上下文桩」整体删除，改复用既有 `ITenantContext`)**
- **Phase B**（Task B-1..B-6）：主数据服务 + 几何重算 + scene/unplaced + 控制器 — 00 章可用
- **Phase C**（Task C-1..C-6）：编码引擎 — 03 章可用
- **Phase D**（Task D-1..D-9）：发布契约 + WMS 桩 + 集成路由 — 04 章可用，P1 后端闭环达成

> 阶段内 TDD：先写失败测试 → 跑红 → 最小实现 → 跑绿 → 提交。每个 Task 末尾 commit。

---

# Phase A — 实体与地基 schema

## ~~Task A-1: 租户上下文桩（D-A）~~ — **(v1.1: 整任务删除)**

> **(v1.1)** 不再自建 `ISpaceTenantContext`/`DefaultSpaceTenantContext`。CP6 已有 `CP6.Core.Services.Common.ITenantContext`（请求级 scoped，`TenantMiddleware` 从 JWT `tenant_id` 写入；`CP6Context` 全局过滤/盖章读取它）与默认实现 `TenantContext`（`TenantContext.DefaultTenant = 00000000-0000-0000-0000-0000000000A1`，恰好等于原桩拟用的默认 GUID）。**无任何文件要建、无提交**。下游凡需"当前租户 Id"处：服务/控制器注入 `ITenantContext` 取 `.CurrentTenantId`（但 P1 绝大多数情形**不需要**——全局过滤与写入盖章已自动按当前租户处理）。Phase A 从 **A-2** 开始。

---

## Task A-2: 9 个实体类（00 §4）

**Files:**
- Create: `CP6.Entity/DomainModels/Space/Space_Site.cs` 等 9 个文件

> **(v1.1: 改为继承 `BaseBizEntity`，D-C)** 全部继承 `CP6.Entity.BaseBizEntity`（链上自带 `Id` + `Creator/CreateDate/Modifier/ModifyDate` + `TenantId` + `IsDeleted` + `[Timestamp] byte[]? RowVersion`）。因此各实体**不再声明 `public Guid TenantId`**（基类已有，重声明会冲突）、**不再手工加 `[Timestamp] RowVersion`**（基类已有，全 9 表都有乐观锁）。字段严格照 00 §4 的 C# DDL，审计字段名按 D-B（已由基类满足）。下面 9 段代码已按 v1.1 删去这两类重复声明。

- [ ] **Step 1: 写 9 个实体类**

```csharp
// Space_Site.cs
using System.ComponentModel.DataAnnotations.Schema;
namespace CP6.Entity.DomainModels.Space;

[Table("Space_Site")]
public class Space_Site : BaseBizEntity   // (v1.1: BaseEntity→BaseBizEntity；TenantId/RowVersion 由基类带入)
{
    public string  SiteCode { get; set; } = "";
    public string  SiteName { get; set; } = "";
    public string? Address  { get; set; }
    public double? Lng      { get; set; }
    public double? Lat      { get; set; }
    public bool    Enable   { get; set; } = true;
}
```

```csharp
// Space_Floor.cs
[Table("Space_Floor")]
public class Space_Floor : BaseBizEntity   // (v1.1: BaseBizEntity；TenantId/RowVersion 由基类带入)
{
    public Guid    SiteId          { get; set; }
    public int     Level           { get; set; }
    public string  FloorCode       { get; set; } = "";
    public string  FloorName       { get; set; } = "";
    public int     Height          { get; set; } = 6000;
    public string? UnderlayImage   { get; set; }
    public double? UnderlayScale   { get; set; }      // mm/px（描图对齐必需）
    public int     UnderlayOffsetX { get; set; }
    public int     UnderlayOffsetY { get; set; }
    public int     OriginX         { get; set; }
    public int     OriginY         { get; set; }
}
```

```csharp
// Space_Zone.cs
[Table("Space_Zone")]
public class Space_Zone : BaseBizEntity   // (v1.1: BaseBizEntity；TenantId/RowVersion 由基类带入)
{
    public Guid    FloorId  { get; set; }
    public string  ZoneCode { get; set; } = "";
    public string  ZoneName { get; set; } = "";
    public int     ZoneType { get; set; } = 1;        // 1存储2收货3发货4分拣5通道
    public string  Polygon  { get; set; } = "[]";     // 顶点 JSON [[x,y],...] mm
    public string? Color    { get; set; }
    public bool    Enable   { get; set; } = true;
}
```

```csharp
// Space_Aisle.cs
[Table("Space_Aisle")]
public class Space_Aisle : BaseBizEntity   // (v1.1: BaseBizEntity；TenantId/RowVersion 由基类带入)
{
    public Guid   ZoneId     { get; set; }
    public string AisleCode  { get; set; } = "";
    public string Polygon    { get; set; } = "[]";    // 巷道面顶点 JSON
    public string Centerline { get; set; } = "[]";    // 中心线节点 JSON（08 拣货路径消费）
}
```

```csharp
// Space_Rack.cs
[Table("Space_Rack")]
public class Space_Rack : BaseBizEntity   // (v1.1: BaseBizEntity；TenantId/RowVersion 由基类带入，删 using DataAnnotations)
{
    public Guid    ZoneId     { get; set; }           // 必填
    public Guid?   AisleId    { get; set; }           // 可选（有巷道才挂）
    public Guid    FloorId    { get; set; }           // 冗余 = Zone.FloorId
    public Guid?   TemplateId { get; set; }
    public string  RackCode   { get; set; } = "";
    public int     X          { get; set; }           // 锚点角 mm
    public int     Y          { get; set; }
    public int     Z          { get; set; }           // v1 恒 0
    public double  RotationZ  { get; set; }           // 偏航角(度)，绕锚点角
    public int     Cols       { get; set; }
    public int     Levels     { get; set; }
    public int     DepthCount { get; set; } = 1;
    public int     CellW      { get; set; }
    public int     CellH      { get; set; }
    public int     CellD      { get; set; }
    public bool    Enable     { get; set; } = true;
    // (v1.1: 删手工 RowVersion —— 由 BaseBizEntity 提供)
}
```

```csharp
// Space_Location.cs  ——  Id(GUID)=LocationId 稳定主键（D4）
[Table("Space_Location")]
public class Space_Location : BaseBizEntity   // (v1.1: BaseBizEntity；TenantId/RowVersion 由基类带入)
{
    public Guid?   RackId       { get; set; }         // 可空（采纳态 D7 未放置为空）
    public Guid?   FloorId      { get; set; }         // 冗余·可空
    public string? LocationCode { get; set; }         // join key；草稿可空，发布后非空冻结
    public int     CodeOrigin   { get; set; } = 1;    // 1 引擎生成 / 2 采纳导入
    public int?    Col          { get; set; }
    public int?    Level        { get; set; }
    public int?    Depth        { get; set; }
    public int?    AbsX         { get; set; }         // 绝对坐标缓存 mm（重算，00 §6）
    public int?    AbsY         { get; set; }
    public int?    AbsZ         { get; set; }
    public int?    SizeW        { get; set; }
    public int?    SizeH        { get; set; }
    public int?    SizeD        { get; set; }
    public int?    LoadLimit    { get; set; }
    public int?    Capacity     { get; set; }
    public int?    CapacityUom  { get; set; }         // 1托盘2箱3件4体积L
    public bool    Placed       { get; set; }         // = (RackId != null)，正交于 Status
    public int     Status       { get; set; }         // 0草稿1已发布2停用
    public long    Version      { get; set; }         // 按 LocationId 递增（04）
    // (v1.1: 删手工 RowVersion —— 由 BaseBizEntity 提供)
}
```

```csharp
// Space_Template.cs
[Table("Space_Template")]
public class Space_Template : BaseBizEntity   // (v1.1: BaseBizEntity；TenantId/RowVersion 由基类带入)
{
    public string TemplateCode { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public int    TemplateType { get; set; } = 1;     // 1货架2库区
    public string Params       { get; set; } = "{}";
}
```

```csharp
// Space_CodeRule.cs
[Table("Space_CodeRule")]
public class Space_CodeRule : BaseBizEntity   // (v1.1: BaseBizEntity；TenantId/RowVersion 由基类带入)
{
    public string RuleName  { get; set; } = "";
    public int    ScopeType { get; set; }             // 0租户默认1楼层2库区
    public Guid?  ScopeId   { get; set; }             // =1→FloorId =2→ZoneId =0→null
    public string Segments  { get; set; } = "[]";     // 分段定义 JSON（03 章）
    public bool   IsDefault { get; set; }
}
```

```csharp
// Space_Marker.cs
[Table("Space_Marker")]
public class Space_Marker : BaseBizEntity   // (v1.1: BaseBizEntity；TenantId/RowVersion 由基类带入)
{
    public Guid   FloorId    { get; set; }
    public int    X          { get; set; }
    public int    Y          { get; set; }
    public int    Z          { get; set; }
    public int    MarkerType { get; set; } = 1;       // 1文字2图标3区域提示
    public string Text       { get; set; } = "";
    public Guid?  RefRackId  { get; set; }            // 锚到货架（随架移动），删货架 SetNull
}
```

- [ ] **Step 2: 构建确认编译通过**

Run: `dotnet build CP6.Entity/CP6.Entity.csproj`
Expected: Build succeeded

- [ ] **Step 3: 提交**

```bash
git add CP6.Entity/DomainModels/Space/
git commit -m "feat(space): add 9 geometry domain entities (ch00)"
```

---

## Task A-3: DbContext 注册 + 索引配置 + 迁移

**Files:**
- Modify: `CP6.Core/EFDbContext/CP6Context.cs`
- Test: `CP6.Tests/SpacePersistenceTests.cs`
- Create: migration `*_SpaceP1Init`

- [ ] **Step 1: 写失败测试（实体落库往返 + 租户隔离）** `[InMemory 仅测逻辑]`

> **(v1.1)** 测试经 `TestHelper.CreateInMemoryContext(user, tenant)` 注入租户上下文（参 `CP6.Tests/Tenant/TenantFilterTests.cs` 写法）。新增不显式设 `TenantId` 也被自动盖章为当前租户、跨租户不可见——证明已吃真·多租户基建。

```csharp
// SpacePersistenceTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;          // (v1.1) ITenantContext / TenantContext
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;

public class SpacePersistenceTests
{
    // (v1.1) 注入租户上下文；不再用自建桩。同库名 + 不同租户验证隔离。
    private static CP6Context DbFor(string dbName, Guid tenant) =>
        TestHelper.CreateInMemoryContext(user: null, tenant: new TenantContext { CurrentTenantId = tenant });

    [Fact]
    public async Task Site_RoundTrips_AndStampsTenant()
    {
        var t = Guid.NewGuid();
        var db = Guid.NewGuid().ToString();
        // (v1.1) 不显式设 TenantId —— SaveChanges 自动盖当前租户
        using (var ctx = DbFor(db, t)) { ctx.Space_Sites.Add(new Space_Site { SiteCode = "WH1", SiteName = "本社倉庫" }); await ctx.SaveChangesAsync(); }
        using (var ctx = DbFor(db, t))
        {
            var got = await ctx.Space_Sites.SingleAsync();   // (v1.1) 全局过滤自动按 t 限定，无显式 .Where
            Assert.Equal("WH1", got.SiteCode);
            Assert.Equal(t, got.TenantId);                   // 已自动盖章
        }
        // 另一个租户看不到（硬墙）
        using (var ctx = DbFor(db, Guid.NewGuid())) Assert.Empty(await ctx.Space_Sites.ToListAsync());
    }
}
```

- [ ] **Step 2: 跑红**

Run: `dotnet test CP6.Tests --filter SpacePersistenceTests`
Expected: FAIL — `CP6Context` 无 `Space_Sites` 属性（编译错误）

- [ ] **Step 3: 加 9 个 DbSet + OnModelCreating 配置**

在 `CP6Context.cs` 类体加（DbSet 区，仿既有 WMS 区注释风格）：

```csharp
    // ───── Space 空间数字底座 P1（ch00 9 表）─────
    public DbSet<CP6.Entity.DomainModels.Space.Space_Site>     Space_Sites     { get; set; }
    public DbSet<CP6.Entity.DomainModels.Space.Space_Floor>    Space_Floors    { get; set; }
    public DbSet<CP6.Entity.DomainModels.Space.Space_Zone>     Space_Zones     { get; set; }
    public DbSet<CP6.Entity.DomainModels.Space.Space_Aisle>    Space_Aisles    { get; set; }
    public DbSet<CP6.Entity.DomainModels.Space.Space_Rack>     Space_Racks     { get; set; }
    public DbSet<CP6.Entity.DomainModels.Space.Space_Location> Space_Locations { get; set; }
    public DbSet<CP6.Entity.DomainModels.Space.Space_Template> Space_Templates { get; set; }
    public DbSet<CP6.Entity.DomainModels.Space.Space_CodeRule> Space_CodeRules { get; set; }
    public DbSet<CP6.Entity.DomainModels.Space.Space_Marker>   Space_Markers   { get; set; }
```

在 `OnModelCreating(ModelBuilder b)` 内加 Space 索引配置。

> **(v1.1: 不要手写全局过滤；唯一索引可不带 TenantId 前缀)** `CP6Context.OnModelCreating` **末尾已有反射块**：对所有 `BaseTenantEntity` 子类（含 Space 9 表）①注册 `HasQueryFilter(x => x.TenantId == CurrentTenantId)`，②把"单列全局唯一"索引升级为 `(TenantId, …)` 复合唯一。所以**严禁手写 `HasQueryFilter`**（会与反射块双注册）；唯一索引**写单列 `.IsUnique()` 即可，反射自动加 TenantId 前缀**（已显式写 `(TenantId, …)` 的也行——反射检测到含 TenantId 会跳过，不重复）。下方代码保留显式 `(TenantId, …)` 仅为可读性，等价。**唯一例外**：`Space_Location` 的过滤唯一索引 `WHERE LocationCode IS NOT NULL` 是 Space 业务专属，**必须显式声明**（含 TenantId 前缀 + filter），反射不替你造它。

```csharp
    // ── Space P1 索引（00 §4）。(v1.1) 全局过滤 + (TenantId,…) 唯一升级由反射块自动；下方显式前缀=可读性等价。──
    var sp = b.Entity<CP6.Entity.DomainModels.Space.Space_Site>();
    sp.HasIndex(x => new { x.TenantId, x.SiteCode }).IsUnique();

    b.Entity<CP6.Entity.DomainModels.Space.Space_Floor>(e => {
        e.HasIndex(x => new { x.TenantId, x.SiteId, x.FloorCode }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.SiteId });
    });
    b.Entity<CP6.Entity.DomainModels.Space.Space_Zone>(e => {
        e.HasIndex(x => new { x.TenantId, x.FloorId, x.ZoneCode }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.FloorId });
    });
    b.Entity<CP6.Entity.DomainModels.Space.Space_Aisle>(e => {
        e.HasIndex(x => new { x.TenantId, x.ZoneId, x.AisleCode }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.ZoneId });
    });
    b.Entity<CP6.Entity.DomainModels.Space.Space_Rack>(e => {
        e.HasIndex(x => new { x.TenantId, x.ZoneId, x.RackCode }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.ZoneId });
        e.HasIndex(x => new { x.TenantId, x.AisleId });
        e.HasIndex(x => new { x.TenantId, x.FloorId });
    });
    b.Entity<CP6.Entity.DomainModels.Space.Space_Location>(e => {
        // ★过滤唯一索引：非空码租户内唯一；草稿期 NULL 不互撞（00 §4.6 / 03 §7）
        e.HasIndex(x => new { x.TenantId, x.LocationCode }).IsUnique()
         .HasFilter("[LocationCode] IS NOT NULL");
        e.HasIndex(x => new { x.TenantId, x.RackId });
        e.HasIndex(x => new { x.TenantId, x.FloorId });
        e.HasIndex(x => new { x.TenantId, x.Status });
    });
    b.Entity<CP6.Entity.DomainModels.Space.Space_Template>()
        .HasIndex(x => new { x.TenantId, x.TemplateCode }).IsUnique();
    b.Entity<CP6.Entity.DomainModels.Space.Space_CodeRule>()
        .HasIndex(x => new { x.TenantId, x.ScopeType, x.ScopeId });
    b.Entity<CP6.Entity.DomainModels.Space.Space_Marker>()
        .HasIndex(x => new { x.TenantId, x.FloorId });
```

> **注**：删除策略（00 §3.2 Restrict/SetNull）在 P1 用**应用层服务校验**强制（Task B-5），不配 EF 级联——CP6 既有外键多走应用层校验、DB 兜底，保持一致，避免 EF 默认级联误删。

- [ ] **Step 4: 跑绿**

Run: `dotnet test CP6.Tests --filter SpacePersistenceTests`
Expected: PASS

- [ ] **Step 5: 生成迁移**

Run: `dotnet ef migrations add SpaceP1Init -p CP6.Core -s CP6.WebApi`
Expected: 生成 `*_SpaceP1Init.cs`；打开确认含 9 表 + 过滤唯一索引 `filter: "[LocationCode] IS NOT NULL"`。**(v1.1)** 每表均含 `TenantId` + `IsDeleted` + `RowVersion`（共 9 套，来自 `BaseBizEntity`，非原稿"两个 RowVersion"）；所有唯一索引应为 `(TenantId, …)` 复合（反射升级的产物）。

- [ ] **Step 6: 提交**

```bash
git add CP6.Core/EFDbContext/CP6Context.cs CP6.Core/Migrations/ CP6.Tests/SpacePersistenceTests.cs
git commit -m "feat(space): register 9 DbSets + indexes + SpaceP1Init migration (ch00)"
```

---

# Phase B — 主数据服务 + 几何 + 场景（00 章可用）

> 服务层统一约定 **(v1.1 已返改——以下为新约定，下文 Phase B/C/D 全部代码样例据此读)**：构造**只注入 `CP6Context db`**（需要当前租户 Id 的极少数场景才追加 `ITenantContext`，取 `.CurrentTenantId`）。**所有查询删除显式 `.Where(x => x.TenantId == …)`** —— `CP6Context` 已对 `BaseTenantEntity` 子类注册全局查询过滤，自动 `WHERE TenantId == CurrentTenantId`。**所有创建删除 `entity.TenantId = …`** —— `SaveChanges` 写入盖章自动补当前租户；仅保留 `entity.Creator = currentUser; entity.CreateDate = DateTime.Now`。控制器仿 `MachineController`：`[ApiController][Route("api/space")][Authorize]`，`CurrentUser => User?.Identity?.Name`，返回 `{ code, message, data }`，业务异常 `InvalidOperationException` → 400。
>
> **(v1.1: 全局映射，适用于下文每段代码样例)** 下文服务/测试样例仍写有 `ISpaceTenantContext`/`DefaultSpaceTenantContext`/`_tenant.TenantId`/`tid`/`.Where(x => x.TenantId == tid)`/`entity.TenantId = tid` 等——**一律按下述读，无需逐行重写**：① `ISpaceTenantContext`→`ITenantContext`、`DefaultSpaceTenantContext`→`TenantContext`、`.TenantId`→`.CurrentTenantId`；② 凡 `.Where(x => x.TenantId == tid …)` → **删除该 TenantId 条件**（保留其余条件；若整句仅此一条件则该 `.Where` 整删），因全局过滤自动加；③ 凡建实体的 `TenantId = tid,` → **删除**（盖章自动）；④ 跨实体 JOIN/`Contains` 子查询同理删 TenantId 条件（两侧都被全局过滤）。测试构造从 `new DefaultSpaceTenantContext()` → `TestHelper.CreateInMemoryContext(user, new TenantContext{ CurrentTenantId = t })`。

## Task B-1: 几何坐标重算 `LocationGeometryService`（00 §6 —— 全模块最核心公式，先做）

**Files:**
- Create: `CP6.Core/Services/Space/LocationGeometryService.cs`
- Test: `CP6.Tests/LocationGeometryServiceTests.cs`

- [ ] **Step 1: 写失败测试（坐标公式 + 重算只改坐标不改码）** `[InMemory 仅测逻辑]`

```csharp
// LocationGeometryServiceTests.cs
public class LocationGeometryServiceTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void ComputeAbs_NoRotation_AnchorIsMinCorner()
    {
        // 锚点(1000,2000)，单格 1200×1500×1000，Col2/Level2/Depth1，RotationZ=0
        var rack = new Space_Rack { X = 1000, Y = 2000, Z = 0, RotationZ = 0,
            CellW = 1200, CellH = 1500, CellD = 1000 };
        var (x, y, z) = LocationGeometryService.ComputeAbs(rack, col: 2, level: 2, depth: 1);
        // localX=(2-0.5)*1200=1800; localY=(1-0.5)*1000=500; localZ=(2-0.5)*1500=2250
        Assert.Equal(1000 + 1800, x);
        Assert.Equal(2000 + 500,  y);
        Assert.Equal(0 + 2250,    z);
    }

    [Fact]
    public void ComputeAbs_Rotate90_RotatesAroundAnchor()
    {
        var rack = new Space_Rack { X = 0, Y = 0, Z = 0, RotationZ = 90,
            CellW = 1000, CellH = 1000, CellD = 1000 };
        var (x, y, _) = LocationGeometryService.ComputeAbs(rack, col: 1, level: 1, depth: 1);
        // localX=500, localY=500; θ=90°: x=500cos90-500sin90=-500; y=500sin90+500cos90=500
        Assert.Equal(-500, x);
        Assert.Equal(500,  y);
    }

    [Fact]
    public async Task Recalc_OnlyUpdatesCoords_NotCodeNorStatus()
    {
        var t = DefaultSpaceTenantContext.DefaultTenant;
        using var db = Db();
        var rackId = Guid.NewGuid();
        db.Space_Racks.Add(new Space_Rack { Id = rackId, TenantId = t, X = 0, Y = 0,
            CellW = 1000, CellH = 1000, CellD = 1000, Cols = 1, Levels = 1, DepthCount = 1 });
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), TenantId = t, RackId = rackId,
            Placed = true, Col = 1, Level = 1, Depth = 1, LocationCode = "A-01-01-01", Status = 1, Version = 3 });
        await db.SaveChangesAsync();

        var svc = new LocationGeometryService(db, new DefaultSpaceTenantContext());
        await svc.RecalcRackLocationsAsync(rackId);

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(500, loc.AbsX);           // 坐标已算
        Assert.Equal("A-01-01-01", loc.LocationCode); // 码不变
        Assert.Equal(1, loc.Status);           // 状态不变
        Assert.Equal(3, loc.Version);          // 版本不变（纯几何不发布、不升版）
    }
}
```

- [ ] **Step 2: 跑红** → Run: `dotnet test CP6.Tests --filter LocationGeometryServiceTests` → FAIL（类不存在）

- [ ] **Step 3: 实现**

```csharp
// LocationGeometryService.cs
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

/// <summary>库位绝对坐标缓存重算（00 §6.2）。几何可动、码不漂移：只改 AbsX/Y/Z，不动 Code/Status/Version。</summary>
public class LocationGeometryService
{
    private readonly CP6Context _db;
    private readonly ISpaceTenantContext _tenant;
    public LocationGeometryService(CP6Context db, ISpaceTenantContext tenant) { _db = db; _tenant = tenant; }

    /// <summary>货架局部 → floor 局部坐标（00 §6.1）。锚点角 + 绕角点旋转。索引 1..N。</summary>
    public static (int x, int y, int z) ComputeAbs(Space_Rack rack, int col, int level, int depth)
    {
        double localX = (col   - 0.5) * rack.CellW;
        double localZ = (level - 0.5) * rack.CellH;
        double localY = (depth - 0.5) * rack.CellD;
        double th = rack.RotationZ * Math.PI / 180.0;
        double cos = Math.Cos(th), sin = Math.Sin(th);
        int absX = rack.X + (int)Math.Round(localX * cos - localY * sin);
        int absY = rack.Y + (int)Math.Round(localX * sin + localY * cos);
        int absZ = rack.Z + (int)Math.Round(localZ);
        return (absX, absY, absZ);
    }

    /// <summary>重算某货架下全部「已放置」库位坐标缓存。不触发 LocationPublished（载荷无几何，04）。</summary>
    public async Task RecalcRackLocationsAsync(Guid rackId)
    {
        var tid = _tenant.TenantId;
        var rack = await _db.Space_Racks.FirstOrDefaultAsync(r => r.Id == rackId && r.TenantId == tid)
                   ?? throw new InvalidOperationException("E-SPACE-002");
        var locs = await _db.Space_Locations
            .Where(l => l.TenantId == tid && l.RackId == rackId && l.Placed).ToListAsync();
        foreach (var l in locs)
            (l.AbsX, l.AbsY, l.AbsZ) = ComputeAbs(rack, l.Col!.Value, l.Level!.Value, l.Depth!.Value);
        await _db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: 跑绿** → Run: `dotnet test CP6.Tests --filter LocationGeometryServiceTests` → PASS

- [ ] **Step 5: 提交**

```bash
git add CP6.Core/Services/Space/LocationGeometryService.cs CP6.Tests/LocationGeometryServiceTests.cs
git commit -m "feat(space): location absolute-coord recalc service (ch00 §6)"
```

---

## Task B-2: 主数据 DTO

**Files:**
- Create: `CP6.Entity/DTOs/Space/SpaceMasterDtos.cs`, `CP6.Entity/DTOs/Space/SceneDto.cs`

- [ ] **Step 1: 写 DTO（CRUD 请求 + scene 聚合响应）**

```csharp
// SpaceMasterDtos.cs — 仿既有 DTO 风格（仅列关键，余字段镜像实体）
namespace CP6.Entity.DTOs.Space;

public class SiteDto      { public Guid? Id; public string SiteCode=""; public string SiteName=""; public string? Address; public double? Lng; public double? Lat; public bool Enable=true; }
public class FloorDto     { public Guid? Id; public Guid SiteId; public int Level; public string FloorCode=""; public string FloorName=""; public int Height=6000; public string? UnderlayImage; public double? UnderlayScale; public int UnderlayOffsetX; public int UnderlayOffsetY; public int OriginX; public int OriginY; }
public class ZoneDto      { public Guid? Id; public Guid FloorId; public string ZoneCode=""; public string ZoneName=""; public int ZoneType=1; public string Polygon="[]"; public string? Color; public bool Enable=true; }
public class AisleDto     { public Guid? Id; public Guid ZoneId; public string AisleCode=""; public string Polygon="[]"; public string Centerline="[]"; }
public class RackDto      { public Guid? Id; public Guid ZoneId; public Guid? AisleId; public Guid? TemplateId; public string RackCode=""; public int X; public int Y; public int Z; public double RotationZ; public int Cols; public int Levels; public int DepthCount=1; public int CellW; public int CellH; public int CellD; public bool Enable=true; public byte[]? RowVersion; }
```

```csharp
// SceneDto.cs — /floor/{id}/scene 聚合（05 渲染一次拉全）
namespace CP6.Entity.DTOs.Space;

public class SceneDto
{
    public Guid FloorId;
    public List<ZoneDto>   Zones   = new();
    public List<AisleDto>  Aisles  = new();
    public List<RackDto>   Racks   = new();
    public List<SceneLocationDto> Locations = new();   // 仅 Placed=true
    public List<MarkerDto> Markers = new();
}
public class SceneLocationDto { public Guid Id; public Guid RackId; public string? LocationCode; public int Col,Level,Depth,AbsX,AbsY,AbsZ,SizeW,SizeH,SizeD; public int Status; }
public class MarkerDto { public Guid? Id; public Guid FloorId; public int X,Y,Z; public int MarkerType=1; public string Text=""; public Guid? RefRackId; }
```

- [ ] **Step 2: 构建** → Run: `dotnet build CP6.Entity/CP6.Entity.csproj` → succeeded

- [ ] **Step 3: 提交**

```bash
git add CP6.Entity/DTOs/Space/
git commit -m "feat(space): master-data + scene DTOs (ch00)"
```

---

## Task B-3: 主数据 CRUD 服务（Site/Floor/Zone/Aisle —— 直 CRUD）

**Files:**
- Create: `CP6.Core/Services/Space/ISpaceMasterService.cs`, `SpaceMasterService.cs`
- Test: `CP6.Tests/SpaceMasterServiceTests.cs`

> 这 4 类是直 CRUD（编码作用域内唯一校验 + 创建落 TenantId/Creator）。Rack（触发几何重算）、删除护栏、scene/unplaced 分到 B-4/B-5/B-6。

- [ ] **Step 1: 写失败测试（含 Site 编码唯一 E-SPACE-001、Zone 多边形顶点<3 E-SPACE-006）** `[InMemory 仅测逻辑]`

```csharp
public class SpaceMasterServiceTests
{
    private static (CP6Context, SpaceMasterService) Make()
    {
        var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new SpaceMasterService(db, new DefaultSpaceTenantContext(),
            new LocationGeometryService(db, new DefaultSpaceTenantContext())));
    }

    [Fact]
    public async Task CreateSite_DuplicateCode_Throws_E001()
    {
        var (db, svc) = Make();
        await svc.CreateSiteAsync(new SiteDto { SiteCode = "WH1", SiteName = "a" }, "u");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateSiteAsync(new SiteDto { SiteCode = "WH1", SiteName = "b" }, "u"));
        Assert.Equal("E-SPACE-001", ex.Message);
    }

    [Fact]
    public async Task CreateZone_PolygonLessThan3Vertices_Throws_E006()
    {
        var (db, svc) = Make();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateZoneAsync(new ZoneDto { FloorId = Guid.NewGuid(), ZoneCode = "A", Polygon = "[[0,0],[1,1]]" }, "u"));
        Assert.Equal("E-SPACE-006", ex.Message);
    }
}
```

- [ ] **Step 2: 跑红** → FAIL（类/方法不存在）

- [ ] **Step 3: 实现接口 + Site/Floor/Zone/Aisle CRUD**

```csharp
// ISpaceMasterService.cs（仅列本任务方法，B-4/5/6 续加）
public interface ISpaceMasterService
{
    Task<Guid> CreateSiteAsync(SiteDto dto, string? user);
    Task UpdateSiteAsync(Guid id, SiteDto dto, string? user);
    Task<List<SiteDto>> ListSitesAsync();
    Task<Guid> CreateFloorAsync(FloorDto dto, string? user);
    Task UpdateFloorAsync(Guid id, FloorDto dto, string? user);
    Task<List<FloorDto>> ListFloorsAsync(Guid siteId);
    Task<Guid> CreateZoneAsync(ZoneDto dto, string? user);
    Task UpdateZoneAsync(Guid id, ZoneDto dto, string? user);
    Task<List<ZoneDto>> ListZonesAsync(Guid floorId);
    Task<Guid> CreateAisleAsync(AisleDto dto, string? user);
    Task UpdateAisleAsync(Guid id, AisleDto dto, string? user);
    Task<List<AisleDto>> ListAislesAsync(Guid zoneId);
}
```

```csharp
// SpaceMasterService.cs（本任务部分；省略 Update/List 的对称实现——按同模式：取 TenantId 过滤、改字段、落 Modifier/ModifyDate）
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

public class SpaceMasterService : ISpaceMasterService
{
    private readonly CP6Context _db;
    private readonly ISpaceTenantContext _t;
    private readonly LocationGeometryService _geo;
    public SpaceMasterService(CP6Context db, ISpaceTenantContext t, LocationGeometryService geo)
    { _db = db; _t = t; _geo = geo; }

    public async Task<Guid> CreateSiteAsync(SiteDto d, string? user)
    {
        var tid = _t.TenantId;
        if (await _db.Space_Sites.AnyAsync(x => x.TenantId == tid && x.SiteCode == d.SiteCode))
            throw new InvalidOperationException("E-SPACE-001");
        var e = new Space_Site { Id = Guid.NewGuid(), TenantId = tid, SiteCode = d.SiteCode,
            SiteName = d.SiteName, Address = d.Address, Lng = d.Lng, Lat = d.Lat, Enable = d.Enable,
            Creator = user, CreateDate = DateTime.Now };
        _db.Space_Sites.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    public async Task<Guid> CreateZoneAsync(ZoneDto d, string? user)
    {
        var tid = _t.TenantId;
        ValidatePolygon(d.Polygon);                                   // E-SPACE-006
        if (await _db.Space_Zones.AnyAsync(x => x.TenantId == tid && x.FloorId == d.FloorId && x.ZoneCode == d.ZoneCode))
            throw new InvalidOperationException("E-SPACE-001");
        var e = new Space_Zone { Id = Guid.NewGuid(), TenantId = tid, FloorId = d.FloorId,
            ZoneCode = d.ZoneCode, ZoneName = d.ZoneName, ZoneType = d.ZoneType, Polygon = d.Polygon,
            Color = d.Color, Enable = d.Enable, Creator = user, CreateDate = DateTime.Now };
        _db.Space_Zones.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    private static void ValidatePolygon(string json)
    {
        var pts = JsonSerializer.Deserialize<List<List<int>>>(json) ?? new();
        if (pts.Count < 3) throw new InvalidOperationException("E-SPACE-006");
    }
    // CreateFloorAsync / CreateAisleAsync / 各 Update*/List* 同模式实现（floor 唯一域=(SiteId,FloorCode)，aisle=(ZoneId,AisleCode)）
}
```

> **实现者注**：Floor/Aisle 的 Create 与 Site 同构（唯一校验作用域换字段）；所有 `Update*` 先 `.FirstOrDefault(x => x.Id==id && x.TenantId==tid)`（无则 `InvalidOperationException("E-SPACE-001"?)` 用 NotFound 语义）、改字段、`Modifier=user; ModifyDate=DateTime.Now`、保存。`List*` 按父 Id + TenantId 过滤投影成 DTO。

- [ ] **Step 4: 跑绿** → Run: `dotnet test CP6.Tests --filter SpaceMasterServiceTests` → PASS

- [ ] **Step 5: 提交**

```bash
git add CP6.Core/Services/Space/ISpaceMasterService.cs CP6.Core/Services/Space/SpaceMasterService.cs CP6.Tests/SpaceMasterServiceTests.cs
git commit -m "feat(space): Site/Floor/Zone/Aisle CRUD service (ch00)"
```

---

## Task B-4: 货架 CRUD + 改位姿触发几何重算（00 §6.2 / §8.1）

**Files:**
- Modify: `ISpaceMasterService.cs`, `SpaceMasterService.cs`
- Test: `CP6.Tests/SpaceMasterServiceTests.cs`

- [ ] **Step 1: 追加失败测试（改货架位姿后，其下已放置库位坐标被重算）**

```csharp
    [Fact]
    public async Task UpdateRack_MovePosition_RecalcsLocations()
    {
        var (db, svc) = Make();
        var tid = DefaultSpaceTenantContext.DefaultTenant;
        var rackId = await svc.CreateRackAsync(new RackDto { ZoneId = Guid.NewGuid(), RackCode = "R1",
            X = 0, Y = 0, Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000 }, "u");
        // 模拟其下已有一个放置库位（编码引擎/发布在 C/D 阶段；此处直插）
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), TenantId = tid, RackId = rackId,
            Placed = true, Col = 1, Level = 1, Depth = 1 });
        await db.SaveChangesAsync();

        var rack = await db.Space_Racks.SingleAsync();
        await svc.UpdateRackAsync(rackId, new RackDto { ZoneId = rack.ZoneId, RackCode = "R1",
            X = 5000, Y = 0, Cols = 1, Levels = 1, DepthCount = 1, CellW = 1000, CellH = 1000, CellD = 1000,
            RowVersion = rack.RowVersion }, "u");

        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(5000 + 500, loc.AbsX);   // 跟随货架移动重算
    }
```

- [ ] **Step 2: 跑红** → FAIL（`CreateRackAsync`/`UpdateRackAsync` 不存在）

- [ ] **Step 3: 实现 Rack CRUD**

接口加：
```csharp
    Task<Guid> CreateRackAsync(RackDto dto, string? user);
    Task UpdateRackAsync(Guid id, RackDto dto, string? user);   // 位姿/尺寸变更后调 _geo.RecalcRackLocationsAsync
    Task<List<RackDto>> ListRacksAsync(Guid zoneId);
```
实现：
```csharp
    public async Task<Guid> CreateRackAsync(RackDto d, string? user)
    {
        var tid = _t.TenantId;
        if (d.ZoneId == Guid.Empty) throw new InvalidOperationException("E-SPACE-002");   // 货架必须归库区
        if (d.Cols < 1 || d.Levels < 1 || d.DepthCount < 1 || d.CellW <= 0 || d.CellH <= 0 || d.CellD <= 0)
            throw new InvalidOperationException("E-SPACE-002");                             // 落库不变量
        if (await _db.Space_Racks.AnyAsync(x => x.TenantId == tid && x.ZoneId == d.ZoneId && x.RackCode == d.RackCode))
            throw new InvalidOperationException("E-SPACE-001");
        var floorId = await _db.Space_Zones.Where(z => z.Id == d.ZoneId && z.TenantId == tid)
                                           .Select(z => z.FloorId).FirstOrDefaultAsync();   // 冗余回填
        var e = new Space_Rack { Id = Guid.NewGuid(), TenantId = tid, ZoneId = d.ZoneId, AisleId = d.AisleId,
            FloorId = floorId, TemplateId = d.TemplateId, RackCode = d.RackCode, X = d.X, Y = d.Y, Z = d.Z,
            RotationZ = d.RotationZ, Cols = d.Cols, Levels = d.Levels, DepthCount = d.DepthCount,
            CellW = d.CellW, CellH = d.CellH, CellD = d.CellD, Enable = d.Enable,
            Creator = user, CreateDate = DateTime.Now };
        _db.Space_Racks.Add(e);
        await _db.SaveChangesAsync();
        return e.Id;
    }

    public async Task UpdateRackAsync(Guid id, RackDto d, string? user)
    {
        var tid = _t.TenantId;
        var e = await _db.Space_Racks.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid)
                ?? throw new InvalidOperationException("E-SPACE-002");
        _db.Entry(e).Property(x => x.RowVersion).OriginalValue = d.RowVersion;   // 乐观并发（真库生效）
        e.X = d.X; e.Y = d.Y; e.Z = d.Z; e.RotationZ = d.RotationZ;
        e.Cols = d.Cols; e.Levels = d.Levels; e.DepthCount = d.DepthCount;
        e.CellW = d.CellW; e.CellH = d.CellH; e.CellD = d.CellD; e.AisleId = d.AisleId;
        e.Modifier = user; e.ModifyDate = DateTime.Now;
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("E-SPACE-009"); }
        await _geo.RecalcRackLocationsAsync(id);   // ★位姿/尺寸变 → 重算库位坐标（码不变，不发布）
    }
```

> **实现者注**：本稿 P1 不含"改 Cols/Levels 增删格子→补码/停用库位"的自动联动（00 §6.2 表第 4 行）——那需编码引擎(C)+发布(D)就绪。建议作为 D 阶段后的**整合任务**或 P1 收尾补；本稿在 D-8 留了挂点说明。

- [ ] **Step 4: 跑绿** → PASS

- [ ] **Step 5: 提交**

```bash
git add -A && git commit -m "feat(space): Rack CRUD with geometry recalc on move/resize (ch00 §6.2)"
```

---

## Task B-5: 删除护栏（00 §3.2 Restrict / SetNull）

**Files:**
- Modify: `ISpaceMasterService.cs`, `SpaceMasterService.cs`
- Test: `CP6.Tests/SpaceMasterServiceTests.cs`

- [ ] **Step 1: 追加失败测试**

```csharp
    [Fact]
    public async Task DeleteRack_WithLocations_Throws_E003()
    {
        var (db, svc) = Make();
        var tid = DefaultSpaceTenantContext.DefaultTenant;
        var rackId = Guid.NewGuid();
        db.Space_Racks.Add(new Space_Rack { Id = rackId, TenantId = tid, ZoneId = Guid.NewGuid(), RackCode = "R" });
        db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), TenantId = tid, RackId = rackId, Placed = true });
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteRackAsync(rackId));
        Assert.Equal("E-SPACE-003", ex.Message);
    }

    [Fact]
    public async Task DeleteAisle_SetsNullRackAisleId()
    {
        var (db, svc) = Make();
        var tid = DefaultSpaceTenantContext.DefaultTenant;
        var aisleId = Guid.NewGuid();
        db.Space_Aisles.Add(new Space_Aisle { Id = aisleId, TenantId = tid, ZoneId = Guid.NewGuid(), AisleCode = "L1" });
        db.Space_Racks.Add(new Space_Rack { Id = Guid.NewGuid(), TenantId = tid, ZoneId = Guid.NewGuid(), AisleId = aisleId, RackCode = "R" });
        await db.SaveChangesAsync();
        await svc.DeleteAisleAsync(aisleId);
        Assert.Null((await db.Space_Racks.SingleAsync()).AisleId);   // 货架 AisleId 置空、货架保留
        Assert.Empty(db.Space_Aisles);
    }
```

- [ ] **Step 2: 跑红** → FAIL

- [ ] **Step 3: 实现删除护栏**（接口加 `DeleteSiteAsync/DeleteFloorAsync/DeleteZoneAsync/DeleteAisleAsync/DeleteRackAsync`）

```csharp
    // Site/Floor/Zone：有子→E-SPACE-007；Rack：有库位→E-SPACE-003；Aisle：SetNull 其下 Rack.AisleId
    public async Task DeleteRackAsync(Guid id)
    {
        var tid = _t.TenantId;
        if (await _db.Space_Locations.AnyAsync(l => l.TenantId == tid && l.RackId == id))
            throw new InvalidOperationException("E-SPACE-003");
        var e = await _db.Space_Racks.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid);
        if (e != null) { _db.Space_Racks.Remove(e); await _db.SaveChangesAsync(); }
    }

    public async Task DeleteAisleAsync(Guid id)
    {
        var tid = _t.TenantId;
        var racks = await _db.Space_Racks.Where(r => r.TenantId == tid && r.AisleId == id).ToListAsync();
        foreach (var r in racks) r.AisleId = null;                 // SetNull
        var e = await _db.Space_Aisles.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tid);
        if (e != null) _db.Space_Aisles.Remove(e);
        await _db.SaveChangesAsync();
    }
    // DeleteSite/Floor/Zone：先 AnyAsync 子表（Floor/Zone+Marker/Aisle+Rack）→ 有则 E-SPACE-007，否则 Remove
```

> **注**：删货架/巷道触及**已发布**库位的护栏（E-SPACE-402/403、路径 A/B）属 04，落 Task D-7（删除 API 带 `?mode=`）；本任务只做 00 §3.2 的基础物理删护栏。

- [ ] **Step 4: 跑绿** → PASS
- [ ] **Step 5: 提交** → `git add -A && git commit -m "feat(space): delete guards Restrict/SetNull (ch00 §3.2)"`

---

## Task B-6: 场景聚合 + 待绑定列表 + 控制器（00 §9）

**Files:**
- Modify: `ISpaceMasterService.cs`, `SpaceMasterService.cs`
- Create: `CP6.WebApi/Controllers/Space/SpaceMasterController.cs`
- Test: `CP6.Tests/SpaceMasterServiceTests.cs`

- [ ] **Step 1: 追加失败测试（scene 只含 Placed=true；unplaced 只含 Status=1∧Placed=false）**

```csharp
    [Fact]
    public async Task GetScene_OnlyPlacedLocations()
    {
        var (db, svc) = Make();
        var tid = DefaultSpaceTenantContext.DefaultTenant;
        var floorId = Guid.NewGuid();
        db.Space_Locations.AddRange(
            new Space_Location { Id = Guid.NewGuid(), TenantId = tid, FloorId = floorId, RackId = Guid.NewGuid(), Placed = true, Status = 1 },
            new Space_Location { Id = Guid.NewGuid(), TenantId = tid, FloorId = null, Placed = false, Status = 1, CodeOrigin = 2 }); // 采纳未放置
        await db.SaveChangesAsync();
        var scene = await svc.GetSceneAsync(floorId);
        Assert.Single(scene.Locations);            // 未放置的不进场景
        var unplaced = await svc.GetUnplacedAsync(floorId);
        // 注：未放置库位 FloorId 为空，按 D7 进"待绑定列表"——本 P1 简化为按租户全量待绑定
        Assert.Single(unplaced);
    }
```

- [ ] **Step 2: 跑红** → FAIL

- [ ] **Step 3: 实现 scene/unplaced**

```csharp
    public async Task<SceneDto> GetSceneAsync(Guid floorId)
    {
        var tid = _t.TenantId;
        var scene = new SceneDto { FloorId = floorId };
        scene.Zones  = await _db.Space_Zones.Where(z => z.TenantId == tid && z.FloorId == floorId)
            .Select(z => new ZoneDto { Id = z.Id, FloorId = z.FloorId, ZoneCode = z.ZoneCode, ZoneName = z.ZoneName, ZoneType = z.ZoneType, Polygon = z.Polygon, Color = z.Color }).ToListAsync();
        var zoneIds = scene.Zones.Select(z => z.Id!.Value).ToList();
        scene.Aisles = await _db.Space_Aisles.Where(a => a.TenantId == tid && zoneIds.Contains(a.ZoneId))
            .Select(a => new AisleDto { Id = a.Id, ZoneId = a.ZoneId, AisleCode = a.AisleCode, Polygon = a.Polygon, Centerline = a.Centerline }).ToListAsync();
        scene.Racks  = await _db.Space_Racks.Where(r => r.TenantId == tid && r.FloorId == floorId)
            .Select(r => new RackDto { Id = r.Id, ZoneId = r.ZoneId, AisleId = r.AisleId, RackCode = r.RackCode, X = r.X, Y = r.Y, Z = r.Z, RotationZ = r.RotationZ, Cols = r.Cols, Levels = r.Levels, DepthCount = r.DepthCount, CellW = r.CellW, CellH = r.CellH, CellD = r.CellD }).ToListAsync();
        scene.Locations = await _db.Space_Locations.Where(l => l.TenantId == tid && l.FloorId == floorId && l.Placed)
            .Select(l => new SceneLocationDto { Id = l.Id, RackId = l.RackId!.Value, LocationCode = l.LocationCode, Col = l.Col ?? 0, Level = l.Level ?? 0, Depth = l.Depth ?? 0, AbsX = l.AbsX ?? 0, AbsY = l.AbsY ?? 0, AbsZ = l.AbsZ ?? 0, SizeW = l.SizeW ?? 0, SizeH = l.SizeH ?? 0, SizeD = l.SizeD ?? 0, Status = l.Status }).ToListAsync();
        scene.Markers = await _db.Space_Markers.Where(m => m.TenantId == tid && m.FloorId == floorId)
            .Select(m => new MarkerDto { Id = m.Id, FloorId = m.FloorId, X = m.X, Y = m.Y, Z = m.Z, MarkerType = m.MarkerType, Text = m.Text, RefRackId = m.RefRackId }).ToListAsync();
        return scene;
    }

    public async Task<List<SceneLocationDto>> GetUnplacedAsync(Guid floorId)
    {
        var tid = _t.TenantId;   // 采纳态待绑定：Status=1 ∧ Placed=false（FloorId 为空，不按 floor 过滤）
        return await _db.Space_Locations.Where(l => l.TenantId == tid && l.Status == 1 && !l.Placed)
            .Select(l => new SceneLocationDto { Id = l.Id, LocationCode = l.LocationCode, Status = l.Status }).ToListAsync();
    }
```

- [ ] **Step 4: 跑绿** → PASS

- [ ] **Step 5: 写控制器**

```csharp
// SpaceMasterController.cs
using CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Route("api/space")]
[Authorize]
public class SpaceMasterController : ControllerBase
{
    private readonly ISpaceMasterService _svc;
    public SpaceMasterController(ISpaceMasterService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null, string msg = "OK") => Ok(new { code = 0, message = msg, data });

    [HttpGet("site")]   public async Task<IActionResult> Sites() => Ok2(await _svc.ListSitesAsync());
    [HttpPost("site")]  public async Task<IActionResult> CreateSite([FromBody] SiteDto d) { try { return Ok2(new { id = await _svc.CreateSiteAsync(d, CurrentUser) }); } catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); } }
    [HttpPut("site/{id}")] public async Task<IActionResult> UpdateSite(Guid id, [FromBody] SiteDto d) { try { await _svc.UpdateSiteAsync(id, d, CurrentUser); return Ok2(); } catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); } }
    [HttpDelete("site/{id}")] public async Task<IActionResult> DeleteSite(Guid id) { try { await _svc.DeleteSiteAsync(id); return Ok2(); } catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); } }

    // floor?siteId= / zone?floorId= / aisle?zoneId= / rack?zoneId= 同模式（GET 带父 Id 查询参数）
    [HttpGet("floor")] public async Task<IActionResult> Floors([FromQuery] Guid siteId) => Ok2(await _svc.ListFloorsAsync(siteId));
    // ... Floor/Zone/Aisle/Rack 的 POST/PUT/DELETE 同 Site 模式 ...

    [HttpGet("floor/{id}/scene")] public async Task<IActionResult> Scene(Guid id) => Ok2(await _svc.GetSceneAsync(id));
    [HttpGet("location/unplaced")] public async Task<IActionResult> Unplaced([FromQuery] Guid floorId) => Ok2(await _svc.GetUnplacedAsync(floorId));
    [HttpGet("location")] public async Task<IActionResult> Locations([FromQuery] Guid rackId) => Ok2(await _svc.ListLocationsAsync(rackId));
}
```

- [ ] **Step 6: DI 注册 + 构建**

在 `Program.cs` 服务注册区加（**(v1.1)** 不注册任何租户上下文——`ITenantContext`/`TenantMiddleware` 已由主程序/安全合规注册）：
```csharp
// (v1.1: 删除 ISpaceTenantContext 桩注册——复用既有 ITenantContext)
builder.Services.AddScoped<CP6.Core.Services.Space.LocationGeometryService>();
builder.Services.AddScoped<CP6.Core.Services.Space.ISpaceMasterService, CP6.Core.Services.Space.SpaceMasterService>();
```
Run: `dotnet build` → succeeded

- [ ] **Step 7: 提交**

```bash
git add -A && git commit -m "feat(space): scene aggregation + unplaced list + master controller + DI (ch00 §9)"
```

---

# Phase C — 可配置编码引擎（03 章）

> **(v1.1: 联动 03 v1.1，仅引用不展开)** 03 设计已增补：①取值源新增 `rack-seq-zone`（货架在 Zone 内的序号，配合变长巷道段保唯一）——本 Phase 的 `SegInput`/取值源 `switch`（C-1/C-3）届时多一条分支；②两阶段重排的 `<作用域>` 改用 **JOIN `Space_Rack` 过滤**圈定重排集合（C-3 §7.2）。落码时以 03 v1.1 为准；本稿算法骨架不变。另：本 Phase 所有代码样例的租户处理一律按 Phase B 开头「v1.1 全局映射」读（删显式 `.Where(TenantId)` 与 `TenantId = tid`）。

## Task C-1: Segments 模型 + 取值源求值（03 §3）

**Files:**
- Create: `CP6.Entity/DTOs/Space/CodeRuleDtos.cs`
- Create: `CP6.Core/Services/Space/CodeSegment.cs`（段渲染纯逻辑）
- Test: `CP6.Tests/CodeEngineServiceTests.cs`

- [ ] **Step 1: 写失败测试（单段渲染：序号源补零、码源大写、拼接）** `[InMemory 仅测逻辑]`

```csharp
public class CodeEngineServiceTests
{
    [Fact]
    public void RenderSegment_SeqSource_PadsWidth()
    {
        var seg = new CodeSegmentDef { Key = "rack", Source = "rack-seq", Width = 2, Pad = "0", Start = 1, Step = 1 };
        Assert.Equal("03", CodeSegment.Render(seg, new SegInput { SeqIndex = 3 }));   // start1+(3-1)*1=3 → "03"
    }

    [Fact]
    public void RenderSegment_CodeSource_Upper()
    {
        var seg = new CodeSegmentDef { Key = "zone", Source = "zone-code", Upper = true };
        Assert.Equal("A", CodeSegment.Render(seg, new SegInput { RawCode = "a" }));
    }
}
```

- [ ] **Step 2: 跑红** → FAIL

- [ ] **Step 3: 实现 Segments DTO + 段渲染**

```csharp
// CodeRuleDtos.cs
namespace CP6.Entity.DTOs.Space;

public class CodeSegmentDef
{
    public string Key = "", Name = "", Source = "";       // source: fixed/site-code/floor-level/zone-code/zone-seq/aisle-code/aisle-seq/rack-code/rack-seq/col/level/depth
    public int Width = 0; public string Pad = "0";
    public int Start = 1, Step = 1;
    public string Sep = "-"; public bool Upper = false;
    public string FixedValue = ""; public bool Optional = false;
}
public class CodeRuleDto { public Guid? Id; public string RuleName=""; public int ScopeType; public Guid? ScopeId; public List<CodeSegmentDef> Segments=new(); public bool IsDefault; }
public class CodePreviewReq { public List<CodeSegmentDef> Segments=new(); public int ScopeType; public Guid? ScopeId; public Guid? FloorId; }
public class CodePreviewResp { public List<object> Structure=new(); public List<string> Samples=new(); public VariableLen VariableLen=new(); public Precheck Precheck=new(); }
public class VariableLen { public string WithAisle=""; public string WithoutAisle=""; }
public class Precheck { public bool Ok=true; public List<string> Errors=new(); }
public class CodePrecheckResp { public int EmptyCodeCount; public List<List<Guid>> DuplicateGroups=new(); public List<string> PrecheckErrors=new(); public int UnplacedDraftCount; }
```

```csharp
// CodeSegment.cs
namespace CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;

public class SegInput { public string? RawCode; public int? SeqIndex; }   // 码源给 RawCode；序号源给 SeqIndex（1-based 位次）

public static class CodeSegment
{
    private static readonly HashSet<string> SeqSources = new() { "zone-seq","aisle-seq","rack-seq","col","level","depth" };
    public static bool IsSeq(string source) => SeqSources.Contains(source);

    public static string Render(CodeSegmentDef seg, SegInput input)
    {
        string raw;
        if (seg.Source == "fixed") raw = seg.FixedValue;
        else if (IsSeq(seg.Source)) raw = (seg.Start + ((input.SeqIndex ?? 1) - 1) * seg.Step).ToString();
        else raw = input.RawCode ?? "";
        if (seg.Upper) raw = raw.ToUpperInvariant();
        if (seg.Width > 0) raw = raw.PadLeft(seg.Width, (seg.Pad.Length > 0 ? seg.Pad[0] : '0'));
        return raw;
    }
}
```

- [ ] **Step 4: 跑绿** → PASS
- [ ] **Step 5: 提交** → `git add -A && git commit -m "feat(space): code segment model + per-segment render (ch03 §3)"`

---

## Task C-2: 静态预检（03 §6.1 —— 租户全局唯一防线一）

**Files:**
- Create: `CP6.Core/Services/Space/CodePrecheck.cs`
- Test: `CP6.Tests/CodeEngineServiceTests.cs`

- [ ] **Step 1: 追加失败测试（缺 zone 段→E-303；含 optional 巷道段但 rack 段非 Zone 级编号→E-303；未到库位粒度→E-306；巷道段未标 optional→E-305）**

```csharp
    [Fact]
    public void Static_NoZoneSegment_E303()
    {
        var segs = new List<CodeSegmentDef> { new() { Key="rack", Source="rack-seq" }, new() { Key="col", Source="col" } };
        var r = CodePrecheck.Validate(segs);
        Assert.Contains("E-SPACE-303", r);
    }

    [Fact]
    public void Static_NoLocationGranularity_E306()
    {
        var segs = new List<CodeSegmentDef> { new() { Key="zone", Source="zone-code" }, new() { Key="rack", Source="rack-seq" } };
        var r = CodePrecheck.Validate(segs);
        Assert.Contains("E-SPACE-306", r);   // 无 col/level/depth
    }

    [Fact]
    public void Static_AisleSegNotOptional_E305()
    {
        var segs = new List<CodeSegmentDef> {
            new() { Key="zone", Source="zone-code" }, new() { Key="aisle", Source="aisle-code", Optional=false },
            new() { Key="rack", Source="rack-seq" }, new() { Key="col", Source="col" } };
        Assert.Contains("E-SPACE-305", CodePrecheck.Validate(segs));
    }
```

- [ ] **Step 2: 跑红** → FAIL

- [ ] **Step 3: 实现静态预检**

```csharp
// CodePrecheck.cs
namespace CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;

public static class CodePrecheck
{
    private static readonly HashSet<string> LocSources = new() { "col","level","depth" };
    private static readonly HashSet<string> ZoneSources = new() { "zone-code","zone-seq" };
    private static readonly HashSet<string> AisleSources = new() { "aisle-code","aisle-seq" };

    /// <summary>纯规则结构分析（03 §6.1）。返回错误码列表，空=通过。</summary>
    public static List<string> Validate(List<CodeSegmentDef> segs)
    {
        var errs = new List<string>();
        // ① 含能区分到 Zone 的段（zone 段，或 site+floor 组合）
        bool hasZone = segs.Any(s => ZoneSources.Contains(s.Source));
        bool hasSiteFloor = segs.Any(s => s.Source == "site-code") && segs.Any(s => s.Source == "floor-level");
        if (!hasZone && !hasSiteFloor) errs.Add("E-SPACE-303");
        // ② 巷道段必须 optional
        if (segs.Any(s => AisleSources.Contains(s.Source) && !s.Optional)) errs.Add("E-SPACE-305");
        // ③ 含 optional 巷道段时，rack-seq 必须 Zone 级编号（约定：规则需显式标记，本稿用 rack 段 Width>0 且无 aisle-seq 同级近似；
        //    更严判据见实现者注）——简化：若含 optional aisle 段且含 rack-seq，要求同时含 zone 段（已被①覆盖），此处仅补提示
        // ④ 到库位粒度
        if (!segs.Any(s => LocSources.Contains(s.Source))) errs.Add("E-SPACE-306");
        return errs;
    }
}
```

> **实现者注（待你修订时定）**：③"rack-seq 是否 Zone 级编号"无法纯从段定义判定，需在生成算法（C-3）的序号计算里**强制 rack-seq 按 Zone 范围排序编号**（而非 Aisle 内），从而结构上保证变长唯一。本稿把它落在 C-3 的序号计算约定里，静态预检只兜①②④；若你要更严的静态判据，请在修订版指明 rack 段的"Zone 级"标记字段。

- [ ] **Step 4: 跑绿** → PASS
- [ ] **Step 5: 提交** → `git add -A && git commit -m "feat(space): static precheck for tenant-global uniqueness (ch03 §6.1)"`

---

## Task C-3: 层级遍历生成 + 两阶段重排 + 值级唯一校验（03 §4/§6.2/§7）

**Files:**
- Create: `CP6.Core/Services/Space/CodeEngineService.cs`, `ICodeEngineService.cs`
- Test: `CP6.Tests/CodeEngineServiceTests.cs`

- [ ] **Step 1: 追加失败测试（只作用草稿∧引擎码；生成后写回；批内重复→E-304 整事务零写入；fill-empty 不动既有码）**

```csharp
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Generate_SkipsPublishedAndAdopted()
    {
        var t = DefaultSpaceTenantContext.DefaultTenant;
        using var db = Db();
        // 一套最小层级 + 1 草稿库位 + 1 已发布库位
        var (floorId, zoneId, rackId) = SeedFloorZoneRack(db, t);
        db.Space_CodeRules.Add(DefaultRule(t));
        db.Space_Locations.AddRange(
            Draft(t, rackId, floorId, 1,1,1),                                  // 草稿、空码
            new Space_Location { Id=Guid.NewGuid(), TenantId=t, RackId=rackId, FloorId=floorId, Placed=true, Col=2,Level=1,Depth=1, Status=1, CodeOrigin=1, LocationCode="FROZEN" }); // 已发布
        await db.SaveChangesAsync();

        var svc = new CodeEngineService(db, new DefaultSpaceTenantContext());
        var res = await svc.GenerateAsync(floorId, mode: "rebuild", scopeZoneId: null);

        Assert.Equal(1, res.Count);                                            // 只生成草稿那条
        Assert.Equal("FROZEN", (await db.Space_Locations.FirstAsync(l => l.Status==1)).LocationCode); // 已发布不动
        Assert.NotNull((await db.Space_Locations.FirstAsync(l => l.Status==0)).LocationCode);         // 草稿被写码
    }
```
（辅助 `SeedFloorZoneRack/DefaultRule/Draft` 在测试类内实现——建最小 Site→Floor→Zone→Rack 链 + 一条 `ScopeType=0,IsDefault=true` 规则 `zone-code - rack-seq(2) - level(2) - col(2)`。）

- [ ] **Step 2: 跑红** → FAIL

- [ ] **Step 3: 实现生成引擎**（核心算法，照 03 §4.2 主流程）

```csharp
// ICodeEngineService.cs
public interface ICodeEngineService
{
    Task<List<string>> GenerateAsync(Guid floorId, string mode, Guid? scopeZoneId);   // mode: fill-empty | rebuild
    Task<CodePreviewResp> PreviewAsync(CodePreviewReq req);
    Task<CodePrecheckResp> PrecheckAsync(Guid floorId);
    Task<string> GenSingleAsync(Guid locationId);
}
```

```csharp
// CodeEngineService.cs（生成主流程；省略 Preview 见 C-4、Precheck 见 C-5）
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CP6.Core.Services.Space;

public class CodeEngineService : ICodeEngineService
{
    private readonly CP6Context _db; private readonly ISpaceTenantContext _t;
    public CodeEngineService(CP6Context db, ISpaceTenantContext t) { _db = db; _t = t; }

    public async Task<List<string>> GenerateAsync(Guid floorId, string mode, Guid? scopeZoneId)
    {
        var tid = _t.TenantId;
        // 1. 拉层级数据（Floor/Zone/Aisle/Rack）+ 草稿库位（Status=0 ∧ CodeOrigin=1）
        var zones  = await _db.Space_Zones.Where(z => z.TenantId==tid && z.FloorId==floorId && (scopeZoneId==null || z.Id==scopeZoneId)).ToListAsync();
        var zoneIds= zones.Select(z=>z.Id).ToList();
        var racks  = await _db.Space_Racks.Where(r => r.TenantId==tid && zoneIds.Contains(r.ZoneId)).ToListAsync();
        var aisles = await _db.Space_Aisles.Where(a => a.TenantId==tid && zoneIds.Contains(a.ZoneId)).ToListAsync();
        var site   = await _db.Space_Floors.Where(f => f.Id==floorId && f.TenantId==tid).Join(_db.Space_Sites, f=>f.SiteId, s=>s.Id, (f,s)=>new{f,s}).FirstOrDefaultAsync();
        var rackIds= racks.Select(r=>r.Id).ToList();
        var query  = _db.Space_Locations.Where(l => l.TenantId==tid && l.Status==0 && l.CodeOrigin==1 && l.RackId!=null && rackIds.Contains(l.RackId!.Value));
        var drafts = await query.ToListAsync();

        // 2. 规则集：每个 Zone 按 §2.2 优先级解析（库区→楼层→租户默认）
        var rules = await _db.Space_CodeRules.Where(r => r.TenantId==tid).ToListAsync();
        // 3. 静态预检（每套命中的规则）—— 不过 → E-303 终止
        // 4. 算序号：rack-seq 按 ★Zone 级排序（覆盖该 Zone 下所有巷道货架，保变长唯一，C-2 实现者注）
        //    zone-seq 按 floor 内 Zone.Code 排序；col/level/depth 取索引
        // 5. 逐库位拼 code（CodeSegment.Render + Aisle 条件段跳过 §5.2）
        // 6. fill-empty: 仅 LocationCode==null 的草稿; rebuild: 全草稿先置 NULL（阶段1）再赋值（阶段2，§7.2）
        // 7. 值级唯一校验（§6.2）：批内去重 + 与库内既有非空码比对 → 冲突 E-304 整体回滚
        // 8. 批量写回 LocationCode

        // —— 详细实现见下方分解；返回生成的 code 列表 ——
        return await RunGenerationAsync(tid, floorId, mode, zones, racks, aisles, site?.s, site?.f, rules, drafts);
    }
    // RunGenerationAsync / BuildContext / ResolveRule / ComputeSeqs / AssembleCode 见实现细节块
}
```

实现细节块（同文件私有方法，照 03 算法逐条落）：

```csharp
    private async Task<List<string>> RunGenerationAsync(Guid tid, Guid floorId, string mode,
        List<Space_Zone> zones, List<Space_Rack> racks, List<Space_Aisle> aisles,
        Space_Site? site, Space_Floor? floor, List<Space_CodeRule> rules, List<Space_Location> drafts)
    {
        // 选生效规则（按 Zone）
        Space_CodeRule PickRule(Guid zoneId, Guid fId)
        {
            var z = rules.Where(r => r.ScopeType==2 && r.ScopeId==zoneId).ToList();
            if (z.Count>0) return z.FirstOrDefault(r=>r.IsDefault) ?? (z.Count==1 ? z[0] : throw new InvalidOperationException("E-SPACE-302"));
            var f = rules.Where(r => r.ScopeType==1 && r.ScopeId==fId).ToList();
            if (f.Count>0) return f.FirstOrDefault(r=>r.IsDefault) ?? (f.Count==1 ? f[0] : throw new InvalidOperationException("E-SPACE-302"));
            var g = rules.Where(r => r.ScopeType==0).ToList();
            if (g.Count>0) return g.FirstOrDefault(r=>r.IsDefault) ?? (g.Count==1 ? g[0] : throw new InvalidOperationException("E-SPACE-302"));
            throw new InvalidOperationException("E-SPACE-301");
        }

        // 序号表：rack-seq 按 Zone 级（该 Zone 下全部货架按 (X,Y) 几何序），zone-seq 按 floor 内 Zone.Code 序
        var zoneSeq  = zones.OrderBy(z=>z.ZoneCode).Select((z,i)=>(z.Id,i+1)).ToDictionary(x=>x.Id, x=>x.Item2);
        var rackSeq  = racks.GroupBy(r=>r.ZoneId).SelectMany(g => g.OrderBy(r=>r.X).ThenBy(r=>r.Y).Select((r,i)=>(r.Id,i+1))).ToDictionary(x=>x.Id, x=>x.Item2);
        var rackById = racks.ToDictionary(r=>r.Id);
        var zoneById = zones.ToDictionary(z=>z.Id);
        var aisleById= aisles.ToDictionary(a=>a.Id);

        // 预检每套命中规则
        foreach (var z in zones)
        {
            var rule = PickRule(z.Id, floorId);
            var segs = JsonSerializer.Deserialize<List<CodeSegmentDef>>(rule.Segments) ?? new();
            var errs = CodePrecheck.Validate(segs);
            if (errs.Count>0) throw new InvalidOperationException(errs[0]);   // E-303/305/306
        }

        // 候选码
        var candidates = new List<(Space_Location loc, string code)>();
        foreach (var l in drafts)
        {
            if (mode=="fill-empty" && l.LocationCode!=null) continue;
            var rack = rackById[l.RackId!.Value];
            var zone = zoneById[rack.ZoneId];
            var aisle = rack.AisleId!=null && aisleById.TryGetValue(rack.AisleId.Value, out var a) ? a : null;
            var rule = PickRule(zone.Id, floorId);
            var segs = JsonSerializer.Deserialize<List<CodeSegmentDef>>(rule.Segments) ?? new();
            candidates.Add((l, Assemble(segs, site, floor, zone, aisle, rack,
                l.Col!.Value, l.Level!.Value, l.Depth!.Value, zoneSeq, rackSeq)));
        }

        // 值级唯一校验（§6.2）
        var dup = candidates.GroupBy(c=>c.code).Where(g=>g.Count()>1).ToList();
        if (dup.Count>0) throw new InvalidOperationException("E-SPACE-304");
        var codes = candidates.Select(c=>c.code).ToHashSet();
        var idsInBatch = candidates.Select(c=>c.loc.Id).ToHashSet();
        var clash = await _db.Space_Locations.AnyAsync(l => l.TenantId==tid && l.LocationCode!=null
            && !idsInBatch.Contains(l.Id) && codes.Contains(l.LocationCode));
        if (clash) throw new InvalidOperationException("E-SPACE-304");

        // 两阶段重排（§7.2）—— rebuild 先置 NULL
        if (mode=="rebuild")
        {
            foreach (var l in drafts) l.LocationCode = null;
            await _db.SaveChangesAsync();
        }
        foreach (var (loc, code) in candidates) loc.LocationCode = code;
        await _db.SaveChangesAsync();
        return candidates.Select(c=>c.code).ToList();
    }

    private static string Assemble(List<CodeSegmentDef> segs, Space_Site? site, Space_Floor? floor,
        Space_Zone zone, Space_Aisle? aisle, Space_Rack rack, int col, int level, int depth,
        Dictionary<Guid,int> zoneSeq, Dictionary<Guid,int> rackSeq)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var seg in segs)
        {
            if (CodeSegment.IsSeq(seg.Source)==false && (seg.Source=="aisle-code") && aisle==null && seg.Optional) continue; // 条件段跳过(连sep)
            if ((seg.Source=="aisle-seq") && aisle==null && seg.Optional) continue;
            var input = Resolve(seg, site, floor, zone, aisle, rack, col, level, depth, zoneSeq, rackSeq);
            sb.Append(CodeSegment.Render(seg, input));
            sb.Append(seg.Sep);
        }
        var s = sb.ToString();
        // 去掉末尾遗留分隔符（最后一段 sep 通常空；保险处理）
        return s.TrimEnd('-','_','.','/',' ');
    }

    private static SegInput Resolve(CodeSegmentDef seg, Space_Site? site, Space_Floor? floor,
        Space_Zone zone, Space_Aisle? aisle, Space_Rack rack, int col, int level, int depth,
        Dictionary<Guid,int> zoneSeq, Dictionary<Guid,int> rackSeq) => seg.Source switch
    {
        "fixed"       => new SegInput { RawCode = seg.FixedValue },
        "site-code"   => new SegInput { RawCode = site?.SiteCode },
        "floor-level" => new SegInput { RawCode = (floor?.Level ?? 0).ToString() },
        "zone-code"   => new SegInput { RawCode = zone.ZoneCode },
        "zone-seq"    => new SegInput { SeqIndex = zoneSeq.GetValueOrDefault(zone.Id, 1) },
        "aisle-code"  => new SegInput { RawCode = aisle?.AisleCode },
        "rack-code"   => new SegInput { RawCode = rack.RackCode },
        "rack-seq"    => new SegInput { SeqIndex = rackSeq.GetValueOrDefault(rack.Id, 1) },
        "col"         => new SegInput { SeqIndex = col },
        "level"       => new SegInput { SeqIndex = level },
        "depth"       => new SegInput { SeqIndex = depth },
        _             => new SegInput { RawCode = "" },
    };
```

> **实现者注**：①末尾分隔符处理用 `TrimEnd` 是简化；更稳的是"先收集各段输出，再用各自 sep join"——你修订时可换。②变长唯一性靠 `rackSeq` **按 Zone 分组编号**（上面 `GroupBy(r=>r.ZoneId)`）兑现 03 §5.3。③`aisle` 条件段跳过同时吞掉 sep——这里靠"跳过整段含其 Append(sep)"实现。

- [ ] **Step 4: 跑绿** → Run: `dotnet test CP6.Tests --filter CodeEngineServiceTests` → PASS

- [ ] **Step 5: 提交**

```bash
git add -A && git commit -m "feat(space): hierarchical code generation + two-phase rebuild + uniqueness (ch03 §4/6/7)"
```

---

## Task C-4: 实时预览（03 §8）

**Files:** Modify `CodeEngineService.cs`; Test `CodeEngineServiceTests.cs`

- [ ] **Step 1: 失败测试**（preview 返回 structure + 合成 samples + 变长两条 + precheck；不写库）
- [ ] **Step 2: 跑红**
- [ ] **Step 3: 实现 `PreviewAsync`**

```csharp
    public async Task<CodePreviewResp> PreviewAsync(CodePreviewReq req)
    {
        var resp = new CodePreviewResp();
        resp.Precheck.Errors = CodePrecheck.Validate(req.Segments);
        resp.Precheck.Ok = resp.Precheck.Errors.Count == 0;
        resp.Structure = req.Segments.Select(s => (object)new { s.Key, s.Name, s.Source, s.Optional }).ToList();
        // 合成样例（无真实数据时）：zone=A, aisle=02, rack=03, level=02, col=05
        var synthZone = new Space_Zone { ZoneCode = "A" };
        var synthAisle = new Space_Aisle { AisleCode = "02" };
        var synthRack = new Space_Rack { RackCode = "R03" };
        var z = new Dictionary<Guid,int>(); var r = new Dictionary<Guid,int> { [synthRack.Id]=3 };
        resp.VariableLen.WithAisle    = Assemble(req.Segments, null, null, synthZone, synthAisle, synthRack, 5, 2, 1, z, r);
        resp.VariableLen.WithoutAisle = Assemble(req.Segments, null, null, synthZone, null,        synthRack, 5, 2, 1, z, r);
        resp.Samples.Add(resp.VariableLen.WithAisle);
        await Task.CompletedTask;
        return resp;
    }
```

- [ ] **Step 4: 跑绿** → PASS
- [ ] **Step 5: 提交** → `git add -A && git commit -m "feat(space): code-rule live preview (ch03 §8)"`

---

## Task C-5: 发布前编码预检 code-precheck（03 §9.2 —— 04 闸门入口）

**Files:** Modify `CodeEngineService.cs`; Test `CodeEngineServiceTests.cs`

- [ ] **Step 1: 失败测试**（floor 内有空码草稿→emptyCodeCount>0；有重复码→duplicateGroups 非空）
- [ ] **Step 2: 跑红**
- [ ] **Step 3: 实现 `PrecheckAsync`**

```csharp
    public async Task<CodePrecheckResp> PrecheckAsync(Guid floorId)
    {
        var tid = _t.TenantId;
        var resp = new CodePrecheckResp();
        var locs = await _db.Space_Locations
            .Where(l => l.TenantId==tid && l.FloorId==floorId && l.Status==0).ToListAsync();
        resp.EmptyCodeCount = locs.Count(l => l.LocationCode == null);
        resp.DuplicateGroups = locs.Where(l => l.LocationCode != null)
            .GroupBy(l => l.LocationCode).Where(g => g.Count() > 1)
            .Select(g => g.Select(x => x.Id).ToList()).ToList();
        resp.UnplacedDraftCount = locs.Count(l => l.LocationCode != null && !l.Placed);
        // 规则完备性（命中本层各 Zone 规则跑静态预检）——汇总错误码
        // resp.PrecheckErrors = ...（复用 PickRule + CodePrecheck.Validate，去重）
        return resp;
    }
```

- [ ] **Step 4: 跑绿** → PASS
- [ ] **Step 5: 写 `CodeRuleController` + DI 注册 + 构建**

```csharp
// CodeRuleController.cs — /api/space/code-rule*, /floor/{id}/generate-codes, /floor/{id}/code-precheck, /location/{id}/gen-code
// 同 SpaceMasterController 风格；generate-codes 体 { mode, scope? }
```
Program.cs 加：`builder.Services.AddScoped<CP6.Core.Services.Space.ICodeEngineService, CP6.Core.Services.Space.CodeEngineService>();`

- [ ] **Step 6: 提交** → `git add -A && git commit -m "feat(space): code-precheck gate + code-rule controller (ch03 §9.2)"`

---

# Phase D — 库位发布与 WMS 集成契约（04 章）

> **(v1.1: 联动 04 v1.1，仅引用不展开)** 04 契约已增补：①WMS 侧新增 `T_WmsBin` 消费表（真实落库目标）；②库位停用由"发事件"改为**同步 RPC**；③`SiteCode ↔ WarehouseCd` 映射。这些**属 WMS 模块后续**——P1 后端**仍只保 `NoOpWmsLocationConsumer`/`StubWmsStockQuery` 桩**（不建 `T_WmsBin`、不实现同步 RPC），但**桩接口签名对齐 04 v1.1**（如消费结果含映射回执字段），切真实实现时桩零改签名。本 Phase 代码样例租户处理同样按 Phase B 开头「v1.1 全局映射」读。

## Task D-1: 发布载荷 DTO + WMS 契约 + 桩（04 §3 / D-D）

**Files:**
- Create: `CP6.Entity/DTOs/Space/LocationPublishBatch.cs`
- Create: `CP6.Core/Services/Integration/IWmsLocationConsumer.cs` + `NoOpWmsLocationConsumer.cs`
- Create: `CP6.Core/Services/Integration/IWmsStockQuery.cs` + `StubWmsStockQuery.cs`

- [ ] **Step 1: 写载荷 + 契约 + 桩**

```csharp
// LocationPublishBatch.cs（04 §3.1）
namespace CP6.Entity.DTOs.Space;

public class LocationPublishBatch
{
    public string BatchNo = ""; public Guid TenantId; public string? PublishedBy;
    public List<LocationPublishItem> Items = new();
}
public class LocationPublishItem
{
    public string Op = "UPSERT";              // UPSERT | DEACTIVATE
    public Guid LocationId; public string LocationCode = ""; public int CodeOrigin; public long Version;
    public LocationPath Path = new();
    public Dictionary<string, object?> Attrs = new();   // 仅业务属性，★无绝对坐标几何
}
public class LocationPath
{
    public string? SiteCode; public int FloorLevel; public string? ZoneCode;
    public string? AisleCode;                 // 无巷道为 null（变长）
    public string? RackCode; public int Col, Level, Depth;
}
public class WmsConsumeResult { public bool Success; public bool AllSkipped; public List<WmsItemResult> Items = new(); }
public class WmsItemResult { public Guid LocationId; public string Status = ""; public string? Reason; }  // Status: UPSERTED|SKIPPED|DEACTIVATED|REJECTED
```

```csharp
// IWmsLocationConsumer.cs — Space 侧定义，WMS 实现（单向，Space 不依赖 WMS 实现程序集）
namespace CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Space;

public interface IWmsLocationConsumer
{
    Task<WmsConsumeResult> ConsumeAsync(LocationPublishBatch batch);   // 幂等 upsert（按 LocationId+Version，04 §5）
}

/// <summary>P1 桩：接受全部、标 UPSERTED（真实 WMS 消费属 WMS 模块工作）。配置 SpaceWms:Enabled 切换。</summary>
public sealed class NoOpWmsLocationConsumer : IWmsLocationConsumer
{
    public Task<WmsConsumeResult> ConsumeAsync(LocationPublishBatch batch) =>
        Task.FromResult(new WmsConsumeResult { Success = true,
            Items = batch.Items.ConvertAll(i => new WmsItemResult { LocationId = i.LocationId,
                Status = i.Op == "DEACTIVATE" ? "DEACTIVATED" : "UPSERTED" }) });
}
```

```csharp
// IWmsStockQuery.cs — D6 停用前查库存（07 给完整契约，本稿只用单码查询）
namespace CP6.Core.Services.Integration;

public interface IWmsStockQuery
{
    Task<int> GetStockQtyAsync(string locationCode);   // 返回该库位当前库存量
}

/// <summary>P1 桩：恒返回 0（无库存）。真实查询属 WMS 模块（07）。</summary>
public sealed class StubWmsStockQuery : IWmsStockQuery
{
    public Task<int> GetStockQtyAsync(string locationCode) => Task.FromResult(0);
}
```

- [ ] **Step 2: 构建** → `dotnet build` → succeeded
- [ ] **Step 3: 提交** → `git add -A && git commit -m "feat(space): publish batch DTO + WMS consumer/stock contracts + P1 stubs (ch04 §3, D-D)"`

---

## Task D-2: SpaceBridgeHook + Dispatcher 路由（04 §2 —— 复用既有基建）

**Files:**
- Create: `CP6.Core/Services/Integration/SpaceBridgeHook.cs`
- Modify: `CP6.Core/Services/Integration/IntegrationEventDispatcher.cs`
- Test: `CP6.Tests/SpaceBridgeHookTests.cs`

- [ ] **Step 1: 失败测试（发布 hook 调 consumer + 落 IntegrationEvent；幂等全 skip→Skipped）** `[InMemory 仅测逻辑]`

```csharp
public class SpaceBridgeHookTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Publish_PersistsIntegrationEvent_Success()
    {
        using var db = Db();
        var hook = new SpaceBridgeHook(db, NullLogger<SpaceBridgeHook>.Instance, new NoOpWmsLocationConsumer());
        var batch = new LocationPublishBatch { BatchNo = "LPUB-20260613-0001", Items = {
            new LocationPublishItem { Op="UPSERT", LocationId=Guid.NewGuid(), LocationCode="A-01-01-01", Version=1 } } };
        var r = await hook.OnLocationPublishedAsync(batch, Guid.NewGuid());
        Assert.True(r.Success);
        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal("SPACE", evt.SourceModule);
        Assert.Equal("WMS", evt.TargetModule);
        Assert.Equal("LPUB-20260613-0001", evt.SourceNo);
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
    }
}
```

- [ ] **Step 2: 跑红** → FAIL

- [ ] **Step 3: 实现 hook**

```csharp
// SpaceBridgeHook.cs
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs.Space;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Integration;

public class SpaceBridgeHook : BridgeHookBase
{
    private readonly IWmsLocationConsumer _wms;
    public SpaceBridgeHook(CP6Context db, ILogger<SpaceBridgeHook> logger, IWmsLocationConsumer wms)
        : base(db, logger) { _wms = wms; }

    public class BridgeResult { public bool Success; public string? Message; }

    public async Task<BridgeResult> OnLocationPublishedAsync(LocationPublishBatch batch, Guid correlationId)
    {
        string status; string? error = null; bool ok = false;
        try
        {
            var res = await _wms.ConsumeAsync(batch);
            ok = res.Success;
            status = !res.Success ? IntegrationEventStatus.Failed
                   : res.Items.All(i => i.Status == "SKIPPED") ? IntegrationEventStatus.Skipped
                   : IntegrationEventStatus.Success;
            if (!res.Success) error = "WMS consume returned failure";
        }
        catch (Exception ex) { status = IntegrationEventStatus.Failed; error = ex.ToString(); }

        await PersistEventAsync("SPACE", "WMS", nameof(OnLocationPublishedAsync),
            sourceNo: batch.BatchNo, targetNo: null, status: status, error: error,
            correlationId: correlationId, payload: batch);
        return new BridgeResult { Success = ok && status != IntegrationEventStatus.Failed, Message = error };
    }
}
```

- [ ] **Step 4: 跑绿** → PASS

- [ ] **Step 5: Dispatcher 加路由**（重试 Worker 重放用）

在 `IntegrationEventDispatcher` 的 `Routes` 字典加：
```csharp
        [RouteKey("SPACE", "WMS", "OnLocationPublishedAsync")] = async ctx =>
        {
            var p = ctx.GetPayload<LocationPublishBatch>();
            var r = await ctx.Space.OnLocationPublishedAsync(p, Guid.NewGuid());
            return r.Success;
        },
```
并给 `DispatchContext` + 构造注入加 `SpaceBridgeHook Space`（构造参数 + 字段，仿既有 `_mes/_wms`）。`GetPayload<LocationPublishBatch>` 用现成泛型。

> **注**：`SpaceBridgeHook` 是具体类（既有 hook 是接口）。为 Dispatcher 注入，建议给它抽 `ISpaceBridgeHook` 接口（仿 `IWmsBridgeHook`），Dispatcher 注入接口。修订时确认是否抽接口。

- [ ] **Step 6: 提交** → `git add -A && git commit -m "feat(space): SpaceBridgeHook + dispatcher SPACE|WMS route (ch04 §2)"`

---

## Task D-3: 发布服务 — 整层/库区发布（04 §4/§9 + D-E 批号）

**Files:**
- Create: `CP6.Core/Services/Space/LocationPublishService.cs`, `ILocationPublishService.cs`
- Test: `CP6.Tests/LocationPublishServiceTests.cs`

- [ ] **Step 1: 失败测试（过闸门→草稿 Status0→1 + Version+1 + 发 UPSERT 事件；闸门有空码→E-307 不发）**

```csharp
public class LocationPublishServiceTests
{
    [Fact]
    public async Task Publish_GatePassed_FlipsStatusAndEmitsEvent()
    {
        var t = DefaultSpaceTenantContext.DefaultTenant;
        using var db = Db();
        var floorId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location { Id=Guid.NewGuid(), TenantId=t, FloorId=floorId,
            RackId=Guid.NewGuid(), Placed=true, Status=0, CodeOrigin=1, LocationCode="A-01-01-01", Col=1,Level=1,Depth=1 });
        await db.SaveChangesAsync();
        var svc = MakePublishSvc(db);
        var n = await svc.PublishFloorAsync(floorId, zoneId: null, user: "u");
        Assert.Equal(1, n);
        var loc = await db.Space_Locations.SingleAsync();
        Assert.Equal(1, loc.Status); Assert.Equal(1, loc.Version);
        Assert.Equal("UPSERT", JsonGet(await db.IntegrationEvents.SingleAsync()));  // 事件载荷含 UPSERT
    }

    [Fact]
    public async Task Publish_EmptyCode_Throws_E307()
    {
        var t = DefaultSpaceTenantContext.DefaultTenant;
        using var db = Db();
        var floorId = Guid.NewGuid();
        db.Space_Locations.Add(new Space_Location { Id=Guid.NewGuid(), TenantId=t, FloorId=floorId, RackId=Guid.NewGuid(), Placed=true, Status=0, CodeOrigin=1, LocationCode=null });
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => MakePublishSvc(db).PublishFloorAsync(floorId, null, "u"));
        Assert.Equal("E-SPACE-307", ex.Message);
    }
}
```

- [ ] **Step 2: 跑红** → FAIL

- [ ] **Step 3: 实现发布**

```csharp
// LocationPublishService.cs（发布部分；停用/采纳/对账/删除护栏见 D-4..D-7）
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

public class LocationPublishService : ILocationPublishService
{
    private readonly CP6Context _db; private readonly ISpaceTenantContext _t;
    private readonly ICodeEngineService _code; private readonly SpaceBridgeHook _hook;
    public LocationPublishService(CP6Context db, ISpaceTenantContext t, ICodeEngineService code, SpaceBridgeHook hook)
    { _db = db; _t = t; _code = code; _hook = hook; }

    public async Task<int> PublishFloorAsync(Guid floorId, Guid? zoneId, string? user)
    {
        var tid = _t.TenantId;
        // 1. 闸门（03 §9.2）
        var pre = await _code.PrecheckAsync(floorId);
        if (pre.EmptyCodeCount > 0 || pre.DuplicateGroups.Count > 0 || pre.PrecheckErrors.Count > 0)
            throw new InvalidOperationException("E-SPACE-307");
        // 2. 取作用域内 Status=0 且编码就绪的库位
        var q = _db.Space_Locations.Where(l => l.TenantId==tid && l.FloorId==floorId && l.Status==0 && l.LocationCode!=null);
        var locs = await q.ToListAsync();
        if (zoneId != null) { /* 经 Rack→Zone 过滤；P1 简化按 floor，zone 过滤补 join */ }
        if (locs.Count == 0) return 0;
        // 3. 批号（D-E）
        var (_, seq) = await DocNumber.NextAsync(_db, "LPB");
        var batchNo = $"LPUB-{DateTime.Today:yyyyMMdd}-{seq:D4}";
        // 4. 翻状态 + 升版 + 组载荷
        var batch = new LocationPublishBatch { BatchNo = batchNo, TenantId = tid, PublishedBy = user };
        foreach (var l in locs)
        {
            l.Status = 1; l.Version += 1;
            l.Modifier = user; l.ModifyDate = DateTime.Now;
            batch.Items.Add(await BuildItemAsync(l, "UPSERT"));
        }
        await _db.SaveChangesAsync();
        // 5. 发事件（复用基建；hook 内 PersistEventAsync）
        await _hook.OnLocationPublishedAsync(batch, Guid.NewGuid());
        return locs.Count;
    }

    private async Task<LocationPublishItem> BuildItemAsync(Space_Location l, string op)
    {
        var tid = _t.TenantId;
        // 组变长 path（跳 Aisle）：Rack→Zone→Floor→Site；attrs 仅 size，★无 AbsX/Y/Z
        var path = new LocationPath { Col = l.Col ?? 0, Level = l.Level ?? 0, Depth = l.Depth ?? 0 };
        if (l.RackId != null)
        {
            var rack = await _db.Space_Racks.FirstAsync(r => r.Id == l.RackId && r.TenantId == tid);
            path.RackCode = rack.RackCode;
            if (rack.AisleId != null) path.AisleCode = (await _db.Space_Aisles.FirstAsync(a => a.Id == rack.AisleId)).AisleCode;
            var zone = await _db.Space_Zones.FirstAsync(z => z.Id == rack.ZoneId);
            path.ZoneCode = zone.ZoneCode;
            var floor = await _db.Space_Floors.FirstAsync(f => f.Id == zone.FloorId);
            path.FloorLevel = floor.Level;
            path.SiteCode = (await _db.Space_Sites.FirstAsync(s => s.Id == floor.SiteId)).SiteCode;
        }
        return new LocationPublishItem { Op = op, LocationId = l.Id, LocationCode = l.LocationCode ?? "",
            CodeOrigin = l.CodeOrigin, Version = l.Version, Path = path,
            Attrs = new() { ["sizeW"] = l.SizeW, ["sizeH"] = l.SizeH, ["sizeD"] = l.SizeD } };
    }
}
```

- [ ] **Step 4: 跑绿** → PASS
- [ ] **Step 5: 提交** → `git add -A && git commit -m "feat(space): publish floor/zone — gate + freeze + emit (ch04 §4/9)"`

---

## Task D-4: 停用 + D6 双重校验（04 §6）

**Files:** Modify `LocationPublishService.cs`; Test `LocationPublishServiceTests.cs`

- [ ] **Step 1: 失败测试**（桩库存=0→停用成功 Status1→2 Version+1 发 DEACTIVATE；前置库存>0→E-401 不发）
- [ ] **Step 2: 跑红**
- [ ] **Step 3: 实现 `DeactivateAsync`**

```csharp
    private readonly IWmsStockQuery _stock;   // 注入（构造加）
    public async Task DeactivateAsync(Guid locationId, string? user)
    {
        var tid = _t.TenantId;
        var l = await _db.Space_Locations.FirstOrDefaultAsync(x => x.Id == locationId && x.TenantId == tid)
                ?? throw new InvalidOperationException("E-SPACE-004");
        if (l.Status != 1) throw new InvalidOperationException("E-SPACE-004");
        // ① Space 前置校验（D6①）
        var qty = await _stock.GetStockQtyAsync(l.LocationCode ?? "");
        if (qty > 0) throw new InvalidOperationException("E-SPACE-401");
        // 发 DEACTIVATE（WMS 侧 TOCTOU 兜底②由 WMS 消费实现；桩恒接受）
        l.Status = 2; l.Version += 1; l.Modifier = user; l.ModifyDate = DateTime.Now;
        var (_, seq) = await DocNumber.NextAsync(_db, "LPB");
        var batch = new LocationPublishBatch { BatchNo = $"LPUB-{DateTime.Today:yyyyMMdd}-{seq:D4}", TenantId = tid, PublishedBy = user };
        batch.Items.Add(await BuildItemAsync(l, "DEACTIVATE"));
        await _db.SaveChangesAsync();
        var r = await _hook.OnLocationPublishedAsync(batch, Guid.NewGuid());
        // WMS 拒绝（W-404）→ 回滚 Status→1（P1 桩不触发；真实消费回写后处理）
        if (!r.Success) { l.Status = 1; l.Version += 1; await _db.SaveChangesAsync(); throw new InvalidOperationException("W-SPACE-404"); }
    }
```

- [ ] **Step 4: 跑绿** → PASS
- [ ] **Step 5: 提交** → `git add -A && git commit -m "feat(space): deactivate with D6 stock pre-check (ch04 §6)"`

---

## Task D-5: 存量采纳导入 adopt（04 §8.1）

**Files:** Modify `LocationPublishService.cs`; Test

- [ ] **Step 1: 失败测试**（adopt 建 Status1/Placed false/CodeOrigin2/RackId null；不发事件；码冲突→E-008 跳过报告）
- [ ] **Step 2: 跑红**
- [ ] **Step 3: 实现 `AdoptAsync`**

```csharp
    public async Task<(int imported, List<string> skipped)> AdoptAsync(List<(string code, Dictionary<string,object?>? attrs)> items, string? user)
    {
        var tid = _t.TenantId;
        var existing = await _db.Space_Locations.Where(l => l.TenantId==tid && l.LocationCode!=null)
            .Select(l => l.LocationCode!).ToListAsync();
        var set = existing.ToHashSet();
        int n = 0; var skipped = new List<string>();
        foreach (var (code, attrs) in items)
        {
            if (set.Contains(code)) { skipped.Add(code); continue; }   // E-SPACE-008
            _db.Space_Locations.Add(new Space_Location { Id = Guid.NewGuid(), TenantId = tid,
                LocationCode = code, CodeOrigin = 2, Status = 1, Placed = false, RackId = null,
                Creator = user, CreateDate = DateTime.Now });
            set.Add(code); n++;
        }
        await _db.SaveChangesAsync();   // 不发 LocationPublished（码本就来自 WMS）
        return (n, skipped);
    }
```

- [ ] **Step 4: 跑绿** → PASS
- [ ] **Step 5: 提交** → `git add -A && git commit -m "feat(space): adopt legacy codes (ch04 §8.1)"`

---

## Task D-6: 对账 reconcile（04 §8.2，桩对比）

**Files:** Modify `LocationPublishService.cs`; Test

- [ ] **Step 1: 失败测试**（给定 Space 采纳目录 vs WMS 桩目录 → 差异三类清单）
- [ ] **Step 2: 跑红**
- [ ] **Step 3: 实现 `ReconcileAsync(floorId)`**——P1 用 `IWmsStockQuery` 暂无目录查询，**对账依赖 WMS 提供"库位目录"查询契约**。本稿建议：D-6 在 P1 **只立接口 `IWmsLocationCatalogQuery`（桩返回空）+ 差异算法**，真实数据等 WMS 实现。

> **实现者注（待你修订时定）**：对账需要 WMS 侧"既有库位编码清单"，这是 07/WMS 的查询契约，P1 桩返回空集→对账结果恒为"Space 有/WMS 无"。若你认为 P1 不必做对账，可把 D-6 整体推迟到 07/WMS 落地后，本稿标为**可选**。

- [ ] **Step 4-5: 跑绿 + 提交**（若推迟则跳过，记 TODO）

---

## Task D-7: 删除护栏升级 — 触及已发布库位（04 §7）

**Files:** Modify `LocationPublishService.cs` + `SpaceMasterController`/`LocationPublishController`; Test

- [ ] **Step 1: 失败测试**（删货架其下有已发布库位→E-403；`?mode=deactivate` 路径A先停用再删；`?mode=rehome` 路径B改挂发 UPSERT 只刷 path）
- [ ] **Step 2: 跑红**
- [ ] **Step 3: 实现护栏 + 两放行路径**

```csharp
    // DeleteRackGuardedAsync(rackId, mode): 默认 Restrict E-403；mode=deactivate 走 D-4 逐个停用后删；
    // mode=rehome 接收新 zone/aisle/rack 目标，回填几何(码不变)+发 UPSERT(Version+1,只更新 path) → I-SPACE-402
    // 删巷道同理：默认 E-402；deactivate / rehome 改挂
```

> **实现者注**：路径 B(re-publish 改挂)需要"目标货架"参数，API 形如 `DELETE /rack/{id}?mode=rehome&toRackId=...`。细节较多，建议 D-7 作为 P1 收尾任务；核心发布闭环（D-3/D-4/D-5）先打通。

- [ ] **Step 4-5: 跑绿 + 提交**

---

## Task D-8: 发布控制器 + DI 装配 + 收尾整合

**Files:**
- Create: `CP6.WebApi/Controllers/Space/LocationPublishController.cs`
- Modify: `CP6.WebApi/Program.cs`

- [ ] **Step 1: 写控制器**（`/floor/{id}/publish`、`/location/{id}/deactivate`、`/location/adopt`、`/reconcile`、`/aisle|rack/{id}` DELETE、`/publish/events`；同 `MachineController` 风格）

- [ ] **Step 2: DI 装配**（Program.cs，仿既有 bridge NoOp/真实切换范式）

```csharp
// Space 发布 + WMS 桩（真实 WMS 消费/库存查询切换：SpaceWms:Enabled）
builder.Services.AddScoped<CP6.Core.Services.Integration.SpaceBridgeHook>();
builder.Services.AddScoped<CP6.Core.Services.Space.ILocationPublishService, CP6.Core.Services.Space.LocationPublishService>();
if (builder.Configuration.GetValue<bool>("SpaceWms:Enabled"))
{
    // 真实 WMS 实现（WMS 模块后续提供）—— 占位，P1 默认走 else
    builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsLocationConsumer, CP6.Core.Services.Integration.NoOpWmsLocationConsumer>();
    builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsStockQuery, CP6.Core.Services.Integration.StubWmsStockQuery>();
}
else
{
    builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsLocationConsumer, CP6.Core.Services.Integration.NoOpWmsLocationConsumer>();
    builder.Services.AddScoped<CP6.Core.Services.Integration.IWmsStockQuery, CP6.Core.Services.Integration.StubWmsStockQuery>();
}
```

> Dispatcher 已在 D-2 注入 `SpaceBridgeHook`；确认 `IntegrationEventDispatcher` 构造能解析它（已注册）。

- [ ] **Step 3: 全量构建 + 全测**

Run: `dotnet build && dotnet test CP6.Tests`
Expected: Build succeeded；全部 Space 测试 PASS

- [ ] **Step 4: 提交** → `git add -A && git commit -m "feat(space): publish controller + DI wiring (ch04 §10)"`

---

## Task D-9: 真库集成测兜底（索引/并发，`[需真库]`）

**Files:** Create `CP6.Tests/SpaceSqlIntegrationTests.cs`（用 SQLite in-memory 或 LocalDB；若 CI 无 SQL Server，标 `[Trait("Category","RequiresSql")]` 默认跳过）

- [ ] **Step 1: 写测试**——验证 InMemory 测不到的三件事：
  1. 过滤唯一索引：两条非空同码插入 → 第二条抛（`DbUpdateException`）；两条 `LocationCode=null` 可共存。
  2. 两阶段重排：A↔B 交换码，先置 NULL 再赋值 → 成功；不先置 NULL 直接交换 → 中途违约抛（证明两阶段必要）。
  3. RowVersion：并发改同一 Rack → 第二个 `DbUpdateConcurrencyException` → 服务转 E-SPACE-009。

- [ ] **Step 2: 跑（本地有 SQL Server/SQLite 时）** → PASS
- [ ] **Step 3: 提交** → `git add -A && git commit -m "test(space): SQL-backed index/concurrency integration tests"`

---

## Self-Review（对照 spec 的覆盖检查）

**Spec coverage：**
- 00 章：9 表(A-2) ✅ / 坐标系+绝对坐标重算(B-1) ✅ / 几何 JSON(实体字段+ValidatePolygon) ✅ / 状态机+放置维度(实体+服务) ✅ / API 骨架(B-6) ✅ / 删除策略(B-5) ✅ / 过滤唯一索引(A-3) ✅ / RowVersion(A-2/B-4) ✅
- 03 章：作用域+优先级(C-3 PickRule) ✅ / Segments(C-1) ✅ / 静态预检(C-2) ✅ / 层级生成(C-3) ✅ / Aisle 条件段+变长(C-3 Assemble) ✅ / 值级唯一(C-3) ✅ / 两阶段重排(C-3) ✅ / 预览(C-4) ✅ / code-precheck(C-5) ✅ / fill-empty vs rebuild(C-3) ✅ / 单格生成 gen-code(C-5 接口预留，实现待补) ⚠️
- 04 章：复用基建(D-2) ✅ / 载荷+变长 path+无几何(D-1/D-3) ✅ / 发布触发(D-3) ✅ / Version 幂等(D-1 契约+桩) ✅ / D6 停用双校验(D-4，①Space 前置真做、②WMS 侧由桩占位) ✅⚠️ / 删除护栏 A/B(D-7，部分推迟) ⚠️ / 采纳(D-5) ✅ / 对账(D-6，依赖 WMS 目录查询，推迟) ⚠️ / 闸门(D-3) ✅

**已知缺口 / 推迟项（已在任务内标注，待你修订时决定取舍）：**
1. **改 Cols/Levels 增删格子→自动补码/停用库位**（00 §6.2 表第 4 行）——需 C+D 就绪后整合，本稿 B-4 留挂点。
2. **对账 reconcile（D-6）**——依赖 WMS 库位目录查询契约，P1 桩返回空，建议推迟到 WMS/07。
3. **删除护栏路径 B re-publish 改挂（D-7）**——参数较多，建议作 P1 收尾。
4. **D6 ②WMS 侧 TOCTOU 再校验**——属 WMS 消费实现，P1 桩占位。
5. **gen-code 单格生成（03 §9.1 旁路）**——C-5 留接口，实现待补。

**Type 一致性：** `ComputeAbs`/`RecalcRackLocationsAsync`(B-1)、`CodeSegment.Render`/`IsSeq`(C-1)、`CodePrecheck.Validate`(C-2)、`GenerateAsync`/`PreviewAsync`/`PrecheckAsync`(C-3/4/5)、`OnLocationPublishedAsync`(D-2)、`PublishFloorAsync`/`DeactivateAsync`/`AdoptAsync`/`BuildItemAsync`(D-3/4/5) 跨任务签名已对齐。**(v1.1)** 租户上下文统一为既有 `ITenantContext`/`TenantContext`；测试统一 `TestHelper.CreateInMemoryContext(user, new TenantContext{ CurrentTenantId = t })`（不再有 `DefaultSpaceTenantContext`）。

---

## 执行交接

计划初稿已存 `docs/superpowers/plans/2026-06-13-space-p1-backend.md`。**(v1.1: 决策 D-A/D-B/D-C 已评审定案并直接返改入本文（复用真·多租户基建）；D-D/D-E 及收尾推迟项仍待最终取舍。)** 按丛书工作流，下一步是确认 D-D/D-E + 收尾推迟项后合并为唯一定稿，然后才进编码。

定稿后两种执行方式：
1. **Subagent-Driven（推荐）**——每个 Task 派新 subagent，任务间评审，快迭代。
2. **Inline Execution**——本会话内分批执行 + 检查点。

---

*初稿生成于 2026-06-13。源：docs/space/00·03·04。已勘察 CP6 真实代码：BaseEntity/BaseBizEntity 审计字段、零多租户现状、BridgeHookBase/IntegrationEventDispatcher 复用点、DocNumber 采番、xUnit+InMemory 测试基建。*

*v1.1 评审返改于 2026-06-27：S 类安全合规已落真·多租户（`BaseTenantEntity`/`BaseBizEntity` + `ITenantContext` + `CP6Context` 反射全局过滤/盖章/复合唯一升级 + `TenantMiddleware`），故 D-A 从桩升级为复用真基建——实体继承 `BaseBizEntity`、删自建 `ISpaceTenantContext`/`DefaultSpaceTenantContext` 桩与默认 GUID 常量、删全部显式 `.Where(TenantId)` 与手工盖章、09 章降级为接线收口；并加 00/03/04 v1.1 联动引用注记。已二次勘察确认：`CP6.Entity/BaseBizEntity.cs`、`CP6.Entity/BaseTenantEntity.cs`、`CP6.Core/Services/Common/ITenantContext.cs`、`CP6.WebApi/Middleware/TenantMiddleware.cs`、`CP6.Core/EFDbContext/CP6Context.cs`(反射全局过滤+唯一升级+StampTenant)、`CP6.Tests/TestHelper.cs`、`CP6.Tests/Tenant/TenantFilterTests.cs`。*
