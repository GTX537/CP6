# Task: CP6 Phase 7 Gap 1.3 (Backend) — QC Block Shipping for Failed Stock

## Mission

Implement the **minimum viable Phase 7 Gap 1.3 backend**: add a `QcStatus` field to `Stock`, make `OutboundService.AllocateAsync` skip `FAILED` and `HOLD` stock, expose an endpoint + service method to set the QC status, and provide a helper to auto-mark stock created by a specific WorkOrder's production-inbound chain.

Working dir: `D:\CP6`. Must not break the existing 240+ passing tests.

**Frontend is out of scope** — backend + tests only. Frontend handover is a separate phase.

## Critical context (read before coding)

### Existing entities you will touch

```csharp
// CP6.Entity/DomainModels/Wms/Stock.cs (lines 1-70 already exist — DO NOT delete existing fields)
[Table("T_Stock")]
public class Stock : BaseBizEntity {
    public string WarehouseCd { get; set; }
    public string LocationCd { get; set; }
    public string ProductCd { get; set; }
    public string LotNo { get; set; }
    public decimal PhysicalQty { get; set; }
    public decimal AllocatedQty { get; set; }
    public decimal AvailableQty { get; set; }
    public string? UnitCd { get; set; }
    public DateTime? ReceiveDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal? UnitPrice { get; set; }
    public bool RecallFlag { get; set; }
    public string OwnerType { get; set; }
    public string? OwnerCd { get; set; }
    public string? PaperRollNo { get; set; }
    // YOU ADD: public string QcStatus { get; set; } = StockQcStatus.Pending;
}
```

### Existing services you will modify

```csharp
// CP6.Core/Services/Wms/OutboundService.cs:306 (AllocateAsync)
// Look for the candidate query around line 326-337.
// You will add a QcStatus filter to that LINQ where clause.

// CP6.Core/Services/Wms/IStockMovementService.cs — DO NOT modify (it's a low-level RSV/UNRSV interface; QcStatus is a separate concern)
```

### Existing related references

- `WarehouseType.Defective = 4` exists at `CP6.Entity/DomainModels/Wms/WmsTxnType.cs:34`
- `InboundReceipt` records have `WorkOrderNo` field — `Phase 7b will wire QualityInspection → Stock via InboundReceipt, but THAT is OUT OF SCOPE for this task`.

### Reference for existing OutboundService allocation pattern

Open `CP6.Core/Services/Wms/OutboundService.cs:306-360` (the `AllocateAsync` method) and replicate its style.

### Reference for existing tests pattern

- `CP6.Tests/OutboundServiceTests.cs` — for OutboundService allocation tests
- `CP6.Tests/StockMovementServiceTests.cs` — for Stock manipulation tests

Use xUnit + Moq + EF Core InMemory + `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`.

## Files to create

| File | Purpose |
|---|---|
| `CP6.Core/Services/Wms/IStockQcService.cs` | Interface for QC status management |
| `CP6.Core/Services/Wms/StockQcService.cs` | Implementation |
| `CP6.WebApi/Controllers/Wms/StockQcController.cs` | REST endpoint |
| `CP6.Core/Migrations/<timestamp>_Phase7AddStockQcStatus.cs` | EF migration |
| `CP6.Tests/StockQcServiceTests.cs` | Unit tests for the service |
| `CP6.Tests/OutboundService_QcFilter_Tests.cs` | Unit tests proving AllocateAsync filters by QcStatus |

## Files to MODIFY (minimal targeted changes — preserve all other code)

| File | Change |
|---|---|
| `CP6.Entity/DomainModels/Wms/Stock.cs` | Add `QcStatus` property + add `StockQcStatus` static class at file end |
| `CP6.Core/EFDbContext/CP6Context.cs` | Add `e.HasIndex(x => x.QcStatus)` to the Stock entity config (and OnModelCreating registration for the Phase 7 block at the bottom) |
| `CP6.Core/Services/Wms/OutboundService.cs` | Add `&& s.QcStatus != StockQcStatus.Failed && s.QcStatus != StockQcStatus.Hold` to the candidate query inside `AllocateAsync` |
| `CP6.WebApi/Program.cs` | Register `IStockQcService` as `Scoped` (add ONE line near the other Wms service registrations around line 110) |

**DO NOT** modify existing field defaults. **DO NOT** change `Stock.AvailableQty` semantics. **DO NOT** add fields to `Sys_OperLog` or other unrelated entities.

## Detailed requirements

### 1. Entity changes

In `CP6.Entity/DomainModels/Wms/Stock.cs`, before the closing `}` of the `Stock` class, add:

```csharp
/// <summary>
/// QC ステータス（Phase 7 Gap 1.3）：
///   PENDING（既定、未検査）/ PASSED（検査合格）/ FAILED（検査不合格、引当禁止）/ HOLD（手動保留、引当禁止）
/// </summary>
[Required, MaxLength(10)]
public string QcStatus { get; set; } = StockQcStatus.Pending;
```

