# 08 · SignalR 实时推送 + 背压

## 🌱 你将学到

- "服务端主动推消息给浏览器"是怎么做到的（不是浏览器一直轮询）
- 看懂 CP6 的 NotifyHub / WmsHub / MesHub 分工
- 理解 SignalR 为什么不是裸 WebSocket
- 知道一秒推 500 条消息浏览器卡死时该怎么救

---

## 🍳 生活类比：广播 vs 轮询

**情景 A：你想知道快递到了没**
你每 5 分钟刷新一次快递 APP 看物流。这叫**轮询（polling）**。你累，服务器也累。

**情景 B：快递公司有更新就主动推你**
你订阅快递公司的微信公众号。一有更新就推一条消息。这叫**推送（push）**。

SignalR 是情景 B。

但 HTTP 协议本身是"客户端发起请求"模式，服务端没办法主动发消息。所以 SignalR 在 HTTP 之上建一个**长连接**通道：

- 浏览器跟服务端建立一个 WebSocket（一种长连接协议）
- 服务端有事就通过这个通道推
- 浏览器一直监听这个通道

---

## 🔎 看 CP6 代码

### Hub 注册

`Program.cs`：

```csharp
builder.Services.AddSignalR();

var app = builder.Build();
app.MapHub<NotifyHub>("/hubs/notify");
app.MapHub<WmsHub>("/hubs/wms");
app.MapHub<MesHub>("/hubs/mes");
```

每个 Hub 是一个"广播站"，绑到一个 URL（`/hubs/xxx`）。前端按 URL 连。

### Hub 类（极简）

`D:\CP6\CP6.WebApi\Hubs\NotifyHub.cs`：

```csharp
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

Hub 类本身可以很薄，CP6 这个只记连接事件。**真正的推消息逻辑不在 Hub 里**，在别处（BackgroundService、Service 类）。

### 怎么从 BackgroundService 推消息

```csharp
public class KafkaOperLogConsumer : BackgroundService
{
    private readonly IHubContext<NotifyHub> _hub;   // ← 不是 NotifyHub，是 IHubContext<NotifyHub>

    public KafkaOperLogConsumer(IHubContext<NotifyHub> hub, IServiceScopeFactory factory)
    {
        _hub = hub;
        _scopeFactory = factory;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var log = await ConsumeOneAsync(ct);

            // 写 DB
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
            db.Sys_OperLogs.Add(log);
            await db.SaveChangesAsync(ct);

            // 推送给所有连接的客户端
            await _hub.Clients.All.SendAsync("NewOperLog", log, ct);
        }
    }
}
```

为什么是 `IHubContext<NotifyHub>` 而不是 `NotifyHub`：

- `NotifyHub` 是 Scoped（每个连接事件创建一次）
- BackgroundService 不在连接的上下文里，没有 ConnectionId
- `IHubContext<T>` 是 Singleton，可以从任何地方推消息

### 前端连接

`cp6.web/src/utils/signalr.ts` 风格：

```typescript
import * as signalR from '@microsoft/signalr'

let connection: signalR.HubConnection | null = null

export function getConnection() {
  if (connection) return connection

  connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/notify', {
      accessTokenFactory: () => localStorage.getItem('token') || ''
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])   // 重连延迟
    .configureLogging(signalR.LogLevel.Warning)
    .build()

  connection.onreconnecting(err => console.warn('SignalR 正在重连', err))
  connection.onreconnected(id => console.log('SignalR 重连成功', id))
  connection.onclose(err => {
    console.error('SignalR 关闭', err)
    connection = null
  })

  return connection
}

export async function start() {
  const c = getConnection()
  if (c.state === signalR.HubConnectionState.Disconnected) await c.start()
  return c
}
```

### 前端监听

DashboardView 风格：

```typescript
onMounted(async () => {
  const conn = await start()
  conn.on('NewOperLog', (log) => {
    // 服务端推过来的消息
    recentLogs.value.unshift(log)
    if (recentLogs.value.length > 20) recentLogs.value.pop()
  })
})

onUnmounted(() => {
  conn.off('NewOperLog')   // 解绑！
})
```

---

## 🤔 为什么这样

### Q1: SignalR 跟 WebSocket 什么关系

WebSocket 是底层协议。SignalR 是基于 WebSocket（及其他后备方案）的高级框架。

SignalR 启动时按顺序尝试：

1. WebSocket（首选，全双工）
2. Server-Sent Events（次选，单向）
3. Long Polling（兜底，几乎任何浏览器都行）

哪个能用就用哪个。这是它比裸 WebSocket 强的地方：**优雅降级**。

### Q2: 为什么 CP6 有 3 个 Hub

如果只有一个 Hub 推所有消息：

- 业务员看到设备状态（不该看的）
- MES 推一秒 100 条设备状态，把 OperLog 推送也挤掉
- 前端代码一团乱（一个 hub 处理所有事件类型）

CP6 分 3 个：

- `NotifyHub`：全局通知（操作日志）
- `WmsHub`：库存相关
- `MesHub`：制造执行相关

前端只订阅自己关心的。

### Q3: 多副本部署的麻烦

```
用户 A 连到 Pod1 → Pod1 知道 A 的连接
库存变动发生在 Pod2 → Pod2 调 _hub.Clients.All.SendAsync(...)
   → Pod2 只知道连到自己的客户端，A 收不到
