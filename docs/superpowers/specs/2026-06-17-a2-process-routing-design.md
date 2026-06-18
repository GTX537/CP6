# A2 工艺路线完善（标准工时 + 工序费率 + 实绩工时 + 成本做真）设计 spec v2

> **来源**：ERP 完整性路线 `docs/00-ERP完整性路线.md` 第 A2 项。基于 2026-06-17 brainstorming 定稿（A2-D1~D5）修订。  
> **修订目标**：在原 spec 基础上补齐“机时/人工工时双口径、标准人数、成本单追溯字段、费率区间校验、回退/错误码、测试覆盖”。  
> **风格**：照 CP6 现有代码逆向、不编造；可落码详细规格（字段级类型/可空/约束 + 服务签名 + 公式 + 错误码 + 测试）。  
> **下一步**：本 spec v2 定稿 → `writing-plans` 转实施计划 → TDD 编码 + gstack QA。

---

## 一、题眼

**把制造成本的 工(Labor)/费(Overhead) 从“传入估算”做真，同时不切换现有实际成本法。**

现状（已勘察 `CostCollectService.cs`）：

- **料 = 真**：`WorkOrderMaterial.ActualQty × ProductMaterial.SupplyPrice`，实际 vs 标准（计划用量 × 同单价）差异都算、落 `CostSheetLine`。
- **工/费 = 估算**：`CollectAsync(workOrderNo, laborStd, overheadStd, user)` 直接把外部传入的 `laborStd`/`overheadStd` 当一行。
- **结转 = 实际成本法**：WIP → FG 按实际额结转；材料差异只在成本单展示，不单独入 GL。

A2 补齐：

```text
标准机时 = SetupHour + 数量 × CycleTime
标准人工工时 = 标准机时 × StandardCrewSize
实际机时 = ProductionResult 按机器时间区间派生，允许覆盖
实际人工工时 = ProductionResult 按作业者时间累加，允许覆盖
人工成本 = 实际人工工时 × LaborRate
制造费用 = 实际机时 × OverheadRate
工/费差异 = 实际额 − 标准额，仅展示，不单独入 GL
```

核心边界：

```text
A2 做：标准工时、工作中心、费率、实绩工时、成本做真、成本差异展示、CRP 地基字段。
A2 不做：标准成本法、差异入账、CRP 负荷引擎、有限产能排程、月末差异分摊。
```

---

## 二、决策表（A2-D1~D5 修订锁版）

| # | 议题 | 现状 | **v2 定稿** |
|---|---|---|---|
| **A2-D1** | 标准工时模型 | `ProductProcess` 无工时字段 | **段取 + 单件 + 标准人数**：`SetupHour` 固定/批，`CycleTime` 单件 h/件，`StandardCrewSize` 标准人数；标准机时 = Setup + Qty×Cycle；标准人工工时 = 标准机时×人数 |
| **A2-D2** | 工序费率 | 无费率；`WgCd` 无主表 | **工作中心表 + 工/费双率**：新建 `WorkCenter` + `ProcessCostRate`；按 `WgCd + ValidFrom/ValidTo` 管理 `LaborRate`/`OverheadRate`；取 ≤ 基准日最新且未失效 |
| **A2-D3** | 实绩工时采集 | `WorkOrderProcess` 无工时；`ProductionResult` 有起止时刻 | **派生为主 + 可覆盖 + 双工时**：`WorkOrderProcess` 物化 `ActualMachineHour`/`ActualLaborHour`；机器按机器区间合并，人工按作业者累加；支持手工覆盖和来源标记 |
| **A2-D4** | 工时差异 / GL | 结转按实际入 WIP→FG；料差异仅报表不入 GL | **实际成本法 + 差异仅报表**：工/费改为工时×费率；结转贷 `DIRECT_LABOR`/`MFG_OVERHEAD` 用实际额；工时/金额差异只进成本单/报表，不单独入 GL |
| **A2-D5** | CRP 产能边界 | 无产能字段 | **只铺地基**：`WorkCenter.DailyCapacityHours` + `ProductProcess` 标准工时齐备；不建 CRP 负荷引擎，留 MRP P4 |

### 命名空间

`WorkCenter` / `ProcessCostRate` 落 **Mes**：

```text
CP6.Entity.DomainModels.Mes
CP6.Core.Services.Mes
```

理由：工作中心、产能、报工、工序实绩都属于制造/MES 侧基础数据。

`CostCollectService` 位于 Fin，但可跨模块读取 Mes 的 `ProcessCostRate` / `WorkOrderProcess`，这与现有 Fin 跨 Erp 读取 `ProductMaterial` 的模式一致。

---

## 三、数据模型（字段级）

### 3.1 扩展 `ProductProcess`

位置：

