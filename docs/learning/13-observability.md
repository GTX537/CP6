# 13 · 可观测性：日志 / 指标 / 追踪

## 📍 学习目标

1. 可观测性"三大支柱"（Logs / Metrics / Traces）各自解决什么问题？
2. 结构化日志（structured logging）vs 字符串日志的本质区别
3. CP6 怎么用 Prometheus + prometheus-net 暴露业务指标？
4. Bridge Health Monitor 是怎样一个生产级别的"指标 + 看板 + 告警"实现？
5. 分布式追踪（distributed tracing）在单体 + 多线程后台 worker 场景里怎么落地？
6. 怎么判断一条日志该不该记？该记成什么级别？

---

## 🔎 真实代码切片

### 操作日志 — 文本审计流（详见第 7 章）

```csharp
// OperLogFilter 投递到 Kafka topic 'cp6.operlog'
var log = new Sys_OperLog
{
    UserName = userName,
    HttpMethod = method,
    RequestUrl = path,
    Controller = controllerName,
    Action = actionName,
    RequestBody = requestBody,
    StatusCode = statusCode,
    ElapsedMs = stopwatch.ElapsedMilliseconds,
    ClientIp = clientIp,
    CreateDate = DateTime.Now
};
await _transport.PublishAsync(log);
```

### Prometheus 指标 — `BridgeMetricsCollector`

```csharp
// CP6.WebApi/Observability/BridgeMetricsCollector.cs (示意)
public class BridgeMetricsCollector
{
    private static readonly Gauge BridgeSuccessTotal = Metrics
        .CreateGauge("cp6_bridge_success_total",
                     "Total successful bridge invocations in last 24h",
                     new[] { "source", "target", "hook" });

    private static readonly Gauge BridgeFailedTotal = Metrics
        .CreateGauge("cp6_bridge_failed_total",
                     "Total failed bridge invocations in last 24h",
                     new[] { "source", "target", "hook" });

    private static readonly Gauge BridgeDeadLetterQueueDepth = Metrics
        .CreateGauge("cp6_bridge_dlq_depth",
                     "Current count of dead-lettered events",
                     new[] { "source", "target", "hook" });

    private readonly IServiceScopeFactory _scopeFactory;

    public BridgeMetricsCollector(IServiceScopeFactory factory)
    {
        _scopeFactory = factory;
        // prometheus-net 在每次 scrape 前回调
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
            BridgeDeadLetterQueueDepth.WithLabels(item.Source, item.Target, item.Hook).Set(item.Dead);
        }
    }
}
```

### `Program.cs` 暴露 /metrics 端点

```csharp
using Prometheus;

// ...
app.UseMetricServer();        // /metrics
app.UseHttpMetrics();         // HTTP request 自动埋点
```

### 业务指标快照来源 —— `BridgeMetricsSnapshotProvider`

```csharp
public class BridgeMetricsSnapshotProvider : IBridgeMetricsSnapshotProvider
{
    private readonly CP6Context _context;

    public async Task<BridgeMetricsSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var data = await _context.IntegrationEvents
            .Where(e => e.CreateDate >= since)
            .GroupBy(e => new { e.SourceModule, e.TargetModule, e.HookName })
            .Select(g => new BridgeMetricItem
            {
                Source = g.Key.SourceModule,
                Target = g.Key.TargetModule,
                Hook = g.Key.HookName,
                Success = g.Count(e => e.Status == IntegrationEventStatus.Success),
                Skipped = g.Count(e => e.Status == IntegrationEventStatus.Skipped),
                Failed = g.Count(e => e.Status == IntegrationEventStatus.Failed),
                Dead = g.Count(e => e.Status == IntegrationEventStatus.Dead)
            })
            .AsNoTracking()
            .ToListAsync(ct);

        return new BridgeMetricsSnapshot { Items = data };
    }
}
```

### 死信告警 —— `DeadLetterNotifier`

```csharp
public async Task NotifyAsync(IntegrationEvent evt)
{
    // 通道 1：SignalR 即时推送（前端 BridgeHealthView 弹 toast）
    await _hub.Clients.All.SendAsync("BridgeDeadLetter", evt);

    // 通道 2：写 Sys_OperLog 标 IsAlert=true（运维大屏定时查询）
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
```

