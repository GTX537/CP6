# 第 5 章 · ASP.NET Core 框架全景

> 本章是全套教程最大的一章。学完后，你要能对着面试官把 CP6 这个真实的 .NET 8 多租户制造业系统「从一个 HTTP 请求进来，到一条 JSON 响应出去」的完整旅程讲清楚——中间经过哪些环节、每个环节为什么这样设计、踩过哪些坑。
>
> **标本项目**：`C:\CP6`，一套多租户（multi-tenant）制造业 ERP/MES/WMS/OA/财务一体化系统，后端 `.NET 8` + `ASP.NET Core Web API`，前端 `Vue 3`，数据库 `SQL Server`，消息用 `Kafka` + `RabbitMQ`，缓存 `Redis`，容器 `Docker`。本章引用的每一段代码都来自这个项目真实文件，路径都标注了。面试时你可以说「我们项目就是这么做的」。
>
> **阅读方式**：每个知识点都按固定结构展开——**概念（类比 + 图）→ CP6 真实代码（标路径）→ 逐行解析 → 坑与真实事故 → 面试问答**。看到「面试问答」的地方请合上书，自己先答一遍。

---

## 本章地图

```
一个 HTTP 请求在 ASP.NET Core 里的完整旅程：

  浏览器/外部系统
      │  HTTP 请求
      ▼
  ┌─────────────────────────────────────────────┐
  │  Kestrel（内置 Web 服务器，跑在 Docker 容器里）  │  ← 5.1
  └─────────────────────────────────────────────┘
      │
      ▼
  ┌─────────────────────────────────────────────┐
  │  中间件管道（Middleware Pipeline，洋葱模型）      │  ← 5.5
  │   CORS → 认证 → 租户 → 本地化 → 异常 → CSRF → 授权 │
  └─────────────────────────────────────────────┘
      │
      ▼
  ┌─────────────────────────────────────────────┐
  │  路由 → 控制器 Controller                       │  ← 5.6
  │   过滤器管道（授权/资源/Action/异常/结果）        │  ← 5.7
  └─────────────────────────────────────────────┘
      │
      ▼
  ┌─────────────────────────────────────────────┐
  │  依赖注入容器解析出来的 Service（业务逻辑）        │  ← 5.3
  │   Scoped DbContext / Dapper / Cache / …        │
  └─────────────────────────────────────────────┘
      │
      ▼   { code, message, data } 统一响应              ← 5.12
  浏览器

  贯穿全程：配置系统（5.4）、认证授权（5.8）、
  旁路的：SignalR 实时推送（5.9）、后台服务（5.10）、Swagger（5.11）
  所有接线都写在 Program.cs（5.2）
```

---

## 5.1 Web 服务器基础

### 5.1.1 概念：HTTP 请求响应模型

先把最底层的东西说清楚。**HTTP 是一问一答的协议**（request-response）：客户端发一个「请求」，服务器回一个「响应」，一来一回一个回合。

一个 HTTP 请求由四部分组成：

```
POST /api/wms/stock/apply HTTP/1.1        ← ① 请求行：方法 + 路径 + 协议版本
Host: cp6.example.com                      ← ② 请求头（Headers）：元数据
Authorization: Bearer eyJhbGc...
Content-Type: application/json
X-CSRF-Token: a1b2c3
                                           ← ③ 空行（分隔头和体）
{ "warehouseCd": "W01", "qty": 100 }       ← ④ 请求体（Body）：真正的数据
```

响应也是四部分：

```
HTTP/1.1 200 OK                            ← ① 状态行：协议 + 状态码 + 短语
Content-Type: application/json             ← ② 响应头
                                           ← ③ 空行
{ "code": 0, "message": "OK", "data": {} } ← ④ 响应体
```

**HTTP 方法（动词）** 表达「你想干什么」，这是面试高频：

| 方法 | 语义 | 幂等？ | CP6 里的例子 |
|------|------|--------|------------|
| GET | 查询，不改数据 | 是 | `GET /api/wms/stock` 查库存 |
| POST | 新建 / 触发动作 | 否 | `POST /api/wms/stock/apply` 库存变动 |
| PUT | 整体替换 | 是 | 更新整条记录 |
| PATCH | 局部更新 | 否*（CP6 语义） | 改单个字段 |
| DELETE | 删除 | 是 | 删一条记录 |

> **幂等（idempotent）**：同一个请求发一次和发十次，服务器最终状态一样。GET/PUT/DELETE 天然幂等，POST 不幂等（连点两次「提交订单」会下两单）。CP6 的 CSRF 中间件就是用「是否安全方法」来判断要不要校验的——GET 安全放行，POST/PUT/PATCH/DELETE 是「不安全方法」要校验（后面 5.5 会精读）。

**常见状态码**（面试必背）：

- `2xx` 成功：`200 OK`、`201 Created`、`204 No Content`
- `3xx` 重定向：`302 Found`（CP6 的 SSO 回调就用 302 跳前端落地屏）
- `4xx` 客户端错误：`400 Bad Request`（参数错）、`401 Unauthorized`（没登录/token 无效）、`403 Forbidden`（登录了但没权限 / CSRF 失败）、`404 Not Found`
- `5xx` 服务器错误：`500 Internal Server Error`

> **401 vs 403 是经典面试陷阱**：401 是「我不知道你是谁」（认证失败，该去登录）；403 是「我知道你是谁，但你不能干这个」（授权失败）。CP6 里：JWT 无效 → 401；登录了但没有 `wms-stock:adjust` 权限 → 403；CSRF token 不对 → 403。

### 5.1.2 Kestrel 是什么

**Kestrel 是 ASP.NET Core 内置的、跨平台的 Web 服务器**。当你 `app.Run()` 启动一个 .NET Web 应用时，实际上是 Kestrel 在监听端口、接收 TCP 连接、把字节流解析成 `HttpContext` 对象、再把你的响应写回 socket。

类比：Kestrel 就像一家餐厅的**前台接待 + 传菜员**。它不做菜（不是你的业务逻辑），但负责把客人（HTTP 请求）领进来、把菜（响应）端出去。它跑得非常快，是 .NET 自己用 C# 写的高性能异步 I/O 服务器。

历史对比（面试可以体现你懂演进）：
- 老 .NET Framework 时代：ASP.NET 跑在 **IIS** 上，和 Windows 深度绑定。
- .NET Core 之后：抽出了 Kestrel，**跨平台**（Linux/Mac/Windows 都能跑），也就能装进 Docker 容器。

### 5.1.3 反向代理部署（CP6 用 Docker 跑 Kestrel）

Kestrel 虽然快，但官方建议在生产环境**前面放一个反向代理**（reverse proxy），比如 Nginx、IIS、或云负载均衡器。

```
        公网 HTTPS 请求
             │
             ▼
  ┌────────────────────┐
  │  反向代理 (Nginx)    │  ← 负责：TLS 终止(HTTPS 解密)、
  │                     │        限流、静态文件、负载均衡、
  └────────────────────┘        把请求转发给后端
             │  HTTP（内网）
             ▼
  ┌────────────────────┐
  │  Docker 容器        │
  │   └ Kestrel         │  ← 只跑纯业务，专注处理请求
  │      └ CP6.WebApi   │
  └────────────────────┘
```

**为什么要反向代理？**（面试标准答案）
1. **TLS 终止**：HTTPS 加解密交给代理，Kestrel 只处理明文 HTTP，减负。
2. **安全边界**：Kestrel 不直接暴露公网，代理挡在前面。
3. **负载均衡**：一个代理后面可以挂多个 Kestrel 实例（横向扩展）。
4. **静态资源 / 限流 / 缓存**：代理擅长这些。

**CP6 的实际部署**：整个后端打包成 Docker 镜像（`cp6-api`），容器里跑的就是 Kestrel。数据库 SQL Server、Redis、Kafka、RabbitMQ 都是同一个 `docker-compose` 里的其他容器。这就是为什么 CP6 的配置里连接字符串要用容器名/环境变量（后面 5.4 的「appsettings.Local.json 事故」就是栽在这个环境变量优先级上）。

> **面试问答**
> **Q：Kestrel 和 IIS 是什么关系？**
> A：Kestrel 是 .NET Core 内置的跨平台 Web 服务器，真正处理请求的是它。IIS（或 Nginx）通常作为反向代理放在 Kestrel 前面，负责 TLS 终止、负载均衡、安全隔离。我们 CP6 是把 Kestrel 打进 Docker 容器跑，前面用反向代理转发。
>
> **Q：为什么不让 Kestrel 直接对公网？**
> A：官方建议加反向代理做 TLS 终止、限流和安全隔离；单实例 Kestrel 也不好做负载均衡。加一层代理后可以挂多个 Kestrel 实例横向扩展。

---

## 5.2 Program.cs 启动全解

`Program.cs` 是整个应用的**入口和总装配图**。CP6 的 `C:\CP6\CP6.WebApi\Program.cs` 有 **2720 行**（是的，一个真实企业项目的启动文件就是这么长），因为它要注册几百个服务、配置认证、组装中间件管道、跑数据库迁移和种子数据。别怕，我们分主题精读。

### 5.2.1 概念：WebApplication.CreateBuilder 做了什么

.NET 6 之后用的是「最小托管模型」（minimal hosting model），入口就一句：

```csharp
var builder = WebApplication.CreateBuilder(args);
```

这一行背后 CreateBuilder 帮你做了一大堆事（面试可以列几条体现你懂）：
1. **建立配置系统**：按顺序加载 `appsettings.json` → `appsettings.{Environment}.json` → **环境变量** → **命令行参数**（越往后优先级越高）。
2. **配置日志**：Console、Debug 等日志提供程序。
3. **准备 DI 容器**：`builder.Services` 就是服务集合（IServiceCollection）。
4. **配置 Kestrel**：默认 Web 服务器。
5. **确定环境**：Development / Staging / Production（读 `ASPNETCORE_ENVIRONMENT` 环境变量）。

### 5.2.2 两段式结构：服务注册 → 管道组装

**这是理解 Program.cs 的总纲**，务必记牢。整个文件永远是两段：

```
第①段：注册服务（Register Services）—— builder.Services.AddXxx(...)
   │    "我这个应用需要哪些能力/零件"
   │    控制器、DbContext、缓存、认证、几百个业务服务……
   │
  var app = builder.Build();   ← 分水岭：容器在此"封箱"，服务集合冻结
   │
第②段：组装管道（Configure Pipeline）—— app.UseXxx(...) / app.MapXxx(...)
        "请求进来后按什么顺序经过哪些环节"
        中间件顺序、路由、Hub 映射……
```

在 CP6 的 Program.cs 里，第①段是第 1~695 行（几百个 `AddScoped`），`var app = builder.Build();` 在第 **697 行**，之后第②段从中间件（第 2669 行起）开始。中间夹着一大段「数据库迁移 + 种子数据」（第 700~2667 行，用 `app.Services.CreateScope()` 手动开作用域跑，这个技巧 5.3 会讲）。

> **面试问答**
> **Q：Program.cs 为什么分两段，`builder.Build()` 之前和之后有什么区别？**
> A：`Build()` 之前是往 DI 容器**注册**服务（往 `builder.Services` 里塞），这时候只是登记「我有哪些服务」。`Build()` 一旦调用，容器就封箱冻结，不能再注册了，返回的 `app` 用来**组装中间件管道和路由**（`app.Use...`）。前段决定「有哪些零件」，后段决定「请求走哪条流水线」。

### 5.2.3 精读 ①：配置源插入技巧（appsettings.Local.json 事故）

Program.cs 一开头就是一段「反直觉」的代码，也是一个真实生产事故的教材。CP6 的 `Program.cs` 第 17~39 行：

```csharp
var builder = WebApplication.CreateBuilder(args);

// 本地凭证覆盖（appsettings.Local.json 在 .gitignore，绝不入仓库）。
// 优先级（低→高）：appsettings.json → appsettings.{Env}.json → appsettings.Local.json → env vars → 命令行。
// 关键：CreateBuilder 已把 env vars/命令行源加在链尾（高优先级）。若用 AddJsonFile 追加，Local.json 会落到
// 更后、反而覆盖 env vars —— 容器里 ConnectionStrings__* 环境变量会被静默吞。故把 Local.json 源**插到 env vars 源之前**，
// 恢复标准 ASP.NET 优先级（env vars 最高）。
var localJsonSource = new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
{
    Path = "appsettings.Local.json",
    Optional = true,
    ReloadOnChange = true,
};
localJsonSource.ResolveFileProvider();
// 注意：Sources 是 IList<IConfigurationSource>，没有 List<T>.FindIndex——手写循环找 env vars 源下标。
var envVarIdx = -1;
for (var i = 0; i < builder.Configuration.Sources.Count; i++)
    if (builder.Configuration.Sources[i] is Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource)
    { envVarIdx = i; break; }
if (envVarIdx >= 0)
    builder.Configuration.Sources.Insert(envVarIdx, localJsonSource);   // 插到 env vars 之前 → env 仍最高
else
    builder.Configuration.Sources.Add(localJsonSource);                 // 兜底（理论不达）
```

**逐行解析**：

- 配置系统是一条「源链」（后面 5.4 详讲），**后加的源优先级更高**（覆盖前面的）。
- `CreateBuilder` 已经默认把顺序排成：JSON 文件（低）→ 环境变量 → 命令行（高）。
- 团队想加一个本地开发用的 `appsettings.Local.json`（放本地数据库密码，不进 Git）。
- **天真的做法**是 `builder.Configuration.AddJsonFile("appsettings.Local.json")`——但这会把 Local.json 加到**链尾**，优先级变成最高，**盖掉了环境变量**。
- 在本地开发没问题；一旦部署到 Docker，容器是靠**环境变量** `ConnectionStrings__DefaultConnection` 注入生产数据库地址的——结果被那个（可能残留在镜像里的）Local.json 静默覆盖，**连到了错误的数据库**。
- **正确做法**：手动找到「环境变量源」在 `Sources` 列表里的下标，把 Local.json **插到它前面**，这样环境变量优先级仍然最高，恢复了标准 ASP.NET 语义。

> **真实事故**（记忆库有记录）：CP6 曾因 `appsettings.Local`/`appsettings.Development` 文件在 Docker 镜像里残留，遮蔽了 docker 环境变量里的连接字符串，导致容器起来后连不上数据库。当时的临时绕行是「部署时删掉 Local.json」，根治办法就是上面这段「插到环境变量源之前」的代码。**这是面试讲配置系统时最有杀伤力的真实故事**——它同时考了你对「配置源优先级」和「容器化部署」的理解。

### 5.2.4 精读 ②：控制器 + 全局过滤器

CP6 第 41~46 行：

```csharp
// 1. 注册控制器（全局注册 OperLogFilter）
builder.Services.AddScoped<OperLogFilter>();
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<OperLogFilter>();
});
```

- `AddControllers()` 注册 MVC 控制器相关服务（模型绑定、验证、格式化器等）。因为是 API，用 `AddControllers` 而不是 `AddControllersWithViews`（那是给 MVC 页面的）。
- `options.Filters.AddService<OperLogFilter>()`：**全局注册**一个操作日志过滤器，意味着**每一个** API 请求都会经过它记日志。`AddService` 而不是 `Add`，是因为这个 filter 有构造函数依赖（要注入 DbContext、Kafka 通道），得从 DI 容器解析——所以上一行先 `AddScoped<OperLogFilter>()`。（过滤器细节见 5.7）

### 5.2.5 精读 ③：SignalR

第 48~49 行：

```csharp
// 1.1 注册 SignalR
builder.Services.AddSignalR();
```

一行注册实时通信框架。CP6 用它做设备状态、库存异动、审批通知的实时推送（详见 5.9）。

### 5.2.6 精读 ④：DbContext（EF Core）

第 59~62 行：

```csharp
// 3. 注册数据库上下文
builder.Services.AddDbContext<CP6Context>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```

- `AddDbContext<T>` 默认把 DbContext 注册成 **Scoped**（每个 HTTP 请求一个实例）——这个生命周期选择极其重要，5.3 会深挖为什么必须是 Scoped。
- `UseSqlServer(...)`：用 SQL Server 提供程序，连接字符串从配置读（就是上面那个可能被 Local.json 坑到的键）。

### 5.2.7 精读 ⑤：Dapper 连接（EF 之外的第二条数据通道）

第 64~66 行：

```csharp
// 3.1 注册 Dapper 用的 IDbConnection（每次请求新建连接）
builder.Services.AddScoped<IDbConnection>(_ =>
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));
```

- CP6 大部分用 EF Core（对象映射方便），但一些**报表/复杂聚合查询**用 Dapper（手写 SQL 更快更灵活）。
- 这里用**工厂委托** `_ => new SqlConnection(...)` 注册（`sp => ...` 这种写法 5.3 会讲）——每个请求 new 一个新连接。Scoped 保证同一请求内复用同一连接，请求结束连接释放。

### 5.2.8 精读 ⑥：Redis / 内存缓存双模

第 68~84 行是一个非常实用的「环境自适应」模式：

```csharp
// 3.2 注册缓存（开发用 Memory，生产切 Redis 只需改这里）
var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConn))
{
    // 生产模式：Redis（配置了连接字符串就用 Redis）
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConn;
        options.InstanceName = "CP6:";  // Key 前缀，区分不同应用
    });
}
else
{
    // 开发模式：内存缓存（零配置，行为和 Redis 一致）
    builder.Services.AddDistributedMemoryCache();
}
builder.Services.AddSingleton<CacheService>();
```

- **配了 Redis 连接字符串就用 Redis，没配就用进程内内存缓存**。两者都实现 `IDistributedCache` 接口，所以业务代码（`CacheService`）完全无感，切换只改这一处。
- 这是「**面向接口编程 + 配置驱动**」的漂亮示范。开发时零依赖（内存），生产时多实例共享缓存（Redis）。
- `CacheService` 注册为 Singleton（全局单例，缓存工具本身无状态）。

