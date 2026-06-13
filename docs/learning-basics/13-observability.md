# 13 · 日志 / 指标 / 追踪

## 🌱 你将学到

- "可观测性"是什么——这词听起来玄但其实就三件事
- 日志、指标、追踪各自适合解决什么问题
- 看懂 CP6 的 Bridge Health 监控
- 知道告警该报什么、不该报什么

---

## 🍳 生活类比：医院体检 vs 急诊 vs 病历

去医院看病有三种信息：

- **日志**：你的病历（医生写的每次就诊记录）—— 查"上次发生了什么"
- **指标**：体温、血压、心率（数字、能画曲线）—— 看"现在身体状况"
- **追踪**：CT 扫描显示血液经过哪些器官（一次操作的完整轨迹）—— 找"问题在哪一环"

医生想知道"为什么你最近头疼"：

- 看病历（日志）：3 个月前是什么样
- 看体温（指标）：在升高吗
- 看 CT（追踪）：脑供血哪里不顺

只看一种很难诊断。三者结合才有完整信息。

软件系统的"可观测性"=这三个。

---

## 🔎 看 CP6 代码

### 日志：OperLogFilter（已在第 07 章看过）

CP6 的操作日志通过 OperLogFilter 拦截每个请求，写到 `Sys_OperLog` 表 + Kafka topic。

记录的字段：

```
UserName / HttpMethod / RequestUrl / RequestBody / StatusCode / 
ElapsedMs / ClientIp / CreateDate
```

这是**业务审计日志**，回答"谁在什么时候做了什么"。

另一种是**应用日志**（`_logger.LogInformation(...)`），回答"程序内部发生了什么"。CP6 当前主要用 `Console.WriteLine` 和 `ILogger`，没接专门日志服务（如 Seq / Loki）。

### 结构化日志的写法

```csharp
// ❌ 字符串拼接（旧风格）
_logger.LogInformation($"User {userName} created order {orderNo} in {ms}ms");

// ✅ 结构化（推荐）
_logger.LogInformation(
    "User {UserName} created order {OrderNo} in {ElapsedMs}ms",
    userName, orderNo, ms);
```

结构化日志的好处：日志后端可以按字段查询（`UserName="tt" AND ElapsedMs > 1000`），而不只是 grep 字符串。

### 指标：Prometheus + prometheus-net

`Program.cs`：

```csharp
using Prometheus;

app.UseMetricServer();   // 暴露 /metrics 端点
app.UseHttpMetrics();    // 自动埋点：http_requests_total / http_request_duration_seconds 等
```

### CP6 的 Bridge 指标

`CP6.WebApi/Observability/BridgeMetricsCollector.cs`：

```csharp
public class BridgeMetricsCollector
{
    private static readonly Gauge BridgeSuccessTotal = Metrics
        .CreateGauge("cp6_bridge_success_total",
                     "Total successful bridge invocations in last 24h",
                     new[] { "source", "target", "hook" });

    private static readonly Gauge BridgeFailedTotal = Metrics
        .CreateGauge("cp6_bridge_failed_total", "...",
                     new[] { "source", "target", "hook" });

    private static readonly Gauge BridgeDlqDepth = Metrics
        .CreateGauge("cp6_bridge_dlq_depth", "...",
                     new[] { "source", "target", "hook" });

    public BridgeMetricsCollector(IServiceScopeFactory factory)
    {
        // 注册一个 callback：每次 Prometheus 来抓数据前更新一次
        Metrics.DefaultRegistry.AddBeforeCollectCallback(async ct => await UpdateAsync(ct));
    }

    private async Task UpdateAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IBridgeMetricsSnapshotProvider>();
        var snapshot = await provider.GetSnapshotAsync(ct);

        foreach (var item in snapshot.Items)
        {
            BridgeSuccessTotal.WithLabels(item.Source, item.Target, item.Hook).Set(item.Success);
            BridgeFailedTotal.WithLabels(item.Source, item.Target, item.Hook).Set(item.Failed);
            BridgeDlqDepth.WithLabels(item.Source, item.Target, item.Hook).Set(item.Dead);
        }
    }
}
```

