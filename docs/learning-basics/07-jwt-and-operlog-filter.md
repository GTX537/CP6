# 07 · JWT 认证 + 全局过滤器审计

## 🌱 你将学到

- 用户登录后服务端怎么"认出"他是谁——JWT 的底层原理
- 看懂 `OperLogFilter`：一段拦截所有请求的代码
- 知道为什么登录接口不能记日志（你以为是 bug 其实是设计）
- 区分认证（Authentication）和授权（Authorization）

---

## 🍳 生活类比：电影院的票根

你进电影院：

1. 售票处买票（登录）
2. 售票员给你一张票根，上面写着场次、座位、有效时间，加盖防伪章（JWT）
3. 检票员看票根防伪 → 让你进场（验签）
4. 看完电影离场，下一场要重新买票（token 过期）

JWT 就是这种"票根 + 防伪章"机制。

**关键性质**：

- 票根上的字大家都能看（JWT 内容不加密，只签名）
- 防伪章不容易伪造（HMAC 签名需要密钥）
- 票根自带有效期（JWT 有 exp 字段）
- 售票处不需要记你买了哪张票（服务端无状态）

---

## 🔎 看 CP6 代码

### JWT 注册

`Program.cs`：

```csharp
var jwt = builder.Configuration.GetSection("JWT");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,           // 检查 token 没过期
            ValidateIssuerSigningKey = true,   // 检查签名
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!))
        };
    });
builder.Services.AddAuthorization();
```

意思：注册一种叫"JwtBearer"的认证方式，验签用 `jwt["Secret"]` 这个密钥。

### 登录接口签发 token

```csharp
[HttpPost("login")]
[AllowAnonymous]   // 这个接口不需要登录就能访问
public async Task<IActionResult> Login([FromBody] LoginRequest req)
{
    var user = await _context.Sys_Users.FirstOrDefaultAsync(u => u.UserName == req.UserName);
    if (user == null || !VerifyPassword(req.Password, user.PasswordHash))
        return Unauthorized(new { code = 401, message = "用户名或密码错误" });

    var token = JwtHelper.GenerateToken(user, _jwt);   // 生成 JWT
    var menus = await GetUserMenusAsync(user.Id);

    return Ok(new
    {
        code = 200,
        data = new { token, menus, userName = user.UserName }
    });
}
```

### JWT 生成

```csharp
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
        claims: claims,                    // 这些 claim 放进 token
        expires: DateTime.Now.AddHours(6),  // 6 小时过期
        signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

生成的 token 长这样（三段，用 `.` 分隔）：

```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0dCIsImV4cCI6MTcyOTQ1NjAwMH0.signature_xxxxx
```

- 第一段：header（哪种算法）
- 第二段：payload（claims，base64 编码可读）
- 第三段：签名（防篡改）

去 [jwt.io](https://jwt.io/) 可以贴一个 token 看里面的内容。

### OperLogFilter — 全局过滤器

`D:\CP6\CP6.WebApi\Filters\OperLogFilter.cs`：

```csharp
public class OperLogFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();

        // 记录入参（POST/PUT/DELETE）
        string? requestBody = null;
        if (context.HttpContext.Request.Method is "POST" or "PUT" or "DELETE")
        {
            var args = context.ActionArguments
                .Where(kv => kv.Value is not CancellationToken
                          && kv.Value is not IFormFile)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (args.Count > 0)
                requestBody = JsonSerializer.Serialize(args);
        }

        // 先放业务通过！
        var resultContext = await next();
        stopwatch.Stop();

        var path = context.HttpContext.Request.Path.Value ?? "";

        // 跳过登录接口（防密码泄露）
        if (path.Contains("/api/auth", StringComparison.OrdinalIgnoreCase))
            return;

        // 跳过日志接口本身（防递归）
        if (path.Contains("/api/operlog", StringComparison.OrdinalIgnoreCase))
            return;

        // GET 默认不记
        if (context.HttpContext.Request.Method == "GET" && !_includeGet)
            return;

        // 构造日志对象
        var log = new Sys_OperLog
        {
            UserName = context.HttpContext.User.FindFirst(ClaimTypes.Name)?.Value,
            HttpMethod = context.HttpContext.Request.Method,
            RequestUrl = path,
            RequestBody = requestBody,
            StatusCode = /* 从 resultContext 拿 */,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            ClientIp = context.HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreateDate = DateTime.Now
        };

        // 投递到 Kafka，失败降级写 DB
        var published = false;
        if (_transport.IsConnected)
        {
            try { await _transport.PublishAsync(log); published = true; }
            catch { /* 吞掉 */ }
        }
        if (!published)
        {
            _context.Sys_OperLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}
