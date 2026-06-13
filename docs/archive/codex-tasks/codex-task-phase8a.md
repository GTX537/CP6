# Task: CP6 Phase 8 Gap 3.4 (Backend) — Unshipped Orders Dashboard

## Mission

Implement the **backend** for the "Unshipped Orders" dashboard widget (Phase 8 Gap 3.4 per `docs/PROJECT_IMPROVEMENT_PLAN.md`). The widget shows orders that are placed but not fully shipped, joined with the current MES and WMS status. **Frontend Vue widget is OUT OF SCOPE** — backend service + controller + tests only.

Working dir: `D:\CP6`. Must not break any existing test.

## Critical context

### Existing entities (read these to confirm field names — do NOT redefine)

- `Order` at `CP6.Entity/DomainModels/Order.cs` — has `WebOrderNo / CustomerCd / OrderDate / CustomerDeliveryDate / OrderStatus (string Phase 6 lifecycle, e.g. CONFIRMED) / ShipStatus (int: 0/5/9) / ActualShipDate`
- `OrderDetail` at `CP6.Entity/DomainModels/OrderDetail.cs` — has `WebOrderNo / WebOrderDetailNo / ProductCd / Quantity / ShippedQty (decimal?) / ShipStatus (int) / LastShipDate`
- `WorkOrder` at `CP6.Entity/DomainModels/Mes/WorkOrder.cs` — has `WorkOrderNo / WebOrderNo / Status (int with WorkOrderStatus constants) / PlanEndDate / PlanStartDate`
- `OutboundOrder` at `CP6.Entity/DomainModels/Wms/OutboundOrder.cs` — has `OutboundNo / WebOrderNo / WorkOrderNo / Status (int with OutboundOrderStatus constants) / OutboundType (1=Material, 2=Shipping)`
- `BusinessPartner` at `CP6.Entity/DomainModels/BusinessPartner.cs` — has `BpCd / BpName / BpKana`
- `OrderLifecycleStatus` constants at `CP6.Entity/DomainModels/Order.cs`: `CONFIRMED / IN_PRODUCTION / SHIPPED / CANCELLED / PARTIALLY_CANCELLED`

### What counts as "unshipped"

An Order is **unshipped** if `Order.ShipStatus < 9` (i.e., not fully shipped) AND `Order.OrderStatus NOT IN ('SHIPPED', 'CANCELLED')`. PartiallyCancelled or PartiallyShipped is still considered unshipped for this view.

## Files to create

| File | Purpose |
|---|---|
| `CP6.Entity/DTOs/UnshippedOrderDto.cs` | DTOs (item + query) |
| `CP6.Core/Services/IUnshippedOrderService.cs` | Interface |
| `CP6.Core/Services/UnshippedOrderService.cs` | Implementation |
| `CP6.WebApi/Controllers/UnshippedOrderController.cs` | REST endpoint |
| `CP6.Tests/UnshippedOrderServiceTests.cs` | Unit tests |

## Files to MODIFY (single-line additions only — preserve everything else)

| File | Change |
|---|---|
| `CP6.WebApi/Program.cs` | Add `builder.Services.AddScoped<IUnshippedOrderService, UnshippedOrderService>();` near other Order-related registrations (around line 99 where `IOrderService` lives) |

## Detailed requirements

### 1. DTOs

