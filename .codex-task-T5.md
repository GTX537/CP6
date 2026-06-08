# Task: CP6 Phase 9 Gap 1.2 — Material Shortage Backflow

## Mission

When `OutboundService.AllocateAsync` (material outbound) hits insufficient stock, instead of throwing `InsufficientStockException`, write a `T_MaterialShortage` record + SignalR push to WmsHub. Provide a service + endpoint to query/resolve/dismiss open shortages. Working dir: `D:\CP6`. Must not break the existing 272 passing tests.

## Critical context (read before coding)

### Existing scope and patterns

- Working dir: `D:\CP6` (.NET 8, C# 12)
- Tests: xUnit + Moq + EF Core InMemory + `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`
- Reference: `CP6.Tests/BridgeHookPersistenceTests.cs` for NewDb() pattern
- All test method names: PascalCase with underscore separators

### Existing entities to reference (DO NOT redefine)

- `OutboundOrder` at `CP6.Entity/DomainModels/Wms/OutboundOrder.cs` has `OutboundType` (1=Material, 2=Shipping)
- `OutboundOrderStatus` constants at `CP6.Entity/DomainModels/Wms/WmsTxnType.cs:83` with values `Draft=0/Confirmed=1/Allocated=2/Picking=3/Completed=4/Cancelled=9`. **You will need to add `PartialAllocated = 5`** to this class if not present.
- `OutboundType.Material = 1` (the value to check for the new shortage backflow path)
- `InsufficientStockException` at `CP6.Core/Services/Wms/InsufficientStockException.cs` — DO NOT remove, still used for shipping outbounds
- `OutboundService.AllocateAsync` at `CP6.Core/Services/Wms/OutboundService.cs:306` — modify only the candidate-throws line
- `WmsHub` at `CP6.WebApi/Hubs/WmsHub.cs` — use `IHubContext<WmsHub>` for SignalR; resolve via reflection like `DeadLetterNotifier` does (avoids circular dep from Core → WebApi)

## Files to create

| File | Purpose |
|---|---|
| `CP6.Entity/DomainModels/Wms/MaterialShortage.cs` | Entity + `MaterialShortageStatus` constants class |
| `CP6.Core/Services/Wms/IMaterialShortageService.cs` | Interface |
| `CP6.Core/Services/Wms/MaterialShortageService.cs` | Implementation (search / resolve / dismiss / create) |
| `CP6.Core/Services/Wms/IMaterialShortageNotifier.cs` | Interface for SignalR push (separate so OutboundService can inject and tests can mock) |
| `CP6.Core/Services/Wms/MaterialShortageNotifier.cs` | Implementation (reflection-resolved IHubContext<WmsHub>, same pattern as DeadLetterNotifier) |
| `CP6.WebApi/Controllers/Wms/MaterialShortageController.cs` | REST endpoint |
| `CP6.Tests/MaterialShortageServiceTests.cs` | Service unit tests |
| `CP6.Tests/Outbound_ShortageBackflowTests.cs` | E2E test for the backflow path on OutboundService |

## Files to MODIFY (minimal targeted changes)

| File | Change |
|---|---|
| `CP6.Core/EFDbContext/CP6Context.cs` | Add `public DbSet<MaterialShortage> MaterialShortages { get; set; }` near the other Wms DbSets; add `e.HasIndex(x => new { x.Status, x.DetectedAt })` and `e.HasIndex(x => x.WorkOrderNo)` in OnModelCreating |
| `CP6.Entity/DomainModels/Wms/WmsTxnType.cs` | Add `public const int PartialAllocated = 5;` to `OutboundOrderStatus` if it's missing (check first) |
| `CP6.Core/Services/Wms/OutboundService.cs` | Inject `IMaterialShortageService _shortage` and `IMaterialShortageNotifier _shortageNotifier`. Modify `AllocateAsync` only — replace the `?? throw new InsufficientStockException(...)` with logic per the "OutboundService change" section below. |
| `CP6.WebApi/Program.cs` | Register `IMaterialShortageService` and `IMaterialShortageNotifier` as Scoped (near the IStockQcService registration around line 130) |

## Entity sketch

```csharp
namespace CP6.Entity.DomainModels.Wms;

[Table("T_MaterialShortage")]
public class MaterialShortage : BaseBizEntity
{
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

public static class MaterialShortageStatus
{
    public const string Open = "OPEN";          // 検出済、対応待ち
    public const string Resolved = "RESOLVED";  // 補充済（手動マーク）
    public const string Dismissed = "DISMISSED"; // 対応不要（手動マーク）
}
```

## OutboundService change

In `CP6.Core/Services/Wms/OutboundService.cs` inside `AllocateAsync`, find the candidate query block. The current code is:

```csharp
var candidate = await _db.Stocks
    .Where(s => ... )
    .OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)
    ...
    .FirstOrDefaultAsync()
    ?? throw new InsufficientStockException(d.ProductCd, d.LotNo ?? "", needed, 0m);
```

**Behavior change**: do NOT throw immediately. Instead:

```csharp
var candidate = await _db.Stocks
    .Where(s => ... unchanged ... )
    .OrderBy(...)
    ...
    .FirstOrDefaultAsync();

if (candidate == null)
{
    // Phase 9 Gap 1.2 — material outbound shortage backflow
    if (header.OutboundType == OutboundType.Material)
    {
        await _shortage.CreateAsync(new MaterialShortage
        {
            Id = Guid.NewGuid(),
            WorkOrderNo = header.WorkOrderNo ?? "",
            RelatedOutboundNo = outboundNo,
            ProductCd = d.ProductCd,
            LotNo = d.LotNo,
            RequiredQty = needed,
            AvailableQty = 0m,
            DetectedAt = DateTime.UtcNow,
            Status = MaterialShortageStatus.Open,
            Creator = userName ?? "system",
            CreateDate = DateTime.Now,
        });
        await _shortageNotifier.NotifyAsync(header.WorkOrderNo ?? "", d.ProductCd, needed);
        anyShortage = true;  // local flag, declared before the foreach
        continue;            // skip to next detail
    }
    // shipping outbound — preserve current throw behavior
    throw new InsufficientStockException(d.ProductCd, d.LotNo ?? "", needed, 0m);
}
```

After the foreach loop in `AllocateAsync`, if `anyShortage` is true:
- Set `header.Status = OutboundOrderStatus.PartialAllocated;` (the new constant)
- Do NOT throw

Otherwise leave existing behavior (set header.Status = Allocated as before).

## IMaterialShortageService API

```csharp
public interface IMaterialShortageService
{
    Task<MaterialShortage> CreateAsync(MaterialShortage entity);
    Task<PagedResultDto<MaterialShortage>> SearchAsync(MaterialShortageQuery query);
    Task<MaterialShortage> ResolveAsync(Guid id, string? remark, string? userName);
    Task<MaterialShortage> DismissAsync(Guid id, string? remark, string? userName);
}

public class MaterialShortageQuery
{
    public string? Status { get; set; }  // null = all
    public string? WorkOrderNo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
```

`PagedResultDto<T>` already exists at `CP6.Entity/DTOs/Mes/PagedResultDto.cs` with `Items / Total / PageIndex / PageSize` shape — reuse it (the existing C3/Phase 8 task does so).

## IMaterialShortageNotifier API

```csharp
public interface IMaterialShortageNotifier
{
    Task NotifyAsync(string workOrderNo, string productCd, decimal requiredQty);
}
```

Implementation: same reflection pattern as `CP6.Core/Services/DeadLetterNotifier.cs` — resolve `IHubContext<WmsHub>` via `IServiceProvider` + `Type.GetType("CP6.WebApi.Hubs.WmsHub, CP6.WebApi")` + `MakeGenericType`. Push SignalR event `"MaterialShortageDetected"` with payload `{ WorkOrderNo, ProductCd, RequiredQty, DetectedAt }`. Wrap in try/catch + log via ILogger; failures must NOT propagate.

## Controller

- `GET  /api/wms/material-shortage` — query string `status`, `workOrderNo`, `page`, `pageSize` → calls `SearchAsync`
- `POST /api/wms/material-shortage/{id}/resolve` body `{ remark }` → calls `ResolveAsync`
- `POST /api/wms/material-shortage/{id}/dismiss` body `{ remark }` → calls `DismissAsync`

All responses use the project standard `{ code, message, data }` shape. `[Authorize]` attribute.

## EF Migration

Run from `D:\CP6`:

```bash
taskkill /F /IM dotnet.exe 2>/dev/null || true
dotnet ef migrations add Phase9AddMaterialShortage --project CP6.Core --startup-project CP6.WebApi --output-dir Migrations --no-build
```

Verify both `.Designer.cs` and updated `CP6ContextModelSnapshot.cs` are generated alongside the migration file. Do NOT hand-write the migration file (this causes snapshot drift).

## Tests

### MaterialShortageServiceTests (≥4 tests)

1. `Create_StoresOpenStatus_AndPersistsKeyFields` — service.Create persists with Status=OPEN, DetectedAt set, key fields preserved
2. `Resolve_TransitionsStatus_SetsResolvedAtAndUserStamp` — Open → Resolved, ResolvedAt non-null, Modifier=userName
3. `Resolve_AlreadyTerminal_ThrowsInvalidOperation` — Resolving a Dismissed row throws "WM-MSG-SHORTAGE-409"
4. `Search_FiltersOpenStatus_ReturnsOnlyOpen` — seed 2 OPEN, 1 RESOLVED, 1 DISMISSED → query `Status=OPEN` returns 2

### Outbound_ShortageBackflowTests (≥2 tests)

5. `Allocate_MaterialOutbound_InsufficientStock_WritesShortage_DoesNotThrow` — seed material OutboundOrder with detail required=100, Stock available=0 → AllocateAsync completes without exception, T_MaterialShortage has 1 OPEN row matching (WO, ProductCd, 100), OutboundOrder.Status == PartialAllocated, Notifier mock called once
6. `Allocate_ShippingOutbound_InsufficientStock_StillThrows_NoShortageWritten` — same setup but OutboundType=Shipping → InsufficientStockException thrown, T_MaterialShortage row count unchanged (regression guard for non-material path)

## Acceptance criteria — verify BEFORE you stop

```bash
cd D:\CP6
taskkill /F /IM dotnet.exe 2>/dev/null || true
dotnet build --nologo -v quiet            # 0 errors
dotnet test --no-build --nologo -v quiet  # ≥ 278 (272 baseline + 6 new); existing 272 MUST all still pass
```

If existing tests fail you broke regression — STOP and report which tests.

## Voice & style

- C# 12 / .NET 8
- Match existing brace/comment style in `OutboundService.cs` and `BridgeHookBase.cs`
- Japanese XML doc comments OK; bilingual where it helps reading
- Async methods end in `Async`
- Use `System.Text.Json` (not Newtonsoft)
- All `Notifier.NotifyAsync` failures wrapped in try/catch — never throw to the caller

## Report when done

1. Files created (paths)
2. Files modified (paths)
3. Migration name + timestamp
4. Test counts: service tests / e2e tests / total new
5. `dotnet build` result
6. `dotnet test` result (passed / failed / total)
7. Deviations from spec and why

If you cannot run `dotnet ef migrations add` (e.g. permission, locked bin), report the exact error and a suggested fix. DO NOT hand-write the migration to bypass — that breaks snapshot.
