# 第 4 章 LINQ 从入门到面试手写

> 面试岗位：制造业生产管理系统开发工程师（C# + SQL + Vue，5 年经验强度）
> 本章所有标本取自 `C:\CP6` 真实生产项目（.NET 8 多租户制造业 ERP/MES/WMS），路径与方法名均可核对，**没有一行编造代码**。
> 学习方式：把自己当成第一次接触 LINQ 的新手，跟着「概念 → CP6 真实代码 → 逐行解析 → 生成的 SQL → 坑 → 面试问答」的固定节奏走一遍，最后做完章末 15 道面试问答 + 10 道手写题，你在面试白板上写 LINQ 就不会怯场。

---

## 目录

- 4.1 LINQ 是什么：为什么要把查询做进语言
- 4.2 撑起 LINQ 的三根语言支柱：扩展方法、Lambda、迭代器
- 4.3 方法语法 vs 查询语法
- 4.4 延迟执行（Deferred Execution）彻底讲透
- 4.5 IEnumerable vs IQueryable 与表达式树
- 4.6 算子全家桶逐个精讲（制造业例子）
- 4.7 实战模式库（CP6 真实标本精读）
- 4.8 LINQ 性能专题
- 4.9 面试手写题集（10 道）
- 4.10 本章面试题 15 问（详细答案）
- 4.11 手写题 10 道参考答案
- 4.12 自测清单

---

## 4.1 LINQ 是什么：为什么要把查询做进语言

### 4.1.1 一句话定义

LINQ（Language Integrated Query，语言集成查询）是 C# 3.0（2007 年）引入的一套**统一的数据查询语法**。它让你用**同一套 C# 代码**去查询内存里的集合（`List`、数组）、数据库（EF Core）、XML、甚至远程 API，而不用为每种数据源学一门新语言。

### 4.1.2 设计动机：消灭「查询语言鸿沟」

在 LINQ 出现之前，一个 .NET 开发者一天要在几种完全不同的语言之间来回切换：

```csharp
// 查数据库：手写 SQL 字符串（编译器完全看不懂，拼错了运行才炸）
string sql = "SELECT ProductCd, SUM(PhysicalQty) FROM T_WmsStock " +
             "WHERE IsDeleted = 0 GROUP BY ProductCd";
// 查内存集合：手写 for 循环（啰嗦、易错）
var dict = new Dictionary<string, decimal>();
foreach (var s in stocks)
{
    if (s.IsDeleted) continue;
    if (!dict.ContainsKey(s.ProductCd)) dict[s.ProductCd] = 0;
    dict[s.ProductCd] += s.PhysicalQty;
}
```

问题很明显：
1. **SQL 是字符串**，编译器不检查语法，也不检查列名，拼错 `PhsyicalQty` 要到运行时才发现。
2. **内存查询靠手写循环**，同样一个「分组求和」，SQL 一行搞定，`for` 循环要写十几行。
3. **两套心智模型**，SQL 是声明式（说「我要什么」），循环是命令式（说「你一步步怎么做」），大脑要不停切换。

LINQ 的设计目标就是：**把「声明式查询」这件事做进 C# 语言本身**。你只描述「我要什么」，至于「怎么做」（是翻译成 SQL 交给数据库，还是在内存里迭代），交给 LINQ 的提供者（Provider）去决定。上面那段分组求和，用 LINQ 写就是：

```csharp
var result = stocks
    .Where(s => !s.IsDeleted)
    .GroupBy(s => s.ProductCd)
    .Select(g => new { ProductCd = g.Key, Qty = g.Sum(x => x.PhysicalQty) });
```

同样这段代码，如果 `stocks` 是 `List<Stock>`，它在内存里跑；如果 `stocks` 是 `_db.Stocks`（EF Core 的 `DbSet`），它会被翻译成 SQL 发给 SQL Server。**代码一模一样，跑的地方不同**——这就是 LINQ 最惊艳的地方。

### 4.1.3 面试话术

> **面试官：LINQ 解决了什么问题？**
>
> 答：它解决了「查询语言与宿主语言割裂」的问题。以前查数据库写 SQL 字符串、查内存写 for 循环，两套语法、编译器管不了 SQL 的类型安全。LINQ 把声明式查询集成进 C#，让你用**强类型、编译期检查、可组合**的表达式描述查询意图，同一套算子既能查内存（LINQ to Objects），也能被 EF Core 翻译成 SQL（LINQ to Entities）。核心价值是**类型安全**和**可组合性**。

---

## 4.2 撑起 LINQ 的三根语言支柱：扩展方法、Lambda、迭代器

LINQ 不是魔法，它是三个更底层的 C# 语言特性拼起来的。面试官很爱问「LINQ 底层是靠什么实现的」，答对这三点直接加分。

### 4.2.1 支柱一：扩展方法（Extension Methods）

`Where`、`Select`、`GroupBy` 这些算子**不是 `List` 或 `IEnumerable` 类身上定义的方法**。它们是定义在静态类 `System.Linq.Enumerable` 里的**扩展方法**。

扩展方法的本质：给一个你无法修改源码的类型「假装」加方法。语法特征是第一个参数带 `this`：

```csharp
// 这是 .NET 源码里 Where 的简化签名
namespace System.Linq
{
    public static class Enumerable
    {
        public static IEnumerable<T> Where<T>(
            this IEnumerable<T> source,          // ← this 关键字：source 就是"点前面"那个对象
            Func<T, bool> predicate)
        {
            foreach (var item in source)
                if (predicate(item))
                    yield return item;
        }
    }
}
```

所以当你写 `stocks.Where(x => x.PhysicalQty > 0)`，编译器实际上把它翻译成 `Enumerable.Where(stocks, x => x.PhysicalQty > 0)`。`stocks` 只是被塞进第一个参数而已。

**这解释了两件事**：
1. 为什么用 LINQ 必须 `using System.Linq;`——不 `using`，编译器找不到这些扩展方法。
2. 为什么 LINQ 算子能「链式」调用——每个算子返回 `IEnumerable<T>`，返回值又能继续 `.Where().Select()`。

### 4.2.2 支柱二：Lambda 表达式

`x => x.PhysicalQty > 0` 就是 Lambda 表达式，本质是一个**匿名函数**。`=>` 读作 "goes to"。

```csharp
x => x.PhysicalQty > 0
// 等价于这个具名方法：
bool IsPositive(Stock x) { return x.PhysicalQty > 0; }
```

Lambda 被自动转换成**委托类型** `Func<Stock, bool>`（接收一个 `Stock`，返回 `bool`）。`Where` 的参数就是 `Func<T, bool>`，所以你能把 Lambda 直接传进去。

多参数、带索引、多语句的 Lambda：

```csharp
(x, i) => x.Name + i          // 两个参数
x => { var y = x * 2; return y + 1; }   // 花括号 = 多语句 Lambda，必须显式 return
() => DateTime.Now             // 无参数
```

### 4.2.3 支柱三：迭代器（Iterator / `yield return`）

`yield return` 是延迟执行的引擎。它让一个方法**「产出一个值就暂停，下次要值时从暂停处继续」**，而不是一次性算完所有值返回。

看上面 `Where` 的实现：`foreach + if + yield return item`。关键是——**调用 `Where` 的那一刻，这个 `foreach` 一次都不会执行**。只有当有人去枚举 `Where` 的返回值（比如 `foreach` 它，或 `.ToList()` 它）时，循环才真正启动，而且是「要一个给一个」。

这就是**延迟执行**的底层原理，也是下一节的主角。

> **面试话术**：LINQ 由三个语言特性支撑——扩展方法（让 `IEnumerable` 凭空多出几十个算子且能链式调用）、Lambda（把「过滤/投影逻辑」当参数传进算子）、迭代器 `yield return`（实现延迟执行，构建查询时不跑，枚举时才逐个产出）。

---

## 4.3 方法语法 vs 查询语法

LINQ 有两种写法，做同一件事。

**方法语法（Method Syntax，也叫 Fluent Syntax）**——链式调用扩展方法，本章主用：

```csharp
var result = _db.WorkOrders
    .Where(w => !w.IsDeleted && w.Status == 3)
    .OrderBy(w => w.PlanEndDate)
    .Select(w => w.WorkOrderNo);
```

**查询语法（Query Syntax）**——长得像 SQL 的关键字写法：

```csharp
var result = from w in _db.WorkOrders
             where !w.IsDeleted && w.Status == 3
             orderby w.PlanEndDate
             select w.WorkOrderNo;
```

**两者关系**：查询语法在编译时会被**改写（rewrite）成方法语法**——`from...where...select` 直接变成 `.Where().Select()`。所以它俩生成的代码完全一样，性能无差别。查询语法只是「语法糖」。

**CP6 里两种都有**。绝大多数用方法语法，但涉及 `join` 时偶尔用查询语法（因为 join 用查询语法更好读）。真实标本 `PermissionAggregator.cs`：

```csharp
// C:\CP6\CP6.Core\Services\Sys\PermissionAggregator.cs  FillActionKeysAsync
ctx.ActionKeys = (await (
        from ra in _db.Sys_RoleActions.Where(ra => roleIds.Contains(ra.RoleId))
        join m in _db.Sys_Menus on ra.MenuId equals m.MenuId
        where m.MenuKey != null
        select m.MenuKey + ":" + ra.ActionCode)
    .ToListAsync())
    .ToHashSet();
```

**为什么本章以方法语法为主？**
1. 查询语法只覆盖一部分算子——`Count`、`Sum`、`Any`、`First`、`ToList`、`Skip`、`Take` 都**没有查询语法关键字**，最后还是要退回方法语法。所以实际项目里方法语法覆盖面更广。
2. 方法语法链式可读性强，动态拼接查询（`if (条件) q = q.Where(...)`）只能用方法语法。
3. CP6 整个代码库 95% 是方法语法。

**结论**：方法语法为主力，查询语法能读懂即可（尤其 join、多层 from 的场景查询语法更清晰）。

> **面试话术**：两种语法编译后等价，查询语法是语法糖，会被改写成方法调用。我主用方法语法，因为它能覆盖全部算子（`Count`/`Any`/`Skip`/`Take` 没有查询关键字）、能动态拼接、链式可读。只有多表 join 时会考虑查询语法，可读性略好。

---

## 4.4 延迟执行（Deferred Execution）彻底讲透

这是 LINQ 面试的**头号考点**，也是 90% 生产事故的根源。务必吃透。

### 4.4.1 核心概念：构建（build）≠ 物化（materialize）

LINQ 查询分两个阶段：

- **构建查询（build）**：`.Where().Select().OrderBy()` 这一串，只是**在描述一个查询计划**，此时**一行数据都没读、一次数据库都没连**。
- **物化查询（materialize）**：当你 `.ToList()`、`.ToArray()`、`foreach`、`.Count()`、`.First()` 时，查询才**真正执行**，产出数据。

```csharp
// 阶段一：构建。这一行执行完，query 变量拿到手，但 SQL 还没发、内存还没遍历。
var query = _db.Stocks.Where(s => s.PhysicalQty > 0).Select(s => s.ProductCd);

// 阶段二：物化。这一行才真正发 SQL / 遍历内存。
var list = await query.ToListAsync();
```

### 4.4.2 `yield return` 如何实现延迟

回到 4.2.3 的 `Where` 实现：

```csharp
public static IEnumerable<T> Where<T>(this IEnumerable<T> source, Func<T, bool> predicate)
{
    foreach (var item in source)
        if (predicate(item))
            yield return item;
}
```

编译器看到 `yield return`，会把这个方法改写成一个**状态机（state machine）类**。调用 `Where(...)` 时，它**只是 new 出这个状态机对象并立刻返回**，`foreach` 一次都没跑。只有当你对返回值调 `MoveNext()`（`foreach`、`ToList` 内部都在调 `MoveNext`）时，状态机才推进一步、执行到下一个 `yield return`、吐出一个值、然后再次暂停。

