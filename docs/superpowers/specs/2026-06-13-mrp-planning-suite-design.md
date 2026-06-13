# CP6 计划中台（预测 / MPS / MRP / CRP）设计 spec

> **生成于 2026-06-13（brainstorming 定稿）。** 给 CP6 补上完整 ERP 的"大脑"——计划中台 `Plan`：需求预测 → 主生产计划 MPS → 物料需求计划 MRP → 粗能力计划 CRP。新建 `Plan` 命名空间（与销售/生产/库存/采购/财务平级）。
>
> **题眼**：MRP = 一台"**按低层码逐层、每 Item×日桶只净算一次**"的净需求计算机：把独立需求（受注/MPS/预测）逐级展开 BOM、扣减所有供给（库存+在途+在制+已确认计划订单）+安全库存，算出净需求 → 输出**计划订单建议** → 人确认转 采购PR / MES 工单。纸箱的"单耗"是**尺寸规格驱动**（面积×损耗），不是静态 BOM 单耗——故抽取共享用量内核 `IMaterialUsageCalculator`，見積计算与 MRP 都消费它。

---

## 一、决策摘要（brainstorming 已拍板）

| # | 决策 | 取值 |
|---|---|---|
| D1 | 范围 | **全套（预测+MPS+MRP+CRP），分阶段**：MRP 净需求为地基，预测/MPS/CRP 依次叠 |
| D2 | BOM 层级 | **混合多级**：按 BOM 实际层级递归展开（外购瓦楞板止一层、自产瓦楞板展到原纸），深度自适应 |
| D3 | 单耗来源 | **混合**：尺寸驱动料（原纸/瓦楞板）= 共享用量内核（面积×损耗×联数，复用見積算法）；辅料（油墨/胶/铁丝）= 静态定额单耗 |
| D4 | 输出 | **计划订单建议 + 人确认转单**（采购类→采购 PR；生产类→MES 工单）；不自动建单 |
| D5 | 时栅 | **基础 = 日历日**（受注交期按日）；预测/MPS 可周/月聚合展示，MRP 净算落日桶；滚动计划 |
| D6 | 連産品/副産物 | **v1 简化**：副产物作库存流入、不参与 netting；完整共产 netting 后续 |
| D7 | 仓库维度 | **v1 砍掉转移类型**，净需求按**公司级汇总口径**；多仓库位维度 + 库间转移建议 = 后续阶段（届时显式加 `WarehouseId`） |
| D8 | 预测源 | v1 = 历史统计 + 手工；客户预测导入随 VMI 后续 |
| D9 | MPS | v1 = 可人工调整的成品计划闸门（受注+净化预测→成品计划→驱动 MRP 下层） |
| D10 | 工序产能定额 | 补到 `ProductProcess`（工艺工序定义的家）；机台日历产能落 `Plan_CapacityProfile` |
| D11 | 与现有关系 | `MaterialShortage` **保持独立**（实时缺料差异告警/兜底，不并入 MRP 算法）；用量内核**向下沉**（見積/MRP 都消费，不向上挂見積） |

---

## 二、架构与边界

```
需求源 ──→ ① Forecast 需求预测 ─┐
受注(独立需求)─────────────────┼─→ ② MPS 主生产计划 ─→ ③ MRP 物料需求计划 ─→ ④ CRP 粗能力校验
                                                              │ 低层码逐层展开 BOM      │ 负荷 vs 产能
                                                              ▼                        ▼
                                                   计划订单(建议)──人确认转单──→ 采购PR / MES工单
```

**依赖方向（全部单向、只读消费）**：
- **读**：受注 `Order`/`OrderDetail`（独立需求）· `ProductProcess`/`ProductMaterial`（BOM+路线）· WMS `Stock`（库存）· `InboundOrder`（采购在途）· `WorkOrder`（在制供给）· MES `Machine`/`WgCd`/`OeeDaily`（CRP 产能基线）
- **依赖内核**：`IMaterialUsageCalculator`（共享用量内核，向下沉——見積计算与 MRP 都消费）
- **写出**：计划订单建议 → 人确认 → 调采购 `PrGenerationService`（已规划）/ MES `WorkOrderService` 建单
- **并存**：`MaterialShortage` 实时缺料告警（独立兜底，不入算法）

> **边界铁律**：Plan 只算与建议，不持有业务真相——库存真相在 WMS、采购单在采购、工单在 MES。计划订单是"建议"，转单后真相归下游。符合 CP6"单一真相、依赖单向"。

