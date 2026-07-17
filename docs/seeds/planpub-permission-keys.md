# 计划中台(Plan)+公共(Pub)写端点 × 権限键清单（M-PLAN/PUB Task 1 真相源）

> 生成于 2026-07-17。本表是 **M-PLAN/PUB 横切接线波（第六波=收尾波）的唯一真相源**：T2（`Sys_MenuAction`/`Sys_RoleAction` 逐租户种子 + 菜单 731/732 MenuKey 首启就位修复）、T3（逐端点贴 `[RequirePermission("menu-key","action")]`）、反射 fail-closed 测试 + 403 用例均以本表为准。
> 依据：`docs/00-横切接线规范.md` 第一章（功能级四粒度）+ 同型先例 `docs/seeds/pur-permission-keys.md`（§一~§七 结构照抄，五波最强形态：豁免须逐条 Service 读证）+ 现有 Plan/Pub 菜单 `CP6.WebApi/Program.cs`（Pub 108–113 :966–1006 / Plan 730–732 :1526–1542）+ 逐 Service 实现读证的只读 POST 豁免判定。
> 扫描范围：`CP6.WebApi/Controllers/Plan/`（**2 控制器**：Mrp / ItemPlanningPolicy）+ `CP6.WebApi/Controllers/Pub/`（**3 控制器**：Attachment / CodeGen / Seq）= **5 控制器全量**。
> **本任务只产出本文档，不改任何控制器/种子/测试/前端代码。**
> **5 控制器均为裸控制器**：仅类级 `[Authorize]`（登录闸），**零 `[RequirePermission]` 既有贴点**（grep 实证 Plan/Pub 目录 0 命中）。全部键为新键。

## 约定

- **资源键 = `{menu-key}:{action}`**，**menu-key 一律连字符小写、绝对禁止下划线**（全仓 100% 连字符）。Plan 冠 `plan-` 域前缀，Pub 冠 `pub-` 域前缀，均由既有菜单 RoutePath 天然派生（`/plan/mrp`→`plan-mrp`、`/pub/seq`→`pub-seq`）。
- **键锚定「消费页菜单」的 MenuKey**（`PermissionAggregator = Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → {MenuKey}:{ActionCode}`）。逐键给出锚定菜单 MenuId/RoutePath。
- **`高危?` 列三值**（沿用 WMS/ERP/MES/OA/PUR 定义）：
  - `是` = 触及**不可逆/重算写、转单建采购/生产承诺、生成写盘覆盖既有元数据**。一次误授即他人可越权重算计划、下达 PR/工单承诺或覆写代码生成模板。T3 贴点最高优先级，绝不与 view/edit 混授。
  - `状态` = 独立工作流状态流转（确认/忽略计划订单），单独成键、不塞 edit/view。
  - `否` = 四基粒度 `add/edit/delete` 之一。
- **只读 POST 豁免**：纯查询/预览类 POST 归 `view`，§四逐条附**读 Service/控制器实现证得的**无写副作用依据（文件:行）。GET 端点一律不列。
- **组件豁免**：无独立页面/菜单行的横切组件（Attachment）端点，归 §四 组件豁免清单（登入 fail-closed 反射测试的显式豁免表，不铸键），裁定理由见 §五.4。

---

## 一、写端点映射表（POST/PUT/DELETE，共 14 行）

