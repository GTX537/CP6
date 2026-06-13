# Task: CP6 Phase 6 Step 7 — End-to-End Integration Tests

## Mission

Implement **2 end-to-end integration tests** for Phase 6 cancel cascade and IntegrationEvent retry/dead-letter flows. Work in `D:\CP6`. **Must not break the existing 236 passing tests.**

## Critical context (read before writing)

The codebase already has Phase 6 fully implemented:

### Implementation summary (already in code — call into it, don't re-create)

- **Entities** (Phase 6 schema): `Order.OrderStatus / CancelledAt / CancelReason` + `OrderLifecycleStatus` constants (`CONFIRMED / IN_PRODUCTION / SHIPPED / CANCELLED / PARTIALLY_CANCELLED`). New `IntegrationEvent` entity. `Sys_OperLog.IsAlert` flag. `WorkOrderStatus` constants class with `Cancelled = 9` (uses existing value, NOT 7).
- **Services**:
  - `IOrderService.CancelAsync(string webOrderNo, string reason, bool force, string? userName) → Task<OrderCancelResult>`
  - `IWorkOrderService.CancelAsync(string workOrderNo, string reason, string? userName) → Task<bool>`
  - `IOrderCancelBridgeHook.OnOrderCancelledAsync(...)` — probes + cascades, returns `OrderCancelHookResult`
  - `IIntegrationEventDispatcher.DispatchAsync(IntegrationEvent evt, CancellationToken ct)` — reflection-style routing
  - `IDeadLetterNotifier.NotifyAsync(IntegrationEvent evt, CancellationToken ct)` — SignalR + Sys_OperLog
- **Worker**: `IntegrationEventRetryWorker : BackgroundService` with `public async Task ProcessOnceAsync(CancellationToken ct)` method (codex made this public specifically so tests can drive it deterministically)
- **Options**: `IntegrationEventOptions { Enabled, MaxAttempts (default 5), BackoffSeconds (default [60,120,240,480,960]), PollIntervalSeconds, int GetBackoffSeconds(int attempts) }`

### Order/Cancel flow (so you can simulate it in test 1)

```
1. Create Order via OrderService.CreateAsync(orderDto, user)
   - This triggers MesBridgeHook.OnOrderCreatedAsync (if MesBridge:Enabled=true)
     → calls WorkOrderService.ExpandFromOrderAsync → creates 1+ WorkOrder
   - This triggers WmsBridgeHook.OnOrderCreatedAsync
     → calls OutboundService.CreateFromOrderAsync → creates Outbound (shipping)
2. For each WO: WorkOrderService.IssueAsync(no, user)
   - Triggers WmsBridgeHook.OnWorkOrderIssuedAsync
     → calls OutboundService.CreateFromWorkOrderAsync → creates Outbound (material)
3. Cancel: OrderService.CancelAsync(webOrderNo, reason, force=true, user)
   - Internally calls IOrderCancelBridgeHook.OnOrderCancelledAsync
     → cancels each related Outbound first (via OutboundService.CancelOrderAsync which releases RSV)
     → cancels each related WO (via WorkOrderService.CancelAsync which sets Status=9)
```

### Test pattern reference

The closest existing E2E pattern is `D:\CP6\CP6.Tests\WmsErpClosedLoopTests.cs`. Open it and use it as the model — same DI wiring, same InMemoryDatabase + `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`.

Also read `D:\CP6\CP6.Tests\BridgeHookPersistenceTests.cs` for IntegrationEvent persistence assertions.

### Files you will create

| File | Purpose |
|---|---|
| `CP6.Tests/OrderCancelFullCascadeE2ETests.cs` | Test 1 |
| `CP6.Tests/IntegrationEventRetryDeadLetterE2ETests.cs` | Test 2 |

## Test 1: OrderCancelFullCascadeE2ETests

This test wires up **real** services (no mocks for the Phase 6 path) and proves end-to-end cancel cascade.

```
Setup:
- Real DbContext (InMemoryDatabase)
- Real WmsBridgeHook (with real OutboundService + InboundService)
- Real MesBridgeHook (with real WorkOrderService that uses the WmsBridgeHook)
- Real OrderCancelBridgeHook (with WorkOrderService + OutboundService)
- Real OrderService (with all the above bridges)
- Seed: ProductMaster + ProductProcess + ProductMaterial + BusinessPartner + Stock (positive qty for materials)

Scenario:
  1. Create Order via OrderService.CreateAsync(...)
  2. Issue all WOs via WorkOrderService.IssueAsync(...)
  3. Cancel via OrderService.CancelAsync(webOrderNo, "Customer changed mind", force=true, "tester")

Assertions:
  A. Outcome == OrderCancelResult.Cancelled (or PartiallyCancelled if some material reservation fails — pick the simpler clean path)
  B. Reload Order from DB → OrderStatus == OrderLifecycleStatus.Cancelled
  C. All related WorkOrders Status == WorkOrderStatus.Cancelled (9)
  D. All related OutboundOrders Status == OutboundOrderStatus.Cancelled (9)
  E. Material reservations released: for each Stock row touched, AllocatedQty == 0 (back to baseline)
  F. T_IntegrationEvent has AT LEAST 4 rows: 1 for MesBridge OnOrderCreated, 1 for WmsBridge OnOrderCreated, 1 for WmsBridge OnWorkOrderIssued, and possibly more for the cancel cascade path
  G. Order.CancelReason == "Customer changed mind"
  H. Order.CancelledAt is within ±10 seconds of now
```

