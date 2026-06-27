# Space 09 · 多租户与 CP6 接入 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | SPACE-09 多租户与 CP6 接入 |
| 所属模块 | Space 空间数字底座 · **接入收尾** |
| 里程碑 | **收尾**（把 00~08 总装进 CP6：多租户隔离 + 权限接 PUB + 登录/菜单 + DI/迁移/事件路由；完成标志=单租户端到端跑通） |
| 技术栈 | .NET8 + EF Core（迁移/DI/多租户过滤）/ Vue3（菜单/路由接入）/ 复用 CP6 既有 [PUB 权限引擎](../pub/README.md)、TenantId 体系、IntegrationEvent |
| 命名空间 | 贯穿 `CP6.Entity/DomainModels/Space`、`CP6.Core/Services/Space`、`cp6.web/src/views/space`、`cp6.web/src/space-viewer` |
| 落地决策 | 全表 `TenantId`（沿用 CP6）/ 权限接 PUB 四粒度 / 编码规则按租户（[03](./03-code-engine.md)）/ Space 独立顶级模块（与 ERP/MES/WMS/OA/PUB 平级） |
| 依赖 | 00~08 全部（本章是它们的接入总装）、CP6 既有 PUB/TenantId/登录/菜单/IntegrationEvent 基建 |

> **题眼**：前 8 章把 Space 的**能力**做齐了，本章把它**接进 CP6 这台车**——让它多租户隔离、受 PUB 权限管控、出现在登录后的菜单里、数据库表建好、事件路由注册好。Space 不是孤立 demo，而是 CP6 的**第六个顶级模块**（与 ERP/MES/WMS/OA/PUB 平级，按 [模块taxonomy](../../docs) 约定：命名空间⊥菜单分组）。**记住一句**：本章不发明新能力，只做"总装 + 清单"——把 00~08 里反复出现的"全表 TenantId / 接 PUB 权限 / 按租户隔离"这些横切关注点收口落地，最后给一张**可勾选的接入清单**，勾完即单租户端到端跑通。
>
> **v1.1 评审补丁（2026-06-27）**：依设计深审修订——①🔴**第2章瘦身（过时→复用）**：多租户从"自建 EF 全局查询过滤 / 手写唯一索引复合升级"改为**复用 CP6 既有基建**——Space 实体继承 `BaseBizEntity` 即自动获全局查询过滤（`CP6Context.cs:1934-1942`，核心 `:1941`）、唯一索引自动升级 `(TenantId, …)` 前缀（`:1951-1988`）、`SaveChanges` 写入自动盖章（`:2094-2103`）；Space **不自设计、不手写** `HasQueryFilter`、不手写 `.Where(TenantId)`，删自建设计、改接线说明；②**实体继承链锁定 `BaseBizEntity`**（白拿 `TenantId/IsDeleted/RowVersion`，与 00 v1.1、backend 计划 v1.1 一致）；③**权限接入澄清**（CodeGen `BaseCrudController` 子类自动挂操作点为主 / 手写控制器须显式 `[RequirePermission]` + `Sys_MenuAction` 注册；Space 无 `DeptId`，P1 数据权限=租户隔离已够，Dept 级预留）；④**补 DI 与事件路由**（`AddSpaceServices` 扩展 + `IntegrationEventDispatcher` 反射路由 + 04 v1.1 新增 `IWmsBinDeactivator` 同步停用 RPC 绑定）；⑤**种子方案**（默认 `Space_CodeRule`/预置 `Space_Template` 的 HasData/Seed + 多租户上线复制）；⑥**菜单/i18n 种子**（`Sys_Menu`/`Sys_MenuAction`/`Sys_Lang` 五语词条 + `/space/*` 路由）；⑦**发布溯源临时方案**（`publishedBy`→`T_WmsBin.LastPublishedBy`，绕 `Creator="system"`，对齐 04 v1.1）；⑧§8 接入清单逐项补可执行指南；⑨YAGNI（模板 clone 属 01 章，09 只说"按租户隔离"）。详见各小节「(v1.1评审补丁)」标注。

---