「要一个，算一个，吐一个」——这就是延迟执行（也叫惰性求值 lazy evaluation）的本质。

### 4.4.3 哪些算子延迟、哪些立即（全表）

这张表面试可能直接让你背。记忆口诀：**返回 `IEnumerable`/`IQueryable` 的延迟；返回具体值或具体集合的立即。**

| 类别 | 延迟执行（返回 IEnumerable/IQueryable，构建时不跑） | 立即执行（触发物化，构建时立刻跑） |
|---|---|---|
| 过滤 | `Where` `OfType` `Distinct` `DistinctBy` | — |
| 投影 | `Select` `SelectMany` | — |
| 排序 | `OrderBy` `OrderByDescending` `ThenBy` `Reverse` | — |
| 分页 | `Skip` `Take` `SkipWhile` `TakeWhile` `Chunk` | — |
| 分组 | `GroupBy` | — |
| 连接 | `Join` `GroupJoin` | — |
| 集合 | `Union` `Intersect` `Except` `Concat` | — |
| 生成 | `Range` `Repeat` `Empty` | — |
| 元素 | — | `First` `FirstOrDefault` `Single` `SingleOrDefault` `Last` `ElementAt` |
| 聚合 | — | `Count` `Sum` `Average` `Min` `Max` `Aggregate` `LongCount` `MinBy` `MaxBy` |
| 判定 | — | `Any` `All` `Contains` `SequenceEqual` |
| 转换 | — | `ToList` `ToArray` `ToDictionary` `ToLookup` `ToHashSet` |

**关键洞察**：`GroupBy` 是延迟的（构建时不跑），但 `GroupBy` 之后你几乎总会接 `.Select().ToList()`，是最后那个 `ToList` 触发的物化。`OrderBy` 也是延迟的——排序会在物化那一刻才真正发生。

### 4.4.4 坑一：多次枚举 = 查询重复执行

延迟执行最大的陷阱：**同一个查询变量被枚举两次，查询就执行两次**。

```csharp
// query 是延迟的，还没跑
var query = _db.Stocks.Where(s => s.PhysicalQty > 0);

var count = query.Count();        // ← 第 1 次执行：发一条 SELECT COUNT(*) 的 SQL
var list  = query.ToList();       // ← 第 2 次执行：又发一条 SELECT * 的 SQL！数据库被查了两遍
foreach (var s in query) { ... }  // ← 第 3 次执行：再发一遍！
```

数据库被打了 3 次。如果 `query` 是内存里的复杂 `Where + Select`，那 CPU 也被重复消耗 3 次。

**修复**：一旦你需要多次使用结果，**先物化一次**存进 `List`，之后都用这个 `List`：

```csharp
var list = query.ToList();     // 只执行一次
var count = list.Count;        // 用 List.Count 属性，不再查库
foreach (var s in list) { ... }  // 遍历内存 List，不再查库
```

**CP6 里正确处理多次使用的真实例子**——`WmsDashboardService.GetKpiAsync`：

```csharp
// C:\CP6\CP6.Core\Services\Wms\WmsDashboardService.cs
var movedProducts = await _db.StockTransactions.AsNoTracking()
    .Where(t => t.TxnDateTime >= ninetyDaysAgo && !t.IsDeleted)
    .Select(t => t.ProductCd)
    .Distinct()
    .ToListAsync();            // ← 立刻物化成 List，只查一次库
var movedSet = new HashSet<string>(movedProducts);   // 装进 HashSet 供后续多次 Contains
```

它没有留着 `IQueryable` 反复用，而是一次 `ToListAsync` 落地，再包进 `HashSet` 供后面几十万次 `Contains` 判断。这是教科书写法。

### 4.4.5 坑二：副作用陷阱

因为延迟，`Select` 里如果写了有副作用的代码（改外部变量、写日志、发消息），**副作用发生的时机不是你写代码的地方，而是枚举的地方**，而且**枚举几次就发生几次**。

```csharp
int counter = 0;
var query = items.Select(x => { counter++; return x.Id; });   // 此刻 counter 还是 0！
// ... 中间隔了很多行 ...
var list = query.ToList();   // 到这里 counter 才变成 items.Count
var arr  = query.ToArray();  // 再枚举一次，counter 又翻倍！
```

**结论**：`Select`/`Where` 的 Lambda 里**永远只写纯函数（无副作用）**。要做有副作用的事（打日志、累加、发消息），先 `ToList()` 物化，再用 `foreach` 显式遍历。

### 4.4.6 坑三：闭包捕获变量

延迟 + 闭包联手制造经典陷阱：

```csharp
var queries = new List<IEnumerable<int>>();
for (int i = 0; i < 3; i++)
    queries.Add(numbers.Where(n => n > i));   // 闭包捕获的是变量 i 本身，不是它当时的值
// 循环结束后 i == 3。三个查询延迟执行时，读到的 i 全是 3！
```

C# 5+ 里 `foreach` 的循环变量每轮是新的，没这个问题；但 `for` 的 `i` 是同一个变量，会被三个 Lambda 共享。修复：循环内拷贝一份 `int local = i;` 让 Lambda 捕获 `local`。

> **面试话术**：延迟执行指查询在构建时不运行，直到被枚举（`ToList`/`foreach`/`Count`/`First`）才执行，底层靠 `yield return` 生成的状态机实现。三大坑：①多次枚举导致查询重复执行——需要复用就先 `ToList` 物化；②`Select` 里放副作用会随枚举次数重复触发——Lambda 只写纯函数；③`for` 循环里闭包捕获同一个变量。判断一个算子延迟还是立即，看返回类型：返回 `IEnumerable`/`IQueryable` 的延迟，返回单值或具体集合的立即。

---

## 4.5 IEnumerable vs IQueryable 与表达式树

这是区分「会用 LINQ」和「懂 LINQ」的分水岭，也是 EF Core 场景的核心。

### 4.5.1 两个接口的根本区别

| | `IEnumerable<T>` | `IQueryable<T>` |
|---|---|---|
| 命名空间 | `System.Collections.Generic` | `System.Linq` |
| 算子参数 | `Func<T, bool>`（**委托**，编译好的可执行代码） | `Expression<Func<T, bool>>`（**表达式树**，代码的数据结构） |
| 查询在哪跑 | **进程内存**里逐条迭代 | 交给 **Provider（如 EF Core）翻译**成别的语言（SQL） |
| 典型来源 | `List` `Array` `.AsEnumerable()` | `DbSet`（EF Core）`.AsQueryable()` |
| 过滤发生在 | 客户端（.NET 进程） | 服务端（数据库） |

一句话：**`IEnumerable` 把数据拉到内存再过滤；`IQueryable` 把过滤条件推到数据库，只拉回结果。**

举例说明这个差别有多致命：

```csharp
// 表 T_WmsStock 有 100 万行，只有 3 行 PhysicalQty > 0

// ❌ 用 IEnumerable：AsEnumerable 之后，100 万行全部从数据库拉进内存，才在内存里过滤
var bad = _db.Stocks.AsEnumerable().Where(s => s.PhysicalQty > 0).ToList();
//        ↑ 网络传 100 万行，内存爆炸

// ✅ 用 IQueryable：Where 被翻译成 SQL 的 WHERE，数据库只返回 3 行
var good = _db.Stocks.Where(s => s.PhysicalQty > 0).ToList();
//        ↑ SQL: SELECT * FROM T_WmsStock WHERE PhysicalQty > 0，只传 3 行
```

`_db.Stocks` 是 `DbSet<Stock>`，实现了 `IQueryable`。只要你不打断它，`Where` 就一直是 `IQueryable` 的 `Where`，条件被推给 SQL。一旦你 `.AsEnumerable()` 或 `.ToList()`，后面的 `Where` 就变成 `IEnumerable` 的 `Where`，在内存里跑。

### 4.5.2 表达式树 Expression<Func<>> 是什么

这是关键。`IQueryable.Where` 的参数是 `Expression<Func<T, bool>>` 而不是 `Func<T, bool>`。区别在于：

- `Func<Stock, bool>` 是**编译好的一段可执行代码**——你只能调用它，看不到它内部长什么样。
- `Expression<Func<Stock, bool>>` 是**这段代码的「结构树」**——它把 `s => s.PhysicalQty > 0` 拆成一棵树：根节点是「大于」比较，左子是「访问 s 的 PhysicalQty 属性」，右子是「常量 0」。

EF Core 拿到这棵树，**遍历它**，就能翻译成 `WHERE [PhysicalQty] > 0` 这段 SQL。如果参数只是编译好的 `Func`，EF Core 根本无从得知你比较的是哪个列、什么运算符——那就翻译不成 SQL 了。

**手动构建一个表达式树的小例子**（面试可能让你解释表达式树，能手写一段直接封神）：

```csharp
using System.Linq.Expressions;

// 目标：手动构建 s => s.PhysicalQty > 0 这个表达式树

// 1. 参数节点 "s"（类型 Stock）
ParameterExpression param = Expression.Parameter(typeof(Stock), "s");

// 2. 属性访问节点 "s.PhysicalQty"
MemberExpression prop = Expression.Property(param, nameof(Stock.PhysicalQty));

// 3. 常量节点 "0m"
ConstantExpression zero = Expression.Constant(0m, typeof(decimal));

// 4. 比较节点 "s.PhysicalQty > 0"
BinaryExpression greater = Expression.GreaterThan(prop, zero);

// 5. 组装成 Lambda：s => (s.PhysicalQty > 0)
Expression<Func<Stock, bool>> lambda =
    Expression.Lambda<Func<Stock, bool>>(greater, param);

// 现在可以直接喂给 IQueryable.Where，EF Core 会把它翻成 SQL
var q = _db.Stocks.Where(lambda);

// 也可以编译成普通委托，在内存里跑
Func<Stock, bool> compiled = lambda.Compile();
bool r = compiled(someStock);
```

这段代码构建出来的 `lambda`，和你直接写 `s => s.PhysicalQty > 0` 是**完全等价**的。手动构建表达式树的实际用途：**动态查询**（运行时根据用户选的字段拼条件，字段名是字符串，没法写死 Lambda 时用 `Expression.Property(param, fieldName)` 动态生成）。

### 4.5.3 EF Core 如何翻译

链条是这样的：

```
你写的 Lambda
  → 编译器生成 Expression<Func<>> 表达式树（因为 IQueryable.Where 参数是 Expression）
    → EF Core 的 Query Provider 遍历这棵树
      → 翻译成 SQL 的 AST
        → 生成 SQL 字符串发给 SQL Server
          → 数据库执行，返回结果集
            → EF Core 把结果集映射回 C# 对象
```

看 CP6 的 `WmsDashboardService.GetWarehouseValueAsync`：

```csharp
// C:\CP6\CP6.Core\Services\Wms\WmsDashboardService.cs
var stockByWh = await _db.Stocks.AsNoTracking()
    .Where(s => !s.IsDeleted && s.PhysicalQty != 0m)
    .GroupBy(s => s.WarehouseCd)
    .Select(g => new
    {
        WarehouseCd = g.Key,
        Value = g.Sum(x => x.PhysicalQty * (x.UnitPrice ?? 0m)),
        Skus = g.Select(x => x.ProductCd).Distinct().Count(),
    })
    .ToListAsync();
```

**这整段是 `IQueryable`，全部被翻译成一条 SQL**，生成的 SQL 大致长这样：

```sql
SELECT [s].[WarehouseCd],
       SUM([s].[PhysicalQty] * COALESCE([s].[UnitPrice], 0.0)) AS [Value],
       COUNT(DISTINCT [s].[ProductCd])                        AS [Skus]
FROM [T_WmsStock] AS [s]
WHERE [s].[IsDeleted] = CAST(0 AS bit) AND [s].[PhysicalQty] <> 0.0
GROUP BY [s].[WarehouseCd]
```

