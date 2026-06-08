# Task: CP6 Phase 6 Step 6 — IntegrationEvent Retry Worker + Dispatcher + DeadLetterNotifier

## Mission (read carefully)

You are implementing **Step 6 of Phase 6** in the CP6 ERP/MES/WMS .NET 8/10 codebase at `D:\CP6`. Steps 1-5 are complete; you build on top of them. You must NOT break the existing 225 passing tests.

End state: a background worker that retries `Failed` IntegrationEvent rows with exponential backoff and writes alerts when retries exhaust.

## Critical context (must use)

### Existing entities (already in code — do not redefine)

```csharp
// CP6.Entity/DomainModels/IntegrationEvent.cs (already exists)
public class IntegrationEvent : BaseBizEntity {
    public string SourceModule { get; set; }  // ERP / MES / WMS
    public string TargetModule { get; set; }  // ERP / MES / WMS
    public string HookName { get; set; }      // e.g. "OnWorkOrderIssuedAsync"
    public string SourceNo { get; set; }
    public string? TargetNo { get; set; }
    public string Status { get; set; }        // see IntegrationEventStatus
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public Guid CorrelationId { get; set; }
    public string PayloadJson { get; set; }
}

public static class IntegrationEventStatus {
    public const string Pending = "PENDING";
    public const string Success = "SUCCESS";
    public const string Skipped = "SKIPPED";
    public const string Failed = "FAILED";
    public const string DeadLetter = "DEAD";
    public const string Compensated = "COMPENSATED";
}
```

### Existing hooks that Dispatcher routes to (signatures are FIXED — do not modify)

```csharp
// CP6.Core/Services/IMesBridgeHook.cs
Task<MesBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName);
// CP6.Core/Services/IWmsBridgeHook.cs
Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string workOrderNo, string? userName);
Task<WmsBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName);
Task<WmsBridgeResult> OnProductionCompletedAsync(string workOrderNo, decimal goodQty, string? userName);
// CP6.Core/Services/IErpBridgeHook.cs
Task<ErpBridgeResult> OnShipmentConfirmedAsync(string outboundNo, string? userName);
```

### Payload shapes already written by existing hooks (Dispatcher must deserialize these)

Each `IntegrationEvent.PayloadJson` was serialized from anonymous types in the existing hooks. Use these field names:

- `OnWorkOrderIssuedAsync` → `{ "workOrderNo": "...", "userName": "..." }`
- `OnOrderCreatedAsync` (WMS) → `{ "webOrderNo": "...", "userName": "..." }`
- `OnOrderCreatedAsync` (MES) → `{ "webOrderNo": "...", "userName": "..." }`
- `OnProductionCompletedAsync` → `{ "workOrderNo": "...", "goodQty": 12.34, "userName": "..." }`
- `OnShipmentConfirmedAsync` → `{ "outboundNo": "...", "userName": "..." }`

### Routing key

The Dispatcher routes on the tuple `(SourceModule, TargetModule, HookName)` because some HookName values collide across hooks. Define a static `RouteKey(string source, string target, string hook)` helper.

### Existing SignalR Hub for DeadLetter notification

Use `IHubContext<WmsHub>` (already registered at `/hubs/wms`, see `CP6.WebApi/Hubs/WmsHub.cs`).

### Existing OperLog entity (for alert audit trail)

```csharp
// CP6.Entity/DomainModels/Sys_OperLog.cs (already exists)
public class Sys_OperLog {
    public int Id { get; set; }              // auto increment
    public string? UserName { get; set; }
    public string? HttpMethod { get; set; }
    public string? RequestUrl { get; set; }
    public string? Controller { get; set; }
    public string? Action { get; set; }
    public string? RequestBody { get; set; }
    public int StatusCode { get; set; }
    public long ElapsedMs { get; set; }
    public string? ClientIp { get; set; }
    public DateTime CreateDate { get; set; }
    public bool IsAlert { get; set; }         // Phase 6 added
}
```