## 目录
- 第1章 功能概述与定位（收尾章做什么）
- 第2章 多租户隔离（复用 CP6 基建：继承 BaseBizEntity 即得过滤/索引/盖章）
- 第3章 权限接 PUB（功能/数据/字段四粒度）
- 第4章 登录 / 菜单 / 路由接入（顶级模块）
- 第5章 DI 注册与契约装配（服务 / 事件路由 / WMS 查询契约）
- 第6章 数据库迁移（9 表 + CodeRule + 索引）
- 第7章 编码规则与模板的租户化
- 第8章 接入清单（可勾选 checklist）
- 第9章 消息一览
- 第10章 集成与依赖
- 自检

---

## 第1章 功能概述与定位

**目的**：把 00~08 的 Space 模块完整接入 CP6 运行时——多租户隔离、PUB 权限管控、登录菜单可达、表结构落库、DI/事件装配，达到"单租户从登录到发布全链路跑通"。

**本章范围（09）：**
- 多租户：**复用 CP6 既有租户基建**——Space 实体继承 `BaseBizEntity` 即自动获全局查询过滤 / 唯一索引升级 / 写入盖章；09 只做**接线与自检**，不自写过滤。**(v1.1评审补丁)**
- 权限：Space 各操作/数据/字段接入 PUB 四粒度权限。
- 接入：Space 作为顶级模块进登录后菜单 + 前端路由。
- 装配：Services/Repo 的 DI、`SPACE|WMS` 事件路由、`IWmsStockQuery` 等契约的 DI 绑定。
- 迁移：9 表 + CodeRule 的 EF Migration + 索引。
- 租户化：编码规则、模板、场景按租户隔离 + 导入重映射。
- 接入清单：一张可勾选的上线 checklist。

**不含（划清边界）：**
| 能力 | 去哪 |
|---|---|
| 各业务能力本身（建模/编码/渲染/发布/叠加/可视化） | [00](./00-data-model.md)~[08](./08-advanced-viz.md) |
| PUB 权限引擎内部实现 | [PUB 模块](../pub/README.md)（Space 只接入，不实现） |
| WMS 侧契约实现 | WMS 模块 |

> **收尾章的性质**：它不增功能，只把横切关注点（多租户/权限/装配/迁移）从各章抽出来集中落地 + 自检。前 8 章每章都在"集成与依赖"里写了"→ PUB 权限 / 多租户"，本章是这些承诺的**统一兑现处**。

---

## 第2章 多租户隔离（复用 CP6 既有基建）

> **(v1.1评审补丁) 从"自建"改为"复用"**：早期草稿把"设计 EF 全局查询过滤的统一落地""唯一索引复合升级"当成 Space 要自己做的事。深审确认——**这些 CP6 已经做好了**，且是反射批量、对所有 `BaseTenantEntity` 一次性生效的"防漏命门"。Space **不自设计、不手写** `HasQueryFilter`、不手写 `.Where(TenantId)`、不手工拼 `(TenantId, Code)` 索引。本章只做**接线 + 自检**。

### 2.1 复用基建：实体继承到位即自动得三件套（v1.1评审补丁）
Space 9 表（Site/Floor/Zone/Aisle/Rack/Location/Template/CodeRule/Marker）**全部继承 `BaseBizEntity`**（`CP6.Entity/BaseBizEntity.cs:11`，继承链 `BaseEntity → BaseTenantEntity → BaseBizEntity`），即一次性白拿：

| 白拿能力 | 来源（file:行号） | 说明 |
|---|---|---|
| `TenantId` 字段 | `CP6.Entity/BaseTenantEntity.cs:8-11` | 行级隔离硬墙；实体无需自加字段 |
| `IsDeleted` / `RowVersion` | `CP6.Entity/BaseBizEntity.cs:11-25` | 逻辑删除 + 乐观锁（`[Timestamp]`），业务表标配 |
| **全局查询过滤** | `CP6Context.cs:1934-1942`（核心 `:1941` `HasQueryFilter`） | 反射对所有 `BaseTenantEntity` 批量注册 `WHERE TenantId == CurrentTenantId`，闭包每次查询重读当前租户——**任何查询自动切片，绝无裸查跨租户** |
| **唯一索引自动升级** | `CP6Context.cs:1951-1988` | 反射把所有"全局唯一"索引升级为 `(TenantId, …)` 复合唯一——`Space_CodeRule`/`Location` 的 `Code` 唯一约束自动变"租户内唯一"，B 租户可与 A 用相同编码 |
| **写入自动盖章** | `CP6Context.cs:2094-2103`（`StampTenant`，`SaveChanges` 于 `:2105-2107` 调用） | 新增实体 `TenantId==Empty` 时自动盖 `CurrentTenantId`——Service 写入**不必显式设租户** |