`Where` → `WHERE`，`GroupBy` → `GROUP BY`，`g.Sum` → `SUM`，`Distinct().Count()` → `COUNT(DISTINCT ...)`，`?? 0m` → `COALESCE`。数据库只返回**每个仓库一行的汇总**，而不是几十万行明细。这就是 `IQueryable` 的威力。

### 4.5.4 「客户端求值」翻车与 EF Core 3.0 的历史

如果你的 `Where` 里写了一个**EF Core 翻译不成 SQL** 的东西会怎样？比如调用了自定义 C# 方法：

```csharp
_db.Stocks.Where(s => MyCustomCheck(s.ProductCd)).ToList();
//                    ↑ EF Core 不知道 MyCustomCheck 翻成什么 SQL
```

- **EF Core 2.x 时代（危险）**：它会「贴心地」自动降级——把整张表拉到内存，在客户端（.NET 进程）执行 `MyCustomCheck`。这叫**客户端求值（client-side evaluation）**。后果是**悄无声息地把 100 万行全拉进内存**，线上偶发性能雪崩，还很难查，因为代码看着没问题。
- **EF Core 3.0（2019 年）的破坏性变更**：官方认为「悄悄客户端求值」危害太大，改成**直接抛异常** `InvalidOperationException: could not be translated`。宁可编译期/启动期就炸，也不让你带着定时炸弹上线。这是 EF Core 历史上最重要的行为变更之一。

所以在 EF Core 3+，你写了翻译不了的表达式，运行到那句会直接抛「could not be translated. Either rewrite the query in a form that can be translated, or switch to client evaluation explicitly by inserting a call to 'AsEnumerable'」。这个异常是在**保护你**。

### 4.5.5 AsEnumerable() 的正确使用场景

`AsEnumerable()` 是「显式切换到内存求值」的开关：它之前的算子交给 SQL，它之后的算子在内存里跑。**正确姿势：先在数据库端过滤和投影到最小，`AsEnumerable()` 落内存后，再做 SQL 干不了的计算。**

CP6 的 `PlanAchievementService.GetSummaryAsync` 是最好的真实标本——它先用 `IQueryable` 把能推给 SQL 的条件全推下去，`ToListAsync` 物化后，再做 SQL 表达不了的日期回退逻辑：

```csharp
// C:\CP6\CP6.Core\Services\Mes\PlanAchievementService.cs
var woQuery = _db.WorkOrders.AsNoTracking()
    .Where(x => !x.IsDeleted && x.Status != WorkOrderStatus.Cancelled && x.ProductionQty > 0);
// ... 若干 if 条件继续 Where（都翻译成 SQL）...
var workOrders = await woQuery.ToListAsync(ct);   // ← 此刻落内存

// 基準日フィルタ（ActualEndDate ?? PlanEndDate ?? PlanStartDate）はメモリ側で評価
// 「实际完工日 ?? 计划完工日 ?? 计划开始日」这种三级回退取基准日，SQL 里写起来很别扭，
// 落内存后用普通 C# 算，清晰又好维护
var facts = workOrders
    .Select(wo => ToFact(wo, groupBy))
    .Where(f => f != null).Select(f => f!)
    .Where(f => (from == null || f.RefDate >= from) && (toExclusive == null || f.RefDate < toExclusive))
    .ToList();
```

`GetWarehouseValueAsync` 也是同一模式——`stockByWh` 在 SQL 端聚合完落地，再在内存里 `Select` 拼上仓库名、`OrderByDescending`：

```csharp
// ToDictionaryAsync 把仓库主档拉成字典（一次查询）
var whNames = await _db.Warehouses.AsNoTracking()
    .Where(w => !w.IsDeleted)
    .ToDictionaryAsync(w => w.WarehouseCd, w => w.WarehouseName);

// stockByWh 已经是 List（内存），这里的 Select/OrderByDescending 都在内存跑
return stockByWh.Select(x => new WmsWarehouseValueDto
{
    WarehouseCd = x.WarehouseCd,
    WarehouseName = whNames.GetValueOrDefault(x.WarehouseCd),  // 字典查名，避免 join
    StockValue = x.Value,
    SkuCount = x.Skus,
}).OrderByDescending(x => x.StockValue).ToList();
```

**AsEnumerable 使用铁律**：`AsEnumerable()` / `ToList()` **之前**要把行数和列数都压到最小（`Where` 过滤 + `Select` 只取需要的列）。绝不能 `_db.Stocks.AsEnumerable().Where(...)`——那等于全表进内存。

> **面试话术**：`IEnumerable` 的算子吃 `Func`（编译好的委托），在内存迭代；`IQueryable` 的算子吃 `Expression`（表达式树，代码的数据结构），EF Core 遍历这棵树翻译成 SQL 推给数据库。表达式树是关键，没有它 EF Core 就不知道你比较的是哪个列。写了翻译不了的表达式，EF Core 3.0 起会直接抛 `could not be translated` 异常——3.0 之前是悄悄客户端求值，把全表拉进内存，非常危险，所以官方改成快速失败。`AsEnumerable` 是显式切内存的开关，正确用法是先在 SQL 端 `Where`+`Select` 把数据压到最小，落内存后再做 SQL 干不了的计算（比如多级 `??` 回退取日期）。

---

## 4.6 算子全家桶逐个精讲（制造业例子）

每个算子的例子都贴近 CP6 的库存（Stock）、工单（WorkOrder）、订单（Order）领域。

先定义几个贯穿全节的模型（简化自 CP6 真实实体）：

```csharp
class Stock { public string ProductCd; public string WarehouseCd; public string LotNo;
              public decimal PhysicalQty; public decimal? UnitPrice; public DateTime? ExpiryDate; public bool IsDeleted; }
class WorkOrder { public string WorkOrderNo; public string ProductCd; public int Status;
                 public decimal ProductionQty; public decimal CompletedQty; public DateTime? PlanEndDate; }
class Order { public string OrderNo; public string CustomerCd; public List<OrderLine> Lines; }
class OrderLine { public string ProductCd; public decimal Qty; public decimal UnitPrice; }
```

### 4.6.1 过滤：Where、OfType

**Where** — 按条件筛选，最基础的算子。

```csharp
// 有货的库存
var inStock = stocks.Where(s => s.PhysicalQty > 0);
// 多条件（&& 连接，也可以链多个 Where，效果等价）
var target = stocks.Where(s => !s.IsDeleted && s.WarehouseCd == "WH01");
```

**OfType\<T\>** — 从混合类型集合里筛出某个类型，同时完成类型转换。

```csharp
// 一个 object 列表里混着各种事件，只要 InboundEvent 类型的
IEnumerable<object> events = GetEvents();
var inbounds = events.OfType<InboundEvent>();   // 只留 InboundEvent，且元素类型变成 InboundEvent
```

`Where(x => x is InboundEvent)` 筛出来元素类型还是 `object`，`OfType` 筛完直接是目标类型，省一次强转。

### 4.6.2 投影：Select、SelectMany

**Select** — 一对一映射，把每个元素变成另一个东西（换形状）。

```csharp
// 只取工单号（一个 WorkOrder → 一个 string）
var nos = workOrders.Select(w => w.WorkOrderNo);
// 投影成 DTO（CP6 里到处都是这种写法）
var dtos = workOrders.Select(w => new { w.WorkOrderNo, Rate = w.CompletedQty / w.ProductionQty });
```

**SelectMany** — 一对多**展开并拍平（flatten）**。每个元素展开成一个集合，再把所有集合首尾相接成一个大集合。**制造业最经典场景：订单 → 明细行拍平。**

```csharp
// 每个 Order 有多条 Lines。要把所有订单的所有明细行拍成一个大的明细列表：
List<Order> orders = GetOrders();

// ❌ Select 得到的是"列表的列表" IEnumerable<List<OrderLine>>，还要再展开一层
var nested = orders.Select(o => o.Lines);         // [[line,line],[line],[line,line,line]]

// ✅ SelectMany 直接拍平成一个 IEnumerable<OrderLine>
var allLines = orders.SelectMany(o => o.Lines);   // [line,line,line,line,line,line]

// 带上父级信息的拍平（第二个参数是"结果选择器"，能同时拿到父 order 和子 line）
var flat = orders.SelectMany(
    o => o.Lines,
    (o, line) => new { o.OrderNo, o.CustomerCd, line.ProductCd, line.Qty });
// 结果：每一行明细都带着它所属订单的 OrderNo 和 CustomerCd —— 就是"订单明细拍平报表"
```

**CP6 真实 SelectMany 标本** — `InboxService.cs`，把每条流转记录里的「预期处理人 / 实际处理人 / 代办人」三个字段拍平成一个 ID 大列表，好一次性去解析人名：

```csharp
// C:\CP6\CP6.Core\Services\Oa\InboxService.cs
var ids = formTos.SelectMany(f => new[] { f.ExpectedHandlerId, f.ActualHandlerId ?? Guid.Empty, f.OnBehalfOfId ?? Guid.Empty })
    .Concat(ccs.Select(c => c.RecipientId));
```

每个 `f`（一条流转）展开成 `[3 个 Guid]` 的数组，`SelectMany` 把这些小数组全拍成一个 `Guid` 序列，再 `Concat` 抄送人的 ID。这正是「一对多展开拍平」。

### 4.6.3 排序：OrderBy / ThenBy / Descending

- `OrderBy(k)` — 按 k 升序（第一排序键）
- `OrderByDescending(k)` — 按 k 降序
- `ThenBy(k)` / `ThenByDescending(k)` — 前一个键相等时的次级排序键，可以链很多层

**注意**：**第二排序键必须用 `ThenBy`，不能再写一个 `OrderBy`**！写两个 `OrderBy` 后一个会覆盖前一个（因为 `OrderBy` 返回 `IOrderedEnumerable`，`ThenBy` 才是「在已排序基础上加次级键」）。

CP6 真实四级排序标本 — `StockController.Search`：

```csharp
// C:\CP6\CP6.WebApi\Controllers\Wms\StockController.cs
.OrderBy(x => x.WarehouseCd).ThenBy(x => x.LocationCd)
.ThenBy(x => x.ProductCd).ThenBy(x => x.LotNo)
```

先按仓库、同仓库按库位、同库位按品番、同品番按批次——四级排序，`OrderBy` 开头，后面全是 `ThenBy`。

### 4.6.4 分页：Skip / Take（+ 为什么必须先排序）

- `Skip(n)` — 跳过前 n 个
- `Take(n)` — 取 n 个

分页公式：**第 `page` 页、每页 `pageSize` 条** = `.Skip((page - 1) * pageSize).Take(pageSize)`。

CP6 `StockController.Search` 的分页：

```csharp
.Skip((page - 1) * pageSize).Take(pageSize)
```

**为什么分页前必须先 OrderBy？**——这是高频面试题。数据库表本身是**无序集合**，`SELECT` 不带 `ORDER BY` 时，返回顺序**不保证、每次可能不同**（取决于执行计划、并行度、页分裂）。如果不排序就分页：

- 第 1 页可能返回 `[A, B, C]`，第 2 页可能又把 `B` 返回一次，`D` 却从没出现——**数据错乱、漏数据、重复数据**。

所以「先 `OrderBy` 建立确定顺序，再 `Skip/Take`」是铁律。EF Core 甚至会警告你「no ORDER BY, results may be nondeterministic」。CP6 的 Search 正是先四级 `OrderBy/ThenBy` 再 `Skip/Take`。

生成的 SQL（SQL Server）：

```sql
SELECT ... FROM T_WmsStock
WHERE IsDeleted = 0 ...
ORDER BY WarehouseCd, LocationCd, ProductCd, LotNo
OFFSET (@page - 1) * @pageSize ROWS FETCH NEXT @pageSize ROWS ONLY
```

`Skip` → `OFFSET`，`Take` → `FETCH NEXT`。

