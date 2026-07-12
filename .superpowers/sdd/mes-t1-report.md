# M-MES T1 报告：权限键清单（端点×权限键真相源）

**Status:** DONE
**交付物:** `C:\CP6\docs\seeds\mes-permission-keys.md`（纯文档，零代码改动）

## 逐控制器计数表

| 控制器 | 非GET端点 | 豁免 | 真写 | menu-key | 高危 |
|---|---|---|---|---|---|
| WorkOrderController | 5 | 0 | 5 | mes-work-order | — |
| ProductionResultController | 5 | 0 | 5 | mes-production-result | complete |
| DefectRecordController | 3 | 0 | 3 | mes-defect | — |
| MachineController | 6 | 0 | 6 | mes-machine | — |
| OeeController | 1 | 0 | 1 | mes-oee | — |
| PlanningBoardController | 2 | 0 | 2 | mes-planning-board | — |
| QualityInspectionController | 2 | 0 | 2 | mes-quality-inspection | — |
| PlanAchievementController | 2 | 2 | 0 | mes-plan-achievement | — |
| WorkCenterController | 2 | 0 | 2 | mes-work-center | — |
| ProcessCostRateController | 2 | 0 | 2 | mes-process-cost-rate | edit |
| MesDashboardController | 0（全GET） | — | — | (309/312 看板) | — |
| **合计** | **30** | **2** | **28** | **10** | **2** |

自洽：30 非GET = 2 豁免 + 28 真写；逐控制器真写累加 = 28 ✅。资源键去重 26；状态键 9。

## 高危清单（2）

- `mes-production-result:complete` — 工程完了触发 WMS完成品入庫 + BOM料耗反冲(OUT/ISSUE) + FIN成本归集/结转（ProductionResultService.cs:263-293）。会话记忆 P0「完工反冲」。
- `mes-process-cost-rate:edit` — 工序费率修正，retroactive 改写已报工工单原価基础，经 CostCollectService 直喂 FIN 结转（ProcessCostRateService.cs:27-62；Program.cs:185）。简报点名。**与 ERP 费率 master 未提级不同**（MES 费率直喂已完工单成本，故提级；§五归并5 留降级出口待 T2 审计）。

## 豁免论证索引（2，均读 Service 证得无写）

- `mes-plan-achievement:view`（Summary）— PlanAchievementService.cs:33-96，仅 WorkOrders.AsNoTracking() 聚合，全类无 Add/SaveChanges。
- `mes-plan-achievement:view`（ExportCsv）— :98-120，调 Summary 后拼 CSV，纯读。

## §六 悬案/硬前置摘要

1. **头号命门（时序）**：回填块 Program.cs:894-901 在 MES 菜单插入 :1511-1611 **之前**执行 → 洁净部署首启 MES 菜单 MenuKey=null → PermissionAggregator 过滤掉 → **MES 全 403 直到二次重启**。T2 须在 MES 插入块显式赋 `MenuKey="mes-*"`（同 WMS/ERP T2 型）。
2. **命门2（machine 键错配）**：菜单 310 RoutePath=`/mes/machine-list` → 回填得 `mes-machine-list` ≠ 本表 `mes-machine`。T2 须对 310 显式赋 `mes-machine`。其余 9 键回填即正确（RoutePath 与键天然一致），但建议随 #1 一并显式化。
3. **注**：quality-inspection 键取菜单域名 `mes-quality-inspection`（非控制器路由 `inspections`），回填自 306 即一致。
4. **盘点差异（简报点名高危端点不存在直接 HTTP 端点）**：
   - 工单强制关闭 = WorkOrderService.CancelAsync 存在但未暴露端点（仅 ERP 受注取消 Bridge 内部调用），受 `erp-order:cancel` 管辖。
   - 报工修正 = 无独立修正/冲销端点；真高危为 complete（反冲，已提级）。
   - 报废判定 = QualityInspection 无 judge 端点、DefectRecord 为记录 CRUD，MES 无独立报废判定端点。
5. **ERP 对比利好**：MES RoutePath **有 `mes/` 前缀（非 ERP 裸路径命门）**，回填得 `mes-*`；**零孤儿路由**（15 个 views/mes/*.vue 全映射 300 段菜单，区别于 ERP 5 条孤儿）。

## Files changed
- 新增 `docs/seeds/mes-permission-keys.md`（真相源）
- 新增 `.superpowers/sdd/mes-t1-report.md`（本报告）

## Self-review findings
- 计数三重核验通过（总=豁免+真写；逐控制器分解；资源键去重）。
- 键全连字符 `mes-*`，高危 2 键各有 Service 行级佐证。
- 豁免 2 条均逐条读 Service 证无写（非按端点名猜）。
- §六 硬前置：RoutePath 形态（有 mes/ 前缀）、MenuId 段位（300-315）、回填时序命门、孤儿路由（零）、machine 键错配——均已查证并落 §六。

## Concerns
- **ProcessCostRate 高危提级为判断项**：与 ERP 费率 master 先例（未提级）不一致，理由是 MES 费率直喂已完工单成本归集。已在 §五归并5/§六.5 留「T2 审计可降级」出口。属需上层确认的裁决点，非硬错误。
- **回填时序命门**是 T2 的真正拦路虎（洁净部署首启 403），已在 §六头条标红——T2 若照抄 ERP「显式 MenuKey 前置」即可闭合。