- **接线唯一动作**：把 9 个实体类的基类从 `BaseEntity` 改成 `BaseBizEntity`（00 v1.1 已锁此决策），其余全自动。**Space 自身零多租户代码**——无 `HasQueryFilter`、无 `.Where(TenantId)`、无手写复合唯一索引。
- 发布批 / 集成事件载荷由 CP6 集成基建侧承载 `TenantId`，Space 仅在 payload 透传（见 04 / §2.2）。
- 当前租户从何而来：`TenantMiddleware`（`CP6.WebApi/Middleware/TenantMiddleware.cs`，解 JWT 得租户）→ `ITenantContext`（`CP6.Core/Services/Common/ITenantContext.cs`，默认租户 A1）→ `CP6Context.CurrentTenantId`。**这条链 Space 不碰**。

### 2.2 跨租户串号防线（汇总前各章，多由基建兜底）
| 场景 | 防线 | 谁负责 | 章 |
|---|---|---|---|
| 场景导入 | 全 ID 重映射 + 注入当前 TenantId | Space 应用层（导入逻辑） | 01 §7.2 |
| 编码唯一 | `(TenantId, LocationCode)` 复合唯一索引 | **基建自动升级**（`CP6Context.cs:1951-1988`），Space 只声明单列唯一 | 00/03 |
| 写入盖章 | 新增行自动盖当前租户 | **基建 `StampTenant`**（`:2094-2103`），Space 不显式设 | 00 |
| 库存查询 | `IWmsStockQuery` 传 TenantId，WMS 按租户隔离 | Space 显式带（跨模块调用越过 EF 过滤） | 07 |
| 发布事件 | payload 带 tenantId，WMS 按租户建 bin | Space 显式带（出 EF 边界） | 04 |
- **自检要点（v1.1评审补丁）**：EF 边界内（Space 自身查询/写入）**靠基建自动切片，无需逐处检查**；**只需重点核**跨模块出口（导入重映射、`IWmsStockQuery` 调用、发布 payload）是否显式带了 TenantId——这些越过了 EF 全局过滤，是唯一需人盯的串号面。

---

## 第3章 权限接 PUB（四粒度）

Space 接入 CP6 既有 [PUB 权限引擎](../pub/README.md)（页面/按钮/数据行/字段四粒度 + 多角色），**不自造权限**。

### 3.1 控制器怎么挂权限：两条路（v1.1评审补丁）
PUB 的操作强校验 `[RequirePermission]` **不是手写控制器默认就有的**——按 codemap-pub：手写控制器默认**只 `[Authorize]`**（登录闸，无操作级管控），`[RequirePermission]` **仅由 CodeGen 产出的 `BaseCrudController` 子类承载**（子类在 override 上贴编译期常量 `[RequirePermission("resource","add")]`，见 `CP6.Core/Pub/BaseCrudController.cs`）。故 Space 两条路：
- **首选：主数据走 CodeGen** —— Site/Floor/Template/CodeRule 等标准 CRUD 用代码生成器产出 `BaseCrudController` 子类，**开箱自动挂操作点 + 四粒度**（Entity 实现 `IDataScoped`、Service 继承 `BaseCrudService`、Controller 贴 `[RequirePermission]`+`[FieldMask]`）。
- **手写控制器须显式**：编辑器/发布/定位/库存叠加这类**非标准 CRUD 的手写控制器**，必须**逐个动作显式** `[RequirePermission("space_editor","edit")]`，并在 `Sys_MenuAction` 注册对应操作点（`menuKey:action`，再由 `Sys_RoleAction` 授给角色，§4.3）——否则只剩 `[Authorize]` 登录闸。
- **高危操作独立授权点**：发布 / 停用 / 采纳 / 删除 各自独立 action，绝不与浏览混授。

