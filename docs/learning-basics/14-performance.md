# 14 · 性能优化清单

## 🌱 你将学到

- "慢"有几种可能的原因（不是只有"代码不好"）
- 看到一个慢接口，按什么顺序排查
- CP6 已经在做的性能优化 + 还可以做的
- 缓存什么时候帮你、什么时候坑你

---

## 🍳 生活类比：餐厅出菜慢

客人投诉等了 30 分钟才上菜。可能原因：

1. **厨师慢**（代码慢）
2. **食材不够**（数据库慢）
3. **服务员忙**（线程池满）
4. **传菜路上堵**（网络慢）
5. **厨房排号**（队列拥堵）
6. **客人特别多**（QPS 高）

只看厨师不够，要从客人进门到上菜全链路检查。性能排查也一样：**从用户点击到数据返回，每一步都可能是元凶**。

---

## 🔎 性能优化分层清单

### 前端层

#### 1. 路由懒加载（CP6 已做）

```typescript
'/wms/stock': () => import('@/views/wms/StockQueryView.vue')
```

Vite 按 import 切 chunk。首屏只加载 LoginView + LayoutView，进 view 才下载。

#### 2. 列表虚拟滚动

`el-table` 渲染几千行会卡。CP6 当前用普通 table，可以换 `el-table-v2`（虚拟滚动，支持 10 万行流畅）。

#### 3. 按需引入 Element Plus

CP6 当前：

```typescript
import ElementPlus from 'element-plus'
app.use(ElementPlus)   // 全量引入
```

按需引入：

```typescript
import { ElButton, ElInput } from 'element-plus'
```

能省 200KB+ JS。

#### 4. 图片优化

webp / avif 比 png 小 30-70%。生产打包前用 `vite-imagetools` 转换。

### 网络层

- HTTPS（Cloudflare 自动）
- HTTP/2（Nginx Ingress 默认开）
- Gzip / Brotli 压缩（CP6 当前没启用，是改进点）
- CDN 缓存静态资源

### API 层

#### 1. 异步全栈

CP6 几乎全用 `async / await`。注意：

```csharp
// ❌ 阻塞
public Order Get(Guid id) => _ctx.Orders.Find(id);

// ✅ 异步
public Task<Order?> GetAsync(Guid id) => _ctx.Orders.FindAsync(id).AsTask();
```

阻塞会让线程在等 IO 时空转，并发上去线程池耗尽。

#### 2. 响应压缩

```csharp
builder.Services.AddResponseCompression(o =>
{
    o.Providers.Add<BrotliCompressionProvider>();
});
app.UseResponseCompression();
```

JSON 响应压缩率通常 80%+。CP6 没启用，可改进。

#### 3. 输出缓存（.NET 8+）

```csharp
app.MapGet("/api/dict/list", ...).CacheOutput(b => b.Expire(TimeSpan.FromMinutes(5)));
```

字典、菜单这种慢变数据可以加。

### 缓存层

CP6 用 `CacheService` 包装 `IDistributedCache`，开发期 Memory / 生产 Redis。

#### Cache-Aside 模式

```csharp
public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration)
{
    var cached = await _cache.GetStringAsync(key);
    if (cached != null) return JsonSerializer.Deserialize<T>(cached)!;
    var data = await factory();
    await _cache.SetStringAsync(key, JsonSerializer.Serialize(data), ...);
    return data;
}
```

逻辑：

1. 先看缓存有没有
2. 没有就调 factory（如查 DB）
3. 把结果写缓存
4. 返回

#### 缓存的三个坑

**击穿**：热点 key 过期瞬间大量请求打 DB。
解决：加互斥锁（SETNX），只让一个请求查 DB，其他等结果。

**雪崩**：大批 key 同时过期。
解决：TTL 加随机抖动（如基础 30 分钟 + 0~5 分钟随机）。

**穿透**：恶意请求查不存在的 key，永远跳过缓存打 DB。
解决：null 也缓存（短 TTL）。

### 数据库层

#### 1. AsNoTracking 必加（第 03 章讲过）

只读路径全部加 `.AsNoTracking()`。

#### 2. 索引

CP6 在 `CP6Context.OnModelCreating` 加了关键索引：

```csharp
modelBuilder.Entity<Stock>()
    .HasIndex(s => new { s.WarehouseCd, s.LocationCd, s.ProductCd, s.LotNo })
    .IsUnique();
```

查询字段没索引 → 全表扫描，慢。DBA 通过慢查询日志找出需要索引的字段。

#### 3. 分页：OFFSET 深翻

```csharp
// CP6 当前
.Skip((page - 1) * pageSize).Take(pageSize)
```

翻到第 1000 页 → DB 要扫 20000 行。慢。

更好做法：keyset pagination（用上一页最后一行的字段当 cursor）：

```csharp
.Where(o => o.CreateDate < lastCreateDate)
.OrderByDescending(o => o.CreateDate)
.Take(pageSize)
```

无论翻到第几页都是 O(log n)。CP6 当前是 OFFSET 方案，大表会慢。

#### 4. 批量操作（EF Core 7+）

```csharp
// 软删除 100 万行
await _context.Sys_OperLogs
    .Where(l => l.CreateDate < cutoff)
    .ExecuteDeleteAsync();   // 一条 SQL，不走 ChangeTracker
```

CP6 的 OperLog 清理服务可以这样优化。

### 后台 Worker 层

#### 节流

```csharp
// CP6 做法
var pending = await db.IntegrationEvents.Where(...).Take(50).ToListAsync();
foreach (var e in pending) await Process(e);
await Task.Delay(TimeSpan.FromSeconds(60), ct);
```