Then at file end (outside the `Stock` class but in same namespace), add:

```csharp
public static class StockQcStatus
{
    public const string Pending = "PENDING";
    public const string Passed = "PASSED";
    public const string Failed = "FAILED";
    public const string Hold = "HOLD";

    /// <summary>Phase 7: 引当可能か（PENDING と PASSED は OK、FAILED と HOLD は NG）</summary>
    public static bool IsAllocatable(string status) =>
        status == Pending || status == Passed;
}
```

### 2. EF Migration

Use `dotnet ef migrations add Phase7AddStockQcStatus --project CP6.Core --startup-project CP6.WebApi --output-dir Migrations`.

The migration should:
- Add `QcStatus nvarchar(10) NOT NULL DEFAULT 'PENDING'` to `T_Stock`
- Add `CREATE INDEX IX_T_Stock_QcStatus ON T_Stock(QcStatus)` for fast allocation filtering

If you cannot run the EF tool because of the running dotnet process locking bin, kill it first: `taskkill /F /IM dotnet.exe`.

Edit the generated `Up()` method if necessary to ensure `defaultValue: "PENDING"` so existing rows are migrated cleanly.

### 3. OutboundService.AllocateAsync filter

In `CP6.Core/Services/Wms/OutboundService.cs` inside `AllocateAsync`, find the candidate query:

```csharp
var candidate = await _db.Stocks
    .Where(s => s.ProductCd == d.ProductCd
                && s.WarehouseCd == header.WarehouseCd
                && !s.IsDeleted
                && !s.RecallFlag
                && s.AvailableQty >= needed
                && s.OwnerType == StockOwnerType.Self)
    .OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)
    ...
```

