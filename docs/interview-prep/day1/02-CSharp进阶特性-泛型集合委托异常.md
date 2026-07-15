# 第 2 章　C# 进阶语言特性（泛型 · 集合 · 委托 · 异常 · 现代语法）

> 面向对象：把你当成刚学完 C# 基础语法（变量、`if`、`for`、类、方法）的新手。
> 目标：3 天后面试「制造业生产管理系统开发工程师（C# + SQL + Vue，5 年强度）」。
> 教学方法：**所有示例都取自你即将要「假装很熟」的真实生产项目 `C:\CP6`**——一个 .NET 8 多租户制造业 ERP/MES/WMS 系统。看懂这些代码，你就能在面试里说「我在项目里就是这么写的」。

---

## 本章学习地图

| 小节 | 主题 | 面试出现频率 | CP6 核心标本 |
|------|------|:---:|------|
| 2.1 | 泛型（Generics） | ★★★★★ | `IRepository<T>` / `RepositoryBase<T>` |
| 2.2 | 集合全家桶 | ★★★★★ | `List<T>` / `Dictionary` / `HashSet` |
| 2.3 | 委托与事件 | ★★★★☆ | `CacheService` / `IntegrationEventDispatcher` |
| 2.4 | 异常处理 | ★★★★★ | `InsufficientStockException` / `StockController` |
| 2.5 | 可空引用类型 | ★★★★☆ | `StockController` 的 `??=` `?.` |
| 2.6 | 字符串专题 | ★★★★☆ | `UnshippedOrderService` 的 CSV 构建 |
| 2.7 | 现代 C# 语法 | ★★★★☆ | `record PagedResult<T>` / `switch` 表达式 |
| 2.8 | 值得知道的 | ★★★☆☆ | `yield return` 的 `TokenLineage` |

> **每个知识点固定五步结构**：概念讲解（配类比）→ CP6 真实代码（标注路径）→ 逐行解析 → 常见坑 → 面试怎么问 + 参考答案。请务必把「面试怎么问」的部分大声读出来练一遍。

---

## 本章要用到的 CP6 标本清单（先混个脸熟）

| 文件路径 | 它是什么 |
|------|------|
| `C:\CP6\CP6.Entity\BaseEntity.cs` | 所有数据库实体的公共基类（有 `Id`/`CreateDate` 等公共字段） |
| `C:\CP6\CP6.Core\BaseProvider\IRepository.cs` | 泛型仓储**接口**——定义「任意实体」的增删改查签名 |
| `C:\CP6\CP6.Core\BaseProvider\RepositoryBase.cs` | 泛型仓储**实现**——一份代码搞定所有表的 CRUD |
| `C:\CP6\CP6.Core\BaseProvider\IService.cs` / `ServiceBase.cs` | 泛型业务服务基类 |
| `C:\CP6\CP6.Core\Services\Wms\InsufficientStockException.cs` | 自定义业务异常（库存不足） |
| `C:\CP6\CP6.WebApi\Controllers\Wms\StockController.cs` | Web API 控制器，异常分类捕获 + 空值运算符 |
| `C:\CP6\CP6.Core\Utilities\CacheService.cs` | 用 `Func<>` 委托实现缓存旁路（Cache-Aside） |
| `C:\CP6\CP6.Core\Services\Wf\TokenLineage.cs` | `yield return` 迭代器真实用例 |

---
---

# 2.1　泛型（Generics）

## 2.1.1 为什么需要泛型：一个「装箱 + 类型不安全」的血案

### 概念讲解（类比）

想象你开一家干洗店，只有一种「万能收纳袋」，什么衣服都往里塞（西装、羽绒服、袜子）。取件时你必须**每次都拆开袋子看一眼**这到底是啥，还可能把顾客 A 的袜子当成顾客 B 的西装拿错。这就是**非泛型集合**（`ArrayList`、`Hashtable`）的世界：所有东西都被当成万能类型 `object`。

泛型（Generics）相当于给你**一整排贴好标签的专用袋子**：「只装西装的袋子」`List<西装>`、「只装袜子的袋子」`List<袜子>`。放错类型？**编译器当场报错**，根本轮不到运行时拿错。

C# 里的 `<T>` 就是那个「标签」，`T` 是 **Type parameter（类型参数）** 的占位符，用的时候换成真实类型。

### 没有泛型的两大痛点

**痛点一：装箱/拆箱（Boxing / Unboxing）性能损耗**

```csharp
// 上古写法（.NET 1.1 时代），ArrayList 里每个元素都是 object
System.Collections.ArrayList list = new();
list.Add(42);          // int(值类型) → object，装箱：在堆上分配一个盒子把 42 包进去
int x = (int)list[0];  // object → int，拆箱：还得强制转换，运行时才检查类型
```

- **装箱（Boxing）**：值类型（如 `int`）被塞进 `object` 时，CLR 要在**托管堆**上新建一个对象，把值复制进去。循环几百万次 = 几百万次堆分配 + GC 压力。
- **拆箱（Unboxing）**：取出来要强制转换，类型不对就运行时抛 `InvalidCastException`。

**痛点二：类型不安全（Type-unsafe）**

```csharp
System.Collections.ArrayList list = new();
list.Add(42);
list.Add("hello");     // 编译器不拦你！object 什么都能装
int sum = 0;
foreach (var item in list)
    sum += (int)item;  // 运行到 "hello" 这行才炸：InvalidCastException
```

泛型把这两个痛点**同时**解决：

```csharp
List<int> list = new();
list.Add(42);
// list.Add("hello");  // ← 编译期直接红线报错，代码根本编译不过
int sum = 0;
foreach (int item in list)   // 不用装箱、不用强转
    sum += item;
```

| 维度 | `ArrayList`（非泛型） | `List<int>`（泛型） |
|------|------|------|
| 存 `int` | 装箱到堆 | 直接存值，无装箱 |
| 取 `int` | 拆箱 + 强转 | 直接取值 |
| 放错类型 | 运行时才炸 | **编译期报错** |
| 性能 | 差（GC 压力大） | 优 |
| 可读性 | 不知道装了啥 | 类型自解释 |

---

## 2.1.2 泛型接口 + 泛型类：精读 CP6 的泛型仓储

这是本章**最重要的标本**。CP6 里所有数据库表的增删改查（几十张表）只写了**一份** CRUD 代码，靠的就是泛型。

### CP6 真实代码：泛型接口 `IRepository<T>`

`C:\CP6\CP6.Core\BaseProvider\IRepository.cs`：

```csharp
using System.Linq.Expressions;
using CP6.Entity;

namespace CP6.Core.BaseProvider;

/// <summary>
/// 泛型仓储接口 - 定义所有实体通用的数据库操作
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    /// <summary>根据 Id 查询单条</summary>
    Task<T?> FindAsync(Guid id);

    /// <summary>分页查询</summary>
    Task<(List<T> Data, int Total)> GetPageListAsync(
        Expression<Func<T, bool>>? filter,
        int page,
        int pageSize,
        string orderBy = "CreateDate desc");

    /// <summary>新增</summary>
    Task<T> AddAsync(T entity);

    /// <summary>修改</summary>
    Task<T> UpdateAsync(T entity);

    /// <summary>删除（支持批量）</summary>
    Task<int> DeleteAsync(params Guid[] ids);
}
```

### 逐行解析

| 代码片段 | 含义 |
|------|------|
| `interface IRepository<T>` | 声明一个**泛型接口**，`T` 是类型参数占位符。用的时候 `IRepository<Stock>`、`IRepository<Order>`，`T` 分别变成 `Stock`、`Order`。 |
| `where T : BaseEntity` | **泛型约束（Generic constraint）**：限定 `T` 必须是 `BaseEntity` 或它的子类。这样接口内部才敢访问 `T` 的 `Id`、`CreateDate` 等 `BaseEntity` 定义的成员。 |
| `Task<T?> FindAsync(Guid id)` | 返回值是 `T?`（可空的 T）——查不到返回 `null`。`Task<>` 表示异步。 |
| `Task<(List<T> Data, int Total)> GetPageListAsync(...)` | 返回一个**元组（Tuple）**：`List<T>` 是数据、`int` 是总数。分页查询同时要数据和总条数。 |
| `Expression<Func<T, bool>>? filter` | 筛选条件，是个「表达式树」（后面 LINQ 章讲），`?` 表示可以传 `null`（不筛选）。 |
| `params Guid[] ids` | `params` 让调用方既能传数组也能传 `Delete(id1, id2, id3)`。 |

> **面试加分点**：`where T : BaseEntity` 这行是理解泛型约束的最好例子——「因为约束了 `T` 是 `BaseEntity`，编译器才允许我在泛型代码里写 `x.CreateDate`」。

### CP6 真实代码：泛型类实现 `RepositoryBase<T>`

`C:\CP6\CP6.Core\BaseProvider\RepositoryBase.cs`：

```csharp
using System.Linq.Expressions;
using CP6.Core.EFDbContext;
using CP6.Entity;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.BaseProvider;

/// <summary>
/// 泛型仓储实现 - 所有实体通用的增删改查
/// 新建业务只需继承它，不用重复写 CRUD 代码
/// </summary>
public class RepositoryBase<T> : IRepository<T> where T : BaseEntity
{
    protected readonly CP6Context _context;
    protected readonly DbSet<T> _dbSet;

    public RepositoryBase(CP6Context context)
    {
        _context = context;
        _dbSet = context.Set<T>();     // ← 关键：按 T 类型取出对应的表
    }

    public async Task<T?> FindAsync(Guid id)
    {
        return await _dbSet.FindAsync(id);
    }

    public async Task<(List<T> Data, int Total)> GetPageListAsync(
        Expression<Func<T, bool>>? filter, int page, int pageSize,
        string orderBy = "CreateDate desc")
    {
        IQueryable<T> query = _dbSet;
        if (filter != null)
            query = query.Where(filter);           // 有条件才筛

        var total = await query.CountAsync();        // 先数总数
        var data = await query
            .OrderByDescending(x => x.CreateDate)    // ← 敢用 CreateDate，全靠 where T : BaseEntity
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (data, total);
    }

    public async Task<T> AddAsync(T entity)
    {
        _dbSet.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<T> UpdateAsync(T entity)
    {
        entity.ModifyDate = DateTime.Now;            // ← 同样是 BaseEntity 的字段
        _context.Entry(entity).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<int> DeleteAsync(params Guid[] ids)
    {
        var entities = await _dbSet.Where(x => ids.Contains(x.Id)).ToListAsync();
        _dbSet.RemoveRange(entities);
        return await _context.SaveChangesAsync();
    }
}
```

### 逐行解析（重点）

- `class RepositoryBase<T> : IRepository<T> where T : BaseEntity`：泛型类**实现**泛型接口，约束必须**一字不差地重复**（`where T : BaseEntity`）。
- `protected readonly DbSet<T> _dbSet;`：`DbSet<T>` 是 EF Core 对「一张数据库表」的抽象。`DbSet<Stock>` 就是库存表，`DbSet<Order>` 就是订单表。
- `_dbSet = context.Set<T>();`：**泛型方法**登场。`context.Set<T>()` 根据运行时的 `T` 返回对应的表。这一行是「一份代码通吃所有表」的魔法核心。
- `.OrderByDescending(x => x.CreateDate)`：`x` 的类型是 `T`，能点出 `.CreateDate`——**正是因为约束了 `T : BaseEntity`**。去掉约束，这行立刻编译报错。
- `entity.ModifyDate = DateTime.Now;`：同理，改的是 `BaseEntity` 定义的公共字段。

### 配套标本：`BaseEntity`（约束的「天花板」）

`C:\CP6\CP6.Entity\BaseEntity.cs`：

```csharp
public abstract class BaseEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    [MaxLength(100)] public string? Creator { get; set; }
    public DateTime CreateDate { get; set; } = DateTime.Now;
    [MaxLength(100)] public string? Modifier { get; set; }
    public DateTime? ModifyDate { get; set; }
}
```

> 一句话理解整套设计：**「`BaseEntity` 定义公共字段 → `where T : BaseEntity` 约束保证能访问这些字段 → 一份泛型 CRUD 代码服务所有表」**。这是企业级 .NET 项目最经典的「泛型仓储模式（Generic Repository Pattern）」，面试常考。

### 泛型服务层：`IService<T>` / `ServiceBase<T>`

CP6 在仓储之上又叠了一层业务服务（同样是泛型）。`C:\CP6\CP6.Core\BaseProvider\ServiceBase.cs`：

```csharp
public class ServiceBase<T> : IService<T> where T : BaseEntity
{
    protected readonly IRepository<T> _repository;      // ← 依赖泛型接口，不依赖实现

    public ServiceBase(IRepository<T> repository)
    {
        _repository = repository;
    }

    public virtual async Task<T> AddAsync(T entity)
    {
        entity.CreateDate = DateTime.Now;               // 统一填创建时间
        return await _repository.AddAsync(entity);
    }

    public virtual async Task<int> DeleteAsync(Guid[] ids)
        => await _repository.DeleteAsync(ids);
}
```

- 注意所有方法都是 `virtual`——子类（如具体的 `OrderService`）可以 `override` 加自己的业务逻辑。这是「泛型基类 + 虚方法」的可扩展套路。
- `ServiceBase` 依赖的是 `IRepository<T>`（接口）而不是 `RepositoryBase<T>`（实现），这是**依赖倒置（DIP）**，方便测试时替换成假仓储。

### 常见坑

1. **忘了在实现类重写约束**：`class Foo<T> : IBar<T>` 若接口有 `where T : BaseEntity`，实现类**也必须写**，否则编译报错 CS0455/CS0311。
2. **以为约束是「可选的注释」**：约束是硬性契约，编译器真的靠它做类型检查。
3. **`context.Set<T>()` 传了没注册的实体**：`T` 必须是 `DbContext` 里配置过的实体，否则运行时抛异常。

### 面试怎么问 + 参考答案

**Q：项目里怎么避免给每张表都写一遍增删改查？**
> A：用泛型仓储模式。定义 `IRepository<T> where T : BaseEntity` 泛型接口和 `RepositoryBase<T>` 泛型实现，内部用 `context.Set<T>()` 按类型参数拿到对应的 `DbSet<T>`。约束 `T : BaseEntity` 保证能统一访问 `Id`、`CreateDate` 等公共字段。新增一张表只要有对应实体，直接复用这份 CRUD，一行都不用重写。我们项目 `C:\CP6\CP6.Core\BaseProvider` 下就是这么组织的。

