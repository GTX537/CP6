# 04 · ASP.NET Core 管道、DI、认证与授权

ASP.NET Core 应用可以看成两张图：启动时构建的对象图，以及请求时经过的管道图。DI 生命周期错了会产生状态污染；管道顺序错了会产生认证、租户、异常和 CSRF 语义错误。本章用这两张图统一理解框架。

## 1. 启动阶段与请求阶段

```text
Startup:
Configuration
→ ServiceCollection registrations
→ Build ServiceProvider / WebApplication
→ Middleware pipeline mapping
→ Host starts listeners/background services

Request:
Kestrel accepts
→ middleware chain
→ endpoint routing
→ auth/authorization
→ model binding/filter/controller
→ response unwind
```

`AddXxx` 通常注册服务；`UseXxx` 把中间件加入请求链；`MapXxx` 映射终点。把它们混在一起背会难以排错。

## 2. Configuration 的覆盖顺序

后加入配置源通常覆盖相同 key。环境变量、appsettings、local secrets 的顺序决定最终值。

开发者要能回答：

- 某配置来自哪个 provider？
- 当前环境名是什么？
- 空值是未配置还是被后源覆盖为空？
- secret 是否进入仓库/日志？
- 配置变化是否支持 reload，消费者读取的是 snapshot 还是 monitor？

不要在启动日志打印连接串和密钥来“方便排查”。

## 3. DI 是对象图管理

Controller 依赖接口，容器负责构造其实现及子依赖。价值是把创建策略从业务代码移到 composition root。

### 3.1 三种生命周期

| 生命周期 | 实例范围 | 合适对象 | 风险 |
|---|---|---|---|
| Singleton | 整个进程 | 无状态线程安全服务、缓存协调 | 捕获请求状态、线程安全 |
| Scoped | 一次请求/手工 scope | DbContext、tenant context | 被 singleton 捕获 |
| Transient | 每次解析 | 轻量无状态对象 | 过多创建、含 IDisposable |

生命周期不是按“服务重要程度”选择，而按状态和资源边界。

## 4. Captive dependency

singleton 直接持有 scoped 服务，scoped 实例被迫活到进程结束或容器拒绝构建：

```text
Singleton BackgroundWorker
  └─ Scoped CP6Context  ❌
```

正确：singleton 注入 `IServiceScopeFactory`，每次工作创建 scope并释放。

类似地，singleton 不应持有当前用户/租户。需要时在 scope 中解析。

## 5. 构造函数注入过多是设计信号

Controller 注入 12 个服务通常说明职责过多或缺少更高层用例服务。不要用 Service Locator 隐藏依赖；先按业务用例拆分。

但为了数字把相关依赖包装成一个“万能 Facade”也可能掩盖耦合。拆分依据是变化原因和事务边界。

## 6. 中间件是嵌套调用

```csharp
app.Use(async (ctx, next) =>
{
    // before
    await next();
    // after
});
```

请求按注册顺序向内，响应反向返回。异常中间件只有包住后续组件才能捕获其异常。

## 7. CP6 管道的依赖推理

当前关键顺序可概括：

```text
CORS
→ metrics
→ authentication
→ tenant
→ localization
→ business exception
→ CSRF
→ must-change-password
→ authorization
→ endpoints
```

不要把这当永恒正确模板。逐项验证：

- Tenant 依赖认证后的 claim，所以在 authentication 后。
- Authorization 依赖 endpoint metadata 和身份。
- CSRF 要知道请求方法/端点豁免，并在业务执行前拒绝。
- Exception 中间件的位置决定能捕获哪些前置异常。
- Localization 在错误格式化前建立 culture 才能本地化。

## 8. Routing、Authentication、Authorization

Routing 选择 endpoint 并附上 metadata。Authentication 解析凭证，建立 `HttpContext.User`；它不自动拒绝所有匿名请求。Authorization 根据 endpoint policy 决定是否允许。

```text
Authenticate: Who are you?
Authorize: May you do this?
```

资源级权限还要考虑“能操作哪个订单/仓库”，不是只有角色名。

## 9. 认证 Cookie 与 JWT

服务器可使用 JWT bearer handler，但从 httpOnly Cookie 读取 token。客户端不必知道 token 内容。

访问 token 短期、refresh token 长期；refresh 需要轮换、撤销、重放检测和安全 Cookie 属性。退出必须让服务端凭证失效/清 Cookie，不只是删 localStorage 标志。

## 10. CSRF、CORS、XSS 的边界

- CSRF：利用浏览器自动携带凭证伪造写请求。
- CORS：浏览器跨域访问规则，不是服务器认证。
- XSS：恶意脚本在同源执行。

httpOnly 降低 token 被读走；CSRF token 防跨站请求；CSP/转义降低脚本注入。没有一项单独全能。