```text
CP6.Entity.DomainModels.Erp.ProductProcess
表：T_ProductProcess
```

现有相关字段不改：

```text
ProductCd / TaskCd / ProcessCd / WgCd / MachineOrVendor / LeadTime / LossRate / PurchasePrice
```

新增字段：

```csharp
/// <summary>
/// 段取工时（h，固定/批；与数量无关）。
/// 标准机时 = SetupHour + 数量 × CycleTime。
/// </summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? SetupHour { get; set; }

/// <summary>
/// 单件加工工时（h/件）。
/// 例如 0.002 表示每件 0.002 小时。
/// </summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? CycleTime { get; set; }

/// <summary>
/// 标准作业人数。
/// 标准人工工时 = 标准机时 × StandardCrewSize。
/// 为空时按 1 处理。
/// </summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? StandardCrewSize { get; set; }
```

字段口径：

```text
SetupHour：固定段取时间，一张工单/一道工序计一次，不随数量放大。
CycleTime：单件加工时间，随数量线性放大。
StandardCrewSize：标准人数，可支持 1、2、1.5 等小数人数。
```

公式：

```text
StandardMachineHour = (SetupHour ?? 0) + CalcQty × (CycleTime ?? 0)
StandardLaborHour   = StandardMachineHour × (StandardCrewSize ?? 1)
```

`LeadTime` 与 `SetupHour/CycleTime` 正交：

```text
LeadTime：供 MRP 反推日期 / 交期。
SetupHour/CycleTime：供成本 / CRP 负荷。
```

---

### 3.2 新建 `WorkCenter`

位置：

```text
CP6.Entity.DomainModels.Mes.WorkCenter
表：T_WorkCenter
继承：BaseBizEntity
```

实体：

```csharp
[Table("T_WorkCenter")]
public class WorkCenter : BaseBizEntity
{
    /// <summary>
    /// 工作中心CD（业务键，唯一；= ProductProcess.WgCd / WorkOrderProcess.WgCd）。
    /// </summary>
    [Required, MaxLength(10)]
    public string WgCd { get; set; } = string.Empty;

    /// <summary>工作中心名称。</summary>
    [MaxLength(100)]
    public string? WgName { get; set; }

    /// <summary>
    /// 日可用产能（h/日）——CRP 入参地基。A2 只维护，不消费。
    /// </summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? DailyCapacityHours { get; set; }

    /// <summary>启用。</summary>
    public bool Enable { get; set; } = true;
}
```

索引：

```text
UX_Mes_WorkCenter_Wg：唯一(WgCd)
多租户环境下按现有框架自动补 TenantId 前缀。
```

校验：

```text
WgCd 必填，不超过 10。
WgCd 唯一。
DailyCapacityHours 为空或 >= 0。
Enable=false 的工作中心不允许新增费率；已有历史费率可保留供历史成本查询。
```

---

### 3.3 新建 `ProcessCostRate`

位置：

```text
CP6.Entity.DomainModels.Mes.ProcessCostRate
表：T_ProcessCostRate
继承：BaseBizEntity
```

实体：

```csharp
[Table("T_ProcessCostRate")]
public class ProcessCostRate : BaseBizEntity
{
    /// <summary>工作中心CD（业务键 → WorkCenter.WgCd）。</summary>
    [Required, MaxLength(10)]
    public string WgCd { get; set; } = string.Empty;

    /// <summary>人工费率（元/h）。</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal LaborRate { get; set; }

    /// <summary>制造费率（元/h）。</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal OverheadRate { get; set; }

    /// <summary>生效日。Resolve 时取 ≤ 基准日的最新有效版本。</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>失效日。null = 长期有效。</summary>
    public DateTime? ValidTo { get; set; }
}
```

索引：

```text
IX_Mes_ProcessCostRate_Wg_ValidFrom：(WgCd, ValidFrom)
UX_Mes_ProcessCostRate_Wg_ValidFrom：唯一(WgCd, ValidFrom)
```

校验规则：

```text
1. WgCd 必须存在于 WorkCenter。
2. LaborRate >= 0。
3. OverheadRate >= 0。
4. ValidTo 为空，或 ValidTo >= ValidFrom。
5. 同一 WgCd 下有效期间不得重叠。
6. 同一 WgCd + ValidFrom 不得重复。
```

期间重叠判定：

```text
新期间：[newFrom, newTo ?? DateTime.MaxValue]
旧期间：[oldFrom, oldTo ?? DateTime.MaxValue]
重叠条件：newFrom <= oldTo && oldFrom <= newTo
```

Resolve 口径：

```text
Where WgCd == wgCd
  and !IsDeleted
  and ValidFrom <= onDate
  and (ValidTo == null || ValidTo >= onDate)
OrderByDescending(ValidFrom)
FirstOrDefault()
```

