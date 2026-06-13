# 03 · EF Core + Dapper 混用之道

## 📍 学习目标

1. 什么时候用 EF Core，什么时候用 Dapper？为什么不只选一个？
2. EF Core 的"延迟加载"和"显式加载"分别有什么坑？
3. N+1 查询是怎么发生的，怎么发现，怎么治？
4. `AsNoTracking()` 是干嘛的？什么时候必须用？
5. CP6 的迁移策略：开发环境和生产环境分别怎么走？

---

## 🔎 真实代码切片

### EF Core 的注册（Scoped DbContext）

```csharp
// Program.cs
builder.Services.AddDbContext<CP6Context>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```

### Dapper 的注册（Scoped IDbConnection，独立连接）

```csharp
builder.Services.AddScoped<IDbConnection>(_ =>
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));
```

### EF Core 的典型用法（来自 `OrderService.CreateAsync` 风格）

```csharp
public async Task<Order> CreateAsync(OrderCreateDto dto)
{
    var order = new Order
    {
        WebOrderNo = await _doc.NextAsync("ORD"),
        CustomerCd = dto.CustomerCd,
        // ...
        Details = dto.Details.Select(d => new OrderDetail { /* ... */ }).ToList()
    };
    _context.Orders.Add(order);
    await _context.SaveChangesAsync();   // 一次 INSERT 主表 + 多次 INSERT 子表，自动事务
    return order;
}
```

### Dapper 的典型用法（聚合报表，跨多表 join）

```csharp
// DashboardService 风格
public async Task<DashboardSummary> GetSummaryAsync()
{
    const string sql = @"
        SELECT 
            (SELECT COUNT(*) FROM T_Order WHERE CreateDate >= @start) AS TodayOrders,
            (SELECT SUM(ShippedQty) FROM T_OrderDetail WHERE ShipDate >= @start) AS TodayShipped,
            (SELECT COUNT(*) FROM T_Stock WHERE AvailableQty < SafetyStock) AS LowStockCount
    ";
    return await _conn.QuerySingleAsync<DashboardSummary>(sql, new { start = DateTime.Today });
}
```

### `AsNoTracking()` 的用法

```csharp
// 只读查询，绝对要加 AsNoTracking
var list = await _context.Stocks
    .AsNoTracking()
    .Where(s => s.WarehouseCd == warehouseCd)
    .ToListAsync();
```

---

## 💡 资深视角

### EF Core vs Dapper 决策表

| 场景 | 选 EF Core | 选 Dapper | 原因 |
|---|---|---|---|
| 简单 CRUD（增删改） | ✅ | ❌ | EF 的 ChangeTracker 自动追踪 dirty，写代码极少 |
| 主子表一起增/改 | ✅ | ❌ | EF 的 navigation property + cascade 自动处理 |
| 复杂多表 join 报表 | ❌ | ✅ | EF 翻译出的 SQL 经常很丑 + 慢 |
| 海量数据导出 | ❌ | ✅ | EF 的对象映射开销大；Dapper 接近 raw ADO.NET |
| 需要原始 SQL 性能调优 | ❌ | ✅ | EF 难精确控制 hint、index、CTE |
| 需要乐观锁 / 行版本 | ✅ | ❌ | EF 自动处理 `[Timestamp]` |
| 测试（InMemory DB） | ✅ | ❌ | EF Core InMemory provider 让 Service 单测不需要真 DB |

**CP6 的做法**：写入路径用 EF Core（事务、乐观锁、navigation 都白送），仪表盘/报表用 Dapper（性能 + SQL 可控）。混用的关键是**两边连同一个连接字符串，但用各自独立的 connection**。

### EF Core 的三种"加载"

```csharp
// 1. 显式加载（Eager Loading）—— Include 提前 join 进来
var order = await _context.Orders
    .Include(o => o.Details)
    .Include(o => o.Customer)
    .FirstOrDefaultAsync(o => o.Id == id);

// 2. 延迟加载（Lazy Loading）—— 访问 navigation 时才查
// 需要 .UseLazyLoadingProxies() + virtual navigation
var order = await _context.Orders.FindAsync(id);
var detailCount = order.Details.Count;  // 这一行触发一次额外查询

// 3. 显式加载（Explicit Loading）—— 手动 Load
var order = await _context.Orders.FindAsync(id);
await _context.Entry(order).Collection(o => o.Details).LoadAsync();
```

**CP6 不用 Lazy Loading**。原因：

1. 隐式查询难追溯。出 N+1 时根本看不出哪一行触发的 SQL。
2. 需要 navigation 是 `virtual`，污染 entity 定义。
3. 序列化为 JSON 时可能触发加载，导致 API 响应慢。

