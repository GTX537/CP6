# CP6 — Codex Task Roadmap（剩余工作 → 自助跑）

> **当前状态**：Phase 6/7/8 全部前后端落地，本地 219→259 测试 全绿，i18n 59 条 × 5 语言。
> **运行方式**：每个 Codex 任务都是独立的，token 估算各异。按优先级跑，跑完一个我（Claude）做一次独立验证。
>
> **通用执行模板**（每个任务都这么跑）：
> ```bash
> cd D:/CP6
> taskkill /F /IM dotnet.exe 2>/dev/null || true   # 防止 bin 锁
> codex exec "Read the task spec in D:/CP6/.codex-task-TX.md and implement it end-to-end. Stay strictly within the listed scope. Build the solution and run all tests as proof. Output the final summary as instructed in the spec's 'Report when done' section." \
>   -s workspace-write \
>   --skip-git-repo-check \
>   -c 'model_reasoning_effort="high"' < /dev/null
> ```
> 把 `TX` 替换成 T1/T2/T3 即可。

---

## 优先级排序 + 概览

| # | 任务 | 类型 | Token 估 | 测试增量 | 价值 |
|---|---|---|---|---|---|
| T1 | Phase 7 + 8 E2E 测试 | 测试补全 | 120-160k | +4 | 防止后续重构破回归 |
| T2 | Phase 7 自动 QC 联动（QI NG → Stock 标记 FAILED） | 后端小特性 | 60-80k | +3 | 闭合 Phase 7 spec |
| T3 | CSV 导出（未出货 Dashboard） | 后端小特性 | 50-70k | +2 | 营业实战需求 |
| T4 | Bridge Health Monitor（24h 成功率 + DLQ Dashboard） | 后端 + 前端 | 200-260k | +5 | 面试 demo 加分 |
| T5 | Phase 9 — 材料欠品反流（T_MaterialShortage 表 + 反流告警） | 后端中等 | 200-260k | +6 | 闭环更完整 |
| T6 | Phase 10a — RMA → ERP CreditNote 回写 | 后端中等 | 200-260k | +6 | 闭环最后一块 |
| C1 | portfolio repo 同步（Phase 6/7/8 脱敏推 public） | Claude/Bash | — | — | 面试看的代码 |
| C2 | 更新 PROJECT_STRUCTURE.md（加 Phase 7/8 章节）| Claude/写作 | — | — | 文档现状对齐 |

**推荐顺序**：T1 → T2 → T3 → T4，然后 C1 + C2 让 Claude 收尾，最后 T5/T6 看情况追加。

---

# T1: Phase 7 + 8 E2E 测试

**目标**：补 2 个 e2e 测试，证明 Phase 7 QC 拦截和 Phase 8 Unshipped 查询在真实 service 链路下正确工作。

**文件创建**：`D:/CP6/.codex-task-T1.md` —— 内容如下（直接 copy）：

