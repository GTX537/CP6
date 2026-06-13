# 14 · 性能与扩展性清单

## 📍 学习目标

读完这一章，你能识别 CP6 项目中**已经在做的性能优化**和**还没做但应该做**的位置，并能在面试里展开任何一项。

---

## 🔎 这章不是单一文件，而是横扫全栈的检查清单

按"数据流方向"梳理：**浏览器 → CDN → 反代 → API → 缓存 → DB → MQ → 后台 worker**。

---

## 💡 资深视角：性能优化清单

### A. 前端层

#### 1. 路由懒加载（CP6 已做）

```typescript
'/wms/stock': () => import('@/views/wms/StockQueryView.vue')   // 按需 chunk
```

Vite 自动按 import 切 chunk，首屏只加载 LoginView + LayoutView，进 view 才下载对应 chunk。

**面试问**：路由懒加载 chunk 划分太碎也有问题（HTTP 请求数多），怎么平衡？  
答：Vite 配 `manualChunks` 把强相关 view 合并（如所有 erp/ 一个 chunk），或用 prefetch hints 让浏览器空闲时预加载。

#### 2. axios 请求去重 / 节流

CP6 当前没做。建议：

```typescript
// composables/useApi.ts
const pending = new Map<string, Promise<any>>()
export async function dedupGet<T>(url: string): Promise<T> {
  if (pending.has(url)) return pending.get(url)
  const p = http.get<T>(url).finally(() => pending.delete(url))
  pending.set(url, p)
  return p
}
```

避免同一 URL 同时发多次（用户狂点刷新按钮）。

#### 3. 大列表虚拟滚动

`el-table-v2`（Element Plus 的虚拟表格）支持 10 万行流畅滚动。CP6 当前用 `el-table`，列表大时（如 OperLog 几千条）卡。改 `el-table-v2` 即可。

#### 4. 图片资源

CP6 截图有 `*.png`（背景、playwright 截图）。生产打包前用 `vite-imagetools` 转 WebP / AVIF。

#### 5. Element Plus 按需引入

```typescript
// ❌ CP6 当前
import ElementPlus from 'element-plus'
app.use(ElementPlus)   // 全部引入

// ✅ 按需
import { ElButton, ElInput } from 'element-plus'
```

按需引入能省 200KB+ JS。CP6 选了全量引入是为了开发期方便，生产可以切。

### B. 网络层

#### 1. HTTP/2 + Gzip/Brotli

- Nginx Ingress 默认开 HTTP/2
- Brotli 比 Gzip 小 15-20%
- 静态资源加 Cache-Control: immutable

#### 2. CDN

cp6.uk 走 Cloudflare 自带 CDN，前端静态资源被边缘缓存。

#### 3. WebSocket 心跳

SignalR 默认 15s 心跳保活，避免代理超时断链。

### C. API 层

#### 1. 异步全栈

CP6 几乎全 async / await。注意：

```csharp
// ❌ 阻塞
public Order Get(Guid id) => _ctx.Orders.Find(id);

// ✅ 异步
public Task<Order?> GetAsync(Guid id) => _ctx.Orders.FindAsync(id).AsTask();
```

阻塞线程在高并发下让线程池耗尽。

#### 2. 响应压缩

```csharp
builder.Services.AddResponseCompression(o =>
{
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
app.UseResponseCompression();
```

CP6 当前没启用。JSON 响应通常压缩率 80%+。

#### 3. 输出缓存（.NET 8+）

```csharp
app.MapGet("/api/dict/list", async () => { /* ... */ })
   .CacheOutput(b => b.Expire(TimeSpan.FromMinutes(5)));
```

字典、菜单这种慢变数据用 OutputCache，省 DB 查询。CP6 用了 `CacheService` 但封装在 Service 层。

#### 4. Pagination 不要 `OFFSET LIMIT` 深翻

```sql
-- ❌ OFFSET 10000 LIMIT 20 → DB 扫 10020 行
SELECT * FROM T_Order ORDER BY CreateDate DESC OFFSET 10000 ROWS FETCH NEXT 20 ROWS ONLY;

-- ✅ Keyset pagination：用上次最后一行的 cursor
SELECT * FROM T_Order WHERE CreateDate < @lastCreateDate ORDER BY CreateDate DESC OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY;
```

CP6 的 `GetPageListAsync` 是 OFFSET 方案，深翻慢。**改进点**：大表加 keyset。

### D. 缓存层

#### 1. CP6 的 `CacheService` 是 Cache-Aside

```csharp
public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration)
{
    var cached = await _cache.GetStringAsync(key);
    if (cached != null) return JsonSerializer.Deserialize<T>(cached)!;
    var data = await factory();
    await _cache.SetStringAsync(key, JsonSerializer.Serialize(data),
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration });
    return data;
}
```

#### 2. 缓存失效策略

