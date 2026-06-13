# 01 · 供应商与采购主数据

> **阶段 0 · 从这里入门。** 采购链的地基是"供应商"和"采购价"。好消息：CP6 的 `BusinessPartner`（取引先）已经有一个**完整的発注先 Tab**——供应商主数据**基本不用新建，复用它**。本章唯一要新建的是**采购价表 `SupplierPrice`**（带阶梯价、有效期）。本章结束时，能选着供应商、自动带出采购价，为 [02 采购订单](./02-purchase-order.md) 备好数据。
>
> 上游：[总纲](./README.md)（复用 vs 新建、同步接口委托）。下游：[02 PO](./02-purchase-order.md)（带出价、PostingBasis）、[04 三单匹配](./04-three-way-match.md)（税码/容差）。

---

## 一、题眼：供应商主数据 ≈ 已存在

CP6 的 `BusinessPartner` 是"一张取引先表 ⇄ 9 种属性 FLG"的设计：

> 得意先 / 売掛先 / 請求先 / 入金先 / 納品先 / **発注先** / 買掛先 / 支払予定管理先 / 支払先

也就是说，**客户和供应商是同一张表**，靠属性 FLG 区分一个取引先扮演哪些角色（一家公司可以既是客户又是供应商）。其中 `SupplierFlg` + **発注先 Tab** 就是采购要的供应商主数据——已经存在，且字段相当完整：

```csharp
// CP6.Entity/DomainModels/Erp/BusinessPartner.cs —— 発注先 Tab（节选，已存在）
public bool    SupplierFlg          { get; set; }          // 是发注先吗
public string? SupplierPattern      { get; set; }          // 1原材料/2資材/3副資材/4購買品/5外注品/9他
public string? PurchasePostingDiv   { get; set; } = "2";   // ★采购计上基准：2=検収基準（默认）
public string? PurchaseTaxCd        { get; set; } = "P010"; // 采购税码
public string? PurchaseTaxFractionDiv{ get; set; } = "3";  // 税额端数处理
public string? CurrencyCd           { get; set; }          // 币种
public string? SupplierCalendarCd   { get; set; } = "CAL01";// 供应商日历（交期算）
// 外注（SupplierPattern=5 时）
public string? /*外注単価区分*/ ...;                        // 1=加工单价+有偿支给单价
public string? /*支給計上区分*/ ...;                        // 有偿支给计上基准
// 有偿支给连动税码 PaidSupplyTaxCd 等
```

> **所以采购模块不新建供应商表。** 新建一张供应商表 = 和 `BusinessPartner` 数据双写、客户/供应商两套人马、对账时对不上。复用它，采购只是"读发注先 Tab"。这与 [模块分类约定](../approval/README.md) 里"供应商管理归主数据、复用 BusinessPartner"一致。

---

## 二、采购要用到発注先 Tab 的哪些字段

| 采购场景 | 用 `BusinessPartner` 的字段 | 怎么用 |
|---|---|---|
| 这家是不是供应商 | `SupplierFlg` | 建 PO 时只能选 `SupplierFlg=true` 的 |
| 是什么类型供应商 | `SupplierPattern` | 5=外注品 → 走 [07 外注流程](./07-subcontract.md)；其余走标准采购 |
| 计上基准（何时认） | `PurchasePostingDiv` | 2=検収基準/着荷基準 → 带入 PO 的 `PostingBasis`（[02](./02-purchase-order.md)/[03](./03-goods-receipt.md)） |
| 采购税码 | `PurchaseTaxCd` | 建 PO 行算税、[04 三单匹配](./04-three-way-match.md)校验税额 |
| 币种 | `CurrencyCd` | PO 币种默认值 |
| 交期 | `SupplierCalendarCd` | 按供应商日历算预计到货日 |
| 外注/有偿支给 | 外注単価区分 / 支給計上区分 / `PaidSupplyTaxCd` | [07 外注加工](./07-subcontract.md) |

**采购读它、不改它**：供应商主数据维护仍在 `BusinessPartner` 的发注先 Tab，采购模块只读引用。依赖单向。

---

## 三、唯一要新建：采购价表 `SupplierPrice`

`BusinessPartner` 没有"这家供应商这个物料卖多少钱"——这是采购独有的，要新建：

```csharp
// CP6.Entity/DomainModels/Pur/SupplierPrice.cs（新建）
[Table("Pur_SupplierPrice")]
public class SupplierPrice : BaseEntity
{
    public Guid     SupplierId  { get; set; }   // → BusinessPartner（发注先）
    public Guid     ItemId      { get; set; }   // → 物料/製品
    public decimal  Price       { get; set; }
    public string   CurrencyCd  { get; set; } = "JPY";
    public decimal  MinQty      { get; set; }   // ★阶梯价：达到此量适用本价
    public DateTime ValidFrom   { get; set; }   // ★有效期
    public DateTime? ValidTo    { get; set; }
    public string   Source      { get; set; } = "manual"; // 来源：manual手工 / rfq询价回写
}
```

三个设计点：

- **阶梯价（`MinQty`）**：同一供应商同一物料，买 100 个和买 1000 个单价不同。一个 `(供应商,物料)` 有多行，按数量落在哪个阶梯取价。
- **有效期（`ValidFrom/To`）**：价格会随时间调。建 PO 那天按"当时有效"的价取，历史 PO 不受新价影响。
- **来源（`Source`）**：手工维护，或由 [06 询价比价 RFQ](./06-rfq.md) 选中报价后**回写**进来——价表既是输入也是 RFQ 的产物。