```markdown
# Task: CP6 Phase 7 + Phase 8 — End-to-End Integration Tests

## Mission

Add **2 end-to-end integration tests** that exercise Phase 7 (QC block shipping) and Phase 8 (Unshipped Orders dashboard) using real services + EF InMemory. Working dir: `D:\CP6`. Must not break the existing 259 passing tests.

## Files to create

| File | Purpose |
|---|---|
| `CP6.Tests/StockQc_AllocateE2ETests.cs` | Phase 7 e2e: 1 PASSED + 1 FAILED + 1 PENDING stock; AllocateAsync demand=50 → asserts PASSED row gets the RSV, FAILED is skipped, T_StockTransaction has correct entries |
| `CP6.Tests/UnshippedOrder_FullCascadeE2ETests.cs` | Phase 8 e2e: real OrderService.CreateAsync → MesBridge → WorkOrderService.IssueAsync → assert UnshippedOrderService.SearchAsync returns 1 row with MesStatusSummary mentioning "Issued" and WmsStatusSummary mentioning the Outbound status |

## Reference patterns

Open and use as templates:
- `CP6.Tests/OrderCancelFullCascadeE2ETests.cs` — real service wiring pattern (NewServiceGraphAsync helper, seeded master data)
- `CP6.Tests/WmsErpClosedLoopTests.cs` — bridge hook chain pattern
- `CP6.Tests/BridgeHookPersistenceTests.cs` — InMemory DB setup + ConfigureWarnings

## Critical context

- `OrderService` constructor signature (current): `(CP6Context db, IPowerEggWorkflowService powerEgg, IWmsBridgeHook wmsBridge, IMesBridgeHook? mesBridge = null, IOrderCancelBridgeHook? cancelBridge = null)`
- `WorkOrderService` constructor: `(CP6Context db, IMesSequenceService seq, IWmsBridgeHook wmsBridge)`
- `WmsBridgeHook` constructor: `(CP6Context db, IOutboundService outbound, IInboundService inbound, ILogger<WmsBridgeHook> logger)`
- `OutboundService` constructor: `(CP6Context db, IWmsSequenceService seq, IStockMovementService stockMovement)`
- For Phase 8 test, seed a BusinessPartner record so CustomerName resolves correctly
- For Phase 7 test, use NewWms helper similar to OutboundServiceTests.cs

## Acceptance criteria

```bash
cd D:\CP6
taskkill /F /IM dotnet.exe 2>/dev/null || true
dotnet build --nologo -v quiet            # 0 errors
dotnet test --no-build --nologo -v quiet  # 259 baseline + 2 new = 261 total, all pass
```

## Report when done

1. Files created
2. Test counts (2 new total)
3. Build result
4. Test result
5. Any deviations
```

**跑完后我做**：build + test 验证、查看代码风格、必要时修正。

---

# T2: Phase 7 自动 QC 联动（QI NG → Stock 自动标 FAILED）

**目标**：在 `QualityInspectionService.CreateAsync` 末尾，当 `OverallResult == 2`（NG）时自动调 `IStockQcService.MarkLinkedStockByWorkOrderAsync`，把该 WO 关联的 Stock 全部标 FAILED。

**文件创建**：`D:/CP6/.codex-task-T2.md`：

```markdown
# Task: CP6 Phase 7 Gap 1.3 — Auto QC Linkage (NG → Stock FAILED)

## Mission

Wire `QualityInspectionService.CreateAsync` to automatically mark related Stock rows as `FAILED` when an inspection result is NG (OverallResult == 2). This closes the Phase 7 spec's auto-link goal. Working dir: `D:\CP6`. Must not break the 259+ existing tests.

## Files to MODIFY (single-method touches)

| File | Change |
|---|---|
| `CP6.Core/Services/Mes/QualityInspectionService.cs` | Inject `IStockQcService` into constructor; in `CreateAsync` after `SaveChangesAsync`, if `dto.OverallResult == 2` AND `dto.WorkOrderNo` not empty, call `_stockQc.MarkLinkedStockByWorkOrderAsync(workOrderNo, "FAILED", $"QC NG: inspection {no}", userName)`. Wrap in try/catch — Stock mark failure must not roll back the QI save. Log via ILogger. |
| `CP6.WebApi/Program.cs` | No change needed (IStockQcService already registered for Phase 7) |

## Tests to add

| File | New tests |
|---|---|
| `CP6.Tests/QualityInspection_AutoQcLinkTests.cs` | 3 tests: (1) NG inspection auto-marks linked Stock FAILED, (2) PASS inspection does NOT change Stock, (3) NG inspection with no linked Stock still saves QI successfully |

## Critical context

- `QualityInspectionService` constructor must be updated; check all existing callers to make sure they compile. Existing tests that mock IQualityInspectionService directly are not affected.
- `IStockQcService.MarkLinkedStockByWorkOrderAsync(workOrderNo, newStatus, reason, userName)` returns `int` (affected count). Already exists in `CP6.Core/Services/Wms/StockQcService.cs`.
- Stock rows are linked to WO via T_InboundReceipt.WorkOrderNo — already implemented in MarkLinkedStockByWorkOrderAsync.

## Acceptance criteria

```bash
cd D:\CP6
taskkill /F /IM dotnet.exe 2>/dev/null || true
dotnet build --nologo -v quiet            # 0 errors
dotnet test --no-build --nologo -v quiet  # ≥ 262 (259 + 3 new)
```

## Report when done

1. Files modified
2. Test counts
3. Build / test result
4. Deviations
```

