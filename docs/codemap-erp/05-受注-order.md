# 05 · 受注 Order（PA070/080/090）— 主线核心

> 先读 [`README.md` §0 公共约定](README.md) 与 [§1 主链](README.md)。全部 `文件:行号` 与代码片段 2026-06-22 实测、逐字引用。

## 0. 架构定位

- **5 表 1-shot**：1 受注 = `T_Order`(头) + `T_OrderDetail`(明细) + `T_OrderProcess`(工程) + `T_OrderProcessNote`(工程备考) + `T_OrderMaterial`(材料)。子表更新「全削除→全挿入」，乐观锁由親 `Order.RowVersion` 担当（`OrderService.cs:17` remarks）。
- 前端 **3-Step 向导** `OrderEntryView.vue`（Step1 头+明细 / Step2 基本信息 / Step3 工程材料），状态在 Pinia `useOrderStore`，最终保存提交 1 个 `OrderDto`。
- **跨模块联动经 Bridge Hook**（分层保持，ERP 只依赖 interface）：作成触发 MES/WMS，取消反向级联，出荷确定回写 ERP。采番 `ORD`。
- ⚠️ **取消(cancel)与删除(delete)是两个不同 API、不同状态机**：取消从列表页起动；删除是 `OrderEntryView` 的 `onDelete`（软删 DELETE）。

---

## 新建受注 — POST /api/orders

**页面入口**：`/order`（`router/index.ts:69` → `OrderEntryView.vue`；动态路由 `addDynamicRoutes` `:229-244`；popup 版 `/order/window` `:190-195`）。

**前端**
- view 切 3-Step（`OrderEntryView.vue:56-58`）。保存 `onSave`（`:188-225`）：
```js
const dto = store.buildDto()
if (store.isNew) {
  const res = await orderApi.create(dto)
  if (res.code === 0 && res.data) {
    store.loadFromDto(res.data)
    store.setOperationType(OrderOperationType.Edit)
  }
}
```
保存前 `validateAll`（`:177-186`）：customerCd/orderType 必填(E10022)、details 非空(E10009)、各行 productCd 必填(E10022)。与信超过时 `ElMessageBox.confirm` 续行确认（`:191-201`，W 警告）。
- Step1（`Step1HeaderAndDetails.vue`）：头直接 v-model `store.order`（受注区分选项 10~90 `:13-21`）；明细 `store.addDetail()`；製品 picker `onPickedProduct` 把製品マスタ 63 字段 `Object.assign(row, res.data)` 引入（`:251-274`）；金额实时算 `onQtyOrPriceChange`（`:242-249`）：
```js
const price = store.order.salesPriceDiv === '1' ? (row.individualUnitPrice ?? 0) : (row.setUnitPrice ?? 0)
row.amount = qty * price
```
- store（`stores/order.ts`）：`emptyOrder()`/`emptyOrderDetail(rowNo)` 工厂；`addDetail(seed)`（`:134-145`）按 `Math.max(...)+1` 采 rowNo；`buildDto()`（`:110-115`）`{ ...order.value, details:[...order.value.details] }`；`loadFromDto`（`:99-107`）展开返却、`currentDetailIndex=0`、`isDirty=false`。
- api `create`（`order.ts:40-42`）。type `OrderDto`（`types/erp/order.ts:15-42`，头+`details:OrderDetailDto[]`+`rowVersion`），`OrderDetailDto` 约 130 字段，内含 `processes/processNotes/materials` 子表。

**后端**
- Controller `Create`（`OrderController.cs:260-274`）：`CreateAsync` → `GetByWebOrderNoAsync` 回查；`InvalidOperationException→400`。`[Authorize]`，`CurrentUser=>User?.Identity?.Name`。
- Service `CreateAsync`（`OrderService.cs:104-234`）核心步骤：
  1. 入力校验（明细非空/上限 500/customerCd/orderType，`:106-113`）。
  2. 客先納期 vs LT 合計（`isEditable=false` 时各明细 `CheckDeliveryLeadTimeAsync`，`:116-126`）。
  3. **采番**（`:128`）`var webOrderNo = await NextSequenceAsync();`（→ `DocNumber.NextAsync(db,"ORD")`，13 桁）。
  4. **冻结汇率**（`:132-138`）：