---

### 3.4 扩展 `WorkOrderProcess`

位置：

```text
CP6.Entity.DomainModels.Mes.WorkOrderProcess
表：T_WorkOrderProcess
```

现有相关字段不改：

```text
WorkOrderNo / ProcessCd / TaskCd / WgCd / ProcessStatus / PlanQty / GoodQty / DefectQty / StdLossRate / LeadTime / ActualStartTime / ActualEndTime
```

新增字段：

```csharp
/// <summary>
/// 实际机时（h）。用于制造费用。
/// 由 ProductionResult 按机器时间区间合并后派生，可手工覆盖。
/// </summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? ActualMachineHour { get; set; }

/// <summary>
/// 实际人工工时（h）。用于直接人工。
/// 由 ProductionResult 按作业者时间累加后派生，可手工覆盖。
/// </summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? ActualLaborHour { get; set; }

/// <summary>
/// 工时来源：Derived / Manual / Import / StandardFallback / LegacyFallback。
/// </summary>
[MaxLength(30)]
public string? ActualHourSource { get; set; }

/// <summary>是否人工覆盖。</summary>
public bool IsHourOverridden { get; set; } = false;

/// <summary>工时覆盖/回退/异常说明。</summary>
[MaxLength(500)]
public string? HourRemark { get; set; }

/// <summary>工时最近计算时间。</summary>
public DateTime? HourCalculatedTime { get; set; }
```

兼容说明：

```text
v2 不再推荐单字段 ActualWorkingHour 作为主计算字段。
若迁移前已加 ActualWorkingHour，可保留为兼容字段，但 CostCollect 不再以它为主；迁移时可初始化：
ActualMachineHour = ActualWorkingHour
ActualLaborHour   = ActualWorkingHour
ActualHourSource  = 'LegacyMigrated'
```

---

### 3.5 扩展 `ProductionResult`

位置：

```text
CP6.Entity.DomainModels.Mes.ProductionResult
表：T_ProductionResult
```

现有相关字段：

```text
ResultType：1开始 / 2中断 / 3中断解除 / 4完了 / 5数量報告
ActualStartTime / ActualEndTime / OperatorCd / MachineCd
```

新增字段：

```csharp
/// <summary>
/// 本次报工人工工时（h，可选）。填写则本行人工工时优先使用该值。
/// </summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? LaborHour { get; set; }

/// <summary>
/// 本次报工机时（h，可选）。填写则本行机时优先使用该值。
/// </summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? MachineHour { get; set; }
```

使用规则：

```text
1. 行有 LaborHour：人工工时使用 LaborHour，不再用该行时刻差推人工工时。
2. 行有 MachineHour：机时使用 MachineHour，不再用该行时刻差推该行机时。
3. 未填显式工时：按 ActualStartTime / ActualEndTime 派生。
4. 若显式工时与时刻同时存在，显式工时优先，时刻保留作审计参考。
```

---

### 3.6 扩展 `CostSheet`

位置：

```text
CP6.Entity.DomainModels.Fin.CostSheet
```

现状：`MaterialActual` / `MaterialStandard` / `LaborStd` / `OverheadStd`。

v2 改造：把 Labor / Overhead 从单值升级为实际/标准双值。

```csharp
[Column(TypeName = "decimal(18,2)")]
public decimal LaborActual { get; set; }

[Column(TypeName = "decimal(18,2)")]
public decimal LaborStandard { get; set; }

[Column(TypeName = "decimal(18,2)")]
public decimal OverheadActual { get; set; }

[Column(TypeName = "decimal(18,2)")]
public decimal OverheadStandard { get; set; }
```

NotMapped 重算：

```csharp
public decimal TotalActual => MaterialActual + LaborActual + OverheadActual;
public decimal StandardCost => MaterialStandard + LaborStandard + OverheadStandard;
public decimal Variance => TotalActual - StandardCost;
public decimal FgUnitCost => CompletedQty > 0 ? TotalActual / CompletedQty : 0;
```

迁移兼容：

```text
旧 LaborStd      → LaborStandard，且 LaborActual = LaborStd
旧 OverheadStd   → OverheadStandard，且 OverheadActual = OverheadStd
历史成本单总额不变。
```

---

### 3.7 扩展 `CostSheetLine`

位置：

```text
CP6.Entity.DomainModels.Fin.CostSheetLine
```

新增字段：