---

## 三、数据模型

### 3.1 扩展现有（最小改动）
- **`ProductMaterial` 补**：`UsageType`(1=尺寸驱动 / 2=静态定额) · `UnitUsage`(每单位成品单耗，仅定额料) · `UsageUnit`。尺寸料 `UsageType=1` 不填单耗，走用量内核。
- **`ProductProcess` 补**（CRP）：`StdRunTimePerUnit`（单位标准工时，hr/unit）或 `StdCapacity`（标准产能 units/hr），二选一存其一。它已是工艺工序定义的家（D10）。

### 3.2 新建 `Plan` 域
| 实体 | 职责 |
|---|---|
| `Plan_ItemPlanningPolicy` | **计划主数据**：ItemCd · `SafetyStock` 安全库存 · `PurchaseLeadDays` 采购提前期(★现无家) · `LotRule`(1按需/2MOQ/3订货倍数/4整卷取整) · `MoqQty` · `MultipleQty` · `MakeOrBuy`(自制/外购)。制造提前期 = `WorkOrderProcess.LeadTime` 沿路线汇总（算，不另存） |
| `Plan_Forecast` | 需求预测（ItemCd + 日桶/期 + 预测量 + 来源 历史/导入/手工） |
| `Plan_Mps` | 主生产计划（成品 + 日桶 + 计划产量；可人工调整） |
| `Plan_MrpRun` | MRP 运算批次（运行时刻/范围/参数；可复算、可对比，结果挂此批次） |
| `Plan_PlannedOrder` | **计划订单**（类型 采购/生产 · ItemCd · 数量 · 需求日 · 下达日=需求日−提前期 · `Status` 建议/已确认/已转单/已忽略 · 转出单号） |
| `Plan_Pegging` | **需求钉住/追溯**（计划订单 ← 来源：受注行/MPS/上级计划订单——"为哪张单买/产"） |
| `Plan_NetRequirement` | 净需求明细（Item × 日桶 → 毛需求/库存/在途/在制/已确认计划订单/安全库存/净需求），挂 MrpRun，供看板钻取 |
| `Plan_CapacityProfile` | 产能档案（机台/工作组 × 日历 → 可用工时/日产能） |
| `Plan_WorkCenterLoad` | CRP 负荷（工作组 × 日桶 → 需求工时 vs 可用工时 → 超载预警） |

### 3.3 复用（不重建）
受注 `Order`/`OrderDetail`(独立需求) · `ProductProcess`/`ProductMaterial`(BOM+路线) · WMS `Stock`/`InboundOrder`/`WorkOrder` · `IMaterialUsageCalculator`(用量内核) · MES `Machine`/`WgCd`/`OeeDaily`(产能基线)

---

## 四、共享用量内核 `IMaterialUsageCalculator`（解纸箱"单耗规格驱动"）

把 `EstimateCalcService` 内联的用量算法（`PaperCost=单价×展开面积×YieldRate`、容リ法使用量等）**抽取下沉**为共享内核：

```csharp
// CP6.Core/Services/Common/IMaterialUsageCalculator.cs（向下沉的内核）
decimal CalcUsage(string productCd, string materialCd, decimal outputQty);
//   尺寸驱动料(UsageType=1)：展开尺寸/面积 × 联数 × (1+损耗率) × outputQty   ← 复用見積算法
//   静态定额料(UsageType=2)：UnitUsage × outputQty
```

**見積计算与 MRP 都调它**——单一真相，改损耗规则一处生效。**重构方式**：把見積里内联的用量逻辑提到内核，見積改为调用它（行为不变），加**回归测试**锁定（見積金额逐项对比改造前后一致）。这是方案 A 的核心价值点。

---

## 五、MRP 核心算法（低层码逐层 + 净需求 + 计划订单 + pegging）

### 5.1 净需求公式（每 Item × 日桶）
```
净需求 = 毛需求
       − 现有库存(WMS Stock)
       − 在途(InboundOrder 采购未到)
       − 在制(WorkOrder 已下达供给)
       − 已确认计划订单(Plan_PlannedOrder.Status∈{已确认,已转单}, 按到货日落桶)   ← scheduled receipt
       − 安全库存(ItemPlanningPolicy.SafetyStock)
```
> 已确认计划订单是真实供给承诺，必须当 scheduled receipt 抵掉，否则复算重复生成。

