# CP6 — Codex Task Roadmap V2（Phase 6-10 之后的剩余工作）

> **当前状态**：314 测试全绿；Phase 6/7/8/9/10a/10b 后端 + Phase 6/7/8 前端 + Phase 10b 前端 完成。
> **T15（Prometheus /metrics）✅ 完成**（2026-06-09，+2 测试）；**T14（多仓位路由）✅ 完成**（2026-06-09，+5 测试）。
> **portfolio repo**：已同步推送（commit 35562a0）。
> **本路线图覆盖**：把 Phase 9/10a 补完前端 + 完成 PROJECT_IMPROVEMENT_PLAN.md 里剩下的 Gap。

---

## 通用执行模板

```bash
cd D:/CP6
taskkill /F /IM dotnet.exe 2>/dev/null

# 把下方对应 task 的 prompt 内容存成文件 .codex-task-TX.md (X = 7/8/9/...)
# 或者直接整段 prompt 用 codex exec 传

codex exec "Read the task spec in D:/CP6/.codex-task-TX.md and implement it end-to-end. Stay strictly within the listed scope. Generate EF migration via 'dotnet ef migrations add' (NOT hand-written). Build the solution and run all tests as proof." \
  -s workspace-write \
  --skip-git-repo-check \
  -c 'model_reasoning_effort="high"' < /dev/null
```

跑完立即验证：

```bash
cd D:/CP6
taskkill /F /IM dotnet.exe 2>/dev/null
dotnet build --nologo -v quiet
dotnet test --no-build --nologo -v quiet
```

---

## 任务优先级总览

| # | 任务 | 类型 | Token 估 | 测试增量 | 价值 |
|---|---|---|---|---|---|
| **T7** | Phase 9 前端 — Material Shortage 列表 + Resolve/Dismiss | 前端为主 | 80-120k | +0 | 闭合 Phase 9 |
| **T8** | Phase 10a 前端 — CreditNote 列表 + RMA→CN trace | 前端为主 | 80-120k | +0 | 闭合 Phase 10a |
| **T9** | Gap 2.2 端到端 trace — 按 WebOrderNo 显示 IntegrationEvent timeline | 后端 + 前端 | 150-200k | +4 | Phase 6 可观测性收尾 |
| **T10** | Gap 4.1 Backorder 管理 — 部分出货后 ERP 关闭剩余 / 转 backorder | 后端中等 | 200-260k | +6 | 商业实战需求 |
| **T11** | Gap 3.1 OTD 准时交付报表 | 后端 + 前端 | 200-260k | +5 | 面试 demo 加分 |
| **T12** | E2E for Phase 9 + 10a + 10b（三个 e2e 测试）| 测试补全 | 100-130k | +3 | 防回归 |
| **T13** | Gap 3.2 在庫滞留分析 (Stock dwell time) | 后端 + 前端 | 150-200k | +4 | 仓库实战 |
| ~~**T14**~~ ✅ | Gap 4.2 多仓位路由策略（规则→仓库优先级→FEFO 三级择仓，opt-in） | 后端+前端 | 200-260k | +5 | 复杂度高 — **完成** |
| ~~**T15**~~ ✅ | Gap 2.3 Prometheus /metrics endpoint（IntegrationEvent 表 scrape 聚合） | 后端小 | 60-80k | +2 | 运维 — **完成** |

**推荐顺序**：T7 → T8 → T12 → T9 → T10 → T11 → T13/T14/T15 看情况。

T7+T8 先把已有后端的 UI 补齐（最小工作量、最大可见度）；T12 防回归；之后选 1-2 个高 ROI 继续。

---

# T7 — Phase 9 前端 (Material Shortage 列表 + Resolve/Dismiss)

**目标**：在 WMS 加一个「材料欠品」管理页面，展示 Phase 9 后端写入的 `T_MaterialShortage`，营业可手动 Resolve（补充后）/ Dismiss（不需要）。

`.codex-task-T7.md` 内容：

