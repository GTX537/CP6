# 06 · 跨模块联动：Bridge Hook

## 🌱 你将学到

- ERP / MES / WMS 是三个独立模块，但又要互相通知，CP6 怎么做的
- 看懂 `IMesBridgeHook` 这种接口为什么存在
- "best-effort"、"幂等"、"死信"这些词的实际含义
- 理解一种"既解耦又轻量"的跨模块通信方式

---

## 🍳 生活类比：外卖平台 vs 餐厅 vs 骑手

外卖平台收到订单后，要：

1. 通知餐厅做菜
2. 通知骑手取餐

**方案 A：外卖平台直接打电话**
外卖平台员工拿起电话打给餐厅，再打给骑手。
问题：员工要记住所有餐厅和骑手的电话。某个餐厅倒闭了员工还在打。换骑手 → 改电话本。

**方案 B：外卖平台只发"通知"**
外卖平台往"订单广播"里扔一个消息："订单 1234 来了，餐厅 X、产品 Y"。餐厅和骑手各自监听这个广播，收到自己的就响应。
平台不知道谁监听了。

Bridge Hook 是方案 B 的简化版——但在**同一个进程内**，不用真的发消息。

---

## 🔎 看 CP6 代码

### 4 个 Bridge Hook 接口

CP6 把跨模块的"通知点"定义成 4 个接口：

```csharp
public interface IMesBridgeHook
{
    Task OnOrderCreatedAsync(string webOrderNo, string user);   // ERP 受注创建后 → MES 自动展开工单
}

public interface IWmsBridgeHook
{
    Task OnOrderCreatedAsync(string webOrderNo, string user);
    Task OnWorkOrderIssuedAsync(string workOrderNo, string user);  // MES 工单发行后 → WMS 出库
    Task OnProductionCompletedAsync(string workOrderNo, decimal goodQty, string user);
}

public interface IErpBridgeHook
{
    Task OnShipmentConfirmedAsync(string outboundNo, string user);   // WMS 出货后 → ERP 回写
}

public interface IOrderCancelBridgeHook  // Phase 6 加的
{
    Task<CancelPlan> ProbeAsync(string webOrderNo);
    Task ExecuteAsync(string webOrderNo, string reason, string user);
}
```

### 调用方：触发通知

打开 ERP 的 `OrderService.CreateAsync`（伪代码）：

```csharp
public async Task<Order> CreateAsync(OrderCreateDto dto, string user)
{
    var order = new Order { /* ... */ };
    _context.Orders.Add(order);
    await _context.SaveChangesAsync();   // 受注创建完成

    // 通知 MES 和 WMS —— best-effort 模式
    try
    {
        await _mesBridge.OnOrderCreatedAsync(order.WebOrderNo, user);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "MES bridge failed, ignoring");
    }

    try
    {
        await _wmsBridge.OnOrderCreatedAsync(order.WebOrderNo, user);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "WMS bridge failed, ignoring");
    }

    return order;
}
```

注意：

- ERP 知道 `IMesBridgeHook` 接口，但不知道实现是谁
- 调用包 `try { } catch { 不抛 }` → 即使 MES 故障，受注照样创建成功

### 接收方：实现 hook

MES 模块实现 `IMesBridgeHook`：

```csharp
public class MesBridgeHook : BridgeHookBase, IMesBridgeHook
{
    public async Task OnOrderCreatedAsync(string webOrderNo, string user)
    {
        try
        {
            var wo = await _workOrderService.ExpandFromOrderAsync(webOrderNo, user);
            // 写一条成功记录
            await PersistEventAsync("ERP", "MES", nameof(OnOrderCreatedAsync),
                webOrderNo, IntegrationEventStatus.Success, targetNo: wo.WorkOrderNo);
        }
        catch (InvalidOperationException biz)   // 业务异常（已展开）
        {
            await PersistEventAsync(..., IntegrationEventStatus.Skipped);
        }
        catch (Exception ex)   // 意外异常
        {
            await PersistEventAsync(..., IntegrationEventStatus.Failed);
            throw;
        }
    }
}
```

### 注册时按开关切换

`Program.cs`：

```csharp
var mesBridgeEnabled = builder.Configuration.GetValue<bool?>("MesBridge:Enabled") ?? false;
if (mesBridgeEnabled)
    builder.Services.AddScoped<IMesBridgeHook, MesBridgeHook>();
else
    builder.Services.AddScoped<IMesBridgeHook, NoOpMesBridgeHook>();   // 空实现
```

