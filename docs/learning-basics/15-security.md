# 15 · 安全清单

## 🌱 你将学到

- "我的项目会不会被黑" 这个问题的本能反应该是什么
- SQL 注入 / XSS / CSRF 到底是怎么回事，CP6 哪里能防住、哪里没防
- 密码不能明文存——但 MD5 也不行，为什么
- 安全不是一个"功能"，是渗透在每一处的习惯

---

## 🍳 生活类比：餐厅的食品安全

开餐厅，要防：

- 食材腐烂 → 冰箱 + 进货检查 → SQL 注入防护
- 客人偷溜进厨房 → 门禁 → 认证授权
- 别人冒名预订 → 身份核对 → CSRF 防护
- 服务员把客人信息卖了 → 员工守则 → PII 加密
- 装监控防偷 → 防盗系统 → 审计日志

每一种都不是"装一次完事"，是日常运营的一部分。软件安全也是。

---

## 🔎 看 CP6 代码（防什么、怎么防）

### 1. SQL 注入

**风险**：用户输入直接拼进 SQL：

```csharp
// ❌ 反例
var sql = $"SELECT * FROM Users WHERE Name = '{input}'";
```

如果 `input = ' OR 1=1 --`，SQL 变成：

```sql
SELECT * FROM Users WHERE Name = '' OR 1=1 --'
```

返回所有用户。或者更糟，`'; DROP TABLE Users; --` 直接删表。

**CP6 防护**：

- EF Core 自动参数化（`Where(o => o.Status == input)` 是参数不是字符串拼接）
- Dapper 用 `@param`：

```csharp
var sql = "SELECT * FROM T_Stock WHERE ProductCd = @cd";
await conn.QueryAsync<Stock>(sql, new { cd = input });
```

**唯一危险点**：`FromSqlRaw` 拼字符串。CP6 用 `FromSqlInterpolated` 替代：

```csharp
// ✅ 安全
_ctx.Stocks.FromSqlInterpolated($"SELECT * FROM T_Stock WHERE ProductCd = {input}");
```

`$"..."` 看起来像拼接，但编译器把 `{input}` 转参数。

### 2. XSS（跨站脚本）

**风险**：用户输入存 DB，别的用户看的时候浏览器执行了里面的 JS。

例：留言板，一个用户提交 `<script>alert('xss')</script>`，其他人看留言时执行了。

**CP6 防护**：

- Vue 模板 `{{ x }}` 自动转义（不会执行 HTML）
- 不用 `v-html`（这个直接渲染 HTML，危险）

**例外**：

```vue
<!-- ❌ 危险 -->
<div v-html="userInput"></div>

<!-- ✅ 必须 v-html 时先净化 -->
<div v-html="DOMPurify.sanitize(userInput)"></div>
```

CP6 没用 v-html，没这个风险。

### 3. CSRF（跨站请求伪造）

**风险**：用户登录了网站 A，访问恶意网站 B，B 偷偷发请求给 A（浏览器自动带 A 的 cookie）。

**CP6 防护**：用 JWT in localStorage，不用 cookie。

为什么 localStorage 防 CSRF：恶意网站的 JS 无法读 localStorage（受 Same-Origin Policy 保护）。

注意：localStorage 怕 **XSS**（XSS 能读 localStorage 偷 token）。所以 XSS 比 CSRF 更可怕，要严防。

### 4. 密码存储

**风险**：明文存 → DB 泄露 = 所有用户密码裸奔。

**演进**：

- 明文 ❌ 1980 年的水平
- MD5 / SHA1 ❌ 1995 年破了，GPU 每秒算几亿次，彩虹表攻击
- SHA256 不加盐 ❌ 彩虹表
- bcrypt / Argon2 / PBKDF2 ✅ 自动加盐，可调成本（GPU 也慢）

**.NET 用法**：

```csharp
using var hasher = new PasswordHasher<Sys_User>();
user.PasswordHash = hasher.HashPassword(user, plainPassword);

// 验证
var result = hasher.VerifyHashedPassword(user, user.PasswordHash, input);
if (result == PasswordVerificationResult.Success) ...
```

CP6 当前用什么哈希需要确认，建议用 `PasswordHasher<T>`（PBKDF2）。

### 5. JWT 安全

详见第 07 章。要点：

- Secret ≥ 32 字符（CP6 强制）
- Payload 不放敏感信息
- HTTPS 传输（cloudflared 自动 HTTPS）

### 6. 权限漏洞

**最常见的真实漏洞**：越权访问。

例：用户 A 登录后改 URL 里的 `orderId` 看到了用户 B 的订单。

**CP6 现状**：

- ✅ JWT 认证（认你是谁）
- ⚠️ 没全局 `[Authorize]`，部分接口公开
- ❌ 没数据行级权限（业务员能看所有订单）

**改进方案**：

```csharp
// Program.cs 全局默认要登录
builder.Services.AddControllers(opt =>
{
    var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    opt.Filters.Add(new AuthorizeFilter(policy));
});

// Controller
public async Task<IActionResult> Get(Guid id)
{
    var order = await _svc.GetAsync(id);
    if (order == null) return NotFound();
    
    // 行级权限
    if (User.IsInRole("Sales") && order.SalesUserId != GetCurrentUserId())
        return Forbid();
    
    return Ok(order);
}
```

### 7. 密钥管理

| 环境 | CP6 做法 |
|---|---|
| 本地开发 | `appsettings.Local.json` + `.gitignore` |
| Docker | `.env` + `${VAR}` |
| K8s | `Secret` 对象 + `envFrom: secretKeyRef` |

绝不能把密钥提交到 Git。CP6 都做对了。

---

## 🤔 为什么这样

### Q1: 为什么 MD5 不行