**Key risk**: this is a multi-service e2e test. Use the existing `WmsErpClosedLoopTests.cs` setup helper functions as your template. Their seeded data shape should work directly.

**Pragmatic shortcut**: If the cascade requires `MesBridge:Enabled=true` AND seeding ProductProcess/ProductMaterial gets messy, you can simulate the cascade by manually inserting WO + Outbound rows before calling Cancel. That's still a valid e2e for the cancel path. Note this deviation in your "Deviations" report if you take this shortcut.

## Test 2: IntegrationEventRetryDeadLetterE2ETests

Drive the worker manually (no Task.Delay needed) to prove retry → dead-letter transition.

```
Setup:
- Real DbContext (InMemoryDatabase)
- Real IntegrationEventOptions { MaxAttempts = 3, BackoffSeconds = [1, 2, 3], PollIntervalSeconds = 60, Enabled = true }
- Mock IIntegrationEventDispatcher that ALWAYS throws InvalidOperationException("network down")
- Real DeadLetterNotifier (writes to Sys_OperLogs; SignalR push is best-effort, no need to mock IHubContext — DeadLetterNotifier swallows reflection failure cleanly)
- Real IntegrationEventRetryWorker (use scope factory that returns scope giving the above services)

Scenario:
  1. Seed 1 IntegrationEvent: Status=FAILED, Attempts=0, NextRetryAt=now-1s, HookName="OnOrderCreatedAsync", SourceModule="ERP", TargetModule="MES", SourceNo="WEB-RETRY-001", PayloadJson="{\"webOrderNo\":\"WEB-RETRY-001\",\"userName\":\"u\"}"
  2. Loop 4 times: rewind NextRetryAt to now-1s, call worker.ProcessOnceAsync(CancellationToken.None)
  3. Reload event from DB
  4. Assert:
     - Attempts == 3 (= MaxAttempts; after that worker skips because Attempts >= MaxAttempts)
     - Status == IntegrationEventStatus.DeadLetter
     - NextRetryAt == null
     - LastError contains "network down"
  5. Verify Sys_OperLogs has exactly 1 new row where IsAlert=true AND HttpMethod="BACKGROUND" AND Action="OnOrderCreatedAsync"

Edge case to also cover:
  - Second test method: same setup but dispatcher succeeds on 2nd retry. Assert Status transitions Failed → Success after 2nd ProcessOnceAsync, NextRetryAt=null, Sys_OperLogs IsAlert row NOT created.
```

**Test helper**: write a small `IServiceScopeFactory` wrapper that returns a single fixed scope (no real DI container needed; pass the db + dispatcher + notifier directly). Or use `Microsoft.Extensions.DependencyInjection.ServiceCollection` to build a real provider — your call.

**ProcessOnceAsync is `public`**: codex made it public in the worker specifically so tests can drive it. Don't make it internal.

## Acceptance criteria — verify BEFORE you stop

```bash
cd D:\CP6

# Stop any running dotnet process to release bin locks
taskkill /F /IM dotnet.exe 2>/dev/null || true

# Build — must be 0 errors, only pre-existing warnings
dotnet build --nologo -v quiet

# Test — total should be ≥ 240 (was 236 + 4 new tests)
dotnet test --no-build --nologo -v quiet
```

If any of the existing 236 tests start failing, you broke a regression. STOP and report which test names and what changed.

## Style rules

- C# 12 / .NET 8
- xUnit + Moq + EF Core InMemory (existing pattern, do NOT introduce new test framework)
- Match brace/comment style with `BridgeHookPersistenceTests.cs`
- All test method names use snake-case-ish dotted convention as in existing files (e.g., `OrderCancel_FullCascade_Cancelled_AllStatusesUpdated`)
- Japanese/Chinese inline comments OK where the surrounding context uses them

## Report format

Output a short summary:
1. Files created (paths)
2. Test counts (per file + total new)
3. `dotnet build` result
4. `dotnet test` result (passed / failed / total)
5. Any deviations from this spec and why (e.g., shortcut for manual WO/Outbound seeding instead of full bridge cascade chain)
