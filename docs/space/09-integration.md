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

---

## 目录
- 第1章 功能概述与定位（收尾章做什么）
- 第2章 多租户隔离（全表 TenantId + 全局查询过滤）
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
- 多租户：全表 `TenantId` + EF 全局查询过滤的统一落地与自检。
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

## 第2章 多租户隔离（全表 TenantId）

### 2.1 全表 TenantId（00 已定，09 统一自检）
- Space 9 表（Site/Floor/Zone/Aisle/Rack/Location/Template/CodeRule/Marker）+ 发布批/事件全带 `TenantId`（00 §3 BaseEntity 已含）。
- **EF 全局查询过滤**：沿用 CP6 既有租户过滤机制（`HasQueryFilter(e => e.TenantId == _currentTenant)`），保证任何查询自动按当前租户切片——绝不裸查跨租户。

### 2.2 跨租户串号防线（汇总前各章）
| 场景 | 防线 | 章 |
|---|---|---|
| 场景导入 | 全 ID 重映射 + 注入当前 TenantId | 01 §7.2 |
| 编码唯一 | `(TenantId, LocationCode)` 过滤唯一索引 | 00/03 |
| 库存查询 | `IWmsStockQuery` 传 TenantId，WMS 按租户隔离 | 07 |
| 发布事件 | payload 带 tenantId，WMS 按租户建 bin | 04 |
- **自检要点**：每个 Space 查询/写入路径都经租户过滤；导入/发布/跨模块调用都显式带 TenantId。

---

## 第3章 权限接 PUB（四粒度）

Space 接入 CP6 既有 [PUB 权限引擎](../pub/README.md)（页面/按钮/数据行/字段四粒度 + 多角色），**不自造权限**。

### 3.1 功能权限（页面/按钮）
| 权限点（示例） | 粒度 | 章 |
|---|---|---|
| Space.Editor.View / Edit | 页面/按钮 | 01/02 建模、精修 |
| Space.Template.Manage | 按钮 | 01 模板 CRUD |
| Space.Code.Generate | 按钮 | 03 生成/重排编码 |
| Space.Publish / Deactivate | 按钮 | 04 发布/停用（高危） |
| Space.Adopt / Import | 按钮 | 04/01 采纳/导入 |
| Space.Viewer.View | 页面 | 05/06 3D 浏览 |
| Space.Stock.View | 按钮 | 07 库存叠加 |
- 高危操作（发布/停用/删除/采纳）必须独立权限点，不与浏览混授。

### 3.2 数据权限（行/字段）
- **数据行**：场景/库位查询接 PUB 数据权限（按 `Sys_Dept` 等维度，PUB 依赖组织域）——用户只见有权的 Site/Floor。
- **字段**：敏感字段（如成本/容量）按 PUB 字段权限控制可见性（v1 字段权限按需，多为预留）。
- 跨层定位（06）受数据权限：无权楼层不可达、不可定位。

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

---

## 第5章 DI 注册与契约装配

### 5.1 服务 DI
```
CP6.Core/Services/Space：
  TemplateService / SceneService / SceneIoService（01）
  （02 纯前端，无后端服务）
  CodeEngineService（03）
  LocationPublishService（04）
  定位查询服务（06：locate/search/detail）
全部 AddScoped 注入；Repo 复用 CP6 既有泛型仓储 + 租户过滤。
```

### 5.2 事件路由（04 接 CP6 集成基建）
```
IntegrationEventDispatcher 注册新路由（04 §2）：
  RouteKey("SPACE","WMS","OnLocationPublishedAsync") → IWms…Consumer.OnLocationPublishedAsync
SpaceBridgeHook 继承 BridgeHookBase；复用既有重试 Worker / 死信 / IDeadLetterNotifier。
```

### 5.3 WMS 查询契约 DI（07/08）
```
IWmsStockQuery     → WMS 实现（07；04 停用校验也用）
IWmsPickTaskQuery  → WMS 实现（08）
IWmsWorkloadQuery  → WMS 实现（08）
IWmsDeviceQuery    → WMS 桩/空实现（08 占位）
```
- Space 只依赖这些**抽象**；WMS 模块提供实现并注入。编译期 Space 不引用 WMS 实现程序集（单向低耦合）。

