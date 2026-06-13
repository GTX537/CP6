# 06 · 成本会计：实际原价（工单归集）+ 标准成本差异

> **阶段 4 · ★差异化卖点章。** 通用 ERP 算成本靠"标准成本"或粗略分摊，但 CP6 有别人没有的东西：`PaperRoll` 的残米长、`InkLot` 的消耗记录——**每张工单到底吃了多少原纸、多少油墨，是有真实数据的**。本章用这些数据做"个别原价（job-order costing）"，算出每张订单的真实成本，再和标准成本比出差异。这是纸箱厂老板最想要、通用 ERP 给不了的能力。
>
> 上游：[01 总账](./01-gl-kernel.md)、[05 自动凭证引擎](./05-auto-voucher.md)。下游被 [04 AR](./04-accounts-receivable.md) 的成本结转凭证依赖。

---

## 一、成本会计在算什么：料 + 工 + 费 → 在制 → 成品 → 卖出

制造业的成本，就是把"花掉的料、工、费"一步步归集到产品上，再随销售变成成本：

```
  生产领料/投工/分摊费用                完工                 出货
  ────────────────────→  在制品 WIP  ──────→  库存商品 FG  ──────→  主营业务成本 COGS
  直接材料 (料) ← PaperRoll/InkLot                                   （配比到收入，见04章）
  直接人工 (工) ← 工时×费率
  制造费用 (费) ← 按成本中心分摊
```

