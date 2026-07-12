# M-MES T5 执行报告：IAuditable 贴点(WorkOrder/ProductionResult 族)+跨波票收口

分支 feat/m-mes-crosscutting。机制=CP6Context `ChangeTracker.Entries<IAuditable>()` 泛型遍历纯标记接口，
零迁移零业务改动。裁决口径=**逐实体字段级实查**：实含货币/定价字段，或不可逆生产事实记录 → 纳入；
纯派生/計数器高频写 → 豁免(源码注释+负测试坐实)。

## 逐实体裁决表(字段级证据)

| # | 实体 | 查过的关键字段(源码实读) | 裁决 |
|---|------|--------------------------|------|
| 1 | WorkOrder(指図头) | ProductionQty/CompletedQty/DefectQty `decimal(21,8)`(完了/不良累计=不可逆生产事实)、ActualStartDate/ActualEndDate(实绩时刻)、Status(指图状态机 0-9) | **纳入** |
| 2 | WorkOrderProcess(工程明细) | ActualMachineHour/ActualLaborHour `decimal(21,8)`(注释"用于制造费用/直接人工"=**直喂成本**)、GoodQty/DefectQty、IsHourOverridden(工时覆盖标志) | **纳入** |
| 3 | WorkOrderMaterial(材料明细) | ActualQty `decimal(21,8)` 実績消費数量(不可逆消耗事实→喂材料成本)、PlanQty | **纳入** |
| 4 | ProductionResult(报工实绩) | GoodQty/DefectQty(良品/不良报工事实)、LaborHour/MachineHour `decimal(21,8)`(**喂成本工时**)、ActualStartTime/EndTime | **纳入** |
| 5 | ProcessCostRate(工序费率) | LaborRate/OverheadRate `decimal(21,8)` **元/h 货币费率**(T1 定高危、直喂成本归集)、ValidFrom/ValidTo(生效区间) | **纳入** |
| 6 | WorkCenter(工作中心主数据) | DailyCapacityHours `decimal(21,8)`(CRP 产能地基)、WgCd/Enable——费率与产能挂载点主数据 | **纳入** |
| 7 | Machine(设备主数据) | StandardCycleSec `decimal(10,4)`/CapacityPerHour `decimal(10,2)`(喂 OEE 性能计算)、Status、维护日期——低频主数据 | **纳入** |
| 8 | MachineDowntime(停止記録) | StartTime/EndTime(停止事实时刻)、DowntimeMinutes、DowntimeType/ReasonCd——OEE 可用率**源**数据、不可逆停止事实、有生命周期更新(start→end) | **纳入** |
| 9 | DefectRecord(不良品記録) | DefectQty `decimal(21,8)`(不良数)、Status(处置状态机 0起票→3完了)、CauseAnalysis/CorrectiveAction(是正处置留痕)、DueDate/CompletedDate | **纳入** |
| 10 | DefectCategory(不良分類マスタ) | CategoryCd/DetailCd/CategoryName/DetailName(分类主数据)、SortOrder、ActiveFlg——低频分类主数据留痕 | **纳入** |
| 11 | QualityInspection(検査ヘッダ) | OverallResult(合格/不合格判定)、DispositionAction(処置:手直し/返品/廃棄)、InspectionQty/SampleQty `decimal(21,8)`——质量判定事实 | **纳入** |
| 12 | QualityInspectionItem(検査明細) | MeasuredValue/StandardValue/UpperLimit/LowerLimit `decimal(21,8)`(計測值事实)、Result(項目判定) | **纳入** |
| 13 | InspectionTemplate(検査テンプレート) | StandardValue/UpperLimit/LowerLimit `decimal(21,8)`(检查公差**规格主数据**,改容差=质量关键)、RequiredFlg/ActiveFlg | **纳入** |
| 14 | **PlateMoldStock**(WMS 印版・木型 在庫, 跨波票) | **MadeCost `decimal(18,2)` 製作費(货币)**、UsedShots/MaxShots(寿命)、Status——M-WMS T6 漏圈,本波顺手收 | **纳入** |
| — | OeeDaily(OEE 日次集計) | Availability/Performance/Quality/Oee `decimal(8,4)`(%)+GoodQty/DefectQty——**全字段由 MachineDowntime+ProductionResult 重算派生**,无源真值、日频重算,无货币 | **豁免**(源码注释+负测试) |
| — | MesSequence(採番管理) | CurrentValue `int`——**純採番計数器**每次採番自增,无货币/无业务事实字段,高频写(`BaseEntity` 非 BizEntity) | **豁免**(源码注释+负测试) |

**裁决计数：纳入 14(13 MES + 1 WMS PlateMoldStock)，豁免 2(OeeDaily/MesSequence)。**

### 豁免与「按类别套先例」防错说明
- ERP 两轮返工教训=豁免含货币字段实体。本波逐实体全字段扫过，**两个豁免实体均实读确认零货币字段**：
  OeeDaily 仅有 %率与数量派生值；MesSequence 仅 int 计数器。均非「未实查就豁免」。
- 未按「主数据一律豁免/头明细一律纳入」套壳：主数据 Machine/WorkCenter/DefectCategory/InspectionTemplate
  逐个查字段后判定其含产能/费率挂载/公差规格/分类等关键留痕价值而纳入(与 ERP ProductMaster/BusinessPartner
  主数据入审计一致)；派生 OeeDaily 虽含数量字段仍豁免(派生非源)。

## TDD Evidence
- **RED**: 写 `CP6.Tests/Mes/MesAuditTests.cs`(18 用例)后先跑 → `Failed: 16, Passed: 2`
  (16 正测试红=实体未贴 IAuditable；2 负测试 OeeDaily/MesSequence 本就无审计行=先绿)。
- **GREEN**: 贴 IAuditable 于 13 MES 实体 + PlateMoldStock 后 → `Passed: 18, Failed: 0`。
- 用例形状照 ErpAuditTests：真值断言 EntityName + EntityKey(op1) / 字段级 Old→New diff(op2)。
  货币键点覆盖：ProcessCostRate.LaborRate 30→35、WorkOrderProcess.ActualMachineHour 2→3.5、
  ProductionResult.LaborHour 1→2.25、PlateMoldStock.MadeCost 30000→32000、
  InspectionTemplate.UpperLimit 100→102、QualityInspectionItem.MeasuredValue 10.0→10.5。
- 负测试真实：OeeDaily create + MesSequence create→update 后 `Assert.Empty(Sys_FieldAuditLogs)`。

## 验证
- `dotnet test --filter MesAuditTests` → 18/18 绿。
- 全量 → `Passed: 1749, Failed: 0, Skipped: 5`(基线 1731 + 18 新用例，无跌落)。
- `git status` → 仅 16 实体单行改 + 1 新测试文件，**无 Migrations 新文件**(纯标记接口零迁移)。

## Concerns
- **DefectCategory 是最薄纳入项**(纯分类 lookup 码表)。按「主数据留痕+过度纳入低频主数据零害」原则纳入，
  且与 ERP 主数据入审计口径一致；若审查者倾向 lookup 码表豁免，可改判但需补负测试——非货币非事实，改判不违 ERP 教训。
- MachineDowntime 归为「生产事实源」而纳入(区别于 OeeDaily 派生)；若审查者视其为遥测类可再议，但其为手工录入的停止事件源数据、非高频遥测。
