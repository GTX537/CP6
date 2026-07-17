# 普通角色授权放开 + v-permission 全模块铺设 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 六波授权粒度 P0 + WF-OWN 归属闸全部落地后的「放开」波——①逐租户预置标准角色「一般用户」(RoleId=10) 含 OA 办理最小键集，开箱即用；②前端 v-permission 按钮级隐藏从 WMS/Space 铺满全部剩余模块（OA/WF/PLAN/PUB/ERP/MES/PUR/FIN），普通角色看到的界面与其权限一致。

**Architecture:** 后端一个新种子类 `StandardRoleSeed`（照 `PurPermissionSeed` 逐租户幂等 insert-only 模式）写 Sys_Roles/Sys_RoleMenus/Sys_RoleActions 三表；授权管线零改动（侦察已证 RolePermView→SaveRolePermAsync→Sys_RoleActions→PermissionAggregator 全链路完整无 admin 硬编码后门，菜单树服务端按 Sys_RoleMenus 过滤）。前端沿用既有 `v-permission` 指令（`cp6.web/src/directives/permission.ts`，键格式 `'menu-key:action'`，store 自 `GET /api/pub/role-perm/my-actions` 加载，未加载 fail-open 属既定 UX 层语义——安全仍由后端 403 兜底）。

**Tech Stack:** .NET 8 / EF Core / xUnit（后端）；Vue3 + Element Plus + vitest + vue-tsc（前端，目录 `cp6.web/`）。

## Global Constraints

- 票源：M-OA/WF 完成记录「普通角色授权暂缓」挡板（WF-OWN 波已解除，main=002ef385）+ 前端 v-permission 不对称 UX 票（M-WMS T5 起挂账，扩至全模块）。
- **v-permission 是 UX 层，不是安全层**——安全边界=后端 `[RequirePermission]` 403（fail-closed 反射测试已锁八模块）。前端任务严禁以任何理由改后端。
- 键字符串**必须与控制器贴点逐字一致**（连字符、大小写）；每模块真相源=`docs/seeds/<mod>-permission-keys.md` + 该模块 `*PermissionAttributeTests` oracle。**严禁凭猜测发明键名**。
- 标准角色最小键集（用户已拍板，不扩不减）：`oa-inbox`: read/approve/transfer/sendback/withdraw；`oa-form-catalog`: submit/favorite；`oa-settings`: delegate。addsign/designer/flow-admin/approver-map/work-calendar 蓄意不授（admin 可经 RolePermView 手工加）。
- 标准角色菜单可见面：MenuId **740**(OA工作流父) + **733**(信箱) + **735**(填單) + **737**(设定)。designer(738)/approverMap(739)/工作日历(743) 不授。
- RoleId=**10**，RoleName=**一般用户**（生产 Sys_Roles 现况：每租户 1=管理员/2=超级管理员/3=开发管理员/9002=LowPriv[QA遗留勿动]；复合主键 (TenantId, RoleId)）。
- 种子幂等 insert-only（存在即跳过），逐租户；注册进 Program.cs 须在菜单种子与 OawfPermissionSeed **之后**（依赖 740/733/735/737 菜单行与 MenuActions 目录已在）。
- 后端全量绿基线：**2213 绿/5 skip**；前端基线：vitest 全绿 + vue-tsc 零错 + build 过（任务各自跑）。
- 构建避雷：后端 `dotnet build ... -m:1`（Roslyn OOM）；每 commit 立即 push。分支 `feat/general-role-vperm`（base=main）。
- 前端改动**只许加 `v-permission` 指令及其必要的模板行内改动**——不重构、不改样式、不动脚本逻辑（既有 `v-if` 业务条件保留并列，参照 WMS 样板 `StocktakeView.vue:86` `v-if="canApprove" v-permission="'wms-stocktake:approve'"` 双条件并存形态）。

## 开工盘点（实现者免重查）

