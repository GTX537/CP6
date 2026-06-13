# 02 · DI 容器 + Program.cs

## 🌱 你将学到

- "依赖注入"（DI）这四个字到底在干嘛——别再被这个词吓到
- 看 CP6 的 `Program.cs` 不再头晕
- 知道 `Singleton / Scoped / Transient` 选错会怎样
- 理解中间件（middleware）的"先来后到"

---

## 🍳 生活类比：家政公司 vs 自己请保姆

**情景 A**：你想找个保姆。你自己上网搜中介、面试、签合同、付钱、办社保。每周做一遍这套流程。累。

**情景 B**：你找家政公司。你说"我要一个保姆"，公司派人来。换保姆只要换一次申请。

依赖注入就是情景 B。

代码里的"保姆" = 各种 Service（OrderService、CacheService、JwtHelper……）。
"家政公司" = DI 容器（`builder.Services`）。
"申请" = 构造函数声明：

```csharp
public class OrderController(IOrderService orderService)   // 我要一个 IOrderService
{
    // 不用自己 new OrderService()，DI 容器会塞给我
}
```

为什么这样好：

- 换实现只在一个地方改（Program.cs 的注册）
- 测试时塞个假的（Mock）很方便
- 一个 Service 内部用了什么子 Service，调用方不用知道

---

## 🔎 看 CP6 代码

打开 `D:\CP6\CP6.WebApi\Program.cs`，前 30 行：

```csharp
var builder = WebApplication.CreateBuilder(args);

// 加载配置文件
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// 1. 注册 Controllers + 全局过滤器
builder.Services.AddScoped<OperLogFilter>();
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<OperLogFilter>();
});

// 2. 注册 SignalR
builder.Services.AddSignalR();

// 3. 注册数据库上下文
builder.Services.AddDbContext<CP6Context>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```

这段干了什么：

| 行 | 干嘛 | 通俗解释 |
|---|---|---|
| `WebApplication.CreateBuilder` | 创建一个"应用构造器" | 像点外卖之前先开个购物车 |
| `Configuration.AddJsonFile` | 多加一个配置文件 | 告诉应用"配置可能从这里读" |
| `AddScoped<OperLogFilter>()` | 注册一个 Service | 跟家政公司说"我可能需要这种保姆" |
| `AddControllers()` | 启用 MVC 控制器 | 启用"接 HTTP 请求"功能 |
| `AddSignalR()` | 启用 SignalR | 启用"服务端主动推消息"功能 |
| `AddDbContext` | 注册数据库连接 | 告诉应用怎么连数据库 |

### 三种生命周期

```csharp
builder.Services.AddSingleton<CacheService>();    // 整个程序一份
builder.Services.AddScoped<OrderService>();        // 每个 HTTP 请求一份
builder.Services.AddTransient<SomeHelper>();      // 每次注入新建
```

| 选什么 | 适合 | 例子 |
|---|---|---|
| Singleton | 无状态工具、连接池、缓存 | `CacheService`、`KafkaProducerService` |
| Scoped | 跟数据库相关的 | `CP6Context`、`OrderService` |
| Transient | 很轻的、用完就扔 | 少用 |

**记忆诀窍**：

- Singleton = 公共自行车，所有人都骑这一辆（但有问题：两人同时骑就出事）
- Scoped = 出租车，一趟服务一个客人，到目的地下车换下一个
- Transient = 一次性筷子，用完扔

### 启动流程：app.Build() 后才开始走中间件

继续看 Program.cs 后半部分：

```csharp
var app = builder.Build();

// 中间件管道（顺序非常重要！）
app.UseCors("AllowAll");
app.UseAuthentication();   // 解 JWT，知道你是谁
app.UseAuthorization();    // 看你能不能做这事
app.UseMetricServer();     // /metrics 端点
app.MapControllers();      // 处理 /api/* 路由
app.MapHub<NotifyHub>("/hubs/notify");

app.Run();   // 启动！
```

每个 `Use*` 是一层"中间件"。每个 HTTP 请求按顺序穿过这些中间件，像快递包裹经过流水线：

```
HTTP 请求 → CORS 检查 → 解 Token → 权限检查 → 找 Controller → 你的代码 → 返回响应
```

**顺序错了会怎样**：

- `UseAuthorization` 放在 `UseAuthentication` 前 → 还没解 Token 就检查权限 → 所有请求都"未登录"
- `UseCors` 放在 `MapControllers` 后 → 浏览器跨域报错

记法："**路由 → 跨域 → 你是谁 → 你能不能 → 干活**"。

---

## 🤔 为什么这样

### Q1: 不用 DI 容器行不行？

行，但累且容易错。比如你想在 OrderService 里用 CacheService：

```csharp
// ❌ 不用 DI
public class OrderService
{
    private CacheService _cache = new CacheService( /* CacheService 又依赖一堆 */ );
}
```

你要在 `OrderService` 里手动创建 `CacheService`，而创建 CacheService 又要创建它的依赖，一层套一层。改 CacheService 的构造函数 → 所有创建它的地方都要改。

用 DI：

```csharp
public class OrderService(CacheService cache)   // DI 自动塞进来
{
    private readonly CacheService _cache = cache;
}
```