```

---

## 🤔 为什么这样

### Q1: 为什么 JWT 内容不加密

JWT 的 payload 是 base64 编码（不是加密）。任何人拿到 token 都能解开看。这是有意的：

- 加密会带来性能开销
- 客户端也需要能读 token 里的 userId（如显示用户名）
- 服务端只要验签名能确保内容没被改

但**所以**：JWT 不能放敏感信息（密码、SSN）。

### Q2: JWT 怎么"无状态"

传统 Session：服务端存 `sessionId → userId` 的字典。每次请求查字典。
JWT：服务端不存任何东西。每次请求验签名（CPU 算一下）。

**好处**：服务端不存状态 → 可以水平扩展（多副本不用共享 session）。
**坏处**：注销很难（服务端不知道哪些 token 还有效）。CP6 当前没做主动注销，靠 token 过期。

### Q3: 认证（Authentication）vs 授权（Authorization）

```
- 认证：你是谁？           (UseAuthentication 中间件，解 JWT)
- 授权：你能不能做这事？   (UseAuthorization 中间件，检查 role / policy)
```

例子：

```csharp
[Authorize]                       // 必须登录
[Authorize(Roles = "Admin")]      // 必须是 Admin
[AllowAnonymous]                  // 跳过认证（如登录接口）
```

CP6 当前没全局加 `[Authorize]`，部分接口没保护。生产应该加。

### Q4: ActionFilter 是什么时候跑

```
请求进来 → 路由匹配 → Authorization filter → Resource filter → 
   Action filter（OperLogFilter 在这）→ Action 方法 → 
   Result filter → 返回响应
```

OperLogFilter 在 Action 方法执行**前后**都能介入。CP6 用法：

- 前：开始 stopwatch + 序列化入参
- 调 `await next()` 让 Controller Action 真正跑
- 后：拿 StatusCode + 耗时 + 写日志

### Q5: Kafka 不可用为什么要降级写 DB

OperLogFilter 的核心承诺："任何操作都被记录"。如果 Kafka 挂了直接丢日志 → 审计断链 → 违反承诺。

降级写 DB 保证不丢，但牺牲 Kafka 的高吞吐优势。短暂故障期可接受。

---

## ⚠️ 容易搞错的地方

### 1. JWT Secret 太短

```csharp
new SymmetricSecurityKey(Encoding.UTF8.GetBytes("abc"))   // ❌ 报错
```

HMAC-SHA256 要求 secret 至少 32 字节（256 bit）。CP6 的 `docker-compose.yml` 用 `JWT_SECRET:?Set JWT_SECRET (>=32 chars) in .env` 强制校验长度。

### 2. JWT 放敏感信息

```csharp
new Claim("password_hash", user.PasswordHash)   // ❌ JWT 内容公开
```

任何人能解开看到密码哈希。CP6 只放 userId、userName、roleId。

### 3. Filter 注册成 Add

```csharp
opt.Filters.Add<OperLogFilter>()   // ❌ Singleton 创建，但 OperLogFilter 依赖 Scoped DbContext
```

要用 `Filters.AddService<OperLogFilter>()` + 独立 `AddScoped<OperLogFilter>()`。第 02 章踩坑 2 提过。

### 4. 序列化入参时遇到 IFormFile / CancellationToken

```csharp
var json = JsonSerializer.Serialize(context.ActionArguments);   // ❌ CancellationToken 不可序列化 → 抛异常
```

CP6 的 OperLogFilter 排除了 `CancellationToken / IFormFile / Stream` 这些不可序列化的：

```csharp
var args = context.ActionArguments
    .Where(kv => kv.Value is not CancellationToken
              && kv.Value is not IFormFile
              ...)
    .ToDictionary(...);
```

新人加新接口可能踩这个坑。这是 CP6 真实踩过的。

### 5. 记日志失败影响主业务

```csharp
// ❌ 反例
await _context.Sys_OperLogs.AddAsync(log);
await _context.SaveChangesAsync();   // 如果挂了，整个请求 500
```

CP6 的 OperLogFilter 把所有日志写入都包 try-catch 吞掉，**绝不影响业务**。这是设计原则：日志是辅助，不能反客为主。

---

## ✋ 动手试试

### 任务 1：拆一个 JWT 看里面

启动 CP6 后端，用 Postman 调登录接口：

```
POST http://localhost:9991/api/auth/login
Body: { "userName": "admin", "password": "你设置的密码" }
```

拿到返回的 `token` 字符串，复制到 [jwt.io](https://jwt.io/)。

观察：

- header 是什么算法？
- payload 里有哪些 claim？
- exp 是什么时候过期？

亲眼看到 JWT 三段结构。

### 任务 2：用 token 调一个需要登录的接口

```
GET http://localhost:9991/api/order/list
Header: Authorization: Bearer <你的 token>
```

去掉 Header 试一遍 → 401。带上 Header → 正常。

这就是 JWT 鉴权的实际效果。

### 任务 3：看 OperLog 表

登录后操作几下（创建一个东西、改一个东西、删一个东西）。

打开数据库查：

```sql
SELECT TOP 20 * FROM Sys_OperLog ORDER BY CreateDate DESC;
```

每条操作有对应记录吗？登录那次有吗（应该没有，因为 /api/auth 被跳过）？

看一下 RequestBody 字段，确认入参被序列化了。

### 任务 4：故意改 token 一个字符再调

把 token 的某个字符改掉（比如把 `e` 改成 `f`），再调那个接口。

应该 401。原因：签名不对了，验签失败。这就是 JWT 的"防伪"。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/07-jwt-and-operlog-filter.md`](../learning/07-jwt-and-operlog-filter.md)
- [JWT.io 官网](https://jwt.io/) — 在线 decode + 文档
- [ASP.NET Core - Filters 概述](https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/filters)
- 关键词搜索："JWT 工作原理"、"Authentication vs Authorization"
