# 06 · 跨模块联动：Bridge Hook 模式

## 📍 学习目标

1. ERP / MES / WMS 三个子系统怎么"既独立又联动"？
2. CP6 的 Bridge Hook 是什么模式？跟 MediatR / Event Bus 比有什么差别？
3. "Best-effort + 幂等 + 可禁用" 各自解决什么问题？
4. `T_IntegrationEvent` 持久化 + 自动重试 + 死信告警怎么实现？
5. 为什么不直接用 RabbitMQ 解耦三个子系统？

---

## 🔎 真实代码切片

### 四个 Bridge Hook 接口

```csharp
// CP6.Core/Services/IMesBridgeHook.cs
public interface IMesBridgeHook
{
    Task OnOrderCreatedAsync(string webOrderNo, string user);
}

// CP6.Core/Services/IWmsBridgeHook.cs
public interface IWmsBridgeHook
{
    Task OnOrderCreatedAsync(string webOrderNo, string user);
    Task OnWorkOrderIssuedAsync(string workOrderNo, string user);
    Task OnProductionCompletedAsync(string workOrderNo, decimal goodQty, string user);
}

// CP6.Core/Services/IErpBridgeHook.cs
public interface IErpBridgeHook
{
    Task OnShipmentConfirmedAsync(string outboundNo, string user);
    Task OnReturnConfirmedAsync(string rmaNo, string user);
}

// CP6.Core/Services/IOrderCancelBridgeHook.cs
public interface IOrderCancelBridgeHook
{
    Task<CancelPlan> ProbeAsync(string webOrderNo);
    Task ExecuteAsync(string webOrderNo, string reason, string user);
}
```

### 调用方：`OrderService.CreateAsync` 末尾的 best-effort hook

```csharp
public async Task<Order> CreateAsync(OrderCreateDto dto, string user)
{
    // ... 主业务：创建受注、明细
    var order = new Order { /* ... */ };
    _context.Orders.Add(order);
    await _context.SaveChangesAsync();

    // best-effort 触发跨模块联动 —— 失败不影响受注创建
    try
    {
        await _mesBridge.OnOrderCreatedAsync(order.WebOrderNo, user);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "MesBridge.OnOrderCreated failed for {No}, ignoring", order.WebOrderNo);
    }

    try
    {
        await _wmsBridge.OnOrderCreatedAsync(order.WebOrderNo, user);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "WmsBridge.OnOrderCreated failed for {No}, ignoring", order.WebOrderNo);
    }

    return order;
}
```

### `BridgeHookBase` —— 所有 Hook 共享的持久化基类

```csharp
// CP6.Core/Services/BridgeHookBase.cs (Phase 6 加入)
public abstract class BridgeHookBase
{
    protected readonly CP6Context _context;
    protected readonly IDeadLetterNotifier _deadLetter;

    protected async Task PersistEventAsync(
        string sourceModule, string targetModule, string hookName,
        string correlationId, string payloadJson,
        IntegrationEventStatus status,
        string? targetNo = null,
        Exception? error = null)
    {
        var evt = new IntegrationEvent
        {
            SourceModule = sourceModule,    // "ERP"
            TargetModule = targetModule,    // "MES"
            HookName = hookName,            // "OnOrderCreated"
            CorrelationId = correlationId,  // webOrderNo
            PayloadJson = payloadJson,
            Status = status,                // SUCCESS / SKIPPED / FAILED
            TargetNo = targetNo,
            ErrorMessage = error?.Message,
            Attempts = 1,
            NextRetryAt = status == IntegrationEventStatus.Failed
                ? DateTime.UtcNow.AddSeconds(60) : null
        };
        _context.IntegrationEvents.Add(evt);
        await _context.SaveChangesAsync();
    }
}

// 具体实现：MesBridgeHook 继承
public class MesBridgeHook : BridgeHookBase, IMesBridgeHook
{
    public async Task OnOrderCreatedAsync(string webOrderNo, string user)
    {
        try
        {
            var wo = await _workOrderService.ExpandFromOrderAsync(webOrderNo, user);
            await PersistEventAsync("ERP", "MES", nameof(OnOrderCreatedAsync),
                webOrderNo, JsonSerializer.Serialize(new { webOrderNo, user }),
                IntegrationEventStatus.Success, targetNo: wo.WorkOrderNo);
        }
        catch (InvalidOperationException biz)  // 业务"重复展开"
        {
            await PersistEventAsync(..., IntegrationEventStatus.Skipped, error: biz);
        }
        catch (Exception ex)  // 意外失败
        {
            await PersistEventAsync(..., IntegrationEventStatus.Failed, error: ex);
            throw;  // 让上层日志记录
        }
    }
}
```

### `IntegrationEventRetryWorker` —— 60s 自动重试