- 管线健康（侦察报告 2026-07-17）：RolePermView (`cp6.web/src/views/pms/RolePermView.vue`) 可对任意角色授菜单+动作，`RolePermService.SaveRolePermAsync` diff 写 `Sys_RoleActions`，CFG-T#8 校验=动作 MenuId ⊆ 已授菜单集（E-PUB-021）。`PermissionAggregator.FillActionKeysAsync`=user→roles→Sys_RoleActions⋈Sys_Menus→`"{MenuKey}:{ActionCode}"`，无 admin 捷径。菜单树 `AuthController.BuildProfileAsync` 按 Sys_RoleMenus 过滤，前端动态路由只建返回的菜单。
- v-permission 现况：WMS ~30 按钮/24 视图 + Space ~4 视图已铺；**oa/wf/plan/pur/erp/mes/fin 视图零覆盖**。视图量：oa 34（子目录 admin/catalog/designer/inbox/notification/query/settings）/ wf 4+designer / plan 2 / pur 8 / erp 21 / mes 15 / fin 22。
- OA 目录键（OawfPermissionSeed 20 元组）：oa-inbox: read/approve/transfer/sendback/addsign/withdraw；oa-form-catalog: add/edit/submit/del/favorite；oa-settings: edit/delegate；oa-flow-admin: enable；oa-designer: edit/add/form-save；oa-approver-map: add/edit/del。附加种子：oa-inbox:batch-transfer（InboxBatchTransfer）/oa-flow-admin:FlowTrigger.Edit（FlowTrigger）/oa-work-calendar:Calendar.View·Edit+oa-settings:Connector.Edit（WorkCalendarConnector）。
- OA/WF 控制器无任何 admin 硬编码闸——键授到即通（侦察 #10 已 grep 实证）。

---

### Task 1: StandardRoleSeed 标准角色种子（TDD）

**Files:**
- Create: `CP6.WebApi/Seed/StandardRoleSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（注册，插在 OawfPermissionSeed 调用之后、与其同形态）
- Test: `CP6.Tests/StandardRoleSeedTests.cs`（新文件）

**Interfaces:**
- Consumes: `Sys_Role`（复合主键 TenantId+RoleId, int RoleId, RoleName）、`Sys_RoleMenu`、`Sys_RoleAction` 实体；`PurPermissionSeed`/`OawfPermissionSeed` 的逐租户播种模式（**先读这两个文件照抄其租户遍历/盖章/幂等形态**——包括它们怎么取租户清单、怎么处理 TenantId 列/全局过滤器）。
- Produces: 每租户 `Sys_Roles` 行 (RoleId=10, RoleName=一般用户)；`Sys_RoleMenus` 4 行/租户 (RoleId=10 × MenuId 740/733/735/737)；`Sys_RoleActions` 8 行/租户 (RoleId=10 × 上述最小键集)。

- [ ] **Step 1: 写失败测试（RED）**

新建 `CP6.Tests/StandardRoleSeedTests.cs`，照既有 `*PermissionSeedTests` 的测试harness形态（先读 `OawfPermissionSeedTests`——用它的 in-memory db + 租户构造方式）。断言面：

```csharp
// 形态照 OawfPermissionSeedTests 改；核心断言（每条一个 [Fact]）：
// 1. Seed_CreatesRole10_PerTenant：跑种子后每租户存在 (RoleId=10, RoleName="一般用户")。
// 2. Seed_GrantsExactly4Menus：RoleId=10 的 Sys_RoleMenus = {740,733,735,737}（集合相等，不多不少）。
// 3. Seed_GrantsExactly8Actions：RoleId=10 的 Sys_RoleActions 投影 (MenuId,ActionCode) 集合相等于
//    {(733,"read"),(733,"approve"),(733,"transfer"),(733,"sendback"),(733,"withdraw"),
//     (735,"submit"),(735,"favorite"),(737,"delegate")}。
// 4. Seed_IsIdempotent：连跑两遍，三表行数不变（无重复）。
// 5. Seed_DoesNotTouchAdminRole：跑种子前后 RoleId=1 的三表行零 diff。
// 6. Seed_ActionsSubsetOfCatalog：8 键均存在于 Sys_MenuActions 目录（先跑 OawfPermissionSeed 再跑本种子）。
// 7. Aggregator_UserWithRole10_GetsExactly8Keys：造用户挂 RoleId=10，PermissionAggregator 聚合出
//    恰好 8 个 "menu-key:action"（含 "oa-inbox:approve"），且不含 "oa-inbox:addsign"。
```

- [ ] **Step 2: 跑新测试确认 RED**

```
dotnet build CP6.Tests/CP6.Tests.csproj -m:1 --nologo -v q
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo --filter "FullyQualifiedName~StandardRoleSeedTests"
```
预期：`StandardRoleSeed` 类不存在编译失败 → 先建空壳类（SeedAsync 空体）让编译过，跑测试全 FAIL。记录 RED 证据。

- [ ] **Step 3: 实现种子**

`CP6.WebApi/Seed/StandardRoleSeed.cs`——骨架（**租户遍历/盖章细节照 PurPermissionSeed 原样，此处只定内容**）：

```csharp
/// <summary>
/// 标准角色种子（普通角色授权放开波 T1）：逐租户预置「一般用户」(RoleId=10) + OA 办理最小键集。
/// 幂等 insert-only：行存在即跳过，绝不更新/删除（admin 后续经 RolePermView 对该角色的手工调整不被重置）。
/// 依赖：菜单 740/733/735/737 与 OawfPermissionSeed 的 MenuActions 目录已播种（Program.cs 注册序保证）。
/// 蓄意不授：addsign / oa-designer / oa-flow-admin / oa-approver-map / oa-work-calendar（admin 手工放）。
/// </summary>
public static class StandardRoleSeed
{
    public const int GeneralRoleId = 10;
    private const string GeneralRoleName = "一般用户";