```csharp
namespace CP6.Entity.DTOs;

/// <summary>Dashboard 受注済未出荷 行アイテム</summary>
public class UnshippedOrderItemDto
{
    public string WebOrderNo { get; set; } = string.Empty;
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public DateTime? OrderDate { get; set; }
    public DateTime? CustomerDeliveryDate { get; set; }
    /// <summary>Phase 6 受注 lifecycle (CONFIRMED / IN_PRODUCTION / PARTIALLY_CANCELLED)</summary>
    public string OrderStatus { get; set; } = string.Empty;
    /// <summary>0=未出荷 / 5=一部出荷 / 9=出荷済</summary>
    public int ShipStatus { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ShippedQty { get; set; }
    /// <summary>Remaining = OrderedQty - ShippedQty</summary>
    public decimal RemainingQty { get; set; }
    /// <summary>納期超過 = CustomerDeliveryDate &lt; today</summary>
    public bool IsOverdue { get; set; }
    public int? DaysUntilDue { get; set; }
    /// <summary>Aggregated MES status across related WOs — e.g. "2 WOs InProgress" or "All Issued"</summary>
    public string? MesStatusSummary { get; set; }
    /// <summary>Aggregated WMS status across related Outbounds — e.g. "1 Allocated, 1 Picking"</summary>
    public string? WmsStatusSummary { get; set; }
}

public class UnshippedOrderQuery
{
    public string? CustomerCd { get; set; }
    public bool? OnlyOverdue { get; set; }
    /// <summary>Pagination — page is 1-indexed</summary>
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SortField { get; set; } // "deliveryDate" / "orderDate" / "remainingQty"
    public string? SortOrder { get; set; } // "asc" / "desc"
}
```

### 2. IUnshippedOrderService + impl

```csharp
namespace CP6.Core.Services;

public interface IUnshippedOrderService
{
    /// <summary>
    /// Search orders that are placed but not yet fully shipped, joined with current MES + WMS status.
    /// </summary>
    Task<PagedResultDto<UnshippedOrderItemDto>> SearchAsync(UnshippedOrderQuery query);
}
```

