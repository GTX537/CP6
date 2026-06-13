# 07 · JWT 认证 + 全局过滤器审计

## 📍 学习目标

1. JWT 是什么、它的安全边界在哪？为什么不要存敏感信息？
2. JWT 怎么签发、怎么验签、怎么续签？
3. ActionFilter 的生命周期阶段（Authorization → Resource → Action → Exception → Result）
4. CP6 的 `OperLogFilter` 怎么做到"既不影响业务又记录全部"？
5. 为什么操作日志走 Kafka 而不是 RabbitMQ？两者各自适合什么场景？

---

## 🔎 真实代码切片

### JWT 注册（`Program.cs`）

```csharp
var jwt = builder.Configuration.GetSection("JWT");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!))
        };
    });
builder.Services.AddAuthorization();
```

### 登录签发 Token（`AuthController.Login` 风格）

```csharp
[HttpPost("login")]
[AllowAnonymous]
public async Task<IActionResult> Login([FromBody] LoginRequest req)
{
    var user = await _context.Sys_Users
        .FirstOrDefaultAsync(u => u.UserName == req.UserName && !u.IsDeleted);
    if (user == null || !VerifyPassword(req.Password, user.PasswordHash))
        return Unauthorized(new { code = 401, message = "用户名或密码错误" });

    var token = JwtHelper.GenerateToken(user, _jwt);
    var menus = await GetUserMenusAsync(user.Id);
    return Ok(new
    {
        code = 200,
        data = new { token, menus, userName = user.UserName }
    });
}
```

### JwtHelper

```csharp
public static class JwtHelper
{
    public static string GenerateToken(Sys_User user, IConfigurationSection jwt)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim("role", user.RoleId.ToString() ?? "")
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: DateTime.Now.AddHours(int.Parse(jwt["ExpireHours"] ?? "6")),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

### `OperLogFilter` 全文（节选关键段）

```csharp
// CP6.WebApi/Filters/OperLogFilter.cs
public class OperLogFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();

        // POST/PUT/DELETE 时序列化入参（排除 CancellationToken/IFormFile 等不可序列化对象）
        string? requestBody = null;
        if (context.HttpContext.Request.Method is "POST" or "PUT" or "DELETE")
        {
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
                    if (requestBody.Length > 2000)
                        requestBody = requestBody[..2000] + "...(truncated)";
                }
                catch (Exception ex)
                {
                    requestBody = $"(serialize failed: {ex.GetType().Name})";
                }
            }
        }

        // 关键：先放业务通过，再记日志
        var resultContext = await next();
        stopwatch.Stop();

        var path = context.HttpContext.Request.Path.Value ?? "";

        // 跳过 1：登录接口（防密码泄露）+ 日志接口自身（防递归）
        if (path.Contains("/api/operlog", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/auth", StringComparison.OrdinalIgnoreCase))
            return;

        // 跳过 2：GET 默认不记，配置开关 IncludeGet=true 时才记
        if (context.HttpContext.Request.Method == "GET" && !_includeGet)
            return;

        var log = new Sys_OperLog { /* ... 各字段 ... */ };

        // 主通道：投递 Kafka
        var published = false;
        if (_transport.IsConnected)
        {
            try
            {
                await _transport.PublishAsync(log);
                published = true;
            }
            catch (Exception ex) { Console.WriteLine($"[OperLog] Kafka 投递失败: {ex.Message}"); }
        }

        // 降级：Kafka 不可用 → 直接写 DB
        if (!published)
        {
            try
            {
                _context.Sys_OperLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex) { Console.WriteLine($"[OperLog] 降级写 DB 失败: {ex.Message}"); }
        }
    }
}
```

---

## 💡 资深视角

### JWT 是什么 —— 三段式签名 Token

```
header.payload.signature
   |       |        |
   {alg:HS256, typ:JWT}.{sub:userId, name:..., exp:1234567890}.HMAC_SHA256(...)