数据来源：

```csharp
public async Task<BridgeMetricsSnapshot> GetSnapshotAsync(CancellationToken ct)
{
    var since = DateTime.UtcNow.AddHours(-24);
    var data = await _context.IntegrationEvents
        .Where(e => e.CreateDate >= since)
        .GroupBy(e => new { e.SourceModule, e.TargetModule, e.HookName })
        .Select(g => new BridgeMetricItem
        {
            Success = g.Count(e => e.Status == IntegrationEventStatus.Success),
            Failed = g.Count(e => e.Status == IntegrationEventStatus.Failed),
            Dead = g.Count(e => e.Status == IntegrationEventStatus.Dead)
        })
        .ToListAsync(ct);
    return new BridgeMetricsSnapshot { Items = data };
}
```

也就是说：访问 `/metrics` 时，CP6 会即时查 `T_IntegrationEvent` 表算出过去 24h 的统计，吐给 Prometheus。

### 追踪：CP6 当前没有

OpenTelemetry 能给每个请求一个 TraceId，所有日志自动带这个 ID，跨 Service 调用是一个 span，能看耗时分布。

CP6 当前没接入。改进点是加：

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation()
                       .AddEntityFrameworkCoreInstrumentation()
                       .AddOtlpExporter());
```

---

## 🤔 为什么这样

### Q1: 三者的边界

| 问题 | 用什么 |
|---|---|
| 谁在什么时候做了什么 | 日志 |
| 服务现在状态如何 / 趋势怎么样 | 指标 |
| 这个请求慢，慢在哪一步 | 追踪 |
| 出异常时的完整上下文 | 日志 |
| 告警 / SLO 监控 | 指标 |

**经验法则**：

- 高频信息（每个请求）→ 指标聚合，不要每条都打日志
- 低频但重要的事件 → 日志（异常、关键业务）
- 性能问题排查 → 追踪

### Q2: 为什么用 Prometheus 不写 DB

如果指标存数据库：

- 高频抓取写满 DB
- 查询聚合慢
- 没有合适的时序数据结构

Prometheus 是**时序数据库**，专为这种"每隔几秒采一次值"的场景设计。

工作流程：

1. 你的应用暴露 `/metrics` 端点（一个文本格式）
2. Prometheus 服务每 15 秒来抓一次
3. 抓到的数据存它的时序 DB
4. Grafana 从 Prometheus 查数据画图

### Q3: 应该记什么级别的日志

| 场景 | 级别 |
|---|---|
| 应用启动、关停 | Information |
| 业务关键动作（受注创建、登录） | Information |
| 业务校验失败（库存不足、权限拒绝） | Warning |
| 系统瞬态错误（Redis 抖动、MQ 重连） | Warning |
| 未处理异常 | Error |
| 数据丢失、不一致 | Critical |

**不要打日志的**：

- 每个 GET 请求（用指标统计就行）
- 敏感数据（密码、Token）
- 入参里的 PII（个人信息）

### Q4: CP6 的双通道告警

死信发生时：

```csharp
// CP6.Core/Services/DeadLetterNotifier.cs
public async Task NotifyAsync(IntegrationEvent evt)
{
    // 通道 1：SignalR 即时推送（前端看板弹 toast）
    await _hub.Clients.All.SendAsync("BridgeDeadLetter", evt);

    // 通道 2：写 Sys_OperLog 标 IsAlert=true（运维 query DB 看）
    _context.Sys_OperLogs.Add(new Sys_OperLog
    {
        UserName = "system",
        HttpMethod = "BRIDGE",
        StatusCode = 999,
        IsAlert = true,
        // ...
    });
    await _context.SaveChangesAsync();
}
```

**为什么两个通道**：

- SignalR 即时但易丢（断网就漏）
- DB 必达但需轮询

两者一起，万无一失。

---

## ⚠️ 容易搞错的地方

### 1. 每个 HTTP 请求都打 Info 日志

```csharp
// ❌ 反例
_logger.LogInformation("Request received {Path}", path);
```

每秒 1000 请求 → 每秒 1000 行日志 → 磁盘/日志服务费用爆炸。

请求日志走指标（`UseHttpMetrics()`）就够，不要全打日志。

### 2. 日志带敏感数据

```csharp
_logger.LogInformation("Login {Body}", request);   // ❌ Body 里有密码
```

CP6 OperLogFilter 跳过 `/api/auth` 就是这个原因。

### 3. 指标的 label 用了 userId / orderId

```csharp
// ❌ 反例
HttpRequests.WithLabels(userId).Inc();
```

每个 userId 创建一行指标 → 几万 userId 几万行指标 → Prometheus 内存爆炸（叫"high cardinality"）。

label 应该是有限的枚举值（如 status: success/skip/fail）。

### 4. 告警噪音过大

```
某项目上线一周，每天 200 条告警 → 没人看了
```

**修复方法**：

- 分级（紧急/警告/通知）
- 聚合（同一告警 5 分钟只发一次）
- 抑制（A 挂了，因 A 报错的 B/C/D 自动抑制）
- 删除（每周回顾没人响应的告警，删掉）

每个告警都要有"runbook"——"收到这个告警应该怎么做"。空告警 = 训练人忽视所有告警。

### 5. /metrics 暴露给公网

```yaml
# K8s service
ports:
  - port: 5000   # 同时暴露了 /metrics 和业务接口