| # | 控制器 | HTTP方法 + 路由 | 方法名 | 建议 menu-key | action | 高危? | 备注 |
|---|---|---|---|---|---|---|---|
| 1 | MrpController | POST `/api/plan/mrp/run` | Run | `plan-mrp` | run | **是** | **MRP 全量重算**：建运算批次、作废重生建议态计划订单、逐层 net 生成 PlannedOrder/NetRequirement（MrpEngine.RunAsync 大量写 + 采番）。重算写、独立成键（详§三） |
| 2 | MrpController | POST `/api/plan/mrp/planned-order/{id}/confirm` | Confirm | `plan-mrp` | confirm | 状态 | 建议→已确认，进供给（PlanConvertService.ConfirmAsync:22-31，仅置 Status，无跨模块写） |
| 3 | MrpController | POST `/api/plan/mrp/planned-order/{id}/convert` | Convert | `plan-mrp` | convert | **是** | **转单=创建采购/生产承诺**：采购类→`IPlanToPrService.CreatePrFromPlannedOrderAsync`（生成 PR），生产类→`IPlanToWorkOrderService`（生成工单）。当前委托 P1 桩（详§三 + §六 必带票） |
| 4 | MrpController | POST `/api/plan/mrp/planned-order/{id}/ignore` | Ignore | `plan-mrp` | ignore | 状态 | 置已忽略、不计供给（PlanConvertService.IgnoreAsync:56-65，仅置 Status） |
| 5 | ItemPlanningPolicyController | POST `/api/plan/item-policy` | Upsert | `plan-item-policy` | add | 否 | 品目计划策略 upsert（按 itemCd 建/改，主数据维护）。沿用 Pur `pur-supplier-price:add` upsert→add 先例 |
| 6 | ItemPlanningPolicyController | DELETE `/api/plan/item-policy/{itemCd}` | Delete | `plan-item-policy` | delete | 否 | 删除品目策略（Plan 域用 `delete` 非 `del`） |
| 7 | CodeGenController | POST `/api/pub/codegen/save` | Save | `pub-codegen` | save | **是** | **代码生成元数据写盘**：整体 upsert `Pub_GenTables` + `RemoveRange` 旧列后重插 `Pub_GenColumns`（CodeGenController:27-53）。覆盖既有列定义、驱动脚手架产物，独立高危键（详§三） |
| 8 | CodeGenController | POST `/api/pub/codegen/preview` | PreviewInline | `pub-codegen` | view | 只读POST→view | body 直传元数据即时生成，`_gen.Generate(req.Table, req.Columns)` 纯内存、**无 `_db`/SaveChanges**（CodeGenController:66-68）。POST 仅为传 body。§四豁免 |
| 9 | AttachmentController | POST `/api/pub/attachment/upload` | Upload | （组件豁免） | — | 组件豁免 | 统一附件上传，无独立页面/菜单行（横切组件嵌入各业务页）。§四组件豁免、§五.4 裁定 |
| 10 | AttachmentController | DELETE `/api/pub/attachment/{id}` | Delete | （组件豁免） | — | 组件豁免（高危-shaped） | 引用计数后物理删附件。删除本属高危形态，但无菜单可锚→组件豁免，并列 §六 follow-up（扩 EnforceBizPermission 至写端点） |
| 11 | AttachmentController | POST `/api/pub/attachment/rebind` | Rebind | （组件豁免） | — | 组件豁免 | 草稿转正：draftToken 附件回填 BizId（业务单据保存后调） |
| 12 | SeqController | POST `/api/pub/seq` | Add | `pub-seq` | add | 否 | 建富采番规则（BizKey 唯一，SeqController:31-39） |
| 13 | SeqController | PUT `/api/pub/seq` | Update | `pub-seq` | edit | 否 | 改采番规则（前缀/日期格式/长度/重置周期）。**⚠️ HttpPut**——反射测试须覆盖 PUT（§六 跨波票背景） |
| 14 | SeqController | DELETE `/api/pub/seq` | Delete | `pub-seq` | delete | 否 | 批量删采番规则（body `Guid[] ids`） |

> **GET-only / 纯读端点（不在上表）**：
> - Mrp：GET `runs` / `run/{id}/planned-orders` / `run/{id}/net-requirements`（看板+钻取，纯读）。
> - ItemPlanningPolicy：GET `` / `{itemCd}`（List/Get，纯读）。
> - CodeGen：GET `tables` / `{id}/preview`（纯读；`{id}/preview` 读持久化元数据后 `_gen.Generate`，无写）。
> - Attachment：GET `list` / `{id}/download` / `{id}/preview`（下载/预览；`Download`/`Preview` 走 `Stream()`，读流 + 可选 `HasMenuAsync(att.BizType)` biz 权限回查，无写）。
> - Seq：GET `` / `preview/{bizKey}`（列表 + 号码格式预览，不消费流水，SeqController:66-73）。

---

## 二、menu-key 汇总清单（去重，共 4 个）

| # | menu-key | 锚定菜单（Program.cs MenuId / RoutePath / 行） | 承载 action | 首启就位性 | 种子现状 |
|---|---|---|---|---|---|
| 1 | `plan-mrp` | 731 MRP运算看板 `/plan/mrp`（:1534） | run, confirm, convert, ignore | 🔴**首启 MenuKey=null**（:1534 插入晚于 :1008 全局回填，且无 Plan 局部回填）→ **§六 头号命门** | ❌ 零 MenuAction/RoleAction，全新播 |
| 2 | `plan-item-policy` | 732 计划主数据 `/plan/item-policy`（:1540） | add, delete | 🔴**同上，首启 null → 403**（§六 头号命门） | ❌ 零种子，全新播 |
| 3 | `pub-codegen` | 113 代码生成 `/pub/codegen`（:1003） | save, view | ✅**首启即就位**（:1003 插入早于 :1008 全局回填，同 pass 回填=`pub-codegen`） | ❌ 零 MenuAction/RoleAction（Sys族种子 :1446-1457 只覆盖 101–111），全新播 |
| 4 | `pub-seq` | 112 采番规则 `/pub/seq`（:996） | add, edit, delete | ✅**首启即就位**（:996 早于 :1008 回填→`pub-seq`） | ❌ 零种子（同上），全新播 |