**Q：泛型和 `object` 存集合有什么区别？**
> A：三点。①类型安全——泛型编译期就拦住放错类型，`object` 要运行时才炸；②性能——值类型存 `object` 会装箱到堆、取出要拆箱，泛型直接存值无装箱；③可读性——`List<Order>` 自解释，`ArrayList` 不知道装了啥。

---

## 2.1.3 泛型方法（Generic Method）

### 概念讲解

泛型不一定要整个类都泛型。**单个方法**也能带自己的类型参数——这叫泛型方法，`<T>` 写在方法名后面。

### CP6 真实代码：缓存旁路里的泛型方法

`C:\CP6\CP6.Core\Utilities\CacheService.cs`：

```csharp
public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory,
                                      TimeSpan? expiration = null) where T : class
{
    // Step 1: 查缓存
    var cached = await GetAsync<T>(key);
    if (cached != null) return cached;

    // Step 2: 缓存未命中，查数据库
    var data = await factory();

    // Step 3: 写入缓存
    await SetAsync(key, data, expiration);
    return data;
}
```

### 逐行解析

- `GetOrSetAsync<T>(...)`：`<T>` 跟在方法名后，说明这是泛型方法。调用方 `GetOrSetAsync<List<Product>>("products", ...)` 时 `T = List<Product>`。
- `Func<Task<T>> factory`：一个**委托**参数（2.3 详讲）——「拿不到缓存时，去哪儿捞数据」的逻辑由调用方以 lambda 传进来。
- `where T : class`：约束 `T` 是引用类型（这样 `cached != null` 才有意义，值类型永不为 null）。
- 这就是经典的 **Cache-Aside（缓存旁路）** 模式：先查缓存，没有就执行 `factory` 查库再回填。

### 常见坑

- **泛型方法的 `<T>` 能被编译器推断**：`GetOrSetAsync("k", () => LoadAsync())`，如果 `LoadAsync` 返回 `Task<List<Product>>`，`T` 会自动推断成 `List<Product>`，不用手写 `<...>`。
- **类 `<T>` 和 方法 `<T>` 别重名**：泛型类里再写个泛型方法用同名 `T` 会警告遮蔽（shadowing）。

### 面试怎么问 + 参考答案

**Q：泛型类和泛型方法什么时候用哪个？**
> A：如果整个类的所有成员都围绕同一个类型参数转（如仓储 `RepositoryBase<T>`），用泛型类；如果只是某个方法需要类型灵活性、类本身不泛型（如缓存服务里的 `GetOrSetAsync<T>`），用泛型方法。泛型方法的类型参数常能被编译器从实参推断，调用更简洁。

---

## 2.1.4 全部泛型约束（Generic Constraints）速查

| 约束写法 | 含义 | 例子 |
|------|------|------|
| `where T : class` | T 必须是**引用类型** | `CacheService.GetOrSetAsync<T>` |
| `where T : struct` | T 必须是**值类型**（不含可空） | `T? Parse<T>() where T : struct` |
| `where T : new()` | T 必须有**公共无参构造**（能 `new T()`） | 工厂方法 |
| `where T : BaseEntity` | T 必须是**某基类或其子类** | `RepositoryBase<T>` |
| `where T : IComparable` | T 必须**实现某接口** | 排序工具 |
| `where T : notnull` | T 不能是可空类型 | 字典 key |
| `where T : unmanaged` | T 是非托管类型（指针场景） | 高性能互操作 |
| `where T : U` | T 必须是另一个类型参数 U 的子类 | 复杂泛型 |
| 组合 | 多个约束用逗号，`class` 须在最前，`new()` 须在最后 | `where T : class, IEntity, new()` |

```csharp
// 组合约束的顺序规则演示
public class Factory<T> where T : BaseEntity, new()
{
    public T Create() => new T();   // 因为有 new() 约束，才能 new T()
}
```

### 面试怎么问 + 参考答案

**Q：`where T : new()` 有什么用？**
> A：它保证类型参数有公共无参构造函数，这样泛型代码里才能写 `new T()`。常见于工厂、需要在泛型里实例化对象的场景。注意组合约束时 `new()` 必须写在最后。

---

## 2.1.5 协变（Covariance）与逆变（Contravariance）入门

### 概念讲解（类比）

「一箱苹果」能不能当「一箱水果」用？直觉上可以（苹果是水果）。但 C# 泛型**默认不允许**：`List<Apple>` 不能赋值给 `List<Fruit>`。原因是 `List` 既能读又能写——如果允许，你就能往「一箱苹果」里塞一根香蕉，类型系统就破了。

**协变/逆变**是「在安全的前提下，放开这种父子替换」的机制：

- **协变 `out`**：只**输出** T 的接口可以「子类当父类」。`IEnumerable<Apple>` 能赋值给 `IEnumerable<Fruit>`——因为只能往外读，读出来的苹果当然是水果，安全。
- **逆变 `in`**：只**输入** T 的接口可以「父类当子类」。`IComparer<Fruit>` 能当 `IComparer<Apple>` 用——能比较任意水果的比较器，当然也能比较苹果。

```csharp
// 协变：out T（只读位置）
IEnumerable<string> strings = new List<string> { "a", "b" };
IEnumerable<object> objects = strings;   // ✅ 合法，因为 IEnumerable<out T>

// 逆变：in T（只写/输入位置）
Action<object> actObj = o => Console.WriteLine(o);
Action<string> actStr = actObj;          // ✅ 合法，因为 Action<in T>
```

| 关键字 | 名称 | T 只能出现在 | 记忆 | 典型接口 |
|------|------|------|------|------|
| `out` | 协变 | 返回值（输出位置） | out=输出=子→父 | `IEnumerable<out T>` |
| `in` | 逆变 | 参数（输入位置） | in=输入=父→子 | `IComparer<in T>` / `Action<in T>` |
| 无 | 不变 | 输入+输出都有 | 只能精确匹配 | `List<T>` |

> CP6 里 `IRepository<T>` 是**不变**的（既有 `AddAsync(T)` 输入又有 `FindAsync` 返回 T 输出），所以它没法加 `out`/`in`，这本身就是个好例子：能读又能写的接口天然不变。

### 常见坑

- `List<T>` 永远不变，别指望 `List<Apple>` → `List<Fruit>`。要协变就用 `IEnumerable<T>`。

### 面试怎么问 + 参考答案

**Q：`IEnumerable<T>` 为什么能协变而 `List<T>` 不能？**
> A：`IEnumerable<out T>` 只提供读（迭代输出），T 只出现在输出位置，子类集合当父类集合读取绝对安全，所以标了 `out` 支持协变。`List<T>` 既能读又能写（`Add(T)`），T 出现在输入位置，若允许协变就能往子类集合塞进不兼容的元素，破坏类型安全，所以它是不变的。

---

## 2.1.6 C# 泛型不擦除 vs Java 类型擦除（高频对比题）

### 概念讲解

这是「有没有跨语言视野」的送分/送命题。

- **Java 泛型是「类型擦除（Type Erasure）」**：泛型只在**编译期**做检查，编译后 `List<String>` 和 `List<Integer>` 都变回 `List`（裸类型），运行时 `T` 的信息**没了**。所以 Java 里 `new T()`、`T.class`、`instanceof List<String>` 都做不到。
- **C# 泛型是「运行时具体化（Reification）」**：`T` 的真实类型信息**保留到运行时**。CLR 会为每个值类型参数生成专门的代码（`List<int>` 和 `List<double>` 是运行时不同的类型），引用类型参数则共享一份代码但保留类型元数据。

| 维度 | C# 泛型 | Java 泛型 |
|------|------|------|
| 实现机制 | 运行时具体化（reified） | 编译期擦除（erased） |
| 运行时能否拿到 T | **能**（`typeof(T)`、反射） | 不能 |
| 能否 `new T()` | 能（加 `new()` 约束） | 不能 |
| 值类型是否装箱 | **不装箱**（`List<int>` 直接存 int） | 装箱（`List<Integer>`，没有 `List<int>`） |
| `List<int>` 与 `List<string>` 运行时 | 不同类型 | 同一个裸 `List` |

```csharp
// C# 能在运行时拿到 T 的真实类型——Java 做不到
public string TypeNameOf<T>() => typeof(T).Name;   // typeof(T) 运行时有效
```

### 面试怎么问 + 参考答案

**Q：C# 和 Java 的泛型有什么本质区别？**
> A：Java 是类型擦除，泛型信息只存在于编译期，运行时 `List<String>` 退化成裸 `List`，拿不到 T 的类型，也没法 `new T()`，值类型还得装箱成 `Integer`。C# 是运行时具体化，泛型类型信息保留到运行时，可以 `typeof(T)`、反射、加 `new()` 约束直接 `new T()`，而且 `List<int>` 真的按 int 存储不装箱，性能更好。CP6 里 `context.Set<T>()` 能在运行时按 `T` 找到对应的表，正是靠 C# 运行时保留了泛型类型信息。

---
---

# 2.2　集合全家桶（Collections）

## 2.2.1 `List<T>`：内部就是一个会自动扩容的数组

### 概念讲解（类比）

`List<T>` 像一个「可伸缩的停车场」。底层其实是一个**定长数组**（`T[]`），但它记着两个数：
- `Count`：现在停了几辆车（元素个数）；
- `Capacity`：一共有几个车位（数组长度）。

当车位停满（`Count == Capacity`）又要再停一辆，它会**新建一个两倍大的数组**，把旧车全部挪过去，再停新车。这就是**扩容（Grow）**。

### 内部扩容机制（面试高频）

```
初始:  Capacity=0
Add 第1个 → 扩容到 4      [_,_,_,_]      (第一次分配一般到 4)
Add 到第5个 → 4满了 → 扩容到 8   [........]  复制原 4 个过去
Add 到第9个 → 8满了 → 扩容到 16
...每次翻倍：4 → 8 → 16 → 32 → 64 ...
```

- 扩容策略：容量不够时**翻倍**（`newCapacity = oldCapacity * 2`）。
- 每次扩容要 `Array.Copy` 把老元素搬到新数组——**O(n)** 操作。
- 但因为是「翻倍」，均摊下来每次 `Add` 仍是 **O(1) 均摊（amortized）**。

**性能优化**：如果你**预先知道大概要放多少个**，用 `new List<T>(capacity)` 一次性分配，避免反复扩容复制。

```csharp
// CP6 的 BankStatementImporter.cs 就是预分配好习惯的体现
var list = new List<string>();      // 若知道行数，可 new List<string>(rowCount)
```

### 内存示意图

```
List<int> { Count=3, Capacity=4 }
        ┌───────────────────────┐
        │ _items (T[] 引用) ────┼──►  [ 10 ][ 20 ][ 30 ][ 空 ]
        │ _size = 3             │        0     1     2     3
        └───────────────────────┘   ↑用了3个        ↑还剩1个车位
```

### 常见坑

1. **在 `foreach` 里增删元素** → 抛 `InvalidOperationException`（集合已被修改）。要删就倒序 `for` 循环，或先筛选到新列表。
2. **误以为 `Capacity` 会自动缩小**：删元素 `Capacity` 不会自动降，内存不会立即释放，需要 `TrimExcess()`。
3. **频繁 `Insert(0, x)`（头插）** → 每次都要把后面所有元素后移，O(n)，量大时用 `LinkedList` 或队列。

### 面试怎么问 + 参考答案

**Q：`List<T>` 底层是什么？扩容怎么扩？**
> A：底层是一个 `T[]` 数组，维护 `Count`（实际元素数）和 `Capacity`（数组容量）。当 `Add` 时 `Count == Capacity`，会新建一个容量翻倍的数组，用 `Array.Copy` 把旧元素复制过去。单次扩容是 O(n)，但翻倍策略让 `Add` 均摊为 O(1)。如果预知大小，用带 capacity 的构造函数预分配能避免多次复制。

---

## 2.2.2 `Dictionary<TKey, TValue>`：哈希表与「为什么 key 要正确实现 GetHashCode」

### 概念讲解（类比）

字典像一个**带很多抽屉的柜子**。你给一个 key（比如「订单号 A001」），字典先用 `key.GetHashCode()` 算出一个数字（哈希码），再对抽屉总数取模，直接定位到「第几号抽屉」，一步到位，不用一个个翻。这就是查找 **O(1)** 的原理。

### 哈希原理三步

```
存 dict["A001"] = order:
  1. hash = "A001".GetHashCode()         → 比如 -1180624258
  2. bucket = hash & (桶数-1)             → 定位到第 6 号桶
  3. 把 (key,value) 放进 6 号桶的链表

查 dict["A001"]:
  1. 同样算 hash → 定位到 6 号桶
  2. 在 6 号桶里逐个用 Equals 比对 key，找到 "A001"
```

### 哈希冲突（Hash Collision）

不同 key 可能算出**同一个桶号**（抽屉不够多，或哈希函数烂）。这叫冲突。字典的解法是**链地址法**：同一个桶挂一条链（.NET 用数组 + 链式索引），冲突的元素排在同一桶里，查的时候在桶内用 `Equals` 逐个比。

- 冲突少 → 每桶 1 个 → 查找 O(1)；
- 冲突严重（所有 key 挤在一个桶）→ 退化成链表 O(n)。

### 为什么 key 要正确实现 GetHashCode 和 Equals（核心考点）

字典靠 **`GetHashCode` 定位桶 + `Equals` 桶内精确匹配**。两条铁律：

1. **`Equals` 相等的两个对象，`GetHashCode` 必须相等**。否则「同一个 key」被算到不同桶，存进去就再也取不出来。
2. **`GetHashCode` 应尽量分散**，减少冲突。

`string`、`int`、`Guid` 这些内置类型都正确实现了，所以直接当 key 没问题。**但如果你用自定义类当 key，又没重写这两个方法**，默认用引用相等——两个「内容相同但对象不同」的 key 会被当成不同 key。

```csharp
// CP6 里字典 key 基本是 string / Guid / 值元组，天生安全
// FlowEngine.cs 用字符串 key，还特意指定了忽略大小写的比较器：
_handlers = (handlers ?? DefaultHandlers())
    .ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase);
//                              ↑ 指定 key 的比较方式，"http" 和 "HTTP" 视为同一 key
```

`StringComparer.OrdinalIgnoreCase` 是「自定义 key 相等语义」的正规做法：它同时提供了配套的 `GetHashCode` 和 `Equals`，保证不违反铁律。

### 复杂度

| 操作 | 平均 | 最坏（全冲突） |
|------|:---:|:---:|
| 增 `Add` | O(1) | O(n) |
| 查 `[key]` / `TryGetValue` | O(1) | O(n) |
| 删 `Remove` | O(1) | O(n) |

### 常见坑