```

**关键性质**：

1. **自包含**：服务端不存 session，所有信息在 token 里
2. **签名而非加密**：payload 是 base64url 编码的明文，任何人都能读
3. **HMAC 验签**：服务端用 secret 算签名，跟 token 第三段比对

**所以**：

- ❌ 不要存密码、SSN、信用卡号 —— 任何人能读
- ❌ 不要存大量数据 —— 每次请求都带，浪费带宽
- ✅ 只存 userId、role、一些权限点

### JWT 的过期 + 续签

CP6 默认 6 小时过期：

```csharp
expires: DateTime.Now.AddHours(int.Parse(jwt["ExpireHours"] ?? "6"))
```

**续签策略**：

| 策略 | 怎么做 | 优缺点 |
|---|---|---|
| 用户重新登录 | 过期就跳登录页 | 体验差但安全 |
| 滑动续期 | 每次有效请求服务端发新 token | 简单，但 token 黑名单难 |
| Refresh Token | 短期 access token + 长期 refresh token | 标准做法，CP6 当前没做 |
| 前端被动刷新 | axios 拦截器收到 401 → 用 refresh 换新 token → 重发原请求 | 用户无感 |

CP6 当前是"过期就跳登录页"（最简单，但 6 小时一次登录确实烦）。生产建议加 refresh token + axios 拦截器无感续签。

### Authorize / Authentication 的区别

- **Authentication（认证）**：你是谁？— 解 JWT，填 `HttpContext.User`
- **Authorization（授权）**：你能不能？— 看 User 的 role/claim 决定

```csharp
[Authorize]                                      // 必须登录
[Authorize(Roles = "Admin")]                     // 必须是 Admin
[Authorize(Policy = "RequireAdminClaim")]        // 自定义策略
[AllowAnonymous]                                 // 跳过
```

CP6 的 Controller 没用 `[Authorize]` 顶层装饰，因为是 demo 项目。生产应该在 `Program.cs` 加：

```csharp
builder.Services.AddControllers(opt =>
{
    opt.Filters.Add(new AuthorizeFilter());  // 全局默认要登录
});
```

然后登录、Swagger、健康检查这些用 `[AllowAnonymous]` 显式放行。

### ActionFilter 的 5 个阶段

```
┌─ Authorization filter   (验 [Authorize])
│
├─ Resource filter        (能拦请求体，常用于缓存)
│
├─ Action filter          ← OperLogFilter 在这层
│    OnActionExecuting(...)
│    Action 方法
│    OnActionExecuted(...)
│
├─ Exception filter       (能捕获未处理异常)
│
└─ Result filter          (能改 IActionResult)
```

`IAsyncActionFilter.OnActionExecutionAsync` 是 Action 阶段的异步版，把 before/after 合并：

```csharp
public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
{
    // Before
    var stopwatch = Stopwatch.StartNew();

    var resultContext = await next();   // 执行 Action
    
    // After
    stopwatch.Stop();
    // 记录日志
}
```

### OperLogFilter 的 3 个关键设计

#### 1. **先业务后日志** — 日志失败绝不影响业务

```csharp
var resultContext = await next();   // 业务先跑
stopwatch.Stop();
// 后面记日志，所有 catch 都吞异常
```

如果反过来"先记日志再跑业务"，日志失败业务也挂了，是反模式。

#### 2. **跳过 /api/auth 和 /api/operlog** —— 防泄露 + 防递归

- `/api/auth` 包含密码，记录就是泄露
- `/api/operlog` 查日志接口本身，记录会递归（查日志 → 记一条日志 → 查日志 → ...）

#### 3. **入参序列化排除不可序列化对象**

```csharp
.Where(kv => kv.Value is not CancellationToken
             && kv.Value is not IFormFile
             ...)
```

CP6 早期没排除时，遇到带 `CancellationToken` 的方法直接 500。这是真实踩过的坑。

### 为什么操作日志走 Kafka 不走 RabbitMQ

CP6 的设计哲学：

> **Kafka 专任操作日志**（高吞吐、append-only、保留可回放）
> **RabbitMQ 专任业务通知**（低频、确认配信、可路由可重试）

| 维度 | Kafka | RabbitMQ |
|---|---|---|
| 吞吐 | 单机 100K msg/s+ | 单机 20K msg/s |
| 模型 | 分区日志（partition log） | 队列（queue） |
| 消费语义 | 客户端控制 offset，可重放 | broker 控制 ack，消费即销毁 |
| 适合 | 日志流、事件溯源、数据管道 | 业务通知、任务队列、RPC |

操作日志的特点：

- 高吞吐（每秒可能 1000+ 请求）
- 必须保留（合规 + 审计）
- 可能要回放（出问题时重建索引）

→ Kafka 完美匹配。

业务通知的特点：

- 低频（出货完了 / 棚卸差异）
- 必达（漏发邮件影响业务）
- 可路由（不同事件给不同订阅方）

→ RabbitMQ 完美匹配。

**这个区分是 CP6 的成熟之处**。新手项目常用一个 MQ 做所有事，结果两边都将就。

### Kafka 不可用时怎么办

```csharp
if (_transport.IsConnected)
{
    try { await _transport.PublishAsync(log); published = true; }
    catch { /* 吞掉，往下走降级 */ }
}

