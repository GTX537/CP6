# 02 · 製造実績 ProductionResult（ME040/050）

> 先读 [`README.md` §0/§1](README.md)。全部 `文件:行号` 与代码片段 2026-06-22 实测、逐字引用。

## 0. 架构定位

- 业务 PK `RESULT_NO`（采番 `PR`，月级 `PR{yyyyMM}{NNNN}`）。一指図×一工程的每次报告（開始/中断/再開/完了/数量報告）按时序累积成一条 `T_ProductionResult` 记录。
- **実績種別 ResultType**：1=開始 2=中断 3=中断解除 4=完了 5=数量報告（`ProductionResultService.cs:12`、实体 `:32`）。
- ⚠️ **append-only 流水**：Controller 只有 GET×2 + POST×5，**无 PUT/DELETE 端点**，前端无改/删按钮。5 个 POST 共用私有 `WriteAsync(req, userName, resultType)`，仅 `resultType` 不同。
- **接缝③**：全工程完了 → WMS 完成品自動入庫（本文重点专讲）。

---

## ① 一覧検索 — GET /api/mes/production-results

**前端**：`/mes/production-result-list`（`router/index.ts:90`）→ `ProductionResultListView.vue`，`onMounted(search)` + 检索/分页触发。`search()`（`:123-132`）→ `productionResultApi.search(query)`。CSV 为纯前端导出（`:139-160`，不调后端）。api（`mes.ts:95-100`）。type 检索 `ProductionResultSearchQuery`（`types/mes/mes.ts:185-194`）、行 DTO（`:146-167`）。
**后端**：Controller `Search`（`ProductionResultController.cs:22-28`，`[Route("api/mes/production-results")][Authorize]`）。Service `SearchAsync`（`ProductionResultService.cs:49-101`）：`Where(!IsDeleted)`+条件→size 上限 500→`OrderByDescending(CreateDate)`→**名称补完**（批量查 `WorkOrders.ProductName` + 逐 `(WorkOrderNo,ProcessCd)` 查 `WorkOrderProcesses.ProcessName` 回填）。无错误码。

---

## ② 指図サマリ加载（详情形态）— GET /api/mes/production-results/work-order/{no}

> "详情"不是加载某条实绩，而是加载指図 + 全工程当前状态，作为実績入力画面的数据源。

**前端**：`/mes/production-result`（`router/index.ts:89`）→ `ProductionResultEntryView.vue`。「取得」`loadWorkOrder()`（`:188-200`）实际调 `workOrderApi.get(workOrderNo)`（返回含 `processes` 的 `WorkOrderDto`），`status<2` 警告"発行前不可入力"。工程表每行依 `processStatus` 决定可见按钮（状态0→開始、1→中断/完了、3→中断解除）。
> 注：`productionResultApi.getWorkOrderSummary`（`mes.ts:102-107`）已封装但该 view 走的是 `workOrderApi.get`。
**后端**：Controller `GetWorkOrderSummary`（`ProductionResultController.cs:30-37`）null→404 `ME-MSG-040`。Service `GetWorkOrderSummaryAsync`（`:272-273`）直接委托 `_woService.GetByNoAsync`。

---

## ③ ⭐ 実績入力（開始/中断/再開/完了/数量報告）— POST ×5

> 5 动作共用前端 dialog + 后端 `WriteAsync`，仅 resultType 不同。良品/不良累计与工程推进的核心。

**前端**：工程表每行操作按钮 `openDialog(row, N)`（`ProductionResultEntryView.vue:64-68`，N=1..5）。`submit()`（`:226-259`）前端校验（`ME-MSG-011` 作業者/`ME-MSG-012` 良品数/`ME-MSG-014` 不良理由/中断理由）后按 resultType 分派到 5 个 api：
```js
const fn = { 1: start, 2: suspend, 3: resume, 4: complete, 5: report }[resultType.value]
const res = await fn(form)
```
api（`mes.ts:109-132`）：`start→POST /start`、`suspend→/suspend`、`resume→/resume`、`complete→/complete`、`report→POST /mes/production-results`（数量報告，无子路径）。type 请求 `ProductionResultRequest`（`mes.ts:169-183`）。

**后端**
- Controller（`ProductionResultController.cs:39-57`）5 个 POST 全委托私有 `Run`（`:59-70`，成功 `{code:0, message:"ME-MSG-041", data:{resultNo}}`，`InvalidOperationException→400`）。
- Service 5 公共方法（`:107-120`）只是 `WriteAsync` 的 resultType 分派。
- **核心 `WriteAsync`（`ProductionResultService.cs:122-270`）**：
  前置（`:124-135`）：`WorkOrderNo` 空→`ME-MSG-001`；`OperatorCd` 空→`ME-MSG-011`；指図不存在→`ME-MSG-040`；`wo.Status<2`→`ME-MSG-042`（発行済以降才可入力）；工程不存在→`ME-MSG-040`。事务 + `justCompleted=false`。
  状态机 switch，**完了分支 case 4（`:168-194`）**：