## Files to create

| File | Purpose |
|---|---|
| `CP6.Core/Options/IntegrationEventOptions.cs` | appsettings binding |
| `CP6.Core/Services/IIntegrationEventDispatcher.cs` | Interface |
| `CP6.Core/Services/IntegrationEventDispatcher.cs` | Implementation (reflection-style routing) |
| `CP6.Core/Services/IDeadLetterNotifier.cs` | Interface |
| `CP6.Core/Services/DeadLetterNotifier.cs` | Implementation (SignalR + OperLog) |
| `CP6.WebApi/BackgroundServices/IntegrationEventRetryWorker.cs` | BackgroundService |
| `CP6.Tests/IntegrationEventDispatcherTests.cs` | Unit tests |
| `CP6.Tests/IntegrationEventRetryWorkerTests.cs` | Unit tests |
| `CP6.Tests/DeadLetterNotifierTests.cs` | Unit tests |

**Do NOT** modify existing files except to add minimal `using` directives if absolutely necessary. **Do NOT** modify `Program.cs` (S9 handles DI registration).

## Detailed requirements

### 1. IntegrationEventOptions

```csharp
namespace CP6.Core.Options;
public class IntegrationEventOptions {
    public int MaxAttempts { get; set; } = 5;
    /// <summary>Backoff in seconds for attempt N (zero-indexed). Default: [60, 120, 240, 480, 960]</summary>
    public int[] BackoffSeconds { get; set; } = new[] { 60, 120, 240, 480, 960 };
    public int PollIntervalSeconds { get; set; } = 60;
    /// <summary>If false, the worker is a no-op (still constructed but does nothing each tick)</summary>
    public bool Enabled { get; set; } = true;

    public int GetBackoffSeconds(int attempts) {
        if (attempts <= 0) return BackoffSeconds[0];
        var idx = Math.Min(attempts - 1, BackoffSeconds.Length - 1);
        return BackoffSeconds[idx];
    }
}
```

### 2. IIntegrationEventDispatcher

```csharp
public interface IIntegrationEventDispatcher {
    /// <summary>
    /// Re-invokes the original hook based on (SourceModule, TargetModule, HookName).
    /// Throws if the route is unknown. Returns the hook's success status.
    /// </summary>
    Task<bool> DispatchAsync(IntegrationEvent evt, CancellationToken ct = default);
}
```

Implementation: inject all 4 hook interfaces (`IMesBridgeHook`, `IWmsBridgeHook`, `IErpBridgeHook`, `IOrderCancelBridgeHook`). Build a static dispatch table:

```csharp
private static readonly Dictionary<string, Func<DispatchContext, Task<bool>>> Routes = new() {
    [Key("ERP", "MES", "OnOrderCreatedAsync")] = async ctx => {
        var p = ctx.GetPayload<OnOrderCreatedPayload>();
        var r = await ctx.Mes.OnOrderCreatedAsync(p.WebOrderNo, p.UserName);
        return r.Success;
    },
    [Key("MES", "WMS", "OnWorkOrderIssuedAsync")] = async ctx => {
        var p = ctx.GetPayload<OnWorkOrderIssuedPayload>();
        var r = await ctx.Wms.OnWorkOrderIssuedAsync(p.WorkOrderNo, p.UserName);
        return r.Success;
    },
    [Key("ERP", "WMS", "OnOrderCreatedAsync")] = async ctx => {
        var p = ctx.GetPayload<OnOrderCreatedPayload>();
        var r = await ctx.Wms.OnOrderCreatedAsync(p.WebOrderNo, p.UserName);
        return r.Success;
    },
    [Key("MES", "WMS", "OnProductionCompletedAsync")] = async ctx => {
        var p = ctx.GetPayload<OnProductionCompletedPayload>();
        var r = await ctx.Wms.OnProductionCompletedAsync(p.WorkOrderNo, p.GoodQty, p.UserName);
        return r.Success;
    },
    [Key("WMS", "ERP", "OnShipmentConfirmedAsync")] = async ctx => {
        var p = ctx.GetPayload<OnShipmentConfirmedPayload>();
        var r = await ctx.Erp.OnShipmentConfirmedAsync(p.OutboundNo, p.UserName);
        return r.Success;
    },
    // OrderCancelBridgeHook NOT included — its events are out of retry scope (sync cascade only)
};
```

