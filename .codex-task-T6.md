# Task: CP6 Phase 10a — RMA → ERP CreditNote Bridge

## Mission

When WMS RMA is confirmed (`RmaService.ConfirmAsync` or equivalent), call back into ERP via a new `IErpBridgeHook` method to generate a `CreditNote` row and increment `OrderDetail.ReturnedQty`. The bridge follows the existing `BridgeHookBase` persistence pattern (writes to `T_IntegrationEvent`). Working dir: `D:\CP6`. Must not break the existing 278 passing tests (or whatever the current count is after T5).

## Critical context (read before coding)

### Existing pieces to USE (DO NOT recreate)

- `IErpBridgeHook` at `CP6.Core/Services/IErpBridgeHook.cs` — already has `OnShipmentConfirmedAsync`. You ADD a method, don't break the existing one.
- `ErpBridgeHook` at `CP6.Core/Services/Wms/ErpBridgeHook.cs` — inherits `BridgeHookBase`, already injected with CP6Context + ILogger
- `BridgeHookBase.PersistEventAsync(...)` — writes T_IntegrationEvent. Existing 5 hooks use it. Follow the same pattern.
- `IntegrationEventStatus` constants at `CP6.Entity/DomainModels/IntegrationEvent.cs`
- `RmaHeader` at `CP6.Entity/DomainModels/Wms/RmaHeader.cs`
- `RmaDetail` at `CP6.Entity/DomainModels/Wms/RmaDetail.cs` — typically has Qty, ProductCd, LotNo, RmaNo
- `RmaService` at `CP6.Core/Services/Wms/RmaService.cs` — find the confirm flow (look for a method that transitions RmaHeader status to a confirmed/closed terminal state)
- `OrderDetail` at `CP6.Entity/DomainModels/OrderDetail.cs` — add `ReturnedQty` field

### Tests reference patterns

- `CP6.Tests/BridgeHookPersistenceTests.cs` for IntegrationEvent persistence assertions
- `CP6.Tests/WmsErpClosedLoopTests.cs` for full e2e bridge chain pattern (real services)

## Files to create

| File | Purpose |
|---|---|
| `CP6.Entity/DomainModels/CreditNote.cs` | Entity + `CreditNoteType` constants (REFUND / EXCHANGE / SCRAP) |
| `CP6.Core/Migrations/<timestamp>_Phase10aAddCreditNoteAndReturnedQty.cs` | EF migration (generated via dotnet ef) |
| `CP6.Tests/Rma_ErpCreditNoteE2ETests.cs` | E2E tests |

## Files to MODIFY (minimal targeted)

| File | Change |
|---|---|
| `CP6.Core/Services/IErpBridgeHook.cs` | Add `Task<ErpBridgeResult> OnReturnConfirmedAsync(string rmaNo, string? userName);` |
| `CP6.Core/Services/Wms/ErpBridgeHook.cs` | Implement OnReturnConfirmedAsync (full impl below). Also implement on `NoOpErpBridgeHook` (returns Skipped). |
| `CP6.Core/Services/Wms/NoOpErpBridgeHook.cs` (if exists) OR wherever the NoOp impl lives | Add `OnReturnConfirmedAsync` returning `ErpBridgeResult.Skipped("ErpBridge:Enabled=false")` |
| `CP6.Core/Services/Wms/RmaService.cs` | In the confirm method, after `SaveChangesAsync`, call `await _erpBridge.OnReturnConfirmedAsync(rmaNo, userName)`. Wrap in try/catch + log warning. Must NOT roll back the RMA save (Best-Effort principle). Inject `IErpBridgeHook` if not already injected. |
| `CP6.Entity/DomainModels/OrderDetail.cs` | Add `[Column(TypeName = "decimal(21,8)")] public decimal? ReturnedQty { get; set; }` (nullable, default null) |
| `CP6.Core/EFDbContext/CP6Context.cs` | Add `public DbSet<CreditNote> CreditNotes { get; set; }` near other ERP-related DbSets; add `e.HasIndex(x => x.CreditNoteNo).IsUnique()` and `e.HasIndex(x => x.WebOrderNo)` |
| `CP6.WebApi/Program.cs` | No change needed (IErpBridgeHook already registered) |

## Entity sketch

```csharp
namespace CP6.Entity.DomainModels;

[Table("T_CreditNote")]
public class CreditNote : BaseBizEntity
{
    [Required, MaxLength(20)] public string CreditNoteNo { get; set; } = "";
    [MaxLength(20)] public string? WebOrderNo { get; set; }
    [MaxLength(20)] public string? RmaNo { get; set; }
    [Required, MaxLength(20)] public string Type { get; set; } = CreditNoteType.Refund;
    [Required, MaxLength(20)] public string CustomerCd { get; set; } = "";
    [MaxLength(20)] public string? ProductCd { get; set; }
    [MaxLength(30)] public string? LotNo { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal Qty { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? Amount { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    public DateTime IssueDate { get; set; } = DateTime.Today;
}

public static class CreditNoteType
{
    public const string Refund   = "REFUND";    // 返金
    public const string Exchange = "EXCHANGE";  // 交換
    public const string Scrap    = "SCRAP";     // 廃棄
}
```