Implementation rules:
- Use `CP6Context` directly (don't go through a generic repository)
- Filter: `Order.ShipStatus < 9 AND Order.OrderStatus NOT IN ('SHIPPED', 'CANCELLED') AND Order.IsDeleted = false`
- Left-join `BusinessPartner` by `Order.CustomerCd = BusinessPartner.BpCd` for `CustomerName` (fall back to CustomerCd if no BP record)
- Aggregate `OrderDetail` sums for `OrderedQty` (sum of `Quantity ?? 0`) and `ShippedQty` (sum of `ShippedQty ?? 0`); `RemainingQty = OrderedQty - ShippedQty`
- `IsOverdue = CustomerDeliveryDate != null && CustomerDeliveryDate < DateTime.Today`
- `DaysUntilDue = CustomerDeliveryDate != null ? (int)(CustomerDeliveryDate - DateTime.Today).TotalDays : null` (negative if overdue)
- `MesStatusSummary` = aggregated `WorkOrder.Status` across related WOs (WHERE WorkOrder.WebOrderNo == Order.WebOrderNo AND Status != Cancelled). Output format: `"3 WOs (1 Issued, 2 InProgress)"`. If no WO: `"No WO"`.
- `WmsStatusSummary` = aggregated `OutboundOrder.Status` across `OutboundOrder.WebOrderNo == Order.WebOrderNo AND OutboundType=Shipping AND Status != Cancelled`. Format: `"2 Outbounds (1 Confirmed, 1 Allocated)"`. If none: `"No Outbound"`.
- Sort: default `CustomerDeliveryDate ASC` (most-overdue-first). Support sort by `deliveryDate`, `orderDate`, `remainingQty`.
- Pagination: standard skip/take

```csharp
public class UnshippedOrderService : IUnshippedOrderService
{
    private readonly CP6Context _db;
    public UnshippedOrderService(CP6Context db) { _db = db; }

    public async Task<PagedResultDto<UnshippedOrderItemDto>> SearchAsync(UnshippedOrderQuery query)
    {
        // 1. Base query on Orders with filter
        // 2. Apply CustomerCd / OnlyOverdue filters
        // 3. Count total
        // 4. Apply sort + paging
        // 5. Materialize Order list
        // 6. For each Order, compute aggregates with separate queries (clearer than complex LINQ join)
        //    — or, group via single query if you prefer; either works
        // 7. Return PagedResultDto with rows + total
    }

    private static string SummarizeWorkOrders(List<WorkOrder> wos) { /* ... */ }
    private static string SummarizeOutbounds(List<OutboundOrder> obs) { /* ... */ }
}
```

For `WorkOrderStatus` integer → label, use Japanese-only labels inline (Phase 7+ would i18n this):
- 0=下書き / 1=確定済 / 2=発行済 / 3=着手中 / 4=完了 / 6=検査済

For `OutboundOrderStatus`:
- 0=下書き / 1=確定済 / 2=引当済 / 3=ピッキング / 4=完了

### 3. Controller

```csharp
[ApiController]
[Route("api/orders/unshipped")]
[Authorize]
public class UnshippedOrderController : ControllerBase
{
    private readonly IUnshippedOrderService _service;
    public UnshippedOrderController(IUnshippedOrderService service) { _service = service; }

    /// <summary>POST /api/orders/unshipped/search</summary>
    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] UnshippedOrderQuery query)
    {
        if (query.PageSize <= 0 || query.PageSize > 200) query.PageSize = 50;
        if (query.Page <= 0) query.Page = 1;
        var paged = await _service.SearchAsync(query);
        return Ok(new { code = 0, message = "OK", data = paged });
    }
}
```

## Tests

Use `xUnit + Moq + EF Core InMemory + ConfigureWarnings(...)`. Reference pattern: `CP6.Tests/OrderCancelBridgeHookTests.cs` or `CP6.Tests/OutboundServiceTests.cs`.

### UnshippedOrderServiceTests (≥6 tests)

1. `Search_FullyShipped_IsExcluded` — seed Order with ShipStatus=9, OrderStatus=SHIPPED → not returned
2. `Search_Cancelled_IsExcluded` — seed Order with OrderStatus=CANCELLED → not returned
3. `Search_PartiallyCancelled_IsIncluded` — seed Order with OrderStatus=PARTIALLY_CANCELLED, ShipStatus=0 → returned
4. `Search_FilterByCustomer_OnlyReturnsThatCustomer` — seed 3 orders different customers, filter CustomerCd → 1 returned
5. `Search_OnlyOverdue_FiltersByCustomerDeliveryDate` — seed 2 orders, one overdue one future. OnlyOverdue=true → only overdue
6. `Search_AggregatesMesAndWmsStatus` — seed Order + 2 WOs (Issued, InProgress) + 1 Outbound (Allocated). Assert `MesStatusSummary` contains "Issued" and "InProgress", `WmsStatusSummary` contains "Allocated"
7. `Search_RemainingQty_IsCalculated` — seed OrderDetail Quantity=100 ShippedQty=30 → RemainingQty=70, ShippedQty=30
8. `Search_NoBusinessPartner_FallsBackToCustomerCd` — Order with CustomerCd "C001" but no BP record → CustomerName == "C001" (or null is also acceptable; document choice)
9. `Search_Pagination_RespectsPageAndPageSize` — seed 25 orders, query PageSize=10 Page=2 → rows.Count=10, total=25

## Acceptance criteria

```bash
cd D:\CP6
taskkill /F /IM dotnet.exe 2>/dev/null || true
dotnet build --nologo -v quiet
# 0 errors

dotnet test --no-build --nologo -v quiet
# Total ≥ 250 (240 baseline + 10 from Phase 7 if already ran + 9 from this task)
# If existing tests fail → regression — STOP and report.
```

## Style rules

- C# 12 / .NET 8
- Match the `Controllers/OrderController.cs` response shape `{ code, message, data }`
- Japanese-language XML doc comments OK
- Async methods end in `Async`
- Use `System.Text.Json` for JSON

## Report when done

1. Files created (paths)
2. Files modified (paths + which lines)
3. Test counts: per file + total new
4. `dotnet build` result
5. `dotnet test` result (passed / failed / total)
6. Deviations from this spec and why
