# CP6 PUB 公共平台 · 完整设计与实现丛书

> **定位**：CP6 补了财务、采购、审批/OA，但有一层一直没搞定——**权限配置**。现在只有最浅的一层（单角色 + 页面级菜单授权），缺按钮级、数据行级、字段级，也没有后端强校验。同时字典/采番/多语言/日志/附件这些**公共能力**散在各处、没统一。本模块把这两件事收成一个**平台底座**：一套完整权限引擎 + 统一的公共模组，供 ERP/MES/WMS/OA 所有业务模块复用。
>
> 风格沿用 [`docs/finance`](../finance/README.md)、[`docs/procurement`](../procurement/README.md)、[`docs/approval`](../approval/README.md)：真实代码当教材，每章讲为什么这么设计、不这么写会出什么事、与业界（VOL/RuoYi/SAP 授权对象）怎么对比。
>
> 需求基线：完整权限引擎（页面/按钮/数据行/字段 四粒度）/ 多角色 / DataScope 五范围 / 前端隐藏+后端强校验 / 公共模组（字典·采番·多语言·日志纳管 + 附件·通用导入导出·通用CRUD基座新建）。

---

## 一、先记住这一句话（题眼）

> **权限不是"前端藏菜单"，而是"后端每个操作和每条数据都被校验"。一套权限引擎 = 功能权限（你能点什么）× 数据权限（你能看哪些行）× 字段权限（你能看哪些列），三者按用户的多角色取并集求出。前端隐藏只是体验，后端强校验才是安全。**

CP6 现状只做到"前端藏菜单"，绕过前端直接调 API 就裸奔。本模块的核心是把校验**下沉到后端**：操作级用特性 `[RequirePermission]`、数据级用 `DataScope` 查询注入、字段级在序列化时按角色掩码。

---

## 二、现状盘点（为什么"权限没搞定"）

| 能力 | 现有 | 缺口 |
|---|---|---|
| 用户↔角色 | `Sys_User.RoleId` 单个 int | ❌ 一人只能一个角色，多职能场景塞不下 |
| 页面/菜单级 | `Sys_Menu` 树 + `Sys_RoleMenu` | ⚠️ 有，但只前端隐藏、无后端校验 |
| 按钮/操作级 | 无 | ❌ 增删改查/导出/审批 不可控 |
| 数据行级 | 无 | ❌ "只看本部门数据"算不出来 |
| 字段级 | 无 | ❌ 价格/成本对某些角色该隐藏 |
| 后端强校验 | 仅 `[Authorize]`（登录即放行） | ❌ 无操作/数据级强校验 |
| 公共能力 | 字典/采番/多语言/日志散在 Sys/Common | ⚠️ 未统一纳管；附件/导入导出/codegen 缺失 |

---

## 三、模块边界与命名空间

```
┌──────────────── PUB 公共平台（顶级，与 ERP/MES/WMS/OA 平级）────────────────┐
│ 权限引擎：多角色 RBAC × 功能权限(菜单/按钮) × 数据权限 DataScope × 字段权限   │
│ 公共模组：字典 · 采番 · 多语言 · 日志（纳管）+ 附件 · 通用导入导出 · CRUD基座 │
└───┬──────────────────────────┬──────────────────────────────────────────┘
    │被所有业务模块消费             │复用
    ▼                            ▼
 各模块 [RequirePermission] +   Sys_Dept 部门树（OA阶段0 产物，
 IDataScopeFilter 查询注入       PUB 数据权限与 OA 审批路由共用）
```

**命名空间策略（避免大重构）：**
- 现有 `CP6.*.Sys`（用户/角色/菜单/字典/日志/多语言/部门）**不改名**；权限引擎新表仍走 `Sys_` 前缀（系统表），落 `DomainModels/Sys`、`Services/Sys`。
- 新公共基建（附件/导入导出/codegen）落新命名空间 `Pub`：`DomainModels/Pub`、`Services/Pub`。
- **"PUB" 是菜单/产品层的伞名**，统辖 Sys + Common + Pub，不强行把 Sys 重命名。

> **关键依赖（2026-06-12 复审定稿）**：数据权限 DataScope 依赖 `Sys_Dept` 部门树。**组织模型(Sys_Dept) 归 PUB（Sys_ 基座的一部分），作为 PUB 前置先落；OA 审批路由消费它，不重复建。** 做一次两处用，归属在 PUB——与"PUB 统辖 Sys_"一致。

---

