# 03 · EF Core + Dapper

## 🌱 你将学到

- "ORM"这三个字母代表什么
- EF Core 和 Dapper 是同类工具但用法不同，CP6 为什么同时用
- 看到 `_context.Orders.Where(...)` 你知道背后翻译成了什么 SQL
- 听到"N+1 问题"能解释一下是怎么发生的

---

## 🍳 生活类比：跟外国人点菜

想象你在国外餐厅，你不会当地语言。

**方案 A：自己学几句简单的**
菜单上看到"steak"，你指着说 steak。简单、直接、说什么是什么。但复杂菜（"sirloin steak, medium rare, sauce on the side"）你说不清楚。

**方案 B：找个翻译陪你**
你说中文，翻译帮你转外语。复杂菜也能描述。但翻译可能误译，每说一句话多一道工序。

方案 B = ORM（如 EF Core）。
方案 A = 直接写 SQL（如 Dapper）。

- **EF Core**：你写 `_context.Orders.Where(o => o.Status == 5)`，EF 翻译成 `SELECT * FROM T_Order WHERE Status = 5`。复杂业务（含 Include / Join / 投影）翻译得很好。但你写得不小心翻译出来的 SQL 可能很糟。
- **Dapper**：你直接写 SQL 字符串，Dapper 帮你映射到对象。完全可控，性能好，但 CRUD 累。

**CP6 同时用**：简单 CRUD 用 EF Core 省代码，复杂报表用 Dapper 控性能。

---

## 🔎 看 CP6 代码

### EF Core 注册

`Program.cs`：

```csharp
builder.Services.AddDbContext<CP6Context>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```

- `CP6Context` 是你的"数据库代理人"（继承 `DbContext`）。
- 注册成 Scoped（每请求一个），所以一个请求里多个 Service 用的是同一个 `CP6Context`，它们的改动会一起 `SaveChanges`。

### CP6Context 长什么样

打开 `D:\CP6\CP6.Core\EFDbContext\CP6Context.cs`（不用全看，看前 50 行就行）。你会看到一堆：

```csharp
public DbSet<Order> Orders { get; set; }
public DbSet<OrderDetail> OrderDetails { get; set; }
public DbSet<Stock> Stocks { get; set; }
public DbSet<StockTransaction> StockTransactions { get; set; }
// ... 几十个
```

每个 `DbSet<T>` 对应数据库里的一张表。`Orders` 就是 `T_Order` 表的代理。

### 一个简单查询

EF Core 风格：

```csharp
public async Task<List<Order>> GetRecentOrdersAsync(int days)
{
    var since = DateTime.Now.AddDays(-days);
    return await _context.Orders
        .Where(o => o.CreateDate >= since)
        .OrderByDescending(o => o.CreateDate)
        .Take(50)
        .AsNoTracking()      // ← 重要，待会解释
        .ToListAsync();
}
```

EF Core 把这段 LINQ 翻译成：

```sql
SELECT TOP 50 * FROM T_Order
WHERE CreateDate >= @since
ORDER BY CreateDate DESC;
```

漂亮。你写 C# 风格的查询，EF 帮你生成 SQL。

### 一个 Dapper 查询（聚合报表）

CP6 的 Dashboard 用 Dapper（因为多个聚合查询，用 EF Core 翻译出来会很丑）：

```csharp
public async Task<DashboardSummary> GetSummaryAsync(IDbConnection conn)
{
    const string sql = @"
        SELECT
            (SELECT COUNT(*) FROM T_Order WHERE CreateDate >= @today) AS TodayOrders,
            (SELECT SUM(ShippedQty) FROM T_OrderDetail WHERE ShipDate >= @today) AS TodayShipped,
            (SELECT COUNT(*) FROM T_Stock WHERE AvailableQty < SafetyStock) AS LowStockCount
    ";
    return await conn.QuerySingleAsync<DashboardSummary>(sql, new { today = DateTime.Today });
}
```

- SQL 你完全控制
- `@today` 是参数（防 SQL 注入，第 15 章讲）
- `QuerySingleAsync<DashboardSummary>` 自动把结果映射到 C# 对象

---

## 🤔 为什么这样

### Q1: AsNoTracking() 是啥？

EF Core 默认会"追踪"你查出来的对象。意思是：

```csharp
var order = await _context.Orders.FirstAsync(o => o.Id == id);
order.Status = 9;
await _context.SaveChangesAsync();   // EF 知道 order 被改了，生成 UPDATE
```

EF 怎么知道你改了 Status？因为它**记住了原始值**。你改了之后跟原始比对，生成 UPDATE 语句。这就是"追踪"。

**问题**：如果你只是读、不改，EF 还在记原始值 → 浪费内存。1 万行结果就要存 1 万份原始值。

**解决**：`.AsNoTracking()` 告诉 EF "我不改，别记了"。性能立刻好。

**规则**：

- 只读查询（列表、报表、Dashboard）→ 必加 `.AsNoTracking()`
- 读出来要改的 → 不要加 `.AsNoTracking()`，让 EF 追踪改动

### Q2: 什么是 N+1 问题

```csharp
// 拿 20 个订单 + 每个订单的明细数
var orders = await _context.Orders.Take(20).ToListAsync();    // 1 次 SQL
foreach (var o in orders)
{
    var count = o.Details.Count;       // 每次访问触发 1 次 SQL！
    Console.WriteLine(count);
}
// 总共 21 次 SQL（1 + 20）
```

慢的原因：每次循环都跑一次数据库（数据库在网络另一头，每次都有几十毫秒延迟）。20 次 = 几百毫秒。

**修复方法 1：Include**