```markdown
# Task: CP6 Phase 9 Frontend — Material Shortage Management Page

## Mission

Build the Vue 3 frontend for the existing Phase 9 backend (T_MaterialShortage + `/api/wms/material-shortage`). Working dir: `D:\CP6`. **Frontend only** — backend is done. No backend changes. Must not break the 282 passing backend tests.

## Existing backend (already implemented — call these endpoints)

- `GET /api/wms/material-shortage?status=OPEN&workOrderNo=...&page=1&pageSize=50` → `{ code, message, data: { Items: MaterialShortageDto[], Total, PageIndex, PageSize } }`
- `POST /api/wms/material-shortage/{id}/resolve` body `{ remark }` → marks as RESOLVED
- `POST /api/wms/material-shortage/{id}/dismiss` body `{ remark }` → marks as DISMISSED

`MaterialShortageDto` fields (from `CP6.Entity/DomainModels/Wms/MaterialShortage.cs`): id (Guid), workOrderNo, relatedOutboundNo, productCd, lotNo, requiredQty, availableQty, detectedAt, resolvedAt, status (OPEN/RESOLVED/DISMISSED), remark, creator, createDate, modifier, modifyDate

## Files to create

| File | Purpose |
|---|---|
| `cp6.web/src/types/materialShortage.ts` | TypeScript types |
| `cp6.web/src/api/wms/materialShortage.ts` | API client (search / resolve / dismiss) |
| `cp6.web/src/views/wms/MaterialShortageView.vue` | List page with filters + action buttons |
| `docs/phase9-material-shortage-i18n-seed.sql` | i18n seed (5 languages, ≥20 keys) |

## Files to MODIFY

| File | Change |
|---|---|
| `cp6.web/src/router/index.ts` | Add route `/wms/material-shortage` → MaterialShortageView |

## UI requirements (MaterialShortageView)

- Top search panel: `WorkOrderNo` input + `Status` select (All / OPEN / RESOLVED / DISMISSED) + Search button + Reset button
- KPI card showing current OPEN count (refresh on every reload)
- Table columns: 检出时刻 (detectedAt) | WO No | 关联出庫 (relatedOutboundNo) | 製品 (productCd) | Lot | 必要数量 (requiredQty) | 现有数量 (availableQty) | 不足数 (= required - available, red color) | Status (color tag) | 备注 (remark) | 操作
- Operation column: 「対応済 (Resolve)」+ 「対応不要 (Dismiss)」buttons — both open the same confirmation dialog with a textarea for remark
- Status badge colors: OPEN=danger, RESOLVED=success, DISMISSED=info
- Status filter default: OPEN (most useful)
- Pagination at bottom

## SignalR (optional polish)

If you have time after the above: listen to `MaterialShortageDetected` event on the page's lifecycle and add `el-message` toast + reload table when a new shortage is detected by the worker.

## i18n keys (≥20 keys under `wms.materialShortage.*`)

Required keys: title, kpi.openCount, search.workOrderNo, search.status, search.btnSearch, search.btnReset, status.OPEN, status.RESOLVED, status.DISMISSED, col.detectedAt, col.wo, col.outbound, col.product, col.lot, col.requiredQty, col.availableQty, col.shortQty, col.status, col.remark, col.action, btn.resolve, btn.dismiss, dlg.resolveTitle, dlg.dismissTitle, dlg.remarkLabel, dlg.remarkPlaceholder, btn.confirm, btn.cancel, msg.resolved, msg.dismissed, realtime.newShortageDetected

## Acceptance criteria

```bash
cd D:/CP6
cd cp6.web && npm run type-check  # 0 errors
```

Apply the i18n seed to local KOUSQLSERVER:
```bash
sqlcmd -S "localhost\KOUSQLSERVER" -E -d CP6DB -f 65001 -i docs/phase9-material-shortage-i18n-seed.sql -b
```

Verify it works in browser at http://localhost:5173/wms/material-shortage (after manually triggering a shortage via material outbound with insufficient stock — Phase 9 backend test gives the pattern).

## Style rules

- Follow existing Vue 3 + Element Plus + Pinia patterns. Reference `cp6.web/src/views/erp/OrderListView.vue` for search + table + paging layout, and `cp6.web/src/views/wms/StockQueryView.vue` for the action-button dialog pattern.
- All labels via `t('...')` — no hardcoded strings.
- Use `ElMessage.success` / `ElMessage.error` for action feedback.
- Use `axios` interceptor (`@/api/http`) — don't reinvent.
- Use the codex-style `Items / Total / PageIndex / PageSize` shape compatibility (the backend returns `items` not `rows`).

## Report when done

1. Files created
2. Files modified
3. i18n seed line count
4. `npm run type-check` result
5. Deviations
```

---

# T8 — Phase 10a 前端 (CreditNote 列表 + RMA Link)

**目标**：让营业 + 财务能看到 RMA 触发的 CreditNote，每条可下载 PDF（v1 先做列表，PDF 留 v2）。

`.codex-task-T8.md` 内容：