## 四、最小数据模型（贯穿全书）

```
■ 多角色（升级 RBAC）—— DomainModels/Sys
  Sys_UserRole       UserId, RoleId          ← 新建中间表；Sys_User.RoleId 保留为"主角色"兼容迁移

■ 功能权限（菜单级已有 + 操作级新增）—— DomainModels/Sys
  Sys_Menu (已有)                            ← 页面级，保留
  Sys_RoleMenu (已有)                        ← 角色↔菜单，保留（升级为后端也校验）
  Sys_MenuAction    MenuId, ActionCode(add/del/edit/export/approve…), Name   ← 给页面挂操作点
  Sys_RoleAction    RoleId, MenuId, ActionCode    ← 按钮级授权

■ 数据权限 DataScope —— DomainModels/Sys
  Sys_RoleDataScope RoleId, ResourceKey, ScopeType(1本人/2本部门/3及下级/4自定义/5全部), CustomDeptIds

■ 字段级权限 —— DomainModels/Sys
  Sys_RoleFieldPerm RoleId, ResourceKey, FieldName, Access(1可读/2只读/3隐藏)

■ 公共模组 —— DomainModels/Pub（新建）+ 纳管现有
  Pub_Attachment    BizType, BizId, FileName, StorePath, Size, Uploader     ← 新建
  (字典 Sys_DictType/DictData · 采番 DocSequence · 多语言 Sys_Lang · 日志 Sys_OperLog —— 纳管现有)
```

> **三权合一的锚**：用户登录 → 取其全部角色（`Sys_UserRole`）→ 菜单/操作取**并集**、DataScope 取**最宽**、字段权限取**最宽**。这套结果缓存到会话，后端每次操作/查询据此校验。

---

## 五、三条核心校验链

### 链 A — 功能权限（你能点什么）
```
请求到 API → [RequirePermission("order","export")] 特性
  → IPermissionService 查当前用户多角色的 Sys_RoleAction 并集
  → 命中放行 / 不命中 403
前端：同一份操作权限下发，按钮 v-permission 指令隐藏（仅体验）
```

### 链 B — 数据权限（你能看哪些行）
```
service 查询 → IDataScopeFilter.Apply(query, "order", userCtx)
  → 取该角色对 order 的 ScopeType：
      ├ 1本人   → where Creator == 当前用户
      ├ 2本部门 → where DeptId == 用户部门
      ├ 3及下级 → where 记录部门.Path like 用户部门.Path + '%'   (物化路径子树)
      ├ 4自定义 → where DeptId in CustomDeptIds
      └ 5全部   → 不加过滤
  → 多角色取最宽范围
```

### 链 C — 字段权限（你能看哪些列）
```
返回 DTO 序列化 → 按 Sys_RoleFieldPerm：
  ├ 只读 → 前端禁用编辑
  └ 隐藏 → 后端置空/脱敏该字段（如成本、价格）
```

---

## 六、章节目录

### Part 0 · 组织模型（PUB 基座前置，OA 共用）
- [00. 组织模型（部门树 Sys_Dept）](./00-org-model.md) — **M0 前置**，`Sys_Dept` 树（物化路径 `Path` + `LeaderId` 部门长）+ `Sys_User` 补 `DeptId/ManagerId/Email`。**双消费方**：PUB 数据权限用 `Path` 做子树过滤；OA 审批路由用 `LeaderId/ManagerId` 解析"直属上级/部门长"。归 PUB 先落，OA 消费

### Part 1 · 权限引擎（痛点核心）
- [01. 多角色 RBAC 升级](./01-rbac-multirole.md) — **M0**，`Sys_UserRole` + 权限并集求解
- [02. 功能权限：菜单 + 按钮 + 后端强校验](./02-function-permission.md) — **M1 ★**，`[RequirePermission]` 特性
- [03. 数据权限 DataScope](./03-data-scope.md) — **M2**，五范围 + 部门树过滤 + 查询注入（依赖 Sys_Dept）
- [04. 字段级权限](./04-field-permission.md) — **M3**，序列化掩码

### Part 2 · 公共模组
- [05. 公共基础纳管](./05-common-foundation.md) — 字典/采番/多语言/日志 可视化配置
- [06. 附件 / 文件统一管理](./06-attachment.md) — **新建**
- [07. 通用导入导出](./07-import-export.md) — **新建**，Excel 模板框架
- [08. 通用 CRUD 基座 / 代码生成](./08-codegen.md) — **新建**，提速后续模块

