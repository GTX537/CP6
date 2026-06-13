# 01 · 分层架构与依赖方向

## 🌱 你将学到

- "为什么 CP6 拆成 4 个项目"——不是为了显得专业，是有具体原因的
- 看到 `using CP6.Entity;` 你能立刻明白允许，看到 `using CP6.WebApi;` 在 Entity 里出现你能立刻意识到不对
- 知道分层是"约束"不是"装饰"

---

## 🍳 生活类比：餐厅的分工

想象一家餐厅。如果是路边摊，一个人接单 + 做菜 + 收钱，能干。但十几桌的中等餐厅：

- **前台服务员**：接待客人，记菜单（HTTP 接收请求）
- **厨房**：做菜（业务逻辑）
- **食材仓库**：存放原料（数据）

为什么不让服务员直接进仓库炒菜？因为：

1. **职责不清**：客人多的时候服务员忙不过来，仓库没人管
2. **不能替换**：换厨师容易（厨房独立），换全能型选手难
3. **混乱出错**：服务员炒菜把食材浪费了，仓库一团糟

CP6 拆 4 个项目，本质就是**让前台、厨房、仓库各管一摊**，方便：

- 单独换：换前端框架不影响业务逻辑
- 单独测：测厨房不用真接待客人
- 不互相污染：业务逻辑里没人胡乱发 HTTP 响应

---

## 🔎 看 CP6 代码

打开 `D:\CP6\CP6.slnx`：

```xml
<Solution>
  <Project Path="CP6.Entity/CP6.Entity.csproj" />   <!-- 食材清单 -->
  <Project Path="CP6.Core/CP6.Core.csproj" />       <!-- 厨房 -->
  <Project Path="CP6.WebApi/CP6.WebApi.csproj" />   <!-- 前台 -->
  <Project Path="CP6.Tests/CP6.Tests.csproj" />     <!-- 质检员 -->
</Solution>
</Solution>
```

四个项目就是四个文件夹（你在 `D:\CP6\` 下能看到）。每个有自己的 `.csproj` 文件，类似前端的 `package.json`，告诉编译器"我这个项目要哪些依赖、生成什么"。

### 依赖方向

打开 `CP6.WebApi\CP6.WebApi.csproj`，里面会有类似这样的：

```xml
<ItemGroup>
  <ProjectReference Include="..\CP6.Core\CP6.Core.csproj" />