技术原因：

- 1995 年发现弱点
- 2004 年发现实用碰撞攻击
- GPU 每秒算几亿次 → 6 位密码秒破
- 没加盐 → 彩虹表（预算好的 MD5 → 原文映射表）查表攻击

正确密码哈希要满足：

- **慢**：故意慢，让爆破不可行（bcrypt 默认每次几十毫秒）
- **可调**：硬件升级时可加大成本
- **加盐**：每用户独立盐，挡彩虹表

### Q2: HTTPS 是必须的吗

是。HTTP 明文传输，wifi 上任何人都能截获 token / 密码。

cloudflared 自动给你 HTTPS（甚至比自己买证书简单）。本地开发可以用 HTTP，生产绝不能。

### Q3: 全局 [Authorize] 怎么不加白名单

```csharp
// 全局默认要求登录
builder.Services.AddControllers(opt =>
{
    var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    opt.Filters.Add(new AuthorizeFilter(policy));
});

// 登录、健康检查、Swagger 这些用 [AllowAnonymous]
[HttpPost("login")]
[AllowAnonymous]
public Task Login(...) { }
```

默认严格 + 显式白名单。比"默认放行 + 手动 [Authorize]"安全得多（你忘了加 [Authorize] = 漏洞）。

### Q4: 怎么防 SSRF（服务端请求伪造）

CP6 当前没有"服务端代用户拉外部 URL"的功能，不存在 SSRF。

如果将来加（如 Webhook 配置），必须：

- 白名单允许的 host
- 禁止内网 IP（10.x / 172.16.x / 192.168.x / 169.254.x）
- 不能访问云元数据服务 (169.254.169.254)

---

## ⚠️ 容易搞错的地方

### 1. catch 异常返回给前端

```csharp
// ❌ 反例
catch (Exception ex)
{
    return BadRequest(ex.ToString());   // 暴露 SQL / 堆栈
}
```

正确：

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to process");
    return Problem("操作失败，请联系管理员");
}
```

ASP.NET Core 的 `app.UseExceptionHandler` 默认就这么做。

### 2. Open Redirect

```csharp
// ❌ 攻击者可以让你跳到钓鱼网站
return Redirect(returnUrl);
```

修复：用 `LocalRedirect`（只允许本站 URL）：

```csharp
return LocalRedirect(returnUrl);
```

### 3. Swagger 在生产暴露

```csharp
app.UseSwagger();
app.UseSwaggerUI();
```

Swagger 暴露所有 API 形状，方便攻击者打探。生产只在开发环境开：

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

CP6 当前没这个判断，是改进点。

### 4. 异常字符串里有敏感数据

```csharp
throw new Exception($"连接失败: server={connString}");   // ❌ 连接串可能含密码
```

异常 message 可能进日志、监控系统。不要包含密码、token、PII。

### 5. 凭证误进 Git

```
appsettings.json 里写真实密码 → git push → 全世界都能看到
```

CP6 的对策：

- `.gitignore` 排除 `appsettings.Local.json`
- `appsettings.json` 里只放占位符
- 真值通过环境变量 / Secret 注入
- pre-commit 用 gitleaks 扫敏感字符串（可加）

### 6. 没账号锁定

```
攻击者无限次试密码 → 总能猜对
```

正确：

```csharp
if (failedAttempts >= 5)
{
    user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
    await _ctx.SaveChangesAsync();
}
```

CP6 当前没做，是常见改进点。

---

## ✋ 动手试试

### 任务 1：故意试一次 SQL 注入（在本地）

启动 CP6，找一个搜索接口（如 `BusinessPartner` 搜索）。

在搜索框输入：

```
' OR 1=1 --
```

观察会怎样：

- 如果代码用 EF Core 参数化 → 没事，按字符串搜
- 如果有人写了 `FromSqlRaw($"...{input}...")` 拼接 → 可能返回所有记录

通常 CP6 是安全的。亲手试一次让你知道"什么情况会出事"。

### 任务 2：看 JWT 里有什么

第 07 章已经做过。再确认一次 payload 里没有 PasswordHash 之类的敏感字段。

### 任务 3：检查 .gitignore

打开 `D:\CP6\.gitignore`，搜：

```
appsettings.Local.json
cloudflared-docker
.env
```

应该都在排除列表里。如果没有 = 红色警报，需要立即加。

### 任务 4：找一个未保护的接口

启动后端，**不登录**，直接调：

```
GET http://localhost:9991/api/order/list
```

- 如果返回 401 → 这个接口有保护
- 如果返回订单数据 → 这个接口未保护，敏感数据裸奔

试几个接口，找出哪些未保护。生产应该加全局 `[Authorize]`。

### 任务 5：用密码哈希器算一次

新建一个 console 项目或在 CP6 测试里：

```csharp
var hasher = new PasswordHasher<object>();
var hash1 = hasher.HashPassword(null!, "mypassword");
var hash2 = hasher.HashPassword(null!, "mypassword");
Console.WriteLine(hash1);
Console.WriteLine(hash2);
// 注意：两次 hash 不同（因为盐不同），但都能验证
Console.WriteLine(hasher.VerifyHashedPassword(null!, hash1, "mypassword"));
// PasswordVerificationResult.Success
```

亲眼看到"加盐"的效果——同样密码哈希出不同结果。

---

## 📚 想再学一点

- 高级版本同章节：[`docs/learning/15-security.md`](../learning/15-security.md)
- OWASP Top 10：[官方](https://owasp.org/www-project-top-ten/)
- ASP.NET Core Security：[微软文档](https://learn.microsoft.com/en-us/aspnet/core/security/)
- 关键词搜索："SQL injection"、"XSS"、"CSRF"、"OWASP"
