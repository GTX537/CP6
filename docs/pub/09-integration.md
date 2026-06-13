# PUB 09 · 与 CP6 集成 详细需求规格（全书收尾）

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | PUB-09 与 CP6 集成 |
| 所属模块 | PUB 公共平台 · Part 3 集成（收尾） |
| 里程碑 | **M5**（各业务模块挂上强校验与公共能力） |
| 技术栈 | Vue3 + Element Plus / .NET8（登录管线 + 中间件）/ SQL Server |
| 命名空间 | `Sys` / `Pub` / 各业务模块 |
| 性质 | 集成落地（把底座接到全 CP6） |

> **题眼**：前八章把 PUB 平台底座建齐了——权限四粒度、组织、字典、采番、附件、导入导出、CRUD 基座。本章是**落地**：各业务模块（财务/采购/MES/WMS/OA）怎么挂上 PUB 的强校验与公共能力，登录时怎么聚合权限上下文，与 OA 怎么共用组织模型。本章结束，CP6 从"前端藏菜单"全面升级为"后端三权强校验 + 统一公共能力"。

---

## 目录
- 第1章 概述（底座 → 全模块接入）
- 第2章 接入矩阵（模块 × PUB 能力）
- 第3章 登录聚合管线（权限上下文从哪来）
- 第4章 与 OA 协同（Sys_Dept 共用）
- 第5章 单模块接入步骤
- 第6章 现有模块改造清单
- 第7章 接入检查清单
- 第8章 分阶段接入路线
- 第9章 全书收尾（PUB 全景）
- 第10章 API / 消息
- 第11章 自检

---

## 第1章 概述

PUB 是"被所有业务模块消费的平台底座"。集成 = 让每个模块**消费**这些能力，而不是各做各的：

| 能力 | 提供方（PUB 章） | 消费方式 |
|---|---|---|
| 多角色权限 | 章01 | 登录聚合 `UserPermissionContext` |
| 操作强校验 | 章02 | Controller 贴 `[RequirePermission]` |
| 数据权限 | 章03 | service 接 `IDataScopeFilter.Apply` |
| 字段权限 | 章04 | 查询贴 `[FieldMask]`、更新走 `StripReadOnly` |
| 字典/采番/日志 | 章05 | 注入 `IDictService`/`DocSequence`/`IOperLogger` |
| 附件 | 章06 | `<PubUpload bizType bizId>` |
| 导入导出 | 章07 | `IExcelService` + 列配置 |
| CRUD 基座/生成 | 章08 | 继承 `BaseCrudService/Controller` 或代码生成 |
| 组织模型 | 章00 | DataScope 用 / OA 审批路由用 |

---

## 第2章 接入矩阵（模块 × PUB 能力）

| 模块 | 操作强校验 | 数据权限 | 字段权限 | 字典 | 采番 | 附件 | 导入导出 | 组织 |
|---|---|---|---|---|---|---|---|---|
| 采购 | ✔ | ✔（按部门看单） | ✔（成本隐藏） | ✔ | ✔（PO/PR 号） | ✔（合同/报价单） | ✔ | ✔ |
| 财务 | ✔ | ✔ | ✔（利润/成本） | ✔ | ✔（凭证/发票号） | ✔（凭证扫描） | ✔ | ✔ |
| 销售(MSBB) | ✔ | ✔ | ✔（价格） | ✔ | ✔（受注/见积号） | ✔（图纸） | ✔ | ✔ |
| MES | ✔ | ✔（按车间） | ○ | ✔ | ✔（工单号） | ○ | ✔ | ✔ |
| WMS | ✔ | ✔（按仓库） | ○ | ✔ | ✔（出入库号） | ○ | ✔ | ✔ |
| OA | ✔ | ✔ | ○ | ✔ | ✔（流程号） | ✔（审批附件） | ○ | ✔（路由） |

> ✔=接入，○=按需。**组织模型一列对所有模块都是 ✔**：数据权限按部门、OA 审批按上级/部门长，都依赖章00 的 `Sys_Dept`。

