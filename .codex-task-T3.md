# Task: CP6 Phase 8 - CSV Export for Unshipped Orders

## Mission

Add CSV export to the Phase 8 unshipped orders dashboard endpoint. Working dir: `D:\CP6`. Must not break existing tests.

## Files to create

| File | Purpose |
|---|---|
| `CP6.Tests/UnshippedOrderCsvExportTests.cs` | Tests for CSV format. |

## Files to modify

| File | Change |
|---|---|
| `CP6.Core/Services/IUnshippedOrderService.cs` | Add `Task<byte[]> ExportCsvAsync(UnshippedOrderQuery query)`. |
| `CP6.Core/Services/UnshippedOrderService.cs` | Implement export with UTF-8 BOM, CRLF, RFC 4180 quoting, no paging, capped at 5000 rows. |
| `CP6.WebApi/Controllers/UnshippedOrderController.cs` | Add `POST /api/orders/unshipped/export-csv`. |

## Acceptance criteria

```bash
cd D:\CP6
dotnet build CP6.Tests/CP6.Tests.csproj --nologo -v quiet
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo -v quiet
```