### 5.2.9 精读 ⑦：DB 本地化（一个 Singleton 读 Scoped 的经典难题）

第 86~93 行——这段是 5.3「被囚禁依赖」和「手动开 scope」的活教材，这里先记住位置，到 5.3 再深挖：

```csharp
// 3.2.1 i18n 优化 P1：DB 支持的本地化（IStringLocalizer 读 Sys_Lang，复用 CacheService）。
//  - DbStringLocalizer 可 Singleton（缓存未命中时经 IServiceScopeFactory 取 scoped DbContext）。
builder.Services.AddLocalization();
builder.Services.AddSingleton<CP6.WebApi.Localization.DbStringLocalizer>();
builder.Services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizerFactory, CP6.WebApi.Localization.DbStringLocalizerFactory>();
builder.Services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizer>(
    sp => sp.GetRequiredService<CP6.WebApi.Localization.DbStringLocalizer>());
```

### 5.2.10 精读 ⑧：Kafka（操作日志专任）与 RabbitMQ（业务通知专任）

CP6 有一个漂亮的架构决策：**两套消息中间件各司其职**。第 98~113 行：

```csharp
// 3.3 操作日志 = Kafka 专任（高吞吐・append-only・可保留可回放的审计流）
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<IOperLogTransport>(sp => sp.GetRequiredService<KafkaProducerService>());
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.KafkaOperLogConsumer>();

// 操作日志保留期清理（默认 7 天，OperLog:RetentionDays 可配置）
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.OperLogCleanupService>();

// 3.4 RabbitMQ = 业务事件通知/告警 专任（低频・确实配信・可路由可重试）
builder.Services.AddSingleton<RabbitMQService>();
builder.Services.AddSingleton<INotificationPublisher>(sp => sp.GetRequiredService<RabbitMQService>());
builder.Services.AddHostedService<CP6.WebApi.BackgroundServices.NotificationConsumer>();
```

**这个「Kafka vs RabbitMQ」的分工是绝佳的面试谈资**：
- **Kafka 管操作日志**：日志是高吞吐、只追加（append-only）、需要保留和回放的审计流。Kafka 的分区+持久化日志模型天生适合。
- **RabbitMQ 管业务通知**：出货完成、库存差异这类通知是低频、要确实送达、要路由到不同订阅者、失败要重试。RabbitMQ 的 per-message ack + 死信 + 路由 更合适。
- 注意 `AddSingleton<接口>(sp => sp.GetRequiredService<具体类>())` 这个模式：**同一个实例既作为具体类注册、又作为接口注册**（一个实例两个身份），5.3 讲「一接口多实现/工厂注册」时会回来。
- `AddHostedService<T>()` 注册**后台服务**（消费者、清理任务），5.10 详讲。

### 5.2.11 精读 ⑨：几百个 Scoped 业务服务

第 115 行到第 566 行，是密密麻麻几百行的业务服务注册，都长这样：

```csharp
builder.Services.AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>));  // 泛型仓储
builder.Services.AddScoped<CP6.Core.Services.Wms.IStockMovementService, CP6.Core.Services.Wms.StockMovementService>();
builder.Services.AddScoped<CP6.Core.Services.Fin.IJournalEntryService, CP6.Core.Services.Fin.JournalEntryService>();
// …… 上百行同样的模式
```

**模式**：`AddScoped<接口, 实现>()`。控制器只依赖接口（`IStockMovementService`），容器负责给它实实在在的 `StockMovementService`。这是**依赖倒置**的核心用法，也是为什么 CP6 能做到「模块之间只靠接口耦合」。

几个值得注意的花样（都在这几百行里）：
- **泛型注册**：`AddScoped(typeof(IRepository<>), typeof(RepositoryBase<>))`——开放泛型，一次注册所有 `IRepository<T>`。
- **一接口多实现**：`INodeHandler` 注册了十几个实现（StartNodeHandler / ApprovalNodeHandler / EndNodeHandler…），消费方注入 `IEnumerable<INodeHandler>` 拿到全部。见第 130~139 行。
- **配置开关切实现**：很多桥接服务用 `if (配置为真) AddScoped<接口,真实现>() else AddScoped<接口,NoOp实现>()`（如第 243~251 行的 `StockFinBridge`）——生产/演示环境靠配置切换，代码不动。
- **工厂注册**：需要构造时读配置、或手动挑构造函数时，用 `sp => new Xxx(...)`（如第 571~573 行的 `BCryptPasswordHasher`——它有两个构造函数，容器选不出来，只能用工厂显式指定）。

这些花样 5.3 会逐个精讲。**面试时你能说出「我们项目里泛型注册、IEnumerable 多实现、配置切 NoOp、工厂注册这几种 DI 花样都用到了」，立刻显得很有经验。**

### 5.2.12 精读 ⑩：JWT 认证（见 5.8 深讲）

第 640~681 行是认证配置，5.8 会逐行精读，这里先记住它在 Program.cs 的位置：`AddAuthentication(...).AddJwtBearer(options => {...})`。

### 5.2.13 精读 ⑪：中间件管道段（见 5.5 深讲）

`var app = builder.Build();`（第 697 行）之后，跳到文件末尾第 2669~2719 行，是中间件管道的组装：

```csharp
app.UseCors("AllowAll");
app.UseHttpMetrics();
app.UseAuthentication();                                          // 认证
app.UseMiddleware<CP6.WebApi.Middleware.TenantMiddleware>();      // 解析租户
app.UseRequestLocalization(locOptions);                          // 本地化
app.UseMiddleware<CP6.WebApi.Middleware.BizExceptionMiddleware>(); // 异常→本地化响应
app.UseMiddleware<CP6.WebApi.Middleware.CsrfMiddleware>();        // CSRF 校验
app.UseMiddleware<CP6.WebApi.Middleware.MustChangePasswordMiddleware>();
app.UseAuthorization();                                           // 授权
app.MapControllers();                                             // 路由到控制器
app.MapHub<NotifyHub>("/hubs/notify");                           // SignalR Hub 路由
// ……
app.Run();                                                        // 启动，阻塞在这里
```

**这个顺序不是随便排的，每一行的先后都有硬约束**（认证必须在租户解析之前，因为要先知道你是谁才能知道你哪个租户；CSRF 必须在授权之前……）——5.5 会把每一条依赖讲透。

> **面试问答**
> **Q：你们项目的 Program.cs 大概是怎么组织的？**
> A：两段式。第一段注册服务：控制器（带全局操作日志过滤器）、EF Core 的 DbContext（Scoped）、Dapper 连接、Redis/内存缓存双模、DB 本地化、Kafka（操作日志专任）+ RabbitMQ（业务通知专任）、上百个业务服务（清一色 `AddScoped<接口,实现>`）、JWT 认证。`builder.Build()` 之后跑数据库迁移和种子数据，最后组装中间件管道——CORS、认证、租户解析、本地化、异常处理、CSRF、授权、路由，顺序有严格约束。

---

## 5.3 依赖注入深讲（面试核心）

> 依赖注入（DI）是 ASP.NET Core 的骨架，也是**后端面试必考、且能拉开档次**的题。这一节请务必吃透。

### 5.3.1 概念：IoC 思想与好莱坞原则

**控制反转（IoC, Inversion of Control）** 说的是：**别自己去创建你依赖的对象，让外部把它给你**。

对比一下。**没有 DI**（自己 new）：

```csharp
public class StockController
{
    private readonly StockMovementService _mover;
    public StockController()
    {
        // 自己 new，还得 new 它依赖的一切……
        var db = new CP6Context(/* 连接字符串？ */);
        _mover = new StockMovementService(db, /* 还有别的依赖…… */);
    }
}
```

问题：控制器和具体实现死死绑在一起，依赖的依赖也得自己管，没法替换、没法测试（想 mock 都插不进去）。

**有 DI**（外部注入）：

```csharp
public class StockController : ControllerBase
{
    private readonly IStockMovementService _mover;
    public StockController(CP6Context db, IStockMovementService mover)  // 只声明"我需要什么"
    {
        _mover = mover;  // 容器把现成的塞进来
    }
}
```

控制器只是**声明**「我需要一个 `IStockMovementService`」，具体是谁、怎么造、它自己的依赖谁管——统统不关心。

**好莱坞原则**（Hollywood Principle）：**"Don't call us, we'll call you."**（别来找我们，我们会找你）。

类比：你去好莱坞面试当演员，不是你天天打电话追着导演问「有戏没有」，而是**留下简历（声明你的能力），需要时导演打给你**。在 DI 里：你的类留下「构造函数参数」这份简历（声明依赖），需要实例时**容器**主动把依赖造好、调你的构造函数塞给你。控制权从「你主动找依赖」反转成了「框架主动喂你依赖」——这就是「控制反转」这个名字的来历。

### 5.3.2 构造函数注入（Constructor Injection）

ASP.NET Core 最主流、CP6 全程使用的注入方式就是**构造函数注入**：把依赖声明成构造函数参数。看 CP6 `AuthController` 的真实构造函数（`C:\CP6\CP6.WebApi\Controllers\Sys\AuthController.cs` 第 44 行），它一口气注入了 16 个服务：

```csharp
public AuthController(CP6Context context, IConfiguration config, ICurrentPermissionContext perm,
    ITenantContext tenant, IPasswordHasher hasher, IPasswordPolicyService policy,
    ILoginSecurityService login, ISecurityAuditService audit, IRefreshTokenService refresh,
    ITokenBlacklistService blacklist, IAuthCookieWriter cookies, IOptions<SecurityOptions> sec,
    ITenantSsoConfigService ssoConfig, ISsoService sso, ITwoFactorService twoFa, IPendingTokenStore pending)
{
    _context = context;
    _config = config;
    // …… 一一存到字段
}
```

**逐点解析**：
- 每个参数都是一个接口（或抽象），控制器完全不知道背后是哪个实现类。
- 容器在**为这个请求创建控制器**时，会**递归地**把这 16 个依赖都解析出来（每个依赖如果还有依赖，继续往下解析）——这叫**依赖图（dependency graph）解析**。
- 你能一次注入 16 个，正说明构造函数注入的威力：依赖再多，也只是声明；组装是容器的活。
- **好处**：一眼看构造函数就知道这个类依赖什么（依赖显式化）；单元测试时传 mock 进去就行（`AuthController` 的测试可以传假的 `IPasswordHasher`）。

> **为什么优先构造函数注入而不是别的方式？**（面试点）构造函数注入让依赖**必填且不可变**（`readonly` 字段），对象一旦建好就是完整可用的状态，避免了「属性注入」那种「建好了但依赖还没设好」的半成品状态。

### 5.3.3 三种生命周期逐个深挖

注册服务时选的生命周期（lifetime），决定**容器什么时候造新实例、造几个**。这是 DI 面试的重头戏，三种一定要能说清「定义 + 适用 + 误用后果」。

```
┌──────────── 一个应用进程的生命 ───────────────────────────┐
│                                                          │
│  Singleton  ●────────────────────────────────────────►  │  全程只有 1 个
│                                                          │
│  请求A ┌─────────┐   请求B ┌─────────┐  请求C ┌────────┐  │
│  Scoped│    ●    │        │    ●    │       │   ●    │  │  每请求 1 个
│        └─────────┘        └─────────┘       └────────┘  │
│                                                          │
│ Transient 每次注入/每次 GetService 都新造 ● ● ● ● ● ● ● │  用完即弃
└──────────────────────────────────────────────────────────┘
```

#### ① Singleton（单例）—— 全应用一个

- **定义**：整个应用进程**只创建一个实例**，所有请求、所有线程共享它。第一次被解析时创建，直到应用关闭。
- **适用**：无状态的工具类、纯配置、缓存包装、连接工厂等。CP6 里注册成 Singleton 的有：`CacheService`（缓存工具，无状态）、`KafkaProducerService`（Kafka 生产者，一个连接池全局共用）、`RabbitMQService`、`DbStringLocalizer`（DB 本地化器，靠缓存 + 手动开 scope，见下文）、`DepreciationCalculator`（折旧纯函数计算器）。
- **误用后果（面试重点）**：
  1. **线程安全问题**：单例被所有请求并发访问，如果它内部有可变状态（字段），必须自己加锁，否则数据错乱。
  2. **内存泄漏**：单例持有的东西整个应用生命周期都不释放。
  3. **最危险——捕获 Scoped 依赖（captive dependency）**：Singleton 若在构造函数里注入一个 Scoped 服务（比如 DbContext），这个 Scoped 实例就被单例「囚禁」了，跟着单例活一辈子。这是重大 bug，下面 5.3.5 专门讲。

#### ② Scoped（作用域）—— 每个请求一个

- **定义**：**每个「作用域」创建一个实例**。在 Web 里，**一个 HTTP 请求 = 一个作用域**（框架自动为每个请求开一个 scope）。同一请求内多次注入同一个 Scoped 服务，拿到的是**同一个实例**；不同请求之间是不同实例。
- **适用**：**EF Core 的 DbContext（最典型！）**、以及绝大多数业务服务。CP6 里几百个 `AddScoped<...>` 都是这个——每个请求一套干净的服务实例，请求结束一起释放。
- **为什么 DbContext 必须是 Scoped？**（超高频面试题）
  1. DbContext **不是线程安全的**——如果做成 Singleton，多个请求并发用同一个 DbContext，会直接崩（EF 会抛「已有一个操作在进行中」）。
  2. DbContext 有**变更追踪（change tracking）** 状态——它记着「这个实体被改了」。一个请求一套，请求内的所有操作共享同一个工作单元（Unit of Work），一起 `SaveChanges` 提交，天然是一个事务边界。请求结束后追踪状态清空。
  - 所以 `AddDbContext<T>()` 默认就是 Scoped。CP6 里 `AddDbContext<CP6Context>(...)` 也是。
- **误用后果**：
  1. 把它做成 Singleton → 上面说的线程崩溃 + 追踪状态跨请求污染。
  2. 在 Singleton 里注入它 → captive dependency（见 5.3.5）。
  3. 在后台服务（本身是 Singleton）里直接注入 Scoped → 同样问题，必须手动开 scope（见 5.3.6）。

#### ③ Transient（瞬态）—— 每次都新造

- **定义**：**每次被请求（注入或 GetService）都创建一个新实例**。用完即弃，最短命。
- **适用**：轻量、无状态、每次要独立实例的小对象。
- **误用后果**：
  1. **性能开销**：如果一个 Transient 服务很重（比如内部建连接），每次 new 会很浪费。
  2. **注入进 Singleton 时行为反直觉**：一个 Singleton 注入一个 Transient，这个 Transient 只在单例构造时 new 一次，之后就跟着单例活着——**Transient 的「瞬态」语义失效了**，实际变成了单例的寿命。这也是 captive dependency 的一种。

> CP6 用得最多的是 **Scoped**（业务服务、DbContext），关键工具用 **Singleton**（缓存、消息生产者、本地化器）。Transient 用得较少——`RequirePermissionAttribute` 这种特性本身不走 DI 生命周期（它是 attribute 实例）。这其实是很多真实项目的常态：**Scoped 打天下，Singleton 管工具**。

### 5.3.4 生命周期矩阵：谁能注入谁

核心规则一句话：**你注入的依赖，寿命不能比你短**。（否则依赖会在你还活着时就该死了，或者被你违规延寿。）

```
        被注入的依赖 →
注入方 ↓      Singleton      Scoped         Transient
────────────────────────────────────────────────────────
Singleton    ✅ 安全        ❌ 囚禁!        ⚠️ 变相延寿
Scoped       ✅ 安全        ✅ 安全         ✅ 安全
Transient    ✅ 安全        ✅ 安全         ✅ 安全
```

- **Singleton 注入 Scoped = ❌ captive dependency**：单例活一辈子，把一个本该「每请求一个」的 Scoped 实例攥住不放，导致这个 Scoped 实例被跨请求复用（数据污染 + 线程不安全）。**这是最经典的错误。**
- **Singleton 注入 Transient = ⚠️**：Transient 被单例「固化」，失去瞬态语义。
- **Scoped / Transient 注入任何东西都安全**（它们寿命够短或相等）。
- .NET 在 Development 环境启动时会做 **`ValidateOnBuild` / `ValidateScopes` 检查**，如果发现 Singleton 直接注入 Scoped，**启动就报错**（帮你在部署前抓住这个 bug）。

> **面试问答**
> **Q：Singleton 里能不能注入 Scoped 的服务？为什么？**
> A：不能直接注入。Singleton 活一整个应用生命周期，如果它构造时注入了一个 Scoped 服务（比如 DbContext），这个 Scoped 实例就被单例「囚禁」了，跟着单例活一辈子——本该每请求一个的东西变成全局一个，既有跨请求数据污染，DbContext 又不是线程安全的会崩。这叫 captive dependency。.NET 在开发环境启动时的 scope 校验会直接报错。正确做法是在单例里注入 `IServiceScopeFactory`，用到时手动 `CreateScope()` 开一个作用域，从里面临时取 Scoped 服务，用完就释放。

### 5.3.5 被囚禁依赖（captive dependency）真实案例

回到 5.2.9 提到的 CP6 `DbStringLocalizer`。它是一个**多语言翻译器**：把错误码（如 `E-SEC-010`）翻成当前语言的文案，译文存在数据库 `Sys_Lang` 表里。

**设计矛盾**：
- 它想注册成 **Singleton**——因为翻译器无状态，而且译文有缓存，做成单例效率最高，不用每请求 new。
- 但它要**读数据库**（`Sys_Lang` 表），而 DbContext 是 **Scoped**。
- **如果它直接在构造函数注入 `CP6Context`，就是 Singleton 囚禁 Scoped——错误！**

**CP6 的解法**（`C:\CP6\CP6.WebApi\Localization\DbStringLocalizer.cs`）：**注入 `IServiceScopeFactory`，用到时手动开 scope**。看真实代码：