```markdown
# Task: CP6 Phase 10a Frontend — CreditNote List + RMA Link

## Mission

Build the CreditNote list page. Add a backend search endpoint for CreditNote (the entity exists, but the controller doesn't yet). Working dir: `D:\CP6`. Must not break 282 passing tests.

## Files to create

### Backend (small additions)

| File | Purpose |
|---|---|
| `CP6.Entity/DTOs/CreditNoteDto.cs` | Query DTO + paged result item DTO |
| `CP6.Core/Services/ICreditNoteService.cs` | Interface (Search) |
| `CP6.Core/Services/CreditNoteService.cs` | Implementation |
| `CP6.WebApi/Controllers/CreditNoteController.cs` | REST endpoint |
| `CP6.Tests/CreditNoteServiceTests.cs` | 4 unit tests |

### Frontend

| File | Purpose |
|---|---|
| `cp6.web/src/types/creditNote.ts` | Types |
| `cp6.web/src/api/erp/creditNote.ts` | API client |
| `cp6.web/src/views/erp/CreditNoteListView.vue` | List page |
| `docs/phase10a-creditnote-i18n-seed.sql` | i18n seed |

## Files to MODIFY

| File | Change |
|---|---|
| `CP6.WebApi/Program.cs` | Register `ICreditNoteService` Scoped |
| `cp6.web/src/router/index.ts` | Add route `/erp/credit-note` |

## Backend contract

```
POST /api/credit-note/search
Body: { customerCd?, webOrderNo?, type?, dateFrom?, dateTo?, page=1, pageSize=50 }
Response: { code, message, data: { items: CreditNoteListItemDto[], total, pageIndex, pageSize } }
```

`CreditNoteListItemDto`: creditNoteNo, webOrderNo, rmaNo, type (REFUND/EXCHANGE/SCRAP), customerCd, customerName (joined from BusinessPartner like UnshippedOrderService does), productCd, qty, amount, reason, issueDate, createDate

## Backend tests (4)

1. `Search_FilterByCustomer` — 3 CN different customers, filter → 1
2. `Search_FilterByDateRange` — 5 CN spread across dates, filter from-to → correct count
3. `Search_JoinsCustomerName_FromBp` — CN has customerCd, BP table has matching → CustomerName populated
4. `Search_NoMatchingBp_FallsBackToCustomerCd` — orphan CN → CustomerName == CustomerCd

## Frontend (CreditNoteListView)

Standard 3-section layout (reference `cp6.web/src/views/erp/OrderListView.vue`):
- Search panel: customerCd (Element Plus autocomplete or input), type select (All/REFUND/EXCHANGE/SCRAP), dateFrom/dateTo (`el-date-picker` range), Search/Reset
- Table columns: 起票日 (issueDate) | CN No | 受注 No (WebOrderNo, click → navigate to /order?webOrderNo=...) | RMA No | 客先 (customerName, fallback customerCd) | 種類 (Type badge) | 製品 | 数量 | 金額 | 理由 (reason, truncate to 50 chars + tooltip)
- Type badge colors: REFUND=warning, EXCHANGE=info, SCRAP=danger
- Pagination

## i18n keys (≥18 under `erp.creditNote.*`)

title, search.customer, search.type, search.dateFrom, search.dateTo, btn.search, btn.reset, type.REFUND, type.EXCHANGE, type.SCRAP, col.issueDate, col.no, col.webOrderNo, col.rmaNo, col.customer, col.type, col.product, col.qty, col.amount, col.reason

## Acceptance criteria

```bash
cd D:/CP6
dotnet build --nologo -v quiet
dotnet test --no-build --nologo -v quiet  # ≥ 286 (282 + 4)
cd cp6.web && npm run type-check
```

## Report when done

Standard: files created/modified, test count, build/test/type-check results, deviations.
```

---

# T9 — Gap 2.2 端到端 trace UI (按 WebOrderNo 显示 IntegrationEvent timeline)

**目标**：把 Phase 6 的 `T_IntegrationEvent.CorrelationId` 真正利用起来 —— 营业输入一个受注号，看到从受注创建 → MES 展开 → WMS 出庫 → 出荷 → 取消 整条链上发生的所有 Bridge Hook 事件，时间轴展示。

`.codex-task-T9.md` 内容：

