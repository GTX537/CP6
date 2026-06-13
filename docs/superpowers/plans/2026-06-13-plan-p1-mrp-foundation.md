# 计划中台 P1 · MRP 净需求地基 Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**计划中台第一份（P1 地基）**。源 spec：`docs/superpowers/specs/2026-06-13-mrp-planning-suite-design.md`（决策 D1-D11）。P2 预测 / P3 MPS / P4 CRP 为后续计划。

**Goal:** 落地 MRP 净需求地基——抽取共享用量内核 `IMaterialUsageCalculator`（解纸箱"尺寸料面积驱动单耗"）+ `ProductMaterial` 补单耗 + 计划主数据 `Plan_ItemPlanningPolicy` + **MRP 低层码逐层净需求引擎**（每 Item×日桶只 net 一次、净需求含已确认计划订单供给、复算存活规则、批量/提前期、计划订单+Pegging）+ 计划看板 + 人确认转 采购PR/MES工单。完成后：一张受注 → 算出原纸/辅料净需求（跨产品合并）、自制半成品转下层 → 输出建议 → 人确认转单。

**Architecture:** 新建 `Plan` 命名空间（`CP6.Entity/DomainModels/Plan`、`CP6.Core/Services/Plan`、`Controllers/Plan`、`views/plan`）。用量内核**向下沉** `CP6.Core/Services/Common/IMaterialUsageCalculator`（見積与 MRP 共消费）。MRP = 低层码（low-level code）逐层扫描：展开父件只往下层累加器加数，扫到该层、需求汇齐才 `net` 一次（防共用原纸重复 netting）。净需求 = 毛需求 − 库存 − 在途 − 在制 − **已确认计划订单(scheduled receipt)** − 安全库存。计划只算与建议，不持业务真相；转单后归下游（采购/MES）。

**Tech Stack:** .NET 8 + EF Core 8 / xUnit + EF Core InMemory / Vue 3.5 + element-plus。复用：受注 `Order`、`ProductProcess`/`ProductMaterial`(BOM+路线)、WMS `Stock`/`InboundOrder`/`WorkOrder`、`EstimateCalcService`(用量算法源)、采购 `PrGenerationService`(已规划)、MES `WorkOrderService`。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 现状/对账 | **本稿建议值** |
|---|---|---|---|
| **MP-D1** | **用量内核入参** | `EstimateCalcService.CalculateAsync` 用 dto(SheetDimW/F/SheetFlute) 算；面积×段成率(M067)是成本子计算 | `IMaterialUsageCalculator.CalcUsage` 入参为**原始规格参数**（sheetDimW/F、yieldRate 或 sheetFlute、outputQty），返回**用量量**（非成本）。見積从 dto 喂、MRP 从产品主数据(ProductProcess/Material 规格 Spec)喂。两者共用核心公式 |
| **MP-D2** | **MRP 用量取规格** | ✅**已确认（2026-06-13 勘察，风险消除）** | `ProductMaster` 全套现成：`SheetDimW/SheetDimF`(展开尺寸,156/157)+`SheetFlute`(段,132)+`PaperCdF/C/B`(三层原纸,134/139/143)+刃幅/罫線/糊代(152-155)；`OrderDetail` 同带全套(SheetDimW/F+SheetFlute+PaperCdF/C/B)——**受注驱动 MRP 入参现成**。段成率=`M_GenericCode(M067, SheetFlute).Num1`(見積已用)。**无需补产品规格字段**，A-1 Step0 风险消除 |
| **MP-D6** | **原纸层级展开** | 纸箱=F面/C中芯/B里 三层瓦楞板；現有見積按 F+复合段成率简化 | **按 `ProductMaterial`(MaterialTypeDiv=4 印刷原紙) 实际原纸行展开**——有几条算几条(F/C/B 三条→三条原纸净需求/单层→一条)，每条用量经用量内核(面积×段成率)。v1 各层用同一 flute 段成率(沿見積口径)；**per-层差异系数(中芯C波纹取り都)作 refinement**(可用 `ProductMaterial.UnitUsage` 当该行系数乘子) |
| **MP-D3** | **TenantId/审计/基类** | 零多租户；Erp 实体继承 `BaseBizEntity`(IsDeleted+RowVersion) | Plan 实体继承 `BaseBizEntity`（与 Erp/Mes 一致）；TenantId 延 OA 章10 系统级多租户 |
| **MP-D4** | **转单对端** | 采购 `PrGenerationService`(已规划未编码)、MES `WorkOrderService`(已有) | 转单接口 P1 **立契约 + 桩/适配**：采购 PR 走 `IPlanToPrService`(采购实现，桩)、生产工单走 MES `WorkOrderService`(已有，直接调或薄适配)。计划编译期不被采购未编码绑死 |
| **MP-D5** | **复算策略** | spec：regenerative 全重算 | P1 = **regenerative**（每次 run 全量重算，未确认建议作废重生）；net-change 增量复算后续 |