### Part 3 · 集成
- [09. 与 CP6 集成](./09-integration.md) — 与 OA 组织共用 Sys_Dept、各业务模块强校验接入

---

## 七、推荐构建顺序（痛点驱动：权限优先）

| 里程碑 | 含章节 | 完成标志 |
|---|---|---|
| **M0 前置** 组织模型 | 00 | 部门树 `Sys_Dept` + 物化路径 `Path` 建好，OA 可消费 |
| **M0** 多角色 | 01 | 一人多角色，权限并集算得出来 |
| **M1 ★** 功能权限 | 02 | 菜单+按钮授权，**后端 `[RequirePermission]` 强校验生效**，绕过前端也挡得住 |
| **M2** 数据权限 | 03 | "只看本部门/及下级"在查询层自动过滤（复用 Sys_Dept） |
| **M3** 字段级 | 04 | 价格/成本对指定角色隐藏 |
| **M4** 公共模组 | 05·06·07·08 | 字典纳管 + 附件 + 导入导出 + codegen 可用 |
| **M5** 接入 | 09 | 各业务模块挂上强校验与 DataScope |

> **与 OA 的协同（复审定稿）**：`Sys_Dept` 部门树是两边共同前置，**归 PUB（章 00），作为 PUB 基座先落**；OA 流程引擎/审批路由直接消费，不重复建。原 [OA 阶段0 计划](../superpowers/plans/2026-06-10-approval-stage0-org-model.md) 中"新建组织模型"的部分相应改为"消费 PUB 组织模型"。

---

## 八、复用 vs 新建

| 能力 | CP6 现成的 | 怎么用 |
|---|---|---|
| 用户/角色/菜单 | `Sys_User`/`Sys_Role`/`Sys_Menu`/`Sys_RoleMenu` | 复用；补多角色中间表与后端校验 |
| 部门树（数据权限靠它） | `Sys_Dept`（**归 PUB 章 00 新建**，OA 共用） | DataScope 子树过滤用物化路径 `Path` |
| 字典/采番/多语言/日志 | `Sys_Dict*`/`DocSequence`/`Sys_Lang`/`Sys_OperLog` | 纳管，补可视化配置页 |
| 登录鉴权 | `JwtHelper` + `[Authorize]` | 复用；在其上叠操作/数据/字段校验 |
| 附件/导入导出/codegen | 无 | **新建**，落 `Pub` 命名空间 |

---

## 九、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 多角色 + 数据权限(VOL 风格) | **VOL.Core / RuoYi** | UserRole 中间表、DataScope 枚举、字段权限掩码 |
| 操作级授权 | **ASP.NET Core Authorization Policy / Permission-based** | `[RequirePermission]` 与 PolicyHandler |
| 企业级授权对象 | **SAP 授权对象 / 字段组** | 把"操作+组织范围+字段"抽象成可配对象 |

> VOL/RuoYi 的"数据权限（全部/本部门/本部门及以下/仅本人/自定义）"就是本书的 DataScope——核心模型与全世界一致。

---

## 十、里程碑自检

- [ ] 一个用户挂两个角色，菜单/操作/数据范围分别怎么合并？（操作并集、范围取最宽）
- [ ] "只看本部门及下级"靠 `Sys_Dept` 的哪个字段、怎么过滤？（物化路径 `Path` 前缀匹配子树）
- [ ] 为什么前端隐藏不算安全？后端在哪三个点强校验？（操作=特性、数据=查询注入、字段=序列化掩码）
- [ ] DataScope 为什么和 OA 审批共用 `Sys_Dept`？谁先落？
- [ ] 字段级"隐藏成本"是前端不显示还是后端置空？（后端置空/脱敏，否则抓包可见）

全部能答 → CP6 的权限从"前端藏菜单"升级为"后端三权强校验"，并有了统一的公共底座，所有业务模块（含 OA/采购/财务）都能挂上来。

---

*生成于 2026-06-11，2026-06-12 复审定稿。需求基线见首部。配套实现将落于 `CP6.Entity/DomainModels/{Sys,Pub}`、`CP6.Core/Services/{Sys,Pub}`、`cp6.web/src/views/pub`（随章节推进）。组织模型 `Sys_Dept` 归 PUB（章 00）先落，OA 共用。细分章节按 M0~M5 里程碑分批写，**产出格式 = `.md` + `.xlsx`（详细规格/线框图，不再 .docx）**。*