1. **可变对象当 key**：如果 key 对象的字段在放进字典后被改，`GetHashCode` 变了，就永远找不回来了。**key 应当不可变**。
2. **`dict[key]` 取不存在的 key** → 抛 `KeyNotFoundException`。安全写法是 `TryGetValue`：
   ```csharp
   if (dict.TryGetValue("A001", out var order)) { /* 用 order */ }
   ```
3. **`dict.ContainsKey` + `dict[key]` 查两次** → 用一次 `TryGetValue` 更高效。

### 面试怎么问 + 参考答案

**Q：`Dictionary` 为什么查找是 O(1)？哈希冲突怎么处理？**
> A：字典是哈希表。存/查时对 key 调 `GetHashCode` 算哈希码，再映射到桶号直接定位，不用遍历，所以平均 O(1)。不同 key 映射到同一桶就是哈希冲突，.NET 用链地址法，把冲突元素挂在同一桶，桶内再用 `Equals` 逐个精确比对。冲突越少越接近 O(1)，极端全冲突退化成 O(n)。

**Q：自定义类型当字典 key 要注意什么？**
> A：必须同时正确重写 `GetHashCode` 和 `Equals`，且保证「`Equals` 相等则 `GetHashCode` 相等」，否则同一逻辑 key 会被算到不同桶导致存进去取不出。而且 key 最好不可变，避免放进字典后哈希值变化。实践中优先用 `string`、`Guid`、`record` 或值元组当 key——它们的相等语义天生正确。

---

## 2.2.3 `HashSet<T>` / `Queue<T>` / `Stack<T>` / `LinkedList<T>`

### `HashSet<T>`：去重集合，「这个元素在不在」O(1)

和 `Dictionary` 同源（只有 key 没有 value），专门解决「去重」和「快速判断存在性」。

**CP6 真实用例**——`C:\CP6\CP6.Core\Services\Wf\TokenLineage.cs` 用 `HashSet<Guid>` 做**防环检测**（visited 集合）：

```csharp
public static IEnumerable<Wf_FlowToken> AncestorChain(
    IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t)
{
    var seen = new HashSet<Guid>();       // 记录访问过的节点 Id，防止死循环
    for (var cur = t; cur is not null && seen.Add(cur.Id);
         cur = cur.ParentTokenId is Guid pid ? all.FirstOrDefault(x => x.Id == pid) : null)
        yield return cur;
}
```

- `seen.Add(cur.Id)`：返回 `bool`——**首次加入返回 `true`，已存在返回 `false`**。所以一旦遇到重复节点（成环），`Add` 返回 `false`，循环条件不成立，自动停止。这是「用 HashSet 一石二鸟：既记录又判重」的经典技巧。

### `Queue<T>`：先进先出（FIFO），排队

```csharp
var q = new Queue<string>();
q.Enqueue("A"); q.Enqueue("B");   // 入队到尾部
var first = q.Dequeue();          // 出队从头部 → "A"
```
类比排队买票，先来的先服务。常用于 BFS 广度优先、任务队列。

### `Stack<T>`：后进先出（LIFO），叠盘子

```csharp
var s = new Stack<string>();
s.Push("A"); s.Push("B");   // 压栈
var top = s.Pop();          // 弹栈 → "B"（最后压的最先弹）
```
类比叠盘子，最后放上去的最先拿。常用于 DFS 深度优先、撤销（Undo）、括号匹配。

### `LinkedList<T>`：双向链表，中间插删 O(1)

每个节点存着「前一个」和「后一个」的指针。**在已知节点位置插入/删除是 O(1)**（改指针即可），但**按下标随机访问是 O(n)**（要从头走）。适合频繁在中间增删、很少随机访问的场景。

### 各集合复杂度总对比表（务必背下来）

| 集合 | 按索引访问 | 查找元素 | 头部插删 | 尾部插删 | 中间插删 | 特点/用途 |
|------|:---:|:---:|:---:|:---:|:---:|------|
| `T[]` 数组 | O(1) | O(n) | — | — | — | 定长，最快随机访问 |
| `List<T>` | O(1) | O(n) | O(n) | O(1)均摊 | O(n) | 最常用动态数组 |
| `Dictionary<K,V>` | — | **O(1)** | — | — | — | 键值查找 |
| `HashSet<T>` | — | **O(1)** | — | — | — | 去重/存在性 |
| `Queue<T>` | — | O(n) | O(1)出队 | O(1)入队 | — | FIFO 排队 |
| `Stack<T>` | — | O(n) | O(1) | O(1) | — | LIFO 栈 |
| `LinkedList<T>` | O(n) | O(n) | **O(1)** | **O(1)** | **O(1)**（已知节点） | 频繁中间增删 |

### 面试怎么问 + 参考答案

**Q：`List` 和 `LinkedList` 怎么选？**
> A：看访问模式。需要频繁按下标随机访问、或主要在尾部追加，用 `List<T>`（随机访问 O(1)，尾插均摊 O(1)）。需要频繁在集合中间/头部插入删除且很少随机访问，用 `LinkedList<T>`（已知节点处增删 O(1)，但随机访问要 O(n) 遍历）。实际项目里 `List<T>` 用得远多于 `LinkedList`，因为 CPU 缓存对连续数组更友好。

**Q：怎么快速给一批数据去重？**
> A：用 `HashSet<T>`，`Add` 是 O(1) 且重复元素自动被丢弃；或 LINQ 的 `.Distinct()`（内部也是哈希）。CP6 里 `TokenLineage` 就用 `HashSet<Guid>` 的 `Add` 返回值同时做「访问记录 + 防环」，一旦 `Add` 返回 false 说明成环立即停止。

---

## 2.2.4 只读集合接口（`IReadOnlyList<T>` 等）

### 概念讲解

有时你想把一个列表**交给别人看，但不许改**。返回 `List<T>` 的话对方能 `.Add` / `.Clear` 破坏你的内部状态。返回**只读接口**就能在编译期禁止修改。

| 接口 | 能力 | 说明 |
|------|------|------|
| `IEnumerable<T>` | 只能 `foreach` 遍历 | 最弱，连 Count 都没有（要遍历才知道） |
| `IReadOnlyCollection<T>` | 遍历 + `Count` | 加了个数 |
| `IReadOnlyList<T>` | 遍历 + Count + `[索引]` | 能按下标读，不能写 |
| `IReadOnlyDictionary<K,V>` | 只读字典 | 能查不能改 |

**CP6 真实用例**：`TokenLineage.AncestorChain` 的参数就是 `IReadOnlyList<Wf_FlowToken> all`——「我只读你这份 token 列表，保证不改」，把契约写进了类型里。`PagedResult<T>` 记录也用了 `IReadOnlyList<T> Rows`。

```csharp
// C:\CP6\CP6.Core\Services\Platform\ITenantAdminService.cs
public record PagedResult<T>(IReadOnlyList<T> Rows, int Total);
//                            ↑ 对外暴露只读，调用方不能篡改分页数据
```

### 常见坑

- `IReadOnlyList<T>` 只是**编译期只读视图**，不是「不可变副本」。如果底层还是同一个 `List<T>`，别人改底层它也会变。要真正不可变用 `ImmutableList<T>` 或 `.ToArray()` 拷贝一份。

### 面试怎么问 + 参考答案

**Q：方法返回集合，为什么有时返回 `IReadOnlyList<T>` 而不是 `List<T>`？**
> A：为了封装——用只读接口对外，调用方只能读不能 `Add`/`Remove`，避免外部代码意外破坏对象内部状态，契约更清晰。但要注意它是只读视图不是深拷贝，底层集合变了它也跟着变，需要真正隔离时得返回不可变集合或副本。

---

## 2.2.5 `ToDictionary` / `ToLookup`：把列表变查找表

### 概念讲解

从数据库查回一个 `List`，然后要「按某字段快速查」，就把它转成字典。CP6 里这招用得极多（避免在循环里反复遍历列表，把 O(n²) 降成 O(n)）。

- **`ToDictionary`**：一个 key 对**一个** value（key 必须唯一，重复会抛异常）。
- **`ToLookup`**：一个 key 对**一组** value（key 可重复，天然分组）。

### CP6 真实用例

`C:\CP6\CP6.Core\Services\Wms\OutboundService.cs`：

```csharp
// 按明细行号建索引，之后 O(1) 查每行已发数量
var shippedByLine = details.ToDictionary(d => d.LineNo, d => d.AllocatedQty - d.ShippedQty);
```

`C:\CP6\CP6.Core\Services\Wms\WmsStockQuery.cs`：

```csharp
// 先按库位分组，再转字典：key=库位, value=该库位的聚合信息
var stockByLoc = stockRows.GroupBy(s => s.LocationCd)
                          .ToDictionary(g => g.Key, g => new { /* 聚合 */ });
```

### 逐行解析

- `d => d.LineNo` 是 **keySelector**（用哪个字段当 key）；
- `d => d.AllocatedQty - d.ShippedQty` 是 **valueSelector**（value 存什么）；
- `GroupBy(...).ToDictionary(g => g.Key, ...)` 是先分组再转字典的常见组合。

### 常见坑

- **`ToDictionary` 的 key 重复** → 立刻抛 `ArgumentException: An item with the same key has already been added`。不确定 key 是否唯一时，改用 `ToLookup`（多值分组）或 `GroupBy`。

### 面试怎么问 + 参考答案

**Q：`ToDictionary` 和 `ToLookup` 区别？**
> A：`ToDictionary` 是一对一，key 必须唯一，重复直接抛异常；`ToLookup` 是一对多，同 key 的元素归为一组，key 天然可重复。当你确定 key 唯一、要 O(1) 精确查单条用 `ToDictionary`；当一个 key 对应多条记录（如按客户分组订单）用 `ToLookup`。二者都是把 List 转成查找结构，避免嵌套循环的 O(n²)。

---

## 2.2.6 并发集合 `ConcurrentDictionary` 一瞥

### 概念讲解

普通 `Dictionary` **不是线程安全**的——多个线程同时 `Add`/`Remove` 会导致内部结构损坏甚至死循环。多线程共享的字典要用 `System.Collections.Concurrent.ConcurrentDictionary<K,V>`，它内部用**分段锁**保证并发安全。

```csharp
var cache = new ConcurrentDictionary<string, int>();
cache.AddOrUpdate("hits", 1, (key, old) => old + 1);   // 原子的「加或更新」
int v = cache.GetOrAdd("config", k => LoadConfig(k));  // 原子的「有就取，没有就算并存」
```

- `GetOrAdd` / `AddOrUpdate` 是**原子操作**，避免「检查再操作」的竞态。
- Web 应用里单例服务的内存缓存、计数器常用它。

### 面试怎么问 + 参考答案

**Q：`Dictionary` 线程安全吗？多线程要用什么？**
> A：`Dictionary` 不是线程安全的，多线程并发写会破坏内部数据结构。多线程场景用 `ConcurrentDictionary`，它提供 `GetOrAdd`、`AddOrUpdate` 等原子方法，内部分段加锁，读多写少时性能也不错。如果只是偶尔加锁的简单场景，也可以自己用 `lock` 包普通字典，但优先用现成的并发集合。

---
---

# 2.3　委托与事件（Delegates & Events）

## 2.3.1 `delegate`：把「一个方法」当成变量传来传去

### 概念讲解（类比）

委托（Delegate）就是**「方法的遥控器」**。平常你传的是数据（数字、字符串），委托让你能传**一段行为**。类比：你雇了个助理，交给他一张便签写着「到点了就打这个电话」——便签上写的不是电话内容，而是「该执行哪个动作」。委托就是那张便签，它指向某个方法。

底层上，委托是一个**类型安全的函数指针**：它约定了「接收什么参数、返回什么」，任何签名匹配的方法都能被它指向。

```csharp
// 声明一个委托类型：指向「接收两个 int、返回 int」的方法
public delegate int MathOp(int a, int b);

int Add(int a, int b) => a + b;
int Mul(int a, int b) => a * b;

MathOp op = Add;          // 便签指向 Add
Console.WriteLine(op(3, 4));   // 输出 7，等于调用 Add(3,4)
op = Mul;                 // 换指向 Mul
Console.WriteLine(op(3, 4));   // 输出 12
```

### 为什么有用

委托让「调用方决定一部分逻辑」。回头看 2.1.3 的缓存标本：

```csharp
public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, ...) where T : class
```

`CacheService` 不知道「缓存没命中时该去哪捞数据」——它把这个决定权用 `Func<Task<T>> factory` 委托交给调用方。调用方传一段 lambda 进来，缓存服务在合适的时机执行它。这就是「**行为参数化**」。

---

## 2.3.2 `Func` / `Action` / `Predicate`：内置的三大委托类型

C# 早就帮你预定义好了通用委托，99% 情况不用自己 `delegate` 声明。

| 委托 | 有无返回值 | 签名 | 记忆 |
|------|------|------|------|
| `Action` | **无**返回（void） | `Action<T1,...,T16>` | Action=动作，做完不回话 |
| `Func` | **有**返回 | `Func<T1,...,TResult>`（最后一个是返回类型） | Func=函数，有返回值 |
| `Predicate<T>` | 返回 **bool** | `Predicate<T>`，等价 `Func<T,bool>` | Predicate=判断，是/否 |

```csharp
Action greet = () => Console.WriteLine("hi");         // 无参无返回
Action<string> log = msg => Console.WriteLine(msg);   // 一参无返回
Func<int, int, int> add = (a, b) => a + b;            // 两参，返回 int
Func<Task<int>> loadAsync = async () => await GetCountAsync();  // 无参，返回 Task<int>
Predicate<int> isEven = n => n % 2 == 0;              // 一参，返回 bool
```

> `Func<...>` 里**最后一个类型参数永远是返回值类型**，前面的都是入参。`Func<int, int, string>` = 接收两个 int，返回 string。这是最容易记混的点。

### CP6 真实用例：用 `Func` 委托做「路由分发表」

`C:\CP6\CP6.Core\Services\Integration\IntegrationEventDispatcher.cs`——把「事件类型」映射到「处理逻辑」，逻辑本身是委托：

```csharp
private static readonly Dictionary<string, Func<DispatchContext, Task<bool>>> Routes = new()
{
    [RouteKey("ERP", "MES", "OnOrderCreatedAsync")] = async ctx =>
    {
        var p = ctx.GetPayload<OnOrderCreatedPayload>();
        var r = await ctx.Mes.OnOrderCreatedAsync(p.WebOrderNo, p.UserName);
        return r.Success;
    },
    [RouteKey("MES", "WMS", "OnWorkOrderIssuedAsync")] = async ctx =>
    {
        var p = ctx.GetPayload<OnWorkOrderIssuedPayload>();
        var r = await ctx.Wms.OnWorkOrderIssuedAsync(p.WorkOrderNo, p.UserName);
        return r.Success;
    },
    // ...更多路由
};
```