## 11. Model binding 与 validation

Model binding 从 route/query/header/body 构造参数。DTO 应表达接口允许客户端控制的字段；不要直接绑定 EF 实体，否则 TenantId、审计、状态等字段可能 mass assignment。

验证分层：

- 形状：必填、长度、格式，可在 DTO/validator。
- 业务：库存是否足、状态能否转换，在 service/domain。
- 数据竞争：唯一/rowversion，由数据库与并发处理。

## 12. Controller 的职责

理想 Controller：

```text
HTTP 输入/身份上下文
→ 调用用例
→ 映射稳定 HTTP 状态/envelope
```

复杂事务和领域分支放 service。复杂只读查询可用 query service 或直接清晰 IQueryable，但要避免 Controller 演变成所有层。

## 13. Filters 与 Middleware 怎么选

Middleware 处理跨所有 endpoint 的 HTTP 关注点；MVC filters 能看到 action、arguments、model state。

| 需求 | 更合适 |
|---|---|
| trace、异常、通用安全 header | middleware |
| action 权限 metadata | authorization/filter/handler |
| action 参数校验/审计 | filter 或 endpoint filter |
| 领域事务 | service，不是 filter |

## 14. 稳定错误协议

错误响应至少有稳定 code、用户消息、trace id；开发环境可有更多诊断，生产不泄露堆栈/SQL。

```json
{
  "code": "WMS_STOCK_INSUFFICIENT",
  "message": "库存不足",
  "traceId": "...",
  "details": null
}
```

映射建议：校验 400、未认证 401、无权限 403、资源不存在 404、并发/状态冲突 409、未知错误 500。具体业务可调整但必须一致。

## 15. CancellationToken 从 HTTP 到数据库

Action 接收 token，传 service，再传 EF/HTTP。不要在中间创建 `CancellationToken.None` 截断。

请求断开后记录 OperationCanceledException 为 Error 会制造噪声；区分正常取消与服务器超时。

## 16. Options 模式

`IOptions<T>` singleton 配置，`IOptionsSnapshot<T>` 每 request 刷新，`IOptionsMonitor<T>` 可观察变化。关键配置启动时 ValidateOnStart，避免第一次请求才发现缺 key。

配置 class 不应包含运行时可变业务状态。

## 17. HttpClientFactory

不要每请求 new/dispose HttpClient 造成连接问题，也不要永久 HttpClient 忽略 DNS 变化。Factory 管 handler 生命周期、命名/typed client、超时和 resilience。

重试只用于安全或幂等操作；POST 要幂等键。重试、timeout、circuit breaker 顺序影响总耗时。

## 18. SignalR

Hub 连接是长连接，认证、租户 group 和断线重连必须设计。`Clients.All` 在多租户系统通常危险；按可信 claim 加 group。

SignalR 适合通知“数据变了”，客户端再拉权威数据；不应把它当唯一持久消息通道。

## 19. BackgroundService

宿主启动/停止管理它。循环必须响应 token、处理异常防宿主意外退出、创建 scoped 依赖、暴露健康/lag。

一个 catch 吞所有异常继续虽然保持进程，但可能无限失败；需要退避、告警和毒任务隔离。

## 20. 日志与 trace

结构化日志：

```csharp
logger.LogInformation(
    "Stock adjusted {TxnNo} tenant={TenantId} product={ProductCd}",
    txnNo, tenantId, productCd);
```

不要字符串拼接。敏感字段、Cookie、token、PII 不入日志。一次请求用 trace id 串联前端、API、SQL和消息。

## 21. 健康检查

Liveness 表示进程是否应重启；readiness 表示是否能接流量。数据库/Kafka 短暂失败是否让 liveness 失败要谨慎，否则所有实例重启放大故障。

## 22. 必做实验

1. 写三个中间件输出 before/after，验证嵌套顺序。
2. 故意让 singleton 依赖 scoped，观察 scope validation。
3. 调整异常中间件位置，制造 TenantMiddleware 异常看格式差异。
4. 用无认证/无权限/无 CSRF/完整请求验证状态码。
5. 客户端取消慢查询，确认 token 到 EF。
6. 建两个租户 SignalR 连接，验证 group 隔离。

## 23. 闭卷问题

1. Add/Use/Map 各在构建什么？
2. Scoped DbContext 为什么不能进 singleton？
3. 中间件响应为什么反向经过？
4. Authentication 为什么不等于 Authorization？
5. DTO 为什么不应直接用实体？
6. Filter 与 Middleware 怎样选？
7. 401/403/409 分别表达什么？
8. HttpClient 重试 POST 的风险？
9. SignalR 为什么不能当持久消息？
10. readiness 与 liveness 为什么不能混？
