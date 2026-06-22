# WMS 倉庫管理 · 代码级实现手册

> **这是什么**：把 WMS（倉庫管理，系统最大模块，32 控制器/39 实体）的**每个页面功能**，从前端到后端逐文件、逐行、带真实代码片段和错误码地讲清楚。与 [`docs/codemap-erp/`](../codemap-erp/README.md)、[`docs/codemap-mes/`](../codemap-mes/README.md) 同一套模板，是 [`docs/CODEMAP.md`](../CODEMAP.md) 地图的"放大镜"续篇。
>
> **公共机制不重复**：`http.ts`、实体基类链、`{code,message,data}`、软删除——见 [`codemap-erp/README.md` §0](../codemap-erp/README.md)。本册只讲 WMS 特有的东西，核心是**库存写入铁律**。
>
> **准确性说明**：所有 `文件:行号` 与代码片段实测于 2026-06-22 仓库快照；逐字代码片段是比行号更稳的锚点。

---

## 📖 目录（按业务区）

| # | 区域 | 画面 | 文件 | 看点 |
|---|---|---|---|---|
| 1 | 库存核心 + **铁律** | WM010/020 | [`01-库存核心-铁律.md`](01-库存核心-铁律.md) | `IStockMovementService` 唯一写库路径 + 不变ログ |
| 2 | 入庫 | WM030/040 | [`02-入庫.md`](02-入庫.md) | 完成品入庫(MES接缝③) + 采购GR委托 |
| 3 | 出庫·出荷（**最重，5 接缝**） | WM050/060/070 | [`03-出庫-出荷.md`](03-出庫-出荷.md) | 引当Phase7 / 出荷回写ERP / 材料出庫 / 製品出荷 / 取消级联 |
| 4 | 棚卸·補充·期限·QC | WM080/090+ | [`04-棚卸-補充-期限-QC.md`](04-棚卸-補充-期限-QC.md) | 差异ADJ调整 + FEFO引当 + Lot追溯 |
| 5 | 紙器特化 | WM100~290 | [`05-紙器特化.md`](05-紙器特化.md) | 原紙/インキ/パレット/残材/Kit/Slotting/CrossDock/サンプル/版型 |
| 6 | 業界連携 + 报表 | WM300~330 | [`06-業界連携-报表.md`](06-業界連携-报表.md) | WCS/RF手持/IoT/VMI/RMA(→CreditNote) + Dashboard |

---

## §0 WMS 特有约定（先读这节）

### 0.1 🔒 库存写入铁律（最重要，贯穿全模块）

**`T_Stock` 严禁直接 `_db.Stocks.Update()/Add()`。所有数量变动必经 `IStockMovementService`**，由它在**单事务**内同时完成：①校验/更新 `T_Stock` 三数量 → ②追加 `T_StockTransaction` 不变ログ。

- 接口注释（`IStockMovementService.cs:8-11`）：「仕様書 §13.3 … 直接 _db.Stocks.Update() / Add() を呼ぶことは禁止」。
- 实体双重宣示：`Stock.cs:12`「书込口は StockMovementService のみ」、`StockTransaction.cs:11`「INSERT only。UPDATE/DELETE 禁止。集計レポートはこのテーブルを唯一の真実源とする」。
- 两个核心方法（`StockMovementService.cs`）：`ApplyAsync`（1 件适用）/ `MoveAsync`（棚移动=源 OUT + 先 IN 成对）。
- 六种 `WmsTxnType`（`WmsTxnType.cs:10-24`）经 `ApplyDelta` 分流（`StockMovementService.cs:222-253`）：

| TxnType | 动什么 |
|---|---|
| `IN` | `PhysicalQty +=` |
| `OUT` | `PhysicalQty -=` 且 `AllocatedQty -=`（注释「UNRSV 不要」）|
| `MOVE` | `PhysicalQty +=`（源 Qty<0 / 先 Qty>0）|
| `ADJ` | `PhysicalQty +=` 符号付き差分（棚卸差异/废弃用）|
| `RSV` | `AllocatedQty +=`（引当，PhysicalQty 不变）|
| `UNRSV` | `AllocatedQty -=`（引当解除）|

- **三数量恒等式** `AvailableQty = PhysicalQty - AllocatedQty` 在每次 `ApplyDelta` 末尾强制重算（`:252`），DB 物化但永不手填。
- **负库存守卫**：读 `Warehouse.AllowNegative`，false 时 `PhysicalQty<0 || AvailableQty<0` → 抛 `InsufficientStockException`（`WM-MSG-040`）。
- **唯一例外**：`StockQcService` 改 `Stock.QcStatus` **不经**铁律、不发 Txn——因为 QC 只改属性标志、**三数量不动**，不属「在庫変動」。**铁律 = 数量变动专属**。