```csharp
/// <summary>工时（h）。Labor 行表示人工工时；Overhead 行表示机时；Material 行为空。</summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? Hours { get; set; }

/// <summary>标准工时（h）。Labor 行表示标准人工工时；Overhead 行表示标准机时。</summary>
[Column(TypeName = "decimal(21,8)")]
public decimal? StandardHours { get; set; }

/// <summary>工序CD。Material 行可为空。</summary>
[MaxLength(50)]
public string? ProcessCd { get; set; }

/// <summary>任务/工序明细CD。</summary>
[MaxLength(50)]
public string? TaskCd { get; set; }

/// <summary>工作中心CD。</summary>
[MaxLength(10)]
public string? WgCd { get; set; }

/// <summary>费率生效日，用于追溯使用了哪版费率。</summary>
public DateTime? RateValidFrom { get; set; }

/// <summary>小时来源：Derived / Manual / Import / StandardFallback / LegacyFallback。</summary>
[MaxLength(30)]
public string? HourSource { get; set; }

/// <summary>计算说明 / 回退说明 / Warning 说明。</summary>
[MaxLength(500)]
public string? CalcNote { get; set; }

/// <summary>Warning 码。无警告为空。</summary>
[MaxLength(50)]
public string? WarningCode { get; set; }
```

字段复用：

```text
UnitPrice：Labor 行承载 LaborRate；Overhead 行承载 OverheadRate。
ActualAmount：实际金额。
StandardAmount：标准金额。
ActualAmount - StandardAmount：金额差异。
Hours - StandardHours：工时差异。
```

---

## 四、服务层（签名 + 公式）

### 4.1 `IWorkCenterService` / `WorkCenterService`

位置：

```text
CP6.Core.Services.Mes
```

接口：

```csharp
public interface IWorkCenterService
{
    Task<List<WorkCenter>> ListAsync(string? keyword);
    Task<WorkCenter?> GetAsync(string wgCd);
    Task UpsertAsync(WorkCenter dto, string? user);
    Task DeleteAsync(string wgCd, string? user);
}
```

校验：

```text
Upsert：WgCd 必填、唯一；DailyCapacityHours >= 0。
Delete：软删；若已有 ProductProcess / WorkOrderProcess 引用，允许软删但 Enable=false，历史数据仍可查。
```

---

### 4.2 `IProcessCostRateService` / `ProcessCostRateService`

位置：

```text
CP6.Core.Services.Mes
```

接口：

```csharp
public interface IProcessCostRateService
{
    Task<List<ProcessCostRate>> ListAsync(string? wgCd);
    Task<ProcessCostRate?> ResolveAsync(string wgCd, DateTime onDate);
    Task UpsertAsync(ProcessCostRate dto, string? user);
    Task DeleteAsync(Guid id, string? user);
}
```

`ResolveAsync`：

```csharp
return await db.ProcessCostRates
    .Where(x => !x.IsDeleted)
    .Where(x => x.WgCd == wgCd)
    .Where(x => x.ValidFrom <= onDate)
    .Where(x => x.ValidTo == null || x.ValidTo >= onDate)
    .OrderByDescending(x => x.ValidFrom)
    .FirstOrDefaultAsync();
```

`UpsertAsync` 必做校验：

```text
1. 工作中心存在。
2. LaborRate / OverheadRate 非负。
3. ValidTo >= ValidFrom。
4. 同一 WgCd 期间不重叠。
5. 同一 WgCd + ValidFrom 不重复。
```

---

### 4.3 扩展 `ProductionResultService`

目标：报工落库后，自动重算并物化工序实绩小时到 `WorkOrderProcess`。

新增内部方法建议：

```csharp
Task RecalculateProcessHoursAsync(
    string workOrderNo,
    string processCd,
    string? taskCd,
    string? user);
```

#### 4.3.1 实际机时派生规则

机时用于制造费用。

优先级：

```text
1. 如果 WorkOrderProcess 被人工覆盖：不自动重算，保留 Manual。
2. 如果 ProductionResult 行有 MachineHour：该行机时使用 MachineHour。
3. 否则使用 ActualStartTime / ActualEndTime 形成机器运行区间。
4. 同一 WorkOrderNo + ProcessCd + TaskCd + MachineCd 下，机器区间先合并再求和，避免多作业者重复累计机时。
5. 中断区间从机器运行区间中扣除。
```

机器区间合并示例：

```text
张三 08:00-10:00 Machine=M1
李四 08:00-10:00 Machine=M1

ActualMachineHour = 2 小时，不是 4 小时。
```

#### 4.3.2 实际人工工时派生规则

人工工时用于直接人工。

优先级：

```text
1. 如果 WorkOrderProcess 被人工覆盖：不自动重算，保留 Manual。
2. 如果 ProductionResult 行有 LaborHour：该行人工工时使用 LaborHour。
3. 否则使用 ActualStartTime / ActualEndTime 按作业者累加。
4. 同一 WorkOrderNo + ProcessCd + TaskCd + OperatorCd 下，人工区间按作业者累加。
5. 多个作业者同时作业，要重复累加人工工时。
6. 中断区间从对应作业者时间中扣除。
```