每次只处理 50 条，避免一口气吃完所有积压。

---

## 🤔 为什么这样

### Q1: 缓存能解决所有性能问题吗

不能。缓存适合"读多写少 + 容忍一定 stale"的数据：

- 字典、菜单、翻译 → 适合
- 用户的实时余额 → 不适合（要强一致）
- 库存数量 → 看场景（CP6 不缓存库存）

**陷阱**：什么都缓存会出"缓存不一致"问题。改 DB 时所有相关缓存都要清，工作量大。

### Q2: 怎么知道慢在哪

排查顺序（从离用户最近的开始）：

1. **前端 DevTools Network 标签**：哪个请求慢？是浏览器渲染慢还是后端慢？
2. **API 层 OperLog.ElapsedMs**：服务端用了多久？
3. **SQL 日志**：哪条 SQL 慢？跑了几次？
4. **DBA 慢查询日志**：DB 看哪些查询超 1 秒
5. **APM（Application Performance Monitoring）**：自动找瓶颈

通常 80% 的性能问题在 DB 层（缺索引、N+1、深翻分页）。

### Q3: 加机器解决不了的问题

- 单条 SQL 慢 → 加机器没用（多机器跑相同慢查询）
- 锁竞争 → 加机器更糟（更多并发抢同一锁）
- 内存泄漏 → 加机器只能延后崩溃

加机器解决的：CPU / 网络带宽不够。其他问题要从根本治。

### Q4: 怎么发现 N+1

第 03 章讲过。开 EF Core 的 SQL 日志，看一次请求执行了几条 SQL。如果一个列表请求执行了 1 + N 条相似的 SQL，就是 N+1。

---

## ⚠️ 容易搞错的地方

### 1. 没测就优化

```
"我猜这里慢，加个缓存"
↓
加完发现没快多少
↓
真正瓶颈在别的地方
```

**先测再优化**。用 stopwatch、profiler、APM 找出真正的瓶颈。

### 2. 缓存 TTL 过长

```csharp
_cache.SetAsync(key, data, TimeSpan.FromHours(24));   // ❌ 字典改了 24 小时不生效
```

CP6 翻译 30 分钟，改了主动 RemoveAsync 立即生效。这是平衡解。

### 3. 加索引加太多

每个索引都要占空间 + 写入时维护。盲目"每个 WHERE 字段都加索引" → DB 变慢。

正确做法：找出真正高频慢查询的字段，针对性加。

### 4. 用错缓存策略

```csharp
// ❌ 写穿透（Write-Through 是另一个意思，这里说常见误用）
await _cache.SetAsync(...);   // 先写缓存
await _db.SaveAsync(...);     // 再写 DB
// 如果 1 成功 2 失败 → 缓存里有 DB 里没有 → 数据不一致
```

CP6 的 Cache-Aside：先写 DB，再清缓存（让下次读重新加载）。简单且一致。

### 5. 缓存大对象

```csharp
_cache.SetAsync("big-list", hugeObject);  // 几 MB 的对象
```

每次读都要从 Redis 拉 + 反序列化 → 可能比直接查 DB 慢。

经验：缓存对象 < 1MB 才划算。大数据用分页 + 索引。

---

## ✋ 动手试试

### 任务 1：用 Stopwatch 测一个接口

打开任意 Service，加：

```csharp
public async Task<List<Order>> GetListAsync(...)
{
    var sw = Stopwatch.StartNew();
    var result = await _context.Orders.Take(50).ToListAsync();
    sw.Stop();
    Console.WriteLine($"GetListAsync took {sw.ElapsedMilliseconds}ms");
    return result;
}
```

调几次看耗时。再加 `.AsNoTracking()` 看耗时变化。

### 任务 2：制造一个 N+1 看 SQL 日志

打开 EF Core 日志（第 03 章动手试 1）。然后在某个 Service 加：

```csharp
public async Task DemoN1Async()
{
    var orders = await _context.Orders.Take(5).ToListAsync();
    foreach (var o in orders)
    {
        var details = o.Details.ToList();   // 触发额外查询
    }
}
```

跑一次看控制台输出。应该看到 1 + 5 = 6 条 SQL。

然后改成 Include：

```csharp
var orders = await _context.Orders.Include(o => o.Details).Take(5).ToListAsync();
```

再跑，应该只有 1 条 SQL。**亲眼看到 N+1 和它的修复**。

### 任务 3：让 CacheService 工作一次

打开任意需要缓存的接口（如 `LangController.Get`），日志加一行确认缓存命中：

```csharp
var cached = await _cache.GetStringAsync(key);
if (cached != null)
{
    Console.WriteLine($"Cache HIT: {key}");
    return ...;
}
Console.WriteLine($"Cache MISS: {key}");
```

调两次：

- 第一次 → MISS（查 DB）
- 第二次 → HIT（从缓存返回）

亲眼看到缓存生效。

### 任务 4：跑一次 Lighthouse 看前端性能

启动前端，浏览器 F12 → Lighthouse 标签 → Generate report。

看几个核心指标：

- First Contentful Paint
- Largest Contentful Paint
- Total Blocking Time

给你一份"还能优化什么"的列表。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/14-performance.md`](../learning/14-performance.md)
- 微软官方：[.NET Performance Best Practices](https://learn.microsoft.com/en-us/aspnet/core/performance/performance-best-practices)
- USE 方法论（性能分析）：[Brendan Gregg 文章](https://www.brendangregg.com/usemethod.html)
- 关键词搜索："数据库索引原理"、"N+1 查询问题"、"缓存击穿 雪崩 穿透"
