# A2 工艺路线完善（标准工时 + 工序费率 + 工时采集 + 成本做真）设计 spec

> **来源**：ERP 完整性路线 `docs/00-ERP完整性路线.md` 第 A2 项。经 2026-06-17 brainstorming 定稿（决策 A2-D1~D5）。
> **风格**：照 CP6 现有代码逆向、不编造；可落码详细规格（字段级类型/可空/约束 + 服务签名 + 公式 + 错误码 + 测试）。
> **下一步**：本 spec 定稿 → `writing-plans` 转实施计划 → TDD 编码 + gstack QA。

---

## 一、题眼

**把制造成本的 工(Labor)/费(Overhead) 从"传入估算"做真。** 现状（已勘察 `CostCollectService.cs`）：

- **料 = 真**：`WorkOrderMaterial.ActualQty × ProductMaterial.SupplyPrice`，实际 vs 标准（计划用量×同单价）差异都算、落 `CostSheetLine`。
- **工/费 = 估算**：`CollectAsync(workOrderNo, laborStd, overheadStd, user)` 直接把外部传入的 `laborStd`/`overheadStd` 当一行（代码注释原文："工/费标准估算行（无 MES 工时，按传入额；用量/单价留空）"）。

A2 补齐：**标准工时 = 段取 + 数量×单件**，**成本 = 工时 × 工序费率（工/费双率）**，**工时差异 = (实际工时 − 标准工时) × 费率**，并铺 CRP 产能地基（标准工时数据 + 工作中心日产能字段）。**料不动**。

---

## 二、决策表（brainstorming 定稿，A2-D1~D5）

| # | 议题 | 现状 | **定稿** |
|---|---|---|---|
| **A2-D1** | 标准工时模型 | `ProductProcess` 无工时字段 | **段取 + 单件**：`ProductProcess` 加 `SetupHour`(段取,固定/批) + `CycleTime`(单件,h/件)；工单标准工时 = `SetupHour + 数量 × CycleTime` |
| **A2-D2** | 工序费率 | 无费率；`WgCd` 无主表 | **工作中心表 + 工/费双率**：新建 `WorkCenter`(by WgCd) + `ProcessCostRate`(by WgCd + 生效日, `LaborRate`/`OverheadRate` 元/h)；取≤基准日最新（同 `SupplierPriceService`） |
| **A2-D3** | 实绩工时采集 | `WorkOrderProcess` 无工时；`ProductionResult` 有起止时刻 | **派生为主 + 可覆盖**：`WorkOrderProcess` 加 `ActualWorkingHour`；`ProductionResultService` 完了报工时按起止时刻累加（扣中断）物化；实绩带显式工时则覆盖 |
| **A2-D4** | 工时差异 / GL | 结转按实际入 WIP→FG，料差异仅报表不入 GL | **实际成本法 + 差异仅报表**：工/费改 工时×费率（实际额/标准额都算），结转贷 `DIRECT_LABOR`/`MFG_OVERHEAD` 用**实际额**；工时差异仅成本单展示，不单独入 GL（与现料差异处理一致） |
| **A2-D5** | CRP 产能边界 | 无产能字段 | **只铺地基**：`WorkCenter` 加 `DailyCapacityHours`（CRP 入参占位）；不建 CRP 负荷引擎（留 MRP P4） |

### 命名空间

`WorkCenter` / `ProcessCostRate` 落 **Mes**（`CP6.Entity.DomainModels.Mes` / `CP6.Core.Services.Mes`）——制造/产能主数据。Fin 的 `CostCollectService` 跨模块读 `ProcessCostRate`（同其已跨模块读 Erp `ProductMaterial` 的模式）。

---

## 三、数据模型（字段级，照现有逆向）

### 3.1 扩展 `ProductProcess`（`CP6.Entity.DomainModels.Erp.ProductProcess`，表 `T_ProductProcess`）

