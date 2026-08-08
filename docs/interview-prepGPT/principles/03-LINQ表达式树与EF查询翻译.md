# 03 · LINQ、表达式树与 EF 查询翻译

LINQ 的危险不是不会写 `Where`，而是不知道当前代码是在遍历内存、构建表达式，还是已经访问数据库。相同的 Lambda 外观可能执行完全不同的工作。本章建立“查询形状—执行边界—SQL—物化结果”的模型。

## 1. LINQ 的三块基础

```text
扩展方法：让 Where/Select 像集合实例方法
Lambda：把行为/表达式传给算子
迭代器：按需产生元素，支持延迟执行
```

`Enumerable.Where` 接收 `Func<T,bool>` 并在枚举时执行 .NET 代码；`Queryable.Where` 接收 `Expression<Func<T,bool>>`，记录代码结构供 provider 翻译。

## 2. `IEnumerable<T>` 的延迟执行

```csharp
var source = new List<int> { 1, 2, 3 };
var query = source.Where(x => x > 1);
source.Add(4);
Console.WriteLine(string.Join(',', query)); // 2,3,4
```

创建 query 时没有遍历；foreach/ToList 才执行。每次枚举可能重新执行，源数据和副作用都可能变化。

### 2.1 立即算子

`ToList`、`ToArray`、`Count`、`Any`、`First`、`Sum` 等需要结果时执行。`OrderBy` 返回延迟序列，但第一次枚举时通常必须缓冲并排序。

### 2.2 多次枚举

```csharp
if (query.Any())
    foreach (var x in query) ...
```

数据库 query 会执行两次；网络/流式序列甚至可能不可重复。需要稳定快照就 `ToList` 一次，但别把百万行无脑物化。

## 3. 迭代器怎样暂停

`yield return` 编译为状态机，保存当前局部和位置：

```csharp
IEnumerable<int> Positive(IEnumerable<int> source)
{
    foreach (var n in source)
        if (n > 0) yield return n;
}
```

调用方法只得到 enumerable；枚举器 `MoveNext` 才推进。异常也可能在枚举时而不是创建 query 时抛出，所以 try/catch 位置要覆盖实际枚举。

## 4. `IQueryable<T>` 是查询描述

```csharp
IQueryable<Stock> query = db.Stocks.AsNoTracking();
query = query.Where(x => x.WarehouseCd == warehouse);
query = query.OrderBy(x => x.ProductCd);
```

每一步创建新的表达式树节点。provider 在 `ToListAsync` 处访问整棵树，翻译为 SQL。

表达式树不是可执行委托本身，而是类似 AST 的数据：

```text
Call Where
├─ source DbSet<Stock>
└─ Lambda x
   └─ Equal
      ├─ x.WarehouseCd
      └─ captured warehouse parameter
```

因此 provider 只能翻译它认识的节点。

## 5. Lambda 捕获如何变成 SQL 参数

```csharp
var warehouse = "W1";
query = query.Where(x => x.WarehouseCd == warehouse);
```

EF 通常提取捕获值为 SQL 参数，避免拼接并利于计划复用。不要为了参数化手写字符串 SQL。

如果捕获可变变量并在执行前修改，最终参数可能是新值，因为执行是延迟的。需要固定就复制局部或立即物化。

## 6. 翻译失败

```csharp
bool IsInteresting(string code) => ...;
query.Where(x => IsInteresting(x.ProductCd));
```

任意 .NET 方法没有 SQL 对应，EF 可能抛“could not be translated”。选择：

- 把逻辑改成可翻译表达式。
- 映射数据库函数。
- 先用 SQL 缩小数据，再 `AsEnumerable` 进入内存处理。
- 对报表使用明确 SQL/Dapper。

千万不要在大表最前面 `AsEnumerable()`，那会把后续过滤从数据库搬到应用内存。

## 7. `AsEnumerable` 是边界标记

```csharp
var rows = db.Stocks
    .Where(x => x.WarehouseCd == warehouse) // SQL
    .Select(x => new { x.Id, x.ProductCd })  // SQL projection
    .AsEnumerable()
    .Where(x => ComplexDotNetRule(x.ProductCd)); // memory
```

只有前半已把数据缩小且字段投影足够窄时才合理。评审看到 AsEnumerable，要问它之前预计返回多少行。

## 8. 动态条件组合

```csharp
var query = db.Stocks.AsNoTracking();
if (!string.IsNullOrWhiteSpace(warehouse))
    query = query.Where(x => x.WarehouseCd == warehouse);
if (hasStockOnly)
    query = query.Where(x => x.AvailableQty > 0);
```

这是清晰、安全的动态查询。条件多且可复用时可用 specification/expression composition，但不要引入能生成任意字段/操作符的复杂 DSL 而缺少白名单。

## 9. 投影决定 SQL 列与边界

```csharp
var rows = await query.Select(x => new StockRowDto
{
    Id = x.Id,
    ProductCd = x.ProductCd,
    AvailableQty = x.AvailableQty
}).ToListAsync(ct);
```

