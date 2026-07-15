# C# / .NET 三天冲刺 · 第一天学习文档

> **目标岗位**：制造业生产管理系统开发工程师（C# + SQL + Vue，WEB 程序 + 客户端程序）
> **本文档定位**：把 C#/.NET 当作新手从零讲起，但每个知识点都用 **CP6 项目的真实生产代码**作标本，学完即能在面试中"讲自己的项目"。
> **三天计划**：Day 1 = C#/.NET（本文档）｜Day 2 = SQL/EF Core 深化 + Vue 前端｜Day 3 = 业务场景 STAR 化 + 故障案例 + 模拟面试
> **用法**：每一节都是「概念 → CP6 真实代码 → 逐行讲解 → 面试怎么问 → 你怎么答」。标 ⭐ 的是面试必考，时间不够先过 ⭐。

---

## 目录

- [第一部分：C# 语言地基](#第一部分c-语言地基)
- [第二部分：异步编程 async/await ⭐](#第二部分异步编程-asyncawait-)
- [第三部分：LINQ 与查询 ⭐](#第三部分linq-与查询-)
- [第四部分：ASP.NET Core 框架 ⭐](#第四部分aspnet-core-框架-)
- [第五部分：EF Core 数据访问 ⭐](#第五部分ef-core-数据访问-)
- [第六部分：面试高频 30 问（带参考答案）](#第六部分面试高频-30-问)
- [第七部分：动手练习（在 CP6 上做）](#第七部分动手练习)
- [附录 A：C# vs Java 对照表](#附录-ac-vs-java-对照表)
- [附录 B：CP6 架构一页图（面试画图用）](#附录-bcp6-架构一页图)

---

# 第一部分：C# 语言地基

## 1.1 一个 .NET 项目长什么样

C# 代码组织的层级：**解决方案（Solution）→ 项目（Project）→ 命名空间（Namespace）→ 类（Class）**。

CP6 的解决方案结构（这就是经典的**分层架构**，面试必问）：

```
CP6.sln
├── CP6.Entity     ← 实体层：数据库表对应的 C# 类 + DTO（不含业务逻辑）
├── CP6.Core       ← 核心层：业务服务 Services、数据访问 Repository、EF 迁移
├── CP6.WebApi     ← 接口层：Controllers、中间件、过滤器、后台服务、Program.cs 启动入口
└── CP6.Tests      ← 测试层：xUnit 单元测试 + 集成测试
```

依赖方向是单向的：`WebApi → Core → Entity`。**上层引用下层，下层永远不知道上层的存在**。这保证了业务逻辑（Core）不依赖 Web 框架，可以被单独测试。

> **面试怎么问**："你的项目是怎么分层的？为什么这么分？"
> **你怎么答**：三层+测试。实体层只放数据结构；业务规则全在 Core 的 Service 里；Controller 只做参数接收、调用 Service、包装响应三件事，保持"薄控制器"。好处是：①业务逻辑可以脱离 HTTP 单独跑单元测试（我们后端有 2000+ 个测试）②换接口形式（比如以后加桌面客户端）不用动业务层。

`.csproj` 是项目文件（相当于 Java 的 `pom.xml`），看一眼 CP6 真实的（`CP6.WebApi/CP6.WebApi.csproj`）：

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>   <!-- 跑在 .NET 8 上 -->
    <Nullable>enable</Nullable>                 <!-- 开启可空引用类型检查（见 1.9） -->
    <ImplicitUsings>enable</ImplicitUsings>     <!-- 常用 using 自动导入 -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.12" />
    <!-- NuGet 包引用，相当于 Maven 依赖 -->
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CP6.Core\CP6.Core.csproj" />  <!-- 项目间引用 -->
  </ItemGroup>
</Project>
```

## 1.2 类型系统：值类型 vs 引用类型 ⭐

C# 的类型分两大阵营，**这是面试第一高频基础题**：

| | 值类型 | 引用类型 |
|---|---|---|
| 存什么 | 数据本身 | 指向堆上对象的引用 |
| 赋值行为 | 复制一份数据 | 复制引用（指向同一对象） |
| 默认值 | 0 / false 等 | `null` |
| 例子 | `int` `decimal` `bool` `DateTime` `Guid` `struct` `enum` | `string` `class` `数组` `List<T>` |

CP6 实体里最常用的几个类型，注意各自的用途：

```csharp
public Guid Id { get; set; }           // 主键用 Guid（16字节值类型，全局唯一，分布式友好）
public string? Creator { get; set; }   // 字符串。? 表示允许为 null
public DateTime CreateDate { get; set; }  // 日期时间
public decimal UnitPrice { get; set; }    // ⭐ 金额一律用 decimal，绝不用 double！
public bool IsDeleted { get; set; }       // 布尔
public int Qty { get; set; }              // 整数
```

> **⭐ 必考：为什么金额用 `decimal` 不用 `double`？**
> `double` 是二进制浮点数，0.1 + 0.2 ≠ 0.3（二进制无法精确表示十进制小数），累计对账必然出差错。`decimal` 是 128 位十进制浮点，专为金融/货币设计。CP6 里所有单价、金额、数量字段全是 decimal——ERP 系统对不上账是重大事故。

> **⭐ 常考：`string` 是引用类型，但为什么表现得像值类型？**
> 因为 string 是**不可变的（immutable）**。任何"修改"（拼接、Replace）其实都创建了新字符串。所以循环里大量拼接字符串要用 `StringBuilder`，避免产生海量临时对象。

**装箱/拆箱（boxing/unboxing）**：值类型转成 `object` 叫装箱（分配堆内存，有性能开销），转回来叫拆箱。泛型（1.6）的存在意义之一就是避免装箱。

## 1.3 类、属性、构造函数 —— 标本：BaseEntity

看 CP6 所有实体的祖先类 `CP6.Entity/BaseEntity.cs`（真实代码，一行没改）：

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity;

/// <summary>
/// 所有实体的公共基类，包含每张表都需要的公共字段
/// </summary>
public abstract class BaseEntity
{
    [Key]                                                    // ← 特性(Attribute)：标记这是主键
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]    // ← 数据库自动生成
    public Guid Id { get; set; }

    [MaxLength(100)]                                         // ← 生成 nvarchar(100) 而非 nvarchar(max)
    public string? Creator { get; set; }

    public DateTime CreateDate { get; set; } = DateTime.Now; // ← 属性初始化器：默认值

    [MaxLength(100)]
    public string? Modifier { get; set; }

    public DateTime? ModifyDate { get; set; }                // ← DateTime? = 可空的值类型
}
```

逐个拆解这段代码里的语言点：

**① 属性（Property）** —— `public Guid Id { get; set; }`
这是 C# 区别于 Java 最直观的特性。Java 要写私有字段+getUnitPrice()+setUnitPrice() 三件套；C# 一行搞定，编译器自动生成背后的字段。还可以：

```csharp
public string Name { get; set; } = "";       // 带默认值
public int Total { get; private set; }       // 外面只读，类内可写
public bool IsOverdue => DueDate < DateTime.Today;  // 表达式属性：只读计算属性（无存储）
```

**② 特性（Attribute）** —— `[Key]`、`[MaxLength(100)]`
方括号语法，相当于 Java 的注解 `@Annotation`。是贴在代码上的元数据，由框架反射读取。CP6 里最常见的：`[ApiController]` `[Route]` `[Authorize]` `[HttpGet]`（ASP.NET Core 用）、`[Key]` `[MaxLength]`（EF Core 用）。

**③ 可空值类型** —— `DateTime? ModifyDate`
值类型默认不能为 null，加 `?` 变成 `Nullable<DateTime>`。业务含义：新建的记录还没被修改过，ModifyDate 就该是 null 而不是某个假日期。

**④ XML 文档注释** —— `/// <summary>`
三斜线注释，IDE 悬停可见，Swagger 能读出来生成 API 文档。CP6 全项目坚持写，这也是 JD 里"文档能力"的体现。

> **踩坑实录（面试可讲的真实故事）**：`CreateDate = DateTime.Now` 这个默认值曾经坑过我们——测试里连续创建多条工作流任务，时间戳精度内完全相同，导致排序不稳定、测试随机挂。修复方式是排序时加 Id 作 tiebreaker。教训：**依赖时间戳排序必须考虑同刻并发**。

## 1.4 继承与抽象类 —— 标本：三层实体继承链 ⭐

CP6 的实体继承体系是讲继承的绝佳标本：

```csharp
// 第一层：所有表的公共字段（审计四件套 + 主键）
public abstract class BaseEntity { /* Id, Creator, CreateDate, Modifier, ModifyDate */ }

// 第二层：需要多租户隔离的表，再加一个 TenantId（CP6.Entity/BaseTenantEntity.cs 真实代码）
public abstract class BaseTenantEntity : BaseEntity
{
    /// <summary>租户 Id（行级隔离硬墙；写入时由 SaveChanges 自动盖当前租户，查询时全局过滤）。</summary>
    public Guid TenantId { get; set; }
}

// 第三层：具体业务实体
public class Stock : BaseTenantEntity
{
    public string WarehouseCd { get; set; } = "";
    public string ProductCd { get; set; } = "";
    public decimal PhysicalQty { get; set; }
    public bool IsDeleted { get; set; }
    // ... 只写自己的业务字段，公共字段全部继承而来
}
```

语言点：

- **`abstract class`**：抽象类，不能被 `new`，只能被继承。BaseEntity 存在的意义就是"抽公因式"——审计字段写一遍，244 个实体全复用。
- **`:` 表示继承**：`class Stock : BaseTenantEntity`。C# 是**单继承**（一个类只能有一个父类），但可以实现多个接口：`class Foo : BaseClass, IInterface1, IInterface2`（父类必须写最前面）。
- **`virtual` / `override`**：父类方法标 `virtual` 才能被子类 `override` 重写（Java 默认都能重写，C# 相反，默认封闭）。
- **`sealed`**：禁止再被继承。

> **面试怎么问**："抽象类和接口的区别？什么时候用哪个？"
> **你怎么答**：抽象类是 **is-a**（Stock 是一个租户实体），可以带字段和实现，单继承；接口是 **can-do**（能力契约），只定义成员签名（C# 8 后也可带默认实现），可多实现。我的实际用法：实体公共字段用抽象基类（BaseEntity），服务层全部用接口（IStockMovementService）——因为服务要走依赖注入和单元测试打桩（mock），接口是解耦的关键。
>
> **加分句**：我们还用**标记接口**做横切控制——比如 `IAuditable` 空接口，贴上它的实体自动进字段级审计管道；`IDataScoped` 标记参与数据范围过滤的实体。框架代码用 `if (entity is IAuditable)` 反射识别，不用逐个实体写审计代码。

## 1.5 接口与"面向接口编程" —— 标本：Service 层

CP6 的每个业务服务都是"一接口一实现"成对出现（`CP6.Core/Services/Wms/` 下真实文件对）：

```csharp
// IStockMovementService.cs —— 契约：只说"能做什么"
public interface IStockMovementService
{
    Task<string> ApplyAsync(StockMovementRequest req, CancellationToken ct);
}

// StockMovementService.cs —— 实现：说"怎么做"
public class StockMovementService : IStockMovementService
{
    private readonly CP6Context _db;
    public StockMovementService(CP6Context db) { _db = db; }

    public async Task<string> ApplyAsync(StockMovementRequest req, CancellationToken ct)
    {
        // 校验 → 扣减/增加库存 → 写流水 → 返回单号
    }
}
```

为什么要多写一个接口文件？三个理由（面试原话可用）：

1. **依赖注入**：Controller 只依赖 `IStockMovementService`，运行时容器注入实现类。换实现不改调用方。
2. **单元测试**：测试 Controller 时可以塞一个假的（mock）实现，不用连真数据库。
3. **并行开发**：接口定好，前后端/上下游可以同时开工。

## 1.6 泛型 —— 标本：通用仓储 IRepository&lt;T&gt; ⭐

泛型 = 类型参数化。CP6 的数据访问层用一个泛型仓储覆盖全部 244 个实体（`CP6.Core/BaseProvider/IRepository.cs`）：

```csharp
public interface IRepository<T> where T : BaseEntity   // ← where 是泛型约束
{
    Task<T?> GetByIdAsync(Guid id);
    Task<List<T>> GetAllAsync();
    Task AddAsync(T entity);
    // ...
}

public class RepositoryBase<T> : IRepository<T> where T : BaseEntity
{
    protected readonly CP6Context _db;
    // 一份实现，Stock、Order、PurchaseOrder…… 全部实体通用
}
```

注册到容器时用**开放泛型**一行搞定（`Program.cs` 真实代码）：

```csharp
builder.Services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));
// 之后任何地方要 IRepository<Stock>，容器自动给 RepositoryBase<Stock>
```

语言点：
- `where T : BaseEntity` —— 泛型约束：T 必须是 BaseEntity 的子类，这样泛型代码内部才能访问 `entity.Id`。
- 常用约束：`where T : class`（引用类型）、`where T : new()`（有无参构造）、`where T : IComparable<T>`。
- 泛型避免了装箱和强制转换，`List<int>` 比装 object 的旧式集合又快又安全。

日常最常用的泛型集合：

```csharp
List<Stock> list = new();                    // 动态数组（最常用）
Dictionary<string, decimal> priceMap = new(); // 哈希表：key→value，查找 O(1)
HashSet<Guid> seen = new();                   // 去重集合
Queue<T> / Stack<T>                           // 队列/栈
```

> **面试常问**："List 和 Dictionary 查找性能差别？" List 按内容找是 O(n) 遍历；Dictionary 按 key 是 O(1) 哈希。实战：把数据库查回的列表转字典再循环匹配，`list.ToDictionary(x => x.ProductCd)`，能把 O(n²) 的双重循环降到 O(n)——对账、匹配类功能常用。

## 1.7 委托与 Lambda —— LINQ 的地基 ⭐

**委托（delegate）= 可以装进变量里的方法**。现代 C# 里 99% 的场景你只需要认识两个内置委托和 Lambda 语法：

```csharp
Func<Stock, bool> isEmpty = s => s.PhysicalQty == 0;   // Func<入参, 返回值>：有返回值
Action<string> log = msg => Console.WriteLine(msg);    // Action<入参>：无返回值

// Lambda 表达式 s => s.PhysicalQty == 0 读作：“给我一个 s，我返回 s.PhysicalQty == 0”
```

为什么必须懂：**LINQ 的每个方法都在收委托**。`Where(x => !x.IsDeleted)` 里那个 `x => !x.IsDeleted` 就是一个 `Func<Stock, bool>`。看不懂 Lambda 就看不懂任何现代 C# 代码。

事件（event）是委托的封装，桌面开发（WinForm/WPF，JD 里的"客户端程序"）大量使用：`button.Click += OnButtonClick;`——把方法挂到事件上，点击时被回调。

## 1.8 异常处理 —— 标本：库存不足异常 ⭐

CP6 定义了**业务语义异常**（`CP6.Core/Services/Wms/InsufficientStockException.cs`），在 Controller 里分类捕获（`StockController.Apply` 真实代码）:

```csharp
[HttpPost("apply")]
[RequirePermission("wms-stock", "adjust")]
public async Task<IActionResult> Apply([FromBody] StockMovementRequest req, CancellationToken ct)
{
    req.OperatorCd ??= CurrentUser;    // ??= ：左边为 null 才赋值（见 1.9）
    try
    {
        var txnNo = await _mover.ApplyAsync(req, ct);
        return Ok(new { code = 0, message = "WM-MSG-071", data = new { txnNo } });
    }
    catch (InsufficientStockException ex)   // ← 业务异常：库存不足 → 400 + 给用户看的消息
    {
        return BadRequest(new { code = 400, message = ex.Message });
    }
    catch (ArgumentException ex)            // ← 参数异常 → 400
    {
        return BadRequest(new { code = 400, message = ex.Message });
    }
}
```

要点：

- **catch 从具体到笼统**：先 `InsufficientStockException` 再 `ArgumentException`，顺序反了具体的永远抓不到（编译器会报错）。
- **业务失败用自定义异常，还是返回错误码？** CP6 的约定：服务层深处的业务失败抛自定义异常（带消息键），Controller 统一翻译成 HTTP 状态码+多语言消息。好处是服务层不感知 HTTP。
- **绝不吞异常**：`catch (Exception) { }` 空 catch 是事故之源。要么处理，要么记日志后 `throw;`（注意是 `throw;` 不是 `throw ex;`——后者会丢失原始堆栈）。
- `finally` / `using`：释放资源。`using var conn = new SqlConnection(...)` 语法保证离开作用域自动 Dispose。

## 1.9 可空引用类型与空值运算符 ⭐

CP6 全项目开启 `<Nullable>enable</Nullable>`。这是 .NET 现代工程的标配：**引用类型默认不可为 null，可能为 null 的必须显式标 `?`，编译器帮你查空指针**。

四个空值运算符，CP6 代码里随处可见（全部真实用例）：

```csharp
public string? Creator { get; set; }                 // ? 声明：可以为 null

private string? CurrentUser => User?.Identity?.Name; // ?. 空条件：链上任一环节null则整体null，不抛异常

req.OperatorCd ??= CurrentUser;                      // ??= ：左边为null才赋值

var name = input ?? "default";                       // ?? 空合并：左边null就用右边

c.CustomSchemaIds(t => (t.FullName ?? t.Name));      // 组合使用（Program.cs 真实代码）
```

> **面试怎么答"你怎么防 NullReferenceException"**：三层防线——①项目开 nullable enable，编译期就警告；②入口处校验参数（fail fast）；③链式访问用 `?.` + `??` 给默认值。C# 的 NRE 相当于 Java 的 NPE，防法思路相同。

## 1.10 现代 C# 速览（代码里遇到不慌）

```csharp
// 顶级语句：Program.cs 不用写 class Main，直接写代码（CP6 就是这样）
var builder = WebApplication.CreateBuilder(args);

// var：类型推断（右边类型明显时用，不是动态类型！编译期就定死了）
var total = await q.CountAsync();   // total 是 int

// 匿名对象：临时拼一个返回结构（CP6 所有 API 响应都这么包）
return Ok(new { code = 0, message = "OK", data = new { total, page, items } });

// record：一行声明不可变数据类，自动生成构造/相等比较/ToString（适合 DTO）
public record StockQuery(string? WarehouseCd, int Page = 1);

// 字符串插值：$ 前缀，{} 里放表达式
var msg = $"库存不足: {productCd} 需要{required} 现有{available}";

// 模式匹配
if (entity is IAuditable auditable) { /* 类型判断+转换一步完成 */ }
var label = qty switch { 0 => "缺货", < 10 => "低库存", _ => "正常" };  // switch 表达式

// 集合初始化与索引
var arr = new[] { 1, 2, 3 };
var last = arr[^1];        // ^1 = 倒数第一个
var slice = arr[1..3];     // 切片
```

---

# 第二部分：异步编程 async/await ⭐

**这是 .NET 面试第一高频专题**，CP6 全项目 I/O 一律异步，标本遍地都是。

## 2.1 为什么需要异步

Web 服务器线程是稀缺资源。一个请求进来要查数据库（比如 50ms），**同步写法**这 50ms 里线程干等着，被这个请求独占；高并发时线程池耗尽，后续请求排队超时。**异步写法**：发起数据库调用后线程立刻回池子服务别的请求，数据库返回后再由（任意一个）线程接着执行后半段。

**异步 ≠ 多线程**。await 不创建线程，它是"让出"线程。这句话说清楚，面试官就知道你懂了。

## 2.2 语法三件套

```csharp
//               ① 返回 Task<T>          ② 方法标 async
public async Task<IActionResult> Search(...)
{
    var total = await q.CountAsync();     // ③ 用 await 等待异步操作
    var items = await q.Skip(...).Take(...).ToListAsync();
    return Ok(new { total, items });
}
```

- `Task` = 无返回值的异步操作（类似 Java 的 `CompletableFuture<Void>`）
- `Task<T>` = 有返回值的异步操作
- `await` 之后的代码 = "回调"，但写起来像同步代码——这就是 async/await 的全部意义：**用同步的写法获得异步的性能**。

## 2.3 CP6 的约定（工程实践）

```csharp
public async Task<string> ApplyAsync(StockMovementRequest req, CancellationToken ct)
```

1. **异步方法名以 Async 结尾**：`ApplyAsync`、`GetByIdAsync`——全项目统一约定。
2. **CancellationToken 一路透传**：用户关掉页面/请求超时，取消信号沿调用链传下去，数据库查询中途放弃，不浪费资源。Controller 方法参数里的 `CancellationToken ct` 由框架自动注入。
3. **I/O 一律异步**：数据库（`ToListAsync` `SaveChangesAsync`）、HTTP 外呼、文件读写。CPU 计算不需要异步。

## 2.4 必知的坑（面试送分题）

```csharp
// ❌ 死锁/阻塞三连——永远不要写：
var result = SomeAsync().Result;       // 同步阻塞等待
SomeAsync().Wait();                    // 同上
var r = SomeAsync().GetAwaiter().GetResult();  // 稍好但仍是阻塞

// ❌ async void：异常无法被捕获，调用方无法等待。只有事件处理器允许。
public async void Save() { ... }       // 除了 UI 事件外都是 bug

// ✅ 并发执行多个独立异步操作（不要串行 await）：
var t1 = _db.Stocks.CountAsync();
var t2 = _db.Orders.CountAsync();
await Task.WhenAll(t1, t2);            // 两个查询并发跑
// 注意：同一个 DbContext 实例不是线程安全的，上面这样并发查同一个 context 实际会翻车
// ——真要并发得开两个 scope/两个 context。这是个高级坑，说出来很加分。
```

> **面试怎么问**："async/await 的原理？"
> **够用的答案**：编译器把 async 方法重写成**状态机**，await 点是状态切换点。await 一个未完成的 Task 时，方法立刻返回，线程回线程池；Task 完成后状态机被调度继续执行后半段。ASP.NET Core 没有 UI 同步上下文，续体在任意线程池线程上跑。

---

# 第三部分：LINQ 与查询 ⭐

LINQ（Language Integrated Query）= 把查询能力做进语言。**你写的 Lambda 会被 EF Core 翻译成 SQL**。

## 3.1 标本：CP6 库存查询接口逐行精读

`CP6.WebApi/Controllers/Wms/StockController.cs` 的 Search 方法，这是一个教科书级的"多条件动态查询+分页"，**面试让你手写查询，就默写它**：

```csharp
[HttpGet]
public async Task<IActionResult> Search(
    [FromQuery] string? warehouseCd,     // 每个查询条件都是可选参数
    [FromQuery] string? productCd,
    [FromQuery] bool? hasStockOnly,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50)
{
    // ① 起点：AsNoTracking 只读查询（见 5.4），软删除过滤
    var q = _db.Stocks.AsNoTracking().Where(x => !x.IsDeleted);

    // ② 动态拼条件：传了哪个参数就 AND 哪个条件。
    //    此时一条 SQL 都没发出去！IQueryable 是"查询的描述"，不是结果。
    if (!string.IsNullOrWhiteSpace(warehouseCd)) q = q.Where(x => x.WarehouseCd == warehouseCd);
    if (!string.IsNullOrWhiteSpace(productCd))   q = q.Where(x => x.ProductCd.Contains(productCd)); // → SQL LIKE '%xx%'
    if (hasStockOnly == true)                    q = q.Where(x => x.PhysicalQty != 0);

    // ③ 先数总数（生成 SELECT COUNT(*)）—— await 才真正执行
    var total = await q.CountAsync();

    // ④ 排序 + 分页（生成 ORDER BY ... OFFSET ... FETCH NEXT ...）
    var items = await q
        .OrderBy(x => x.WarehouseCd).ThenBy(x => x.LocationCd)
        .Skip((page - 1) * pageSize).Take(pageSize)
        .ToListAsync();

    return Ok(new { code = 0, message = "OK", data = new { total, page, pageSize, items } });
}
```

三个核心概念全在这段里：

**① 延迟执行（Deferred Execution）⭐必考**
`Where` / `OrderBy` / `Skip` / `Take` 只是在**构建查询表达式**，不查库。真正触发 SQL 的是：`ToListAsync()`、`CountAsync()`、`FirstOrDefaultAsync()`、`AnyAsync()` 这些"物化"方法。所以上面动态拼十个 Where 也只发两条 SQL（一条 COUNT 一条分页查询）。

**② IQueryable vs IEnumerable ⭐必考**

```csharp
IQueryable<Stock> q1 = _db.Stocks.Where(x => x.Qty > 0);   // 条件翻译成 SQL，数据库里过滤
IEnumerable<Stock> q2 = _db.Stocks.AsEnumerable().Where(x => x.Qty > 0); // ❌ 全表拉到内存再过滤！
```

`IQueryable` 携带表达式树，由 EF Core 翻译成 SQL 在数据库端执行；一旦转成 `IEnumerable`（或先 `ToList()` 再 `Where`），后续操作全在内存做。**分页前先 ToList 是最经典的性能事故**：本想取 50 条，实际把 50 万行全拉进了内存。

**③ 分页公式**：`Skip((page - 1) * pageSize).Take(pageSize)`，且 **Skip 前必须 OrderBy**（SQL Server 的 OFFSET 语法要求排序，且无排序的分页结果不稳定）。

## 3.2 高频 LINQ 方法速查

```csharp
// 过滤与查找
.Where(x => ...)                    // 过滤
.FirstOrDefaultAsync(x => ...)      // 第一条，没有返回 null（❗First 没有会抛异常）
.SingleOrDefaultAsync(x => ...)     // 断言最多一条，两条以上抛异常（唯一性校验用）
.AnyAsync(x => ...)                 // 存在性判断 → SQL EXISTS，比 Count()>0 高效

// 投影（只取需要的列——性能优化利器）
.Select(x => new StockDto { Code = x.ProductCd, Qty = x.PhysicalQty })

// 聚合与分组
.SumAsync(x => x.Amount)  .MaxAsync(...)  .CountAsync()
.GroupBy(x => x.WarehouseCd)
 .Select(g => new { Warehouse = g.Key, Total = g.Sum(x => x.PhysicalQty) })

// 集合运算（内存中）
.OrderByDescending(x => x.CreateDate).ThenBy(x => x.Id)   // 多级排序
.Distinct()  .ToDictionary(x => x.Id)  .ToHashSet()
```

> **面试手写题最常出**："按仓库分组统计库存总量，只要总量大于0的，按总量倒序"：
> ```csharp
> var result = await _db.Stocks.AsNoTracking()
>     .Where(x => !x.IsDeleted)
>     .GroupBy(x => x.WarehouseCd)
>     .Select(g => new { WarehouseCd = g.Key, TotalQty = g.Sum(x => x.PhysicalQty) })
>     .Where(x => x.TotalQty > 0)
>     .OrderByDescending(x => x.TotalQty)
>     .ToListAsync();
> ```

---

# 第四部分：ASP.NET Core 框架 ⭐

## 4.1 启动流程：Program.cs 就是全部

.NET 8 的 Web 应用从 `Program.cs` 顶级语句开始，结构固定为两段——**先注册服务，后组装管道**：

```csharp
var builder = WebApplication.CreateBuilder(args);

// ===== 第一段：服务注册（往 DI 容器里放东西）=====
builder.Services.AddControllers();
builder.Services.AddDbContext<CP6Context>(...);
builder.Services.AddScoped<IStockMovementService, StockMovementService>();
builder.Services.AddSignalR();

var app = builder.Build();

// ===== 第二段：中间件管道（请求处理流水线，顺序敏感！）=====
app.UseSwagger();
app.UseCors(...);
app.UseAuthentication();   // 先认证（你是谁）
app.UseAuthorization();    // 后授权（你能干什么）—— 顺序反了授权拿不到身份
app.MapControllers();
app.MapHub<NotifyHub>("/hubs/notify");

app.Run();
```

## 4.2 依赖注入与三种生命周期 ⭐必考

**DI（依赖注入）**：类不自己 `new` 依赖，而是构造函数声明"我需要什么"，容器负责创建和注入。ASP.NET Core 的 DI 是内建的、无处不在的。

```csharp
public class StockController : ControllerBase
{
    private readonly CP6Context _db;
    private readonly IStockMovementService _mover;
    public StockController(CP6Context db, IStockMovementService mover)  // ← 容器自动注入
    {
        _db = db;
        _mover = mover;
    }
}
```

**三种生命周期**（CP6 Program.cs 里三种都有，真实例子）：

| 生命周期 | 含义 | CP6 真实用例 | 为什么 |
|---|---|---|---|
| `AddSingleton` | 全应用一个实例 | `CacheService`、`KafkaProducerService`、`DbStringLocalizer` | 无状态/自带线程安全，创建开销大 |
| `AddScoped` | 每个 HTTP 请求一个实例 | `CP6Context`（DbContext）、所有业务 Service | DbContext 非线程安全且要按请求隔离变更追踪 |
| `AddTransient` | 每次注入都新建 | 轻量、有状态的小工具 | 谁用谁新建，绝不共享 |

> **⭐ 经典陷阱题："Singleton 里能注入 Scoped 吗？"**
> 不能直接注入（构造函数注入会被容器拒绝或造成"被囚禁的依赖"——Scoped 对象被单例永久持有，形同 Singleton）。正确做法是注入 `IServiceScopeFactory`，用时手动开 scope。**CP6 里真实存在这个模式**：`DbStringLocalizer` 是 Singleton（做多语言缓存），但它要读数据库（DbContext 是 Scoped），所以缓存未命中时通过 `IServiceScopeFactory.CreateScope()` 临时取 DbContext 查一次。这个例子面试讲出来非常加分。

## 4.3 中间件与过滤器

**中间件（Middleware）**：请求处理管道上的关卡，每个请求依次穿过。可以自定义——CP6 有 `CsrfMiddleware`（CSRF 防护）、租户解析中间件等。

**过滤器（Filter）**：MVC 层的切面，比中间件更靠近 Action。CP6 的 `OperLogFilter` 全局注册，自动记录每个接口的操作日志（谁、何时、调了什么、参数摘要）——**不用在每个 Controller 里写一行日志代码**，这就是 AOP 思想：

```csharp
builder.Services.AddScoped<OperLogFilter>();
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<OperLogFilter>();   // 全局过滤器
});
```

> 面试概念题："横切关注点（cross-cutting concerns）怎么处理？" 答：日志/审计用全局 Filter，认证授权用中间件+特性，多租户隔离用 EF 全局查询过滤器（见 5.6）——共同思想是**一处实现，处处生效，业务代码零侵入**。

## 4.4 控制器与路由 —— 标本精读

```csharp
[ApiController]                      // 启用自动模型校验、参数推断等 API 约定
[Route("api/wms/stock")]             // 路由前缀：URL 即 API 设计
[Authorize]                          // 整个控制器需要 JWT 登录
public class StockController : ControllerBase
{
    [HttpGet]                        // GET  /api/wms/stock            → 查询（幂等）
    [HttpGet("{stockId:guid}/history")] // GET /api/wms/stock/{guid}/history → 路由参数+类型约束
    [HttpPost("apply")]              // POST /api/wms/stock/apply      → 变更操作
    [RequirePermission("wms-stock", "adjust")]  // ← CP6 自定义授权特性：细粒度权限
}
```

参数绑定四来源：`[FromQuery]`（URL 问号后）、`[FromRoute]`（路径段）、`[FromBody]`（JSON 请求体，一个 Action 最多一个）、`[FromHeader]`。

REST 语义约定：GET 查询（无副作用）、POST 创建/动作、PUT 整体更新、PATCH 局部更新、DELETE 删除。返回：`Ok()`=200、`BadRequest()`=400、`NotFound()`=404、401=未登录、403=已登录但无权限。

> **可以主动讲的安全实战**：CP6 的授权是三层的——①`[Authorize]` 验 JWT（认证）②`[RequirePermission("wms-stock","adjust")]` 验细粒度权限（授权，权限键关联到菜单+角色，逐租户种子）③**fail-closed 反射测试**：单元测试反射扫描全部 Controller，凡是会写数据的端点必须贴权限特性，漏贴直接测试失败——把"忘了加权限"从人祸变成编译期问题。这是我们授权收口六个模块波次的核心机制。

## 4.5 配置系统与后台服务

**配置优先级**（低→高）：`appsettings.json` → `appsettings.{Environment}.json` → 环境变量 → 命令行。

> **真实事故可讲**：CP6 曾因自定义 `appsettings.Local.json` 加载顺序不当，把本地配置源加到了环境变量**之后**，导致 Docker 容器里 `ConnectionStrings__DefaultConnection` 环境变量被本地文件**静默覆盖**，容器连不上数据库。修复：手动把 Local.json 源插入到环境变量源之前，恢复"环境变量最高"的标准优先级。教训：**配置加载顺序 = 覆盖顺序，链尾覆盖链头**。（环境变量里 `__` 双下划线等价于配置里的 `:` 层级。）

**后台服务（IHostedService / BackgroundService）**：与 Web 请求无关的常驻任务。CP6 真实用例：

```csharp
builder.Services.AddHostedService<KafkaOperLogConsumer>();      // 消费 Kafka 操作日志落库
builder.Services.AddHostedService<OperLogCleanupService>();     // 按保留期清理日志（默认7天）
builder.Services.AddHostedService<NotificationConsumer>();      // 消费 RabbitMQ 业务事件→SignalR 推送
```

**SignalR**：WebSocket 实时推送。CP6 用于业务通知（出货完成、盘点差异告警实时弹给在线用户）。`app.MapHub<NotifyHub>("/hubs/notify")` + 前端 `@microsoft/signalr` 客户端。

**消息队列分工**（架构题可讲）：CP6 用 Kafka 扛操作日志（高吞吐、append-only、可回放审计流），RabbitMQ 走业务事件通知（低频、可靠投递、可路由重试），各取所长；Kafka 不可用时过滤器降级直接写库——**降级路径**是生产系统设计的必答项。

---

# 第五部分：EF Core 数据访问 ⭐

## 5.1 EF Core 是什么

ORM（对象关系映射）：C# 类 ↔ 数据库表，LINQ ↔ SQL。你操作对象，它生成 SQL。对应 Java 的 JPA/Hibernate。

```csharp
public class CP6Context : DbContext
{
    public DbSet<Stock> Stocks => Set<Stock>();          // DbSet<T> ≈ 一张表
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    // ... 244 个实体
}
```

## 5.2 迁移（Migration）：数据库结构的版本控制 ⭐

改了实体类 → 生成迁移 → 应用到数据库。CP6 的 `CP6.Core/Migrations/` 下有 60+ 个迁移文件，就是整个数据库结构的演进史：

```bash
dotnet ef migrations add AddWmsCore     # 对比模型快照，生成迁移代码（Up/Down 两个方法）
dotnet ef database update               # 把未应用的迁移按顺序执行到数据库
```

工程纪律（面试可讲）：
- **迁移文件进 git**，和代码一起评审——数据库变更可追溯、可回滚（Down 方法）。
- **每波功能恰好一个迁移**：我们每个功能波次收敛为一个命名清晰的迁移（如 `AddWmsKitting`），部署时可控。
- **Down 也要能跑**：我们曾在唯一索引迁移的 Down 里先删重复数据副本再删索引，否则回滚会撞重复键。

## 5.3 变更追踪与 SaveChanges

```csharp
var stock = await _db.Stocks.FirstOrDefaultAsync(x => x.Id == id);  // 被追踪
stock.PhysicalQty += 10;               // 只改对象
await _db.SaveChangesAsync();          // EF 对比快照，只 UPDATE 变了的列，自动包事务
```

一次 `SaveChangesAsync` 里的多个增删改在**同一个数据库事务**里，要么全成要么全败。CP6 还在 SaveChanges 里做了两件横切的事（重写 SaveChangesAsync）：**自动盖租户章**（新增的 BaseTenantEntity 自动填 TenantId）和**字段级审计**（IAuditable 实体的字段变更自动写审计日志表）。

## 5.4 AsNoTracking：只读查询必加 ⭐

```csharp
var q = _db.Stocks.AsNoTracking().Where(x => !x.IsDeleted);
```

查询默认"被追踪"（EF 留快照以便检测修改），纯展示/报表场景是纯浪费。**CP6 约定：凡是只读查询一律 `AsNoTracking()`**，内存省一半，速度明显快。这是最简单有效的 EF 性能习惯，面试说出来就是实战派的标志。

## 5.5 N+1 问题 ⭐必考

```csharp
// ❌ N+1：1 条查订单 + N 条逐个查明细
var orders = await _db.Orders.ToListAsync();
foreach (var o in orders)
    var details = await _db.OrderDetails.Where(d => d.OrderId == o.Id).ToListAsync();

// ✅ 方案1：Include 预加载（JOIN）
var orders = await _db.Orders.Include(o => o.Details).ToListAsync();

// ✅ 方案2：两条查询+内存组装（CP6 常用，避免 JOIN 笛卡尔膨胀）
var orders = await _db.Orders.ToListAsync();
var ids = orders.Select(o => o.Id).ToList();
var details = await _db.OrderDetails.Where(d => ids.Contains(d.OrderId)).ToListAsync(); // → SQL IN
var map = details.ToLookup(d => d.OrderId);   // 按外键分组，循环里 O(1) 取
```

## 5.6 全局查询过滤器：多租户的硬墙 ⭐（CP6 最亮的架构点）

CP6 是多租户 SaaS：四个租户共用一个库，靠 `TenantId` 行级隔离。隔离不靠自觉——靠 **EF Core 全局查询过滤器**：

```csharp
// OnModelCreating 里对所有 BaseTenantEntity 子类统一注册（示意）：
modelBuilder.Entity<Stock>().HasQueryFilter(x => x.TenantId == _currentTenantId);
```

效果：**任何人写任何 LINQ 查询，EF 自动在 SQL 上追加 `WHERE TenantId = @当前租户`**。开发者忘了过滤也不会串租户——这就是"硬墙"（写入侧由 SaveChanges 自动盖章，见 5.3）。

> **配套安全实战（面试杀手锏）**：光有过滤器还不够。我们终审时发现过一个真实漏洞——角色新增接口直接绑定实体，请求体里带上别人的 TenantId 就能**跨租户写入**（过滤器只管查不管写入者伪造）。修复：写入路径统一改写 TenantId 为当前登录租户，请求体里的值一律不信任。教训：**多租户要查写两侧都设防**。

## 5.7 事务、并发与批量操作

```csharp
// 显式事务：跨多次 SaveChanges 的原子性（如：扣库存+写流水+更新单据状态）
await using var tx = await _db.Database.BeginTransactionAsync(ct);
try { /* 多步操作 */ await tx.CommitAsync(ct); }
catch { await tx.RollbackAsync(ct); throw; }

// 并发控制：乐观锁。实体加 [Timestamp] byte[] RowVersion 字段，
// UPDATE 时 WHERE RowVersion=旧值，被人改过则抛 DbUpdateConcurrencyException → 提示用户刷新重提

// 批量更新（EF7+）：一条 SQL 直达数据库，不加载实体
await _db.Stocks.Where(x => x.WarehouseCd == "W1")
    .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
```

> **ExecuteUpdate 的审计盲区（真实发现，高级话题）**：ExecuteUpdate 绕过变更追踪直发 SQL——快，但**也绕过了我们挂在 SaveChanges 上的字段级审计**。我们在 ERP 审计收口时专门审查了所有 ExecuteUpdate 调用点，发现"单价订正一括伝播"功能零审计行，已记录为待修复项。教训：**框架的快捷通道往往绕过你的横切管道**，引入时要盘点副作用。

---

# 第六部分：面试高频 30 问

> 先自己答，再看参考。答不出的标记，明天重点复习。

**基础（1-10）**

1. **值类型和引用类型的区别？** 见 1.2 表格。加分：struct 在栈/内联存储、赋值即复制；class 堆分配、赋值复制引用。
2. **为什么金额用 decimal？** double 二进制浮点有精度误差，decimal 十进制精确，财务必用。
3. **string 为什么不可变？拼接大量字符串用什么？** 不可变保证线程安全/哈希稳定；用 StringBuilder。
4. **抽象类 vs 接口？** is-a vs can-do；单继承 vs 多实现；CP6 实例：BaseEntity vs IStockMovementService。见 1.4。
5. **readonly 和 const 的区别？** const 编译期常量（内联进调用方）；readonly 运行期只读（构造函数里可赋值）。
6. **ref / out 的区别？** 都是按引用传参；ref 进方法前必须已赋值，out 方法内必须赋值（典型：`int.TryParse(s, out var n)`）。
7. **IEnumerable / ICollection / IList 的关系？** 逐级增强：可枚举 → +Count/Add → +索引器。参数声明用最小够用的接口。
8. **装箱拆箱是什么，怎么避免？** 值类型↔object 转换；用泛型集合避免。
9. **GC 大概怎么工作？** 分代（Gen0/1/2）标记-清除-压缩；大对象堆 LOH ≥85KB；IDisposable+using 管非托管资源，GC 只管托管内存。
10. **Guid 主键 vs 自增 int 主键？** Guid：分布式生成不冲突、不泄露业务量、CP6 全项目用；缺点是索引碎片（可用顺序 Guid 缓解）、16 字节较大。

**异步与 LINQ（11-17）**

11. **async/await 原理？** 编译成状态机，await 让出线程，完成后续体调度回线程池。见 2.4。
12. **Task.Result 为什么危险？** 同步阻塞线程，UI/老框架下经典死锁，高并发下耗尽线程池。
13. **async void 什么时候能用？** 仅事件处理器。异常捕获不到、无法 await。
14. **Task.WhenAll 和连续 await 的区别？** 并发 vs 串行；独立操作用 WhenAll 省总时长（注意 DbContext 不能并发共用）。
15. **IQueryable vs IEnumerable？** 表达式树译成 SQL vs 内存委托执行。分页场景差 4 个数量级。见 3.1。
16. **LINQ 延迟执行？哪些方法触发执行？** Where/Select/OrderBy 构建查询；ToList/Count/First/Any 物化执行。
17. **First / FirstOrDefault / Single / SingleOrDefault？** First 无结果抛异常；Single 断言恰一条，多条抛异常（用于唯一性校验）。

**框架与 EF（18-26）**

18. **DI 三种生命周期？各举一例。** 见 4.2 表格，直接背 CP6 真实注册。
19. **Singleton 注入 Scoped 会怎样？怎么解决？** 被囚禁依赖；IServiceScopeFactory 手动开 scope（CP6 DbStringLocalizer 实例）。
20. **中间件顺序为什么重要？** 管道按注册顺序执行；UseAuthentication 必须在 UseAuthorization 之前。
21. **DbContext 为什么是 Scoped？** 非线程安全 + 变更追踪按请求隔离 + 请求结束统一释放。
22. **AsNoTracking 什么时候用？** 一切只读查询。省内存快照开销。
23. **N+1 是什么？怎么解决？** 见 5.5：Include 或 两查+ToLookup 内存组装。
24. **EF 迁移工作原理？生产环境怎么应用？** 模型快照对比生成 Up/Down；生产：部署时执行 database update 或生成幂等 SQL 脚本审核后执行（CP6：每波恰一迁移，部署清单里显式执行）。
25. **乐观锁 vs 悲观锁？** RowVersion 冲突检测（适合低冲突 Web）vs SELECT ... 锁行（高冲突/短事务）。Web 系统默认乐观锁。
26. **JWT 认证流程？** 登录发 token（含用户/租户/角色声明+签名）→ 每次请求 Authorization: Bearer 头 → 中间件验签构建 ClaimsPrincipal → [Authorize] 放行。无状态、可水平扩展；注销/踢人需要额外机制（黑名单/短有效期+刷新）。

**架构与实战（27-30）**

27. **你们的多租户怎么实现的？** 单库行级隔离：BaseTenantEntity.TenantId + EF 全局查询过滤器（读侧硬墙）+ SaveChanges 自动盖章（写侧）+ 写入路径不信任请求体 TenantId（防伪造）。见 5.6。
28. **接口权限怎么控制的？** JWT 认证 + 自定义 RequirePermission(资源,动作) 特性 + 权限键挂菜单、角色按租户授权 + fail-closed 反射测试兜底防漏贴。见 4.4。
29. **操作日志/审计怎么做的？** 三层：操作日志=全局 Filter→Kafka→消费者落库（可降级直写）；字段级审计=SaveChanges 拦截 IAuditable 实体 diff；容量治理=定时任务监控行数/体积告警。
30. **讲一个你定位过的生产故障。**（Day 3 会展开成 STAR，先备提纲）候选：①磁盘满→swap 分配失败→容器守护进程崩→加看门狗自愈+根治清理；②配置源顺序导致容器环境变量被本地文件覆盖→连不上库；③CSRF 中间件无豁免导致外部回调端点生产 403 死路，三层测试都没拦住→形状精确豁免+上线双向实证。

---

# 第七部分：动手练习

> 光看不写=白学。每题 20-40 分钟，全部在 CP6 里完成，写完跑测试验证。

**练习 1（读代码）**：打开 `CP6.WebApi/Controllers/Wms/StockController.cs`，不看本文档，给 Search 方法逐行加注释解释；然后删掉注释，凭记忆默写出"动态条件+分页"骨架。

**练习 2（写查询）**：在 `CP6.Tests` 里新建一个测试类，用 InMemory/SQLite 方式（模仿现有测试的写法）实现并验证：查询某仓库下库存数量最多的前 5 个产品，返回 `产品编码、总数量`。要求用 GroupBy + OrderByDescending + Take。

**练习 3（写接口）**：给 Stock 加一个只读端点 `GET api/wms/stock/summary?warehouseCd=xx`，返回该仓库的 `产品种类数、总物理库存量`。要求：AsNoTracking、参数校验、匿名对象响应包装 `{ code, message, data }`、照抄邻近 Controller 的风格。写完跑 `dotnet build` + 相关测试。

**练习 4（异步改错）**：自己写一段含 `.Result`、`async void`、循环内逐条 await 独立查询的"坏代码"，然后逐个改对，口头说出每处为什么错。

**练习 5（迁移体验）**：在本地分支上给某个实体加一个可空字段，跑 `dotnet ef migrations add`，**打开生成的迁移文件读懂 Up/Down**，再 `migrations remove` 撤掉（别真应用到库）。

**练习 6（讲解输出）**：对着附录 B 的架构图，把"一个 HTTP 请求从进来到返回"完整讲一遍（中间件→路由→过滤器→控制器→服务→EF→SQL→响应包装），录音听一遍，不顺的地方就是没懂的地方。

---

# 附录 A：C# vs Java 对照表

> 面试口径："主力 C#，了解 Java 生态对应关系，可快速上手。"下表能脱口而出即可。

| 概念 | C# / .NET | Java |
|---|---|---|
| 运行时 | CLR / .NET 8 | JVM |
| 包管理 | NuGet (.csproj) | Maven / Gradle (pom.xml) |
| Web 框架 | ASP.NET Core | Spring Boot |
| ORM | EF Core | JPA / Hibernate / MyBatis |
| DI 容器 | 内建 IServiceCollection | Spring IoC |
| 注解/特性 | `[Attribute]` | `@Annotation` |
| 属性 | `{ get; set; }` 自动属性 | 字段 + getter/setter |
| 异步 | `async/await` + Task | CompletableFuture / 虚拟线程(21+) |
| 集合查询 | LINQ | Stream API |
| 命名习惯 | 方法名 PascalCase | 方法名 camelCase |
| 值类型自定义 | struct / record struct | 无（Valhalla 未落地）；record 类似 |
| 泛型 | 运行时保留（真泛型） | 类型擦除 |
| 测试 | xUnit / NUnit | JUnit |

差异亮点（能说出 1-2 个显得真懂）：C# 泛型运行时不擦除，`List<int>` 不装箱；C# 的 LINQ 表达式树能被翻译成 SQL，Java Stream 只能内存执行，查询要靠 JPA Criteria/QueryDSL 另一套。

---

# 附录 B：CP6 架构一页图

> 面试前用 draw.io 把这两张画成正式图（JD 要求矢量绘图，一举两得）。

**图 1：请求流水线**

```
浏览器 (Vue 3 + Element Plus + Pinia)
   │  axios (JWT Bearer / CSRF token)          ┌─ SignalR /hubs/* (实时通知回推)
   ▼                                           │
ASP.NET Core 中间件管道                          │
   Swagger → CORS → 认证(JWT) → CSRF → 授权 ────┘
   ▼
MVC 过滤器: OperLogFilter(操作日志→Kafka) 
   ▼
Controller ([RequirePermission] 细粒度授权)
   ▼
Service 层 (业务规则、事务边界、业务异常)
   ▼
EF Core CP6Context
   ├─ 全局查询过滤器: WHERE TenantId=@current (多租户硬墙)
   ├─ SaveChanges 拦截: 盖租户章 + IAuditable 字段审计
   ▼
SQL Server ←(EF Migrations 版本化管理)
```

**图 2：部署与周边**

```
Docker Compose 栈:
  cp6-api (ASP.NET Core) ─── SQL Server
       │                 └── Redis (分布式缓存, 开发环境退化为内存缓存)
       ├── Kafka  ── KafkaOperLogConsumer (操作日志落库, 不可用降级直写)
       ├── RabbitMQ ── NotificationConsumer ──→ SignalR fanout
       ├── Prometheus 指标采集
  cp6-web (Vue3 静态站)
运维: DB 每4h备份 / 审计表容量监控告警 / WSL 看门狗自愈
```

---

## 今天结束时的自检清单

- [ ] 能不看文档说出值类型/引用类型区别 + 为什么金额用 decimal
- [ ] 能画出 BaseEntity → BaseTenantEntity → Stock 三层继承并解释为什么
- [ ] 能默写"动态条件+分页"查询骨架（练习 1）
- [ ] 能解释 async/await 让出线程的机制，说出 .Result 和 async void 两个坑
- [ ] 能说出 IQueryable vs IEnumerable 的本质区别和事故场景
- [ ] 能背出 DI 三生命周期 + CP6 各一个真实例子 + Singleton 套 Scoped 陷阱
- [ ] 能讲清多租户"查写两侧设防"的完整方案（全局过滤器+盖章+不信任请求体）
- [ ] 30 问里能独立答出 24 题以上
- [ ] 练习 3 的接口写完且编译通过

**明天（Day 2）**：SQL 手写题+索引与执行计划+EF 性能深化 → Vue 3 组合式 API+组件通信+权限指令实现。