```markdown
# Task: CP6 Gap 2.2 — End-to-End CorrelationId Trace UI

## Mission

Build a backend service + REST endpoint + frontend page that displays the full chronological timeline of IntegrationEvent rows for a given WebOrderNo, joining with related OperLog entries for user-action context. Working dir: `D:\CP6`. Must not break 282 passing tests.

## Files to create

### Backend

| File | Purpose |
|---|---|
| `CP6.Entity/DTOs/OrderTraceDto.cs` | DTO: OrderTraceTimelineItem + summary |
| `CP6.Core/Services/IOrderTraceService.cs` | Interface |
| `CP6.Core/Services/OrderTraceService.cs` | Implementation |
| `CP6.WebApi/Controllers/OrderTraceController.cs` | REST endpoint |
| `CP6.Tests/OrderTraceServiceTests.cs` | 4 tests |

### Frontend

| File | Purpose |
|---|---|
| `cp6.web/src/types/orderTrace.ts` | Types |
| `cp6.web/src/api/erp/orderTrace.ts` | API client |
| `cp6.web/src/views/erp/OrderTraceView.vue` | Timeline page |
| `docs/gap22-order-trace-i18n-seed.sql` | i18n seed |

## Files to MODIFY

- `CP6.WebApi/Program.cs` — register IOrderTraceService
- `cp6.web/src/router/index.ts` — add `/erp/order-trace`
- `cp6.web/src/views/erp/OrderListView.vue` — add a「Trace」link button on each row → navigates to /erp/order-trace?webOrderNo=...

## Backend contract

```
GET /api/order-trace/{webOrderNo}
Response: {
  code, message,
  data: {
    webOrderNo, customerName, orderDate,
    summary: { totalEvents, successCount, failedCount, skippedCount, deadCount, distinctCorrelationIds },
    timeline: [
      {
        eventTime: "2026-06-01T10:30:00Z",
        eventKind: "BRIDGE_HOOK" | "ORDER_ACTION" | "WO_ACTION" | "OUTBOUND_ACTION",
        sourceModule: "ERP",
        targetModule: "MES",
        hookName: "OnOrderCreatedAsync",
        sourceNo: "ORD20260601-0001",
        targetNo: "WO20260601-0001",
        status: "SUCCESS",
        message: "...",
        correlationId: "guid"
      },
      ...
    ]
  }
}
```

Algorithm:
1. Find Order by WebOrderNo → if not found return 404 with `code: 1`
2. Pull `T_IntegrationEvent` rows where `SourceNo == webOrderNo` directly
3. Also pull related WO numbers (WHERE WorkOrder.WebOrderNo == webOrderNo) and pull `T_IntegrationEvent` where SourceNo IN (those WO numbers)
4. Also pull related Outbound numbers (WHERE OutboundOrder.WebOrderNo == webOrderNo) and pull `T_IntegrationEvent` where SourceNo IN (those outbound numbers)
5. Sort by CreateDate ASC
6. Compute summary stats

## Frontend (OrderTraceView)

URL param: `?webOrderNo=ORD...`

- Top: Order summary card (WebOrderNo, customer, orderDate, total events, success/failed counts as KPI tags)
- Below: Element Plus `<el-timeline>` with each event as `<el-timeline-item>`. Color/icon based on status:
  - SUCCESS = green check circle
  - SKIPPED = gray info circle
  - FAILED = red warning
  - DEAD = red exclamation
- Each item shows: time | source→target hook | source/target nos | status | message preview (truncate 80 chars + tooltip full)
- If list is long (>20 items), add CorrelationId grouping toggle: when toggled, items are grouped by correlationId with a collapsible panel per chain
- Empty state: if no events, show el-empty with "No Bridge Hook activity recorded for this order"

## i18n keys (≥18 under `erp.orderTrace.*`)

title, label.webOrderNo, label.customer, label.orderDate, summary.totalEvents, summary.success, summary.failed, summary.skipped, summary.dead, summary.distinctChains, timeline.empty, group.byCorrelation, status.SUCCESS, status.SKIPPED, status.FAILED, status.DEAD, status.PENDING, status.COMPENSATED, kindLabel.BRIDGE_HOOK, btn.copyCorrelationId, msg.copied

## Tests (4)

1. `Trace_AggregatesEventsByOrderRelatedWoOutbound` — seed Order + 2 WOs + 1 Outbound + 5 events (3 keyed by Order, 1 by WO, 1 by Outbound) → returns all 5 sorted by CreateDate
2. `Trace_OrderNotFound_Returns404` — call with bogus webOrderNo → 404
3. `Trace_SummaryStats_Correct` — seed mixed status events → summary.successCount/failedCount/etc match
4. `Trace_DistinctCorrelationIds_Counted` — seed 5 events across 2 correlation ids → summary.distinctCorrelationIds == 2

## Acceptance criteria

```bash
cd D:/CP6
dotnet build --nologo -v quiet
dotnet test --no-build --nologo -v quiet  # ≥ 286 (282 + 4)
cd cp6.web && npm run type-check
```

## Style

- Backend: match existing controller `{code,message,data}` shape
- Frontend: vue-i18n, Element Plus `el-timeline`, `axios` via interceptor
- Compatible with PagedResultDto `Items` vs `rows` (use `items ?? rows ?? []`)
```