现有相关字段（不改）：`ProductCd`/`TaskCd`/`ProcessCd`/`WgCd`/`MachineOrVendor`/`LeadTime`(decimal? 制造リードタイム日)/`LossRate`/`PurchasePrice`。新增：

```csharp
/// <summary>段取工时（h，固定/批；与数量无关）。标准工时 = SetupHour + 数量 × CycleTime。</summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? SetupHour { get; set; }

/// <summary>单件加工工时（h/件）。</summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? CycleTime { get; set; }
```

> `LeadTime`(提前期) 与 `SetupHour/CycleTime`(产能工时) 正交：前者供 MRP 反推下达日（A1 已用），后者供成本/CRP。两者并存。

### 3.2 新建 `WorkCenter`（`CP6.Entity.DomainModels.Mes`，表 `T_WorkCenter`，继承 `BaseBizEntity`）

```csharp
[Table("T_WorkCenter")]
public class WorkCenter : BaseBizEntity
{
    /// <summary>工作中心CD（业务键，唯一；= ProductProcess.WgCd / WorkOrderProcess.WgCd）</summary>
    [Required, MaxLength(10)] public string WgCd { get; set; } = string.Empty;
    /// <summary>工作中心名称</summary>
    [MaxLength(100)] public string? WgName { get; set; }
    /// <summary>日可用产能（h/日）——CRP 入参地基（A2-D5），A2 不消费</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? DailyCapacityHours { get; set; }
    /// <summary>启用</summary>
    public bool Enable { get; set; } = true;
}
```
索引：`UX_Mes_WorkCenter_Wg` 唯一(WgCd)（多租户重写自动补 TenantId 前缀）。

### 3.3 新建 `ProcessCostRate`（`CP6.Entity.DomainModels.Mes`，表 `T_ProcessCostRate`，继承 `BaseBizEntity`）

```csharp
[Table("T_ProcessCostRate")]
public class ProcessCostRate : BaseBizEntity
{
    /// <summary>工作中心CD（FK 业务键 → WorkCenter.WgCd）</summary>
    [Required, MaxLength(10)] public string WgCd { get; set; } = string.Empty;
    /// <summary>人工费率（元/h）</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal LaborRate { get; set; }
    /// <summary>制造费率（元/h）</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal OverheadRate { get; set; }
    /// <summary>生效日（取 ≤ 基准日 的最新一条；同 SupplierPrice 改版口径）</summary>
    public DateTime ValidFrom { get; set; }
    /// <summary>失效日（null = 长期）</summary>
    public DateTime? ValidTo { get; set; }
}
```
索引：`IX_Mes_ProcessCostRate_Wg`(WgCd, ValidFrom)。

### 3.4 扩展 `WorkOrderProcess`（`CP6.Entity.DomainModels.Mes`，表 `T_WorkOrderProcess`）

现有相关字段（不改）：`WorkOrderNo`/`ProcessCd`/`TaskCd`/`WgCd`/`ProcessStatus`/`PlanQty`/`GoodQty`/`DefectQty`/`StdLossRate`/`LeadTime`/`ActualStartTime`/`ActualEndTime`。新增：

```csharp
/// <summary>实绩工时（h）。ProductionResultService 完了报工时按起止时刻累加（扣中断）物化；实绩带显式工时则覆盖（A2-D3）。</summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? ActualWorkingHour { get; set; }
```

### 3.5 扩展 `ProductionResult`（`CP6.Entity.DomainModels.Mes`，表 `T_ProductionResult`）

现有：`ResultType`(1开始/2中断/3中断解除/4完了/5数量報告)/`ActualStartTime`/`ActualEndTime`/`OperatorCd`/`MachineCd`。新增（可选显式工时覆盖）：

```csharp
/// <summary>本次报工工时（h，可选）。填写则按此值累加/覆盖派生工时（A2-D3 可覆盖）。</summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? WorkingHour { get; set; }
```

