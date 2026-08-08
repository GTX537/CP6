# 03 · 多租户、权限、CSRF 与审计边界

安全章节最容易写成名词列表：认证、授权、JWT、CSRF、多租户、审计。开发者真正需要的不是背六个定义，而是回答一条请求在哪些边界被拒绝、每层能防什么、又有哪些代码路径能绕过。本章以“租户 A 的普通用户试图修改租户 B 的库存”为攻击场景，逐层检查当前 CP6。

## 1. 先建立威胁模型

攻击者不一定是匿名黑客，也可能是：

- 已登录的普通用户修改前端请求。
- 租户管理员尝试访问别的租户。
- 平台运维使用 acting-as 时选错租户。
- 后台任务没有 HttpContext，租户上下文为空。
- 开发者写 Dapper/原生 SQL 时忘了 TenantId。
- 批量更新绕过 `SaveChanges` 审计。

安全目标至少包括：

```text
身份真实：知道是谁
租户正确：知道代表哪个租户
权限足够：知道能做什么
请求真实：写操作不是跨站伪造
数据隔离：查询和写入不能越界
操作可追：关键字段变化能审计
```

这些目标不能由一个中间件完成。

## 2. 六层防线

```text
Browser
  │ Cookie + CSRF header
  ▼
Authentication  身份是谁
  ▼
TenantMiddleware  当前租户是谁
  ▼
Authorization / Permission  能做什么
  ▼
EF query filter + write stamp  能看到/写入哪行
  ▼
DB constraint + audit  数据最后是否仍守规则、能否追溯
```

逐层问：输入来自哪里，信任依据是什么，失败是 401、403、409 还是 500，谁能绕过。

## 3. 认证：Cookie 里装的是凭证，不是前端标志

当前前端不是把 JWT 存进 localStorage 再手动加 `Authorization`。axios 使用 `withCredentials`，认证 Cookie 由浏览器携带；`cp6_authed` 之类前端值只帮助路由 UX，不能证明用户真的已认证。

### 3.1 401 与 403 的区别

```text
401：没有有效身份，或会话已失效
403：身份已知，但无权执行当前动作
```

只有 401 才可能触发 refresh。403 若也 refresh，会造成无意义请求甚至循环，而且刷新凭证不会凭空增加权限。

### 3.2 httpOnly 的边界

httpOnly 阻止普通 JavaScript 读取 Cookie 值，降低 token 被直接窃取的风险。但同源 XSS 仍能调用接口，浏览器会自动带 Cookie。因此还要：

- 默认模板转义，谨慎使用 `v-html`。
- CSP 限制脚本来源。
- 依赖漏洞治理。
- 后端逐请求授权。
- 对高风险操作做二次确认或再认证。

## 4. 租户解析：可信 tenant 不能只来自客户端参数

`TenantMiddleware` 从已认证用户的 `tenant_id` claim 解析 Guid，写入 scoped `ITenantContext.CurrentTenantId`。关键顺序是认证在前，租户解析在后。

若 Controller 接收一个任意 `tenantId` query 参数并直接赋给上下文，普通用户就能改参数越权。平台 acting-as 是特殊能力，应有独立权限、审计和明显的 UI 状态，而不是所有用户都能传头切租户。

### 4.1 claim 缺失时发生什么

当前 `CP6Context.CurrentTenantId` 在没有注入租户上下文时回退 `TenantContext.DefaultTenant`，主要为了单测或非 HTTP 场景兼容。

这是一项必须理解的工程取舍：

- 好处：旧测试和单参构造不需要全部改造。
- 风险：生产某条路径漏注入租户时，可能静默落到默认租户，而不是立即失败。

更严格的生产策略可以在非测试环境 fail-closed：缺失租户时抛出明确异常。是否调整要先盘点登录、平台共享表、后台任务和迁移工具的真实需要。

## 5. 全局查询过滤器如何生成

`CP6Context.OnModelCreating` 反射所有继承 `BaseTenantEntity` 的根实体，构造：