| 策略 | 实现 | CP6 使用 |
|---|---|---|
| TTL | `AbsoluteExpiration` | ✅ |
| 主动清 | `RemoveAsync(key)` | ✅（改翻译时清） |
| 版本化 key | `key:v2` | ❌ |
| 标签清除 | Redis SCAN + DEL | ❌ |

#### 3. 缓存击穿 / 雪崩

- 击穿：热点 key 过期瞬间大量请求打 DB → 用互斥锁（SETNX）让只有一个请求查 DB
- 雪崩：大批 key 同时过期 → TTL 加随机抖动

CP6 当前没防击穿。如果有热点字典，可加：

```csharp
private static readonly SemaphoreSlim _sem = new(1, 1);
public async Task<T> GetOrSetWithLockAsync(...)
{
    var cached = await Get();
    if (cached != null) return cached;
    await _sem.WaitAsync();
    try
    {
        cached = await Get();   // 双检
        if (cached != null) return cached;
        // ... factory + set
    }
    finally { _sem.Release(); }
}
```

### E. 数据库层

#### 1. AsNoTracking 必加

详见第 03 章。所有只读路径必须 `.AsNoTracking()`，CP6 大部分做了，少数报表 Service 没做。

#### 2. 索引清单

CP6 的 `CP6Context.OnModelCreating` 加了关键索引：

```csharp
modelBuilder.Entity<Stock>()
    .HasIndex(s => new { s.WarehouseCd, s.LocationCd, s.ProductCd, s.LotNo })
    .IsUnique();

modelBuilder.Entity<Sys_OperLog>()
    .HasIndex(l => l.CreateDate);

modelBuilder.Entity<IntegrationEvent>()
    .HasIndex(e => new { e.Status, e.NextRetryAt });
```

**审视点**：高频查询的字段都有索引吗？慢查询日志看看。

#### 3. 批量操作

EF Core 7+ 支持 ExecuteUpdate / ExecuteDelete：

```csharp
// 软删除 100 万行
await _context.Sys_OperLogs
    .Where(l => l.CreateDate < cutoff)
    .ExecuteDeleteAsync();   // 一条 SQL，不走 ChangeTracker
```

CP6 的 `OperLogCleanupService` 应该用 ExecuteDeleteAsync。

#### 4. 连接池

`Microsoft.Data.SqlClient` 默认开连接池，关注：

- `Max Pool Size` 默认 100，高并发可能不够
- `Connection Timeout` 默认 30s

连接字符串：

```
"Server=...;Max Pool Size=200;Connection Timeout=15;"
```

#### 5. 读写分离

大型 ERP 常用 SQL Server AlwaysOn 读写分离。CP6 当前单库。改造：

```csharp
builder.Services.AddDbContext<CP6Context>(/* 主库 */);
builder.Services.AddDbContextFactory<CP6ReadOnlyContext>(/* 从库 */);

// 报表 Service 注入 ReadOnly
public class OtdReportService(IDbContextFactory<CP6ReadOnlyContext> factory) { ... }
```

#### 6. 分区表

`T_StockTransaction` / `Sys_OperLog` 月增百万 → 按月分区（partition by RANGE）。SQL Server 用 `CREATE PARTITION FUNCTION` + `PARTITION SCHEME`。CP6 当前未做。

### F. 后台任务层

#### 1. Worker 节流

```csharp
// ❌ 反例
while (true)
{
    var batch = await db.IntegrationEvents.Where(...).ToListAsync();
    foreach (var e in batch) await Process(e);
}

// ✅ CP6 做法
var pending = await db.IntegrationEvents
    .Where(...)
    .Take(50)   // 每次只处理一批
    .ToListAsync();
foreach (var e in pending) await Process(e);
await Task.Delay(TimeSpan.FromSeconds(60), ct);
```

#### 2. 并行度

如果 Worker 处理慢，可以并发：

```csharp
await Parallel.ForEachAsync(pending, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (e, ct) => {
    using var scope = _factory.CreateScope();
    await Process(scope, e, ct);
});
```

注意 DbContext 不是线程安全，每个并发任务必须有自己的 scope。

#### 3. 调度精度

CP6 的 IntegrationEventRetryWorker 用 `Task.Delay(60s)`，简单但精度差。生产可用 Quartz.NET / Hangfire。

### G. SignalR 层

详见第 08 章。关键：

- 启用 Redis backplane（多副本必备）
- 后端节流大量推送（200ms 攒一批）
- 前端 RAF 节流渲染

### H. MQ 层

#### 1. Kafka producer 配置

```csharp
new ProducerConfig
{
    BootstrapServers = "...",
    BatchSize = 16384,           // 攒到 16KB 一发
    LingerMs = 5,                // 攒不到也最多 5ms 发一次
    CompressionType = CompressionType.Snappy,
    Acks = Acks.Leader,          // 等 leader 确认就行（高吞吐）
}
```

