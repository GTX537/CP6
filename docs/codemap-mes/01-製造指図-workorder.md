# 01 · 製造指図 WorkOrder（ME020/030）— MES 枢纽

> 先读 [`README.md` §0/§1](README.md) 与 [`codemap-erp/README.md` §0](../codemap-erp/README.md)。全部 `文件:行号` 与代码片段 2026-06-22 实测、逐字引用。

## 0. 架构定位

- **三表一体**：1 指図头 `T_WorkOrder` → N 工程 `T_WorkOrderProcess` → N 材料 `T_WorkOrderMaterial`，一事务写入。
- **枢纽地位**：上游受注经 `MesBridgeHook.ExpandFromOrderAsync` 自动展开成它（接缝①）；它的发行 `IssueAsync` 触发 WMS 材料出庫（接缝②）；全工程完了触发 WMS 完成品入庫（接缝③，详见 [02 製造実績](02-製造実績-productionresult.md)）。
- **采番** `MesSequenceService.NextAsync("WO")` → `WO{yyyyMM}{NNNN}`（注意实体注释写 `WOYYYYMMDD-NNNN` 是错的，见 README §0.3）。
- **状态机** 见 [README §1](README.md)；可编辑/删除/发行仅 `Status∈{0,1}`。
- 路由 `/mes/work-order`→`WorkOrderEntryView.vue`（`router/index.ts:87`）、`/mes/work-order-list`→`WorkOrderListView.vue`（`:88`）。

---

## 列表检索 — GET /api/mes/work-orders

**前端**：`WorkOrderListView.vue` 检索表单（`:4-59`，含状态多选/遅延のみ），`search()`（`:198-207`）→ `workOrderApi.search(query)`。无 store。api（`api/mes/mes.ts:51-56`，`paramsSerializer:{indexes:null}` 让 `statuses[]` 序列化为 `statuses=0&statuses=1`）。type `WorkOrderSearchQuery`（`types/mes/mes.ts:127-144`）。
**后端**：Controller `Search`（`WorkOrderController.cs:31-36`）。Service `SearchAsync`（`WorkOrderService.cs:58-140`）：动态条件（`OrderNo` 命中 `OrderNo1/2/3/WebOrderNo` 任一 `:64-68`；`DelayedOnly`=`PlanEndDate<today && Status∉{4,6,9}` `:78-82`；工程CD/WG 走子表 join 反查 `:85-92`）；分页 size 上限 500；**进度批量化**——一次 GroupBy 取每 WO 工程总数/完成数（`:106-115`），逐行算 `ProgressRate=CompletedQty/ProductionQty*100`、`DelayDays`。无错误码。

---

## 新建(手动) — POST /api/mes/work-orders

**前端**：`/mes/work-order` 无 `?no=` = 新建。`WorkOrderEntryView.vue` 3 步向导（el-steps `:26-30`，Step1 基本/Step2 工程/Step3 材料）。`onSave()` 新建分支（`:193-211`）→ `workOrderApi.create(form)` → `loadByNo(no)` 回填。api `create`（`mes.ts:64-66`）。提交整个 `WorkOrderDto`（子表 `processes[]`/`materials[]`）。
**后端**：Controller `Create`（`WorkOrderController.cs:48-60`，catch `InvalidOperationException→400`）。Service `CreateAsync`（`WorkOrderService.cs:146-223`）：开事务→采番 `NextAsync("WO")`→建头（`Status=dto.Status>=0?dto.Status:0` 默认下書き、`Priority<=0→1`）→遍历 `Processes`（`ProcessStatus=0`、`PlanQty=p.PlanQty ?? dto.ProductionQty`）→遍历 `Materials`→SaveChanges+Commit。
- 实体：头 `WorkOrder : BaseBizEntity`；工程 `WorkOrderProcess`（业务复合 PK `WorkOrderNo+ProcessCd+TaskCd`）；材料 `WorkOrderMaterial`（PK `WorkOrderNo+ProcessCd+MaterialCd`）。
**校验与错误码**：后端 `CreateAsync` **无显式业务校验**（靠实体 `[Required]` + 前端拦）。前端 `validateStep1()`（`:147-167`）：`ME-MSG-001`(手配NO/製品CD均空)/`ME-MSG-002`(数量≤0)/`ME-MSG-003`(开始>完了)/`ME-MSG-004`(完了>納期,确认框)；`validateStep2()`（`:169-185`）：`ME-MSG-006`(无工程)/`ME-MSG-007`(工程+作业CD重复)。这些前端码有 i18n。

