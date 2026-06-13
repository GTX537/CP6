# Task: CP6 Phase 7 + Phase 8 - End-to-End Integration Tests

## Mission

Add 2 end-to-end integration tests that exercise Phase 7 (QC block shipping) and Phase 8 (Unshipped Orders dashboard) using real services and EF InMemory. Working dir: `D:\CP6`. Must not break the existing 259 passing tests.

## Files to create

| File | Purpose |
|---|---|
| `CP6.Tests/StockQc_AllocateE2ETests.cs` | Phase 7 e2e: 1 PASSED + 1 FAILED + 1 PENDING stock; `AllocateAsync` demand=50 asserts PASSED row gets the RSV, FAILED is skipped, and `T_StockTransaction` has correct entries. |
| `CP6.Tests/UnshippedOrder_FullCascadeE2ETests.cs` | Phase 8 e2e: real `OrderService.CreateAsync` -> `MesBridge` -> `WorkOrderService.IssueAsync` -> assert `UnshippedOrderService.SearchAsync` returns 1 row with `MesStatusSummary` mentioning `Issued` and `WmsStatusSummary` mentioning the outbound status. |

## Reference patterns

- `CP6.Tests/OrderCancelFullCascadeE2ETests.cs`
- `CP6.Tests/WmsErpClosedLoopTests.cs`
- `CP6.Tests/BridgeHookPersistenceTests.cs`

## Acceptance criteria

```bash
cd D:\CP6
dotnet build CP6.Tests/CP6.Tests.csproj --nologo -v quiet
dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo -v quiet
```

## Report when done

1. Files created
2. Test counts (2 new total)
3. Build result
4. Test result
5. Any deviations