---

# T10 — Gap 4.1 Backorder 管理

**目标**：当 WMS 部分出货后剩余无库存补不上时，营业能在 ERP 端决定：把剩余转 backorder 或直接关闭。

`.codex-task-T10.md` 内容：

```markdown
# Task: CP6 Gap 4.1 — Backorder Management

## Mission

Add `OrderDetail.BackorderQty` field + service method + REST endpoint that lets sales decide what to do with a partially-shipped order detail's remaining quantity: either generate a backorder (split out) or close as forfeit. Working dir: `D:\CP6`. Must not break 282 passing tests.

## Backend files to create

| File | Purpose |
|---|---|
| `CP6.Core/Migrations/<timestamp>_Gap41AddOrderDetailBackorderQty.cs` | EF migration (generated via dotnet ef) |
| `CP6.Entity/DTOs/BackorderDto.cs` | Request/response DTOs |
| `CP6.Core/Services/IBackorderService.cs` | Interface |
| `CP6.Core/Services/BackorderService.cs` | Implementation |
| `CP6.WebApi/Controllers/BackorderController.cs` | REST endpoint |
| `CP6.Tests/BackorderServiceTests.cs` | 5 tests |

## Backend files to MODIFY

- `CP6.Entity/DomainModels/OrderDetail.cs` — add `[Column(TypeName="decimal(21,8)")] public decimal? BackorderQty { get; set; }`
- `CP6.WebApi/Program.cs` — register IBackorderService
- `CP6.Entity/DomainModels/Order.cs` — add `BackorderStatus` lifecycle constants if needed (no DB change to Order itself, just constants for clarity)

## Frontend files to create

| File | Purpose |
|---|---|
| `cp6.web/src/types/backorder.ts` | Types |
| `cp6.web/src/api/erp/backorder.ts` | API client |
| `cp6.web/src/views/erp/BackorderListView.vue` | Backorder queue page |
| `docs/gap41-backorder-i18n-seed.sql` | i18n seed |

## Frontend files to MODIFY

- `cp6.web/src/router/index.ts` — add `/erp/backorder`
- Optionally `cp6.web/src/views/erp/OrderListView.vue` — show a "BO" tag on rows that have any detail with BackorderQty > 0

## Backend contract

```
GET /api/backorder/queue
Response: list of OrderDetail rows where (Quantity - ShippedQty - (BackorderQty ?? 0)) > 0 AND Order.OrderStatus IN (CONFIRMED, IN_PRODUCTION, PARTIALLY_CANCELLED)
Each item: webOrderNo, customerName, detailNo, productCd, orderedQty, shippedQty, backorderQty, remainingQty, lastShipDate

POST /api/backorder/{webOrderNo}/{detailNo}/close-remaining
Body: { reason }
→ Sets BackorderQty = (Quantity - ShippedQty), increments shipped to match ordered (sets ShipStatus = 9 for that detail). The order detail is now "done" from sales perspective with the BackorderQty being the close-out.

POST /api/backorder/{webOrderNo}/{detailNo}/split-to-new-order
Body: { reason }
→ Generates a NEW Order with one detail copying everything but Quantity = remainingQty; sets parent detail BackorderQty = remainingQty + ShippedQty (closed). Returns the new WebOrderNo.
```

Wrap both close-remaining and split-to-new-order in a transaction. Generate the new WebOrderNo via existing `DocNumber.NextAsync(_db, "ORD")`. Copy all OrderDetail fields including OrderHeader-pointing customer/product/etc.

## Tests (5)

1. `Queue_FiltersOnlyOpenOrdersWithRemaining` — seed orders with various states → returns only the relevant
2. `CloseRemaining_SetsBackorderQty_AndShipStatus9` — close → BackorderQty=remaining, ShipStatus=9
3. `SplitToNewOrder_CreatesNewOrderWithRemainingQty` — split → new order created, new detail.Quantity == old remaining; parent detail BackorderQty == remaining
4. `SplitToNewOrder_CopiesHeaderFields` — new order header has CustomerCd, OrderDate (today), all required header fields populated
5. `OperationOnAlreadyClosedDetail_Throws` — call close/split on a detail with BackorderQty already set → InvalidOperationException

## Frontend (BackorderListView)

- Top: filter by customerCd, dateRange (last shipment date)
- Table: customer | webOrderNo (link) | detail# | product | ordered | shipped | remaining (red) | last ship date | actions
- Actions: "Close remaining" + "Split to new order" — both open confirmation dialog with reason textarea
- After action: refresh list + el-message success

## i18n keys (≥15 under `erp.backorder.*`)

title, search.customer, search.dateFrom, search.dateTo, col.customer, col.no, col.detail, col.product, col.ordered, col.shipped, col.remaining, col.lastShipDate, col.action, btn.closeRemaining, btn.splitNew, dlg.closeTitle, dlg.splitTitle, dlg.reasonLabel, msg.closed, msg.splitSuccess, msg.splitNewOrderNo

## Acceptance criteria

```bash
cd D:/CP6
dotnet ef migrations add Gap41AddOrderDetailBackorderQty --project CP6.Core --startup-project CP6.WebApi --output-dir Migrations --no-build
dotnet build --nologo -v quiet  # 0 errors
dotnet test --no-build --nologo -v quiet  # ≥ 287 (282 + 5)
cd cp6.web && npm run type-check
```

## Report

Standard.
```

