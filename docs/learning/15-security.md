# 15 · 安全清单

## 📍 学习目标

读完这一章，你能逐项检查 CP6 的安全风险，并能在面试里答出每个常见漏洞的本质 + 防御。

---

## 💡 OWASP Top 10 对照 CP6

### 1. Broken Access Control（最常见的真实漏洞）

**风险**：用户能访问不该访问的资源（如 `/api/order/{id}` 越权看别人订单）。

**CP6 现状**：

- ✅ JWT 认证（认你是谁）
- ⚠️ 没全局 `[Authorize]`，Controller 没标的接口公开
- ❌ 没数据行级权限（业务员能看所有订单）

**修复**：

```csharp
// Program.cs 全局默认要求认证
builder.Services.AddControllers(opt =>
{
    var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    opt.Filters.Add(new AuthorizeFilter(policy));
});

// Controller 里
[HttpGet("{id}")]
public async Task<IActionResult> Get(Guid id)
{
    var order = await _svc.GetAsync(id);
    if (order == null) return NotFound();
    
    // 行级权限：营业员只能看自己的客户
    if (User.IsInRole("Sales") && order.SalesUserId != GetCurrentUserId())
        return Forbid();
    
    return Ok(order);
}
```

### 2. Cryptographic Failures（加密失败）

**风险**：密码明文存储、传输不加密。

**CP6 现状**：

- ✅ HTTPS（cloudflared 自动）
- ⚠️ 密码哈希是否用 bcrypt/argon2？需确认
- ✅ JWT secret 强制 ≥ 32 字符（`docker-compose.yml` 检查）

**修复 / 验证**：

```csharp
// 用户注册 / 改密码时
using var hasher = new PasswordHasher<Sys_User>();   // .NET 自带 Identity 用 PBKDF2
user.PasswordHash = hasher.HashPassword(user, plainPassword);

// 验证
var result = hasher.VerifyHashedPassword(user, user.PasswordHash, input);
if (result == PasswordVerificationResult.Success) ...
```

**绝对不能**：

- `MD5(password)` ← 1995 年就破了
- `SHA256(password)` 不加 salt ← 彩虹表
- 自己手撸加密算法

### 3. Injection（注入）

#### SQL 注入

CP6 几乎免疫，因为：

- EF Core 自动参数化
- Dapper 用 `@param` 参数化

**唯一危险点**：`FromSqlRaw` 拼接字符串：

```csharp
// ❌ 反例
var list = _ctx.Stocks.FromSqlRaw($"SELECT * FROM T_Stock WHERE ProductCd = '{input}'").ToList();

// ✅ FromSqlInterpolated（编译器把 {input} 转参数）
var list = _ctx.Stocks.FromSqlInterpolated($"SELECT * FROM T_Stock WHERE ProductCd = {input}").ToList();
```

#### XSS（跨站脚本）

**风险**：用户输入存 DB 然后渲染 → 别人浏览器执行注入的 JS。

**CP6 现状**：

- ✅ Vue 模板 `{{ x }}` 自动 escape
- ⚠️ 用 `v-html="x"` 时危险（CP6 检查没用过）
- ⚠️ Element Plus 的某些组件接受 raw HTML

**最佳实践**：

```vue
<!-- ✅ 安全 -->
<div>{{ userInput }}</div>

<!-- ❌ 危险 -->
<div v-html="userInput"></div>

<!-- ✅ 必须 v-html 时先 DOMPurify -->
<div v-html="DOMPurify.sanitize(userInput)"></div>
```

#### CSRF（跨站请求伪造）

**风险**：用户登录了网站 A，访问恶意网站 B，B 偷偷发请求给 A（带 A 的 cookie）。

**CP6 现状**：用 JWT in localStorage，不用 cookie → 天然防 CSRF。

**注意**：如果改成 Cookie 鉴权，必须加：

```csharp
builder.Services.AddAntiforgery();
app.UseAntiforgery();
// 前端每个 POST 带 X-CSRF-TOKEN header
```

### 4. Insecure Design（不安全设计）

**风险**：业务逻辑层面的漏洞。

**CP6 例子**：

- ⚠️ 没有"修改密码必须输入旧密码"
- ⚠️ 没有"账号锁定"（5 次失败锁 15 分钟）
- ⚠️ 没有"操作敏感动作需 2FA"

**修复**：

```csharp
[HttpPost("change-password")]
[Authorize]
public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
{
    var user = await _ctx.Sys_Users.FindAsync(GetCurrentUserId());
    if (!VerifyPassword(dto.OldPassword, user.PasswordHash))
        return BadRequest("旧密码错误");
    
    user.PasswordHash = HashPassword(dto.NewPassword);
    await _ctx.SaveChangesAsync();
    return Ok();
}
```

