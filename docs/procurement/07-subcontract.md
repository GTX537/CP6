# 07 · 外注加工 + 有償支給

> **阶段 5 · procurement 业务最特殊的一章。** 纸箱厂常把印刷、模切、贴合这些工序**委外**给外协厂——你把原纸/油墨（支給材）发过去，外协加工成半成品/成品再收回来，付的是**加工费**不是材料费。这和标准采购有两个根本不同：① 你发出去的支給材**还是你的资产**（不算卖、不算消耗），只是物理位置在外协；② 成品成本 = **加工费 + 支給材成本**，要并起来。本章把外注 PO（Type=2）、支給材发料追踪、成品成本核算做出来。
>
> 上游：[02 PO](./02-purchase-order.md)（Type=2 同表、`PoConsignMaterial` 子表）、[03 GR](./03-goods-receipt.md)（收成品复用 `IWmsReceiveService`）。下游：[财务 06 成本会计](../finance/06-cost-accounting.md)（成品成本入账）、[04 三单匹配](./04-three-way-match.md)（加工费走匹配）。

---

## 一、题眼：支給材发出去，资产没动，只是位置变了

标准采购是"花钱买料、料进你库"；外注是"你把料发出去、付加工费、成品收回来"。最容易错的就是把"发支給材"当成卖货或领料消耗：

> **有償支給的支給材，发出后仍是你的资产，会计上不确认收入、不确认消耗——它只是从"你的仓"挪到了"外协处（仍属你）"。`PoConsignMaterial` 追踪发了多少（`IssuedQty`）、对应成品收回多少，防止外协吞料。成品收回时，支給材成本并入成品成本，加工费另算。**

为什么强调这点？因为做错的后果很直接：

- 把支給材当"卖"→ 虚增收入、虚增成本，账全乱。
- 把支給材当"领料消耗"→ 成本提前结转，成品成本算不准。
- 不追踪 `IssuedQty`→ 发出 1000 张纸，外协只用 950 张做成品、私吞 50 张，你不知道。

> "有償支給"对应日企/SAP 的 **components provided to subcontractor**：支給材以一个内部成本（`ConsignUnitCost`）记账，转移但不出表，加工完按成品 BOM 反冲。CP6 取"追踪 + 成本并入"的最小可用形态，不做完整的委托库存科目（够纸箱厂用、可调试）。

---

## 二、数据模型：外注 PO 同表 + 支給材子表

外注 PO **不新建头表**——复用 [02 的 `PurchaseOrder`](./02-purchase-order.md)，`Type=2` 区分（80% 字段相同，省一半代码）。外注特有的"支給材"用子表 `PoConsignMaterial` 挂在 PO 行下。

```csharp
// CP6.Entity/DomainModels/Pur/PoConsignMaterial.cs —— 有償支給材追踪
[Table("Pur_PoConsignMaterial")]
public class PoConsignMaterial : BaseEntity
{
    public string   PoNo          { get; set; } = "";
    public int      LineNo        { get; set; }              // 对应哪条外注成品行
    public Guid     ConsignItemId { get; set; }              // 支給材（原纸/油墨）
    public decimal  ConsignQty    { get; set; }              // 应发数量（按成品 BOM 算）
    public decimal  ConsignUnitCost{ get; set; }             // 支給材单位成本（内部成本，非售价）
    public decimal  IssuedQty     { get; set; }              // ★已发数量（发料累加，防吞料的锚）
    public string?  WmsIssueNo    { get; set; }              // WMS 出库单号（物理出库委托返回）
}
```

外注 PO 的成品行（`PurchaseOrderLine`）的 `UnitPrice` 装的是**加工费单价**，不是材料价——这是外注和标准 PO 在"PO 行价"含义上的关键区别。

> **同表 + Type + 子表**的设计回报：状态机、收货、三单匹配、AP 全部复用标准 PO 的逻辑，外注只在"发料"和"成本核算"两处加特殊处理。如果给外注单建一套表，这些通用逻辑要写两遍。

---

## 三、外注闭环全流程

```
外注 PO（成品行=加工费 + PoConsignMaterial 支給材）确认
  → 发料：支給材发外协 → 同步调 IWmsIssueService 出库 → IssuedQty 累加、记 WmsIssueNo
  → （外协加工，系统外）
  → 收成品：建 GR → 调 IWmsReceiveService 入库成品（复用 03）
  → 成本核算：加工费（PO 单价）+ 支給材成本（ConsignQty×ConsignUnitCost）→ 成品成本
              → 接 财务 06 成本会计
  → 三单匹配 + AP：加工费走匹配 → 建 ApInvoice（复用 04）
```

四步里，**发料**和**成本核算**是外注独有的；**收成品**和**三单匹配 + AP** 复用前面章节。

---

## 四、发料：支給材出库，委托 WMS，追踪 IssuedQty