> **零孤儿 menu-key**：4 键均对应实在菜单行（731/732/113/112），RoutePath 派生键与本表逐字一致，无 MES `machine-list` 那种错配。
> **关键差异（vs Pur）**：Pub 112/113 因插入位置**早于** :1008 全局回填（每菜单各自 `db.SaveChanges()`，回填 `Where(MenuKey==null)` 查库即见）→ 首启 MenuKey 就位；而 Plan 731/732 插入位置**晚于** :1008 且无局部回填 → 首启失配（§六）。四键**全部零 RoleAction**（首启就位 ≠ 有授权），须 T2 逐租户新播。

---

## 三、高危动作清单（`是`：重算写/转单建承诺/生成写盘，共 3 个资源键）

> T3 贴 `[RequirePermission]` 的**第一优先级**，**绝不可**与 view/edit 混授。

| 资源键 | 端点# | 为何高危独立（读证 文件:行） |
|---|---|---|
| `plan-mrp:run` | 1 | **MRP 全量重算**：`MrpEngine.RunAsync` 采番建 `Plan_MrpRun`、`o.IsDeleted=true` 作废全部建议态计划订单再逐层 net 重生 PlannedOrder/NetRequirement（MrpEngine.cs:40-60+）。重算冲刷全域计划视图、代价高，须与看板浏览分权（可授看板、不授重算）。 |
| `plan-mrp:convert` | 3 | **转单=创建采购/生产承诺**：PlanConvertService.ConvertAsync:45-47 采购类→`_prService.CreatePrFromPlannedOrderAsync` 建 PR、生产类→`_woService.CreateWorkOrderFromPlannedOrderAsync` 建工单，回填 `ConvertedDocNo`、置 Converted。**跨模块建承诺**，对标 `pur-pr:convert`/`pur-rfq:convert` 高危。当前委托 P1 桩（§六 必带票）。 |
| `pub-codegen:save` | 7 | **代码生成元数据写盘覆盖**：CodeGenController.Save:43-51 `RemoveRange(oldCols)` 后重插整套 `Pub_GenColumns` + upsert `Pub_GenTables`——**整体替换、覆写既有列定义**，误授即他人可篡改脚手架产物模型。生成写盘类独立成键。 |

> **旁注·attachment:delete（端点#10）高危-shaped 但不铸键**：物理删附件本属不可逆写，但 Attachment 无独立菜单可锚（§五.4），归组件豁免；其高危形态正是 §六 follow-up「扩 `Attachment:EnforceBizPermission` 至 upload/delete」的驱动理由。

### 3b. 独立状态流转动作键（`状态`，共 2 个，仍单独成键、不塞 edit/view）

`plan-mrp:confirm`（计划订单确认进供给，#2）· `plan-mrp:ignore`（计划订单忽略，#4）。二者仅置 `Plan_PlannedOrder.Status`，无跨模块/财务/库存写（PlanConvertService.cs:22-31 / 56-65），故判 `状态`。

---

## 四、只读 POST 豁免 + 组件豁免清单（逐条读实现证得）

### 4a. 只读 POST → view（归 view 键，共 1 个）

| # | 端点（方法） | 豁免依据（读实现，文件:行） |
|---|---|---|
| 1 | POST `/api/pub/codegen/preview`（CodeGenController.PreviewInline） | 方法体单行 `Ok(new { code=0, data=_gen.Generate(req.Table, req.Columns) })`——`CodeGenService.Generate` 由 body 元数据纯内存生成产物字符串，**控制器无 `_db` 触碰、无 SaveChanges**（CodeGenController.cs:66-68）。POST 仅为传 body 元数据入参。归 `pub-codegen:view`。 |

### 4b. 组件豁免（横切组件、无菜单可锚，登 fail-closed 反射测试显式豁免表，不铸键，共 3 个端点）