```csharp
public class IntegrationEventRetryWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

                var pending = await db.IntegrationEvents
                    .Where(e => e.Status == IntegrationEventStatus.Failed
                             && e.NextRetryAt <= DateTime.UtcNow
                             && e.Attempts < _options.MaxAttempts)
                    .Take(50)
                    .ToListAsync(ct);

                foreach (var evt in pending)
                {
                    try
                    {
                        await dispatcher.DispatchAsync(evt);  // 反射路由回原 hook
                        evt.Status = IntegrationEventStatus.Success;
                    }
                    catch
                    {
                        evt.Attempts++;
                        evt.NextRetryAt = DateTime.UtcNow.AddSeconds(60 * (1 << evt.Attempts));  // 指数退避
                        if (evt.Attempts >= _options.MaxAttempts)
                        {
                            evt.Status = IntegrationEventStatus.Dead;
                            await _deadLetter.NotifyAsync(evt);
                        }
                    }
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IntegrationEventRetryWorker tick failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }
}
```

---

## 💡 资深视角

### Bridge Hook 是什么模式？

**它是接口隔离 + 事件通知的合体**。换个角度看：

- 从 ERP 角度：`IMesBridgeHook` 是"我**通知**别人发生了什么"的出口（OUT 端）
- 从 MES 角度：`MesBridgeHook` 实现 = "我**接受**通知，做相应动作"的入口（IN 端）

这本质上是**进程内的事件订阅**，只是用接口实现而不是 Pub/Sub 总线。

### Bridge Hook vs MediatR vs Event Bus

| 方案 | 解耦程度 | 复杂度 | CP6 选它的原因 |
|---|---|---|---|
| **直接 using `MesService`** | ❌ 无 | 极低 | ERP 必须知道 MES 类型 → 不行 |
| **Bridge Hook（接口）** | ✅ 强 | 低 | 编译时解耦，运行时同进程，无序列化开销 |
| **MediatR** | ✅ 强 | 中 | 多了一层 IRequestHandler 抽象，价值不大 |
| **进程内 Event Bus** | ✅ 强 | 中 | Mass Transit 等需要外置依赖 |
| **MQ（Kafka / RabbitMQ）** | ✅✅ 极强 | 高 | 跨进程需求才用，CP6 是单体进程 |

**CP6 是单体应用**（一个 .NET 进程跑所有模块），所以不需要 MQ 解耦。Bridge Hook 是最朴素也是性价比最高的选择：

- 编译时类型安全
- 调用栈完整可追溯（不像 MQ 那样断在 producer 处）
- 无序列化、无网络
- 测试时直接 mock 接口

**未来如果拆微服务**：把 Bridge Hook 接口实现换成 MQ 发布即可，调用方完全不用改。

### Best-effort 的含义

```csharp
try
{
    await _mesBridge.OnOrderCreatedAsync(...);
}
catch (Exception ex)
{
    _logger.LogWarning(...);   // 不抛
}
```

**含义**：

- 主业务（创建受注）必须成功
- 联动（自动展开指図）如果失败，**不阻塞**主业务
- 失败的联动写到 `T_IntegrationEvent` 等 worker 重试

**为什么不让联动失败也回滚**：

- 受注创建是用户操作，必须给用户一个确定结果
- MES 服务可能临时不可用（DB 网络抖动），不能因此拒收订单
- 联动是异步的"最终一致性"目标

**反例 —— 严格一致性**：

```csharp
using var tx = await _context.Database.BeginTransactionAsync();
_context.Orders.Add(order);
await _mesBridge.OnOrderCreatedAsync(...);  // MES 失败 → rollback Order
await tx.CommitAsync();
```

这种"分布式事务"在单体里能做，但代价是任何 MES 临时故障都让受注无法创建。生产经验证明 best-effort + 重试比严格事务更稳。

### 幂等的实现

**幂等**：同样输入调多次结果一样。Bridge Hook 必须幂等是因为：

- 自动重试可能多次调用同一 hook
- 用户也可能手动触发"补偿"

CP6 的幂等实现：

```csharp
public async Task ExpandFromOrderAsync(string webOrderNo, string user)
{
    // 检查是否已展开
    var existing = await _context.WorkOrders
        .AnyAsync(w => w.WebOrderNo == webOrderNo && !w.IsDeleted);
    if (existing)
    {
        throw new InvalidOperationException("WO already expanded for this order");  // 业务 SKIPPED
    }
    // ... 真正展开
}
```

调用方 `MesBridgeHook` 把 `InvalidOperationException` 捕获为 `Skipped` 状态，不视为失败 → 不重试。

### 可禁用（开关化）