### 3.2 功能权限（页面/按钮）
> 权限点 = `menuKey:action`（MenuKey 下划线小写，见 §4.3 菜单种子）。CodeGen 子类自动注册；手写控制器须在 `Sys_MenuAction` 显式注册并贴 `[RequirePermission]`。

| 权限点（menuKey:action） | 粒度 | 承载方 | 章 |
|---|---|---|---|
| `space_editor:view` / `:edit` | 页面/按钮 | 手写编辑器控制器（显式） | 01/02 建模、精修 |
| `space_template:manage` | 按钮 | CodeGen `BaseCrudController` | 01 模板 CRUD |
| `space_code:generate` | 按钮 | 手写（显式） | 03 生成/重排编码 |
| `space_publish:publish` / `:deactivate` | 按钮（高危） | 手写（显式，独立授权） | 04 发布/停用 |
| `space_editor:adopt` / `space_editor:import` | 按钮（高危） | 手写（显式） | 04/01 采纳/导入 |
| `space_viewer:view` | 页面 | 手写（显式） | 05/06 3D 浏览 |
| `space_viewer:stock` | 按钮 | 手写（显式） | 07 库存叠加 |
- 高危操作（发布/停用/删除/采纳）必须独立权限点，不与浏览混授。

### 3.3 数据权限（行/字段，v1.1评审补丁）
- **P1 数据权限维度 = 租户隔离已足**：CP6 行级数据权限（`DataScopeFilter`）按 `IDataScoped.DeptId`（`CP6.Entity/IDataScoped.cs:13`，需实体自补 `DeptId`）注入范围过滤。**Space 9 表无 `DeptId`**——P1 **不做 Dept 级行权限**，仅靠 §2 的租户全局过滤隔离（已是行级硬墙，够用）。
- **Dept 级预留怎么挂**：若后续需"用户只见本部门 Site/Floor"，让相关实体实现 `IDataScoped`（补 `Guid? DeptId`，可空，P1 留 null 即等价不过滤），即自动进 `DataScopeFilter`——无需改 Space 业务码。本版仅说明挂载方式，不实现。
- **字段权限**：敏感字段（如成本/容量）可挂 `[FieldMask("space_xxx")]`（CodeGen 子类自带 `StripReadOnly` 拒写），v1 多为预留、按需开启。
- 跨层定位（06）：P1 仅受租户隔离；Dept 级"无权楼层不可达"待 Dept 权限开启后生效。

---

## 第4章 登录 / 菜单 / 路由接入

### 4.1 顶级模块
- Space 是 CP6 **第六个顶级模块**（与 ERP/MES/WMS/OA/PUB 平级；按 taxonomy：代码命名空间 ⊥ 菜单分组）。
- 菜单分组（建议）：`空间数字底座`（顶级），下挂：空间建模（编辑器 01/02）、编码规则（03）、库位发布（04）、3D 浏览（05/06/07/08）。

### 4.2 接入点
| 接入 | 做法 |
|---|---|
| 登录 | 复用 CP6 登录/鉴权；登录后按 PUB 权限渲染 Space 菜单项 |
| 菜单 | 在 CP6 菜单表注册 Space 菜单节点（按权限点显隐） |
| 前端路由 | `cp6.web` 注册 `/space/*` 路由（editor / code-rule / publish / viewer） |
| 多语言 | Space 菜单/界面文案接 CP6 i18n（沿用 i18n 迁移指南） |