### 3.6 扩展 `CostSheet` + `CostSheetLine`（`CP6.Entity.DomainModels.Fin`）

`CostSheet` 现有：`MaterialActual`/`MaterialStandard`/`LaborStd`/`OverheadStd` + NotMapped `TotalActual=Material+Labor+Overhead`/`StandardCost`/`Variance`/`FgUnitCost`。

**改造**：把单值 `LaborStd`/`OverheadStd` 升级为 实际/标准 双值（保留旧字段名做兼容映射，见迁移）：

```csharp
[Column(TypeName = "decimal(18,2)")] public decimal LaborActual { get; set; }
[Column(TypeName = "decimal(18,2)")] public decimal LaborStandard { get; set; }
[Column(TypeName = "decimal(18,2)")] public decimal OverheadActual { get; set; }
[Column(TypeName = "decimal(18,2)")] public decimal OverheadStandard { get; set; }
// NotMapped 重算：
// TotalActual   = MaterialActual + LaborActual + OverheadActual
// StandardCost  = MaterialStandard + LaborStandard + OverheadStandard
// Variance      = TotalActual − StandardCost
// FgUnitCost    = CompletedQty>0 ? TotalActual / CompletedQty : 0
```

`CostSheetLine` 加工时承载（料行留空，工/费行填）：

```csharp
/// <summary>工时（h，工/费行用；料行留空）</summary>
[Column(TypeName = "decimal(21,8)")] public decimal? Hours { get; set; }
/// <summary>标准工时（h，工/费行用）</summary>
[Column(TypeName = "decimal(21,8)")] public decimal? StandardHours { get; set; }
// 复用现有 UnitPrice 承载费率（元/h）、ActualAmount/StandardAmount 承载金额
```

> 迁移兼容：旧 `LaborStd` → 映射进 `LaborStandard` 且 `LaborActual=LaborStd`（历史成本单等价不变）；新逻辑写四字段。`CostElement` 枚举(Material/Labor/Overhead) 不变。

---

## 四、服务层（签名 + 公式）

### 4.1 新建 `IWorkCenterService` / `WorkCenterService`（`CP6.Core.Services.Mes`）

```csharp
public interface IWorkCenterService
{
    Task<List<WorkCenter>> ListAsync(string? keyword);
    Task UpsertAsync(WorkCenter dto, string? user);   // 按 WgCd upsert
    Task DeleteAsync(string wgCd, string? user);       // 软删
}
```
照 `ItemPlanningPolicyService`（A1）/`SupplierPriceService` 既有 CRUD 模式。

### 4.2 新建 `IProcessCostRateService` / `ProcessCostRateService`（`CP6.Core.Services.Mes`）

```csharp
public interface IProcessCostRateService
{
    Task<List<ProcessCostRate>> ListAsync(string? wgCd);
    Task<ProcessCostRate?> ResolveAsync(string wgCd, DateTime onDate);  // ≤onDate 最新且未失效
    Task UpsertAsync(ProcessCostRate dto, string? user);
    Task DeleteAsync(Guid id, string? user);
}
```
**ResolveAsync**：`Where(WgCd==wgCd && !IsDeleted && ValidFrom<=onDate && (ValidTo==null || ValidTo>=onDate)).OrderByDescending(ValidFrom).FirstOrDefault()`（同 `EstimateCalcService` 取見積用シート単価 / `SupplierPriceService` 口径）。

### 4.3 扩展 `ProductionResultService`（`CP6.Core.Services.Mes`，已有）

报工落库后聚合实绩工时到 `WorkOrderProcess.ActualWorkingHour`：

- **派生**：对 (WorkOrderNo, ProcessCd, TaskCd) 取本工序全部实绩，按 配对区间 求净工时——`完了/数量報告`(含 ActualStart/End) 累加 `(End−Start)` 小时；`中断→中断解除` 区间不计。
- **覆盖**：若 `ProductionResult.WorkingHour` 有值，则用其累加（人工填工时优先于时刻派生，按报工行二选一：行有 WorkingHour 用它，否则用时刻差）。
- 物化：`WorkOrderProcess.ActualWorkingHour = Σ本工序各报工净工时`。无 `WorkOrderProcess` 行则跳过（防御）。