```csharp
// appsettings.json
{
  "MesBridge": { "Enabled": false },
  "WmsBridge": { "Enabled": true },
  "ErpBridge": { "Enabled": true },
  "OrderCancelBridge": { "Enabled": true }
}

// Program.cs
if (mesBridgeEnabled)
    builder.Services.AddScoped<IMesBridgeHook, MesBridgeHook>();
else
    builder.Services.AddScoped<IMesBridgeHook, NoOpMesBridgeHook>();
```

`NoOpMesBridgeHook.OnOrderCreatedAsync` 直接返回 `Task.CompletedTask` —— **Null Object 模式**。

**作用**：

- 单模块演示（只放 ERP 屏给客户看，关掉 MES 联动）
- 生产事故隔离（MES 数据库挂了，临时关掉联动让 ERP 不被拖累）
- AB 测试新版本 hook

### T_IntegrationEvent 表的字段

```csharp
public class IntegrationEvent : BaseEntity
{
    public string SourceModule { get; set; }    // ERP / MES / WMS
    public string TargetModule { get; set; }
    public string HookName { get; set; }        // OnOrderCreatedAsync
    public string CorrelationId { get; set; }   // 业务关联（webOrderNo / workOrderNo）
    public string PayloadJson { get; set; }     // 调用时的入参
    public IntegrationEventStatus Status { get; set; }  // PENDING/SUCCESS/SKIPPED/FAILED/DEAD/COMPENSATED
    public string? TargetNo { get; set; }       // 成功时下游产生的单号
    public string? ErrorMessage { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextRetryAt { get; set; }
}
```

这张表的作用：

1. **审计**：每次跨模块联动都有记录
2. **重试基础**：Retry Worker 扫这张表
3. **死信观察**：Bridge Health Monitor 看 DLQ
4. **手动补偿**：运维可以"标记为已处理"或"重发"

### 死信告警的双通道

```csharp
public class DeadLetterNotifier : IDeadLetterNotifier
{
    public async Task NotifyAsync(IntegrationEvent evt)
    {
        // 通道 1：SignalR push 到 WmsHub
        await _hub.Clients.All.SendAsync("BridgeDeadLetter", evt);

        // 通道 2：写 Sys_OperLog 加 IsAlert=true，运维大屏看
        _context.Sys_OperLogs.Add(new Sys_OperLog
        {
            UserName = "system",
            HttpMethod = "BRIDGE",
            RequestUrl = $"{evt.SourceModule}->{evt.TargetModule}/{evt.HookName}",
            StatusCode = 999,
            IsAlert = true,
            RequestBody = JsonSerializer.Serialize(evt)
        });
        await _context.SaveChangesAsync();
    }
}
```

**双通道**是关键：

- SignalR 是即时但易丢（断网就漏）
- DB 是必达但需轮询

两者结合：值班大屏看 SignalR 即时弹窗，每天对账时查 DB 历史，万无一失。

---

## ⚠️ 踩坑记录

### 坑 1：Hook 内部又触发了另一个 Hook → 循环

```csharp
// WmsBridge.OnProductionCompletedAsync 内部调用入库
await _inboundService.CreateFinishedGoodsFromWorkOrderAsync(...);

// InboundService.CreateAsync 又触发了 SignalR + 一个事件
// 如果事件订阅里又回调 WMS Hook → 循环
```

**修复**：CP6 的策略是 Hook 只发"业务事件"通知，不发"系统事件"通知。完成品入库不会再触发 OnProductionCompleted（用业务字段 `SourceType=PRODUCTION` 区分）。

### 坑 2：Retry Worker 跟 Hook 同时跑 → 重复

```
T0  ERP 创建 Order → Hook OnOrderCreated → MES 创建 WorkOrder → 写 IntegrationEvent(SUCCESS)
T1  60s 后，Worker 误读了一条没标 SUCCESS 的旧 event → 再次调用 OnOrderCreated → MES 抛 SKIPPED（因为已展开）
T2  Worker 把 SKIPPED 记成 SUCCESS
```

这个**重复但无害**是 CP6 接受的：因为 hook 本身幂等。如果业务非幂等（如下游会发邮件），就要在 Worker 里加更严格的"乐观锁 + Take 后 Update Status=Processing"再做。

### 坑 3：Worker 启动时积压一堆 Failed

如果服务挂了 1 小时，`T_IntegrationEvent` 里可能积累 1000 条 Failed。Worker 一启动一次性扫 1000 条直接打爆 MES。

**修复**：CP6 的 Worker 用 `Take(50)`，每次只处理一批，自然限速。生产环境还可以加 Polly 的 BulkheadPolicy。

### 坑 4：CorrelationId 不唯一导致 Bridge Health 算错

`T_IntegrationEvent.CorrelationId` 是业务关联 ID（如 webOrderNo），但同一个 webOrderNo 可能触发多次 hook（一次成功 + 一次手动补偿）。

