# 06 · ASP.NET Core、启动与依赖注入

## 1. 一次应用启动发生什么

简化流程：

```text
WebApplication.CreateBuilder(args)
  → 建配置和日志
  → 向 IServiceCollection 注册服务
  → builder.Build() 生成宿主
  → 配置中间件和端点
  → app.Run() 启动监听
```

关键区分：注册阶段描述“怎么创建服务”；运行阶段按请求/作用域真正解析服务。

## 2. Kestrel、反向代理和 Controller

- Kestrel 是 ASP.NET Core 跨平台 Web 服务器。
- 生产常在反向代理、Ingress 或隧道之后。
- 中间件处理跨端点的请求逻辑。
- Controller 把 HTTP 输入翻译为应用调用，再把结果翻译为 HTTP 响应。

不要说 Controller 是“业务逻辑层”。理想情况下它保持薄，但输入验证、状态码和协议转换仍是它的职责。

## 3. DI 的三个生命周期

| 生命周期 | 创建频率 | 适合 | 风险 |
|---|---|---|---|
| Transient | 每次解析 | 轻量、无状态 | 对象多；同请求不共享 |
| Scoped | 每个 scope，Web 中通常每请求 | DbContext、请求用户上下文 | 不能被 singleton 长期持有 |
| Singleton | 宿主一份 | 无状态服务、线程安全缓存/客户端 | 共享状态竞争、captured scoped |

CP6：

- `CP6Context` 由 `AddDbContext` 注册为 scoped。
- 业务 Service 大量 scoped。
- `CacheService`、Kafka/RabbitMQ 工具注册 singleton。
- BackgroundService 由宿主管理，实质是 singleton。

## 4. Captive Dependency

singleton 构造函数直接接收 scoped 服务，会把请求级对象“囚禁”到应用生命周期，造成跨请求共享、已释放对象或错误租户。

修复方式：

- 重新评估 singleton 是否必要。
- 在执行时使用 `IServiceScopeFactory` 创建 scope。
- 让 singleton 依赖线程安全、无 scope 状态的抽象。

不要使用 service locator 到处 `GetService` 隐藏依赖。scope factory 是宿主生命周期边界的特例。

## 5. 注册同一实现的两个服务类型

CP6 工作流中有这种注册：

```csharp
services.AddScoped<FlowEngine>();
services.AddScoped<IFlowEngine>(sp => sp.GetRequiredService<FlowEngine>());
```

目的：接口和具体类型在同一 scope 共享同一个实例。若分别：

```csharp
services.AddScoped<FlowEngine>();
services.AddScoped<IFlowEngine, FlowEngine>();
```

容器可能把它们视作两个独立注册并创建两个 scoped 实例。是否有影响取决于实现是否有状态，但显式共享更清楚。

## 6. 多实现与 `IEnumerable<T>`

工作流注册多个 `INodeHandler`。解析 `IEnumerable<INodeHandler>` 时得到全部实现，适合策略/处理器集合。

设计要点：

- 每个 handler 有稳定的 can-handle key。
- 重复 key 要启动时失败，不能随机取一个。
- 无匹配项要返回清晰错误。
- 顺序是否有语义需明确。

## 7. 配置系统

默认配置来源按后添加覆盖前添加。常见来源：

```text
appsettings.json
→ appsettings.{Environment}.json
→ User Secrets（开发）
→ 环境变量
→ 命令行
```

CP6 插入 `appsettings.Local.json` 时特意放在环境变量源之前，保证容器环境变量仍有更高优先级。这来自真实配置覆盖风险。

### Options 模式

比到处使用字符串 key 更稳：

```csharp
services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
```

可进一步使用 `ValidateDataAnnotations()`、`ValidateOnStart()` 在启动阶段暴露缺失配置。

机密不应写入仓库 appsettings；使用环境变量、Secret 管理或本地忽略文件。

## 8. Controller 绑定与验证

`[ApiController]` 提供参数绑定推断和模型验证行为。常见来源：

- `[FromRoute]`
- `[FromQuery]`
- `[FromBody]`
- `[FromHeader]`
- 服务注入

DTO 应表达边界契约，不直接暴露完整 EF 实体给写接口，否则容易 over-posting：调用方提交本不该修改的字段。

CP6 某些通用仓储把整个实体标 Modified，面试可讨论 DTO + 显式映射的安全性。

## 9. 路由

```csharp
[Route("api/wms/stock")]
[HttpGet("{stockId:guid}/history")]
```

`:guid` 是路由约束，匹配阶段就排除不合法形状。路由命名要围绕资源/动作语义，一致优先于教条化 REST。

## 10. 响应状态码

| 情况 | 常见状态 |
|---|---:|
| 成功查询 | 200 |
| 成功创建 | 201 + Location |
| 无响应体成功 | 204 |
| 输入/业务前置失败 | 400/422，按项目约定 |
| 未认证 | 401 |
| 已认证但无权 | 403 |
| 不存在 | 404 |
| 并发/状态冲突 | 409 |
| 未处理系统故障 | 500 |

HTTP 状态和业务错误码可以并存。不能所有失败都返回 200，否则代理、告警和客户端重试难以正确工作。

## 11. 日志

结构化日志：

```csharp
logger.LogInformation(
    "库存事务已提交 {TxnNo} {WarehouseCd} {ProductCd}",
    txnNo, warehouseCd, productCd);
```

不要字符串拼接。不要记录密码、token、敏感个人信息。每次请求最好有 TraceId/CorrelationId，跨消息也传播关联键。

## 12. 可观测性

日志回答“发生了什么”，指标回答“发生多少/多慢”，追踪回答“请求经过哪里”。CP6 使用 Prometheus HTTP metrics 和业务 Bridge 指标。面试排障应把三者结合：

- 先看错误率、延迟和流量变化。
- 用 trace/correlation 缩小请求。
- 用结构化日志和数据库证据找根因。

## 13. CP6 Program.cs 的阅读方法

不要从第一行逐个背数百个注册。分组阅读：

1. 基础框架：Controller、Swagger、DbContext、缓存。
2. 横切能力：i18n、日志、权限、认证。
3. 业务域：Wf、Fin、Pur、Plan、ERP、MES、WMS、Space。
4. 基础设施：消息、SignalR、后台任务。
5. 管道顺序与端点。

面试展示的是“能从大型装配文件找结构和风险”，不是记住每个服务名。

## 高频陷阱

1. Scoped 等于线程局部。
2. Singleton 自动线程安全。
3. 接口和实现分别注册也一定共享同一实例。
4. Controller 越薄越好，所以不能有任何协议逻辑。
5. 环境变量总会覆盖自定义后添加的配置源。
6. 返回 200 + 错误码对监控没有影响。

## 闭卷验收

- [ ] 画启动阶段和请求阶段的分界。
- [ ] 给三个生命周期各举 CP6 例子和错误用法。
- [ ] 解释配置源顺序事故。
- [ ] 说明 DTO 如何防 over-posting。
- [ ] 把 Program.cs 注册按五类整理出来。