### 4.6.5 元素算子全对比表（何时抛异常）

这张表是面试**必考**，务必背熟——尤其「空集合」和「多个元素」两列什么时候抛异常。

| 算子 | 取什么 | 空集合时 | 多于一个时 | 典型用途 |
|---|---|---|---|---|
| `First()` | 第一个 | **抛 InvalidOperationException** | 取第一个，不管 | 确定至少有一条 |
| `FirstOrDefault()` | 第一个 | 返回默认值（引用类型 null，值类型 0） | 取第一个，不管 | **最常用**，可能没有 |
| `Single()` | 唯一一个 | **抛异常** | **抛异常** | 断言"恰好一条"（如按主键查） |
| `SingleOrDefault()` | 唯一一个 | 返回默认值 | **抛异常** | 最多一条，可能没有 |
| `Last()` | 最后一个 | **抛异常** | 取最后一个 | 需配合 OrderBy |
| `LastOrDefault()` | 最后一个 | 返回默认值 | 取最后一个 | 同上 |
| `ElementAt(n)` | 第 n 个（0 基） | 越界**抛 ArgumentOutOfRange** | — | 按下标取 |
| `ElementAtOrDefault(n)` | 第 n 个 | 越界返回默认值 | — | 按下标取，安全 |

**记忆法**：
- 带 `OrElse...Default` 后缀 = 空集合不抛，返回默认值。
- `Single...` 系列 = 多于一个就抛（它的语义是「我断言只有一个」，多了说明数据有问题，抛给你看）。
- `First` 系列 = 多于一个不抛，就是要第一个。

**CP6 真实用法** — `StockController.History` 用 `FirstOrDefaultAsync`，因为「按 ID 查库存，可能查不到」（查不到就返回 404，不能抛异常）：

```csharp
// C:\CP6\CP6.WebApi\Controllers\Wms\StockController.cs
var s = await _db.Stocks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == stockId);
if (s == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
```

`WmsDashboardService` 用 `FirstOrDefaultAsync` 取 `GroupBy(_ => 1)` 的单行汇总（可能整表为空，所以要 `OrDefault`，随后 `stockSummary?.Value ?? 0m` 兜底 null）：

```csharp
var stockSummary = await _db.Stocks.AsNoTracking()
    .Where(s => !s.IsDeleted && s.PhysicalQty != 0m)
    .GroupBy(_ => 1)
    .Select(g => new { Value = g.Sum(...), ... })
    .FirstOrDefaultAsync();
// stockSummary 可能是 null（表全空时），用 ?. 和 ?? 兜底
TotalStockValue = stockSummary?.Value ?? 0m,
```

**面试陷阱题**：`Single()` 和 `First()` 查主键有什么区别？——`Single` 会**多查一行**来验证「是否唯一」（SQL 里 `TOP 2`，取到 2 行就抛），`First` 只 `TOP 1`。按主键查用 `SingleOrDefault` 语义更严谨（主键本就唯一，查出两条说明数据坏了，应该炸），但 `First` 性能略好。CP6 主键查询多用 `FirstOrDefault`（性能优先）。

### 4.6.6 聚合：Count / Sum / Average / Min / Max / Aggregate

```csharp
int n = workOrders.Count();                          // 计数
int active = workOrders.Count(w => w.Status == 3);   // 带条件计数（= Where().Count()，但一步）
decimal total = stocks.Sum(s => s.PhysicalQty);      // 求和
decimal avg = orderLines.Average(l => l.UnitPrice);  // 平均
decimal max = stocks.Max(s => s.PhysicalQty);        // 最大
DateTime? earliest = stocks.Min(s => s.ExpiryDate);  // 最小
```

**Aggregate** — 自定义折叠，从种子值出发逐个累积（相当于手写 `foreach` 累加）：

```csharp
// 把工单号用 " / " 拼成一个字符串（种子 = ""，每步追加）
string joined = workOrders.Aggregate("", (acc, w) => acc == "" ? w.WorkOrderNo : acc + " / " + w.WorkOrderNo);
// 实际项目里字符串拼接更该用 string.Join；Aggregate 适合复杂的自定义累积逻辑
```

**CP6 真实聚合标本** — `MesDashboardService.GetSummaryAsync`，一次分组求良品数、不良数：

```csharp
// C:\CP6\CP6.Core\Services\Mes\MesDashboardService.cs
var qty = await _db.ProductionResults.AsNoTracking()
    .Where(r => !r.IsDeleted && r.CreateDate >= today && r.CreateDate < tomorrow)
    .GroupBy(_ => 1)
    .Select(g => new { Good = g.Sum(x => x.GoodQty), Defect = g.Sum(x => x.DefectQty) })
    .FirstOrDefaultAsync();
```

`CountAsync` 直接带条件的例子（比 `Where().Count()` 更紧凑）：

```csharp
var inProgress = await _db.WorkOrders.AsNoTracking()
    .CountAsync(w => !w.IsDeleted && w.Status == 3);
```

`GroupBy(_ => 1)` 是个技巧：把整个结果集塞进**一个组**（key 恒为 1），从而在一条 SQL 里算多个聚合值。生成 SQL 是 `SELECT SUM(GoodQty), SUM(DefectQty) FROM ... WHERE ...`（没有 GROUP BY，因为只有一组）。

### 4.6.7 判定：Any / All / Contains

- `Any()` — 有没有元素（**只要看第一个就返回，极快**）
- `Any(条件)` — 有没有满足条件的元素
- `All(条件)` — 是不是全部满足
- `Contains(值)` — 包不包含某个值

```csharp
bool hasStock = stocks.Any(s => s.PhysicalQty > 0);           // 有没有一个有货
bool allDone = workOrders.All(w => w.Status == 4);            // 是不是全部完工
bool inList  = allowedCodes.Contains(order.CustomerCd);       // 客户在白名单里吗
```

**`Any()` vs `Count() > 0`——高频性能面试题**：判断「有没有」永远用 `Any()`，别用 `Count() > 0`。

- `Any()`：找到第一个满足的就立刻返回 `true`（SQL 是 `EXISTS`，或内存里迭代第一个就停）。
- `Count() > 0`：把**所有**满足的都数一遍才比较（SQL 是 `COUNT(*)`，内存里遍历整个集合）。

一个百万行的表，`Any` 可能扫一行就返回，`Count` 要扫全表。

**`Contains` 在 EF Core 里翻译成 SQL `IN`**——CP6 `PermissionAggregator` 里到处用 `roleIds.Contains(x.RoleId)`：

```csharp
// C:\CP6\CP6.Core\Services\Sys\PermissionAggregator.cs
.Where(rm => roleIds.Contains(rm.RoleId))   // 翻译成 WHERE RoleId IN (@r0, @r1, @r2)
```

`roleIds` 是内存里的 `List<int>`，`Contains` 被翻译成 SQL 的 `IN (...)`。这是「拿一批 ID 去数据库批量查」的标准写法。

### 4.6.8 分组：GroupBy 深讲

GroupBy 是报表统计的核心，面试常考「IGrouping 是什么」「怎么写 SQL 的 HAVING」。

**IGrouping\<TKey, TElement\> 是什么**：`GroupBy` 的返回值是 `IEnumerable<IGrouping<TKey, TElement>>`。每个 `IGrouping` 是「一个组」——它**既是一个 key（`g.Key`），又是这个组内所有元素的集合（可以 `g.Sum()`、`g.Count()`、`foreach g`）**。可以理解成「一个带名字（Key）的小列表」。

```csharp
// 按品番分组，每组算库存汇总
var summary = stocks
    .GroupBy(s => s.ProductCd)               // 按 ProductCd 分组
    .Select(g => new                          // g 是一个组：g.Key 是品番，g 本身是该品番的所有 Stock
    {
        ProductCd = g.Key,                    // 组的键
        TotalQty  = g.Sum(x => x.PhysicalQty),// 组内求和
        LotCount  = g.Count(),                // 组内计数
        MaxPrice  = g.Max(x => x.UnitPrice),  // 组内最大
    });
```

**复合键分组**（按多个字段分组）——用匿名对象当 key：

```csharp
// CP6 MesDashboardService：按 工序编码 + 工序名 分组
.GroupBy(p => new { p.ProcessCd, p.ProcessName })
.Select(g => new ProcessProgressDto
{
    ProcessCd  = g.Key.ProcessCd,             // 复合键的分量用 g.Key.字段 取
    ProcessName= g.Key.ProcessName,
    NotStarted = g.Count(x => x.ProcessStatus == 0),   // 组内条件计数
    InProgress = g.Count(x => x.ProcessStatus == 1 || x.ProcessStatus == 3),
    Completed  = g.Count(x => x.ProcessStatus == 2),
})
```

**SQL 的 HAVING 等价写法**——分组后再 `Where` 就是 HAVING：

```csharp
// SQL: SELECT ProductCd, SUM(Qty) FROM ... GROUP BY ProductCd HAVING SUM(Qty) > 1000
var big = stocks
    .GroupBy(s => s.ProductCd)
    .Select(g => new { ProductCd = g.Key, Qty = g.Sum(x => x.PhysicalQty) })
    .Where(x => x.Qty > 1000);   // ← 这个 Where 在分组聚合之后，等价于 HAVING
```

**区分**：`GroupBy` **之前**的 `Where` 是 SQL 的 `WHERE`（分组前过滤行）；`GroupBy` **之后**的 `Where` 是 SQL 的 `HAVING`（对聚合结果过滤）。

**CP6 完整 GroupBy 报表标本** — `MesDashboardService.GetDefectTop5Async`，缺陷 Top5 排行：

```csharp
// C:\CP6\CP6.Core\Services\Mes\MesDashboardService.cs
var rows = await _db.DefectRecords.AsNoTracking()
    .Where(d => !d.IsDeleted && d.OccurDate >= since)   // 分组前过滤 = WHERE
    .GroupBy(d => d.CategoryCd)                          // 按缺陷类别分组
    .Select(g => new
    {
        CategoryCd = g.Key,
        Count = g.Count(),                               // 每类发生次数
        Qty   = g.Sum(x => x.DefectQty),                 // 每类不良数量
    })
    .OrderByDescending(x => x.Count)                     // 按次数降序
    .Take(5)                                             // 取前 5
    .ToListAsync();
```

生成的 SQL：

```sql
SELECT TOP(5) [d].[CategoryCd], COUNT(*) AS [Count], SUM([d].[DefectQty]) AS [Qty]
FROM [T_MesDefectRecord] AS [d]
WHERE [d].[IsDeleted] = 0 AND [d].[OccurDate] >= @since
GROUP BY [d].[CategoryCd]
ORDER BY COUNT(*) DESC
```

### 4.6.9 连接：Join / GroupJoin（对应 SQL JOIN）+ 导航属性 vs 手写 Join

**Join** — 对应 SQL 的 `INNER JOIN`。四个参数：外表键、内表键、结果选择器。

```csharp
// 工单 JOIN 产品主档，取产品名（INNER JOIN，两边都匹配上才出现）
var result = workOrders.Join(
    products,                    // 内表
    w => w.ProductCd,            // 外键（外表 workOrders 的连接字段）
    p => p.ProductCd,            // 内键（内表 products 的连接字段）
    (w, p) => new { w.WorkOrderNo, p.ProductName });   // 匹配上后怎么组合
```

**CP6 真实 Join 标本** — `PermissionAggregator`，角色菜单表 JOIN 菜单表取 MenuKey：

```csharp
// C:\CP6\CP6.Core\Services\Sys\PermissionAggregator.cs
ctx.MenuKeys = (await _db.Sys_RoleMenus
        .Where(rm => roleIds.Contains(rm.RoleId))
        .Join(_db.Sys_Menus, rm => rm.MenuId, m => m.MenuId, (rm, m) => m.MenuKey)
        .Where(k => k != null)
        .ToListAsync())
    .Select(k => k!)
    .ToHashSet();
```