```csharp
public class DbStringLocalizer : IStringLocalizer
{
    private readonly CacheService _cache;
    private readonly IServiceScopeFactory _scopeFactory;   // ← 注入的是"作用域工厂"，不是 DbContext！
    private readonly IHttpContextAccessor _http;

    public DbStringLocalizer(CacheService cache, IServiceScopeFactory scopeFactory, IHttpContextAccessor http)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;   // 存起来，不在构造函数里碰 DbContext
        _http = http;
    }

    // …… 当缓存未命中、真的需要查库时：
    private Dictionary<string, string> LoadDict(string cacheKey, string langCode, int? tenantId)
    {
        return _cache.GetOrSetAsync(cacheKey, async () =>
        {
            using var scope = _scopeFactory.CreateScope();                       // ① 手动开一个作用域
            var ctx = scope.ServiceProvider.GetRequiredService<CP6Context>();    // ② 从作用域里临时取 Scoped 的 DbContext
            var items = await ctx.Sys_Langs.AsNoTracking()
                .Where(l => l.TenantId == tenantId)
                .ToListAsync();
            // …… 组装字典
            return result;
        }, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
    }   // ③ using 结束，scope 释放，里面那个临时 DbContext 也被正确 Dispose
}
```

看类顶部的注释，CP6 作者自己写得很清楚：

```
// 3. 复用现有 CacheService（Cache-Aside，1h TTL）；缓存未命中时用
//    IServiceScopeFactory 取 scoped CP6Context，故本类可安全注册为 Singleton。
```

**逐步解析这个模式（面试可以照着讲）**：
1. Singleton 服务**不直接注入**它需要的 Scoped 依赖，而是注入 **`IServiceScopeFactory`**（作用域工厂，它本身是 Singleton，安全）。
2. 真正需要那个 Scoped 服务时（这里是缓存未命中要查库），调 `_scopeFactory.CreateScope()` **手动开一个短命作用域**。
3. 从这个作用域的 `ServiceProvider` 里 `GetRequiredService<CP6Context>()` 临时取出一个「新鲜的」DbContext。
4. **`using` 包住 scope**，用完立即释放，里面临时借出的 DbContext 也随之被正确 Dispose。
5. 这样单例不再囚禁任何 Scoped 实例，每次查库都用一个即用即弃的干净 DbContext——**完美绕开 captive dependency**。

> **这个例子是本章最有分量的面试素材之一。** 它同时展示了：你懂生命周期规则、你知道单例读 Scoped 的正确姿势、你能读懂并解释真实生产代码。面试时描述「我们有个 DB 驱动的多语言翻译器，无状态想做单例但要查库，就注入 IServiceScopeFactory 用到时开 scope 取 DbContext」，非常加分。

### 5.3.6 后台服务里也要手动开 scope

同样的道理，**后台服务（HostedService）本身是 Singleton 级别的**（应用启动时创建一次，一直跑到关闭），所以它**也不能直接注入 Scoped 的 DbContext**。CP6 的 `OperLogCleanupService`（`C:\CP6\CP6.WebApi\BackgroundServices\OperLogCleanupService.cs`）就是这么处理的：

```csharp
public class OperLogCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;   // ← 又是它
    // ……

    private async Task CleanupOnceAsync(int retentionDays, CancellationToken stoppingToken)
    {
        var cutoff = DateTime.Now.AddDays(-retentionDays);
        using var scope = _scopeFactory.CreateScope();                    // 手动开 scope
        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();  // 取 Scoped DbContext
        var deleted = await db.Sys_OperLogs
            .IgnoreQueryFilters()
            .Where(l => l.CreateDate < cutoff)
            .ExecuteDeleteAsync(stoppingToken);
        // …… scope 结束自动释放
    }
}
```

**同一个模式在 CP6 出现了很多次**：单例（含后台服务）要用 Scoped → 注入 `IServiceScopeFactory` → `CreateScope()` → 从 scope 里取 → `using` 释放。记住它，因为面试官很可能追问「那后台任务里怎么用 DbContext？」——答案就是这个。

### 5.3.7 一接口多实现

有时候一个接口有**很多个实现**，你想全都拿到。CP6 的工作流引擎就是这样——一个流程图有很多种节点（开始/审批/结束/并行分叉/并行汇聚/服务任务/子流程……），每种节点一个 `INodeHandler` 实现。Program.cs 第 130~139 行：

```csharp
builder.Services.AddScoped<CP6.Core.Services.Wf.INodeHandler, CP6.Core.Services.Wf.StartNodeHandler>();
builder.Services.AddScoped<CP6.Core.Services.Wf.INodeHandler, CP6.Core.Services.Wf.ApprovalNodeHandler>();
builder.Services.AddScoped<CP6.Core.Services.Wf.INodeHandler, CP6.Core.Services.Wf.EndNodeHandler>();
builder.Services.AddScoped<CP6.Core.Services.Wf.INodeHandler, CP6.Core.Services.Wf.ParallelSplitNodeHandler>();
builder.Services.AddScoped<CP6.Core.Services.Wf.INodeHandler, CP6.Core.Services.Wf.ParallelJoinNodeHandler>();
// …… 十几个 INodeHandler 实现，全注册到同一个接口
```

**同一个接口注册了多个实现**。消费方注入 **`IEnumerable<INodeHandler>`** 就能拿到**全部实现的列表**：

```csharp
// 引擎里大致是这样用（示意）：
public NodeDispatcher(IEnumerable<INodeHandler> handlers)  // 拿到所有 handler
{
    _handlers = handlers.ToList();
}
public INodeHandler Resolve(string nodeType)
    => _handlers.First(h => h.CanHandle(nodeType));  // 按节点类型挑一个
```

这是「**策略模式 + DI**」的经典组合：加一种新节点类型，只要写个新 `INodeHandler` 实现 + 在 Program.cs 加一行注册，引擎代码一字不改（开闭原则）。

### 5.3.8 Keyed Services（.NET 8 新特性）

.NET 8 引入了 **keyed services（带键服务）**：同一接口的多个实现，用「键」区分，注入时指定键取特定那个。

```csharp
// 注册（.NET 8 语法示意）
builder.Services.AddKeyedScoped<INotifier, EmailNotifier>("email");
builder.Services.AddKeyedScoped<INotifier, SmsNotifier>("sms");

// 消费
public class OrderService([FromKeyedServices("email")] INotifier notifier) { }
```

- **`IEnumerable<T>` vs keyed 的区别**：`IEnumerable<T>` 是「我全都要，自己遍历/挑」；keyed 是「我只要键为 X 的那一个」。
- CP6 目前主要用 `IEnumerable<T>` 模式（如上面的 `INodeHandler`）和「配置切实现」模式，keyed services 是 .NET 8 提供的新选择。面试时能说出「.NET 8 加了 keyed services，同接口多实现可以按 key 精确取，而不用 IEnumerable 全取再筛」就够了。

### 5.3.9 工厂注册 `sp => ...`

当创建一个服务需要「在注册时做点逻辑」时，用**工厂委托**注册：`AddXxx<接口>(sp => 创建实例)`，`sp` 是 `IServiceProvider`（可以从里面再取别的服务）。CP6 有几个真实的、非用工厂不可的场景：

**场景 1：一个实例，两个身份**（Program.cs 第 101~102 行）：

```csharp
builder.Services.AddSingleton<KafkaProducerService>();                              // 具体类注册一次
builder.Services.AddSingleton<IOperLogTransport>(sp => sp.GetRequiredService<KafkaProducerService>());  // 接口指向同一个实例
```
如果直接写 `AddSingleton<IOperLogTransport, KafkaProducerService>()` 再 `AddSingleton<KafkaProducerService>()`，会得到**两个不同的 KafkaProducerService 实例**（一个连接池就变两个）。用工厂 `sp => sp.GetRequiredService<KafkaProducerService>()` 保证「接口」和「具体类」解析到**同一个实例**。

**场景 2：容器选不出构造函数**（Program.cs 第 569~573 行）：

```csharp
// BCryptPasswordHasher 有 (int=11) 与 (IOptions) 两个公共构造器，内置容器无法择一。工厂绕开构造器选择。
builder.Services.AddScoped<CP6.Core.Services.Sys.IPasswordHasher>(sp =>
    new CP6.Core.Services.Sys.BCryptPasswordHasher(
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CP6.Core.Services.Sys.SecurityOptions>>()));
```
`BCryptPasswordHasher` 有两个构造函数，DI 容器不知道该用哪个（会报 ambiguous 错误）。工厂里**显式 new + 显式挑构造函数**，一劳永逸。

**场景 3：构造时需要读配置算参数**（Program.cs 第 296~304 行）：

```csharp
builder.Services.AddScoped<CP6.Core.Services.Pub.IAttachmentService>(sp =>
{
    var maxMb = builder.Configuration.GetValue<int?>("Attachment:MaxSizeMb") ?? 20;
    var exts = builder.Configuration.GetSection("Attachment:AllowedExt").Get<string[]>();
    return new CP6.Core.Services.Pub.AttachmentService(
        sp.GetRequiredService<CP6.Core.EFDbContext.CP6Context>(),
        sp.GetRequiredService<CP6.Core.Services.Pub.IFileStore>(),
        maxMb, exts);   // 把从配置读来的参数传进构造函数
});
```

> **面试问答**
> **Q：什么时候需要用工厂委托 `sp => ...` 注册服务，而不是 `AddScoped<接口,实现>()`？**
> A：三种情况。① 想让「接口」和「具体类」解析到同一个实例（`sp => sp.GetRequiredService<具体类>()`）；② 类有多个构造函数容器选不出来，工厂里显式 new 指定；③ 构造时需要读配置计算参数再传进去。我们 CP6 里 Kafka 生产者（一实例两身份）、BCrypt 哈希器（多构造函数）、附件服务（读配置传大小限制）都用了工厂注册。

---

## 5.4 配置系统

### 5.4.1 概念：IConfiguration 与配置源链

**`IConfiguration` 是一个键值对的分层字典**，把「配置从哪来」和「代码怎么读」解耦。代码里读配置永远是一样的写法，不管值来自 JSON 文件还是环境变量。

**分层键**用冒号 `:` 表示层级。比如 `appsettings.json`：

```json
{
  "ConnectionStrings": { "DefaultConnection": "Server=...;Database=CP6;" },
  "JWT": { "Issuer": "CP6", "Audience": "CP6Client", "Secret": "超长密钥" },
  "OperLog": { "IncludeGet": false, "RetentionDays": 7 }
}
```

读法：
```csharp
builder.Configuration.GetConnectionString("DefaultConnection");        // ConnectionStrings:DefaultConnection 的语法糖
builder.Configuration["JWT:Issuer"];                                    // 用冒号下钻
builder.Configuration.GetSection("OperLog").GetValue<int?>("RetentionDays") ?? 7;  // 取段 + 带默认值
```

CP6 里到处是这种读法，比如 `OperLogFilter` 读 `OperLog:IncludeGet`、`OperLogCleanupService` 读 `OperLog:RetentionDays`。

### 5.4.2 配置源与优先级链

`IConfiguration` 由**多个配置源（source）叠加**而成，像图层一样，**后面的盖前面的**。`CreateBuilder` 默认的叠放顺序（低 → 高）：

```
① appsettings.json                （基础，进 Git）
② appsettings.{Environment}.json  （按环境覆盖，如 appsettings.Development.json）
③ 用户机密 User Secrets           （仅开发环境）
④ 环境变量 Environment Variables  （部署时注入，Docker 靠这个）
⑤ 命令行参数 Command-line args    （最高，临时覆盖）
        ↓ 优先级递增，同名键后者胜
```

**关键认知**：**同一个键在多个源里都有，取优先级最高那个源的值**。这就是为什么 Docker 部署时能用环境变量 `ConnectionStrings__DefaultConnection` 覆盖 JSON 里的默认连接串——环境变量优先级比 JSON 高。

### 5.4.3 环境变量的 `__` 约定

环境变量名里**不能有冒号** `:`（很多操作系统/shell 不允许）。约定用**双下划线 `__`** 代替冒号来表达层级：

```
配置键：      ConnectionStrings:DefaultConnection
环境变量名：  ConnectionStrings__DefaultConnection   ← 双下划线

配置键：      JWT:Secret
环境变量名：  JWT__Secret
```

CP6 在 `docker-compose.yml` 里就是用 `ConnectionStrings__DefaultConnection=Server=sqlserver;...` 这样的环境变量把生产数据库地址注入容器的。**这个 `__` 约定是面试常问的细节。**

### 5.4.4 IOptions&lt;T&gt; 强类型配置模式

直接用字符串键 `Configuration["A:B:C"]` 读配置容易拼错、没类型检查。更好的方式是**把一段配置绑定到一个强类型类**，这就是 **Options 模式**。

CP6 的真实例子（Program.cs 第 568 行）：

```csharp
// 把 appsettings 的 "Security" 段绑定到 SecurityOptions 类
builder.Services.Configure<CP6.Core.Services.Sys.SecurityOptions>(builder.Configuration.GetSection("Security"));
```

之后任何服务想用这段配置，注入 **`IOptions<SecurityOptions>`**：

```csharp
// CsrfMiddleware 就是这么拿 CSRF 开关的：
public CsrfMiddleware(RequestDelegate next, IOptions<SecurityOptions> opt)
{
    _next = next;
    _enabled = opt.Value.Csrf.Enabled;   // .Value 拿到强类型对象，点出来
}
```

**逐点解析**：
- `Configure<T>(section)` 把配置段绑到类 `T`（属性名对应 JSON 键）。
- 消费方注入 `IOptions<T>`，`.Value` 拿到那个强类型对象——**有智能提示、有编译期类型检查，不会拼错键**。
- CP6 里 `AuthController` 注入 `IOptions<SecurityOptions>`、`CsrfMiddleware` 注入它、`BCryptPasswordHasher` 也注入它——**一处配置，多处强类型消费**。

**`IOptions` 家族**（面试可能问区别）：
- `IOptions<T>`：**单例**，应用启动时读一次，之后不变（最常用）。
- `IOptionsSnapshot<T>`：**Scoped**，每请求读一次，支持配置热更新。
- `IOptionsMonitor<T>`：**单例**但能监听变更、随时拿最新值 + 变更回调。

### 5.4.5 精读：appsettings.Local.json 插源事故（完整版）

现在把 5.2.3 那段事故彻底讲透，因为它是**配置系统 + 容器部署**的顶级面试素材。再看 Program.cs 第 19~39 行的核心逻辑（配合前面贴的注释）：

**事故还原**：
1. 团队想加一个 `appsettings.Local.json` 存本地开发用的数据库密码（这文件在 `.gitignore` 里，不进仓库）。
2. **最自然的写法**是 `builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true)`。
3. 但 `AddJsonFile` 把新源**追加到源链末尾**——而末尾是**最高优先级**。于是 Local.json 的优先级**盖过了环境变量**。
4. 本地开发看不出问题（本地本来就靠 Local.json）。
5. **部署到 Docker 后爆炸**：容器靠环境变量 `ConnectionStrings__DefaultConnection` 注入生产库地址，但如果镜像里残留了 Local.json（或 Local.json 里有旧连接串），环境变量被**静默覆盖**，应用连到了错误/不存在的数据库，起不来。

**根治代码逻辑**（就是 5.2.3 贴的那段）：
- 不用 `AddJsonFile`（会加到链尾）。
- 手动 new 一个 `JsonConfigurationSource`。
- 遍历 `builder.Configuration.Sources` 找到「环境变量源」`EnvironmentVariablesConfigurationSource` 的下标。
- 用 `Sources.Insert(envVarIdx, localJsonSource)` 把 Local.json **插到环境变量源之前**。
- 结果优先级恢复成：JSON 基础 → Local.json → **环境变量（最高）** → 命令行。环境变量重新压过 Local.json，Docker 部署正常。

**注释里还有个细节**：`Sources` 是 `IList<IConfigurationSource>`，没有 `List<T>.FindIndex` 方法，所以只能手写 for 循环找下标——这种「读源码发现 API 限制」的细节，面试讲出来特别真实。

> **面试问答**
> **Q：讲一个你踩过或见过的配置相关的坑。**
> A：我们项目加了一个 `appsettings.Local.json` 存本地数据库密码，一开始直接用 `AddJsonFile` 追加，本地没问题。但部署到 Docker 后连不上数据库——因为 `AddJsonFile` 把新源加到了配置链最末尾，也就是最高优先级，把容器注入的环境变量连接串给盖掉了。根治办法是手动把这个 JSON 源**插到环境变量源之前**，恢复「环境变量最高」的标准优先级。这个坑让我彻底搞懂了配置源是有优先级顺序的、后加的源默认优先级更高、以及容器化部署严重依赖环境变量。

---

## 5.5 中间件管道

### 5.5.1 概念：洋葱模型

**中间件（middleware）** 是一段处理请求的代码，多个中间件串成一条**管道（pipeline）**。每个中间件可以：① 在请求进来时做点事 → ② 调用「下一个」中间件 → ③ 在响应回来时再做点事。

这形成了一个**洋葱模型（onion model）**：请求从外层穿到里层，响应再从里层穿回外层，**同一个中间件的代码被穿过两次**（去程一次、回程一次）。

```
              请求 Request 进入
                    │
   ┌────────────────▼─────────────────┐
   │ UseCors            [去程①]        │
   │  ┌──────────────▼──────────────┐ │
   │  │ UseAuthentication [去程②]    │ │
   │  │  ┌───────────▼────────────┐ │ │
   │  │  │ TenantMiddleware [去程③]│ │ │
   │  │  │  ┌────────▼─────────┐  │ │ │
   │  │  │  │ CsrfMiddleware   │  │ │ │
   │  │  │  │   ┌────▼─────┐   │  │ │ │
   │  │  │  │   │ 控制器    │   │  │ │ │  ← 最内层：真正的业务
   │  │  │  │   │ Action   │   │  │ │ │
   │  │  │  │   └────┬─────┘   │  │ │ │
   │  │  │  │ [回程] await next│  │ │ │
   │  │  │  └────────▲─────────┘  │ │ │
   │  │  │ [回程]                  │ │ │
   │  │  └───────────▲────────────┘ │ │
   │  │ [回程]                       │ │
   │  └──────────────▲──────────────┘ │
   │ [回程]                            │
   └────────────────▲─────────────────┘
                    │
              响应 Response 返回
```