> **测试基建**：xUnit + InMemory。低层码排序/共用料 net-once/净需求供给抵扣/复算存活/批量规则/提前期反推/成环检测/用量内核**回归(見積金额改造前后一致)** 全可纯单测——这是 P1 的核心验证。

---

## File Structure

### 用量内核（`CP6.Core/Services/Common`，向下沉）
- `IMaterialUsageCalculator.cs` / `MaterialUsageCalculator.cs`（CalcUsage：尺寸料面积×段成率、定额料 UnitUsage×qty）
- 修改 `CP6.Core/Services/Erp/EstimateCalcService.cs`（CalculateAsync 改调用内核，行为不变）

### 扩展现有实体
- 修改 `CP6.Entity/DomainModels/Erp/ProductMaterial.cs`（+`UsageType`/`UnitUsage`/`UsageUnit`）

### 计划主数据 + MRP 实体（`CP6.Entity/DomainModels/Plan`）
- `Plan_ItemPlanningPolicy.cs`（安全库存/采购提前期/批量规则）
- `Plan_MrpRun.cs`、`Plan_PlannedOrder.cs`、`Plan_Pegging.cs`、`Plan_NetRequirement.cs`

### 服务（`CP6.Core/Services/Plan`）
- `IItemPlanningPolicyService.cs`/`...Service.cs`（主数据 CRUD + 提前期汇总）
- `ILowLevelCodeService.cs`/`LowLevelCodeService.cs`（低层码计算 + 成环检测）
- `ISupplyService.cs`/`SupplyService.cs`（汇总各供给：库存/在途/在制/已确认计划订单）
- `IMrpEngine.cs`/`MrpEngine.cs`（★低层码逐层净需求 + 计划订单 + Pegging）
- `IPlanConvertService.cs`/`PlanConvertService.cs`（人确认转单：PR/工单）
- `Contracts/IPlanToPrService.cs`+桩（采购实现）

### 控制器 + DI + 迁移 + 前端 + 测试
- `Controllers/Plan/{MrpController,ItemPlanningPolicyController}.cs`
- 迁移 `*_PlanP1Mrp`；`cp6.web/src/views/plan/{MrpBoardView,ItemPolicyView}.vue`
- 测试：`MaterialUsageCalculatorTests`/`EstimateCalcRegressionTests`、`LowLevelCodeServiceTests`、`MrpEngineTests`（★核心）、`SupplyServiceTests`

---

## 实施分四阶段

- **Phase A**（A-1..A-2）：用量内核抽取 + 見積回归（★解单耗难点，先做不破坏現有）
- **Phase B**（B-1..B-3）：扩展单耗 + 计划主数据 + 低层码计算
- **Phase C**（C-1..C-4）：MRP 引擎（实体 + 供给汇总 + 低层码逐层净需求 + 复算存活）★
- **Phase D**（D-1..D-2）：计划看板 + 人确认转单

---

# Phase A — 共享用量内核（抽取 + 見積回归）