### 4.3 菜单 / 操作点 / i18n 种子（v1.1评审补丁）
随迁移种子（§6.2 同一 `Seed`/`HasData`）写入 PUB 三张表 + 前端路由——把"接入 CP6"落成可执行清单：
- **`Sys_Menu` 菜单节点**：顶级"空间数字底座"+子节点，`MenuKey` 下划线小写约定：`space_editor`（建模 01/02）、`space_code`（编码 03）、`space_publish`（发布 04）、`space_viewer`（3D 浏览 05/06/07/08）、`space_template`（模板）。挂路由 `/space/editor` 等，按 `HasMenuAsync` 显隐。
- **`Sys_MenuAction` 操作点**：每菜单下注册动作（`menuKey:action`）——如 `space_editor:view/edit/adopt/import`、`space_code:generate`、`space_publish:publish/deactivate`、`space_viewer:view/stock`。手写控制器的 `[RequirePermission]` 键须与此处一致（§3.1）；再由 `Sys_RoleAction` 授角色。
- **`Sys_Lang` 五语词条**：菜单名 / 界面文案命名 `space.editor.title`、`space.publish.confirm` 等，五语（zh-CN/zh-TW/en/ja/…，沿用 CP6 词条资产化流程）随种子入 `Sys_Lang`。新增文案走既有 i18n 迁移指南，**不硬编码**。
- **`cp6.web` 路由**：注册 `/space/*`（`editor` / `code-rule` / `publish` / `viewer`），菜单 `MenuKey` ↔ 路由 path 对齐。

---

## 第5章 DI 注册与契约装配

### 5.1 服务 DI：`AddSpaceServices` 扩展方法（v1.1评审补丁）
在 `CP6.Core/Services/Space` 提供一个扩展方法收口 Space 全部注册，`Program.cs` 一行调用（DI 注册位置 = `Program.cs` 服务装配段）：
```csharp
// CP6.Core/Services/Space/SpaceServiceCollectionExtensions.cs
public static IServiceCollection AddSpaceServices(this IServiceCollection services)
{
    services.AddScoped<TemplateService>();         // 01 模板
    services.AddScoped<SceneService>();            // 01 场景
    services.AddScoped<SceneIoService>();          // 01 场景导入/导出
    services.AddScoped<CodeEngineService>();       // 03 编码引擎
    services.AddScoped<LocationPublishService>();  // 04 发布
    services.AddScoped<LocationLocateService>();   // 06 定位/检索/详情
    services.AddScoped<SpaceBridgeHook>();         // 04 集成 Hook（继承 BridgeHookBase）
    return services;
}
// Program.cs ： builder.Services.AddSpaceServices();
```
- 02 纯前端，无后端服务。Repo 复用 CP6 既有泛型仓储——租户过滤 / 写入盖章由 `CP6Context` 自动兜（§2），仓储层**零多租户代码**。

### 5.2 事件路由与同步 RPC 契约 DI（04，含 v1.1 新增）
- **异步事件路由**：`IntegrationEventDispatcher`（`CP6.Core/Services/Integration/IntegrationEventDispatcher.cs`）用静态 `Routes` 字典 + 反射分发，键为 `RouteKey("SRC","DST","Method")`。Space 发布在此注册一条（与现有 `RouteKey("MES","WMS",…)` 等同构）：
```csharp
[RouteKey("SPACE","WMS","OnLocationPublishedAsync")] = async ctx => {
    var p = ctx.GetPayload<OnLocationPublishedPayload>();
    var r = await ctx.Wms.OnLocationPublishedAsync(p);   // 经 IWmsBridgeHook / 消费者
    return r.Success;
};
```
`SpaceBridgeHook` 继承 `BridgeHookBase`（`CP6.Core/Services/Integration/BridgeHookBase.cs`），复用既有重试 Worker / 死信 / `IDeadLetterNotifier`。
- **同步 RPC 契约（04 v1.1 新增 `IWmsBinDeactivator`）**：停用改"同步 RPC 即时确认 + 异步兜底"后，新增 `IWmsBinDeactivator`（Space 侧抽象，调 WMS `POST /api/wms/bins/deactivate` 拿权威库存判定）。DI 绑定 WMS 实现：`services.AddScoped<IWmsBinDeactivator, WmsBinDeactivator>();`（实现在 WMS 侧注册）。Space 只依赖抽象。
- **发布溯源临时方案（v1.1评审补丁，对齐 04 v1.1）**：`BridgeHookBase` 当前把集成事件写 `Creator="system"`（`BridgeHookBase.cs:75`）——丢失发布人。P1 临时方案：发布 payload 显式带 `publishedBy`（取自当前登录用户），WMS 落到 `T_WmsBin.LastPublishedBy`（04 §5.3）。长期方案=补集成基建的用户上下文透传，届时去掉 payload 冗余字段。