Define `DispatchContext` with the 4 hooks + a `JsonElement Payload` and `GetPayload<T>()` helper using `System.Text.Json`.

If a route is not found, throw `InvalidOperationException` with message `"DISPATCH-404: unknown route {Source}→{Target}.{Hook}"`.

### 3. IDeadLetterNotifier + DeadLetterNotifier

```csharp
public interface IDeadLetterNotifier {
    Task NotifyAsync(IntegrationEvent evt, CancellationToken ct = default);
}
```

Implementation responsibilities:
1. Push a SignalR event `"IntegrationDeadLetter"` to all clients via `IHubContext<WmsHub>`. Payload: `{ EventId, HookName, SourceNo, Attempts, LastError, OccurredAt }`.
2. Insert one `Sys_OperLog` row:
   - `HttpMethod = "BACKGROUND"`
   - `RequestUrl = $"/integration-event/{evt.Id}"`
   - `Controller = "IntegrationEvent"`
   - `Action = evt.HookName`
   - `RequestBody = $"hook={evt.HookName} source={evt.SourceNo} attempts={evt.Attempts}; lastError={Truncate(evt.LastError, 4000)}"`
   - `StatusCode = 500`
   - `IsAlert = true`
   - `CreateDate = DateTime.Now`
   - `UserName = "system"`

Failures in SignalR push must NOT prevent OperLog write. Wrap each in its own try/catch + log via `ILogger`.

### 4. IntegrationEventRetryWorker (BackgroundService)

```csharp
public class IntegrationEventRetryWorker : BackgroundService {
    // Inject: IServiceScopeFactory, IOptions<IntegrationEventOptions>, ILogger<IntegrationEventRetryWorker>
    protected override async Task ExecuteAsync(CancellationToken ct) {
        // Loop:
        //   if (!_opts.Enabled) { delay; continue; }
        //   using var scope = _scopeFactory.CreateScope();
        //   var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
        //   var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
        //   var notifier = scope.ServiceProvider.GetRequiredService<IDeadLetterNotifier>();
        //
        //   var now = DateTime.UtcNow;
        //   var due = await db.IntegrationEvents
        //       .Where(e => e.Status == IntegrationEventStatus.Failed
        //                && e.NextRetryAt != null && e.NextRetryAt <= now
        //                && e.Attempts < _opts.MaxAttempts)
        //       .OrderBy(e => e.NextRetryAt)
        //       .Take(50)
        //       .ToListAsync(ct);
        //
        //   foreach (var evt in due) {
        //       evt.Attempts++;
        //       try {
        //           var ok = await dispatcher.DispatchAsync(evt, ct);
        //           evt.Status = ok ? IntegrationEventStatus.Success : IntegrationEventStatus.Failed;
        //           if (!ok) {
        //               evt.NextRetryAt = now.AddSeconds(_opts.GetBackoffSeconds(evt.Attempts));
        //           } else {
        //               evt.NextRetryAt = null;
        //           }
        //       } catch (Exception ex) {
        //           evt.LastError = ex.ToString();
        //           evt.NextRetryAt = now.AddSeconds(_opts.GetBackoffSeconds(evt.Attempts));
        //           evt.Status = IntegrationEventStatus.Failed;
        //       }
        //
        //       if (evt.Attempts >= _opts.MaxAttempts && evt.Status == IntegrationEventStatus.Failed) {
        //           evt.Status = IntegrationEventStatus.DeadLetter;
        //           evt.NextRetryAt = null;
        //           await notifier.NotifyAsync(evt, ct);
        //       }
        //   }
        //   await db.SaveChangesAsync(ct);
        //   await Task.Delay(TimeSpan.FromSeconds(_opts.PollIntervalSeconds), ct);
    }
}
```