人工工时示例：

```text
张三 08:00-10:00
李四 08:00-10:00

ActualLaborHour = 4 小时。
```

#### 4.3.3 中断区间

ResultType 口径：

```text
2 = 中断
3 = 中断解除
```

规则：

```text
1. 中断必须有成对的中断开始 / 中断解除。
2. 中断区间只扣除与其时间重叠的运行区间。
3. 未闭合中断不参与扣减，并产生 Warning：W-A2-HOUR-003。
4. 中断结束早于中断开始，忽略并产生 Warning：W-A2-HOUR-002。
```

#### 4.3.4 物化结果

写回：

```csharp
workOrderProcess.ActualMachineHour = machineHours;
workOrderProcess.ActualLaborHour = laborHours;
workOrderProcess.ActualHourSource = "Derived";
workOrderProcess.IsHourOverridden = false;
workOrderProcess.HourCalculatedTime = now;
```

如果手工覆盖：

```csharp
workOrderProcess.ActualMachineHour = manualMachineHour;
workOrderProcess.ActualLaborHour = manualLaborHour;
workOrderProcess.ActualHourSource = "Manual";
workOrderProcess.IsHourOverridden = true;
workOrderProcess.HourRemark = reason;
```

---

### 4.4 改造 `CostCollectService.CollectAsync`

位置：

```text
CP6.Core.Services.Fin
```

签名保持兼容：

```csharp
Task<FinResult> CollectAsync(
    string workOrderNo,
    decimal laborStd,
    decimal overheadStd,
    string user);
```

说明：

```text
laborStd / overheadStd 不再作为主计算来源，只作为迁移期 LegacyFallback。
```

#### 4.4.1 取数

1. 取工单 `WorkOrder`。
2. 取工单工序 `WorkOrderProcess where WorkOrderNo == wo && !IsDeleted`。
3. 按 `ProductCd + ProcessCd + TaskCd` 关联 `ProductProcess`。
4. 按 `WorkOrderProcess.WgCd` + 成本基准日 Resolve `ProcessCostRate`。

成本基准日：

```text
CostBaseDate = WorkOrder.ActualEndTime
            ?? WorkOrder.PlanStartDate
            ?? WorkOrder.CreateDate
            ?? Today
```

计算数量：

```text
CalcQty = WorkOrder.CompletedQty
若 CompletedQty 为空或 0，可回退 GoodQty 汇总；仍为 0 时标准变动工时为 0，段取仍可计。
```

#### 4.4.2 标准工时

逐工序：

```text
StandardMachineHour = (SetupHour ?? 0) + CalcQty × (CycleTime ?? 0)
StandardLaborHour   = StandardMachineHour × (StandardCrewSize ?? 1)
```

#### 4.4.3 实际工时

逐工序优先级：

```text
ActualMachineHour = WorkOrderProcess.ActualMachineHour
                  ?? StandardMachineHour   // 工时缺失时按标准机时回退

ActualLaborHour   = WorkOrderProcess.ActualLaborHour
                  ?? StandardLaborHour     // 工时缺失时按标准人工工时回退
```

如果使用标准工时回退：

```text
HourSource = StandardFallback
WarningCode = W-A2-COST-001
CalcNote = 实绩工时缺失，按标准工时作为实际工时计算。
```

#### 4.4.4 金额计算

逐工序落两行：Labor / Overhead。

Labor 行：

```text
Hours          = ActualLaborHour
StandardHours  = StandardLaborHour
UnitPrice      = LaborRate
ActualAmount   = ActualLaborHour × LaborRate
StandardAmount = StandardLaborHour × LaborRate
VarianceAmount = ActualAmount − StandardAmount
```

Overhead 行：

```text
Hours          = ActualMachineHour
StandardHours  = StandardMachineHour
UnitPrice      = OverheadRate
ActualAmount   = ActualMachineHour × OverheadRate
StandardAmount = StandardMachineHour × OverheadRate
VarianceAmount = ActualAmount − StandardAmount
```

汇总写入 `CostSheet`：

```text
LaborActual       = Σ Labor 行 ActualAmount
LaborStandard     = Σ Labor 行 StandardAmount
OverheadActual    = Σ Overhead 行 ActualAmount
OverheadStandard  = Σ Overhead 行 StandardAmount
```

#### 4.4.5 费率缺失处理

费率缺失属于主数据不完整。A2 提供两种模式：

```text
StrictCostRate = true：严格模式，缺费率则成本归集失败，返回 E-A2-RATE-002。
StrictCostRate = false：迁移模式，缺费率不阻断，使用 legacy estimate 回退并写 Warning。
```