```csharp
var currencyCd = FxConstants.BaseCurrency;
var fxRate = 1m;
if (_fxRate != null)
    (currencyCd, fxRate) = await _fxRate.ResolveForCustomerAsync(dto.CustomerCd, orderDate);
```
  5. 建头（`:141-170`）`Status=0`(未転送)、`McOrderNo=null`、`McTransferFlg=false`、凍結 `CurrencyCd/FxRate`、`Add(header)`。
  6. 建 3+1 子表（`:173-223`）：明细内 detailNo 采番、手配NO 初期化（`HaibaiNo1 ??= $"{webOrderNo}-{detailNo:D3}"` 等 `:180-183`）、`DtoToDetail` 实体化；工程 OperationCd/ProcessCd 空跳过，备考 OperationCd 空跳过，材料 MaterialCd 空跳过。
  7. `SaveChangesAsync`（`:225`）。
  8. **Hook 触发**（`:228-231`）：
```csharp
// WM-3.5：WMS 自動展開（best-effort、失敗しても受注作成は成功）
await _wmsBridge.OnOrderCreatedAsync(webOrderNo, userName);
// Phase1：MES 製造指図 自動展開（既定無効・MesBridge:Enabled=true で有効化、best-effort）
await _mesBridge.OnOrderCreatedAsync(webOrderNo, userName);
return webOrderNo;
```
- 实体 `Order`（`T_Order`，`: BaseBizEntity`，业务 PK `WebOrderNo`，导航 `List<OrderDetail> Details`）；`OrderDetail`(PK `WebOrderNo,WebOrderDetailNo`)/`OrderProcess`/`OrderProcessNote`/`OrderMaterial`。DTO `OrderDto`（`CurrencyCd/FxRate` 服务端冻结只读 `:60-64`）。

**校验与错误码**（CreateAsync **无码平文异常** → Controller 400）：
| 行 | 消息 |
|---|---|
| `:107` | `"登録する明細がありません。"` |
| `:109` | `"明細行は {MaxDetailLimit} 件までです（…）"`（上限 500 `:31`） |
| `:111` | `"得意先 CD は必須です。"` |
| `:113` | `"受注区分は必須です。"` |
| `:124` | `"明細 {No} ({ProductCd}): {msg}"`（LT 不足） |
前端 `validateAll` 用 i18n 键 `sales.err.E10022`/`sales.err.E10009`。

**⭐跨模块联动专讲**（核心）
CreateAsync 在 SaveChanges 后 **best-effort** 顺次呼 2 Hook（`:228-231`），两 Hook 均 `BridgeHookBase` 继承、`IntegrationEvent` 持久化（corrId 串联）。
1. **WMS** `WmsBridgeHook.OnOrderCreatedAsync`（`WmsBridgeHook.cs:58-84`）：
```csharp
var no = await _outbound.CreateFromOrderAsync(webOrderNo, userName);   // 受注→出荷指示 OutboundOrder
await PersistEventAsync("ERP","WMS",nameof(OnOrderCreatedAsync), webOrderNo, no, IntegrationEventStatus.Success, ...);
return WmsBridgeResult.Ok(no);
```
业务错误(`InvalidOperationException`)转 Skipped，不让受注失败。
2. **MES** `MesBridgeHook.OnOrderCreatedAsync`（`MesBridgeHook.cs:26-57`）：
```csharp
var nos = await _woService.ExpandFromOrderAsync(new ExpandFromOrderRequest { WebOrderNo = webOrderNo }, userName);
```
受注全明细→製造指図 WorkOrder。既定无效（`MesBridge:Enabled=true` 才有效），未注入时 `NoOpMesBridgeHook`（`OrderService.cs:44`）。