`Sys_RoleMenus` INNER JOIN `Sys_Menus` ON `rm.MenuId == m.MenuId`，只取菜单的 `MenuKey`。

**另一个 Join 标本**（跨系统主键对账）— `SpaceBinDriftScanner`：

```csharp
// C:\CP6\CP6.Core\Services\Space\SpaceBinDriftScanner.cs
=> await db.Space_Locations
    .Where(l => l.Status == 1 && !l.IsDeleted)
    .Join(db.WmsBins.Where(b => !b.IsActive),         // 内表先过滤"已停用的 bin"
          l => l.Id, b => b.Id,                        // 两表用同一个 GUID 主键连接
          (l, b) => new SpaceBinDrift(l.Id, l.LocationCode, b.Version))
    .ToListAsync(ct);
```

**GroupJoin** — 对应 SQL 的 `LEFT JOIN`（左表每行都保留，右表匹配不上就是空集合）。它把右表的匹配结果分组塞给左表每一行：

```csharp
// 每个客户 LEFT JOIN 他的订单（没订单的客户也要出现，订单列表为空）
var result = customers.GroupJoin(
    orders,
    c => c.CustomerCd,
    o => o.CustomerCd,
    (c, matchedOrders) => new { c.CustomerCd, OrderCount = matchedOrders.Count() });
// matchedOrders 是"该客户的订单集合"，没订单就是空集合（Count 0），而不是消失
```

要实现真正的 LEFT JOIN 展开（右表空时补 null），GroupJoin 后接 `SelectMany + DefaultIfEmpty`：

```csharp
var leftJoin = customers.GroupJoin(orders, c => c.CustomerCd, o => o.CustomerCd, (c, os) => new { c, os })
    .SelectMany(x => x.os.DefaultIfEmpty(),          // 右表空时给一个 null 占位
                (x, o) => new { x.c.CustomerCd, OrderNo = o?.OrderNo });
```

**EF Core 导航属性 vs 手写 Join——重要工程实践**：在 EF Core 里，如果实体配了**导航属性**（`WorkOrder.Product`、`Order.Lines`），你**几乎不用手写 `Join`**，直接用导航属性，EF Core 自动生成 JOIN：

```csharp
// 有导航属性时（推荐）：
var result = _db.WorkOrders
    .Include(w => w.Product)                    // 加载导航属性
    .Select(w => new { w.WorkOrderNo, w.Product.ProductName });   // 直接点进去，EF 自动 JOIN
```

**什么时候还是要手写 `Join`？**——两张表**没有配导航属性**（比如跨模块、跨系统的表，像 CP6 的 `Space_Locations` 和 `WmsBins` 是两个不同子系统的表，故意不建导航属性），或者连接条件不是外键关系时，就手写 `Join`。CP6 的 `SpaceBinDriftScanner` 正是这种「跨系统同 GUID 对账」，所以手写 `Join`。

### 4.6.10 集合：Distinct / DistinctBy / Union / Intersect / Except / Concat

```csharp
var codes = stocks.Select(s => s.ProductCd).Distinct();   // 去重（整个元素相等才算重复）
// DistinctBy（.NET 6+）：按某个键去重，保留每个键的第一个元素
var oneEach = stocks.DistinctBy(s => s.ProductCd);        // 每个品番只留一条

var a = new[] { "A", "B", "C" };
var b = new[] { "B", "C", "D" };
var union     = a.Union(b);       // 并集去重 → A B C D
var intersect = a.Intersect(b);   // 交集 → B C
var except    = a.Except(b);      // 差集（a 有 b 没有）→ A
var concat    = a.Concat(b);      // 直接拼接不去重 → A B C B C D
```

**Concat vs Union**：`Concat` 单纯首尾相接（**不去重**），`Union` 拼接后**去重**。CP6 `InboxService` 用 `Concat` 把处理人 ID 和抄送人 ID 拼一起（不需要去重，后面用字典解析，重复无害）：

```csharp
var ids = formTos.SelectMany(...).Concat(ccs.Select(c => c.RecipientId));
```

`Except` 的实战——**缺料清单**（BOM 需要的料 减去 库存有的料 = 缺的料）：

```csharp
var needed = bomLines.Select(b => b.ProductCd);
var inStock = stocks.Where(s => s.PhysicalQty > 0).Select(s => s.ProductCd);
var shortage = needed.Except(inStock);   // 需要但没库存的品番
```

### 4.6.11 转换：ToList / ToArray / ToDictionary / ToLookup / ToHashSet

这组算子**触发物化**（立即执行），把查询变成实际集合。

- `ToList()` / `ToArray()` — 变成列表 / 数组（最常用）
- `ToHashSet()` — 变成哈希集合，`Contains` O(1)，适合做「存在性判断」
- `ToDictionary(keySelector, valueSelector)` — 变字典，**key 重复直接抛异常**
- `ToLookup(keySelector, ...)` — 变成「一键对多值」的查找表，**key 重复合法**

**ToDictionary vs ToLookup——重要对比**：

```csharp
// ToDictionary：一个 key 对应一个 value。若有重复 key → 抛 ArgumentException！
var priceMap = products.ToDictionary(p => p.ProductCd, p => p.UnitPrice);
// 用途：品番→单价，一对一映射。前提：ProductCd 唯一

// ToLookup：一个 key 对应一组 value（IGrouping）。key 重复完全 OK
var linesByProduct = orderLines.ToLookup(l => l.ProductCd);
// 用途：品番→该品番的所有明细行。天然处理"一个品番多行"
var lines = linesByProduct["P001"];   // 取某品番的所有行；key 不存在返回空集合（不抛）
```

**记忆**：`ToDictionary` = 一对一（key 唯一，重复抛异常）；`ToLookup` = 一对多（key 可重复，本质是「预先分好组的字典」）。

**CP6 真实 ToDictionary 标本** — `WmsDashboardService`，仓库编码 → 仓库名（编码唯一，安全用 ToDictionary）：

```csharp
// C:\CP6\CP6.Core\Services\Wms\WmsDashboardService.cs
var whNames = await _db.Warehouses.AsNoTracking()
    .Where(w => !w.IsDeleted)
    .ToDictionaryAsync(w => w.WarehouseCd, w => w.WarehouseName);
// 之后：whNames.GetValueOrDefault(x.WarehouseCd) —— O(1) 查名，避免 join
```

**CP6 真实 ToHashSet 标本** — `PermissionAggregator`，菜单键集合（用于快速判断「有没有某个权限」）：

```csharp
ctx.MenuKeys = (await ...ToListAsync()).Select(k => k!).ToHashSet();
```

**防重复 key 崩溃的技巧**（CP6 `MesDashboardService.GetDefectTop5Async` 用到）——若不确定 key 唯一，先 `GroupBy` 再 `ToDictionary(g => g.Key, g => g.First())`：

```csharp
// 万一 CategoryCd 有重复行，直接 ToDictionary 会崩，所以先 GroupBy 保证唯一
var catMap = cats.GroupBy(c => c.CategoryCd).ToDictionary(g => g.Key, g => g.First().CategoryName);
```

### 4.6.12 生成：Range / Repeat / Empty

```csharp
var nums = Enumerable.Range(1, 5);        // 1,2,3,4,5（起点 1，个数 5）
var zeros = Enumerable.Repeat(0m, 12);    // 12 个 0（生成 12 个月的空槽位）
var none = Enumerable.Empty<Stock>();     // 空序列（比 new List 更省，做默认返回值）
```

`Range` 的实战——**生成连续日期序列填补报表空洞**。CP6 `WmsDashboardService.GetTransactionTrendAsync` 用 `for` 循环生成日期（也可以用 `Range`），把数据库里「没有交易的日子」补成 0，让趋势图不断线：

```csharp
// C:\CP6\CP6.Core\Services\Wms\WmsDashboardService.cs
for (int i = 0; i < days; i++)
{
    var d = fromDate.AddDays(i);
    var inQty = rawData.Where(r => r.Date == d && r.TxnType == WmsTxnType.IN).Sum(r => r.Qty);
    // ... 数据库没这天就 Sum 出 0，图表不断线
}
```

等价的 `Range` 写法：`Enumerable.Range(0, days).Select(i => fromDate.AddDays(i))`。

### 4.6.13 .NET 6+ 新算子：Chunk / MinBy / MaxBy / DistinctBy

这几个是相对新的算子，面试问「你知道 .NET 6 新增哪些 LINQ 算子」能答上就是加分项。

- **`MinBy` / `MaxBy`** — 取「某个键最小/最大」的**那个元素**（不是那个键值）。

```csharp
// 取库存金额最大的那一条 Stock（返回 Stock 对象，不是金额）
Stock richest = stocks.MaxBy(s => s.PhysicalQty * (s.UnitPrice ?? 0m));
// 对比：Max 只给你最大的金额数字，MaxBy 给你产生这个最大值的整条记录
decimal maxValue = stocks.Max(s => s.PhysicalQty * (s.UnitPrice ?? 0m));  // 只有数字
```

`MinBy/MaxBy` 是「去重取最新一条」模式的利器（见 4.7.6）。.NET 6 之前只能 `OrderByDescending(...).First()`。

- **`DistinctBy`** — 按键去重（4.6.10 已讲），.NET 6 前要 `GroupBy(key).Select(g => g.First())`。

- **`Chunk(n)`** — 把序列切成每块 n 个的小块，**批处理神器**：

```csharp
// 3000 个 ID 要调外部接口，接口一次最多收 500 个 → 切成 6 批
foreach (var batch in productIds.Chunk(500))
{
    await CallExternalApi(batch);   // 每批 500 个（最后一批可能不足 500）
}
```

CP6 的批量场景（如逐租户批量转单、批量外呼）非常适合 `Chunk` 控制批量大小，避免一次性把几千个参数塞进一条 SQL 的 `IN (...)`（SQL Server 参数上限 2100）。

> 注：`MinBy/MaxBy/DistinctBy/Chunk` 是 **LINQ to Objects（内存）** 算子。在 EF Core 里对 `IQueryable` 用它们大多翻译不了 SQL，需先 `ToList()` 落内存再用。

---

## 4.7 实战模式库（CP6 真实标本精读）

这一节是「拿来即用」的六大模式，每个都是面试和工作都要会的套路。

### 4.7.1 模式一：动态多条件查询（StockController.Search 逐行精读）

**场景**：一个查询接口，用户可能填 0 到 N 个筛选条件（仓库、库位、品番、批次…），填了才过滤，没填就忽略。这是**每个后端都要写**的查询接口标准骨架。

CP6 教科书标本，逐行精读：