```

`/metrics` 不该公网访问（信息泄露 + 可被 DDoS）。生产应该用 Network Policy 限制只让 Prometheus 抓。

---

## ✋ 动手试试

### 任务 1：访问一次 /metrics 看什么样

启动 CP6 后端，浏览器访问：

```
http://localhost:9991/metrics
```

应该看到一大坨文本，类似：

```
# HELP http_requests_total Total HTTP requests
# TYPE http_requests_total counter
http_requests_total{code="200",method="GET",controller="..."} 42
http_request_duration_seconds_bucket{le="0.005"} 30
http_request_duration_seconds_bucket{le="0.01"} 35
...

cp6_bridge_success_total{source="ERP",target="MES",hook="OnOrderCreated"} 1234
```

这就是 Prometheus 格式。把它喂给 Prometheus 服务就能画图。

### 任务 2：触发一次 Bridge Hook 看指标更新

调一次创建受注（前端或 API）。再访问 `/metrics`，找 `cp6_bridge_success_total` 行，看数字是否变了。

如果你登录前后看两遍，能看到这个指标的变化曲线（如果你有 Prometheus 服务）。

### 任务 3：看 Bridge Health 看板

启动前端，登录，访问 `/wms/bridge-health` 路径（如果有这个菜单）。

应该看到：

- 24h 总成功率 KPI
- 每个 Hook 一行的状态表
- 死信列表
- 30s 自动刷新

这是 CP6 的"运维大屏"。理解它的数据怎么来的（来自 `T_IntegrationEvent` 表的聚合）。

### 任务 4：写一个结构化日志

打开任意 Service，加一行：

```csharp
_logger.LogInformation(
    "Hello from {ServiceName}, count is {Count}",
    nameof(MyService), 42);
```

跑一次，看控制台输出。注意 `{ServiceName}` 和 `{Count}` 不是字符串模板，是结构化字段——好的日志后端会把它们存成独立字段方便查询。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/13-observability.md`](../learning/13-observability.md)
- Prometheus 官方：[Best Practices](https://prometheus.io/docs/practices/naming/)
- OpenTelemetry：[.NET 文档](https://opentelemetry.io/docs/instrumentation/net/)
- Serilog（.NET 结构化日志事实标准）：[官网](https://serilog.net/)
- 关键词搜索："Three Pillars of Observability"、"Structured Logging"