行业最佳实践是 **只用 Eager Loading + AsNoTracking**，把所有查询塑造成显式。

### N+1 是什么、怎么避免

```csharp
// ❌ N+1 反例
var orders = await _context.Orders.Take(20).ToListAsync();   // 1 次
foreach (var o in orders)
    Console.WriteLine(o.Details.Count);   // 每次访问 .Details 触发 1 次 = 20 次

// 总共 21 次 SQL！

// ✅ 修复 1：Include
var orders = await _context.Orders
    .Include(o => o.Details)
    .Take(20).ToListAsync();   // 1 次 SQL，join 进来

// ✅ 修复 2：Projection（只取需要的字段）
var data = await _context.Orders
    .Take(20)
    .Select(o => new { o.WebOrderNo, DetailCount = o.Details.Count() })
    .ToListAsync();   // 1 次 SQL，DB 端聚合
```

**怎么发现 N+1**：开发期开启 EF Core 的 SQL 日志：

```csharp
o.UseSqlServer(...)
 .LogTo(Console.WriteLine, LogLevel.Information);  // 看每一条 SQL
```

或加 MiniProfiler（产线推荐），能在 UI 上看每个请求的 SQL 数。

### AsNoTracking() 的必要性

EF Core 的 ChangeTracker 会跟踪每个查询出来的实体，方便后续 `SaveChanges` 自动 detect 改动。但：

- 只读查询不需要这种追踪 → 浪费内存（每个实体 ~1KB 元数据）
- 大列表查询不加 `AsNoTracking` 直接 OOM
- 追踪会跨实体共享，可能误判"已修改"

**规则**：所有"只读"路径 100% 加 `AsNoTracking()`。CP6 的 Dashboard、报表、列表查询都遵守这个规则。

```csharp
public async Task<List<Order>> GetListAsync(int page, int size)
{
    return await _context.Orders
        .AsNoTracking()    // 读路径必加
        .OrderByDescending(o => o.CreateDate)
        .Skip((page - 1) * size)
        .Take(size)
        .ToListAsync();
}
```

### CP6 的乐观锁怎么工作

```csharp
// BaseBizEntity.cs
[Timestamp]
public byte[]? RowVersion { get; set; }
```

`[Timestamp]` 让 EF Core 在 UPDATE 时把 `RowVersion` 加到 WHERE 子句：

```sql
UPDATE T_Order
SET CustomerCd = @p0, RowVersion = @p1
WHERE Id = @p2 AND RowVersion = @oldVersion;
```

如果两个请求同时改一条记录，第二个的 `WHERE` 找不到，受影响行数 = 0，EF Core 抛 `DbUpdateConcurrencyException`。

**API 层处理**：

```csharp
try {
    await _context.SaveChangesAsync();
} catch (DbUpdateConcurrencyException) {
    return Conflict(new { code = 409, message = "数据已被他人修改，请刷新后重试" });
}
```

### 迁移策略：开发 vs 生产

```bash
# 开发期：边改 entity 边迁移
dotnet ef migrations add AddOrderShipmentTracking -p CP6.Core -s CP6.WebApi
dotnet ef database update -p CP6.Core -s CP6.WebApi

# 生产部署（CP6 的 Docker/K8s 流程）
# Program.cs 里有：
// using (var scope = app.Services.CreateScope())
//     scope.ServiceProvider.GetRequiredService<CP6Context>().Database.Migrate();
```

CP6 选择**应用启动时自动迁移**（`db.Database.Migrate()`），简单但有风险：

- **风险**：两个 Pod 同时启动 → 同时迁移 → 冲突。
- **缓解**：K8s 设 `replicas: 1` 启动第一次部署完，再 scale 到 2+。
- **更稳的做法**：用独立的 Job 跑迁移，应用启动只校验版本不迁移。

```yaml
# k8s 风格
apiVersion: batch/v1
kind: Job
metadata: { name: cp6-db-migrate }
spec:
  template:
    spec:
      containers:
        - name: migrate
          image: cp6-api:latest
          command: ["dotnet", "ef", "database", "update", ...]
      restartPolicy: Never
```

---

## ⚠️ 踩坑记录

### 坑 1：EF Core InMemory provider 不支持事务

```csharp
public async Task TestSomething() {
    using var tx = await _context.Database.BeginTransactionAsync();   // InMemory 抛 InvalidOperationException
}
```

CP6.Tests 解决方案：在 DbContext options 加 `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`。这只是"假装事务存在"，并不真的回滚 —— 单测里不能依赖事务回滚行为做断言。

### 坑 2：忘记 `await SaveChangesAsync` 