---

## 第3章 登录聚合管线（权限上下文从哪来）

```
登录成功（JwtHelper 验通过）
  → PermissionAggregator.BuildAsync(userId)             // 章01
      ├ 取全部角色（Sys_UserRole ∪ 主角色）
      ├ ActionKeys  = 多角色操作并集（章02）
      ├ DataScopes  = 多角色范围最宽（章03）
      ├ FieldPerms  = 多角色字段最宽（章04）
      └ 组织字段 DeptId/DeptPath/UserName（章00）
  → 缓存 UserPermissionContext（会话 / Redis，键=userId）
  → 每次请求：中间件从缓存取 ctx 注入 ICurrentContext
      → [RequirePermission] 读 ActionKeys
      → IDataScopeFilter 读 DataScopes/DeptPath
      → [FieldMask] 读 FieldPerms
缓存失效：用户角色变更 / 角色权限变更 / 角色删 → 失效该用户（或该角色下全部用户）ctx
```

> **集成的核心枢纽是 `UserPermissionContext`**：登录算一次、缓存、请求时注入。三权校验全读它，不每次查库。它把章00-04 的产物聚到一处，是整个权限引擎的"运行时单一事实源"。

---

## 第4章 与 OA 协同（Sys_Dept 共用）

```
PUB 章00 建组织模型 Sys_Dept（树 + Path + LeaderId + Sys_User.DeptId/ManagerId）
   ├ PUB 数据权限（章03）：用 Path 做"本部门及下级"子树过滤
   └ OA（阶段1）：IApproverResolver 用 LeaderId/ManagerId 解析"直属上级/部门长"
做一次两处用，归 PUB 先落，OA 消费、不重复建。
```

> 这是 2026-06-12 复审定稿的关键协同：组织模型是 PUB 与 OA 的共同前置，归属在 PUB（统辖 `Sys_`），OA 审批路由直接消费。详见 [章00](./00-org-model.md) 与 [OA 总纲](../approval/README.md)。

---

## 第5章 单模块接入步骤

任意业务模块接入 PUB 的标准步骤：

```
① 实体：业务实体实现 IDataScoped（Creator/DeptId），无 DeptId 的只支持 本人/全部
② 注册：
   - 菜单 + 操作点（Sys_Menu + Sys_MenuAction：add/edit/del/query/export/import…）
   - 数据资源（DataScopeRegistry.Register：支持范围 + 默认）
   - 可控字段（FieldRegistry.Register：哪些字段可做字段权限）
   - 采番规则（DocSequence：业务键 + 规则）
③ 后端：
   - Controller 贴 [RequirePermission(menu,action)]（或继承 BaseCrudController）
   - 查询 service 接 IDataScopeFilter.Apply（或继承 BaseCrudService）
   - 查询贴 [FieldMask(resource)]、保存调 StripReadOnly
   - 枚举接 IDictService、单号接 DocSequence.NextAsync
④ 前端：
   - 按钮贴 v-permission、字典下拉接 /api/pub/dict
   - 需要附件挂 <PubUpload>、列表接导入导出
⑤ 运营：在 PUB 授权画面给角色配 菜单/操作/数据范围/字段权限
```

---

## 第6章 现有模块改造清单

| 模块 | 改造要点 |
|---|---|
| 采购 | PO/PR/GR 等实体补 `DeptId`；Controller 贴权限；成本/价格配字段权限；合同附件挂 PubUpload |
| 财务 | 凭证/发票实体补 `DeptId`；利润/成本字段权限；凭证扫描接附件；采番接现有单号规则 |
| 销售(MSBB) | 受注/见积/製品 实体接 IDataScoped；价格字段权限；图纸附件 |
| MES/WMS | 按车间/仓库做数据权限（DeptId 映射车间/仓库或扩展资源维度）；工单/出入库采番纳管 |
| OA | 消费 Sys_Dept 做审批路由；审批附件挂 PubUpload |

> 改造是**渐进**的：先接操作强校验（最痛点、最快见效），再接数据权限，最后字段权限/附件/导入导出。不必一次全接。