支給材的物理出库和库存一样**委托 WMS**（库存唯一真相在 WMS，采购不自己写库存），通过 `IWmsIssueService` 同步调用：

```csharp
// CP6.Core/Services/Pur/Contracts/IWmsIssueService.cs —— 采购→WMS 支給材出库
public interface IWmsIssueService
{
    Task<WmsIssueResult> IssueAsync(WmsIssueRequest req);    // 返回 WmsIssueNo + 出库明细
}

// CP6.Core/Services/Pur/SubcontractService.cs —— 发支給材
public async Task IssueConsignAsync(string poNo, int lineNo, string? user)
{
    var consigns = await _db.PoConsignMaterials
        .Where(c => c.PoNo == poNo && c.LineNo == lineNo).ToListAsync();

    foreach (var c in consigns)
    {
        var wms = await _wmsIssue.IssueAsync(new WmsIssueRequest {  // 同步委托 WMS 出库
            ItemId = c.ConsignItemId, Qty = c.ConsignQty - c.IssuedQty,
            Purpose = "subcontract", RefNo = $"{poNo}-{lineNo}"     // 标明用途=外注支給，非销售出库
        });
        c.IssuedQty += wms.IssuedQty;                              // ★累加已发（防吞料的锚）
        c.WmsIssueNo = wms.IssueNo;
    }
    await _db.SaveChangesAsync();
}
```

**会计角度**：这次出库**不确认消耗、不确认收入**——支給材只是从"自有仓"转到"外协处（仍属你）"。`WmsIssueRequest.Purpose="subcontract"` 让 WMS 把它和销售出库/生产领料区分开，库存上记为"在外协"而非"已消耗"。

> **依赖单向**（同 [03](./03-goods-receipt.md)）：采购引用 `IWmsIssueService`，WMS 不反向依赖采购。采购对库存只有"请你出库给外协"这个请求，没有写库存的权力。

---

## 五、收成品 + 成本核算：加工费 + 支給材成本并起来

成品收回走 GR，物理入库**复用 [03 的 `IWmsReceiveService`](./03-goods-receipt.md)**（和标准收货同一条线）。外注独有的是**成本核算**——成品成本要把两块并起来：

```csharp
// CP6.Core/Services/Pur/SubcontractService.cs —— 收成品后核算成品成本
public async Task<decimal> CalcFinishedCostAsync(string poNo, int lineNo, decimal finishedQty)
{
    var poLine  = await _db.PurchaseOrderLines.FirstAsync(l => l.PoNo == poNo && l.LineNo == lineNo);
    var consigns= await _db.PoConsignMaterials
        .Where(c => c.PoNo == poNo && c.LineNo == lineNo).ToListAsync();

    var processingFee = poLine.UnitPrice * finishedQty;            // ① 加工费（PO 行单价×成品数）
    var consignCost   = consigns.Sum(c => c.ConsignQty * c.ConsignUnitCost);  // ② 支給材成本

    var finishedCost  = processingFee + consignCost;              // 成品成本 = 加工费 + 支給材成本
    await _finCost.PostSubcontractCostAsync(poNo, lineNo, finishedCost, user: null); // 接财务 06
    return finishedCost;
}
```

成品成本的两块：

| 成本块 | 来源 | 含义 |
|---|---|---|
| 加工费 | 外注 PO 行 `UnitPrice × 成品数` | 付给外协的钱（走 AP，对外付款） |
| 支給材成本 | `Σ ConsignQty × ConsignUnitCost` | 你自己发出去的料的成本（内部成本，反冲在外协的资产） |

> **支給材成本"并入"而非"另付"**：加工费要付给外协（走 AP），支給材成本是你**早就买料时付过的钱**，收成品时只是从"在外协的支給材资产"结转进"成品成本"，不产生新的对外付款。这就是"有償支給"和"买成品"的会计差异——买成品付一笔全款，外注付的只是加工费这一块。成本核算结果接 [财务 06 成本会计](../finance/06-cost-accounting.md) 入账。

---

## 六、防吞料：IssuedQty 对账

`IssuedQty` 是支給材追踪的锚——发出去多少、按成品反推应耗多少，两者对账就能发现外协吞料/损耗异常：

```
应耗支給材 = 成品数 × 成品 BOM 单耗
实发支給材 = PoConsignMaterial.IssuedQty
差异 = 实发 − 应耗（含合理损耗）
   ├ 在损耗容差内 → 正常
   └ 超容差 → 异常（外协多领未用 / 私吞 / 报废未报）→ 挂起核查
```

> **为什么追踪到 `IssuedQty` 而不是只记 `ConsignQty`？** `ConsignQty` 是"应发"（计划），`IssuedQty` 是"实发"（实际）——分批发料、补发、退料都改 `IssuedQty`。只有实发量才能和成品反推的应耗量对账。不追踪实发，外协吞料你永远查不出来。这是真实纸箱厂外注最在意的风控点。

