# 08 · SignalR 实时推送 + 背压

## 📍 学习目标

1. SignalR 怎么自动协商 WebSocket / SSE / Long Polling？为什么不直接用裸 WebSocket？
2. Hub 是 Singleton 还是每次新建？为什么后台 Service 推消息要用 `IHubContext`？
3. CP6 三个 Hub（NotifyHub / WmsHub / MesHub）怎么分工？
4. SignalR 怎么扩展（scale-out）到多实例？
5. 前端怎么处理掉线重连、消息背压（前端来不及处理）？

---

## 🔎 真实代码切片

### Hub 注册 + 端点映射

```csharp
// Program.cs
builder.Services.AddSignalR();

var app = builder.Build();
app.MapHub<NotifyHub>("/hubs/notify");
app.MapHub<WmsHub>("/hubs/wms");
app.MapHub<MesHub>("/hubs/mes");
```

### Hub 类（极简，主要是连接事件日志）

```csharp
// CP6.WebApi/Hubs/NotifyHub.cs
public class NotifyHub : Hub
{
    private readonly ILogger<NotifyHub> _logger;
    public NotifyHub(ILogger<NotifyHub> logger) => _logger = logger;

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("客户端连接: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("客户端断开: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
```

### 后台 Service 推消息：用 `IHubContext`

```csharp
public class KafkaOperLogConsumer : BackgroundService
{
    private readonly IHubContext<NotifyHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;

    public KafkaOperLogConsumer(IHubContext<NotifyHub> hub, IServiceScopeFactory factory)
    {
        _hub = hub;
        _scopeFactory = factory;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // 消费 Kafka topic ...
        while (!ct.IsCancellationRequested)
        {
            var log = await ConsumeOneAsync(ct);

            // 1. 写 DB（用 Scope 取 Scoped 服务）
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
            db.Sys_OperLogs.Add(log);
            await db.SaveChangesAsync(ct);

            // 2. 推 SignalR
            await _hub.Clients.All.SendAsync("NewOperLog", log, ct);
        }
    }
}
```

### Core 层定义抽象 + WebApi 层实现（依赖反转）

```csharp
// CP6.Core/Services/Wms/IWmsNotifier.cs
public interface IWmsNotifier
{
    Task NotifyStockChangedAsync(StockChangeNotification msg);
    Task NotifyMaterialShortageAsync(MaterialShortageNotification msg);
}

// CP6.WebApi/Services/SignalRWmsNotifier.cs
public class SignalRWmsNotifier : IWmsNotifier
{
    private readonly IHubContext<WmsHub> _hub;
    public SignalRWmsNotifier(IHubContext<WmsHub> hub) => _hub = hub;

    public Task NotifyStockChangedAsync(StockChangeNotification msg)
        => _hub.Clients.All.SendAsync("StockChanged", msg);

    public Task NotifyMaterialShortageAsync(MaterialShortageNotification msg)
        => _hub.Clients.All.SendAsync("MaterialShortage", msg);
}
```

### 前端连接（`cp6.web/src/utils/signalr.ts` 风格）

```typescript
import * as signalR from '@microsoft/signalr'

let connection: signalR.HubConnection | null = null

export function getConnection() {
  if (connection) return connection

  connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/notify', {
      accessTokenFactory: () => localStorage.getItem('token') || ''
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])  // 重连延迟
    .configureLogging(signalR.LogLevel.Warning)
    .build()

  connection.onreconnecting(err => console.warn('SignalR reconnecting', err))
  connection.onreconnected(id => console.log('SignalR reconnected', id))
  connection.onclose(err => {
    console.error('SignalR closed', err)
    connection = null  // 允许下次 getConnection 重建
  })

  return connection
}

export async function start() {
  const c = getConnection()
  if (c.state === signalR.HubConnectionState.Disconnected)
    await c.start()
  return c
}
```

### 前端订阅（DashboardView 风格）