```csharp
// C:\CP6\CP6.WebApi\Controllers\Wms\StockController.cs  Search 方法
public async Task<IActionResult> Search(
    [FromQuery] string? warehouseCd, [FromQuery] string? locationCd,
    [FromQuery] string? productCd,   [FromQuery] string? lotNo,
    [FromQuery] string? ownerType,   [FromQuery] string? ownerCd,
    [FromQuery] bool? hasStockOnly,
    [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
{
    // ① 起点：拿到 IQueryable，先加最基础的固定条件（未软删）。
    //    AsNoTracking 告诉 EF 不做变更追踪 —— 只读查询用它，省内存省 CPU。
    //    注意此刻 SQL 没发！q 只是个"查询计划"。
    var q = _db.Stocks.AsNoTracking().Where(x => !x.IsDeleted);

    // ② 动态拼条件：每个参数"有值才追加 Where"。
    //    因为 Where 返回新的 IQueryable，q = q.Where(...) 是在原计划上叠一层条件。
    //    延迟执行让这种"分步构建"成为可能 —— 叠再多层也不查库。
    if (!string.IsNullOrWhiteSpace(warehouseCd)) q = q.Where(x => x.WarehouseCd == warehouseCd);
    if (!string.IsNullOrWhiteSpace(locationCd))  q = q.Where(x => x.LocationCd == locationCd);
    if (!string.IsNullOrWhiteSpace(productCd))   q = q.Where(x => x.ProductCd.Contains(productCd)); // 模糊查 → LIKE '%..%'
    if (!string.IsNullOrWhiteSpace(lotNo))       q = q.Where(x => x.LotNo == lotNo);
    if (!string.IsNullOrWhiteSpace(ownerType))   q = q.Where(x => x.OwnerType == ownerType);
    if (!string.IsNullOrWhiteSpace(ownerCd))     q = q.Where(x => x.OwnerCd == ownerCd);
    if (hasStockOnly == true)                    q = q.Where(x => x.PhysicalQty != 0);

    // ③ 第一次物化：算总数（分页要用）。这里发第 1 条 SQL：SELECT COUNT(*)。
    //    注意：CountAsync 用的是"加完所有条件的 q"，所以总数是过滤后的。
    var total = await q.CountAsync();

    // ④ 第二次物化：排序 + 分页 + 取数据。发第 2 条 SQL。
    //    先四级 OrderBy/ThenBy 建立确定顺序（分页前必须排序！），再 Skip/Take。
    var items = await q
        .OrderBy(x => x.WarehouseCd).ThenBy(x => x.LocationCd)
        .ThenBy(x => x.ProductCd).ThenBy(x => x.LotNo)
        .Skip((page - 1) * pageSize).Take(pageSize)
        .ToListAsync();

    return Ok(new { code = 0, message = "OK", data = new { total, page, pageSize, items } });
}
```

**四个必须讲清的点**（面试常追问）：
1. **为什么能 `q = q.Where(...)` 一层层叠？** 因为 `Where` 返回新 `IQueryable`，且延迟执行——叠的时候不查库，最后物化时 EF 把所有条件合成**一条** SQL 的 `WHERE ... AND ...`。
2. **总共发几条 SQL？** 两条：`CountAsync` 一条 `COUNT(*)`，`ToListAsync` 一条取数据。`q` 被枚举了两次，但因为一次是 Count 一次是取列表，是有意为之的，不是 bug。
3. **`Contains` 翻译成什么？** `x.ProductCd.Contains(productCd)` → SQL `WHERE ProductCd LIKE '%' + @productCd + '%'`（模糊查询）。而 `==` 翻译成精确 `=`。
4. **`AsNoTracking` 为什么加？** 只读查询不需要 EF 的变更追踪快照，加它省内存、提速。查询接口一律加。

生成的第二条 SQL：

```sql
SELECT * FROM T_WmsStock
WHERE IsDeleted = 0
  AND WarehouseCd = @warehouseCd        -- 只有填了的条件才出现
  AND ProductCd LIKE '%' + @productCd + '%'
ORDER BY WarehouseCd, LocationCd, ProductCd, LotNo
OFFSET (@page-1)*@pageSize ROWS FETCH NEXT @pageSize ROWS ONLY
```

### 4.7.2 模式二：分页封装

把「查总数 + 排序分页」封装成通用方法，全项目复用：

```csharp
public record PagedResult<T>(long Total, int Page, int PageSize, List<T> Items);

public static async Task<PagedResult<T>> ToPagedAsync<T>(
    this IQueryable<T> query, int page, int pageSize)
{
    if (page < 1) page = 1;
    if (pageSize < 1) pageSize = 50;
    var total = await query.CountAsync();               // 第 1 条 SQL
    var items = await query
        .Skip((page - 1) * pageSize).Take(pageSize)     // 第 2 条 SQL
        .ToListAsync();
    return new PagedResult<T>(total, page, pageSize, items);
}
// 用法（注意：排序必须在调用前完成，否则 Skip/Take 结果不确定）
var result = await _db.Stocks.AsNoTracking().Where(x => !x.IsDeleted)
    .OrderBy(x => x.WarehouseCd)          // ← 排序在封装外面先做好
    .ToPagedAsync(page, pageSize);
```

### 4.7.3 模式三：分组统计报表（GroupBy + Sum/Count + OrderBy）

CP6 `MesDashboardService.GetDefectTop5Async` 是标准报表三段式：**过滤 → 分组聚合 → 排序取 TopN**（4.6.8 已贴代码）。通用骨架：

```csharp
var report = await _db.源表.AsNoTracking()
    .Where(x => 过滤条件)                       // 1. WHERE：先把无关行滤掉
    .GroupBy(x => x.分组键)                      // 2. GROUP BY：按维度分组
    .Select(g => new 报表行 {                    // 3. 每组算聚合值
        维度 = g.Key,
        计数 = g.Count(),
        金额 = g.Sum(x => x.Amount),
        均值 = g.Average(x => x.Val),
    })
    .OrderByDescending(r => r.金额)             // 4. 排序
    .Take(N)                                     // 5. TopN
    .ToListAsync();
```

### 4.7.4 模式四：两表对账（ToLookup/ToDictionary，O(n²) → O(n)）

**这是性能面试的经典题**：两个列表要按 key 配对（对账、匹配），朴素写法是双重循环 O(n²)，正确写法是「一张表转字典/Lookup，另一张表遍历时 O(1) 查」，整体 O(n)。

**朴素错误写法（O(n²)，1 万 × 1 万 = 1 亿次比较，卡死）**：

```csharp
// ❌ 对每个 order，在 payments 里线性找匹配 —— 嵌套循环
foreach (var order in orders)              // n 次
{
    var pay = payments.FirstOrDefault(p => p.OrderNo == order.OrderNo);  // 每次内部又 m 次
    // ... O(n×m)
}
```

**正确写法（O(n)，用字典/Lookup 把内层查找降成 O(1)）**：

```csharp
// ✅ 一次性把 payments 建成"订单号 → 付款"的查找表
var payByOrder = payments.ToLookup(p => p.OrderNo);   // O(m) 建表；一个订单可能多笔付款，用 Lookup

foreach (var order in orders)                          // O(n)
{
    var pays = payByOrder[order.OrderNo];              // O(1) 查（key 不存在返回空集合，不抛）
    var paid = pays.Sum(p => p.Amount);
    var diff = order.Amount - paid;                    // 对账差额
    // ...
}
// 总复杂度 O(n + m)，从 1 亿次降到 2 万次
```

**CP6 真实同款优化** — `WmsDashboardService.GetKpiAsync` 判断「滞留品」（90 天没动过的库存）。如果用嵌套循环，对每个 SKU 去交易表里找有没有动过，就是 O(SKU数 × 交易数)。CP6 的做法是**先把「动过的品番」建成 HashSet，再 O(1) 判断**：

```csharp
// C:\CP6\CP6.Core\Services\Wms\WmsDashboardService.cs
var movedProducts = await _db.StockTransactions.AsNoTracking()
    .Where(t => t.TxnDateTime >= ninetyDaysAgo && !t.IsDeleted)
    .Select(t => t.ProductCd).Distinct().ToListAsync();
var movedSet = new HashSet<string>(movedProducts);   // 动过的品番集合，Contains O(1)

var allActiveSkus = await _db.Stocks.AsNoTracking()
    .Where(s => !s.IsDeleted && s.PhysicalQty > 0m)
    .Select(s => s.ProductCd).Distinct().ToListAsync();

// 对每个活跃 SKU，O(1) 判断它在不在"动过"集合里 —— 整体 O(n)，不是 O(n×m)
var stagnantCount = allActiveSkus.Count(p => !movedSet.Contains(p));
```

`HashSet.Contains` 是 O(1)。如果这里用 `movedProducts.Contains(p)`（List 的 Contains 是 O(m) 线性扫描），整体就退化成 O(n×m)。**用对数据结构（HashSet/Dictionary/Lookup）是把 O(n²) 降到 O(n) 的关键**。`MesDashboardService.GetDailyTrendAsync` 也是同款——`raw.ToDictionary(x => x.Date, ...)` 后 `for` 循环里 `map.TryGetValue(d, out var v)` O(1) 填每一天。

### 4.7.5 模式五：层级数据组装（父子菜单树）

**场景**：数据库里菜单是扁平的（每行有 `Id` 和 `ParentId`），前端要树形结构。用 `ToLookup(ParentId)` 一次建「父→子列表」查找表，再递归组装：

```csharp
class MenuNode { public int Id; public int? ParentId; public string Name; public List<MenuNode> Children = new(); }

List<MenuNode> BuildTree(List<MenuNode> flat)
{
    // ① 一次性把所有节点按 ParentId 分组 —— O(n)
    var byParent = flat.ToLookup(m => m.ParentId);

    // ② 递归函数：给一个 parentId，组装它的所有子树
    List<MenuNode> Build(int? parentId)
    {
        return byParent[parentId]              // O(1) 拿到直接子节点（无匹配返回空集合）
            .OrderBy(m => m.Id)
            .Select(m => { m.Children = Build(m.Id); return m; })   // 递归组装孙节点
            .ToList();
    }
    return Build(null);   // 根节点的 ParentId 为 null
}
```

关键：`ToLookup(ParentId)` 把「找某节点的所有孩子」从 O(n) 遍历降成 O(1) 查表，整棵树 O(n) 建成。CP6 的菜单/部门树（`Sys_Menus`、`Sys_Depts` 带 `Path`/`ParentId`）就是这种结构。

### 4.7.6 模式六：去重取最新一条（GroupBy + Select First / MaxBy）

**场景**：每个品番有多条价格历史，只要每个品番**最新的那一条**。这是「分组内取代表」的经典模式（SQL 里要用窗口函数 `ROW_NUMBER`，LINQ 一行搞定）。

```csharp
// 每个 ProductCd 取 EffectiveDate 最新的一条
var latest = priceHistory
    .GroupBy(p => p.ProductCd)                              // 按品番分组
    .Select(g => g.OrderByDescending(x => x.EffectiveDate)  // 组内按日期降序
                  .First());                                // 取第一条 = 最新
```

.NET 6+ 更简洁的 `MaxBy`：

```csharp
var latest = priceHistory
    .GroupBy(p => p.ProductCd)
    .Select(g => g.MaxBy(x => x.EffectiveDate));   // 直接取组内 EffectiveDate 最大的那条
```

CP6 `MesDashboardService.GetRecentCompletedAsync` 是「取最新 N 条」的同族写法（全局按完工时间降序取 Top10，用 `??` 做时间字段回退）：

```csharp
// C:\CP6\CP6.Core\Services\Mes\MesDashboardService.cs
.OrderByDescending(w => w.ActualEndDate ?? w.ModifyDate ?? w.CreateDate)
.Take(top)
```

> **EF Core 注意**：`GroupBy(...).Select(g => g.OrderBy().First())` 这种「分组内再排序取首」在 EF Core 里翻译成 SQL 可能失败或低效（EF 对 GroupBy 后取元素的翻译能力有限）。稳妥做法：要么用 `ROW_NUMBER` 的等价写法，要么先把过滤后的小数据集 `ToList()` 落内存再分组取首。

---

## 4.8 LINQ 性能专题

面试到 5 年经验强度，性能题几乎必问。

### 4.8.1 投影只取需要的列（别 SELECT *）

**坏**：`_db.Orders.ToList()` → SQL `SELECT *`，把每行所有列（可能几十列、含大 text 字段）全拉回。
**好**：`_db.Orders.Select(o => new { o.OrderNo, o.Amount }).ToList()` → SQL 只 `SELECT OrderNo, Amount`，网络传输和内存都小得多。

CP6 到处是这种精准投影：`WmsDashboardService` 里 `.Select(t => t.ProductCd)` 只取一列去做 Distinct，而不是拉整行。**规则：查询结果只 `Select` 前端/业务真正需要的字段。**

### 4.8.2 Any() 优于 Count() > 0

判断「存在性」用 `Any()`（找到一个就停 / SQL `EXISTS`），不要用 `Count() > 0`（数完所有 / SQL `COUNT(*)`）。详见 4.6.7。同理判断「不存在」用 `!Any(...)` 而非 `Count() == 0`。