```csharp
case 4: // 完了
    if (proc.ProcessStatus != 1) throw new InvalidOperationException("ME-MSG-042");
    if (req.GoodQty <= 0) throw new InvalidOperationException("ME-MSG-012");
    if (req.DefectQty > 0 && string.IsNullOrWhiteSpace(req.DefectReasonCd))
        throw new InvalidOperationException("ME-MSG-014");
    if (req.ActualEndTime.HasValue && proc.ActualStartTime.HasValue && req.ActualEndTime < proc.ActualStartTime)
        throw new InvalidOperationException("ME-MSG-016");
    proc.ProcessStatus = 2;
    proc.ActualEndTime = req.ActualEndTime ?? now;
    proc.GoodQty += req.GoodQty;        // 工程级良品累计
    proc.DefectQty += req.DefectQty;    // 工程级不良累计
    // 全工程完了？
    var procs = await _db.WorkOrderProcesses.Where(p => p.WorkOrderNo == req.WorkOrderNo && !p.IsDeleted).ToListAsync();
    var allDone = procs.All(p => (p.Id == proc.Id) ? true : p.ProcessStatus == 2);
    if (allDone) { wo.Status = 4; wo.ActualEndDate = now; justCompleted = true; }   // ← WMS入庫点火
    break;
```
  其它分支：case1 開始(`0→1`,`wo.Status=3`,首启记 `ActualStartDate`)、case2 中断(`1→3`,`wo.Status=5`,中断理由空→`ME-MSG-024`)、case3 再開(`3→1`,`wo.Status=3`)、case5 数量報告(仅累计无迁移)。
  **指図级累计**（`:204-208`，仅 resultType 4/5）：`wo.CompletedQty += req.GoodQty; wo.DefectQty += req.DefectQty;`（→ WMS 入庫数来源）。
  实绩生成（`:215-241`）：算 `lossRate`→采番 `NextAsync("PR")`→new `ProductionResult` Add→`SaveChanges`+`Commit`。
  提交后副作用（`:247-267`）：SignalR 推送（try/catch 吞）、**WMS 入庫触发**（见专讲）、工时派生 `RecalculateProcessHoursAsync`。

**三级数量累计**（重要）：实绩行级（new ProductionResult）→ 工程级（`proc.GoodQty/DefectQty +=`）→ 指図级（`wo.CompletedQty/DefectQty +=`，仅 4/5）。WMS 入庫用**指図级累计良品 `wo.CompletedQty`**。

**校验与错误码（grep `ProductionResultService.cs`）**：
| 码 | 行 | 触发 |
|---|---|---|
| ME-MSG-001 | :124 | WorkOrderNo 空 |
| ME-MSG-011 | :125 | OperatorCd 空 |
| ME-MSG-040 | :128,135 | 指図/工程未找到 |
| ME-MSG-042 | :131,147,156,164,170 | 指図未発行/工程状态不允许该动作 |
| ME-MSG-024 | :158 | 中断理由CD 空 |
| ME-MSG-012 | :172,197 | 完了良品≤0 / 数量报告良+不良均≤0 |
| ME-MSG-014 | :174 | 不良数>0 但理由空 |
| ME-MSG-016 | :176 | 完了日时 < 工程开始日时 |
成功 `ME-MSG-041`。

---

## ⭐ 接缝③专讲：全工程完了 → WMS 完成品自動入庫

**第1步 完了判定点火**（`ProductionResultService.cs:182-193`）：case4 内本工程置 `ProcessStatus=2` 后，`procs.All(p => p.Id==proc.Id ? true : p.ProcessStatus==2)` 判全完→`wo.Status=4`、`justCompleted=true`。传给 WMS 的是**指図级累计良品 `wo.CompletedQty`**。

**第2步 提交后调 Hook**（`:256-258`，逐字）：
```csharp
// ERP→MES→WMS 接缝：全工程完了時、完成品（累計良品数）を WMS へ自動入庫（best-effort）
if (justCompleted)
    await _wmsBridge.OnProductionCompletedAsync(req.WorkOrderNo, wo.CompletedQty, userName);
```
在 `Commit` 之后调用；仅 `justCompleted` 触发；入参累计良品。

**第3步 Hook 标准实现** `WmsBridgeHook.OnProductionCompletedAsync`（`WmsBridgeHook.cs:86-112`）：try → `_inbound.CreateFinishedGoodsFromWorkOrderAsync(workOrderNo, goodQty, userName)` + `PersistEvent(Success)`；`catch InvalidOperationException`→Skipped；`catch Exception`→Failed。三态都落 `IntegrationEvent`（corrId+payload）。