```typescript
onMounted(async () => {
  const conn = await start()
  conn.on('NewOperLog', (log) => {
    pendingLogs.value.push(log)
    refreshSummary()  // 节流刷新
  })
  conn.on('StockChanged', (msg) => {
    ElNotification({ title: '库存变动', message: msg.summary, type: 'info' })
  })
})

onUnmounted(() => {
  conn.off('NewOperLog')
  conn.off('StockChanged')
})
```

---

## 💡 资深视角

### SignalR 不是 WebSocket，是"WebSocket / SSE / Long Polling 自动协商"

客户端连接时 SignalR 按顺序尝试：

1. **WebSocket**（全双工，效率最高）
2. **Server-Sent Events**（单向 server → client）
3. **Long Polling**（兜底，IE 也能跑）

服务端根据客户端能力自动选。这是它比裸 WebSocket 强的地方：**优雅降级**。

代价：稍多一些握手往返。生产环境基本都走 WebSocket。

### Hub 是 Scoped（每次连接新建）

```csharp
public class NotifyHub : Hub { ... }
```

Hub 类不是 Singleton。每次连接事件（OnConnected / Method Invoke / OnDisconnected）都会创建新实例。所以你可以在 Hub 里安全注入 Scoped 服务：

```csharp
public class WmsHub : Hub
{
    public WmsHub(CP6Context db, IStockMovementService svc) { ... }  // 可以
}
```

但**后台服务**（如 `KafkaOperLogConsumer`）不能直接注入 `WmsHub`，要用 `IHubContext<WmsHub>`：

```csharp
public KafkaOperLogConsumer(IHubContext<NotifyHub> hub) { ... }
```

`IHubContext<T>` 是 Singleton，能直接推消息但不能拿 ConnectionId 等连接上下文（因为根本没在某个连接的上下文里）。

### 三个 Hub 怎么分工

| Hub | 端点 | 推送内容 | 订阅前端 |
|---|---|---|---|
| `NotifyHub` | `/hubs/notify` | 操作日志 / 全局告警 | Dashboard、OperLogView |
| `WmsHub` | `/hubs/wms` | 库存变动、材料欠品、死信 | WMS 各 View、BridgeHealthView |
| `MesHub` | `/hubs/mes` | 工程实绩、设备状态 | MES 各 View、ControlTower 大屏 |

**为什么分多个 Hub** 而不是一个 NotifyHub 全包：

- **权限隔离**：不同角色看不同 Hub（业务员不该收到设备状态）
- **流量隔离**：MES 设备每秒上百条状态，跟 OperLog 分开避免互相挤压
- **业务清晰**：前端按业务模块订阅，代码更整洁

### SignalR 多实例扩展（scale-out）

单实例时所有连接都在一个进程内，`Clients.All` 直接广播。多实例（K8s 起 3 个副本）时：

```
用户 A 连到 Pod1 → Pod1 知道 A
用户 B 连到 Pod2 → Pod2 知道 B
后台 Service 在 Pod3 调用 _hub.Clients.All.SendAsync(...) 
   → Pod3 只知道连到自己的人，A/B 都收不到
```

**解决方案**：

