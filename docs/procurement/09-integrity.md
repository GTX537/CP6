# 09 · 完整性与异常

> **Part 3 收尾 · 把风控兜底做完。** 前面各章埋了不少"防滥用"的点——[04](./04-three-way-match.md) 用容差防虚开发票、[07](./07-subcontract.md) 用 `IssuedQty` 防外协吞料。本章把这些点收成一张**完整性保障网**，再补上两个还没系统化的风险：**重复收货**（同一批货收两次、虚增库存与应付）和**采购对账**（PO↔GR↔AP 三方对不上时怎么查）。本章结束时，采购链不只"能跑通"，而且"跑不歪"——每个能被钻空子的地方都有兜底。
>
> 上游：[04 三单匹配](./04-three-way-match.md)（容差、`InvoicedQty` 锚）、[07 外注](./07-subcontract.md)（`IssuedQty` 对账）。全书风控的汇总章。

---

## 一、题眼：采购的钱从三个口子漏，每个口子都要堵

采购是企业"花钱"的模块，舞弊和差错的口子都在"数量"和"金额"对不上的缝里：

> **采购的完整性 = 堵三个漏：① 虚开发票（没收货就建应付、或发票量超收货量套现）② 重复收货（同一批货收两次，虚增库存和应付）③ 外协吞料（支給材发出多于成品实耗）。三个漏的共同根：累计量失控。堵法的共同解：以 PO 行的累计锚（`ReceivedQty/AcceptedQty/InvoicedQty`）为唯一基准，任何超额都挡下或挂起。**

```
PO 行三个累计锚（02 定义、03/04 累加）是完整性的总基准：
  ReceivedQty ≤ PO.Qty（+收货容差）   ← 防重复/超量收货
  AcceptedQty ≤ ReceivedQty            ← QC 合格不能超过实收
  InvoicedQty ≤ AcceptedQty（+容差）   ← 防虚开：没合格收货不能建票
```

> 一句话：**所有量都挂在 PO 行的累计锚上，单调累加、各有上限，超限即异常**。完整性不是靠事后审计，是靠每次累加时的硬约束。

---

## 二、防虚开发票：没合格收货，不能建票

[04 三单匹配](./04-three-way-match.md) 的容差匹配已经是主防线，这里强调它的完整性内核——**`InvoicedQty` 不能超过 `AcceptedQty`**：

```csharp
// CP6.Core/Services/Pur/ThreeWayMatchService.cs —— 建票前的硬校验
private void GuardInvoiceQty(PurchaseOrderLine poLine, decimal invoiceQty)
{
    var allowable = poLine.AcceptedQty - poLine.InvoicedQty;       // 还能开票的量
    var tol = _tol.AmountAbsTol(poLine);                           // 容差兜小额波动
    if (invoiceQty > allowable + tol)
        throw new BizException(
            $"开票量 {invoiceQty} 超过可开票量 {allowable}（已合格 {poLine.AcceptedQty}、已开 {poLine.InvoicedQty}）");
}
```

三条防虚开规则（[04](./04-three-way-match.md) 已述，这里汇总）：

| 规则 | 防什么 |
|---|---|
| `InvoicedQty ≤ AcceptedQty + 容差` | 防"没收货/收货没合格就开票"套现 |
| 发票单价 ↔ PO 单价在价格容差内 | 防"抬高单价"虚增应付 |
| 超容差 → 挂起人工放行，不自动建票 | 防异常溜进自动流程 |

> **为什么以 `AcceptedQty` 而非 `ReceivedQty` 为开票上限？** 检收基准下，收了货但 QC 不良不该付钱——只有**合格**的货才产生付款义务。用 `AcceptedQty` 当上限，QC 不良品自动被挡在应付之外。着荷基准下 `AcceptedQty=ReceivedQty`（免检即合格），规则统一不用分叉。

---

## 三、防重复收货：累计锚 + WMS 入库号双锁

同一批货收两次，会虚增库存（WMS）和后续应付——这是采购最常见的差错。两道锁：