### 5.3 WMS 契约 DI 一览（07/08，v1.1评审补丁）
```
IWmsStockQuery      → WMS 实现（07 库存叠加查询）
IWmsBinDeactivator  → WMS 实现（04 v1.1 停用同步 RPC，§5.2）
IWmsPickTaskQuery   → WMS 实现（08）
IWmsWorkloadQuery   → WMS 实现（08）
IWmsDeviceQuery     → WMS 桩/空实现（08 占位）
```
- Space 只依赖这些**抽象**；WMS 模块提供实现并注入。**编译期 Space 不引用 WMS 实现程序集**（单向低耦合）。

---

## 第6章 数据库迁移

### 6.1 迁移内容（00 DDL 落地）
- 9 表：`Space_Site / Floor / Zone / Aisle / Rack / Location / Template / CodeRule / Marker`。
- 关键索引（00）：`UX_Space_Location_Tenant_Code`（过滤唯一）、`IX_Space_Location_Status`、`IX_Space_CodeRule_Scope`、各表 TenantId 索引、Rack/Location `RowVersion`。**(v1.1评审补丁)** 唯一索引的 `(TenantId, …)` 前缀**不必在迁移里手写**——Space 实体只声明单列业务唯一，基建在 `OnModelCreating` 反射统一升级（§2.1，`CP6Context.cs:1951-1988`）；上表带 `Tenant` 的索引名仅表意，落库前缀由基建保证。
- 外键删除策略：00 定义的 Restrict/SetNull（+ 04 §7 删巷道/货架护栏在应用层补）。

### 6.2 迁移与种子（v1.1评审补丁）
```
dotnet ef migrations add Space_Init   （CP6.Core）
```
**种子方案（两类，随迁移落地）：**
- **默认编码规则 `Space_CodeRule`**：对 `DefaultTenant`（A1）建一条 `ScopeType=0 / IsDefault=true` 兜底规则（03 §2.3）。落法：迁移 `HasData` 或启动幂等 `Seed` 方法（仿 S 类种子）——置于 `if(!_db.Space_CodeRule.Any())` 守卫块；注意 CP6 既有踩坑：**引导/默认种子须放在 `Sys_Menus.Any()` 守卫块之外**（每启动跑 + 幂等），否则首启后不再补种。
- **预置 `Space_Template`**：系统预置货架/库区模板（01 §4.3）同样种到 `DefaultTenant`。
- **多租户上线复制**：新租户开通时，按租户**复制默认编码规则 + 预置模板**到其 `TenantId` 名下（开通流程一步动作，非每次查询回落）；运行期若某租户缺规则，回落默认并告警（W-SPACE-901）。
- **菜单/操作点/词条种子**：见 §4.3（`Sys_Menu`/`Sys_MenuAction`/`Sys_Lang` 五语）。

- 迁移并入 CP6 迁移链；多租户共享 schema、按 TenantId 隔离数据（SaaS 单库多租户，沿用 CP6）；唯一索引按租户隔离由**基建自动升级**（§2.1，`CP6Context.cs:1951-1988`），迁移**无需手写复合唯一**。

---

## 第7章 编码规则与模板的租户化

- **编码规则**（03）：按 `TenantId` 隔离（基建自动，§2.1）；每租户至少一条默认规则（种子，§6.2）；作用域规则（楼层/库区）含租户上层段保证**租户内**唯一（03 §6）。
- **模板**（01）：**每租户独立模板库隔离**（基建租户过滤自动兜）。**(v1.1评审补丁)** 模板 `clone` / 预置库克隆机制属 01 章职责（01 §9 `/template/{id}/clone`），09 不复述——本章只声明"按租户隔离"这一接入事实。
- **场景复制**（01 §7）：跨租户导入强制 ID 重映射 + 注入当前 TenantId + 清编码/状态 → 草稿（防串号；导入出 EF 边界，须显式带租户，§2.2）。