## Task A-1: 摸清产品规格落点 + IMaterialUsageCalculator（spec §4，MP-D1/D2）★

**Files:** Create `CP6.Core/Services/Common/IMaterialUsageCalculator.cs`/`MaterialUsageCalculator.cs`; Test `CP6.Tests/MaterialUsageCalculatorTests.cs`

- [ ] **Step 0: ✅已勘察确认（MP-D2/MP-D6，无前置缺口）** 规格落点已确认：`ProductMaster.{SheetDimW,SheetDimF,SheetFlute,PaperCdF/C/B}` + `OrderDetail` 同带全套，段成率=`M067 by SheetFlute`，**无需补字段**。原纸需求按 `ProductMaterial`(MaterialTypeDiv=4 印刷原紙) 实际行展开（有几层算几层，MP-D6）。本任务直接做内核，入参 = 規格(尺寸/段成率/数量)。
- [ ] **Step 1: 失败测试**（尺寸料：用量 = 面积(W×F/1e6) × 段成率 × outputQty；定额料：UnitUsage × outputQty）

```csharp
public class MaterialUsageCalculatorTests
{
    [Fact]
    public void CalcUsage_DimensionDriven_AreaTimesYieldTimesQty()
    {
        var calc = new MaterialUsageCalculator();
        // 面积 = 1000×800/1e6 = 0.8 m²/枚; 段成率 1.05; 数量 5000 → 用量 = 0.8×1.05×5000 = 4200 m²
        var usage = calc.CalcDimensional(sheetDimW:1000, sheetDimF:800, yieldRate:1.05m, outputQty:5000);
        Assert.Equal(4200m, usage);
    }
    [Fact]
    public void CalcUsage_FixedQuota_UnitUsageTimesQty()
    {
        Assert.Equal(150m, new MaterialUsageCalculator().CalcFixed(unitUsage:0.03m, outputQty:5000));
    }
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现内核**

```csharp
// IMaterialUsageCalculator.cs（向下沉，見積与 MRP 共消费）
namespace CP6.Core.Services.Common;
public interface IMaterialUsageCalculator
{
    /// 尺寸驱动料：面积(mm→m²) × 段成率 × 数量
    decimal CalcDimensional(decimal sheetDimW, decimal sheetDimF, decimal yieldRate, decimal outputQty);
    /// 静态定额料：单耗 × 数量
    decimal CalcFixed(decimal unitUsage, decimal outputQty);
}
public class MaterialUsageCalculator : IMaterialUsageCalculator
{
    public decimal CalcDimensional(decimal w, decimal f, decimal yieldRate, decimal qty)
        => (w * f / 1_000_000m) * yieldRate * qty;
    public decimal CalcFixed(decimal unitUsage, decimal qty) => unitUsage * qty;
}
```

- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(plan): extract IMaterialUsageCalculator (dimensional + fixed) (spec §4)"`

## Task A-2: 見積计算改调用内核 + 回归测试（spec §4 铁律）★

**Files:** Modify `EstimateCalcService.cs`(CalculateAsync); Test `CP6.Tests/EstimateCalcRegressionTests.cs`

- [ ] **Step 1: 先写回归测试（改造前锁定行为）**（用几组真实输入跑现有 `CalculateAsync`，断言 `StandardUnitPrice`/`EstimateUnitPrice`/`EstimateSqm` 当前值——作为改造后必须一致的基线）

```csharp
[Fact]
public async Task EstimateCalc_UnchangedAfterRefactor()
{
    var dto = new EstimateCalcDto { SheetDimW=1000, SheetDimF=800, SheetFlute="A", PaperCdF="...", OrderQty=5000, /*...*/ };
    var r = await _svc.CalculateAsync(dto);
    // 锁定当前输出（改造后逐项必须一致）
    Assert.Equal(/*基线值*/, r.StandardUnitPrice);
    Assert.Equal(/*基线值*/, r.EstimateUnitPrice);
}
```