### 5.2 复算存活规则（每次 MrpRun）
| 计划订单状态 | 复算时 | 净需求里 |
|---|---|---|
| 建议(未确认) | **作废重生** | 不计供给 |
| 已确认/已锁定(未转单) | **保留** | **计入供给(scheduled receipt)** |
| 已转单(已生成 PR/工单) | 保留(只读) | 由在途/在制体现，不重复计 |

### 5.3 低层码逐层算法（防共用料重复 netting）
```
0. 预处理：算每个 Item 的低层码(它在所有 BOM 里出现的最深层级)
1. 汇总独立需求(受注/MPS/净化预测) → 各成品毛需求累加器
2. 按低层码 从 0(成品) 到 N(最底层原料) 逐层：
     对本层每个 Item × 日桶：
        【只在此处 net 一次】净需求 = 毛需求累加器 − 各供给(5.1) − 安全库存
        若净需求>0 → 按 LotRule(MOQ/订货倍数/整卷取整)定批量 → 生成计划订单(类型按 MakeOrBuy)
                    下达日 = 需求日 − 提前期(采购 PurchaseLeadDays / 制造 路线 LeadTime 汇总)
                    记 Pegging(钉住来源需求)
        若为自制半成品(仕掛品·MaterialTypeDiv=1) → 用 IMaterialUsageCalculator 展开其 BOM 子料
                                 → 子料需求【累加到子料下层累加器】(不立即 net)
3. 扫到最底层，需求全汇齐后各 Item 只 net 过一次 → 原纸跨产品需求自动合并，不重复备料
```
> 核心：**展开父件只往下层累加器加数；扫到该层、需求汇齐才 net 一次**（教科书低层码法）。

### 5.4 纸箱特性处理
- 混合多级（D2）：递归深度由 BOM 实际层级自适应。
- 尺寸料用量经用量内核（面积驱动，D3）。
- **原纸三层 F面/C中芯/B里 按 `ProductMaterial`(MaterialTypeDiv=4) 实际行展开**（有几条算几条；F/C/B 三条→三条原纸净需求、单层→一条）。**规格落点已确认（2026-06-13 勘察）**：`ProductMaster.{SheetDimW,SheetDimF,SheetFlute,PaperCdF/C/B}` + `OrderDetail` 同带全套，段成率=`M067 by SheetFlute`，**无需补产品规格字段**；per-层差异系数（中芯波纹取り都）作 refinement（可用 `ProductMaterial.UnitUsage` 当层系数）。
- 损耗率进用量（用量内核内含）。
- 連産品/副産物（D6 v1）：副产物作库存流入、**不参与 netting**。

---

## 六、需求预测 Forecast（阶段 P2）

- **源**（v1，D8）：① 历史受注统计（Item×期 移动平均，简单法）③ 手工录入/调整；②客户预测导入（VMI Excel，复用 PUB 导入导出）随客户拉动。
- **预测消耗（forecast consumption，关键）**：同期实际受注**消耗**预测，毛需求避免预测与受注双计（取 `max(预测, 受注)` 或"预测 − 已落受注"）。这是预测接 MRP 的命门。
- v1 方法简单（移动平均/手工），统计预测后续。

## 七、主生产计划 MPS（阶段 P3）

- MPS = **成品级**时间分段计划产量；需求源 = 受注(确定) + 净化预测 → 成品计划闸门（D9，**可人工调整**）→ 作为 MRP 对成品的独立需求。
- 纯订单厂可弱化 MPS（直接受注驱动）；MPS 提供按预测提前排产的缓冲。

## 八、粗能力计划 CRP（阶段 P4）

- **负荷**：生产类计划订单 → 按工艺路线工序 → `需求工时 = 数量 × StdRunTimePerUnit`（或 ÷StdCapacity）→ 工作组/机台 × 日桶汇总（`Plan_WorkCenterLoad`）。
- **产能**：机台/工作组 `可用工时 = 日历 × 班次`（`Plan_CapacityProfile`）；`OeeDaily` 实际产能作定额校准基线。
- **数据补全（D10）**：`ProductProcess.StdRunTimePerUnit`（工序标准工时）+ `Plan_CapacityProfile`（机台日历产能）。CP6 现有 `Machine`/`OeeDaily`/`WgCd` 但无工序产能定额，故补。
- CRP v1 = **粗能力校验/超载预警**（负荷 vs 产能红黄灯），有限产能排程（APS）后续。