```csharp
var orders = await _context.Orders
    .Include(o => o.Details)    // 一次 join 全拿来
    .Take(20)
    .ToListAsync();
// 1 次 SQL
```

**修复方法 2：投影（只取需要的字段）**

```csharp
var data = await _context.Orders
    .Take(20)
    .Select(o => new { o.WebOrderNo, DetailCount = o.Details.Count() })
    .ToListAsync();
// 1 次 SQL，DB 端算 Count
```

### Q3: EF Core 和 Dapper 谁优谁劣？

| 场景 | EF Core | Dapper |
|---|---|---|
| 简单增删改 | 占优（少代码） | 累 |
| 主子表保存 | 占优（自动级联） | 累 |
| 复杂聚合查询 | 翻出来的 SQL 可能丑 | 占优（你写 SQL） |
| 批量导出 | 慢（对象映射开销） | 占优 |
| 测试 | 占优（InMemory provider） | 没有 InMemory |
| 性能极限优化 | 难 | 占优 |

CP6 的混合策略：90% 用 EF Core 省代码 + 关键报表用 Dapper 保性能。

### Q4: SaveChangesAsync 在干嘛

```csharp
_context.Orders.Add(order);          // 还没真的写 DB，只是登记
order.Status = 5;                    // 改字段
_context.OrderDetails.Add(detail);   // 再登记
await _context.SaveChangesAsync();   // 一次性把所有改动写 DB
```

- `Add / Update / Remove` 只是在内存里登记意图
- `SaveChangesAsync` 才真的发 SQL 到数据库
- EF Core 会自动包一个事务，全成功或全失败

如果你忘了 `SaveChangesAsync`，啥也不会写进数据库。一个常见 bug。

---

## ⚠️ 容易搞错的地方

### 1. 忘 await

```csharp
var orders = _context.Orders.ToListAsync();   // ❌ 没 await，orders 是 Task<List<Order>>
```

下一行用 `orders.Count` 会报错（Task 没有 Count）。一定要 `await`：

```csharp
var orders = await _context.Orders.ToListAsync();
```

### 2. 在 foreach 里 SaveChangesAsync

```csharp
// ❌ 1000 次循环 1000 次 DB 往返，慢
foreach (var detail in details)
{
    _context.OrderDetails.Add(detail);
    await _context.SaveChangesAsync();   // ← 这里慢
}

// ✅ 循环外 SaveChanges 一次
foreach (var detail in details)
    _context.OrderDetails.Add(detail);
await _context.SaveChangesAsync();       // 一次写 1000 行
```

### 3. 同一个 DbContext 跨多个并发任务

```csharp
// ❌ DbContext 不是线程安全
await Task.WhenAll(
    _context.Orders.ToListAsync(),
    _context.Stocks.ToListAsync()
);   // 并发跑会崩溃
```

要并行就开多个 scope，各拿一个 DbContext。或者改成顺序 await。

### 4. 修改后忘 SaveChanges

```csharp
var order = await _context.Orders.FindAsync(id);
order.Status = 9;
return order;   // ❌ Status 改了，但没 SaveChanges，DB 没变
```

### 5. Dapper 里拼字符串

```csharp
// ❌ SQL 注入风险
var sql = $"SELECT * FROM T_Stock WHERE ProductCd = '{input}'";
```

如果 `input` 是 `' OR 1=1 --`，你就把整张表暴露了。第 15 章讲。

正确：

```csharp
var sql = "SELECT * FROM T_Stock WHERE ProductCd = @cd";
await conn.QueryAsync<Stock>(sql, new { cd = input });
```

---

## ✋ 动手试试

### 任务 1：看一次 EF Core 生成的 SQL

在 `Program.cs` 的 `AddDbContext` 里加一行：

```csharp
builder.Services.AddDbContext<CP6Context>(options =>
    options
        .UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
        .LogTo(Console.WriteLine, LogLevel.Information));   // ← 加这行
```

启动后端，发一次任意请求（比如查列表）。控制台会打出 EF Core 实际执行的 SQL。

看一眼这些 SQL，思考：

- 我写的 LINQ 是怎么变成 SQL 的？
- 有没有看到 N+1 影子（同一种 SQL 跑了很多次）？

**实验完别忘把 .LogTo 删掉**（生产不能开）。

### 任务 2：找一个 Service，看它有没有忘加 AsNoTracking

打开 `D:\CP6\CP6.Core\Services\` 任意一个 Service，搜 `_context.X.Where`、`_context.X.ToListAsync`。检查每一处：

- 这是读操作还是写操作？
- 有 `AsNoTracking()` 吗？没有的话该不该有？

这是真正的"读代码训练"。CP6 大部分加了，但你可能找到漏的——那就是 PR 机会。

### 任务 3：写一个简单 LINQ 试试

启动 SQL Server（docker compose up 或本地），在某个 Service 里加一个测试方法：

```csharp
public async Task<List<Order>> TestQueryAsync()
{
    return await _context.Orders
        .Where(o => o.CreateDate >= DateTime.Now.AddDays(-7))
        .OrderByDescending(o => o.CreateDate)
        .Take(10)
        .AsNoTracking()
        .ToListAsync();
}
```

跑通后改一改：去掉 Take 看会怎样、去掉 AsNoTracking 看 SQL 有什么差别。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/03-ef-and-dapper.md`](../learning/03-ef-and-dapper.md)——讲乐观锁、迁移、混用边界
- 微软官方：[EF Core 教程](https://learn.microsoft.com/en-us/ef/core/)
- Dapper：[GitHub README](https://github.com/DapperLib/Dapper) 五分钟看完
- 关键词搜索："N+1 query problem"、"EF Core AsNoTracking"