### 5. Security Misconfiguration（配置错误）

**CP6 已做**：

- ✅ `appsettings.Local.json` 在 `.gitignore`
- ✅ Docker secrets via `.env`
- ✅ K8s Secret 对象

**漏洞点**：

- ⚠️ `Microsoft.AspNetCore.SignalR` 的 `EnableDetailedErrors` 生产环境不能开
- ⚠️ Swagger 在 prod 该关闭或加认证

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

CP6 当前没看到这个判断，Swagger 在 prod 也能访问，泄露 API 形状。

### 6. Vulnerable Components

**风险**：用了带漏洞的 nuget / npm 包。

**检查**：

```bash
# .NET
dotnet list package --vulnerable --include-transitive

# Node
npm audit
```

CI 集成 Dependabot / Renovate 自动跟踪。

### 7. Authentication Failures

**CP6 现状**：

- ✅ JWT 签名
- ⚠️ 没 refresh token，长期登录靠 6h access token
- ⚠️ 没账号锁定
- ⚠️ 密码复杂度策略不明

**面试常考**：

- 怎么实现"同时只允许一个设备登录"？
  → JWT 加 `jti` (JWT ID)，登录时把上次 jti 加入黑名单 Redis SET，验签时检查。
- Refresh token 怎么 rotate？
  → 每次用 refresh 换 access 时同时换新 refresh，旧 refresh 立刻失效，能挡住"refresh 泄露"。

### 8. Software & Data Integrity

**风险**：CI/CD 流水线被篡改，部署带后门的镜像。

**CP6 缓解**：

- ✅ Docker 镜像走自己的构建（不 pull 第三方 latest）
- ⚠️ 没有镜像签名（Cosign / Notary）
- ⚠️ 没有 SBOM（软件物料清单）

### 9. Logging & Monitoring Failures

**CP6 已做**：

- ✅ OperLogFilter 全请求审计
- ✅ DeadLetterNotifier 双通道告警

**漏洞**：

- ⚠️ 失败登录没单独告警（仅记 Sys_OperLog）
- ⚠️ 异常操作（凌晨 3 点删 100 条订单）没行为分析

### 10. SSRF（服务端请求伪造）

**风险**：用户输入 URL，服务端去 fetch，被诱导请求内网（如 `http://169.254.169.254/` 拿云元数据）。

**CP6 现状**：当前没有"服务端代用户拉外部 URL"的功能。如果将来加（如 Webhook 配置），必须：

- 白名单允许的 host
- 禁止内网 IP（10.x / 172.16.x / 192.168.x / 169.254.x）
- 用 SOCKS 代理隔离

---

## 🔐 CP6 专属安全清单

### 业务安全

| 位置 | 检查项 |
|---|---|
| 受注创建 | 数量、单价、金额上限校验（防"100元卖100万元" 单价手误） |
| 库存调整 | 调整数量阈值（超过 1000 件需主管复核） |
| 用户删除 | 软删除 + 保留 90 天可恢复 |
| 密码重置 | 邮件链接 + 1 次性 token + 15 分钟过期 |
| API 限流 | 防爆破登录、防大数据导出（RateLimiting middleware） |

### 数据安全

| 位置 | 检查项 |
|---|---|
| 数据库备份 | 每天全量 + 每小时增量 + 异地存放 |
| 备份恢复演练 | 每季度恢复测试，验证 RPO/RTO |
| PII 加密 | 手机号、身份证号字段在 DB 加密（Always Encrypted） |
| 审计日志 | OperLog 至少保留 3 年（合规） |
| 数据销毁 | 用户注销后 30 天硬删，备份按生命周期清理 |

### 部署安全

| 位置 | 检查项 |
|---|---|
| 容器镜像 | 用最小基础镜像（distroless / chiseled） |
| 容器运行 | non-root user，readOnlyRootFilesystem |
| K8s NetworkPolicy | 限制 Pod 间通信只到必要 Service |
| K8s RBAC | Pod 用 ServiceAccount 最小权限 |
| Secret | etcd encryption at rest + sealed-secrets |

### 监控告警

| 位置 | 检查项 |
|---|---|
| 失败登录 | 1 分钟内 ≥ 10 次 / 同 IP → 告警 + 临时封 IP |
| 异常导出 | 普通用户导出 > 10000 行 → 告警 |
| 异常时间 | 凌晨 2-5 点的关键写操作 → 告警 |
| 权限提升 | 角色改动、用户被改成 Admin → 告警 |
| DLQ 死信 | Bridge Hook DLQ 出现就告警（CP6 已实现） |