### 4.4 改造 `CostCollectService.CollectAsync`（核心，`CP6.Core.Services.Fin`）

签名保持兼容（`laborStd`/`overheadStd` 改为**可选回退**）：

```csharp
Task<FinResult> CollectAsync(string workOrderNo, decimal laborStd, decimal overheadStd, string user);
```

**料**：不变（现逻辑）。**工/费 改造**：

1. 取工单各工序：`WorkOrderProcess where WorkOrderNo==wo && !IsDeleted`。
2. 取标准工时（按 ProductCd+ProcessCd+TaskCd join `ProductProcess`，同料 join `ProductMaterial` 的口径）：
   `stdHour(工序) = (SetupHour ?? 0) + CompletedQty × (CycleTime ?? 0)`
   （CompletedQty 取 `WorkOrder.CompletedQty`；段取按工单计一次，不乘数量。）
3. 取实际工时：`actHour(工序) = WorkOrderProcess.ActualWorkingHour ?? 0`。
4. 取费率：`rate = ProcessCostRateService.Resolve(WorkOrderProcess.WgCd, 工单基准日)`；基准日 = `WorkOrder.PlanStartDate ?? CreateDate ?? Today`。
5. 金额（逐工序累加，落 `CostSheetLine` Element=Labor / Overhead，Hours/StandardHours/UnitPrice=率/ActualAmount/StandardAmount）：
   - `LaborActual   += actHour × rate.LaborRate`
   - `LaborStandard += stdHour × rate.LaborRate`
   - `OverheadActual   += actHour × rate.OverheadRate`
   - `OverheadStandard += stdHour × rate.OverheadRate`
   - 工时差异（展示）：`(actHour − stdHour) × rate`。
6. 写 `CostSheet.{LaborActual,LaborStandard,OverheadActual,OverheadStandard}`。
7. **平滑回退**：若某工序无费率（Resolve 返回 null）或全工单无 `WorkOrderProcess`/无工时数据，则退回传入 `laborStd`/`overheadStd`（旧行为），保证迁移期不破、旧调用方仍可用。回退命中时在成本单标注（line note）。

### 4.5 微调 `CostSettleService.SettleAsync`（`CP6.Core.Services.Fin`）

- 贷方金额：`DIRECT_LABOR` 用 `sheet.LaborActual`、`MFG_OVERHEAD` 用 `sheet.OverheadActual`（原用 `LaborStd`/`OverheadStd`）。
- `total = TotalActual`（已含真实工/费），WIP→FG 不变。
- 差异不单独入 GL（A2-D4）。

---

## 五、API + 前端

### 5.1 控制器（`CP6.WebApi/Controllers/Mes`）
- `WorkCenterController`：`/api/mes/work-center`（List/Get/Upsert/Delete）。
- `ProcessCostRateController`：`/api/mes/process-cost-rate`（List by wgCd/Upsert/Delete/Resolve 调试）。
- 返回包 `{code,message,data}`，`[Authorize]`，照既有控制器。

### 5.2 前端（`cp6.web/src/views`）
- `mes/WorkCenterView.vue`：工作中心主数据 CRUD（含 DailyCapacityHours）。
- `mes/ProcessCostRateView.vue`：费率维护（按 WgCd 列生效日序列 + 工/费率）。
- `erp/ProductMasterView`（製品工程页）：ProductProcess 编辑加 `SetupHour`/`CycleTime` 列。
- `fin/CostSheetView.vue`：成本单加 工/费 实际/标准/差异列 + 工时行展示。
- api/types/路由 + 菜单（MES 组下新增 工作中心/工序费率）+ 五语 i18n（含 E-* 错误码）。

---