</ItemGroup>
```

意思是 "WebApi 依赖 Core"，编译时会先编译 Core 再编译 WebApi。

CP6 的依赖方向：

```
CP6.WebApi（前台）  →  CP6.Core（厨房）  →  CP6.Entity（食材）
```

注意：**箭头是单向的**。Entity 不知道 Core 存在，Core 不知道 WebApi 存在。

### 每一层在干嘛

**CP6.Entity**（最底层）—— 只放"数据的形状"。
打开 `D:\CP6\CP6.Entity\BaseEntity.cs`：

```csharp
public abstract class BaseEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public string? Creator { get; set; }
    public DateTime CreateDate { get; set; } = DateTime.Now;
    // ... 修改人、修改时间
}
```

这就是"一张数据库表里都该有的公共字段"。所有业务表会继承这个。

注意 Entity 项目里**没有数据库连接代码**，**没有 HTTP 代码**，**没有业务逻辑**。它就是一堆"形状"。

**CP6.Core**（厨房）—— 业务逻辑 + 数据库交互。
里面有 `EFDbContext/CP6Context.cs`（怎么连数据库）、`Services/OrderService.cs`（受注怎么创建）等等。这一层负责"怎么做菜"。

**CP6.WebApi**（前台）—— 接 HTTP 请求 + 调 Core。
打开 `D:\CP6\CP6.WebApi\Controllers\OrderController.cs`（你看一眼就够了）：

```csharp
[HttpPost("create")]
public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
{
    var order = await _orderService.CreateAsync(dto, GetCurrentUser());
    return Ok(new { code = 200, data = order });
}
```

Controller 自己**不做业务**，只是接客人 → 转给厨房（`_orderService`）→ 厨房做好了端给客人。这就是"薄壳"——Controller 应该薄。

**CP6.Tests**（质检员）—— 测试代码，不上生产。

---

## 🤔 为什么这样

### Q1: 为什么 Entity 不能引用 EF Core？

EF Core 是数据库 ORM 库（第 03 章会讲）。如果 Entity 项目引入 EF Core：

- 任何用 Entity 的地方（脚本、CLI 工具、单测）都被迫拖一个 EF Core 包
- 想换数据库或 ORM，要改 Entity 里所有的属性标记
- Entity 失去"轻量"的特性

CP6 的做法：Entity 只用 `[Key]` `[MaxLength]` 这种来自 .NET 自身的属性，不依赖 EF Core 这种"重"库。

### Q2: 为什么 Core 不能引用 WebApi？

如果 Core 引了 WebApi：

- 业务逻辑就跟 HTTP 绑死了，没法重用（比如想做一个 console 命令行版本）
- 测试 Core 要把 ASP.NET Core 也启动起来
- 反过来 WebApi 也要引 Core，**循环依赖** → 编译报错

### Q3: 那 WebApi 怎么用业务逻辑？

通过依赖注入（第 02 章详讲）。WebApi 不"创建" `OrderService`，而是"让别人塞给我"：

```csharp
public class OrderController(IOrderService orderService)   // 构造函数注入
{
    private readonly IOrderService _orderService = orderService;
}
```

`IOrderService` 是接口（在 Core 里定义），`OrderService` 是实现（在 Core 里实现）。WebApi 只知道接口，不在意是哪个实现 → **解耦**。

### Q4: 这不是 DDD 吗？

听过 DDD（领域驱动设计）的人会觉得这套眼熟。CP6 是**简化版的分层架构 + 一些 DDD 思想**，不算完整 DDD。完整 DDD 还有聚合根、值对象、领域事件等概念。CP6 选了最有用的部分。

---

## ⚠️ 容易搞错的地方

### 1. 把数据库相关代码塞 Entity 项目

```csharp
// ❌ 反例
namespace CP6.Entity;
public class Order
{
    public async Task SaveToDbAsync(CP6Context db) { ... }  // 不该在 Entity 里
}
```

Entity 不应该知道有"数据库"这个东西。"保存"是 Service 的活。

### 2. 在 Controller 里写业务逻辑

```csharp
// ❌ 反例
[HttpPost("create")]
public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
{
    if (await _ctx.Orders.AnyAsync(o => o.WebOrderNo == dto.WebOrderNo))
        return BadRequest("订单号重复");
    var order = new Order { ... };
    _ctx.Orders.Add(order);
    await _ctx.SaveChangesAsync();
    return Ok(order);
}
```

Controller 直接操作数据库 + 写校验逻辑。问题：

- 这段逻辑没法在别的地方（如批处理脚本）复用
- 测试 Controller 要启 ASP.NET Core + 真数据库

正确做法是 Controller 只调 Service：

```csharp
public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
{
    var order = await _orderService.CreateAsync(dto);   // 校验、保存都在 Service
    return Ok(order);
}
```

### 3. 循环依赖

```csharp
// ❌ Core 引用 WebApi → 编译报错
namespace CP6.Core;
public class SomeService
{
    public SomeService(CP6.WebApi.Hubs.NotifyHub hub) { }   // ❌
}
```

Core 不能引用 WebApi。如果 Service 想推 SignalR 消息怎么办？答案是 Core 里定义 `IWmsNotifier` 接口，WebApi 里实现 `SignalRWmsNotifier`。Service 依赖接口，运行时 DI 注入实现。第 06、08 章会反复看到这招。

---

## ✋ 动手试试

### 任务 1：找一个文件，判断它属于哪一层

打开 `D:\CP6\CP6.Core\Services\OrderService.cs` 的前 30 行。回答：

1. 它在哪个项目？
2. 它的 `using` 列表里有 `CP6.Entity` 吗？有 `CP6.WebApi` 吗？为什么？
3. 它的构造函数注入了哪些依赖？

### 任务 2：故意制造一次循环依赖（再撤销）

在 `D:\CP6\CP6.Entity\BaseEntity.cs` 顶部加一行：

```csharp
using CP6.Core;   // ← 故意加
```

然后到命令行：

```bash
cd D:\CP6
dotnet build
```

观察报错信息。理解"循环依赖"长什么样。然后把这行删掉，确认能 build 过。

### 任务 3：画一张图

在纸上或电子白板上画一个矩形代表 CP6 整套系统。在内部画出 4 个项目矩形（Entity / Core / WebApi / Tests）和 1 个外部矩形（cp6.web 前端）。用箭头标出依赖方向。

画完跟本章顶部的图对比，看你哪里画错了。这个动作能把"分层"刻在脑子里。

---

## 📚 想再学一点

- 高级版同章节：[`docs/learning/01-architecture-layering.md`](../learning/01-architecture-layering.md) ——看完本章后再去，会看出更多门道
- 关键词搜索："Clean Architecture"、"Onion Architecture"、"分层架构"
- 项目内：通读 `docs/PROJECT_STRUCTURE.md` §1, §2，对照 CP6 的目录看