### 4.8.3 避免循环内查询（N+1 问题）

**最致命的性能反模式**——循环里每轮发一次数据库查询：

```csharp
// ❌ N+1：1 次查订单 + 对每个订单 1 次查客户 = 1 + N 条 SQL
var orders = await _db.Orders.ToListAsync();
foreach (var o in orders)
{
    var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Cd == o.CustomerCd);  // 循环内查库！
    o.CustomerName = customer?.Name;
}
```

100 个订单 = 101 条 SQL，往返延迟叠加，慢到爆。**修复：循环外一次性把需要的数据查回来建字典，循环内 O(1) 查字典**（就是 4.7.4 的对账模式）：

```csharp
// ✅ 2 条 SQL 搞定
var orders = await _db.Orders.ToListAsync();
var cds = orders.Select(o => o.CustomerCd).Distinct().ToList();
var custMap = await _db.Customers.Where(c => cds.Contains(c.Cd))   // IN 批量查，1 条 SQL
    .ToDictionaryAsync(c => c.Cd, c => c.Name);
foreach (var o in orders)
    o.CustomerName = custMap.GetValueOrDefault(o.CustomerCd);       // O(1) 内存查
```

EF Core 场景更简单：用 `Include` 导航属性让 EF 一条 JOIN 搞定，或用投影 `Select`。CP6 `WmsDashboardService.GetWarehouseValueAsync` 正是「先 `ToDictionaryAsync` 拉主档，再内存查名」避免 N+1。

### 4.8.4 内存 LINQ 的复杂度分析

对**内存集合**（`List`）用 LINQ，要有复杂度意识：

| 写法 | 复杂度 | 说明 |
|---|---|---|
| `list.Contains(x)` | O(n) | List 线性扫描 |
| `hashSet.Contains(x)` | O(1) | 哈希查找 |
| `dict[key]` / `dict.TryGetValue` | O(1) | 哈希查找 |
| `list.First(predicate)` | O(n) 最坏 | 线性找 |
| 嵌套 `Where`/`FirstOrDefault` in `foreach` | O(n×m) | 双重循环，务必改字典 |
| `OrderBy` | O(n log n) | 排序 |
| `GroupBy` | O(n) | 内部用哈希分组 |
| `Distinct` | O(n) | 内部用哈希 |

**核心心法**：内层的「查找/匹配」如果在循环里，一定要用 `HashSet`/`Dictionary`/`Lookup`（O(1)），别用 `List.Contains` 或 `List.First`（O(n)）。这是把 O(n²) 变 O(n) 的唯一手段。

### 4.8.5 什么时候放弃 LINQ 写循环

LINQ 不是万能，以下情况手写 `for`/`foreach` 更好：

1. **需要副作用**（累加、写日志、发消息、改外部状态）——LINQ 的 `Select` 里放副作用是反模式（4.4.5），老实写 `foreach`。CP6 `WmsDashboardService.GetTransactionTrendAsync` 生成日期序列 + 填补空洞就是用 `for`，因为要往 `result` 列表里 `Add`（副作用）。
2. **需要 index、需要看前一个/后一个元素**——虽然 LINQ 有 `Select((x,i)=>...)`，但复杂的「相邻元素比较」用 `for` 更清晰。
3. **一趟循环里要同时算好几个不相干的东西**——LINQ 每个聚合是一次遍历，算 5 个聚合可能遍历 5 遍；手写一个 `foreach` 里 5 个累加变量，一趟搞定（对超大内存集合有意义；对 SQL 无所谓，因为 SQL 端一条语句就算完）。
4. **可读性反而变差时**——如果 LINQ 链嵌套三层 `SelectMany` + `GroupBy` 已经没人看得懂，拆成命名良好的循环反而好维护。

> **面试话术**：LINQ 性能四要点——①投影只取需要的列，别拉全表全列；②存在性判断用 `Any` 不用 `Count>0`；③杜绝循环内查数据库（N+1），改成循环外批量查建字典；④内存里嵌套匹配要用 HashSet/Dictionary 把 O(n²) 降 O(n)。放弃 LINQ 的时机：需要副作用、需要相邻元素比较、一趟要算多个聚合、或链太长可读性崩了。

---

## 4.9 面试手写题集（10 道）

> 题目在前，答案在 4.11。先自己在纸上写，再对答案。模型定义见 4.6 开头。

**手写题 1**：给 `List<Stock> stocks`，求每个品番（ProductCd）的库存总量（PhysicalQty 之和），结果按总量降序，返回 `List<(string ProductCd, decimal TotalQty)>`。

**手写题 2**：给 `List<WorkOrder> workOrders`，计算「工单达成率」= 已完工数量（CompletedQty）之和 ÷ 计划生产数量（ProductionQty）之和 × 100，保留 2 位小数。注意分母为 0 时返回 0。

**手写题 3**：给 BOM 需求 `List<string> neededCodes` 和 库存 `List<Stock> stocks`，求「缺料清单」——BOM 需要但库存里没有（或 PhysicalQty ≤ 0）的品番列表。

**手写题 4**：给 `List<WorkOrder> workOrders`，按状态（Status）分组，统计每个状态有多少工单，返回 `Dictionary<int, int>`（状态 → 数量）。

**手写题 5**：给 `List<StockTransaction>`（字段 `TxnDateTime`、`Qty`、`TxnType`），按「年-月」分组，统计每月出入库总量，结果按月份升序。

**手写题 6**：给 `List<Order> orders`（每个订单有 `List<OrderLine> Lines`），把所有订单的所有明细行拍平，输出每行带上所属订单号，返回 `List<(string OrderNo, string ProductCd, decimal Qty)>`。

**手写题 7**：给 `List<Stock> stocks`，找出库存金额（PhysicalQty × UnitPrice）最高的那**一条** Stock 记录（不是金额，是记录本身）。用两种写法（.NET 6 前后各一种）。

**手写题 8**：给价格历史 `List<PriceHistory>`（字段 `ProductCd`、`EffectiveDate`、`Price`），每个品番只取生效日期最新的一条，返回 `List<PriceHistory>`。

**手写题 9**：给订单 `List<Order>` 和 收款 `List<Payment>`（字段 `OrderNo`、`Amount`），做两表对账，返回每个订单的 `(OrderNo, OrderAmount, PaidAmount, Diff)`，要求整体复杂度 O(n)。

**手写题 10**：给 `List<WorkOrder> workOrders`，判断「是否存在」任何一个逾期未完工的工单（`PlanEndDate < today` 且 `Status != 4`）。用最高效的写法。

---

## 4.10 本章面试题 15 问（详细答案）

**Q1：什么是 LINQ？它解决了什么问题？**
A：Language Integrated Query，把声明式查询集成进 C#。解决查询语言与宿主语言割裂的问题——以前查库写 SQL 字符串（编译器管不了类型）、查内存写 for 循环（啰嗦），LINQ 用一套强类型、可组合、编译期检查的算子统一二者，同一套代码既能查内存（LINQ to Objects）也能被 EF Core 翻成 SQL（LINQ to Entities）。

**Q2：LINQ 底层靠哪三个语言特性支撑？**
A：①扩展方法——`Where`/`Select` 定义在静态类 `Enumerable` 上，第一个参数带 `this IEnumerable<T>`，让集合凭空多出算子且能链式调用；②Lambda——把过滤/投影逻辑作为 `Func`/`Expression` 参数传进算子；③迭代器 `yield return`——实现延迟执行，构建时不跑、枚举时逐个产出。

**Q3：方法语法和查询语法有什么区别？用哪个？**
A：编译后等价，查询语法是语法糖会被改写成方法调用，性能无差别。我主用方法语法，因为它覆盖全部算子（`Count`/`Any`/`Skip`/`Take` 没有查询关键字）、能动态拼条件、链式可读。多表 join 时查询语法略清晰，能读懂即可。

**Q4：什么是延迟执行？举例说明。**
A：LINQ 查询在构建（`.Where().Select()`）时不执行，只描述查询计划；直到被枚举（`ToList`/`foreach`/`Count`/`First`）才真正执行。底层靠 `yield return` 生成的状态机——调算子时只 new 出状态机就返回，`MoveNext` 时才逐个产出。例：`var q = list.Where(...)` 不跑，`q.ToList()` 才跑。

**Q5：延迟执行有哪些坑？**
A：①多次枚举 = 查询重复执行，同一个 `IQueryable` 调 `Count()` 再 `ToList()` 会发两条 SQL，需要复用就先 `ToList` 物化；②`Select` 里放副作用会随枚举次数重复触发，Lambda 只写纯函数；③`for` 循环里 Lambda 闭包捕获同一个循环变量，延迟执行时读到的都是循环结束后的值。

**Q6：哪些算子是延迟的，哪些是立即的？怎么记？**
A：返回 `IEnumerable`/`IQueryable` 的是延迟（`Where`/`Select`/`OrderBy`/`GroupBy`/`Join`/`Skip`/`Take`）；返回单值或具体集合的是立即（`ToList`/`ToArray`/`Count`/`Sum`/`Any`/`First`/`Single`/`ToDictionary`）。口诀：还能继续接算子的就延迟，给你最终结果的就立即。

**Q7：IEnumerable 和 IQueryable 的根本区别？**
A：`IEnumerable` 的算子吃 `Func`（编译好的委托），在内存里逐条迭代，过滤发生在客户端；`IQueryable` 的算子吃 `Expression`（表达式树），EF Core 遍历表达式树翻译成 SQL 推给数据库，过滤发生在服务端。对 `DbSet` 保持 `IQueryable` 能把条件推给 SQL 只拉回结果；一旦 `AsEnumerable`/`ToList` 就切到内存，之后的 `Where` 变成全表进内存再过滤。

**Q8：表达式树 Expression<Func<>> 是什么？为什么 EF Core 需要它？**
A：`Func<T,bool>` 是编译好的可执行委托，只能调用看不到内部；`Expression<Func<T,bool>>` 是这段代码的结构树（把 `s => s.Qty > 0` 拆成「大于」节点、「属性访问」节点、「常量」节点）。EF Core 遍历这棵树才知道你比较的是哪个列、什么运算符，从而翻译成 SQL。若参数只是 `Func`，EF 无从得知内部结构，翻译不了。

**Q9：什么是客户端求值？EF Core 3.0 做了什么改变？**
A：客户端求值指 EF 把翻译不了 SQL 的表达式降级到 .NET 进程内存里执行，代价是可能把全表拉进内存。EF Core 2.x 会「悄悄」这么做，埋下线上性能雪崩的定时炸弹。EF Core 3.0 改成破坏性变更——遇到翻译不了的表达式**直接抛 `could not be translated` 异常**，快速失败，逼你要么改写成可翻译的形式，要么显式 `AsEnumerable` 主动切内存。

**Q10：AsEnumerable() 什么时候用？怎么用才对？**
A：它是「显式切内存求值」的开关，之前的算子给 SQL、之后的给内存。正确姿势：先在 SQL 端用 `Where` 过滤行、`Select` 只取需要的列，把数据压到最小，`AsEnumerable`/`ToList` 落内存后，再做 SQL 干不了的计算（如多级 `??` 回退取日期、调用自定义 C# 方法）。铁律：落内存前必须先过滤+投影，绝不能 `_db.Table.AsEnumerable().Where(...)`（等于全表进内存）。CP6 `PlanAchievementService` 就是先 SQL 过滤再落内存算基准日回退。

**Q11：分页为什么必须先 OrderBy？**
A：数据库表是无序集合，`SELECT` 不带 `ORDER BY` 时返回顺序不保证、每次可能不同（取决于执行计划）。不排序直接 `Skip/Take`，会出现第 2 页重复第 1 页的行、或漏行——分页数据错乱。所以必须先 `OrderBy` 建立确定顺序再 `Skip((page-1)*size).Take(size)`。EF Core 会警告 "no ORDER BY, results may be nondeterministic"。