### 逐行解析

- `Dictionary<string, Func<DispatchContext, Task<bool>>>`：value 类型是**委托** `Func<DispatchContext, Task<bool>>`——接收一个 `DispatchContext`，返回 `Task<bool>`（异步、返回成功与否）。
- `[RouteKey(...)] = async ctx => { ... }`：字典的每个 value 是一段 **async lambda**，就是具体的处理逻辑。
- **好处**：来一个事件，`Routes[key](ctx)` 一步查表 + 执行，取代了一大坨 `if/else` 或 `switch`。这是「用委托字典替代分支」的高级技巧，面试聊到「怎么消灭大量 if-else」时可以举这个例子。

### 面试怎么问 + 参考答案

**Q：`Func` 和 `Action` 区别？`Predicate` 呢？**
> A：`Action` 没有返回值（返回 void），`Func` 有返回值且最后一个泛型参数是返回类型，`Predicate<T>` 是返回 bool 的特化，等价 `Func<T,bool>`，常用于筛选判断。CP6 里 `IntegrationEventDispatcher` 用 `Dictionary<string, Func<DispatchContext, Task<bool>>>` 把事件路由到处理委托，避免了长长的 switch。

---

## 2.3.3 Lambda 表达式的演变：从匿名方法到 `=>`

### 语法演变三阶段

```csharp
// 阶段① 具名方法（最啰嗦）
bool IsEven(int n) { return n % 2 == 0; }
list.Where(IsEven);

// 阶段② 匿名方法（C# 2.0，delegate 关键字）
list.Where(delegate(int n) { return n % 2 == 0; });

// 阶段③ Lambda 表达式（C# 3.0 至今，主流写法）
list.Where(n => n % 2 == 0);        // 表达式 lambda（单表达式，自动 return）
list.Where(n => { return n % 2 == 0; });   // 语句 lambda（带花括号，要显式 return）
```

- `=>` 读作「goes to」，左边是参数，右边是方法体。
- 单个参数可省括号：`n => ...`；无参或多参要括号：`() => ...`、`(a, b) => ...`。
- CP6 里 `x => !x.IsDeleted`、`ctx => { ... }` 满屏都是。

---

## 2.3.4 闭包（Closure）与循环变量捕获的坑（高频陷阱题）

### 概念讲解

Lambda 能「记住」它外层的变量，这叫**闭包（Closure）**——捕获的不是变量的**值**，而是变量本身（引用）。

```csharp
int factor = 10;
Func<int, int> multiply = x => x * factor;   // 捕获了外层的 factor
factor = 20;
Console.WriteLine(multiply(5));   // 输出 100 还是 50？→ 100！捕获的是变量，执行时 factor 已是 20，5*20
```

### 经典陷阱：循环变量捕获

```csharp
// ❌ C# 5.0 之前 / 用 for 的老坑
var actions = new List<Action>();
for (int i = 0; i < 3; i++)
    actions.Add(() => Console.WriteLine(i));   // 三个 lambda 都捕获同一个 i
foreach (var a in actions) a();
// 输出：3 3 3   ——不是 0 1 2！因为循环结束时 i 已经是 3，三个闭包共享同一个 i
```

**修复**：在循环内建一个「每轮独立」的局部变量：

```csharp
for (int i = 0; i < 3; i++)
{
    int copy = i;                              // 每轮新建一份，各自捕获
    actions.Add(() => Console.WriteLine(copy));
}
// 输出：0 1 2 ✅
```

> **重要更新**：C# 5.0 起，**`foreach` 的循环变量**每轮都是独立的，`foreach` 里捕获**不会**踩这个坑；但 **`for` 循环变量**仍然是共享的，`for` 里捕获依然要小心。面试问到通常指 `for` 的场景。

### 常见坑

- 闭包会**延长被捕获变量的生命周期**——变量本该出栈却因被 lambda 引用而留在堆上，量大时注意内存。
- 在异步/多线程里捕获循环变量，几乎必踩「都是最后一个值」的坑。

### 面试怎么问 + 参考答案

**Q：什么是闭包？循环里用 lambda 捕获变量有什么坑？**
> A：闭包是 lambda 捕获并记住外层作用域变量的能力，捕获的是变量本身而非当时的值，所以执行时读到的是变量的最新值。经典坑是在 `for` 循环里创建多个 lambda 都捕获同一个循环变量，循环结束后它们读到的都是最终值（比如都输出 3 而不是 0/1/2）。解决办法是在循环体内声明一个局部变量拷贝当前值再捕获。注意 C# 5.0 起 `foreach` 变量每轮独立不会踩坑，但 `for` 变量仍共享。

---

## 2.3.5 多播委托（Multicast Delegate）与 `event`

### 多播委托

一个委托可以用 `+=` 挂**多个**方法，调用一次全部执行——这叫多播。

```csharp
Action pipeline = () => Console.WriteLine("step1");
pipeline += () => Console.WriteLine("step2");   // 追加
pipeline += () => Console.WriteLine("step3");
pipeline();   // 依次输出 step1 step2 step3
pipeline -= /* 某方法 */;   // 也能移除
```

> 坑：如果多播委托里的方法**有返回值**（`Func`），最终只拿得到**最后一个**方法的返回值，前面的都被丢弃。所以多播基本只配 `Action`。

### `event`：发布-订阅模式（Publish-Subscribe）

`event` 是对委托的**封装**，专门用于「事件通知」。它在多播委托基础上加了保护：**外部只能 `+=` 订阅 / `-=` 取消订阅，不能直接赋值（`=`）覆盖、也不能替发布者触发**。

```csharp
public class OrderService
{
    // 声明事件：订单创建后通知别人
    public event Action<Order>? OrderCreated;

    public void Create(Order order)
    {
        // ...保存订单...
        OrderCreated?.Invoke(order);   // 触发事件（?. 防止没人订阅时的 null）
    }
}

// 订阅方
var svc = new OrderService();
svc.OrderCreated += o => Console.WriteLine($"发邮件通知：{o.Id}");
svc.OrderCreated += o => Console.WriteLine($"更新库存：{o.Id}");
// svc.OrderCreated = null;   // ❌ 外部不允许，event 保护了它
```

- **发布者（Publisher）**：`OrderService`，它拥有并触发事件。
- **订阅者（Subscriber）**：外部代码，用 `+=` 登记「事件发生时通知我」。
- `OrderCreated?.Invoke(...)`：`?.` 保证没人订阅（委托为 null）时不报空引用。

> CP6 是 Web API 项目，进程内事件通知的角色由**集成事件分发器**（`IntegrationEventDispatcher`，2.3.2）等机制承担——它用委托字典把「ERP 下单」路由到「通知 MES/WMS」，本质就是发布-订阅思想的持久化落地版。

### 事件在桌面开发（WinForm/WPF）里的角色

岗位 JD 提到「客户端程序」，桌面 UI 就是**事件驱动**的活字典：

```csharp
// WinForm：按钮点击事件
button1.Click += (sender, e) => MessageBox.Show("保存成功");
//         ↑event  ↑标准事件签名 (object sender, EventArgs e)

// WPF：同理
saveButton.Click += SaveButton_Click;
void SaveButton_Click(object sender, RoutedEventArgs e) { /* ... */ }
```

- 桌面控件的 `Click`、`TextChanged`、`SelectionChanged` 全是 `event`。
- **标准事件签名**是 `(object sender, EventArgs e)`：`sender` 是触发源（哪个按钮），`e` 携带事件数据。委托类型通常是 `EventHandler` 或 `EventHandler<TEventArgs>`。
- **MVVM**（WPF 主流架构）里的「命令绑定」「`INotifyPropertyChanged.PropertyChanged` 事件」也都是事件机制——数据变了触发 `PropertyChanged`，UI 自动刷新。

### 面试怎么问 + 参考答案

**Q：`delegate` 和 `event` 什么关系？为什么不直接用委托字段？**
> A：`event` 是对委托的封装，专用于发布-订阅。区别在访问控制：普通委托字段外部能用 `=` 直接覆盖，甚至替对象触发它；而 `event` 对外只暴露 `+=`/`-=` 订阅和退订，赋值和触发只能在声明它的类内部进行。这样发布者独占「何时触发」的控制权，订阅者只管登记回调，封装更安全。桌面开发里按钮 `Click`、WPF 的 `PropertyChanged` 都是事件。

**Q：多播委托里如果方法有返回值会怎样？**
> A：只能拿到最后一个方法的返回值，前面所有方法的返回值都被丢弃。所以多播一般只用于无返回值的 `Action`/事件通知场景，需要收集每个结果时应手动遍历 `GetInvocationList()` 逐个调用。

---
---

# 2.4　异常处理体系（Exception Handling）

## 2.4.1 异常类层次结构

### 概念讲解（类比）

异常（Exception）是「程序运行时出岔子的报警器」。所有异常都是一棵**继承树**，根是 `System.Exception`。

```
System.Object
  └─ System.Exception                     ← 所有异常的根
       ├─ SystemException                 ← CLR/运行时抛的（系统级）
       │    ├─ NullReferenceException     空引用（用了 null）
       │    ├─ IndexOutOfRangeException   数组越界
       │    ├─ InvalidOperationException  对象状态不对时的操作
       │    │    └─ InsufficientStockException  ← CP6 自定义（库存不足）
       │    ├─ ArgumentException          参数不合法
       │    │    ├─ ArgumentNullException 参数为 null
       │    │    └─ ArgumentOutOfRangeException 参数越界
       │    └─ ...
       └─ ApplicationException            ← 早期设计给「应用自定义异常」的基类（现已不推荐）
```

- **`SystemException`**：CLR 运行时自己抛的（空引用、越界、类型转换失败等）。
- **`ApplicationException`**：微软最初想让开发者自定义异常都继承它，**但现在官方明确不推荐**——直接继承 `Exception` 或更贴切的 `InvalidOperationException`/`ArgumentException` 即可。CP6 就是这么做的（见下）。

### `Exception` 的关键成员

| 成员 | 含义 |
|------|------|
| `Message` | 人能读的错误描述 |
| `StackTrace` | 出错的调用栈（哪一行、经过哪些方法） |
| `InnerException` | 内层异常（包裹底层异常时用） |
| `Data` | 附带的键值对信息 |

---

## 2.4.2 `try` / `catch` / `finally` / `when`

### 基本结构

```csharp
try
{
    // 可能出错的代码
}
catch (SpecificException ex) when (ex.Code == "X")  // 异常过滤器：条件满足才进这个 catch
{
    // 处理特定异常
}
catch (Exception ex)
{
    // 兜底处理
}
finally
{
    // 无论有没有异常，都一定执行（释放资源、清理）
}
```

- `catch` 要**从具体到宽泛**排列：先 `catch 具体异常`，最后 `catch (Exception)` 兜底。反了会编译报错（更具体的永远进不去）。
- `finally` **一定执行**（除非进程被强杀），用于释放资源。
- `when` 是**异常过滤器（Exception filter，C# 6.0）**：只有条件为 true 才捕获，否则继续往上抛。好处是**不进 catch 块就不破坏原始堆栈**。

### CP6 真实用例：控制器里的异常分类捕获

`C:\CP6\CP6.WebApi\Controllers\Wms\StockController.cs` 的 `Apply` 端点：

```csharp
[HttpPost("apply")]
[RequirePermission("wms-stock", "adjust")]
public async Task<IActionResult> Apply([FromBody] StockMovementRequest req, CancellationToken ct)
{
    req.OperatorCd ??= CurrentUser;
    try
    {
        var txnNo = await _mover.ApplyAsync(req, ct);
        return Ok(new { code = 0, message = "WM-MSG-071", data = new { txnNo } });
    }
    catch (InsufficientStockException ex)     // ① 业务异常：库存不足
    {
        return BadRequest(new { code = 400, message = ex.Message });
    }
    catch (ArgumentException ex)              // ② 参数异常：请求不合法
    {
        return BadRequest(new { code = 400, message = ex.Message });
    }
}
```

### 逐行解析

- **按异常类型分开 catch**：库存不足（`InsufficientStockException`）和参数错误（`ArgumentException`）都返回 400，但语义清晰、便于将来分别处理（比如库存不足要记录预警）。
- 没被这两个 catch 捕获的其他异常（如数据库崩了）**会继续往上抛**，交给全局异常中间件统一处理成 500——这是「**业务异常在控制器处理，系统异常交给全局**」的分层策略（见 2.4.5）。
- `ex.Message` 就是异常的描述文本，回给前端展示。

---

## 2.4.3 自定义异常最佳实践

### CP6 真实标本

`C:\CP6\CP6.Core\Services\Wms\InsufficientStockException.cs`：

```csharp
namespace CP6.Core.Services.Wms;

/// <summary>
/// 在庫不足例外。OUT/RSV 時に AvailableQty が要求量を下回り、
/// かつ Warehouse.AllowNegative=false の場合に投げられる。
/// API 層は 400 + "WM-MSG-040" を返す。
/// </summary>
public class InsufficientStockException : InvalidOperationException
{
    public string ProductCd { get; }
    public string LotNo { get; }
    public decimal Requested { get; }
    public decimal Available { get; }

    public InsufficientStockException(string productCd, string lotNo, decimal requested, decimal available)
        : base($"WM-MSG-040: 製品[{productCd}]ロット[{lotNo}]の利用可能在庫が不足しています。必要={requested}, 在庫={available}")
    {
        ProductCd = productCd;
        LotNo = lotNo;
        Requested = requested;
        Available = available;
    }
}
```

### 逐行解析 + 为什么这是「最佳实践」

| 做法 | 好处 |
|------|------|
| 继承 `InvalidOperationException` 而非 `ApplicationException` | 遵循现代 .NET 官方建议，`InvalidOperationException` 语义贴切（对象状态不允许该操作） |
| 携带业务字段 `ProductCd` / `Requested` / `Available` | catch 方能拿到结构化数据（哪个产品、缺多少），而不只是一句文本 |
| 属性是**只读**（只有 `get`） | 异常一旦创建不可篡改 |
| 构造函数把细节拼进 `base(message)` | `ex.Message` 直接是完整可读信息，含错误码 `WM-MSG-040` |
| 类名以 `Exception` 结尾 | .NET 命名约定 |

### 常见坑

- **自定义异常忘了调 `base(message)`** → `ex.Message` 变成默认的一句废话，前端拿不到有用信息。
- **用异常控制正常流程**：异常开销大（要抓堆栈），不该用它做「if 判断」。库存不足在 CP6 里是「真正的异常路径」才抛，不是每次都走。

### 面试怎么问 + 参考答案