一个中间件的典型代码形态（就是「洋葱的一层」）：

```csharp
public async Task Invoke(HttpContext ctx)
{
    // 【去程】await next 之前的代码：请求进来时执行（从外到内）
    ...做点事...
    await _next(ctx);   // 【调用下一层】把控制权交给内层
    // 【回程】await next 之后的代码：响应回来时执行（从内到外）
    ...再做点事...
}
```

CP6 的 `OperLogFilter`（虽然是过滤器不是中间件，但结构一样）就完美演示了「去程记开始时间，回程算耗时」：`var stopwatch = Stopwatch.StartNew();`（去程）→ `await next();`（进内层）→ `stopwatch.Stop();`（回程算 `ElapsedMs`）。

### 5.5.2 Use / Run / Map

三个组装管道的方法（面试常问区别）：

- **`Use`**：加一个「会调用下一层」的中间件（洋葱的一层，能穿过去也能穿回来）。绝大多数是这个。
- **`Run`**：加一个「**终结点**」中间件——它不调用 `next`，管道到此为止（短路）。
- **`Map`**：**按路径分支**。`app.Map("/admin", ...)` 把 `/admin` 开头的请求引到另一条子管道。CP6 用 `MapControllers()`（路由到控制器）、`MapHub<NotifyHub>("/hubs/notify")`（路由到 SignalR Hub）、`MapMetrics()`（Prometheus 指标端点）——这些 `MapXxx` 都是「终结点路由」，把特定路径交给对应处理器。

### 5.5.3 执行顺序为什么重要

**中间件的注册顺序 = 请求经过它们的顺序**（去程按注册顺序，回程逆序）。**顺序错了，功能就废了或有安全漏洞。** 这是中间件面试的核心。

看 CP6 真实的管道组装（`Program.cs` 第 2669~2707 行）和它每一步为什么必须在这个位置：

```csharp
app.UseCors("AllowAll");                                           // ① 跨域：最早，连预检请求都要处理
app.UseHttpMetrics();                                             // ② 指标采集：路由后端点前
app.UseAuthentication();                                          // ③ 认证：解析 JWT，填充 User —— 必须在所有"需要知道你是谁"的中间件之前
app.UseMiddleware<TenantMiddleware>();                           // ④ 租户解析：从 User 的 tenant_id claim 定租户 —— 必须在 ③ 之后（要先有 User）
app.UseRequestLocalization(locOptions);                         // ⑤ 本地化：culture 来源之一是 User 的 lang claim —— 也要在 ③ 之后
app.UseMiddleware<BizExceptionMiddleware>();                    // ⑥ 业务异常→本地化响应 —— 必须在 ⑤ 之后(要用 culture)、在会抛 BizException 的下游之前(要能 catch 到)
app.UseMiddleware<CsrfMiddleware>();                            // ⑦ CSRF 校验 —— 在 ⑥ 下游(抛的异常被 ⑥ 本地化)、在授权 ⑧ 之前
app.UseMiddleware<MustChangePasswordMiddleware>();             // ⑧ 强制改密拦截
app.UseAuthorization();                                          // ⑨ 授权：查权限 —— 必须在认证 ③ 之后
app.MapControllers();                                            // ⑩ 终结点：路由到控制器
```

**逐条讲「为什么是这个位置」（面试金句）**：

1. **`UseAuthentication` 必须在 `UseAuthorization` 之前** —— 这是最经典的顺序规则。**认证（Authentication）是「你是谁」，授权（Authorization）是「你能干嘛」。得先知道你是谁，才能判断你能不能干。** 顺序反了，授权时 `User` 还没填充，所有需要登录的请求都会被误判为未登录。
2. **`TenantMiddleware` 在 `UseAuthentication` 之后** —— CP6 是多租户，租户信息藏在 JWT 的 `tenant_id` claim 里。必须认证先把 `User` 解析出来，租户中间件才能从 `User.FindFirst("tenant_id")` 读到租户。（看 Program.cs 第 2677 行注释：「须在 UseAuthentication 之后，User 已解析」。）
3. **`UseRequestLocalization` 在认证之后** —— CP6 的语言优先级是「用户偏好（JWT `lang` claim）> `?culture=` > Cookie > Accept-Language > 默认 ja」，第一优先级要读 JWT claim，所以也得在认证之后。
4. **`BizExceptionMiddleware` 在 `UseRequestLocalization` 之后** —— 它捕获业务异常并**用当前 culture 翻译错误码**。如果放在本地化之前，异常上抛时 culture 已经被还原，翻译会落到默认语言（看第 2681 行注释）。
5. **`CsrfMiddleware` 在授权之前、异常处理之下游** —— CSRF 失败要抛 `BizException("E-SEC-010", 403)` 让 `BizExceptionMiddleware` 本地化，所以在它下游；又要在授权之前拦住伪造请求。

> **面试问答**
> **Q：为什么 `UseAuthentication` 一定要在 `UseAuthorization` 前面？**
> A：认证是解析身份（读 JWT、填充 `HttpContext.User`），授权是基于身份判断权限。授权中间件依赖 `User` 已经被填充。如果顺序反了，授权执行时 `User` 还是空的，所有请求都会被当成匿名用户处理，要么全部 401，要么授权逻辑全错。中间件的注册顺序就是请求经过的顺序，所以必须认证在前。
>
> **Q：中间件顺序还有哪些约束？**
> A：以我们项目为例：多租户解析要在认证之后（租户 ID 在 JWT claim 里）；请求本地化也在认证之后（语言偏好来自 JWT claim）；全局异常处理要在本地化之后（异常要用当前语言翻译）且在会抛异常的业务下游（要能 catch 到）；CSRF 在授权之前。核心原则是「依赖别人产出的中间件必须排在后面」。

### 5.5.4 自定义中间件的两种写法

**写法一：约定式类（convention-based）** —— 一个普通类，有构造函数（可注入单例依赖）+ 一个 `Invoke`/`InvokeAsync(HttpContext)` 方法。CP6 全用这种，`CsrfMiddleware` 就是标准范本（见下面 5.5.5）。用 `app.UseMiddleware<CsrfMiddleware>()` 注册。

**写法二：内联委托（inline）** —— 直接用 lambda：

```csharp
app.Use(async (context, next) =>
{
    // 去程
    await next(context);
    // 回程
});
```

约定式适合有依赖、逻辑复杂、可复用的中间件；内联适合简单的一次性逻辑。**CP6 生产代码一律用约定式类**（可测试、可复用、依赖注入干净）。

> **一个易错点**：约定式中间件的**构造函数**是应用启动时调一次（单例级别），所以构造函数只能注入 **Singleton** 依赖。要用 Scoped 依赖（如 DbContext），得注入到 **`Invoke` 方法的参数**里（方法是每请求调用，参数按请求作用域解析）。看 CP6 `BizExceptionMiddleware`：`IStringLocalizer` 是注入到 `InvokeAsync(HttpContext context, IStringLocalizer localizer)` 的**方法参数**，而不是构造函数——这样每请求解析一次，安全。

### 5.5.5 精读 CsrfMiddleware（CSRF 攻击原理 + 防护 + 豁免事故）

这是本章**安全部分最重要的标本**，也牵出一个真实生产事故。先讲原理。

**CSRF（Cross-Site Request Forgery，跨站请求伪造）是什么？**

场景：你登录了银行网站 `bank.com`，浏览器存了你的登录 Cookie。你没退出，又去逛一个恶意网站 `evil.com`。`evil.com` 页面里藏了一个自动提交的表单：

```html
<form action="https://bank.com/transfer" method="POST">
  <input name="to" value="黑客账户"><input name="amount" value="10000">
</form>
<script>document.forms[0].submit()</script>  <!-- 自动提交 -->
```

浏览器提交这个表单到 `bank.com` 时，**会自动带上你在 bank.com 的 Cookie**（浏览器就是这么工作的）。于是银行以为是你本人发的转账请求，钱就被转走了。**这就是 CSRF：利用「浏览器自动带 Cookie」的特性，从第三方站点伪造你的身份发请求。**

**为什么 CP6 会有这个风险？** 因为 CP6 做了安全加固，把 JWT access token 存进了 **httpOnly Cookie**（`cp6_at`）——好处是 JavaScript 读不到 token（防 XSS 偷 token），但坏处是**浏览器会自动带这个 Cookie**，于是有了 CSRF 面。

**防护方案：双提交 Cookie（double-submit cookie）**。原理：
- 登录时，服务器除了 httpOnly 的 token cookie，再下发一个**非 httpOnly** 的 CSRF cookie（`cp6_csrf`，JS 能读）。
- 前端每次发「不安全」请求（POST/PUT/PATCH/DELETE）时，用 JS 读出 CSRF cookie 的值，塞进请求头 `X-CSRF-Token`。
- 服务器校验：**cookie 里的 CSRF 值 == 请求头里的 X-CSRF-Token 值** → 放行；不等或缺失 → 403。
- **为什么这能防住？** 跨站攻击者的 `evil.com` 页面：① 读不到你 bank.com 的 CSRF cookie（跨域，同源策略挡着）；② 也没法设置自定义请求头 `X-CSRF-Token`（HTML 表单发不了自定义头）。所以攻击者**没法让 cookie 和 header 对上**，请求被 403 拦下。合法的前端因为读得到自己域的 cookie，能对上。

看 CP6 `CsrfMiddleware` 真实代码（`C:\CP6\CP6.WebApi\Middleware\CsrfMiddleware.cs`）核心逻辑：

```csharp
public async Task Invoke(HttpContext ctx)
{
    if (_enabled)   // 开关 Security:Csrf:Enabled
    {
        var path = ctx.Request.Path.Value ?? "";
        // 只对"不安全方法"且"非豁免路径"校验
        if (!IsExempt(path) && UnsafeMethods.Contains(ctx.Request.Method.ToUpperInvariant()))
        {
            var cookie = ctx.Request.Cookies[AuthCookieWriter.CsrfCookie];   // cp6_csrf cookie
            var header = ctx.Request.Headers["X-CSRF-Token"].ToString();     // 请求头
            if (string.IsNullOrEmpty(cookie) || cookie != header)
                throw new BizException("E-SEC-010", 403);   // 双提交不匹配 → 403
        }
    }
    await _next(ctx);
}

private static readonly string[] UnsafeMethods = { "POST", "PUT", "PATCH", "DELETE" };
```

- `UnsafeMethods`：只有会改数据的方法才校验，GET/HEAD 这种安全（幂等只读）方法直接放行。
- `cookie != header` 就 403——双提交比对。
- 抛 `BizException("E-SEC-010", 403)` 而不是直接写响应——交给上游 `BizExceptionMiddleware` 本地化成当前语言（这就是 5.5.3 说的「CSRF 在异常中间件下游」的原因）。

**现在讲真实事故——豁免设计**。有些端点**不能**校验 CSRF，否则会误伤。CP6 的 `IsExempt`：

```csharp
internal static bool IsExempt(string path)
    => PathMatches(path, "/api/auth/login")        // ① 登录端点：此刻还没有 CSRF cookie
       || PathMatches(path, "/hubs")               // ② SignalR hub：negotiate 是 POST 但不改业务
       || IsFlowTriggerFirePath(path);             // ③ 消息触发器 fire 端点：外部系统调用
```

**三个豁免各有故事**：

- **① 登录端点** `/api/auth/login`：登录时用户**还没拿到 CSRF cookie**（cookie 是登录成功才下发的），如果校验必然失败，没法登录。所以豁免。注意用 `PathMatches`（段边界匹配）而不是 `StartsWith`，防止 `/api/auth/login-xxx` 这种同前缀端点被误豁免。

- **② SignalR `/hubs` 前缀（票11 事故）**：SignalR 建连要先发一个 `POST /hubs/notify/negotiate`（协商传输方式）。这是 POST（不安全方法），如果不豁免会被 CSRF 中间件 403 拦死，**结果实时通知整个连不上**。CP6 记忆库里这就是「票11」事故——线上 negotiate 被 403。修复是豁免 `/hubs` 前缀。豁免的安全论据（代码注释里写得很清楚）：4 个 hub 都没有状态变更方法，只有订阅/退订组操作，即便被跨站触发也无副作用；且 CORS 有显式白名单挡跨站。**注释里还留了前瞻警示**：将来若给 hub 加了可调用的状态变更方法，必须重新评估这个豁免——这种「给未来的自己留警告」的工程素养，面试讲出来很加分。

- **③ 消息触发器 fire 端点（波③终审事故——你的重点故事）**：CP6 工作流有个「外部系统回调触发流程」的端点 `POST /api/oa/flow-triggers/{guid}/fire`。它是给**外部系统**（不是浏览器）调用的，用自定义头 `X-Api-Key` 认证，**不带任何 cookie**。

  **事故经过**：一开始没豁免它。生产环境 `Csrf.Enabled=true` 时，外部系统调这个端点——外部系统没有浏览器、没有 CSRF cookie——直接被 403 `E-SEC-010` 拦死，**消息触发功能整体失效，成了「生产 403 死路」**。而且这个 bug 被三层测试都遮蔽了（测试环境 CSRF 没开），直到线上才暴露。

  **修复**：精确豁免这个端点。但豁免设计极其讲究——看 `IsFlowTriggerFirePath`：

  ```csharp
  internal static bool IsFlowTriggerFirePath(string path)
  {
      const string prefix = "/api/oa/flow-triggers/";
      const string suffix = "/fire";
      if (path.Length <= prefix.Length + suffix.Length) return false;
      if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
      if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
      var idSegment = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
      return Guid.TryParse(idSegment, out _);   // 中段必须是单个合法 GUID（含 '/' 必解析失败）
  }
  ```

  **为什么这么较真？** 因为**豁免面就是攻击面**。绝不能简单豁免 `/api/oa/flow-triggers` 整个前缀——因为同级还有 `create`/`update`/`enable`/`reset-key`/`manual-fire` 这些**管理端点，它们走 cookie 认证，必须留在 CSRF 保护里**。所以豁免做成**「形状精确匹配」**：前缀 + 字面 `/fire` 结尾 + 中间段必须是**单个合法 GUID**（用 `Guid.TryParse` 判，含 `/` 的多段路径必然解析失败，杜绝路径穿透）。这样只有 `/api/oa/flow-triggers/{一个真 guid}/fire` 命中豁免，兄弟端点一个都逃不掉。

> **这个「fire 端点 CSRF 豁免」事故是你面试的杀手锏故事。** 它一次考了：CSRF 原理、双提交防护、httpOnly cookie 的权衡、外部系统认证（API Key）、豁免即攻击面、精确匹配防穿透、以及「测试环境遮蔽 bug、线上才暴露」的血泪教训。练熟它。
>
> **面试问答**
> **Q：什么是 CSRF？怎么防？**
> A：跨站请求伪造。因为浏览器发请求会自动带上目标站点的 Cookie，攻击者可以在第三方恶意页面放一个自动提交的表单，伪造你的身份向你已登录的站点发写请求。我们的防护是双提交 Cookie：登录时下发一个非 httpOnly 的 CSRF cookie，前端每次写请求把它读出来塞进 `X-CSRF-Token` 头，服务端校验 cookie 值和 header 值相等才放行。攻击者跨站既读不到你的 CSRF cookie 也设不了自定义头，就伪造不出匹配的请求。
>
> **Q：CSRF 中间件哪些端点要豁免，豁免时要注意什么？**
> A：登录端点（登录时还没 CSRF cookie）、SignalR 的 negotiate（POST 但无副作用，且外部系统/浏览器场景不同）、还有给外部系统用的 API Key 认证的回调端点（它不带 cookie，CSRF 模型不成立）。豁免最大的坑是「豁免即攻击面」——我们踩过一个坑：一个外部回调 fire 端点没豁免，生产开了 CSRF 后被 403 拦死，功能整体失效；修复时又不能简单豁免整个前缀，因为同级的管理端点是 cookie 认证必须保护，所以做成了「前缀+/fire 结尾+中间段必须是单个 GUID」的形状精确匹配，防止路径穿透误豁免兄弟端点。

---

## 5.6 控制器与路由

### 5.6.1 概念：控制器是什么

**控制器（Controller）** 是一组相关 API 端点（action）的容器。CP6 的 `StockController` 就把「库存查询、库存变动、棚移动、交易履历」这些相关操作放一起。看 `C:\CP6\CP6.WebApi\Controllers\Wms\StockController.cs` 的类头：

```csharp
[ApiController]
[Route("api/wms/stock")]
[Authorize]
public class StockController : ControllerBase
{
    private readonly CP6Context _db;
    private readonly IStockMovementService _mover;
    public StockController(CP6Context db, IStockMovementService mover)  // 构造注入
    {
        _db = db;
        _mover = mover;
    }
    // …… 各个 action
}
```

- 继承 `ControllerBase`（API 用这个；`Controller` 是给带视图的 MVC 用的，API 不需要视图）。
- 三个类级特性 `[ApiController]`、`[Route(...)]`、`[Authorize]` 下面逐个讲。

### 5.6.2 特性路由（Attribute Routing）

**路由决定「哪个 URL 走到哪个方法」**。CP6 用特性路由（把路由写在方法上方的特性里）：

```csharp
[Route("api/wms/stock")]           // 类级：这个控制器的基础路径
public class StockController ...
{
    [HttpGet]                       // GET  api/wms/stock
    public async Task<IActionResult> Search(...) { }

    [HttpGet("{stockId:guid}/history")]   // GET api/wms/stock/{stockId}/history
    public async Task<IActionResult> History(Guid stockId, ...) { }

    [HttpPost("apply")]             // POST api/wms/stock/apply
    public async Task<IActionResult> Apply(...) { }

    [HttpPost("move")]              // POST api/wms/stock/move
    public async Task<IActionResult> Move(...) { }
}
```