```csharp
// CP6.Core/Services/Pur/GoodsReceiptService.cs —— 收货前校验
private async Task GuardDuplicateReceipt(GoodsReceiptLine grl, PurchaseOrderLine poLine)
{
    // 锁① 累计上限：本次 + 已收 不能超过 PO 量（含收货容差，允许小幅多到货）
    var recvTol = _tol.QtyTolerance(poLine);
    if (poLine.ReceivedQty + grl.ReceivedQty > poLine.Qty * (1 + recvTol))
        throw new BizException($"超量收货：PO 行 {poLine.LineNo} 已收 {poLine.ReceivedQty}，本次 {grl.ReceivedQty} 超 PO 量 {poLine.Qty}");

    // 锁② 物理幂等：同一 WMS 入库明细不能被两张 GR 引用（防同一物理入库建两次 GR）
    var dup = await _db.GoodsReceiptLines
        .AnyAsync(x => x.WmsReceiptDetailRef == grl.WmsReceiptDetailRef && x.GrNo != grl.GrNo);
    if (dup) throw new BizException($"重复收货：WMS 入库明细 {grl.WmsReceiptDetailRef} 已被其他 GR 引用");
}
```

> **两道锁各防一种重复**：锁①防"逻辑超量"（PO 只订 100，收了 80 又想收 50）；锁②防"物理重复"（同一次 WMS 入库被错误地建了两张 GR）。`WmsReceiptDetailRef`（[03](./03-goods-receipt.md) WMS 返回的明细引用）是物理幂等的唯一键——它保证一次真实入库只对应一次采购收货。收货容差允许小幅多到货（散装/卷材常见），但超容差就是异常。

---

## 四、防外协吞料：IssuedQty 对账（汇总 07）

[07](./07-subcontract.md) 已讲支給材 `IssuedQty` 追踪，这里把它纳入完整性体系——**外协实耗对账**：

```
应耗支給材 = Σ(成品收回数 × 成品 BOM 单耗)
实发支給材 = PoConsignMaterial.IssuedQty
损耗 = 实发 − 应耗
   ├ 在损耗容差内 → 正常（加工合理损耗）
   └ 超容差 → 异常 → 挂起核查（外协多领未用 / 私吞 / 报废未报）
```

> 外协吞料的本质也是"累计量失控"——发出的支給材（`IssuedQty`）远多于成品反推的应耗，钱就漏在外协手里。和虚开、重复收货同源同解：以追踪量为基准，超额即异常。

---

## 五、采购对账：三方对不上时怎么查

完整性不只是"挡住异常"，还要在对不上时**能定位**。采购对账是 PO↔GR↔AP 三方的累计量核对表：

```csharp
// CP6.Core/Services/Pur/ReconciliationService.cs —— PO 三方对账
public async Task<List<PoReconRow>> ReconcilePoAsync(string poNo)
{
    var lines = await _db.PurchaseOrderLines.Where(l => l.PoNo == poNo).ToListAsync();
    return lines.Select(l => new PoReconRow {
        LineNo = l.LineNo, ItemId = l.ItemId,
        Ordered  = l.Qty,                          // 订了多少
        Received = l.ReceivedQty,                  // 收了多少
        Accepted = l.AcceptedQty,                  // 合格多少
        Invoiced = l.InvoicedQty,                  // 开票多少
        OpenToReceive = l.Qty - l.ReceivedQty,     // 待收
        OpenToInvoice = l.AcceptedQty - l.InvoicedQty, // 待开票
        Status = DiagnoseLine(l)                   // 诊断：正常/待收/待开票/超量/挂起
    }).ToList();
}
```

对账表的诊断口径：

| 现象 | 含义 | 处理 |
|---|---|---|
| `Received < Ordered` | 还有货没到 | 待收，正常在途 |
| `Accepted < Received` | 有货在待检/不良 | 查 QC，不良走退货/让步 |
| `Invoiced < Accepted` | 合格了还没开票 | 待开票，正常 |
| `Invoiced > Accepted + 容差` | **虚开嫌疑** | 挂起核查 |
| `Received > Ordered + 容差` | **超量收货** | 挂起核查 |