**Q：自定义异常怎么写？为什么不继承 `ApplicationException`？**
> A：继承 `Exception` 或更贴切的内置异常（如状态非法用 `InvalidOperationException`、参数错用 `ArgumentException`），类名以 `Exception` 结尾，通过构造函数把可读信息传给 `base(message)`，并用只读属性携带结构化的业务字段方便上层处理。不继承 `ApplicationException` 是因为它是早期设计遗留，微软官方已明确不推荐，它没带来任何额外价值。CP6 的 `InsufficientStockException` 就继承 `InvalidOperationException`，携带产品、需求量、库存量三个字段。

---

## 2.4.4 `throw` vs `throw ex`：堆栈丢失的血案（必考演示）

### 概念讲解

catch 到异常后想「记个日志再抛出去」，有两种写法，**差别巨大**：

```csharp
try { DoWork(); }
catch (Exception ex)
{
    Log(ex);
    throw;       // ✅ 正确：重新抛出，保留原始堆栈（出错的真正位置）
    // throw ex; // ❌ 错误：把堆栈起点重置成当前这行，原始出错位置丢失！
}
```

### 演示对比

假设异常真正发生在 `DoWork` 内部的第 88 行：

```
用 throw;      → StackTrace 指向：DoWork 第88行 → 这里的 catch   （完整链路，能定位真凶）
用 throw ex;   → StackTrace 指向：这里的 catch 那一行             （原始 88 行没了，排查抓瞎）
```

- `throw;`（光秃秃的 throw）：**重新抛出当前异常**，堆栈信息原封不动。
- `throw ex;`：被 CLR 当成「抛一个新异常」，**堆栈跟踪从当前行重新开始**，原始出错点被抹掉——线上排查最痛恨这个。

> 记忆口诀：**「要重抛，光 `throw`；带 `ex`，坑自己」**。

### 如果要包一层（保留原始异常）

```csharp
catch (SqlException ex)
{
    throw new DataAccessException("查询订单失败", ex);   // 把原异常作为 InnerException 传入
    //                                          ↑ 原始堆栈通过 InnerException 保留
}
```

### 面试怎么问 + 参考答案

**Q：`throw` 和 `throw ex` 有什么区别？（几乎必考）**
> A：`throw;` 是重新抛出当前捕获的异常，完整保留原始的堆栈跟踪，能定位到最初出错的那一行。`throw ex;` 会重置堆栈跟踪的起点为当前抛出这一行，原始出错位置就丢失了，线上排查会非常困难。所以在 catch 里想把异常继续往上传，永远用 `throw;`。如果需要转换异常类型，就 `throw new XxxException(msg, ex)` 把原异常作为 InnerException 传进去，这样原始堆栈也不丢。

---

## 2.4.5 异常过滤器 `catch...when` 与全局异常处理

### `catch ... when`（异常过滤器）

```csharp
try { CallApi(); }
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyExceptions)
{
    await RetryLater();   // 只在 429 时重试
}
catch (HttpRequestException ex) when (ex.StatusCode >= HttpStatusCode.InternalServerError)
{
    Alert();              // 5xx 才告警
}
```

- `when(条件)` 为 false 时，**这个 catch 直接跳过**，异常继续找下一个 catch 或往上抛——而且**不会展开堆栈**，比「进 catch 再 if 判断然后 `throw;`」更干净、更利于调试。

### 全局异常处理思路（ASP.NET Core）

不可能每个控制器都写 try-catch 兜底。Web 项目在**管道最外层**装一个异常处理中间件，统一把「漏网的异常」转成规范的 HTTP 响应：

```csharp
// 思路示意（ASP.NET Core Program.cs / 中间件）
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    ctx.Response.StatusCode = ex switch
    {
        InsufficientStockException => 400,   // 业务异常 → 400
        UnauthorizedAccessException => 403,
        _ => 500                              // 其他 → 500
    };
    await ctx.Response.WriteAsJsonAsync(new { message = ex?.Message });
}));
```

- CP6 的做法（见记忆里的架构记录）：Core 层抛携带错误码的异常，WebApi 边界统一转成 `BizException` 做本地化（如 `E-SEC-032`），中间件兜底 5xx。

---

## 2.4.6 业务异常 vs 系统异常的分层策略

| | 业务异常（Business/Domain Exception） | 系统异常（System Exception） |
|------|------|------|
| 例子 | 库存不足、余额不够、审批已撤回 | 空引用、数据库超时、网络断开 |
| CP6 例子 | `InsufficientStockException` | `NullReferenceException`、`DbUpdateException` |
| 语义 | 「业务规则不允许」，**可预期** | 「程序/环境出错」，**非预期** |
| 处理位置 | 控制器就地 catch，转成 400 + 友好提示 | 交给全局中间件，转成 500 + 记录告警 |
| 要不要告警 | 一般不告警（正常业务拒绝） | 要告警/记日志（真出故障了） |
| 前端展现 | 直接展示给用户看 | 展示「系统繁忙」，细节只进日志 |

回看 `StockController.Apply`：它专门 catch 了两个**业务异常**（`InsufficientStockException`、`ArgumentException`）转 400，其余**系统异常**故意不 catch，让它们冒泡到全局处理成 500。这就是教科书级的分层。

### 面试怎么问 + 参考答案

**Q：项目里异常怎么分层处理？**
> A：分业务异常和系统异常。业务异常是可预期的规则拒绝（库存不足、余额不足），在控制器里按类型 catch，转成 400 加友好的错误码/文案直接返回给用户，通常不告警。系统异常是非预期的程序或环境故障（空引用、数据库超时），控制器不去 catch，让它冒泡到全局异常中间件，统一转成 500、记录日志并告警，对用户只显示「系统繁忙」。CP6 的 `StockController` 就是只 catch 业务异常转 400，系统异常交给全局中间件。

---
---

# 2.5　可空引用类型（Nullable Reference Types）完整篇

## 2.5.1 `#nullable enable`：让编译器帮你抓空引用

### 概念讲解（类比）

`NullReferenceException`（空引用异常，简称 NRE）是 .NET 头号线上杀手——「对一个 null 变量点方法」。C# 8.0 引入**可空引用类型（NRT）**：打开开关后，编译器把引用类型分成两类，帮你在**编译期**就把潜在的 null 问题标出来。

- `string name`：**不可空**——编译器认为它「不该是 null」，给它赋 null 会警告。
- `string? name`：**可空**——明确告诉编译器「它可能是 null」，用之前必须判空，否则警告。

类比：`string` 是「保证有货的快递柜」，`string?` 是「可能是空的柜子，开之前先敲一下」。

### 怎么开启

```xml
<!-- .csproj 里，对整个项目生效（CP6 就是这么开的） -->
<Nullable>enable</Nullable>
```

```csharp
// 或在单个文件顶部
#nullable enable
```

### 回看 CP6 标本里的 `?`

```csharp
// BaseEntity.cs —— 可空和不可空的对比
public Guid Id { get; set; }              // 值类型，主键，非空
[MaxLength(100)] public string? Creator { get; set; }   // string? 创建人「可能没填」
public DateTime? ModifyDate { get; set; } // DateTime? 「可能还没改过」

// IRepository.cs
Task<T?> FindAsync(Guid id);              // T? —— 查不到会返回 null，调用方必须判空
```

`Task<T?>` 这个 `?` 是在明确告诉调用方：「我可能返回 null，你敢不判空直接用，编译器就警告你」。

---

## 2.5.2 空值相关运算符全家（务必全记）

| 运算符 | 名称 | 作用 | 例子 |
|------|------|------|------|
| `?.` | 空条件运算符（null-conditional） | 左边为 null 就整体返回 null，不炸 | `User?.Identity?.Name` |
| `??` | 空合并（null-coalescing） | 左边为 null 就取右边 | `name ?? "匿名"` |
| `??=` | 空合并赋值（null-coalescing assignment） | 左边为 null 才赋值 | `req.OperatorCd ??= CurrentUser` |
| `!` | 空值宽容（null-forgiving） | 「我保证它不为 null」，压制警告 | `config!.Value` |
| `?[]` | 空条件索引 | 数组/字典为 null 不炸 | `list?[0]` |

### CP6 真实用例（一次看全四个）

`C:\CP6\CP6.WebApi\Controllers\Wms\StockController.cs`：

```csharp
// ① ?. 空条件：User 或 Identity 任一为 null，整体就是 null，不抛异常
private string? CurrentUser => User?.Identity?.Name;

// ② ??= 空合并赋值：OperatorCd 没传（null）才用当前登录用户填上
req.OperatorCd ??= CurrentUser;
```

`C:\CP6\CP6.Core\Services\Erp\UnshippedOrderService.cs`：

```csharp
// ?? 空合并：CustomerName 为 null 就用空字符串，避免 CSV 里出现 "null"
item.CustomerName ?? string.Empty,
FormatDate(value)  // 内部：value?.ToString(...) ?? string.Empty  —— ?. 和 ?? 连用
```

`C:\CP6\CP6.Core\Services\Fin\BudgetDtos.cs`：

```csharp
// ?? 给可空 TimeSpan 兜底
SlidingExpiration = expiration ?? TimeSpan.FromMinutes(30)
```

### 逐行解析（重点看 `??=`）

```csharp
req.OperatorCd ??= CurrentUser;
// 等价于：
if (req.OperatorCd == null) req.OperatorCd = CurrentUser;
```

含义：调用方如果在请求里指定了操作人就用它；没指定（null）就自动填成当前登录用户。一行代码搞定「默认值兜底」。

### 常见坑

1. **滥用 `!`（空值宽容）**：`config!.Value` 是在跟编译器打包票「绝不为 null」。你打错包票，运行时照样 NRE，而且编译器已经不再警告你了。`!` 是「我确实知道它不为 null，请闭嘴」，不是「消除警告的万能胶」。
2. **`?.` 的链式返回值是可空的**：`User?.Identity?.Name` 的类型是 `string?`，后面还想用要继续判空或 `?? 默认值`。
3. **可空值类型 vs 可空引用类型**：`int?` 是老早就有的 `Nullable<int>`（值类型），`string?` 是 C# 8 的编译期标注（引用类型本来就能 null，`?` 只是给编译器看的注解，运行时没区别）。

### 编译器流分析（Flow Analysis）

编译器会**跟踪代码流**判断某处变量到底可不可能为 null：

```csharp
void Print(string? s)
{
    // Console.WriteLine(s.Length);   // ⚠️ 警告：s 可能为 null
    if (s != null)
        Console.WriteLine(s.Length);  // ✅ 这里编译器知道 s 一定不为 null，不警告
    if (string.IsNullOrEmpty(s)) return;
    Console.WriteLine(s.Length);      // ✅ 提前 return 后，编译器也推断出 s 非 null
}
```

这就是流分析：判空之后的分支里，编译器自动把 `string?` **收窄**成非空，不再警告。

---

## 2.5.3 迁移旧项目的策略

老项目一打开 `<Nullable>enable</Nullable>` 会冒出成百上千个警告。策略：

1. **逐文件/逐模块开启**：用 `#nullable enable` 只在新写的文件顶部开，老文件保持原样，增量迁移。
2. **先当警告不当错误**：`<Nullable>enable</Nullable>` 是警告不阻断编译；等清干净了再考虑 `<WarningsAsErrors>Nullable</WarningsAsErrors>`。
3. **从底层（实体、DTO）往上**：先把数据模型的 null 语义标准确（哪些字段真能空），上层自然跟着清晰。
4. **别用 `!` 图省事**：`!` 只压制警告不解决问题，滥用等于假装迁移完了。

### 面试怎么问 + 参考答案

**Q：`?.` `??` `??=` 分别是什么？**
> A：`?.` 是空条件运算符，左边为 null 就短路返回 null 不抛异常，常用于 `a?.b?.c` 链式访问；`??` 是空合并，左边为 null 时取右边的默认值；`??=` 是空合并赋值，只有左边为 null 时才赋值。CP6 的 `StockController` 里 `User?.Identity?.Name` 用 `?.` 安全取登录名，`req.OperatorCd ??= CurrentUser` 用 `??=` 在未指定操作人时兜底成当前用户。

**Q：可空引用类型是运行时特性吗？`string?` 和 `string` 运行时有区别吗？**
> A：不是运行时特性，是编译期的静态分析注解。`string?` 和 `string` 在运行时是**同一个类型**（引用类型本来就能存 null），`?` 只是告诉编译器「这里允许 null，请对未判空的使用发警告」。它靠编译器的流分析在编译期帮你发现潜在的空引用，不改变运行时行为，也没有性能开销。这和值类型的 `int?`（真的是 `Nullable<int>` 结构体）本质不同。

---
---

# 2.6　字符串专题（String）

## 2.6.1 不可变性（Immutability）与字符串驻留（String Interning）

### 概念讲解（类比）

C# 的 `string` 是**不可变（immutable）**的：一旦创建，内容永不改变。所有看起来「改字符串」的操作（拼接、替换、大写）其实都是**新建一个字符串**，原来的原封不动。

类比：字符串像**刻好的印章**，想改字不是打磨旧印章，而是刻一枚新的。

```csharp
string s = "hello";
s = s + " world";   // 不是改 "hello"，而是新建 "hello world"，让 s 指向它
                    // 原来的 "hello" 变成垃圾等待回收
s.ToUpper();        // ⚠️ 常见坑：返回了大写的新串，但没接收，s 本身没变！
s = s.ToUpper();    // ✅ 要重新赋值
```

### 为什么设计成不可变

- **线程安全**：多个线程读同一个字符串永远安全（没人能改它）。
- **可作字典 key**：内容不变，哈希码就稳定（回顾 2.2.2 的 key 要求）。
- **驻留优化**：见下。

### 字符串驻留（String Interning）

CLR 维护一个「**驻留池（intern pool）**」。**编译期的字符串字面量**相同的会共享同一个对象：

```csharp
string a = "abc";
string b = "abc";
Console.WriteLine(ReferenceEquals(a, b));   // True！编译期字面量被驻留，指向同一对象

string c = new string("abc".ToCharArray());
Console.WriteLine(ReferenceEquals(a, c));   // False！运行时构造的不自动驻留
Console.WriteLine(ReferenceEquals(a, string.Intern(c)));  // True，手动驻留后相等
```

- 好处：省内存（相同字面量只存一份）。
- 注意：**运行时拼出来的字符串默认不驻留**，需要 `string.Intern()` 手动加入。

### 常见坑