**Q12：First/FirstOrDefault/Single/SingleOrDefault 有什么区别？**
A：`First` 取第一个，空集合抛异常；`FirstOrDefault` 空集合返回默认值（最常用）。`Single` 取唯一一个，空集合**和**多于一个都抛异常（断言恰好一条）；`SingleOrDefault` 空返回默认值、多于一个仍抛。区别：`First` 系列多个不抛就取第一个，`Single` 系列多个必抛。按主键查用 `SingleOrDefault` 语义更严（查出两条说明数据坏了应该炸），但 `First` 只 `TOP 1`、`Single` 要 `TOP 2` 验唯一，性能上 `First` 略优。

**Q13：Any() 和 Count() > 0 哪个好，为什么？**
A：判断存在性一律用 `Any()`。`Any` 找到第一个满足的就返回（SQL `EXISTS`，内存迭代第一个就停）；`Count() > 0` 要数完所有满足的才比较（SQL `COUNT(*)`，内存遍历整个集合）。百万行表 `Any` 可能扫一行就返回，`Count` 要扫全表。

**Q14：GroupBy 返回什么？IGrouping 是什么？分组后怎么做 HAVING？**
A：`GroupBy` 返回 `IEnumerable<IGrouping<TKey,TElement>>`。每个 `IGrouping` 既是 key（`g.Key`）又是组内元素集合（可 `g.Sum()`/`g.Count()`/`foreach`），相当于「带名字的小列表」。`GroupBy` 之前的 `Where` 是 SQL 的 `WHERE`（分组前过滤行），`GroupBy` 之后的 `Where` 是 `HAVING`（对聚合结果过滤），例如 `.GroupBy(x=>x.P).Select(g=>new{g.Key,S=g.Sum(...)}).Where(x=>x.S>1000)`。

**Q15：ToDictionary 和 ToLookup 有什么区别？各自适合什么场景？**
A：`ToDictionary` 一个 key 对应一个 value，**key 重复直接抛 `ArgumentException`**，适合一对一映射且 key 确定唯一（如品番→单价）。`ToLookup` 一个 key 对应一组 value（`IGrouping`），**key 重复合法**，且查不存在的 key 返回空集合不抛异常，适合一对多（如品番→该品番所有明细行）。本质 `ToLookup` 是「预先分好组的字典」。若不确定 key 唯一又想用字典，先 `GroupBy` 再 `ToDictionary(g=>g.Key, g=>g.First())`。

**Q16（附加）：什么是 N+1 查询问题？怎么解决？**
A：循环里对每个元素单独发一次数据库查询——1 次主查 + N 次子查 = N+1 条 SQL，往返延迟叠加导致慢。解决：循环外一次性批量查回相关数据（用 `Contains` 翻 SQL `IN`）建成字典，循环内 O(1) 查字典；EF Core 里更简单，用 `Include` 导航属性让 EF 一条 JOIN 搞定，或用投影 `Select` 一次拉全。

---

## 4.11 手写题 10 道参考答案

**答案 1**（分组求和降序）：
```csharp
var result = stocks
    .GroupBy(s => s.ProductCd)
    .Select(g => (ProductCd: g.Key, TotalQty: g.Sum(x => x.PhysicalQty)))
    .OrderByDescending(x => x.TotalQty)
    .ToList();
```

**答案 2**（达成率，防除零）：
```csharp
decimal produced = workOrders.Sum(w => w.ProductionQty);
decimal completed = workOrders.Sum(w => w.CompletedQty);
decimal rate = produced > 0 ? Math.Round(completed / produced * 100m, 2) : 0m;
// 对应 CP6 MesDashboardService 里 ProgressRate = ProductionQty > 0 ? Math.Round(CompletedQty/ProductionQty*100m,2) : 0m
```

**答案 3**（缺料清单，Except）：
```csharp
var inStock = stocks.Where(s => s.PhysicalQty > 0).Select(s => s.ProductCd);
var shortage = neededCodes.Except(inStock).ToList();
// Except 自动去重；也可写成 neededCodes.Where(c => !inStockSet.Contains(c))（先把 inStock 做成 HashSet 更快）
```

**答案 4**（按状态分组计数成字典）：
```csharp
var byStatus = workOrders
    .GroupBy(w => w.Status)
    .ToDictionary(g => g.Key, g => g.Count());
// 等价简写：workOrders.GroupBy(w => w.Status).ToDictionary(g => g.Key, g => g.Count());
```

**答案 5**（按年月分组统计升序）：
```csharp
var monthly = txns
    .GroupBy(t => new { t.TxnDateTime.Year, t.TxnDateTime.Month })
    .Select(g => new
    {
        g.Key.Year,
        g.Key.Month,
        TotalQty = g.Sum(x => Math.Abs(x.Qty)),
    })
    .OrderBy(x => x.Year).ThenBy(x => x.Month)
    .ToList();
// 参考 CP6 WmsDashboardService.GetTransactionTrendAsync 的 GroupBy(new{Date, TxnType})
```

**答案 6**（订单明细拍平带父级，SelectMany）：
```csharp
var flat = orders
    .SelectMany(o => o.Lines, (o, line) => (o.OrderNo, line.ProductCd, line.Qty))
    .ToList();
// SelectMany 第二个参数（结果选择器）能同时拿到父 order 和子 line
```

**答案 7**（金额最高的记录，两种写法）：
```csharp
// .NET 6+：MaxBy 直接返回记录
Stock top1 = stocks.MaxBy(s => s.PhysicalQty * (s.UnitPrice ?? 0m));

// .NET 6 之前：OrderByDescending().First()
Stock top2 = stocks
    .OrderByDescending(s => s.PhysicalQty * (s.UnitPrice ?? 0m))
    .First();
```

**答案 8**（每品番取最新一条）：
```csharp
// .NET 6+
var latest = priceHistory
    .GroupBy(p => p.ProductCd)
    .Select(g => g.MaxBy(x => x.EffectiveDate))
    .ToList();

// 通用写法
var latest2 = priceHistory
    .GroupBy(p => p.ProductCd)
    .Select(g => g.OrderByDescending(x => x.EffectiveDate).First())
    .ToList();
```

**答案 9**（两表对账 O(n)，ToLookup）：
```csharp
var payByOrder = payments.ToLookup(p => p.OrderNo);   // O(m) 建表，一个订单可能多笔
var recon = orders.Select(o =>
{
    var paid = payByOrder[o.OrderNo].Sum(p => p.Amount);   // O(1) 查，空集合 Sum=0
    return (o.OrderNo, OrderAmount: o.Amount, PaidAmount: paid, Diff: o.Amount - paid);
}).ToList();
// 整体 O(n+m)，不是嵌套循环的 O(n*m)。对应 CP6 用 HashSet/Dictionary 消灭 O(n²) 的做法
```

**答案 10**（存在性判断用 Any）：
```csharp
var today = DateTime.Today;
bool hasOverdue = workOrders.Any(w => w.PlanEndDate < today && w.Status != 4);
// 用 Any 不用 Count()>0：找到第一个逾期工单就立即返回 true。
// EF Core 里翻译成 SELECT CASE WHEN EXISTS(...) —— 数据库只需判断存在性
```

---

## 4.12 自测清单

对照下面每一条，能脱口而出讲清楚+手写代码，才算掌握本章：

- [ ] 能一句话说清 LINQ 解决了什么问题（查询语言与宿主语言割裂）。
- [ ] 能说出撑起 LINQ 的三根支柱（扩展方法 / Lambda / 迭代器 yield return）并解释各自作用。
- [ ] 能区分方法语法和查询语法，知道后者是语法糖、编译后等价，知道为什么主用方法语法。
- [ ] 能解释延迟执行的原理（yield return 状态机），能默写「延迟 vs 立即」算子分类。
- [ ] 能说出延迟执行三大坑（多次枚举重复执行 / Select 副作用 / for 闭包捕获）并给修复方案。
- [ ] 能讲清 IEnumerable（Func，内存）vs IQueryable（Expression，翻 SQL）的根本区别。
- [ ] 能解释表达式树是什么，能**手写一段** `Expression.Property/Constant/GreaterThan/Lambda` 构建过程。
- [ ] 能讲客户端求值的危害和 EF Core 3.0 改为抛异常的历史。
- [ ] 能说清 AsEnumerable 的正确用法（先 SQL 过滤投影到最小，再落内存算 SQL 干不了的）。
- [ ] 能默写 First/FirstOrDefault/Single/SingleOrDefault 的「空集合 / 多元素」抛异常对比表。
- [ ] 能解释「分页必须先 OrderBy」的原因（无序集合、返回顺序不保证）。
- [ ] 能说清 Any() 优于 Count()>0 的原因（EXISTS vs COUNT）。
- [ ] 能解释 IGrouping、复合键分组、GroupBy 前后的 Where 分别对应 WHERE / HAVING。
- [ ] 能区分 Join（INNER）/ GroupJoin（LEFT），知道 EF 导航属性何时替代手写 Join、何时必须手写。
- [ ] 能区分 ToDictionary（key 唯一，重复抛异常）和 ToLookup（key 可重复，一对多）。
- [ ] 能说出 .NET 6+ 新算子 Chunk / MinBy / MaxBy / DistinctBy 各自用途。
- [ ] 能默写「动态多条件查询」骨架（`q = _db.X.Where(基础); if(有值) q = q.Where(...)`）。
- [ ] 能默写「两表对账 O(n²)→O(n)」用 ToLookup/Dictionary/HashSet 优化的写法。
- [ ] 能识别并修复 N+1 查询（循环内查库 → 循环外批量查建字典）。
- [ ] 能说出何时该放弃 LINQ 改写循环（副作用 / 相邻元素 / 多聚合一趟 / 可读性）。
- [ ] 10 道手写题能不看答案独立写出。

---

### 本章 CP6 真实标本索引（面试可自信引用）

| 标本文件 | 方法 | 演示的算子/模式 |
|---|---|---|
| `CP6.WebApi\Controllers\Wms\StockController.cs` | `Search` | 动态多条件 Where、四级 OrderBy/ThenBy、Skip/Take 分页、Count+ToList 两次物化、AsNoTracking |
| `CP6.Core\Services\Wms\WmsDashboardService.cs` | `GetKpiAsync` / `GetWarehouseValueAsync` | GroupBy+Sum、Distinct().Count()、HashSet 消 O(n²)、ToDictionaryAsync、FirstOrDefault+??兜底 |
| `CP6.Core\Services\Mes\MesDashboardService.cs` | `GetDefectTop5Async` / `GetProcessProgressAsync` | 复合键 GroupBy、g.Count(条件)、OrderByDescending+Take TopN、GroupBy 防重复 key |
| `CP6.Core\Services\Mes\PlanAchievementService.cs` | `GetSummaryAsync` | IQueryable 端 Where + ToList 落内存后算多级 ?? 回退（AsEnumerable 模式典范） |
| `CP6.Core\Services\Sys\PermissionAggregator.cs` | `FillActionKeysAsync` 等 | Join（方法+查询两种语法）、Contains 翻 IN、GroupBy+ToDictionary+SelectMany、ToHashSet |
| `CP6.Core\Services\Space\SpaceBinDriftScanner.cs` | `ScanAsync` | 跨系统同 GUID 手写 Join（无导航属性场景） |
| `CP6.Core\Services\Oa\InboxService.cs` | `DetailAsync` | SelectMany 拍平多字段 + Concat 拼接 |

> 面试时被问「你在项目里怎么用 LINQ」，就从这张表挑 2-3 个说：动态查询讲 `StockController.Search`，报表聚合讲 `MesDashboardService.GetDefectTop5Async`，性能优化讲 `WmsDashboardService` 用 HashSet 消灭滞留品判断的 O(n²)。有真实代码支撑，比背概念可信十倍。
