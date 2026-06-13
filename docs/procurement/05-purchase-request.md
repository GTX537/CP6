# 05 · 采购申请 PR + 需求驱动

> **阶段 3 · 完整型扩展的起点。** MVP（01-04）解决了"下单到付款"，但漏了源头——**采购需求从哪来**。本章补上采购申请 PR：谁要买什么。需求可以手工提，也可以由**缺料反流**、**工单需求**自动生成。PR 批准后转成 PO。本章结束时，采购从"被动下单"变成"需求驱动"。
>
> 上游：[02 PO](./02-purchase-order.md)（PR 转 PO）、CP6 现有 `MaterialShortage`（Phase9 缺料）。下游：[06 RFQ](./06-rfq.md)（PR 走询价）、[08 集成](./08-integration.md)（审批）。

---

## 一、题眼：PR 是采购的需求入口

PO 是"决定向谁买、买多少钱"，PR 是它前面那一步——"**有人提出要买什么**"。把需求（PR）和订单（PO）分开，是为了：

- **需求归集**：多个部门、多张工单的缺料先汇成 PR，再由采购统一询价、合单下 PO，拿量价优势。
- **审批前置**：在花钱（PO）之前，先批"该不该买"（PR）。
- **可追溯**：每张 PO 能回答"为什么买"——它从哪张 PR、哪个缺料、哪张工单来。

> PR 不直接产生采购义务（不对供应商承诺），它只是"内部要买"的申请。真正对外的承诺是 [PO](./02-purchase-order.md)。PR→PO 是"需求 → 订单"的转化。

---

## 二、数据模型

```csharp
// CP6.Entity/DomainModels/Pur/PurchaseRequest.cs —— 头
[Table("Pur_PurchaseRequest")]
public class PurchaseRequest : BaseEntity
{
    public string  PrNo        { get; set; } = "";
    public Guid    RequesterId { get; set; }
    public Guid?   DeptId      { get; set; }                 // 申请部门（组织模型，approval 01）
    public DateTime RequestDate{ get; set; }
    public int     Status      { get; set; }                 // 草稿/已提/已批/驳回/已转PO/关闭
    public string  Source      { get; set; } = "manual";     // ★manual手工 / shortage缺料 / workorder工单
    public string? SourceRefNo { get; set; }                 // 来源单号（缺料单/工单号）
    public string? ApprovalRef { get; set; }
}

// CP6.Entity/DomainModels/Pur/PurchaseRequestLine.cs —— 行
[Table("Pur_PurchaseRequestLine")]
public class PurchaseRequestLine : BaseEntity
{
    public string  PrNo            { get; set; } = "";
    public int     LineNo          { get; set; }
    public Guid    ItemId          { get; set; }
    public decimal Qty             { get; set; }
    public string? UnitCd          { get; set; }
    public DateTime RequiredDate    { get; set; }
    public decimal? EstPrice        { get; set; }            // 估价（参考采购价表/历史）
    public Guid?   SuggestSupplierId{ get; set; }            // 建议供应商
    public string? ConvertedPoNo    { get; set; }            // 转出的 PO 号（转PO后回填）
    public int     Status          { get; set; }
}
```

---

## 三、三种来源：手工 / 缺料 / 工单

`Source` 字段标明 PR 怎么来的，这是"需求驱动"的核心：

| 来源 | 触发 | 怎么生成 |
|---|---|---|
| `manual` | 人手工提 | 采购/部门直接录 PR |
| `shortage` | **缺料反流**（CP6 Phase9 已有 `MaterialShortage`） | 库存不足/欠品 → 自动生成 PR |
| `workorder` | **工单需求** | 工单 BOM 展开发现原料不够 → 自动生成 PR |

### 缺料反流自动生成 PR（复用 Phase9）

CP6 已有 `MaterialShortage`（材料欠品反流，Phase9）。把它接到 PR：

```csharp
// CP6.Core/Services/Pur/PrGenerationService.cs
public async Task<string?> GenerateFromShortageAsync(Guid shortageId, string? user)
{
    var sh = await _db.MaterialShortages.FindAsync(shortageId);
    if (sh == null || sh.Handled) return null;

    var pr = new PurchaseRequest { PrNo = await _seq.NextAsync("PR"),
        Source = "shortage", SourceRefNo = sh.ShortageNo, Status = 0 };
    pr.Lines.Add(new PurchaseRequestLine {
        ItemId = sh.ItemId, Qty = sh.ShortageQty, RequiredDate = sh.RequiredDate,
        EstPrice = await _price.ResolvePriceAsync(/*历史/价表估价*/),
        SuggestSupplierId = await SuggestSupplier(sh.ItemId)        // 按历史采购建议供应商
    });
    _db.PurchaseRequests.Add(pr);
    sh.Handled = true;                                              // 标记已转 PR，防重复生成
    await _db.SaveChangesAsync();
    return pr.PrNo;
}
```