**数据流**：`onSave` → `buildDto()` → `create` → `CreateAsync`（采番→冻结→建头+3子表→SaveChanges）→ `WmsBridge`(出荷指示) → `MesBridge`(製造指図) → 回查 → `loadFromDto`。

---

## 訂正(更新) — PUT /api/orders/{webOrderNo}

**前端**：`onSave` Edit 分支（`OrderEntryView.vue:213-218`）`orderApi.update(no, dto)`。既存读込 `onLoad`（`:124-149`）：searchNo→`getByWebOrderNo` / searchHaibaiNo1→`lookupByHaibaiNo`，成功 `loadFromDto`+`setOperationType(Edit)`，`rowVersion` 随 DTO 往返。api `update`（`order.ts:44-46`）。
**后端**：Controller `Update`（`:277-303`）catch `KeyNotFound→404`、`DbUpdateConcurrency→409 W10002(msgId MSG-W10002)`、`InvalidOperation→400`。Service `UpdateAsync`（`OrderService.cs:240-334`）：
```csharp
var entity = await _db.Orders.FirstOrDefaultAsync(x => x.WebOrderNo == webOrderNo && !x.IsDeleted)
    ?? throw new KeyNotFoundException($"受注 '{webOrderNo}' が見つかりません。");
if (dto.RowVersion != null && dto.RowVersion.Length > 0)
    _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = dto.RowVersion;
```
头更新（mc 键/Status 不变，`McTransferFlg=false` `:265`）；**子表全删全插**（`:272-331`）：
```csharp
_db.OrderProcesses.RemoveRange(oldProcesses);
_db.OrderProcessNotes.RemoveRange(oldNotes);
_db.OrderMaterials.RemoveRange(oldMaterials);
_db.OrderDetails.RemoveRange(oldDetails);
// 之后 detailNo 再采番再 Add
```
`SaveChangesAsync`（`:333`）。**訂正不触发 Hook**（Create 专用）。
校验码：409 `W10002`（乐观锁）、404。

---

## 列表检索 — GET /api/orders/list

**前端**：`/order-list`（`router/index.ts:70` → `OrderListView.vue`）。`search`（`:260-272`）→ `orderApi.searchList(query)`，0 件 `info(E10008)`。详情遷移 `goDetail`→`/order?webOrderNo=...`。CSV `exportCsv`。api `searchList`/`exportListCsv`（`order.ts:149-162`，`paramsSerializer:{indexes:null}`）。
**后端**：Controller `SearchList`（`OrderController.cs:159-168`）：`ValidateFromTo`（受注日/纳期 FROM≤TO，`E10036`）→ `SearchOrdersAsync` → `{rows,total}`。Service `SearchOrdersAsync`（`OrderService.cs:746-769`）：`BuildOrderListQuery`（`:1137-1217`，40+ 条件）→ Count → `QuerySort.Apply`（白名单 `OrderSortMap`，默认 HaibaiNo2→HaibaiNo1→…）→ `ProjectListAsync`（`:1219-1276`，明细粒度，构成列合成 + 通貨/FxRate 从头补完）→ 分页；`MaxRows` 超 → 截断(`E10013`)。
校验码：`E10036`(FROM≤TO `:342`)、`E10013`(截断 `:757`)、前端 `sales.err.E10008`(0 件)。

---

## 详情加载 — GET /api/orders/{webOrderNo}