### 结构化日志示例（ILogger）

```csharp
_logger.LogWarning(
    "Bridge hook {HookName} failed for {CorrelationId}, attempts={Attempts}",
    evt.HookName, evt.CorrelationId, evt.Attempts);
```

---

## 💡 资深视角

### 三大支柱

| 支柱 | 回答的问题 | CP6 实现 |
|---|---|---|
| **Logs** | "刚才发生了什么？" | `Sys_OperLog` 文本审计 + Console / ILogger |
| **Metrics** | "现在系统状态如何？" | Prometheus `/metrics`（HTTP + Bridge 业务指标） |
| **Traces** | "这一条请求走了哪些组件？" | CP6 目前没用 OpenTelemetry，未做 |

**经验法则**：

- 异常 → Logs（带 stack trace）
- 趋势/告警 → Metrics（聚合数据，告警便宜）
- 性能定位 → Traces（看链路耗时分布）

### 结构化日志为什么必须

```csharp
// ❌ 文本日志
_logger.LogInformation($"User {userName} created order {orderNo} in {ms}ms");
// 输出："User tt created order ORD-001 in 245ms"

// ✅ 结构化日志
_logger.LogInformation("User {UserName} created order {OrderNo} in {ElapsedMs}ms",
                       userName, orderNo, ms);
// 输出（Serilog JSON）:
// { "UserName": "tt", "OrderNo": "ORD-001", "ElapsedMs": 245, ... }
```

**结构化的好处**：

- 可以按字段查询：`UserName = "tt" AND ElapsedMs > 1000`
- 可以聚合：每用户平均响应时间
- ELK / Loki / Datadog 都按字段索引

**反例**：拼字符串后日志只能 `grep`，过滤性能差。

### Prometheus 的 4 种指标类型

| 类型 | 用途 | 例子 |
|---|---|---|
| **Counter** | 单调递增 | `http_requests_total` |
| **Gauge** | 可上可下的瞬时值 | `cp6_bridge_dlq_depth` |
| **Histogram** | 分桶统计 | `http_request_duration_seconds_bucket` |
| **Summary** | 客户端算分位 | 一般不推荐，用 Histogram |

CP6 用 Gauge 暴露"过去 24h 成功/失败次数"是非标准的（Counter 更合适，Prometheus 服务端聚合时间窗）。

**严谨的写法**：

```csharp
private static readonly Counter BridgeInvocations = Metrics
    .CreateCounter("cp6_bridge_invocations_total",
                   "Bridge hook invocations",
                   new[] { "source", "target", "hook", "status" });

// Hook 执行时
BridgeInvocations.WithLabels("ERP", "MES", "OnOrderCreated", "success").Inc();
```

然后 Grafana 用 `increase(cp6_bridge_invocations_total[24h])` 算 24h 增量。

CP6 选 Gauge + 每次 scrape 时查 DB 重算 24h，**好处**是 DB 是单一真实源（重启不丢），**代价**是每次 scrape 都跑一次 DB 聚合查询。生产环境如果 Prometheus 抓取间隔 15s 则每 15s 一次查询，可接受。

### Bridge Health 看板的设计

```
┌─────────────────────────────────────────────────┐
│  Bridge Hook Health (Last 24h)                  │
│  Success rate: 98.5% | Queue depth: 3 | DLQ: 2  │
├─────────────────────────────────────────────────┤
│ Hook                  Total  OK  Skip  Fail  DLQ│
│ ERP→MES OnOrderCreat  1234  1200  30   3   1    │
│ MES→WMS OnWoIssued     567   560   5   1   1    │
│ ...                                              │
├─────────────────────────────────────────────────┤
│  Recent Dead Letters:                            │
│  • ORD-099 [ERP→MES] DB timeout × 5  [Compensate]│
│  • WO-052  [MES→WMS] Skipped duplicate           │
└─────────────────────────────────────────────────┘
```

CP6 的 `BridgeHealthView.vue` 实现了这个看板（按 PROJECT_STRUCTURE.md §8.7）。设计精髓：