优点：只取需要列、不暴露实体敏感字段、避免序列化导航循环。读接口优先 DTO 投影。

投影里调用不可翻译方法会失败；可以先投影原始数据，物化后再格式化显示字符串。

## 10. 元素算子的失败语义

| 算子 | 空序列 | 多条 |
|---|---|---|
| First | 抛 | 取第一 |
| FirstOrDefault | default | 取第一 |
| Single | 抛 | 抛 |
| SingleOrDefault | default | 抛 |

选择表达业务契约。按唯一键查询，数据库有唯一约束时 `SingleOrDefault` 能暴露数据损坏；普通筛选用 First 不代表唯一。

## 11. `Any` 与 `Count`

只问是否存在用 `AnyAsync`，SQL 可用 EXISTS 遇到第一行停止。`CountAsync() > 0` 表达要统计全部，优化器有时能改进但语义不如 Any 清楚。

集合已是 `ICollection<T>` 时 Count 属性 O(1)；不要机械把所有 Count 都改 Any。

## 12. 排序和分页

分页前必须稳定排序。排序键不唯一时加主键 tiebreaker。

```csharp
.OrderBy(x => x.WarehouseCd)
.ThenBy(x => x.LocationCd)
.ThenBy(x => x.ProductCd)
.ThenBy(x => x.LotNo)
.ThenBy(x => x.Id)
.Skip((page - 1) * size)
.Take(size)
```

校验 page/size，防负数、0 和过大 pageSize。深分页考虑 keyset。

## 13. GroupBy 的两个世界

内存 GroupBy 产生 `IGrouping<TKey,T>`；EF GroupBy 通常只有聚合形状容易翻译：

```csharp
var summary = await db.Stocks
    .GroupBy(x => x.WarehouseCd)
    .Select(g => new
    {
        Warehouse = g.Key,
        Physical = g.Sum(x => x.PhysicalQty),
        Available = g.Sum(x => x.AvailableQty)
    })
    .ToListAsync(ct);
```

把整个 grouping 带回客户端或复杂地取组内任意实体，翻译依 provider/版本。检查生成 SQL。

## 14. JOIN 与导航属性

导航属性让 EF 通过关系翻译 JOIN，语义通常清楚；显式 Join 适合无导航、特殊键或读模型。

LEFT JOIN 的方法语法常用 GroupJoin + DefaultIfEmpty。过滤右表条件的位置会改变语义：放 WHERE 可能把 LEFT JOIN 变成 INNER 效果。

## 15. N+1

先查 100 个订单，再循环每个订单查明细，产生 101 次往返。解决：

- 一次投影需要的数据。
- Include（警惕笛卡尔膨胀）。
- 批量按 ids 查询后字典/lookup 组装。
- split query 在大导航组合时取舍。

只看源码循环不一定确认 N+1，开启 SQL 日志统计命令数量。

## 16. Include 不是默认答案

多个 collection Include 可能把行数乘法放大。`AsSplitQuery` 降低笛卡尔爆炸但增加往返和一致性窗口。列表页通常 DTO 投影更合适。

## 17. `ToDictionary` 与重复键

`ToDictionary` 遇重复键抛异常。若业务允许一对多，用 `ToLookup` 或先 GroupBy；若业务要求唯一，异常提示数据/约束有问题，不要随意 `GroupBy().First()` 把坏数据藏掉。

## 18. 查询编译与缓存

EF 会缓存查询编译形状；参数值不同可复用。动态拼接产生大量不同表达式形状时可能增加编译成本。Compiled query 只在测量确认热点后使用，别在普通 CRUD 提前复杂化。

## 19. 查询测试分层

- 纯 LINQ 逻辑：内存单元。
- 是否能翻译：关系型 provider + `ToQueryString`。
- collation/rowversion/索引：SQL Server 集成。
- 性能：真实数据分布 + actual plan/IO。

InMemory 返回正确不代表 SQL 能翻译。

## 20. 必做实验

1. 建 query 后修改源 List，观察延迟执行。
2. 对数据库 query 调两次 Any/ToList，数 SQL 次数。
3. 写一个不可翻译自定义方法，观察异常；安全移动到 AsEnumerable 后比较读取行数。
4. 制造非唯一排序并分页，再加 Id。
5. 用循环制造 N+1，改投影并比较 SQL 数。
6. 比较 Include collection 与投影的返回行/数据量。

## 21. 闭卷问题

1. IEnumerable 与 IQueryable 的 Lambda 为什么看似相同却不同？
2. 表达式树怎样让 EF 生成 SQL？
3. AsEnumerable 为什么是高风险边界？
4. 多次枚举可能产生什么？
5. First/Single 表达什么业务契约？
6. 为什么分页最后要唯一键？
7. GroupBy 哪种形状容易翻译？
8. Include 为什么会笛卡尔膨胀？
9. 如何用证据确认 N+1？
10. InMemory LINQ 测试为什么不能证明 SQL？