**前端**：`OrderEntryView.onLoad`（searchNo 经由 `:127-133`）或一覧 `goDetail`。api `getByWebOrderNo`（`order.ts:33-38`），读后 `store.loadFromDto`。
**后端**：Controller `Get`（`OrderController.cs:251-258`）null→404。Service `GetByWebOrderNoAsync`（`OrderService.cs:53-98`）：header→details→processes→notes→materials 各 `AsNoTracking`，`HeaderToDto`+逐明细 `DetailToDto`，子表按 `WebOrderNo+WebOrderDetailNo+ProductCd` 紐付：
```csharp
ddto.Processes = processes
    .Where(p => p.WebOrderNo == d.WebOrderNo && p.WebOrderDetailNo == d.WebOrderDetailNo && p.ProductCd == d.ProductCd)
    .Select(ProcessToDto).ToList();
```
`DetailToDto` 还载 `ShippedQty/ShipStatus/LastShipDate/LastOutboundNo`（出荷实绩 `:1385-1388`）。校验码 `E10008`。

---

## ⭐ 取消 — POST /api/orders/{webOrderNo}/cancel（force=false 探查 / force=true 实施 二段）

**前端**：`OrderListView` 行「取消」（`:134-141`）→ `openCancelDialog(row)` → `OrderCancelDialog.vue`（3 步状态机 `step:'input'|'decision'|'done'` `:153`）。
第1段探查 `onProbe`（`:168-192`）`orderApi.cancel(no, reason, false)`：`outcome` 为 `Cancelled/PartiallyCancelled`→done；`NeedsDecision`→decision（展示关联 WO/Outbound 与 `autoCancellable`）；`Rejected`→done。
第2段实施 `onForceConfirm`（`:194-210`）`orderApi.cancel(no, reason, true)`→done。api `cancel`（`order.ts:67-72`，body `{reason, force}`）。
**后端**
- Controller `Cancel`（`OrderController.cs:360-370`）：`reason` 空→`BadRequest`；`OrderCancelRequest{Reason必填, Force默认false}`。
- Service `CancelAsync`（`OrderService.cs:382-439`）状态机闸门（`:396-405`）：
```csharp
if (order.OrderStatus == OrderLifecycleStatus.Cancelled)
    return OrderCancelResult.Rejected(corr, "PA-MSG-CANCEL-001: 既に取消済");
if (order.OrderStatus == OrderLifecycleStatus.Shipped)
    return OrderCancelResult.Rejected(corr, "PA-MSG-CANCEL-002: 出荷済の受注は取消不可");
if (order.ShipStatus >= 5)
    return OrderCancelResult.Rejected(corr, $"PA-MSG-CANCEL-003: 出荷実績あり (ShipStatus={order.ShipStatus}) — 取消不可");
```
1 段探查（`:408-418`）：
```csharp
var probe = await _cancelBridge.OnOrderCancelledAsync(webOrderNo, force: false, userName, correlationId);
var allAutoCancellable = probe.WorkOrders.All(w => w.AutoCancellable) && probe.Outbounds.All(o => o.AutoCancellable);
if (!force && !allAutoCancellable)
    return OrderCancelResult.NeedsDecision(correlationId, probe.WorkOrders, probe.Outbounds);
```
2 段实施（`:421-438`）：
```csharp
var cascade = await _cancelBridge.OnOrderCancelledAsync(webOrderNo, force: true, userName, correlationId);
order.OrderStatus = cascade.FullyCascaded ? OrderLifecycleStatus.Cancelled : OrderLifecycleStatus.PartiallyCancelled;
order.CancelledAt = now; order.CancelReason = reason;
await _db.SaveChangesAsync();
```

