# 03 · 收货 GR + WMS 委托（双基准）

> **阶段 1 · 锚开始动。** 货到了，建收货单 GR。这一章有两件事最关键：① 采购建 GR 用于匹配，但**物理入库委托给 WMS**——库存的唯一真相在 WMS，采购绝不自己写库存；② **双基准分叉**——着荷基准"货到即认"、检收基准"QC 合格才认"，决定何时累加 PO 行的 `ReceivedQty`/`AcceptedQty`、何时能建应付。本章结束时，PO 行三个锚里的前两个开始动。
>
> 上游：[02 PO](./02-purchase-order.md)（累计锚、PostingBasis）、[总纲](./README.md)（四接口、库存唯一真相在 WMS）。下游：[04 三单匹配](./04-three-way-match.md)（用 `AcceptedQty`）、[08 集成](./08-integration.md)（WMS 接口落地）。

---

## 一、题眼：采购拥有 GR 单据，但不拥有库存

```
采购：GoodsReceipt（收货单——为匹配而存在）
  │ 同步调
  ▼
WMS：IWmsReceiveService → 写库存/批次/PaperRoll（库存唯一真相）
```

> **采购有 GR 单据与逻辑，物理库存通过同步接口单向委托给 WMS。库存唯一真相在 WMS，采购不双写。** 采购的 GR 记的是"为三单匹配服务的收货事实"（收了多少、合格多少），不是库存本身。真正的"库存 +N、生成批次/卷号"是 WMS 干的，采购只是同步调它、拿回一个入库单号。

为什么是**同步接口**而不是事件？总纲讲过：收货→入库这种动作，调试时要能一步步跟下去（采购调 WMS → WMS 返回入库号 → 采购记下），不该丢进异步死信里捞。这是"同步可调试"。

---

## 二、数据模型：GR 头 + 行

```csharp
// CP6.Entity/DomainModels/Pur/GoodsReceipt.cs —— 头
[Table("Pur_GoodsReceipt")]
public class GoodsReceipt : BaseEntity
{
    public string  GrNo        { get; set; } = "";
    public string  PoNo        { get; set; } = "";
    public Guid    SupplierId  { get; set; }
    public DateTime ReceiptDate { get; set; }
    public int     Status      { get; set; }      // 0待检/1已检收/2部分/3完成
    public string? WmsInboundNo{ get; set; }      // ★WMS 返回的物理入库单号
    public string  PostingBasis{ get; set; } = "检收"; // 从 PO 带（02章）
}

// CP6.Entity/DomainModels/Pur/GoodsReceiptLine.cs —— 行
[Table("Pur_GoodsReceiptLine")]
public class GoodsReceiptLine : BaseEntity
{
    public string  GrNo        { get; set; } = "";
    public int     LineNo      { get; set; }
    public int     PoLineNo    { get; set; }      // 对应哪条 PO 行
    public Guid    ItemId      { get; set; }
    public decimal ReceivedQty { get; set; }      // 这次收了多少
    public decimal AcceptedQty { get; set; }      // 这次合格多少
    public decimal RejectedQty { get; set; }      // 这次不良多少
    public int     QcStatus    { get; set; }      // 0免检/1待检/2合格/3不良
    public string? WmsReceiptDetailRef { get; set; } // WMS 入库明细引用
}
```

---

## 三、两个 WMS 委托接口

```csharp
// CP6.Core/Services/Pur/Contracts/IWmsReceiveService.cs —— 采购→WMS 物理入库
public interface IWmsReceiveService
{
    Task<WmsReceiveResult> ReceiveAsync(WmsReceiveRequest req);  // 返回 WmsInboundNo + 明细引用
}

// IWmsQcQuery —— 采购→WMS 查 QC 结果（检收基准用）
public interface IWmsQcQuery
{
    Task<QcResult> QueryByReceiptAsync(string wmsInboundNo);     // 合格/不良/待检
}
```

接口由 WMS 模块实现（[08 集成](./08-integration.md)落地）。采购把"收什么、收多少、哪个 PO"打包传给 WMS，WMS 写库存/批次/纸卷、返回入库号；QC 结果也在 WMS（它有 `QcInspection`），采购**查**而不自己判。

> **依赖单向**：采购 `→` 引用 `IWmsReceiveService`/`IWmsQcQuery`，WMS 不反向依赖采购。采购对库存只有"请你入库"和"告诉我 QC 结果"两个动作，没有任何写库存的权力。

---

## 四、双基准分叉（本章核心）

`PostingBasis`（[01](./01-master-data.md)从供应商带出、[02](./02-purchase-order.md)落在 PO）决定收货走哪条路：

### 着荷基准：货到即认

```
建 GR → 调 IWmsReceiveService 入库 → ReceivedQty=AcceptedQty 直接累加到 PO 行
       → GR 状态=完成 → 可建 AP（04）
```
货一到就认，`Received` 和 `Accepted` 同时累加。适合免检物料、信任度高的供应商。

### 检收基准：QC 合格才认

```
建 GR（QcStatus=待检）→ 调 IWmsReceiveService 入库（货在库但待检区）
   → 只累加 ReceivedQty，AcceptedQty 先不动
   → QC 检验（WMS 做）
   → 采购查 IWmsQcQuery：
        ├ 合格 → 累加 AcceptedQty，GR=已检收 → 才能建 AP
        └ 不良 → 累加 RejectedQty，不计 Accepted（退货/让步另处理）
```
货到只算"收到"，**QC 合格才算"验收通过"**，只有 `Accepted` 的量能进三单匹配、能建应付。

