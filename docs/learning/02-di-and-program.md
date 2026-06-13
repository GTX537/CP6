# 02 · DI 容器与 Program.cs 编排术

## 📍 学习目标

1. ASP.NET Core 的 `Singleton / Scoped / Transient` 各自适用场景？选错会怎么样？
2. `Program.cs` 里中间件的注册顺序为什么不能乱？典型的"`UseAuthentication` 必须在 `UseAuthorization` 前"是怎么回事？
3. CP6 用 `AddHostedService` 跑了一堆后台任务，它和 `AddSingleton` + 启动时 `Task.Run` 有什么区别？
4. 什么是"按配置切换实现"？CP6 怎么用 `MesBridge:Enabled = false` 一秒切到 NoOp？
5. 怎么避免 Program.cs 长到 500 行不可维护？

---

## 🔎 真实代码切片

### Program.cs 主要骨架（取自 `D:\CP6\CP6.WebApi\Program.cs`）

```csharp
var builder = WebApplication.CreateBuilder(args);

// 关键：加载顺序 appsettings.json → appsettings.{Env}.json
//                → appsettings.Local.json（本地密钥，.gitignore）→ 环境变量
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// 1. Controllers + 全局过滤器
builder.Services.AddScoped<OperLogFilter>();
builder.Services.AddControllers(opt => opt.Filters.AddService<OperLogFilter>());

// 2. SignalR
builder.Services.AddSignalR();

// 3. EF Core DbContext —— Scoped 生命周期（每请求一个）
builder.Services.AddDbContext<CP6Context>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 4. Dapper 的 IDbConnection —— 也是 Scoped（每请求一个连接）
builder.Services.AddScoped<IDbConnection>(_ =>
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

// 5. 缓存：有 Redis 连接串就 Redis，否则 MemoryCache（接口一致，开发不感知）
var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConn))
    builder.Services.AddStackExchangeRedisCache(o => { o.Configuration = redisConn; o.InstanceName = "CP6:"; });
else
    builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<CacheService>();   // 包装类是 Singleton

// 6. 消息：Kafka 单例（操作日志专任）+ RabbitMQ 单例（业务通知专任）
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<IOperLogTransport>(sp => sp.GetRequiredService<KafkaProducerService>());
builder.Services.AddHostedService<KafkaOperLogConsumer>();   // 后台消费者
builder.Services.AddSingleton<RabbitMQService>();
builder.Services.AddSingleton<INotificationPublisher>(sp => sp.GetRequiredService<RabbitMQService>());
builder.Services.AddHostedService<NotificationConsumer>();

// 7. 通用仓储
builder.Services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));

// 8. 业务 Service —— 全部 Scoped（依赖 Scoped 的 DbContext）
builder.Services.AddScoped<IOrderService, OrderService>();
// ... 数十行类似注册（按业务域分组）

// 9. 按配置切换实现（核心技巧）
var mesBridgeEnabled = builder.Configuration.GetValue<bool?>("MesBridge:Enabled") ?? false;
if (mesBridgeEnabled)
    builder.Services.AddScoped<IMesBridgeHook, MesBridgeHook>();
else
    builder.Services.AddScoped<IMesBridgeHook, NoOpMesBridgeHook>();

// 10. JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(opt => { /* ... */ });
builder.Services.AddAuthorization();

var app = builder.Build();

// ===== 中间件管道（顺序敏感！）=====
app.UseCors("AllowAll");
app.UseAuthentication();   // 必须在 Authorization 之前
app.UseAuthorization();
app.UseMetricServer();     // Prometheus /metrics
app.MapControllers();
app.MapHub<NotifyHub>("/hubs/notify");

// ===== 启动时数据库迁移 + 种子（仅 Docker/生产环境）=====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
    db.Database.Migrate();
}

app.Run();
```

---

## 💡 资深视角

### 三种生命周期：什么时候用哪个

| 生命周期 | 创建时机 | 典型用法 | CP6 例子 | 选错的下场 |
|---|---|---|---|---|
| **Singleton** | 整个进程一个 | 无状态工具、连接池、缓存包装 | `CacheService`、`KafkaProducerService` | 装了 Scoped 依赖（`DbContext`）→ 启动报错（捕获异常）或者线程不安全 |
| **Scoped** | 每个 HTTP 请求一个 | 一切跟数据库相关的 | `CP6Context`、`OrderService`、`OperLogFilter` | 装在 Singleton 里 → 跨请求共享状态、并发崩溃 |
| **Transient** | 每次注入新建 | 极其轻的、无状态的 helper | CP6 几乎没用 | 没什么大坑，但浪费 |

**核心规则**：内层生命周期 ≥ 外层。Singleton 不能依赖 Scoped 是因为 Singleton 一辈子只创建一次，那个时刻的 Scoped 已经销毁了。

