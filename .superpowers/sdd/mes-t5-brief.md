# M-MES T5 任务简报：IAuditable 贴点(WorkOrder/ProductionResult 族)+跨波票收口

## 背景与位置
M-MES 横切接线波第六任务(计划原文: 「T4 IAuditable: WorkOrder/ProductionResult 贴点」)。先例=M-WMS T6(eb5fb51)与 M-ERP T5(45df1ce+df878f1+f605ce0——**注意它经历两轮返工,教训=豁免裁决必须逐实体字段级实查,禁止按实体类别/头明细套先例**)。拦截器机制: CP6Context ChangeTracker.Entries<IAuditable>() 泛型遍历,纯标记接口零迁移。

## 需求
1. 圈定贴点实体(CP6.Entity/DomainModels/Mes/ 全目录实查):
   - WorkOrder(工单)头/相关明细
   - ProductionResult(报工/生产实绩)
   - 其余 MES 实体(Machine/WorkCenter/ProcessCostRate/DefectRecord/QualityInspection/Oee 快照等)按**「实含货币/定价字段,或不可逆生产事实记录」即纳入**口径逐实体裁决——ProcessCostRate(工序费率,直喂成本归集,T1 已定高危)显然应纳入;高频追加型(如 OEE 快照/机台遥测类)可豁免但须源码注释+负测试坐实。
   - **逐实体裁决表**(纳入/豁免+字段级证据)是交付物核心,每行给出「查过哪些字段」。
2. **跨波票收口**: `CP6.Entity/DomainModels/Wms/PlateMoldStock.cs` 含 MadeCost decimal(18,2) 未贴 IAuditable(M-WMS T6 未圈入,M-ERP 终审记票指定本波顺手收)——单行补贴+一条真值测试。
3. 每实体单行追加 IAuditable,零其他改动、零迁移、零业务逻辑改动。
4. 测试: MesAuditTests 照 ErpAuditTests 形状——真值断言(EntityName/Field/Old/New)含负测试(豁免实体不产日志)。
5. 若发现贴点引发迁移,停手报 DONE_WITH_CONCERNS/BLOCKED。

## 验证与提交
- `dotnet test --filter MesAuditTests` 全绿;全量(基线 1731)不许跌;git status 无 Migrations 新文件。
- 单 commit 即 push。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\mes-t5-report.md`(逐实体裁决表+字段级证据+TDD 证据)。回复只返回: 状态、commit sha、一行测试结论、裁决计数一行(纳入N/豁免M)、concerns、报告路径(15 行内)。