- 类级 `[Route("api/wms/stock")]` 定基础路径；方法级 `[HttpGet]`/`[HttpPost("apply")]` 在它后面拼接。
- 最终 URL = 类路径 + 方法路径。`[HttpPost("apply")]` → `POST api/wms/stock/apply`。
- **`[HttpGet]` vs `[HttpPost]`** 既指定了 HTTP 方法，又指定了路径后缀，二合一。

`AuthController` 还展示了 `[Route("api/[controller]")]` 这种 **token 替换**写法——`[controller]` 会被替换成控制器名去掉 `Controller` 后缀，即 `Auth`，所以路径是 `api/auth`。

### 5.6.3 路由约束（Route Constraints）

路由参数可以加**约束**，限定类型/格式：

```csharp
[HttpGet("{stockId:guid}/history")]   // stockId 必须是合法 GUID，否则根本不匹配这个路由
public async Task<IActionResult> History(Guid stockId, ...) { }
```

`{stockId:guid}` 里的 `:guid` 就是约束——只有 URL 里那段是合法 GUID 才命中这个 action。常见约束：`:int`、`:guid`、`:min(1)`、`:alpha`、`:length(5)` 等。好处：过滤非法请求于路由层，进不了 action，还能消歧（同一路径不同类型走不同 action）。

（注意：5.5.5 的 `IsFlowTriggerFirePath` 里 `Guid.TryParse` 用的判据，就是刻意和路由约束 `{id:guid}` 保持一致。）

### 5.6.4 参数绑定四来源

ASP.NET Core 把 HTTP 请求的各部分**绑定**到 action 方法的参数上。四个主要来源（面试常问）：

| 特性 | 从哪取 | CP6 例子 |
|------|--------|---------|
| `[FromQuery]` | URL 查询字符串 `?a=1&b=2` | `Search([FromQuery] string? warehouseCd, [FromQuery] int page = 1)` |
| `[FromRoute]` | URL 路径段 | `History(Guid stockId)` —— 来自 `{stockId}` |
| `[FromBody]` | 请求体 JSON | `Apply([FromBody] StockMovementRequest req)` |
| `[FromHeader]` | 请求头 | 读自定义头，如 `X-Api-Key` |

看 `StockController.Search` 的真实签名，全是 `[FromQuery]`（因为查询用 GET，参数在 URL 里）：

```csharp
[HttpGet]
public async Task<IActionResult> Search(
    [FromQuery] string? warehouseCd,
    [FromQuery] string? locationCd,
    [FromQuery] string? productCd,
    [FromQuery] bool? hasStockOnly,
    [FromQuery] int page = 1,          // 有默认值 → 可选
    [FromQuery] int pageSize = 50)
{ ... }
```

而 `Apply` 用 `[FromBody]`（POST 提交对象，在请求体里）：

```csharp
[HttpPost("apply")]
public async Task<IActionResult> Apply([FromBody] StockMovementRequest req, CancellationToken ct)
{ ... }
```

- `[FromBody]` **一个 action 最多一个**（请求体只有一份）。
- `[ApiController]` 特性下，框架会**智能推断**来源：复杂类型默认 `[FromBody]`，简单类型默认 `[FromQuery]`（所以很多时候不写特性也能对）。CP6 显式写出来是为了清晰。
- `CancellationToken ct` 是框架特殊参数（不来自请求，是框架传的取消令牌，用于请求中断时取消数据库操作）。

### 5.6.5 模型验证（DataAnnotations + ModelState）

请求体对象可以用**数据注解（DataAnnotations）** 声明验证规则：

```csharp
public class StockMovementRequest
{
    [Required] public string WarehouseCd { get; set; }
    [Range(0.01, double.MaxValue)] public decimal Qty { get; set; }
    [StringLength(50)] public string? Remark { get; set; }
}
```

- `[Required]`（必填）、`[Range]`（范围）、`[StringLength]`（长度）、`[EmailAddress]` 等。
- 验证结果存在 `ModelState`。没有 `[ApiController]` 时你得手动 `if (!ModelState.IsValid) return BadRequest(ModelState);`；**有了 `[ApiController]`，验证失败会自动返回 400**（见下）。

### 5.6.6 [ApiController] 的自动行为

类头那个 `[ApiController]` 特性开启了一堆 Web API 便利行为（面试点）：

1. **自动模型验证**：`ModelState` 无效时自动返回 `400 Bad Request`，不用你手写检查。
2. **自动推断参数来源**：复杂类型→Body，简单类型→Query/Route，少写很多 `[FromXxx]`。
3. **要求特性路由**：必须用 `[Route]`/`[HttpGet]` 等，不支持约定式路由。
4. **ProblemDetails 错误格式**：错误响应默认用 RFC 7807 标准格式。

### 5.6.7 IActionResult 家族与统一响应

action 的返回类型 `Task<IActionResult>` 里的 `IActionResult` 是「HTTP 响应的抽象」，有一大家子：

- `Ok(obj)` → 200 + body
- `BadRequest(obj)` → 400
- `NotFound(obj)` → 404
- `Redirect(url)` → 302（CP6 SSO 回调用它）
- `StatusCode(403, obj)` → 任意状态码
- `NoContent()` → 204

**CP6 的统一响应约定**：所有正常返回都包成 `{ code, message, data }` 信封。看 `StockController` 的真实返回：

```csharp
// 成功
return Ok(new { code = 0, message = "OK", data = new { total, page, pageSize, items } });
// 库存不足（业务错）
return BadRequest(new { code = 400, message = ex.Message });
// 找不到（注意 message 用的是多语言键 WM-MSG-070，不是硬编码文案）
return NotFound(new { code = 404, message = "WM-MSG-070" });
```

- `code = 0` 表示业务成功，`data` 装真正的数据。前端统一按这个信封解析。
- 出错时 `message` 装的是**多语言消息键**（`WM-MSG-070`、`WM-MSG-071`），不是写死的中文/日文——由前端或本地化层翻译。这个统一响应 + 消息键设计 5.12 会完整讲。

### 5.6.8 内容协商（Content Negotiation）

**内容协商**：客户端通过 `Accept` 请求头说「我想要什么格式」（`application/json` / `application/xml`），服务器据此选格式化器（formatter）序列化响应。ASP.NET Core 默认只配 JSON（`System.Text.Json`），CP6 也是纯 JSON API。如果需要支持 XML，得额外 `AddXmlSerializerFormatters()`。面试知道「内容协商 = 按 Accept 头选响应格式，默认 JSON」即可。

> **面试问答**
> **Q：`[ApiController]` 特性做了什么？**
> A：开启 Web API 的一组便利行为：模型验证失败自动返回 400（不用手写 `ModelState.IsValid` 检查）、自动推断参数绑定来源（复杂类型走 Body、简单类型走 Query/Route）、强制使用特性路由、以及标准化的错误响应格式。
>
> **Q：`[FromBody]` 和 `[FromQuery]` 什么区别，一个 action 能有几个 `[FromBody]`？**
> A：`[FromBody]` 从请求体反序列化（通常是 POST/PUT 的 JSON 对象），`[FromQuery]` 从 URL 查询字符串取（通常 GET 的过滤/分页参数）。一个 action 最多一个 `[FromBody]`，因为请求体只有一份。我们的库存查询接口全用 `[FromQuery]` 接分页和过滤条件，库存变动接口用 `[FromBody]` 接请求对象。

---

## 5.7 过滤器管道

### 5.7.1 概念：五种过滤器与执行顺序

**过滤器（Filter）** 是在 MVC 处理请求的**特定阶段**插入逻辑的机制。和中间件的区别：中间件是「管道级」（所有请求都过，不懂 MVC 概念）；过滤器是「MVC 级」（知道控制器、action、模型绑定结果，能拿到 `ActionArguments`）。

五种过滤器，按执行顺序：

```
请求进入 MVC
   │
   ▼
① Authorization Filter（授权）   ← 最先跑，决定"能不能进"。CP6 的 RequirePermissionAttribute 是这类
   │  （不通过 → 直接短路返回，后面全不跑）
   ▼
② Resource Filter（资源）        ← 模型绑定前后都跑，可用于缓存、短路
   │
   ▼   [模型绑定 Model Binding 发生在这里]
   │
③ Action Filter（动作）          ← 环绕 action 方法执行前后。CP6 的 OperLogFilter 是这类
   │   ┌── OnActionExecuting（action 前）
   │   ├── ★ Action 方法本体执行 ★
   │   └── OnActionExecuted（action 后）
   ▼
④ Exception Filter（异常）        ← action 抛异常时兜底
   │
   ▼
⑤ Result Filter（结果）          ← 环绕结果（IActionResult）的执行前后
   │
   ▼
响应返回
```

**去程顺序** ①→②→③→⑤，**回程逆序**（洋葱模型同理）。**Authorization 永远第一**——所以 CP6 的权限校验 `RequirePermissionAttribute` 做成授权过滤器，能在最早期把无权请求挡掉，连模型绑定都不浪费。

### 5.7.2 三级注册

过滤器可以注册在三个层级（范围递减）：

1. **全局**（所有控制器所有 action）：`Program.cs` 里 `options.Filters.AddService<OperLogFilter>()`——CP6 的操作日志就是全局注册，每个请求都记。
2. **控制器级**（贴在 class 上）：`[Authorize]`、`[RequirePermission(...)]` 贴在控制器上，对该控制器所有 action 生效。
3. **Action 级**（贴在方法上）：`[RequirePermission("wms-stock", "adjust")]` 贴在单个方法上，只管那个 action。

CP6 三级都用了：`OperLogFilter` 全局、`[Authorize]` 控制器级、`[RequirePermission]` action 级。

### 5.7.3 精读 OperLogFilter（零侵入操作日志 + Kafka + 降级）

`OperLogFilter`（`C:\CP6\CP6.WebApi\Filters\OperLogFilter.cs`）是一个 **Action 过滤器**，实现 `IAsyncActionFilter`。它的使命：**不改动任何业务代码，自动记录每个写操作的日志**（谁、什么时候、调了哪个接口、传了什么参数、耗时多久、成功还是失败），发到 Kafka，Kafka 挂了就降级直接写数据库。

**为什么用过滤器实现「零侵入」？** 因为它全局注册，所有 action 自动经过，业务代码里**一行日志代码都不用写**。这是「横切关注点（cross-cutting concern）」的经典解法——日志、审计、性能监控这类「每个接口都要但和业务无关」的事，用过滤器统一处理。

看核心结构（`IAsyncActionFilter` 只有一个方法 `OnActionExecutionAsync`，一个方法里同时处理「前」和「后」）：

```csharp
public class OperLogFilter : IAsyncActionFilter
{
    private readonly CP6Context _context;       // 降级写库用
    private readonly IOperLogTransport _transport;  // Kafka 通道（注入的是接口！）
    private readonly bool _includeGet;

    public OperLogFilter(CP6Context context, IOperLogTransport transport, IConfiguration config)
    {
        _context = context;
        _transport = transport;
        _includeGet = config.GetSection("OperLog").GetValue<bool?>("IncludeGet") ?? false;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();   // 【去程】开始计时

        // 【去程】采集请求参数（POST/PUT/DELETE 才采）
        string? requestBody = null;
        if (context.HttpContext.Request.Method is "POST" or "PUT" or "DELETE")
        {
            // 剔除不可序列化的参数（CancellationToken / IFormFile / Stream），否则序列化会抛异常把业务 API 也弄成 500
            var args = context.ActionArguments
                .Where(kv => kv.Value is not CancellationToken
                             && kv.Value is not IFormFile
                             && kv.Value is not IFormFileCollection
                             && kv.Value is not Stream)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (args.Count > 0)
            {
                try
                {
                    requestBody = JsonSerializer.Serialize(args);
                    if (requestBody.Length > 2000)   // 太长截断，防日志爆炸
                        requestBody = requestBody[..2000] + "...(truncated)";
                }
                catch (Exception ex) { requestBody = $"(serialize failed: {ex.GetType().Name})"; }
            }
        }

        var resultContext = await next();   // ★ 执行 action 本体（进内层）★
        stopwatch.Stop();                    // 【回程】停表

        var path = context.HttpContext.Request.Path.Value ?? "";
        // 始终跳过：登录（防密码泄露）+ 日志接口自身（防递归）
        if (path.Contains("/api/operlog", ...) || path.Contains("/api/auth", ...))
            return;
        if (context.HttpContext.Request.Method == "GET" && !_includeGet)   // GET 默认不记
            return;

        // 【回程】组装日志（从 User claim 取用户名、从路由取控制器/action、判定状态码）
        var statusCode = 200;
        if (resultContext.Exception != null) statusCode = 500;
        else if (resultContext.Result is ObjectResult objResult) statusCode = objResult.StatusCode ?? 200;

        var log = new Sys_OperLog
        {
            TenantId = _context.CurrentTenantId,   // 章10：盖当前租户戳
            UserName = context.HttpContext.User.FindFirst(ClaimTypes.Name)?.Value,
            HttpMethod = method, RequestUrl = path,
            Controller = controllerName, Action = actionName,
            RequestBody = requestBody, StatusCode = statusCode,
            ElapsedMs = stopwatch.ElapsedMilliseconds,   // 耗时
            ClientIp = clientIp, CreateDate = DateTime.Now
        };

        // 投 Kafka（操作日志专任通道）
        var published = false;
        if (_transport.IsConnected)
        {
            try { await _transport.PublishAsync(log); published = true; }
            catch (Exception ex) { Console.WriteLine($"[OperLog] 通道投递失败: {ex.Message}"); }
        }
        // ★降级：Kafka 不可用 → 直接写 DB（保证日志不丢）★
        if (!published)
        {
            try { _context.Sys_OperLogs.Add(log); await _context.SaveChangesAsync(); }
            catch (Exception ex) { Console.WriteLine($"[OperLog] 降级写DB失败: {ex.Message}"); }
        }
    }
}
```

**几个设计精华（都是面试谈资）**：

1. **旁路不阻断业务**：日志采集失败**绝不能影响主业务**。所以序列化失败 try-catch 吞掉、写库失败也只打印不抛。「记日志」再重要也是配角，不能让配角搞崩主角。
2. **剔除不可序列化参数**：`CancellationToken`、`IFormFile`、`Stream` 序列化会炸（`CancellationToken.WaitHandle.Handle` 抛 `NotSupportedException`）。**这是踩过的坑**——不剔除的话，任何带文件上传或 CancellationToken 的接口都会被这个日志过滤器搞成 500。
3. **Kafka 主 + DB 降级的双通道**：正常走 Kafka（高吞吐、异步落库），Kafka 挂了立刻降级直接写 DB，**日志一条不丢**。这个「优雅降级」是分布式系统的基本功。
4. **始终跳过登录和日志接口**：跳过 `/api/auth`（防把密码记进日志）、跳过 `/api/operlog`（防记日志的接口又触发记日志，无限递归）。
5. **多租户盖戳**：`TenantId = _context.CurrentTenantId`——日志也要归属正确的租户。

> **面试问答**
> **Q：你们怎么做操作日志/审计，业务代码里要写日志吗？**
> A：完全零侵入。我们写了一个全局注册的 Action 过滤器 `OperLogFilter`，所有 API 自动经过它，采集用户、接口、参数、耗时、状态码。业务代码一行日志都不用写。日志走 Kafka 异步落库（高吞吐、可回放），Kafka 不可用时降级直接写数据库保证不丢。有几个细节坑：要剔除 CancellationToken/IFormFile 这类不可序列化的参数否则会把业务接口弄崩、要跳过登录接口防密码泄露、要跳过日志接口自身防递归、参数太长要截断。
>
> **Q：过滤器和中间件有什么区别？**
> A：中间件是管道级的，所有请求都经过，但它不懂 MVC，拿不到「这是哪个 action、绑定出来的参数是什么」。过滤器是 MVC 管道内的，能拿到控制器、action、模型绑定后的参数（`ActionArguments`），适合做和 action 强相关的横切逻辑，比如权限校验、操作日志、结果包装。执行上过滤器在中间件更内层（路由到 MVC 之后）。

---

## 5.8 认证与授权深讲

### 5.8.1 概念：认证 vs 授权

再强调一次这对孪生概念（面试必考）：
- **认证（Authentication / AuthN）= 你是谁**。核对身份。CP6 里 = 校验 JWT 签名有效、没过期、没被拉黑 → 确认「这是用户 admin，租户 A」。
- **授权（Authorization / AuthZ）= 你能干嘛**。核对权限。CP6 里 = 确认「admin 有没有 `wms-stock:adjust` 这个权限点」。

顺序永远是**先认证后授权**（5.5.3 讲过中间件顺序）。

### 5.8.2 JWT 结构逐段拆

**JWT（JSON Web Token）** 是一个字符串，由**三段** Base64Url 编码、用 `.` 分隔组成：

```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9  .  eyJzdWIiOiJhZG1pbiIsInRlbmFudF9pZCI6...  .  SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c
└──────────── ① Header ──────────────┘   └──────────── ② Payload ─────────────┘   └──────────────── ③ Signature ─────────────────┘
```

- **① Header（头）**：`{ "alg": "HS256", "typ": "JWT" }`——签名算法和类型。CP6 用 `HmacSha256`（对称密钥签名）。
- **② Payload（载荷）**：一堆 **claims（声明）**——存用户信息。**注意：payload 只是 Base64 编码，不是加密，任何人都能解出来看，所以绝不能放密码等敏感信息**。
- **③ Signature（签名）**：用密钥对「头.载荷」做 HMAC 签名。**服务器用密钥验签，能防篡改**——你改了 payload（比如把 userId 改成别人的），签名就对不上，验证失败。

看 CP6 怎么生成 JWT（`C:\CP6\CP6.Core\Utilities\JwtHelper.cs`）：