**修复**：Bridge Health 算 24h 成功率时按 `(CorrelationId, HookName, Attempts最大值)` 去重，避免重复计数。

### 坑 5：单测里把 Hook 拉进 Tests 工程导致循环引用

```
CP6.Tests → CP6.WebApi（要 mock SignalR 用的 IHubContext）
CP6.WebApi → CP6.Core（CP6.Core 里有 IDeadLetterNotifier 接口）
CP6.Core → CP6.Entity
```

正常。但如果你想在 CP6.Core 里直接 using SignalR 的 IHubContext → 循环。CP6 的做法：在 CP6.Core 定义 `IWmsNotifier` 抽象，WebApi 实现 `SignalRWmsNotifier`。**依赖反转**（DIP）。

---

## 🧪 自检题

1. **设计判断**：现在要给 ERP 加一个"取消订单"操作，需要联动 MES 取消工单 + WMS 取消出库单。怎么用 Bridge Hook 模式实现？  
   <details><summary>答案</summary>这就是 CP6 的 Phase 6 <code>IOrderCancelBridgeHook</code>。两段模式：(1) <code>ProbeAsync</code> 探查会影响哪些 WO/Outbound（force=false）；(2) <code>ExecuteAsync</code> 真正执行（force=true）。顺序是反向级联：先取消 Outbound 让 RSV 解锁 → 再取消 WO → 最后 Order 头。每步都经 BridgeHookBase 写 IntegrationEvent。半路状态（如 WO 已开始生产）走 NeedsDecision 让用户强制确认。</details>

2. **取舍题**：什么时候 Bridge Hook 不如换成 RabbitMQ？  
   <details><summary>答案</summary>(1) 跨进程时（拆微服务后）；(2) 需要审计 / 重放 / 跨语言时；(3) 联动方处理时间长（&gt; 几秒），同步调用会拖慢主业务；(4) 联动方可用性差，需要 MQ 缓冲。CP6 单体进程 + Best-effort + Retry Worker 已经覆盖 80% 场景，不需要 MQ。</details>

3. **故障演练**：MES 数据库挂了 30 分钟，期间创建了 100 条 ERP 订单，Bridge Hook 全部失败。30 分钟后 MES 恢复，会发生什么？  
   <details><summary>答案</summary>这 100 条都进了 <code>T_IntegrationEvent</code> 表，Status=Failed，NextRetryAt 散布在故障期间。Retry Worker 每 60s 跑一次，每次 Take(50)，所以约 2~3 次 tick 就把这 100 条全部成功展开。如果 MES 30 分钟后还没好，Attempts 累加到 MaxAttempts(5) 转 Dead，触发死信告警，运维介入。</details>

4. **方案对比**：有人提议把 Bridge Hook 改成 .NET 的 `IEventBus`（如 MediatR 的 INotificationHandler），你怎么权衡？  
   <details><summary>答案</summary>MediatR 提供发布订阅但失去**编译时唯一目标性**：CP6 的 <code>IMesBridgeHook</code> 永远只有一个实现（生产或 NoOp），调用栈清晰。MediatR 的 INotification 可以有 N 个 Handler，调试时不知道哪个 Handler 出问题。CP6 这种业务联动**不需要广播**（一个事件只对应一个下游动作），所以 Bridge Hook 更直观。MediatR 适合"同一事件多方关心"的场景（如审计日志、缓存失效、邮件通知都订阅 OrderCreated）。</details>

5. **质疑题**：为什么 CP6 的 Hook 不直接 `await` 返回新生成的下游单号给调用方？反正是同进程。  
   <details><summary>答案</summary>因为<b>事件式调用语义</b>不应该承诺返回值。Bridge Hook 是"通知发生了什么"，下游怎么响应是它的事。如果让 IMesBridgeHook.OnOrderCreatedAsync 返回 WorkOrderNo，调用方就开始依赖这个返回 → 解耦失败。需要 WorkOrderNo 的话查 <code>T_IntegrationEvent.TargetNo</code> 或反查 <code>WorkOrder.WebOrderNo</code>。这个细节是真正的"解耦设计"和"接口好看但实际耦合"的区别。</details>

---

## 🔗 延伸阅读

- [Integration Events (Microsoft eShopOnContainers)](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/multi-container-microservice-net-applications/integration-event-based-microservice-communications)
- [Outbox Pattern](https://microservices.io/patterns/data/transactional-outbox.html) — CP6 的 IntegrationEvent + Worker 就是 Outbox 的简化版
- [Saga Pattern](https://microservices.io/patterns/data/saga.html) — 进一步的跨服务事务模式
- 项目内：`docs/PROJECT_STRUCTURE.md` §2.3 + §8.2、`CP6.Core/Services/BridgeHookBase.cs`、`CP6.WebApi/BackgroundServices/IntegrationEventRetryWorker.cs`