#### 方案 A：Redis Backplane

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis("localhost:6379", o => o.Configuration.ChannelPrefix = "cp6");
```

所有 Pod 通过 Redis Pub/Sub 同步消息，任一 Pod `Clients.All` 都会广播到全部连接。

**代价**：每条消息多一次 Redis 往返。

#### 方案 B：Azure SignalR Service

托管服务，SDK 透明使用。`Clients.All` 自动跨实例。

**代价**：钱。

CP6 当前**没启用 backplane**。docker-compose 单机部署没问题；K8s 启 `replicas: 2` 时会有"消息漏发"问题（用户连到 A，事件发生在 B → 收不到）。**生产部署必须配 Redis backplane**。

### 背压问题：前端来不及处理

后端 1 秒推 100 条 `StockChanged`，前端只能 10 fps 渲染怎么办？

#### 后端节流

```csharp
// 比如棚卸时大量库存变动，合并批次推
public class StockBroadcastBatcher
{
    private readonly Channel<StockChangeNotification> _ch = Channel.CreateUnbounded<StockChangeNotification>();
    public async Task StartAsync(CancellationToken ct)
    {
        while (await _ch.Reader.WaitToReadAsync(ct))
        {
            await Task.Delay(200, ct);  // 攒 200ms
            var batch = new List<StockChangeNotification>();
            while (_ch.Reader.TryRead(out var msg)) batch.Add(msg);
            await _hub.Clients.All.SendAsync("StockChangedBatch", batch, ct);
        }
    }
}
```

#### 前端节流

```typescript
import { throttle } from 'lodash-es'
const refreshThrottled = throttle(refreshSummary, 500)
conn.on('NewOperLog', (log) => {
  recent.value.unshift(log)
  if (recent.value.length > 20) recent.value.pop()
  refreshThrottled()
})
```

#### 队列削峰

前端推荐用 `requestAnimationFrame` 控制渲染节奏：

```typescript
let pending: any[] = []
let scheduled = false
conn.on('TickerUpdate', (data) => {
  pending.push(data)
  if (!scheduled) {
    scheduled = true
    requestAnimationFrame(() => {
      flushBatch(pending)
      pending = []
      scheduled = false
    })
  }
})
```

### 鉴权：JWT 通过 query string 传

SignalR 的 WebSocket 不能自定义 Header（浏览器 API 限制），所以 JWT 通过 query string `?access_token=xxx` 传。`accessTokenFactory` 就是干这事的。

后端需要在 `AddJwtBearer` 加：

```csharp
.AddJwtBearer(options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            var path = ctx.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                ctx.Token = token;
            return Task.CompletedTask;
        }
    };
});
```

CP6 当前没在 Hub 上加 `[Authorize]`（demo 简化），生产要加。

### 反向代理的坑

Nginx / Ingress 转发 SignalR WebSocket 时要：

```nginx
location /hubs/ {
    proxy_pass http://cp6-api:5000;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_read_timeout 3600s;   # 心跳间隔够长
    proxy_send_timeout 3600s;
}
```

K8s Ingress 用 annotation：

```yaml
nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"
nginx.ingress.kubernetes.io/proxy-send-timeout: "3600"
nginx.ingress.kubernetes.io/websocket-services: "cp6-api"
```

CP6 的 `k8s/ingress.yaml` 已经配好。

---

## ⚠️ 踩坑记录

### 坑 1：在 Hub 里持有连接列表

```csharp
public class WmsHub : Hub
{
    private static readonly List<string> _connections = new();  // ❌