| # | 端点（方法） | 豁免依据 + 裁定 |
|---|---|---|
| 1 | POST `/api/pub/attachment/upload`（Upload） | 统一附件组件（PUB 章06），嵌入各业务单据页、无独立页面/菜单行/RoutePath。现有设计以 `Attachment:EnforceBizPermission` 配置 + `_perm.HasMenuAsync(att.BizType)` 按宿主业务菜单自门控（v1 默认 false=仅登录）。§五.4 裁定：登入组件豁免表。 |
| 2 | DELETE `/api/pub/attachment/{id}`（Delete） | 同上；删除属高危形态，§六 follow-up 建议将 biz 权限回查扩至此端点。 |
| 3 | POST `/api/pub/attachment/rebind`（Rebind） | 同上；草稿附件回填 BizId，业务单据保存后由前端调，随宿主页授权。 |

> **复核结论（防望文生义）**：
> - `POST /api/pub/codegen/save`（Save）：`RemoveRange`+`Add`+`SaveChangesAsync` → **真写、高危**，非「预览」。仅 `POST .../codegen/preview`（PreviewInline）无持久化可豁免。
> - `POST /api/plan/mrp/run`（Run）：`MrpEngine.RunAsync` 大量写 + 采番 → **真写、高危**，非「查询运算结果」。
> - `GET .../codegen/{id}/preview`（Preview）读持久化元数据后生成，是 **GET**，本就不列。

---

## 五、命名归并判断与裁定（供 T2/T3 复核）