- [ ] **Step 2: 跑绿（基线）→ Step 3: 重构 CalculateAsync**（把内联的"面积×段成率"用量子计算改为调 `_usageCalc.CalcDimensional(...)`；`PaperCost = UnitPrice × 用量/数量`，即用量内核出"量"、見積乘"价"。**行为严格不变**——只是把用量算法提取出来）
- [ ] **Step 4: 跑回归测试，必须仍 PASS**（改造前后見積金额逐项一致）→ Run: `dotnet test CP6.Tests --filter EstimateCalcRegression` → PASS
- [ ] **Step 5: DI 注册 `IMaterialUsageCalculator` + 提交** → `git commit -m "refactor(erp): EstimateCalc uses shared usage calculator (behavior unchanged, regression-locked) (spec §4)"`

---

# Phase B — 单耗扩展 + 计划主数据 + 低层码

## Task B-1: ProductMaterial 补单耗三字段 + 迁移（spec §3.1）

**Files:** Modify `ProductMaterial.cs`; Modify `CP6Context`(若需); migration

- [ ] **Step 1-3: 补字段**（`UsageType` int 1尺寸驱动/2静态定额、`UnitUsage` decimal? 仅定额料、`UsageUnit` string?）；尺寸料 UsageType=1 不填单耗
- [ ] **Step 4-5: 迁移 + 提交** → `git commit -m "feat(plan): ProductMaterial usage fields (UsageType/UnitUsage/Unit) (spec §3.1)"`

## Task B-2: Plan_ItemPlanningPolicy 主数据 + 提前期汇总（spec §3.2/§5.3）

**Files:** Create `Plan_ItemPlanningPolicy.cs`, `IItemPlanningPolicyService.cs`/`...Service.cs`; Modify `CP6Context`; migration; Test

- [ ] **Step 1: 失败测试**（取计划参数；制造提前期 = WorkOrderProcess.LeadTime 沿路线汇总；缺 policy 默认 lot-for-lot+提前期0+告警）
- [ ] **Step 2: 跑红 → Step 3: 实现**（实体 Plan_ItemPlanningPolicy[ItemCd/SafetyStock/PurchaseLeadDays/LotRule(1按需/2MOQ/3订货倍数/4整卷取整)/MoqQty/MultipleQty/MakeOrBuy]；GetPolicy(itemCd) 缺则默认；GetLeadDays(itemCd)=采购取 PurchaseLeadDays / 制造取路线 LeadTime 汇总）
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + 提交** → `git commit -m "feat(plan): Plan_ItemPlanningPolicy + lead-time aggregation (spec §3.2)"`

## Task B-3: 低层码计算 + 成环检测（spec §5.3 step0 / §10）★

**Files:** Create `ILowLevelCodeService.cs`/`LowLevelCodeService.cs`; Test `LowLevelCodeServiceTests.cs`

- [ ] **Step 1: 失败测试**（共用料低层码 = 最深出现层级；成环 BOM A→B→A 检出报错）

```csharp
[Fact]
public void LowLevelCode_SharedItem_TakesDeepestLevel()
{
    // 成品X→板→原纸; 成品Y→原纸(直挂); 原纸在X下层级2、Y下层级1 → 低层码=2(取最深)
    var codes = svc.Compute(boms);
    Assert.Equal(2, codes["原纸"]);
}
[Fact]
public void LowLevelCode_Cycle_Throws() { /* A→B→A → 抛 E-PLAN-循环BOM */ }
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（遍历 BOM(ProductMaterial 按 ProcessCd 链 + 仕掛品 MaterialTypeDiv=1 转下级)，每 Item 取最深层级为低层码；DFS 检环→抛错）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(plan): low-level code computation + BOM cycle detection (spec §5.3/§10)"`

---

# Phase C — MRP 引擎（★核心）

## Task C-1: MRP 实体（MrpRun/PlannedOrder/Pegging/NetRequirement）+ 迁移（spec §3.2）