    private static readonly int[] Menus = { 740, 733, 735, 737 };

    private static readonly (int MenuId, string Code)[] Actions =
    {
        (733, "read"), (733, "approve"), (733, "transfer"), (733, "sendback"), (733, "withdraw"),
        (735, "submit"), (735, "favorite"),
        (737, "delegate"),
    };

    // SeedAsync(db)：照 PurPermissionSeed 的租户遍历形态——对每租户：
    //   ① Sys_Roles 无 (tenant, 10) 行 → 插 { RoleId=10, RoleName=GeneralRoleName }
    //   ② 对 Menus 每项：Sys_RoleMenus 无 (tenant, RoleId=10, MenuId) 行 → 插
    //   ③ 对 Actions 每项：Sys_RoleActions 无 (tenant, RoleId=10, MenuId, ActionCode) 行 → 插
    //   TenantId 盖章方式与 PurPermissionSeed 逐字同款。
}
```

`Program.cs`：在 OawfPermissionSeed（及其三个附加种子）调用之后追加 `await StandardRoleSeed.SeedAsync(db);`（调用形态照前后邻居）。

- [ ] **Step 4: 跑新测试确认 GREEN**

```
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo --filter "FullyQualifiedName~StandardRoleSeedTests"
```
预期 7/7 PASS。

- [ ] **Step 5: 全量回归**

```
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo
```
预期 **2220 绿（2213+7）/5 skip/0 fail**。

- [ ] **Step 6: Commit + push**

```
git add -A
git commit -m "feat(auth): 标准角色种子——逐租户一般用户(RoleId=10)+OA办理最小键集8键4菜单, 幂等insert-only"
git push
```

---

### Task 2-6: v-permission 铺设（每模块一任务，共同规程）

**模块分工与真相源：**

| Task | 模块/视图目录 | 真相源键表 | oracle 测试 |
|---|---|---|---|
| T2 | `cp6.web/src/views/oa` + `views/wf` | `docs/seeds/oawf-permission-keys.md` | `OawfPermissionAttributeTests` |
| T3 | `views/erp` | `docs/seeds/erp-permission-keys.md`（无则 grep docs/seeds erp） | `ErpPermissionAttributeTests` |
| T4 | `views/mes` | `docs/seeds/mes-*.md` | `MesPermissionAttributeTests` |
| T5 | `views/fin` | `docs/seeds/fin-*.md`（无则以 oracle 测试内清单为准） | `Fin*PermissionAttributeTests` |
| T6 | `views/pur` + `views/plan`（PLAN/PUB 前端页在 plan 目录；pub-codegen/pub-seq 若有独立页一并） | `docs/seeds/pur-*.md` + `plan-pub-*.md` | `PurPermissionAttributeTests` + `PlanPubPermissionAttributeTests` |

**共同规程（每个任务逐字适用；示例为形态说明，键名以各模块真相源为准）：**

- [ ] **Step 1: 建按钮-键映射清单（先盘后改）**

读真相源键表，得该模块全部 `menu-key:action` 集。逐视图文件扫描**变更动作触发点**：调用 POST/PUT/PATCH/DELETE API 的按钮、菜单项、开关、行内操作链接。产出映射清单（视图文件 → 元素 → 键）写入任务报告。规则：
1. 键**只取自真相源清单**，与控制器贴点逐字一致；找不到对应键的按钮=该端点未贴键（组件/只读POST豁免面），**不贴指令并在报告豁免小节列明**；
2. 纯读操作（查询/翻页/导出预览/刷新/展开）不贴；
3. 入口按钮贴，已被入口守住的对话框内确认按钮不重复贴；
4. 一个按钮对应多端点取**主动作**的键（报告注明）。

- [ ] **Step 2: 加指令**

WMS 样板形态（`views/wms/StocktakeView.vue:86`）：
```html
<el-button v-if="canApprove" v-permission="'wms-stocktake:approve'" type="success" @click="onApprove">
```
既有 `v-if` 业务条件保留并列；只加 `v-permission="'<key>'"` 字面量，零脚本/样式/结构改动。

- [ ] **Step 3: 前端三连验证**

```
cd cp6.web
npx vue-tsc --noEmit
npx vitest run
npm run build
```
预期：type-check 零错 / vitest 全绿（基线数在任务报告记录）/ build 过。

- [ ] **Step 4: Commit + push**

```
git add cp6.web/src/views/<mod>
git commit -m "feat(web): <MOD> v-permission 铺设——<N>按钮×<M>视图, 键与后端贴点逐字对齐"
git push
```

---

### Task 7: 部署上线 + 端到端冒烟

**Files:** 无代码改动。产物=冒烟记录入台账。

**Interfaces:**
- Consumes: 合并后 main；部署降级路线（publish→剥 Local/Dev 配置→thin build→compose up）；冒烟 harness 避雷（wf-actor-ownership-done 记忆：secure cookie 手工 CookieContainer+X-CSRF-Token/Wf 表单数/QUOTED_IDENTIFIER）。
- Produces: 线上标准角色端到端实证。

- [ ] **Step 1: 双镜像重建部署**

```
dotnet publish CP6.WebApi/CP6.WebApi.csproj -c Release -o publish-docker -m:1
# 删 publish-docker/appsettings.Local.json 与 appsettings.Development.json
# WSL 内: docker build -t cp6-cp6-api:latest ./publish-docker && docker compose up -d cp6-api
# cp6-web: 照 cp6-web 既有构建流程重建（前端有 v-permission 新码, 必须重建, 不许跳过）
```

- [ ] **Step 2: 种子落库验证（SQL）**

四租户各查：Sys_Roles 有 (RoleId=10, 一般用户)；RoleMenus=4 行；RoleActions=8 行；RoleId=1 行数与部署前持平（admin 零扰动）。

- [ ] **Step 3: 端到端冒烟（A1 建测试用户）**

1. SQL/管理页在 A1 建用户 `qa_general`（密码走既有用户建立机制），挂 RoleId=10；
2. `qa_general` 登录 → profile 菜单**只含** OA工作流/信箱/填單/设定 四项（无 designer/无 WMS/ERP…）；
3. `qa_general` 起一条测试流程（735:submit 键）审批人=自己 → 收件箱可见 → approve 本人待办 → 200（管线端到端通）；
4. `qa_general` 对**他人**（admin 名下）待办按 taskId 直调 act → 403 或 400 E-WF-029（后端双闸仍兜底）；
5. `qa_general` 调无键端点（如 `POST /api/oa/inbox/batch-transfer`）→ 403（fail-closed）；
6. 浏览器/接口验证 `my-actions` 返回恰 8 键；
7. admin 登录回归：菜单全量、任意模块按钮可见（v-permission 对 admin 全放行）；
8. 测试流程数据清理；`qa_general` 保留为常驻测试用户（记台账）。

- [ ] **Step 4: 台账+记忆收口**

progress.md 冒烟证据；MEMORY.md 交接点更新；v-permission 不对称 UX 票关闭。commit+push。

---

## 完成后跟踪票

1. PMS/Sys 平台管理页（pub-role-perm 等）v-permission 未铺——平台页有 isPlatformAdmin 面纱，风险低，独立小票。
2. 标准角色是否要日文名（现随库内既有角色中文名口径「一般用户」）——待用户裁决后改一行种子。
3. B1/C1 租户零用户——标准角色已就位但无人可挂，归 TenantAdminService 平台票。

## Self-Review 记录

- 两拍板全落：预置标准角色=T1（键集/菜单面逐字按拍板）；v-permission 全模块=T2-T6 覆盖 oa/wf/erp/mes/fin/pur/plan 全部剩余视图目录（wms/space 已有，pms 记票①）。
- T1 测试 #5/#6/#7 防三类回归：admin 扰动/键不在目录/聚合器端到端。种子 insert-only 保 admin 后续手工调整不被重置。
- 前端任务约束「只加指令」+ 豁免小节防静默漏贴；键逐字对齐由任务审查对照真相源验证。
- 冒烟 #4 兼验 WF-OWN 归属闸与本波键集的叠加语义（有 approve 键仍不能代批）。