> **复用而非重建**：缺料检测（Phase9）已经做完了，本章只把"缺料 → 自动开 PR"这根线接上。`Handled` 标记防止同一个缺料反复生成 PR。工单需求同理——工单 BOM 缺料触发 `GenerateFromWorkOrderAsync`。

---

## 四、PR 审批（可插拔）

PR 提交后走审批（"该不该买"），与 [PO 审批](./02-purchase-order.md)同一接法——调 [approval 05 的 `IApprovalService`](../approval/05-integration.md)：

```
PR 草稿 →提交→ IApprovalService.SubmitAsync("PR", prId, requesterId, {amount, deptId})
   通过 → OnApprovedAsync → PR.Status=已批，可转 PO
   驳回 → 退回申请人
```

> **审批可插拔**：审批引擎建好前，`IApprovalService` 是"单人/跳过"的桩（总纲），PR 提交即批；引擎好了无缝换实现，PR/PO 一起享受组织路由 + 会签 + 高级动作。采购不阻塞于审批引擎。

---

## 五、PR → PO 转换

已批 PR 转 PO，按**建议供应商分组**：

```csharp
public async Task<List<string>> ConvertToPoAsync(string prNo, string? user)
{
    var lines = await _db.PurchaseRequestLines
        .Where(l => l.PrNo == prNo && l.Status == 已批 && l.ConvertedPoNo == null).ToListAsync();

    var poNos = new List<string>();
    foreach (var grp in lines.GroupBy(l => l.SuggestSupplierId))   // ★按供应商拆 PO
    {
        var poNo = await _po.CreateAsync(BuildPoDto(grp), user);   // 复用 02 建 PO（带价/税/PostingBasis）
        foreach (var l in grp) l.ConvertedPoNo = poNo;             // 回填
        poNos.Add(poNo);
    }
    await _db.SaveChangesAsync();
    return poNos;
}
```

转换规则：
- **一 PR 多供应商 → 拆多张 PO**（不同物料建议不同供应商）。
- **多 PR 同供应商 → 可合成一张 PO**（归集下单，拿量价优势）。
- 没定供应商的行 → 先走 [06 RFQ 询价](./06-rfq.md) 选定，再转 PO。
- 转出后回填 `ConvertedPoNo`，PR 行/头状态推进到"已转PO"——可追溯需求到订单的全链路。

---

## 六、资深视角

**为什么要 PR、不直接 PO？** 小作坊可以直接 PO，但规模化采购必须分离需求与订单：需求要审批（该不该买）、要归集（合单议价）、要追溯（为什么买）。PR 是采购内控和成本优化的入口。CP6 做完整可售产品，PR 不能省。

**需求驱动是采购"智能"的开始**：手工 PR 只是录入工具；接上缺料反流和工单需求，采购才从"等人来报"变成"系统自动发现要买什么"。这也是 CP6 已有 Phase9 缺料的回报——闭环越完整，越多需求能自动流入采购。

**PR 估价 `EstPrice` 的意义**：PR 审批要知道大概花多少钱（决定审批层级），但此时未必询过价。`EstPrice` 用价表/历史估个数供审批参考，真实价在转 PO（带价表）或 RFQ（询价）时定。估价 ≠ 成交价。

---

## 七、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 采购申请 → 订单 | **SAP MM 采购申请（PR / ME51N）→ PO** | PR 审批、转 PO、需求来源 |
| 需求自动生成 | **SAP MRP → 采购申请** | 物料需求计划自动产生 PR |
| 申请合单 | **Odoo 采购申请 → RFQ/PO** | 多需求归集、按供应商拆单 |

> SAP 的 MRP 自动跑出采购申请，就是本章"缺料/工单 → 自动 PR"——需求驱动采购，全世界一个套路。

---

## 八、阶段3（PR 部分）自检

- [ ] PR 和 PO 的区别？为什么规模化采购要把需求和订单分开？
- [ ] PR 三种来源是什么？缺料反流怎么复用 Phase9、怎么防重复生成？
- [ ] PR 审批为什么"可插拔"？审批引擎没好怎么不阻塞？
- [ ] PR→PO 转换按什么分组？一 PR 多供应商、多 PR 同供应商分别怎么处理？
- [ ] `EstPrice` 估价和成交价什么关系？

全部能答 → 采购有了需求入口，能被缺料/工单驱动。下一步 [06 RFQ 询价比价](./06-rfq.md)：没定供应商/要比价的需求，走询价→报价→比价选定→回写采购价表。

---

*实现：新建 `CP6.Entity/DomainModels/Pur/{PurchaseRequest,PurchaseRequestLine}.cs` + `CP6.Core/Services/Pur/{PurchaseRequestService,PrGenerationService}.cs`；复用 `MaterialShortage`（Phase9）、`IApprovalService`（审批）。*