### 0.2 T_Stock 业务唯一键 + 特殊标志

业务 UK = **`WarehouseCd + LocationCd + ProductCd + LotNo`** 四列（`Stock.cs:18-32`，无批管理时 `LotNo=""` 非 null）。三数量 `PhysicalQty/AllocatedQty/AvailableQty`（`decimal(21,8)`）。特殊标志：`ExpiryDate?`(FEFO)/`RecallFlag`(召回禁出)/`OwnerType`(SELF/CUSTOMER VMI)/`OwnerCd`/`PaperRollNo`/`QcStatus`(PENDING/PASSED/FAILED/HOLD，仅前两者可引当)。

### 0.3 采番 WmsSequenceService

`NextAsync(prefix)` → `{prefix}{yyyyMM}{NNNN}`，**全期间累计、跨月不归零**。主线前缀：`IN`(入庫指示)/`RC`(入庫実績)/`OUT`(出庫指示)/`PKG`(梱包)/`TXN`(库存流水)/`ST`(棚卸)/`SHIP`(运送便)；紙器特化：`ROLL/INK/PLT(パレット)/REM/KIT/SLP/XD/SMP/PLT2(版型,因PLT被占)`。

### 0.4 ⚠️ 错误码体系（WM-MSG-xxx，内联字面量，未入 i18n）

WMS 用 `WM-MSG-NNN` 码，但**全是 Service 内联字符串字面量**（如 `throw new InvalidOperationException("WM-MSG-043: ...")`），直接当 `message` 回前端，**未在任何 i18n Seed 表登记**（`CP6.WebApi/Seed` grep 无匹配）→ 前端 `ElMessage.error(res.message)` 会**裸码显示** `WM-MSG-xxx`。

| 现象 | 实情 |
|---|---|
| 通用码 | `WM-MSG-070`(数据不存在/404) `WM-MSG-071`(成功) `WM-MSG-043`(状态守卫,后接日文说明) `WM-MSG-001`(重复) `WM-MSG-020`(明细0件) `WM-MSG-021`(数量>0) `WM-MSG-031`(数量≠0) `WM-MSG-040`(库存不足) |
| 专用码 | `WM-MSG-072`(乐观锁409) `WM-MSG-QC-001/404`(QC) `WM-MSG-SHORTAGE-409`(缺料重复处理) `WM-MSG-RMA-404` `WM-MSG-202/203`(原紙) `WM-MSG-060/061`(棚卸) `WM-MSG-102`(QC判定理由) `WM-MSG-303/300/301/302`(RF手持) |
| 无前缀 | 部分校验用裸日文/英文 message（如「出庫倉庫CDは必須です」「deviceCd required」） |

> ⚠️ `WM-MSG-203`（原紙巾割子巾超亲巾）连需求文档都没文案，仅代码内用。

### 0.5 主数据 CRUD 无 Service 层

倉庫/Location（`WarehouseController`）**直接注入 `CP6Context` 操作 DbContext**，无 Service。与「库存必经 Service」形成对照：**铁律只约束库存数量变动，主数据 CRUD 走 Controller 直操**。倉庫/Location Update 用 `RowVersion` 乐观锁（冲突→409 `WM-MSG-072`）。

### 0.6 实时与后台

- **SignalR** `/hubs/wms`（`WmsHub`），`IWmsNotifier`→`SignalRWmsNotifier` 在每笔库存变动事务提交后 best-effort 推 `StockChanged`/`InboundReceived`/`OutboundShipped`（三经路：All + `wh:{仓}` + `product:{品}`）。唯一前端消费者是 `WmsDashboardView`。缺料告警 `MaterialShortageDetected` 走 `MaterialShortageNotifier` **反射**推送（Core 不引 SignalR 程序集）。
- ⚠️ **IoT 告警是 30 秒轮询**（`IotMonitorView` `setInterval`），**不走 SignalR**。
- **Dashboard 用 EF Core**（注释明示 Dapper 仅"将来移行"），与 MES Dashboard 的 Dapper/SP 版不同。

---

## §1 WMS 主链 + 跨模块接缝全景

WMS 是 ERP→MES→WMS 闭环的末段，接缝最多。**所有真增/减库存最终都汇聚到 `IStockMovementService`**：