```csharp
// CP6.Core/Services/Pur/GoodsReceiptService.cs（分叉核心）
public async Task ConfirmReceiveAsync(GrDto dto, string? user)
{
    var wms = await _wmsReceive.ReceiveAsync(BuildReq(dto));   // ★同步委托 WMS 入库
    gr.WmsInboundNo = wms.InboundNo;

    foreach (var l in gr.Lines)
    {
        l.ReceivedQty = dto.Qty;
        AddPoReceived(l.PoLineNo, dto.Qty);                   // PO 行 ReceivedQty += （锚动）

        if (gr.PostingBasis == "着荷")                        // 着荷：收即认
        {
            l.AcceptedQty = dto.Qty; l.QcStatus = 0;          // 免检
            AddPoAccepted(l.PoLineNo, dto.Qty);
        }
        else                                                  // 检收：待 QC
        {
            l.QcStatus = 1;                                   // 待检，AcceptedQty 先不动
        }
    }
    await _db.SaveChangesAsync();
}

// 检收基准：QC 出结果后回来确认
public async Task ApplyQcResultAsync(string grNo, string? user)
{
    var qc = await _wmsQc.QueryByReceiptAsync(gr.WmsInboundNo); // ★查 WMS QC
    foreach (var l in gr.Lines.Where(x => x.QcStatus == 1))
    {
        if (qc.IsPass(l)) { l.AcceptedQty = l.ReceivedQty - qc.NgQty(l); l.QcStatus = 2;
                            AddPoAccepted(l.PoLineNo, l.AcceptedQty); }   // 合格才累加 Accepted
        else            { l.RejectedQty = qc.NgQty(l); l.QcStatus = 3; }
    }
}
```

---

## 五、锚怎么动（回写 PO 行）

收货/检收时**回写 PO 行的累计量**，PO 状态随之派生（[02 状态机](./02-purchase-order.md)）：

| 动作 | PO 行 | 触发 |
|---|---|---|
| 收货确认 | `ReceivedQty +=` | 着荷&检收都累加 |
| 着荷收货 / 检收 QC 合格 | `AcceptedQty +=` | 决定能否建 AP |
| QC 不良 | （记 `RejectedQty` 在 GR 行） | 不计 Accepted |

回写后调 [02 `DeriveStatus`](./02-purchase-order.md)：`Received` 收齐 → PO=收货完成。**三个锚里的前两个，到这里活了**；第三个 `InvoicedQty` 在 [04](./04-three-way-match.md)/AP 动。

---

## 六、部分收货与超收

- **部分收货**：一张 PO 可多次收货，每次建一张 GR、累加。`ReceivedQty < Qty` → PO=部分收货。
- **超收**：收的比订的多。按供应商容差（[04 `MatchTolerance`](./04-three-way-match.md)）：容差内允许并提示，超容差拒收或挂起。**超收要挡**，否则库存虚高、应付虚增。
- **退货/让步**：QC 不良的量，走退货（通知 WMS 出库退回）或让步接收（特批入库）——这部分与质量流程交叉，本章只负责"记下 `RejectedQty`、不计 Accepted"。

---

## 七、资深视角

**为什么采购坚决不自己写库存？** 因为库存只能有一个真相。采购写一份、WMS 写一份，迟早不一致——到底信谁？让 WMS 当唯一真相，采购只委托，是避免"双写不一致"这个 ERP 头号顽疾的根本办法。采购的 GR 是"采购视角的收货事实"，不是库存账。

**双基准的本质区别？** 着荷基准是"信任 + 效率"（货到就认，省一道），检收基准是"管控 + 质量"（QC 把关才认）。差别就一个：**`AcceptedQty` 在何时累加**——着荷在收货时、检收在 QC 合格时。而 `AcceptedQty` 正是能不能建应付的闸门。所以双基准本质是"何时让供应商的钱变成应付"的策略。

**为什么 QC 在 WMS 不在采购？** 因为 QC 要对着物理货物做（在库、抽样、判定），那是仓储/质量的现场动作，数据天然在 WMS 的 `QcInspection`。采购只需要结果（合格多少），所以是**查**（`IWmsQcQuery`），不重复建一套 QC。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 收货 + 库存过账 | **SAP MM 收货（MIGO / 移动类型 101）** | GR 触发库存过账、更新 PO history |
| 质检放行 | **SAP 质量管理（QM）检验批 + 库存类型** | 待检库存 vs 非限制库存，正是双基准 |
| 收货与库存分离 | **WMS/ERP 集成模式** | ERP 记单据、WMS 记库存，接口对接 |

> SAP 的"检验批 + 质量检验库存"就是检收基准：货到先进"质量检验库存"（待检），合格才转"非限制库存"（可用）——和本章"QC 合格才累加 Accepted"一模一样。

---

## 九、阶段1（收货部分）自检

- [ ] 采购为什么有 GR 单据却不写库存？库存唯一真相在哪？为什么用同步接口？
- [ ] 着荷基准和检收基准，差别就在哪个量何时累加？这个量管什么闸门？
- [ ] `IWmsReceiveService` 和 `IWmsQcQuery` 各干什么？依赖方向是怎样的？
- [ ] 收货怎么回写 PO 行的锚？PO 状态怎么随之变？
- [ ] 超收为什么要挡？QC 不良的量怎么处理？
- [ ] 为什么 QC 在 WMS 而采购只"查"？

全部能答 → 收货链通了，前两个锚（Received/Accepted）会动了。下一步 [04 三单匹配](./04-three-way-match.md)——★MVP 核心：拿 PO、GR（Accepted）、发票三个数 + 价格做容差匹配，通过就同步调财务建应付发票。

---

*实现：新建 `CP6.Entity/DomainModels/Pur/{GoodsReceipt,GoodsReceiptLine}.cs` + `CP6.Core/Services/Pur/GoodsReceiptService.cs`；`IWmsReceiveService`/`IWmsQcQuery` 由 WMS 实现（08 集成）。*