---

## ⚠️ 真实事故案例

### 案例 1：JWT Secret 泄露到 Git

某次 commit 把 `appsettings.Local.json` 误提交，几小时后被发现。**处置**：

1. **立即换 secret**：旧 token 全部失效，强制所有用户重登
2. **rotate**：未来 Secret 都通过 Vault / K8s Secret 注入
3. **加 pre-commit hook**：gitleaks 扫敏感字符串

CP6 的 `.gitignore` 把 Local.json 排除了，但仍要 pre-commit 兜底。

### 案例 2：SQL 报错信息泄露表结构

```csharp
catch (Exception ex)
{
    return BadRequest(ex.ToString());   // ❌ 暴露 SQL / stack trace
}
```

修复：

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to process");
    return Problem("操作失败，请联系管理员");   // 给用户的是 friendly 信息
}
```

ASP.NET Core 8 的 `app.UseExceptionHandler` 默认就这么做。

### 案例 3：Open Redirect

```csharp
return Redirect(returnUrl);   // ❌ returnUrl 来自用户输入
```

攻击者诱导 `?returnUrl=https://evil.com/fake-login` 钓鱼。修复：白名单或 `LocalRedirect`：

```csharp
return LocalRedirect(returnUrl);   // 只允许本站 URL
```

---

## 🧪 自检题

1. **越权检查**：用户 A 登录后改 URL 里的 orderId 看到了用户 B 的订单，怎么修？  
   <details><summary>答案</summary>API 层校验：<code>if (order.CreatorId != currentUserId &amp;&amp; !User.IsInRole("Admin")) return Forbid();</code>。或全局用 EF Core <code>HasQueryFilter</code> 自动加 <code>WHERE CreatorId = @currentUserId</code>。<b>原则</b>：永远不要相信前端传的 ID，每个查询都加权限 WHERE。</details>

2. **CSRF 区分**：CP6 用 JWT in localStorage，为什么免疫 CSRF？  
   <details><summary>答案</summary>CSRF 攻击靠浏览器自动带 cookie。JWT 在 localStorage，攻击者的恶意页面无法读你的 localStorage（受 Same-Origin Policy 保护），所以没法在请求里加 Authorization header。注意：localStorage 怕 XSS（XSS 能读 localStorage），所以 XSS 比 CSRF 更危险，必须严防。</details>

3. **密码哈希**：你看到代码 <code>user.PasswordHash = MD5(input)</code>，怎么解释为什么不行？  
   <details><summary>答案</summary>(1) MD5 1995 年就被证明不安全，碰撞容易；(2) MD5 太快（GPU 一秒算几亿次），暴力破解容易；(3) 没加 salt → 彩虹表攻击。正确：<b>bcrypt / Argon2 / PBKDF2</b>，自动加 salt，可调成本（GPU 也慢）。.NET 用 <code>PasswordHasher&lt;T&gt;</code>。</details>

4. **API 限流**：怎么防"暴力破解登录"？  
   <details><summary>答案</summary>(1) <code>app.UseRateLimiter()</code> 配置 IP 维度：1 分钟内 /api/auth/login 最多 5 次；(2) 失败 5 次锁账号 15 分钟（记 DB）；(3) 失败时返回固定的"用户名或密码错误"，不区分 user / password 错（防用户名枚举）；(4) 添加 CAPTCHA（连续失败后）；(5) WAF（Cloudflare 已带）。</details>

5. **质疑题**：开发问"我们 demo 项目，加这么多安全干嘛"，怎么回答？  
   <details><summary>答案</summary>(1) <b>安全是一种习惯</b>，demo 时不写以后写不出来；(2) demo 通常会有真实用户测试，泄露真实数据照样违法；(3) 面试时安全是高频追问点，"你的项目做了哪些安全"是必问；(4) 修一个漏洞 10 分钟，被攻击修复要 10 天 + 商誉损失。CP6 当前安全基线尚可（HTTPS / JWT / 参数化 SQL），但全局 [Authorize]、密码哈希、refresh token、rate limit 都该补。</details>

---

## 🔗 延伸阅读

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [OWASP Cheat Sheet Series](https://cheatsheetseries.owasp.org/) — 每个主题都有实操指南
- [ASP.NET Core Security](https://learn.microsoft.com/en-us/aspnet/core/security/)
- [CIS Kubernetes Benchmark](https://www.cisecurity.org/benchmark/kubernetes) — K8s 安全配置审计