1. **循环里用 `+=` 拼字符串**：每次都新建一个字符串 + 复制，n 次拼接是 O(n²)，量大时性能灾难 → 用 `StringBuilder`（下节）。
2. **忘了接收返回值**：`s.Trim()`、`s.Replace()` 都返回新串，不改原串。
3. **用 `==` 比较字符串是比内容还是引用？** C# 特意重载了 `string` 的 `==` 比**内容**（不是引用），这点和 Java 的 `==` 比引用不同，是常考陷阱。

---

## 2.6.2 `StringBuilder`：可变字符串缓冲区

### 概念讲解

要拼**很多次**字符串（循环、拼 CSV/SQL/报文），用 `StringBuilder`——它内部维护一个**可变的字符缓冲区**，`Append` 直接往里写，不每次新建对象。最后 `ToString()` 一次性生成结果。

| | 字符串 `+=` 拼接 | `StringBuilder` |
|------|------|------|
| 每次操作 | 新建字符串 + 复制（O(n)） | 往缓冲区追加（均摊 O(1)） |
| 拼 n 次总复杂度 | O(n²) | O(n) |
| 适用 | 少量、固定几次拼接 | 循环、大量、次数不定 |

> 经验法则：**编译期就知道的固定几段**（`"a" + b + "c"`，编译器会优化）用 `+` 没问题；**循环里/次数不定**的拼接用 `StringBuilder`。

### CP6 真实用例：导出 CSV

`C:\CP6\CP6.Core\Services\Erp\UnshippedOrderService.cs`：

```csharp
private static byte[] BuildCsv(List<UnshippedOrderItemDto> items)
{
    var sb = new StringBuilder();            // ← 可变缓冲区
    AppendCsvRow(sb, new[] { "WebOrderNo", "CustomerCd", /* ...表头... */ });

    foreach (var item in items)              // ← 行数不定，正是 StringBuilder 的主场
    {
        AppendCsvRow(sb, new[]
        {
            item.WebOrderNo,
            item.CustomerName ?? string.Empty,
            // ...
        });
    }

    var body = Encoding.UTF8.GetBytes(sb.ToString());   // ← 最后一次性转成字符串
    // ...加 UTF-8 BOM...
    return bytes;
}

private static void AppendCsvRow(StringBuilder sb, IEnumerable<string> fields)
{
    sb.Append(string.Join(",", fields.Select(EscapeCsv)));   // 拼一行
    sb.Append("\r\n");                                        // 换行
}
```

### 逐行解析

- `new StringBuilder()`：创建空缓冲区（也可 `new StringBuilder(capacity)` 预估容量，进一步减少内部扩容）。
- `sb.Append(...)`：往缓冲区追加，返回 `sb` 自身，支持链式 `sb.Append(a).Append(b)`。
- `foreach` 逐行 `Append`——**行数不确定**，用 `+=` 会 O(n²)，`StringBuilder` 是 O(n)，这就是选它的理由。
- `sb.ToString()`：最后一次性把缓冲区变成不可变字符串。

### 面试怎么问 + 参考答案

**Q：什么时候用 `StringBuilder`？为什么？**
> A：需要在循环里或不确定次数地反复拼接字符串时用。因为 `string` 不可变，每次 `+=` 都会新建字符串并复制已有内容，拼 n 次是 O(n²)，产生大量临时对象加重 GC。`StringBuilder` 内部是可变缓冲区，`Append` 均摊 O(1)，总共 O(n)，最后 `ToString()` 一次成型。CP6 导出 CSV 就是用 `StringBuilder` 逐行 `Append`。反过来，只拼固定几段（如 `"a"+b+"c"`）用 `+` 更简洁，编译器还会帮你优化。

---

## 2.6.3 字符串格式化全法

| 写法 | 例子 | 说明 |
|------|------|------|
| 字符串插值（Interpolation，C# 6） | `$"缺 {requested}，有 {available}"` | 最常用、最易读，编译期展开 |
| `string.Format` | `string.Format("缺 {0}，有 {1}", req, avail)` | 老式，占位符按序号 |
| 复合格式化 + 格式说明符 | `$"{amount:C}"`、`$"{date:yyyy-MM-dd}"` | 冒号后是格式串 |
| 拼接 `+` | `"缺 " + req` | 少量可用 |
| `string.Join` | `string.Join(",", fields)` | 拼集合，自动加分隔符 |

### CP6 真实用例

```csharp
// 插值 —— InsufficientStockException 的构造函数
: base($"WM-MSG-040: 製品[{productCd}]ロット[{lotNo}]の...必要={requested}, 在庫={available}")

// 格式说明符 —— UnshippedOrderService
value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)   // 日期格式
value.ToString("0.########", CultureInfo.InvariantCulture)    // 数字格式

// string.Join —— AppendCsvRow
sb.Append(string.Join(",", fields.Select(EscapeCsv)));
```

- `$"...{x}..."` 里花括号内直接放变量/表达式，`:` 后跟格式说明符（`C`=货币、`yyyy-MM-dd`=日期、`0.##`=数字）。
- 注意 CP6 导出时特意传了 `CultureInfo.InvariantCulture`——**避免不同区域把小数点变成逗号、日期格式乱套**（见下节文化敏感）。

---

## 2.6.4 常用 API 与 `StringComparison` 文化敏感陷阱

### 常用方法速查

| 方法 | 作用 | 注意 |
|------|------|------|
| `Split(',')` | 按分隔符拆成数组 | 可传 `StringSplitOptions.RemoveEmptyEntries` 去空段 |
| `string.Join(",", arr)` | 用分隔符拼集合 | CSV/日志常用 |
| `Trim()` / `TrimStart` / `TrimEnd` | 去首尾空白 | 返回新串 |
| `Contains` / `StartsWith` / `EndsWith` | 包含/前缀/后缀判断 | 默认区域敏感，见下 |
| `IndexOf` / `Replace` | 查位置 / 替换 | `IndexOf` 找不到返回 -1 |
| `string.IsNullOrEmpty` / `IsNullOrWhiteSpace` | 判空 | CP6 里筛选条件满天飞 |
| `ToUpper` / `ToLower` | 大小写转换 | 返回新串，别忘接收 |

CP6 里 `string.IsNullOrWhiteSpace` 用得极多，如 `StockController.Search`：

```csharp
if (!string.IsNullOrWhiteSpace(warehouseCd)) q = q.Where(x => x.WarehouseCd == warehouseCd);
```

### `StringComparison` 文化敏感陷阱（考点）

`StartsWith`、`Contains`、`Equals`、`ToUpper` 默认可能**依赖当前线程的区域文化（Culture）**，会导致：

- **土耳其 i 问题**：土耳其语里 `"I".ToLower()` 不是 `"i"`。一个「不区分大小写比较用户名」的逻辑，部署到土耳其区域机器上就出诡异 bug。
- 性能：文化敏感比较比序数（Ordinal）比较慢。

**规则**：
- 面向**程序内部**的比较（key、枚举值、代号、协议字段）→ 用 **`StringComparison.Ordinal` / `OrdinalIgnoreCase`**（按字节序，快且稳定）。
- 面向**给人看的排序展示**（界面列表排序）→ 才用文化敏感的 `CurrentCulture`。

```csharp
// ✅ 内部代号比较，用 Ordinal（不受区域影响）
if (txnType.Equals("OUT", StringComparison.OrdinalIgnoreCase)) { ... }

// CP6 FlowEngine 建字典时指定 OrdinalIgnoreCase 比较器（回顾 2.2.2）
.ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase);

// CP6 格式化时锁 InvariantCulture，避免区域把 "1.5" 变成 "1,5"
value.ToString("0.########", CultureInfo.InvariantCulture);
```

### 面试怎么问 + 参考答案

**Q：字符串比较为什么要指定 `StringComparison`？**
> A：因为 `Equals`/`StartsWith`/`ToUpper` 默认可能是区域文化敏感的，同一段代码在不同区域设置的机器上行为会不同，最著名的是土耳其语的 `i`/`I` 大小写映射问题，会导致「忽略大小写比较」出诡异 bug，而且文化敏感比较更慢。所以内部的标识符、代号、协议字段比较应显式用 `StringComparison.Ordinal` 或 `OrdinalIgnoreCase`，按字节序比较，快且不受区域影响；只有面向用户展示的排序才用 `CurrentCulture`。CP6 里建流程处理器字典就指定了 `StringComparer.OrdinalIgnoreCase`，格式化数字日期锁 `InvariantCulture`。

---
---

# 2.7　现代 C# 语法全集（C# 8 ~ 12）

> 5 年经验强度的面试，面试官会默认你熟悉这些「新语法」。它们让代码更短更安全，CP6 里随处可见。

## 2.7.1 `var`：类型推断

`var` 让编译器**从右边推断**变量类型——**注意它是静态类型，不是弱类型**，编译后类型完全确定，只是少写一遍。

```csharp
var total = await q.CountAsync();   // 编译器推断 total 是 int
var sb = new StringBuilder();       // 推断为 StringBuilder
// var x;          // ❌ 没初始值无法推断，编译报错
// var y = null;   // ❌ null 推断不出类型
```

- 右边类型一目了然（`new`、方法名自解释）时用 `var` 更清爽；右边看不出类型（如返回 `object` 的方法）时写明确类型更可读。
- CP6 里 `var q = _db.Stocks...`、`var (data, total) = ...` 满屏 `var`。

**面试**：`var` 是弱类型吗？→ 不是，它是编译期类型推断，变量类型编译后就固定死了，和 JavaScript 的 `var` 完全不同。

---

## 2.7.2 模式匹配全家（Pattern Matching）

### `is` 类型/常量模式

```csharp
if (obj is Order order)          // 类型模式：是 Order 就转换并赋给 order 变量
    Console.WriteLine(order.Id);

if (x is null) return;           // 常量模式
if (x is not null) { ... }       // 否定模式（C# 9）
```

CP6 的 `TokenLineage` 就用了：`cur is not null`、`cur.ParentTokenId is Guid pid`（**声明模式**：如果是 Guid 就取出赋给 pid）。

### `switch` 表达式（Switch Expression，C# 8）

比传统 `switch` 语句更紧凑，**有返回值**，用 `=>`：

`C:\CP6\CP6.Core\Services\Wms\IotService.cs`：

```csharp
private static string DefaultUnit(string sensorType) => sensorType switch
{
    IotSensorType.Temperature => "℃",
    IotSensorType.Humidity    => "%",
    IotSensorType.Shock       => "G",
    IotSensorType.Shelf       => "ON-OFF",
    _ => string.Empty,            // _ 是「其余情况」（弃元/default）
};
```

- 每个分支 `模式 => 结果值`，逗号分隔。
- `_`（下划线）是**弃元模式**，等价传统 `default`。
- 编译器会检查是否**穷尽所有情况**，漏了会警告——比传统 switch 安全。

### 属性模式（Property Pattern）

按对象的属性值匹配：

```csharp
string Describe(Order o) => o switch
{
    { Status: "Cancelled" } => "已取消",
    { Amount: > 10000 }     => "大额订单",
    { Amount: <= 0 }        => "异常金额",
    _                        => "普通订单"
};
```

### 元组模式（Tuple Pattern）

同时匹配多个值：

```csharp
string Judge(int score, bool vip) => (score, vip) switch
{
    ( >= 90, _ )    => "优秀",
    ( >= 60, true ) => "及格(VIP)",
    ( >= 60, false )=> "及格",
    _               => "不及格"
};
```

### 列表模式（List Pattern，C# 11）

匹配集合的形状：

```csharp
int[] arr = { 1, 2, 3 };
var r = arr switch
{
    []            => "空",
    [var single]  => $"一个元素:{single}",
    [1, .., 3]    => "以1开头以3结尾",     // .. 是「切片」，匹配中间任意
    _             => "其他"
};
```

### 面试怎么问 + 参考答案

**Q：`switch` 表达式和传统 `switch` 语句有什么区别？**
> A：`switch` 表达式是 C# 8 引入的，本身是个**表达式有返回值**，用 `模式 => 结果` 加逗号分隔，`_` 代表默认分支，语法更紧凑，且编译器会检查是否穷尽所有情况、漏分支会警告。传统 `switch` 是语句、每个 `case` 要 `break`、不返回值。CP6 的 `IotService` 用 `sensorType switch { ... }` 把传感器类型映射到单位，一行一个分支，比传统写法干净很多。配合属性模式、元组模式、列表模式还能做很复杂的条件分发而不写一堆嵌套 if。

---

## 2.7.3 `record`：为「数据载体」而生的类型

### 概念讲解

`record`（记录，C# 9）是专门装数据的引用类型，编译器自动帮你生成：**基于值的相等比较**、`ToString`、解构、`with` 复制。写 DTO/查询结果/事件负载特别省事。

### CP6 真实标本

`C:\CP6\CP6.Core\Services\Platform\ITenantAdminService.cs`：

```csharp
/// <summary>通用分页结果包。</summary>
public record PagedResult<T>(IReadOnlyList<T> Rows, int Total);

/// <summary>租户列表行。</summary>
public record TenantRow(Guid Id, string TenantCode, string TenantName, bool Enable,
                        DateTime? ExpireDate, int UserCount, DateTime CreateDate);

/// <summary>建租户结果：新租户 Id + admin 账号 + 一次性临时密码。</summary>
public record CreateTenantResult(Guid TenantId, string AdminUserName, string TempPassword);
```

### 逐行解析

- `record PagedResult<T>(IReadOnlyList<T> Rows, int Total)` 是**位置记录（positional record）**——括号里就是属性，编译器自动生成：
  - 构造函数 `new PagedResult<TenantRow>(rows, 100)`；
  - 只读属性 `Rows`、`Total`（默认 `init`，创建后不可改）；
  - **值相等**：两个 record 所有属性相等就 `==` 相等（普通 class 是引用相等）；
  - `ToString()`：`PagedResult { Rows = ..., Total = 100 }`；
  - 解构：`var (rows, total) = result;`
- `record PagedResult<T>` 还能泛型化——注意它同时用到了 2.1 的泛型 + 2.2 的 `IReadOnlyList<T>`。

### `record` vs `class` 对比

| | `class` | `record` |
|------|------|------|
| 相等语义 | 引用相等（同一对象才 ==） | **值相等**（属性都相等就 ==） |
| 用途 | 有行为、可变状态的对象 | 不可变的数据载体（DTO、结果、事件） |
| `with` 表达式 | 无 | 有：`r with { Total = 200 }` 复制并改个别属性 |
| 自动 `ToString` | 无（打印类名） | 有（打印各属性值） |

```csharp
var r1 = new CreateTenantResult(id, "admin", "pwd");
var r2 = r1 with { TempPassword = "newpwd" };   // 复制 r1，只改密码，r1 不变
```

---

## 2.7.4 `init` / `required`