```

**解决**：加 Redis backplane。所有 Pod 通过 Redis pub/sub 同步消息：

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis("localhost:6379", o => o.Configuration.ChannelPrefix = "cp6");
```

CP6 当前没启 backplane（demo 单机部署够用）。生产 K8s 多副本必须配。

### Q4: 背压是什么

背压（backpressure）= 生产者比消费者快时的"反向压力"。

例：MES 高峰每秒推 500 条设备状态，浏览器只能 60 fps 渲染，渲染队列堆积 → 卡死。

**解决方法**：

- **服务端节流**：BackgroundService 攒 200ms 一次推一批，不是每条都推
- **前端节流**：`requestAnimationFrame` 控制渲染节奏
- **降低精度**：只推 diff（变化部分）不推全量

---

## ⚠️ 容易搞错的地方

### 1. 用 NotifyHub 而不是 IHubContext<NotifyHub>

```csharp
// ❌ 反例
public class MyBackgroundService(NotifyHub hub) { }   // 拿不到 → DI 报错
```

后台服务必须用 `IHubContext<NotifyHub>`。

### 2. onUnmounted 没解绑

```typescript
onMounted(() => conn.on('NewOperLog', handler))
// 没 onUnmounted({ conn.off(...) })
```

后果：

- 路由切回这个 view 时累加监听
- 同一事件触发多次
- 内存泄漏

CP6 的 view 应该全部成对 on/off。

### 3. 前端 connection 不重置

```typescript
connection.onclose(err => {
  console.error(err)
  // ❌ 没 connection = null
})

// 下次调 getConnection() 返回那个已关闭的，无法重连
```

CP6 的 `signalr.ts` 在 onclose 把 `connection = null`，让下次 getConnection 重建。

### 4. 反向代理不配 WebSocket

K8s Ingress 没加 WebSocket 注解 → 长连接连不上：

```yaml
nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"
nginx.ingress.kubernetes.io/websocket-services: "cp6-api"
```

CP6 的 `k8s/ingress.yaml` 已配。

### 5. 在 Hub 方法里抛普通异常

```csharp
public class WmsHub : Hub
{
    public async Task SomeAction()
    {
        throw new InvalidOperationException("详细错误信息");   // 默认不发给客户端
    }
}
```

默认 SignalR 出于安全考虑只把 "An error occurred" 发给客户端。要发详情用 `HubException`：

```csharp
throw new HubException("详细错误信息");   // 这个会送达客户端
```

---

## ✋ 动手试试

### 任务 1：打开浏览器 DevTools 看 SignalR 连接

启动 CP6 前端 + 后端，登录后打开浏览器 F12 → Network → 筛选 WS。

应该看到一个 WebSocket 连接到 `/hubs/notify` 或类似。点开看：

- Status: 101 Switching Protocols（WebSocket 握手成功）
- Messages 标签下能看到实时收到的消息

### 任务 2：触发一个事件看是否收到

打开 Dashboard 页（应该有 SignalR 监听）。

在另一个标签页发一次操作（如创建一个东西）。

观察 Dashboard 标签页有没有自动更新 / 弹通知。

如果有 → 完整链路通了：触发 → OperLogFilter → Kafka → Consumer → SignalR → 前端。

### 任务 3：故意断开 WiFi 看自动重连

打开 Dashboard，确认 SignalR 已连。

关闭 WiFi 几秒 → DevTools 应该看到 WebSocket 断开。

重新打开 WiFi → 等一会，应该自动重连（CP6 的 `withAutomaticReconnect` 配置）。

看 console 应该有 "SignalR reconnected" 类似日志。

### 任务 4（如果你能改代码）：自己加一个 Hub 事件

后端：

```csharp
// 某个 Controller 或 Service
await _hub.Clients.All.SendAsync("HelloFromServer", "测试消息", DateTime.Now);
```

前端：

```typescript
conn.on('HelloFromServer', (msg, time) => {
  console.log('收到:', msg, time)
  ElNotification({ title: msg, message: `时间: ${time}` })
})
```

调一下那个 Controller，看前端是否弹通知。

亲手做一遍"自己定义事件 + 自己监听"，对 SignalR 直觉立刻建立。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/08-signalr-and-backpressure.md`](../learning/08-signalr-and-backpressure.md)——讲 scale-out、Redis backplane
- 微软官方：[ASP.NET Core SignalR 概述](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction)
- 关键词搜索："WebSocket vs Server-Sent Events"、"SignalR Hub 教程"