1. **KPI 卡片**：让值班人 30 秒看完关键数字
2. **每 Hook 一行**：找出"哪条联动有问题"
3. **DLQ 详情**：可直接补偿（点 Compensate 按钮触发 Worker）
4. **30s 自动刷新**：不用手动 F5

### "应该记什么日志" 的清单

| 场景 | 级别 | 应记 |
|---|---|---|
| 启动 / 关停 | Information | 版本、配置摘要、监听端口 |
| 业务关键动作 | Information | 受注创建、出货确认、用户登录 |
| 业务校验失败 | Warning | 库存不足、权限拒绝、数据冲突 |
| 系统瞬态错误 | Warning | Redis 抖动、MQ 重连 |
| 未处理异常 | Error | 含 stack trace、关键参数 |
| 数据丢失 / 不一致 | Critical | 告警值班 |

**不要记**：

- 高频 GET 请求（OperLogFilter 默认就跳过 GET）
- 敏感数据（密码、Token、信用卡号）
- 入参原文里的 PII（用脱敏中间件）

### CP6 缺什么 —— 改进方向

#### 1. 缺 OpenTelemetry 分布式追踪

Bridge Hook 链 `ERP.OrderService.CreateAsync → WMS Bridge → WMS Service` 在一个进程内但跨 Service 调用。OpenTelemetry 能：

- 给每个请求一个 TraceId，所有日志自动带
- 每个 Service 调用是一个 Span，可以看耗时分布
- 后台 Worker 重试也能关联到原始请求

加法：

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation()
                       .AddEntityFrameworkCoreInstrumentation()
                       .AddSource("CP6.Bridge")
                       .AddOtlpExporter());
```

#### 2. 缺业务 SLA 指标

```csharp
private static readonly Histogram OrderCreateDuration = Metrics
    .CreateHistogram("cp6_order_create_duration_seconds",
                     "Time to create an order",
                     new HistogramConfiguration { Buckets = new[] { 0.1, 0.5, 1, 2, 5, 10 } });

// OrderService.CreateAsync
using (OrderCreateDuration.NewTimer())
{
    // ...
}
```

然后 Grafana 看 p95 / p99 延迟。

#### 3. 缺日志聚合

CP6 日志写 `Sys_OperLog` 表 + `Console.WriteLine`。生产应该：

- 用 Serilog + Seq / Loki / ELK
- 应用日志和审计日志分离（审计走 OperLog 表，应用走 stdout → 容器日志收集）

### 关键告警怎么定义

```promql
# 1. Bridge Hook 死信
cp6_bridge_dlq_depth > 0
# 任何一条死信都告警，运维介入手动 Compensate

# 2. 失败率超过 5%
sum(rate(cp6_bridge_invocations_total{status="failed"}[5m])) 
/ sum(rate(cp6_bridge_invocations_total[5m])) > 0.05

# 3. API 5xx 超过 1%
sum(rate(http_requests_total{code=~"5.."}[5m])) 
/ sum(rate(http_requests_total[5m])) > 0.01

# 4. p99 响应时间超过 2s
histogram_quantile(0.99, http_request_duration_seconds_bucket) > 2
```

每条告警都要有"runbook"（怎么响应）。空告警 = 训练值班人忽视所有告警。

---

## ⚠️ 踩坑记录

### 坑 1：每个请求都记 Info 级日志 → 日志洪水

```csharp
// ❌ 反例
_logger.LogInformation("Request received {Path}", path);
```

每秒 1000 请求 → 每秒 1000 行日志 → 磁盘 / 日志服务费用爆炸。

修复：

- HTTP 请求日志走 `UseHttpMetrics()`（埋指标，不打日志）
- 真要打用 Debug 级，prod 关掉

### 坑 2：日志带敏感数据

```csharp
_logger.LogInformation("Login attempt {Body}", request);  // ❌ Body 里有密码
```

修复：

- 用专门的 Login Request 类，重写 ToString() 屏蔽
- 或在 OperLogFilter 里 PII 脱敏中间件
- CP6 跳过 `/api/auth` 路径就是这个原因

### 坑 3：Prometheus 指标 cardinality 爆炸

```csharp
// ❌ 反例
HttpRequests.WithLabels(userId).Inc();   // userId 几万个 → 几万条 metric line
```

每个 label 值组合都生成一条 metric。CP6 用 `(source, target, hook)` 三维约 10 种组合，安全。`userId` / `orderId` 永远不要做 label。

### 坑 4：MetricServer 暴露给外网

```yaml
# K8s
spec:
  ports:
    - port: 5000
      targetPort: 5000   # 同时暴露了 /metrics
