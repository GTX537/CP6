# M-MES T1 任务简报：权限键清单(端点×权限键真相源)

## 背景与位置
M-MES 横切接线波首任务(branch=feat/m-mes-crosscutting)。本波按 M-WMS/M-ERP 同型流程收口授权粒度: T1 产出「端点×权限键」真相源,是后续 T2 菜单锚定/T3a 贴点/T3b 种子/T4 反射测试的唯一依据——**键错一字全链 403,清单质量决定整波**。

## 必读(按顺序)
1. `docs/00-横切接线规范.md`(命门: 键连字符非下划线/资源键=锚定菜单 MenuKey/RoleAction 逐租户)
2. 同型先例: `docs/seeds/erp-permission-keys.md`(M-ERP T1 交付物——结构、豁免论证方式、§七扫描面口径照此)与 `docs/seeds/wms-permission-keys.md`
3. 扫描对象: `CP6.WebApi/Controllers/Mes/` 全部控制器(实有 11 个: DefectRecord/Machine/MesDashboard/Oee/PlanAchievement/PlanningBoard/ProcessCostRate/ProductionResult/QualityInspection/WorkCenter/WorkOrder;计划口径 10,以实扫为准并在 §七 说明差异)

## 需求
1. **全量扫描**: 11 控制器全部非 GET 端点逐方法列表,双向验证(端点数闭环,零缺漏零 GET 误列)。
2. **逐端点定键**: `mes-xxx:action` 格式(连字符!),资源键按「锚定菜单」原则规划——先查 Sys_Menu 现有 MES 菜单行(Program.cs 种子/迁移里 MES 段 MenuId 与 RoutePath),给出每键的锚定菜单候选;RoutePath 若为裸路径(M-ERP 曾因此全体失配),在 §硬前置 里标红提示 T2。
3. **高危键独立**(计划点名: 报工修正/工单强制关闭;实扫如有其他不可逆/钱相关操作一并拆分,如报废判定、成本费率修正),每个高危拆分给一句佐证。
4. **只读 POST 豁免**: 查询/报表/看板类 POST 端点逐条读 Service 证得无写库才可豁免,逐条记论证(照 ERP §四格式)。
5. **交付**: `docs/seeds/mes-permission-keys.md`(§一 端点×键全表/§二 menu-key 与锚定菜单候选/§三 高危佐证/§四 豁免论证/§五 归并裁决/§六 T2 硬前置与悬案/§七 扫描面口径与计数自洽)。
6. 纯文档任务,零代码改动。计数必须自洽(总端点=真写+豁免,逐控制器分解可复核)。

## 验证与提交
- 无测试要求(文档任务),但 §七 计数自洽是审查硬门。
- 单 commit(docs 前缀)即 push。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\mes-t1-report.md`(逐控制器计数表+悬案清单)。回复只返回: 状态、commit sha、一行计数摘要(N控制器/M真写/K豁免/J键)、concerns、报告路径(15 行内)。