## 六、桩 / 迁移 / 多租户 / 权限
- 迁移 `*_A2ProcessRouting`（ProductProcess+2 / WorkCenter / ProcessCostRate / WorkOrderProcess+1 / ProductionResult+1 / CostSheet 改 4 字段 + CostSheetLine+2）。
- 新实体继承 `BaseBizEntity`（多租户自动隔离 + 复合唯一索引自动补 TenantId 前缀，A1 已验证）。
- 权限：随主数据贴资源键（或延后，同既有约定）。
- 跨模块：Fin→Mes 读 `ProcessCostRate` 同步直读（同 Fin→Erp 读 ProductMaterial），不走事件。

---

## 七、测试（TDD，纯单测为主）

| 测试 | 断言 |
|---|---|
| `ProcessCostRateServiceTests.Resolve_TakesLatestEffective` | 多版本费率取 ≤基准日最新；失效后不取 |
| `ProductionResultService_AggregatesActualHours` | 起止时刻累加；中断区间不计；多次报工累加 |
| `ProductionResultService_ExplicitHourOverrides` | 报工带 WorkingHour 则用其、不用时刻 |
| `CostCollect_StandardHour_SetupPlusQtyTimesCycle` | 标准工时 = Setup + CompletedQty×Cycle |
| `CostCollect_LaborOverhead_HoursTimesRate` | 工=实际工时×LaborRate、费=实际工时×OverheadRate；标准额=标准工时×率 |
| `CostCollect_Variance_ActualMinusStandard` | 工时差异 = (实际−标准工时)×率，落 line |
| `CostCollect_NoRate_FallsBackToEstimate` | 无费率/无工时 → 回退传入 laborStd/overheadStd |
| `CostSettle_CreditsActualLaborOverhead` | 结转贷 DIRECT_LABOR/MFG_OVERHEAD = 实际额；FG=TotalActual |
| `WorkCenterService` CRUD | upsert/list/软删 |

gstack 真浏览器 QA：工作中心建 → 费率建（生效日）→ ProductProcess 填 Setup/Cycle → 报工 → 成本归集 → CostSheet 见工/费实际/标准/差异。

---

## 八、Self-Review（对照覆盖）

- **A2-D1**：ProductProcess SetupHour/CycleTime(3.1) ✅ 标准工时公式(4.4) ✅
- **A2-D2**：WorkCenter(3.2)+ProcessCostRate 双率(3.3)+ResolveAsync 生效日(4.2) ✅
- **A2-D3**：WorkOrderProcess.ActualWorkingHour(3.4)+ProductionResult.WorkingHour(3.5)+聚合派生/覆盖(4.3) ✅
- **A2-D4**：CostSheet 四字段(3.6)+CostCollect 工时×费率+差异(4.4)+settle 贷实际(4.5)+差异不入GL ✅
- **A2-D5**：WorkCenter.DailyCapacityHours(3.2) ✅ 不建 CRP 引擎(六) ✅

**已知推迟**：CRP 负荷引擎（MRP P4，消费本 spec 的 StandardHour + DailyCapacityHours）；标准成本法差异入账（与现实际成本模型不一致，不做）；费率审批流；机台级费率（A2 工作中心级，号机级后续）。

**Type 一致性**：`ProcessCostRate`(3.3) 被 CostCollect(4.4) 经 Resolve(4.2) 消费；`ProductProcess.SetupHour/CycleTime`(3.1) 被标准工时(4.4) + 将来 CRP 用；`WorkOrderProcess.ActualWorkingHour`(3.4) 由 ProductionResultService(4.3) 写、CostCollect(4.4) 读；`CostSheet` 四字段(3.6) 被 settle(4.5) 贷实际额。

---

*生成于 2026-06-17。源：brainstorming A2-D1~D5 + 真实代码勘察（CostCollectService/CostSettleService/ProductProcess/WorkOrderProcess/ProductionResult/CostSheet）。下一步 writing-plans。*