**⭐反向级联专讲** `OrderCancelBridgeHook.OnOrderCancelledAsync`（`OrderCancelBridgeHook.cs:42-152`）。**取消顺序依赖关系上重要**（`:16-21`）：① 受注紐付 Outbound（出荷指示）→ ② WO → ③ WO 紐付 Outbound（材料出庫），此序防在庫(Allocated/Physical)二重解除。
- 探查（`:45-93`）：WO `AutoCancellable = WorkOrderStatus.IsCancellable(w.Status)`（`:55`；`IsCancellable` 仅 Draft0/Confirmed1/Issued2 true，InProgress 以后不可 `WorkOrder.cs:116-117`）；Outbound `AutoCancellable = o.Status < OutboundOrderStatus.Picking`（`:74`；Picking=3 `WmsTxnType.cs:88`，仅 ≤2 可）；force=false 则 DB 未变直接返 probe。
- 实施 force=true（`:95-152`）：
```csharp
// Step 1: OutboundOrder を先に取消（RSV 解除を含む）
foreach (var probe in outProbes.Where(p => p.AutoCancellable)) {
    try { await _outboundService.CancelOrderAsync(probe.OutboundNo, userName); probe.Cancelled = true; }
    catch (Exception ex) { probe.Cancelled = false; probe.Message = $"FAILED: {ex.Message}"; }
}
// Step 2: WorkOrder を取消（RSV は OutboundOrder.CancelOrderAsync が既に解除済）
foreach (var probe in woProbes.Where(p => p.AutoCancellable)) {
    var ok = await _woService.CancelAsync(probe.WorkOrderNo, "受注取消連動", userName);
    probe.Cancelled = ok;
}
```
`fullyCascaded` = 全 AutoCancellable 项 Cancelled=true 且 allAutoCancellable（`:139-145`）。配置无效时 `NoOpOrderCancelBridgeHook`（`OrderCancelBridge:Enabled=false`）。

**校验与错误码（grep）**：`PA-MSG-CANCEL-404`（受注不存在 `:393`）、`-001`（已取消 `:397`）、`-002`（出荷済 `:400`）、`-003`（ShipStatus≥5 `:405`）；`"取消理由（reason）は必須です"`（Controller `:365`）。
**数据流**：`onProbe(false)` → `CancelAsync`(闸门→Bridge探查) →（NeedsDecision→decision）→ `onForceConfirm(true)` → `CancelAsync`(Bridge实施: Outbound→WO→OrderStatus→SaveChanges) → done。

---

## 単価订正 — GET price-correction/list ＋ PUT price-correction/batch（PA090）