---

# T11 — Gap 3.1 OTD 准时交付报表

**目标**：营业主管 + 工厂经理需要看到「按客户/月份的准时交付率（On-Time Delivery）」报表，做 KPI 复盘。

`.codex-task-T11.md` 内容（简化版，自包含所有上下文）：

```markdown
# Task: CP6 Gap 3.1 — On-Time Delivery (OTD) Report

## Mission

Add backend OTD aggregation service + REST endpoint + frontend report page with CSV export. Working dir: `D:\CP6`. Must not break 282 tests.

## Backend files to create

| File | Purpose |
|---|---|
| `CP6.Entity/DTOs/OtdReportDto.cs` | Query + response DTOs |
| `CP6.Core/Services/IOtdReportService.cs` | Interface |
| `CP6.Core/Services/OtdReportService.cs` | Implementation |
| `CP6.WebApi/Controllers/OtdReportController.cs` | REST endpoint |
| `CP6.Tests/OtdReportServiceTests.cs` | 5 tests |

## Backend MODIFY

- `CP6.WebApi/Program.cs` — register IOtdReportService

## Definition

For each shipped Order (`ShipStatus >= 5`):
- Promise date = `CustomerDeliveryDate`
- Actual date = `ActualShipDate` (if null, fall back to max(OrderDetail.LastShipDate))
- On-time = ActualDate <= PromiseDate (delta in days, negative for early)
- Late = ActualDate > PromiseDate (positive delta)

Group output by either `customerCd` or `yyyyMM` of OrderDate (parameter `groupBy` = "customer" | "month").

## Endpoints

```
POST /api/otd-report/summary
Body: { dateFrom, dateTo, groupBy: "customer" | "month", customerCd? }
Response: data.rows = [{
  groupKey,
  groupLabel,
  totalShippedOrders,
  onTimeCount,
  lateCount,
  onTimeRate, // 0..1
  avgLateDays
}]
Sort by onTimeRate DESC.

POST /api/otd-report/export-csv
Body: same as summary
Response: text/csv attachment (UTF-8 BOM + RFC 4180)
```

## Frontend files to create

| File | Purpose |
|---|---|
| `cp6.web/src/types/otdReport.ts` | Types |
| `cp6.web/src/api/erp/otdReport.ts` | API client |
| `cp6.web/src/views/erp/OtdReportView.vue` | Report page (filter + table + chart + CSV button) |
| `docs/gap31-otd-i18n-seed.sql` | i18n seed |

## Frontend MODIFY

- `cp6.web/src/router/index.ts` — add `/erp/otd-report`

## UI

- Filter panel: dateFrom/dateTo (default last 90 days), groupBy radio, customerCd autocomplete (optional)
- KPI cards row: overall on-time rate, total shipped orders, late count
- Table: group | total | on-time | late | on-time% (with progress bar) | avg late days
- ECharts bar chart (or simple Element Plus visualization) of on-time% by group
- "Export CSV" button

## Tests (5)

1. `OtdSummary_GroupByCustomer_AggregatesCorrectly`
2. `OtdSummary_GroupByMonth_AggregatesCorrectly`
3. `OtdSummary_OnTimeRate_Mathematically Correct` — 3 on-time + 2 late → 0.6
4. `OtdSummary_AvgLateDays_OnlyConsidersLateOrders`
5. `OtdExport_GeneratesCsv_WithBomAndHeader`

## i18n keys (≥18 under `erp.otdReport.*`)

title, filter.dateFrom, filter.dateTo, filter.groupBy.label, filter.groupBy.customer, filter.groupBy.month, filter.customer, btn.search, btn.exportCsv, kpi.overallRate, kpi.totalShipped, kpi.lateCount, col.group, col.total, col.onTime, col.late, col.onTimeRate, col.avgLateDays, chart.title

## Acceptance criteria

```bash
cd D:/CP6
dotnet build --nologo -v quiet
dotnet test --no-build --nologo -v quiet  # ≥ 287
cd cp6.web && npm run type-check
```
```

