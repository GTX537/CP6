# M-PLAN/PUB T2 实现校验报告（贴点+锚定+种子）

> 生成于 2026-07-17。角色：T2 续作校验/验证/提交者（前一实现者已完成编辑，会话中断于验证前）。
> 真相源：`docs/seeds/planpub-permission-keys.md`（§一~§七）。任务简报：`.superpowers/sdd/planpub-t2-brief.md`。

## 一、逐条核对结论（对照真相源 §一 端点表 / §七 计数）

### 1. 11 处 RequirePermission 贴点（键名/action 逐字一致）— 全对 ✅

| # | 控制器:方法 | 贴点 `[RequirePermission(...)]` | 真相源 §一 期望 | 结论 |
|---|---|---|---|---|
| 1 | MrpController.Run | `("plan-mrp","run")` | plan-mrp / run | ✅ |
| 2 | MrpController.Confirm | `("plan-mrp","confirm")` | plan-mrp / confirm | ✅ |
| 3 | MrpController.Convert | `("plan-mrp","convert")` | plan-mrp / convert | ✅ |
| 4 | MrpController.Ignore | `("plan-mrp","ignore")` | plan-mrp / ignore | ✅ |
| 5 | ItemPlanningPolicyController.Upsert | `("plan-item-policy","add")` | plan-item-policy / add（§五.2 upsert→add） | ✅ |
| 6 | ItemPlanningPolicyController.Delete | `("plan-item-policy","delete")` | plan-item-policy / delete（§五.1） | ✅ |
| 7 | CodeGenController.Save | `("pub-codegen","save")` | pub-codegen / save | ✅ |
| 8 | CodeGenController.PreviewInline | `("pub-codegen","view")` | pub-codegen / view（§四.4a 只读豁免归 view 贴点非旁路） | ✅ |
| 9 | SeqController.Add | `("pub-seq","add")` | pub-seq / add | ✅ |
| 10 | SeqController.Update `[HttpPut]` | `("pub-seq","edit")` | pub-seq / edit（唯一 PUT，§六 跨波票背景） | ✅ |
| 11 | SeqController.Delete | `("pub-seq","delete")` | pub-seq / delete | ✅ |

- 属性构造签名 `RequirePermissionAttribute(string menu, string action)`（CP6.Core/Auth/RequirePermissionAttribute.cs:20），入参顺序与 Pur 先例一致。
- 4 控制器各补 `using CP6.Core.Auth;`。

### 2. Attachment 三端点零触碰 ✅
`git status --short CP6.WebApi/Controllers/Pub/AttachmentController.cs` 空输出 = 未改。upload/delete/rebind 组件豁免（§五.4），不贴、不入种子，交 T3 反射测试显式豁免表。

### 3. 硬前置① 731/732 MenuKey 显式赋值（插入行本体，非依赖 :1008 回填）✅
Program.cs 菜单插入块（:1534 一带）：
- 731：`new Sys_Menu{ MenuId=731, MenuName="MRP运算看板", RoutePath="/plan/mrp", MenuKey="plan-mrp", ... }` — MenuKey 在插入行本体
- 732：`new Sys_Menu{ MenuId=732, MenuName="计划主数据", RoutePath="/plan/item-policy", MenuKey="plan-item-policy", ... }` — 同上
照 M-PUR 705-707 先例（首启即就位，不依赖全局回填）。修复 §六 头号命门（洁净首启 null → 全 403）。112/113 未触碰（§二：早于 :1008 回填，首启已就位）。

### 4. 硬前置② 逐租户种子 PlanPubPermissionSeed（11 条 1:1 §七）✅
`Actions` 数组 11 条 (MenuId, Code, Name)：
- 731 plan-mrp ×4：run/confirm/convert/ignore
- 732 plan-item-policy ×2：add/delete
- 113 pub-codegen ×2：save/view（含只读豁免键）
- 112 pub-seq ×3：add/edit/delete
逐租户机制逐字照 PurPermissionSeed：枚举 `Sys_Tenants`、显式 `TenantId=tid`、`IgnoreQueryFilters()` 幂等判存、Sys_MenuAction + Sys_RoleAction(RoleId=1) 双播、`changed` 才 SaveChanges。结构与先例逐行一致。

### 5. 接线位置 ✅
Program.cs :1607 一带，`PlanPubPermissionSeed.EnsureSeeded(db)` 挂在 `PurPermissionSeed.EnsureSeeded(db)` 之后、MES 菜单块之前，位于 Plan 菜单 731/732 MenuKey 赋值（:1534）之后。RoleAction 锚定 MenuId 731/732/113/112 均已就位。

## 二、build / test 实跑输出摘要

- **无 .sln**（仓库无解决方案文件），改建 WebApi 项目（传递引用 Entity/Core）。
- **build**：`dotnet build CP6.WebApi/CP6.WebApi.csproj -c Debug` → `Build succeeded. 0 Error(s), 1 Warning(s)`。唯一警告 = `CP6.Core/Services/Wms/InboundService.cs(369,25) CS8601`，**pre-existing、与本 diff 无关**（Wms 模块，非本波触碰文件）。本 diff 零新增警告。
- **test**：`dotnet test CP6.Tests/CP6.Tests.csproj -c Debug` → `Passed! - Failed: 0, Passed: 2181, Skipped: 5, Total: 2186, Duration: 1m48s`。**与基线 2181 绿/5 skip 精确吻合，零跌**。测试项 4 处警告（PendingCookieTests/BudgetVsActualTests/InboxServiceTests）均 pre-existing，非本 diff。

## 三、计数收口

- 贴点：**11**（10 铸键写 + 1 view）= §七铸键端点 11。含 3 高危（plan-mrp:run/convert、pub-codegen:save）、2 状态（confirm/ignore）、1 view 豁免。
- 种子：**11 资源键**（4 menu-key：plan-mrp/plan-item-policy/pub-codegen/pub-seq）× 每租户 × {MenuAction, RoleAction}。
- MenuKey 修复：2 行（731/732）。种子接线：1 行。改动文件：4 控制器 + Program.cs + 新 PlanPubPermissionSeed.cs = 6。
- Attachment 组件豁免：3 端点，零触碰。

## 四、偏差与理由

- **无偏差**。全 11 键逐字符合真相源，无自行改键。唯一环境差异：仓库无 .sln，build/test 以项目文件为目标（等价，Tests 项目传递引用全部生产项目）。

## 五、状态

**DONE** — 校验通过、build/test 双绿、单 commit 已推。