```csharp
public static string GenerateToken(string userId, string userName, string secret, string issuer,
    string audience, int expireMinutes, Guid? tenantId = null, string? jti = null,
    bool mustChangePassword = false, bool isPlatformAdmin = false, Guid? impersonatorId = null)
{
    // ① 把用户信息塞进 claims（这些就是 payload 的内容）
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, userId),                        // 用户 ID
        new Claim(ClaimTypes.Name, userName),                               // 用户名
        new Claim("tenant_id", (tenantId ?? TenantContext.DefaultTenant).ToString()),  // 租户 ID（多租户关键！）
        new Claim(JwtRegisteredClaimNames.Jti, jti ?? Guid.NewGuid().ToString()),      // jti：token 唯一标识（登出拉黑用）
        new Claim("must_change_password", mustChangePassword ? "true" : "false")       // 强制改密标志
    };
    // 平台超管/替身 claim 仅在需要时写出（普通用户令牌里没有这些）
    if (isPlatformAdmin) claims.Add(new Claim("is_platform_admin", "true"));
    if (impersonatorId.HasValue) claims.Add(new Claim("impersonator_id", impersonatorId.Value.ToString()));

    // ② 用密钥造签名凭证
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    // ③ 组装 token（issuer 签发者、audience 接收者、expires 过期时间）
    var token = new JwtSecurityToken(issuer: issuer, audience: audience, claims: claims,
        expires: DateTime.Now.AddMinutes(expireMinutes), signingCredentials: credentials);

    // ④ 序列化成 xxx.yyy.zzz 字符串
    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

**CP6 的 JWT 设计亮点**：
- `tenant_id` claim 是多租户的命脉——每个请求靠它确定「这人属于哪个租户」，`TenantMiddleware` 从中读取，数据库查询据此过滤。
- `jti`（JWT ID）让「无状态的 JWT」也能被吊销——登出时把 jti 加进黑名单，5.8.4 讲。
- claims 按需写出（超管标志只有超管才有），解析端永远不会因为「默认值」误判。

### 5.8.3 CP6 的 JWT 认证配置（Program.cs 精读）

Program.cs 第 640~681 行配置 JWT 认证：

```csharp
// 5. 配置 JWT 认证
var jwt = builder.Configuration.GetSection("JWT");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)   // 默认认证方案 = JWT Bearer
    .AddJwtBearer(options =>
    {
        // ① 验签 + 验发行方/受众/有效期 —— 验证 token 的四个维度
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,              // 验签发者
            ValidateAudience = true,            // 验受众
            ValidateLifetime = true,            // 验有效期（过期拒绝）
            ValidateIssuerSigningKey = true,    // 验签名密钥
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!))  // 同一把密钥
        };
        // ② JWT 事件钩子（CP6 安全加固）
        options.Events = new JwtBearerEvents
        {
            // T6：从 httpOnly cookie 里读 token（不只从 Authorization 头）
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token))
                {
                    var c = ctx.Request.Cookies[AuthCookieWriter.AccessCookie];  // cp6_at cookie
                    if (!string.IsNullOrEmpty(c)) ctx.Token = c;
                }
                return Task.CompletedTask;
            },
            // T5：jti 黑名单校验 —— 登出后即使签名/有效期都合法的 token 也被拒
            OnTokenValidated = async ctx =>
            {
                var jti = ctx.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                if (!string.IsNullOrEmpty(jti))
                {
                    var bl = ctx.HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>();
                    if (await bl.IsBlacklistedAsync(jti)) ctx.Fail("token blacklisted");
                }
            }
        };
    });
builder.Services.AddAuthorization();
```

**逐点解析**：
- `AddAuthentication(...).AddJwtBearer(...)`：注册 JWT Bearer 认证。「Bearer」意为「持票人」——谁持有有效 token 谁就是那个身份。
- `TokenValidationParameters` 的四个 `Validate*` 是验证的四道关：签发者、受众、有效期、签名密钥。**生成用的密钥和验证用的密钥是同一把**（对称加密），所以密钥泄露 = 别人能伪造任意 token（这也是为什么 CP6 把密钥当机密管，用环境变量注入）。
- **`OnMessageReceived`（cookie 化的关键）**：默认 JWT 从 `Authorization: Bearer xxx` 头读，但 CP6 为防 XSS 把 token 存进了 httpOnly cookie（JS 读不到），所以这个钩子在头里没 token 时，改从 cookie `cp6_at` 读。
- **`OnTokenValidated`（无状态 JWT 的吊销术）**：JWT 是无状态的，签名有效就放行——那**登出怎么让 token 立刻失效？** CP6 的招：登出时把该 token 的 `jti` 加进黑名单（Redis），这个钩子在验签通过后**再查一次黑名单**，命中就 `ctx.Fail()` 拒绝。用「一次额外的黑名单查询」换来「可主动吊销」，是无状态 JWT 的经典补强。

### 5.8.4 登录发 token 全流程（AuthController 精读）

`AuthController.Login`（`C:\CP6\CP6.WebApi\Controllers\Sys\AuthController.cs`）是「怎么发 token」的完整教材。串一遍登录流程（真实代码，简化标注）：

```csharp
[HttpPost("login")]
[AllowAnonymous]   // 登录端点不需要已登录
public async Task<IActionResult> Login([FromBody] LoginRequest request)
{
    // 1. 跨租户找用户（登录时租户还未知，IgnoreQueryFilters 绕过租户过滤）
    //    带 TenantCode → 缩到该租户；不带 → 按用户名跨租户查，同名多租户要求指定租户消歧
    Sys_User? user = ...;
    if (user == null)
    {
        await _audit.LogAsync(SecurityEventType.LoginFailed, ...);   // 审计
        throw new BizException("E-SEC-001");   // 统一失败码（防用户名枚举：不区分"不存在"和"密码错"）
    }

    // 2. 账户锁定检查（防暴力破解：连续失败会锁定）
    _login.EnsureNotLocked(user);   // 锁定期内即使密码对也拒

    // 3. 验密码（BCrypt 哈希比对，不是明文！）
    if (!_hasher.Verify(request.Password, user.Password))
    {
        await _login.RecordFailureAsync(user);   // 记一次失败（累积到阈值就锁定）
        throw new BizException("E-SEC-001");
    }

    // （中间还有：账号禁用检查、租户停用检查、强制 SSO 检查、2FA 双因素检查……）

    // 4. 确定当前请求租户 = 该用户的租户
    _tenant.CurrentTenantId = user.TenantId;

    // 5. 生成 access JWT（带 jti 用于登出吊销 + must_change_password 标志）
    var jti = Guid.NewGuid().ToString();
    var mustChange = user.MustChangePassword || _policy.IsExpired(user);
    var token = BuildAccessToken(user, jti, mustChange, isPlatformAdmin: user.IsPlatformAdmin);

    // 6. 预热权限上下文 + 按全部角色聚合菜单（多角色 RBAC 并集）
    var profile = await BuildProfileAsync(user, mustChange);

    // 7. 记录登录成功 + 审计
    await _login.RecordSuccessAsync(user, ClientIp);
    await _audit.LogAsync(SecurityEventType.LoginSuccess, ...);

    // 8. 签发 refresh 令牌 + CSRF 令牌，三者写 httpOnly/双提交 Cookie
    //    ★ access JWT 不再放进响应 body（防前端 localStorage 存 token 被 XSS 偷）★
    var rawRt = await _refresh.IssueAsync(user, ClientIp, ClientUa);
    var csrf = AuthCookieWriter.NewCsrfToken();
    _cookies.WriteAuthCookies(Response, token, rawRt, csrf);

    // 9. 返回用户信息和菜单（不含 token）
    return Ok(profile);
}
```

**这个登录流程堆满了安全设计（每一条都是面试加分点）**：
1. **防用户名枚举**：无论「用户不存在」还是「密码错误」，都返回同一个错误码 `E-SEC-001`——不让攻击者通过错误信息推断哪些用户名存在。
2. **账户锁定防暴破**：连续失败 N 次锁定账户，锁定期内密码对也拒。
3. **BCrypt 密码哈希**：密码库里存的是 BCrypt 哈希（加盐、慢哈希），不是明文，`Verify` 做哈希比对。
4. **jti 支持登出吊销**（配合 5.8.3 的黑名单钩子）。
5. **Cookie 化防 XSS**：token 存 httpOnly cookie 而不是返回给 JS，前端存不了 token，XSS 偷不走。代价是引入 CSRF 面，所以配套双提交 CSRF（同时下发 csrf cookie）——这就是 5.5.5 CSRF 中间件存在的原因。整条链闭环了。
6. **Refresh Token 轮换**：access token 短命（几分钟），refresh token 长命且每次刷新轮换（配 `Refresh` 端点），旧的立即失效并检测重用。
7. **全程审计**：成功、失败、锁定都写安全审计日志。

### 5.8.5 Claims 与 ClaimsPrincipal

认证成功后，框架把 JWT 的 claims 解析成 **`ClaimsPrincipal`**，挂在 `HttpContext.User` 上。之后任何地方都能读：

```csharp
// OperLogFilter 里读用户名：
var userName = context.HttpContext.User.FindFirst(ClaimTypes.Name)?.Value;

// StockController 里：
private string? CurrentUser => User?.Identity?.Name;

// DbStringLocalizer 里读租户：
var raw = _http.HttpContext?.User?.FindFirst("tenant")?.Value;
```

- **`ClaimsPrincipal`（User）= 当前登录者的身份画像**，由一个或多个 `ClaimsIdentity` 组成，每个 identity 有一堆 `Claim`（键值对）。
- `User.Identity.Name`、`User.FindFirst("tenant_id")`、`User.FindFirst("jti")` 都是从这里取。CP6 全靠这套读用户、租户、jti、超管标志。

### 5.8.6 自定义授权：RequirePermission 特性（精读）

CP6 不用 ASP.NET Core 内置的「角色/策略」授权，而是自己实现了一套**细粒度功能权限**——每个操作对应一个 `menuKey:action` 权限点（如 `wms-stock:adjust`），贴在方法上校验。

看 `RequirePermissionAttribute`（`C:\CP6\CP6.Core\Auth\RequirePermissionAttribute.cs`）——它是一个**授权过滤器**（实现 `IAsyncAuthorizationFilter`，5.7 讲过授权过滤器最先跑）：

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _menu;
    private readonly string _action;
    public RequirePermissionAttribute(string menu, string action)   // [RequirePermission("wms-stock","adjust")]
    {
        _menu = menu;
        _action = action;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // 特性不能构造注入，用 RequestServices 服务定位取权限服务
        var svc = context.HttpContext.RequestServices.GetService<IPermissionService>();
        if (svc == null)   // 服务没注册 → 500（fail-safe：宁可报错也不放行）
        {
            context.Result = new ObjectResult(new { code = 500, message = "权限服务未注册" })
            { StatusCode = StatusCodes.Status500InternalServerError };
            return;
        }
        // 核心：查当前用户有没有 "menu:action" 这个权限点，没有就 403
        if (!await svc.HasActionAsync(_menu, _action))
        {
            context.Result = new ObjectResult(new { code = 403, message = $"无权限：{_menu}:{_action}" })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
```

**逐点解析**：
- **特性为什么用服务定位（`RequestServices.GetService`）而不是构造注入？** 因为 .NET 的 attribute 是编译期元数据，实例化不走 DI 容器，构造函数没法注入服务。所以在 `OnAuthorizationAsync` 运行时从 `HttpContext.RequestServices` 里「服务定位」拿 `IPermissionService`。（这是「特性做过滤器」的固定套路。）
- **`[Authorize]` 和 `[RequirePermission]` 配合**：`[Authorize]` 先验「登录了没」（认证），`[RequirePermission]` 再验「有没有这个操作权」（授权）。看 `StockController.Apply`：类头 `[Authorize]` + 方法 `[RequirePermission("wms-stock", "adjust")]`——先要登录，再要有 `wms-stock:adjust` 权限。
- **权限服务本身零查库**：`PermissionService.HasActionAsync`（`C:\CP6\CP6.Core\Services\Sys\PermissionService.cs`）超级简单——

  ```csharp
  public async Task<bool> HasActionAsync(string menu, string action) =>
      (await _cur.GetAsync()).ActionKeys.Contains($"{menu}:{action}");
  ```

  它读的是**请求级缓存的权限上下文**（`ICurrentPermissionContext`），登录时已经把该用户所有角色的权限点聚合好缓存了，这里只是 `HashSet.Contains` 一次内存判断，不查库，极快。

### 5.8.7 CP6 三层防线

CP6 的授权是**三层纵深防御**（面试讲这个体现体系化思维）：

```
第①层：JWT 认证（你是谁）
   └ Program.cs AddJwtBearer + OnTokenValidated 黑名单
   └ 挡住：未登录、token 伪造、token 过期、已登出

第②层：细粒度功能权限（你能干这个操作吗）
   └ [RequirePermission("menu","action")] 授权过滤器
   └ 挡住：登录了但没有该操作权限 → 403

第③层：fail-closed 反射测试（防开发者忘贴权限）
   └ 单元测试反射扫描所有写端点，没贴 [RequirePermission] 就测试失败
   └ 挡住：新加的接口忘了加权限校验（默认关闭 = 安全）
```

第③层特别值得说。CP6 有一个 `RequirePermissionFilterTests`（`C:\CP6\CP6.Tests\RequirePermissionFilterTests.cs`）验证过滤器行为：

```csharp
[Fact]
public async Task NoPermission_Sets403()   // 没权限 → 403
{
    var ctx = MakeContext(hasAction: false);
    await new RequirePermissionAttribute("order", "export").OnAuthorizationAsync(ctx);
    var result = Assert.IsType<ObjectResult>(ctx.Result);
    Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
}

[Fact]
public async Task HasPermission_DoesNotSetResult()   // 有权限 → 放行（不设 Result）
{
    var ctx = MakeContext(hasAction: true);
    await new RequirePermissionAttribute("order", "export").OnAuthorizationAsync(ctx);
    Assert.Null(ctx.Result);
}

[Fact]
public async Task ServiceMissing_Sets500()   // 服务缺失 → 500（fail-safe 不放行）
{
    var ctx = MakeContext(hasAction: true, registerSvc: false);
    await new RequirePermissionAttribute("order", "export").OnAuthorizationAsync(ctx);
    var result = Assert.IsType<ObjectResult>(ctx.Result);
    Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
}
```

「**fail-closed / fail-safe**」是安全设计的黄金原则：**出问题时默认拒绝，而不是默认放行**。权限服务没注册？返回 500 拒绝，而不是「算了放行吧」。这样任何配置疏漏都不会变成安全漏洞。

> **面试问答**
> **Q：JWT 是什么，三段分别是什么？**
> A：JSON Web Token，一个用点分隔的三段字符串。第一段 Header 说签名算法；第二段 Payload 装 claims（用户信息），只是 Base64 编码不是加密，所以不能放敏感信息；第三段 Signature 是用密钥对前两段做的签名。服务器用密钥验签，能防篡改——改了 payload 签名就对不上。它的好处是无状态，服务器不用存 session。
>
> **Q：JWT 是无状态的，那用户登出后怎么让 token 立刻失效？**
> A：JWT 本身签名有效就会被接受，没法直接作废。我们的做法是给每个 token 一个唯一的 jti claim，登出时把 jti 加进一个黑名单（用 Redis，TTL 设成 token 剩余寿命，到期自动清）。认证时在验签通过后额外查一次黑名单，命中就拒绝。用一次黑名单查询换来主动吊销能力。
>
> **Q：认证和授权你们怎么分层做的？**
> A：三层。第一层 JWT 认证解决「你是谁」，包括验签、验期、黑名单吊销。第二层细粒度功能权限，每个操作对应一个 `menuKey:action` 权限点，用自定义的 `[RequirePermission]` 授权过滤器校验，权限点在登录时按用户所有角色聚合缓存，校验时只做内存 HashSet 判断不查库。第三层是 fail-closed 的反射测试，自动扫描所有写接口有没有贴权限特性，忘贴就测试失败，防止开发者疏漏。而且权限服务缺失时返回 500 拒绝而不是放行，是 fail-safe 设计。

---

## 5.9 SignalR

### 5.9.1 概念：为什么需要 SignalR

普通 HTTP 是「客户端问、服务器答」，服务器**没法主动推**消息给客户端。但很多场景需要服务器主动推：MES 车间大屏要实时显示设备状态、库存异动要实时刷新、审批通知要立刻弹出。

**SignalR 是 .NET 的实时通信框架**，让服务器能主动向客户端推送消息。它自动选择最佳传输方式，**优雅降级**：

```
SignalR 传输方式（自动协商，从优到劣回退）：
  ① WebSocket        ← 首选，全双工长连接，最高效
        │ 不支持则降级
  ② Server-Sent Events (SSE)  ← 服务器→客户端单向推
        │ 不支持则降级
  ③ Long Polling     ← 兜底，客户端反复轮询"有新消息吗"
```

客户端建连时先发一个 `POST /hubs/notify/negotiate` 协商用哪种传输——**这就是 5.5.5 里 CsrfMiddleware 必须豁免 `/hubs` 的原因**（这个 POST 会被 CSRF 拦）。

### 5.9.2 Hub 概念

**Hub 是 SignalR 的服务端「消息中心」**。客户端连到 Hub，Hub 能调用客户端的方法（推消息）、客户端也能调 Hub 的方法。看 CP6 的 `NotifyHub`（`C:\CP6\CP6.WebApi\Hubs\NotifyHub.cs`）：

```csharp
public class NotifyHub : Hub
{
    private readonly ILogger<NotifyHub> _logger;
    public NotifyHub(ILogger<NotifyHub> logger) => _logger = logger;

    public override Task OnConnectedAsync()   // 客户端连上时触发
    {
        _logger.LogInformation("客户端连接: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }
    public override Task OnDisconnectedAsync(Exception? exception)  // 断开时触发
    {
        _logger.LogInformation("客户端断开: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
```