**Files:** Create `Plan_MrpRun.cs`/`Plan_PlannedOrder.cs`/`Plan_Pegging.cs`/`Plan_NetRequirement.cs`; Modify `CP6Context`; migration

- [ ] **Step 1-3: 写实体**（Plan_MrpRun[RunNo/RunAt/ScopeJson/Status]；Plan_PlannedOrder[MrpRunId/Type 采购|生产/ItemCd/Qty/RequiredDate/ReleaseDate/**Status 建议|已确认|已转单|已忽略**/ConvertedDocNo]；Plan_Pegging[PlannedOrderId/SourceType 受注|MPS|上级计划订单/SourceRefNo/Qty]；Plan_NetRequirement[MrpRunId/ItemCd/Bucket/Gross/OnHand/InTransit/InWip/FirmPlanned/SafetyStock/Net]）+ 索引（ItemCd/Bucket、Status）
- [ ] **Step 4-5: 迁移 + 提交** → `git commit -m "feat(plan): MRP entities (run/planned-order/pegging/net-req) (spec §3.2)"`

## Task C-2: SupplyService 供给汇总（含已确认计划订单，spec §5.1）★

**Files:** Create `ISupplyService.cs`/`SupplyService.cs`; Test `SupplyServiceTests.cs`

- [ ] **Step 1: 失败测试**（某 Item×日桶 供给 = WMS Stock + InboundOrder 在途 + WorkOrder 在制 + **已确认/已转单计划订单(按到货日落桶)**；建议态计划订单不计）

```csharp
[Fact]
public async Task Supply_IncludesFirmPlannedOrders_NotSuggestions()
{
    // 库存100 + 在途50 + 已确认计划订单200(到货日落桶) + 建议计划订单300
    // → 该桶供给 = 100+50+200 = 350（建议300不计）
    var supply = await svc.GetSupplyAsync(itemCd, bucket);
    Assert.Equal(350m, supply);
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（汇总四源：WMS Stock(现库存,公司级汇总 D7)、InboundOrder(采购未到)、WorkOrder(已下达在制供给)、Plan_PlannedOrder Status∈{已确认,已转单} 按 RequiredDate 落桶[scheduled receipt]）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(plan): supply aggregation incl. firm planned orders (scheduled receipt) (spec §5.1)"`

## Task C-3: MrpEngine 低层码逐层净需求 + 计划订单 + Pegging + 复算存活（spec §5.2/§5.3）★★

**Files:** Create `IMrpEngine.cs`/`MrpEngine.cs`; Test `MrpEngineTests.cs`

- [ ] **Step 1: 失败测试（★核心，多个）**