---

## ⭐ 受注展开(ExpandFromOrder) — POST /api/mes/work-orders/expand-from-order

> 接缝①的手动触发路径（自动路径见末尾联动专讲）。

**前端**：Step1「受注Web NO」框后放大镜（`WoStep1BasicInfo.vue:42-47`）→ `onExpand()`→`emit('expandFromOrder', webOrderNo)`。父 `onExpandFromOrder`（`WorkOrderEntryView.vue:125-145`）确认框 → `workOrderApi.expandFromOrder({webOrderNo, priority:1})` → 多指图则 `loadByNo(first)`。api `expandFromOrder`（`mes.ts:86-88`）。type `ExpandFromOrderRequest`（`{webOrderNo, webOrderDetailNos?, baseCd?, priority?}`，不传明细=全展开）。
**后端**：Controller `ExpandFromOrder`（`WorkOrderController.cs:108-120`）→ `ExpandFromOrderAsync`。详见下方联动专讲。
**校验与错误码**：`ME-MSG-001`(WebOrderNo 空 `:386`)、`ME-MSG-040`(受注头不存在 `:391`/明细 0 件 `:397`)、`ME-MSG-005`(同 `WebOrderNo+ProductCd` 已有未取消指图 `:412`)。⚠️ `ME-MSG-005/040` **无 i18n 词条**（裸码返回）。

---

## 详情加载 — GET /api/mes/work-orders/{no}

**前端**：列表「詳細」`goDetail(row)`→`/mes/work-order?no=`，Entry `onMounted` 读 `route.query.no`→`loadByNo`（`WorkOrderEntryView.vue:117-123`）`Object.assign(form, emptyForm(), res.data)`。api `get`（`mes.ts:59-61`）。
**后端**：Controller `Get`（`WorkOrderController.cs:39-45`）null→404 `ME-MSG-040`。Service `GetByNoAsync`（`WorkOrderService.cs:29-56`）头+工程(按 SortOrder)+材料 三查→`ToDto`，算 `ProcessCount/CompletedProcessCount`(`ProcessStatus==2` 计完成)/`ProgressRate`/`DelayDays`。

---

## ⭐ 指図発行(Issue→WMS材料出庫) — POST /api/mes/work-orders/{no}/issue

> 接缝②的触发点。

**前端**：Step3「指図発行」（`WorkOrderEntryView.vue:53`，`canIssue=isEdit && (status===0||1)`）→ `onIssue()`（`:213-227`）确认框 → `workOrderApi.issue(no)`（无 body）→ `loadByNo` 刷新。api `issue`（`mes.ts:81-83`）。
**后端**：Controller `Issue`（`WorkOrderController.cs:93-105`）。Service `IssueAsync` **逐字**（`WorkOrderService.cs:323-343`）：
```csharp
public async Task IssueAsync(string workOrderNo, string? userName)
{
    var wo = await _db.WorkOrders.FirstOrDefaultAsync(x => x.WorkOrderNo == workOrderNo && !x.IsDeleted)
        ?? throw new InvalidOperationException("ME-MSG-043");
    if (wo.Status >= 2) return; // already issued（幂等）
    if (wo.Status != 0 && wo.Status != 1)
        throw new InvalidOperationException("ME-MSG-042");
    var procCount = await _db.WorkOrderProcesses.CountAsync(x => x.WorkOrderNo == workOrderNo && !x.IsDeleted);
    if (procCount == 0) throw new InvalidOperationException("ME-MSG-006"); // 登録する工程がありません
    wo.Status = 2;
    wo.Modifier = userName; wo.ModifyDate = DateTime.Now;
    await _db.SaveChangesAsync();
    // WM-3.5：WMS 自動展開フック（best-effort、失敗しても発行は成功とする）
    await _wmsBridge.OnWorkOrderIssuedAsync(workOrderNo, userName);
}
```
步骤：①不存在→`ME-MSG-043` ②`Status>=2` 直接 return（幂等） ③`Status∉{0,1}`→`ME-MSG-042` ④无工程→`ME-MSG-006` ⑤`Status→2` 落库 ⑥**调 WMS Hook**（Hook 内自带 try/catch 不抛，故无需外层包裹）。
**校验与错误码**：`ME-MSG-043`(不存在,无i18n)、`ME-MSG-042`(状态不可发行,无i18n)、`ME-MSG-006`(无工程,有i18n)。