```csharp
entity => entity.TenantId == CurrentTenantId
```

EF Core 将它自动合并到普通 LINQ 查询。这比要求每个开发者手写 `Where(TenantId...)` 更不容易遗漏。

### 5.1 为什么表达式引用的是 Context 实例

过滤表达式读取 `CurrentTenantId`，EF 在具体上下文实例执行查询时得到当前租户。若把模型创建时的某个 Guid 固化成常量，模型缓存可能让所有上下文共享错误租户。

### 5.2 根实体反射的边界

代码只对特定继承体系自动注册；一些使用 int 主键、没有继承 `BaseTenantEntity` 的系统表需要手工过滤与盖章。每增加一种特殊实体都要确认：

- 是否进入自动过滤。
- 是否手工 `HasQueryFilter`。
- 唯一索引是否带 TenantId。
- 新增行是否会盖章。
- 测试是否能反射发现漏网实体。

## 6. 唯一约束为什么也必须租户化

查询隔离正确，不代表建数据正确。若产品编码有全局唯一索引 `UNIQUE(ProductCd)`，租户 A 创建 P001 后，租户 B 不能创建同名产品，业务上通常不合理。

多租户唯一性应是：

```text
UNIQUE(TenantId, ProductCd)
```

当前模型对许多租户实体的全局唯一索引自动补 TenantId 前缀，并跳过已经包含 TenantId 或有特殊跨租户检索需求的索引。

开发者必须检查数据库迁移实际生成结果。模型意图正确但迁移未执行，生产约束仍旧不存在。

## 7. 写入盖章只覆盖了哪种情况

`StampTenant` 对 `Added` 且 `TenantId == Guid.Empty` 的租户实体写入当前租户。这个规则解决的是“新对象忘记赋 TenantId”。

它没有自动覆盖一个已被显式赋成其他租户的 TenantId。这样做可能是为了数据迁移或平台操作，但也意味着普通写路径若把客户端 DTO 的 TenantId 映射进实体，可能形成注入风险。

推荐边界：

- 普通租户 API 的创建 DTO 不暴露 TenantId。
- 服务端从 `ITenantContext` 决定租户。
- 普通请求发现非空且不等于当前租户时拒绝，而不是默默接受。
- 平台跨租户操作使用独立服务与权限，并记录 acting-as 审计。

### 7.1 修改已有实体呢

普通查询先受全局过滤，只能加载当前租户实体，因此“加载再修改”相对安全。但以下路径需要单独检查：

- 用客户端提供 Id 创建 stub entity 后 `Attach`。
- 直接设置 `EntityState.Modified`。
- `ExecuteUpdate`。
- Dapper/原生 SQL。
- `IgnoreQueryFilters` 后的修改。

安全审查不能只看最常见的 Repository `GetById`。

## 8. Dapper 与原生 SQL 是显式信任边界

Dapper 不知道 EF Core 的全局过滤器。下面查询即使参数化，也仍可能跨租户：

```sql
SELECT * FROM T_Stock WHERE ProductCd = @productCd;
```

参数化只防 SQL 注入，不负责租户隔离。正确查询至少要有：

```sql
WHERE TenantId = @tenantId
  AND ProductCd = @productCd
```

并且 `tenantId` 来自可信上下文，不来自普通客户端参数。

建议建立 Dapper 代码审查清单：

1. SQL 是否包含 TenantId。
2. 参数是否来自 scoped tenant context。
3. JOIN 的每张租户表是否保证同租户。
4. UPDATE/DELETE 是否同时限制 TenantId。
5. 是否有跨租户平台用例；若有，权限和审计在哪里。

## 9. `ExecuteUpdate` 为什么绕过审计

字段审计依赖 `ChangeTracker.Entries<IAuditable>()` 比较 Original/Current values。`ExecuteUpdate` 直接生成 SQL，不加载实体，也不经过 ChangeTracker；Dapper 同样绕过。

因此以下代码不会自然产生字段审计行：