```csharp
public class MrpEngineTests
{
    [Fact] public async Task Run_NetsOncePerItem_SharedPaperMerged()
    {
        // 受注: 成品X 5000 + 成品Y 3000，都用同一原纸 → 原纸只 net 一次、需求合并(不重复备料)
        // 断言: 原纸计划订单数量 = X用量+Y用量 − 库存等，单条
    }
    [Fact] public async Task Run_FirmPlannedOrder_NotRegenerated_CountedAsSupply()
    {
        // 已确认计划订单保留(不重生) + 当供给抵扣; 建议态作废重生
    }
    [Fact] public async Task Run_NetRequirement_SubtractsAllSupply()
    {
        // 净需求 = 毛 − 库存 − 在途 − 在制 − 已确认计划订单 − 安全库存
    }
    [Fact] public async Task Run_LotRule_MOQ_RoundsUp() { /* 净需求80, MOQ100 → 计划订单100 */ }
    [Fact] public async Task Run_ReleaseDate_EqualsRequiredMinusLeadTime() { /* 下达日=需求日−提前期 */ }
    [Fact] public async Task Run_SelfMadeWip_ExplodesToLowerLevel()
    {
        // 自制瓦楞板(仕掛品)净需求>0 → 用用量内核展开其原纸子料 → 累加到原纸下层(不立即net)
    }
    [Fact] public async Task Run_Paper_ExpandsByProductMaterialRows()
    {
        // 成品X 的 ProductMaterial 有 F/C/B 三条原纸行(MaterialTypeDiv=4) → MRP 出3条原纸净需求,
        // 各用量=面积×段成率(CalcDimensional); 单层产品只出1条 —— 按 BOM 实际行,有几层算几层(MP-D6)
    }
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（照 spec §5.2/§5.3：①复算存活[建议作废重生、已确认保留]②汇总独立需求[受注交期×数量]→成品毛需求累加器③低层码 0→N 逐层：每 Item×日桶 net 一次[净需求=毛−SupplyService供给−安全库存]→净>0 按 LotRule 定批量[MOQ/倍数/整卷取整]生成 Plan_PlannedOrder[Type 按 MakeOrBuy，下达日=需求日−GetLeadDays]+Plan_Pegging→**展开直接材料：按 ProductMaterial 行(有几条算几条,MP-D6)——尺寸料(原纸 MaterialTypeDiv=4,UsageType=1)调 CalcDimensional(ProductMaster/OrderDetail 规格 SheetDimW/F + M067 段成率)、定额辅料(UsageType=2)调 CalcFixed(UnitUsage)；自制半成品(仕掛品 MaterialTypeDiv=1)同法再展开下级**→子料需求累加到下层累加器[不立即net]④落 Plan_NetRequirement 明细）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(plan): MRP engine — low-level-code netting + planned orders + pegging + regen survival (spec §5.2/§5.3)"`

## Task C-4: MrpController 运算入口（spec §9）

**Files:** Create `Controllers/Plan/MrpController.cs`; DI

- [ ] **Step 1-3: 实现**（`/api/plan/mrp/run`(范围+日桶→MrpRun)、`/mrp/run/{id}/net-requirements`(明细钻取)、`/mrp/run/{id}/planned-orders`(计划订单清单) + DI 注册 Plan 服务 + 集成测起 run→出计划订单 + 提交）

---

# Phase D — 计划看板 + 人确认转单

## Task D-1: 人确认转单服务（spec §9，MP-D4）★

**Files:** Create `IPlanConvertService.cs`/`PlanConvertService.cs`, `Contracts/IPlanToPrService.cs`+桩; Test

- [ ] **Step 1: 失败测试**（确认计划订单→Status=已确认[进供给]；采购类转单→调 IPlanToPrService 生成 PR[带 Pegging]→已转单+ConvertedDocNo；生产类转单→调 MES WorkOrderService→已转单；忽略→已忽略）
- [ ] **Step 2: 跑红 → Step 3: 实现**（ConfirmAsync[建议→已确认]；ConvertAsync[采购类调 IPlanToPrService(桩,采购实现真实)、生产类调 MES WorkOrderService，回填 ConvertedDocNo，Status=已转单，Pegging 传下游]；IgnoreAsync）
- [ ] **Step 4: 跑绿 → Step 5: DI(IPlanToPrService 桩) + MrpController 加确认/转单/忽略端点 + 提交** → `git commit -m "feat(plan): confirm + convert planned orders to PR/work-order (spec §9)"`

## Task D-2: 计划看板 + 主数据 UI（spec §9）

**Files:** Create `cp6.web/src/views/plan/{MrpBoardView,ItemPolicyView}.vue`, `src/api/plan/*`; Controller `ItemPlanningPolicyController`; 路由

- [ ] **Step 1: 实现**——MRP 看板（运行 MRP / 计划订单清单[下达日·物料·数量·类型·状态] / 净需求钻取[毛-供给-净] / Pegging 追溯 / 确认·转单·忽略按钮）+ 计划主数据维护页（ItemPlanningPolicy）。
- [ ] **Step 2: e2e 冒烟**（受注种子→运行MRP→看板出计划订单→确认→转单）
- [ ] **Step 3: DI 全装配 + 全量构建全测 + 提交** → `git commit -m "feat(plan): MRP board + planning policy UI (spec §9)"`