---

## 更新 — PUT /api/mes/work-orders/{no}

**前端**：编辑模式 Step3「保存」`onSave()` 编辑分支（`WorkOrderEntryView.vue:198-201`）→ `workOrderApi.update(no, form)`。子表可编辑性由 `isEditable=status===0||1` 控制。api `update`（`mes.ts:69-71`）。
**后端**：Controller `Update`（`WorkOrderController.cs:63-75`）。Service `UpdateAsync`（`WorkOrderService.cs:225-305`）：查头不存在→`ME-MSG-043`；**状态守卫** `Status∉{0,1}`→`ME-MSG-042`；开事务→覆盖头字段→**子表全删全插**（`WorkOrderProcesses.RemoveRange(old)`+`WorkOrderMaterials.RemoveRange(old)` 后 foreach 重插）→SaveChanges+Commit。
> ⚠️ **不做乐观锁**：`UpdateAsync` 未读 `RowVersion`（见 README §0.4），乐观锁仅 `DeleteAsync` 用到（`:318`）。
**校验与错误码**：`ME-MSG-043`/`ME-MSG-042`（均无 i18n）。

---

## 取消 — 无独立 HTTP 端点（受注取消的后端级联）

> **真实状态**：`CancelAsync` 已实现（`WorkOrderService.cs:351-378`），但 Controller 无路由、前端无按钮、api 无方法。指図取消**不是用户页面动作**，而是上游 `IOrderCancelBridgeHook` 调用的后端级联（接口注释 `IWorkOrderService.cs:32-43` 明确：上位先取消关联 OutboundOrder，本方法只负责 Status 迁移）。
- `CancelAsync` 逻辑：`workOrderNo/reason` 非空→不存在→`ME-MSG-043`→已 `Cancelled(9)` 幂等返 false→`!IsCancellable(Status)`（`Status≥3 着手`）→抛 `ME-MSG-CANCEL-001: 着手済の指図は取消不可`→`Status→9` + Remarks 戳记。
- 错误码 `ME-MSG-043`/`ME-MSG-CANCEL-001` 仅源码、无 i18n。

---

## 删除 — DELETE /api/mes/work-orders/{no}

列表「削除」（`v-if="row.status<=1"`）→ `workOrderApi.delete(no)`（`mes.ts:74-78`，body 带 rowVersion）→ Controller `:78-90` → `DeleteAsync`（`WorkOrderService.cs:307-321`）：状态守卫 `∉{0,1}`→`ME-MSG-042`、`IsDeleted=true` 软删、`if(rowVersion!=null) wo.RowVersion=rowVersion`（**全 MES 唯一用到乐观锁处**）。

---

## ⭐ 跨模块联动专讲

### 接缝① 受注 → 製造指図：MesBridgeHook.OnOrderCreatedAsync → ExpandFromOrderAsync

**自动触发源**：`OrderService.CreateAsync` 受注创建 SaveChanges 后（`Services/Erp/OrderService.cs:231`）`await _mesBridge.OnOrderCreatedAsync(webOrderNo, userName);`。
> `_mesBridge` 默认 `NoOpMesBridgeHook`（`OrderService.cs:44`），需 `MesBridge:Enabled=true` 才注入真实 `MesBridgeHook`。