建议默认：

```text
开发/TDD：StrictCostRate = true
生产迁移初期：StrictCostRate = false
迁移完成后：切回 true
```

迁移模式回退规则：

```text
1. 如果任一必需工序缺费率，不混用部分真实成本与部分估算成本。
2. LaborActual = LaborStandard = laborStd。
3. OverheadActual = OverheadStandard = overheadStd。
4. 写入两条 LegacyFallback 行，标注 WarningCode = W-A2-COST-002。
5. CostSheet.CalcNote / CostSheetLine.CalcNote 说明缺哪个 WgCd 的费率。
```

不混算原因：

```text
laborStd / overheadStd 是工单级估算，不具备准确工序分摊依据。
混合“部分真实 + 整单估算”容易重复计成本。
```

#### 4.4.6 无工序数据处理

```text
无 WorkOrderProcess：迁移模式下回退 legacy estimate；严格模式下返回 E-A2-COST-001。
有 WorkOrderProcess 但无 ProductProcess：返回 E-A2-COST-004 或按配置回退。
```

#### 4.4.7 材料逻辑

材料不动：

```text
MaterialActual = WorkOrderMaterial.ActualQty × ProductMaterial.SupplyPrice
MaterialStandard = 标准/计划用量 × ProductMaterial.SupplyPrice
MaterialVariance = MaterialActual − MaterialStandard
```

---

### 4.5 微调 `CostSettleService.SettleAsync`

位置：

```text
CP6.Core.Services.Fin
```

贷方金额改为实际额：

```text
DIRECT_LABOR = sheet.LaborActual
MFG_OVERHEAD = sheet.OverheadActual
```

总成本：

```text
TotalActual = MaterialActual + LaborActual + OverheadActual
```

GL 规则保持实际成本法：

```text
借：WIP / 生产成本-在制品
贷：INVENTORY / 原材料库存
贷：DIRECT_LABOR / 直接人工
贷：MFG_OVERHEAD / 制造费用

借：FG / 产成品库存
贷：WIP / 生产成本-在制品
```

不新增：

```text
人工效率差异科目
制造费用效率差异科目
标准成本差异科目
```

---

## 五、API + 前端

### 5.1 控制器

位置：

```text
CP6.WebApi/Controllers/Mes
```

新增：

```text
WorkCenterController：/api/mes/work-center
ProcessCostRateController：/api/mes/process-cost-rate
```

接口：

```text
GET    /api/mes/work-center?keyword=
GET    /api/mes/work-center/{wgCd}
POST   /api/mes/work-center/upsert
DELETE /api/mes/work-center/{wgCd}

GET    /api/mes/process-cost-rate?wgCd=
GET    /api/mes/process-cost-rate/resolve?wgCd=&onDate=
POST   /api/mes/process-cost-rate/upsert
DELETE /api/mes/process-cost-rate/{id}
```

返回包：

```json
{ "code": 0, "message": "success", "data": {} }
```

权限：

```text
[Authorize]
资源键随 MES 主数据菜单。
```

---

### 5.2 前端页面

新增页面：

```text
cp6.web/src/views/mes/WorkCenterView.vue
cp6.web/src/views/mes/ProcessCostRateView.vue
```

改造页面：

```text
erp/ProductMasterView：製品工程页增加 SetupHour / CycleTime / StandardCrewSize。
fin/CostSheetView：成本单增加工/费实际、标准、差异；工序维度展开；显示工时来源与 Warning。
```

菜单：

```text
MES
 ├─ 工作中心
 └─ 工序费率
```

i18n：

```text
中文 / 日文 / 英文 / 其他既有语言包同步新增字段名、按钮、错误码、Warning 文案。
```

---

## 六、错误码 / Warning 码

### 6.1 Error 码

| 错误码 | 场景 | 处理 |
|---|---|---|
| `E-A2-WC-001` | 工作中心不存在 | 阻断保存费率 / 严格模式下阻断成本归集 |
| `E-A2-WC-002` | 工作中心编码重复 | 阻断保存 |
| `E-A2-WC-003` | 日可用产能小于 0 | 阻断保存 |
| `E-A2-RATE-001` | 费率生效区间重叠 | 阻断保存 |
| `E-A2-RATE-002` | 成本基准日找不到费率 | 严格模式阻断成本归集 |
| `E-A2-RATE-003` | LaborRate / OverheadRate 小于 0 | 阻断保存 |
| `E-A2-RATE-004` | ValidTo 早于 ValidFrom | 阻断保存 |
| `E-A2-HOUR-001` | 报工起止时间不完整 | 不参与派生，记录 warning |
| `E-A2-HOUR-002` | 结束时间早于开始时间 | 不参与派生，记录 warning |
| `E-A2-COST-001` | 工单无工序数据 | 严格模式阻断；迁移模式回退 |
| `E-A2-COST-004` | 工单工序找不到对应 ProductProcess | 严格模式阻断；迁移模式回退 |
| `E-A2-COST-005` | 成本单 CompletedQty/CalcQty 无法确定 | 阻断或按 0 处理，取决于现有约定 |