## 九、与各模块衔接

```
MRP 计划订单(建议) → 计划看板 → CRP 校验(超载预警) → 人审 确认/调整/忽略
   ├ 采购类 已确认 → 转单：复用采购 PrGenerationService 生成 PR(带 Pegging)   [采购模块已规划]
   └ 生产类 已确认 → 转单：调 MES WorkOrderService 生成工单建议               [MES 已有]
读：受注/BOM·路线/WMS库存·在途/WorkOrder在制/用量内核
并存：MaterialShortage 实时缺料告警(独立兜底,不入算法分支)
```
> 转单后计划订单→已转单(只读)，供给由在途/在制体现；Pegging 一路传到 PR/工单，回答"为哪张受注买/产"。

---

## 十、错误处理 / 边界 / 测试

- **BOM 成环检测**：低层码预处理时检出循环 BOM（A→B→A）→ 报错终止，不死循环。
- **无 BOM/无路线/无计划主数据**：缺 `ItemPlanningPolicy` 的料 → 用默认（lot-for-lot、提前期 0 + 告警）；缺 BOM 的自制件 → 告警挂起。
- **用量内核回归**：見積计算改造前后金额逐项对比一致（铁律）。
- **复算幂等/对比**：同输入两次 run 结果一致；net-change vs regenerative 复算（v1 regenerative 全重算）。
- **核心单测**：低层码排序、共用料只 net 一次、已确认计划订单当供给抵扣、批量规则(MOQ/倍数/整卷)、提前期反推下达日、forecast consumption、CRP 负荷计算、成环检测。
- **TenantId / 权限**：TenantId 延 OA 章10 系统级多租户；计划运算/确认/转单接 PUB B1 权限（落地后）。

## 十一、分阶段实施

| 阶段 | 内容 | 完成标志 |
|---|---|---|
| **P1 地基** | 用量内核抽取 + ProductMaterial 单耗扩展 + ItemPlanningPolicy + MRP（低层码法 + 5.1/5.2 供给与存活规则 + 批量/提前期 + 计划订单/Pegging + 计划看板 + 转单采购/MES） | 一张受注 → MRP 算出原纸/辅料净需求(跨产品合并)、自制半成品转下层、输出建议 → 人确认转 PR/工单 |
| **P2** | 需求预测 + 安全库存 + forecast consumption | 常备料按预测+安全库存补货 |
| **P3** | MPS 成品计划闸门 | 成品计划驱动 MRP 下层 |
| **P4** | CRP（含工序产能定额）| 负荷 vs 产能超载预警 |
| 后续 | 多仓库位维度+库间转移、完整共产 netting、统计预测、有限产能 APS、net-change 复算 | 按客户拉动 |

> 依赖：用量内核抽取需协调見積计算（加回归测试）；P1 转单依赖采购模块（已规划 PR）+ MES（已有工单）；多仓/共产/APS = P4 之后。

---

## 自检
- [ ] 净需求公式是否含"已确认计划订单"供给？复算存活规则三态是否写死？
- [ ] MRP 是低层码逐层、每 Item×日桶只 net 一次吗（共用料不重复备料）？
- [ ] 纸箱单耗：尺寸料走用量内核、辅料走静态定额——内核是否下沉为見積/MRP 共享？
- [ ] 計划主数据 `ItemPlanningPolicy`（安全库存/采购提前期/批量规则）有家了吗？
- [ ] 連産品 v1 是否只作库存流入不参与 netting？转移类型是否 v1 砍掉（公司级口径）？
- [ ] 时栅是否钉死日桶？预测消耗是否避免与受注双计？
- [ ] CRP 工序产能定额落点（ProductProcess）+ 机台日历产能（CapacityProfile）是否补齐？
- [ ] MaterialShortage 是否保持独立兜底、不并入 MRP 算法？

---

*brainstorming 定稿于 2026-06-13。已勘察 CP6 真实代码：ProductProcess(工艺路线雏形,补 StdRunTimePerUnit)/ProductMaterial(单级BOM,无单耗,补三字段)/ParentChildDiv·SetProduct(多级线索)/EstimateCalcService(用量算法,抽取下沉)/WMS Stock·InboundOrder·WorkOrder(供给源)/Machine·OeeDaily·WgCd(CRP基线)/MaterialShortage(独立兜底)/采购PrGenerationService(已规划)。下一步：writing-plans 转实施计划（建议先 P1 地基）。*