```

`/metrics` 不该外网可达（信息泄露 + 被 DDoS）。生产：

```csharp
app.MapGet("/metrics", async ctx => { /* 写 prometheus 数据 */ })
   .RequireAuthorization("internal");
```

或 K8s 配 Network Policy 只让 Prometheus 抓取。

### 坑 5：缺 TraceId 关联

```
日志 1：[10:00:01] Order created
日志 2：[10:00:01] Bridge hook failed
日志 3：[10:00:01] Retry scheduled
```

三条日志怎么知道是同一请求的？没有 TraceId → 排查靠人脑拼。

修复：用 `IHttpContextAccessor` 注入 + Serilog Enricher 自动加 TraceId 到所有日志。

---

## 🧪 自检题

1. **日志级别**：用户输入密码错误 N 次被锁定，应记什么级别？  
   <details><summary>答案</summary>(1) 单次失败 = Information（正常）；(2) 5 次内失败 = Warning（提醒）；(3) 触发锁定 = Warning + 告警通道；(4) 自动解锁 = Information。Error 留给"系统问题"，不是"用户问题"。</details>

2. **指标设计**：你想监控"队列里待处理的死信"，用什么指标类型？  
   <details><summary>答案</summary>Gauge。死信数量会上下波动（处理掉就减，新失败就增）。不是 Counter（单调递增）。Histogram / Summary 用于度量分布（如响应时间），不适合这个场景。</details>

3. **告警调优**：上线一周后告警每天发 200 条，没人看了，怎么救？  
   <details><summary>答案</summary>(1) <b>分类</b>：紧急 / 警告 / 通知三档，紧急上 pager，警告进 Slack，通知归档；(2) <b>聚合</b>：相同告警 5 分钟内只发一次；(3) <b>抑制</b>：A 服务挂了，B/C/D 因 A 报错的告警自动抑制；(4) <b>SLO 驱动</b>：定义 99.9% 可用，错误预算用完才告警，而不是"任何失败都告警"；(5) <b>淘汰</b>：每周回顾哪些告警从没被 acted on，删掉。</details>

4. **trace 实战**：用户反馈"某次下单慢"，怎么定位？  
   <details><summary>答案</summary>(1) 让用户提供请求时间或 trace ID（前端展示 X-Trace-Id）；(2) Loki/Seq 按 TraceId 查所有日志；(3) 看 OperLog 的 ElapsedMs；(4) 如果跨 Service，看 Tempo/Jaeger 的 span 时间线；(5) DB 慢查询日志看是不是数据库慢。CP6 当前没 trace 链路，只能靠时间戳手动拼，是改进点。</details>

5. **质疑题**：DBA 抱怨 `Sys_OperLog` 表月增千万行，"为什么不用 ELK 算了"。你怎么权衡？  
   <details><summary>答案</summary>OperLog 表的角色是<b>审计</b>（合规要求保留几年），而不是查询。设计：(1) 热区 90 天放 SQL 高性能表 + index；(2) 老数据归档到 ELK 或冷存储；(3) 配合 Kafka 同步：OperLog 既进 DB（合规）又进 ELK（运营查询）。CP6 当前架构已有 Kafka → 增加 ELK consumer 即可，不冲突。</details>

---

## 🔗 延伸阅读

- [Three Pillars with Zero Answers (Distributed Systems Observability, Cindy Sridharan)](https://www.oreilly.com/library/view/distributed-systems-observability/9781492033431/)
- [Prometheus - Best Practices](https://prometheus.io/docs/practices/naming/)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/instrumentation/net/)
- [Serilog](https://serilog.net/) — .NET 结构化日志事实标准
- 项目内：`CP6.WebApi/Observability/`、`CP6.Core/Services/BridgeMetricsSnapshotProvider.cs`、`cp6.web/src/views/wms/BridgeHealthView.vue`