Catch `OperationCanceledException` cleanly when ct fires. Log info on start/stop.

## Testing

Use the existing test pattern: xUnit + Moq + EF Core InMemory with `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`.

Reference `CP6.Tests/BridgeHookPersistenceTests.cs` for the NewDb() helper pattern.

### IntegrationEventDispatcherTests (≥4 tests)

1. `Dispatch_MesBridgeRoute_CallsMesHook` — route ERP→MES OnOrderCreatedAsync, verify mock IMesBridgeHook called with deserialized payload
2. `Dispatch_WmsProductionCompletedRoute_PassesGoodQty` — route MES→WMS OnProductionCompletedAsync, verify decimal goodQty correctly deserialized
3. `Dispatch_UnknownRoute_Throws` — route ERP→ERP UnknownHook → InvalidOperationException with "DISPATCH-404"
4. `Dispatch_HookReturnsFailedResult_ReturnsFalse` — mock hook returns `WmsBridgeResult.Failed("x")`, verify dispatcher returns false

### IntegrationEventRetryWorkerTests (≥4 tests)

1. `Worker_RetriesDueEvents_AndMarksSuccess` — seed 1 Failed event with NextRetryAt past, dispatcher mocked to succeed → event Status=Success after one tick
2. `Worker_AfterMaxAttempts_TransitionsToDeadLetter` — seed event with Attempts=4 (next attempt = 5 = max), dispatcher fails → Status=DeadLetter + notifier called once
3. `Worker_DisabledViaOptions_DoesNothing` — Options.Enabled=false → no DB queries, no dispatcher calls
4. `Worker_SkipsEventsNotYetDue` — seed event with NextRetryAt in future → not picked up
5. `Worker_BackoffIncreasesAttemptByAttempt` — Options.BackoffSeconds = [10, 20] → Attempts=1 fail → NextRetryAt ≈ now+10s; second tick after manual NextRetryAt rewind, Attempts=2 fail → NextRetryAt ≈ now+20s

### DeadLetterNotifierTests (≥2 tests)

1. `Notify_WritesOperLogWithIsAlertTrue` — verify Sys_OperLogs row inserted with IsAlert=true, HttpMethod="BACKGROUND", StatusCode=500
2. `Notify_PushesSignalRMessage` — verify mock `IHubContext<WmsHub>` SendCoreAsync called with method name "IntegrationDeadLetter"

For SignalR mocking: `Mock<IHubContext<WmsHub>>` + `Mock<IHubClients>` + `Mock<IClientProxy>` chain. SendCoreAsync verification pattern: `mockClientProxy.Verify(c => c.SendCoreAsync("IntegrationDeadLetter", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once)`.

## Acceptance criteria — verify BEFORE you stop

Run from `D:\CP6`:

```bash
dotnet build --nologo -v quiet
# Expected: 0 errors, 1 known pre-existing warning in InboundService.cs

dotnet test --no-build --nologo -v quiet
# Expected: ALL 225 + your new tests pass. Total should be ≥ 235.
# If any of the existing 225 fail, you broke a regression — STOP and report.
```

## Voice rules

- Write production-quality C# with XML doc comments matching existing style (Japanese/Chinese comments OK where idiomatic)
- Match the existing brace style (Allman braces, one statement per line)
- Use `System.Text.Json` for JSON, not Newtonsoft
- All async methods end in `Async`
- No `async void`, always `Task` or `Task<T>`

## Report when done

Output a short summary:
1. Files created (paths)
2. Test counts (Dispatcher / Worker / Notifier / total new)
3. `dotnet build` result (errors + warnings)
4. `dotnet test` result (passed / failed / total)
5. Any decisions you made that deviated from this spec (and why)