`NoOpMesBridgeHook` 的实现是空的（什么都不做）：

```csharp
public class NoOpMesBridgeHook : IMesBridgeHook
{
    public Task OnOrderCreatedAsync(string webOrderNo, string user) => Task.CompletedTask;
}
```

配置开关 `MesBridge:Enabled = false` → DI 注入 NoOp → 调用 hook 等于什么都没发生。

### T_IntegrationEvent 表

每次 hook 调用都写一行记录：

```csharp
public class IntegrationEvent : BaseEntity
{
    public string SourceModule { get; set; }    // ERP
    public string TargetModule { get; set; }    // MES
    public string HookName { get; set; }        // OnOrderCreatedAsync
    public string CorrelationId { get; set; }   // webOrderNo
    public string PayloadJson { get; set; }     // 入参 JSON
    public IntegrationEventStatus Status { get; set; }  // SUCCESS / SKIPPED / FAILED / DEAD
    public int Attempts { get; set; }
    public DateTime? NextRetryAt { get; set; }
}
```

### 自动重试 Worker

```csharp
public class IntegrationEventRetryWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 每 60 秒扫一次 Failed 状态的事件
            using var scope = _factory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
            var pending = await db.IntegrationEvents
                .Where(e => e.Status == IntegrationEventStatus.Failed
                         && e.NextRetryAt <= DateTime.UtcNow
                         && e.Attempts < 5)
                .Take(50)
                .ToListAsync(ct);

            foreach (var evt in pending)
            {
                try
                {
                    await dispatcher.DispatchAsync(evt);   // 重新调原 hook
                    evt.Status = IntegrationEventStatus.Success;
                }
                catch
                {
                    evt.Attempts++;
                    if (evt.Attempts >= 5)
                    {
                        evt.Status = IntegrationEventStatus.Dead;
                        await _deadLetter.NotifyAsync(evt);   // 死信告警
                    }
                }
                await db.SaveChangesAsync(ct);
            }

            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }
}
```

---

## 🤔 为什么这样

### Q1: 为什么不让 ERP 直接 `using` MES？

```csharp
// ❌ 反例
using CP6.Core.Services.Mes;

public class OrderService
{
    public OrderService(WorkOrderService woService) { }   // ERP 直接知道 MES
}
```

问题：

- ERP 和 MES 互相耦合，改 MES 接口 ERP 也要改
- 测试 ERP 要把 MES 也启动
- 关掉 MES 模块 ERP 也得改

用接口 `IMesBridgeHook` 反转依赖：ERP 知道接口（在自己这边），不知道实现（在 MES 那边）。改 MES 实现 ERP 不感知。

### Q2: 什么是 best-effort？

best-effort = "尽力而为"。意思：

- 主操作（受注创建）必须成功
- 联动操作（通知 MES）"尽力做"，失败不阻塞主操作

CP6 的实现方式：try-catch 把 hook 调用包起来，catch 里只记日志不抛。

**反义词**：strict / strong consistency = 强一致。意思 hook 也必须成功，否则整个事务回滚。CP6 没用强一致，因为：

- MES 短暂故障不该让 ERP 拒收订单
- 异步重试机制能补回来

### Q3: 什么是幂等？

幂等 = 同一操作做几次结果一样。

```
GET /api/order/123   ← 幂等（查 N 次结果一样）
DELETE /api/order/123  ← 幂等（删了再删还是删了）
POST /api/order  ← 不幂等（每次创建新订单）
```

Bridge Hook 必须幂等，因为：

- 自动重试可能调多次同一 hook
- 没人能保证调用方只调一次

CP6 的幂等实现：

```csharp
public async Task ExpandFromOrderAsync(string webOrderNo, string user)
{
    var existing = await _context.WorkOrders.AnyAsync(w => w.WebOrderNo == webOrderNo);
    if (existing)
        throw new InvalidOperationException("WO already exists");  // 业务 SKIP，不是 FAIL
    // ... 真正展开
}
```

重复调用时检测到已有，抛 `InvalidOperationException`，被 Hook 接住后标 Skipped 状态，不当失败重试。

### Q4: 死信（Dead Letter）什么意思

消息队列术语。一条消息重试 N 次还失败 → 不再自动重试，扔到"死信队列"等人工处理。

CP6 的死信：Attempts ≥ MaxAttempts (5) → Status = Dead → 触发 `DeadLetterNotifier`：