---

# T3: CSV 导出（未出货 Dashboard）

**目标**：营业实战需要把未出货清单导出 Excel 跟客户对账。加一个 `POST /api/orders/unshipped/export-csv` 端点。

**文件创建**：`D:/CP6/.codex-task-T3.md`：

```markdown
# Task: CP6 Phase 8 — CSV Export for Unshipped Orders

## Mission

Add CSV export to the Phase 8 unshipped orders dashboard endpoint. Working dir: `D:\CP6`. Must not break 262+ existing tests.

## Files to create

| File | Purpose |
|---|---|
| `CP6.Tests/UnshippedOrderCsvExportTests.cs` | Tests for CSV format |

## Files to MODIFY

| File | Change |
|---|---|
| `CP6.Core/Services/IUnshippedOrderService.cs` | Add `Task<byte[]> ExportCsvAsync(UnshippedOrderQuery query)` |
| `CP6.Core/Services/UnshippedOrderService.cs` | Implement: same query as SearchAsync but no paging (cap at 5000 rows); write UTF-8 BOM + CSV with columns: WebOrderNo, CustomerCd, CustomerName, OrderDate, CustomerDeliveryDate, OrderStatus, OrderedQty, ShippedQty, RemainingQty, IsOverdue, MesStatusSummary, WmsStatusSummary. Use `\r\n` line endings. Quote fields containing comma/quote/newline per RFC 4180. |
| `CP6.WebApi/Controllers/UnshippedOrderController.cs` | Add `[HttpPost("export-csv")]` returning `File(bytes, "text/csv", "unshipped-orders-{yyyyMMdd-HHmmss}.csv")` |

## Tests to add (≥4)

1. `Export_BasicRows_ProducesCorrectCsvShape` — 3 rows → CSV has header + 3 data rows, BOM present, columns in spec order
2. `Export_FieldsWithCommas_AreQuoted` — Customer name contains comma → quoted
3. `Export_FieldsWithQuotes_AreEscaped` — Quote becomes "" inside quoted field
4. `Export_NoRows_StillProducesHeader` — Empty result → CSV with header only + BOM

## Acceptance criteria

```bash
cd D:\CP6
dotnet build --nologo -v quiet
dotnet test --no-build --nologo -v quiet  # ≥ 266 (262 + 4)
```

## Report when done

1. Files created/modified
2. Test counts
3. Build / test result
4. Deviations
```

---

# T4: Bridge Health Monitor（24h 成功率 + DLQ Dashboard）

**目标**：从 `T_IntegrationEvent` 聚合最近 24h 的 Bridge Hook 健康度，前端做一个状态面板。面试 demo 高分项。

**文件创建**：`D:/CP6/.codex-task-T4.md`：