```csharp
await query.ExecuteUpdateAsync(setters =>
    setters.SetProperty(x => x.Status, NewStatus));
```

这不表示 `ExecuteUpdate` 不能用，而是要明确选择：

- 对需要字段级审计的少量关键对象，加载实体并正常保存。
- 批量操作显式写业务审计/操作审计，记录条件、旧状态摘要、新值、操作者和影响行数。
- 使用数据库 temporal table/CDC 等基础设施补充，但理解它们与业务审计语义不同。
- 对禁止绕过的实体写架构测试或代码扫描规则。

## 10. 当前字段审计为什么分两阶段

`CP6Context` 在保存前捕获变更，在第一次保存后写审计行，再执行第二次 base save。

原因是新增实体的数据库生成主键在第一次保存前可能还没有最终值：

```text
阶段一：保存前记录实体、操作、字段 diff、临时/旧键、租户
阶段二：业务行保存后重新取 Added 的真实键，创建审计行
```

它还跳过：

- 主键。
- TenantId。
- 元数据字段。
- `[AuditIgnore]`。
- 内建敏感字段拒名单。
- Modified 但没有真实字段差异的空变更。

### 10.1 两次 SaveChanges 的事务问题

代码在需要审计时开启事务，使业务行与审计行共同提交。评审时要验证同步和异步两个 override 都覆盖，递归调用用 `base.SaveChanges` 避免再次捕获审计行。

还要验证审计失败时业务行是否回滚。否则“业务成功但审计缺失”会破坏合规目标。

## 11. 权限：前端隐藏按钮不是授权

`v-permission` 根据权限 store 移除或保留 DOM，它只改善 UX。攻击者可以在 DevTools 直接调用 API。

真正的写权限必须由后端端点元数据和授权处理器执行，例如库存调整端点的 `[Authorize]` 与资源动作权限。

检查一个端点时至少问：

- 只要求登录，还是要求具体 action？
- action key 是否和菜单/角色配置一致？
- Controller 与 Service 是否可能被别的入口绕过？
- 401 与 403 是否稳定区分？
- 权限修改后缓存多久生效？

### 11.1 `loaded=false` 时保留按钮的取舍

当前权限指令在权限尚未加载时暂时保留元素，避免首屏误删。这是可用性选择，前提是后端强校验。如果按钮本身会展示敏感本地数据，加载前就应隐藏或显示 skeleton。

## 12. CSRF：为什么 Cookie 认证需要额外防线

浏览器会自动带 Cookie。恶意网站可诱导用户浏览器向 CP6 发写请求，所以服务端要求一个恶意站点拿不到的 CSRF token/header。

当前前端对非安全方法注入 CSRF 头，服务端中间件验证。要检查：

- 登录、refresh、SSO callback 等端点是否有必要豁免。
- 豁免是否精确到端点，而不是整个路径前缀。
- OPTIONS 预检是否被正确放行。
- token 轮换后前端是否更新。
- 错误是稳定的 403/错误码，还是模糊 500。

CSRF 与 CORS 不是一回事。CORS 控制浏览器是否让脚本读取/发出特定跨域请求，不应作为唯一 CSRF 防线。

## 13. 安全失败矩阵

| 场景 | 应拦截层 | 期望结果 |
|---|---|---|
| 无 Cookie 请求库存 | Authentication | 401 |
| 登录但无 adjust 权限 | Authorization | 403 |
| 租户 A 请求租户 B Id | Query filter/tenant predicate | 404 或受控拒绝，不泄露存在性 |
| 写请求无 CSRF header | CSRF middleware | 403 |
| DTO 注入别的 TenantId | Service/write guard | 400/403，绝不能写入 |
| Dapper 漏 TenantId | 测试/审查/DB 边界 | 测试失败，不得上线 |
| ExecuteUpdate 修改审计实体 | 架构规则/显式审计 | 有批量审计或禁止 |
| 后台任务无租户 scope | TenantScopeRunner/fail-closed | 明确失败，不落默认租户 |

