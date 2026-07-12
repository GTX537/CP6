# M-MES T3a 执行报告：9 控制器 28 写端点贴 [RequirePermission]

- **任务**：M-MES 横切接线波 T3a——9 个含真写端点的 MES 控制器逐方法贴 `[RequirePermission("menu-key","action")]`。
- **真相源**：`docs/seeds/mes-permission-keys.md`（键值逐字取用，未创造/未改名任何键）。
- **样板**：M-ERP T3a 先例 commit `bdbc532`（attribute 紧贴 `[HttpXxx]` 之下、`using CP6.Core.Auth;` 置顶）。
- **性质**：纯注解叠加，零方法体改动，类级 `[Authorize]` 全保留。

## 逐控制器「端点 → 键」1:1 对账表（28 = 28）

| # | 控制器 | HTTP + 路由 | 方法 | 键 | action | 高危 |
|---|---|---|---|---|---|---|
| 1 | WorkOrderController | POST `/api/mes/work-orders` | Create | `mes-work-order` | add | 否 |
| 2 | WorkOrderController | PUT `/api/mes/work-orders/{no}` | Update | `mes-work-order` | edit | 否 |
| 3 | WorkOrderController | DELETE `/api/mes/work-orders/{no}` | Delete | `mes-work-order` | del | 否 |
| 4 | WorkOrderController | POST `/api/mes/work-orders/{no}/issue` | Issue | `mes-work-order` | issue | 状态 |
| 5 | WorkOrderController | POST `/api/mes/work-orders/expand-from-order` | ExpandFromOrder | `mes-work-order` | add | 否 |
| 6 | ProductionResultController | POST `/production-results/start` | Start | `mes-production-result` | start | 状态 |
| 7 | ProductionResultController | POST `/production-results/suspend` | Suspend | `mes-production-result` | suspend | 状态 |
| 8 | ProductionResultController | POST `/production-results/resume` | Resume | `mes-production-result` | suspend | 状态 |
| 9 | ProductionResultController | POST `/production-results/complete` | Complete | `mes-production-result` | complete | **是** |
| 10 | ProductionResultController | POST `/production-results` | Report | `mes-production-result` | report | 状态 |
| 11 | DefectRecordController | POST `/api/mes/defects` | Create | `mes-defect` | add | 否 |
| 12 | DefectRecordController | PUT `/api/mes/defects/{no}` | Update | `mes-defect` | edit | 否 |
| 13 | DefectRecordController | DELETE `/api/mes/defects/{no}` | Delete | `mes-defect` | del | 否 |
| 14 | MachineController | POST `/api/mes/machines` | Create | `mes-machine` | add | 否 |
| 15 | MachineController | PUT `/api/mes/machines/{cd}` | Update | `mes-machine` | edit | 否 |
| 16 | MachineController | DELETE `/api/mes/machines/{cd}` | Delete | `mes-machine` | del | 否 |
| 17 | MachineController | POST `/api/mes/machines/{cd}/status` | ChangeStatus | `mes-machine` | status | 状态 |
| 18 | MachineController | POST `/api/mes/machines/downtimes` | RegisterDowntime | `mes-machine` | downtime | 状态 |
| 19 | MachineController | POST `/api/mes/machines/downtimes/{no}/close` | CloseDowntime | `mes-machine` | downtime | 状态 |
| 20 | OeeController | POST `/api/mes/oee/recalculate` | Recalculate | `mes-oee` | recalculate | 状态 |
| 21 | PlanningBoardController | PUT `/planning-board/reschedule` | Reschedule | `mes-planning-board` | reschedule | 状态 |
| 22 | PlanningBoardController | POST `/planning-board/auto-arrange` | AutoArrange | `mes-planning-board` | arrange | 状态 |
| 23 | QualityInspectionController | POST `/api/mes/inspections` | Create | `mes-quality-inspection` | add | 否 |
| 24 | QualityInspectionController | PUT `/api/mes/inspections/{no}` | Update | `mes-quality-inspection` | edit | 否 |
| 25 | WorkCenterController | POST `/work-center/upsert` | Upsert | `mes-work-center` | edit | 否 |
| 26 | WorkCenterController | DELETE `/work-center/{wgCd}` | Delete | `mes-work-center` | del | 否 |
| 27 | ProcessCostRateController | POST `/process-cost-rate/upsert` | Upsert | `mes-process-cost-rate` | edit | **是** |
| 28 | ProcessCostRateController | DELETE `/process-cost-rate/{id}` | Delete | `mes-process-cost-rate` | del | 否 |