### 6.2 Warning 码

| Warning 码 | 场景 | 处理 |
|---|---|---|
| `W-A2-HOUR-001` | 报工起止时间不完整 | 跳过该行派生 |
| `W-A2-HOUR-002` | 报工结束早于开始 | 跳过该行派生 |
| `W-A2-HOUR-003` | 中断区间未闭合 | 不扣减该中断区间 |
| `W-A2-COST-001` | 实绩工时缺失 | 使用标准工时作为实际工时 |
| `W-A2-COST-002` | 迁移模式下缺费率 | 使用 legacy estimate 回退 |
| `W-A2-COST-003` | CompletedQty 为 0 | 变动工时为 0，段取仍计；单位成本按现有规则处理 |
| `W-A2-COST-004` | StandardCrewSize 为空 | 按 1 处理 |
| `W-A2-COST-005` | ProductProcess 缺 SetupHour/CycleTime | 缺失项按 0 处理 |

---

## 七、迁移 / 多租户 / 权限

### 7.1 迁移名称

```text
*_A2ProcessRoutingCostTruthV2
```

### 7.2 迁移内容

```text
ProductProcess +3：SetupHour / CycleTime / StandardCrewSize
WorkCenter：新表
ProcessCostRate：新表
WorkOrderProcess +6：ActualMachineHour / ActualLaborHour / ActualHourSource / IsHourOverridden / HourRemark / HourCalculatedTime
ProductionResult +2：LaborHour / MachineHour
CostSheet：新增 LaborActual / LaborStandard / OverheadActual / OverheadStandard
CostSheetLine +9：Hours / StandardHours / ProcessCd / TaskCd / WgCd / RateValidFrom / HourSource / CalcNote / WarningCode
```

### 7.3 历史数据回填

```text
LaborActual      = 旧 LaborStd
LaborStandard    = 旧 LaborStd
OverheadActual   = 旧 OverheadStd
OverheadStandard = 旧 OverheadStd
```

若历史库已有 `ActualWorkingHour`：

```text
ActualMachineHour = ActualWorkingHour
ActualLaborHour   = ActualWorkingHour
ActualHourSource  = 'LegacyMigrated'
```

### 7.4 多租户

```text
新实体继承 BaseBizEntity。
唯一索引按现有多租户机制自动补 TenantId。
跨租户不得 Resolve 费率。
```

### 7.5 权限

```text
工作中心：MES.WorkCenter.View / MES.WorkCenter.Edit / MES.WorkCenter.Delete
工序费率：MES.ProcessCostRate.View / MES.ProcessCostRate.Edit / MES.ProcessCostRate.Delete
成本单展示：沿用 Fin.CostSheet 权限。
```

---

## 八、测试（TDD）

| 测试 | 断言 |
|---|---|
| `WorkCenterService_Upsert_CreatesAndUpdates` | WgCd upsert；DailyCapacityHours 非负；软删可用 |
| `WorkCenterService_DuplicateWgCd_Blocked` | 同租户 WgCd 唯一 |
| `ProcessCostRate_Resolve_TakesLatestEffective` | 多版本费率取 ≤ 基准日最新 |
| `ProcessCostRate_Resolve_ExpiredNotTaken` | ValidTo 早于基准日不取 |
| `ProcessCostRate_Overlap_Blocked` | 同 WgCd 生效区间重叠阻断 |
| `ProcessCostRate_NegativeRate_Blocked` | LaborRate / OverheadRate 负数阻断 |
| `ProductionResult_AggregatesMachineHour_MergeIntervals` | 同机器同区间多作业者不重复算机时 |
| `ProductionResult_AggregatesLaborHour_ByOperator` | 多作业者同时作业重复累加人工工时 |
| `ProductionResult_ExplicitLaborHour_OverridesTime` | LaborHour 有值时优先于时刻差 |
| `ProductionResult_ExplicitMachineHour_OverridesTime` | MachineHour 有值时优先于时刻差 |
| `ProductionResult_Interrupt_DeductsClosedInterval` | 成对中断区间扣减 |
| `ProductionResult_UnclosedInterrupt_WarningOnly` | 未闭合中断产生 warning，不扣减 |
| `CostCollect_StandardMachineHour_SetupPlusQtyTimesCycle` | 标准机时 = Setup + Qty×Cycle |
| `CostCollect_StandardLaborHour_MultipliesCrewSize` | 标准人工工时 = 标准机时×标准人数 |
| `CostCollect_Labor_UsesActualLaborHourTimesLaborRate` | 人工实际额 = 实际人工工时×人工率 |
| `CostCollect_Overhead_UsesActualMachineHourTimesOverheadRate` | 制造费实际额 = 实际机时×制造费率 |
| `CostCollect_MissingActualHour_UsesStandardFallback` | 实绩工时缺失时按标准工时回退并写 warning |
| `CostCollect_MissingRate_StrictMode_Blocks` | 严格模式缺费率返回 E-A2-RATE-002 |
| `CostCollect_MissingRate_MigrationMode_LegacyFallback` | 迁移模式缺费率回退 laborStd/overheadStd，并写 LegacyFallback 行 |
| `CostCollect_CostSheetLine_HasTraceFields` | Labor/Overhead 行写 ProcessCd/TaskCd/WgCd/RateValidFrom/HourSource |
| `CostSettle_CreditsActualLaborOverhead` | 结转贷 DIRECT_LABOR/MFG_OVERHEAD 使用实际额 |
| `CostSettle_DoesNotPostVariance` | 差异不生成 GL 分录 |