```markdown
# Task: CP6 Phase 10b — Bridge Hook Health Monitor

## Mission

Add an aggregation service + endpoint + frontend widget showing 24h bridge hook health metrics. Working dir: `D:\CP6`. Must not break existing tests.

## Files to create (Backend)

| File | Purpose |
|---|---|
| `CP6.Entity/DTOs/BridgeHealthDto.cs` | DTOs: BridgeHealthMetrics, BridgeHookStats, DeadLetterItem |
| `CP6.Core/Services/IBridgeHealthService.cs` | Interface |
| `CP6.Core/Services/BridgeHealthService.cs` | Implementation |
| `CP6.WebApi/Controllers/BridgeHealthController.cs` | REST endpoint |
| `CP6.Tests/BridgeHealthServiceTests.cs` | Unit tests |

## Files to create (Frontend)

| File | Purpose |
|---|---|
| `cp6.web/src/views/wms/BridgeHealthView.vue` | Standalone page (route /wms/bridge-health) |
| `cp6.web/src/api/wms/bridgeHealth.ts` | API client |

## Files to MODIFY

| File | Change |
|---|---|
| `CP6.WebApi/Program.cs` | Register `IBridgeHealthService` as Scoped |
| `cp6.web/src/router/index.ts` | Add `/wms/bridge-health` route |
| `docs/wms-menu-seed.sql` | (Optional) Add menu entry |

## API contract

```
GET /api/bridge-health/metrics
Response: {
  code: 0, message: "OK",
  data: {
    windowStartUtc: "2026-06-04T00:00:00Z",
    windowEndUtc: "2026-06-05T00:00:00Z",
    hooks: [
      { hookName: "OnOrderCreatedAsync", sourceModule: "ERP", targetModule: "MES",
        totalCount: 120, successCount: 117, skippedCount: 2, failedCount: 1, deadLetterCount: 0,
        successRate: 0.975 },
      ...
    ],
    queueDepth: 3,    // current FAILED count waiting for retry
    deadLetters: [   // last 10 DEAD events
      { eventId, hookName, sourceNo, attempts, lastError, createDate }
    ]
  }
}
```

## Tests (≥4)

1. `Metrics_GroupsByHook_Returns24hWindow` — seed 5 events for hook A (3 success, 1 failed, 1 dead), 2 events for hook B → 2 hook entries, A.successRate=0.6
2. `Metrics_EventsOlderThan24h_ExcludedFromCount` — seed event with CreateDate=now-25h → not counted
3. `Metrics_QueueDepth_CountsFailedNotPending` — 3 FAILED + 1 PENDING + 1 SUCCESS → queueDepth=3
4. `Metrics_DeadLetters_LimitedTo10` — seed 15 DEAD events → returns latest 10

## Frontend widget requirements

- 3 KPI cards at top: 24h success rate %, queue depth, dead letter count
- Table per hook: Hook | Source→Target | Total | Success% | Skipped | Failed | Dead
- Dead letter table at bottom: top 10 with manual "Mark Compensated" button (calls additional endpoint `POST /api/bridge-health/compensate/{eventId}` which sets Status=COMPENSATED)
- Auto-refresh every 30s (use setInterval with onUnmounted cleanup)
- Element Plus components, follow existing dashboard styling

## i18n seed

Create `docs/phase10b-bridge-health-i18n-seed.sql` with keys under `wms.bridgeHealth.*` for 5 languages. Include at minimum: title, successRate, queueDepth, deadLetterCount, hookName, totalCount, status labels, compensateBtn, compensateConfirm.

## Acceptance criteria

```bash
cd D:\CP6
dotnet build --nologo -v quiet
dotnet test --no-build --nologo -v quiet  # ≥ 270 (266 + 4)
cd cp6.web && npm run type-check  # 0 errors
```

## Report when done

1. Files created/modified
2. Test counts (Backend new)
3. Build + dotnet test + npm type-check results
4. i18n seed line count
5. Deviations
```

---

# T5: Phase 9 — 材料欠品反流

**目标**：MES 指図発行时 WMS 引当不足 → 不抛异常，改为写 `T_MaterialShortage` + SignalR 推送。让运维能从 dashboard 看到「现在缺什么」。

**文件创建**：`D:/CP6/.codex-task-T5.md`：