- **`init`**：属性只能在**对象初始化时**赋值，之后只读。介于 `set`（随便改）和只读之间。
- **`required`（C# 11）**：强制调用方在初始化时**必须**给这个属性赋值，否则编译报错。

### CP6 真实用例（`init`）

`C:\CP6\CP6.Core\Services\Fin\BudgetDtos.cs`：

```csharp
public class FinResult<T>
{
    public bool Ok { get; init; }        // 只能初始化时设，之后不可改
    public string? Code { get; init; }
    public T? Data { get; init; }
    public static FinResult<T> Pass(T data) => new() { Ok = true, Data = data };
    public static FinResult<T> Fail(string code, params object[] args)
        => new() { Ok = false, Code = code, Args = args };
}
```

- `new() { Ok = true, Data = data }` 用**对象初始化器**赋值 `init` 属性；出了这个初始化块，`Ok`/`Data` 就锁死了，保证结果对象不可变。
- `FinResult<T>` 还是个漂亮的**泛型 + 静态工厂**例子：`FinResult<Budget>.Pass(budget)` / `.Fail("E-001", arg)`。

### `required` 演示

```csharp
public class CreateOrderDto
{
    public required string CustomerCd { get; init; }   // 必须赋值
    public string? Memo { get; init; }                 // 可选
}
var dto = new CreateOrderDto { CustomerCd = "C001" };  // ✅
// var bad = new CreateOrderDto { Memo = "x" };        // ❌ 编译报错：没给 required 的 CustomerCd
```

---

## 2.7.5 元组（Tuple）与解构（Deconstruction）

### 概念讲解

元组是「临时打包几个值一起返回」的轻量方式，不用为此专门定义一个类。

### CP6 真实用例

`IRepository.cs` 的分页返回**命名元组**：

```csharp
Task<(List<T> Data, int Total)> GetPageListAsync(...);   // 返回值有两部分且带名字
```

`RepositoryBase.cs` 里构造和 `ServiceBase.cs` 里解构：

```csharp
return (data, total);                            // 构造元组返回
// 调用方解构：
var (data, total) = await _repository.GetPageListAsync(null, page, pageSize);
//   ↑ 一次性拆成两个变量
```

- `(List<T> Data, int Total)` 是**命名元组**——用 `.Data`/`.Total` 访问比 `.Item1`/`.Item2` 可读。
- `var (data, total) = ...` 是**解构**，一行把元组拆成独立变量。
- 元组适合「**方法内部或紧邻调用**的临时多值返回」；跨层、要复用的多值结构，还是定义 `record` 更清晰（对比 `PagedResult<T>`）。

---

## 2.7.6 `Range` 与 `Index`（`^` 和 `..`）

C# 8 的下标新语法：

```csharp
int[] a = { 10, 20, 30, 40, 50 };
var last  = a[^1];      // ^1 = 倒数第一 → 50
var last2 = a[^2];      // 倒数第二 → 40
var mid   = a[1..4];    // 索引 1 到 4（不含4）→ {20,30,40}
var head  = a[..2];     // 开头到 2（不含）→ {10,20}
var tail  = a[2..];     // 2 到结尾 → {30,40,50}

string s = "ORDER-2026";
var year = s[^4..];     // 后四位 → "2026"
```

- `^n`：从末尾数（`^1` 是最后一个，`^0` 是长度本身/末尾之后）。
- `a..b`：区间 `[a, b)`，含头不含尾。

---

## 2.7.7 其余「省样板」语法

### `using` 声明（C# 8）

不用再套花括号，作用域结束自动释放：

```csharp
// 传统
using (var conn = new SqlConnection(cs)) { /* ... */ }   // 花括号结束时 Dispose

// using 声明（少一层缩进）
using var conn = new SqlConnection(cs);
// ... 用 conn ...
// 方法/代码块结束时自动 conn.Dispose()
```

### 顶级语句（Top-level statements，C# 9）

`Program.cs` 不用再写 `class Program { static void Main }`，直接写语句。CP6 的 `Program.cs` 就是顶级语句风格（`builder.Services.Add...` 直接写在文件里）。

### 文件作用域命名空间（File-scoped namespace，C# 10）

CP6 **所有**文件都用这个——省一层缩进：

```csharp
namespace CP6.Core.BaseProvider;   // ← 一个分号，整个文件都在这个命名空间

public class RepositoryBase<T> { ... }
```

对比老写法 `namespace X { ... 整个文件包起来 ... }` 要多一层花括号和缩进。

### 原始字符串字面量（Raw string literals，C# 11）

三个以上引号包裹，里面不用转义，适合塞 JSON/SQL/正则：

```csharp
string json = """
    { "name": "订单", "qty": 5, "path": "C:\CP6\data" }
    """;   // 里面的 " 和 \ 都不用转义
```

### 集合表达式（Collection expressions，C# 12）

用 `[ ]` 统一初始化各种集合：

```csharp
int[] a = [1, 2, 3];                 // 数组
List<int> list = [1, 2, 3];          // List
int[] merged = [.. a, 4, 5];         // .. 展开（spread）已有集合
Span<int> span = [1, 2, 3];          // 连 Span 都行
```

### `global using`（C# 10）——CP6 重度使用

一处声明，**整个项目**都不用再 `using`。`C:\CP6\CP6.WebApi\GlobalUsings.cs`：

```csharp
global using CP6.Entity.DomainModels.Erp;
global using CP6.Entity.DomainModels.Sys;
global using CP6.Entity.DTOs.Wms;
global using CP6.Core.Services.Erp;
// ...
```

- `global using X;` 等于在**每个 .cs 文件顶部**都写了 `using X;`。
- 好处：常用命名空间集中管理一处，业务文件顶部清爽。CP6 把实体、DTO、服务的命名空间都 global 掉了，所以控制器里能直接用 `Order`、`StockMovementRequest` 不用逐个 `using`。

### 面试怎么问 + 参考答案

**Q：`record` 和 `class` 什么时候用哪个？**
> A：`record` 适合不可变的数据载体——DTO、查询结果、事件负载，它自动生成值相等比较、`ToString`、解构和 `with` 复制，省大量样板。`class` 适合有行为、有可变状态、需要引用语义的对象。判断标准：如果这个类型只是「一包数据」、两个实例内容相同就该视为相等，用 `record`；如果它有身份、要修改内部状态，用 `class`。CP6 里 `PagedResult<T>`、`TenantRow`、`CreateTenantResult` 这些结果类型都是 `record`。

**Q：`init` 和 `set` 区别？`required` 干嘛的？**
> A：`set` 属性任何时候都能改；`init` 属性只能在对象初始化器里赋值，构造完就只读，用来做不可变对象又不必写一堆构造参数。`required` 强制调用方初始化时必须给该属性赋值，否则编译报错，用来保证必填字段不被遗漏。CP6 的 `FinResult<T>` 用 `init` 让结果对象一旦创建就不可篡改。

---
---

# 2.8　值得知道的进阶特性

## 2.8.1 扩展方法（Extension Methods）——LINQ 的实现基础

### 概念讲解（类比）

扩展方法让你**给一个已有类型「外挂」新方法**，而不改它的源码、不继承它。类比：给现成的手机贴个「外接扩展坞」，手机本身没变，但你能像用它自带功能一样用新接口。

关键：**静态类 + 静态方法 + 第一个参数加 `this`**。

```csharp
public static class StringExtensions
{
    // 给 string「外挂」一个 IsValidOrderNo 方法
    public static bool IsValidOrderNo(this string s)
        => s.StartsWith("ORD-") && s.Length == 12;
}

// 用起来就像 string 自带的方法：
"ORD-20260715".IsValidOrderNo();   // 编译器翻译成 StringExtensions.IsValidOrderNo("ORD-...")
```

### 为什么说它是 LINQ 的基础

`Where`、`Select`、`ToList`、`ToDictionary`、`OrderBy`……**全都是 `IEnumerable<T>` 的扩展方法**（定义在 `System.Linq.Enumerable`）。所以你能对任何 `List`、数组写 `.Where(...)`——它们本身没这些方法，是 LINQ 用扩展方法「外挂」上去的。CP6 里 `q.Where(...).OrderBy(...).ToListAsync()` 的链式调用，每一环都是扩展方法。

### 常见坑

- 扩展方法**优先级低于实例方法**：如果类型本身有同名方法，永远调用实例方法，扩展方法被忽略。
- 要 `using` 对应命名空间才能看到扩展方法（LINQ 要 `using System.Linq;`，CP6 常靠 global using）。

### 面试怎么问 + 参考答案

**Q：LINQ 的 `Where`/`Select` 是怎么实现的？**
> A：它们是定义在 `System.Linq.Enumerable` 里、针对 `IEnumerable<T>` 的**扩展方法**。扩展方法通过「静态类里的静态方法 + 第一个参数用 `this` 修饰目标类型」实现，编译器允许你像调用实例方法一样 `list.Where(...)`，实际编译成静态方法调用。正因如此，LINQ 才能给所有集合类型统一加上查询能力而不改它们的源码。

---

## 2.8.2 `partial` 分部类

`partial` 允许把**一个类拆到多个文件**里写，编译时合并成一个。

```csharp
// Order.cs（手写业务）
public partial class Order { public bool IsOverdue() => ...; }
// Order.Generated.cs（工具自动生成的字段）
public partial class Order { public Guid Id { get; set; } ... }
```

- 主要用途：**分离「自动生成代码」和「手写代码」**——EF、WinForm 设计器、源生成器（Source Generator）生成的部分放一个文件，你手写的放另一个，重新生成时不覆盖你的代码。
- WinForm 里 `Form1.Designer.cs`（拖控件生成）和 `Form1.cs`（你写事件处理）就是同一个类的两个 partial 文件。

---

## 2.8.3 迭代器 `yield return`——延迟执行（Lazy Evaluation）的语言基础

### 概念讲解（类比）

普通方法返回集合是「**一次做好整桌菜端上来**」。`yield return` 是「**客人吃一口，厨房现炒一口**」——每次 `foreach` 要下一个元素时才计算一个，用不到的永远不算。这叫**延迟执行 / 惰性求值（lazy evaluation）**。

带 `yield return` 的方法，编译器会自动把它变成一个**状态机**，实现 `IEnumerator`。

### CP6 真实标本

`C:\CP6\CP6.Core\Services\Wf\TokenLineage.cs`——沿父指针上溯，逐个 `yield`：

```csharp
public static IEnumerable<Wf_FlowToken> AncestorChain(
    IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t)
{
    var seen = new HashSet<Guid>();
    for (var cur = t; cur is not null && seen.Add(cur.Id);
         cur = cur.ParentTokenId is Guid pid ? all.FirstOrDefault(x => x.Id == pid) : null)
        yield return cur;    // ← 每次产出一个祖先，不一次性建整个列表
}
```

### 逐行解析

- 返回类型是 `IEnumerable<Wf_FlowToken>`——迭代器方法必须返回 `IEnumerable`/`IEnumerator`（或其泛型版）。
- `yield return cur;`：产出一个元素后**暂停**，保留当前循环状态（`cur`、`seen`），等下次迭代再从这里继续。
- **延迟执行的威力**：`AncestorChain(...).Any(x => x.ForkId == forkId)`——`Any` 一旦找到匹配就停止，`yield` 就**不再往下算**，剩余祖先根本不遍历。若换成先 `ToList()` 建完整列表再找，会白算一堆。
- `seen.Add(cur.Id)` 同时做防环（回顾 2.2.3）——上溯遇到环立即停。

### 常见坑

1. **延迟执行的「陷阱」**：迭代器方法体在你**真正 `foreach`/`ToList` 时才执行**，不是调用时。如果里面依赖的变量在调用后、迭代前被改了，会读到改后的值。
2. **多次遍历 = 多次执行**：对同一个 `IEnumerable`（迭代器）`foreach` 两遍，方法体跑两遍。要复用结果先 `.ToList()` 固化。
3. **`yield` 方法里不能有 `ref`/`out` 参数，也不能在 `try-catch` 的 catch 块里 yield**。

### 面试怎么问 + 参考答案

**Q：`yield return` 是干什么的？和直接返回 List 有什么区别？**
> A：`yield return` 用来写迭代器，实现延迟执行——每次迭代要下一个元素时才计算一个，编译器把方法体转成状态机。和直接返回 `List` 的区别：返回 List 是立即把所有元素算好放内存，`yield` 是惰性的，只在 `foreach` 消费时逐个产出，配合 `Any`/`First`/`Take` 这类短路操作能提前停止、避免多余计算，也不必一次性占用整个集合的内存。CP6 的 `TokenLineage.AncestorChain` 沿父指针逐个 `yield` 祖先节点，配合 `Any` 找到就停，不会白算整条链。

---

## 2.8.4 `IDisposable` / `using` / `await using`——资源释放模式

### 概念讲解（类比）

数据库连接、文件句柄、网络套接字这些**非托管资源**，GC 管不好（GC 只管内存，不知道「连接该关了」）。`IDisposable` 接口约定了一个 `Dispose()` 方法——「用完请显式归还」。`using` 保证**无论正常结束还是抛异常，都会调用 `Dispose()`**。

类比：借了图书馆的书（资源），`using` 就是「借书区」，你一走出这个区域，系统自动帮你还书，绝不会忘。

```csharp
using (var conn = new SqlConnection(cs))   // 进入 using
{
    conn.Open();
    // 用 conn... 即使这里抛异常
}   // ← 离开 using 块，自动调用 conn.Dispose() 关闭连接

// C# 8 using 声明写法（少一层缩进）
using var conn2 = new SqlConnection(cs);
// 方法结束时自动 Dispose
```

### `await using`：异步释放（`IAsyncDisposable`）

有些资源关闭本身要 IO（如异步刷盘、异步关连接），实现 `IAsyncDisposable`，用 `await using`：

```csharp
await using var stream = new FileStream(path, FileMode.Open);
// 用 stream...
// 结束时 await stream.DisposeAsync()
```

- EF Core 的 `DbContext` 同时实现了 `IDisposable` 和 `IAsyncDisposable`。CP6 里 `CP6Context` 由**依赖注入容器**托管生命周期（每个请求一个，请求结束容器自动 `Dispose`），所以你在控制器里**看不到**手写 `using _db`——这是 ASP.NET Core 帮你做了。理解这点很重要：不是不用释放，是框架替你释放了。

### 常见坑

1. **实现了 `IDisposable` 却不 `using`** → 资源泄漏（连接池耗尽、文件被锁）。
2. **DI 托管的对象别自己 `Dispose`**：像 CP6 的 `CP6Context` 是注入进来的，容器负责释放，你手动 `using` 反而会提前关掉别人还在用的实例。
3. **`Dispose` 里别抛异常**。