**Hook 实现** `MesBridgeHook.OnOrderCreatedAsync`（`MesBridgeHook.cs:26-57`）：
```csharp
var nos = await _woService.ExpandFromOrderAsync(new ExpandFromOrderRequest { WebOrderNo = webOrderNo }, userName);
await PersistEventAsync("ERP", "MES", nameof(OnOrderCreatedAsync), webOrderNo, nos.Count>0?nos[0]:null,
    IntegrationEventStatus.Success, null, corrId, payload);
return MesBridgeResult.Ok(nos);
```
`catch (InvalidOperationException)`（业务错如 `ME-MSG-005` 已有指图）→ `Skipped` 不失败受注；`catch (Exception)`→ `Failed` 仍不抛。每次落 `IntegrationEvent`。

**展开逻辑** `ExpandFromOrderAsync`（`WorkOrderService.cs:384-495`）逐明细：
1. 取受注头/明细（`:389-397`，不存在/0 件→`ME-MSG-040`）。
2. **重复守卫**：同 `WebOrderNo+ProductCd` 已有未取消指图→`ME-MSG-005`（`:407-412`）。
3. 采番 `NextAsync("WO")`。
4. **从 PA050 製品マスタ展开**工程/材料（`:417-425`）：
```csharp
var prodProcs = await _db.ProductProcesses.AsNoTracking()
    .Where(p => p.ProductCd == d.ProductCd && !p.IsDeleted).OrderBy(p => p.SortOrder).ToListAsync();
var prodMats = await _db.ProductMaterials.AsNoTracking()
    .Where(m => m.ProductCd == d.ProductCd && !m.IsDeleted).OrderBy(m => m.SortOrder).ToListAsync();
```
5. 建指图头（`Status=1` 確定済、`WebOrderNo`/`CustomerCd`/`ProductCd`/`ProductionQty=qty`、`PlanEndDate=納期-1天`、`PlanStartDate=納期-7天`、`Priority=req.Priority`）。
6. 工程子表（`prodProcs→WorkOrderProcess`：`ProcessName=pp.Spec01`、`MachineCd=pp.MachineOrVendor`、`PlanQty=qty`、`ProcessStatus=0`）。
7. 材料子表（`prodMats→WorkOrderMaterial`：`MaterialCd`、`MaterialTypeDiv`、`PlanQty=qty`、`SupplyStatus=0`）。
8. SaveChanges+Commit，返回 `createdNos`。
> 一句话：**受注每条明细 → 一张指图(Status=1)+按製品 BOM/路由展开工程与材料**，整批一事务。

### 接缝② 指図発行 → WMS 材料出庫：IssueAsync → WmsBridgeHook.OnWorkOrderIssuedAsync → OutboundService.CreateFromWorkOrderAsync

**Hook 实现** `WmsBridgeHook.OnWorkOrderIssuedAsync`（`WmsBridgeHook.cs:30-56`）：
```csharp
var no = await _outbound.CreateFromWorkOrderAsync(workOrderNo, userName);
await PersistEventAsync("MES", "WMS", nameof(OnWorkOrderIssuedAsync), workOrderNo, no, IntegrationEventStatus.Success, ...);
return WmsBridgeResult.Ok(no);
// catch InvalidOperationException → Skipped；catch Exception → Failed（都不抛）
```
**生成材料出庫指示** `OutboundService.CreateFromWorkOrderAsync`（`OutboundService.cs:538-581`）：去重（同指図已有未取消 OutboundOrder→`WM-MSG-043`）→ 取指图材料（0 件→报错）→ 采番 → 建 `OutboundOrderDto`：
```csharp
var dto = new OutboundOrderDto {
    OutboundType = OutboundType.Material,       // 出庫種別＝材料
    WorkOrderNo = workOrderNo,
    WarehouseCd = "W01",                        // 既定原材料倉庫
    PlannedDate = wo.PlanStartDate?.Date ?? DateTime.Today,
    Details = materials.Select((m, i) => new OutboundOrderDetailDto {
        LineNo = i + 1,
        ProductCd = m.MaterialCd,               // 材料CD → 出庫明細製品CD
        RequiredQty = m.PlanQty ?? 0,           // 計画必要数量 → 必要数量
        UnitCd = m.Unit,
    }).ToList(),
};
await CreateOrderInternalAsync(no, dto, userName);
```
> 一句话：**指図的材料明细 → 一张 WMS 材料出庫指示(类型=Material,仓 W01)，数量取材料計画必要数量**。