### gstack 真浏览器 QA

路径：

```text
1. 新建 WorkCenter：PRINT，DailyCapacityHours=16。
2. 新建 ProcessCostRate：PRINT，ValidFrom=2026-01-01，LaborRate=80，OverheadRate=120。
3. ProductProcess 填：SetupHour=0.5，CycleTime=0.002，StandardCrewSize=2。
4. 工单完工数量 1000。
5. 报工：同一机器 M1，两个作业者 08:00-10:00。
6. ProductionResultService 派生：ActualMachineHour=2，ActualLaborHour=4。
7. 成本归集：
   StandardMachineHour = 0.5 + 1000×0.002 = 2.5
   StandardLaborHour = 2.5×2 = 5
   LaborActual = 4×80 = 320
   LaborStandard = 5×80 = 400
   OverheadActual = 2×120 = 240
   OverheadStandard = 2.5×120 = 300
8. CostSheet 显示 Labor/Overhead 实际、标准、差异。
9. 结转：DIRECT_LABOR=320，MFG_OVERHEAD=240，差异不入 GL。
```

---

## 九、Self-Review（对照覆盖）

- **A2-D1**：`ProductProcess.SetupHour/CycleTime/StandardCrewSize` ✅；标准机时/标准人工工时公式 ✅
- **A2-D2**：`WorkCenter` + `ProcessCostRate` 双率 ✅；费率生效区间校验 ✅；Resolve 口径 ✅
- **A2-D3**：`WorkOrderProcess.ActualMachineHour/ActualLaborHour` ✅；`ProductionResult.LaborHour/MachineHour` 覆盖 ✅；机器区间合并/作业者累加 ✅
- **A2-D4**：`CostSheet` 实际/标准四字段 ✅；`CostCollect` 工时×费率 ✅；`CostSettle` 贷实际额 ✅；差异不入 GL ✅
- **A2-D5**：`WorkCenter.DailyCapacityHours` ✅；不建 CRP 引擎 ✅
- **追溯能力**：`CostSheetLine.ProcessCd/TaskCd/WgCd/RateValidFrom/HourSource/CalcNote/WarningCode` ✅
- **迁移稳定**：Strict / Migration fallback 两模式 ✅；历史 LaborStd / OverheadStd 等价回填 ✅
- **测试完整性**：服务、公式、回退、结转、GL 非差异入账均覆盖 ✅

---

## 十、已知推迟

```text
1. CRP 负荷引擎：留 MRP P4，消费 ProductProcess 标准工时 + WorkCenter.DailyCapacityHours。
2. 标准成本法差异入账：与现实际成本模型不一致，A2 不做。
3. 月末制造费用实际发生额分摊：A2 仍按工作中心费率吸收，不做月末差异分摊。
4. 费率审批流：后续如需可加。
5. 机台级费率：A2 工作中心级，Machine 只作为执行资源；机台级成本后续扩展。
6. 外协工序成本：保留 ProductProcess.PurchasePrice 现状，A2 不重构外协成本要素。
```

---

## 十一、最终一句话

> A2 v2 的落点是：**用 ProductProcess 的标准机时/人工工时打底，用 ProductionResult 派生实际机时/人工工时，用 WorkCenter 费率算真实 Labor/Overhead，结转仍走实际成本法，差异只展示不入 GL，同时给 MRP P4 留好 CRP 产能地基。**

---

*修订于 2026-06-17。源：A2-D1~D5 brainstorming + 原始 A2 spec + 修订补强项。下一步：writing-plans。*