**前端**：`/order-price-correction`（`router/index.ts:71` → `OrderPriceCorrectionView.vue`，baseCd 必填 `:6`）。`search`（`:156-172`）；选中行才可编辑单价单元格（`isRowSelected` 控 `:disabled`）。`onSubmit`（`:184-220`）`batchUpdatePrice({ items: 选中行映射 after价/specialPriceFlg/priceChangeReason/rowVersion })`，结果 `更新:{updated} / WF起票:{wf}`，`conflictedKeys` 有则 warning。api `searchPriceCorrection`/`batchUpdatePrice`（`order.ts:166-178`）。
**后端**
- Controller 检索（`OrderController.cs:195-214`）FROM≤TO + 数量/金额 FROM≤TO（`E10036`）；批量（`:227-244`）catch `DbUpdateConcurrency→409 W10002`。
- Service 检索 `SearchPriceCorrectionsAsync`（`OrderService.cs:814-929`）：baseCd 必填(`E10022`)、件数上限(`E10013`)、`...After` 初值=Before。
- Service 批量 `BatchUpdatePriceAsync`（`:931-1000`）：
```csharp
if (item.RowVersion != null && item.RowVersion.Length > 0)
    _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = item.RowVersion;
var priceChanged = item.IndividualUnitPriceAfter != entity.IndividualUnitPrice
    || item.SetUnitPriceAfter != entity.SetUnitPrice
    || item.SpecialPriceFlg != entity.SpecialPriceFlg;
entity.ProvisionalPriceFlg = false;   // 本単価に確定
entity.Amount = (entity.Quantity ?? 0m) * (entity.SalesPriceDiv == "1"
    ? (entity.IndividualUnitPrice ?? 0m) : (entity.SetUnitPrice ?? 0m));
if (priceChanged) {
    entity.ApprovalStatus = 1;        // 承認依頼中
    entity.WfApprovalFlg = false;
    result.WfRequestedCount++;
    await PostPowerEggWorkflowAsync(entity, userName);   // POWER EGG WF 起票
}
```
不存在明细加入 `ConflictedKeys`；セット単価一括(NO.1') 经 `headerSetUnitPriceMap` `ExecuteUpdateAsync` 同一 WebOrderNo 全明细。
校验码：`E10022`(拠点未指定 `:817`)、`E10013`(件数 `:863`)、`E10036`(数量/金额 FROM≤TO)、`W10002`(409)。
> 単価订正只触发 POWER EGG WF，不触发 MES/WMS Hook。

---

## ⭐ 出荷実績回写专讲 — ErpBridgeHook.OnShipmentConfirmedAsync（WMS → ERP 逆方向）

**触发元**：WMS `OutboundService.ShipAsync`（出荷确定）成功后 best-effort（`OutboundService.cs:524-529`）：
```csharp
if (header.OutboundType == OutboundType.Shipping && !string.IsNullOrWhiteSpace(header.WebOrderNo)) {
    try { await _erpBridge.OnShipmentConfirmedAsync(outboundNo, userName); }
    catch { /* best-effort：回写失敗は出荷確定を失敗させない */ }
}
```
**实现** `ErpBridgeHook.OnShipmentConfirmedAsync`（`ErpBridgeHook.cs:26-134`）。Skip 条件：出庫不存在 / OutboundType≠Shipping or WebOrderNo 无 / 无出荷数明细 / 受注不存在（各 Skipped+PersistEvent）。
回写核心（`:82-109`）—— **出庫明细按製品CD向受注明细顺次充当**（同製品多行从未充足行开始填）：
```csharp
foreach (var grp in shippedLines.GroupBy(s => s.ProductCd)) {
    var remaining = grp.Sum(s => s.ShippedQty);
    var targets = orderDetails.Where(d => d.ProductCd == grp.Key).OrderBy(d => d.WebOrderDetailNo).ToList();
    if (targets.Count == 0) continue;
    foreach (var od in targets) {
        if (remaining <= 0) break;
        var ordered = od.Quantity ?? 0m;
        var already = od.ShippedQty ?? 0m;
        var capacity = ordered > 0 ? ordered - already : remaining;
        var apply = capacity > 0 ? Math.Min(remaining, capacity) : remaining;
        if (apply <= 0) continue;
        od.ShippedQty = already + apply;
        od.ShipStatus = (ordered > 0 && od.ShippedQty >= ordered) ? 9 : 5;   // 9=出荷済 / 5=一部
        od.LastShipDate = now; od.LastOutboundNo = outboundNo;
        remaining -= apply;
    }
}
```
头 roll-up（`:111-119`）：`order.ShipStatus = allShipped ? 9 : (anyShipped ? 5 : 既存)`，`order.ActualShipDate = now`，`SaveChangesAsync`。**幂等由 `ShippedQty` 累计担保**（防同一出庫二重反映）。
> 回写字段：`OrderDetail.ShippedQty/ShipStatus/LastShipDate/LastOutboundNo`、头 `Order.ShipStatus/ActualShipDate`。**这个 `ShipStatus>=5` 又驱动受注取消的闸门**（`CancelAsync:403` 的 `PA-MSG-CANCEL-003`）——形成完整闭环。
> 同 Hook 还有 `OnReturnConfirmedAsync`（RMA 返品→`OrderDetail.ReturnedQty`+CreditNote，`:136-232`）。配置无效时 `NoOpErpBridgeHook`（`ErpBridge:Enabled=false`）。

**数据流**：WMS `ShipAsync`(出荷确定 SaveChanges) → `ErpBridge.OnShipmentConfirmedAsync` → 出庫明细解决 → 按製品CD充当 → `OrderDetail.ShippedQty/ShipStatus` → `Order.ShipStatus` roll-up → SaveChanges → IntegrationEvent 持久化。

---

## 涉及文件清单

| 层 | 文件 | 角色 |
|---|---|---|
| FE view | `cp6.web/src/views/erp/OrderEntryView.vue` | PA070 3-Step 壳、onSave/onLoad |
| FE view | `cp6.web/src/views/erp/order/Step1HeaderAndDetails.vue` | 头+明细、製品引入、金额计算 |
| FE view | `cp6.web/src/views/erp/order/Step2BasicInfo.vue` · `Step3ProcessInfo.vue` | 第2/3 画面（经 `store.currentDetail` 编辑子表，`stores/order.ts:202-218`） |
| FE view | `cp6.web/src/views/erp/OrderListView.vue` | PA080 一覧、CSV、取消 dialog mount |
| FE view | `cp6.web/src/views/erp/OrderPriceCorrectionView.vue` | PA090 単価订正 |
| FE dialog | `cp6.web/src/views/erp/OrderCancelDialog.vue` | 取消二段状态机 |
| FE store | `cp6.web/src/stores/order.ts` | buildDto/loadFromDto/addDetail |
| FE api | `cp6.web/src/api/erp/order.ts` | create/update/cancel/searchList/batchUpdatePrice |
| FE type | `cp6.web/src/types/erp/order.ts` | OrderDto/OrderDetailDto/OrderOperationType |
| FE router | `cp6.web/src/router/index.ts` | `/order`:69、`/order-list`:70、`/order-price-correction`:71、`/order/window`:190 |
| BE Controller | `CP6.WebApi/Controllers/Erp/OrderController.cs` | 全 action + 各 Request DTO |
| BE Service | `CP6.Core/Services/Erp/OrderService.cs` | Create/Update/Delete/Cancel/Search*/BatchUpdatePrice/mapper |
| 实体 | `CP6.Entity/DomainModels/Erp/Order.cs`+`OrderDetail`+`OrderProcess`+`OrderProcessNote`+`OrderMaterial` | `T_Order` 5 表 |
| DTO | `CP6.Entity/DTOs/Erp/OrderDto.cs` · `OrderCancelDto.cs` | OrderDto / CancelOutcome/Result/Probe |
| 联动 IF | `Services/Integration/IMesBridgeHook.cs`·`IWmsBridgeHook.cs`·`IOrderCancelBridgeHook.cs`·`IErpBridgeHook.cs` | 4 接口 + NoOp |
| 联动 impl | `Services/Mes/MesBridgeHook.cs`·`Services/Wms/WmsBridgeHook.cs`·`Services/Integration/OrderCancelBridgeHook.cs`·`Services/Wms/ErpBridgeHook.cs` | 展开/级联/**出荷回写** |
| 联动 触发点 | `Services/Wms/OutboundService.cs` | `ShipAsync:524-529` 呼 ErpBridge |
| 采番 | `Services/Common/DocNumber.cs` | `NextAsync(db,"ORD")` 13 桁 |
| 状态常量 | `DomainModels/Mes/WorkOrder.cs`(IsCancellable:116) · `DomainModels/Wms/WmsTxnType.cs`(Picking=3:88) | 取消判定依据 |

## 关键发现
1. **取消≠删除**：取消(cancel)从一覧起动、有状态机+反向级联；删除(delete)是 `OrderEntryView.onDelete` 软删。
2. **訂正不触发 Hook**：仅 Create 触发 MES/WMS；単価订正仅触发 POWER EGG WF。
3. **错误码混合**：Create 入力=平文无码、列表/単价=`E10013/E10022/E10036`、取消=`PA-MSG-CANCEL-001~003/404`、乐观锁=`W10002`。**`E-PA` 前缀全模块未发现**。
4. **出荷回写充当逻辑**：按製品CD GroupBy，未充足顺充当；`ShipStatus` 由 `ShippedQty>=Quantity` 判 9/5，又驱动取消闸门（闭环）。
5. **Step2/Step3 两文件**未逐行精读（结构同 Step1，经 `store.currentDetail` + `setCurrentDetailProcesses/Materials/Notes` 编辑子表）；需逐行可再深挖。