---

## 第7章 接入检查清单

每个模块接入时逐项核对：

- [ ] 实体实现 `IDataScoped`？无 DeptId 的范围限制清楚？
- [ ] 菜单 + 操作点已注册（Sys_Menu/Sys_MenuAction）？
- [ ] Controller 贴 `[RequirePermission]`（绕过前端也挡）？
- [ ] 查询接 `IDataScopeFilter.Apply`（按范围收窄）？
- [ ] 敏感字段配 `[FieldMask]` + `StripReadOnly`？
- [ ] 枚举走字典、单号走采番？
- [ ] 附件/导入导出按需接入？
- [ ] 在授权画面配好角色的四粒度权限？

---

## 第8章 分阶段接入路线

| 阶段 | 内容 | 完成标志 |
|---|---|---|
| M0 前置 | 组织模型（章00） | Sys_Dept 就绪，OA/数据权限可用 |
| M1 ★ | 操作强校验（章01-02）接各模块 | 绕过前端调 API 被 403 挡住 |
| M2 | 数据权限（章03）接各模块 | "只看本部门/及下级"生效 |
| M3 | 字段权限（章04）接敏感字段 | 成本/价格对指定角色隐藏 |
| M4 | 公共能力（章05-08）接各模块 | 字典/采番/附件/导入导出统一 |
| M5 | 全量接入 + 运营配权限 | 各模块挂齐，授权可视化配置 |

---

## 第9章 全书收尾（PUB 全景）

```
┌──────────────────── PUB 公共平台（底座）────────────────────┐
│ 组织模型(00) ┐                                              │
│ 权限引擎：多角色(01)→功能(02)→数据(03)→字段(04)  四粒度强校验 │
│ 公共模组：字典/采番/多语言/日志(05)+附件(06)+导入导出(07)     │
│ CRUD 基座/代码生成(08)：把上面能力一键装配进新模块            │
│ 集成(09)：登录聚合 UserPermissionContext，各模块挂接          │
└───────────────────────────┬─────────────────────────────────┘
        被消费                │ 共用组织
   ┌────────────────┐         ▼
   采购/财务/销售/MES/WMS    OA 审批路由（消费 Sys_Dept）
   （强校验 + 公共能力）
```

> **PUB 丛书（00-09）至此全齐**：从组织模型到代码生成到集成，把"权限配置"和"公共能力"两件散乱的事收成一个平台底座。CP6 的所有业务模块——既有的销售/MES/WMS、新增的财务/采购/OA——都挂在这个底座上，享受统一的后端三权强校验与公共基建。权限从"前端藏菜单"升级为"后端三权强校验"，公共能力从"各写各的"升级为"一处建、处处用"。

---

## 第10章 API / 消息

- API：本章不新增独立端点；集成点在登录管线（聚合 + 缓存）、中间件（注入 ctx）、各模块 Controller（贴特性）。
- 授权配置 API 复用章02-05 的配置端点。
- 消息：复用各章消息；集成期新增 `E-PUB-091 未配置任何权限的用户登录提示`（可选，提示管理员去配权限）。

---

## 第11章 自检
- [ ] PUB 与各业务模块是什么关系（提供/消费）？
- [ ] 登录聚合管线把哪些章的产物聚到 UserPermissionContext？请求时怎么用？
- [ ] 组织模型为什么 PUB 与 OA 共用？谁建谁消费？
- [ ] 一个新业务模块接入 PUB 的标准步骤是什么？
- [ ] 现有模块改造为什么渐进？先接哪个最快见效？

全部能答 → PUB 平台底座落地，CP6 完成"后端三权强校验 + 统一公共能力"的升级，所有业务模块挂上同一底座。

---

*实现：登录管线集成 `PermissionAggregator` + 缓存 + 中间件注入 `ICurrentContext`；各模块按第5章步骤接入；与 OA 共用 `Sys_Dept`。本章完成 → PUB 丛书（00-09）全齐。配套 xlsx 详细设计见同名 `.xlsx`。*