Add this exact filter (don't reorder existing clauses):

```csharp
                && s.OwnerType == StockOwnerType.Self
                && s.QcStatus != StockQcStatus.Failed
                && s.QcStatus != StockQcStatus.Hold)
```

### 4. IStockQcService + impl

```csharp
namespace CP6.Core.Services.Wms;

public interface IStockQcService
{
    /// <summary>Set QcStatus for a single Stock row (manual operator action).</summary>
    Task<Stock> SetStockQcStatusAsync(Guid stockId, string newStatus, string? reason, string? userName);

    /// <summary>
    /// Mark all Stock rows that were produced via the given WorkOrder's
    /// production-inbound chain. Looks up T_InboundReceipt where WorkOrderNo
    /// matches and then bulk-updates Stock rows whose (ProductCd, LotNo)
    /// pair matches one of the receipt detail rows.
    /// Returns affected row count.
    /// </summary>
    Task<int> MarkLinkedStockByWorkOrderAsync(string workOrderNo, string newStatus, string? reason, string? userName);
}

public class StockQcService : IStockQcService
{
    private readonly CP6Context _db;
    private readonly ILogger<StockQcService> _logger;
    public StockQcService(CP6Context db, ILogger<StockQcService> logger) { _db = db; _logger = logger; }

    public async Task<Stock> SetStockQcStatusAsync(Guid stockId, string newStatus, string? reason, string? userName)
    {
        // 1. validate newStatus is one of the 4 constants → throw ArgumentException with "WM-MSG-QC-001" prefix
        // 2. load Stock by Id, !IsDeleted → if not found, throw InvalidOperationException "WM-MSG-QC-404"
        // 3. update Stock.QcStatus, Stock.Modifier, Stock.ModifyDate
        // 4. SaveChangesAsync
        // 5. log via _logger.LogInformation: [StockQc] {stockId} {oldStatus}→{newStatus} reason={reason}
        // 6. return the updated Stock
    }

    public async Task<int> MarkLinkedStockByWorkOrderAsync(string workOrderNo, string newStatus, string? reason, string? userName)
    {
        // 1. validate newStatus
        // 2. query T_InboundReceiptDetail where parent T_InboundReceipt.WorkOrderNo == workOrderNo and !IsDeleted
        //    SELECT DISTINCT ProductCd, LotNo
        // 3. for each (ProductCd, LotNo) pair, update all matching T_Stock rows
        // 4. SaveChangesAsync
        // 5. return total affected count
        //
        // Implementation hint: use a single bulk UPDATE via EF Core's ExecuteUpdateAsync if InMemory provider supports it,
        // OR fall back to load-then-update if simpler. Either is acceptable.
    }
}
```

### 5. REST endpoint

```csharp
// CP6.WebApi/Controllers/Wms/StockQcController.cs

[ApiController]
[Route("api/wms/stock-qc")]
[Authorize]
public class StockQcController : ControllerBase
{
    private readonly IStockQcService _service;
    public StockQcController(IStockQcService service) { _service = service; }
    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>POST /api/wms/stock-qc/{stockId}/set</summary>
    [HttpPost("{stockId:guid}/set")]
    public async Task<IActionResult> SetSingle(Guid stockId, [FromBody] SetStockQcRequest req)
    {
        // validate req.NewStatus not empty, return BadRequest with code 400 if missing
        var s = await _service.SetStockQcStatusAsync(stockId, req.NewStatus, req.Reason, CurrentUser);
        return Ok(new { code = 0, message = "OK", data = new { stockId = s.Id, qcStatus = s.QcStatus } });
    }

    /// <summary>POST /api/wms/stock-qc/by-work-order/{workOrderNo}</summary>
    [HttpPost("by-work-order/{workOrderNo}")]
    public async Task<IActionResult> MarkByWO(string workOrderNo, [FromBody] SetStockQcRequest req)
    {
        var affected = await _service.MarkLinkedStockByWorkOrderAsync(workOrderNo, req.NewStatus, req.Reason, CurrentUser);
        return Ok(new { code = 0, message = "OK", data = new { workOrderNo, affected } });
    }
}

public class SetStockQcRequest
{
    public string NewStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
```

Match the existing controller response shape `{ code, message, data }` exactly.

### 6. Program.cs DI registration

Find the existing Wms scoped registrations (around line 110 you'll see `IStockMovementService`, `IOutboundService`, `IInboundService` etc.) and append:

```csharp
builder.Services.AddScoped<CP6.Core.Services.Wms.IStockQcService, CP6.Core.Services.Wms.StockQcService>();
```

## Testing

### StockQcServiceTests (≥6 tests)

1. `SetStockQcStatusAsync_ValidTransition_UpdatesAndReturns` — Pending → Failed, assert DB row updated
2. `SetStockQcStatusAsync_InvalidStatus_ThrowsArgumentException` — pass "BOGUS" → ArgumentException with "WM-MSG-QC-001"
3. `SetStockQcStatusAsync_StockNotFound_ThrowsInvalidOperation` — non-existent Guid → InvalidOperationException "WM-MSG-QC-404"
4. `MarkLinkedStockByWorkOrderAsync_UpdatesAllMatchingStocks` — seed 1 WO with 2 InboundReceiptDetails (different ProductCd/LotNo), 3 Stock rows matching, 1 NOT matching → 3 affected, the non-matching one untouched
5. `MarkLinkedStockByWorkOrderAsync_NoReceipts_ReturnsZero` — WO has no Receipt records → 0 affected, no exception
6. `MarkLinkedStockByWorkOrderAsync_PreservesUnrelatedFields` — affected Stock rows: PhysicalQty/AllocatedQty/RecallFlag unchanged

### OutboundService_QcFilter_Tests (≥4 tests)

1. `Allocate_OnlyPickPassedAndPending_NotFailed` — seed 3 Stock rows for same ProductCd+Warehouse: one PASSED qty=100, one FAILED qty=100, one PENDING qty=100. OutboundOrder demand=50. Assert: allocation hit the PASSED row (preferred by FEFO since codex spec allows either PASSED or PENDING through; if both FEFO-eligible, just assert it did NOT hit the FAILED one).
2. `Allocate_NoEligibleStock_ThrowsInsufficient` — seed only FAILED + HOLD rows → InsufficientStockException
3. `Allocate_PendingIsAllocatable_BackwardCompatible` — seed only PENDING rows → allocation succeeds (proves we did not break existing data which has QcStatus=PENDING by default after migration)
4. `Allocate_HoldStockIsSkipped` — same as test 1 but with HOLD instead of FAILED, assert skipped

## Acceptance criteria — verify BEFORE you stop

```bash
cd D:\CP6
taskkill /F /IM dotnet.exe 2>/dev/null || true
dotnet build --nologo -v quiet
# expect: 0 errors

dotnet test --no-build --nologo -v quiet
# expect: total ≥ 250 (current 236 + S7 contributions if S7 already ran + 10 from this task)
# if existing tests fail, you broke regression — STOP and report
```

## Voice & style rules

- C# 12 / .NET 8, xUnit + Moq + InMemory DB
- Match comment / brace style with existing `OutboundService.cs` and `StockMovementService.cs`
- Japanese-language XML doc comments OK
- All async methods end in `Async`
- Use `System.Text.Json` (not Newtonsoft) if you serialize anything

## Report when done

1. Files created (paths)
2. Files modified (paths + which lines)
3. Migration name + generated timestamp
4. Test counts: StockQcService new / OutboundService_QcFilter new / total new
5. `dotnet build` result
6. `dotnet test` result (passed / failed / total)
7. Deviations from this spec and why

If you cannot generate the EF migration (e.g., dotnet-ef not installed or fails), report the exact error message and a suggested workaround — DO NOT skip the migration silently.
