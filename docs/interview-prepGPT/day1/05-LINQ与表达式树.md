# 05 · LINQ、延迟执行与表达式树

## 1. LINQ 的两层能力

LINQ 提供统一查询语法，但执行者可能完全不同：

- LINQ to Objects：对内存对象执行委托。
- LINQ provider：读取表达式树并翻译到 SQL 等目标语言。

因此相同的 `.Where(x => ...)` 外观，不代表相同执行位置。

## 2. `IEnumerable<T>` 与 `IQueryable<T>`

| 维度 | IEnumerable | IQueryable |
|---|---|---|
| 核心 | 可枚举序列 | 可组合查询描述 |
| Where 参数 | `Func<T,bool>` | `Expression<Func<T,bool>>` |
| 常见执行处 | 当前进程内存 | 提供者翻译后在数据源 |
| 风险 | 拉取过多、重复枚举 | 翻译失败、隐式昂贵 SQL |

表达式树把 Lambda 的结构保存为数据。例如 `x => x.PhysicalQty != 0` 可以被 EF 分析为列比较，而不是直接执行 C# 委托。

## 3. 延迟执行

```csharp
var query = db.Stocks.Where(x => x.PhysicalQty > 0);
// 还没有执行 SQL

var rows = await query.ToListAsync(ct);
// 这里执行
```

常见终结操作：`ToList`、`First`、`Single`、`Any`、`Count`、`Sum`。

延迟执行的好处是继续组合，风险是：

- 每次枚举可能重新查询。
- DbContext 已释放后才枚举会失败。
- 查询变量引用的外部值在执行前变化。
- 调试时看起来“有数据”，实际 SQL 尚未跑。

## 4. CP6 库存查询逐步拆解

当前核心形态：

```csharp
var q = _db.Stocks.AsNoTracking().Where(x => !x.IsDeleted);

if (!string.IsNullOrWhiteSpace(warehouseCd))
    q = q.Where(x => x.WarehouseCd == warehouseCd);

if (hasStockOnly == true)
    q = q.Where(x => x.PhysicalQty != 0);

var total = await q.CountAsync();
var items = await q
    .OrderBy(x => x.WarehouseCd)
    .ThenBy(x => x.LocationCd)
    .ThenBy(x => x.ProductCd)
    .ThenBy(x => x.LotNo)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

逐点理解：

1. `q` 的静态类型仍是 `IQueryable<Stock>`。
2. 每个 if 只增加表达式节点。
3. `CountAsync` 执行一次 `COUNT` SQL。
4. `ToListAsync` 再执行一次分页 SQL。
5. 确定性排序避免同一页记录漂移。
6. `AsNoTracking` 适合只读结果。

### 可改进边界

- `page <= 0` 或 pageSize 过大需要输入约束。
- `Contains(productCd)` 可能翻译为包含匹配，普通 B-tree 索引不易有效 seek。
- count 和 items 是两次查询，中间数据变化时可能轻微不一致。
- 深分页 `OFFSET` 成本随页码增加，可考虑 keyset pagination。

## 5. 常用算子与 SQL 心智

| LINQ | 意图 | 常见 SQL |
|---|---|---|
| `Where` | 过滤 | WHERE |
| `Select` | 投影 | SELECT 列 |
| `Join` | 内连接 | INNER JOIN |
| `GroupJoin` + DefaultIfEmpty | 左连接 | LEFT JOIN |
| `GroupBy` | 分组 | GROUP BY |
| `OrderBy/ThenBy` | 排序 | ORDER BY |
| `Skip/Take` | 分页 | OFFSET/FETCH |
| `Any` | 是否存在 | EXISTS |
| `All` | 全部满足 | NOT EXISTS 反例 |
| `SelectMany` | 展平 | CROSS APPLY/JOIN 等，取决于形态 |

不要死背翻译。查看 `ToQueryString()` 或日志验证实际 SQL。

## 6. 投影优先

只需要四列时不要加载完整实体：

```csharp
var rows = await db.Stocks
    .AsNoTracking()
    .Where(x => x.AvailableQty > 0)
    .Select(x => new
    {
        x.WarehouseCd,
        x.ProductCd,
        x.LotNo,
        x.AvailableQty
    })
    .ToListAsync(ct);