三股成本（料/工/费）汇到工单（`WorkOrder`，CP6 MES 已有）上，工单完工结转成"成品成本"，出货时再结转成"销售成本"。**会计科目对应**（见 [01 章科目表](./01-gl-kernel.md#三默认科目表模板多国别准则模板包)）：
- 料/工/费归集 → `1411 在制品 WIP`（Role=`WIP`）
- 完工 → `1412 库存商品 FG`（Role=`FG`）
- 出货 → `5001 主营业务成本 COGS`（Role=`COGS`）

---

## 二、料：吃 CP6 的真实消耗数据（核心卖点）

你选了"实际为主"。实际材料成本不靠估算，靠**真实领用记录**：

| 数据源（CP6 已有） | 提供什么 | 怎么算成本 |
|---|---|---|
| `PaperRoll`（原纸卷，WM200） | 残米长变化 = 这张工单用了多少米原纸 | 用量 × 原纸单价（批次成本） |
| `InkLot`（油墨批次，WM230） | 开封/消耗记录 = 用了多少 kg 油墨 | 用量 × 油墨批次单价 |
| `StockTransaction`（库存流水） | 一般物料的工单领用 | 出库数量 × 批次/加权单价 |

```csharp
// CP6.Core/Services/Fin/CostCollectService.cs
public async Task<decimal> CollectMaterialAsync(Guid workOrderId)
{
    decimal total = 0;
    // ① 原纸：从 PaperRoll 消耗记录取该工单用量（残米长差）
    var paperUse = await _paperRollService.ConsumptionByWorkOrderAsync(workOrderId);
    total += paperUse.Sum(p => p.UsedMeters * p.UnitCostPerMeter);
    // ② 油墨：从 InkLot 消耗取该工单用量
    var inkUse = await _inkService.ConsumptionByWorkOrderAsync(workOrderId);
    total += inkUse.Sum(i => i.UsedKg * i.UnitCostPerKg);
    // ③ 其他物料：库存流水里该工单的领用
    var matUse = await _stockService.IssueByWorkOrderAsync(workOrderId);
    total += matUse.Sum(m => m.Qty * m.UnitCost);
    return total;
}
```

> **这就是差异化**：客户问"这批 5,000 个纸箱到底花了多少原纸成本？"——通用 ERP 只能给标准用量×标准价，CP6 能给**真实领用的残米×批次价**，连损耗（残料、废品）都算得进去。对成本敏感的纸箱厂，这是签单的硬卖点。把它和[成本中心](./01-gl-kernel.md#411-成本中心分析性会计维度现在就占位)结合，还能切到"哪台机、哪道工序最费料"。

---

## 三、工 + 费：CP6 的硬缺口（必须先定数据源）

⚠️ **这是阶段 4 动手前必须拍板的前置**（[总纲待定项](./README.md#-仍待定不阻塞阶段-02到对应阶段再拍)）：料有真实数据，但**工时（直接人工）和制造费用，CP6 目前没有采集**。两条路：

| 方案 | 怎么做 | 取舍 |
|---|---|---|
| **(a) 标准估算**（推荐起步） | 工 = 工单标准工时 × 标准费率；费 = 按成本中心/工时标准分摊率 | 不改 MES、立刻能用；精度依赖标准维护 |
| **(b) 真实采集** | MES 加工时报工（开工/完工打卡）、设备工时从 OEE 取 | 精度高、和料一样"实际"；要改 MES 采集 |

> 我的建议：**料用实际（CP6 有数据）、工和费先用标准估算（方案 a）**——这已经比通用 ERP 准（料是真的），又不阻塞。等价值验证了，再上方案 b 把工时也变实际。这正是你定的"实际为主 + 标准参考"的务实落地。`OeeDaily`/`MachineDowntime`（MES 已有）未来可作方案 b 的工时数据源。

---

## 四、CostSheet：一张工单的成本归集单

```csharp
// CP6.Entity/DomainModels/Fin/CostSheet.cs
public class CostSheet : BaseEntity
{
    public int TenantId { get; set; }
    public Guid WorkOrderId { get; set; }              // 归集到哪张工单（MES 已有）
    public Guid? OrderId { get; set; }                 // 关联受注
    public Guid? CostCenterId { get; set; }            // 主成本中心（机台/工序）

    public decimal MaterialActual { get; set; }        // 实际料（PaperRoll/InkLot）
    public decimal LaborStd { get; set; }              // 工（标准估算）
    public decimal OverheadStd { get; set; }           // 费（标准分摊）
    public decimal TotalActual => MaterialActual + LaborStd + OverheadStd;

    public decimal StandardCost { get; set; }          // 标准总成本（ProductMaster 维护）
    public decimal Variance => TotalActual - StandardCost;   // 差异（>0 超支）
    public CostSheetStatus Status { get; set; }         // 归集中/已完工结转
    public List<CostSheetLine> Lines { get; set; } = new();   // 料工费明细（来源可追）
}
public enum CostSheetStatus { Collecting = 0, Settled = 1 }

// CostSheetLine —— 归集明细（每笔料/工/费的来源可追溯）
public class CostSheetLine : BaseEntity
{
    public Guid CostSheetId { get; set; }
    public CostElement Element { get; set; }            // 料 / 工 / 费
    public string SourceType { get; set; } = "";       // PaperRoll / InkLot / Stock / 工时 / 费率分摊
    public string? SourceId { get; set; }              // 来源单据/批次 Id（残米记录/油墨批次…）
    public decimal Qty { get; set; }                   // 用量（米/kg/工时…）
    public decimal UnitCost { get; set; }
    public decimal Amount { get; set; }                // = Qty × UnitCost
    public bool IsStandard { get; set; }               // 实际 or 标准（工费暂用标准时为 true）
}
public enum CostElement { Material = 1, Labor = 2, Overhead = 3 }
```

**标准成本（参考线）**放在现有 `ProductMaster` 上（每个产品的标准料工费）。`Variance = 实际 − 标准`就是差异分析——超支多少、哪块超（料超还是工超），是成本管理的抓手。

---

## 五、成本相关的自动凭证（料工费 → WIP → FG → COGS）

全由 [05 自动凭证引擎](./05-auto-voucher.md)生成：

```
① 生产领料/投工/分摊（归集到 WIP）：
   借  在制品 WIP (Role=WIP, 带 CostCenterId)
   贷  原材料 / 应付职工薪酬 / 制造费用

② 工单完工（WIP → 成品）：
   借  库存商品 FG (Role=FG)        TotalActual
   贷  在制品 WIP (Role=WIP)              TotalActual

③ 出货（FG → 销售成本，见 04 章②）：
   借  主营业务成本 COGS
   贷  库存商品 FG
```

> **完工结转用 `工单完工事件`**（MES `WorkOrder` 状态→完工）触发，挂 `FinBridgeHook`。FG 单位成本 = `CostSheet.TotalActual / 完工数量`，这个数**正是 [04 章成本结转](./04-accounts-receivable.md#二杀手锏出货自动开票吃-cp6-现成数据)要的**——成本会计和 AR 在这里接上。

---

## 六、它怎么嵌进 CP6

| 成本需要 | CP6 现成的 | 怎么用 |
|---|---|---|
| 实际料用量 | `PaperRoll` 残米 / `InkLot` 消耗（WM200/230） | 按工单取真实用量×批次价 |
| 工单 / 完工事件 | `WorkOrder`（MES 已有） | 归集载体 + 完工触发结转 |
| 标准成本 | `ProductMaster`（已有） | 维护标准料工费做差异基准 |
| 成本中心 | `CostCenter`（01 章新建）↔ MES `Machine` | 料工费按机台/工序切，OEE 数据可作工时源 |
| 工时（未来实际化） | `OeeDaily`/`MachineDowntime`（MES） | 方案 b 的数据源 |

落点：`CP6.*/.../Fin/{CostSheet,CostCollectService}`、`cp6.web/src/views/fin/CostSheetView.vue`（含差异分析看板）。

---

## 七、阶段 4 完成自检

- [ ] 一张工单的实际材料成本，是从 `PaperRoll`/`InkLot` 真实用量算的，还是估的？
- [ ] 工和费我用的是标准估算（方案 a）还是真实采集（方案 b）？为什么这样选？
- [ ] 料工费 → WIP → FG → COGS 四步结转，每步的凭证我能写出来吗？
- [ ] 完工时 FG 单位成本怎么算？它和 04 章出货成本结转怎么接上？
- [ ] 实际 vs 标准的差异（`Variance`），能切到"哪块超支、哪个成本中心超"吗？
- [ ] 这套能回答客户"这批纸箱真实成本多少"——通用 ERP 为什么答不了？

全部能答 → 你做出了差异化的核心。下一章 [07 多币种](./07-multi-currency.md)：把延后的外币 AP/AR 补上，处理汇兑损益。

---

*生成于 2026-06-10。需求基线：实际为主+标准参考 / 料用 PaperRoll·InkLot 实际、工费先标准估算 / 成本中心切分。配套实现落于 `CP6.*/.../Fin`。*