### 两接缝完整数据流
```
[ERP] 受注作成 OrderService.CreateAsync (SaveChanges)
   └→(MesBridge:Enabled=true) _mesBridge.OnOrderCreatedAsync
        └→ MesBridgeHook → WorkOrderService.ExpandFromOrderAsync
             └ 逐明细：采番 + 建 T_WorkOrder(Status=1) + 按 PA050 展开 T_WorkOrderProcess/Material + 落 IntegrationEvent(ERP→MES)
──────────────────────────────────────────────
[FE] /mes/work-order「指図発行」onIssue → POST /{no}/issue → WorkOrderService.IssueAsync
   ├ Status 0/1 → 2(発行済) 落库
   └→ _wmsBridge.OnWorkOrderIssuedAsync → WmsBridgeHook → OutboundService.CreateFromWorkOrderAsync
        └ 采番 + 建 OutboundOrder(Material, W01) + 明细=指图材料 + 落 IntegrationEvent(MES→WMS)
```

---

## 涉及文件清单

| 层 | 文件 | 关键 |
|---|---|---|
| 路由 | `cp6.web/src/router/index.ts` | 87-88 |
| FE 列表 | `cp6.web/src/views/mes/WorkOrderListView.vue` | search 198 / 跳转·删除 218 / CSV 239 |
| FE 编辑器 | `cp6.web/src/views/mes/WorkOrderEntryView.vue` | 加载·展开 117-145 / 保存·発行 193-227 |
| FE step | `cp6.web/src/views/mes/work-order/steps/WoStep1BasicInfo.vue`(+Step2/3) | 受注NO框 42 / onExpand 141 |
| FE api | `cp6.web/src/api/mes/mes.ts` | `workOrderApi` 44-89 |
| FE type | `cp6.web/src/types/mes/mes.ts` | 状态 8-28 / DTO+Query 56-144 / ExpandReq 196-201 |
| BE Controller | `CP6.WebApi/Controllers/Mes/WorkOrderController.cs` | 22-120 |
| BE Service | `CP6.Core/Services/Mes/WorkOrderService.cs` | Search 58 / Create 146 / Update 225 / Issue 323 / Cancel 351 / Expand 384 |
| BE 契约 | `CP6.Core/Services/Mes/IWorkOrderService.cs` | 12-50 |
| BE 采番 | `CP6.Core/Services/Mes/MesSequenceService.cs` | `WO{yyyyMM}{NNNN}` :52 |
| 联动① Hook | `CP6.Core/Services/Mes/MesBridgeHook.cs` | 26-57 |
| 联动① 触发 | `CP6.Core/Services/Erp/OrderService.cs` | 228,231 |
| 联动② 契约 | `CP6.Core/Services/Integration/IWmsBridgeHook.cs` | 18-68 |
| 联动② Hook | `CP6.Core/Services/Wms/WmsBridgeHook.cs` | 30-56 |
| 联动② 落地 | `CP6.Core/Services/Wms/OutboundService.cs` | 538-581 |
| 实体 | `CP6.Entity/DomainModels/Mes/WorkOrder.cs`(+`WorkOrderProcess`+`WorkOrderMaterial`) | 头15-118(状态常量89-118) |
| DTO | `CP6.Entity/DTOs/Mes/WorkOrderDto.cs` | Dto+Process+Material+Query |
| i18n | `CP6.WebApi/Seed/I18nMesScreenSeed.cs` | 17-38（仅 001-007,041，缺 005/040/042/043） |

## 关键发现
1. **采番注释 vs 实现冲突**：注释 `WOYYYYMMDD-NNNN`，实现 `WO{yyyyMM}{NNNN}`（月级无连字符）。以实现为准。
2. **`ME-MSG-040/042/043/005` 无 i18n**：失败时前端展示裸码。前端自校验码 `ME-MSG-001~007` 才有翻译。
3. **取消无前端入口**：`CancelAsync` 是受注取消的后端级联，非用户动作。
4. **更新不做乐观锁**：仅 `DeleteAsync` 用 `RowVersion`。
5. **两接缝都 best-effort + IntegrationEvent**：MES Hook 默认 NoOp（需 `MesBridge:Enabled=true`），WMS Hook 默认 ON。