CacheService 怎么创建？DI 容器查注册表，发现 `AddSingleton<CacheService>()`，按 Singleton 规则创建（整个程序一份）。改 CacheService 构造函数？只动它自己，调用方不知不觉。

### Q2: Singleton 不能依赖 Scoped 是为什么？

Singleton 整个程序就一份，从启动到关停都是同一个对象。Scoped 是每请求新建，请求结束就销毁。

如果 Singleton 持有 Scoped：

```csharp
// ❌ 反例
public class CacheService                  // Singleton
{
    public CacheService(CP6Context db)     // ← 这个 db 是 Scoped
    {
        _db = db;
    }
}
```

CacheService 启动时被创建一次，那一刻拿到的 `db` 是某个临时的 Scope 创建的。请求结束后那个 Scope 销毁了，但 CacheService 还在用那个失效的 `db` → 报错或数据乱。

**.NET 8 默认会在启动时检测这种错误并拒绝启动**（"Cannot consume scoped service from singleton"）。

**正确做法**：Singleton 里如果要用 Scoped，注入 `IServiceScopeFactory`，用时自己开 scope：

```csharp
public class CacheService(IServiceScopeFactory factory)
{
    public async Task DoSomething()
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
        // 用 db
    }
    // 离开 using → scope 销毁 → db 释放
}
```

### Q3: appsettings.Local.json 是干嘛的？

CP6 加载顺序：

```
appsettings.json           （基础配置，公开）
appsettings.Docker.json    （Docker 环境用）
appsettings.Local.json     （你本地的密钥，被 .gitignore）
环境变量                    （部署时注入）
```

后面的覆盖前面的。`appsettings.Local.json` 放本地数据库连接字符串、JWT secret 这些不能进 git 的东西。CP6 在 `.gitignore` 里禁了它。

---

## ⚠️ 容易搞错的地方

### 1. 中间件顺序乱

```csharp
// ❌ 错的
app.UseAuthorization();
app.UseAuthentication();   // 太晚了
```

`UseAuthorization` 检查 `HttpContext.User`，但 `User` 是 `UseAuthentication` 填的。反过来等于"看护士的工卡之前先看她有没有权限给你打针" → 永远没权限。

### 2. Filter 注册成 Add 而不是 AddService

```csharp
// ❌ 反例
builder.Services.AddControllers(opt => opt.Filters.Add<OperLogFilter>());
```

`Filters.Add<T>()` 会试图按 Singleton 创建 Filter。但 `OperLogFilter` 依赖 Scoped 的 `CP6Context` → 启动报错。

CP6 的写法：

```csharp
builder.Services.AddScoped<OperLogFilter>();        // 先注册成 Scoped
builder.Services.AddControllers(opt => opt.Filters.AddService<OperLogFilter>());   // 再 AddService 从容器拿
```

### 3. 在 Singleton 构造函数注入 DbContext

```csharp
// ❌ 启动会失败
builder.Services.AddSingleton<MyService>();
public class MyService(CP6Context db) { ... }
```

### 4. Program.cs 越写越长

CP6 的 Program.cs 已经 400+ 行。学到资深时会知道用扩展方法拆短（看高级版本第 02 章）。但初学者阶段先看懂这一坨，别急着优化。

---

## ✋ 动手试试

### 任务 1：数一数 CP6 注册了多少 Service

打开 `D:\CP6\CP6.WebApi\Program.cs`，搜 `AddScoped`、`AddSingleton`、`AddHostedService`，分别有几行？

不用真数到一个不差，目的是让你**亲眼看到这个文件的结构**——上半段全是注册，下半段是中间件 + Run。

### 任务 2：把一个 Singleton 改成 Scoped 看看

找到这一行：

```csharp
builder.Services.AddSingleton<CacheService>();
```

改成：

```csharp
builder.Services.AddScoped<CacheService>();
```

然后跑 `dotnet build`，看会不会报错。再启动应用 `dotnet run --project CP6.WebApi`，看哪些 Service 因为依赖 CacheService 也要跟着改。

**改完别忘改回来**。这是实验，不是真的改。这个实验的目的是让你感受"DI 容器在启动时会自检"。

### 任务 3：跟踪一次请求

启动后端，用 Postman 或浏览器访问 `http://localhost:9991/api/lang/zh-CN`（这个不要登录就能访问）。

然后回到 Program.cs，按中间件顺序在脑子里"模拟"这次请求：

1. 进入 `UseCors` — 跨域允许通过
2. 进入 `UseAuthentication` — 没 Token 也允许（因为这个接口没标 `[Authorize]`）
3. 进入 `UseAuthorization` — 同上
4. 进入 `MapControllers` — 路由匹配到 `LangController.Get`
5. Controller 执行，返回数据
6. 响应反向穿出去

这个"模拟一遍"是真正建立中间件直觉的方式。看 100 篇文章不如自己跟一次。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/02-di-and-program.md`](../learning/02-di-and-program.md)——会讲取舍和性能影响
- 微软官方：[Dependency injection in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)
- 关键词搜索："依赖注入 三种生命周期"、"ASP.NET Core middleware 顺序"