---

## 第8章 接入清单（checklist）

> 勾完即"单租户端到端跑通"（建仓→生成→编码→发布→浏览→定位→库存叠加）。
> **(v1.1评审补丁)** 每项后括号 = **怎么做**（指向具体文件/API/种子/§），从"知道做什么"变"知道怎么做"。

```
□ 数据库
  □ Space_Init 迁移已应用（9 表 + 索引）  （dotnet ef migrations add Space_Init；唯一索引只声明单列，复合升级交基建 CP6Context.cs:1951-1988）
  □ 9 表基类已改 BaseBizEntity  （CP6.Entity/DomainModels/Space/*.cs：基类 BaseEntity→BaseBizEntity，白拿 TenantId/IsDeleted/RowVersion，§2.1）
  □ 每租户种子：默认编码规则 + 预置模板库  （HasData 或幂等 Seed，对 DefaultTenant 建 ScopeType=0/IsDefault；§6.2）
□ 多租户（基建兜底，只需自检）
  □ 全局查询过滤生效  （抽查任意 Space 查询的 SQL 带 WHERE TenantId=@current；机制 CP6Context.cs:1941，Space 零代码）
  □ 写入盖章生效  （新建行 TenantId 自动=当前租户；StampTenant CP6Context.cs:2094-2103）
  □ 跨模块出口显式带 TenantId  （导入重映射 01 §7.2 / IWmsStockQuery 调用 07 / 发布 payload 04——出 EF 边界，§2.2）
□ 权限（PUB）
  □ 主数据走 CodeGen BaseCrudController（自动挂操作点 + 四粒度，§3.1）；手写控制器逐动作贴 [RequirePermission("space_xxx","action")]
  □ Sys_MenuAction 注册操作点（menuKey:action，§4.3）+ Sys_RoleAction 授角色
  □ 高危操作独立授权点  （space_publish:publish/deactivate、space_editor:adopt，不与浏览混授，§3.2）
  □ 数据权限：P1 = 租户隔离即可  （Space 无 DeptId，不做 Dept 级行权限；预留挂法见 §3.3）
□ 接入
  □ Sys_Menu 节点注册  （顶级"空间数字底座"+子节点，MenuKey space_editor 等，§4.3）
  □ cp6.web /space/* 路由 + Sys_Lang 五语词条  （路由 path↔MenuKey 对齐；文案 space.editor.title 等，§4.3）
  □ 登录后可见可达  （HasMenuAsync 按权限显隐）
□ 装配（DI）
  □ AddSpaceServices() 注册并在 Program.cs 调用  （Template/Scene/CodeEngine/Publish/定位/SpaceBridgeHook，§5.1）
  □ SPACE|WMS 事件路由注册  （IntegrationEventDispatcher Routes 加 RouteKey("SPACE","WMS","OnLocationPublishedAsync")，§5.2）
  □ WMS 契约绑定 WMS 实现  （IWmsStockQuery / IWmsBinDeactivator(04 v1.1 停用 RPC) / PickTask / Workload / Device(可桩)，§5.3）
  □ 发布溯源：payload 带 publishedBy → T_WmsBin.LastPublishedBy  （绕开 Creator="system"，§5.2 / 04 v1.1）
□ 端到端冒烟（单租户）
  □ 建 Site/Floor → 2D 模板生成货架/库位（01）
  □ 精修对齐（02）→ 生成编码（03，过唯一校验）
  □ 发布（04，过 code-precheck 闸门）→ WMS 收到 LocationPublished
  □ 3D 浏览（05）→ 按编码定位（06）
  □ （P2）库存叠加着色（07）+ 按物料定位
  □ （P3）拣货路径动画 + 作业热图（08，WMS 数据就绪时）
```

---

## 第9章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-SPACE-901 | Error | 无权访问该空间模块功能 | PUB 功能权限校验失败 |
| E-SPACE-902 | Error | 无权访问该楼层/库区数据 | PUB 数据权限拦截（跨层定位等） |
| W-SPACE-901 | Warn | 当前租户尚未配置编码规则，已用默认规则 | 租户缺规则，回落种子默认（03 §2.3） |
| I-SPACE-901 | Info | Space 模块接入校验通过 | 接入清单冒烟全过 |