- 继承 `Hub`，`Context.ConnectionId` 是每个连接的唯一标识。
- `OnConnectedAsync` / `OnDisconnectedAsync` 是连接生命周期钩子。
- 注册（Program.cs）：`builder.Services.AddSignalR()` + 路由 `app.MapHub<NotifyHub>("/hubs/notify")`。CP6 有四个 Hub：notify（通用通知）、mes（车间）、wms（仓库）、space（空间）。

推消息用 `Clients.All.SendAsync("方法名", 数据)`——广播给所有连接的客户端。

### 5.9.3 CP6 全链路：业务事件 → RabbitMQ → 消费者 → SignalR fanout

CP6 的实时通知不是「业务代码直接调 Hub」，而是走了一条**解耦的消息链路**（这个架构很值得讲）：

```
  业务发生事件（如：出货完成 / 库存差异）
        │
        ▼  发布业务通知
  ┌──────────────────────┐
  │ RabbitMQService       │  实现 INotificationPublisher
  │ 发到 cp6.notification  │  队列（低频、确实送达、可重试）
  └──────────────────────┘
        │
        ▼  消费
  ┌──────────────────────┐
  │ NotificationConsumer  │  BackgroundService，订阅队列
  │  (后台服务)            │
  └──────────────────────┘
        │
        ▼  IHubContext<NotifyHub>.Clients.All.SendAsync(...)
  ┌──────────────────────┐
  │ NotifyHub (SignalR)   │  fanout 广播
  └──────────────────────┘
        │
        ▼  推送
  所有连接的浏览器 → 前端弹通知/刷新
```

看 `NotificationConsumer`（`C:\CP6\CP6.WebApi\BackgroundServices\NotificationConsumer.cs`）的核心——它订阅 RabbitMQ 队列，收到消息就通过 SignalR 广播：

```csharp
public class NotificationConsumer : BackgroundService
{
    private readonly IHubContext<NotifyHub> _hubContext;   // ← 后台服务里用 IHubContext 推消息（不是 Hub 本身）
    // ……
    consumer.ReceivedAsync += async (sender, ea) =>
    {
        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
        var notice = JsonSerializer.Deserialize<BusinessNotification>(json);
        if (notice != null)
        {
            // 全客户端 fanout 业务通知
            await _hubContext.Clients.All.SendAsync("BusinessNotification", new
            {
                notice.EventType, notice.Level, notice.Title,
                notice.Message, notice.Source, notice.RefNo, notice.CreateDate
            });
        }
        await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);   // 处理成功才 ack
    };
}
```

**为什么绕这么一圈（业务→MQ→消费者→SignalR）而不直接推？**（面试亮点）
1. **解耦**：业务代码只管「发一个通知事件」，不关心谁在听、怎么推。
2. **可靠**：RabbitMQ 保证消息不丢（持久化队列 + ack 机制），处理失败能 Nack 重试。
3. **可扩展**：将来要加「邮件通知」「Webhook 通知」，只在消费者里加分支，业务代码不动（代码注释里写了这个前瞻）。
4. **在后台服务里推消息要用 `IHubContext<T>`**，而不是 `Hub` 实例——因为 Hub 实例是「每个方法调用」临时创建的，后台服务拿不到；`IHubContext` 是单例、可注入，专门用于「从 Hub 外部推消息」。**这是 SignalR 的一个重要面试点。**

> **面试问答**
> **Q：SignalR 是什么，底层怎么工作？**
> A：.NET 的实时通信框架，让服务器能主动推消息给客户端。底层自动协商传输方式，优先 WebSocket（全双工），不支持就降级到 Server-Sent Events，再不行用长轮询兜底。服务端的核心是 Hub，客户端连到 Hub，服务端用 `Clients.All.SendAsync` 广播。我们 MES 大屏、库存刷新、审批通知都用它。
>
> **Q：在一个后台服务或普通 service 里怎么给客户端推 SignalR 消息？**
> A：注入 `IHubContext<THub>`，用 `_hubContext.Clients.All.SendAsync(...)`。不能用 Hub 实例本身，因为 Hub 实例是每次客户端调用时临时创建的，外部拿不到；`IHubContext` 是单例、随处可注入，专门用于从 Hub 外部推送。我们的通知链路就是 RabbitMQ 消费者（后台服务）注入 `IHubContext<NotifyHub>` 来广播的。

---

## 5.10 后台服务

### 5.10.1 概念：IHostedService / BackgroundService

**后台服务（hosted service）** 是随应用一起启动、在后台持续运行的任务（不响应 HTTP 请求）。两个基础：
- **`IHostedService`**：接口，有 `StartAsync` / `StopAsync` 两个方法（应用启动/停止时调）。
- **`BackgroundService`**：抽象基类，实现了 `IHostedService`，只要你重写一个 `ExecuteAsync(CancellationToken)` 方法写「循环干活」的逻辑即可。**大多数后台服务继承它。**

注册：`builder.Services.AddHostedService<T>()`。它们的生命周期是**单例级**（应用启动创建一次，跑到关闭）——所以 5.3.6 说过，要用 Scoped 的 DbContext 必须手动开 scope。

### 5.10.2 CP6 三个真实后台服务

CP6 有十几个后台服务，挑三个代表讲不同模式：

**① KafkaOperLogConsumer —— 消息消费者模式**（`C:\CP6\CP6.WebApi\BackgroundServices\KafkaOperLogConsumer.cs`）

持续从 Kafka 拉操作日志落库。它演示了「阻塞式拉取放到独立长线程」+「手动开 scope 取 DbContext」：

```csharp
public class KafkaOperLogConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;   // 手动开 scope 用
    // ……
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Kafka 的 Consume 是阻塞调用 → 放到独立的长线程跑，避免占用线程池
        return Task.Factory.StartNew(
            () => ConsumeLoop(bootstrap, topic, groupId, stoppingToken),
            stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    private async Task ConsumeLoop(...)
    {
        // EnableAutoCommit=false + 处理成功才手动 Commit → 至少一次语义（不丢消息）
        while (!stoppingToken.IsCancellationRequested)
        {
            var cr = consumer.Consume(stoppingToken);   // 阻塞拉取
            try
            {
                await PersistAsync(cr.Message.Value);   // 落库
                consumer.Commit(cr);                     // 成功才提交位移
            }
            catch { /* 不 Commit → 下次重试 */ }
        }
    }
    private async Task PersistAsync(string json)
    {
        using var scope = _scopeFactory.CreateScope();                    // 开 scope
        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();  // 取 Scoped DbContext
        db.Sys_OperLogs.Add(log);
        await db.SaveChangesAsync();
    }
}
```
要点：① Kafka 阻塞拉取用 `TaskCreationOptions.LongRunning` 独立线程，不占线程池；② `EnableAutoCommit=false` + 处理成功才 `Commit` = 至少一次投递语义；③ 后台服务手动开 scope 取 DbContext。

**② OperLogCleanupService —— 定时清理模式**（`C:\CP6\CP6.WebApi\BackgroundServices\OperLogCleanupService.cs`）

周期性删过期日志，演示「定时循环 + 可配置周期」：

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var retentionDays = section.GetValue<int?>("RetentionDays") ?? 7;    // 保留天数可配
    var intervalHours = section.GetValue<int?>("CleanupIntervalHours") ?? 24;  // 周期可配
    if (retentionDays <= 0) return;   // <=0 表示永久保留，服务不跑

    try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }   // 启动后稍等，避开初始化高峰
    catch (OperationCanceledException) { return; }

    var interval = TimeSpan.FromHours(intervalHours);
    while (!stoppingToken.IsCancellationRequested)
    {
        await CleanupOnceAsync(retentionDays, stoppingToken);   // 干一轮活
        try { await Task.Delay(interval, stoppingToken); }       // 睡到下一轮
        catch (OperationCanceledException) { break; }            // 停机信号 → 优雅退出
    }
}
```
用 EF Core 8 的 `ExecuteDeleteAsync` 批量删（不把整表加载进内存），配 `IgnoreQueryFilters()` 跨租户删（因为清理是跨租户的运维操作）。

**③ NotificationConsumer —— 就是 5.9.3 那个**（RabbitMQ 消费 → SignalR 推送）。演示后台服务 + SignalR `IHubContext` 结合。

### 5.10.3 优雅停机（Graceful Shutdown）

**`ExecuteAsync` 收到的 `CancellationToken stoppingToken` 是「停机信号」**。应用关闭时框架会触发它，后台服务应该**及时响应、干净收尾**：

```csharp
while (!stoppingToken.IsCancellationRequested)   // 每轮循环检查停机信号
{
    ...干活...
    try { await Task.Delay(interval, stoppingToken); }
    catch (OperationCanceledException) { break; }  // Delay 被取消 → 立即跳出，不硬等
}
```

看 CP6 的模式：所有 `await Task.Delay(..., stoppingToken)` 都传了 token，这样停机时正在 `Delay` 的服务会立刻抛 `OperationCanceledException` 被 catch 到、优雅退出，而不是硬等到 24 小时后。`NotificationConsumer` 还重写了 `StopAsync` 关闭 RabbitMQ 连接：

```csharp
public override async Task StopAsync(CancellationToken cancellationToken)
{
    if (_channel != null) await _channel.CloseAsync(cancellationToken);      // 关 channel
    if (_connection != null) await _connection.CloseAsync(cancellationToken); // 关连接
    await base.StopAsync(cancellationToken);
}
```

**优雅停机的意义**：容器重启/部署时，正在处理的消息要处理完、连接要正常关闭、不留脏状态。生产系统必须做好。

> **面试问答**
> **Q：`IHostedService` 和 `BackgroundService` 什么关系？后台任务里怎么用 DbContext？**
> A：`IHostedService` 是接口（`StartAsync`/`StopAsync`），`BackgroundService` 是它的抽象基类，只需重写 `ExecuteAsync` 写循环逻辑，更常用。后台服务是单例级别的，不能直接注入 Scoped 的 DbContext，否则会 captive dependency。正确做法是注入 `IServiceScopeFactory`，每次干活时 `CreateScope()` 开一个作用域取 DbContext，用完 `using` 释放。我们的 Kafka 消费者、日志清理服务都是这个模式。
>
> **Q：后台服务怎么优雅停机？**
> A：`ExecuteAsync` 的参数 `CancellationToken stoppingToken` 是停机信号，应用关闭时被触发。循环里每轮检查 `IsCancellationRequested`，所有 `Task.Delay` 都把 token 传进去，这样停机时正在睡眠的服务能立刻响应退出而不是硬等。有外部资源（消息队列连接）的还要重写 `StopAsync` 关闭连接。目的是部署/重启时不丢正在处理的消息、不留脏连接。

---

## 5.11 Swagger / OpenAPI

### 5.11.1 概念

**OpenAPI** 是描述 REST API 的标准规范（一个 JSON 文档，列出所有端点、参数、返回结构）。**Swagger** 是围绕 OpenAPI 的工具集，最常用的是 **Swagger UI**——一个自动生成的、能在浏览器里直接测试 API 的交互页面。

作用：① 前后端联调时的「活文档」（后端不用手写接口文档）；② 可以直接在页面上填参数、点「Try it out」发请求测试。

### 5.11.2 CP6 的 Swagger 配置与 CustomSchemaIds 冲突解决

Program.cs 第 51~57 行：

```csharp
// 2. 注册 Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // 完全限定名で schemaId を一意化（入れ子型 DeleteRequest 等の衝突回避）
    c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("+", "."));
});
```

启用（只在开发环境，第 706~710 行）：

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();      // 暴露 /swagger/v1/swagger.json（OpenAPI 文档）
    app.UseSwaggerUI();    // 暴露 /swagger 交互页面
}
```

**`CustomSchemaIds` 解决的真实冲突**：Swagger 默认用**类的短名**（不含命名空间）作为 schema ID。CP6 里很多控制器都有自己的嵌套类型叫 `DeleteRequest`（`WmsController.DeleteRequest`、`OrderController.DeleteRequest`……）——**短名全叫 `DeleteRequest`，撞车了**，Swagger 生成文档时会抛「schemaId 冲突」错误，`/swagger` 打不开。

**解法**：`c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("+", "."))`——用**完全限定名**（含命名空间和外层类名）作为 schema ID，保证唯一。`.Replace("+", ".")` 是把嵌套类型的 `+` 号（`OrderController+DeleteRequest`）换成 `.`（Swagger schema ID 不喜欢 `+`）。

> **面试问答**
> **Q：Swagger/OpenAPI 是干什么的，遇到过什么坑？**
> A：OpenAPI 是描述 REST API 的标准，Swagger UI 能据此生成交互式 API 文档和测试页面，前后端联调很方便。我们只在开发环境启用。踩过一个 schemaId 冲突的坑：Swagger 默认用类短名做 schema ID，我们多个控制器都有嵌套的 `DeleteRequest` 类型，短名撞车导致 Swagger 页面打不开。解法是配 `CustomSchemaIds` 用完全限定名做 ID 来消除冲突。

---

## 5.12 全局异常处理与统一响应

### 5.12.1 概念：为什么要统一响应

如果每个接口各返回各的格式，前端就得为每个接口写不同的解析逻辑，出错处理也乱。**统一响应约定** = 所有接口都返回同一个信封结构，前端一套逻辑通吃。

CP6 的信封是 **`{ code, message, data }`**：
- `code`：业务状态码（`0` 或省略表示成功，非 0 是各种错误码）。
- `message`：提示信息（成功是 "OK"，失败是**多语言消息键**）。
- `data`：真正的业务数据（失败时为 null）。

在 `StockController` 里到处可见：`Ok(new { code = 0, message = "OK", data = new {...} })`。

### 5.12.2 BizException + 中间件：全局异常翻译

CP6 的错误处理很优雅：**业务层抛一个只带「错误码」的异常，中间件统一捕获并翻译成当前语言的响应**。

先看 `BizException`（`C:\CP6\CP6.Core\Localization\BizException.cs`）——它**只携带 i18n 错误码，绝不携带具体语言文字**：

```csharp
public class BizException : Exception
{
    public string Code { get; }        // i18n 词条 key，如 "E-SEC-010"
    public object[] Args { get; }      // 消息模板的格式化参数
    public int HttpStatus { get; }     // HTTP 状态码，默认 400

    public BizException(string code, params object[] args) : base(code)
    {
        Code = code; Args = args ?? Array.Empty<object>(); HttpStatus = 400;
    }
    public BizException(string code, int httpStatus, params object[] args) : base(code)
    {
        Code = code; Args = args ?? Array.Empty<object>(); HttpStatus = httpStatus;
    }
}
```

用法（类注释里写得很清楚）：业务层 `throw new BizException("E-SEC-010", 403)` 而不是 `throw new Exception("CSRF校验失败")`。**好处：后端文案随语言走、随数据库改、永不需要为改文案重新发版。**

再看捕获它的 `BizExceptionMiddleware`（`C:\CP6\CP6.WebApi\Middleware\BizExceptionMiddleware.cs`）：

```csharp
public class BizExceptionMiddleware
{
    private readonly RequestDelegate _next;
    public BizExceptionMiddleware(RequestDelegate next) => _next = next;

    // 注意 IStringLocalizer 注入到方法参数（每请求解析），不是构造函数（见 5.5.4 的坑）
    public async Task InvokeAsync(HttpContext context, IStringLocalizer localizer)
    {
        try
        {
            await _next(context);   // 放行，等下游可能抛 BizException
        }
        catch (BizException ex)
        {
            // 用当前请求 culture 把错误码翻译成本地化消息
            var localized = ex.Args.Length > 0 ? localizer[ex.Code, ex.Args] : localizer[ex.Code];
            context.Response.StatusCode = ex.HttpStatus;
            context.Response.ContentType = "application/json; charset=utf-8";
            var payload = JsonSerializer.Serialize(new
            {
                code = ex.HttpStatus,
                message = localized.Value,   // 已是当前语言译文；未命中则回退码本身
                data = (object?)null
            });
            await context.Response.WriteAsync(payload);   // 输出统一信封
        }
    }
}
```

**整条链路**：
```
业务/中间件 throw new BizException("E-SEC-010", 403)
        │ 异常上抛
        ▼
BizExceptionMiddleware catch 到
        │
        ▼ localizer["E-SEC-010"] 按当前 culture 翻译
        │   （中文→"CSRF校验失败" / 日文→"CSRF検証に失敗" / 英文→"CSRF validation failed"）
        ▼
输出 { code: 403, message: "翻译后的文案", data: null }
```

### 5.12.3 多语言消息键设计（WM-MSG-xxx / E-SEC-xxx）

CP6 的消息 **全部用「键」而非硬编码文案**。你在代码里见到的：
- `WM-MSG-070`、`WM-MSG-071`（WMS 模块消息，见 `StockController`）
- `E-SEC-001`、`E-SEC-010`（安全模块错误码，见 `AuthController`、`CsrfMiddleware`）
- `E-WF-025`、`E-PUR-080`（工作流、采购模块错误码）

这些键存在数据库 `Sys_Lang` 表里，每个键有 5 种语言的译文（`Ja`/`ZhCN`/`ZhTW`/`En`/`Ko`）。翻译由 5.3.5 讲的 `DbStringLocalizer` 完成，它按当前请求语言 + 租户覆盖 + 回退链解析。

**键的命名规律**（面试可以体现你懂设计）：`模块前缀-类型-编号`。`WM`=WMS 仓库，`E-SEC`=Security 错误，`E-WF`=Workflow 错误，`E-PUR`=Purchase 错误。这种前缀分段让几千个消息键有序可管。

**这套设计的价值**（面试金句）：
1. **国际化**：CP6 是给日本制造业用的，支持日/中/繁/英/韩五语。消息键 + DB 译文让同一份代码适配所有语言。
2. **文案与代码解耦**：改文案只需改数据库 `Sys_Lang` 表（甚至可以让运营改），**不用改代码、不用重新发版**。
3. **租户覆盖**：`DbStringLocalizer` 支持「租户级覆盖译文」——A 租户可以把某个提示改成自己的说法，不影响别的租户。