> 为什么不把价直接塞 `BusinessPartner`？因为价是 `(供应商 × 物料 × 数量 × 时间)` 四维的、且高频变动，塞进供应商主数据会让它臃肿且无法表达阶梯/历史。**价独立建表**，主数据保持稳定。

---

## 四、采购价带出逻辑

建 [PO](./02-purchase-order.md) 行选了供应商 + 物料 + 数量后，自动带出单价：

```csharp
// CP6.Core/Services/Pur/SupplierPriceService.cs
public async Task<decimal?> ResolvePriceAsync(Guid supplierId, Guid itemId, decimal qty, DateTime onDate)
{
    return await _db.SupplierPrices
        .Where(p => p.SupplierId == supplierId && p.ItemId == itemId
                 && p.MinQty <= qty                                   // 满足阶梯
                 && p.ValidFrom <= onDate && (p.ValidTo == null || p.ValidTo >= onDate)) // 当时有效
        .OrderByDescending(p => p.MinQty)                             // 取满足条件的最高阶梯
        .Select(p => (decimal?)p.Price)
        .FirstOrDefaultAsync();
}
```

取价规则：**满足"数量≥MinQty 且 当时有效"的所有价里，取 MinQty 最大的那条**（买够 1000 享 1000 档价，而不是 100 档）。取不到（新物料没维护价）→ 返回 null，PO 行价留空让采购手填，并提示"无采购价"。

---

## 五、PostingBasis：检收基准 vs 着荷基准（贯穿全书的开关）

`BusinessPartner.PurchasePostingDiv` 带出的"采购计上基准"是采购链一个核心开关，建 PO 时拍进 `PostingBasis`，影响后面**何时确认收货、何时建应付**：

| 基准 | 含义 | 影响（[03](./03-goods-receipt.md)/[04](./04-three-way-match.md)） |
|---|---|---|
| **着荷基准** | 货到即认 | 收货入库即累加 `ReceivedQty`、可建 AP |
| **検収基准** | QC 合格才认 | 收货先待检 → 查 QC 合格才累加 `AcceptedQty`、才建 AP |

> 这个基准**按供应商配**（发注先 Tab 默认值），建 PO 时可覆盖。它决定了 [03 收货](./03-goods-receipt.md) 走哪条确认路径——是本书"双基准"的源头。本章只负责把它从供应商带进 PO，逻辑在 03/04。

---

## 六、与财务、受注共用一张 `BusinessPartner`

呼应 [模块分类](../approval/README.md)：`BusinessPartner` 是**跨模块共享主数据**——

- 受注用它的**得意先**属性（客户）。
- 采购用它的**発注先**属性（供应商）。
- 财务用它的**買掛先/売掛先**属性（应付/应收对象）。

一家公司可能同时是客户和供应商，**同一条记录、不同属性 FLG**。所以供应商管理归"主数据"、不归采购独有——采购只是它的发注先视图的消费者之一。

---

## 七、资深视角

**为什么"复用主数据"是采购落地最大的便宜？** 采购模块最重的部分本该是供应商主数据（联系人、资质、税务、付款条件、外注配置……）。CP6 的 `BusinessPartner` 发注先 Tab 已经把这些做完了。采购直接站在它肩上，真正要新建的只有"价表 + 单据流"——工作量砍掉一大块。这也是总纲说采购"30% 已有地基"的来源。

**价表为什么是 RFQ 的下游又是 PO 的上游？** [06 RFQ](./06-rfq.md) 询价选中报价 → 回写价表（`Source=rfq`）；[02 PO](./02-purchase-order.md) 建单 → 从价表带出。价表是采购价格的"单一事实源"，询价喂它、下单用它，形成闭环。

**主数据只读 vs 可改？** 采购对 `BusinessPartner` 严格只读。要新增/改供应商，走主数据维护（发注先 Tab），不在采购单据里顺手改主数据——否则主数据被各模块乱改，没人能信任它。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 供应商主数据 | **SAP 供应商主记录（采购视图/会计视图）** | 一个 BP 多视图，正是 BusinessPartner 的多属性 FLG |
| 客户供应商合一 | **SAP Business Partner（S/4 的 BP 概念）** | 一条 BP 兼任客户/供应商 |
| 阶梯价/价格主数据 | **SAP 采购信息记录 / Odoo 供应商价格表** | (供应商×物料) 的价、阶梯、有效期 |

> SAP 的"采购信息记录（Info Record）"就是本章的 `SupplierPrice`——`(供应商, 物料)` 的价格主数据，全世界一个模型。

---

## 九、阶段0（主数据部分）自检

- [ ] 采购为什么不新建供应商表？`BusinessPartner` 的"9 属性 FLG"是什么意思？
- [ ] 采购要从发注先 Tab 读哪几类字段？分别用在哪？
- [ ] `SupplierPrice` 为什么要有 `MinQty`/`ValidFrom-To`/`Source` 三样？取价规则是什么？
- [ ] `PostingBasis`（检收/着荷）从哪来、影响后面什么？
- [ ] 为什么客户和供应商是同一张 `BusinessPartner`？这对"供应商归主数据"意味着什么？

全部能答 → 主数据就位。下一步 [02 采购订单](./02-purchase-order.md)：用复用的供应商 + 带出的采购价，建标准 PO，配状态机与 PostingBasis。

---

*实现：新建 `CP6.Entity/DomainModels/Pur/SupplierPrice.cs` + `CP6.Core/Services/Pur/SupplierPriceService.cs`；`BusinessPartner` 只读复用。*