```markdown
# Task: CP6 Phase 9 Gap 1.2 — Material Shortage Backflow

## Mission

When `OutboundService.AllocateAsync` (material outbound) hits insufficient stock, instead of throwing `InsufficientStockException`, write a `T_MaterialShortage` record + SignalR push to WmsHub. Provide a service + endpoint to query open shortages. Working dir: `D:\CP6`. Must not break existing tests.

## Files to create

| File | Purpose |
|---|---|
| `CP6.Entity/DomainModels/Wms/MaterialShortage.cs` | Entity |
| `CP6.Core/Services/Wms/IMaterialShortageService.cs` | Interface |
| `CP6.Core/Services/Wms/MaterialShortageService.cs` | Implementation (list / resolve / dismiss) |
| `CP6.WebApi/Controllers/Wms/MaterialShortageController.cs` | REST endpoint |
| `CP6.Core/Migrations/<timestamp>_Phase9AddMaterialShortage.cs` | EF migration |
| `CP6.Tests/MaterialShortageServiceTests.cs` | Tests |
| `CP6.Tests/Outbound_ShortageBackflowTests.cs` | E2E test for the backflow path |

## Files to MODIFY

| File | Change |
|---|---|
| `CP6.Core/EFDbContext/CP6Context.cs` | Add `DbSet<MaterialShortage> MaterialShortages` + index on (Status, DetectedAt) |
| `CP6.Core/Services/Wms/OutboundService.cs` | In AllocateAsync, when material outbound (header.OutboundType == Material) and stock insufficient → write MaterialShortage row + SignalR push instead of throwing |
| `CP6.WebApi/Program.cs` | Register IMaterialShortageService |

## Entity

```csharp
[Table("T_MaterialShortage")]
public class MaterialShortage : BaseBizEntity {
    [Required, MaxLength(20)] public string WorkOrderNo { get; set; } = "";
    [MaxLength(20)] public string? RelatedOutboundNo { get; set; }
    [Required, MaxLength(20)] public string ProductCd { get; set; } = "";
    [MaxLength(30)] public string? LotNo { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal RequiredQty { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal AvailableQty { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    [MaxLength(20)] public string Status { get; set; } = MaterialShortageStatus.Open;
    [MaxLength(500)] public string? Remark { get; set; }
}
public static class MaterialShortageStatus {
    public const string Open = "OPEN";
    public const string Resolved = "RESOLVED";    // user marked resolved after replenishment
    public const string Dismissed = "DISMISSED";  // user dismissed (no action needed)
}
```

## API

- `GET /api/wms/material-shortage?status=OPEN` — list with paging
- `POST /api/wms/material-shortage/{id}/resolve` body: `{ remark }`
- `POST /api/wms/material-shortage/{id}/dismiss` body: `{ remark }`

## OutboundService change

In `AllocateAsync`, the existing logic throws `InsufficientStockException`. Change to:

```csharp
// 既存
?? throw new InsufficientStockException(d.ProductCd, d.LotNo ?? "", needed, 0m);
```

→ Replace with:

```csharp
?? await HandleShortageAsync(header, d, needed, userName);
```

Where `HandleShortageAsync`:
- If `header.OutboundType == OutboundType.Material` → write MaterialShortage row, push SignalR `MaterialShortageDetected` event, return `null` and continue to next detail
- Else (shipping outbound) → still throw InsufficientStockException (current behavior preserved for shipping)

After loop, if any shortage was written, set header.Status to a new status `OutboundOrderStatus.PartialAllocated` (= 5, add this constant if missing).

## Tests (≥6)

Service:
1. `Create_StoresOpenStatus`
2. `Resolve_UpdatesStatus_AndResolvedAt`
3. `Dismiss_UpdatesStatus`
4. `Search_FiltersStatus_OnlyOpen`

E2E:
5. `Allocate_MaterialOutbound_InsufficientStock_WritesShortage_DoesNotThrow`
6. `Allocate_ShippingOutbound_InsufficientStock_StillThrows` (regression: shipping behavior unchanged)

## Acceptance criteria

```bash
cd D:\CP6
dotnet ef migrations add Phase9AddMaterialShortage --project CP6.Core --startup-project CP6.WebApi --output-dir Migrations
dotnet build --nologo -v quiet  # 0 errors
dotnet test --no-build --nologo -v quiet  # ≥ 276 (270 + 6)
```

## Report when done

Standard report.
```

---

# T6: Phase 10a — RMA → ERP CreditNote 回写

**目标**：WMS RMA 確定时通过 `IErpBridgeHook.OnReturnConfirmedAsync`（新增）写回 ERP，生成贷方传票 + 更新 `OrderDetail.ReturnedQty`。

**文件创建**：`D:/CP6/.codex-task-T6.md`：

```markdown
# Task: CP6 Phase 10a — RMA → ERP CreditNote Bridge

## Mission

When WMS RMA is confirmed (RmaHeader.Status moves to a "Confirmed" terminal state), call back into ERP via a new IErpBridgeHook method to generate a CreditNote and update OrderDetail.ReturnedQty. Working dir: `D:\CP6`. Must not break existing tests.

## Files to create

| File | Purpose |
|---|---|
| `CP6.Entity/DomainModels/CreditNote.cs` | New entity T_CreditNote with CreditNoteNo / WebOrderNo / Type (Refund/Exchange/Scrap) / Qty / Amount / Reason / IssueDate |
| `CP6.Core/Migrations/<timestamp>_Phase10aAddCreditNoteAndReturnedQty.cs` | EF migration |
| `CP6.Tests/Rma_ErpCreditNoteE2ETests.cs` | E2E tests |

## Files to MODIFY

| File | Change |
|---|---|
| `CP6.Core/Services/IErpBridgeHook.cs` | Add `Task<ErpBridgeResult> OnReturnConfirmedAsync(string rmaNo, string? userName);` |
| `CP6.Core/Services/Wms/ErpBridgeHook.cs` | Implement OnReturnConfirmedAsync: look up RmaHeader → for each RmaDetail look up matching OrderDetail by (WebOrderNo, ProductCd, LotNo); insert CreditNote row; increment OrderDetail.ReturnedQty by detail.Qty; persist; persist IntegrationEvent (Source=WMS, Target=ERP, HookName=OnReturnConfirmedAsync). Wrap with BridgeHookBase PersistEventAsync. |
| `CP6.Core/Services/Wms/RmaService.cs` | After saving RMA confirm: call `_erpBridge.OnReturnConfirmedAsync(rmaNo, userName)` (Best-Effort, log on failure but don't throw) |
| `CP6.Entity/DomainModels/OrderDetail.cs` | Add `decimal? ReturnedQty` field (nullable, default null) |
| `CP6.Core/EFDbContext/CP6Context.cs` | Add `DbSet<CreditNote> CreditNotes` |
| `CP6.WebApi/Program.cs` | Existing IErpBridgeHook registration unchanged |

## Entity sketch

```csharp
[Table("T_CreditNote")]
public class CreditNote : BaseBizEntity {
    [Required, MaxLength(20)] public string CreditNoteNo { get; set; } = "";
    [MaxLength(20)] public string? WebOrderNo { get; set; }
    [MaxLength(20)] public string? RmaNo { get; set; }
    [Required, MaxLength(20)] public string Type { get; set; } = "REFUND";  // REFUND / EXCHANGE / SCRAP
    [Required, MaxLength(20)] public string CustomerCd { get; set; } = "";
    [MaxLength(20)] public string? ProductCd { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal Qty { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? Amount { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    public DateTime IssueDate { get; set; } = DateTime.Today;
}
```

## Tests (≥4)

1. `RmaConfirm_GeneratesCreditNote_UpdatesOrderDetailReturnedQty`
2. `RmaConfirm_WithNoMatchingOrderDetail_StillCreatesCreditNote_LogsWarn`
3. `RmaConfirm_BridgeFailure_DoesNotRollbackRmaConfirm` (Best-Effort regression)
4. `RmaConfirm_PersistsIntegrationEvent_StatusSuccess`

## Acceptance criteria

```bash
cd D:\CP6
dotnet ef migrations add Phase10aAddCreditNoteAndReturnedQty --project CP6.Core --startup-project CP6.WebApi --output-dir Migrations
dotnet build --nologo -v quiet
dotnet test --no-build --nologo -v quiet  # ≥ 280 (276 + 4)
```

## Report

Standard.
```

---

# C1: Portfolio repo 同步（让 Claude 做）

不适合 codex —— 纯 bash/git + 文件脱敏判断。等 T1-T4 完成后让 Claude 做。

操作：
1. `cd D:/CP6` 改动同步到 `D:/CP6-portfolio`（rsync 或手动 cp，跳过 `cloudflared-docker/`、`backend.local.log` 等）
2. 跑敏感关键词扫一遍
3. `git -C D:/CP6-portfolio add -A && git commit -m "feat: Phase 6/7/8 — Order cancel + QC block + Unshipped dashboard"`
4. `git -C D:/CP6-portfolio push origin main`

---

# C2: 更新 PROJECT_STRUCTURE.md（让 Claude 做）

不适合 codex —— 文档写作需要全局视野 + 风格一致性。等 C1 完成后让 Claude 做。

要更新的章节：
- §2.3 Bridge Hook 表加 `IOrderCancelBridgeHook` 一行（如未加）
- §4.2 ERP 模块清单加「PA070 受注取消」row
- §4.4 WMS 模块清单加「WM-QC Stock QC 管理」row
- §3 业务流程加「取消反向级联」sequence 图
- 新增 §8「Phase 6/7/8 改进汇总」

---

# 跑 Codex 的标准流程提醒

每次跑 Codex 之前：

```bash
# 1. 停 dotnet（防 bin 锁，C2 第一次因此偏离 spec）
cd D:/CP6
taskkill /F /IM dotnet.exe 2>/dev/null

# 2. 跑任务（替换 TX）
codex exec "Read the task spec in D:/CP6/.codex-task-TX.md and implement it end-to-end. Stay strictly within the listed scope. Build the solution and run all tests as proof." \
  -s workspace-write \
  --skip-git-repo-check \
  -c 'model_reasoning_effort="high"'
```

每次跑完，**重启本地 dotnet + vite**：

```bash
cd D:/CP6
nohup dotnet run --project CP6.WebApi --urls "http://localhost:5177" --no-build > backend.local.log 2>&1 &
# vite dev 通常会自动 HMR 不用重启
```

---

# 常见踩坑（基于已经踩过的）

1. **EF migration**：codex 偶尔会手写 migration 文件而不跑 `dotnet ef migrations add`，导致 `CP6ContextModelSnapshot.cs` 不同步。每次跑完检查：
   ```bash
   ls CP6.Core/Migrations/ | grep -E "Phase.*Designer"
   ```
   如果新 migration 没有 `.Designer.cs`，删了重跑 `dotnet ef migrations add` 即可。

2. **bin 锁**：codex 报「process locking files」→ 它的偏离记录会说「used tmp/testbin/ instead」。这是正常的，但用完记得清 `rm -rf D:/CP6/tmp/testbin/`。

3. **Stock 字段返回**：当前 Stock 列表 API 后端可能没显式 select QcStatus 字段（codex 改 Stock entity 但不一定改 search SQL）。如果前端 QC 列显示全 PENDING，去 `StockController.cs` 看 search 返回的 DTO 有没有 qcStatus 字段映射。

4. **PagedResultDto vs PagedResult**：已经踩过一次。后端用 `PagedResultDto<T>.Items`，前端如果新写需要兼容 `items ?? rows`。

5. **SignalR 反馈循环**：dashboard 任何新加的 API call 都不要放在 `loadData()` 里（会被 NewOperLog SignalR 触发的 loadData 调用拉爆）。独立 `onMounted` 触发 + 手动刷新按钮。