**第4步 WMS 实际入庫** `InboundService.CreateFinishedGoodsFromWorkOrderAsync`（`InboundService.cs:423-463`）关键：
```csharp
if (goodQty <= 0) throw new InvalidOperationException($"指図 [{workOrderNo}] の完成品数量が 0 のため入庫対象なし");
// 二重入庫防止：同一指図の PRODUCTION 入庫実績が既にあればスキップ
var existing = await _db.InboundReceipts.AsNoTracking()
    .AnyAsync(x => x.WorkOrderNo == workOrderNo && x.SourceType == InboundSourceType.Production && !x.IsDeleted);
if (existing)
    throw new InvalidOperationException($"WM-MSG-043: 指図 [{workOrderNo}] の完成品入庫は既に登録済みです");
// ... 建 InboundReceiptDto（SourceType=Production, WarehouseCd=FinishedGoodsWarehouse,
//     ProductCd=wo.ProductCd, LotNo=wo.LotNo??workOrderNo, ReceivedQty=goodQty, LocationCd=FinishedGoodsLocation）
return await ConfirmReceiptAsync(dto, userName);
```
要点：①**幂等护栏**——同指図已有 `Production` 入庫则抛 `WM-MSG-043` 跳过（被 Hook `catch(InvalidOperationException)` 接住→Skipped，不影响 MES）；②入庫数=累计良品；③入完成品仓；④终调 `ConfirmReceiptAsync` 真正建入庫单+增库存。

**NG 数与品質/不良的衔接**：ProductionResult 的 `DefectQty/DefectReasonCd` **不会**自动建品質/不良记录（grep 确认无此逻辑）。不良下游是**分析消费**（`OeeService`/`MesDashboardService` 读取算良率）。品質検査(ME060/070)、不良管理(ME080)是独立录入入口，靠 `WorkOrderNo` 关联。完了入庫只取**良品累计**，不良不入完成品仓。

**联动数据流**：完了 POST → `WriteAsync` case4 全完判定 `justCompleted` + `wo.CompletedQty` 累计 → Commit → `_wmsBridge.OnProductionCompletedAsync(WorkOrderNo, wo.CompletedQty)` → `WmsBridgeHook`(try/catch+IntegrationEvent) → `InboundService.CreateFinishedGoodsFromWorkOrderAsync`(幂等 → 完成品仓) → `ConfirmReceiptAsync` 建入庫单+增库存 → 返回 ReceiptNo。

---

## 涉及文件清单

| 层 | 文件 | 关键 |
|---|---|---|
| 路由 | `cp6.web/src/router/index.ts` | 89-90 |
| FE 列表 | `cp6.web/src/views/mes/ProductionResultListView.vue` | search 123 / CSV 139 |
| FE 录入 | `cp6.web/src/views/mes/ProductionResultEntryView.vue` | 指図加载 188 / dialog·submit 202-259 |
| FE api | `cp6.web/src/api/mes/mes.ts` | `productionResultApi` 94-133 |
| FE type | `cp6.web/src/types/mes/mes.ts` | RESULT_TYPE 44-50 / DTO·Req·Query 146-194 |
| BE Controller | `CP6.WebApi/Controllers/Mes/ProductionResultController.cs` | 6 端点 22-57 / Run 59-70 |
| BE Service | `CP6.Core/Services/Mes/ProductionResultService.cs` | Search 49 / **WriteAsync 122-270** / WMS触发 256 / 工时派生 280 |
| 实体 | `CP6.Entity/DomainModels/Mes/ProductionResult.cs` | 字段 15-77 |
| 实体（累计目标） | `WorkOrder.cs`(Status/CompletedQty/DefectQty) / `WorkOrderProcess.cs`(ProcessStatus/GoodQty/DefectQty) | — |
| DTO | `CP6.Entity/DTOs/Mes/ProductionResultDto.cs` | Dto/Req/Query 4-58 |
| 接缝③ Hook | `CP6.Core/Services/Integration/IWmsBridgeHook.cs`(:31-35) / `CP6.Core/Services/Wms/WmsBridgeHook.cs`(:86-112) | OnProductionCompletedAsync |
| 接缝③ 落地 | `CP6.Core/Services/Wms/InboundService.cs` | CreateFinishedGoodsFromWorkOrderAsync 423-463（幂等 WM-MSG-043） |

## 关键发现
1. **无更新/删除端点**：append-only，5 POST 共用 `WriteAsync`。"详情"实为加载指図+全工程状态（Entry view 走 `workOrderApi.get` 而非 `getWorkOrderSummary`）。
2. **三级数量累计**：行→工程→指图；WMS 入庫用指图级 `wo.CompletedQty`。
3. **全工程完了判定**：`procs.All(p => p.Id==当前 ? true : p.ProcessStatus==2)`。
4. **WMS 入庫在 Commit 之后**，双层 try/catch + 幂等(`WM-MSG-043`)，不回滚 MES。
5. **NG 数不自动建品質/不良单**，仅供 OEE/Dashboard 分析。