> **对账是完整性的"看得见"层**：硬约束（二、三、四节）在写入时挡异常，对账表在事后让人**一眼看出每张 PO 卡在哪**——待收、待开票、超量、挂起，状态清清楚楚。采购员据此追在途、催发票、查异常，财务据此核应付。

---

## 六、资深视角

**三个漏同源同解，是这章最该带走的认知。** 虚开、重复收货、外协吞料，表面是三种舞弊/差错，本质都是"某个累计量超过了它该有的上限"。所以解法统一：**把所有量挂到 PO 行的累计锚上，单调累加、各设上限、超限即挡**。理解了这个"同源同解"，再多的风险点也能用一套框架兜住，不必为每种异常发明一套机制。

**硬约束 + 对账表，缺一不可。** 只有硬约束（写入时挡）没对账表，异常被挡了但人不知道卡在哪，单据堆积无人处理；只有对账表没硬约束，等于事后才发现钱漏了、补救已晚。**写入时挡（防）+ 事后能查（追）**，才是完整的风控。

**容差是风控的旋钮，不是漏洞。** 收货容差允许小幅多到货、金额容差兜小额波动——不是放松风控，而是让"自动流程"不被正常的物理波动淹没（散装称重、汇率尾差）。容差按品类/供应商精调：太松漏舞弊，太紧把采购淹在挂起里。**会调容差，才会做风控。**

**完整性是"可售产品"和"演示 demo"的分水岭。** demo 只要正向流程跑通；真实企业用的采购系统，必须假设有人会钻空子、有货会收重、有外协会吞料。把这些兜底做完，CP6 的采购才是能交付给企业、扛得住审计的产品。

---

## 七、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 发票校验容差与冻结 | **SAP MM 发票校验（MIRO）+ 容差码/冻结** | 数量/价格/金额容差、超差冻结待人工放行 |
| 收货防重复 | **SAP GR + 移动类型 + 参考凭证** | 以采购订单/交货单为参考，防重复过账 |
| 三方对账 | **SAP 采购订单历史（PO History）/ Odoo 账单控制** | PO↔GR↔IR 累计量对照、未清量分析 |

> SAP 的"PO History"逐行列出订购/收货/开票累计量，就是本章的采购对账表；它的"发票校验容差冻结"就是本章"超容差挂起人工放行"——风控的核心模型，全世界一致。

---

## 八、本章自检

- [ ] 采购的钱从哪三个口子漏？三个漏的"同源"是什么？"同解"又是什么？
- [ ] 为什么开票上限用 `AcceptedQty` 而非 `ReceivedQty`？检收/着荷基准下怎么统一？
- [ ] 防重复收货的两道锁各防什么？`WmsReceiptDetailRef` 在物理幂等里起什么作用？
- [ ] 外协吞料怎么靠 `IssuedQty` 对账？它和虚开、重复收货为什么同源同解？
- [ ] 采购对账表对哪三方的什么量？`Invoiced > Accepted`、`Received > Ordered` 分别是什么嫌疑？
- [ ] 为什么"硬约束 + 对账表"缺一不可？容差是放松风控吗？

全部能答 → 采购链不只能跑通，而且跑不歪：三个漏都堵上、对账能定位异常。**procurement 丛书（01-09）至此全齐**——从供应商主数据到完整性兜底，采购链通，财务 AP 从"手工录票"升级为"三单匹配自动建票"，CP6 的"买"这条腿立住了。

---

*实现：完整性校验分散在各 Service 写入路径（`GuardInvoiceQty`/`GuardDuplicateReceipt`/外协对账），统一基准是 [02 PO 行累计锚](./02-purchase-order.md)；新建 `CP6.Core/Services/Pur/ReconciliationService.cs`（PO 三方对账表）。容差复用 [04 `MatchTolerance`](./04-three-way-match.md)。*