```
【ERP 受注作成】──接缝④──► CreateFromOrderAsync → 製品出荷指示(OutboundType=Shipping, 仓W01)
【MES 指図発行】──接缝③──► CreateFromWorkOrderAsync → 材料出庫指示(OutboundType=Material, 仓W01)
【MES 全工程完了】─接缝(入)► CreateFinishedGoodsFromWorkOrderAsync → 完成品入庫(幂等 WM-MSG-043, 仓W01/W01-FG)
【MES QC NG】──接缝(QC)──► StockQcService.MarkLinkedStockByWorkOrder(FAILED)
                                    │
        ┌───────────────────────────┴── 出庫流程（OutboundService 三态合一）─────────────┐
        │  Draft → Confirm → Allocate ──接缝①──► FEFO+QC过滤引当(RSV)；材料不足→MaterialShortage(不抛)│
        │                    → Picking(拣货,行级不落库) → Ship ──接缝②──► OnShipmentConfirmed         │
        │                                                          └→【ERP 受注 ShippedQty 回写】     │
        │  受注取消 ──接缝⑤──► CancelOrderAsync(UNRSV 解引当, 反向级联先Outbound后WO)                  │
        └────────────────────────────────────────────────────────────────────────────────────┘
【WMS RMA クローズ】──接缝(出)► OnReturnConfirmedAsync →【ERP CreditNote + OrderDetail.ReturnedQty】
```

**五个出庫接缝 + 三个其它接缝**（全部 best-effort + `IntegrationEvent` 持久化）：

| 接缝 | 方向 | 触发 | 动作 |
|---|---|---|---|
| ① 引当 Phase7 | WMS 内 | `AllocateAsync` | `FindCandidateStockAsync` 用 `QcStatus∉{FAILED,HOLD}` + FEFO 过滤；材料不足写 `MaterialShortage` 不抛、非材料抛异常 |
| ② 出荷回写 | WMS→ERP | `ShipAsync`(Shipping+WebOrderNo) | `OnShipmentConfirmedAsync` 按製品CD充当 `OrderDetail.ShippedQty/ShipStatus` |
| ③ 材料出庫 | MES→WMS | `WorkOrder.IssueAsync` | `CreateFromWorkOrderAsync` 把指図材料→Material 出庫指示 |
| ④ 製品出荷 | ERP→WMS | `Order.CreateAsync` | `CreateFromOrderAsync` 把受注明细→Shipping 出庫指示 |
| ⑤ 取消级联 | ERP→WMS | 受注取消 | `CancelOrderAsync` UNRSV 解引当(仅 Status<Picking 自动) |
| (入) 完成品入庫 | MES→WMS | 全工程完了 | `CreateFinishedGoodsFromWorkOrderAsync`(幂等) |
| (QC) NG 阻出 | MES→WMS | QC 判 NG | `MarkLinkedStockByWorkOrder(FAILED)` |
| (出) RMA回写 | WMS→ERP | RMA クローズ | `OnReturnConfirmedAsync` 生成 CreditNote + ReturnedQty |

> 接缝③④⑤(入)(QC) 的 MES/ERP 侧触发点见 [codemap-mes](../codemap-mes/README.md) 与 [codemap-erp](../codemap-erp/05-受注-order.md)。

---

## §2 FEFO 引当（先过期先出）— 全模块共享的核心算法

真正的 FEFO 在 `OutboundService.FindCandidateStockAsync`（`:325-340`），三级排序：
```csharp
.OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)     // 1. 賞味期限最早先出（FEFO）
.ThenBy(s => s.ReceiveDate ?? DateTime.MaxValue)     // 2. 同期限则受入日早先出（退化 FIFO）
.ThenBy(s => s.LotNo)                                // 3. 再相同则 LotNo 字典序（确定性）
```
配合过滤 `!RecallFlag / AvailableQty>=needed / OwnerType==Self / QcStatus∉{Failed,Hold}`。补充源选择（`ReplenishService`）、Kit 扣料（`KittingService`）用同向简化 FEFO。`ExpiryService` 只做近效期查询 + 一括废弃（ADJ -全数），不含引当。

---

*生成于 2026-06-22。基于 6 个并行勘察 agent 对真实源码的逐行核对。续 [codemap-erp](../codemap-erp/README.md) / [codemap-mes](../codemap-mes/README.md)。至此 ERP+MES+WMS 三大子系统代码级手册齐备。*