## 14. 后台任务为什么要为每个租户开新 scope

`BackgroundService` 没有 HttpContext，也没有用户 claim。`TenantScopeRunner` 先读取活跃租户列表，再为每个租户创建新 DI scope，在解析业务服务/DbContext 前设置 `ITenantContext`。

顺序很重要：

```text
create scope
→ set CurrentTenantId
→ resolve/run body using same scope
→ dispose scope
```

若先解析 DbContext 再在另一个 scope 设置租户，两者不是同一 scoped 实例。若多个租户共用同一个 DbContext，还会出现 tracking 污染和非线程安全问题。

当前 runner 捕获单租户异常后记录并继续其他租户。这提高批任务可用性，但必须有失败指标和重试，否则某租户会长期被“跳过”。

## 15. 必做实验 A：跨租户读取

使用两个真实 tenant id 创建同业务编码的数据：

1. 在租户 A scope 插入 P001。
2. 在租户 B scope 插入 P001。
3. 分别查询，断言每边只看到自己的记录。
4. 验证复合唯一索引允许两个租户同码。
5. 使用 `IgnoreQueryFilters` 做管理员测试，确认必须显式授权且结果带租户标识。

测试不能只断言数量为 1，还要断言返回行的 TenantId。

## 16. 必做实验 B：写入注入

构造一个创建请求，故意在 JSON 中加入另一个 TenantId，即使 DTO 正常不声明该字段也观察模型绑定行为。然后检查：

- 字段是否被忽略。
- 映射工具是否会从扩展字段/通用字典映射。
- 服务端最终实体 TenantId 来自哪里。
- 数据库结果属于哪个租户。

再写一个直接 Attach stub 的内部测试，确认是否可能修改别租户 Id。安全测试需要攻击非常规路径。

## 17. 必做实验 C：审计绕过

对一个 `IAuditable` 实体分别执行：

1. 加载实体、修改属性、`SaveChangesAsync`。
2. `ExecuteUpdateAsync` 修改同一属性。
3. Dapper UPDATE。

比较 `Sys_FieldAuditLog`。预期只有第一条自然进入 ChangeTracker 审计。然后为批量路径设计显式审计并写测试。

## 18. 必做实验 D：CSRF 与权限

对同一写端点发送四个请求：

```text
无认证
有认证、无权限
有认证有权限、无 CSRF
有认证有权限、有 CSRF
```

记录状态码、错误码和响应体，确认顺序与信息泄露符合预期。若不同中间件顺序导致无认证请求先暴露 CSRF 错误，应评估是否需要调整。

## 19. 面试回答模板

> CP6 的租户隔离不是靠前端传 TenantId。认证后，TenantMiddleware 从可信 claim 建立 scoped tenant context；EF Core 对租户基类自动加全局查询过滤器，新实体保存时由 Context 盖 TenantId，唯一索引也按 TenantId 复合化。后端端点再做资源动作授权，Cookie 写请求使用 CSRF header。字段审计依赖 ChangeTracker 两阶段保存，所以 `ExecuteUpdate`、Dapper 和原生 SQL 是显式绕过边界，必须补租户条件与显式审计。后台没有 HttpContext，则用 TenantScopeRunner 为每个租户创建独立 scope。这个方案减少常规漏写，但不是绝对安全；`IgnoreQueryFilters`、Attach stub、显式错误 TenantId 和默认租户回退都需要负向测试和 fail-closed 规则。

## 20. 闭卷验收

1. 画六层安全防线，并说明每层不能防什么。
2. 解释 `cp6_authed` 为什么不是凭证。
3. 说明全局过滤器如何引用当前 DbContext 的租户。
4. 列出五条绕过 EF 隔离/审计的路径。
5. 写出普通创建 DTO 为什么不应包含 TenantId。
6. 解释字段审计为什么需要两阶段。
7. 设计跨租户读、写入注入、Dapper 漏条件、CSRF/权限四组负向测试。
8. 回答默认租户回退的好处、风险和收紧方案。