**CP6 里的例子**：`CacheService` 是 Singleton（缓存包装跨请求复用），但它注入的是 `IDistributedCache`（也是 Singleton），不会出问题。如果你想让 `CacheService` 注入 `CP6Context` 来兜底"缓存没命中就查库"，必须改 Scoped，否则跨请求共享 DbContext 会爆炸。

### 中间件顺序的"绝对"规则

```csharp
app.UseRouting();          // 1. 决定要走哪个 endpoint
app.UseCors();             // 2. 跨域要在认证前（preflight 不带 token）
app.UseAuthentication();   // 3. 解 JWT，填充 HttpContext.User
app.UseAuthorization();    // 4. 看 User 是否有权限
app.MapControllers();      // 5. 执行 endpoint
```

记忆诀窍："**路由 → 跨域 → 认你是谁 → 看你能不能 → 干活**"。

**反过来会怎样**：
- `UseAuthorization` 在 `UseAuthentication` 前 → `HttpContext.User` 是空的，所有 `[Authorize]` 都拒绝。
- `UseCors` 在 `UseRouting` 前可能无效，因为路由还没决定 endpoint 上的 `[EnableCors]`。
- `MapControllers` 在 `UseAuthorization` 前 → 权限检查跳过，未授权请求也能进 Controller。

### HostedService vs Singleton + Task.Run

CP6 用 `AddHostedService<KafkaOperLogConsumer>()` 跑后台消费者，而不是：

```csharp
// ❌ 反例
builder.Services.AddSingleton<KafkaOperLogConsumer>();
var consumer = app.Services.GetRequiredService<KafkaOperLogConsumer>();
Task.Run(() => consumer.RunAsync());
```

为什么前者好：

1. **生命周期对齐**：HostedService 在 `IHostApplicationLifetime.ApplicationStarted` 后启动，在 `ApplicationStopping` 时收到 `CancellationToken`，能优雅关停。`Task.Run` 不知道宿主何时退出。
2. **异常传播**：HostedService 启动异常会让进程 fail-fast（健康检查会发现）。`Task.Run` 失败默默无声。
3. **生命周期作用域**：HostedService 是 Singleton，但 CP6 在每次消费消息时 `_scopeFactory.CreateScope()` 取出 Scoped 的 `CP6Context`，这是消费者类的标准写法。

### "按配置切换实现"的威力

```csharp
if (mesBridgeEnabled)
    builder.Services.AddScoped<IMesBridgeHook, MesBridgeHook>();
else
    builder.Services.AddScoped<IMesBridgeHook, NoOpMesBridgeHook>();
```

`NoOp*BridgeHook` 是空实现，所有方法返回 Skipped。好处：

- **运行时关闭跨模块联动**：演示 ERP 单模块 demo 时，`MesBridge:Enabled = false` 就行。
- **不需要 if-null 判断**：调用方永远不写 `if (_mesBridge != null)`，只写 `await _mesBridge.OnOrderCreatedAsync(...)`。
- **测试友好**：单测可以注 NoOp，不用 Mock。

**这是 Null Object 模式**。比 `Nullable<T>` 干净，比 if-else 整齐。

### CP6 Program.cs 长达 400+ 行，怎么治？

行业常见做法是写**扩展方法**：

```csharp
// 新建 CP6.WebApi/Extensions/ServiceCollectionExtensions.cs
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWmsServices(this IServiceCollection s)
    {
        s.AddScoped<IStockMovementService, StockMovementService>();
        s.AddScoped<IInboundService, InboundService>();
        // ... 30 行
        return s;
    }

    public static IServiceCollection AddBridgeHooks(this IServiceCollection s, IConfiguration cfg)
    {
        var mesEnabled = cfg.GetValue<bool?>("MesBridge:Enabled") ?? false;
        s.AddScoped<IMesBridgeHook>(_ => mesEnabled ? new MesBridgeHook() : new NoOpMesBridgeHook());
        // ...
        return s;
    }
}

// Program.cs 变成：
builder.Services
    .AddCp6Infrastructure(builder.Configuration)
    .AddWmsServices()
    .AddMesServices()
    .AddErpServices()
    .AddBridgeHooks(builder.Configuration);
```

CP6 还没做这步重构，原因可能是 Program.cs 平铺让"全局开关"一目了然。这是个**可改进点**，面试时被问可以提。

---

## ⚠️ 踩坑记录

### 坑 1：Filter 必须先 AddScoped

```csharp
// ❌ 这样会出错
builder.Services.AddControllers(opt => opt.Filters.Add<OperLogFilter>());

// ✅ CP6 的写法
builder.Services.AddScoped<OperLogFilter>();
builder.Services.AddControllers(opt => opt.Filters.AddService<OperLogFilter>());
```