#### 2. Consumer Offset 管理

- `enable.auto.commit = false` 手动 commit，处理完才 commit
- 失败的消息进死信 topic

#### 3. 消息去重

Kafka 至少一次语义 → 消费者必须幂等（CP6 Bridge Hook 已经幂等）。

---

## ⚠️ CP6 已知性能问题清单

| 文件 / 位置 | 问题 | 影响 | 修复建议 |
|---|---|---|---|
| `RepositoryBase.GetPageListAsync` | OFFSET 深翻 | 大表后页慢 | keyset pagination |
| `RepositoryBase.UpdateAsync` | State = Modified 全列写 | 并发覆盖 | 用 Property().IsModified |
| `BridgeMetricsSnapshotProvider` | 每次 scrape 都查 DB | Prom 抓取间隔短时压力大 | 加内存缓存 30s |
| `IntegrationEventRetryWorker` | Take(50) 串行处理 | 积压时恢复慢 | Parallel.ForEachAsync |
| `OperLogCleanupService` | 可能用循环 delete | 老数据多时慢 | ExecuteDeleteAsync 一条 SQL |
| `main.ts` 全量 import ElementPlus | bundle 大 | 首屏慢 | 按需引入 |
| `LangController.Get` | 一次性返回所有翻译 | 大字典慢 | 按 namespace 分包 |
| 前端 `el-table` | 大列表渲染卡 | OperLog 几千条慢 | el-table-v2 虚拟 |
| `appsettings` 缺 ResponseCompression | 响应未压缩 | 流量浪费 | 加 Brotli/Gzip |

---

## 🧪 自检题

1. **场景题**：你的接口在 1000 QPS 时 p99 = 3s，DB 是单点 SQL Server，怎么排查？  
   <details><summary>答案</summary>(1) Application Insights / MiniProfiler 看 SQL 数 / 耗时；(2) DBA 看 sys.dm_exec_query_stats 找慢查询；(3) 看索引使用 sys.dm_db_missing_index_details；(4) 看锁等待 sys.dm_os_wait_stats；(5) 看连接池是否打满（Max Pool Size 默认 100）；(6) APM 看是哪一层慢（API CPU / DB IO / 网络）。</details>

2. **缓存策略**：商品详情页 QPS 5000，DB 抗不住，怎么加缓存？  
   <details><summary>答案</summary>(1) 应用层 Redis cache-aside，TTL 5 分钟 + 随机抖动；(2) 防击穿：热点 key 用单飞（SingleFlight 模式）；(3) 防雪崩：TTL 加随机；(4) 防穿透：null 也缓存（短 TTL）防恶意查不存在 ID；(5) 极致 case 加 CDN 层缓存（Cloudflare cache rule）；(6) 写入时主动 Invalidate。</details>

3. **数据库分页**：表 5000 万行，用户点第 1000 页，OFFSET 20000，怎么救？  
   <details><summary>答案</summary>(1) Keyset pagination：用上一页最后一行的排序键当 cursor，<code>WHERE CreateDate &lt; @cursor ORDER BY CreateDate DESC FETCH NEXT 20</code>，O(log n) 而非 O(n)；(2) 限制最大页数（业务上没人真的看第 1000 页）；(3) 提供更精准的搜索过滤减少结果集。</details>

4. **背压**：MES 高峰每秒推 5000 条工序实绩，下游处理不过来，怎么办？  
   <details><summary>答案</summary>(1) Producer 端：批量 + 压缩；(2) Kafka 增加 partition + consumer 并行；(3) Consumer 端：批量消费（一次 ack 100 条）；(4) 限流：Token Bucket 让 producer 慢下来；(5) 降级：非关键字段不写 DB 只写日志。</details>

5. **质疑题**：架构师说"加 Redis 就行"，你怎么挑战？  
   <details><summary>答案</summary>(1) 你怎么保证一致性（写 DB 后清缓存的顺序与失败处理）；(2) Redis 单点了吗，挂了 DB 直接被冲爆；(3) 缓存命中率多少（&lt; 80% 说明数据访问模式不适合缓存）；(4) 缓存项太大反而拖累网络；(5) 缓存有 TTL，对一致性要求高的业务不适合（如库存）；(6) 真正的瓶颈在哪测过吗，可能加索引比加缓存更划算。Redis 是工具不是银弹。</details>

---

## 🔗 延伸阅读

- [.NET Performance Best Practices](https://learn.microsoft.com/en-us/aspnet/core/performance/performance-best-practices)
- [High Performance MySQL (相关思想适用所有 RDBMS)](https://www.oreilly.com/library/view/high-performance-mysql/9781492080503/)
- [Designing Data-Intensive Applications (Kleppmann)](https://dataintensive.net/) — 数据系统的圣经
- [Brendan Gregg - USE Method](https://www.brendangregg.com/usemethod.html) — 性能分析方法论