---

# T12 — E2E Tests for Phase 9 + Phase 10a + Phase 10b

**目标**：补 Phase 9/10a/10b 的端到端测试，达到 Phase 6 的同等质量水准。

`.codex-task-T12.md` 内容：

```markdown
# Task: CP6 — E2E Tests for Phase 9 + Phase 10a + Phase 10b

## Mission

Add 3 end-to-end integration tests in `CP6.Tests/` to provide regression coverage for Phase 9 / 10a / 10b. Working dir: `D:\CP6`. Must not break 282 passing tests.

## Files to create

| File | Coverage |
|---|---|
| `CP6.Tests/MaterialShortage_E2ETests.cs` | Phase 9: full backflow chain |
| `CP6.Tests/RmaCreditNote_E2ETests.cs` | Phase 10a: RMA confirm → CreditNote + ReturnedQty + IntegrationEvent |
| `CP6.Tests/BridgeHealth_AggregationE2ETests.cs` | Phase 10b: seed IntegrationEvents → metrics correct |

## Reference patterns

- `CP6.Tests/OrderCancelFullCascadeE2ETests.cs` — real service wiring + InMemory DB
- `CP6.Tests/BridgeHookPersistenceTests.cs` — IntegrationEvent persistence pattern

## Test details

### MaterialShortage_E2ETests (1 test)

```csharp
[Fact]
public async Task MaterialOutbound_InsufficientStock_WritesShortage_NotifiesNoOpNotifier_AndOutboundIsPartialAllocated()
{
    // Seed:
    // - WorkOrder
    // - Material OutboundOrder (Confirmed) with 1 detail required=100
    // - Stock available=0 for that ProductCd
    // Act: OutboundService.AllocateAsync(outboundNo, user)
    // Assert:
    // - No InsufficientStockException thrown
    // - T_MaterialShortage has 1 OPEN row matching (WO, ProductCd, requiredQty=100)
    // - OutboundOrder.Status == OutboundOrderStatus.PartialAllocated
    // - Mocked IMaterialShortageNotifier.NotifyAsync called once with matching args
}
```

### RmaCreditNote_E2ETests (1 test)

```csharp
[Fact]
public async Task RmaConfirm_GeneratesCreditNote_UpdatesReturnedQty_PersistsIntegrationEvent()
{
    // Seed:
    // - Order ORD20260601-0001 + 1 detail (ProductCd=P001, Quantity=100, ShippedQty=100)
    // - OutboundOrder (Shipping, ShipStatus=Completed) linking to that Order
    // - RmaHeader linking to that Outbound, 1 RmaDetail (ProductCd=P001, Qty=10)
    // Act: RmaService.ConfirmAsync (or whatever the confirm method is) → triggers bridge
    // Assert:
    // - T_CreditNote has 1 row with WebOrderNo, Qty=10
    // - OrderDetail.ReturnedQty == 10
    // - T_IntegrationEvent has 1 SUCCESS row with HookName=OnReturnConfirmedAsync
}
```

### BridgeHealth_AggregationE2ETests (1 test)

```csharp
[Fact]
public async Task GetMetrics_Aggregates24hWindow_GroupsByHook_ComputesSuccessRate()
{
    // Seed 8 IntegrationEvent rows:
    // - 5 for hook "OnOrderCreatedAsync" Source=ERP Target=MES: 3 SUCCESS, 1 FAILED, 1 DEAD
    // - 3 for hook "OnWorkOrderIssuedAsync" Source=MES Target=WMS: 3 SUCCESS
    // - Also 2 events from 25 hours ago (should be excluded from 24h window)
    // Act: BridgeHealthService.GetMetricsAsync()
    // Assert:
    // - hooks array has 2 entries
    // - hook A (OnOrderCreated): totalCount=5, success=3, dead=1, successRate ≈ 0.6
    // - hook B (OnWorkOrderIssued): totalCount=3, success=3, successRate=1.0
    // - queueDepth == 1 (the failed one)
    // - deadLetterCount == 1
    // - deadLetters[0].hookName == "OnOrderCreatedAsync"
}
```

## Acceptance criteria

```bash
cd D:/CP6
dotnet build --nologo -v quiet
dotnet test --no-build --nologo -v quiet  # ≥ 285 (282 + 3)
```

## Style

xUnit + Moq + EF Core InMemory + `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`. Match existing test naming conventions. Use `Mock<T>` for IMaterialShortageNotifier (test 1) since real one requires IHubContext reflection.

## Report when done

Standard.
```