```

投影减少网络、物化和跟踪成本，也避免意外序列化导航对象。

## 7. 分组与聚合

```csharp
var summary = await db.Stocks
    .AsNoTracking()
    .GroupBy(x => x.WarehouseCd)
    .Select(g => new
    {
        Warehouse = g.Key,
        SkuCount = g.Select(x => x.ProductCd).Distinct().Count(),
        Physical = g.Sum(x => x.PhysicalQty),
        Available = g.Sum(x => x.AvailableQty)
    })
    .ToListAsync(ct);
```

深挖点：空集 `Sum`、可空列、decimal 翻译、Distinct 成本、GroupBy 是否在服务器执行，都要用版本和 SQL 验证。

## 8. `First`、`Single` 与默认值

- `First`：至少一条，取第一条；无数据抛异常。
- `FirstOrDefault`：无数据返回默认值。
- `Single`：必须恰好一条，多条也抛异常。
- `SingleOrDefault`：零或一条，多条抛异常。

若数据库唯一约束保证业务键唯一，`SingleOrDefault` 可以暴露脏数据；若只是“任取一条”，使用 First 但必须先定义排序。

## 9. `Any` vs `Count`

判断是否存在优先 `Any`，提供者通常生成 EXISTS 并可在首个匹配停止。`Count > 0` 可能要求统计更多行。最终仍以 SQL 和执行计划为准。

## 10. N+1

先查 N 个父对象，再逐个查询子对象，会产生 1+N 次往返。

解法不是一律 `Include`：

- 投影为需要的 DTO。
- `Include` 合理加载对象图。
- 显式批量查询，再按 key 组装。
- 分拆查询避免笛卡尔爆炸。

要平衡往返数与单次结果集膨胀。

## 11. 客户端求值边界

EF Core 无法翻译自定义 C# 方法时可能抛异常。不要随手在查询中间 `AsEnumerable()`，因为后续过滤会搬到内存。

正确步骤：

1. 尽量改写为可翻译表达式。
2. 先用数据库条件缩小结果。
3. 明确物化或 `AsEnumerable` 后执行必须在客户端的逻辑。
4. 评估最大数据量。

## 12. 动态表达式

泛型仓储接收：

```csharp
Expression<Func<T, bool>>? filter
```

这让调用方的条件保留为表达式树，EF 可以继续翻译。若参数改成 `Func<T,bool>`，很可能切换到内存查询或根本无法用于 `DbSet` 的数据库翻译。

## 13. 性能检查清单

- [ ] 过滤和投影是否在物化前。
- [ ] 只读查询是否禁用跟踪。
- [ ] 是否稳定排序后分页。
- [ ] 是否重复枚举/重复查询。
- [ ] 是否 N+1。
- [ ] `Contains`、函数包列等谓词是否可用索引。
- [ ] 是否拉取大文本/不需要列。
- [ ] 是否设置 pageSize 上限。
- [ ] 是否用 `ToQueryString`/日志/执行计划验证。

## 高频陷阱

1. LINQ 都在内存执行。
2. 定义查询变量时就执行 SQL。
3. `ToList` 放哪里都一样。
4. Include 越多越省查询。
5. `FirstOrDefault` 不排序也有稳定第一条。
6. `AsEnumerable` 只是改类型，不影响执行位置。

## 闭卷验收

- [ ] 解释表达式树为何能翻译 SQL。
- [ ] 逐行解释 StockController 动态查询。
- [ ] 给出 N+1 的三种修复与取舍。
- [ ] 说明深分页问题和 keyset 思路。
- [ ] 写一个分组库存汇总并查看实际 SQL。