### 面试怎么问 + 参考答案

**Q：`using` 的作用是什么？和 GC 什么关系？**
> A：`using` 用于确定性地释放实现了 `IDisposable` 的对象，无论代码块正常结束还是抛异常，都保证调用 `Dispose()`。它和 GC 是互补的：GC 只负责回收托管内存，且时机不确定；而数据库连接、文件句柄这类非托管资源必须及时显式释放，不能等 GC。所以这类资源实现 `IDisposable`，用 `using`（或 C# 8 的 `using` 声明、异步的 `await using`）保证用完立即归还。在 ASP.NET Core 里像 `DbContext` 这种由 DI 容器管理生命周期的对象，容器会在请求结束时自动释放，不需要也不应该自己去 `using`。

---
---

# 本章面试题 15 问（含详细参考答案）

**1. 为什么需要泛型？和用 `object` 装元素比有什么好处？**
> 三点：①类型安全，泛型编译期就拦住放错类型，`object` 要运行时才抛 `InvalidCastException`；②性能，值类型存 `object` 会装箱到堆、取出要拆箱，泛型直接存值无装箱，减轻 GC；③可读性，`List<Order>` 自解释。CP6 的 `RepositoryBase<T>` 用泛型让一份 CRUD 服务所有实体表。

**2. 解释 `where T : BaseEntity` 这个约束的作用。**
> 泛型约束限定类型参数 `T` 必须是 `BaseEntity` 或其子类。有了约束，泛型代码内部才能安全访问 `T` 上 `BaseEntity` 定义的成员（如 `x.CreateDate`、`entity.ModifyDate`）。去掉约束，`OrderByDescending(x => x.CreateDate)` 就编译不过。约束是编译器做类型检查的硬性契约。

**3. C# 泛型和 Java 泛型的本质区别？**
> C# 是运行时具体化（reified），泛型类型信息保留到运行时，能 `typeof(T)`、反射、加 `new()` 约束 `new T()`，且 `List<int>` 按 int 存储不装箱。Java 是类型擦除，泛型只在编译期检查，运行时退化成裸类型，拿不到 T、不能 `new T()`、值类型还得装箱成 `Integer`。CP6 的 `context.Set<T>()` 能在运行时按 T 定位表，正依赖 C# 保留泛型信息。

**4. 协变和逆变分别是什么？`IEnumerable<T>` 为什么能协变而 `List<T>` 不能？**
> 协变 `out` 允许「子类集合当父类集合」，T 只能在输出位置（返回值），如 `IEnumerable<out T>`；逆变 `in` 允许「父类当子类」，T 只在输入位置，如 `Action<in T>`。`IEnumerable<T>` 只读输出，子当父读取绝对安全所以能协变；`List<T>` 既读又写（`Add(T)` 是输入位置），若协变就能往子类集合塞不兼容元素，破坏类型安全，所以不变。

**5. `List<T>` 底层结构和扩容机制？**
> 底层是 `T[]` 数组，维护 `Count` 和 `Capacity`。`Add` 时若满了，新建容量翻倍的数组并 `Array.Copy` 搬迁旧元素。单次扩容 O(n)，但翻倍策略使 `Add` 均摊 O(1)。预知大小时用带 capacity 的构造函数预分配可避免多次复制。

**6. `Dictionary` 为什么查找 O(1)？哈希冲突怎么解决？自定义类型当 key 要注意什么？**
> 字典是哈希表，用 key 的 `GetHashCode` 算哈希码映射到桶直接定位，平均 O(1)。冲突（不同 key 同桶）用链地址法，桶内再用 `Equals` 逐个精确比对。自定义类型当 key 必须同时正确重写 `GetHashCode` 和 `Equals`，且保证「Equals 相等则 HashCode 相等」，否则同一逻辑 key 存进去取不出；key 还应不可变。优先用 string/Guid/record/值元组。

**7. `ToDictionary` 和 `ToLookup` 区别？**
> `ToDictionary` 一对一，key 唯一，重复抛异常；`ToLookup` 一对多，同 key 归为一组，可重复。确定 key 唯一要 O(1) 精确查用前者，一个 key 对多条记录（按客户分组订单）用后者。都是把 List 转查找结构，消灭嵌套循环的 O(n²)。CP6 的 `OutboundService` 用 `ToDictionary(d => d.LineNo, ...)` 建行号索引。

**8. `Func`、`Action`、`Predicate` 区别？举个项目里的委托用法。**
> `Action` 无返回值，`Func` 有返回值（最后一个泛型参数是返回类型），`Predicate<T>` 返回 bool，等价 `Func<T,bool>`。CP6 的 `CacheService.GetOrSetAsync<T>(string key, Func<Task<T>> factory)` 用委托把「缓存未命中时去哪查数据」的逻辑交给调用方；`IntegrationEventDispatcher` 用 `Dictionary<string, Func<DispatchContext, Task<bool>>>` 把事件路由到处理委托，消灭大 switch。

**9. 什么是闭包？循环里 lambda 捕获变量的坑怎么解决？**
> 闭包是 lambda 捕获并记住外层变量的能力，捕获的是变量本身不是当时的值。经典坑：`for` 循环里多个 lambda 捕获同一个循环变量，循环结束后都读到最终值（都输出 3 而非 0/1/2）。解决：循环体内声明局部变量拷贝当前值再捕获。注意 C# 5 起 `foreach` 变量每轮独立不踩坑，但 `for` 变量仍共享。

**10. `delegate` 和 `event` 的关系？为什么用 event 不直接暴露委托字段？**
> `event` 是对委托的封装，专用于发布-订阅。区别在访问控制：委托字段外部能 `=` 覆盖甚至替对象触发；`event` 对外只暴露 `+=`/`-=`，赋值和触发只能在声明类内部。这样发布者独占「何时触发」控制权，订阅者只管登记，封装更安全。桌面开发的按钮 `Click`、WPF 的 `PropertyChanged` 都是事件。

**11. `throw` 和 `throw ex` 有什么区别？（高频）**
> `throw;` 重新抛出当前异常，完整保留原始堆栈，能定位最初出错行；`throw ex;` 会把堆栈起点重置为当前行，原始出错位置丢失，线上排查困难。catch 里想继续上抛永远用 `throw;`。需要转换异常类型时用 `throw new XxxException(msg, ex)` 把原异常作为 InnerException，堆栈也不丢。

**12. 自定义异常怎么设计？为什么不继承 `ApplicationException`？项目里业务异常和系统异常怎么分层？**
> 继承 `Exception` 或更贴切的内置异常（状态非法用 `InvalidOperationException`），类名以 Exception 结尾，构造函数把可读信息传给 `base(message)`，用只读属性携带结构化业务字段。不继承 `ApplicationException` 因其是官方已不推荐的早期遗留。分层：业务异常（库存不足）在控制器就地 catch 转 400 加友好文案、通常不告警；系统异常（空引用、DB 超时）不 catch，冒泡到全局中间件转 500 并记日志告警。CP6 的 `InsufficientStockException` 继承 `InvalidOperationException` 携带产品/需求量/库存量，`StockController` 只 catch 业务异常。

**13. `?.`、`??`、`??=`、`!` 各是什么？可空引用类型是运行时特性吗？**
> `?.` 空条件，左边 null 就短路返回 null 不抛；`??` 空合并，左边 null 取右边默认；`??=` 空合并赋值，仅左边 null 才赋值；`!` 空值宽容，向编译器打包票压制警告（不改变运行时，打错包票仍会 NRE）。可空引用类型不是运行时特性，是编译期静态分析注解，`string?` 与 `string` 运行时同一类型，`?` 只让编译器对未判空的使用发警告，靠流分析在编译期发现潜在空引用，零运行时开销。

**14. 为什么循环拼字符串要用 `StringBuilder`？**
> `string` 不可变，每次 `+=` 都新建字符串并复制已有内容，拼 n 次是 O(n²) 且产生大量临时对象加重 GC。`StringBuilder` 内部是可变缓冲区，`Append` 均摊 O(1)、总 O(n)，最后 `ToString()` 一次成型。CP6 导出 CSV 就用 `StringBuilder` 逐行 `Append`。只拼固定几段用 `+` 即可，编译器会优化。

**15. `record` 和 `class` 怎么选？`switch` 表达式相比传统 switch 好在哪？`yield return` 有什么用？**
> `record` 适合不可变数据载体（DTO、结果、事件），自动生成值相等、`ToString`、解构、`with`；`class` 适合有行为、可变状态、引用语义的对象。CP6 的 `PagedResult<T>`/`TenantRow` 都是 record。`switch` 表达式是有返回值的表达式，`模式 => 结果` 更紧凑，编译器检查是否穷尽分支。`yield return` 写迭代器实现延迟执行，逐个产出元素，配合 `Any`/`Take` 短路可提前停止、省内存；CP6 的 `TokenLineage.AncestorChain` 就用它逐个上溯祖先。

---

# 自测清单（Self-Check）

对着下面每一条，能**脱口说出**就打勾。做不到的回到对应小节重读。

## 泛型
- [ ] 能说清「泛型 vs object 集合」的装箱和类型安全两个差异
- [ ] 看懂 `IRepository<T> where T : BaseEntity` 每个部分的含义
- [ ] 能解释 `context.Set<T>()` 为什么能「一份代码通吃所有表」
- [ ] 记得 5 种以上泛型约束（class/struct/new()/基类/接口/notnull）
- [ ] 能说清协变 `out` / 逆变 `in`，并举 `IEnumerable` / `Action` 例子
- [ ] 能对比 C# 具体化 vs Java 类型擦除

## 集合
- [ ] 能画出 `List<T>` 的扩容过程（4→8→16 翻倍）
- [ ] 能讲哈希表定位桶 + 冲突链地址法 + 为什么 key 要正确 GetHashCode
- [ ] 背下六种集合的增删查复杂度表
- [ ] 知道 `HashSet.Add` 返回 bool 可同时去重 + 判存在
- [ ] 知道 `ToDictionary`（一对一）vs `ToLookup`（一对多）
- [ ] 知道 `Dictionary` 非线程安全、多线程用 `ConcurrentDictionary`

## 委托与事件
- [ ] 记得 `Func`（有返回）/`Action`（无返回）/`Predicate`（返回 bool）
- [ ] 能默写 lambda 三阶段演变
- [ ] 能讲循环变量捕获的坑 + 修复方法，且知道 for 和 foreach 的差异
- [ ] 能说清 `event` 相比裸委托的封装保护

## 异常
- [ ] 能画异常继承树（Exception → System/Application → 具体）
- [ ] 能演示 `throw` vs `throw ex` 的堆栈差异
- [ ] 知道 `catch...when` 异常过滤器不展开堆栈的好处
- [ ] 能讲业务异常 vs 系统异常的分层处理策略

## 可空 / 字符串 / 现代语法
- [ ] `?.` `??` `??=` `!` 各自作用张口就来
- [ ] 知道可空引用类型是编译期特性、无运行时开销
- [ ] 能讲字符串不可变 + 驻留 + 为何循环拼接用 StringBuilder
- [ ] 知道 `StringComparison.Ordinal` 与文化敏感的区别（土耳其 i）
- [ ] `record`/`init`/`required`/元组解构/`switch` 表达式/`global using` 都能举 CP6 例子
- [ ] 能讲扩展方法是 LINQ 的基础、`yield return` 是延迟执行、`using` 与 IDisposable

---

# 动手练习（Hands-on Exercises）

> 建议在 CP6 项目里新建一个控制台小程序或单元测试来验证，边写边体会。答案思路附后。

## 练习 1：给泛型仓储加一个「条件计数」方法（泛型 + 表达式 + 约束）

**任务**：仿照 `C:\CP6\CP6.Core\BaseProvider\RepositoryBase.cs`，给 `IRepository<T>` 和 `RepositoryBase<T>` 加一个方法：

```csharp
Task<int> CountAsync(Expression<Func<T, bool>>? filter);
```

要求：`filter` 为 null 时统计全表，否则按条件统计。**提示**：内部用 `_dbSet`，`filter != null ? _dbSet.Where(filter) : _dbSet`，再 `.CountAsync()`。

**思考题**：为什么参数是 `Expression<Func<T,bool>>` 而不是 `Func<T,bool>`？（提示：EF 要把它翻译成 SQL 的 WHERE，需要表达式树而不是编译好的委托。这点第 3 章 LINQ 会深讲。）

## 练习 2：用委托 + 字典消灭一段 if-else（委托 / Func / 模式）

**任务**：假设有个按传感器类型算默认单位的方法（仿 `IotService.DefaultUnit`），先用传统 `if-else` 写一版，再分别用：
- ① `switch` 表达式改写；
- ② `Dictionary<string, Func<string>>`（仿 `IntegrationEventDispatcher`）改写。

体会三种写法的可读性与扩展性差异，并说出：新增一种传感器类型时，哪种改动最小？

## 练习 3：亲手复现「`throw ex` 丢堆栈」和「循环变量捕获」两个坑

**任务 A（异常堆栈）**：
```csharp
static void Deep() => throw new InvalidOperationException("boom");
```
分别写两个 catch：一个 `throw;`、一个 `throw ex;`，在最外层打印 `ex.StackTrace`，对比两者是否包含 `Deep` 这一帧。**预期**：`throw;` 保留 `Deep`，`throw ex;` 丢失。

**任务 B（闭包捕获）**：
```csharp
var actions = new List<Action>();
for (int i = 0; i < 3; i++) actions.Add(() => Console.Write(i));
actions.ForEach(a => a());   // 观察输出
```
先跑一遍观察输出（应是 `333`），再用「循环内建局部变量拷贝」修复成 `012`。最后把 `for` 换成 `foreach`（遍历 `Enumerable.Range(0,3)`）观察是否还有坑。

---

## 章末寄语

本章的所有语法，你在 CP6 里都能找到活样本——**面试时不要背语法定义，要讲「我在项目里怎么用的」**。比如被问泛型，直接说「我们用泛型仓储 `RepositoryBase<T> where T : BaseEntity`，靠 `context.Set<T>()` 让一份 CRUD 服务几十张表」，比复述教科书定义强十倍。

下一章（第 3 章）进入 **LINQ 与 EF Core**——你会看到本章的泛型、委托、`Expression<Func<T,bool>>`、延迟执行如何汇聚成 CP6 里那些 `q.Where(...).OrderBy(...).ToListAsync()` 的查询链。

> 复习动线：泛型（仓储）→ 集合（复杂度表）→ 委托（缓存/分发器）→ 异常（throw/分层）→ 空值运算符 → StringBuilder → record/switch/yield。每天早晚各过一遍自测清单。