> 真相源表行 25/26（PlanAchievement Summary/ExportCsv）为只读 POST 豁免（→view），T3a **不贴**；其余表行 1–24、27–30 即本表 1–28（真相源 §一为 30 行含 2 豁免，去豁免后 28 真写，精确吻合）。

### 逐控制器计数（grep `RequirePermission("mes-` 实证）

| 控制器 | 贴点数 |
|---|---|
| WorkOrderController | 5 |
| ProductionResultController | 5 |
| DefectRecordController | 3 |
| MachineController | 6 |
| OeeController | 1 |
| PlanningBoardController | 2 |
| QualityInspectionController | 2 |
| WorkCenterController | 2 |
| ProcessCostRateController | 2 |
| **合计** | **28** ✅ |

## 合规缺席说明（两控制器不贴，符合真相源）

- **MesDashboardController**：纯 GET 看板/SP 版，0 非 GET 端点 → 无贴点。合规。
- **PlanAchievementController**：2 个 POST（Summary/ExportCsv）均为只读 POST 豁免（→view，真相源 §四读 Service 实证无写副作用），真写=0 → 全员豁免不贴。合规。
- grep 验证：`RequirePermission` 在 PlanAchievementController.cs 出现 0 次 ✅。

## 自查清单

- [x] 28 端点全贴，键值与真相源逐字一致（连字符 `mes-*`，无下划线，无自造/改名键）。
- [x] 2 只读 POST 豁免未贴（PlanAchievement Summary/ExportCsv）。
- [x] 高危未降级：`mes-production-result:complete`（是）、`mes-process-cost-rate:edit`（是）逐字保留独立键。状态键 9 个（issue/start/suspend×2/report/status/downtime×2/recalculate/reschedule/arrange）单独成键，未塞进 edit/view。
- [x] 归并键按真相源：resume→suspend（§五归并2）、CloseDowntime→downtime（§五归并3）、Upsert→edit（§五归并4）、ExpandFromOrder→add（§五归并1）。
- [x] quality-inspection 键取菜单域名（非控制器路由 `inspections`），逐字 `mes-quality-inspection`（§六注3）。
- [x] 零 GET 端点被贴（人工核对 + grep 各贴点均紧贴 POST/PUT/DELETE）。
- [x] 零方法体改动：仅新增 `using CP6.Core.Auth;`（9 文件各 1 行）+ 28 行 `[RequirePermission]` attribute。
- [x] 类级 `[Authorize]` 9 控制器全保留。

## 验证输出摘要

- **Build**：`dotnet build CP6.WebApi` → `Build succeeded. 0 Error(s)`（1 既有 CS8601 warning，与本任务无关）。
- **Test**：`dotnet test` → `Passed! Failed: 0, Passed: 1722, Skipped: 5, Total: 1727`。基线 1722 绿，零跌。

## 交接给后续任务

- T3b：`Sys_MenuAction`/`Sys_RoleAction` 逐租户种子（本任务只贴点，不种子）。
- T4：反射 fail-closed 测试。
- 硬前置提醒（真相源 §六，属 T2/T4 范畴，非本任务）：菜单 310 须显式赋 `MenuKey="mes-machine"`（回填得 `mes-machine-list` ≠ 贴点 `mes-machine`），否则 Machine 全 403；MES 菜单插入在回填块之后，洁净首启 MenuKey null 需 T2 显式赋值治理。