> **面试问答**
> **Q：你们的 API 响应和错误处理是怎么统一的？**
> A：所有接口返回统一信封 `{ code, message, data }`，前端一套逻辑解析。错误处理上，业务层抛一个只带错误码的 `BizException`（比如 `throw new BizException("E-SEC-010", 403)`），一个全局异常中间件统一捕获它，用当前请求语言把错误码翻译成本地化消息，再输出成统一信封。错误码和文案都存在数据库多语言表里，支持日中繁英韩五语和租户级覆盖。这样改文案只改数据库不用发版，也天然支持国际化。
>
> **Q：为什么异常里只放错误码不放文案？**
> A：因为我们要支持多语言。如果业务代码里 `throw new Exception("必填项缺失")` 写死了中文，就没法按用户语言返回了，改文案还得改代码重新发版。只带错误码 `E10022`，由异常中间件在请求的语言上下文里翻译，文案存数据库，能随语言走、随数据库改、支持租户覆盖，代码零改动。

---

## 本章面试题 20 问（详细答案）

**1. Kestrel 是什么？为什么生产环境要在它前面加反向代理？**
Kestrel 是 ASP.NET Core 内置的跨平台高性能 Web 服务器，真正接收和处理 HTTP 请求的就是它。生产建议前置反向代理（Nginx/IIS）做 TLS 终止、限流、安全隔离和负载均衡（一个代理挂多个 Kestrel 实例横向扩展）。CP6 把 Kestrel 打进 Docker 容器跑，前面用反向代理。

**2. 401 和 403 的区别？**
401 Unauthorized = 认证失败，「我不知道你是谁」，该去登录（token 无效/缺失/过期）。403 Forbidden = 授权失败，「我知道你是谁但你不能干这个」（登录了但没权限，或 CSRF 校验失败）。CP6 里 JWT 无效→401，没有某操作权限→403，CSRF 不匹配→403。

**3. Program.cs 的两段式结构是什么？`builder.Build()` 前后有何区别？**
前段往 DI 容器注册服务（`builder.Services.AddXxx`），只是登记有哪些零件。`Build()` 一调容器封箱冻结不能再注册，返回的 `app` 用来组装中间件管道和路由（`app.UseXxx`/`app.MapXxx`）。前段决定「有哪些服务」，后段决定「请求走哪条流水线」。

**4. 什么是依赖注入？好莱坞原则是什么？**
DI 是控制反转的实现：不自己 new 依赖，由外部容器把依赖造好注入进来（通常通过构造函数）。好莱坞原则「Don't call us, we'll call you」——你留下构造函数参数这份「简历」声明依赖，需要时框架主动造好喂给你，控制权从「主动找依赖」反转成「被动被喂依赖」。好处：解耦、可替换、可测试。

**5. 三种服务生命周期的区别、适用场景、误用后果？**
Singleton 全应用一个实例（适合无状态工具/缓存/连接工厂；误用后果=线程安全问题、囚禁 Scoped 依赖）。Scoped 每请求一个（适合 DbContext 和业务服务；误用后果=做成单例会线程崩溃+状态跨请求污染）。Transient 每次注入都新建（适合轻量无状态小对象；误用后果=注入进单例会变相延寿失去瞬态语义）。核心规则：注入的依赖寿命不能比自己短。

**6. 为什么 EF Core 的 DbContext 必须注册为 Scoped？**
两个原因：① DbContext 不是线程安全的，做成 Singleton 时多请求并发用同一个会崩；② 它有变更追踪状态，一个请求一套天然形成一个工作单元/事务边界，请求结束追踪清空。所以 `AddDbContext` 默认就是 Scoped。

**7. 什么是 captive dependency（被囚禁依赖）？怎么解决？**
Singleton 直接注入一个 Scoped 服务，导致这个 Scoped 实例被单例攥住活一整个应用生命周期，本该每请求一个变成全局一个，产生跨请求污染和线程安全问题。解决：单例注入 `IServiceScopeFactory`，用到时 `CreateScope()` 手动开作用域取 Scoped 服务，`using` 用完即释放。CP6 的 `DbStringLocalizer`（单例，要查库）和所有后台服务都用这个模式。.NET 开发环境启动时的 scope 校验会直接报出这类错误。

**8. 一个接口有多个实现，DI 里怎么处理？**
两种方式。① 同接口注册多个实现，消费方注入 `IEnumerable<接口>` 拿到全部列表自己遍历/挑选（CP6 工作流的 `INodeHandler` 就注册了十几个实现）。② .NET 8 的 keyed services，用键区分，注入时 `[FromKeyedServices("key")]` 精确取某一个。前者「全都要」，后者「按 key 要一个」。

**9. 什么时候用工厂委托 `sp => ...` 注册？**
三种：① 让接口和具体类解析到同一实例（`sp => sp.GetRequiredService<具体类>()`，避免两个实例）；② 类有多个构造函数容器选不出来，工厂显式 new 指定；③ 构造时要读配置计算参数。CP6 的 Kafka 生产者、BCrypt 哈希器、附件服务都用了。

**10. 配置系统的源优先级是怎样的？环境变量的 `__` 约定是什么？**
配置由多个源叠加，后面的盖前面的。默认顺序（低→高）：appsettings.json → appsettings.{Env}.json → User Secrets → 环境变量 → 命令行。环境变量名不能有冒号，用双下划线 `__` 代替层级分隔（`ConnectionStrings:DefaultConnection` → `ConnectionStrings__DefaultConnection`）。Docker 部署就靠环境变量覆盖 JSON 里的默认值。

**11. 讲一个配置相关的坑（appsettings.Local.json 事故）。**
加了一个本地开发用的 `appsettings.Local.json`，用 `AddJsonFile` 追加，本地正常但 Docker 部署连不上数据库——因为 `AddJsonFile` 把源加到链尾即最高优先级，盖掉了容器注入的环境变量连接串。根治是手动把这个 JSON 源插到「环境变量源」之前，恢复「环境变量最高」的标准优先级。教训：配置源有优先级、后加的默认更高、容器化严重依赖环境变量。

**12. IOptions&lt;T&gt; 模式是什么？IOptions/IOptionsSnapshot/IOptionsMonitor 的区别？**
把一段配置绑定到强类型类，消费方注入 `IOptions<T>` 用 `.Value` 拿到，有类型检查不会拼错键。`IOptions<T>` 单例、启动读一次不变；`IOptionsSnapshot<T>` Scoped、每请求读一次支持热更新；`IOptionsMonitor<T>` 单例但能监听变更随时拿最新值+回调。CP6 用 `Configure<SecurityOptions>` 绑定 Security 段，`CsrfMiddleware`/`AuthController` 都注入 `IOptions<SecurityOptions>`。

**13. 中间件的洋葱模型是什么？Use/Run/Map 区别？**
中间件串成管道，请求从外层穿到里层、响应从里层穿回外层，同一中间件代码去程回程各执行一次（`await next` 前是去程，后是回程）。Use 加一个会调用下一层的中间件；Run 加终结点中间件不调 next（短路）；Map 按路径分支到子管道（`MapControllers`/`MapHub` 是终结点路由）。

**14. 为什么 UseAuthentication 必须在 UseAuthorization 之前？还有哪些顺序约束？**
认证解析身份填充 `HttpContext.User`，授权依赖 User 判断权限，顺序反了授权时 User 是空的全乱。中间件注册顺序=请求经过顺序。CP6 其他约束：租户解析在认证后（租户 ID 在 JWT claim）、请求本地化在认证后（语言偏好来自 claim）、全局异常处理在本地化后（异常要按语言翻译）且在业务下游、CSRF 在授权前。原则：依赖别人产出的排在后面。

**15. 自定义中间件有几种写法？构造函数注入有什么坑？**
两种：约定式类（构造函数+Invoke 方法，`UseMiddleware<T>` 注册，可复用可测试）和内联委托（`app.Use(async (ctx,next)=>...)`）。坑：约定式中间件的构造函数是启动时调一次（单例级），只能注入 Singleton 依赖；要用 Scoped 依赖（如 DbContext、IStringLocalizer）必须注入到 `Invoke` 方法的参数上（每请求解析）。CP6 的 `BizExceptionMiddleware` 就把 `IStringLocalizer` 注入到方法参数。

**16. 什么是 CSRF？CP6 怎么防？豁免设计要注意什么（讲 fire 端点事故）？**
CSRF 是跨站请求伪造，利用浏览器自动带 Cookie 的特性从恶意站点伪造你的身份发写请求。CP6 因为把 token 存进 httpOnly cookie 而有 CSRF 面，用双提交 Cookie 防护：登录下发非 httpOnly 的 csrf cookie，前端每次写请求读出来塞进 `X-CSRF-Token` 头，服务端校验 cookie==header，攻击者跨站读不到 cookie 也设不了自定义头就伪造不出来。豁免的坑「豁免即攻击面」：我们踩过一个外部回调 fire 端点没豁免，生产开 CSRF 后被 403 拦死功能全失效；修复时不能简单豁免整个前缀（同级管理端点是 cookie 认证要保护），做成「前缀+/fire 结尾+中间段必须是单个 GUID」的形状精确匹配防路径穿透。

**17. `[ApiController]` 特性做了什么？参数绑定四来源？**
`[ApiController]` 开启：模型验证失败自动返回 400、自动推断参数来源（复杂类型→Body 简单类型→Query/Route）、强制特性路由、标准化错误格式。四来源：`[FromQuery]`（URL 查询串）、`[FromRoute]`（路径段）、`[FromBody]`（请求体 JSON，一个 action 最多一个）、`[FromHeader]`（请求头）。

**18. 五种过滤器及执行顺序？过滤器和中间件的区别？OperLogFilter 怎么做零侵入日志？**
五种按序：Authorization（授权，最先）→ Resource（资源）→ Action（动作，环绕方法）→ Exception（异常）→ Result（结果）。过滤器是 MVC 级的能拿到控制器/action/绑定后参数，中间件是管道级的不懂 MVC，过滤器在中间件更内层。CP6 的 `OperLogFilter` 是全局 Action 过滤器，所有 API 自动经过，采集用户/接口/参数/耗时/状态码，业务代码零日志。走 Kafka 异步落库、Kafka 挂了降级写 DB 不丢日志。坑：剔除 CancellationToken/IFormFile 等不可序列化参数、跳过登录接口防密码泄露、跳过日志接口防递归。

**19. JWT 三段结构？无状态 JWT 怎么实现登出吊销？**
三段点分隔：Header（签名算法）、Payload（claims 用户信息，Base64 编码非加密不能放敏感信息）、Signature（密钥签名防篡改）。JWT 无状态签名有效就放行，登出吊销靠：每个 token 带唯一 jti claim，登出把 jti 加进黑名单（Redis，TTL=剩余寿命），认证时验签通过后额外查一次黑名单命中就拒。CP6 的 `OnTokenValidated` 钩子就是干这个。

**20. CP6 的统一响应和多语言错误处理怎么设计的？**
所有接口返回 `{ code, message, data }` 统一信封。业务层抛只带错误码的 `BizException`（如 `new BizException("E-SEC-010", 403)`），全局 `BizExceptionMiddleware` 捕获后用当前请求语言把错误码翻译成本地化消息再输出统一信封。错误码和文案存数据库 `Sys_Lang` 表（日中繁英韩五语+租户覆盖），键按 `模块-类型-编号` 命名（`WM-MSG-070`、`E-SEC-001`）。好处：改文案只改数据库不发版、天然国际化、租户可覆盖。

---

## 本章自测清单

对照下面每一项，能脱口讲清楚就打勾。讲不清的回去重看对应小节。

- [ ] 能画出一个 HTTP 请求在 ASP.NET Core 里从 Kestrel 到响应的完整旅程（本章地图）
- [ ] 能说清 Kestrel 是什么、为什么要反向代理（5.1）
- [ ] 能区分 401 和 403、GET/POST/PUT/PATCH/DELETE 的语义和幂等性（5.1）
- [ ] 能解释 Program.cs 两段式、`builder.Build()` 前后的区别（5.2）
- [ ] 能讲 appsettings.Local.json 插源事故的来龙去脉（5.2/5.4）
- [ ] 能解释 IoC / 好莱坞原则 / 构造函数注入（5.3）
- [ ] 能背出三种生命周期的定义、适用、误用后果，和「谁能注入谁」矩阵（5.3）
- [ ] 能解释为什么 DbContext 必须是 Scoped（5.3）
- [ ] 能讲 captive dependency，并用 DbStringLocalizer / 后台服务的 IServiceScopeFactory 模式解释解法（5.3）
- [ ] 能说清 IEnumerable&lt;T&gt; 多实现、keyed services、工厂注册三种花样（5.3）
- [ ] 能解释配置源优先级、环境变量 `__` 约定、IOptions&lt;T&gt; 模式（5.4）
- [ ] 能画中间件洋葱模型，说清 Use/Run/Map（5.5）
- [ ] 能逐条解释 CP6 中间件管道每一步为什么在那个位置（5.5）
- [ ] 能讲 CSRF 原理、双提交防护、fire 端点豁免事故（5.5）
- [ ] 能解释特性路由、路由约束、参数绑定四来源、`[ApiController]` 自动行为（5.6）
- [ ] 能画五种过滤器执行顺序，讲 OperLogFilter 零侵入日志 + Kafka 降级（5.7）
- [ ] 能区分认证 vs 授权，拆解 JWT 三段结构（5.8）
- [ ] 能讲 CP6 登录发 token 的安全设计（防枚举/锁定/BCrypt/jti/Cookie 化）（5.8）
- [ ] 能解释 RequirePermission 特性、三层防线、fail-closed（5.8）
- [ ] 能讲 SignalR 传输回退、Hub、业务→RabbitMQ→消费者→SignalR 全链路、IHubContext（5.9）
- [ ] 能讲后台服务两种基类、手动开 scope、优雅停机（5.10）
- [ ] 能解释 Swagger CustomSchemaIds 冲突解决（5.11）
- [ ] 能讲统一响应信封 + BizException + 多语言消息键设计（5.12）

---

## 动手练习（3 个）

> 这三个练习都基于 CP6 代码，做完能极大加深理解。建议在 `C:\CP6` 里对照真实文件做。

### 练习 1：给 StockController 加一个新端点 + 权限校验 + 统一响应

**目标**：把「控制器 + 路由 + 参数绑定 + 权限 + 统一响应」串起来。

要求：
1. 在 `StockController` 里加一个新 action：`GET api/wms/stock/summary`，接一个 `[FromQuery] string? warehouseCd`，返回该仓库的库存总量（按 `PhysicalQty` 求和）。
2. 用 `[RequirePermission("wms-stock", "view")]` 加权限校验。
3. 返回值包成 `Ok(new { code = 0, message = "OK", data = new { totalQty } })`。
4. 思考题：这个 action 为什么可以不写 `[FromQuery]` 也能正确绑定？（提示：`[ApiController]` 的参数来源推断 + 简单类型规则）
5. 思考题：如果不加 `[RequirePermission]`，CP6 的哪一层防线会在测试阶段发现你漏了？（提示：5.8.7 第③层）

### 练习 2：写一个自定义中间件——请求耗时头

**目标**：吃透中间件洋葱模型的「去程/回程」和注册顺序。

要求：
1. 写一个约定式中间件 `RequestTimingMiddleware`，在**去程**记开始时间，在**回程**算出耗时，写进响应头 `X-Response-Time-Ms`。
2. 在 Program.cs 里用 `app.UseMiddleware<RequestTimingMiddleware>()` 注册。
3. 思考题：这个中间件应该注册在管道的哪个位置才能测到「包含所有下游处理」的总耗时？（提示：越靠外层测到的越全）
4. 思考题：如果你的中间件要用 DbContext（Scoped），构造函数里能注入吗？应该注入到哪里？（提示：5.5.4 的坑）
5. 对照 `OperLogFilter` 的 `Stopwatch` 用法，理解「过滤器」和「中间件」测耗时的差别（过滤器测的是 action 部分，中间件能测更外层）。

### 练习 3：追踪一条业务通知的完整链路

**目标**：把 SignalR + RabbitMQ + 后台服务 + IHubContext 串成一条线。

要求（只需读代码 + 画图，不用写代码）：
1. 从「某业务事件发生」开始，画出完整时序图：业务代码 → `RabbitMQService`（`INotificationPublisher`）→ `cp6.notification` 队列 → `NotificationConsumer`（后台服务）→ `IHubContext<NotifyHub>` → 浏览器。
2. 在图上标注：哪一步用了「后台服务手动开 scope」？哪一步用了 `IHubContext` 而不是 `Hub` 实例，为什么？
3. 思考题：为什么 CP6 不让业务代码直接调 SignalR 推送，而要绕 RabbitMQ 一圈？（提示：解耦、可靠、可扩展——5.9.3）
4. 思考题：`NotificationConsumer` 处理消息失败时 `BasicNackAsync(requeue: true)` 起什么作用？和 Kafka 消费者「不 Commit 就重试」是不是一个思路？
5. 延伸：结合 5.5.5，解释为什么这套 SignalR 依赖 CsrfMiddleware 豁免 `/hubs` 路径才能正常建连。

---

> **本章小结**：ASP.NET Core 的骨架就是「Kestrel 接请求 → 中间件管道处理 → 路由到控制器 → 过滤器把关 → DI 解析出的服务干活 → 统一响应返回」，全部接线写在 Program.cs 的两段式结构里。面试时你要能对着 CP6 这个真实项目，把依赖注入的生命周期、中间件顺序、CSRF 防护、JWT 认证、统一异常处理这几个「必考且能拉开档次」的点讲透，最好每个点都带上一个 CP6 的真实代码或真实事故。把本章的自测清单全部讲清、三个练习做完，这一章就稳了。