if (!published)
{
    // 降级：直接写 DB
    _context.Sys_OperLogs.Add(log);
    await _context.SaveChangesAsync();
}
```

降级写 DB 保证日志不丢，但牺牲了 Kafka 的吞吐优势。短暂故障期可接受。**注意**：降级时如果 DB 也不可用，CP6 选择**继续吞掉异常**而不是抛出，因为日志相对于业务永远是次要的。

---

## ⚠️ 踩坑记录

### 坑 1：JWT Secret 太短导致签名弱

```csharp
new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!))
```

HMAC-SHA256 要求 secret 至少 32 字节（256 bit）。如果 `Secret = "abc"`，会抛 `ArgumentOutOfRangeException`。CP6 的 `docker-compose.yml` 有 `JWT_SECRET:?Set JWT_SECRET (>=32 chars) in .env` 强制校验长度。

### 坑 2：JWT 在 Header 大小写敏感

```
Authorization: Bearer eyJxxx...
```

注意 `Bearer` 后面**一个空格**。axios 默认正确，但有些前端框架可能漏。

### 坑 3：CORS + SignalR 配 AllowAnyOrigin 报错

```csharp
// ❌ SignalR 需要 cookie/auth，不能用 AllowAnyOrigin
app.UseCors(p => p.AllowAnyOrigin().AllowCredentials());  // 编译报错
```

CP6 的修复（从 DEVELOPMENT-GUIDE.md 里学的）：

```csharp
app.UseCors(p => p
    .SetIsOriginAllowed(_ => true)   // 替代 AllowAnyOrigin
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials());
```

### 坑 4：Filter 注册成 Add 而不是 AddService

如第 02 章踩坑 1 所述，Filter 依赖 Scoped DbContext 时必须用 `Filters.AddService<T>()` + 单独注册 `AddScoped<OperLogFilter>()`。

### 坑 5：跳过路径用 ToLower 比较小心

```csharp
// ❌ 反例
if (path.ToLower() == "/api/auth/login") ...  // path 可能是 "/api/Auth/Login"

// ✅ CP6 的写法
if (path.Contains("/api/auth", StringComparison.OrdinalIgnoreCase))
```

`StringComparison.OrdinalIgnoreCase` 比 `.ToLower()` 快且不分配内存。

### 坑 6：序列化 IFormFile 抛 NotSupportedException

`CancellationToken.WaitHandle.Handle` 是 `IntPtr`，`System.Text.Json` 默认拒绝。如果不排除会 500 整个 API。CP6 现在的代码就是从这个坑里改出来的。

---

## 🧪 自检题

1. **JWT 安全**：把用户的 PasswordHash 放 JWT 的 claim 里会怎样？  
   <details><summary>答案</summary>等于把密码哈希广播给所有能拿到 token 的人。base64 是公开的，不是加密。攻击者拿 hash 离线爆破。<b>原则：JWT payload 只放 ID 和 role，敏感信息一概不放。</b></details>

2. **续签实战**：用户 6 小时后点保存，提示"登录过期"。怎么改让他无感？  
   <details><summary>答案</summary>(1) 登录时返回 access token (6h) + refresh token (7d)；(2) refresh token 存 DB（带 user/expire/revoked 状态）；(3) axios 响应拦截器看到 401 → 用 refresh 换新 access → 自动 retry 原请求；(4) refresh token 也过期 → 跳登录。CP6 当前没做这套，是改进点。</details>

3. **过滤器顺序**：如果有两个 ActionFilter，一个记日志，一个验数据完整性，应该谁先？  
   <details><summary>答案</summary>验数据完整性先（如果数据不合法可以早返回省一次业务调用）。Filter 注册顺序 = OnActionExecuting 顺序，OnActionExecuted 反向。或者用 <code>IOrderedFilter.Order</code> 显式排序。</details>

4. **场景题**：你想让 GET 也记日志，但不想拖慢响应，怎么做？  
   <details><summary>答案</summary>(1) Filter 里 <code>await next()</code> 之后<b>不 await 投递</b>，开 fire-and-forget：<code>_ = Task.Run(() =&gt; _transport.PublishAsync(log));</code>（注意要 capture scope 的服务）；(2) 投递批量化：BackgroundService 攒 100 条或 500ms 才发一次。CP6 当前 GET 不记，配置开关 <code>IncludeGet=true</code> 才记，是平衡解。</details>

5. **质疑题**：有人说"日志直接写 DB 就行了，搞 Kafka 这么麻烦"。怎么反驳？  
   <details><summary>答案</summary>三个层面：(1) <b>吞吐</b>：高 QPS 服务每秒上千请求都写 DB 直接打死库；(2) <b>解耦</b>：Kafka 让日志可以转发到 ES、Splunk、数仓多个消费者，DB 只能写一份；(3) <b>峰值</b>：Kafka 能消化突发流量，DB 写入有锁会拖慢业务。当 QPS &lt; 50 时直接写 DB 没问题，CP6 选 Kafka 是按"中等规模 MOM 系统的常见日志吞吐"设计的。</details>

---

## 🔗 延伸阅读

- [JWT.io](https://jwt.io/) — 在线 decode 看明白结构
- [OWASP JWT Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html)
- [ASP.NET Core - Filters](https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/filters)
- [Kafka vs RabbitMQ](https://www.confluent.io/blog/kafka-vs-rabbitmq/)
- 项目内：`CP6.WebApi/Filters/OperLogFilter.cs`、`CP6.Core/Utilities/JwtHelper.cs`、`CP6.WebApi/BackgroundServices/KafkaOperLogConsumer.cs`