    public override Task OnConnectedAsync()
    {
        _connections.Add(Context.ConnectionId);   // 线程不安全
        return base.OnConnectedAsync();
    }
}
```

Hub 是 Scoped，但这个 static 列表跨实例共享。问题：

- 非线程安全（多个连接同时进入）
- 多实例部署时只记当前 Pod 的连接

**正确做法**：用 `Groups`（按 user 分组），或外部存（Redis）。

### 坑 2：在 Hub 方法里抛异常

```csharp
public class WmsHub : Hub
{
    public async Task AllocateStock(...)
    {
        throw new InvalidOperationException("库存不足");  // 客户端会收到 HubException
    }
}
```

默认 SignalR 不把异常细节发给客户端，只发"An error occurred"。要发详情：

```csharp
builder.Services.AddSignalR(o => o.EnableDetailedErrors = true);  // 仅开发开
```

或用 `HubException` 显式抛：

```csharp
throw new HubException("库存不足");
```

`HubException` 的 message 一定送达客户端。

### 坑 3：DashboardView 自循环触发死循环（CP6 真实坑）

```typescript
// ❌ 反例（CP6 Phase 8 文档里记录的坑）
conn.on('NewOperLog', () => {
  refreshSummary()   // 这里又会触发 GET /api/dashboard，被 OperLogFilter 记录
  // ↑ 但 GET 默认不记 oplog，所以其实安全
  // 真正的坑：如果配了 IncludeGet=true，GET 也记 → 触发 NewOperLog → 又 refreshSummary → 死循环
})
```

CP6 的解法：dashboard 的 refresh 不放在 NewOperLog 监听里，独立 onMounted + 手动 refresh 按钮。

### 坑 4：onClosed 后没重置 connection

如果 `connection.onclose` 不把变量置 null，下次 `getConnection()` 仍然返回那个关闭的实例，无法重连。CP6 的 `signalr.ts` 在 `onclose` 里 `connection = null` 触发重建。

### 坑 5：Hub 内部 throw 不记日志

Hub 方法异常默认不进 ASP.NET Core 的日志管道，要自己注 ILogger 记。或者写一个 `HubFilter`（.NET 7+）。

---

## 🧪 自检题

1. **连接生命周期**：用户登录后打开 5 个浏览器 tab，他在后端有几个 SignalR 连接？  
   <details><summary>答案</summary>5 个（每个 tab 独立 ConnectionId）。如果想"同一用户只算一组"，用 <code>Groups.AddToGroupAsync(ConnectionId, userId)</code> 加分组，推送时 <code>Clients.Group(userId).SendAsync(...)</code>。</details>

2. **生命周期**：BackgroundService 注入 IHubContext 推消息 vs 注入 Hub 推消息，哪种对？为什么？  
   <details><summary>答案</summary>必须用 IHubContext。Hub 是 Scoped（基于连接），后台没有连接上下文，注入 Hub 会失败（DI 报错）。IHubContext 是 Singleton，专门给"连接外"代码推消息。</details>

3. **scale-out**：K8s 启 3 个副本，用户连到 Pod1，库存变动发生在 Pod2 的 Service 调用，怎么让用户收到？  
   <details><summary>答案</summary>(1) 启用 Redis backplane：<code>AddSignalR().AddStackExchangeRedis(...)</code>。所有 Pod 通过 Redis 同步消息；(2) 或换成 Azure SignalR Service；(3) Sticky Session（同一用户固定连到一个 Pod）只能解决"该用户的多 tab 同步"，不能解决"跨 Pod 的服务推送"。</details>

4. **背压实战**：大屏每秒收到 500 条设备状态推送，浏览器卡死，怎么救？  
   <details><summary>答案</summary>(1) <b>后端节流合并</b>：BackgroundService 攒 200ms 一次推批量；(2) <b>前端 RAF 批处理</b>：requestAnimationFrame 控制渲染节奏；(3) <b>降低推送精度</b>：只推变化（diff），不推全量；(4) 极端情况上 WebTransport 或自己分片。CP6 的 ControlTower 大屏就是结合后端 OEE 计算 worker 周期推 + 前端 RAF 渲染。</details>

5. **质疑题**：直接用 WebSocket 不行吗？为什么用 SignalR 这个抽象？  
   <details><summary>答案</summary>SignalR 提供：(1) 自动协议协商（IE 也能跑 long polling 兜底）；(2) 自动重连；(3) 心跳保活；(4) 鉴权集成（AddJwtBearer 一行）；(5) Group / User 分组路由；(6) 强类型 Hub（SignalR Strong Typed Client）。自己写 WebSocket 这些都要手撕。但如果只对接特定客户端（如 Unity 游戏、IoT 设备），可以裸 WebSocket 更轻。</details>

---

## 🔗 延伸阅读

- [ASP.NET Core SignalR Overview](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction)
- [Scale-out with Redis backplane](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane)
- [SignalR Hub filters (.NET 7+)](https://learn.microsoft.com/en-us/aspnet/core/signalr/hub-filters)
- 项目内：`CP6.WebApi/Hubs/`、`cp6.web/src/utils/signalr.ts`、`k8s/ingress.yaml`