---

## 七、三单匹配 + AP：只匹加工费

外注的应付**只针对加工费**（支給材不对外付款），所以三单匹配匹的是"PO 行加工费 ↔ 收成品数 ↔ 外协加工费发票"，**复用 [04 三单匹配](./04-three-way-match.md) 的同一套容差逻辑**：

```
外协开来的发票 = 加工费发票（不含支給材）
三单匹配：PO 行(加工费单价) × 收成品数(AcceptedQty) ↔ 发票金额
   容差内 → 自动建 ApInvoice（填 PoNo，复用 04 接 IFinApService）
```

> 支給材不进这次匹配——它不是"向外协买的"，没有外协发票。外注的 AP 比标准采购少一块（材料），只匹加工费这一块。`PurchaseOrderLine` 的三个累计锚（`ReceivedQty/AcceptedQty/InvoicedQty`）对外注成品行照常用，只是 `UnitPrice` 含义是加工费。

---

## 八、资深视角

**外注的本质是"加工费采购 + 你的料的位移"，不是"买成品"。** 想清楚这一句，所有特殊处理都顺：支給材发出不算卖（位移）、成品成本要并料（你的料 + 加工费）、AP 只匹加工费（只向外协买了"加工"这个服务）。把外注当"买成品"做，账一定错。

**`Purpose="subcontract"` 这个标记不能省。** 同样是出库，销售出库要确认收入、生产领料要确认消耗、外注支給材两者都不是。WMS 靠这个 `Purpose` 把三类出库分开记账。少了它，支給材出库会被当成销售或消耗，库存和成本全错。

**支給材成本用 `ConsignUnitCost` 而非市场价。** 发出去的料按你的**入库成本**（内部成本）记，不是按能卖多少（售价）。因为这是你的资产位移，不是交易——用售价会虚增成品成本。这也是"有償支給"里"有償"两字容易误解的地方：有償指外协若损耗超量要按价赔，**正常加工不按售价结算支給材**。

**纸箱厂为什么大量用外注？** 印刷、模切设备贵、产能波动大，自建不划算时委外更经济。CP6 做纸箱厂可售产品，外注是绕不开的真实业务——这章是 procurement 区别于通用采购模板、贴合行业的地方。

---

## 九、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 完整外注/委外加工 | **SAP MM 委外采购（Subcontracting / 加工类型 L）** | 外注 PO、components provided（支給材）、成品收货反冲组件 |
| 委托库存 | **SAP 委外库存（库存类型 O，"在供应商处"）** | 支給材发出后仍属你、记在"供应商处库存" |
| 外注成本归集 | **Odoo Subcontracting（MRP Subcontracting）** | 加工费 + 原料成本归集到成品 |

> SAP 委外采购里"发给外协的 components"就是本章的 `PoConsignMaterial`（支給材），它的"库存类型 O = 在供应商处仍属你"就是本章"支給材发出不算消耗"——核心模型全世界一样，CP6 取追踪 + 成本并入的最小可用形态。

---

## 十、阶段5（外注部分）自检

- [ ] 外注和标准采购最根本的两个区别是什么？（支給材是你的资产位移 / 成品成本=加工费+料）
- [ ] 外注 PO 为什么和标准 PO 同表（Type=2）+ 子表？外注 PO 行的 `UnitPrice` 装的是什么？
- [ ] 支給材发出去，会计上算卖了吗？算消耗了吗？`Purpose="subcontract"` 为什么不能省？
- [ ] 成品成本由哪两块并起来？支給材成本是"另付"还是"结转"？为什么？
- [ ] `IssuedQty` 追踪实发量是为了防什么？怎么和成品应耗对账？
- [ ] 外注的三单匹配/AP 匹的是哪一块？为什么支給材不进匹配？

全部能答 → 外注闭环跑通：外注 PO 发支給材 → 收成品 → 成本并料核算 → 加工费走匹配建 AP，纸箱厂的委外印刷/模切立住。下一步 [08 与 CP6/财务集成](./08-integration.md)：把四个同步接口（WMS 收/发/QC、财务 AP）落地，串起采购全链。

---

*实现：新建 `CP6.Entity/DomainModels/Pur/PoConsignMaterial.cs` + `CP6.Core/Services/Pur/{SubcontractService.cs,Contracts/IWmsIssueService.cs}`；复用 [02 `PurchaseOrder`(Type=2)](./02-purchase-order.md)、[03 `IWmsReceiveService`](./03-goods-receipt.md)（收成品）、[04 三单匹配](./04-three-way-match.md)（加工费 AP）、[财务 06 成本会计](../finance/06-cost-accounting.md)（成品成本入账）。`IWmsIssueService` 由 WMS 实现（[08 集成](./08-integration.md)）。*