---

# T13/T14/T15 — 低优先级补充任务

如果 T7-T12 跑完还想继续：

### T13 — Gap 3.2 在庫滞留分析 (Stock Dwell Time)

简介：扫 T_Stock，按 `(DATEDIFF(day, ReceiveDate, NOW())` 分桶（0-30d / 31-60d / 61-90d / 90+d），按客户/产品聚合。前端 Dashboard widget + 单页报表。  
工作量：~150-200k tokens / +4 tests。

### T14 — Gap 4.2 多仓位路由策略 ✅ 完成（2026-06-09）

简介：新增 `Warehouse.OutboundPriority` int + 新表 `T_OutboundRoutingRule`（按客户区域 / 产品类别配置首选仓）。`OutboundService.AllocateAsync` 引当时按 (RoutingRule → WarehousePriority → FEFO) 三级排序。  
工作量：~200-260k tokens / +5 tests。

**实现要点**（与原规格的差异/补充）：
- 产品类别条件以 `OutboundRoutingRule.ProductCdPrefix`（製品CD接頭辞 StartsWith）实现，避免耦合製品マスタ。
- 实分配仓记录到 `OutboundOrderDetail.WarehouseCd`；Ship/Cancel 用 `d.WarehouseCd ?? header.WarehouseCd`（旧数据兜底）。
- 路由为 `OutboundService` 可选构造参数 + `OutboundRouting:Enabled`（默认 false，opt-in）门控 → 现有出库测试零改动通过。
- 新 `IOutboundRoutingService`（候选解析 + 规则 CRUD + 预览）、`OutboundRoutingController`、前端 `OutboundRoutingView.vue`（规则管理 + 候选仓预览）、菜单 419、i18n `docs/t14-outbound-routing-i18n-seed.sql`。
- 迁移 `20260609133018_Gap42AddOutboundRouting`（`dotnet ef` 生成）。

### T15 — Gap 2.3 Prometheus /metrics endpoint ✅ 完成（2026-06-09）

简介：装 `prometheus-net.AspNetCore` 包，暴露 `/metrics`。Custom metrics：`cp6_bridge_hook_total{hook,status}`、`cp6_bridge_retry_queue_depth`、`cp6_integration_event_dead_letter_total`。  
工作量：~60-80k tokens / +2 tests。

**实现要点**：指标在 scrape 时从 `T_IntegrationEvent` 表聚合（DB=唯一真相、重启不丢值），prometheus-net `BeforeCollect` 回调薄适配；可测聚合逻辑抽到 Core 的 `BridgeMetricsSnapshotProvider`。另含内置 `UseHttpMetrics()` 的 HTTP 指标。注意：`/metrics` 默认无鉴权，公网暴露需自行加白名单。

---

# 跑完一轮后的收尾建议

1. 整理 portfolio repo — 跑完几个 Codex 任务后再次同步：
```bash
# 在 D:/CP6 commit 新代码
cd D:/CP6 && git add -A && git commit -m "feat: Tx implementation"

# 同步到 portfolio（沿用之前的 cp -r 策略，保留 portfolio 已脱敏 configs）
# 参考之前 C1 task 流程
```

2. 跑一遍前端整体：`cd cp6.web && npm run e2e`（Playwright）确保新页面不打架

3. 更新 PROJECT_STRUCTURE.md §八 — 把 T7-T15 完成项添加到测试矩阵和路线说明

---

# 常见踩坑提示

1. **EF migration** 一定要用 `dotnet ef migrations add`，不要手写文件
2. **bin 锁** — 跑 codex 之前 `taskkill /F /IM dotnet.exe`
3. **PagedResultDto** 后端返 `Items`，前端兼容 `items ?? rows`
4. **SignalR 反馈循环** — dashboard 任何新 API call 不要放 `loadData()` 里
5. **i18n 落库** — 跑完 codex 记得 `sqlcmd -i docs/phaseX-...-i18n-seed.sql` 否则前端 label 是 key 不是翻译
6. **Stock 后端列表查询** 需要返回 `qcStatus` 字段才能让 Phase 7 列显示有意义内容
7. **路由 + 菜单** — 新页面加路由后还要看是否需要更新 `docs/wms-menu-seed.sql` 让左侧导航出现