`Filters.Add<T>()` 是 Singleton 创建模式，但 `OperLogFilter` 依赖 Scoped 的 `CP6Context` —— 会抛 "Cannot consume scoped service from singleton"。
`Filters.AddService<T>()` 是从 DI 容器解析，能拿到 Scoped 实例。

### 坑 2：Singleton 持有 DbContext 的 capture 陷阱

```csharp
// ❌ 反例：跨请求 DbContext 状态污染
builder.Services.AddSingleton<MyService>();

public class MyService(CP6Context db)   // 编译器允许，运行时炸
```

DI 容器会在解析 MyService 时按 Singleton 创建并 capture `db`，但 `db` 是 Scoped 应该每请求一个。.NET 8 默认开启 "validate scopes" 会在启动直接抛出，建议保留 `ValidateScopes = true`（开发环境默认就开）。

### 坑 3：appsettings.Local.json 没在 .gitignore

CP6 用 `AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)` 加载本地密钥，前提是 `.gitignore` 里有这一行。仓库里有 `appsettings.json` + `appsettings.Docker.json` + `appsettings.Development.json` 三份，加 Local 共四级覆盖。**面试常考**："密钥怎么管？"答案分场景：
- 本地：`appsettings.Local.json` + `.gitignore`
- Docker：`.env` + `docker-compose.yml` 的 `${VAR}` 注入
- K8s：`Secret` 对象 + `envFrom: secretRef`
- 云上：Key Vault / AWS Secrets Manager / cloud-native secret

### 坑 4：HostedService 启动顺序

```csharp
builder.Services.AddHostedService<A>();
builder.Services.AddHostedService<B>();
```

注册顺序 = 启动顺序，停止顺序相反（LIFO）。CP6 把 `KafkaOperLogConsumer` 在 `OperLogCleanupService` 前注册，因为消费者优先。如果有依赖，就要靠注册顺序，而不是写在 `StartAsync` 里手动 wait。

---

## 🧪 自检题

1. **生命周期判断**：`KafkaProducerService` 注册成 Singleton，但它内部要写 SQL Server（降级写库）需要 `CP6Context`，怎么处理？  
   <details><summary>答案</summary>注入 <code>IServiceScopeFactory</code>（Singleton 安全），用时 <code>using var scope = _factory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService&lt;CP6Context&gt;();</code>。永远不能直接构造函数注入 Scoped 的 DbContext。</details>

2. **顺序陷阱**：如果把 `app.UseAuthentication()` 删掉，但保留 `app.UseAuthorization()`，所有标了 `[Authorize]` 的接口会怎样？  
   <details><summary>答案</summary>全部 401。<code>UseAuthorization</code> 只看 <code>HttpContext.User</code> 是否有有效 Identity，没有 <code>UseAuthentication</code> 来填充就是匿名。</details>

3. **Bridge Hook 关停**：跑 demo 时想完全禁用所有 Bridge Hook，但又不想改代码，怎么做？  
   <details><summary>答案</summary>把 appsettings 的四个开关全设 false：<code>MesBridge:Enabled / WmsBridge:Enabled / ErpBridge:Enabled / OrderCancelBridge:Enabled</code>。DI 会注入对应的 NoOp 实现，所有 hook 调用都回 Skipped 且不写 IntegrationEvent。</details>

4. **重构题**：你接手了 Program.cs 长达 600 行的项目，怎么把它拆短同时不改行为？  
   <details><summary>答案</summary>(1) 按业务域抽 <code>IServiceCollection</code> 扩展方法（<code>AddWmsServices()</code> 之类）；(2) 中间件管道抽 <code>WebApplication</code> 扩展方法；(3) 启动种子和迁移抽成独立类 <code>DatabaseInitializer</code>；(4) 用 dotnet 的 startup hooks 或自定义 marker interface 自动扫描注册（如 <a href="https://github.com/khellang/Scrutor">Scrutor</a>）。</details>

5. **质疑题**：有人说 DI 容器是过度工程，直接 <code>new OrderService(new CP6Context(...))</code> 也能跑，你怎么解释 DI 的不可替代性？  
   <details><summary>答案</summary>三点：(1) 生命周期管理——一个请求里跨 5 个 Service 注入同一个 <code>CP6Context</code>，手 new 做不到这种"作用域共享"；(2) 配置驱动的实现切换——Bridge Hook 那种 if-config-else 注册，没有容器就要写工厂方法；(3) 测试替换——Mock 注入只要改 <code>builder.Services.AddScoped&lt;IOrderService, MockOrderService&gt;()</code>，手 new 要改业务代码。</details>

---

## 🔗 延伸阅读

- [Dependency injection in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection) — 必读
- [Captive Dependencies (Mark Seemann)](https://blog.ploeh.dk/2014/06/02/captive-dependency/) — 生命周期错配的本质
- [.NET Generic Host - BackgroundService](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers)
- 项目内：`Program.cs` 全文（420+ 行，建议通读一遍）