---

## 第10章 集成与依赖

| 关系 | 说明 |
|---|---|
| ← 00~08 全章 | 本章是它们横切关注点（多租户/权限/装配/迁移）的统一兑现 + 接入清单 |
| ← PUB 权限引擎 | 主数据走 CodeGen `BaseCrudController` 自动挂操作点；手写控制器显式 `[RequirePermission]` + `Sys_MenuAction`；Space 无 `DeptId`→P1 数据权限=租户隔离 **(v1.1评审补丁)** |
| ← CP6 TenantId 体系 | 实体继承 `BaseBizEntity` → 全局过滤/索引升级/写入盖章**全自动**（`CP6Context.cs:1941` / `:1951-1988` / `:2094-2103`），Space **零多租户代码** **(v1.1评审补丁)** |
| ← CP6 集成基建 | SPACE\|WMS 事件路由 + BridgeHookBase + 重试/死信（04） |
| ← CP6 登录/菜单/i18n | Space 作为顶级模块接入登录后菜单 + 路由 + 多语言 |
| → WMS | 经事件（发布）+ 查询契约（库存/路径/作业）；单向低耦合 |
| 多租户 SaaS | 单库多租户、共享 schema、TenantId 隔离、统一升级（README ④） |

---

## 自检
- [ ] Space 是 CP6 的第几个顶级模块？命名空间与菜单分组什么关系？
- [ ] 多租户隔离靠什么——为什么 09 不自写过滤而是继承 `BaseBizEntity` 复用基建（全局过滤/索引升级/盖章三件套在 `CP6Context` 哪几行）？跨租户串号哪些靠基建自动、哪些是 EF 边界外须人盯？
- [ ] Space 控制器怎么挂权限（CodeGen 自动 vs 手写显式 `[RequirePermission]`+`Sys_MenuAction`）？哪些是高危操作必须独立授权？Space 无 `DeptId`，P1 数据权限到底控什么、Dept 级怎么预留？
- [ ] 事件路由怎么注册进 CP6 既有 `IntegrationEventDispatcher`（`RouteKey` 三元）？04 v1.1 新增的 `IWmsBinDeactivator` 怎么 DI 绑定、为什么 Space 不引用 WMS 实现？发布溯源临时方案怎么绕 `Creator="system"`？
- [ ] 迁移要建哪些表/索引？种子放什么？编码规则/模板怎么租户化？
- [ ] 接入清单冒烟的端到端链路是什么（从建仓到 P2/P3）？勾完代表什么？

---

## 丛书收官

至此 Space 空间数字底座细分丛书 **00~09 全部完成**：

| 阶段 | 章 | 完成标志 |
|---|---|---|
| **P1 建模底座** | 00 数据模型 · 01 编辑器+模板 · 02 精修 · 03 编码引擎 · 04 发布 WMS · 05 渲染 · 06 定位 | 建仓→生成→编码→发布→3D 浏览→按编码定位 |
| **P2 实时叠加** | 07 库存叠加 | 3D 看真实库存 + 按物料定位 + 库容率 |
| **P3 高级可视化** | 08 路径动画/热图/设备占位 | 拣货路径 3D 动画 + 作业热图 |
| **收尾接入** | 09 多租户与 CP6 接入 | 多租户隔离 + 权限 + 单租户端到端跑通 |

> 下一步：可转**实施计划**（writing-plans），按 P1→P2→P3 自底向上落码；或先为 00~09 批量生成配套 `.xlsx`（各章末已列 xlsx 清单）。

---

*实现：本章贯穿 `CP6.Entity/DomainModels/Space`（迁移）、`CP6.Core/Services/Space`（DI）、`CP6.Core/Services/Integration`（事件路由）、`cp6.web/src/views/space` + `space-viewer`（菜单/路由）。配套 xlsx（多租户自检表 / PUB 权限点清单 / DI 装配表 / 迁移与索引清单 / 接入 checklist）见同名 `.xlsx`。*