---

## Self-Review（对照 spec 覆盖）

- **§3 数据模型**：ProductMaterial 单耗(B-1) ✅ / ItemPlanningPolicy(B-2) ✅ / MrpRun·PlannedOrder·Pegging·NetRequirement(C-1) ✅ / 复用受注·BOM·WMS供给(C-2) ✅
- **§4 用量内核**：抽取 IMaterialUsageCalculator(A-1) ✅ / 見積改调用+回归(A-2) ✅ / 尺寸料面积驱动+定额料(A-1) ✅
- **§5 MRP 算法**：净需求含已确认计划订单供给(C-2) ✅ / 复算存活规则(C-3) ✅ / 低层码逐层 net-once(B-3+C-3) ✅ / 批量规则(C-3) ✅ / 提前期反推下达日(B-2+C-3) ✅ / Pegging(C-3) ✅ / 自制半成品展开下层(C-3) ✅ / 日桶(C-3) ✅
- **§9 衔接**：人确认转单 PR/工单(D-1) ✅ / 计划看板(D-2) ✅ / MaterialShortage 独立(不动它) ✅
- **§10 错误**：成环检测(B-3) ✅ / 缺 policy 默认(B-2) ✅ / 用量内核回归(A-2) ✅

**已知缺口/推迟（spec 已界定）：**
1. ~~产品规格落点（MP-D2）~~ —— ✅**已确认消除**：ProductMaster/OrderDetail 全套现成，无需补字段。原纸按 ProductMaterial 实际行展开（MP-D6）；per-层差异系数(中芯波纹)作 refinement。
2. **連産品完整 netting / 多仓库位维度 / 转移类型**（spec D6/D7）—— 后续阶段，P1 公司级口径、副产物仅库存流入。
3. **预测(P2)/MPS(P3)/CRP(P4)** —— 后续计划。
4. **net-change 复算**（MP-D5）—— P1 regenerative。
5. **采购 PR 真实对接**（MP-D4）—— 采购模块编码后接，P1 桩 IPlanToPrService。
6. **TenantId/PUB 权限**（MP-D3）—— OA 章10 / PUB B1 统一。

**Type 一致性：** `IMaterialUsageCalculator`(A-1) 被見積(A-2)+MRP(C-3) 共用；`Plan_PlannedOrder.Status`(C-1) 的 建议/已确认/已转单 贯穿 SupplyService 供给判定(C-2)+复算存活(C-3)+转单(D-1)；`ItemPlanningPolicy`(B-2) 的提前期/批量被 C-3 调用；低层码(B-3)被 MrpEngine(C-3)逐层用；Pegging(C-1)一路传到转单(D-1)。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-plan-p1-mrp-foundation.md`。**计划中台第一份（P1 MRP 地基）**。后续：P2 预测+安全库存 / P3 MPS / P4 CRP（各一份，源同一 spec）。

**下一步按工作流是你修订**（拍板 MP-D1~D5，尤其 **MP-D2 产品规格落点**——用量内核的入参来源，是 A-1 的前置）。定稿后执行：用量内核 → 单耗/主数据 → MRP 引擎 → 看板转单。

两种执行方式：
1. **Subagent-Driven（推荐）**——每 Task 派新 subagent，任务间评审。
2. **Inline Execution**——本会话分批 + 检查点。

---

*初稿生成于 2026-06-13。源 spec：docs/superpowers/specs/2026-06-13-mrp-planning-suite-design.md。已勘察 CP6 真实代码：EstimateCalcService.CalculateAsync(用量=面积×段成率M067,抽内核)、ProductMaterial(无单耗补字段)/ProductProcess(路线)/WorkOrderProcess(LeadTime)、WMS Stock/InboundOrder/WorkOrder(供给源)、Order(受注独立需求)、采购PrGenerationService(已规划)、MES WorkOrderService(已有)、零多租户。*