1. **Plan 域删除用 `delete` 非 `del`**：`plan-item-policy:delete`(#6) 沿用 Pur `delete` 风格（各模块沿用自身既定风格，WMS 用 `del`，勿混改）。
2. **`plan-item-policy:add` 承载 upsert**（#5）：控制器 `Upsert` 为按 itemCd 建/改二合一，沿用 Pur `pur-supplier-price:add`（Save=upsert→add）先例，不拆 add/edit。若 T2 审计认为应分权可改判 `edit`，**当前 `add`**。
3. **`plan-mrp:run` 独立成键而非 view**：虽由「运算」触发，但产生大量重生写（作废重生建议态计划订单），非只读，且业务上须与看板浏览分权，故独立高危键、不并 view。
4. **★ Attachment 锚定裁定（组件豁免，非新增菜单）**：
   - **形态**：AttachmentController 是 PUB 章06 **统一附件横切组件**，被各业务单据页内嵌调用（`bizType`/`bizId` 定位宿主），**自身无路由页、无 `Sys_Menu` 行**——与 Space editor 那种「standalone 全屏页」不同（后者有 RoutePath 可锚），附件连 RoutePath 都没有。
   - **规范约束**：§三.2「资源键必须能锚定到一个 `Sys_Menu` 行」——为无菜单的组件铸 `pub-attachment:*` 键，`PermissionAggregator`（join `Sys_Menus`）永远 join 不出 → 该键**恒 403**，铸键即死键。故**不铸键**。
   - **裁定 = 组件豁免（方案A，采纳）**：upload/delete/rebind 登入 fail-closed 反射测试的**显式豁免清单**，理由=横切组件、随宿主业务页授权、当前 `[Authorize]` 登录闸 + 既有 `Attachment:EnforceBizPermission`（开启时 `HasMenuAsync(att.BizType)` 按宿主菜单门控）的分层设计。此与现有架构一致（附件已以 biz-menu 回查自门控，而非自建菜单键）。
   - **拒绝方案B（新增 `pub-attachment` 隐藏菜单 114 仅为挂键）**：附件无页面，凭空造隐藏菜单只为寄存键，破坏「菜单=可达页面」语义（§三.2 暗物质禁令的反面），且 114 段位挤占 Pub 菜单序列，收益仅为形式合规。**不采纳**；若未来产品要求附件独立授权，再按 §六 follow-up 落 114 段位 + 扩 biz 权限。
   - **follow-up（§六）**：现 `EnforceBizPermission` 仅门控 download/preview（Stream 路径），**未覆盖 upload/delete/rebind**——建议后续扩至写端点做纵深防御（delete 尤为高危形态）。本波按登录闸豁免、留票。

---

## 六、命门与遗留（T2 硬前置）

### 头号硬前置·Plan 731/732 MenuKey 首启 null（洁净首启 plan-mrp/plan-item-policy 全 403）

**证据链**：
- Plan 菜单 730/731/732 在 Program.cs :1526–1542 插入，**插入时均未设 MenuKey**（:1534 `new Sys_Menu{ MenuId=731, RoutePath="/plan/mrp" … }` 无 MenuKey 字段），各带独立 `if (!…Any(MenuId==73x))` 守卫 + `db.SaveChanges()`。
- 唯一的全局回填块 Program.cs :1008–1013（`menusNoKey = Sys_Menus.Where(MenuKey==null && RoutePath!=null)` → `RoutePath.Trim('/').Replace('/','-')`）位于 :1008，**在 Plan 菜单插入(:1534)之前**执行 → 洁净库首启时 731/732 尚不存在 → 跳过。
- 全仓 grep 实证：**无任何 731/732 的局部回填或 MenuKey 显式赋值**（对比 Pur 有 :1599 局部回填 701–704、Fin 有 :1317 局部回填 601–623；Plan 段无对应块）。
- 结果：首启后 731/732 MenuKey 留 **null** → `PermissionAggregator` 过滤 `MenuKey!=null` → `plan-mrp`/`plan-item-policy` 全 action 键 join 不出 → **首启即 fail-closed 403，须二次重启由 :1008 回填才生效**（与 Pur 705/706/707、OA 头号命门、MES 命门#1 同型）。
- → **T2 必须**在 Plan 菜单插入块对 731/732 各行**显式赋 `MenuKey`**（`plan-mrp`/`plan-item-policy`），或在 Plan 菜单之后、Seed 之前加 Plan 局部回填（`Where(MenuKey==null && RoutePath!=null && MenuId>=731 && MenuId<=732)`）。**照 M-PUR 终审后定论优先显式赋值**（Program.cs:1597 注释「705/706/707 已在菜单插入时显式赋值，首启即就位，不依赖全局回填」即先例）。**不做则洁净部署首启这两控制器全 403。**

### 次硬前置·四菜单（731/732/112/113）零 RoleAction（含首启就位的 Pub 两键，admin 也 403）

**证据链**：
- Sys族权限种子 Program.cs :1446–1457（`sysActions`）**只覆盖 MenuId 101–111**（含 108–111 pub-dept/role-perm/data-scope/field-perm），**112/113 完全不在其中**；Plan 731/732 亦无任何 MenuAction/RoleAction 种子。
- `PermissionService.HasActionAsync` 无 admin 旁路 → 即使 112/113 MenuKey 首启就位，admin(RoleId=1) 无 RoleAction 仍 **403**。
- → **T2 必须**新建 `PlanPubPermissionSeed.EnsureSeeded(db)`，照 `CP6.WebApi/Seed/PurPermissionSeed.cs` 逐租户模式（枚举 `Sys_Tenants` 全 Id、显式 `TenantId=tid`、`IgnoreQueryFilters()` 幂等判存），**一次播齐全部 11 资源键**（含 `pub-codegen:view` 只读豁免键）× MenuAction + RoleAction(RoleId=1)，于 Plan 菜单 + 731/732 MenuKey 修复**之后**调用（RoleAction 挂锚定 MenuId 731/732/112/113，菜单行须先在）。**Attachment 三端点不入种子**（组件豁免，§五.4）。

### 跨波票·反射 fail-closed 测试须覆盖 HttpPut

- SeqController.Update 为 **`[HttpPut]`**（#13），非 POST/DELETE。§一.5 规范测试断言范围为「POST/PUT/DELETE」，T4 反射测试**必须显式扫 `HttpPut`**——否则漏贴 `pub-seq:edit` 不报红（对应 M-PUR 跨波 sweep 票「PATCH/PUT 不在部分 IsMutating 谓词」背景）。本波 5 控制器唯一 PUT。

### 必带票·MRP→PR 生成端点（M-PUR 终审 Minor#1）现状与结论

- **现状（读证）**：MRP `convert` 端点(#3)→`PlanConvertService.ConvertAsync`（PlanConvertService.cs:33-54）采购类分支调 `IPlanToPrService.CreatePrFromPlannedOrderAsync`。该接口 DI 注册为 **`PlanToPrServiceStub`**（Program.cs:327），桩实现 `Task.FromResult($"PR-STUB-{ItemCd}")`——**返回桩单号、不实写 `PurchaseRequests`**（IPlanToPrService.cs:15-20）。
- **与 M-PUR 必带票的关系**：M-PUR Minor#1 点名的 `PrGenerationService`（Program.cs:263 注册 `IPrGenerationService`，写 `PurchaseRequests`，M-PUR 判其无生产调用方=死代码）与本桩**当前未接线**（是两个不同类；桩未委托 PrGenerationService）。
- **结论**：`convert` 端点当前**不产生跨命名空间 Pur 写**（走桩），但它**正是该闸门要抓的形态**——一旦 `PlanToPrServiceStub` 被真实实现替换（大概率委托 Pur 侧生成、写 `PurchaseRequests`），`convert` 即跨命名空间建采购承诺。故 **T3 必须现在就为 `plan-mrp:convert` 贴 `[RequirePermission("plan-mrp","convert")]`**（本表已判其高危 §三），无论桩/真实，闸门先落地。同理，生产类分支 `IPlanToWorkOrderService` 亦为桩（Program.cs:328），接线真实工单生成时 convert 键同样覆盖之。

---

## 七、计数收口

- **扫描控制器**：5（Mrp / ItemPlanningPolicy / Attachment / CodeGen / Seq）。与计划「Plan 2 控制器 + Pub 3 控制器」精确吻合。
- **既有 `[RequirePermission]` 贴点**：**0**（5 控制器全裸，仅类级 `[Authorize]`）。
- **POST/PUT/DELETE 端点行总数**：**14**（= §一表行数，含 1 个 HttpPut）。
  - **组件豁免（Attachment，无菜单不铸键）**：**3**（#9–#11）。
  - **只读 POST 豁免（→view 键）**：**1**（#8 codegen preview）。
  - **铸键写端点（真·写，带非-view 键）**：**10**（#1–#7 + #12–#14）。
- **menu-key（去重，承载 action 键）**：**4**（`plan-mrp` / `plan-item-policy` / `pub-codegen` / `pub-seq`；对应菜单 731/732/113/112）。
- **资源键（去重，含 view）**：**11**——`plan-mrp` ×4（run/confirm/convert/ignore）+ `plan-item-policy` ×2（add/delete）+ `pub-codegen` ×2（save/view）+ `pub-seq` ×3（add/edit/delete）。端点 11（10 铸键写 + 1 view）↔ 资源键 11，1:1（Attachment 3 端点铸 0 键）。**全 11 键为新键**（零既有种子）。
- **高危键（是）**：**3**：`plan-mrp:run`、`plan-mrp:convert`、`pub-codegen:save`。
- **状态键**：**2**：`plan-mrp:confirm`、`plan-mrp:ignore`。
- **只读豁免键（view）**：**1**：`pub-codegen:view`。
- **组件豁免端点（不铸键）**：**3**：attachment upload/delete/rebind。

### 逐控制器双向核对（控制器→表 / 表→控制器，零缺漏零 GET 误列）

| 控制器 | POST/PUT/DELETE 端点数 | 组件豁免 | view 豁免 | 铸键写 | 既有贴点 | 表内 # |
|---|---|---|---|---|---|---|
| MrpController | 4（Run/Confirm/Convert/Ignore；GET Runs/PlannedOrders/NetRequirements 不列） | 0 | 0 | 4 | 0 | 1–4 |
| ItemPlanningPolicyController | 2（Upsert/Delete；GET List/Get 不列） | 0 | 0 | 2 | 0 | 5–6 |
| CodeGenController | 2（Save/PreviewInline；GET Tables/Preview 不列） | 0 | 1 | 1 | 0 | 7–8 |
| AttachmentController | 3（Upload/Delete/Rebind；GET List/Download/Preview 不列） | 3 | 0 | 0 | 0 | 9–11 |
| SeqController | 3（Add/Update[PUT]/Delete；GET GetList/Preview 不列） | 0 | 0 | 3 | 0 | 12–14 |
| **合计** | **14** | **3** | **1** | **10** | **0** | **14 ✅** |

> 自洽核验：
> - 总非 GET 端点 14 = 组件豁免 3 + view 豁免 1 + 铸键写 10 ✅；
> - 逐控制器铸键写累加 4+2+1+0+3 = 10 ✅；逐控制器非 GET 累加 4+2+2+3+3 = 14 ✅；
> - 资源键 11 = 高危 3 + 状态 2 + view 1 + 基础写 5 = 11 ✅（基础写 5 = plan-item-policy add/delete + pub-seq add/edit/delete）；
> - 铸键端点 11（10 写 + 1 view）↔ 资源键 11，1:1 无归并 ✅；
> - 既有贴点 0（5 控制器全裸）✅；新键 11 = 资源键全量 ✅；
> - 表行 #1–#14 连续无跳号 ✅。