CreditNoteNo numbering: use `$"CN{DateTime.Today:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpperInvariant()}"` — that's good enough for this phase. If `DocNumber` infrastructure handles it, use that instead (see `CP6.Core/Services/DocNumber.cs`).

## ErpBridgeHook.OnReturnConfirmedAsync implementation

Flow:

1. Look up `RmaHeader` by `rmaNo` (no-tracking). If null → return `ErpBridgeResult.Skipped("WM-MSG-RMA-404")`, persist Skipped IntegrationEvent.
2. Look up `RmaDetail` rows by RmaNo, !IsDeleted.
3. For each detail:
   - Find matching `OrderDetail` by (WebOrderNo on header, ProductCd, LotNo). If not found, log warning but still create the CreditNote (no OrderDetail update).
   - If found, increment `OrderDetail.ReturnedQty = (ReturnedQty ?? 0) + detail.Qty`, set Modifier/ModifyDate.
   - Create a new `CreditNote` row: Type=Refund (default for Phase 10a — RMA type → CreditNote type mapping is Phase 10b future work), CustomerCd from RmaHeader, Qty=detail.Qty, RmaNo=rmaNo.
4. `await _db.SaveChangesAsync()`.
5. Log Info: `"[ERP-Bridge] RMA {RmaNo} → {N} credit notes generated, returned qty back-written"`
6. Persist IntegrationEvent: SourceModule="WMS", TargetModule="ERP", HookName=nameof(OnReturnConfirmedAsync), SourceNo=rmaNo, TargetNo=null or first CreditNoteNo, Status=Success.
7. Return `ErpBridgeResult.Ok(rmaNo)`.

On `InvalidOperationException` → catch, persist Skipped, return `Skipped(ex.Message)`.
On other Exception → catch, persist Failed, return `Failed(ex.Message)`.

Follow exact try/catch shape from `WmsBridgeHook.OnWorkOrderIssuedAsync` (already in the codebase).

## EF Migration

```bash
cd D:\CP6
taskkill /F /IM dotnet.exe 2>/dev/null || true
dotnet ef migrations add Phase10aAddCreditNoteAndReturnedQty --project CP6.Core --startup-project CP6.WebApi --output-dir Migrations --no-build
```

Verify `.Designer.cs` and updated `CP6ContextModelSnapshot.cs` were generated. DO NOT hand-write — that breaks snapshot.

## Tests (≥4)

1. `RmaConfirm_GeneratesCreditNote_UpdatesOrderDetailReturnedQty` — seed Order with 1 detail Qty=100, RmaHeader confirmed with detail.Qty=10 → after confirm: 1 CreditNote row inserted, OrderDetail.ReturnedQty == 10, T_IntegrationEvent has 1 Success row with HookName="OnReturnConfirmedAsync"
2. `RmaConfirm_NoMatchingOrderDetail_StillCreatesCreditNote_LogsWarning` — seed RmaDetail with ProductCd that has no matching OrderDetail → CreditNote still created (linked to CustomerCd), no OrderDetail update, no exception
3. `RmaConfirm_BridgeFailure_DoesNotRollbackRmaConfirm` — inject a mock CP6Context that throws on CreditNote save → RMA save still succeeds (RmaHeader.Status changed), bridge returns Failed, IntegrationEvent persisted with Status=Failed. Tests Best-Effort regression.
4. `OnReturnConfirmedAsync_RmaNotFound_ReturnsSkipped_PersistsIntegrationEvent` — call bridge with unknown rmaNo → returns Skipped, IntegrationEvent persisted with Status=Skipped

## Acceptance criteria

```bash
cd D:\CP6
taskkill /F /IM dotnet.exe 2>/dev/null || true
dotnet build --nologo -v quiet            # 0 errors
dotnet test --no-build --nologo -v quiet  # ≥ 282 (278 + 4 new); existing tests MUST all pass
```

## Style rules

- C# 12 / .NET 8
- Match existing `WmsBridgeHook.cs` / `ErpBridgeHook.cs` brace + comment style
- Japanese-language XML doc comments encouraged
- Async methods end in `Async`
- Use `System.Text.Json`
- All bridge invocations from RmaService must be Best-Effort: try/catch + log + don't propagate

## Report when done

1. Files created
2. Files modified
3. Migration name + timestamp + verified Designer/Snapshot present
4. Test counts (per file + total new)
5. `dotnet build` result
6. `dotnet test` result (passed / failed / total)
7. Deviations from spec and why