1. SignalR push 给前端的 BridgeHealthView（值班看板）
2. 写 Sys_OperLog 标 IsAlert=true

运维介入，手动 Compensate（点按钮触发重新调用）或标记为已处理。

---

## ⚠️ 容易搞错的地方

### 1. 把 hook 放在事务里

```csharp
// ❌ 反例
using var tx = await _context.Database.BeginTransactionAsync();
_context.Orders.Add(order);
await _context.SaveChangesAsync();
await _mesBridge.OnOrderCreatedAsync(order.WebOrderNo, user);   // ← 这一行失败回滚
await tx.CommitAsync();
```

`OnOrderCreatedAsync` 失败会让整个事务回滚，受注没创建成功。失去 best-effort 意义。

正确：受注创建提交事务后，再发 hook 通知（CP6 的做法）。

### 2. hook 内部又调对方 service

```csharp
// ❌ 反例
public class MesBridgeHook
{
    public async Task OnOrderCreatedAsync(...)
    {
        await _workOrderService.ExpandFromOrderAsync(...);
        await _erpBridge.OnSomethingElse(...);   // ← MES hook 又调回 ERP
    }
}
```

容易出循环（ERP → MES → ERP → MES → ...）。CP6 规则：hook 只调本模块的 Service，不调对方模块的 hook。

### 3. NoOpXxxHook 实现不彻底

```csharp
// ❌ 反例
public class NoOpMesBridgeHook : IMesBridgeHook
{
    public Task OnOrderCreatedAsync(string webOrderNo, string user)
    {
        throw new NotImplementedException();   // ← 这不叫 NoOp
    }
}
```

NoOp 的本义是"什么都不做"。要返回 `Task.CompletedTask` 才对。否则关掉开关后整个系统崩。

### 4. 忘了写 IntegrationEvent

如果 hook 没写 `T_IntegrationEvent`：

- 重试 worker 看不到失败的事件 → 不会重试
- 健康看板看不到统计 → 运维盲

CP6 用 `BridgeHookBase.PersistEventAsync` 强制每个 hook 都写记录。

---

## ✋ 动手试试

### 任务 1：跑通一次跨模块调用

启动后端，登录，创建一个受注（前端 ERP/受注入力页面）。

打开数据库（用 SSMS 或 Azure Data Studio）：

```sql
SELECT TOP 10 * FROM T_IntegrationEvent ORDER BY CreateDate DESC;
```

应该看到刚才创建受注触发的 hook 记录（如果 MesBridge:Enabled = true）。

看每条记录的 Status / TargetNo / PayloadJson，理解 hook 实际做了什么。

### 任务 2：把开关关掉再看一次

打开 `D:\CP6\CP6.WebApi\appsettings.json` 或 `appsettings.Local.json`，加：

```json
"MesBridge": { "Enabled": false }
```

重启后端。再创建一个受注。再查 `T_IntegrationEvent`。

应该看到 MES 那条没产生（因为换成 NoOp 了），WMS 那条还有。这就是"按配置切换实现"的实际效果。

**实验完别忘改回来**。

### 任务 3：故意让 hook 失败看死信流程

如果你能改代码（不上线），在 `MesBridgeHook.OnOrderCreatedAsync` 一开始故意抛异常：

```csharp
public async Task OnOrderCreatedAsync(...)
{
    throw new Exception("故意失败");
}
```

启动后端创建受注。

查 `T_IntegrationEvent`：第一条 Status=Failed，NextRetryAt 是 60 秒后。
等 5 分钟 retry worker 跑了 5 次 → Status=Dead → 触发 `DeadLetterNotifier` → SignalR 推送 + Sys_OperLog 写一条 IsAlert=true 的记录。

打开前端 `/wms/bridge-health` 应该能看到死信。

完整看到整个"自动重试 → 死信 → 告警"循环，对 Bridge Hook 模式就建立直觉了。

**实验完恢复代码**。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/06-bridge-hook-pattern.md`](../learning/06-bridge-hook-pattern.md)——讲跟 MQ / MediatR 的对比
- 关键词搜索："Outbox pattern"、"Best-effort delivery"、"Dead letter queue"
- 项目内：`CP6.Core/Services/BridgeHookBase.cs`、`CP6.WebApi/BackgroundServices/IntegrationEventRetryWorker.cs`、前端 `BridgeHealthView.vue`
