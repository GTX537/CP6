# M-OA/WF T3a 执行报告：16 控制器 31 写端点贴 [RequirePermission]

生成于 2026-07-12。基线 1764 绿。分支 `feat/m-oawf-crosscutting`。

## 结论

- **贴点 31/31 全贴**，键值逐字取自 T1 真相源 `docs/seeds/oawf-permission-keys.md`（合一后版本）。
- **2 豁免未贴**：ForecastController.Preview、QueryController.Search（只读 POST→view，无 `using CP6.Core.Auth;`、无属性）。
- **三 delegate 端点统一键** `("oa-settings","delegate")`：OA Delegate.Add / Delegate.Remove + AdvancedFlow.Delegate（§注4 委派双键合一）。
- **8 高危键全独立不降级**：`oa-inbox:approve/transfer/sendback/addsign` + `oa-settings:delegate` + `oa-designer:edit/add/form-save`。
- **零方法体改动**：纯注解叠加；类级 `[Authorize]` 16 控制器全保留；未删改任何路由/端点（双栈 Flow/Form.SaveDef 照真相源贴 `oa-designer:edit`/`oa-designer:form-save`，不删不改）。
- **顺手项**：Program.cs OawfMenuSeed 接线注释去行号化（`:908`/`:1446–1496` 硬编码行号改为「紧随 MesPermissionSeed 之后」「全局回填块之前」「本文件下方 OA 菜单插入块」内容描述）。

## 逐控制器 1:1 对账表（31 真写 + 2 豁免）

| # | 控制器 | 方法 | menu-key | action | 高危 | 已贴 |
|---|---|---|---|---|---|---|
| 1 | ApproverMap | Create | oa-approver-map | add | 否 | ✅ |
| 2 | ApproverMap | Update | oa-approver-map | edit | 否 | ✅ |
| 3 | ApproverMap | Delete | oa-approver-map | del | 否 | ✅ |
| 4 | Catalog | Favorite | oa-form-catalog | favorite | 否 | ✅ |
| 5 | Delegate | Add | oa-settings | delegate | **是** | ✅ |
| 6 | Delegate | Remove | oa-settings | delegate | **是** | ✅ |
| 7 | Designer | Save | oa-designer | edit | **是** | ✅ |
| 8 | Designer | Clone | oa-designer | add | **是** | ✅ |
| 9 | Draft | Save | oa-form-catalog | add | 否 | ✅ |
| 10 | Draft | Update | oa-form-catalog | edit | 否 | ✅ |
| 11 | Draft | Submit | oa-form-catalog | submit | 状态 | ✅ |
| 12 | Draft | Delete | oa-form-catalog | del | 否 | ✅ |
| 13 | FlowAdmin | Enable | oa-flow-admin | enable | 状态 | ✅ |
| 14 | Forecast | Preview | — | (view) | 豁免 | ⛔不贴 |
| 15 | Inbox | MarkTaskRead | oa-inbox | read | 否 | ✅ |
| 16 | Inbox | MarkCcRead | oa-inbox | read | 否 | ✅ |
| 17 | Inbox | Batch | oa-inbox | approve | **是** | ✅ |
| 18 | Inbox | Transfer | oa-inbox | transfer | **是** | ✅ |
| 19 | Inbox | SendBack | oa-inbox | sendback | **是** | ✅ |
| 20 | Notification | Read | oa-inbox | read | 否 | ✅ |
| 21 | Notification | ReadAll | oa-inbox | read | 否 | ✅ |
| 22 | Pref | Save | oa-settings | edit | 否 | ✅ |
| 23 | Query | Search | — | (view) | 豁免 | ⛔不贴 |
| 24 | AdvancedFlow | SendBack | oa-inbox | sendback | **是** | ✅ |
| 25 | AdvancedFlow | AddSign | oa-inbox | addsign | **是** | ✅ |
| 26 | AdvancedFlow | Delegate | oa-settings | delegate | **是** | ✅ |
| 27 | Approval | Submit | oa-form-catalog | submit | 状态 | ✅ |
| 28 | Flow | SaveDef | oa-designer | edit | **是** | ✅ |
| 29 | Flow | Submit | oa-form-catalog | submit | 状态 | ✅ |
| 30 | Flow | Act | oa-inbox | approve | **是** | ✅ |
| 31 | Form | SaveDef | oa-designer | form-save | **是** | ✅ |
| 32 | Form | SubmitData | oa-form-catalog | submit | 状态 | ✅ |
| 33 | Task | Withdraw | oa-inbox | withdraw | 状态 | ✅ |

自洽：33 非 GET 端点 = 31 真写贴点 + 2 豁免。grep `RequirePermission("oa-` = 31（跨 14 控制器；Forecast/Query 各 0）。

### 逐控制器贴点数核对（grep 计数）
PrefController 1 / NotificationController 2 / InboxController 5 / FlowAdminController 1 / DraftController 4 / TaskController 1 / FormController 2 / FlowController 3 / ApprovalController 1 / AdvancedFlowController 3 / DesignerController 2 / ApproverMapController 3 / CatalogController 1 / DelegateController 2 = **31** ✅

## 验证

- `dotnet build CP6.WebApi`：**Build succeeded, 0 Error**（1 既有 warning，与本任务无关）。
- `dotnet test`（全量）：**Passed! Failed: 0, Passed: 1764, Skipped: 5, Total: 1769**。基线 1764 零跌。

## 自查（六问）

1. 31 全贴逐字？✅ 键值逐字对齐真相源 §一表。
2. 2 豁免未贴？✅ Forecast/Query 无属性、无 using。
3. 三 delegate 统一键？✅ 均 `("oa-settings","delegate")`。
4. 零方法体改动？✅ 仅 `using` + 属性行；方法体、类级 [Authorize] 未动。
5. 高危未降级？✅ 8 高危键独立，approve/transfer/sendback/addsign/delegate 未与 read/edit/view 混授。
6. 逐控制器对账？✅ 见上表（33=31+2）。

## 交接给下游

- T3b：逐租户 `Sys_MenuAction`/`Sys_RoleAction` 种子（本任务不种；PermissionService 无 admin 旁路，未种前 admin 亦 403）。
- T4：反射 fail-closed 测试。
- 双栈孤儿路由 `/wf/form-designer`、`/wf/flow-designer`（真相源 §六头号裁决点）仍待用户裁决退役/收编——本任务照真相源贴 `oa-designer:*`，退役时属性随端点一并删无冲突。