```csharp
_context.Orders.Add(order);
return order;   // ❌ 没存！返回了一个内存对象，DB 里啥也没有
```

EF Core 没有"flush on read"。Service 方法的契约要明确"是不是已经 persist 了"。CP6 的约定：Service 方法名是动词态（CreateAsync、UpdateAsync）就一定调了 SaveChangesAsync。

### 坑 3：多次 SaveChangesAsync 没事务

```csharp
// ❌ 反例：两次 SaveChanges 之间挂了，数据不一致
await _context.SaveChangesAsync();  // 写了 Order
await _context.SaveChangesAsync();  // 写 OutboundOrder 时挂了 → Order 已存
```

CP6 的做法：写 entity + 写子表用同一次 `SaveChangesAsync`（EF 自动包事务）。跨 entity 树写入用显式 `BeginTransactionAsync`。

### 坑 4：Dapper 和 EF Core 用同一个 connection 会冲突

```csharp
// ❌ 反例
public class MyService(CP6Context db, IDbConnection conn)
{
    // db 和 conn 都用同一个连接字符串，但不是同一个 SqlConnection 实例
    // db 在事务里，conn 不在 → conn 读到的是 commit 前的状态（取决于隔离级别）
}
```

CP6 的处理：Dapper 路径专走只读，且只在 Dashboard/报表这种非事务场景。如果一定要跨：用 EF Core 的 `_context.Database.GetDbConnection()` 拿到底层 connection，传给 Dapper。

### 坑 5：迁移时改了列名导致数据丢失

```bash
dotnet ef migrations add RenameColumn
# 默认生成的迁移可能是 DROP + ADD COLUMN，而不是 RENAME
```

EF Core 生成的迁移要**人工检查 + 修改**。CP6 的 Migrations 文件夹下都是手动审过的。

---

## 🧪 自检题

1. **N+1 排查**：报表接口慢，日志显示一次请求执行了 80 条 SQL，你怎么定位？  
   <details><summary>答案</summary>(1) 加 SQL 日志或 MiniProfiler 看具体哪一段触发了 80 条；(2) 大概率是循环里访问了 navigation property（即 lazy load）；(3) 改成 <code>Include</code> 或 <code>Select</code> projection；(4) 严重时直接换 Dapper 写一条聚合 SQL。</details>

2. **AsNoTracking 例外**：什么场景"读"也不能加 AsNoTracking？  
   <details><summary>答案</summary>读出来准备改的场景：<code>var order = await _context.Orders.FindAsync(id); order.Status = ...; await _context.SaveChangesAsync();</code>。如果加了 AsNoTracking，SaveChanges 不知道实体改了什么，不会生成 UPDATE。</details>

3. **乐观锁实战**：并发修改同一条订单，第二个请求该返回什么 HTTP 状态码？前端怎么处理？  
   <details><summary>答案</summary>409 Conflict。前端弹"数据已被他人修改"对话框，让用户选择"放弃 / 重新加载 / 强制覆盖"。如果业务允许 last-write-wins，那连乐观锁都不要加。</details>

4. **混用决策**：现在要写"按客户、按月份、按产品分组统计销售额"，你选 EF Core 还是 Dapper？  
   <details><summary>答案</summary>Dapper。多维分组 + 计算字段，EF Core 能翻但 SQL 难读、难调优。直接写 SQL <code>GROUP BY CustomerCd, FORMAT(OrderDate, 'yyyy-MM'), ProductCd</code> + Dapper.Query 几行搞定。EF Core 的 LINQ 这种语法翻出来一定不优雅。</details>

5. **迁移题**：生产环境跑了一个 `dotnet ef database update`，加了一列 NOT NULL 但没默认值，数据已有 100 万行，更新失败。怎么救？  
   <details><summary>答案</summary>正确流程是分三步迁移：(1) 加列时允许 NULL；(2) 写脚本 backfill 默认值；(3) 改 ALTER COLUMN NOT NULL。CP6 里 <code>AddOrderShipmentTracking</code> 迁移就是这样分步的。已经爆了的话只能手动回滚（如果迁移已部分应用），再按三步重做。</details>

---

## 🔗 延伸阅读

- [EF Core - Performance Best Practices](https://learn.microsoft.com/en-us/ef/core/performance/) — 微软官方
- [Dapper - GitHub README](https://github.com/DapperLib/Dapper) — 简洁是它的全部
- [Performance Diagnosis](https://learn.microsoft.com/en-us/ef/core/performance/performance-diagnosis) — N+1 排查
- [MiniProfiler](https://miniprofiler.com/dotnet/) — 生产可用的 SQL profiler
- 项目内：`CP6.Core/EFDbContext/CP6Context.cs`、`CP6.Core/Migrations/` 目录