---

## 第6章 数据库迁移

### 6.1 迁移内容（00 DDL 落地）
- 9 表：`Space_Site / Floor / Zone / Aisle / Rack / Location / Template / CodeRule / Marker`。
- 关键索引（00）：`UX_Space_Location_Tenant_Code`（过滤唯一）、`IX_Space_Location_Status`、`IX_Space_CodeRule_Scope`、各表 TenantId 索引、Rack/Location `RowVersion`。
- 外键删除策略：00 定义的 Restrict/SetNull（+ 04 §7 删巷道/货架护栏在应用层补）。

### 6.2 迁移与种子
```
dotnet ef migrations add Space_Init   （CP6.Core）
种子：每租户初始化一条 ScopeType=0 IsDefault 兜底编码规则（03 §2.3）+ 系统预置货架/库区模板库（01 §4.3）
```
- 迁移并入 CP6 迁移链；多租户共享 schema、按 TenantId 隔离数据（SaaS 单库多租户，沿用 CP6）。

---

## 第7章 编码规则与模板的租户化

- **编码规则**（03）：按 `TenantId` 隔离；每租户至少一条默认规则（种子）；作用域规则（楼层/库区）也含租户上层段保证全局唯一（03 §6）。
- **模板**（01）：货架/库区模板租户级；系统预置模板库可被租户 `clone`（01 §9 `/template/{id}/clone`）到自己名下再改。
- **场景复制**（01 §7）：跨租户导入强制 ID 重映射 + 注入当前 TenantId + 清编码/状态 → 草稿（防串号）。

---

## 第8章 接入清单（checklist）

> 勾完即"单租户端到端跑通"（建仓→生成→编码→发布→浏览→定位→库存叠加）。

```
□ 数据库
  □ Space_Init 迁移已应用（9 表 + 索引 + 唯一/状态/作用域索引）
  □ 每租户种子：默认编码规则 + 预置模板库
□ 多租户
  □ 9 表 + 发布批/事件全带 TenantId
  □ EF 全局查询过滤生效（抽查任意查询自动切租户）
  □ 导入/发布/库存查询显式带 TenantId
□ 权限（PUB）
  □ Space 功能权限点注册（建模/模板/编码/发布/停用/采纳/浏览/库存）
  □ 高危操作（发布/停用/删除/采纳）独立授权
  □ 场景/库位查询接数据权限（按 Sys_Dept）
□ 接入
  □ Space 菜单节点注册（顶级模块，按权限显隐）
  □ cp6.web /space/* 路由 + i18n 文案
  □ 登录后可见可达
□ 装配（DI）
  □ Space Services 注入（Template/Scene/CodeEngine/Publish/定位）
  □ SPACE|WMS 事件路由注册 + SpaceBridgeHook
  □ IWmsStockQuery/PickTask/Workload/Device 契约绑定 WMS 实现（Device 可桩）
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
| ← PUB 权限引擎 | Space 接入四粒度权限（功能/数据/字段）；不自造权限；数据权限依赖组织域 |
| ← CP6 TenantId 体系 | 全表 TenantId + EF 全局过滤，沿用既有 |
| ← CP6 集成基建 | SPACE\|WMS 事件路由 + BridgeHookBase + 重试/死信（04） |
| ← CP6 登录/菜单/i18n | Space 作为顶级模块接入登录后菜单 + 路由 + 多语言 |
| → WMS | 经事件（发布）+ 查询契约（库存/路径/作业）；单向低耦合 |
| 多租户 SaaS | 单库多租户、共享 schema、TenantId 隔离、统一升级（README ④） |

---

## 自检
- [ ] Space 是 CP6 的第几个顶级模块？命名空间与菜单分组什么关系？
- [ ] 多租户隔离靠什么（全表 TenantId + EF 全局过滤）？跨租户串号的防线散落在哪几章、本章怎么汇总？
- [ ] Space 为什么接 PUB 而非自造权限？哪些是高危操作必须独立授权？数据权限控什么？
- [ ] 事件路由怎么注册进 CP6 既有 Dispatcher？WMS 查询契约怎么 DI 绑定、为什么 Space 不引用 WMS 实现？
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
